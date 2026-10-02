using UnityEngine;

/// <summary>
/// Crouch pose for the character body, made from the normal animations: there is no crouch clip
/// yet, so while PlayerCrouch is down this lowers the hips, leans the upper body forward a little
/// and keeps the feet where the animation put them (Unity's humanoid foot IK bends the knees).
/// Idle / Walk / Walk Backward therefore become crouch idle / crouch walk. Bones are only changed
/// through the Animator's IK pass, so every frame starts from the clean animation (no drift).
///
/// Put it on the same object as the Animator (next to CharacterAnimator). The Animator's Base
/// Layer needs IK Pass ticked (Ore What > Add Crouch does that).
/// Replace with real crouch states when crouch clips exist.
/// </summary>
[RequireComponent(typeof(Animator))]
public class CharacterCrouchPose : MonoBehaviour
{
    [Tooltip("Auto-found on the parent if empty.")]
    [SerializeField] private PlayerCrouch crouch;
    [Tooltip("How far the hips go down when fully crouched, metres. Keep it equal to PlayerCrouch's Camera Drop " +
             "so the first-person hands stay framed as when standing.")]
    [SerializeField, Min(0f)] private float hipDrop = 0.4f;
    [Tooltip("Upper body lean forward when fully crouched, degrees.")]
    [SerializeField, Range(0f, 45f)] private float forwardLean = 10f;
    [Tooltip("Knee hint: how far in front of the feet the knees are pushed, metres.")]
    [SerializeField, Min(0f)] private float kneeForward = 0.6f;

    private Animator animator;

    /// <summary>Used when there is no PlayerCrouch (another player's body in multiplayer): how crouched, 0..1.</summary>
    public float ExternalAmount { get; set; }

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (crouch == null) crouch = GetComponentInParent<PlayerCrouch>();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (layerIndex != 0) return;
        float amount = crouch != null ? crouch.Amount : ExternalAmount;
        if (amount <= 0.001f) return;

        // Feet where the animation has them, read before the body moves.
        Vector3 leftPos = animator.GetIKPosition(AvatarIKGoal.LeftFoot);
        Quaternion leftRot = animator.GetIKRotation(AvatarIKGoal.LeftFoot);
        Vector3 rightPos = animator.GetIKPosition(AvatarIKGoal.RightFoot);
        Quaternion rightRot = animator.GetIKRotation(AvatarIKGoal.RightFoot);

        Vector3 up = transform.up;
        Vector3 forward = transform.forward;
        animator.bodyPosition -= up * (hipDrop * amount);
        animator.bodyRotation = Quaternion.AngleAxis(forwardLean * amount, transform.right) * animator.bodyRotation;

        PinFoot(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, leftPos, leftRot, up, forward, amount);
        PinFoot(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, rightPos, rightRot, up, forward, amount);
    }

    private void PinFoot(AvatarIKGoal goal, AvatarIKHint hint, Vector3 pos, Quaternion rot, Vector3 up, Vector3 forward, float weight)
    {
        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKRotationWeight(goal, weight);
        animator.SetIKPosition(goal, pos);
        animator.SetIKRotation(goal, rot);
        animator.SetIKHintPositionWeight(hint, weight);
        animator.SetIKHintPosition(hint, pos + up * 0.5f + forward * kneeForward);
    }
}
