---
system: WeathervanePuzzle
layer: world
summary: "A ring of cranked weathervanes that, all pointed up the plateau, opens a gust carrying players to the top"
paths:
  - Assets/Game/Scripts/Gameplay/Puzzles/Weathervane
  - Assets/Game/Scripts/Gameplay/Puzzles/Dials
  - Assets/Game/Scripts/Core/Persistence/Adapters/DialSetSaveable.cs
  - Assets/Game/Editor/Tests/WeathervanePositionsTests.cs
  - "Assets/Game/Art/Models/_Source~/models/props/weathervane.blend"
  - "Assets/Game/Art/Models/_Source~/models/props/wind_vent.blend"
  - "Assets/Game/Art/Models/_Source~/models/props/mesa_rock.blend"
symptoms:
  - "the wind vent stands on its edge like a wall"
  - "the gust particles spray out sideways instead of rising"
  - "turning a crank also turns the vane next to it"
  - "a client sees the gust open for a moment on joining, or can turn a crank the host has not seen"
  - "the gust lifts me but I fall short of the plateau, or take fall damage landing on it"
  - "the ring is scrambled again after loading a save"
reads_with: [SunShrine, InteractionSystem, Multiplayer, Persistence, PlayerCharacter, ArtPipeline]
updated: 2026-10-28
---

# Weathervane Puzzle

A ring of four weathervanes round a wind vent at the foot of a mesa. Each vane has a crank; right-click
turns it a quarter. When every arrow points up the plateau the vent opens a gust. Stepping in throws
the player up the column; nothing carries them sideways, so they **steer themselves** off the top,
and the crown slows their fall onto the plateau. **Solved is permanent**: the cranks lock and the
gust stays open, so nobody is stranded on top.

