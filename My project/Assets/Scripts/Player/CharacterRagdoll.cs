using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a humanoid character (the CorporateMiner) into a ragdoll and back. Put it on the object with the
/// character's Animator. The same component is used on the local Player's body and on the bodies other
/// players see (NetworkPlayer), so death looks the same everywhere.
///
/// The ragdoll is made from the character's OWN skeleton (hips, spine, head, arms, legs: 11 parts with
/// capsule/box/sphere colliders, Rigidbodies and CharacterJoints, sized from the bones), the first time it's
/// needed. While alive those parts are kinematic with their colliders off, so they cost nothing and the
/// Animator moves the body as usual. Activate: Animator off, physics on, a push from the killing hit.
/// Deactivate: physics off, Animator back on (it puts the body back in its animated pose the same frame).
///
/// On other players' bodies the same colliders are also the HITBOXES while alive (SetHitboxes): head, chest, arms
/// and legs, so shots know which body part they hit (BodyZone; damage per part in CombatSettings).
///
/// Nothing here is networked: every machine simulates its own copy from the same start (pose + push), so the
/// fall looks the same without sending any bone over the network.
/// </summary>
[DefaultExecutionOrder(120)] // after the Animator and the other pose scripts, so the ragdoll starts from the shown pose
public class CharacterRagdoll : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [Tooltip("Total weight of the body, kg (shared out over the parts).")]
    [SerializeField, Min(1f)] private float totalMass = 70f;
    [Tooltip("Layer of the ragdoll's colliders (Player: other players' movement and loose items ignore it the usual way).")]
    [SerializeField] private int ragdollLayer = 8;

    private class Part
    {
        public Transform bone;
        public Rigidbody body;
        public Collider collider;
        public CharacterJoint joint;
        public BodyZone zone;
    }

    private readonly List<Part> parts = new List<Part>();
    private bool built, active, pending, hitboxes;
    private int hitboxLayer;
    private Vector3 pendingImpulse, pendingPoint, pendingVelocity;
    private float pendingMaxSpeed = 4f;

    /// <summary>True while the body is a ragdoll.</summary>
    public bool IsActive => active || pending;
    /// <summary>The ragdoll's hips (to follow it with a camera), or null before it's built.</summary>
    public Transform Hips => animator != null ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
    /// <summary>The head bone, or null.</summary>
    public Transform Head => animator != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
    }

    /// <summary>
    /// Goes limp at the end of this frame (from the pose shown this frame). impulse = push of the killing hit
    /// (direction × kg·m/s) at point; velocity = how fast the character was moving (it keeps going).
    /// </summary>
    public void Activate(Vector3 impulse, Vector3 point, Vector3 velocity, float maxSpeed)
    {
        if (active) return;
        pending = true;
        pendingImpulse = impulse;
        pendingPoint = point;
        pendingVelocity = velocity;
        pendingMaxSpeed = maxSpeed;
    }

    /// <summary>Stands back up: physics off, the Animator drives the body again.</summary>
    public void Deactivate()
    {
        pending = false;
        if (!active) return;
        active = false;
        foreach (Part p in parts)
        {
            if (p.body == null) continue;
            if (!p.body.isKinematic) { p.body.linearVelocity = Vector3.zero; p.body.angularVelocity = Vector3.zero; }
            p.body.isKinematic = true;
            p.body.interpolation = RigidbodyInterpolation.None;
            p.body.detectCollisions = false;
            p.collider.enabled = false;
        }
        if (animator != null)
        {
            animator.enabled = true;
            animator.Rebind();    // the pose from before the death, not where the ragdoll left the bones
            animator.Update(0f);
        }
        if (hitboxes) ApplyHitboxes();
    }

    // ---------------------------------------------------------------- hitboxes (alive)

    /// <summary>
    /// Hitboxes: while alive, the ragdoll's own colliders (sized to the head, chest, arms and legs) follow the animated
    /// body as kinematic parts on the given layer, so weapon rays hit the real body shape and know which part they hit
    /// (<see cref="TryGetZone"/>). Used on the bodies of OTHER players (NetworkPlayerAvatar); off = colliders off again.
    /// While the body is a ragdoll the ragdoll keeps its colliders either way.
    /// </summary>
    public void SetHitboxes(bool on, int layer)
    {
        hitboxes = on;
        hitboxLayer = layer;
        if (on && !built && !Build()) return;
        if (!built || active) return;
        if (on) ApplyHitboxes();
        else foreach (Part p in parts) { p.collider.enabled = false; p.body.detectCollisions = false; }
    }

    private void ApplyHitboxes()
    {
        foreach (Part p in parts)
        {
            p.bone.gameObject.layer = hitboxLayer;
            p.body.isKinematic = true;     // follows the animation, pushes nothing
            p.body.interpolation = RigidbodyInterpolation.None; // the Animator moves these bones, not physics
            p.body.detectCollisions = true;
            p.collider.enabled = true;
        }
    }

    /// <summary>The body part a collider belongs to (false if it isn't one of this body's parts).</summary>
    public bool TryGetZone(Collider collider, out BodyZone zone)
    {
        foreach (Part p in parts)
            if (p.collider == collider) { zone = p.zone; return true; }
        zone = BodyZone.Chest;
        return false;
    }

    /// <summary>
    /// How far (metres) a point is from a body part, measured on the bones (no colliders needed, so it also works on a
    /// body without hitboxes, e.g. the host's own player). 0 = on / inside it.
    /// </summary>
    public float DistanceToZone(BodyZone zone, Vector3 point)
    {
        if (animator == null) return float.MaxValue;
        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
        Transform hips = B(HumanBodyBones.Hips), head = B(HumanBodyBones.Head), neck = B(HumanBodyBones.Neck);
        if (hips == null || head == null) return float.MaxValue;
        if (neck == null) neck = head;
        float s = Vector3.Distance(head.position, hips.position) / 0.4f; // the sizes of Build()
        switch (zone)
        {
            case BodyZone.Head:
                Vector3 centre = head.position + (head.position - neck.position).normalized * 0.12f * s;
                return Mathf.Max(0f, Vector3.Distance(point, centre) - 0.14f * s);
            case BodyZone.Chest:
                return Mathf.Max(0f, Segment(point, hips.position - (neck.position - hips.position).normalized * 0.1f * s, neck.position) - 0.18f * s);
            case BodyZone.Arms:
                return Mathf.Max(0f, Mathf.Min(Limb(point, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 1.3f),
                                               Limb(point, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 1.3f)) - 0.06f * s);
            default:
                return Mathf.Max(0f, Mathf.Min(Limb(point, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 1.15f),
                                               Limb(point, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 1.15f)) - 0.08f * s);
        }
    }

    /// <summary>The body part nearest to a point (head first when two are equally near).</summary>
    public BodyZone ClosestZone(Vector3 point)
    {
        BodyZone best = BodyZone.Chest;
        float bestDistance = float.MaxValue;
        foreach (BodyZone z in new[] { BodyZone.Head, BodyZone.Chest, BodyZone.Arms, BodyZone.Legs })
        {
            float d = DistanceToZone(z, point);
            if (d < bestDistance - 0.001f) { bestDistance = d; best = z; }
        }
        return best;
    }

    private float Limb(Vector3 point, HumanBodyBones upper, HumanBodyBones middle, HumanBodyBones end, float reach)
    {
        Transform u = animator.GetBoneTransform(upper), m = animator.GetBoneTransform(middle), e = animator.GetBoneTransform(end);
        if (u == null || m == null || e == null) return float.MaxValue;
        return Mathf.Min(Segment(point, u.position, m.position), Segment(point, m.position, m.position + (e.position - m.position) * reach));
    }

    private static float Segment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }

    private void LateUpdate()
    {
        if (!pending) return;
        pending = false;
        if (!built && !Build()) return;
        active = true;
        if (animator != null) animator.enabled = false;

        float speedCap = Mathf.Max(0.5f, pendingMaxSpeed);
        Part nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (Part p in parts)
        {
            p.collider.enabled = true;
            p.body.detectCollisions = true;
            p.body.interpolation = RigidbodyInterpolation.Interpolate;
            p.body.isKinematic = false;
            p.body.linearVelocity = Vector3.ClampMagnitude(pendingVelocity, speedCap);
            p.body.angularVelocity = Vector3.zero;
            float d = (p.bone.position - pendingPoint).sqrMagnitude;
            if (d < nearestDistance) { nearestDistance = d; nearest = p; }
        }

        // The push: most of it moves the whole body (so it falls the way it was hit), the rest goes into the part
        // that was hit (so it twists a little, like a real hit). Each part's speed change is capped.
        float mass = 0f;
        foreach (Part p in parts) mass += p.body.mass;
        Vector3 shared = pendingImpulse * 0.7f / Mathf.Max(1f, mass);
        foreach (Part p in parts) p.body.AddForce(Vector3.ClampMagnitude(shared, speedCap), ForceMode.VelocityChange);
        if (nearest != null)
            nearest.body.AddForceAtPosition(Vector3.ClampMagnitude(pendingImpulse * 0.3f, nearest.body.mass * speedCap), pendingPoint, ForceMode.Impulse);
    }

    // ---------------------------------------------------------------- building (once)

    private bool Build()
    {
        if (animator == null || !animator.isHuman) return false;
        Transform hips = Bone(HumanBodyBones.Hips), spine = Bone(HumanBodyBones.Spine), head = Bone(HumanBodyBones.Head);
        if (hips == null || spine == null || head == null) return false;
        Transform chest = Bone(HumanBodyBones.Chest) ?? spine;
        Transform neck = Bone(HumanBodyBones.Neck) ?? head;

        // A joint's limits count from the pose it was made in, so make them with straight elbows and knees
        // (then the hinge limits mean the same whatever pose the first death happened in), and put the pose back after.
        var bent = new List<(Transform bone, Quaternion local)>();
        Straighten(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, bent);
        Straighten(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, bent);
        Straighten(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, bent);
        Straighten(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, bent);

        // Sizes are for the CorporateMiner (hips to head 0.4 m) and scale with the character.
        float s = Vector3.Distance(head.position, hips.position) / 0.4f;
        Part pHips = Add(hips, null, 0.20f, BodyZone.Chest);
        Box(pHips, hips, chest, 0.34f * s, 0.22f * s, 0.22f * s);
        Part pChest = Add(chest, pHips, 0.22f, BodyZone.Chest);
        Box(pChest, chest, neck, 0.36f * s, 0.24f * s, 0.12f * s);
        Joint(pChest, false, -15f, 15f, 20f, 10f);
        Part pHead = Add(head, pChest, 0.08f, BodyZone.Head);
        var sphere = head.gameObject.AddComponent<SphereCollider>();
        sphere.radius = Local(head, 0.14f * s); // the big head + hard hat
        sphere.center = head.InverseTransformPoint(head.position + (head.position - neck.position).normalized * 0.12f * s);
        pHead.collider = sphere;
        Joint(pHead, false, -30f, 30f, 30f, 20f);

        foreach (bool left in new[] { true, false })
        {
            Transform upperArm = Bone(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lowerArm = Bone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = Bone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform upperLeg = Bone(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            Transform lowerLeg = Bone(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            Transform foot = Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
            if (upperArm != null && lowerArm != null && hand != null)
            {
                Part a = Add(upperArm, pChest, 0.03f, BodyZone.Arms); Capsule(a, upperArm, lowerArm, 0.055f * s, 1f); Joint(a, false, -40f, 40f, 80f, 60f);
                // Elbow: a hinge that only bends one way.
                Part f = Add(lowerArm, a, 0.02f, BodyZone.Arms); Capsule(f, lowerArm, hand, 0.045f * s, 1.3f); Joint(f, true, ElbowBend, 0f, 15f, 0f);
            }
            if (upperLeg != null && lowerLeg != null && foot != null)
            {
                Part u = Add(upperLeg, pHips, 0.10f, BodyZone.Legs); Capsule(u, upperLeg, lowerLeg, 0.075f * s, 1f); Joint(u, false, -20f, 20f, 60f, 30f);
                // Knee: a hinge that only bends one way.
                Part l = Add(lowerLeg, u, 0.06f, BodyZone.Legs); Capsule(l, lowerLeg, foot, 0.06f * s, 1.15f); Joint(l, true, 0f, KneeBend, 5f, 0f);
            }
        }

        foreach (Part p in parts)
        {
            p.body.isKinematic = true;
            p.body.detectCollisions = false;
            p.collider.enabled = false;
            p.bone.gameObject.layer = ragdollLayer;
        }
        foreach (var b in bent) b.bone.localRotation = b.local;
        built = true;
        return true;
    }

    /// <summary>Turns the middle bone (forearm / shin) so the limb is straight, remembering its pose.</summary>
    private void Straighten(HumanBodyBones upper, HumanBodyBones middle, HumanBodyBones end, List<(Transform, Quaternion)> saved)
    {
        Transform u = Bone(upper), m = Bone(middle), e = Bone(end);
        if (u == null || m == null || e == null) return;
        saved.Add((m, m.localRotation));
        m.rotation = Quaternion.FromToRotation(e.position - m.position, m.position - u.position) * m.rotation;
    }

    // Hinge limits around the character's right axis (degrees): the elbow bends the hand forward/up, the knee the foot back.
    // (Signs checked against the run animation: with the other signs the knees folded backward.)
    private const float ElbowBend = 135f, KneeBend = -130f;

    private Transform Bone(HumanBodyBones b)
    {
        Transform t = animator.GetBoneTransform(b);
        return t != null ? t : null; // a real null, so ?? works
    }

    private Part Add(Transform bone, Part parent, float massShare, BodyZone zone)
    {
        var body = bone.gameObject.AddComponent<Rigidbody>();
        body.mass = totalMass * massShare;
        body.interpolation = RigidbodyInterpolation.None; // on only while it is a ragdoll (the Animator moves the bones otherwise)
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.linearDamping = 0.05f;
        body.angularDamping = 0.6f;
        var part = new Part { bone = bone, body = body, zone = zone };
        if (parent != null)
        {
            part.joint = bone.gameObject.AddComponent<CharacterJoint>();
            part.joint.connectedBody = parent.body;
            part.joint.enablePreprocessing = false; // steadier with these light limbs
        }
        parts.Add(part);
        return part;
    }

    /// <summary>A capsule along the bone from 'from' to 'to' (lengthScale > 1 reaches past 'to', e.g. over the hand / foot).</summary>
    private static void Capsule(Part part, Transform from, Transform to, float worldRadius, float lengthScale)
    {
        Vector3 local = from.InverseTransformPoint(to.position) * lengthScale;
        var capsule = from.gameObject.AddComponent<CapsuleCollider>();
        int axis = Dominant(local);
        capsule.direction = axis;
        capsule.radius = Local(from, worldRadius);
        capsule.height = Mathf.Abs(local[axis]) + capsule.radius * 2f;
        capsule.center = local * 0.5f;
        part.collider = capsule;
    }

    /// <summary>A box from this bone to the next one up the spine: width (side to side), depth (front to back), at least minLength long.</summary>
    private void Box(Part part, Transform from, Transform to, float width, float depth, float minLength)
    {
        Vector3 local = from.InverseTransformPoint(to.position);
        int axis = Dominant(local);
        int depthAxis = (axis + 1) % 3, widthAxis = (axis + 2) % 3;
        Vector3 forward = from.InverseTransformDirection(animator.transform.forward);
        if (Mathf.Abs(forward[widthAxis]) > Mathf.Abs(forward[depthAxis])) (depthAxis, widthAxis) = (widthAxis, depthAxis);
        Vector3 size = Vector3.zero;
        size[axis] = Mathf.Max(Mathf.Abs(local[axis]), Local(from, minLength));
        size[widthAxis] = Local(from, width);
        size[depthAxis] = Local(from, depth);
        var box = from.gameObject.AddComponent<BoxCollider>();
        box.size = size;
        box.center = local * 0.5f;
        part.collider = box;
    }

    /// <summary>
    /// Joint limits, degrees. A normal joint twists around the bone (Mixamo bones point along +Y) and swings around the
    /// character's right axis (swing1) and the other one (swing2). A hinge (elbow, knee) instead turns around the
    /// character's right axis between lowTwist and highTwist, so it bends one way only.
    /// </summary>
    private void Joint(Part part, bool hinge, float lowTwist, float highTwist, float swing1, float swing2)
    {
        CharacterJoint j = part.joint;
        if (j == null) return;
        Vector3 along = Vector3.up;
        Vector3 right = part.bone.InverseTransformDirection(animator.transform.right).normalized;
        if (hinge) { j.axis = right; j.swingAxis = Vector3.ProjectOnPlane(along, right).normalized; }
        else { j.axis = along; j.swingAxis = Vector3.ProjectOnPlane(right, along).normalized; }
        j.lowTwistLimit = new SoftJointLimit { limit = Mathf.Min(lowTwist, highTwist) };
        j.highTwistLimit = new SoftJointLimit { limit = Mathf.Max(lowTwist, highTwist) };
        j.swing1Limit = new SoftJointLimit { limit = swing1 };
        j.swing2Limit = new SoftJointLimit { limit = swing2 };
    }

    private static int Dominant(Vector3 v)
    {
        Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        return a.x > a.y && a.x > a.z ? 0 : a.y > a.z ? 1 : 2;
    }

    /// <summary>A world length in the bone's own (scaled) units.</summary>
    private static float Local(Transform bone, float world) => world / Mathf.Max(0.0001f, bone.lossyScale.x);
}
