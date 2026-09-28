using UnityEngine;

/// <summary>
/// Lets the player kick loose items around by walking into them. Put it on the Player.
///
/// Dropped items don't physically collide with the player (their layers are set not to),
/// so small ore on the floor can never block, trip or lift the CharacterController.
/// Instead, once per physics step this checks a capsule around the player (a single small
/// overlap query) and nudges any items inside it along the player's movement.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerItemPusher : MonoBehaviour
{
    [Tooltip("Layers of items that can be pushed (the DroppedItem layer).")]
    [SerializeField] private LayerMask itemLayers;
    [Tooltip("How hard items get kicked, relative to the player's speed (1 = pushed at walking speed).")]
    [SerializeField, Min(0f)] private float pushStrength = 1.1f;
    [Tooltip("Small upward pop so kicked items tumble instead of sliding (m/s).")]
    [SerializeField, Min(0f)] private float liftSpeed = 0.6f;
    [Tooltip("Below this player speed (m/s) nothing is pushed.")]
    [SerializeField, Min(0f)] private float minPlayerSpeed = 0.3f;

    private CharacterController controller;
    private readonly Collider[] hits = new Collider[16];

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (itemLayers.value == 0 && ItemDrops.Layer >= 0) itemLayers = 1 << ItemDrops.Layer;
    }

    private void FixedUpdate()
    {
        Vector3 velocity = controller.velocity;
        Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
        float speed = flat.magnitude;
        if (speed < minPlayerSpeed) return;

        Vector3 center = transform.TransformPoint(controller.center);
        float radius = controller.radius + 0.05f;
        float half = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
        int count = Physics.OverlapCapsuleNonAlloc(center + Vector3.up * half, center - Vector3.up * half,
                                                   radius, hits, itemLayers, QueryTriggerInteraction.Ignore);

        Vector3 moveDir = flat / speed;
        for (int i = 0; i < count; i++)
        {
            Rigidbody body = hits[i].attachedRigidbody;
            if (body == null || body.isKinematic || AlreadyPushed(body, i)) continue;
            if (body.TryGetComponent(out DroppedItem dropped) && dropped.IsCarried) continue; // the one in your hands

            // Mostly along the player's movement, partly away from the player's centre.
            Vector3 away = body.position - center;
            away.y = 0f;
            Vector3 dir = (moveDir * 0.75f + away.normalized * 0.25f).normalized;

            float wanted = speed * pushStrength;
            float current = Vector3.Dot(body.linearVelocity, dir);
            if (current >= wanted) continue; // already moving away fast enough

            body.AddForce(dir * (wanted - current) + Vector3.up * liftSpeed, ForceMode.VelocityChange);
            body.AddTorque(Vector3.Cross(Vector3.up, dir) * wanted * 2f, ForceMode.VelocityChange);
        }
    }

    /// <summary>An item made of several colliders only gets one push.</summary>
    private bool AlreadyPushed(Rigidbody body, int index)
    {
        for (int j = 0; j < index; j++)
            if (hits[j].attachedRigidbody == body) return true;
        return false;
    }
}
