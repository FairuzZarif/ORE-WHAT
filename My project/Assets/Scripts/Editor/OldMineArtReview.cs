using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>Environment acceptance checks. Uses the actual player in Play Mode; never shipped in a build.</summary>
public static class OldMineArtReview
{
    private const string Output = "../Builds/OldMineArtReview/";
    private static readonly string[] Down = {
        "Cavern_01", "OldMine_Adit", "OldMine_Office", "OldMine_StoreDoor", "OldMine_Storeroom", "OldMine_Office",
        "OldMine_Drift_1", "OldMine_Stope", "OldMine_SideDrift", "OldMine_Abandoned", "OldMine_AbandonedLink", "MiningArea_02",
        "OldMine_Ramp", "OldMine_Drift_2", "OldMine_Stope", "OldMine_Drift_2", "OldMine_Ramp", "MiningArea_02",
        "DeepTunnel_A", "DeepTunnel_B", "DeepTunnel_C", "DeepCavern", "OldMine_LowerDrift_1", "OldMine_LowerDrift_2",
        "OldShaft_Gallery", "OldShaft_Incline_1", "OldShaft_Landing_1", "OldShaft_Incline_2", "OldShaft_Landing_2",
        "OldShaft_Incline_3", "OldShaft", "OldShaft_BottomDrift", "OldMine_WorkArea", "OldMine_PocketDrift", "OldMine_Pocket",
        "OldMine_WorkArea", "OldMine_Approach_1", "OldMine_CollapsedHall", "OldMine_Approach_2", "OldMine_ApproachTurn",
        "OldMine_ArenaGate", "OldMine_BossThreshold", "BossArena" };
    private static readonly string[] Back = {
        "BossArena", "OldMine_BossThreshold", "OldMine_ArenaGate", "OldMine_ApproachTurn", "OldMine_Approach_2",
        "OldMine_CollapsedHall", "OldMine_Approach_1", "OldMine_WorkArea", "OldMine_OldIncline", "OldShaft_Landing_1",
        "OldShaft_Incline_1", "OldShaft_Gallery", "OldMine_LowerDrift_2", "OldMine_LowerDrift_1", "DeepCavern",
        "DeepTunnel_C", "DeepTunnel_B", "DeepTunnel_A", "MiningArea_02", "OldMine_AbandonedLink", "OldMine_Abandoned",
        "OldMine_SideDrift", "OldMine_Stope", "Collapsed_Drift", "OreArea_01", "Cavern_01" };
    private struct Waypoint { public Vector3 point; public string label; }
    private static readonly Dictionary<float, NavMeshData> ReviewMeshes = new Dictionary<float, NavMeshData>();

    private static NavMeshDataInstance Nav(CaveLayout cave, float height)
    {
        if (EditorApplication.isPlaying)
        {
            if (!ReviewMeshes.TryGetValue(height, out NavMeshData cached) || cached == null)
                throw new InvalidOperationException("Run OldMineArtReview.ExportRoutes in Edit Mode before player checks.");
            return NavMesh.AddNavMeshData(cached);
        }
        Physics.SyncTransforms();
        var bounds = new Bounds(new Vector3(495, -30, 740), new Vector3(330, 200, 380));
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        var player = GameObject.FindWithTag("Player");
        if (player != null) sources.RemoveAll(s => s.component != null && s.component.transform.IsChildOf(player.transform));
        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = .4f; settings.agentHeight = height; settings.agentClimb = .3f; settings.agentSlope = 45;
        settings.overrideVoxelSize = true; settings.voxelSize = .15f;
        if (ReviewMeshes.TryGetValue(height, out NavMeshData previous) && previous != null) UnityEngine.Object.DestroyImmediate(previous);
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        data.hideFlags = HideFlags.HideAndDontSave;
        ReviewMeshes[height] = data;
        return NavMesh.AddNavMeshData(data);
    }

