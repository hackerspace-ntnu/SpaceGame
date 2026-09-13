# Nomad Settlement Kit — component map

Reference for `components_clean.blend`: what every part is, how the pieces mate, how to scale
them, and how to move a finished building. Everything here was measured out of the file, not
assumed.

| File | What it is |
| --- | --- |
| `components.blend` | The original, exactly as hand-modelled. Untouched. |
| `components_clean.blend` | The same geometry with names, collections and materials cleaned up. Work here. |

**No geometry was changed.** Every object's transform, dimensions, vertex count, polygon count,
modifier stack and resolved material colour match the original one-for-one — verified by matching
objects between the two files on geometric fingerprint rather than on name, so a rename could not
hide a move. 93 objects before, 93 after.

What the cleanup did do:

- Renamed all 93 objects and their mesh/curve datablocks to `Mesh_Thing_Part` / `Curve_Thing_Part`.
  `detail_ring_1/3` in particular carried a `/` in its name, which breaks FBX export paths.
- Replaced the 13 ad-hoc collections (including one empty, `roof_ventilation`, and the catch-all
  `Collection`) with 18 collections, one per reusable component, all prefixed `Coll_`.
- Collapsed **63 material datablocks into 8**. The file used 8 distinct colours; the other 55
  datablocks were byte-identical duplicates created by Blender's copy-on-duplicate. Only materials
  whose *entire* node tree and settings matched were merged, so nothing could be flattened by
  accident.

---

## Materials

All eight are local to this file, not linked from the shared `palette.blend`, so the `Nomad` infix
keeps them from colliding if this file is ever appended next to the palette.

| Material | Hex | Roughness | Slots | Used for |
| --- | --- | --- | --- | --- |
| `Mat_Nomad_Clay_Terracotta` | `#B87D5B` | 0.5 | 28 | The main accent: bands, pilasters, footings, frames, chimneys |
| `Mat_Nomad_Metal_Charcoal` | `#525252` | 0.5 | 19 | Vent hardware, pipe clamps, the loose meter-box kit |
| `Mat_Nomad_Clay_Sand` | `#DFC196` | 0.5 | 15 | Tower walls, roof decks, domes — the body colour |
| `Mat_Nomad_Metal_Grey` | `#969696` | 0.5 | 7 | Vent housings, pipe collars, dials |
| `Mat_Nomad_Glass_Amber` | `#E7E18F` | 0.267 | 4 | Every window pane. The only material with a non-default roughness |
| `Mat_Nomad_Clay_Oxblood` | `#624535` | 0.5 | 6 | The plated meter box's greebles |
| `Mat_Nomad_Clay_Bone` | `#FFF2CD` | 0.5 | 1 | The plinth only |
| `Mat_Nomad_Clay_Ochre` | `#DFB38A` | 0.5 | 1 | The door leaf only |

Twelve objects carry **no material at all**: the four antenna masts, both dish parts, and all six
bevelled curves. They render default grey. That is pre-existing and was left alone.

---

## The kit

Eighteen collections. The tower at the origin is the one assembled example; everything else is
either a spare parked beside it or a sub-assembly waiting to be placed.

### Tower shell — `Coll_Tower_Body` (7 objects)

The stack, bottom to top. Diameters are outside diameter at the bottom and top of each piece.

| Object | Ø bottom → top | Height | Z range |
| --- | --- | --- | --- |
| `Mesh_TowerBody_Plinth` | 2.13 × 2.17 (not round — has cast feet) | 1.216 | −0.571 → 0.645 |
| `Mesh_TowerBody_SegLower` | 1.955 → 1.955 (straight) | 1.257 | 0.634 → 1.891 |
| `Mesh_TowerBody_SegMid` | 1.955 → 1.674 | 1.257 | 1.814 → 3.071 |
| `Mesh_TowerBody_SegUpper` | 1.666 → 1.426 | 1.071 | 3.052 → 4.123 |
| `Mesh_TowerBody_RoofCap` | 1.431 → 1.225 | 0.228 | 4.117 → 4.345 |
| `Mesh_TowerBody_StackWide` | 0.36 × 0.41 chimney | 0.334 | 4.272 → 4.606 |
| `Mesh_TowerBody_StackNarrow` | 0.22 × 0.27 chimney | 0.734 | 4.308 → 5.042 |

