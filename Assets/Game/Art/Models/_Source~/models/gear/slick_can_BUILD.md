# Slick can — build record

The aerosol can of the sprayer kit — the smallest item in the set and the only
one that is not a gun. Built 2026-09-07.
Design: [`docs/AI/systems/Artifacts/SlickCan.md`](../../../../../../docs/AI/systems/Artifacts/SlickCan.md).

| File | |
|---|---|
| `slick_can.py` | generator — historical record, never re-run over the `.blend` |
| `slick_can.blend` | **source of truth** |
| `slick_can_export.py` | re-runnable export |
| `Assets/Game/Art/Models/Items/slick_can.fbx` | what Unity imports |

**0.3950 m tall**, 0.116 × 0.127 m in plan, 4 172 tris, 11 objects (8 meshes +
3 markers), 9 palette materials. +Z up, −Y forward, 1 unit = 1 m.
`_zverify`: **0 clashing pairs**.

## Scale — and the one place this diverges from the brief

Bracketed against `models/gear/dragon_bazooka.blend` (1.3685 m in Blender, worn
at `holdSize` 1.25 — the anchor of `ItemScaleLadder.cs`).

The wave brief bracketed all three sprayers at ~0.5 m. The design doc is more
specific about this one — *"About 0.4 m, one-handed — the smallest of the
sprayers, closest to an aerosol can in the hand"* — and the doc wins: the step
down is a stated design intent, not a rounding. Authored at **0.395 m**, one
bracket below the foam gun and the cryo sprayer.

**The long axis is Z, not Y.** The can stands; it does not point. The export
maps that onto Unity's Y, so `ItemGrip.holdSize` — which is the longest axis —
is this item's *height* while it is the other three sprayers' *length*. The
export prints the number; take it from there rather than assuming the family's
0.5.

## Not a gun, and that is the design

Three guns and a can. The can is what makes the set legible in a hotbar at icon
size (`GDC-L1-UX-0003`): `BatchIconGenerator` frames every item from its own
bounds, so the only item with a vertical silhouette is unmistakable next to
three horizontal ones. It is reinforced everywhere:

- The gauge runs **up** the flank instead of along it — same plate, same lit
  strip, rotated a quarter turn.
- There is **no pistol grip**. The hand wraps the can body the way a hand wraps
  a spray can, and the control is `kit.trigger_collar` — a thumb lever on a
  collar with a guard under it. A grip here would have made it a fourth gun.
