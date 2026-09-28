using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Poses a rigged pair of first-person arms so both hands grip the pickaxe handle.
/// Put it on the arms model. Every frame, after the swing and idle motion have moved the
/// pickaxe (and the grip points on it), it:
///   1. curls the fingers into a fist,
///   2. sets each hand's rotation from its grip point (calibrated once, so the hand turns
///      rigidly with the pickaxe instead of being re-guessed every frame),
///   3. places the wrist so the centre of the fist sits exactly on the grip point,
///   4. bends shoulder + elbow (two-bone IK, elbow toward a hint) to reach the wrist,
///   5. spreads the hand's twist along the forearm's twist bones (real forearms rotate,
///      wrists don't), so the wrist never looks wrung.
/// </summary>
[DefaultExecutionOrder(100)] // after PickaxeSwing / ViewModelMotion have moved the grip points
public class FirstPersonArmsIK : MonoBehaviour
{
    [Serializable]
    public class Arm
    {
        [Tooltip("Upper arm bone (\"bicep\").")] public Transform upperArm;
        [Tooltip("Forearm bone.")] public Transform forearm;
        [Tooltip("Hand bone (\"wrist\").")] public Transform hand;
        [Tooltip("Grip point on the handle. Up (green) runs along the handle toward the head; forward (blue) " +
                 "is the way the palm faces. Rotate it around its green axis to slide the hand around the shaft.")]
        public Transform gripTarget;
        [Tooltip("Calibration keeps the palm from facing down onto the top of the shaft: the palm must face at least this " +
                 "much upward (relative to the camera). 0 = sideways at worst, 0.3 = from underneath, -1 = no limit.")]
        [Range(-1f, 1f)] public float minPalmUp = 0f;
        [Tooltip("Which side of the shaft the hand should sit on, in camera space (x right, y up, z forward). " +
                 "E.g. (1,0,0) = the hand grips from the right side of the shaft. Zero = no preference.")]
        public Vector3 preferredHandSide = Vector3.zero;
        [Tooltip("How strongly calibration follows Preferred Hand Side.")]
        [Range(0f, 1f)] public float handSideWeight = 0f;
        [Tooltip("How strongly calibration turns the palm to face the player (the camera), so the fingers wrap " +
                 "the shaft from underneath on the player's side. 0 = off.")]
        [Range(0f, 1f)] public float palmTowardViewer = 0f;
        [Tooltip("Thumb curl for this hand only (-1 = use the shared Thumb Curl). Low = thumb lies along the shaft.")]
        [Range(-1f, 90f)] public float thumbCurlOverride = -1f;
        [Tooltip("Turns the thumb's base joint so the thumb points up the shaft toward the head (0 = off, 1 = fully).")]
        [Range(0f, 1f)] public float thumbAlongShaft = 0f;
        [HideInInspector] public Transform thumbBase;
        [HideInInspector] public Quaternion thumbBaseRest = Quaternion.identity;
        [Tooltip("The elbow bends toward this point.")] public Transform elbowHint;
        [Tooltip("First bone of the index finger (sets which way the knuckles run).")] public Transform indexKnuckle;
        [Tooltip("First bone of the little finger.")] public Transform pinkyKnuckle;
        [Tooltip("First bones of index, middle, ring, little finger: their joints define the centre of the fist.")]
        public Transform[] fistFingers;
        [Tooltip("Forearm twist bones, elbow to wrist.")] public Transform[] twistBones;
        [Tooltip("How much of the hand's twist each twist bone takes (0..1).")] public float[] twistWeights = { 0.3f, 0.65f };

        // Filled in by CaptureRestPose / CalibrateGrips.
        [HideInInspector] public Quaternion handRest = Quaternion.identity;
        [HideInInspector] public Vector3 elbowHingeLocal; // elbow hinge axis in upper-arm space (from the rest pose)
        [HideInInspector] public Quaternion[] twistRest = Array.Empty<Quaternion>();
        [HideInInspector] public Quaternion handOffset = Quaternion.identity;
        [HideInInspector] public bool calibrated;
        [NonSerialized] public Vector3 lastForearmDir;
        [NonSerialized] public Vector3 cachedGripLocal;
        [NonSerialized] public float cachedGripCurl = -1f;
    }

