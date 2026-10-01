using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Punching with empty hands. Put it on the Player next to PlayerEquipment.
/// An empty hotbar slot means nothing is equipped (relaxed arms, nothing on screen); a left click then
/// briefly shows the fists view (PlayerEquipment.ShowUnarmedAction) and plays a punch. When the
/// hands are back down the view is hidden again. Selecting an item or carrying an ore cancels it.
/// </summary>
[DefaultExecutionOrder(-10)] // read the click before PlayerLook re-locks the cursor (like the held items)
public class UnarmedAttack : MonoBehaviour
{
    [SerializeField] private PlayerEquipment equipment;
    [Tooltip("Optional. Empty = the FistsController on PlayerEquipment's Unarmed View.")]
    [SerializeField] private FistsController fists;

    private void Awake()
    {
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
        if (fists == null && equipment != null && equipment.UnarmedView != null)
            fists = equipment.UnarmedView.GetComponent<FistsController>();
    }

    private void OnEnable() { if (fists != null) fists.Finished += OnFinished; }
    private void OnDisable() { if (fists != null) fists.Finished -= OnFinished; }

    private void OnFinished() { if (equipment != null) equipment.ShowUnarmedAction(false); }

    private void Update()
    {
        if (equipment == null || fists == null || equipment.Equipped != null || equipment.IsCarrying) return;
        Mouse mouse = Mouse.current;
        if (mouse == null || Cursor.lockState != CursorLockMode.Locked || !mouse.leftButton.wasPressedThisFrame) return;
        Punch();
    }

    /// <summary>Punches if the hands are empty (also used by tests). Returns false if it couldn't.</summary>
    public bool Punch()
    {
        if (equipment == null || fists == null || equipment.Equipped != null || equipment.IsCarrying) return false;
        bool wasShown = equipment.UnarmedActionShown;
        equipment.ShowUnarmedAction(true); // shows the fists view (its hands start lowered, off screen)
        if (fists.Punch()) return true;
        if (!wasShown) equipment.ShowUnarmedAction(false); // still cooling down: nothing to show
        return false;
    }
}
