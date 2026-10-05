using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Adds the Hammer from the Simple Caving Pack as a second mining tool. Safe to run again
/// (the hammer view is rebuilt; the item, prefabs and scene pickup are kept if they exist).
/// Menu: Ore What > Add Hammer
///
///   Assets/Prefabs/HammerModel.prefab   the hammer mesh (pivot at the grip, handle +Y, head/face along Z,
///                                       the same layout as PickaxeModel)
///   Assets/Items/Hammer.asset           ItemData: Tool, Can Mine, not stackable
///   Assets/Prefabs/Items/Hammer.prefab  world object: Rigidbody + colliders + DroppedItem (throwable)
///   PlayerCamera/HammerViewModel        its first-person view: a copy of FirstPersonViewModel (the same
///                                       arms, IK and your hand tuning) holding the hammer, with its own
///                                       swing (slower, heavier) and a MiningToolController (2 damage)
///   Scene                               a Hammer lying near the player's start, to pick up with E
/// </summary>
public static class HammerSetupTool
{
    private const string BlendPath = "Assets/Simple Caving Pack/SimpleCavingPack.blend";
    private const string HammerModelPath = "Assets/Prefabs/HammerModel.prefab";
    private const string HammerItemPath = "Assets/Items/Hammer.asset";
    private const string HammerWorldPath = "Assets/Prefabs/Items/Hammer.prefab";
    private const string ViewName = "HammerViewModel";
    private const string PickupName = "Hammer (pickup)";

    // What makes the hammer feel different from the pickaxe.
    private const int HammerDamage = 2;          // retained melee-controller tuning; Hammer has no CanMine capability
    private const float HammerCooldown = 0.75f;  // pickaxe: 0.6
    private const float HammerAnimationSpeed = 0.8f;

