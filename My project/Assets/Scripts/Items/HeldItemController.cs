using System;
using UnityEngine;

/// <summary>
/// Base for the item-specific behaviour of a held item. Put one on the root of each item's
/// first-person view (e.g. FirstPersonViewModel for the pickaxe, HammerViewModel for the hammer).
///
/// PlayerEquipment shows the view of the equipped item and exposes this as ActiveController;
/// everything that reacts to "the tool in your hands" talks to it instead of a specific tool:
///   - PlayerEquipment won't switch or throw while <see cref="IsBusy"/> (mid-strike)
///   - CameraEffects adds <see cref="CameraOffset"/>/<see cref="CameraRotation"/> and nods on <see cref="HitLanded"/>
///   - MiningController mines with it if it's a <see cref="MiningToolController"/>
///
/// A new kind of item (sword, gun, flashlight) gets its own subclass with its own attack logic.
/// </summary>
public abstract class HeldItemController : MonoBehaviour
{
    /// <summary>True while the item must not be switched away or thrown (e.g. before a strike lands).</summary>
    public abstract bool IsBusy { get; }
    /// <summary>Small camera position offset (camera space) that follows the item's animation.</summary>
    public virtual Vector3 CameraOffset => Vector3.zero;
    /// <summary>Small camera rotation (degrees, camera space) that follows the item's animation.</summary>
    public virtual Vector3 CameraRotation => Vector3.zero;

    // How this item hurts players / creatures (IDamageable). The host reads these from its own copy of the
    // item's view to check a hit someone else reports, so the values live in ONE place: the controller.
    /// <summary>Damage of one attack to a player or creature (0 = this item can't hurt them).</summary>
    public virtual float PlayerDamage => 0f;
    /// <summary>How far one attack reaches, metres.</summary>
    public virtual float AttackRange => 0f;
    /// <summary>Shortest time between two attacks, seconds.</summary>
    public virtual float AttackInterval => 0.5f;
    /// <summary>True for guns (a kill pushes the body harder than a melee hit).</summary>
    public virtual bool IsRanged => false;

    /// <summary>Raised when an attack actually hits something (camera nod, sounds...).</summary>
    public event Action HitLanded;
    protected void RaiseHitLanded() => HitLanded?.Invoke();

    /// <summary>
    /// Number of the local player's current attack (one shot, one punch, one swing); it goes up with every new attack,
    /// whatever the item. A hit on another player is sent with it, and the host takes at most ONE hit per attack per
    /// target, so one punch (or swing, or bullet) can never damage the same player twice.
    /// </summary>
    public static int CurrentAttackId { get; private set; }
    /// <summary>Call when an attack starts (a shot is fired, a punch or swing begins).</summary>
    public static void BeginAttack() => CurrentAttackId++;
}
