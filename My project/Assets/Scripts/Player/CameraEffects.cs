using UnityEngine;

/// <summary>
/// Head-motion camera effects: walk/run bob, idle breathing, directional sway,
/// state-based jump/fall motion, a spring-based landing (compress → rebound → settle),
/// a mining-hit reaction, and a general-purpose shake hook.
///
/// Put this on "CameraRoot", the parent of the player camera. PlayerLook keeps rotating
/// the camera itself (pitch); this script only ever moves CameraRoot, so the two never
/// fight. Every effect is added into one offset, applied once per frame in LateUpdate.
///
/// Movement state comes from the existing PlayerMovement (IsGrounded, IsSprinting),
/// the CharacterController's velocity, and PlayerMotionState (jump/fall/landing).
/// </summary>
public class CameraEffects : MonoBehaviour
{
    [Header("General")]
    [Tooltip("Scales every effect. 0 = off, 1 = default, 2 = double.")]
    [SerializeField, Range(0f, 2f)] private float globalIntensity = 1f;
    [Tooltip("Smoothing for bob / sway / idle position, in seconds. Higher = softer.")]
    [SerializeField, Range(0f, 0.3f)] private float positionSmoothness = 0.06f;
    [Tooltip("Smoothing for bob / sway / idle rotation, in seconds. Higher = softer.")]
    [SerializeField, Range(0f, 0.3f)] private float rotationSmoothness = 0.08f;

    [Header("Walking")]
    [Tooltip("Steps per second while walking.")]
    [SerializeField, Min(0f)] private float walkBobFrequency = 1.8f;
    [Tooltip("Up/down movement per step, in metres.")]
    [SerializeField, Min(0f)] private float walkBobVerticalAmplitude = 0.025f;
    [Tooltip("Side-to-side movement (once per two steps), in metres.")]
    [SerializeField, Min(0f)] private float walkBobHorizontalAmplitude = 0.015f;
    [Tooltip("Head roll/tilt with each step, in degrees.")]
    [SerializeField, Min(0f)] private float walkRotationAmount = 0.6f;

    [Header("Running")]
    [SerializeField, Min(0f)] private float runBobFrequency = 2.5f;
    [SerializeField, Min(0f)] private float runBobVerticalAmplitude = 0.045f;
    [SerializeField, Min(0f)] private float runBobHorizontalAmplitude = 0.025f;
    [SerializeField, Min(0f)] private float runRotationAmount = 1.2f;
    [Tooltip("How quickly walking and running values blend into each other (seconds).")]
    [SerializeField, Range(0.01f, 1f)] private float walkRunBlendTime = 0.25f;

    [Header("Idle")]
    [Tooltip("Up/down breathing movement when standing still, in metres.")]
    [SerializeField, Min(0f)] private float breathingAmount = 0.003f;
    [Tooltip("Breaths per second.")]
    [SerializeField, Min(0f)] private float breathingSpeed = 0.22f;
    [Tooltip("Very slow head drift when standing still, in degrees.")]
    [SerializeField, Min(0f)] private float idleSway = 0.12f;

    [Header("Jump & Fall (reads PlayerMotionState)")]
    [Tooltip("Head lift when a jump starts, metres.")]
    [SerializeField, Min(0f)] private float jumpStartLift = 0.018f;
    [Tooltip("Share of the lift kept while still rising (fades to 0 at the apex).")]
    [SerializeField, Range(0f, 1f)] private float ascendLift = 0.5f;
    [Tooltip("Head sinks while falling, metres at maximum fall speed.")]
    [SerializeField, Min(0f)] private float fallingDrop = 0.035f;
    [Tooltip("Head tips down while falling, degrees at maximum fall speed.")]
    [SerializeField, Min(0f)] private float fallingPitch = 1.5f;
    [Tooltip("How quickly the head follows jump changes (seconds).")]
    [SerializeField, Range(0.01f, 0.5f)] private float jumpSmoothing = 0.1f;
    [Tooltip("How slowly the head sinks as a fall builds (seconds).")]
    [SerializeField, Range(0.01f, 1f)] private float fallSmoothing = 0.35f;

    [Header("Landing (spring timing & strength live on PlayerMotionState)")]
    [Tooltip("How far the head dips at full compression on the hardest landing, metres.")]
    [SerializeField, Min(0f)] private float landingShakeAmount = 0.06f;
    [Tooltip("How far the head nods down at full compression on the hardest landing, degrees.")]
    [SerializeField, Min(0f)] private float landingRotationAmount = 2.2f;
    [Tooltip("Sideways tilt on landing, degrees at full strength.")]
    [SerializeField, Min(0f)] private float landingTilt = 0.8f;

