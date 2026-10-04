using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// What OTHER players see of a player's hands: the held item in the right hand, the arms holding it, and
/// short actions (punch, mining swing, shot, reload, equip). Lives on the NetworkPlayer prefab and only runs on
/// remote copies; the local player keeps its first-person views and never sees this.
///
/// Nothing here is sent per frame. NetworkPlayerAvatar tells it which item is held (a synced item index) and
/// when an action happens (a small event); everything else is computed here:
///   * Hold poses are BAKED from the first-person views by Ore What > Multiplayer > Set Up Multiplayer: each
///     view is solved once with the real arm IK, and the hands / elbows / finger joints are stored relative to
///     the shoulders and the look direction. So a remote player holds a tool exactly the way its owner's own
///     (third-person) skeleton does, tilting with where they look.
///   * Item models are copies of the first-person models (same mesh, muzzle flash), already placed under the
///     right hand bone in the prefab and simply switched on.
///   * Actions are short procedural motions applied on top (not replays of the first-person animation).
/// Purely visual: nothing here can deal damage or change the game.
/// </summary>
[DefaultExecutionOrder(70)] // after the Animator and NetworkPlayerAvatar's look tilt (60), before the Headlamp (95)
public class RemotePlayerPresentation : MonoBehaviour
{
    /// <summary>How one item is held (baked). Positions/rotations are in "look space": origin between the shoulders,
    /// axes = where the player looks (right, up, forward).</summary>
    [Serializable]
    public class HoldPose
    {
        public ItemData item;
        [Tooltip("The item model, a child of the right hand bone (inactive until held). Empty for empty hands.")]
        public GameObject visual;
        public Vector3 rightHand, leftHand, rightElbow, leftElbow;
        public Quaternion rightRotation = Quaternion.identity, leftRotation = Quaternion.identity;
        public Quaternion[] rightFingers = new Quaternion[0], leftFingers = new Quaternion[0];
        [Tooltip("Guns: how hard each shot kicks the arms up (degrees). 0 = not a gun.")]
        public float shotKick;
        public bool rifleSounds;

        [Tooltip("Mining tools: the first-person swings (0 = Right, 1 = Left, 2 = Overhead), sampled over time.")]
        public ArmClip[] swings = new ArmClip[0];
        [Tooltip("Guns: the first-person reload, sampled over its progress (0..1).")]
        public ArmClip reload = new ArmClip();
        [Tooltip("Rig bones of the item model the reload moves (magazine, slide...), paths under Visual.")]
        public string[] reloadBones = new string[0];
        [Tooltip("Points on the item, in the right hand bone's space: kept outside the head during swings.")]
        public Vector3[] clearancePoints = new Vector3[0];
    }

    /// <summary>One baked moment of a first-person action: both arms in look space (+ rig bone positions for reloads).</summary>
    [Serializable]
    public class ArmSample
    {
        public Vector3 rightHand, leftHand, rightElbow, leftElbow;
        public Quaternion rightRotation = Quaternion.identity, leftRotation = Quaternion.identity;
        public Vector3[] bones = new Vector3[0];
    }

    /// <summary>A baked first-person action: evenly spaced samples over Duration (seconds; reloads use 1 = the whole reload).</summary>
    [Serializable]
    public class ArmClip
    {
        public float duration;
        public float impact;
        public ArmSample[] samples = new ArmSample[0];
        public bool IsEmpty => samples == null || samples.Length < 2 || duration <= 0f;
    }

    [Header("Skeleton (set by the setup tool)")]
    [SerializeField] private Transform rightUpperArm, rightForearm, rightHandBone;
    [SerializeField] private Transform leftUpperArm, leftForearm, leftHandBone;
    [SerializeField] private string[] rightFingerPaths = new string[0], leftFingerPaths = new string[0];

    [Header("Baked poses (set by the setup tool)")]
    [SerializeField] private HoldPose[] holds = new HoldPose[0];
    [Tooltip("Unarmed punch: the fists at the guard, and at full extension (both from FistsController's own poses).")]
    [SerializeField] private HoldPose fists = new HoldPose();
    [SerializeField] private HoldPose fistsStrike = new HoldPose();

