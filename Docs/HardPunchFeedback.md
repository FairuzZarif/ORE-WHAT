# Bare-fist hard-surface feedback

Bare fists now produce a short thud, six small red droplets and a restrained recoil after a confirmed solid environmental contact. Air remains a swing/whoosh. This is cosmetic hand-injury feedback; it does not subtract player health or grant fists a mining capability. Crystal models and all ore assets are unchanged.

## Implementation report

1. **Existing collision:** `FistsController` already makes one `SphereCastNonAlloc` at the end of its Strike phase, selecting the nearest collider outside its player hierarchy. It ignores triggers and the IgnoreRaycast, ViewModel, DroppedItem and Player layers. Its existing centre ray refines a damageable target's body hit. The new effect reuses this contact; remote presentation never performs damage or mining casts.
2. **Classification:** ordinary solid colliders, including `RockHealth` nodes, receive hard feedback by default. Known player/character components are excluded first. Unmarked `IDamageable` targets retain their existing combat feedback, so future damageable enemies are also excluded. The optional `HardPunchSurface` marker opts an intentionally damageable environmental prop into hard feedback; it cannot override a recognized character. There are no object-name checks in gameplay classification.
3. **Thud audio:** existing assets provide swings, ore/pickaxe hits, footsteps and gun sounds, but no dedicated fist impact. `HardPunchImpactFX` synthesizes three cached, 170 ms mono PCM thuds following the existing procedural weapon/mining audio convention. A falling low tone and filtered contact tap avoid a pickaxe ring or firearm crack. Pitch varies only from 0.96 to 1.04. All hard materials currently share this sound.
4. **Blood:** one manually emitted burst of six red mesh droplets. Each is 1.2–2.3 cm wide and lasts 0.18–0.34 seconds, shrinking away. World-space simulation, no continuous emission, collision simulation, wall splats or decals. The existing larger `BloodSplatter` character effect is unchanged.
5. **Origin:** the confirmed hit point plus 3.5 cm along its normal, on the hand side of the surface. The spray points away from the surface. Initial overlaps are projected onto the already-confirmed collider when possible; unsupported non-convex mesh/terrain `ClosestPoint` queries are avoided. Invalid unresolved contact positions do not emit blood at Unity's unset world origin or inside the camera.
6. **Recoil:** the contacted hand withdraws 4 cm during the existing Hold phase and blends into Recover. The hard-contact camera kick is capped at 0.45 degrees before its existing axis weights. The shared camera hit nod/drop/roll runs at one-quarter strength for hard punches; character and tool reactions retain their previous strength. No new camera shaker or animation system was added.
7. **Duplicates:** only the Strike-to-Hold transition calls Impact, once per hand/punch. It emits once and raises one `HardSurfaceHit` event. The existing `HitLanded` notification remains available to its subscribers. The owner renders immediately; the network event excludes its sender.
8. **Characters and props:** existing `IDamageable.TakeDamage`, body-zone refinement, rigidbody impulse and host-authoritative player-hit feedback are preserved. Character punches produce their normal flesh blood/damage, without the environmental hand effect. Solid scenery acquires no damage/destruction behavior. Marked damageable props retain their own damage behavior.
9. **Ore durability:** three actual empty-hands punches against each Copper, Iron, Gold and Crystal node left durability at **5 → 5 → 5 → 5**. Fists never call `TakeMiningHit`. Hammer, pistol and assault-rifle regression attacks also left durability and drop counts unchanged. A real equipped pickaxe then reduced each tested node **5 → 4**.
10. **Cracks and drops:** each resource remained at crack stage 0 after every punch, with zero additional drops. Pickaxe damage then selected the appropriate durability-derived crack stage. Review-only health changes were restored after the single-player test.
11. **Multiplayer:** `NetworkPlayerAvatar` forwards only contact point, normal and seed through `HardPunchRpc(SendTo.NotMe)`. Each receiving machine plays its own pooled cosmetics. The existing right/left-hand `PunchRpc` still drives `RemotePlayerPresentation`. There are no particle network objects, damage RPCs, mining requests or persistent blood history for this effect. Every voice is fully spatial, has a 2.5 m full-volume radius, linear falloff and an 18 m maximum distance. Owner contacts remain within the full-volume radius.
12. **Validation:** the real CorporateMiner player passed the Play Mode matrix below, plus character and damageable-prop cases. The final rendered Windows host/client run passed both directions for rock and Copper ore, exactly one remote event, no owner echo, animated remote hands, particle expiry, stable network-object counts and unchanged shared ore state. A real host punch reduced the client's health **100 → 90**, produced exactly one existing flesh-blood event and no hard-surface burst.
13. **Limits:** collider-free decorative art still cannot be punched as a solid target. Existing forgiving melee reach is preserved. Materials share the same thud; dedicated wood/metal recordings remain optional. Playback, non-silent PCM data, peak limits and spatial settings were checked automatically, and rendered first/third-person frames were inspected; perceived sound quality still needs human listening on the player's audio setup. The pool supports eight simultaneous voices, reusing the oldest under heavier overlap. The Windows build succeeded with zero errors and one existing optional `RuntimePipelineConfig` warning.

