---
system: CruciblePuzzle
layer: world
summary: "Two players leash one cell and fly it over lava through rim slots; neither can steer it alone"
paths:
  - Assets/Game/Scripts/Gameplay/Crucible
  - Assets/Game/Scripts/Items/Artifacts/Leash/LeashRail.cs
  - Assets/Game/Editor/Tests/CrucibleTests.cs
symptoms:
  - "the cell sits at a different height on the host and on the client"
  - "a rope goes onto a slot and can never come off it"
  - "the lava never appears no matter how many people are in the room"
  - "the lava is there in single player and the room cannot be finished"
  - "the vault is shut again after reloading a save"
  - "the cell can be lifted straight over the maze"
  - "the room is solved by flinging the cell past the socket"
  - "a second cell appears every time the world is loaded"
reads_with: [LeashSystem, Multiplayer, Persistence, Lasso]
updated: 2026-09-05
---

# The Crucible

A pit of lava with a maze of rock rising out of it. Two players work the rim and never enter. Each
ties a leash to a power cell in a cradle, threads their rope through a **rail** — a long slot cut
through the rim wall — and the cell hangs in the air between them, held up by nothing but the two
ropes' tension. They walk the rim to fly it to a socket at the far end. Drop it and it is destroyed.

**Scope:** [`Assets/Game/Scripts/Gameplay/Crucible/`](Assets/Game/Scripts/Gameplay/Crucible) (4 files), [LeashRail.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashRail.cs), [CrucibleTests.cs](Assets/Game/Editor/Tests/CrucibleTests.cs).
**Related:** [LeashSystem.md](LeashSystem.md) (the rope and the rail both live there) · [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md)

## Model

- **Two taut ropes plus gravity pin a body to exactly one point in 3D.** Neither player can place
  the cell alone and neither can be talked to it in words, which is the entire reason the room
  exists. It asks for something the leash could already do — `Leash.CombinedPull` has always summed
  ropes per body — and nothing else in the game had ever asked for it.
- **Each player has two continuous axes while moving.** Along their rail, the bend slides and the
  cell sweeps sideways. Away from their rail, rope is spent outside the pit so the path measures
  longer and the cell is drawn in and rises. **A winch made of legs, with no winch in the code.**
- **The cell can never rise above rail height**, because the rope bends at the rail and the cell
  hangs below it. Any wall built taller than the rails must be routed *through*. The whole
  difficulty of the room is authored in wall heights, with no mechanic and nothing to tune — and a
  wall built *below* rail height is deliberately a shortcut for players who are good.
- **The fail state is gravity.** There is no "you touched a wall" rule anywhere in this system. The
  walls are obstacles; the lava is the only thing that destroys anything.
- **Alone, the lava is a floor.** The cell can be set down, so the puzzle becomes sequential — park
  it, walk round, re-rig, pull again — and the room turns from a test of nerve into a test of
  planning. Same geometry, different game. Solo is the patient route and carries no counterweight:
  it is slower and fiddlier, and that is judged to be enough.

## Key types

| Type | File | Role |
|---|---|---|
| `LeashRail` | [LeashRail.cs](Assets/Game/Scripts/Items/Artifacts/Leash/LeashRail.cs) | The slot. `ClosestBend` (closed form), `BendFor`, `AtEnd`, `HandOverAt`, static `Capturing` |
| `CrucibleCarrier` | [CrucibleCarrier.cs](Assets/Game/Scripts/Gameplay/Crucible/CrucibleCarrier.cs) | The cell. `Settled`, `Recradle`. Server-owned |
| `CruciblePit` | [CruciblePit.cs](Assets/Game/Scripts/Gameplay/Crucible/CruciblePit.cs) | The hazard surface, the kill trigger, `HazardFor`/`HazardActive` |
| `CrucibleSocket` | [CrucibleSocket.cs](Assets/Game/Scripts/Gameplay/Crucible/CrucibleSocket.cs) | The goal trigger. Requires present **and** settled |
| `CrucibleRoom` | [CrucibleRoom.cs](Assets/Game/Scripts/Gameplay/Crucible/CrucibleRoom.cs) | `Solve`/`Solved`, the vault, and the only saver. Key `crucible` |

