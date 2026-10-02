using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shooting and reloading for a held gun (pistol, assault rifle...). Put it on the root of the
/// gun's first-person view (e.g. PistolViewModel). PlayerEquipment only activates the view of
/// the selected item, so only the gun in your hands can fire or reload; switching away
/// deactivates it, which stops firing and cancels a reload cleanly.
///
///   Left click    fire (Automatic: hold to keep firing)
///   R             reload
///
/// Ammo: each gun has a magazine (Magazine Capacity rounds). Every shot uses one round; an empty
/// gun doesn't fire (just an optional empty click) until you press R. Spare magazines are
/// infinite: every reload gives a full magazine, and there's no reserve count and no HUD.
/// The rounds left stay with the gun when you switch away and back.
///
/// Reload: the gun tilts, the supporting hand takes the magazine out (it slides out of the gun,
/// then leaves the view with the hand), a short pause with the gun empty, the hand brings a
/// magazine back and pushes it in, the slide/charging handle is racked, the gun settles, and the
/// magazine is full. The magazine is the imported rig's Magazine bone (the mesh follows it fully).
///
/// Recoil: the gun model kicks back and up (Recoil), and the aim kicks too (Aim recoil & spray
/// pattern), shown through CameraRotation (CameraEffects adds it to CameraRoot; PlayerLook's own aim is
/// never changed). Automatic guns follow Spray Pattern one entry per shot fired; single-shot guns add
/// Aim Kick per shot. The aim settles back when you stop firing; the spray restarts after Spray Reset
/// Delay or a reload.
///
/// Each shot casts one ray from the camera, along the crosshair plus the recoil. It never hits the player or the
/// first-person view. What it hits:
///   - an IDamageable (future enemies) takes Damage
///   - a rock (RockHealth) takes Rock Damage through its normal TakeHit
///   - a Rigidbody gets a small push
///   - anything else just registers the hit (the ShotHit event), no errors
///
/// Leave the sound fields empty to use built-in synthesised placeholder sounds.
/// </summary>
// Runs before PlayerLook, so the click that re-locks the cursor after Escape doesn't also fire.
[DefaultExecutionOrder(-10)]
public class WeaponController : HeldItemController
{
    /// <summary>When each part of the reload happens, as a fraction of Reload Duration (0 = start, 1 = end).</summary>
    [Serializable]
    public class ReloadTiming
    {
        [Tooltip("The supporting hand starts reaching for the magazine.")]
        [Range(0f, 1f)] public float handReach = 0.08f;
        [Tooltip("The magazine starts sliding out of the gun.")]
        [Range(0f, 1f)] public float magazineOut = 0.24f;
        [Tooltip("The old magazine (and the hand) have left the view. The gun is shown empty from here...")]
        [Range(0f, 1f)] public float magazineGone = 0.44f;
        [Tooltip("...until here, when the hand brings a new magazine back.")]
        [Range(0f, 1f)] public float newMagazine = 0.56f;
        [Tooltip("The new magazine is pushed fully in.")]
        [Range(0f, 1f)] public float magazineIn = 0.76f;
        [Tooltip("The slide / charging handle is pulled back and released (takes 10% of the reload).")]
        [Range(0f, 1f)] public float rack = 0.82f;
        [Tooltip("The hand is back on its normal grip and the gun starts returning to its normal position.")]
        [Range(0f, 1f)] public float ready = 0.9f;
    }

    [Header("Firing")]
    [Tooltip("Hold the button to keep firing (assault rifle). Off = one shot per click (pistol).")]
    [SerializeField] private bool automatic;
    [Tooltip("Seconds between shots. Pistol: the fastest it can be clicked. Rifle: 0.1 = 10 shots per second (600 RPM).")]
    [SerializeField, Min(0.02f)] private float fireCooldown = 0.15f;
    [Tooltip("Maximum distance a shot can hit, metres.")]
    [SerializeField, Min(1f)] private float range = 100f;
    [Tooltip("Damage per shot to anything with an IDamageable component (future enemies).")]
    [SerializeField, Min(0f)] private float damage = 20f;
    [Tooltip("Damage per shot to rocks (RockHealth; rocks have 5 health). 0 = shots don't break rocks.")]
    [SerializeField, Min(0)] private int rockDamage = 1;
    [Tooltip("Push given to physics objects that are hit (impulse).")]
    [SerializeField, Min(0f)] private float hitForce = 2f;
    [Tooltip("What shots can hit. The Player and ViewModel layers should be left out.")]
    [SerializeField] private LayerMask hitLayers = ~((1 << 6) | (1 << 8));

