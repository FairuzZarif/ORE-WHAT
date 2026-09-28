using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor utility that (re)builds the Phase 1 test scene from Unity primitives.
/// Menu: Ore What > Build Player Test Scene
/// </summary>
public static class PrototypeSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/PlayerTest.unity";
    private const string MaterialFolder = "Assets/Materials/Prototype";

    [MenuItem("Ore What/Build Player Test Scene")]
    public static void BuildScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build(NewSceneMode.Single);
    }

    /// <summary>
    /// Builds and saves the scene. Additive mode keeps currently open scenes untouched.
    /// </summary>
    public static void Build(NewSceneMode mode)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
        SceneManager.SetActiveScene(scene); // lighting settings apply to the active scene

        // --- Materials ------------------------------------------------------
        Material floorMat    = GetOrCreateMaterial("Floor",    new Color(0.35f, 0.38f, 0.35f));
        Material wallMat     = GetOrCreateMaterial("Wall",     new Color(0.55f, 0.50f, 0.45f));
        Material obstacleMat = GetOrCreateMaterial("Obstacle", new Color(0.85f, 0.45f, 0.15f));
        Material platformMat = GetOrCreateMaterial("Platform", new Color(0.20f, 0.50f, 0.85f));
        Material rampMat     = GetOrCreateMaterial("Ramp",     new Color(0.30f, 0.70f, 0.35f));
        Material playerMat   = GetOrCreateMaterial("Player",   new Color(0.90f, 0.85f, 0.20f));

        // --- Lighting -------------------------------------------------------
        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.color = new Color(1f, 0.96f, 0.88f);
        light.shadows = LightShadows.Soft;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.62f, 0.75f);
        RenderSettings.ambientEquatorColor = new Color(0.40f, 0.40f, 0.40f);
        RenderSettings.ambientGroundColor = new Color(0.20f, 0.20f, 0.20f);
        RenderSettings.sun = light;

        // --- Environment ----------------------------------------------------
        var environment = new GameObject("Environment").transform;

        // Floor: default plane is 10x10 m, scaled to 100x100 m.
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(environment);
        floor.transform.localScale = new Vector3(10f, 1f, 10f);
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;

        // Perimeter walls
        var walls = Group("Walls", environment);
        Box("Wall_North", walls, new Vector3(0f, 2f, 30f),  new Vector3(60f, 4f, 1f), wallMat);
        Box("Wall_South", walls, new Vector3(0f, 2f, -30f), new Vector3(60f, 4f, 1f), wallMat);
        Box("Wall_East",  walls, new Vector3(30f, 2f, 0f),  new Vector3(1f, 4f, 60f), wallMat);
        Box("Wall_West",  walls, new Vector3(-30f, 2f, 0f), new Vector3(1f, 4f, 60f), wallMat);
        // An inner L-shaped wall to walk around and test sliding along walls
        Box("Wall_Inner_A", walls, new Vector3(-10f, 1.5f, 10f), new Vector3(12f, 3f, 0.5f), wallMat);
        Box("Wall_Inner_B", walls, new Vector3(-15.75f, 1.5f, 5f), new Vector3(0.5f, 3f, 10f), wallMat);

        // Obstacles of different sizes
        var obstacles = Group("Obstacles", environment);
        Box("Crate_Small",  obstacles, new Vector3(4f, 0.25f, 6f),   new Vector3(0.5f, 0.5f, 0.5f), obstacleMat); // taller than stepOffset, so jump onto it
        Box("Crate_Medium", obstacles, new Vector3(6f, 0.5f, 8f),    new Vector3(1f, 1f, 1f),       obstacleMat); // jumpable
        Box("Crate_Large",  obstacles, new Vector3(10f, 1f, 4f),     new Vector3(2f, 2f, 2f),       obstacleMat); // too tall to jump
        Box("Pillar",       obstacles, new Vector3(-6f, 3f, -8f),    new Vector3(1.5f, 6f, 1.5f),   obstacleMat);
        Box("Block_Wide",   obstacles, new Vector3(12f, 1.5f, -12f), new Vector3(6f, 3f, 3f),       obstacleMat);

        // Step-up platforms and a ramp for testing jumping and slopes
        var platforms = Group("Platforms", environment);
        Box("Step_1", platforms, new Vector3(-12f, 0.4f, -18f), new Vector3(3f, 0.8f, 3f), platformMat);
        Box("Step_2", platforms, new Vector3(-12f, 0.8f, -21f), new Vector3(3f, 1.6f, 3f), platformMat);
        Box("Step_3", platforms, new Vector3(-12f, 1.2f, -24f), new Vector3(3f, 2.4f, 3f), platformMat);
        var ramp = Box("Ramp", platforms, new Vector3(18f, 1f, 10f), new Vector3(4f, 0.3f, 8f), rampMat);
        ramp.transform.rotation = Quaternion.Euler(-15f, 0f, 0f);

        // --- Player ---------------------------------------------------------
        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 1f, 0f); // feet start 1 m above the floor
        player.tag = "Player";

        var controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.4f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        controller.stepOffset = 0.3f;
        controller.slopeLimit = 45f;
        controller.skinWidth = 0.05f;

        // Visible body so the player shows up in the Scene view (and later, to other players).
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(player.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        body.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
        Object.DestroyImmediate(body.GetComponent<Collider>()); // CharacterController is the collider
        body.GetComponent<Renderer>().sharedMaterial = playerMat;

        var camGO = new GameObject("PlayerCamera");
        camGO.tag = "MainCamera";
        camGO.transform.SetParent(player.transform, false);
        camGO.transform.localPosition = new Vector3(0f, 1.6f, 0f); // eye height
        var cam = camGO.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.fieldOfView = 75f;
        camGO.AddComponent<AudioListener>();

        player.AddComponent<PlayerMovement>();
        var look = player.AddComponent<PlayerLook>();
        var so = new SerializedObject(look);
        so.FindProperty("cameraTransform").objectReferenceValue = camGO.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        // --- Save & register in build settings ------------------------------
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettingsFirst(ScenePath);
        MiningSetupTool.AddToScene(); // pickaxe, MiningController, rocks
        CameraEffectsSetupTool.AddToPlayer(player); // CameraRoot + head bob / landing / sway
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = player;
        Debug.Log($"[Ore What] Built {ScenePath}. Press Play to test.");
    }

    private static Transform Group(string name, Transform parent)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent);
        return t;
    }

    private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 size, Material mat)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent);
        box.transform.position = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = mat;
        box.isStatic = true;
        return box;
    }

    internal static Material GetOrCreateMaterial(string name, Color color)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder("Assets/Materials", "Prototype");

        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }

        // URP Lit uses _BaseColor; Standard uses _Color. Set both to be safe.
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void AddSceneToBuildSettingsFirst(string path)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == path);
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
