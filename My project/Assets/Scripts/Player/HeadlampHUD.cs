using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small "Headlamp: ON / OFF" note that appears for a moment when the headlamp is switched, just
/// above the health and stamina bars. Only listens to Headlamp. Put it on the Player.
/// Built at runtime on the hotbar's canvas (or its own), like PlayerStatsHUD.
/// </summary>
public class HeadlampHUD : MonoBehaviour
{
    [SerializeField] private Headlamp headlamp;
    [Tooltip("Position from the bottom-left corner (pixels at 1920x1080), above the health/stamina bars.")]
    [SerializeField] private Vector2 position = new Vector2(24f, 92f);
    [SerializeField, Min(0f)] private float showSeconds = 1.5f;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.4f;

    private Text label;
    private float shownAt = -100f;

    private void Start() // after HotbarUI has built its canvas
    {
        if (headlamp == null) headlamp = GetComponent<Headlamp>();
        if (headlamp == null) { enabled = false; return; }

        Transform canvas = null;
        foreach (Canvas c in GetComponentsInChildren<Canvas>()) if (c.name == "HotbarCanvas") canvas = c.transform;
        if (canvas == null)
        {
            var go = new GameObject("HeadlampCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvas = go.transform;
        }

        var rect = new GameObject("HeadlampStatus", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(canvas, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(300f, 26f);
        label = rect.gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 17;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleLeft;
        label.raycastTarget = false;
        rect.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.6f);
        label.color = Color.clear;

        headlamp.Changed += Show;
    }

    private void OnDestroy()
    {
        if (headlamp != null) headlamp.Changed -= Show;
    }

    private void Show(bool on)
    {
        if (label == null) return;
        label.text = on ? "Headlamp: ON" : "Headlamp: OFF";
        shownAt = Time.unscaledTime;
    }

    private void Update()
    {
        if (label == null) return;
        float age = Time.unscaledTime - shownAt;
        float alpha = age < showSeconds ? 1f : 1f - Mathf.Clamp01((age - showSeconds) / fadeSeconds);
        label.color = new Color(1f, 0.93f, 0.75f, alpha);
    }
}
