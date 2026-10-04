using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

/// <summary>
/// Builds the island's main play area into the gameplay scene (PlayerTest): a big generated mountain
/// with the cave carved out of it, a path from the start to the cave entrance, lamps, mining rocks
/// and zone markers. Menu: Ore What > Island Map > Build Or Rebuild Island Map.
///
/// What you edit (kept between rebuilds): Environment/Island/Mountain (MountainShape numbers) and
/// Environment/Island/Cave (each CaveSpace child: move / resize rooms and tunnels, Depth).
/// What's regenerated every time: Mountain/Generated (rock mesh chunks, Assets/Models/Island),
/// Props, Lighting, MiningRocks, Zones, the terrain holes under the cave, and the path.
/// The first build backs up the terrain asset to Assets/Backups.
/// </summary>
public static class IslandMapBuilder
{
    private const string MeshFolder = "Assets/Models/Island";
    private const string MaterialFolder = "Assets/Materials/Island";
    private const string PrefabFolder = "Assets/Prefabs/Environment";
    private const string BackupFolder = "Assets/Backups";
    private const string RockSource = "Assets/Polytope Studio/Lowpoly_Environments/Sources/Materials/PT_Rocks_mat.mat";
    private const string GrassTexture = "Assets/Polytope Studio/Lowpoly_Environments/Sources/Textures/PT_Ground_Grass_Green_01.png";
    private const string DirtLayer = "Assets/Polytope Studio/Lowpoly_Demos/Environment_Free/Helpers/Ground_Layer_01.terrainlayer";
    private const string OreRockPrefab = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Rocks/PT_Ore_Rock_01.prefab";
    private const string RockFolder = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Rocks/";
    private const float ChunkSize = 64f;
    private const float MaxFogDensity = 0.004f; // was 0.007: the mountain (250-300 m away) vanished in the fog

    // Default layout, used only when Environment/Island doesn't exist yet.
    private static readonly Vector3 MountainCentre = new Vector3(500f, 0f, 770f);
    private static readonly Vector2[] PathPoints =
    {
        new Vector2(500f, 507f), new Vector2(503f, 535f), new Vector2(497f, 565f), new Vector2(500f, 598f),
    };

    [MenuItem("Ore What/Island Map/Build Or Rebuild Island Map")]
    public static void BuildMenu() => Debug.Log("[Ore What] " + Build());

