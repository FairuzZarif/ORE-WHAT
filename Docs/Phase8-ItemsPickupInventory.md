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
