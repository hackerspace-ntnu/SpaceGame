# The Crucible — two-player leash puzzle — design

**Date:** 2026-09-05
**Status:** approved, not yet implemented
**Depends on:** [2026-09-05-leash-rope-collision-design.md](2026-09-05-leash-rope-collision-design.md) — must land first
**Doc to write on landing:** `docs/AI/systems/CruciblePuzzle.md`

## The room in one paragraph

A pit of lava with a maze of rock rising out of it. Two players work the rim, never entering. Each
ties a leash to a power cell in a cradle at one end, threads their rope through a **rail** — a long
slot cut through the rim wall — and the cell hangs in the air between them, held up by nothing but
the two ropes' tension. They walk the rim to fly it through the maze to a socket at the far end.
If it touches the lava it is destroyed and a fresh one rolls into the cradle. Seating it in the
socket opens a vault.

Alone, the lava is a floor. The cell can be set down, so the puzzle becomes sequential — park it,
walk round, re-rig, pull again — and the room turns from a test of nerve into a test of planning.

## Why the controls work

Two taut ropes plus gravity pin a body to exactly one point in 3D. Neither player can place the
cell alone, and neither can be talked to it in words. Each player has two continuous axes while
moving:

- **Along their rail** — the rope's bend point slides, sweeping the cell sideways.
- **Away from their rail** — rope is spent outside the pit, so the free length inside shrinks and
  the cell reels in and rises. A winch made of legs, straight out of the rope-collision work.

**The item can never rise above rail height**, because the rope bends at the rail and the cell
hangs below it. So any wall built taller than the rails must be routed *through*. The entire
difficulty of the room is authored in wall heights, with no mechanic and nothing to tune. Walls
built *below* rail height become optional shortcuts for players who are good.

## Content

Precision-first, per the chosen obstacle set: the maze is a rock slab pierced by **narrow vertical
chimneys** joined by galleries. You lower into a shaft dead straight, traverse, rise out of the
next. Tall walls exist only as the connective tissue between chimneys, not as a routing puzzle of
their own. Target run length 60–90 s; three chimneys, the tightest last, immediately before the
socket, when both players are already rattled.

**Assumption to confirm:** the Crucible is an interior scene entered from the world like any other
ruin ([SceneTransitions.md](../../AI/systems/SceneTransitions.md)), not a set-piece in an existing
scene. Nothing else in the design depends on which it is.

## Key types

| Type | Role |
|---|---|
| `LeashRail` | An authored segment. `ClosestBend(from, to)`, `connections`, `captureRadius`, `junctionRadius`. Pure geometry — no physics, no networking. |
| `CruciblePit` | Owns the hazard surface and swaps it on player count. `HazardActive`, the kill trigger, the lava visuals and audio. |
| `CrucibleCarrier` | The power cell. Dynamic `Rigidbody`, `LeashAttachable`, `NetworkObject`, server-owned. |
| `CrucibleCradle` | Where a fresh cell appears. Server spawns; one live cell at a time. |
| `CrucibleSocket` | The goal trigger. Cell seated and settled → solved. |
| `CrucibleRoom` | The solved flag, the vault door, the saver. |

### `LeashRail.ClosestBend` is closed-form

The bend is the point on the rail minimising total rope, `|from − p| + |p − to|`. Parametrised
along the rail's unit axis, with `t` the projection and `h` the perpendicular distance of each
endpoint, that is the classic reflection problem and solves exactly:

```
t* = (t_from · h_to + t_to · h_from) / (h_from + h_to)     clamped to the segment
```

