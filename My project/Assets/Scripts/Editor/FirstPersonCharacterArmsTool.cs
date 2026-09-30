using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Makes the player character's own arms (CorporateMiner, under Player/CharacterVisual) the arms
/// seen in first person, instead of the separate WRAD arms each first-person view used to carry.
/// Menu: Ore What > Use Character Arms In First Person. Safe to run again (also after
/// rebuilding the viewmodels or the character; MiningSetupTool and CharacterSetupTool call it).
///
/// 1. Splits the character's shirt+arms mesh along its bone weights into a torso part and an arms
///    part (same bones, same materials; meshes saved in Assets/Models/CorporateMiner/), so the
///    local camera can show just the arms. Nothing is duplicated.
/// 2. Every first-person view under the camera keeps its grips, elbow hints, grip settings and
///    behaviour (swing, weapon, held item); only its FirstPersonArmsIK moves from the WRAD arms onto
///    the character's arm bones, on a "CharacterArmsIK" child. The grips are recalibrated for the
///    character's hands (held the way FirstPersonPresentation will place them: the layout's shoulder
///    point on the character's real shoulders) and the old ArmsModel is deleted.
/// 3. Adds FirstPersonPresentation to the camera: the character's skeleton stays anatomical, the
///    held item is moved into its real hands, and only the overlay camera is offset so first person
///    looks unchanged. Carried ores get a carry point at the presented spot.
/// 4. Adds FirstPersonBodyVisibility to CharacterVisual (what the local camera sees of the body).
/// </summary>
public static class FirstPersonCharacterArmsTool
{
    private const string MeshFolder = "Assets/Models/CorporateMiner";
    private const string IKName = "CharacterArmsIK";
    private const string ArmsRendererName = "Arms";
    private const string CarryPointName = "CarryPoint";
    // Midpoint of the shoulders the first-person layouts were designed around (camera space): the old
    // arms' shoulders were at (-0.221, -0.151, 0.134) and (0.203, -0.151, 0.134).
    private static readonly Vector3 DefaultLayoutShoulders = new Vector3(-0.009f, -0.151f, 0.134f);
    private static readonly string[] OldAnchorNames = { "LeftShoulderAnchor", "RightShoulderAnchor" };

