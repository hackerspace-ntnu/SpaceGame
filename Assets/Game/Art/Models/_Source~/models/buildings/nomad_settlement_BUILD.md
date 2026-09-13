# Nomad settlement — building generator

A rule-based system for making nomad buildings out of the existing kit, and twenty buildings
produced with it.

| File | What it is |
| --- | --- |
| `nomad_settlement.py` | The system: the round family and the shared rules. |
| `nomad_rect.py` | The boxy half: block plans, terraces, detail discovery. |
| `nomad_settlement.blend` | Its output: **40 buildings** in three families, 74 units, 8137 objects, 399 meshes. |
| `../../components/nomad_settlement/components_clean.blend` | The kit it draws parts from. |
| `../../components/nomad_settlement/KIT_MAP.md` | What every kit part is and how the parts mate. |

```bash
cd Assets/Game/Art/Models/_Source~
blender --background --python models/buildings/nomad_settlement.py -- \
    --out models/buildings/nomad_settlement.blend
```

`_buildlib.start` refuses to overwrite an existing `.blend`, which is the guard that stops a
regeneration from destroying hand edits. Delete the output deliberately to rebuild it, and only
while it still holds nothing but generated work.

## Three families

| Family | Count | What it is |
| --- | --- | --- |
| round | 20 | The original drum towers. Same rules, same seed, same buildings. |
| rect | 12 | The kit's 2.2 m block module on a 1.219 m storey — seven plans composed of square, narrow and long units, with bands, foundations and rare recessed terraces. Mostly one and two storeys: a settlement is huts with a few towers in it. |
| hybrid | 8 | A low block plan with a round tower rising out of it. The shape the two kits were plainly made to make together. |

## What the system actually is

Random, but every roll is bounded, and the bounds are what make twenty buildings read as one town
instead of twenty unrelated props. Silhouette variety is what a player navigates by
(`GDC-L1-LEVEL-0002` — vary silhouettes so places aren't confusable, give districts a shared
character), so the rules split into two jobs: hold the shared language fixed, and force the
silhouettes apart.

### The rules that hold the language

| # | Rule | Why |
| --- | --- | --- |
| R1 | Every coned drum uses the kit's one wall angle, a 6.4° half-angle: `top Ø = bottom Ø − 0.2243 × h` | One angle across a whole town is the single strongest cue that the buildings belong together |
| R2 | A drum never tapers below 0.70 m across | No spikes |
| R1>R2 | If the taper would breach R2, the drum goes **straight** instead of finishing on a shallower angle | One wall at 2.8° in a town of 6.4° walls is exactly what the eye picks out. Caught by the validator |
| R3 | Every joint is an overlap: the upper drum sinks 40 mm into the lower one and seats 8 mm inside it | No coplanar faces, so no z-fighting |
| R5 | An opening's frame is pushed out until a quarter of its depth is buried in the wall | Embedded, never flush |
| R6 | An opening is leaned to the slope of the wall it sits on | A vertical window on a coned wall gaps along one edge |
| R12 | **The ground storey is always a straight block, always in pale `Clay_Bone`, and always carries the door** | It reads as the stone footing the kit's own tower had under its adobe, and a plain block is what a door belongs on |

### The rules that force the silhouettes apart

| # | Rule |
| --- | --- |
| R7 | Openings snap to a 12-sector grid. The door owns its sector and blocks the two either side. Odd storeys are offset half a sector so windows never column up |
| R8 | No opening within 180 mm of a storey seam, so a frame cannot clip a ring |
| R9 | **A ring marks a floor seam, and one ring is the default.** 64 % of seams get a single thin ring, 16 % a pair, 12 % a tall band and 8 % a heavy one, never two heavy in a row. Banding every seam three deep turned the town into a stack of tyres and stopped reading as storeys at all |
| R15 | **A ring never crosses an opening.** Rings are placed *after* the windows and the door and tested against their measured spans; a blocked ring is dropped, or demoted to a 90° arc on a stretch of blank wall. Part-way up a storey the detail piece is *always* an arc — a full ring there reads as a floor line that is not there |
| R16 | **Nothing stands in front of a door.** The doorway is barred to electrical boxes, pilasters, pipe runs and pipe brackets, at every height, out to 65° either side |
| R10 | Archetype, storey count, crown type and window family are **dealt from shuffled decks**, not rolled per building. The deck guarantees 3 `tall`, 3 `large` and 14 `normal`, five of each crown, and a fusion count of 0–3 across the set. Then no two neighbours in the layout may share both storey count and crown |
| R11 | One `SEED` constant reproduces the whole settlement exactly (`GDC-L1-CONTENT-0005`) |
| R13 | **A fused building's annexes bite 35 % of the smaller radius into the main body**, so they merge into one silhouette instead of kissing tangentially. Annex angles stay clear of the main door and of each other, and the main body gives up the sectors an annex is buried in |
| — | Naming is mechanical: `Coll_NomadBuilding_NN`, `Empty_BNN_Root`, `BNN_<unit>_<part>` where unit is `M` or `A1..A3` (`GDC-L1-CONTENT-0003`) |

