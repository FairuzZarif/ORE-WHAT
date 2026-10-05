# Ore distribution and respawning

The four mines now use validated potential sockets and a randomized active population. The gameplay scene contains no permanent mineable nodes in Edit Mode. Copper, Iron and Gold retain their KayKit visuals; Crystal retains its original models and material. Decorative crystals remain scenery.

## Implementation

- `Assets/Scripts/Editor/OreSpawnSetup.cs` samples the authored `CaveSpace` layout after terrain and dressing are complete. It validates actual floor/wall raycasts, surface normals, a standing approach, cave clearance, prop colliders, decorative renderer bounds, exclusion areas and socket spacing. The sorted layout and fixed generation seed produce reproducible sockets; rebuilding preserves configuration edits.
- `Assets/Scripts/Mining/OreSpawnSystem.cs` stores the sockets on one map component and manages occupancy, active records and delayed replacement tickets. There are no individual socket GameObjects or per-socket update loops. Each socket records its source space, mine, depth, ground/wall surface, normal, approach, resource mask, rare eligibility and relative selection probability. Runtime occupancy is authoritative.
- `Assets/Mining/OreSpawnConfig.asset` references the existing shared Rock prefab and four existing ItemData assets. It exposes mine targets, resource weights, caps, minimum depths, refresh times, player distance, spacing and common-resource clustering preferences.
- `RockHealth` retains five health and three normal item drops. Its damage event tells the population scheduler when health changes or a node depletes. Spawn initialization selects the resource and refreshes its visual and hit-flash renderer cache. Drops originate outward along the socket's surface normal, so wall deposits do not release pieces inside solid rock.
- `NetworkWorld` owns a persistent `NetworkList<OrePopulation>` containing generation ID, socket index, resource index and health. Only the host initializes populations, accepts mining damage, produces shared drops and selects replacements. Clients instantiate ordinary local presentation objects from the host's records. Existing static prototype scenes retain their previous network path.

The former fixed-ore dressing plan now only reserves its historical spacing and consumes the same random draws, preserving the surrounding environmental dressing. It no longer instantiates ore. The map rebuild removes legacy nodes, including the three outdoor prototype rocks. Adding mining setup to an island that already has an ore system does not recreate outdoor test rocks.

## Final socket counts

Counts after **Build Or Rebuild Island Map** on the current authored map:

| Mine | Potential sockets | Active target | Copper compatible | Iron compatible | Gold compatible | Crystal compatible |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Old Mine | 273 | 30 | 148 | 125 | 138 | 0 |
| Deep Mine | 104 | 24 | 63 | 41 | 82 | 0 |
| Crystal Caverns | 80 | 20 | 48 | 32 | 69 | 28 |
| The Rift | 161 | 20 | 92 | 69 | 153 | 120 |
| Total | 618 | 94 | 351 | 267 | 442 | 148 |

Compatibility columns overlap: a deep ground socket can permit Copper and Gold, for example. Many Gold-compatible sockets do not imply many active Gold nodes; resource selection precedes socket selection and explicit caps apply. The expanded Old Mine needs more potential locations than the prompt's smaller 70–100 example.

## Economy and population rules

Prices remain centralized in the existing ItemData assets. Inspection found the older prices 10/20/50/75; these and the new-asset setup defaults were updated to the requested economy:

| Resource | Rarity | Value per item | Normal three-piece node value | Respawn time |
| --- | ---: | ---: | ---: | ---: |
| Copper | 1 | $3 | $9 | 90 seconds |
| Iron | 2 | $40 | $120 | 150 seconds |
| Gold | 3 | $300 | $900 | 360 seconds |
| Crystal | 4 | $4,000 | $12,000 | 600 seconds |

| Mine | Copper / Iron / Gold / Crystal weights | Gold cap | Crystal cap | Minimum Gold depth | Minimum Crystal depth |
| --- | --- | ---: | ---: | ---: | ---: |
| Old Mine | 66 / 31 / 3 / 0 | 2 | 0 | 0.58 | Unavailable |
| Deep Mine | 30 / 55 / 15 / 0 | 5 | 0 | 0.40 | Unavailable |
| Crystal Caverns | 8 / 37 / 48 / 7 | 11 | 2 | 0.40 | 0.60 |
| The Rift | 3 / 22 / 50 / 25 | 12 | 5 | 0.55 | 0.75 |

Depth is the authored CaveSpace progression value, not a physical distance. Old Mine Gold begins around the Old Shaft gallery/lower workings; the early collapsed chamber can no longer produce Gold. Crystal has zero weight, zero cap and no compatible sockets in both early mines.

Initial populations use stochastic rounding to keep each run near the intended mix. Old Mine Gold instead rolls a small binomial possibility, capped at two, so a run can contain zero Gold. Integer counts at a 30-node target cannot match every percentage exactly; two Gold nodes are 6.7%, while the configured probability remains 3%. Ordinary replacements use the configured weights and available caps. Active nodes plus reserved pending replacements respect rare caps.

Copper uses ground sockets and receives an early-depth location preference; Iron uses wall sockets and receives a deeper-location preference. Gold and Crystal can use both validated ground and wall pockets. Side/lower/abandoned/pocket sockets receive a 1.35 selection weight; final Old Mine approach sockets receive 0.3. Deeper rare sockets receive an additional location preference. These location preferences do not replace the resource weights.