    [Header("Head clearance (set by the setup tool)")]
    [Tooltip("Skull + hardhat as an ellipsoid fitted to the meshes (neck left out), in the head bone's frame (metres). " +
             "Held tools are kept outside it.")]
    [SerializeField] private Transform headBone;
    [SerializeField] private Vector3 headCentre = new Vector3(0f, 0.12f, 0.02f);
    [SerializeField] private Vector3 headRadii = new Vector3(0.2f, 0.2f, 0.2f);
    [SerializeField, Min(0f)] private float headMargin = 0.03f;

    [Header("Motion")]
    [SerializeField, Min(0.01f)] private float equipTime = 0.25f;
    [SerializeField, Min(0.01f)] private float unequipTime = 0.2f;
    [Tooltip("Blend between the hold, a swing and a reload (and into a new swing started before the last one ended), seconds.")]
    [SerializeField, Min(0.01f)] private float transitionTime = 0.12f;

    /// <summary>Up/down look angle (degrees, + = down), set every frame by NetworkPlayerAvatar.</summary>
    public float Pitch { get; set; }

    // Read-only, for debugging and tests: what is shown right now.
    public ItemData HeldItem => held != null ? held.item : null;
    public float PunchTime => punchTime;
    public float SwingTime => swingTime;
    public float SwingImpactTime => swingImpact;
    public int SwingKind => swingKind;
    public float ShotTime => shotTime;
    public float ReloadProgress => reloadTime < 0f ? -1f : reloadTime / reloadDuration;

    private HoldPose held;            // what's in the hands (null = empty)
    private float holdWeight;         // 0 = arms follow the Animator, 1 = holding pose
    private bool carryingOnly;        // hands hold a carried world object (no item model)
    private Transform[] rightFingers, leftFingers;
    private readonly System.Collections.Generic.Dictionary<HoldPose, Transform[]> reloadBoneCache =
        new System.Collections.Generic.Dictionary<HoldPose, Transform[]>();

    // Actions (time since they started; < 0 = not playing).
    private float punchTime = -1f; private bool punchRight;
    private float swingTime = -1f; private int swingKind; private float swingImpact, swingEnd;
    private float shotTime = -1f;
    private float reloadTime = -1f, reloadDuration = 1f;
    private float muzzleLightTimer;
    private AudioSource audioSource;

    private void Awake()
    {
        rightFingers = FindAll(rightHandBone, rightFingerPaths);
        leftFingers = FindAll(leftHandBone, leftFingerPaths);
        foreach (HoldPose h in holds)
            if (h.visual != null) h.visual.SetActive(false);
        foreach (HoldPose h in holds)
            if (h.visual != null && h.reloadBones != null && h.reloadBones.Length > 0)
                reloadBoneCache[h] = h.reloadBones.Select(p => h.visual.transform.Find(p)).ToArray();
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // heard from where the player is
        audioSource.minDistance = 2f;
        audioSource.maxDistance = 60f;
    }

    // ------------------------------------------------------------------ what NetworkPlayerAvatar calls

    /// <summary>Shows this item in the hands (null = empty hands). carrying = hands around a carried world object.</summary>
    public void SetHeldItem(ItemData item, bool carrying)
    {
        HoldPose next = null;
        if (carrying) next = FirstNonEquippableHold();
        else if (item != null) next = Find(item);
        if (next == held && carrying == carryingOnly) return;

        if (held != null && held.visual != null) held.visual.SetActive(false);
        if (held != null) ResetReloadBones(held);
        held = next;
        carryingOnly = carrying;
        if (held != null && held.visual != null && !carrying) held.visual.SetActive(true);
        holdWeight = 0f; // raise the new item into the hands (or let the arms relax)
        if (held != null) CopyPose(HoldSample(held), shown); // a new item starts from its own hold (the raise covers the change)
        blendTime = -1f;
        shownSource = 0;
        actionRestarted = false;
        swingTime = shotTime = reloadTime = -1f;
        if (held != null) punchTime = -1f;
    }

    public void Punch(bool right)
    {
        if (held != null) return; // punches are only thrown with empty hands
        punchRight = right;
        punchTime = 0f;
    }

