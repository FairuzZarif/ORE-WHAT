# Ore What: Phase 8, Physical Items, Pickup and Inventory

Mined ore is now a real physics object:
- It pops out of the rock, tumbles, bounces a little and settles.
- You can kick it by walking into it.
- Look at it and press **E** to put it in your inventory.
- Press **Q** to toss it back out.

Movement, mouse look, camera bob, the pickaxe swings, mining damage, rock health and hit detection are unchanged.

```
Rock breaks ──► ItemDrops.Spawn ──► DroppedItem (Rigidbody + colliders + ItemData)
                                        │   physics: fall, bounce, roll, sleep
Player looks at it (camera ray) ──► "[E] Pick up Copper Ore" ──► E ──► PlayerInventory.AddItem
PlayerInventory ──► Q ──► ItemDropper ──► ItemDrops.Spawn ──► tossed DroppedItem
```

---

## 1. Scripts

**New, in `Assets/Scripts/Items/`**

| Script | On | Job |
|---|---|---|
| `ItemData.cs` | asset | One item type: id, name, icon, world prefab, value, stackable, max stack, optional pickup sound. |
| `DroppedItem.cs` | each world item | The item + amount; tunes the Rigidbody (sleep, gentle push-apart); the pickup animation. **No Update.** |
| `ItemDrops.cs` | static | The only place items are created (`Spawn`) and removed (`Despawn`). Object pooling would go here later. |
| `PlayerInventory.cs` | Player | Slots, stacking, `AddItem`, `SpaceFor`, `RemoveFromSlot`, `Count`, `TotalValue`, the selected slot, a `Changed` event. |
| `ItemPickupInteractor.cs` | Player | One camera ray per frame, the E key, the "Inventory Full" message, the pickup sound. |
| `ItemDropper.cs` | Player | Q drops one, Ctrl+Q drops the stack, 1–9 select a slot; spawn position and toss physics. |
| `PlayerItemPusher.cs` | Player | Kicks items the player walks into (see section 2). |
| `InventoryHUD.cs` | Player | Placeholder on-screen text: the pickup prompt, messages, the slot list. Easy to replace. |
| `Editor/ItemSetupTool.cs` | menu | **Ore What → Add Item Pickup Setup** creates everything below. Safe to run again. |

**Modified**

| Script | Change |
|---|---|
| `Mining/RockHealth.cs` | Only the ore-spawning part. The new **Ore Item** field makes each of the (unchanged) Ore Count pieces a DroppedItem, with the same pop speed plus a random spin. Pieces are placed apart so they don't spawn inside each other. There are optional **Bonus Drops** (item, amount, chance). With Ore Item empty, the old cubes are spawned exactly as before. |
| `Editor/MiningSetupTool.cs` | Also runs the item setup, so a freshly built scene has items too. |

**Created by the setup tool**
- Layers **DroppedItem** (7) and **Player** (8). The Player root is on the Player layer.
- Collision matrix: DroppedItem ignores Player and ViewModel.
- The Player's MiningController **Hit Layers** now exclude DroppedItem and Player.
- `Assets/Items/`: CopperOre, IronOre, GoldOre, Crystal (ItemData).
- `Assets/Prefabs/Items/`: their world prefabs.
- `Assets/Materials/Physics/DroppedItem.asset`: bounce and friction for all items.
- Rock.prefab's **Ore Item** is set to Copper Ore.

---

## 2. How dropped items get their physics

**The world prefab.** Each item's world prefab (for example `CopperOre.prefab`) has:
- A **Rigidbody**:

  | Setting | Value | Why |
  |---|---|---|
  | Mass | 0.25–0.6 kg | depends on the ore |
  | Linear damping | 0.05 | |
  | Angular damping | 0.35 | stops endless rolling |
  | Interpolate | on | smooth close to the camera |
  | Collision detection | Continuous Speculative | small, fast pieces don't tunnel |
- **Box colliders** on the chunk parts. Several colliders on one body make it tumble unevenly, like a real rock.
- The **DroppedItem** physics material: bounciness 0.25 (Maximum combine, so the item bounces even on the non-bouncy floor) and friction 0.55/0.65. Pieces hop once or twice, tumble, then grip and stop.

**Spawning.** `ItemDrops.Spawn(item, amount, position, rotation, velocity, spin)`:
1. instantiates the prefab (or a plain cube if there is none);
2. puts it on the DroppedItem layer;
3. sets the item and amount;
4. starts it moving.

**Settling.** `DroppedItem` raises the Rigidbody's sleep threshold (0.01), so a settled item falls asleep quickly and costs almost nothing. It also limits push-apart speed to 2 m/s, so pieces spawned touching separate gently instead of flying apart. **No custom code runs on items every frame.** The only per-frame code is the 0.15 s pickup animation.

**What items collide with**

| | Collides? |
|---|---|
| Floor, walls, rocks (Default layer) | yes |
| Other items | yes |
| Player | not physically. Items on the floor can never block, lift or trip the CharacterController. `PlayerItemPusher` kicks them instead: one small overlap check around the player per physics step, pushing items along the player's movement with a little lift so they tumble. |
| Mining ray | no, it passes through items to the rock behind |
| First-person arms and pickaxe | no |

**Measured** (a rock breaking into 3 Copper Ore):
- Each piece bounced once.
- They settled 0.7–1.2 m from the rock.
- They were asleep 1.2–1.7 s after spawning.

---

## 3. How pickup detection works

`ItemPickupInteractor` (on the Player), every frame:
1. Casts **one** thin sphere ray (radius 0.05 m) from the centre of the camera, up to **Pickup Range**. It hits items and anything that blocks the view (walls, rocks).
2. If the **first** thing hit is a DroppedItem, that item is the **Target**. Walls and rocks in front hide items behind them.
3. `PromptText` becomes "[E] Pick up Copper Ore" (or "Copper Ore x48" for a stack). It is null when looking away, when out of range, or once the item is picked up.
4. Pressing **E** calls `TryPickup(target)`.

Like mining, it ignores input while the cursor is unlocked. It never scans the scene.

The prompt and messages are drawn by `InventoryHUD` (simple `OnGUI` text under the crosshair). To use real UI later, read `ItemPickupInteractor.PromptText` and `.Message` from a Canvas script and remove InventoryHUD.

---

## 4. How inventory integration works

There was no inventory before, so `PlayerInventory` is new. It is the only one; everything uses it.

- **8 slots** (Slot Count). Each slot holds one item type and an amount.
- **Stacking.** `AddItem(item, amount)` tops up existing stacks of the same item first, then uses empty slots, up to each item's **Max Stack Size**. Copper Ore x4 + 1 = Copper Ore x5, in the same slot.

**On pickup:**
1. `AddItem` returns how many actually fit.
2. **All fit:** the item flies into the hands and shrinks (0.15 s), a soft "pop" plays, and it is removed with `ItemDrops.Despawn`.
3. **Some fit:** those are added, and the rest stays in the world with its amount reduced. "Inventory Full" is shown.
4. **None fit:** nothing is destroyed. "Inventory Full" is shown and the item stays where it is.

For other scripts (a shop, crafting, saving):
- `PlayerInventory.Changed`: an event when the contents change.
- `ItemPickupInteractor.PickedUp`: an event with the item and amount.
- Also available: `SpaceFor`, `Count`, `TotalValue`, `RemoveFromSlot`.

---

## 5. How dropping works

`ItemDropper` (on the Player):

| Key | Action |
|---|---|
| **1–9** | select an inventory slot (shown with ">" in the list) |
| **Q** | drop one item from the selected slot |
| **Ctrl+Q** | drop the whole stack as one object ("Copper Ore x48"); picking it up adds all 48 |

**Where it appears**
1. The spawn point is **Drop Distance** in front of the eyes, at **Spawn Height** (below eye level, around the hands). That's outside the player's body.
2. If a wall is closer than that, it spawns just in front of the wall instead of inside it.

