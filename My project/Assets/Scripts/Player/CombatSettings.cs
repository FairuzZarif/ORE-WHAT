using UnityEngine;

/// <summary>Where on the body a hit landed (each part of the character's ragdoll/hitboxes belongs to one).</summary>
public enum BodyZone : byte { Chest, Head, Arms, Legs }

/// <summary>
/// The few player-vs-player and death/respawn settings, in ONE place so they're easy to find and change:
/// friendly fire, respawn delay, ragdoll pushes and how strictly the host checks a hit.
///
/// The asset lives at Assets/Resources/CombatSettings.asset (made by Ore What > Add Player Combat) and is
/// loaded with <see cref="Current"/>; without it the defaults below are used. Weapon damage is NOT here: each
/// weapon keeps its own damage on its controller (WeaponController / FistsController / MiningToolController).
/// </summary>
[CreateAssetMenu(menuName = "Ore What/Combat Settings", fileName = "CombatSettings")]
public class CombatSettings : ScriptableObject
{
    [Header("Rules")]
    [Tooltip("Players can hurt each other. There are no teams yet, so this is the only switch.")]
    public bool friendlyFire = true;

    [Header("Death & respawn")]
    [Tooltip("Seconds between dying and coming back at the spawn (automatic).")]
    [Min(0.5f)] public float respawnDelay = 5f;
    [Tooltip("The death screen has a RESPAWN NOW button.")]
    public bool allowRespawnNow = true;
    [Tooltip("RESPAWN NOW works this long after dying, seconds (so a click still meant for shooting doesn't skip the death).")]
    [Min(0f)] public float respawnNowDelay = 1f;

    [Header("Damage by body part (× the weapon's own damage)")]
    [Tooltip("Head hits (the head and hard hat).")]
    [Min(0f)] public float headMultiplier = 2f;
    [Tooltip("Chest / stomach / hips: the weapon's listed damage.")]
    [Min(0f)] public float chestMultiplier = 1f;
    [Tooltip("Upper arms, forearms and hands.")]
    [Min(0f)] public float armsMultiplier = 0.6f;
    [Tooltip("Thighs, shins and feet.")]
    [Min(0f)] public float legsMultiplier = 0.6f;
    [Tooltip("How far (metres) a hit may be from the body part the attacker reports, on the host's copy of the target, " +
             "before the host decides the part itself (lag allowance).")]
    [Min(0.05f)] public float zoneTolerance = 0.35f;

    /// <summary>The damage multiplier of a body part.</summary>
    public float Multiplier(BodyZone zone) => zone switch
    {
        BodyZone.Head => headMultiplier,
        BodyZone.Arms => armsMultiplier,
        BodyZone.Legs => legsMultiplier,
        _ => chestMultiplier,
    };

    [Header("Ragdoll push when killed (impulse, kg·m/s; the body weighs ~70 kg)")]
    [Tooltip("Killed by a gun: pushed along the shot.")]
    [Min(0f)] public float rangedImpulse = 120f;
    [Tooltip("Killed by a punch or a tool: a smaller shove.")]
    [Min(0f)] public float meleeImpulse = 70f;
    [Tooltip("No body part is pushed faster than this (m/s), so nobody is launched across the cave.")]
    [Min(0.5f)] public float maxRagdollSpeed = 4f;

    [Header("Host checks on a hit (generous, for lag)")]
    [Tooltip("How far the shot's start may be from where the host sees the shooter's eyes, metres.")]
    [Min(0.5f)] public float originTolerance = 2.5f;
    [Tooltip("How far the hit point may be from where the host sees the target, metres.")]
    [Min(0.2f)] public float targetTolerance = 1.5f;
    [Tooltip("Extra reach allowed on top of a weapon's range, metres.")]
    [Min(0f)] public float rangeTolerance = 1.5f;

    private static CombatSettings current;

    /// <summary>The project's settings (Resources/CombatSettings), or the defaults if that asset doesn't exist.</summary>
    public static CombatSettings Current
    {
        get
        {
            if (current == null) current = Resources.Load<CombatSettings>("CombatSettings");
            if (current == null) { current = CreateInstance<CombatSettings>(); current.hideFlags = HideFlags.DontSave; }
            return current;
        }
    }
}
