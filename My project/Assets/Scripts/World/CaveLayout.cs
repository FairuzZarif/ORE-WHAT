using UnityEngine;

/// <summary>
/// The cave: every CaveSpace child, blended together. Used by the island map builder (to carve the
/// mountain) and at runtime by CaveAtmosphere (how deep in the cave a point is).
/// </summary>
public class CaveLayout : MonoBehaviour
{
    [Tooltip("How smoothly rooms and tunnels merge where they meet, metres. Large values also dip the floor " +
             "where spaces overlap (by up to a quarter of this), so keep it small.")]
    [SerializeField, Min(0f)] private float blend = 2.5f;
    [SerializeField] private int noiseSeed = 11;

    private CaveSpace[] spaces;

    public CaveSpace[] Spaces { get { if (spaces == null) Prepare(); return spaces; } }

    private void Awake() => Prepare();

    /// <summary>Finds the spaces and caches their positions (call after editing them).</summary>
    public void Prepare()
    {
        spaces = GetComponentsInChildren<CaveSpace>();
        foreach (CaveSpace s in spaces) s.Prepare(noiseSeed);
    }

    /// <summary>Negative inside cave air, positive in rock (approximate distance, metres).</summary>
    public float Distance(Vector3 p, bool includeOpenCuts = true)
    {
        float d = 1000f;
        foreach (CaveSpace s in Spaces)
        {
            if (!includeOpenCuts && s.SpaceKind == CaveSpace.Kind.OpenCut) continue;
            if (!s.Bounds.Contains(p)) continue;
            d = WorldNoise.SMin(d, s.Distance(p, out _), blend);
        }
        return d;
    }

    /// <summary>
    /// Distance to the enclosed cave (open cuts ignored) and the depth (0..1) of the nearest space.
    /// Returns false when the point is nowhere near the cave.
    /// </summary>
    public bool Sample(Vector3 p, out float distance, out float depth, out CaveSpace nearest)
    {
        distance = 1000f;
        depth = 0f;
        nearest = null;
        float best = float.MaxValue;
        foreach (CaveSpace s in Spaces)
        {
            if (s.SpaceKind == CaveSpace.Kind.OpenCut || !s.Bounds.Contains(p)) continue;
            float d = s.Distance(p, out float t);
            distance = WorldNoise.SMin(distance, d, blend);
            if (d < best) { best = d; depth = s.DepthAt(t); nearest = s; }
        }
        return nearest != null;
    }
}
