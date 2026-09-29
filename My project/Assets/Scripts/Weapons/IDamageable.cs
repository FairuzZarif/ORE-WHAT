using UnityEngine;

/// <summary>
/// Anything that can be damaged by a weapon (future enemies, breakable props...).
/// Put a component implementing this on the object (or a parent of its colliders);
/// WeaponController calls it when a shot hits. Rocks keep their own RockHealth.
/// </summary>
public interface IDamageable
{
    /// <summary>Called once per shot that hits this object.</summary>
    void TakeDamage(float amount, RaycastHit hit);
}
