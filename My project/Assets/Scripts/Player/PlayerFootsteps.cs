using UnityEngine;

/// <summary>
/// Plays grounded player footsteps, choosing grass, rock, or enclosed-cave sounds from the
/// ground collider and the cave space overhead.
/// </summary>
[DefaultExecutionOrder(100)] // read CharacterController.velocity after PlayerMovement has called Move
[RequireComponent(typeof(CharacterController))]
public class PlayerFootsteps : MonoBehaviour
{
    [Header("Footstep clips")]
    [Tooltip("Outside clips ordered left then right; they alternate on every exterior step.")]
    [SerializeField] private AudioClip[] outdoorClips = System.Array.Empty<AudioClip>();
    [Tooltip("Random stone-step variations. The same clip is not repeated on consecutive stone steps.")]
    [SerializeField] private AudioClip[] rockClips = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] caveClips = System.Array.Empty<AudioClip>();

    [Header("Timing")]
    [Tooltip("Ground distance travelled between walking or crouching footsteps, in metres.")]
    [SerializeField, Min(0.2f)] private float walkStepDistance = 1.2f;
    [Tooltip("Ground distance travelled between sprinting footsteps, in metres.")]
    [SerializeField, Min(0.2f)] private float sprintStepDistance = 1.65f;
    [Tooltip("Movement below this horizontal speed is treated as standing still.")]
    [SerializeField, Min(0f)] private float movementThreshold = 0.2f;

    [Header("Volume")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.3f;
    [SerializeField, Range(0f, 2f)] private float grassVolumeMultiplier = 1f;
    [SerializeField, Range(0f, 2f)] private float rockVolumeMultiplier = 1f;
    [Tooltip("Reduce this if the cave recordings overpower the outdoor sounds.")]
    [SerializeField, Range(0f, 2f)] private float caveVolumeMultiplier = 0.55f;
    [Tooltip("Per-clip cave volume adjustment, in the same order as Cave Clips. Missing entries use 1.")]
    [SerializeField] private float[] caveClipVolumeMultipliers = { 1f, 1f, 1f };

    [Header("Playback speed")]
    [Tooltip("AudioSource pitch also controls playback speed. 1 is the recorded speed.")]
    [SerializeField, Range(0.5f, 1.5f)] private float grassPlaybackSpeed = 1f;
    [SerializeField, Range(0.5f, 1.5f)] private float rockPlaybackSpeed = 1f;
    [SerializeField, Range(0.5f, 1.5f)] private float cavePlaybackSpeed = 1f;
    [Tooltip("Per-clip cave speed adjustment, in the same order as Cave Clips. Missing entries use 1.")]
    [SerializeField] private float[] caveClipSpeedMultipliers = { 1f, 1f, 1f };

    [Header("Surface detection and transition")]
    [Tooltip("How far below the player to probe for the surface collider.")]
    [SerializeField, Min(0.1f)] private float groundProbeDistance = 0.75f;
    [Tooltip("Distance above the feet where the ground probe begins.")]
    [SerializeField, Min(0f)] private float groundProbeHeight = 0.25f;
    [Tooltip("Maximum upward search for the cave's generated rock ceiling.")]
    [SerializeField, Min(1f)] private float caveCeilingCheckDistance = 25f;
    [Tooltip("Fade time between the exterior surface sound and cave sound, in seconds.")]
    [SerializeField, Min(0.05f)] private float caveTransitionTime = 0.8f;

    private CharacterController controller;
    private PlayerMovement movement;
    private CaveLayout caveLayout;
    private AudioSource grassSource;
    private AudioSource rockSource;
    private AudioSource[] caveSources;
    private readonly RaycastHit[] ceilingHits = new RaycastHit[8];
    private float distanceSinceStep;
    private float caveBlend;
    private int nextPairedClipIndex;
    private int lastCaveClipIndex = -1;
    private int lastRockClipIndex = -1;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        movement = GetComponent<PlayerMovement>();
        caveLayout = FindAnyObjectByType<CaveLayout>();

        grassSource = CreateSource("Grass Footsteps", grassPlaybackSpeed);
        rockSource = CreateSource("Rock Footsteps", rockPlaybackSpeed);
        caveSources = new AudioSource[caveClips != null ? caveClips.Length : 0];
        for (int i = 0; i < caveSources.Length; i++)
        {
            float speed = cavePlaybackSpeed * GetMultiplier(caveClipSpeedMultipliers, i);
            caveSources[i] = CreateSource("Cave Footsteps " + (i + 1), speed);
        }

        int pairedClipCount = Mathf.Max(outdoorClips != null ? outdoorClips.Length : 0,
                                        rockClips != null ? rockClips.Length : 0);
        nextPairedClipIndex = pairedClipCount > 1 ? Random.Range(0, 2) : 0;
    }

    private AudioSource CreateSource(string sourceName, float playbackSpeed)
    {
        var host = new GameObject(sourceName);
        host.transform.SetParent(transform, false);
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f; // footsteps belong to the local player's listening perspective
        source.dopplerLevel = 0f;
        source.pitch = Mathf.Max(0.1f, playbackSpeed);
        return source;
    }

    private void Update()
    {
        bool insideCave = IsInsideEnclosedCave();
        float targetBlend = insideCave ? 1f : 0f;
        caveBlend = Mathf.MoveTowards(caveBlend, targetBlend,
            Time.deltaTime / Mathf.Max(0.05f, caveTransitionTime));

        Vector3 velocity = controller.velocity;
        float horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
        if (!controller.isGrounded || horizontalSpeed < movementThreshold)
        {
            distanceSinceStep = 0f;
            return;
        }

        float stepLength = movement != null && movement.IsSprinting ? sprintStepDistance : walkStepDistance;
        distanceSinceStep += horizontalSpeed * Time.deltaTime;
        if (distanceSinceStep < stepLength) return;
        distanceSinceStep %= stepLength;

        bool onRock = IsStandingOnRock();
        PlayStep(onRock);
    }

    private bool IsInsideEnclosedCave()
    {
        if (caveLayout == null) return false;

        Vector3 samplePoint = transform.position + Vector3.up * (controller.height * 0.8f);
        if (!caveLayout.Sample(samplePoint, out float distance, out _, out _) || distance >= -0.1f)
            return false;

        // The CaveLayout also covers nearby exterior rock. Require the generated cave roof overhead
        // so standing on an outside ledge or platform cannot activate cave footsteps.
        int worldMask = ~(1 << gameObject.layer);
        int hitCount = Physics.RaycastNonAlloc(samplePoint, Vector3.up, ceilingHits,
            caveCeilingCheckDistance, worldMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
            if (ceilingHits[i].collider is MeshCollider &&
                ceilingHits[i].collider.gameObject.name.StartsWith("Mountain_", System.StringComparison.Ordinal))
                return true;
        return false;
    }

    private bool IsStandingOnRock()
    {
        Vector3 origin = transform.position + Vector3.up * groundProbeHeight;
        int worldMask = ~(1 << gameObject.layer);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundProbeDistance,
                             worldMask, QueryTriggerInteraction.Ignore))
            return false;

        // The island ground is a TerrainCollider. The generated stone ledges and cave mesh use
        // MeshColliders, so they get the rock set while ordinary terrain keeps the grass set.
        return !(hit.collider is TerrainCollider);
    }

    private void PlayStep(bool onRock)
    {
        float exteriorBlend = Mathf.Cos(caveBlend * Mathf.PI * 0.5f);
        float caveWeight = Mathf.Sin(caveBlend * Mathf.PI * 0.5f);

        AudioClip[] exteriorClips = onRock ? rockClips : outdoorClips;
        AudioSource exteriorSource = onRock ? rockSource : grassSource;
        float exteriorVolume = onRock ? rockVolumeMultiplier : grassVolumeMultiplier;
        AudioClip exteriorClip = onRock ? PickRockClip() : PickPairedClip(exteriorClips);
        if (exteriorClip != null && exteriorBlend > 0.001f)
            exteriorSource.PlayOneShot(exteriorClip, volume * exteriorVolume * exteriorBlend);

        if (caveWeight > 0.001f)
        {
            int caveIndex = PickCaveClipIndex();
            if (caveIndex >= 0)
            {
                float clipVolume = GetMultiplier(caveClipVolumeMultipliers, caveIndex);
                caveSources[caveIndex].PlayOneShot(caveClips[caveIndex],
                    volume * caveVolumeMultiplier * clipVolume * caveWeight);
            }
        }
    }

    private AudioClip PickPairedClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        int index = nextPairedClipIndex % clips.Length;
        nextPairedClipIndex = clips.Length > 1 ? 1 - nextPairedClipIndex : 0;
        return clips[index];
    }

    private AudioClip PickRockClip()
    {
        if (rockClips == null || rockClips.Length == 0) return null;

        int index = Random.Range(0, rockClips.Length);
        if (rockClips.Length > 1 && index == lastRockClipIndex)
            index = (index + Random.Range(1, rockClips.Length)) % rockClips.Length;
        lastRockClipIndex = index;
        return rockClips[index];
    }

    private int PickCaveClipIndex()
    {
        if (caveClips == null || caveClips.Length == 0) return -1;

        int index = Random.Range(0, caveClips.Length);
        if (caveClips.Length > 1 && index == lastCaveClipIndex)
            index = (index + Random.Range(1, caveClips.Length)) % caveClips.Length;
        lastCaveClipIndex = index;
        return index;
    }

    private static float GetMultiplier(float[] multipliers, int index)
        => multipliers != null && index < multipliers.Length ? Mathf.Max(0f, multipliers[index]) : 1f;
}
