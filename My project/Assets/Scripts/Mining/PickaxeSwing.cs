using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedural first-person pickaxe animation with three alternating swings. Put this on
/// "PickaxeRoot". The grip points (and so the hands, via IK) are children, so the hands
/// always follow the handle.
///
/// Every mining input plays the next swing in a fixed order:
///
///   RIGHT (right → left) → LEFT (left → right) → OVERHEAD (top → down) → RIGHT → ...
///
/// Each swing is one continuous path through key poses:
///
///   Resting → Wind-up (hold) → Acceleration → STRIKE (fastest) → [hit: bite + recoil → short follow-through]
///                                                              → [miss: full follow-through]
///           → Recovery → Resting
///
/// Each key has a pose and a velocity, and the path between keys is a Hermite curve, so the
/// pickaxe never starts, stops or turns instantly. A new swing can start during the previous
/// one's follow-through or recovery and blends from the pickaxe's current pose *and velocity*.
///
/// POSES are offsets from the "Pickaxe" object's pose (the held pose set up by the viewmodel
/// tool), in CAMERA space, pivoting at the right hand:
///   Position: x = right, y = up, z = forward (metres).
///   Rotation (degrees): X = tip the head forward/down (+) or back toward you (−),
///                       Y = turn the head right (+) or left (−),
///                       Z = lean the head left (+) or right (−).
///
/// At the strike key it raises <see cref="ImpactReached"/>. MiningController does its normal
/// raycast/damage there and calls <see cref="ReportImpact"/>, which picks the hit or miss path.
/// </summary>
public class PickaxeSwing : MonoBehaviour
{
    private enum Phase { Idle, WindUp, Acceleration, Strike, Impact, FollowThrough, Recovery }

    /// <summary>The relaxed, lowered pose the pickaxe sits in when not swinging.</summary>
    [Serializable]
    public class RestingPoseSettings
    {
        [Tooltip("Camera space, metres: x = right, y = up, z = forward.")]
        public Vector3 restingPosition = new Vector3(0.079f, -0.188f, -0.183f);
        [Tooltip("Degrees around the camera's axes: X = head forward/down (+), Y = head right (+), Z = head leans left (+).")]
        public Vector3 restingRotation = new Vector3(16f, 9f, -16f);
        [Tooltip("Size of the slow idle drift while resting (0 = perfectly still).")]
        [Range(0f, 3f)] public float idleSwayAmount = 1f;
        [Tooltip("Speed of the idle drift.")]
        [Range(0f, 3f)] public float idleSwaySpeed = 1f;
    }

    /// <summary>One swing's trajectory. Poses use the same camera-space convention as the resting pose.</summary>
    [Serializable]
    public class SwingAnimation
    {
        [Header("Wind-up (anticipation)")]
        public Vector3 windUpPosition;
        public Vector3 windUpRotation;
        [Min(0.01f)] public float windUpDuration = 0.25f;
        [Tooltip("How long the pickaxe hangs at the top of the wind-up before it accelerates.")]
        [Min(0f)] public float windUpHold = 0.03f;

        [Header("Acceleration (a waypoint the head passes on its way in)")]
        public Vector3 accelerationPosition;
        public Vector3 accelerationRotation;
        [Min(0.01f)] public float accelerationDuration = 0.1f;

        [Header("Strike (the moment of impact: the hit check happens here)")]
        public Vector3 strikePosition;
        public Vector3 strikeRotation;
        [Min(0.01f)] public float strikeDuration = 0.08f;
        [Tooltip("Speed at impact relative to the strike's average speed. >1 = still accelerating into the rock.")]
        [Range(0.5f, 3f)] public float strikeAcceleration = 1.4f;

        [Header("Follow-through (momentum carries on past the impact)")]
        public Vector3 followThroughPosition;
        public Vector3 followThroughRotation;
        [Min(0.01f)] public float followThroughDuration = 0.2f;

        [Header("Recovery (a waypoint on the way back to resting)")]
        public Vector3 recoveryPosition;
        public Vector3 recoveryRotation;
        [Min(0.01f)] public float recoveryDuration = 0.3f;

