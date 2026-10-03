using UnityEngine;

/// <summary>
/// Draws a small dot in the centre of the screen so you can see what the
/// mining ray is aiming at. Temporary until there's a real UI.
///
/// Also the hit marker: <see cref="ShowHitMarker"/> flashes four short ticks around the dot when one of YOUR
/// attacks actually hurt another player (the host confirms it; firing alone never shows it). Kills show a
/// bigger red one. Local only: nothing about it goes over the network. Hidden while dead.
/// </summary>
public class Crosshair : MonoBehaviour
{
    [SerializeField, Min(1f)] private float size = 6f;
    [SerializeField] private Color color = new Color(1f, 1f, 1f, 0.8f);

    [Header("Hit marker")]
    [SerializeField] private Color hitColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Color killColor = new Color(0.95f, 0.2f, 0.15f, 1f);
    [Tooltip("Seconds the marker stays (it pops out a little and fades).")]
    [SerializeField, Min(0.05f)] private float hitTime = 0.28f;
    [Tooltip("Tick length and distance from the centre, pixels at 1080p.")]
    [SerializeField] private float tickLength = 9f, tickGap = 7f;

    private static float lastHitTime = -10f;
    private static bool lastWasKill;
    private PlayerDeath death;

    /// <summary>How many hit markers were shown this session (for tests).</summary>
    public static int HitMarkersShown { get; private set; }

    /// <summary>Shows the hit marker (kill = the hit killed them).</summary>
    public static void ShowHitMarker(bool kill)
    {
        lastHitTime = Time.time;
        lastWasKill = kill;
        HitMarkersShown++;
    }

    private void Awake() => death = GetComponent<PlayerDeath>();

    private void OnGUI()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        if (death != null && death.IsDead) return;

        var rect = new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size);
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        DrawHitMarker();
        GUI.color = old;
    }

    private void DrawHitMarker()
    {
        float age = Time.time - lastHitTime;
        if (age < 0f || age > hitTime) return;
        float t = age / hitTime;
        float scale = Screen.height / 1080f * (lastWasKill ? 1.35f : 1f);
        float pop = 1f + 0.35f * Mathf.Sin(Mathf.Min(1f, t * 3f) * Mathf.PI); // quick pop out, then back
        Color c = lastWasKill ? killColor : hitColor;
        GUI.color = new Color(c.r, c.g, c.b, c.a * (1f - t * t));

        Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float length = tickLength * scale, gap = tickGap * scale * pop, thick = Mathf.Max(2f, 2f * scale);
        Matrix4x4 matrix = GUI.matrix;
        for (int i = 0; i < 4; i++)
        {
            // Diagonal ticks: rotate the GUI around the centre and draw one horizontal bar to the right.
            GUI.matrix = matrix;
            GUIUtility.RotateAroundPivot(45f + 90f * i, centre);
            GUI.DrawTexture(new Rect(centre.x + gap, centre.y - thick * 0.5f, length, thick), Texture2D.whiteTexture);
        }
        GUI.matrix = matrix;
    }
}
