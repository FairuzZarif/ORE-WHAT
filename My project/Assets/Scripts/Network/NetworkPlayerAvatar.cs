using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// How a player appears to the OTHER players. Every player in a session gets one of these (Netcode spawns it
/// as the player object); its owner is that player.
///
/// The real player is still the scene's Player (camera, input, HUD, first-person arms, inventory): one per
/// game, controlled only by the person at that computer, exactly as in single player. This avatar never
/// moves anything; it only copies:
///   Owner   every frame, copies its own Player's position, facing, animation parameters, crouch, look pitch
///           and headlamp into the network (the NetworkTransform + State). Its own body is hidden.
///   Others  show the CorporateMiner body there, interpolated, with the same animations (walk, run, walk
///           backward, jump, fall, land, crouch), the head tilting with the look, and their headlamp.
///
/// It also puts its owner's Player at a free spawn spot when the map loads, so players don't start inside each other.
/// </summary>
[DefaultExecutionOrder(60)] // after PlayerMovement / PlayerLook / PlayerMotionState have run this frame
public class NetworkPlayerAvatar : NetworkBehaviour
{
    /// <summary>Everything the body needs to look right, sent by the owner.</summary>
    public struct State : INetworkSerializable, System.IEquatable<State>
    {
        public float speed, forwardSpeed, walkPlayback, runPlayback, walkBackPlayback, verticalSpeed, pitch;
        public byte crouch;   // 0..255 = 0..1
        public byte jumps;    // counts up on every jump, so a missed update can't lose one
        public bool grounded;
        public bool ready;    // false until the owner has placed its player (the avatar is hidden until then)

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref speed); s.SerializeValue(ref forwardSpeed);
            s.SerializeValue(ref walkPlayback); s.SerializeValue(ref runPlayback); s.SerializeValue(ref walkBackPlayback);
            s.SerializeValue(ref verticalSpeed); s.SerializeValue(ref pitch);
            s.SerializeValue(ref crouch); s.SerializeValue(ref jumps);
            s.SerializeValue(ref grounded); s.SerializeValue(ref ready);
        }

        public bool Equals(State o) =>
            speed == o.speed && forwardSpeed == o.forwardSpeed && walkPlayback == o.walkPlayback && runPlayback == o.runPlayback &&
            walkBackPlayback == o.walkBackPlayback && verticalSpeed == o.verticalSpeed && pitch == o.pitch &&
            crouch == o.crouch && jumps == o.jumps && grounded == o.grounded && ready == o.ready;
    }

    [Header("Body (this prefab's own parts)")]
    [SerializeField] private Animator body;
    [SerializeField] private CharacterCrouchPose crouchPose;
    [SerializeField] private Headlamp headlamp;
    [Tooltip("Turned up/down with the owner's look; the headlamp aims along it.")]
    [SerializeField] private Transform aim;
    [Tooltip("Blocks the local player from walking through other players.")]
    [SerializeField] private Collider bodyCollider;
    [Tooltip("Shows the held item, the arms holding it and actions (punch, swing, shot, reload) to other players.")]
    [SerializeField] private RemotePlayerPresentation presentation;

    [Header("Remote look")]
    [Tooltip("Share of the look pitch put into the chest / neck / head, so others see where you look.")]
    [SerializeField, Range(0f, 1f)] private float chestPitch = 0.25f, neckPitch = 0.25f, headPitch = 0.35f;
    [Tooltip("How quickly the remote body follows the received animation values (per second).")]
    [SerializeField, Min(1f)] private float smoothing = 15f;

    [Header("Spawn spots")]
    [Tooltip("Players join around the map's start point, this far apart (metres).")]
    [SerializeField, Min(1f)] private float spawnSpacing = 2.5f;

    private readonly NetworkVariable<State> state = new NetworkVariable<State>(default, NetworkVariableReadPermission.Everyone,
                                                                                NetworkVariableWritePermission.Owner);
    /// <summary>What the owner holds: an index into NetworkWorld's item list, NoItem (empty hands) or Carrying (a world object).</summary>
    private readonly NetworkVariable<int> heldItem = new NetworkVariable<int>(NoItem, NetworkVariableReadPermission.Everyone,
                                                                              NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> headlampOn = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone,
                                                                                  NetworkVariableWritePermission.Owner);
    private const int NoItem = -1, Carrying = -2;

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int ForwardSpeedId = Animator.StringToHash("ForwardSpeed");
    private static readonly int WalkPlaybackId = Animator.StringToHash("WalkPlayback");
    private static readonly int RunPlaybackId = Animator.StringToHash("RunPlayback");
    private static readonly int WalkBackPlaybackId = Animator.StringToHash("WalkBackPlayback");
    private static readonly int GroundedId = Animator.StringToHash("Grounded");
    private static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");
    private static readonly int JumpId = Animator.StringToHash("Jump");

    // Owner: the real (scene) player this avatar copies.
    private PlayerMovement player;
    private Animator playerAnimator;
    private PlayerMotionState playerMotion;
    private PlayerCrouch playerCrouch;
    private Headlamp playerHeadlamp;
    private Transform playerCamera;
    private PlayerEquipment equipment;
    private bool heldResolved = true;
    private FistsController fists;
    private WeaponController[] weapons = new WeaponController[0];
    private PickaxeSwing[] swings = new PickaxeSwing[0];
    private NetworkTransform netTransform;
    private byte jumps;

    // Remote: smoothed values.
    private State shown;
    private byte shownJumps;
    private Transform chest, neck, head;

    /// <summary>The scene Player this avatar belongs to (owner only), once found.</summary>
    public PlayerMovement LocalPlayer => player;

    private void Awake() => netTransform = GetComponent<NetworkTransform>();

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            // We see our own body through the real Player: hide this one completely.
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            if (body != null) body.enabled = false;
            if (bodyCollider != null) bodyCollider.enabled = false;
            if (headlamp != null) { headlamp.SetOn(false); headlamp.enabled = false; }
            if (presentation != null) presentation.enabled = false; // we see our own first-person views instead
        }
        else
        {
            shownJumps = state.Value.jumps;
            SetVisible(state.Value.ready); // hidden until its owner has put it at its spawn spot
            // Persistent state (also right for players who join later): held item and headlamp.
            heldItem.OnValueChanged += OnHeldItemChanged;
            headlampOn.OnValueChanged += OnHeadlampChanged;
            OnHeldItemChanged(NoItem, heldItem.Value);
            OnHeadlampChanged(false, headlampOn.Value);
        }
        name = $"Player {OwnerClientId}" + (IsOwner ? " (you)" : "");
    }

    public override void OnNetworkDespawn()
    {
        heldItem.OnValueChanged -= OnHeldItemChanged;
        headlampOn.OnValueChanged -= OnHeadlampChanged;
        Unbind();
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (IsOwner) CopyFromLocalPlayer();
        else ShowRemote();
    }

    // ---------------------------------------------------------------- owner

    private void CopyFromLocalPlayer()
    {
        if (player == null && !Bind()) return;

        Transform root = player.transform;
        transform.SetPositionAndRotation(root.position, root.rotation);

        var s = new State
        {
            pitch = playerCamera != null ? Mathf.DeltaAngle(0f, playerCamera.localEulerAngles.x) : 0f,
            crouch = (byte)Mathf.RoundToInt(Mathf.Clamp01(playerCrouch != null ? playerCrouch.Amount : 0f) * 255f),
            jumps = jumps,
            ready = true,
        };
        if (playerAnimator != null && playerAnimator.isInitialized)
        {
            s.speed = playerAnimator.GetFloat(SpeedId);
            s.forwardSpeed = playerAnimator.GetFloat(ForwardSpeedId);
            s.walkPlayback = playerAnimator.GetFloat(WalkPlaybackId);
            s.runPlayback = playerAnimator.GetFloat(RunPlaybackId);
            s.walkBackPlayback = playerAnimator.GetFloat(WalkBackPlaybackId);
            s.verticalSpeed = playerAnimator.GetFloat(VerticalSpeedId);
            s.grounded = playerAnimator.GetBool(GroundedId);
        }
        if (!s.Equals(state.Value)) state.Value = s;

        // Held item: only written when it actually changes.
        int held = equipment == null ? NoItem : equipment.IsCarrying ? Carrying : ItemIndex(equipment.Equipped);
        if (held != heldItem.Value) heldItem.Value = held;
    }

    /// <summary>Finds this game's Player (it's in the map scene, which may still be loading), then places it.</summary>
    private bool Bind()
    {
        player = FindAnyObjectByType<PlayerMovement>();
        if (player == null) return false;

        var animator = player.GetComponentInChildren<CharacterAnimator>(true);
        playerAnimator = animator != null ? animator.GetComponent<Animator>() : null;
        playerMotion = player.GetComponent<PlayerMotionState>();
        playerCrouch = player.GetComponent<PlayerCrouch>();
        playerHeadlamp = player.GetComponent<Headlamp>();
        Camera cam = player.GetComponentInChildren<Camera>(true);
        playerCamera = cam != null ? cam.transform : null;
        if (playerMotion != null) playerMotion.JumpStarted += OnLocalJump;
        HookActions(player);

        PlaceAtSpawnSpot(player);
        // Jump straight there for everyone instead of sliding across the map from where the avatar was made.
        if (netTransform != null) netTransform.Teleport(player.transform.position, player.transform.rotation, transform.localScale);
        else transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
        return true;
    }

    private void Unbind()
    {
        if (playerMotion != null) playerMotion.JumpStarted -= OnLocalJump;
        UnhookActions();
        player = null;
        playerMotion = null;
    }

    // ---------------------------------------------------------------- owner: actions → small events for the others
    // Only VISUALS are sent. Damage stays where it is (the local punch / shot / swing code, and the host for rocks):
    // a remote copy playing one of these never hits anything.

    private void HookActions(PlayerMovement p)
    {
        equipment = p.GetComponent<PlayerEquipment>();
        fists = equipment != null && equipment.UnarmedView != null ? equipment.UnarmedView.GetComponent<FistsController>() : null;
        weapons = p.GetComponentsInChildren<WeaponController>(true);
        swings = p.GetComponentsInChildren<PickaxeSwing>(true);
        if (fists != null) fists.Punched += OnLocalPunch;
        foreach (WeaponController w in weapons) { w.ShotFired += OnLocalShot; w.ReloadStarted += OnLocalReload; }
        foreach (PickaxeSwing s in swings) s.SwingStarted += OnLocalSwing;
        if (playerHeadlamp != null)
        {
            playerHeadlamp.Changed += OnLocalHeadlamp;
            headlampOn.Value = playerHeadlamp.IsOn;
        }
    }

    private void UnhookActions()
    {
        if (fists != null) fists.Punched -= OnLocalPunch;
        foreach (WeaponController w in weapons) if (w != null) { w.ShotFired -= OnLocalShot; w.ReloadStarted -= OnLocalReload; }
        foreach (PickaxeSwing s in swings) if (s != null) s.SwingStarted -= OnLocalSwing;
        if (playerHeadlamp != null) playerHeadlamp.Changed -= OnLocalHeadlamp;
        fists = null;
        weapons = new WeaponController[0];
        swings = new PickaxeSwing[0];
    }

    // The local lamp has already switched (no delay for its owner); the others follow.
    private void OnLocalHeadlamp(bool on) { if (IsSpawned && IsOwner) headlampOn.Value = on; }
    private void OnLocalPunch(bool right) { if (IsSpawned) PunchRpc(right); }
    private void OnLocalShot() { if (IsSpawned) ShotRpc(); }
    private void OnLocalReload(float duration) { if (IsSpawned) ReloadRpc(duration); }
    private void OnLocalSwing(int kind, float impact, float end) { if (IsSpawned) SwingRpc((byte)kind, impact, end); }

    private static int ItemIndex(ItemData item)
    {
        if (item == null) return NoItem;
        int i = NetworkWorld.Instance != null ? NetworkWorld.Instance.IndexOf(item) : -1;
        return i >= 0 ? i : NoItem;
    }

    [Rpc(SendTo.NotMe)] private void PunchRpc(bool right) { if (presentation != null) presentation.Punch(right); }
    [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)] private void ShotRpc() { if (presentation != null) presentation.Shot(); }
    [Rpc(SendTo.NotMe)] private void ReloadRpc(float duration) { if (presentation != null) presentation.Reload(duration); }
    [Rpc(SendTo.NotMe)] private void SwingRpc(byte kind, float impact, float end) { if (presentation != null) presentation.Swing(kind, impact, end); }

    // ---------------------------------------------------------------- everyone else: persistent state

    private void OnHeldItemChanged(int oldValue, int newValue)
    {
        if (presentation == null) return;
        ItemData item = newValue >= 0 && NetworkWorld.Instance != null ? NetworkWorld.Instance.ItemAt(newValue) : null;
        heldResolved = newValue < 0 || item != null; // a late joiner may get this before the world (item list) exists
        presentation.SetHeldItem(item, newValue == Carrying);
    }

    private void OnHeadlampChanged(bool oldValue, bool on)
    {
        if (headlamp != null) headlamp.SetOn(on); // the real Spot Light on this body (its glow follows it)
    }

    private void OnLocalJump() => jumps++;

    /// <summary>
    /// The host keeps the map's start point; everyone else gets a spot on a ring around it (by join order),
    /// on the ground and clear of rock. If a spot is blocked, the next one is tried.
    /// </summary>
    private void PlaceAtSpawnSpot(PlayerMovement p)
    {
        int slot = (int)(OwnerClientId % 16);
        if (slot == 0) return;

        var controller = p.GetComponent<CharacterController>();
        float radius = controller != null ? controller.radius : 0.4f;
        float height = controller != null ? controller.height : 1.8f;
        int ignore = (1 << 2) | (1 << 6) | (1 << 7) | (1 << 8); // Ignore Raycast, ViewModel, DroppedItem, Player
        int mask = ~ignore;
        Vector3 start = p.transform.position;

        for (int i = 0; i < 16; i++)
        {
            int s = (slot - 1 + i) % 15;
            float ring = spawnSpacing * (1 + s / 6);
            float angle = (s % 6) * 60f + (s / 6) * 30f;
            Vector3 spot = start + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * ring;
            if (!Physics.Raycast(spot + Vector3.up * 4f, Vector3.down, out RaycastHit ground, 10f, mask, QueryTriggerInteraction.Ignore)) continue;
            Vector3 feet = ground.point + Vector3.up * 0.05f;
            if (Physics.CheckCapsule(feet + Vector3.up * (radius + 0.05f), feet + Vector3.up * (height - radius), radius, mask, QueryTriggerInteraction.Ignore)) continue;
            Teleport(p, feet, controller);
            return;
        }
        Debug.LogWarning("[Ore What] No free spawn spot found; starting at the map's start point.");
    }

    private static void Teleport(PlayerMovement p, Vector3 feet, CharacterController controller)
    {
        // A CharacterController overrides transform moves while enabled.
        if (controller != null) controller.enabled = false;
        p.transform.position = feet;
        if (controller != null) controller.enabled = true;
    }

    // ---------------------------------------------------------------- everyone else

    private void ShowRemote()
    {
        State s = state.Value;
        if (s.ready != visible) SetVisible(s.ready);
        float k = 1f - Mathf.Exp(-smoothing * Time.deltaTime);
        shown.speed = Mathf.Lerp(shown.speed, s.speed, k);
        shown.forwardSpeed = Mathf.Lerp(shown.forwardSpeed, s.forwardSpeed, k);
        shown.walkPlayback = Mathf.Lerp(shown.walkPlayback, s.walkPlayback, k);
        shown.runPlayback = Mathf.Lerp(shown.runPlayback, s.runPlayback, k);
        shown.walkBackPlayback = Mathf.Lerp(shown.walkBackPlayback, s.walkBackPlayback, k);
        shown.verticalSpeed = Mathf.Lerp(shown.verticalSpeed, s.verticalSpeed, k);
        shown.pitch = Mathf.Lerp(shown.pitch, s.pitch, k);
        float crouch = Mathf.Lerp(crouchPose != null ? crouchPose.ExternalAmount : 0f, s.crouch / 255f, k);

        if (body != null && body.isInitialized)
        {
            body.SetFloat(SpeedId, shown.speed);
            body.SetFloat(ForwardSpeedId, shown.forwardSpeed);
            body.SetFloat(WalkPlaybackId, shown.walkPlayback);
            body.SetFloat(RunPlaybackId, shown.runPlayback);
            body.SetFloat(WalkBackPlaybackId, shown.walkBackPlayback);
            body.SetFloat(VerticalSpeedId, shown.verticalSpeed);
            body.SetBool(GroundedId, s.grounded);
            if (s.jumps != shownJumps) { shownJumps = s.jumps; body.SetTrigger(JumpId); }
        }
        if (crouchPose != null) crouchPose.ExternalAmount = crouch;
        if (aim != null) aim.localRotation = Quaternion.Euler(shown.pitch, 0f, 0f);
        if (presentation != null) presentation.Pitch = shown.pitch;
        if (!heldResolved && NetworkWorld.Instance != null) OnHeldItemChanged(NoItem, heldItem.Value);
    }

    private bool visible = true;

    private void SetVisible(bool on)
    {
        visible = on;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = on;
        if (bodyCollider != null) bodyCollider.enabled = on;
    }

    // After the Animator (and before the headlamp, order 95): tilt the upper body with the look.
    private void LateUpdate()
    {
        if (!IsSpawned || IsOwner || body == null || !body.isInitialized) return;
        if (head == null)
        {
            chest = body.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = body.GetBoneTransform(HumanBodyBones.Chest);
            neck = body.GetBoneTransform(HumanBodyBones.Neck);
            head = body.GetBoneTransform(HumanBodyBones.Head);
            if (head == null) return;
        }
        Vector3 axis = transform.right;
        if (chest != null) chest.rotation = Quaternion.AngleAxis(shown.pitch * chestPitch, axis) * chest.rotation;
        if (neck != null) neck.rotation = Quaternion.AngleAxis(shown.pitch * neckPitch, axis) * neck.rotation;
        head.rotation = Quaternion.AngleAxis(shown.pitch * headPitch, axis) * head.rotation;
    }
}
