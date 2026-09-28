using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Subtle "alive" motion for the first-person arms. Put it on FirstPersonViewModel
/// (the parent of Arms). Adds idle breathing and sway, a slight lag when turning,
/// and a small walking bob. It fades mostly out while a swing plays, so it never
/// fights the PickaxeSwing animation on the child Arms object.
/// </summary>
[DefaultExecutionOrder(60)] // after PlayerMotionState, before FirstPersonArmsIK
public class ViewModelMotion : MonoBehaviour
{
    [Header("Idle")]
    [Tooltip("Overall size of the idle sway. 0 = perfectly still, 1 = default, 2 = double.")]
    [SerializeField, Range(0f, 3f)] private float idleSwayAmount = 1f;
    [Tooltip("Overall speed of the idle sway. 1 = default.")]
    [SerializeField, Range(0.1f, 3f)] private float idleSwaySpeed = 1f;
    [Tooltip("Up/down breathing movement in metres.")]
    [SerializeField, Min(0f)] private float breathingAmount = 0.006f;
    [Tooltip("Breaths per second (0.25 = one breath every 4 seconds).")]
    [SerializeField, Min(0f)] private float breathingRate = 0.25f;

    [Header("Look Sway")]
    [Tooltip("How much the arms lag behind when you turn, in degrees per pixel of mouse movement.")]
    [SerializeField, Min(0f)] private float lookSwayAmount = 0.035f;
    [Tooltip("Maximum lag angle in degrees.")]
    [SerializeField, Min(0f)] private float maxLookSway = 3f;
    [Tooltip("How quickly the arms catch up. Lower = heavier.")]
    [SerializeField, Min(0.1f)] private float lookSwaySmoothing = 8f;

    [Header("Walk Bob")]
    [Tooltip("Size of the walking bob in metres. 0 = off.")]
    [SerializeField, Min(0f)] private float walkBobAmount = 0.008f;
    [Tooltip("Bob cycles per metre walked.")]
    [SerializeField, Min(0f)] private float walkBobFrequency = 0.9f;

    [Header("While Swinging")]
    [Tooltip("How much idle/bob motion stays during a swing (0 = none).")]
    [SerializeField, Range(0f, 1f)] private float motionWhileSwinging = 0.15f;
    [Tooltip("How fast the idle motion fades out/in around a swing.")]
    [SerializeField, Min(0.1f)] private float blendSpeed = 6f;

    [Header("Jump & Fall (reads PlayerMotionState)")]
    [Tooltip("Arms lift when a jump starts, metres.")]
    [SerializeField, Min(0f)] private float jumpStartLift = 0.012f;
    [Tooltip("Share of the lift kept while still rising (fades to 0 at the apex).")]
    [SerializeField, Range(0f, 1f)] private float ascendLift = 0.5f;
    [Tooltip("Arms sink while falling, metres at maximum fall speed.")]
    [SerializeField, Min(0f)] private float fallDrop = 0.02f;
    [Tooltip("Pickaxe tips down while falling, degrees at maximum fall speed.")]
    [SerializeField, Min(0f)] private float fallTilt = 2f;
    [Tooltip("How quickly the arms follow jump changes (seconds).")]
    [SerializeField, Range(0.01f, 0.5f)] private float airSmoothing = 0.1f;
    [Tooltip("How slowly the arms sink as a fall builds (seconds).")]
    [SerializeField, Range(0.01f, 1f)] private float fallSmoothing = 0.3f;

    [Header("Landing (reads PlayerMotionState)")]
    [Tooltip("How far the arms drop on the hardest landing, metres.")]
    [SerializeField, Min(0f)] private float landingDrop = 0.035f;
    [Tooltip("How far the arms squash back toward the body on the hardest landing, metres.")]
    [SerializeField, Min(0f)] private float landingCompress = 0.02f;
    [Tooltip("How far the pickaxe dips forward on the hardest landing, degrees.")]
    [SerializeField, Min(0f)] private float landingTilt = 4f;

    [Header("Mining Hit")]
    [Tooltip("Arms jolt back/down this much when the pickaxe hits something, metres.")]
    [SerializeField, Min(0f)] private float hitKick = 0.012f;

    [Header("References (auto-found if empty)")]
    [SerializeField] private PickaxeSwing pickaxeSwing;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerMotionState motionState;

    private Vector3 restPosition;
    private Quaternion restRotation;
    private float idleTime, bobPhase, bobWeight, motionWeight = 1f;
    private Vector2 lookSway, lookSwayVelocity;
    private Vector3 airPos, airPosVel;
    private float airTilt, airTiltVel;
    private float hitTimer = -1f;

