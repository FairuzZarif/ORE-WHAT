# Player damage, death and respawn

Players can hurt and kill each other (multiplayer) and die and respawn (single player too). Everything reuses the
existing systems: `PlayerAttributes` is still the one health value, weapons/tools/fists still damage anything that is
an `IDamageable`, and the host validates hits the same way `NetworkWorld` validates rock hits.

## Pieces

| Script | Where | What it does |
|---|---|---|
| `PlayerAttributes` (extended) | Player | Health 0–100 (`Max Health`). New: `Authority` (the host in multiplayer, null in single player), `ApplyAuthoritativeHealth`, `Revive`, `Revived` event, `DeathImpulse/DeathPoint` (the killing push). `Died` fires exactly once. |
| `PlayerDeath` | Player | Local death: switches gameplay input off (movement, look, crouch, mining, punch, pickup, drop, headlamp key), puts the hands away (`PlayerEquipment.SetHidden`), drops a carried ore, turns the CharacterController off, ragdolls the body, moves the camera up and behind the body (the body is drawn fully while dead). Single player: respawns by itself after the delay. Multiplayer: the host says when. |
| `DeathScreen` | Player | "YOU DIED / Respawning in N..." + progress bar + **[ RESPAWN NOW ]** button on the hotbar canvas (same font/panels as the HEALTH/STAMINA bars). The cursor is freed while dead (PlayerDeath locks it again on respawn); the button is dimmed until `respawnNowDelay` (1 s) and works once; it creates an EventSystem if the scene has none. The multiplayer Esc menu stays hidden while dead. |
| `HitConfirmFeedback` | Player | Your host-confirmed hits only: `Crosshair` hit marker, a damage number at the hit ("-20"; pop-in, floats up 0.55 m, fades in 0.75 s; red and bigger for a kill; quick hits spread out; pooled, max 12) and a synthesised **tick** — or the **kill thump/squish** instead for the killing hit (`WeaponSounds.HitTick/KillThump`; assign real clips to replace). |
| `BloodSplatter` | runtime | The burst (`BloodHit.prefab`: spray toward the shooter, exit spray, fine spray, mist; random counts) + 2–6 splats on surfaces found by rays from the hit (along the shot, down to the floor, a spray around); a splat lies 1 cm above the surface, rotated to its normal, only if all 4 corners are on that surface (else shrinks once or skips); never on players, loose items or moving bodies; 4 looks (droplets, medium, big irregular, streak) × random size/rotation; max 48 (oldest reused), fade out at 40 s. Same seed on every machine → same splats. Late joiners get no blood history. |
| `CharacterHitReaction` | each NetworkPlayer body (added at runtime) | The flinch others see: 0.22 s (gun) / 0.3 s (melee), the spine/chest/neck lean 9° / 14° along the hit direction (front → back, left → right…) plus a twist toward the side that was hit, then ease back. Runs after the arm IK and turns the upper body as one piece, so grips and the held item stay together; never while ragdolled; no physics or position change. |
| `DamageFlash` | Player | Red screen edges + a small `CameraEffects.Shake` whenever health drops. |
| `CharacterRagdoll` | the character's Animator object (local body and every NetworkPlayer body) | Builds an 11-part ragdoll from the humanoid skeleton the first time it's needed (Rigidbodies, capsule/box/sphere colliders, CharacterJoints; elbows/knees are one-way hinges, signs checked against the run clip). Alive = kinematic + colliders off. `Activate(push, point, velocity, maxSpeed)` / `Deactivate()` (Animator back on, `Rebind`). |
| `PlayerSpawnPoints` | static | Free standing spot inside the map's **PlayerStart** `MapZone`s (the island map has one at the start clearing, r 4): centre, then rings; ground under it, capsule free, ≥1.2 m from other players. Add PlayerStart zones for more spawn points. |
| `CombatSettings` | `Assets/Resources/CombatSettings.asset` | Friendly fire, respawn delay, ragdoll pushes, host hit tolerances. |
| `NetworkPlayerHealth` | NetworkPlayer prefab | Host-owned health (`NetworkVariable<float>`) and life state (`NetworkVariable<Life>`: dead, push, point, respawn time). `IDamageable` for remote bodies, hit validation, hit marker / blood / respawn RPCs. |
| `Crosshair` (extended) | Player | `Crosshair.ShowHitMarker(kill)`: four diagonal ticks around the dot, pop + fade 0.28 s; red and bigger for a kill. Hidden while dead. |
| `CombatSetupTool` | Editor | Menu **Ore What → Add Player Combat** (also called by MiningSetupTool / CharacterSetupTool; Set Up Multiplayer uses its blood prefab). |

