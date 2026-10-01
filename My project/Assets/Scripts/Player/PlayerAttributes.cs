using System;
using UnityEngine;

/// <summary>
/// The player's health and stamina. Put it on the Player (next to PlayerMovement).
/// Works on its own: the HUD (PlayerStatsHUD) only listens to the events.
///
/// Health: TakeDamage / Heal / SetHealth, clamped to 0..Max Health. At 0 the player is Dead
/// (IsDead + Died event) - there is no respawn yet; SetHealth/Heal above 0 revives.
/// Also an IDamageable, so anything that already damages IDamageable things can hurt the player.
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
    public void TakeDamage(float amount)
    {
        if (amount <= 0f || IsDead) return;
        SetHealth(Health - amount);
    }

    /// <summary>IDamageable: the same as TakeDamage(amount); where it hit doesn't matter yet.</summary>
    public void TakeDamage(float amount, RaycastHit hit) => TakeDamage(amount);

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
