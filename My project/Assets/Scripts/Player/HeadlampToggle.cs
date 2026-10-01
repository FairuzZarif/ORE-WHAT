using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The local player's headlamp key (L by default; F is already Store). One toggle per press:
/// holding the key doesn't flicker it. Put it on the Player next to Headlamp.
/// </summary>
public class HeadlampToggle : MonoBehaviour
{
    [SerializeField] private Key toggleKey = Key.L;
    [SerializeField] private Headlamp headlamp;

    private void Awake()
    {
        if (headlamp == null) headlamp = GetComponent<Headlamp>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || headlamp == null) return;
        if (keyboard[toggleKey].wasPressedThisFrame) headlamp.Toggle();
    }
}
