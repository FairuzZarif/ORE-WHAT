# Old Mine environment art pass

This pass extends the generated Old Mine in `PlayerTest`. Its rooms, tunnels, elevations, side routes, old-incline shortcut and endpoint are preserved. No player, mining, inventory, weapon, animation or networking code was changed. All environmental placements regenerate through **Ore What → Island Map → Build Or Rebuild Island Map**. Nothing was committed.

## Existing assets inspected and reused

- Loafbrr CC0 Mines and Cave Set: reinforced posts, beams and braces, straight rail pieces and end stops, cart families including loaded carts, plank wall panels, damaged panels, platforms, ladders, rubble, rock wall forms and pillars. Typical examples: cart CA **88 triangles**, wall-rock A **124**, ladder **100**.
- Existing generated props: crates, barrels, desk and ledger, benches, winch, pump, cage, timber huts and shaft pulley.
- Existing mine lamp prefab (**60 triangles**), warm light materials, shared wood/iron materials and Old Mine rock material.
- Existing minable ore rocks, copper/iron/gold/crystal spawning, depth-aware cave atmosphere, corrected sign material and spatial placement system.
- Existing boss threshold, winding-headframe wreck, reserved combat floor and perimeter cover.

The existing held pickaxe model is approximately **12,220 triangles**. It was unsuitable for repeated small environmental tools, so chunky picks are built into the lightweight equipment kit instead. No new textures or materials were needed.

## New reusable mesh assets

These are generated once per rebuild and shared by every placement, under `Assets/Models/Island/Props/`. Tools are deliberately decorative and have no collider. Larger stations use simple grounded box colliders.

| Asset | Triangles | Uses |
|---|---:|---|
| MineToolRack | 192 | 3 copies: office interior, crew station, tipped No. 3 worksite rack. |
| MineStoresShelf | 192 | 4 copies: store room, abandoned workings, Dig Site 9. |
| MineCableReel | 264 | 6 copies: main/lower workings, shaft gallery and arena maintenance wrecks. |
| MineCrewTable | 192 | 1 copy: camp meal table, stools, cups, lunch tins and ledger. |
| MineRepairBench | 132 | 3 copies: main workings, lower cavern and Dig Site 9; vice, hammer and equipment box. |
| MineToolBundle | 120 | 8 copies: benches, foreman's desk, abandoned tools and arena wrecks. |
| MineOreChute | 132 | 1 copy: sorting junction; sloping timber tray and metal reinforcement. |

Overhead cable segments reuse Unity's cube mesh (**12 triangles** each); damaged galleries, rubble and structural pieces reuse existing prefab meshes rather than creating more unique assets.

## Section changes

| Section | Composition |
|---|---|
| Entrance | Maintained timber sequence, warm lamps, grounded cart, service rails and high wall-side cable runs lead into the crew camp. |
| Camp / office | The existing hut gains an interior tool rack, bench and warm lantern pool. A meal table replaces a generic stores cluster; shelving replaces store-room crate piles. The desk/ledger and existing signs remain. |
| Main workings | The stope becomes a sorting junction with a timber ore chute, a maintained main rail run and a branch toward the abandoned workings. Repair bench, tools and reel replace generic stores. |
| Abandoned workings | A tipped tool rack, leaning shelving, dropped tools and broken working canopy frame the existing derelict rails, stranded/tipped carts and dead lamps. Oversized rubble is reduced and grounded, revealing the abandoned table. |
| Lower mine | Broken rail runs, spare reel and leaning ladder at the safe shaft-gallery edge accompany the existing collar, pulley, guard rail and fallen cage. Angular perimeter rocks and a tall layered buttress distinguish the broad lower cavern. |
| Deep Old Mine | Dig Site 9 receives a repair station, shelving, cable equipment and tools left on the foreman's desk. Existing hut, meal-break remains and damaged supports stay. Infrastructure becomes sparse; exposed rock silhouettes become larger. |
| Final approach | Existing broken rails, snapped supports and abandoned carts remain the direction cues. Rubble gets a consistent stone palette and safer scale; damaged structures and dark pillar silhouettes keep the approach distinct. |
| Boss entrance | Paired heavy timber frames remain the defining threshold. The warm lighting and view toward the far wrecked headframe remain; the spotlight is narrowed to reduce spill onto cover. |
| Boss arena | The centre remains open. Maintenance reels, abandoned tools, broken working panels, rubble and a collapsed loading gallery strengthen the perimeter story. Cover rocks now use the Old Mine's existing stone palette. |

