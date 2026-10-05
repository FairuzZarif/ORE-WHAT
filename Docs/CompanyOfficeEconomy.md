# Company Worker and ore selling

The Company Office now completes the first mining economy loop. Approach the worker inside the existing hub booth, look at him and press **E**. Sell all stored ores or an entire ore type, then use Close, Escape or E to return to gameplay. Money appears above the health/stamina HUD.

## NPC and office

- Model: `My project/Assets/company_worker_npc/CorporateWorker.fbx`. `GoblinCorpWorker.blend` is preserved; no Blender modification or re-export was needed.
- The FBX includes old miner meshes, export cameras/lights, a large `CW_Studio_Floor` backdrop, and the separate 22-bone `CW_Rig` suit skeleton. The reusable `Assets/Prefabs/CompanyWorker.prefab` renders only the skinned `CW_` character meshes and preserves the embedded charcoal suit, ivory shirt, burgundy tie, skin, hair, shoes and sunglasses materials. Studio scenery and export camera/light GameObjects are removed, including their URP components. Filtering by the `CW_` name alone previously retained the studio floor, producing a roughly 451 m white plate whenever the worker prefab was placed; the prefab and setup filter are now corrected. No external texture dependency was found.
- Import uses a valid Humanoid avatar with 21 explicit human bone mappings. `Assets/Animations/Character/CompanyWorkerIdle.controller` plays the existing `Assets/CorporateMiner (4)@Breathing Idle.fbx` loop. Chest rotation changes during play; root motion is disabled and the worker stays stationary. No navigation, combat, dialogue tree or look-at system was added.
- Exact placement: `Environment/Island/Props/Hub/Booth COMPANY OFFICE/Company Workstation/CompanyWorker`, world **(487.21, 17.21, 728.73)**, yaw **114.458°**; booth-local **(0, 0.18, 0.95)**. He faces the customer approach behind the existing counter. The existing raised `Reserved_CompanyOfficeNPC` marker remains a reservation hint, rather than being mistaken for floor height.
- The existing counter moved from local Z -0.7 to 0, giving customers room in front. Three simple paper sheets sit on it. Existing walls, roof, sign, lamp, crate and barrel are reused. The worker is 1.78 m tall with a solid capsule, radius 0.28 m, leaving the entrance and hub routes clear.
- `CompanyOfficeSetupTool.DressBooth` is called by the existing hub booth generator. **Build Or Rebuild Island Map** was actually run: one worker appeared inside the same booth at the same workstation, and the front customer capsule was clear. The full regenerated map passed ore reservation and geometry audits with zero failures. Incidental regenerated assets/object-ID churn was discarded and setup reapplied to preserve the existing saved map; its 663 ore sockets and original geometry remain. The rebuild hook remains active.

## Interaction and UI

`ItemPickupInteractor` uses its existing nearest-hit look probe, now recognizing `CompanyWorkerNPC`. It shows `[E] Talk to Company Worker`. Both the local interaction and host sale validation require the customer to be inside the booth, within **2.5 m** of the worker's capsule, and have a clear ray to his talk point. Outside, side-wall and obstructed-counter checks were rejected.

The dark brown/gold uGUI menu shows each ore's quantity and stored value, total, type-specific SELL buttons, SELL ALL ORES, and Close. Empty selling is disabled. It creates the standard Input System EventSystem only if none exists, so real mouse buttons work before the player has ever died. Opening frees the cursor and suspends movement, mouse look, crouch, punch, mining and pickup input; other weapons/drop input already requires a locked cursor. Closing restores each component's previous state. Death closes the menu before `PlayerDeath` records disabled inputs, allowing normal respawn to restore controls.

A successful sale updates quantities and money, displays a three-second `+$earned` receipt, plays a short generated receipt chime, and gives a dry company line. Crystal sales use “Now that's company property.”

## Selling and authority

The existing seven-slot `PlayerInventory` remains the inventory and hotbar. Only Resource items marked `ItemData.CompanyOre` are bought; the flag is enabled on Copper, Iron, Gold and Crystal. Prices come solely from each existing **ItemData.Value**, not a second NPC price table. Entire stacks of the selected ore type, or all four types, are removed by `CompanySale` before the integer credit is applied.

Selected/held inventory ore is saleable because it still occupies the hotbar. Loose ore and physically carried ore stay world objects and are excluded, even when nearby or physics-owned by that player. **F** stores carried ore through the existing pickup path, making it saleable. Pickaxe, hammer, pistol, rifle, equipment and other unmarked resources are preserved.

`PlayerCurrency` stores whole dollars and exposes a balance-change event. In multiplayer, `NetworkPlayerEconomy` on each existing network avatar reuses `PlayerInventory` for the host-owned seven slots and keeps a separate wallet. One server-written account revision replicates the complete slots and balance together into the owner's scene inventory/HUD. Other peers receive that player's account balance for synchronization; there is no shared wallet.

