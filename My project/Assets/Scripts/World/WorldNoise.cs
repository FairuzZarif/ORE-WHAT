using UnityEngine;

/// <summary>Small, deterministic 3D value noise for the generated mountain and cave walls (0..1).</summary>
public static class WorldNoise
{
    private static float Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + seed * 362437);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    public static float Value(Vector3 p, int seed)
    {
        int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
        float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
        float a = Mathf.Lerp(Hash(x0, y0, z0, seed), Hash(x0 + 1, y0, z0, seed), fx);
        float b = Mathf.Lerp(Hash(x0, y0 + 1, z0, seed), Hash(x0 + 1, y0 + 1, z0, seed), fx);
        float c = Mathf.Lerp(Hash(x0, y0, z0 + 1, seed), Hash(x0 + 1, y0, z0 + 1, seed), fx);
        float d = Mathf.Lerp(Hash(x0, y0 + 1, z0 + 1, seed), Hash(x0 + 1, y0 + 1, z0 + 1, seed), fx);
        return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
    }

    /// <summary>Two octaves, centred on 0 (about −1..1).</summary>
    public static float Signed2(Vector3 p, int seed)
        => (Value(p, seed) * 0.65f + Value(p * 2.03f, seed + 17) * 0.35f) * 2f - 1f;

    /// <summary>Smooth minimum (blends two distance fields with radius k).</summary>
    public static float SMin(float a, float b, float k)
    {
        if (k <= 0f) return Mathf.Min(a, b);
        float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
        return Mathf.Min(a, b) - h * h * k * 0.25f;
    }
}
