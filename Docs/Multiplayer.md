# Multiplayer (host & join)

Online co-op for up to 4 players: one player hosts, friends join with a 6-character code.
Single player (PLAY) is unchanged and never touches any network code.

## Packages

| Package | Version | Why |
|---|---|---|
| `com.unity.netcode.gameobjects` (Netcode for GameObjects) | 2.13.3 | networked objects, RPCs, scene sync |
| `com.unity.transport` (Unity Transport) | 2.6.0 | comes with Netcode |
| `com.unity.services.multiplayer` (Multiplayer Services) | 2.3.3 | Relay (join codes, no port forwarding) + Authentication |

## How a game starts

1. **HOST GAME** (main menu): anonymous sign-in → Relay server reserved for 4 players → Relay join code → host started →
   the map (`PlayerTest`) is loaded for everyone by Netcode's scene manager. The code is shown top-right in game.
2. **JOIN GAME** with the code: sign-in → join the Relay allocation → connect → the host sends the map.
3. Errors end up as a red line on the menu, never a stuck loading screen: invalid/expired code, game full,
   no internet / services unavailable, host unreachable, host ended the game, connection lost (20 s timeouts).
4. In game, **Esc** shows *Copy join code* and *Leave / End game*. Leaving returns to the menu; the host leaving
   ends the game and every client returns to the menu with "The host ended the game."

Relay is used directly, not through Multiplayer *Sessions*/Lobby: a session also opens a WebSocket to the Lobby
service, which failed its TLS handshake on the dev PC (likely antivirus HTTPS scanning / a proxy) and made
"create session" hang. Relay only needs HTTPS + UDP.

Dev / LAN: start the host with `-lan` (direct connection on port 7777) and type an IP address as the "code".

## Architecture

```
NetworkManager.prefab (created by the menu on first Host/Join, DontDestroyOnLoad)
  NetworkManager + UnityTransport + NetworkSessionManager (host/join/leave, messages) + MultiplayerHUD (code, Esc menu)
Per player:  NetworkPlayer.prefab (Netcode player object, owned by that player)
Per game:    NetworkWorld.prefab (spawned by the host when the map has loaded)
Items:       every item world prefab = DroppedItem + NetworkObject + NetworkTransform (owner auth) + NetworkItem
```

* **Your player is still the scene `Player`** – camera, input, HUD, first-person arms, inventory, equipment all local,
  exactly as in single player. `NetworkPlayerAvatar` (owner) copies its position, facing, Animator parameters,
  crouch, look pitch and headlamp into the network every frame and hides its own body. Other players see the
  CorporateMiner body there (interpolated), playing the same Idle/Walk/Run/Walk Backward/Jump/Fall/Land states,
  crouching (CharacterCrouchPose.ExternalAmount), tilting the head with the look, with their own headlamp.
  Nobody else ever moves your player. Spawn spots: host at the map start, others on a ring around it
  (ground-checked, clear of rock).
* **Host authority** (`NetworkWorld`, gameplay code reaches it only through `IWorldNetwork` / `WorldNetwork.Current`,
  which is null in single player):
  * **Rocks** stay plain scene objects (no NetworkObject); they are identified by their hierarchy path.
    `RockHealth.TakeHit` → request → the host applies it once (`ApplyDamage`) → hit/health shown to everyone;
    broken rocks go into a NetworkList so late joiners see them gone. Pickaxe, hammer, guns all go through TakeHit.
  * **Items**: `ItemDrops.Spawn` (ore drops, Q drop, G throw) → only the host spawns, as networked items.
    Pickup (`ItemPickupInteractor.TryPickup`) → request → the host checks reach + that nobody carries it, removes it
    once, and grants it to that one player (`GrantPickup`), partial amounts when the inventory is nearly full.
    Carrying asks the host for ownership; the carrier then simulates it with its own physics (others follow).
  * Players leaving with an item hand it back to the host (DontDestroyWithOwner).
* **Not networked on purpose**: first-person viewmodels, HUD, cameras, input, inventory/equipment contents,
  static map geometry, cave lighting.

## What other players see (remote presentation)

Local first-person views are never networked. Other players see your third-person body driven by small state + events
(`NetworkPlayerAvatar` sends, `RemotePlayerPresentation` on the remote copy shows; purely visual, can never deal damage):

