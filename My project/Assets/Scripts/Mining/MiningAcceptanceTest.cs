using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Opt-in development acceptance run: actual equipped attacks, colliders, drops and real host/client state.</summary>
public sealed class MiningAcceptanceTest : MonoBehaviour
{
    public string role = "sp";
    public string output;
    public string singlePlayerResource;
    int failures;
    PlayerMovement player;
    PlayerEquipment equipment;
    Camera eye;
    RockHealth aiming;
    Vector3 originalPosition;
    Quaternion originalRotation, originalEyeRotation;
    bool originalMove, originalLook;
    int originalSlot;
    readonly List<GameObject> temporary = new List<GameObject>();
    readonly List<ItemData> added = new List<ItemData>();
    Dictionary<string, RockHealth[]> nodes;
    static readonly string[] Resources = { "CopperOre", "IronOre", "GoldOre", "Crystal" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Debug.isDebugBuild && !Application.isEditor) return;
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, "-kaytest");
        if (index < 0 || index + 1 >= args.Length) return;
        var go = new GameObject("MiningAcceptanceTest"); DontDestroyOnLoad(go);
        var test = go.AddComponent<MiningAcceptanceTest>(); test.role = args[index + 1];
        int folder = Array.IndexOf(args, "-kayreview");
        test.output = folder >= 0 && folder + 1 < args.Length ? args[folder + 1] : Path.GetFullPath("../Builds/KayKitMiningReview");
    }

    void Check(bool passed, string text)
    {
        string line = "[KAYTEST] " + role + " " + (passed ? "PASS " : "FAIL ") + text;
        if (!passed) failures++;
        Debug.Log(line);
        File.AppendAllText(Path.Combine(output, role + "-results.txt"), line + Environment.NewLine);
    }

    IEnumerator Start()
    {
        if (string.IsNullOrEmpty(output)) output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/KayKitMiningReview"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, role + "-results.txt"), "");
        Application.targetFrameRate = 60;
        if (role != "sp")
        {
            yield return Until(() => FindAnyObjectByType<MultiplayerMenu>() != null, "main menu");
            if (role == "client")
            {
                var field = GameObject.Find("JoinCodeField").GetComponent<InputField>(); field.text = "127.0.0.1";
            }
            GameObject.Find(role == "host" ? "HostButton" : "JoinButton").GetComponent<Button>().onClick.Invoke();
            yield return Until(() => FindAnyObjectByType<PlayerMovement>() != null && NetworkWorld.Instance != null, "loaded world");
            yield return new WaitForSeconds(2f);
        }
        player = FindAnyObjectByType<PlayerMovement>(); equipment = player.GetComponent<PlayerEquipment>();
        eye = player.GetComponentsInChildren<Camera>().First(c => c.name == "PlayerCamera");
        originalPosition = player.transform.position; originalRotation = player.transform.rotation;
        originalEyeRotation = eye.transform.localRotation;
        originalMove = player.enabled; originalLook = player.GetComponent<PlayerLook>().enabled;
        originalSlot = player.GetComponent<PlayerInventory>().SelectedSlot;
        player.enabled = false; player.GetComponent<PlayerLook>().enabled = false;
        var all = FindObjectsByType<RockHealth>().OrderBy(r => r.transform.position.x)
            .ThenBy(r => r.transform.position.z).ThenBy(r => r.transform.position.y).ToArray();
        nodes = Resources.ToDictionary(n => n, n => all.Where(r => r.OreItem != null && r.OreItem.name == n).ToArray());
        if (role == "sp") yield return SinglePlayer();
        else if (role == "host") yield return Host();
        else yield return Client();
        aiming = null;
        Check(failures == 0, "COMPLETE failures=" + failures);
        Signal("done");
        foreach (var go in temporary) if (go != null) Destroy(go);
        foreach (var item in added)
        {
            var inventory = player.GetComponent<PlayerInventory>();
            for (int s = 0; s < inventory.SlotCount; s++) if (inventory.Slots[s].item == item) inventory.RemoveFromSlot(s, 1);
        }
        player.GetComponent<PlayerInventory>().SelectSlot(originalSlot);
        Move(originalPosition); player.transform.rotation = originalRotation; eye.transform.localRotation = originalEyeRotation;
        player.enabled = originalMove; player.GetComponent<PlayerLook>().enabled = originalLook;
        if (role != "sp") { yield return new WaitForSeconds(role == "host" ? 2f : .2f); Application.Quit(failures == 0 ? 0 : 1); }
    }

    void LateUpdate() { if (aiming != null && eye != null) Aim(); }
    void Aim()
    {
        Vector3 direction = aiming.transform.TransformPoint(Vector3.up * .53f) - eye.transform.position;
        player.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 0);
        eye.transform.localRotation = Quaternion.Euler(-Mathf.Asin(direction.normalized.y) * Mathf.Rad2Deg, 0, 0);
    }
    void Move(Vector3 position)
    {
        var cc = player.GetComponent<CharacterController>(); bool on = cc.enabled; cc.enabled = false;
        player.transform.position = position; cc.enabled = on; Physics.SyncTransforms();
    }
    IEnumerator Target(RockHealth rock, bool otherSide = false)
    {
        aiming = rock;
        bool hit = false; RaycastHit rayHit = default;
        var spawns = rock.GetComponentInParent<OreSpawnSystem>();
        if (spawns != null)
            foreach (var entry in spawns.Active)
                if (entry.Value == rock && spawns.TryRecord(entry.Key, out var state))
                {
                    Move(spawns.sockets[state.socket].approach - Vector3.up * 1.05f);
                    yield return new WaitForSeconds(.12f); Aim();
                    hit = Physics.Raycast(eye.transform.position, eye.transform.forward, out rayHit, 3f, ~((1 << 6) | (1 << 8)), QueryTriggerInteraction.Ignore);
                    if (hit && rayHit.collider.GetComponentInParent<RockHealth>() == rock)
                    { Check(true, rock.OreItem.name + " reachable from validated standing position"); yield break; }
                    break;
                }
        for (int direction = 0; direction < 8; direction++)
        {
            Vector3 offset = Quaternion.Euler(0, (otherSide ? 180 : 0) + direction * 45, 0) * new Vector3(-.35f, .04f, -1.65f);
            Move(rock.transform.position + offset);
            yield return new WaitForSeconds(.12f); Aim();
            hit = Physics.Raycast(eye.transform.position, eye.transform.forward, out rayHit, 3f, ~((1 << 6) | (1 << 8)), QueryTriggerInteraction.Ignore);
            if (hit && rayHit.collider.GetComponentInParent<RockHealth>() == rock) break;
        }
        Check(hit && rayHit.collider.GetComponentInParent<RockHealth>() == rock, rock.OreItem.name + " first aim ray hits node (" + (hit ? rayHit.collider.name : "nothing") + ")");
    }
    ItemData Item(string name) => UnityEngine.Resources.FindObjectsOfTypeAll<ItemData>().FirstOrDefault(i => i.name == name);
    IEnumerator Equip(string name)
    {
        var inventory = player.GetComponent<PlayerInventory>();
        if (name == "Fist")
        {
            for (int i = 0; i < inventory.SlotCount; i++) if (inventory.Slots[i].IsEmpty) { inventory.SelectSlot(i); break; }
        }
        else
        {
            ItemData item = Item(name);
            if (inventory.Count(item) == 0) { inventory.AddItem(item, 1); added.Add(item); }
            for (int i = 0; i < inventory.SlotCount; i++) if (inventory.Slots[i].item == item) { inventory.SelectSlot(i); break; }
        }
        yield return new WaitForSeconds(.75f); // equipment transition and owner held-item synchronization
    }
    int Drops(ItemData item) => FindObjectsByType<DroppedItem>().Where(d => d.Item == item).Sum(d => d.Amount);
    int Stage(RockHealth rock) => rock != null ? rock.GetComponent<OreNodeVisual>().Stage : 4;
    IEnumerator Swing(RockHealth rock)
    {
        Aim(); var tool = equipment.ActiveController as MiningToolController;
        Check(tool != null && tool.Swing.Swing(), "equipped swing started");
        yield return new WaitForSeconds(1.25f);
    }

    IEnumerator NonMining(RockHealth rock)
    {
        int health = rock.CurrentHealth, stage = Stage(rock), drops = Drops(rock.OreItem);
        foreach (string source in new[] { "Hammer", "Fist", "Pistol", "AssaultRifle" })
        {
            yield return Equip(source); int impacts = 0, actualShots = 0;
            var weapon = equipment.ActiveController as WeaponController;
            Action<RaycastHit> onShot = h => { if (h.collider.GetComponentInParent<RockHealth>() == rock) impacts++; };
            Action<RaycastHit, RockHealth> onSurface = (h, r) => { if (h.collider.GetComponentInParent<RockHealth>() == rock) impacts++; };
            if (weapon != null) weapon.ShotHit += onShot;
            var fists = equipment.UnarmedView.GetComponent<FistsController>();
            Action onPunch = () => impacts++;
            if (source == "Fist") fists.HitLanded += onPunch;
            var mining = player.GetComponent<MiningController>(); mining.SurfaceHit += onSurface;
            int beforeShots = weapon != null ? weapon.ShotsFired : 0;
            for (int hit = 0; hit < (weapon != null ? 5 : 2); hit++)
            {
                Aim();
                if (weapon != null) { weapon.SendMessage("Fire"); yield return new WaitForSeconds(.23f); }
                else if (source == "Hammer") yield return Swing(rock);
                else { Check(player.GetComponent<UnarmedAttack>().Punch(), "fist punch started"); yield return new WaitForSeconds(.8f); }
            }
            if (weapon != null) { actualShots = weapon.ShotsFired - beforeShots; weapon.ShotHit -= onShot; }
            if (source == "Fist") fists.HitLanded -= onPunch;
            mining.SurfaceHit -= onSurface;
            if (role != "sp")
            {
                // A forged mining request from a non-mining equipped item must also be refused on the host.
                WorldNetwork.Current.RequestRockHit(rock, 1);
                yield return new WaitForSeconds(.3f);
            }
            Check(rock != null && rock.CurrentHealth == health && Stage(rock) == stage && Drops(rock.OreItem) == drops,
                rock.OreItem.name + " " + source + " unchanged HP=" + health + " stage=" + stage + " drops=" + drops + " nodeImpacts=" + impacts + " shots=" + actualShots);
            if (weapon != null) Check(actualShots == 5 && impacts == 5, source + " five real shots blocked by resource collider");
            if (source == "Hammer") Check(impacts == 2, "hammer surface hit feedback preserved");
            if (source == "Fist") Check(impacts == 2, "two real punches contacted the blocking node");
        }
    }

    IEnumerator MineFully(RockHealth rock, string label)
    {
        var ore = rock.OreItem; int before = Drops(ore); int previous = rock.CurrentHealth;
        yield return Equip("Pickaxe");
        while (rock != null && previous > 0)
        {
            yield return Swing(rock);
            int health = rock != null ? rock.CurrentHealth : 0;
            Check(health == previous - 1, label + " pickaxe HP=" + previous + " -> " + health + " stage=" + Stage(rock));
            if (rock != null) Check(Stage(rock) == OreNodeVisualView.StageFor(health, rock.MaxHealth), label + " durability-derived cracks");
            if (role == "sp" && rock != null) Capture(label + "-hp" + health);
            if (health >= previous) break; previous = health;
        }
        yield return new WaitForSeconds(.4f);
        Check(rock == null && Drops(ore) == before + 3, label + " depleted exactly once; correct " + ore.name + " drops +" + (Drops(ore) - before));
    }

    IEnumerator SinglePlayer()
    {
        foreach (string resource in Resources.Where(r => string.IsNullOrEmpty(singlePlayerResource) || r == singlePlayerResource))
        {
            var clone = Instantiate(nodes[resource][0].gameObject, new Vector3(500, 160, 500), Quaternion.identity);
            temporary.Add(clone); var rock = clone.GetComponent<RockHealth>();
            yield return Target(rock); yield return Equip("Pickaxe"); Capture(resource + "-intact");
            Check(rock.GetComponent<IDamageable>() == null, resource + " does not accept the generic combat damage interface");
            yield return NonMining(rock);
            int impacts = 0; player.GetComponent<MiningController>().SurfaceHit += CountImpact;
            void CountImpact(RaycastHit hit, RockHealth target) { if (target == rock) impacts++; }
            yield return MineFully(rock, resource);
            player.GetComponent<MiningController>().SurfaceHit -= CountImpact;
            Check(impacts == 5, resource + " existing pickaxe impact feedback fired for all five strikes");
            // Fresh full-health node for the explicit reset-and-shoot regression.
            var reset = Instantiate(nodes[resource][0].gameObject, new Vector3(500, 160, 500), Quaternion.identity);
            temporary.Add(reset); yield return Target(reset.GetComponent<RockHealth>());
            yield return NonMining(reset.GetComponent<RockHealth>());
            reset.SetActive(false);
        }
        foreach (int max in new[] { 4, 7, 20 })
        {
            Check(OreNodeVisualView.StageFor(max, max) == 0 && OreNodeVisualView.StageFor(0, max) == 4,
                "percentage stages support maxHP=" + max);
            Check(OreNodeVisualView.StageFor(Mathf.FloorToInt(max * .75f), max) >= 1
                && OreNodeVisualView.StageFor(Mathf.FloorToInt(max * .5f), max) >= 2
                && OreNodeVisualView.StageFor(Mathf.FloorToInt(max * .25f), max) >= 3, "75/50/25 percent thresholds maxHP=" + max);
        }
    }

    void Signal(string name) => File.WriteAllText(Path.Combine(output, role + "-" + name + ".flag"), "ready");
    IEnumerator WaitSignal(string who, string name) => Until(() => File.Exists(Path.Combine(output, who + "-" + name + ".flag")), who + " " + name);
    IEnumerator Until(Func<bool> condition, string description)
    {
        float until = Time.realtimeSinceStartup + 90f;
        while (!condition() && Time.realtimeSinceStartup < until) yield return null;
        Check(condition(), "wait for " + description);
    }
    void Park() { aiming = null; Move(new Vector3(500, 160, 520)); }
    IEnumerator Observe(RockHealth rock, string label)
    {
        int previous = rock.CurrentHealth; var ore = rock.OreItem; int before = Drops(ore);
        float until = Time.realtimeSinceStartup + 30f; var seen = new HashSet<int> { Stage(rock) };
        while (rock != null && Time.realtimeSinceStartup < until)
        {
            if (rock.CurrentHealth != previous)
            {
                previous = rock.CurrentHealth; seen.Add(Stage(rock));
                Check(Stage(rock) == OreNodeVisualView.StageFor(previous, rock.MaxHealth), label + " remote HP=" + previous + " stage=" + Stage(rock));
            }
            yield return null;
        }
        yield return new WaitForSeconds(.6f);
        Check(rock == null && Drops(ore) == before + 3, label + " remote depletion/drop identity and quantity");
        Check(seen.Contains(0) && seen.Contains(1) && seen.Contains(2) && seen.Contains(3), label + " all remote crack stages observed");
    }
    IEnumerator Host()
    {
        var copper = nodes["CopperOre"][0]; yield return Target(copper); yield return Equip("Pickaxe");
        for (int i = 0; i < 4; i++) yield return Swing(copper);
        Check(copper.CurrentHealth == 1 && Stage(copper) == 3, "pre-join damaged copper HP=1 stage=3");
        Park(); Signal("ready"); yield return WaitSignal("client", "latejoin");
        foreach (string resource in Resources) { yield return Target(nodes[resource][1]); yield return NonMining(nodes[resource][1]); }
        Park(); Signal("guns"); yield return WaitSignal("client", "guns");
        var ore = copper.OreItem; int before = Drops(ore);
        Signal("finish-copper"); yield return Until(() => copper == null, "client depleted late-join copper");
        yield return new WaitForSeconds(.6f); Check(Drops(ore) == before + 3, "late-join copper single shared drop batch");
        yield return Target(nodes["IronOre"][0]); Signal("iron");
        yield return WaitSignal("client", "watch-iron"); yield return MineFully(nodes["IronOre"][0], "host-iron");
        Park(); yield return WaitSignal("client", "iron"); Signal("watch-gold");
        yield return Observe(nodes["GoldOre"][0], "client-gold"); yield return WaitSignal("client", "gold");
        var crystal = nodes["Crystal"][0]; yield return Target(crystal); yield return Equip("Pickaxe");
        Signal("crystal"); yield return WaitSignal("client", "crystal");
        double start = NetworkManager.Singleton.ServerTime.Time + 2;
        File.WriteAllText(Path.Combine(output, "simultaneous-time.txt"), start.ToString("R", CultureInfo.InvariantCulture));
        int crystalBefore = Drops(crystal.OreItem); var crystalItem = crystal.OreItem;
        for (int i = 0; i < 3; i++)
        {
            while (NetworkManager.Singleton.ServerTime.Time < start + i * 1.5) yield return null;
            if (crystal != null) yield return Swing(crystal);
        }
        yield return new WaitForSeconds(.7f);
        Check(crystal == null && Drops(crystalItem) == crystalBefore + 3, "simultaneous mining one destruction and three shared Crystal drops");
        Park(); Signal("crystal-done"); yield return WaitSignal("client", "crystal-done");
    }
    IEnumerator Client()
    {
        var copper = nodes["CopperOre"][0];
        Check(copper.CurrentHealth == 1 && Stage(copper) == 3, "late join received HP=1 / heavy cracks without another hit");
        Park(); Signal("latejoin"); yield return WaitSignal("host", "guns");
        foreach (string resource in Resources)
        {
            var rock = nodes[resource][1]; Check(rock.CurrentHealth == 5 && Stage(rock) == 0, "host gun/melee attempts synchronized unchanged " + resource);
            yield return Target(rock, true); yield return NonMining(rock);
        }
        Park(); Signal("guns"); yield return WaitSignal("host", "finish-copper");
        yield return Target(copper); yield return Equip("Pickaxe"); var ore = copper.OreItem; int before = Drops(ore);
        yield return Swing(copper); yield return new WaitForSeconds(.6f);
        Check(copper == null && Drops(ore) == before + 3, "client final pickaxe hit on late-join node: correct Copper drops");
        Park(); yield return WaitSignal("host", "iron"); Signal("watch-iron");
        yield return Observe(nodes["IronOre"][0], "host-iron"); Signal("iron");
        yield return WaitSignal("host", "watch-gold"); yield return Target(nodes["GoldOre"][0]);
        yield return MineFully(nodes["GoldOre"][0], "client-gold"); Park(); Signal("gold");
        yield return WaitSignal("host", "crystal"); var crystal = nodes["Crystal"][0];
        yield return Target(crystal, true); yield return Equip("Pickaxe"); Signal("crystal");
        yield return Until(() => File.Exists(Path.Combine(output, "simultaneous-time.txt")), "simultaneous start");
        double start = double.Parse(File.ReadAllText(Path.Combine(output, "simultaneous-time.txt")), CultureInfo.InvariantCulture);
        var item = crystal.OreItem; int crystalBefore = Drops(item);
        for (int i = 0; i < 3; i++)
        {
            while (NetworkManager.Singleton.ServerTime.Time < start + i * 1.5) yield return null;
            if (crystal != null) yield return Swing(crystal);
        }
        yield return WaitSignal("host", "crystal-done"); yield return new WaitForSeconds(.7f);
        Check(crystal == null && Drops(item) == crystalBefore + 3, "simultaneous shared Crystal depletion/drop count");
        Park(); Signal("crystal-done");
    }
    void Capture(string label)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var target = RenderTexture.GetTemporary(960, 600, 24);
        var previous = eye.targetTexture; var active = RenderTexture.active;
        eye.targetTexture = target; eye.Render(); RenderTexture.active = target;
        var image = new Texture2D(960, 600, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, label + ".png"), image.EncodeToPNG());
        eye.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); Destroy(image);
    }

    // Editor can run this coroutine on a disabled test component for actual cave-lighting review.
    public IEnumerator CaptureMineContext(string folder)
    {
        output = folder; role = "art"; Directory.CreateDirectory(output);
        player = FindAnyObjectByType<PlayerMovement>(); equipment = player.GetComponent<PlayerEquipment>();
        eye = player.GetComponentsInChildren<Camera>().First(c => c.name == "PlayerCamera");
        var position = player.transform.position; var rotation = player.transform.rotation; var eyeRotation = eye.transform.localRotation;
        bool move = player.enabled, look = player.GetComponent<PlayerLook>().enabled;
        int slot = player.GetComponent<PlayerInventory>().SelectedSlot;
        var lamp = player.GetComponent<Headlamp>(); bool lampOn = lamp.IsOn;
        player.enabled = false; player.GetComponent<PlayerLook>().enabled = false; lamp.SetOn(true);
        foreach (string resource in Resources)
        {
            var rock = FindObjectsByType<RockHealth>().Where(r => r.OreItem != null && r.OreItem.name == resource
                && (r.GetComponentInParent<OreSpawnSystem>() != null || (r.transform.parent != null
                    && r.transform.parent.parent != null && r.transform.parent.parent.name == "MiningRocks")))
                .OrderBy(r => Vector3.Distance(r.transform.position, new Vector3(500, 0, 700))).First();
            yield return Target(rock); yield return Equip("Pickaxe"); yield return new WaitForSeconds(.5f);
            Capture("mine-" + resource + "-intact");
            for (int i = 0; i < 4; i++) { yield return Swing(rock); Capture("mine-" + resource + "-hp" + rock.CurrentHealth); }
            rock.ShowNetworkHit(rock.MaxHealth, false);
            var spawns = rock.GetComponentInParent<OreSpawnSystem>();
            if (spawns != null)
                foreach (var entry in spawns.Active)
                    if (entry.Value == rock && spawns.TryRecord(entry.Key, out var state))
                    { state.health = rock.MaxHealth; spawns.ApplyRecord(state, false); break; }
        }
        aiming = null;
        foreach (var item in added)
        {
            var inventory = player.GetComponent<PlayerInventory>();
            for (int s = 0; s < inventory.SlotCount; s++) if (inventory.Slots[s].item == item) inventory.RemoveFromSlot(s, 1);
        }
        player.GetComponent<PlayerInventory>().SelectSlot(slot); Move(position); player.transform.rotation = rotation;
        eye.transform.localRotation = eyeRotation; lamp.SetOn(lampOn); player.enabled = move; player.GetComponent<PlayerLook>().enabled = look;
        Signal("done");
    }
}