**How it moves:** forward at **Drop Force** (m/s), plus **Upward Force** for a small arc, plus the player's own velocity (so it doesn't fall behind while running), plus a random spin of up to **Random Torque**. The effect is a casual toss, not an item appearing on the floor.

Other code can drop items with `ItemDropper.Drop(slotIndex, amount)`, or create them anywhere with `ItemDrops.Spawn(...)`.

---

## 6. How to create a new resource or item

1. **Data.** Project window → **Create → Ore What → Item Data**. Fill in:
   - Item Id: unique, e.g. `ore_diamond`
   - Display Name
   - Value
   - Stackable and Max Stack Size
   - Icon (optional)
   - Pickup Sound (optional)
2. **World prefab.** The quickest way is to duplicate one of `Assets/Prefabs/Items/*.prefab`, swap the mesh and material, and adjust the Rigidbody mass. Any prefab works if it has:
   - a **Rigidbody** and **colliders** (colliders using the DroppedItem physics material);
   - a **DroppedItem** component with its **Item** set to your new ItemData;
   - everything on the **DroppedItem** layer. `ItemDrops.Spawn` also sets the layer.
3. Drag the prefab into the ItemData's **World Prefab** field.
4. **Make it drop.** Either:
   - set a rock's **Ore Item** to it, or add it to the rock's **Bonus Drops** (for example Crystal, amount 1, chance 0.25); or
   - call `ItemDrops.Spawn(myItem, 1, position, rotation, velocity, spin)` from any script (chests, enemies, explosives).

No pickup, inventory or drop code needs changing for new item types: weapons, artifacts and consumables all work the same way. Equipping or using an item would be a new component that reads the inventory.

---

## 7. Inspector values

| Where | Field | Default | Controls |
|---|---|---|---|
| **Player → Item Pickup Interactor** | Pickup Range | 2.5 m | how far away items can be picked up |
| | Aim Radius | 0.05 m | how forgiving the aim is on small items |
| | Pickup Layer | DroppedItem | which layers are pickups |
| | Blocking Layers | everything except items/player/viewmodel | what hides items behind it |
| | Pickup Key | E | |
| | Collect Duration | 0.15 s | fly-to-hands animation (0 = vanish) |
| | Pickup Sound / Volume | built-in pop / 0.5 | |
| | Message Duration | 1.5 s | how long "Inventory Full" shows |
| **Player → Item Dropper** | Drop Key | Q | Ctrl+Q drops the stack |
| | Drop Distance | 0.7 m | how far in front of the eyes it appears |
| | Spawn Height | −0.3 m | relative to the eyes (negative = lower) |
| | **Drop Force** | 2 m/s | forward toss speed |
| | Upward Force | 1 m/s | the little arc |
| | **Random Torque** | 5 rad/s | maximum random spin |
| | Inherit Player Velocity | on | |
| **Player → Player Inventory** | Slot Count | 8 | |
| **Player → Player Item Pusher** | Push Strength / Lift Speed / Min Player Speed | 1.1 / 0.6 m/s / 0.3 m/s | how items react to being walked into |
| **Rock → Rock Health** | Ore Count, Ore Item, Bonus Drops | 3, Copper Ore, none | what a rock drops |
| | Ore Pop Speed / Ore Spin | 2.5 m/s / 6 rad/s | how the pieces burst out |
| **Item prefab → Rigidbody** | Mass, Linear/Angular Damping | per item, 0.05, 0.35 | weight, how fast rolling stops |
| **Item prefab → Dropped Item** | Sleep Threshold, Max Depenetration Speed | 0.01, 2 m/s | how soon items settle, how gently overlaps separate |
| **Assets/Materials/Physics/DroppedItem** | Bounciness, Dynamic/Static Friction | 0.25, 0.55/0.65 | bounce and grip for **all** items |
| **ItemData asset** | Value, Stackable, Max Stack Size | per item | |

**Tuning tips**
- **Bouncier items:** raise Bounciness on the physics material. Keep it under about 0.4, or items keep hopping.
- **Items settle faster:** raise Angular Damping on the prefab (0.5–1), or raise Sleep Threshold on its Dropped Item.
- **Harder throws:** raise Drop Force (4–6 is a real throw).

---

## 8. Measured in Play mode