Total shell height 4.916 m; 5.042 m with the chimneys; 6.164 m with the tallest antenna.

### Tower trim — `Coll_Tower_Trim` (9 objects)

| Object | Size | Where it is |
| --- | --- | --- |
| `Mesh_TowerTrim_BandThin` | Ø2.098, 0.045 tall | Fitted, at z 1.348 |
| `Mesh_TowerTrim_BandArcThird` | Ø1.049 partial arc, 0.045 tall, 18 verts | Fitted, at z 1.583 |
| `Mesh_TowerTrim_BandTall` | Ø2.098, 0.220 tall | **Spare**, parked at x 2.78 |
| `Mesh_TowerTrim_BandWide` | Ø2.301, 0.241 tall | **Spare**, parked at x 3.48 |
| `Mesh_TowerTrim_PilasterMounted` | 0.107 × 0.178 × 1.228 | Fitted to the plinth |
| `Mesh_TowerTrim_PilasterTapered` | same, pitched 6.3° | Fitted to `SegMid`'s cone |
| `Mesh_TowerTrim_Pilaster` | same, upright | **Spare**, parked at x −2.82 |
| `Mesh_TowerTrim_FootPad` | 0.767 × 0.767 × 0.147 | Under the plinth |
| `Mesh_TowerTrim_DoorStep` | 0.429 × 0.584 × 0.082 | Threshold slab |

### Openings

| Collection | Frame | Pane | Notes |
| --- | --- | --- | --- |
| `Coll_Door_Single` | 0.181 × 0.642 × 0.996 | leaf 0.039 thick | Parked at x 2.25, rotated 90° about Z so it faces +X |
| `Coll_Window_RectLarge` | 0.642 × 0.125 × 0.457 | 0.578 × 0.018 × 0.408 | Fitted to the plinth, faces −Y |
| `Coll_Window_RectSmallHigh` | 0.352 tall | 0.301 tall | Faces +X, and leans 6.8° |
| `Coll_Window_RoundDeep` | Ø0.696, 0.241 deep | Ø0.58, 0.031 thick | Leans 5.6° off vertical |
| `Coll_Window_RoundFlat` | Ø0.691, 0.192 deep | Ø0.58, 0.031 thick | Leans 4.9° off vertical |

The two round windows share an identical pane and differ only in frame depth — deep reads as a
porthole set into a thick wall, flat as a plate stuck on one.

### Roofs

- **`Coll_RoofCap_Crowned`** (6 objects) — the pillared hat, parked in the air at z 6.93 → 8.50,
  1.57 m tall, 3.34 m across. `Mesh_RoofCap_Dome` is a **straight Ø1.955 drum**, which is the same
  diameter as `SegLower`/`SegMid`, so this drops straight onto a tower drum with no refitting.
  `Mesh_RoofCap_ColumnRing` carries a **Geometry Nodes modifier** — it is the only procedural object
  in the file, and it is what arrays the columns around the brim. Change the column count there.
- **`Coll_RoofDeck_Vented`** (11 objects) — walled deck, outer Ø2.086, inner Ø1.778, 0.463 tall,
  with a vent unit (housing, fan well, two ports, an elbow, two bevelled-curve cowls) and three
  loose blocks. Sits at x ≈ −4.3, z 2.84.
- **`Coll_RoofDeck_Vented_B`** (11 objects) — an exact duplicate of the above, parked one metre
  lower and to the side. Same geometry, different position. It is a working copy, not a variant;
  delete it if you don't want two.

### Bodies and pipework

- **`Coll_Drum_Plain`** — `Mesh_Drum_Plain`, Ø1.931 → 1.851, 1.174 tall. A near-cylindrical hut
  body. Its taper is only 2°, unlike the tower's 6.4°, so it does **not** stack cleanly under the
  tower segments.
- **`Coll_PipeWrap_Upper`** / **`Coll_PipeWrap_Lower`** — one bevelled bezier run wrapping the
  building, plus a clamp and a collar at each end (4 blocks per run). The two runs are the same
  shape at z 1.24 and z 0.83. Each run spans about 1.74 × 1.24 m.

