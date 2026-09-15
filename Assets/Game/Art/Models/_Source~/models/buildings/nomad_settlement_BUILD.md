# Nomad settlement — building generator

A rule-based system for making nomad buildings out of the existing kit, and twenty buildings
produced with it.

| File | What it is |
| --- | --- |
| `nomad_settlement.py` | The system: the round family and the shared rules. |
| `nomad_rect.py` | The boxy half: block plans, terraces, detail discovery. |
| `nomad_palette.py` | The colour map: eight roles, the contrast ladder between them, and the schemes. |
| `nomad_settlement_flip_doors.py` | One-shot: turned the shipped file's 74 doors round without rebuilding it. |
| `nomad_settlement_export.py` | Ships each building to Unity as its own FBX under `Assets/Game/Art/Models/Environment/Structures/NomadSettlement/`. |
| `nomad_settlement.blend` | Its output: **40 buildings** in three families, 82 units, 11331 objects, 403 meshes. |
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
| R17 | **A rectangular opening is the norm and a porthole is the accent.** The window-family deck is three `rect` to two `mixed` to one `round`, and `mixed` itself is weighted two rect to one round. Measured over the finished file: **80 % of the openings are rectangular** |
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
| X9 | **Every building stands on a foundation**, both families. The boxy one takes the kit's square or rectangular slab, sized to its plan; a drum takes the kit's circular slab, **one pad per building rather than one per unit** — a pad each would give two overlapping slabs coplanar tops to z-fight over wherever an annex meets the main body. The pad reaches 0.42 m past the outermost wall of the outermost annex, stands 0.22 m proud of the ground line and is buried 0.30 m below it, and **the drums start on top of it**, so the door and its step stand on the pad instead of being buried by it |
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
| Window | 15 % of the wall diameter at that height | 0.30 m | 26 % of the storey height | 0.36 m |
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

Measured over the finished file: **widest door 0.620 m**, exactly on the cap; the widest window
measures **0.318 m** across as it sits on the wall, which is its 0.30 m aperture plus what R6's lean
adds to a horizontal measurement. Nothing over either cap.

**How many, not just how big.** The count is capped as hard as the size is, because a wall of
windows reads as an office block and these are mud huts. A drum takes one window per 2.2 m of
diameter on top of a 1-3 draw; a flat face takes one per 3.3 m of width, and a third of the faces
take none at all. Over the forty that is **410 openings**, down from 591 when the count followed the
circumference twice as fast.

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
per building:
  foundation pad                  stock slab, one per building, every building (X9)
per unit (main body M, plus annexes A1..A3 fused into it):
  ground block                    generated, straight, Clay_Bone, carries the door
  + drum x storeys                generated: 32 sides, n-gon caps, flat shaded
    + ring stack at every seam    stock, scaled to the wall diameter + 0.14 or + 0.35
    + windows on the sector grid  stock
    + electrical boxes            stock, 5-9 per unit, three different boxes,
                                  sized 9-26 % of the drum diameter
    + wall detail panels          stock, 6-15 per unit, from the same pass as the
                                  boxes and under the same R16 door keep-out
    + pipe runs                   generated against the wall, stock clamps
    + pilasters                   stock, main body only
  + crown                         flat cap | crowned hat | vented deck | open deck
    + chimneys, masts, dish       stock, on the roof