    private static List<Waypoint> Path(CaveLayout cave, string[] rooms)
    {
        var list = new List<Waypoint>();
        Vector3 last = Vector3.zero;
        foreach (string n in rooms)
        {
            CaveSpace s = cave.transform.Find(n).GetComponent<CaveSpace>();
            Vector3 target = s.SpaceKind == CaveSpace.Kind.Room || s.End == null ? s.transform.position : (s.transform.position + s.End.position) * .5f;
            if (n == "OldShaft") target += new Vector3(-3, 0, -6);
            if (!NavMesh.SamplePosition(target + Vector3.up, out NavMeshHit hit, 8, NavMesh.AllAreas)) throw new InvalidOperationException("No floor: " + n);
            if (list.Count == 0) list.Add(new Waypoint { point = hit.position, label = n });
            else
            {
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(last, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("No complete path to " + n);
                foreach (var p in path.corners) list.Add(new Waypoint { point = p, label = n });
            }
            last = hit.position;
        }
        return list;
    }

    public static string ExportRoutes()
    {
        var cave = UnityEngine.Object.FindAnyObjectByType<CaveLayout>();
        NavMesh.RemoveNavMeshData(Nav(cave, 1.3f));
        var nav = Nav(cave, 1.85f);
        try
        {
            Directory.CreateDirectory(Output);
            foreach (var pair in new[] { ("down", Down), ("back", Back) })
            {
                var path = Path(cave, pair.Item2); var csv = new StringBuilder();
                foreach (var w in path) csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F4},{3}", w.point.x, w.point.y, w.point.z, w.label));
                File.WriteAllText(Output + pair.Item1 + ".csv", csv.ToString());
            }
            return "Exported full Old Mine routes including stores, No. 3 loop, shaft, ore pocket, final approach, arena and old-incline return.";
        }
        finally { NavMesh.RemoveNavMeshData(nav); }
    }

