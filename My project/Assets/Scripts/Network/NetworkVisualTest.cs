using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// DEVELOPMENT TEST ONLY. Does nothing unless the game is started with "-mptest vhost", "vclient" or "vlate"
/// (plus "-shots &lt;folder&gt;"). Three real game processes (batch mode, rendering on) check what players SEE of each
/// other: one player acts (headlamp, every item, swings, shots, reloads, empty hands, punches) while the other
/// renders its own view and a side view to PNGs at the key moments; then they swap; then a late joiner checks
/// that the headlamp and held item are right on arrival. Actions are triggered through the same methods the
/// input code calls (no keyboard/mouse in batch mode).
/// </summary>
public class NetworkVisualTest : MonoBehaviour
{
    private string role, shots;
    private int shotCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string role = Arg("-mptest");
        if (role != "vhost" && role != "vclient" && role != "vlate") return;
        var go = new GameObject("NetworkVisualTest");
        DontDestroyOnLoad(go);
        var t = go.AddComponent<NetworkVisualTest>();
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

    private static void Log(string s) => Debug.Log("[VTEST] " + s);

    private IEnumerator Start()
    {
        Application.targetFrameRate = 30;
        yield return WaitFor(() => FindAnyObjectByType<MultiplayerMenu>() != null, 30f);
        yield return new WaitForSeconds(1f);
        if (role == "vhost")
        {
            Press("HostButton");
            yield return WaitFor(InGame, 60f);
            yield return Place(new Vector3(484f, 0f, 812f), 90f);
            Log("HOST_READY");
            yield return WaitFor(() => Others().Any(), 180f);
            yield return new WaitForSeconds(4f);
            yield return Act(full: true);
            Log("host: watching the client");
            yield return Watch(Other(), until: p => p.HeldItem != null && p.HeldItem.name == "Crystal", seconds: 120f, prefix: "p1sees_");
            // State for the late joiner: lamp on, rifle in hand.
            Lamp(true);
            Equip("AssaultRifle");
            yield return new WaitForSeconds(1f);
            Log("READY_FOR_LATE");
            yield return WaitFor(() => Others().Count() >= 2, 120f);
            yield return WaitFor(() => Others().Count() < 2, 90f);
            yield return new WaitForSeconds(1f);
            NetworkSessionManager.Instance.Leave();
        }
        else if (role == "vclient")
        {
            yield return Join();
            yield return Place(new Vector3(488.5f, 0f, 812f), 270f);
            Log("CLIENT_READY");
            yield return Watch(Other(), until: p => p.HeldItem != null && p.HeldItem.name == "Crystal", seconds: 180f, prefix: "p2sees_");
            yield return new WaitForSeconds(2f);
            yield return Act(full: false);
            yield return WaitFor(() => SceneManager.GetActiveScene().name == "MainMenu", 200f);
        }
        else // vlate
        {
            yield return Join();
            yield return Place(new Vector3(487f, 0f, 815.5f), 225f);
            yield return new WaitForSeconds(3f);
            NetworkPlayerAvatar host = Others().FirstOrDefault(a => a.OwnerClientId == NetworkManager.ServerClientId);
            var p = host != null ? host.GetComponent<RemotePlayerPresentation>() : null;
            var lamp = host != null ? host.GetComponent<Headlamp>() : null;
            Log($"LATE JOIN sees host: held={(p != null && p.HeldItem != null ? p.HeldItem.name : "none")} " +
                $"headlampOn={(lamp != null && lamp.IsOn)} lightEnabled={(lamp != null && lamp.Light != null && lamp.Light.enabled)}");
            yield return Capture("late_sees_host", host != null ? host.transform : null);
            NetworkSessionManager.Instance.Leave();
            yield return new WaitForSeconds(2f);
        }
        Log("DONE");
        Application.Quit();
    }