| What | How it is sent | Remote result |
|---|---|---|
| Held item | `heldItem` NetworkVariable<int> (owner-written, only on change): NetworkWorld item index, -1 empty, -2 carrying | copy of the first-person item model switched on in the right hand bone; arms posed by a small two-bone IK to the baked hold pose (tilts with look pitch); empty = relaxed Animator arms |
| Headlamp | `headlampOn` NetworkVariable<bool> (owner-written from `Headlamp.Changed`, no delay for the owner) | that body's Spot Light on/off + `HeadlampGlow` sprite at the lens |
| Punch | `PunchRpc(right)` from `FistsController.Punched` | that arm goes guard → strike → back (baked from FistsController's own guard/strike poses) |
| Mining swing | `SwingRpc(kind, impactTime, endTime)` from `PickaxeSwing.SwingStarted` | the first-person swing itself, baked (Right / Left / Overhead, ~50 samples/s), played from its start; kept clear of the head |
| Shot | `ShotRpc()` (unreliable) from `WeaponController.ShotFired` | arm kick, muzzle flash particles + light, 3D shot sound |
| Reload | `ReloadRpc(duration)` from `WeaponController.ReloadStarted` | the first-person reload itself, baked over its progress (gun pose, support hand, magazine out / away / back in, slide or charging handle), stretched over the sent duration |
| Equip | the heldItem change | item raised into the hands over 0.25 s |

Hold poses are **baked** by Set Up Multiplayer (`RemoteHoldPoseBaker`): each first-person view is put on the real
shoulders like FirstPersonPresentation does, its own FirstPersonArmsIK is solved once, and the wrists / elbows / finger
joints are stored in look space; the item model copy is parented to the right hand bone at the pose it has in the real
hand. So remote players hold things exactly like their owner's third-person skeleton. **Re-run Set Up Multiplayer after
changing a first-person view, grip, hand pose or item model.** Late joiners get heldItem + headlampOn automatically;
one-off actions (punch, shot) aren't replayed.

**Swings and reloads are baked too** (same tool): `PickaxeSwing.BeginSwingPreview/PreviewSwingAt` and
`WeaponController.PreviewReloadPose` (editor-only, they run the components' own pose code) pose the first-person view
at each sample time, the view's IK is solved, and the arms (plus the magazine / slide / charging-handle bone positions)
are stored. Remote copies interpolate the samples, so their timing and phases are exactly the first-person ones.
The first-person poses pass through the head (the camera is inside it; up to 26 cm for the pickaxe overhead swing), so
the baker keeps the tool out of the head + hardhat (an ellipsoid fitted to the skull, hat and lamp vertices above the neck; Unity's skinned-mesh bounds were far too big): first the least forward tilt of the tool around the point
between the hands (up to 40°, both hands stay on the handle), then the smallest move along the one direction per swing
that needs the shortest arms; both smoothed over time. At runtime `KeepClearOfHead` re-checks against the real head
bone (looking up/down tilts the head less than the look) and the left hand is placed relative to where the right hand
actually got, so it stays on the handle even at full arm stretch.

Mining tools are held in the swing's own resting pose (the first-person idle) moved lower, forward and toward the centre
with the elbows down for third person (`ThirdPersonToolRest`: same grip on the handle; swing frames near the rest fade
into it), so swings start and end exactly on the hold; any switch between hold, swing and reload (or a swing started during the previous one's recovery) blends all arm
data over `transitionTime` (0.12 s). The rifle is shouldered for other players (`ShoulderLongGun`): the first-person
hand-on-gun grip is kept, the whole gun is placed with its butt at the right shoulder and the barrel along the look,
the support hand slides along the gun to the handguard and round to underneath, and the reload samples are re-based on
it (the hand still goes to the magazine). The pistol keeps its first-person hold.
Looking down, held poses follow the look pitch by turning around a point between the shoulders at the grip hand's rest height: an arm
held out level (pistol) turns at the shoulder, low holds (tools, rifle) tip in the hands. Turned around the shoulders, low
hands swung back through the body when looking down. Looking up they turn around the shoulders: the low pivot swung the
hands up and back, folding the arms behind the shoulders. A support hand that isn't on the item (pistol) turns at its own
height, and only when looking down; looking up it keeps its level pose.

## Setup / rebuilding

* Menu **Ore What → Multiplayer → Set Up Multiplayer** (re-runnable) builds the prefabs in `Assets/Prefabs/Network`,
  makes item world prefabs networked, registers them in `NetworkPrefabs.asset`, puts `MultiplayerMenu` on the main
  menu. Re-run it after adding items or changing the character body. Rebuilding the main menu keeps Host/Join.
* Unity Dashboard: the project (`cloudProjectId 0ad2bf69-…`) already has Authentication and Relay working —
  nothing to set up. A new project needs to be linked (Project Settings → Services) with Relay enabled.

## Testing

`NetworkSmokeTest` (does nothing without `-mptest`) drives a real host + client as two game processes:
```
OreWhat.exe -batchmode -nographics -mptest host -logFile host.log              (Relay; prints HOST_READY code='XXXXXX')
OreWhat.exe -batchmode -nographics -mptest client -joincode XXXXXX -logFile client.log
OreWhat.exe ... -lan -mptest host     /   ... -mptest client                    (direct, 127.0.0.1:7777)
OreWhat.exe ... -mptest probe -joincode ZZZZZZ                                   (one join attempt, logs the result)
```
Extra arguments: `-lan`, `-nettimeout <s>`, `-maxplayers <n>`.

## Limitations / next steps

* No host migration: when the host leaves, the game ends for everyone (they return to the menu with a message).
* Remote actions are procedural approximations (no third-person punch/swing/reload clips exist); the exact
  first-person swing curves, viewmodel sway and recoil patterns are not reproduced. Item pickup has no remote animation.
* Carrying a world ore shows a generic two-handed hold; the carried rock itself sits where the owner's physics puts it.
* No enemies or boss exist yet, so none are networked. When they are added: host-run AI, NetworkObject per enemy,
  damage requests through the same pattern as rocks.
* Players can't damage each other (weapons ignore the Player layer).
* Rock hit validation is basic (damage limit, rock exists); no rate limiting / line-of-sight check.
* Player health/stamina are local only (no shared death/respawn yet).
