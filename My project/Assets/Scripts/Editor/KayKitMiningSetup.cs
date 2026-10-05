using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds reusable KayKit node art and upgrades the shared gameplay prefab, preserving its drops.</summary>
public static class KayKitMiningSetup
{
    public const string CatalogPath = "Assets/Mining/OreNodeCatalog.asset";
    const string Folder = "Assets/Mining";
    const string Pack = "Assets/KayKit_ResourceBits_1.0_FREE/Assets/fbx(unity)/";
    const string RockPath = "Assets/Prefabs/Rock.prefab";

    [MenuItem("Ore What/Mining/Set Up KayKit Resource Nodes")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Exit Play Mode before rebuilding node assets.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Mining");
        var stone = Material("NodeStone", new Color(.28f, .31f, .34f));
        var iron = Material("NodeIron", new Color(.50f, .58f, .64f));
        iron.mainTexture = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + "Iron_Nugget_Large.fbx").GetComponentInChildren<Renderer>().sharedMaterial.mainTexture;
        iron.SetFloat("_Metallic", .25f);
        var cracks = Material("NodeCracks", new Color(.022f, .018f, .028f), true);
        cracks.SetFloat("_Cull", 0);
        var catalog = AssetDatabase.LoadAssetAtPath<OreNodeCatalog>(CatalogPath);
        if (catalog == null) { catalog = ScriptableObject.CreateInstance<OreNodeCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
        string[] names = { "Copper", "Iron", "Gold", "Crystal" };
        catalog.entries = new OreNodeCatalog.Entry[4];
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            var root = new GameObject(name + "NodeVisual");
            var view = root.AddComponent<OreNodeVisualView>();
            if (name == "Crystal")
            {
                // Keep Crystal's original model/material. Only the metal resources use KayKit replacements.
                OriginalCrystalVisual(root.transform);
            }
            else
            {
                Part(root.transform, "HostRock", "Iron_Nugget_Large", new Vector3(0, .34f, 0), new Vector3(3.1f, 1.8f, 2.8f), Quaternion.Euler(0, 20, 0), stone);
                for (int p = 0; p < 3; p++)
                {
                    float angle = p * 120f + (i * 25f);
                    Vector3 position = Quaternion.Euler(0, angle, 0) * new Vector3(.29f, 0, .05f);
                    position.y = .52f + p * .045f;
                    Part(root.transform, "Mineral" + p, name + "_Nugget_Large", position,
                        Vector3.one * (1.3f - p * .12f), Quaternion.Euler(8f, angle, 15f), name == "Iron" ? iron : null);
                }
            }
            var surfaces = Triangles(root);
            for (int stage = 1; stage <= 3; stage++)
            {
                var mesh = CrackMesh(surfaces, stage);
                mesh.name = name + "Cracks" + stage;
                string path = Folder + "/" + name + "Cracks" + stage + ".asset";
                var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Mesh>(path);
                if (existing == null) AssetDatabase.CreateAsset(mesh, path);
                else
                {
                    // Mesh setters refresh the live GPU buffers as well as the serialized asset.
                    // CopySerialized alone can leave the old model's overlay visible until an editor restart.
                    existing.Clear();
                    existing.vertices = mesh.vertices;
                    existing.triangles = mesh.triangles;
                    existing.normals = mesh.normals;
                    existing.bounds = mesh.bounds;
                    existing.name = mesh.name;
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(mesh);
                    mesh = existing;
                }
                var child = new GameObject("Cracks" + stage, typeof(MeshFilter), typeof(MeshRenderer));
                child.transform.SetParent(root.transform, false);
                child.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = child.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = cracks;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                view.crackStages[stage - 1] = child;
                child.SetActive(false);
            }
            string itemPath = "Assets/Items/" + (name == "Crystal" ? "Crystal" : name + "Ore") + ".asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(itemPath);
            if (item == null) throw new System.InvalidOperationException("Missing resource: " + itemPath);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + name + "NodeVisual.prefab");
            catalog.entries[i] = new OreNodeCatalog.Entry { ore = item, visual = prefab };
            Object.DestroyImmediate(root);
        }
        EditorUtility.SetDirty(catalog);
        var gameplay = PrefabUtility.LoadPrefabContents(RockPath);
        try { Upgrade(gameplay, catalog); PrefabUtility.SaveAsPrefabAsset(gameplay, RockPath); }
        finally { PrefabUtility.UnloadPrefabContents(gameplay); }
        foreach (var rock in Object.FindObjectsByType<RockHealth>(FindObjectsInactive.Include))
        {
            // Scene prefab overrides that selected the ore are preserved.
            Upgrade(rock.gameObject, catalog);
            EditorSceneManager.MarkSceneDirty(rock.gameObject.scene);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[Ore What] KayKit node catalog, four visual prefabs and twelve crack meshes rebuilt.");
    }

    static void Upgrade(GameObject root, OreNodeCatalog catalog)
    {
        foreach (string old in new[] { "Main", "Lump_A", "Lump_B" })
        { var child = root.transform.Find(old); if (child != null) Object.DestroyImmediate(child.gameObject); }
        // Preserve existing gameplay colliders once this prefab has been upgraded.
        if (root.GetComponents<Collider>().Length == 0)
        {
            var baseCollider = root.AddComponent<SphereCollider>();
            baseCollider.center = new Vector3(0, .34f, 0); baseCollider.radius = .58f;
            var mineralCollider = root.AddComponent<SphereCollider>();
            mineralCollider.center = new Vector3(0, .72f, 0); mineralCollider.radius = .37f;
        }
        var visual = root.GetComponent<OreNodeVisual>();
        if (visual == null) visual = root.AddComponent<OreNodeVisual>();
        var so = new SerializedObject(visual);
        so.FindProperty("catalog").objectReferenceValue = catalog;
        so.ApplyModifiedPropertiesWithoutUndo();
        visual.Configure(root.GetComponent<RockHealth>().OreItem, true);
        EditorUtility.SetDirty(visual);
    }

    static Material Material(string name, Color color, bool unlit = false)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        { material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color);
        if (!unlit) material.SetFloat("_Smoothness", .18f);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void OriginalCrystalVisual(Transform parent)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Prototype/Rock.mat");
        void OriginalPart(string name, Vector3 position, Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(part.GetComponent<Collider>());
        }
        OriginalPart("Main", new Vector3(0, .4f, 0), new Vector3(1.2f, .9f, 1.1f));
        OriginalPart("Lump_A", new Vector3(.45f, .3f, .2f), Vector3.one * .6f);
        OriginalPart("Lump_B", new Vector3(-.4f, .25f, -.25f), Vector3.one * .5f);
    }

    static void Part(Transform parent, string name, string model, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Pack + model + ".fbx");
        if (source == null) throw new System.InvalidOperationException("Missing KayKit model " + model);
        var part = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
        part.name = name;
        part.transform.SetLocalPositionAndRotation(position, rotation);
        part.transform.localScale = scale;
        foreach (var collider in part.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
        if (material != null) foreach (var renderer in part.GetComponentsInChildren<Renderer>()) renderer.sharedMaterial = material;
    }

    struct Triangle { public Vector3 a, b, c; }
    static List<Triangle> Triangles(GameObject root)
    {
        var triangles = new List<Triangle>();
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            Matrix4x4 matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int i = 0; i < indices.Length; i += 3)
                triangles.Add(new Triangle { a = matrix.MultiplyPoint3x4(vertices[indices[i]]), b = matrix.MultiplyPoint3x4(vertices[indices[i + 1]]), c = matrix.MultiplyPoint3x4(vertices[indices[i + 2]]) });
        }
        return triangles;
    }

    static bool Project(List<Triangle> triangles, Vector3 origin, Vector3 direction, out Vector3 point)
    {
        float nearest = float.PositiveInfinity;
        point = default;
        foreach (var t in triangles)
        {
            Vector3 e1 = t.b - t.a, e2 = t.c - t.a, h = Vector3.Cross(direction, e2);
            float determinant = Vector3.Dot(e1, h);
            if (Mathf.Abs(determinant) < .000001f) continue;
            float inverse = 1f / determinant;
            Vector3 s = origin - t.a;
            float u = inverse * Vector3.Dot(s, h);
            if (u < 0 || u > 1) continue;
            Vector3 q = Vector3.Cross(s, e1);
            float v = inverse * Vector3.Dot(direction, q);
            if (v < 0 || u + v > 1) continue;
            float distance = inverse * Vector3.Dot(e2, q);
            if (distance < 0 || distance >= nearest) continue;
            nearest = distance;
            point = origin + direction * distance - direction * .004f;
        }
        return !float.IsPositiveInfinity(nearest);
    }

    static UnityEngine.Mesh CrackMesh(List<Triangle> surface, int stage)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        float width = stage == 1 ? .022f : stage == 2 ? .034f : .048f;
        // Four sides and the top: cracks remain readable regardless of the procedural node's yaw.
        for (int face = 0; face < 5; face++)
        {
            Vector3 normal = face == 4 ? Vector3.up : Quaternion.Euler(0, face * 90f, 0) * Vector3.forward;
            Vector3 horizontal = face == 4 ? Vector3.right : Vector3.Cross(Vector3.up, normal);
            Vector3 vertical = face == 4 ? Vector3.forward : Vector3.up;
            int paths = stage == 1 ? 1 : stage == 2 ? 2 : 3;
            for (int path = 0; path < paths; path++)
            {
                Vector2 previous = default; bool hasPrevious = false;
                for (int step = 0; step <= 24; step++)
                {
                    float t = step / 24f;
                    float zigzag = Mathf.PingPong(t * 6f + path * .35f, 1f) - .5f;
                    float x = (path - .8f) * .24f + zigzag * .20f;
                    float y = Mathf.Lerp(-.44f, .52f, t);
                    Vector2 point = new Vector2(x, y);
                    if (hasPrevious)
                    {
                        Vector2 side = new Vector2(width * .5f, 0);
                        var strip = new List<Vector2> { previous - side, previous + side, point + side, point - side };
                        ClipStrip(surface, strip, normal, horizontal, vertical, vertices, indices);
                    }
                    previous = point; hasPrevious = true;
                }
            }
        }
        var mesh = new UnityEngine.Mesh { name = "Cracks" + stage };
        mesh.SetVertices(vertices); mesh.SetTriangles(indices, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    // Clip every strip to actual visible source triangles. This never bridges disconnected nuggets
    // or cuts across a faceted surface: all emitted vertices lie on that triangle's plane.
    static void ClipStrip(List<Triangle> surface, List<Vector2> strip, Vector3 face, Vector3 horizontal,
        Vector3 vertical, List<Vector3> vertices, List<int> indices)
    {
        Vector3 centre = new Vector3(0, .48f, 0);
        Vector2 UV(Vector3 p) => new Vector2(Vector3.Dot(p - centre, horizontal), Vector3.Dot(p - centre, vertical));
        foreach (var triangle in surface)
        {
            Vector3 normal = Vector3.Cross(triangle.b - triangle.a, triangle.c - triangle.a).normalized;
            if (Vector3.Dot(normal, face) < .08f) continue;
            Vector2[] edge = { UV(triangle.a), UV(triangle.b), UV(triangle.c) };
            float determinant = Cross(edge[1] - edge[0], edge[2] - edge[0]);
            if (Mathf.Abs(determinant) < .000001f) continue;
            float winding = Mathf.Sign(determinant);
            var polygon = new List<Vector2>(strip);
            for (int e = 0; e < 3 && polygon.Count > 0; e++)
            {
                Vector2 a = edge[e], b = edge[(e + 1) % 3];
                var clipped = new List<Vector2>();
                Vector2 previous = polygon[polygon.Count - 1];
                float previousDistance = Cross(b - a, previous - a) * winding;
                foreach (Vector2 point in polygon)
                {
                    float distance = Cross(b - a, point - a) * winding;
                    if ((distance >= 0) != (previousDistance >= 0))
                        clipped.Add(Vector2.LerpUnclamped(previous, point, previousDistance / (previousDistance - distance)));
                    if (distance >= 0) clipped.Add(point);
                    previous = point; previousDistance = distance;
                }
                polygon = clipped;
            }
            if (polygon.Count < 3) continue;
            var projected = new List<Vector3>(); Vector3 midpoint = Vector3.zero;
            foreach (var p in polygon)
            {
                Vector2 delta = p - edge[0];
                float u = Cross(delta, edge[2] - edge[0]) / determinant;
                float v = Cross(edge[1] - edge[0], delta) / determinant;
                Vector3 world = triangle.a + u * (triangle.b - triangle.a) + v * (triangle.c - triangle.a);
                projected.Add(world + normal * .003f); midpoint += world;
            }
            midpoint /= polygon.Count;
            if (!Project(surface, midpoint + face * 3f, -face, out Vector3 visible) || (visible - midpoint).sqrMagnitude > .0001f) continue;
            int start = vertices.Count; vertices.AddRange(projected);
            for (int i = 1; i + 1 < projected.Count; i++) indices.AddRange(new[] { start, start + i, start + i + 1 });
        }
    }

    public static GameObject SharedRockPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RockPath);
        if (prefab == null || prefab.GetComponent<OreNodeVisual>() == null) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RockPath); }
        return prefab;
    }
}
