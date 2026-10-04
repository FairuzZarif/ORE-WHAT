using UnityEngine;

/// <summary>
/// Chips and sound when the pickaxe strikes something. Put it next to MiningController
/// (on the Player); it listens to MiningController.SurfaceHit, so it fires at exactly the
/// same moment as the damage.
///
/// Chips: small cube particles that fly out of the hit point along the surface normal,
/// tinted to the surface's colour, bounce on the ground and shrink away.
/// Sound: ore-named targets use Ore Impact Clips; other surfaces use Impact Clips or a
/// synthesised "tok" (low thump + gritty crack + a faint metallic ring from the pick).
/// </summary>
public class MiningImpactFX : MonoBehaviour
{
    [Header("Chips")]
    [Tooltip("Chips per hit on a rock.")]
    [SerializeField, Range(0, 40)] private int rockChips = 12;
    [Tooltip("Chips per hit on anything else (walls, floor).")]
    [SerializeField, Range(0, 40)] private int otherChips = 5;
    [Tooltip("Particle material. URP Particles/Lit or Lit works. Auto-created if empty.")]
    [SerializeField] private Material chipMaterial;
    [SerializeField] private Vector2 chipSize = new Vector2(0.015f, 0.045f);
    [SerializeField] private Vector2 chipSpeed = new Vector2(1.5f, 3.5f);
    [SerializeField] private Vector2 chipLifetime = new Vector2(0.5f, 0.9f);
    [Tooltip("Tint used when the hit surface's colour can't be read.")]
    [SerializeField] private Color fallbackColor = new Color(0.45f, 0.45f, 0.47f);

    [Header("Sound")]
    [Tooltip("Optional. One is picked at random per hit. Leave empty to use the built-in synthesised sound.")]
    [SerializeField] private AudioClip[] impactClips;
    [Tooltip("Used by default when the hit object, mesh, material or dropped item has Ore in its name.")]
    [SerializeField] private AudioClip[] oreImpactClips;
    [SerializeField, Range(0f, 1f)] private float volume = 0.7f;
    [Tooltip("Level of the recorded ore strikes, separate from the other impact sounds.")]
    [SerializeField, Range(0f, 1f)] private float oreVolume = 0.55f;
    [Tooltip("Random pitch range per hit, so repeated hits don't sound identical.")]
    [SerializeField] private Vector2 pitchRange = new Vector2(0.9f, 1.1f);

    [Header("References (auto-found if empty)")]
    [SerializeField] private MiningController miningController;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private ParticleSystem chips;
    private AudioSource audioSource;
    private AudioClip synthClip;
    private int lastOreClipIndex = -1;

    private void Awake()
    {
        if (miningController == null) miningController = GetComponent<MiningController>();
        BuildParticles();
        BuildAudio();
    }

    private void OnEnable() { if (miningController != null) miningController.SurfaceHit += OnSurfaceHit; }
    private void OnDisable() { if (miningController != null) miningController.SurfaceHit -= OnSurfaceHit; }

    private void OnSurfaceHit(RaycastHit hit, RockHealth rock)
    {
        // Chips burst out of the surface, tinted to match it.
        Transform fx = chips.transform;
        fx.SetPositionAndRotation(hit.point + hit.normal * 0.02f, Quaternion.LookRotation(hit.normal));
        ParticleSystem.MainModule main = chips.main;
        Color c = SurfaceColor(hit.collider);
        main.startColor = new ParticleSystem.MinMaxGradient(c * 0.8f, c * 1.15f);
        chips.Emit(rock != null ? rockChips : otherChips);

        // Sound at the hit point.
        AudioClip clip = IsOreHit(hit, rock) ? ChooseOreClip() : null;
        bool usingOreClip = clip != null;
        if (clip == null) clip = ChooseClip(impactClips);
        if (clip == null) clip = synthClip;
        if (clip == null) return;
        audioSource.transform.position = hit.point;
        audioSource.pitch = Random.Range(pitchRange.x, pitchRange.y);
        audioSource.PlayOneShot(clip, usingOreClip ? oreVolume : volume * (rock != null ? 1f : 0.7f));
    }

    private AudioClip ChooseOreClip()
    {
        if (oreImpactClips == null || oreImpactClips.Length == 0) return null;
        int index = Random.Range(0, oreImpactClips.Length);
        if (oreImpactClips.Length > 1 && index == lastOreClipIndex)
            index = (index + Random.Range(1, oreImpactClips.Length)) % oreImpactClips.Length;
        lastOreClipIndex = index;
        return oreImpactClips[index];
    }

    private static AudioClip ChooseClip(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        int start = Random.Range(0, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip clip = clips[(start + i) % clips.Length];
            if (clip != null) return clip;
        }
        return null;
    }

