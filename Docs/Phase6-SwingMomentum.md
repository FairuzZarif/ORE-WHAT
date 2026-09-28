# Ore What: Phase 6, Swing with Momentum

> **Update (Phase 7):** the single swing below has been replaced by three alternating swings (Right, Left, Overhead) and a lowered resting pose. They use the same momentum engine (key poses, Hermite curves, hit and miss paths, chaining). The Inspector fields in section 4 are now per swing. See `Phase7-ThreeSwingCombo.md`.

The pickaxe swing is now one continuous motion with weight and momentum:

```
IDLE → WIND-UP → ACCELERATION → STRIKE (fastest) → IMPACT (bite + recoil) → FOLLOW-THROUGH → RECOVERY → IDLE
                                                  ↘ (miss) ───────────────→ bigger FOLLOW-THROUGH ↗
```

It's no longer "raise → hit → snap back". The pickaxe:
- eases out of rest and hangs for an instant at the top;
- accelerates into the rock and is moving fastest exactly when the damage lands;
- on a hit, bites and kicks back slightly;
- lets its momentum carry it past the impact;
- is pulled back to ready by the hands.

Repeated clicks chain into a rhythm without ever stopping at idle.

Only the visual swing changed. Hit detection, damage, rock health, ore drops, movement and mouse look are untouched. The rock still takes damage from the same `MiningController` raycast, fired by the same `ImpactReached` event.

---

## 1. How it works

`PickaxeSwing` (on `PickaxeRoot`) builds each swing as a short **timeline of key poses**. Each key has:
- a pose: position offset plus rotation, in pickaxe space;
- a velocity going in, and a velocity going out.

Between keys, the pose follows a **cubic Hermite curve**. That curve passes through each key at exactly the velocity set for it. As a result:
- **Nothing starts, stops, or changes direction instantly.** Velocity carries through every key.
- **Speed varies throughout.** The velocities decide it:

| Key | Velocity | Effect |
|---|---|---|
| Swing start | the pickaxe's **current** velocity × Animation Blend | fluid chaining from the previous swing |
| Top of wind-up | 0 | a brief hang: anticipation |
| Acceleration | 90% of the average speed from the top to the impact | picking up speed |
| **Impact** | strike average × **Strike Acceleration** (1.4) | still speeding up at the moment of impact: fastest point |
| After impact, **hit** | impact speed × **Impact Speed Kept** (0.12) | a deliberate velocity break: "it hit something" |
| After impact, **miss** | full impact speed (trimmed only if it would overshoot) | momentum carries straight on |
| End of follow-through | 0 | the weight comes to rest at the far end |
| Recovery midpoint | smooth pass-through | a curved return, not a straight line |
| Rest | 0 | settles into idle |

**Hit versus miss.**
- The swing always assumes a miss at first.
- At the impact key it raises `ImpactReached`, and `MiningController` runs its raycast.
- If the ray hit something, `ReportImpact(true)` replaces the rest of the timeline with the hit path: bite, recoil, a shorter follow-through, then recovery.

**Hands.** The hands still follow the grip points through IK: Pickaxe → grip points → hand IK → arms. They move and rotate with the handle through the whole arc; see Phase 5.

**Diagonal 3D arc.** Every key pose sets all three rotations and all three offsets:
- **X**: the big chop.
- **Z**: the sideways lean that makes it diagonal.
- **Y**: a slight twist.
- **Offsets**: the arms pull in, push forward and drop.

So the head travels a curved 3D path, not a flat back-and-forth.

---

## 2. Measured in Play mode (120 fps, default settings)

| Phase | Hit swing | Miss swing |
|---|---|---|
| Wind-up | 0.27 s, 0° → −52°, peak **280°/s** | 0.27 s, peak 273°/s |
| Acceleration | 0.11 s, → −20°, up to 370°/s | same |
| Strike | 0.085 s, → +18°, **peaks at 591°/s on the impact frame** | peaks 556°/s at impact |
| Impact | speed drops to **65°/s** (bite), 3° recoil, 0.10 s | — (no stop) |
| Follow-through | carries on to **34°**, 0.19 s | carries on at 522°/s to **59°**, 0.22 s |
| Recovery | back to 0, 0.28 s | 0.27 s |
| **Total** | **1.08 s** | 0.98 s |

Other measurements:
- **Damage timing.** Damage landed on the fastest frame of the whole swing, 0.49 s after the click.
- **Hands.** 0.00 cm fist-to-grip error on all 875 recorded frames.
- **Wrists.** Bend at most 44°. Arm reach at most 85%.
- **Chaining.** 3 swings started straight from each other's follow-through: 3 hits, 0 frames at idle in between. The chained wind-up eased from 52°/s up to 368°/s, with no snap.
- **Click buffer.** A click 0.3 s into a swing, which is too early, was remembered and started the next swing as soon as the 0.6 s cooldown allowed.

---

## 3. Camera reaction

`PickaxeSwing` works out a tiny camera offset from its own pose and speed. `CameraEffects` adds it into the single camera offset it already applies, so nothing fights over the camera.

| Moment | Camera (measured) |
|---|---|
| Wind-up | leans back/up to about −1.1° |
| Strike | kicks forward/down as speed builds, to about +0.55° |
| Impact | the existing tiny shake plus the hit nod |
| Follow-through | follows the momentum, to about +0.7° (hit) or +1.2° (miss) |

**Camera Motion** on `PickaxeRoot` scales all of this (0 = off). **Camera Shake Amount** controls the hit shake.