    [MenuItem("Ore What/Add Hammer")]
    public static void AddHammerMenu()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("[Ore What] No object tagged 'Player' in the open scene."); return; }
        if (AddHammer(player))
        {
            EditorSceneManager.MarkSceneDirty(player.scene);
            EditorSceneManager.SaveScene(player.scene);
            Debug.Log("[Ore What] Hammer added. Pick it up with E, then press its hotbar number to hold it.");
        }
    }

    /// <summary>Creates everything for the hammer. Returns false if something it needs is missing.</summary>
    public static bool AddHammer(GameObject player)
    {
        ItemSetupTool.EnsureAssets(); // layers, physics material, pickaxe item
        GameObject model = GetOrCreateHammerModel();
        if (model == null) return false;
        ItemData hammer = GetOrCreateHammerItem(model);

        Camera cam = player.GetComponentInChildren<Camera>(true);
        Transform pickaxeView = cam != null ? cam.transform.Find("FirstPersonViewModel") : null;
        if (pickaxeView == null)
        {
            Debug.LogError("[Ore What] No FirstPersonViewModel under the camera. Run Ore What > Rebuild First-Person Viewmodel first.");
            return false;
        }
        ItemSetupTool.LinkPickaxeView(player); // the pickaxe view gets its MiningToolController too

        GameObject view = BuildHammerView(cam.transform, pickaxeView, model, out Transform heldMesh);
        ItemSetupTool.LinkView(player, hammer, view, heldMesh);
        PlacePickup(player, hammer);
        AssetDatabase.SaveAssets();
        return true;
    }

    /// <summary>The hammer's first-person view: the pickaxe view's arms and rig, holding the hammer instead.</summary>
    private static GameObject BuildHammerView(Transform cam, Transform pickaxeView, GameObject model, out Transform heldMesh)
    {
        Transform old = cam.Find(ViewName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        // Where the pickaxe's head sits on its handle, so the hammer head can sit in the same place
        // and the tuned swing poses land it on the crosshair just the same.
        PickaxeSwing pickSwing = pickaxeView.GetComponentInChildren<PickaxeSwing>(true);
        Transform pickMesh = pickSwing.transform.Find("PickaxeMesh");
        Vector3 meshPos = pickMesh != null ? pickMesh.localPosition : Vector3.zero;
        float headY = 0.47f;
        Transform pickHead = pickMesh != null ? FindDeep(pickMesh, "Pickaxe Head Base") : null;
        if (pickHead != null && pickHead.TryGetComponent(out Renderer headRenderer))
            headY = pickSwing.transform.InverseTransformPoint(headRenderer.bounds.center).y;

        GameObject view = Object.Instantiate(pickaxeView.gameObject, cam);
        view.name = ViewName;
        view.transform.localPosition = pickaxeView.localPosition;
        view.transform.localRotation = pickaxeView.localRotation;
        Undo.RegisterCreatedObjectUndo(view, "Add Hammer View");

        PickaxeSwing swing = view.GetComponentInChildren<PickaxeSwing>(true);
        Transform root = swing.transform;
        root.name = "HammerRoot";
        root.parent.name = "Hammer";
        Transform oldMesh = root.Find("PickaxeMesh");
        if (oldMesh != null) Object.DestroyImmediate(oldMesh.gameObject);

        var mesh = (GameObject)PrefabUtility.InstantiatePrefab(model, root);
        mesh.name = "HammerMesh";
        mesh.transform.localPosition = Vector3.zero;
        mesh.transform.localRotation = Quaternion.identity;
        float hammerHeadY = 0.68f;
        Transform head = FindDeep(mesh.transform, "Hammer Head");
        if (head != null && head.TryGetComponent(out Renderer hr))
            hammerHeadY = mesh.transform.InverseTransformPoint(hr.bounds.center).y;
        mesh.transform.localPosition = new Vector3(meshPos.x, headY - hammerHeadY, meshPos.z);

        int layer = LayerMask.NameToLayer("ViewModel");
        foreach (Transform t in mesh.GetComponentsInChildren<Transform>(true))
        {
            if (layer >= 0) t.gameObject.layer = layer;
            foreach (Collider c in t.GetComponents<Collider>()) Object.DestroyImmediate(c);
            if (t.TryGetComponent(out Renderer r)) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        }

        // Heavier than the pickaxe: slower swings that stop dead on the rock and shake harder.
        var so = new SerializedObject(swing);
        so.FindProperty("animationSpeed").floatValue = HammerAnimationSpeed;
        so.FindProperty("impactSpeedKept").floatValue = 0.05f;
        so.FindProperty("impactRecoil").floatValue = 4.5f;
        so.FindProperty("impactVibration").floatValue = 1.8f;
        so.FindProperty("followThroughStrength").floatValue = 0.3f;
        so.FindProperty("cameraShakeAmount").floatValue = 0.35f;
        so.FindProperty("cameraMotion").floatValue = 1.3f;
        so.ApplyModifiedPropertiesWithoutUndo();

        ItemSetupTool.EnsureMiningTool(view, swing, HammerDamage, HammerCooldown);
        view.SetActive(false); // PlayerEquipment shows it while the hammer is held
        heldMesh = mesh.transform;
        return view;
    }

    /// <summary>Hammer Handle + Hammer Head from the .blend, laid out like PickaxeModel.</summary>
    private static GameObject GetOrCreateHammerModel()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(HammerModelPath);
        if (existing != null) return existing;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(BlendPath);
        if (source == null)
        {
            Debug.LogError($"[Ore What] {BlendPath} not found (it needs Blender installed to import).");
            return null;
        }

        Material wood = PrototypeSceneBuilder.GetOrCreateMaterial("Pickaxe_Handle", new Color(0.55f, 0.36f, 0.20f));
        Material steel = PrototypeSceneBuilder.GetOrCreateMaterial("Hammer_Head", new Color(0.33f, 0.34f, 0.37f));
        steel.SetFloat("_Metallic", 0.8f);
        steel.SetFloat("_Smoothness", 0.45f);
        EditorUtility.SetDirty(steel);

        GameObject pack = Object.Instantiate(source);
        Transform handle = pack.transform.Find("Hammer Handle");
        Transform headPart = pack.transform.Find("Hammer Head");
        if (handle == null || headPart == null)
        {
            Object.DestroyImmediate(pack);
            Debug.LogError("[Ore What] 'Hammer Handle' / 'Hammer Head' not found in the .blend.");
            return null;
        }

        // Pivot 0.15 m above the bottom of the handle (like PickaxeModel). Rotating 90° around Y
        // turns the head (which spans X in the .blend) to face along Z, the swing direction.
        float bottom = handle.GetComponent<Renderer>().bounds.min.y;
        var model = new GameObject("HammerModel");
        model.transform.SetPositionAndRotation(new Vector3(handle.position.x, bottom + 0.15f, handle.position.z), Quaternion.Euler(0f, 90f, 0f));
        foreach (Transform part in new[] { handle, headPart })
        {
            part.SetParent(model.transform, true);
            var r = part.GetComponent<Renderer>();
            r.sharedMaterial = part == handle ? wood : steel;
            r.shadowCastingMode = ShadowCastingMode.Off; // first-person use; the world prefab turns shadows back on
        }
        Object.DestroyImmediate(pack);
        model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        ItemSetupTool.EnsureFolder("Assets", "Prefabs");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(model, HammerModelPath);
        Object.DestroyImmediate(model);
        return prefab;
    }

    private static ItemData GetOrCreateHammerItem(GameObject model)
    {
        var data = AssetDatabase.LoadAssetAtPath<ItemData>(HammerItemPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(data, HammerItemPath);
            var so = new SerializedObject(data);
            so.FindProperty("category").enumValueIndex = (int)ItemData.ItemCategory.Tool;
            so.FindProperty("canMine").boolValue = false;
            so.FindProperty("itemId").stringValue = "tool_hammer";
            so.FindProperty("displayName").stringValue = "Hammer";
            so.FindProperty("description").stringValue = "A heavy melee weapon. Cannot mine resource nodes.";
            so.FindProperty("value").intValue = 120;
            so.FindProperty("stackable").boolValue = false;
            so.FindProperty("maxStackSize").intValue = 1;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var world = AssetDatabase.LoadAssetAtPath<GameObject>(HammerWorldPath);
        if (world == null)
        {
            PhysicsMaterial pm = ItemSetupTool.GetOrCreatePhysicsMaterial();
            var root = Object.Instantiate(model);
            root.name = "Hammer";
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                r.gameObject.AddComponent<BoxCollider>().sharedMaterial = pm; // sizes itself to the mesh
            }
            var item = root.AddComponent<DroppedItem>();
            var body = root.GetComponent<Rigidbody>();
            body.mass = 2.5f;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var iso = new SerializedObject(item);
            iso.FindProperty("item").objectReferenceValue = data;
            iso.ApplyModifiedPropertiesWithoutUndo();
            int layer = LayerMask.NameToLayer(ItemDrops.LayerName);
            if (layer >= 0)
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = layer;
            ItemSetupTool.EnsureFolder("Assets/Prefabs", "Items");
            world = PrefabUtility.SaveAsPrefabAsset(root, HammerWorldPath);
            Object.DestroyImmediate(root);
        }

        var dso = new SerializedObject(data);
        if (dso.FindProperty("worldPrefab").objectReferenceValue == null)
        {
            dso.FindProperty("worldPrefab").objectReferenceValue = world;
            dso.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(data);
        return data;
    }

    /// <summary>A hammer lying on the ground a few steps in front of where the player starts.</summary>
    private static void PlacePickup(GameObject player, ItemData hammer)
    {
        if (GameObject.Find(PickupName) != null || hammer.WorldPrefab == null) return;
        Vector3 forward = player.transform.forward; forward.y = 0f; forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 spot = player.transform.position + forward * 2.5f + right * 0.8f;
        float y = player.transform.position.y;
        if (Physics.Raycast(spot + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 10f, 1, QueryTriggerInteraction.Ignore))
            y = hit.point.y;
        var pickup = (GameObject)PrefabUtility.InstantiatePrefab(hammer.WorldPrefab);
        pickup.name = PickupName;
        pickup.transform.SetPositionAndRotation(new Vector3(spot.x, y + 0.08f, spot.z),
                                                Quaternion.LookRotation(forward) * Quaternion.Euler(0f, 30f, 90f));
        Undo.RegisterCreatedObjectUndo(pickup, "Place Hammer");
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}
