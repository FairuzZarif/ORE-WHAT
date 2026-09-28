using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One inventory slot: an item type and how many of it.</summary>
[Serializable]
public class InventorySlot
{
    public ItemData item;
    public int amount;
    public bool IsEmpty => item == null || amount <= 0;
}

/// <summary>
/// The single inventory: every item the player carries, resources and equippable tools/weapons
/// alike (PlayerEquipment just points at one slot in here; it holds nothing of its own).
/// A fixed number of slots. Stackable items fill existing stacks first (Copper Ore x4 + 1 =
/// Copper Ore x5), then empty slots, up to each item's Max Stack. Non-stackable items (most
/// tools/weapons) always take a fresh slot, so two pickaxes sit in two separate slots.
/// Put it on the Player. Pickup, dropping, equipping and the HUD all use this component.
/// </summary>
[DefaultExecutionOrder(-5)] // other Items scripts read this in their own Awake; slots must exist first
public class PlayerInventory : MonoBehaviour
{
    [Tooltip("How many slots the player has.")]
    [SerializeField, Min(1)] private int slotCount = 7;
    [Tooltip("Current contents (visible for debugging; you can also pre-fill it here).")]
    [SerializeField] private List<InventorySlot> slots = new List<InventorySlot>();

    /// <summary>Raised whenever the contents or the selected slot change.</summary>
    public event Action Changed;

    public IReadOnlyList<InventorySlot> Slots => slots;
    public int SlotCount => slots.Count;
    /// <summary>The selected hotbar slot: what the hands hold (PlayerEquipment) and what Q/G act on. The one source of truth.</summary>
    public int SelectedSlot { get; private set; }

    /// <summary>Total $ value of everything carried.</summary>
    public int TotalValue
    {
        get
        {
            int total = 0;
            foreach (InventorySlot s in slots)
                if (!s.IsEmpty) total += s.item.Value * s.amount;
            return total;
        }
    }

    private void Awake() => EnsureSlots();
    private void OnValidate() => EnsureSlots();

    private void EnsureSlots()
    {
        while (slots.Count < slotCount) slots.Add(new InventorySlot());
        if (slots.Count > slotCount) slots.RemoveRange(slotCount, slots.Count - slotCount);
        SelectedSlot = Mathf.Clamp(SelectedSlot, 0, slotCount - 1);
    }

    /// <summary>How many of this item would fit right now.</summary>
    public int SpaceFor(ItemData item)
    {
        if (item == null) return 0;
        int space = 0;
        foreach (InventorySlot s in slots)
        {
            if (s.IsEmpty) space += item.MaxStack;
            else if (s.item == item) space += Mathf.Max(0, item.MaxStack - s.amount);
        }
        return space;
    }

    public bool CanAdd(ItemData item, int amount = 1) => SpaceFor(item) >= amount;

    /// <summary>
    /// Adds as many as fit (existing stacks first, then empty slots).
    /// Returns how many were actually added (0 = inventory full).
    /// </summary>
    public int AddItem(ItemData item, int amount) => AddItem(item, amount, out _);

    /// <summary>
    /// Same as <see cref="AddItem(ItemData, int)"/>, and also reports which slot the item
    /// ended up in (the last slot touched; for a non-stackable item that's its only slot,
    /// which is what PlayerEquipment/ItemPickupInteractor use to auto-equip a first pickup).
    /// -1 if nothing was added.
    /// </summary>
    public int AddItem(ItemData item, int amount, out int lastSlotUsed)
    {
        lastSlotUsed = -1;
        if (item == null || amount <= 0) return 0;
        int left = amount;

        for (int i = 0; i < slots.Count && left > 0; i++) // top up existing stacks
        {
            InventorySlot s = slots[i];
            if (s.IsEmpty || s.item != item) continue;
            int add = Mathf.Min(left, item.MaxStack - s.amount);
            if (add <= 0) continue;
            s.amount += add;
            left -= add;
            lastSlotUsed = i;
        }
        for (int i = 0; i < slots.Count && left > 0; i++) // then start new stacks
        {
            InventorySlot s = slots[i];
            if (!s.IsEmpty) continue;
            int add = Mathf.Min(left, item.MaxStack);
            s.item = item;
            s.amount = add;
            left -= add;
            lastSlotUsed = i;
        }

        int added = amount - left;
        if (added > 0) Changed?.Invoke();
        return added;
    }

    /// <summary>Takes up to `amount` out of a slot. Returns how many were removed.</summary>
    public int RemoveFromSlot(int slotIndex, int amount)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count || amount <= 0) return 0;
        InventorySlot s = slots[slotIndex];
        if (s.IsEmpty) return 0;

        int removed = Mathf.Min(amount, s.amount);
        s.amount -= removed;
        if (s.amount <= 0) { s.item = null; s.amount = 0; }
        Changed?.Invoke();
        return removed;
    }

    /// <summary>How many of this item the player carries in total.</summary>
    public int Count(ItemData item)
    {
        int n = 0;
        foreach (InventorySlot s in slots)
            if (!s.IsEmpty && s.item == item) n += s.amount;
        return n;
    }

    public void SelectSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= slots.Count || slotIndex == SelectedSlot) return;
        SelectedSlot = slotIndex;
        Changed?.Invoke();
    }
}
