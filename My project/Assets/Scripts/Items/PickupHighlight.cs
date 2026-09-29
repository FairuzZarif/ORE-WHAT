using UnityEngine;

/// <summary>
/// The outline of a world pickup (pistol, rifle, pickaxe, hammer... anything equippable).
/// Sits on the pickup prefab's root, next to DroppedItem. It only shows and fades the outline;
/// PickupHighlighter (on the Player) decides when, and sets the colour, brightness and width.
///
/// The outline is a child renderer ("PickupOutline") drawing a copy of the item's shape with the
/// Ore What/Pickup Outline shader: a thin glowing ring around the silhouette. The item's own
/// materials are never changed. Ore What > Add Pickup Highlights builds it for every
/// Tool/Weapon world prefab.
/// </summary>
[DisallowMultipleComponent]
public class PickupHighlight : MonoBehaviour
{
    [Tooltip("The outline renderer (child 'PickupOutline'): material 0 = stencil mask, material 1 = the outline ring.")]
    [SerializeField] private Renderer outlineRenderer;

    private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
    private MaterialPropertyBlock block;
    private float amount, target;
    private Color color; private float width;

    public bool HasOutline => outlineRenderer != null;

    private void Awake()
    {
        if (outlineRenderer != null) outlineRenderer.enabled = false; // hidden until PickupHighlighter asks
    }

    /// <summary>How strongly to show the outline (0 = hidden, 1 = full). It fades there smoothly.</summary>
    public void SetTarget(float strength, Color outlineColor, float outlineWidth)
    {
        target = Mathf.Clamp01(strength);
        color = outlineColor;
        width = outlineWidth;
    }

    /// <summary>Called by PickupHighlighter every frame while this pickup is (or was just) highlighted.</summary>
    public void Tick(float fadeSpeed, float dt)
    {
        if (outlineRenderer == null) return;
        amount = Mathf.MoveTowards(amount, target, fadeSpeed * dt);
        bool show = amount > 0.001f;
        if (outlineRenderer.enabled != show) outlineRenderer.enabled = show;
        if (!show) return;
        block ??= new MaterialPropertyBlock();
        Color c = color; c.a *= amount;
        block.SetColor(ColorId, c);
        block.SetFloat(WidthId, width);
        outlineRenderer.SetPropertyBlock(block, 1); // only the ring; the mask (material 0) keeps width 0
    }

    public bool IsVisible => amount > 0.001f;

    /// <summary>Hide at once (e.g. being picked up).</summary>
    public void HideNow()
    {
        amount = target = 0f;
        if (outlineRenderer != null) outlineRenderer.enabled = false;
    }
}