| Test | Result |
|---|---|
| Rock breaks | 3 Copper Ore, each with its own Rigidbody on the DroppedItem layer; 1 bounce each; settled 0.7–1.2 m from the rock; asleep within 1.7 s |
| Look at ore | "[E] Pick up Copper Ore"; looking away clears it |
| E × 3 | Copper Ore x3 in **one** slot; each piece flew into the hands and was removed |
| Q | one ore tossed from 0.77 m in front (the player's radius is 0.4 m), about 2.2 m/s forward with spin; landed about 2.8 m away and fell asleep (that run used the earlier 2.5 m/s Drop Force; the default is now 2 m/s) |
| Full inventory + E | "Inventory Full", the ore stayed in the world; after freeing space, E picked it up |
| Ctrl+Q | dropped "Copper Ore x48" as one object, inventory emptied |
| Mining through an ore held on the ray | the ray hit the rock behind it (HP 5 → 4) |
| Walk (W) into an ore for 1 s | player moved 3.7 m, no height change; ore kicked 4.9 m ahead |
| Idle items | all asleep once settled |

---

## 9. Checklist

- [ ] Break a rock: ore pieces pop out separately, tumble, bounce a little, and stop
- [ ] Look at a piece: "[E] Pick up Copper Ore" under the crosshair; look away and it disappears
- [ ] Press E: it flies into the hands with a pop; the list (bottom left) shows Copper Ore x1, then x2, x3 in the same slot
- [ ] Q tosses one in front of you; Ctrl+Q tosses the whole stack as one piece
- [ ] Walk into loose ore: it gets kicked, and you never stumble or step up onto it
- [ ] Mining a rock with ore lying in front of it still damages the rock

---

## 10. Tools and weapons: throw and pick up (update)

> **Superseded in part by section 11.** Tools and weapons now go into the inventory like everything else, and picking one up no longer swaps what you hold.

The pickaxe is now an item too. You can throw it away (**G**) and pick it back up (**E**). Any tool or weapon added later works the same way.

**How it works**
- `ItemData` has a **Category**: Resource, Tool, Weapon, Equipment, Consumable or Artifact.
  - **Tools and weapons are held in the hands**, not stored in the inventory.
  - A tool with **Can Mine** ticked lets you mine.
- `PlayerEquipment` (on the Player) is the hand slot. It links each holdable item to its first-person **view**. For the pickaxe, the view is `FirstPersonViewModel` (the arms and pickaxe), shown while held and hidden when your hands are empty.
- **G** throws the held item:
  - The world pickaxe starts exactly where the held one was on screen (**Throw From** = the held `PickaxeMesh`), so it leaves your hands seamlessly.
  - It flies forward at **Throw Force** (6 m/s) with an upward arc and tumbles end over end (**Throw Spin**).
  - G is ignored until the swing's hit has landed, so you can't throw away a hit mid-strike.
- **E** on a tool or weapon puts it straight into your hands:
  - Hands empty: "[E] Pick up Pickaxe".
  - Hands full: "[E] Swap Pickaxe for Sword". The held item is dropped at your feet.
- **No tool, no mining.** `MiningController` ignores clicks unless the held item Can Mine. That is its only change (input only; damage and raycast untouched).
- The HUD shows "Holding: Pickaxe [G] throw" or "Hands empty".

**Assets** (made by **Ore What → Add Item Pickup Setup**)
- `Assets/Items/Pickaxe.asset`: Tool, Can Mine, not stackable.
- `Assets/Prefabs/Items/Pickaxe.prefab`: the same pickaxe model, with shadows, a box collider per part, and a 1.5 kg Rigidbody.
- **Rebuild First-Person Viewmodel** re-links the new viewmodel to the hand slot automatically.

**Measured**
- A throw left the hands at 6 m/s spinning end over end, peaked at 1.8 m, and landed about 7 m away. It was asleep within about 2 s.
- Clicking a rock with empty hands did nothing (5 → 5 HP).
- After E, the pickaxe was back in the hands and mining worked (5 → 4 HP).
- Swap: still holding a pickaxe, with the old one on the ground.
- G during the wind-up was ignored; G during the follow-through threw it.

**Adding a weapon or another tool**
1. **ItemData:** Category **Weapon** (or Tool), with **Can Mine** ticked if it should mine. Set the value, and set stackable off.
2. **World prefab:** model, Rigidbody, colliders, and **DroppedItem** with its Item set. Assign it as the item's **World Prefab**.
3. **First-person view:** build it under the camera, next to `FirstPersonViewModel`, and leave it inactive. Add an entry to **PlayerEquipment → Views**:
   - **Item:** the new ItemData.
   - **View:** the first-person object.
   - **Throw From:** the held model's transform (optional).

   An item with no view entry can still be held and thrown; you just won't see it in your hands.
4. Weapons have no attack yet. That would be a new component on the weapon's view, like PickaxeSwing is for the pickaxe.

**Inspector (Player → Player Equipment)**

| Field | Default | Meaning |
|---|---|---|
| Starting Item | Pickaxe | what you spawn holding (empty = bare hands) |
| Views | Pickaxe → FirstPersonViewModel | item → first-person object (+ Throw From) |
| Throw Key | G | |
| Throw Force | 6 m/s | ~2 = drop at your feet, 8+ = hurl |
| Throw Upward / Throw Spin / Random Torque | 1.5 m/s / 9 rad/s / 2 rad/s | arc, end-over-end spin, wobble |
| Swap Drop Force | 1.5 m/s | how the swapped-out item is dropped |
| Inherit Player Velocity, Blocking Layers | on, all but items/player/viewmodel | |

---

## 11. One inventory for everything, equipping and the hotbar (update)

The player now carries many items at once: ores, several tools, weapons, and later consumables. They all live in the **one** existing inventory. The item in your hands is just one of those inventory entries.

```
ItemData ─► InventorySlot (item + amount) ─► PlayerInventory (20 slots)
                                                   │
                                   PlayerEquipment ─┘ points at ONE slot (EquippedSlotIndex)
                                                   │
                                   that item's first-person view (e.g. FirstPersonViewModel + PickaxeSwing)
```

**How the inventory stores things**
- Every slot is `ItemData + amount`.
- **Stackable items** (ores) top up existing stacks first, then use empty slots, up to their **Max Stack**. With Gold Ore (Max Stack 50), adding 40 and then 30 gives a stack of 50 plus a stack of 20.
- **Non-stackable items** (tools and weapons have Max Stack 1) always take their own slot. Two pickaxes sit in two slots.
- **Capacity:** **Slot Count** on Player → Player Inventory. It is now 20.
- **Equippable:** comes from the item's **Category**. Tool and Weapon are equippable; Resource, Consumable and the rest are not. No item names are hard-coded anywhere.

**Picking up (E)**
- Every item goes into the inventory; the prompt is always "[E] Pick up <Display Name>".
- If it's equippable **and your hands are empty**, it is equipped automatically.
- If you're already holding something, it just goes into the inventory. **Nothing is swapped any more.**
- **Inventory full** (no free slot and no room in a matching stack): "Inventory Full", and the item stays in the world. A partly fitting stack leaves the rest on the ground.

**Equipping and the hotbar (1–5)**
- The hotbar is the equippable items in your inventory, in slot order. Ores never appear in it. For example: [1] Pickaxe [2] Axe [3] Pickaxe.
- Pressing a number:
  1. hides the current first-person view;
  2. points `PlayerEquipment` at that inventory slot;
  3. shows the new item's view.
- The item stays in the inventory while held.
- Switching is ignored mid-strike, the same as G.
- **Hotbar Size** (Player → Player Equipment, 1–9) sets how many number keys are used.

**Throwing (G)**
- G always throws the **currently equipped** item, whatever it is. The equipped ItemData decides what's spawned; nothing checks for "Pickaxe".
- It removes one of that item from its inventory slot, empties your hands, and spawns the physics object with exactly the same throw as before (Throw Force, Throw Upward, Throw Spin, Random Torque, starting from the held mesh).
- Other copies stay. With two pickaxes, throwing the one on key 3 leaves the one on key 1 in your inventory.

**Mining:** unchanged. `MiningController` still asks `PlayerEquipment.CanMine`, which now reads the equipped inventory slot's ItemData.

**Dropping resources (Q)**
- Number keys now belong to the hotbar, so **the mouse wheel** chooses the slot Q drops from. The slot is marked ">" in the list.
- Q drops one; Ctrl+Q drops the stack.
- Dropping the item you're holding with Q also empties your hands.

**HUD (bottom left)**
- "Holding: Pickaxe [G] throw" or "Hands empty".
- The hotbar row, with the held item in < >.
- The inventory list: occupied slots only, "(in hands)" on the equipped one, and "Inventory n/20 $value".

**Scripts**

| Script | Change |
|---|---|
| `ItemData` | `IsHoldable` renamed to **`Equippable`** (still decided by Category) |
| `PlayerInventory` | new `AddItem(item, amount, out slot)` overload (reports where it landed); default **Slot Count 20**; runs before the other item scripts |
| `PlayerEquipment` | now points at an inventory slot (`EquippedSlotIndex`); adds `EquipSlot`, `Unequip`, `EquipHotbar`, `RefreshHotbar`/`HotbarSlots`, and hotbar keys; G removes the item from the inventory; the auto-swap (`PickUp`, Swap Drop Force) was removed |
| `ItemPickupInteractor` | every pickup goes through the inventory; auto-equips only with empty hands; no "Swap" prompt |
| `ItemDropper` | slot selection moved from number keys to the mouse wheel |
| `InventoryHUD` | hotbar row, held line, compact list |
| `MiningController` | **unchanged** |

**Measured in Play mode** (the requested 23-step sequence)
- Start: holding the Pickaxe (slot 0); inventory [Pickaxe].
- Mined 2 rocks and picked up all 6 Copper Ore: **Copper Ore x6 in one slot**.
- G: Pickaxe thrown (1 in the world), removed from the inventory, hands empty.
- Click at a rock with empty hands: HP 5 → 5.
- E on the Pickaxe: back in the inventory **and equipped** (the hands were empty).
- E on a Test Axe: added to the inventory; **still holding the Pickaxe**. A second Pickaxe went into its own slot.
  - Hotbar: [1] Pickaxe [2] Test Axe [3] Pickaxe.
- 2: holding the Test Axe (it has no view yet, so no hands are shown).
- 3: holding the 2nd Pickaxe, with the viewmodel on.
- G: only the 2nd Pickaxe was thrown; the Pickaxe on key 1 and the Axe stayed.
- 2, then G: the Test Axe was thrown.
- Empty hands: HP 5 → 5. Pressed 1: Pickaxe back. Click: HP 5 → 4.
- Gold 40 + 30: stacks of 50 and 20.
- Inventory full (20/20, copper stack at 50): E on Copper Ore and E on a Pickaxe both showed "Inventory Full", and both stayed in the world.

**Adding a new tool or weapon (e.g. an Axe)**
1. **ItemData:** Category Tool or Weapon (Can Mine if it should mine), Stackable off.
2. **World prefab:** Rigidbody, colliders, DroppedItem.
3. **First-person view** (e.g. an AxeView with its own swing/attack controller): add it under the camera, inactive, and add a **Player Equipment → Views** entry (Item, View, Throw From).

Inventory, pickup, hotbar and throw need no code changes. An item with no view entry works but shows no hands.

**Limitations**
- The only view that exists is the pickaxe's; other tools show empty hands until they get their own view and controller.
- The hotbar follows inventory order; there is no drag-to-rearrange yet.
- Q's slot choice is mouse wheel only. There is no inventory screen yet.
- Inventory contents aren't saved between sessions.

---

## 12. Every tool gets its own view and attack controller + the Hammer (update)

Each equippable item now has its **own first-person view** and its **own attack controller**. The **Hammer** from the Simple Caving Pack is the second tool.

```
ItemData ─► PlayerEquipment.Views entry ─► first-person view (its own arms + tool mesh + swing)
                                               └── HeldItemController (the item's behaviour)
                                                     └── MiningToolController: swing, damage, cooldown
```

**The pieces**
- **`HeldItemController`** (new, abstract): the base for any item's behaviour. It sits on the root of the item's view and provides:
  - `IsBusy`: no switching or throwing mid-strike;
  - `CameraOffset`/`CameraRotation`: the camera follows the swing;
  - `HitLanded`: the camera nods on a hit.

  A future sword, gun or flashlight gets its own subclass with its own attack logic.
- **`MiningToolController`** (new): the attack controller for mining tools. It holds the tool's **own swing** (a `PickaxeSwing` with that tool's poses and timing), **Damage Per Hit** and **Swing Cooldown**.
- **`PlayerEquipment.ActiveController`**: the equipped item's controller. It is found on the view when you equip.
- **`MiningController`**: now plays the **equipped tool's** swing and uses its damage and cooldown. The raycast, range, click buffering and rock damage path are unchanged. Without a PlayerEquipment it falls back to its own Pickaxe Swing, Cooldown and Damage fields as before.
- **`CameraEffects`**: the swing camera motion and hit nod now come from whatever tool is held.

