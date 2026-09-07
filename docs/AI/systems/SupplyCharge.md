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
  - "a sprayer's tank is full again on the client while the host watches it empty"
  - "an artifact's tank refills itself when I stow it on the pack"
  - "a gadget with a tank saves its fill but a client never sees it"
  - "I cannot put a reservoir on an artifact, one of the two UsableItems wins at random"
  - "the pack refuses to take a second oxygen tank"
  - "I can only ever carry one of any item on the pack"
  - "a dropped tank is full again when I pick it up"
  - "moving a tank across the pack mat emptied it"
  - "a charge is right on the host and wrong on every client"
  - "an item's charge resets when I scroll one hotbar slot and back"
  - "a rifle shows a gauge reading 0%"
  - "a charge written into a slot never reaches the owning client"
reads_with: [SupplyGauge, Inventory, Backpack, Oxygen, Persistence, Multiplayer]
updated: 2026-09-07
---

# Supply charge

How full a carried thing is — an oxygen tank, a battery, a sprayer's own cartridge — as **one
fraction per item instance** that survives an equip, a stow, a drag, a drop, a save and the wire.

**Scope:** [Items/Supplies/](Assets/Game/Scripts/Items/Supplies) · [PackItemKey.cs](Assets/Game/Scripts/Items/Backpack/Placement/PackItemKey.cs)
**Related:** [SupplyGauge.md](SupplyGauge.md) (how it is DRAWN) · [Oxygen.md](Oxygen.md) (bottle and battery) · [Artifacts.md](Artifacts.md) (items that drain their own tank) · [Inventory.md](Inventory.md) (`ItemState`, the hotbar wire) · [Backpack.md](Backpack.md) (placements) · [Persistence.md](Persistence.md)

## Model

- **The value is a FRACTION 0…1, never a quantity.** Capacity lives on the item's **prefab**
  (`SupplyReservoir.Capacity`, in the kind's own unit — seconds of air, watt-hours, seconds of
  continuous use for a reagent); the fraction lives on the instance. So a variant is free, and
  storing a quantity instead would tie every saved number to an authored value that can change — a
  silent rebalance of every save on disk. It also fits in ONE BYTE (`ToByte`, ~0.4% — finer than the
  whole percent any readout shows), which is what made both wire formats affordable.
- **`SupplyCharge.None` (−1) is not zero.** "Holds nothing" and "is empty" are different claims: a
  rifle is not an empty tank. `None` is never written into a bag (absent already means that), and a
  nullable save field keeps "written before charges existed" apart from "written empty".
- **The player always reads a whole percent** (`Describe`) — visor, pack mat, machine readout. Lossy,
  and one number in four places beats four correct-but-different ones. **On the object it is a BAR,
  not a hue** — [SupplyGauge.md](SupplyGauge.md) owns that, and skips a `None` rather than painting
  it as zero.
- **`SupplyKind` decides which receptacle accepts it.** `Oxygen` / `Power` / `Reagent`; persisted and
  sent as a byte, so append only. `TryFindSocketed(kind)` asks by kind, not by face, so a second
  socket costs nothing. `Reagent` is the kind nothing in the world accepts: a sprayer's tank,
  refilled by the item itself.
- **A reservoir is STATE, not a verb.** `SupplyReservoir` is a plain component any item may hold;
  `DockableSupply` is the verb-less `UsableItem` a bottle also carries, for its hold pose. One class
  until 2026-09-07 — so a tank could not sit beside a tool's own `UsableItem`, every equip path
  resolving the held item with one `GetComponent<UsableItem>()` (`GDC-L1-ARCH-0002`).
- **The drain policy is the reservoir's too**, one policy for every tank: `drainPerSecond`,
  `refillPerSecond`, `refillDelay`, `restartFraction`, stepped by `Tick(deltaTime, drawing)`. All
  zero on a bottle and a battery, which nothing empties by carrying. `restartFraction` is not optional
  on anything that *is*: without it a tank that just ran dry refills by a frame's worth, allows a
  frame of use and strobes while the button is held — the frustrating end of the scarcity dial rather
  than the tense one (`GDC-L1-ECON-0002`).

## Key types

