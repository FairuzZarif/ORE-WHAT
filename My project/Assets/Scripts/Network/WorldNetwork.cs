using UnityEngine;

/// <summary>
/// Shared world actions. In single player <see cref="WorldNetwork.Current"/>
/// is null and every script works exactly as before. In a multiplayer session NetworkWorld (spawned by the
/// host) sets it, and the few shared actions are routed through the host instead:
///   rock hits, spawning items, picking items up and starting to carry them.
/// Owned inventory drops and sales additionally use the player's NetworkPlayerEconomy account.
/// </summary>
public interface IWorldNetwork
{
    /// <summary>A mining-capable local tool hit a rock. The host checks the equipped capability, applies it and synchronizes durability.</summary>
    void RequestRockHit(RockHealth rock, int damage);

    /// <summary>
    /// Puts an item into the shared world. On the host it spawns a networked item and returns it;
    /// on a client returns null. Owned inventory drops go through NetworkPlayerEconomy instead of minting items.
    /// </summary>
    DroppedItem SpawnItem(ItemData item, int amount, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity);

    /// <summary>Asks the host to give this item (up to maxAmount of it) to the local player; it can only be collected once.
    /// The host commits its inventory and sends the owner a complete account snapshot. False = invalid shared item.</summary>
    bool RequestPickup(DroppedItem item, int maxAmount);

    /// <summary>True if the local player may move this item's physics right now (it owns it).</summary>
    bool HasControl(DroppedItem item);

    /// <summary>Asks the host for control of the item, then starts carrying it. False = not a shared item.</summary>
    bool RequestCarry(DroppedItem item, OreCarryController carrier);
}

/// <summary>Where the current multiplayer session (if any) is found.</summary>
public static class WorldNetwork
{
    /// <summary>The running session's world, or null in single player.</summary>
    public static IWorldNetwork Current { get; set; }

    // Play Mode doesn't reload scripts in this project, so a static from the last session must not survive.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Current = null;
}
