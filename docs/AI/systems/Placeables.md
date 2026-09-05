---
system: Placeables
layer: items
summary: "Items put into the world under a placement rule, and picked back up with Q"
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
| `PlaceableItem` | The thing in your hand, and the **loop**: aim → validate → place → spend. Nothing else. |
| `PlacementRule` | What placing *means* for this item: its **criteria** and its **logic**. On the item's prefab. |
| `PlacedObject` | A thing on the ground. Answers **Q** and returns the item. |
| `IRetrievable` | "This can be taken back", bound to Q the way `ISecondaryInteractable` is bound to LMB. |

**Rules that exist:**

| Rule | Criteria | Logic |
| --- | --- | --- |
| `GroundPlacement` | ground no steeper than `maxGroundAngle` | spawn `placedPrefab`, facing away from the placer |
| `SaddlePlacement` | the target has a free `SaddleSocket` | `socket.Fit()` — an `Instantiate` onto a bone, **not** a spawn |

- **The system owns the loop; a rule owns the meaning.** Every placeable does the same four things
  — aim on the holder's machine, ask whether it may go there, place it on the server, spend the item
  only if the world changed. What differs is the two questions inside: *may this go HERE* and *what
  does placing DO*. Those are a `PlacementRule` on the item, not branches in `PlaceableItem`, so
  nothing in the system knows what a bone or an animal is. **A saddle is a placeable** — its
  "ground" is an animal with a socket. Without the rule split it was a second copy of this loop
  under another name (`SaddleArtifact`, now gone).
- **Two prefabs, not one wearing a hat** — for rules that spawn one. The held half has a grip pose
  and a hold animation; the placed half has a footprint, a collider you cannot walk through, and
  whatever it is *for*. A rule need not spawn anything at all: `SaddlePlacement` does not.
- **What it returns is authored on the placed prefab**, not sent at placement time. A placeable is
  a pair, so the placed half already knows what it is — nothing about that has to survive the wire,
  a save, or a client joining halfway through. Binding it at spawn would mean replicating the asset
  id and re-applying it on every load, for a fact that never changes.
- **Q, not E, undoes a placement.** A placeable that *does* something keeps E for doing it: a
  placed lamp switches on with E and is pocketed with Q. One key for both would make the player
  guess which way the next press goes.

## Flows

**Placing.** `OnRequestUse` raycasts on the holder's machine — the server's `Camera.main` is the
*host's* camera — packs point, yaw and the hit object into the `NetArg`, and asks
`rule.CanPlace`. A refusal there costs no round trip and no item. `Use()` (server) rebuilds the
`PlacementAim`, asks **again** because the first answer came from a machine that decides nothing,
calls `rule.Place`, and calls `Deplete()` only on a true.

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

- **Consume on the rule's answer, never before.** `Place` returns whether the world actually
  changed — a missing network-prefab registration, an animal already saddled — and the item is spent
  only on true. Deplete first and a refused placement eats what the player was holding.
- **The aim's `Normal` is zero on the server.** `NetArg` does not carry one, so a rule that tests
  slope must do it owner-side and tolerate the missing normal on the far side, as `GroundPlacement`
  does. Re-testing against a fabricated straight-up normal passes everything and is worse than not
  testing.
- **`aim.Target` only resolves if it has a `NetworkObject`.** Terrain does not, so a ground rule
  gets null there and must work from `Point`; an animal does, so `SaddlePlacement` gets its socket.
- **`maxUses = 1` is the wrong way to spend a placeable.** The counter increments in `TryUse`
  *after* `Use()` returns, so it charges for the click that missed the ground as readily as the one
  that worked, and `RefundUse` cannot undo it from inside `Use()`. Call `Deplete()` on the path
  that actually placed something.
- **Resolve the inventory as `IPlayerInventory`, upward from the Interactor.** The networked player
  carries only `PlayerInventoryNetwork`, so naming the concrete `PlayerInventoryComponent` resolves
  to null on every real player and the retrieval silently does nothing at all. And the Interactor
  sits on a child of the player, so it is `GetComponentInParent`, never `InChildren`.
  `PickupableItem` is the pattern to copy.
- **A verb with no E has no crosshair unless you widen the hover test.** `Interactor` lit the
  crosshair from `CanInteract()` alone, so a placeable — which deliberately has no primary verb —
  showed no prompt and no highlight, and the player had no way to learn Q would work.
  `IsActionable` now also asks `IRetrievable.CanRetrieve()`.
- **`IRetrievable` alone is never found.** `Interactor.ResolveAlongRay` walks `IInteractable` only.
  Implement both; `PlacedObject` already does, with `CanInteract` false so E stays free.
- **A trigger collider only answers for an interactable on its own GameObject** and never inherits
  one from a parent — the same rule that governs every other interactable.
- **Every builder run clears the prefabId again.** `SaveAsPrefabAsset` writes a fresh asset, so
  **Wire Saveable Prefabs is part of building a placeable, not a one-off** — it has to run after
  every rebuild of either half. `SaveWiringOnDiskTests` is the guard and has caught this twice.
- **Both halves need a prefabId stamped ON DISK, not just in memory.** `SaveableEntity.OnValidate`
  fills it in the editor, so a prefab looks correctly wired while the `.prefab` file ships an empty
  string — and anything spawned from it is written into the save and can never be restored. Both
  lantern prefabs failed this when first built. `SaveWiringOnDiskTests` is the guard; run
  **Tools ▸ Save System ▸ Wire Saveable Prefabs** and then check the file, because the tool does not
  always flush the placed half.
- **The returned asset must be the item that placed it.** Nothing enforces the pairing at runtime,
  and getting it wrong transmutes the item on every place/pick cycle.

## Extending

**A new placeable that stands on the ground** — 1) Two prefabs: the held one (the ordinary artifact
recipe in [Artifacts.md](Artifacts.md), carrying `PlaceableItem` + `GroundPlacement`) and the placed
one (`PlacedObject` + collider + `NetworkObject` + `SaveableEntity` + `TransformSaveable`). 2) Point
`GroundPlacement.placedPrefab` at the second and its `returnItem` back at the first's item asset.
3) Register the placed prefab in the network prefab list *and* the saveable prefab registry.
4) Verify by placing, reloading, and picking it up on a client rather than the host.

**A placeable that attaches to something** — write a `PlacementRule` instead. `CanPlace` is the
criteria, `Place` returns whether the world changed, and no other file needs to know it exists.
`SaddlePlacement` is 60 lines and is the worked example.

**A placeable that does something** — subclass `PlacedObject`, override `CanInteract`/`Interact`
for the E verb. Q keeps working underneath.