| Type | File | Role |
| --- | --- | --- |
| `SupplyCharge` | [Supplies/SupplyCharge.cs](Assets/Game/Scripts/Items/Supplies/SupplyCharge.cs) | Static. The state key, the byte quantisation, capacity/kind lookup off a prefab, `Describe`. |
| `SupplyKind` | same | `Oxygen` / `Power` / `Reagent`. Persisted and sent as a byte — append only. |
| `SupplyReservoir` | [Supplies/SupplyReservoir.cs](Assets/Game/Scripts/Items/Supplies/SupplyReservoir.cs) | **The tank.** Kind, capacity, starting charge, live charge, the drain/refill policy, its gauge, the `ItemState` round trip. `IItemStateCarrier`. `On(GameObject)` is the one lookup rule. |
| `DockableSupply` | [Supplies/DockableSupply.cs](Assets/Game/Scripts/Items/Supplies/DockableSupply.cs) | The verb-less `UsableItem` a bottle or battery carries beside its reservoir, for the hold pose. `[RequireComponent(typeof(SupplyReservoir))]`. Holds no state. |
| `SupplyGauge` | [Supplies/SupplyGauge.cs](Assets/Game/Scripts/Items/Supplies/SupplyGauge.cs) | The fill bar that draws a charge. [SupplyGauge.md](SupplyGauge.md). |
| `PackItemKey` | [Placement/PackItemKey.cs](Assets/Game/Scripts/Items/Backpack/Placement/PackItemKey.cs) | `<assetId>` / `<assetId>#2` … — the instance handle a container keys a placement by. |
| `HotbarSlotWire` | [Components/PlayerInventoryNetwork.cs](Assets/Game/Scripts/Items/Inventory/Components/PlayerInventoryNetwork.cs) | One hotbar slot on the wire: item id **plus a charge byte**. |
| `PackPlacementWire` | [Backpack/BackpackNetwork.cs](Assets/Game/Scripts/Items/Backpack/BackpackNetwork.cs) | One placement on the wire, same extra byte. Shared with the ship's wall. |

## Where a charge lives, per container

| Container | Storage | Replicates via | Saved as |
| --- | --- | --- | --- |
| Hotbar slot | `InventorySlot.State["supply.charge"]` | `HotbarSlotWire.Charge` | the bag, free |
| Pack surface / socket / ship wall | `PackPlacement.Charge` | `PackPlacementWire.Charge` | `PackSaveCodec.PackPlacementRecord.charge`, a **nullable** float |
| Gear slot (Q/E/back) | `ItemState` | — (worn gear has its own list) | `GearSaveCodec` |
| Dropped in the world | the item's own `SupplyReservoir`, painted from the carried bag at the drop | its `NetworkObject` | its `SaveableEntity` |
| Fitted in a machine | that machine's own `NetworkVariable` | — | that machine's own saver |

## Flows

**Into the hand.** `EquipmentController` instantiates the prefab → `RestoreItemState(slot.State)` on
the item's one `UsableItem` → forwarded to the sibling `SupplyReservoir` → `SetCharge`. An empty bag
means "never been through a container that knows about charges" and reads as the item's **authored
starting charge**, not as empty.

**Drained.** A held artifact calls `Tick(deltaTime, drawing)` from its own `Update` on **every**
machine: the drain is a pure function of the hold stream, so nothing travels per tick. `CanStart` is
asked once, on the press, never during a draw.

**Off the pack into the hotbar.** `TryTakeToHotbar` → `TryAddItem(item, out index)` → `GiveCharge`
writes the placement's charge into that slot's `ItemState` → `PublishSlotCharges()`. **Onto the
pack.** `TryStowFromHotbar` reads `slot.State` **before** the removal (removing takes the bag with
the item), mints a `PackItemKey`, and places with the charge.
**Dropped.** `OnItemDropped` → `PlayerDropService` → `SupplyCharge.Read(state)` → `SetCharge` on the
spawned object: the charge is **a key in the slot's bag, not a parameter beside it**. **Picked up.**
`PickupableItem` restores that bag, then writes the object's live charge over it.

## Multiplayer

**`ItemState` does not replicate — it is a bag on the server's own slot.** Harmless while everything
in it was invisible (a magazine count, a cooldown); not harmless once an item grew a gauge the player
reads, because a client's own tank painted its authored starting charge and stayed there while the
server drained it. Hence the byte on both wire forms — **both are protocol changes: host and clients
must share a build.** The server owns every charge; clients only display, and a client's `TryAddItem`
reports index **-1** rather than a guess, because state written into a predicted index would land in
whatever the server later put there.

