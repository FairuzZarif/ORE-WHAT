using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drops items from the inventory as physical objects, tossed gently in front of the player.
/// Put it on the Player.
///   Q            drop one item from the selected hotbar slot (1-7 / mouse wheel, on PlayerEquipment)
///   Ctrl+Q       drop the whole stack (as one object, e.g. "Copper Ore x5")
/// (G, on PlayerEquipment, throws the item in the hands instead.)
/// Other scripts (a future inventory screen) can call <see cref="Drop"/> directly.
/// </summary>
public class ItemDropper : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private CharacterController controller;
    [SerializeField] private PlayerEquipment equipment;

    [Header("Input")]
    [SerializeField] private Key dropKey = Key.Q;

    [Header("Drop")]
    [Tooltip("How far in front of the eyes the item appears (metres).")]
    [SerializeField, Min(0.1f)] private float dropDistance = 0.7f;
    [Tooltip("Height of the spawn point relative to the eyes (metres). Negative = lower, around the hands.")]
    [SerializeField] private float spawnHeight = -0.3f;
    [Tooltip("Speed the item is tossed forward along the view, in m/s.")]
    [SerializeField, Min(0f)] private float dropForce = 2f;
    [Tooltip("Extra upward speed so it arcs a little (m/s).")]
    [SerializeField, Min(0f)] private float upwardForce = 1f;
    [Tooltip("Maximum random spin, in radians per second.")]
    [SerializeField, Min(0f)] private float randomTorque = 5f;
    [Tooltip("Add the player's own movement, so items don't fall behind when dropped while running.")]
    [SerializeField] private bool inheritPlayerVelocity = true;
    [Tooltip("Layers the item must not spawn inside (walls, rocks).")]
    [SerializeField] private LayerMask blockingLayers = 1; // Default

    private void Awake()
    {
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (controller == null) controller = GetComponent<CharacterController>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || inventory == null || Cursor.lockState != CursorLockMode.Locked) return;

        // Not mid-swing (same rule as G), and not while the hands are carrying a world ore
        // (then E releases / G throws that ore instead).
        bool blocked = equipment != null && (equipment.IsCarrying || (equipment.ActiveController != null && equipment.ActiveController.IsBusy));
        if (keyboard[dropKey].wasPressedThisFrame && !blocked)
        {
            bool wholeStack = keyboard.ctrlKey.isPressed;
            InventorySlot slot = inventory.Slots[inventory.SelectedSlot];
            if (!slot.IsEmpty)
                Drop(inventory.SelectedSlot, wholeStack ? slot.amount : 1);
        }
    }

    /// <summary>Takes items out of a slot and tosses them into the world. Returns the new world object.</summary>
    public DroppedItem Drop(int slotIndex, int amount)
    {
        if (playerCamera == null || slotIndex < 0 || slotIndex >= inventory.SlotCount) return null;
        ItemData item = inventory.Slots[slotIndex].item;
        int removed = inventory.RemoveFromSlot(slotIndex, amount);
        if (item == null || removed <= 0) return null;

        Transform eye = playerCamera.transform;
        Vector3 forward = eye.forward;
        Vector3 origin = eye.position + Vector3.up * spawnHeight;

        // Don't spawn inside a wall right in front of the player: stop short of it.
        float distance = dropDistance;
        const float clearance = 0.15f;
        if (Physics.SphereCast(origin, clearance, forward, out RaycastHit hit, dropDistance, blockingLayers, QueryTriggerInteraction.Ignore))
            distance = Mathf.Max(0f, hit.distance - 0.02f);
        Vector3 position = origin + forward * distance;

        Vector3 velocity = forward * dropForce + Vector3.up * upwardForce;
        if (inheritPlayerVelocity && controller != null) velocity += controller.velocity;
        Vector3 spin = Random.insideUnitSphere * randomTorque;

        DroppedItem dropped = ItemDrops.Spawn(item, removed, position, Random.rotation, velocity, spin);
        if (dropped != null) GetComponent<ItemPickupInteractor>()?.PlayDropSound();
        return dropped;
    }
}