## Flows

1. **Tie.** Both players leash the cell in the cradle, exactly as they would leash anything.
2. **Thread.** Walking so the rope crosses the rim wall makes an ordinary wrap on it;
   `LeashPath.SlideRails` promotes that wrap to the rail cut through that wall in the same step.
3. **Fly.** Each player's rim position sets their rope's bend, and the two constraints together
   place the cell. Walking past a rail's mouth hands the rope to a connected rail with no input.
4. **Fail.** The cell enters the hazard trigger; the server calls `Recradle`.
5. **Solve.** The cell rests in the socket and is `Settled` for `settleSeconds`; the server sets
   `solved`, and the vault door derives from it.

### The bend, as `LeashRail` solves it

Along the rail's own axis each end reduces to a distance `t` and a perpendicular height `h`, which
is the flat reflection problem:

```
t* = (t_from · h_to + t_to · h_from) / (h_from + h_to)      clamped to the segment
```

Exact, one expression, no loop, no raycast. Verified against brute-force sampling over 2000 random
3D configurations with a worst excess of 0.000000000 — `ClosestBend_IsTheShortestRopeOverTheRail`.

## Multiplayer

- **The carrier is server-owned**, with a server-authoritative `NetworkTransform`. Both ropes'
  cell-ends therefore resolve on one machine, which follows from `LeashEnd.ResolvedHere` with no new
  ownership rule. Player ends resolve on their own machines through `LeashedBody`, unchanged.
- **Nothing is sent about rope shape.** The bend is a closed-form projection onto an authored
  segment from already-replicated positions, so both machines get the same answer. `LeashRail` uses
  a **registry**, not a trigger volume or a scene search, precisely so the answer does not depend on
  when a collider streamed in.
- **No new `NetMsg`.** `solved` and `hazard` are `NetworkVariable`s; a cell reset is a server-side
  rigidbody write on a server-authoritative transform. Two message ids were reserved in the design
  and deliberately not spent — neither would carry anything a `NetworkVariable` does not.
- **The cell prefab must be in the network prefab list.** It is spawned at runtime, and an
  unregistered prefab works perfectly on the host and fails silently for clients.
- Hazard follows connected client count, so a second player joining floods the room mid-attempt.
  That is intended: it teaches the room in one shot.

## Persistence

`CrucibleRoom` is the only saver — `{ solved: bool }`, on a `SaveableEntity`, addressed by identity
and never by scene. The vault's state is **derived** from `solved` rather than saved beside it: two
records of one fact are two records that can disagree.

**The cell is deliberately not saved.** It is a consumable the lava destroys several times a minute,
so an unsolved room spawns a fresh one. That sidesteps the runtime-spawned-entity problem outright —
no prefab id to resolve, nothing to duplicate on reload, nothing to go quietly missing.

## Gotchas

- **A rail's `connections` are one-way unless set both ways.** A rope gets onto that rail and can
  never come off it, which in play looks like the rope having jammed.
- **A wall shorter than rail height is a shortcut, not a bug.** The cell can be lifted over it. That
  is how difficulty is authored here; if a wall should not be clearable, build it taller than the
  rails.
- **The cell must not be in the leash's `wrapLayers`.** It is dynamic, and a dynamic collider in
  that mask desynchronises every rope in the room.
- **`CruciblePit.HazardFor` counts connected clients**, so a listen-host playing alone is one and
  gets the floor. Check this first if the lava never appears.
- **The socket requires settled, not merely present.** Without it the room is solved by flinging the
  cell through the hole, which is easier than the puzzle and no fun to have done.
- **Rail capture has no hysteresis.** See the same note in [LeashSystem.md](LeashSystem.md).

## Extending

1. A new route is a new `LeashRail` plus its `connections`. The level is a graph; which rails meet
   decides which routes exist.
2. Moving hazards (pistons, swinging arms) were held back deliberately: an impulse that shoves the
   cell has to agree on both machines, so it must be authored on a shared clock rather than
   simulated. Do not add one as a plain physics body.
3. The reward is whatever is behind the vault. Nothing in this system knows or cares what that is,
   which is the point of putting a socket between the puzzle and the prize.
