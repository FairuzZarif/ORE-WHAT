using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Opt-in development review using the real empty-hands attack and actual scene colliders.</summary>
public sealed class HardPunchAcceptanceTest : MonoBehaviour
{
    public string role = "sp", output;
    public bool Complete { get; private set; }
    public int Failures { get; private set; }
    PlayerMovement player;
    PlayerEquipment equipment;
    PlayerInventory inventory;
    Camera eye;
    Vector3 savedPosition, aim;
    Quaternion savedRotation, savedEyeRotation;
    bool savedMove, savedLook, aiming;
    int savedSlot;
    readonly List<ItemData> added = new List<ItemData>();
    readonly List<GameObject> temporary = new List<GameObject>();
    readonly Dictionary<RockHealth, int> savedHealth = new Dictionary<RockHealth, int>();
    const int HitMask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));
    static readonly string[] ResourcesToTest = { "CopperOre", "IronOre", "GoldOre", "Crystal" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!Debug.isDebugBuild && !Application.isEditor) return;
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-punchtest");
        if (i < 0 || i + 1 >= args.Length) return;
        var go = new GameObject("HardPunchAcceptanceTest"); DontDestroyOnLoad(go);
        var test = go.AddComponent<HardPunchAcceptanceTest>(); test.role = args[i + 1];
        i = Array.IndexOf(args, "-punchreview");
        if (i >= 0 && i + 1 < args.Length) test.output = args[i + 1];
    }

    void Check(bool ok, string text)
    {
        if (!ok) Failures++;
        string line = "[PUNCHTEST] " + role + " " + (ok ? "PASS " : "FAIL ") + text;
        Debug.Log(line);
        File.AppendAllText(Path.Combine(output, role + "-results.txt"), line + Environment.NewLine);
    }

    IEnumerator Until(Func<bool> condition, string label, float timeout = 90f)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), label);
    }
    void Signal(string phase) => File.WriteAllText(Path.Combine(output, role + "-" + phase + ".signal"), "ready");
    IEnumerator Wait(string other, string phase) => Until(() => File.Exists(Path.Combine(output, other + "-" + phase + ".signal")), other + " " + phase);

    IEnumerator Start()
    {
        if (string.IsNullOrEmpty(output)) output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/HardPunchReview/PlayMode"));
        Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, role + "-results.txt"), "");
        Application.targetFrameRate = 60;
        if (role != "sp")
        {
            yield return Until(() => FindAnyObjectByType<MultiplayerMenu>() != null, "main menu");
            if (role == "client") GameObject.Find("JoinCodeField").GetComponent<InputField>().text = "127.0.0.1";
            GameObject.Find(role == "host" ? "HostButton" : "JoinButton").GetComponent<Button>().onClick.Invoke();
            yield return Until(() => FindAnyObjectByType<PlayerMovement>() != null && NetworkWorld.Instance != null, "loaded world");
            yield return new WaitForSeconds(2f);
        }
        player = FindAnyObjectByType<PlayerMovement>(); equipment = player.GetComponent<PlayerEquipment>();
        inventory = player.GetComponent<PlayerInventory>();
        eye = player.GetComponentsInChildren<Camera>().First(c => c.name == "PlayerCamera");
        savedPosition = player.transform.position; savedRotation = player.transform.rotation; savedEyeRotation = eye.transform.localRotation;
        savedMove = player.enabled; savedLook = player.GetComponent<PlayerLook>().enabled; savedSlot = inventory.SelectedSlot;
        player.enabled = false; player.GetComponent<PlayerLook>().enabled = false;
        if (role == "sp") yield return SinglePlayer(); else yield return Multiplayer();
        aiming = false;
        if (role == "sp") foreach (var state in savedHealth) if (state.Key != null) state.Key.ShowNetworkHit(state.Value, false);
        foreach (var go in temporary) if (go != null) Destroy(go);
        foreach (var item in added)
            for (int i = 0; i < inventory.SlotCount; i++) if (inventory.Slots[i].item == item) inventory.RemoveFromSlot(i, 1);
        inventory.SelectSlot(savedSlot); Move(savedPosition); player.transform.rotation = savedRotation; eye.transform.localRotation = savedEyeRotation;
        player.enabled = savedMove; player.GetComponent<PlayerLook>().enabled = savedLook;
        Check(Failures == 0, "COMPLETE failures=" + Failures); Complete = true; Signal("done");
        if (role != "sp") { yield return new WaitForSeconds(2f); Application.Quit(); }
    }

    void LateUpdate() { if (aiming && eye != null) Aim(); }
    void Aim()
    {
        Vector3 direction = (aim - eye.transform.position).normalized;
        player.transform.rotation = Quaternion.Euler(0, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, 0);
        eye.transform.localRotation = Quaternion.Euler(-Mathf.Asin(direction.y) * Mathf.Rad2Deg, 0, 0);
    }
    void Move(Vector3 position)
    {
        var cc = player.GetComponent<CharacterController>(); bool enabled = cc.enabled; cc.enabled = false;
        player.transform.position = position; cc.enabled = enabled; Physics.SyncTransforms();
    }
    void Park() { aiming = false; Move(new Vector3(500, 160, 500)); }
    IEnumerator Equip(string name)
    {
        if (name == "Fist")
        {
            for (int i = 0; i < inventory.SlotCount; i++) if (inventory.Slots[i].IsEmpty) { inventory.SelectSlot(i); break; }
        }
        else
        {
            var item = UnityEngine.Resources.FindObjectsOfTypeAll<ItemData>().First(i => i.name == name);
            if (inventory.Count(item) == 0) { inventory.AddItem(item, 1); added.Add(item); }
            for (int i = 0; i < inventory.SlotCount; i++) if (inventory.Slots[i].item == item) { inventory.SelectSlot(i); break; }
        }
        yield return new WaitForSeconds(0.75f);
    }
    RaycastHit FirstHit()
    {
        return Physics.SphereCastAll(eye.transform.position, 0.25f, eye.transform.forward, 2f, HitMask, QueryTriggerInteraction.Ignore)
            .Where(h => !h.collider.transform.IsChildOf(player.transform)).OrderBy(h => h.distance).FirstOrDefault();
    }

    IEnumerator TargetNode(RockHealth rock)
    {
        var system = rock.GetComponentInParent<OreSpawnSystem>();
        foreach (var entry in system.Active)
            if (entry.Value == rock && system.TryRecord(entry.Key, out var record))
            {
                Move(system.sockets[record.socket].approach - Vector3.up * 1.05f);
                aim = rock.transform.TransformPoint(Vector3.up * 0.53f); aiming = true;
                yield return new WaitForSeconds(0.15f); Aim();
                Check(FirstHit().collider != null && FirstHit().collider.GetComponentInParent<RockHealth>() == rock,
                    rock.OreItem.name + " actual punch cast reaches node");
                yield break;
            }
        Check(false, "missing ore socket");
    }

    // Review placement only: seek a visible surface of an existing scene collider.
    // Runtime attacks still use only FistsController's single contact check.
    IEnumerator TargetScenery(string label, Func<Collider, bool> select, Vector3 near)
    {
        foreach (var c in FindObjectsByType<Collider>().Where(c => c.enabled && !c.isTrigger && select(c) && c.bounds.size.sqrMagnitude > 0.01f)
            .OrderBy(c => Vector3.Distance(c.bounds.center, near)).Take(30))
        {
            for (int a = 0; a < 12; a++)
            {
                var dir = Quaternion.Euler(0, a * 30, 0) * Vector3.forward;
                Vector3 outside = c.bounds.center + Vector3.Scale(c.bounds.extents + Vector3.one * 0.9f, dir);
                Move(outside - Vector3.up * 1.6f);
                var floor = Physics.RaycastAll(player.transform.position + Vector3.up, Vector3.down, 7f, HitMask, QueryTriggerInteraction.Ignore)
                    .Where(h => h.collider != c && h.normal.y > 0.65f).OrderBy(h => h.distance).FirstOrDefault();
                if (floor.collider != null) Move(new Vector3(outside.x, floor.point.y + 0.04f, outside.z));
                aim = c.bounds.center; aiming = true; Aim(); yield return new WaitForSeconds(0.04f);
                if (FirstHit().collider != c) continue;
                Check(true, label + " real collider=" + c.name + " cameraHeight=" + (eye.transform.position.y - player.transform.position.y).ToString("F2"));
                yield break;
            }
        }
        Check(false, label + " no accessible real collider");
    }

    IEnumerator Punch(string label, bool hard = true, bool capture = false)
    {
        int local = HardPunchImpactFX.LocalImpacts, remote = HardPunchImpactFX.RemoteImpacts;
        Physics.SyncTransforms();
        if (hard)
        {
            var expected = FirstHit();
            Check(expected.collider != null && HardPunchImpactFX.IsHardSurface(expected.collider),
                label + " confirmed hard target before punch=" + (expected.collider != null ? expected.collider.name : "miss"));
        }
        // Capture playback at the actual contact, before a GPU screenshot readback can outlast the 170 ms clip.
        bool spatialPlayback = false, validClip = false;
        var fists = equipment.UnarmedView.GetComponent<FistsController>();
        Action<Vector3, Vector3, int> onContact = (point, normal, seed) =>
        {
            var audio = FindObjectsByType<AudioSource>().Where(s => s.name.StartsWith("Hand impact voice")).ToArray();
            spatialPlayback = audio.Length == 8 && audio.Count(s => s.isPlaying) == 1 &&
                audio.All(s => s.spatialBlend == 1 && s.rolloffMode == AudioRolloffMode.Linear && s.maxDistance == 18);
            var clip = audio.FirstOrDefault(s => s.isPlaying)?.clip;
            if (clip == null) return;
            float[] samples = new float[clip.samples]; clip.GetData(samples, 0);
            validClip = samples.Any(s => Mathf.Abs(s) > 0.1f) && samples.All(s => Mathf.Abs(s) <= 0.86f) && clip.length < 0.2f;
        };
        fists.HardSurfaceHit += onContact;
        Check(player.GetComponent<UnarmedAttack>().Punch(), label + " punch started");
        float deadline = Time.time + 0.4f;
        while (HardPunchImpactFX.LocalImpacts == local && Time.time < deadline) yield return null;
        if (capture && hard) { yield return new WaitForSeconds(0.03f); yield return new WaitForEndOfFrame(); Capture(label); }
        Check(HardPunchImpactFX.LocalImpacts - local == (hard ? 1 : 0), label + " local bursts/thuds=" + (HardPunchImpactFX.LocalImpacts - local));
        Check(HardPunchImpactFX.RemoteImpacts == remote, label + " owner has no echoed remote effect");
        if (hard)
        {
            Check(HardPunchImpactFX.LiveParticles > 0 && HardPunchImpactFX.LiveParticles <= 6, label + " small live burst=" + HardPunchImpactFX.LiveParticles);
            Check(Vector3.Distance(HardPunchImpactFX.LastOrigin, eye.transform.position) > 0.25f, label + " blood outside camera");
            Check(spatialPlayback,
                label + " one spatial thud; bounded eight voices; linear silence at 18m");
            Check(validClip, label + " non-silent PCM clip; bounded peak; short 0.17s thud");
        }
        yield return new WaitForSeconds(0.7f);
        fists.HardSurfaceHit -= onContact;
        Check(HardPunchImpactFX.LocalImpacts - local == (hard ? 1 : 0) && HardPunchImpactFX.LiveParticles == 0, label + " no repeated contact; particles expired");
    }

    IEnumerator SinglePlayer()
    {
        yield return Equip("Fist");
        var hub = new Vector3(500, 18, 730);
        yield return TargetScenery("hub wall", c => c.name == "Panel_Wood_A", hub); yield return Punch("hub-wall", capture: true);
        // Stand at normal eye height over the real hub floor and aim down.
        Move(new Vector3(500, 17.5f, 730)); aiming = true; aim = player.transform.position + Vector3.down; Aim();
        yield return new WaitForSeconds(0.15f); Check(FirstHit().collider != null, "hub floor contact"); yield return Punch("hub-floor", capture: true);
        yield return TargetScenery("cave wall", c => c.name.StartsWith("JaggedWall"), hub); yield return Punch("cave-wall", capture: true);
        var oldMine = new Vector3(444, 17.25f, 698);
        yield return TargetScenery("Old Mine rock", c => c.name.StartsWith("Cave_Wall_Rocks"), oldMine); yield return Punch("old-mine-rock", capture: true);
        yield return TargetScenery("timber support", c => c.name == "Post_Reinforced_A", oldMine); yield return Punch("timber", capture: true);
        yield return TargetScenery("metal equipment", c => c.name == "Winch" || c.name == "Pump" || c.name == "MineCart", oldMine); yield return Punch("metal", capture: true);
        yield return Tree(); yield return Punch("tree", capture: true);
        foreach (string resource in ResourcesToTest)
        {
            var rock = FindObjectsByType<RockHealth>().Where(r => r.OreItem.name == resource).OrderBy(r => r.transform.position.x).ThenBy(r => r.transform.position.z).First();
            yield return TargetNode(rock); yield return Equip("Fist");
            int hp = rock.CurrentHealth, stage = rock.GetComponent<OreNodeVisual>().Stage, drops = Drops(rock.OreItem); savedHealth[rock] = hp;
            for (int i = 0; i < 3; i++)
            {
                yield return Punch(resource + "-" + (i + 1), capture: i == 0);
                Check(rock.CurrentHealth == hp && rock.GetComponent<OreNodeVisual>().Stage == stage && Drops(rock.OreItem) == drops,
                    resource + " punch " + (i + 1) + " HP=" + rock.CurrentHealth + " cracks=" + stage + " unchanged drops=" + drops);
            }
            foreach (var tool in new[] { "Hammer", "Pistol", "AssaultRifle" })
            {
                yield return Equip(tool);
                int impacts = HardPunchImpactFX.LocalImpacts;
                if (equipment.ActiveController is WeaponController gun) gun.SendMessage("Fire");
                else Check((equipment.ActiveController as MiningToolController).Swing.Swing(), tool + " swing");
                yield return new WaitForSeconds(1.3f);
                Check(rock.CurrentHealth == hp && rock.GetComponent<OreNodeVisual>().Stage == stage && Drops(rock.OreItem) == drops && HardPunchImpactFX.LocalImpacts == impacts,
                    resource + " " + tool + " unchanged durability/cracks/drops; no fist effects");
            }
            yield return Equip("Pickaxe"); Check((equipment.ActiveController as MiningToolController).Swing.Swing(), resource + " pickaxe swing");
            yield return new WaitForSeconds(1.3f);
            Check(rock.CurrentHealth == hp - 1 && rock.GetComponent<OreNodeVisual>().Stage == OreNodeVisualView.StageFor(hp - 1, rock.MaxHealth), resource + " pickaxe HP " + hp + " -> " + rock.CurrentHealth);
        }
        yield return Equip("Fist"); Park(); aim = eye.transform.position + Vector3.forward * 10; aiming = true; Aim(); yield return Punch("air", false);
        var dummy = GameObject.CreatePrimitive(PrimitiveType.Cube); temporary.Add(dummy); dummy.name = "Review future enemy";
        dummy.transform.position = eye.transform.position + eye.transform.forward * 1.2f;
        dummy.transform.localScale = Vector3.one * 0.5f; var damageable = dummy.AddComponent<HardPunchReviewDamageable>(); Physics.SyncTransforms();
        yield return Punch("character", false);
        Check(damageable.Hits == 1 && damageable.Damage == 10f, "unmarked character IDamageable receives one normal 10-damage punch");
        dummy.AddComponent<HardPunchSurface>(); yield return Punch("damageable-prop");
        Check(damageable.Hits == 2, "marked damageable environment keeps intended damage");
    }

    int Drops(ItemData item) => FindObjectsByType<DroppedItem>().Where(d => d.Item == item).Sum(d => d.Amount);
    IEnumerator Tree()
    {
        foreach (var terrain in FindObjectsByType<Terrain>())
            foreach (var tree in terrain.terrainData.treeInstances.Where(t => terrain.terrainData.treePrototypes[t.prototypeIndex].prefab.GetComponentInChildren<Collider>() != null)
                .OrderBy(t => Vector3.Distance(Vector3.Scale(t.position, terrain.terrainData.size) + terrain.transform.position, savedPosition)).Take(40))
            {
                Vector3 foot = Vector3.Scale(tree.position, terrain.terrainData.size) + terrain.transform.position;
                for (int angle = 0; angle < 8; angle++)
                {
                    var dir = Quaternion.Euler(0, angle * 45, 0) * Vector3.forward;
                    Move(foot + dir * 1.1f + Vector3.up * 0.03f); aim = foot + Vector3.up * 1.3f; aiming = true; Aim();
                    yield return new WaitForSeconds(0.04f);
                    var hit = FirstHit();
                    if (hit.collider != null && hit.normal.y < 0.5f && Vector3.Distance(hit.point, foot + Vector3.up * 1.3f) < 1f)
                    { Check(true, "outside terrain tree confirmed trunk contact=" + hit.collider.name); yield break; }
                }
            }
        Check(false, "outside tree contact");
    }

    IEnumerator Multiplayer()
    {
        Park(); yield return Equip("Fist"); Signal("ready"); yield return Wait(role == "host" ? "client" : "host", "ready");
        var copper = FindObjectsByType<RockHealth>().Where(r => r.OreItem.name == "CopperOre").OrderBy(r => r.transform.position.x).ThenBy(r => r.transform.position.z).First();
        int hp = copper.CurrentHealth, stage = copper.GetComponent<OreNodeVisual>().Stage, drops = Drops(copper.OreItem);
        foreach (string actor in new[] { "host", "client" })
        {
            foreach (string surface in new[] { "rock", "ore" })
            {
                string phase = actor + "-" + surface;
                if (role == actor)
                {
                    if (surface == "ore") yield return TargetNode(copper);
                    else yield return TargetScenery("network rock", c => c.name.StartsWith("Cave_Wall_Rocks"), new Vector3(500, 18, 730));
                    Vector3 actorPosition = player.transform.position;
                    File.WriteAllText(Path.Combine(output, phase + "-position.txt"),
                        actorPosition.x.ToString("R", CultureInfo.InvariantCulture) + ";" + actorPosition.y.ToString("R", CultureInfo.InvariantCulture) + ";" + actorPosition.z.ToString("R", CultureInfo.InvariantCulture));
                    Signal(phase + "-aim"); yield return Wait(role == "host" ? "client" : "host", phase + "-watch");
                    yield return Punch(phase, capture: true); Signal(phase + "-hit");
                    yield return Wait(role == "host" ? "client" : "host", phase + "-seen"); Park();
                }
                else
                {
                    yield return Wait(actor, phase + "-aim");
                    var avatar = FindObjectsByType<NetworkPlayerAvatar>().First(a => !a.IsOwner);
                    var coordinates = File.ReadAllText(Path.Combine(output, phase + "-position.txt")).Split(';');
                    var actorPosition = new Vector3(float.Parse(coordinates[0], CultureInfo.InvariantCulture), float.Parse(coordinates[1], CultureInfo.InvariantCulture), float.Parse(coordinates[2], CultureInfo.InvariantCulture));
                    yield return Until(() => Vector3.Distance(avatar.transform.position, actorPosition) < 0.15f, phase + " observer sees settled actor");
                    // Keep the observing player's hitboxes beyond the fist's reach in every direction.
                    // A close camera must not put the observer's body between the punch and its target.
                    Move(actorPosition + avatar.transform.right * 4f);
                    aim = actorPosition + Vector3.up * 1.5f + avatar.transform.forward * 0.6f; aiming = true; Aim();
                    yield return new WaitForSeconds(0.35f);
                    var animator = avatar.GetComponentInChildren<Animator>();
                    var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    Vector3 beforeRight = avatar.transform.InverseTransformPoint(right.position), beforeLeft = avatar.transform.InverseTransformPoint(left.position);
                    int local = HardPunchImpactFX.LocalImpacts, remote = HardPunchImpactFX.RemoteImpacts;
                    int networkObjects = FindObjectsByType<NetworkObject>().Length;
                    Signal(phase + "-watch");
                    yield return Until(() => HardPunchImpactFX.RemoteImpacts > remote, phase + " received impact");
                    yield return null;
                    Check(Vector3.Distance(beforeRight, avatar.transform.InverseTransformPoint(right.position)) + Vector3.Distance(beforeLeft, avatar.transform.InverseTransformPoint(left.position)) > 0.04f,
                        phase + " remote CorporateMiner hands animate");
                    Capture(phase + "-observer"); yield return Wait(actor, phase + "-hit");
                    Check(HardPunchImpactFX.RemoteImpacts - remote == 1 && HardPunchImpactFX.LocalImpacts == local, phase + " exactly one remote thud/burst; no owner effect");
                    Check(FindObjectsByType<NetworkObject>().Length == networkObjects, phase + " no particle network objects");
                    Check(HardPunchImpactFX.LiveParticles == 0, phase + " remote particles expire"); Signal(phase + "-seen"); Park();
                }
                Check(copper.CurrentHealth == hp && copper.GetComponent<OreNodeVisual>().Stage == stage && Drops(copper.OreItem) == drops,
                    phase + " shared Copper HP=" + hp + " cracks=" + stage + " drops=" + drops + " unchanged");
            }
        }
        // Real player-vs-player regression: host punches client, host-authoritative damage and flesh blood.
        if (role == "client")
        {
            Move(new Vector3(500, 160, 503)); Signal("character-ready"); yield return Wait("host", "character-done");
            Check(player.GetComponent<PlayerAttributes>().Health == 90, "client health 100 -> " + player.GetComponent<PlayerAttributes>().Health + " from one real host punch"); Signal("character-seen");
        }
        else
        {
            yield return Wait("client", "character-ready"); Move(new Vector3(500, 160, 501.7f));
            var avatar = FindObjectsByType<NetworkPlayerAvatar>().First(a => !a.IsOwner);
            yield return Until(() => Vector3.Distance(avatar.transform.position, new Vector3(500, 160, 503)) < 0.1f, "client avatar settled at character target");
            aim = avatar.GetComponentInChildren<Animator>().GetBoneTransform(HumanBodyBones.Chest).position;
            aiming = true; Aim(); yield return new WaitForSeconds(0.5f);
            var contact = FirstHit();
            Check(contact.collider != null && contact.collider.GetComponentInParent<NetworkPlayerHealth>() == avatar.GetComponent<NetworkPlayerHealth>(),
                "actual character punch cast contacts remote player=" + (contact.collider != null ? contact.collider.name : "miss"));
            int blood = BloodSplatter.Events; yield return Punch("network-character", false);
            Check(avatar.GetComponent<NetworkPlayerHealth>().Health == 90 && BloodSplatter.Events == blood + 1,
                "character authoritative health=" + avatar.GetComponent<NetworkPlayerHealth>().Health + " existing flesh blood events=" + (BloodSplatter.Events - blood));
            Signal("character-done"); yield return Wait("client", "character-seen");
        }
        Signal("finished"); yield return Wait(role == "host" ? "client" : "host", "finished");
    }

    void Capture(string label)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var target = RenderTexture.GetTemporary(960, 600, 24); var previous = eye.targetTexture; var active = RenderTexture.active;
        eye.targetTexture = target; eye.Render(); RenderTexture.active = target;
        var image = new Texture2D(960, 600, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, role + "-" + label + ".png"), image.EncodeToPNG());
        eye.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); Destroy(image);
    }
}
