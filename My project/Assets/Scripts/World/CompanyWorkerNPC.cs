using UnityEngine;

/// <summary>Static shared service, generated inside the existing Company Office booth.</summary>
[RequireComponent(typeof(CapsuleCollider))]
public sealed class CompanyWorkerNPC : MonoBehaviour
{
    [SerializeField] private int serviceId;
    [SerializeField] private Transform office;
    [SerializeField] private ItemData[] ores = new ItemData[0];
    public const float TalkDistance = 2.5f;
    public int ServiceId => serviceId;
    public ItemData[] Ores => ores;
    public Transform Office => office;
    public Vector3 TalkPoint => transform.position + Vector3.up * 1.35f;
    public void ConfigureOffice(Transform booth) => office = booth;

    public bool CanInteract(Vector3 eyes, Vector3 feet)
    {
        if (office == null) return false;
        Vector3 local = office.InverseTransformPoint(feet);
        // Customer recess inside the front posts; no talking from the hub or through side/back walls.
        if (Mathf.Abs(local.x) > 1.65f || local.z < -1.3f || local.z > 1.45f || Mathf.Abs(local.y) > 1.5f) return false;
        if (Vector3.Distance(eyes, GetComponent<CapsuleCollider>().ClosestPoint(eyes)) > TalkDistance) return false;
        Vector3 direction = TalkPoint - eyes;
        int mask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));
        return Physics.Raycast(eyes, direction.normalized, out RaycastHit hit, direction.magnitude + 0.05f, mask, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<CompanyWorkerNPC>() == this;
    }
}