**Tools**

| | Pickaxe | Hammer |
|---|---|---|
| View | `FirstPersonViewModel` | `HammerViewModel` |
| Damage per hit | 1 (5 hits per rock) | **2** (3 hits per rock) |
| Swing cooldown | 0.6 s | 0.75 s |
| Animation speed | 1 | 0.8 (heavier) |
| Impact | normal | stops dead, recoil 4.5°, more shake |

**The Hammer** (**Ore What → Add Hammer**, `Editor/HammerSetupTool.cs`)
- **Model:** `Assets/Prefabs/HammerModel.prefab`, taken from `SimpleCavingPack.blend` ("Hammer Handle" + "Hammer Head"). Its layout matches the PickaxeModel: pivot 15 cm above the handle's end, handle +Y, head along Z. The head is a new dark steel material, `Hammer_Head`.
- **Item:** `Assets/Items/Hammer.asset`. Tool, Can Mine, not stackable, $120.
- **World prefab:** `Assets/Prefabs/Items/Hammer.prefab`. Rigidbody (2.5 kg), colliders, DroppedItem, so it can be thrown with G and picked up with E.
- **View:** `PlayerCamera/HammerViewModel` is a **copy of your pickaxe view**, with the same arms, IK and hand tuning, holding the hammer instead.
  - The hammer is placed so its head sits where the pick head was, so the tuned swing poses land it on the crosshair.
  - It has its own `PickaxeSwing` (the heavier settings above) and its own `MiningToolController`.
- **In the scene:** "Hammer (pickup)" lies 2.5 m in front of the player's start.

**Playing:** walk up to the hammer and press E; it goes into the inventory, and you keep holding the pickaxe. Press **2** to hold the hammer and **1** to go back to the pickaxe.

**Measured in Play mode**
- E on the hammer while holding the pickaxe: inventory [Pickaxe, Hammer], still holding the Pickaxe.
- 2: hammer view on, pickaxe view off, controller = HammerViewModel (2 damage).
- 3 hammer swings: rock 5 → 3 → 1 → broken. The camera followed the hammer swing (up to 1.7°).
- G early in a hammer swing: ignored. G afterwards: the hammer was thrown, hands empty, the hammer in the world.
- 1: pickaxe back; one swing did rock 5 → 4 (1 damage, unchanged).
- The thrown hammer was picked back up without replacing the held pickaxe.

**Adding another tool (Axe, Shovel...)**
1. **Model and item:** make a model prefab like HammerModel, an ItemData (Tool, Can Mine if it mines), and a world prefab.
2. **First-person view:** duplicate `HammerViewModel`, rename it, swap the mesh under `HammerRoot`, and tune its `PickaxeSwing` and `MiningToolController`. `HammerSetupTool.BuildHammerView` shows the steps in code.
3. **Link it:** add a **Player Equipment → Views** entry (Item, View, Throw From = the held mesh).

For a non-mining item (sword, gun), write a `HeldItemController` subclass with its own input and attack instead of `MiningToolController`.

**Notes**
- **Pickaxe rebuilds:** **Rebuild First-Person Viewmodel** rebuilds only the pickaxe view. Run **Add Hammer** again afterwards so the hammer view copies the new rig and your latest hand tuning.
- **Swing paths:** the hammer uses the pickaxe's swing paths (Right → Left → Overhead). Its own poses can be tuned on `HammerViewModel/Hammer/HammerRoot → Pickaxe Swing`.
- **Script name:** the swing component is still called `PickaxeSwing`. It's the generic key-pose swing and is used by both tools.

---

## 13. Carry ores physically (E) or store them (F) (update)

Ores can now be handled two ways:
- **E: carry it.** It stays a real physics object in the world. You hold it in front of you, walk it around the cave, and put it down somewhere else. The inventory is not touched.
- **F: store it.** It goes into the (one, existing) inventory and stacks, exactly as E used to.

Tools and weapons are unchanged: E puts them in the inventory, and they're equipped only if your hands are empty.

| Key | Looking at an ore | While carrying | Looking at a tool/weapon |
|---|---|---|---|
| **E** | carry it | release it | pick it up into the inventory (equip if hands empty) |
| **F** | store it in the inventory | store the carried ore | store it in the inventory |

