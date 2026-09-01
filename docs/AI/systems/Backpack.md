---
system: Backpack
layer: items
summary: "Physical inventory: a deployable rig whose seven gridded faces hold real items, rummaged in focus mode"
paths:
  - Assets/Game/Scripts/Items/Backpack
  - Assets/Game/Editor/Backpack
  - Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab
  - Assets/Game/ScriptableObjects/Items/PackShapes.asset
  - "Assets/Game/Art/Models/_Source~/components/props/expedition_rig.blend"
symptoms:
  - "the ghost cells stay red and the item refuses to drop anywhere on the pack"
  - "an item measures metres across and fills the whole pack"
  - "gear placed on the pack is missing after a save and reload"
  - "other players do not see the items lying on my pack"
  - "a mesh built under a pack surface lands tens of metres away"
  - "everything not on the rack disappears when the pack is reshouldered or restored"
  - "the same item cannot be put in the pack twice"
reads_with: [Inventory, Multiplayer, Persistence]
updated: 2026-09-01
---

# Backpack

The physical inventory: a deployable expedition rig whose seven flat faces are cell grids you lay real items onto, rummaged in from a dedicated focus camera.

**Scope:** [`Assets/Game/Scripts/Items/Backpack/`](Assets/Game/Scripts/Items/Backpack) (+ [`Focus/`](Assets/Game/Scripts/Items/Backpack/Focus), [`Placement/`](Assets/Game/Scripts/Items/Backpack/Placement), [`Holders/`](Assets/Game/Scripts/Items/Backpack/Holders)), [`Assets/Game/Editor/Backpack/`](Assets/Game/Editor/Backpack), [`ExpeditionRig.prefab`](Assets/Game/Prefabs/Items/Equipment/ExpeditionRig.prefab), [`expedition_rig.fbx`](Assets/Game/Art/Models/Props/expedition_rig.fbx) / [`.blend`](Assets/Game/Art/Models/_Source~/components/props/expedition_rig.blend).
**Related:** [Inventory.md](Inventory.md) (item defs, hotbar — not repeated here), [Multiplayer.md](Multiplayer.md), [Persistence.md](Persistence.md).

## Model