No iteration, so no iteration count to disagree about between machines, and no raycast. Two
machines with the same replicated player and cell positions compute the same bend to the bit.
Degenerate case `h_from + h_to == 0` (both points on the rail's own line) falls back to the clamped
midpoint.

### Rails are not a special case

A rail is a *preferred wrap point*, not a parallel system. When the generic wrap algorithm from the
rope-collision work produces a waypoint inside a rail's `captureRadius`, that waypoint is replaced
by a rail-bound one, whose position is recomputed by `ClosestBend` each step rather than frozen.
One code path; the physical story ("a lipped notch holds the rope instead of letting it slide off")
matches the code exactly.

**Transfer** — when a rail-bound bend clamps to a rail end and a connected rail's mouth is within
`junctionRadius`, it rebinds to that rail. No input, no interaction key, no pause: you slide off
one rail and onto the next by continuing to walk. Level design is a graph of connected rails, so
which routes exist is something laid out by hand.

## Multiplayer

- **The carrier is server-owned**, with a server-authoritative `NetworkTransform`. Both ropes'
  carrier-ends therefore resolve on the server, which already follows from
  `LeashEnd.ResolvedHere == Network.Owns(Body ?? Anchor)` — no new ownership rule. Player ends
  resolve on their own machines through `LeashedBody`, unchanged.
- **This is the riskiest assumption in the design.** Each player sees their own walking take effect
  one round-trip late, in a room built on precision. Prototype it before building geometry: two
  rails, one cell, no maze, two machines. If it is unplayable the fallback is client-side
  prediction of the carrier from local rim motion, reconciled against the server — a much larger
  piece of work that should be costed before, not after, the art goes in.
- **Rail binding is derived, not sent**, consistent with the leash's "nothing about rope shape is
  on the wire" model. If binding is seen to diverge in playtest, the fix is to carry the bound rail
  id in the existing leash join-snapshot, not to add a per-frame message.
- Two new messages, both server → everyone: `CrucibleSolved` (98) and `CarrierReset` (99).
  Next free ids after `ArrivalLaunched` (97).
- **The carrier prefab must be in the network prefab list.** It is spawned at runtime; an
  unregistered prefab fails on clients only, and silently.
- Hazard state is driven by connected player count, so a second player joining mid-attempt floods
  the room. That is the intended drama, not an edge case — it teaches the room in one shot.

## Persistence

- `CrucibleRoom` is the only saver: `{ solved: bool }`, on a `SaveableEntity`, addressed by
  identity and never by scene.
- **The carrier is deliberately not saved.** It is a consumable; on load, an unsolved room spawns a
  fresh one in the cradle. This sidesteps the runtime-spawned-entity trap entirely — no prefab id
  to resolve, no duplicate on reload, nothing to go missing.
- The vault door's state derives from `solved` rather than being saved separately, so the two can
  never disagree.
- Live ropes are already handled by `LeashSaveable`; a rope into the pit reloads and re-derives its
  wrap, per the rope-collision spec.

## Testing

EditMode, pure functions, no scene:

- `ClosestBend` — closed form agrees with a brute-force sample of `t` over the segment, on random
  configurations; clamping at both ends; the degenerate collinear case.
- Junction transfer fires at the end of a rail and only within `junctionRadius`.
- Free-length accounting: walking away from a rail shortens the free segment by the same amount.
- `HazardActive` follows player count.

Manual, and required before this is called done, per the project's non-negotiables:

- Two machines, host and a real client, cell flown end to end.
- Save mid-run, quit, reload: room unsolved, fresh cell in the cradle.
- Solve, quit, reload: vault still open.

## Build order

0. **Prototype the ownership/latency question.** Two rails, one cell, no maze, two machines.
1. `LeashRail` + `ClosestBend` + tests. No scene needed.
2. Rail-bound wrap points in the leash path; junction transfer.
3. Carrier, cradle, pit hazard, socket, room + saver.
4. Grey-box the maze from primitives in Unity — chimneys and wall heights are the tuning surface,
   and they will change every playtest. **No art until the layout stops moving**
   (`GDC-L1-PROD-0001`).
5. Model pass for the real room once the blockout is fun.

## Out of scope

Moving hazards (pistons, swinging arms) — they need a shared clock and were held back deliberately.
More than two players. A second Crucible. Any reward beyond opening the vault.