**Prompts** (under the crosshair, using the item's display name):
- Looking at an ore: `[E] Carry Copper Ore` and `[F] Store Copper Ore`.
- Carrying: `Carrying: Copper Ore` and `[E] Release   [F] Store`.
- Inventory full: `Inventory Full`.

**How carrying works** (`OreCarryController`, on the Player)
- **Physics:** the ore stays a normal, **non-kinematic** Rigidbody. It is not parented to the camera, and nothing becomes an inventory entry.
  - Every physics step, a damped spring pulls it toward the **carry point**: 1.1 m in front of the eyes, 0.35 m below them. The formula is `acceleration = spring × offset + damping × (carry-point velocity − ore velocity)`.
  - It follows smoothly, lags a little, and heavier items lag more (Mass Influence).
  - Acceleration and speed are capped, so it can't be launched.
  - Gravity is off while carried and restored on release. Spin is damped, and the ore keeps its orientation relative to where you face.
- **Walls:** the carry point is pulled in front of any wall, rock or floor between your eyes and it, and the ore still collides with the world. It can't be pushed through things. If it snags more than **Max Carry Distance** (2 m) away, it's let go ("Dropped Copper Ore").
- **Looking down:** the point is kept at least 0.75 m in front of you, so the ore never ends up inside or behind you.
- **Release (E):** gravity and damping are restored. It keeps its current velocity (capped) plus a small **Release Force**, then falls, rolls and settles. Item and amount are preserved.
- **Collisions:** items already ignore the Player and ViewModel layers. A carried ore never pushes, blocks or lifts you, can't hit the first-person arms, and releasing it "inside" you is harmless. The mining ray ignores items, so **mining works while carrying**. PlayerItemPusher leaves the carried ore alone, and the look ray sees *through* the carried ore.
- **One at a time:** **Max Carried** (default 1). With room for more, E on another ore carries it too, stacked above the first; otherwise E releases.

**Storing (F)** uses the same inventory call as before (`PlayerInventory.AddItem`):
- **Stacking:** existing stacks are filled first, then new slots; Max Stack and Slot Count are respected.
- **Removal:** the world object is removed **only** if everything fit.
- **Partial fit:** part of the stack is stored; the rest stays in the world, still carried if it was.
- **Nothing fits:** "Inventory Full". The ore stays exactly where it was, **still in your hands** if you were carrying it. Nothing is ever destroyed.

**Scripts**

| Script | Change |
|---|---|
| `Items/OreCarryController.cs` | **new**: the carry physics |
| `Items/ItemPickupInteractor.cs` | E = carry/release (tools: pickup as before); F = store (target or carried); carried item ignored by the look ray; two-line prompts; new keys **Release Key** and **Store Key** |
| `Items/DroppedItem.cs` | `IsCarried` flag |
| `Items/ItemData.cs` | `Carryable` (= not equippable: Resources, Consumables, Artifacts...) |
| `Items/PlayerItemPusher.cs` | skips the carried item |
| `Items/InventoryHUD.cs` | multi-line prompt |
| `Editor/ItemSetupTool.cs` | adds OreCarryController (with Blocking Layers) to the Player |
| PlayerEquipment, PlayerInventory, MiningController, the swings, camera, movement | **unchanged** |

**Inspector**
- **Player → Ore Carry Controller**
  - **Carry point:** Carry Point (optional Transform; auto-created under the camera), Carry Distance 1.1, Carry Height −0.35, Min Horizontal Distance 0.75, Blocking Layers.
  - **Carry physics:** Spring Strength 70, Damping 1 (critical), Mass Influence 0.4, Max Acceleration 60, Max Speed 10, Rotation Follow 8, Max Carry Distance 2.
  - **Release:** Release Force 0.5, Max Release Speed 6.
  - **Limits:** Max Carried 1, Max Carry Mass 0 (no limit).
- **Player → Item Pickup Interactor:** Pickup Key (E, carry/pick up), Release Key (E), Store Key (F).

**Measured in Play mode**
- E on a Copper Ore: carried, gravity off, inventory unchanged.
- Carrying performance:

| Action | Lag behind the carry point | Other |
|---|---|---|
| Walking 2 s | at most 0.23 m | speed change at most 0.6 m/s per frame, no spin |
| Turning 120° in 0.8 s | at most 0.31 m | |
| Jumping | at most 0.40 m | |
| Walking into a rock | — | the ore never went inside the rock, still carried |

- E: released, gravity back, settled on the floor. The inventory was unchanged, and the item and amount were kept.
- F on an ore: Copper Ore x1. F while carrying another: Copper Ore **x2** (stacked).
- Mining while carrying: rock 5 → 4, still carrying.
- Tools:
  - E on the hammer: into the inventory; still holding the pickaxe.
  - 2 then G: only the hammer was thrown; the carried ore was untouched.
- Full inventory:
  - F while carrying: "Inventory Full", still carrying, amount kept.
  - Released, then F on it: "Inventory Full", still in the world.

**Limitations**
- **Hidden under rocks:** an ore released against a rock can roll under its rounded base. From a steep angle the rock then hides it from the look ray; step back to target it.
- **Held position:** carried ores follow a fixed point in front of you; there's no mouse-wheel distance control yet.
- **Multiplayer:** the carry is simulated locally. For multiplayer, the ore is already a normal world Rigidbody (ready to sync), but ownership and hand-off will need a networking layer.

---

## 14. 7-slot hotbar, and every item in your hands (update)

The inventory is now a **visible 7-slot hotbar** at the bottom of the screen. The **selected slot is what's in your hands**, whether it's a tool or a stack of ore. This replaces §11's "hotbar = tools only", the mouse-wheel choice for Q, and the text inventory list.

```
PlayerInventory (7 slots, SelectedSlot = the one source of truth)
      │                                   │
  HotbarUI (reads only)          PlayerEquipment (reads SelectedSlot)
  icons, counts, 1-7, highlight        │
                                       ├─ tool with a Views entry → its own view (FirstPersonViewModel, HammerViewModel)
                                       ├─ any other item (ores…)   → ItemHoldViewModel + HeldResourceView (a visual copy)
                                       └─ carrying a world ore      → ItemHoldViewModel around the REAL ore (no copy)
```

**Keys**

| Key | Does |
|---|---|
| **1–7** | select that slot (an empty slot = empty hands). Ignored mid-strike. |
| **Mouse wheel** | next/previous *occupied* slot |
| **E** | carry an ore / release it; pick up a tool (unchanged) |
| **F** | store the targeted or carried item (unchanged) |
| **G** | throw **one** of the selected item (a tool leaves its slot; a stack loses one), or throw the carried ore |
| **Q / Ctrl+Q** | drop one / the whole stack of the selected slot |

**How the hotbar works** (`HotbarUI`, on the Player)
- At startup it builds its own Screen Space Overlay canvas ("HotbarCanvas", Unity UI, scales from 1920×1080), so nothing needs setting up in the scene.
- Each slot has a background, the item's **Icon**, its **count** (`x12`, only above 1), its number **1–7**, and a gold frame when selected.
- An item with no Icon gets a placeholder: its initials on a colour picked from its Item Id. It's never an error.
- A "Holding: …" line sits above the bar:
  - `Holding: Pickaxe   [G] Throw`
  - `Holding: Copper Ore x4   [G] Throw one`
  - `Carrying: Copper Ore`
  - `Hands empty`
- It only reads the inventory and equipment, and refreshes every frame, so counts are always current.
- The E/F prompts and "Inventory Full" stay under the crosshair (`InventoryHUD`).

**Icons.** **Ore What → Add Hotbar And Held Items** renders each item's world prefab once (128 px, transparent, three-quarter view) to `Assets/Items/Icons/<item>.png` and assigns it as the item's **Icon**.
- Items that already have an icon are skipped. To use your own art, drop a Sprite into **ItemData → Icon**.
- **Ore What → Regenerate Item Icons** re-renders them all.

**How ores appear in your hands** (`HeldResourceView`, on `PlayerCamera/ItemHoldViewModel`)
- `ItemHoldViewModel` is a copy of the pickaxe view: the same arms, IK and your hand tuning, minus the pickaxe.
  - Both grip points sit on an `ItemHolder` in front of the chest.
  - The hands are **open** (Finger Curl 30, thumb 10) and pressed against the item's sides.
- **Selecting an ore** shows a **visual-only copy** of **ItemData → Held Model**. If that's empty, it uses the World Prefab.
  - The copy's DroppedItem, Rigidbodies and colliders are stripped, and it's put on the ViewModel layer with no shadows.
  - It's scaled down if it's bigger than **Max Model Size** (0.2 m) and centred.
  - The hands move out to its width.
  - Each copy is built once per item and reused. **Nothing is spawned in the world, and the count doesn't change.**
- **Carrying (E)** switches to the same view. There's **no copy**: the real Rigidbody is pulled to `ItemHolder` (OreCarryController's carry point is now `ItemHolder`), and the hands fit around it. The inventory is untouched.
  - Releasing restores physics and brings back whatever slot is selected; a held tool is never unequipped.
  - While carrying, the tool is hidden, so **mining pauses**. This replaces §13's "mining works while carrying". Carried items still never block the mining ray.

**Carry vs. selection**

| | Carried (E) | Selected from the hotbar |
|---|---|---|
| What is it | the real world Rigidbody | a visual copy of an inventory stack |
| In the inventory? | no | yes (count unchanged while held) |
| Physics | yes (spring to the hands) | none |
| G | throws that object | throws one from the stack |
| Q | nothing (release with E) | drops one / the stack |
| F | stores it | — |