    [Header("Magazine (spare magazines are infinite)")]
    [Tooltip("Rounds in one full magazine.")]
    [SerializeField, Min(1)] private int magazineCapacity = 12;
    [Tooltip("Allow R while the magazine is still full (off = R does nothing until at least one shot was fired).")]
    [SerializeField] private bool reloadWhenFull;
    [Tooltip("Minimum seconds between empty clicks, so holding/clicking an empty gun can't spam the sound.")]
    [SerializeField, Min(0f)] private float emptyClickCooldown = 0.3f;

    [Header("Reload")]
    [Tooltip("How long a reload takes, seconds. Firing is blocked meanwhile.")]
    [SerializeField, Min(0.3f)] private float reloadDuration = 1.8f;
    [SerializeField] private ReloadTiming reloadTiming = new ReloadTiming();
    [Tooltip("How far the gun moves into the reload pose (camera space, metres). Up and toward the centre keeps the magazine in view.")]
    [SerializeField] private Vector3 reloadOffset = new Vector3(-0.06f, 0.07f, 0.03f);
    [Tooltip("How far the gun tilts in the reload pose (degrees: X = muzzle up, Y = turn inward, Z = roll; negative Z turns the magazine well toward the centre of the view).")]
    [SerializeField] private Vector3 reloadTilt = new Vector3(-12f, -12f, -35f);

    [Header("Recoil")]
    [Tooltip("Gun kick per shot, metres: X = random side-to-side, Y = up, Z = back toward you.")]
    [SerializeField] private Vector3 recoilKick = new Vector3(0.004f, 0.01f, 0.035f);
    [Tooltip("Gun rotation per shot, degrees: X = muzzle up, Y = random left/right.")]
    [SerializeField] private Vector2 recoilRotation = new Vector2(6f, 1.5f);
    [Tooltip("Extra camera jolt per shot, degrees (a short pulse on top of the aim recoil below).")]
    [SerializeField, Min(0f)] private float cameraKick = 0.6f;
    [Tooltip("How fast the gun settles back after a shot. Higher = snappier.")]
    [SerializeField, Min(1f)] private float recoilRecovery = 14f;
    [Tooltip("Automatic fire: most shots' worth of recoil that can stack up. Single shots: also caps how far repeated Aim Kicks climb.")]
    [SerializeField, Min(1f)] private float maxRecoilStack = 2.5f;

    [Header("Aim recoil & spray pattern")]
    [Tooltip("Where shot N of a spray goes, relative to where you aim, degrees (X = right, Y = up); one entry per " +
             "shot actually fired. The view follows it, so the crosshair shows where the bullets go and pulling the " +
             "mouse against it compensates. Past the last entry the spray stays at the last point. " +
             "Empty = no pattern: each shot adds Aim Kick instead (pistol).")]
    [SerializeField] private Vector2[] sprayPattern = new Vector2[0];
    [Tooltip("Without a spray pattern: aim kick per shot, degrees. X = random left/right up to this, Y = up.")]
    [SerializeField] private Vector2 aimKick = new Vector2(0.35f, 1.4f);
    [Tooltip("Small random deviation per shot, degrees. In a spray it grows over the first 4 shots, so the first bullet is exact.")]
    [SerializeField, Min(0f)] private float shotVariation = 0.1f;
    [Tooltip("How quickly the aim settles back after you stop firing (per second; higher = faster).")]
    [SerializeField, Min(0.1f)] private float aimRecovery = 7f;
    [Tooltip("Seconds without firing before the next spray starts again from its first shot.")]
    [SerializeField, Min(0f)] private float sprayResetDelay = 0.35f;
    [Tooltip("How quickly the view follows each kick while firing (per second).")]
    [SerializeField, Min(1f)] private float kickSpeed = 30f;

    /// <summary>The assault rifle's spray (degrees): straight up first, then a drift left, back right, and left again.</summary>
    public static readonly Vector2[] DefaultRifleSpray =
    {
        new Vector2(0f, 0f),       new Vector2(0f, 0.45f),     new Vector2(0.05f, 0.95f),  new Vector2(-0.05f, 1.5f),
        new Vector2(-0.1f, 2.05f), new Vector2(-0.2f, 2.55f),  new Vector2(-0.4f, 3f),     new Vector2(-0.7f, 3.4f),
        new Vector2(-1f, 3.75f),   new Vector2(-1.3f, 4.05f),  new Vector2(-1.5f, 4.3f),   new Vector2(-1.6f, 4.55f),
        new Vector2(-1.5f, 4.75f), new Vector2(-1.2f, 4.95f),  new Vector2(-0.8f, 5.1f),   new Vector2(-0.3f, 5.25f),
        new Vector2(0.2f, 5.4f),   new Vector2(0.7f, 5.5f),    new Vector2(1.1f, 5.6f),    new Vector2(1.4f, 5.7f),
        new Vector2(1.6f, 5.8f),   new Vector2(1.6f, 5.9f),    new Vector2(1.4f, 6f),      new Vector2(1f, 6.05f),
        new Vector2(0.6f, 6.1f),   new Vector2(0.2f, 6.15f),   new Vector2(-0.2f, 6.2f),   new Vector2(-0.6f, 6.25f),
        new Vector2(-0.9f, 6.3f),  new Vector2(-1.1f, 6.35f),
    };

