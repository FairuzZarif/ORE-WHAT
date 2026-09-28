using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Builds the first-person viewmodel (rigged arms holding the pickaxe) under the player camera:
///
///   PlayerCamera                   CameraShake
///   ├── FirstPersonViewModel       ViewModelMotion    (idle / look sway / walk bob / jump / landing)
///   │   ├── ArmsModel              FirstPersonArmsIK  (WRAD arms; hands follow the grip points)
///   │   ├── LeftElbowHint / RightElbowHint            (elbows bend toward these)
///   │   └── Pickaxe                                   (mount: resting place + orientation, at the right-hand grip)
///   │       └── PickaxeRoot        PickaxeSwing       (the animated transform; local axes = pickaxe axes)
///   │           ├── PickaxeMesh                        (PickaxeModel prefab: handle +Y, pick point +Z)
///   │           ├── RightHandGrip                      (dominant hand, main grip near the end of the handle)
///   │           └── LeftHandGrip                       (supporting hand, further up the shaft)
///   └── ViewModelCamera            URP overlay camera that draws only the "ViewModel" layer
///
/// PickaxeRoot's axes are the pickaxe's own: Y up the handle, Z the way the pick point faces,
/// X the swing axis. Rotating around X swings the head in its own plane, point first, and
/// no extra rotations are ever needed to "fix" the model.
///
/// Menu: Ore What > Rebuild First-Person Viewmodel (replaces any existing viewmodel).
/// </summary>
public static class ViewModelSetupTool
{
    private const string LayerName = "ViewModel";
    private const string PickaxePrefabPath = "Assets/Prefabs/PickaxeModel.prefab";
    private const string ArmsModelPath = "Assets/arms/arms.fbx";
    private const float ViewModelFov = 65f;

    // ---- Resting pose, camera space (x right, y up, z forward), metres/degrees --------
    // Found by sweeping several thousand layouts and scoring each over the whole swing for wrist bend
    // (< ~30°), forearm twist, arm reach, hands staying on screen and off the crosshair, and the
    // pick point landing near the crosshair. The key: the forearms must cross the handle, not
    // run along it, or no wrist angle can wrap a fist around it. Tweak and re-run the menu item.
    /// <summary>Where the dominant (right) hand holds the handle.</summary>
    private static readonly Vector3 RightGrip = new Vector3(0.14f, -0.19f, 0.40f);
    /// <summary>Handle lean toward the left, from vertical (degrees).</summary>
    private const float HandleSideLean = 10f;
    /// <summary>Handle lean forward (away from the player), from vertical (degrees).</summary>
    private const float HandleForwardLean = 40f;
    /// <summary>Turn of the head around the handle. 0 = pick points straight ahead (degrees).</summary>
    private const float HeadRoll = -10f;
    /// <summary>Supporting (left) hand, metres further up the handle than the right hand.</summary>
    private const float LeftGripDistance = 0.18f;
    /// <summary>The right hand sits this far up from the bottom tip of the handle (metres).</summary>
    private const float RightGripFromButt = 0.08f;
    /// <summary>PickaxeModel's own pivot is this far up from the bottom tip of the handle (metres).</summary>
    private const float ModelPivotFromButt = 0.15f;

    // The WRAD arms are modelled ~9x real size, facing -Z, with the eye 1.5 units above the
    // shoulders. Scale 0.11 gives ~0.3 m upper arms; the position lines its eye up with the
    // camera and puts the (off-screen) shoulders a little forward so the hands keep their reach.
    private const float ArmsModelScale = 0.11f;
    private static readonly Vector3 ArmsModelPosition = new Vector3(0f, -0.166f, 0.1f);
    // Elbows low, so both forearms come up from under the shaft: the palms wrap it from
    // underneath (a real two-handed grip) with straight wrists, and nothing crowds the lens.
    // Found by sweeping hint positions over every swing pose (wrist bend/twist, reach, palm
    // direction, distance from the camera).
    private static readonly Vector3 LeftElbowHint = new Vector3(-0.8f, -0.6f, -0.2f);
    // Right elbow out to the right at about grip height: the forearm comes in from the right, so
    // the palm can face the player with the least wrist bend (swept: 31° bend at rest).
    private static readonly Vector3 RightElbowHint = new Vector3(0.6f, -0.2f, 0.1f);
    /// <summary>The right hand sits this much further down the shaft than the pickaxe's pivot (metres).</summary>
    private const float RightHandLower = 0.025f;

