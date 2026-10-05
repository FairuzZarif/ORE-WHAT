# Map layout: the Central Mining Hub and four mines

The later [Old Mine environment art pass](OldMineEnvironmentArt.md) documents the regenerated dressing, shared prop kit, current 98-space validation, and first-person/multiplayer acceptance checks. [Ore distribution and respawning](OreDistribution.md) supersedes the historical fixed-node resource placement described below.

The island's cave is organised as one hub with four self-contained mines. Each mine has its own entrance in the hub, and the hub is the only connection between mines.

```
                 MINE 3 - CRYSTAL CAVERNS (N, Tunnel_02)
                              |
 MINE 1 - OLD MINE (SW) --- CENTRAL MINING HUB --- MINE 2 - DEEP MINE (NE, SideTunnel)
                              |          \
                  mountain entrance       MINE 4 - THE RIFT (SE, the great gate)
                  (ravine + MainTunnel)
```

## How it's built

Everything is still the island map system: every room and tunnel is a `CaveSpace` under `Environment/Island/Cave`, carved by **Ore What → Island Map → Build Or Rebuild Island Map**. Nothing else generates caves.

- `Assets/Scripts/Editor/IslandMapBuilder.MineNetwork.cs` holds the layout:
  - `MineSpaces`: every generated space, with its mine. Floors are relative to the hub floor (17.3 m).
  - `OriginalSpaces`: the original cave spaces, with the mine they now belong to and their new depth.
  - `RetiredSpaces`: the old cross-links that were removed.
  - `RenamedSpaces`: DeepRift became `Rift_Cavern`.
  - `MineFloorShapes`.
- **Apply Hub And Four Mines Layout** (menu, or automatically by the first rebuild) does the one-time layout change. A marker object `_Layout: Hub And Four Mines` under Cave records that it ran. After that, a rebuild only adds missing spaces, so hand edits to spaces stay.
- `Assets/Scripts/Editor/IslandMapBuilder.MineDressing.cs` holds the dressing, regenerated on every rebuild:
  - per-space lamp, rock and crystal plans, merged into `Dress()`;
  - the hub's booths and signs;
  - the four entrances, supports, rails, pipes, machines, crystals, giant columns, signs and zones.
- Each mine has its own rock colour. `IslandMapBuilder.CaveSubmesh` picks the cave material from the nearest space's mine:

  | Area | Material | Colour |
  |---|---|---|
  | Hub, Mine 1 | `Cave_Rock` | warm |
  | Mine 2 | `Cave_Rock_DeepMine` | steel grey |
  | Mine 3 | `Cave_Rock_Crystal` | blue-violet |
  | Mine 4 | `Cave_Rock_Rift` | near-black |

- Lamp colour also follows the mine (`MineLampColor`): warm, cold work light, pale cyan, red.

## The hub and the way in

**Outside → hub:** the ravine and the timber-framed MainTunnel lead straight into Cavern_01, the Central Mining Hub. Signs read "ORE WHAT? MINING CO. / CENTRAL MINING HUB" and "CENTRAL MINING HUB / MINES 1 - 4".

**Hub size:** enlarged to 60 × 50 m, with a big overhead work light. The hub side of every entrance is kept clear of props.

**Reserved spaces (physical only, no systems):** each has a `Reserved_*` MapZone.

| Space | Reserved for |
|---|---|
| ORE EXCHANGE booth | selling station |
| UPGRADES booth | mining upgrades |
| OUTFITTER booth | weapons and equipment shop |
| STORAGE booth | backpack and storage |
| EQUIPMENT booth | equipment station |
| COMPANY OFFICE booth | NPCs and services |
| Raised deck with stairs | overlook / announcements |
| Gathering area | benches around a lamp |
| "MINE ACCESS" board | future progression / key display |
| "COMPANY NOTICES" board | company information |

## The four mines

The Old Mine now includes its expanded shaft/lower workings and a distinct final boss threshold. Its existing 68 x 60 m arena has an open centre and wrecked timber headframe at the far edge. See [OldMineBossArena.md](OldMineBossArena.md) for the current approach, reservation markers, and validation results.

The route columns are walked in order; "Large area" is the space reserved for a future major encounter.