        [Tooltip("Time-shaping inside every stretch of this swing (x = time, y = progress). Linear keeps the natural ease.")]
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Calculated: a missed swing from resting back to resting, in seconds, at Animation Speed 1. Editing it does nothing.")]
        public float swingDuration;

        public float ImpactTime => windUpDuration + windUpHold + accelerationDuration + strikeDuration;
    }

    [Header("Resting / holstered pose")]
    [SerializeField] private RestingPoseSettings restingPose = new RestingPoseSettings();

    [Header("Right swing (right → left)")]
    [SerializeField] private SwingAnimation rightSwing = new SwingAnimation
    {
        windUpPosition = new Vector3(-0.001f, -0.048f, 0.056f),
        windUpRotation = new Vector3(-57f, 15f, -63f),
        windUpDuration = 0.3f,
        windUpHold = 0.03f,
        accelerationPosition = new Vector3(0.047f, -0.147f, 0.017f),
        accelerationRotation = new Vector3(-44f, -5f, -31f),
        accelerationDuration = 0.08f,
        strikePosition = new Vector3(0.018f, -0.164f, 0.036f),
        strikeRotation = new Vector3(-26f, -21f, -20f),
        strikeDuration = 0.07f,
        strikeAcceleration = 1.6f,
        followThroughPosition = new Vector3(-0.307f, -0.22f, -0.011f),
        followThroughRotation = new Vector3(-1f, -44f, 16f),
        followThroughDuration = 0.2f,
        recoveryPosition = new Vector3(-0.065f, -0.23f, -0.046f),
        recoveryRotation = new Vector3(17f, -23f, -10f),
        recoveryDuration = 0.34f,
    };

    [Header("Left swing (left → right)")]
    [SerializeField] private SwingAnimation leftSwing = new SwingAnimation
    {
        windUpPosition = new Vector3(-0.266f, 0.006f, 0.112f),
        windUpRotation = new Vector3(-45f, -42f, 63f),
        windUpDuration = 0.32f,
        windUpHold = 0.03f,
        accelerationPosition = new Vector3(-0.244f, -0.13f, 0.041f),
        accelerationRotation = new Vector3(-51f, -1f, 25f),
        accelerationDuration = 0.08f,
        strikePosition = new Vector3(-0.145f, -0.192f, -0.005f),
        strikeRotation = new Vector3(-31f, 19f, 11f),
        strikeDuration = 0.07f,
        strikeAcceleration = 1.6f,
        followThroughPosition = new Vector3(0.069f, -0.194f, -0.262f),
        followThroughRotation = new Vector3(8f, 36f, -9f),
        followThroughDuration = 0.2f,
        recoveryPosition = new Vector3(0.07f, -0.213f, -0.22f),
        recoveryRotation = new Vector3(12f, 24f, -6f),
        recoveryDuration = 0.3f,
    };

    [Header("Overhead swing (top → down)")]
    [SerializeField] private SwingAnimation overheadSwing = new SwingAnimation
    {
        windUpPosition = new Vector3(-0.144f, 0.189f, 0.188f),
        windUpRotation = new Vector3(-73f, 11f, -19f),
        windUpDuration = 0.32f,
        windUpHold = 0.07f,
        accelerationPosition = new Vector3(-0.176f, 0.05f, 0.152f),
        accelerationRotation = new Vector3(-48f, 16f, -23f),
        accelerationDuration = 0.09f,
        strikePosition = new Vector3(-0.125f, -0.049f, 0.091f),
        strikeRotation = new Vector3(-11f, 7f, -3f),
        strikeDuration = 0.08f,
        strikeAcceleration = 1.6f,
        followThroughPosition = new Vector3(-0.102f, -0.225f, -0.064f),
        followThroughRotation = new Vector3(41f, 7f, 3f),
        followThroughDuration = 0.22f,
        recoveryPosition = new Vector3(-0.076f, -0.236f, -0.095f),
        recoveryRotation = new Vector3(28f, 7f, -7f),
        recoveryDuration = 0.32f,
    };