    [SerializeField] private Arm leftArm = new Arm();
    [SerializeField] private Arm rightArm = new Arm();

    [Header("Grip")]
    [Tooltip("How far the fingers close around the handle, in degrees per joint.")]
    [SerializeField, Range(0f, 90f)] private float fingerCurl = 65f;
    [Tooltip("How far the thumbs close, in degrees per joint.")]
    [SerializeField, Range(0f, 90f)] private float thumbCurl = 35f;
    [Tooltip("During a swing, how much each hand may roll around the handle to follow its forearm " +
             "(0 = locked to the grip point: the hands keep exactly the same grip on the shaft all swing).")]
    [SerializeField, Range(0f, 1f)] private float gripAdapt = 0f;
    [Tooltip("Largest roll around the handle that Grip Adapt may add, in degrees.")]
    [SerializeField, Range(0f, 90f)] private float maxGripAdapt = 35f;

    // Filled in by the setup tool: each finger joint, its untouched rotation, and how it curls.
    [SerializeField, HideInInspector] private Transform[] curlJoints = Array.Empty<Transform>();
    [SerializeField, HideInInspector] private Quaternion[] curlRest = Array.Empty<Quaternion>();
    [SerializeField, HideInInspector] private Vector3[] curlAxes = Array.Empty<Vector3>();
    [SerializeField, HideInInspector] private bool[] curlIsThumb = Array.Empty<bool>();

    private void LateUpdate() => Solve();

    /// <summary>Poses the arms now. Also called by the editor tool so the scene shows the grip.</summary>
    public void Solve()
    {
        ApplyFingerCurl();
        SolveArm(leftArm, adapt: true);
        SolveArm(rightArm, adapt: true);
    }

    /// <summary>Used by the setup tool to register the finger joints and their curl axes.</summary>
    public void SetCurlJoints(Transform[] joints, Vector3[] axes, bool[] isThumb)
    {
        curlJoints = joints;
        curlAxes = axes;
        curlIsThumb = isThumb;
        curlRest = Array.ConvertAll(joints, j => j.localRotation);
    }

    /// <summary>Record the model's untouched wrist and twist-bone rotations. Call before any solving.</summary>
    public void CaptureRestPose()
    {
        foreach (Arm arm in new[] { leftArm, rightArm })
        {
            if (arm.hand != null)
            {
                arm.handRest = arm.hand.localRotation;
                foreach (Transform child in arm.hand)
                    if (child.name.StartsWith("finger_thumb")) { arm.thumbBase = child; arm.thumbBaseRest = child.localRotation; break; }
            }
            arm.twistRest = arm.twistBones == null ? Array.Empty<Quaternion>()
                : Array.ConvertAll(arm.twistBones, b => b != null ? b.localRotation : Quaternion.identity);
            if (IsValid(arm))
            {
                // The rest pose has a bent elbow: the axis it bends around is the hinge.
                Vector3 hinge = Vector3.Cross(arm.forearm.position - arm.upperArm.position,
                                              arm.hand.position - arm.forearm.position);
                arm.elbowHingeLocal = arm.upperArm.InverseTransformDirection(hinge.normalized);
            }
        }
    }

