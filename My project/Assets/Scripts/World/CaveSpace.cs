using UnityEngine;

/// <summary>
/// One room, tunnel or open-air cut of the cave. Children of the CaveLayout; the island map builder
/// carves them out of the mountain. Move / resize them in the Scene view, then rebuild
/// (Ore What > Island Map > Build Or Rebuild Island Map).
///
/// This object's position is on the FLOOR (rooms: middle of the floor; tunnels / cuts: the start).
/// Room: an ellipsoid with a flat floor, Size = (half width, height, half length), turned with this object.
/// Tunnel: from here to End, Size.x = half width, Size.y = height; the floor slopes from here to End.
/// Open Cut: like a tunnel but open to the sky (the ravine in front of the entrance).
/// Depth (0 = entrance, 1 = deepest) drives the cave lighting (CaveAtmosphere) and wall materials.
/// </summary>
public class CaveSpace : MonoBehaviour
{
    public enum Kind { Room, Tunnel, OpenCut }

    [SerializeField] private Kind kind = Kind.Room;
    [SerializeField] private Vector3 size = new Vector3(20f, 15f, 18f);
    [Tooltip("Tunnel / Open Cut: where it ends (a point on the floor).")]
    [SerializeField] private Transform end;
    [Tooltip("0 = at the entrance, 1 = the deepest part of the cave. Rooms use this; tunnels go from Depth to End Depth.")]
    [SerializeField, Range(0f, 1f)] private float depth;
    [SerializeField, Range(0f, 1f)] private float endDepth;
    [Tooltip("How bumpy the walls are, metres.")]
    [SerializeField, Min(0f)] private float wallNoise = 2.5f;

    public Kind SpaceKind => kind;
    public Vector3 Size => size;
    public Transform End => end;
    public Bounds Bounds { get; private set; }

    private Vector3 a, b, axisA, axisAB, center, radii;
    private Quaternion inverseRotation;
    private float abLengthSq, verticalRadius;
    private int seed;

    /// <summary>Caches positions; call after moving anything (CaveLayout.Prepare does it).</summary>
    public void Prepare(int noiseSeed)
    {
        int nameHash = 0;
        foreach (char ch in name) nameHash = (nameHash * 31 + ch) % 10007; // stable across sessions (string.GetHashCode isn't guaranteed)
        seed = noiseSeed + nameHash;
        a = transform.position;
        b = end != null ? end.position : a + transform.forward * 10f;
        float margin = wallNoise + 6f;
        var bounds = new Bounds();
        if (kind == Kind.Room)
        {
            center = a + Vector3.up * (size.y * 0.3f);
            radii = new Vector3(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y * 0.7f), Mathf.Max(1f, size.z));
            inverseRotation = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            float h = Mathf.Max(radii.x, radii.z);
            bounds.SetMinMax(new Vector3(center.x - h, a.y - 2f, center.z - h) - Vector3.one * margin,
                             new Vector3(center.x + h, center.y + radii.y, center.z + h) + Vector3.one * margin);
        }
        else
        {
            verticalRadius = Mathf.Max(1f, size.y * 0.6f);
            axisA = a + Vector3.up * (size.y * 0.4f);
            axisAB = b - a;
            if (kind == Kind.OpenCut) axisAB.y = 0f;
            abLengthSq = Mathf.Max(0.0001f, axisAB.sqrMagnitude);
            float w = size.x + margin;
            Vector3 lo = Vector3.Min(a, b), hi = Vector3.Max(a, b);
            bounds.SetMinMax(lo - new Vector3(w, 2f + margin, w),
                             hi + new Vector3(w, kind == Kind.OpenCut ? 2000f : size.y + margin, w));
        }
        Bounds = bounds;
    }

    /// <summary>Negative inside this space's air, positive in rock. t = how far along a tunnel (0..1).</summary>
    public float Distance(Vector3 p, out float t)
    {
        t = 0f;
        float noise = wallNoise > 0f ? WorldNoise.Signed2(p * 0.08f, seed) * wallNoise : 0f;
        float floorY;
        float d;
        if (kind == Kind.Room)
        {
            Vector3 q = inverseRotation * (p - center);
            Vector3 q0 = new Vector3(q.x / radii.x, q.y / radii.y, q.z / radii.z);
            Vector3 q1 = new Vector3(q0.x / radii.x, q0.y / radii.y, q0.z / radii.z);
            float k0 = q0.magnitude, k1 = Mathf.Max(0.0001f, q1.magnitude);
            d = k0 * (k0 - 1f) / k1;
            floorY = a.y;
        }
        else
        {
            t = Mathf.Clamp01(Vector3.Dot(p - axisA, axisAB) / abLengthSq);
            Vector3 v = p - (axisA + axisAB * t);
            if (kind == Kind.OpenCut)
            {
                v.y = 0f;
                d = v.magnitude - size.x;
            }
            else
            {
                v.y *= size.x / verticalRadius;
                d = (v.magnitude - size.x) * Mathf.Min(1f, verticalRadius / size.x);
            }
            floorY = Mathf.Lerp(a.y, b.y, t);
        }
        floorY += WorldNoise.Signed2(p * 0.15f, seed + 5) * 0.2f;
        return Mathf.Max(d + noise, floorY - p.y);
    }

    public float DepthAt(float t) => kind == Kind.Room ? depth : Mathf.Lerp(depth, endDepth, t);

    private void OnDrawGizmos()
    {
        Gizmos.color = kind == Kind.OpenCut ? new Color(0.4f, 0.9f, 0.4f, 0.5f)
                     : Color.Lerp(new Color(1f, 0.8f, 0.3f, 0.5f), new Color(0.5f, 0.4f, 1f, 0.5f), depth);
        if (kind == Kind.Room)
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position + Vector3.up * (size.y * 0.3f), Quaternion.Euler(0f, transform.eulerAngles.y, 0f),
                                          new Vector3(size.x, size.y * 0.7f, size.z));
            Gizmos.DrawWireSphere(Vector3.zero, 1f);
            Gizmos.matrix = Matrix4x4.identity;
        }
        else if (end != null)
        {
            Vector3 up = Vector3.up * size.y * 0.4f;
            Gizmos.DrawLine(transform.position + up, end.position + up);
            Gizmos.DrawWireSphere(transform.position + up, size.x);
            Gizmos.DrawWireSphere(end.position + up, size.x);
        }
    }
}