## Damage values (where they live)

Each weapon keeps its own damage on its controller — the host reads the same fields from its own copy of the view
to validate a hit (`HeldItemController.PlayerDamage / AttackRange / AttackInterval / IsRanged`).

| Attack | Damage | Set in |
|---|---|---|
| Pistol | 20 per shot (5 hits) | `WeaponController.damage` (WeaponSetupTool Spec) |
| Assault rifle | 25 per shot (4 hits) | `WeaponController.damage` (WeaponSetupTool Spec) |
| Pickaxe | 20 per hit | `MiningToolController.playerDamage` (default) |
| Hammer | 25 per hit | `MiningToolController.playerDamage` (CombatSetupTool) |
| Punch | 10 (one damage event per punch: the punch's own single hit check + the host's one-hit-per-attack rule) | `FistsController.damage` |

Rock damage (`rockDamage`, `damagePerHit`) is unchanged.

### Body parts (hit zones)

The damage above is the **chest** value. Where a hit lands multiplies it (`CombatSettings`, `Assets/Resources/CombatSettings.asset`):

| Body part | Multiplier | Pistol | Rifle | Pickaxe | Hammer | Punch |
|---|---|---|---|---|---|---|
| Head (head + hard hat) | × 2.0 | 40 | 50 | 40 | 50 | 20 |
| Chest (chest, stomach, hips) | × 1.0 | 20 | 25 | 20 | 25 | 10 |
| Arms (upper arm, forearm, hand) | × 0.6 | 12 | 15 | 12 | 15 | 6 |
| Legs (thigh, shin, foot) | × 0.6 | 12 | 15 | 12 | 15 | 6 |

* **Hitboxes:** other players' bodies use the ragdoll's own 11 colliders (`CharacterRagdoll`: head sphere, chest + hips
  boxes, arm and leg capsules) as kinematic hitboxes while alive (`SetHitboxes`, switched on by `NetworkPlayerAvatar`
  while the body is shown and alive; Default layer, which every weapon / tool / punch ray hits). The NetworkPlayer's
  body capsule is back on the **Player** layer: it only stops players walking through each other and is never hit.
  When the player dies the same colliders become the ragdoll.
* **Which part:** the attacker's computer takes the part from the collider its ray hit (`CharacterRagdoll.TryGetZone`;
  a punch uses its aim line when that lands on the same player, else the sphere's touch point) and sends it with the hit.
* **The host checks it** on its own copy of the target (`DistanceToZone`, measured on the bones, so it also works for
  the host's own player): within `zoneTolerance` (0.35 m, for lag) → accepted; otherwise the host uses the part *it*
  finds nearest (`ClosestZone`). Claiming "head" for a knee hit gives a leg hit. The host animates other players'
  bodies even off screen (`AnimatorCullingMode.AlwaysAnimate`) so this check never uses a frozen pose.
* **Feedback:** head hits show a gold damage number and a higher tick; arm / leg numbers are a little smaller; kills
  stay red with the kill thump.

## How a hit works (multiplayer)

1. Your weapon code is unchanged: its ray hits one of another player's **hitboxes** (head / chest / arm / leg, on
   the Default layer — weapon rays leave out the Player layer, which is the local player; see Body parts) and calls
   `IDamageable.TakeDamage` → `NetworkPlayerHealth` on that player's copy → `DamageRpc` to the host
   (attack id = `HeldItemController.CurrentAttackId`, ray start, hit point, direction — **no damage amount**).
   Nothing changes locally. Remote animations never call it.