    public static string Build()
    {
        if (EditorApplication.isPlaying) return "Leave Play mode first.";
        var log = new StringBuilder();
        var timer = Stopwatch.StartNew();

        Terrain terrain = MainTerrain(log);
        if (terrain == null) return "No terrain found.";
        Transform island = Child(terrain.transform.parent, "Island");
        var mountain = island.Find("Mountain") != null ? island.Find("Mountain").GetComponent<MountainShape>() : null;
        var cave = island.Find("Cave") != null ? island.Find("Cave").GetComponent<CaveLayout>() : null;
        if (mountain == null) mountain = CreateMountain(island, terrain);
        if (cave == null) cave = CreateCave(island, terrain);
        cave.Prepare();

        BackupTerrain(terrain, log);
        Material[] rockMaterials = { RockMaterial("Mountain_Rock", 0), RockMaterial("Cave_Rock", 1), RockMaterial("Cave_Rock_Deep", 2) };
        Transform generated = Child(mountain.transform, "Generated");
        Clear(generated);
        log.AppendLine(GenerateMesh(mountain, cave, terrain, generated, rockMaterials));
        Physics.SyncTransforms();

        log.AppendLine(EditTerrain(terrain, mountain, cave));
        log.AppendLine(Dress(island, mountain, cave, terrain, generated));

        if (RenderSettings.fogDensity > MaxFogDensity)
        {
            log.AppendLine($"Fog density {RenderSettings.fogDensity} -> {MaxFogDensity} so the mountain shows from the start.");
            RenderSettings.fogDensity = MaxFogDensity;
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(island.gameObject.scene);
        EditorSceneManager.SaveScene(island.gameObject.scene);
        log.Append($"Done in {timer.Elapsed.TotalSeconds:F1} s.");
        return log.ToString();
    }

    // ---- Scene objects --------------------------------------------------------------------------

    private static Terrain MainTerrain(StringBuilder log)
    {
        Terrain main = null;
        foreach (Terrain t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            if (t.gameObject.activeInHierarchy && (main == null || t.name == "Terrain")) main = t;
        if (main == null) return null;
        // The scene had two terrains with the same data at the same place (everything drawn twice).
        foreach (Terrain t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            if (t != main && t.terrainData == main.terrainData && t.gameObject.activeSelf)
            {
                t.gameObject.SetActive(false);
                log.AppendLine($"Disabled duplicate terrain '{t.name}' (same TerrainData as '{main.name}').");
            }
        return main;
    }

    private static MountainShape CreateMountain(Transform island, Terrain terrain)
    {
        var go = new GameObject("Mountain");
        go.transform.SetParent(island, false);
        go.transform.position = new Vector3(MountainCentre.x, Height(terrain, 500f, 650f), MountainCentre.z);
        return go.AddComponent<MountainShape>();
    }

    private static CaveLayout CreateCave(Transform island, Terrain terrain)
    {
        var go = new GameObject("Cave");
        go.transform.SetParent(island, false);
        var cave = go.AddComponent<CaveLayout>();
        go.AddComponent<CaveAtmosphere>();

        // Floor of the entrance: just above the highest ground it crosses, so no terrain pokes into it.
        float f0 = float.MinValue;
        for (float z = 640f; z <= 706f; z += 2f)
            for (float x = 486f; x <= 514f; x += 2f) f0 = Mathf.Max(f0, Height(terrain, x, z));
        f0 += 0.3f;
        float cutStart = Height(terrain, 500f, 598f) + 0.1f;

        Cut(go.transform, "Entrance", new Vector3(500f, cutStart, 598f), new Vector3(500f, f0, 645f), 15f);
        Tunnel(go.transform, "MainTunnel", new Vector3(500f, f0, 645f), new Vector3(500f, f0 - 2f, 700f), 10f, 13f, 0f, 0.12f);
        Room(go.transform, "Cavern_01", new Vector3(502f, f0 - 2f, 722f), new Vector3(26f, 18f, 22f), 10f, 0.15f);
        Room(go.transform, "OreArea_01", new Vector3(474f, f0 - 2f, 730f), new Vector3(11f, 9f, 10f), 0f, 0.17f);
        Tunnel(go.transform, "SideTunnel", new Vector3(524f, f0 - 2f, 728f), new Vector3(560f, f0 - 5f, 752f), 6.5f, 8f, 0.18f, 0.25f);
        Room(go.transform, "SideCave", new Vector3(570f, f0 - 5f, 760f), new Vector3(14f, 11f, 13f), 30f, 0.27f);
        Tunnel(go.transform, "Tunnel_02", new Vector3(500f, f0 - 2f, 738f), new Vector3(490f, f0 - 12f, 790f), 8.5f, 11f, 0.2f, 0.4f);
        Room(go.transform, "Cavern_02", new Vector3(488f, f0 - 12f, 812f), new Vector3(30f, 20f, 24f), -15f, 0.42f);
        Room(go.transform, "MiningArea_02", new Vector3(450f, f0 - 12f, 805f), new Vector3(14f, 10f, 13f), 20f, 0.45f);
        Tunnel(go.transform, "CombatTunnel", new Vector3(515f, f0 - 12f, 815f), new Vector3(545f, f0 - 14f, 826f), 8f, 10f, 0.45f, 0.5f);
        Room(go.transform, "CombatArea", new Vector3(562f, f0 - 14f, 830f), new Vector3(21f, 14f, 18f), 0f, 0.52f);
        Tunnel(go.transform, "DeepTunnel_A", new Vector3(478f, f0 - 12f, 832f), new Vector3(452f, f0 - 20f, 860f), 8f, 11f, 0.5f, 0.6f);
        Tunnel(go.transform, "DeepTunnel_B", new Vector3(452f, f0 - 20f, 860f), new Vector3(418f, f0 - 27f, 852f), 8f, 11f, 0.6f, 0.68f);
        Tunnel(go.transform, "DeepTunnel_C", new Vector3(418f, f0 - 27f, 852f), new Vector3(402f, f0 - 36f, 818f), 8f, 11f, 0.68f, 0.76f);
        Room(go.transform, "DeepCavern", new Vector3(400f, f0 - 36f, 795f), new Vector3(30f, 20f, 26f), 0f, 0.8f);
        Tunnel(go.transform, "BossGate", new Vector3(400f, f0 - 36f, 770f), new Vector3(405f, f0 - 38f, 752f), 9f, 12f, 0.85f, 0.92f);
        Room(go.transform, "BossArena", new Vector3(410f, f0 - 38f, 720f), new Vector3(34f, 24f, 30f), 0f, 1f);
        ApplyFloorShapes(cave);
        return cave;
    }

    // Floor shapes per cave space: (roll, step height, step ramp share, plateau height, plateau offset, radius, ramp).
    // Steps/ramps stay well under the player's 45° slope limit (steepest ≈ 1.5 × height / ramp length), so the whole
    // route can be walked both ways (also uphill carrying ore) without jumping. Plateaus sit away from tunnel joins.
    private static readonly Dictionary<string, (float roll, float steps, float share, float plateau, Vector2 offset, float radius, float ramp)> FloorShapes =
        new Dictionary<string, (float, float, float, float, Vector2, float, float)>
    {
        { "MainTunnel",    (0.3f, 1.0f,  0.5f, 0f,   Vector2.zero,              5f,   4f) },
        { "Cavern_01",     (0.6f, 0f,    0.5f, 0f,   Vector2.zero,              5f,   4f) },
        { "OreArea_01",    (0.3f, 0f,    0.5f, 0f,   Vector2.zero,              5f,   4f) },
        { "SideTunnel",    (0.3f, 1.0f,  0.5f, 0f,   Vector2.zero,              5f,   4f) },
        { "SideCave",      (0.4f, 0f,    0.5f, 1.4f, new Vector2(2.7f, 7.3f),   3.5f, 4f) },   // ledge away from the side tunnel
        { "Tunnel_02",     (0.3f, 2.0f,  0.45f, 0f,   Vector2.zero,              5f,   4f) },
        { "Cavern_02",     (0.6f, 0f,    0.5f, 0f,   Vector2.zero,              5f,   4f) },
        { "MiningArea_02", (0.3f, 0f,    0.5f, 1.0f, new Vector2(-5.6f, -2.1f), 2.5f, 3.5f) }, // mining shelf at the back
        { "CombatTunnel",  (0.3f, 1.0f,  0.5f, 0f,   Vector2.zero,              5f,   4f) },
        { "CombatArea",    (0.4f, 0f,    0.5f, 1.6f, new Vector2(9f, 2f),       5f,   4.5f) }, // high ground for fights
        { "DeepTunnel_A",  (0.3f, 2.0f,  0.55f, 0f,   Vector2.zero,              5f,   4f) },
        { "DeepTunnel_B",  (0.3f, 2.0f,  0.55f, 0f,   Vector2.zero,              5f,   4f) },
        { "DeepTunnel_C",  (0.3f, 2.0f,  0.65f, 0f,   Vector2.zero,              5f,   4f) },
        { "DeepCavern",    (0.5f, 0f,    0.5f, 2.0f, new Vector2(15f, 0f),      6f,   6f) },   // raised shelf east of the route
        { "BossGate",      (0.2f, 1.0f,  0.6f, 0f,   Vector2.zero,              5f,   4f) },
        { "BossArena",     (0f,   0f,    0.5f, 0f,   Vector2.zero,              5f,   4f) },   // flat: the arena platform sits on it
    };

    /// <summary>Gives the cave spaces their default floor shapes (steps, roll, plateaus). Rebuild afterwards.</summary>
    [MenuItem("Ore What/Island Map/Apply Default Floor Shapes")]
    public static void ApplyFloorShapesMenu()
    {
        var caveT = GameObject.Find("Island") != null ? GameObject.Find("Island").transform.Find("Cave") : null;
        if (caveT == null) { Debug.LogError("[Ore What] No Environment/Island/Cave in the open scene."); return; }
        ApplyFloorShapes(caveT.GetComponent<CaveLayout>());
        EditorSceneManager.MarkSceneDirty(caveT.gameObject.scene);
        Debug.Log("[Ore What] Floor shapes applied. Run Build Or Rebuild Island Map to regenerate the rock.");
    }

    private static void ApplyFloorShapes(CaveLayout cave)
    {
        foreach (CaveSpace s in cave.GetComponentsInChildren<CaveSpace>())
        {
            if (!FloorShapes.TryGetValue(s.name, out var f)) continue;
            Undo.RecordObject(s, "Floor Shapes");
            s.SetFloorShape(f.roll, f.steps, f.share, f.plateau, f.offset, f.radius, f.ramp);
            EditorUtility.SetDirty(s);
        }
    }

    private static CaveSpace NewSpace(Transform parent, string name, Vector3 floor, CaveSpace.Kind kind, Vector3 size,
                                      float depth, float endDepth, Vector3? end)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = floor;
        var space = go.AddComponent<CaveSpace>();
        var so = new SerializedObject(space);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.FindProperty("size").vector3Value = size;
        so.FindProperty("depth").floatValue = depth;
        so.FindProperty("endDepth").floatValue = endDepth;
        if (end.HasValue)
        {
            var e = new GameObject("End").transform;
            e.SetParent(go.transform, false);
            e.position = end.Value;
            so.FindProperty("end").objectReferenceValue = e;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return space;
    }

    private static void Room(Transform p, string name, Vector3 floor, Vector3 size, float yaw, float depth)
        => NewSpace(p, name, floor, CaveSpace.Kind.Room, size, depth, depth, null).transform.rotation = Quaternion.Euler(0f, yaw, 0f);

    private static void Tunnel(Transform p, string name, Vector3 a, Vector3 b, float halfWidth, float height, float d0, float d1)
        => NewSpace(p, name, a, CaveSpace.Kind.Tunnel, new Vector3(halfWidth, height, 0f), d0, d1, b);

    private static void Cut(Transform p, string name, Vector3 a, Vector3 b, float halfWidth)
        => NewSpace(p, name, a, CaveSpace.Kind.OpenCut, new Vector3(halfWidth, 0f, 0f), 0f, 0f, b);

    private static Transform Child(Transform parent, string name)
    {
        Transform t = parent != null ? parent.Find(name) : null;
        if (t != null) return t;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void Clear(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--) Object.DestroyImmediate(t.GetChild(i).gameObject);
    }

    private static float Height(Terrain t, float x, float z) => t.SampleHeight(new Vector3(x, 0f, z)) + t.transform.position.y;

    // ---- Materials and meshes -------------------------------------------------------------------

    private static Material RockMaterial(string name, int kind)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing; // keep hand tweaks
        Directory.CreateDirectory(MaterialFolder);
        var m = new Material(AssetDatabase.LoadAssetAtPath<Material>(RockSource)) { name = name };
        m.SetTexture("_BaseTexture", null);                                     // colour comes from the gradient
        m.SetFloat("_DECALSONOFF", 0f); m.DisableKeyword("_DECALSONOFF_ON");   // no runes on the generated rock
        m.SetFloat("_DECALEMISSIONONOFF", 0f); // the rune glow reads UV2 even with decals off; the generated mesh has no UVs (glowing specks)
        m.SetFloat("_GRADIENTONOFF", 1f); m.EnableKeyword("_GRADIENTONOFF_ON");
        m.SetFloat("_WorldObjectGradient", 0f);                                // gradient by world height
        m.SetFloat("_Smoothness", 0f); // matte stone
        if (kind == 0)
        {
            m.SetFloat("_Gradient", 0.0045f); // dark at the foot (~18 m), light at the top (~220 m)
            m.SetColor("_GroundColor", new Color(0.2f, 0.18f, 0.17f)); // dark: the scene's sun (2.3) + ambient (1.15) are bright
            m.SetColor("_TopColor", new Color(0.47f, 0.45f, 0.43f));
            m.SetFloat("_TOPPROJECTIONONOFF", 1f); m.EnableKeyword("_TOPPROJECTIONONOFF_ON"); // grass on the ledges
            m.SetTexture("_TopProjectionTexture", AssetDatabase.LoadAssetAtPath<Texture2D>(GrassTexture));
            m.SetFloat("_TopProjectionTextureTiling", 0.2f);
            m.SetFloat("_TopProjectionTextureCoverage", 0.42f);
            m.SetFloat("_OREEMISSIONONOFF", 0f);
        }
        else
        {
            bool deep = kind == 2;
            m.SetFloat("_Gradient", 0f); // one colour: Ground Color
            m.SetColor("_GroundColor", deep ? new Color(0.3f, 0.3f, 0.36f) : new Color(0.34f, 0.29f, 0.25f));
            m.SetFloat("_TOPPROJECTIONONOFF", 0f); m.DisableKeyword("_TOPPROJECTIONONOFF_ON");
            m.SetFloat("_OREEMISSIONONOFF", 0f); // no glowing ore painted on the walls
            // Kept modest: brighter values blow out to flat white patches with the bloom.
            m.SetColor("_OreColor", deep ? new Color(0.12f, 0.45f, 0.7f) : new Color(0.75f, 0.38f, 0.1f));
            m.SetColor("_OreEmissionColor", deep ? new Color(0.15f, 0.9f, 1.6f) : new Color(1.2f, 0.5f, 0.08f));
            m.SetFloat("_OreEmissionIntensity", 1f);
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static Material SimpleMaterial(string name, Color color, Color emission)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(MaterialFolder);
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static Material CrystalMaterial()
    {
        string path = $"{MaterialFolder}/Crystal_Rock.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Directory.CreateDirectory(MaterialFolder);
        m = new Material(AssetDatabase.LoadAssetAtPath<Material>(RockSource)) { name = "Crystal_Rock" };
        m.SetFloat("_DECALSONOFF", 0f); m.DisableKeyword("_DECALSONOFF_ON");
        m.SetColor("_OreColor", new Color(0.25f, 1.1f, 1.7f));
        m.SetColor("_OreEmissionColor", new Color(0.6f, 3f, 5f));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>Saves a mesh at path, reusing the existing asset (so nothing is deleted).</summary>
    private static Mesh SaveMesh(Mesh mesh, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        // Rewrite through the Mesh API. (EditorUtility.CopySerialized into an existing mesh left its GPU
        // buffers partly stale: holes in the rendered rock where the collider was fine.)
        existing.Clear();
        existing.indexFormat = mesh.indexFormat;
        existing.SetVertices(mesh.vertices);
        existing.SetNormals(mesh.normals);
        if (mesh.colors32.Length > 0) existing.SetColors(mesh.colors32);
        if (mesh.uv.Length > 0) existing.SetUVs(0, mesh.uv);
        existing.subMeshCount = mesh.subMeshCount;
        for (int s = 0; s < mesh.subMeshCount; s++) existing.SetTriangles(mesh.GetTriangles(s), s, false);
        existing.RecalculateBounds();
        existing.name = Path.GetFileNameWithoutExtension(path);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    // ---- Mountain + cave mesh (surface nets on a voxel grid) ------------------------------------

    private class Chunk
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Color32> c = new List<Color32>();
        public readonly List<int>[] i = { new List<int>(), new List<int>(), new List<int>() };
    }

    private static string GenerateMesh(MountainShape mountain, CaveLayout cave, Terrain terrain, Transform parent, Material[] materials)
    {
        var timer = Stopwatch.StartNew();
        float vs = mountain.VoxelSize;
        Bounds bounds = mountain.GetBounds();
        foreach (CaveSpace s in cave.Spaces)
            if (s.SpaceKind != CaveSpace.Kind.OpenCut) bounds.Encapsulate(s.Bounds);
        Vector3 min = bounds.min - Vector3.one * vs * 2f, max = bounds.max + Vector3.one * vs * 2f;
        int nx = Mathf.CeilToInt((max.x - min.x) / vs) + 1, ny = Mathf.CeilToInt((max.y - min.y) / vs) + 1, nz = Mathf.CeilToInt((max.z - min.z) / vs) + 1;

        // 1. Sample: negative = rock. Mountain, minus the cave.
        var field = new float[nx * ny * nz];
        for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                float wx = min.x + x * vs, wz = min.z + z * vs;
                float surface = mountain.SurfaceHeight(wx, wz);
                for (int y = 0; y < ny; y++)
                {
                    var p = new Vector3(wx, min.y + y * vs, wz);
                    float d = mountain.Distance(p, surface);
                    if (d < 8f) d = Mathf.Max(d, -cave.Distance(p));
                    field[x + nx * (y + ny * z)] = d;
                }
            }
        long sampleMs = timer.ElapsedMilliseconds;

        // 2. Surface nets: one vertex per cell the surface crosses (average of its edge crossings).
        //    Fewer, more even triangles than marching cubes/tetrahedra: suits the faceted low-poly look.
        int cx = nx - 1, cy = ny - 1, cz = nz - 1;
        var cellVertex = new int[cx * cy * cz];
        var vertices = new List<Vector3>();
        int[] ox = { 0, 1, 0, 1, 0, 1, 0, 1 }, oy = { 0, 0, 1, 1, 0, 0, 1, 1 }, oz = { 0, 0, 0, 0, 1, 1, 1, 1 };
        int[,] edges = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
        var corner = new float[8];
        for (int z = 0; z < cz; z++)
            for (int y = 0; y < cy; y++)
                for (int x = 0; x < cx; x++)
                {
                    int ci = x + cx * (y + cy * z);
                    cellVertex[ci] = -1;
                    int mask = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        corner[k] = field[(x + ox[k]) + nx * ((y + oy[k]) + ny * (z + oz[k]))];
                        if (corner[k] < 0f) mask |= 1 << k;
                    }
                    if (mask == 0 || mask == 255) continue;
                    Vector3 sum = Vector3.zero;
                    int count = 0;
                    for (int e = 0; e < 12; e++)
                    {
                        int a = edges[e, 0], b = edges[e, 1];
                        if ((corner[a] < 0f) == (corner[b] < 0f)) continue;
                        sum += Vector3.Lerp(new Vector3(ox[a], oy[a], oz[a]), new Vector3(ox[b], oy[b], oz[b]), corner[a] / (corner[a] - corner[b]));
                        count++;
                    }
                    cellVertex[ci] = vertices.Count;
                    vertices.Add(min + (new Vector3(x, y, z) + sum / count) * vs);
                }

        // 3. A quad across every grid edge with a sign change; triangles sorted into chunks and materials.
        var chunks = new Dictionary<Vector2Int, Chunk>();
        int culled = 0, triangles = 0;
        float terrainY = terrain.transform.position.y;

        void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            float len = normal.magnitude;
            if (len < 1e-5f) return;
            normal /= len;
            Vector3 centre = (a + b + c) / 3f;
            if (Vector3.Dot(normal, outward) < 0f) { Vector3 tmp = b; b = c; c = tmp; normal = -normal; } // face the air
            cave.Sample(centre, out float caveDistance, out float depth, out CaveSpace nearest);
            bool inCave = caveDistance < 2.5f;
            if (!inCave && Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < terrain.SampleHeight(centre) + terrainY - 1f) { culled++; return; } // under the ground
            // The open-air ravine floor uses the cave rock too (the mountain material would grow grass on it).
            bool ravineFloor = !inCave && normal.y > 0.5f && cave.Distance(centre) < 2.5f;
            int sub = inCave ? (depth >= 0.55f ? 2 : 1) : ravineFloor ? 1 : 0;
            var key = new Vector2Int(Mathf.FloorToInt((centre.x - min.x) / ChunkSize), Mathf.FloorToInt((centre.z - min.z) / ChunkSize));
            if (!chunks.TryGetValue(key, out Chunk chunk)) chunks[key] = chunk = new Chunk();
            int start = chunk.v.Count;
            chunk.v.Add(a); chunk.v.Add(b); chunk.v.Add(c);
            for (int k = 0; k < 3; k++) { chunk.n.Add(normal); chunk.c.Add(new Color32(255, 255, 255, 255)); } // alpha < 1 would paint glowing ore
            chunk.i[sub].Add(start); chunk.i[sub].Add(start + 1); chunk.i[sub].Add(start + 2);
            triangles++;
        }

        void AddQuad(int c0, int c1, int c2, int c3, Vector3 outward)
        {
            if (c0 < 0 || c1 < 0 || c2 < 0 || c3 < 0) return;
            Vector3 v0 = vertices[c0], v1 = vertices[c1], v2 = vertices[c2], v3 = vertices[c3];
            // Split along the diagonal whose two triangles face the most alike (flatter, no folded "bow-ties").
            float split02 = Vector3.Dot(Vector3.Cross(v1 - v0, v2 - v0).normalized, Vector3.Cross(v2 - v0, v3 - v0).normalized);
            float split13 = Vector3.Dot(Vector3.Cross(v1 - v0, v3 - v0).normalized, Vector3.Cross(v2 - v1, v3 - v1).normalized);
            if (split02 >= split13) { AddTriangle(v0, v1, v2, outward); AddTriangle(v0, v2, v3, outward); }
            else { AddTriangle(v0, v1, v3, outward); AddTriangle(v1, v2, v3, outward); }
        }

        int Cell(int x, int y, int z) => cellVertex[x + cx * (y + cy * z)];

        for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    bool solid = field[x + nx * (y + ny * z)] < 0f;
                    float sign = solid ? 1f : -1f; // the face points from the rock sample to the air sample
                    if (x < nx - 1 && y >= 1 && y <= ny - 2 && z >= 1 && z <= nz - 2 && solid != field[(x + 1) + nx * (y + ny * z)] < 0f)
                        AddQuad(Cell(x, y - 1, z - 1), Cell(x, y, z - 1), Cell(x, y, z), Cell(x, y - 1, z), Vector3.right * sign);
                    if (y < ny - 1 && x >= 1 && x <= nx - 2 && z >= 1 && z <= nz - 2 && solid != field[x + nx * ((y + 1) + ny * z)] < 0f)
                        AddQuad(Cell(x - 1, y, z - 1), Cell(x, y, z - 1), Cell(x, y, z), Cell(x - 1, y, z), Vector3.up * sign);
                    if (z < nz - 1 && x >= 1 && x <= nx - 2 && y >= 1 && y <= ny - 2 && solid != field[x + nx * (y + ny * (z + 1))] < 0f)
                        AddQuad(Cell(x - 1, y - 1, z), Cell(x, y - 1, z), Cell(x, y, z), Cell(x - 1, y, z), Vector3.forward * sign);
                }

        // 4. Chunk objects: mesh asset + renderer + collider.
        int vertexTotal = 0;
        foreach (var pair in chunks)
        {
            Chunk ch = pair.Value;
            string name = $"Mountain_{pair.Key.x}_{pair.Key.y}";
            var mesh = new Mesh { name = name, indexFormat = ch.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(ch.v);
            mesh.SetNormals(ch.n);
            mesh.SetColors(ch.c);
            mesh.subMeshCount = 3;
            for (int s = 0; s < 3; s++) mesh.SetTriangles(ch.i[s], s, false);
            mesh.RecalculateBounds();
            mesh = SaveMesh(mesh, $"{MeshFolder}/{name}.asset");
            vertexTotal += ch.v.Count;

            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // vertices are in world space
            go.transform.localScale = Vector3.one;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = materials;
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        }
        return $"Mountain mesh: grid {nx}x{ny}x{nz} at {vs} m (sampling {sampleMs} ms, total {timer.ElapsedMilliseconds} ms), " +
               $"{triangles} triangles, {vertexTotal} vertices in {chunks.Count} chunks, {culled} hidden under the terrain skipped.";
    }

    // ---- Terrain --------------------------------------------------------------------------------

    private static void BackupTerrain(Terrain terrain, StringBuilder log)
    {
        string source = AssetDatabase.GetAssetPath(terrain.terrainData);
        string backup = $"{BackupFolder}/{Path.GetFileNameWithoutExtension(source)} (before island map).asset";
        if (string.IsNullOrEmpty(source) || File.Exists(backup)) return;
        Directory.CreateDirectory(BackupFolder);
        AssetDatabase.Refresh();
        if (AssetDatabase.CopyAsset(source, backup)) log.AppendLine("Terrain backed up to " + backup);
    }

    private static float PathDistance(float x, float z)
    {
        float best = float.MaxValue;
        var p = new Vector2(x, z);
        for (int i = 0; i < PathPoints.Length - 1; i++)
        {
            Vector2 a = PathPoints[i], ab = PathPoints[i + 1] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    private static readonly Vector2 Plaza = new Vector2(500f, 600f); // the open ground in front of the ravine

    private static string EditTerrain(Terrain terrain, MountainShape mountain, CaveLayout cave)
    {
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position, size = data.size;
        float lowest = float.MaxValue;
        foreach (CaveSpace s in cave.Spaces) lowest = Mathf.Min(lowest, s.transform.position.y);

        // Holes wherever the cave is below the ground (a heightfield collider is solid underneath).
        // Only under rock (the mountain hides them); grown by one cell for safety.
        int hr = data.holesResolution;
        var hole = new bool[hr, hr];
        for (int i = 0; i < hr; i++)
            for (int j = 0; j < hr; j++)
            {
                float wx = origin.x + (j + 0.5f) / hr * size.x, wz = origin.z + (i + 0.5f) / hr * size.z;
                float ground = data.GetInterpolatedHeight((j + 0.5f) / hr, (i + 0.5f) / hr) + origin.y;
                if (mountain.SurfaceHeight(wx, wz) < ground + 8f) continue;
                for (float y = ground + 1f; y > lowest - 2f; y -= 2f)
                    if (cave.Distance(new Vector3(wx, y, wz), false) < 1.5f) { hole[i, j] = true; break; }
            }
        var surface = new bool[hr, hr];
        int holes = 0;
        for (int i = 0; i < hr; i++)
            for (int j = 0; j < hr; j++)
            {
                bool h = false;
                for (int di = -1; di <= 1 && !h; di++)
                    for (int dj = -1; dj <= 1 && !h; dj++)
                    {
                        int a = i + di, b = j + dj;
                        h = a >= 0 && b >= 0 && a < hr && b < hr && hole[a, b];
                    }
                if (h)
                {
                    float wx = origin.x + (j + 0.5f) / hr * size.x, wz = origin.z + (i + 0.5f) / hr * size.z;
                    float ground = data.GetInterpolatedHeight((j + 0.5f) / hr, (i + 0.5f) / hr) + origin.y;
                    h = mountain.SurfaceHeight(wx, wz) > ground + 6f;
                }
                surface[i, j] = !h;
                if (h) holes++;
            }
        data.SetHoles(0, 0, surface);

        // Trees: none in the rock, on the path, or around the ravine.
        var kept = new List<TreeInstance>();
        int removedTrees = 0;
        foreach (TreeInstance t in data.treeInstances)
        {
            Vector3 w = Vector3.Scale(t.position, size) + origin;
            bool remove = mountain.SurfaceHeight(w.x, w.z) > w.y - 4f || PathDistance(w.x, w.z) < 8f || Vector2.Distance(new Vector2(w.x, w.z), Plaza) < 26f;
            if (remove) removedTrees++; else kept.Add(t);
        }
        data.treeInstances = kept.ToArray();

        // Grass: none under/against the rock, on the path, or in front of the ravine.
        int dr = data.detailResolution;
        for (int layer = 0; layer < data.detailPrototypes.Length; layer++)
        {
            int[,] d = data.GetDetailLayer(0, 0, dr, dr, layer);
            for (int i = 0; i < dr; i++)
                for (int j = 0; j < dr; j++)
                {
                    if (d[i, j] == 0) continue;
                    float wx = origin.x + (j + 0.5f) / dr * size.x, wz = origin.z + (i + 0.5f) / dr * size.z;
                    if (PathDistance(wx, wz) < 3.5f || Vector2.Distance(new Vector2(wx, wz), Plaza) < 18f) { d[i, j] = 0; continue; }
                    if (Mathf.Abs(wz - mountain.transform.position.z) > 260f || Mathf.Abs(wx - mountain.transform.position.x) > 260f) continue;
                    float ground = data.GetInterpolatedHeight((j + 0.5f) / dr, (i + 0.5f) / dr) + origin.y;
                    if (mountain.SurfaceHeight(wx, wz) > ground - 1f) d[i, j] = 0;
                }
            data.SetDetailLayer(0, 0, layer, d);
        }

        // Dirt path from the start to the ravine.
        var dirt = AssetDatabase.LoadAssetAtPath<TerrainLayer>(DirtLayer);
        if (dirt != null)
        {
            var layers = new List<TerrainLayer>(data.terrainLayers);
            int dirtIndex = layers.IndexOf(dirt);
            if (dirtIndex < 0) { layers.Add(dirt); data.terrainLayers = layers.ToArray(); dirtIndex = layers.Count - 1; }
            int ar = data.alphamapResolution, count = data.alphamapLayers;
            float[,,] alpha = data.GetAlphamaps(0, 0, ar, ar);
            for (int i = 0; i < ar; i++)
                for (int j = 0; j < ar; j++)
                {
                    float wx = origin.x + (j + 0.5f) / ar * size.x, wz = origin.z + (i + 0.5f) / ar * size.z;
                    float w = Mathf.Max(1f - Mathf.InverseLerp(2.5f, 4.5f, PathDistance(wx, wz)),
                                        1f - Mathf.InverseLerp(12f, 17f, Vector2.Distance(new Vector2(wx, wz), Plaza)));
                    if (w <= alpha[i, j, dirtIndex]) continue;
                    for (int l = 0; l < count; l++) alpha[i, j, l] = l == dirtIndex ? w : alpha[i, j, l] * (1f - w);
                }
            data.SetAlphamaps(0, 0, alpha);
        }
        terrain.Flush();
        return $"Terrain: {holes} hole cells under the cave, {removedTrees} trees removed (from the mountain, path and ravine), grass cleared there, dirt path painted.";
    }

    // ---- Props, lights, mining rocks, zones -----------------------------------------------------

    private class PropMesh
    {
        private static Mesh cube, cylinder;
        private readonly Material[] materials;
        private readonly List<CombineInstance>[] parts;

        public PropMesh(params Material[] mats)
        {
            materials = mats;
            parts = new List<CombineInstance>[mats.Length];
            for (int i = 0; i < mats.Length; i++) parts[i] = new List<CombineInstance>();
            if (cube == null) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            if (cylinder == null) cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
        }

        public void Box(Vector3 pos, Quaternion rot, Vector3 size, int mat) => parts[mat].Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(pos, rot, size) });
        public void Cyl(Vector3 pos, Quaternion rot, Vector3 size, int mat) => parts[mat].Add(new CombineInstance { mesh = cylinder, transform = Matrix4x4.TRS(pos, rot, size) });

        /// <summary>One object, one mesh (a submesh per material), saved as an asset.</summary>
        public GameObject Build(string name, Transform parent, Vector3 position, string assetName)
        {
            var subs = new List<CombineInstance>();
            var used = new List<Material>();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Count == 0) continue;
                var m = new Mesh { indexFormat = IndexFormat.UInt32 };
                m.CombineMeshes(parts[i].ToArray(), true, true);
                subs.Add(new CombineInstance { mesh = m, transform = Matrix4x4.identity });
                used.Add(materials[i]);
            }
            var mesh = new Mesh { name = assetName, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(subs.ToArray(), false, false);
            foreach (var s in subs) Object.DestroyImmediate(s.mesh);
            mesh = SaveMesh(mesh, $"{MeshFolder}/Props/{assetName}.asset");
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterials = used.ToArray();
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }
    }

    private class Dresser
    {
        public CaveLayout cave;
        public Transform generated;
        public Terrain terrain;
        public Material wood, metal, glow, crystal;
        public GameObject lampPrefab, rockPrefab, oreRockPrefab;
        public readonly List<Vector3> used = new List<Vector3>();
        public int lamps, lights, rocks;

        /// <summary>Floor point under p on the generated rock, with standing room above it.</summary>
        public bool Floor(Vector3 p, out Vector3 floor, float clearance = 2.2f)
        {
            floor = p;
            if (!Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (!hit.collider.transform.IsChildOf(generated) || hit.normal.y < 0.75f) return false;
            floor = hit.point;
            if (cave.Distance(floor + Vector3.up * clearance, false) > -0.6f) return false; // too close to a wall / ceiling
            foreach (Vector3 u in used) if ((u - floor).sqrMagnitude < 9f) return false;
            return true;
        }

        /// <summary>A free floor spot near the walls of a space (rooms: around the edge; tunnels: along a side).</summary>
        public bool Spot(CaveSpace s, System.Random rnd, float edgeMin, float edgeMax, out Vector3 floor, float clearance = 2.2f)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                Vector3 p;
                float f = Mathf.Lerp(edgeMin, edgeMax, (float)rnd.NextDouble());
                if (s.SpaceKind == CaveSpace.Kind.Room)
                {
                    float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    p = s.transform.position + s.transform.rotation * new Vector3(Mathf.Cos(a) * s.Size.x * f, 0f, Mathf.Sin(a) * s.Size.z * f);
                }
                else
                {
                    float t = Mathf.Lerp(0.2f, 0.8f, (float)rnd.NextDouble());
                    Vector3 a = s.transform.position, b = s.End.position;
                    Vector3 side = Vector3.Cross(Vector3.up, (b - a).normalized).normalized * (rnd.NextDouble() < 0.5 ? -1f : 1f);
                    p = Vector3.Lerp(a, b, t) + side * s.Size.x * f;
                }
                if (Floor(p, out floor, clearance)) { used.Add(floor); return true; }
            }
            floor = Vector3.zero;
            return false;
        }

        public void Lamp(Transform parent, Vector3 floor, Vector3 faceToward, Color color, float intensity, float range, bool withLight)
        {
            var lamp = (GameObject)PrefabUtility.InstantiatePrefab(lampPrefab, parent);
            Vector3 dir = faceToward - floor; dir.y = 0f;
            lamp.transform.SetPositionAndRotation(floor, (dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity) * Quaternion.Euler(0f, -90f, 0f));
            var light = lamp.GetComponentInChildren<Light>();
            if (withLight) { light.color = color; light.intensity = intensity; light.range = range; lights++; }
            else light.enabled = false;
            lamps++;
        }

        public void Rock(Transform parent, Vector3 floor, ItemData ore, System.Random rnd)
        {
            var rock = (GameObject)PrefabUtility.InstantiatePrefab(rockPrefab, parent);
            rock.transform.SetPositionAndRotation(floor - Vector3.up * 0.15f, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
            rock.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.25f, (float)rnd.NextDouble());
            rock.name = "Rock (" + ore.name + ")";
            var so = new SerializedObject(rock.GetComponent<RockHealth>());
            so.FindProperty("oreItem").objectReferenceValue = ore;
            so.ApplyModifiedPropertiesWithoutUndo();
            rocks++;
        }
    }