### Greebles

| Collection | What it is |
| --- | --- |
| `Coll_Greeble_PanelBox` | Wall-mounted panel box, 0.625 × 0.198 × 0.679, body plus three plates |
| `Coll_Greeble_MeterBoxPlated` | The same instrument set assembled on a 0.45 backplate with an oxblood face |
| `Coll_Greeble_MeterBoxLoose` | The same instrument set loose and unpainted — vents, lip, stud, two dial plates, two dials |

`MeterBoxPlated` and `MeterBoxLoose` share a part-for-part identical set of eight greebles. Plated
adds the backplate and face; loose is the raw kit for scattering across other surfaces.

### Antennas

- **`Coll_Antenna_Masts`** — four independent masts. `Tall` 1.836 m, `Offset` 1.937 m (bent out from
  the wall), `Thin` 1.618 m, `Bulky` 1.369 m. All currently stand above the tower's roof cap.
- **`Coll_Antenna_Dish`** — Ø0.78 reflector on a 0.711 m tapered mast.

---

## Sizing rules

**The tower's wall taper is a single constant: a 6.4° half-angle, on every coned segment.**
That works out to

```
top Ø = bottom Ø − 0.2243 × height
```

Checked against the file: `SegMid` 1.955 − 0.2243 × 1.257 = 1.673 (measured 1.674); `SegUpper`
1.666 − 0.2243 × 1.071 = 1.426 (measured 1.426). Build any new segment to that formula and it will
mate with the existing ones at any height you choose.

**Scaling a component up or down.** Every part is scaled at the object level and no transform has
been applied — there is not one object in the file at scale 1.0. So:

- Scaling a single object in the viewport is safe and is how the parts were built.
- Scale **uniformly** if you want the piece to keep mating. Squashing a drum on Z alone changes its
  taper angle and it will no longer meet its neighbours.
- To grow the whole tower, scale the collection's objects together about the plinth's base, not
  about each object's own origin — origins are scattered, not at connection points.
- The ring bands do not follow the 6.4° rule; they are straight cylinders sized by diameter. Match a
  band to the drum diameter at the height you want it, not to the drum's nominal size.

**The diameter ladder.** Diameters in the kit are not arbitrary; they come in a small set, and that
is what makes the parts interchangeable.

| Ø | Used by |
| --- | --- |
| 3.32 / 3.24 | Crowned cap brim and its brim band |
| 2.30 | `BandWide`, and the crowned cap's lower and upper bands |
| 2.13 | Plinth |
| 2.10 | `BandThin`, `BandTall` |
| 2.09 | Roof deck, outer |
| 1.96 | `SegLower`, `SegMid` base, `RoofCap_Dome`, `Drum_Plain` |
| 1.78 | Roof deck, inner clear |
| 1.67 | `SegMid` top, `SegUpper` base |
| 1.43 | `SegUpper` top, `RoofCap` base |

---

## What fits what

**Stacking, bottom to top.** Every joint in this kit is built as a deliberate overlap, never a
face-on-face butt — that is what keeps it free of z-fighting. Measured overlaps:

| Joint | Z overlap | Radial fit |
| --- | --- | --- |
| Plinth → SegLower | 0.011 | drum sits inside the plinth's top |
| SegLower → SegMid | 0.077 | same Ø 1.955, flush |
| SegMid → SegUpper | 0.019 | SegUpper is 0.008 narrower, so it seats **inside** |
| SegUpper → RoofCap | 0.006 | RoofCap is 0.005 wider, so it sleeves **over** |

Keep 10–80 mm of z overlap and a few millimetres of radial difference when you add a segment.

**Interchangeable joins:**

- Anything Ø1.955 mates with anything else Ø1.955 — that is `SegLower`, the base of `SegMid`,
  `RoofCap_Dome` and (within 25 mm) `Drum_Plain`. The crowned cap therefore drops onto any of them.
- `Coll_RoofDeck_Vented` has a 1.778 inner clear, so it slips over the `SegMid`/`SegUpper` junction
  (1.674 / 1.666) as a balcony.
