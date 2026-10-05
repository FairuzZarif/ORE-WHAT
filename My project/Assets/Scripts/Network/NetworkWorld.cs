using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The shared world of a multiplayer session: rocks and items. One per session, spawned by the host
/// when the map has loaded (NetworkSessionManager does it). The host is the authority:
///
///   Rocks   a player's hit is sent to the host, which applies it once (two players can mine the same
///           rock; each hit counts once) and tells everyone; a broken rock goes into BrokenRocks, so it is
///           also gone for players who join later. DamagedRocks persists partial durability for late joins.
///           Generated ore uses a persistent OrePopulation list (generation id, socket, resource, durability).
///           Clients render ordinary local rocks from those records; only the server rolls or replaces them.
///           Static prototype scenes retain hierarchy ids for compatibility.
///   Items   only the host spawns shared items (ore drops, drops, throws: everything goes through ItemDrops).
///           Picking one up asks the host, which hands it to the first player who asks and removes it.
///           Carrying asks the host for ownership, so the carrier moves it with its own physics.
///
/// The inventory UI stays local; NetworkPlayerEconomy records host-approved ownership for pickups, drops and sales.
/// </summary>
public class NetworkWorld : NetworkBehaviour, IWorldNetwork
{
    [Tooltip("Every item that can exist in the world. Items are sent over the network as an index into this list " +
             "(Ore What > Multiplayer > Set Up Multiplayer fills it from Assets/Items).")]
    [SerializeField] private ItemData[] items = new ItemData[0];
    [Tooltip("How far (metres) a player may be from an item it picks up or carries. Generous, for lag.")]
    [SerializeField, Min(1f)] private float maxReach = 6f;
    [Tooltip("Largest damage one hit may do to a rock (anything bigger is ignored as invalid).")]
    [SerializeField, Min(1)] private int maxRockDamage = 20;

    /// <summary>The running session's world, or null.</summary>
    public static NetworkWorld Instance { get; private set; }

    /// <summary>Rocks broken so far (their ids). Late joiners break these when they arrive.</summary>
    private readonly NetworkList<int> brokenRocks = new NetworkList<int>();

    public struct RockDurability : INetworkSerializable, System.IEquatable<RockDurability>
    {
        public int id, health;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        { serializer.SerializeValue(ref id); serializer.SerializeValue(ref health); }
        public bool Equals(RockDurability other) => id == other.id && health == other.health;
    }
    // Persistent partial damage, so a late join receives the same state as existing clients.
    private readonly NetworkList<RockDurability> damagedRocks = new NetworkList<RockDurability>();
    private readonly NetworkList<OrePopulation> orePopulation = new NetworkList<OrePopulation>();
    private readonly Dictionary<int, RockHealth> spawnedOres = new Dictionary<int, RockHealth>();
    private readonly Dictionary<RockHealth, int> spawnedIds = new Dictionary<RockHealth, int>();
    private OreSpawnSystem oreSpawns;

    public void BindOre(int id, RockHealth rock) { spawnedOres[id] = rock; spawnedIds[rock] = id; }
    public void UnbindOre(int id)
    {
        if (spawnedOres.TryGetValue(id, out RockHealth rock)) spawnedIds.Remove(rock);
        spawnedOres.Remove(id);
    }
    public void AddOre(OrePopulation state) { if (IsServer) orePopulation.Add(state); }
    public void ChangeOre(OrePopulation state)
    {
        if (!IsServer) return;
        for (int i = 0; i < orePopulation.Count; i++) if (orePopulation[i].id == state.id) { orePopulation[i] = state; return; }
    }
    public void RemoveOre(int id)
    {
        if (!IsServer) return;
        for (int i = 0; i < orePopulation.Count; i++) if (orePopulation[i].id == id) { orePopulation.RemoveAt(i); return; }
    }
    private void OnOreChanged(NetworkListEvent<OrePopulation> change)
    {
        if (IsServer || oreSpawns == null) return;
        if (change.Type == NetworkListEvent<OrePopulation>.EventType.Add || change.Type == NetworkListEvent<OrePopulation>.EventType.Value)
            oreSpawns.ApplyRecord(change.Value, change.Type == NetworkListEvent<OrePopulation>.EventType.Value);
        else if (change.Type == NetworkListEvent<OrePopulation>.EventType.Remove || change.Type == NetworkListEvent<OrePopulation>.EventType.RemoveAt)
            oreSpawns.RemoveRecord(change.Value.id, true);
    }

