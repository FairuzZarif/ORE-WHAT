using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Gives every Tool/Weapon world pickup (pistol, rifle, pickaxe, hammer, and any future one) an
/// outline, and the Player a PickupHighlighter that decides when it shows. Safe to run again.
/// Menu: Ore What > Add Pickup Highlights
///
/// For each equippable ItemData's World Prefab it:
///   - bakes the item's shape into one mesh (prefab-root space) with smoothed normals, so the
///     outline has no cracks at hard edges: Assets/Prefabs/Items/Outlines/[Item]_Outline.asset
///   - adds a hidden child "PickupOutline" (that mesh, materials PickupOutlineMask + PickupOutline,
///     no shadows, no collider) and a PickupHighlight component on the prefab root.
/// The item's own renderers, materials, colliders and physics are not changed; the source models
/// aren't either (non-readable meshes are read through a temporary baked copy).
/// </summary>
public static class PickupHighlightSetupTool
{
    private const string MaterialFolder = "Assets/Materials/Outline";
    private const string MaskPath = MaterialFolder + "/PickupOutlineMask.mat";
    private const string OutlinePath = MaterialFolder + "/PickupOutline.mat";
    private const string MeshFolder = "Assets/Prefabs/Items/Outlines";
    private const string ChildName = "PickupOutline";

    [MenuItem("Ore What/Add Pickup Highlights")]
    public static void AddMenu()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("[Ore What] No object tagged 'Player' in the open scene."); return; }
        int n = AddAll(player);
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
        Debug.Log($"[Ore What] Pickup highlights added to {n} item prefab(s).");
    }

    /// <summary>Outlines every equippable world prefab and adds the PickupHighlighter to the player. Returns how many prefabs.</summary>
    public static int AddAll(GameObject player)
    {
        Material mask = GetOrCreateMaterial(MaskPath, true);
        Material outline = GetOrCreateMaterial(OutlinePath, false);
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (item == null || !item.Equippable || item.WorldPrefab == null) continue;
            if (AddToPrefab(AssetDatabase.GetAssetPath(item.WorldPrefab), mask, outline)) count++;
        }
        if (player != null && player.GetComponent<PickupHighlighter>() == null) Undo.AddComponent<PickupHighlighter>(player);
        AssetDatabase.SaveAssets();
        return count;
    }

    /// <summary>Adds (or rebuilds) the outline on one pickup prefab. Reusable for any future pickup prefab.</summary>
    public static bool AddToPrefab(string prefabPath, Material mask, Material outline)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            Transform old = root.transform.Find(ChildName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            Mesh shape = BakeShape(root);
            if (shape == null) { Debug.LogWarning($"[Ore What] {prefabPath}: no meshes to outline."); return false; }
            ItemSetupTool.EnsureFolder("Assets/Prefabs/Items", "Outlines");
            string meshPath = $"{MeshFolder}/{root.name}_Outline.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing != null) { EditorUtility.CopySerialized(shape, existing); Object.DestroyImmediate(shape); shape = existing; }
            else AssetDatabase.CreateAsset(shape, meshPath);

            var child = new GameObject(ChildName);
            child.layer = root.layer;
            child.transform.SetParent(root.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = shape;
            var r = child.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { mask, outline }; // both draw the single sub-mesh
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
            r.enabled = false; // PickupHighlight shows it

            var h = root.GetComponent<PickupHighlight>();
            if (h == null) h = root.AddComponent<PickupHighlight>();
            var so = new SerializedObject(h);
            so.FindProperty("outlineRenderer").objectReferenceValue = r;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return true;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>All the item's meshes in one mesh (root space), normals averaged per position so the outline doesn't crack.</summary>
    private static Mesh BakeShape(GameObject root)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            Mesh baked = ReadableCopy(r, out Transform space);
            if (baked == null) continue;
            int start = verts.Count;
            Vector3[] v = baked.vertices, n = baked.normals;
            for (int i = 0; i < v.Length; i++)
            {
                verts.Add(root.transform.InverseTransformPoint(space.TransformPoint(v[i])));
                normals.Add(root.transform.InverseTransformDirection(space.TransformDirection(n.Length > i ? n[i] : Vector3.up)));
            }
            for (int s = 0; s < baked.subMeshCount; s++)
                foreach (int idx in baked.GetTriangles(s)) tris.Add(start + idx);
            Object.DestroyImmediate(baked);
        }
        if (verts.Count == 0) return null;

        // Smooth: every vertex at the same position gets the same (averaged) normal.
        var sum = new Dictionary<Vector3Int, Vector3>();
        Vector3Int Key(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));
        for (int i = 0; i < verts.Count; i++)
        {
            var k = Key(verts[i]);
            sum[k] = (sum.TryGetValue(k, out Vector3 acc) ? acc : Vector3.zero) + normals[i].normalized;
        }
        for (int i = 0; i < verts.Count; i++) normals[i] = sum[Key(verts[i])].normalized;

        var mesh = new Mesh { name = root.name + "_Outline" };
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// A readable copy of a renderer's mesh and the transform its vertices are in. Skinned meshes are
    /// baked; non-readable static meshes are baked through a temporary SkinnedMeshRenderer (so the
    /// source model's import settings never have to change).
    /// </summary>
    private static Mesh ReadableCopy(Renderer r, out Transform space)
    {
        space = r.transform;
        if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
        {
            var m = new Mesh();
            smr.BakeMesh(m, true);
            return m;
        }
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return null;
        if (mf.sharedMesh.isReadable) return Object.Instantiate(mf.sharedMesh);
        var temp = new GameObject("TempBake");
        try
        {
            var tsmr = temp.AddComponent<SkinnedMeshRenderer>();
            tsmr.sharedMesh = mf.sharedMesh;
            var m = new Mesh();
            tsmr.BakeMesh(m, true);
            return m;
        }
        finally { Object.DestroyImmediate(temp); }
    }

    /// <summary>The two outline materials: the stencil mask (queue 3000) and the ring (queue 3001).</summary>
    private static Material GetOrCreateMaterial(string path, bool isMask)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            ItemSetupTool.EnsureFolder("Assets/Materials", "Outline");
            mat = new Material(Shader.Find("Ore What/Pickup Outline")) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(mat, path);
        }
        if (isMask)
        {
            mat.SetFloat("_OutlineWidth", 0f);
            mat.SetFloat("_StencilComp", (float)CompareFunction.Always);
            mat.SetFloat("_StencilPass", (float)StencilOp.Replace);
            mat.SetFloat("_ColorMask", 0f);
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            mat.SetFloat("_OutlineWidth", 2.5f);
            mat.SetFloat("_StencilComp", (float)CompareFunction.NotEqual);
            mat.SetFloat("_StencilPass", (float)StencilOp.Keep);
            mat.SetFloat("_ColorMask", 15f);
            mat.SetColor("_OutlineColor", new Color(1f, 0.72f, 0.18f, 1f) * 1.6f);
            mat.renderQueue = (int)RenderQueue.Transparent + 1;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
