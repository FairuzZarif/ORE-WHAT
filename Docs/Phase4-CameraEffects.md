# Ore What: Phase 4, Camera Head Motion

> **Updated in Phase 5:** jump, fall and landing are now state-based (`PlayerMotionState`) with a spring landing that compresses, rebounds and settles. The mining reaction only fires on real hits. The Jump and Landing fields below have changed; see [Phase5-GripSwingImpactLanding.md](Phase5-GripSwingImpactLanding.md#5-jump-fall-and-landing-playermotionstate-on-player). Walk/run bob, idle, sway and shake are unchanged.

The camera now moves like it's attached to a head: a subtle bob when walking, a stronger and faster bob when running, a gentle lift and sink when jumping and falling, a short dip on landing that gets bigger the harder you land, and a slight lean into strafes. Standing still, it's almost perfectly stable.

Movement, jump height, gravity, mouse look, and the pickaxe/mining logic are **unchanged**.

---

## 1. Quick start

- **The test scene is already set up.** Press **Play**, then walk, sprint, strafe, and jump.
- **To add the effects to another scene's player**, click **Ore What → Add Camera Effects**. It's safe to run more than once.
- **Ore What → Build Player Test Scene** now includes the effects automatically.
- **Tuning:** select **Player → CameraRoot** and adjust the **Camera Effects** component. Changes apply live in Play mode, but Unity resets them when you stop, so copy the numbers down, then paste them back in after stopping.

---

## 2. What changed

| File | Change |
|---|---|
| `Scripts/Player/CameraEffects.cs` | **New.** All head-motion effects, combined into one offset and applied once per frame |
| `Scripts/Editor/CameraEffectsSetupTool.cs` | **New.** Menu item that inserts `CameraRoot` and adds `CameraEffects` |
| `Scripts/Player/PlayerMovement.cs` | Added two **read-only** properties, `WalkSpeed` and `SprintSpeed`, so the bob scales with your real speeds. No behaviour changed. |
| `Scripts/Editor/PrototypeSceneBuilder.cs` | Calls the new setup tool when building the test scene |

Not touched: `PlayerLook`, `PickaxeSwing`, `MiningController`, `RockHealth`, `CameraShake`, `ViewModelMotion`, `FirstPersonArmsIK`.

---

## 3. Hierarchy: why a CameraRoot?

```
Player                      CharacterController, PlayerMovement, PlayerLook, ...
└── CameraRoot   (y 1.6)    CameraEffects     ← head motion (this script only moves CameraRoot)
    └── PlayerCamera        Camera, CameraShake   ← PlayerLook sets its pitch every frame
        ├── FirstPersonViewModel …
        └── ViewModelCamera
```

`PlayerLook` overwrites `PlayerCamera`'s rotation every frame. If head motion were applied to the same object, the two would fight. So the camera got a parent:
- **`CameraRoot`** holds the eye height and the head motion.
- **`PlayerCamera`** sits at zero offset inside it and keeps receiving mouse look exactly as before.

Nothing writes to the same transform twice. `CameraRoot`'s position and rotation when the game starts are the neutral pose.

The arms and pickaxe are children of the camera, so they move with the head, as a real viewmodel would. `ViewModelMotion`'s own small walk bob still adds a slight hand lag on top.

---

## 4. How the effects are layered

Every frame, in `LateUpdate` (after `PlayerMovement` has moved the player):

```
Continuous layers (added, then smoothed with SmoothDamp):
    walk/run bob  +  idle breathing  +  jump/fall offset  +  directional sway

Impulse layers (added on top, NOT smoothed, so they stay snappy):
    landing impact  +  mining nod  +  Shake() (damage hook)

CameraRoot = neutral pose + (sum × Global Intensity)      ← applied once
```

### Where the movement state comes from (no duplicate detection)

| State | Source |
|---|---|
| Grounded | `PlayerMovement.IsGrounded` |
| Running | `PlayerMovement.IsSprinting` |
| Speed, direction, vertical speed | `CharacterController.velocity` (actual movement) |
| Jumping / falling | not grounded, and vertical speed > 0 / < 0 |
| Landing | `IsGrounded` switching false → true; strength comes from the fall speed recorded just before |

### Each effect

- **Walk/run bob.**
  - One vertical dip per step and one side-to-side sway per two steps. The side-to-side runs at half the vertical rhythm, like the user example.
  - A small extra harmonic makes each footfall land a little harder than it lifts, so it doesn't look like a pure sine wave.
  - The head rolls with the weight shift and nods very slightly on each step.
  - Walking and running values are **blended smoothly** (`Walk Run Blend Time`), never switched.
  - The step cycle only advances while you're actually moving, so starting to walk again resumes smoothly.
- **Idle.** Very slow breathing (about 3 mm) and a tiny Perlin-noise drift. It fades out as soon as you move.
- **Jump / fall.** The offset follows your **actual vertical speed**: it lifts while rising, fades to nothing at the top of the jump, and sinks slightly while falling. Walking bob is off while airborne.
- **Landing.** A quick dip and nod lasting about 0.18 s (fast down, slower recovery), with a slight random roll.
  - Strength = fall speed mapped from **Min Landing Speed** (3 m/s, ignored below) to **Max Landing Strength** (16 m/s, clamped above).
  - Even a small hop is felt a little.
- **Directional sway.**
  - Strafing right tilts the head right and shifts it slightly right, and strafing left mirrors that.
  - Speeding up makes the head lag back a touch; stopping makes it lean forward a touch.
  - Everything is smoothed by **Sway Smoothness**.
- **Mining nod.** A 0.4° nod at the moment the pickaxe strike lands, taken from `PickaxeSwing.ImpactReached`. It adds to the existing tiny impact shake.
- **Damage hook.** Call `GetComponentInChildren<CameraEffects>().Shake(0.5f)` (0–1) from a future damage system. It's a decaying Perlin shake controlled by **Shake Max Angle** and **Shake Decay**.

---

## 5. Measured results (default settings)

These were recorded in Play mode with simulated keyboard input, sampling the camera every frame:

| State | Measured |
|---|---|
| Standing still | < 0.1 mm, 0.01° (stable) |
| Walking (4 m/s) | ±1.8 cm vertical, ±1.5 cm sideways, ±0.6° roll, about 2 dips/s |
| Running (7 m/s) | ±3 cm vertical, ±2.4 cm sideways, ±1.1° roll, about 2.5 dips/s |
| Stopping from a run | settles to neutral in about 0.5 s |
| Strafing right | tilts about 1° right, shifts about 1 cm |
| Normal jump | +3 cm rising, −2.3 cm falling |
| Landing a jump (6.9 m/s) | 3.6 cm dip, 1° nod, recovered in 0.17 s |
| Landing a 4 m drop (12.8 m/s) | 7.8 cm dip, 2.4° nod, recovered in 0.17 s |

---

## 6. Inspector reference (`CameraRoot → Camera Effects`)

| Section | Field | Default | Meaning |
|---|---|---|---|
| General | **Global Intensity** | 1 | Scales everything (0 = off) |
| | Position Smoothness | 0.06 | Seconds; higher = softer bob/sway |
| | Rotation Smoothness | 0.08 | Seconds |
| Walking | **Walk Bob Frequency** | 1.8 | Steps per second |
| | **Walk Bob Vertical Amplitude** | 0.025 | Metres per step |
| | Walk Bob Horizontal Amplitude | 0.015 | Metres side to side |
| | Walk Rotation Amount | 0.6 | Degrees of roll |
| Running | **Run Bob Frequency** | 2.5 | |
| | **Run Bob Vertical Amplitude** | 0.045 | |
| | Run Bob Horizontal Amplitude | 0.025 | |
| | Run Rotation Amount | 1.2 | |
| | Walk Run Blend Time | 0.25 | Seconds to blend walk ↔ run |
| Idle | Breathing Amount | 0.003 | Metres |
| | Breathing Speed | 0.22 | Breaths per second |
| | Idle Sway | 0.12 | Degrees of slow drift |
| Jump | Jump Camera Amount | 0.006 | Metres per m/s of upward speed |
| | Falling Amount | 0.004 | Metres per m/s of fall speed |
| | Max Jump Offset | 0.05 | Limit, metres |
| Landing | **Landing Shake Amount** | 0.07 | Metres of dip on the hardest landing |
| | **Landing Rotation Amount** | 2.5 | Degrees of nod on the hardest landing |
| | Landing Shake Duration | 0.18 | Seconds |
| | Min Landing Speed | 3 | m/s; slower landings are ignored |
| | Max Landing Strength | 16 | m/s that gives full strength (clamp) |
| Movement Sway | Forward Back Amount | 0.0015 | Metres per m/s² |
| | Sideways Amount | 0.002 | Metres per m/s |
| | **Sway Rotation Amount** | 0.35 | Degrees of tilt per m/s of strafe |
| | Sway Smoothness | 0.15 | Seconds |
| Mining | Mining Impact Nod | 0.4 | Degrees (0 = off) |
| Shake | Shake Max Angle / Shake Decay | 2 / 3 | For `Shake()` |

### If it's too weak, raise these first

1. **Global Intensity** (try 1.3)
2. **Walk / Run Bob Vertical Amplitude**
3. **Walk / Run Rotation Amount** (roll is what you "feel" most)
4. **Landing Shake Amount** and **Landing Rotation Amount**
5. **Sway Rotation Amount**

### If it's too much, lower these

1. **Run Bob Vertical Amplitude** and **Run Rotation Amount**. Running is the most likely to cause motion sickness.
2. **Walk/Run Bob Horizontal Amplitude**. Side-to-side is the most noticeable while aiming.
3. **Landing Rotation Amount** (the nod)
4. **Global Intensity** to scale everything down at once (0.6–0.8), or 0 to turn it all off. That's good for an accessibility option later.
5. Raise **Position / Rotation Smoothness** to soften everything without making it smaller.

---

## 7. Test checklist

- [ ] Standing still, the view is steady (only barely perceptible breathing)
- [ ] Walking has a gentle rhythmic bob with a slight side-to-side roll
- [ ] Sprinting is clearly bouncier and faster, and blends in without a jump
- [ ] Releasing Shift or W settles the camera smoothly back to neutral
- [ ] Jumping: no walking bob in the air; a soft lift going up and a slight sink coming down
- [ ] Landing from a jump: small, quick dip. Dropping off the tallest blue step: noticeably bigger, but still short
- [ ] Strafing with A/D tilts the head slightly into the direction of movement
- [ ] Mouse look still works exactly as before, and the crosshair doesn't drift while standing still
- [ ] Mining still works; the strike gets a very small nod
- [ ] Setting **Global Intensity** to 0 makes the camera completely static

## 8. Troubleshooting

| Problem | Fix |
|---|---|
| No effect at all | Check that `CameraEffects` is on **CameraRoot** and that **Global Intensity** is above 0. Run **Ore What → Add Camera Effects**. |
| Camera snaps back to a strange height | `CameraRoot`'s position at Play time is the neutral pose. Keep it at (0, 1.6, 0) and don't move `PlayerCamera` inside it. |
| Landings trigger on stairs/slopes | Raise **Min Landing Speed**. |
| Bob feels out of step with walking speed | Walk speed changed in PlayerMovement? Adjust **Walk/Run Bob Frequency** to match. |