    private static bool IsOreHit(RaycastHit hit, RockHealth rock)
    {
        if (rock != null && (HasOreName(rock.name) ||
                             (rock.OreItem != null && HasOreName(rock.OreItem.name))))
            return true;

        Collider collider = hit.collider;
        if (collider == null) return false;
        if (HasOreName(collider.name) ||
            (collider.attachedRigidbody != null && HasOreName(collider.attachedRigidbody.name)))
            return true;

        var meshFilter = collider.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null && HasOreName(meshFilter.sharedMesh.name))
            return true;
        if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null &&
            HasOreName(meshCollider.sharedMesh.name))
            return true;

        Renderer renderer = collider.GetComponent<Renderer>();
        if (renderer == null) renderer = collider.GetComponentInParent<Renderer>();
        if (renderer != null)
            foreach (Material material in renderer.sharedMaterials)
                if (material != null && HasOreName(material.name)) return true;
        return false;
    }

    private static bool HasOreName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        for (int i = 0; i <= name.Length - 3; i++)
        {
            if (string.Compare(name, i, "ore", 0, 3, System.StringComparison.OrdinalIgnoreCase) != 0)
                continue;
            bool before = i == 0 || !char.IsLetterOrDigit(name[i - 1]) ||
                          (char.IsLower(name[i - 1]) && char.IsUpper(name[i]));
            bool after = i + 3 == name.Length || !char.IsLetterOrDigit(name[i + 3]) || char.IsUpper(name[i + 3]);
            if (before || after) return true; // Don't mistake names such as "Forest" for ore.
        }
        return false;
    }

    private Color SurfaceColor(Collider col)
    {
        Renderer r = col != null ? col.GetComponent<Renderer>() : null;
        Material m = r != null ? r.sharedMaterial : null;
        if (m == null) return fallbackColor;
        if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
        if (m.HasProperty(ColorId)) return m.GetColor(ColorId);
        return fallbackColor;
    }

    private void BuildParticles()
    {
        var go = new GameObject("MiningChipsFX");
        go.transform.SetParent(null); // world space, independent of the player
        chips = go.AddComponent<ParticleSystem>();
        chips.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = chips.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(chipLifetime.x, chipLifetime.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(chipSpeed.x, chipSpeed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(chipSize.x, chipSize.y);
        main.startRotation3D = true;
        main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 1.2f;
        main.maxParticles = 200;

        ParticleSystem.EmissionModule emission = chips.emission;
        emission.enabled = false; // we Emit() manually

        ParticleSystem.ShapeModule shape = chips.shape;
        shape.shapeType = ParticleSystemShapeType.Cone; // opens along the surface normal
        shape.angle = 40f;
        shape.radius = 0.03f;

        ParticleSystem.RotationOverLifetimeModule spin = chips.rotationOverLifetime;
        spin.enabled = true;
        spin.separateAxes = true;
        spin.x = spin.y = spin.z = new ParticleSystem.MinMaxCurve(-8f, 8f);

        ParticleSystem.SizeOverLifetimeModule size = chips.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 0f)));

        ParticleSystem.CollisionModule collision = chips.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.bounce = 0.3f;
        collision.dampen = 0.4f;
        collision.radiusScale = 0.5f;
        collision.quality = ParticleSystemCollisionQuality.Medium;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        renderer.alignment = ParticleSystemRenderSpace.Local;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (chipMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Lit");
            chipMaterial = new Material(shader) { name = "MiningChips (runtime)" };
        }
        renderer.sharedMaterial = chipMaterial;
    }

    private void BuildAudio()
    {
        var go = new GameObject("MiningImpactAudio");
        audioSource = go.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.6f; // mostly positional, but always clearly audible
        audioSource.minDistance = 1.5f;
        audioSource.maxDistance = 25f;

        if (impactClips == null || impactClips.Length == 0)
            synthClip = SynthesizeImpact();
    }

    /// <summary>Builds a short pickaxe-on-rock "tok": thump + crack + faint metallic ring.</summary>
    private static AudioClip SynthesizeImpact()
    {
        const int rate = 44100;
        const float length = 0.35f;
        int n = Mathf.CeilToInt(rate * length);
        var data = new float[n];
        var rng = new System.Random(1234);
        float lowNoise = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            lowNoise += (white - lowNoise) * 0.35f; // gritty, not hissy

            float thump = Mathf.Sin(2f * Mathf.PI * (95f + 60f * Mathf.Exp(-t * 40f)) * t) * Mathf.Exp(-t * 28f);
            float crack = lowNoise * Mathf.Exp(-t * 55f);
            float ring = (Mathf.Sin(2f * Mathf.PI * 1780f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 2630f * t) * 0.4f)
                       * Mathf.Exp(-t * 14f) * 0.18f;
            float attack = Mathf.Clamp01(t / 0.0015f); // avoid a click at the very start
            data[i] = (thump * 0.9f + crack * 0.8f + ring) * attack;
        }

        float peak = 0f;
        foreach (float s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
        if (peak > 0f) for (int i = 0; i < n; i++) data[i] *= 0.85f / peak;

        AudioClip clip = AudioClip.Create("MiningImpact (synth)", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void OnDestroy()
    {
        if (chips != null) Destroy(chips.gameObject);
        if (audioSource != null) Destroy(audioSource.gameObject);
    }
}
