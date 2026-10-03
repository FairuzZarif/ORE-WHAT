using UnityEngine;

/// <summary>
/// Drives the character body's Animator from the existing movement (read only; it never moves the
/// player). Put it on CharacterVisual, next to the Animator.
/// Parameters: Speed (horizontal m/s), ForwardSpeed (m/s along the facing direction, negative =
/// backward), MoveX / MoveY (movement direction relative to the facing, length 1: x right, y forward; the
/// Walk / Run / Walk Backward direction blends use it to strafe), Grounded, VerticalSpeed, Jump (trigger),
/// and Walk/Run/WalkBackPlayback (state speeds).
///
/// Foot sliding: each locomotion clip covers a fixed ground speed at 1× (its "clip speed", measured
/// from the stride). Playback = real speed / clip speed × multiplier, so the feet keep pace with
/// the ground at any speed, up to a natural-cadence cap per state (Max Playback). Tweak the caps /
/// multipliers in the Inspector (higher = faster steps, less sliding).
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterAnimator : MonoBehaviour
{
    [Header("References (auto-found on the parent if empty)")]
    [SerializeField] private CharacterController controller;
    [SerializeField] private PlayerMotionState motionState;

    [Tooltip("How quickly Speed follows the real speed (higher = snappier). PlayerMovement already eases its speed, so keep this high.")]
    [SerializeField, Min(0.1f)] private float speedSmoothing = 25f;

    // Measured: speed of the planted foot relative to the body at 1x playback, at the model's 0.9 scale.
    [Header("Stride matching (ground speed each clip covers at 1x, m/s)")]
    [SerializeField, Min(0.1f)] private float walkClipSpeed = 1.57f;
    [SerializeField, Min(0.1f)] private float runClipSpeed = 2.57f;
    [SerializeField, Min(0.1f)] private float walkBackClipSpeed = 1.13f;
    [Tooltip("Extra playback multipliers for fine-tuning (1 = exact stride match).")]
    [SerializeField, Min(0f)] private float walkSpeedMultiplier = 1f;
    [SerializeField, Min(0f)] private float runSpeedMultiplier = 1f;
    [SerializeField, Min(0f)] private float walkBackSpeedMultiplier = 1f;
    // Natural-cadence caps. Full stride matching at 4 / 7 m/s would need ~2.5x / 2.7x (5-7 steps
    // per second, which reads as frantic shuffling): the clips' strides are short for these speeds.
    // Above the cap the feet slide a little instead of stepping unnaturally fast.
    [Tooltip("Lowest playback. Low, so the legs slow down with the body while stopping instead of cycling on the spot.")]
    [SerializeField, Min(0f)] private float minPlayback = 0.25f;
    [Tooltip("Highest Walk playback. Walking clip = 1.94 steps/s at 1x, so 1.75 = ~3.4 steps/s (4 m/s is jogging pace).")]
    [SerializeField, Min(0.1f)] private float walkMaxPlayback = 1.75f;
    [Tooltip("Highest Run playback. Run clip = 2.55 steps/s at 1x, so 1.7 = ~4.3 steps/s (sprint cadence).")]
    [SerializeField, Min(0.1f)] private float runMaxPlayback = 1.7f;
    [Tooltip("Highest Walk Backward playback. Backward clip = 1.64 steps/s at 1x, so 2.0 = ~3.3 steps/s.")]
    [SerializeField, Min(0.1f)] private float walkBackMaxPlayback = 2f;

    [Header("Sideways (jog strafe clips)")]
    [Tooltip("Ground speed the Jog Strafe Left / Right clips cover at 1x, m/s (planted foot, model at 0.9 scale).")]
    [SerializeField, Min(0.1f)] private float strafeLeftClipSpeed = 2.73f;
    [SerializeField, Min(0.1f)] private float strafeRightClipSpeed = 2.23f;
    [Tooltip("Highest strafe playback. The strafes are jogs (~2.8 steps/s at 1x), so 1.7 = ~4.8 steps/s.")]
    [SerializeField, Min(0.1f)] private float strafeMaxPlayback = 1.7f;
    [Tooltip("How quickly the movement direction (MoveX / MoveY) follows the real one, per second.")]
    [SerializeField, Min(0.1f)] private float directionSmoothing = 12f;
    [Tooltip("Below this speed (m/s) the direction is kept as it was (stopping / starting jitter).")]
    [SerializeField, Min(0f)] private float directionMinSpeed = 0.3f;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int ForwardSpeedId = Animator.StringToHash("ForwardSpeed");
    private static readonly int WalkPlaybackId = Animator.StringToHash("WalkPlayback");
    private static readonly int RunPlaybackId = Animator.StringToHash("RunPlayback");
    private static readonly int WalkBackPlaybackId = Animator.StringToHash("WalkBackPlayback");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");
    private static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
    private static readonly int JumpId = Animator.StringToHash("Jump");

    private static readonly int MoveXId = Animator.StringToHash("MoveX");
    private static readonly int MoveYId = Animator.StringToHash("MoveY");

    private Animator animator;
    private float speed;
    private float forwardSpeed;
    private Vector2 move = Vector2.up; // movement direction relative to the facing (x right, y forward), length 1

    private void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false; // the CharacterController moves the player, never the animation
        if (controller == null) controller = GetComponentInParent<CharacterController>();
        if (motionState == null) motionState = GetComponentInParent<PlayerMotionState>();
    }

    private void OnEnable()
    {
        if (motionState == null) return;
        motionState.JumpStarted += OnJump;
    }

    private void OnDisable()
    {
        if (motionState == null) return;
        motionState.JumpStarted -= OnJump;
    }

    private void OnJump() => animator.SetTrigger(JumpId);

    private void Update()
    {
        if (controller == null) return;
        Vector3 v = controller.velocity;
        float target = new Vector2(v.x, v.z).magnitude;
        float blend = 1f - Mathf.Exp(-speedSmoothing * Time.deltaTime);
        speed = Mathf.Lerp(speed, target, blend);
        // Along the player's facing (the Player root turns with the mouse; the body just follows it).
        float forwardTarget = Vector3.Dot(v, controller.transform.forward);
        forwardSpeed = Mathf.Lerp(forwardSpeed, forwardTarget, blend);

        // Which way we move relative to where we face (the body keeps facing the look; strafing = moving sideways).
        Transform facing = controller.transform;
        Vector2 local = new Vector2(Vector3.Dot(v, facing.right), Vector3.Dot(v, facing.forward));
        if (local.magnitude > directionMinSpeed)
        {
            // Turn the direction (not a straight lerp: A → D would pass through zero length).
            Vector3 turned = Vector3.Slerp(new Vector3(move.x, 0f, move.y), new Vector3(local.x, 0f, local.y).normalized,
                                           1f - Mathf.Exp(-directionSmoothing * Time.deltaTime));
            move = new Vector2(turned.x, turned.z).normalized;
        }

        animator.SetFloat(SpeedId, speed);
        animator.SetFloat(ForwardSpeedId, forwardSpeed);
        animator.SetFloat(MoveXId, move.x);
        animator.SetFloat(MoveYId, move.y);
        animator.SetFloat(WalkPlaybackId, DirectionalPlayback(walkClipSpeed, walkMaxPlayback, walkSpeedMultiplier));
        animator.SetFloat(RunPlaybackId, DirectionalPlayback(runClipSpeed, runMaxPlayback, runSpeedMultiplier));
        animator.SetFloat(WalkBackPlaybackId, DirectionalPlayback(walkBackClipSpeed, walkBackMaxPlayback, walkBackSpeedMultiplier));
        animator.SetBool(GroundedId, motionState != null ? !motionState.IsAirborne : controller.isGrounded);
        animator.SetFloat(VerticalSpeedId, motionState != null ? motionState.VerticalSpeed : v.y);
    }

    private float Playback(float clipSpeed, float maxPlayback, float multiplier)
        => Mathf.Clamp(speed / clipSpeed, minPlayback, maxPlayback) * multiplier;

    /// <summary>
    /// A state's playback when its direction blend mixes in the strafes: the clip speed and cadence cap move from the
    /// state's own clip (straight ahead / back) toward the strafe clip's as the movement turns sideways.
    /// </summary>
    private float DirectionalPlayback(float clipSpeed, float maxPlayback, float multiplier)
    {
        float side = Mathf.Abs(move.x);
        float strafeSpeed = move.x < 0f ? strafeLeftClipSpeed : strafeRightClipSpeed;
        return Playback(Mathf.Lerp(clipSpeed, strafeSpeed, side), Mathf.Lerp(maxPlayback, strafeMaxPlayback, side), multiplier);
    }
}
