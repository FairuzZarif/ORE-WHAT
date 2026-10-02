using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The online part of the start screen: HOST GAME, JOIN GAME, a box for the join code and a status line
/// (progress and errors such as "No game with that code"). Put it on the menu's Canvas next to
/// MainMenuController; PLAY (single player) is untouched.
///
/// The buttons are built when the menu opens as copies of the PLAY button, so they share its look and its
/// hover/press feedback. The actual hosting/joining is NetworkSessionManager's job (on the Network Prefab,
/// created on first use and kept for the rest of the game).
/// </summary>
public class MultiplayerMenu : MonoBehaviour
{
    [Tooltip("The PLAY button: copied for the new buttons, and disabled while connecting.")]
    [SerializeField] private Button playButton;
    [Tooltip("Prefab with NetworkManager + UnityTransport + NetworkSessionManager.")]
    [SerializeField] private GameObject networkPrefab;
    [SerializeField] private Vector2 buttonSize = new Vector2(300f, 84f);
    [SerializeField] private float rowY = -300f;
    [SerializeField] private float columnX = 200f;

    private Button hostButton, joinButton;
    private InputField codeField;
    private Text status;
    private Font font;

    private NetworkSessionManager subscribed;

    /// <summary>The session manager, created from the Network Prefab on first use; the status line follows it.</summary>
    private NetworkSessionManager Session
    {
        get
        {
            if (NetworkSessionManager.Instance == null && networkPrefab != null) Instantiate(networkPrefab);
            var s = NetworkSessionManager.Instance;
            if (s != null && s != subscribed) { s.Changed += Refresh; subscribed = s; }
            return s;
        }
    }

    private void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (playButton == null) { Debug.LogWarning("[Ore What] MultiplayerMenu: no Play button set."); return; }

        hostButton = MakeButton("HostButton", "HOST GAME", new Vector2(-columnX, rowY));
        joinButton = MakeButton("JoinButton", "JOIN GAME", new Vector2(columnX, rowY));
        codeField = MakeCodeField(new Vector2(columnX, rowY - buttonSize.y * 0.5f - 44f));
        status = MakeText("Status", "", 26, new Vector2(0f, rowY - buttonSize.y * 0.5f - 112f), new Vector2(1400f, 44f));

        hostButton.onClick.AddListener(() => { if (Session != null) Session.Host(); });
        joinButton.onClick.AddListener(Join);
        codeField.onSubmit.AddListener(_ => Join()); // Enter in the code box

        if (NetworkSessionManager.Instance != null) _ = Session; // already exists (we came back from a game): follow it
        Refresh(); // shows why we came back here, e.g. "The host ended the game."
    }

    private void OnDestroy()
    {
        if (subscribed != null) subscribed.Changed -= Refresh;
    }

    private void Join()
    {
        if (Session != null) Session.Join(codeField.text);
    }

    private void Refresh()
    {
        var s = NetworkSessionManager.Instance;
        bool busy = s != null && s.Current != NetworkSessionManager.Phase.Offline;
        if (hostButton != null) hostButton.interactable = !busy;
        if (joinButton != null) joinButton.interactable = !busy;
        if (codeField != null) codeField.interactable = !busy;
        if (playButton != null) playButton.interactable = !busy;
        if (status != null)
        {
            status.text = s != null ? s.Status : "";
            status.color = s != null && s.StatusIsError ? new Color(1f, 0.45f, 0.35f) : new Color(1f, 0.82f, 0.55f);
        }
    }

    // ------------------------------------------------------------------ building the UI

    /// <summary>A copy of PLAY with text instead of its picture label, same plate, outline, shadow and hover markers.</summary>
    private Button MakeButton(string name, string text, Vector2 position)
    {
        // Copy while PLAY is inactive, so the copy's MenuButtonFeedback wakes up only after it has been resized.
        bool wasActive = playButton.gameObject.activeSelf;
        playButton.gameObject.SetActive(false);
        GameObject go = Instantiate(playButton.gameObject, playButton.transform.parent);
        playButton.gameObject.SetActive(wasActive);
        go.name = name;

        var rect = (RectTransform)go.transform;
        rect.sizeDelta = buttonSize;
        rect.anchoredPosition = position;
        Transform oldLabel = go.transform.Find("Label");
        if (oldLabel != null) DestroyImmediate(oldLabel.gameObject);
        foreach (string marker in new[] { "MarkerLeft", "MarkerRight" })
        {
            var m = go.transform.Find(marker) as RectTransform;
            if (m == null) continue;
            float side = marker == "MarkerLeft" ? -1f : 1f;
            m.sizeDelta = new Vector2(56f, 56f);
            m.anchoredPosition = new Vector2(side * (buttonSize.x * 0.5f + 40f), 0f);
        }

        Text label = MakeText("Label", text, 38, Vector2.zero, buttonSize, go.transform);
        label.fontStyle = FontStyle.Bold;
        label.color = new Color(1f, 0.93f, 0.8f);

        var button = go.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent(); // don't inherit PLAY's listeners
        go.SetActive(true);
        return button;
    }

    private InputField MakeCodeField(Vector2 position)
    {
        var go = new GameObject("JoinCodeField", typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(playButton.transform.parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(buttonSize.x, 60f);
        rect.anchoredPosition = position;
        var plate = go.GetComponent<Image>();
        var playPlate = playButton.GetComponent<Image>();
        plate.sprite = playPlate.sprite;
        plate.type = Image.Type.Sliced;
        plate.pixelsPerUnitMultiplier = playPlate.pixelsPerUnitMultiplier;
        plate.color = new Color(0.07f, 0.05f, 0.04f, 0.92f);
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.85f, 0.85f, 0.85f, 0.6f);
        outline.effectDistance = new Vector2(2f, -2f);

        Text text = MakeText("Text", "", 32, Vector2.zero, rect.sizeDelta - new Vector2(24f, 0f), go.transform);
        text.supportRichText = false;
        text.color = Color.white;
        Text placeholder = MakeText("Placeholder", "ENTER JOIN CODE", 26, Vector2.zero, rect.sizeDelta - new Vector2(24f, 0f), go.transform);
        placeholder.fontStyle = FontStyle.Italic;
        placeholder.color = new Color(1f, 1f, 1f, 0.35f);

        var field = go.GetComponent<InputField>();
        field.textComponent = text;
        field.placeholder = placeholder;
        field.characterLimit = 40;
        field.targetGraphic = plate;
        return field;
    }

    private Text MakeText(string name, string text, int size, Vector2 position, Vector2 box, Transform parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent != null ? parent : playButton.transform.parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        t.color = new Color(1f, 0.82f, 0.55f);
        t.rectTransform.sizeDelta = box;
        t.rectTransform.anchoredPosition = position;
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);
        return t;
    }
}
