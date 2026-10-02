using UnityEngine;

/// <summary>
/// Makes an object mineable. Put it on the rock's root object. The colliders can be on
/// the root or on child parts. Each mining hit removes health and flashes the rock.
/// At zero health the rock is destroyed and drops ore pieces.
/// </summary>
public class RockHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 5;

    [Header("Hit Flash")]
    [SerializeField] private Color flashColor = Color.white;
    [Tooltip("How long the rock stays flashed after a hit, in seconds.")]
    [SerializeField, Min(0f)] private float flashDuration = 0.1f;

    [Header("Ore Drop")]
    [SerializeField, Min(0)] private int oreCount = 3;
    [Tooltip("What each ore piece is: it spawns as a physical item the player can pick up (E). " +
             "If empty, the Ore Prefab or plain cubes are used as before.")]
    [SerializeField] private ItemData oreItem;
    [Tooltip("Optional extra drops, e.g. a 25% chance of a Crystal. Each piece is its own physics object.")]
    [SerializeField] private BonusDrop[] bonusDrops = new BonusDrop[0];
    [Tooltip("Random spin given to each dropped piece (radians per second).")]
    [SerializeField, Min(0f)] private float oreSpin = 6f;
    [Tooltip("Optional. If empty, small cubes are created instead.")]
    [SerializeField] private GameObject orePrefab;
    [Tooltip("Material for the auto-created cubes (ignored when an Ore Prefab is set).")]
    [SerializeField] private Material oreMaterial;
    [SerializeField, Min(0.01f)] private float oreSize = 0.2f;
    [Tooltip("Speed (m/s) ore pieces pop out of the rock at.")]
    [SerializeField, Min(0f)] private float orePopSpeed = 2.5f;

    private int currentHealth;
    private Renderer[] renderers;
    private Color[] originalColors;
    private MaterialPropertyBlock propertyBlock;
    private float flashTimer;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in Standard

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    private void Awake()
    {
        currentHealth = maxHealth;
        propertyBlock = new MaterialPropertyBlock();

        renderers = GetComponentsInChildren<Renderer>();
        originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Material mat = renderers[i].sharedMaterial;
            originalColors[i] = mat == null ? Color.white
                : mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId)
                : mat.HasProperty(ColorId) ? mat.GetColor(ColorId)
                : Color.white;
        }
    }

    /// <summary>Called by MiningController when the pickaxe hits this rock.</summary>
    public void TakeHit(int damage)
    {
        if (currentHealth <= 0) return; // already breaking

        // Multiplayer: flash right away for feel, but the host applies the damage (once) and tells everyone.
        if (WorldNetwork.Current != null)
        {
            Flash();
            WorldNetwork.Current.RequestRockHit(this, damage);
            return;
        }
        ApplyDamage(damage);
    }

    /// <summary>Removes health, flashes, and breaks the rock (with its drops) at zero. Returns true if it broke.
    /// Single player: every hit. Multiplayer: only the host calls this.</summary>
    public bool ApplyDamage(int damage)
    {
        if (currentHealth <= 0) return false;

        currentHealth -= damage;
        Flash();

        if (currentHealth > 0) return false;
        Break(spawnOre: true);
        return true;
    }

    /// <summary>Multiplayer: another player hit this rock; shows the hit and the host's health.</summary>
    public void ShowNetworkHit(int health)
    {
        if (currentHealth <= 0) return;
        currentHealth = health;
        Flash();
    }

    /// <summary>Multiplayer (not the host): the host broke this rock. No drops here: the host spawns the shared ore.</summary>
    public void BreakFromNetwork()
    {
        if (this == null) return;
        currentHealth = 0;
        Break(spawnOre: false);
    }

    private void Update()
    {
        if (flashTimer <= 0f) return;

        flashTimer -= Time.deltaTime;
        if (flashTimer <= 0f)
            SetColor(restore: true);
    }

    private void Flash()
    {
        if (flashDuration <= 0f) return;
        flashTimer = flashDuration;
        SetColor(restore: false);
    }

    private void SetColor(bool restore)
    {
        // A MaterialPropertyBlock changes the colour of just this rock
        // without editing the shared material asset (other rocks stay grey).
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            Color c = restore ? originalColors[i] : flashColor;
            renderers[i].GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, c);
            propertyBlock.SetColor(ColorId, c);
            renderers[i].SetPropertyBlock(propertyBlock);
        }
    }

    private void Break(bool spawnOre)
    {
        // Destroy() only happens at the end of the frame, so switch the rock's colliders
        // off now. Otherwise the new ore would spawn inside them and get shoved out violently.
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        if (spawnOre) SpawnOre();
        Destroy(gameObject);
    }

    private void SpawnOre()
    {
        Vector3 center = transform.position + Vector3.up * 0.3f;

        if (oreItem != null)
        {
            SpawnItems(center);
            return;
        }

        for (int i = 0; i < oreCount; i++)
        {
            // Spread pieces around the centre so they don't spawn inside each other.
            Vector3 offset = Random.insideUnitSphere * 0.3f;
            offset.y = Mathf.Abs(offset.y);
            Vector3 position = center + offset;

            GameObject ore = orePrefab != null
                ? Instantiate(orePrefab, position, Random.rotation)
                : CreateOreCube(position);

            // Pop outward and upward if the piece has physics.
            // VelocityChange ignores mass, so orePopSpeed is simply metres per second.
            if (ore.TryGetComponent(out Rigidbody body))
                body.AddForce((offset.normalized + Vector3.up).normalized * orePopSpeed, ForceMode.VelocityChange);
        }
    }

    /// <summary>One entry in the optional extra drops: an item, how many, and how likely.</summary>
    [System.Serializable]
    public class BonusDrop
    {
        public ItemData item;
        [Min(1)] public int amount = 1;
        [Range(0f, 1f)] public float chance = 0.25f;
    }

    /// <summary>
    /// Ore Count pieces of Ore Item, plus any bonus drops, each spawned as its own physical
    /// DroppedItem at a slightly different spot, popping outward with a random spin.
    /// </summary>
    private void SpawnItems(Vector3 center)
    {
        var pieces = new System.Collections.Generic.List<ItemData>();
        for (int i = 0; i < oreCount; i++) pieces.Add(oreItem);
        foreach (BonusDrop drop in bonusDrops)
            if (drop.item != null && Random.value < drop.chance)
                for (int i = 0; i < drop.amount; i++) pieces.Add(drop.item);

        var used = new System.Collections.Generic.List<Vector3>();
        foreach (ItemData item in pieces)
        {
            // Pick a spot not too close to the pieces already placed (a few tries is plenty).
            Vector3 offset = Vector3.zero;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                offset = Random.insideUnitSphere * 0.3f;
                offset.y = Mathf.Abs(offset.y);
                bool clear = true;
                foreach (Vector3 p in used)
                    if ((p - offset).sqrMagnitude < 0.2f * 0.2f) { clear = false; break; }
                if (clear) break;
            }
            used.Add(offset);

            // Same pop as before: outward and upward at orePopSpeed m/s, plus a random tumble.
            Vector3 velocity = (offset.normalized + Vector3.up).normalized * orePopSpeed;
            ItemDrops.Spawn(item, 1, center + offset, Random.rotation, velocity, Random.insideUnitSphere * oreSpin);
        }
    }

    private GameObject CreateOreCube(Vector3 position)
    {
        GameObject ore = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ore.name = "Ore Piece";
        ore.transform.SetPositionAndRotation(position, Random.rotation);
        ore.transform.localScale = Vector3.one * oreSize;
        if (oreMaterial != null)
            ore.GetComponent<Renderer>().sharedMaterial = oreMaterial;

        Rigidbody body = ore.AddComponent<Rigidbody>();
        body.mass = 0.2f;
        return ore;
    }
}