    [MenuItem("Ore What/Use Character Arms In First Person")]
    public static void Menu()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("[Ore What] No object tagged 'Player' in the open scene."); return; }
        if (!Apply(player)) return;
        EditorSceneManager.MarkSceneDirty(player.scene);
        EditorSceneManager.SaveScene(player.scene);
    }

    /// <summary>Converts every first-person view to the character's arms. Returns false if there's no character.</summary>
    public static bool Apply(GameObject player)
    {
        Transform visual = player.transform.Find("CharacterVisual");
        Animator animator = visual != null ? visual.GetComponentInChildren<Animator>(true) : null;
        Transform cam = player.transform.Find("CameraRoot/PlayerCamera");
        if (cam == null) { Camera c = player.GetComponentInChildren<Camera>(true); cam = c != null ? c.transform : null; }
        if (animator == null || cam == null)
        {
            Debug.LogWarning("[Ore What] No character under Player/CharacterVisual (or no camera): first-person arms left as they are.");
            return false;
        }
        var rig = new Rig(animator.transform);
        SkinnedMeshRenderer body = FindBodyRenderer(animator.transform, rig);
        if (!rig.Valid || body == null)
        {
            Debug.LogWarning("[Ore What] The character has no humanoid arm bones (LeftArm/LeftForeArm/LeftHand...): first-person arms left as they are.");
            return false;
        }

        SkinnedMeshRenderer arms = SplitArms(body, rig);
        Vector3 layoutShoulders = LayoutShoulders(cam);

        int converted = 0;
        List<(Transform t, Vector3 p, Quaternion r, Vector3 s)> pose = Snapshot(animator.transform);
        try
        {
            SampleElbowHinges(animator, rig, out Vector3 hingeL, out Vector3 hingeR);
            foreach (Transform view in cam.Cast<Transform>().ToArray())
                if (ConvertView(view, cam, rig, body, hingeL, hingeR, layoutShoulders)) converted++;
        }
        finally { Restore(pose); } // the scene keeps the character's own pose; the Animator poses it in play

        SetUpPresentation(player, cam, animator, layoutShoulders);

        var vis = visual.GetComponent<FirstPersonBodyVisibility>();
        if (vis == null) vis = Undo.AddComponent<FirstPersonBodyVisibility>(visual.gameObject);
        var so = new SerializedObject(vis);
        SetArray(so.FindProperty("arms"), new Object[] { arms });
        SetArray(so.FindProperty("hiddenInFirstPerson"),
                 animator.GetComponentsInChildren<Renderer>(true).Where(r => r != arms).Cast<Object>().ToArray());
        so.FindProperty("firstPersonViews").objectReferenceValue = cam;
        so.FindProperty("animator").objectReferenceValue = animator;
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        Debug.Log($"[Ore What] First person now uses the character's arms ({converted} view(s)).");
        return true;
    }

    // --- The character's arm bones (Mixamo "mixamorig:LeftArm" or plain "LeftArm") ---------------

    private class Rig
    {
        public readonly Side Left, Right;
        public Rig(Transform root) { Left = new Side(root, "Left"); Right = new Side(root, "Right"); }
        public bool Valid => Left.Valid && Right.Valid;
    }

    private class Side
    {
        private readonly Transform root;
        private readonly string side;
        public readonly Transform upper, fore, hand;
        public Side(Transform root, string side)
        {
            this.root = root; this.side = side;
            upper = Bone(root, side + "Arm");
            fore = Bone(root, side + "ForeArm");
            hand = Bone(root, side + "Hand");
        }
        public bool Valid => upper != null && fore != null && hand != null && Finger("Index", 1) != null && Finger("Pinky", 1) != null;
        public Transform Finger(string finger, int joint) => Bone(root, $"{side}Hand{finger}{joint}");
    }

    private static Transform Bone(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name || t.name.EndsWith(":" + name)) return t;
        return null;
    }

    /// <summary>The skinned mesh that contains the arms (the biggest one skinned to the forearms).</summary>
    private static SkinnedMeshRenderer FindBodyRenderer(Transform model, Rig rig) =>
        model.GetComponentsInChildren<SkinnedMeshRenderer>(true)
             .Where(r => r.name != ArmsRendererName && r.bones.Contains(rig.Left.fore) && r.bones.Contains(rig.Right.fore))
             .OrderByDescending(r => r.sharedMesh != null ? r.sharedMesh.vertexCount : 0)
             .FirstOrDefault();

    // --- 1. Torso / arms split ----------------------------------------------------------------------

    /// <summary>
    /// Triangles mostly weighted to an arm (upper arm down to the fingertips) go to the "Arms"
    /// renderer, the rest stay on the body renderer. Both keep the original skin weights, so the
    /// seam between them deforms identically and never opens.
    /// </summary>
    private static SkinnedMeshRenderer SplitArms(SkinnedMeshRenderer body, Rig rig)
    {
        Mesh original = body.sharedMesh;
        if (original.name.EndsWith("_Torso")) // already split: start again from the imported mesh
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(body);
            if (source != null && source.sharedMesh != null) original = source.sharedMesh;
        }

        Transform[] bones = body.bones;
        int[] boneSide = bones.Select(b => b == null ? 0 : b.IsChildOf(rig.Left.upper) ? 1 : b.IsChildOf(rig.Right.upper) ? 2 : 0).ToArray();

        int n = original.vertexCount;
        byte[] perVertex = original.GetBonesPerVertex().ToArray(); // copies: the native views don't outlive mesh edits
        BoneWeight1[] weights = original.GetAllBoneWeights().ToArray();
        var sideWeight = new float[n, 3];
        for (int v = 0, w = 0; v < n; v++)
            for (int k = 0; k < perVertex[v]; k++, w++)
                sideWeight[v, boneSide[weights[w].boneIndex]] += weights[w].weight;

        Mesh torso = Object.Instantiate(original);
        Mesh arms = Object.Instantiate(original);
        torso.name = original.name + "_Torso";
        arms.name = original.name + "_Arms";
        for (int sub = 0; sub < original.subMeshCount; sub++)
        {
            int[] tris = original.GetTriangles(sub);
            var torsoTris = new List<int>();
            var armTris = new List<int>();
            for (int i = 0; i < tris.Length; i += 3)
            {
                float left = 0f, right = 0f;
                for (int c = 0; c < 3; c++) { left += sideWeight[tris[i + c], 1]; right += sideWeight[tris[i + c], 2]; }
                bool isArm = (left + right) / 3f > 0.5f;
                (isArm ? armTris : torsoTris).AddRange(new[] { tris[i], tris[i + 1], tris[i + 2] });
            }
            torso.SetTriangles(torsoTris, sub, false);
            arms.SetTriangles(armTris, sub, false);
        }

        torso = SaveMesh(torso);
        arms = SaveMesh(arms);

        Undo.RecordObject(body, "Split Character Arms");
        body.sharedMesh = torso;

        Transform armsT = body.transform.parent.Find(ArmsRendererName);
        if (armsT == null)
        {
            armsT = new GameObject(ArmsRendererName).transform;
            armsT.SetParent(body.transform.parent, false);
            Undo.RegisterCreatedObjectUndo(armsT.gameObject, "Split Character Arms");
        }
        armsT.localPosition = body.transform.localPosition;
        armsT.localRotation = body.transform.localRotation;
        armsT.localScale = body.transform.localScale;
        armsT.gameObject.layer = body.gameObject.layer;
        var smr = armsT.GetComponent<SkinnedMeshRenderer>();
        if (smr == null) smr = armsT.gameObject.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = arms;
        smr.bones = body.bones;
        smr.rootBone = body.rootBone;
        smr.sharedMaterials = body.sharedMaterials;
        smr.localBounds = body.localBounds;
        smr.updateWhenOffscreen = true; // the IK moves the arms far from the body's bounds
        return smr;
    }

    private static Mesh SaveMesh(Mesh mesh)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Models")) AssetDatabase.CreateFolder("Assets", "Models");
        if (!AssetDatabase.IsValidFolder(MeshFolder)) AssetDatabase.CreateFolder("Assets/Models", "CorporateMiner");
        string path = $"{MeshFolder}/{mesh.name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, existing); // keep the asset (and every reference to it)
        Object.DestroyImmediate(mesh);
        return existing;
    }

    // --- 2. Views ---------------------------------------------------------------------------------

    private static bool ConvertView(Transform view, Transform cam, Rig rig, SkinnedMeshRenderer body,
                                    Vector3 hingeL, Vector3 hingeR, Vector3 layoutShoulders)
    {
        Transform holder = view.Find(IKName);
        FirstPersonArmsIK ik = holder != null ? holder.GetComponent<FirstPersonArmsIK>() : null;
        FirstPersonArmsIK old = view.GetComponentsInChildren<FirstPersonArmsIK>(true).FirstOrDefault(c => c != ik);
        if (ik == null && old == null) return false; // not a first-person view (e.g. the overlay camera)

        // Shoulder anchors (an earlier version moved the character's shoulders up to them) are gone.
        foreach (string name in OldAnchorNames)
        {
            Transform anchor = view.Find(name);
            if (anchor != null) Undo.DestroyObjectImmediate(anchor.gameObject);
        }

        if (ik == null)
        {
            holder = new GameObject(IKName).transform;
            holder.SetParent(view, false);
            holder.gameObject.layer = view.gameObject.layer;
            Undo.RegisterCreatedObjectUndo(holder.gameObject, "Use Character Arms");
            ik = holder.gameObject.AddComponent<FirstPersonArmsIK>();
            EditorUtility.CopySerialized(old, ik); // grips, elbow hints and this view's grip style
        }

        var so = new SerializedObject(ik);
        Wire(so.FindProperty("leftArm"), rig.Left);
        Wire(so.FindProperty("rightArm"), rig.Right);
        EnsureHandPose(so.FindProperty("leftArm.handPose"), view.name + "_LeftHand");
        EnsureHandPose(so.FindProperty("rightArm.handPose"), view.name + "_RightHand");
        so.ApplyModifiedPropertiesWithoutUndo();

        // Rest data from the bind pose (straight fingers, neutral wrists), the elbow hinge from a
        // bent-arm animation frame (the bind pose has straight arms, so no hinge can be read there).
        SetBindPose(body);
        ik.CaptureRestPose();
        so.Update();
        SetHinge(so.FindProperty("leftArm.elbowHingeLocal"), rig.Left, hingeL, body.transform.root);
        SetHinge(so.FindProperty("rightArm.elbowHingeLocal"), rig.Right, hingeR, body.transform.root);
        so.ApplyModifiedPropertiesWithoutUndo();
        SetCurlJoints(ik, rig);

        // Calibrate the grips the way they'll really be held: the view placed so its layout's
        // shoulder point sits on the character's real shoulders (what FirstPersonPresentation does).
        Vector3 offset = (rig.Left.upper.position + rig.Right.upper.position) * 0.5f - cam.TransformPoint(layoutShoulders);
        view.position += offset;
        try { ik.CalibrateGrips(); }
        finally { view.position -= offset; }
        EditorUtility.SetDirty(ik);

        if (old != null) Undo.DestroyObjectImmediate(old.gameObject); // the old WRAD ArmsModel
        return true;
    }

    /// <summary>The layout's shoulder point (camera space): from the old anchors if a view still has them, else the presentation's, else the default.</summary>
    private static Vector3 LayoutShoulders(Transform cam)
    {
        foreach (Transform view in cam)
        {
            Transform l = view.Find(OldAnchorNames[0]), r = view.Find(OldAnchorNames[1]);
            if (l != null && r != null) return cam.InverseTransformPoint((l.position + r.position) * 0.5f);
        }
        var existing = cam.GetComponent<FirstPersonPresentation>();
        return existing != null ? new SerializedObject(existing).FindProperty("layoutShoulders").vector3Value : DefaultLayoutShoulders;
    }

    // --- 3. Presentation ----------------------------------------------------------------------------

    private static void SetUpPresentation(GameObject player, Transform cam, Animator animator, Vector3 layoutShoulders)
    {
        var presentation = cam.GetComponent<FirstPersonPresentation>();
        if (presentation == null) presentation = Undo.AddComponent<FirstPersonPresentation>(cam.gameObject);
        var so = new SerializedObject(presentation);
        so.FindProperty("character").objectReferenceValue = animator;
        so.FindProperty("layoutShoulders").vector3Value = layoutShoulders;
        so.FindProperty("viewModelCamera").objectReferenceValue = cam.Find("ViewModelCamera");

        // Carried world ores are drawn by the main camera, so they're carried at the spot where the
        // first-person hands appear (the held-item holder, un-offset), not at the holder itself.
        HeldResourceView held = cam.GetComponentInChildren<HeldResourceView>(true);
        if (held != null && held.Holder != null)
        {
            Transform carry = cam.Find(CarryPointName);
            if (carry == null)
            {
                carry = new GameObject(CarryPointName).transform;
                carry.SetParent(cam, false);
                Undo.RegisterCreatedObjectUndo(carry.gameObject, "Use Character Arms");
            }
            carry.SetPositionAndRotation(held.Holder.position, held.Holder.rotation);
            so.FindProperty("carryPoint").objectReferenceValue = carry;
            so.FindProperty("carryHolder").objectReferenceValue = held.Holder;
            var carrier = player.GetComponent<OreCarryController>();
            if (carrier != null)
            {
                var cso = new SerializedObject(carrier);
                cso.FindProperty("carryPoint").objectReferenceValue = carry;
                cso.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>An (empty) Hand Pose asset per view and hand, so poses can be captured in Play mode (assets keep Play-mode changes; scene references wouldn't).</summary>
    private static void EnsureHandPose(SerializedProperty prop, string name)
    {
        if (prop.objectReferenceValue != null) return;
        const string folder = "Assets/Animations/Character/HandPoses";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Animations/Character", "HandPoses");
        string path = $"{folder}/{name}.asset";
        var pose = AssetDatabase.LoadAssetAtPath<HandPose>(path);
        if (pose == null) { pose = ScriptableObject.CreateInstance<HandPose>(); AssetDatabase.CreateAsset(pose, path); }
        prop.objectReferenceValue = pose;
    }

    private static void Wire(SerializedProperty arm, Side s)
    {
        arm.FindPropertyRelative("upperArm").objectReferenceValue = s.upper;
        arm.FindPropertyRelative("forearm").objectReferenceValue = s.fore;
        arm.FindPropertyRelative("hand").objectReferenceValue = s.hand;
        arm.FindPropertyRelative("indexKnuckle").objectReferenceValue = s.Finger("Index", 1);
        arm.FindPropertyRelative("pinkyKnuckle").objectReferenceValue = s.Finger("Pinky", 1);
        SetArray(arm.FindPropertyRelative("fistFingers"),
                 new Object[] { s.Finger("Index", 1), s.Finger("Middle", 1), s.Finger("Ring", 1), s.Finger("Pinky", 1) });
        arm.FindPropertyRelative("twistBones").arraySize = 0; // Mixamo arms have no forearm twist bones
    }

    /// <summary>Call in the bind pose (flat hands). Each finger joint curls around the local axis that moves its tip toward the palm.</summary>
    private static void SetCurlJoints(FirstPersonArmsIK ik, Rig rig)
    {
        var joints = new List<Transform>();
        var axes = new List<Vector3>();
        var thumbs = new List<bool>();
        foreach (Side s in new[] { rig.Left, rig.Right })
        {
            Vector3 palm = PointInFrontOfPalm(s);
            foreach (string finger in new[] { "Index", "Middle", "Ring", "Pinky", "Thumb" })
            {
                bool isThumb = finger == "Thumb";
                Transform tip = s.Finger(finger, 4);
                for (int i = isThumb ? 2 : 1; i <= 3; i++) // the thumb's base stays put; only its tip joints close
                {
                    Transform joint = s.Finger(finger, i);
                    if (joint == null || tip == null) continue;
                    joints.Add(joint);
                    axes.Add(CurlAxisToward(joint, tip, palm));
                    thumbs.Add(isThumb);
                }
            }
        }
        ik.SetCurlJoints(joints.ToArray(), axes.ToArray(), thumbs.ToArray());
    }

    /// <summary>
    /// A point out in front of the palm. The bind pose has flat fingers in line with the wrist, so
    /// "toward the wrist" (what the WRAD setup used) can't tell curling in from bending backward.
    /// The palm is the side of the hand the thumb sits on.
    /// </summary>
    private static Vector3 PointInFrontOfPalm(Side s)
    {
        Vector3 fingers = s.Finger("Middle", 1).position - s.hand.position;
        Vector3 across = s.Finger("Index", 1).position - s.Finger("Pinky", 1).position;
        Vector3 palmNormal = Vector3.Cross(across, fingers).normalized;
        Transform thumbTip = s.Finger("Thumb", 4) != null ? s.Finger("Thumb", 4) : s.Finger("Thumb", 3);
        if (thumbTip != null && Vector3.Dot(palmNormal, thumbTip.position - s.hand.position) < 0f) palmNormal = -palmNormal;
        return s.hand.position + fingers * 1.5f + palmNormal * (fingers.magnitude * 2f);
    }

    /// <summary>The local axis (±X or ±Z) whose rotation brings the fingertip closest to <paramref name="target"/>.</summary>
    private static Vector3 CurlAxisToward(Transform joint, Transform tip, Vector3 target)
    {
        Quaternion rest = joint.localRotation;
        Vector3 best = Vector3.right;
        float bestDist = float.MaxValue;
        foreach (Vector3 axis in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            joint.localRotation = rest * Quaternion.AngleAxis(30f, axis);
            float d = Vector3.Distance(tip.position, target);
            if (d < bestDist) { bestDist = d; best = axis; }
        }
        joint.localRotation = rest;
        return best;
    }

    // --- Poses ----------------------------------------------------------------------------------------

    /// <summary>Elbow hinge axes (upper-arm space) read from a bent-arm frame of the run animation.</summary>
    private static void SampleElbowHinges(Animator animator, Rig rig, out Vector3 left, out Vector3 right)
    {
        left = right = Vector3.zero;
        AnimationClip clip = animator.runtimeAnimatorController != null
            ? animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.Contains("Run"))
            : null;
        if (clip == null) return;
        AnimationMode.StartAnimationMode();
        try
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, 0f);
            AnimationMode.EndSampling();
            left = HingeFromPose(rig.Left);
            right = HingeFromPose(rig.Right);
        }
        finally { AnimationMode.StopAnimationMode(); }
    }

    private static Vector3 HingeFromPose(Side s)
    {
        Vector3 upper = s.fore.position - s.upper.position, lower = s.hand.position - s.fore.position;
        if (Vector3.Angle(upper, lower) < 20f) return Vector3.zero; // too straight to tell
        return s.upper.InverseTransformDirection(Vector3.Cross(upper, lower).normalized);
    }

    /// <summary>Sampled hinge, or (bind pose, arms out) the axis that bends the forearm toward the front.</summary>
    private static void SetHinge(SerializedProperty prop, Side s, Vector3 sampled, Transform characterRoot)
    {
        if (sampled != Vector3.zero) { prop.vector3Value = sampled; return; }
        Vector3 forward = characterRoot.forward;
        prop.vector3Value = s.upper.InverseTransformDirection(Vector3.Cross(s.fore.position - s.upper.position, forward).normalized);
    }

    /// <summary>Puts the skeleton in its bind pose (the pose the mesh was skinned in).</summary>
    private static void SetBindPose(SkinnedMeshRenderer body)
    {
        Matrix4x4[] bind = body.sharedMesh.bindposes;
        Transform[] bones = body.bones;
        foreach (int i in Enumerable.Range(0, bones.Length).Where(i => bones[i] != null).OrderBy(i => Depth(bones[i])))
        {
            Matrix4x4 m = body.transform.localToWorldMatrix * bind[i].inverse;
            bones[i].SetPositionAndRotation(m.GetColumn(3), m.rotation);
        }
    }

    private static int Depth(Transform t) { int d = 0; for (; t != null; t = t.parent) d++; return d; }

    private static List<(Transform, Vector3, Quaternion, Vector3)> Snapshot(Transform root) =>
        root.GetComponentsInChildren<Transform>(true).Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToList();

    private static void Restore(List<(Transform t, Vector3 p, Quaternion r, Vector3 s)> pose)
    {
        foreach (var (t, p, r, s) in pose)
            if (t != null) { t.localPosition = p; t.localRotation = r; t.localScale = s; }
    }

    private static void SetArray(SerializedProperty array, Object[] values)
    {
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