## Test matrix

| Target | Thud | Hand droplets | Ore mining damage | Result |
|---|---|---|---|---|
| Cave wall | Yes | Yes | None | Pass |
| Cave/hub floor | Yes | Yes | None | Pass |
| Solid rock/boulder | Yes | Yes | None | Pass |
| Outside tree trunk | Yes | Yes | None | Pass |
| Timber support | Yes | Yes | None | Pass |
| Metal mining equipment | Yes | Yes | None | Pass |
| Copper node | Yes | Yes | None | Pass |
| Iron node | Yes | Yes | None | Pass |
| Gold node | Yes | Yes | None | Pass |
| Crystal node | Yes | Yes | None | Pass |
| Air | No | No | None | Pass |
| Character | Existing combat feedback | Existing flesh blood only | None | Pass |

Single-player: **218 checks, zero failures**. Final host: **58 checks, zero failures**. Final client: **51 checks, zero failures**. The review uses `UnarmedAttack.Punch`, equipped tool swings, actual gun firing, real colliders, live durability/crack/drop values and the actual network presentation. Earlier harness runs were corrected to wait for avatar placement and keep the observing body outside the fist's reach; the counts above are from the successful runs.

Local generated evidence, under the ignored `Builds/` directory:

- [Play Mode results](../Builds/HardPunchReview/PlayModeFinal/sp-results.txt)
- [Host results](../Builds/HardPunchReview/MultiplayerVerified/host-results.txt), [client results](../Builds/HardPunchReview/MultiplayerVerified/client-results.txt)
- [First-person Copper contact](../Builds/HardPunchReview/PlayModeFinal/sp-CopperOre-1.png), [tree contact](../Builds/HardPunchReview/PlayModeFinal/sp-tree.png), [Old Mine rock contact](../Builds/HardPunchReview/PlayModeFinal/sp-old-mine-rock.png)
- [Host observing client ore contact](../Builds/HardPunchReview/MultiplayerVerified/host-client-ore-observer.png)
- [Build summary](../Builds/HardPunchReview/build-summary.txt)

## Repeating the review

In Play Mode on `PlayerTest`, add `HardPunchAcceptanceTest` to a temporary object, set `role = "sp"` and `output` to a fresh folder before Start. It selects an empty slot, uses the scene's actual arms/attack path, tests all four ores and other weapons, and restores the player position, look, enabled movement/look state, selected slot, added items and ore HP at completion. Exit Play Mode to remove the temporary component and fixtures.

A development Windows build can run `-lan -punchtest host -punchreview "<fresh shared folder>"`; after `host-ready.signal`, launch another instance with `-punchtest client -punchreview "<same folder>"`. Both use the real multiplayer menu and LAN transport, write separate results and synchronization files, and quit on completion. Use a fresh folder for each run. These hooks do nothing during normal gameplay.

The effect creates at most eight reusable particle systems/AudioSources and three cached audio clips per scene. Idle systems have emission disabled, finished particles expire automatically, and the scene destroys pooled voices and generated materials/clips. No per-frame surface classification or permanent blood objects are created.