```

Drums and pipes are **generated**, not appended — that is what lets height, width and taper vary
while R1 and R14 still hold. Everything else is a copy of a kit part. Copies share their source's
mesh datablock, so 3741 objects cost **280 meshes**, and an edit to one kit part propagates to
every building that used it.

## The forty

What the generator prints as it builds them. Storeys and annexes are the main body's; `top` is the
top of the main body, before its crown and roof furniture; `parts` is every object in the building,
annexes included.

| Building | Family | Plan / archetype | Storeys | Annexes | Parts | Top |
| --- | --- | --- | --- | --- | --- | --- |
| B01 | round | normal | 3 | 0 | 207 | 4.29 |
| B02 | round | large | 2 | 3 | 454 | 2.73 |
| B03 | round | normal | 2 | 3 | 489 | 2.87 |
| B04 | round | normal | 1 | 1 | 235 | 1.64 |
| B05 | round | normal | 3 | 0 | 213 | 4.27 |
| B06 | round | tall | 8 | 2 | 567 | 10.13 |
| B07 | round | normal | 4 | 3 | 585 | 5.21 |
| B08 | round | tall | 10 | 3 | 696 | 13.30 |
| B09 | round | normal | 1 | 1 | 223 | 1.50 |
| B10 | round | normal | 2 | 1 | 358 | 2.29 |
| B11 | round | normal | 4 | 0 | 232 | 5.38 |
| B12 | round | normal | 2 | 2 | 290 | 2.08 |
| B13 | round | tall | 10 | 2 | 532 | 12.06 |
| B14 | round | large | 2 | 3 | 563 | 2.54 |
| B15 | round | normal | 2 | 2 | 297 | 2.16 |
| B16 | round | normal | 3 | 1 | 308 | 3.84 |
| B17 | round | normal | 3 | 1 | 340 | 4.06 |
| B18 | round | normal | 1 | 0 | 165 | 1.71 |
| B19 | round | normal | 4 | 3 | 557 | 5.33 |
| B20 | round | large | 2 | 3 | 502 | 3.09 |
| B21 | rect | Wing | 2 | 0 | 125 | 2.93 |
| B22 | rect | Hut | 2 | 0 | 82 | 2.37 |
| B23 | rect | Stack | 3 | 0 | 80 | 2.87 |
| B24 | rect | Long | 3 | 0 | 229 | 3.98 |
| B25 | rect | Hut | 2 | 0 | 152 | 2.92 |
| B26 | rect | Tee | 1 | 0 | 100 | 1.80 |
| B27 | rect | Wing | 2 | 0 | 138 | 2.50 |
| B28 | rect | Yard | 2 | 0 | 202 | 2.79 |
| B29 | rect | Tee | 2 | 0 | 154 | 3.11 |
| B30 | rect | Long | 2 | 0 | 176 | 3.34 |
| B31 | rect | Ell | 1 | 0 | 85 | 1.84 |
| B32 | rect | Ell | 2 | 0 | 159 | 3.01 |
| B33 | hybrid | Hut | 1 | 1 | 174 | 4.57 |
| B34 | hybrid | Stack | 2 | 1 | 213 | 7.25 |
| B35 | hybrid | Wing | 1 | 1 | 278 | 4.66 |
| B36 | hybrid | Yard | 1 | 1 | 192 | 5.14 |
| B37 | hybrid | Tee | 1 | 1 | 225 | 6.86 |
| B38 | hybrid | Ell | 1 | 1 | 259 | 7.07 |
| B39 | hybrid | Long | 2 | 1 | 303 | 6.68 |
| B40 | hybrid | Hut | 1 | 1 | 192 | 4.63 |

34 annexes over the set. The round family carries all of them and the hybrid's tower counts as a
unit of its own, which is what makes 40 buildings into 82 units. Tallest are B08 and B13; widest is
B20 at 6.5 m across before its annexes.

They are laid out on an 8 × 5 grid at 17 m pitch. That is a contact sheet, not a town plan — place
them properly with instances.

## Moving and placing them

Each building has an **`Empty_BNN_Root` at its ground centre with every one of its parts parented to
it, annexes included**. Grab the empty and the whole complex follows. Each collection's
`instance_offset` is set to the same point, so `Add ▸ Collection Instance` drops a building with its
base on the 3D cursor.

Instance rather than duplicate: one edit to `Coll_NomadBuilding_07` then updates every placement of
it, which is the whole point at settlement scale.

## Colour — the map, not the hexes

The kit's eight materials are eight **jobs**, not eight colours, and they live mapped to those jobs
in `nomad_palette.py`. Retuning the town is one word — `SCHEME` at the top of `nomad_settlement.py`,
or `--apply <scheme>` on any file that already exists.

| Role | Material | What actually carries it (measured off the finished file) |
| --- | --- | --- |
| `wall` | `Mat_Nomad_Clay_Sand` | drums, block faces, meter-box bodies — 1964 parts, the base colour |
| `footing` | `Mat_Nomad_Clay_Bone` | R12's ground block on the 54 round units, and nothing else — the hand-tuned yellow |
| `joinery` | `Mat_Nomad_Clay_Ochre` | door leaves, rect blocks, foundations, simple roofs |
| `trim` | `Mat_Nomad_Clay_Terracotta` | rings and bands, door frames, doorsteps, roof decks, stacks |
| `shadow` | `Mat_Nomad_Clay_Oxblood` | vent slots, recesses, deep detail — the near-black |
| `pipe` | `Mat_Nomad_Metal_Charcoal` | pipe runs and their clamps |
| `fitting` | `Mat_Nomad_Metal_Grey` | collars, brackets, masts, dish, and anything the kit left bare |
| `glass` | `Mat_Nomad_Glass_Amber` | every window pane — the brightest thing on a building |

What must survive a retune is not a hue, it is the **ladder** — the relative-luminance order and the
gaps between neighbours. Two principles, both `contextual`, confidence 4:

- `GDC-L1-LEVEL-0002` asks for districts with **their own palette, architecture and mood, so an area
  is identifiable at a glance**. That is the argument for schemes rather than one fixed set of hexes:
  a second settlement in `ash` reads as a different place built by the same hands.
- `GDC-L1-LEVEL-0001` says light and contrast are the strongest tools, and that the **strongest
  contrast should be reserved for what matters**. That is the argument for the ladder being a rule
  rather than a note: a retune that flattens wall against trim does not just look different, it stops
  the facade guiding the eye.

Worth deciding rather than inheriting: by that second principle the **door** should own the strongest
local contrast on a building, since it is the thing that matters. Today it does not — the brightest
roles are `glass` and `footing`, and the door is `trim` frame on `joinery` leaf, a middling step down
from the wall. Left as it is because it is a colour-direction call, not a bug.

`CONTRASTS` states it as rules — panes brighter
than the wall, footing lighter than the adobe it carries, banding markedly darker or the storeys stop
reading, vents the near-black — and `check` refuses a scheme that breaks one before it reaches a
`.blend`. `MIN_SEPARATION` catches the other failure: two roles so close in value that the palette
has quietly collapsed to seven colours.

```bash
blender --background --python nomad_palette.py -- --list       # roles and rules
blender --background --python nomad_palette.py -- --check      # every scheme, with its ladder
blender --background <file>.blend --python nomad_palette.py -- --dump
blender --background <file>.blend --python nomad_palette.py -- --apply ash --save
```

Three schemes ship: `nomad` (what the file wears — warm clay, with the hand-tuned yellow bone
course), `ash` (cold volcanic grey), `verdigris` (oxidised copper over bleached clay). A fourth is
eight hex values. `apply` touches the colour, roughness and metallic of the eight mapped materials
and **nothing else** — no geometry, and no material the map does not name — so it is safe on a
hand-edited file. It reaches `.001`-suffixed duplicates too, which is what the kit is full of.

## Tuning it

Everything worth turning is a named constant at the top of the script.

| Knob | Effect |
| --- | --- |
| `SEED` | A different settlement. Same seed, same twenty buildings, forever |
| `SCHEME` | Which entry of `nomad_palette.SCHEMES` the town is painted in — all eight colours at once |
| `OPENING_RULES`, `SMALL_WINDOW` | The size caps, and the fallback aperture |
| `WINDOW_SETS`, `WINDOW_DECK` | R17 - which windows a family may use, and how often each family is dealt. Rectangular is the norm here; swap the deck to turn the town round again |
| `ROUND_FOUNDATION`, `FOUNDATION_PROUD`, `FOUNDATION_BURIED`, `FOUNDATION_MARGIN` | X9 - which slab a drum stands on, how far it stands proud, how deep it is buried, and how far it reaches past the walls |
| `TAPER` | The kit's wall angle. Changing it makes a different town, not a broken one — but change it in `KIT_MAP.md` too, or the doc lies |
| `RING_STACKS`, `RING_GAP` | How banded the town is — the dial for more or fewer full rings |
| `ARC_SEAM_CHANCE`, `ARC_MID_CHANCE`, `ARC_CLEARANCE` | How often a blocked ring becomes a half ring, how often a storey gets a mid-height arc, and how far an arc keeps off an opening |
| `DOOR_KEEPOUT` | R16 — how wide a berth everything gives the doorway |
| `GEAR_MIN_FRAC`, `GEAR_MAX_FRAC` | Electrical box and wall-panel size range, as a fraction of the drum |
| `spec["panels"]`, `spec["greebles"]` | How much wall decoration a unit carries, and how many of those are electrical boxes rather than detail panels. Asked for separately: one roll for both meant more decoration also meant more meter boxes |
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
buildings=40 units=82 annexes=34 objects=11331
doors=74 windows=410 rings=177 arcs=35 pipes=654 foundations=40
wall furniture 803 groups: 5963 panel parts + 2476 box parts
widest window 0.318 m on the wall (0.30 m aperture), widest door 0.620 m
meshes=403 materials=8
ALL RULES HOLD      floaters 0      trim overhangs 0
every part of every building stands on that building's foundation
```

