using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Host-owned account on each existing player avatar. Uses PlayerInventory for the authoritative
/// seven slots and replicates one complete inventory/balance revision into the owner's existing scene inventory.
/// Pickups, drops and sales never accept client-reported holdings or sale values.
/// </summary>
[RequireComponent(typeof(PlayerInventory), typeof(PlayerCurrency))]
public sealed class NetworkPlayerEconomy : NetworkBehaviour
{
    public struct SlotState { public int item, amount; }
    public struct AccountState : INetworkSerializable, IEquatable<AccountState>
    {
        public int revision, money;
        public FixedList128Bytes<SlotState> slots;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref revision); s.SerializeValue(ref money);
            byte count = (byte)slots.Length; s.SerializeValue(ref count);
            if (s.IsReader) slots.Clear();
            for (int i = 0; i < count; i++)
            {
                SlotState slot = s.IsReader ? default : slots[i];
                s.SerializeValue(ref slot.item); s.SerializeValue(ref slot.amount);
                if (s.IsReader) slots.Add(slot);
            }
        }
        public bool Equals(AccountState other)
        {
            if (revision != other.revision || money != other.money || slots.Length != other.slots.Length) return false;
            for (int i = 0; i < slots.Length; i++) if (slots[i].item != other.slots[i].item || slots[i].amount != other.slots[i].amount) return false;
            return true;
        }
    }

    readonly NetworkVariable<AccountState> account = new NetworkVariable<AccountState>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public static NetworkPlayerEconomy Local { get; private set; }
    public int Money => account.Value.money;
    public int Revision => account.Value.revision;
    public PlayerInventory ServerInventory => IsServer ? inventory : null;
    public event Action<int, string> SaleCompleted;
    PlayerInventory inventory, localInventory;
    PlayerCurrency currency, localCurrency;
    NetworkPlayerAvatar avatar;
    bool transacting, needsPublish, starterGranted;
    int appliedRevision = -1, nextRequest, lastRequest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Local = null;
    void Awake()
    {
        inventory = GetComponent<PlayerInventory>(); currency = GetComponent<PlayerCurrency>(); avatar = GetComponent<NetworkPlayerAvatar>();
    }
    public override void OnNetworkSpawn()
    {
        account.OnValueChanged += OnAccountChanged;
        if (IsServer) { inventory.Changed += Publish; currency.Changed += Publish; Publish(); }
        if (IsOwner) Local = this;
    }
    public override void OnNetworkDespawn()
    {
        account.OnValueChanged -= OnAccountChanged;
        inventory.Changed -= Publish; currency.Changed -= Publish;
        if (Local == this) Local = null;
    }
    void Update()
    {
        if (!IsSpawned) return;
        if (IsServer && needsPublish && NetworkWorld.Instance != null) Publish();
        if (IsOwner && localInventory == null && avatar.LocalPlayer != null && NetworkWorld.Instance != null)
        {
            localInventory = avatar.LocalPlayer.GetComponent<PlayerInventory>();
            localCurrency = avatar.LocalPlayer.GetComponent<PlayerCurrency>(); Apply(account.Value);
        }
    }
    void Publish()
    {
        if (!IsServer || !IsSpawned || transacting) return;
        if (NetworkWorld.Instance == null) { needsPublish = true; return; }
        if (!starterGranted)
        {
            var equipment = FindAnyObjectByType<PlayerEquipment>();
            if (equipment == null) { needsPublish = true; return; }
            starterGranted = true;
            transacting = true;
            if (equipment.StartingItem != null && inventory.Count(equipment.StartingItem) == 0) inventory.AddItem(equipment.StartingItem, 1);
            transacting = false;
        }
        needsPublish = false;
        var state = new AccountState { revision = account.Value.revision + 1, money = currency.Money };
        foreach (var slot in inventory.Slots) state.slots.Add(new SlotState { item = slot.IsEmpty ? -1 : NetworkWorld.Instance.IndexOf(slot.item), amount = slot.IsEmpty ? 0 : slot.amount });
        account.Value = state;
    }
    void OnAccountChanged(AccountState before, AccountState after) { if (IsOwner) Apply(after); }
    void Apply(AccountState state)
    {
        if (localInventory == null && avatar.LocalPlayer != null)
        {
            localInventory = avatar.LocalPlayer.GetComponent<PlayerInventory>(); localCurrency = avatar.LocalPlayer.GetComponent<PlayerCurrency>();
        }
        if (localInventory == null || localCurrency == null || NetworkWorld.Instance == null || state.slots.Length != localInventory.SlotCount || state.revision < appliedRevision) return;
        var items = new ItemData[state.slots.Length]; var amounts = new int[items.Length];
        for (int i = 0; i < items.Length; i++) { items[i] = NetworkWorld.Instance.ItemAt(state.slots[i].item); amounts[i] = state.slots[i].amount; }
        appliedRevision = state.revision;
        localInventory.ApplyAuthoritativeSlots(items, amounts); localCurrency.SetAuthoritativeMoney(state.money);
    }

    internal int StorePickup(ItemData item, int requested)
    {
        if (!IsServer || item == null || requested <= 0) return 0;
        return inventory.AddItem(item, Mathf.Min(requested, inventory.SpaceFor(item)));
    }
    internal void ConfirmPickup(ItemData item, int amount)
    {
        if (IsServer) PickupCommittedRpc(account.Value, NetworkWorld.Instance.IndexOf(item), amount);
    }
    [Rpc(SendTo.Owner)] void PickupCommittedRpc(AccountState state, int item, int amount)
    {
        Apply(state);
        avatar.LocalPlayer?.GetComponent<ItemPickupInteractor>()?.ConfirmAuthoritativePickup(NetworkWorld.Instance.ItemAt(item), amount);
    }

    public bool RequestSale(CompanyWorkerNPC worker, ItemData only = null)
    {
        if (!IsSpawned || !IsOwner || worker == null || NetworkWorld.Instance == null) return false;
        int index = only == null ? -1 : NetworkWorld.Instance.IndexOf(only);
        if (only != null && index < 0) return false;
        SellRpc(++nextRequest, worker.ServiceId, index);
        return true;
    }
    [Rpc(SendTo.Server)] void SellRpc(int request, int service, int item, RpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId != OwnerClientId || request <= lastRequest) return;
        lastRequest = request;
        CompanyWorkerNPC worker = null;
        foreach (var npc in FindObjectsByType<CompanyWorkerNPC>()) if (npc.ServiceId == service) { worker = npc; break; }
        var health = GetComponent<NetworkPlayerHealth>();
        if (worker == null || (health != null && health.IsDead) || !worker.CanInteract(avatar.EyePosition, transform.position))
        { SaleResultRpc(account.Value, 0, "Come to the Company Office counter."); return; }
        ItemData only = item == -1 ? null : NetworkWorld.Instance.ItemAt(item);
        if (item < -1 || (item != -1 && (only == null || !only.CompanyOre)))
        { SaleResultRpc(account.Value, 0, "The company only buys mined ores."); return; }
        bool crystal = false;
        foreach (var slot in inventory.Slots) if (!slot.IsEmpty && slot.item.CompanyOre && (only == null || only == slot.item) && slot.item.ItemId == "crystal") crystal = true;
        transacting = true;
        bool sold = CompanySale.Commit(inventory, currency, only, out int earned);
        transacting = false;
        if (sold) Publish();
        SaleResultRpc(account.Value, earned, sold ? crystal ? "Now that's company property." : "Company appreciates your contribution." : "Come back when you've actually mined something.");
    }
    [Rpc(SendTo.Owner)] void SaleResultRpc(AccountState state, int earned, string message)
    {
        Apply(state); SaleCompleted?.Invoke(earned, message);
    }

    /// <summary>Q/G use the same authoritative slots as selling, so dropping can never leave saleable ghost ore.</summary>
    public DroppedItem RequestDrop(int slot, ItemData expected, int amount, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 spin)
    {
        if (!IsSpawned || !IsOwner || expected == null || NetworkWorld.Instance == null) return null;
        int index = NetworkWorld.Instance.IndexOf(expected);
        if (IsServer) return CommitDrop(slot, index, amount, position, rotation, velocity, spin);
        DropRpc(slot, index, amount, position, rotation, velocity, spin); return null;
    }
    [Rpc(SendTo.Server)] void DropRpc(int slot, int item, int amount, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 spin, RpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId == OwnerClientId) CommitDrop(slot, item, amount, position, rotation, velocity, spin);
    }
    DroppedItem CommitDrop(int slot, int item, int amount, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 spin)
    {
        if (!IsServer || slot < 0 || slot >= inventory.SlotCount || amount <= 0 || NetworkWorld.Instance == null) return null;
        var owned = inventory.Slots[slot];
        if (owned.IsEmpty || owned.item != NetworkWorld.Instance.ItemAt(item) || amount > owned.amount ||
            !(Vector3.Distance(position, avatar.EyePosition) <= 2.5f) || !(velocity.sqrMagnitude <= 1600f) || !(spin.sqrMagnitude <= 3600f)) return null;
        ItemData data = owned.item;
        inventory.RemoveFromSlot(slot, amount);
        var dropped = NetworkWorld.Instance.SpawnItem(data, amount, position, rotation, velocity, spin);
        DropCommittedRpc(account.Value); return dropped;
    }
    [Rpc(SendTo.Owner)] void DropCommittedRpc(AccountState state)
    {
        Apply(state); avatar.LocalPlayer?.GetComponent<ItemPickupInteractor>()?.PlayDropSound();
    }
}
