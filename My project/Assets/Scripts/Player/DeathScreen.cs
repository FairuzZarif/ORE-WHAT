using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "YOU DIED / Respawning in 5..." while the local player is dead, with a bar that fills up to the respawn.
/// Put it on the Player next to PlayerDeath; it only listens. Built at runtime on the hotbar's canvas, in the
/// same style as the HEALTH / STAMINA bars (dark panels, bold white text), so there's nothing to set up.
/// It fades in over the death camera (the body stays visible behind it) and disappears on respawn.
///
/// RESPAWN NOW (if CombatSettings allows it): the mouse is freed while dead; the button lights up a moment after
/// dying (Respawn Now Delay) and can be used once - PlayerDeath respawns right away in single player, or asks the host.
/// The automatic countdown keeps running meanwhile.
/// </summary>
public class DeathScreen : MonoBehaviour
{
    [SerializeField] private PlayerDeath death;

    [Header("Look")]
    [SerializeField] private Color dimColor = new Color(0.12f, 0.01f, 0.01f, 0.55f);
    [SerializeField] private Color titleColor = new Color(0.9f, 0.2f, 0.18f, 1f);
    [SerializeField] private Color barColor = new Color(0.86f, 0.22f, 0.2f, 1f);
    [SerializeField] private Color backgroundColor = new Color(0.05f, 0.05f, 0.06f, 0.6f);
    [SerializeField] private Color textColor = new Color(1f, 1f, 1f, 0.95f);
    [Tooltip("Seconds to fade in.")]
    [SerializeField, Min(0.01f)] private float fadeIn = 0.8f;

    private CanvasGroup group;
    private RectTransform barFill;
    private Text countdown;
    private Button respawnButton;
    private Text respawnLabel;
    private Font font;
    private float shownSince;

    private void Start() // after HotbarUI.Awake has built its canvas
    {
        if (death == null) death = GetComponent<PlayerDeath>();
        if (death == null) { enabled = false; return; }
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build(FindCanvas());
        death.DeathStarted += Show;
        death.Respawned += Hide;
        Hide();
    }

    private void OnDestroy()
    {
        if (death != null) { death.DeathStarted -= Show; death.Respawned -= Hide; }
    }

    private void Show()
    {
        shownSince = Time.time;
        group.transform.SetAsLastSibling(); // over the hotbar and the bars
        group.gameObject.SetActive(true);
        respawnButton.gameObject.SetActive(CombatSettings.Current.allowRespawnNow);
        if (CombatSettings.Current.allowRespawnNow)
        {
            // Free the mouse for the button (PlayerDeath locks it again on respawn).
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            EnsureEventSystem();
        }
        Update();
    }

    /// <summary>The RESPAWN NOW button's click: asks once (PlayerDeath / the host decide).</summary>
    public void RespawnNow()
    {
        if (death != null && death.RequestRespawnNow()) respawnButton.interactable = false;
    }

    private void Hide() { if (group != null) group.gameObject.SetActive(false); }

    private void Update()
    {
        if (group == null || !group.gameObject.activeSelf) return;
        group.alpha = Mathf.Clamp01((Time.time - shownSince) / fadeIn);
        float left = death.RespawnCountdown;
        countdown.text = left > 0.05f && !death.RespawnRequested ? $"Respawning in {Mathf.CeilToInt(left)}..." : "Respawning...";
        respawnButton.interactable = death.CanRespawnNow;
        respawnLabel.color = new Color(textColor.r, textColor.g, textColor.b, death.CanRespawnNow ? textColor.a : 0.35f);
        float total = Mathf.Max(0.01f, death.RespawnDelay);
        barFill.anchorMax = new Vector2(Mathf.Clamp01(1f - left / total), 1f);
    }

    // ---------------------------------------------------------------- building

    private Transform FindCanvas()
    {
        foreach (Canvas c in GetComponentsInChildren<Canvas>(true))
            if (c.name == "HotbarCanvas") return c.transform;
        var go = new GameObject("DeathCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return go.transform;
    }

    private void Build(Transform canvas)
    {
        RectTransform root = NewRect("DeathScreen", canvas);
        Stretch(root);
        if (canvas.GetComponent<GraphicRaycaster>() == null) canvas.gameObject.AddComponent<GraphicRaycaster>(); // for the button
        group = root.gameObject.AddComponent<CanvasGroup>();
        root.gameObject.AddComponent<Image>().color = dimColor;

        Text title = NewText("Title", root, "YOU DIED", 96, titleColor);
        title.rectTransform.anchoredPosition = new Vector2(0f, 70f);
        title.rectTransform.sizeDelta = new Vector2(1200f, 130f);

        countdown = NewText("Countdown", root, "", 30, textColor);
        countdown.rectTransform.anchoredPosition = new Vector2(0f, -20f);
        countdown.rectTransform.sizeDelta = new Vector2(800f, 44f);

        RectTransform frame = NewRect("Progress", root);
        frame.anchoredPosition = new Vector2(0f, -70f);
        frame.sizeDelta = new Vector2(420f, 14f);
        frame.gameObject.AddComponent<Image>().color = backgroundColor;
        barFill = NewRect("Fill", frame);
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = new Vector2(0f, 1f);
        barFill.offsetMin = new Vector2(2f, 2f);
        barFill.offsetMax = new Vector2(-2f, -2f);
        barFill.gameObject.AddComponent<Image>().color = barColor;

        // [ RESPAWN NOW ]: a dark panel like the bars, lighting up red under the mouse.
        RectTransform buttonRect = NewRect("RespawnNow", root);
        buttonRect.anchoredPosition = new Vector2(0f, -130f);
        buttonRect.sizeDelta = new Vector2(260f, 52f);
        var image = buttonRect.gameObject.AddComponent<Image>();
        image.color = Color.white;
        respawnButton = buttonRect.gameObject.AddComponent<Button>();
        respawnButton.targetGraphic = image;
        ColorBlock colours = respawnButton.colors;
        colours.normalColor = new Color(0.08f, 0.06f, 0.05f, 0.85f);
        colours.highlightedColor = new Color(0.62f, 0.12f, 0.1f, 0.95f);
        colours.selectedColor = colours.normalColor;
        colours.pressedColor = new Color(0.86f, 0.22f, 0.2f, 1f);
        colours.disabledColor = new Color(0.08f, 0.06f, 0.05f, 0.5f);
        colours.colorMultiplier = 1f;
        colours.fadeDuration = 0.08f;
        respawnButton.colors = colours;
        respawnButton.onClick.AddListener(RespawnNow);
        respawnLabel = NewText("Label", buttonRect, "RESPAWN NOW", 24, textColor);
        Stretch(respawnLabel.rectTransform);
        group.blocksRaycasts = true; // so the button can be clicked
        group.interactable = true;
        root.GetComponent<Image>().raycastTarget = false; // but the dim backdrop doesn't swallow anything
    }

    /// <summary>uGUI buttons need an EventSystem; the game scene has none (its other HUD is drawn without one).</summary>
    private static void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) return;
        var go = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        go.transform.SetParent(null);
    }

    private Text NewText(string name, RectTransform parent, string text, int size, Color color)
    {
        RectTransform r = NewRect(name, parent);
        var t = r.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = color;
        t.text = text;
        t.raycastTarget = false;
        var shadow = r.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }
}
