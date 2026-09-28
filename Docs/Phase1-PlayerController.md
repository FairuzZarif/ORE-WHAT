# Ore What: Phase 1, Basic First-Person Controller

This phase covers only a first-person player you can walk, look, jump, and sprint with, plus a simple test level made of primitives. It has no mining, items, UI, or multiplayer yet.

---

## 1. Quick start

1. Open `My project` in **Unity 6000.6.3f1**.
2. Wait for scripts to compile. The bottom-right spinner stops when it's done.
3. In the top menu bar, click **Ore What → Build Player Test Scene**.
   This creates and opens `Assets/Scenes/PlayerTest.unity` and places it first in Build Settings.
4. Press **Play**, then click inside the Game view.

If you edit the scene by hand, don't run the menu item again. It rebuilds the scene from scratch and overwrites your changes.

## 2. Controls

| Input | Action |
|---|---|
| **W A S D** | Move |
| **Mouse** | Look around |
| **Space** | Jump (only while on the ground) |
| **Left Shift** (held, while moving forward) | Sprint |
| **Escape** | Unlock and show the cursor |
| **Left click** (in the Game view) | Lock the cursor again |

---

## 3. Files added

```
My project/Assets/
├── Scripts/
│   ├── Player/
│   │   ├── PlayerMovement.cs        ← walking, sprinting, jumping, gravity
│   │   └── PlayerLook.cs            ← mouse look + cursor locking
│   └── Editor/
│       └── PrototypeSceneBuilder.cs ← menu item that builds the test scene
├── Materials/Prototype/             ← created by the builder (Floor, Wall, Obstacle, ...)
└── Scenes/PlayerTest.unity          ← created by the builder
```

`Scripts/Editor/` is a special folder name in Unity. Code in it runs only inside the Unity Editor and is never included in a built game. That's why the scene builder lives there.

---

## 4. How the player is put together

```
Player                (CharacterController, PlayerMovement, PlayerLook)   ← turns left/right
├── Body              (capsule mesh, no collider, only visual)
└── PlayerCamera      (Camera, AudioListener) at eye height 1.6 m          ← tilts up/down
```

The rotation is split between the two objects on purpose:

- **Left/right mouse** rotates the whole **Player** object. `PlayerMovement` moves along `transform.forward` and `transform.right`, so **W** always moves you toward where you're looking. The camera is a child of the Player, so it can never face a different horizontal direction than the body.
- **Up/down mouse** rotates only the **PlayerCamera**. If the whole body tilted, pressing **W** while looking at the sky would push you into the air.

### CharacterController settings

| Setting | Value | Meaning |
|---|---|---|
| Height | 1.8 | Player is 1.8 m tall |
| Radius | 0.4 | Player is 0.8 m wide |
| Center | (0, 0.9, 0) | Puts the capsule's bottom at the Player's feet (its pivot point) |
| Step Offset | 0.3 | Walks up ledges up to 30 cm tall without jumping |
| Slope Limit | 45° | Can't walk up slopes steeper than this |
| Skin Width | 0.05 | Small collision buffer that prevents getting stuck |

The **CharacterController** is Unity's built-in collider for players. When you call `controller.Move(...)`, it slides along walls instead of going through them, so wall collision needs no extra code. It isn't a physics Rigidbody, so it doesn't get pushed around by physics and it doesn't apply gravity on its own. `PlayerMovement` handles gravity itself.

---

## 5. PlayerMovement.cs

Runs once per frame in `Update()`:

1. **Read WASD** into a 2D direction. `ClampMagnitude` keeps diagonal movement from being faster than straight movement.
2. **Pick a speed.** It uses sprint speed if Shift is held and you're moving forward, and walk speed otherwise.
3. **Turn input into a world direction** using the player's facing: `right * x + forward * y`.
4. **Smooth it.** The current velocity `Lerp`s toward the target velocity, so starting and stopping feel slightly eased instead of instant.
5. **Gravity and jumping:**
   - While grounded, a small downward speed (`-2`) keeps the controller pressed onto the floor. Without it, `isGrounded` flickers on slopes and steps.
   - When Space is pressed while grounded, it sets upward speed to `√(2 · g · jumpHeight)`. This physics formula makes the jump reach exactly `jumpHeight` metres.
   - Gravity is added every frame.
6. **Move** with `controller.Move(velocity * Time.deltaTime)`. If you hit a ceiling, upward speed is reset so you don't stick to it.

Other scripts can read `IsGrounded` and `IsSprinting`, for example for footstep sounds or a stamina bar later.

### Tunable values (Inspector)

| Field | Default | Effect |
|---|---|---|
| Walk Speed | 4 | m/s when walking |
| Sprint Speed | 7 | m/s when sprinting |
| Acceleration | 12 | Higher is snappier, lower is floatier |
| Jump Height | 1.2 | Metres |
| Gravity | -20 | Stronger than real gravity (-9.81) because games feel better with snappier falls |
| Grounded Stick Force | -2 | Leave this alone unless you have ground-detection problems |