2. The host checks: friendly fire on, not yourself, target alive, attacker alive and holding something that can
   hurt (its item from the synced held item; carrying an ore can't), **one hit per attack** (every shot / punch /
   swing gets a new id; a request with the same or an older id is refused, so one punch can never hit twice), rate
   (token bucket refilling at 1.25 × the weapon's attack rate: 3 bunched gun hits, 1 melee blow), ray start within 2.5 m of the attacker's
   eyes, hit point within 1.5 m of the target's body, distance ≤ weapon range + 1.5 m, and nothing solid between
   (line cast; players don't block).
3. Accepted: the damage is the HOST's value for that weapon; health drops (`NetworkVariable`, sent only on change).
   Then, once each: the attacker gets `HitConfirmedRpc(point, damage, killed)` → `HitConfirmFeedback` (hit marker,
   damage number, tick — or the kill thump instead of the tick for the killing hit); everyone gets
   `HitRpc(point, direction, melee, seed)` → `BloodSplatter.Play` (the burst for all but the victim + the same
   surface splats everywhere, from the seed) and, for all but the victim, the body's flinch (`CharacterHitReaction`);
   the victim's `PlayerAttributes` updates → HUD bar + `DamageFlash`. A miss, a refused hit or a hit through a wall
   shows nothing anywhere.
4. At 0: the host sets `Life.dead` with the push (direction × 120 for guns, 70 for melee) and the respawn time.
   Owner → `PlayerDeath`; everyone else → `NetworkPlayerAvatar.SetDead` (collider off, held item hidden,
   presentation/IK and look tilt off, `CharacterRagdoll.Activate` with the push and the body's speed). Each machine
   simulates its own ragdoll from the same start; no bone is ever sent. Late joiners read `Life` on spawn and show a
   ragdoll (no push).
5. After `respawnDelay` (5 s) — or when the dead owner presses **RESPAWN NOW** (`RespawnNowRpc`: the host checks the
   sender is that player, they are dead and `respawnNowDelay` (1 s) has passed; one respawn per death) — the host picks a spawn spot, resets health and life, and sends `RespawnRpc(spot)` to
   the owner, who stands up there (`PlayerDeath.Respawn`) and teleports its NetworkTransform. Others: ragdoll off,
   item and animation back, the body hidden until the teleport arrives (≤ 1 s) so it never stands up where it fell.

Same NetworkObject throughout: nothing is despawned or duplicated.

Single player: `Authority` is null, so `PlayerAttributes.TakeDamage` applies locally and `PlayerDeath` counts down
and respawns by itself at a PlayerStart spot. (Nothing in single player damages the player yet; future enemies can
call `TakeDamage(amount, push, point)` or `IDamageable.TakeDamage`.)

The remote body capsule shrinks with the synced crouch (1.8 → 1.3 m), so shots over a crouched head miss.

## Friendly fire

There are no teams, so `CombatSettings.friendlyFire` is **on** (everyone can hurt everyone). Turn it off in
`Assets/Resources/CombatSettings.asset`; the host then refuses every player-on-player hit.

## Tests

* Single player (editor Play mode): damage, death (input off, ragdoll, death camera, death screen), respawn at the
  PlayerStart zone with full health/stamina and controls; repeated deaths; ragdoll pushes in 4 directions with knee
  and elbow bends compared against the run clip.
* Multiplayer (`NetworkCombatTest`, 3 real processes, LAN):
  ```
  OreWhat.exe -batchmode -lan -mptest chost -shots <dir> -logFile host.log
  OreWhat.exe -batchmode -lan -mptest cclient -shots <dir> -logFile client.log      (after HOST_READY)
  OreWhat.exe -batchmode -lan -mptest clate -shots <dir> -logFile late.log          (after READY_FOR_LATE)
  ```
  Results (`[CTEST] RESULT` lines): forged hit through a wall only the host has → refused; forged 1000 damage →
  20 (pistol); miss → no damage / marker / blood; pistol hits on a crouched client 100→80→60→40, one marker
  each; rifle on the strafing client 40→15→0 → dead + ragdoll on the host, collider off; shooting the dead body
  and a dead player's hits do nothing; client respawns at the start zone with 100 health, controls, camera, mining;
  client kills the host (both directions); a late joiner sees the dead host as a ragdoll and then its respawn;
  a punch after respawn 100→90. No errors in any log.

## Limitations

* Hit zones are four simple parts (head / chest / arms / legs) on 11 colliders; no neck or hand special cases. Lag
  tolerance is generous (no lag compensation / rewind): the host checks the reported body part within 0.35 m.
* The death camera is a simple rise-and-look; the uGUI death screen isn't part of camera renders (use a Game view
  screenshot to see it).
* Hit markers / blood show only for player-on-player hits (no enemies exist yet).
* Late joiners' ragdoll starts from the body's current pose with no push (they missed the hit).
* Inventory is kept on death (no item drop).