**Scope:** [Gameplay/Puzzles/Weathervane/](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane),
[Gameplay/Puzzles/Dials/](Assets/Game/Scripts/Gameplay/Puzzles/Dials) (shared with [SunShrine.md](SunShrine.md)),
[DialSetSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/DialSetSaveable.cs).
**Placed in:** `Assets/Game/Scenes/Tests/Marius test scene.unity` under `WeathervanePeaks` (vent at
(-60,0,0), mesa at (-100,0,0); the plateau's flat top starts ~29 m from the vent, at x ≈ −89, y ≈ 38–40).

## Model

- **The rules are one struct.** [WeathervanePositions](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WeathervanePositions.cs) packs each vane's quarter (0–3) into 2 bits of one int. **Position 0 = the vane's authored pose = pointing up the plateau**; solved ⇔ `Packed == 0`.
- **A crank turns its own vane AND the next one round the ring** (clockwise from above: N, E, S, W). That coupling is what makes it a puzzle; players see both arrows swing and can reason from it. Only the cranked vane's handle spins and clunks.
- **The coupling is not invertible on a ring of four**: only 64 of the 256 arrangements are reachable (`WeathervanePositionsTests.SomeArrangementsCannotBeSolved`). So a ring is **never set to arbitrary positions — it is scrambled by cranking from solved** (`Scramble`, 6 cranks by default, re-rolled if it lands on solved), which is always solvable. Any authored start must be produced the same way.
- **Legibility over hidden state** (`GDC-L1-LEVEL-0002`, `GDC-L1-UX-0004`): each plinth's amber lamp ring lights when its vane is right, the vanes are 10 m tall landmarks, and the gust is a visible particle column whose mushrooming crown shows how far its soft-landing reach spreads.
- **The gust is a force, not a ride.** It writes only `linearVelocity.y`; horizontal motion stays PlayerMovement's air control (0.3 lerp toward walk/sprint speed per step), so the player flies themselves the whole way.
- **The bypass is allowed** (`GDC-L1-SYS-0003`): a jetpack or grapple can still reach the 40 m plateau. The mesa is sheer and tall so the puzzle stays the cheaper route (`GDC-L1-SYS-0007`) without being the only one.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `WeathervanePositions` | [WeathervanePositions.cs](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WeathervanePositions.cs) | Pure rules: `Turn`, `IsSolved`, `Scramble`, packing |
| `DialSet` | [DialSet.cs](Assets/Game/Scripts/Gameplay/Puzzles/Dials/DialSet.cs) | **Shared with the sun shrine.** Abstract: one packed arrangement, the ask/announce wire, first roll, `Restore`. `IPersistentEntity`. Subclasses supply `Turn`, `Fresh`, `Show`, `Describe`, `Progress` |
| `WeathervaneRing` | [WeathervaneRing.cs](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WeathervaneRing.cs) | The ring's `DialSet`: coupled turn, scramble, vanes + gust, locked once solved. On the root with `NetworkObject` + `NetRelay` |
| `WeathervaneVane` | [WeathervaneVane.cs](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WeathervaneVane.cs) | Presentation only: swings `head`, spins `crank`, toggles `alignedLamp` |
| `DialCrank` | [DialCrank.cs](Assets/Game/Scripts/Gameplay/Puzzles/Dials/DialCrank.cs) | `IInteractable` + `IInteractionReadout` on the `CrankHandle` box; serialized `dial` index (0–3 = N, E, S, W), `label` "Weathervane crank", `prompt` |
| `WindUpdraft` | [WindUpdraft.cs](Assets/Game/Scripts/Gameplay/Puzzles/Weathervane/WindUpdraft.cs) | Owner-side updraft on the LOCAL player: column lift plus crown sink cap. Execution order 200 |
| `DialSetSaveable` | [DialSetSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/DialSetSaveable.cs) | Key `dials`, `{ arrangement }` |

## Flows

1. **Start.** A restore that already landed wins; otherwise `Network.Decides` scrambles (instant, silent). Clients do not scramble — they ask (`DialTurn` A = −1) once the root is spawned.
2. **Crank.** `DialCrank.Interact` → `ring.RequestTurn(i)` → `DialTurn` to the server → re-checks `CanTurn` → `Apply` → `DialState` (A = packed, B = cranked vane) to all.
3. **Apply (every machine).** Idempotent on the packed value. Each vane swings clockwise the short way to its quarter; the lamp lights on arrival. `updraft.SetOpen(positions.IsSolved)`.
4. **Ride (each player's own machine).** `WindUpdraft.FixedUpdate` is stateless, one rule per tick:
   - **In the column** (within `columnRadius`, below `liftHeight`): `vy += (wind − vy) · windGrip · dt`, a drag toward the wind's speed. Gravity still acts, so the climb settles at `windSpeed − g/windGrip` ≈ 14 m/s, and stepping in kicks at `windGrip·windSpeed − g` ≈ 42 m/s². `wind` fades linearly to zero over the top `liftFade` m, so riders bob where `wind = g/windGrip` (≈ 59 m with the defaults).
   - **In the crown** (within `reachRadius` of the axis, outside the column, at any height ≥ −1 m): a fall is capped at `sinkSpeed`, under PlayerMovement's 5 m/s fall-damage threshold.
   - Beyond `reachRadius` the gust does nothing; a rider who steers that far falls normally.

| Tunable | Default | Where |
| --- | --- | --- |
| `scrambleCranks` | 6 | ring |
| `columnRadius` / `liftHeight` / `liftFade` | 3.2 / 62 / 10 m | updraft — the prefab value of `liftHeight` is 62 (the code default matches) |
| `windSpeed` / `windGrip` | 20 m/s / 3 /s | updraft |
| `reachRadius` / `sinkSpeed` | 34 m / 3.5 m/s | updraft — reach must pass the plateau's rim (~29 m) |
| `quarterTurnSeconds` | 0.7 s | vane |

**The crossing budget.** From the bob height (~59 m) to the plateau (~40 m) a rider sinking at 3.5 m/s has ~5.4 s of air, and ~26 m to cover from the column's edge to the rim. At the 6 m/s walk that is ~32 m: it lands with a few metres to spare. Lowering `liftHeight` or raising `sinkSpeed` eats that margin first.

**Gust effects** (all under `Gust`, all in `gustEffects`, all but the streaks on [`WindGustPuff.mat`](Assets/Game/Art/Materials/Environment/WindGustPuff.mat), the textureless `SpaceGame/Effects/JetSmoke` shader): `GustBillows` (the mass: ~95/s big puffs rising 14–17 m/s, orbital swirl, a radial burst at end of life that mushrooms the top), `GustCrown` (slow puffs spreading outward on a horizontal Circle emitter at 57 m), `GustStreaks` (thin stretched wind lines spiralling on the outside), `GustDust` (puffs at the vent mouth).

## Multiplayer

- `NetMsg.DialTurn` (142, player → server) and `DialState` (143, server → all) on the **ring's** relay (named `VaneTurn`/`VaneState` until the sun shrine shared them; 117/118 until the 2026-10-15 merge with main, which had taken 117–139; 140/141 until the 2026-10-21 merge, which took those for captive hauling). A client asks once spawned through `NetMessaging.NetToServerWhenSpawned`, as `NetLatch` does — the ask/announce shape of `LatchSet`/`LatchState`, widened to a packed ring.
- **Server decides the arrangement; every machine presents it; each owner flies its own body.** The gust is never applied by the server: the player's `NetworkTransform` is owner-authoritative. Remote riders are seen through transform sync.
- **Until a client has heard the arrangement, `IsKnown` is false**: cranks refuse and the gust stays shut. Before that the default positions are all zero, which *is* solved.
- The root needs an in-scene `NetworkObject` + `NetRelay`. Without them the ring still works on each machine alone.
- **Verified offline in play mode only** (host-of-none: scrambled, solved by six real crank presses; the old carried ride reached the plateau at 100/100 health). **The 2026-10-28 self-steered gust has not been flown in play mode yet**, and nothing has been verified on a real client. The authority split is unchanged: still owner-side, still only the open flag on the wire.

## Persistence

- `DialSetSaveable` (key `dials`, auto-attached by `SaveablePolicy.EnsureWorldInteractables` to any `DialSet`) captures the packed positions; `DialSet.Restore` lands instantly and re-announces. The gust is derived, not saved. The key was `weathervanes` (`WeathervaneRingSaveable`) before the extraction; it never reached `main`, so no shipped save carries it.
- A null state (nothing known at save time) keeps this session's fresh scramble.
- **Not yet verified by a save/reload**: the test scene has no save system running, so the saver is not attached there.

## Gotchas

- **A single-mesh FBX's root carries the axis conversion** (−90° X). `wind_vent.fbx` is one mesh, so its root *is* the mesh node; placing it at an identity rotation stands the vent on its edge. Copy the asset root's rotation. `mesa_rock` and `weathervane` have a container root and do not have this problem.
- **The head's rest pose is the solved pose, and it is authored per instance**: the plinth is turned so the crank faces the vent, and the `Head` child is turned separately so the arrow points at the rock. Rotating a vane instance later changes what "solved" points at.
- **A Circle-shaped particle emitter fires in its own plane**, not along its normal. The gust uses a near-flat Cone.
- **Order 200 keeps the gust the last word on `vy`**: it runs after `PlayerMovement.FixedUpdate`, which leaves `vy` alone. The gust never touches horizontal velocity. A horizontal push written here would be lerped back toward walking pace by air control the next tick.
- **No gravity compensation in the lift**: the drag model `vy += (wind − vy)·grip·dt` lets Unity's gravity act, and the equilibrium already accounts for it. Adding `g·dt` back as well (the old carry did) makes the climb settle at the full `windSpeed` and the bob sit at the very top of the fade.
- **The crown caps every fall within `reachRadius`, not only a rider's.** Anyone jumping off the plateau's edge near the vent also floats down while the gust is open. That is deliberate: it is the same wind.
- **The lamp uses `Materials/Environment/WeathervaneLamp.mat`**, not the FBX's embedded `Mat_Emissive_Amber`: FBX sub-asset materials cannot have `_EMISSION` enabled ([ArtPipeline.md](ArtPipeline.md)).
- No gust audio yet; the crank uses `SfxId.InteractLever`.
- **A crank's dial is a serialized index now, not its parent vane's position in `vanes`.** Reordering `vanes` without renumbering the four `CrankHandle` `DialCrank.dial` values makes a crank turn the wrong vane.

## Extending

1. **More vanes:** add vane instances in ring order and grow `vanes` (2–15). Re-check solvability with the tests' `Reachable` search before relying on a count.
2. **Place in the real world:** copy `WeathervanePeaks`' structure into a chunk scene. Keep the `NetworkObject` on the ring root. Set `liftHeight` so the bob sits well above the plateau, and `reachRadius` past its rim. Re-check the crossing budget above.
3. **A reward on the plateau** is unauthored — put the hull module or cache there.
4. **Models:** the `.blend` files under `_Source~/models/props/` are the source. Re-export with `_exportlib.export(src, unity_path("Environment", "<name>.fbx"))`; `weathervane` needs `triangulate=True` (its arrowhead and pennant are concave n-gons). Stone colours are the `Mat_Stone_*` palette entries. The weathervane was restyled on 2026-10-28: twisted iron pole, scroll brackets, spade finials, brass arrowhead, red swallowtail pennant, buttressed plinth. The plain original is `_backups~/weathervane_before_stylize.blend`. Keep the four object names (`Base`, `Head`, `Crank`, `AlignedLamp`), their origins and their material slot order: the prefab, the `Base` MeshCollider and the lamp material override all bind to them.