- The pack is a **plain `Instantiate` per player** parented to the spine bone — not a spawned `NetworkObject`. Contents replicate as *state*, not as objects. Deploy unparents it; reshoulder reparents.
- Contents live on the **pack**, not the player: a pack set down keeps its gear.
- An item is `(itemId, PackSurfaceId, uv in metres, yaw)`. No slots, no indices — an index means nothing because a placement carries its own face and position.
- **One global 0.09 m cell** ([`PackGrid.Cell`](Assets/Game/Scripts/Items/Backpack/Placement/PackGrid.cs)), read off the rig's own webbing pitch. All 7 faces are exact multiples of it (Leaf 8x8, Rack 9x9, LongGoods 18x1, Back 3x6 each, Wings 4x7 each = 255 cells, zero hem). Capacity is *cells occupied*, not a count and no longer a raw-area or diagonal test.
- Each item fills a **mask** ([`PackShape`](Assets/Game/Scripts/Items/Backpack/Placement/PackShape.cs)) — authored in [`PackShapes.asset`](Assets/Game/ScriptableObjects/Items/PackShapes.asset), else derived by ceiling its measured footprint to whole cells. Masks let two L-shapes interlock.
- Layout logic ([`PackLayout`](Assets/Game/Scripts/Items/Backpack/Placement/PackLayout.cs), `PackGrid`, `PackShape`) touches no `UnityEngine` beyond `Vector2`, so EditMode tests drive it as plain C#.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `BackpackController` | [BackpackController.cs](Assets/Game/Scripts/Items/Backpack/BackpackController.cs) | On the player. Owns the pack instance, the 4 states (`Shouldered/Deploying/Open/Stowing`), the deploy arc, and every wire request handler. |
| `BackpackObject` | [BackpackObject.cs](Assets/Game/Scripts/Items/Backpack/BackpackObject.cs) | The rig. Unfold choreography, `Reaches`, item↔hotbar transfers, and rebuilding all display copies. `IInteractable`. |
| `PackLayout` | [Placement/PackLayout.cs](Assets/Game/Scripts/Items/Backpack/Placement/PackLayout.cs) | What is where. `CanPlace`/`TryPlace`/`TryMove`/`TryFindSpot`. Raises one coarse `OnChanged`. |
| `PackGrid` / `PackShape` / `PackShapes` | [Placement/](Assets/Game/Scripts/Items/Backpack/Placement) | Cell arithmetic (idempotent `Snap`), footprint masks + rotation, and the single item→shape resolver. |
| `PackSurface` / `PackSurfaceId` | [Placement/PackSurface.cs](Assets/Game/Scripts/Items/Backpack/Placement/PackSurface.cs) | One `SURF_` empty: id + size in metres, uv↔world, gizmo grid. Ids are persisted bytes — never renumber. |
| `PackOverhang` | [Placement/PackOverhang.cs](Assets/Game/Scripts/Items/Backpack/Placement/PackOverhang.cs) | The only rule letting an item exceed its face: Rack overhangs on u, back panels on both, everything else strict. Rectangles only. |
| `ItemFootprint` | [Placement/ItemFootprint.cs](Assets/Game/Scripts/Items/Backpack/Placement/ItemFootprint.cs) | Measures a prefab once, cached by `GameObject`. Size = [`ItemGrip.PackSize`](Assets/Game/Scripts/Items/Equipped/ItemGrip.cs); footprint = `(size.x, size.z)`; also classifies `HolderKind`. |
| `PackFocusSession` / `PackFocusCamera` | [Focus/](Assets/Game/Scripts/Items/Backpack/Focus) | B enters focus mode: own camera 2.46 m past the rig, 1.5 m up, 38° down, FOV 40. Never touches `timeScale`. |
| `PackHandController` | [Focus/PackHandController.cs](Assets/Game/Scripts/Items/Backpack/Focus/PackHandController.cs) | The hand state machine: hover, lift, turn, put down, leaf drag. Hotbar is treated as a fifth surface. |
| `PackGridVisual` / `PackHandVisuals` / `PackStrapVisual` | [Placement/](Assets/Game/Scripts/Items/Backpack/Placement), [Focus/](Assets/Game/Scripts/Items/Backpack/Focus) | Ghost cells + hovered-face lattice, the carried copy, and the lashing bands. |
| `BackpackNetwork` | [BackpackNetwork.cs](Assets/Game/Scripts/Items/Backpack/BackpackNetwork.cs) | `NetworkList<PackPlacementWire>` of contents + owner-written `racked` bool. |
| `BackpackSaveable` / `BackpackSaveCodec` | [Adapters/BackpackSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/BackpackSaveable.cs) | Save key `backpack`, on the player. |
| `ExpeditionRigWiring` | [Editor/Backpack/ExpeditionRigWiring.cs](Assets/Game/Editor/Backpack/ExpeditionRigWiring.cs) | Rebuilds rig + 5 holder prefabs from FBX; `SurfaceTable` must equal `PackGrid`'s rows and the `.py`'s `SURFACES`. |

## Flows

**Open.** 1) B → `PackFocusSession` + `BackpackController.Toggle` both fire. 2) Toggle only *asks* (`NetMsg.PackState`, request verb). 3) Server re-checks state, runs **its own** ground probe, `Commit` + broadcast. 4) Every machine plays the toss arc, then the unfold. 5) The focus camera flies in 0.15 s later without waiting for the unfold.

**Pick up.** 1) `PackPointer` ray on the `PackItem` layer names the display copy. 2) Left click → `Lift`: purely local, nothing sent — the layout still holds the item; `SetInHand` just stops the pack drawing it. 3) `originGrab` is `AnchorUv` (a cell the item *fills*), not the stored block-centre uv.

**Place.** 1) Every frame `PackLayout.Snap` grid-snaps the cursor uv for the current shape+yaw; `CanPlace` judges *exactly* those cells. 2) `PackGridVisual` paints the footprint green/red plus the whole face's free/taken lattice. 3) Click on green → `LetGo`, then `RequestMove` (from pack) or `RequestStow` (from hotbar) — **nothing moves locally**. 4) Server resolves the grab point positionally, applies, and the `NetworkList` rewrite moves everyone's display.

