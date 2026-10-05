# Ore What?
A cooperative first-person mining game ("friendslop" genre game). Built in Unity 6.

## Status
Early prototype. The island map has a Central Mining Hub and four mines, multiplayer, pickaxe-only mining, an inventory/hotbar, physical item pickup, a hammer, and two early weapons (pistol and assault rifle). Ore populations are randomized and respawn under host authority. No saving between sessions yet.

See [Ore distribution and respawning](Docs/OreDistribution.md) for socket counts, resource progression, economy settings, tuning and validation results.

See [Cave geometry and geology](Docs/CaveGeometry.md) for the angular cave pass, before/after views, geometry budget and traversal checks.

## Playing it
**Play in the Editor:** open `My project/` in Unity 6000.6.3f1 and press Play on `Assets/Scenes/PlayerTest.unity`.

**Play a build:** see `Builds/OreWhat-Windows/README.txt` for a standalone Windows build and its controls (also summarized below).

### Controls
| Action | Key |
|---|---|
| Move | W A S D |
| Sprint | Left Shift (while moving forward) |
| Jump | Space |
| Look | Mouse |
| Mine / swing / shoot | Left click |
| Reload (weapons) | R |
| Carry an ore | E (look at it); E again to put it down |
| Store in inventory | F |
| Pick up a tool/weapon | E |
| Select hotbar slot | 1–7, or mouse wheel |
| Throw held item | G |
| Drop one item | Q (Ctrl+Q drops the whole stack) |
| Free the mouse | Esc |

## Project layout
- `My project/` — the Unity project (note the space in the path; always quote it).
  - `Assets/Scripts/` — gameplay code, organized by system (`Player/`, `Mining/`, `Items/`, `Weapons/`, `ViewModel/`, `Editor/`).
  - `Assets/Scripts/Editor/*SetupTool.cs` — idempotent menu commands (**Ore What →** ...) that build/rebuild scene content (rocks, viewmodel, items, weapons, pickup highlights) from primitives and imported assets. Prefer running these over hand-editing the scene.
  - `Assets/Scenes/PlayerTest.unity` — the game (first scene in the build list).
- `Docs/` — a phase-by-phase design/implementation log (`Phase1-PlayerController.md` through `Phase8-ItemsPickupInventory.md`), written up as each feature was built. This is the most detailed reference for *why* something works the way it does.
- `Builds/` — local build output (git-ignored).

## Building
No CLI build script yet; use the Unity Editor (**File → Build Profiles → Windows → Build**).

## Version control
This project uses **Unity Version Control (Plastic SCM)** for `My project/`, not git, for day-to-day scene/asset work — see `.plastic/`. Top-level docs (this file, `Docs/`) are tracked in git.
