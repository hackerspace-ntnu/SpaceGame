# Vrescal — build record

A heavy armoured sand-crocodile that crawls. The head, jaw, eyes, neck spikes,
the overlapping dorsal armour stack and the tail were **hand-sculpted** by the
author; this record covers the legs, hips and shoulders that were added around
them, and the rig and animation built on top.

`vrescal.blend` is the source of truth. It was **untracked in git** when this
work started, so the only copies of the pre-existing sculpt are:

    Assets/Game/Art/Models/_backups~/vrescal_before_legs.blend   before any edit
    Assets/Game/Art/Models/_backups~/vrescal_before_anim.blend   legs, no rig binding

## The problem the legs had to solve

Measured off the sculpt before anything was touched:

| | |
|---|---|
| Length, snout to tail tip | 19.94 working units |
| Widest / tallest | 6.19 × 5.23, both peaking at x ≈ −6 |
| Belly line | z = −1.92 |
| Existing front limbs | two rough blobs per side, bottoming out at z = −1.72 |
| Existing rear limbs | none |

Four things were wrong, and they are the reason the build is shaped as it is:

1. **The legs did not reach the ground.** The rough front limbs bottomed out
   *above* the belly, so the animal read as lying down rather than standing.
2. **One mass centre, at mid-length.** Width and height both peaked around
   x = −6 and the body then tapered monotonically into the tail, so there was
   nothing at the hips for a rear limb to attach to. This is what the `haunch`
   component exists for.
3. **The body drifted off the centreline** by up to 0.62, worst at the shoulder
   hump, while the head sat on y = 0.
4. **Every object carried negative scale on all three axes** — a negative
   determinant, so the normals were inside out.

## Decisions the author should know about

- **Scale.** The sculpt is worked at 19.94 units and ships at 5.5 m. Nothing
  rescales the .blend; `vrescal_export.py` applies the 0.2759 factor on the way
  out, so the file stays exactly the size it has been modelled at.
- **Hostile.** The prefab is wired hostile to the player through a new
  `WildlifeFaction`. It is a predator, so that was the obvious default, but it
  is one line in `VrescalBuilder.EnsureWildlifeFaction` to change.
- **Renaming.** The sculpted objects were all `Icosphere.0xx`; they now have
  descriptive names (`Mesh_Vrescal_Plate_07`, `Mesh_Vrescal_Skull`, …). Nothing
  referenced the old names — the file had no generator script and no git
  history.
- **Materials.** The 23 ad-hoc materials held exactly two colours between them,
  `#E7B345` and `#987340`. Both were added to the palette as `Mat_Hide_*` and
  the slots remapped onto them. Visually identical; it is what lets the export
  localise the palette in one pass.

## Components

`components/organic/` was empty before this — every leg in the library
(`walker_leg`, `leg_shroud`, `support_leg`) is hard mechanical and none of it
suits a creature. These are the library's first organic parts, and they are
built to serve any sprawling quadruped, not just this one.

| Component | Variations | Used here |
|---|---|---|
| `limb_segment.blend` | `BrachialHeavy`, `BrachialSlim`, `AntebrachialPlated`, `FemoralHeavy`, `CruralRibbed`, `Stub` | 4 of 6 |
| `foot_splayed.blend` | `Manus4`, `Pes5`, `Spade`, `Fringed` | 2 of 4 |
| `claw_talon.blend` | `Digging`, `Hooked`, `Blunt`, `Dewclaw` | 1 of 4 |
| `haunch.blend` | `HipHeavy`, `ShoulderBroad`, `HipLean`, `ShoulderPlated` | 2 of 4 |

Built ahead, not needed by this model: `BrachialSlim`, `Stub`, `Spade`,
`Fringed`, `Hooked`, `Blunt`, `Dewclaw`, `HipLean`, `ShoulderPlated` — a
lighter or juvenile Vrescal, a burrowing variant, and a sandfish-style
fringe-toed animal are all assemblies away rather than models away.

Conventions for the whole family are documented at the top of
`components/organic/_organic.py`: limb segments run along +X from the proximal
joint with dorsal +Z and are y-symmetric so one mesh serves both sides; feet
have the ankle at the origin and toes on +X; claws grow from the origin along
+X; haunches are built for the port side with the socket at the origin.

Three non-obvious things learned building them, all recorded in the scripts:

