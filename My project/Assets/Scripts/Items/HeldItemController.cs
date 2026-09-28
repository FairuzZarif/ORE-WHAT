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

    /// <summary>Raised when an attack actually hits something (camera nod, sounds...).</summary>
    public event Action HitLanded;
    protected void RaiseHitLanded() => HitLanded?.Invoke();
}
