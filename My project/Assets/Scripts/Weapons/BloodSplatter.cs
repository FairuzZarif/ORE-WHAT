using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Blood where a player was hit: the short particle burst (BloodHit prefab) plus splats on the surfaces around it
/// (ground, cave walls, props). Called on every machine for one confirmed hit (NetworkPlayerHealth's HitRpc); nothing
/// here is networked. The same seed is used everywhere, so every player gets the same splats from one small event.
///
/// Splats: a few rays go out from the hit (through the body along the shot, down to the floor, and a spray around
/// the shot direction); the first solid surface each ray meets gets a splat lying on it (rotated to its normal, 1 cm
/// above it). A splat is only placed if all four corners lie on that surface too (otherwise it shrinks once, then
/// gives up), so it never hangs over an edge, floats or sits inside the rock. Players, loose items and moving
/// physics objects never get splats. Four looks from one texture (droplets, medium splat, big irregular splat,
/// streak), random rotation and size. Each splat fades after Lifetime; at most MaxSplats exist (oldest reused).
/// </summary>
public class BloodSplatter : MonoBehaviour
{
    public const int MaxSplats = 48;
    public const float Lifetime = 40f, FadeTime = 4f;

    // Ignore Raycast, ViewModel, DroppedItem, Player.
    private const int SurfaceMask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));
    private const float Lift = 0.012f;

    private class Splat { public GameObject go; public MeshRenderer renderer; public float born; }

    private static BloodSplatter instance;
    private static UnityEngine.Mesh[] meshes;
    private readonly List<Splat> splats = new List<Splat>();
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private MaterialPropertyBlock block;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    /// <summary>Counts, for tests.</summary>
    public static int Events { get; private set; }
    public static int SplatsPlaced { get; private set; }
    public static int SplatsAlive => instance != null ? instance.splats.Count : 0;

    /// <summary>
    /// One confirmed hit at point, shot along direction. particles = play the burst (false for the victim, who'd get
    /// it in the face); ignore = the hit player's body (never painted).
    /// </summary>
    public static void Play(GameObject burst, Material splatMaterial, Vector3 point, Vector3 direction, bool melee, int seed,
                            bool particles, Transform ignore)
    {
        Events++;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
        direction.Normalize();
        if (particles && burst != null) Instantiate(burst, point, Quaternion.LookRotation(-direction)); // removes itself
        if (splatMaterial == null) return;
        if (instance == null) instance = new GameObject("Blood Splats").AddComponent<BloodSplatter>();
        instance.Spray(splatMaterial, point, direction, melee, seed, ignore);
    }

    private void Spray(Material material, Vector3 point, Vector3 direction, bool melee, int seed, Transform ignore)
    {
        var rng = new System.Random(seed);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        Vector3 InCone(Vector3 axis, float degrees)
        {
            Vector3 side = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Quaternion spin = Quaternion.AngleAxis(R(0f, 360f), axis);
            return Quaternion.AngleAxis(R(0f, degrees), spin * side) * axis;
        }

        int rays = melee ? rng.Next(2, 4) : rng.Next(3, 7);
        for (int i = 0; i < rays; i++)
        {
            Vector3 dir;
            float reach;
            if (i == 0) { dir = InCone(direction, 20f); reach = 3f; }                                     // out the back, along the shot
            else if (i == 1) { dir = (Vector3.down + direction * 0.4f + InCone(Vector3.down, 60f) * 0.5f).normalized; reach = 2.5f; } // drips on the floor
            else { dir = InCone(direction, 70f); reach = 2f; }                                              // spray around
            if (!Cast(point + dir * 0.05f, dir, reach, ignore, out RaycastHit hit)) continue;

            float near = 1f - Mathf.Clamp01(hit.distance / reach);
            int look = near > 0.6f ? rng.Next(1, 3) : near > 0.3f ? rng.Next(0, 4) : (rng.NextDouble() < 0.6 ? 0 : 3);
            float size = Mathf.Lerp(0.22f, 0.65f, near) * R(0.75f, 1.2f) * (look == 2 ? 1.2f : 1f) * (melee ? 0.8f : 1f);
            float angle = look == 3 ? Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(dir, hit.normal), hit.normal) // streaks follow the spray
                                    : R(0f, 360f);
            Place(material, hit, look, size, angle);
        }
    }

    /// <summary>The first solid surface along the ray that isn't a player, a loose item or a moving physics object.</summary>
    private bool Cast(Vector3 from, Vector3 dir, float reach, Transform ignore, out RaycastHit best)
    {
        best = default;
        int count = Physics.RaycastNonAlloc(from, dir, hits, reach, SurfaceMask, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = hits[i];
            if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
            if (h.collider.GetComponentInParent<NetworkPlayerAvatar>() != null) continue; // other players' bodies
            if (h.distance >= nearest) continue;
            nearest = h.distance;
            best = h;
            found = true;
        }
        if (!found) return false;
        if (best.rigidbody != null && !best.rigidbody.isKinematic) return false; // it moves: a splat wouldn't follow
        return best.distance > 0f;
    }

    private void Place(Material material, RaycastHit hit, int look, float size, float angle)
    {
        Vector3 n = hit.normal;
        Quaternion rotation = Quaternion.LookRotation(-n, Quaternion.AngleAxis(angle, n) * Tangent(n)); // the quad's face (-Z) looks out of the surface
        Vector2 half = new Vector2(size, look == 3 ? size * 0.45f : size) * 0.5f;
        if (!FitsOnSurface(hit, rotation, half))
        {
            half *= 0.6f;
            if (!FitsOnSurface(hit, rotation, half)) return;
        }

        Splat s = Take(material);
        s.go.transform.SetPositionAndRotation(hit.point + n * Lift, rotation);
        s.go.transform.localScale = new Vector3(half.x * 2f, half.y * 2f, 1f);
        s.go.GetComponent<MeshFilter>().sharedMesh = QuadFor(look);
        s.born = Time.time;
        SetAlpha(s, 1f);
        s.go.SetActive(true);
        SplatsPlaced++;
    }

    /// <summary>Do all four corners lie on the same surface (within 4 cm, facing the same way)?</summary>
    private bool FitsOnSurface(RaycastHit hit, Quaternion rotation, Vector2 half)
    {
        Vector3 n = hit.normal, right = rotation * Vector3.right, up = rotation * Vector3.up;
        for (int c = 0; c < 4; c++)
        {
            Vector3 corner = hit.point + right * (c % 2 == 0 ? -half.x : half.x) + up * (c < 2 ? -half.y : half.y);
            if (!Physics.Raycast(corner + n * 0.15f, -n, out RaycastHit h, 0.3f, SurfaceMask, QueryTriggerInteraction.Ignore)) return false;
            if (h.collider.GetComponentInParent<NetworkPlayerAvatar>() != null) return false;
            if (Mathf.Abs(Vector3.Dot(h.point - hit.point, n)) > 0.04f || Vector3.Dot(h.normal, n) < 0.85f) return false;
        }
        return true;
    }

    private Splat Take(Material material)
    {
        if (splats.Count >= MaxSplats)
        {
            Splat oldest = splats[0];
            splats.RemoveAt(0);
            splats.Add(oldest);
            return oldest;
        }
        var go = new GameObject("Blood Splat", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(transform, false);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var s = new Splat { go = go, renderer = renderer };
        splats.Add(s);
        return s;
    }

    private void Update()
    {
        for (int i = splats.Count - 1; i >= 0; i--)
        {
            Splat s = splats[i];
            if (!s.go.activeSelf) continue;
            float left = Lifetime - (Time.time - s.born);
            if (left <= 0f) { s.go.SetActive(false); continue; }
            if (left < FadeTime) SetAlpha(s, left / FadeTime);
        }
    }

    private void SetAlpha(Splat s, float alpha)
    {
        block ??= new MaterialPropertyBlock();
        Color c = s.renderer.sharedMaterial.GetColor(BaseColorId);
        c.a *= alpha;
        block.SetColor(BaseColorId, c);
        s.renderer.SetPropertyBlock(block);
    }

    private static Vector3 Tangent(Vector3 n) => Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;

    /// <summary>A unit quad (facing -Z) showing one quarter of the 2×2 splat texture.</summary>
    private static UnityEngine.Mesh QuadFor(int look)
    {
        if (meshes == null || meshes.Length != 4) meshes = new UnityEngine.Mesh[4];
        if (meshes[look] != null) return meshes[look];
        float u = (look % 2) * 0.5f, v = (look / 2) * 0.5f;
        var m = new UnityEngine.Mesh { name = "Blood Splat " + look };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        m.uv = new[] { new Vector2(u, v), new Vector2(u + 0.5f, v), new Vector2(u, v + 0.5f), new Vector2(u + 0.5f, v + 0.5f) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        m.RecalculateBounds();
        meshes[look] = m;
        return m;
    }

    private void OnDestroy() { if (instance == this) instance = null; }
}
