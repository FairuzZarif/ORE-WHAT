using UnityEngine;

/// <summary>
/// A marked area of the map for future systems: where ore can spawn (and its tier), where enemies
/// or a boss will live, the player start. Data only for now; shown as a gizmo in the Scene view.
/// </summary>
public class MapZone : MonoBehaviour
{
    public enum ZoneKind { Ore, Enemy, Boss, PlayerStart, Landmark }

    [SerializeField] private ZoneKind kind = ZoneKind.Ore;
    [Tooltip("Ore: 1 = common (copper) ... 4 = rare (crystal). Enemy/Boss: difficulty.")]
    [SerializeField, Range(1, 4)] private int tier = 1;
    [SerializeField, Min(0.5f)] private float radius = 8f;
    [TextArea] [SerializeField] private string notes;

    public ZoneKind Kind => kind;
    public int Tier => tier;
    public float Radius => radius;

    public void Set(ZoneKind zoneKind, int zoneTier, float zoneRadius, string zoneNotes)
    {
        kind = zoneKind;
        tier = zoneTier;
        radius = zoneRadius;
        notes = zoneNotes;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = kind switch
        {
            ZoneKind.Ore => Color.Lerp(new Color(1f, 0.6f, 0.2f), new Color(0.3f, 0.9f, 1f), (tier - 1) / 3f),
            ZoneKind.Enemy => new Color(1f, 0.3f, 0.3f),
            ZoneKind.Boss => new Color(0.8f, 0.1f, 0.6f),
            ZoneKind.PlayerStart => new Color(0.3f, 1f, 0.4f),
            _ => Color.white,
        };
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
