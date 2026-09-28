using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sets up physical items and pickup. Safe to run more than once; existing item assets
/// and prefabs are kept (so your edits survive), only missing pieces are created.
/// Menu: Ore What > Add Item Pickup Setup
///
///   Layers     "DroppedItem" (items in the world) and "Player" (the player's CharacterController).
///              DroppedItem ignores Player and ViewModel collisions, so items never block
///              movement; PlayerItemPusher kicks them instead. The mining ray ignores both.
///   Assets     Assets/Items/*.asset (ItemData), Assets/Prefabs/Items/*.prefab (world objects),
///              Assets/Materials/Physics/DroppedItem.asset (bounce/friction for all items).
///   Rock       Rock.prefab's ore pieces become Copper Ore items.
///   Player     PlayerInventory, ItemPickupInteractor (E), ItemDropper (Q), PlayerItemPusher, InventoryHUD.
/// </summary>
public static class ItemSetupTool
{
    private const string ItemFolder = "Assets/Items";
    private const string ItemPrefabFolder = "Assets/Prefabs/Items";
    private const string PhysicsMaterialPath = "Assets/Materials/Physics/DroppedItem.asset";
    private const string RockPrefabPath = "Assets/Prefabs/Rock.prefab";
    public const string CopperOrePath = ItemFolder + "/CopperOre.asset";
    public const string PickaxeItemPath = ItemFolder + "/Pickaxe.asset";
    private const string PickaxeModelPath = "Assets/Prefabs/PickaxeModel.prefab";
    public const string PlayerLayerName = "Player";

    private enum Shape { Chunk, Crystal }

    private struct Def
    {
        public string file, id, name, material;
        public int value;
        public float mass;
        public Color color, emission;
        public float metallic, smoothness;
        public Shape shape;
    }

    private static readonly Def[] Items =
    {
        new Def { file = "CopperOre", id = "ore_copper",  name = "Copper Ore", material = "Ore_Copper",  value = 10, mass = 0.35f,
                  color = new Color(0.85f, 0.50f, 0.20f), metallic = 0.8f, smoothness = 0.6f, shape = Shape.Chunk },
        new Def { file = "IronOre",   id = "ore_iron",    name = "Iron Ore",   material = "Ore_Iron",    value = 20, mass = 0.5f,
                  color = new Color(0.58f, 0.52f, 0.48f), metallic = 0.7f, smoothness = 0.45f, shape = Shape.Chunk },
        new Def { file = "GoldOre",   id = "ore_gold",    name = "Gold Ore",   material = "Ore_Gold",    value = 50, mass = 0.6f,
                  color = new Color(1.00f, 0.78f, 0.22f), metallic = 0.9f, smoothness = 0.75f, shape = Shape.Chunk },
        new Def { file = "Crystal",   id = "crystal",     name = "Crystal",    material = "Ore_Crystal", value = 75, mass = 0.25f,
                  color = new Color(0.40f, 0.85f, 1.00f), metallic = 0.1f, smoothness = 0.9f, shape = Shape.Crystal,
                  emission = new Color(0.10f, 0.35f, 0.45f) },
    };

