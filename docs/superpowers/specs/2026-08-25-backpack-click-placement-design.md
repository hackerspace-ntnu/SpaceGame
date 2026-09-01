# Backpack click placement

**Date:** 2026-08-25
**Branch:** `movement-and-perspective`
**Supersedes the interaction half of:** `2026-08-24-backpack-usability-design.md` (magnet snap),
`2026-08-23-physical-inventory-design.md` §5.1–5.2 (drag and drop, throw away)

## The problem

The backpack is operated by dragging. Press on an item, hold the button across the screen, release
over a spot. The 2026-08-24 usability pass then added a **magnet**: the ghost left the cursor and
flew to the nearest legal spot, so a shown ghost always placed.

Both halves are wrong for this game. A press-hold-release gesture is the most demanding pointer
verb there is, and the magnet means the player is no longer the one deciding where the item goes —
it decides, and the player negotiates with it. Two of the four release outcomes (spring-back,
throw-on-the-ground) also put items in states the player did not ask for.

## What replaces it

**One button. One verb. Two states: hand empty, hand full.** Nothing is held down, ever.

### Hand empty

| Click on | Result |
| --- | --- |
| An item on the pack | It lifts into your hand |
| An occupied hotbar slot | It lifts into your hand |
| Bare board | The board flips up or down (unchanged) |
| Anything else | Nothing |

You must have an empty hand to take something out. This falls out of the state machine rather than
being enforced by a check.

### Hand full

A true-size proxy of the item follows the cursor, grid-snapped to **exactly the cell under it**.
There is no search and no auto-placement. The cells the item would occupy are drawn:

- **green** when the placement is legal
- **red** when it is not — clashing with something placed, or hanging off an edge that face does
  not allow overhang on

| Click on | Result |
| --- | --- |
| A face, cells green | Placed there, at the rotation shown |
| A face, cells red | **Rotates 90°.** The item stays in hand |
| A hotbar slot | Goes in that slot, swapping with whatever was there |
| Off the mat and off the bar | Nothing. The item stays in hand |

The scroll wheel still rotates freely, so a rotation is reachable without a refused click. An item
whose authored row forbids rotation (`PackShapes.AllowsRotation`) does not turn on either input; a
click on red shakes instead, because a click that silently does nothing reads as a broken button.

Leaving focus mode — Esc, B, walking away, death — puts a held item back where it came from. The
lift was never sent to the server, so there is nothing to undo and nothing to animate.

### Where items can be

**The hotbar or the pack. There is no third place.** The verb that threw an item on the ground from
inside the pack is removed outright.

## Consequences

### No auto-placement anywhere on the player's path

Removing the magnet is not only a UI change. `BackpackObject.TryStowAt` falls back to `TryStow`
(first-fit across every reachable face) when the aimed spot is taken, and `NetMsg.PackStow`
carries a `-1` sentinel that asks the server to first-fit outright. Both are auto-placement, and
both go: the client sends a stow only when the cells are green, and a server that disagrees —
another player took the cell in flight — **refuses**, leaving the item in the hotbar, rather than
putting it somewhere the player never pointed at.

First-fit itself stays. `TryStow`, `TryFindSpot` and `TryArrange` are still what the save-v1
migration and `AdoptPlacements` use to rescue placements that reference a removed face. They are
simply no longer reachable from a player action.

### `NetMsg.PackStow` learns yaw

Today the server places hotbar items at yaw 0, which was correct while a hotbar drag was pinned to
0 and is wrong the moment an item can be rotated in hand before being put down. Encoded as
`A = slot | (quarterTurns << 8)` — the same byte-packing `PackMove` already uses for its two
surface ids, so the idiom is one that file already carries. `B` becomes the surface id with no
sentinel, since every stow is now aimed.

### Deleted

- **Every drag path.** `IBeginDragHandler` / `IDragHandler` / `IEndDragHandler` / `IDropHandler`
  come off `InventorySlotUI`; `BeginHotbarDrag`, `EndHotbarDrag`, `CancelHotbarDrag`,
  `BeginSlotDrag`, `EndSlotDrag`, `DragSlot` and the EventSystem bridge in `InventoryUI` go with
  them. The project is then free of EventSystem drag plumbing entirely.