    private void Awake()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        if (pickaxeSwing == null) pickaxeSwing = GetComponentInChildren<PickaxeSwing>();
        if (characterController == null) characterController = GetComponentInParent<CharacterController>();
        if (motionState == null) motionState = GetComponentInParent<PlayerMotionState>();
    }

    private void OnEnable() { if (pickaxeSwing != null) pickaxeSwing.HitLanded += OnHit; }
    private void OnDisable() { if (pickaxeSwing != null) pickaxeSwing.HitLanded -= OnHit; }
    private void OnHit() => hitTimer = 0f;

    private void Update()
    {
        float dt = Time.deltaTime;

        // Fade idle/bob out while swinging, back in afterwards.
        float targetWeight = pickaxeSwing != null && pickaxeSwing.IsSwinging ? motionWhileSwinging : 1f;
        motionWeight = Mathf.MoveTowards(motionWeight, targetWeight, blendSpeed * dt);

        // --- Idle: breathing + slow, uneven sway (a few sines at odd speeds) ---
        idleTime += dt * idleSwaySpeed;
        float breath = Mathf.Sin(idleTime * breathingRate * 2f * Mathf.PI);
        float swayA = Mathf.Sin(idleTime * 0.83f);
        float swayB = Mathf.Sin(idleTime * 0.57f + 1.3f);
        float swayC = Mathf.Sin(idleTime * 1.31f + 2.1f);

        Vector3 pos = new Vector3(
            swayA * 0.0025f,
            breath * breathingAmount + swayC * 0.001f,
            breath * breathingAmount * 0.3f) * idleSwayAmount;
        Vector3 rot = new Vector3(
            -breath * 0.6f + swayC * 0.15f, // pitch: heavy head dips slightly on the exhale
            swayB * 0.35f,
            swayA * 0.5f) * idleSwayAmount;

        // --- Walk bob: figure-eight driven by actual ground speed ---
        float speed = 0f;
        if (characterController != null && characterController.isGrounded)
        {
            Vector3 v = characterController.velocity;
            speed = new Vector2(v.x, v.z).magnitude;
        }
        bobWeight = Mathf.MoveTowards(bobWeight, Mathf.Clamp01(speed / 4f), 4f * dt);
        bobPhase += speed * walkBobFrequency * dt * 2f * Mathf.PI;
        pos += new Vector3(Mathf.Sin(bobPhase * 0.5f) * walkBobAmount,
                           -Mathf.Abs(Mathf.Cos(bobPhase * 0.5f)) * walkBobAmount, 0f) * bobWeight;
        rot.z += Mathf.Sin(bobPhase * 0.5f) * 0.8f * bobWeight;

        pos *= motionWeight;
        rot *= motionWeight;

        // --- Look sway: arms lag slightly behind the camera when turning ---
        Vector2 target = Vector2.zero;
        if (Mouse.current != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            target = Vector2.ClampMagnitude(new Vector2(-delta.x, delta.y) * lookSwayAmount, maxLookSway);
        }
        lookSway = Vector2.SmoothDamp(lookSway, target, ref lookSwayVelocity, 1f / lookSwaySmoothing);
        rot += new Vector3(lookSway.y, lookSway.x, lookSway.x * 0.5f);

        // --- Jump / fall: state-based, follows the real vertical speed (not a bob) ---
        Vector3 airTarget = Vector3.zero;
        float tiltTarget = 0f;
        bool falling = false;
        if (motionState != null)
        {
            switch (motionState.State)
            {
                case PlayerMotionState.MotionState.JumpStart:
                    airTarget.y = jumpStartLift;
                    break;
                case PlayerMotionState.MotionState.Ascending:
                    airTarget.y = jumpStartLift * ascendLift * Mathf.Clamp01(motionState.VerticalSpeed / 7f);
                    break;
                case PlayerMotionState.MotionState.Falling:
                    falling = true;
                    airTarget.y = -fallDrop * motionState.FallAmount01;
                    tiltTarget = fallTilt * motionState.FallAmount01;
                    break;
                // Apex / Grounded / Landing: settle to neutral.
            }
        }
        float airSmooth = falling ? fallSmoothing : airSmoothing;
        airPos = Vector3.SmoothDamp(airPos, airTarget, ref airPosVel, airSmooth);
        airTilt = Mathf.SmoothDamp(airTilt, tiltTarget, ref airTiltVel, airSmooth);
        pos += airPos;
        rot.x += airTilt;

        // --- Landing: compress, small rebound, settle (spring shared with the camera) ---
        if (motionState != null)
        {
            float c = motionState.LandingCompression;
            pos += new Vector3(0f, -landingDrop * c, -landingCompress * c);
            rot.x += landingTilt * c;
        }

        // --- Mining hit: quick jolt back/down, eased out ---
        if (hitTimer >= 0f)
        {
            hitTimer += dt;
            float t = hitTimer / 0.18f;
            if (t >= 1f) hitTimer = -1f;
            else
            {
                float k = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.5f)) * (1f - t);
                pos += new Vector3(0f, -0.4f, -1f) * (hitKick * k);
            }
        }

        transform.localPosition = restPosition + pos;
        transform.localRotation = restRotation * Quaternion.Euler(rot);
    }
}