    [Header("Rig parts (from the gun's imported rig)")]
    [Tooltip("Slide (pistol) or bolt (rifle): moves back on each shot.")]
    [SerializeField] private Transform slide;
    [SerializeField, Min(0f)] private float slideTravel = 0.03f;
    [Tooltip("Magazine bone: taken out and put back in during a reload.")]
    [SerializeField] private Transform magazine;
    [Tooltip("How far the magazine slides straight out of the magazine well before the hand carries it away, metres.")]
    [SerializeField, Min(0f)] private float magazineExtract = 0.08f;
    [Tooltip("Length of the magazine below its bone, metres (the hand holds it near the bottom).")]
    [SerializeField, Min(0.01f)] private float magazineLength = 0.1f;
    [Tooltip("Where the hand carries the magazine after pulling it out (camera space, metres; down = out of view).")]
    [SerializeField] private Vector3 magazineStowOffset = new Vector3(-0.06f, -0.24f, -0.06f);
    [Tooltip("Charging handle (rifle): pulled back at the end of a reload. Empty = the slide is racked instead.")]
    [SerializeField] private Transform chargingHandle;
    [SerializeField, Min(0f)] private float chargingTravel = 0.05f;
    [Tooltip("The supporting hand's grip point (LeftHandGrip under Weapon Root). It takes the magazine out and puts it back.")]
    [SerializeField] private Transform supportHandGrip;

    [Header("Muzzle flash")]
    [Tooltip("Particles emitted at the muzzle on each shot (optional).")]
    [SerializeField] private ParticleSystem muzzleFlash;
    [SerializeField, Range(1, 5)] private int muzzleFlashParticles = 2;
    [Tooltip("Light flashed at the muzzle on each shot (optional).")]
    [SerializeField] private Light muzzleLight;
    [SerializeField, Min(0.01f)] private float muzzleLightTime = 0.05f;

    [Header("Impact")]
    [Tooltip("Material for the little debris burst where a shot lands. Empty = no burst.")]
    [SerializeField] private Material impactMaterial;
    [SerializeField, Range(0, 20)] private int impactParticles = 6;

    [Header("Sound (empty = built-in placeholder sounds)")]
    [SerializeField] private AudioClip fireSound;
    [SerializeField, Range(0f, 1f)] private float fireVolume = 0.8f;
    [Tooltip("Random pitch per shot, so repeated shots don't sound identical.")]
    [SerializeField] private Vector2 firePitch = new Vector2(0.95f, 1.05f);
    [Tooltip("Played when the trigger is pulled on an empty magazine.")]
    [SerializeField] private AudioClip emptySound;
    [SerializeField, Range(0f, 1f)] private float emptyVolume = 0.6f;
    [Tooltip("Reload: the magazine leaves the gun (played at Reload Timing > Magazine Out).")]
    [SerializeField] private AudioClip magazineOutSound;
    [Tooltip("Reload: the new magazine clicks in (played at Reload Timing > Magazine In).")]
    [SerializeField] private AudioClip magazineInSound;
    [Tooltip("Reload: slide / charging handle racked (played at Reload Timing > Rack).")]
    [SerializeField] private AudioClip rackSound;
    [SerializeField, Range(0f, 1f)] private float reloadVolume = 0.75f;
    [Tooltip("Played when the gun is taken out (selected).")]
    [SerializeField] private AudioClip equipSound;
    [SerializeField, Range(0f, 1f)] private float equipVolume = 0.5f;

    [Header("References (auto-found if empty)")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("The transform that holds the gun and the hand grip points (WeaponHolder). Recoil and reload move it.")]
    [SerializeField] private Transform weaponRoot;

    /// <summary>Raised on every shot, hit or miss.</summary>
    public event Action ShotFired;
    /// <summary>Raised when a reload starts, with its duration (seconds). Used to show it to other players.</summary>
    public event Action<float> ReloadStarted;
    /// <summary>Raised when a shot hits something (anything with a collider).</summary>
    public event Action<RaycastHit> ShotHit;

