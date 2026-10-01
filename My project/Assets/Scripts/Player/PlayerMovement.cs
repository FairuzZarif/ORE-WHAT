using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Moves the player with a CharacterController: WASD walking, Shift sprinting,
/// Space jumping, and gravity. Rotation is handled by PlayerLook.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Speed")]
    [Tooltip("Walking speed in metres per second.")]
    [SerializeField] private float walkSpeed = 4f;
    [Tooltip("Sprinting speed in metres per second (hold Shift).")]
    [SerializeField] private float sprintSpeed = 7f;
    [Tooltip("How quickly the player reaches target speed. Higher = snappier.")]
    [SerializeField] private float acceleration = 12f;

    [Header("Jumping & Gravity")]
    [Tooltip("How high a jump goes, in metres.")]
    [SerializeField] private float jumpHeight = 1.2f;
    [Tooltip("Downward acceleration. Real-world gravity is -9.81.")]
    [SerializeField] private float gravity = -20f;
    [Tooltip("Small constant downward push while grounded so the controller stays snapped to the floor.")]
    [SerializeField] private float groundedStickForce = -2f;

    private CharacterController controller;
    private Vector3 horizontalVelocity; // x/z movement, smoothed
    private float verticalVelocity;     // y movement (jumping/falling)

    /// <summary>True when the controller is touching the ground this frame.</summary>
    public bool IsGrounded => controller.isGrounded;

    /// <summary>True while the sprint key is held, the player is moving forward and has stamina to sprint.</summary>
    public bool IsSprinting { get; private set; }

    /// <summary>True while the sprint key is held, whether or not sprinting is possible.</summary>
    public bool SprintHeld { get; private set; }

    /// <summary>Sprinting AND actually moving (not blocked by a wall or standing still). Uses stamina.</summary>
    public bool IsRunning
    {
        get
        {
            if (!IsSprinting) return false;
            Vector3 v = controller.velocity;
            return new Vector2(v.x, v.z).magnitude > walkSpeed * 0.5f;
        }
    }

    [Tooltip("Optional. Sprinting needs stamina from this (none = unlimited sprint).")]
    [SerializeField] private PlayerAttributes attributes;

    /// <summary>Raised on the frame a jump starts (e.g. for camera / viewmodel effects).</summary>
    public event System.Action Jumped;

    /// <summary>Configured walking speed (read-only, e.g. for camera effects).</summary>
    public float WalkSpeed => walkSpeed;

    /// <summary>Configured sprinting speed (read-only, e.g. for camera effects).</summary>
    public float SprintSpeed => sprintSpeed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (attributes == null) attributes = GetComponent<PlayerAttributes>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return; // no keyboard connected

        // --- Read input -------------------------------------------------
        Vector2 input = Vector2.zero;
        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        input = Vector2.ClampMagnitude(input, 1f); // diagonals aren't faster

        SprintHeld = keyboard.leftShiftKey.isPressed;
        IsSprinting = SprintHeld && input.y > 0f && (attributes == null || attributes.CanSprint);
        float targetSpeed = IsSprinting ? sprintSpeed : walkSpeed;

        // --- Horizontal movement (relative to where the player faces) ---
        Vector3 targetVelocity = (transform.right * input.x + transform.forward * input.y) * targetSpeed;
        horizontalVelocity = Vector3.Lerp(horizontalVelocity, targetVelocity, acceleration * Time.deltaTime);

        // --- Gravity & jumping ------------------------------------------
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0f)
                verticalVelocity = groundedStickForce;

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity); // v = sqrt(2gh)
                Jumped?.Invoke();
            }
        }

        verticalVelocity += gravity * Time.deltaTime;

        // --- Apply ------------------------------------------------------
        // CharacterController.Move handles collisions, so walls block the player.
        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        CollisionFlags flags = controller.Move(velocity * Time.deltaTime);

        // Stop upward velocity when hitting a ceiling.
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;
    }
}