    [MenuItem("Ore What/Add Item Pickup Setup")]
    public static void AddToScene()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogError("[Ore What] No object tagged 'Player' in the open scene.");
            return;
        }
        EnsureAssets();
        AddToPlayer(player);
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
        Debug.Log("[Ore What] Item pickup set up. Break a rock, look at the ore and press E. Q drops, 1-9 selects a slot.");
    }

    /// <summary>Layers, collision rules, physics material, item assets and prefabs.</summary>
    public static void EnsureAssets()
    {
        int itemLayer = EnsureLayer(ItemDrops.LayerName);
        int playerLayer = EnsureLayer(PlayerLayerName);
        int viewModelLayer = LayerMask.NameToLayer("ViewModel");
        if (itemLayer >= 0 && playerLayer >= 0) Physics.IgnoreLayerCollision(itemLayer, playerLayer, true);
        if (itemLayer >= 0 && viewModelLayer >= 0) Physics.IgnoreLayerCollision(itemLayer, viewModelLayer, true);

        PhysicsMaterial physicsMaterial = GetOrCreatePhysicsMaterial();
        EnsureFolder("Assets", "Items");
        EnsureFolder("Assets", "Prefabs");
        EnsureFolder("Assets/Prefabs", "Items");

        foreach (Def d in Items)
            GetOrCreateItem(d, physicsMaterial, itemLayer);
        GetOrCreatePickaxeItem(physicsMaterial, itemLayer);

        // Rock pieces become Copper Ore items (unless you've already chosen something else).
        var rockPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RockPrefabPath);
        var copper = AssetDatabase.LoadAssetAtPath<ItemData>(CopperOrePath);
        if (rockPrefab != null && copper != null && rockPrefab.TryGetComponent(out RockHealth rock))
        {
            var so = new SerializedObject(rock);
            SerializedProperty ore = so.FindProperty("oreItem");
            if (ore.objectReferenceValue == null)
            {
                ore.objectReferenceValue = copper;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SavePrefabAsset(rockPrefab);
            }
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>Player layer + the inventory / pickup / drop / push / HUD components; mining ray ignores items.</summary>
    public static void AddToPlayer(GameObject player)
    {
        int itemLayer = LayerMask.NameToLayer(ItemDrops.LayerName);
        int playerLayer = LayerMask.NameToLayer(PlayerLayerName);
        int viewModelLayer = LayerMask.NameToLayer("ViewModel");
        if (playerLayer >= 0) player.layer = playerLayer; // root only: the CharacterController lives here

        int itemMask = itemLayer >= 0 ? 1 << itemLayer : 0;
        int notBlocking = itemMask | (playerLayer >= 0 ? 1 << playerLayer : 0)
                        | (viewModelLayer >= 0 ? 1 << viewModelLayer : 0) | (1 << 2); // + Ignore Raycast
        int blocking = ~notBlocking;

        if (player.GetComponent<PlayerInventory>() == null) Undo.AddComponent<PlayerInventory>(player);

        var pickup = player.GetComponent<ItemPickupInteractor>();
        if (pickup == null) pickup = Undo.AddComponent<ItemPickupInteractor>(player);
        var so = new SerializedObject(pickup);
        so.FindProperty("pickupLayer").intValue = itemMask;
        so.FindProperty("blockingLayers").intValue = blocking;
        so.ApplyModifiedProperties();

        var dropper = player.GetComponent<ItemDropper>();
        if (dropper == null) dropper = Undo.AddComponent<ItemDropper>(player);
        so = new SerializedObject(dropper);
        so.FindProperty("blockingLayers").intValue = blocking;
        so.ApplyModifiedProperties();

        var pusher = player.GetComponent<PlayerItemPusher>();
        if (pusher == null) pusher = Undo.AddComponent<PlayerItemPusher>(player);
        so = new SerializedObject(pusher);
        so.FindProperty("itemLayers").intValue = itemMask;
        so.ApplyModifiedProperties();

        if (player.GetComponent<InventoryHUD>() == null) Undo.AddComponent<InventoryHUD>(player);

        // Hands: start holding the pickaxe; its first-person view is the arms + pickaxe viewmodel.
        var equipment = player.GetComponent<PlayerEquipment>();
        if (equipment == null) equipment = Undo.AddComponent<PlayerEquipment>(player);
        so = new SerializedObject(equipment);
        so.FindProperty("blockingLayers").intValue = blocking;
        var pickaxe = AssetDatabase.LoadAssetAtPath<ItemData>(PickaxeItemPath);
        if (so.FindProperty("startingItem").objectReferenceValue == null)
            so.FindProperty("startingItem").objectReferenceValue = pickaxe;
        so.ApplyModifiedProperties();
        LinkPickaxeView(player);

        // The mining ray passes through loose items (and the player) to reach the rock behind.
        var mining = player.GetComponent<MiningController>();
        if (mining != null)
        {
            so = new SerializedObject(mining);
            SerializedProperty hitLayers = so.FindProperty("hitLayers");
            hitLayers.intValue &= ~(itemMask | (playerLayer >= 0 ? 1 << playerLayer : 0));
            so.ApplyModifiedProperties();
        }
    }

    /// <summary>
    /// Points PlayerEquipment's pickaxe entry at the first-person viewmodel (shown while held) and at
    /// the held pickaxe mesh (where a throw starts). Called again after the viewmodel is rebuilt.
    /// </summary>
    public static void LinkPickaxeView(GameObject player)
    {
        var equipment = player.GetComponent<PlayerEquipment>();
        var pickaxe = AssetDatabase.LoadAssetAtPath<ItemData>(PickaxeItemPath);
        Camera cam = player.GetComponentInChildren<Camera>(true);
        Transform viewModel = cam != null ? cam.transform.Find("FirstPersonViewModel") : null;
        if (equipment == null || pickaxe == null || viewModel == null) return;
        PickaxeSwing swing = viewModel.GetComponentInChildren<PickaxeSwing>(true);
        Transform mesh = swing != null ? swing.transform.Find("PickaxeMesh") : null;

        var so = new SerializedObject(equipment);
        SerializedProperty views = so.FindProperty("views");
        int index = -1;
        for (int i = 0; i < views.arraySize; i++)
            if (views.GetArrayElementAtIndex(i).FindPropertyRelative("item").objectReferenceValue == pickaxe) index = i;
        if (index < 0)
        {
            index = views.arraySize;
            views.InsertArrayElementAtIndex(index);
        }
        SerializedProperty entry = views.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("item").objectReferenceValue = pickaxe;
        entry.FindPropertyRelative("view").objectReferenceValue = viewModel.gameObject;
        entry.FindPropertyRelative("throwFrom").objectReferenceValue = mesh;
        so.ApplyModifiedProperties();
    }

    /// <summary>The pickaxe as an item: a Tool that can mine, whose world prefab is the same model the hands hold.</summary>
    private static void GetOrCreatePickaxeItem(PhysicsMaterial physicsMaterial, int layer)
    {
        var data = AssetDatabase.LoadAssetAtPath<ItemData>(PickaxeItemPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(data, PickaxeItemPath);
            var so = new SerializedObject(data);
            so.FindProperty("category").enumValueIndex = (int)ItemData.ItemCategory.Tool;
            so.FindProperty("canMine").boolValue = true;
            so.FindProperty("itemId").stringValue = "tool_pickaxe";
            so.FindProperty("displayName").stringValue = "Pickaxe";
            so.FindProperty("value").intValue = 100;
            so.FindProperty("stackable").boolValue = false;
            so.FindProperty("maxStackSize").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        string prefabPath = $"{ItemPrefabFolder}/Pickaxe.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(PickaxeModelPath);
        if (prefab == null && model != null)
        {
            var root = Object.Instantiate(model);
            root.name = "Pickaxe";
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On; // the model prefab is set up for first person (no shadows)
                r.receiveShadows = true;
                var box = r.gameObject.AddComponent<BoxCollider>(); // sizes itself to the mesh
                box.sharedMaterial = physicsMaterial;
            }
            var item = root.AddComponent<DroppedItem>();
            var body = root.GetComponent<Rigidbody>();
            body.mass = 1.5f;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.2f; // keeps tumbling in the air, still settles on the ground
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var iso = new SerializedObject(item);
            iso.FindProperty("item").objectReferenceValue = data;
            iso.ApplyModifiedPropertiesWithoutUndo();
            if (layer >= 0)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = layer;
            prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }

        var dso = new SerializedObject(data);
        if (dso.FindProperty("worldPrefab").objectReferenceValue == null && prefab != null)
        {
            dso.FindProperty("worldPrefab").objectReferenceValue = prefab;
            dso.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(data);
    }

    private static ItemData GetOrCreateItem(Def d, PhysicsMaterial physicsMaterial, int layer)
    {
        string dataPath = $"{ItemFolder}/{d.file}.asset";
        var data = AssetDatabase.LoadAssetAtPath<ItemData>(dataPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(data, dataPath);
            var so = new SerializedObject(data);
            so.FindProperty("itemId").stringValue = d.id;
            so.FindProperty("displayName").stringValue = d.name;
            so.FindProperty("value").intValue = d.value;
            so.FindProperty("stackable").boolValue = true;
            so.FindProperty("maxStackSize").intValue = 50;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        string prefabPath = $"{ItemPrefabFolder}/{d.file}.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
            prefab = CreateItemPrefab(d, data, prefabPath, physicsMaterial, layer);

        var dso = new SerializedObject(data);
        SerializedProperty world = dso.FindProperty("worldPrefab");
        if (world.objectReferenceValue == null)
        {
            world.objectReferenceValue = prefab;
            dso.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(data);
        return data;
    }

    /// <summary>A small ore chunk (or crystal cluster) built from cubes, with physics ready to go.</summary>
    private static GameObject CreateItemPrefab(Def d, ItemData data, string path, PhysicsMaterial physicsMaterial, int layer)
    {
        Material mat = PrototypeSceneBuilder.GetOrCreateMaterial(d.material, d.color);
        mat.SetFloat("_Metallic", d.metallic);
        mat.SetFloat("_Smoothness", d.smoothness);
        if (d.emission.maxColorComponent > 0f)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", d.emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(mat);

        var root = new GameObject(d.file);
        if (d.shape == Shape.Crystal)
        {
            AddPart(root, "Shard_A", new Vector3(0f, 0.05f, 0f), new Vector3(8f, 0f, 6f), new Vector3(0.07f, 0.2f, 0.07f), mat, physicsMaterial);
            AddPart(root, "Shard_B", new Vector3(0.05f, 0.02f, 0.02f), new Vector3(-10f, 0f, -35f), new Vector3(0.05f, 0.14f, 0.05f), mat, physicsMaterial);
            AddPart(root, "Shard_C", new Vector3(-0.045f, 0.015f, -0.02f), new Vector3(15f, 20f, 30f), new Vector3(0.045f, 0.11f, 0.045f), mat, physicsMaterial);
        }
        else
        {
            AddPart(root, "Chunk", Vector3.zero, new Vector3(12f, 25f, 8f), new Vector3(0.17f, 0.13f, 0.15f), mat, physicsMaterial);
            AddPart(root, "Lump", new Vector3(0.05f, 0.045f, 0.02f), new Vector3(35f, 40f, 15f), new Vector3(0.1f, 0.09f, 0.1f), mat, physicsMaterial);
        }

        var item = root.AddComponent<DroppedItem>(); // adds the Rigidbody too
        var body = root.GetComponent<Rigidbody>();
        body.mass = d.mass;
        body.linearDamping = 0.05f;
        body.angularDamping = 0.35f; // stops rolling so pieces settle
        body.interpolation = RigidbodyInterpolation.Interpolate; // smooth up close to the camera
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // small + fast, cheap anti-tunnelling

        var so = new SerializedObject(item);
        so.FindProperty("item").objectReferenceValue = data;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (layer >= 0)
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void AddPart(GameObject root, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material mat, PhysicsMaterial pm)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube); // comes with a BoxCollider
        part.name = name;
        part.transform.SetParent(root.transform, false);
        part.transform.localPosition = pos;
        part.transform.localRotation = Quaternion.Euler(euler);
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = mat;
        part.GetComponent<Collider>().sharedMaterial = pm;
    }

    /// <summary>A little bounce, decent grip: items hop once or twice, tumble, then settle.</summary>
    private static PhysicsMaterial GetOrCreatePhysicsMaterial()
    {
        var pm = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysicsMaterialPath);
        if (pm != null) return pm;
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets/Materials", "Physics");
        pm = new PhysicsMaterial("DroppedItem")
        {
            bounciness = 0.25f,
            bounceCombine = PhysicsMaterialCombine.Maximum, // the floor has no bounce of its own
            dynamicFriction = 0.55f,
            staticFriction = 0.65f,
            frictionCombine = PhysicsMaterialCombine.Average,
        };
        AssetDatabase.CreateAsset(pm, PhysicsMaterialPath);
        return pm;
    }

    /// <summary>Finds or creates a user layer. Returns -1 if every user layer is taken.</summary>
    private static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0) return existing;

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        for (int i = 6; i < layers.arraySize; i++) // 0-5 are Unity's built-in layers
        {
            SerializedProperty entry = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(entry.stringValue)) continue;
            entry.stringValue = layerName;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            return i;
        }
        Debug.LogWarning($"[Ore What] No free layer for '{layerName}'.");
        return -1;
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            AssetDatabase.CreateFolder(parent, name);
    }
}
