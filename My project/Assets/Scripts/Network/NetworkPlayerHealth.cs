using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A player's health in a multiplayer session. On the NetworkPlayer (next to NetworkPlayerAvatar); the HOST
/// decides everything here, nobody else can change a player's health.
///
///   Health   a NetworkVariable only the host writes (sent when it changes, never per frame). The owner's
///            PlayerAttributes shows it (so the HUD bars, DamageFlash and PlayerDeath work as in single player).
///   Hits     your weapon / tool / punch code is unchanged: when its hit lands on another player's body it calls
///            IDamageable.TakeDamage on that player's copy, which sends a request to the host. The host checks it
///            (friendly fire on, attacker alive and holding something that can hurt, hit point at the target, start
///            point at the attacker's eyes, within the weapon's reach, nothing solid in between, not faster than the
///            weapon can attack, at most ONE hit per attack (shot / punch / swing) on this player) and then applies
///            the damage of the weapon the host sees in their hands × the body part's multiplier (head / chest /
///            arms / legs, CombatSettings; the part the attacker reports is checked against the host's copy of the
///            body, see DamageRpc) - the client never says how much. Remote
///            animations (swings, shots others see) never cause damage.
///   Results  only for hits the host accepted: the attacker gets HitConfirmFeedback (hit marker, damage number,
///            tick - or the kill sound for the killing hit); everyone gets one HitRpc (blood burst + splats on nearby
///            surfaces from a shared seed, the body's flinch for everyone but the victim); the victim sees their health
///            drop (DamageFlash).
///   Death    at 0 health the host marks the player dead (with the killing push and the time of the respawn):
///            everyone shows the ragdoll, the victim's PlayerDeath takes over the controls. Late joiners get the
///            same state, so a dead player is a ragdoll for them too.
///   Respawn  after CombatSettings' delay - or earlier when the dead player asks (RESPAWN NOW; the host checks they
///            are dead and the minimum wait has passed) - the host picks a free spawn spot (PlayerSpawnPoints),
///            resets health and tells the owner, who stands up there (the same NetworkObject: nothing is despawned).
/// </summary>
[DefaultExecutionOrder(65)]
public class NetworkPlayerHealth : NetworkBehaviour, IDamageable, IHealthAuthority
{
    /// <summary>Dead or alive, plus what the ragdoll needs and when the respawn is due.</summary>
    public struct Life : INetworkSerializable, System.IEquatable<Life>
    {
        public bool dead;
        public Vector3 impulse, point;
        public double respawnAt; // host time (NetworkManager.ServerTime)
        public byte deaths;      // counts up, so every death is a change

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref dead); s.SerializeValue(ref impulse); s.SerializeValue(ref point);
            s.SerializeValue(ref respawnAt); s.SerializeValue(ref deaths);
        }

        public bool Equals(Life o) => dead == o.dead && impulse == o.impulse && point == o.point && respawnAt == o.respawnAt && deaths == o.deaths;
    }

    [SerializeField] private NetworkPlayerAvatar avatar;
    [Tooltip("Short blood burst played where a player is hit (Assets/Prefabs/Effects/BloodHit.prefab). It removes itself.")]
    [SerializeField] private GameObject bloodEffect;
    [Tooltip("Material of the blood splats left on nearby surfaces (Assets/Materials/Effects/BloodSplat.mat).")]
    [SerializeField] private Material bloodSplat;
    [Tooltip("Used if the host has no PlayerAttributes to read Max Health from.")]
    [SerializeField, Min(1f)] private float defaultMaxHealth = 100f;

    private readonly NetworkVariable<float> health = new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone,
                                                                                NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Life> life = new NetworkVariable<Life>(default, NetworkVariableReadPermission.Everyone,
                                                                            NetworkVariableWritePermission.Server);

    // Everything solid in the world for the host's line-of-sight check (not Ignore Raycast, ViewModel, DroppedItem, Player).
    private const int WorldMask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));
    private const float BodyRadius = 0.4f;

    /// <summary>This computer's own player, once spawned.</summary>
    public static NetworkPlayerHealth Local { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Local = null; // Play Mode doesn't reload scripts in this project

    public float Health => health.Value;
    public bool IsDead => life.Value.dead;

    // Owner: the real (scene) player.
    private PlayerAttributes attributes;
    private PlayerDeath death;
    private Camera localCamera;
    // Host: each attacker's hit allowance (rate check). It refills at the weapon's attack rate and holds up to 3 gun
    // hits, so hits that arrive bunched together (network jitter) still count, but a faster stream doesn't. Melee holds
    // only 1: one punch / swing at a time.
    private struct Budget { public float credit, time; }
    private readonly Dictionary<ulong, Budget> budgets = new Dictionary<ulong, Budget>();
    private const float MaxBunchedShots = 3f, MaxBunchedBlows = 1f;
    // Host: the last attack (HeldItemController.CurrentAttackId) of each attacker that hurt this player. One attack can
    // hurt a player once: a second request for the same punch / swing / bullet is refused.
    private readonly Dictionary<ulong, int> lastAttack = new Dictionary<ulong, int>();
    private readonly RaycastHit[] lineHits = new RaycastHit[16];

    private void Awake() { if (avatar == null) avatar = GetComponent<NetworkPlayerAvatar>(); }

    public override void OnNetworkSpawn()
    {
        if (IsServer) health.Value = MaxHealth;
        health.OnValueChanged += OnHealthChanged;
        life.OnValueChanged += OnLifeChanged;
        if (IsOwner) Local = this;
        else if (life.Value.dead && avatar != null) avatar.SetDead(true, Vector3.zero, life.Value.point); // joined while they're dead
    }

    public override void OnNetworkDespawn()
    {
        health.OnValueChanged -= OnHealthChanged;
        life.OnValueChanged -= OnLifeChanged;
        if (Local == this) Local = null;
        if (attributes != null && ReferenceEquals(attributes.Authority, this)) attributes.Authority = null;
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsOwner && attributes == null) Bind();
        if (IsServer && life.Value.dead && NetworkManager.ServerTime.Time >= life.Value.respawnAt) Respawn();
    }

    /// <summary>Owner: finds this game's Player (its scene may still be loading) and lets the host own its health.</summary>
    private void Bind()
    {
        attributes = FindAnyObjectByType<PlayerAttributes>();
        if (attributes == null) return;
        attributes.Authority = this;
        death = attributes.GetComponent<PlayerDeath>();
        localCamera = attributes.GetComponentInChildren<Camera>(true);
        if (!life.Value.dead && !Mathf.Approximately(attributes.Health, health.Value))
            attributes.ApplyAuthoritativeHealth(health.Value, Vector3.zero, attributes.transform.position);
    }

    private float MaxHealth
    {
        get
        {
            var local = FindAnyObjectByType<PlayerAttributes>(); // every copy of the game uses the same Player settings
            return local != null ? local.MaxHealth : defaultMaxHealth;
        }
    }

    // ---------------------------------------------------------------- attacking (on the attacker's computer)

    /// <summary>
    /// IDamageable: one of OUR attacks hit this (other) player's body. Only asks the host; nothing changes here.
    /// </summary>
    public void TakeDamage(float amount, RaycastHit hit)
    {
        if (!IsSpawned || IsOwner || life.Value.dead || amount <= 0f) return;
        NetworkPlayerHealth me = Local;
        if (me == null || me.IsDead || me.localCamera == null) return; // the dead can't hurt anyone
        Vector3 origin = me.localCamera.transform.position;
        Vector3 direction = hit.point - origin;
        if (direction.sqrMagnitude < 0.0001f) direction = me.localCamera.transform.forward;
        // Which body part: the hitbox the ray struck (or the nearest part to the point, e.g. a punch's sphere).
        CharacterRagdoll body = avatar != null ? avatar.BodyForHits : null;
        BodyZone zone = BodyZone.Chest;
        if (body != null && !body.TryGetZone(hit.collider, out zone)) zone = body.ClosestZone(hit.point);
        RequestHit(origin, hit.point, direction.normalized, zone);
    }

    /// <summary>
    /// Asks the host to apply one hit of our current attack: only WHICH attack hit WHERE (and the body part we saw);
    /// how much it hurts, and whether that body part is believable, is the host's call.
    /// </summary>
    public void RequestHit(Vector3 origin, Vector3 point, Vector3 direction, BodyZone zone)
    {
        if (!IsSpawned || IsOwner || life.Value.dead) return;
        DamageRpc(HeldItemController.CurrentAttackId, origin, point, direction, zone);
    }

    /// <summary>IHealthAuthority: the dead owner pressed RESPAWN NOW.</summary>
    public void RequestRespawn()
    {
        if (IsSpawned && IsOwner) RespawnNowRpc();
    }

    /// <summary>IHealthAuthority: damage this player took from something on its own computer (not another player).</summary>
    public void RequestDamage(float amount, Vector3 impulse)
    {
        if (IsSpawned && IsOwner && amount > 0f) SelfDamageRpc(amount, impulse);
    }

    // ---------------------------------------------------------------- the host decides

    [Rpc(SendTo.Server)]
    private void DamageRpc(int attackId, Vector3 origin, Vector3 point, Vector3 direction, BodyZone claimedZone, RpcParams rpcParams = default)
    {
        ulong attacker = rpcParams.Receive.SenderClientId;
        if (!AcceptHit(attacker, attackId, origin, point, direction, out float amount, out bool ranged)) return;

        // The body part: the attacker's, if the point really is on that part of the host's copy of us (within the lag
        // allowance); otherwise the part the host itself finds nearest. Then the weapon's damage × that part's multiplier.
        CombatSettings settings = CombatSettings.Current;
        BodyZone zone = claimedZone;
        CharacterRagdoll body = avatar != null ? avatar.BodyForHits : null;
        if (body != null && body.DistanceToZone(claimedZone, point) > settings.zoneTolerance) zone = body.ClosestZone(point);
        amount *= settings.Multiplier(zone);
        if (amount <= 0f) return;

        bool killed = Apply(amount, direction * (ranged ? settings.rangedImpulse : settings.meleeImpulse), point);
        // Everything below happens only for this accepted hit, once each.
        HitConfirmedRpc(point, amount, killed, zone, RpcTarget.Single(attacker, RpcTargetUse.Temp));
        HitRpc(point, direction, !ranged, Random.Range(int.MinValue, int.MaxValue));
    }

    [Rpc(SendTo.Server)]
    private void RespawnNowRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || !life.Value.dead) return; // only the dead player, only while dead
        CombatSettings settings = CombatSettings.Current;
        if (!settings.allowRespawnNow) return;
        double diedAt = life.Value.respawnAt - settings.respawnDelay;
        if (NetworkManager.ServerTime.Time - diedAt < settings.respawnNowDelay - 0.25f) return; // too soon (small allowance for lag)
        Respawn();
    }

    [Rpc(SendTo.Server)]
    private void SelfDamageRpc(float amount, Vector3 impulse, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId || life.Value.dead) return;
        Apply(Mathf.Min(amount, MaxHealth), Vector3.ClampMagnitude(impulse, CombatSettings.Current.rangedImpulse), transform.position + Vector3.up);
    }

    /// <summary>Host: is this reported hit believable? amount = the damage of the attacker's weapon (host's value).</summary>
    private bool AcceptHit(ulong attacker, int attackId, Vector3 origin, Vector3 point, Vector3 direction, out float amount, out bool ranged)
    {
        ranged = false;
        amount = 0f;
        CombatSettings settings = CombatSettings.Current;
        if (!settings.friendlyFire || attacker == OwnerClientId) return false; // no self-hits
        if (lastAttack.TryGetValue(attacker, out int last) && attackId <= last) return false; // this attack already hit us
        if (life.Value.dead || health.Value <= 0f) return false;
        if (!NetworkManager.ConnectedClients.TryGetValue(attacker, out NetworkClient client) || client.PlayerObject == null) return false;
        var attackerHealth = client.PlayerObject.GetComponent<NetworkPlayerHealth>();
        var attackerAvatar = client.PlayerObject.GetComponent<NetworkPlayerAvatar>();
        if (attackerAvatar == null || (attackerHealth != null && attackerHealth.IsDead)) return false;

        // What they hold, by the host's own copy of that item's controller (the real damage / reach / rate).
        HeldItemController weapon = WeaponOf(attackerAvatar);
        if (weapon == null || weapon.PlayerDamage <= 0f) return false;
        ranged = weapon.IsRanged;
        amount = weapon.PlayerDamage; // never the client's number

        float maxCredit = ranged ? MaxBunchedShots : MaxBunchedBlows;
        if (!budgets.TryGetValue(attacker, out Budget budget)) budget = new Budget { credit = maxCredit, time = Time.time };
        // (refills a little faster than the weapon's rate: two honest attacks can arrive closer together than they were made)
        budget.credit = Mathf.Min(maxCredit, budget.credit + (Time.time - budget.time) / Mathf.Max(0.02f, weapon.AttackInterval * 0.8f));
        budget.time = Time.time;
        budgets[attacker] = budget;
        if (budget.credit < 1f) return false; // faster than that weapon can attack
        if ((origin - attackerAvatar.EyePosition).sqrMagnitude > settings.originTolerance * settings.originTolerance) return false;
        if (DistanceToBody(point) > BodyRadius + settings.targetTolerance) return false;
        float reach = weapon.AttackRange + settings.rangeTolerance;
        if ((point - origin).sqrMagnitude > reach * reach) return false;
        if (Blocked(origin, point)) return false; // no shooting through walls

        budget.credit -= 1f;
        budgets[attacker] = budget;
        lastAttack[attacker] = attackId;
        return true;
    }

    private static HeldItemController WeaponOf(NetworkPlayerAvatar who)
    {
        int held = who.HeldItemIndex;
        if (held == NetworkPlayerAvatar.CarryingItem) return null; // hands full of ore
        ItemData item = held >= 0 && NetworkWorld.Instance != null ? NetworkWorld.Instance.ItemAt(held) : null;
        if (held >= 0 && item == null) return null;
        var equipment = FindAnyObjectByType<PlayerEquipment>(); // the host's own player has every item's view
        return equipment != null ? equipment.ControllerFor(item) : null;
    }

    private float DistanceToBody(Vector3 point)
    {
        Vector3 a = transform.position + Vector3.up * BodyRadius, b = transform.position + Vector3.up * 1.8f;
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude);
        return Vector3.Distance(point, a + ab * t);
    }

    /// <summary>Is there rock (or anything solid that isn't a player) between the two points?</summary>
    private bool Blocked(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float length = d.magnitude - 0.05f;
        if (length <= 0f) return false;
        int count = Physics.RaycastNonAlloc(from, d / d.magnitude, lineHits, length, WorldMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (lineHits[i].collider.GetComponentInParent<NetworkPlayerAvatar>() == null) return true; // players don't block
        return false;
    }

    /// <summary>Host: lowers health; at 0 the player dies (once). Returns true if this killed them.</summary>
    private bool Apply(float amount, Vector3 impulse, Vector3 point)
    {
        if (life.Value.dead) return false;
        float left = Mathf.Max(0f, health.Value - amount);
        health.Value = left;
        if (left > 0f) return false;

        Life l = life.Value;
        l.dead = true;
        l.impulse = impulse;
        l.point = point;
        l.respawnAt = NetworkManager.ServerTime.Time + CombatSettings.Current.respawnDelay;
        l.deaths++;
        life.Value = l;
        return true;
    }

    /// <summary>Host: back to full health at a free spawn spot.</summary>
    private void Respawn()
    {
        var others = new List<Vector3>();
        foreach (NetworkClient c in NetworkManager.ConnectedClientsList)
            if (c.PlayerObject != null && c.PlayerObject != NetworkObject) others.Add(c.PlayerObject.transform.position);
        Vector3 spot = PlayerSpawnPoints.FindSpot(transform.position, (int)OwnerClientId + life.Value.deaths, others);

        Life l = life.Value;
        l.dead = false;
        l.impulse = Vector3.zero;
        life.Value = l;
        health.Value = MaxHealth;
        RespawnRpc(spot);
    }

    // ---------------------------------------------------------------- results

    /// <summary>The attacker only: hit marker, damage number and the tick (or the kill sound).</summary>
    [Rpc(SendTo.SpecifiedInParams)]
    private void HitConfirmedRpc(Vector3 point, float damage, bool killed, BodyZone zone, RpcParams rpcParams) =>
        HitConfirmFeedback.Confirm(point, damage, killed, zone);

    /// <summary>
    /// Everyone, once per accepted hit: blood (burst + splats; the same seed gives everyone the same splats) and the
    /// body's flinch. The victim gets the splats but not the burst or flinch (their own camera; DamageFlash instead).
    /// </summary>
    [Rpc(SendTo.Everyone)]
    private void HitRpc(Vector3 point, Vector3 direction, bool melee, int seed)
    {
        BloodSplatter.Play(bloodEffect, bloodSplat, point, direction, melee, seed, particles: !IsOwner, ignore: transform);
        if (!IsOwner && avatar != null) avatar.PlayHitReaction(direction, point, melee);
    }

    [Rpc(SendTo.Owner)]
    private void RespawnRpc(Vector3 spot)
    {
        if (attributes == null) Bind();
        if (death != null && death.IsDead) death.Respawn(spot);
        else if (attributes != null)
        {
            // No PlayerDeath on this Player: just put it there with full health.
            var controller = attributes.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            attributes.transform.position = spot;
            if (controller != null) controller.enabled = true;
            if (attributes.IsDead) attributes.Revive();
        }
        if (avatar != null) avatar.TeleportToPlayer(); // jump there for everyone instead of sliding across the map
    }

    private void OnHealthChanged(float before, float now)
    {
        // Owner: show the host's value. Dying and coming back are handled by the life state / RespawnRpc, so the
        // body falls with the right push and stands up at the spawn, not where it fell.
        if (!IsOwner || attributes == null || attributes.IsDead || now <= 0f) return;
        attributes.ApplyAuthoritativeHealth(now, Vector3.zero, attributes.transform.position);
    }

    private void OnLifeChanged(Life before, Life now)
    {
        if (IsOwner)
        {
            if (attributes == null) Bind();
            if (now.dead && !before.dead && attributes != null)
            {
                attributes.ApplyAuthoritativeHealth(0f, now.impulse, now.point);
                if (death != null) death.SetRespawnCountdown((float)(now.respawnAt - NetworkManager.ServerTime.Time));
            }
            return;
        }
        if (avatar != null && now.dead != before.dead) avatar.SetDead(now.dead, now.impulse, now.point);
    }
}
