using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

/// <summary>Opt-in development review. Normal play never starts this; fixtures are physical pickups, not inventory grants.</summary>
public sealed class CompanyEconomyAcceptanceTest : MonoBehaviour
{
    public string role = "sp", output;
    public bool pointerOnly;
    public bool Complete { get; private set; }
    public int Failures { get; private set; }
    PlayerMovement player;
    PlayerInventory inventory;
    PlayerEquipment equipment;
    PlayerCurrency wallet;
    ItemPickupInteractor pickup;
    CompanyOfficeUI menu;
    CompanyWorkerNPC worker;
    Camera eye;
    Vector3 aim;
    bool aiming;
    static readonly string[] OreNames = { "CopperOre", "IronOre", "GoldOre", "Crystal" };
    const int WorldMask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Debug.isDebugBuild && !Application.isEditor) return;
        var args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, "-companytest");
        if (i < 0 || i + 1 >= args.Length) return;
        var go = new GameObject("CompanyEconomyAcceptanceTest"); DontDestroyOnLoad(go);
        var test = go.AddComponent<CompanyEconomyAcceptanceTest>(); test.role = args[i + 1];
        i = Array.IndexOf(args, "-companyreview"); if (i >= 0 && i + 1 < args.Length) test.output = args[i + 1];
    }
    void Check(bool ok, string message)
    {
        if (!ok) Failures++;
        string line = "[COMPANYTEST] " + role + " " + (ok ? "PASS " : "FAIL ") + message;
        Debug.Log(line); File.AppendAllText(Path.Combine(output, role + "-results.txt"), line + Environment.NewLine);
    }
    IEnumerator Until(Func<bool> condition, string label, float seconds = 45)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < end) yield return null;
        Check(condition(), label);
    }
    void Signal(string phase) => File.WriteAllText(Path.Combine(output, role + "-" + phase + ".signal"), "ready");
    IEnumerator Wait(string other, string phase) => Until(() => File.Exists(Path.Combine(output, other + "-" + phase + ".signal")), other + " " + phase, 120);
    ItemData Item(string name) => UnityEngine.Resources.FindObjectsOfTypeAll<ItemData>().First(i => i.name == name);
    IEnumerator Start()
    {
        if (string.IsNullOrEmpty(output)) output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/CompanyOfficeReview/PlayMode"));
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, role + "-results.txt"), "");
        Application.targetFrameRate = 60;
        if (role != "sp")
        {
            yield return Until(() => FindAnyObjectByType<MultiplayerMenu>() != null, "main menu");
            if (role != "host") GameObject.Find("JoinCodeField").GetComponent<InputField>().text = "127.0.0.1";
            GameObject.Find(role == "host" ? "HostButton" : "JoinButton").GetComponent<Button>().onClick.Invoke();
            yield return Until(() => FindAnyObjectByType<PlayerMovement>() != null && NetworkWorld.Instance != null && NetworkPlayerEconomy.Local != null, "loaded world and own account");
            yield return Until(() => NetworkPlayerEconomy.Local.Revision > 0, "initial account snapshot");
            yield return new WaitForSeconds(2);
        }
        player = FindAnyObjectByType<PlayerMovement>(); inventory = player.GetComponent<PlayerInventory>();
        equipment = player.GetComponent<PlayerEquipment>(); wallet = player.GetComponent<PlayerCurrency>();
        pickup = player.GetComponent<ItemPickupInteractor>(); menu = player.GetComponent<CompanyOfficeUI>();
        eye = player.GetComponentsInChildren<Camera>().First(c => c.name == "PlayerCamera"); worker = FindAnyObjectByType<CompanyWorkerNPC>();
        player.enabled = false; player.GetComponent<PlayerLook>().enabled = false;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        Check(worker != null && wallet.Money == 0 && CompanySale.Quote(inventory) == 0 && inventory.Count(Item("Pickaxe")) == 1, "one generated worker; new account starts at $0 with existing starter pickaxe");
        if (role == "sp") { if (pointerOnly) yield return PointerReview(); else yield return SinglePlayer(); } else yield return Multiplayer();
        menu.Close(); aiming = false;
        Check(Failures == 0, "COMPLETE failures=" + Failures); Complete = true; Signal("done");
        if (role != "sp") { yield return new WaitForSeconds(role == "host" ? 2f : .5f); Application.Quit(Failures == 0 ? 0 : 1); }
    }
    void LateUpdate() { if (aiming && eye != null && !player.GetComponent<PlayerAttributes>().IsDead) Aim(); }
    void Aim()
    {
        var d = (aim - eye.transform.position).normalized;
        player.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0);
        eye.transform.localRotation = Quaternion.Euler(-Mathf.Asin(d.y) * Mathf.Rad2Deg, 0, 0);
    }
    void Move(Vector3 feet)
    {
        var cc = player.GetComponent<CharacterController>(); bool enabled = cc.enabled; cc.enabled = false;
        player.transform.position = feet; cc.enabled = enabled; Physics.SyncTransforms();
    }
    Vector3 Ground(Vector3 point)
    {
        return Physics.Raycast(point + Vector3.up * 2.5f, Vector3.down, out var h, 5, WorldMask, QueryTriggerInteraction.Ignore)
            ? new Vector3(point.x, h.point.y + .04f, point.z) : point;
    }
    Vector3 CounterFeet(float x = 0) => Ground(worker.Office.TransformPoint(new Vector3(x, 0, -1.05f)));
    IEnumerator Counter(float x = 0)
    {
        menu.Close(); Move(CounterFeet(x)); aim = worker.TalkPoint; aiming = true; Aim();
        yield return new WaitForSeconds(.75f);
        pickup.RefreshTarget(); Check(pickup.CompanyWorker == worker && worker.CanInteract(eye.transform.position, player.transform.position), "inside office: existing look probe targets worker");
    }
    IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame();
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), texture.EncodeToPNG()); Destroy(texture);
    }
    IEnumerator KeyPress(Key key)
    {
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key)); yield return null; yield return null;
        InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null;
    }
    IEnumerator Click(UnityEngine.UI.Button button)
    {
        var rect = button.GetComponent<RectTransform>(); Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = point }); yield return null; yield return null;
        InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = point }.WithButton(MouseButton.Left)); yield return null; yield return null;
        InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = point }); yield return null; yield return null;
    }
    IEnumerator PointerReview()
    {
        yield return Fixture("CopperOre", 10); yield return Counter();
        Check(menu.Open(worker) && UnityEngine.EventSystems.EventSystem.current != null, "selling menu has working Input System event module before any death");
        yield return Click(menu.SellAllButton);
        Check(wallet.Money == 30 && inventory.Count(Item("CopperOre")) == 0 && menu.LastEarned == 30, "actual mouse press/release clicks Sell All and credits $30");
        Check(!player.GetComponent<MiningController>().enabled && !player.GetComponent<UnarmedAttack>().enabled, "mouse sale does not swing or punch");
        yield return Capture("actual-mouse-sale");
        var close = player.GetComponentsInChildren<Button>(true).First(b => b.name == "Close"); yield return Click(close);
        Check(!menu.IsOpen && pickup.enabled && player.GetComponent<UnarmedAttack>().enabled, "actual mouse click closes menu and restores gameplay input");
    }
    IEnumerator Collect(DroppedItem dropped)
    {
        if (dropped == null) { Check(false, "physical fixture exists"); yield break; }
        var item = dropped.Item; int amount = dropped.Amount, before = inventory.Count(item);
        aim = dropped.GetComponentsInChildren<Collider>().First(c => !c.isTrigger).bounds.center; aiming = true; Aim(); Physics.SyncTransforms();
        pickup.RefreshTarget(); Check(pickup.Target == dropped, item.name + " original look probe reaches physical pickup");
        Check(pickup.TryPickup(dropped), item.name + " original pickup requested x" + amount);
        yield return Until(() => inventory.Count(item) == before + amount, item.name + " real pickup stored x" + amount);
        yield return new WaitForSeconds(.2f);
    }
    IEnumerator Fixture(string name, int amount)
    {
        if (!Item(name).CompanyOre && inventory.Count(Item(name)) >= amount) { Check(true, name + " existing starter preserved"); yield break; }
        menu.Close(); Move(new Vector3(500, 18.25f, 500)); aim = player.transform.position + Vector3.forward * 2; aiming = true; Aim();
        var dropped = ItemDrops.Spawn(Item(name), amount, player.transform.position + Vector3.forward * 1.25f + Vector3.up * .6f, Quaternion.identity, Vector3.zero, Vector3.zero);
        yield return null; yield return Collect(dropped);
    }
    IEnumerator Sale(int expected, ItemData only = null, string label = "sale")
    {
        int before = wallet.Money;
        Check(menu.Open(worker), label + " menu opens");
        Check(CompanySale.Quote(inventory, only) == expected, label + " quote=$" + expected);
        Check(menu.Sell(only), label + " sale requested");
        yield return Until(() => !menu.Pending && wallet.Money == before + expected, label + " credited exactly $" + expected);
        Check(menu.LastEarned == expected && !menu.Sell(only), label + " receipt correct; repeat cannot pay again");
        Check(player.GetComponents<AudioSource>().Any(a => a.isPlaying), label + " feedback sound playing");
        yield return Capture(role + "-" + label.Replace(' ', '-'));
        menu.Close();
    }
    IEnumerator SinglePlayer()
    {
        aim = player.transform.position + Vector3.forward * 2 + Vector3.up; aiming = true; Aim();
        var starter = player.GetComponent<ItemDropper>().Drop(0, 1); yield return null; yield return Collect(starter);
        yield return Counter();
        var animator = worker.GetComponentInChildren<Animator>(); var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        var rotation = chest.localRotation; var root = worker.transform.position;
        yield return new WaitForSeconds(.7f);
        Check(animator.isHuman && animator.avatar.isValid && Quaternion.Angle(rotation, chest.localRotation) > .01f && worker.transform.position == root, "valid humanoid Breathing Idle moves chest without root motion");
        Check(worker.GetComponentsInChildren<Camera>().Length == 0 && worker.GetComponentsInChildren<Light>().Length == 0, "worker has no exported cameras/lights");
        Check(worker.GetComponentsInChildren<Renderer>().All(r => r.bounds.size.magnitude < 3f), "worker geometry stays character-sized; no exported studio backdrop spans the map");
        yield return Capture("company-worker");
        Check(menu.Open(worker) && !menu.SellAllButton.interactable && !menu.Sell() && wallet.Money == 0, "empty inventory disables selling with no $0 transaction"); menu.Close();
        foreach (var name in OreNames) foreach (int count in new[] { 1, 10 })
        {
            yield return Fixture(name, count); yield return Counter();
            yield return Sale(Item(name).Value * count, null, name + " x" + count);
            Check(inventory.Count(Item(name)) == 0, name + " sold quantity removed");
        }
        foreach (var name in new[] { "Pickaxe", "Hammer", "Pistol" }) yield return Fixture(name, 1);
        for (int i = 0; i < 4; i++) yield return Fixture(OreNames[i], new[] { 10, 4, 2, 1 }[i]);
        yield return Counter(); Check(menu.Open(worker), "mixed menu opens"); yield return Capture("mixed-before-sale"); menu.Close();
        yield return Sale(4790, null, "mixed 4790");
        Check(new[] { "Pickaxe", "Hammer", "Pistol" }.All(n => inventory.Count(Item(n)) == 1), "mixed sale preserves pickaxe/hammer/pistol");
        yield return Fixture("AssaultRifle", 1); yield return Counter();
        Check(menu.Open(worker) && !menu.SellAllButton.interactable && !menu.Sell(Item("AssaultRifle")) && !menu.Sell(Item("Pickaxe")), "rifle and other non-ores cannot be sold"); menu.Close();
        yield return Fixture("CopperOre", 2); yield return Fixture("IronOre", 1); yield return Counter();
        yield return Sale(6, Item("CopperOre"), "individual Copper"); Check(inventory.Count(Item("IronOre")) == 1, "individual sale leaves other ore type");
        yield return Sale(40, null, "remaining Iron");
        var loose = ItemDrops.Spawn(Item("GoldOre"), 1, player.transform.position + Vector3.up * .5f, Quaternion.identity, Vector3.zero, Vector3.zero);
        Check(CompanySale.Quote(inventory) == 0 && loose != null, "nearby loose Gold excluded from owned quote");
        var carrier = player.GetComponent<OreCarryController>(); Check(carrier.TryCarry(loose), "physical Gold carried using existing carry system");
        Check(menu.Open(worker) && !menu.Sell() && inventory.Count(Item("GoldOre")) == 0, "carried world ore excluded until stored"); menu.Close();
        Check(pickup.TryPickup(loose), "F-store path transfers carried ore into inventory"); yield return new WaitForSeconds(.25f);
        Check(!carrier.IsCarrying && inventory.Count(Item("GoldOre")) == 1, "stored carried Gold now owned"); yield return Sale(300, null, "stored carried Gold");
        yield return Counter();
        Move(Ground(worker.Office.TransformPoint(new Vector3(0, 0, -3)))); Aim(); pickup.RefreshTarget();
        Check(pickup.CompanyWorker == null && !menu.Open(worker), "outside office interaction refused");
        Move(Ground(worker.Office.TransformPoint(new Vector3(2.4f, 0, .95f)))); Aim(); pickup.RefreshTarget();
        Check(pickup.CompanyWorker == null && !menu.Open(worker), "side wall interaction refused");
        yield return Counter();
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Company review LOS blocker";
        blocker.transform.position = (eye.transform.position + worker.TalkPoint) / 2; blocker.transform.localScale = new Vector3(1, 1, .3f); Physics.SyncTransforms();
        Check(!worker.CanInteract(eye.transform.position, player.transform.position) && !menu.Open(worker), "obstructed line of sight refused inside office"); Destroy(blocker); yield return null;
        player.enabled = true; player.GetComponent<PlayerLook>().enabled = true;
        Check(menu.Open(worker) && !player.enabled && !player.GetComponent<PlayerLook>().enabled && !player.GetComponent<UnarmedAttack>().enabled && !player.GetComponent<MiningController>().enabled && Cursor.lockState == CursorLockMode.None, "menu suspends movement/look/punch/mining and frees mouse");
        yield return KeyPress(Key.Escape); Check(!menu.IsOpen && player.enabled && player.GetComponent<PlayerLook>().enabled && player.GetComponent<UnarmedAttack>().enabled, "Escape closes and restores input");
        player.enabled = false; player.GetComponent<PlayerLook>().enabled = false;
        yield return FullMineLoop();
        yield return Counter(); player.enabled = true; player.GetComponent<PlayerLook>().enabled = true;
        Check(menu.Open(worker), "respawn test menu opens"); int money = wallet.Money;
        player.GetComponent<PlayerAttributes>().TakeDamage(1000); yield return null;
        Check(!menu.IsOpen && !player.enabled && !player.GetComponent<UnarmedAttack>().enabled, "death closes menu while controls stay disabled");
        yield return Until(() => !player.GetComponent<PlayerDeath>().IsDead, "normal respawn completes", 20);
        Check(wallet.Money == money && player.enabled && player.GetComponent<PlayerLook>().enabled && player.GetComponent<UnarmedAttack>().enabled, "wallet and pre-menu controls survive respawn");
    }

    IEnumerator FullMineLoop()
    {
        var system = FindAnyObjectByType<OreSpawnSystem>(); RockHealth rock = null; OreSpawnSystem.Socket socket = null;
        foreach (var entry in system.Active.OrderBy(e => Vector3.Distance(e.Value.transform.position, new Vector3(444, 17.25f, 698))))
        {
            if (entry.Value.OreItem != Item("CopperOre") || !system.TryRecord(entry.Key, out var record)) continue;
            var candidate = system.sockets[record.socket];
            if (candidate.mine != 1 || candidate.approach.y < 15) continue;
            Move(candidate.approach - Vector3.up * 1.05f); aim = entry.Value.transform.TransformPoint(Vector3.up * .53f); aiming = true; Aim();
            if (!Physics.Raycast(eye.transform.position, eye.transform.forward, out var hit, 3, WorldMask, QueryTriggerInteraction.Ignore) || hit.collider.GetComponentInParent<RockHealth>() != entry.Value) continue;
            rock = entry.Value; socket = candidate; break;
        }
        Check(rock != null, "real Old Mine Copper node reachable" + (socket != null ? " in " + socket.space : "")); if (rock == null) yield break;
        Check(inventory.Count(Item("Pickaxe")) == 1, "pickaxe obtained earlier through a real physical pickup");
        for (int i = 0; i < inventory.SlotCount; i++) if (inventory.Slots[i].item == Item("Pickaxe")) inventory.SelectSlot(i);
        yield return new WaitForSeconds(.8f); int strikes = 0;
        while (rock != null && rock.CurrentHealth > 0 && strikes++ < 12)
        {
            Aim(); var tool = equipment.ActiveController as MiningToolController;
            Check(tool != null && tool.Swing.Swing(), "real equipped pickaxe mining strike " + strikes); yield return new WaitForSeconds(1.25f);
        }
        Check(rock == null || rock.CurrentHealth == 0, "Old Mine Copper broken by mining controller, not direct damage");
        var drops = FindObjectsByType<DroppedItem>().Where(d => d.Item == Item("CopperOre") && Vector3.Distance(d.transform.position, socket.position) < 5).ToArray();
        Check(drops.Sum(d => d.Amount) == 3, "mining produced three real Copper pickups");
        foreach (var drop in drops)
        {
            if (Vector3.Distance(eye.transform.position, drop.transform.position) > 2.3f) Move(Ground(drop.transform.position + Vector3.forward * .9f));
            yield return Collect(drop);
        }
        int amount = inventory.Count(Item("CopperOre")); Check(amount == 3, "mined Copper stored in existing hotbar");
        aiming = false; yield return WalkToOffice();
        aim = worker.TalkPoint; aiming = true; Aim(); pickup.RefreshTarget();
        Check(pickup.CompanyWorker == worker, "physical return ends inside office looking at worker");
        Cursor.lockState = CursorLockMode.Locked; yield return KeyPress(Key.E);
        Check(menu.IsOpen, "real E interaction opens Company Office");
        if (!menu.IsOpen) menu.Open(worker);
        int before = wallet.Money; menu.SellAllButton.onClick.Invoke(); yield return null;
        Check(inventory.Count(Item("CopperOre")) == 0 && wallet.Money == before + amount * 3 && menu.LastEarned == 9, "full loop: mined Copper sold via button for $9");
        yield return Capture("full-loop-receipt"); yield return KeyPress(Key.E); Check(!menu.IsOpen, "E closes Company Office");
    }

    // Review only: temporary paths from actual colliders; the NPC has no navigation component.
    // The mine-to-office return moves the real CharacterController continuously and never teleports.
    IEnumerator WalkToOffice()
    {
        Vector3 start = player.transform.position, goal = CounterFeet();
        var bounds = new Bounds((start + goal) / 2, new Vector3(Mathf.Abs(start.x - goal.x) + 90, 24, Mathf.Abs(start.z - goal.z) + 90));
        var sources = new List<UnityEngine.AI.NavMeshBuildSource>();
        UnityEngine.AI.NavMeshBuilder.CollectSources(bounds, WorldMask, UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders, 0,
            new List<UnityEngine.AI.NavMeshBuildMarkup>(), sources);
        var settings = UnityEngine.AI.NavMesh.GetSettingsByID(0);
        settings.agentRadius = .45f; settings.agentHeight = 1.8f; settings.agentSlope = 45; settings.agentClimb = .3f;
        settings.overrideVoxelSize = true; settings.voxelSize = .1f;
        var data = UnityEngine.AI.NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        var instance = UnityEngine.AI.NavMesh.AddNavMeshData(data);
        var path = new UnityEngine.AI.NavMeshPath();
        bool found = UnityEngine.AI.NavMesh.SamplePosition(start, out var from, 3, UnityEngine.AI.NavMesh.AllAreas) &&
            UnityEngine.AI.NavMesh.SamplePosition(goal, out var to, 2, UnityEngine.AI.NavMesh.AllAreas) &&
            UnityEngine.AI.NavMesh.CalculatePath(from.position, to.position, UnityEngine.AI.NavMesh.AllAreas, path) && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
        Check(found, "real collision-aware mine-to-office walking route planned: start=" + start + " goal=" + goal);
        float walked = 0; bool reached = found;
        if (found)
        {
            var cc = player.GetComponent<CharacterController>();
            foreach (var corner in path.corners.Concat(new[] { goal }))
            {
                float deadline = Time.realtimeSinceStartup + 25;
                while (Vector2.Distance(new Vector2(player.transform.position.x, player.transform.position.z), new Vector2(corner.x, corner.z)) > .12f && Time.realtimeSinceStartup < deadline)
                {
                    Vector3 delta = corner - player.transform.position; delta.y = 0;
                    var before = player.transform.position; cc.Move(Vector3.ClampMagnitude(delta, 4 * Time.deltaTime) + Vector3.down * 2 * Time.deltaTime);
                    walked += Vector3.Distance(before, player.transform.position); yield return null;
                }
                if (Time.realtimeSinceStartup >= deadline) { reached = false; break; }
            }
        }
        UnityEngine.AI.NavMesh.RemoveNavMeshData(instance); Destroy(data);
        Check(reached && Vector3.Distance(player.transform.position, goal) < .25f, "physically walked " + walked.ToString("F1") + "m from Old Mine to office using CharacterController; end=" + player.transform.position);
    }
    Vector3 CollectionSpot(string who) => new Vector3(who == "host" ? 500 : who == "client" ? 505 : 510, 18.25f, 500);
    IEnumerator Multiplayer()
    {
        if (role == "late")
        {
            Move(CollectionSpot("client2") + Vector3.right * 8);
            yield return Until(() => FindObjectsByType<NetworkPlayerEconomy>().Count(a => a.Money > 0) == 3, "late join receives three independent existing balances");
            Check(wallet.Money == 0 && CompanySale.Quote(inventory) == 0 && inventory.Count(Item("Pickaxe")) == 1, "late join own account fresh $0 with starter; no inherited money");
            Check(FindObjectsByType<NetworkPlayerEconomy>().Select(a => a.Money).OrderBy(v => v).SequenceEqual(new[] { 0, 40, 330, 4200 }), "late join sees host $330 / client $4200 / client2 $40 / own $0");
            Signal("verified"); yield return Wait("host", "release"); yield break;
        }
        Move(CollectionSpot(role)); yield return new WaitForSeconds(.8f);
        if (role == "host") Signal("ready");
        if (role != "host") Signal("joined");
        if (role == "host")
        {
            yield return Wait("client", "joined"); yield return Wait("client2", "joined");
            var fixtures = new[] { ("host", "CopperOre", 10), ("host", "GoldOre", 1), ("client", "IronOre", 5), ("client", "Crystal", 1), ("client2", "IronOre", 1) };
            foreach (var f in fixtures)
            {
                var dropped = ItemDrops.Spawn(Item(f.Item2), f.Item3, CollectionSpot(f.Item1) + Vector3.forward * 1.25f + Vector3.up * .6f, Quaternion.identity, Vector3.zero, Vector3.zero);
                Check(dropped != null, "host spawned shared physical " + f.Item1 + " " + f.Item2); if (f.Item1 == "host") yield return Collect(dropped);
                else { Signal(f.Item1 + "-" + f.Item2); yield return Wait(f.Item1, f.Item2 + "-picked"); }
            }
            Signal("fixtures");
        }
        else
        {
            foreach (var name in role == "client" ? new[] { "IronOre", "Crystal" } : new[] { "IronOre" })
            {
                yield return Wait("host", role + "-" + name);
                DroppedItem dropped = null;
                yield return Until(() => (dropped = FindObjectsByType<DroppedItem>().FirstOrDefault(d => d.Item == Item(name) && Vector3.Distance(d.transform.position, CollectionSpot(role)) < 3)) != null, "shared " + name + " present");
                yield return Collect(dropped); Signal(name + "-picked");
            }
            yield return Wait("host", "fixtures");
        }
        int expected = role == "host" ? 330 : role == "client" ? 4200 : 40;
        var dropItem = Item(role == "host" ? "CopperOre" : "IronOre");
        int ownedBefore = inventory.Count(dropItem), slot = 0;
        for (int i = 0; i < inventory.SlotCount; i++) if (inventory.Slots[i].item == dropItem) slot = i;
        aim = eye.transform.position + Vector3.forward * 2; aiming = true; Aim();
        player.GetComponent<ItemDropper>().Drop(slot, 1);
        yield return Until(() => inventory.Count(dropItem) == ownedBefore - 1, "Q drop removes one authoritative owned ore");
        yield return new WaitForSeconds(.25f);
        var droppedBack = FindObjectsByType<DroppedItem>().FirstOrDefault(d => d.Item == dropItem && Vector3.Distance(d.transform.position, CollectionSpot(role)) < 4);
        yield return Collect(droppedBack);
        Check(CompanySale.Quote(inventory) == expected, "own actual holdings quote $" + expected);
        // A forged request from the clearing must fail on the host, even if bypassing the menu.
        NetworkPlayerEconomy.Local.RequestSale(worker); yield return new WaitForSeconds(.5f);
        Check(wallet.Money == 0 && CompanySale.Quote(inventory) == expected, "host rejects sale from outside office");
        yield return Counter(role == "host" ? -.9f : role == "client" ? 0 : .9f);
        Check(menu.Open(worker), "own UI open simultaneously"); Signal("open");
        if (role == "host")
        {
            yield return Wait("client", "open"); yield return Wait("client2", "open");
            Check(menu.IsOpen, "host and two clients concurrently use same worker"); Signal("sell");
        }
        else yield return Wait("host", "sell");
        menu.SellAllButton.onClick.Invoke(); Check(!menu.Sell(), "rapid repeat button cannot start duplicate sale");
        yield return Until(() => !menu.Pending && wallet.Money == expected, "own sale credited exactly $" + expected);
        Check(CompanySale.Quote(inventory) == 0 && inventory.Count(Item("Pickaxe")) == 1, "only own sold ores removed; starter preserved");
        NetworkPlayerEconomy.Local.RequestSale(worker); NetworkPlayerEconomy.Local.RequestSale(worker);
        yield return new WaitForSeconds(.75f);
        Check(wallet.Money == expected, "repeated RPC requests cannot pay for consumed ore");
        yield return Capture(role + "-receipt"); menu.Close(); Signal("sold");
        if (role == "host")
        {
            yield return Wait("client", "sold"); yield return Wait("client2", "sold");
            Check(FindObjectsByType<NetworkPlayerEconomy>().Select(a => a.Money).OrderBy(v => v).SequenceEqual(new[] { 40, 330, 4200 }), "host ledger holds separate $330/$4200/$40 balances");
            Signal("wantlate"); yield return Wait("late", "verified");
            Signal("security"); yield return Wait("client", "security");
            yield return Wait("client", "respawned");
            Check(CompanySale.Quote(FindObjectsByType<NetworkPlayerEconomy>().First(a => a.Money == 4200).ServerInventory) == 0, "host confirms client wallet/no ore after respawn");
            Signal("release");
        }
        else if (role == "client")
        {
            yield return Wait("host", "security");
            // Deliberately alter the non-authoritative local view; the real server must never buy this fake Crystal.
            inventory.AddItem(Item("Crystal"), 1); NetworkPlayerEconomy.Local.RequestSale(worker);
            yield return new WaitForSeconds(.75f);
            Check(wallet.Money == 4200 && inventory.Count(Item("Crystal")) == 0, "host ignores forged local inventory and returns actual authoritative slots");
            Check(ItemDrops.Spawn(Item("GoldOre"), 1, eye.transform.position, Quaternion.identity, Vector3.zero, Vector3.zero) == null, "client cannot mint world ore through spawn request"); Signal("security");
            player.enabled = true; player.GetComponent<PlayerLook>().enabled = true;
            Check(menu.Open(worker), "client respawn menu opens");
            player.GetComponent<PlayerAttributes>().TakeDamage(1000); yield return Until(() => player.GetComponent<PlayerDeath>().IsDead, "host-authorized client death");
            Check(!menu.IsOpen, "client death closes selling UI");
            yield return Until(() => !player.GetComponent<PlayerDeath>().IsDead, "client normal respawn", 20);
            Check(wallet.Money == 4200 && CompanySale.Quote(inventory) == 0 && inventory.Count(Item("Pickaxe")) == 1 && player.enabled && player.GetComponent<PlayerLook>().enabled && player.GetComponent<UnarmedAttack>().enabled, "client $4200, starter and controls survive respawn");
            Signal("respawned"); yield return Wait("host", "release");
        }
        else yield return Wait("host", "release");
    }
}
