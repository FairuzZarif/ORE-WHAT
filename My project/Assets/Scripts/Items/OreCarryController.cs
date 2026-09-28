using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Physically carries loose items (ores) in front of the player without putting them in the
/// inventory. Put it on the Player. ItemPickupInteractor decides WHEN to carry/release (E);
/// this component only does the physics.
///
/// The carried object stays a normal, non-kinematic Rigidbody in the world (so it still collides
/// with walls, rocks and other items, and can later be synced for other players). It is NOT
/// parented to the camera. Every physics step a damped spring pulls it toward the carry point:
///   acceleration = spring * (carry point - position) + damping * (carry point velocity - velocity)
/// so it follows smoothly, lags a little (more for heavier items), and can't tunnel through
/// walls: the carry point itself is pulled in front of any wall between the eyes and it.
/// Gravity is off while carried and restored on release. If the item gets snagged too far
/// away (Max Carry Distance), it's released.
///
/// Collisions: items already ignore the Player and ViewModel layers, so a carried ore never
/// pushes or blocks the player and can't collide with the first-person arms; the mining ray
/// ignores items too.
/// </summary>
public class OreCarryController : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Where carried items are held. Empty = one is created in front of the camera at Carry Distance.")]
    [SerializeField] private Transform carryPoint;

    [Header("Carry point")]
    [Tooltip("How far in front of the eyes a carried item is held (metres). Used when the Carry Point is auto-created.")]
    [SerializeField, Min(0.3f)] private float carryDistance = 1.1f;
    [Tooltip("How far below eye level it's held (metres).")]
    [SerializeField] private float carryHeight = -0.35f;
    [Tooltip("Closest the item may come to the player's centre, horizontally (metres), e.g. when looking straight down.")]
    [SerializeField, Min(0f)] private float minHorizontalDistance = 0.75f;
    [Tooltip("Layers the carry point can't be pushed through (walls, rocks, floor). Don't include Player, items or ViewModel.")]
    [SerializeField] private LayerMask blockingLayers = 1; // Default

    [Header("Carry physics")]
    [Tooltip("How strongly the item is pulled toward the carry point (1/s²). Higher = stiffer, less lag.")]
    [SerializeField, Min(1f)] private float springStrength = 70f;
    [Tooltip("1 = critically damped (no overshoot). Lower = a little bouncy, higher = sluggish.")]
    [SerializeField, Range(0.3f, 2f)] private float damping = 1f;
    [Tooltip("How much heavier items lag (0 = mass ignored, 1 = pull divided by mass).")]
    [SerializeField, Range(0f, 1f)] private float massInfluence = 0.4f;
    [Tooltip("Cap on the pulling acceleration (m/s²), so nothing ever gets launched.")]
    [SerializeField, Min(1f)] private float maxAcceleration = 60f;
    [Tooltip("Top speed of a carried item (m/s).")]
    [SerializeField, Min(0.5f)] private float maxSpeed = 10f;
    [Tooltip("How quickly the item turns to keep its orientation relative to where you face (1/s).")]
    [SerializeField, Min(0f)] private float rotationFollow = 8f;
    [Tooltip("If the item gets snagged this far from the carry point (metres), it is let go.")]
    [SerializeField, Min(0.3f)] private float maxCarryDistance = 2f;

    [Header("Release")]
    [Tooltip("Extra forward speed when letting go (m/s). 0 = just drop it.")]
    [SerializeField, Min(0f)] private float releaseForce = 0.5f;
    [Tooltip("A released item never leaves faster than this (m/s).")]
    [SerializeField, Min(0f)] private float maxReleaseSpeed = 6f;

    [Header("Limit")]
    [Tooltip("How many items can be carried at once. Extra items stack above the first.")]
    [SerializeField, Min(1)] private int maxCarried = 1;
    [Tooltip("Heaviest item that can be carried (kg). 0 = no limit.")]
    [SerializeField, Min(0f)] private float maxCarryMass = 0f;

    private class Carried
    {
        public DroppedItem item;
        public Rigidbody body;
        public bool usedGravity;
        public float angularDamping;
        public Quaternion rotationToView; // item rotation relative to the player's facing, kept while carried
    }

    private readonly List<Carried> carried = new List<Carried>();
    private Vector3 lastCarryPoint, targetVelocity;
    private bool hasLastCarryPoint;
    private CharacterController controller;

    public bool IsCarrying => carried.Count > 0;
    public int CarriedCount => carried.Count;
    public bool CanCarryMore => carried.Count < maxCarried;
    /// <summary>The most recently picked-up carried item, or null.</summary>
    public DroppedItem LastCarried => carried.Count > 0 ? carried[carried.Count - 1].item : null;
    /// <summary>Set when an item had to be let go by itself (snagged); ItemPickupInteractor shows it.</summary>
    public event System.Action<DroppedItem> DroppedBySnag;
    /// <summary>Raised whenever carrying starts or stops (PlayerEquipment switches the hands' view).</summary>
    public event System.Action CarryChanged;

    private void Awake()
    {
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        controller = GetComponent<CharacterController>();
        if (carryPoint == null && playerCamera != null)
        {
            carryPoint = new GameObject("CarryPoint").transform;
            carryPoint.SetParent(playerCamera.transform, false);
            carryPoint.localPosition = new Vector3(0f, carryHeight, carryDistance);
        }
    }

    /// <summary>Why an item can't be carried, or null if it can.</summary>
    public string CannotCarryReason(DroppedItem item)
    {
        if (item == null || item.Item == null || item.Body == null || item.IsBeingPickedUp || item.IsCarried) return "Can't carry that";
        if (!item.Item.Carryable) return $"{item.Item.DisplayName} can't be carried";
        if (!CanCarryMore) return "Hands full";
        if (maxCarryMass > 0f && item.Body.mass > maxCarryMass) return "Too heavy";
        return null;
    }

    /// <summary>Starts carrying the item. Returns false if it can't be carried.</summary>
    public bool TryCarry(DroppedItem item)
    {
        if (CannotCarryReason(item) != null) return false;
        Rigidbody body = item.Body;
        var c = new Carried
        {
            item = item,
            body = body,
            usedGravity = body.useGravity,
            angularDamping = body.angularDamping,
            rotationToView = Quaternion.Inverse(ViewYaw()) * body.rotation,
        };
        body.useGravity = false;
        body.angularDamping = 2f;
        body.WakeUp();
        item.IsCarried = true;
        carried.Add(c);
        CarryChanged?.Invoke();
        return true;
    }

    /// <summary>Lets go of the most recently carried item and sends it off with this velocity (the G throw).</summary>
    public DroppedItem ThrowLast(Vector3 velocity, Vector3 angularVelocity)
    {
        if (carried.Count == 0) return null;
        Carried c = carried[carried.Count - 1];
        Release(carried.Count - 1, toss: false);
        if (c.body != null)
        {
            c.body.linearVelocity = velocity;
            c.body.angularVelocity = angularVelocity;
            c.body.WakeUp();
        }
        return c.item;
    }

    /// <summary>Lets go of the most recently carried item. It keeps its item data and amount.</summary>
    public DroppedItem ReleaseLast()
    {
        if (carried.Count == 0) return null;
        DroppedItem item = carried[carried.Count - 1].item;
        Release(carried.Count - 1, toss: true);
        return item;
    }

    public void ReleaseAll()
    {
        for (int i = carried.Count - 1; i >= 0; i--) Release(i, toss: true);
    }

    /// <summary>
    /// Stops carrying without tossing it, restoring its physics. Used right before it's stored in
    /// the inventory (it then animates away) or when it's kept in the world.
    /// </summary>
    public void Forget(DroppedItem item)
    {
        int i = carried.FindIndex(c => c.item == item);
        if (i >= 0) Release(i, toss: false);
    }

    private void Release(int index, bool toss)
    {
        Carried c = carried[index];
        carried.RemoveAt(index);
        if (c.item != null) c.item.IsCarried = false;
        CarryChanged?.Invoke();
        if (c.body == null) return;
        c.body.useGravity = c.usedGravity;
        c.body.angularDamping = c.angularDamping;
        if (!toss) return;
        Vector3 v = Vector3.ClampMagnitude(c.body.linearVelocity, maxReleaseSpeed);
        if (playerCamera != null) v += playerCamera.transform.forward * releaseForce;
        c.body.linearVelocity = v;
        c.body.WakeUp();
    }

    private void OnDisable() => ReleaseAll();

    private void FixedUpdate()
    {
        if (carried.Count == 0 || carryPoint == null || playerCamera == null) { hasLastCarryPoint = false; return; }
        float dt = Time.fixedDeltaTime;

        // The camera moves every rendered frame but physics runs at a fixed rate, so the raw
        // per-step velocity of the carry point is uneven: smooth it so the item doesn't twitch.
        Vector3 target = SafeCarryPoint();
        Vector3 rawVelocity = hasLastCarryPoint ? (target - lastCarryPoint) / dt : Vector3.zero;
        if (controller != null) rawVelocity = Vector3.ClampMagnitude(rawVelocity, controller.velocity.magnitude + 8f);
        targetVelocity = hasLastCarryPoint ? Vector3.Lerp(targetVelocity, rawVelocity, 1f - Mathf.Exp(-dt * 15f)) : Vector3.zero;
        lastCarryPoint = target;
        hasLastCarryPoint = true;

        Quaternion yaw = ViewYaw();
        for (int i = carried.Count - 1; i >= 0; i--)
        {
            Carried c = carried[i];
            if (c.item == null || c.body == null || c.item.IsBeingPickedUp) { carried.RemoveAt(i); CarryChanged?.Invoke(); continue; }

            Vector3 goal = target + Vector3.up * (0.3f * i); // extra items stack above the first
            Vector3 toGoal = goal - c.body.position;
            if (toGoal.magnitude > maxCarryDistance)
            {
                DroppedItem snagged = c.item;
                Release(i, toss: false);
                DroppedBySnag?.Invoke(snagged);
                continue;
            }

            // Damped spring toward the carry point (mass-independent acceleration, scaled by weight).
            float k = springStrength / Mathf.Pow(Mathf.Max(0.1f, c.body.mass), massInfluence);
            float d = 2f * Mathf.Sqrt(k) * damping;
            Vector3 accel = k * toGoal + d * (targetVelocity - c.body.linearVelocity);
            c.body.AddForce(Vector3.ClampMagnitude(accel, maxAcceleration), ForceMode.Acceleration);
            if (c.body.linearVelocity.sqrMagnitude > maxSpeed * maxSpeed)
                c.body.linearVelocity = c.body.linearVelocity.normalized * maxSpeed;

            // Keep the orientation it had relative to the player's facing, with a soft rotational spring.
            Quaternion wanted = yaw * c.rotationToView;
            (wanted * Quaternion.Inverse(c.body.rotation)).ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 wantSpin = float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad * rotationFollow) : Vector3.zero;
            c.body.angularVelocity = Vector3.Lerp(c.body.angularVelocity, wantSpin, 1f - Mathf.Exp(-10f * dt));
        }
    }

    /// <summary>The carry point, pulled out of walls/floors and kept in front of the player's body.</summary>
    private Vector3 SafeCarryPoint()
    {
        Vector3 eye = playerCamera.transform.position;
        Vector3 point = carryPoint.position;

        // Looking straight down/up the point swings under or even behind you: keep it at least
        // Min Horizontal Distance out in front (measured along where you face).
        Vector3 fwd = playerCamera.transform.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = transform.forward;
        fwd.Normalize();
        Vector3 flat = point - transform.position; flat.y = 0f;
        float along = Vector3.Dot(flat, fwd);
        if (along < minHorizontalDistance) point += fwd * (minHorizontalDistance - along);

        // Never behind a wall: stop just in front of anything between the eyes and the point.
        Vector3 toPoint = point - eye;
        const float radius = 0.12f;
        if (Physics.SphereCast(eye, radius, toPoint.normalized, out RaycastHit hit, toPoint.magnitude, blockingLayers, QueryTriggerInteraction.Ignore))
            point = eye + toPoint.normalized * Mathf.Max(0.2f, hit.distance);
        return point;
    }

    private Quaternion ViewYaw()
    {
        Transform t = playerCamera != null ? playerCamera.transform : transform;
        return Quaternion.Euler(0f, t.eulerAngles.y, 0f);
    }

    /// <summary>True if this component is carrying the item.</summary>
    public bool IsCarryingItem(DroppedItem item) => carried.Exists(c => c.item == item);
}