- A crossways cylinder is the obvious way to make a joint condyle and it is
  wrong — it caps flat and the limb reads as a machined dumbbell. Lofting the
  bulge into the profile and closing on a collapsed ring is what makes it read
  as muscle.
- An anatomically correct ilium — broad at the spine, narrow at the socket —
  puts the entire hip mass *inside* the body where nothing sees it, and drops
  it below the belly at the centreline so the animal reads as sagging. The
  haunch runs fat-to-thin outboard-to-inboard instead.
- Loft caps matter. Terminating on a small ring with `cap=False` leaves a hole
  a third the width of the haunch, and on the shoulder — where the limb leaves
  at a downward angle and covers nothing directly outboard — it catches no
  light and reads as an open socket.

## Geometry

Ground plane at z = −3.35, about 1.43 units (0.39 m shipped) under the belly.
Joint positions, in the sculpt's working units:

| | shoulder / hip | elbow / knee | wrist / ankle |
|---|---|---|---|
| Front | (−3.40, ±2.60, −0.60) | (−3.80, ±4.30, −1.80) | (−3.25, ±5.05, −3.04) |
| Rear | (−8.80, ±2.20, −0.62) | (−9.30, ±4.00, −1.92) | (−8.55, ±4.85, −3.04) |

The shoulder is where the author's own rough limbs sprouted. The hip is one
crocodilian trunk length behind it — 26 % of body length — which lands at
x = −8.8, where the armour stack starts tapering into tail. The lateral figures
are set by the **haunches**, not the legs: a haunch is only visible where it
reaches past the flank, so the sockets sit a little outboard of the body wall
(2.61 at the shoulder, 1.97 at the hip) rather than inside it.

Front track ±5.05, rear ±4.85, against a 3.1 body half-width — a 1.6× sprawl,
and narrower than the ±5.33 the author's rough limbs already reached.

## Rig and animation

24 bones: root, three spine, neck, head, jaw, five tail, and Upper/Lower/Foot
per limb. Everything is **rigid bone-parented**, not skinned — the armour is a
stack of separate hard plates and skinning would smear the overlaps.

Six actions, at 30 fps:

| Action | Frames | Loops | |
|---|---|---|---|
| `Vrescal_Idle` | 96 | yes | breathing, tail sway, one weight shift, all on different periods |
| `Vrescal_Walk` | 40 | yes | lateral-sequence crawl, duty 0.65 |
| `Vrescal_Run` | 26 | yes | diagonal couplets, duty 0.45 |
| `Vrescal_Attack` | 34 | no | coil, lunge, jaw snaps shut at full reach |
| `Vrescal_Hurt` | 18 | no | flinch and drop |
| `Vrescal_Death` | 56 | no | legs splay, body settles, holds the corpse pose |

The gait rests on two things: the limb swings through a near-horizontal arc
about a vertical axis rather than fore-aft under the body, and the trunk
undulates side to side with the wave travelling back into the tail. Without the
undulation it reads as a table walking.

All clips are **in place**. `NavMeshAgentMotor` owns movement and a clip that
also walked the creature forward would fight it.

## Known compromise

`Mesh_Vrescal_TailKeel` is a single 6.6-unit sculpted piece spanning most of the
tail, so it can only be parented to one tail bone and cannot bend with the
chain. Tail amplitude is capped low enough that it does not visibly separate
from the plates. Splitting the keel per segment — or skinning the tail — is the
proper fix, and is the author's call because it means cutting hand-modelled
geometry.

## Pipeline

    blender --background --python vrescal_legs.py     # legs, hips, rig, cleanup
    blender --background --python vrescal_anim.py     # bind body, author actions
    blender --background --python vrescal_export.py   # -> Assets/.../vrescal.fbx
    # then in Unity: Tools > Creatures > Build Vrescal Prefab

`vrescal_legs.py` and `vrescal_anim.py` are **one-shot edit scripts**, not
generators. Both refuse to run against a file that is not in the state they were
written for, rather than silently duplicating legs or actions. `vrescal_export.py`
is re-runnable and never writes to the .blend.

Two traps the export hit, both silent:

- A parent empty carrying the placement transform is **dropped** by Blender's
  FBX exporter. The model arrives in Unity unrotated, lying on its side with its
  length along X, and nothing reports an error. The transform goes on
  `Arm_Vrescal` instead, which is the only root object.