    /// <summary>
    /// Works out a natural wrap-around grip for each hand, turns its grip point so that
    /// forward (blue) is the way the palm faces, and stores the hand relative to it.
    /// From then on the hand follows the grip point rigidly.
    /// </summary>
    [ContextMenu("Recalibrate Grips")]
    public void CalibrateGrips()
    {
        ApplyFingerCurl();
        Transform view = transform.parent;
        Vector3 viewUp = view != null ? view.up : Vector3.up;
        foreach (Arm arm in new[] { leftArm, rightArm })
        {
            if (!IsValid(arm)) continue;
            arm.calibrated = false;
            Vector3 wantSide = arm.preferredHandSide.sqrMagnitude > 1e-6f
                ? (view != null ? view.TransformDirection(arm.preferredHandSide) : arm.preferredHandSide).normalized
                : Vector3.zero;

            // Every hand rotation that grips the handle (knuckles along it) is one of these:
            // a roll around the handle, thumb-side toward the head or (reverse grip) toward the end.
            // Try them all; keep the one with the most relaxed wrist whose palm wraps the shaft
            // from the side or underneath instead of sitting on top of it.
            Vector3 handleAxis = arm.gripTarget.up;
            Vector3 guess = (arm.gripTarget.position - arm.upperArm.position).normalized;
            Quaternion best = NaturalHandRotation(arm, guess, false);
            float bestCost = float.MaxValue;
            foreach (bool reverse in new[] { false, true })
            {
                Quaternion baseRot = NaturalHandRotation(arm, guess, reverse);
                for (float roll = -180f; roll < 180f; roll += 5f)
                {
                    Quaternion rot = Quaternion.AngleAxis(roll, handleAxis) * baseRot;
                    PlaceArm(arm, rot);
                    WristAngles(arm, out float bend, out float twist);
                    float palmUp = Vector3.Dot(PalmDirection(arm), viewUp);
                    float cost = bend
                               + Mathf.Max(0f, Mathf.Abs(twist) - 60f) * 1.5f
                               + (reverse ? 15f + 200f * arm.thumbAlongShaft : 0f) // a reverse grip puts the thumb toward the end
                               + Mathf.Max(0f, arm.minPalmUp - palmUp) * 300f   // never palm-down on top of the shaft
                               + (1f - palmUp) * 8f * (1f - arm.handSideWeight); // gently prefer wrapping from below
                    if (arm.palmTowardViewer > 0f && view != null)
                    {
                        Vector3 toViewer = Vector3.ProjectOnPlane(view.position - arm.gripTarget.position, handleAxis).normalized;
                        cost += (1f - Vector3.Dot(PalmDirection(arm), toViewer)) * 80f * arm.palmTowardViewer;
                    }
                    if (wantSide != Vector3.zero)
                    {
                        // Where the hand's body sits around the shaft (shaft centre → wrist).
                        Vector3 side = Vector3.ProjectOnPlane(arm.hand.position - arm.gripTarget.position, handleAxis).normalized;
                        cost += (1f - Vector3.Dot(side, wantSide)) * 80f * arm.handSideWeight
                              + Mathf.Max(0f, Vector3.Dot(side, viewUp) - 0.3f) * 200f; // never on top of the shaft
                    }
                    if (cost < bestCost) { bestCost = cost; best = rot; }
                }
            }

            // Turn the grip point so its axes describe the grip: up = along the handle,
            // forward = the way the palm faces. Then lock the hand to it.
            PlaceArm(arm, best);
            arm.gripTarget.rotation = Quaternion.LookRotation(PalmDirection(arm), handleAxis);
            arm.handOffset = Quaternion.Inverse(arm.gripTarget.rotation) * best;
            arm.calibrated = true;
            arm.lastForearmDir = (arm.hand.position - arm.forearm.position).normalized;
        }
        SolveArm(leftArm, adapt: false);
        SolveArm(rightArm, adapt: false);
    }

    /// <summary>
    /// Which way the palm faces, perpendicular to the handle: from the knuckle line toward the
    /// centre of the curled fingers (fingers always curl toward the palm).
    /// </summary>
    public Vector3 PalmDirection(bool left) => PalmDirection(left ? leftArm : rightArm);

    private static Vector3 PalmDirection(Arm arm)
    {
        Vector3 knuckles = (arm.indexKnuckle.position + arm.pinkyKnuckle.position) * 0.5f;
        return Vector3.ProjectOnPlane(FistCentre(arm) - knuckles, arm.gripTarget.up).normalized;
    }

    /// <summary>
    /// Wrist angles relative to the model's rest pose, for checking poses.
    /// twist = rotation around the forearm (shown along the forearm by the twist bones),
    /// bend = everything else (what the wrist joint itself shows).
    /// </summary>
    public void GetWristAngles(bool left, out float bend, out float twist) =>
        WristAngles(left ? leftArm : rightArm, out bend, out twist);

    private static void WristAngles(Arm arm, out float bend, out float twist)
    {
        Quaternion delta = arm.hand.localRotation * Quaternion.Inverse(arm.handRest);
        twist = TwistAngle(delta);
        Quaternion twistOnly = Quaternion.AngleAxis(twist, Vector3.up);
        bend = Quaternion.Angle(twistOnly, delta);
    }

    private void ApplyFingerCurl()
    {
        for (int i = 0; i < curlJoints.Length; i++)
        {
            if (curlJoints[i] == null) continue;
            float angle = curlIsThumb[i] ? ThumbCurlFor(curlJoints[i]) : fingerCurl;
            curlJoints[i].localRotation = curlRest[i] * Quaternion.AngleAxis(angle, curlAxes[i]);
        }
    }

