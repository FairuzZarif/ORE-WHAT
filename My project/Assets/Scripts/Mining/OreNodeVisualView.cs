using UnityEngine;

/// <summary>Three baked, surface-conforming crack meshes. No runtime meshes or network objects.</summary>
public sealed class OreNodeVisualView : MonoBehaviour
{
    public GameObject[] crackStages = new GameObject[3];
    public int Stage { get; private set; }

    public static int StageFor(int health, int maximum)
    {
        float remaining = maximum > 0 ? Mathf.Clamp01((float)health / maximum) : 0f;
        return health <= 0 ? 4 : remaining <= 0.25f ? 3 : remaining <= 0.5f ? 2 : remaining <= 0.75f ? 1 : 0;
    }

    public void SetHealth(int health, int maximum)
    {
        Stage = StageFor(health, maximum);
        for (int i = 0; i < crackStages.Length; i++)
            if (crackStages[i] != null) crackStages[i].SetActive(Stage == i + 1);
    }
}
