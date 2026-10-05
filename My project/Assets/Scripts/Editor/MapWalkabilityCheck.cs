using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Walkability check for the island map (no changes to the scene): builds a temporary NavMesh with the player's own
/// limits (radius 0.4, step 0.3 m, slope 45°) from the scene's colliders, then
///   1. walks from the cave mouth to every cave space, standing (1.8 m) and crouched (1.3 m);
///   2. blocks off the Central Mining Hub and checks that no mine can be walked into from another one.
/// Menu: Ore What > Island Map > Validate Walkability. Takes a few seconds.
/// </summary>
public static class MapWalkabilityCheck
{
    [MenuItem("Ore What/Island Map/Validate Walkability")]
    public static void Menu() => Debug.Log("[Ore What] " + Run());

    // The boss arena centre is deliberately clear; only the shaft needs an offset from its wreckage.
    private static readonly Dictionary<string, Vector3> TargetOffsets = new Dictionary<string, Vector3> { { "OldShaft", new Vector3(-3f, 0f, -6f) } };

    // One deep space per mine, for the "mines don't connect" test.
    private static readonly string[] MineArenas = { null, "BossArena", "DeepHollow", "Crystal_Geode", "Rift_Final" };

    public static string Run()
    {
        if (EditorApplication.isPlaying) return "Leave Play mode first.";
        var island = GameObject.Find("Island");
        var cave = island != null ? island.transform.Find("Cave")?.GetComponent<CaveLayout>() : null;
        if (cave == null) return "No island map in the open scene.";
        cave.Prepare();
        Physics.SyncTransforms(); // colliders made in this editor frame aren't in the physics scene yet otherwise

        var bounds = new Bounds(new Vector3(495f, -45f, 770f), new Vector3(320f, 170f, 320f));
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        var log = new StringBuilder();
        Transform start = cave.transform.Find("MainTunnel");
        if (start == null) return "No MainTunnel (the way in) in the cave.";

        var spaces = new List<CaveSpace>();
        foreach (CaveSpace s in cave.Spaces) if (s.SpaceKind != CaveSpace.Kind.OpenCut) spaces.Add(s);
        CaveSpace mouth = start.GetComponent<CaveSpace>();

        foreach (float height in new[] { 1.8f, 1.3f })
        {
            NavMeshDataInstance mesh = Build(sources, bounds, height);
            HashSet<CaveSpace> reached = Flood(spaces, new[] { mouth });
            var unreachable = new List<string>();
            foreach (CaveSpace s in spaces) if (!reached.Contains(s)) unreachable.Add(s.name);
            log.AppendLine($"{(height > 1.5f ? "Standing" : "Crouched")} ({height} m): {reached.Count}/{spaces.Count} spaces reachable from the cave mouth" +
                           (unreachable.Count > 0 ? "; not: " + string.Join(", ", unreachable) : "."));
            NavMesh.RemoveNavMeshData(mesh);
        }

        // Block the hub, then walk outward from each mine's deepest area: nothing of another mine may be reached.
        Transform hub = cave.transform.Find("Cavern_01");
        if (hub != null)
        {
            var blocked = new List<NavMeshBuildSource>(sources)
            {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.ModifierBox, area = 1, size = new Vector3(60f, 30f, 52f),
                                         transform = Matrix4x4.TRS(hub.position + Vector3.up * 8f, hub.rotation, Vector3.one) },
            };
            NavMeshDataInstance mesh = Build(blocked, bounds, 1.3f);
            var withoutHub = spaces.FindAll(s => IslandMapBuilder.MineOf(s.name) != IslandMapBuilder.Hub);
            int leaks = 0;
            for (int mine = 1; mine < MineArenas.Length; mine++)
            {
                Transform arena = cave.transform.Find(MineArenas[mine]);
                if (arena == null) continue;
                foreach (CaveSpace s in Flood(withoutHub, new[] { arena.GetComponent<CaveSpace>() }))
                {
                    int other = IslandMapBuilder.MineOf(s.name);
                    if (other == mine || other < 0) continue;
                    leaks++;
                    log.AppendLine($"LEAK: {IslandMapBuilder.MineNames[mine]} reaches {s.name} ({IslandMapBuilder.MineNames[other]}) without the hub.");
                }
            }
            log.AppendLine(leaks == 0 ? "Mines are separate: with the hub blocked, no mine can be walked into from another." : $"{leaks} spaces reachable across mines without the hub.");
            NavMesh.RemoveNavMeshData(mesh);
        }
        return log.ToString();
    }

    /// <summary>
    /// Every space reachable from the starting spaces, found space by space with short paths (from the nearest
    /// already-reached spaces): one long path across the whole map can exceed the path search's limits.
    /// </summary>
    private static HashSet<CaveSpace> Flood(List<CaveSpace> spaces, IEnumerable<CaveSpace> from)
    {
        var points = new Dictionary<CaveSpace, Vector3>();
        foreach (CaveSpace s in spaces)
            if (NavMesh.SamplePosition(Target(s) + Vector3.up, out NavMeshHit h, 7f, NavMesh.AllAreas)) points[s] = h.position;
        var reached = new HashSet<CaveSpace>();
        foreach (CaveSpace s in from) if (s != null && points.ContainsKey(s)) reached.Add(s);
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (CaveSpace s in spaces)
            {
                if (reached.Contains(s) || !points.ContainsKey(s)) continue;
                var nearest = new List<CaveSpace>(reached);
                nearest.Sort((x, y) => (points[x] - points[s]).sqrMagnitude.CompareTo((points[y] - points[s]).sqrMagnitude));
                for (int i = 0; i < Mathf.Min(8, nearest.Count); i++)
                {
                    var path = new NavMeshPath();
                    if (NavMesh.CalculatePath(points[nearest[i]], points[s], NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    {
                        reached.Add(s);
                        grew = true;
                        break;
                    }
                }
            }
        }
        return reached;
    }

    // The map's main routes and their loops back to the hub, as cave space names (walked in this order).
    private static readonly (string title, string[] spaces)[] Routes =
    {
        ("Outside -> Hub", new[] { "@500,17,600", "MainTunnel", "Cavern_01" }),
        ("Mine 1 main route", new[] { "Cavern_01", "OldMine_Adit", "OldMine_Office", "OldMine_Drift_1", "OldMine_Stope", "OldMine_Drift_2", "OldMine_Ramp", "MiningArea_02",
            "DeepTunnel_A", "DeepTunnel_B", "DeepTunnel_C", "DeepCavern", "OldMine_LowerDrift_1", "OldMine_LowerDrift_2", "OldShaft_Gallery", "OldShaft_Incline_1",
            "OldShaft_Landing_1", "OldShaft_Incline_2", "OldShaft_Landing_2", "OldShaft_Incline_3", "OldShaft", "OldShaft_BottomDrift", "OldMine_WorkArea",
            "OldMine_Approach_1", "OldMine_CollapsedHall", "OldMine_Approach_2", "OldMine_ApproachTurn", "OldMine_ArenaGate", "OldMine_BossThreshold", "BossArena" }),
        ("Mine 1 back (old incline, No. 3 workings, collapsed workings)", new[] { "BossArena", "OldMine_BossThreshold", "OldMine_ArenaGate", "OldMine_ApproachTurn", "OldMine_Approach_2",
            "OldMine_CollapsedHall", "OldMine_Approach_1", "OldMine_WorkArea", "OldMine_OldIncline", "OldShaft_Landing_1", "OldShaft_Incline_1", "OldShaft_Gallery",
            "OldMine_LowerDrift_2", "OldMine_LowerDrift_1", "DeepCavern", "DeepTunnel_C", "DeepTunnel_B", "DeepTunnel_A", "MiningArea_02", "OldMine_AbandonedLink",
            "OldMine_Abandoned", "OldMine_SideDrift", "OldMine_Stope", "Collapsed_Drift", "OreArea_01", "Cavern_01" }),
        ("Mine 1 shortest (old incline shortcut)", new[] { "Cavern_01", "OldMine_Adit", "OldMine_Office", "OldMine_Drift_1", "OldMine_Stope", "OldMine_Drift_2",
            "OldMine_Ramp", "MiningArea_02", "DeepTunnel_A", "DeepTunnel_B", "DeepTunnel_C", "DeepCavern", "OldMine_LowerDrift_1", "OldMine_LowerDrift_2",
            "OldShaft_Gallery", "OldShaft_Incline_1", "OldShaft_Landing_1", "OldMine_OldIncline", "OldMine_WorkArea", "OldMine_Approach_1",
            "OldMine_CollapsedHall", "OldMine_Approach_2", "OldMine_ApproachTurn", "OldMine_ArenaGate", "OldMine_BossThreshold", "BossArena" }),
        ("Mine 1 store room", new[] { "OldMine_Office", "OldMine_StoreDoor", "OldMine_Storeroom", "OldMine_StoreDoor", "OldMine_Office" }),
        ("Mine 1 ore pocket", new[] { "OldMine_WorkArea", "OldMine_PocketDrift", "OldMine_Pocket", "OldMine_PocketDrift", "OldMine_WorkArea" }),
        ("Mine 1 arena perimeter and open floor", new[] { "OldMine_BossThreshold", "BossArena@20,0", "BossArena@16,16", "BossArena@0,22",
            "BossArena@-16,16", "BossArena@-22,0", "BossArena@-16,-16", "BossArena@0,-22", "BossArena@16,-16", "BossArena@20,0",
            "BossArena", "BossArena@-16,0", "BossArena@16,0", "BossArena@0,16", "BossArena@0,-16", "OldMine_BossThreshold" }),
        ("Mine 2 main route", new[] { "Cavern_01", "SideTunnel", "SideCave", "RailTunnel_Link", "RailTunnel_2", "RailJunction", "RailTunnel_3", "CombatArea", "DeepMine_Incline_1", "DeepMine_Shaft", "DeepMine_Incline_2", "DeepMine_Landing", "DeepMine_Incline_3", "DeepHollow" }),
        ("Mine 2 back (return incline)", new[] { "DeepHollow", "DeepMine_Incline_3", "DeepMine_Landing", "DeepMine_Incline_2", "DeepMine_Shaft", "DeepMine_Return", "RailJunction", "RailTunnel_2", "RailTunnel_Link", "SideCave", "SideTunnel", "Cavern_01" }),
        ("Mine 3 main route (upper)", new[] { "Cavern_01", "Tunnel_02", "Cavern_02", "CrystalTunnel_W", "CrystalCavern", "Crystal_Descent_1", "Crystal_Descent_2", "Crystal_Descent_3", "Crystal_Descent_4", "Crystal_Descent_5", "Crystal_Geode" }),
        ("Mine 3 back (lower route)", new[] { "Crystal_Geode", "Crystal_Lower_5", "Crystal_Lower_4", "Crystal_Lower_3", "Crystal_Lower_2", "Crystal_Lower_1", "Cavern_02", "Tunnel_02", "Cavern_01" }),
        ("Mine 4 main route", new[] { "Cavern_01", "Rift_Portal", "Rift_Descent_1", "Rift_Descent_2", "Rift_Cavern", "Rift_Lower_1", "Rift_Lower_2", "Rift_Lower_3", "Rift_Ledge", "Rift_Lower_4", "Rift_Landing", "Rift_Lower_5", "Rift_Abyss", "Rift_DeepDescent", "Rift_Final" }),
        ("Mine 4 back (chasm + survey climb)", new[] { "Rift_Final", "Rift_DeepDescent", "Rift_Abyss", "Rift_ChasmLink", "Rift_Chasm", "Rift_Climb_1", "Rift_Climb_2", "Rift_Climb_3", "Rift_Climb_4", "Rift_Climb_5", "Rift_Climb_6", "Rift_Cavern", "Rift_Descent_2", "Rift_Descent_1", "Rift_Portal", "Cavern_01" }),
    };

    [MenuItem("Ore What/Island Map/Walk Test Routes")]
    public static void WalkMenu() => Debug.Log("[Ore What] " + WalkRoutes());

    /// <summary>
    /// Walks a stand-in for the player (a CharacterController with the player's height, radius, step and slope limit)
    /// along every route, NavMesh corner by corner with CharacterController.Move, and reports where it got stuck.
    /// Tests the real collision (steps, slopes, props), not just the NavMesh. No scene changes (the stand-in is removed).
    /// </summary>
    public static string WalkRoutes()
    {
        var island = GameObject.Find("Island");
        var cave = island != null ? island.transform.Find("Cave")?.GetComponent<CaveLayout>() : null;
        if (cave == null) return "No island map in the open scene.";
        Physics.SyncTransforms();
        var bounds = new Bounds(new Vector3(495f, -30f, 740f), new Vector3(330f, 200f, 380f));
        var sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) sources.RemoveAll(s => s.component != null && s.component.transform.IsChildOf(player.transform));
        NavMeshDataInstance mesh = Build(sources, bounds, 1.85f, 0.45f);

        var walker = new GameObject("Walk Test (temporary)") { hideFlags = HideFlags.HideAndDontSave };
        walker.layer = LayerMask.NameToLayer("Player") >= 0 ? LayerMask.NameToLayer("Player") : 0;
        var cc = walker.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.4f; cc.stepOffset = 0.3f; cc.slopeLimit = 45f; cc.skinWidth = 0.05f; cc.center = new Vector3(0f, 0.9f, 0f);
        var log = new StringBuilder();
        int totalStuck = 0;
        try
        {
            foreach (var (title, names) in Routes)
            {
                int stuck = 0; float walked = 0f;
                var notes = new List<string>();
                Vector3 Point(string n)
                {
                    if (n[0] == '@') { string[] a = n.Substring(1).Split(','); return new Vector3(float.Parse(a[0]), float.Parse(a[1]), float.Parse(a[2])); }
                    if (n.Contains("@"))
                    {
                        string[] parts = n.Split('@'), offset = parts[1].Split(',');
                        Transform room = cave.transform.Find(parts[0]);
                        return room.position + new Vector3(float.Parse(offset[0], System.Globalization.CultureInfo.InvariantCulture), 0f,
                            float.Parse(offset[1], System.Globalization.CultureInfo.InvariantCulture));
                    }
                    Transform t = cave.transform.Find(n);
                    return t == null ? Vector3.zero : Target(t.GetComponent<CaveSpace>());
                }
                if (!NavMesh.SamplePosition(Point(names[0]) + Vector3.up, out NavMeshHit first, 8f, NavMesh.AllAreas)) { log.AppendLine(title + ": no start"); continue; }
                cc.enabled = false; walker.transform.position = first.position + Vector3.up * 0.1f; cc.enabled = true;
                for (int i = 1; i < names.Length; i++)
                {
                    if (names[i][0] != '@' && cave.transform.Find(names[i].Split('@')[0]) == null) { notes.Add(names[i] + " missing"); continue; }
                    if (!NavMesh.SamplePosition(Point(names[i]) + Vector3.up, out NavMeshHit to, 8f, NavMesh.AllAreas)) { notes.Add("no floor in " + names[i]); continue; }
                    NavMesh.SamplePosition(walker.transform.position, out NavMeshHit from, 4f, NavMesh.AllAreas);
                    var path = new NavMeshPath();
                    NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path);
                    if (path.status != NavMeshPathStatus.PathComplete) notes.Add($"no full path to {names[i]}");
                    foreach (Vector3 corner in path.corners)
                    {
                        float best = float.MaxValue; int noProgress = 0, guard = 0;
                        while (true)
                        {
                            Vector3 pos = walker.transform.position, flat = corner - pos; flat.y = 0f;
                            float dist = flat.magnitude;
                            if (dist < 0.35f) break;
                            cc.Move(flat.normalized * Mathf.Min(0.3f, dist) + Vector3.down * 0.25f);
                            Vector3 moved = walker.transform.position - pos; moved.y = 0f;
                            walked += moved.magnitude;
                            noProgress = dist > best - 0.02f ? noProgress + 1 : 0;
                            best = Mathf.Min(best, dist);
                            if (noProgress > 40 || ++guard > 4000)
                            {
                                stuck++;
                                notes.Add($"stuck at {walker.transform.position:F1} on the way to {names[i]}");
                                cc.enabled = false; walker.transform.position = corner + Vector3.up * 0.1f; cc.enabled = true;
                                break;
                            }
                        }
                    }
                }
                totalStuck += stuck;
                log.AppendLine($"{title}: walked {walked:F0} m, {(stuck == 0 ? "never stuck" : stuck + " stuck")}" + (notes.Count > 0 ? " - " + string.Join("; ", notes) : ""));
            }
        }
        finally
        {
            Object.DestroyImmediate(walker);
            NavMesh.RemoveNavMeshData(mesh);
        }
        log.Insert(0, totalStuck == 0 ? "All routes walked with the player's controller.\n" : $"{totalStuck} stuck spots:\n");
        return log.ToString();
    }

    private static NavMeshDataInstance Build(List<NavMeshBuildSource> sources, Bounds bounds, float height, float radius)
    {
        // Copy the built-in agent settings; CreateSettings adds persistent project agents on every test run.
        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = radius; settings.agentHeight = height; settings.agentSlope = 45f; settings.agentClimb = 0.3f;
        settings.voxelSize = 0.15f; settings.overrideVoxelSize = true;
        return NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity));
    }

    private static NavMeshDataInstance Build(List<NavMeshBuildSource> sources, Bounds bounds, float height)
    {
        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = 0.4f; settings.agentHeight = height; settings.agentSlope = 45f; settings.agentClimb = 0.3f;
        settings.voxelSize = 0.15f; settings.overrideVoxelSize = true;
        return NavMesh.AddNavMeshData(NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity));
    }

    private static Vector3 Target(CaveSpace s)
    {
        Vector3 p = s.SpaceKind == CaveSpace.Kind.Room || s.End == null ? s.transform.position : (s.transform.position + s.End.position) * 0.5f;
        return TargetOffsets.TryGetValue(s.name, out Vector3 o) ? p + o : p;
    }
}