    private static string Dress(Transform island, MountainShape mountain, CaveLayout cave, Terrain terrain, Transform generated)
    {
        Transform props = Child(island, "Props"), lighting = Child(island, "Lighting"), mining = Child(island, "MiningRocks"), zones = Child(island, "Zones");
        Clear(props); Clear(lighting); Clear(mining); Clear(zones);

        var d = new Dresser
        {
            cave = cave, generated = generated, terrain = terrain,
            wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Prototype/Pickaxe_Handle.mat"),
            metal = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Prototype/Pickaxe_Head.mat"),
            glow = SimpleMaterial("LanternGlow", new Color(1f, 0.85f, 0.55f), new Color(4f, 2.4f, 0.9f)),
            crystal = CrystalMaterial(),
            rockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Rock.prefab"),
            oreRockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OreRockPrefab),
        };
        d.lampPrefab = LampPrefab(d.wood, d.metal, d.glow);
        var spaces = new Dictionary<string, CaveSpace>();
        foreach (CaveSpace s in cave.Spaces) spaces[s.name] = s;
        CaveSpace Space(string n) => spaces.TryGetValue(n, out CaveSpace s) ? s : null;

        // --- Entrance: where the ravine meets the rock (first point along the main tunnel with rock overhead).
        CaveSpace mainTunnel = Space("MainTunnel");
        Vector3 mouth = mainTunnel != null ? mainTunnel.transform.position : new Vector3(500f, 20f, 650f);
        Vector3 inward = mainTunnel != null ? (mainTunnel.End.position - mainTunnel.transform.position).normalized : Vector3.forward;
        if (mainTunnel != null)
        {
            float roof = mainTunnel.Size.y + 1.5f;
            for (float s = 0f; s < 60f; s += 0.5f)
            {
                Vector3 p = mainTunnel.transform.position + inward * s + Vector3.up * roof;
                if (mountain.Distance(p) < 0f && cave.Distance(p) > 0f) { mouth = mainTunnel.transform.position + inward * s; break; }
            }
        }
        Vector3 side = Vector3.Cross(Vector3.up, inward).normalized;
        Transform entranceProps = Child(props, "Entrance"), entranceLights = Child(lighting, "Entrance");