    [Header("Movement Sway")]
    [Tooltip("Head lags back when speeding up and leans forward when stopping, in metres per m/s².")]
    [SerializeField, Min(0f)] private float forwardBackAmount = 0.0015f;
    [Tooltip("Head shifts toward the strafe direction, in metres per m/s.")]
    [SerializeField, Min(0f)] private float sidewaysAmount = 0.002f;
    [Tooltip("Head tilts into strafes, in degrees per m/s.")]
    [SerializeField, Min(0f)] private float swayRotationAmount = 0.35f;
    [Tooltip("How gently the sway follows changes in movement (seconds).")]
    [SerializeField, Range(0.01f, 0.5f)] private float swaySmoothness = 0.15f;

    [Header("Mining Hit")]
    [Tooltip("Head nods down when the pickaxe hits something (degrees). 0 = off.")]
    [SerializeField, Min(0f)] private float miningImpactNod = 0.7f;
    [Tooltip("Head drops when the pickaxe hits something (metres).")]
    [SerializeField, Min(0f)] private float miningImpactDrop = 0.006f;
    [Tooltip("Small random roll on a hit (degrees).")]
    [SerializeField, Min(0f)] private float miningImpactRoll = 0.3f;

    [Header("Shake (damage hook)")]
    [Tooltip("Rotation at full shake strength, in degrees. Used by Shake().")]
    [SerializeField, Min(0f)] private float shakeMaxAngle = 2f;
    [Tooltip("How fast a shake dies out. Higher = shorter.")]
    [SerializeField, Min(0.1f)] private float shakeDecay = 3f;

