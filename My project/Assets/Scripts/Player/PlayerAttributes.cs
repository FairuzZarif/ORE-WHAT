using System;
using UnityEngine;

/// <summary>Who decides this player's health in multiplayer (the host). Null in single player.</summary>
public interface IHealthAuthority
{
    /// <summary>Asks the host to apply damage this player took from something local (not from another player).</summary>
    void RequestDamage(float amount, Vector3 impulseDirection);

    /// <summary>The dead player asks to respawn now (the death screen's RESPAWN NOW); the host decides.</summary>
    void RequestRespawn();
}

/// <summary>
/// The player's health and stamina. Put it on the Player (next to PlayerMovement).
/// Works on its own: the HUD (PlayerStatsHUD) only listens to the events.
///
/// Health: TakeDamage / Heal / SetHealth, clamped to 0..Max Health. At 0 the player is Dead
/// (IsDead + Died event, exactly once); PlayerDeath then plays the death and respawns with Revive().
/// Also an IDamageable, so anything that already damages IDamageable things can hurt the player.
///
/// Multiplayer: the host owns every player's health. NetworkPlayerHealth sets <see cref="Authority"/>, so
/// local damage is sent to the host instead of applied here, and the host's value comes back through
/// ApplyAuthoritativeHealth. In single player Authority is null and everything is local, as before.
///
/// Stamina: drains while PlayerMovement.IsRunning (sprinting AND actually moving), regenerates
/// otherwise (faster when standing still). When it runs out the player is Exhausted: sprinting
/// is refused until stamina is back to Sprint Resume Stamina.
/// </summary>
[DefaultExecutionOrder(40)] // after PlayerMovement has moved the player this frame
public class PlayerAttributes : MonoBehaviour, IDamageable
{
    [Header("Health")]
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [Tooltip("Health when the game starts (clamped to Max Health).")]
    [SerializeField, Min(0f)] private float startingHealth = 100f;

    [Header("Stamina")]
    [SerializeField, Min(1f)] private float maxStamina = 100f;
    [Tooltip("Stamina when the game starts (clamped to Max Stamina).")]
    [SerializeField, Min(0f)] private float startingStamina = 100f;
    [Tooltip("Stamina used per second while running.")]
    [SerializeField, Min(0f)] private float runDrainPerSecond = 20f;
    [Tooltip("Stamina regained per second while walking.")]
    [SerializeField, Min(0f)] private float walkRegenPerSecond = 15f;
    [Tooltip("Stamina regained per second while standing still.")]
    [SerializeField, Min(0f)] private float standRegenPerSecond = 20f;
    [Tooltip("After running out, sprinting is allowed again once stamina is back to this. Just above 0 would " +
             "make a held sprint stutter on and off every frame.")]
    [SerializeField, Min(0f)] private float sprintResumeStamina = 10f;
    [Tooltip("Below this horizontal speed (m/s) the player counts as standing still.")]
    [SerializeField, Min(0f)] private float standingSpeed = 0.2f;

    [Header("References (auto-found if empty)")]
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private CharacterController controller;

    private bool waitingForSprintRelease; // stamina hit 0 while running and sprint is still held

    public float Health { get; private set; }
    public float MaxHealth => maxHealth;
    public float Stamina { get; private set; }
    public float MaxStamina => maxStamina;
    public bool IsDead { get; private set; }
    /// <summary>Stamina ran out and hasn't recovered to Sprint Resume Stamina yet.</summary>
    public bool IsExhausted { get; private set; }
    /// <summary>PlayerMovement asks this before sprinting.</summary>
    public bool CanSprint => !IsDead && !IsExhausted && Stamina > 0f;

    /// <summary>(current, max) whenever health changes.</summary>
    public event Action<float, float> HealthChanged;
    /// <summary>(current, max) whenever stamina changes.</summary>
    public event Action<float, float> StaminaChanged;
    /// <summary>Health reached 0.</summary>
    public event Action Died;
    /// <summary>Back alive after dying (Revive or health set above 0).</summary>
    public event Action Revived;

    /// <summary>Multiplayer: the host, which owns this health. Null = single player (local health).</summary>
    public IHealthAuthority Authority { get; set; }
    /// <summary>Push of the hit that killed the player (direction × impulse), for the ragdoll. Zero if unknown.</summary>
    public Vector3 DeathImpulse { get; private set; }
    /// <summary>Where the killing hit landed (world), or the body's centre if unknown.</summary>
    public Vector3 DeathPoint { get; private set; }