        // Timber frames at the mouth and along the first stretch of tunnel.
        var frames = new PropMesh(d.wood, d.metal);
        var frameColliders = new List<(Vector3, Vector3)>();
        float halfSpan = mainTunnel != null ? mainTunnel.Size.x - 2.5f : 7.5f;
        foreach (float along in new[] { 1f, 15f, 30f })
        {
            Vector3 c = mouth + inward * along;
            if (!d.Floor(c, out Vector3 floor, 1f)) floor = c;
            Quaternion rot = Quaternion.LookRotation(inward);
            float h = 9f;
            foreach (float sgn in new[] { -1f, 1f })
            {
                Vector3 postBase = floor + side * (halfSpan * sgn);
                if (Physics.Raycast(postBase + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f)) postBase = hit.point;
                float postH = floor.y + h - postBase.y;
                frames.Box(postBase + Vector3.up * (postH * 0.5f - 0.3f) - mouth, rot, new Vector3(0.7f, postH + 0.6f, 0.7f), 0);
                frames.Box(postBase + Vector3.up * (h - 1.6f) + side * (-sgn * 1.1f) - mouth, rot * Quaternion.Euler(0f, 0f, sgn * 45f), new Vector3(0.35f, 2.8f, 0.35f), 0);
                frameColliders.Add((postBase + Vector3.up * (postH * 0.5f), new Vector3(0.7f, postH, 0.7f)));
            }
            frames.Box(floor + Vector3.up * h - mouth, rot, new Vector3(halfSpan * 2f + 1.6f, 0.8f, 0.8f), 0);
            frames.Box(floor + Vector3.up * (h - 0.45f) - mouth, rot, new Vector3(halfSpan * 2f + 1.7f, 0.12f, 0.9f), 1);
        }
        GameObject frameGo = frames.Build("MineFrames", entranceProps, mouth, "MineFrames");
        foreach (var (centre, size) in frameColliders)
        {
            var col = new GameObject("Post Collider", typeof(BoxCollider));
            col.transform.SetParent(frameGo.transform, false);
            col.transform.SetPositionAndRotation(centre, Quaternion.LookRotation(inward));
            col.GetComponent<BoxCollider>().size = size;
        }

