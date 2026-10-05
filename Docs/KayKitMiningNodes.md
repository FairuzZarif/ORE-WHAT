# KayKit mining nodes — implementation and validation

The later [ore distribution and respawning system](OreDistribution.md) replaces fixed placement and updates the resource economy; the models and mining presentation described here remain in use.

The shared mineable-node prefab uses resource-specific KayKit presentation for Copper, Iron and Gold, with progressive cracks. Crystal retains its original model and material, following the user's correction. Mining requires an explicitly mining-capable equipped item. Host-authoritative partial durability survives late joining. Existing resource identities, five-point durability, three-piece drops, bonus-drop configuration, item values, inventory, dropped/carried meshes, weapon animations and combat damage remain in place.

## Exact assets

The imported pack was inspected, including its contents image, Unity FBX models, source meshes, bounds, triangle counts, URP materials and atlas. It contains matching copper/iron/gold nuggets, stone piles, bars, silver nuggets and other resource pieces. It has no crystal model or ready-made mineable-deposit prefab. Bars and large storage piles were unsuitable for these small mineable nodes.

The three metal-resource source models below live in `My project/Assets/KayKit_ResourceBits_1.0_FREE/Assets/fbx(unity)/`.

| Resource | Exposed mineral model | New visual prefab | Drop result |
| --- | --- | --- | --- |
| Copper | `Copper_Nugget_Large.fbx`, three rotated mineral pieces with the pack's atlas material | `Assets/Mining/CopperNodeVisual.prefab` | 3 × existing `CopperOre` |
| Iron | `Iron_Nugget_Large.fbx`, three pieces with a darker steel-blue atlas/material variant | `Assets/Mining/IronNodeVisual.prefab` | 3 × existing `IronOre` |
| Gold | `Gold_Nugget_Large.fbx`, three pieces with the pack's gold atlas material | `Assets/Mining/GoldNodeVisual.prefab` | 3 × existing `GoldOre` |
| Crystal | Original Rock-prefab `Main`, `Lump_A`, `Lump_B` sphere meshes/transforms and `Assets/Materials/Prototype/Rock.mat` | `Assets/Mining/CrystalNodeVisual.prefab`, preserving the original appearance | 3 × existing `Crystal` |

The three metal visuals use another `Iron_Nugget_Large.fbx` as a broad, gray, faceted host rock. Each source mesh has 100 triangles; each intact metal deposit has 400 triangles. Crystal keeps the original three sphere meshes (2,304 base triangles), material, relative positions/scales and procedural floor inset. The cyan/red cave crystal formations were never replaced and remain unchanged. The purple KayKit-derived crystal replacement was removed.

New materials in `Assets/Mining/`:

- `NodeStone.mat`: matte gray URP Lit host stone.
- `NodeIron.mat`: darker steel-blue URP Lit variant using the pack's atlas; metallic 0.25.
- `NodeCracks.mat`: shared dark URP Unlit, double-sided crack material.

`Assets/Mining/OreNodeCatalog.asset` maps the four existing ItemData assets to these visual prefabs. Twelve shared crack mesh assets are named `{Copper,Iron,Gold,Crystal}Cracks{1,2,3}.asset`.

`NodeCrystal.mat` was removed; the restored Crystal view references the existing Rock material. Crystal's crack meshes were rebaked for its original geometry. The bake updates live mesh buffers directly, so an editor restart is not needed to see changed overlay geometry.

`Assets/Prefabs/Rock.prefab` retains `RockHealth`, drop configuration and gameplay identity. `OreNodeVisual` selects a `Visual` child; its `OreNodeVisualView` owns the three local crack-stage objects. The root has two simple sphere colliders, and the visual children have no colliders or NetworkObjects. Editor generation retains links to the reusable visual prefabs. No imported KayKit model or texture was modified.

Dropped, physically carried, inventory and held-resource models were **not replaced**. Pickaxe/hammer/gun hand positioning and swing/reload animations were not edited.

## Damage and cracks

The original gun hit handler explicitly called `RockHealth.TakeHit(rockDamage)` when its ray hit a rock without IDamageable. Thus a bullet used exactly the same mining path as a pickaxe. The hammer's ItemData also had `canMine: true`, and the shared melee ray always called TakeHit when it found a rock.

The gun-to-mining call and obsolete rockDamage field were removed. Gun raycasts, nearest-hit selection, recoil, tracers, physics impulses, combat IDamageable damage, particles and ShotHit remain intact. Rocks continue blocking bullets.