    private float ThumbCurlFor(Transform joint)
    {
        foreach (Arm arm in new[] { leftArm, rightArm })
            if (arm.hand != null && joint.IsChildOf(arm.hand))
                return arm.thumbCurlOverride >= 0f ? arm.thumbCurlOverride : thumbCurl;
        return thumbCurl;
    }

    private float ThumbCurl(Arm arm) => arm.thumbCurlOverride >= 0f ? arm.thumbCurlOverride : thumbCurl;

    /// <summary>Direction the thumb points (from its middle joint to its tip).</summary>
    private static Vector3 ThumbDirection(Arm arm)
    {
        Transform thumb1 = null;
        foreach (Transform child in arm.hand)
            if (child.name.StartsWith("finger_thumb")) { thumb1 = child; break; }
        if (thumb1 == null || thumb1.childCount == 0) return Vector3.zero;
        Transform mid = thumb1.GetChild(0), tip = mid;
        while (tip.childCount > 0) tip = tip.GetChild(0);
        return (tip.position - mid.position).normalized;
    }

    /// <summary>How well the thumb points up the handle toward the head (1 = exactly along it).</summary>
    public float ThumbAlongHandle(bool left)
    {
        Arm arm = left ? leftArm : rightArm;
        return Vector3.Dot(ThumbDirection(arm), arm.gripTarget.up);
    }

    private static bool IsValid(Arm arm) =>
        arm.upperArm != null && arm.forearm != null && arm.hand != null && arm.gripTarget != null
        && arm.indexKnuckle != null && arm.pinkyKnuckle != null;

    private void SolveArm(Arm arm, bool adapt)
    {
        if (!IsValid(arm)) return;
        if (arm.thumbBase != null) arm.thumbBase.localRotation = arm.thumbBaseRest; // fist shape measured with the thumb at rest

        Quaternion handRotation;
        if (arm.calibrated)
        {
            handRotation = arm.gripTarget.rotation * arm.handOffset;

            // Let the hand roll a little around the handle toward its forearm, as a real
            // grip slides during a swing. Rolling around the handle keeps the fist wrapped.
            if (adapt && gripAdapt > 0f && arm.lastForearmDir != Vector3.zero)
            {
                Vector3 axis = arm.gripTarget.up;
                Vector3 fingersNow = handRotation * Vector3.up;
                Vector3 fingersWanted = NaturalHandRotation(arm, arm.lastForearmDir) * Vector3.up;
                float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(fingersNow, axis),
                                                 Vector3.ProjectOnPlane(fingersWanted, axis), axis);
                roll = Mathf.Clamp(roll * gripAdapt, -maxGripAdapt, maxGripAdapt);
                handRotation = Quaternion.AngleAxis(roll, axis) * handRotation;
            }
        }
        else
        {
            Vector3 dir = arm.lastForearmDir != Vector3.zero ? arm.lastForearmDir
                : (arm.gripTarget.position - arm.upperArm.position).normalized;
            handRotation = NaturalHandRotation(arm, dir);
        }