        // Rails from the plaza into the mine, along the right side (a spur beside the walking route), with a cart.
        var rails = new PropMesh(d.wood, d.metal);
        Vector3 railStart = new Vector3(mouth.x, 0f, Plaza.y + 4f) + side * RailSideOffset;
        Vector3? previous = null;
        float railLength = Vector3.Distance(new Vector3(mouth.x, 0f, mouth.z), railStart) + 35f;
        for (float s = 0f; s <= railLength; s += 2f)
        {
            Vector3 p = railStart + inward * s;
            p.y = Physics.Raycast(new Vector3(p.x, mouth.y + 6f, p.z), Vector3.down, out RaycastHit hit, 20f) ? hit.point.y : Height(terrain, p.x, p.z);
            Quaternion rot = Quaternion.LookRotation(inward);
            rails.Box(p + Vector3.up * 0.07f - mouth, rot, new Vector3(2.2f, 0.14f, 0.4f), 0);
            if (previous.HasValue)
            {
                Vector3 a = previous.Value, mid = (a + p) * 0.5f;
                Quaternion segRot = Quaternion.LookRotation(p - a);
                foreach (float sgn in new[] { -0.65f, 0.65f })
                    rails.Box(mid + side * sgn + Vector3.up * 0.2f - mouth, segRot, new Vector3(0.09f, 0.12f, (p - a).magnitude + 0.05f), 1);
            }
            previous = p;
        }
        rails.Build("MineRails", entranceProps, mouth, "MineRails");
        Vector3 cartPos = railStart + inward * 12f;
        cartPos.y = Height(terrain, cartPos.x, cartPos.z);
        if (Physics.Raycast(cartPos + Vector3.up * 10f, Vector3.down, out RaycastHit cartHit, 20f)) cartPos = cartHit.point;
        BuildCart(d, entranceProps, cartPos, Quaternion.LookRotation(inward), "MineCart");