    public bool IsReloading => reloading;
    public bool Automatic => automatic;
    /// <summary>Rounds left in the current magazine (tracked internally; not shown on the HUD).</summary>
    public int CurrentAmmo => currentAmmo;
    public int MagazineCapacity => magazineCapacity;
    /// <summary>How far through the current reload (0-1), or 0 when not reloading.</summary>
    public float ReloadProgress => reloading ? reloadT : 0f;
    /// <summary>Shots fired since this gun was taken out (for testing/debugging).</summary>
    public int ShotsFired { get; private set; }
    /// <summary>Shots in the current spray (0 = the next shot starts a new spray).</summary>
    public int SprayIndex => sprayIndex;
    /// <summary>Where the gun's recoil currently puts the aim, degrees (X = right, Y = up).</summary>
    public Vector2 AimRecoil => aimRecoil;
    /// <summary>Direction of the last shot fired (for testing/debugging).</summary>
    public Vector3 LastShotDirection { get; private set; }
    /// <summary>Switching or throwing is always allowed: it simply cancels a reload.</summary>
    public override bool IsBusy => false;
    public override Vector3 CameraRotation => cameraRot;

    private Transform playerRoot;
    private AudioSource fireSource, reloadSource;
    private ParticleSystem impactFX;
    private Vector3 rootRestPos; private Quaternion rootRestRot;
    private Vector3 handRestPos;
    private Vector3 slideRest, magRest, chargeRest;
    private Vector3 slideDirModel, magDirModel; // in the gun's own space, captured at rest
    private float nextShotTime, nextEmptyClick, recoil, recoilSide, slideKick, lightTimer;
    private int currentAmmo;
    private bool reloading, triggerArmed;
    private float reloadT;
    private Vector3 cameraRot;
    private Vector2 aimRecoil, shownRecoil; // degrees, X = right, Y = up: target / what the view shows
    private int sprayIndex;
    private float lastShotTime = -10f;
    private readonly RaycastHit[] hits = new RaycastHit[16];

    private void Awake()
    {
        if (playerCamera == null) playerCamera = GetComponentInParent<Camera>();
        if (weaponRoot == null) weaponRoot = transform.Find("WeaponHolder");
        if (supportHandGrip == null && weaponRoot != null) supportHandGrip = weaponRoot.Find("LeftHandGrip");
        playerRoot = transform.root;
        currentAmmo = magazineCapacity; // the gun starts with a full magazine

        CaptureRest();
        if (muzzleLight != null) muzzleLight.enabled = false;

        // Sounds come from the gun itself (it sits just in front of the camera/listener).
        GameObject audioHost = weaponRoot != null ? weaponRoot.gameObject : gameObject;
        fireSource = CreateSource(audioHost);
        reloadSource = CreateSource(audioHost);
        if (fireSound == null) fireSound = WeaponSounds.Shot(automatic);
        if (emptySound == null) emptySound = WeaponSounds.Click(3200f, 0.35f);
        if (magazineOutSound == null) magazineOutSound = WeaponSounds.MagazineOut();
        if (magazineInSound == null) magazineInSound = WeaponSounds.MagazineIn();
        if (rackSound == null) rackSound = WeaponSounds.Rack(chargingHandle != null);
        if (equipSound == null) equipSound = WeaponSounds.Equip();
        if (impactMaterial != null) impactFX = CreateImpactFX(impactMaterial);
    }

    /// <summary>Remembers the gun, hand grip and rig bones at rest (what recoil and the reload move from).</summary>
    private void CaptureRest()
    {
        if (weaponRoot != null) { rootRestPos = weaponRoot.localPosition; rootRestRot = weaponRoot.localRotation; }
        if (supportHandGrip != null) handRestPos = supportHandGrip.localPosition;
        if (slide != null) { slideRest = slide.localPosition; slideDirModel = Vector3.back; }
        if (magazine != null) { magRest = magazine.localPosition; magDirModel = weaponRoot.InverseTransformDirection(magazine.up); }
        if (chargingHandle != null) chargeRest = chargingHandle.localPosition;
    }

#if UNITY_EDITOR
    /// <summary>The rig bones the reload moves (magazine, slide, charging handle), for the multiplayer setup tool.</summary>
    public Transform[] ReloadBones => new[] { magazine, slide, chargingHandle };

    /// <summary>
    /// Editor only, used to bake what other players see of this reload: poses the gun, support-hand grip and rig bones
    /// exactly as the reload looks at progress t (0..1), through the same ApplyPose the game uses. Call on the gun at rest;
    /// the caller restores the transforms afterwards.
    /// </summary>
    public void PreviewReloadPose(float t)
    {
        if (weaponRoot == null) weaponRoot = transform.Find("WeaponHolder");
        if (supportHandGrip == null && weaponRoot != null) supportHandGrip = weaponRoot.Find("LeftHandGrip");
        CaptureRest();
        recoil = slideKick = 0f;
        reloading = true;
        reloadT = Mathf.Clamp01(t);
        ApplyPose();
        reloading = false;
        reloadT = 0f;
    }
#endif