**Dropping and counts**
- **Q** takes 1 (Ctrl+Q: all) from the selected slot and spawns it in front of you.
- **G** takes exactly 1 and throws it from where the held copy is on screen (tools: from the held mesh, same throw as before).
- A slot that reaches 0 becomes empty, which means empty hands.
- The spawn point is pulled out of walls, so an item never spawns inside the player or the level.

**Mining** only happens when the selected item has **Can Mine** and you aren't carrying. Rock damage, the raycast, the cooldown and ore spawning are unchanged.

**Scripts**

| Script | Change |
|---|---|
| `Items/HotbarUI.cs` | **new**: the uGUI hotbar and "Holding" line |
| `Items/HeldResourceView.cs` | **new**: the ore/any-item hand view (visual copies, hand fitting) |
| `Items/PlayerEquipment.cs` | shows the selected slot (any item), 1–7 + wheel, one active view, G throws one of anything or the carried ore, `StatusText` |
| `Items/PlayerInventory.cs` | default **Slot Count 7**; `SelectedSlot` is clamped |
| `Items/ItemData.cs` | optional **Held Model** (falls back to World Prefab) |
| `Items/OreCarryController.cs` | `CarryChanged` event, `ThrowLast` |
| `Items/ItemDropper.cs` | Q uses the selected slot; the wheel moved to PlayerEquipment |
| `Items/InventoryHUD.cs` | only the crosshair prompts/messages (the list and hotbar text moved to HotbarUI) |
| `Editor/HotbarSetupTool.cs` | **new**: 7 slots, builds `ItemHoldViewModel`, links it, points the carry at it, adds HotbarUI, renders icons |
| `Editor/MiningSetupTool.cs` | calls `HotbarSetupTool.Add` |
| MiningController, PickaxeSwing, the tool views, camera, movement | **unchanged** |

**Inspector**
- **Player → Player Inventory:** Slot Count (7).
- **Player → Player Equipment:**
  - Starting Item (Pickaxe);
  - Views (tool → its view, Throw From);
  - **Item View** (`ItemHoldViewModel`);
  - Throw settings.
- **Player → Hotbar UI:** Slot Size 76, Spacing 8, Bottom Margin 22, and the colours.
- **ItemHoldViewModel → Held Resource View:** Holder, Right/Left Grip, Grip Clearance 0.02, Grip Drop 0, Max Model Size 0.2, Min Half Width 0.06.
- **Hold pose:**
  - To move the held item, move `ItemHoldViewModel/ItemHolder`: it sits at (0.02, −0.15, 0.42) in camera space. After moving it, right-click **First Person Arms IK → Recalibrate Grips**. Or change `HotbarSetupTool.HoldPosition` and rerun the menu.
  - Finger Curl on its First Person Arms IK sets how open the hands are.
- **Per item:**
  - **ItemData → Icon** (the hotbar picture);
  - **ItemData → Held Model** (optional hand model);
  - Max Stack, Can Mine, Category.