---

## 4. Inspector (`PickaxeRoot → Pickaxe Swing`)

Positions and rotations are offsets from the resting pose, in **pickaxe space**:
- x is along the swing axis;
- y is up the handle;
- z is the pick direction;
- rotation X = chop (− back, + forward), Z = sideways lean, Y = twist.

| Section | Field | Default | Notes |
|---|---|---|---|
| **Wind-up** | Wind Up Duration | 0.28 s | |
| | Wind Up Position / Rotation | (0.03, 0.07, −0.08) / (−50, 4, −10) | how far back and up it's pulled |
| | Wind Up Curve | linear | optional time-shaping of that stretch |
| **Acceleration** | Duration / Position / Rotation | 0.12 s / (0.01, 0.03, −0.01) / (−18, 2, −4) | |
| **Strike** | Strike Duration | 0.09 s | kept exact (not varied) so hit timing is predictable |
| | Strike Position / Rotation | (−0.02, −0.06, 0.06) / (18, −4, 6) | pose at the moment of impact |
| | **Strike Acceleration** | 1.4 | speed at impact vs. the strike's average; higher = whips in harder |
| | Strike Curve | linear | |
| **Impact** (hits only) | **Impact Pause** | 0.05 s | the "bite" |
| | Impact Rotation / Bite Position | (3, 0, 1) / (0, −0.005, 0.008) | how far the head sinks in |
| | Impact Speed Kept | 0.12 | lower = the rock stops it harder |
| | **Impact Recoil** / Recoil Duration | 3° / 0.06 s | kick back |
| | Impact Vibration | 1.2° | handle rattle |
| **Follow-through** | Follow Through Duration | 0.2 s | |
| | Follow Through Position / Rotation | (−0.04, −0.1, 0.03) / (54, −8, 12) | where momentum carries it |
| | **Follow Through Strength** | 0.45 | share of that still used after a **hit** (1 = same as a miss) |
| | Miss Follow Through Multiplier | 1.15 | misses carry further and a little longer |
| | Follow Through Curve | linear | |
| **Recovery** | Recovery Duration | 0.28 s | |
| | Recovery Position / Rotation | (−0.01, −0.02, −0.02) / (10, 0, 4) | a waypoint on the way back, so the return curves |
| | Recovery Curve | linear | |
| **Overall** | **Swing Speed** | 1 | plays the whole swing faster or slower (strike timing stays exact) |
| | **Swing Strength** | 1 | scales every angle and offset |
| | **Animation Blend** | 0.6 | how much momentum a chained swing inherits (0 = starts from standstill) |
| | Swing Variation | 0.5 | per-swing randomness (0 = identical) |
| | Camera Shake Amount | 0.2 | hit shake |
| | Camera Motion | 1 | camera follow of the swing |
| | Total Swing Duration | (read-only) | a missed swing, start to rest |

**Curves:** the four curves reshape time *within* their stretch. Left linear, the motion is already eased by the velocities above. Bend them for special timing, e.g. a wind-up that lingers longer at the start.

`MiningController` also has a new field, **Click Buffer** (default 0.4 s). A click that arrives while the swing is still busy, or during the cooldown, is remembered this long and fires as soon as it's allowed.

### Quick tuning

| I want… | Change |
|---|---|
| A heavier, weightier swing | Swing Speed 0.85, Strike Acceleration 1.6, Impact Pause 0.07 |
| A snappier, arcade swing | Swing Speed 1.2, Wind Up Duration 0.2, Recovery Duration 0.22 |
| More follow-through after hits | Follow Through Strength 0.6–0.7 |
| Hits to feel like hitting a wall | Impact Speed Kept 0.05, Impact Recoil 5 |
| Faster rhythm when spam-clicking | Mining Cooldown on MiningController 0.5, Animation Blend 0.8 |
| Less camera movement | Camera Motion 0.5 (or 0) |

---

## 5. Test checklist

- [ ] Click at a rock: slow pull back and up (the upper hand comes toward you), a brief hang, then an accelerating chop
- [ ] The head is moving fastest right as it reaches the rock, and the rock flashes/chips at exactly that moment
- [ ] A short "bite" (sudden slowdown), a tiny kick back, then the pickaxe keeps travelling past the impact point before being pulled back
- [ ] Swing at the air: no stop at all, and the pickaxe carries much further down before returning
- [ ] Recovery eases into the idle pose with no snap
- [ ] Spam-click: swings flow into each other (the next wind-up starts out of the previous follow-through) and never snap to idle in between
- [ ] Both hands stay on the handle and rotate with it through the whole arc
- [ ] The camera leans back slightly on the wind-up and dips with the strike, never enough to distract
- [ ] 5 hits still break a rock and drop ore

## 6. Files

| File | Change |
|---|---|
| `Scripts/Mining/PickaxeSwing.cs` | Rewritten as a key-pose timeline with Hermite curves, separate hit and miss paths, chaining with inherited momentum, and a camera reaction. **Public API unchanged:** `Swing()`, `IsSwinging`, `CanSwing`, `ImpactReached`, `HitLanded`, `ReportImpact()`. Adds `CameraOffset`, `CameraRotation` and `PhaseName`. |
| `Scripts/Player/CameraEffects.cs` | Adds the swing's camera offset and rotation into its single final offset |
| `Scripts/Mining/MiningController.cs` | Click buffer (input timing only; raycast, damage and cooldown unchanged) |