The client requests only a service ID and optional item type. The host checks sender ownership, increasing request ID, living player, office bounds, range and line of sight, then prices its actual inventory and commits removal/credit synchronously. Snapshot publication waits until both changes finish. The local UI blocks pending requests; the host also rejects replayed IDs, and requests with fresh IDs cannot pay again after the ore is gone. No client total or reported quantities are accepted.

Pickups now enter those host-owned slots, using actual server capacity. Q/G inventory drops remove actual owned slots before spawning the shared object. The unrestricted client world-spawn RPC was removed, so a client cannot mint ore to collect and sell. The existing starting pickaxe is granted by the host from the scene's equipment configuration, rather than existing only in a local inventory view.

## Validation results

Final Windows development build succeeded with **zero errors**, 496 existing package/shader/import warnings, and a 335,723,296-byte build. No economy compile errors occurred.

Single-player acceptance: **198 passed, zero failures**, using actual physical pickups and existing hotbar/UI. An additional **10 passed, zero failures** verified real Input System mouse press/release events on Sell All and Close, rather than invoking button callbacks alone.

| Ore | One collected and sold | Ten collected and sold |
|---|---:|---:|
| Copper | $3 | $30 |
| Iron | $40 | $400 |
| Gold | $300 | $3,000 |
| Crystal | $4,000 | $40,000 |

- Mixed **10 Copper + 4 Iron + 2 Gold + 1 Crystal = $4,790**, exactly credited; all four ores removed while pickaxe, hammer and pistol remained.
- Individual Copper x2 sold for **$6**, preserving Iron x1; the remaining Iron sold for **$40**.
- Empty inventory produced no sale/credit. Pickaxe, hammer, pistol and rifle were not bought.
- Nearby and carried Gold were excluded. Storing the carried Gold made it eligible for **$300**.
- Short-range/inside-office/LOS validation, Escape and E closing, mouse closing, input suspension/restoration, receipt sound playback and death/respawn were checked.
- **Actual full gameplay loop:** equipped pickaxe hit a randomized Old Mine Copper node five times, breaking it through the real mining controller. All three physical Copper drops were collected through the existing look/pickup path. The real CharacterController then walked **58.2 m** back to the office, without teleporting on the return. The original **E** input opened the menu, Sell All paid **$9**, and E closed it. A temporary review-only path was calculated from actual colliders; no navigation was added to the worker or saved map.

Real separate Windows LAN processes: host **43**, client **38**, second client **25**, late joiner **9** checks passed; **zero failures** across all four. Host and two clients simultaneously opened their own menu at the same worker, then independently sold:

| Player | Host-verified physical pickups | Own sale/balance |
|---|---|---:|
| Host | Copper x10 + Gold x1 | $330 |
| Client | Iron x5 + Crystal x1 | $4,200 |
| Second client | Iron x1 | $40 |
| Late joiner | Existing starter pickaxe, no ore | $0 |

Every active player dropped and recollected an ore through the authoritative inventory path before selling. Out-of-office requests were rejected. Rapid clicks/repeated sale requests did not pay twice. A client-side injected fake Crystal did not earn money and was replaced by the host's true inventory snapshot. Client world spawning was refused. The late joiner saw all three existing balances independently. Client death with the menu open then normal respawn preserved **$4,200**, starter pickaxe and gameplay controls; the host confirmed the ledger afterward.

## Persistence and limits

Balances and inventory survive ordinary respawn because the same network account/avatar remains spawned. Late joiners receive existing players' current account state, but begin with their own **$0** and normal starting equipment. Disconnect/reconnect creates a new account; balances do **not** survive reconnection, stopping Play, restarting the host or a new session. This follows the project's existing lack of saves/profile persistence. Purchases, upgrades and access fees are not implemented yet.

Sound playback was programmatically checked; subjective listening remains a human check. Multiplayer review ran in hidden batch-mode players, so its black screenshots are not visual evidence; office presentation and the real mouse UI were inspected in Editor gameplay screenshots instead. LAN authority was tested; Relay/cloud services were not tested.

Local artifacts are under git-ignored `Builds/CompanyOfficeReview/`: `PlayMode4/sp-results.txt`, `PlayMode4/company-worker.png`, `PlayMode4/mixed-before-sale.png`, `PlayMode4/full-loop-receipt.png`, `MouseReview/actual-mouse-sale.png`, `Multiplayer1/*-results.txt`, map reports, and `Game/OreWhat.exe`. The opt-in `CompanyEconomyAcceptanceTest` does not run during normal play. No Git commit was made.
