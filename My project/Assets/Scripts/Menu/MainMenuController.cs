using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The start screen. Put it on the menu's Canvas. PLAY fades to black and loads the gameplay
/// scene (Single mode, so the whole menu scene, its UI and EventSystem are unloaded).
/// More buttons (Settings, Credits, Quit) can be added later: give each a Button and point its
/// onClick at a public method here.
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Tooltip("Scene loaded by PLAY. Must be in the build's scene list.")]
    [SerializeField] private string gameplayScene = "PlayerTest";
    [SerializeField] private Button playButton;
    [Tooltip("Full-screen black overlay faded in before loading.")]
    [SerializeField] private CanvasGroup fade;
    [SerializeField, Min(0f)] private float fadeOutTime = 0.45f;
    [SerializeField, Min(0f)] private float fadeInTime = 0.6f;

    private bool loading;

    private void Start()
    {
        // The game locks the cursor; the menu needs it.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (playButton != null) playButton.onClick.AddListener(Play);
        Select(playButton);
        if (fade != null) StartCoroutine(Fade(1f, 0f, fadeInTime));
    }

    private void Update()
    {
        // Keyboard: if the mouse cleared the selection, any arrow / Enter / Space / Tab selects PLAY again.
        if (loading || EventSystem.current == null || EventSystem.current.currentSelectedGameObject != null) return;
        Keyboard k = Keyboard.current;
        if (k != null && (k.upArrowKey.wasPressedThisFrame || k.downArrowKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame ||
                          k.spaceKey.wasPressedThisFrame || k.tabKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame))
            Select(playButton);
    }

    /// <summary>Starts the game.</summary>
    public void Play()
    {
        if (loading) return;
        if (!Application.CanStreamedLevelBeLoaded(gameplayScene))
        {
            Debug.LogError($"[Ore What] Scene '{gameplayScene}' isn't in the build's scene list (File > Build Profiles > Scene List).");
            return;
        }
        loading = true;
        if (playButton != null) playButton.interactable = false;
        StartCoroutine(LoadGame());
    }

    private IEnumerator LoadGame()
    {
        AsyncOperation load = SceneManager.LoadSceneAsync(gameplayScene, LoadSceneMode.Single);
        load.allowSceneActivation = false; // keep the menu on screen until the fade has finished
        if (fade != null) yield return Fade(fade.alpha, 1f, fadeOutTime);
        load.allowSceneActivation = true;
    }

    private IEnumerator Fade(float from, float to, float time)
    {
        fade.blocksRaycasts = true;
        for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
        {
            fade.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / time));
            yield return null;
        }
        fade.alpha = to;
        fade.blocksRaycasts = to > 0f;
    }

    private static void Select(Selectable s)
    {
        if (s != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(s.gameObject);
    }
}