- The fan head is the **only non-round mouth** in the nine-piece set: a wide
  flat slot with a dark recess and two accent rails framing it
  (`GDC-L1-UX-0004` — the mouth's shape states that this sprays a sheet).
- A rubber foot ring, so it reads as a *can* lying in the sand next to a gun
  that has a stock.

## The accent, and why it is a metal

`Mat_Metal_Copper_Oxide` is documented for verdigris pipework, and this is the
one place in the library it is used as an item's function colour. The reason is
specific: the item's whole subject is a **frictionless, iridescent film**, and
the palette has no violet, no iridescent and no wet-look colour. Copper oxide is
the only cool teal in the set and the only one that is *metallic*, so it shifts
with the viewing angle the way the film it dispenses is supposed to.

Every alternative was worse for a stated reason: `Mat_Paint_Cell_Green` is the
power colour and sits next to the gauge's own green; `Mat_Paint_Rose_Dusty`
reads as a pastel cottage wall; `Mat_Paint_Lacquer_Vermilion` is the palette's
only glossy paint but red means danger and is already the flamethrower's muzzle
band. **No material was added to the palette.**

## Reuse

From `components/props/sprayer_kit.py` (`sprayer_kit_BUILD.md`): `tank` (on the
Z axis), `rounded_rect`, `trigger_collar`, `nozzle_fan`, `gauge_plate`,
`marker`, and the eight-entry `KIT_MATS` table.

`kit.tank`'s own `bands` are deliberately **not** used. They are spaced evenly
along the vessel, and evenly spaced on a 0.26 m can puts one straight through
the gauge; the two accent hoops are placed by hand above and below the
instrument instead. `SupplyGauge.md`'s rule applies to the model as much as to
the generated bar — the reading must not be crossed by decoration.

## Decomposition

**Nothing is joined.** Eight meshes:

| Object | Origin (blender) | Dimensions (m) | Tris | Why separate |
|---|---|---|---|---|
| `Mesh_SlickCan_Body` | (0, 0, 0) | 0.108 × 0.108 × 0.267 | 1276 | the can and its two teal hoops |
| `Mesh_SlickCan_Foot` | (0, 0, 0) | 0.112 × 0.112 × 0.018 | 384 | rubber base ring |
| `Mesh_SlickCan_Neck` | (0, 0, 0) | 0.074 × 0.074 × 0.068 | 472 | shoulder collar between can and head |
| `Mesh_SlickCan_Trigger` | (0, 0, 0) | 0.093 × 0.113 × 0.057 | 668 | the thumb lever, its collar and its guard |
| `Mesh_SlickCan_Head` | (0, 0, 0) | 0.068 × 0.062 × 0.068 | 368 | the moulded head the fan is set into |
| `Mesh_SlickCan_Fan` | (0, −0.068, 0.340) | 0.082 × 0.045 × 0.039 | 336 | **the fan nozzle** — pivot at the mouth on the slot's centre line, so scaling local X widens the fan from its own lip |
| `Mesh_SlickCan_Cap` | (0, 0, 0) | 0.047 × 0.047 × 0.030 | 188 | pressure cap on the crown |
| `Mesh_SlickCan_Gauge` | (0, 0, 0) | 0.012 × 0.030 × 0.090 | 444 | the kit's supply gauge, running up the flank |

**No armature.** The moving parts the design doc names are the trigger, the
gauge and "a nozzle that visibly widens its fan while spraying". The fan is its
own object with its origin at the mouth, which is the whole capability without
a hierarchy for Unity to unpick. The trigger is a lever inside the collar mesh
and the fill is built by `OxygenGearBuilder` on the Unity side.

## Markers — what the wave-2 assets agent needs

| Marker | Blender | Unity (`−x, z, −y`) | For |
|---|---|---|---|
| `Marker_Muzzle` | (0.000, −0.068, 0.340) | (0.000, 0.340, 0.068) | the fan's origin — the slot mouth, firing along −Y |
| `Marker_Grip` | (0.000, 0.000, 0.150) | (0.000, 0.150, 0.000) | `ItemGrip` — on the can's own axis at the height a fist closes; **not** a grip surface, because there is no grip |
| `Marker_Gauge` | (0.058, 0.000, 0.140) | (−0.058, 0.140, 0.000) | the gauge face, buried in the lit strip |

`holdSize` from the export: the model's longest axis is **0.3950** — the
height, see the scale note above.

`GAUGE_SIDE = +1` (Blender +X, which the export maps onto Unity −X — the flank
a right-handed hold turns toward the camera), the same constant the whole family
uses so all four flip together if the wave-2 seating finds it facing away.

## Things that had to be redone

- **Three `_zverify` clashes round the neck at once.** The can's rolled cap has
  discs at z 0.256 and 0.274 and its dome closes at 0.270, and the first cut put
  the neck's own faces a millimetre from those. The neck now runs 0.250 → 0.318
  — well inside the can at one end and well inside the head at the other — and
  its dark ring sits at 0.286. Nothing is within 4 mm of anything parallel.
- **The fan mouth was a blunt capped wedge.** `kit.nozzle_fan` now sinks a dark
  slot into the mouth face: the one cue that says this sprays a sheet is a mouth
  you can see into.

## Principles cited

`GDC-L1-UX-0003` (readability and hierarchy — the vertical silhouette is what
makes this item findable in a row of icons; the gauge is placed where the hand
does not cover it), `GDC-L1-UX-0004` (affordances, signifiers and convention —
an aerosol can is a shape the player already knows how to read, and the flat
slot states what comes out of it).
