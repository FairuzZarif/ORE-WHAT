using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Ore What > Multiplayer > Set Up Multiplayer. Safe to run again (rebuilds the network prefabs).
///
///  - Item world prefabs (Assets/Items/*.asset → World Prefab) get NetworkObject + NetworkTransform (owner
///    authority) + NetworkItem, so dropped/thrown/mined items are shared. Nothing else in them changes, and in
///    single player they behave exactly as before.
///  - Assets/Prefabs/Network/NetworkPlayer.prefab: how a player looks to others (a copy of the scene Player's
///    CharacterVisual body + headlamp light), with NetworkPlayerAvatar.
///  - Assets/Prefabs/Network/NetworkWorld.prefab: rocks + items authority (NetworkWorld), with the item list.
///  - Assets/Prefabs/Network/NetworkManager.prefab: NetworkManager + UnityTransport + NetworkSessionManager +
///    MultiplayerHUD, with the prefab list Assets/Prefabs/Network/NetworkPrefabs.asset.
///  - MainMenu: MultiplayerMenu on the MenuCanvas (Host / Join / code / status).
///  - PlayerTest is re-saved if it holds item pickups (their NetworkObjects need scene ids). If the scene has
///    unsaved changes it is NOT saved; you're asked to save it yourself.
/// </summary>
public static class MultiplayerSetupTool
{
    private const string Folder = "Assets/Prefabs/Network";
    private const string PlayerPrefabPath = Folder + "/NetworkPlayer.prefab";
    private const string WorldPrefabPath = Folder + "/NetworkWorld.prefab";
    private const string ManagerPrefabPath = Folder + "/NetworkManager.prefab";
    private const string PrefabListPath = Folder + "/NetworkPrefabs.asset";
    private const string MenuScenePath = "Assets/Scenes/MainMenu.unity";
    private const string GameScenePath = "Assets/Scenes/PlayerTest.unity";
    private const int PlayerLayer = 8;

