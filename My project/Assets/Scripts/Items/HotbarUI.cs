using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The on-screen hotbar: one box per inventory slot (7 by default), centred at the bottom of the
/// screen, plus a "Holding: ..." line above it. Put it on the Player; it builds its own
/// Screen Space Overlay canvas at startup (Unity UI), so there's nothing to set up in the scene.
///
/// Each slot shows the item's Icon (from ItemData), its stack count (x12) when above 1, the slot
/// number (1-7), and a bright frame when it's the selected slot. An item without an icon gets a
/// simple placeholder (its initials on a coloured tile) instead of an error.
/// It only reads PlayerInventory and PlayerEquipment; it never changes them.
/// </summary>
public class HotbarUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerEquipment equipment;

    [Header("Layout (pixels at 1920x1080; scales with the screen)")]
    [SerializeField, Min(24f)] private float slotSize = 76f;
    [SerializeField, Min(0f)] private float spacing = 8f;
    [SerializeField, Min(0f)] private float bottomMargin = 22f;

    [Header("Colours")]
    [SerializeField] private Color slotColor = new Color(0.05f, 0.05f, 0.06f, 0.55f);
    [SerializeField] private Color frameColor = new Color(1f, 1f, 1f, 0.18f);
    [SerializeField] private Color selectedFrameColor = new Color(1f, 0.8f, 0.25f, 1f);
    [SerializeField] private Color textColor = new Color(1f, 1f, 1f, 0.95f);

    private class Slot
    {
        public Image frame, background, icon;
        public Text count, number, placeholder;
    }

    private Slot[] slots = new Slot[0];
    private RectTransform bar;
    private Text status;
    private Font font;

    private void Awake()
    {
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildCanvas();
    }

    private void BuildCanvas()
    {
        var canvasGO = new GameObject("HotbarCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        bar = NewRect("Hotbar", canvasGO.transform);
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, bottomMargin);

        status = NewText("Status", canvasGO.transform, 22, TextAnchor.LowerCenter);
        RectTransform sr = status.rectTransform;
        sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0f);
        sr.pivot = new Vector2(0.5f, 0f);
        sr.sizeDelta = new Vector2(900f, 30f);
        sr.anchoredPosition = new Vector2(0f, bottomMargin + slotSize + 10f);
        status.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);
    }

    private void BuildSlots(int count)
    {
        foreach (Transform child in bar) Destroy(child.gameObject);
        slots = new Slot[count];
        float width = count * slotSize + (count - 1) * spacing;
        bar.sizeDelta = new Vector2(width, slotSize);
        for (int i = 0; i < count; i++)
        {
            var s = new Slot();
            RectTransform root = NewRect($"Slot {i + 1}", bar);
            root.anchorMin = root.anchorMax = new Vector2(0f, 0.5f);
            root.pivot = new Vector2(0f, 0.5f);
            root.sizeDelta = new Vector2(slotSize, slotSize);
            root.anchoredPosition = new Vector2(i * (slotSize + spacing), 0f);

            s.frame = NewImage("Frame", root, Vector2.zero);                 // the border (fills the slot)
            s.background = NewImage("Background", root, new Vector2(-6f, -6f)); // inset: leaves a 3 px border
            s.icon = NewImage("Icon", root, new Vector2(-18f, -18f));
            s.icon.preserveAspect = true;

            s.placeholder = NewText("Placeholder", root, 22, TextAnchor.MiddleCenter);
            Stretch(s.placeholder.rectTransform, new Vector2(-8f, -8f));

            s.number = NewText("Number", root, 15, TextAnchor.UpperLeft);
            Stretch(s.number.rectTransform, new Vector2(-10f, -6f));
            s.number.text = (i + 1).ToString();

            s.count = NewText("Count", root, 18, TextAnchor.LowerRight);
            Stretch(s.count.rectTransform, new Vector2(-10f, -6f));
            s.count.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            slots[i] = s;
        }
    }

    private void LateUpdate()
    {
        if (inventory == null) return;
        if (slots.Length != inventory.SlotCount) BuildSlots(inventory.SlotCount);

        for (int i = 0; i < slots.Length; i++)
        {
            InventorySlot data = inventory.Slots[i];
            Slot s = slots[i];
            bool selected = i == inventory.SelectedSlot;
            s.frame.color = selected ? selectedFrameColor : frameColor;
            s.background.color = slotColor;

            ItemData item = data.IsEmpty ? null : data.item;
            Sprite icon = item != null ? item.Icon : null;
            s.icon.enabled = icon != null;
            if (icon != null) s.icon.sprite = icon;

            bool placeholder = item != null && icon == null;
            s.placeholder.enabled = placeholder;
            if (placeholder)
            {
                s.placeholder.text = Initials(item.DisplayName);
                s.background.color = PlaceholderColor(item);
            }

            s.count.enabled = item != null && data.amount > 1;
            if (s.count.enabled) s.count.text = "x" + data.amount;
            s.number.color = selected ? selectedFrameColor : new Color(1f, 1f, 1f, 0.6f);
        }

        if (status != null && equipment != null) status.text = equipment.StatusText;
    }

    private static string Initials(string name)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        string[] words = name.Split(' ');
        return words.Length > 1 ? $"{words[0][0]}{words[1][0]}".ToUpper() : name.Substring(0, Mathf.Min(2, name.Length));
    }

    private static Color PlaceholderColor(ItemData item)
    {
        // A stable colour per item, so the same item always looks the same.
        float hue = (Mathf.Abs(item.ItemId.GetHashCode()) % 360) / 360f;
        Color c = Color.HSVToRGB(hue, 0.45f, 0.45f);
        c.a = 0.85f;
        return c;
    }

    private RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private Image NewImage(string name, Transform parent, Vector2 inset)
    {
        RectTransform r = NewRect(name, parent);
        Stretch(r, inset);
        var img = r.gameObject.AddComponent<Image>();
        img.raycastTarget = false;
        return img;
    }

    private Text NewText(string name, Transform parent, int size, TextAnchor anchor)
    {
        RectTransform r = NewRect(name, parent);
        var t = r.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = FontStyle.Bold;
        t.alignment = anchor;
        t.color = textColor;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        return t;
    }

    private static void Stretch(RectTransform r, Vector2 inset)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = inset;
        r.anchoredPosition = Vector2.zero;
    }
}
