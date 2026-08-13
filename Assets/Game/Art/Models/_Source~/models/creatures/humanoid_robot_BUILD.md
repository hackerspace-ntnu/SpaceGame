# humanoid_robot — build record

**File:** `Assets/Game/Art/Models/_Source~/models/creatures/humanoid_robot.blend`
**Generator:** `humanoid_robot.py` · **Export:** `humanoid_robot_export.py`
**Unity:** `Assets/Game/Art/Models/Creatures/Robotic/Humanoid/humanoid_robot.fbx`
→ `Assets/Game/Prefabs/agents/creatures/HumanoidRobot.prefab`

An upright two-legged, two-armed robot, ~1.90 m to the top of its head. Built for
`LeggedLocomotion`'s arm seam and for the case the two shipping machines do not cover: a
**forward-bending knee**.

## Reused from the library

| Component | Variation | Used as |
| --- | --- | --- |
| `components/mechanical/walker_leg.blend` | `Coll_WalkerLeg_Heavy` | thigh + shin, both legs |
| | `Coll_WalkerLeg_Compact` | upper arm + forearm, both arms |
| `components/mechanical/shoulder_gear.blend` | `Coll_ShoulderGear_Bearing` | hip yaw bearing |
| | `Coll_ShoulderGear_Spoked` | shoulder yaw bearing |
| `components/mechanical/neck_column.blend` | `Coll_NeckColumn_VertSlim` | two neck vertebrae |
| | `Coll_NeckColumn_Joint` | neck collars |

`Heavy` was chosen for the legs because it has the squarest thigh-to-shin ratio of the four
linkages (3.87 : 3.49), which is what makes it read as a human leg rather than a bird's. `Compact`
is the shortest and is scaled down again for the arms, so the elbow keeps a visible break at rest
and the IK has somewhere to go.

**No new component file was created**, and nothing pre-existing was modified. Every material comes
from the shared palette; none was added.

## Unique geometry, and why each is not a component

| Object | Why it is not reusable |
| --- | --- |
| `Mesh_Humanoid_Pelvis` | Its hip bearing housings sit on this machine's own coxa axis. |
| `Mesh_Humanoid_Chest` | Its shoulder yokes reach out to this machine's own arm yaw axis. |
| `Mesh_Humanoid_Head` | A visored sensor head; `Coll_NeckColumn_Head` is a beaked bird's. |
| `Mesh_Leg_*_Foot` | See below — the one measurement the component could not supply. |
| `Mesh_Arm_*_Hand` | A three-finger gripper. The component's foot pad is not a hand. |

If a second humanoid is ever needed, the pelvis / chest / head are the pieces to promote into a
component file with variations. They are single-use today and promoting them now would be a
component with one variation, which is worse than none.

## The foot is modelled here, and that is load-bearing

The walker-leg component's own foot is a splayed pad whose **height and width are locked together
by a uniform scale**. A foot long enough to stand on (0.30 m) scaled from it puts the ankle 0.17 m
off the ground — and every centimetre of ankle height is a centimetre the knee has to give back:

```
mid-stance knee break = 2 * acos((hipHeight - ankleHeight) / (thigh + shin))
```

With the component's foot the machine stood in a permanent 73° squat. A purpose-built plantigrade
sole with a 0.07 m ankle brings that to 65° at the shipped tuning. The toe and the heel are given
**equal reach** on purpose: `WalkerRig` takes the contact from the lowest point of the meshes under
the last pitch joint and reports its bounds *centre* horizontally, so an asymmetric footprint would
move the measured contact off the ankle's axis and every stride would be aimed at a point the leg
is not standing on.

## Rig

```
Root (pelvis)
├── Chest
│   ├── Arm_L → Shoulder_L → Elbow_L → Wrist_L        (hand on the wrist)
│   ├── Arm_R → Shoulder_R → Elbow_R → Wrist_R
│   └── Neck_01 → Neck_02 → Head
├── Coxa_L → Hip_L → Knee_L → Ankle_L → Foot_L        (sole on Foot_, the roll joint)
└── Coxa_R → Hip_R → Knee_R → Ankle_R → Foot_R
```

Every joint carries a `*Pin*` cylinder child whose longest axis is its hinge axis — that is how
`WalkerRig.MeasureAxle` recovers it, and a bone without one is not followed at all when the arms are
discovered. Exported with `add_leaf_bones=False`.

**The legs use the classic names** and are assembled by name across the armature. **The arms are
not**, and must not be: `Coxa_/Hip_/Knee_/Ankle_/Foot_` is looked up by *id*, so an arm sharing the
id `L` with a leg would be built out of the leg's bones. An `Arm_` root is discovered by walking the
hierarchy instead, which is what leaves its joints free to be called what they are.

**The shoulder's base axle is vertical, exactly like a coxa's.** `WalkerRig.Measure` signs the base
axle with `MatchSense(axle, body.up)`, which is arbitrary for an axle lying square to up — a
horizontally-axled shoulder can measure either way between one export and the next. A vertical
shoulder yaw is the known-good shape, and invariant I5 wants one anyway.

## How the knee comes out forward

The component is authored with **+X as the knee side**. Which way that reads on the finished machine
is purely which way the limb is yawed about Z:

```
yaw = -90  ->  limb-local +X lands on world -Y = FORWARD  -> a human knee
yaw = +90  ->  limb-local +X lands on world +Y = REARWARD -> a hock
```

Every limb here is at −90. Nothing in the locomotion code selects the sense; `BendSign` is measured
from the rest pose, and it comes out −1 here against the ostrich's +1.

## The rest pose is crouched, and it is not a mistake

`HipBudgetStride` sizes a stride from what is left of the leg once the hip height is paid for:

```
stride = 2 * sqrt(MaxReach^2 - (RestHipHeight * hipHeightFraction)^2) * 0.72
```

with a **floor** under the square root. On this rig (`MaxReach` 0.8785 m) that floor is reached at a
working hip height of 0.800 m — `hipHeightFraction` 0.930 — and above it the stride stops being
geometry and stops responding to the number at all. So the usable range is bounded by the rig, not
by taste. The shipped 0.90 sits inside it with margin and gives a 0.599 m stride, which is 0.70× the
hip height: a natural step for something this size.

The build prints that sweep, with the floor marked, at the end of every run.

## Measured at build

```
L/R leg  linkage 0.9057  maxReach 0.8785  hip 0.8600 (95.0% of linkage)  restRadius 0.0300
L/R arm  linkage 0.6700  maxReach 0.6499  drop 0.6400 (95.5% of linkage)  restRadius 0.0200
worst sole-to-ground error 0.000000 m
stance width 0.320 m · hand rest height 0.830 m · head top 1.900 m
72 656 tris
```

## Decisions a reader might want made differently

- **The arms are chunky.** They are the walker-leg component scaled down, which is honest reuse but
  reads as pauldrons rather than as arms at this size. A dedicated `robot_arm` component with two or
  three variations is the right fix and is deliberately not done here.
- **No `COL_` collision boxes.** Neither the horse nor this machine has them; the crawler does.
  `WalkerRig` skips `COL_*` everywhere it matters, so adding them later is additive and safe.
- **One variation.** This is a single named machine, not a family, so it is a model rather than a
  component and no variations were built ahead.
