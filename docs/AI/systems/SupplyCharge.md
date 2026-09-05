---
system: SupplyCharge
layer: items
summary: "A fraction that rides one item instance through every container: hotbar, pack, gear, world, machine"
paths:
  - Assets/Game/Scripts/Items/Supplies
  - Assets/Game/Scripts/Items/Backpack/Placement/PackItemKey.cs
  - Assets/Game/Scripts/Items/Backpack/Placement/PackPlacement.cs
  - Assets/Game/Scripts/Items/Inventory/Core/IPlayerInventory.cs
  - Assets/Game/Scripts/Items/Inventory/Components/PlayerInventoryNetwork.cs
symptoms:
  - "my tank reads full in my hand but the server is draining it"
  - "the pack refuses to take a second oxygen tank"
  - "I can only ever carry one of any item on the pack"
  - "a dropped tank is full again when I pick it up"
  - "moving a tank across the pack mat emptied it"
  - "a charge is right on the host and wrong on every client"
  - "an item's charge resets when I scroll one hotbar slot and back"
  - "a rifle shows a gauge reading 0%"
  - "a charge written into a slot never reaches the owning client"
reads_with: [Inventory, Backpack, Oxygen, Persistence, Multiplayer]
updated: 2026-09-05
---

# Supply charge

How full a carried thing is — an oxygen tank, a battery — as **one fraction per item instance** that
survives an equip, a stow, a drag, a drop, a save and the wire.

**Scope:** [Items/Supplies/](Assets/Game/Scripts/Items/Supplies) · [PackItemKey.cs](Assets/Game/Scripts/Items/Backpack/Placement/PackItemKey.cs)
**Related:** [Oxygen.md](Oxygen.md) (its only consumers today) · [Inventory.md](Inventory.md) (`ItemState`, the hotbar wire) · [Backpack.md](Backpack.md) (placements) · [Persistence.md](Persistence.md)

## Model