    private void Awake()
    {
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (controller == null) controller = GetComponent<CharacterController>();
        Health = Mathf.Clamp(startingHealth, 0f, maxHealth);
        Stamina = Mathf.Clamp(startingStamina, 0f, maxStamina);
        IsDead = Health <= 0f;
        IsExhausted = false;
        waitingForSprintRelease = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        bool running = movement != null && movement.IsRunning;
        float before = Stamina;
        // Ran dry while sprinting: stay at 0 until the sprint key is let go.
        if (waitingForSprintRelease && (movement == null || !movement.SprintHeld)) waitingForSprintRelease = false;

        if (running)
        {
            Stamina = Mathf.Max(0f, Stamina - runDrainPerSecond * dt);
            if (Stamina <= 0f) waitingForSprintRelease = true;
        }
        else if (!waitingForSprintRelease)
        {
            Vector3 v = controller != null ? controller.velocity : Vector3.zero;
            bool standing = new Vector2(v.x, v.z).magnitude < standingSpeed;
            Stamina = Mathf.Min(maxStamina, Stamina + (standing ? standRegenPerSecond : walkRegenPerSecond) * dt);
        }

        if (Stamina <= 0f) IsExhausted = true;
        else if (IsExhausted && Stamina >= Mathf.Min(sprintResumeStamina, maxStamina)) IsExhausted = false;

        if (!Mathf.Approximately(before, Stamina)) StaminaChanged?.Invoke(Stamina, maxStamina);
    }

    // ---- Health -------------------------------------------------------------------------------

    /// <summary>Lowers health (negative amounts are ignored). At 0 the player dies.</summary>
    public void TakeDamage(float amount) => TakeDamage(amount, Vector3.zero, transform.position + Vector3.up);

    /// <summary>
    /// Lowers health; impulse = push given to the ragdoll if this hit kills (direction × strength).
    /// In multiplayer the request goes to the host, which sends the new health back.
    /// </summary>
    public void TakeDamage(float amount, Vector3 impulse, Vector3 point)
    {
        if (amount <= 0f || IsDead) return;
        if (Authority != null) { Authority.RequestDamage(amount, impulse); return; }
        DeathImpulse = impulse;
        DeathPoint = point;
        SetHealth(Health - amount);
    }

    /// <summary>IDamageable: the hit pushes the body away from the surface it struck.</summary>
    public void TakeDamage(float amount, RaycastHit hit) => TakeDamage(amount, -hit.normal * CombatSettings.Current.meleeImpulse, hit.point);

    /// <summary>Multiplayer: the host's value for this player's health (with the killing push, if it killed).</summary>
    public void ApplyAuthoritativeHealth(float value, Vector3 impulse, Vector3 point)
    {
        DeathImpulse = impulse;
        DeathPoint = point;
        SetHealth(value);
    }

    /// <summary>Back to full health and stamina after dying (PlayerDeath calls it when respawning).</summary>
    public void Revive()
    {
        DeathImpulse = Vector3.zero;
        SetHealth(maxHealth);
        waitingForSprintRelease = false;
        SetStamina(maxStamina);
    }

    /// <summary>Raises health up to Max Health (negative amounts are ignored).</summary>
    public void Heal(float amount)
    {
        if (amount <= 0f) return;
        SetHealth(Health + amount);
    }

    /// <summary>Sets health directly (clamped). Above 0 also clears the dead state.</summary>
    public void SetHealth(float value)
    {
        float before = Health;
        Health = Mathf.Clamp(value, 0f, maxHealth);
        bool wasDead = IsDead;
        IsDead = Health <= 0f;
        if (!Mathf.Approximately(before, Health)) HealthChanged?.Invoke(Health, maxHealth);
        if (IsDead && !wasDead) Died?.Invoke();
        else if (!IsDead && wasDead) Revived?.Invoke();
    }

    /// <summary>Sets stamina directly (clamped), e.g. for future items.</summary>
    public void SetStamina(float value)
    {
        Stamina = Mathf.Clamp(value, 0f, maxStamina);
        if (Stamina <= 0f) IsExhausted = true;
        else if (Stamina >= Mathf.Min(sprintResumeStamina, maxStamina)) IsExhausted = false;
        StaminaChanged?.Invoke(Stamina, maxStamina);
    }
}
