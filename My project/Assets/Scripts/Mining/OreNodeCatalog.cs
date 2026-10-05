using UnityEngine;

/// <summary>Resource identity selects a visual; drops and durability stay on RockHealth.</summary>
[CreateAssetMenu(menuName = "Ore What/Ore Node Catalog")]
public sealed class OreNodeCatalog : ScriptableObject
{
    [System.Serializable]
    public struct Entry { public ItemData ore; public GameObject visual; }
    public Entry[] entries;

    public GameObject VisualFor(ItemData ore)
    {
        if (entries != null)
            foreach (var entry in entries)
                if (entry.ore == ore) return entry.visual;
        return null;
    }
}
