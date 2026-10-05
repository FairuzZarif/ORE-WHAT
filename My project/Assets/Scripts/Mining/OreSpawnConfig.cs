using UnityEngine;

/// <summary>Population rules only; the referenced ItemData assets own prices and drops.</summary>
[CreateAssetMenu(menuName = "Ore What/Ore Spawn Configuration")]
public sealed class OreSpawnConfig : ScriptableObject
{
    [System.Serializable]
    public sealed class MineRule
    {
        public string name;
        [Min(0)] public int target;
        public float[] weights = new float[4]; // Copper, Iron, Gold, Crystal
        public int[] caps = new int[4];
        public float[] minimumDepth = new float[4];
    }
    public GameObject nodePrefab;
    public ItemData[] resources = new ItemData[4];
    public MineRule[] mines = new MineRule[4];
    public float[] respawnSeconds = { 90f, 150f, 360f, 600f };
    [Min(1f)] public float playerDistance = 12f;
    [Min(0.25f)] public float pollSeconds = 2f;
    [Min(0.5f)] public float nodeSpacing = 3f;
    [Range(0f, 1f)] public float commonClusterChance = 0.15f;
    [Min(1f)] public float clusterRadius = 9f;
    [Min(0f)] public float copperEarlyPreference = 1.5f;
    [Min(0f)] public float ironDeepPreference = 1f;
}
