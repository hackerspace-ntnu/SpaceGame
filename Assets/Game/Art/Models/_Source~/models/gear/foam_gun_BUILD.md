# Foam gun — build record

The bell-nozzled one-hander of the sprayer kit. Built 2026-09-07.
Design: [`docs/AI/systems/Artifacts/FoamGun.md`](../../../../../../docs/AI/systems/Artifacts/FoamGun.md).

| File | |
|---|---|
| `foam_gun.py` | generator — historical record, never re-run over the `.blend` |
| `foam_gun.blend` | **source of truth** |
| `foam_gun_export.py` | re-runnable export |
| `Assets/Game/Art/Models/Items/foam_gun.fbx` | what Unity imports |

**0.5042 m** long, 0.174 m across the bell, 6 268 tris, 12 objects (9 meshes +
3 markers), 9 palette materials. +Z up, −Y forward, 1 unit = 1 m.
`_zverify`: **0 clashing pairs**.

## Scale

Bracketed against `models/gear/dragon_bazooka.blend` (1.3685 m in Blender, worn
at `holdSize` 1.25 — the anchor of `ItemScaleLadder.cs`). Authored at 0.504 m:
the one-handed sprayer bracket, a little over a third of the anchor, and the
same length as the cryo sprayer so the two read as issued peers rather than as
one item and its bigger cousin.

## The bell is the identification

0.174 m across on a 0.504 m item — the widest thing in the nine-piece set
relative to its length, and the only flared mouth. It says "this comes out wide
and slow" before the player has pulled the trigger, which is the opposite claim
to the flamethrower's narrow lance and the cryo sprayer's finned barrel
(`GDC-L1-UX-0004`). It is also what makes the generated inventory icon
unmistakable: `BatchIconGenerator` frames each item from its own bounds, so the
silhouette is the whole icon.

## Reuse

From `components/props/sprayer_kit.py` (`sprayer_kit_BUILD.md`): `shell`,
`nozzle_bell`, `iris_vanes`, `tank`, `clamp_band`, `grip_moulded`,
`gauge_plate`, `marker`, and the eight-entry `KIT_MATS` table. One material
beyond it — `Mat_Plastic_Safety_Yellow`, the palette's only bright moulded
plastic and the natural fit for a moulded pistol body. **No material added to
the palette.**

`components/props/gas_bottle.blend` was read as a source for the cartridge and
rejected for size: its three variations are 0.11–0.12 m scene dressing with
their own valve furniture, which is neither the right shape nor the right scale
bolted under a 0.5 m gun. `kit.tank` builds the shape at the size this item
needs.

## Decomposition

**Nothing is joined.** Nine meshes:

| Object | Origin (blender) | Dimensions (m) | Tris | Why separate |
|---|---|---|---|---|
| `Mesh_FoamGun_Body` | (0, 0, 0) | 0.084 × 0.350 × 0.087 | 584 | the moulded shell and its yellow flank plates |
| `Mesh_FoamGun_Bell` | (0, 0, 0) | 0.174 × 0.140 × 0.174 | 1120 | the flared nozzle, hollow, with a rolled chrome lip |
| `Mesh_FoamGun_Iris` | (0, −0.324, 0.020) | 0.154 × 0.032 × 0.162 | 648 | **the shutter** — pivot on the bell axis at the mouth, so the prefab animates a local Y rotation and nothing else |
| `Mesh_FoamGun_Collar` | (0, 0, 0) | 0.078 × 0.058 × 0.078 | 520 | chrome union at the shell's face, plus the dark throat plug |
| `Mesh_FoamGun_Cartridge` | (0, 0, 0) | 0.068 × 0.137 × 0.068 | 868 | the foam cartridge — swappable on its own |
| `Mesh_FoamGun_Saddle` | (0, 0, 0) | 0.079 × 0.095 × 0.080 | 452 | the bridge and band that hold it on |
| `Mesh_FoamGun_Grip` | (0, 0, 0) | 0.053 × 0.132 × 0.132 | 1172 | the kit's moulded grip, trigger and guard |
| `Mesh_FoamGun_Breech` | (0, 0, 0) | 0.056 × 0.036 × 0.056 | 424 | rear boss and filler cap |
| `Mesh_FoamGun_Gauge` | (0, 0, 0) | 0.012 × 0.084 × 0.030 | 444 | the kit's supply gauge, on the cartridge's outboard flank |

**No armature.** Two things move — the trigger and the iris — and the iris is
the only one worth rigging. It is already its own object with its origin on the
axis it turns about, which is the same capability as a one-bone rig without a
hierarchy for Unity to unpick (the call `item_scanner.py` and `jumping_rod.py`
both made).

## Markers — what the wave-2 assets agent needs

| Marker | Blender | Unity (`−x, z, −y`) | For |
|---|---|---|---|
| `Marker_Muzzle` | (0.000, −0.324, 0.020) | (0.000, 0.020, 0.324) | where a dab leaves the bell |
| `Marker_Grip` | (0.000, 0.050, −0.074) | (0.000, −0.074, −0.050) | `ItemGrip` — the palm, inside the grip on its core axis |
| `Marker_Gauge` | (0.040, −0.115, −0.058) | (−0.040, −0.058, 0.115) | the gauge face, buried in the lit strip |

`holdSize` from the export: the model's longest axis is **0.5042**.

`GAUGE_SIDE = +1` (Blender +X, which the export maps onto Unity −X — the flank
a right-handed hold turns toward the camera). This item has no plumbing to keep
off it; the constant exists so the whole family flips together if the wave-2
seating finds the gauge facing away.

## Things that had to be redone

- **A capped loft is a solid cone.** The bell first rendered as a white disc
  filling the mouth, hiding the shutter completely. `kit.nozzle_bell` is now a
  closed loop of rings — outer surface, rim, inner surface, throat annulus —
  which is open *and* two-sided.
- **The vanes poked out through the lip.** A canted plate's far corner is at
  `sqrt((mid + halflen)² + halfwidth²)`, not `mid + halflen`; sized against the
  mouth radius they showed outside the bell. `iris_vanes` now takes an `outer`
  fraction and stops at 0.90 of the mouth.
- **Two rings 2 mm off the shell's front face.** Both collar rings were parked
  just short of the body's front plane, which `_zverify` reports as a real
  clash — a gap that small is a flicker, not a gap. The chrome ring straddles
  the plane and the dark plug sits 34 mm down the bell, 5 mm clear of its inner
  wall.
- **The cartridge is entirely forward of the trigger guard.** Under the receiver
  is where the guard has to be, and geometry through a hand is the failure this
  whole family had to be laid out around.

## Principles cited

`GDC-L1-UX-0004` (affordances and signifiers — the bell's shape states the
verb; the shutter is modelled part-open because a shutter drawn shut is a disc
and one drawn fully open is nothing), `GDC-L1-UX-0003` (readability — the gauge
on the flank the player sees), `GDC-L1-UX-0006` (never encode information in
colour alone — the foam gun is told from the cryo sprayer by silhouette first,
hue second).