- **`PackLayout.TryFindNearest`** and `PackNearestFitTests` — the magnet, and nothing else uses it.
- **`NetMsg.PackDrop` (77)**, `BackpackController.RequestDrop`, `OnDropRequested` and
  `BackpackObject.TryDropToWorld`. Id 77 is retired in place and never reused.
- **Right-click-to-hotbar**, the spring-back coroutine, and the `dropIsLegal`-versus-`overSurface`
  distinction, which only existed to tell a magnet failure from a throw.
- **`BackpackObject.TryStowAt`** — its whole body was the first-fit fallback.
- **`BackpackObject.CanStow`** — it predicted the server's first-fit, which no longer happens. The
  hand asks `PackLayout.CanPlace` about the exact cells it is drawing, which is the same question
  the server will answer.

`PackDragVisuals.SetDragTint` is **repurposed**, not deleted, as `SetCarryDenied`. The cells carry
the ordinary green/red verdict now, so the proxy no longer tints on every illegal hover — but the
one click that can do nothing at all (red cells on an item that may not rotate) still needs to say
so, and a brief red flash on the proxy is that.

### Renamed

`PackDragController` → **`PackHandController`**; `PackDragVisuals` → **`PackHandVisuals`**. The
existing docstring already opens "the hands of focus mode"; nothing in either file is a drag any
more. Their `BeginDrag` / `MoveDrag` / `EndDrag` methods become `BeginCarry` / `MoveCarry` /
`EndCarry`, and `InventoryUI.ClearDragFeedback` becomes `ClearPackFeedback`.

### Re-pointed

Keys **1–4** with an empty hand lift that slot's item into your hand: the click verb on a key.
They previously performed an aimed stow that depended on the magnet.

## Deliberate non-goal: hotbar reordering

`IPlayerInventory` has no move or swap entry point, and adding one pulls in its own
server-authoritative netcode through `PlayerInventoryNetwork`. So an item lifted **from** the
hotbar can be placed on the pack, or clicked back into the slot it came from. Clicking a
**different** hotbar slot shakes that slot and changes nothing. Pack → hotbar swapping is
unaffected and works exactly as it does today.

## What the player sees

- The item under the cursor rim-lights, as now.
- A lifted pack item leaves a ghost outline on the cells it came from, so the space it will free is
  visible while deciding.
- A lifted hotbar item leaves its slot drawn as reserved, as the drag did.
- The 2D icon follows the cursor whenever the cursor is over the hotbar bar; the 3D true-size proxy
  shows whenever the cursor is over a face. One rule, for either source — a pack item carried over
  the bar previously showed nothing but a proxy hidden behind the HUD.
- The lattice of the hovered face stays: free cells faint, taken cells ochre, underneath the
  green/red ghost.

## Multiplayer

Unchanged by construction, which is the point of doing it this way. Lifting is **local only** —
nothing is sent, nothing is applied, and the placed copy stays exactly where it is until the
server's layout change arrives. Commits go through the same requests as before:

| Verb | Request | Message |
| --- | --- | --- |
| Place a pack item elsewhere on the pack | `RequestMove` | `PackMove` (76) |
| Put a held item into a hotbar slot | `RequestTake` | `PackTake` (68) |
| Put a held hotbar item on the pack | `RequestStow` | `PackStow` (78), now carrying yaw |

Two players in one pack still resolve on the server, and a refusal still publishes nothing and
changes nothing — which the requester's screen is already showing, because nothing was optimistic.

**Client verification is required:** lift, rotate, place and hotbar-swap must each be performed on
a joined client, not only on the host.

## Persistence

No new state. The hand is a UI state owned by the focus session and dies with it. `PackPlacement`,
the save codec and the wire struct are untouched; a placement made by a click is the same record a
placement made by a drag was.

## Tests

Small and targeted, in `Assets/Game/Tests/Editor`:

1. `PackStow`'s yaw packing round-trips, and a slot index survives the shifted encoding.
2. `TryStowFromHotbar` places at the exact spot given and **refuses** when that spot is taken,
   rather than first-fitting elsewhere.
3. Rotating on refusal cycles through four turns and returns to the start; an item that forbids
   rotation never turns.

`PackNearestFitTests` is deleted with the magnet. `BackpackStowTests` and `BackpackNetworkingTests`
are updated for the new `RequestStow` / `TryStowFromHotbar` signatures.