## Persistence

Where each container writes it is the table above. A **missing** `charge` restores the item's authored
starting charge, never 0 — reading absent as empty would drain every tank in every existing save on
its first load.

## Gotchas

- **There is exactly ONE state key, `supply.charge`.** The first two tank artifacts wrote theirs under
  `tank.charge`, off a component that was not a reservoir — and every gate downstream (`ApplyWire`,
  `TryStowFromHotbar`, `PackSaveCodec`) asks `Carries`, which resolves one. So the number was captured
  and saved and then dropped by the wire and by the pack, silently: right on the host, wrong on every
  client, gone after a stow. For the same reason `Carries` and the held item's bag must resolve the
  reservoir the SAME way, which is why both go through `SupplyReservoir.On` (a
  `GetComponentInChildren`, inactive included).
- **A reservoir's `ItemState` methods are called by `UsableItem`, not through the interface.** Nothing
  resolves `IItemStateCarrier` directly; every caller does `GetComponent<UsableItem>() is
  IItemStateCarrier`. So the base forwards to its sibling reservoir, once, for every item there is.
- **A container is keyed by `PackItemKey`, not by asset id.** `PackLayout` refuses a second placement
  under a key it already holds, so while the key *was* the asset id **no container could hold two of
  anything**. Resolve one with `PackContainer.ItemFor`, never by comparing it to an `InventoryItem.ID`.
  The first copy's key is the BARE asset id, which every existing save already contains; `#n` is a
  within-container disambiguator, **not** stable across a take and a put-back, and never a count.
- **A charge rides the PLACEMENT, not a table beside the layout.** Every path that already moves an
  item correctly moves a placement, and the first parallel table anybody forgot would empty a tank
  silently. `PackLayout.TryMove` reads the charge off the entry it replaces for the same reason.
- **`PackLayout.SetCharge` raises `OnChanged`, so it is not a per-frame drain** — a changed layout
  republishes the whole contents list to every machine. Anything draining continuously holds its own
  float and writes back only on a step the player could see (`OxygenSocket`).
- **Bag hygiene, in order.** Read a slot's charge **before** removing the item (`TryRemoveItem` takes
  the bag with it); assign the item **before** writing the charge (`Inventory.SetItem` clears the bag
  when the item changes); and follow any direct `InventorySlot.State` write with
  `PublishSlotCharges()`, which is the only thing that pushes it to the owning client.
- **The drop path carries the whole bag; a charge is not special there.** Not a second channel: it is
  the one key a dropped object also *draws*, so the ground's reading is the later word.
- **Only write a charge for an item that CARRIES one.** Stamping every rifle's slot with "0%" would
  put an `ItemState` on every slot in the game and in every save file — and a rifle would draw a
  gauge about a reservoir it does not have.
- **A charge is not an identity, and merging identities is not free.** Deleting `OxygenTankEmpty`
  cost every world naming it that item, with a warning; `PowerCell` → `Battery` went through
  `AssetDatabase.MoveAsset`, which **preserves the GUID** — and an `InventoryItem`'s `ID` *is* its
  GUID, so every save naming a cell still resolves. Rename, never recreate.

## Extending

**An artifact with a tank** — a `SupplyReservoir` on the prefab root beside the artifact's own
`UsableItem`, its rates authored, and `Tick(Time.deltaTime, drawing)` from `Update` on every machine.
Gate a *new* draw on `CanStart` and nothing else; the fill is then captured, saved, replicated,
stowed and drawn because the component is there. An item that refills must also silence
`OnMaxUsesReached()`, or the inventory removes it the first time it empties.

**A new dockable supply** — append to `SupplyKind` (never renumber), put a `SupplyReservoir` **and** a
`DockableSupply` on the prefab, and name the item in the receptacle's `PackSurface.AcceptsOnly`. Its
gauge costs one row in `OxygenGearBuilder.Roster`.

**A new container** — store the fraction beside the item id, quantised by `SupplyCharge.ToByte`, and
reconstruct it as `SupplyCharge.None` when `SupplyCharge.Carries(item)` is false: the byte is 0 both
for an empty tank and for a rifle, and only the item can tell them apart.
