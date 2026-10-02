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
        if (args[i + 1] != "host" && args[i + 1] != "client" && args[i + 1] != "probe") return; // other roles belong to NetworkVisualTest
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
        if (rock != null) { rock.TakeHit(1); Log($"host hit rock A, health now {rock.CurrentHealth}/{rock.MaxHealth}"); }

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

        // Mine rock A until it breaks (each TakeHit = one swing's hit).
        yield return new WaitForSeconds(1f); // let the host's hit land first
        RockHealth rock = RockA();
        Log($"rock A health seen by client before mining: {(rock != null ? rock.CurrentHealth.ToString() : "gone")}");
        int hits = 0;
        while (rock != null && hits < 20) { rock.TakeHit(1); hits++; yield return new WaitForSeconds(0.4f); }
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
