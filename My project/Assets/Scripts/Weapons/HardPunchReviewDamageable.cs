using UnityEngine;

/// <summary>Created only by the opt-in acceptance review; never attached to gameplay objects.</summary>
public sealed class HardPunchReviewDamageable : MonoBehaviour, IDamageable
{
    public int Hits { get; private set; }
    public float Damage { get; private set; }
    public void TakeDamage(float damage, RaycastHit hit) { Hits++; Damage += damage; }
}