    [MenuItem("Ore What/Multiplayer/Set Up Multiplayer")]
    public static void SetUp()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Ore What] Stop Play mode first."); return; }
        Directory.CreateDirectory(Folder);

        List<ItemData> items = LoadItems();
        var networkedItems = new List<GameObject>();
        foreach (ItemData item in items)
            if (item.WorldPrefab != null && MakeItemNetworked(item.WorldPrefab)) networkedItems.Add(item.WorldPrefab);

        GameObject world = BuildWorldPrefab(items);
        GameObject player = BuildPlayerPrefab(items);
        if (player == null) return;
        GameObject manager = BuildManagerPrefab(player, world, networkedItems);
        AddMenu(manager);
        ResaveGameScene();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Ore What] Multiplayer set up: {networkedItems.Count} shared item prefabs, player/world/manager prefabs in {Folder}.");
    }

    // ------------------------------------------------------------------ items

    private static List<ItemData> LoadItems()
    {
        var items = new List<ItemData>();
        foreach (string guid in AssetDatabase.FindAssets("t:ItemData", new[] { "Assets/Items" }))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (item != null) items.Add(item);
        }
        items.Sort((a, b) => string.CompareOrdinal(a.ItemId, b.ItemId)); // same order every time
        return items;
    }

    private static bool MakeItemNetworked(GameObject prefab)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (root.GetComponent<DroppedItem>() == null) return false;
            var netObj = GetOrAdd<NetworkObject>(root);
            netObj.DontDestroyWithOwner = true; // a player who leaves while holding it hands it back to the host
            var nt = GetOrAdd<NetworkTransform>(root);
            nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
            nt.Interpolate = true;
            nt.InLocalSpace = false;
            GetOrAdd<NetworkItem>(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return true;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // ------------------------------------------------------------------ world

    private static GameObject BuildWorldPrefab(List<ItemData> items)
    {
        var go = new GameObject("NetworkWorld");
        go.AddComponent<NetworkObject>();
        var world = go.AddComponent<NetworkWorld>();
        var so = new SerializedObject(world);
        var list = so.FindProperty("items");
        list.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, WorldPrefabPath);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ------------------------------------------------------------------ player avatar

    private static GameObject BuildPlayerPrefab(List<ItemData> items)
    {
        GameObject scenePlayer = GameObject.Find("Player");
        Transform visual = scenePlayer != null ? scenePlayer.transform.Find("CharacterVisual") : null;
        if (visual == null)
        {
            Debug.LogError($"[Ore What] Open {GameScenePath} first: the remote player's body is copied from Player/CharacterVisual.");
            return null;
        }

        // The root (with the body capsule) is on the Player layer, which every weapon / tool / punch ray leaves out: the
        // capsule only stops other players walking through. What weapons hit are the body's hitboxes (CharacterRagdoll's
        // head / chest / arm / leg colliders, switched on at runtime on the Default layer by NetworkPlayerAvatar).
        var root = new GameObject("NetworkPlayer") { layer = PlayerLayer };
        root.AddComponent<NetworkObject>();
        CompanyOfficeSetupTool.ConfigureNetworkPlayer(root);
        var nt = root.AddComponent<NetworkTransform>();
        nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner; // each player moves its own avatar
        nt.SyncRotAngleX = nt.SyncRotAngleZ = false;               // the body only turns around Y
        nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
        nt.Interpolate = true;
        nt.InLocalSpace = false;

        var controller = scenePlayer.GetComponent<CharacterController>();
        var capsule = root.AddComponent<CapsuleCollider>(); // other players bump into it
        capsule.height = controller != null ? controller.height : 1.8f;
        capsule.radius = controller != null ? controller.radius : 0.35f;
        capsule.center = controller != null ? controller.center : new Vector3(0f, 0.9f, 0f);

        // The body: the scene's CharacterVisual, minus the local-player-only parts.
        GameObject body = Object.Instantiate(visual.gameObject, root.transform);
        body.name = "CharacterVisual";
        body.transform.localPosition = visual.localPosition;
        body.transform.localRotation = visual.localRotation;
        foreach (var c in body.GetComponentsInChildren<FirstPersonBodyVisibility>(true)) Object.DestroyImmediate(c);
        foreach (var c in body.GetComponentsInChildren<CharacterAnimator>(true)) Object.DestroyImmediate(c);
        foreach (Transform t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PlayerLayer;
        foreach (var r in body.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
            r.shadowCastingMode = ShadowCastingMode.On;
        }
        Animator animator = body.GetComponentInChildren<Animator>(true);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        var crouchPose = body.GetComponentInChildren<CharacterCrouchPose>(true);
        if (crouchPose != null)
        {
            var cso = new SerializedObject(crouchPose);
            cso.FindProperty("crouch").objectReferenceValue = null; // driven by NetworkPlayerAvatar instead
            cso.ApplyModifiedPropertiesWithoutUndo();
        }

        // Look direction (pitch) + headlamp.
        var aim = new GameObject("Aim").transform;
        aim.SetParent(root.transform, false);
        Camera cam = scenePlayer.GetComponentInChildren<Camera>(true);
        aim.localPosition = cam != null ? scenePlayer.transform.InverseTransformPoint(cam.transform.position) : new Vector3(0f, 1.6f, 0f);

        Headlamp headlamp = null;
        var sceneLamp = scenePlayer.GetComponent<Headlamp>();
        if (sceneLamp != null && sceneLamp.Light != null)
        {
            GameObject light = Object.Instantiate(sceneLamp.Light.gameObject, root.transform);
            light.name = "Headlamp Light";
            headlamp = root.AddComponent<Headlamp>();
            EditorUtility.CopySerialized(sceneLamp, headlamp);
            var hso = new SerializedObject(headlamp);
            hso.FindProperty("lamp").objectReferenceValue = light.GetComponent<Light>();
            hso.FindProperty("character").objectReferenceValue = animator;
            hso.FindProperty("aimSource").objectReferenceValue = aim;
            hso.FindProperty("startOn").boolValue = false;
            hso.ApplyModifiedPropertiesWithoutUndo();
            AddHeadlampGlow(light.GetComponent<Light>());
        }

        // What others see in this player's hands: items, arm poses, actions (baked from the first-person views).
        var remote = root.AddComponent<RemotePlayerPresentation>();
        RemoteHoldPoseBaker.Bake(scenePlayer, animator, remote, items);

        var avatar = root.AddComponent<NetworkPlayerAvatar>();
        var aso = new SerializedObject(avatar);
        aso.FindProperty("body").objectReferenceValue = animator;
        aso.FindProperty("crouchPose").objectReferenceValue = crouchPose;
        aso.FindProperty("headlamp").objectReferenceValue = headlamp;
        aso.FindProperty("aim").objectReferenceValue = aim;
        aso.FindProperty("bodyCollider").objectReferenceValue = capsule;
        aso.FindProperty("presentation").objectReferenceValue = remote;
        aso.ApplyModifiedPropertiesWithoutUndo();

        // Health, damage, death (ragdoll from the body's own skeleton) and respawn.
        if (animator.GetComponent<CharacterRagdoll>() == null) animator.gameObject.AddComponent<CharacterRagdoll>();
        var health = root.AddComponent<NetworkPlayerHealth>();
        var hpso = new SerializedObject(health);
        hpso.FindProperty("avatar").objectReferenceValue = avatar;
        hpso.FindProperty("bloodEffect").objectReferenceValue = CombatSetupTool.EnsureBloodEffect();
        hpso.FindProperty("bloodSplat").objectReferenceValue = CombatSetupTool.EnsureSplatMaterial();
        hpso.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>A soft glow sprite at the lens, seen by other players while the lamp is on (HeadlampGlow).</summary>
    private static void AddHeadlampGlow(Light lamp)
    {
        var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glow.name = "Headlamp Glow";
        Object.DestroyImmediate(glow.GetComponent<Collider>());
        glow.layer = PlayerLayer;
        glow.transform.SetParent(lamp.transform, false);
        glow.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        var renderer = glow.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = GlowMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var g = glow.AddComponent<HeadlampGlow>();
        var so = new SerializedObject(g);
        so.FindProperty("lamp").objectReferenceValue = lamp;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Additive glow material: a copy of the muzzle flash material (URP Particles/Unlit, additive) with a soft round texture.</summary>
    private static Material GlowMaterial()
    {
        const string matPath = Folder + "/HeadlampGlow.mat", texPath = Folder + "/HeadlampGlow.png";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) == null)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n, n) * 0.5f) / (n * 0.5f);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
                    tex.SetPixel(x, y, new Color(a, a, a, a));
                }
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Weapons/MuzzleFlash.mat");
            mat = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        mat.SetTexture("_BaseMap", texture);
        mat.mainTexture = texture;
        mat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ------------------------------------------------------------------ network manager

    private static GameObject BuildManagerPrefab(GameObject player, GameObject world, List<GameObject> items)
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
        if (list == null)
        {
            list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            AssetDatabase.CreateAsset(list, PrefabListPath);
        }
        var wanted = new List<GameObject> { player, world };
        wanted.AddRange(items);
        foreach (GameObject prefab in wanted)
        {
            bool present = false;
            foreach (NetworkPrefab p in list.PrefabList) if (p.Prefab == prefab) present = true;
            if (!present) list.Add(new NetworkPrefab { Prefab = prefab });
        }
        EditorUtility.SetDirty(list);

        var go = new GameObject("NetworkManager");
        var manager = go.AddComponent<NetworkManager>();
        var transport = go.AddComponent<UnityTransport>();
        transport.MaxConnectAttempts = 15; // ~15 s, then the join fails with a message instead of waiting a minute
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            PlayerPrefab = player,
            EnableSceneManagement = true,
            ConnectionApproval = true, // NetworkSessionManager turns players away when the game is full
            TickRate = 30,
        };
        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
        var session = go.AddComponent<NetworkSessionManager>();
        go.AddComponent<MultiplayerHUD>();
        var so = new SerializedObject(session);
        so.FindProperty("worldPrefab").objectReferenceValue = world.GetComponent<NetworkObject>();
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(go, ManagerPrefabPath);
        Object.DestroyImmediate(go);
        return prefabAsset;
    }

    // ------------------------------------------------------------------ scenes

    /// <summary>Puts MultiplayerMenu on the main menu's canvas (also called by MainMenuSetupTool when it rebuilds the menu).</summary>
    public static void AddMenu(GameObject managerPrefab)
    {
        if (managerPrefab == null) managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
        Scene menu = SceneManager.GetSceneByPath(MenuScenePath);
        bool opened = false;
        if (!menu.isLoaded) { menu = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Additive); opened = true; }

        MainMenuController controller = null;
        foreach (GameObject root in menu.GetRootGameObjects())
            if (controller == null) controller = root.GetComponentInChildren<MainMenuController>(true);
        if (controller == null) { Debug.LogError("[Ore What] MainMenu has no MainMenuController (run Ore What > Build Main Menu)."); return; }

        var mp = GetOrAdd<MultiplayerMenu>(controller.gameObject);
        var so = new SerializedObject(mp);
        so.FindProperty("playButton").objectReferenceValue = new SerializedObject(controller).FindProperty("playButton").objectReferenceValue as Button;
        so.FindProperty("networkPrefab").objectReferenceValue = managerPrefab;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(menu);
        EditorSceneManager.SaveScene(menu);
        if (opened) EditorSceneManager.CloseScene(menu, true);
    }

    /// <summary>In-scene item pickups now hold NetworkObjects, whose scene ids are written when the scene is saved.</summary>
    private static void ResaveGameScene()
    {
        Scene game = SceneManager.GetSceneByPath(GameScenePath);
        if (!game.isLoaded) return; // it'll get its ids the next time it's saved in the Editor
        if (game.isDirty)
        {
            Debug.LogWarning($"[Ore What] {GameScenePath} has unsaved changes, so it wasn't saved automatically. Save it (Ctrl+S) so its item pickups work in multiplayer.");
            return;
        }
        EditorSceneManager.MarkSceneDirty(game);
        EditorSceneManager.SaveScene(game);
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }
}