    [Header("Impact (only on a hit, all swings)")]
    [Tooltip("How long the pickaxe 'bites' into the rock: a sharp slowdown right after impact (seconds).")]
    [SerializeField, Min(0.005f)] private float impactPause = 0.05f;
    [Tooltip("Share of the impact speed kept going into the bite. Low = hits a wall of rock.")]
    [SerializeField, Range(0f, 1f)] private float impactSpeedKept = 0.12f;
    [Tooltip("Small kick back after the bite, against the swing direction (degrees) and its duration (seconds).")]
    [SerializeField] private float impactRecoil = 3f;
    [SerializeField, Min(0.01f)] private float impactRecoilDuration = 0.06f;
    [Tooltip("Degrees of rattle in the handle right after a hit.")]
    [SerializeField, Min(0f)] private float impactVibration = 1.2f;
    [Tooltip("How much of the follow-through still happens after a HIT (the rock absorbs the rest). 1 = same as a miss.")]
    [SerializeField, Range(0f, 1f)] private float followThroughStrength = 0.45f;
    [Tooltip("Misses carry further and a little longer than the follow-through pose.")]
    [SerializeField, Range(1f, 2f)] private float missFollowThroughMultiplier = 1.15f;

    [Header("General")]
    [Tooltip("Plays every swing faster (>1) or slower (<1). The hit check moves with the strike.")]
    [SerializeField, Range(0.5f, 2f)] private float animationSpeed = 1f;
    [Tooltip("How fast the pickaxe returns to resting after a swing (>1 = quicker recovery).")]
    [SerializeField, Range(0.5f, 2f)] private float recoverySpeed = 1f;
    [Tooltip("A mining click that comes while a swing is still busy is remembered this long (seconds) and " +
             "starts the next swing as soon as it can. One click is remembered. 0 = off.")]
    [SerializeField, Min(0f)] private float inputBuffering = 1f;
    [Tooltip("When a swing starts during the previous one, how much of the pickaxe's momentum carries into it " +
             "(0 = starts from a standstill, 1 = fully fluid).")]
    [SerializeField, Range(0f, 1f)] private float blendSmoothness = 0.6f;
    [Tooltip("Small per-swing differences in the wind-up so repeats don't look robotic (0 = identical). The swing order never changes.")]
    [SerializeField, Range(0f, 1f)] private float swingVariation = 0.3f;
    [Tooltip("Camera shake strength on a hit (0 = off). Needs a CameraShake on the camera.")]
    [SerializeField, Range(0f, 1f)] private float cameraShakeAmount = 0.2f;
    [Tooltip("How much the camera follows the swing (0 = off).")]
    [SerializeField, Range(0f, 2f)] private float cameraMotion = 1f;
    [Tooltip("Optional. Auto-found on a parent (the camera) if left empty.")]
    [SerializeField] private CameraShake cameraShake;

    /// <summary>Raised once per swing, at the strike key (the fastest point), hit or miss.</summary>
    public event Action ImpactReached;
    /// <summary>Raised when the strike actually hit a surface (after ReportImpact(true)).</summary>
    public event Action HitLanded;

    /// <summary>True while any part of a swing is playing.</summary>
    public bool IsSwinging => phase != Phase.Idle;
    /// <summary>A new swing may start when idle, or once the previous one is following through or recovering.</summary>
    public bool CanSwing => phase == Phase.Idle || phase == Phase.FollowThrough || phase == Phase.Recovery;
    /// <summary>How long a mining click may wait for the swing to become ready (seconds). Read by MiningController.</summary>
    public float InputBuffering => inputBuffering;
    /// <summary>Current phase name, for debugging.</summary>
    public string PhaseName => phase.ToString();
    /// <summary>The swing playing now ("Right", "Left", "Overhead"), or "None" when idle.</summary>
    public string CurrentSwingName => phase == Phase.Idle ? "None" : SwingNames[currentSwing];
    /// <summary>The swing the next input will play.</summary>
    public string NextSwingName => SwingNames[nextSwing];

    /// <summary>Tiny camera position offset (camera space) that follows the swing. Read by CameraEffects.</summary>
    public Vector3 CameraOffset { get; private set; }
    /// <summary>Tiny camera rotation (degrees, camera space) that follows the swing. Read by CameraEffects.</summary>
    public Vector3 CameraRotation { get; private set; }