### Rules the rectangular half adds

| # | Rule |
| --- | --- |
| X1 | Blocks scale **uniformly** — they carry a modelled bevel and a batter, and a per-axis scale makes the bevel oval. Slabs, bands and foundations may scale per axis, and do |
| X2 | A plan is cells on the half-module grid, and every upper storey is a subset of the one below, so no block floats |
| X3 | **Wall details in quantity.** The kit's loose detail parts are grouped into panels by *proximity*, not by collection name — the kit's own collections mix four kinds of thing in one, so the grouping has to come from where the parts sit. Seven panels found, 0.45 m to 0.91 m wide. They go on both families, several per face, never across a door and never overlapping each other |
| X4 | An **arcade is generated**, not appended: piers plus a swept segmental arch ring in the wall's own material. The kit has no arch part. `rise = span/2` gives a full semicircle, less gives the flatter desert arch |
| X5 | A **terrace is rare** (18 %), is **recessed into the wall** rather than bolted to it, and never appears without a door opening onto it |
| X7 | **Faces, roofs and trim belong to a block, not to the plan.** They used to be sized to the storey's outer *rectangle*, which is a lie for any plan that is not a single box: on an L that put a band and a roof across the notch, hanging over nothing. Faces are now per block with internal ones dropped, and any block with nothing above it gets its own roof |
| X8 | **Every storey of every block carries its own ring**, including the topmost, so the storeys read as storeys |
| X6 | **Neighbouring blocks overlap by the bevel.** Every block carries a Bevel modifier 0.3 wide in its own space — 0.33 m off each vertical edge, 0.18 m off top and bottom. A bevel does not move a face plane, so butted blocks still touch across the flat middle, but the rounded strip either side of the seam leaves a lens-shaped gap that reads as a crack. Cells sit `CELL_BITE` closer and storeys sink `STOREY_BITE` deeper |

### Archetypes

Height alone does not read as tall — slenderness does. So the extremes are defined on two axes at
once, not by scaling one building up.

| Archetype | Count | Storeys | Base Ø | Annexes |
| --- | --- | --- | --- | --- |
| `tall` | 3 | 8–11 | 1.50–2.20 m | 0–3 |
| `large` | 3 | 2–4 | 4.50–7.00 m | 3 |
| `normal` | 14 | 1–4 | 1.40–3.00 m | 0–3 |

### Half rings

The kit's `BandArcThird` is 90° of arc, radius 1.049 about **its own object origin** — measured off
the file, because its bounding-box centre is nowhere near its centre of curvature, so it needs an
`origin` anchor that the full rings do not. It is the detail piece, and it goes in two places:

- where a full ring would have to cut through a window or the door, and
- part-way up a tall storey, 30 % of the time, where a full ring would lie about where the floor is.

An arc is only placed on a heading whose clearance beats **half the arc span + the opening's own
angular half-width + 6°**. That last term matters: a 0.62 m door on a 1.6 m drum subtends over 20°,
so clearing its centre line by 45° still clips its edge.

Everything angular in this generator works in **angles, not sector indices**, for the same reason —
odd storeys are offset half a sector by R7, so an index cannot say where an opening actually is.
Two of the defects below were exactly that mistake.

### Opening size caps — R4

Enforced twice: proportionally, so a window never swallows a narrow wall, and absolutely, so it
never gets silly on a wide one. The tightest wins.

