using System;
using UnityEngine;

/// <summary>
/// The light of the hardhat's headlamp. Put it on the Player; it owns a Spot Light (its child) that is
/// moved every frame to the lamp on the character's head bone, so it follows walking, jumping,
/// crouching and every animation, and is never left behind.
///
/// Aim: the local player aims it with the camera (the head bone doesn't pitch with the mouse);
/// without an Aim Source (e.g. a future remote player) it points the way the head faces.
///
/// State lives here (IsOn / SetOn / Toggle / Changed), input lives in HeadlampToggle, so a future
/// network layer can set the state of other players' lamps without the keyboard code.
/// </summary>
[DefaultExecutionOrder(95)] // after the Animator and the crouch pose (head pose final) and the camera's own motion
public class Headlamp : MonoBehaviour
{
    [SerializeField] private Light lamp;
    [Tooltip("The character's Animator; the lamp follows its head bone.")]
    [SerializeField] private Animator character;
    [Tooltip("Where the lamp's lens is, in the head bone's space (measured from the Headlamp mesh by Ore What > Add Headlamp).")]
    [SerializeField] private Vector3 lensOffset = new Vector3(0f, 0.29f, 0.125f);
    [Tooltip("Optional. The lamp points where this looks (the first-person camera). Empty = the way the head faces.")]
    [SerializeField] private Transform aimSource;
    [SerializeField] private bool startOn;

    [Header("Brightness (adapts to how far away the lamp is pointing)")]
    [Tooltip("Light intensity when the lamp points at something Reference Distance away. URP lights fade with " +
             "distance squared (25 m gets ~2% of what 6 m gets), so one fixed value either blows out nearby rock " +
             "or barely reaches down a tunnel. Instead the intensity scales with the distance straight ahead.")]
    [SerializeField, Min(0f)] private float intensity = 16f;
    [SerializeField, Min(0.5f)] private float referenceDistance = 6f;
    [Tooltip("Limits of the scaling: looking at a wall close by / looking down a long tunnel.")]
    [SerializeField, Min(0f)] private float minScale = 0.45f;
    [SerializeField, Min(1f)] private float maxScale = 12f;
    [Tooltip("What the distance ray hits (not the player or the first-person arms).")]
    [SerializeField] private LayerMask aimMask = ~((1 << 2) | (1 << 6) | (1 << 8));
    [Tooltip("How quickly the brightness follows (per second). Smooth, so sweeping past a pillar doesn't flicker.")]
    [SerializeField, Min(0.1f)] private float adaptSpeed = 5f;

    /// <summary>(isOn) whenever the lamp is switched.</summary>
    public event Action<bool> Changed;
    public bool IsOn { get; private set; }
    public Light Light => lamp;

    private Transform head;
    private float scale = 1f;

    private void Awake()
    {
        if (lamp == null) lamp = GetComponentInChildren<Light>(true);
        if (character == null)
        {
            var body = GetComponentInChildren<CharacterAnimator>(true);
            if (body != null) character = body.GetComponent<Animator>();
        }
        IsOn = startOn;
        if (lamp != null) lamp.enabled = IsOn;
    }

    /// <summary>Switches the lamp. Safe to call with the current state (does nothing).</summary>
    public void SetOn(bool on)
    {
        if (IsOn == on) return;
        IsOn = on;
        if (lamp != null) lamp.enabled = on;
        Changed?.Invoke(on);
    }

    public void Toggle() => SetOn(!IsOn);

    private void LateUpdate()
    {
        if (lamp == null) return;
        if (head == null && character != null && character.isInitialized) head = character.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return;
        Quaternion aim = aimSource != null ? aimSource.rotation : head.rotation;
        Vector3 lens = head.TransformPoint(lensOffset);
        lamp.transform.SetPositionAndRotation(lens, aim);
        if (!IsOn) return;

        float distance = Physics.Raycast(lens, aim * Vector3.forward, out RaycastHit hit, lamp.range, aimMask, QueryTriggerInteraction.Ignore)
            ? hit.distance : lamp.range;
        float target = Mathf.Clamp(distance * distance / (referenceDistance * referenceDistance), minScale, maxScale);
        scale = Mathf.Lerp(scale, target, 1f - Mathf.Exp(-adaptSpeed * Time.deltaTime));
        lamp.intensity = intensity * scale;
    }
}