## Landmarks and story

Five main orientation anchors are the **crew camp**, **sorting rail junction**, **No. 3 collapsed worksite**, **Old Shaft No. 1**, and **boss threshold/wrecked winding operation**. These extend existing features rather than inventing new connections or bridges.

The early mine shows organized storage, meals and maintained equipment. Tools and shelving become tipped or abandoned in No. 3. Lower infrastructure shows ageing cables, spare shaft equipment and failed lifting machinery. Dig Site 9 contains unfinished maintenance and a deserted foreman's desk. Broken rails and supports culminate in a wrecked loading gallery and winding operation around an empty final chamber. Existing brief signage is retained; no new exposition system was added.

## Lighting

Two new non-shadowing point lights reveal the office interior and sorting chute. Existing early lamps are retuned to warm intensity 10; middle lamps generally use 6, the lower cavern 4 and Dig Site 9 5. Deep gaps and extinguished lamps remain, keeping the headlamp useful. Existing warm arena fill and wreckage spill remain, with the headframe spotlight narrowed to 30° and intensity 110. No new shadow-casting light was introduced.

The global cave atmosphere, headlamp and other mines' light plans are preserved. First-person review captures include both headlamp-off and headlamp-on views.

## Regeneration and placement safeguards

`IslandMapBuilder.OldMineArt.cs` builds the shared kit and composes Old Mine sections. `DressOldMineStores` replaces selected original stores at their reserved anchors. It consumes no extra shared random values, and the later art pass uses its own seed. Equipment is not layered over old crate clusters. New walls/rocks use actual visible mesh colliders, small tools/cables/planks are decorative, and traversal corridors remain reserved. Source rubble nearly ten metres wide is clamped to a three-metre footprint before grounding.

`OldMineArtReview.cs` is Editor-only acceptance tooling. It exports fresh multiplayer route CSVs, walks the actual player controller, checks the intentional crouched branch, captures the real player camera and performs a normal-speed visual survey over actual game frames. Review NavMeshes are prepared in Edit Mode, so Play Mode checks do not require imported models to be readable in a standalone build.

## Performance and validation

Final measurements and test results are recorded after the final rebuild in `Builds/OldMineArtReview/` (ignored local artifacts). The detailed results below distinguish completed checks from limitations.

| Measurement | Before this pass | After |
|---|---:|---:|
| Generated mountain/cave triangles | 199,411 | 199,411 |
| Old Mine dressing triangles, excluding arena | 28,411 | 30,189 |
| Arena prop triangles | 4,572 | 6,128 |
| Entire island prop triangles | 106,315 | 109,649 |
| Island prop mesh renderers | 1,794 | 1,892 |
| Active generated environmental lights | 124 | 126 |

Net geometry change is **+3,334 triangles**, approximately **1.1%** of the existing cave plus generated prop/lamp geometry. These are summed mesh-filter counts, including available rock LOD meshes; they are not a GPU/frame-time benchmark. Counts are taken in **Edit Mode**: Play Mode static batching shares large combined meshes between renderers, and naïvely summing those would greatly overcount geometry.

