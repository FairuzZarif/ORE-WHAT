using UnityEngine;

/// <summary>
/// Plays one ground-surface recording on each shared gait footfall. Enclosed caves use
/// the same stone recording with a separate, smoothly faded echo and reverb path.
/// </summary>
[DefaultExecutionOrder(100)] // PlayerGait has updated after PlayerMovement.
[RequireComponent(typeof(CharacterController), typeof(PlayerGait))]
public class PlayerFootsteps : MonoBehaviour
{
    [Header("Footstep clips")]
    [SerializeField] private AudioClip[] grassClips = System.Array.Empty<AudioClip>();
    [Tooltip("Linear gains matching Grass Clips. Missing entries use 1.")]
    [SerializeField] private float[] grassClipGains = System.Array.Empty<float>();
    [Tooltip("Stone recordings. A two-clip set alternates left and right; larger sets play from a shuffle bag.")]
    [SerializeField] private AudioClip[] rockClips = System.Array.Empty<AudioClip>();
    [Tooltip("Linear gains matching Rock Clips. Missing entries use 1.")]
    [SerializeField] private float[] rockClipGains = System.Array.Empty<float>();

    [Header("Level and variation")]
    [Tooltip("Master gain for the entire footstep system, before clip leveling and cave effects.")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.15f;
    [SerializeField, Range(0f, 0.03f)] private float pitchVariation = 0.03f;
    [SerializeField, Range(0f, 1f)] private float levelVariationDb = 1f;

    [Header("Cave reflections")]
    [SerializeField, Range(0f, 1f)] private float caveEchoGain = 0.2f;
    [SerializeField, Range(10f, 5000f)] private float caveEchoDelayMs = 100f;
    [SerializeField, Range(0f, 1f)] private float caveEchoDecay = 0.2f;
    [SerializeField, Min(0.05f)] private float caveTransitionTime = 0.8f;
    [SerializeField, Min(1f)] private float caveCeilingCheckDistance = 25f;

    [Header("Ground probe")]
    [SerializeField, Min(0.1f)] private float groundProbeDistance = 0.75f;
    [SerializeField, Min(0f)] private float groundProbeHeight = 0.25f;

    private const int VoiceCount = 12; // >4 seconds between reuse at the fastest planned gait
    private const float MinimumFloorNormalY = 0.55f;

    private sealed class StepVoice
    {
        public AudioSource Dry;
        public AudioSource Wet;
        public AudioEchoFilter Echo;
        public float Gain;
    }

    private CharacterController controller;
    private PlayerGait gait;
    private CaveLayout caveLayout;
    private StepVoice[] voices;
    private readonly RaycastHit[] ceilingHits = new RaycastHit[8];
    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    private int[] grassBag = System.Array.Empty<int>();
    private int bagPosition;
    private int lastGrassIndex = -1;
    private int[] rockBag = System.Array.Empty<int>();
    private int rockBagPosition;
    private int lastRockIndex = -1;
    private int nextVoice;
    private float caveBlend;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        gait = GetComponent<PlayerGait>();
        caveLayout = FindAnyObjectByType<CaveLayout>();

        BuildGrassBag();
        BuildRockBag();
        voices = new StepVoice[VoiceCount];
        for (int i = 0; i < voices.Length; i++)
            voices[i] = CreateVoice(i);
    }

    private void Update()
    {
        bool insideCave = IsInsideEnclosedCave();
        caveBlend = Mathf.MoveTowards(caveBlend, insideCave ? 1f : 0f,
            Time.deltaTime / Mathf.Max(0.05f, caveTransitionTime));

        // Changing source volume also fades echoes that started on earlier footsteps.
        foreach (StepVoice voice in voices)
            voice.Wet.volume = voice.Gain * caveEchoGain * caveBlend;

        if (!gait.FootfallThisFrame) return;
        PlayStep(insideCave || IsStandingOnRock());
    }

    private StepVoice CreateVoice(int index)
    {
        AudioSource dry = CreateSource("Footstep Dry " + index);
        AudioSource wet = CreateSource("Footstep Cave Echo " + index);

        var echo = wet.gameObject.AddComponent<AudioEchoFilter>();
        echo.delay = caveEchoDelayMs;
        echo.decayRatio = caveEchoDecay;
        echo.dryMix = 0f; // dry impact comes only from the other source
        echo.wetMix = 1f;

        var reverb = wet.gameObject.AddComponent<AudioReverbFilter>();
        reverb.reverbPreset = AudioReverbPreset.Cave;

        return new StepVoice { Dry = dry, Wet = wet, Echo = echo };
    }

    private AudioSource CreateSource(string sourceName)
    {
        var host = new GameObject(sourceName);
        host.transform.SetParent(transform, false);
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f; // the local player's listening perspective
        source.dopplerLevel = 0f;
        source.bypassReverbZones = true;
        return source;
    }

