# Old Mine boss arena environment

Implemented 2026-10-04. Environment only; no boss, enemies, encounter scripting, progression gates, or cutscenes. No Git commit was made.

## Location and scale

The endpoint remains `Environment/Island/Cave/BossArena`, centred approximately at world **(410, -18.75, 720)**, 36 m below the hub floor. The existing oval carving envelope is **68 x 60 m**, with a **24 m ceiling**. It was already large enough, so the cavern and all earlier Old Mine routes, loops, shaft inclines, and hub connection were retained.

The centre has an unobstructed **36 m diameter** combat disk, approximately 1,018 square metres. Most cover and equipment are around the perimeter. The old central raised platform, crystal, and central headframe were replaced with open natural rock floor and a single wrecked timber landmark at the far edge.

## Approach, threshold and reveal

The lower workings and final approach retain their existing route. The last stretch now has separated broken rail runs, timber stumps, fallen beams, overturned carts, exposed formations, and dead lamps. An isolated random seed keeps the new dressing from advancing the other mines' shared random sequence.

`OldMine_ArenaGate` now ends at hub-relative **(430, -36, 696)**. A new flat `OldMine_BossThreshold` turns into the arena, ending at **(418, -36, 711)**. The throat is approximately **19 m long**, **11 m wide**, and **10 m tall**. Two heavy, metal-banded timber frames define the threshold. Their overhead braces stay above the walking opening. A small crew-access notice and amber light distinguish it from ordinary mine supports. There is no door or barrier across the route.

The existing elevation changes and bends conceal the room through the Old Mine. All **24 sampled sightlines** from the adit, office, stope, MiningArea_02, DeepCavern, and final approach were blocked by generated rock. The room opens across the fighting floor toward the wrecked headframe when the player reaches the timber entrance.

The arena is the **end of the main route**, with the same entrance providing the return trip. No onward mining route or cross-mine connection was added.

## Landmark, story and lighting

A roughly **12 m tall wrecked winding headframe** stands near **(401, -18.75, 740)**. Broken rear posts, a fallen cap beam, a leaning brace, a pulley and dangling cable, a tilted working deck, and an overturned cart suggest a sudden collapse. A deserted supply desk, ledger, crates, barrels, stranded cart, and extinguished lamps reinforce the abandoned-crew story without explaining the future boss or parasite.

Warm amber entrance light, dim perimeter lamps, broad subdued floor fill, and a focused warm pool on the headframe keep the Old Mine's timber-and-work-light identity. The arena's cyan crystal lights and crystal centrepiece are gone. The arena floodlight uses its own mesh asset to avoid changing other sites' shared floodlight geometry.

## Reservation markers

All markers are data-only `MapZone` objects regenerated under `Environment/Island/Zones`:

| Marker | Purpose |
|---|---|
| `Reserved_OldMine_BossArena` | Clear central combat disk, radius 18 m; Mine 1 boss reservation. |
| `Reserved_OldMine_BossSpawn` | Empty future spawn region, radius 5 m. |
| `Reserved_OldMine_ArenaCenter` | Centre reference, radius 3 m. |
| `Reserved_OldMine_PlayerEntrance` | Entrance and return region, radius 4 m. |
| `BossZone_BossArena` | Existing boss-zone convention retained; tier 1. |

## Regeneration

`IslandMapBuilder.BossArena.cs` owns the targeted layout migration and arena dressing. The `_Layout: Old Mine Boss Arena` marker applies the short entrance change once, without resetting earlier spaces. Missing generated spaces are restored by the existing map builder; dressing is rebuilt every time. Repeated **Build Or Rebuild Island Map** runs retained the arena, entrance, and reservations.

`MapWalkabilityCheck` now includes the threshold in Old Mine routes and a 287 m arena perimeter/open-floor route. Room-relative waypoints keep that test valid if the arena moves. Checks copy the default NavMesh agent settings instead of adding permanent project agents.

## Validation

| Check | Result |
|---|---|
| Project compilation and Windows development build | Succeeded; **0 build errors**. Unity reported 496 build warnings, including package shaders, pipeline configuration, and mesh collision advisories. |
| Rebuild/regeneration | Passed repeatedly; scene and generated assets saved. One optional Crystal_Descent_1 decoration could not be placed on the last rebuild. |
| Check Mine Network | Passed; four mines connect only through the hub, with at least 8 m rock cover. |
| Standing / crouched walkability | **96/98 standing**, **98/98 crouched**. Standing exclusions are the intentional Collapsed_Crawl and Collapsed_Chamber. |
| Mine isolation with hub blocked | Passed. |
| Controller route checks | All routes passed with zero stuck spots, including **881 m** Old Mine main route, **755 m** return, **762 m** shortcut, and **287 m** arena perimeter/open floor. |
| Combat floor/headroom | **113 samples**, zero obstructions. Sampled floor height range was 0.78 m; controller tests passed. |
| Actual player in Play Mode | Approximately **1.96 km** through Old Mine, arena, and return using the actual player CharacterController; zero stuck spots. |
| Reveal and lighting | 24/24 early sightlines blocked; actual player-camera threshold/reveal captures reviewed in Play Mode. |
| Multiplayer | Fresh host/client build joined successfully with two avatars; movement began. **Full traversal remains unverified**: shell-owned, foreground, rendered, and Unity-owned launches all became suspended by the execution environment. The Unity-owned attempt stopped in the early mine; it did not reach the arena. All test instances were stopped. |

Local review captures, paths, build, and logs are in the ignored `Builds/OldMineArenaReview/` directory. `play-threshold.png` and `play-reveal.png` show the actual player-camera views; `final-validation.txt` and `play-walk.txt` contain the completed traversal results. Multiplayer logs and route CSVs are retained for a manual rerun outside this execution environment.

The later [Old Mine environment art pass](OldMineEnvironmentArt.md) supersedes the multiplayer limitation above: a fresh standalone host/client run completed the full route to this arena and the return to the hub with zero stuck spots for both players. It also adds perimeter maintenance wreckage, a collapsed loading gallery, matching stone colours and a narrower headframe spotlight.