**Swap.** A full hotbar is not a refusal. `TryTakeToHotbar` puts the pack item in the selected slot and the displaced one back on the pack (same face, then first-fit on that face). A drag onto a *named* slot goes through `BackpackController.TakeIntoSlot`, whose fallback is `TryStow` across every reachable face; `CanTakeToHotbar` predicts both branches so the preview cannot approve a drop the server refuses.

**Flip the leaf.** The one held gesture in focus mode: grab bare board, drag through the arc, release past halfway to commit (`PackLeafDrag`, `BackpackObject.ScrubRack`/`SettleRack`). R is the same thing as a toggle. It only fires where the item hit found nothing.

**Refuse.** There is no message and no cursor change: red cells *are* the refusal. A click on red turns the item a quarter — the refusal and its usual fix are the same click. A symmetric shape with no turn to offer gets a timed flash instead. A click off the faces turns silently. World pickups that overflow use first-fit (`TryStow` → `TryArrange`) across *reachable* faces only.

## Multiplayer

| Concern | Authority |
| --- | --- |
| Contents (`PackLayout`) | **Server**. Clients never write; `BackpackNetwork` republishes the whole list on `OnChanged` and clients `AdoptPlacements`. |
| Deploy state + drop pose | **Server**, via `NetMsg.PackState` (A = state or `-1` ask, B = request/announce verb). Ground probe is the server's — clients never stream their own chunk. |
| Rack (leaf up) | **Owner-written `NetworkVariable`**. Not contested; focus mode already requires ownership. |
| Take / move / stow | Client asks on the **pack owner's** channel (`PackTake` 68, `PackMove` 76, `PackStow` 78); all handlers are idempotent and positional. |
| Lift into hand | Purely local — no request, no undo needed. |

Other players see the rig, its unfold, every placed item's display copy, holder and straps. An item in someone's hand is hidden on **their** machine only (`SetInHand`); remotes still see it lying on the mat until the server accepts the move.

## Persistence

- `BackpackSaveable` sits on the player (`[RequireComponent(BackpackController)]`), key `backpack`; the format is `BackpackSaveCodec`, testable without a prefab.
- v2 record = `itemId, surface (byte), u, v, yaw` + `deployed`/`packPosition`/`packRotation`. Footprints are **never** stored — recomputed from the prefab, so resizing an item does not stale a save.
- Restore places each item back exactly, else falls back to `TryArrange` first-fit; only a total failure logs and drops it. A payload with no contents keys at all leaves the pack alone (old saves must not be emptied).
- v1 (`strapItemIds`/`mainItemIds` compartments) migrates through first-fit. Free-placement uvs and 24°/45° yaws from before the grid land on the nearest cell/quarter-turn — no second file version.
- Redeploy is deferred to `OnLoadComplete` and goes **straight to the settled pose**, never through an arc.

## Gotchas

