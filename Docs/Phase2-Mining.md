# Ore What: Phase 2, Basic Mining

> **Current mining implementation:** resource-specific KayKit nodes, percentage-based crack stages, pickaxe-only mining and synchronized partial durability are documented in [KayKitMiningNodes.md](KayKitMiningNodes.md). The prototype description below is historical.

> **Updated in Phase 3.** The single `PickaxeVisual` object and the simple two-part swing described below have been replaced by first-person arms (`FirstPersonViewModel → Arms → hands + Pickaxe`) and a 5-phase swing. Damage is now applied when the strike lands, not on click. The rock, ore, and raycast parts of this doc are still accurate. See [Phase3-FirstPersonViewmodel.md](Phase3-FirstPersonViewmodel.md).

This phase adds a pickaxe, rocks you can break, and ore that drops out of them. There's no inventory, pickup, or currency yet; the ore just sits on the ground.

The movement and camera scripts from Phase 1 (`PlayerMovement.cs`, `PlayerLook.cs`) were **not changed**.

---

## 1. Quick start

**If you're using the test scene** (`Assets/Scenes/PlayerTest.unity`), it's already set up. Press **Play**, walk up to a grey rock, aim the white dot at it, and left-click.

**To add mining to any scene that has a Player**, click **Ore What → Add Mining Setup To Scene**. It's safe to run more than once, because it skips anything that already exists.

**Ore What → Build Player Test Scene** now includes mining automatically.

To set things up by hand instead, see [section 5](#5-manual-setup-step-by-step).

## 2. Controls

| Input | Action |
|---|---|
| **Left click** | Swing the pickaxe; mines a rock if you're aiming at one within 3 m |

Everything from Phase 1 still works (WASD, mouse, Space, Shift, Escape).

---

## 3. Files added

```
Assets/
├── Scripts/
│   ├── Mining/
│   │   ├── MiningController.cs   ← on the Player: click → raycast → damage rock
│   │   ├── PickaxeSwing.cs       ← on PickaxeVisual: the swing animation (visual only)
│   │   ├── RockHealth.cs         ← on each rock: health, hit flash, breaking, ore drop
│   │   └── Crosshair.cs          ← on the Player: small aiming dot (temporary)
│   └── Editor/
│       └── MiningSetupTool.cs    ← "Add Mining Setup To Scene" menu item
├── Prefabs/
│   ├── Rock.prefab               ← three grey spheres + RockHealth
│   └── PickaxeModel.prefab       ← pickaxe meshes from the Simple Caving Pack
└── Materials/Prototype/
    ├── Rock.mat                  ← grey
    ├── Ore_Copper.mat            ← copper, slightly metallic
    ├── Pickaxe_Handle.mat        ← brown wood
    └── Pickaxe_Head.mat          ← light metal
```

`Crosshair.cs` wasn't part of the request, but without an aiming dot it's very hard to know whether you're "looking directly at" a rock.

### About the Simple Caving Pack

The pack has the pickaxe in two formats:

- **`.glb` files** (Pickaxe.glb, Hammer.glb, Ingots.glb). Unity can't read these without the extra **glTFast** package, so they show up as plain files. They aren't used.
- **`SimpleCavingPack.blend`**. Unity reads this because Blender is installed on this PC. `PickaxeModel.prefab` is built from its "Pickaxe Handle", "Pickaxe Head" and "Pickaxe Head Base" parts.

The pack's own materials came in flat grey (the wood texture didn't carry over), so the prefab uses new brown and metal materials instead.

If you open this project on a PC **without Blender**, the `.blend` won't import. `PickaxeModel.prefab` will then be empty, but mining still works.

---

## 4. Hierarchy

```
Player                        CharacterController, PlayerMovement, PlayerLook,
│                             MiningController, Crosshair
├── Body
└── PlayerCamera              Camera, AudioListener
    └── PickaxeVisual         PickaxeSwing           ← the swing pivot = the "hand"
        └── PickaxeModel      (prefab: the meshes; no scripts, no colliders)

Rocks                         (empty parent, just for tidiness)
├── Rock                      RockHealth             ← root holds the script
│   ├── Main                  Sphere + SphereCollider + Rock material
│   ├── Lump_A                Sphere + SphereCollider + Rock material
│   └── Lump_B                Sphere + SphereCollider + Rock material
├── Rock (1)
└── Rock (2)
```

Why is the pickaxe split into **PickaxeVisual** and **PickaxeModel**?