Common resources have a configurable 15% opportunity to prefer nearby same-resource sockets within 9 metres, with a 2× location weight. Gold and Crystal receive no clustering bonus. All nodes still obey the 3-metre active spacing rule; generated sockets are at least 3.1 metres apart.

## Restricted areas

The generator excludes outside terrain and mountain exterior by requiring actual generated cave surfaces and an enclosed cave standing position. It explicitly excludes Entrance, MainTunnel, Cavern_01, OreArea_01 beside the hub, OldMine_Adit, OldMine_Office, its store doorway/storeroom, SideTunnel, Tunnel_02 and Rift_Portal. Hub/service/office/threshold spaces cannot supply sockets, and candidates near their actual cave volumes are rejected.

Boss exclusions include BossArena, OldMine_ArenaGate, OldMine_BossThreshold, DeepHollow and its final incline, Crystal_Geode and both final descent legs, Rift_Final and Rift_DeepDescent. Boss MapZone radii add a further two-metre exclusion margin. Ordinary ore density falls before the Old Mine final gate; the gate, threshold and encounter areas remain ore-free.

The decorative cyan/red cave crystals and imported decorative crystal props never gain RockHealth or ore-spawn participation.

## Respawning and synchronization

Depletion drops the existing three pieces once, frees the socket and removes the generation record. A replacement ticket rolls a permitted resource while considering active and pending caps. Its delay is the longer of the depleted resource's timer and the replacement resource's timer. Mining Copper therefore cannot turn a 90-second ticket into a 90-second Crystal jackpot.

At the deadline, the scheduler selects an empty compatible socket in the same mine, explicitly excluding the depleted socket. It checks all connected player avatar positions and the local controller, enforcing a 12-metre minimum distance. A blocked replacement remains pending; the scheduler does not exceed the target or spawn beside a player to fill a deficit. Moved colliders and dropped items are also checked before activation. The host polls every two seconds, without scanning cave geometry every frame.

Each replacement gets a fresh generation ID. The host removes the old ID from its lookup immediately, so stale hit requests cannot damage a replacement. Spawned-node hit requests validate equipped mining capability, configured pickaxe damage and host-side reach. Partial health changes drive the existing cracks on every peer. Late joiners read the current persistent list, including replacements and damaged nodes, without rolling their own population or replaying already-depleted nodes.

Session/world saving is not implemented; starting a fresh world produces a new random run. Changes to weights/timers/caps can be tuned on the asset. Depth availability or edited geometry requires rebuilding sockets.

## Validation and evidence

- **Ore What → Island Map → Rebuild Ore Spawn Sockets** regenerates only socket metadata from current surfaces and dressing, saving the scene.
- **Ore What → Island Map → Validate Ore Spawns** checks excluded spaces/volumes, boss areas, forbidden Crystal, early Gold, normals, support surfaces, props/scenery and duplicate positions. Selecting Island/OreSpawns shows socket normals and colors: green ground, cyan wall, yellow rare-compatible, red occupied.
- Full map rebuild: 618 sockets, zero validation failures. No permanent mineable nodes remain in Edit Mode.
- All 13 player-controller travel routes passed, including outside-to-hub, all four mines, optional Old Mine paths, the lower loops and the arena perimeter/open floor.
- Final single-player acceptance: **101 passes, zero failures**. All four resources depleted, freed their sockets, produced exactly three items with drop centers clear of cave surfaces, changed layout after replacement and maintained targets/caps. Blocking every socket with the distance guard deferred respawn; clearing the guard restored the target at a different socket.
- Real host/client Windows processes: **77 host passes and 73 client passes, zero failures**. Host depleted and replaced Copper, then partially damaged Iron before the client's late join. Client received identical current socket/resource/generation/health records, observed host Copper damage/cracks/depletion and mined Iron back on the host. Post-respawn records matched exactly, with no duplicate generations, occupied sockets, drops or excess population; drop centers also cleared cave surfaces on both peers.
- Cave context review: all four resources were reachable from their validated standing positions using the equipped pickaxe's normal swing/impact/raycast path. Each progressed from five to one health over four swings; 20 intact/damaged first-person captures were saved under `CaveContext/`. The Iron wall placement and original Crystal presentation were visually inspected.
- Short test timers were set on temporary configuration clones only. The saved asset retains 90/150/360/600 seconds and normal proximity settings.
- Windows development build succeeded with zero errors. Its 496 warnings come from existing package shader compilation/stripping and the missing optional RuntimePipelineConfig. Unity Console had zero errors after gameplay tests. The map rebuild retains the previously known decorative placement miss at Crystal_Descent_1.

Local evidence is under the ignored `Builds/OreDistributionReview/` directory: `sockets.txt`, `map-rebuild.txt`, `walk-routes.txt`, `build-final.txt`, `SinglePlayerWallDrops/sp-results.txt`, and `Multiplayer-03/` results plus expected/final layouts. `OreDistributionTest` is an opt-in development harness invoked with `-oretest host/client -orereview <folder>`; it does nothing in normal play.

Long-session economy balance, Relay/internet latency and player-built construction are not claimed as tested. The current timers and targets are exposed for playtesting. The editor was returned to Edit Mode with the original player spawn and no test components saved; no commit was made.
