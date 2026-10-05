using System;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Opt-in development acceptance run over real host/client processes. No gameplay behaviour without -oretest.</summary>
public sealed class OreDistributionTest : MonoBehaviour
{
    public string role = "sp", output;
    private int failures;
    private OreSpawnSystem system;
    private PlayerMovement player;
    private ItemData pickaxe;
    private int originalSlot;
    private Vector3 originalPosition;
    private bool moveEnabled, lookEnabled;
    private OreSpawnConfig originalConfig;
    private float deadline;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Debug.isDebugBuild && !Application.isEditor) return;
        string[] args = Environment.GetCommandLineArgs();
        int flag = Array.IndexOf(args, "-oretest");
        if (flag < 0 || flag + 1 >= args.Length) return;
        var go = new GameObject("OreDistributionTest"); DontDestroyOnLoad(go);
        var test = go.AddComponent<OreDistributionTest>(); test.role = args[flag + 1];
        int folder = Array.IndexOf(args, "-orereview");
        test.output = folder >= 0 && folder + 1 < args.Length ? args[folder + 1] : Path.GetFullPath("../Builds/OreDistributionReview");
    }

    private void Check(bool pass, string message)
    {
        string line = "[ORETEST] " + role + " " + (pass ? "PASS " : "FAIL ") + message;
        if (!pass) failures++;
        Debug.Log(line); File.AppendAllText(Path.Combine(output, role + "-results.txt"), line + Environment.NewLine);
    }
    private void Signal(string marker) => File.WriteAllText(Path.Combine(output, role + "-" + marker + ".flag"), "ready");
    private IEnumerator Wait(Func<bool> condition, string message, float seconds = 90)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        Check(condition(), message);
    }
    private IEnumerator Marker(string other, string marker) => Wait(() => File.Exists(Path.Combine(output, other + "-" + marker + ".flag")), other + " " + marker);

    private IEnumerator Start()
    {
        if (string.IsNullOrEmpty(output)) output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/OreDistributionReview"));
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, role + "-results.txt"), "");
        deadline = Time.realtimeSinceStartup + 240f;
        Application.targetFrameRate = 30;
        if (role != "sp")
        {
            yield return Wait(() => GameObject.Find("HostButton") != null && GameObject.Find("JoinCodeField") != null, "menu ready");
            if (role == "client") GameObject.Find("JoinCodeField").GetComponent<InputField>().text = "127.0.0.1";
            GameObject.Find(role == "host" ? "HostButton" : "JoinButton").GetComponent<Button>().onClick.Invoke();
        }
        yield return Wait(() => (system = FindAnyObjectByType<OreSpawnSystem>()) != null && system.Ready && system.Active.Count == 94, "authoritative population ready");
        player = FindAnyObjectByType<PlayerMovement>();
        originalPosition = player.transform.position; moveEnabled = player.enabled;
        lookEnabled = player.GetComponent<PlayerLook>().enabled;
        originalSlot = player.GetComponent<PlayerInventory>().SelectedSlot;
        player.enabled = false; player.GetComponent<PlayerLook>().enabled = false;
        pickaxe = Resources.FindObjectsOfTypeAll<ItemData>().First(i => i.name == "Pickaxe");
        var inventory = player.GetComponent<PlayerInventory>();
        if (inventory.Count(pickaxe) == 0) inventory.AddItem(pickaxe, 1);
        for (int i = 0; i < inventory.SlotCount; i++) if (inventory.Slots[i].item == pickaxe) { inventory.SelectSlot(i); break; }
        yield return new WaitForSeconds(1f);
        originalConfig = system.config;
        system.config = Instantiate(originalConfig); // Test timers never edit the economy asset.
        system.config.respawnSeconds = new[] { 3f, 3f, 4f, 5f }; system.config.pollSeconds = 0.25f;
        ValidatePopulation();
        if (role == "sp")
        {
        int proximityId = Select(0); system.TryRecord(proximityId, out var proximityState);
        system.config.playerDistance = 10000f;
        yield return Mine(proximityId); Move(originalPosition);
        yield return new WaitForSeconds(7f);
        Check(system.Active.Count == 93, "all nearby sockets defer respawn without exceeding target");
        system.config.playerDistance = originalConfig.playerDistance;
        yield return Wait(() => system.Active.Count == 94, "deferred population resumes when distance clears", 25f);
        Check(!system.IsOccupied(proximityState.socket), "replacement selects a different socket");
        }
        if (role == "sp") yield return SinglePlayer();
        else if (role == "host") yield return Host();
        else yield return Client();
        Move(originalPosition); inventory.SelectSlot(originalSlot);
        player.enabled = moveEnabled; player.GetComponent<PlayerLook>().enabled = lookEnabled;
        Destroy(system.config); system.config = originalConfig;
        Check(failures == 0, "DONE failures=" + failures);
        Signal("done");
        if (role != "sp") { yield return new WaitForSeconds(1f); Application.Quit(failures == 0 ? 0 : 1); }
        Destroy(gameObject);
    }
    private void Update()
    {
        if (deadline > 0 && Time.realtimeSinceStartup > deadline) { Debug.LogError("[ORETEST] test timeout"); if (!Application.isEditor) Application.Quit(2); deadline = 0; }
    }
    private void Move(Vector3 position)
    {
        var controller = player.GetComponent<CharacterController>(); bool on = controller.enabled;
        controller.enabled = false; player.transform.position = position; controller.enabled = on; Physics.SyncTransforms();
    }
    private int Drops(ItemData item) => FindObjectsByType<DroppedItem>().Where(i => i.Item == item).Sum(i => i.Amount);
    private string Layout() => string.Join("\n", system.Active.Keys.OrderBy(id => id).Select(id =>
    {
        system.TryRecord(id, out OrePopulation state);
        return $"{id},{state.socket},{state.resource},{state.health}";
    }));
    private void ValidatePopulation()
    {
        Check(system.Active.Count == system.Active.Keys.Distinct().Count(), "unique generation ids");
        Check(system.Active.Keys.Select(id => { system.TryRecord(id, out var s); return s.socket; }).Distinct().Count() == system.Active.Count, "unique occupied sockets");
        for (int mine = 1; mine <= 4; mine++)
        {
            Check(system.Count(mine) <= system.config.mines[mine - 1].target, "mine " + mine + " target/caps total=" + system.Count(mine));
            for (int ore = 0; ore < 4; ore++) Check(system.Count(mine, ore) <= system.config.mines[mine - 1].caps[ore], $"mine {mine} resource {ore} count={system.Count(mine, ore)}");
        }
        Check(system.Count(1, 3) == 0 && system.Count(2, 3) == 0, "no Crystal in first two mines");
        Check(system.Active.All(entry => { system.TryRecord(entry.Key, out var state); var socket = system.sockets[state.socket]; return (socket.allowed & (1 << state.resource)) != 0 && socket.depth >= system.config.mines[socket.mine - 1].minimumDepth[state.resource]; }), "every active resource obeys socket/depth restrictions");
        Check(FindObjectsByType<RockHealth>().Length == system.Active.Count, "all mineable nodes belong to population");
    }
    private int Select(int resource) => system.Active.Keys.First(id => { system.TryRecord(id, out var s); return s.resource == resource; });
    private IEnumerator Mine(int id)
    {
        if (!system.Active.TryGetValue(id, out RockHealth rock)) { Check(false, "target exists " + id); yield break; }
        system.TryRecord(id, out OrePopulation state);
        Move(system.sockets[state.socket].approach - Vector3.up * 1.05f);
        yield return new WaitForSeconds(0.8f); // Avatar position + equipped capability reach the server.
        int before = Drops(rock.OreItem); ItemData item = rock.OreItem;
        var priorDrops = new System.Collections.Generic.HashSet<DroppedItem>(FindObjectsByType<DroppedItem>());
        for (int hit = 0; hit < 5 && rock != null; hit++)
        {
            int health = rock.CurrentHealth;
            rock.TakeMiningHit(1, pickaxe);
            yield return new WaitForSeconds(0.3f);
            if (rock != null) Check(rock.CurrentHealth == health - 1, "authoritative health decrement " + id);
        }
        Check(!system.Active.ContainsKey(id), "depleted generation removed " + id);
        Check(!system.IsOccupied(state.socket), "mined socket freed " + state.socket);
        yield return new WaitForSeconds(0.4f);
        Check(Drops(item) == before + 3, "one shared drop batch " + id);
        Transform generated = system.transform.parent.Find("Mountain/Generated");
        var pieces = FindObjectsByType<DroppedItem>().Where(piece => piece.Item == item && !priorDrops.Contains(piece)).ToArray();
        Check(pieces.Length == 3 && pieces.All(piece => !Physics.OverlapSphere(piece.transform.position, 0.03f, ~0, QueryTriggerInteraction.Ignore)
            .Any(collider => collider.transform.IsChildOf(generated))), "drop centers clear cave surface " + id);
    }
    private IEnumerator SinglePlayer()
    {
        string before = Layout();
        int[] ids = new[] { Select(0), Select(1), Select(2), Select(3) };
        foreach (int id in ids) yield return Mine(id);
        Move(originalPosition);
        yield return Wait(() => system.Active.Count == 94, "population replenished at short timers", 25f);
        Check(before != Layout(), "layout changes after replacements");
        foreach (int id in ids) Check(!system.Active.ContainsKey(id), "old generation never resurrected " + id);
        ValidatePopulation();
    }
    private IEnumerator Host()
    {
        // Deplete one node and partially mine a second before the first client joins.
        yield return Mine(Select(0)); Move(originalPosition);
        yield return Wait(() => system.Active.Count == 94, "prejoin replacement ready", 25f);
        int partial = Select(1); system.TryRecord(partial, out var state);
        Move(system.sockets[state.socket].approach - Vector3.up * 1.05f); yield return new WaitForSeconds(0.8f);
        system.Active[partial].TakeMiningHit(1, pickaxe); yield return new WaitForSeconds(0.4f);
        Check(system.Active[partial].CurrentHealth == 4, "prejoin partial durability persisted");
        Move(originalPosition);
        File.WriteAllText(Path.Combine(output, "expected-layout.txt"), Layout()); Signal("ready");
        yield return Marker("client", "joined");
        int copper = Select(0); File.WriteAllText(Path.Combine(output, "host-target.txt"), copper.ToString()); Signal("copper-start");
        yield return Mine(copper); Move(originalPosition); Signal("copper-mined");
        yield return Marker("client", "copper-seen");
        yield return Marker("client", "iron-mined");
        int iron = int.Parse(File.ReadAllText(Path.Combine(output, "client-target.txt")));
        Check(!system.Active.ContainsKey(iron), "host sees client's Iron depletion");
        yield return Wait(() => system.Active.Count == 94, "shared population replenished", 25f);
        ValidatePopulation();
        File.WriteAllText(Path.Combine(output, "final-layout.txt"), Layout()); Signal("final");
        yield return Marker("client", "done");
    }
    private IEnumerator Client()
    {
        Check(File.ReadAllText(Path.Combine(output, "expected-layout.txt")) == Layout(), "late join receives current sockets/resources/generations/partial health");
        Signal("joined"); yield return Marker("host", "copper-start");
        int copper = int.Parse(File.ReadAllText(Path.Combine(output, "host-target.txt")));
        bool sawDamage = false;
        float until = Time.realtimeSinceStartup + 30f;
        while (!File.Exists(Path.Combine(output, "host-copper-mined.flag")) && Time.realtimeSinceStartup < until)
        {
            if (system.Active.TryGetValue(copper, out RockHealth observed) && observed.CurrentHealth < observed.MaxHealth)
            {
                sawDamage = true;
                Check(observed.GetComponent<OreNodeVisual>().Stage == OreNodeVisualView.StageFor(observed.CurrentHealth, observed.MaxHealth), "client cracks match host durability");
            }
            yield return new WaitForSeconds(0.2f);
        }
        Check(sawDamage, "client observes host's partial mining state");
        yield return Wait(() => !system.Active.ContainsKey(copper), "client sees host Copper depletion");
        Signal("copper-seen");
        int iron = Select(1); File.WriteAllText(Path.Combine(output, "client-target.txt"), iron.ToString());
        yield return Mine(iron); Move(originalPosition); Signal("iron-mined");
        yield return Marker("host", "final");
        yield return new WaitForSeconds(0.5f);
        Check(File.ReadAllText(Path.Combine(output, "final-layout.txt")) == Layout(), "identical post-respawn layout on both peers");
        ValidatePopulation();
    }
}
