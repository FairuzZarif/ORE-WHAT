using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes how every item is held, for other players' view of you (RemotePlayerPresentation). Called by
/// MultiplayerSetupTool while it builds the NetworkPlayer prefab.
///
/// For each item it shows that item's first-person view, moves the view onto the character's real shoulders
/// exactly like FirstPersonPresentation does in game, runs the view's own arm IK once, and records:
///   the wrists, elbows and finger joints relative to the shoulders and the look direction, and
///   a copy of the first-person item model placed in the right hand bone (with its muzzle flash).
/// Everything in the scene is restored afterwards (the scene is not saved).
/// </summary>
public static class RemoteHoldPoseBaker
{
    private const int PlayerLayer = 8;

    private struct Saved { public Transform t; public Vector3 p; public Quaternion r; public Vector3 s; public bool active; }

    public static void Bake(GameObject scenePlayer, Animator avatarAnimator, RemotePlayerPresentation target, List<ItemData> items)
    {
        Animator sceneAnimator = scenePlayer.GetComponentInChildren<CharacterAnimator>(true).GetComponent<Animator>();
        Transform cam = scenePlayer.GetComponentInChildren<Camera>(true).transform;
        var presentation = cam.GetComponent<FirstPersonPresentation>();
        Vector3 layoutShoulders = presentation != null
            ? new SerializedObject(presentation).FindProperty("layoutShoulders").vector3Value
            : new Vector3(-0.009f, -0.251f, 0.134f);

        // Scene bones (posed by the view IK) and the matching avatar bones (where the copies go).
        Transform sRU = Bone(sceneAnimator, "RightArm"), sRL = Bone(sceneAnimator, "RightForeArm"), sRH = Bone(sceneAnimator, "RightHand");
        Transform sLU = Bone(sceneAnimator, "LeftArm"), sLL = Bone(sceneAnimator, "LeftForeArm"), sLH = Bone(sceneAnimator, "LeftHand");
        Transform aRU = Bone(avatarAnimator, "RightArm"), aRL = Bone(avatarAnimator, "RightForeArm"), aRH = Bone(avatarAnimator, "RightHand");
        Transform aLU = Bone(avatarAnimator, "LeftArm"), aLL = Bone(avatarAnimator, "LeftForeArm"), aLH = Bone(avatarAnimator, "LeftHand");
        string[] rightPaths = FingerPaths(sRH), leftPaths = FingerPaths(sLH);

        // Head + hardhat ellipsoid (measured in the head bone's frame), and where it is in look space (shoulders + view rotation).
        Transform sHead = Bone(sceneAnimator, "Head"), aHead = Bone(avatarAnimator, "Head");
        Vector3 headLocal = MeasureHead(sHead, sceneAnimator.transform, out Vector3 headRadii);
        System.Func<HeadShape> headInLook = () => new HeadShape
        {
            centre = Quaternion.Inverse(cam.rotation) * (sHead.position + sHead.rotation * headLocal - (sLU.position + sRU.position) * 0.5f),
            rotation = Quaternion.Inverse(cam.rotation) * sHead.rotation,
            radii = headRadii + Vector3.one * HeadMargin,
        };
        Vector3 shoulderMid = (sLU.position + sRU.position) * 0.5f;
        var reach = new Reach
        {
            right = Quaternion.Inverse(cam.rotation) * (sRU.position - shoulderMid),
            left = Quaternion.Inverse(cam.rotation) * (sLU.position - shoulderMid),
            length = Vector3.Distance(sRU.position, sRL.position) + Vector3.Distance(sRL.position, sRH.position),
        };

        // The first-person views, from PlayerEquipment.
        var equipment = scenePlayer.GetComponent<PlayerEquipment>();
        var eso = new SerializedObject(equipment);
        var viewOf = new Dictionary<ItemData, (GameObject view, Transform model)>();
        SerializedProperty views = eso.FindProperty("views");
        for (int i = 0; i < views.arraySize; i++)
        {
            SerializedProperty e = views.GetArrayElementAtIndex(i);
            if (e.FindPropertyRelative("item").objectReferenceValue is ItemData item)
                viewOf[item] = (e.FindPropertyRelative("view").objectReferenceValue as GameObject,
                                e.FindPropertyRelative("throwFrom").objectReferenceValue as Transform);
        }
        var itemView = eso.FindProperty("itemView").objectReferenceValue as HeldResourceView;
        var fistsView = eso.FindProperty("fistsView").objectReferenceValue as GameObject;

        // Remember everything we touch: every transform under the Player, and what already exists under the item view.
        var saved = new List<Saved>();
        foreach (Transform t in scenePlayer.GetComponentsInChildren<Transform>(true))
            saved.Add(new Saved { t = t, p = t.localPosition, r = t.localRotation, s = t.localScale, active = t.gameObject.activeSelf });
        var existing = new HashSet<Transform>(itemView != null ? itemView.GetComponentsInChildren<Transform>(true) : new Transform[0]);

        var holds = new List<RemotePlayerPresentation.HoldPose>();
        RemotePlayerPresentation.HoldPose fistsPose = null, fistsStrike = null;
        try
        {
            foreach (ItemData item in items)
            {
                Restore(saved);
                GameObject view;
                Transform model;
                if (viewOf.TryGetValue(item, out var v) && v.view != null) { view = v.view; model = v.model; }
                else if (itemView != null) { view = itemView.gameObject; model = null; }
                else continue;

                ShowOnly(cam, view);
                if (model == null) { itemView.ShowItem(item); model = itemView.CurrentModel; }
                var hold = Solve(view, cam, layoutShoulders, sRU, sLU, sRL, sLL, sRH, sLH, rightPaths, leftPaths);
                if (hold == null) continue;
                hold.item = item;
                if (model != null)
                {
                    hold.visual = CopyModel(model, sRH, aRH, item.name);
                    hold.clearancePoints = ClearancePoints(model, sRH);
                }
                var weapon = view.GetComponentInChildren<WeaponController>(true);
                if (weapon != null) { hold.shotKick = weapon.Automatic ? 3f : 8f; hold.rifleSounds = weapon.Automatic; }

                // The first-person actions themselves, sampled: their own code poses the view, the view's own IK poses the arms.
                GameObject sampledView = view;
                SampleView sample = () => Solve(sampledView, cam, layoutShoulders, sRU, sLU, sRL, sLL, sRH, sLH, rightPaths, leftPaths);
                var swing = view.GetComponentInChildren<PickaxeSwing>(true);
                if (swing != null)
                {
                    hold.swings = new RemotePlayerPresentation.ArmClip[3];
                    for (int kind = 0; kind < 3; kind++)
                        hold.swings[kind] = BakeSwing(swing, kind, saved, cam, view, sample, hold, headInLook, reach);
                    swing.EndSwingPreview();
                    // In game the first-person idle IS the swing's resting pose (every swing starts and ends there), not the
                    // tool's saved scene transform: hold the tool exactly like that, so swings flow out of and back into it.
                    UseAsHold(hold, hold.swings[0].samples[hold.swings[0].samples.Length - 1]);
                    ThirdPersonToolRest(hold);
                }
                if (weapon != null && model != null)
                {
                    Transform[] bones = weapon.ReloadBones.Where(b => b != null && b.IsChildOf(model)).ToArray();
                    hold.reloadBones = bones.Select(b => AnimationUtility.CalculateTransformPath(b, model)).ToArray();
                    hold.reload = BakeReload(weapon, bones, saved, cam, view, sample);
                    if (weapon.Automatic) ShoulderLongGun(hold, sRH.lossyScale.x, reach); // the pistol keeps its first-person hold
                }
                holds.Add(hold);
            }

            var fistsController = fistsView != null ? fistsView.GetComponentInChildren<FistsController>(true) : null;
            if (fistsController != null)
            {
                // The punch's guard and full-extension poses, with the grips placed exactly as FistsController places them.
                var f = new SerializedObject(fistsController);
                var rGrip = f.FindProperty("rightGrip").objectReferenceValue as Transform;
                var lGrip = f.FindProperty("leftGrip").objectReferenceValue as Transform;
                Vector3 rGuard = f.FindProperty("rightGuard").vector3Value, lGuard = f.FindProperty("leftGuard").vector3Value;
                Quaternion rRot = f.FindProperty("rightGuardRotation").quaternionValue, lRot = f.FindProperty("leftGuardRotation").quaternionValue;
                Vector3 strike = f.FindProperty("strikeOffset").vector3Value;
                float twist = f.FindProperty("strikeTwist").floatValue;
                for (int pass = 0; pass < 2; pass++)
                {
                    Restore(saved);
                    ShowOnly(cam, fistsView);
                    float s = pass;
                    rGrip.SetLocalPositionAndRotation(rGuard + strike * s, Quaternion.AngleAxis(twist * s, Vector3.forward) * rRot);
                    lGrip.SetLocalPositionAndRotation(lGuard + new Vector3(-strike.x, strike.y, strike.z) * s, Quaternion.AngleAxis(-twist * s, Vector3.forward) * lRot);
                    var pose = Solve(fistsView, cam, layoutShoulders, sRU, sLU, sRL, sLL, sRH, sLH, rightPaths, leftPaths);
                    if (pass == 0) fistsPose = pose; else fistsStrike = pose;
                }
            }
        }
        finally
        {
            Restore(saved);
            if (itemView != null) // remove the held-item copies ShowItem made
                foreach (Transform t in itemView.GetComponentsInChildren<Transform>(true))
                    if (t != null && !existing.Contains(t) && (t.parent == null || existing.Contains(t.parent)))
                        Object.DestroyImmediate(t.gameObject);
        }

        var so = new SerializedObject(target);
        so.FindProperty("rightUpperArm").objectReferenceValue = aRU;
        so.FindProperty("rightForearm").objectReferenceValue = aRL;
        so.FindProperty("rightHandBone").objectReferenceValue = aRH;
        so.FindProperty("leftUpperArm").objectReferenceValue = aLU;
        so.FindProperty("leftForearm").objectReferenceValue = aLL;
        so.FindProperty("leftHandBone").objectReferenceValue = aLH;
        SetStrings(so.FindProperty("rightFingerPaths"), rightPaths);
        SetStrings(so.FindProperty("leftFingerPaths"), leftPaths);
        so.FindProperty("headBone").objectReferenceValue = aHead;
        so.FindProperty("headCentre").vector3Value = headLocal;
        so.FindProperty("headRadii").vector3Value = headRadii;
        so.FindProperty("headMargin").floatValue = HeadMargin;
        so.ApplyModifiedPropertiesWithoutUndo();
        // Plain C# data (poses): assign directly, then let the serializer pick it up.
        target.SetBakedPoses(holds.ToArray(), fistsPose ?? new RemotePlayerPresentation.HoldPose(), fistsStrike ?? fistsPose ?? new RemotePlayerPresentation.HoldPose());
        EditorUtility.SetDirty(target);
        Debug.Log($"[Ore What] Baked {holds.Count} remote hold poses" + (fistsPose != null ? " + fists." : "."));
    }

