using System.Collections;
using UnityEngine;

/// <summary>
/// An item lying in the world as a physics object. Put it on the root of an item's world
/// prefab, next to a Rigidbody; the colliders can be on the root or on child parts.
///
/// It has no Update: Unity's physics moves it, and once it has settled the Rigidbody falls
/// asleep and costs (almost) nothing. The only code that runs is the short pickup animation.
/// Spawn and remove items through <see cref="ItemDrops"/>, so pooling can be added there later.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroppedItem : MonoBehaviour
{
    [Header("Item")]
    [SerializeField] private ItemData item;
    [SerializeField, Min(1)] private int amount = 1;

    [Header("Physics")]
    [Tooltip("Below this energy the Rigidbody goes to sleep (stops simulating). Higher = settles sooner. Unity's default is 0.005.")]
    [SerializeField, Min(0f)] private float sleepThreshold = 0.01f;
    [Tooltip("Top speed (m/s) at which overlapping items push apart, so pieces spawned touching separate gently instead of exploding.")]
    [SerializeField, Min(0.1f)] private float maxDepenetrationSpeed = 2f;
    [Tooltip("Fastest the item may spin (radians per second). Unity's default of 7 is too low for a thrown axe tumbling end over end.")]
    [SerializeField, Min(1f)] private float maxSpinSpeed = 25f;

    public ItemData Item => item;
    public int Amount => amount;
    public Rigidbody Body { get; private set; }
    /// <summary>True once a pickup has started (it's flying into the player's hands).</summary>
    public bool IsBeingPickedUp { get; private set; }
    /// <summary>True while a player is physically carrying it (OreCarryController). It stays a normal world object.</summary>
    public bool IsCarried { get; set; }
    /// <summary>"Copper Ore" or "Copper Ore x3".</summary>
    public string DisplayName => item == null ? name : amount > 1 ? $"{item.DisplayName} x{amount}" : item.DisplayName;

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Body.sleepThreshold = sleepThreshold;
        Body.maxDepenetrationVelocity = maxDepenetrationSpeed;
        Body.maxAngularVelocity = maxSpinSpeed;
    }

    /// <summary>Sets what this object is. Called by ItemDrops when it spawns the item.</summary>
    public void Init(ItemData data, int count)
    {
        item = data;
        amount = Mathf.Max(1, count);
    }

    /// <summary>After a partial pickup (inventory nearly full), the rest stays in the world.</summary>
    public void SetAmount(int count) => amount = Mathf.Max(1, count);

    /// <summary>
    /// Stops the physics and plays a quick "fly into the hands and shrink" animation,
    /// then removes the item from the world.
    /// </summary>
    public void Collect(Transform collector, float duration)
    {
        if (IsBeingPickedUp) return;
        IsBeingPickedUp = true;

        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;
        Body.interpolation = RigidbodyInterpolation.None; // we move the transform ourselves now
        Body.isKinematic = true;

        if (duration <= 0f || collector == null) ItemDrops.Despawn(this);
        else StartCoroutine(CollectRoutine(collector, duration));
    }

    private IEnumerator CollectRoutine(Transform collector, float duration)
    {
        Vector3 startPos = transform.position;
        Vector3 startScale = transform.localScale;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            float ease = k * k * (3f - 2f * k);
            // Just below the centre of the view, where the hands are.
            Vector3 target = collector.position + collector.forward * 0.35f - collector.up * 0.3f;
            transform.position = Vector3.Lerp(startPos, target, ease);
            transform.localScale = startScale * (1f - ease * 0.9f);
            yield return null;
        }
        ItemDrops.Despawn(this);
    }
}
