# Backpack Inventory Usability Rework — Design

**Date:** 2026-08-24
**Status:** Approved by user (magnet snap, visual refusal, right-click both ways, drag-time lattice)

## Problem

Moving items between the hotbar and the open pack feels janky. Placement is precise but
punishing: aiming at an occupied cell turns the preview red and a release springs back with a
text notice ("No room there for X"), so the player loops refuse → read message → re-aim. The
grid is invisible except for the held item's own cells, so free space cannot be judged at a
glance. The user's directives: keep the rig, faces and shape system; make moving items on and
off simple; visualize the grid better; never show a "no room" message — the system finds room
itself.

Constitution grounding: GDC-L1-UX-0004 (make the wrong action hard rather than refused),
GDC-L1-UX-0007 (the refusal loop is incidental friction; the physical inventory itself is
meaningful friction and stays), GDC-L1-UX-0003 (every gesture still needs immediate visible
feedback, so a full pack cannot silently eat the action).

## What does not change

- The rig, faces (`PackSurface`), grid (`PackGrid`), shapes/masks (`PackShape`), overhang
  rules (`PackOverhang`), and the focus camera/session.
- The server-authoritative request model: every verb is a request, nothing moves locally
  until the server publishes a layout change. Two players can share one open pack; races are
  resolved by the server refusing (publishing nothing).
- The wire format: requests still name an item by a uv on a face. No new messages, no new
  fields. Hotbar stows still place at yaw 0 because `NetMsg.PackStow` has no yaw field.
- The save record (`PackPlacement`). No new persistent state is introduced; persistence is
  unaffected by design, not by omission.

## Change 1 — Magnet snap

`PackLayout` gains a pure-C# query:

```
bool TryFindNearest(PackSurfaceId surface, Vector2 surfaceSize, PackShape shape,
                    Vector2 cursorUv, float preferredYaw, bool allowTurns,
                    out Vector2 uv, out float yaw, string ignoreItemId = null)
```

Semantics: among all legal placements of the shape on this face, return the one whose
snapped block centre is nearest the cursor uv. The preferred yaw is searched exhaustively
first; only if it fits nowhere are the other quarter-turns tried (in nearest-first order),
and only when `allowTurns`. Rectangles skip the redundant 180° orientations exactly as
`TryFindSpot` does. `ignoreItemId` excludes the item in the air, as everywhere else.

`PackDragController.UpdateDrag` replaces its `PackLayout.Snap` + `CanPlace` pair with
`TryFindNearest` on the hovered face:

- Found → the ghost and the cell preview sit at the returned spot; `dropIsLegal = true`;
  release sends the request for that spot. If the search had to change yaw, the drag's `yaw`
  field adopts the returned one so preview and request agree.
- Not found (face genuinely has no room for this shape) → no ghost cells; the drag proxy
  follows the cursor dimmed/tinted; release slides home (existing spring-back), with no text.

The search is per-face: the cursor names a face and the item never jumps to a face the
player is not pointing at. Off every face is still the throw-away verb (pack drags) or a
cancel (hotbar drags) — untouched.

Hotbar drags run the same search pinned to yaw 0, matching what the server will place.

The race case remains: a request the server refuses (another player filled the spot in
flight) still results in nothing happening, which is current, accepted behaviour.

## Change 2 — Text notices removed; refusal is visual

All `ShowNotice` call sites in `PackDragController` are removed, along with the
notice/noticeUntil plumbing if nothing else uses it:

| Old notice | New behaviour |
| --- | --- |
| "No room there for X" (hotbar drag released on a full spot) | Cannot happen: magnet snap means a shown ghost is legal. No ghost → release is a no-op, proxy returns to the slot. |
| "No room on the pack for X" (`BeginHotbarDrag` refuses to start) | The drag still refuses to start (an item whose id is already on the pack, or a pack with no room anywhere); the hotbar slot plays a short shake instead. |
| "No room on the pack for X" (`OnStowKey` / stow keys 1-4) | Slot shake. |
| "Hotbar slot N is empty" (`OnStowKey`) | Nothing — pressing a key for an empty slot simply does nothing; there is no misconception to correct. |
| Take refusal from `CanTakeToHotbar` (swap with nowhere to put the displaced item) | Brief red flash on the hovered pack item (reusing the drag-tint material family). No text. |

The hover labels that *teach* verbs ("right-click to take", the leaf hints, "Drag a hotbar
item here, or press 1-4") are not notices and stay, updated for change 3.

`InventoryUI` gains a small `ShakeSlot(int index)` (a few-frame positional wiggle on the
slot's rect), used by the refused stow paths.

## Change 3 — Right-click both ways

- Right-click a pack item → hotbar. Exists (`SendToHotbar`); unchanged except its refusal
  becomes the red flash above.
- Right-click a hotbar slot while a focus session is active → auto-stow that slot via the
  existing unaimed path: local `CanStow(aimed: false)` predicts; refusal shakes the slot;
  otherwise `RequestStow(slotIndex, aimed: false, …)`. Wired from the HUD slot's pointer
  handler to `PackDragController.Active`, the same reach-in the hotbar drag already uses.
- The 1-4 stow keys keep working (now aimed stows fall back through magnet logic server-side
  exactly as before — `TryStowAt` already first-fits). No bindings are removed.
- Hover labels teach the symmetry: the bare-mat label becomes "Drag or right-click a hotbar
  item to stow it here"; the pack-item label keeps "(right-click to take)".

## Change 4 — Lattice while dragging

`PackGridVisual` gains a lattice pass drawn only while an item is in hand, on the hovered
face only:

- Free cells: faint outline (a dimmer variant of the existing outline geometry).
- Occupied cells: clearly marked (filled, in the placed-ochre family) so free vs taken reads
  at a glance through the gear sitting on them.
- Ghost cells: the existing clear-blue outlines at the magnet-snapped spot.
- The blocked-red pass is removed: a shown ghost is legal by construction, and the
  no-room-on-face state draws no ghost cells at all (the dimmed proxy is the readout), so
  red cells became unreachable.

Idle focus mode (nothing in hand) draws no lattice — the pack stays a pack. The lattice is
rebuilt when the hovered face or the layout changes, not per frame, and reuses
`PackGridVisual`'s existing local-frame mesh building (surface-frame geometry, shared
materials, explicit disposal).

## Component responsibilities after the change

- `PackLayout` — adds `TryFindNearest`; still owns all placement rules; still pure C#.
- `PackDragController` — drops the notice channel; drives magnet snap and the lattice; adds
  the right-click-stow entry point beside `BeginHotbarDrag`.
- `PackGridVisual` — adds the lattice pass beside the existing per-cell preview.
- `InventoryUI` — adds `ShakeSlot`; forwards right-click on a slot during focus.
- `BackpackObject`, `BackpackNetwork`, save/load — untouched.

## Testing

- EditMode tests for `TryFindNearest` beside `PackLayoutTests`: nearest-by-distance on an
  empty face, snapping around one obstacle, preferred-yaw-first with yaw fallback, masks
  (L-shape into a corner), rectangle 180° skip, full face → false, `ignoreItemId` letting an
  item nudge past itself.
- Manual verification per project rules: host **and** an actual client, both directions
  (stow by drag, stow by right-click, stow by keys, take by right-click, take by drag to
  slot, throw-away), plus the two-players-one-pack race sanity check.
- No persistence verification needed beyond a smoke reload: no state was added.

## Out of scope

- Adding a yaw field to `NetMsg.PackStow` (hotbar stows keep yaw 0).
- Cross-face magnet search.
- Any change to the rig meshes, holders, or the leaf-flip gesture.
