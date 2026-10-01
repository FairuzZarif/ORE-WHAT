using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HEALTH and STAMINA bars in the bottom-left corner of the HUD, away from the hotbar (bottom
/// centre) and the crosshair. Put it on the Player next to PlayerAttributes; it only reads it.
/// Built at runtime on the hotbar's canvas (HotbarUI's "HotbarCanvas"), or on its own canvas if
/// there's no hotbar, so there's nothing to set up in the scene.
/// </summary>
public class PlayerStatsHUD : MonoBehaviour
{
    [SerializeField] private PlayerAttributes attributes;

    [Header("Layout (pixels at 1920x1080; scales with the screen)")]
    [SerializeField] private Vector2 margin = new Vector2(24f, 22f);
    [SerializeField] private Vector2 barSize = new Vector2(300f, 22f);
    [SerializeField, Min(0f)] private float barGap = 12f;

    [Header("Colours")]
    [SerializeField] private Color healthColor = new Color(0.86f, 0.22f, 0.2f, 1f);
    [SerializeField] private Color staminaColor = new Color(0.35f, 0.8f, 0.35f, 1f);
    [SerializeField] private Color exhaustedColor = new Color(0.85f, 0.65f, 0.2f, 1f);
    [SerializeField] private Color backgroundColor = new Color(0.05f, 0.05f, 0.06f, 0.6f);
    [SerializeField] private Color textColor = new Color(1f, 1f, 1f, 0.95f);

    private class Bar { public RectTransform fill; public Image fillImage; public Text value; }
    private Bar health, stamina;
    private Font font;

    private void Start() // after HotbarUI.Awake has built its canvas
    {
        if (attributes == null) attributes = GetComponent<PlayerAttributes>();
        if (attributes == null) { enabled = false; return; }
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        Transform canvas = FindHotbarCanvas();
        if (canvas == null) canvas = BuildCanvas();
        var root = NewRect("PlayerStats", canvas);
        root.anchorMin = root.anchorMax = root.pivot = Vector2.zero;
        root.anchoredPosition = margin;
        root.sizeDelta = new Vector2(barSize.x, barSize.y * 2f + barGap);

        stamina = BuildBar(root, "STAMINA", staminaColor, 0f);
        health = BuildBar(root, "HEALTH", healthColor, barSize.y + barGap);

        attributes.HealthChanged += (c, m) => Show(health, c, m, healthColor);
        attributes.StaminaChanged += (c, m) => Show(stamina, c, m, attributes.IsExhausted ? exhaustedColor : staminaColor);
        Show(health, attributes.Health, attributes.MaxHealth, healthColor);
        Show(stamina, attributes.Stamina, attributes.MaxStamina, staminaColor);
    }

    private void Show(Bar bar, float current, float max, Color color)
    {
        if (bar == null) return;
        float t = max > 0f ? Mathf.Clamp01(current / max) : 0f;
        bar.fill.anchorMax = new Vector2(t, 1f);
        bar.fillImage.color = color;
        bar.value.text = Mathf.CeilToInt(current) + " / " + Mathf.RoundToInt(max);
    }

    private Transform FindHotbarCanvas()
    {
        foreach (Canvas c in GetComponentsInChildren<Canvas>())
            if (c.name == "HotbarCanvas") return c.transform;
        return null;
    }

    private Transform BuildCanvas()
    {
        var go = new GameObject("StatsCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return go.transform;
    }

    private Bar BuildBar(RectTransform root, string label, Color color, float y)
    {
        var frame = NewRect(label, root);
        frame.anchorMin = frame.anchorMax = frame.pivot = Vector2.zero;
        frame.anchoredPosition = new Vector2(0f, y);
        frame.sizeDelta = barSize;
        frame.gameObject.AddComponent<Image>().color = backgroundColor;

        var fill = NewRect("Fill", frame);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(-2f, -2f);
        var fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = color;

        NewText("Label", frame, label, TextAnchor.MiddleLeft, new Vector2(8f, 0f));
        var value = NewText("Value", frame, "", TextAnchor.MiddleRight, new Vector2(-8f, 0f));
        return new Bar { fill = fill, fillImage = fillImage, value = value };
    }

    private Text NewText(string name, RectTransform parent, string text, TextAnchor anchor, Vector2 offset)
    {
        var r = NewRect(name, parent);
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(offset.x > 0f ? offset.x : 0f, 0f);
        r.offsetMax = new Vector2(offset.x < 0f ? offset.x : 0f, 0f);
        var t = r.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = Mathf.RoundToInt(barSize.y * 0.7f);
        t.fontStyle = FontStyle.Bold;
        t.alignment = anchor;
        t.color = textColor;
        t.text = text;
        t.raycastTarget = false;
        var shadow = r.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
        return t;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