    private readonly Dictionary<int, RockHealth> rocksById = new Dictionary<int, RockHealth>();
    private readonly Dictionary<RockHealth, int> idsByRock = new Dictionary<RockHealth, int>();

    public ItemData ItemAt(int index) => index >= 0 && index < items.Length ? items[index] : null;
    public int IndexOf(ItemData item) => System.Array.IndexOf(items, item);

    // ---------------------------------------------------------------- lifetime

    public override void OnNetworkSpawn()
    {
        Instance = this;
        WorldNetwork.Current = this;
        IndexRocks();
        brokenRocks.OnListChanged += OnBrokenRocksChanged;
        damagedRocks.OnListChanged += OnDurabilityChanged;
        oreSpawns = FindAnyObjectByType<OreSpawnSystem>();
        orePopulation.OnListChanged += OnOreChanged;
        if (oreSpawns != null)
        {
            if (IsServer) oreSpawns.StartAuthority(this);
            else { oreSpawns.StartClient(this); foreach (OrePopulation state in orePopulation) oreSpawns.ApplyRecord(state, false); }
        }
        if (!IsServer)
        {
            foreach (RockDurability state in damagedRocks) ShowDurability(state, false);
            foreach (int id in brokenRocks) BreakLocally(id);
        }
    }

    public override void OnNetworkDespawn()
    {
        brokenRocks.OnListChanged -= OnBrokenRocksChanged;
        damagedRocks.OnListChanged -= OnDurabilityChanged;
        orePopulation.OnListChanged -= OnOreChanged;
        if (Instance == this) Instance = null;
        if (ReferenceEquals(WorldNetwork.Current, this)) WorldNetwork.Current = null;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (ReferenceEquals(WorldNetwork.Current, this)) WorldNetwork.Current = null;
        base.OnDestroy();
    }

    // ---------------------------------------------------------------- rocks

    /// <summary>Gives every rock in the loaded scenes an id from its hierarchy path (same on every machine).</summary>
    private void IndexRocks()
    {
        rocksById.Clear();
        idsByRock.Clear();
        foreach (RockHealth rock in FindObjectsByType<RockHealth>(FindObjectsInactive.Include))
        {
            int id = StableId(rock.transform);
            if (rocksById.ContainsKey(id)) { Debug.LogWarning($"[Ore What] Two rocks share network id {id}: {rock.name}. One won't sync."); continue; }
            rocksById.Add(id, rock);
            idsByRock.Add(rock, id);
        }
    }

