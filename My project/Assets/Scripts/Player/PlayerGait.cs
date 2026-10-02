using UnityEngine;

/// <summary>
/// One movement-driven step cycle shared by the camera bob and local footsteps.
/// Phase advances from actual ground travel, so walls and partial-speed movement cannot
/// play a full-speed walking rhythm.
/// </summary>
[DefaultExecutionOrder(50)] // PlayerMovement has moved; PlayerFootsteps reads this in Update.
[RequireComponent(typeof(CharacterController), typeof(PlayerMovement))]
public sealed class PlayerGait : MonoBehaviour
{
    [Header("Steps per second at configured movement speed")]
    [SerializeField, Min(0.1f)] private float walkCadence = 2.1f;
    [SerializeField, Min(0.1f)] private float sprintCadence = 2.8f;
    [SerializeField, Min(0.1f)] private float crouchCadence = 1.5f;

    [Header("Feel")]
    [SerializeField, Min(0f)] private float movementThreshold = 0.2f;
    [SerializeField, Range(0f, 0.03f)] private float stepDistanceVariation = 0.03f;
    [SerializeField, Min(0.01f)] private float stanceBlendTime = 0.25f;
    [SerializeField, Min(0f)] private float phaseResetDelay = 0.35f;

    // A footfall is at the low point of sin(PhaseRadians). The extra 2PI on every
    // other step keeps the camera's left/right sway continuous across footfalls.
    public float PhaseRadians => (cycleProgress * 2f * Mathf.PI - Mathf.PI * 0.5f)
                                 + (stepIndex & 1) * 2f * Mathf.PI;
    public bool FootfallThisFrame { get; private set; }
    public bool LastFootWasLeft { get; private set; }

    private CharacterController controller;
    private PlayerMovement movement;
    private PlayerCrouch crouch;
    private float cycleProgress = 0.75f; // first impact comes after a quarter cycle
    private float stepLengthVariation = 1f;
    private float runBlend;
    private float crouchBlend;
    private float stillTime;
    private int stepIndex;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        movement = GetComponent<PlayerMovement>();
        crouch = GetComponent<PlayerCrouch>();
    }

    private void Update()
    {
        FootfallThisFrame = false;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 velocity = controller.velocity;
        float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        bool isCrouching = crouch != null && crouch.isActiveAndEnabled && crouch.IsCrouching;
        float blendStep = dt / Mathf.Max(0.01f, stanceBlendTime);
        runBlend = Mathf.MoveTowards(runBlend,
            movement.IsSprinting && horizontalSpeed > 0.5f ? 1f : 0f, blendStep);
        crouchBlend = Mathf.MoveTowards(crouchBlend, isCrouching ? 1f : 0f, blendStep);

        if (!controller.isGrounded || horizontalSpeed < movementThreshold)
        {
            stillTime += dt;
            if (stillTime >= phaseResetDelay)
                cycleProgress = 0.75f;
            return;
        }

        stillTime = 0f;

        float walkLength = movement.WalkSpeed / Mathf.Max(0.1f, walkCadence);
        float runLength = movement.SprintSpeed / Mathf.Max(0.1f, sprintCadence);
        float crouchSpeed = crouch != null ? crouch.CrouchSpeed : movement.WalkSpeed * 0.5f;
        float crouchLength = crouchSpeed / Mathf.Max(0.1f, crouchCadence);
        float stepLength = Mathf.Lerp(Mathf.Lerp(walkLength, runLength, runBlend), crouchLength, crouchBlend);

        // Cap one-frame progress after a hitch; a dropped frame must not create a burst of impacts.
        cycleProgress += horizontalSpeed * Mathf.Min(dt, 0.25f)
                         / Mathf.Max(0.2f, stepLength * stepLengthVariation);
        if (cycleProgress < 1f) return;

        cycleProgress = Mathf.Repeat(cycleProgress, 1f);
        LastFootWasLeft = (stepIndex & 1) == 0;
        stepIndex++;
        FootfallThisFrame = true;
        stepLengthVariation = Random.Range(1f - stepDistanceVariation, 1f + stepDistanceVariation);
    }
}