    private static AudioSource CreateSource(GameObject host)
    {
        var s = host.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0.85f; // positional (from the gun), but never quiet
        s.minDistance = 1f;
        s.maxDistance = 30f;
        return s;
    }

    private void OnEnable()
    {
        triggerArmed = false; // a button still held from before must be released first
        ShotsFired = 0;
        if (equipSound != null && fireSource != null) fireSource.PlayOneShot(equipSound, equipVolume);
    }

    /// <summary>
    /// Put away (switched, thrown): stop firing, cancel a reload (the magazine keeps the rounds it
    /// had; only a finished reload refills it) and put the gun, magazine and hand back to rest.
    /// </summary>
    private void OnDisable()
    {
        reloading = false;
        reloadT = 0f;
        recoil = slideKick = 0f;
        cameraRot = Vector3.zero;
        aimRecoil = shownRecoil = Vector2.zero;
        sprayIndex = 0;
        lastShotTime = -10f;
        if (reloadSource != null) reloadSource.Stop();
        if (muzzleLight != null) muzzleLight.enabled = false;
        ApplyPose();
    }

    private void OnDestroy()
    {
        if (impactFX != null) Destroy(impactFX.gameObject);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        if (locked && keyboard != null && keyboard.rKey.wasPressedThisFrame) StartReload();

        if (mouse != null && locked)
        {
            if (!mouse.leftButton.isPressed) triggerArmed = true;
            bool pull = automatic ? mouse.leftButton.isPressed : mouse.leftButton.wasPressedThisFrame;
            if (pull && triggerArmed && !reloading)
            {
                if (currentAmmo > 0) { if (Time.time >= nextShotTime) Fire(); }
                else if (mouse.leftButton.wasPressedThisFrame) DryFire(); // empty: a click per press, never a shot
            }
        }

        if (reloading) AdvanceReload(dt);

        // Recoil settles back; the slide snaps forward quickly.
        float settle = 1f - Mathf.Exp(-recoilRecovery * dt);
        recoil = Mathf.Lerp(recoil, 0f, settle);
        slideKick = Mathf.MoveTowards(slideKick, 0f, dt / 0.07f);

        // Aim recoil: the view follows each kick quickly while firing, then settles back to your aim.
        bool firing = Time.time - lastShotTime < fireCooldown + 0.05f;
        if (!firing) aimRecoil = Vector2.zero;
        if (Time.time - lastShotTime > sprayResetDelay) sprayIndex = 0;
        shownRecoil = Vector2.Lerp(shownRecoil, aimRecoil, 1f - Mathf.Exp(-(firing ? kickSpeed : aimRecovery) * dt));
        // CameraEffects adds this to CameraRoot (X = pitch, negative = up; Y = yaw). PlayerLook is untouched.
        cameraRot = new Vector3(-shownRecoil.y - cameraKick * recoil, shownRecoil.x + cameraKick * 0.3f * recoil * recoilSide, 0f);

        if (muzzleLight != null && muzzleLight.enabled)
        {
            lightTimer -= dt;
            if (lightTimer <= 0f) muzzleLight.enabled = false;
        }
    }

    // After the view's idle/bob motion (Update), before the hand IK (LateUpdate, order 100).
    private void LateUpdate() => ApplyPose();

    /// <summary>
    /// Starts a reload of this gun. Ignored while already reloading, and (unless Reload When Full)
    /// while the magazine is full. Spare magazines are infinite, so it never fails for lack of ammo.
    /// </summary>
    public void StartReload()
    {
        if (reloading) return; // never two at once
        if (currentAmmo >= magazineCapacity && !reloadWhenFull) return;
        reloading = true;
        reloadT = 0f;
        sprayIndex = 0;      // a new magazine starts a new spray
        lastShotTime = -10f; // and the recoil settles during the reload
        if (reloadSource != null) reloadSource.Stop();
        ReloadStarted?.Invoke(reloadDuration);
    }

    /// <summary>Moves the reload on, plays each reload sound as its moment is reached, refills at the end.</summary>
    private void AdvanceReload(float dt)
    {
        float before = reloadT;
        reloadT += dt / reloadDuration;
        ReloadTiming k = reloadTiming;
        PlayAt(before, reloadT, k.magazineOut + 0.02f, magazineOutSound);
        PlayAt(before, reloadT, k.magazineIn, magazineInSound);
        PlayAt(before, reloadT, k.rack, rackSound);
        if (reloadT >= 1f)
        {
            reloading = false;
            reloadT = 0f;
            currentAmmo = magazineCapacity; // a fresh, full magazine
        }
    }

