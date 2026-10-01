using UnityEngine;

/// <summary>
/// Small idle motions for the start screen, all optional: a slow camera drift, a flickering
/// lantern light, and a gentle bob for a UI element (the title). Visual only.
/// </summary>
public class MenuAmbientMotion : MonoBehaviour
{
    public enum Kind { CameraDrift, LightFlicker, UIBob }

    [SerializeField] private Kind kind = Kind.CameraDrift;
    [Tooltip("Drift: metres / degrees. Flicker: share of the intensity. Bob: pixels.")]
    [SerializeField] private float amount = 0.15f;
    [SerializeField] private float speed = 0.2f;

    private Vector3 basePos;
    private Quaternion baseRot;
    private Light lightSource;
    private float baseIntensity;
    private RectTransform rect;
    private Vector2 baseAnchored;
    private float seed;

    private void Awake()
    {
        basePos = transform.localPosition;
        baseRot = transform.localRotation;
        lightSource = GetComponent<Light>();
        if (lightSource != null) baseIntensity = lightSource.intensity;
        rect = transform as RectTransform;
        if (rect != null) baseAnchored = rect.anchoredPosition;
        seed = Random.value * 50f;
    }

    private void Update()
    {
        float t = Time.unscaledTime * speed;
        switch (kind)
        {
            case Kind.CameraDrift:
                transform.localPosition = basePos + new Vector3(Mathf.Sin(t) * amount, Mathf.Sin(t * 0.7f + 1f) * amount * 0.4f, 0f);
                transform.localRotation = baseRot * Quaternion.Euler(Mathf.Sin(t * 0.8f) * amount * 2f, Mathf.Sin(t * 0.6f + 2f) * amount * 4f, 0f);
                break;
            case Kind.LightFlicker:
                if (lightSource != null)
                    lightSource.intensity = baseIntensity * (1f + (Mathf.PerlinNoise(seed, t) - 0.5f) * 2f * amount);
                break;
            case Kind.UIBob:
                if (rect != null) rect.anchoredPosition = baseAnchored + new Vector2(0f, Mathf.Sin(t * Mathf.PI * 2f) * amount);
                break;
        }
    }
}