        PlaceArm(arm, handRotation);
        DistributeTwist(arm);
        AimThumb(arm);
        arm.lastForearmDir = (arm.hand.position - arm.forearm.position).normalized;
    }

    /// <summary>
    /// Points the thumb up the shaft (toward the head) by turning its base joint. Starts from
    /// the rest rotation every frame, so the adjustment never accumulates.
    /// </summary>
    private static void AimThumb(Arm arm)
    {
        if (arm.thumbBase == null) return;
        arm.thumbBase.localRotation = arm.thumbBaseRest;
        if (arm.thumbAlongShaft <= 0f || arm.thumbBase.childCount == 0) return;

        Transform tip = arm.thumbBase;
        while (tip.childCount > 0) tip = tip.GetChild(0);
        Vector3 current = tip.position - arm.thumbBase.position;
        Vector3 wanted = Vector3.Slerp(current.normalized, arm.gripTarget.up, arm.thumbAlongShaft) * current.magnitude;
        arm.thumbBase.rotation = Quaternion.FromToRotation(current, wanted) * arm.thumbBase.rotation;
    }

    /// <summary>
    /// Hand rotation with the knuckles along the handle (index finger toward the head, like
    /// holding a bat) and the fingers continuing <paramref name="forearmDir"/> as closely as the
    /// grip allows, so the wrist stays straight.
    /// </summary>
    private static Quaternion NaturalHandRotation(Arm arm, Vector3 forearmDir, bool reverseGrip = false)
    {
        Transform hand = arm.hand;
        Vector3 handleAxis = arm.gripTarget.up;
        Quaternion toHand = Quaternion.Inverse(hand.rotation);
        Vector3 localKnuckles = toHand * (arm.pinkyKnuckle.position - arm.indexKnuckle.position);
        Vector3 localFingers = Vector3.up; // the hand bone points along the fingers

        Vector3 fingersWorld = Vector3.ProjectOnPlane(forearmDir, handleAxis);
        if (fingersWorld.sqrMagnitude < 1e-6f) fingersWorld = Vector3.ProjectOnPlane(Vector3.forward, handleAxis);
        fingersWorld.Normalize();

        Quaternion localFrame = Quaternion.LookRotation(localFingers, Vector3.ProjectOnPlane(localKnuckles, localFingers));
        Quaternion worldFrame = Quaternion.LookRotation(fingersWorld, reverseGrip ? handleAxis : -handleAxis);
        return worldFrame * Quaternion.Inverse(localFrame);
    }

    /// <summary>Sets the hand to <paramref name="handRotation"/> and bends the arm so the fist centre lands on the grip.</summary>
    private void PlaceArm(Arm arm, Quaternion handRotation)
    {
        Transform hand = arm.hand, upper = arm.upperArm, lower = arm.forearm;

        // Where the centre of the fist's hole sits relative to the wrist (unchanged by the arm's pose).
        Vector3 localGrip = GripHoleLocal(arm);
        Vector3 wristTarget = arm.gripTarget.position - handRotation * localGrip;

        float upperLen = Vector3.Distance(upper.position, lower.position);
        float lowerLen = Vector3.Distance(lower.position, hand.position);
        Vector3 toTarget = wristTarget - upper.position;
        float dist = Mathf.Clamp(toTarget.magnitude, 0.001f, upperLen + lowerLen - 0.0001f);
        Vector3 dir = toTarget.normalized;

        // Elbow position from the law of cosines, bent toward the hint (never backward).
        float cosShoulder = Mathf.Clamp((upperLen * upperLen + dist * dist - lowerLen * lowerLen) / (2f * upperLen * dist), -1f, 1f);
        Vector3 hint = arm.elbowHint != null ? arm.elbowHint.position - upper.position : -Vector3.up;
        Vector3 bendDir = Vector3.ProjectOnPlane(hint, dir).normalized;
        if (bendDir.sqrMagnitude < 1e-6f) bendDir = Vector3.ProjectOnPlane(Vector3.down, dir).normalized;
        Vector3 elbow = upper.position + dir * (cosShoulder * upperLen)
                      + bendDir * (Mathf.Sqrt(1f - cosShoulder * cosShoulder) * upperLen);

        // Aim the upper arm at the elbow position...
        upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, elbow - upper.position) * upper.rotation;

        // ...then roll it around its own length so the elbow's hinge lines up with the bend.
        // Without this the aim above leaves the upper arm rolled arbitrarily and the elbow
        // (and everything below it) looks twisted.
        if (arm.elbowHingeLocal != Vector3.zero)
        {
            Vector3 upperAxis = (elbow - upper.position).normalized;
            Vector3 bendNormal = Vector3.Cross(elbow - upper.position, wristTarget - elbow);
            if (bendNormal.sqrMagnitude > 1e-8f)
            {
                Vector3 hingeNow = upper.TransformDirection(arm.elbowHingeLocal);
                float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(hingeNow, upperAxis),
                                                 Vector3.ProjectOnPlane(bendNormal, upperAxis), upperAxis);
                upper.rotation = Quaternion.AngleAxis(roll, upperAxis) * upper.rotation;
            }
        }

        // The forearm now only needs to bend around the hinge to reach the wrist target.
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, wristTarget - lower.position) * lower.rotation;
        hand.rotation = handRotation;
    }

    /// <summary>Spreads the hand's twist (around the forearm) along the forearm twist bones.</summary>
    private static void DistributeTwist(Arm arm)
    {
        if (arm.twistBones == null || arm.twistRest == null || arm.twistRest.Length != arm.twistBones.Length) return;
        float twist = TwistAngle(arm.hand.localRotation * Quaternion.Inverse(arm.handRest));
        for (int i = 0; i < arm.twistBones.Length; i++)
        {
            if (arm.twistBones[i] == null) continue;
            float w = arm.twistWeights != null && i < arm.twistWeights.Length ? arm.twistWeights[i] : 0.5f;
            arm.twistBones[i].localRotation = arm.twistRest[i] * Quaternion.AngleAxis(twist * w, Vector3.up);
        }
    }

    /// <summary>Signed rotation (degrees) of a forearm-space rotation around the forearm's own axis (local Y).</summary>
    private static float TwistAngle(Quaternion q)
    {
        float angle = 2f * Mathf.Atan2(q.y, q.w) * Mathf.Rad2Deg;
        if (angle > 180f) angle -= 360f;
        if (angle < -180f) angle += 360f;
        return angle;
    }

    /// <summary>
    /// The centre of the hole the curled fingers and thumb make, in the hand's frame (relative
    /// to the wrist). This is where the handle's centreline goes. Found as the point, across
    /// the knuckle line, that is furthest from every finger/thumb joint — so the handle sits
    /// in the middle of the fist instead of pushing through a finger. Cached per curl amount.
    /// </summary>
    private Vector3 GripHoleLocal(Arm arm)
    {
        float key = fingerCurl * 1000f + ThumbCurl(arm);
        if (Mathf.Approximately(arm.cachedGripCurl, key)) return arm.cachedGripLocal;

        Transform hand = arm.hand;
        Quaternion toHand = Quaternion.Inverse(hand.rotation);
        var joints = new List<Vector3>(24);
        foreach (Transform finger in arm.fistFingers)
            for (Transform j = finger; j != null; j = j.childCount > 0 ? j.GetChild(0) : null)
                joints.Add(toHand * (j.position - hand.position));
        Transform thumb = arm.indexKnuckle != null ? arm.indexKnuckle.parent : null; // wrist: thumb is a sibling chain
        if (thumb != null)
            foreach (Transform child in thumb)
                if (child.name.StartsWith("finger_thumb"))
                    for (Transform j = child.childCount > 0 ? child.GetChild(0) : null; j != null; j = j.childCount > 0 ? j.GetChild(0) : null)
                        joints.Add(toHand * (j.position - hand.position)); // thumb tip joints only

        Vector3 centre = toHand * (FistCentre(arm) - hand.position);
        Vector3 axis = (toHand * (arm.pinkyKnuckle.position - arm.indexKnuckle.position)).normalized;
        Vector3 u = Vector3.Cross(axis, Vector3.up);
        if (u.sqrMagnitude < 1e-6f) u = Vector3.Cross(axis, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(axis, u);

        Vector3 best = centre;
        float bestClearance = Clearance(centre, axis, joints);
        const int steps = 12;
        const float step = 0.0015f;
        for (int i = -steps; i <= steps; i++)
            for (int k = -steps; k <= steps; k++)
            {
                Vector3 p = centre + (u * i + v * k) * step;
                float c = Clearance(p, axis, joints);
                if (c > bestClearance) { bestClearance = c; best = p; }
            }

        arm.cachedGripCurl = key;
        arm.cachedGripLocal = best;
        return best;
    }

    /// <summary>Distance from a line (through p, along axis) to the nearest joint.</summary>
    private static float Clearance(Vector3 p, Vector3 axis, List<Vector3> joints)
    {
        float min = float.MaxValue;
        foreach (Vector3 j in joints)
            min = Mathf.Min(min, Vector3.ProjectOnPlane(j - p, axis).magnitude);
        return min;
    }

    private static Vector3 FistCentre(Arm arm)
    {
        // A curled finger forms a loop; the average of its joints is roughly the loop's centre.
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (Transform finger in arm.fistFingers)
        {
            for (Transform joint = finger; joint != null; joint = joint.childCount > 0 ? joint.GetChild(0) : null)
            {
                sum += joint.position;
                count++;
            }
        }
        return count > 0 ? sum / count : arm.hand.position;
    }
}
