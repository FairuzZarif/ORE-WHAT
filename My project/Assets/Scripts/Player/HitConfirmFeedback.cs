using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What YOU get when the host confirms one of your attacks hurt another player. Put it on the Player. Local only:
/// nothing here is networked; NetworkPlayerHealth calls <see cref="Confirm"/> once per hit the host accepted, so a
/// miss, a refused hit or a hit through a wall never shows anything.
///   - the crosshair's hit marker (Crosshair.ShowHitMarker)
///   - a damage number ("-20") at the hit: pops in, floats up, fades (red and bigger for a kill, gold for a head
///     hit, a little smaller for arms / legs)
///   - a short "tick" (higher for a head hit), or on the killing hit a "thump/squish" instead (never both)
/// Leave the sound fields empty to use synthesised placeholder sounds (WeaponSounds).
/// </summary>
public class HitConfirmFeedback : MonoBehaviour
{
    [Header("Sounds (empty = synthesised placeholders)")]
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip killSound;
    [SerializeField, Range(0f, 1f)] private float hitVolume = 0.55f;
    [SerializeField, Range(0f, 1f)] private float killVolume = 0.9f;

    [Header("Damage numbers")]
    [SerializeField] private Color numberColor = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color killColor = new Color(0.95f, 0.22f, 0.17f, 1f);
    [Tooltip("Head hits (not killing ones).")]
    [SerializeField] private Color headshotColor = new Color(1f, 0.8f, 0.2f, 1f);
    [Tooltip("Pitch of the tick for a head hit (1 = normal).")]
    [SerializeField, Range(0.5f, 2f)] private float headshotPitch = 1.3f;
    [Tooltip("Font size at 1080p.")]
    [SerializeField, Min(8)] private int fontSize = 30;
    [Tooltip("Seconds a number stays.")]
    [SerializeField, Min(0.1f)] private float numberTime = 0.75f;
    [Tooltip("How far a number floats up, metres (world).")]
    [SerializeField, Min(0f)] private float rise = 0.55f;

    private class Number
    {
        public RectTransform rect;
        public Text text;
        public Vector3 world;
        public Vector2 pixelOffset;
        public float born;
        public bool kill;
    }

    private static HitConfirmFeedback instance;
    private readonly List<Number> numbers = new List<Number>();
    private AudioSource audioSource;
    private Camera view;
    private RectTransform canvas;
    private Font font;
    private int recentCount;
    private float lastNumberTime = -10f;

    /// <summary>Counts, for tests: confirmed hits, ticks played, kill sounds played, last number shown.</summary>
    public static int HitsConfirmed { get; private set; }
    public static int TicksPlayed { get; private set; }
    public static int KillSoundsPlayed { get; private set; }
    public static string LastNumber { get; private set; } = "";

    /// <summary>Counts head hits confirmed (for tests).</summary>
    public static int HeadshotsConfirmed { get; private set; }
    /// <summary>The body part of the last confirmed hit (for tests).</summary>
    public static BodyZone LastZone { get; private set; }

    /// <summary>The host confirmed your hit: damage dealt at point (world) on that body part; killed = the killing hit.</summary>
    public static void Confirm(Vector3 point, float damage, bool killed, BodyZone zone = BodyZone.Chest)
    {
        HitsConfirmed++;
        LastZone = zone;
        if (zone == BodyZone.Head) HeadshotsConfirmed++;
        Crosshair.ShowHitMarker(killed);
        if (instance != null) instance.Show(point, damage, killed, zone);
    }

    private void Awake()
    {
        instance = this;
        view = GetComponentInChildren<Camera>(true);
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f; // a UI sound: always the same, wherever the target is
        if (hitSound == null) hitSound = WeaponSounds.HitTick();
        if (killSound == null) killSound = WeaponSounds.KillThump();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Start() // after HotbarUI.Awake has built its canvas
    {
        foreach (Canvas c in GetComponentsInChildren<Canvas>(true))
            if (c.name == "HotbarCanvas") canvas = (RectTransform)c.transform;
    }

    private void Show(Vector3 point, float damage, bool killed, BodyZone zone)
    {
        // One sound per confirmed hit: the kill sound replaces the tick. A head hit ticks higher.
        if (killed) { audioSource.pitch = 1f; audioSource.PlayOneShot(killSound, killVolume); KillSoundsPlayed++; }
        else { audioSource.pitch = zone == BodyZone.Head ? headshotPitch : 1f; audioSource.PlayOneShot(hitSound, hitVolume); TicksPlayed++; }

        LastNumber = "-" + Mathf.RoundToInt(damage);
        if (canvas == null) return;
        // Several quick hits: spread them out a little so they don't sit on top of each other.
        recentCount = Time.time - lastNumberTime < 0.45f ? recentCount + 1 : 0;
        lastNumberTime = Time.time;
        Number n = Take();
        n.world = point;
        n.pixelOffset = new Vector2(((recentCount % 3) - 1) * 26f + Random.Range(-6f, 6f), (recentCount % 3) * 14f);
        n.born = Time.time;
        n.kill = killed;
        n.text.text = LastNumber;
        n.text.color = killed ? killColor : zone == BodyZone.Head ? headshotColor : numberColor;
        float size = killed ? 1.3f : zone == BodyZone.Head ? 1.2f : zone == BodyZone.Chest ? 1f : 0.85f; // limbs a bit smaller
        n.text.fontSize = Mathf.RoundToInt(fontSize * size);
        n.rect.SetAsLastSibling();
        n.rect.gameObject.SetActive(true);
        Place(n);
    }

    private void LateUpdate()
    {
        foreach (Number n in numbers)
            if (n.rect.gameObject.activeSelf) Place(n);
    }

    /// <summary>Pop in (bigger, then settle), float up, fade out; stays on the hit point as the camera moves.</summary>
    private void Place(Number n)
    {
        float t = (Time.time - n.born) / numberTime;
        if (t >= 1f || view == null) { n.rect.gameObject.SetActive(false); return; }
        Vector3 world = n.world + Vector3.up * (rise * (1f - (1f - t) * (1f - t)));
        Vector3 screen = view.WorldToScreenPoint(world);
        if (screen.z <= 0f) { n.text.enabled = false; return; } // behind us
        n.text.enabled = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screen, null, out Vector2 local);
        n.rect.anchoredPosition = local + n.pixelOffset;
        float pop = t < 0.12f ? Mathf.Lerp(0.5f, 1.25f, t / 0.12f) : Mathf.Lerp(1.25f, 1f, Mathf.Clamp01((t - 0.12f) / 0.15f));
        n.rect.localScale = Vector3.one * pop;
        Color c = n.text.color;
        c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
        n.text.color = c;
    }

    private Number Take()
    {
        foreach (Number n in numbers)
            if (!n.rect.gameObject.activeSelf) return n;
        if (numbers.Count >= 12) { Number oldest = numbers[0]; numbers.RemoveAt(0); numbers.Add(oldest); return oldest; }

        var go = new GameObject("DamageNumber", typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(canvas, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); // the canvas centre = (0,0) for ScreenPointToLocalPoint
        rect.sizeDelta = new Vector2(160f, 50f);
        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        var outline = go.AddComponent<Outline>(); // readable on bright and dark rock alike
        outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        var n2 = new Number { rect = rect, text = text };
        numbers.Add(n2);
        return n2;
    }
}