It checks every coned drum's angle against 6.4° ± 0.35°, that every ground storey is straight, every
drum's top diameter against the floor, every seam for both z-overlap and radial step, that every
unit has exactly one door, **that every building has a foundation and that nothing on it sinks below
that foundation or stands off its footprint**, that every building has one root empty with everything parented to it and
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

## In Unity

`nomad_settlement_export.py` writes one FBX per `Coll_NomadBuilding_NN`, each moved onto its own
origin; `NomadSettlementBuilder` (menu *Tools ▸ Environment ▸ Build Nomad Settlement Prefabs*) turns
them into prefabs under `Assets/Game/Prefabs/Environment/Structures/NomadSettlement/`, sorted into
`Small` (6), `Medium` (22) and `Large` (12) by their finished size.

- **Everything is scaled to the astronaut, who is 2.00 m, and then grown by half again.** This kit
  is authored at about half the astronaut: the doors here measure 0.53–0.96 m. Each building is
  scaled so its own smallest door clears the astronaut with headroom, per building rather than one
  factor for the settlement, because the generator's doors vary two to one. On top of that sits
  `SettlementGrowth`, a flat 1.5× on the whole town — the door sets the scale a building is *read*
  at, this sets how much of the skyline it takes. Together that is **3.5×–6.8×**, a town **10–77 m
  across and 16–99 m tall** on 4.9–9.6 m storeys, with every doorway 3.38 m.
  The doorways are deliberately 1.7 astronauts tall: that is what a flat growth factor does to a set
  scaled by its doors, and it was the trade asked for. The size-class thresholds divide the growth
  back out, so `Small`/`Medium`/`Large` still split the set the way they were measured.