| Opening | Width ≤ | Width ≤ | Height ≤ | Height ≤ |
| --- | --- | --- | --- | --- |
| Window | 20 % of the wall diameter at that height | 0.40 m | 32 % of the storey height | 0.46 m |
| Door | 30 % of the ground wall diameter | 0.62 m | 60 % of the height budget | 1.05 m |

The stock part is scaled **uniformly** to meet whichever cap binds, so the frame keeps its
proportions.

Two escape hatches keep the caps from producing blank buildings:

- **A door is never dropped** — a building without one reads as a silo — so if the caps would push
  it below the legibility floor, its scale is clamped up instead. Its height budget may also borrow
  45 % of the next storey and run up through the seam, which is fine because the drums already
  overlap there.
- **A window that fails the caps falls back to the kit's smallest aperture** (`rect_small`, 0.224 m
  wide) before being dropped. Without this the top half of a ten-storey tower came out blank: high
  up, the wall is under a metre across and 26 % of it is narrower than any porthole.

Measured over the finished file: **widest window 0.550 m, widest door 0.620 m** — both exactly on
the cap, nothing over it.

### Pipework — R14

The kit's own pipe run is a bezier following the original tower's cone at one fixed radius, so
copying it onto any other drum leaves part of the run hanging in the air. It is **generated**
instead: sampled against `wall_radius` at every step, so it hugs whatever wall it is on, whatever
that wall's diameter and taper. It is built as **mesh, not curve**, which also means it survives
the FBX exporter — `_exportlib` filters on `object_types={'MESH'}` and drops every curve silently.

Gauge comes from the kit: bevel depth 0.070 at object scale 0.4387, so a 0.061 m pipe, scaled with
the building. Two routings:

- **Riser** — a near-vertical service run climbing several storeys. Sweep is held under ±0.35 rad;
  any more and it reads as a cable drooping across the facade rather than pipe bolted to it.
- **Wrap** — 1.4–3.6 rad around one drum, hugging the top or bottom of the storey, because the
  middle belongs to windows.

Both start in a sector no opening claimed, and each run gets the kit's clamp and collar at both
ends and in the middle. The bracket is held to 75 % of the full gauge ratio; scaled straight off
the pipe it swamps a thin run.

## How a building is assembled

```
per unit (main body M, plus annexes A1..A3 fused into it):
  ground block                    generated, straight, Clay_Bone, carries the door
  + drum x storeys                generated: 32 sides, n-gon caps, flat shaded
    + ring stack at every seam    stock, scaled to the wall diameter + 0.14 or + 0.35
    + windows on the sector grid  stock
    + electrical boxes            stock, 3-9 per unit, three different boxes,
                                  sized 9-26 % of the drum diameter
    + pipe runs                   generated against the wall, stock clamps
    + pilasters                   stock, main body only
  + crown                         flat cap | crowned hat | vented deck | open deck
    + chimneys, masts, dish       stock, on the roof
```

Drums and pipes are **generated**, not appended — that is what lets height, width and taper vary
while R1 and R14 still hold. Everything else is a copy of a kit part. Copies share their source's
mesh datablock, so 3741 objects cost **280 meshes**, and an edit to one kit part propagates to
every building that used it.

## The twenty

Storeys and base Ø are the main body's; height includes annexes and roof furniture.

| Building | Storeys | Base Ø | Annexes | Crown | Windows | Height | Parts |
| --- | --- | --- | --- | --- | --- | --- | --- |
| B01 | 3 | 1.54 | 0 | vented deck | rect+round | 5.27 | 79 |
| B02 | 2 | 4.53 | 3 | vented deck | rect | 6.10 | 210 |
| B03 | 2 | 1.87 | 3 | vented deck | rect+round | 3.40 | 258 |
| B04 | 1 | 1.62 | 1 | flat | rect+round | 3.31 | 133 |
| B05 | 3 | 1.92 | 0 | vented deck | round | 5.66 | 98 |
| B06 | 8 | 2.11 | 2 | deck | rect | 11.56 | 337 |
| B07 | 4 | 2.67 | 3 | deck | rect | 5.42 | 270 |
| B08 | 10 | 1.88 | 3 | flat | rect+round | 13.34 | 376 |
| B09 | 1 | 1.91 | 1 | vented deck | rect | 3.13 | 120 |
| B10 | 2 | 2.96 | 1 | crowned | round | 6.17 | 132 |
| B11 | 4 | 2.10 | 0 | flat | rect | 5.69 | 114 |
| B12 | 2 | 2.07 | 2 | vented deck | rect+round | 3.67 | 187 |
| B13 | 10 | 1.94 | 2 | crowned | rect+round | 13.14 | 398 |
| B14 | 2 | 4.91 | 3 | flat | rect | 4.36 | 161 |
| B15 | 2 | 2.22 | 2 | deck | round | 5.38 | 142 |
| B16 | 3 | 1.64 | 1 | flat | rect | 4.91 | 127 |
| B17 | 3 | 2.50 | 1 | deck | rect+round | 6.52 | 115 |
| B18 | 1 | 2.35 | 0 | flat | round | 3.79 | 42 |
| B19 | 4 | 2.27 | 3 | deck | rect+round | 6.54 | 225 |
| B20 | 2 | 6.48 | 3 | flat | rect+round | 5.41 | 217 |