- **PickaxeVisual** is the pivot point: the player's hand. `PickaxeSwing` rotates this object, so the pickaxe swings around the grip instead of around its middle.
- **PickaxeModel** holds the meshes. You can later swap it for a different tool model without touching any scripts.

**PickaxeVisual** is a child of the **camera**, so it follows your view up and down and the pickaxe always stays in the same spot on screen.

---

## 5. Manual setup, step by step

Follow these steps if you want to build it yourself or set up a different scene.

### 5a. Pickaxe visual

1. In the Hierarchy, expand **Player** and right-click **PlayerCamera → Create Empty**. Name it **`PickaxeVisual`**.
2. Set its Transform:
   - Position: **X 0.45, Y -0.42, Z 0.55**
   - Rotation: **X 15, Y -5, Z 20**
   - Scale: **1, 1, 1**
3. Drag **`Assets/Prefabs/PickaxeModel`** onto `PickaxeVisual` so it becomes a child. Set its Position and Rotation to **0, 0, 0**.
4. Select `PickaxeVisual` → **Add Component → Pickaxe Swing**.

Whatever position and rotation `PickaxeVisual` has when you press Play becomes the resting pose. To reposition the pickaxe, move `PickaxeVisual` and never `PickaxeModel`.

### 5b. Mining controller

1. Select **Player** → **Add Component → Mining Controller**.
2. Drag **PlayerCamera** into the **Player Camera** field.
3. Drag **PickaxeVisual** into the **Pickaxe Swing** field.
4. Optionally, **Add Component → Crosshair**.

Both fields fill themselves in automatically if you leave them empty, as long as the camera and the pickaxe are children of the Player. Assigning them yourself is still clearer.

### 5c. A rock

1. **GameObject → Create Empty**, name it **`Rock`**, and set Position to where you want it with **Y 0**, so it sits on the floor.
2. Right-click `Rock` → **3D Object → Sphere**. Name it `Main`. Set Position **(0, 0.4, 0)** and Scale **(1.2, 0.9, 1.1)**.
3. Add one or two more spheres as lumps, for example Position **(0.45, 0.3, 0.2)** with Scale **0.6**.
4. Drag **`Materials/Prototype/Rock`** onto each sphere to make them grey.
   - To make a new material instead: **Project window → right-click → Create → Material**, set **Base Map** colour to grey.
5. Select the **`Rock`** parent → **Add Component → Rock Health**.
6. Drag **`Materials/Prototype/Ore_Copper`** into **Ore Material**.
7. To reuse it, drag `Rock` from the Hierarchy into `Assets/Prefabs` to make a prefab, then place copies.

Each sphere already has a **Sphere Collider**, which the mining ray needs to hit. Keep `RockHealth` on the **parent**; the controller finds it from whichever child sphere was hit.

---

## 6. How it works

### MiningController.cs (on the Player)

Each frame:

1. **Was the left mouse button just pressed?** If not, do nothing.
2. **Is the cursor locked?** If you pressed Escape, clicks are ignored. The click that re-locks the cursor also doesn't count as a swing, because this script runs just *before* `PlayerLook` (`[DefaultExecutionOrder(-10)]`).
3. **Is the cooldown over?** If less than 0.6 s has passed since the last swing, the click is ignored.
4. **Swing the pickaxe**, if there is one. This happens even if you miss, so every click gives feedback.
5. **Cast a ray** from the exact centre of the camera (`ViewportPointToRay(0.5, 0.5)`), up to **3 m**.
6. **Check what it hit.** The ray stops at the first collider, so a wall between you and a rock blocks mining. If that first collider belongs to a rock (`GetComponentInParent<RockHealth>()`), the rock takes **1 damage**.

With **Draw Debug Ray** on, each click draws a line in the **Scene view** for one second. It's **green** if you hit a rock and **red** otherwise. It's handy for checking range and aim.

| Inspector field | Default | Meaning |
|---|---|---|
| Player Camera | PlayerCamera | Where the ray starts |
| Pickaxe Swing | PickaxeVisual | Optional animation; leave empty and mining still works |
| Mining Range | 3 | Metres |
| Mining Cooldown | 0.6 | Seconds between swings |
| Damage Per Hit | 1 | Health removed per hit |
| Hit Layers | Everything | Which layers the ray can hit |
| Draw Debug Ray | on | Green/red line in the Scene view |

### PickaxeSwing.cs (on PickaxeVisual)

- `Swing()` starts one swing and returns `false` if a swing is already playing. That's what stops swings from overlapping.
- A swing has two parts:
  - **Chop:** the first 35% of the time. It speeds up as it goes, so the hit feels punchy.
  - **Return:** the rest of the time, eased smoothly back to the resting pose.