- **Collision is one convex hull per structural part** — `Drum*`, `Blk*`, `Foundation*`, the roof
  slabs, the roof cap and the parapet — and nothing at all on rings, arcs, windows, doors, panels,
  gear, pipes or roof furniture. Every structural part is convex by construction, so a hull is not an
  approximation, it is the shape: 2–25 hulls a building, 311 over the settlement.
- **The negative scales are repaired on the Unity side**, per renderer, by the sign of the
  determinant. `_exportlib`'s `fix_inverted` does it in Blender and on *this* file it also threw three
  buildings' mirrored parts up to 137 m away — these collections share one mesh datablock between as
  many as 75 objects. See the export script's docstring.
- **Each building carries shade sails** off its walls, from
  `components/nomad_settlement/tents.blend`, scaled to its own storeys and seated against its own
  colliders. How many it ends up with is what fits round it, not what was asked for: a seated sail
  owns the heading it sits on, so a wall is full at three or four of them. They are therefore hung in
  **three bands** — 3.5, 2.2 and 1.1 storeys up — which is what lets a building carry **two to
  twelve** without any of them being made smaller. A band that clamps to within 0.9 storeys of one
  already hung is skipped rather than stacked. A hung sail is then asked the question the verify
  pass asks — is every wall fixing touching masonry? — and one that cannot answer it is taken down
  again rather than shipped hanging in the air.
