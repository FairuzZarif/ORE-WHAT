using UnityEngine;

/// <summary>
/// Placeholder on-screen UI for items (IMGUI, like the crosshair), easy to replace later:
///   - under the crosshair: "[E] Pick up Copper Ore" and messages like "Inventory Full"
///   - bottom-left: the inventory slots, the selected slot, and the total value
/// Put it on the Player. It only reads PlayerInventory and ItemPickupInteractor.
/// </summary>
public class InventoryHUD : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private ItemPickupInteractor pickup;
    [SerializeField] private PlayerEquipment equipment;
    [SerializeField] private bool showInventory = true;
    [SerializeField] private Color promptColor = Color.white;
    [SerializeField] private Color messageColor = new Color(1f, 0.45f, 0.35f);

    private GUIStyle centerStyle, listStyle;

    private void Awake()
    {
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (pickup == null) pickup = GetComponent<ItemPickupInteractor>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
    }

    private void OnGUI()
    {
        if (centerStyle == null)
        {
            centerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };
            listStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        }

        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        if (pickup != null)
        {
            if (pickup.PromptText != null) Shadowed(new Rect(cx - 250f, cy + 24f, 500f, 28f), pickup.PromptText, centerStyle, promptColor);
            if (pickup.Message != null) Shadowed(new Rect(cx - 250f, cy + 52f, 500f, 28f), pickup.Message, centerStyle, messageColor);
        }

        if (!showInventory || inventory == null) return;
        float line = 20f, x = 16f;
        float y = Screen.height - 16f - line * (inventory.SlotCount + 2);
        if (equipment != null)
        {
            string held = equipment.Equipped != null ? $"Holding: {equipment.Equipped.DisplayName}   [G] throw" : "Hands empty";
            Shadowed(new Rect(x, y - line * 1.5f, 400f, line), held, listStyle, new Color(0.7f, 0.9f, 1f));
        }
        Shadowed(new Rect(x, y, 400f, line), $"Inventory   ${inventory.TotalValue}   [1-9] select  [Q] drop  [Ctrl+Q] drop stack", listStyle, Color.white);
        for (int i = 0; i < inventory.SlotCount; i++)
        {
            InventorySlot s = inventory.Slots[i];
            bool selected = i == inventory.SelectedSlot;
            string text = s.IsEmpty ? "-" : $"{s.item.DisplayName} x{s.amount}";
            Shadowed(new Rect(x, y + line * (i + 1), 400f, line), $"{(selected ? ">" : " ")} {i + 1}  {text}",
                     listStyle, selected ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 1f, 1f, 0.8f));
        }
    }

    private static void Shadowed(Rect r, string text, GUIStyle style, Color color)
    {
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
        GUI.color = color;
        GUI.Label(r, text, style);
        GUI.color = old;
    }
}
