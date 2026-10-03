using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tells the local player they were hit: a short red flash around the edges of the screen (stronger for a bigger
/// hit) and a small camera shake through CameraEffects. Put it on the Player next to PlayerAttributes; it only
/// listens to HealthChanged, so it works for any damage, in single player and multiplayer (where the host's new
/// health arrives through the same event). Nothing touches the first-person weapons.
/// </summary>
public class DamageFlash : MonoBehaviour
{
    [SerializeField] private PlayerAttributes attributes;
    [Tooltip("Optional. Shaken a little on each hit.")]
    [SerializeField] private CameraEffects cameraEffects;

    [SerializeField] private Color color = new Color(0.75f, 0.02f, 0.02f, 1f);
    [Tooltip("Flash strength for a hit of 25 damage (smaller hits flash less).")]
    [SerializeField, Range(0f, 1f)] private float strength = 0.55f;
    [Tooltip("Seconds for the flash to fade out.")]
    [SerializeField, Min(0.05f)] private float fadeTime = 0.45f;
    [Tooltip("Camera shake per hit (0..1, CameraEffects' scale). Keep it small.")]
    [SerializeField, Range(0f, 1f)] private float shake = 0.18f;

    private Image image;
    private float lastHealth, alpha;

    private void Start() // after HotbarUI.Awake has built its canvas
    {
        if (attributes == null) attributes = GetComponent<PlayerAttributes>();
        if (cameraEffects == null) cameraEffects = GetComponentInChildren<CameraEffects>(true);
        if (attributes == null) { enabled = false; return; }
        Build();
        lastHealth = attributes.Health;
        attributes.HealthChanged += OnHealthChanged;
    }

    private void OnDestroy() { if (attributes != null) attributes.HealthChanged -= OnHealthChanged; }

    private void OnHealthChanged(float current, float max)
    {
        float lost = lastHealth - current;
        lastHealth = current;
        if (lost <= 0f) return; // healed / respawned
        alpha = Mathf.Max(alpha, Mathf.Clamp01(strength * Mathf.Lerp(0.6f, 1.4f, Mathf.Clamp01(lost / 50f))));
        if (cameraEffects != null && shake > 0f) cameraEffects.Shake(shake);
    }

    private void Update()
    {
        if (image == null) return;
        alpha = Mathf.MoveTowards(alpha, 0f, Time.deltaTime / fadeTime);
        image.enabled = alpha > 0.001f;
        image.color = new Color(color.r, color.g, color.b, alpha);
    }

    private void Build()
    {
        Transform canvas = null;
        foreach (Canvas c in GetComponentsInChildren<Canvas>(true))
            if (c.name == "HotbarCanvas") canvas = c.transform;
        if (canvas == null)
        {
            var go = new GameObject("DamageCanvas", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvas = go.transform;
        }
        var rect = new GameObject("DamageFlash", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(canvas, false);
        rect.SetAsFirstSibling(); // under the hotbar and the bars
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        image = rect.gameObject.AddComponent<Image>();
        image.sprite = Vignette();
        image.raycastTarget = false;
        image.enabled = false;
    }

    /// <summary>A soft frame: clear in the middle, solid at the edges (made once, in code).</summary>
    private static Sprite Vignette()
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(u * u * 0.8f + v * v); // a bit wider than tall
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.25f, d));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
