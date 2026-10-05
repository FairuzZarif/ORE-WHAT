using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Angular cave surfaces and a small reusable geology kit; no decorative runtime behaviours.</summary>
public static partial class IslandMapBuilder
{
    private const string GeologyMeshes = "Assets/Models/Island/Geology";
    private const string GeologyPrefabs = "Assets/Prefabs/CaveGeology";
    private static readonly string[] GeologyKinds = { "WallSlab", "JaggedWall", "CeilingCluster", "SmallRubble", "MediumRubble", "CollapseCluster" };

    private static void InitializeCaveGeology(CaveLayout cave)
    {
        foreach (CaveSpace space in cave.GetComponentsInChildren<CaveSpace>())
        {
            int mine = MineOf(space.name);
            float strength = mine == RiftMine ? 1.15f : mine == Hub ? 0.75f : 1f;
            float size = mine == RiftMine || space.name == "BossArena" ? 9f : space.SpaceKind == CaveSpace.Kind.Room ? 7f : 5.5f;
            space.InitializeRockStyle(strength, size, mine == OldMine || mine == DeepMine);
        }
    }

    private static void GeologyFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        GeologyFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    private static Material[] GeologyPalette(int mine, bool deep = false)
    {
        string sourceName = mine == DeepMine ? "Cave_Rock_DeepMine" : mine == CrystalMine ? "Cave_Rock_Crystal"
            : mine == RiftMine ? "Cave_Rock_Rift" : deep ? "Cave_Rock_Deep" : "Cave_Rock";
        Material source = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + sourceName + ".mat");
        var palette = new Material[3]; palette[0] = source;
        for (int index = 1; index < 3; index++)
        {
            string path = MaterialFolder + "/" + sourceName + (index == 1 ? "_LayerLight" : "_LayerDark") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                material.SetColor("_GroundColor", source.GetColor("_GroundColor") * (index == 1 ? 1.10f : 0.84f));
                material.SetFloat("_Smoothness", 0f);
                AssetDatabase.CreateAsset(material, path);
            }
            palette[index] = material;
        }
        return palette;
    }

    private static GameObject[] GeologyKit()
    {
        GeologyFolder(GeologyMeshes); GeologyFolder(GeologyPrefabs);
        var result = new GameObject[GeologyKinds.Length];
        Material[] palette = GeologyPalette(OldMine);
        for (int variant = 0; variant < result.Length; variant++)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var indices = new[] { new List<int>(), new List<int>(), new List<int>() };
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] shape = { new Vector3(-1,t,0),new Vector3(1,t,0),new Vector3(-1,-t,0),new Vector3(1,-t,0),
                new Vector3(0,-1,t),new Vector3(0,1,t),new Vector3(0,-1,-t),new Vector3(0,1,-t),
                new Vector3(t,0,-1),new Vector3(t,0,1),new Vector3(-t,0,-1),new Vector3(-t,0,1) };
            int[] faces = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var random = new System.Random(7231 + variant * 591);
            void Rock(Vector3 center, Vector3 half, float yaw, int seed)
            {
                var points = new Vector3[shape.Length];
                Quaternion rotation = Quaternion.Euler(seed % 13 - 6, yaw, seed % 19 - 9);
                for (int p = 0; p < points.Length; p++)
                    points[p] = center + rotation * Vector3.Scale(shape[p].normalized * (0.86f + (float)random.NextDouble() * 0.24f), half);
                for (int face = 0; face < faces.Length; face += 3)
                {
                    Vector3 a = points[faces[face]], b = points[faces[face + 1]], c = points[faces[face + 2]];
                    Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                    int first = vertices.Count;
                    vertices.Add(a); vertices.Add(b); vertices.Add(c);
                    normals.Add(normal); normals.Add(normal); normals.Add(normal);
                    int shade = face % 15 == 0 ? 1 : face % 21 == 0 ? 2 : 0;
                    indices[shade].Add(first); indices[shade].Add(first + 1); indices[shade].Add(first + 2);
                }
            }
            if (variant == 0)
            {
                Rock(Vector3.zero, new Vector3(3.6f, .72f, 2.1f), 14, 1);
                Rock(new Vector3(-1.9f, -.15f, .9f), new Vector3(2.5f, .65f, 1.3f), -21, 2);
                Rock(new Vector3(1.8f, -.3f, -.7f), new Vector3(2.3f, .6f, 1.4f), 28, 3);
            }
            else if (variant == 1 || variant == 2)
                for (int rock = 0; rock < 4; rock++) Rock(new Vector3((rock % 2 - .5f) * 2.3f, -.18f * rock, (rock / 2 - .5f) * 1.8f),
                    new Vector3(2.1f, variant == 2 ? 1.25f : 1f, 1.8f), rock * 41, rock + 7);
            else
            {
                int rocks = variant == 3 ? 4 : 5;
                for (int rock = 0; rock < rocks; rock++)
                {
                    float scale = variant == 3 ? .32f + rock * .045f : variant == 4 ? (rock == 0 ? .9f : .43f) : (rock < 3 ? 1.55f : .6f);
                    float angle = rock * 2.4f, spread = variant == 5 ? 1.6f : .65f;
                    Rock(new Vector3(Mathf.Cos(angle) * spread, scale * .46f, Mathf.Sin(angle) * spread), new Vector3(scale, scale * .62f, scale * .86f), rock * 53, rock + 11);
                }
            }
            var mesh = new Mesh { name = GeologyKinds[variant] };
            mesh.SetVertices(vertices); mesh.SetNormals(normals);
            mesh.colors32 = Enumerable.Repeat(new Color32(255,255,255,255), vertices.Count).ToArray();
            mesh.subMeshCount = 3;
            for (int sub = 0; sub < 3; sub++) mesh.SetTriangles(indices[sub], sub);
            mesh.RecalculateBounds(); mesh = SaveMesh(mesh, GeologyMeshes + "/" + mesh.name + ".asset");
            var root = new GameObject(GeologyKinds[variant], typeof(MeshFilter), typeof(MeshRenderer));
            root.GetComponent<MeshFilter>().sharedMesh = mesh; root.GetComponent<MeshRenderer>().sharedMaterials = palette;
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            result[variant] = PrefabUtility.SaveAsPrefabAsset(root, GeologyPrefabs + "/" + root.name + ".prefab");
            Object.DestroyImmediate(root);
        }
        // Prepare all theme palettes once; instances share them.
        GeologyPalette(OldMine, true); GeologyPalette(DeepMine); GeologyPalette(CrystalMine); GeologyPalette(RiftMine);
        return result;
    }

    private static Bounds GeologyBounds(Mesh mesh, Vector3 position, Quaternion rotation, float scale)
    {
        Bounds local = mesh.bounds;
        var result = new Bounds(position + rotation * (local.center * scale), Vector3.zero);
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            result.Encapsulate(position + rotation * ((local.center + Vector3.Scale(local.extents, new Vector3(x,y,z))) * scale));
        return result;
    }

    private static bool GeologyClear(Bounds bounds, OreSpawnSystem ores, List<Corridor> routes, Bounds[] props, CaveSpace space)
    {
        foreach (OreSpawnSystem.Socket socket in ores.sockets)
            if (bounds.SqrDistance(socket.position + socket.normal * .55f) < 1.2f * 1.2f || bounds.SqrDistance(socket.approach) < 1f) return false;
        foreach (Corridor route in routes)
        {
            Vector3 ab = route.b - route.a;
            float t = Mathf.Clamp01(Vector3.Dot(bounds.center - route.a, ab) / Mathf.Max(.01f, ab.sqrMagnitude));
            Vector3 walking = route.a + ab * t + Vector3.up * 1.1f;
            if (bounds.SqrDistance(walking) < Mathf.Min(1.8f, route.half) * Mathf.Min(1.8f, route.half)) return false;
        }
        if (space.name == "BossArena" && bounds.min.y < space.transform.position.y + 3f)
        {
            Vector3 closest = bounds.ClosestPoint(space.transform.position);
            if (Vector2.Distance(new Vector2(closest.x, closest.z), new Vector2(space.transform.position.x, space.transform.position.z)) < OldMineCombatRadius + 2f) return false;
        }
        // Bounding-box checks are conservative: large forms should not engulf stations, rail beds or support posts.
        foreach (Bounds prop in props) if (bounds.Intersects(prop)) return false;
        return true;
    }

    private static bool GeologyAttached(Dresser dresser, Mesh mesh, Vector3 position, Quaternion rotation, float scale)
    {
        // Every 20-face rock in the combined cluster must intersect the shell. A single
        // centre ray cannot anchor all lobes to an irregular wall or ceiling.
        Vector3[] vertices = mesh.vertices;
        bool visible = false;
        for (int first = 0; first < vertices.Length; first += 60)
        {
            Vector3 centre = Vector3.zero; float outside = float.MinValue;
            for (int vertex = first; vertex < first + 60; vertex++)
            { centre += vertices[vertex] / 60f; outside = Mathf.Max(outside, vertices[vertex].y); }
            Vector3 air = centre; air.y = outside + 1f;
            air = position + rotation * (air * scale);
            if (dresser.cave.Distance(air, false) > -.1f) return false;
            bool embedded = false;
            for (int vertex = first; vertex < first + 60; vertex++)
            {
                Vector3 point = position + rotation * (vertices[vertex] * scale);
                Vector3 ray = point - air;
                if (RockRay(dresser, air, ray.normalized, ray.magnitude, out RaycastHit hit) && hit.distance < ray.magnitude - .08f) embedded = true;
                if (dresser.cave.Distance(point, false) < -.15f) visible = true;
            }
            if (!embedded) return false;
        }
        return visible;
    }

    private static string DressCaveGeology(CaveLayout cave)
    {
        Transform island = cave.transform.parent, props = island.Find("Props");
        Transform root = Child(props, "Cave Geology"); Clear(root);
        var dresser = new Dresser { cave = cave, generated = island.Find("Mountain/Generated") };
        var ores = island.Find("OreSpawns").GetComponent<OreSpawnSystem>();
        var kit = GeologyKit(); var routes = Corridors(cave.Spaces);
        Bounds[] existing = props.GetComponentsInChildren<Renderer>().Select(r => r.bounds).ToArray();
        var occupied = new List<Bounds>();
        int wallCount = 0, ceilingCount = 0, rubbleCount = 0, triangles = 0, colliders = 0;
        foreach (CaveSpace space in cave.Spaces.OrderBy(s => s.name, System.StringComparer.Ordinal))
        {
            int mine = MineOf(space.name); if (mine <= 0 || space.SpaceKind == CaveSpace.Kind.OpenCut) continue;
            int nameSeed = 0; foreach (char ch in space.name) nameSeed = unchecked(nameSeed * 31 + ch);
            var random = new System.Random(nameSeed ^ 471337);
            bool room = space.SpaceKind == CaveSpace.Kind.Room, arena = space.name == "BossArena";
            int budget = arena ? 18 : room ? 8 : Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(space.transform.position, space.End.position) / 16f), 2, 8);
            if (mine != OldMine) budget = Mathf.Max(2, budget / 2);
            var group = Child(root, space.name);
            int placed = 0;
            for (int attempt = 0; attempt < budget * 28 && placed < budget; attempt++)
            {
                int category = attempt % 5 == 0 ? 2 : attempt % 3 == 0 ? 0 : 1; // ceiling, rubble, wall
                Vector3 at, direction;
                float t = Mathf.Lerp(.19f, .81f, (float)random.NextDouble());
                if (room)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    direction = space.transform.rotation * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    at = space.transform.position + space.transform.rotation * new Vector3(Mathf.Cos(angle) * space.Size.x * .62f, 0, Mathf.Sin(angle) * space.Size.z * .62f);
                }
                else
                {
                    direction = Vector3.Cross(Vector3.up, space.End.position - space.transform.position).normalized * (random.NextDouble() < .5 ? -1 : 1);
                    at = Vector3.Lerp(space.transform.position, space.End.position, t);
                }
                if (!RockRay(dresser, at + Vector3.up * 4f, Vector3.down, 13f, out var floor) || floor.normal.y < .75f) continue;
                int variant; RaycastHit surface;
                float scale = Mathf.Lerp(.75f, 1.2f, (float)random.NextDouble());
                if (category == 2)
                {
                    if (!RockRay(dresser, floor.point + Vector3.up * 1.7f, Vector3.up, space.Size.y + 8f, out surface) || surface.point.y - floor.point.y < 4.3f) continue;
                    variant = 2; scale *= arena ? 2.8f : mine == RiftMine ? 1.7f : 1f;
                }
                else
                {
                    float height = category == 1 ? Mathf.Min(space.Size.y * .5f, arena ? 9f : mine == RiftMine ? 6f : 4f) : 1f;
                    if (!RockRay(dresser, floor.point + Vector3.up * height, direction, Mathf.Max(space.Size.x, space.Size.z) + 12f, out surface)) continue;
                    if (category == 1)
                    { variant = random.Next(2); scale *= arena ? 2.3f : mine == RiftMine ? 1.5f : 1f; }
                    else
                    {
                        Vector3 edge = surface.point + surface.normal * 1.2f;
                        if (!RockRay(dresser, edge + Vector3.up * 3f, Vector3.down, 6f, out surface) || surface.normal.y < .8f) continue;
                        variant = (space.name.Contains("Collapsed") || arena) && random.NextDouble() < .45 ? 5 : random.NextDouble() < .55 ? 3 : 4;
                    }
                }
                Vector3 forward = category == 1 ? Vector3.ProjectOnPlane(Vector3.up, surface.normal) : Vector3.ProjectOnPlane(space.transform.forward, surface.normal);
                if (forward.sqrMagnitude < .01f) forward = Vector3.ProjectOnPlane(Vector3.right, surface.normal);
                Quaternion rotation = Quaternion.LookRotation(forward.normalized, surface.normal) * Quaternion.Euler(0, (float)random.NextDouble() * (category == 1 ? 25 : 180), 0);
                float inset = category == 0 ? .08f : category == 2 ? 1.1f * scale : .65f * scale;
                Vector3 position = surface.point - surface.normal * inset;
                Mesh mesh = kit[variant].GetComponent<MeshFilter>().sharedMesh;
                Bounds bounds = GeologyBounds(mesh, position, rotation, scale);
                if (!GeologyAttached(dresser, mesh, position, rotation, scale)) continue;
                if (!GeologyClear(bounds, ores, routes, existing, space) || occupied.Any(b => b.Intersects(bounds))) continue;
                if (category == 2 && bounds.min.y < floor.point.y + 3f) continue;
                var form = (GameObject)PrefabUtility.InstantiatePrefab(kit[variant], group);
                form.name = GeologyKinds[variant] + " " + placed;
                form.transform.SetPositionAndRotation(position, rotation); form.transform.localScale = Vector3.one * scale;
                form.GetComponent<MeshRenderer>().sharedMaterials = GeologyPalette(mine, mine == OldMine && space.DepthAt(.5f) >= .5f);
                if (category == 1 || variant == 5) { form.AddComponent<MeshCollider>().sharedMesh = mesh; colliders++; }
                occupied.Add(bounds); triangles += mesh.triangles.Length / 3; placed++;
                if (category == 1) wallCount++; else if (category == 2) ceilingCount++; else rubbleCount++;
            }
        }
        Physics.SyncTransforms();
        return $"Angular geology: {wallCount} wall, {ceilingCount} ceiling, {rubbleCount} rubble clusters; {triangles} instanced triangles, {colliders} exact visible-mesh colliders, six shared meshes, ten layer materials. All ore sockets and approaches reserved.";
    }
}
