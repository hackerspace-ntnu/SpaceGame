# Dock Cradle — build record

The hardware that says "put the thing HERE". Three receptacles, one per way a
consumable attaches to a machine.

| Collection | What it accepts | Needed / ahead |
|---|---|---|
| `Coll_DockCradle_Collar` | a bottle, plugged in base-first, standing out at 90° from the wall | the oxygen generator's tank dock |
| `Coll_DockCradle_Shoe` | a slab power cell, lying flat on its back | the oxygen generator's cell dock |
| `Coll_DockCradle_Clamp` | a bottle standing upright against a bulkhead | built ahead |

## Why this is a component and not part of the generator

A receptacle is the signifier for the one verb a machine has, and it is the same
signifier on every machine that has that verb — the oxygen generator, a charging
rack in the lander, a refuelling post at an outpost. Building it into the first
machine that needed it is how the second machine ends up with a slightly
different cradle that means the same thing.

## Two docks on one machine must differ in shape

The collar is a circle and the shoe is a rectangle, **before either is painted**.
The colour coding — orange on the collar, green on the shoe, each matching what
goes in it — is confirmation on top of a shape difference, never the message
itself (`GDC-L1-UX-0003`: never encode critical information in colour alone).
And a slab cell physically will not enter a round collar, which is the stronger
half of `GDC-L1-UX-0004`: make the right action obvious *and the wrong one hard*.

## The mating numbers are imported, never retyped

`components/props/power_cell.py` owns `PORT`, `SLAB_W`, `SLAB_H`;
`components/props/oxygen_tank.py` owns `OXY_SKIRT_R`, `OXY_CAP_R`. This file
imports them and derives `BORE_R`, `COLLAR_R` and the socket size from them. Two
copies of one measurement is exactly how a cell ends up floating 4 mm off its
contacts with nothing in either file looking wrong.

`BORE_R` is cut for the tank's **skirt**, not its barrel — the skirt is the
widest thing that has to pass through, 8 mm fatter.

## Kept deliberately plain

Each variation is a handful of primitives under a 6 mm bevel and nothing else.
These are read at a glance from across a room while a player is deciding where to
walk, so the silhouette has to survive being small; detail added here costs
triangles and reads as grey noise at the only distance that matters.

Materials: `Mat_Neutral_Panel_Grey`, `Mat_Neutral_Slate_Dark`,
`Mat_Paint_Safety_Orange`, `Mat_Paint_Cell_Green`, `Mat_Metal_Steel_Dark`,
`Mat_Metal_Steel_Worn`, `Mat_Metal_Chrome_Scuffed`, `Mat_Plastic_Rubber_Black`,
`Mat_Neutral_Black_Matte`. Nothing added for this component.

## The saddle that was replaced

The first version of the tank dock was `Coll_DockCradle_Saddle`: a V-cradle with
two ribs, a rubber liner, an over-centre band and a filling yoke that closed on
the bottle's collar. It lay the bottle **along** the wall. It was cut on
request, and the reason is visible in any render of it — two thirds of the bottle
disappeared behind the cradle's own hardware, and the object a player is meant to
reach for was the least legible thing in the bay. It is not kept as a
built-ahead variation, because nothing about it is worth reaching for again.

Its one durable lesson is recorded here: **a coaxial fill coupling cannot meet
this bottle end-on**, because the cap is the widest part of it. That is why the
generator's filler arm comes down from above.

## Gotchas this build produced

- **The shoe was over-detailed at first** — a sunk bay, a coloured lip, side
  rails, a ledge, a catch, a status lamp and two-step surrounds. At the size a
  player sees it that read as a smear of grey rather than as a slot: the shape
  was doing none of the work and the parts were doing all of the noise. Four
  boxes and one socket now.
- **`_zverify.py` over-reports here.** The three variations are stacked at the
  origin by library convention, so every pair it flags is between variations that
  never ship together. Filter by the variation token in the object name; no
  same-variation pair remains.

## Shipping

Nothing ships this file directly. `models/props/oxygen_generator.py` appends the
Collar and the Shoe and renames them to `Mesh_OxyGen_TankDock_*` /
`Mesh_OxyGen_CellDock_*`; the Clamp is unused so far.
