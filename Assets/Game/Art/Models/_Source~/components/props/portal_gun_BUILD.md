# Portal gun — build record

One component file, `portal_gun.blend`, holding four variations of a handheld
aperture emitter built on a fire-extinguisher chassis.

| Variation | Silhouette | Ships to |
|---|---|---|
| `Coll_PortalGun_Extinguisher` | Upright bottle, swan-neck handle, horn as the barrel | `Items/portal_gun.fbx` → `Prefabs/Items/Artifacts/Portals/PortalGun.prefab` |
| `Coll_PortalGun_Spent` | The same bottle, dented and drained | `Items/portal_gun_spent.fbx` |
| `Coll_PortalGun_Twin` | Squat horizontal sidearm, two short reservoirs over a pistol grip | not shipped — built ahead |
| `Coll_PortalGun_Sprayer` | Pressure-sprayer tank with a long lance on a hose | not shipped — built ahead |

Export: `portal_gun_export.py`. Only the first two are wired to anything; the
other two exist because the chassis, the material choices and the reservoir
construction were already paid for, and a workshop or armoury scene now has
spares that do not look copy-pasted.

## The brief, and the one substitution

The reference was a photograph of a 5 litre foam extinguisher. Everything
structural about it is kept — chromed bottle, domed base, swan-neck carry handle
over a squeeze lever, yellow safety pin with its pull ring, side clamp bracket,
black discharge horn. The only change is what the bottle holds: two reservoirs
of portal fluid, orange and blue, on the flanks.

That single substitution is what makes it read as a weapon without changing the
silhouette, and it doubles as the charge readout — `PortalGunItem` drives each
column's `_Fill` and spikes its `_Agitation` on the frame that barrel fires.

## Reuse

**Nothing existing was reused, and the alternatives were checked.** The nearest
things in the library are the three carried devices in `item_devices_BUILD.md`
(0.15–0.27 m gadgets) and `walking_staff`. None contains a pressure vessel, a
valve head or a horn, and the extinguisher's whole read comes from exactly those
parts. What *was* reused is the convention: layout follows `item_devices_*`, the
origin sits at the base so the gun stands on a surface, and the material list
leads with a structural metal for the reason under "Bevel discipline".

Three palette entries were added, each confirmed by `palette.py check` to have
nothing within its duplicate threshold:

| Material | Hex | Why nothing existing served |
|---|---|---|
| `Mat_Emissive_Portal_Blue` | `#2FB8FF` | The palette had **no emissive blue at all**. `Mat_Emissive_Green_CRT` is a readout, `Mat_Glass_Canopy_Tinted` is glazing — neither is a light source. |
| `Mat_Emissive_Portal_Orange` | `#FF8A1E` | `Mat_Emissive_Amber` (#FFB347) is a warm indicator lamp and too pale to hold its own beside Portal_Blue. The two portal colours have to read as a matched pair *against each other*. |
| `Mat_Plastic_Safety_Yellow` | `#F2B01E` | `Mat_Paint_Safety_Orange` is enamel sprayed on steel; the pin and ring are moulded plastic, and the reference's yellow is a distinctly different hue. |

## Decomposition

One file rather than several, because all four variations are the same product
line and share five helpers: `sweep` (a parallel-transport swept tube), `bottle`
(a lofted pressure-vessel shell), `sight_tube` (a reservoir), `gauge` and `horn`.
Sharing those is what makes the four read as one manufacturer rather than four
unrelated props.

The hero and the spent bottle are the *same function* with `drained` and `dent`
flags, not two bodies of code. Rebuilding a whole chassis a second time to change
a fill level and add three dents is two files' worth of drift waiting to happen.

## The reservoir was built twice

The first version was the obvious one: a hollow glass tube (`p.tube` with
`Mat_Glass_Canopy_Tinted`) containing a fluid cylinder. It rendered as a **grey
pipe** — a glass material is opaque to any renderer not doing refraction, which
includes the EEVEE previews and Unity's opaque queue, so the fluid inside was
never visible. Fatal here, because the fluid is the only thing separating this
model from a piece of safety equipment.

The fix inverts it: the fluid column is the exposed surface, with three thin
chrome guard rods around it standing in for the tube, plus chrome collars and a
brass fitting at each end. That is also a real level-gauge design, and unlike the
glass it survives being a 256 px inventory icon. The same treatment was applied
to the twin's two horizontal reservoirs and to the sprayer's window.

`Mat_Glass_Canopy_Tinted` stayed in the material list, moved to the one place it
belongs on this model — a flat cover over the lit face of the pressure gauge,
where an opaque fallback still reads as a gauge crystal.

The two reservoirs were also splayed from ±36° to ±52° around the front, so both
are visible in profile rather than only head-on.

## Markers

The hero collection carries two 4 mm cubes, `Marker_Muzzle` and `Marker_Grip`.
They exist only to carry a coordinate across the FBX: Blender empties are not
exported (`object_types={"MESH"}`), and deriving the muzzle on the Unity side
means composing the FBX's −90° X root rotation with a scale-100 root and hoping.
`PortalContentBuilder` reads their transforms, creates `Muzzle` and `GripPoint`
on the prefab **root**, and disables the marker renderers.

Verified after import: `Marker_Muzzle` at Blender `(0, −0.151, 0.348)` arrives at
prefab-local `(0, 0.348, 0.151)`. So the prefab root's **+Y is the bottle's up and
+Z is the way the horn points**, which is what `ItemGrip`'s pose convention and
`PortalProjectile`'s launch direction both assume.

Hidden rather than deleted, for two reasons: a child of a prefab instance cannot
be removed without unpacking it, and keeping the link is what lets a re-export
from Blender reach the prefab. A disabled renderer is also skipped by
`EquipItemSocket.MeasureLocalBounds`, so a 4 mm cube cannot quietly influence how
large the gun is held.

## Bevel discipline

`BEVEL_W = 0.0018`, applied only to an accumulated `hard` list of boxy faces —
never a whole-part `p.bevel()`. At this scale a wider bevel exceeds half the
radius of the swept handle tubing and `finish()`'s `remove_doubles` welds the
over-bevelled ends into a blob. `Mat_Metal_Steel_Worn` is deliberately material
index 0, because `bmesh.ops.bevel` stamps every face it creates with index 0.
Both traps are recorded in `item_devices_BUILD.md`; neither was rediscovered.

`_buildlib` has no `sweep`, despite older component scripts calling `p.sweep` —
whatever provided it has since been removed. This file carries its own, using
parallel-transport frames so the carry handle's 180° turn does not pinch or twist
where it leaves the vertical plane.

## Counts

| Object | Triangles |
|---|---|
| `Mesh_PortalGun_Extinguisher` | 4 012 |
| `Mesh_PortalGun_Spent` | 3 844 |
| `Mesh_PortalGun_Twin` | 2 292 |
| `Mesh_PortalGun_Sprayer` | 2 210 |

Hero dimensions 0.14 × 0.21 × 0.43 m, held at `ItemGrip.holdSize` 0.42.

## Unity side

`Assets/Game/Editor/Portals/PortalContentBuilder.cs` builds every material,
prefab and item asset from this FBX and is re-runnable. The two fluid slots are
pointed at `SpaceGame/Portal/PortalFluid` materials through the importer's
**remap table**, not a prefab override, so a re-export from Blender does not lose
them.

The fluid shader must be told where the reservoir starts and ends along its own
axis (`_FillMin` / `_FillMax`): the columns run z 0.092 → 0.240 in mesh space, and
the origin-at-base convention means the usual −0.5…0.5 assumption would put the
entire column inside the filled half and the level would never move.
