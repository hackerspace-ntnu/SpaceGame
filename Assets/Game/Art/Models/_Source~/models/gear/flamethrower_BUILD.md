# Flamethrower — build record

The two-handed lance of the sprayer kit. Built 2026-09-07.
Design: [`docs/AI/systems/Artifacts/Flamethrower.md`](../../../../../../docs/AI/systems/Artifacts/Flamethrower.md).

| File | |
|---|---|
| `flamethrower.py` | generator — historical record, never re-run over the `.blend` |
| `flamethrower.blend` | **source of truth** |
| `flamethrower_export.py` | re-runnable export |
| `Assets/Game/Art/Models/Items/flamethrower.fbx` | what Unity imports |

**0.9130 m** nose to butt, 9 712 tris, 17 objects (12 meshes + 5 markers),
11 palette materials. +Z up, −Y forward, 1 unit = 1 m.
`_zverify`: **0 clashing pairs**.

## Scale

Bracketed, not multiplied. `models/gear/dragon_bazooka.blend` measures
**1.3685 m** along Y in Blender and is worn at `holdSize` **1.25** — the anchor
of `Assets/Game/Editor/Items/ItemScaleLadder.cs`. This is authored at 0.913 m,
about two thirds of the anchor and the only long tool in the nine-item set:
long enough to need two hands, short enough not to read as a rocket launcher.
The other three sprayers are 0.50/0.50/0.40, so **length alone** identifies this
one across a room, which is what the design doc means by "this one is serious"
(`GDC-L1-UX-0003` — rank by salience).

## Reuse

From `components/props/sprayer_kit.py` (built in the same pass; see
`sprayer_kit_BUILD.md`): `shell`, `tank`, `grip_moulded` ×2, `gauge_plate`,
`hose`, `arc`, `clamp_band`, `marker`, and the eight-entry `KIT_MATS` table.

`components/mechanical/weapon_grip.blend` → `Mesh_WeaponGrip_Fore` was appended
as the support hand first, exactly as `dragon_bazooka.blend` does. It is
**rejected**: its canvas wrap and ply cheeks are a scavenged weapon's language
and read as a part off a different model on a white issued shell. The support
hand is `kit.grip_moulded(..., trigger=False, guard=False)` — the same part as
the firing hand, upright and unguarded, which is the stronger statement anyway
(one kit, one grip: `GDC-L1-UX-0004`).

No material added to the palette. Three beyond `KIT_MATS`:
`Mat_Paint_Safety_Orange` (fire), `Mat_Paint_Warn_Red` (the muzzle band — red
for danger is a convention worth honouring rather than reinventing) and
`Mat_Emissive_Amber` (the pilot flame).

## Decomposition

One model file, no new component: every proportion here is specific to this
lance, and the parts that were generic went into the kit instead.

**Nothing is joined.** Twelve meshes:

| Object | Origin (blender) | Dimensions (m) | Tris | Why separate |
|---|---|---|---|---|
| `Mesh_Flamethrower_Shell` | (0, 0, 0) | 0.090 × 0.440 × 0.103 | 584 | the moulded receiver and its orange flank plates |
| `Mesh_Flamethrower_Barrel` | (0, 0, 0) | 0.038 × 0.380 × 0.038 | 668 | hollow steel lance; the bore is a real hole |
| `Mesh_Flamethrower_Cage` | (0, 0, 0) | 0.066 × 0.146 × 0.072 | 1120 | the vented heat shroud — six ribs, two rings |
| `Mesh_Flamethrower_Muzzle` | (0, 0, 0) | 0.076 × 0.083 × 0.068 | 1176 | collar, hazard band, hose manifold, igniter arm |
| `Mesh_Flamethrower_Pilot` | (0.024, −0.517, 0.012) | 0.012 × 0.018 × 0.013 | 116 | **the pilot flame** — pivot at its root, so scaling local Y stretches it forward from the igniter |
| `Mesh_Flamethrower_Bottle` | (0, 0, 0) | 0.077 × 0.205 × 0.076 | 816 | the fuel bottle and its valve block |
| `Mesh_Flamethrower_Clamps` | (0, 0, 0) | 0.087 × 0.136 × 0.099 | 904 | two bands and their yokes up to the barrel |
| `Mesh_Flamethrower_Hose` | (0, 0, 0) | 0.044 × 0.210 × 0.078 | 920 | bottle valve to muzzle manifold |
| `Mesh_Flamethrower_Grip` | (0, 0, 0) | 0.053 × 0.132 × 0.132 | 1172 | firing hand: kit grip, trigger, guard |
| `Mesh_Flamethrower_Foregrip` | (0, 0, 0) | 0.053 × 0.066 × 0.096 | 788 | support hand: same kit grip, no trigger, no guard |
| `Mesh_Flamethrower_Stock` | (0, 0, 0) | 0.050 × 0.257 × 0.111 | 944 | skeleton stock, butt plate, rubber pad, sling loop |
| `Mesh_Flamethrower_Gauge` | (0, 0, 0) | 0.013 × 0.090 × 0.030 | 444 | the kit's supply gauge |

