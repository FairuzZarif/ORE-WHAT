using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// What's in the player's hands. Put it on the Player, next to PlayerInventory.
///
/// The inventory's SELECTED SLOT (PlayerInventory.SelectedSlot) is the one source of truth: this
/// component holds no item of its own, it only shows whatever that slot holds.
///
///   1-7 (one per slot)  select that hotbar slot (an empty slot = empty hands; UnarmedAttack can punch from there)
///   Mouse wheel         cycle through the occupied slots
///   G                   throw what's in the hands: the selected item (one of a stack), or the
///                       ore you're carrying. It leaves the hands where it is on screen.
///
/// Which first-person view is shown:
///   - a tool/weapon with an entry in Views (pickaxe, hammer): its own view (arms + tool + controller)
///   - any other item (ores...): the shared HeldResourceView, holding a visual-only copy of it
///   - while carrying a world ore (OreCarryController): the HeldResourceView around the real ore;
///     the selected slot is untouched and comes back when the ore is released or stored
/// Only one view is ever active. Item-specific behaviour (the pickaxe swing, a future sword/gun)
/// lives on that view (HeldItemController), not here. Mining needs the held item to Can Mine.
/// </summary>
public class PlayerEquipment : MonoBehaviour
{
    /// <summary>Links a tool/weapon to what's shown in first person while holding it.</summary>
    [Serializable]
    public class HeldView
    {
        public ItemData item;
        [Tooltip("First-person object shown while this item is held (e.g. FirstPersonViewModel).")]
        public GameObject view;
        [Tooltip("Optional. When thrown, the world item starts exactly at this transform (e.g. the held pickaxe mesh), " +
                 "so it leaves the hands seamlessly. Empty = in front of the camera.")]
        public Transform throwFrom;
    }

