using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Player damage, death and respawn on the scene Player. Safe to run again.
/// Menu: Ore What > Add Player Combat (also called by MiningSetupTool and CharacterSetupTool; Set Up Multiplayer
/// uses its blood effect).
///  - PlayerDeath (death, ragdoll, death camera, respawn), DeathScreen ("YOU DIED" + countdown + RESPAWN NOW),
///    DamageFlash (red edges when hurt) and HitConfirmFeedback (your confirmed hits: damage numbers, tick / kill
///    sound) on the Player; CharacterRagdoll on the body.
///  - Assets/Materials/Effects/BloodSplat.mat (+ BloodSplats.png): the splats BloodSplatter leaves on surfaces.
///  - Assets/Resources/CombatSettings.asset: friendly fire, respawn delay, ragdoll pushes, host hit checks.
///  - Assets/Prefabs/Effects/BloodHit.prefab (+ Assets/Materials/Effects/BloodHit.mat/.png): the blood burst.
///  - The hammer hits players a little harder than the pickaxe (25 vs 20).
/// </summary>
public static class CombatSetupTool
{
    private const string SettingsPath = "Assets/Resources/CombatSettings.asset";
    private const string BloodPrefabPath = "Assets/Prefabs/Effects/BloodHit.prefab";
    private const string BloodMaterialPath = "Assets/Materials/Effects/BloodHit.mat";
    private const string BloodTexturePath = "Assets/Materials/Effects/BloodHit.png";
    private const float HammerPlayerDamage = 25f;

