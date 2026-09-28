using UnityEngine;

/// <summary>
/// Tiny, short camera shake. Put it on the player camera and call Shake().
///
/// PlayerLook sets the camera's rotation every frame, so this script never keeps
/// its own offset around: it removes last frame's shake early in the frame, then
/// adds a fresh one in LateUpdate after everything else has moved the camera.
/// </summary>
[DefaultExecutionOrder(-50)]
public class CameraShake : MonoBehaviour
{
    [Tooltip("Rotation (degrees) at full strength.")]
    [SerializeField] private float maxAngle = 1.2f;
    [Tooltip("Position jitter (metres) at full strength.")]
    [SerializeField] private float maxOffset = 0.006f;
    [Tooltip("How fast the shake wobbles.")]
    [SerializeField] private float frequency = 28f;
    [Tooltip("How quickly the shake dies out. Higher = shorter shake.")]
    [SerializeField, Min(0.1f)] private float decay = 7f;

    private float strength;
    private float seed;
    private Quaternion appliedRotation = Quaternion.identity;
    private Vector3 appliedOffset;

    private void Awake() => seed = Random.value * 100f;

    /// <summary>Adds shake. 0 = nothing, 1 = full strength.</summary>
    public void Shake(float amount) => strength = Mathf.Clamp01(Mathf.Max(strength, amount));

    private void Update()
    {
        // Undo last frame's shake before PlayerLook (and anything else) runs.
        transform.localRotation *= Quaternion.Inverse(appliedRotation);
        transform.localPosition -= appliedOffset;
        appliedRotation = Quaternion.identity;
        appliedOffset = Vector3.zero;
    }

    private void LateUpdate()
    {
        if (strength <= 0f) return;

        float time = Time.time * frequency;
        // Perlin noise in -1..1 gives a smooth wobble rather than random jitter.
        float nx = Mathf.PerlinNoise(seed, time) * 2f - 1f;
        float ny = Mathf.PerlinNoise(seed + 10f, time) * 2f - 1f;
        float nz = Mathf.PerlinNoise(seed + 20f, time) * 2f - 1f;

        appliedRotation = Quaternion.Euler(new Vector3(nx, ny, nz * 0.5f) * (maxAngle * strength));
        appliedOffset = new Vector3(ny, nx, 0f) * (maxOffset * strength);
        transform.localRotation *= appliedRotation;
        transform.localPosition += appliedOffset;

        strength = Mathf.MoveTowards(strength, 0f, decay * strength * Time.deltaTime + 0.5f * Time.deltaTime);
    }

    private void OnDisable()
    {
        transform.localRotation *= Quaternion.Inverse(appliedRotation);
        transform.localPosition -= appliedOffset;
        appliedRotation = Quaternion.identity;
        appliedOffset = Vector3.zero;
        strength = 0f;
    }
}