    private static readonly string[] SwingNames = { "Right", "Left", "Overhead" };

    // ---- A pose: camera-space position offset + rotation (Euler degrees) ---------------
    private struct Pose
    {
        public Vector3 p, r;
        public Pose(Vector3 p, Vector3 r) { this.p = p; this.r = r; }
        public static Pose operator +(Pose a, Pose b) => new Pose(a.p + b.p, a.r + b.r);
        public static Pose operator -(Pose a, Pose b) => new Pose(a.p - b.p, a.r - b.r);
        public static Pose operator *(Pose a, float k) => new Pose(a.p * k, a.r * k);
        public static readonly Pose Zero = new Pose(Vector3.zero, Vector3.zero);
    }

    private struct Key
    {
        public float time;
        public Pose pose;
        public Pose velocityIn;      // velocity arriving at this key
        public Pose velocityOut;     // velocity leaving it (differs only where something stops the pickaxe)
        public bool autoVelocity;    // derived from the neighbours (smooth pass-through)
        public Phase phase;          // phase of the segment that ENDS at this key
        public AnimationCurve warp;  // optional time-shaping of that segment
    }

    private readonly List<Key> keys = new List<Key>(12);
    private float time;
    private int impactKey = -1;
    private bool impactFired;
    private Phase phase = Phase.Idle;
    private int currentSwing;
    private int nextSwing;

    private Quaternion mountRotation = Quaternion.identity; // the "Pickaxe" object's rotation in camera space
    private Quaternion baseRotation;
    private Vector3 basePosition;
    private Pose current, currentVelocity;
    private float idleWeight = 1f;
    private float vibrationTime = -1f;

    private Pose Resting => new Pose(restingPose.restingPosition, restingPose.restingRotation);

    private void Awake()
    {
        baseRotation = transform.localRotation;
        basePosition = transform.localPosition;
        if (transform.parent != null) mountRotation = transform.parent.localRotation;
        if (cameraShake == null) cameraShake = GetComponentInParent<CameraShake>();
        current = Resting;
        currentVelocity = Pose.Zero;
    }

    private void OnValidate()
    {
        foreach (SwingAnimation s in new[] { rightSwing, leftSwing, overheadSwing })
            if (s != null)
                s.swingDuration = s.ImpactTime + s.followThroughDuration * missFollowThroughMultiplier + s.recoveryDuration / recoverySpeed;
    }

    private SwingAnimation Get(int index) => index == 0 ? rightSwing : index == 1 ? leftSwing : overheadSwing;

    /// <summary>
    /// Plays the next swing in the Right → Left → Overhead order. Returns false (and does
    /// nothing) if the current swing hasn't reached its follow-through yet. When called mid
    /// follow-through/recovery, the new swing blends from the current pose and momentum.
    /// </summary>
    public bool Swing()
    {
        if (!CanSwing) return false;

        currentSwing = nextSwing;
        nextSwing = (nextSwing + 1) % SwingNames.Length;
        SwingAnimation s = Get(currentSwing);

        float timeScale = 1f / animationSpeed;
        float v = swingVariation;
        Vector3 jitter = new Vector3(UnityEngine.Random.Range(-1f, 1f) * 3f, UnityEngine.Random.Range(-1f, 1f) * 3f,
                                     UnityEngine.Random.Range(-1f, 1f) * 3f) * v;

        keys.Clear();
        time = 0f;
        impactFired = false;
        vibrationTime = -1f;

        Pose windUp = new Pose(s.windUpPosition, s.windUpRotation + jitter);
        Pose accel = new Pose(s.accelerationPosition, s.accelerationRotation + jitter * 0.5f);
        Pose strike = new Pose(s.strikePosition, s.strikeRotation);
        float tw = s.windUpDuration * timeScale, th = s.windUpHold * timeScale;
        float ta = s.accelerationDuration * timeScale, ts = s.strikeDuration * timeScale;

        // Start from wherever the pickaxe is now (resting, or mid-way through the last swing),
        // carrying some of its momentum.
        AddKey(0f, current, Phase.WindUp, null, velocity: currentVelocity * blendSmoothness);
        // Top of the wind-up: zero velocity = the anticipation hang.
        AddKey(tw, windUp, Phase.WindUp, s.curve, velocity: Pose.Zero);
        if (th > 0f)
        {
            // Keep drifting a touch further back while hanging, so the pose never looks frozen.
            Pose drift = windUp + (windUp - accel) * 0.04f;
            AddKey(tw + th, drift, Phase.WindUp, null, velocity: Pose.Zero);
            windUp = drift;
        }
        float t = tw + th;
        // Picking up speed: a little under the average speed from the top to the impact...
        AddKey(t + ta, accel, Phase.Acceleration, s.curve, velocity: (strike - windUp) * (0.9f / (ta + ts)));
        // ...and fastest right at impact: faster than the strike's own average speed.
        impactKey = AddKey(t + ta + ts, strike, Phase.Strike, s.curve, velocity: (strike - accel) * (s.strikeAcceleration / ts));

        // Assume a miss for now; ReportImpact(true) swaps the rest for the hit path.
        AppendMissPath(s, t + ta + ts, timeScale);
        ComputeAutoVelocities();

        phase = Phase.WindUp;
        return true;
    }