- It rotates by **Swing Rotation** and moves by **Swing Offset** at the bottom of the chop. At the end it snaps exactly back to rest.
- This script only animates. It knows nothing about rocks, so you can delete the pickaxe entirely and mining still works.

| Inspector field | Default | Meaning |
|---|---|---|
| Swing Duration | 0.45 | Seconds for the whole swing. Keep it **below** the mining cooldown (0.6). |
| Down Portion | 0.35 | Share of the swing spent chopping down |
| Swing Rotation | (75, -10, 10) | Degrees added at the bottom. **X** tips it forward/down. |
| Swing Offset | (-0.08, -0.05, 0.12) | Metres moved at the bottom (a little left, down, and forward) |

### RockHealth.cs (on each rock)

- Starts at **Max Health** (5).
- `TakeHit(damage)` subtracts health and **flashes** every mesh in the rock white for 0.1 s.
  - The flash uses a *MaterialPropertyBlock*. This changes the colour of just this one rock without editing the shared `Rock.mat`, so other rocks don't flash.
- At **0 health** it spawns **3 ore pieces** and destroys itself.
- Ore pieces are small **cubes** with the **Ore Material** and a **Rigidbody**. They pop out with a small upward push and then roll to a stop on the floor.
- You can set an **Ore Prefab** instead. If you do, that prefab is spawned instead of the auto-made cubes, and it's pushed only if it has a Rigidbody.

| Inspector field | Default | Meaning |
|---|---|---|
| Max Health | 5 | Hits needed to break the rock |
| Flash Color | White | Colour shown when hit |
| Flash Duration | 0.1 | Seconds |
| Ore Count | 3 | Pieces dropped |
| Ore Prefab | none | Optional custom ore object |
| Ore Material | Ore_Copper | Used for the auto-made cubes |
| Ore Size | 0.2 | Cube size in metres |
| Ore Pop Speed | 2.5 | Speed in m/s that pieces fly out at |

---

## 7. Test checklist

- [ ] Pressing Play shows the pickaxe in the lower right and a white dot in the centre
- [ ] Left-clicking anywhere swings the pickaxe forward and down, and it eases back up
- [ ] Clicking rapidly doesn't restart or stack swings, and you get at most one swing every 0.6 s
- [ ] Aiming at a rock from close up and clicking makes the rock flash white
- [ ] Aiming at a rock from farther than about 3 m does nothing to it (the pickaxe still swings)
- [ ] Aiming just beside a rock does nothing to it
- [ ] Clicking a wall, crate, or the floor does nothing except swing
- [ ] The **5th** hit on the same rock destroys it, and **3 copper cubes** pop out and settle on the floor
- [ ] Other rocks still take 5 hits each (health isn't shared)
- [ ] After Escape, clicking the Game view re-locks the cursor **without** swinging
- [ ] With **Draw Debug Ray** on, the Scene view shows green lines on rock hits and red on misses
- [ ] **Robustness:** delete or disable `PickaxeVisual`, press Play, and rocks still break

---

## 8. Troubleshooting

| Problem | Likely cause / fix |
|---|---|
| Clicking a rock does nothing | Check that the rock (or its child) has a **collider** and the **parent** has `RockHealth`. Check that you're within 3 m. Turn on Draw Debug Ray and look at the Scene view. |
| Pickaxe doesn't appear | `PickaxeModel` may be empty (Blender not installed), or `PickaxeVisual` is outside the camera's view. Check its position. |
| Pickaxe swings around its middle instead of the grip | You moved `PickaxeModel` instead of `PickaxeVisual`. Reset `PickaxeModel` to position/rotation 0. |
| Swing looks cut short | **Swing Duration** is longer than **Mining Cooldown**. The swing just ignores the next click, but keep duration below cooldown. |
| Pink rock or ore | The material isn't using the URP Lit shader. |
| Ore falls through the floor | The floor needs a collider (the default Plane has one). |

## 9. Where to go next

- **Hit at the moment of impact.** Damage is currently applied on the click. For more weight, apply it when the chop reaches the bottom, for example with an event from `PickaxeSwing`.
- **Ore pickup and inventory.** Give the ore pieces a script and trigger collider, and collect them on touch.
- **Different rock types.** Duplicate `Rock.prefab` and change Max Health, colours, Ore Material, or Ore Prefab. The `Ingot` meshes in the Simple Caving Pack could become a smelted-ore prefab later.
- **Sound and particles.** `RockHealth.TakeHit` and `Break` are the natural places to add hit sounds and rock-chip particles.
