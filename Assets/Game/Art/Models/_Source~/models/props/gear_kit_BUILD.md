# gear_kit.blend — build record

Every piece a gear puzzle needs, from power source to the thing it drives: puzzle gears,
decoys, decorative gears, shafts and transmission parts, sources, outputs, and four gear
boxes (3 / 5 / 8 / 12 pegs, Easy → Very hard). 41 assets, catalog **Puzzles/Gears**.
Built 2026-10-07. Generated once by script; from here on the `.blend` is the source of truth.

## Conventions (read before using or extending)

- **Spin axis is local Y** (Unity forward/Z after export). Every turning part hangs under a
  `Root_*` or `*_Pivot*` empty whose local Y is its axis, unless its name says otherwise
  (wind rotor yaw: Z; bevel/worm/side shafts: rotated pivots, axis = the pivot's local Y).
- **Front is −Y.** Gears face −Y; a gear's origin is the centre of its **back face** (the seat).
- **Module 0.04 m** for every meshing gear (puzzle, decor, box gears, worm wheel, rack).
  Two gears mesh when their centres are `(teeth_a + teeth_b) × 0.02 m` apart. Every pair sum
  of the five puzzle sizes is distinct, so a peg spacing names exactly one pair of sizes.
- **Layers:** board layer 0 seat sits 0.05 m in front of the plate; layer 1 is 0.12 m further.
  Flush pegs (`Socket_NN_F`) hold layer 0; raised bosses (`Socket_NN_R`) hold layer 1.
- **Ports:** `*_Port*` empties (single arrow, pointing out) mark where a shaft connects.
  Sources/outputs: origin on the floor. Inline transmission parts: origin on the shaft axis.
- Materials are the `Mat_Deco_*` set appended from `puzzle_kit.blend` (local copies, same as
  there). No new materials were added.

## Puzzle gears (`Puzzles/Gears/Puzzle`)

| Asset | Teeth | Ø pitch | Look | Size dots |
|---|---|---|---|---|
| `Coll_Gear_Puzzle_XS` | 10 | 0.40 m | brass, solid web | 1 |
| `Coll_Gear_Puzzle_S` | 14 | 0.56 m | copper, 4 windows | 2 |
| `Coll_Gear_Puzzle_M` | 20 | 0.80 m | brass, 5 windows | 3 |
| `Coll_Gear_Puzzle_L` | 28 | 1.12 m | copper, 6 spokes | 4 |
| `Coll_Gear_Puzzle_XL` | 40 | 1.60 m | brass, 8 spokes | 5 |
| `Coll_Gear_Stack_SL` (solver `CL`) | 14 + 28 | — | S behind, L in front, red spacer | |
| `Coll_Gear_Stack_XSXL` (solver `CX`) | 10 + 40 | — | XS behind, XL in front | |
| `Coll_Gear_Decoy_Broken` | 20 | 0.80 m | rusted M, 4 teeth gone, cracked rim: slips | |
| `Coll_Gear_Decoy_Coarse` | 9 @ module 0.062 | ~0.56 m | iron, coarse teeth: never meshes | |

Stacked gears carry `Seat_Back` and `Seat_Front` (rotated 180° about Z): mount by
`Seat_Front` to put them on back-to-front. Carryable gears are brass/copper; everything fixed
to a machine is iron or rust, so players can tell what they can pick up.

## Decor, transmission, sources, outputs

- **Decor** (move, mesh with the puzzle gears): `Deco_Flywheel` (56 t, curved spokes),
  `Deco_Skeleton` (48 t, weathered), `Deco_CrownPair` (crown wheel + pinion at 90°),
  `Deco_RackPinion` (2 m rack slides on X).
- **Transmission:** `Shaft_1m/2m/4m` (red index stripe so rotation reads), `Coupling_Flange`,
  `UJoint` (30° bend), `Bearing_Pillow` / `_Hanger` / `_Wall`, `Bevel_Box` (1:1 right angle),
  `Worm_Drive` (24:1, self-locking), `Chain_Drive` (10/18 sprockets), `Clutch_Lever` (sliding
  dog collar, 0.09 m travel), `Ratchet_Pawl` (free clockwise seen from the front).
- **Sources:** `HandCrank`, `WindRotor` (yawing head + down-shaft), `CounterweightDrum`
  (rope is its own object with origin at the drum, scale it in Z), `WallDrive`.
- **Outputs:** `WinchDrum` (gate chain; chain lead is its own object), `CamLifter`
  (0.14 m lift), `IndicatorDial` (needle + amber lamp).

## Gear boxes (`Puzzles/Gears/Boxes`)

One fixed **input** gear (iron, far left) and one fixed **output** gear (rust, far right) per
box; the outer half of each runs into an iron side housing, and a shaft leaves the box
sideways to a coupling flange (`*_Port_In` faces −X, `*_Port_Out` faces +X). The puzzle area
between them is clear. Painted yellow/red rings and arrows show the input's spin and the
direction the output must turn. `Lamp_Power` (amber) and `Lamp_Solved` (green) are separate
objects for the engine to toggle. `Coll_Gear_Box_N_HintRings` is a separate opt-in asset
(rust shadows of the solution) — leave it out unless you want the hint.

The full puzzle definition (pegs, input, output, required direction, max output speed,
inventory, solution) is stored as JSON in the `puzzle` custom property on each box root.
Every layout was found by a seeded search and checked by a solver: **exactly one solution**
(a stacked gear standing in for a plain one counts as the same solution), no peg outside the
input–output span, all gears inside the window, collisions, lock-ups and direction modelled.

| Box | Look | Window | Pegs (raised) | Inventory | Solution (peg → gear) | Traps found |
|---|---|---|---|---|---|---|
| 3 · Easy | plank board on wall | 3.6 × 1.6 m | 3 (0) | S, L, XS + Broken | 0 S · 1 XS · 2 L | — (tutorial) |
| 5 · Medium | iron plate in stone, hood | 4.3 × 2.1 m | 5 (0) | L, S, M, XS, M + Coarse | 1 L · 2 M · 3 S · 4 XS | 1 wrong-way |
| 8 · Hard | slate in timber A-frame | 3.5 × 2.8 m | 8 (1) | S, M, SL, XS, L, S + Broken | 0 SL · 1 S · 5 M · 6 XS | 4 lock-up, 4 wrong-way |
| 12 · Very hard | machine wall + step | 6.1 × 3.3 m | 12 (4) | S, M, XSXL, XL, S, SL, XS, L, M + Broken, Coarse | 2 XSXL **back-to-front** · 6 M · 9 S · 10 S · 11 XL | 54 lock-up |

Box 12 also records `max_output_speed` 0.223 (the intended train turns the drum at 0.179×
input): the weight sign by the drum is the clue that the train must be geared down.
`Ref_Solved` (not an asset) shows each box with its solution placed, teeth phased to mesh.

## Exporting

`_exportlib.export_collections` ships **meshes only** — it would drop every pivot, socket and
port empty. Ship these with `export(src, dst, keep_collection="Coll_Gear_…", keep_empties=True)`
(and move by the collection's `instance_offset` in `prepare`, since the contact sheet keeps
items off-origin).

## Judgement calls

- New file next to `puzzle_kit.blend` instead of adding to it; `puzzle_kit.blend` untouched.
  Its older `Coll_Puzzle_GearWall_Board` / `Gear_Key` are not compatible with module 0.04.
- Pivots are empties, not armatures: the engine drives rotation from the gear simulation.
- No generator scripts are kept in the library (per ArtPipeline.md).
