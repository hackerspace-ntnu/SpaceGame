# Wrist Blade — build record

A sheath on the forearm's deck that throws a blade out over the back of the hand and draws it back.
The gauntlet family's fourth combat device, beside the puncher and the repulsor.

`models/gear/gauntlet_blade.blend` → `Assets/Game/Art/Models/Items/gauntlet_blade.fbx`
→ `Assets/Game/Prefabs/Items/Artifacts/Gadgets/WristBlade.prefab` (built by
`Assets/Game/Editor/AssetPipeline/WristBladeBuilder.cs`, fired by `WristBladeArtifact`).

## Reused from the library

| Component | Object | Why it served |
|---|---|---|
| `components/mechanical/retract_blade.blend` | `Mesh_RetractBlade_Straight` | New for this build, but a component rather than device geometry: a blade that rides a sheath is the kind of thing a second device will want (a boot spike, a mount's horn). Seated with its tang's rear face at `BLADE_ROOT`, scale 1. |
| `_gauntlet.py` | `BASE_DECK_Z`, `BASE_WRIST_EDGE`, `append_objects` | The deck contract every device stands on. |

## New components

**`retract_blade.blend`** — three blades on one sheath contract (origin at the tang's rear face,
tip along −Y, `THICK` at the edge and `THICK + 2·SPINE_H` at the spine), in three collections:

| Collection | Needed by | Silhouette |
|---|---|---|
| `Coll_RetractBlade_Straight` | this device | a dagger: parallel tang, long taper to a point |
| `Coll_RetractBlade_Curved` | built ahead | a kukri sweep to a dropped point, 1.6× the width at the belly |
| `Coll_RetractBlade_Serrated` | built ahead | the dagger with nine teeth down one edge |

The two extra blades are the variation the skill asks for; a sheath sized for the straight blade
(`CHAN_HX`, `CHAN_HZ`) takes the serrated one as it is and the curved one only if the channel is
widened to its 0.07 m belly.

## Decomposition

| Object | Moves? | Separate because |
|---|---|---|
| `Mesh_WristBlade_Sheath`   | fixed | floor, walls, roof and rear cap round the channel — one piece of pressed steel |
| `Mesh_WristBlade_Mouth`    | fixed | the chrome frame the blade emerges through; a wear part |
| `Mesh_WristBlade_Actuator` | fixed | the cylinder, rod and lug; a rod that rode the blade would leave its cylinder at 0.26 m of stroke |
| `Mesh_WristBlade_Cover`    | fixed | the suit's orange accent with the arming stripe |
| `Mesh_WristBlade_Lamps`    | fixed | two amber ready lamps, emissive |
| `Mesh_RetractBlade_Straight` | **blade** | the component, sliding along −Y by `STROKE` |

Nothing is merged; every part is its own object. No armature: the one moving part slides on one
axis from an origin already on it, which is a single-bone rig without a hierarchy to unpick.

## Numbers the Unity side consumes

| | Blender | Unity |
|---|---|---|
| `BLADE_ROOT` (the blade's origin) | (0, 0.340, 0.262) | (0, 0.262, −0.340) |
| `STROKE` | 0.550 along −Y | 0.550 along +Z (the prefab's forward) |
| Blade tip at rest / out | y −0.210 / −0.760 | z 0.210 / 0.760 |
| `Marker_Grip` | origin | GripPoint |
| `Marker_Mouth` | (0, −0.216, 0.262) | the spark burst's origin, at the knuckles |

`audit()` asserts the pivot, the envelope at rest and at full stroke, the collar clearance
forward of y 0.090, and that no vertex of the blade touches the channel's walls anywhere along the
travel. It refuses to save otherwise. The one deliberate exception to the family's forward reach
limit (y ≥ −0.240) is the blade at full stroke, allowed out to `BLADE_REACH_Y0` (−0.780): the reach
is the item, and the sheath, mouth and actuator still keep the device limit.

## Materials

All from the palette: `Mat_Metal_Steel_Worn`, `Mat_Metal_Steel_Dark`, `Mat_Metal_Chrome_Scuffed`,
`Mat_Metal_Brass_Tarnished`, `Mat_Paint_Safety_Orange`, `Mat_Paint_Warn_Red`, `Mat_Emissive_Amber`.
Nothing added.

## Tooling note

The library's `.blend` files, `palette.blend` included, are written by Blender 5 and cannot be
opened by the Blender 4.2 that was the only install on this machine; this build ran on a portable
Blender 5.2.1 placed at `%LOCALAPPDATA%\Programs\Blender5\blender-5.2.1-windows-x64\blender.exe`.

Decisions a reader might want made differently: the blade is fully hidden at rest (6 mm inside the
mouth) rather than peeking out, which cost the sheath a 60 mm overhang past the deck's front edge;
and the straight blade ships rather than the curved one, because a stab reads more clearly as a
gauntlet weapon than a sweep whose edge is on one side of the arm.
