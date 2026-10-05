using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>One scheduler per map. Clients render the host's records and never roll resources or timers.</summary>
public sealed class OreSpawnSystem : MonoBehaviour
{
    [System.Serializable]
    public sealed class Socket
    {
        public int id, mine, allowed;
        public string space;
        public Vector3 position, normal, approach;
        public bool wall, rare;
        public float depth, probability = 1f;
        public Quaternion Rotation => Quaternion.FromToRotation(Vector3.up, normal);
    }
    public OreSpawnConfig config;
    public List<Socket> sockets = new List<Socket>();
    public IReadOnlyDictionary<int, RockHealth> Active => active;
    public bool Ready { get; private set; }
    private readonly Dictionary<int, RockHealth> active = new Dictionary<int, RockHealth>();
    private readonly Dictionary<int, int> occupied = new Dictionary<int, int>();
    private readonly Dictionary<int, OrePopulation> records = new Dictionary<int, OrePopulation>();
    private struct Pending { public int mine, previous, resource; public float due; }
    private readonly List<Pending> pending = new List<Pending>();
    private readonly List<Vector3> players = new List<Vector3>();
    private System.Random random;
    private NetworkWorld world;
    private int nextId;
    private float nextPoll;
    private bool authority;
    private Transform generated;
    private bool clusterThisRoll;
    private void Awake() => generated = transform.parent.Find("Mountain/Generated");

    private void Start()
    {
        // NGO loads the scene before spawning NetworkWorld. Never run a local roll during that gap.
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;
        StartAuthority(null);
    }

    public void StartAuthority(NetworkWorld networkWorld)
    {
        if (Ready) return;
        world = networkWorld;
        authority = true;
        random = new System.Random(System.Guid.NewGuid().GetHashCode());
        Ready = true;
        CachePlayers();
        for (int mine = 1; mine <= config.mines.Length; mine++)
        {
            var rule = config.mines[mine - 1];
            int[] counts = new int[4];
            float total = 0f;
            foreach (float weight in rule.weights) total += weight;
            for (int ore = 2; ore < 4; ore++)
            {
                float expected = rule.target * rule.weights[ore] / total;
                if (mine == 1 && ore == 2)
                { for (int roll = 0; roll < rule.target; roll++) if (random.NextDouble() < rule.weights[ore] / total) counts[ore]++; }
                else counts[ore] = Mathf.FloorToInt(expected) + (random.NextDouble() < expected % 1f ? 1 : 0);
                counts[ore] = Mathf.Min(counts[ore], rule.caps[ore]);
            }
            int common = Mathf.Max(0, rule.target - counts[2] - counts[3]);
            float copper = common * rule.weights[0] / (rule.weights[0] + rule.weights[1]);
            counts[0] = Mathf.FloorToInt(copper) + (random.NextDouble() < copper % 1f ? 1 : 0);
            counts[1] = common - counts[0];
            // Spawn constrained wall resources first so rare pockets cannot consume all Iron-compatible sockets.
            foreach (int ore in new[] { 1, 3, 2, 0 })
                for (int count = 0; count < counts[ore]; count++)
                    if (!TrySpawn(mine, -1, ore)) pending.Add(new Pending { mine = mine, previous = -1, resource = ore, due = Time.time + config.pollSeconds });
        }
    }

    public void StartClient(NetworkWorld networkWorld) { world = networkWorld; authority = false; Ready = true; }