    /// <summary>
    /// Called by MiningController from inside <see cref="ImpactReached"/>.
    /// hitSomething = the pickaxe struck a surface: bite, recoil, shorter follow-through, shake.
    /// </summary>
    public void ReportImpact(bool hitSomething)
    {
        if (!impactFired || !hitSomething || impactKey < 0 || impactKey >= keys.Count) return;

        SwingAnimation s = Get(currentSwing);
        float timeScale = 1f / animationSpeed;
        Key impact = keys[impactKey];
        Pose impactPose = impact.pose;
        Pose arriving = impact.velocityIn;
        keys.RemoveRange(impactKey + 1, keys.Count - impactKey - 1);

        // The rock stops it: the speed leaving the impact collapses
        // (a deliberate velocity break = "it hit something").
        impact.velocityOut = arriving * impactSpeedKept;
        keys[impactKey] = impact;
        float t = impact.time + impactPause * timeScale;

        // The head sinks in a little further along its own path, whichever way it was swinging...
        Pose bite = impactPose + arriving * (impactPause * impactSpeedKept);
        AddKey(t, bite, Phase.Impact, null, velocity: Pose.Zero);
        // ...then kicks back against it.
        float speed = arriving.r.magnitude;
        Pose recoil = speed > 1e-3f ? impactPose - arriving * (impactRecoil / speed) : impactPose;
        t += impactRecoilDuration * timeScale;
        AddKey(t, recoil, Phase.Impact, null, velocity: Pose.Zero);

        // Momentum still carries on through — just less of it.
        Pose full = new Pose(s.followThroughPosition, s.followThroughRotation);
        t += s.followThroughDuration * timeScale;
        AddKey(t, impactPose + (full - impactPose) * followThroughStrength, Phase.FollowThrough, s.curve, velocity: Pose.Zero);
        AppendRecovery(s, t, timeScale);
        ComputeAutoVelocities();

        vibrationTime = 0f;
        if (cameraShake != null && cameraShakeAmount > 0f)
            cameraShake.Shake(cameraShakeAmount);
        HitLanded?.Invoke();
    }

    private void AppendMissPath(SwingAnimation s, float t, float timeScale)
    {
        // Carries straight through the impact point with no stop, further than a hit.
        Key impact = keys[impactKey];
        Pose full = new Pose(s.followThroughPosition, s.followThroughRotation);
        Pose end = impact.pose + (full - impact.pose) * missFollowThroughMultiplier;
        float T = s.followThroughDuration * missFollowThroughMultiplier * timeScale;

        // Keep as much of the strike speed as possible without the curve overshooting past
        // the end pose and swinging back (a cubic stays one-way if it starts at < 3x its average speed).
        float chord = (end.r - impact.pose.r).magnitude;
        float speed = impact.velocityIn.r.magnitude;
        float maxSpeed = 2.8f * chord / Mathf.Max(T, 1e-4f);
        impact.velocityOut = speed > maxSpeed && speed > 1e-4f ? impact.velocityIn * (maxSpeed / speed) : impact.velocityIn;
        keys[impactKey] = impact;

        AddKey(t + T, end, Phase.FollowThrough, s.curve, velocity: Pose.Zero);
        AppendRecovery(s, t + T, timeScale);
    }

