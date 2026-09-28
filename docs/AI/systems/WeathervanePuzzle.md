---
system: WeathervanePuzzle
layer: world
summary: "A ring of cranked weathervanes that, all pointed up the plateau, opens a gust carrying players to the top"
paths:
  - Assets/Game/Scripts/Gameplay/Puzzles/Weathervane
  - Assets/Game/Scripts/Core/Persistence/Adapters/WeathervaneRingSaveable.cs
  - Assets/Game/Scripts/Core/Multiplayer/Messaging/NetJoin.cs
  - Assets/Game/Editor/Tests/WeathervanePositionsTests.cs
  - "Assets/Game/Art/Models/_Source~/models/props/weathervane.blend"
  - "Assets/Game/Art/Models/_Source~/models/props/wind_vent.blend"
  - "Assets/Game/Art/Models/_Source~/models/props/mesa_rock.blend"
symptoms:
  - "the wind vent stands on its edge like a wall"
  - "the gust particles spray out sideways instead of rising"
  - "turning a crank also turns the vane next to it"
  - "a client sees the gust open for a moment on joining, or can turn a crank the host has not seen"
  - "the gust drops me halfway up, or carries me past the plateau"
  - "the ring is scrambled again after loading a save"
reads_with: [InteractionSystem, Multiplayer, Persistence, PlayerCharacter, ArtPipeline]
updated: 2026-09-28
---

# Weathervane Puzzle

A ring of four weathervanes round a wind vent at the foot of a mesa. Each vane has a crank; right-click
turns it a quarter. When every arrow points up the plateau the vent opens a gust that lifts whoever
stands in it up the column, carries them across to the plateau and sets them down. **Solved is
permanent**: the cranks lock and the gust stays open, so nobody is stranded on top.

**Scope:** [Gameplay/Puzzles/Weathervane/](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane),
[WeathervaneRingSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/WeathervaneRingSaveable.cs),
[NetJoin.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetJoin.cs).
**Placed in:** `Assets/Game/Scenes/Tests/Marius test scene.unity` under `WeathervanePeaks` (vent at
(-60,0,0), mesa at (-100,0,0), landing at (-95,40.4,0)). Not in any world chunk yet.

## Model

- **The rules are one struct.** [WeathervanePositions](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WeathervanePositions.cs) packs each vane's quarter (0–3) into 2 bits of one int. **Position 0 = the vane's authored pose = pointing up the plateau**; solved ⇔ `Packed == 0`.
- **A crank turns its own vane AND the next one round the ring** (clockwise from above: N, E, S, W). That coupling is what makes it a puzzle; players see both arrows swing and can reason from it. Only the cranked vane's handle spins and clunks.
- **The coupling is not invertible on a ring of four**: only 64 of the 256 arrangements are reachable (`WeathervanePositionsTests.SomeArrangementsCannotBeSolved`). So a ring is **never set to arbitrary positions — it is scrambled by cranking from solved** (`Scramble`, 6 cranks by default, re-rolled if it lands on solved), which is always solvable. Any authored start must be produced the same way.
- **Legibility over hidden state** (`GDC-L1-LEVEL-0002`, `GDC-L1-UX-0004`): each plinth's amber lamp ring lights when its vane is right, the vanes are 10 m tall landmarks, and the gust is a visible particle column.
- **The bypass is allowed** (`GDC-L1-SYS-0003`): a jetpack or grapple can still reach the 40 m plateau. The mesa is sheer and tall so the puzzle stays the cheaper route (`GDC-L1-SYS-0007`) without being the only one.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `WeathervanePositions` | [WeathervanePositions.cs](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WeathervanePositions.cs) | Pure rules: `Turn`, `IsSolved`, `Scramble`, packing |
| `WeathervaneRing` | [WeathervaneRing.cs](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WeathervaneRing.cs) | State + wire. `IPersistentEntity`. On the root with `NetworkObject` + `NetRelay` |
| `WeathervaneVane` | [WeathervaneVane.cs](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WeathervaneVane.cs) | Presentation only: swings `head`, spins `crank`, toggles `alignedLamp` |
| `WeathervaneCrank` | [WeathervaneCrank.cs](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WeathervaneCrank.cs) | `IInteractable` + `IInteractionReadout` ("Weathervane crank", "RMB: turn") on the `CrankHandle` box |
| `WindUpdraft` | [WindUpdraft.cs](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WindUpdraft.cs) | Owner-side lift/carry/set-down of the LOCAL player. Execution order 200 |
| `WeathervaneRingSaveable` | [WeathervaneRingSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/WeathervaneRingSaveable.cs) | Key `weathervanes`, `{ positions }` |
| `NetJoin` | [NetJoin.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetJoin.cs) | Late joiner's "what state is this?", shared with `NetLatch` |