There are **18 shared-kit station placements** and **8 small tool bundles**, plus reused structural/rock/rail pieces. The collapsed loading gallery uses eight existing modules totalling **240 triangles**. The art group contains 97 mesh renderers, largely reused rail/cable modules. Across generated props the net increase is **98 renderers**; new stations replace earlier generic stores, so the added group count is larger than the net increase. No new textures, materials, shadow lights, gameplay objects or minable ore spawns were added. Static batching is retained, smaller decorative pieces have no collider, and shared mesh/material reuse keeps asset overhead small. No claim is made about measured multiplayer frame rate.

| Validation | Result |
|---|---|
| Compilation / Windows development build | **Succeeded**, 0 build errors. Unity reports 496 warnings, including existing package shaders, pipeline configuration and mesh-collision advisories. |
| Full island rebuild | Passed repeatedly; final rebuild about **8.9 s**. Scene and generated mesh assets saved. All authored Old Mine compositions survive regeneration. |
| Mine Network | Passed: four separate mines through the hub, rock cover at least **8 m**. |
| Standing walkability | **96/98 spaces**; the existing intentional Collapsed_Crawl and Collapsed_Chamber are the two exclusions. |
| Crouched walkability | **98/98 spaces**. |
| Mine separation with hub blocked | Passed; no cross-mine path. |
| Controller route checks | All map routes pass with zero stuck spots, including Mine 1 main **881 m**, return **755 m**, shortcut **762 m**, stores **48 m**, ore pocket **81 m**, and arena perimeter/open-floor route **287 m**. Other mines' routes also pass. |
| Actual player in Play Mode | **1,938.4 m**, zero stuck spots, including stores, No. 3 loop, complete shaft descent, ore pocket and old-incline shortcut return. |
| Actual crouched player | Collapsed drift/crawl/chamber round trip passes with zero stuck spots. |
| Continuous first-person survey | Normal-speed traversal over actual game frames, **1,180.7 m** to the arena, zero stuck spots. Additional stationary player-camera captures inspect all nine sections, with headlamp both off and on. |
| Boss arena | Large endpoint and heavy threshold retained. **113 central floor/headroom samples**, zero obstructions; sampled floor range **0.78 m**. Perimeter route passes. |
| Signs | **16 boards / 32 text faces**, all using the corrected depth-tested shader; nearest two boards **8.33 m** apart. Render test: **10,096** bright glyph pixels visible, **0** behind an opaque occluder. No sign shader changes were needed. |
| Standalone host + client, LAN | **Passed**, fresh development build. Client walks **1,182 m down / 755 m back**; host walks **1,185 m down / 756 m back**; **zero stuck spots** in all four runs. Both reach the actual arena centre, and both observe the remote avatar reach the arena and return to the hub. Both tests finish normally. |

The standalone test launcher resumes only its own two game processes if they become externally suspended; this resolved the execution-environment limitation encountered in the earlier boss-area task. The first run placed both return destinations at the same hub point and produced an expected avatar collision. The final run uses separate hub parking spots; no movement, collision or networking gameplay code was changed.

Review artifacts include `old-mine-nine-areas.png`, stationary and continuous first-person images, headlamp comparisons, `final-validation.txt`, `actual-player-walk.txt`, `actual-crawl.txt`, `visual-walk.txt`, `arena-signs.txt`, `sign-depth.txt`, `build-result.txt`, fresh route CSVs and host/client logs. The earlier crowded-parking run is retained as `host-first.log` / `client-first.log`. The player returns to the original spawn **(500, 18.233482, 500)**, the scene is saved in Edit Mode, test processes stop, and temporary build-generated project-setting changes are restored.

## Remaining polish

- Some long connector tunnels still repeat the existing modular timber rhythm. Bespoke damaged-frame silhouettes and additional rock-face compositions could improve later polish without expanding topology.
- The broad lower cavern deliberately keeps a sparse open floor; its perimeter equipment and buttress could support another close-range composition pass after enemy/ore encounter placement is decided.
- The build's existing pipeline/shader/collision warnings remain. This pass does not change those project-wide settings.
- An optional **Crystal_Descent_1 crystal decoration** cannot be placed on the final rebuild. This is outside Mine 1; its route remains reachable and walkable.
