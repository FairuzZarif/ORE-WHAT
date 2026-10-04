using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Everything you do to an item by looking at it. Put it on the Player. Each frame it casts one
/// thin ray from the centre of the camera (no scanning of the scene), so walls and rocks block
/// items behind them; an item you're carrying is looked through.
///
///   E  on an ore/resource   carry it physically (OreCarryController); it stays in the world
///   E  while carrying       release it
///   E  on a tool/weapon     put it in the inventory (and equip it if your hands are empty)
///   F  on any item          store it in the inventory (stacks; stays in the world if it won't fit)
///   F  while carrying       store the carried item
///
/// The prompt text is exposed for any UI to show (InventoryHUD draws it for now).
/// </summary>
public class ItemPickupInteractor : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerInventory inventory;
    [Tooltip("Optional. If the hands are empty, a picked-up tool/weapon is equipped through this.")]
    [SerializeField] private PlayerEquipment equipment;
    [Tooltip("Optional. Lets E physically carry ores instead of storing them.")]
    [SerializeField] private OreCarryController carrier;

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
    [Tooltip("Carry key: carries ores, picks up tools/weapons.")]
    [SerializeField] private Key pickupKey = Key.E;
    [Tooltip("Lets go of the carried item. Can be the same key as the carry key (press E again).")]
    [SerializeField] private Key releaseKey = Key.E;
    [Tooltip("Stores the item you look at (or carry) in the inventory.")]
    [SerializeField] private Key storeKey = Key.F;

    [Header("Feedback")]
    [Tooltip("How long the item takes to fly into the hands when picked up (0 = vanish instantly).")]
    [SerializeField, Min(0f)] private float collectDuration = 0.15f;
    [Tooltip("Played on pickup unless the item has its own sound. Empty = a small built-in pop.")]
    [SerializeField] private AudioClip pickupSound;
    [SerializeField, Range(0f, 1f)] private float pickupVolume = 0.5f;
    [Tooltip("Volume of the pickup sound played in reverse when dropping or throwing an item.")]
    [SerializeField, Range(0f, 1f)] private float dropVolume = 0.5f;
    [Tooltip("How long messages like \"Inventory Full\" stay on screen (seconds).")]
    [SerializeField, Min(0f)] private float messageDuration = 1.5f;

    /// <summary>The item being looked at (in range and not blocked), or null.</summary>
    public DroppedItem Target { get; private set; }
    /// <summary>
    /// What the keys would do right now, one action per line ("[E] Carry Copper Ore\n[F] Store Copper Ore"),
    /// or null when there's nothing to do.
    /// </summary>
    public string PromptText
    {
        get
        {
            if (carrier != null && carrier.IsCarrying)
            {
                string held = $"Carrying: {carrier.LastCarried.DisplayName}\n[{releaseKey}] Release   [{storeKey}] Store";
                return Target != null && Target.Item.Carryable && carrier.CanCarryMore
                    ? $"[{pickupKey}] Carry {Target.DisplayName}\n{held}" : held;
            }
            if (Target == null) return null;
            if (carrier != null && Target.Item.Carryable)
                return $"[{pickupKey}] Carry {Target.DisplayName}\n[{storeKey}] Store {Target.DisplayName}";
            return $"[{pickupKey}] Pick up {Target.DisplayName}";
        }
    }
    /// <summary>How far (from the eyes) an item can be picked up. Also used by PickupHighlighter.</summary>
    public float PickupRange => pickupRange;
    /// <summary>Layers that hold pickups.</summary>
    public LayerMask PickupLayer => pickupLayer;
    /// <summary>Layers that block the view of an item.</summary>
    public LayerMask BlockingLayers => blockingLayers;
    /// <summary>A short message such as "Inventory Full", or null.</summary>
    public string Message => Time.time < messageUntil ? message : null;

    /// <summary>Raised after items were added to the inventory: (item, amount).</summary>
    public event Action<ItemData, int> PickedUp;

    private AudioSource audioSource;
    private AudioClip dropSound;
    private string message;
    private float messageUntil;

    private void Awake()
    {
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
        if (carrier == null) carrier = GetComponent<OreCarryController>();
        if (carrier != null) carrier.DroppedBySnag += item => ShowMessage($"Dropped {item.DisplayName}");
        if (pickupLayer.value == 0 && ItemDrops.Layer >= 0) pickupLayer = 1 << ItemDrops.Layer;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        if (pickupSound == null)
        {
            pickupSound = CreatePopSound();
            dropSound = CreatePopSound(reversed: true);
        }
        else
            dropSound = CreateReversedSound(pickupSound) ?? CreatePopSound(reversed: true);
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
        if (keyboard == null) return;
        if (keyboard[storeKey].wasPressedThisFrame)
        {
            // Store what you're carrying first, otherwise what you're looking at.
            if (carrier != null && carrier.IsCarrying) TryPickup(carrier.LastCarried);
            else if (Target != null) TryPickup(Target);
        }
        else if (keyboard[pickupKey].wasPressedThisFrame || keyboard[releaseKey].wasPressedThisFrame)
            HandleCarryKeys(keyboard[pickupKey].wasPressedThisFrame, keyboard[releaseKey].wasPressedThisFrame);
    }

    /// <summary>E: carry an ore (if there's room), otherwise let go of the carried one; tools go to the inventory.</summary>
    private void HandleCarryKeys(bool carryPressed, bool releasePressed)
    {
        if (carrier == null) // no carrying on this player: E stores everything, as before
        {
            if (carryPressed && Target != null) TryPickup(Target);
            return;
        }
        bool lookingAtOre = Target != null && Target.Item.Carryable;

        if (carryPressed && lookingAtOre && carrier.CanCarryMore)
        {
            // Multiplayer: only the player who controls an item's physics can carry it; ask the host first.
            if (WorldNetwork.Current != null && !WorldNetwork.Current.HasControl(Target) && carrier.CannotCarryReason(Target) == null
                && WorldNetwork.Current.RequestCarry(Target, carrier))
            { PlaySound(Target.Item); Target = null; return; }
            if (carrier.TryCarry(Target)) { PlaySound(Target.Item); Target = null; }
            else ShowMessage(carrier.CannotCarryReason(Target));
            return;
        }
        if (releasePressed && carrier.IsCarrying) { carrier.ReleaseLast(); return; }
        if (carryPressed && Target != null && !Target.Item.Carryable) { TryPickup(Target); return; }
        if (carryPressed && lookingAtOre) ShowMessage(carrier.CannotCarryReason(Target)); // e.g. "Hands full"
    }

    private readonly RaycastHit[] lookHits = new RaycastHit[16];

    private DroppedItem FindTarget()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        int mask = pickupLayer | blockingLayers;
        int count = aimRadius > 0f
            ? Physics.SphereCastNonAlloc(ray, aimRadius, lookHits, pickupRange, mask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(ray, lookHits, pickupRange, mask, QueryTriggerInteraction.Ignore);

        // Only the first thing hit counts, so a wall or rock in front hides the item.
        // A carried item is looked through (it floats right in front of you).
        DroppedItem nearest = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            DroppedItem item = lookHits[i].collider.GetComponentInParent<DroppedItem>();
            if (item != null && item.IsCarried) continue;
            if (lookHits[i].distance >= nearestDistance) continue;
            nearestDistance = lookHits[i].distance;
            nearest = item; // null = a wall/rock is closest
        }
        return nearest != null && nearest.Item != null && !nearest.IsBeingPickedUp ? nearest : null;
    }

    /// <summary>
    /// Puts as much of the item into the inventory as fits (all item types, one inventory).
    /// An equippable item is also equipped, but only if the hands were empty: picking up never
    /// replaces what's held. Returns true if anything was picked up.
    /// </summary>
    public bool TryPickup(DroppedItem item)
    {
        if (item == null || item.IsBeingPickedUp || item.Item == null) return false;
        ItemData data = item.Item;

        // Multiplayer: the host decides who gets it (only once); GrantPickup runs when it says yes.
        if (WorldNetwork.Current != null)
        {
            if (inventory.SpaceFor(data) <= 0) { ShowMessage("Inventory Full"); return false; }
            if (carrier != null && item.IsCarried) carrier.Forget(item);
            if (WorldNetwork.Current.RequestPickup(item, inventory.SpaceFor(data))) { Target = null; return true; }
        }

        int added = inventory.AddItem(data, item.Amount, out int slot);
        if (added <= 0)
        {
            ShowMessage("Inventory Full"); // the item stays in the world (and in your hands if carried)
            return false;
        }

        if (added < item.Amount)
        {
            item.SetAmount(item.Amount - added); // the rest stays in the world (still carried if it was)
            ShowMessage("Inventory Full");
        }
        else
        {
            if (carrier != null && item.IsCarried) carrier.Forget(item); // only once it's safely in the inventory
            item.Collect(playerCamera.transform, collectDuration);
            Target = null;
        }

        if (equipment != null && data.Equippable && equipment.Equipped == null && slot >= 0)
            equipment.EquipSlot(slot);

        PlaySound(data);
        PickedUp?.Invoke(data, added);
        return true;
    }

    /// <summary>
    /// Multiplayer: the host gave this player an item it asked for (the world object is already gone).
    /// Adds it like a normal pickup; whatever doesn't fit is dropped back into the world.
    /// </summary>
    public void GrantPickup(ItemData data, int amount)
    {
        if (data == null || amount <= 0) return;
        int added = inventory.AddItem(data, amount, out int slot);
        if (added < amount)
        {
            ShowMessage("Inventory Full");
            Vector3 at = playerCamera != null ? playerCamera.transform.position + playerCamera.transform.forward * 0.8f : transform.position;
            ItemDrops.Spawn(data, amount - added, at, Quaternion.identity, Vector3.zero, Vector3.zero);
        }
        if (added <= 0) return;
        if (equipment != null && data.Equippable && equipment.Equipped == null && slot >= 0)
            equipment.EquipSlot(slot);
        PlaySound(data);
        PickedUp?.Invoke(data, added);
    }

    private void PlaySound(ItemData item)
    {
        AudioClip clip = item.PickupSound != null ? item.PickupSound : pickupSound;
        if (clip != null) audioSource.PlayOneShot(clip, pickupVolume);
    }

    /// <summary>Plays the default pickup cue backwards after an item is released or thrown.</summary>
    public void PlayDropSound()
    {
        if (dropSound != null) audioSource.PlayOneShot(dropSound, dropVolume);
    }

    public void ShowMessage(string text)
    {
        message = text;
        messageUntil = Time.time + messageDuration;
    }

    /// <summary>A short, soft rising "pop" so pickups make a sound without any audio files.</summary>
    private static AudioClip CreatePopSound(bool reversed = false)
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
        if (reversed) Array.Reverse(data);
        AudioClip clip = AudioClip.Create(reversed ? "DropPop" : "PickupPop", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip CreateReversedSound(AudioClip source)
    {
        float[] data = new float[source.samples * source.channels];
        if (!source.GetData(data, 0)) return null;
        for (int left = 0, right = source.samples - 1; left < right; left++, right--)
        {
            for (int channel = 0; channel < source.channels; channel++)
            {
                int a = left * source.channels + channel;
                int b = right * source.channels + channel;
                (data[a], data[b]) = (data[b], data[a]);
            }
        }
        AudioClip clip = AudioClip.Create(source.name + " Reversed", source.samples, source.channels, source.frequency, false);
        clip.SetData(data, 0);
        return clip;
    }
}