    [Header("References (auto-found if empty)")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerMotionState motionState;
    [Tooltip("Used only when there's no PlayerEquipment; otherwise the held item's controller drives the swing reaction.")]
    [SerializeField] private PickaxeSwing pickaxeSwing;
    [SerializeField] private PlayerEquipment equipment;
    [Tooltip("Lowers the camera while crouched.")]
    [SerializeField] private PlayerCrouch crouch;

    private HeldItemController heldItem; // the equipped item's behaviour (pickaxe, hammer...)

    // Neutral pose: wherever CameraRoot sits when the game starts.
    private Vector3 basePosition;
    private Quaternion baseRotation;

    // Smoothed continuous layers (bob + idle + sway).
    private Vector3 smoothPos, smoothPosVel;
    private Vector3 smoothRot, smoothRotVel;

    private float bobPhase;
    private float moveWeight, moveWeightVel;
    private float runBlend, runBlendVel;
    private float idleTime;

    private Vector3 swayVelocity, swayVelocityVel; // smoothed local velocity
    private Vector3 swayAccel, swayAccelVel;       // smoothed local acceleration
    private Vector3 lastLocalVelocity;

    private float airY, airYVel, airPitch, airPitchVel;

    private float hitTimer = -1f, hitRollSign = 1f;
    private float shakeStrength;
    private float noiseSeed;

    private void Awake()
    {
        basePosition = transform.localPosition;
        baseRotation = transform.localRotation;
        if (playerMovement == null) playerMovement = GetComponentInParent<PlayerMovement>();
        if (characterController == null) characterController = GetComponentInParent<CharacterController>();
        if (motionState == null) motionState = GetComponentInParent<PlayerMotionState>();
        if (pickaxeSwing == null) pickaxeSwing = GetComponentInChildren<PickaxeSwing>();
        if (equipment == null) equipment = GetComponentInParent<PlayerEquipment>();
        if (crouch == null) crouch = GetComponentInParent<PlayerCrouch>();
        noiseSeed = Random.value * 100f;
    }

    private void OnEnable()
    {
        if (equipment != null) { equipment.Changed += BindHeldItem; BindHeldItem(); }
        else if (pickaxeSwing != null) pickaxeSwing.HitLanded += OnPickaxeHit;
    }

    private void OnDisable()
    {
        if (equipment != null) { equipment.Changed -= BindHeldItem; SetHeldItem(null); }
        else if (pickaxeSwing != null) pickaxeSwing.HitLanded -= OnPickaxeHit;
        transform.localPosition = basePosition;
        transform.localRotation = baseRotation;
    }

    private void BindHeldItem() => SetHeldItem(equipment.ActiveController);

    private void SetHeldItem(HeldItemController item)
    {
        if (heldItem == item) return;
        if (heldItem != null) heldItem.HitLanded -= OnPickaxeHit;
        heldItem = item;
        if (heldItem != null) heldItem.HitLanded += OnPickaxeHit;
    }

    /// <summary>
    /// General-purpose shake, e.g. for taking damage or explosions later.
    /// 0 = nothing, 1 = full strength (Shake Max Angle).
    /// </summary>
    public void Shake(float amount) => shakeStrength = Mathf.Clamp01(Mathf.Max(shakeStrength, amount));

    private void OnPickaxeHit()
    {
        hitTimer = 0f;
        hitRollSign = Random.value < 0.5f ? -1f : 1f;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // --- Read the existing movement state ----------------------------------
        bool grounded = playerMovement != null ? playerMovement.IsGrounded
                      : characterController != null && characterController.isGrounded;
        bool sprinting = playerMovement != null && playerMovement.IsSprinting;
        Vector3 velocity = characterController != null ? characterController.velocity : Vector3.zero;
        Vector3 localVelocity = transform.parent != null
            ? transform.parent.InverseTransformDirection(velocity) : velocity;
        float horizontalSpeed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
        float walkSpeed = playerMovement != null ? playerMovement.WalkSpeed : 4f;

        Vector3 pos = Vector3.zero; // continuous layers, smoothed below
        Vector3 rot = Vector3.zero;

        // --- Walk / run bob ------------------------------------------------------
        float targetMove = grounded ? Mathf.Clamp01(horizontalSpeed / Mathf.Max(walkSpeed, 0.01f)) : 0f;
        moveWeight = Mathf.SmoothDamp(moveWeight, targetMove, ref moveWeightVel, 0.12f);
        runBlend = Mathf.SmoothDamp(runBlend, sprinting && horizontalSpeed > 0.5f ? 1f : 0f, ref runBlendVel, walkRunBlendTime);

        float frequency = Mathf.Lerp(walkBobFrequency, runBobFrequency, runBlend);
        float vAmp = Mathf.Lerp(walkBobVerticalAmplitude, runBobVerticalAmplitude, runBlend);
        float hAmp = Mathf.Lerp(walkBobHorizontalAmplitude, runBobHorizontalAmplitude, runBlend);
        float rAmp = Mathf.Lerp(walkRotationAmount, runRotationAmount, runBlend);

        // Only advance the step cycle while actually walking, so it resumes smoothly.
        bobPhase += frequency * 2f * Mathf.PI * dt * Mathf.Clamp01(moveWeight * 1.5f);
        if (bobPhase > 1000f) bobPhase -= 4f * Mathf.PI * 100f; // keep precision, stays in phase

        // One head dip per step (vertical), one side-to-side sway per two steps (horizontal).
        // The small second harmonic makes each step land a little harder than it lifts.
        float step = Mathf.Sin(bobPhase);
        float vertical = step * 0.8f - Mathf.Abs(step) * 0.2f + Mathf.Sin(bobPhase * 2f + 0.7f) * 0.1f;
        float sideways = Mathf.Cos(bobPhase * 0.5f);

        pos += new Vector3(sideways * hAmp, vertical * vAmp, 0f) * moveWeight;
        rot += new Vector3(
            step * rAmp * 0.25f,                         // tiny nod with each step
            Mathf.Sin(bobPhase * 0.5f) * rAmp * 0.15f,   // tiny look-around
            -sideways * rAmp) * moveWeight;              // roll with the weight shift

        // --- Idle breathing (only when still and grounded) ---------------------------
        float idleWeight = grounded ? 1f - moveWeight : 0f;
        idleTime += dt;
        float breath = Mathf.Sin(idleTime * breathingSpeed * 2f * Mathf.PI);
        pos.y += breath * breathingAmount * idleWeight;
        rot += new Vector3(
            (Mathf.PerlinNoise(noiseSeed, idleTime * 0.15f) - 0.5f) * 2f * idleSway - breath * idleSway * 0.3f,
            (Mathf.PerlinNoise(noiseSeed + 5f, idleTime * 0.12f) - 0.5f) * 2f * idleSway,
            0f) * idleWeight;

        // --- Directional sway --------------------------------------------------------
        swayVelocity = Vector3.SmoothDamp(swayVelocity, localVelocity, ref swayVelocityVel, swaySmoothness);
        Vector3 accel = (localVelocity - lastLocalVelocity) / dt;
        lastLocalVelocity = localVelocity;
        accel = Vector3.ClampMagnitude(new Vector3(accel.x, 0f, accel.z), 30f);
        swayAccel = Vector3.SmoothDamp(swayAccel, accel, ref swayAccelVel, swaySmoothness);

        pos += new Vector3(swayVelocity.x * sidewaysAmount, 0f, -swayAccel.z * forwardBackAmount);
        rot += new Vector3(
            -swayAccel.z * 0.03f * swayRotationAmount,  // slight pitch on accelerate / stop
            0f,
            -swayVelocity.x * swayRotationAmount);      // tilt into strafes

        // --- Smooth the continuous layers ------------------------------------------------
        smoothPos = Vector3.SmoothDamp(smoothPos, pos, ref smoothPosVel, positionSmoothness);
        smoothRot = Vector3.SmoothDamp(smoothRot, rot, ref smoothRotVel, rotationSmoothness);

        Vector3 finalPos = smoothPos;
        Vector3 finalRot = smoothRot;

        // --- Jump / fall: state-based, follows the real vertical speed --------------------
        float yTarget = 0f, pitchTarget = 0f;
        bool falling = false;
        if (motionState != null)
        {
            switch (motionState.State)
            {
                case PlayerMotionState.MotionState.JumpStart:
                    yTarget = jumpStartLift;
                    pitchTarget = -0.4f; // chin lifts slightly with the push-off
                    break;
                case PlayerMotionState.MotionState.Ascending:
                    float rise = Mathf.Clamp01(motionState.VerticalSpeed / 7f);
                    yTarget = jumpStartLift * ascendLift * rise;
                    pitchTarget = -0.3f * rise;
                    break;
                case PlayerMotionState.MotionState.Falling:
                    falling = true;
                    yTarget = -fallingDrop * motionState.FallAmount01;
                    pitchTarget = fallingPitch * motionState.FallAmount01;
                    break;
                // Apex / Grounded / Landing: settle to neutral (the apex feels "weightless").
            }
        }
        float airSmooth = falling ? fallSmoothing : jumpSmoothing;
        airY = Mathf.SmoothDamp(airY, yTarget, ref airYVel, airSmooth);
        airPitch = Mathf.SmoothDamp(airPitch, pitchTarget, ref airPitchVel, airSmooth);
        finalPos.y += airY;
        finalRot.x += airPitch;

        // --- Impulse layers (not smoothed, so they stay snappy) -----------------------------
        if (motionState != null)
        {
            // Landing spring: compress (down + nod + tilt), small rebound, settle.
            float c = motionState.LandingCompression;
            finalPos.y -= c * landingShakeAmount;
            finalRot.x += c * landingRotationAmount;
            finalRot.z += c * landingTilt * motionState.LandingSide;
        }

        if (hitTimer >= 0f)
        {
            hitTimer += dt;
            float t = hitTimer / 0.18f;
            if (t >= 1f) hitTimer = -1f;
            else
            {
                // Sharp attack, eased recovery.
                float k = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.5f)) * (1f - t);
                finalPos.y -= k * miningImpactDrop;
                finalRot.x += k * miningImpactNod;
                finalRot.z += k * miningImpactRoll * hitRollSign;
            }
        }