    private void PlayAt(float before, float now, float at, AudioClip clip)
    {
        if (clip == null || reloadSource == null || before >= at || now < at) return;
        reloadSource.pitch = 1f;
        reloadSource.PlayOneShot(clip, reloadVolume);
    }

    private void Fire()
    {
        nextShotTime = Time.time + fireCooldown;
        currentAmmo--;
        ShotsFired++;

        // Where this shot goes, relative to the centre of the screen (degrees, X = right, Y = up).
        Vector2 offset;
        Vector2 shown = new Vector2(cameraRot.y, -cameraRot.x); // what this gun's recoil already turned the view by
        if (sprayPattern != null && sprayPattern.Length > 0)
        {
            // Spray: shot N lands at pattern[N] from your aim. The view already shows part of that, so only the rest is added.
            Vector2 point = sprayPattern[Mathf.Min(sprayIndex, sprayPattern.Length - 1)];
            point += UnityEngine.Random.insideUnitCircle * (shotVariation * Mathf.Clamp01(sprayIndex / 4f));
            aimRecoil = point;
            offset = point - shown;
        }
        else
        {
            // Single shots: this bullet goes where the crosshair is (plus a tiny variation); the kick comes after.
            offset = UnityEngine.Random.insideUnitCircle * shotVariation;
            aimRecoil = shownRecoil + new Vector2(UnityEngine.Random.Range(-aimKick.x, aimKick.x), aimKick.y);
            aimRecoil.y = Mathf.Min(aimRecoil.y, aimKick.y * maxRecoilStack);
        }
        sprayIndex++;
        lastShotTime = Time.time;

        // Hit detection: one ray from the camera; skip anything that belongs to the player.
        Transform eye = playerCamera.transform;
        Vector3 direction = Quaternion.AngleAxis(offset.x, eye.up) * Quaternion.AngleAxis(-offset.y, eye.right) * eye.forward;
        Ray ray = new Ray(eye.position, direction);
        LastShotDirection = direction;
        int count = Physics.RaycastNonAlloc(ray, hits, range, hitLayers, QueryTriggerInteraction.Ignore);
        int best = -1;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(playerRoot)) continue;
            if (best < 0 || hits[i].distance < hits[best].distance) best = i;
        }
        if (best >= 0) RegisterHit(hits[best], ray.direction);

        // Feedback.
        recoil = Mathf.Min(recoil + 1f, maxRecoilStack);
        recoilSide = UnityEngine.Random.Range(-1f, 1f);
        slideKick = 1f;
        if (muzzleFlash != null) muzzleFlash.Emit(muzzleFlashParticles);
        if (muzzleLight != null) { muzzleLight.enabled = true; lightTimer = muzzleLightTime; }
        if (fireSound != null)
        {
            fireSource.pitch = UnityEngine.Random.Range(firePitch.x, firePitch.y);
            fireSource.PlayOneShot(fireSound, fireVolume);
        }
        ShotFired?.Invoke();
    }

    /// <summary>Trigger pulled on an empty magazine: just a click (rate-limited), no shot, no auto-reload.</summary>
    private void DryFire()
    {
        if (Time.time < nextEmptyClick) return;
        nextEmptyClick = Time.time + emptyClickCooldown;
        if (emptySound != null) { fireSource.pitch = 1f; fireSource.PlayOneShot(emptySound, emptyVolume); }
    }

    private void RegisterHit(RaycastHit hit, Vector3 direction)
    {
        var target = hit.collider.GetComponentInParent<IDamageable>();
        if (target != null) target.TakeDamage(damage, hit);
        else if (rockDamage > 0)
        {
            var rock = hit.collider.GetComponentInParent<RockHealth>();
            if (rock != null) rock.TakeHit(rockDamage);
        }
        if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
            hit.rigidbody.AddForceAtPosition(direction * hitForce, hit.point, ForceMode.Impulse);

        if (impactFX != null && impactParticles > 0)
        {
            impactFX.transform.SetPositionAndRotation(hit.point + hit.normal * 0.01f, Quaternion.LookRotation(hit.normal));
            impactFX.Emit(impactParticles);
        }
        ShotHit?.Invoke(hit);
    }

    /// <summary>
    /// Recoil + reload on the weapon root (the hands follow through their grip points), the
    /// magazine bone and the supporting hand's grip point, and the slide / charging handle.
    /// </summary>
    private void ApplyPose()
    {
        if (weaponRoot == null) return;
        float t = reloading ? reloadT : 0f;
        ReloadTiming k = reloadTiming;

        // Phase 1 / 5: into the reload pose, back to the normal position at the end.
        float pose = reloading ? Smooth(t, 0f, k.handReach + 0.08f) * (1f - Smooth(t, k.ready, 1f)) : 0f;
        Vector3 pos = reloadOffset * pose + new Vector3(recoilKick.x * recoilSide, recoilKick.y, -recoilKick.z) * recoil;
        Vector3 rot = reloadTilt * pose + new Vector3(-recoilRotation.x, recoilRotation.y * recoilSide, 0f) * recoil;
        weaponRoot.localPosition = rootRestPos + pos;
        weaponRoot.localRotation = Quaternion.Euler(rot) * rootRestRot;

        // Phases 2-4: the magazine slides out along its own axis, then leaves the view with the hand;
        // after a pause it comes back, lines up under the magazine well and slides in.
        float extract = 0f, stow = 0f;
        if (reloading)
        {
            if (t >= k.magazineOut && t < k.magazineGone)
            {
                float u = Mathf.InverseLerp(k.magazineOut, k.magazineGone, t);
                extract = Smooth(u, 0f, 0.45f);
                stow = Smooth(u, 0.35f, 1f);
            }
            else if (t >= k.magazineGone && t < k.newMagazine) { extract = 1f; stow = 1f; }
            else if (t >= k.newMagazine && t < k.magazineIn)
            {
                float u = Mathf.InverseLerp(k.newMagazine, k.magazineIn, t);
                stow = 1f - Smooth(u, 0f, 0.65f);
                extract = 1f - Smooth(u, 0.6f, 1f);
            }
        }
        Vector3 magAxis = magazine != null ? weaponRoot.TransformDirection(magDirModel) : Vector3.down;
        Transform view = weaponRoot.parent != null ? weaponRoot.parent : weaponRoot;
        if (magazine != null)
        {
            Vector3 rest = magazine.parent != null ? magazine.parent.TransformPoint(magRest) : magRest;
            magazine.position = rest + magAxis * (magazineExtract * extract) + view.TransformVector(magazineStowOffset) * stow;
        }

        // The supporting hand reaches the bottom of the magazine, carries it out and back in, then returns.
        if (supportHandGrip != null)
        {
            float hold = 0f;
            if (reloading && magazine != null)
                hold = Smooth(t, k.handReach, k.magazineOut) * (1f - Smooth(t, k.magazineIn + 0.02f, k.ready));
            Vector3 restWorld = weaponRoot.TransformPoint(handRestPos);
            Vector3 magBottom = magazine != null ? magazine.position + magAxis * (magazineLength * 0.8f) : restWorld;
            supportHandGrip.position = Vector3.Lerp(restWorld, magBottom, hold);
        }

        // Slide/bolt: kicks back on each shot; the slide or charging handle is racked near the end of a reload.
        float rack = reloading ? Mathf.Sin(Mathf.PI * Smooth(t, k.rack, k.rack + 0.1f)) : 0f;
        if (slide != null)
            MoveBone(slide, slideRest, slideDirModel, slideTravel * Mathf.Max(slideKick, chargingHandle == null ? rack : 0f));
        if (chargingHandle != null)
            MoveBone(chargingHandle, chargeRest, slideDirModel, chargingTravel * rack);
    }

    /// <summary>Offsets a rig bone by a distance (metres) along a direction given in the gun's space.</summary>
    private void MoveBone(Transform bone, Vector3 rest, Vector3 dirModel, float distance)
    {
        Vector3 worldOffset = weaponRoot.TransformDirection(dirModel) * distance;
        bone.localPosition = rest + (bone.parent != null ? bone.parent.InverseTransformVector(worldOffset) : worldOffset);
    }

    private static float Smooth(float t, float a, float b) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));

    private static ParticleSystem CreateImpactFX(Material material)
    {
        var go = new GameObject("WeaponImpactFX");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.33f, 0.3f), new Color(0.6f, 0.58f, 0.55f));
        main.gravityModifier = 1f;
        main.maxParticles = 100;
        var emission = ps.emission; emission.enabled = false; // Emit() per hit
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 35f; shape.radius = 0.01f;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.sharedMaterial = material;
        return ps;
    }
}