    public void Swing(int kind, float impactTime, float duration)
    {
        swingKind = kind;
        swingImpact = Mathf.Max(0.05f, impactTime);
        swingEnd = Mathf.Max(swingImpact + 0.1f, duration);
        swingTime = 0f;
        actionRestarted = true;
    }

    /// <summary>The player fired: kick, muzzle flash, sound, and a tracer from this gun's muzzle to where the shot ended.</summary>
    public void Shot(Vector3 end)
    {
        shotTime = 0f;
        if (held == null || held.visual == null) return;
        ParticleSystem[] flashes = held.visual.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in flashes) ps.Emit(2);
        // The muzzle flash sits on the muzzle (copied from the first-person gun), so the tracer starts there.
        BulletTracers.Play(flashes.Length > 0 ? flashes[0].transform.position : held.visual.transform.position, end, held.rifleSounds);
        foreach (Light l in held.visual.GetComponentsInChildren<Light>(true)) l.enabled = true;
        muzzleLightTimer = 0.05f;
        audioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
        audioSource.PlayOneShot(WeaponSounds.Shot(held.rifleSounds), 0.8f);
    }

    public void Reload(float duration)
    {
        reloadDuration = Mathf.Max(0.2f, duration);
        reloadTime = 0f;
        actionRestarted = true;
        shotTime = -1f;
    }

    // ------------------------------------------------------------------ every frame

    private void LateUpdate()
    {
        if (rightHandBone == null || leftHandBone == null || rightUpperArm == null || leftUpperArm == null) return;
        float dt = Time.deltaTime;
        TickMuzzleLight(dt);

        float target = held != null ? 1f : 0f;
        holdWeight = Mathf.MoveTowards(holdWeight, target, dt / (target > holdWeight ? equipTime : unequipTime));

        Vector3 shoulders = (leftUpperArm.position + rightUpperArm.position) * 0.5f;
        Quaternion look = transform.rotation * Quaternion.Euler(Pitch, 0f, 0f);

        if (held != null) PoseHold(held, shoulders, look, Smooth(holdWeight), dt);
        else if (punchTime >= 0f) PosePunch(shoulders, look, dt);
    }

    private void PoseHold(HoldPose h, Vector3 shoulders, Quaternion look, float weight, float dt)
    {
        // What the arms should do now, in look space: the hold, or a sample of the swing / reload being played.
        ArmSample pose = null;
        int source = 0; // 0 = hold, 1 = swing, 2 = reload

        // Mining swing: the first-person swing's own arm poses (baked), timed from its start, through its own smooth
        // return to the resting hold (which is exactly what the hold pose is).
        if (swingTime >= 0f)
        {
            swingTime += dt;
            ArmClip clip = swingKind >= 0 && swingKind < h.swings.Length ? h.swings[swingKind] : null;
            if (clip == null || clip.IsEmpty || swingTime > clip.duration) swingTime = -1f;
            else { pose = Sample(clip, swingTime); source = 1; }
        }

        // Reload: the first-person reload's own arm poses and magazine / slide movement (baked), over its duration.
        if (reloadTime >= 0f)
        {
            reloadTime += dt;
            float u = reloadTime / reloadDuration;
            if (u >= 1f || h.reload.IsEmpty) { reloadTime = -1f; ResetReloadBones(h); }
            else { pose = Sample(h.reload, u); source = 2; ApplyReloadBones(h, pose); }
        }
        if (pose == null) pose = HoldSample(h);

        // Whenever the arms switch between hold, swing and reload (or a new swing starts mid-way through the last one),
        // blend everything (both hands, both elbows, the tool in the hand) from where it was shown, over a moment.
        if (source != shownSource || actionRestarted)
        {
            CopyPose(shown, blendFrom);
            blendTime = 0f;
            shownSource = source;
            actionRestarted = false;
        }
        if (blendTime >= 0f)
        {
            blendTime += dt;
            float k = Smooth(blendTime / transitionTime);
            pose = BlendPose(blendFrom, pose, k, blended);
            if (k >= 1f) blendTime = -1f;
        }
        CopyPose(pose, shown);

        // The pose follows the look pitch by turning around a point between the shoulders:
        //   looking down: at the height of the item's grip hand at rest, so low holds (tools, rifle) tip in the hands and
        //     stay in front (turned around the shoulders, low hands swing back through the body);
        //   looking up: at the shoulders, so the hands rise AND move forward, away from the chest (around a low point
        //     they would rise into the chest and the elbows would fold back through the body).
        // Both give the same pose when looking straight ahead, so there is no jump between them. An arm held out level
        // (pistol) turns at the shoulder either way.
        Quaternion body = transform.rotation;
        Quaternion tilt = Quaternion.AngleAxis(Pitch, body * Vector3.right);
        bool lookingDown = Pitch > 0f;
        Vector3 lookPivot = shoulders + body * new Vector3(0f, lookingDown ? Mathf.Min(0f, h.rightHand.y) : 0f, 0f);
        Vector3 Place(Vector3 local) => lookPivot + tilt * (shoulders + body * local - lookPivot);
        // A support hand that isn't on the item (the pistol's, parked low) turns around a point at its own height instead,
        // and only when looking down: looking up it keeps its level pose (raising it with the aim swung it across the chest).
        bool leftOnItem = Vector3.Distance(h.rightHand, h.leftHand) < 0.4f;
        Vector3 leftPivot = leftOnItem ? lookPivot : shoulders + body * new Vector3(0f, Mathf.Min(0f, h.leftHand.y), 0f);
        Quaternion leftTilt = leftOnItem || lookingDown ? tilt : Quaternion.identity;
        Vector3 PlaceLeft(Vector3 local) => leftPivot + leftTilt * (shoulders + body * local - leftPivot);
        Vector3 rPos = Place(pose.rightHand), lPos = PlaceLeft(pose.leftHand);
        Quaternion rRot = look * pose.rightRotation, lRot = leftTilt * body * pose.leftRotation; // tilt * body == look
        Vector3 rElbow = Place(pose.rightElbow), lElbow = PlaceLeft(pose.leftElbow);

        // Raising into the hands: start lower and come up.
        Vector3 lift = look * Vector3.down * (0.3f * (1f - weight));
        rPos += lift; lPos += lift;

        // Shot: a quick kick up and back around the right hand.
        if (shotTime >= 0f)
        {
            shotTime += dt;
            float k = Mathf.Exp(-shotTime / 0.06f);
            Quaternion q = look * Quaternion.Euler(-h.shotKick * k, 0f, 0f) * Quaternion.Inverse(look);
            Vector3 pivot = rPos, back = look * Vector3.back * (0.035f * k);
            Turn(q, pivot, ref rPos, ref rRot, ref rElbow);
            Turn(q, pivot, ref lPos, ref lRot, ref lElbow);
            rPos += back; lPos += back;
            if (shotTime > 0.4f) shotTime = -1f;
        }

        // Keep the tool out of the head (the poses come from a first-person layout; looking up tilts them toward it).
        KeepClearOfHead(h, look, ref rPos, ref lPos, ref rElbow, ref lElbow, rRot);

        // Both arms always follow the baked layout (one-handed items park the support hand where first person does).
        DebugRightTarget = rPos; DebugRightHint = rElbow;
        SolveArm(rightUpperArm, rightForearm, rightHandBone, rPos, rRot, rElbow, weight);
        ApplyFingers(rightFingers, h.rightFingers, weight);
        // The item sits in the right hand, so the left hand goes where it is on the item: relative to where the right
        // hand actually got to (if the right arm couldn't quite reach, the left hand still stays on the handle).
        if (weight > 0.999f && leftOnItem)
        {
            Quaternion toActual = rightHandBone.rotation * Quaternion.Inverse(rRot);
            lPos = rightHandBone.position + toActual * (lPos - rPos);
            lRot = toActual * lRot;
        }
        SolveArm(leftUpperArm, leftForearm, leftHandBone, lPos, lRot, lElbow, weight);
        ApplyFingers(leftFingers, h.leftFingers, weight);
    }

    /// <summary>Unarmed punch: from the relaxed arm up to the guard, out, and back down. Only the punching arm moves.</summary>
    private void PosePunch(Vector3 shoulders, Quaternion look, float dt)
    {
        punchTime += dt;
        // Same timing as FistsController (raise, strike, hold, recover).
        const float up = 0.1f, strike = 0.09f, hold = 0.04f, down = 0.24f;
        float t = punchTime, w, reach;
        if (t < up) { w = Smooth(t / up); reach = 0f; }
        else if (t < up + strike) { w = 1f; float u = (t - up) / strike; reach = 1f - (1f - u) * (1f - u) * (1f - u); }
        else if (t < up + strike + hold) { w = 1f; reach = 1f; }
        else if (t < up + strike + hold + down) { float u = (t - up - strike - hold) / down; w = 1f - Smooth(u); reach = 1f - Smooth(u); }
        else { punchTime = -1f; return; }

        // Between the baked guard and full-extension poses.
        HoldPose g = fists, s = fistsStrike;
        if (punchRight)
        {
            SolveArm(rightUpperArm, rightForearm, rightHandBone, shoulders + look * Vector3.Lerp(g.rightHand, s.rightHand, reach),
                     look * Quaternion.Slerp(g.rightRotation, s.rightRotation, reach), shoulders + look * Vector3.Lerp(g.rightElbow, s.rightElbow, reach), w);
            ApplyFingers(rightFingers, g.rightFingers, w);
        }
        else
        {
            SolveArm(leftUpperArm, leftForearm, leftHandBone, shoulders + look * Vector3.Lerp(g.leftHand, s.leftHand, reach),
                     look * Quaternion.Slerp(g.leftRotation, s.leftRotation, reach), shoulders + look * Vector3.Lerp(g.leftElbow, s.leftElbow, reach), w);
            ApplyFingers(leftFingers, g.leftFingers, w);
        }
    }

    /// <summary>The baked sample at time t (linear between neighbours).</summary>
    private static ArmSample Sample(ArmClip clip, float t)
    {
        int n = clip.samples.Length;
        float f = Mathf.Clamp01(t / clip.duration) * (n - 1);
        int i = Mathf.Min((int)f, n - 2);
        float k = f - i;
        ArmSample a = clip.samples[i], b = clip.samples[i + 1];
        var s = scratchSample;
        s.rightHand = Vector3.Lerp(a.rightHand, b.rightHand, k);
        s.leftHand = Vector3.Lerp(a.leftHand, b.leftHand, k);
        s.rightElbow = Vector3.Lerp(a.rightElbow, b.rightElbow, k);
        s.leftElbow = Vector3.Lerp(a.leftElbow, b.leftElbow, k);
        s.rightRotation = Quaternion.Slerp(a.rightRotation, b.rightRotation, k);
        s.leftRotation = Quaternion.Slerp(a.leftRotation, b.leftRotation, k);
        int m = Mathf.Min(a.bones.Length, b.bones.Length);
        if (s.bones.Length != m) s.bones = new Vector3[m];
        for (int j = 0; j < m; j++) s.bones[j] = Vector3.Lerp(a.bones[j], b.bones[j], k);
        return s;
    }
    private static readonly ArmSample scratchSample = new ArmSample();

    /// <summary>Moves the item model's rig bones (magazine, slide...) to a reload sample.</summary>
    private void ApplyReloadBones(HoldPose h, ArmSample s)
    {
        if (!reloadBoneCache.TryGetValue(h, out Transform[] bones)) return;
        for (int j = 0; j < bones.Length && j < s.bones.Length; j++)
            if (bones[j] != null) bones[j].localPosition = s.bones[j];
    }

    /// <summary>The hold pose as a sample (look space).</summary>
    private ArmSample HoldSample(HoldPose h)
    {
        ArmSample s = holdScratch;
        s.rightHand = h.rightHand; s.leftHand = h.leftHand; s.rightElbow = h.rightElbow; s.leftElbow = h.leftElbow;
        s.rightRotation = h.rightRotation; s.leftRotation = h.leftRotation;
        return s;
    }

    private static void CopyPose(ArmSample from, ArmSample to)
    {
        to.rightHand = from.rightHand; to.leftHand = from.leftHand; to.rightElbow = from.rightElbow; to.leftElbow = from.leftElbow;
        to.rightRotation = from.rightRotation; to.leftRotation = from.leftRotation;
    }

    /// <summary>Everything (both hands, both elbows) between two poses, so the hands stay on the tool all the way.</summary>
    private static ArmSample BlendPose(ArmSample a, ArmSample b, float k, ArmSample into)
    {
        into.rightHand = Vector3.Lerp(a.rightHand, b.rightHand, k);
        into.leftHand = Vector3.Lerp(a.leftHand, b.leftHand, k);
        into.rightElbow = Vector3.Lerp(a.rightElbow, b.rightElbow, k);
        into.leftElbow = Vector3.Lerp(a.leftElbow, b.leftElbow, k);
        into.rightRotation = Quaternion.Slerp(a.rightRotation, b.rightRotation, k);
        into.leftRotation = Quaternion.Slerp(a.leftRotation, b.leftRotation, k);
        return into;
    }

    // Transitions between hold / swing / reload: what was shown last frame, and the blend from it.
    private readonly ArmSample holdScratch = new ArmSample(), shown = new ArmSample(), blendFrom = new ArmSample(), blended = new ArmSample();
    private float blendTime = -1f;
    private int shownSource;
    private bool actionRestarted;

    /// <summary>Magazine / slide back where they rest (the reload's first sample).</summary>
    private void ResetReloadBones(HoldPose h)
    {
        if (h == null || h.reload.IsEmpty || !reloadBoneCache.TryGetValue(h, out Transform[] bones)) return;
        ArmSample rest = h.reload.samples[0];
        for (int j = 0; j < bones.Length && j < rest.bones.Length; j++)
            if (bones[j] != null) bones[j].localPosition = rest.bones[j];
    }

    /// <summary>
    /// If any point of the held item would be inside the head, moves both hands (and the item with them) forward, along
    /// the look direction, just far enough. Normally a no-op: the baked swings were already cleared at bake time;
    /// this catches what looking up / down changes (the head tilts less than the look).
    /// </summary>
    private void KeepClearOfHead(HoldPose h, Quaternion look, ref Vector3 rPos, ref Vector3 lPos, ref Vector3 rElbow, ref Vector3 lElbow, Quaternion rRot)
    {
        if (headBone == null || h.clearancePoints == null || h.clearancePoints.Length == 0) return;
        Vector3 c = headBone.position + headBone.rotation * headCentre;
        Vector3 radii = headRadii + Vector3.one * headMargin;
        Vector3 dir = look * Vector3.forward;
        float push = 0f;
        foreach (Vector3 local in h.clearancePoints)
            push = Mathf.Max(push, EllipsoidExit(rPos + rRot * local, dir, c, headBone.rotation, radii));
        LastHeadPush = push;
        if (push <= 0f) return;
        Vector3 move = dir * push;
        rPos += move; lPos += move; rElbow += move; lElbow += move;
    }

    /// <summary>
    /// How far point p must move along dir (unit) to leave the ellipsoid (centre, rotation, radii); 0 if it is outside.
    /// Shared with the setup tool's baker, so bake-time and runtime clearance agree.
    /// </summary>
    public static float EllipsoidExit(Vector3 p, Vector3 dir, Vector3 centre, Quaternion rotation, Vector3 radii)
    {
        Quaternion inv = Quaternion.Inverse(rotation);
        Vector3 q = inv * (p - centre), d = inv * dir;
        q = new Vector3(q.x / radii.x, q.y / radii.y, q.z / radii.z);   // the ellipsoid becomes a unit sphere
        d = new Vector3(d.x / radii.x, d.y / radii.y, d.z / radii.z);
        float c = q.sqrMagnitude - 1f;
        if (c >= 0f) return 0f;                                         // outside already
        float a = d.sqrMagnitude, b = Vector3.Dot(q, d);
        return (-b + Mathf.Sqrt(b * b - a * c)) / a;                    // the exit along dir
    }

    /// <summary>Debugging: the last right-hand IK target and elbow hint (world).</summary>
    public Vector3 DebugRightTarget { get; private set; }
    public Vector3 DebugRightHint { get; private set; }

    /// <summary>How far the last frame had to push the item out of the head (metres; tests/debugging).</summary>
    public float LastHeadPush { get; private set; }

    private void TickMuzzleLight(float dt)
    {
        if (muzzleLightTimer <= 0f) return;
        muzzleLightTimer -= dt;
        if (muzzleLightTimer <= 0f && held != null && held.visual != null)
            foreach (Light l in held.visual.GetComponentsInChildren<Light>(true)) l.enabled = false;
    }

    // ------------------------------------------------------------------ helpers

    private static void Turn(Quaternion q, Vector3 pivot, ref Vector3 pos, ref Quaternion rot, ref Vector3 elbow)
    {
        pos = pivot + q * (pos - pivot);
        elbow = pivot + q * (elbow - pivot);
        rot = q * rot;
    }

    /// <summary>
    /// Two-bone arm IK: bends the elbow toward the hint so the wrist reaches the target, then sets the hand's
    /// rotation. weight blends from the Animator's pose (0) to the solved pose (1).
    /// </summary>
    private static void SolveArm(Transform upper, Transform lower, Transform hand, Vector3 target, Quaternion handRotation, Vector3 hint, float weight)
    {
        if (weight <= 0.001f || upper == null || lower == null || hand == null) return;
        Quaternion upper0 = upper.rotation, lower0 = lower.rotation, hand0 = hand.rotation;

        Vector3 a = upper.position;
        float lab = Vector3.Distance(a, lower.position), lbc = Vector3.Distance(lower.position, hand.position);
        Vector3 toTarget = target - a;
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(lab - lbc) + 0.001f, (lab + lbc) * 0.999f);
        Vector3 dir = toTarget.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(hint - a, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
        bend.Normalize();
        float cosA = Mathf.Clamp((lab * lab + d * d - lbc * lbc) / (2f * lab * d), -1f, 1f);
        Vector3 elbow = a + dir * (lab * cosA) + bend * (lab * Mathf.Sqrt(1f - cosA * cosA));

        upper.rotation = Quaternion.FromToRotation(lower.position - a, elbow - a) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(hand.position - lower.position, a + dir * d - lower.position) * lower.rotation;
        hand.rotation = handRotation;

        if (weight < 0.999f)
        {
            upper.rotation = Quaternion.Slerp(upper0, upper.rotation, weight);
            lower.rotation = Quaternion.Slerp(lower0, lower.rotation, weight);
            hand.rotation = Quaternion.Slerp(hand0, hand.rotation, weight);
        }
    }

    private static void ApplyFingers(Transform[] joints, Quaternion[] pose, float weight)
    {
        if (joints == null || pose == null) return;
        int n = Mathf.Min(joints.Length, pose.Length);
        for (int i = 0; i < n; i++)
            if (joints[i] != null) joints[i].localRotation = Quaternion.Slerp(joints[i].localRotation, pose[i], weight);
    }

    private static Transform[] FindAll(Transform root, string[] paths)
    {
        var result = new Transform[paths.Length];
        if (root == null) return result;
        for (int i = 0; i < paths.Length; i++) result[i] = root.Find(paths[i]);
        return result;
    }

    private HoldPose Find(ItemData item)
    {
        foreach (HoldPose h in holds) if (h.item == item) return h;
        return null;
    }

    private HoldPose FirstNonEquippableHold()
    {
        foreach (HoldPose h in holds) if (h.item != null && !h.item.Equippable) return h;
        return null;
    }

#if UNITY_EDITOR
    /// <summary>Editor only: the setup tool stores the baked poses here.</summary>
    public void SetBakedPoses(HoldPose[] bakedHolds, HoldPose bakedFists, HoldPose bakedStrike) { holds = bakedHolds; fists = bakedFists; fistsStrike = bakedStrike; }

    /// <summary>Editor only (tuning / previews): poses the arms holding this item, fully raised, as other players see it.</summary>
    public void PreviewHold(ItemData item, float pitch)
    {
        if (rightFingers == null) Awake();
        SetHeldItem(item, false);
        holdWeight = 1f;
        Pitch = pitch;
        Vector3 shoulders = (leftUpperArm.position + rightUpperArm.position) * 0.5f;
        Quaternion look = transform.rotation * Quaternion.Euler(Pitch, 0f, 0f);
        if (held != null) PoseHold(held, shoulders, look, 1f, 0f);
    }
#endif

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}