- **The value is a FRACTION 0…1, never a quantity.** Capacity lives on the item's **prefab**
  (`DockableSupply.Capacity`, in the kind's own unit — seconds of air, watt-hours); the fraction
  lives on the instance. That is what makes a new variant free: a 15-minute tank is a prefab with a
  different capacity, and every saved fraction in every existing world still means what it meant.
  Storing the quantity would make each saved number depend on an authored value that can change — a
  silent rebalance of every save on disk.
- **A fraction fits in ONE BYTE** (`ToByte`, ~0.4% resolution — finer than the whole percent any
  readout shows). That is what made both container wire formats affordable at one byte per item.
- **`SupplyCharge.None` (−1) is not zero.** "Holds nothing" and "is empty" are different claims: a
  rifle is not an empty tank. A `None` is never written into a bag, so a bag never carries a key
  meaning "not applicable" — absent already means that, and a nullable field in a save record keeps
  "written before charges existed" distinguishable from "written empty".
- **The player always reads a whole percent** (`Describe`), everywhere: the visor gauge, the item's
  own emissive gauge, the pack mat's hover label and the machine's reticle readout. Slightly lossy —
  a 15-minute tank at 100% reads like a 30-minute one — and one number in four places beats four
  correct-but-different ones.
- **`SupplyKind` decides which receptacle accepts it.** `Oxygen` / `Power`; persisted and sent as a
  byte, so append only. `PackContainer.TryFindSocketed(kind)` asks by kind, not by face, so a second
  socket needs no change there.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `SupplyCharge` | [Supplies/SupplyCharge.cs](Assets/Game/Scripts/Items/Supplies/SupplyCharge.cs) | Static. The state key, the byte quantisation, capacity/kind lookup off a prefab, `Describe`. |
| `SupplyKind` | same | `Oxygen` / `Power`. Persisted and sent as a byte — append only. |
| `DockableSupply` | [Supplies/DockableSupply.cs](Assets/Game/Scripts/Items/Supplies/DockableSupply.cs) | The reservoir on the item: kind, capacity, starting charge, live charge, its own gauge. `IItemStateCarrier`. No use verb. |
| `PackItemKey` | [Placement/PackItemKey.cs](Assets/Game/Scripts/Items/Backpack/Placement/PackItemKey.cs) | `<assetId>` / `<assetId>#2` … — the instance handle a container keys a placement by. |
| `HotbarSlotWire` | [Components/PlayerInventoryNetwork.cs](Assets/Game/Scripts/Items/Inventory/Components/PlayerInventoryNetwork.cs) | One hotbar slot on the wire: item id **plus a charge byte**. |
| `PackPlacementWire` | [Backpack/BackpackNetwork.cs](Assets/Game/Scripts/Items/Backpack/BackpackNetwork.cs) | One placement on the wire, same extra byte. Shared with the ship's wall. |

## Where a charge lives, per container

| Container | Storage | Replicates via |
| --- | --- | --- |
| Hotbar slot | `InventorySlot.State["supply.charge"]` | `HotbarSlotWire.Charge` |
| Pack surface / socket / ship wall | `PackPlacement.Charge` | `PackPlacementWire.Charge` |
| Gear slot (Q/E/back) | `ItemState`, via `GearSaveCodec` | — (worn gear has its own list) |
| Dropped in the world | the item GameObject's own `DockableSupply` | its `NetworkObject` |
| Fitted in a machine | that machine's own `NetworkVariable` | — |

## Flows

**Into the hand.** `EquipmentController` instantiates the prefab → `RestoreItemState(slot.State)` →
`DockableSupply.SetCharge`. A bag with no charge in it means "never been through a container that
knows about charges", and reads as the item's **authored starting charge**, not as empty.

**Off the pack into the hotbar.** `PackContainer.TryTakeToHotbar` → `TryAddItem(item, out index)` →
`GiveCharge` writes the placement's charge into that slot's `ItemState` → `PublishSlotCharges()`.

**Onto the pack.** `TryStowFromHotbar` reads `slot.State` **before** the removal (removing takes the
bag with the item), mints a `PackItemKey`, and places with the charge.

**Dropped.** `IPlayerInventory.OnItemDropped(item, charge)` → `PlayerDropService.DropItem` →
`SetCharge` on the spawned world object. **Picked up.** `PickupableItem` reads the world instance's
own charge and writes it into the slot it landed in.

## Multiplayer

**`ItemState` does not replicate — it is a bag on the server's own slot.** That was harmless while
everything in it was invisible (a magazine count, a cooldown), and stopped being harmless the moment
an item grew a gauge the player reads: a client's own tank painted its authored starting charge and
stayed there while the server drained it. Hence the byte on both wire forms.

**Both are protocol changes: host and clients must share a build.**

The server owns every charge; clients only display. A client's `TryAddItem` reports index **−1**
rather than a guess — the slot is the server's to choose, and a client that wrote per-instance state
into a predicted index would write it into whatever the server later put there.

## Persistence

| What | Where |
| --- | --- |
| Hotbar / gear | `ItemState` under `supply.charge`, through `GearSaveCodec`. Free. |
| Pack / ship wall | `PackSaveCodec.PackPlacementRecord.charge`, a **nullable** float. |
| On the ground | the world object's `DockableSupply`, through its `SaveableEntity`. |

A **missing** `charge` restores the item's authored starting charge, never 0 — reading absent as
empty would drain every tank in every existing save on its first load.

## Gotchas

- **A container is keyed by `PackItemKey`, not by asset id.** `PackLayout` refuses a second placement
  under a key it already holds — deliberately, an item is one object — so while the key *was* the
  asset id **no container could hold two of anything**. Nobody noticed, because the only item worth
  two of was an oxygen tank and a full one and an empty one were two different assets; merging them
  without this would have silently taken the pack from two tanks to one. Resolve a key with
  `PackContainer.ItemFor`, never by comparing it to an `InventoryItem.ID`.
- **The first copy's key is the BARE asset id**, which is what every existing save file and every
  authored starting list already contains — so nothing had to be migrated. The `#n` suffix is a
  within-container disambiguator and nothing more: it is **not** stable across a take and a
  put-back, and nothing may read it as a copy number.
- **A charge rides the PLACEMENT, not a table beside the layout.** Every path that already moves an
  item correctly — the wire, the save codec, a drag, a swap, both halves of a hotbar transfer —
  moves a placement. A parallel table would have to be kept in step with all of them, and the first
  one anybody forgot would empty somebody's tank silently. `PackLayout.TryMove` reads the charge back
  off the entry it replaces rather than asking the caller, for the same reason.
- **`PackLayout.SetCharge` raises `OnChanged`, so it is not a per-frame drain.** A changed layout
  republishes the whole contents list to every machine. Anything draining continuously must hold its
  own float and write back only on a step the player could actually see — see `OxygenSocket`.
- **Read a slot's charge BEFORE removing the item.** `TryRemoveItem` takes the bag with it. Every
  transfer path here reads first and writes second.
- **Writing `InventorySlot.State` directly must be followed by `PublishSlotCharges()`.** A restore
  and a transfer off the pack both write the bag behind the back of the replication path that
  normally publishes a slot change.
- **Only write a charge for an item that CARRIES one.** Stamping every rifle's slot with a bag saying
  "0%" would put an `ItemState` on every slot in the game and write one into every save file — and a
  rifle would draw a gauge about a reservoir it does not have.
- **`Inventory.SetItem` clears a slot's `ItemState` when the item changes**, which is right (a slot
  that changed hands must not keep the last item's ammo) — so a charge written *before* the
  assignment is wiped by it. Item first, charge second, always.
- **A charge is not an identity, and merging identities is not free.** `OxygenTankEmpty` was deleted
  when the two tanks merged, so a world naming it loses one item with a warning; `PowerCell` →
  `Battery` went through `AssetDatabase.MoveAsset`, which **preserves the GUID** — and an
  `InventoryItem`'s `ID` *is* its GUID, so every save naming a cell keeps resolving. Rename, never
  recreate.

## Extending

**A new kind of reservoir** — append to `SupplyKind` (persisted, so never renumber), put
`DockableSupply` on the item prefab with its capacity and starting charge, and give the receptacle
that accepts it a `PackSurface.AcceptsOnly` naming the item. Nothing in the containers changes.

**A new container** — store the fraction beside the item id and quantise it with
`SupplyCharge.ToByte` on the wire. Reconstruct as `SupplyCharge.None` for an item that does not carry
one, using `SupplyCharge.Carries(item)`: the byte is 0 both for an empty tank and for a rifle, and
only the item can tell them apart.
