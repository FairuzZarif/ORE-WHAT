using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Editor utility that adds the mining prototype to the open scene:
/// creates the Rock and PickaxeModel prefabs, gives the player MiningController,
/// Crosshair and first-person arms holding the pickaxe (ViewModelSetupTool),
/// and places a few rocks.
/// Safe to run more than once. Existing scene objects are left alone.
/// Menu: Ore What > Add Mining Setup To Scene
/// </summary>
public static class MiningSetupTool
{
    private const string PrefabFolder = "Assets/Prefabs";
    private const string RockPrefabPath = PrefabFolder + "/Rock.prefab";
    private const string PickaxePrefabPath = PrefabFolder + "/PickaxeModel.prefab";
    private const string CavingPackPath = "Assets/Simple Caving Pack/SimpleCavingPack.blend";

    [MenuItem("Ore What/Add Mining Setup To Scene")]
    public static void AddToScene()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogError("[Ore What] No object tagged 'Player' in the open scene. Build the player test scene first.");
            return;
        }
        Camera cam = player.GetComponentInChildren<Camera>();
        if (cam == null)
        {
            Debug.LogError("[Ore What] The Player has no child Camera.");
            return;
        }

        ItemSetupTool.EnsureAssets(); // item assets first, so the rock can drop Copper Ore items
        GameObject rockPrefab = CreateRockPrefab();
        CreatePickaxePrefab();

        // --- Player components ---------------------------------------------
        var mining = player.GetComponent<MiningController>();
        if (mining == null) mining = Undo.AddComponent<MiningController>(player);
        var so = new SerializedObject(mining);
        so.FindProperty("playerCamera").objectReferenceValue = cam;
        var pickaxeMissClips = so.FindProperty("pickaxeMissClips");
        if (pickaxeMissClips.arraySize == 0)
        {
            pickaxeMissClips.arraySize = 2;
            for (int i = 0; i < pickaxeMissClips.arraySize; i++)
                pickaxeMissClips.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(
                    $"Assets/Audio/Swings/swing_{i + 1}.mp3");
        }
        if (pickaxeMissClips.arraySize == 2)
        {
            pickaxeMissClips.arraySize = 3;
            pickaxeMissClips.GetArrayElementAtIndex(2).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/Audio/Swings/swing_3.wav");
        }
        so.ApplyModifiedProperties();

        // --- First-person arms + pickaxe (also wires MiningController.pickaxeSwing) ---
        if (cam.transform.Find("FirstPersonViewModel") == null)
            ViewModelSetupTool.Build(player, cam);

        if (player.GetComponent<Crosshair>() == null) Undo.AddComponent<Crosshair>(player);

        // Rock chips + impact sound, fired from MiningController.SurfaceHit.
        var fx = player.GetComponent<MiningImpactFX>();
        if (fx == null) fx = Undo.AddComponent<MiningImpactFX>(player);
        var fxSo = new SerializedObject(fx);
        fxSo.FindProperty("chipMaterial").objectReferenceValue = GetOrCreateChipMaterial();
        var oreClips = fxSo.FindProperty("oreImpactClips");
        if (oreClips.arraySize == 0)
        {
            oreClips.arraySize = 4;
            for (int i = 0; i < oreClips.arraySize; i++)
                oreClips.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(
                    $"Assets/Audio/Impacts/Ore/ore_hit_{i + 1:00}.wav");
        }
        fxSo.ApplyModifiedProperties();

        // --- Rocks -----------------------------------------------------------
        if (GameObject.Find("Rocks") == null)
        {
            var rocks = new GameObject("Rocks").transform;
            PlaceRock(rockPrefab, rocks, new Vector3(-3f, 0f, 4f), 0f, 1f);
            PlaceRock(rockPrefab, rocks, new Vector3(0.5f, 0f, 5.5f), 70f, 1.15f);
            PlaceRock(rockPrefab, rocks, new Vector3(-1.5f, 0f, 8.5f), 200f, 0.9f);
            Undo.RegisterCreatedObjectUndo(rocks.gameObject, "Add Rocks");
        }

        // Inventory, pickup (E), dropping (Q), item pushing and the item HUD.
        ItemSetupTool.AddToPlayer(player);
        HammerSetupTool.AddHammer(player); // melee weapon: its own view + attack controller, without mining capability
        HotbarSetupTool.Add(player);       // 7-slot hotbar, held-item view, item icons
        WeaponSetupTool.AddWeapons(player); // Pistol + Assault Rifle (holdable only) and their test pickups
        PickupHighlightSetupTool.AddAll(player); // outlines on Tool/Weapon pickups + the Player's PickupHighlighter
        PlayerStatsSetupTool.Add(player);           // health + stamina and their HUD bars
        CrouchSetupTool.Add(player);                // C to crouch, plus the body's crouch pose
        HeadlampSetupTool.Add(player);              // L toggles the hardhat's headlamp
        FistsSetupTool.Add(player);                 // fists for empty hotbar slots (copy of ItemHoldViewModel)
        FirstPersonCharacterArmsTool.Apply(player); // the views above get the character's own arms (if it has a CharacterVisual)
        CombatSetupTool.Add(player);                // damage feedback, death (ragdoll), death screen, respawn

        AssetDatabase.SaveAssets(); // persist material tweaks (metallic/smoothness)
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
        Debug.Log("[Ore What] Mining setup added. Press Play and mine the KayKit resource nodes with the pickaxe.");
    }

    /// <summary>Particle material for rock chips (URP Particles/Lit, tinted per hit by particle colour).</summary>
    private static Material GetOrCreateChipMaterial()
    {
        const string path = "Assets/Materials/Prototype/RockChips.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(shader) { name = "RockChips" };
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void PlaceRock(GameObject prefab, Transform parent, Vector3 position, float yaw, float scale)
    {
        var rock = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        rock.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        rock.transform.localScale = Vector3.one * scale;
    }

    /// <summary>The shared gameplay node with resource-specific KayKit presentation.</summary>
    private static GameObject CreateRockPrefab()
    {
        return KayKitMiningSetup.SharedRockPrefab();
    }

    /// <summary>
    /// Pulls the pickaxe meshes out of the Simple Caving Pack .blend into a prefab whose
    /// pivot is at the hand grip, with the handle pointing up (+Y) and the head along Z.
    /// </summary>
    private static GameObject CreatePickaxePrefab()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(CavingPackPath);
        if (source == null)
        {
            Debug.LogWarning($"[Ore What] {CavingPackPath} not found. Mining will work, but without a pickaxe model.");
            return null;
        }

        Material wood = PrototypeSceneBuilder.GetOrCreateMaterial("Pickaxe_Handle", new Color(0.55f, 0.36f, 0.20f));
        Material metal = PrototypeSceneBuilder.GetOrCreateMaterial("Pickaxe_Head", new Color(0.75f, 0.75f, 0.78f));
        metal.SetFloat("_Metallic", 0.7f);
        metal.SetFloat("_Smoothness", 0.5f);

        GameObject pack = Object.Instantiate(source);

        // The grip is near the bottom of the handle (the handle runs from y=0 to y=0.81 at x=0.70).
        // Rotating 90° around Y turns the head (which spans X in the .blend) to face forward.
        var model = new GameObject("PickaxeModel");
        model.transform.SetPositionAndRotation(new Vector3(0.70f, 0.15f, -0.05f), Quaternion.Euler(0f, 90f, 0f));

        foreach (string part in new[] { "Pickaxe Handle", "Pickaxe Head", "Pickaxe Head Base" })
        {
            Transform t = pack.transform.Find(part);
            if (t == null) continue;
            t.SetParent(model.transform, true);
            var r = t.GetComponent<Renderer>();
            r.sharedMaterial = part == "Pickaxe Handle" ? wood : metal;
            r.shadowCastingMode = ShadowCastingMode.Off; // first-person view models look odd casting shadows
        }
        Object.DestroyImmediate(pack);

        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        EnsureFolder();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(model, PickaxePrefabPath);
        Object.DestroyImmediate(model);
        return prefab;
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
    }
}