    [MenuItem("Ore What/Add Player Combat")]
    public static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Ore What] Stop Play mode first."); return; }
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("[Ore What] No object tagged 'Player' in the open scene."); return; }
        Add(player);
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
    }

    public static void Add(GameObject player)
    {
        EnsureSettings();
        EnsureBloodEffect();
        if (player.GetComponent<PlayerAttributes>() == null) Undo.AddComponent<PlayerAttributes>(player);
        if (player.GetComponent<PlayerDeath>() == null) Undo.AddComponent<PlayerDeath>(player);
        if (player.GetComponent<DeathScreen>() == null) Undo.AddComponent<DeathScreen>(player);
        if (player.GetComponent<DamageFlash>() == null) Undo.AddComponent<DamageFlash>(player);
        if (player.GetComponent<HitConfirmFeedback>() == null) Undo.AddComponent<HitConfirmFeedback>(player);
        EnsureSplatMaterial();

        Transform visual = player.transform.Find("CharacterVisual");
        Animator body = visual != null ? visual.GetComponentInChildren<Animator>(true) : null;
        if (body != null && body.GetComponent<CharacterRagdoll>() == null) Undo.AddComponent<CharacterRagdoll>(body.gameObject);

        foreach (MiningToolController tool in player.GetComponentsInChildren<MiningToolController>(true))
        {
            if (tool.gameObject.name != "HammerViewModel") continue;
            var so = new SerializedObject(tool);
            so.FindProperty("playerDamage").floatValue = HammerPlayerDamage;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        Debug.Log("[Ore What] Player combat set up: PlayerDeath, DeathScreen, DamageFlash, CharacterRagdoll, " + SettingsPath + ", " + BloodPrefabPath);
    }

    public static CombatSettings EnsureSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<CombatSettings>(SettingsPath);
        if (settings != null) return settings;
        ItemSetupTool.EnsureFolder("Assets", "Resources");
        settings = ScriptableObject.CreateInstance<CombatSettings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);
        AssetDatabase.SaveAssets();
        return settings;
    }

    // ------------------------------------------------------------------ blood

    /// <summary>
    /// The burst at the hit (rebuilt every run, same asset): dark red droplets sprayed back out of the wound (the prefab's
    /// forward faces the shooter), more thrown out the far side along the shot, a fine fast spray and a quick puff of
    /// mist. Random counts per hit; everything ends within 0.8 s and the prefab destroys itself. The splats it leaves on
    /// surfaces are BloodSplatter's (EnsureSplatMaterial). Clearly not rock chips (grey, bouncing) or a muzzle flash.
    /// </summary>
    public static GameObject EnsureBloodEffect()
    {
        ItemSetupTool.EnsureFolder("Assets", "Prefabs");
        ItemSetupTool.EnsureFolder("Assets/Prefabs", "Effects");
        Material material = BloodMaterial();
        Color dark = new Color(0.25f, 0.01f, 0.01f, 1f), red = new Color(0.48f, 0.03f, 0.02f, 1f);

        var root = new GameObject("BloodHit");
        Configure(root.AddComponent<ParticleSystem>(), material, count: new Vector2Int(14, 24), life: new Vector2(0.35f, 0.65f),
                  speed: new Vector2(1.2f, 3.4f), size: new Vector2(0.025f, 0.065f), gravity: 1.6f, angle: 35f, colourA: red, colourB: dark, stretch: true);
        Configure(Child(root, "Exit", Quaternion.Euler(0f, 180f, 0f)), material, count: new Vector2Int(8, 16), life: new Vector2(0.3f, 0.6f),
                  speed: new Vector2(1.8f, 4.2f), size: new Vector2(0.03f, 0.07f), gravity: 1.8f, angle: 22f, colourA: red, colourB: dark, stretch: true);
        Configure(Child(root, "Fine Spray", Quaternion.identity), material, count: new Vector2Int(10, 18), life: new Vector2(0.12f, 0.25f),
                  speed: new Vector2(3f, 6f), size: new Vector2(0.01f, 0.02f), gravity: 0.6f, angle: 60f, colourA: red, colourB: dark, stretch: true);
        Configure(Child(root, "Mist", Quaternion.identity), material, count: new Vector2Int(3, 6), life: new Vector2(0.18f, 0.35f),
                  speed: new Vector2(0.3f, 0.9f), size: new Vector2(0.12f, 0.24f), gravity: 0.1f, angle: 50f,
                  colourA: new Color(0.55f, 0.04f, 0.04f, 0.55f), colourB: new Color(0.35f, 0.02f, 0.02f, 0.4f), stretch: false);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, BloodPrefabPath); // overwrites in place (same GUID)
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static ParticleSystem Child(GameObject root, string name, Quaternion rotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localRotation = rotation;
        return go.AddComponent<ParticleSystem>();
    }

    private static void Configure(ParticleSystem ps, Material material, Vector2Int count, Vector2 life, Vector2 speed, Vector2 size,
                                  float gravity, float angle, Color colourA, Color colourB, bool stretch)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = true;
        main.loop = false;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = new ParticleSystem.MinMaxGradient(colourA, colourB);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count.y;
        main.stopAction = ps.transform.parent == null ? ParticleSystemStopAction.Destroy : ParticleSystemStopAction.None;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count.x, (short)count.y) }); // a random number each hit
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone; // along the prefab's forward = out of the hit
        shape.angle = angle;
        shape.radius = 0.03f;
        shape.rotation = Vector3.zero;
        var colour = ps.colorOverLifetime; colour.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colour.color = fade;
        var sizeOverLife = ps.sizeOverLifetime; sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, stretch ? 0.6f : 1.6f));
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
        renderer.velocityScale = stretch ? 0.03f : 0f;
        renderer.lengthScale = stretch ? 1.5f : 1f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = material;
    }

    /// <summary>Alpha-blended URP particle material with a generated soft round blob.</summary>
    private static Material BloodMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(BloodMaterialPath);
        if (mat != null) return mat;
        ItemSetupTool.EnsureFolder("Assets/Materials", "Effects");

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f) / size * 2f - Vector2.one;
                float a = Mathf.Clamp01(1f - p.magnitude);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, a * 1.6f)));
            }
        System.IO.File.WriteAllBytes(BloodTexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(BloodTexturePath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(BloodTexturePath);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();

        mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "BloodHit" };
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BloodTexturePath));
        mat.SetColor("_BaseColor", Color.white); // the particles carry the red
        mat.SetFloat("_Surface", 1f);   // transparent
        mat.SetFloat("_Blend", 0f);     // alpha
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(mat, BloodMaterialPath);
        return mat;
    }

    // ------------------------------------------------------------------ splats on surfaces

    private const string SplatTexturePath = "Assets/Materials/Effects/BloodSplats.png";
    private const string SplatMaterialPath = "Assets/Materials/Effects/BloodSplat.mat";

    /// <summary>
    /// The material of the splats BloodSplatter leaves on surfaces: URP Lit, transparent, a little glossy (wet), lit by
    /// the cave lights and headlamps like the rock. Its texture holds four looks in a 2×2 grid (made here, stylised):
    /// 0 droplets, 1 medium splat, 2 big irregular splat, 3 streak (sprays along +Y).
    /// </summary>
    public static Material EnsureSplatMaterial()
    {
        ItemSetupTool.EnsureFolder("Assets", "Materials");
        ItemSetupTool.EnsureFolder("Assets/Materials", "Effects");
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(SplatTexturePath) == null) BuildSplatTexture();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(SplatMaterialPath);
        bool created = mat == null;
        if (created) mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "BloodSplat" };
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SplatTexturePath));
        mat.SetColor("_BaseColor", new Color(0.36f, 0.02f, 0.02f, 0.95f));
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.45f);
        mat.SetFloat("_Surface", 1f);   // transparent
        mat.SetFloat("_Blend", 0f);     // alpha
        // Plain alpha blending. URP's default "preserve specular" keeps reflections where the texture is see-through,
        // which showed every splat as a glossy grey card.
        mat.SetFloat("_BlendModePreserveSpecular", 0f);
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_EnvironmentReflections", 0f);
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent - 10; // before other see-through things (particles)
        if (created) AssetDatabase.CreateAsset(mat, SplatMaterialPath);
        else EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void BuildSplatTexture()
    {
        const int size = 512, cell = size / 2;
        var alpha = new float[size * size];
        var rng = new System.Random(2026);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        // Shapes are drawn in each cell's own -1..1 space (kept inside ±0.9 so cells never bleed into each other).
        void Blob(int look, float cx, float cy, float rx, float ry)
        {
            int ox = (look % 2) * cell, oy = (look / 2) * cell;
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    float u = (x + 0.5f) / cell * 2f - 1f, v = (y + 0.5f) / cell * 2f - 1f;
                    float d = Mathf.Sqrt((u - cx) * (u - cx) / (rx * rx) + (v - cy) * (v - cy) / (ry * ry));
                    float edge = 0.04f / Mathf.Min(rx, ry);
                    float a = Mathf.Clamp01((1f - d) / edge);
                    int i = (oy + y) * size + ox + x;
                    alpha[i] = Mathf.Max(alpha[i], a);
                }
        }
        void Drops(int look, int count, float spread, float rMin, float rMax)
        {
            for (int k = 0; k < count; k++)
            {
                float ang = R(0f, Mathf.PI * 2f), dist = Mathf.Sqrt(R(0.05f, 1f)) * spread, r = R(rMin, rMax);
                Blob(look, Mathf.Cos(ang) * dist, Mathf.Sin(ang) * dist, r, r);
            }
        }

        Drops(0, 14, 0.75f, 0.04f, 0.13f);                                                  // droplets
        Blob(1, 0f, 0f, 0.38f, 0.34f); Drops(1, 10, 0.72f, 0.05f, 0.13f);                   // medium splat
        for (int k = 0; k < 7; k++) { float a = k * 0.9f; Blob(1, Mathf.Cos(a) * 0.4f, Mathf.Sin(a) * 0.4f, 0.22f, 0.07f); }
        for (int k = 0; k < 6; k++) Blob(2, R(-0.3f, 0.3f), R(-0.3f, 0.3f), R(0.22f, 0.36f), R(0.2f, 0.34f)); // big irregular
        Drops(2, 16, 0.85f, 0.04f, 0.12f);
        Blob(3, 0f, -0.25f, 0.2f, 0.45f); Blob(3, 0f, 0.3f, 0.1f, 0.3f);                    // streak (along +Y)
        for (int k = 0; k < 8; k++) Blob(3, R(-0.12f, 0.12f), R(0.2f, 0.85f), R(0.03f, 0.08f), R(0.05f, 0.12f));

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            float a = alpha[i];
            byte shade = (byte)(255f * (0.75f + 0.25f * (1f - a))); // edges a bit lighter than the thick middle
            pixels[i] = new Color32(shade, shade, shade, (byte)(a * 255f));
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        System.IO.File.WriteAllBytes(SplatTexturePath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(SplatTexturePath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(SplatTexturePath);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
    }
}
