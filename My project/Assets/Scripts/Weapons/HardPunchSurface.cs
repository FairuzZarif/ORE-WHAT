using UnityEngine;

/// <summary>
/// Opts an intentionally damageable environmental prop into hard-punch feedback.
/// Ordinary solid scenery and ore need no marker. Characters always take precedence.
/// Unmarked IDamageable targets keep their own hit feedback, including future enemies.
/// </summary>
[DisallowMultipleComponent]
public sealed class HardPunchSurface : MonoBehaviour { }
