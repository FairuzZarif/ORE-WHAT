using UnityEngine;

/// <summary>
/// A short flinch when the character is hit, shown to the OTHER players. Put it on the object with the character's
/// Animator (NetworkPlayerAvatar adds it to its body). Pure presentation, layered on top of everything else:
///
/// After the Animator, the look tilt and the remote arm IK have posed the body this frame, it turns the spine, chest
/// and neck a few degrees away from the hit (the top of the body is pushed along the shot: shot from the front =
/// leans back, from the left = leans right...) plus a small twist toward the side that was hit, then eases back.
/// Because it turns the upper body as one piece, both arms, the held item and the grips move with it and stay
/// together. Nothing else changes: no physics, no movement, the player's real position is untouched, and the
/// Animator rewrites the bones every frame, so nothing builds up. While the body is a ragdoll it does nothing.
/// </summary>
[DefaultExecutionOrder(80)] // after RemotePlayerPresentation's arm IK (70), before the headlamp (95) and ragdoll (120)
public class CharacterHitReaction : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [Tooltip("Lean of the upper body, degrees: gun hit / melee hit.")]
    [SerializeField] private float rangedLean = 9f, meleeLean = 14f;
    [Tooltip("Twist toward the side that was hit, degrees.")]
    [SerializeField] private float twist = 7f;
    [Tooltip("How long a reaction lasts, seconds: gun hit / melee hit.")]
    [SerializeField] private float rangedTime = 0.22f, meleeTime = 0.3f;

    private Transform spine, chest, neck;
    private CharacterRagdoll ragdoll;
    private Vector3 axis;        // lean axis (world)
    private float leanAngle, twistAngle, startTime = -10f, duration = 0.25f;

    /// <summary>Reactions played (for tests).</summary>
    public int Count { get; private set; }
    /// <summary>How far through the current reaction (0 = none).</summary>
    public float Weight => Envelope((Time.time - startTime) / duration);

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        ragdoll = GetComponent<CharacterRagdoll>();
    }

    /// <summary>Flinch from a hit at point, the shot / blow travelling along direction.</summary>
    public void Play(Vector3 direction, Vector3 point, bool melee)
    {
        if (animator == null || (ragdoll != null && ragdoll.IsActive)) return;
        if (spine == null)
        {
            spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (spine == null) return;
        }
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flat.sqrMagnitude < 0.0001f) flat = -transform.forward;
        flat.Normalize();
        axis = Vector3.Cross(Vector3.up, flat); // turning around this tips the top of the body along the hit
        leanAngle = (melee ? meleeLean : rangedLean) * Random.Range(0.85f, 1.15f);
        // Hit on the right of the body → the right shoulder goes back (and the other way round).
        Vector3 toHit = Vector3.ProjectOnPlane(point - spine.position, Vector3.up);
        float side = Vector3.Dot(toHit, transform.right);
        float facing = Vector3.Dot(flat, transform.forward) < 0f ? 1f : -1f; // hit from the front or the back
        twistAngle = twist * Mathf.Clamp(side / 0.2f, -1f, 1f) * facing;
        duration = melee ? meleeTime : rangedTime;
        startTime = Time.time;
        Count++;
    }

    private void LateUpdate()
    {
        float w = Weight;
        if (w <= 0f || spine == null) return;
        if (ragdoll != null && ragdoll.IsActive) { startTime = -10f; return; } // the ragdoll owns the bones now
        Apply(spine, 0.4f, w);
        if (chest != null) Apply(chest, 0.35f, w);
        if (neck != null) Apply(neck, 0.25f, w);
    }

    private void Apply(Transform bone, float share, float w)
    {
        Quaternion turn = Quaternion.AngleAxis(leanAngle * share * w, axis) * Quaternion.AngleAxis(twistAngle * share * w, Vector3.up);
        bone.rotation = turn * bone.rotation;
    }

    /// <summary>0 → 1 quickly (the jolt, first 20%), then a smooth return to 0.</summary>
    private static float Envelope(float t)
    {
        if (t <= 0f || t >= 1f) return 0f;
        if (t < 0.2f) return Mathf.Sin(t / 0.2f * Mathf.PI * 0.5f);
        float u = (t - 0.2f) / 0.8f;
        return 1f - u * u * (3f - 2f * u);
    }
}
