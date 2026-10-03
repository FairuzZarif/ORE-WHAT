using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// DEVELOPMENT TEST ONLY. Does nothing unless the game is started with "-mptest chost", "cclient" or "clate"
/// (plus "-shots &lt;folder&gt;" and "-lan"). Real game processes check player damage, death, ragdoll and respawn:
///   1. the host checks the host's hit validation (a hit through a wall it alone has is refused; a huge damage
///      number is cut to the weapon's real damage),
///   2. the host shoots the (crouched) client: a miss, three pistol hits, then the rifle while the client strafes
///      until it dies; the client respawns,
///   3. the client shoots the host dead (pistol, then rifle); a late joiner arrives while the host is a ragdoll,
///   4. after the host respawns the client punches it.
/// Weapons fire through the same method the mouse click calls (WeaponController.Fire), punches through
/// UnarmedAttack.Punch; the players signal each other's phase through what they hold (the held item is synced).
///
/// Second scenario, "-mptest phost|pclient|pclient2|plate" (host, client A, client B, late joiner): punches (exactly one
/// 10-damage event each), pickaxe, hammer, pistol, rifle kill with damage numbers / ticks / kill sound / blood / hit
/// reactions counted, a forged duplicate request for one attack, RESPAWN NOW from a client and from the host (too early,
/// twice), client vs client, the automatic respawn and a late joiner.
/// </summary>
public class NetworkCombatTest : MonoBehaviour
{
    private string role, shots;
    private int shotCount;
    private float lookPitch, strafeSpeed;
    private Vector2 walkDir; // test walking: direction relative to the facing (x right, y forward) at walkSpeed m/s
    private float walkSpeed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string role = Arg("-mptest");
        if (System.Array.IndexOf(new[] { "chost", "cclient", "clate", "phost", "pclient", "pclient2", "plate", "zhost", "zclient", "shost", "sclient" }, role) < 0) return;
        var go = new GameObject("NetworkCombatTest");
        DontDestroyOnLoad(go);
        var t = go.AddComponent<NetworkCombatTest>();
        t.role = role;
        t.shots = Arg("-shots") ?? Application.persistentDataPath;
        Directory.CreateDirectory(t.shots);
    }

    private static string Arg(string name)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        int i = System.Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }

    private static void Log(string s) => Debug.Log("[CTEST] " + s);

    private IEnumerator Start()
    {
        Application.targetFrameRate = 30;
        yield return WaitFor(() => FindAnyObjectByType<MultiplayerMenu>() != null, 30f);
        yield return new WaitForSeconds(1f);
        if (role == "chost") yield return RunHost();
        else if (role == "cclient") yield return RunClient();
        else if (role == "clate") yield return RunLate();
        else if (role == "phost") yield return RunPolishHost();
        else if (role == "pclient" || role == "pclient2") yield return RunPolishClient(isA: role == "pclient");
        else if (role == "zhost") yield return RunZoneHost();
        else if (role == "zclient") yield return RunZoneClient();
        else if (role == "shost") yield return RunStrafeHost();
        else if (role == "sclient") yield return RunStrafeClient();
        else yield return RunPolishLate();
        Log("DONE");
        Application.Quit();
    }

    // ------------------------------------------------------------------ host

    private IEnumerator RunHost()
    {
        Press("HostButton");
        yield return WaitFor(InGame, 60f);
        yield return Place(new Vector3(484f, 0f, 812f), 90f);
        Log("HOST_READY");
        yield return WaitFor(() => Others().Any(), 180f);
        yield return new WaitForSeconds(4f);
        NetworkPlayerAvatar client = Other();
        var clientHealth = client.GetComponent<NetworkPlayerHealth>();
        var myAttributes = Me().GetComponent<PlayerAttributes>();

        // 1a. A wall only the host has: the client's "hit" must be refused.
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = (Me().transform.position + client.transform.position) * 0.5f + Vector3.up * 1.2f;
        wall.transform.localScale = new Vector3(0.3f, 3f, 3f);
        Equip("CopperOre");
        Log("STEP wall up (host only); waiting for the client's hit through it");
        yield return WaitFor(() => HeldName(client) == "Hammer", 40f);
        yield return new WaitForSeconds(0.5f);
        Log($"RESULT wall: host health {myAttributes.Health} (expect 100: refused)");
        Destroy(wall);

        // 1b. Damage claimed far above the weapon's: cut to the pistol's 20.
        Equip("Crystal");
        yield return WaitFor(() => HeldName(client) == "Pickaxe", 40f);
        yield return new WaitForSeconds(0.5f);
        Log($"RESULT clamp: host health {myAttributes.Health} (expect 80: 1000 cut to the pistol's 20)");

        // 2. Host shoots the client (crouched, standing still).
        Equip("Pistol");
        yield return WaitFor(() => HeldName(client) == "Pistol", 20f);
        yield return new WaitForSeconds(1.5f);
        int markers = Crosshair.HitMarkersShown;
        Aim(client, yawOffset: 25f); yield return null; Fire();
        yield return new WaitForSeconds(0.6f);
        Log($"RESULT miss: client health {clientHealth.Health} (expect 100) markers +{Crosshair.HitMarkersShown - markers} (expect 0) blood {Blood()}");
        for (int i = 0; i < 3; i++)
        {
            Aim(client, 0f); yield return null; Fire();
            yield return new WaitForSeconds(0.5f);
            Log($"RESULT pistol hit {i + 1}: client health {clientHealth.Health} (expect {100 - 20 * (i + 1)}) markers +{Crosshair.HitMarkersShown - markers} blood {Blood()}");
            if (i == 0) yield return Capture("host_sees_blood", client.transform);
        }

        // The rifle while the client strafes: until dead.
        Equip("AssaultRifle");
        yield return new WaitForSeconds(1.5f);
        int fired = 0;
        while (!clientHealth.IsDead && fired < 20)
        {
            Aim(client, 0f); yield return null; Fire(); fired++;
            yield return new WaitForSeconds(0.25f);
        }
        yield return new WaitForSeconds(0.3f);
        var clientRagdoll = client.GetComponentInChildren<CharacterRagdoll>(true);
        Log($"RESULT kill: rifle shots {fired} client health {clientHealth.Health} dead {clientHealth.IsDead} ragdoll {clientRagdoll != null && clientRagdoll.IsActive} " +
            $"collider {client.GetComponent<Collider>().enabled} markers +{Crosshair.HitMarkersShown - markers}");
        float hpAfterDeath = clientHealth.Health;
        Aim(client, 0f); yield return null; Fire(); yield return new WaitForSeconds(0.4f);
        Log($"RESULT shoot the dead: client health {clientHealth.Health} (expect {hpAfterDeath}) still dead {clientHealth.IsDead}");
        yield return new WaitForSeconds(1.2f);
        yield return Capture("host_sees_client_ragdoll", clientRagdoll != null && clientRagdoll.Hips != null ? clientRagdoll.Hips : client.transform);
        LogLimbSpeeds(clientRagdoll, "client ragdoll on host");

        yield return WaitFor(() => !clientHealth.IsDead, 12f);
        yield return new WaitForSeconds(1.5f);
        Log($"RESULT client respawned (host's view): health {clientHealth.Health} at {client.transform.position} ragdoll {clientRagdoll.IsActive} " +
            $"collider {client.GetComponent<Collider>().enabled} rendered {client.GetComponentInChildren<SkinnedMeshRenderer>().enabled}");
        yield return Capture("host_sees_client_respawned", client.transform);

        // 3. The client shoots the host dead. A long respawn so a late joiner can arrive while the host is a ragdoll.
        CombatSettings.Current.respawnDelay = 25f;
        yield return Place(new Vector3(484f, 0f, 812f), 90f);
        Equip("Hammer"); // "shoot me"
        yield return WaitFor(() => myAttributes.IsDead, 60f);
        var death = Me().GetComponent<PlayerDeath>();
        yield return new WaitForSeconds(0.2f);
        Log($"RESULT host died: health {myAttributes.Health} PlayerDeath {death.IsDead} cc {Me().GetComponent<CharacterController>().enabled} " +
            $"move {Me().GetComponent<PlayerMovement>().enabled} mining {Me().GetComponent<MiningController>().enabled} hands hidden {Me().GetComponent<PlayerEquipment>().Hidden} " +
            $"ragdoll {Me().GetComponentInChildren<CharacterRagdoll>().IsActive} countdown {death.RespawnCountdown:F1} " +
            $"death screen {Me().transform.Find("HotbarCanvas/DeathScreen")?.gameObject.activeSelf}");
        yield return new WaitForSeconds(1.5f);
        yield return Capture("host_death_camera", null);
        Log("READY_FOR_LATE");
        yield return WaitFor(() => !myAttributes.IsDead, 40f);
        yield return new WaitForSeconds(0.5f);
        Log($"RESULT host respawned: health {myAttributes.Health} at {Me().transform.position} cc {Me().GetComponent<CharacterController>().enabled} " +
            $"move {Me().GetComponent<PlayerMovement>().enabled} look {Me().GetComponent<PlayerLook>().enabled} hands hidden {Me().GetComponent<PlayerEquipment>().Hidden}");
        Equip("Pickaxe");
        yield return new WaitForSeconds(0.5f);
        Log($"RESULT host can mine after respawn: {Me().GetComponent<MiningController>().enabled && Me().GetComponent<PlayerEquipment>().CanMine}");
        SelectEmpty(Me().GetComponent<PlayerInventory>()); // "punch me"

        // 4. The client punches the host.
        float before = myAttributes.Health;
        yield return WaitFor(() => myAttributes.Health < before, 40f);
        Log($"RESULT punched: host health {myAttributes.Health} (expect {before - 10})");
        yield return WaitFor(() => Others().Count() < 2, 60f);
        yield return new WaitForSeconds(4f);
        NetworkSessionManager.Instance.Leave();
        yield return new WaitForSeconds(2f);
    }

    // ------------------------------------------------------------------ client

    private IEnumerator RunClient()
    {
        yield return Join();
        yield return Place(new Vector3(490f, 0f, 812f), 270f);
        Log("CLIENT_READY");
        NetworkPlayerAvatar host = Others().First(a => a.OwnerClientId == NetworkManager.ServerClientId);
        var hostHealth = host.GetComponent<NetworkPlayerHealth>();
        var me = Me();
        var attributes = me.GetComponent<PlayerAttributes>();
        attributes.HealthChanged += (h, m) => Log($"client health now {h}");

        // 1a / 1b: forged hits straight to the host's copy (what a cheating client could send).
        yield return WaitFor(() => HeldName(host) == "CopperOre", 60f);
        Equip("Pistol"); yield return new WaitForSeconds(1.5f);
        ForgedHit(hostHealth, 20f);
        yield return new WaitForSeconds(1f);
        Equip("Hammer");
        yield return WaitFor(() => HeldName(host) == "Crystal", 30f);
        Equip("Pistol"); yield return new WaitForSeconds(1.5f);
        ForgedHit(hostHealth, 1000f);
        yield return new WaitForSeconds(1f);
        Equip("Pickaxe");

        // 2. Get shot: crouched and still, then strafing.
        me.GetComponent<PlayerCrouch>().Crouch();
        yield return WaitFor(() => HeldName(host) == "Pistol", 30f);
        Equip("Pistol");
        yield return WaitFor(() => attributes.Health <= 40f, 30f);
        yield return Capture("client_hurt_view", null);
        me.GetComponent<PlayerCrouch>().TryStand();
        strafeSpeed = 2.5f;
        yield return WaitFor(() => attributes.IsDead, 30f);
        strafeSpeed = 0f;
        yield return new WaitForSeconds(0.2f);
        var death = me.GetComponent<PlayerDeath>();
        Log($"RESULT client died: health {attributes.Health} PlayerDeath {death.IsDead} cc {me.GetComponent<CharacterController>().enabled} " +
            $"move {me.GetComponent<PlayerMovement>().enabled} look {me.GetComponent<PlayerLook>().enabled} mining {me.GetComponent<MiningController>().enabled} " +
            $"pickup {me.GetComponent<ItemPickupInteractor>().enabled} hands hidden {me.GetComponent<PlayerEquipment>().Hidden} " +
            $"ragdoll {me.GetComponentInChildren<CharacterRagdoll>().IsActive} countdown {death.RespawnCountdown:F1} " +
            $"death screen {me.transform.Find("HotbarCanvas/DeathScreen")?.gameObject.activeSelf}");
        float hostBefore = hostHealth.Health;
        ForgedHit(hostHealth, 20f); // the dead can't hurt anyone
        yield return new WaitForSeconds(0.8f);
        Log($"RESULT dead client's hit: host health {hostHealth.Health} (expect {hostBefore})");
        yield return new WaitForSeconds(0.5f);
        yield return Capture("client_death_camera", null);

        yield return WaitFor(() => !death.IsDead, 12f);
        yield return new WaitForSeconds(0.5f);
        Log($"RESULT client respawned: health {attributes.Health} stamina {attributes.Stamina} at {me.transform.position} cc {me.GetComponent<CharacterController>().enabled} " +
            $"move {me.GetComponent<PlayerMovement>().enabled} look {me.GetComponent<PlayerLook>().enabled} hands hidden {me.GetComponent<PlayerEquipment>().Hidden} " +
            $"ragdoll {me.GetComponentInChildren<CharacterRagdoll>().IsActive} death screen {me.transform.Find("HotbarCanvas/DeathScreen")?.gameObject.activeSelf} " +
            $"camera local {me.GetComponentInChildren<Camera>().transform.localPosition}");
        Equip("Pickaxe");
        yield return new WaitForSeconds(0.5f);
        Log($"RESULT client can mine after respawn: {me.GetComponent<MiningController>().enabled && me.GetComponent<PlayerEquipment>().CanMine}");
        yield return Capture("client_respawned_view", null);

        // 3. Shoot the host dead (walk back first: we respawned at the start).
        yield return WaitFor(() => HeldName(host) == "Hammer", 30f);
        yield return Place(new Vector3(490f, 0f, 812f), 270f);
        int markers = Crosshair.HitMarkersShown;
        Equip("Pistol"); yield return new WaitForSeconds(1.5f);
        for (int i = 0; i < 3 && !hostHealth.IsDead; i++) { Aim(host, 0f); yield return null; Fire(); yield return new WaitForSeconds(0.5f); }
        Log($"RESULT client pistol on host: host health {hostHealth.Health} (expect 40) markers +{Crosshair.HitMarkersShown - markers}");
        Equip("AssaultRifle"); yield return new WaitForSeconds(1.5f);
        for (int i = 0; i < 12 && !hostHealth.IsDead; i++) { Aim(host, 0f); yield return null; Fire(); yield return new WaitForSeconds(0.2f); }
        yield return new WaitForSeconds(0.5f);
        Log($"RESULT client killed host: dead {hostHealth.IsDead} ragdoll {host.GetComponentInChildren<CharacterRagdoll>().IsActive} markers +{Crosshair.HitMarkersShown - markers}");
        yield return new WaitForSeconds(1f);
        var hostRagdoll = host.GetComponentInChildren<CharacterRagdoll>();
        yield return Capture("client_sees_host_ragdoll", hostRagdoll.Hips);
        LogLimbSpeeds(hostRagdoll, "host ragdoll on client");

        // 4. After the host respawns: punch it.
        yield return WaitFor(() => !hostHealth.IsDead, 40f);
        yield return new WaitForSeconds(1.5f);
        yield return Capture("client_sees_host_respawned", host.transform);
        yield return WaitFor(() => HeldName(host) == null, 20f); // empty hands = "punch me"
        yield return PlaceNear(host.transform, 1.3f);
        SelectEmpty(me.GetComponent<PlayerInventory>());
        yield return new WaitForSeconds(0.5f);
        markers = Crosshair.HitMarkersShown;
        Aim(host, 0f); yield return null;
        bool punched = me.GetComponent<UnarmedAttack>().Punch();
        yield return new WaitForSeconds(0.8f);
        Log($"RESULT punch started {punched}: host health {hostHealth.Health} markers +{Crosshair.HitMarkersShown - markers}");
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 120f);
    }

    // ------------------------------------------------------------------ late joiner

    private IEnumerator RunLate()
    {
        yield return Join();
        yield return new WaitForSeconds(3f);
        NetworkPlayerAvatar host = Others().FirstOrDefault(a => a.OwnerClientId == NetworkManager.ServerClientId);
        var health = host.GetComponent<NetworkPlayerHealth>();
        var ragdoll = host.GetComponentInChildren<CharacterRagdoll>(true);
        Log($"RESULT late join sees host: dead {health.IsDead} health {health.Health} ragdoll {ragdoll.IsActive} " +
            $"held item shown {(host.GetComponent<RemotePlayerPresentation>().HeldItem != null)} rendered {host.GetComponentInChildren<SkinnedMeshRenderer>().enabled}");
        yield return PlaceNear(ragdoll.Hips != null ? ragdoll.Hips : host.transform, 3f);
        yield return new WaitForSeconds(1f);
        yield return Capture("late_sees_host_ragdoll", ragdoll.Hips != null ? ragdoll.Hips : host.transform);
        yield return WaitFor(() => !health.IsDead, 40f);
        yield return new WaitForSeconds(1.5f);
        Log($"RESULT late join sees host respawn: dead {health.IsDead} health {health.Health} ragdoll {ragdoll.IsActive} at {host.transform.position} " +
            $"rendered {host.GetComponentInChildren<SkinnedMeshRenderer>().enabled}");
        NetworkSessionManager.Instance.Leave();
        yield return new WaitForSeconds(2f);
    }

    // ================================================================== polish scenario (-mptest phost|pclient|pclient2|plate)
    // Host H, client A, client B, late joiner L. Every number checked is what the host confirmed.

    private IEnumerator RunPolishHost()
    {
        Press("HostButton");
        yield return WaitFor(InGame, 60f);
        yield return Place(new Vector3(484f, 0f, 812f), 90f);
        Log("HOST_READY");
        yield return WaitFor(() => Others().Count() >= 2, 180f);
        yield return new WaitForSeconds(5f);
        NetworkPlayerAvatar a = Others().First(o => o.OwnerClientId == 1);
        NetworkPlayerAvatar b = Others().First(o => o.OwnerClientId == 2);
        var aHealth = a.GetComponent<NetworkPlayerHealth>();
        var myAttributes = Me().GetComponent<PlayerAttributes>();
        Log($"RESULT forged duplicate: host health {myAttributes.Health} (expect ONE pistol hit counted: 80 if it landed on the chest, 88 on an arm/leg)");

        int ticks0 = HitConfirmFeedback.TicksPlayed, kills0 = HitConfirmFeedback.KillSoundsPlayed, hits0 = HitConfirmFeedback.HitsConfirmed;
        int blood0 = BloodSplatter.Events, splats0 = BloodSplatter.SplatsPlaced;
        var aReaction = a.GetComponentInChildren<CharacterHitReaction>(true);

        // Punches: each one exactly one 10-damage event.
        yield return PlaceNear(a.transform, 1.3f);
        SelectEmpty(Me().GetComponent<PlayerInventory>());
        yield return new WaitForSeconds(0.6f);
        for (int p = 0; p < 2; p++)
        {
            Aim(a, 0f); yield return null;
            bool started = Me().GetComponent<UnarmedAttack>().Punch();
            var seen = new List<float> { aHealth.Health };
            float until = Time.time + 0.9f; // the whole punch (raise, strike, hold, recover) and then some
            while (Time.time < until) { if (aHealth.Health != seen[seen.Count - 1]) seen.Add(aHealth.Health); yield return null; }
            Log($"RESULT punch {p + 1} (started {started}): A health {string.Join(" -> ", seen)} number {HitConfirmFeedback.LastNumber} reactions on A {aReaction.Count}");
        }

        // Tools, then guns (the rifle kills).
        foreach (string tool in new[] { "Pickaxe", "Hammer" })
        {
            Equip(tool); yield return new WaitForSeconds(1.5f);
            Aim(a, 0f); yield return null;
            float before = aHealth.Health;
            var mining = Me().GetComponent<PlayerEquipment>().ActiveController as MiningToolController;
            mining.Swing.Swing(); // what a click does (MiningController hits at the strike)
            yield return new WaitForSeconds(1.6f);
            Log($"RESULT {tool}: A health {before} -> {aHealth.Health} number {HitConfirmFeedback.LastNumber}");
        }
        Equip("Pistol"); yield return new WaitForSeconds(1.5f);
        Aim(a, 0f); yield return null; Fire(); yield return new WaitForSeconds(0.6f);
        Log($"RESULT Pistol: A health {aHealth.Health} number {HitConfirmFeedback.LastNumber}");
        Equip("AssaultRifle"); yield return new WaitForSeconds(1.5f);
        for (int i = 0; i < 6 && !aHealth.IsDead; i++) { Aim(a, 0f); yield return null; Fire(); yield return new WaitForSeconds(0.3f); }
        yield return new WaitForSeconds(0.3f);
        Log($"RESULT rifle kill: A dead {aHealth.IsDead} number {HitConfirmFeedback.LastNumber} | totals: hits confirmed +{HitConfirmFeedback.HitsConfirmed - hits0} " +
            $"ticks +{HitConfirmFeedback.TicksPlayed - ticks0} kill sounds +{HitConfirmFeedback.KillSoundsPlayed - kills0} blood events +{BloodSplatter.Events - blood0} " +
            $"splats +{BloodSplatter.SplatsPlaced - splats0} (alive {BloodSplatter.SplatsAlive}) reactions on A {aReaction.Count}");
        yield return Capture("host_sees_blood_splats", a.transform);
        float diedAt = Time.time;
        yield return WaitFor(() => !aHealth.IsDead, 12f);
        Log($"RESULT A came back after {Time.time - diedAt:F1}s (RESPAWN NOW, expect ~1.5s) health {aHealth.Health} objects {FindObjectsByType<NetworkPlayerAvatar>().Length} (expect 3)");

        // Client vs client happens now; then A kills the host.
        yield return Place(new Vector3(484f, 0f, 812f), 90f);
        var bReaction = b.GetComponentInChildren<CharacterHitReaction>(true);
        int aBefore = aReaction.Count, bBefore = bReaction.Count;
        yield return WaitFor(() => myAttributes.IsDead, 90f);
        Log($"RESULT host saw the clients' fight: reactions on A +{aReaction.Count - aBefore} on B +{bReaction.Count - bBefore} (expect 1 each)");
        var death = Me().GetComponent<PlayerDeath>();
        Button respawn = Me().transform.Find("HotbarCanvas/DeathScreen/RespawnNow").GetComponent<Button>();
        yield return new WaitForSeconds(0.3f);
        respawn.onClick.Invoke(); // too early: refused
        yield return new WaitForSeconds(0.3f);
        Log($"RESULT host RESPAWN NOW at 0.6s: still dead {death.IsDead} (expect True) button interactable {respawn.interactable}");
        yield return new WaitForSeconds(0.8f);
        float clicked = Time.time;
        respawn.onClick.Invoke();
        respawn.onClick.Invoke(); // twice: only one request
        yield return WaitFor(() => !death.IsDead, 8f);
        Log($"RESULT host RESPAWN NOW: back after {Time.time - clicked:F2}s health {myAttributes.Health} cc {Me().GetComponent<CharacterController>().enabled} " +
            $"move {Me().GetComponent<PlayerMovement>().enabled} hands hidden {Me().GetComponent<PlayerEquipment>().Hidden} death screen {Me().transform.Find("HotbarCanvas/DeathScreen").gameObject.activeSelf} " +
            $"cursor {Cursor.lockState}");
        Equip("Crystal"); // "B: kill A now"
        yield return WaitFor(() => aHealth.IsDead, 60f);
        Log("READY_FOR_LATE");
        diedAt = Time.time;
        yield return WaitFor(() => !aHealth.IsDead, 12f);
        Log($"RESULT A auto respawn after {Time.time - diedAt:F1}s (expect ~5)");
        yield return WaitFor(() => Others().Count() >= 3, 90f);
        yield return WaitFor(() => Others().Count() < 3, 90f);
        yield return new WaitForSeconds(3f);
        NetworkSessionManager.Instance.Leave();
        yield return new WaitForSeconds(2f);
    }

    private IEnumerator RunPolishClient(bool isA)
    {
        yield return Join();
        yield return Place(isA ? new Vector3(490f, 0f, 812f) : new Vector3(487f, 0f, 817f), isA ? 270f : 180f);
        Log("CLIENT_READY");
        var me = Me();
        var attributes = me.GetComponent<PlayerAttributes>();
        var death = me.GetComponent<PlayerDeath>();
        NetworkPlayerAvatar host = Others().First(o => o.OwnerClientId == NetworkManager.ServerClientId);
        var hostHealth = host.GetComponent<NetworkPlayerHealth>();
        attributes.HealthChanged += (h, m) => Log($"own health now {h}");
        yield return WaitFor(() => Others().Count() >= 2, 60f);
        NetworkPlayerAvatar other = Others().First(o => o.OwnerClientId != NetworkManager.ServerClientId);
        var otherHealth = other.GetComponent<NetworkPlayerHealth>();

        if (isA)
        {
            // A forged second request for the same shot: the host must count it once.
            Equip("Pistol"); yield return new WaitForSeconds(1.5f);
            ForgedHit(hostHealth, 20f);
            ForgedHit(hostHealth, 20f);
            Equip("Pickaxe");
            // Get beaten up by the host until dead, then RESPAWN NOW.
            yield return WaitFor(() => death.IsDead, 120f);
            Log($"RESULT A died: death screen {me.transform.Find("HotbarCanvas/DeathScreen").gameObject.activeSelf} cursor {Cursor.lockState} " +
                $"button {(me.transform.Find("HotbarCanvas/DeathScreen/RespawnNow") != null)}");
            yield return Capture("A_death_screen_view", null);
            yield return new WaitForSeconds(1.2f);
            float clicked = Time.time;
            me.transform.Find("HotbarCanvas/DeathScreen/RespawnNow").GetComponent<Button>().onClick.Invoke();
            yield return WaitFor(() => !death.IsDead, 8f);
            Log($"RESULT A RESPAWN NOW: back after {Time.time - clicked:F2}s health {attributes.Health} at {me.transform.position} cc {me.GetComponent<CharacterController>().enabled} " +
                $"look {me.GetComponent<PlayerLook>().enabled} hands hidden {me.GetComponent<PlayerEquipment>().Hidden} death screen {me.transform.Find("HotbarCanvas/DeathScreen").gameObject.activeSelf}");

            // Client vs client: A shoots B once, B answers once.
            yield return Place(new Vector3(490f, 0f, 812f), 270f);
            int ticks = HitConfirmFeedback.TicksPlayed;
            Equip("Pistol"); yield return new WaitForSeconds(1.5f);
            Aim(other, 0f); yield return null; Fire(); yield return new WaitForSeconds(0.6f);
            Log($"RESULT A shot B: B health {otherHealth.Health} (expect one pistol hit: 80 chest, 88 arm/leg, 60 head) ticks +{HitConfirmFeedback.TicksPlayed - ticks} number {HitConfirmFeedback.LastNumber}");
            yield return WaitFor(() => attributes.Health < 100f, 30f);

            // Then A kills the host with the rifle (the host is at 80 after the forged-hit check).
            Equip("AssaultRifle"); yield return new WaitForSeconds(1.5f);
            int kills = HitConfirmFeedback.KillSoundsPlayed; ticks = HitConfirmFeedback.TicksPlayed;
            for (int i = 0; i < 8 && !hostHealth.IsDead; i++) { Aim(host, 0f); yield return null; Fire(); yield return new WaitForSeconds(0.3f); }
            yield return new WaitForSeconds(0.4f);
            Log($"RESULT A killed host: dead {hostHealth.IsDead} ticks +{HitConfirmFeedback.TicksPlayed - ticks} (expect 3) kill sounds +{HitConfirmFeedback.KillSoundsPlayed - kills} (expect 1) number {HitConfirmFeedback.LastNumber}");
            Equip("Pickaxe");

            // B kills A next; this time wait for the automatic respawn.
            yield return WaitFor(() => death.IsDead, 90f);
            float diedAt = Time.time;
            yield return WaitFor(() => !death.IsDead, 12f);
            Log($"RESULT A automatic respawn after {Time.time - diedAt:F1}s health {attributes.Health} cc {me.GetComponent<CharacterController>().enabled}");
        }
        else
        {
            yield return WaitFor(() => attributes.Health < 100f, 180f); // A shot us
            yield return new WaitForSeconds(0.5f);
            int ticks = HitConfirmFeedback.TicksPlayed;
            Equip("Pistol"); yield return new WaitForSeconds(1.5f);
            Aim(other, 0f); yield return null; Fire(); yield return new WaitForSeconds(0.6f);
            Log($"RESULT B shot A: A health {otherHealth.Health} (expect one pistol hit: 80 chest, 88 arm/leg, 60 head) ticks +{HitConfirmFeedback.TicksPlayed - ticks} number {HitConfirmFeedback.LastNumber}");
            Equip("Pickaxe");
            yield return WaitFor(() => HeldName(host) == "Crystal", 120f);
            Equip("AssaultRifle"); yield return new WaitForSeconds(1.5f);
            int kills = HitConfirmFeedback.KillSoundsPlayed;
            for (int i = 0; i < 8 && !otherHealth.IsDead; i++) { Aim(other, 0f); yield return null; Fire(); yield return new WaitForSeconds(0.3f); }
            yield return new WaitForSeconds(0.4f);
            Log($"RESULT B killed A: dead {otherHealth.IsDead} kill sounds +{HitConfirmFeedback.KillSoundsPlayed - kills} (expect 1)");
            Equip("Pickaxe");
        }
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 240f);
    }

    private IEnumerator RunPolishLate()
    {
        yield return Join();
        yield return new WaitForSeconds(4f);
        foreach (NetworkPlayerAvatar p in Others())
        {
            var h = p.GetComponent<NetworkPlayerHealth>();
            Log($"RESULT late join sees player {p.OwnerClientId}: health {h.Health} dead {h.IsDead} ragdoll {p.GetComponentInChildren<CharacterRagdoll>(true).IsActive} " +
                $"rendered {p.GetComponentInChildren<SkinnedMeshRenderer>().enabled} held {HeldName(p) ?? "none"}");
        }
        Log($"RESULT late join: blood splats inherited {BloodSplatter.SplatsAlive} (expect 0: no blood history)");
        NetworkSessionManager.Instance.Leave();
        yield return new WaitForSeconds(2f);
    }

    // ================================================================== body-part damage (-mptest zhost|zclient)

    private IEnumerator RunZoneHost()
    {
        Press("HostButton");
        yield return WaitFor(InGame, 60f);
        yield return Place(new Vector3(484f, 0f, 812f), 90f);
        Log("HOST_READY");
        yield return WaitFor(() => Others().Any(), 180f);
        NetworkPlayerAvatar a = Other();
        var aHealth = a.GetComponent<NetworkPlayerHealth>();
        var body = a.GetComponentInChildren<CharacterRagdoll>(true);
        var myAttributes = Me().GetComponent<PlayerAttributes>();

        yield return WaitFor(() => HeldName(a) == "Crystal", 90f);
        Log($"RESULT forged head claim on a knee hit: host health {myAttributes.Health} (expect 88: counted as a leg hit, 20 x 0.6)");
        yield return WaitFor(() => HeldName(a) == null, 30f); // client: empty hands, arms hanging
        yield return new WaitForSeconds(1f);

        Equip("Pistol"); yield return new WaitForSeconds(1.5f);
        var targets = new (string name, System.Func<Vector3> point, BodyZone zone)[]
        {
            ("head", () => HeadCentre(body), BodyZone.Head),
            ("chest", () => Mid(body, HumanBodyBones.UpperChest, HumanBodyBones.Neck), BodyZone.Chest),
            ("arm", () => Mid(body, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand), BodyZone.Arms),
            ("leg", () => Mid(body, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg), BodyZone.Legs),
        };
        foreach (var t in targets)
        {
            float before = aHealth.Health;
            AimAtPoint(t.point()); yield return null; AimAtPoint(t.point()); Fire();
            yield return new WaitForSeconds(0.6f);
            Log($"RESULT pistol {t.name}: {before} -> {aHealth.Health} (-{before - aHealth.Health}; expect -{20f * CombatSettings.Current.Multiplier(t.zone)}) " +
                $"host says {HitConfirmFeedback.LastZone} number {HitConfirmFeedback.LastNumber} | tracer {BulletTracers.LastStart:F2} -> {BulletTracers.LastEnd:F2}");
        }

        yield return PlaceNear(a.transform, 1.1f);
        SelectEmpty(Me().GetComponent<PlayerInventory>());
        yield return new WaitForSeconds(0.6f);
        float beforePunch = aHealth.Health;
        AimAtPoint(HeadCentre(body)); yield return null; AimAtPoint(HeadCentre(body));
        Me().GetComponent<UnarmedAttack>().Punch();
        yield return new WaitForSeconds(0.8f);
        Log($"RESULT punch to the head: {beforePunch} -> {aHealth.Health} (expect -20, kills from 16) dead {aHealth.IsDead} host says {HitConfirmFeedback.LastZone} " +
            $"kill sounds {HitConfirmFeedback.KillSoundsPlayed}");
        yield return WaitFor(() => aHealth.IsDead, 3f);
        yield return WaitFor(() => !aHealth.IsDead, 12f);
        yield return new WaitForSeconds(1.5f);
        yield return Place(new Vector3(484f, 0f, 812f), 90f);
        yield return WaitFor(() => aHealth.Health >= 100f && (a.transform.position - new Vector3(490f, a.transform.position.y, 812f)).sqrMagnitude < 1f, 30f); // respawned, walked back
        yield return new WaitForSeconds(1.5f);

        Equip("AssaultRifle"); yield return new WaitForSeconds(1.5f);
        int heads = HitConfirmFeedback.HeadshotsConfirmed, shots = 0;
        float start = aHealth.Health;
        while (!aHealth.IsDead && shots < 6)
        {
            AimAtPoint(HeadCentre(body)); yield return null; AimAtPoint(HeadCentre(body));
            Camera eye = Me().GetComponentInChildren<Camera>();
            Physics.Raycast(eye.transform.position, eye.transform.forward, out RaycastHit seen, 50f, ~((1 << 6) | (1 << 8)), QueryTriggerInteraction.Ignore);
            float before = aHealth.Health;
            Fire(); shots++;
            yield return new WaitForSeconds(0.35f);
            Log($"rifle shot {shots}: aim ray hits {(seen.collider != null ? seen.collider.name : "nothing")} at {seen.point:F2}, head centre {HeadCentre(body):F2}; " +
                $"{before} -> {aHealth.Health}, host says {HitConfirmFeedback.LastZone} {HitConfirmFeedback.LastNumber} | tracer end {BulletTracers.LastEnd:F2}");
        }
        yield return new WaitForSeconds(0.3f);
        Log($"RESULT rifle headshots from {start}: dead {aHealth.IsDead} after {shots} shots, headshots +{HitConfirmFeedback.HeadshotsConfirmed - heads} " +
            $"(50 each) last number {HitConfirmFeedback.LastNumber}");
        yield return WaitFor(() => !aHealth.IsDead, 12f);
        yield return new WaitForSeconds(2f);
        Log($"RESULT after respawn: hitbox colliders on {body.GetComponentsInChildren<Collider>().Count(c => c.enabled)} (expect 11) layer {body.Head.gameObject.layer}");
        NetworkSessionManager.Instance.Leave();
        yield return new WaitForSeconds(2f);
    }

    private IEnumerator RunZoneClient()
    {
        yield return Join();
        yield return Place(new Vector3(490f, 0f, 812f), 270f);
        Log("CLIENT_READY");
        NetworkPlayerAvatar host = Others().First(o => o.OwnerClientId == NetworkManager.ServerClientId);
        var hostHealth = host.GetComponent<NetworkPlayerHealth>();
        var hostBody = host.GetComponentInChildren<CharacterRagdoll>(true);
        StartCoroutine(WatchRemoteTracers(host));
        Me().GetComponent<PlayerAttributes>().HealthChanged += (h, m) => Log($"own health now {h}");
        yield return new WaitForSeconds(2f);
        Log($"hitboxes on the host's body here: {hostBody.GetComponentsInChildren<Collider>().Count(c => c.enabled)} enabled, head layer {hostBody.Head.gameObject.layer}");

        // A cheat: a hit on the host's knee, reported as a head hit.
        Equip("Pistol"); yield return new WaitForSeconds(1.5f);
        Vector3 knee = Me().GetComponentInChildren<Camera>().transform.position;
        Vector3 point = Mid(hostBody, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg);
        hostHealth.RequestHit(knee, point, (point - knee).normalized, BodyZone.Head);
        yield return new WaitForSeconds(1f);
        Equip("Crystal"); yield return new WaitForSeconds(1.5f);
        SelectEmpty(Me().GetComponent<PlayerInventory>());
        // Killed by the punch: after the respawn, walk back for the rifle headshots.
        var death = Me().GetComponent<PlayerDeath>();
        yield return WaitFor(() => death.IsDead, 120f);
        yield return WaitFor(() => !death.IsDead, 12f);
        yield return Place(new Vector3(490f, 0f, 812f), 270f);
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 240f);
    }

    // ================================================================== strafing seen by others (-mptest shost|sclient)

    private static readonly (string name, Vector2 dir)[] StrafeCases =
    {
        ("A", new Vector2(-1f, 0f)), ("D", new Vector2(1f, 0f)), ("WA", new Vector2(-1f, 1f).normalized), ("WD", new Vector2(1f, 1f).normalized),
        ("SA", new Vector2(-1f, -1f).normalized), ("SD", new Vector2(1f, -1f).normalized), ("W", new Vector2(0f, 1f)), ("S", new Vector2(0f, -1f)),
    };

    private IEnumerator RunStrafeHost()
    {
        Press("HostButton");
        yield return WaitFor(InGame, 60f);
        yield return Place(new Vector3(484f, 0f, 812f), 90f);
        Log("HOST_READY");
        yield return WaitFor(() => Others().Any(), 180f);
        NetworkPlayerAvatar a = Other();
        Animator body = a.GetComponentInChildren<Animator>(true);
        var presentation = a.GetComponent<RemotePlayerPresentation>();
        bool shotLeft = false, shotRight = false;
        float end = Time.time + 60f;
        string last = "";
        while (Time.time < end && Others().Any())
        {
            float speed = body.GetFloat("Speed"), mx = body.GetFloat("MoveX"), my = body.GetFloat("MoveY");
            if (speed > 3f)
            {
                AnimatorStateInfo st = body.GetCurrentAnimatorStateInfo(0);
                string state = new[] { "Idle", "Walk", "Run", "Walk Backward", "Jump", "Fall", "Land" }.FirstOrDefault(n => st.IsName(n)) ?? "other";
                string line = $"client body: MoveX {mx:F2} MoveY {my:F2} state {state} speed {speed:F1} held {HeldName(a) ?? "none"} shotTime {presentation.ShotTime:F2} reload {presentation.ReloadProgress:F2}";
                string key = $"{Mathf.Round(mx * 2f)}/{Mathf.Round(my * 2f)}/{state}";
                if (key != last) { Log("RESULT " + line); last = key; } // one line per direction/state change
                if (!shotLeft && mx < -0.95f) { shotLeft = true; yield return Capture("host_sees_client_strafe_left", a.transform); }
                if (!shotRight && mx > 0.95f) { shotRight = true; yield return Capture("host_sees_client_strafe_right", a.transform); }
            }
            yield return new WaitForSeconds(0.1f);
        }
        NetworkSessionManager.Instance.Leave();
        yield return new WaitForSeconds(2f);
    }

    private IEnumerator RunStrafeClient()
    {
        yield return Join();
        Vector3 start = new Vector3(490f, 0f, 812f);
        yield return Place(start, 270f);
        Log("CLIENT_READY");
        Equip("Pistol");
        yield return new WaitForSeconds(3f);
        Animator mine = Me().GetComponentInChildren<CharacterAnimator>().GetComponent<Animator>();
        foreach (var c in StrafeCases)
        {
            walkDir = c.dir; walkSpeed = 4f;
            yield return new WaitForSeconds(0.9f);
            var gun = Me().GetComponent<PlayerEquipment>().ActiveController as WeaponController;
            if (c.name == "D") { Fire(); yield return new WaitForSeconds(0.25f); Fire(); }          // shoot while strafing right
            if (c.name == "A" && gun != null) { Fire(); gun.StartReload(); }                         // reload while strafing left
            yield return new WaitForSeconds(0.4f);
            Log($"RESULT client {c.name,-2}: own MoveX {mine.GetFloat("MoveX"):F2} MoveY {mine.GetFloat("MoveY"):F2} speed {mine.GetFloat("Speed"):F1} facing {Me().transform.eulerAngles.y:F0}");
            walkSpeed = 0f;
            yield return new WaitForSeconds(0.6f);
            yield return Place(start, 270f);
            yield return new WaitForSeconds(0.4f);
        }
        Log("STRAFES_DONE");
        yield return new WaitForSeconds(2f);
        NetworkSessionManager.Instance.Leave();
        yield return new WaitForSeconds(2f);
    }

    /// <summary>Logs every tracer this computer draws for the shooter's shots (start vs the shooter's gun hand, end).</summary>
    private IEnumerator WatchRemoteTracers(NetworkPlayerAvatar shooter)
    {
        int seen = BulletTracers.Played;
        Animator body = shooter.GetComponentInChildren<Animator>(true);
        while (shooter != null)
        {
            if (BulletTracers.Played != seen)
            {
                seen = BulletTracers.Played;
                Transform hand = body.GetBoneTransform(HumanBodyBones.RightHand);
                Log($"remote tracer #{seen}: {BulletTracers.LastStart:F2} -> {BulletTracers.LastEnd:F2} (start {Vector3.Distance(BulletTracers.LastStart, hand.position):F2} m from the shooter's gun hand, " +
                    $"active {BulletTracers.Active}, pool {BulletTracers.Pooled})");
            }
            yield return null;
        }
    }

    private static Vector3 HeadCentre(CharacterRagdoll body)
    {
        Animator anim = body.GetComponent<Animator>();
        Transform head = anim.GetBoneTransform(HumanBodyBones.Head), neck = anim.GetBoneTransform(HumanBodyBones.Neck);
        return head.position + (head.position - neck.position).normalized * 0.1f;
    }

    private static Vector3 Mid(CharacterRagdoll body, HumanBodyBones a, HumanBodyBones b)
    {
        Animator anim = body.GetComponent<Animator>();
        Transform ta = anim.GetBoneTransform(a) ?? anim.GetBoneTransform(HumanBodyBones.Chest);
        return (ta.position + anim.GetBoneTransform(b).position) * 0.5f;
    }

    /// <summary>Turns this player (yaw) and camera (pitch) so the crosshair is on a world point.</summary>
    private void AimAtPoint(Vector3 point)
    {
        PlayerMovement me = Me();
        Camera cam = me.GetComponentInChildren<Camera>();
        Vector3 d = point - cam.transform.position;
        me.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f);
        lookPitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        cam.transform.localRotation = Quaternion.Euler(lookPitch, 0f, 0f);
    }

    // ------------------------------------------------------------------ actions

    /// <summary>What a cheating client could send: a hit on that player's copy without our weapon's ray.</summary>
    private static void ForgedHit(NetworkPlayerHealth target, float damage)
    {
        Physics.Raycast(target.transform.position + Vector3.up * 1.2f + Vector3.right * 2f, Vector3.left, out RaycastHit hit, 4f);
        if (hit.collider == null) { Log("forged hit: no collider found, using a made-up hit"); }
        Log($"STEP forged hit {damage} on player {target.OwnerClientId}");
        target.TakeDamage(damage, hit);
    }

    private void Fire()
    {
        var weapon = Me().GetComponent<PlayerEquipment>().ActiveController as WeaponController;
        if (weapon != null) weapon.SendMessage("Fire"); // what a click does
        else Log("FIRE: no gun in hand");
    }

    /// <summary>Turns this player (yaw) and the camera (pitch) to the target's body (+ yawOffset degrees to miss).</summary>
    private void Aim(NetworkPlayerAvatar target, float yawOffset)
    {
        PlayerMovement me = Me();
        Camera cam = me.GetComponentInChildren<Camera>();
        Collider body = target.GetComponent<Collider>();
        Vector3 point = body != null && body.enabled ? body.bounds.center + Vector3.up * 0.1f : target.transform.position + Vector3.up * 1.1f;
        Vector3 d = point - cam.transform.position;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + yawOffset;
        me.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        lookPitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        cam.transform.localRotation = Quaternion.Euler(lookPitch, 0f, 0f);
    }

    // Batch mode has no keyboard / mouse: stand in for gravity (and a strafe), and hold the look pitch.
    private void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current != null) return;
        PlayerMovement me = FindAnyObjectByType<PlayerMovement>();
        if (me == null) return;
        if (me.TryGetComponent(out CharacterController cc) && cc.enabled)
            cc.Move((Vector3.down * 2f + me.transform.right * strafeSpeed + (me.transform.right * walkDir.x + me.transform.forward * walkDir.y) * walkSpeed) * Time.deltaTime);
        var death = me.GetComponent<PlayerDeath>();
        Camera cam = me.GetComponentInChildren<Camera>();
        if (cam != null && (death == null || !death.IsDead)) cam.transform.localRotation = Quaternion.Euler(lookPitch, 0f, 0f);
    }

    private static void LogLimbSpeeds(CharacterRagdoll ragdoll, string label)
    {
        if (ragdoll == null) return;
        float fastest = 0f, lowest = float.MaxValue;
        foreach (Rigidbody rb in ragdoll.GetComponentsInChildren<Rigidbody>())
        {
            if (rb.isKinematic) continue;
            fastest = Mathf.Max(fastest, rb.linearVelocity.magnitude);
            lowest = Mathf.Min(lowest, rb.position.y);
        }
        Log($"RESULT {label}: fastest part {fastest:F2} m/s, lowest part y {lowest:F2}, hips {(ragdoll.Hips != null ? ragdoll.Hips.position.ToString("F2") : "?")}");
    }

    private static int Blood() => FindObjectsByType<ParticleSystem>().Count(p => p.name.StartsWith("BloodHit"));

    private static string HeldName(NetworkPlayerAvatar who)
    {
        var p = who.GetComponent<RemotePlayerPresentation>();
        return p != null && p.HeldItem != null ? p.HeldItem.name : null;
    }

    private static PlayerMovement Me() => FindAnyObjectByType<PlayerMovement>();

    private static ItemData Item(string name)
    {
        for (int i = 0; i < 64; i++)
        {
            ItemData d = NetworkWorld.Instance.ItemAt(i);
            if (d == null) break;
            if (d.name == name) return d;
        }
        return null;
    }

    private static void Equip(string name)
    {
        var inventory = Me().GetComponent<PlayerInventory>();
        ItemData item = Item(name);
        if (inventory.Count(item) == 0) inventory.AddItem(item, 1);
        for (int s = 0; s < inventory.SlotCount; s++)
            if (inventory.Slots[s].item == item) { inventory.SelectSlot(s); return; }
    }

    private static void SelectEmpty(PlayerInventory inventory)
    {
        for (int s = 0; s < inventory.SlotCount; s++)
            if (inventory.Slots[s].IsEmpty) { inventory.SelectSlot(s); return; }
    }

    // ------------------------------------------------------------------ capturing

    /// <summary>Renders what this player sees and (if given) a side view of the target to PNGs.</summary>
    private IEnumerator Capture(string label, Transform target)
    {
        int n = ++shotCount;
        Camera view = Camera.main;
        yield return new WaitForEndOfFrame();
        if (view != null) Save(view, null); // warm-up: skinned meshes seen for the first time render stale
        yield return new WaitForEndOfFrame();
        if (view != null) Save(view, $"{n:00}_{label}_view.png");
        if (target == null) yield break;
        var go = new GameObject("SideCamera");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 45f;
        cam.cullingMask = ~(1 << 6);
        go.transform.position = target.position + Vector3.right * 2.2f + Vector3.forward * 1.2f + Vector3.up * 1.6f;
        go.transform.LookAt(target.position + Vector3.up * 0.5f);
        yield return new WaitForEndOfFrame();
        Save(cam, null);
        yield return new WaitForEndOfFrame();
        Save(cam, $"{n:00}_{label}_side.png");
        Destroy(go);
    }

    private void Save(Camera cam, string file)
    {
        var rt = RenderTexture.GetTemporary(960, 540, 24);
        RenderTexture old = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = old;
        RenderTexture.active = rt;
        var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);
        if (file != null) File.WriteAllBytes(Path.Combine(shots, file), tex.EncodeToPNG());
        Destroy(tex);
    }

    // ------------------------------------------------------------------ helpers

    private IEnumerator Join()
    {
        for (int attempt = 0; attempt < 20 && !InGame(); attempt++)
        {
            yield return WaitFor(() => FindAnyObjectByType<MultiplayerMenu>() != null, 30f);
            yield return new WaitForSeconds(1f);
            GameObject.Find("JoinCodeField").GetComponent<InputField>().text = "127.0.0.1";
            Press("JoinButton");
            yield return WaitFor(() => InGame() || NetworkSessionManager.Instance.Current == NetworkSessionManager.Phase.Offline, 60f);
            if (!InGame()) yield return new WaitForSeconds(3f);
        }
        yield return new WaitForSeconds(2f);
    }

    /// <summary>Puts this game's player on the cave floor at (x, z), facing yaw.</summary>
    private static IEnumerator Place(Vector3 xz, float yaw)
    {
        yield return WaitFor(() => Me() != null, 30f);
        yield return new WaitForSeconds(1f);
        PlayerMovement me = Me();
        Vector3 start = new Vector3(xz.x, 14f, xz.z);
        int mask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));
        RaycastHit[] hits = Physics.RaycastAll(start, Vector3.down, 30f, mask, QueryTriggerInteraction.Ignore);
        float y = hits.Length > 0 ? hits.Where(h => h.point.y < 12f).Select(h => h.point.y).DefaultIfEmpty(7.3f).Max() : 7.3f;
        Teleport(me, new Vector3(xz.x, y + 0.05f, xz.z), yaw);
    }

    /// <summary>Puts this game's player on the ground 'distance' metres from the target, facing it.</summary>
    private static IEnumerator PlaceNear(Transform target, float distance)
    {
        PlayerMovement me = Me();
        Vector3 spot = target.position + Vector3.right * distance;
        int mask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));
        if (Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 10f, mask, QueryTriggerInteraction.Ignore)) spot = ground.point;
        Teleport(me, spot + Vector3.up * 0.05f, 270f);
        yield return new WaitForSeconds(0.5f);
    }

    private static void Teleport(PlayerMovement me, Vector3 feet, float yaw)
    {
        var cc = me.GetComponent<CharacterController>();
        cc.enabled = false;
        me.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
        cc.enabled = true;
        Log($"placed at {me.transform.position} yaw {yaw}");
    }

    private static IEnumerable<NetworkPlayerAvatar> Others() =>
        FindObjectsByType<NetworkPlayerAvatar>().Where(a => a.IsSpawned && !a.IsOwner);

    private static NetworkPlayerAvatar Other() => Others().FirstOrDefault();

    private static bool InGame() => NetworkSessionManager.Instance != null &&
                                    NetworkSessionManager.Instance.Current == NetworkSessionManager.Phase.InGame;

    private static void Press(string button)
    {
        GameObject go = GameObject.Find(button);
        if (go != null) go.GetComponent<Button>().onClick.Invoke();
    }

    private static IEnumerator WaitFor(System.Func<bool> condition, float seconds)
    {
        float giveUp = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < giveUp) yield return null;
    }
}
