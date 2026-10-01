using System;
using UnityEngine;

/// <summary>
/// The shape of the island's mountain, used by Ore What > Island Map > Build Or Rebuild Island Map
/// to generate its rock mesh (the cave is carved out of it by the CaveLayout next to it).
/// This object's position = centre of the base, at ground level. Change the numbers, then rebuild.
///
/// Shape: a broad cone (Radius / Height / Profile Power) with an uneven, lobed base, extra Peaks
/// (shoulders) merged in, ridges, and terraces (rock layers with cliffs between them).
/// </summary>
public class MountainShape : MonoBehaviour
{
    [Serializable]
    public struct Peak
    {
        [Tooltip("Offset from the mountain centre on the ground (x, z), metres.")] public Vector2 offset;
        public float radius;
        public float height;
    }

    [Header("Main shape")]
    [SerializeField, Min(10f)] private float radius = 190f;
    [SerializeField, Min(10f)] private float height = 185f;
    [Tooltip("Higher = more concave flanks and a sharper peak.")]
    [SerializeField, Range(1f, 3f)] private float profilePower = 1.5f;
    [Tooltip("How much the base outline wobbles (0 = circle).")]
    [SerializeField, Range(0f, 0.4f)] private float lobeAmount = 0.15f;
    [Tooltip("The shape starts this far below the ground, so its foot disappears into the terrain.")]
    [SerializeField, Min(0f)] private float sink = 12f;
    [SerializeField] private Peak[] peaks =
    {
        new Peak { offset = new Vector2(75f, 15f), radius = 80f, height = 115f },
        new Peak { offset = new Vector2(-85f, 35f), radius = 70f, height = 100f },
        new Peak { offset = new Vector2(15f, 85f), radius = 80f, height = 150f },
    };

    [Header("Detail")]
    [Tooltip("Height of each rock layer (terrace), metres.")]
    [SerializeField, Min(2f)] private float terraceStep = 24f;
    [SerializeField, Range(0f, 1f)] private float terraceStrength = 0.6f;
    [SerializeField, Min(0f)] private float ridgeAmount = 14f;
    [Tooltip("Lumpy rock outcrops on the surface, metres.")]
    [SerializeField, Min(0f)] private float rockNoise = 5f;
    [SerializeField] private int seed = 7;

    [Header("Mesh")]
    [Tooltip("Generation grid size, metres. Smaller = more detail and more triangles.")]
    [SerializeField, Range(1f, 4f)] private float voxelSize = 2f;

    public float VoxelSize => voxelSize;
    public int Seed => seed;

    /// <summary>World-space height of the mountain's top surface at (x, z) (below ground away from it).</summary>
    public float SurfaceHeight(float x, float z)
    {
        Vector3 c = transform.position;
        float dx = x - c.x, dz = z - c.z;
        float r = Mathf.Sqrt(dx * dx + dz * dz);
        float ang = Mathf.Atan2(dz, dx);
        float lobe = 1f + lobeAmount * (Mathf.PerlinNoise(seed * 0.37f + Mathf.Cos(ang) * 1.1f + 10f, seed * 0.13f + Mathf.Sin(ang) * 1.1f + 10f) * 2f - 1f) * 1.6f;
        float s = Mathf.Clamp01(1f - r / (radius * lobe));
        float h = height * Mathf.Pow(s, profilePower);

        foreach (Peak p in peaks)
        {
            float d = Vector2.Distance(new Vector2(dx, dz), p.offset);
            float ps = Mathf.Clamp01(1f - d / Mathf.Max(1f, p.radius));
            h = SmoothMax(h, p.height * Mathf.Pow(ps, profilePower) * Mathf.Clamp01(s * 3f), 18f);
        }

        if (s > 0f)
        {
            float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise((x + seed * 31f) * 0.012f, z * 0.012f) * 2f - 1f);
            float broad = Mathf.PerlinNoise(x * 0.005f + seed, z * 0.005f - seed) * 2f - 1f;
            h += (ridgeAmount * (ridge - 0.5f) * 2f + broad * 12f) * Mathf.Sqrt(s);
        }

        if (terraceStrength > 0f && h > 0f)
        {
            // Wobble the layer heights so the ledges aren't perfect contour rings.
            float wobble = (Mathf.PerlinNoise(x * 0.011f + 3.1f + seed, z * 0.011f - 7.7f) * 2f - 1f) * terraceStep * 0.45f;
            float t = (h + wobble) / terraceStep;
            float floor = Mathf.Floor(t);
            float stepped = (floor + Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, t - floor))) * terraceStep;
            stepped -= wobble;
            h = Mathf.Lerp(h, stepped, terraceStrength * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 30f, h)));
        }
        return c.y - sink + h;
    }

    /// <summary>Signed distance-like value: negative inside rock, positive in the air (cave not included).</summary>
    public float Distance(Vector3 p) => Distance(p, SurfaceHeight(p.x, p.z));

    /// <summary>Same, with the surface height at (p.x, p.z) already known.</summary>
    public float Distance(Vector3 p, float surfaceHeight)
    {
        float d = (p.y - surfaceHeight) * 0.55f;
        if (rockNoise > 0f && Mathf.Abs(d) < rockNoise * 3f)
            d += WorldNoise.Signed2(p * 0.06f, seed) * rockNoise;
        return d;
    }

    /// <summary>Everything the mountain can occupy (for the generator).</summary>
    public Bounds GetBounds()
    {
        float top = height;
        foreach (Peak p in peaks) top = Mathf.Max(top, p.height);
        top += ridgeAmount + 12f + terraceStep + rockNoise * 2f;
        float r = radius * (1f + lobeAmount * 1.6f) + rockNoise * 2f;
        Vector3 c = transform.position;
        var b = new Bounds();
        b.SetMinMax(new Vector3(c.x - r, c.y - sink - rockNoise * 2f - 4f, c.z - r), new Vector3(c.x + r, c.y - sink + top, c.z + r));
        return b;
    }

    private static float SmoothMax(float a, float b, float k) => -WorldNoise.SMin(-a, -b, k);

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.6f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, radius);
        foreach (Peak p in peaks) Gizmos.DrawWireSphere(transform.position + new Vector3(p.offset.x, 0f, p.offset.y), p.radius);
    }
}
