using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// In-game multiplayer info, drawn on top of the game (placeholder IMGUI like the inventory HUD):
/// the join code in the top-right corner (so the host can read it out to friends), short notices such as
/// "Player 2 joined", and, while the cursor is free (Esc), a small panel to copy the code or leave the game.
/// Lives next to NetworkSessionManager and shows nothing outside a multiplayer game.
/// </summary>
[RequireComponent(typeof(NetworkSessionManager))]
[DefaultExecutionOrder(-100)]
public class MultiplayerHUD : MonoBehaviour
{
    [SerializeField, Min(0.5f)] private float noticeSeconds = 4f;

    private NetworkSessionManager session;
    private readonly List<(string text, float until)> notices = new List<(string, float)>();
    private GUIStyle box, label, small, button;
    private int players;
    private float nextCount;
    private bool cursorFreeThisFrame;
    private PlayerDeath localDeath;

    private void Awake()
    {
        session = GetComponent<NetworkSessionManager>();
        session.Notice += text => notices.Add((text, Time.unscaledTime + noticeSeconds));
    }

    private void OnGUI()
    {
        if (session.Current != NetworkSessionManager.Phase.InGame) return;
        MakeStyles();
        float scale = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float width = Screen.width / scale;

        if (Time.unscaledTime >= nextCount)
        {
            players = FindObjectsByType<NetworkPlayerAvatar>().Length;
            nextCount = Time.unscaledTime + 1f;
        }

        // Join code, top right.
        var codeRect = new Rect(width - 360f, 20f, 340f, 74f);
        GUI.Box(codeRect, GUIContent.none, box);
        GUI.Label(new Rect(codeRect.x + 16f, codeRect.y + 6f, 320f, 36f), $"Join code: {session.JoinCode}", label);
        GUI.Label(new Rect(codeRect.x + 16f, codeRect.y + 40f, 320f, 26f),
                  $"{(session.IsHost ? "Hosting" : "Joined")}  ·  {players} player{(players == 1 ? "" : "s")}  ·  Esc = menu", small);

        // Notices under it.
        notices.RemoveAll(n => Time.unscaledTime > n.until);
        for (int i = 0; i < notices.Count; i++)
            GUI.Label(new Rect(width - 360f, 104f + i * 30f, 340f, 28f), notices[i].text, small);

        // Esc menu while the cursor is free. Clicks are taken on mouse DOWN: PlayerLook locks the cursor again on that
        // same click (in Update, before this), so a normal GUI.Button (which fires on mouse up) would never fire.
        if (!cursorFreeThisFrame && Cursor.lockState == CursorLockMode.Locked) return;
        if (localDeath == null) localDeath = FindAnyObjectByType<PlayerDeath>();
        if (localDeath != null && localDeath.IsDead) return; // the cursor is free for the death screen's button, not this menu
        var panel = new Rect(width * 0.5f - 200f, 1080f * 0.5f - 90f, 400f, 180f);
        GUI.Box(panel, GUIContent.none, box);
        if (Clicked(new Rect(panel.x + 30f, panel.y + 25f, 340f, 56f), "Copy join code"))
            GUIUtility.systemCopyBuffer = session.JoinCode;
        if (Clicked(new Rect(panel.x + 30f, panel.y + 98f, 340f, 56f), session.IsHost ? "End game (back to menu)" : "Leave game (back to menu)"))
            session.Leave();
    }

    private bool Clicked(Rect rect, string text)
    {
        GUI.Box(rect, text, button);
        Event e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || !rect.Contains(e.mousePosition)) return false;
        e.Use();
        return true;
    }

    // Runs before PlayerLook (which re-locks the cursor on a click).
    private void Update() => cursorFreeThisFrame = Cursor.lockState != CursorLockMode.Locked;

    private void MakeStyles()
    {
        if (box != null) return;
        var dark = new Texture2D(1, 1);
        dark.SetPixel(0, 0, new Color(0.08f, 0.06f, 0.05f, 0.85f));
        dark.Apply();
        box = new GUIStyle(GUI.skin.box) { normal = { background = dark } };
        label = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.82f, 0.55f) } };
        small = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = new Color(1f, 1f, 1f, 0.8f) } };
        button = new GUIStyle(GUI.skin.button) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
    }
}