    [Header("References (auto-found if empty)")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private OreCarryController carrier;
    [Tooltip("Optional. The held item is shown in first person somewhere other than its real pose; throws start where it's shown.")]
    [SerializeField] private FirstPersonPresentation presentation;

    [Header("Held item")]
    [Tooltip("Put into the inventory and selected when the game starts (can be empty).")]
    [SerializeField] private ItemData startingItem;
    [Tooltip("Tools/weapons with their own first-person view.")]
    [SerializeField] private HeldView[] views = new HeldView[0];
    [Tooltip("Shared view for every other item (ores...) and for carried world ores.")]
    [SerializeField] private HeldResourceView itemView;
    [Tooltip("Optional. Shown ONLY while an unarmed punch plays (UnarmedAttack calls ShowUnarmedAction). " +
             "An empty slot itself shows nothing: relaxed arms.")]
    [SerializeField] private GameObject fistsView;

    [Header("Throw")]
    [SerializeField] private Key throwKey = Key.G;
    [Tooltip("Forward speed of the throw (m/s). ~2 = drop it at your feet, ~7 = a proper throw.")]
    [SerializeField, Min(0f)] private float throwForce = 6f;
    [Tooltip("Extra upward speed for an arc (m/s).")]
    [SerializeField, Min(0f)] private float throwUpward = 1.5f;
    [Tooltip("End-over-end spin (radians per second).")]
    [SerializeField] private float throwSpin = 9f;
    [Tooltip("Small random extra spin (radians per second).")]
    [SerializeField, Min(0f)] private float randomTorque = 2f;
    [SerializeField] private bool inheritPlayerVelocity = true;
    [Tooltip("Layers a thrown item must not spawn inside (walls, rocks).")]
    [SerializeField] private LayerMask blockingLayers = 1; // Default

    /// <summary>The selected slot if it holds an item, else -1.</summary>
    public int EquippedSlotIndex
    {
        get
        {
            if (inventory == null || inventory.SlotCount == 0) return -1;
            int i = inventory.SelectedSlot;
            return inventory.Slots[i].IsEmpty ? -1 : i;
        }
    }
    /// <summary>The selected inventory item (any kind: tool, ore...), or null for empty hands.</summary>
    public ItemData Equipped => EquippedSlotIndex >= 0 ? inventory.Slots[EquippedSlotIndex].item : null;
    /// <summary>True while a world ore is being carried (the hands hold it instead of the selected item).</summary>
    public bool IsCarrying => carrier != null && carrier.IsCarrying;
    /// <summary>True while holding something that can mine (not while carrying an ore).</summary>
    public bool CanMine => !hidden && !IsCarrying && Equipped != null && Equipped.CanMine;
    /// <summary>The shown view's behaviour, e.g. a MiningToolController (the FistsController only during an unarmed punch). Null for held ores and empty hands.</summary>
    public HeldItemController ActiveController { get; private set; }
    /// <summary>"Holding: Pickaxe   [G] Throw", "Carrying: Copper Ore", "Hands empty"...</summary>
    public string StatusText
    {
        get
        {
            if (IsCarrying) return $"Carrying: {carrier.LastCarried.DisplayName}";
            ItemData item = Equipped;
            if (item == null) return "Hands empty";
            int amount = inventory.Slots[EquippedSlotIndex].amount;
            return amount > 1 ? $"Holding: {item.DisplayName} x{amount}   [{throwKey}] Throw one" : $"Holding: {item.DisplayName}   [{throwKey}] Throw";
        }
    }
    /// <summary>Raised when what's shown in the hands changes.</summary>
    public event Action Changed;

    private CharacterController controller;
    private GameObject shownView;
    private ItemData shownItem;
    private bool shownCarrying, shownOnce, unarmedActionShown, hidden;

    /// <summary>True while the hands are put away (dead): nothing is shown and slot keys / throwing are ignored.</summary>
    public bool Hidden => hidden;

    /// <summary>Puts the hands away (no view, no weapon / tool / punch) or brings back the selected item.</summary>
    public void SetHidden(bool hide)
    {
        if (hidden == hide) return;
        hidden = hide;
        if (hide) unarmedActionShown = false;
        shownOnce = false; // force Refresh to switch
        Refresh();
    }

    /// <summary>
    /// The attack behaviour used with this item (its view's HeldItemController; null item = the unarmed punch), or
    /// null if it has none (e.g. ores). The host uses it to check other players' hits against the real values.
    /// </summary>
    public HeldItemController ControllerFor(ItemData item)
    {
        if (item == null) return fistsView != null ? fistsView.GetComponentInChildren<HeldItemController>(true) : null;
        HeldView v = Find(item);
        return v != null && v.view != null ? v.view.GetComponentInChildren<HeldItemController>(true) : null;
    }

    private void Awake()
    {
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>(true);
        if (carrier == null) carrier = GetComponent<OreCarryController>();
        if (presentation == null && playerCamera != null) presentation = playerCamera.GetComponent<FirstPersonPresentation>();
        controller = GetComponent<CharacterController>();

        if (startingItem != null && inventory != null && inventory.AddItem(startingItem, 1, out int slot) > 0)
            inventory.SelectSlot(slot);
        Refresh();
    }

    private void OnEnable()
    {
        if (inventory != null) inventory.Changed += Refresh;
        if (carrier != null) carrier.CarryChanged += Refresh;
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.Changed -= Refresh;
        if (carrier != null) carrier.CarryChanged -= Refresh;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || inventory == null || hidden || Cursor.lockState != CursorLockMode.Locked) return;

        for (int i = 0; i < Mathf.Min(9, inventory.SlotCount); i++)
            if (keyboard[Key.Digit1 + i].wasPressedThisFrame && !MidStrike())
                inventory.SelectSlot(i);

        Mouse mouse = Mouse.current;
        float scroll = mouse != null ? mouse.scroll.ReadValue().y : 0f;
        if (Mathf.Abs(scroll) > 0.01f && !MidStrike()) SelectNextOccupied(scroll > 0f ? -1 : 1);

        if (keyboard[throwKey].wasPressedThisFrame)
        {
            if (IsCarrying) ThrowCarried();
            else if (Equipped != null && !MidStrike()) Throw();
        }
    }

    /// <summary>The view shown while punching with empty hands (or null).</summary>
    public GameObject UnarmedView => fistsView;
    /// <summary>True while the unarmed punch view is up.</summary>
    public bool UnarmedActionShown => unarmedActionShown;

    /// <summary>Shows / hides the unarmed punch view. Ignored while holding an item or carrying an ore.</summary>
    public void ShowUnarmedAction(bool show)
    {
        if (show && (Equipped != null || IsCarrying || hidden)) show = false;
        if (show == unarmedActionShown) return;
        unarmedActionShown = show;
        Refresh();
    }

    /// <summary>Selects an inventory slot (kept for callers from before the hotbar; same as PlayerInventory.SelectSlot).</summary>
    public void EquipSlot(int slotIndex) => inventory.SelectSlot(slotIndex);

    private void SelectNextOccupied(int step)
    {
        int n = inventory.SlotCount;
        for (int k = 1; k <= n; k++)
        {
            int i = ((inventory.SelectedSlot + step * k) % n + n) % n;
            if (!inventory.Slots[i].IsEmpty) { inventory.SelectSlot(i); return; }
        }
    }

