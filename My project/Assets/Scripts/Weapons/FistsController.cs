using System;
using UnityEngine;

/// <summary>
/// The unarmed punch: motion and hit, played on the fists' first-person view (FistsViewModel).
/// Not an equipped item: the view is only shown while a punch plays (UnarmedAttack starts it from
/// empty hands, PlayerEquipment shows the view); when both hands are back down, Finished fires and the
/// view is hidden again, so empty hands go back to the character's relaxed animation.
///
/// Each punch moves one hand's grip point (the IK target of the character's own arm): it rises from
/// lowered (below the screen) into a short wind-up, strikes fast toward the crosshair, holds for a
/// moment and drops back down. Hands alternate. At full extension one sphere cast along the camera
/// damages the nearest IDamageable (once per punch); anything else hit only gives a little impact.
/// Punches never mine rocks (RockHealth isn't IDamageable) and never hit the player.
/// </summary>
[DefaultExecutionOrder(-10)]
public class FistsController : HeldItemController
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform rightGrip;
    [SerializeField] private Transform leftGrip;

    [Header("Attack")]
    [SerializeField, Min(0f)] private float damage = 10f;
    [Tooltip("How far in front of the camera a punch reaches, metres.")]
    [SerializeField, Min(0.1f)] private float range = 2f;
    [Tooltip("Thickness of the punch check, metres (forgiving aim).")]
    [SerializeField, Min(0f)] private float radius = 0.25f;
    [Tooltip("Minimum time between punches, seconds.")]
    [SerializeField, Min(0f)] private float cooldown = 0.35f;
    [Tooltip("What a punch can hit (not the player, the first-person arms or loose items).")]
    [SerializeField] private LayerMask hitLayers = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));
    [Tooltip("Push given to a punched Rigidbody.")]
    [SerializeField, Min(0f)] private float impulse = 2f;

    [Header("Hand positions (view space; set by Ore What > Add Fists)")]
    [Tooltip("Where each fist is just before it strikes.")]
    [SerializeField] private Vector3 rightGuard = new Vector3(0.12f, -0.15f, 0.36f);
    [SerializeField] private Vector3 leftGuard = new Vector3(-0.14f, -0.13f, 0.40f);
    [SerializeField] private Quaternion rightGuardRotation = Quaternion.identity;
    [SerializeField] private Quaternion leftGuardRotation = Quaternion.identity;
    [Tooltip("Relaxed: hanging down, below the bottom of the screen. Punches start and end here.")]
    [SerializeField] private Vector3 rightLowered = new Vector3(0.16f, -0.7f, 0.16f);
    [SerializeField] private Vector3 leftLowered = new Vector3(-0.16f, -0.7f, 0.16f);

    [Header("Motion")]
    [Tooltip("Rise from lowered into the wind-up, seconds.")]
    [SerializeField, Min(0.01f)] private float raiseTime = 0.1f;
    [SerializeField, Min(0.01f)] private float strikeTime = 0.09f;
    [SerializeField, Min(0f)] private float holdTime = 0.04f;
    [Tooltip("Drop back down to lowered, seconds.")]
    [SerializeField, Min(0.01f)] private float recoverTime = 0.24f;
    [Tooltip("Wind-up (from the guard): pulled back and a little down.")]
    [SerializeField] private Vector3 windUpOffset = new Vector3(0f, -0.02f, -0.05f);
    [Tooltip("Fully extended punch of the RIGHT hand from its guard (the left one is mirrored).")]
    [SerializeField] private Vector3 strikeOffset = new Vector3(-0.08f, 0.04f, 0.22f);
    [Tooltip("Twist of the fist at full extension, degrees.")]
    [SerializeField] private float strikeTwist = 20f;
    [Tooltip("Camera kick when a punch connects, degrees.")]
    [SerializeField] private float impactKick = 1.2f;

    [Header("Punch sound")]
    [SerializeField] private AudioClip[] swingClips;
    [SerializeField, Range(0f, 1f)] private float swingVolume = 0.4f;
    [SerializeField] private Vector2 swingPitchRange = new Vector2(1.20f, 1.35f);

    private enum Phase { Idle, Raise, Strike, Hold, Recover }
    private class Hand { public Phase phase; public float time; public bool hardImpact; }

    private readonly Hand right = new Hand(), left = new Hand();
    private float lastPunchTime = -10f, kick;
    private bool rightHandNext = true, lastWasRight = true, wasPunching;
    private Transform player;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private AudioSource swingAudio;
    private int nextSwingClip;

    /// <summary>Never blocks switching: selecting an item just cancels the punch.</summary>
    public override bool IsBusy => false;
    public override Vector3 CameraRotation => new Vector3(-kick * 0.6f, (lastWasRight ? -1f : 1f) * kick * 0.4f, 0f);
    public override float PlayerDamage => damage;
    public override float AttackRange => range + radius;
    public override float AttackInterval => cooldown;
    /// <summary>True while either hand is moving.</summary>
    public bool IsPunching => right.phase != Phase.Idle || left.phase != Phase.Idle;
    public bool LastHitWasHardSurface { get; private set; }
    /// <summary>Raised on every punch (right hand = true), for sounds / third-person animation later.</summary>
    public event Action<bool> Punched;
    /// <summary>One confirmed environmental contact; shared as cosmetics, never as mining damage.</summary>
    public event Action<Vector3, Vector3, int> HardSurfaceHit;
    /// <summary>Both hands are back down: the punch (and any follow-up) is over.</summary>
    public event Action Finished;

    private void Awake()
    {
        if (playerCamera == null) playerCamera = GetComponentInParent<Camera>();
        if (rightGrip == null) rightGrip = transform.Find("RightHandGrip");
        if (leftGrip == null) leftGrip = transform.Find("LeftHandGrip");
        player = transform.root;
        swingAudio = gameObject.AddComponent<AudioSource>();
        swingAudio.playOnAwake = false;
        swingAudio.spatialBlend = 0f;
    }

    private void OnEnable() => ResetPose();
    private void OnDisable() => ResetPose();

    private void ResetPose()
    {
        right.phase = left.phase = Phase.Idle;
        right.hardImpact = left.hardImpact = false;
        wasPunching = false;
        kick = 0f;
        Place(true, Phase.Idle, 0f);
        Place(false, Phase.Idle, 0f);
    }

    /// <summary>Starts a punch with the next hand (alternating). False if still cooling down or both hands are busy.</summary>
    public bool Punch()
    {
        if (Time.time - lastPunchTime < cooldown) return false;
        bool useRight = rightHandNext;
        if ((useRight ? right : left).phase != Phase.Idle) useRight = !useRight;
        Hand hand = useRight ? right : left;
        if (hand.phase != Phase.Idle) return false;

        lastPunchTime = Time.time;
        BeginAttack(); // this punch can damage a given player once
        rightHandNext = !useRight;
        lastWasRight = useRight;
        hand.phase = Phase.Raise;
        hand.time = 0f;
        hand.hardImpact = false;
        wasPunching = true;
        Punched?.Invoke(useRight);
        return true;
    }

    // After ViewModelMotion (the view's bob), before the arm IK (order 100).
    private void LateUpdate()
    {
        kick = Mathf.MoveTowards(kick, 0f, Time.deltaTime * 12f);
        Advance(right, true);
        Advance(left, false);
        if (wasPunching && !IsPunching)
        {
            wasPunching = false;
            Finished?.Invoke();
        }
    }

    private void Advance(Hand hand, bool isRight)
    {
        if (hand.phase == Phase.Idle) { Place(isRight, Phase.Idle, 0f); return; }
        hand.time += Time.deltaTime;
        float duration = hand.phase == Phase.Raise ? raiseTime : hand.phase == Phase.Strike ? strikeTime : hand.phase == Phase.Hold ? holdTime : recoverTime;
        float t = Mathf.Clamp01(hand.time / Mathf.Max(0.0001f, duration));
        Place(isRight, hand.phase, t);
        if (t < 1f) return;

        if (hand.phase == Phase.Raise) PlayPunchSwing();
        if (hand.phase == Phase.Strike) Impact(hand);
        hand.time = 0f;
        hand.phase = hand.phase == Phase.Raise ? Phase.Strike : hand.phase == Phase.Strike ? Phase.Hold : hand.phase == Phase.Hold ? Phase.Recover : Phase.Idle;
        if (hand.phase == Phase.Idle) Place(isRight, Phase.Idle, 0f);
    }

    private void PlayPunchSwing()
    {
        if (swingClips == null || swingClips.Length == 0) return;
        for (int i = 0; i < swingClips.Length; i++)
        {
            AudioClip clip = swingClips[nextSwingClip++ % swingClips.Length];
            if (clip == null) continue;
            swingAudio.pitch = UnityEngine.Random.Range(swingPitchRange.x, swingPitchRange.y);
            swingAudio.PlayOneShot(clip, swingVolume);
            return;
        }
    }

    /// <summary>Puts one hand's grip point where it is along the punch.</summary>
    private void Place(bool isRight, Phase phase, float t)
    {
        Transform grip = isRight ? rightGrip : leftGrip;
        if (grip == null) return;
        Vector3 guard = isRight ? rightGuard : leftGuard, lowered = isRight ? rightLowered : leftLowered;
        Vector3 windUp = guard + Mirror(windUpOffset, isRight), strike = guard + Mirror(strikeOffset, isRight);
        Vector3 position; float twist;
        switch (phase)
        {
            case Phase.Raise: position = Vector3.Lerp(lowered, windUp, Smooth(t)); twist = 0f; break;
            case Phase.Strike:
                float s = 1f - (1f - t) * (1f - t) * (1f - t); // fast out, decelerating into full extension
                position = Vector3.LerpUnclamped(windUp, strike, s); twist = strikeTwist * s; break;
            case Phase.Hold: position = strike; twist = strikeTwist; break;
            case Phase.Recover: position = Vector3.Lerp(strike, lowered, Smooth(t)); twist = strikeTwist * (1f - Smooth(t)); break;
            default: position = lowered; twist = 0f; break;
        }
        // A contacted fist pulls back 4 cm, then blends into its existing recovery.
        Hand hand = isRight ? right : left;
        if (hand.hardImpact && (phase == Phase.Hold || phase == Phase.Recover))
            position.z -= 0.04f * (phase == Phase.Hold ? Smooth(t) : 1f - Smooth(t));
        Quaternion rotation = Quaternion.AngleAxis(isRight ? twist : -twist, Vector3.forward) * (isRight ? rightGuardRotation : leftGuardRotation);
        grip.SetLocalPositionAndRotation(position, rotation);
    }

    private static Vector3 Mirror(Vector3 v, bool isRight) => isRight ? v : new Vector3(-v.x, v.y, v.z);

    /// <summary>One check at full extension: damage the nearest damageable thing in reach (once per punch).</summary>
    private void Impact(Hand hand)
    {
        if (playerCamera == null) return;
        Transform eye = playerCamera.transform;
        int count = Physics.SphereCastNonAlloc(eye.position, radius, eye.forward, hits, range, hitLayers, QueryTriggerInteraction.Ignore);
        int best = -1;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(player)) continue; // never ourselves
            if (best < 0 || hits[i].distance < hits[best].distance) best = i;
        }
        if (best < 0) return; // a miss: no feedback

        RaycastHit hit = hits[best];
        var target = hit.collider.GetComponentInParent<IDamageable>();
        // The sphere is forgiving and may touch a shoulder first; if the aim line itself lands on the same target,
        // use that point instead, so the body part hit is the one aimed at (head, chest...).
        if (target != null && Physics.Raycast(eye.position, eye.forward, out RaycastHit aimed, range + radius, hitLayers, QueryTriggerInteraction.Ignore)
            && aimed.collider.GetComponentInParent<IDamageable>() == target)
            hit = aimed;
        // (distance 0 = it was already touching the fist's sphere: still a hit, but Unity gives no hit point)
        // Project onto this already-confirmed collider for overlapping casts. ClosestPoint isn't supported
        // on non-convex cave MeshColliders; a ray against this collider also supplies its surface normal.
        if (hit.distance <= 0f && hit.collider.Raycast(new Ray(eye.position, eye.forward), out RaycastHit contact, range + radius))
            hit = contact;
        else if (hit.distance <= 0f && hit.collider.Raycast(new Ray(eye.position - eye.forward * (radius + 0.02f), eye.forward), out contact, range + radius * 2f))
            hit = contact;
        bool hard = HardPunchImpactFX.IsHardSurface(hit.collider);
        Vector3 point = hit.point;
        Vector3 normal = hit.normal;
        if (hit.distance <= 0f)
        {
            bool supportsClosestPoint = !(hit.collider is TerrainCollider) && !(hit.collider is MeshCollider mesh && !mesh.convex);
            if (supportsClosestPoint)
            {
                point = hit.collider.ClosestPoint(eye.position);
                normal = (eye.position - point).normalized;
                if (normal.sqrMagnitude < 0.001f) hard = false; // camera inside solid geometry has no usable contact
            }
            else if (hit.collider.Raycast(new Ray(eye.position, hit.collider.bounds.center - eye.position), out contact, range + radius))
            { point = contact.point; normal = contact.normal; }
            else hard = false; // no valid surface point: never emit blood at Unity's unset (0,0,0)
        }
        if (target != null) target.TakeDamage(damage, hit);
        LastHitWasHardSurface = hard;
        if (hit.rigidbody != null && !hit.rigidbody.isKinematic) hit.rigidbody.AddForceAtPosition(eye.forward * impulse, point, ForceMode.Impulse);
        if (hard)
        {
            hand.hardImpact = true;
            kick = Mathf.Min(impactKick, 0.45f);
            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            HardPunchImpactFX.Play(point, normal.sqrMagnitude > 0.001f ? normal : -eye.forward, seed);
            HardSurfaceHit?.Invoke(point, normal.sqrMagnitude > 0.001f ? normal : -eye.forward, seed);
        }
        else
        {
            kick = impactKick;
        }
        RaiseHitLanded(); // one shared contact notification; CameraEffects scales the hard-surface nod
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