The existing `ItemData.CanMine` capability is now the explicit distinction. Only Pickaxe enables it. Hammer disables it, including in its asset-creation utility. MiningController still allows a hammer's melee swing and combat hit, but submits node damage only for an equipped mining-capable item with a MiningToolController. `RockHealth.TakeMiningHit` also requires that capability and positive damage. Its authoritative mutation method is internal and explicitly named `ApplyMiningDamage`. RockHealth does not implement the generic combat IDamageable interface. Fists use their existing combat path and cannot mine.

`OreNodeVisualView.StageFor` derives presentation from `currentHealth / maxHealth`:

| Remaining durability | Stage |
| --- | --- |
| Above 75% | 0: intact |
| Above 50%, up to 75% | 1: light cracks |
| Above 25%, up to 50% | 2: medium cracks |
| Above zero, up to 25% | 3: heavy cracks |
| Zero | 4: depleted; all overlays disabled and the node destroyed |

The existing five-health, one-damage pickaxe means the accepted-hit sequence is `5 → 4 → 3 → 2 → 1 → 0`; crack stages are `0 → 0 → 1 → 2 → 3 → depleted`. The first hit retains the existing flash, chips and impact sound. The economy and durability were preserved instead of forcing exactly four hits. Percentage boundaries were additionally checked against maximum durability 4, 7 and 20.

Cracks are angular ribbon meshes baked in the editor onto the actual visible mesh triangles, with a 3 mm normal offset. Triangle clipping avoids spanning gaps between separate mineral pieces. Four side projections and a top projection cover procedural yaw variation. Increasing stages add paths and increase width from 2.2 cm to 3.4 cm to 4.8 cm. Only one overlay renderer is active on a damaged node. The meshes and material are shared; there is no runtime fracturing, per-hit mesh allocation, damage texture, custom shader, or separate network object per crack.

The three metal-resource stage meshes range from 504–518 triangles for light damage, 1,006–1,062 for medium, and 1,679–1,722 for heavy. Restored Crystal overlays have 811/1,665/2,897 triangles; including its original base geometry, heavy Crystal totals 5,201 triangles. These are shared baked meshes and geometry counts, not a measured GPU frame-time benchmark.

Health changes update cracks synchronously with accepted damage. Multiplayer waits for authoritative acceptance rather than predicting separate client health. The existing pickaxe impact callback still drives audio/chips and swing recoil. Depletion disables colliders immediately and destroys the root and all visual children together. Existing ore spawning still runs only once.

## Multiplayer authority

Nodes remain ordinary scene objects indexed by stable hierarchy IDs through the existing NetworkWorld. The host validates the RPC sender's synchronized held-item capability and checks damage against the host's configured tool controller. Non-mining equipped items cannot bypass the rule by submitting a mining RPC directly.

The existing server-written brokenRocks list still preserves depletion for late joins. A new server-written sparse `NetworkList<RockDurability>` persists `{node ID, remaining health}` for partially damaged nodes. Existing clients handle Add/Value updates; late joiners apply the received initial list. Each client derives its crack stage from that health, without networking individual crack objects. Depleted entries leave the partial-damage list and enter brokenRocks. Only the server spawns the shared ore drops.

## Validation results

The opt-in `MiningAcceptanceTest` harness exercised actual equipped swing animations and impact callbacks, the fist punch action, and the gun Fire/raycast/impact path. It did not substitute direct health changes for the pickaxe/gun matrix. Single-player tests used resettable test nodes and actual player-camera screenshots. Multiplayer used separate Windows development-build processes connected over real local UDP, through the existing Host/Join menu. Files coordinated test phases; assertions read the actual networked node/item state.