17 of the 20 are fused, 34 annexes in total. Tallest are B08 and B13 at 13.3 m; widest is B20 at
6.5 m across before its annexes.

They are laid out on a 5 × 4 grid at 17 m pitch. That is a contact sheet, not a town plan — place
them properly with instances.

## Moving and placing them

Each building has an **`Empty_BNN_Root` at its ground centre with every one of its parts parented to
it, annexes included**. Grab the empty and the whole complex follows. Each collection's
`instance_offset` is set to the same point, so `Add ▸ Collection Instance` drops a building with its
base on the 3D cursor.

Instance rather than duplicate: one edit to `Coll_NomadBuilding_07` then updates every placement of
it, which is the whole point at settlement scale.

## Tuning it

Everything worth turning is a named constant at the top of the script.

| Knob | Effect |
| --- | --- |
| `SEED` | A different settlement. Same seed, same twenty buildings, forever |
| `OPENING_RULES`, `SMALL_WINDOW` | The size caps, and the fallback aperture |
| `TAPER` | The kit's wall angle. Changing it makes a different town, not a broken one — but change it in `KIT_MAP.md` too, or the doc lies |
| `RING_STACKS`, `RING_GAP` | How banded the town is — the dial for more or fewer full rings |
| `ARC_SEAM_CHANCE`, `ARC_MID_CHANCE`, `ARC_CLEARANCE` | How often a blocked ring becomes a half ring, how often a storey gets a mid-height arc, and how far an arc keeps off an opening |
| `DOOR_KEEPOUT` | R16 — how wide a berth everything gives the doorway |
| `GEAR_MIN_FRAC`, `GEAR_MAX_FRAC` | Electrical box size range, as a fraction of the drum |
| `FUSE_BITE` | How deeply annexes merge. Lower reads as separate huts touching, higher as one mass |
| `CROWN_HAT_MAX_D` | Above this drum diameter the crowned hat becomes a parasol and is swapped out |
| `PIPE_SRC_RADIUS`, `PIPE_STANDOFF`, `PIPE_SEGS` | Pipe gauge, how proud it sits, how round it is |
| `MIN_TOP_D`, `JOINT_SINK`, `JOINT_STEP` | Drum floor and joint overlaps |
| `SECTORS`, `EDGE_MARGIN` | Opening grid and how far openings stay off a seam |
| `GRID_PITCH`, `GRID_COLS`, `COUNT` | Contact-sheet layout and how many buildings |
| `roll_settlement` | The decks — archetype mix, storey mix, crown mix, window families, fusion counts, dressing odds |

## Verified

A validator re-opens the finished `.blend` and checks the rules against the geometry rather than
against the script's intentions. Every unit — main body and each annex — is checked independently,
on its own axis:

```
buildings=40 units=74 annexes=34 objects=8137
doors=54 windows=420 rings=167 pipes=78 electrical-boxes=248
block units=20 detail-panel parts=1630
widest window 0.400 m, widest door 0.620 m
meshes=399 materials=8
ALL RULES HOLD      floaters 0      trim overhangs 0
```

It checks every coned drum's angle against 6.4° ± 0.35°, that every ground storey is straight, every
drum's top diameter against the floor, every seam for both z-overlap and radial step, that every
unit has exactly one door, that every building has one root empty with everything parented to it and
an instance offset, that every opening straddles the wall rather than floating off it or sinking into
it, both opening size caps, that no crown overhangs its drum past its type's ratio, **that no ring
or arc overlaps an opening in both height and angle**, **that no box, pilaster, pipe or bracket
stands in the doorway**, and — vertex by vertex — that no pipe run wanders more than 160 mm from the
wall it is supposed to be bolted to.