    private void AppendRecovery(SwingAnimation s, float t, float timeScale)
    {
        float d = s.recoveryDuration * timeScale / recoverySpeed;
        AddKey(t + d * 0.5f, new Pose(s.recoveryPosition, s.recoveryRotation), Phase.Recovery, s.curve);
        AddKey(t + d, Resting, Phase.Recovery, s.curve, velocity: Pose.Zero);
    }

    private int AddKey(float t, Pose pose, Phase ph, AnimationCurve warp, Pose? velocity = null)
    {
        Pose v = velocity ?? Pose.Zero;
        keys.Add(new Key
        {
            time = t, pose = pose, phase = ph, warp = warp,
            velocityIn = v, velocityOut = v, autoVelocity = !velocity.HasValue,
        });
        return keys.Count - 1;
    }

    /// <summary>Smooth pass-through velocities (Catmull-Rom style) for keys without a set velocity.</summary>
    private void ComputeAutoVelocities()
    {
        for (int i = 0; i < keys.Count; i++)
        {
            Key k = keys[i];
            if (!k.autoVelocity) continue;
            int a = Mathf.Max(i - 1, 0), b = Mathf.Min(i + 1, keys.Count - 1);
            float dt = keys[b].time - keys[a].time;
            k.velocityIn = k.velocityOut = dt > 1e-5f ? (keys[b].pose - keys[a].pose) * (1f / dt) : Pose.Zero;
            keys[i] = k;
        }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        // Mid-swing with no timeline (a script reload doesn't keep it, and this project enters Play
        // without reloading the scene): settle back to resting instead of reading missing keys.
        if (phase != Phase.Idle && keys.Count < 2) phase = Phase.Idle;
        // Idle drift fades out while swinging and back in once resting.
        idleWeight = Mathf.MoveTowards(idleWeight, phase == Phase.Idle ? 1f : 0f, dt * (phase == Phase.Idle ? 1.5f : 6f));

        if (phase == Phase.Idle)
        {
            current = Resting; // follows Inspector edits live
            currentVelocity = Pose.Zero;
            ApplyPose(dt);
            UpdateCamera(dt);
            return;
        }

        Pose previous = current;
        time += dt;

        // Impact: exactly when the timeline reaches the strike key.
        if (!impactFired && impactKey >= 0 && time >= keys[impactKey].time)
        {
            time = keys[impactKey].time; // land exactly on the strike pose this frame
            current = keys[impactKey].pose;
            phase = Phase.Impact;
            impactFired = true;
            ApplyPose(dt);
            ImpactReached?.Invoke(); // may call ReportImpact → switches to the hit path
            currentVelocity = dt > 0f ? (current - previous) * (1f / dt) : currentVelocity;
            UpdateCamera(dt);
            return;
        }

        if (time >= keys[keys.Count - 1].time)
        {
            phase = Phase.Idle;
            current = Resting;
            currentVelocity = Pose.Zero;
            vibrationTime = -1f;
            ApplyPose(dt);
            UpdateCamera(dt);
            return;
        }

        current = Evaluate(time, out phase);
        if (impactFired && phase < Phase.Impact) phase = Phase.Impact;
        currentVelocity = dt > 0f ? (current - previous) * (1f / dt) : currentVelocity;
        ApplyPose(dt);
        UpdateCamera(dt);
    }

