using UnityEngine;

/// <summary>Ordinary local presentation. Only authoritative RockHealth determines crack stage.</summary>
public sealed class OreNodeVisual : MonoBehaviour
{
    [SerializeField] private OreNodeCatalog catalog;
    [SerializeField] private ItemData configuredOre;
    [SerializeField] private OreNodeVisualView view;
    public int Stage => view != null ? view.Stage : 0;

    public void Configure(ItemData ore, bool force = false)
    {
        if (!force && view != null && configuredOre == ore) return;
        GameObject prefab = catalog != null ? catalog.VisualFor(ore) : null;
        if (prefab == null) return;
        if (view != null)
        {
            view.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(view.gameObject);
            else DestroyImmediate(view.gameObject);
        }
        GameObject visual;
#if UNITY_EDITOR
        if (!Application.isPlaying) visual = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
        else
#endif
            visual = Instantiate(prefab, transform);
        visual.name = "Visual";
        view = visual.GetComponent<OreNodeVisualView>();
        configuredOre = ore;
        SetHealth(1, 1);
    }

    public void SetHealth(int health, int maximum)
    {
        if (view != null) view.SetHealth(health, maximum);
    }
}