Defects it caught, all now fixed:

- a drum clamped to `MIN_TOP_D` finishing at 2.77° instead of 6.4°;
- doors skipped entirely on short ground storeys;
- an annex generated at 0.66 m across, too narrow to be a room or to hold a door;
- `Clay_Bone` vanishing from the file once the plinth was removed, because no appended part carried
  it;
- at the diameter floor, two consecutive drums clamping to the same radius, leaving a seam with no
  radial step and two coincident walls;
- pipe **brackets** crossing a doorway their run had legitimately cleared, because the guard tested
  the run's centreline and the bracket is a 0.2 m block;
- a pipe run stepping straight over a doorway between two samples, because the guard tested sample
  points rather than the whole swept interval;
- arcs and boxes landing on a door or window because the guard compared **rounded sector indices**,
  and openings on odd storeys sit half a sector off that grid. Everything angular now compares
  angles, and against the opening's real width, not its centre line.

## Known gaps

- **Negative scales are inherited from the kit.** 36 kit objects carry a negative scale axis, which
  inverts face winding; every copy of those parts carries it too. See the Gotchas in `KIT_MAP.md`.
  Fixing it means changing the kit's geometry, which is a separate decision.
- **The roof-vent cowls are still bevelled curves** — two per vented deck — and `_exportlib.export`
  filters on `{'MESH'}`, so they will be **silently dropped on FBX export**. The pipe runs no longer
  have this problem because they are generated as mesh; the cowls come straight from the kit.
  Convert them in the in-memory copy at export time.
- **Antenna masts are wire-thin** at kit scale and nearly invisible at distance. They at least carry
  a material now: the kit leaves masts, dish and curves with no material at all, and the generator
  assigns `Mat_Nomad_Metal_Grey` to any source part that has none rather than editing the hand-made
  component file.
- **Materials are the kit's eight local ones**, not links from `palette.blend`, for the same reason
  the kit keeps them local — swapping would change the colours. All eight are appended explicitly so
  the file always carries the full palette.
- **Annexes do not share interiors with the main body.** They interpenetrate geometrically; nothing
  cuts an opening between them. Fine for exterior silhouettes, not for a walkable interior.
- **No LOD, no collision, no UVs.** These are silhouette blockouts of production quality, not
  finished game assets.
- **The kit now ships ~80 materials** because duplicating an object in Blender duplicates its
  material. They are byte-identical copies of the same eight colours; the generator folds them back
  at load time rather than editing the hand-made component file.
- **Rectangular openings sit on the storey's outer rectangle**, so on an L-shaped plan nothing is
  placed in the notch. That is deliberate — it is also what the eye reads — but it means the inner
  faces of an L are blank.
- **An opening's `pre_z` is measured, never assumed.** The door group is
  0.642 x 0.181 x 0.996 with its leaf on the **+Y** side — it already stands with its width on X and
  its outer face on +Y, so it needs a **half** turn to join the windows on −Y. It carried a quarter
  turn, which laid all forty doors flat along their walls and buried them in the masonry. The
  validator now checks it: on a block unit by how much of the door's volume is inside the walls (a
  seated door is about a quarter embedded, a turned one nearly all of it), and on a drum by the same
  radial straddle test the windows use — a box-overlap test says nothing about a cylinder.
- **Tapered blocks carry bands but no openings.** Their batter would gap a frame.
- **No arches anywhere.** An arcade generator was built and removed at the user's request; the kit
  has no arch part either.
- **Openings keep 0.46 m off a block's corner**, because the bevel rounds 0.33 m off each vertical
  edge and anything nearer hangs off the curve.
- **A face's free-space test checks height as well as width.** Sideways only let one panel claim a
  whole face and the other fifteen were dropped, which is why walls came out nearly bare.
- **`Cube.012` is the kit's top extension, not a storey.** It pinches to a 1.2 m neck at its base,
  so used as a storey it read as a missing block with the band and the storey above levitating over
  the gap. Left out of the plans.
- **Wall panels are buried to 64 % of their depth.** They are deep — backplate plus dials — and the
  quarter-depth embed used for a window left them standing a third of a metre off the wall.
