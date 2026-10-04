using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds the unarmed punch: PlayerCamera/FistsViewModel, a copy of ItemHoldViewModel (so it already has
/// the character's own arms wired and calibrated) without the held-item parts, fist hand poses, a
/// FistsController, and UnarmedAttack on the Player. Empty hotbar slots show nothing; the view is only
/// shown while a punch plays. Safe to run again (rebuilds the view).
/// Menu: Ore What > Add Fists (also called by MiningSetupTool after the other views exist).
/// </summary>
public static class FistsSetupTool
{
    // Guard positions of the grip points (view space): fists low in front, the right one a bit back.
    private static readonly Vector3 RightGuard = new Vector3(0.12f, -0.15f, 0.36f);
    private static readonly Vector3 LeftGuard = new Vector3(-0.14f, -0.13f, 0.40f);
    // The held-ore hands hold their palms facing each other (a vertical fist); rolled this far palm-down.
    private const float FistRoll = 40f;
    // Elbows down and back: the held-ore hints flare the elbows out and up, which swings the sleeve into the lens on a punch.
    private static readonly Vector3 RightElbowHint = new Vector3(0.35f, -0.8f, -0.25f);
    private static readonly Vector3 LeftElbowHint = new Vector3(-0.35f, -0.8f, -0.25f);

    [MenuItem("Ore What/Add Fists")]
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
        Camera cam = player.GetComponentInChildren<Camera>(true);
        Transform source = cam != null ? cam.transform.Find("ItemHoldViewModel") : null;
        if (source == null) { Debug.LogWarning("[Ore What] No ItemHoldViewModel to build the fists from (run Add Hotbar And Held Items first)."); return; }

        Transform old = cam.transform.Find("FistsViewModel");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        GameObject view = Object.Instantiate(source.gameObject, cam.transform);
        view.name = "FistsViewModel";
        view.SetActive(false);
        Undo.RegisterCreatedObjectUndo(view, "Add Fists");
        view.transform.SetSiblingIndex(source.GetSiblingIndex() + 1);

        // No held item: keep the two grip points (as fists), drop the holder and the item view.
        Object.DestroyImmediate(view.GetComponent<HeldResourceView>());
        Transform holder = view.transform.Find("ItemHolder");
        Transform right = holder.Find("RightHandGrip"), left = holder.Find("LeftHandGrip");
        right.SetParent(view.transform, true);
        left.SetParent(view.transform, true);
        Object.DestroyImmediate(holder.gameObject);
        right.localPosition = RightGuard;
        left.localPosition = LeftGuard;
        right.localRotation = Quaternion.AngleAxis(FistRoll, Vector3.forward) * right.localRotation;
        left.localRotation = Quaternion.AngleAxis(-FistRoll, Vector3.forward) * left.localRotation;
        view.transform.Find("RightElbowHint").localPosition = RightElbowHint;
        view.transform.Find("LeftElbowHint").localPosition = LeftElbowHint;

        // Fists: fingers and thumbs curled (the held-ore view opens its thumbs per arm), no open-hand pose assets.
        var ik = view.GetComponentInChildren<FirstPersonArmsIK>(true);
        var so = new SerializedObject(ik);
        so.FindProperty("fingerCurl").floatValue = 88f;
        so.FindProperty("thumbCurl").floatValue = 75f;
        foreach (string arm in new[] { "leftArm", "rightArm" })
        {
            so.FindProperty(arm + ".handPose").objectReferenceValue = null;
            so.FindProperty(arm + ".thumbCurlOverride").floatValue = 80f; // only used to place the hand; the pose below shapes it
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log("[Ore What] " + BuildFistPoses(ik));

        var fists = view.AddComponent<FistsController>();
        var fso = new SerializedObject(fists);
        fso.FindProperty("playerCamera").objectReferenceValue = cam;
        fso.FindProperty("rightGrip").objectReferenceValue = right;
        fso.FindProperty("leftGrip").objectReferenceValue = left;
        fso.FindProperty("rightGuard").vector3Value = right.localPosition;
        fso.FindProperty("leftGuard").vector3Value = left.localPosition;
        fso.FindProperty("rightGuardRotation").quaternionValue = right.localRotation;
        fso.FindProperty("leftGuardRotation").quaternionValue = left.localRotation;
        var swingClips = fso.FindProperty("swingClips");
        swingClips.arraySize = 2;
        for (int i = 0; i < swingClips.arraySize; i++)
            swingClips.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(
                $"Assets/Audio/Swings/swing_{i + 1}.mp3");
        fso.ApplyModifiedPropertiesWithoutUndo();
        // Relaxed: grips start lowered (the view is only shown while punching; hands rise from below the screen).
        right.localPosition = fso.FindProperty("rightLowered").vector3Value;
        left.localPosition = fso.FindProperty("leftLowered").vector3Value;
        if (player.GetComponent<UnarmedAttack>() == null) Undo.AddComponent<UnarmedAttack>(player);

        var equipment = player.GetComponent<PlayerEquipment>();
        if (equipment != null)
        {
            var eso = new SerializedObject(equipment);
            eso.FindProperty("fistsView").objectReferenceValue = view;
            eso.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ---- Fist hand poses ----------------------------------------------------------------------

    private const string PoseFolder = "Assets/Animations/Character/HandPoses";

    /// <summary>
    /// A real fist for each hand, saved as HandPose assets (FistsViewModel_Left/RightHand) and assigned to
    /// the fists' arms: fingers rolled tightly into the palm (base knuckle ~85°, middle joint ~105°, tip
    /// ~70°, so the middle knuckles make a flat front), and the thumb wrapped across the OUTSIDE of the
    /// index and middle fingers' middle segments, found by a small search over its three joints (thumb tip on
    /// the middle finger, thumb's middle joint on the index finger, never through the fingers).
    /// Uses the arm IK's own curl axes and rest rotations; the skeleton is restored afterwards.
    /// </summary>
    public static string BuildFistPoses(FirstPersonArmsIK ik)
    {
        var so = new SerializedObject(ik);
        SerializedProperty pJoints = so.FindProperty("curlJoints"), pRest = so.FindProperty("curlRest"), pAxes = so.FindProperty("curlAxes"), pThumb = so.FindProperty("curlIsThumb");
        int n = pJoints.arraySize;
        var joints = new Transform[n]; var rest = new Quaternion[n]; var axes = new Vector3[n]; var thumb = new bool[n];
        for (int i = 0; i < n; i++)
        {
            joints[i] = (Transform)pJoints.GetArrayElementAtIndex(i).objectReferenceValue;
            rest[i] = pRest.GetArrayElementAtIndex(i).quaternionValue;
            axes[i] = pAxes.GetArrayElementAtIndex(i).vector3Value;
            thumb[i] = pThumb.GetArrayElementAtIndex(i).boolValue;
        }

        var report = new System.Text.StringBuilder("Fist poses:");
        foreach (string side in new[] { "leftArm", "rightArm" })
        {
            var hand = (Transform)so.FindProperty(side + ".hand").objectReferenceValue;
            if (hand == null) continue;
            var saved = new System.Collections.Generic.Dictionary<Transform, Quaternion>();
            foreach (Transform t in hand.GetComponentsInChildren<Transform>(true)) saved[t] = t.localRotation;

            // Fingers: per-joint curl.
            int t2 = -1, t3 = -1;
            for (int i = 0; i < n; i++)
            {
                if (joints[i] == null || !joints[i].IsChildOf(hand)) continue;
                char digit = joints[i].name[joints[i].name.Length - 1];
                if (thumb[i]) { if (digit == '2') t2 = i; else if (digit == '3') t3 = i; continue; }
                float angle = digit == '1' ? 85f : digit == '2' ? 105f : 70f;
                if (joints[i].name.Contains("Pinky")) angle += 6f;
                joints[i].localRotation = rest[i] * Quaternion.AngleAxis(angle, axes[i]);
            }

            // The thumb's base joint isn't one of the IK's curl joints (it's aimed separately): its rest is stored on the arm.
            Transform thumb1 = null;
            foreach (Transform t in hand.GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith("Thumb1")) thumb1 = t;
            var restProp = so.FindProperty(side + ".thumbBaseRest");
            Quaternion rest1 = restProp != null && restProp.quaternionValue != new Quaternion(0f, 0f, 0f, 0f) ? restProp.quaternionValue : thumb1 != null ? thumb1.localRotation : Quaternion.identity;

            string result = "no thumb";
            if (thumb1 != null && t2 >= 0 && t3 >= 0)
                result = FitThumb(hand, thumb1, rest1, joints[t2], rest[t2], axes[t2], joints[t3], rest[t3], axes[t3]);

            string assetName = side == "leftArm" ? "FistsViewModel_LeftHand" : "FistsViewModel_RightHand";
            string path = $"{PoseFolder}/{assetName}.asset";
            var pose = AssetDatabase.LoadAssetAtPath<HandPose>(path);
            if (pose == null)
            {
                System.IO.Directory.CreateDirectory(PoseFolder);
                pose = ScriptableObject.CreateInstance<HandPose>();
                AssetDatabase.CreateAsset(pose, path);
            }
            pose.Clear();
            pose.Capture(hand);
            EditorUtility.SetDirty(pose);
            foreach (var kv in saved) kv.Key.localRotation = kv.Value; // put the skeleton back
            so.FindProperty(side + ".handPose").objectReferenceValue = pose;
            report.Append($" {side}: {result};");
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        return report.ToString();
    }

    private static string FitThumb(Transform hand, Transform b1, Quaternion rest1, Transform b2, Quaternion rest2, Vector3 axis2,
                                   Transform b3, Quaternion rest3, Vector3 axis3)
    {
        Transform Bone(string suffix)
        {
            foreach (Transform t in hand.GetComponentsInChildren<Transform>(true)) if (t.name.EndsWith(suffix)) return t;
            return null;
        }
        Transform idx1 = Bone("Index1"), idx2 = Bone("Index2"), idx3 = Bone("Index3"), mid1 = Bone("Middle1"), mid2 = Bone("Middle2"), mid3 = Bone("Middle3");
        Transform ring1 = Bone("Ring1"), pinky1 = Bone("Pinky1"), idx4 = Bone("Index4"), mid4 = Bone("Middle4");
        if (idx2 == null || mid2 == null || idx3 == null || mid3 == null) return "finger bones not found";

        Vector3 palm = (hand.position + idx1.position + mid1.position + (ring1 ? ring1.position : mid1.position) + (pinky1 ? pinky1.position : mid1.position)) / 5f;
        float r = 0.45f * Vector3.Distance(idx1.position, idx2.position); // finger radius ~ half its first segment
        Vector3 mI = (idx2.position + idx3.position) * 0.5f, mM = (mid2.position + mid3.position) * 0.5f;
        Vector3 tipTarget = mM + (mM - palm).normalized * 2f * r;   // thumb tip lies on the middle finger
        Vector3 jointTarget = mI + (mI - palm).normalized * 2f * r; // thumb's last joint on the index finger
        var fingers = new System.Collections.Generic.List<Vector3>();
        foreach (Transform t in new[] { idx1, idx2, idx3, idx4, mid1, mid2, mid3, mid4 })
            if (t != null) fingers.Add(t.position);
        for (int k = fingers.Count - 1; k > 0; k--) fingers.Add((fingers[k] + fingers[k - 1]) * 0.5f);

        Transform tipBone = b3.childCount > 0 ? b3.GetChild(0) : null;
        float clearance = 0f;
        float Cost(Vector3 e, float a2, float a3)
        {
            b1.localRotation = rest1 * Quaternion.Euler(e);
            b2.localRotation = rest2 * Quaternion.AngleAxis(a2, axis2);
            b3.localRotation = rest3 * Quaternion.AngleAxis(a3, axis3);
            Vector3 p2 = b2.position, p3 = b3.position, tip = tipBone != null ? tipBone.position : p3 + (p3 - p2);
            float cost = (tip - tipTarget).sqrMagnitude + 0.6f * (p3 - jointTarget).sqrMagnitude;
            float minD = float.MaxValue;
            for (int s = 0; s <= 8; s++)
            {
                Vector3 q = s <= 4 ? Vector3.Lerp(p2, p3, s / 4f) : Vector3.Lerp(p3, tip, (s - 4) / 4f);
                foreach (Vector3 f in fingers)
                {
                    float d = Vector3.Distance(q, f);
                    minD = Mathf.Min(minD, d);
                    if (d < 1.8f * r) cost += 20f * (1.8f * r - d) * (1.8f * r - d);
                }
            }
            clearance = minD;
            return cost;
        }

        var rnd = new System.Random(5);
        float Rand(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        Vector3 bestE = Vector3.zero; float bestA2 = 40f, bestA3 = 40f, best = Cost(bestE, bestA2, bestA3);
        for (int k = 0; k < 6000; k++)
        {
            var e = new Vector3(Rand(-90f, 90f), Rand(-90f, 90f), Rand(-90f, 90f));
            float a2 = Rand(0f, 95f), a3 = Rand(0f, 95f);
            float c = Cost(e, a2, a3);
            if (c < best) { best = c; bestE = e; bestA2 = a2; bestA3 = a3; }
        }
        for (int k = 0; k < 4000; k++)
        {
            float step = Mathf.Lerp(20f, 1f, k / 4000f);
            var e = bestE + new Vector3(Rand(-step, step), Rand(-step, step), Rand(-step, step));
            float a2 = Mathf.Clamp(bestA2 + Rand(-step, step), 0f, 100f), a3 = Mathf.Clamp(bestA3 + Rand(-step, step), 0f, 100f);
            float c = Cost(e, a2, a3);
            if (c < best) { best = c; bestE = e; bestA2 = a2; bestA3 = a3; }
        }
        Cost(bestE, bestA2, bestA3); // leave the thumb in the best pose (captured by the caller)
        Vector3 tipNow = tipBone != null ? tipBone.position : b3.position + (b3.position - b2.position);
        return $"thumb tip {Vector3.Distance(tipNow, tipTarget) * 100f:F1} cm from its spot on the middle finger, closest to a finger {clearance * 100f:F1} cm (finger radius {r * 100f:F1} cm)";
    }
}