    /// <summary>Shows the right first-person view for the selected slot / carried ore. Only switches when that changes.</summary>
    private void Refresh()
    {
        bool carrying = IsCarrying;
        ItemData item = carrying ? null : Equipped;
        if (carrying || item != null) unarmedActionShown = false; // holding something cancels a punch
        HeldView toolView = Find(item);
        GameObject view = hidden ? null // dead: hands put away
                        : carrying ? ItemViewObject
                        : toolView != null && toolView.view != null ? toolView.view
                        : item != null ? ItemViewObject
                        : unarmedActionShown ? fistsView : null; // empty slot = nothing, except during a punch

        if (shownOnce && view == shownView && item == shownItem && carrying == shownCarrying)
        {
            if (carrying && itemView != null) itemView.ShowCarried(carrier.LastCarried); // e.g. a second ore picked up
            return;
        }

        // Exactly one view active (the target stays active if it already was, so a tool's swing isn't reset).
        foreach (HeldView v in views)
            if (v.view != null && v.view != view) v.view.SetActive(false);
        if (ItemViewObject != null && ItemViewObject != view) ItemViewObject.SetActive(false);
        if (fistsView != null && fistsView != view) fistsView.SetActive(false);
        if (view != null) view.SetActive(true);

        if (view != null && view == ItemViewObject)
        {
            if (carrying) itemView.ShowCarried(carrier.LastCarried);
            else itemView.ShowItem(item);
        }

        ActiveController = view != null ? view.GetComponentInChildren<HeldItemController>(true) : null;
        shownView = view; shownItem = item; shownCarrying = carrying; shownOnce = true;
        Changed?.Invoke();
    }

    private GameObject ItemViewObject => itemView != null ? itemView.gameObject : null;

    /// <summary>
    /// Throws the selected item into the world: removes ONE from its slot (the rest of a stack stays
    /// selected) and spawns it as a tumbling physics object. Returns the new world object.
    /// </summary>
    public DroppedItem Throw()
    {
        ItemData item = Equipped;
        if (item == null || playerCamera == null) return null;
        Transform eye = playerCamera.transform;

        // Start where the item is on screen (seamless), or just in front of the camera.
        Vector3 position = eye.position + eye.forward * 0.6f - eye.up * 0.25f + eye.right * 0.15f;
        Quaternion rotation = eye.rotation;
        HeldView held = Find(item);
        Transform from = held != null ? held.throwFrom : itemView != null ? itemView.CurrentModel : null;
        if (from != null && from.gameObject.activeInHierarchy)
        {
            position = presentation != null ? presentation.ToPresented(from.position) : from.position;
            rotation = from.rotation;
        }

        // Never inside a wall: stop short of anything between the eyes and the spawn point.
        Vector3 toSpawn = position - eye.position;
        if (Physics.SphereCast(eye.position, 0.1f, toSpawn.normalized, out RaycastHit hit, toSpawn.magnitude,
                               blockingLayers, QueryTriggerInteraction.Ignore))
            position = eye.position + toSpawn.normalized * Mathf.Max(0f, hit.distance - 0.05f);

        inventory.RemoveFromSlot(EquippedSlotIndex, 1); // the view updates itself (empty slot = empty hands)
        DroppedItem dropped = ItemDrops.Spawn(item, 1, position, rotation, ThrowVelocity(), ThrowSpin());
        if (dropped != null) GetComponent<ItemPickupInteractor>()?.PlayDropSound();
        return dropped;
    }

    /// <summary>Throws the carried world ore with the same throw as an inventory item.</summary>
    public DroppedItem ThrowCarried() => carrier != null ? carrier.ThrowLast(ThrowVelocity(), ThrowSpin()) : null;

    private Vector3 ThrowVelocity()
    {
        Transform eye = playerCamera.transform;
        Vector3 velocity = eye.forward * throwForce + Vector3.up * throwUpward;
        if (inheritPlayerVelocity && controller != null) velocity += controller.velocity;
        return velocity;
    }

    // Spin around the camera's right axis = end over end, head first.
    private Vector3 ThrowSpin() => playerCamera.transform.right * throwSpin + UnityEngine.Random.insideUnitSphere * randomTorque;

    /// <summary>Don't throw away or switch a tool in the middle of its strike (before the hit lands).</summary>
    private bool MidStrike() => ActiveController != null && ActiveController.IsBusy;

    private HeldView Find(ItemData item)
    {
        if (item == null) return null;
        foreach (HeldView v in views)
            if (v.item == item) return v;
        return null;
    }
}