- **The magnet is gone.** `CanPlace` answers about the exact cells drawn, never a nearby spot. Do not reintroduce "find the nearest legal spot" — comments and [BackpackStowTests](Assets/Game/Tests/Editor/BackpackStowTests.cs) exist to keep it out. The ghost cache is keyed on `(surface, cell origin, legal, rect dims)`, and masked shapes bypass the early-out entirely because `Rotated` allocates a new pattern per call.
- **Class-level comments still say "the limit is surface area."** That is legacy prose from the pre-grid design; the real limit is cell occupancy.
- **`Reaches` gates player paths only.** `TryPlace`/`AdoptPlacements`/`StowAuthored` deliberately ignore it, or a restore onto a worn pack (every pack is worn in `Awake`) would lose everything not on the Rack. `Rack` is the *only* face reachable while stowed; the leaf, lash line and both wings ride `PIVOT_Leaf` and vanish when racked.
- **The layout is keyed by item ID.** The same asset can never be in the pack twice — lifts from the hotbar are refused up front rather than dying at the drop.
- **`PackSurface.ToLocal` divides out `lossyScale`.** The FBX is on the centimetre convention; skip the divide and a 0.4 m uv lands 40 m away. Any mesh built under a surface must use this exact frame.
- **Holder FBX axes are baked, not rotated.** `pack_holders.fbx` imports with `bakeAxisConversion: true` and `ExpeditionRigWiring.NormaliseRoot` folds any residual root rotation into children. `HolderBuilder` stretches a holder non-uniformly and counter-scales `HARD_` children componentwise — that inverse is only valid when axes agree, so a rotated `HARD_` empty comes out sheared.
- **An item with no `ItemGrip` measures its raw mesh bounds** (one shipped prefab came out 11 m and filled the screen). `ItemFootprint` warns once per prefab past 2 m. `PackSize` falls back to `HoldSize`, whose default 0.30 m must stay equal to `EquipItemSocket.DefaultHoldSize`.
- **The footprint cache never expires by itself** — [`ItemFootprintCacheInvalidator`](Assets/Game/Scripts/Items/Backpack/Placement/Editor/ItemFootprintCacheInvalidator.cs) clears it on asset import. `PackShapeLibrary` re-indexes on `OnValidate`.
- **An item keeps its own up.** `FootprintOf` is *defined* as `(size.x, size.z)`; a prefab that lies down wrong is authored data — fix it with [`ItemPackOrientation`](Assets/Game/Editor/Backpack/ItemPackOrientation.cs), not at seating time.
- **`PackSurfaceId` values are persisted and on the wire.** Append only.
- **Rebuilding the rig prefab wipes hand edits.** `ExpeditionRigWiring.Build` reads the starting-item lists forward and re-verifies the saved prefab, because Unity discards prefab saves silently when the AssetDatabase is read-only.
- Rotation in the hand ignores `allowRotation` on purpose; that row only constrains authored shapes and first-fit.
- **`PackLayout.OnChanged` is coarse** — no index, because a placement can change face. Every change tears down and rebuilds *all* display copies, holders, cell rings and straps; `AdoptPlacements` sets `rebuilding` to suppress the storm and rebuilds once at the end.
- **A stow is never re-aimed.** `TryStowFromHotbar` has no first-fit fallback: the player only ever sends cells they watched turn green.
- Coverage lives in [Tests/Editor](Assets/Game/Tests/Editor) (`PackLayoutTests`, `PackShapeTests`, `PackSurfaceTests`, `PackSizeTests`, `PackHandTests`, `PackLeafDragTests`, `Backpack{Stow,Swap,Fold,SaveCodec,DeployArc}Tests`) and [Editor/Tests/BackpackNetworkingTests.cs](Assets/Game/Editor/Tests/BackpackNetworkingTests.cs).

## Extending

**Make a new item placeable** — 1) Give the prefab an `ItemGrip` with a sane `holdSize` (or `packSize`); without one the mesh bounds are believed. 2) Nothing else is required: the derived block from `PackShape.ForFootprint` works immediately. 3) Only if it is not a rectangle, run `Tools/SpaceGame/Items/Create Pack Shape Library`, then draw the mask in the `PackShapes.asset` inspector grid ([`PackShapeLibraryEditor`](Assets/Game/Scripts/Items/Backpack/Placement/Editor/PackShapeLibraryEditor.cs)). 4) Watch for the `PackShapes:` warning that says the mask is smaller than the item. 5) Verify it lays out on a client, and reload a save with it on the mat.

**Add a holder surface** — 1) Add the `SURF_` empty in [`expedition_rig.py`](Assets/Game/Art/Models/_Source~/components/props/expedition_rig.py) and re-export; make its rectangle an exact multiple of 0.09 m. 2) Append a `PackSurfaceId` value (never renumber). 3) Add the row to `ExpeditionRigWiring.SurfaceTable` and mirror it in `PackGrid`'s doc table. 4) Decide `PackOverhang.Axes` for it — default is strict. 5) Add a `Reaches` case if it rides a hinge (`PIVOT_Leaf` children are hidden when racked) and an `IsExteriorWhenStowed` case only if the fold genuinely exposes it. 6) Re-run `ExpeditionRigWiring.Build` and read its `VERIFIED` line. 7) Confirm gear on it survives a save/load and appears on a joining client.