- The Ø2.098 bands ride a Ø1.955 drum with 72 mm standing proud. The Ø2.301 bands ride the same drum
  with 173 mm proud — that is the heavier cornice look, and it is exactly what the crowned cap uses.
- The window panes are sized to their own frames only: Ø0.58 pane in a Ø0.69 frame, 55 mm of lip.
- Both pipe runs and both meter boxes are surface dressing — they have no mating diameter and go
  anywhere on a wall.

**Facing.** Fitted windows sit on the −Y face, which matches the library's −Y-forward convention.
The door and the small high window face +X. The round windows are pre-leaned about 5° so they lie
flush against the coned wall — that lean is the wall's own slope, so if you move one onto the
straight `SegLower` you must level it.

---

## Moving a building

**Nothing in this file is parented — all 93 objects are loose, and no object has a parent.** So
moving a building today means box-selecting or selecting its collection and moving all of its
objects at once, and the moment two buildings overlap in the viewport that gets error-prone.

Two ways to fix that, both additive and neither done here:

1. **An empty per building.** Add one empty at the base of each assembly, parent that assembly's
   objects to it, and move the empty. Cheapest option, works with the existing collections.
2. **Collection instances.** Set each `Coll_` collection's instance offset to its intended ground
   contact point, then place `Add ▸ Collection Instance` copies around the scene. One edit to the
   source collection then updates every placement, which is what you want for a settlement of many
   buildings from one kit.

Option 2 is the one that pays off at settlement scale. Say the word and I'll wire it up.

---

## Gotchas

- **No transforms are applied. Not one object is at scale 1.0.** 78 objects have non-uniform scale.
  This is fine in Blender and it is how the kit was modelled, but the FBX exporter here applies
  nothing, so those scales arrive in Unity as node scales on the imported prefab. Any C# that
  assigns `localScale` will erase them, and anything measuring `mesh.vertices` will miss them —
  measure through `localToWorldMatrix`.
- **36 objects have a negative scale on at least one axis.** A negative scale mirrors the object and
  inverts its face winding, which shows up in-engine as faces lit from the wrong side or culled
  entirely. Worth fixing before export, but fixing it changes geometry, so it was left alone. The
  full list is every `MeterBox*` greeble, all eight `PipeWrap` clamps and collars, all three
  pilasters, both rect window frames, the door frame, both vent housings, all four vent ports,
  `RoofCap_ColumnRing` and `AntennaMast_Tall`.
- **Nothing is on the 0.25 m grid** the library uses for modular pieces. The kit is internally
  consistent but will not snap against components from other `.blend` files.
- **Two small angle mismatches**, both harmless but visible at a grazing angle: the two chimney
  stacks are rotated 361.21° about X (a full turn plus 1.21°), and on `Coll_Window_RoundDeep` the
  frame leans 84.38° while its pane leans 84.22° — a 0.16° disagreement.
- **The cowls are curves, not meshes.** `Curve_RoofVent_CowlA`/`B` (and their `_B` copies) and both
  `Curve_PipeWrap_Run_*` are bevelled beziers. `_exportlib.export` builds its `object_types` set as
  `{'MESH'}` plus optionally `EMPTY` and `ARMATURE` — `CURVE` is never in it, so all six will be
  **silently dropped on export**. Convert them to mesh in an in-memory copy at export time and
  assert the part count afterwards.
- **`Coll_RoofDeck_Vented_B` is a duplicate**, not a variant. Same geometry as `Coll_RoofDeck_Vented`.
- **The materials are local, not from `palette.blend`.** Every other component in this library links
  its materials from the shared palette. Switching this kit over would change its colours, so it was
  not done — it is a call for you to make.

## If you want more variety

The kit currently has one real variation axis (window shape) and repeats everything else. The
cheapest additions that would change silhouettes rather than just colour:

- More drum segments at the 6.4° rule but different heights — a squat 0.6 m ring and a tall 2.0 m
  one give a settlement of towers that are not all the same proportion.
- A collapsed or half-built variant of the crowned cap.
- Two more roof deck treatments — open railing, and a solid parapet — since the deck is the piece
  most visible from a distance.
