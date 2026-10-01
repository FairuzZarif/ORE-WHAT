using UnityEngine;

/// <summary>
/// Makes the cave darker the deeper you go: fades the sun, ambient light, sky reflections and fog
/// toward cave values based on where the camera is in the CaveLayout (each CaveSpace has a Depth).
/// Lamps and glowing ore then do the lighting. Everything is restored when this is disabled.
/// Put it on the Cave (next to CaveLayout).
/// </summary>
[DefaultExecutionOrder(50)]
public class CaveAtmosphere : MonoBehaviour
{
    [SerializeField] private CaveLayout layout;
    [Tooltip("Empty = the main camera.")]
    [SerializeField] private Transform viewer;
    [Tooltip("Empty = RenderSettings.sun.")]
    [SerializeField] private Light sun;

    [Header("Inside the cave")]
    [Tooltip("Ambient light kept just inside the entrance (1 = like outside).")]
    [SerializeField, Range(0f, 1f)] private float ambientAtEntrance = 0.8f;
    [Tooltip("Ambient light kept at depth 1. Well above 0: the cave must stay navigable without a headlamp " +
             "(lamps and glowing ore add the contrast).")]
    [SerializeField, Range(0f, 1f)] private float ambientDeep = 0.5f;
    [Tooltip("Depth at which the sun is fully gone.")]
    [SerializeField, Range(0.01f, 1f)] private float sunGoneAtDepth = 0.2f;
    [SerializeField] private Color caveFogColor = new Color(0.1f, 0.09f, 0.09f);
    [SerializeField, Min(0f)] private float caveFogDensity = 0.008f;
    [SerializeField, Range(0f, 1f)] private float reflectionsDeep = 0.08f;
    [Tooltip("How quickly the lighting follows you, per second.")]
    [SerializeField, Min(0.1f)] private float blendSpeed = 2.5f;

    /// <summary>0 = outside, 1 = fully in the cave (smoothed).</summary>
    public float Inside { get; private set; }
    /// <summary>Smoothed cave depth at the viewer (0..1).</summary>
    public float Depth { get; private set; }

    private Color sky, equator, ground, flat;
    private float sunIntensity, fogDensity, reflection, ambientIntensity;
    private Color fogColor;
    private bool saved;

    private void OnEnable()
    {
        if (layout == null) layout = GetComponent<CaveLayout>();
        if (sun == null) sun = RenderSettings.sun;
        sky = RenderSettings.ambientSkyColor;
        equator = RenderSettings.ambientEquatorColor;
        ground = RenderSettings.ambientGroundColor;
        flat = RenderSettings.ambientLight;
        ambientIntensity = RenderSettings.ambientIntensity;
        fogColor = RenderSettings.fogColor;
        fogDensity = RenderSettings.fogDensity;
        reflection = RenderSettings.reflectionIntensity;
        sunIntensity = sun != null ? sun.intensity : 0f;
        saved = true;
        Inside = 0f;
        Depth = 0f;
    }

    private void OnDisable()
    {
        if (!saved) return;
        Apply(0f, 0f);
        saved = false;
    }

    private void LateUpdate()
    {
        if (layout == null) return;
        Transform v = viewer != null ? viewer : Camera.main != null ? Camera.main.transform : null;
        if (v == null) return;

        float targetInside = 0f, targetDepth = Depth;
        if (layout.Sample(v.position, out float distance, out float depth, out _))
        {
            // Fully inside half a metre into the cave air (the camera is only ~1.6 m above the floor), none 1 m into rock.
            targetInside = Mathf.Clamp01((1f - distance) / 1.5f);
            targetDepth = depth;
        }
        float k = 1f - Mathf.Exp(-blendSpeed * Time.deltaTime);
        Inside = Mathf.Lerp(Inside, targetInside, k);
        Depth = Mathf.Lerp(Depth, targetDepth, k);
        Apply(Inside, Depth);
    }

    private void Apply(float inside, float depth)
    {
        float ambient = Mathf.Lerp(1f, Mathf.Lerp(ambientAtEntrance, ambientDeep, Mathf.SmoothStep(0f, 1f, depth / 0.7f)), inside);
        RenderSettings.ambientSkyColor = sky * ambient;
        RenderSettings.ambientEquatorColor = equator * ambient;
        RenderSettings.ambientGroundColor = ground * ambient;
        RenderSettings.ambientLight = flat * ambient;
        RenderSettings.ambientIntensity = ambientIntensity * ambient;

        float fog = inside * Mathf.SmoothStep(0f, 1f, depth / 0.35f);
        RenderSettings.fogColor = Color.Lerp(fogColor, caveFogColor, Mathf.Max(fog, inside * 0.6f));
        RenderSettings.fogDensity = Mathf.Lerp(fogDensity, caveFogDensity, fog);
        RenderSettings.reflectionIntensity = Mathf.Lerp(reflection, reflectionsDeep, inside * Mathf.SmoothStep(0f, 1f, depth / 0.4f));
        if (sun != null) sun.intensity = sunIntensity * (1f - inside * Mathf.SmoothStep(0f, 1f, depth / sunGoneAtDepth));
    }
}