## 6. PlayerLook.cs

1. On **Start**, it finds the camera (if not assigned) and locks the cursor.
2. Each frame, it handles cursor lock. **Escape** unlocks it and **left click** locks it again. While unlocked, the view doesn't rotate, so you can click on editor windows safely.
3. It reads the mouse movement for this frame and multiplies it by **sensitivity**. Mouse delta is already the movement for one frame, so it's *not* multiplied by `Time.deltaTime`.
4. **Smoothing:** `SmoothDamp` eases the mouse input over `smoothTime` seconds, which removes jitter. Set it to 0 for raw input.
5. **Yaw:** rotates the Player body around the Y axis.
6. **Pitch:** adds to a stored angle, clamps it between `-maxPitch` and `+maxPitch` (default ±85°) so you can't flip over, and applies it to the camera's local rotation.

### Tunable values (Inspector)

| Field | Default | Effect |
|---|---|---|
| Camera Transform | PlayerCamera | The camera that tilts up/down |
| Mouse Sensitivity | 0.1 | Degrees per pixel of mouse movement |
| Smooth Time | 0.03 | 0 is raw; above about 0.06 starts to feel laggy |
| Invert Y | off | Flight-sim style controls |
| Max Pitch | 85 | Up/down look limit in degrees |

## 7. Input approach

The project uses Unity's **new Input System** package, which the URP template already enables. The legacy `Input.GetAxis` API is turned off. For simplicity the scripts read the devices directly:

```csharp
Keyboard.current.wKey.isPressed
Keyboard.current.spaceKey.wasPressedThisFrame
Mouse.current.delta.ReadValue()
```

This is the simplest approach and needs no extra setup. The template also includes `Assets/InputSystem_Actions.inputactions`, which already defines `Move`, `Look`, `Jump`, `Sprint`, `Interact`, `Attack`, and more. You could switch to it later to get rebindable keys and gamepad support. Only the "Read input" lines at the top of each `Update()` would change.

---

## 8. The test scene

Built by `PrototypeSceneBuilder.cs` using only cubes and a plane.

| Object | Colour | What it tests |
|---|---|---|
| Floor (100 × 100 m plane) | Dark grey | Ground detection |
| 4 perimeter walls (4 m tall) | Tan | Can't leave the area |
| Inner L-shaped wall | Tan | Sliding along walls and corners |
| Crate_Small (0.5 m) | Orange | Taller than the 0.3 m step offset, so you must jump |
| Crate_Medium (1 m) | Orange | Jumpable (jump height is 1.2 m) |
| Crate_Large (2 m) | Orange | Too tall to jump onto |
| Pillar, Block_Wide | Orange | Obstacles of different sizes |
| Step_1/2/3 (0.8 / 1.6 / 2.4 m) | Blue | Jumping up a staircase of platforms |
| Ramp (15° slope) | Green | Walking up slopes |
| Player (yellow capsule) | Yellow | Spawns at (0, 1, 0), 1 m above the floor, and drops down |
| Directional Light | — | Warm sun at 50°, soft shadows |

Materials use the **URP Lit** shader with only a base colour set.

### Test checklist

- [ ] Player drops onto the floor on Play and doesn't fall through
- [ ] WASD moves relative to where you're looking
- [ ] Looking straight up or down stops at about 85° and never flips
- [ ] Turning left/right turns the movement direction too
- [ ] Space jumps; holding Space doesn't fly; you can't jump in mid-air
- [ ] Shift + W is clearly faster
- [ ] Can't walk through walls, crates, or the pillar; you slide along them
- [ ] Can jump onto Crate_Medium and up the blue steps, but not onto Crate_Large
- [ ] Can walk up the green ramp
- [ ] Escape shows the cursor and stops look; clicking the Game view locks it again

---

## 9. Troubleshooting

| Problem | Likely cause / fix |
|---|---|
| "Ore What" menu missing | Scripts haven't compiled or have errors. Check the Console window (Ctrl+Shift+C). |
| Mouse look does nothing | Click inside the Game view to lock the cursor. |
| Pink/magenta objects | URP isn't the active pipeline. Check *Project Settings → Graphics*. |
| Error about `UnityEngine.Input` / "Input System package" | Some other script uses legacy input. These two scripts don't. |
| Player jitters or falls through the floor | Make sure the Player has no Rigidbody or extra collider, and only one CharacterController. |
| Look feels too slow or fast | Change **Mouse Sensitivity** on PlayerLook. |

## 10. Where to go next

The scripts are kept small and separate so new systems can sit next to them instead of inside them. Some natural next steps:

- **PlayerInteract.cs**: raycast from `PlayerCamera` forward to find and use things (ore nodes, tools).
- **Input actions asset**: switch to `InputSystem_Actions` for gamepad support and key rebinding.
- **Stamina**: read `PlayerMovement.IsSprinting`.
- **Multiplayer (friendslop co-op)**: only the local player should run these scripts. Remote players would disable `PlayerMovement`, `PlayerLook`, and their camera, and keep the yellow body visible.