| Test | Result |
| --- | --- |
| Copper mining | PASS: five actual pickaxe impacts, all three crack stages, depletion, exactly three CopperOre pieces |
| Iron mining | PASS: all stages and exactly three IronOre pieces |
| Gold mining | PASS: all stages and exactly three GoldOre pieces |
| Crystal mining | PASS: all stages and exactly three Crystal pieces |
| Pickaxe | PASS: accepted damage and five impact-feedback events for every resource |
| Hammer | PASS: real swings/surface feedback, no health/crack/drop changes |
| Fist | PASS: two actual contact events, no health/crack/drop changes |
| Pistol | PASS: five shots hit/block on every resource, no damage; repeated on fresh/reset nodes |
| Assault rifle | PASS: five shots hit/block on every resource, no damage; repeated on fresh/reset nodes |
| Host mining | PASS: client observed iron HP 4/3/2/1 and every crack stage, followed by correct shared drops |
| Client mining | PASS: host observed gold HP 4/3/2/1 and every crack stage, followed by correct shared drops |
| Simultaneous mining | PASS: both players swung at one crystal node; one depletion and exactly three shared crystal pieces on both machines |
| Host non-mining attacks | PASS: four resources unchanged; client confirmed unchanged state |
| Client non-mining attacks | PASS: four resources unchanged |
| Non-mining RPC attempts | PASS: requests sent while hammer/fists/guns equipped were refused |
| Late joining partial damage | PASS: host damaged copper to HP 1/stage 3 before client launch; client received that state without another hit, then mined the final hit; three shared copper drops |
| Pickup/inventory/drop/carry regression | PASS: duplicate pickup granted one copper item; inventory 0→1, world items 19→18; dropping returned 18→19; carrying transferred ownership to the client |
| Rejoining depleted state | PASS: broken node remained gone and shared items survived rejoin |
| Map rebuild | PASS: final rebuild 8.6 seconds; 107 island nodes, plus three existing prototype nodes; all 110 have correct visual prefab sources |
| Collider/model checks | PASS: no old placeholder parts, no node MeshColliders, 1,760/1,760 isolated approach rays hit simple node colliders |
| Map traversal | PASS: all controller routes across four mines; 96/98 standing spaces and 98/98 crouched spaces; the two standing exceptions are the intended collapsed crawl spaces; mines remain separate when the hub is blocked |
| Actual cave-lighting review | PASS: intact and damaged nodes captured through the real player camera, with pickaxe and headlamp; node health/player state restored afterward |
| Play Mode / console | PASS: no Editor console errors during mining tests; no unexpected exceptions/errors in the mining host/client logs |
| Windows development build | PASS: final build succeeded with zero errors and 496 existing package shader/pipeline/collider warnings; no new C# warnings remain |

The final full single-player matrix recorded 179 passing checks and zero failures. The mining host and client each recorded 97 passing checks and zero failures. The existing networking regression deliberately tries an unreachable address first and logs the expected handled connection-timeout error; that negative test recovered, joined successfully, and both processes reached DONE.

After the user's Crystal-model correction, all 26 Crystal nodes were restored to the original visual and the map was rebuilt successfully in 9.0 seconds. The collider check again passed all 1,760 approach rays. A targeted Crystal-only Play Mode regression is recorded in `CrystalRestored/sp-results.txt`. The earlier multiplayer results precede this visual-only correction; mining authority and synchronization were not changed by the correction.

The map still reports inability to place the optional decorative `Crystal_Descent_1 crystal`, which predates this work. It does not refer to a mineable Crystal resource node and did not prevent traversal or mining validation.

Limits: attacks were automated through actual gameplay entry points rather than human mouse/keyboard input. Multiplayer ran on one machine over local UDP; separate-machine, Relay/WAN, high-latency and packet-loss scenarios were not tested. No target-hardware frame-time profiling or subjective human playtest was performed. No new break sound or standalone fracture/debris effect was added; the working impact feedback was retained.

## Review artifacts and repeatability

All artifacts are in the ignored `Builds/KayKitMiningReview/` directory:

- [Visual contact sheet](../Builds/KayKitMiningReview/resource-crack-stages.png): Copper, Iron, Gold, Crystal rows; intact, light, medium, heavy columns.
- `SinglePlayerFinal/sp-results.txt` and first-person stage PNGs.
- `CaveViews/`: actual mine/headlamp screenshots for all resources and durability states.
- `Multiplayer-01/host-results.txt`, `client-results.txt` and complete player logs.
- `InventoryRegression/host.log` and `client.log`.
- `map-validation.txt`, `final-rebuild.txt`, `build-report.txt`.
- `Game/OreWhat.exe`, `run-multiplayer.ps1`, `run-inventory-regression.ps1`.

Editor menu `Ore What → Mining → Set Up KayKit Resource Nodes` rebuilds the catalog/art and upgrades the shared prefab while retaining RockHealth configuration. `Ore What → Mining → Capture KayKit Crack Stages` renders the contact sheet. `Ore What → Island Map → Build Or Rebuild Island Map` selects the appropriate visual for each ore and preserves these nodes on future rebuilds.

The development build accepts `-kaytest host` or `-kaytest client`, with `-kayreview <fresh shared output directory>`. Start the client after the host's ready marker to exercise late joining. The existing inventory networking regression uses `-mptest host`/`client`. Test bootstrap is inert without those explicit flags and disabled in release builds.

No Git commit was created. Earlier map/environment changes and the user's imported KayKit pack were preserved. Build-only project-settings changes were restored to their pre-task snapshots. The saved scene is left in Edit Mode at its original player spawn.
