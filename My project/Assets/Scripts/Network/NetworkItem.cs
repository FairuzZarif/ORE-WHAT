using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Makes a world item (DroppedItem) shared in multiplayer. Sits on every item world prefab next to a
/// NetworkObject and a NetworkTransform (Owner authority); Ore What > Multiplayer > Set Up Multiplayer adds them.
///
/// Whoever OWNS the item simulates its physics and everyone else follows it: the host owns new items,
/// and a player who starts carrying one is given ownership (so carrying stays smooth and local).
/// In single player nothing here runs: the item is an ordinary physics object.
/// </summary>
[RequireComponent(typeof(DroppedItem))]
public class NetworkItem : NetworkBehaviour
{
    /// <summary>Which ItemData this is (index in NetworkWorld's item list, −1 = keep the prefab's own).</summary>
    public readonly NetworkVariable<int> ItemIndex = new NetworkVariable<int>(-1);
    public readonly NetworkVariable<int> Amount = new NetworkVariable<int>(1);
    /// <summary>True while its owner is carrying it (others can't grab it then).</summary>
    public readonly NetworkVariable<bool> Held = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone,
                                                                           NetworkVariableWritePermission.Owner);

    public DroppedItem Dropped { get; private set; }

    /// <summary>Called once this player has been given control (ownership) of the item.</summary>
    public System.Action GainedControl;

    private RigidbodyInterpolation interpolation;

    private void Awake()
    {
        Dropped = GetComponent<DroppedItem>();
        interpolation = Dropped.Body != null ? Dropped.Body.interpolation : RigidbodyInterpolation.Interpolate;
    }

    public override void OnNetworkSpawn()
    {
        if (ItemIndex.Value >= 0 && NetworkWorld.Instance != null)
        {
            ItemData data = NetworkWorld.Instance.ItemAt(ItemIndex.Value);
            if (data != null) Dropped.Init(data, Amount.Value);
        }
        else if (IsServer && NetworkWorld.Instance != null)
        {
            ItemIndex.Value = NetworkWorld.Instance.IndexOf(Dropped.Item); // an item placed in the scene
            Amount.Value = Dropped.Amount;
        }
        Amount.OnValueChanged += OnAmountChanged;
        Held.OnValueChanged += OnHeldChanged;
        ApplyAuthority();
        OnHeldChanged(false, Held.Value);
    }

    public override void OnNetworkDespawn()
    {
        Amount.OnValueChanged -= OnAmountChanged;
        Held.OnValueChanged -= OnHeldChanged;
    }

    protected override void OnOwnershipChanged(ulong previous, ulong current)
    {
        ApplyAuthority();
        if (IsOwner)
        {
            Dropped.IsCarried = false; // nobody here carries it yet (e.g. the host taking over from a player who left)
            GainedControl?.Invoke();
        }
        GainedControl = null;
    }

    private void OnAmountChanged(int oldValue, int newValue) => Dropped.SetAmount(newValue);

    // Someone else carrying it: the local look ray passes through it and it can't be grabbed.
    private void OnHeldChanged(bool oldValue, bool newValue)
    {
        if (!IsOwner) Dropped.IsCarried = newValue;
    }

    /// <summary>Only the owner simulates; everyone else is kinematic and follows the NetworkTransform.</summary>
    private void ApplyAuthority()
    {
        Rigidbody body = Dropped.Body;
        if (body == null) return;
        if (IsOwner)
        {
            body.isKinematic = false;
            body.interpolation = interpolation;
            body.WakeUp();
        }
        else
        {
            body.interpolation = RigidbodyInterpolation.None; // the NetworkTransform smooths it instead
            body.isKinematic = true;
        }
    }

    private void Update()
    {
        // The owner reports whether it is carrying the item (only sent when it changes).
        if (IsSpawned && IsOwner && Held.Value != Dropped.IsCarried) Held.Value = Dropped.IsCarried;
    }
}
