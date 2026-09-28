using UnityEngine;

/// <summary>
/// Draws a small dot in the centre of the screen so you can see what the
/// mining ray is aiming at. Temporary until there's a real UI.
/// </summary>
public class Crosshair : MonoBehaviour
{
    [SerializeField, Min(1f)] private float size = 6f;
    [SerializeField] private Color color = new Color(1f, 1f, 1f, 0.8f);

    private void OnGUI()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;

        var rect = new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size);
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }
}
