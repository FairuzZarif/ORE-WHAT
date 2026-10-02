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
* Other players' held tools/items and swing animations aren't shown yet (only the body); first-person stays local.
* No enemies or boss exist yet, so none are networked. When they are added: host-run AI, NetworkObject per enemy,
  damage requests through the same pattern as rocks.
* Players can't damage each other (weapons ignore the Player layer).
* Rock hit validation is basic (damage limit, rock exists); no rate limiting / line-of-sight check.
* Player health/stamina are local only (no shared death/respawn yet).