    private void BuildGrassBag()
    {
        int count = 0;
        if (grassClips != null)
            foreach (AudioClip clip in grassClips)
                if (clip != null) count++;

        grassBag = new int[count];
        int write = 0;
        if (grassClips != null)
            for (int i = 0; i < grassClips.Length; i++)
                if (grassClips[i] != null) grassBag[write++] = i;
        bagPosition = grassBag.Length;
    }

    private int PickGrassIndex()
    {
        if (grassBag.Length == 0) return -1;
        if (bagPosition >= grassBag.Length) ShuffleGrassBag();
        int index = grassBag[bagPosition++];
        lastGrassIndex = index;
        return index;
    }

    private void ShuffleGrassBag()
    {
        for (int i = grassBag.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (grassBag[i], grassBag[j]) = (grassBag[j], grassBag[i]);
        }

        if (grassBag.Length > 1 && grassBag[0] == lastGrassIndex)
        {
            int swap = Random.Range(1, grassBag.Length);
            (grassBag[0], grassBag[swap]) = (grassBag[swap], grassBag[0]);
        }
        bagPosition = 0;
    }

    private int PickRockIndex()
    {
        if (rockBag.Length == 0) return -1;

        // The original left/right recordings are a deliberate pair. Larger
        // banks need every recording to be heard without repeating a clip.
        if (rockClips.Length == 2 && rockBag.Length == 2)
            return gait.LastFootWasLeft ? 0 : 1;

        if (rockBagPosition >= rockBag.Length) ShuffleRockBag();
        int index = rockBag[rockBagPosition++];
        lastRockIndex = index;
        return index;
    }

    private void BuildRockBag()
    {
        int count = 0;
        if (rockClips != null)
            foreach (AudioClip clip in rockClips)
                if (clip != null) count++;

        rockBag = new int[count];
        int write = 0;
        if (rockClips != null)
            for (int i = 0; i < rockClips.Length; i++)
                if (rockClips[i] != null) rockBag[write++] = i;
        rockBagPosition = rockBag.Length;
    }

    private void ShuffleRockBag()
    {
        for (int i = rockBag.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (rockBag[i], rockBag[j]) = (rockBag[j], rockBag[i]);
        }

        if (rockBag.Length > 1 && rockBag[0] == lastRockIndex)
        {
            int swap = Random.Range(1, rockBag.Length);
            (rockBag[0], rockBag[swap]) = (rockBag[swap], rockBag[0]);
        }
        rockBagPosition = 0;
    }

    private void PlayStep(bool onRock)
    {
        int index = onRock ? PickRockIndex() : PickGrassIndex();
        if (index < 0) return;

        AudioClip clip = onRock ? rockClips[index] : grassClips[index];
        float clipGain = GetClipGain(onRock ? rockClipGains : grassClipGains, index);
        float gainJitter = Mathf.Pow(10f, Random.Range(-levelVariationDb, levelVariationDb) / 20f);
        float gain = volume * clipGain * gainJitter;
        float pitch = Random.Range(1f - pitchVariation, 1f + pitchVariation);

        StepVoice voice = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        voice.Dry.Stop();
        voice.Wet.Stop();
        voice.Gain = gain;
        voice.Dry.pitch = pitch;
        voice.Wet.pitch = pitch;
        voice.Echo.delay = caveEchoDelayMs * Random.Range(0.92f, 1.08f);
        voice.Dry.volume = gain;
        voice.Wet.volume = gain * caveEchoGain * caveBlend;
        voice.Dry.PlayOneShot(clip);

        // No grass echo on the way out of a cave; existing stone echo tails still fade.
        if (onRock && caveBlend > 0.001f)
            voice.Wet.PlayOneShot(clip);
    }

    private static float GetClipGain(float[] gains, int index)
        => gains != null && index < gains.Length ? Mathf.Max(0f, gains[index]) : 1f;

    private bool IsInsideEnclosedCave()
    {
        if (caveLayout == null) return false;

        Vector3 samplePoint = transform.position + Vector3.up * (controller.height * 0.8f);
        if (!caveLayout.Sample(samplePoint, out float distance, out _, out _) || distance >= -0.1f)
            return false;

        // CaveLayout also covers exterior rock and open ledges. Require the generated roof.
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
        int hitCount = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits,
            groundProbeDistance, worldMask, QueryTriggerInteraction.Ignore);

        Collider floor = null;
        float nearest = float.MaxValue;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == null || hit.normal.y < MinimumFloorNormalY ||
                hit.collider.transform.IsChildOf(transform) ||
                (hit.rigidbody != null && !hit.rigidbody.isKinematic) ||
                hit.distance >= nearest)
                continue;

            floor = hit.collider;
            nearest = hit.distance;
        }

        // The island ground uses TerrainColliders; generated stone and other solid
        // walkable surfaces use colliders. Moving pickups never choose a surface.
        return floor != null && !(floor is TerrainCollider);
    }
}
