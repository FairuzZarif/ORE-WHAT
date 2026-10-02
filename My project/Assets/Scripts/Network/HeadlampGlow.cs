using UnityEngine;

/// <summary>
/// The visible glow of another player's headlamp: a soft additive sprite at the lens that faces the viewer,
/// shown while the lamp is on. The lamp's Spot Light lights the world; this makes the lamp itself readable at a
/// glance (in daylight a spot light alone is hard to see). Put it on a child of the headlamp Light (it follows
/// the lens); used on the NetworkPlayer prefab only (you never see your own lamp from the front).
/// </summary>
[DefaultExecutionOrder(96)] // after Headlamp (95) has moved the light to the lens
[RequireComponent(typeof(MeshRenderer))]
public class HeadlampGlow : MonoBehaviour
{
    [SerializeField] private Light lamp;
    [SerializeField] private Color color = new Color(1f, 0.9f, 0.7f, 1f);
    [Tooltip("Size of the glow (metres).")]
    [SerializeField, Min(0.01f)] private float size = 0.5f;
    [Tooltip("Brighter when you look into the beam, dimmer from the side or behind.")]
    [SerializeField, Range(0f, 1f)] private float sideGlow = 0.25f;

    private MeshRenderer glow;
    private MaterialPropertyBlock block;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        glow = GetComponent<MeshRenderer>();
        block = new MaterialPropertyBlock();
        if (lamp == null) lamp = GetComponentInParent<Light>();
    }

    private void LateUpdate()
    {
        Camera viewer = Camera.main;
        bool on = lamp != null && lamp.enabled && lamp.gameObject.activeInHierarchy && viewer != null;
        glow.enabled = on;
        if (!on) return;

        Vector3 toViewer = viewer.transform.position - transform.position;
        transform.rotation = Quaternion.LookRotation(-toViewer, viewer.transform.up); // quad faces the viewer
        float facing = Mathf.Clamp01(Vector3.Dot(lamp.transform.forward, toViewer.normalized));
        float strength = Mathf.Lerp(sideGlow, 1f, facing * facing);
        transform.localScale = Vector3.one * size * Mathf.Lerp(0.6f, 1.4f, facing);

        glow.GetPropertyBlock(block);
        block.SetColor(BaseColorId, color * strength);
        glow.SetPropertyBlock(block);
    }
}