    private delegate RemotePlayerPresentation.HoldPose SampleView();

    private const float SwingStep = 0.02f; // 50 samples a second
    private const int ReloadSamples = 41;

    /// <summary>
    /// One first-person swing, sampled from rest back to rest. The first-person layout never has to avoid the head (the
    /// camera is inside it), so a sample where the tool would pass through the head is corrected, in this order:
    ///   1. tilt the tool forward around the point between the hands, the least that clears the head (both hands stay
    ///      on the handle and the arms' reach barely changes: the pick swings over the front of the hat, not through it);
    ///   2. only what tilting can't fix: move the hands and tool forward along the look direction.
    /// Both corrections are smoothed over time so the swing stays one fluid motion.
    /// </summary>
    private static RemotePlayerPresentation.ArmClip BakeSwing(PickaxeSwing swing, int kind, List<Saved> saved, Transform cam, GameObject view,
        SampleView sample, RemotePlayerPresentation.HoldPose hold, System.Func<HeadShape> headInLook, Reach reach)
    {
        Restore(saved);
        float duration = swing.BeginSwingPreview(kind, out float impact);
        int n = Mathf.Max(2, Mathf.CeilToInt(duration / SwingStep) + 1);
        var samples = new RemotePlayerPresentation.ArmSample[n];
        HeadShape head = default;
        for (int i = 0; i < n; i++)
        {
            Restore(saved);
            ShowOnly(cam, view);
            swing.PreviewSwingAt(duration * i / (n - 1));
            samples[i] = ToSample(sample());
            head = headInLook();
        }

        // 1. The smallest forward tilt that clears the head, per sample, then smoothed.
        var raw = new float[n];
        var tilt = new float[n];
        for (int i = 0; i < n; i++)
        {
            raw[i] = HeadPush(samples[i], hold.clearancePoints, head);
            if (raw[i] <= 0f) continue;
            tilt[i] = MaxTilt;
            for (float a = 2f; a <= MaxTilt; a += 2f)
                if (HeadPush(Tilted(samples[i], a), hold.clearancePoints, head) <= 0f) { tilt[i] = a; break; }
        }
        float[] tiltSmooth = SmoothNonDecreasing(tilt);
        for (int i = 0; i < n; i++) samples[i] = Tilted(samples[i], tiltSmooth[i]);

        // 2. Whatever is left (the hands themselves pass over the head, e.g. an overhead wind-up): move the hands and tool
        //    out of the head, in the one direction (per swing) that needs the shortest arms: beside / in front of the head,
        //    never through it. Smoothed.
        Vector3 best = Vector3.forward;
        float[] pushSmooth = null;
        float bestReach = float.MaxValue;
        foreach (Vector3 candidate in PushDirections)
        {
            Vector3 dir = candidate.normalized;
            var need = new float[n];
            for (int i = 0; i < n; i++) need[i] = HeadPush(samples[i], hold.clearancePoints, head, dir);
            float[] smooth = SmoothNonDecreasing(need);
            float worst = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector3 m = dir * smooth[i];
                worst = Mathf.Max(worst, Vector3.Distance(samples[i].rightHand + m, reach.right) / reach.length,
                                         Vector3.Distance(samples[i].leftHand + m, reach.left) / reach.length);
            }
            if (worst < bestReach - 0.01f) { bestReach = worst; best = dir; pushSmooth = smooth; }
        }
        float maxRaw = 0f, maxTilt = 0f, maxPush = 0f, worstReach = 0f;
        for (int i = 0; i < n; i++)
        {
            Vector3 move = best * pushSmooth[i];
            samples[i].rightHand += move; samples[i].leftHand += move; samples[i].rightElbow += move; samples[i].leftElbow += move;
            maxRaw = Mathf.Max(maxRaw, raw[i]); maxTilt = Mathf.Max(maxTilt, tiltSmooth[i]); maxPush = Mathf.Max(maxPush, pushSmooth[i]);
            worstReach = Mathf.Max(worstReach, Vector3.Distance(samples[i].rightHand, reach.right) / reach.length,
                                               Vector3.Distance(samples[i].leftHand, reach.left) / reach.length);
        }
        string[] names = { "Right", "Left", "Overhead" };
        Debug.Log($"[Ore What] {hold.item.name} {names[kind]} swing: {n} samples over {duration:F2}s (impact {impact:F2}s). " +
                  $"First-person poses go up to {maxRaw * 100f:F0} cm into the head; corrected by tilting up to {maxTilt:F0}° " +
                  $"and moving up to {maxPush * 100f:F0} cm toward {best.ToString("F2")} (look space: x right, y up, z forward). " +
                  $"Furthest hand reach {worstReach * 100f:F0}% of the arm.");
        return new RemotePlayerPresentation.ArmClip { duration = duration, impact = impact, samples = samples };
    }

    private static void UseAsHold(RemotePlayerPresentation.HoldPose h, RemotePlayerPresentation.ArmSample s)
    {
        h.rightHand = s.rightHand; h.leftHand = s.leftHand; h.rightElbow = s.rightElbow; h.leftElbow = s.leftElbow;
        h.rightRotation = s.rightRotation; h.leftRotation = s.leftRotation;
    }

    // ---- Third-person mining-tool rest ------------------------------------------------------------------------------
    // The first-person rest holds the tool close beside the right shoulder: from outside the right elbow sticks up above
    // the shoulder and the left forearm cuts through the chest to reach the handle. Other players see the same grip
    // (hands on the handle exactly as in first person) with the whole tool moved lower, forward and toward the centre,
    // and the elbows hanging down. Swing frames near the rest fade into this placement; the swing itself is untouched.
    // Look space (x right, y up, z forward):
    private static readonly Vector3 ToolRestMove = new Vector3(-0.10f, -0.15f, 0.16f);
    // Elbow bend directions at rest (relative to the point between the shoulders): down and slightly out.
    private static readonly Vector3 ToolRightElbow = new Vector3(0.32f, -0.32f, -0.04f), ToolLeftElbow = new Vector3(-0.22f, -0.32f, 0.06f);
    // How far (metres) the hands must have left the rest before a swing frame is fully the first-person swing.
    private const float ToolRestFadeStart = 0.03f, ToolRestFadeEnd = 0.22f;

    private static void ThirdPersonToolRest(RemotePlayerPresentation.HoldPose h)
    {
        Vector3 restR = h.rightHand, restL = h.leftHand;
        foreach (RemotePlayerPresentation.ArmClip clip in h.swings)
        {
            if (clip == null || clip.IsEmpty) continue;
            var weights = new float[clip.samples.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                RemotePlayerPresentation.ArmSample s = clip.samples[i];
                float away = Mathf.Max(Vector3.Distance(s.rightHand, restR), Vector3.Distance(s.leftHand, restL));
                float t = Mathf.Clamp01((away - ToolRestFadeStart) / (ToolRestFadeEnd - ToolRestFadeStart));
                weights[i] = 1f - t * t * (3f - 2f * t); // 1 at rest, 0 once the swing is under way
            }
            for (int i = 0; i < weights.Length; i++)
            {
                RemotePlayerPresentation.ArmSample s = clip.samples[i];
                float w = weights[i];
                s.rightHand += ToolRestMove * w; s.leftHand += ToolRestMove * w;
                s.rightElbow = Vector3.Lerp(s.rightElbow, ToolRightElbow, w);
                s.leftElbow = Vector3.Lerp(s.leftElbow, ToolLeftElbow, w);
            }
        }
        h.rightHand += ToolRestMove; h.leftHand += ToolRestMove;
        h.rightElbow = ToolRightElbow; h.leftElbow = ToolLeftElbow;
    }

    // ---- Third-person long gun (rifle) ----------------------------------------------------------------------------
    // The first-person rifle layout is made for the camera: from outside the gun floats at shoulder height with nothing
    // in the shoulder, and the support hand clamps the magazine well from the side (best for the first-person wrist).
    // Other players see it shouldered instead. The first-person hand-on-gun relationship stays the source of truth;
    // only where the whole gun sits on the body changes, and the support hand slides along the gun to the handguard.
    // Look space (x right, y up, z forward), relative to the right shoulder joint:
    private static readonly Vector3 StockPocket = new Vector3(-0.06f, -0.09f, 0.05f);
    private const float BarrelDip = 6f;          // degrees nose-down: a ready carry (looking down tips it further)
    private const float BarrelInward = 3f;       // degrees toward the body's centre line
    private const float ForegripAlong = 0.62f;   // support hand: this far from the pistol grip toward the muzzle (0..1)
    // Elbow bend directions (look space, relative to the point between the shoulders): elbows down, not chicken-winged.
    private static readonly Vector3 RifleRightElbow = new Vector3(0.30f, -0.26f, 0.02f), RifleLeftElbow = new Vector3(-0.14f, -0.30f, 0.24f);

    private struct Pose
    {
        public Vector3 p; public Quaternion r;
        public Pose(Vector3 p, Quaternion r) { this.p = p; this.r = r; }
        public Pose Mul(Pose b) => new Pose(p + r * b.p, r * b.r);
        public Pose Inverse() { Quaternion i = Quaternion.Inverse(r); return new Pose(i * -p, i); }
    }

    private static void ShoulderLongGun(RemotePlayerPresentation.HoldPose h, float handScale, Reach reach)
    {
        Transform v = h.visual.transform;
        // Gun geometry in its own space (pivot on the pistol grip, barrel along +Z, up +Y).
        Transform muzzleT = v.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Attach_Muzzle");
        Vector3 muzzle = muzzleT != null ? v.InverseTransformPoint(muzzleT.position) : new Vector3(0f, 0.1f, 0.4f);
        float buttZ = 0f;
        foreach (Renderer r in v.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            UnityEngine.Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
            if (mesh == null) continue;
            Bounds b = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                buttZ = Mathf.Min(buttZ, v.InverseTransformPoint(r.transform.TransformPoint(c)).z);
            }
        }
        var butt = new Vector3(0f, muzzle.y - 0.02f, buttZ);

        var gunInHand = new Pose(v.localPosition * handScale, v.localRotation);
        Pose GunOf(Vector3 hand, Quaternion rot) => new Pose(hand, rot).Mul(gunInHand);
        Pose gun0 = GunOf(h.rightHand, h.rightRotation);

        // Where the gun goes: butt in the shoulder pocket, barrel along the look direction (a little low and inward).
        Quaternion gunRot = Quaternion.Euler(BarrelDip, -BarrelInward, 0f);
        var gun = new Pose(reach.right + StockPocket - gunRot * butt, gunRot);
        Pose move = gun.Mul(gun0.Inverse()); // first-person gun pose → shouldered gun pose (rigid)

        // Support hand: its first-person grip turned around the barrel to underneath and slid forward to the handguard.
        Pose leftOnGun0 = gun0.Inverse().Mul(new Pose(h.leftHand, h.leftRotation));
        var aroundOffset = new Vector2(leftOnGun0.p.x, leftOnGun0.p.y - muzzle.y);
        Quaternion around = Quaternion.AngleAxis(Vector2.SignedAngle(aroundOffset, Vector2.down), Vector3.forward);
        var foregrip = new Pose(new Vector3(0f, muzzle.y, muzzle.z * ForegripAlong) + around * new Vector3(aroundOffset.x, aroundOffset.y, 0f),
                                around * leftOnGun0.r);

        Pose right = move.Mul(new Pose(h.rightHand, h.rightRotation));
        Pose left = gun.Mul(foregrip);
        h.rightHand = right.p; h.rightRotation = right.r;
        h.leftHand = left.p; h.leftRotation = left.r;
        h.rightElbow = RifleRightElbow; h.leftElbow = RifleLeftElbow;

        // The reload, re-based on the shouldered gun. The support hand stays on the handguard except while the
        // first-person reload actually moves it away from its grip (to the magazine and back).
        foreach (RemotePlayerPresentation.ArmSample s in h.reload.samples)
        {
            Pose gunI = GunOf(s.rightHand, s.rightRotation);
            Pose gunI2 = move.Mul(gunI);
            Pose r2 = move.Mul(new Pose(s.rightHand, s.rightRotation));
            Pose leftOnGun = gunI.Inverse().Mul(new Pose(s.leftHand, s.leftRotation));
            float away = Mathf.Clamp01((Vector3.Distance(leftOnGun.p, leftOnGun0.p) - 0.01f) / 0.05f);
            var blended = new Pose(Vector3.Lerp(foregrip.p, leftOnGun.p, away), Quaternion.Slerp(foregrip.r, leftOnGun.r, away));
            Pose l2 = gunI2.Mul(blended);
            s.rightHand = r2.p; s.rightRotation = r2.r;
            s.leftHand = l2.p; s.leftRotation = l2.r;
            s.rightElbow = RifleRightElbow;
            s.leftElbow = Vector3.Lerp(RifleLeftElbow, move.Mul(new Pose(s.leftElbow, Quaternion.identity)).p, away);
        }
        Debug.Log($"[Ore What] {h.item.name}: shouldered for other players (butt at z {buttZ:F2} m, muzzle {muzzle.z:F2} m; " +
                  $"support hand moved {Vector3.Distance(leftOnGun0.p, foregrip.p) * 100f:F0} cm to the handguard).");
    }

    private const float MaxTilt = 40f;

    // Look space (x right, y up, z forward). Never toward the left: the right hand leads every swing.
    private static readonly Vector3[] PushDirections =
    {
        new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 2f), new Vector3(2f, 0f, 1f),
        new Vector3(1f, 1f, 1f), new Vector3(1f, 0.5f, 0.5f), new Vector3(0f, 0.5f, 1f),
    };

    /// <summary>Where the shoulders are (look space) and how long an arm is, to report reach.</summary>
    private struct Reach { public Vector3 right, left; public float length; }

    /// <summary>The sample with the tool (and both hands on it) turned forward by angle degrees around the point between the hands.</summary>
    private static RemotePlayerPresentation.ArmSample Tilted(RemotePlayerPresentation.ArmSample s, float angle)
    {
        if (angle <= 0f) return s;
        Quaternion q = Quaternion.AngleAxis(angle, Vector3.right); // look space: top of the tool toward the front
        Vector3 pivot = (s.rightHand + s.leftHand) * 0.5f;
        return new RemotePlayerPresentation.ArmSample
        {
            rightHand = pivot + q * (s.rightHand - pivot), leftHand = pivot + q * (s.leftHand - pivot),
            rightElbow = pivot + q * (s.rightElbow - pivot), leftElbow = pivot + q * (s.leftElbow - pivot),
            rightRotation = q * s.rightRotation, leftRotation = q * s.leftRotation, bones = s.bones,
        };
    }

    /// <summary>A widened (max-filtered over ±4 samples) and blurred copy, never below the original values.</summary>
    private static float[] SmoothNonDecreasing(float[] values)
    {
        int n = values.Length;
        var result = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0f, weight = 0f;
            for (int j = -4; j <= 4; j++)
            {
                int k = Mathf.Clamp(i + j, 0, n - 1);
                float widest = 0f;
                for (int q = -4; q <= 4; q++) widest = Mathf.Max(widest, values[Mathf.Clamp(k + q, 0, n - 1)]);
                float w = 5 - Mathf.Abs(j);
                sum += widest * w; weight += w;
            }
            result[i] = Mathf.Max(values[i], sum / weight);
        }
        return result;
    }


    /// <summary>The first-person reload, sampled over its progress (0..1), with the magazine / slide bone positions.</summary>
    private static RemotePlayerPresentation.ArmClip BakeReload(WeaponController weapon, Transform[] bones, List<Saved> saved, Transform cam,
        GameObject view, SampleView sample)
    {
        var samples = new RemotePlayerPresentation.ArmSample[ReloadSamples];
        for (int i = 0; i < ReloadSamples; i++)
        {
            Restore(saved);
            ShowOnly(cam, view);
            weapon.PreviewReloadPose(i / (float)(ReloadSamples - 1));
            samples[i] = ToSample(sample());
            samples[i].bones = bones.Select(b => b.localPosition).ToArray();
        }
        return new RemotePlayerPresentation.ArmClip { duration = 1f, samples = samples };
    }

    private static RemotePlayerPresentation.ArmSample ToSample(RemotePlayerPresentation.HoldPose p) => new RemotePlayerPresentation.ArmSample
    {
        rightHand = p.rightHand, leftHand = p.leftHand, rightElbow = p.rightElbow, leftElbow = p.leftElbow,
        rightRotation = p.rightRotation, leftRotation = p.leftRotation,
    };

    /// <summary>The head + hardhat as an ellipsoid (look space here; RemotePlayerPresentation uses the same shape on the head bone).</summary>
    private struct HeadShape { public Vector3 centre; public Quaternion rotation; public Vector3 radii; }

    /// <summary>How far forward (look space +Z) the sample must move so no clearance point is inside the head.</summary>
    private static float HeadPush(RemotePlayerPresentation.ArmSample s, Vector3[] points, HeadShape head) =>
        HeadPush(s, points, head, Vector3.forward);

    /// <summary>How far along dir (a unit vector in look space) the sample must move so no clearance point is inside the head.</summary>
    private static float HeadPush(RemotePlayerPresentation.ArmSample s, Vector3[] points, HeadShape head, Vector3 dir)
    {
        float push = 0f;
        foreach (Vector3 local in points)
            push = Mathf.Max(push, RemotePlayerPresentation.EllipsoidExit(s.rightHand + s.rightRotation * local, dir,
                                                                           head.centre, head.rotation, head.radii));
        return push;
    }

    /// <summary>The corners and centre of each mesh part of the item (its own bounds, so a long handle isn't one big box), in the hand's space.</summary>
    private static Vector3[] ClearancePoints(Transform model, Transform hand)
    {
        var points = new List<Vector3>();
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            UnityEngine.Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh
                                  : r.TryGetComponent(out MeshFilter mf) ? mf.sharedMesh : null;
            if (mesh == null || r is ParticleSystemRenderer) continue;
            Bounds b = mesh.bounds;
            Transform space = r.transform;
            for (int i = 0; i < 9; i++)
            {
                Vector3 corner = i == 8 ? b.center
                    : b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                points.Add(hand.InverseTransformPoint(space.TransformPoint(corner)));
            }
        }
        return points.ToArray();
    }

    private const float HeadMargin = 0.03f;
    // Vertices this far below the head bone or lower are neck, not head (the chin is about at the head bone).
    private const float NeckCut = -0.06f;

    /// <summary>
    /// The skull + hardhat + lamp as an ellipsoid fitted to their actual (posed) vertices, in the head bone's frame
    /// (rotation only, metres). The neck is left out, so the space in front of the chest stays free for held items.
    /// </summary>
    private static Vector3 MeasureHead(Transform head, Transform characterRoot, out Vector3 radii)
    {
        Quaternion toHead = Quaternion.Inverse(head.rotation);
        Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
        var baked = new UnityEngine.Mesh();
        foreach (Renderer r in characterRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name != "Hardhat" && r.name != "Headlamp" && r.name != "Head_and_Neck") continue;
            Vector3[] vertices;
            if (r is SkinnedMeshRenderer smr) { smr.BakeMesh(baked); vertices = baked.vertices; } // posed, scale applied
            else if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                vertices = mf.sharedMesh.vertices.Select(v => Vector3.Scale(v, r.transform.lossyScale)).ToArray();
            else continue;
            foreach (Vector3 v in vertices)
            {
                Vector3 local = toHead * (r.transform.position + r.transform.rotation * v - head.position);
                if (local.y < NeckCut) continue;
                min = Vector3.Min(min, local); max = Vector3.Max(max, local);
            }
        }
        Object.DestroyImmediate(baked);
        // An ellipsoid inside the box misses its corners: a little larger covers the rounded hat and face.
        radii = (max - min) * 0.5f * 1.1f;
        Vector3 centre = (min + max) * 0.5f;
        Debug.Log($"[Ore What] Head clearance ellipsoid: radii {radii * 100f:F0} cm, centre {centre * 100f:F0} cm from the head bone");
        return centre;
    }

    /// <summary>Puts the view on the real shoulders (like FirstPersonPresentation), solves its IK and reads the arm pose.</summary>
    private static RemotePlayerPresentation.HoldPose Solve(GameObject view, Transform cam, Vector3 layoutShoulders,
        Transform rU, Transform lU, Transform rL, Transform lL, Transform rH, Transform lH, string[] rightPaths, string[] leftPaths)
    {
        var ik = view.GetComponentInChildren<FirstPersonArmsIK>(true);
        if (ik == null) return null;
        Vector3 shoulders = (lU.position + rU.position) * 0.5f;
        view.transform.position += shoulders - cam.TransformPoint(layoutShoulders);
        ik.Solve();

        shoulders = (lU.position + rU.position) * 0.5f;
        Quaternion inv = Quaternion.Inverse(cam.rotation);
        return new RemotePlayerPresentation.HoldPose
        {
            rightHand = inv * (rH.position - shoulders), leftHand = inv * (lH.position - shoulders),
            rightElbow = inv * (rL.position - shoulders), leftElbow = inv * (lL.position - shoulders),
            rightRotation = inv * rH.rotation, leftRotation = inv * lH.rotation,
            rightFingers = LocalRotations(rH, rightPaths), leftFingers = LocalRotations(lH, leftPaths),
        };
    }

    /// <summary>A visual-only copy of the first-person item model, placed in the avatar's right hand like it sits in the real one.</summary>
    private static GameObject CopyModel(Transform model, Transform sceneHand, Transform avatarHand, string itemName)
    {
        GameObject copy = Object.Instantiate(model.gameObject, avatarHand);
        copy.name = "Held " + itemName;
        Matrix4x4 rel = sceneHand.worldToLocalMatrix * model.localToWorldMatrix;
        copy.transform.localPosition = rel.GetColumn(3);
        copy.transform.localRotation = rel.rotation;
        copy.transform.localScale = rel.lossyScale;

        foreach (var c in copy.GetComponentsInChildren<Unity.Netcode.NetworkBehaviour>(true)) Object.DestroyImmediate(c);
        foreach (var c in copy.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true)) Object.DestroyImmediate(c);
        foreach (var c in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(c);
        foreach (var c in copy.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(c);
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PlayerLayer;
        foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
        }
        foreach (Light l in copy.GetComponentsInChildren<Light>(true)) l.enabled = false; // the muzzle light flashes per shot
        copy.SetActive(false);
        return copy;
    }

    private static void ShowOnly(Transform cam, GameObject view)
    {
        foreach (Transform child in cam)
            if (child.GetComponentInChildren<FirstPersonArmsIK>(true) != null) child.gameObject.SetActive(child.gameObject == view);
    }

    private static void Restore(List<Saved> saved)
    {
        foreach (Saved s in saved)
        {
            if (s.t == null) continue;
            s.t.localPosition = s.p; s.t.localRotation = s.r; s.t.localScale = s.s;
            if (s.t.gameObject.activeSelf != s.active) s.t.gameObject.SetActive(s.active);
        }
    }

    private static Transform Bone(Animator animator, string mixamoName)
    {
        foreach (Transform t in animator.GetComponentsInChildren<Transform>(true))
            if (t.name == "mixamorig:" + mixamoName) return t;
        return null;
    }

    private static string[] FingerPaths(Transform hand)
    {
        var paths = new List<string>();
        foreach (Transform t in hand.GetComponentsInChildren<Transform>(true))
            if (t != hand) paths.Add(AnimationUtility.CalculateTransformPath(t, hand));
        return paths.ToArray();
    }

    private static Quaternion[] LocalRotations(Transform hand, string[] paths)
    {
        var result = new Quaternion[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            Transform t = hand.Find(paths[i]);
            result[i] = t != null ? t.localRotation : Quaternion.identity;
        }
        return result;
    }

    private static void SetStrings(SerializedProperty prop, string[] values)
    {
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).stringValue = values[i];
    }
}
