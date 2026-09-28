using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Sets up the 7-slot hotbar and holding any item in the first-person hands. Safe to run again
/// (the held-item view is rebuilt; icons are only generated for items that have none).
/// Menu: Ore What > Add Hotbar And Held Items
///
///   Inventory        7 slots (PlayerInventory.Slot Count)
///   Hotbar           HotbarUI on the Player (builds its own canvas at runtime)
///   Held-item view   PlayerCamera/ItemHoldViewModel: a copy of FirstPersonViewModel (the same arms,
///                    IK and hand tuning) with the pickaxe removed and both hands re-gripped to clasp an
///                    item from either side (HeldResourceView). Used for ores and any item without a tool
///                    view, and for carried world ores (it becomes the OreCarryController's carry point).
///   Icons            Assets/Items/Icons/*.png rendered from each item's world prefab, assigned to ItemData.Icon
/// Menu: Ore What > Regenerate Item Icons re-renders every icon.
/// </summary>
public static class HotbarSetupTool
{
    private const string ViewName = "ItemHoldViewModel";
    private const string IconFolder = "Assets/Items/Icons";
    private const int InventorySlots = 7;

    // Where the held item sits, camera space (x right, y up, z forward), metres: low and central,
    // like carrying a rock in front of your chest.
    private static readonly Vector3 HoldPosition = new Vector3(0.02f, -0.15f, 0.42f);
    private static readonly Vector3 HoldTilt = Vector3.zero;
    // Open hands pressed on the item's sides (a tool grip is a tight fist around a handle).
    private const float HoldFingerCurl = 30f;
    private const float HoldThumbCurl = 10f;

