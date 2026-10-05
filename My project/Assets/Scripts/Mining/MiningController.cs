using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Put this on the Player. Left-click swings the held mining tool and fires a ray from the
/// centre of the camera. If the first thing the ray hits within range is a rock (RockHealth),
/// a held item with explicit CanMine capability damages the rock. With or without a swing visual:
/// with one, the hit happens when the swing's strike lands (ImpactReached);
/// without one, it happens immediately on click.
///
/// With a PlayerEquipment, the equipped tool's MiningToolController decides which swing plays,
/// the damage per hit and the cooldown (pickaxe, hammer...). Hammer still swings and deals combat damage.
/// Mining always requires an equipped item with CanMine; an absent equipment component cannot grant mining.
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

    [Header("Pickaxe miss sound")]
    [Tooltip("Clips for the Right, Left and Overhead swings, in that order.")]
    [SerializeField] private AudioClip[] pickaxeMissClips;
    [SerializeField, Range(0f, 1f)] private float pickaxeMissVolume = 0.4f;
    [SerializeField] private Vector2 pickaxeMissPitchRange = new Vector2(0.75f, 0.85f);

    [Header("Debug")]
    [Tooltip("Draws the mining ray in the Scene view (green = hit rock, red = missed).")]
    [SerializeField] private bool drawDebugRay = true;

    private float nextMineTime;
    private float bufferedClickUntil = -1f;
    private PickaxeSwing activeSwing; // the swing of the tool in hand (or Pickaxe Swing without equipment)
    private AudioSource pickaxeMissAudio;
    private int nextPickaxeMissClip;

    /// <summary>The equipped mining tool, if the held item has one.</summary>
    private MiningToolController ActiveTool => equipment != null ? equipment.ActiveController as MiningToolController : null;
    /// <summary>Legacy scene tuning used when the editor configures the pickaxe's explicit tool controller.</summary>
    public int DefaultMiningDamage => damagePerHit;

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
        pickaxeMissAudio = gameObject.AddComponent<AudioSource>();
        pickaxeMissAudio.playOnAwake = false;
        pickaxeMissAudio.spatialBlend = 0f;

        if (playerCamera == null)
            Debug.LogError("MiningController: no camera assigned or found in children.", this);
    }

    private void OnEnable()
    {
        if (equipment != null) equipment.Changed += BindSwing;
        BindSwing();
    }

    private void OnDisable()
    {
        if (equipment != null) equipment.Changed -= BindSwing;
        SetSwing(null);
    }

    /// <summary>Listens to the swing of whatever tool is in hand now.</summary>
    private void BindSwing()
    {
        MiningToolController tool = ActiveTool;
        SetSwing(equipment == null ? pickaxeSwing : tool != null ? tool.Swing : null);
    }

    private void SetSwing(PickaxeSwing swing)
    {
        if (activeSwing == swing) return;
        if (activeSwing != null) activeSwing.ImpactReached -= OnSwingImpact;
        activeSwing = swing;
        if (activeSwing != null) activeSwing.ImpactReached += OnSwingImpact;
    }

    /// <summary>True if there's an active swing animation that will call us back at impact.</summary>
    private bool HasSwingVisual => activeSwing != null && activeSwing.isActiveAndEnabled;

    private void Update()
    {
        if (playerCamera == null) return;

        // Melee tools may swing; the separate CanMine capability decides whether a node takes damage.
        if (equipment != null && (equipment.Hidden || equipment.IsCarrying || ActiveTool == null))
        {
            bufferedClickUntil = -1f;
            return;
        }

        // Ignore clicks while the cursor is free (e.g. after pressing Escape).
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked)
        {
            // With a pickaxe animation, its Input Buffering setting decides how long a click may wait.
            float buffer = HasSwingVisual ? activeSwing.InputBuffering : clickBuffer;
            bufferedClickUntil = Time.time + Mathf.Max(buffer, 0.0001f);
        }

        if (Time.time > bufferedClickUntil) return; // no (recent) click waiting
        if (Time.time < nextMineTime) return;

        // Don't use up the cooldown on a click that can't start a swing yet (it stays buffered).
        if (HasSwingVisual && !activeSwing.CanSwing) return;

        bufferedClickUntil = -1f;
        MiningToolController tool = ActiveTool;
        nextMineTime = Time.time + (tool != null ? tool.SwingCooldown : miningCooldown);

        TryMine();
    }

    private void TryMine()
    {
        // With a swing animation, the hit waits for the strike to land (OnSwingImpact).
        // Without one, mining still works: hit immediately.
        if (HasSwingVisual)
            activeSwing.Swing(); // (its tool counts the new attack when the swing starts)
        else
        {
            HeldItemController.BeginAttack();
            bool hitSomething = ApplyHit();
            if (!hitSomething && activeSwing != null && activeSwing == pickaxeSwing) PlayPickaxeMiss();
        }
    }

    /// <summary>Called by the tool's swing at the exact moment the strike lands.</summary>
    private void OnSwingImpact()
    {
        PickaxeSwing swing = activeSwing;
        bool hitSomething = ApplyHit();
        if (!hitSomething && swing != null && swing == pickaxeSwing) PlayPickaxeMiss(swing.CurrentSwingIndex);
        if (swing != null) swing.ReportImpact(hitSomething); // recoil + camera shake if we struck a surface
    }

    private void PlayPickaxeMiss(int swingIndex = -1)
    {
        if (pickaxeMissClips == null || pickaxeMissClips.Length == 0) return;
        int start = swingIndex >= 0 ? swingIndex % pickaxeMissClips.Length : nextPickaxeMissClip;
        for (int i = 0; i < pickaxeMissClips.Length; i++)
        {
            AudioClip clip = pickaxeMissClips[(start + i) % pickaxeMissClips.Length];
            if (clip == null) continue;
            if (swingIndex < 0) nextPickaxeMissClip = (start + i + 1) % pickaxeMissClips.Length;
            pickaxeMissAudio.pitch = Random.Range(pickaxeMissPitchRange.x, pickaxeMissPitchRange.y);
            pickaxeMissAudio.PlayOneShot(clip, pickaxeMissVolume);
            return;
        }
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

        if (rock != null && equipment != null && equipment.CanMine && ActiveTool != null)
        {
            MiningToolController tool = ActiveTool;
            rock.TakeMiningHit(tool.DamagePerHit, equipment.Equipped);
        }
        else if (hitSomething && !hit.collider.transform.IsChildOf(transform))
        {
            // Not a rock: a tool hit can still hurt a player or creature (their own code decides what that means).
            MiningToolController tool = ActiveTool;
            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null && tool != null && tool.PlayerDamage > 0f) target.TakeDamage(tool.PlayerDamage, hit);
        }

        if (hitSomething)
            SurfaceHit?.Invoke(hit, equipment != null && equipment.CanMine ? rock : null);

        return hitSomething;
    }
}
