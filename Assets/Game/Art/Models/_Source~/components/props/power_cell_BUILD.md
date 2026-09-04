# Power Cell — build record

The swappable battery the game runs on. Same language as the oxygen tank: pale
enamel shell, one saturated accent, chunky rubber corners, wide chamfers.

> **`power_cell.blend` is hand-edited and is the source of truth.**
> Never re-run `power_cell.py` over it. The script is historical record.

| Collection | Size | Accent | State |
|---|---|---|---|
| `Coll_PowerCell_Slab` | 0.528 × 0.138 × 0.224 | green | the request — the generator's cell. Ships. |
| `Coll_PowerCell_Compact` | 0.268 × 0.103 × 0.185 | blue | built ahead — one-handed, bar handle. Ships. |
| `Coll_PowerCell_Drum` | — | — | **withdrawn by hand edit**, see below |

The variations differ in **silhouette** first: a two-handed brick and a
one-handed box with a folding bar over the lid. Cells that differed only in
accent colour would be one cell painted three ways.

**The Drum was withdrawn.** A hand edit removed its `Shell`, `Collar` and
`Port`, leaving `Coll_PowerCell_Drum` holding only `Mesh_PowerCell_Drum_Face` —
a readout plate with no cell behind it. `power_cell_export.py` no longer lists
it; leaving it there would hard-fail `_keep_only`, which refuses to ship a name
that is not in the file. The orphan `Drum_Face` is left in place rather than
deleted, because it is hand-authored geometry and removing it is not this
build's call to make.

## The slab is sized from the machine

Its long side is 0.52 against the generator's 0.60 face — it spans almost the
whole width, so where it goes is unmistakable before any prompt appears. Flat
enough to lie against a wall, deep enough at 0.13 to still read as heavy.

## Decomposition — six objects, never merged

`Shell`, `Bumpers`, `Face`, `Port`, `Strap`, `Latch`. The `Face` carries the
charge ladder; the `Port` is the whole docking interface; `Bumpers` are the
corners a dropped battery lands on and are what give the silhouette its stepped
ends.

## The charge ladder is five bars, not a colour

Charge reads as a **count** of lit segments — three of five in the rest pose —
so it survives a colour-blind player, a dark room and a distant glance. The
green is confirmation, never the message (`GDC-L1-UX-0003` states this
explicitly: never encode critical information in colour alone). Three lit rather
than five also makes the part look like a gauge at a value rather than a lamp
that is simply on.

## The charging port — one rectangle

`PORT = (0.110, 0.016, 0.052)`, centred on the flat back, published at the top of
`power_cell.py` and **imported** by `components/mechanical/dock_cradle.py`, which
cuts its socket 6 mm larger all round.

It replaced a pair of blade contacts flanking a 30 mm round locating peg. At the
size a cell is actually seen, the peg was the biggest thing on the back of the
object and read as a nozzle, and three separate fittings made a plain face busy
for no gain. One rectangle says "this plugs in" with one shape, and being
rectangular it enters one way up and no other (`GDC-L1-UX-0004`).

## Materials

`Mat_Paint_White_Arctic`, `Mat_Neutral_Panel_Grey`, `Mat_Plastic_Rubber_Black`,
`Mat_Metal_Steel_Dark`, `Mat_Metal_Chrome_Scuffed`, `Mat_Neutral_Black_Matte`,
`Mat_Neutral_Slate_Dark`, `Mat_Emissive_Green_CRT`, `Mat_Paint_Blue_Station`,
`Mat_Plastic_Safety_Yellow`, and `Mat_Paint_Cell_Green` — the one material this
work added to the palette (see `oxygen_generator_BUILD.md` for why nothing
existing served).

Indices 0–15 of `MATS` match `oxygen_tank.py` and `dock_cradle.py` position for
position, so parts appended from any of the three can share one material list;
0–9 additionally match `panel_control.MATS` so its builders can be called
against it. Index 0 is structural steel because `bmesh.ops.bevel` stamps every
edge it creates with material index 0.

## Gotchas this build produced

- **The bumpers were flush with the shell's ends.** Outer face exactly on the
  shell's own end plane — a guaranteed flicker on the most-seen part of the
  object. They now stand 12 mm proud and are buried 18 mm.
- **The plinth, the sleeve and the collar all shared a cap plane with the body.**
  Same failure four more times. Each now overshoots.
- **Grouping the front connector strip with the back contacts** made one object
  span the full depth of the cell, which is wrong for a part a game reaches by
  role. It moved to `Face`.

`_zverify.py` on the generated build: only cross-variation pairs, which are not
real — variations are stacked at the origin by library convention and never ship
together. Filter by the variation token before acting on that report.

On the **final hand-edited file: 7 same-variation pairs**, the largest 0.006 m².
`Mesh_PowerCell_Slab_Shell` now carries a slight non-uniform scale
(1, 1.037, 0.998) and a moved origin, which put two of its own faces 1.4 mm
apart; the `Port` blocks and the `Compact` shell have small internal coplanar
pairs. All are ≤ 0.006 m² and none is on a silhouette edge. Left as authored.

## Shipping

`power_cell_export.py` writes one FBX per shipping variation:
`power_cell.fbx` (Slab — the one the generator's dock is cut for) and
`power_cell_compact.fbx`, both under `Assets/Game/Art/Models/Props/`. The stale
`power_cell_drum.fbx` from before the Drum was withdrawn has been deleted.
No Unity builder or gameplay code yet.