/// <summary>
/// Placeholder gun sounds, synthesised once at startup (so the guns make noise before real
/// audio files are imported). Assign real clips on the WeaponController to replace them.
/// </summary>
public static class WeaponSounds
{
    private const int Rate = 44100;

    /// <summary>A gunshot: sharp crack + low thump + short noisy tail. Rifle = heavier and longer.</summary>
    public static AudioClip Shot(bool rifle)
    {
        float length = rifle ? 0.45f : 0.32f;
        var data = new float[Mathf.CeilToInt(Rate * length)];
        var rng = new System.Random(rifle ? 71 : 37);
        float low = 0f, tail = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += (white - low) * 0.3f;
            tail += (white - tail) * 0.05f;
            float crack = white * Mathf.Exp(-t * (rifle ? 70f : 90f));
            float body = low * Mathf.Exp(-t * (rifle ? 22f : 30f));
            float thumpHz = rifle ? 55f : 80f;
            float thump = Mathf.Sin(2f * Mathf.PI * (thumpHz + 90f * Mathf.Exp(-t * 60f)) * t) * Mathf.Exp(-t * (rifle ? 14f : 20f));
            float echo = tail * Mathf.Exp(-t * 7f) * 2.5f;
            data[i] = (crack * 0.55f + body * 1.1f + thump * 0.9f + echo) * Mathf.Clamp01(t / 0.0004f);
        }
        return Finish(rifle ? "RifleShot (synth)" : "PistolShot (synth)", data, 0.95f);
    }

    /// <summary>A single dry metal click (empty trigger).</summary>
    public static AudioClip Click(float ringHz, float gain)
    {
        var data = new float[Mathf.CeilToInt(Rate * 0.08f)];
        AddClick(data, 0f, ringHz, gain, new System.Random(3));
        return Finish("Click (synth)", data, 0.6f);
    }

    /// <summary>Magazine release click + the magazine sliding out.</summary>
    public static AudioClip MagazineOut()
    {
        var data = new float[Mathf.CeilToInt(Rate * 0.3f)];
        var rng = new System.Random(5);
        AddClick(data, 0f, 1400f, 0.6f, rng);
        AddSlide(data, 0.02f, 0.2f, 0.18f, rng);
        return Finish("MagazineOut (synth)", data, 0.75f);
    }

    /// <summary>The magazine sliding in and seating with a solid click.</summary>
    public static AudioClip MagazineIn()
    {
        var data = new float[Mathf.CeilToInt(Rate * 0.25f)];
        var rng = new System.Random(6);
        AddSlide(data, 0f, 0.1f, 0.15f, rng);
        AddClick(data, 0.09f, 1900f, 1f, rng);
        return Finish("MagazineIn (synth)", data, 0.85f);
    }

    /// <summary>Slide / charging handle pulled back and released.</summary>
    public static AudioClip Rack(bool chargingHandle)
    {
        var data = new float[Mathf.CeilToInt(Rate * 0.25f)];
        var rng = new System.Random(7);
        AddClick(data, 0f, chargingHandle ? 2300f : 2600f, 0.7f, rng);
        AddSlide(data, 0.01f, 0.07f, 0.12f, rng);
        AddClick(data, 0.1f, chargingHandle ? 1700f : 2100f, 0.9f, rng);
        return Finish("Rack (synth)", data, 0.8f);
    }

    /// <summary>A short handling sound: cloth rustle + a light metal tap.</summary>
    public static AudioClip Equip()
    {
        var data = new float[Mathf.CeilToInt(Rate * 0.3f)];
        var rng = new System.Random(9);
        AddSlide(data, 0f, 0.18f, 0.25f, rng);
        AddClick(data, 0.16f, 2200f, 0.4f, rng);
        return Finish("Equip (synth)", data, 0.6f);
    }

    /// <summary>Metallic click: noise snap + a short ring.</summary>
    private static void AddClick(float[] data, float at, float ringHz, float gain, System.Random rng)
    {
        int start = Mathf.Clamp((int)(at * Rate), 0, data.Length);
        int n = Mathf.Min((int)(0.06f * Rate), data.Length - start);
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += (white - low) * 0.5f;
            float snap = low * Mathf.Exp(-t * 180f);
            float ring = (Mathf.Sin(2f * Mathf.PI * ringHz * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * ringHz * 1.53f * t)) * Mathf.Exp(-t * 70f) * 0.35f;
            data[start + i] += (snap + ring) * gain;
        }
    }

    /// <summary>Soft scraping noise (a magazine sliding, cloth).</summary>
    private static void AddSlide(float[] data, float at, float length, float gain, System.Random rng)
    {
        int start = Mathf.Clamp((int)(at * Rate), 0, data.Length);
        int n = Mathf.Min((int)(length * Rate), data.Length - start);
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)n;
            float white = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += (white - low) * 0.15f;
            data[start + i] += low * Mathf.Sin(Mathf.PI * u) * gain;
        }
    }

    private static AudioClip Finish(string name, float[] data, float peakTarget)
    {
        float peak = 0f;
        foreach (float s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
        if (peak > 0f) for (int i = 0; i < data.Length; i++) data[i] *= peakTarget / peak;
        AudioClip clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
