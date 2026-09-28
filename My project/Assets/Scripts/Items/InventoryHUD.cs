using UnityEngine;

/// <summary>
/// Crosshair-area text for items (IMGUI, like the crosshair), easy to replace later:
/// "[E] Carry Copper Ore / [F] Store Copper Ore" prompts and messages like "Inventory Full".
/// The hotbar and "Holding: ..." line are drawn by HotbarUI.
/// Put it on the Player. It only reads ItemPickupInteractor.
/// </summary>
public class InventoryHUD : MonoBehaviour
{
    [SerializeField] private ItemPickupInteractor pickup;
    [SerializeField] private Color promptColor = Color.white;
    [SerializeField] private Color messageColor = new Color(1f, 0.45f, 0.35f);

    private GUIStyle centerStyle;

    private void Awake()
    {
        if (pickup == null) pickup = GetComponent<ItemPickupInteractor>();
    }

    private void OnGUI()
    {
        if (pickup == null) return;
        if (centerStyle == null)
            centerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold };

        float cx = Screen.width * 0.5f, y = Screen.height * 0.5f + 24f;
        string prompt = pickup.PromptText;
        if (prompt != null)
        {
            foreach (string line in prompt.Split('\n'))
            {
                Shadowed(new Rect(cx - 300f, y, 600f, 26f), line, centerStyle, promptColor);
                y += 26f;
            }
        }
        if (pickup.Message != null) Shadowed(new Rect(cx - 300f, y + 2f, 600f, 28f), pickup.Message, centerStyle, messageColor);
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
