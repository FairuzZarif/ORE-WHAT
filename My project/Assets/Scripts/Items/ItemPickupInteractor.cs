using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Look at an item and press the pickup key (E) to put it in the inventory.
/// Put it on the Player. Each frame it casts one thin ray from the centre of the camera
/// (no scanning of the scene), so walls and rocks block items behind them.
/// The prompt text is exposed for any UI to show (InventoryHUD draws it for now).
/// </summary>
public class ItemPickupInteractor : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerInventory inventory;
    [Tooltip("Optional. Tools and weapons go into the hands through this instead of the inventory.")]
    [SerializeField] private PlayerEquipment equipment;

    [Header("Detection")]
    [Tooltip("How far away (metres, from the eyes) an item can be picked up.")]
    [SerializeField, Min(0.1f)] private float pickupRange = 2.5f;
    [Tooltip("Thickness of the look ray, so small items are easy to aim at (metres). 0 = exact centre only.")]
    [SerializeField, Min(0f)] private float aimRadius = 0.05f;
    [Tooltip("Layers that hold pickups (the DroppedItem layer).")]
    [SerializeField] private LayerMask pickupLayer;
    [Tooltip("Layers that block the view of an item (walls, rocks). Don't include the Player layer.")]
    [SerializeField] private LayerMask blockingLayers = 1; // Default

    [Header("Input")]
    [SerializeField] private Key pickupKey = Key.E;

    [Header("Feedback")]
    [Tooltip("How long the item takes to fly into the hands when picked up (0 = vanish instantly).")]
    [SerializeField, Min(0f)] private float collectDuration = 0.15f;
    [Tooltip("Played on pickup unless the item has its own sound. Empty = a small built-in pop.")]
    [SerializeField] private AudioClip pickupSound;
    [SerializeField, Range(0f, 1f)] private float pickupVolume = 0.5f;
    [Tooltip("How long messages like \"Inventory Full\" stay on screen (seconds).")]
    [SerializeField, Min(0f)] private float messageDuration = 1.5f;

    /// <summary>The item being looked at (in range and not blocked), or null.</summary>
    public DroppedItem Target { get; private set; }
    /// <summary>"[E] Pick up Copper Ore", or null when not looking at an item.</summary>
    public string PromptText
    {
        get
        {
            if (Target == null) return null;
            if (IsHoldable(Target) && equipment.Equipped != null)
                return $"[{pickupKey}] Swap {equipment.Equipped.DisplayName} for {Target.DisplayName}";
            return $"[{pickupKey}] Pick up {Target.DisplayName}";
        }
    }
    /// <summary>A short message such as "Inventory Full", or null.</summary>
    public string Message => Time.time < messageUntil ? message : null;

    /// <summary>Raised after items were added to the inventory: (item, amount).</summary>
    public event Action<ItemData, int> PickedUp;

    private AudioSource audioSource;
    private string message;
    private float messageUntil;

    private void Awake()
    {
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
        if (pickupLayer.value == 0 && ItemDrops.Layer >= 0) pickupLayer = 1 << ItemDrops.Layer;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        if (pickupSound == null) pickupSound = CreatePopSound();
    }

    private void Update()
    {
        // Same rule as mining: ignore input while the cursor is free (e.g. after Escape).
        if (playerCamera == null || inventory == null || Cursor.lockState != CursorLockMode.Locked)
        {
            Target = null;
            return;
        }

        Target = FindTarget();

        Keyboard keyboard = Keyboard.current;
        if (Target != null && keyboard != null && keyboard[pickupKey].wasPressedThisFrame)
            TryPickup(Target);
    }

    private DroppedItem FindTarget()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int mask = pickupLayer | blockingLayers;
        bool hit = aimRadius > 0f
            ? Physics.SphereCast(ray, aimRadius, out RaycastHit info, pickupRange, mask, QueryTriggerInteraction.Ignore)
            : Physics.Raycast(ray, out info, pickupRange, mask, QueryTriggerInteraction.Ignore);
        if (!hit) return null;

        // Only the first thing hit counts, so a wall or rock in front hides the item.
        DroppedItem item = info.collider.GetComponentInParent<DroppedItem>();
        return item != null && item.Item != null && !item.IsBeingPickedUp ? item : null;
    }

    /// <summary>Puts as much of the item into the inventory as fits. Returns true if anything was picked up.</summary>
    public bool TryPickup(DroppedItem item)
    {
        if (item == null || item.IsBeingPickedUp || item.Item == null) return false;

        // Tools and weapons go straight into the hands (swapping out whatever is held).
        if (IsHoldable(item))
        {
            equipment.PickUp(item.Item);
            item.Collect(playerCamera.transform, collectDuration);
            Target = null;
            PlaySound(item.Item);
            PickedUp?.Invoke(item.Item, 1);
            return true;
        }

        int added = inventory.AddItem(item.Item, item.Amount);
        if (added <= 0)
        {
            ShowMessage("Inventory Full"); // the item stays in the world
            return false;
        }

        if (added < item.Amount)
        {
            item.SetAmount(item.Amount - added); // the rest stays in the world
            ShowMessage("Inventory Full");
        }
        else
        {
            item.Collect(playerCamera.transform, collectDuration);
            Target = null;
        }

        PlaySound(item.Item);
        PickedUp?.Invoke(item.Item, added);
        return true;
    }

    private bool IsHoldable(DroppedItem item) => equipment != null && item.Item != null && item.Item.IsHoldable;

    private void PlaySound(ItemData item)
    {
        AudioClip clip = item.PickupSound != null ? item.PickupSound : pickupSound;
        if (clip != null) audioSource.PlayOneShot(clip, pickupVolume);
    }

    public void ShowMessage(string text)
    {
        message = text;
        messageUntil = Time.time + messageDuration;
    }

    /// <summary>A short, soft rising "pop" so pickups make a sound without any audio files.</summary>
    private static AudioClip CreatePopSound()
    {
        const int rate = 44100;
        const float length = 0.09f;
        int n = Mathf.CeilToInt(rate * length);
        var data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float freq = Mathf.Lerp(520f, 980f, t / length);
            phase += 2f * Mathf.PI * freq / rate;
            float envelope = Mathf.Min(1f, t / 0.005f) * Mathf.Exp(-t * 38f);
            data[i] = Mathf.Sin(phase) * envelope * 0.6f;
        }
        AudioClip clip = AudioClip.Create("PickupPop", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