    [MenuItem("Ore What/Rebuild First-Person Viewmodel")]
    public static void RebuildMenu()
    {
        GameObject player = GameObject.FindWithTag("Player");
        Camera cam = player != null ? player.GetComponentInChildren<Camera>() : null;
        if (cam == null)
        {
            Debug.LogError("[Ore What] Need an object tagged 'Player' with a child Camera.");
            return;
        }
        Build(player, cam);
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
        Debug.Log("[Ore What] First-person viewmodel rebuilt.");
    }

    /// <summary>The pickaxe's resting orientation in camera space: up = handle, forward = pick point.</summary>
    public static Quaternion RestingPickaxeRotation()
    {
        float side = HandleSideLean * Mathf.Deg2Rad, fwd = HandleForwardLean * Mathf.Deg2Rad;
        Vector3 handle = (-Mathf.Sin(side) * Vector3.right
                          + Mathf.Cos(side) * (Mathf.Cos(fwd) * Vector3.up + Mathf.Sin(fwd) * Vector3.forward)).normalized;
        // Pick point faces forward (perpendicular to the handle), turned by HeadRoll. The swing
        // axis (PickaxeRoot X) is then perpendicular to both, so the head swings point-first.
        Vector3 pick = Quaternion.AngleAxis(HeadRoll, handle) * Vector3.ProjectOnPlane(Vector3.forward, handle).normalized;
        return Quaternion.LookRotation(pick, handle);
    }

    /// <summary>Builds (or rebuilds) the viewmodel and wires it to MiningController.</summary>
    public static PickaxeSwing Build(GameObject player, Camera cam)
    {
        RemoveOld(cam);
        int layer = EnsureLayer();

        var root = new GameObject("FirstPersonViewModel").transform;
        root.SetParent(cam.transform, false);

        // --- Pickaxe ------------------------------------------------------------------
        var mount = new GameObject("Pickaxe").transform;
        mount.SetParent(root, false);
        mount.localPosition = RightGrip;
        mount.localRotation = RestingPickaxeRotation();

        var pickaxeRoot = new GameObject("PickaxeRoot").transform;
        pickaxeRoot.SetParent(mount, false);

        var pickaxePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PickaxePrefabPath);
        if (pickaxePrefab != null)
        {
            var mesh = (GameObject)PrefabUtility.InstantiatePrefab(pickaxePrefab, pickaxeRoot);
            mesh.name = "PickaxeMesh";
            mesh.transform.localPosition = new Vector3(0f, ModelPivotFromButt - RightGripFromButt, 0f);
            mesh.transform.localRotation = Quaternion.identity; // prefab is already handle +Y, pick +Z
        }
        else
            Debug.LogWarning($"[Ore What] {PickaxePrefabPath} not found. The hands will grip an invisible handle, but mining still works.");

        Transform rightGrip = CreatePoint("RightHandGrip", pickaxeRoot, new Vector3(0f, -RightHandLower, 0f), Quaternion.identity);
        Transform leftGrip = CreatePoint("LeftHandGrip", pickaxeRoot, new Vector3(0f, LeftGripDistance, 0f), Quaternion.identity);

        // --- Rigged arms -------------------------------------------------------------
        FirstPersonArmsIK ik = CreateArmsModel(root, leftGrip, rightGrip);

        // Viewmodels are purely visual: no colliders (never block the mining ray or get
        // pushed by physics), no shadows, and on their own layer.
        foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(c);
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        if (layer >= 0)
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = layer;

        // --- Scripts ---------------------------------------------------------------
        var shake = cam.GetComponent<CameraShake>();
        if (shake == null) shake = Undo.AddComponent<CameraShake>(cam.gameObject);
        if (player.GetComponent<PlayerMotionState>() == null) Undo.AddComponent<PlayerMotionState>(player);

        root.gameObject.AddComponent<ViewModelMotion>();
        var swing = pickaxeRoot.gameObject.AddComponent<PickaxeSwing>();
        SetRef(swing, "cameraShake", shake);

