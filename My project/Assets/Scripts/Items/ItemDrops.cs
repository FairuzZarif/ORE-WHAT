using UnityEngine;

/// <summary>
/// The one place that creates and removes items in the world. Rocks, the player's drop key
/// and anything added later (chests, enemies) all call <see cref="Spawn"/>; pickups call
/// <see cref="Despawn"/>. To add object pooling later, only these two methods change.
/// </summary>
public static class ItemDrops
{
    /// <summary>Physics layer for items in the world (set up by Ore What > Add Item Pickup Setup).</summary>
    public const string LayerName = "DroppedItem";

    private static int layer = -2;

    /// <summary>The DroppedItem layer index, or -1 if the layer hasn't been created.</summary>
    public static int Layer
    {
        get
        {
            if (layer == -2) layer = LayerMask.NameToLayer(LayerName);
            return layer;
        }
    }

    /// <summary>
    /// Creates a physical item in the world and starts it moving.
    /// velocity is in m/s, angularVelocity in radians/s (both world space).
    /// </summary>
    public static DroppedItem Spawn(ItemData item, int amount, Vector3 position, Quaternion rotation,
                                    Vector3 velocity, Vector3 angularVelocity)
    {
        if (item == null || amount <= 0) return null;

        // Multiplayer: items are shared, so the host spawns them (a client gets null; the item appears a moment later).
        if (WorldNetwork.Current != null)
            return WorldNetwork.Current.SpawnItem(item, amount, position, rotation, velocity, angularVelocity);

        return SpawnLocal(item, amount, position, rotation, velocity, angularVelocity);
    }

    /// <summary>Creates the item in this game only (single player, or the host building a shared item).</summary>
    public static DroppedItem SpawnLocal(ItemData item, int amount, Vector3 position, Quaternion rotation,
                                         Vector3 velocity, Vector3 angularVelocity)
    {
        GameObject go = item.WorldPrefab != null
            ? Object.Instantiate(item.WorldPrefab, position, rotation)
            : CreateFallbackCube(position, rotation);
        go.name = item.DisplayName;
        if (Layer >= 0) SetLayer(go.transform, Layer);

        if (!go.TryGetComponent(out DroppedItem dropped))
            dropped = go.AddComponent<DroppedItem>(); // also adds a Rigidbody (RequireComponent)
        dropped.Init(item, amount);

        Rigidbody body = dropped.Body != null ? dropped.Body : go.GetComponent<Rigidbody>();
        body.linearVelocity = velocity;
        body.angularVelocity = angularVelocity;
        return dropped;
    }

    /// <summary>Removes an item from the world (a pool would take it back here).</summary>
    public static void Despawn(DroppedItem item)
    {
        if (item != null) Object.Destroy(item.gameObject);
    }

    private static GameObject CreateFallbackCube(Vector3 position, Quaternion rotation)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.localScale = Vector3.one * 0.18f;
        Rigidbody body = go.AddComponent<Rigidbody>();
        body.mass = 0.4f;
        body.angularDamping = 0.35f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        return go;
    }

    private static void SetLayer(Transform t, int newLayer)
    {
        t.gameObject.layer = newLayer;
        foreach (Transform child in t)
            SetLayer(child, newLayer);
    }
}
