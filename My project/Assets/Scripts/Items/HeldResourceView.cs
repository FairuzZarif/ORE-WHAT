using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The first-person view for items that have no tool view of their own (ores, crystals, and any
/// future resource): the same floating arms as the pickaxe, both hands clasping the item.
/// Put it on the root of that view (ItemHoldViewModel); PlayerEquipment drives it.
///
///   ShowItem(item)      an inventory item is selected: a visual-only copy of its model sits in the
///                       hands (no Rigidbody, no colliders, never a second world object)
///   ShowCarried(item)   a world ore is being carried: no copy at all; the real physics object is
///                       pulled to <see cref="Holder"/> by OreCarryController and the hands fit around it
///
/// The hands' grip points sit either side of the item, spread to its width, so the same view works
/// for any size. Models are built once per item and reused.
/// </summary>
public class HeldResourceView : MonoBehaviour
{
    [Tooltip("Where the held item sits (its centre). Also the carry point for carried world ores.")]
    [SerializeField] private Transform holder;
    [SerializeField] private Transform rightGrip;
    [SerializeField] private Transform leftGrip;
    [Tooltip("Gap between the item's side and each hand's grip (metres).")]
    [SerializeField, Min(0f)] private float gripClearance = 0.02f;
    [Tooltip("How far below the item's middle the hands hold it (metres).")]
    [SerializeField] private float gripDrop = 0f;
    [Tooltip("Items bigger than this (metres, largest side) are shown scaled down so they fit between the hands.")]
    [SerializeField, Min(0.05f)] private float maxModelSize = 0.2f;
    [Tooltip("Narrowest the hands get (metres from the centre), so they never clip into each other.")]
    [SerializeField, Min(0f)] private float minHalfWidth = 0.06f;

    private struct Model { public GameObject go; public Vector3 halfSize; }
    private readonly Dictionary<ItemData, Model> models = new Dictionary<ItemData, Model>();
    private GameObject current;

    public Transform Holder => holder;
    /// <summary>The visual copy being shown, or null (e.g. while carrying a real ore). Throws start from here.</summary>
    public Transform CurrentModel => current != null ? current.transform : null;

    /// <summary>Shows a visual-only copy of an inventory item between the hands.</summary>
    public void ShowItem(ItemData item)
    {
        HideModel();
        if (item == null) return;
        if (!models.TryGetValue(item, out Model m) || m.go == null) models[item] = m = BuildModel(item);
        current = m.go;
        current.SetActive(true);
        FitHands(m.halfSize);
    }

    /// <summary>Hands fit around a real, carried world object; nothing extra is shown.</summary>
    public void ShowCarried(DroppedItem item)
    {
        HideModel();
        Vector3 half = new Vector3(0.08f, 0.06f, 0.08f);
        if (item != null)
        {
            var b = new Bounds(item.transform.position, Vector3.zero);
            foreach (Renderer r in item.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            half = b.extents;
        }
        FitHands(half);
    }

    private void HideModel()
    {
        if (current != null) current.SetActive(false);
        current = null;
    }

    private void FitHands(Vector3 half)
    {
        float x = Mathf.Max(minHalfWidth, half.x) + gripClearance;
        if (rightGrip != null) rightGrip.localPosition = new Vector3(x, -gripDrop, 0f);
        if (leftGrip != null) leftGrip.localPosition = new Vector3(-x, -gripDrop, 0f);
    }

    /// <summary>A copy of the item's look with every physics part removed, centred on the holder.</summary>
    private Model BuildModel(ItemData item)
    {
        var shell = new GameObject(item.DisplayName + " (held)");
        shell.SetActive(false); // children stay asleep: no Awake on the copied DroppedItem/Rigidbody
        shell.transform.SetParent(holder, false);
        shell.layer = holder.gameObject.layer;

        GameObject source = item.HeldModel;
        GameObject visual = source != null ? Instantiate(source, shell.transform) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        if (source == null) { visual.transform.SetParent(shell.transform, false); visual.transform.localScale = Vector3.one * 0.15f; }
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;

        // Never a second physics object: strip everything that could collide or simulate.
        foreach (var d in visual.GetComponentsInChildren<DroppedItem>(true)) DestroyImmediate(d);
        foreach (var rb in visual.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
        foreach (var c in visual.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (Transform t in visual.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = holder.gameObject.layer;
        foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // Centre it on the holder and shrink it if it's too big for the hands.
        Bounds b = LocalBounds(shell.transform, visual);
        float largest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        float scale = largest > maxModelSize ? maxModelSize / largest : 1f;
        visual.transform.localScale *= scale;
        visual.transform.localPosition = -b.center * scale;
        return new Model { go = shell, halfSize = b.extents * scale };
    }

    /// <summary>Bounds of all meshes under <paramref name="root"/>, in <paramref name="space"/>'s local space (works while inactive).</summary>
    private static Bounds LocalBounds(Transform space, GameObject root)
    {
        bool any = false;
        var result = new Bounds();
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = space.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        return any ? result : new Bounds(Vector3.zero, Vector3.one * 0.15f);
    }
}