    private void Update()
    {
        if (!Ready || !authority || Time.time < nextPoll) return;
        nextPoll = Time.time + config.pollSeconds;
        CachePlayers();
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            Pending replacement = pending[i];
            if (Time.time < replacement.due) continue;
            if (Count(replacement.mine) >= config.mines[replacement.mine - 1].target || TrySpawn(replacement.mine, replacement.previous, replacement.resource))
                pending.RemoveAt(i);
        }
    }

    public int Count(int mine, int resource = -1)
    {
        int count = 0;
        foreach (OrePopulation record in records.Values)
            if (sockets[record.socket].mine == mine && (resource < 0 || record.resource == resource)) count++;
        return count;
    }

    public bool IsOccupied(int socket) => occupied.ContainsKey(socket);
    public bool TryRecord(int id, out OrePopulation record) => records.TryGetValue(id, out record);

    private void CachePlayers()
    {
        players.Clear();
        if (world != null)
        {
            foreach (NetworkClient client in world.NetworkManager.ConnectedClientsList)
                if (client.PlayerObject != null) players.Add(client.PlayerObject.transform.position);
        }
        // Includes the host's local controller before its network avatar finishes spawning.
        GameObject local = GameObject.FindWithTag("Player");
        if (local != null) players.Add(local.transform.position);
    }

    private bool Clear(Socket socket)
    {
        if (occupied.ContainsKey(socket.id)) return false;
        foreach (Vector3 player in players)
            if ((player - socket.position).sqrMagnitude < config.playerDistance * config.playerDistance) return false;
        foreach (OrePopulation record in records.Values)
            if ((sockets[record.socket].position - socket.position).sqrMagnitude < config.nodeSpacing * config.nodeSpacing) return false;
        // A newly dropped item, moved prop or player-built obstacle must not be covered by a replacement.
        Vector3 center = socket.position + socket.normal * 0.45f;
        foreach (Collider collider in Physics.OverlapSphere(center, 0.52f, ~0, QueryTriggerInteraction.Ignore))
            if (!(collider is MeshCollider && collider.transform.IsChildOf(generated))) return false;
        return true;
    }

    private bool TrySpawn(int mine, int previous, int preferred = -1)
    {
        var rule = config.mines[mine - 1];
        if (Count(mine) >= rule.target) return false;
        // Roll a resource before choosing its socket: having more wall sockets cannot inflate Iron's share.
        var choices = new List<Socket>[4];
        var valid = new List<Socket>();
        foreach (Socket socket in sockets)
            if (socket.mine == mine && socket.id != previous && Clear(socket)) valid.Add(socket);
        float total = 0f;
        for (int ore = 0; ore < 4; ore++)
        {
            choices[ore] = new List<Socket>();
            if ((preferred >= 0 && ore != preferred) || rule.weights[ore] <= 0 || Count(mine, ore) >= rule.caps[ore]) continue;
            foreach (Socket socket in valid)
                if ((socket.allowed & (1 << ore)) != 0 && socket.depth >= rule.minimumDepth[ore] && (ore < 2 || socket.rare)) choices[ore].Add(socket);
            if (choices[ore].Count > 0) total += rule.weights[ore];
        }
        if (total <= 0) return false; // Defer instead of popping a node beside a player or reusing the mined socket.
        float roll = (float)random.NextDouble() * total;
        int selected = 0;
        for (; selected < 4; selected++)
        {
            if (choices[selected].Count == 0) continue;
            roll -= rule.weights[selected];
            if (roll < 0) break;
        }
        if (selected >= 4) return false;
        clusterThisRoll = random.NextDouble() < config.commonClusterChance;
        float socketTotal = 0f;
        foreach (Socket socket in choices[selected]) socketTotal += Weight(socket, selected);
        float socketRoll = (float)random.NextDouble() * socketTotal;
        Socket chosen = choices[selected][choices[selected].Count - 1];
        foreach (Socket socket in choices[selected])
        {
            socketRoll -= Weight(socket, selected);
            if (socketRoll < 0) { chosen = socket; break; }
        }
        var state = new OrePopulation { id = ++nextId, socket = chosen.id, resource = selected, health = config.nodePrefab.GetComponent<RockHealth>().MaxHealth };
        ApplyRecord(state, false);
        if (world != null) world.AddOre(state);
        return true;
    }

    private float Weight(Socket socket, int resource)
    {
        float weight = socket.probability;
        if (resource == 0) weight *= 1f + config.copperEarlyPreference * (1f - socket.depth);
        if (resource == 1) weight *= 1f + config.ironDeepPreference * socket.depth;
        if (resource >= 2) weight *= 1f + socket.depth;
        if (resource < 2 && clusterThisRoll)
            foreach (OrePopulation record in records.Values)
                if (record.resource == resource && (sockets[record.socket].position - socket.position).sqrMagnitude < config.clusterRadius * config.clusterRadius)
                { weight *= 2f; break; }
        return weight;
    }

    public void ApplyRecord(OrePopulation state, bool flash)
    {
        if (active.TryGetValue(state.id, out RockHealth existing) && existing != null)
        {
            records[state.id] = state;
            existing.ShowNetworkHit(state.health, flash);
            return;
        }
        Socket socket = sockets[state.socket];
        GameObject node = Instantiate(config.nodePrefab, socket.position - socket.normal * (state.resource == 3 ? 0.15f : 0.04f), socket.Rotation, transform);
        node.name = $"Ore {state.id} ({config.resources[state.resource].DisplayName})";
        RockHealth rock = node.GetComponent<RockHealth>();
        rock.InitializeOre(config.resources[state.resource]);
        rock.ShowNetworkHit(state.health, false);
        active.Add(state.id, rock);
        occupied.Add(state.socket, state.id);
        records.Add(state.id, state);
        if (world != null) world.BindOre(state.id, rock);
        if (authority) rock.DamageApplied += OnDamage;
    }

    private void OnDamage(RockHealth rock)
    {
        int id = 0;
        foreach (var entry in active) if (entry.Value == rock) { id = entry.Key; break; }
        if (id == 0 || !records.TryGetValue(id, out OrePopulation state)) return;
        if (rock.CurrentHealth > 0)
        {
            state.health = rock.CurrentHealth;
            records[id] = state;
            if (world != null) world.ChangeOre(state);
            return;
        }
        // Refresh the resource as well as the location, without using a cheap ore's timer for a new jackpot.
        int mine = sockets[state.socket].mine;
        RemoveRecord(id, false);
        if (world != null) world.RemoveOre(id);
        var rule = config.mines[mine - 1];
        float total = 0f;
        for (int ore = 0; ore < 4; ore++) if (ReservedCount(mine, ore) < rule.caps[ore]) total += rule.weights[ore];
        float roll = (float)random.NextDouble() * total;
        int replacement = state.resource;
        for (int ore = 0; ore < 4; ore++)
        {
            if (ReservedCount(mine, ore) >= rule.caps[ore]) continue;
            roll -= rule.weights[ore];
            if (roll < 0) { replacement = ore; break; }
        }
        pending.Add(new Pending { mine = mine, previous = state.socket, resource = replacement,
            due = Time.time + Mathf.Max(config.respawnSeconds[state.resource], config.respawnSeconds[replacement]) });
    }

    private int ReservedCount(int mine, int resource)
    {
        int count = Count(mine, resource);
        foreach (Pending replacement in pending) if (replacement.mine == mine && replacement.resource == resource) count++;
        return count;
    }

    public void RemoveRecord(int id, bool breakLocally)
    {
        if (!records.TryGetValue(id, out OrePopulation state)) return;
        if (active.TryGetValue(id, out RockHealth rock) && rock != null)
        {
            rock.DamageApplied -= OnDamage;
            if (breakLocally) rock.BreakFromNetwork();
        }
        if (world != null) world.UnbindOre(id);
        active.Remove(id);
        occupied.Remove(state.socket);
        records.Remove(id);
    }

    private void OnDrawGizmosSelected()
    {
        foreach (Socket socket in sockets)
        {
            Gizmos.color = IsOccupied(socket.id) ? Color.red : socket.rare ? Color.yellow : socket.wall ? Color.cyan : Color.green;
            Gizmos.DrawWireSphere(socket.position, 0.45f);
            Gizmos.DrawLine(socket.position, socket.position + socket.normal);
        }
    }
}

/// <summary>A persistent authoritative spawn record. Id changes on every replacement, rejecting stale hit RPCs.</summary>
public struct OrePopulation : INetworkSerializable, System.IEquatable<OrePopulation>
{
    public int id, socket, resource, health;
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref id); serializer.SerializeValue(ref socket);
        serializer.SerializeValue(ref resource); serializer.SerializeValue(ref health);
    }
    public bool Equals(OrePopulation other) => id == other.id && socket == other.socket && resource == other.resource && health == other.health;
}