    // Batch mode has no keyboard, so PlayerMovement never runs and the player would never be grounded (its own
    // Animator would sit in Fall). Stand in for its gravity step.
    private void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current != null) return;
        PlayerMovement me = FindAnyObjectByType<PlayerMovement>();
        if (me != null && me.TryGetComponent(out CharacterController cc) && cc.enabled)
            cc.Move((Vector3.down * 2f + me.transform.right * strafeSpeed) * Time.deltaTime);
        // No mouse either: PlayerLook leaves the camera alone, so the test sets the look pitch itself.
        Camera cam = me != null ? me.GetComponentInChildren<Camera>() : null;
        if (cam != null) cam.transform.localRotation = Quaternion.Euler(lookPitch, 0f, 0f);
    }

    private float strafeSpeed, lookPitch;

    // ------------------------------------------------------------------ acting (same calls as the input code)

    private IEnumerator Act(bool full)
    {
        if (Arg("-vscenario") == "swingreload") { yield return ActSwingReload(full); yield break; }
        PlayerMovement me = FindAnyObjectByType<PlayerMovement>();
        var inventory = me.GetComponent<PlayerInventory>();
        foreach (string n in new[] { "Pickaxe", "Hammer", "Pistol", "AssaultRifle", "CopperOre", "Crystal" })
            if (inventory.Count(Item(n)) == 0) inventory.AddItem(Item(n), 1);

        Step("lamp ON"); Lamp(true); yield return new WaitForSeconds(2f);
        Step("lamp OFF"); Lamp(false); yield return new WaitForSeconds(2f);
        Step("lamp ON"); Lamp(true); yield return new WaitForSeconds(1.5f);

        foreach (string tool in full ? new[] { "Pickaxe", "Hammer" } : new string[0])
        {
            Step("equip " + tool); Equip(tool); yield return new WaitForSeconds(1.5f);
            Step("swing " + tool);
            var mining = me.GetComponent<PlayerEquipment>().ActiveController as MiningToolController;
            if (mining != null && mining.Swing != null) mining.Swing.Swing();
            yield return new WaitForSeconds(1.8f);
        }
        foreach (string gun in full ? new[] { "Pistol", "AssaultRifle" } : new[] { "AssaultRifle" })
        {
            Step("equip " + gun); Equip(gun); yield return new WaitForSeconds(1.5f);
            var weapon = me.GetComponent<PlayerEquipment>().ActiveController as WeaponController;
            int shotsToFire = gun == "Pistol" ? 2 : 3;
            for (int i = 0; i < shotsToFire; i++)
            {
                Step("fire " + gun);
                if (weapon != null) weapon.SendMessage("Fire");
                yield return new WaitForSeconds(gun == "Pistol" ? 0.6f : 0.12f);
            }
            yield return new WaitForSeconds(0.8f);
            Step("reload " + gun);
            if (weapon != null) weapon.StartReload();
            yield return new WaitForSeconds(gun == "Pistol" ? 2.3f : 2.9f);
        }
        if (full) { Step("equip CopperOre"); Equip("CopperOre"); yield return new WaitForSeconds(1.5f); }

        Step("empty hands"); SelectEmpty(inventory); yield return new WaitForSeconds(1.5f);
        var equipment = me.GetComponent<PlayerEquipment>();
        var fists = equipment.UnarmedView != null ? equipment.UnarmedView.GetComponent<FistsController>() : null;
        for (int i = 0; i < 2; i++)
        {
            Step("punch " + (i + 1));
            equipment.ShowUnarmedAction(true);
            if (fists != null) fists.Punch();
            yield return new WaitForSeconds(0.9f);
        }
        Step("end marker (Crystal)"); Equip("Crystal"); yield return new WaitForSeconds(1.5f);
    }

    /// <summary>Every pickaxe swing at three look pitches, then pistol + rifle reloads standing and the rifle reload walking.</summary>
    private IEnumerator ActSwingReload(bool full)
    {
        PlayerMovement me = FindAnyObjectByType<PlayerMovement>();
        var inventory = me.GetComponent<PlayerInventory>();
        foreach (string n in new[] { "Pickaxe", "Hammer", "Pistol", "AssaultRifle", "Crystal" })
            if (inventory.Count(Item(n)) == 0) inventory.AddItem(Item(n), 1);
        Lamp(true);

        Step("equip Pickaxe"); Equip("Pickaxe"); yield return new WaitForSeconds(1.5f);
        var swing = (me.GetComponent<PlayerEquipment>().ActiveController as MiningToolController)?.Swing;
        foreach (float pitch in full ? new[] { 0f, -25f, 25f } : new[] { 0f })
        {
            lookPitch = pitch;
            yield return new WaitForSeconds(0.8f);
            for (int i = 0; i < 3; i++)
            {
                Step($"swing {swing.NextSwingName} at pitch {pitch}");
                swing.Swing();
                yield return new WaitForSeconds(1.9f);
            }
        }
        lookPitch = 0f;

        if (full)
        {
            Step("swing Pickaxe while walking"); strafeSpeed = 1.5f;
            swing.Swing(); yield return new WaitForSeconds(1.9f);
            strafeSpeed = 0f;

            Step("equip Hammer"); Equip("Hammer"); yield return new WaitForSeconds(1.5f);
            var hammer = (me.GetComponent<PlayerEquipment>().ActiveController as MiningToolController)?.Swing;
            for (int i = 0; i < 3; i++) { Step($"swing Hammer {hammer.NextSwingName}"); hammer.Swing(); yield return new WaitForSeconds(2.1f); }
            // Two swings back to back: the second starts during the first's recovery.
            Step("Hammer swings back to back"); hammer.Swing(); yield return new WaitForSeconds(0.95f); hammer.Swing(); yield return new WaitForSeconds(2.1f);
        }

        foreach (string gun in full ? new[] { "Pistol", "AssaultRifle" } : new[] { "AssaultRifle" })
        {
            Step("equip " + gun); Equip(gun); yield return new WaitForSeconds(1.5f);
            if (gun == "AssaultRifle")
                foreach (float pitch in new[] { -15f, 15f, 40f, 0f }) { Step($"rifle look pitch {pitch}"); lookPitch = pitch; yield return new WaitForSeconds(1.6f); }
            var weapon = me.GetComponent<PlayerEquipment>().ActiveController as WeaponController;
            weapon.SendMessage("Fire");
            yield return new WaitForSeconds(0.8f);
            Step("reload " + gun + " standing");
            weapon.StartReload();
            yield return new WaitForSeconds(gun == "Pistol" ? 2.6f : 3.2f);
        }
        if (full)
        {
            var rifle = me.GetComponent<PlayerEquipment>().ActiveController as WeaponController;
            rifle.SendMessage("Fire");
            yield return new WaitForSeconds(0.6f);
            Step("reload AssaultRifle walking, looking down");
            strafeSpeed = 1.5f; lookPitch = 20f;
            rifle.StartReload();
            yield return new WaitForSeconds(3.2f);
            strafeSpeed = 0f; lookPitch = 0f;
        }
        Step("end marker (Crystal)"); Equip("Crystal"); yield return new WaitForSeconds(1.5f);
    }

    private static void Step(string s) => Log("STEP " + s + " t=" + Time.time.ToString("F2"));

    private static void Lamp(bool on) => FindAnyObjectByType<PlayerMovement>().GetComponent<Headlamp>().SetOn(on);

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
        var inventory = FindAnyObjectByType<PlayerMovement>().GetComponent<PlayerInventory>();
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

    // ------------------------------------------------------------------ watching + capturing

    // Swing moments as fractions of the time to impact; reload moments as reload progress (first-person phases:
    // hand reaches 0.08, magazine out 0.24, gone 0.44, new magazine 0.56, in 0.76, rack 0.82, ready 0.9).
    private static readonly float[] SwingMoments = { 0.35f, 0.75f, 1.0f, 1.45f, 1.85f };
    private static readonly string[] SwingMomentNames = { "a_windup", "b_top", "c_strike", "d_follow", "e_return" };
    private static readonly float[] ReloadMoments = { 0.12f, 0.32f, 0.5f, 0.68f, 0.84f, 0.97f };
    private static readonly string[] ReloadMomentNames = { "a_reach", "b_magOut", "c_magAway", "d_newMag", "e_rack", "f_ready" };
    private int swingStage, swingCount, reloadStage, reloadCount;
    private float shownPitch;

    private IEnumerator Watch(NetworkPlayerAvatar target, System.Func<RemotePlayerPresentation, bool> until, float seconds, string prefix)
    {
        if (target == null) { Log("no other player to watch"); yield break; }
        var p = target.GetComponent<RemotePlayerPresentation>();
        var lamp = target.GetComponent<Headlamp>();
        string held = "?";
        bool lampOn = lamp.IsOn;
        float lastPunch = -1f, lastSwing = -1f, lastShot = -1f, lastReload = -1f;
        bool punchShot = false;
        float giveUp = Time.time + seconds;
        while (Time.time < giveUp && target != null)
        {
            string h = p.HeldItem != null ? p.HeldItem.name : "none";
            if (h != held)
            {
                held = h;
                Log($"{prefix}held -> {h}");
                yield return new WaitForSeconds(0.7f);
                yield return Capture($"{prefix}held_{h}", target.transform);
                if (until(p)) break;
                continue;
            }
            if (lamp.IsOn != lampOn)
            {
                lampOn = lamp.IsOn;
                Log($"{prefix}headlamp -> {(lampOn ? "ON" : "OFF")} light={lamp.Light.enabled}");
                yield return new WaitForSeconds(0.3f);
                yield return Capture($"{prefix}lamp_{(lampOn ? "on" : "off")}", target.transform);
                continue;
            }
            if (Mathf.Abs(Mathf.DeltaAngle(p.Pitch, shownPitch)) > 8f && p.SwingTime < 0f && p.ReloadProgress < 0f)
            {
                shownPitch = p.Pitch;
                yield return new WaitForSeconds(0.5f);
                Log($"{prefix}look pitch {p.Pitch:F0} holding {(p.HeldItem != null ? p.HeldItem.name : "none")} headPush={p.LastHeadPush * 100f:F1}cm");
                yield return Capture($"{prefix}aim_{(p.HeldItem != null ? p.HeldItem.name : "none")}_pitch{p.Pitch:F0}", target.transform);
                continue;
            }
            if (p.PunchTime >= 0f && lastPunch < 0f) punchShot = false;
            if (p.PunchTime >= 0.19f && !punchShot) { punchShot = true; Log($"{prefix}punch seen"); yield return Capture($"{prefix}punch", target.transform); }
            lastPunch = p.PunchTime;

            if (p.SwingTime < 0f && lastSwing >= 0f)
            {
                lastSwing = -1f;
                yield return new WaitForSeconds(0.08f);
                yield return Capture($"{prefix}swing{swingCount:00}_f_backToHold", target.transform);
                continue;
            }
            if (p.SwingTime >= 0f && lastSwing < 0f) { swingStage = 0; swingCount++; Log($"{prefix}swing {p.SwingKind} seen (impact at {p.SwingImpactTime:F2}s, pitch {p.Pitch:F0})"); }
            if (p.SwingTime >= 0f && swingStage < SwingMoments.Length && p.SwingTime >= p.SwingImpactTime * SwingMoments[swingStage])
            {
                string[] kinds = { "right", "left", "overhead" };
                string label = $"{prefix}swing{swingCount:00}_{kinds[Mathf.Clamp(p.SwingKind, 0, 2)]}_pitch{p.Pitch:F0}_{SwingMomentNames[swingStage]}";
                swingStage++;
                Log($"{label}: t={p.SwingTime:F2} headPush={p.LastHeadPush * 100f:F1}cm");
                yield return Capture(label, target.transform);
                lastSwing = p.SwingTime;
                continue;
            }
            lastSwing = p.SwingTime;

            if (p.ShotTime >= 0f && (lastShot < 0f || p.ShotTime < lastShot)) { Log($"{prefix}shot seen"); yield return Capture($"{prefix}shot", target.transform); }
            lastShot = p.ShotTime;

            if (p.ReloadProgress >= 0f && lastReload < 0f) { reloadStage = 0; reloadCount++; Log($"{prefix}reload seen ({(p.HeldItem != null ? p.HeldItem.name : "?")})"); }
            if (p.ReloadProgress >= 0f && reloadStage < ReloadMoments.Length && p.ReloadProgress >= ReloadMoments[reloadStage])
            {
                string label = $"{prefix}reload{reloadCount}_{(p.HeldItem != null ? p.HeldItem.name : "?")}_{ReloadMomentNames[reloadStage]}";
                reloadStage++;
                Log($"{label}: progress={p.ReloadProgress:F2}");
                yield return Capture(label, target.transform);
                lastReload = p.ReloadProgress;
                continue;
            }
            lastReload = p.ReloadProgress;
            yield return null;
        }
    }

    /// <summary>Renders what this player sees, and (next frame) a side view of the target, to PNGs.</summary>
    private IEnumerator Capture(string label, Transform target)
    {
        int n = ++shotCount;
        if (target != null)
        {
            Animator a = target.GetComponentInChildren<Animator>();
            AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(0);
            string state = new[] { "Idle", "Walk", "Run", "Walk Backward", "Jump", "Fall", "Land" }.FirstOrDefault(s => st.IsName(s)) ?? "other";
            Log($"capture {label}: remote anim={state} speed={a.GetFloat("Speed"):F2} grounded={a.GetBool("Grounded")}");
        }
        // Batch mode renders nothing on its own, so a skinned mesh seen for the first time would show stale skinning:
        // render once to mark it visible, then save the next frame's render.
        Camera view = Camera.main;
        yield return new WaitForEndOfFrame();
        if (view != null) Save(view, null);
        yield return new WaitForEndOfFrame();
        if (view != null) Save(view, $"{n:00}_{label}_view.png");
        if (target == null) yield break;
        yield return null;
        var go = new GameObject("SideCamera");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 45f;
        cam.cullingMask = ~(1 << 6);
        go.transform.position = target.position + target.right * 1.9f + target.forward * 1.6f + Vector3.up * 1.55f;
        go.transform.LookAt(target.position + Vector3.up * 1.15f + target.forward * 0.3f);
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

    /// <summary>Puts this game's player on the ground at (x, z), facing yaw.</summary>
    private static IEnumerator Place(Vector3 xz, float yaw)
    {
        yield return WaitFor(() => FindAnyObjectByType<PlayerMovement>() != null, 30f);
        yield return new WaitForSeconds(1f); // after NetworkPlayerAvatar has put us at our spawn spot
        PlayerMovement me = FindAnyObjectByType<PlayerMovement>();
        if (me == null) { Log("PLACE FAILED: not in the game"); yield break; }
        Vector3 start = new Vector3(xz.x, 14f, xz.z);
        int mask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));
        RaycastHit[] hits = Physics.RaycastAll(start, Vector3.down, 30f, mask, QueryTriggerInteraction.Ignore);
        float y = hits.Length > 0 ? hits.Where(h => h.point.y < 12f).Select(h => h.point.y).DefaultIfEmpty(7.3f).Max() : 7.3f;
        var cc = me.GetComponent<CharacterController>();
        cc.enabled = false;
        me.transform.SetPositionAndRotation(new Vector3(xz.x, y + 0.05f, xz.z), Quaternion.Euler(0f, yaw, 0f));
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
