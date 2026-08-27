# Jumping Rod — build record

A spring-loaded hopping stick: plant it and the coil throws you back up on every
ground contact. Built 2026-08-25.

The model outlived a design change and is unaffected by it — the rod was first
built as a ridden vehicle and is now a held item that bounces the player's own
body. Nothing about the geometry had to move, which is what the peg/bar
proportions below are still measured against.

| File | |
|---|---|
| `jumping_rod.py` | generator — historical record, never re-run over the `.blend` |
| `jumping_rod.blend` | **source of truth** |
| `jumping_rod_export.py` | re-runnable export |
| `Assets/Game/Art/Models/Items/jumping_rod.fbx` | what Unity imports |

Standing height **1.6502 m**, 6 700 tris, 13 objects, 9 palette materials.
Authored on +Z with the origin **at the ground contact point** and **−Y forward**.

## Reuse

Nothing was reused in the end, and one attempt is worth recording so it is not
retried: `components/mechanical/weapon_grip.blend` → `Mesh_WeaponGrip_Fore` was
appended as the two bar-end grips, and it is the closest thing the library has —
a 0.125 m moulded sleeve. It came out wrong for a reason placement cannot fix.
A foregrip is a *vertical* handle with a wide finger flange down one face; laid
onto a handlebar it reads as a flat plate bolted across the bar rather than as
something a hand closes round. A bar-end grip is a body of revolution and that
component is not one. `Mesh_JumpingRod_Grip_L/R` are built here instead.

No material was added to the palette: the whole model is nine existing entries.

## Decomposition

One model file rather than a component plus an assembly — every piece is
specific to this machine's proportions, and the one genuinely generic part (the
grip) is 580 tris of revolution that no other model has asked for yet. Splitting
it out would put a file in the index that nothing links to.

**Nothing is joined.** Thirteen separate objects, each with its origin at the
point it pivots or slides about:

| Object | Origin (blender) | Why separate |
|---|---|---|
| `Mesh_JumpingRod_Shaft` | (0, 0, 0.410) | the fixed body; the peg bosses and bar clamp are welded to it, so they are part of it |
| `Mesh_JumpingRod_Collar` | (0, 0, 0.430) | the fixed spring anchor at the shaft's foot |
| `Mesh_JumpingRod_Band` | (0, 0, 1.200) | the hazard stripe, recolourable on its own |
| `Mesh_JumpingRod_Gauge` | (0, −0.054, 1.050) | travel dial; its own +Z is the dial normal, so a needle added later turns about the axis it already has |
| `Mesh_JumpingRod_Piston` | (0, 0, 0.720) | **slides** — Unity translates it up the shaft under load |
| `Mesh_JumpingRod_SpringSeat` | (0, 0, 0.105) | rides the piston |
| `Mesh_JumpingRod_Foot` | (0, 0, 0.000) | rides the piston; the part that touches the ground |
| `Mesh_JumpingRod_Spring` | (0, 0, 0.400) | **squashes** — origin at its TOP, the end bolted into the fixed collar, so scaling local Z shortens it downward from a fixed anchor the way the machine does |
| `Mesh_JumpingRod_Peg_L` / `_R` | (∓0.052, 0, 0.520) | footboards, per side, so one can be broken off in a battered variant |
| `Mesh_JumpingRod_Handlebar` | (0, 0, 1.620) | one swept bar across both hands |
| `Mesh_JumpingRod_Grip_L` / `_R` | (∓0.150, 0.010, 1.620) | rubber grips, per side for the same reason as the pegs |

**No armature.** Two things move and both are rigid: the piston slides and the
coil squashes. Both are already separate objects with their origins on the axis
of motion, which is the same capability as a two-bone rig without a hierarchy
for Unity to unpick on import — the call `item_scanner.py` made for its dial and
antenna.

## Proportions, and the one that had to be redone

The stack constants at the top of the generator are all heights above the ground
contact point, so the machine re-proportions from that block alone.

The first pass put the pegs at z 0.400 with the outer tube starting at 0.560 —
**the footboards hung off nothing**, floating in the piston's travel zone below
the tube they are supposed to be welded to. The pegs must be on the *fixed*
shaft or they would travel with every compression. Fixed by dropping the tube to
0.410 and lifting the pegs to 0.520, just clear of the collar.

Rider geometry that falls out of it: feet at 0.520, hands at 1.620 — 1.10 m of
rise, which is a hand's height above a foot on a standing figure. The planted rod
is scaled to 1.45 m under the player rather than stood on, so the pegs read as
footrests rather than carrying weight; the 1.10 m relationship is what keeps the
bar near the hold pose's hands.

## Travel

`TRAVEL = 0.110` m in the generator is the piston stroke. The coil's measured
free length is 0.300 m, so at full squash it goes to 0.190 m — a factor of 0.633,
which `JumpingRodSpring.SolidFraction` derives rather than being told, so
re-proportioning the coil cannot leave its lower end floating off its own seat.

**`JumpingRodSpring.travel` in Unity carries the same 0.110** and is the only
place the Unity side holds it. Change the number here and change it there, or the
coil drives through its own seat at full compression.

The coil is swept by `Part.helix` in `_buildlib.py`, added with this model — the
library had no primitive that follows a curve in three axes, and `loft` cannot be
one because its rings sit perpendicular to a single axis, which turns a helix into
a flat spiral ribbon rather than a round wire.

## Two sizes, one model

The `.fbx` is used by both Unity prefabs. The planted rod takes it at 1.45 m,
scaled under the player; the carried item nests the same asset and `ItemGrip.holdSize`
scales it to 1.25 m in the hand. Measured in Unity at that size, its backpack
footprint is **0.393 × 0.129 m — 5 × 2 cells** on the pack's 0.09 m grid, which
fits the Leaf (8 × 8), the Rack (9 × 9) and, turned, either Wing (4 × 7). It is
classified `Sleeve`, the open holder a long tool hangs head-down in.
