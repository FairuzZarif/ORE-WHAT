using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What happens to the LOCAL player when health reaches 0, and coming back. Put it on the Player next to
/// PlayerAttributes. Works the same in single player and multiplayer:
///
///   Death    gameplay input stops (movement, looking, crouch, mining, weapons/punch, pickup, drop, headlamp
///            key), the held item is put away and a carried ore dropped, the CharacterController is switched
///            off, the body becomes a ragdoll (CharacterRagdoll, pushed by the killing hit) and the camera
///            leaves the eyes: it rises behind and above the body and looks down at it while DeathScreen
///            shows "YOU DIED" and the countdown.
///   Respawn  after CombatSettings' Respawn Delay: full health and stamina, standing at a spawn point
///            (PlayerSpawnPoints = the map's PlayerStart zones), everything above switched back on.
///
/// Single player: this counts down and respawns by itself. Multiplayer: the host decides (NetworkPlayerHealth
/// calls SetRespawnCountdown and Respawn); this only presents it. RESPAWN NOW (DeathScreen's button) calls
/// RequestRespawnNow: right away in single player, a request to the host in multiplayer; once per death.
/// </summary>
[DefaultExecutionOrder(200)] // the death camera is placed after everything else that moves the camera
public class PlayerDeath : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [SerializeField] private PlayerAttributes attributes;
    [SerializeField] private CharacterController controller;
    [SerializeField] private PlayerEquipment equipment;
    [SerializeField] private OreCarryController carrier;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private CharacterRagdoll ragdoll;
    [SerializeField] private FirstPersonBodyVisibility bodyVisibility;
    [Tooltip("Switched off while dead (gameplay input). Empty = the Player's usual input scripts, found automatically.")]
    [SerializeField] private Behaviour[] disableWhileDead = new Behaviour[0];

    [Header("Death camera")]
    [Tooltip("Where the camera ends up, relative to the body: up and back (metres).")]
    [SerializeField] private Vector2 cameraRise = new Vector2(2.2f, 2f);
    [Tooltip("Seconds for the camera to get there.")]
    [SerializeField, Min(0.1f)] private float cameraTime = 1.4f;

    private readonly List<Behaviour> switchedOff = new List<Behaviour>();
    private Vector3 startPosition;
    private Vector3 cameraLocalPosition;
    private Quaternion cameraLocalRotation;
    private Vector3 deathCameraPosition, deathLookDirection;
    private Quaternion deathCameraRotation;
    private bool wasFirstPerson, respawnRequested;
    private float deathTime, respawnTime;

    public bool IsDead { get; private set; }
    /// <summary>True when RESPAWN NOW may be used: dead, a moment after dying, and not already asked.</summary>
    public bool CanRespawnNow => IsDead && !respawnRequested && CombatSettings.Current.allowRespawnNow &&
                                 Time.time - deathTime >= CombatSettings.Current.respawnNowDelay;
    /// <summary>RESPAWN NOW was used and the respawn is on its way (multiplayer: waiting for the host).</summary>
    public bool RespawnRequested => IsDead && respawnRequested;
    /// <summary>Seconds until the respawn (0 when due; in multiplayer the host may still be on its way).</summary>
    public float RespawnCountdown => IsDead ? Mathf.Max(0f, respawnTime - Time.time) : 0f;
    /// <summary>The whole respawn wait, seconds (for a progress bar).</summary>
    public float RespawnDelay { get; private set; }

    /// <summary>The local player died (after everything above was switched off).</summary>
    public event Action DeathStarted;
    /// <summary>The local player is back (standing at the spawn, controls on).</summary>
    public event Action Respawned;

    private void Awake()
    {
        if (attributes == null) attributes = GetComponent<PlayerAttributes>();
        if (controller == null) controller = GetComponent<CharacterController>();
        if (equipment == null) equipment = GetComponent<PlayerEquipment>();
        if (carrier == null) carrier = GetComponent<OreCarryController>();
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>(true);
        if (ragdoll == null) ragdoll = GetComponentInChildren<CharacterRagdoll>(true);
        if (bodyVisibility == null) bodyVisibility = GetComponentInChildren<FirstPersonBodyVisibility>(true);
        if (disableWhileDead == null || disableWhileDead.Length == 0) disableWhileDead = FindInputScripts();
        startPosition = transform.position;
        IsDead = false; // Play Mode keeps state between sessions in this project
    }

    private void OnEnable() { if (attributes != null) { attributes.Died += Die; attributes.Revived += OnRevived; } }
    private void OnDisable() { if (attributes != null) { attributes.Died -= Die; attributes.Revived -= OnRevived; } }

    /// <summary>The Player's scripts that read the keyboard / mouse (or move the player) for gameplay.</summary>
    private Behaviour[] FindInputScripts()
    {
        var list = new List<Behaviour>();
        void Add<T>() where T : Behaviour { foreach (T b in GetComponents<T>()) list.Add(b); }
        Add<PlayerMovement>(); Add<PlayerLook>(); Add<PlayerCrouch>(); Add<MiningController>(); Add<UnarmedAttack>();
        Add<ItemPickupInteractor>(); Add<ItemDropper>(); Add<HeadlampToggle>();
        return list.ToArray();
    }

    // ---------------------------------------------------------------- death

    private void Die()
    {
        if (IsDead) return;
        GetComponent<CompanyOfficeUI>()?.CloseBeforeDeath();
        IsDead = true;
        respawnRequested = false;
        deathTime = Time.time;
        RespawnDelay = CombatSettings.Current.respawnDelay;
        respawnTime = deathTime + RespawnDelay;

        Vector3 velocity = controller != null && controller.enabled ? controller.velocity : Vector3.zero;
        switchedOff.Clear();
        foreach (Behaviour b in disableWhileDead)
            if (b != null && b.enabled) { b.enabled = false; switchedOff.Add(b); }
        if (carrier != null) carrier.ReleaseAll();
        if (equipment != null) equipment.SetHidden(true); // no weapon / tool / punch while dead
        if (controller != null) controller.enabled = false;

        // The body falls; the camera leaves the eyes and the whole body is drawn (it's normally hidden from its own camera).
        if (ragdoll != null)
            ragdoll.Activate(attributes.DeathImpulse, attributes.DeathPoint, velocity, CombatSettings.Current.maxRagdollSpeed);
        if (bodyVisibility != null) { wasFirstPerson = bodyVisibility.LocalFirstPerson; bodyVisibility.LocalFirstPerson = false; }
        if (playerCamera != null)
        {
            Transform cam = playerCamera.transform;
            cameraLocalPosition = cam.localPosition;
            cameraLocalRotation = cam.localRotation;
            deathCameraPosition = cam.position;
            deathCameraRotation = cam.rotation;
            deathLookDirection = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            if (deathLookDirection.sqrMagnitude < 0.01f) deathLookDirection = transform.forward;
        }
        DeathStarted?.Invoke();
    }

    private void Update()
    {
        // Single player: respawn by ourselves. Multiplayer: the host says when (NetworkPlayerHealth → Respawn).
        if (IsDead && attributes != null && attributes.Authority == null && Time.time >= respawnTime)
            Respawn(PlayerSpawnPoints.FindSpot(startPosition, 0, null, controller != null ? controller.radius : 0.4f,
                                               controller != null ? controller.height : 1.8f));
    }

    /// <summary>
    /// RESPAWN NOW (the death screen's button). Single player: respawns right away. Multiplayer: asks the host, which
    /// checks it and sends the respawn back like the automatic one. Only once per death. Returns false if not allowed.
    /// </summary>
    public bool RequestRespawnNow()
    {
        if (!CanRespawnNow) return false;
        respawnRequested = true;
        if (attributes != null && attributes.Authority != null) attributes.Authority.RequestRespawn();
        else Respawn(PlayerSpawnPoints.FindSpot(startPosition, 0, null, controller != null ? controller.radius : 0.4f,
                                                controller != null ? controller.height : 1.8f));
        return true;
    }

    /// <summary>Multiplayer: the host's time left until the respawn (seconds).</summary>
    public void SetRespawnCountdown(float secondsLeft) => respawnTime = Time.time + Mathf.Max(0f, secondsLeft);

    // Health set above 0 some other way (e.g. a test or a future revive item): come back where the body is.
    private void OnRevived() { if (IsDead) Respawn(ragdoll != null && ragdoll.Hips != null ? GroundBelow(ragdoll.Hips.position) : transform.position); }

    // ---------------------------------------------------------------- respawn

    /// <summary>Stands the player up at feet (world), full health, controls back on.</summary>
    public void Respawn(Vector3 feet)
    {
        if (!IsDead) return;
        IsDead = false; // first, so Revive's Revived event doesn't come back here

        if (ragdoll != null) ragdoll.Deactivate();
        transform.position = feet; // the CharacterController is still off, so this sticks
        if (controller != null) controller.enabled = true;
        if (playerCamera != null) playerCamera.transform.SetLocalPositionAndRotation(cameraLocalPosition, cameraLocalRotation);
        if (bodyVisibility != null) bodyVisibility.LocalFirstPerson = wasFirstPerson;
        if (equipment != null) equipment.SetHidden(false);
        foreach (Behaviour b in switchedOff) if (b != null) b.enabled = true;
        switchedOff.Clear();
        var look = GetComponent<PlayerLook>();
        if (look != null && look.enabled) look.LockCursor(true); // the death screen freed the cursor for its button
        if (attributes != null && attributes.IsDead) attributes.Revive();
        else if (attributes != null) attributes.SetStamina(attributes.MaxStamina);
        Respawned?.Invoke();
    }

    private static Vector3 GroundBelow(Vector3 point)
    {
        int mask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));
        return Physics.Raycast(point + Vector3.up, Vector3.down, out RaycastHit hit, 5f, mask, QueryTriggerInteraction.Ignore)
            ? hit.point + Vector3.up * 0.05f : point;
    }

    // ---------------------------------------------------------------- death camera

    private void LateUpdate()
    {
        if (!IsDead || playerCamera == null) return;
        Transform cam = playerCamera.transform;
        Transform body = ragdoll != null && ragdoll.Hips != null ? ragdoll.Hips : transform;
        Vector3 focus = body.position;

        // Up and behind the body (behind = where the player was looking from), never through rock.
        Vector3 wanted = focus + Vector3.up * cameraRise.x - deathLookDirection * cameraRise.y;
        int mask = ~((1 << 2) | (1 << 6) | (1 << 7) | (1 << 8));
        Vector3 toWanted = wanted - focus;
        if (Physics.SphereCast(focus, 0.2f, toWanted.normalized, out RaycastHit hit, toWanted.magnitude, mask, QueryTriggerInteraction.Ignore))
            wanted = focus + toWanted.normalized * Mathf.Max(0.3f, hit.distance - 0.1f);

        float t = Mathf.Clamp01((Time.time - deathTime) / cameraTime);
        float k = t * t * (3f - 2f * t);
        Quaternion lookAtBody = Quaternion.LookRotation(focus - wanted, Vector3.up);
        cam.SetPositionAndRotation(Vector3.Lerp(deathCameraPosition, wanted, k), Quaternion.Slerp(deathCameraRotation, lookAtBody, k));
    }
}
