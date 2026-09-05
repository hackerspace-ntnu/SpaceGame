---
system: Placeables
layer: items
summary: "Items you put down in the world and pick back up again with Q"
paths:
  - Assets/Game/Scripts/Items/Placeables
  - Assets/Game/Scripts/Gameplay/Interaction/Core/IRetrievable.cs
symptoms:
  - "an item was placed but cannot be picked up again"
  - "placing an item did not consume it, or consumed it without placing anything"
  - "picking a placed thing up gave back a different item, or nothing"
  - "a placed object is gone after a save and reload"
  - "pressing Q over a placed object does nothing"
reads_with: [Artifacts, InteractionSystem, Backpack, Saddles]
updated: 2026-09-05
---

# Placeables

An item you put down and can take back. Two prefabs, one item asset, and a loop that conserves:
at no point should a placeable exist in the world *and* in an inventory.

**Worked example:** the camp lantern — `Lantern.prefab` (held) / `PlacedLantern.prefab`
(placed) / `Lantern.asset`, built by
[`LanternBuilder`](Assets/Game/Editor/Items/LanternBuilder.cs).

**Scope:** [`PlaceableItem`](Assets/Game/Scripts/Items/Placeables/PlaceableItem.cs),
[`PlacedObject`](Assets/Game/Scripts/Items/Placeables/PlacedObject.cs),
[`IRetrievable`](Assets/Game/Scripts/Gameplay/Interaction/Core/IRetrievable.cs).
**Related:** [Artifacts.md](Artifacts.md) (the held item), [InteractionSystem.md](InteractionSystem.md) (how Q
finds its target), [Saddles.md](Saddles.md) (the same verb on an animal).

## Model

| Piece | Is |
| --- | --- |
| `PlaceableItem` | The thing in your hand. A `ToolItem`; on use it spawns the placed prefab and spends itself. |
| `PlacedObject` | The thing on the ground. Answers **Q** and returns the item. |
| `IRetrievable` | "This can be taken back", bound to Q the way `ISecondaryInteractable` is bound to LMB. |

- **Two prefabs, not one wearing a hat.** The held half has a grip pose and a hold animation; the
  placed half has a footprint, a collider you cannot walk through, and whatever it is *for*.
- **What it returns is authored on the placed prefab**, not sent at placement time. A placeable is
  a pair, so the placed half already knows what it is — nothing about that has to survive the wire,
  a save, or a client joining halfway through. Binding it at spawn would mean replicating the asset
  id and re-applying it on every load, for a fact that never changes.
- **Q, not E, undoes a placement.** A placeable that *does* something keeps E for doing it: a
  placed lamp switches on with E and is pocketed with Q. One key for both would make the player
  guess which way the next press goes.

## Flows

**Placing.** `OnRequestUse` raycasts on the holder's machine — the server's `Camera.main` is the
*host's* camera — and refuses ground steeper than `maxGroundAngle`. `Use()` (server) spawns through
`GameServices.World.Spawn` and only then calls `Deplete()`.

**Retrieving.** Q → `Interactor.RetrieveTarget` resolves the crosshair through the ordinary
`IInteractable` path and casts to `IRetrievable`, so retrieval inherits line of sight and reach for
free. `PlacedObject.Retrieve` sends `NetMsg.RetrieveRequest` (102) to the server, which adds the
item to the asker's inventory and **then** despawns.

## Multiplayer

One id, on the placed object's own relay, gated on `Network.Owns`. Retrieval must be
server-authoritative: two players pressing Q on the same crate on the same frame must not produce
two crates. There is no reply message — the despawn is what every other machine sees.

## Persistence

A placed object is a spawned `NetworkObject`, so it needs a registered saveable prefab id and a
`SaveableEntity`, or it is gone on reload. It carries **no** state of its own worth saving beyond
its transform: what it returns is on the prefab, so `TransformSaveable` is the whole of it.

## Gotchas

- **Consume after the spawn, never before.** A missing network-prefab registration makes `Spawn`
  fail; deplete first and the item is eaten with nothing put down.
- **`maxUses = 1` is the wrong way to spend a placeable.** The counter increments in `TryUse`
  *after* `Use()` returns, so it charges for the click that missed the ground as readily as the one
  that worked, and `RefundUse` cannot undo it from inside `Use()`. Call `Deplete()` on the path
  that actually placed something.
- **`IRetrievable` alone is never found.** `Interactor.ResolveAlongRay` walks `IInteractable` only.
  Implement both; `PlacedObject` already does, with `CanInteract` false so E stays free.
- **A trigger collider only answers for an interactable on its own GameObject** and never inherits
  one from a parent — the same rule that governs every other interactable.
- **Both halves need a prefabId stamped ON DISK, not just in memory.** `SaveableEntity.OnValidate`
  fills it in the editor, so a prefab looks correctly wired while the `.prefab` file ships an empty
  string — and anything spawned from it is written into the save and can never be restored. Both
  lantern prefabs failed this when first built. `SaveWiringOnDiskTests` is the guard; run
  **Tools ▸ Save System ▸ Wire Saveable Prefabs** and then check the file, because the tool does not
  always flush the placed half.
- **The returned asset must be the item that placed it.** Nothing enforces the pairing at runtime,
  and getting it wrong transmutes the item on every place/pick cycle.

## Extending

**A new placeable** — 1) Two prefabs: the held one (the ordinary artifact recipe in
[Artifacts.md](Artifacts.md), with `PlaceableItem` instead of a bespoke script) and the placed one
(`PlacedObject` + collider + `NetworkObject` + `SaveableEntity` + `TransformSaveable`). 2) Point
`placedPrefab` at the second and its `returnItem` back at the first's item asset. 3) Register the
placed prefab in the network prefab list *and* the saveable prefab registry. 4) Verify by placing,
reloading, and picking it up on a client rather than the host.

**A placeable that does something** — subclass `PlacedObject`, override `CanInteract`/`Interact`
for the E verb. Q keeps working underneath.