        // Crates and signs.
        var crates = new PropMesh(d.wood);
        Vector3 crateBase = new Vector3(Plaza.x + 9f, 0f, Plaza.y + 2f);
        crateBase.y = Height(terrain, crateBase.x, crateBase.z);
        crates.Box(new Vector3(0f, 0.5f, 0f), Quaternion.Euler(0f, 12f, 0f), Vector3.one, 0);
        crates.Box(new Vector3(1.15f, 0.45f, 0.3f), Quaternion.Euler(0f, -8f, 0f), Vector3.one * 0.9f, 0);
        crates.Box(new Vector3(0.5f, 1.4f, 0.1f), Quaternion.Euler(0f, 30f, 0f), Vector3.one * 0.8f, 0);
        GameObject crateGo = crates.Build("Crates", entranceProps, crateBase, "Crates");
        crateGo.AddComponent<BoxCollider>().center = new Vector3(0.55f, 0.6f, 0.15f);
        crateGo.GetComponent<BoxCollider>().size = new Vector3(2.3f, 1.2f, 1.4f);
        Sign(d, entranceProps, new Vector3(Plaza.x + 12f, 0f, Plaza.y - 2f), "ORE WHAT? MINING CO.\nMINE No. 1", terrain);
        Sign(d, Child(props, "Path"), new Vector3(PathPoints[0].x + 5f, 0f, PathPoints[0].y + 8f), "THE MINE\nstraight ahead", terrain);