    /// <summary>Normal-speed visual survey across real game frames, with the actual camera following the player.</summary>
    public static string BeginVisualWalk()
    {
        if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
        var player = GameObject.FindWithTag("Player"); var cc = player.GetComponent<CharacterController>();
        var camera = player.GetComponentsInChildren<Camera>().First(c => c.name == "PlayerCamera");
        var cave = UnityEngine.Object.FindAnyObjectByType<CaveLayout>(); var nav = Nav(cave, 1.85f);
        var path = Path(cave, Down); Vector3 saved = player.transform.position; Quaternion savedRotation = player.transform.rotation;
        var movement = player.GetComponent<PlayerMovement>(); var look = player.GetComponent<PlayerLook>();
        bool movementEnabled = movement.enabled, lookEnabled = look.enabled;
        movement.enabled = false; look.enabled = false;
        cc.enabled = false; player.transform.position = path[0].point + Vector3.up * .1f; cc.enabled = true;
        int index = 0, stalled = 0; float best = float.MaxValue, walked = 0, last = Time.realtimeSinceStartup;
        string lastLabel = ""; var log = new StringBuilder();
        EditorApplication.CallbackFunction tick = null;
        void Finish(string reason)
        {
            EditorApplication.update -= tick; NavMesh.RemoveNavMeshData(nav);
            if (player != null)
            {
                cc.enabled = false; player.transform.SetPositionAndRotation(saved, savedRotation); cc.enabled = true;
                movement.enabled = movementEnabled; look.enabled = lookEnabled;
            }
            log.AppendLine(reason + "; frame-based actual-player survey " + walked.ToString("F1") + " m.");
            File.WriteAllText(Output + "visual-walk.txt", log.ToString()); Debug.Log("[Old Mine Art Review] " + reason);
        }
        tick = () =>
        {
            if (!EditorApplication.isPlaying || player == null) { Finish("Interrupted"); return; }
            float now = Time.realtimeSinceStartup, dt = Mathf.Clamp(now - last, 0, .05f); last = now;
            if (dt <= 0 || EditorApplication.isPaused) return;
            if (index >= path.Count) { Finish("Completed with no stuck spots"); return; }
            var w = path[index]; Vector3 before = player.transform.position, delta = w.point - before; delta.y = 0;
            if (delta.magnitude < .35f)
            {
                if ((index == path.Count - 1 || path[index + 1].label != w.label) && w.label != lastLabel)
                {
                    log.AppendLine(w.label + " actual=" + player.transform.position); lastLabel = w.label;
                    var rt = new RenderTexture(960, 600, 24); var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
                    var texture = new Texture2D(960, 600, TextureFormat.RGB24, false);
                    try
                    {
                        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                        texture.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); texture.Apply();
                        File.WriteAllBytes(Output + "walk-" + w.label + ".png", texture.EncodeToPNG());
                    }
                    finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(texture); }
                }
                index++; best = float.MaxValue; stalled = 0; return;
            }
            player.transform.rotation = Quaternion.LookRotation(delta);
            cc.Move(delta.normalized * Mathf.Min(6f * dt, delta.magnitude) + Vector3.down * (5f * dt));
            Vector3 moved = player.transform.position - before; moved.y = 0; walked += moved.magnitude;
            stalled = delta.magnitude > best - .001f ? stalled + 1 : 0; best = Mathf.Min(best, delta.magnitude);
            if (stalled > 180) Finish("STUCK " + w.label + " " + player.transform.position);
        };
        EditorApplication.update += tick;
        File.WriteAllText(Output + "visual-walk.txt", "Running normal-speed first-person survey.");
        return "Started normal-speed player-camera survey; completion goes to visual-walk.txt.";
    }

    public static string WalkActualPlayer()
    {
        if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
        var player = GameObject.FindWithTag("Player"); var cc = player.GetComponent<CharacterController>();
        var cave = UnityEngine.Object.FindAnyObjectByType<CaveLayout>();
        Vector3 saved = player.transform.position; float height = cc.height; Vector3 center = cc.center;
        var log = new StringBuilder(); int failures = 0; float meters = 0;
        var nav = Nav(cave, 1.85f);
        try
        {
            foreach (var plan in new[] { Down, Back })
            {
                var path = Path(cave, plan);
                cc.enabled = false; player.transform.position = path[0].point + Vector3.up * .1f; cc.enabled = true;
                foreach (var w in path)
                {
                    float best = float.MaxValue; int stalled = 0, guard = 0;
                    while (true)
                    {
                        Vector3 a = player.transform.position, delta = w.point - a; delta.y = 0;
                        if (delta.magnitude < .35f) break;
                        cc.Move(delta.normalized * Mathf.Min(.3f, delta.magnitude) + Vector3.down * .25f);
                        Vector3 moved = player.transform.position - a; moved.y = 0; meters += moved.magnitude;
                        stalled = delta.magnitude > best - .02f ? stalled + 1 : 0; best = Mathf.Min(best, delta.magnitude);
                        if (stalled > 40 || ++guard > 4000) { failures++; log.AppendLine("STUCK " + w.label + " " + player.transform.position); break; }
                    }
                }
            }
        }
        finally
        {
            NavMesh.RemoveNavMeshData(nav);
            cc.enabled = false; player.transform.position = saved; cc.height = height; cc.center = center; cc.enabled = true;
        }
        log.AppendLine("Actual player: " + meters.ToString("F1") + " m, stuck=" + failures + ". Full route, side paths, loop and shortcut return.");
        File.WriteAllText(Output + "actual-player-walk.txt", log.ToString());
        return log.ToString();
    }

    public static string WalkActualCrawl()
    {
        if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
        var player = GameObject.FindWithTag("Player"); var cc = player.GetComponent<CharacterController>();
        var cave = UnityEngine.Object.FindAnyObjectByType<CaveLayout>(); Vector3 saved = player.transform.position;
        float height = cc.height; Vector3 center = cc.center; var nav = Nav(cave, 1.3f); int stuck = 0;
        try
        {
            cc.height = 1.3f; cc.center = new Vector3(0, .65f, 0);
            var path = Path(cave, new[] { "OreArea_01", "Collapsed_Drift", "Collapsed_Crawl", "Collapsed_Chamber", "Collapsed_Crawl", "Collapsed_Drift", "OreArea_01" });
            cc.enabled = false; player.transform.position = path[0].point + Vector3.up * .1f; cc.enabled = true;
            foreach (var w in path)
            {
                int guard = 0;
                while (true)
                {
                    Vector3 delta = w.point - player.transform.position; delta.y = 0;
                    if (delta.magnitude < .35f) break;
                    cc.Move(delta.normalized * Mathf.Min(.25f, delta.magnitude) + Vector3.down * .2f);
                    if (++guard > 4000) { stuck++; break; }
                }
            }
        }
        finally
        {
            NavMesh.RemoveNavMeshData(nav); cc.enabled = false; player.transform.position = saved;
            cc.height = height; cc.center = center; cc.enabled = true;
        }
        string report = "Actual crouched player: collapsed drift/crawl/ore chamber round trip, stuck=" + stuck;
        File.WriteAllText(Output + "actual-crawl.txt", report); return report;
    }

    // Called in Play Mode; camera and headlamp remain the actual player's components.
    public static string CaptureSections(string referencePoses = null)
    {
        if (!EditorApplication.isPlaying) return "Enter Play Mode first.";
        var poses = new Dictionary<string, Vector3>();
        if (!string.IsNullOrEmpty(referencePoses) && File.Exists(referencePoses))
            foreach (string line in File.ReadAllLines(referencePoses))
            {
                int split = line.IndexOf(" camera=");
                if (split < 0) continue;
                string[] axes = line.Substring(split + 8).Trim('(', ')').Split(',');
                var culture = System.Globalization.CultureInfo.InvariantCulture;
                poses[line.Substring(0, split)] = new Vector3(float.Parse(axes[0], culture), float.Parse(axes[1], culture), float.Parse(axes[2], culture));
            }
        var player = GameObject.FindWithTag("Player"); var cc = player.GetComponent<CharacterController>();
        var camera = player.GetComponentsInChildren<Camera>().First(c => c.name == "PlayerCamera");
        var cave = UnityEngine.Object.FindAnyObjectByType<CaveLayout>(); var generated = cave.transform.parent.Find("Mountain/Generated");
        var headlamp = player.GetComponentInChildren<Headlamp>(); bool lampWasOn = headlamp != null && headlamp.IsOn;
        var nav = Nav(cave, 1.85f); Vector3 saved = player.transform.position, cameraSaved = camera.transform.localPosition;
        Quaternion rotation = player.transform.rotation, cameraRotation = camera.transform.localRotation;
        var log = new StringBuilder();
        try
        {
            foreach (var n in new[] { "OldMine_Adit", "OldMine_Office", "OldMine_Stope", "OldMine_Abandoned", "OldShaft_Gallery", "DeepCavern", "OldMine_WorkArea", "OldMine_CollapsedHall", "OldMine_Approach_2", "OldMine_BossThreshold", "BossArena" })
            {
                var s = cave.transform.Find(n).GetComponent<CaveSpace>(); Vector3 aim = s.transform.position;
                Vector3 at = aim + new Vector3(3, 0, -5);
                if (s.End != null) { at = Vector3.Lerp(aim, s.End.position, .2f); aim = s.End.position; }
                if (n == "OldShaft_Gallery") { at = s.transform.position + new Vector3(-4, 0, -1); aim = new Vector3(432, -32, 871); }
                if (n == "BossArena") { at = new Vector3(425, -18.5f, 701); aim = new Vector3(401, -15, 740); }
                if (n == "OldMine_BossThreshold") { at = new Vector3(429.1f, -20.1f, 693.1f); aim = new Vector3(418, -18.75f, 711); }
                if (!NavMesh.SamplePosition(at + Vector3.up, out NavMeshHit hit, 5f, NavMesh.AllAreas)) { log.AppendLine("No safe camera floor " + n); continue; }
                cc.enabled = false; player.transform.position = hit.position + Vector3.up * .1f; cc.enabled = true;
                if (poses.TryGetValue(n, out Vector3 reference))
                {
                    cc.enabled = false; player.transform.position = reference - Vector3.up * 1.6f; cc.enabled = true;
                }
                Vector3 look = aim + Vector3.up * 1.5f - (player.transform.position + Vector3.up * 1.6f);
                player.transform.rotation = Quaternion.LookRotation(new Vector3(look.x, 0, look.z));
                camera.transform.position = player.transform.position + Vector3.up * 1.6f; camera.transform.rotation = Quaternion.LookRotation(look);
                var atmosphere = UnityEngine.Object.FindAnyObjectByType<CaveAtmosphere>();
                if (atmosphere != null) for (int i = 0; i < 150; i++) atmosphere.SendMessage("LateUpdate");
                foreach (bool lampOn in new[] { false, true })
                {
                    if (headlamp != null)
                    {
                        headlamp.SetOn(lampOn); headlamp.SendMessage("LateUpdate");
                        if (lampOn && headlamp.Light != null) headlamp.Light.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                    }
                    var rt = new RenderTexture(1200, 750, 24); var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
                    var texture = new Texture2D(1200, 750, TextureFormat.RGB24, false);
                    try
                    {
                        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                        texture.ReadPixels(new Rect(0, 0, 1200, 750), 0, 0); texture.Apply();
                        File.WriteAllBytes(Output + "after-" + n + (lampOn ? "-headlamp" : "") + ".png", texture.EncodeToPNG());
                    }
                    finally { camera.targetTexture = oldTarget; RenderTexture.active = oldActive; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(texture); }
                }
                log.AppendLine(n + " camera=" + camera.transform.position);
            }
        }
        finally
        {
            NavMesh.RemoveNavMeshData(nav); cc.enabled = false; player.transform.SetPositionAndRotation(saved, rotation); cc.enabled = true;
            camera.transform.localPosition = cameraSaved; camera.transform.localRotation = cameraRotation;
            if (headlamp != null) headlamp.SetOn(lampWasOn);
        }
        File.WriteAllText(Output + "camera-review.txt", log.ToString()); return log.ToString();
    }
}
