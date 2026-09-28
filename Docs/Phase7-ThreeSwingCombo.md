# Ore What: Phase 7, Three Alternating Swings and a Holstered Rest

The pickaxe no longer plays one swing over and over. Every mining click plays the next swing in a fixed order:

```
RIGHT (right → left)  →  LEFT (left → right)  →  OVERHEAD (top → down)  →  RIGHT → ...
```

Between swings, the pickaxe sits low on the right in a relaxed, holstered pose.

Only the swing animation changed. Mining input, the raycast, damage, cooldown, range, rock health, rock destruction and ore drops all work exactly as before.

---

## 1. Scripts modified

| File | Change |
|---|---|
| `Scripts/Mining/PickaxeSwing.cs` | Rewritten. It now has separate settings for the resting pose and for the Right, Left and Overhead swings, and plays them in order. The public API is unchanged, so nothing else needed rewriting: `Swing()`, `IsSwinging`, `CanSwing`, `ImpactReached`, `HitLanded`, `ReportImpact()`, `CameraOffset`, `CameraRotation`, `PhaseName`. New read-only extras: `CurrentSwingName`, `NextSwingName`, `InputBuffering`. |
| `Scripts/Mining/MiningController.cs` | One small change, in input timing only. When a pickaxe animation exists, a click that arrives too early now waits for up to `PickaxeSwing`'s **Input Buffering** time (it used to wait `clickBuffer`). The raycast, damage, cooldown and range are untouched. |

Not changed: the hand IK (`FirstPersonArmsIK`), the grip points, `CameraEffects`, `ViewModelMotion`, `RockHealth`, movement and mouse look.

---

## 2. How the three-swing sequence works

- `PickaxeSwing` keeps a counter of the **next** swing: 0 = Right, 1 = Left, 2 = Overhead.
- Every accepted mining input calls `Swing()`. That plays the swing the counter points to, then advances it: Right → Left → Overhead → Right …
- The order is deterministic: there is no random selection, and there are no extra buttons.

Each swing is one continuous path through key poses:

```
Resting → Wind-up (+ short hold) → Acceleration → STRIKE → [hit: bite + recoil + short follow-through]
                                                         → [miss: full follow-through]
        → Recovery → Resting
```

- **Smooth motion.** Between key poses, the pickaxe follows smooth curves that carry velocity through each key. It never snaps or stops dead.
- **Wind-up.** The pickaxe hangs briefly at the top of the wind-up. The overhead hold is longer (0.07 s).
- **Strike.** Each swing reaches its fastest point exactly at the strike.
- **Hit or miss.** After a hit, the pickaxe bites into the rock, kicks back against the swing direction, and follows through less. After a miss, momentum carries it further.

**Chaining.** A new swing may start once the current one is in its follow-through or recovery. It starts from wherever the pickaxe is, carrying its momentum, so repeated mining flows:
- Right's follow-through ends on the left, where Left's wind-up begins.
- Left ends on the right; the overhead lifts from there.
- The overhead ends low in the centre; Right winds up from there.

**Clicking while busy.** If you click while a swing is busy, one click is remembered (for up to **Input Buffering** seconds). The next swing starts the moment it's allowed. Extra clicks don't queue more swings, and animations never overlap or restart abruptly.

**Hands.** The hands stay on the grip points through IK, exactly as before: the pickaxe moves, the grip points move with it, and the arms follow.

---

## 3. The resting (holstered) pose

**Where:** `PickaxeRoot → Pickaxe Swing → Resting / holstered pose`.

| Field | Default | Meaning |
|---|---|---|
| Resting Position | (0.079, −0.188, −0.183) | lowered, pulled in, slightly right |
| Resting Rotation | (16, 9, −16) | head tipped forward and down, leaning right |
| Idle Sway Amount | 1 | size of the slow drift while resting (0 = still) |
| Idle Sway Speed | 1 | speed of that drift |

- **When it's used:** the pickaxe sits here whenever it isn't swinging. Every swing starts from it and eases back into exactly this pose.
- **Idle sway:** it fades out as a swing starts and back in once the pickaxe is resting.
- **Live editing:** changes to the resting pose apply straight away in Play mode.

---

## 4. Adjusting the swing trajectories in Unity

Select **Player → CameraRoot → PlayerCamera → FirstPersonViewModel → Pickaxe → PickaxeRoot**. Every value can be changed in Play mode, so you can tune while swinging (Play-mode edits are lost when you stop, so copy values you like).

### How the numbers work

Every pose is an offset from the pickaxe's default held position (the `Pickaxe` object), in **camera space**, pivoting at the right hand:

| | x | y | z |
|---|---|---|---|
| **Position** (metres) | + right | + up | + forward |
| **Rotation** (degrees) | + head tips forward/down, − back toward you | + head turns right, − left | + head leans left, − right |

### Per-swing settings (Right Swing, Left Swing, Overhead Swing)

| Section | Fields | Notes |
|---|---|---|
| Wind-up | Position, Rotation, Duration, Hold | the anticipation pose, how long it takes to get there, and how long it hangs |
| Acceleration | Position, Rotation, Duration | a waypoint the head passes on its way in; shapes the arc |
| Strike | Position, Rotation, Duration, Strike Acceleration | the pose at the moment of the hit check. Strike Acceleration > 1 = still speeding up into the rock |
| Follow-through | Position, Rotation, Duration | where the momentum carries it (a miss goes 15% further) |
| Recovery | Position, Rotation, Duration | a waypoint on the way back to resting, so the return curves |
| Curve | AnimationCurve | optional time-shaping inside each stretch; leave it linear for the natural ease |
| Swing Duration | (calculated) | a missed swing from rest back to rest, in seconds |