        // Lamps: two at the mouth (lit), unlit lanterns along the path.
        foreach (float sgn in new[] { -1f, 1f })
        {
            Vector3 p = mouth - inward * 3f + side * (halfSpan + 2.5f) * sgn;
            if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 12f)) p = hit.point;
            d.Lamp(entranceLights, p, mouth - inward * 3f, new Color(1f, 0.72f, 0.4f), 5f, 18f, true);
        }
        // Floodlights in the ravine, aimed at the mouth: the timber frames and rock face stay readable even in the
        // ravine's shade. Local lights only (the sun and the cave's darkness are unchanged).
        foreach (float sgn in new[] { -1f, 1f })
        {
            Vector3 p = mouth - inward * 15f + side * 11f * sgn;
            if (Physics.Raycast(p + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f)) p = hit.point;
            Floodlight(d, entranceProps, entranceLights, p, mouth + inward * 2f + Vector3.up * 5f, sgn > 0 ? "R" : "L");
        }
        var signLight = new GameObject("Sign Light", typeof(Light)).GetComponent<Light>();
        signLight.transform.SetParent(entranceLights, false);
        Vector3 signAt = new Vector3(Plaza.x + 12f, 0f, Plaza.y - 2f);
        signLight.transform.position = new Vector3(signAt.x, Height(terrain, signAt.x, signAt.z) + 3.2f, signAt.z - 1.2f);
        signLight.type = LightType.Point; signLight.color = new Color(1f, 0.8f, 0.55f); signLight.intensity = 2.5f; signLight.range = 4.5f;
        signLight.shadows = LightShadows.None;
        d.lights++;
        Transform pathLights = Child(lighting, "Path");
        for (int i = 0; i < 4; i++)
        {
            float z = Mathf.Lerp(PathPoints[0].y + 25f, Plaza.y - 8f, i / 3f);
            float x = 500f + ((i & 1) == 0 ? 4f : -4f);
            float bestT = 0f; // nudge to the path's actual x at this z
            for (int k = 0; k < PathPoints.Length - 1; k++)
                if (z >= PathPoints[k].y && z <= PathPoints[k + 1].y) { bestT = Mathf.Lerp(PathPoints[k].x, PathPoints[k + 1].x, Mathf.InverseLerp(PathPoints[k].y, PathPoints[k + 1].y, z)); x = bestT + ((i & 1) == 0 ? 4f : -4f); }
            var p = new Vector3(x, Height(terrain, x, z), z);
            d.Lamp(pathLights, p, new Vector3(bestT, p.y, z), Color.white, 0f, 0f, false);
        }

        // --- Inside: lamps per space, minable rocks in the ore pockets, glowing crystals deep down.
        var lampPlan = new Dictionary<string, int>
        {
            { "MainTunnel", 2 }, { "Cavern_01", 4 }, { "OreArea_01", 1 }, { "SideTunnel", 1 }, { "SideCave", 2 }, { "Tunnel_02", 2 },
            { "Cavern_02", 5 }, { "MiningArea_02", 2 }, { "CombatTunnel", 1 }, { "CombatArea", 3 }, { "DeepTunnel_A", 1 },
            { "DeepTunnel_B", 1 }, { "DeepTunnel_C", 1 }, { "DeepCavern", 5 }, { "BossGate", 1 }, { "BossArena", 6 },
        };
        ItemData copper = Item("CopperOre"), iron = Item("IronOre"), gold = Item("GoldOre"), crystalOre = Item("Crystal");
        var rockPlan = new Dictionary<string, ItemData[]>
        {
            { "MainTunnel", new[] { copper, copper } },
            { "OreArea_01", new[] { copper, copper, copper, iron } },
            { "SideCave", new[] { copper, copper, iron, iron } },
            { "Cavern_02", new[] { iron } },
            { "MiningArea_02", new[] { iron, iron, iron, gold } },
            { "CombatArea", new[] { gold } },
            { "DeepCavern", new[] { gold, gold, crystalOre, crystalOre } },
            { "BossArena", new[] { crystalOre, crystalOre } },
        };
        var crystalPlan = new Dictionary<string, int> { { "MiningArea_02", 1 }, { "CombatArea", 1 }, { "DeepCavern", 2 }, { "BossArena", 3 } };
        Transform caveLights = Child(lighting, "Cave"), caveProps = Child(props, "Cave");
        var missed = new List<string>();
        int seed = 1;
        foreach (CaveSpace s in cave.Spaces)
        {
            if (s.SpaceKind == CaveSpace.Kind.OpenCut) continue;
            var rnd = new System.Random(1000 + seed++ * 7919);
            float depth = s.DepthAt(0.5f);
            Color lampColor = s.name == "BossArena" ? new Color(1f, 0.38f, 0.22f) : Color.Lerp(new Color(1f, 0.76f, 0.46f), new Color(1f, 0.55f, 0.28f), depth);
            float range = s.SpaceKind == CaveSpace.Kind.Room ? Mathf.Clamp(Mathf.Max(s.Size.x, s.Size.z) * 0.9f, 16f, 28f) : 18f;
            Vector3 centre = s.SpaceKind == CaveSpace.Kind.Room ? s.transform.position : (s.transform.position + s.End.position) * 0.5f;

            if (lampPlan.TryGetValue(s.name, out int lampCount))
            {
                Transform group = Child(caveLights, s.name);
                for (int i = 0; i < lampCount; i++)
                    if (d.Spot(s, rnd, 0.62f, 0.85f, out Vector3 floor)) d.Lamp(group, floor, centre, lampColor, 9f, range, true);
                    else missed.Add(s.name + " lamp");
            }
            if (rockPlan.TryGetValue(s.name, out ItemData[] ores))
            {
                Transform group = Child(mining, s.name);
                foreach (ItemData ore in ores)
                    if (ore != null && d.Spot(s, rnd, 0.5f, 0.82f, out Vector3 floor, 2f)) d.Rock(group, floor, ore, rnd);
                    else missed.Add(s.name + " rock");
            }
            if (crystalPlan.TryGetValue(s.name, out int crystals) && d.oreRockPrefab != null)
            {
                bool deep = depth >= 0.55f;
                for (int i = 0; i < crystals; i++)
                {
                    if (!d.Spot(s, rnd, 0.7f, 0.95f, out Vector3 floor, 2.5f)) { missed.Add(s.name + " crystal"); continue; }
                    var rock = (GameObject)PrefabUtility.InstantiatePrefab(d.oreRockPrefab, Child(caveProps, s.name));
                    rock.transform.SetPositionAndRotation(floor - Vector3.up * 0.3f, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
                    rock.transform.localScale = Vector3.one * Mathf.Lerp(1.6f, 2.4f, (float)rnd.NextDouble());
                    if (deep)
                    {
                        foreach (var r in rock.GetComponentsInChildren<Renderer>()) r.sharedMaterial = d.crystal;
                        var lightGo = new GameObject("Crystal Glow", typeof(Light));
                        lightGo.transform.SetParent(rock.transform, false);
                        lightGo.transform.localPosition = Vector3.up * 1.2f;
                        var l = lightGo.GetComponent<Light>();
                        l.type = LightType.Point; l.color = new Color(0.35f, 0.85f, 1f); l.intensity = 3f; l.range = 9f; l.shadows = LightShadows.None;
                        d.lights++;
                    }
                    foreach (var c in rock.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c); // decoration only: no snagging
                    GameObjectUtility.SetStaticEditorFlags(rock, StaticEditorFlags.BatchingStatic);
                }
            }

            // Zones for future systems (ore spawning, enemies, the boss).
            string zoneName = null;
            MapZone.ZoneKind kind = MapZone.ZoneKind.Ore;
            int tier = 1;
            if (rockPlan.ContainsKey(s.name) && s.name != "Cavern_02") { zoneName = "OreZone_" + s.name; tier = depth < 0.2f ? 1 : depth < 0.5f ? 2 : depth < 0.9f ? 3 : 4; }
            if (s.name == "CombatArea" || s.name == "DeepCavern" || s.name == "Cavern_02") { zoneName = "EnemyZone_" + s.name; kind = MapZone.ZoneKind.Enemy; tier = depth < 0.5f ? 1 : 2; }
            if (s.name == "BossArena") { zoneName = "BossZone_BossArena"; kind = MapZone.ZoneKind.Boss; tier = 4; }
            if (zoneName != null)
            {
                var z = new GameObject(zoneName).AddComponent<MapZone>();
                z.transform.SetParent(zones, false);
                z.transform.position = centre + Vector3.up * 2f;
                z.Set(kind, tier, s.SpaceKind == CaveSpace.Kind.Room ? Mathf.Max(s.Size.x, s.Size.z) : s.Size.x * 2f, $"Generated for {s.name} (depth {depth:F2}).");
            }
        }
        string arena = DressBossArena(d, Space("BossArena"), Space("BossGate"), Child(props, "BossArena"), Child(lighting, "BossArena"));
        if (arena != null) missed.Add(arena);

        var start = new GameObject("PlayerStart").AddComponent<MapZone>();
        start.transform.SetParent(zones, false);
        GameObject player = GameObject.FindWithTag("Player");
        start.transform.position = player != null ? player.transform.position : new Vector3(500f, 18f, 500f);
        start.Set(MapZone.ZoneKind.PlayerStart, 1, 4f, "Where the player starts (the Player object itself decides the spawn).");
        var entranceZone = new GameObject("Landmark_CaveEntrance").AddComponent<MapZone>();
        entranceZone.transform.SetParent(zones, false);
        entranceZone.transform.position = mouth + Vector3.up * 6f;
        entranceZone.Set(MapZone.ZoneKind.Landmark, 1, 10f, "The cave mouth.");

        var ambience = cave.GetComponent<CaveAmbience>();
        if (ambience == null) ambience = cave.gameObject.AddComponent<CaveAmbience>();
        ambience.Configure(entranceZone.transform, mainTunnel,
            AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Ambience/forest_ambiance_loop.ogg"),
            AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Ambience/cave_ambiance_loop.ogg"));

        return $"Dressing: mouth at {mouth:F1}, {d.lamps} lamps ({d.lights} lights), {d.rocks} minable rocks." +
               (missed.Count > 0 ? " Couldn't place: " + string.Join(", ", missed) : "");
    }

    private static ItemData Item(string name) => AssetDatabase.LoadAssetAtPath<ItemData>($"Assets/Items/{name}.asset");

    private const float RailSideOffset = 6f; // rails / cart this far right of the route's centre line

    /// <summary>A site floodlight: a wooden pole with a lamp head and a Spot Light aimed at a point.</summary>
    private static void Floodlight(Dresser d, Transform propParent, Transform lightParent, Vector3 foot, Vector3 aimAt, string tag)
    {
        Vector3 head = foot + Vector3.up * 5f;
        Vector3 flat = aimAt - foot; flat.y = 0f;
        Quaternion facing = Quaternion.LookRotation(flat.normalized);
        var pole = new PropMesh(d.wood, d.metal, d.glow);
        pole.Box(Vector3.up * 2.5f, facing, new Vector3(0.22f, 5f, 0.22f), 0);
        pole.Box(Vector3.up * 0.15f, facing, new Vector3(0.9f, 0.3f, 0.9f), 0);
        pole.Box(Vector3.up * 5.1f + facing * Vector3.forward * 0.25f, facing, new Vector3(0.7f, 0.5f, 0.3f), 1);
        pole.Box(Vector3.up * 5.1f + facing * Vector3.forward * 0.42f, facing, new Vector3(0.55f, 0.36f, 0.04f), 2);
        GameObject go = pole.Build("Floodlight " + tag, propParent, foot, "Floodlight");
        var col = go.AddComponent<BoxCollider>(); col.center = Vector3.up * 2.5f; col.size = new Vector3(0.3f, 5f, 0.3f);

        var l = new GameObject("Floodlight " + tag, typeof(Light)).GetComponent<Light>();
        l.transform.SetParent(lightParent, false);
        l.transform.SetPositionAndRotation(head + facing * Vector3.forward * 0.5f, Quaternion.LookRotation(aimAt - head));
        l.type = LightType.Spot; l.color = new Color(1f, 0.85f, 0.65f);
        l.intensity = 60f; l.range = 40f; l.spotAngle = 60f; l.innerSpotAngle = 35f;
        l.shadows = LightShadows.None;
        d.lights++;
    }

    /// <summary>
    /// The boss arena's look (no gameplay): a stone platform with a metal-edged inlay, a ring of rock formations
    /// around the edge with a gap at the gate, a timber frame at the gate, and a mining headframe over a glowing
    /// crystal in the middle, lit from above. Returns a note if something couldn't be placed.
    /// </summary>
    private static string DressBossArena(Dresser d, CaveSpace arena, CaveSpace gate, Transform props, Transform lights)
    {
        if (arena == null) return null;
        Vector3 centre = arena.transform.position;
        if (!Physics.Raycast(centre + Vector3.up * 3f, Vector3.down, out RaycastHit floorHit, 10f)) return "BossArena floor";
        centre = floorHit.point;
        Material stone = SimpleMaterial("ArenaStone", new Color(0.24f, 0.23f, 0.27f), Color.black);

        // Floor: a 14 m stone disc 0.15 m above the rock floor (a normal step up), an inner metal-edged ring.
        var floor = new PropMesh(stone, d.metal, d.wood);
        floor.Cyl(Vector3.up * -0.05f, Quaternion.identity, new Vector3(14f, 0.2f, 14f), 0); // the built-in Cylinder.fbx mesh has radius 1
        floor.Cyl(Vector3.up * -0.02f, Quaternion.identity, new Vector3(6.5f, 0.2f, 6.5f), 2);
        for (int i = 0; i < 28; i++)
        {
            float a = i / 28f * Mathf.PI * 2f;
            foreach (float r in new[] { 13.9f, 6.5f })
            {
                if (r < 10f && i % 2 == 1) continue;
                Vector3 at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r + Vector3.up * 0.16f;
                floor.Box(at, Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), new Vector3(0.35f, 0.06f, r < 10f ? 1.5f : 3.2f), 1);
            }
        }
        GameObject platform = floor.Build("ArenaFloor", props, centre, "ArenaFloor");
        platform.AddComponent<MeshCollider>().sharedMesh = platform.GetComponent<MeshFilter>().sharedMesh;

        // Centre: a mining headframe (4 posts, braces, a pulley wheel and cable) over a glowing crystal.
        var frame = new PropMesh(d.wood, d.metal);
        float h = 9f, half = 2.3f;
        var postColliders = new List<Vector3>();
        foreach (float x in new[] { -half, half })
            foreach (float z in new[] { -half, half })
            {
                frame.Box(new Vector3(x, h * 0.5f, z), Quaternion.identity, new Vector3(0.45f, h, 0.45f), 0);
                postColliders.Add(new Vector3(x, h * 0.5f, z));
            }
        foreach (float y in new[] { 3.5f, h })
        {
            frame.Box(new Vector3(0f, y, -half), Quaternion.identity, new Vector3(half * 2f + 0.5f, 0.35f, 0.35f), 0);
            frame.Box(new Vector3(0f, y, half), Quaternion.identity, new Vector3(half * 2f + 0.5f, 0.35f, 0.35f), 0);
            frame.Box(new Vector3(-half, y, 0f), Quaternion.identity, new Vector3(0.35f, 0.35f, half * 2f + 0.5f), 0);
            frame.Box(new Vector3(half, y, 0f), Quaternion.identity, new Vector3(0.35f, 0.35f, half * 2f + 0.5f), 0);
        }
        foreach (float sgn in new[] { -1f, 1f })
        {
            frame.Box(new Vector3(sgn * half, (3.5f + h) * 0.5f, 0f), Quaternion.Euler(sgn * 35f, 0f, 0f), new Vector3(0.25f, 6.8f, 0.25f), 0);
            frame.Box(new Vector3(0f, (3.5f + h) * 0.5f, sgn * half), Quaternion.Euler(0f, 0f, sgn * 35f), new Vector3(0.25f, 6.8f, 0.25f), 0);
        }
        frame.Cyl(new Vector3(0f, h + 1.2f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(1.3f, 0.15f, 1.3f), 1);
        frame.Box(new Vector3(0f, h + 0.5f, 0f), Quaternion.identity, new Vector3(0.3f, 1.4f, 0.3f), 1);
        frame.Box(new Vector3(1.25f, (h + 1.2f + 4.2f) * 0.5f, 0f), Quaternion.identity, new Vector3(0.07f, h + 1.2f - 4.2f, 0.07f), 1);
        GameObject headframe = frame.Build("Headframe", props, centre + Vector3.up * 0.15f, "ArenaHeadframe");
        foreach (Vector3 c in postColliders)
        {
            var col = new GameObject("Post Collider", typeof(BoxCollider));
            col.transform.SetParent(headframe.transform, false);
            col.transform.localPosition = c;
            col.GetComponent<BoxCollider>().size = new Vector3(0.5f, h, 0.5f);
        }
        GameObject crystal = Place(OreRockPrefab, props, centre + Vector3.up * 0.15f, 25f, 3.2f);
        if (crystal != null)
        {
            crystal.name = "Arena Crystal";
            foreach (var r in crystal.GetComponentsInChildren<Renderer>()) r.sharedMaterial = d.crystal;
            if (TryBounds(crystal, out Bounds cb)) // solid, so nobody walks through it between the posts
            {
                var box = crystal.AddComponent<BoxCollider>();
                box.center = crystal.transform.InverseTransformPoint(cb.center);
                box.size = Vector3.Scale(cb.size, new Vector3(1f / crystal.transform.lossyScale.x, 1f / crystal.transform.lossyScale.y, 1f / crystal.transform.lossyScale.z)) * 0.8f;
            }
            GameObjectUtility.SetStaticEditorFlags(crystal, StaticEditorFlags.BatchingStatic);
        }

        // Lights: warm work light under the headframe top, cyan glow from the crystal.
        AddPointLight(lights, "Headframe Light", centre + Vector3.up * (h - 0.6f), new Color(1f, 0.62f, 0.36f), 16f, 34f);
        AddPointLight(lights, "Crystal Glow", centre + Vector3.up * 2.2f, new Color(0.35f, 0.85f, 1f), 7f, 14f);
        d.lights += 2;

        // Rock formations around the edge, leaving the gate side open.
        Vector3 gateDir = gate != null ? (gate.End.position - centre) : Vector3.forward;
        gateDir.y = 0f; gateDir.Normalize();
        int pillars = 0;
        for (int i = 0; i < 12; i++)
        {
            float a = (i + 0.5f) / 12f * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            if (Vector3.Angle(dir, gateDir) < 32f) continue; // the way in stays open
            Vector3 at = centre + Vector3.Scale(dir, new Vector3(arena.Size.x, 0f, arena.Size.z)) * 0.8f;
            if (!Physics.Raycast(at + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 14f) || !hit.collider.transform.IsChildOf(d.generated)) continue;
            // A tall, narrow rock column (9-13 m), sunk a little into the floor. Kept solid: it's cover.
            GameObject rock = Place(RockFolder + "PT_Generic_Rock_01.prefab", props, hit.point - Vector3.up * 0.8f, i * 53f, 9f + (i * 37 % 5));
            if (rock == null) continue;
            rock.name = "Arena Pillar";
            rock.transform.localScale = Vector3.Scale(rock.transform.localScale, new Vector3(0.5f, 1f, 0.5f));
            if (TryBounds(rock, out Bounds rb))
            {
                var box = rock.AddComponent<BoxCollider>();
                box.center = rock.transform.InverseTransformPoint(rb.center);
                box.size = Vector3.Scale(rb.size, new Vector3(1f / rock.transform.lossyScale.x, 1f / rock.transform.lossyScale.y, 1f / rock.transform.lossyScale.z)) * 0.85f;
            }
            GameObjectUtility.SetStaticEditorFlags(rock, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            pillars++;
        }

        // Gate: a timber frame where the gate tunnel opens into the arena.
        if (gate != null)
        {
            Vector3 g = gate.End.position, inward = (gate.End.position - gate.transform.position); inward.y = 0f; inward.Normalize();
            Vector3 gSide = Vector3.Cross(Vector3.up, inward).normalized;
            if (Physics.Raycast(g + Vector3.up * 3f, Vector3.down, out RaycastHit gh, 10f)) g = gh.point;
            float span = gate.Size.x - 2f, fh = 8f;
            var gateFrame = new PropMesh(d.wood, d.metal);
            Quaternion rot = Quaternion.LookRotation(inward);
            var colliders = new List<Vector3>();
            foreach (float sgn in new[] { -1f, 1f })
            {
                Vector3 b = gSide * span * sgn;
                gateFrame.Box(b + Vector3.up * (fh * 0.5f - 0.3f), rot, new Vector3(0.7f, fh + 0.6f, 0.7f), 0);
                gateFrame.Box(b + Vector3.up * (fh - 1.6f) - gSide * sgn * 1.1f, rot * Quaternion.Euler(0f, 0f, sgn * 45f), new Vector3(0.35f, 2.8f, 0.35f), 0);
                colliders.Add(b + Vector3.up * (fh * 0.5f));
            }
            gateFrame.Box(Vector3.up * fh, rot, new Vector3(span * 2f + 1.6f, 0.8f, 0.8f), 0);
            gateFrame.Box(Vector3.up * (fh - 0.45f), rot, new Vector3(span * 2f + 1.7f, 0.12f, 0.9f), 1);
            GameObject gf = gateFrame.Build("GateFrame", props, g, "ArenaGateFrame");
            foreach (Vector3 c in colliders)
            {
                var col = new GameObject("Post Collider", typeof(BoxCollider));
                col.transform.SetParent(gf.transform, false);
                col.transform.SetPositionAndRotation(g + c, rot);
                col.GetComponent<BoxCollider>().size = new Vector3(0.7f, fh, 0.7f);
            }
        }
        return pillars < 6 ? $"BossArena: only {pillars} pillars placed" : null;
    }

    /// <summary>Instantiates a prefab scaled to a height, standing on <paramref name="pos"/> (its colliders removed).</summary>
    private static GameObject Place(string prefabPath, Transform parent, Vector3 pos, float yaw, float height)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        if (TryBounds(go, out Bounds b) && b.size.y > 0.001f)
        {
            go.transform.localScale *= height / b.size.y;
            TryBounds(go, out b);
            go.transform.position += Vector3.up * (pos.y - b.min.y);
        }
        return go;
    }

    private static bool TryBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    private static void AddPointLight(Transform parent, string name, Vector3 at, Color color, float intensity, float range)
    {
        var l = new GameObject(name, typeof(Light)).GetComponent<Light>();
        l.transform.SetParent(parent, false);
        l.transform.position = at;
        l.type = LightType.Point; l.color = color; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
    }

    private static GameObject LampPrefab(Material wood, Material metal, Material glow)
    {
        string path = $"{PrefabFolder}/MineLamp.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null) return existing;
        var root = new GameObject("MineLamp");
        var mesh = new PropMesh(wood, metal, glow);
        mesh.Box(new Vector3(0f, 1.25f, 0f), Quaternion.identity, new Vector3(0.18f, 2.5f, 0.18f), 0);
        mesh.Box(new Vector3(0.3f, 2.42f, 0f), Quaternion.identity, new Vector3(0.66f, 0.1f, 0.1f), 0);
        mesh.Box(new Vector3(0.55f, 2.28f, 0f), Quaternion.identity, new Vector3(0.3f, 0.06f, 0.3f), 1);
        mesh.Box(new Vector3(0.55f, 2.08f, 0f), Quaternion.identity, new Vector3(0.22f, 0.32f, 0.22f), 2);
        mesh.Box(new Vector3(0.55f, 1.9f, 0f), Quaternion.identity, new Vector3(0.28f, 0.05f, 0.28f), 1);
        GameObject body = mesh.Build("Lamp", root.transform, Vector3.zero, "MineLamp");
        GameObjectUtility.SetStaticEditorFlags(body, 0);
        var light = new GameObject("Light", typeof(Light)).GetComponent<Light>();
        light.transform.SetParent(root.transform, false);
        light.transform.localPosition = new Vector3(0.55f, 1.95f, 0f);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.74f, 0.44f);
        light.intensity = 4.5f;
        light.range = 15f;
        light.shadows = LightShadows.None;
        Directory.CreateDirectory(PrefabFolder);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void BuildCart(Dresser d, Transform parent, Vector3 floor, Quaternion rot, string assetName)
    {
        var cart = new PropMesh(d.wood, d.metal);
        cart.Box(new Vector3(0f, 0.85f, 0f), Quaternion.identity, new Vector3(1.3f, 0.75f, 1.9f), 0);
        cart.Box(new Vector3(0f, 1.24f, 0f), Quaternion.identity, new Vector3(1.4f, 0.08f, 2f), 1);
        cart.Box(new Vector3(0f, 0.45f, 0f), Quaternion.identity, new Vector3(1.1f, 0.12f, 1.7f), 1);
        foreach (float x in new[] { -0.62f, 0.62f })
            foreach (float z in new[] { -0.6f, 0.6f })
                cart.Cyl(new Vector3(x, 0.3f, z), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.27f, 0.06f, 0.27f), 1); // radius (Cylinder.fbx is radius 1)
        GameObject go = cart.Build("MineCart", parent, floor, assetName);
        go.transform.rotation = rot;
        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.75f, 0f);
        box.size = new Vector3(1.4f, 1.1f, 2f);
    }

    private static void Sign(Dresser d, Transform parent, Vector3 at, string text, Terrain terrain)
    {
        at.y = Height(terrain, at.x, at.z);
        var sign = new PropMesh(d.wood);
        sign.Box(new Vector3(0f, 1.1f, 0.1f), Quaternion.identity, new Vector3(0.15f, 2.2f, 0.15f), 0);
        sign.Box(new Vector3(0f, 1.95f, 0f), Quaternion.identity, new Vector3(2.4f, 0.9f, 0.08f), 0);
        GameObject go = sign.Build("Sign", parent, at, "Sign_" + Mathf.RoundToInt(at.z));
        var label = new GameObject("Text", typeof(TextMesh));
        label.transform.SetParent(go.transform, false);
        label.transform.localPosition = new Vector3(0f, 1.95f, -0.05f);
        var tm = label.GetComponent<TextMesh>();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = 0.035f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontStyle = FontStyle.Bold;
        tm.color = new Color(1f, 0.92f, 0.75f);
        label.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
    }
}
