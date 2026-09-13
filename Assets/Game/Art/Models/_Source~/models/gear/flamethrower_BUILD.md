# Flamethrower — build record

The two-handed lance of the sprayer kit. Built 2026-09-07, reworked by hand in
Blender 2026-09-09.
Design: [`docs/AI/systems/Artifacts/Flamethrower.md`](../../../../../../docs/AI/systems/Artifacts/Flamethrower.md).

| File | |
|---|---|
| `flamethrower.py` | generator — historical record, never re-run over the `.blend` |
| `flamethrower.blend` | **source of truth**, hand-edited |
| `flamethrower_markers.py` | re-runnable: puts the `Marker_*` back on the parts they describe |
| `flamethrower_export.py` | re-runnable export |
| `Assets/Game/Art/Models/Items/flamethrower.fbx` | what Unity imports |

**1.2909 m** nose to butt, 12 032 tris, 22 objects (15 meshes + 7 markers),
11 palette materials. +Z up, −Y forward, 1 unit = 1 m.
`_zverify`: **1 clashing pair**, 0.001 m² at 1.99 mm — two faces inside
`Mesh_Flamethrower_Gauge.001`, the hand-duplicated rear instrument.

**Re-export in this order**, or the item lands in Unity wrong with a clean console:

```bash
blender --background models/gear/flamethrower.blend --python models/gear/flamethrower_markers.py
blender --background --python models/gear/flamethrower_export.py
# then, in the Editor:  Tools/SpaceGame/Items/Reseat Flamethrower
```

## Scale

Bracketed, not multiplied. `models/gear/dragon_bazooka.blend` measures
**1.3685 m** along Y in Blender and is worn at `holdSize` **1.25** — the anchor
of `Assets/Game/Editor/Items/ItemScaleLadder.cs`. The 2026-09-07 build was
authored at 0.913 m; the hand rework grew it to 1.2909 m, which changes nothing
in the hand: `holdSize` is a **bracket**, and this item's is pinned at **0.90**,
so `ItemGrip` scales whatever the artist saved down to 0.90 m along its longest
axis. It stays the only long tool in the nine-item set — long enough to need two
hands, short enough not to read as a rocket launcher — while the other three
sprayers are 0.50/0.50/0.40, so **length alone** identifies this one across a
room (`GDC-L1-UX-0003` — rank by salience). Growing the .blend further does not
make it bigger in the hand; only the bracket does.

## Reuse

From `components/props/sprayer_kit.py` (built in the same pass; see
`sprayer_kit_BUILD.md`): `shell`, `tank`, `grip_moulded` ×2, `gauge_plate`,
`hose`, `arc`, `clamp_band`, `marker`, and the eight-entry `KIT_MATS` table.

`components/mechanical/weapon_grip.blend` → `Mesh_WeaponGrip_Fore` was appended
as the support hand first, exactly as `dragon_bazooka.blend` does. It is
**rejected**: its canvas wrap and ply cheeks are a scavenged weapon's language
and read as a part off a different model on a white issued shell. The support
hand was `kit.grip_moulded(..., trigger=False, guard=False)` — the same part as
the firing hand, upright and unguarded (one kit, one grip: `GDC-L1-UX-0004`).

**The hand rework deleted that second grip.** `Mesh_Flamethrower_Foregrip` is
gone; the support hand now takes the shell's underside directly, which is where
`Marker_GripFore` points. Nothing in Unity reads that marker — the two-handed
pose is animation, not a socket — so this is a look, not a wiring change.

No material added to the palette. Three beyond `KIT_MATS`:
`Mat_Paint_Safety_Orange` (fire), `Mat_Paint_Warn_Red` (the muzzle band — red
for danger is a convention worth honouring rather than reinventing) and
`Mat_Emissive_Amber` (the pilot flame).

## Decomposition

One model file, no new component: every proportion here is specific to this
lance, and the parts that were generic went into the kit instead.

**Nothing is joined.** Fifteen meshes, as the hand rework left them — the parts
carry object rotation and non-unit scale now, and four of them are duplicates
the artist added as a second, smaller instrument cluster over the stock:

| Object | Origin (blender) | Dimensions (m) | Tris | Why separate |
|---|---|---|---|---|
| `Mesh_Flamethrower_Shell` | (0, 0, 0) | 0.090 × 0.440 × 0.103 | 584 | the moulded receiver and its orange flank plates |
| `Mesh_Flamethrower_Barrel` | (0, 0.029, 0.000) | 0.041 × 0.569 × 0.041 | 668 | hollow steel lance; the bore is a real hole |
| `Mesh_Flamethrower_Cage` | (0, −0.530, 0.013) | 0.093 × 0.284 × 0.102 | 1120 | the vented heat shroud — six ribs, two rings |
| `Mesh_Flamethrower_Muzzle` | (0.000, −0.708, 0.013) | 0.110 × 0.165 × 0.098 | 1176 | collar, hazard band, hose manifold, igniter arm |
| `Mesh_Flamethrower_Pilot` | (0.040, −0.772, 0.013) | 0.018 × 0.036 × 0.019 | 116 | **the pilot flame** — pivot at its root, so scaling local Y stretches it forward from the igniter |
| `Mesh_Flamethrower_Bottle` | (0.005, 0.058, −0.099) | 0.155 × 0.412 × 0.153 | 816 | the fuel bottle and its valve block |
| `Mesh_Flamethrower_Clamps` | (−0.025, 0.082, −0.086) | 0.175 × 0.274 × 0.200 | 904 | two bands and their yokes up to the barrel |
| `Mesh_Flamethrower_Hose` | (−0.051, −0.327, −0.069) | 0.073 × 0.447 × 0.135 | 920 | bottle valve to muzzle manifold |
| `Mesh_Flamethrower_Grip` | (0, 0.433, 0) | 0.063 × 0.157 × 0.157 | 1172 | firing hand: kit grip, trigger, guard |
| `Mesh_Flamethrower_Stock` | (0, −0.054, −0.006) | 0.046 × 0.367 × 0.102 | 944 | skeleton stock, butt plate, rubber pad, sling loop |
| `Mesh_Flamethrower_Gauge` | (−0.086, 0.079, −0.121) | 0.030 × 0.216 × 0.072 | 444 | the kit's supply gauge — **on the −X flank now**, see below |
| `Mesh_Flamethrower_Bottle.001` | (−0.040, 0.349, 0.000) | 0.086 × 0.228 × 0.085 | 816 | hand-added: a second, smaller bottle lying over the stock |
| `Mesh_Flamethrower_Clamps.001` | (−0.028, 0.362, 0.013) | 0.097 × 0.172 × 0.111 | 904 | its bands |
| `Mesh_Flamethrower_Hose.001` | (−0.037, 0.210, 0.011) | 0.032 × 0.125 × 0.071 | 920 | its feed |
| `Mesh_Flamethrower_Gauge.001` | (−0.037, 0.361, 0.051) | 0.017 × 0.119 × 0.040 | 444 | its instrument — dressing only; Unity reads the main gauge |

**No armature.** One thing on this model moves independently — the pilot flame,
which grows — and it is already its own object with its origin on the axis of
motion. The trigger and the gauge fill are the other two "moving parts" the
design doc names, and neither wants a bone: the trigger is 44 mm of blade
inside the grip and the fill is built by `OxygenGearBuilder` on the Unity side.

## Markers — and why they need a script now

Four-millimetre `Marker_*` meshes, not empties: empties do not survive
`object_types={"MESH"}` and the FBX arrives axis-converted, so a named mesh is
the only handle that crosses reliably (`portal_gun.py` documents the reasoning).
Unity reads the transform and switches the renderer off.

**A hand edit does not move the markers.** The 2026-09-09 rework moved, turned
and rescaled nearly every part, and the markers stayed where the generator left
them: the grip marker ended up inside the fuel bottle and the gauge marker in
the air behind the trigger. Nothing warns about it — the model looks right in
Blender and the item is simply broken in the game. `flamethrower_markers.py`
recomputes each marker from the part it describes, and is the first of the three
re-export steps at the top of this file.

Each marker also takes its part's **rotation and scale**, which is what lets the
Unity side seat the gauge bar in the marker's frame and size it to the
instrument. A marker is a proxy for its part, not a loose point.

| Marker | Blender | Unity (`−x, z, −y`) | Part it is placed from | For |
|---|---|---|---|---|
| `Marker_Muzzle` | (0.000, −0.764, 0.014) | (0.000, 0.014, 0.764) | `Mesh_Flamethrower_Muzzle` | the jet's origin — the bore mouth |
| `Marker_Pilot` | (0.024, −0.801, 0.013) | (−0.024, 0.013, 0.801) | `Mesh_Flamethrower_Pilot` | the idle pilot flame and its light |
| `Marker_Grip` | (0.000, 0.385, −0.107) | (0.000, −0.107, −0.385) | `Mesh_Flamethrower_Grip` | `ItemGrip` — the firing hand's palm |
| `Marker_GripFore` | (0.000, −0.224, −0.071) | (0.000, −0.071, 0.224) | `Mesh_Flamethrower_Shell` | the support hand, for the two-handed pose |
| `Marker_Gauge` | (−0.091, 0.079, −0.123) | (0.091, −0.123, −0.079) | `Mesh_Flamethrower_Gauge` | the gauge face, under the lit strip |

`Marker_Grip.001` and `Marker_GripFore.001` came along with the hand-duplicated
rear cluster and mean nothing. They ship, and the Unity reseat switches every
`Marker_*` off, so they cost 24 triangles and nothing else.

The grip markers sit **inside** the grip on its core axis, not on its surface:
the hand closes around a grip, and a marker on the skin holds the gun a
centimetre clear of the palm (`net_gun.py`).

## Which flank carries what

The generator built with `GAUGE_SIDE = +1` (Blender +X) and `PLUMB_SIDE = −1`:
gauge, igniter arm and pilot flame on one flank, hose, valve block and muzzle
manifold on the other, so neither occludes the other. The export maps Blender +X
onto **Unity −X**, which is the flank a right-handed hold turns toward the
camera.

**The hand rework moved the gauge to the other flank** — it is at Blender −X
now, so it arrives on Unity +X and faces *away* from the player in a
right-handed hold. The bar still draws correctly, it is simply on the far side.
A readout the player cannot see is not one (`GDC-L1-UX-0003`); mirroring the
gauge cluster back across X in Blender and re-running the three steps is the
whole fix, if that was not deliberate.

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
