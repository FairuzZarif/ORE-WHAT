using UnityEngine;

/// <summary>
/// Describes one kind of item (Copper Ore, Crystal, a weapon...). Create new items with
/// Assets > Create > Ore What > Item Data. Every system (drops, pickup, inventory) works
/// from this asset, so a new resource needs no new code: just an ItemData and a world prefab.
/// </summary>
[CreateAssetMenu(menuName = "Ore What/Item Data", fileName = "NewItem", order = 0)]
public class ItemData : ScriptableObject
{
    /// <summary>What kind of item this is. The category decides <see cref="Equippable"/>.</summary>
    public enum ItemCategory { Resource, Tool, Weapon, Equipment, Consumable, Artifact }

    [Header("Category")]
    [SerializeField] private ItemCategory category = ItemCategory.Resource;
    [Tooltip("Tools only: holding this lets the player mine rocks.")]
    [SerializeField] private bool canMine;

    [Header("Identity")]
    [Tooltip("Unique id used by code and save data, e.g. \"ore_copper\". Don't change it once items exist in saves.")]
    [SerializeField] private string itemId = "new_item";
    [Tooltip("Name shown to the player, e.g. \"Copper Ore\".")]
    [SerializeField] private string displayName = "New Item";
    [SerializeField, TextArea] private string description;
    [Tooltip("Optional. For inventory UI later.")]
    [SerializeField] private Sprite icon;

    [Header("World")]
    [Tooltip("What the item looks like lying in the world. Needs a Rigidbody, colliders and a DroppedItem " +
             "(the item setup tool builds these). If empty, a plain cube is used.")]
    [SerializeField] private GameObject worldPrefab;
    [Tooltip("Optional. Model shown in the first-person hands when this item is selected and has no tool view of its own " +
             "(ores etc.). Empty = the World Prefab's look is used (its physics parts are stripped).")]
    [SerializeField] private GameObject heldModel;

    [Header("Economy & stacking")]
    [Tooltip("Worth of ONE item, in $.")]
    [SerializeField, Min(0)] private int value = 10;
    [Tooltip("Can several share one inventory slot?")]
    [SerializeField] private bool stackable = true;
    [Tooltip("Most items one slot can hold (ignored if not stackable).")]
    [SerializeField, Min(1)] private int maxStackSize = 50;

    [Header("Feedback")]
    [Tooltip("Optional sound when this item is picked up. Empty = the pickup system's default sound.")]
    [SerializeField] private AudioClip pickupSound;

    public ItemCategory Category => category;
    /// <summary>
    /// Everything lives in the inventory (PlayerInventory). Equippable items (Tools and
    /// Weapons) can additionally be made the one item held in the hands (PlayerEquipment);
    /// Resources and the rest stay inventory-only.
    /// </summary>
    public bool Equippable => category == ItemCategory.Tool || category == ItemCategory.Weapon;
    /// <summary>Can be physically carried around as a world object (E) instead of being stored (F). Everything that isn't equippable.</summary>
    public bool Carryable => !Equippable;
    public bool CanMine => canMine;
    public string ItemId => itemId;
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public GameObject WorldPrefab => worldPrefab;
    /// <summary>What the hands hold when this item is selected (HeldModel, or the World Prefab's look).</summary>
    public GameObject HeldModel => heldModel != null ? heldModel : worldPrefab;
    public int Value => value;
    public bool Stackable => stackable;
    /// <summary>How many fit in one inventory slot.</summary>
    public int MaxStack => stackable ? Mathf.Max(1, maxStackSize) : 1;
    public AudioClip PickupSound => pickupSound;
}
