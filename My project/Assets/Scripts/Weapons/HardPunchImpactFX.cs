using UnityEngine;

/// <summary>
/// Cosmetic hand injury at a confirmed fist contact. Eight reusable world-space voices,
/// no decals, damage, network objects, collision simulation or per-frame surface probes.
/// </summary>
public sealed class HardPunchImpactFX : MonoBehaviour
{
    const int VoiceCount = 8;
    public const float HearingDistance = 18f;
    static HardPunchImpactFX instance;
    readonly ParticleSystem[] bursts = new ParticleSystem[VoiceCount];
    readonly AudioSource[] voices = new AudioSource[VoiceCount];
    readonly AudioClip[] clips = new AudioClip[3];
    Material material;
    int nextVoice;

    public static int LocalImpacts { get; private set; }
    public static int RemoteImpacts { get; private set; }
    public static Vector3 LastOrigin { get; private set; }
    public static int LiveParticles
    {
        get
        {
            int count = 0;
            if (instance != null) foreach (var burst in instance.bursts) count += burst.particleCount;
            return count;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        LocalImpacts = RemoteImpacts = 0;
        LastOrigin = Vector3.zero;
    }

    /// <summary>Classification uses existing components, never scene or object names.</summary>
    public static bool IsHardSurface(Collider collider)
    {
        if (collider == null || collider.isTrigger) return false;
        if (collider.GetComponentInParent<NetworkPlayerAvatar>() != null ||
            collider.GetComponentInParent<CompanyWorkerNPC>() != null ||
            collider.GetComponentInParent<PlayerAttributes>() != null ||
            collider.GetComponentInParent<CharacterRagdoll>() != null) return false;
        return collider.GetComponentInParent<IDamageable>() == null ||
            collider.GetComponentInParent<HardPunchSurface>() != null;
    }

    public static void Play(Vector3 contact, Vector3 normal, int seed, bool remote = false)
    {
        if (instance == null) instance = new GameObject("Hard Punch Feedback (pooled)").AddComponent<HardPunchImpactFX>();
        normal = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
        // On the hand side of the surface: nothing is attached to the wall or left bleeding.
        Vector3 origin = contact + normal * 0.035f;
        LastOrigin = origin;
        if (remote) RemoteImpacts++; else LocalImpacts++;
        int index = instance.nextVoice++ % VoiceCount;
        var burst = instance.bursts[index];
        burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        burst.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(normal));
        burst.randomSeed = unchecked((uint)seed) | 1u;
        burst.Play();
        burst.Emit(6);
        var audio = instance.voices[index];
        audio.Stop();
        uint variation = unchecked((uint)seed);
        audio.pitch = 0.96f + (variation % 81) * 0.001f;
        audio.clip = instance.clips[variation % 3];
        audio.Play();
    }

    void Awake()
    {
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Hand blood (runtime)" };
        material.SetColor("_BaseColor", new Color(0.65f, 0.018f, 0.025f));
        for (int i = 0; i < clips.Length; i++) clips[i] = MakeThud(i);
        for (int i = 0; i < VoiceCount; i++)
        {
            var go = new GameObject("Hand impact voice " + i);
            go.transform.SetParent(transform, false);
            var burst = go.AddComponent<ParticleSystem>();
            burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = burst.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.023f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 1.1f);
            main.gravityModifier = 0.6f;
            main.maxParticles = 6;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = burst.emission; emission.enabled = false;
            var shape = burst.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 48f;
            shape.radius = 0.018f;
            var size = burst.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.8f), new Keyframe(1f, 0f)));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            bursts[i] = burst;
            var audio = go.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.dopplerLevel = 0f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 2.5f; // owner remains in the full-volume range
            audio.maxDistance = HearingDistance;
            audio.volume = 0.65f;
            voices[i] = audio;
        }
    }

    // Dedicated fleshy knock: low, falling body tone and a filtered contact tap.
    // No pickaxe ring, explosive bass tail or sharp firearm crack. Matches GunAudio's PCM convention.
    static AudioClip MakeThud(int variation)
    {
        const int rate = 22050;
        float[] samples = new float[(int)(rate * 0.17f)];
        var random = new System.Random(137 + variation * 53);
        float phase = 0f, noise = 0f, peak = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            float t = (float)i / rate;
            phase += 2f * Mathf.PI * (115f + variation * 7f + 45f * Mathf.Exp(-t * 55f)) / rate;
            noise = Mathf.Lerp(noise, (float)random.NextDouble() * 2f - 1f, 0.16f);
            float attack = Mathf.Clamp01(t / 0.0025f);
            float tail = Mathf.Clamp01((0.17f - t) / 0.025f);
            samples[i] = attack * tail * (Mathf.Sin(phase) * Mathf.Exp(-t * 29f) * 0.75f +
                noise * Mathf.Exp(-t * 75f) * 0.8f + Mathf.Sin(phase * 2.1f) * Mathf.Exp(-t * 65f) * 0.12f);
            peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        }
        for (int i = 0; i < samples.Length; i++) samples[i] *= 0.85f / Mathf.Max(0.001f, peak);
        var clip = AudioClip.Create("Fist hard thud " + (variation + 1), samples.Length, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        foreach (var clip in clips) if (clip != null) Destroy(clip);
        if (material != null) Destroy(material);
    }
}