| Mine | Look | Main route | Loop / shortcut | Large area |
|---|---|---|---|---|
| **1 Old Mine** (warm, timber) | Old timber adit with old rails and a cart; timber sets throughout | Adit → Office (old camp, hut) → Drift 1 → Stope → Drift 2 → Ramp → MiningArea_02 (moved off Cavern_02) → DeepTunnel A/B/C → DeepCavern → BossGate → BossArena | Stope → Collapsed Drift → OreArea_01 (opens onto the hub). Side: the crouch-only crawl to a hidden ore chamber. | BossArena (timber headframe, rock columns) |
| **2 Deep Mine** (industrial, cold light) | Steel portal with hazard stripes, pipes, a pump; steel support sets, pipes along the tunnels | SideTunnel → SideCave → RailTunnel_Link (rails) → RailTunnel_2 → Junction B → RailTunnel_3 → CombatArea → Shaft No. 2 → Pump Station landing → the Hollow | DeepMine_Return: the shaft back up to Junction B. Side: Station 1 depot (rail branch), Junction B loading spur, Survey Station 4 (abandoned facility, story signs) | The Hollow (rock columns, machinery, floodlights) |
| **3 Crystal Caverns** (blue rock, crystal light) | Natural rock arch, big glowing crystal clusters, cyan light; no timber | Tunnel_02 → Cavern_02 → Crystal Cavern (raised crystal ledge) → Crystal_Descent 1–5 (upper route) → Crystal Geode | Crystal_Lower 1–5: a lower route straight from Cavern_02 down to the Geode (two ways down = a loop) | Crystal Geode (crystal shelf) |
| **4 The Rift** (black rock, darkness, red glows) | Massive concrete/steel gate with a torn-loose girder, heavy equipment (cable drum, winch), red warning lamps, "DANGER - EXTREME DEPTH" | Rift_Portal → Descent 1/2 → Rift Cavern (an overlook over the Chasm) → Lower spiral 1–3 → Ledge (a balcony in the Abyss) → Lower 4 → Landing → Lower 5 → Abyss floor (~100 m below the hub) → Deep Descent → Final Area | Abyss → ChasmLink → Chasm floor → the old survey climb (Climb 1–6, a squared spiral) → Rift Cavern. Side chamber off the Abyss. | Final Area (~72 × 60 m, raised shelf, giant columns) |

**Vertical spaces reserved for future traversal:** the Chasm (about 60 m from the cavern floor) and the Abyss (a 40 m tall cavern).

**Ore placement:** the existing system and items (copper, iron, gold, crystal) in pockets, side chambers, mining rooms and deep areas.
- Mine 1: copper and iron.
- Mine 2: iron and gold.
- Mine 3: crystal and gold.
- Mine 4: mostly crystal.

The mining system itself is unchanged.

## Rules learned while building it (keep them when editing the layout)

- **Separate mines:** no space may share air with a space of another mine, except through the hub. **Check Mine Network** finds leaks and thin walls between mines.
- **Room entries:** a descending tunnel must reach the room's floor at the room's wall (or end with a flat stretch). Inside the room's footprint a tunnel that is still above the floor leaves a cliff.
- **Switchback turns:** a leg that doubles back alongside the leg above (under about 70° from the reverse direction) carves under its end and leaves a cliff. Use wide turns, or start the leg flat (a one-step `stepHeight` with a long ramp, as on Crystal_Lower_3/5).
- **Giant rock columns:** their colliders come from the mesh's local bounds, full height down to the floor. A box shrunk around the centre floats and makes an invisible low ceiling.

## Tools

| Menu (Ore What → Island Map) | What it does |
|---|---|
| **Check Mine Network** | Layout only: leaks between mines, thin walls between mines, rock cover ≥ 8 m. |
| **Validate Walkability** | Temporary NavMesh with the player's limits (radius 0.4, step 0.3, slope 45°). Floods from the cave mouth standing and crouched, then blocks the hub and confirms no mine reaches another. |
| **Walk Test Routes** | Walks a CharacterController with the player's settings along every main route and loop, and reports stuck spots. |

## Validation (2026-10-03)

| Check | Result |
|---|---|
| Compile | OK. |
| Check Mine Network | OK. |
| Validate Walkability, standing | 72/74 spaces reachable; only the crouch-only crawl and the chamber behind it are not. |
| Validate Walkability, crouched | 74/74. |
| Validate Walkability, hub blocked | Mines separate. |
| Walk Test Routes | Every route walked and never stuck, about 3.4 km including all four loops. |
| Play mode | Screenshots of the hub, entrances and interiors; frames in all four deep areas; no game errors. |
| Multiplayer (NetworkSmokeTest, host + client on LAN, fresh build) | Join; both players mine the same rock and their hits add up; ore drops; one-time pickup; drop; carry; late re-join sees the broken rock; host ends the game. |