    private Pose Evaluate(float t, out Phase segmentPhase)
    {
        int i = 0;
        while (i < keys.Count - 2 && t >= keys[i + 1].time) i++;
        Key k0 = keys[i], k1 = keys[i + 1];
        segmentPhase = k1.phase;
        float T = Mathf.Max(k1.time - k0.time, 1e-5f);
        float u = Mathf.Clamp01((t - k0.time) / T);
        if (k1.warp != null && k1.warp.length > 1) u = Mathf.Clamp01(k1.warp.Evaluate(u));

        // Cubic Hermite: passes through both poses with the given velocities.
        float u2 = u * u, u3 = u2 * u;
        float h00 = 2f * u3 - 3f * u2 + 1f, h10 = u3 - 2f * u2 + u;
        float h01 = -2f * u3 + 3f * u2, h11 = u3 - u2;
        return k0.pose * h00 + k0.velocityOut * (h10 * T) + k1.pose * h01 + k1.velocityIn * (h11 * T);
    }

    private void ApplyPose(float dt)
    {
        Vector3 position = current.p;
        Vector3 angle = current.r;

        // Slow idle drift while resting.
        if (idleWeight > 0f && restingPose.idleSwayAmount > 0f)
        {
            float t = Time.time * restingPose.idleSwaySpeed;
            float a = restingPose.idleSwayAmount * idleWeight;
            position += new Vector3(Mathf.Sin(t * 0.9f) * 0.003f, Mathf.Sin(t * 1.3f + 0.5f) * 0.002f, 0f) * a;
            angle += new Vector3(Mathf.Sin(t * 0.7f) * 0.6f, Mathf.Sin(t * 0.5f + 1f) * 0.5f, Mathf.Sin(t * 0.6f + 2f) * 0.8f) * a;
        }

        // Rattle after a hit: fast, small, dying away.
        if (vibrationTime >= 0f)
        {
            vibrationTime += dt;
            float decay = Mathf.Exp(-vibrationTime * 18f);
            if (decay < 0.01f) vibrationTime = -1f;
            else
            {
                float w = vibrationTime * 2f * Mathf.PI * 28f;
                angle += new Vector3(Mathf.Sin(w), 0f, Mathf.Sin(w * 1.37f + 1f) * 0.6f) * (impactVibration * decay);
            }
        }

        // Camera-space rotation around the right hand, turned into PickaxeRoot's local space.
        transform.localRotation = Quaternion.Inverse(mountRotation) * Quaternion.Euler(angle) * mountRotation * baseRotation;
        transform.localPosition = basePosition + Quaternion.Inverse(mountRotation) * position;
    }

    /// <summary>
    /// A small camera reaction that follows the swing: leans with the wind-up, kicks toward
    /// the strike as it speeds up, and follows the momentum through.
    /// </summary>
    private void UpdateCamera(float dt)
    {
        if (cameraMotion <= 0f) { CameraOffset = Vector3.zero; CameraRotation = Vector3.zero; return; }

        Pose away = current - Resting; // how far the swing has taken the pickaxe from resting
        float pitch = away.r.x * 0.015f + Mathf.Clamp(currentVelocity.r.x * 0.001f, -0.8f, 0.8f);
        float yaw = away.r.y * 0.012f + Mathf.Clamp(currentVelocity.r.y * 0.0008f, -0.6f, 0.6f);
        float roll = -away.r.z * 0.01f;
        Vector3 targetRot = new Vector3(Mathf.Clamp(pitch, -1.2f, 1.2f), Mathf.Clamp(yaw, -0.8f, 0.8f),
                                        Mathf.Clamp(roll, -0.6f, 0.6f)) * cameraMotion;
        Vector3 targetPos = Vector3.ClampMagnitude(away.p * 0.08f, 0.01f) * cameraMotion;

        // Light smoothing so the camera never twitches.
        float k = dt > 0f ? 1f - Mathf.Exp(-dt * 25f) : 1f;
        CameraRotation = Vector3.Lerp(CameraRotation, targetRot, k);
        CameraOffset = Vector3.Lerp(CameraOffset, targetPos, k);
    }

    private void OnDisable()
    {
        phase = Phase.Idle;
        vibrationTime = -1f;
        current = Resting;
        currentVelocity = Pose.Zero;
        CameraOffset = Vector3.zero;
        CameraRotation = Vector3.zero;
        if (Application.isPlaying) ApplyPose(0f); // never move the saved scene's transform in edit mode
    }
}
