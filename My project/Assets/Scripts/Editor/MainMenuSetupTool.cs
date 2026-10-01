using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the start screen: Assets/Scenes/MainMenu.unity (a small dark mine set made from existing
/// props + the menu UI) and puts it first in the build's scene list, before the gameplay scene.
/// Menu: Ore What > Build Main Menu Scene. Rerunning overwrites MainMenu.unity (not the gameplay scene).
///
/// The images in Assets/start-screen are full 1920x1080 overlays with the artwork at one spot, so
/// each is imported as a Sprite cut to its artwork (the PNG files themselves aren't changed).
/// </summary>
public static class MainMenuSetupTool
{
    public const string ScenePath = "Assets/Scenes/MainMenu.unity";
    private const string GameplayScenePath = "Assets/Scenes/PlayerTest.unity";
    private const string ArtFolder = "Assets/start-screen";
    private const string GeneratedFolder = "Assets/UI/MainMenu";
    private const string VignettePath = GeneratedFolder + "/MenuVignette.png";
    private const string GroundMaterialPath = "Assets/Materials/Menu/MenuGround.mat";
    private const string IdleControllerPath = "Assets/Animations/Character/MenuIdle.controller";
    private const string MinerModelPath = "Assets/CorporateMiner (4)@Unarmed Run Forward.fbx"; // same rigged model as the player body
    private const string RockFolder = "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Rocks/";

    [MenuItem("Ore What/Build Main Menu Scene")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Ore What] Leave Play mode first."); return; }

        Sprite title = ImportCutSprite(ArtFolder + "/Titling.png");
        Sprite play = ImportCutSprite(ArtFolder + "/selection-play.png");
        // Ready for later buttons (not shown until they do something).
        ImportCutSprite(ArtFolder + "/selection-options.png");
        ImportCutSprite(ArtFolder + "/selection-credits.png");
        ImportCutSprite(ArtFolder + "/selection-multiplayer.png");
        Sprite vignette = GetOrCreateVignette();

        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene); // RenderSettings below belong to the active scene

        BuildBackdrop();
        BuildUI(title, play, vignette);

        EditorSceneManager.SaveScene(scene, ScenePath);
        if (previous.IsValid()) SceneManager.SetActiveScene(previous);
        EditorSceneManager.CloseScene(scene, true);
        SetBuildScenes();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Ore What] Built {ScenePath} and made it the first build scene. Open it and press Play to try the menu.");
    }

    // ---- Art import ---------------------------------------------------------------------------

    /// <summary>Imports a full-screen PNG as one Sprite cut to its non-transparent pixels.</summary>
    private static Sprite ImportCutSprite(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) { Debug.LogWarning("[Ore What] Missing " + path); return null; }

        var tex = new Texture2D(2, 2);
        tex.LoadImage(File.ReadAllBytes(path));
        RectInt box = AlphaBounds(tex, 8);
        Object.DestroyImmediate(tex);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();

        var factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        string name = Path.GetFileNameWithoutExtension(path);
        SpriteRect[] existing = provider.GetSpriteRects();
        var rect = existing.Length > 0 ? existing[0] : new SpriteRect { spriteID = GUID.Generate() };
        rect.name = name;
        rect.rect = new Rect(box.x, box.y, box.width, box.height);
        rect.alignment = SpriteAlignment.Center;
        rect.pivot = new Vector2(0.5f, 0.5f);
        provider.SetSpriteRects(new[] { rect });
        var ids = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        ids?.SetNameFileIdPairs(new[] { new SpriteNameFileIdPair(name, rect.spriteID) });
        provider.Apply();
        importer.SaveAndReimport();

        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is Sprite s) return s;
        return null;
    }

    private static RectInt AlphaBounds(Texture2D tex, int pad)
    {
        Color32[] px = tex.GetPixels32();
        int w = tex.width, h = tex.height, minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * w + x].a > 8)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        if (maxX < 0) return new RectInt(0, 0, w, h);
        minX = Mathf.Max(0, minX - pad); minY = Mathf.Max(0, minY - pad);
        maxX = Mathf.Min(w - 1, maxX + pad); maxY = Mathf.Min(h - 1, maxY + pad);
        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Soft dark edges so the white title and button read over the 3D scene.</summary>
    private static Sprite GetOrCreateVignette()
    {
        if (!File.Exists(VignettePath))
        {
            Directory.CreateDirectory(GeneratedFolder);
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                    float a = Mathf.SmoothStep(0.35f, 1.25f, r) * 0.9f;
                    a = Mathf.Max(a, Mathf.SmoothStep(-0.2f, -1f, dy) * 0.55f); // darker floor behind the button
                    tex.SetPixel(x, y, new Color(0.02f, 0.015f, 0.03f, a));
                }
            tex.Apply();
            File.WriteAllBytes(VignettePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(VignettePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(VignettePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(VignettePath);
    }

    // ---- 3D backdrop --------------------------------------------------------------------------

    private static void BuildBackdrop()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.17f, 0.15f, 0.2f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.07f;
        RenderSettings.fogColor = new Color(0.05f, 0.04f, 0.06f);

        var root = new GameObject("Backdrop").transform;

        var camGo = new GameObject("MenuCamera");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = RenderSettings.fogColor;
        cam.fieldOfView = 45f;
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(0f, 1.55f, -6.2f);
        camGo.transform.LookAt(new Vector3(0f, 1.05f, 0.5f));
        camGo.tag = "MainCamera";
        AddMotion(camGo, MenuAmbientMotion.Kind.CameraDrift, 0.06f, 0.12f);

        var moon = new GameObject("Rim Light").AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = new Color(0.45f, 0.55f, 0.85f);
        moon.intensity = 0.35f;
        moon.shadows = LightShadows.Soft;
        moon.transform.rotation = Quaternion.Euler(30f, 160f, 0f);
        moon.transform.SetParent(root, true);

        var lantern = new GameObject("Lantern Light").AddComponent<Light>();
        lantern.type = LightType.Point;
        lantern.color = new Color(1f, 0.6f, 0.28f);
        lantern.intensity = 7f;
        lantern.range = 9f;
        lantern.shadows = LightShadows.Soft;
        lantern.transform.position = new Vector3(-0.4f, 2.1f, -1.4f);
        lantern.transform.SetParent(root, true);
        AddMotion(lantern.gameObject, MenuAmbientMotion.Kind.LightFlicker, 0.12f, 2.5f);

        var oreGlow = new GameObject("Ore Glow").AddComponent<Light>();
        oreGlow.type = LightType.Point;
        oreGlow.color = new Color(1f, 0.45f, 0.15f);
        oreGlow.intensity = 2.5f;
        oreGlow.range = 3f;
        oreGlow.transform.position = new Vector3(2.2f, 0.6f, -0.3f);
        oreGlow.transform.SetParent(root, true);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(root, false);
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        ground.GetComponent<Renderer>().sharedMaterial = GetOrCreateGroundMaterial();
        Object.DestroyImmediate(ground.GetComponent<Collider>());

        // Cave walls: big rocks in an arc behind the props (deterministic layout).
        // (The river rock pile is a wide, flat spread: scaled up as a wall it covers the whole set, so it's only used small.)
        string[] wallRocks = { "PT_Generic_Rock_01", "PT_Menhir_Rock_02" };
        for (int i = 0; i < 11; i++)
        {
            float angle = Mathf.Lerp(-80f, 80f, i / 10f) * Mathf.Deg2Rad;
            float radius = 6.5f + (i % 3) * 0.8f;
            var pos = new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius + 0.5f);
            bool menhir = i % wallRocks.Length == 1;
            float yaw = i * 47f + (menhir ? 180f : 0f); // turns the menhirs' glowing rune away from the camera
            Place(RockFolder + wallRocks[i % wallRocks.Length] + ".prefab", root, pos, yaw, 2.6f + (i * 37 % 5) * 0.5f);
        }

        // The "set": an ore rock with a pickaxe leaning on it, a hammer, a small rock pile, and the miner.
        Place(RockFolder + "PT_Ore_Rock_01.prefab", root, new Vector3(2.3f, 0f, 0.3f), 200f, 1.5f);
        Place(RockFolder + "PT_Generic_Rock_01.prefab", root, new Vector3(-3.4f, 0f, 1.6f), 80f, 1.3f);
        Place(RockFolder + "PT_River_Rock_Pile_02.prefab", root, new Vector3(0.9f, 0f, -1.2f), 30f, 0.45f);
        var pick = Place("Assets/Prefabs/PickaxeModel.prefab", root, new Vector3(1.35f, 0f, -0.2f), 0f, 0f);
        if (pick != null) pick.transform.rotation = Quaternion.Euler(0f, -60f, 18f); // leaning on the ore rock
        var hammer = Place("Assets/Prefabs/HammerModel.prefab", root, new Vector3(-0.9f, 0.08f, -1.6f), 0f, 0f);
        if (hammer != null) hammer.transform.rotation = Quaternion.Euler(0f, 70f, 90f); // lying on its side
        PlaceMiner(root, new Vector3(-1.9f, 0f, 0.2f), 155f);

        // Floating dust in the lantern light.
        var dust = new GameObject("Dust").AddComponent<ParticleSystem>();
        dust.transform.SetParent(root, false);
        dust.transform.position = new Vector3(0f, 1.6f, 0f);
        var main = dust.main;
        main.startLifetime = 9f;
        main.startSpeed = 0.06f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.045f);
        main.startColor = new Color(1f, 0.85f, 0.65f, 0.45f);
        main.maxParticles = 200;
        main.prewarm = true;
        main.useUnscaledTime = true;
        var emission = dust.emission;
        emission.rateOverTime = 18f;
        var shape = dust.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(9f, 3f, 5f);
        var noise = dust.noise;
        noise.enabled = true;
        noise.strength = 0.08f;
        noise.frequency = 0.3f;
        var dustRenderer = dust.GetComponent<ParticleSystemRenderer>();
        if (GraphicsSettings.defaultRenderPipeline != null)
            dustRenderer.sharedMaterial = GraphicsSettings.defaultRenderPipeline.defaultParticleMaterial;
    }

    /// <summary>Instantiates a prefab (no colliders), scaled so its height = <paramref name="height"/> (0 = keep), standing on the ground.</summary>
    private static GameObject Place(string prefabPath, Transform parent, Vector3 pos, float yaw, float height)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) { Debug.LogWarning("[Ore What] Menu prop missing: " + prefabPath); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        if (height > 0f && TryBounds(go, out Bounds b) && b.size.y > 0.001f)
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

    /// <summary>The CorporateMiner model breathing idly (an Animator with one Idle state; no player scripts).</summary>
    private static void PlaceMiner(Transform parent, Vector3 pos, float yaw)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(MinerModelPath);
        if (model == null) return;
        var miner = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        miner.name = "Miner (menu)";
        miner.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        miner.transform.localScale = Vector3.one * 0.9f;
        var animator = miner.GetComponent<Animator>();
        if (animator == null) animator = miner.AddComponent<Animator>();
        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = GetOrCreateIdleController();
    }

    private static AnimatorController GetOrCreateIdleController()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(IdleControllerPath);
        if (existing != null) return existing;
        AnimationClip idle = null;
        foreach (string guid in AssetDatabase.FindAssets("Breathing Idle t:Model"))
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) { idle = c; break; }
        var controller = AnimatorController.CreateAnimatorControllerAtPath(IdleControllerPath);
        var state = controller.layers[0].stateMachine.AddState("Idle");
        state.motion = idle;
        return controller;
    }

    private static Material GetOrCreateGroundMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
        if (mat != null) return mat;
        Directory.CreateDirectory(Path.GetDirectoryName(GroundMaterialPath));
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "MenuGround" };
        mat.SetColor("_BaseColor", new Color(0.22f, 0.17f, 0.14f));
        mat.SetFloat("_Smoothness", 0.1f);
        AssetDatabase.CreateAsset(mat, GroundMaterialPath);
        return mat;
    }

    private static void AddMotion(GameObject go, MenuAmbientMotion.Kind kind, float amount, float speed)
    {
        var m = go.AddComponent<MenuAmbientMotion>();
        var so = new SerializedObject(m);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.FindProperty("amount").floatValue = amount;
        so.FindProperty("speed").floatValue = speed;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---- UI -----------------------------------------------------------------------------------

    private static void BuildUI(Sprite title, Sprite play, Sprite vignette)
    {
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

        var canvasGo = new GameObject("MenuCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var canvas = canvasGo.transform;

        var vig = NewImage("Vignette", canvas, vignette, Color.white);
        vig.preserveAspect = false; // stretched over the whole screen
        Stretch(vig.rectTransform);
        vig.raycastTarget = false;

        // Title (start-screen/Titling.png) near the top, with a soft shadow and a gentle bob.
        var titleImg = NewImage("Title", canvas, title, Color.white);
        titleImg.raycastTarget = false;
        var titleRect = titleImg.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = SpriteSize(title) * 1.1f;
        titleRect.anchoredPosition = new Vector2(0f, -250f);
        AddShadow(titleImg.gameObject, new Vector2(6f, -7f), 0.55f);
        AddMotion(titleImg.gameObject, MenuAmbientMotion.Kind.UIBob, 6f, 0.25f);

        var tagline = NewText("Tagline", canvas, "The world's least reliable mining company", 30, FontStyle.Italic, new Color(1f, 0.82f, 0.55f));
        var tagRect = tagline.rectTransform;
        tagRect.anchorMin = tagRect.anchorMax = new Vector2(0.5f, 1f);
        tagRect.sizeDelta = new Vector2(1000f, 50f);
        tagRect.anchoredPosition = new Vector2(0f, -400f);
        AddShadow(tagline.gameObject, new Vector2(2f, -2f), 0.8f);

        // PLAY: a rounded plate holding the start-screen/selection-play.png label, pickaxe icons beside it.
        var buttonGo = new GameObject("PlayButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(canvas, false);
        var buttonRect = (RectTransform)buttonGo.transform;
        buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
        buttonRect.sizeDelta = new Vector2(380f, 124f);
        buttonRect.anchoredPosition = new Vector2(0f, -170f);
        var plate = buttonGo.GetComponent<Image>();
        plate.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = 0.35f; // rounder corners
        var outline = buttonGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.85f, 0.85f, 0.85f, 0.95f); // light frame like the title box
        outline.effectDistance = new Vector2(4f, -4f);
        AddShadow(buttonGo, new Vector2(5f, -7f), 0.5f);
        var button = buttonGo.GetComponent<Button>();
        button.transition = Selectable.Transition.None;

        var label = NewImage("Label", buttonRect, play, Color.white);
        label.raycastTarget = false;
        label.rectTransform.sizeDelta = SpriteSize(play) * 1.45f;
        label.rectTransform.anchoredPosition = new Vector2(0f, 6f); // the glyphs have descenders ("p", "y")
        AddShadow(label.gameObject, new Vector2(3f, -3f), 0.6f);

        Sprite pickIcon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Items/Icons/Pickaxe.png");
        var markers = new List<RectTransform>();
        foreach (float side in new[] { -1f, 1f })
        {
            if (pickIcon == null) break;
            var m = NewImage(side < 0 ? "MarkerLeft" : "MarkerRight", buttonRect, pickIcon, new Color(1f, 1f, 1f, 0f));
            m.raycastTarget = false;
            m.rectTransform.sizeDelta = new Vector2(84f, 84f);
            m.rectTransform.anchoredPosition = new Vector2(side * 250f, 0f);
            m.rectTransform.localScale = new Vector3(-side, 1f, 1f); // both heads point at the button
            markers.Add(m.rectTransform);
        }

        var feedback = buttonGo.AddComponent<MenuButtonFeedback>();
        var fso = new SerializedObject(feedback);
        fso.FindProperty("plate").objectReferenceValue = plate;
        var markerProp = fso.FindProperty("markers");
        markerProp.arraySize = markers.Count;
        for (int i = 0; i < markers.Count; i++) markerProp.GetArrayElementAtIndex(i).objectReferenceValue = markers[i];
        fso.ApplyModifiedPropertiesWithoutUndo();
        plate.color = new Color(0.13f, 0.1f, 0.08f, 1f);

        var hint = NewText("Hint", canvas, "Click PLAY or press Enter", 22, FontStyle.Normal, new Color(1f, 1f, 1f, 0.45f));
        var hintRect = hint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
        hintRect.sizeDelta = new Vector2(600f, 40f);
        hintRect.anchoredPosition = new Vector2(0f, 60f);

        // Fade overlay (black), used when entering and leaving the menu.
        var fadeImg = NewImage("Fade", canvas, null, Color.black);
        Stretch(fadeImg.rectTransform);
        var fade = fadeImg.gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0f;
        fade.blocksRaycasts = false;
        fade.interactable = false;

        var controller = canvasGo.AddComponent<MainMenuController>();
        var cso = new SerializedObject(controller);
        cso.FindProperty("gameplayScene").stringValue = Path.GetFileNameWithoutExtension(GameplayScenePath);
        cso.FindProperty("playButton").objectReferenceValue = button;
        cso.FindProperty("fade").objectReferenceValue = fade;
        cso.ApplyModifiedPropertiesWithoutUndo();
        es.GetComponent<EventSystem>().firstSelectedGameObject = buttonGo;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.preserveAspect = sprite != null;
        return img;
    }

    private static Text NewText(string name, Transform parent, string text, int size, FontStyle style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        return t;
    }

    private static void AddShadow(GameObject go, Vector2 distance, float alpha)
    {
        var s = go.AddComponent<Shadow>();
        s.effectColor = new Color(0f, 0f, 0f, alpha);
        s.effectDistance = distance;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    private static Vector2 SpriteSize(Sprite s) => s != null ? s.rect.size : new Vector2(200f, 80f);

    // ---- Build settings -----------------------------------------------------------------------

    /// <summary>MainMenu first (the startup scene), then the gameplay scene; other listed scenes are kept after them.</summary>
    private static void SetBuildScenes()
    {
        var list = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(GameplayScenePath, true),
        };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != ScenePath && s.path != GameplayScenePath) list.Add(s);
        EditorBuildSettings.scenes = list.ToArray();
    }
}