## Flows

1. **Start.** A restore that already landed wins; otherwise `Network.Decides` scrambles (instant, silent). Clients do not scramble — they ask (`VaneTurn` A = −1) once the root is spawned.
2. **Crank.** `WeathervaneCrank.Interact` → `ring.RequestTurn(i)` → `VaneTurn` to the server → re-checks `CanTurn` → `Apply` → `VaneState` (A = packed, B = cranked vane) to all.
3. **Apply (every machine).** Idempotent on the packed value. Each vane swings clockwise the short way to its quarter; the lamp lights on arrival. `updraft.SetOpen(positions.IsSolved)`.
4. **Ride (each player's own machine).** `WindUpdraft.FixedUpdate` phases: **Idle** → inside `columnRadius` below `liftHeight` → **Rising** (vy eased to `riseSpeed`, drawn to the axis; leaving 1.6× the radius drops you) → at `liftHeight` → **Carrying** (horizontal `carrySpeed` toward `landing`, altitude held) → within `landingRadius` → **Descending** (−3.5 m/s, below fall-damage speed) → grounded → Idle.

| Tunable | Default | Where |
| --- | --- | --- |
| `scrambleCranks` | 6 | ring |
| `columnRadius` / `liftHeight` | 3.2 / 45 m | updraft — `liftHeight` sits ~5 m above the plateau (40.4) |
| `riseSpeed` / `riseAcceleration` | 14 m/s / 22 m/s² | updraft |
| `carrySpeed` / `landingRadius` / `descentSpeed` | 11 m/s / 4 m / 3.5 m/s | updraft |
| `quarterTurnSeconds` | 0.7 s | vane |

## Multiplayer

- `NetMsg.VaneTurn` (117, player → server) and `VaneState` (118, server → all) on the **ring's** relay — the ask/announce shape of `LatchSet`/`LatchState`, widened to a packed ring.
- **Server decides the arrangement; every machine presents it; each owner flies its own body.** The gust is never applied by the server: the player's `NetworkTransform` is owner-authoritative. Remote riders are seen through transform sync.
- **Until a client has heard the arrangement, `IsKnown` is false**: cranks refuse and the gust stays shut. Before that the default positions are all zero, which *is* solved.
- The root needs an in-scene `NetworkObject` + `NetRelay`. Without them the ring still works on each machine alone.
- **Verified offline in play mode only** (host-of-none: scrambled, solved by six real crank presses, rode vent → plateau, 100/100 health). **Not yet verified on a real client.**

## Persistence

- `WeathervaneRingSaveable` (key `weathervanes`, auto-attached by `SaveablePolicy.EnsureWorldInteractables`) captures the packed positions; `RestorePositions` lands instantly and re-announces. The gust is derived, not saved.
- A null state (nothing known at save time) keeps this session's fresh scramble.
- **Not yet verified by a save/reload**: the test scene has no save system running, so the saver is not attached there.

## Gotchas

- **A single-mesh FBX's root carries the axis conversion** (−90° X). `wind_vent.fbx` is one mesh, so its root *is* the mesh node; placing it at an identity rotation stands the vent on its edge. Copy the asset root's rotation. `mesa_rock` and `weathervane` have a container root and do not have this problem.
- **The head's rest pose is the solved pose, and it is authored per instance**: the plinth is turned so the crank faces the vent, and the `Head` child is turned separately so the arrow points at the rock. Rotating a vane instance later changes what "solved" points at.
- **A Circle-shaped particle emitter fires in its own plane**, not along its normal. The gust uses a near-flat Cone.
- **Order 200 is load-bearing**: `PlayerMovement.FixedUpdate` assigns horizontal velocity; a carry written earlier is lerped back to walking pace in the same tick. Gravity is added back each step (`gravityStep`) or every phase sags.
- **The lamp uses `Materials/Environment/WeathervaneLamp.mat`**, not the FBX's embedded `Mat_Emissive_Amber`: FBX sub-asset materials cannot have `_EMISSION` enabled ([ArtPipeline.md](ArtPipeline.md)).
- No gust audio yet; the crank uses `SfxId.InteractLever`.

## Extending

1. **More vanes:** add vane instances in ring order and grow `vanes` (2–15). Re-check solvability with the tests' `Reachable` search before relying on a count.
2. **Place in the real world:** copy `WeathervanePeaks`' structure into a chunk scene. Keep the `NetworkObject` on the ring root, set `landing` on the plateau, and set `liftHeight` a few metres above it.
3. **A reward on the plateau** is unauthored — put the hull module or cache there.
4. **Models:** the `.blend` files under `_Source~/models/props/` are the source. Re-export with `_exportlib.export(src, unity_path("Environment", "<name>.fbx"))`. Stone colours are the `Mat_Stone_*` palette entries.
