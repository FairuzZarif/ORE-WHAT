using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// DEVELOPMENT TEST ONLY. Does nothing unless the game is started with "-mptest host" or "-mptest client".
/// Two copies of a build run a scripted multiplayer session over a real network connection (direct, port 7777)
/// and print "[MPTEST]" lines to their logs: join through the menu buttons, spawn spots, movement sync,
/// mining one rock from both sides, ore drops, pickup (and a double pickup), drop, carry, leave, rejoin (late
/// join state) and the host ending the game.
///   host:   OreWhat.exe -batchmode -nographics -lan -mptest host   -logFile host.log
///   client: OreWhat.exe -batchmode -nographics -mptest client -logFile client.log
/// Map walk: "-mptest mwhost" / "-mptest mwclient" with "-routedown <csv> -routeback <csv>" (see MineWalkHost).
/// Actions are called the way the input code calls them (no real keyboard/mouse in batch mode).
/// </summary>
public class NetworkSmokeTest : MonoBehaviour
{
    private string role;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int i = System.Array.FindIndex(args, a => a == "-mptest");
        if (i < 0 || i + 1 >= args.Length) return;
        if (System.Array.IndexOf(new[] { "host", "client", "probe", "mwhost", "mwclient" }, args[i + 1]) < 0) return; // other roles belong to NetworkVisualTest
        var go = new GameObject("NetworkSmokeTest");
        DontDestroyOnLoad(go);
        go.AddComponent<NetworkSmokeTest>().role = args[i + 1];
    }

    private static void Log(string text) => Debug.Log("[MPTEST] " + text);

    private static string Arg(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private IEnumerator Start()
    {
        Application.targetFrameRate = 30;
        Log($"role={role}");
        yield return WaitFor(() => FindAnyObjectByType<MultiplayerMenu>() != null, 30f, "main menu");
        if (role == "host") yield return Host();
        else if (role == "probe") yield return Probe();
        else if (role == "mwhost") yield return MineWalkHost();
        else if (role == "mwclient") yield return MineWalkClient();
        else yield return Client();
        Log("DONE");
        Application.Quit();
    }

    // ------------------------------------------------------------------ host

    private IEnumerator Host()
    {
        Press("HostButton");
        yield return WaitFor(InGame, 60f, "host in game");
        Log($"HOST_READY code='{NetworkSessionManager.Instance.JoinCode}' world={(NetworkWorld.Instance != null)}");

        yield return WaitFor(() => Avatars() >= 2, 180f, "client joined");
        Log($"client joined: avatars={Avatars()}");

        // Host hits rock A once too (the client finishes it): the hits must add up across players.
        RockHealth rock = RockA();
        if (rock != null) { rock.TakeMiningHit(1, FindAnyObjectByType<PlayerEquipment>().Equipped); Log($"host hit rock A, health now {rock.CurrentHealth}/{rock.MaxHealth}"); }

        // Watch the client move: its avatar's position over time.
        NetworkPlayerAvatar remote = null;
        for (int t = 0; t < 14; t++)
        {
            remote = FindObjectsByType<NetworkPlayerAvatar>().FirstOrDefault(a => !a.IsOwner);
            if (remote != null)
            {
                var anim = remote.GetComponentInChildren<Animator>();
                var pose = remote.GetComponentInChildren<CharacterCrouchPose>();
                var lamp = remote.GetComponent<Headlamp>();
                Log($"remote avatar pos={remote.transform.position} dist-to-host={Vector3.Distance(remote.transform.position, LocalPlayer().transform.position):F2} " +
                    $"animSpeed={anim.GetFloat("Speed"):F2} state={(anim.GetCurrentAnimatorStateInfo(0).IsName("Walk") ? "Walk" : anim.GetCurrentAnimatorStateInfo(0).IsName("Idle") ? "Idle" : "other")} " +
                    $"crouch={(pose != null ? pose.ExternalAmount : -1f):F2} headlamp={(lamp != null && lamp.IsOn)} lampLight={(lamp != null && lamp.Light != null && lamp.Light.enabled)} renderersOn={remote.GetComponentsInChildren<Renderer>().Count(r => r.enabled)}");
            }
            yield return new WaitForSeconds(0.5f);
        }

        yield return WaitFor(() => Avatars() == 1, 120f, "client left");
        Log($"client left: avatars={Avatars()} items={Items()} brokenRockA={(RockA() == null)}");

        yield return WaitFor(() => Avatars() >= 2, 120f, "client rejoined");
        Log($"client rejoined: avatars={Avatars()}");
        yield return new WaitForSeconds(6f);
        Log($"host ending the game. items={Items()}");
        NetworkSessionManager.Instance.Leave();
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 30f, "host back in menu");
        Log($"host back in menu, status='{NetworkSessionManager.Instance.Status}'");
    }

    // ------------------------------------------------------------------ client

    private IEnumerator Client()
    {
        // Wrong code first: must come back with an error, not hang.
        SetCode("10.255.255.1"); // nothing listens there
        Press("JoinButton");
        yield return WaitFor(() => NetworkSessionManager.Instance != null && NetworkSessionManager.Instance.Current == NetworkSessionManager.Phase.Offline
                                   && NetworkSessionManager.Instance.StatusIsError, 60f, "bad address error");
        Log($"bad address -> '{NetworkSessionManager.Instance.Status}'");

        yield return JoinHost();
        PlayerMovement me = LocalPlayer();
        var hostAvatar = FindObjectsByType<NetworkPlayerAvatar>().FirstOrDefault(a => !a.IsOwner);
        Log($"CLIENT_JOINED avatars={Avatars()} me={me.transform.position} host={(hostAvatar != null ? hostAvatar.transform.position.ToString() : "none")} " +
            $"apart={(hostAvatar != null ? Vector3.Distance(me.transform.position, hostAvatar.transform.position).ToString("F2") : "?")} world={(NetworkWorld.Instance != null)}");

        // Walk forward for a second (the host logs our avatar moving).
        var cc = me.GetComponent<CharacterController>();
        for (int f = 0; f < 30; f++) { cc.Move(me.transform.forward * 4f / 30f + Vector3.down * 0.1f); yield return null; }
        Log($"walked to {me.transform.position}");

        // Crouch with the headlamp on for a moment (the host logs both on our avatar), then stand up, lamp off.
        var lampMe = me.GetComponent<Headlamp>();
        var crouchMe = me.GetComponent<PlayerCrouch>();
        lampMe.SetOn(true);
        crouchMe.Crouch();
        yield return new WaitForSeconds(1.5f);
        Log($"crouched: amount={crouchMe.Amount:F2} lamp={lampMe.IsOn}");
        crouchMe.TryStand();
        lampMe.SetOn(false);

        // Mine rock A until it breaks (each TakeMiningHit = one swing's hit).
        yield return new WaitForSeconds(1f); // let the host's hit land first
        RockHealth rock = RockA();
        Log($"rock A health seen by client before mining: {(rock != null ? rock.CurrentHealth.ToString() : "gone")}");
        int hits = 0;
        while (rock != null && hits < 20) { rock.TakeMiningHit(1, FindAnyObjectByType<PlayerEquipment>().Equipped); hits++; yield return new WaitForSeconds(0.4f); }
        Log($"rock A broken after {hits} client hits: {rock == null}");
        yield return WaitFor(() => Items() > 0, 10f, "ore drops");
        yield return new WaitForSeconds(1.5f); // let them settle
        Log($"ore items in world: {Items()}");

        // Pick one up (twice in a row: the host must only grant it once).
        var interactor = me.GetComponent<ItemPickupInteractor>();
        var inventory = me.GetComponent<PlayerInventory>();
        NetworkItem ore = NearestItem(me.transform.position, carryableOnly: true); // an ore from the rock
        TeleportNear(me, ore.transform.position);
        ItemData oreType = ore.Dropped.Item;
        int before = inventory.Count(oreType), itemsBefore = Items();
        bool first = interactor.TryPickup(ore.Dropped);
        bool second = interactor.TryPickup(ore.Dropped);
        yield return new WaitForSeconds(1.5f);
        Log($"pickup requests sent: {first},{second}; {oreType.DisplayName} in inventory {before} -> {inventory.Count(oreType)}; items {itemsBefore} -> {Items()}");

        // Drop it again (the host spawns it for everyone).
        int slot = Enumerable.Range(0, inventory.SlotCount).First(s => inventory.Slots[s].item == oreType);
        itemsBefore = Items();
        me.GetComponent<ItemDropper>().Drop(slot, 1);
        yield return new WaitForSeconds(1.5f);
        Log($"dropped 1: inventory {inventory.Count(oreType)}, items {itemsBefore} -> {Items()}");

        // Carry one (ownership moves to us, then we carry it with our own physics).
        NetworkItem carryMe = NearestItem(me.transform.position, carryableOnly: true);
        TeleportNear(me, carryMe.transform.position);
        var carrier = me.GetComponent<OreCarryController>();
        bool asked = WorldNetwork.Current.RequestCarry(carryMe.Dropped, carrier);
        yield return WaitFor(() => carrier.IsCarrying, 5f, "carry");
        Log($"carry asked={asked} owner-is-me={carryMe.IsOwner} carrying={carrier.IsCarrying}");
        yield return new WaitForSeconds(1f);
        carrier.ReleaseLast();

        // Leave, come back (late join: the broken rock must already be gone), then the host ends the game.
        yield return new WaitForSeconds(1f);
        NetworkSessionManager.Instance.Leave();
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu" && NetworkSessionManager.Instance.Current == NetworkSessionManager.Phase.Offline, 30f, "back in menu");
        Log($"left -> menu, status='{NetworkSessionManager.Instance.Status}'");
        yield return new WaitForSeconds(2f);

        yield return JoinHost();
        Log($"REJOINED rockA-gone={RockA() == null} items={Items()} avatars={Avatars()}");

        yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 120f, "host ended the game");
        Log($"host ended -> menu, status='{NetworkSessionManager.Instance.Status}' error={NetworkSessionManager.Instance.StatusIsError}");
    }

    // ------------------------------------------------------------------ mine walk (map test)
    // "-mptest mwhost" / "-mptest mwclient" with "-routedown <file> -routeback <file>" (lines "x,y,z[,label]", e.g. a
    // mine's route from the hub to its arena and back): the client walks both routes with its CharacterController
    // while the host watches the client's avatar reach the arena and come back; then the host walks them while the
    // client watches. Checks that both players can get through the map and that the others see it.

    private IEnumerator MineWalkHost()
    {
        Press("HostButton");
        yield return WaitFor(InGame, 60f, "host in game");
        Log($"HOST_READY world={(NetworkWorld.Instance != null)}");
        yield return WaitFor(() => Avatars() >= 2, 180f, "client joined");
        Log($"client joined: avatars={Avatars()}");
        yield return WatchRemoteWalk("client");
        PlayerMovement me = LocalPlayer();
        yield return WalkRoute(me, Arg("-routedown"), "host down", new Vector3(3f, 0f, 0f));
        yield return WalkRoute(me, Arg("-routeback"), "host back", Vector3.zero);
        yield return new WaitForSeconds(4f);
        Log("host ending the game");
        NetworkSessionManager.Instance.Leave();
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 30f, "host back in menu");
    }

    private IEnumerator MineWalkClient()
    {
        yield return JoinHost();
        PlayerMovement me = LocalPlayer();
        Log($"CLIENT_JOINED avatars={Avatars()} me={me.transform.position}");
        yield return new WaitForSeconds(2f);
        yield return WalkRoute(me, Arg("-routedown"), "client down", Vector3.zero);
        yield return WalkRoute(me, Arg("-routeback"), "client back", Vector3.zero);
        yield return WatchRemoteWalk("host");
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 120f, "host ended the game");
        Log($"host ended -> menu, status='{NetworkSessionManager.Instance.Status}'");
    }

    /// <summary>Walks the local player along a route file's points with its CharacterController (collisions, steps and
    /// slopes as in play), logging each labelled point reached and any spot it got stuck on.</summary>
    private static IEnumerator WalkRoute(PlayerMovement me, string file, string title, Vector3 startOffset)
    {
        var points = new System.Collections.Generic.List<(Vector3 p, string label)>();
        if (!string.IsNullOrEmpty(file) && System.IO.File.Exists(file))
            foreach (string line in System.IO.File.ReadAllLines(file))
            {
                string[] a = line.Split(',');
                if (a.Length < 3) continue;
                var c = System.Globalization.CultureInfo.InvariantCulture;
                points.Add((new Vector3(float.Parse(a[0], c), float.Parse(a[1], c), float.Parse(a[2], c)), a.Length > 3 ? a[3] : ""));
            }
        if (points.Count < 2) { Log($"{title}: no route in '{file}'"); yield break; }
        var cc = me.GetComponent<CharacterController>();
        if (Vector3.Distance(me.transform.position, points[0].p) > 4f)
        {
            cc.enabled = false; me.transform.position = points[0].p + startOffset + Vector3.up * 0.2f; cc.enabled = true;
            yield return null;
        }
        float walked = 0f; int stuck = 0;
        for (int i = 1; i < points.Count; i++)
        {
            Vector3 corner = points[i].p;
            float best = float.MaxValue; int noProgress = 0, guard = 0;
            bool done = false;
            while (!done)
            {
                for (int k = 0; k < 2 && !done; k++) // 2 steps of up to 0.3 m a frame
                {
                    Vector3 pos = me.transform.position, flat = corner - pos; flat.y = 0f;
                    float dist = flat.magnitude;
                    if (dist < 0.4f) { done = true; break; }
                    me.transform.rotation = Quaternion.LookRotation(flat.normalized);
                    cc.Move(flat.normalized * Mathf.Min(0.3f, dist) + Vector3.down * 0.25f);
                    Vector3 moved = me.transform.position - pos; moved.y = 0f;
                    walked += moved.magnitude;
                    noProgress = dist > best - 0.02f ? noProgress + 1 : 0;
                    best = Mathf.Min(best, dist);
                    if (noProgress > 40 || ++guard > 6000)
                    {
                        stuck++;
                        Log($"{title}: STUCK at {me.transform.position} on the way to point {i} {points[i].label}");
                        cc.enabled = false; me.transform.position = corner + Vector3.up * 0.2f; cc.enabled = true;
                        done = true;
                    }
                }
                yield return null;
            }
            if (points[i].label.Length > 0) Log($"{title}: reached {points[i].label} at {me.transform.position}");
        }
        Log($"{title}: walked {walked:F0} m, stuck {stuck}, ended at {me.transform.position}");
    }

    /// <summary>Follows the other player's avatar until it has been in Mine 1's arena and come back to the hub.</summary>
    private static IEnumerator WatchRemoteWalk(string who)
    {
        Transform cave = GameObject.Find("Island") != null ? GameObject.Find("Island").transform.Find("Cave") : null;
        Vector3 arena = cave != null && cave.Find("BossArena") != null ? cave.Find("BossArena").position : new Vector3(410f, -18.7f, 720f);
        Vector3 hub = cave != null && cave.Find("Cavern_01") != null ? cave.Find("Cavern_01").position : new Vector3(502f, 17.3f, 722f);
        bool reachedArena = false, backAtHub = false;
        float lowest = float.MaxValue, nextLog = 0f, giveUp = Time.realtimeSinceStartup + 420f;
        while (Time.realtimeSinceStartup < giveUp && !backAtHub)
        {
            NetworkPlayerAvatar remote = FindObjectsByType<NetworkPlayerAvatar>().FirstOrDefault(a => !a.IsOwner && a.IsSpawned);
            if (remote != null)
            {
                Vector3 p = remote.transform.position;
                lowest = Mathf.Min(lowest, p.y);
                Vector3 toArena = p - arena; toArena.y = 0f;
                if (!reachedArena && Mathf.Abs(p.y - arena.y) < 6f && toArena.magnitude < 36f) { reachedArena = true; Log($"saw the {who} reach the boss arena at {p}"); }
                if (reachedArena && Vector3.Distance(p, hub) < 30f) { backAtHub = true; Log($"saw the {who} back in the hub at {p}"); }
                if (Time.realtimeSinceStartup > nextLog) { nextLog = Time.realtimeSinceStartup + 6f; Log($"{who} avatar at {p}"); }
            }
            yield return null;
        }
        Log($"watched the {who}: reachedArena={reachedArena} backAtHub={backAtHub} lowestY={lowest:F1}");
    }

    /// <summary>One join attempt with -joincode, then report what the menu says (invalid code, game full...).</summary>
    private IEnumerator Probe()
    {
        SetCode(Arg("-joincode"));
        Press("JoinButton");
        yield return new WaitForSeconds(0.5f);
        yield return WaitFor(() => InGame() || NetworkSessionManager.Instance.Current == NetworkSessionManager.Phase.Offline, 60f, "join result");
        Log($"probe '{Arg("-joincode")}' -> inGame={InGame()} status='{NetworkSessionManager.Instance.Status}' error={NetworkSessionManager.Instance.StatusIsError} scene={SceneManager.GetActiveScene().name}");
    }

    private IEnumerator JoinHost()
    {
        float giveUp = Time.realtimeSinceStartup + 120f;
        while (Time.realtimeSinceStartup < giveUp)
        {
            yield return WaitFor(() => FindAnyObjectByType<MultiplayerMenu>() != null, 30f, "menu");
            yield return new WaitForSeconds(1f);
            SetCode(Arg("-joincode") ?? "127.0.0.1"); // a real join code tests the online (Relay) path
            Press("JoinButton");
            yield return WaitFor(() => InGame() || NetworkSessionManager.Instance.Current == NetworkSessionManager.Phase.Offline, 60f, "join result");
            if (InGame()) yield break;
            Log($"join attempt failed: '{NetworkSessionManager.Instance.Status}' (retrying: host may still be loading)");
            yield return new WaitForSeconds(3f);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static bool InGame() => NetworkSessionManager.Instance != null && NetworkSessionManager.Instance.Current == NetworkSessionManager.Phase.InGame
                                    && NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    private static int Avatars() => FindObjectsByType<NetworkPlayerAvatar>().Count(a => a.IsSpawned);
    private static int Items() => FindObjectsByType<NetworkItem>().Count(i => i.IsSpawned);
    private static PlayerMovement LocalPlayer() => FindAnyObjectByType<PlayerMovement>();

    private static Vector3? rockAPosition;

    /// <summary>The rock nearest the map's start point when first asked (the same one on both machines), found again by
    /// its position (after a rejoin the map is reloaded). Null once it's broken.</summary>
    private static RockHealth RockA()
    {
        if (rockAPosition == null)
        {
            Vector3 start = new Vector3(500f, 18f, 500f);
            RockHealth first = FindObjectsByType<RockHealth>().Where(r => r.CurrentHealth > 0)
                .OrderBy(r => (r.transform.position - start).sqrMagnitude).FirstOrDefault();
            if (first != null) rockAPosition = first.transform.position;
            return first;
        }
        return FindObjectsByType<RockHealth>().FirstOrDefault(r => r.CurrentHealth > 0 && (r.transform.position - rockAPosition.Value).sqrMagnitude < 0.01f);
    }

    private static NetworkItem NearestItem(Vector3 p, bool carryableOnly = false) =>
        FindObjectsByType<NetworkItem>().Where(i => i.IsSpawned && !i.Held.Value && (!carryableOnly || i.Dropped.Item.Carryable))
            .OrderBy(i => (i.transform.position - p).sqrMagnitude).FirstOrDefault();

    private static void TeleportNear(PlayerMovement p, Vector3 target)
    {
        var cc = p.GetComponent<CharacterController>();
        cc.enabled = false;
        p.transform.position = target + Vector3.back * 1.2f;
        cc.enabled = true;
    }

    private static void Press(string button)
    {
        GameObject go = GameObject.Find(button);
        Log($"press {button}: {(go != null ? "found" : "MISSING")}");
        if (go != null) go.GetComponent<Button>().onClick.Invoke();
    }

    private static void SetCode(string code)
    {
        GameObject go = GameObject.Find("JoinCodeField");
        if (go != null) go.GetComponent<InputField>().text = code;
    }

    private static IEnumerator WaitFor(System.Func<bool> condition, float seconds, string what)
    {
        float giveUp = Time.realtimeSinceStartup + seconds;
        while (!condition())
        {
            if (Time.realtimeSinceStartup > giveUp) { Log($"TIMEOUT waiting for {what}"); yield break; }
            yield return null;
        }
    }
}
