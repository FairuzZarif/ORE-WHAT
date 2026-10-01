using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds the hardhat headlamp to the Player: Headlamp (+ its Spot Light child "Headlamp Light"),
/// HeadlampToggle (key L) and HeadlampHUD. The lens position is measured from the character's own
/// "Headlamp" mesh (its front-most vertices, in the head bone's space). Safe to run again; light
/// settings are only set when the light is first created.
/// Menu: Ore What > Add Headlamp (also called by MiningSetupTool and CharacterSetupTool).
/// </summary>
public static class HeadlampSetupTool
{
    [MenuItem("Ore What/Add Headlamp")]
    public static void Menu()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("[Ore What] No object tagged 'Player' in the open scene."); return; }
        Add(player);
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
    }

    public static void Add(GameObject player)
    {
        var headlamp = player.GetComponent<Headlamp>();
        if (headlamp == null) headlamp = Undo.AddComponent<Headlamp>(player);
        if (player.GetComponent<HeadlampToggle>() == null) Undo.AddComponent<HeadlampToggle>(player);
        if (player.GetComponent<HeadlampHUD>() == null) Undo.AddComponent<HeadlampHUD>(player);

        Transform lightT = player.transform.Find("Headlamp Light");
        if (lightT == null)
        {
            var go = new GameObject("Headlamp Light", typeof(Light));
            Undo.RegisterCreatedObjectUndo(go, "Add Headlamp");
            go.transform.SetParent(player.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 0.3f);
            var l = go.GetComponent<Light>();
            ApplyBeam(l);
            l.enabled = false;
            lightT = go.transform;
        }

        var so = new SerializedObject(headlamp);
        so.FindProperty("lamp").objectReferenceValue = lightT.GetComponent<Light>();
        var body = player.GetComponentInChildren<CharacterAnimator>(true);
        Animator animator = body != null ? body.GetComponent<Animator>() : null;
        so.FindProperty("character").objectReferenceValue = animator;
        Camera cam = player.GetComponentInChildren<Camera>();
        if (cam != null) so.FindProperty("aimSource").objectReferenceValue = cam.transform;
        if (animator != null && TryMeasureLens(animator, player.transform, out Vector3 lens))
            so.FindProperty("lensOffset").vector3Value = lens;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private const string CookiePath = "Assets/Textures/HeadlampBeam.png";

    /// <summary>
    /// The wide mining-lamp beam: broad cone with a soft edge, long range, and a beam texture (cookie) that
    /// dims the lower part of the cone, which always lands on the floor close by. The brightness itself
    /// is set every frame by Headlamp (it adapts to the distance ahead).
    /// </summary>
    public static void ApplyBeam(Light l)
    {
        l.type = LightType.Spot;
        l.color = new Color(1f, 0.95f, 0.86f); // slightly warm white
        l.range = 45f;          // URP's range fade starts well before Range: 45 keeps 30-35 m lit
        l.spotAngle = 95f;
        l.innerSpotAngle = 75f; // the cookie does the soft fall-off, not a bright centre
        l.intensity = 16f;      // Headlamp overrides this at runtime
        l.cookie = GetOrCreateCookie();
        l.cullingMask = ~(1 << 6);     // not the first-person arms / held item (ViewModel layer, 0.5 m from the lens: blown out)
        l.shadows = LightShadows.None; // the hidden head/hardhat would shadow the lamp at its own lens
    }

    /// <summary>The beam texture (white = full light). Rewritten when <paramref name="rebuild"/> is true.</summary>
    public static Texture2D GetOrCreateCookie(bool rebuild = false)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(CookiePath);
        if (existing != null && !rebuild) return existing;
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f; // -1..1
                float r = Mathf.Sqrt(u * u + v * v);
                float edge = 1f - Smooth(0.35f, 1f, r);                    // broad disc, very soft edge (no visible rim)
                // Low rows (v < 0) light the BOTTOM of the cone (checked on a flat panel): the floor close by gets less.
                float lower = Mathf.Lerp(1f, 0.06f, Smooth(0f, 0.7f, -v));
                float c = Mathf.Clamp01(edge * lower);
                tex.SetPixel(x, y, new Color(c, c, c, c));
            }
        tex.Apply();
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CookiePath));
        System.IO.File.WriteAllBytes(CookiePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(CookiePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(CookiePath);
        importer.textureType = TextureImporterType.Cookie;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaSource = TextureImporterAlphaSource.FromGrayScale;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(CookiePath);
    }

    /// <summary>GLSL-style smoothstep: 0 at <paramref name="a"/>, 1 at <paramref name="b"/> (Mathf.SmoothStep interpolates between its first two arguments instead).</summary>
    private static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

    /// <summary>The front of the "Headlamp" mesh (its lens), in the head bone's space.</summary>
    private static bool TryMeasureLens(Animator animator, Transform player, out Vector3 lens)
    {
        lens = default;
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        SkinnedMeshRenderer lamp = null;
        foreach (var r in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name.ToLowerInvariant().Contains("headlamp")) lamp = r;
        if (head == null || lamp == null) return false;

        var baked = new Mesh();
        lamp.BakeMesh(baked, true);
        Vector3[] verts = baked.vertices;
        Object.DestroyImmediate(baked);
        if (verts.Length == 0) return false;
        float front = float.MinValue;
        foreach (Vector3 v in verts) front = Mathf.Max(front, Vector3.Dot(lamp.transform.TransformPoint(v) - head.position, player.forward));
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Vector3 v in verts)
        {
            Vector3 w = lamp.transform.TransformPoint(v);
            if (Vector3.Dot(w - head.position, player.forward) > front - 0.02f) { sum += w; count++; }
        }
        lens = head.InverseTransformPoint(sum / count);
        return true;
    }
}
