using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover / keyboard-select / press feedback for a menu button: the button grows and its plate
/// lights up, the side markers (pickaxe icons) slide in, and it squashes while pressed.
/// Put it on the Button (set the Button's Transition to None; this does the visuals).
/// </summary>
public class MenuButtonFeedback : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    [SerializeField] private Graphic plate;
    [Tooltip("Keep the plate colours opaque: the Outline effect draws behind the plate and would show through.")]
    [SerializeField] private Color normalColor = new Color(0.13f, 0.1f, 0.08f, 1f);
    [SerializeField] private Color highlightColor = new Color(0.93f, 0.55f, 0.16f, 1f);
    [SerializeField] private Color pressedColor = new Color(0.7f, 0.36f, 0.08f, 1f);
    [SerializeField] private float highlightScale = 1.08f;
    [SerializeField] private float pressedScale = 0.95f;
    [Tooltip("Shown beside the button while it's highlighted.")]
    [SerializeField] private RectTransform[] markers;
    [SerializeField] private float markerSlide = 24f;
    [SerializeField, Min(1f)] private float speed = 14f;

    private bool hovered, selected, pressed;
    private float lit;            // 0 = normal, 1 = highlighted (smoothed)
    private Vector2[] markerHome;
    private Selectable selectable;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        markerHome = new Vector2[markers != null ? markers.Length : 0];
        for (int i = 0; i < markerHome.Length; i++) markerHome[i] = markers[i].anchoredPosition;
    }

    private void OnDisable() { hovered = selected = pressed = false; }

    public void OnPointerEnter(PointerEventData e) => hovered = true;
    public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; }
    public void OnPointerDown(PointerEventData e) => pressed = true;
    public void OnPointerUp(PointerEventData e) => pressed = false;
    public void OnSelect(BaseEventData e) => selected = true;
    public void OnDeselect(BaseEventData e) => selected = false;

    private void Update()
    {
        float k = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        bool interactable = selectable == null || selectable.IsInteractable();
        bool held = pressed && interactable;
        lit = Mathf.Lerp(lit, interactable && (hovered || selected) ? 1f : 0f, k);

        float scale = held ? pressedScale : Mathf.Lerp(1f, highlightScale, lit);
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * scale, k);
        if (plate != null) plate.color = held ? pressedColor : Color.Lerp(normalColor, highlightColor, lit);

        for (int i = 0; i < markerHome.Length; i++)
        {
            if (markers[i] == null) continue;
            float side = Mathf.Sign(markerHome[i].x);
            markers[i].anchoredPosition = markerHome[i] + new Vector2(side * markerSlide * (1f - lit), 0f);
            var g = markers[i].GetComponent<Graphic>();
            if (g != null) { Color c = g.color; c.a = lit; g.color = c; }
        }
    }
}