    [MenuItem("Ore What/Add Hotbar And Held Items")]
    public static void AddMenu()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("[Ore What] No object tagged 'Player' in the open scene."); return; }
        if (Add(player))
        {
            EditorSceneManager.MarkSceneDirty(player.scene);
            EditorSceneManager.SaveScene(player.scene);
            Debug.Log("[Ore What] Hotbar ready: 1-7 select a slot, the selected item shows in your hands.");
        }
    }

    [MenuItem("Ore What/Regenerate Item Icons")]
    public static void RegenerateIconsMenu() => GenerateIcons(overwrite: true);

    public static bool Add(GameObject player)
    {
        var inventory = player.GetComponent<PlayerInventory>();
        var equipment = player.GetComponent<PlayerEquipment>();
        if (inventory == null || equipment == null)
        {
            Debug.LogError("[Ore What] Run Ore What > Add Item Pickup Setup first.");
            return false;
        }

        var so = new SerializedObject(inventory);
        so.FindProperty("slotCount").intValue = InventorySlots;
        so.ApplyModifiedProperties();

        Camera cam = player.GetComponentInChildren<Camera>(true);
        Transform pickaxeView = cam != null ? cam.transform.Find("FirstPersonViewModel") : null;
        if (pickaxeView == null)
        {
            Debug.LogError("[Ore What] No FirstPersonViewModel under the camera. Run Ore What > Rebuild First-Person Viewmodel first.");
            return false;
        }
        HeldResourceView view = BuildItemView(cam.transform, pickaxeView);

        so = new SerializedObject(equipment);
        so.FindProperty("itemView").objectReferenceValue = view;
        so.ApplyModifiedProperties();

        // Carried world ores are held in the same place, between the hands.
        var carrier = player.GetComponent<OreCarryController>();
        if (carrier != null)
        {
            so = new SerializedObject(carrier);
            so.FindProperty("carryPoint").objectReferenceValue = view.Holder;
            so.FindProperty("minHorizontalDistance").floatValue = 0.35f; // the hands are closer than the old carry point
            so.FindProperty("springStrength").floatValue = 160f;         // held tighter: it's in your hands now
            so.FindProperty("maxAcceleration").floatValue = 150f;
            so.ApplyModifiedProperties();
        }

        if (player.GetComponent<HotbarUI>() == null) Undo.AddComponent<HotbarUI>(player);
        GenerateIcons(overwrite: false);
        return true;
    }

    /// <summary>The pickaxe view's arms, without the pickaxe, clasping an item from both sides.</summary>
    private static HeldResourceView BuildItemView(Transform cam, Transform pickaxeView)
    {
        Transform old = cam.Find(ViewName);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        GameObject view = Object.Instantiate(pickaxeView.gameObject, cam);
        view.name = ViewName;
        view.transform.localPosition = pickaxeView.localPosition;
        view.transform.localRotation = pickaxeView.localRotation;
        view.SetActive(true);
        Undo.RegisterCreatedObjectUndo(view, "Add Held Item View");

        // Move the two grip points onto a new holder, then drop the pickaxe (mesh, swing, mount).
        PickaxeSwing swing = view.GetComponentInChildren<PickaxeSwing>(true);
        Transform right = swing.transform.Find("RightHandGrip");
        Transform left = swing.transform.Find("LeftHandGrip");
        var holder = new GameObject("ItemHolder").transform;
        holder.SetParent(view.transform, false);
        holder.localPosition = HoldPosition;
        holder.localRotation = Quaternion.Euler(HoldTilt);
        holder.gameObject.layer = view.layer;
        right.SetParent(holder, false);
        left.SetParent(holder, false);
        // Each hand closes around an upright "bar" at the item's side (grip up = the bar), palms inward.
        right.localPosition = new Vector3(0.1f, -0.02f, 0f);
        left.localPosition = new Vector3(-0.1f, -0.02f, 0f);
        right.localRotation = left.localRotation = Quaternion.identity;
        Object.DestroyImmediate(swing.transform.parent.gameObject);
        var miningTool = view.GetComponent<MiningToolController>();
        if (miningTool != null) Object.DestroyImmediate(miningTool);

        // Re-grip: the back of each hand on the outside, open hands, both palms against the item.
        var ik = view.GetComponentInChildren<FirstPersonArmsIK>(true);
        var iso = new SerializedObject(ik);
        iso.FindProperty("fingerCurl").floatValue = HoldFingerCurl;
        SetArm(iso.FindProperty("rightArm"), new Vector3(1f, 0f, 0f));
        SetArm(iso.FindProperty("leftArm"), new Vector3(-1f, 0f, 0f));
        iso.ApplyModifiedPropertiesWithoutUndo();
        ik.CalibrateGrips();

        var held = view.GetComponent<HeldResourceView>();
        if (held == null) held = view.AddComponent<HeldResourceView>();
        var hso = new SerializedObject(held);
        hso.FindProperty("holder").objectReferenceValue = holder;
        hso.FindProperty("rightGrip").objectReferenceValue = right;
        hso.FindProperty("leftGrip").objectReferenceValue = left;
        hso.ApplyModifiedPropertiesWithoutUndo();

        view.SetActive(false); // PlayerEquipment shows it when an item/carried ore needs it
        return held;
    }

    private static void SetArm(SerializedProperty arm, Vector3 handSide)
    {
        arm.FindPropertyRelative("preferredHandSide").vector3Value = handSide;
        arm.FindPropertyRelative("handSideWeight").floatValue = 1f;
        arm.FindPropertyRelative("palmTowardViewer").floatValue = 0f;
        arm.FindPropertyRelative("minPalmUp").floatValue = -1f;
        arm.FindPropertyRelative("thumbAlongShaft").floatValue = 0f;
        arm.FindPropertyRelative("thumbCurlOverride").floatValue = HoldThumbCurl;
    }

    /// <summary>Renders a 128x128 icon of each item's world prefab and assigns it to ItemData.Icon.</summary>
    public static void GenerateIcons(bool overwrite)
    {
        if (EditorApplication.isPlaying) return;
        if (!AssetDatabase.IsValidFolder(IconFolder)) AssetDatabase.CreateFolder("Assets/Items", "Icons");

        foreach (string guid in AssetDatabase.FindAssets("t:ItemData"))
        {
            var item = AssetDatabase.LoadAssetAtPath<ItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (item == null || item.WorldPrefab == null || (item.Icon != null && !overwrite)) continue;

            string path = $"{IconFolder}/{item.name}.png";
            File.WriteAllBytes(path, RenderIcon(item.WorldPrefab, 128));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            var so = new SerializedObject(item);
            so.FindProperty("icon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(item);
        }
        AssetDatabase.SaveAssets();
    }

    /// <summary>A transparent-background, three-quarter view of the prefab, framed to fit.</summary>
    private static byte[] RenderIcon(GameObject prefab, int size)
    {
        var go = Object.Instantiate(prefab);
        go.transform.SetPositionAndRotation(new Vector3(0f, -500f, 0f), Quaternion.identity);
        var bounds = new Bounds(go.transform.position, Vector3.zero);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);

        var camGO = new GameObject("IconCamera");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        cam.fieldOfView = 20f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 50f;
        Vector3 dir = new Vector3(-0.8f, 0.7f, -1f).normalized;
        float radius = bounds.extents.magnitude;
        float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
        cam.transform.position = bounds.center + dir * distance;
        cam.transform.LookAt(bounds.center);

        var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply();
        byte[] png = tex.EncodeToPNG();

        cam.targetTexture = null;
        RenderTexture.active = null;
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camGO);
        Object.DestroyImmediate(go);
        return png;
    }
}
