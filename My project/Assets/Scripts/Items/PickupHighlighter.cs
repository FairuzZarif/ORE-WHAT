using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Outlines equippable pickups (pistol, rifle, pickaxe, hammer... every Tool/Weapon) lying in the
/// world so they're easy to spot. Put it on the Player, next to ItemPickupInteractor; it reuses that
/// component's pickup range, pickup layer, blocking layers and look target, so there's no second
/// interaction system.
///
/// A pickup is outlined while it's
///   - within Highlight Range of the eyes (0 = the pickup range, so it lights up exactly when E can reach it),
///   - on screen, and
///   - in line of sight (not behind a rock or wall).
/// The one you're looking at (the pickup target) gets the full outline; others nearby a softer one.
/// Look away, walk away or pick it up and the outline fades out.
/// Pickups need a PickupHighlight component (Ore What > Add Pickup Highlights adds it).
/// </summary>
public class PickupHighlighter : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private ItemPickupInteractor pickup;
    [SerializeField] private Camera playerCamera;

    [Header("When")]
    [Tooltip("How far from the eyes pickups are outlined, metres. 0 = the pickup range (Item Pickup Interactor).")]
    [SerializeField, Min(0f)] private float highlightRange = 0f;
    [Tooltip("Only Tools and Weapons are outlined (not ores).")]
    [SerializeField] private bool onlyEquippable = true;
    [Tooltip("Outline strength for pickups in range that you're not looking straight at (the targeted one gets 1).")]
    [SerializeField, Range(0f, 1f)] private float nearbyStrength = 0.55f;

    [Header("Look")]
    [Tooltip("Outline colour. Warm yellow-orange stands out against grass, rock and dark caves.")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.72f, 0.18f, 1f);
    [Tooltip("Brightness. Above 1 glows a little (bloom); 1.5-2 is subtle.")]
    [SerializeField, Range(0.5f, 4f)] private float highlightIntensity = 1.6f;
    [Tooltip("Outline thickness in screen pixels.")]
    [SerializeField, Range(1f, 8f)] private float outlineWidth = 2.5f;
    [Tooltip("How fast the outline fades in and out (full fades per second).")]
    [SerializeField, Min(0.5f)] private float fadeSpeed = 8f;

    private readonly Collider[] overlaps = new Collider[32];
    private readonly HashSet<PickupHighlight> wanted = new HashSet<PickupHighlight>();
    private readonly List<PickupHighlight> active = new List<PickupHighlight>();

    /// <summary>The range actually used (Highlight Range, or the pickup range when that's 0).</summary>
    public float Range => highlightRange > 0f ? highlightRange : pickup != null ? pickup.PickupRange : 2.5f;

    private void Awake()
    {
        if (pickup == null) pickup = GetComponent<ItemPickupInteractor>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
    }

    private void OnDisable()
    {
        foreach (var h in active) if (h != null) h.HideNow();
        active.Clear();
    }

    private void LateUpdate()
    {
        if (pickup == null || playerCamera == null) return;
        wanted.Clear();
        Vector3 eye = playerCamera.transform.position;
        float range = Range;
        Color c = highlightColor * highlightIntensity; c.a = 1f;
        DroppedItem targeted = pickup.Target;

        int count = Physics.OverlapSphereNonAlloc(eye, range, overlaps, pickup.PickupLayer, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            DroppedItem item = overlaps[i].GetComponentInParent<DroppedItem>();
            if (item == null || item.Item == null || item.IsBeingPickedUp || item.IsCarried) continue;
            if (onlyEquippable && !item.Item.Equippable) continue;
            if (!item.TryGetComponent(out PickupHighlight h) || !h.HasOutline || wanted.Contains(h)) continue;
            if (!OnScreen(overlaps[i].bounds.center) || !InSight(eye, overlaps[i], item)) continue;

            wanted.Add(h);
            h.SetTarget(item == targeted ? 1f : nearbyStrength, c, outlineWidth);
            if (!active.Contains(h)) active.Add(h);
        }

        // Fade everything that's shown; fade out (and forget) what's no longer wanted.
        float dt = Time.deltaTime;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            PickupHighlight h = active[i];
            if (h == null) { active.RemoveAt(i); continue; } // picked up / destroyed
            DroppedItem item = h.GetComponent<DroppedItem>();
            if (item != null && item.IsBeingPickedUp) { h.HideNow(); active.RemoveAt(i); continue; }
            if (!wanted.Contains(h)) h.SetTarget(0f, c, outlineWidth);
            h.Tick(fadeSpeed, dt);
            if (!wanted.Contains(h) && !h.IsVisible) active.RemoveAt(i);
        }
    }

    private bool OnScreen(Vector3 point)
    {
        Vector3 v = playerCamera.WorldToViewportPoint(point);
        return v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
    }

    /// <summary>Nothing solid (rock, wall, terrain) between the eyes and the item.</summary>
    private bool InSight(Vector3 eye, Collider col, DroppedItem item)
    {
        Vector3 to = col.bounds.center - eye;
        float dist = to.magnitude;
        if (dist < 0.01f) return true;
        if (!Physics.Raycast(eye, to / dist, out RaycastHit hit, dist, pickup.BlockingLayers, QueryTriggerInteraction.Ignore)) return true;
        // Items lie on the ground: a ray that only grazes the terrain right next to it still counts as seen.
        return hit.collider.transform.IsChildOf(item.transform) || hit.distance >= dist - 0.25f;
    }
}
