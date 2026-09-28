using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// What the player holds in their hands: one tool or weapon (the pickaxe, later swords,
/// drills...). Put it on the Player.
///
///   G   throw the held item: it leaves the hands where it is on screen and tumbles
///       away as a physical DroppedItem.
///   E   (ItemPickupInteractor) on a tool/weapon: it goes straight into the hands. If the
///       hands are full, the held one is dropped at your feet (a swap).
///
/// Each holdable item can have a "view": the first-person object shown while it's held
/// (for the pickaxe that's FirstPersonViewModel: the arms + pickaxe). Holding nothing
/// hides every view. MiningController only mines while the held item Can Mine.
/// </summary>
public class PlayerEquipment : MonoBehaviour
{
    /// <summary>Links a holdable item to what's shown in first person while holding it.</summary>
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

    [Header("Held item")]
    [Tooltip("What the player starts the game holding (can be empty).")]
    [SerializeField] private ItemData startingItem;
    [SerializeField] private HeldView[] views = new HeldView[0];
    [Tooltip("Auto-found if empty.")]
    [SerializeField] private Camera playerCamera;

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

    [Header("Swap")]
    [Tooltip("When picking up a tool with full hands, how hard the old one is dropped (m/s).")]
    [SerializeField, Min(0f)] private float swapDropForce = 1.5f;

    /// <summary>The item in the hands, or null.</summary>
    public ItemData Equipped { get; private set; }
    /// <summary>True while holding something that can mine.</summary>
    public bool CanMine => Equipped != null && Equipped.CanMine;
    /// <summary>Raised when the held item changes.</summary>
    public event Action Changed;

    private CharacterController controller;

    private void Awake()
    {
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>(true);
        controller = GetComponent<CharacterController>();
        Equip(startingItem);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || Cursor.lockState != CursorLockMode.Locked) return;
        if (Equipped != null && keyboard[throwKey].wasPressedThisFrame && !MidStrike())
            Throw();
    }

    /// <summary>Puts an item in the hands (or empties them with null) and shows its view.</summary>
    public void Equip(ItemData item)
    {
        Equipped = item;
        foreach (HeldView v in views)
            if (v.view != null) v.view.SetActive(false);
        HeldView current = Find(item);
        if (current != null && current.view != null) current.view.SetActive(true);
        Changed?.Invoke();
    }

    /// <summary>
    /// Called by ItemPickupInteractor when picking up a tool or weapon from the world.
    /// A held item is swapped out and dropped at the player's feet.
    /// </summary>
    public void PickUp(ItemData item)
    {
        if (item == null) return;
        if (Equipped != null) Release(swapDropForce, 0.5f, 0f, fromHands: false);
        Equip(item);
    }

    /// <summary>Throws the held item into the world. Returns the new world object.</summary>
    public DroppedItem Throw() => Release(throwForce, throwUpward, throwSpin, fromHands: true);

    private DroppedItem Release(float forward, float upward, float spin, bool fromHands)
    {
        ItemData item = Equipped;
        if (item == null || playerCamera == null) return null;
        Transform eye = playerCamera.transform;
        HeldView held = Find(item);

        // Start where the item is on screen (seamless), or just in front of the camera.
        Vector3 position = eye.position + eye.forward * 0.6f - eye.up * 0.25f + eye.right * 0.15f;
        Quaternion rotation = eye.rotation;
        if (fromHands && held != null && held.throwFrom != null && held.throwFrom.gameObject.activeInHierarchy)
        {
            position = held.throwFrom.position;
            rotation = held.throwFrom.rotation;
        }
        if (!fromHands) position = eye.position + eye.forward * 0.5f - eye.up * 0.6f;

        // Never inside a wall: stop short of anything between the eyes and the spawn point.
        Vector3 toSpawn = position - eye.position;
        if (Physics.SphereCast(eye.position, 0.1f, toSpawn.normalized, out RaycastHit hit, toSpawn.magnitude,
                               blockingLayers, QueryTriggerInteraction.Ignore))
            position = eye.position + toSpawn.normalized * Mathf.Max(0f, hit.distance - 0.05f);

        Vector3 velocity = eye.forward * forward + Vector3.up * upward;
        if (inheritPlayerVelocity && controller != null) velocity += controller.velocity;
        // Spin around the camera's right axis = end over end, head first.
        Vector3 angular = eye.right * spin + UnityEngine.Random.insideUnitSphere * randomTorque;

        Equip(null);
        return ItemDrops.Spawn(item, 1, position, rotation, velocity, angular);
    }

    /// <summary>Don't throw away a tool in the middle of its strike (before the hit lands).</summary>
    private bool MidStrike()
    {
        HeldView held = Find(Equipped);
        if (held == null || held.view == null) return false;
        PickaxeSwing swing = held.view.GetComponentInChildren<PickaxeSwing>();
        return swing != null && swing.IsSwinging && !swing.CanSwing;
    }

    private HeldView Find(ItemData item)
    {
        if (item == null) return null;
        foreach (HeldView v in views)
            if (v.item == item) return v;
        return null;
    }
}