- The sculpt's origin is at the *head*. Exported as-is a NavMeshAgent steers the
  creature's nose to the destination and drags five metres of body behind it.
  `PIVOT` moves the origin under the middle of the trunk, on the sole plane.

## Unity side

Generated by `Assets/Game/Editor/Creatures/VrescalBuilder.cs`, re-runnable from
**Tools > Creatures > Build Vrescal Prefab**:

- `Assets/Game/Art/Models/Creatures/Organic/Vrescal/vrescal.fbx` — Generic rig,
  avatar from this model, six clips sliced from the takes. `optimizeGameObjects`
  and `optimizeBones` are **off**: the meshes are bone-parented rather than
  skinned, so the transforms the clips animate are the very ones Unity would
  otherwise strip.
- `Assets/Game/Art/Animations/Creatures/Vrescal.controller` — one 1-D blend tree
  on `SpeedY` (idle 0 / walk 1.6 / run 4.2 m/s), plus Attack, Hurt and Death off
  Any State. The parameter names are `AgentAnimatorDriver`'s verbatim,
  misspellings included.
- `Assets/Game/ScriptableObjects/Factions/Core/WildlifeFaction.asset` and a
  `Wildlife ↔ Player = Hostile` row appended to `GlobalRelationships.asset`.
- `Assets/Game/Prefabs/Agents/Creatures/Vrescal.prefab` — NavMeshAgent +
  `NavMeshAgentMotor` + `AgentController`, perception, chase, close combat,
  wander, health, faction, targeting and `SceneTracked` on Migrate.

`AgentAnimatorDriver`'s two scale factors are set to 1 on the prefab so `SpeedY`
reaches the blend tree as true metres per second — by default it multiplies
velocity by 3× and the tree would sit pinned at Run. The walk threshold (1.6)
and `NavMeshAgentMotor.walkSpeedMultiplier` (1.6 / 4.2) are the same number
twice; change one and the creature moon-walks.

A **box** collider, not the capsule the generic-enemy profile uses: the animal
is 5.5 m long and 1.8 m wide, and a capsule around it would either miss the tail
or swallow half the dune.

### Two traps on the Unity side, both silent, both cost a debugging round

**The prefab root must be a fresh GameObject with the model as a child.** Blender
FBXs import here carrying a compensating root transform — rotation (270, 90, 0)
and scale 27.59, being the 0.2759 export factor times Unity's 100 file-unit
scale — with the mesh data scaled down to match. `horse_robot.fbx` has the same
at scale 100. The creature therefore *looks* the right size while every value
set in metres is multiplied by 27.59: the first build shipped a **45 × 40 × 119
metre** BoxCollider and a 26 m-radius NavMeshAgent. Nothing warns. It simply
cannot be placed, and `isOnNavMesh` is false wherever you put it.
`CrabWalkerBuilder`, `HorseBuilder` and `PatrolRobot` all wrap; so does this.

**The Animator has to be on that clean root, which means re-pathing the clips.**
`AgentAnimatorDriver` derives forward speed from
`animator.transform.worldToLocalMatrix.MultiplyVector(velocity).z`. Left on the
rotated, ×27.59 model child, world +Z lands on its local −X, `SpeedY` reads a
constant zero and the blend tree never leaves Idle — the creature slides around
in its idle pose. Curve paths are relative to the Animator's GameObject, so
`VrescalBuilder.Rehost` copies the imported clips into standalone `.anim` assets
under `Assets/Game/Art/Animations/Creatures/Vrescal/` with every path prefixed
by the model child's name. The Animator needs no Avatar for this — a Generic
Animator plays clips by path when root motion is off.

Worth knowing for the next creature: verifying a prefab by instantiating it and
*forcing* the transform to identity hides both of these. Instantiate it the way
a user would, then check root scale, `BoxCollider.bounds.size`, and
`worldToLocalMatrix.MultiplyVector(Vector3.forward)`.

## Not done

- Nothing spawns it. There is no wildlife spawn table in the project; creatures
  are hand-placed in `Assets/Game/Scenes/World/Chunks/Chunk_*.unity`, or added
  to `RobotSettlementRecipe.robotPrefabs`, which is semantically wrong for an
  animal.
- No `NetworkObject`. No creature or robot prefab in the project has one, so it
  matches what is already there. If one is added, run
  **Tools > SpaceGame > Multiplayer > Sync Network Prefabs**.
- No audio. `PerceptionModule` and `CloseCombatModule` both take FMOD
  `EventReference`s that are left empty.
