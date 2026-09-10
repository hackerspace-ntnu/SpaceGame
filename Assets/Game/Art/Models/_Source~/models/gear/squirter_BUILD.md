# Squirter — build record

The two-handed foam sprayer. **Hand-built in Blender by the user**, not generated: there is no
`squirter.py` and there never was one. It replaced `foam_gun.blend` as the foam gun's model on
2026-09-08. Governing doc: [`docs/AI/systems/FoamGun.md`](../../../../../../docs/AI/systems/FoamGun.md).

| File | |
|---|---|
| `squirter.blend` | **source of truth** — hand-edited, never overwrite it |
| `squirter_export.py` | re-runnable export; normalises three things in memory |
| `Assets/Game/Art/Models/Items/squirter.fbx` | what Unity imports |

**1.300 m** long as exported, 0.456 m across the hoses, 0.942 m tall, 18 objects, 4 084 tris
pre-modifier, 5 materials. −Y forward, +Z up, 1 unit = 1 m **after the export normalises it**.

## What the export changes, and why it has to

The file was built freely rather than against the library's conventions, so three things are fixed
in memory, on a copy. None of them touches the .blend.

| As built | Exported | Why |
|---|---|---|
| three `BézierCurve*` hoses with a bevel depth | meshes off the evaluated depsgraph | `_exportlib.export` ships `object_types={"MESH"}`; a curve is dropped with no warning and the gun arrives with bare fittings |
| bell at **+Y** | turned 180° about Z, bell at −Y | the library faces −Y, which the export's axis conversion lands on Unity's +Z; un-rotated the gun points backwards out of the hands |
| 11.034 units long, origin out in space | scaled to 1.300 m, origin on the barrel axis at mid-length | the prefab's own markers are authored in these units, so a model at true size is the difference between markers that read as metres and markers that read as nothing |

A 180° turn is a rotation, not a mirror, so nothing ends up inside out and `fix_inverted` is not
needed.

## Scale

Bracketed against `models/gear/dragon_bazooka.blend` (1.3685 m in Blender, worn at `holdSize` 1.25 —
the anchor of `ItemScaleLadder.cs`) and `models/gear/flamethrower.blend` (0.9130 m, worn at 0.9).
The squirter is authored just under the anchor and worn at **1.05**: visibly the bigger of the two
sprayers and visibly not a shoulder-fired launcher.

## Markers — printed by the export, not modelled

The hand-built file carries no `Marker_*` meshes to adopt, so the export measures them off the
geometry and prints them. `FoamGunModelBuilder` carries the numbers; re-export and copy them across.

| Marker | Measured from | Unity (`−x, z, −y`) | For |
|---|---|---|---|
| `Muzzle` | the barrel's front face, on its own axis | (0.000, 0.000, 0.650) | where a dab leaves the bell |
| `Grip` | inside the rear handle, 60 % up it | (0.000, −0.142, −0.261) | `ItemGrip` — the firing hand's palm |
| `Foregrip` | the stub handle under the barrel | (0.004, −0.130, 0.355) | where the support hand reads as landing; nothing wires it |
| `Gauge` | the tank's outboard flank | (−0.139, −0.330, 0.030) | the `SupplyGauge` bar, seated 5 mm proud of the curve |

The grip marker sits **inside** the handle on its core axis, not on its surface: the hand closes
around a grip, and a marker on the skin holds the gun a hand's thickness away from itself.

## Decomposition

Nothing is joined and nothing is renamed — the objects keep Blender's default names
(`Cylinder.003` is the barrel, `Cube.001` the rear handle, `Cylinder.001` the foregrip, `Cylinder`
the tank). Renaming them in the export was considered and rejected: the names would then disagree
with what the user sees when they open the .blend to edit it again, and nothing in Unity looks any
of them up by name.

**No armature**, and unlike the kit-built foam gun, **no moving parts**: there is no iris, so
`FoamGunNozzle.iris` is left empty and the shutter animation simply does not run.

## Materials

Five, authored in the file rather than linked from `palette.blend`: one dark metal on most of the
body, a near-black rubber on the hoses, trigger and rear canister, and a teal metal on the tank.
They import embedded (`materialLocation: 1` on the .meta, copied from `foam_gun.fbx.meta` so the
import settings are identical), so their generic names cannot collide with the
`Material.00X.mat` files an older export left in `Assets/Game/Art/Models/Items/Materials/`.
