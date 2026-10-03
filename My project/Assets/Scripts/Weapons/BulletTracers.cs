using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bullet tracers: a thin, bright streak that runs from a gun's muzzle to where the shot ended, very fast. Purely visual:
/// the shot itself is still WeaponController's camera ray (and the host's checks); a tracer only draws it.
///
///   Local shots   WeaponController.Fire calls <see cref="Play"/> with the muzzle (where it's drawn on screen) and the
///                 end of its own ray (the hit point, or the end of its range on a miss).
///   Other players NetworkPlayerAvatar's ShotRpc carries that end point; RemotePlayerPresentation draws the tracer from
///                 the muzzle of the gun in that player's hands.
///
/// A streak is a short line whose bright head travels along the path in 0.03-0.08 s and whose tail catches up and
/// fades. One small pool of line renderers (max <see cref="MaxTracers"/>, oldest reused), so a rifle spray never piles
/// up objects; a finished tracer is just switched off. The material is Resources/BulletTracer (made by
/// Ore What > Add Weapon Tracers).
/// </summary>
public class BulletTracers : MonoBehaviour
{
    public const int MaxTracers = 24;

    /// <summary>How a gun's tracer looks. One place for both guns: the pistol's is a bit bigger and brighter.</summary>
    public struct Style
    {
        public float width, streakLength, maxStreak, speed, minTime, maxTime, fadeTime;
        public Color colour;
    }

    public static Style PistolStyle => new Style
    {
        width = 0.022f, streakLength = 3f, maxStreak = 8f, speed = 420f, minTime = 0.04f, maxTime = 0.08f, fadeTime = 0.035f,
        colour = new Color(1f, 0.88f, 0.6f, 1f),
    };

    public static Style RifleStyle => new Style
    {
        width = 0.014f, streakLength = 2.2f, maxStreak = 6f, speed = 520f, minTime = 0.03f, maxTime = 0.06f, fadeTime = 0.025f,
        colour = new Color(1f, 0.82f, 0.5f, 0.85f),
    };

    private class Tracer
    {
        public LineRenderer line;
        public Vector3 start, end;
        public float born, travelTime;
        public Style style;
    }

    private static BulletTracers instance;
    private static Material material;
    private readonly List<Tracer> tracers = new List<Tracer>();

    /// <summary>Counts, for tests.</summary>
    public static int Played { get; private set; }
    public static int Active => instance != null ? instance.CountActive() : 0;
    public static int Pooled => instance != null ? instance.tracers.Count : 0;
    /// <summary>Start / end of the last tracer played (for tests).</summary>
    public static Vector3 LastStart { get; private set; }
    public static Vector3 LastEnd { get; private set; }

    /// <summary>Draws one tracer from start (the muzzle) to end (where the shot stopped). rifle = the rifle's look.</summary>
    public static void Play(Vector3 start, Vector3 end, bool rifle)
    {
        if ((end - start).sqrMagnitude < 0.01f) return; // muzzle against the wall: nothing to see
        if (material == null) material = Resources.Load<Material>("BulletTracer");
        if (material == null) return;
        if (instance == null) instance = new GameObject("Bullet Tracers").AddComponent<BulletTracers>();
        instance.Launch(start, end, rifle ? RifleStyle : PistolStyle);
        Played++;
        LastStart = start;
        LastEnd = end;
    }

    private void Launch(Vector3 start, Vector3 end, Style style)
    {
        Tracer t = Take();
        t.start = start;
        t.end = end;
        t.style = style;
        t.born = Time.time;
        t.travelTime = Mathf.Clamp(Vector3.Distance(start, end) / style.speed, style.minTime, style.maxTime);
        t.line.widthMultiplier = 1f; // the widths themselves are set per frame (Place)
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(style.colour, 0f), new GradientColorKey(style.colour, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(style.colour.a, 1f) }); // faint tail → bright head
        t.line.colorGradient = gradient;
        t.line.gameObject.SetActive(true);
        Place(t, 0f);
    }

    private void Update()
    {
        foreach (Tracer t in tracers)
            if (t.line.gameObject.activeSelf) Place(t, Time.time - t.born);
    }

    /// <summary>
    /// The head runs from the muzzle to the end; the tail follows a streak-length behind (35% of the shot, within the style's
    /// min..max length, so long shots show a longer streak but never a laser), then catches up and it's gone. Each end's width grows with its distance from
    /// the camera, so the streak stays a few pixels thick on screen near and far (a fixed width vanishes at 20 m).
    /// </summary>
    private static void Place(Tracer t, float age)
    {
        float distance = Vector3.Distance(t.start, t.end);
        float run = distance / t.travelTime; // this tracer's speed (m/s)
        float head = Mathf.Min(distance, age * run);
        float tail = Mathf.Max(0f, head - Mathf.Clamp(distance * 0.35f, t.style.streakLength, t.style.maxStreak));
        if (age > t.travelTime) tail = Mathf.Lerp(tail, distance, Mathf.Clamp01((age - t.travelTime) / t.style.fadeTime));
        if (age > t.travelTime + t.style.fadeTime || tail >= distance - 0.001f)
        {
            t.line.gameObject.SetActive(false);
            return;
        }
        Vector3 dir = (t.end - t.start) / Mathf.Max(0.0001f, distance);
        Vector3 a = t.start + dir * tail, b = t.start + dir * Mathf.Max(head, tail + 0.05f);
        t.line.SetPosition(0, a);
        t.line.SetPosition(1, b);
        Camera view = Camera.main;
        if (view != null)
        {
            Vector3 eye = view.transform.position;
            t.line.startWidth = t.style.width * Mathf.Max(1f, Vector3.Distance(eye, a) / WidthDistance);
            t.line.endWidth = t.style.width * Mathf.Max(1f, Vector3.Distance(eye, b) / WidthDistance);
        }
    }

    // Closer than this (metres) a tracer keeps its own width; further away it widens with distance (same size on screen).
    private const float WidthDistance = 4f;

    private Tracer Take()
    {
        foreach (Tracer t in tracers)
            if (!t.line.gameObject.activeSelf) return t;
        if (tracers.Count >= MaxTracers) // all busy: reuse the oldest
        {
            Tracer oldest = tracers[0];
            foreach (Tracer t in tracers) if (t.born < oldest.born) oldest = t;
            return oldest;
        }
        var go = new GameObject("Tracer");
        go.transform.SetParent(transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.sharedMaterial = material;
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        var t2 = new Tracer { line = line };
        tracers.Add(t2);
        return t2;
    }

    private int CountActive()
    {
        int n = 0;
        foreach (Tracer t in tracers) if (t.line.gameObject.activeSelf) n++;
        return n;
    }

    private void OnDestroy() { if (instance == this) instance = null; }
}
