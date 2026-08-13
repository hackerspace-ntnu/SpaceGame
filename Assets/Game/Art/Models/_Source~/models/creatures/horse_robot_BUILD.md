# horse_robot — build notes

**Source:** `Assets/Game/Art/Models/_Source~/models/creatures/horse_robot.py` → `horse_robot.blend`
**Export:** `Assets/Game/Art/Models/_Source~/models/creatures/horse_robot_export.py` →
`Assets/Game/Art/Models/Creatures/Robotic/Horse/horse_robot.fbx`
**Unity prefab:** Tools ▸ Creatures ▸ Build Horse Robot Prefab
(`Assets/Game/Editor/Creatures/HorseBuilder.cs`) → `Assets/Game/Prefabs/agents/creatures/HorseRobot.prefab`

A rideable quadruped robot horse. 2.44 m at the withers, 3.30 m to the poll, 4.6 m nose to tail,
100 k triangles. Prefab origin sits on the hip plane at 1.781 m; that is also the ride height the
locomotion derives for itself.

## The one thing this model exists to prove

**The forelegs and the hind legs are genuinely different linkages**, because `LegMeasurement` carries
a stride per leg and nothing in the project had exercised that on a real rig. They differ in three
ways at once, and all three are load-bearing:

| | foreleg | hind leg |
| --- | --- | --- |
| component variant | `Coll_WalkerLeg_Long` (straighter) | `Coll_WalkerLeg_Compact` (deeper fold) |
| instance scale | 0.300 | 0.265 |
| cannon (authored here) | 0.62 m | 0.50 m |
| **linkage** | **2.378 m** | **2.041 m** |
| rest hip height | 2.05 m (86% of linkage) | 1.70 m (83%) |
| bend plane yaw | −90° (knee juts forward) | +90° (hock juts rearward) |
| **measured stride** | **1.781 m** | **1.650 m** |

The forelegs are the taller, straighter pair; the hind legs are shorter and fold harder, which is
what makes the hock read as a hock. Measured in Unity, the two pairs come out 0.131 m (7.9%) apart
in `StrideLength` — the check that per-leg measurement has not regressed to an average.

## The rest pose is not decoration

`HorseLocomotion` uses `HipBudgetStride`, so a leg's stride is what its hip pitch can still reach on
the ground after the hip height has been paid for:

    stride = 2 * sqrt(MaxReach^2 - (RestHipHeight * hipHeightFraction)^2) * strideFraction

A leg modelled dead straight spends its whole length holding the body up and has nothing left to
step with. Both pairs are therefore posed with a real bend — `LegPose` runs a two-link IK that puts
the hoof on z = 0 with the cannon vertical, keeping the elbow on the side the art already bends
toward, and it refuses the build outright if the target is past 99.5% of the linkage.

This is the opposite lesson to the crawler's. That machine's stride comes from the coxa's yaw arc,
so its legs must be splayed **outboard**; this one's comes from the hip pitch, so its hooves sit
almost under their own yaw axis (`RestFootRadius` 0.12 m in front, 0.22 m behind) and the bend is
what matters instead.

## Rig conventions, and why each one is there

- `HORSE_Rig`: `Root` → four `Coxa_/Hip_/Knee_/Ankle_/Foot_<FL|FR|HL|HR>` chains, plus `Spine` →
  `Neck_01..05` → `Head` → `Jaw`, plus `Tail_01..03`. Only the leg chains use names `WalkerRig`
  recognises; the neck and tail are outside that vocabulary on purpose, so they are driven by
  `HorseSpineMotion` and never walked on.
- **A yaw joint at the base of every leg** (`Coxa_*`, 0.20 m above the hip, axle vertical). Invariant
  I5: a planar limb cannot hold a planted hoof through a turn.
- **The first pitch joint sits ON the yaw axis** — `Hip_*` is directly below `Coxa_*`, which is what
  makes the IK's two stages independent. Measured off-axis: 0.000 m.
- **A `*Pin*` cylinder at every joint**, 0.23 m long against 0.08 m across, so its longest axis is
  unambiguous. Twenty of them share one mesh datablock.
- **The foot pin is lifted 0.16 m clear of the sole.** `WalkerRig.LowestRendererPoint` takes the
  contact from the lowest renderer under the ankle and skips nothing but `COL_`; a pin centred on
  the contact point hangs through the ground and stands the machine that much high.
- **The hoof is a separate mesh on `Foot_`**, and the cannon is on `Ankle_`. That split is what makes
  `MeasureFootprint` measure the hoof (0.084 m) rather than the whole lower leg — and it is why the
  component's own splayed walking pad is not used at all: at horse scale it is a 1.8 m dinner plate.
- **No leaf bones** (`add_leaf_bones=False`). A `<bone>_end` child arriving ahead of the real pin is
  a silent wrong answer, not an error.
- Collision boxes are added in Unity by `HorseBuilder`, as direct children of each joint.

Verified after export: all four soles at z = 0.000, roll axes exactly horizontal on all four limbs
(`dot(rollAxis, up) = 0.0000`), three pitch segments per leg (two free links, so the analytic IK
path), `BendSign` = −1 consistently.

## Components reused

`mechanical/walker_leg` (Long, Compact) · `mechanical/neck_column` (VertMid, VertSlim, Joint) ·
`mechanical/tail_segment` (Slim, Patched) · `mechanical/shoulder_gear` (Bearing, Toothed).
The barrel, saddle, head, jaw, cannon, hoof and pins are built here with `Part`.

The head is modelled rather than taken from `neck_column`'s head, whose skull is beaked. A horse's
head is the one part of this machine that has to be its own shape or the silhouette reads as
something else.

## Authoring frame

−Y forward, +X starboard, +Z up. Blender's default FBX axis conversion puts −Y on Unity's +Z, and
the gait patterns derive front/rear from `HomeLocal.z` and left/right from `HomeLocal.x` in Unity
space, so authoring on the convention makes the diagonals come out as diagonals with no yaw
correction anywhere.

## A bug worth not repeating

`attach(obj, arm, bone, world)` puts `bone_matrix.inverted() @ world` into `matrix_basis`, so
`world` has to be the object's ACTUAL world matrix. The head and jaw are built in world space and
then pivoted on the poll and the jaw hinge via `Part.finish(origin=...)`, which leaves the object
with a translation of its own — so they need `Matrix.Translation(poll)` and
`Matrix.Translation(hinge)`, not identity. Passing identity threw the offset away and dropped both
under the belly, 0.36 m below the hooves. The rig still measured and walked perfectly, because the
head is on no limb chain; what gave it away was `DropModelOntoHips` taking `soleY` from the LOWEST
renderer and putting the prefab origin 0.36 m too high. **Render the model and look at it** —
nothing in the numbers said anything was wrong.

## Known gaps

- The neck reads slim: the `neck_column` vertebrae are 0.22 m across against a 0.84 m barrel. Making
  it thicker means either a non-uniform scale (which bone parenting through FBX does not survive
  cleanly) or fewer, larger vertebrae at a coarser joint pitch.
- Neck and tail meshes are rigidly bone-parented, so the joints show gaps when the chain bends
  hard. Anything meant to span two moving bones would have to be skinned (`SkinPart`).
- The barrel is a single lofted hull with no separate ribcage or belly plating, and the saddle is a
  plain slab. Both read as blocky next to the legs, which are hand-made art.
- The tail's rest pose trails out behind rather than hanging, so it reads as a rudder.
