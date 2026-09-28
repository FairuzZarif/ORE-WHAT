using System;
using UnityEngine;

/// <summary>
/// Works out what the player's body is doing in the air, from the existing PlayerMovement
/// and CharacterController, so the camera and the first-person arms can react the same way:
///
///   Grounded → JumpStart → Ascending → Apex → Falling → Landing → Grounded
///   (walking off a ledge goes straight to Falling)
///
/// It also owns the landing "spring": a landing kicks it by an amount based on how fast
/// the player was falling, and it compresses, rebounds a little, and settles.
/// CameraEffects and ViewModelMotion both read LandingCompression, so they stay in sync.
/// </summary>
[DefaultExecutionOrder(50)] // after PlayerMovement has moved the player this frame
public class PlayerMotionState : MonoBehaviour
{
    public enum MotionState { Grounded, JumpStart, Ascending, Apex, Falling, Landing }

    [Header("Detection")]
    [Tooltip("How long the 'jump start' push lasts before the player counts as ascending (seconds).")]
    [SerializeField, Min(0f)] private float jumpStartDuration = 0.12f;
    [Tooltip("Vertical speed (m/s) below which the top of the jump counts as the apex.")]
    [SerializeField, Min(0.1f)] private float apexSpeed = 1.2f;
    [Tooltip("How long the Landing state lasts before returning to Grounded (seconds).")]
    [SerializeField, Min(0f)] private float landingStateDuration = 0.35f;

    [Header("Landing Strength")]
    [Tooltip("Falls slower than this (m/s) have no landing effect: steps, slopes, tiny drops.")]
    [SerializeField, Min(0f)] private float minLandingSpeed = 2.5f;
    [Tooltip("Fall speed (m/s) that gives the maximum landing effect. Faster is clamped.")]
    [SerializeField, Min(0.1f)] private float maxLandingSpeed = 16f;
    [Tooltip("Maps fall speed (0 = min, 1 = max) to landing strength (0..1).")]
    [SerializeField] private AnimationCurve landingStrengthCurve = AnimationCurve.Linear(0f, 0.15f, 1f, 1f);

    [Header("Landing Spring")]
    [Tooltip("How fast the compression/rebound happens. Higher = quicker, snappier landing.")]
    [SerializeField, Range(1f, 10f)] private float landingFrequency = 4f;
    [Tooltip("Lower = more rebound. 1 = no rebound at all.")]
    [SerializeField, Range(0.1f, 1f)] private float landingDamping = 0.42f;

    [Header("References (auto-found if empty)")]
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private CharacterController controller;

    public MotionState State { get; private set; } = MotionState.Grounded;
    /// <summary>Seconds spent in the current state.</summary>
    public float StateTime { get; private set; }
    /// <summary>Actual vertical speed (m/s), + up / - down.</summary>
    public float VerticalSpeed { get; private set; }
    /// <summary>Downward speed as 0..1 of Max Landing Speed (0 when rising or grounded).</summary>
    public float FallAmount01 => State == MotionState.Falling || State == MotionState.Apex
        ? Mathf.Clamp01(-VerticalSpeed / maxLandingSpeed) : 0f;
    /// <summary>Landing spring: ~0 at rest, up to ~1 at full compression, slightly negative on the rebound.</summary>
    public float LandingCompression { get; private set; }
    /// <summary>+1 or -1, picked per landing, for effects that tilt to one side.</summary>
    public float LandingSide { get; private set; } = 1f;
    public bool IsAirborne => State != MotionState.Grounded && State != MotionState.Landing;

    /// <summary>Raised on landing with the strength (0..1). Not raised for tiny drops.</summary>
    public event Action<float> Landed;
    /// <summary>Raised when a jump starts.</summary>
    public event Action JumpStarted;

    private bool pendingJump;
    private float peakFallSpeed;
    private float springVelocity;

    private void Awake()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (controller == null) controller = GetComponent<CharacterController>();
    }

    private void OnEnable() { if (movement != null) movement.Jumped += OnJumped; }
    private void OnDisable() { if (movement != null) movement.Jumped -= OnJumped; }
    private void OnJumped() => pendingJump = true;

    private void Update()
    {
        float dt = Time.deltaTime;
        StateTime += dt;

        bool grounded = movement != null ? movement.IsGrounded : controller.isGrounded;
        VerticalSpeed = controller != null ? controller.velocity.y : 0f;

        if (pendingJump)
        {
            pendingJump = false;
            SetState(MotionState.JumpStart);
            JumpStarted?.Invoke();
        }

        if (!grounded)
        {
            peakFallSpeed = Mathf.Max(peakFallSpeed, -VerticalSpeed);
            switch (State)
            {
                case MotionState.Grounded:
                case MotionState.Landing: // left the ground without jumping (ledge, slope)
                    SetState(VerticalSpeed > apexSpeed ? MotionState.Ascending : MotionState.Falling);
                    break;
                case MotionState.JumpStart:
                    if (StateTime >= jumpStartDuration)
                        SetState(VerticalSpeed > apexSpeed ? MotionState.Ascending : MotionState.Apex);
                    break;
                case MotionState.Ascending:
                    if (VerticalSpeed <= apexSpeed) SetState(MotionState.Apex);
                    break;
                case MotionState.Apex:
                    if (VerticalSpeed < -apexSpeed) SetState(MotionState.Falling);
                    break;
            }
        }
        else if (IsAirborne && State != MotionState.JumpStart)
        {
            // Touched down: strength from how fast we were falling just before.
            if (peakFallSpeed > minLandingSpeed)
            {
                float t = Mathf.InverseLerp(minLandingSpeed, maxLandingSpeed, peakFallSpeed);
                float strength = Mathf.Clamp01(landingStrengthCurve.Evaluate(t));
                LandingSide = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                // Impulse sized so the spring's first (compression) peak is about 'strength'.
                springVelocity += strength * landingFrequency * 2f * Mathf.PI * 1.7f;
                Landed?.Invoke(strength);
            }
            SetState(MotionState.Landing);
            peakFallSpeed = 0f;
        }
        else if (State == MotionState.Landing && StateTime >= landingStateDuration)
        {
            SetState(MotionState.Grounded);
        }

        if (grounded && !IsAirborne) peakFallSpeed = 0f;

        StepSpring(dt);
    }

    private void SetState(MotionState next)
    {
        State = next;
        StateTime = 0f;
    }

    /// <summary>Damped spring: compresses, rebounds a little, settles. Sub-stepped for stability.</summary>
    private void StepSpring(float dt)
    {
        float omega = landingFrequency * 2f * Mathf.PI;
        float x = LandingCompression;
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 240f)));
        float h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            float accel = -omega * omega * x - 2f * landingDamping * omega * springVelocity;
            springVelocity += accel * h;
            x += springVelocity * h;
        }
        if (Mathf.Abs(x) < 1e-4f && Mathf.Abs(springVelocity) < 1e-3f) { x = 0f; springVelocity = 0f; }
        LandingCompression = x;
    }
}