        var mining = player.GetComponent<MiningController>();
        if (mining != null) SetRef(mining, "pickaxeSwing", swing);

        if (layer >= 0) SetupOverlayCamera(cam, layer);

        // Work out the natural hand rotations once, then pose the arms for the scene view.
        if (ik != null)
        {
            ik.CalibrateGrips();
            EditorUtility.SetDirty(ik);
        }

        Undo.RegisterCreatedObjectUndo(root.gameObject, "Build Viewmodel");
        ItemSetupTool.LinkPickaxeView(player); // PlayerEquipment shows/hides this new viewmodel
        return swing;
    }

    private static Transform CreatePoint(string name, Transform parent, Vector3 localPos, Quaternion localRot)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        t.localRotation = localRot;
        return t;
    }

    /// <summary>Instantiates the WRAD arms, adds FirstPersonArmsIK and wires every bone.</summary>
    private static FirstPersonArmsIK CreateArmsModel(Transform root, Transform leftGrip, Transform rightGrip)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArmsModelPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[Ore What] {ArmsModelPath} not found. Building without arms.");
            return null;
        }

        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
        model.name = "ArmsModel";
        model.transform.localPosition = ArmsModelPosition;
        model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // model faces -Z
        model.transform.localScale = Vector3.one * ArmsModelScale;

        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            smr.updateWhenOffscreen = true; // bounds follow the IK pose, so the arms never get culled

        Transform leftHint = CreatePoint("LeftElbowHint", root, LeftElbowHint, Quaternion.identity);
        Transform rightHint = CreatePoint("RightElbowHint", root, RightElbowHint, Quaternion.identity);

        var ik = model.AddComponent<FirstPersonArmsIK>();
        var so = new SerializedObject(ik);
        WireArm(so.FindProperty("leftArm"), model.transform, "l", leftGrip, leftHint);
        WireArm(so.FindProperty("rightArm"), model.transform, "r", rightGrip, rightHint);
        // Right (dominant, lower) hand: palm faces the player, fingers wrap the shaft from underneath
        // on the player's side, thumb points up the shaft toward the head.
        // The left hand keeps the default wrap-from-underneath grip.
        SerializedProperty right = so.FindProperty("rightArm");
        right.FindPropertyRelative("palmTowardViewer").floatValue = 1f;
        right.FindPropertyRelative("minPalmUp").floatValue = -1f;
        right.FindPropertyRelative("thumbAlongShaft").floatValue = 1f;
        right.FindPropertyRelative("thumbCurlOverride").floatValue = 5f;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Untouched wrist / twist-bone rotations, recorded before anything is posed.
        ik.CaptureRestPose();

        // Finger joints and the axis each one curls around (measured, not guessed).
        var joints = new List<Transform>();
        var axes = new List<Vector3>();
        var thumbs = new List<bool>();
        foreach (string side in new[] { "l", "r" })
        {
            Transform wrist = FindDeep(model.transform, "wrist." + side);
            Transform middleBase = FindDeep(model.transform, "finger_middle1." + side);
            foreach (string finger in new[] { "index", "middle", "ring", "pinky", "thumb" })
            {
                bool isThumb = finger == "thumb";
                for (int i = isThumb ? 2 : 1; i <= 3; i++) // thumb base stays put; only its tip joints close
                {
                    Transform joint = FindDeep(model.transform, $"finger_{finger}{i}.{side}");
                    Transform tip = FindDeep(model.transform, $"finger_{finger}3.{side}_end");
                    if (joint == null || tip == null) continue;
                    joints.Add(joint);
                    axes.Add(FindCurlAxis(joint, tip, isThumb ? middleBase : wrist));
                    thumbs.Add(isThumb);
                }
            }
        }
        ik.SetCurlJoints(joints.ToArray(), axes.ToArray(), thumbs.ToArray());
        EditorUtility.SetDirty(ik);
        return ik;
    }

    private static void WireArm(SerializedProperty arm, Transform model, string side, Transform grip, Transform hint)
    {
        arm.FindPropertyRelative("upperArm").objectReferenceValue = FindDeep(model, "bicep." + side);
        arm.FindPropertyRelative("forearm").objectReferenceValue = FindDeep(model, "forearm." + side);
        arm.FindPropertyRelative("hand").objectReferenceValue = FindDeep(model, "wrist." + side);
        arm.FindPropertyRelative("gripTarget").objectReferenceValue = grip;
        arm.FindPropertyRelative("elbowHint").objectReferenceValue = hint;
        arm.FindPropertyRelative("indexKnuckle").objectReferenceValue = FindDeep(model, "finger_index1." + side);
        arm.FindPropertyRelative("pinkyKnuckle").objectReferenceValue = FindDeep(model, "finger_pinky1." + side);

        SerializedProperty fingers = arm.FindPropertyRelative("fistFingers");
        string[] names = { "index", "middle", "ring", "pinky" };
        fingers.arraySize = names.Length;
        for (int i = 0; i < names.Length; i++)
            fingers.GetArrayElementAtIndex(i).objectReferenceValue = FindDeep(model, $"finger_{names[i]}1.{side}");

        SerializedProperty twist = arm.FindPropertyRelative("twistBones");
        twist.arraySize = 2;
        twist.GetArrayElementAtIndex(0).objectReferenceValue = FindDeep(model, "forearm.Twist0." + side);
        twist.GetArrayElementAtIndex(1).objectReferenceValue = FindDeep(model, "forearm.Twist1." + side);
    }

    /// <summary>
    /// Tries the joint's local X and Z axes in both directions and returns the one that
    /// pulls the fingertip closest to <paramref name="palm"/>, i.e. the direction it curls.
    /// </summary>
    private static Vector3 FindCurlAxis(Transform joint, Transform tip, Transform palm)
    {
        Quaternion rest = joint.localRotation;
        Vector3 best = Vector3.right;
        float bestDist = float.MaxValue;
        foreach (Vector3 axis in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            joint.localRotation = rest * Quaternion.AngleAxis(30f, axis);
            float d = Vector3.Distance(tip.position, palm.position);
            if (d < bestDist) { bestDist = d; best = axis; }
        }
        joint.localRotation = rest;
        return best;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    /// <summary>
    /// Second camera that renders only the viewmodel on top of the world
    /// (URP camera stacking), so the arms never poke through walls or rocks.
    /// </summary>
    private static void SetupOverlayCamera(Camera cam, int layer)
    {
        var go = new GameObject("ViewModelCamera");
        go.transform.SetParent(cam.transform, false);
        go.layer = layer;
        var vmCam = go.AddComponent<Camera>();
        vmCam.cullingMask = 1 << layer;
        vmCam.nearClipPlane = 0.01f;
        vmCam.farClipPlane = 5f;
        vmCam.fieldOfView = ViewModelFov;

        var vmData = vmCam.GetUniversalAdditionalCameraData();
        vmData.renderType = CameraRenderType.Overlay;
        vmData.renderShadows = false;

        var baseData = cam.GetUniversalAdditionalCameraData();
        baseData.cameraStack.RemoveAll(c => c == null);
        baseData.cameraStack.Add(vmCam);

        cam.cullingMask &= ~(1 << layer); // the main camera no longer draws the arms itself
    }

    private static void RemoveOld(Camera cam)
    {
        foreach (string child in new[] { "PickaxeVisual", "FirstPersonViewModel", "ViewModelCamera" })
        {
            Transform t = cam.transform.Find(child);
            if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        }
        cam.GetUniversalAdditionalCameraData().cameraStack.RemoveAll(c => c == null);
    }

    /// <summary>Finds or creates the "ViewModel" layer. Returns -1 if every user layer is taken.</summary>
    private static int EnsureLayer()
    {
        int existing = LayerMask.NameToLayer(LayerName);
        if (existing >= 0) return existing;

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        for (int i = 6; i < layers.arraySize; i++) // 0-5 are Unity's built-in layers
        {
            SerializedProperty entry = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(entry.stringValue))
            {
                entry.stringValue = LayerName;
                tagManager.ApplyModifiedProperties();
                return i;
            }
        }
        Debug.LogWarning("[Ore What] No free layer for the viewmodel; skipping the overlay camera.");
        return -1;
    }

    private static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