        if (shakeStrength > 0f)
        {
            float time = Time.time * 25f;
            finalRot += new Vector3(
                Mathf.PerlinNoise(noiseSeed + 20f, time) - 0.5f,
                Mathf.PerlinNoise(noiseSeed + 30f, time) - 0.5f,
                (Mathf.PerlinNoise(noiseSeed + 40f, time) - 0.5f) * 0.5f) * (2f * shakeMaxAngle * shakeStrength);
            shakeStrength = Mathf.MoveTowards(shakeStrength, 0f, shakeDecay * dt);
        }

        // Tiny reaction to the held tool's swing (wind-up lean, strike kick, follow-through).
        if (equipment != null)
        {
            if (heldItem != null && heldItem.isActiveAndEnabled)
            {
                finalPos += heldItem.CameraOffset;
                finalRot += heldItem.CameraRotation;
            }
        }
        else if (pickaxeSwing != null)
        {
            finalPos += pickaxeSwing.CameraOffset;
            finalRot += pickaxeSwing.CameraRotation;
        }

        // --- Apply once ----------------------------------------------------------------------
        // Crouch height is not an effect, so Global Intensity doesn't scale it.
        Vector3 crouchOffset = crouch != null && crouch.isActiveAndEnabled ? crouch.CameraOffset : Vector3.zero;
        transform.localPosition = basePosition + finalPos * globalIntensity + crouchOffset;
        transform.localRotation = baseRotation * Quaternion.Euler(finalRot * globalIntensity);
    }
}