    /// <summary>FNV-1a hash of "scene/root#index/child#index/..." (string.GetHashCode isn't stable between runs).</summary>
    private static int StableId(Transform t)
    {
        var path = new System.Text.StringBuilder();
        for (Transform p = t; p != null; p = p.parent)
            path.Insert(0, "/" + p.name + "#" + p.GetSiblingIndex());
        path.Insert(0, t.gameObject.scene.name);
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in path.ToString()) { hash ^= c; hash *= 16777619; }
            return (int)hash;
        }
    }

    public void RequestRockHit(RockHealth rock, int damage)
    {
        if (rock != null && spawnedIds.TryGetValue(rock, out int spawnId)) { SpawnedOreHitRpc(spawnId, damage); return; }
        if (rock != null && idsByRock.TryGetValue(rock, out int id)) RockHitRpc(id, damage);
        // Runtime-spawned nodes must be indexed by the world; never create unsynchronized host-only damage.
    }

    [Rpc(SendTo.Server)]
    private void SpawnedOreHitRpc(int id, int damage, RpcParams rpcParams = default)
    {
        if (damage < 1 || damage > maxRockDamage || !spawnedOres.TryGetValue(id, out RockHealth rock)
            || rock == null || rock.CurrentHealth <= 0) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client)
            || client.PlayerObject == null) return;
        var avatar = client.PlayerObject.GetComponent<NetworkPlayerAvatar>();
        ItemData held = avatar != null ? ItemAt(avatar.HeldItemIndex) : null;
        var equipment = FindAnyObjectByType<PlayerEquipment>();
        var tool = equipment != null && held != null && held.CanMine ? equipment.ControllerFor(held) as MiningToolController : null;
        if (tool == null || damage != tool.DamagePerHit) return;
        // Reach is checked against the synchronized avatar, with generous latency allowance.
        if ((client.PlayerObject.transform.position - rock.transform.position).sqrMagnitude > maxReach * maxReach) return;
        rock.ApplyMiningDamage(damage);
    }

    [Rpc(SendTo.Server)]
    private void RockHitRpc(int id, int damage, RpcParams rpcParams = default)
    {
        if (damage < 1 || damage > maxRockDamage) return;
        if (!rocksById.TryGetValue(id, out RockHealth rock) || rock == null || rock.CurrentHealth <= 0) return;
        // The host checks the sender's equipped capability, rather than trusting a claimed damage type.
        if (!NetworkManager.ConnectedClients.TryGetValue(rpcParams.Receive.SenderClientId, out NetworkClient client)
            || client.PlayerObject == null) return;
        var avatar = client.PlayerObject.GetComponent<NetworkPlayerAvatar>();
        ItemData held = avatar != null ? ItemAt(avatar.HeldItemIndex) : null;
        if (held == null || !held.CanMine) return;
        var equipment = FindAnyObjectByType<PlayerEquipment>();
        var tool = equipment != null ? equipment.ControllerFor(held) as MiningToolController : null;
        if (tool == null || damage != tool.DamagePerHit) return;

        int index = -1;
        for (int i = 0; i < damagedRocks.Count; i++) if (damagedRocks[i].id == id) { index = i; break; }
        if (rock.ApplyMiningDamage(damage))
        {
            if (index >= 0) damagedRocks.RemoveAt(index);
            brokenRocks.Add(id);
        }
        else
        {
            var state = new RockDurability { id = id, health = rock.CurrentHealth };
            if (index < 0) damagedRocks.Add(state);
            else damagedRocks[index] = state;
        }
    }

    private void OnDurabilityChanged(NetworkListEvent<RockDurability> change)
    {
        if (!IsServer && (change.Type == NetworkListEvent<RockDurability>.EventType.Add
            || change.Type == NetworkListEvent<RockDurability>.EventType.Value)) ShowDurability(change.Value, true);
    }

    private void ShowDurability(RockDurability state, bool flash)
    {
        if (rocksById.TryGetValue(state.id, out RockHealth rock) && rock != null) rock.ShowNetworkHit(state.health, flash);
    }

    private void OnBrokenRocksChanged(NetworkListEvent<int> change)
    {
        if (!IsServer && change.Type == NetworkListEvent<int>.EventType.Add) BreakLocally(change.Value);
    }

    private void BreakLocally(int id)
    {
        if (rocksById.TryGetValue(id, out RockHealth rock) && rock != null) rock.BreakFromNetwork();
    }

    // ---------------------------------------------------------------- items

    public DroppedItem SpawnItem(ItemData item, int amount, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
    {
        if (IsServer) return SpawnShared(item, amount, position, rotation, velocity, angularVelocity);
        // Mining/world rewards are host-only. Inventory drops use the player's account and remove owned items first.
        return null;
    }

    /// <summary>Host: builds the item and spawns it on every machine.</summary>
    private DroppedItem SpawnShared(ItemData item, int amount, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
    {
        DroppedItem dropped = ItemDrops.SpawnLocal(item, amount, position, rotation, velocity, angularVelocity);
        if (dropped == null) return null;
        if (dropped.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(destroyWithScene: true);
            dropped.Body.linearVelocity = velocity; // spawning resets nothing, but be sure the toss survives
            dropped.Body.angularVelocity = angularVelocity;
        }
        else Debug.LogWarning($"[Ore What] {item.name}'s world prefab has no NetworkObject: only the host sees it (run Ore What > Multiplayer > Set Up Multiplayer).");
        return dropped;
    }

    public bool HasControl(DroppedItem item)
    {
        if (item == null || !item.TryGetComponent(out NetworkItem net) || !net.IsSpawned) return true;
        return net.IsOwner;
    }

    public bool RequestPickup(DroppedItem item, int maxAmount)
    {
        if (item == null || !item.TryGetComponent(out NetworkItem net) || !net.IsSpawned) return false;
        PickupRpc(net.NetworkObject, maxAmount);
        return true;
    }

    [Rpc(SendTo.Server)]
    private void PickupRpc(NetworkObjectReference target, int maxAmount, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (maxAmount < 1 || !target.TryGet(out NetworkObject netObj) || !netObj.TryGetComponent(out NetworkItem net)) return;
        if (net.Held.Value && netObj.OwnerClientId != sender) { MessageRpc("Someone else is carrying that", RpcTarget.Single(sender, RpcTargetUse.Temp)); return; }
        if (!InReach(sender, netObj.transform.position)) return;

        DroppedItem dropped = net.Dropped;
        int index = IndexOf(dropped.Item);
        if (index < 0) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(sender, out var player) || player.PlayerObject == null ||
            !player.PlayerObject.TryGetComponent<NetworkPlayerEconomy>(out var account)) return;
        int granted = account.StorePickup(dropped.Item, Mathf.Min(dropped.Amount, maxAmount));
        if (granted <= 0) { MessageRpc("Inventory Full", RpcTarget.Single(sender, RpcTargetUse.Temp)); return; }
        ItemData data = dropped.Item;
        if (granted >= dropped.Amount) netObj.Despawn(true); // gone for everyone: nobody else can collect it
        else { net.Amount.Value = dropped.Amount - granted; dropped.SetAmount(net.Amount.Value); }
        account.ConfirmPickup(data, granted);
    }

    public bool RequestCarry(DroppedItem item, OreCarryController carrier)
    {
        if (item == null || !item.TryGetComponent(out NetworkItem net) || !net.IsSpawned) return false;
        net.GainedControl = () => { if (carrier != null && item != null) carrier.TryCarry(item); };
        CarryRpc(net.NetworkObject);
        return true;
    }

    [Rpc(SendTo.Server)]
    private void CarryRpc(NetworkObjectReference target, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!target.TryGet(out NetworkObject netObj) || !netObj.TryGetComponent(out NetworkItem net)) return;
        if (net.Held.Value && netObj.OwnerClientId != sender) { MessageRpc("Someone else is carrying that", RpcTarget.Single(sender, RpcTargetUse.Temp)); return; }
        if (!InReach(sender, netObj.transform.position)) return;
        if (netObj.OwnerClientId != sender) netObj.ChangeOwnership(sender); // the new owner starts carrying (NetworkItem.GainedControl)
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void MessageRpc(string text, RpcParams rpcParams)
    {
        var interactor = FindAnyObjectByType<ItemPickupInteractor>();
        if (interactor != null) interactor.ShowMessage(text);
    }

    /// <summary>Host check: is that player's character close enough to the point? (No character yet = allowed.)</summary>
    private bool InReach(ulong clientId, Vector3 point)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) || client.PlayerObject == null) return true;
        return (client.PlayerObject.transform.position - point).sqrMagnitude <= maxReach * maxReach;
    }
}
