using UnityEngine;

/// <summary>Crossfades local ambience across the cave mouth, then keeps cave ambience inside the mountain.</summary>
public class CaveAmbience : MonoBehaviour
{
    [SerializeField] private CaveLayout layout;
    [SerializeField] private Transform entrance;
    [SerializeField] private CaveSpace mainTunnel;

    [Header("Looping recordings")]
    [SerializeField] private AudioClip forestClip;
    [SerializeField] private AudioClip caveClip;
    [SerializeField, Range(0f, 1f)] private float forestVolume = 0.22f;
    [SerializeField, Range(0f, 1f)] private float caveVolume = 0.45f;

    [Header("Transition")]
    [Tooltip("Distance on each side of the cave mouth over which the ambience crossfades.")]
    [SerializeField, Min(0.1f)] private float fadeHalfDistance = 10f;
    [SerializeField, Min(0.1f)] private float responseSpeed = 8f;

    private const float RoofCheckDistance = 30f;
    private readonly RaycastHit[] roofHits = new RaycastHit[8];
    private Transform player;
    private AudioSource forestSource;
    private AudioSource caveSource;
    private float caveBlend;

    // The builder calls this after recreating the generated entrance marker. Keep Inspector volume tweaks.
    public void Configure(Transform caveEntrance, CaveSpace tunnel, AudioClip forest, AudioClip cave)
    {
        if (layout == null) layout = GetComponent<CaveLayout>();
        entrance = caveEntrance;
        mainTunnel = tunnel;
        if (forestClip == null) forestClip = forest;
        if (caveClip == null) caveClip = cave;
    }

    private void Start()
    {
        if (layout == null) layout = GetComponent<CaveLayout>();
        var playerObject = GameObject.FindWithTag("Player");
        player = playerObject != null ? playerObject.transform : null;
        if (player == null || layout == null || entrance == null || mainTunnel == null || mainTunnel.End == null)
        {
            Debug.LogWarning("Cave ambience needs the player, cave layout, entrance marker and main tunnel.", this);
            enabled = false;
            return;
        }

        forestSource = CreateSource("Forest Ambience", forestClip);
        caveSource = CreateSource("Cave Ambience", caveClip);
        caveBlend = TargetCaveBlend();
        ApplyVolumes();
        if (forestClip != null) forestSource.Play();
        if (caveClip != null) caveSource.Play();
    }

    private void Update()
    {
        float target = TargetCaveBlend();
        caveBlend = Mathf.Lerp(caveBlend, target, 1f - Mathf.Exp(-responseSpeed * Time.deltaTime));
        ApplyVolumes();
    }

    private void OnDisable()
    {
        if (forestSource != null) forestSource.Stop();
        if (caveSource != null) caveSource.Stop();
    }

    private void OnEnable()
    {
        if (forestSource != null && forestClip != null) forestSource.Play();
        if (caveSource != null && caveClip != null) caveSource.Play();
    }

    private AudioSource CreateSource(string sourceName, AudioClip clip)
    {
        var host = new GameObject(sourceName);
        host.transform.SetParent(transform, false);
        var source = host.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.bypassReverbZones = true;
        source.volume = 0f;
        return source;
    }

    private float TargetCaveBlend()
    {
        Vector3 inward = mainTunnel.End.position - mainTunnel.transform.position;
        inward.y = 0f;
        inward.Normalize();
        Vector3 fromMouth = player.position - entrance.position;
        fromMouth.y = 0f;
        float along = Vector3.Dot(fromMouth, inward);
        float across = (fromMouth - inward * along).magnitude;

        // The mouth marker follows the generated roof line. Blend only along the playable entrance corridor.
        if (Mathf.Abs(along) <= fadeHalfDistance && across <= mainTunnel.Size.x)
        {
            float t = Mathf.InverseLerp(-fadeHalfDistance, fadeHalfDistance, along);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        // Elsewhere, enclosed cave air wins. The roof check prevents exterior ledges above the cave
        // and open terrain beside the entrance from playing cave ambience.
        Vector3 sample = player.position + Vector3.up * 1.5f;
        if (!layout.Sample(sample, out float distance, out _, out _) || distance >= -0.1f)
            return 0f;
        int mask = ~(1 << player.gameObject.layer);
        int count = Physics.RaycastNonAlloc(sample, Vector3.up, roofHits, RoofCheckDistance, mask,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (roofHits[i].collider is MeshCollider &&
                roofHits[i].collider.gameObject.name.StartsWith("Mountain_", System.StringComparison.Ordinal))
                return 1f;
        return 0f;
    }

    private void ApplyVolumes()
    {
        // Equal-power crossfade keeps the combined ambience from dipping at the entrance.
        float angle = caveBlend * Mathf.PI * 0.5f;
        forestSource.volume = forestVolume * Mathf.Cos(angle);
        caveSource.volume = caveVolume * Mathf.Sin(angle);
    }
}
