using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Mouse look for a first-person player. Horizontal mouse movement turns the
/// whole player body (yaw); vertical mouse movement tilts only the camera (pitch).
/// Also locks the cursor: Escape unlocks it, left-click locks it again.
/// </summary>
public class PlayerLook : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The camera (child of the player) that tilts up and down.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Sensitivity")]
    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [Tooltip("Time in seconds to smooth mouse input. 0 = raw, no smoothing.")]
    [SerializeField, Range(0f, 0.1f)] private float smoothTime = 0.03f;
    [SerializeField] private bool invertY = false;

    [Header("Limits")]
    [Tooltip("How far up/down the camera can tilt, in degrees.")]
    [SerializeField, Range(0f, 90f)] private float maxPitch = 85f;

    private float pitch;               // current up/down angle of the camera
    private Vector2 smoothedDelta;     // smoothed mouse movement
    private Vector2 smoothVelocity;    // used internally by SmoothDamp

    private void Start()
    {
        if (cameraTransform == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) cameraTransform = cam.transform;
        }

        LockCursor(true);
    }

    private void Update()
    {
        HandleCursorLock();

        // Don't rotate while the cursor is free (e.g. after pressing Escape).
        if (Cursor.lockState != CursorLockMode.Locked) return;

        Mouse mouse = Mouse.current;
        if (mouse == null || cameraTransform == null) return;

        // Mouse delta is already "movement this frame", so no Time.deltaTime here.
        Vector2 rawDelta = mouse.delta.ReadValue() * mouseSensitivity;
        smoothedDelta = smoothTime > 0f
            ? Vector2.SmoothDamp(smoothedDelta, rawDelta, ref smoothVelocity, smoothTime)
            : rawDelta;

        // Yaw: rotate the whole player so movement always follows the view.
        transform.Rotate(Vector3.up, smoothedDelta.x, Space.Self);

        // Pitch: tilt only the camera, clamped so it can't flip upside down.
        pitch += invertY ? smoothedDelta.y : -smoothedDelta.y;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleCursorLock()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            LockCursor(false);
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
                 && Cursor.lockState != CursorLockMode.Locked)
            LockCursor(true);
    }

    public void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
        smoothedDelta = Vector2.zero; // avoid a jump when re-locking
        smoothVelocity = Vector2.zero;
    }
}
