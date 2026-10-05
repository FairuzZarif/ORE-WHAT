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

    /// <summary>Piecewise planar tetrahedral noise. Broad rock faces have hard changes of slope, without texture noise.</summary>
    public static float Faceted(Vector3 p, int seed)
    {
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
        float fx = p.x - x, fy = p.y - y, fz = p.z - z;
        float start = Hash(x, y, z, seed), end = Hash(x + 1, y + 1, z + 1, seed);
        float a, b, first, second, third;
        if (fx >= fy)
        {
            if (fy >= fz) { a = Hash(x + 1, y, z, seed); b = Hash(x + 1, y + 1, z, seed); first = fx; second = fy; third = fz; }
            else if (fx >= fz) { a = Hash(x + 1, y, z, seed); b = Hash(x + 1, y, z + 1, seed); first = fx; second = fz; third = fy; }
            else { a = Hash(x, y, z + 1, seed); b = Hash(x + 1, y, z + 1, seed); first = fz; second = fx; third = fy; }
        }
        else
        {
            if (fx >= fz) { a = Hash(x, y + 1, z, seed); b = Hash(x + 1, y + 1, z, seed); first = fy; second = fx; third = fz; }
            else if (fy >= fz) { a = Hash(x, y + 1, z, seed); b = Hash(x, y + 1, z + 1, seed); first = fy; second = fz; third = fx; }
            else { a = Hash(x, y, z + 1, seed); b = Hash(x, y + 1, z + 1, seed); first = fz; second = fy; third = fx; }
        }
        return (start + (a - start) * first + (b - a) * second + (end - b) * third) * 2f - 1f;
    }

    /// <summary>Smooth minimum (blends two distance fields with radius k).</summary>
    public static float SMin(float a, float b, float k)
    {
        if (k <= 0f) return Mathf.Min(a, b);
        float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
        return Mathf.Min(a, b) - h * h * k * 0.25f;
    }
}