**Default timings:**

| | Wind-up + hold | Acceleration + strike | Follow-through | Recovery |
|---|---|---|---|---|
| Right | 0.30 + 0.03 s | 0.08 + 0.07 s | 0.20 s | 0.34 s |
| Left | 0.32 + 0.03 s | 0.08 + 0.07 s | 0.20 s | 0.30 s |
| Overhead | 0.32 + 0.07 s | 0.09 + 0.08 s | 0.22 s | 0.32 s |

The hit check lands 0.48 s (Right), 0.50 s (Left) and 0.56 s (Overhead) after the click.

### Shared settings

| Section | Field | Default | Meaning |
|---|---|---|---|
| **General** | Animation Speed | 1 | plays every swing faster (>1) or slower; the hit check moves with the strike |
| | Recovery Speed | 1 | >1 = quicker return to resting |
| | Input Buffering | 1 s | how long one early click is remembered (0 = off) |
| | Blend Smoothness | 0.6 | how much momentum a chained swing inherits (0 = starts from a standstill) |
| | Swing Variation | 0.3 | small random differences in each wind-up; never changes the order |
| | Camera Shake Amount / Camera Motion | 0.2 / 1 | hit shake and how much the camera follows the swing |
| **Impact** (all swings, hits only) | Impact Pause, Speed Kept, Recoil, Recoil Duration, Vibration | 0.05 s, 0.12, 3°, 0.06 s, 1.2° | the bite and kick when the pick hits a rock |
| | Follow Through Strength | 0.45 | share of the follow-through left after a hit |
| | Miss Follow Through Multiplier | 1.15 | how much further a miss carries |

### Tuning tips

- **Keep the strike on the crosshair.** The rock takes damage from the centre-screen ray no matter what the animation does, but it looks best when the pick point reaches the screen centre at the strike pose. If you change a Strike pose, check it in Play mode.
- **Keep the hands attached.** The arms have limited reach, and the left hand sits 18 cm up the handle. Large Position values (more than about 0.3 m from the defaults), especially up or forward, can straighten an arm fully. If a hand stops following the handle, bring that pose closer in.
- **Bigger sweep:** move the Wind-up and Follow-through poses further apart (for example Right Wind-up Rotation Y up, Follow-through Y down).
- **Heavier:** Animation Speed 0.85, Strike Acceleration 1.8, Impact Pause 0.07.
- **Snappier:** Animation Speed 1.2, and lower the wind-up Holds.

The defaults were found with a search that renders each pose from the player camera. It aimed for the pick point on the crosshair at the strike, the head leading the swing, both arms under 90% reach, wrists not over-bent, and nothing crowding the lens.

---

## 5. How the existing mining was preserved

- **Hit timing.** The hit still happens where it always did. At the strike key, `PickaxeSwing` raises `ImpactReached`, and `MiningController` runs its unchanged raycast (`ApplyHit`): same range, same damage, same layers.
- **Hit versus miss.** It then calls `ReportImpact(hit)`, which chooses the hit or miss path.
- **One hit check.** No new hit detection or collision was added. All three swings share the same centre-screen ray.
- **Cooldown.** It is unchanged at 0.6 s. The swing can't start its next wind-up until the current one reaches its follow-through, which is later than the cooldown anyway.

---

## 6. Measured in Play mode (120 fps)

| Check | Result |
|---|---|
| Order over 10 clicks | Right, Left, Overhead, Right, Left, Overhead, Right, Left, Overhead, Right |
| Damage | one hit per swing, on the strike frame; rock 5 → 0 HP in 5 swings, broke and dropped 3 ore |
| Swing at the sky | no hit, full follow-through, back to resting |
| Rapid clicking (every 0.25 s) | one click buffered each time; swings chained out of each follow-through with no gaps or overlaps |
| Hands on the handle | 0.0 mm drift from the grip points on every frame |
| Arm reach | at most 90% (hands never detach) |
| Wrist bend | at most 63° |
| Head speed at impact | Right ~3.9 m/s, Left ~4.6–5.5 m/s, Overhead ~5.6 m/s: the fastest point of each swing |
| Largest single-frame jump | 5 cm, during the overhead strike (motion, not a snap) |
| Return to rest | exact; only the idle sway (a few mm or degrees) moves it afterwards |

---

## 7. Test checklist

- [ ] Standing still: the pickaxe rests low on the right, head tipped down, hands mostly out of view, with a slight slow drift
- [ ] Click 1: pulls back to the right, then sweeps right → left across the crosshair
- [ ] Click 2: winds up on the left, then sweeps left → right on a different path
- [ ] Click 3: lifts both arms up, hangs a moment, then chops straight down through the centre
- [ ] Click 4 is Right again, and the order keeps repeating
- [ ] Every hit flashes and chips the rock exactly when the pick arrives; 5 hits still break a rock and drop ore
- [ ] Spam-clicking flows from one swing into the next without snapping
- [ ] Both hands stay on the handle through every swing
