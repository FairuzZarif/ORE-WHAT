# Ore What: Phase 5, Natural Grip, Physical Swing, Impact and Landing

> **Grip update (after Phase 6): wrap-around grip.** Both hands now wrap the shaft from the side or underneath instead of the left palm sitting on top of it. See [section 2b](#2b-wrap-around-grip-update) below.
>
> **Right-hand update.** The right (lower) hand holds the shaft underhand: palm facing the player, fingers wrapped round from underneath, thumb pointing up the shaft toward the head. See [section 2c](#2c-right-hand-grip-palm-toward-the-player-update).
>
> **Updated in Phase 6:** the swing (section 3) has been replaced by a momentum-based version: wind-up → acceleration → strike → bite/recoil → follow-through → recovery, with chaining. Its Inspector fields are different. See [Phase6-SwingMomentum.md](Phase6-SwingMomentum.md). Everything else here (grip, IK, impact FX, landing) still applies.

This phase fixes how the first-person arms hold the pickaxe and makes mining and movement feel physical:

- Both hands now wrap the handle with natural wrists.
- The pickaxe head is oriented correctly, point first.
- The swing has a full wind-up, strike, hit-stop and recoil.
- Hits throw rock chips and make a sound.
- Jumping and landing go through distinct states, and the landing compresses and rebounds.

Nothing about *what* mining or movement does has changed: speeds, jump height, gravity, the mining raycast, rock health and ore drops are all as before.

---

## 1. What was wrong, and why

I measured the old grip before changing it:

| | Old | New (worst case over a whole swing) |
|---|---|---|
| Left wrist bend | 95° | 22° |
| Right wrist bend | 72° | 24° |
| Twist at the wrist | up to 112°, all at the wrist joint | up to 90°, spread along the forearm |
| Hands on the handle | yes | yes: 0.00 cm error on every frame of a Play-mode test |

A real wrist bends about 60° at most and never twists; twisting happens along the forearm. The old grip broke both rules, for three reasons:

1. **The layout.** The handle ran almost parallel to the forearms. A fist can only wrap a handle naturally when the forearm **crosses** it, so no wrist angle could look right.
2. **The elbow.** The IK aimed each bone with a shortest-path rotation, which left the upper arm rolled arbitrarily. The elbow hinge was twisted, and the wrist had to make up for it.
3. **The hand orientation.** Each hand was re-aimed every frame from the shoulder, and the whole twist landed on the wrist joint.

---

## 2. How it's fixed

### Grip and IK (`FirstPersonArmsIK`)

- **A proper hinge elbow.** The elbow's hinge axis is recorded from the model's rest pose. After aiming the upper arm, the IK rolls it so that hinge lines up with the bend, and the forearm then only bends around it, like a real elbow. Elbow hints sit out to the sides, so elbows never flip backwards.
- **Grips calibrated once, then locked.**
  - At build time, every hand rotation that grips the handle is tried: each roll around the handle, plus a reverse grip with a penalty.
  - The one closest to the model's relaxed wrist is stored relative to its grip point.
  - From then on the hand turns rigidly with the pickaxe, so hands and pickaxe behave as one object.
  - **Grip Adapt** lets each hand roll up to 35° around the handle during a swing, as a real grip slides.
- **Twist goes to the forearm.** Any rotation of the hand around the forearm is shared by the forearm's two twist bones (30% and 65%), so the skin twists gradually instead of wringing at the wrist.
- **Fist on the handle.** Fingers curl into a fist, and the wrist is placed so the centre of the fist lies exactly on the grip point.

Menu **Ore What → Rebuild First-Person Viewmodel** rebuilds and recalibrates everything. On the `ArmsModel` component, right-click → **Recalibrate Grips** redoes just the calibration, for example after moving a grip point.

### Pickaxe hierarchy and orientation

```
PlayerCamera
└── FirstPersonViewModel     ViewModelMotion
    ├── ArmsModel            FirstPersonArmsIK
    ├── LeftElbowHint / RightElbowHint
    └── Pickaxe              ← resting place + orientation (at the right-hand grip)
        └── PickaxeRoot      PickaxeSwing   ← the only animated transform
            ├── PickaxeMesh                 ← the model (handle +Y, pick point +Z), no extra rotations
            ├── RightHandGrip               ← dominant hand: main grip near the end of the handle
            └── LeftHandGrip                ← supporting hand: 18 cm further up the shaft
```

**PickaxeRoot's axes are the pickaxe's own axes:**
- **Y** runs up the handle.
- **Z** is where the pick point faces.
- **X** is the swing axis.

Rotating around X therefore always swings the head in its own plane, point first. There are no per-frame "fix-up" rotations anywhere.

**Resting layout.** I didn't hand-pick it. I swept several thousand layouts, varying:
- handle lean;
- head roll;
- right-hand position;
- left-hand distance;
- elbow hints;
- strike angle.

Each was scored over every swing pose on:
- wrist bend and twist;
- arm reach;
- hands staying on screen and off the crosshair;
- the pick point landing near the crosshair at impact.

The winner:
- **Right hand:** low on the right.
- **Handle:** leans 40° forward and 10° left.
- **Left hand:** further up the shaft, just below and right of the crosshair.
- **Head:** at the top of the screen, point forward.

These values are the constants at the top of `Scripts/Editor/ViewModelSetupTool.cs`.

### 2b. Wrap-around grip (update)

**Problem.** The grip calibration only minimised wrist bend. With elbows pointing out to the sides, the forearms arrived over the top of the shaft, so the most comfortable grip it found was palm-down on top: left palm facing −0.61 down, right −0.36.

**Fix.** The pickaxe's position and orientation are unchanged; only the hands and elbows moved:

1. **Palm rule.** Each arm has a **Min Palm Up** setting (default 0). Calibration rejects any roll around the shaft where the palm would face down onto the top of it, and gently prefers wrapping from underneath. The palm direction is measured from the knuckle line toward the curled fingers.
2. **Grip transforms now mean something.**
   - After calibration, `LeftHandGrip` and `RightHandGrip` are rotated so **up (green) = along the handle** and **forward (blue) = the way the palm faces**.
   - The hand is locked to that transform.
   - To slide a hand around the shaft yourself, rotate its grip around the green axis and press Play. Don't recalibrate afterwards, or it recomputes the rotation.
3. **Elbows low.** The elbow hints now sit low, so both forearms come up from **under** the shaft: left (−0.8, −0.6, −0.2), right (0, −0.2, 0.2). I found them by searching hint positions over every swing pose, scoring:
   - wrist bend and twist;
   - reach;
   - palm direction;
   - penalties for an elbow or forearm crowding the lens.
4. **No roll during the swing.** **Grip Adapt** now defaults to 0, so the hands keep exactly the same grip on the shaft for the whole swing.
5. **Fist centred on the shaft.** The handle goes through the true centre of the fist's hole: the point furthest from every finger and thumb joint, rather than the joints' average. That keeps the fingers wrapped on the surface instead of cutting through the shaft.

**Measured (Play mode, a hit, a miss and 3 chained swings, 596 frames):**

| | Result |
|---|---|
| Palm direction at rest | left +0.59, right +0.65 (from underneath; was −0.61 / −0.36) |
| Hand rotation around the shaft during swings | 0.00° (never rolls onto the top) |
| Hand sliding along the shaft | 0.000 cm |
| Wrist bend | left ≤ 38°, right ≤ 30° |
| Forearm twist | left ≤ 126° (deepest miss follow-through), right ≤ 91°; carried by the twist bones, no visible wringing |
| Arm reach | left ≤ 88%, right ≤ 59% |

The shaft is 4.4 cm thick, a little wider than this model's fist can open (about 3.3 cm between the thumb base and the little-finger knuckle). The base of the thumb therefore sits slightly inside the shaft. The hand hides this from the player's camera.

### 2c. Right-hand grip: palm toward the player (update)

The right hand is the lower hand on the shaft. It now holds the shaft underhand:
- the **palm faces the player**;
- the fingers wrap around the shaft from underneath, on the player's side;
- the **thumb points up the shaft toward the head**.

The pickaxe's position and orientation are unchanged, and so is the left hand.

**History.** The first version of this fix put the back of the right hand on the right side of the shaft. That left the palm facing away from the player, which was the wrong way round, so it was replaced by the setup below.

**How it works.** Each arm in `FirstPersonArmsIK` has these grip settings:

| Setting | Right arm | What it does |
|---|---|---|
| **Palm Toward Viewer** (0–1) | 1 | Calibration picks the roll around the shaft where the palm faces the camera. |
| **Min Palm Up** | −1 | Turns off the default "palm up" rule, which would fight the setting above. |
| **Thumb Along Shaft** (0–1) | 1 | Aims the thumb's base joint up the shaft, toward the head. Calibration also rejects the reverse grip, where the thumb would point toward the end of the handle. |
| **Thumb Curl Override** | 5° | Thumb curl for this hand only (−1 = use the shared Thumb Curl). Low, so the thumb lies along the handle instead of hooking round it. |
| Preferred Hand Side / Hand Side Weight | (0, 0, 0) / 0 | Off on the right arm. The older way of choosing which side of the shaft the hand sits on. |

**Elbow.** The right elbow hint stays at (0.6, −0.2, 0.1), so the forearm comes in from the right. I swept 27 elbow positions: every one could turn the palm toward the player, and this one gave the straightest wrist (31° bend at rest).

**Measured (Play mode, a hit, a miss and 3 chained swings, 730 frames):**

| | Idle | Whole swing (wind-up → strike → impact → follow-through → recovery) |
|---|---|---|
| Hand sliding or rolling on the shaft | 0.00 mm / 0.00° | 0.00 mm / 0.00° on every frame |
| Palm facing the player (1 = straight at the camera) | 0.96 | 0.74–0.96 |
| Thumb along the shaft toward the head (1 = exactly) | 0.98 | 0.98 |
| Right hand below the left, along the shaft | 19 cm | 19 cm |
| Closest point between the two hands | 2.4 cm | 2.4 cm (never touching) |
| Wrist bend | 31° | 5–58° |
| Forearm twist | −109° | −87° to −140°; the forearm twist bones take most of it |
| Arm reach | 46% | ≤ 56% |

To adjust by hand, rotate `RightHandGrip` around its green (up) axis to slide the hand around the shaft, or move it along Y to change its height, then press Play. **Ore What → Rebuild First-Person Viewmodel** recalibrates the grip from these settings. It also resets `PickaxeMesh` and the grip positions to the setup tool's values, so it will undo any hand tweaks to those.

---

## 3. The swing (`PickaxeSwing`, on PickaxeRoot)

```
Idle → Prepare → Raise → Pull back → STRIKE → [hit: hit-stop + rattle → recoil] → Recovery → Idle
       0.10 s    0.22 s   0.09 s     0.10 s     [miss: follow-through 0.16 s]      0.30 s
                                        └─ ImpactReached → mining raycast + damage (same frame)
```

| Phase | Motion | Easing |
|---|---|---|
| Prepare | Small settle, slightly down and forward | slow sine in-out |
| Raise | Pickaxe tips back over the shoulder, hands lift | sine in-out (slow start, slow top) |
| Pull back | A little further behind and above | ease-out |
| Strike | Chops down toward the crosshair, point first | **cubic ease-in**: fastest at impact |
| Hit-stop | 0.045 s freeze on the impact pose while the handle rattles | none |
| Recoil / follow-through | Bounce back on a hit; carry through on a miss | cubic ease-out |
| Recovery | Back to rest | sine in-out |

- **Variation.** Each swing randomly varies strength (±8%), timing (±7%) and angle (±2–3°), scaled by **Swing Variation**, so repeated mining doesn't look robotic. The strike itself stays exact, so hit timing is predictable.
- **Impact sync.** Damage is applied inside `ImpactReached`, the same frame the strike reaches its fastest point. It's the existing `MiningController` raycast, unchanged. In testing, damage, chips and sound all started on the same frame, 0.51 s after the click.

### Tuning

| Field | Default | More aggressive → |
|---|---|---|
| **Swing Intensity** | 1 | 1.2–1.5 (scales every angle and offset) |
| Prepare / Wind-up / Anticipation Duration | 0.10 / 0.22 / 0.09 | shorter |
| **Strike Duration** | 0.10 | shorter = snappier hit |
| **Hit Pause Duration** | 0.045 | longer = heavier "thunk" (0 = off) |
| Impact / Recovery Duration | 0.12 / 0.30 | shorter = ready sooner |
| Prepare / Wind-up / Anticipation Angle | (6,0,0) / (−42,0,−6) / (−6,0,−2) | more negative X = raised further |
| **Swing Angle** | (18, 0, 4) | bigger X = chops lower |
| Swing Offset | (−0.02, −0.06, 0.06) | more Z = lunges further |
| Follow Through Angle | (14, 0, 2) | bigger = carries further on a miss |
| **Recoil Amount** / Recoil Angle / Recoil Distance | 1 / 9° / 0.03 m | bigger bounce |
| **Impact Vibration** | 1.2° | more rattle in the handle |
| **Swing Variation** | 0.5 | 0 = identical every time |
| Camera Shake Amount | 0.25 | 0 = off |

Offsets are in **pickaxe space**:
- **x** is along the swing axis;
- **y** is up the handle;
- **z** is the direction the pick point faces.

---

## 4. Impact feedback

When a strike lands on something, these all fire at the same moment as the damage:

| Effect | Where | Size |
|---|---|---|
| Hit-stop and handle rattle | `PickaxeSwing` | 0.045 s freeze; 1.2° rattle decaying over about 0.2 s |
| Recoil | `PickaxeSwing` | about 9° kick back, 3 cm |
| Arms jolt back and down | `ViewModelMotion` (**Hit Kick**) | 1.2 cm |
| Camera dip, nod and tiny roll | `CameraEffects` (**Mining Impact Drop / Nod / Roll**) | 6 mm, 0.7°, 0.3°; about 3–4 mm and 0.6° measured |
| Tiny camera shake | `CameraShake` (existing) | Camera Shake Amount 0.25 |
| Rock chips | `MiningImpactFX` (new, on Player) | 12 per rock hit, 5 on other surfaces |
| Impact sound | `MiningImpactFX` | synthesised, see below |

**Rock chips:**
- small spinning cubes that fly out of the hit point along the surface;
- tinted to the surface's colour;
- bounce on the ground, then shrink away;
- use the `Materials/Prototype/RockChips.mat` (URP Particles/Lit) material.

**Sound:**
- There were no audio files in the project, so `MiningImpactFX` **synthesises** a short pickaxe-on-rock "tok" at startup: a low thump, a gritty crack, and a faint metallic ring.
- Pitch varies ±10% per hit.
- To use real recordings, drag clips into **Impact Clips**, and one is picked at random per hit.

Misses get none of this: no rattle, recoil, camera reaction, chips or sound.

---

## 5. Jump, fall and landing (`PlayerMotionState`, on Player)

A small state machine now tracks the body, using the existing `PlayerMovement` (plus a new `Jumped` event) and the CharacterController's velocity:

```
Grounded → JumpStart → Ascending → Apex → Falling → Landing → Grounded
(walking off a ledge goes Grounded → Falling)
```

Both the camera (`CameraEffects`) and the arms (`ViewModelMotion`) read it, so they always agree:

| State | Camera | Arms / pickaxe |
|---|---|---|
| JumpStart | lifts 18 mm, chin up slightly (smoothed, not instant) | lift 12 mm |
| Ascending | keeps part of the lift, fading as upward speed drops | same |
| Apex | settles to neutral (a moment of stability) | same |
| Falling | sinks slowly, up to 35 mm at max fall speed, and tips down | sink up to 20 mm, pickaxe tips forward |
| Landing | spring: compress, small rebound, settle | same spring: drop, pull toward the body, tip forward |

Nothing here is a sine-wave bob. Every offset follows the player's actual vertical speed.

### Landing

On touchdown, strength = fall speed, mapped from **Min Landing Speed** (2.5 m/s; below that, stairs and slopes do nothing) to **Max Landing Speed** (16 m/s, clamped). The **Landing Strength Curve** shapes it. That strength kicks a damped spring, which goes:
1. **compression** (peaks about 0.05 s after touchdown);
2. **small rebound** (about 20% of the compression);
3. **settle**.

Measured in Play mode:

| Fall | Compression | Camera dip | Arms drop | Rebound |
|---|---|---|---|---|
| Normal jump (lands at 6.9 m/s) | 0.41 | 30 mm | 20 mm | −0.09 |
| 5 m drop (≈20 m/s, hits the Max Landing Speed clamp = full strength) | 0.84 | 63 mm | 31 mm | −0.19 |

| Where | Field | Default | Meaning |
|---|---|---|---|
| PlayerMotionState | Landing Frequency | 4 | Higher = quicker, snappier landing |
| | Landing Damping | 0.42 | Lower = more rebound (1 = none) |
| | Min / Max Landing Speed | 2.5 / 16 | Fall-speed range that maps to strength 0..1 |
| | Landing Strength Curve | linear 0.15 → 1 | Shapes small vs big falls |
| CameraEffects | Landing Shake Amount | 0.06 m | Dip at full compression |
| | Landing Rotation Amount | 2.2° | Nod at full compression |
| | Landing Tilt | 0.8° | Sideways tilt (random side per landing) |
| | Jump Start Lift / Ascend Lift | 0.018 m / 0.5 | Jump push-off |
| | Falling Drop / Falling Pitch | 0.035 m / 1.5° | At max fall speed |
| ViewModelMotion | Landing Drop / Compress / Tilt | 0.035 m / 0.02 m / 4° | Arms at full compression |
| | Jump Start Lift / Fall Drop / Fall Tilt | 0.012 m / 0.02 m / 2° | Arms in the air |

---

## 6. Files

**Changed:**

| File | Change |
|---|---|
| `Scripts/ViewModel/FirstPersonArmsIK.cs` | Hinge-aligned elbow, calibrated rigid grips with a small adaptive roll, twist spread to twist bones, `GetWristAngles` for checks |
| `Scripts/Mining/PickaxeSwing.cs` | Prepare / raise / pull-back / strike / hit-stop / recoil phases, rattle, per-swing variation, `HitLanded` event. `Swing()`, `IsSwinging`, `CanSwing`, `ImpactReached` and `ReportImpact` work as before. |
| `Scripts/ViewModel/ViewModelMotion.cs` | State-based jump/fall, landing compression, hit kick |
| `Scripts/Player/CameraEffects.cs` | Jump/fall/landing now from `PlayerMotionState` (spring landing); mining reaction only on actual hits |
| `Scripts/Player/PlayerMovement.cs` | Added a `Jumped` event. Movement, jump height and gravity unchanged. |
| `Scripts/Mining/MiningController.cs` | Added a `SurfaceHit` event after the existing raycast. Hit logic unchanged. |
| `Scripts/Editor/ViewModelSetupTool.cs` | New hierarchy, the swept layout, twist-bone wiring, calibration |
| `Scripts/Editor/MiningSetupTool.cs`, `CameraEffectsSetupTool.cs` | Add `MiningImpactFX` + chip material, and `PlayerMotionState` |

**New:**
- `Scripts/Player/PlayerMotionState.cs`
- `Scripts/Mining/MiningImpactFX.cs`
- `Materials/Prototype/RockChips.mat`

**Not touched:** `PlayerLook`, `RockHealth` (and so the ore drops), `Crosshair`, `CameraShake`.

Unity's Animation Rigging package isn't in the project. Rather than add a dependency, the existing lightweight IK was fixed.

---

## 7. Test checklist

- [ ] Idle: both hands wrap the handle, knuckles along it, wrists straight, pick head at the top pointing forward
- [ ] Looking straight ahead, up, and down: the grip looks the same (the viewmodel is attached to the camera)
- [ ] Click: a slow settle, the pickaxe rises back over the shoulder, a short pull-back, then a fast chop toward the crosshair
- [ ] On a rock: the rock flashes, chips fly, the "tok" plays, the pickaxe freezes for an instant and rattles, bounces back, and the view nods slightly, **all on the same frame as the damage**
- [ ] On air: the pickaxe carries through with no chips, sound or camera reaction
- [ ] Several swings in a row look slightly different from each other
- [ ] The hands never leave the handle, and the wrists never kink or wring during any swing phase
- [ ] Jump: a soft lift at push-off, calm at the top, a gradual sink while falling
- [ ] Hop off a crate vs. drop from the tallest blue step: the landing dip is clearly bigger for the big drop, with a quick compress, small rebound and settle
- [ ] Walking down the ramp or steps doesn't trigger landings
- [ ] 5 hits still break a rock and drop 3 ore cubes

## 8. If something looks off

| Problem | Fix |
|---|---|
| A wrist looks kinked after moving a grip point | Right-click `ArmsModel` → **Recalibrate Grips**. |
| A hand leaves the handle at the end of the strike | The grip is out of reach. Lower **Swing Offset** z or **Swing Intensity**, or move the grips closer (`RightGrip` in `ViewModelSetupTool`) and rebuild. |
| The left hand is in the way | Lower `LeftGripDistance` (e.g. 0.14) in `ViewModelSetupTool` and rebuild. It then sits well below the crosshair. |
| The impact point is too high or low | Adjust **Swing Angle** X (bigger = lower). |
| Landings feel too soft or too hard | **Landing Shake Amount** / **Landing Rotation Amount** (camera), **Landing Drop / Tilt** (arms), or **Landing Damping** (rebound). |
| No sound | Check that the scene has an AudioListener (on `PlayerCamera`) and that **Volume** on `MiningImpactFX` is above 0. |
