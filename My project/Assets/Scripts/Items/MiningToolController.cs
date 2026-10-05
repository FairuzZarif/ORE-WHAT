using UnityEngine;

/// <summary>
/// Shared melee attack controller (pickaxe, hammer...). Put it on the root of the
/// tool's first-person view, next to (or above) the tool's swing animation.
///
/// It holds what makes this tool different: its own swing (a PickaxeSwing configured with this
/// tool's poses and timing), how much damage one hit does, and how often it can swing.
/// MiningController (on the Player) reads these from whichever mining tool is equipped and does
/// the shared part: the click and centre-screen ray at the strike. Only ItemData.CanMine grants rock damage.
/// </summary>
public class MiningToolController : HeldItemController
{
    [Tooltip("This tool's swing animation. Auto-found in children if empty.")]
    [SerializeField] private PickaxeSwing swing;
    [Tooltip("Rock damage per hit (rocks have 5 health by default).")]
    [SerializeField, Min(1)] private int damagePerHit = 1;
    [Tooltip("Seconds between swings.")]
    [SerializeField, Min(0f)] private float swingCooldown = 0.6f;
    [Tooltip("Damage of one hit to a player or creature (IDamageable). 0 = this tool only mines.")]
    [SerializeField, Min(0f)] private float playerDamage = 20f;
    [Tooltip("Reach of a swing, metres (the same as MiningController's Mining Range).")]
    [SerializeField, Min(0.5f)] private float reach = 3f;

    public PickaxeSwing Swing => swing;
    public int DamagePerHit => damagePerHit;
    public float SwingCooldown => swingCooldown;
    public override float PlayerDamage => playerDamage;
    public override float AttackRange => reach;
    public override float AttackInterval => swingCooldown;

    public override bool IsBusy => swing != null && swing.IsSwinging && !swing.CanSwing;
    public override Vector3 CameraOffset => swing != null ? swing.CameraOffset : Vector3.zero;
    public override Vector3 CameraRotation => swing != null ? swing.CameraRotation : Vector3.zero;

    private void Awake()
    {
        if (swing == null) swing = GetComponentInChildren<PickaxeSwing>(true);
    }

    private void OnEnable() { if (swing != null) { swing.HitLanded += RaiseHitLanded; swing.SwingStarted += OnSwingStarted; } }
    private void OnDisable() { if (swing != null) { swing.HitLanded -= RaiseHitLanded; swing.SwingStarted -= OnSwingStarted; } }

    private void OnSwingStarted(int kind, float impact, float end) => BeginAttack(); // one swing = one possible hit
}
