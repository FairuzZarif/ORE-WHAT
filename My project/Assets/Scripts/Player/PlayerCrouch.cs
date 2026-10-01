using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Crouching. Put it on the Player next to PlayerMovement and the CharacterController.
///
/// Crouch Key (C by default; Left Ctrl is already Ctrl+Q = drop whole stack) toggles crouching, or
/// crouches while held in Hold mode. While crouched PlayerMovement uses Crouch Speed and can't
/// sprint (so no stamina is used). The CharacterController shrinks from its own configured height
/// to Crouched Height with its bottom kept where it is (the centre moves down by half the height
/// change; the Player transform never moves). Standing up first checks the space above the capsule
/// against the layers the player collides with, and stays crouched if something is in the way.
///
/// The camera drop is applied by CameraEffects (it adds <see cref="CameraOffset"/> to CameraRoot)
/// and the body pose by CharacterCrouchPose, both from <see cref="Amount"/>.
/// </summary>
[DefaultExecutionOrder(-1)] // before PlayerMovement, so it moves with this frame's capsule and speed
[RequireComponent(typeof(CharacterController))]
public class PlayerCrouch : MonoBehaviour
{
    public enum CrouchMode { Toggle, Hold }

    [Header("Input")]
    [SerializeField] private Key crouchKey = Key.C;
    [Tooltip("Toggle: press to crouch, press again to stand. Hold: crouch while the key is held.")]
    [SerializeField] private CrouchMode mode = CrouchMode.Toggle;

    [Header("Movement")]
    [Tooltip("Movement speed while crouched, m/s. Keep it above ~1.8 or the body plays its Idle animation while moving.")]
    [SerializeField, Min(0.1f)] private float crouchSpeed = 2f;

    [Header("Capsule")]
    [Tooltip("CharacterController height while crouched. The standing height is read from the CharacterController when the game starts.")]
    [SerializeField, Min(0.5f)] private float crouchedHeight = 1.3f;
    [Tooltip("Seconds to go fully down or fully up.")]
    [SerializeField, Min(0.01f)] private float transitionTime = 0.25f;

    [Header("Camera")]
    [Tooltip("How far the camera goes down when fully crouched, metres (keep it equal to CharacterCrouchPose's Hip Drop so the hands stay framed as when standing).")]
    [SerializeField, Min(0f)] private float cameraDrop = 0.4f;

    [Header("Standing-up clearance")]
    [Tooltip("Layers that can block standing up. Only layers the player actually collides with (Physics collision matrix) are used, and never the player's own layer.")]
    [SerializeField] private LayerMask clearanceLayers = ~0;
    [Tooltip("The check uses a slightly thinner capsule, so walls you're touching don't count as a ceiling.")]
    [SerializeField, Range(0.5f, 1f)] private float clearanceRadiusScale = 0.95f;

    private CharacterController controller;
    private float standingHeight;
    private Vector3 standingCenter;
    private int blockingMask;
    private float progress; // 0 = standing, 1 = crouched (linear; Amount is eased)

    /// <summary>Crouched or crouching (also true while blocked from standing). PlayerMovement reads this.</summary>
    public bool IsCrouching { get; private set; }
    /// <summary>0 = standing, 1 = fully crouched, eased. Drives the capsule, camera and body pose.</summary>
    public float Amount { get; private set; }
    public float CrouchSpeed => crouchSpeed;
    public float StandingHeight => standingHeight;
    public float CrouchedHeight => crouchedHeight;
    /// <summary>Camera offset in the Player's local space, added to CameraRoot by CameraEffects.</summary>
    public Vector3 CameraOffset => Vector3.down * (cameraDrop * Amount);

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        standingHeight = controller.height;
        standingCenter = controller.center;

        int layer = gameObject.layer;
        blockingMask = 0;
        for (int i = 0; i < 32; i++)
            if (i != layer && !Physics.GetIgnoreLayerCollision(layer, i)) blockingMask |= 1 << i;
        blockingMask &= clearanceLayers;

        IsCrouching = false;
        progress = Amount = 0f;
    }

    private void OnDisable()
    {
        // Back to the configured capsule (standing may be blocked, but a disabled crouch can't hold it).
        if (controller == null) return;
        controller.height = standingHeight;
        controller.center = standingCenter;
        IsCrouching = false;
        progress = Amount = 0f;
    }

    private void Update()
    {
        ReadInput();

        float target = IsCrouching ? 1f : 0f;
        float next = Mathf.MoveTowards(progress, target, Time.deltaTime / transitionTime);
        if (next < progress && !HasClearance(HeightAt(next)))
        {
            next = progress;    // something moved in above while rising: stay down
            IsCrouching = true;
        }
        progress = next;
        Amount = Mathf.SmoothStep(0f, 1f, progress);

        float height = HeightAt(progress);
        controller.height = height;
        controller.center = standingCenter + Vector3.down * ((standingHeight - height) * 0.5f); // bottom stays put
    }

    private void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (mode == CrouchMode.Toggle)
        {
            if (!keyboard[crouchKey].wasPressedThisFrame) return;
            if (!IsCrouching) Crouch();
            else TryStand();
        }
        else
        {
            if (keyboard[crouchKey].isPressed) Crouch();
            else if (IsCrouching) TryStand();
        }
    }

    /// <summary>Starts crouching.</summary>
    public void Crouch() => IsCrouching = true;

    /// <summary>Stands up if there's room for the standing capsule. Returns false (still crouched) if blocked.</summary>
    public bool TryStand()
    {
        if (!IsCrouching) return true;
        if (!HasClearance(standingHeight)) return false;
        IsCrouching = false;
        return true;
    }

    /// <summary>True if the capsule can grow from its current height to <paramref name="height"/>.</summary>
    public bool HasClearance(float height)
    {
        if (height <= controller.height + 0.0001f) return true;
        float radius = controller.radius * clearanceRadiusScale;
        Vector3 bottom = transform.TransformPoint(standingCenter + Vector3.down * (standingHeight * 0.5f));
        Vector3 up = transform.up;
        // Sweep only the part above us: from the current top sphere to the new top sphere.
        Vector3 from = bottom + up * Mathf.Max(controller.radius, controller.height - controller.radius);
        Vector3 to = bottom + up * Mathf.Max(controller.radius, height - controller.radius);
        return !Physics.CheckCapsule(from, to, radius, blockingMask, QueryTriggerInteraction.Ignore);
    }

    private float HeightAt(float t) => Mathf.Lerp(standingHeight, crouchedHeight, Mathf.SmoothStep(0f, 1f, t));
}