- **The freestanding tents are grown 2.5× in their own prefabs**, because nothing else sizes them: a
  wall sail is sized to the wall it hangs on, but a tent in a yard is placed at the size it ships at,
  and beside a building at this scale the authored size read as luggage. The growth goes on the
  parts inside the prefab and never on its root — `NomadSettlementGenerator.LongestSide` measures a
  tent in root-local metres to space the scatter, and a root scale is exactly what that does not
  see.

### What the Unity side reports

```
sails: 18 prefabs, 0 colliders, 18 cloth parts, 0 still back-face culled
Small: 6 buildings    Medium: 19 buildings    Large: 15 buildings
buildings 40, bare 0, sails hung 247
colliders 338 hulls, concave 0; shortest door 3.38 m against 2.00 m
sail scale 2.00-6.61x, fixings 5.0-19.0 m up, cloth 5.3-29.8 m across
fixings 506: on the wall 302, sunk in 197, off it 7 (worst 0.08 m)
mirrored parts still back-face culled: 0
```

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
  the file always carries the full palette, and then repainted from the colour map (below), so the
  kit's own colours are a starting point rather than the answer.
- **The kit file still holds the old colours.** `nomad_palette` is applied to the settlement at the
  end of generation, not to `components_clean.blend` — the kit is hand-made and is left alone. So the
  kit and `tents.blend` still show the kit's cream `Clay_Bone` while the settlement shows the mapped
  yellow one. Retune the kit deliberately if you want them to agree:
  `blender --background components_clean.blend --python nomad_palette.py -- --apply nomad --save`.
- **A settlement already placed in the world does not re-space itself.** The building and tent
  prefabs keep their GUIDs, so every settlement in the chunk scenes picks up the new geometry the
  moment it is rebuilt — at the new size, in the old positions. The spacing those positions were
  chosen for was a smaller town. Re-run *Tools ▸ SpaceGame ▸ Settlements ▸ Build Nomad Settlements*
  after a scale change, and check its report for tents it could not fit.
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
  its outer face on +Y, so it needs **no turn at all**: `pre_z = 0`. Two wrong values have shipped.
  A **quarter** turn laid every door flat along its wall and buried it in the masonry — that one the
  validator catches, on a block unit by how much of the door's volume is inside the walls (a seated
  door is about a quarter embedded, a turned one nearly all of it), and on a drum by the same radial
  straddle test the windows use, because a box-overlap test says nothing about a cylinder. A **half**
  turn is the one no geometric test can catch: the door's bounding box is unchanged, it is seated
  correctly, it straddles the wall correctly, and it is simply back to front — frame to the street,
  leaf inside the wall. Only looking at it finds that. Fixed in the shipped file by
  `nomad_settlement_flip_doors.py`, which turns each door about the vertical axis through its own box
  centre; because the box is symmetric about that axis, the half turn moves nothing but the facing
  (worst box drift over 74 doors: 0.000017 m).
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