- **Ore Carry Controller:** Carry Point = `ItemHolder`, Min Horizontal Distance 0.35, Spring Strength 160, Max Acceleration 150 (held tighter now that it's in your hands).

**Setup:** **Ore What → Add Hotbar And Held Items**. It's also called by **Add Mining Setup To Scene**. It's safe to rerun: it rebuilds only `ItemHoldViewModel` and doesn't touch the tool views or your grip tweaks there. After a **Rebuild First-Person Viewmodel**, rerun it so the item view copies the new rig.

**Measured in Play mode** (the requested 25 steps, driven by real key/mouse input; 38/38 checks passed)
1–2. Start: `[Pickaxe]` in slot 1, selected. The pickaxe view is on, and the slot shows the pickaxe icon.
3. Mining: 2 swings, rock 5 → 3.
4–6. E on a Copper Ore:
   - it was carried, and the item view came on (pickaxe view off);
   - it sat 0.00 m from `ItemHolder`, including after a 40° turn;
   - no visual copy was made;
   - the inventory was unchanged; "Carrying: Copper Ore".
7–8. E: released. The pickaxe view came back, still in slot 1. The ore fell to the floor (y 0.07) with gravity on and not kinematic.
9–11. F ×3 on ores: `Copper Ore x3` in slot 2, with its icon and "x3". The pickaxe stayed selected. F while carrying another: x4.
12–14. Pressed 2:
   - the item view shows "Copper Ore (held)", with no colliders and no Rigidbody;
   - the count is still 4; "Holding: Copper Ore x4 [G] Throw one";
   - slot 2 is highlighted;
   - clicking at the rock did nothing (3 → 3).
Wheel. Down → slot 1, up → slot 2.
15–16. Pressed 1: pickaxe back; one swing did rock 3 → 2.
17–18. Q: x4 → x3, with one more Copper Ore in the world. G: x3 → x2, one more thrown. The ore stayed in the hands.
19–21. All 7 slots full: `Pickaxe | Copper x50 | Hammer | Copper x50 | Iron x50 | Gold x50 | Crystal x50`.
   - F on a Gold Ore: "Inventory Full", still in the world.
   - Carrying one + F: "Inventory Full", still carried.
   - Released fine. Nothing was lost.
22–24. Mid-swing:
   - G was ignored (busy), and pressing 2 was ignored.
   - After the swing, G threw the pickaxe at 6.1 m/s.
   - Slot 1 emptied (icon gone), both views off, "Hands empty".
25. Console: no errors or warnings.

**Limitations**
- **One hold pose for all resources:** every non-tool item uses the same two-handed pose, sized to the item. A tall crystal and a flat ore are held the same way. A per-item pose would be a Held Model with its own grip points.
- **Hotbar is the whole inventory:** 7 slots, no backpack screen, and no drag-to-rearrange.
- **Icons are rendered once** from the world prefab. After changing a model, use **Regenerate Item Icons**.
- **Low hold position:** on very short/wide windows the held ore sits partly behind the hotbar; raise `ItemHolder` if needed.
- **Not saved:** the inventory isn't saved between sessions.

---

## 15. Pistol and Assault Rifle (holdable only) (update)

Two weapons from the **Flat Guns West** pack (`Assets/flat_guns_west/Flat Guns West/FBX/`) can be picked up, selected on the hotbar and held in first person. They can't shoot yet: there's no ammo, firing, reloading, recoil or damage.

| | Pistol | Assault Rifle |
|---|---|---|
| Source model | `Pistol_Full_West.Rig.fbx` (22 cm, rigged) | `Rifle_Assault_West.Rig.fbx` (70 cm, rigged) |
| Item | `Assets/Items/Pistol.asset` (Weapon) | `Assets/Items/AssaultRifle.asset` (Weapon) |
| Held model | `Assets/Prefabs/Weapons/PistolModel.prefab` | `Assets/Prefabs/Weapons/AssaultRifleModel.prefab` |
| World prefab | `Assets/Prefabs/Items/Pistol.prefab` | `Assets/Prefabs/Items/AssaultRifle.prefab` |
| First-person view | `PlayerCamera/PistolViewModel` | `PlayerCamera/AssaultRifleViewModel` |
| Hands | right hand on the grip; left hand lowered out of view | right hand on the grip; left hand on the magazine well |

**How it fits the existing systems** (no new runtime scripts):
- **Items:** Category **Weapon** makes them equippable. **Can Mine** is off, so clicking with a gun does nothing.
- **Pickup and hotbar:** E picks them up (auto-selected only if your hands are empty), 1–7 or the mouse wheel select them, and G throws them. Q drops them as usual.
- **Views:** each gun's view is a copy of `FirstPersonViewModel` (the same arms, IK, idle, walk and run bob) with the pickaxe mount removed. The gun mesh and both grip points sit on a `WeaponHolder`. Each view is registered in **Player Equipment → Views**, so selecting a gun shows its view.
- **Rigs:** the FBX rigs (slide, trigger, magazine, bolt, etc.) are kept intact for future animation.
- **Icons:** rendered automatically, like the other items; the pack has none.

**Test items:** the scene objects **"Pistol (pickup)"** and **"Assault Rifle (pickup)"** lie near the player's start, like the Hammer. Delete them to remove the test weapons. The inventory doesn't give weapons automatically.

**Setup:** menu **Ore What → Add Weapons**. It's safe to rerun: it rebuilds only the two views and keeps the existing items, prefabs and pickups. It's also run by **Add Mining Setup To Scene**. To change how a gun is held, edit its `holdPosition`, `holdEuler` or `supportPoint` in `WeaponSetupTool` and rerun.

**Measured in Play mode** (real key/mouse input, 18/18 passed):
- **Pickup:** E picked up both guns without replacing the held pickaxe (slots: Pickaxe | Pistol | Assault Rifle).
- **Switching:** 2 → only `PistolViewModel` active; 3 → only `AssaultRifleViewModel`; 2 ↔ 3 switched cleanly.
- **Mining:** with either gun, clicking at a rock did nothing (5 → 5). Back on the pickaxe, it mined (5 → 4).
- **Throw:** G threw the rifle (6 m/s); its slot emptied and the hands were empty. It landed on the ground.
- **Clean:** no missing references in the scene or the new assets, and the console had no errors.

**Limitations**
- **Hold pose:** the guns are held at the hip, not aimed down the sights. The pistol is one-handed because a two-handed pistol grip would overlap the hands with the current IK.
- **Pack coverage:** only the West pack is in the project; the East pack isn't imported.

---

## 16. Shooting, reloading and weapon sounds (update)

Both guns can now fire and reload. **Ammo is infinite:** there's no ammo counter, reserve or ammo item, and a reload always works.

| | Pistol | Assault Rifle |
|---|---|---|
| Fire | one shot per click | hold to fire |
| Fire cooldown | 0.15 s | 0.1 s (600 per minute) |
| Reload (R) | 1.4 s | 2.2 s |
| Range | 60 m | 120 m |
| Damage (IDamageable) | 20 | 25 |
| Rock damage | 1 (5 shots break a rock) | 1 |
| Rig parts used | Slide (per shot + rack), Magazine | Bolt (per shot), Magazine, Charging Handle |

**How it's built** (new scripts in `Assets/Scripts/Weapons/`):
- **`WeaponController`** (a `HeldItemController`) sits on each gun's view (`PistolViewModel`, `AssaultRifleViewModel`).
  - Only the selected item's view is active, so only the gun in your hands can fire or reload.
  - Switching away deactivates it, which stops firing, cancels a reload and resets the gun.
  - After you switch to a gun, the button must be released before it fires, so a held button doesn't carry over.
- **`IDamageable`:** an interface for future enemies and breakables. Shots call `TakeDamage`.
- **Hit detection:** one ray from the centre of the camera.
  - **Ignored:** the Player and ViewModel layers, and anything under the Player object.
  - **Rocks:** take **Rock Damage** through their normal `RockHealth.TakeHit`. Set Rock Damage to 0 if guns shouldn't break rocks.
  - **Physics objects:** get a small push.
  - **Anything else:** just registers the hit (the `ShotHit` event).
- **Recoil:** moves the `WeaponHolder`. The hand grips are its children, so the hands follow through the existing IK. The camera gets a small visual kick through CameraEffects; your aim isn't changed.
- **Reload:** the gun dips and rolls, the magazine drops out and back in, then the slide or charging handle is racked. Firing is blocked meanwhile, and a second R is ignored.
- **Muzzle flash:** a few glowing sprites plus a quick light at the rig's `Attach_Muzzle` bone. Material: `Assets/Materials/Weapons/MuzzleFlash.mat`.
- **Impacts:** a small debris burst using `RockChips.mat`.
- **Sounds:** the project has no audio files, so placeholder fire, reload and equip sounds are synthesised at startup. They play from the gun, just in front of the camera.
  - Real clips assigned in the Inspector replace them.
  - The reload sound plays on its own source and never overlaps; fire sounds may overlap for automatic fire.

**Inspector** (select `PlayerCamera/PistolViewModel` or `AssaultRifleViewModel` → **Weapon Controller**):
- **Firing:** Automatic, Fire Cooldown, Range, Damage, Rock Damage, Hit Force, Hit Layers.
- **Reload:** Duration, Offset, Tilt.
- **Recoil:** Kick, Rotation, Camera Kick, Recovery, Max Stack.
- **Rig parts:** slide / magazine / charging handle, and how far each moves.
- **Muzzle flash:** particles, light, light time.
- **Impact:** material, particle count.
- **Sound:** Fire / Reload / Equip clip and volume, fire pitch range.

Rerunning **Ore What → Add Weapons** rebuilds the views and resets these values to the defaults in `WeaponSetupTool`. Change them there if you want your tuning to survive a rebuild.

**Sound files to import** (optional; drag them onto the fields above):
- a pistol shot;
- a rifle shot (a short single shot, not a loop);
- a pistol reload and a rifle reload, about as long as each Reload Duration;
- optionally a weapon handling/equip sound.

**Measured in Play mode** (real key/mouse input): 30/30 checks passed, plus a separate re-check that the pickaxe still mines (the first mining check was on a rock the pistol had already broken).
- **Pistol:**
  - one shot per click; holding the button didn't fire again;
  - 3 clicks gave 3 shots, and 2 clicks within 0.1 s gave 1 shot;
  - muzzle light and particles appeared;
  - the fire sound played from the gun, 0.44 m from the camera;
  - a shot at a rock took it 5 → 4.
- **Reload:**
  - R started it and the sound played;
  - a click during the reload didn't fire, and a second R didn't restart it;
  - the magazine moved 0.12 m out and back;
  - it finished after 1.4 s and firing resumed.
- **Rifle:** held fire gave 9 shots in 1.0 s.
- **Switching:**
  - switching to the pistol while holding the button stopped the rifle, and the pistol didn't fire until the button was released;
  - switching mid-reload cancelled it cleanly, and the other gun wasn't reloading.
- **Self-hits:** a shot straight down hit the terrain, not the player; no shot ever hit the player or the view.
- **No ammo UI:** there's no ammo text anywhere; the status reads "Holding: Assault Rifle [G] Throw".
- **Existing systems:** the pickaxe mines (5 → 4), F stores ore, G throws a gun, and the console is clean.

---

## 17. Magazines (finite) with infinite spare magazines, and a visible reload (update)

This replaces §16's "infinite ammo". Each gun now has a **finite magazine**. Only the **spare magazines** are infinite: there's no reserve count, no magazine count, no ammo pickups, and no HUD counter. The magazine is tracked internally.

| | Pistol | Assault Rifle |
|---|---|---|
| Magazine Capacity | 12 | 30 |
| Firing | 1 round per click | 1 round per shot while held (0.1 s) |
| Reload Duration | 1.8 s | 2.4 s |

**Rules** (`WeaponController`, unchanged architecture):
- **Firing:** every shot uses 1 round. At 0 the gun stops at once and won't fire again, whether you hold the button or press it again. Each new press gives only an **empty click**, at most one per 0.3 s.
- **No auto-reload:** you press **R**. R is ignored while the magazine is full; **Reload When Full** allows it.
- **Refill:** the magazine refills to capacity only when the reload **finishes**. Switching away mid-reload cancels it and keeps the rounds you had.
- **Each gun keeps its own magazine:** e.g. pistol 5/12 → rifle 17/30 → back to pistol 5/12. Switching never refills.

**The reload you see** (fractions of Reload Duration, under **Reload Timing**):

| Phase | Default | What happens |
|---|---|---|
| 1. Preparation | 0 – Hand Reach (0.08) | the gun rises toward the centre and rolls so its magazine well faces you; the left hand starts reaching |
| 2. Removal | Magazine Out (0.24) – Magazine Gone (0.44) | the hand holds the magazine's bottom; the magazine slides straight out of the well, then leaves the view with the hand |
| 3. Empty | Magazine Gone – New Magazine (0.56) | the gun is shown with no magazine |
| 4. New magazine | New Magazine – Magazine In (0.76) | the hand brings a magazine up, lines it up under the well and pushes it in |
| 5. Ready | Rack (0.82), Ready (0.90) – 1 | the slide (pistol) or charging handle (rifle) is racked, the hand returns to its grip, the gun settles, and the magazine is full |

- **The magazine is the imported rig's `Magazine` bone.** Both FBX rigs weight their magazine 100% to that bone (checked: 569 and 2,087 vertices, none partially weighted), so it moves as a clean separate piece. No extra mesh was made, and the source assets weren't changed.
- **The hand:** the supporting hand's grip point (`LeftHandGrip`) is moved to the magazine, and the existing arm IK follows it.
- **Reload sounds** are three separate clips, each played when the reload reaches its moment:
  - **Magazine Out Sound:** at Magazine Out;
  - **Magazine In Sound:** at Magazine In;
  - **Rack Sound:** at Rack.

  Nothing plays before the reload visibly starts. Plus an **Empty Sound**.

**Inspector** (Weapon Controller on `PistolViewModel` / `AssaultRifleViewModel`):
- **Magazine:** Magazine Capacity, Reload When Full, Empty Click Cooldown.
- **Reload:** Reload Duration, Reload Timing (7 moments), Reload Offset/Tilt.
- **Rig parts:** Magazine Extract, Magazine Length, Magazine Stow Offset, Support Hand Grip.
- **Sound:** Empty, Magazine Out, Magazine In, and Rack sounds, plus Reload Volume.
- **Unchanged:** fire rate, damage, range, recoil and the fire sound.

**Measured in Play mode** (real key/mouse input): 40/40 checks passed. The empty-click check initially failed only because my test looked too late for an 80 ms sound; a frame-by-frame recheck passed.
- **Pistol:**
  - 12/12 at the start; 1 round per click; 12 clicks empty it;
  - clicking at 0 fires nothing and gives one click sound (rate-limited);
  - no auto-reload.
- **Pistol reload (R):**
  - the hand moved 0.25 m toward the magazine by 15%;
  - the magazine was 0.095 m out at 30% and 0.37 m away at 50% (out of view);
  - it was 0.077 m from seated at 70% and seated at 80%;
  - the reload ended full, and firing was blocked during it;
  - repeating empty → R → 12 twice worked.
- **Rifle:**
  - 30/30; 9 shots in 1 s with one round each;
  - it stopped at exactly 30 shots with the button still held;
  - releasing and pressing again didn't fire;
  - the reload moved the magazine 0.054 → 0.336 m out and back to seated, ending 30/30;
  - two more reloads each gave 30.
- **Reload sounds:** silent at 15% of the reload, playing at 30%, for both guns.
- **Switching:**
  - the pistol kept 5/12 and the rifle 17/30;
  - switching mid-reload didn't reload the other gun, and the cancelled reload didn't refill.
- **No ammo HUD, no duplicated items.**
- **Existing systems:** the pickaxe mines (5 → 4), G throws and E picks up the pickaxe, E carries ore, movement works, and the console is clean.

---

## 18. Pickup highlights (outline on weapons and tools lying in the world) (update)

Weapons and tools on the ground get a thin, slightly glowing outline when you're close enough to pick them up and can see them. This covers the Pistol, Assault Rifle, Pickaxe, Hammer, and any future Tool/Weapon item. Ores don't get one.

**When it shows** (`PickupHighlighter` on the Player, reusing `ItemPickupInteractor`):
- **Range:** within **Highlight Range** of your eyes. The default 0 means *the pickup range* (2.5 m), so the outline appears exactly when E can reach the item, measured to its nearest part.
- **On screen, in sight:** it must be on screen and not behind a rock, wall or the terrain.
- **Strength:** the item you're looking at (the pickup target, which shows the "[E] Pick up" prompt) gets the full outline; other nearby weapons get a softer one.
- **Hiding:** look away, walk away or pick it up and the outline fades out. It's hidden instantly when picked up.

**How it looks:**
- **The ring:** a warm yellow-orange ring about 2.5 px wide traces the item's actual silhouette. The gun's own materials aren't changed, and the gun itself doesn't glow.
- **Through grass:** the ring stays visible over grass, which was what hid the guns. The line-of-sight check stops it showing through solid objects.
- **Glow:** brightness 1.6 gives a slight bloom glow, so it's easy to see in dark areas.

**How it's built:**

| Piece | Where | What |
|---|---|---|
| `PickupHighlighter` | **Player** (Inspector) | all settings (below) and the when-to-show logic |
| `PickupHighlight` | each weapon/tool **world prefab** root, `Assets/Prefabs/Items/{Pistol, AssaultRifle, Pickaxe, Hammer}.prefab` | shows and fades that item's outline; one field, Outline Renderer |
| `PickupOutline` child | inside each of those prefabs | disabled `MeshRenderer` with the outline shape (`Assets/Prefabs/Items/Outlines/*_Outline.asset`) and two materials |
| Shader | `Assets/Shaders/PickupOutline.shader` | "Ore What/Pickup Outline"; the item's silhouette is masked with the stencil and the shape is pushed out by N pixels |
| Materials | `Assets/Materials/Outline/PickupOutlineMask.mat`, `PickupOutline.mat` | the mask (draws nothing) and the ring |
| Setup | **Ore What → Add Pickup Highlights** (`PickupHighlightSetupTool`) | builds all of the above for every Tool/Weapon world prefab; safe to rerun |

- **First-person guns get no outline.** They use the separate *Model* prefabs, not the world prefabs; checked: 0 outline objects under the camera.
- **Other code changes:** `ItemPickupInteractor` only gained three read-only properties (`PickupRange`, `PickupLayer`, `BlockingLayers`). Shooting, magazines, inventory, the hotbar, carrying and mining are unchanged.

**Inspector: Player → Pickup Highlighter:**
- **Highlight Range:** 0 = pickup range (2.5 m). Raise it, e.g. to 4, to spot weapons from further away.
- **Only Equippable:** on. Weapons and tools only.
- **Nearby Strength:** 0.55. Outline strength for items in range that you aren't looking at.
- **Highlight Color:** warm yellow-orange.
- **Highlight Intensity:** 1.6. Above 1 glows slightly.
- **Outline Width:** 2.5 px.
- **Fade Speed:** 8.

**Adding it to a new weapon:** give its ItemData the Tool or Weapon category and a World Prefab, then run **Ore What → Add Pickup Highlights**.

**Measured in Play mode:**
- **Pistol and rifle, each:** dropped with `ItemDropper.Drop` (what Q calls). No outline at 6 m. Approaching, it appeared when the nearest part of the gun was within 2.5 m (pistol 2.48 m, rifle 2.44 m; one step earlier, 2.56 / 2.52 m, it didn't).
- **Hiding:** stepping away hid it (5.6 m); looking away hid it; looking back while close showed it again.
- **Pickup:** `TryPickup` (what E calls) hid it instantly and removed the world gun.
- **Behind a rock:** a rifle behind a rock at 2.9 m got **no** outline.
- **First person:** no outline objects on the held guns. The console is clean.
- **Not driven by real keys:** Unity didn't have window focus during this test, so simulated keys and mouse were dropped. The pickup, drop and walk steps were therefore driven through the same methods the keys call. Firing and mining weren't re-run this time; their code is untouched and they passed in §17.
