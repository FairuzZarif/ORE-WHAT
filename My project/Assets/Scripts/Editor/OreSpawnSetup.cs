using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Compiles authored cave geometry into reproducible, surface-tested candidate sockets after dressing.</summary>
public static class OreSpawnSetup
{
    public const string ConfigPath = "Assets/Mining/OreSpawnConfig.asset";
    private static readonly HashSet<string> Excluded = new HashSet<string>
    {
        "Entrance", "MainTunnel", "Cavern_01", "OreArea_01", "OldMine_Adit", "OldMine_Office",
        "OldMine_StoreDoor", "OldMine_Storeroom", "SideTunnel", "Tunnel_02", "Rift_Portal",
        "BossArena", "OldMine_ArenaGate", "OldMine_BossThreshold", "DeepHollow", "DeepMine_Incline_3",
        "Crystal_Geode", "Crystal_Descent_5", "Crystal_Lower_5", "Rift_Final", "Rift_DeepDescent"
    };

    public static OreSpawnConfig EnsureConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<OreSpawnConfig>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<OreSpawnConfig>();
            config.nodePrefab = KayKitMiningSetup.SharedRockPrefab();
            config.resources = new[] { "CopperOre", "IronOre", "GoldOre", "Crystal" }
                .Select(n => AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Items/" + n + ".asset")).ToArray();
            config.mines = new[]
            {
                Rule("Old Mine", 30, new[]{66f,31f,3f,0f}, new[]{30,30,2,0}, new[]{0f,0f,0.58f,1f}),
                Rule("Deep Mine", 24, new[]{30f,55f,15f,0f}, new[]{24,24,5,0}, new[]{0f,0f,0.4f,1f}),
                Rule("Crystal Caverns", 20, new[]{8f,37f,48f,7f}, new[]{20,20,11,2}, new[]{0f,0f,0.4f,0.6f}),
                Rule("The Rift", 20, new[]{3f,22f,50f,25f}, new[]{20,20,12,5}, new[]{0f,0f,0.55f,0.75f})
            };
            AssetDatabase.CreateAsset(config, ConfigPath);
        }
        return config; // Rebuilds preserve economy tuning.
    }

    private static OreSpawnConfig.MineRule Rule(string name, int target, float[] weights, int[] caps, float[] depth)
        => new OreSpawnConfig.MineRule { name = name, target = target, weights = weights, caps = caps, minimumDepth = depth };

    [MenuItem("Ore What/Island Map/Rebuild Ore Spawn Sockets")]
    public static void RebuildMenu()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Leave Play mode before rebuilding sockets."); return; }
        var cave = Object.FindAnyObjectByType<CaveLayout>();
        Debug.Log(Rebuild(cave));
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(cave.gameObject.scene);
        EditorSceneManager.SaveScene(cave.gameObject.scene);
    }

    public static bool Forbidden(CaveLayout cave, Vector3 point)
    {
        foreach (CaveSpace space in cave.Spaces)
            if (Excluded.Contains(space.name) && space.Bounds.Contains(point + Vector3.up)
                && space.Distance(point + Vector3.up, out _) < 2f) return true;
        foreach (MapZone zone in cave.transform.parent.GetComponentsInChildren<MapZone>())
            if (zone.Kind == MapZone.ZoneKind.Boss && Vector3.Distance(point, zone.transform.position) < zone.Radius + 2f) return true;
        return false;
    }

    public static bool AllowedSpace(string name) => IslandMapBuilder.MineOf(name) > 0 && !Excluded.Contains(name);

    public static string Rebuild(CaveLayout cave)
    {
        if (EditorApplication.isPlaying) return "Leave Play mode first.";
        if (cave == null) return "No cave layout found.";
        cave.Prepare();
        Transform island = cave.transform.parent, generated = island.Find("Mountain/Generated");
        // Remove both the old map nodes and the original three outdoor prototype nodes in this gameplay scene.
        foreach (RockHealth rock in Object.FindObjectsByType<RockHealth>(FindObjectsInactive.Include))
            if (rock.gameObject.scene == cave.gameObject.scene) Object.DestroyImmediate(rock.gameObject);
        Transform root = island.Find("OreSpawns");
        if (root == null) { root = new GameObject("OreSpawns").transform; root.SetParent(island, false); }
        var system = root.GetComponent<OreSpawnSystem>() ?? root.gameObject.AddComponent<OreSpawnSystem>();
        system.config = EnsureConfig();
        system.sockets.Clear();
        Physics.SyncTransforms();
        Bounds[] scenery = island.Find("Props").GetComponentsInChildren<Renderer>().Select(r => r.bounds).ToArray();
        var random = new System.Random(194731);
        foreach (CaveSpace space in cave.Spaces.OrderBy(s => s.name, System.StringComparer.Ordinal))
        {
            int mine = IslandMapBuilder.MineOf(space.name);
            if (mine < 1 || Excluded.Contains(space.name) || space.SpaceKind == CaveSpace.Kind.OpenCut) continue;
            int budget = space.SpaceKind == CaveSpace.Kind.Room ? 12 : 8;
            int accepted = 0;
            int groundCount = 0;
            for (int attempt = 0; attempt < 350 && accepted < budget; attempt++)
            {
                bool wall = attempt % 2 == 1;
                if (!wall && groundCount >= budget / 2) continue;
                Vector3 center, outward;
                float t = Mathf.Lerp(0.18f, 0.82f, (float)random.NextDouble());
                if (space.SpaceKind == CaveSpace.Kind.Room)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    outward = space.transform.rotation * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    float edge = Mathf.Lerp(0.52f, 0.78f, (float)random.NextDouble());
                    center = space.transform.position + space.transform.rotation * new Vector3(Mathf.Cos(angle) * space.Size.x * edge, 0, Mathf.Sin(angle) * space.Size.z * edge);
                }
                else
                {
                    outward = Vector3.Cross(Vector3.up, (space.End.position - space.transform.position).normalized).normalized * (random.NextDouble() < 0.5 ? -1 : 1);
                    center = Vector3.Lerp(space.transform.position, space.End.position, t) + outward * space.Size.x * 0.5f;
                }
                if (!Physics.Raycast(center + Vector3.up * 4f, Vector3.down, out RaycastHit floor, 12f, ~0, QueryTriggerInteraction.Ignore)
                    || !floor.collider.transform.IsChildOf(generated) || floor.normal.y < 0.8f) continue;
                Vector3 point = floor.point, normal = floor.normal, approach = floor.point + Vector3.up * 1.1f;
                if (wall)
                {
                    if (!Physics.Raycast(floor.point + Vector3.up * 1.35f, outward, out RaycastHit face, 12f, ~0, QueryTriggerInteraction.Ignore)
                        || !face.collider.transform.IsChildOf(generated) || Mathf.Abs(face.normal.y) > 0.35f) continue;
                    point = face.point; normal = face.normal;
                    // Standing position immediately in front of the deposit, tested against the actual floor.
                    Vector3 stand = point + normal * 1.6f;
                    if (!Physics.Raycast(stand + Vector3.up * 1.5f, Vector3.down, out RaycastHit support, 4f)
                        || !support.collider.transform.IsChildOf(generated) || support.normal.y < 0.8f
                        || point.y - support.point.y > 1.7f || point.y - support.point.y < 0.55f) continue;
                    approach = support.point + Vector3.up * 1.1f;
                }
                if (Forbidden(cave, point) || cave.Distance(approach, false) > -0.65f) continue;
                if (Blocked(generated, point, normal, approach, scenery)) continue;
                if (system.sockets.Any(s => Vector3.Distance(s.position, point) < 3.1f)) continue;
                float depth = space.DepthAt(t);
                var rule = system.config.mines[mine - 1];
                int allowed = wall ? 2 : 1;
                bool rare = false;
                for (int ore = 2; ore < 4; ore++)
                    if (rule.weights[ore] > 0 && depth >= rule.minimumDepth[ore]) { allowed |= 1 << ore; rare = true; }
                bool side = space.name.Contains("Pocket") || space.name.Contains("Abandoned") || space.name.Contains("SideChamber")
                    || space.name.Contains("Lower") || space.name.Contains("OldIncline");
                float probability = side ? 1.35f : 1f;
                if (space.name.Contains("Approach") || space.name.Contains("CollapsedHall")) probability = 0.3f;
                system.sockets.Add(new OreSpawnSystem.Socket { id = system.sockets.Count, mine = mine, space = space.name,
                    position = point, normal = normal, approach = approach, wall = wall, allowed = allowed, rare = rare,
                    depth = depth, probability = probability });
                accepted++;
                if (!wall) groundCount++;
            }
        }
        EditorUtility.SetDirty(system);
        return Report(system);
    }

    public static bool Blocked(Transform generated, Vector3 point, Vector3 normal, Vector3 approach, Bounds[] scenery = null)
    {
        foreach (Collider collider in Physics.OverlapCapsule(point + normal * 0.35f, point + normal * 0.9f, 0.7f, ~0, QueryTriggerInteraction.Ignore))
            if (!collider.transform.IsChildOf(generated)) return true;
        if (scenery != null)
            foreach (Bounds bounds in scenery)
                if (bounds.SqrDistance(point + normal * 0.45f) < 0.45f * 0.45f) return true;
        foreach (Collider collider in Physics.OverlapCapsule(approach - Vector3.up * 0.5f, approach + Vector3.up * 0.35f, 0.42f, ~0, QueryTriggerInteraction.Ignore))
            if (!collider.transform.IsChildOf(generated)) return true;
        return false;
    }

    public static string Report(OreSpawnSystem system)
    {
        var log = new StringBuilder("Ore sockets (no permanent edit-mode nodes):\n");
        for (int mine = 1; mine <= 4; mine++)
        {
            var sockets = system.sockets.Where(s => s.mine == mine).ToArray();
            log.AppendLine($"{system.config.mines[mine - 1].name}: potential {sockets.Length}, target {system.config.mines[mine - 1].target}; compatible Cu/Iron/Gold/Crystal = "
                + string.Join("/", Enumerable.Range(0, 4).Select(ore => sockets.Count(s => (s.allowed & (1 << ore)) != 0))));
        }
        return log.ToString();
    }

    [MenuItem("Ore What/Island Map/Validate Ore Spawns")]
    public static void ValidateMenu() => Debug.Log(Validate());

    public static string Validate()
    {
        var system = Object.FindAnyObjectByType<OreSpawnSystem>();
        var cave = Object.FindAnyObjectByType<CaveLayout>();
        if (system == null || cave == null) return "FAIL: Missing ore system or cave.";
        cave.Prepare(); Physics.SyncTransforms();
        Transform generated = cave.transform.parent.Find("Mountain/Generated");
        Bounds[] scenery = cave.transform.parent.Find("Props").GetComponentsInChildren<Renderer>().Select(r => r.bounds).ToArray();
        int failures = 0;
        var log = new StringBuilder();
        foreach (var socket in system.sockets)
        {
            string error = null;
            if (socket.mine < 1 || socket.mine > 4 || IslandMapBuilder.MineOf(socket.space) != socket.mine || Excluded.Contains(socket.space) || Forbidden(cave, socket.position)) error = "restricted area";
            else if (socket.mine <= 2 && (socket.allowed & 8) != 0) error = "forbidden Crystal";
            else if ((socket.allowed & 4) != 0 && socket.depth < system.config.mines[socket.mine - 1].minimumDepth[2]) error = "Gold too early";
            else if (Mathf.Abs(socket.normal.magnitude - 1) > 0.01f || (socket.wall ? Mathf.Abs(socket.normal.y) > 0.35f : socket.normal.y < 0.8f)) error = "bad surface normal";
            else if (!Physics.Raycast(socket.position + socket.normal * 0.5f, -socket.normal, out RaycastHit surface, 0.7f)
                || !surface.collider.transform.IsChildOf(generated) || Vector3.Distance(surface.point, socket.position) > 0.08f) error = "floating or wrong surface";
            else if (!EditorApplication.isPlaying && Blocked(generated, socket.position, socket.normal, socket.approach, scenery)) error = "prop obstruction";
            else if (system.sockets.Any(other => other.id != socket.id && Vector3.Distance(other.position, socket.position) < 3f)) error = "duplicate/overlapping socket";
            if (error != null) { failures++; log.AppendLine($"FAIL socket {socket.id} {socket.space}: {error}"); }
        }
        if (!EditorApplication.isPlaying)
            foreach (RockHealth rock in Object.FindObjectsByType<RockHealth>(FindObjectsInactive.Include)) { failures++; log.AppendLine("FAIL permanent node " + rock.name); }
        log.Append(Report(system));
        log.AppendLine($"Validation: {system.sockets.Count} sockets, {failures} failures.");
        return log.ToString();
    }
}
