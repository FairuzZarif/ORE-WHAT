using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Put this on the Player. Left-click swings the pickaxe and fires a ray from the centre
/// of the camera. If the first thing the ray hits within range is a rock (RockHealth),
/// the rock takes damage. Works with or without a PickaxeSwing visual:
/// with one, the hit happens when the swing's strike lands (ImpactReached);
/// without one, it happens immediately on click.
/// </summary>
// Runs before PlayerLook, so the click that re-locks the cursor after Escape
// is seen as "cursor unlocked" here and doesn't also count as a swing.
[DefaultExecutionOrder(-10)]
public class MiningController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The first-person camera. Auto-found in children if left empty.")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Optional pickaxe animation. Auto-found in children if left empty. Mining works without it.")]
    [SerializeField] private PickaxeSwing pickaxeSwing;
    [Tooltip("Optional. Auto-found on this object. If present, mining only works while holding a tool that Can Mine " +
             "(e.g. not after throwing the pickaxe away).")]
    [SerializeField] private PlayerEquipment equipment;

    [Header("Mining")]
    [Tooltip("How far the pickaxe reaches, in metres.")]
    [SerializeField, Min(0.1f)] private float miningRange = 3f;
    [Tooltip("Seconds between swings.")]
    [SerializeField, Min(0f)] private float miningCooldown = 0.6f;
    [Tooltip("A click that comes slightly too early (swing still busy / cooldown) is remembered this long, " +
             "so rapid clicking chains swings smoothly instead of being dropped. 0 = off. " +
             "Only used without a pickaxe animation; with one, PickaxeSwing's Input Buffering is used.")]
    [SerializeField, Min(0f)] private float clickBuffer = 0.4f;
    [SerializeField, Min(1)] private int damagePerHit = 1;
    [Tooltip("Which layers the mining ray can hit. Leave on Everything unless you need to ignore something.")]
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Debug")]
    [Tooltip("Draws the mining ray in the Scene view (green = hit rock, red = missed).")]
    [SerializeField] private bool drawDebugRay = true;

    private float nextMineTime;
    private float bufferedClickUntil = -1f;

    /// <summary>
    /// Raised whenever a mining hit strikes a surface within range (rock or not), for effects
    /// like chips and sound. The RockHealth is null when the surface isn't mineable.
    /// </summary>
    public event System.Action<RaycastHit, RockHealth> SurfaceHit;

    private void Awake()
    {
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        if (pickaxeSwing == null) pickaxeSwing = GetComponentInChildren<PickaxeSwing>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();

        if (playerCamera == null)
            Debug.LogError("MiningController: no camera assigned or found in children.", this);
    }

    private void OnEnable()
    {
        if (pickaxeSwing != null) pickaxeSwing.ImpactReached += OnSwingImpact;
    }

    private void OnDisable()
    {
        if (pickaxeSwing != null) pickaxeSwing.ImpactReached -= OnSwingImpact;
    }

    /// <summary>True if there's an active pickaxe animation that will call us back at impact.</summary>
    private bool HasSwingVisual => pickaxeSwing != null && pickaxeSwing.isActiveAndEnabled;

    private void Update()
    {
        if (playerCamera == null) return;

        // No tool in hand (e.g. the pickaxe was thrown away): no mining.
        if (equipment != null && !equipment.CanMine)
        {
            bufferedClickUntil = -1f;
            return;
        }

        // Ignore clicks while the cursor is free (e.g. after pressing Escape).
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked)
        {
            // With a pickaxe animation, its Input Buffering setting decides how long a click may wait.
            float buffer = HasSwingVisual ? pickaxeSwing.InputBuffering : clickBuffer;
            bufferedClickUntil = Time.time + Mathf.Max(buffer, 0.0001f);
        }

        if (Time.time > bufferedClickUntil) return; // no (recent) click waiting
        if (Time.time < nextMineTime) return;

        // Don't use up the cooldown on a click that can't start a swing yet (it stays buffered).
        if (HasSwingVisual && !pickaxeSwing.CanSwing) return;

        bufferedClickUntil = -1f;
        nextMineTime = Time.time + miningCooldown;

        TryMine();
    }

    private void TryMine()
    {
        // With a pickaxe animation, the hit waits for the strike to land (OnSwingImpact).
        // Without one, mining still works: hit immediately.
        if (HasSwingVisual)
            pickaxeSwing.Swing();
        else
            ApplyHit();
    }

    /// <summary>Called by PickaxeSwing at the exact moment the strike lands.</summary>
    private void OnSwingImpact()
    {
        bool hitSomething = ApplyHit();
        pickaxeSwing.ReportImpact(hitSomething); // recoil + camera shake if we struck a surface
    }

    /// <summary>
    /// The mining raycast. Damages a rock if one is the first thing in range.
    /// Returns true if the ray struck any surface within range.
    /// </summary>
    private bool ApplyHit()
    {
        // Ray from the exact centre of the screen.
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        bool hitSomething = Physics.Raycast(ray, out RaycastHit hit, miningRange, hitLayers, QueryTriggerInteraction.Ignore);

        // Only the first thing hit counts, so walls block mining through them.
        // GetComponentInParent lets rocks be built from several child shapes.
        RockHealth rock = hitSomething ? hit.collider.GetComponentInParent<RockHealth>() : null;

        if (drawDebugRay)
        {
            Vector3 end = hitSomething ? hit.point : ray.origin + ray.direction * miningRange;
            Debug.DrawLine(ray.origin, end, rock != null ? Color.green : Color.red, 1f);
        }

        if (rock != null)
            rock.TakeHit(damagePerHit);

        if (hitSomething)
            SurfaceHit?.Invoke(hit, rock);

        return hitSomething;
    }
}
