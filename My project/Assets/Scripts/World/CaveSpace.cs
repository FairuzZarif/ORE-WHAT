using UnityEngine;

/// <summary>
/// One room, tunnel or open-air cut of the cave. Children of the CaveLayout; the island map builder
/// carves them out of the mountain. Move / resize them in the Scene view, then rebuild
/// (Ore What > Island Map > Build Or Rebuild Island Map).
///
/// This object's position is on the FLOOR (rooms: middle of the floor; tunnels / cuts: the start).
/// Room: an ellipsoid with a flat floor (plus optional roll and a plateau / pit), Size = (half width, height, half length), turned with this object.
/// Tunnel: from here to End, Size.x = half width, Size.y = height; the floor slopes from here to End (optionally in broad steps).
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

    [Header("Floor shape (keep slopes walkable: the player's slope limit is 45°)")]
    [Tooltip("Gentle rolling of the floor, metres up/down (about 14 m between bumps). 0 = flat.")]
    [SerializeField, Min(0f)] private float floorRoll;
    [Tooltip("Tunnels: the drop from start to end is taken in broad steps of about this height (a flat tread, then a " +
             "short ramp down). 0 = one smooth ramp.")]
    [SerializeField, Min(0f)] private float stepHeight;
    [Tooltip("Tunnels: share of each step that is the ramp (bigger = gentler ramps, shorter flat treads).")]
    [SerializeField, Range(0.2f, 0.9f)] private float stepRampShare = 0.5f;
    [Tooltip("Rooms: a raised plateau (positive) or a sunken pit (negative), metres. 0 = none.")]
    [SerializeField] private float plateauHeight;
    [Tooltip("Rooms: plateau centre, metres from the room centre in the room's own space (X = right, Y = forward).")]
    [SerializeField] private Vector2 plateauOffset;
    [Tooltip("Rooms: radius of the flat top of the plateau / bottom of the pit, metres.")]
    [SerializeField, Min(0.5f)] private float plateauRadius = 5f;
    [Tooltip("Rooms: width of the ramp around it, metres (the ramp's steepest part is about 1.5 × height / width).")]
    [SerializeField, Min(0.5f)] private float plateauRamp = 4f;

    public Kind SpaceKind => kind;
    public Vector3 Size => size;
    public float PlateauHeight => plateauHeight;
    /// <summary>World position of the plateau's centre, on the base floor (rooms with a plateau).</summary>
    public Vector3 PlateauCentre => transform.position + Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * new Vector3(plateauOffset.x, 0f, plateauOffset.y);
    public float PlateauRadius => plateauRadius;

    /// <summary>Sets the floor shape (used by the island map builder's defaults).</summary>
    public void SetFloorShape(float roll, float steps, float rampShare, float plateau, Vector2 offset, float radius, float ramp)
    {
        floorRoll = roll; stepHeight = steps; stepRampShare = rampShare;
        plateauHeight = plateau; plateauOffset = offset; plateauRadius = radius; plateauRamp = ramp;
    }
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
            if (plateauHeight != 0f)
            {
                Vector3 local = inverseRotation * (p - a);
                float r = Vector2.Distance(new Vector2(local.x, local.z), plateauOffset);
                floorY += plateauHeight * (1f - Smooth(plateauRadius, plateauRadius + plateauRamp, r));
            }
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
            floorY = a.y + (b.y - a.y) * StepProfile(t);
        }
        floorY += WorldNoise.Signed2(p * 0.15f, seed + 5) * 0.2f;
        if (floorRoll > 0f) floorY += WorldNoise.Signed2(p * 0.07f, seed + 9) * floorRoll;
        return Mathf.Max(d + noise, floorY - p.y);
    }

    public float DepthAt(float t) => kind == Kind.Room ? depth : Mathf.Lerp(depth, endDepth, t);

    /// <summary>
    /// How far along the tunnel's height change the floor is at t (0..1): a smooth ramp, or broad steps
    /// (flat tread, then a ramp of Step Ramp Share of the step's length). Starts at 0, ends exactly at 1.
    /// </summary>
    private float StepProfile(float t)
    {
        if (kind != Kind.Tunnel || stepHeight <= 0f) return t;
        int steps = Mathf.Max(1, Mathf.RoundToInt(Mathf.Abs(b.y - a.y) / stepHeight));
        float u = t * steps;
        float k = Mathf.Min(Mathf.Floor(u), steps - 1);
        return (k + Smooth(1f - stepRampShare, 1f, u - k)) / steps;
    }

    private static float Smooth(float edge0, float edge1, float x)
    {
        float v = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return v * v * (3f - 2f * v);
    }

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