**No armature.** One thing on this model moves independently — the pilot flame,
which grows — and it is already its own object with its origin on the axis of
motion. The trigger and the gauge fill are the other two "moving parts" the
design doc names, and neither wants a bone: the trigger is 44 mm of blade
inside the grip and the fill is built by `OxygenGearBuilder` on the Unity side.

## Markers — what the wave-2 assets agent needs

Four-millimetre `Marker_*` meshes, not empties: empties do not survive
`object_types={"MESH"}` and the FBX arrives axis-converted, so a named mesh is
the only handle that crosses reliably (`portal_gun.py` documents the reasoning).
The prefab builder reads the transform and strips the renderer.

| Marker | Blender | Unity (`−x, z, −y`) | For |
|---|---|---|---|
| `Marker_Muzzle` | (0.000, −0.520, 0.012) | (0.000, 0.012, 0.520) | the jet's origin — the bore mouth |
| `Marker_Pilot` | (0.024, −0.534, 0.012) | (−0.024, 0.012, 0.534) | the idle pilot flame and its light |
| `Marker_Grip` | (0.000, −0.040, −0.090) | (0.000, −0.090, 0.040) | `ItemGrip` — the firing hand's palm |
| `Marker_GripFore` | (0.000, −0.224, −0.071) | (0.000, −0.071, 0.224) | the support hand, for the two-handed pose |
| `Marker_Gauge` | (0.044, −0.360, −0.062) | (−0.044, −0.062, 0.360) | the gauge face, buried in the lit strip |

The grip markers sit **inside** the grip on its core axis, not on its surface:
the hand closes around a grip, and a marker on the skin holds the gun a
centimetre clear of the palm (`net_gun.py`).

`holdSize` from the export: the model's longest axis is **0.9130**.

## Which flank carries what

`GAUGE_SIDE = +1` (Blender +X) and `PLUMB_SIDE = −1`. The gauge, the igniter arm
and the pilot flame are on one flank; the hose, the bottle's valve block and the
muzzle manifold on the other, so neither occludes the other. The export maps
Blender +X onto **Unity −X**, which is the flank a right-handed hold turns
toward the camera. If the wave-2 seating finds it facing away, flipping those
two constants is the whole change — a readout the player cannot see is not one
(`GDC-L1-UX-0003`).

## Things that had to be redone

- **The bottle is forward of both hands, not under the receiver.** A bottle
  under the shell is where the firing hand has to be, and the first cut ran the
  grip straight through the tank. Moving it forward also matches the design
  doc's "clamped under the barrel" exactly, so the constraint and the design
  agree.
- **The barrel is a tube, not a rod.** A capped cylinder's front disc and the
  muzzle collar's front disc land on the same plane at y = −0.520. Both rings at
  the muzzle are tubes now, standing 3 mm clear of the barrel's outside —
  `_zverify`'s coplanarity tolerance is 2 mm and two concentric cylinders a hair
  apart count as parallel faces.
- **The comb rises over the shell's roof.** Level with it, the two top surfaces
  sat 0.7 mm apart over 66 mm of overlap. A comb that steps up is also what a
  shoulder stock actually does.
- **The grips are seated against the shell's TAPERED underside.** The shell's
  front station is 0.86 of the back one, so its bottom edge rises from −0.040 at
  the breech to −0.033 at the muzzle end. Seating a grip on the flat `SHELL_Z0`
  number floated it 2 mm clear of the gun at the front.
- **The heat shroud is a cage.** A perforated sleeve needs booleans through a
  tube; six longitudinal ribs between two rings reads as vented from any angle,
  silhouettes better, and costs a fifth of the triangles.

## Principles cited

`GDC-L1-UX-0003` (readability and hierarchy — length as the identifying
salience; a gauge on the visible flank), `GDC-L1-UX-0004` (affordances — a
firearm shape for a firearm hold pose, red for the dangerous end, one grip
across the kit), `GDC-L1-UX-0005` (controls for the hand — grip dimensions are
the hand's, not the item's).
