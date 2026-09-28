# Ore What: Phase 3, First-Person Arms and Swing Feel

This phase makes mining feel physical. You now see a pair of rigged first-person arms (**WRAD ARMS**, from `Assets/arms`) gripping the pickaxe with both hands. The arms breathe and sway while idle, and each swing has a wind-up, a fast strike, and a recoil, with the elbows bending as the pickaxe moves. The impact happens at the same moment the rock takes damage, and the pick's **point** strikes the rock.

> **Updated again in Phase 5:** the grip, pickaxe hierarchy (`Pickaxe → PickaxeRoot → PickaxeMesh + RightHandGrip + LeftHandGrip`), swing phases, and landing now work differently. See [Phase5-GripSwingImpactLanding.md](Phase5-GripSwingImpactLanding.md). The parts of this doc about the overlay camera, look sway and idle breathing still apply.
>
> **Update (Phase 3b):** the first version used placeholder hands built from primitives, and its pickaxe head was twisted so the flat side led the strike. Both are replaced; see [section 3](#3-the-viewmodel-hierarchy) and [section 3b](#3b-the-rigged-arms-firstpersonarmsik).

Nothing about *what* mining does has changed: the same raycast, the same 3 m range, and the same `RockHealth` and ore drop. Only *when* the hit happens (at impact instead of on click) and *how it looks* are new.

---

## 1. Quick start

- **The test scene is already set up.** Press **Play** and left-click a rock.
- **To rebuild the arms from scratch**, click **Ore What → Rebuild First-Person Viewmodel**. This undoes any hand-made changes to the arms, but not to other objects.
- **Ore What → Build Player Test Scene** and **Add Mining Setup To Scene** now include the arms automatically.

---

## 2. What changed

### Files changed

| File | Change |
|---|---|
| `Scripts/Mining/PickaxeSwing.cs` | The simple down-and-back swing is now a 5-phase swing with easing, recoil, and camera shake. `Swing()` and `IsSwinging` work as before. It adds an `ImpactReached` event, `ReportImpact()`, and `CanSwing`. |
| `Scripts/Mining/MiningController.cs` | The raycast and damage code is **unchanged** but moved into `ApplyHit()`. With a pickaxe, `ApplyHit()` runs when the swing's strike lands. Without one, it runs on click like before. |
| `Scripts/Editor/MiningSetupTool.cs` | Builds the new arms instead of the old single `PickaxeVisual` object. |

### Files created

| File | Purpose |
|---|---|
| `Scripts/ViewModel/ViewModelMotion.cs` | Idle breathing and sway, arms lagging slightly when you turn, and a small walking bob |
| `Scripts/ViewModel/CameraShake.cs` | Tiny shake on impact |
| `Scripts/ViewModel/FirstPersonArmsIK.cs` | Poses the rigged arms every frame: curls the fingers, turns each hand onto the handle, and bends shoulder and elbow (two-bone IK) so the fists land on the grips |
| `Scripts/Editor/ViewModelSetupTool.cs` | Builds the arms, grip points, pickaxe, and viewmodel camera, and wires every bone |

The arms model itself is **WRAD ARMS** by wriks (`Assets/arms`, **CC0**: free to use, no attribution required). The setup tool uses `arms.fbx`. The `.blend`, `.glb` and `.obj` copies in that folder aren't used.

The old placeholder materials `Materials/Prototype/Viewmodel_Glove`, `_Skin` and `_Sleeve` are no longer used and can be deleted.

### Not touched

- `PlayerMovement.cs`
- `PlayerLook.cs`
- `RockHealth.cs` (so ore spawning is unchanged too)
- `Crosshair.cs`

---

## 3. The viewmodel hierarchy

```
Player
└── PlayerCamera                   Camera, CameraShake
    ├── FirstPersonViewModel       ViewModelMotion      ← idle / look sway / walk bob
    │   ├── ArmsModel              FirstPersonArmsIK    ← WRAD arms (skinned mesh + skeleton)
    │   ├── LeftElbowHint                               ← elbows bend toward these
    │   ├── RightElbowHint
    │   └── Arms                   PickaxeSwing         ← the swing (pivot between the hands)
    │       ├── Pickaxe                                 ← handle, grip point
    │       │   └── PickaxeModel (prefab)
    │       ├── LeftHand                                ← where the left fist holds the handle
    │       └── RightHand                               ← where the right fist holds the handle
    └── ViewModelCamera            Camera (URP Overlay, draws only the "ViewModel" layer)
```

These pieces work together as follows:

- **The swing moves the pickaxe and the grip points; the arms follow.** `PickaxeSwing` only ever moves `Arms`. `LeftHand` and `RightHand` are empty markers on the handle, so they move exactly with the pickaxe. Every frame, `FirstPersonArmsIK` then bends the real arms so each fist sits on its marker. The shoulders stay fixed to the camera, so during a swing you see the elbows bend and straighten, not a stiff model sliding around.
- **Two layers of motion that don't fight.** `ViewModelMotion` moves `FirstPersonViewModel` (idle, sway, bob), which carries the arms, pickaxe, and elbow hints together. `PickaxeSwing` moves the child `Arms` (the swing). A child's movement adds on top of its parent's.
- **Everything is under the camera**, so it follows the view exactly when you look around. The only intentional exception is the slight "look sway" lag.
- **The pivot is between the hands.** The pickaxe tips around the grip like a lever. The head travels a big arc while the hands only shift a few centimetres, which is how it looks when *you* swing something heavy.
- **Order each frame:** `PickaxeSwing` and `ViewModelMotion` move things in `Update`, then `FirstPersonArmsIK` poses the arms in `LateUpdate` (`[DefaultExecutionOrder(100)]`). The arms always match this frame's pickaxe, with no one-frame lag.

### The pickaxe head: the point strikes the rock

The head lies in the **plane the swing moves through**, so one pick-point faces the direction of travel and is what hits the rock:
- The swing mostly rotates around the camera's right axis.
- A point swinging around an axis moves along `cross(axis, handle direction)`, so that's the direction the pick is aimed.

That plane is leaned about 25° (`PickPlaneTilt`) so you can see the head's curve. With 0 tilt it points the same way but you look at it edge-on, like a needle. The point still leads the strike.

## 3b. The rigged arms (`FirstPersonArmsIK`)

WRAD ARMS is a skinned mesh with a 50-bone skeleton (upper arm → forearm → hand → 3-jointed fingers) but no animations. `FirstPersonArmsIK` poses it procedurally every frame:

1. **Finger curl.** Each finger joint rotates from its original pose by **Finger Curl** degrees (thumbs by **Thumb Curl**), closing the hand into a fist. The setup tool measured which axis each joint curls around, so fingers close toward the palm, not sideways.
2. **Hand orientation.** Each hand is turned so:
   - its knuckle line (index → little finger) runs along the handle, with the index finger toward the head, like holding a bat;
   - its fingers carry on the line from the shoulder, so the wrist stays straight.
3. **Hand position.** The centre of the curled fist (the average of all its finger joints) must sit exactly on the grip marker, which gives the wrist position.
4. **Two-bone IK.** The upper arm and forearm are rotated so the wrist reaches that position. The elbow position comes from the law of cosines and bends toward the elbow-hint object on that side (down and outward).

Checked across 49 poses sampled along the whole swing path (rest → wind-up → impact → recoil → follow-through → rest): both fists stay centred on their grip markers (0.00 cm error). The hands never leave the handle.

**Model placement.** WRAD ARMS is modelled about 9× real size, facing backwards (−Z). The tool therefore:
- scales it by 0.11, giving about 30 cm upper arms and 19 cm hands;
- turns it 180°;
- lines its eye point up with the camera;
- moves the shoulders, which are off screen, 18 cm forward. Without that, the right arm couldn't reach its grip at the bottom of the follow-through: it needed 100% of its length, and now needs 88%.

| Field (on `ArmsModel`) | Default | Meaning |
|---|---|---|
| Left Arm / Right Arm | (wired by the tool) | Bones (`bicep`, `forearm`, `wrist`), grip marker, elbow hint, knuckles, fingers |
| **Finger Curl** | 65 | How far each finger joint closes (0 = open hand) |
| **Thumb Curl** | 35 | How far the thumb tips close |
| Hand Roll | 0 | Rolls both hands around the handle, if you want the knuckles facing a different way |

**Skin tone:** the arms use `arm_mat_pale`. The pack also includes `Assets/arms/arm_albedo_dark.png`. To use it, select the material on `ArmsModel/arms_mesh` (or extract it from `arms.fbx`) and swap its **Base Map**.

### Why a second camera? (no clipping)

The arms and pickaxe sit on their own **`ViewModel`** layer:

- **`PlayerCamera`** draws the world but skips that layer.
- **`ViewModelCamera`** is a URP *Overlay* camera stacked on top of it. It draws **only** the arms, after the world. It uses a near clip of 1 cm and a 65° field of view.

As a result, the pickaxe never pokes through a wall or rock you're standing against, and it never gets cut off by the main camera's near plane.

The viewmodel also can't affect gameplay:

- It has **no colliders**, so it can't block the mining ray and physics can't push it.
- It doesn't cast or receive shadows. Viewmodel shadows look wrong in first person.

---

## 4. Idle animation (`ViewModelMotion`)

All the motion is built from a few sine waves at different speeds. None of them repeat in step with each other, so it never looks like an obvious loop.

- **Breathing:** a slow up/down movement (one breath every 4 s by default, 6 mm). The pickaxe dips slightly on each breath, as if its weight is pulling the arms down.
- **Sway:** tiny side-to-side and rolling movement at three different speeds.
- **Look sway:** when you turn, the arms lag a few degrees behind and then catch up (smoothed). This makes the pickaxe feel like it has weight.
- **Walk bob:** a figure-eight bob driven by your actual ground speed, so sprinting bobs faster. It fades out when you stop.
- **Blending with swings:** while a swing plays, the idle and bob motion fade down to 15% over about 0.15 s, then fade back in afterwards. The swing never fights the idle, and nothing snaps.

| Field | Default | What it does |
|---|---|---|
| **Idle Sway Amount** | 1 | Overall size of breathing and sway (0 = still, 2 = double) |
| **Idle Sway Speed** | 1 | Overall speed of the idle motion |
| Breathing Amount | 0.006 | Metres of up/down per breath |
| Breathing Rate | 0.25 | Breaths per second |
| Look Sway Amount | 0.035 | Degrees of lag per pixel of mouse movement |
| Max Look Sway | 3 | Limit on the lag, in degrees |
| Look Sway Smoothing | 8 | How fast the arms catch up (lower = heavier) |
| Walk Bob Amount | 0.008 | Metres; 0 turns it off |
| Walk Bob Frequency | 0.9 | Bob cycles per metre walked |
| Motion While Swinging | 0.15 | How much idle motion is kept during a swing |
| Blend Speed | 6 | How quickly idle fades out and in around swings |

---

## 5. Swing animation (`PickaxeSwing`)

### The sequence

```
 Rest ─► Wind-up ─► Anticipation ─► Strike ─► IMPACT ─► Recoil (hit) / Follow-through (miss) ─► Recovery ─► Rest
        0.26 s       0.07 s          0.10 s     │         0.12 s                                    0.30 s
                                                └─ ImpactReached → MiningController raycast + TakeHit
```

| Phase | Motion | Easing | Why |
|---|---|---|---|
| **Wind-up** | Tips the pickaxe back over the right shoulder, and the hands lift a little | Sine in-out: slow start, slow arrival | Heavy things take effort to lift |
| **Anticipation** | Keeps pulling about 8% further back | Ease-out | A brief "loading" beat, not a dead stop |
| **Strike** | Chops forward and down toward the crosshair | **Cubic ease-in**: accelerates the whole way, fastest at impact | Power and speed right where it matters |
| **Impact → Recoil** | If it hit something: kicks back about 9° and jolts back about 3.5 cm, plus camera shake | Cubic ease-out: a sharp stop | The pickaxe bounces off rock |
| **Impact → Follow-through** | If it hit nothing: carries on about 12° further, and this phase lasts 30% longer | Cubic ease-out: momentum dying off | The swing has nothing to stop it |
| **Recovery** | Back to the rest pose | Sine in-out | A smooth settle |

Every phase starts **from wherever the arms currently are**, not from a fixed pose. Nothing can jump between states, even if you click again during recovery. A new swing may start during recovery and blends straight into its wind-up.

### How the impact syncs with damage

1. Click. `MiningController` checks the cooldown and whether `PickaxeSwing.CanSwing` is true, then calls `Swing()`. **No damage yet.**
2. About 0.43 s later, the strike reaches its fastest point and `PickaxeSwing` raises **`ImpactReached`**.
3. `MiningController.OnSwingImpact` runs the **existing** raycast (`ApplyHit`): same ray from the centre of the camera, same 3 m range, same `RockHealth.TakeHit`. It returns whether the ray hit any surface.
4. `MiningController` calls `PickaxeSwing.ReportImpact(hitSomething)`. If something was hit, the swing switches from follow-through to **recoil** and triggers the **camera shake**.

All of this happens in the same frame, so the rock's flash, the recoil, and the shake appear together.

Notes:
- The ray is cast **at impact**, from wherever you're looking at that moment. Turning away during the wind-up makes you miss, like a real swing.
- Hitting a wall or the floor within 3 m also recoils and shakes, because a pickaxe bounces off stone. Only rocks take damage.
- Without a pickaxe (deleted or disabled), `MiningController` falls back to hitting immediately on click, so mining still works.

### Inspector fields: more or less aggressive

**Quickest knob:** **Swing Intensity** scales every angle and offset. Try 0.7 for gentle and 1.3 for violent.

| Field | Default | More aggressive → | Gentler → |
|---|---|---|---|
| **Swing Intensity** | 1 | 1.2–1.5 | 0.6–0.8 |
| **Wind-up Duration** | 0.26 | shorter (0.18) | longer (0.35) |
| **Anticipation Duration** | 0.07 | longer (0.1) = more "loaded" | 0 = no pause |
| **Strike Duration** | 0.10 | shorter (0.07) = snappier hit | longer (0.15) |
| **Impact Duration** | 0.12 | shorter = sharper bounce | longer |
| **Recovery Duration** | 0.30 | shorter = ready sooner | longer = heavier |
| Total Swing Duration | 0.85 | *(calculated automatically, read only)* | |
| **Wind-up Angle** | (-32, 8, -10) | bigger negative X = raised higher | smaller |
| **Wind-up Offset** | (0.03, 0.06, -0.03) | more Y = hands lift more | |
| **Swing Angle** | (30, -12, 4) | bigger X = chops lower | smaller |
| **Swing Offset** | (-0.08, -0.08, 0.08) | more Z = lunges further forward | |
| Follow-through Angle | (12, -3, 2) | bigger = carries further on a miss | |
| **Recoil Amount** | 1 | 1.5–2 | 0 = no bounce |
| Recoil Angle / Distance | 9° / 0.035 m | bounce size per 1.0 of Recoil Amount | |
| **Camera Shake Amount** | 0.35 | 0.5–0.6 | 0 = off |

Angles are in degrees, relative to the resting pose, around the camera's axes:
- **X** tips forward/down (+) or back/up (−).
- **Y** turns right (+) or left (−).
- **Z** rolls.

Offsets are in metres:
- **x** is right.
- **y** is up.
- **z** is forward.

### CameraShake (on PlayerCamera)

| Field | Default | Meaning |
|---|---|---|
| Max Angle | 1.2 | Degrees at full strength. At the swing's 0.35 that's about 0.4°. |
| Max Offset | 0.006 | Metres of jitter at full strength |
| Frequency | 28 | Wobble speed |
| Decay | 7 | Higher = shorter shake (about 0.2 s by default) |

`PlayerLook` sets the camera's rotation every frame, so `CameraShake` never leaves an offset behind. It removes last frame's shake early in each frame (`[DefaultExecutionOrder(-50)]`) and adds a fresh one in `LateUpdate`. That's why `PlayerLook.cs` didn't need changing.

---

## 6. Changing the resting pose

**Small nudges:** move or rotate **`FirstPersonViewModel`** in the Inspector. Everything else follows, and `ViewModelMotion` treats wherever you put it as the new rest.

**Moving hands or the grip:** these values live at the top of `Scripts/Editor/ViewModelSetupTool.cs`. Edit them, then run **Ore What → Rebuild First-Person Viewmodel**.

| Constant | Meaning |
|---|---|
| `HandleEnd` | Bottom tip of the handle, in camera space (x right, y up, z forward, metres) |
| `HandleDir` | Direction from the handle end to the head |
| `LeftGrip`, `RightGrip` | Distance along the handle for each hand |
| `PickPlaneTilt` | How far the head's plane is leaned so its curve shows (0 = edge-on; the point leads the strike either way) |
| `ArmsPivot` | Point the swing rotates around (currently between the hands) |
| `ArmsModelScale`, `ArmsModelPosition` | Size of the WRAD arms and where the (off-screen) shoulders sit. Move the shoulders forward if a hand can't reach its grip. |
| `LeftElbowHint`, `RightElbowHint` | Points the elbows bend toward |

You can also just drag the `LeftElbowHint` / `RightElbowHint` objects in the scene; the IK reads them every frame.

Don't move `Arms` directly in the scene. `PickaxeSwing` records its starting pose as "rest" when you press Play, so a moved `Arms` would still work, but a rebuild overwrites it.

---

## 7. Test checklist

- [ ] On Play, two rigged hands hold the pickaxe diagonally in the lower right, fingers closed around the handle; the head is at upper right
- [ ] The pick's **point** (not the flat side) comes down onto the rock at impact
- [ ] During the wind-up and strike, the elbows visibly bend and straighten while the fists stay on the handle
- [ ] Standing still, the arms move very slightly (breathing and sway), not frozen and not wobbly
- [ ] Turning the mouse makes the arms lag slightly and then settle
- [ ] Walking gives a gentle bob; sprinting bobs faster; stopping settles it
- [ ] Clicking plays: a slower pull back over the shoulder, a short pause, a fast chop toward the crosshair, then a return
- [ ] The hands stay locked on the handle for the whole swing
- [ ] Hitting a rock within 3 m: the rock flashes **at the moment the chop lands** (not on click), the pickaxe bounces back, and the view shakes very slightly
- [ ] Swinging at the air: no bounce or shake; the pickaxe carries further down, then returns
- [ ] 5 landed hits still break a rock and drop 3 ore cubes
- [ ] Spam-clicking never restarts a swing mid-chop; the next swing can start during the return and blends in without a jump
- [ ] Walking right up against a wall or rock, the pickaxe never clips into it
- [ ] Clicking a rock, then turning away during the wind-up, misses (the hit is checked at impact)
- [ ] Escape, then clicking to re-lock the cursor, doesn't swing
- [ ] **Fallback:** disable `Arms`, and clicking a rock still damages it immediately

## 8. Troubleshooting

| Problem | Likely cause / fix |
|---|---|
| Arms not visible | Check that `PlayerCamera`'s **Stack** contains `ViewModelCamera` (Camera component → Stack), and that `ViewModelCamera`'s **Culling Mask** is only `ViewModel`. Re-running **Rebuild First-Person Viewmodel** fixes both. |
| Arms drawn twice or clipping into walls | `PlayerCamera`'s Culling Mask still includes `ViewModel`. Untick it. |
| Rock takes damage on click, before the chop lands | `MiningController → Pickaxe Swing` is empty or points to a disabled object, so it's using the no-pickaxe fallback. Assign **Arms**. |
| No camera shake | `PlayerCamera` needs a **CameraShake** component, and **Camera Shake Amount** must be above 0. |
| Swing feels sluggish | Lower the durations, especially Wind-up and Recovery, or lower **Mining Cooldown** on MiningController. |
| Pink arms | The arm material isn't using URP Lit. Select `arm_mat_pale` and set its shader to *Universal Render Pipeline/Lit*. |
| A hand comes off the handle at the end of the strike | The grip is out of arm's reach. Move the shoulders forward (`ArmsModelPosition` z), make the swing less extreme (lower **Swing Offset** z or **Swing Intensity**), then rebuild. |
| Fingers poke through the handle / hand looks open | Adjust **Finger Curl** on `ArmsModel` (higher = tighter fist). |
| Knuckles face the wrong way | Adjust **Hand Roll** on `ArmsModel`. |
| Changing Finger Curl etc. doesn't update the Scene view | The IK only runs in Play mode. Press Play to see changes, or re-run **Rebuild First-Person Viewmodel** (which resets fields to defaults). |

## 9. Where to go next

- **Sound:** play a "whoosh" when the strike phase starts and a "clink" in `MiningController.OnSwingImpact`.
- **Rock chips:** spawn a small particle burst at the ray's hit point in `ApplyHit`.
- **Different arms model:** any rig with upper arm → forearm → hand → finger bones works. Point the **Left Arm / Right Arm** fields on `FirstPersonArmsIK` at its bones. The grip markers and swing don't change.
- **Gloves / sleeves:** swap the arm material's texture, or parent a glove mesh to the `wrist.l` / `wrist.r` bones.
- **Multiplayer:** the viewmodel is for the local player only. Other players should see the third-person `Body` instead.
