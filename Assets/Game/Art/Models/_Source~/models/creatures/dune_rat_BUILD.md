# Dune Rat — build record

A bipedal desert rodent: long hind legs, small forelimbs held up at the chest,
a spined dorsal crest and a metre and a half of counterbalancing tail. Hostile
wildlife. The mesh and the skeleton were **hand-authored by Tobias Fremming**;
this record covers the rig repair, the animation and the Unity side built
around them.

## What arrived, and what was missing

The model landed on `main` as `rotte.fbx` and was moved to
`Assets/Game/Art/Models/Creatures/Organic/DuneRat/dune_rat.fbx`. **There was no
`.blend` anywhere in the repository** — the FBX was the only artifact, so the
round-trip through FBX is the whole of the rig's history, and it is a lossy
one.

Measured off the import before anything was touched:

| | |
|---|---|
| Armature | `Armature.001`, 55 bones, rotated 180° about Z |
| Mesh | one skinned `Plane.001`, 1883 verts / 1836 polys, 32 vertex groups |
| Mesh scale | **0.3935, unapplied** |
| Material | `Material.002`, one image datablock pointing at a *directory* |
| Actions | **none at all** — `bpy.data.actions` was empty |

Four things were wrong, and they are the reason the build is shaped as it is:

1. **The IK constraints were gone.** FBX stores no constraints. The rig carries
   a complete control layer — `IK_back.L/R`, `IK_front.L/R`, `hoof_B.*` and
   `hoof_F.*` under them, and `pole_back.*` / `pole_fromt.*` directors — and all
   of it imported as loose bones driving nothing. That is worse than it sounds:
   `hoof_B.*` and `hoof_F.*` are **deform** bones carrying the foot and hand
   geometry, and they hang off the IK bones rather than off the leg chain. With
   no constraints, rotating a femur moved the shin and left the foot behind.
2. **15 of the 55 bones were leaf-bone litter** (`metarsal.L_end`,
   `pole_fromt.R_end`, …) added by whichever exporter wrote the file. None of
   them carried a vertex group. Re-exporting with `add_leaf_bones=True` would
   have stacked a second set on top of them.
3. **Nothing was measured in anything.** 180° of armature yaw, 0.3935 of
   unapplied mesh scale, origin nowhere in particular.
4. **No animation.** This was the bulk of the work.

## It is a biped, whatever the bone names say

The deform chain is named for a quadruped — `femur`/`fibula`/`metarsal` behind,
`scapula`/`humerus`/`radius`/`metacarpal` in front. **That reading is wrong**,
and taking it at face value would have produced a four-legged walk cycle on an
animal that has no front legs to walk on.

What settled it was rendering the rest pose rather than reading the outliner:

| | hind | fore |
|---|---|---|
| Chain reach | 0.990 m (L) / 1.037 m (R) | 0.291 m (L) / 0.296 m (R) |
| Rest foot height | on the sand | **0.41 m clear of it** |

The forelimbs are a third the length of the hind legs and do not touch the
ground in the pose the author built. The trunk is held horizontal, the tail is
out behind as the counterweight, and the whole silhouette is a jerboa or a
small theropod. Every clip is built on that reading: the hind feet carry the
gait, and the forelimbs gesture, tuck, and swipe in the attack.

**Bone names are left exactly as the author made them**, misspellings included
(`metarsal` for metatarsal, `pole_fromt` for pole_front). The vertex groups are
keyed to them and renaming would silently unbind the mesh.

## The one rig change

`IK_back.L/R` and `IK_front.L/R` were parented to `root`. That is wrong for
foot IK and it is not a cosmetic complaint: `root` is the body bone, so the
feet travelled with the hips and **there was no pose in which a foot could stay
planted while the body moved over it** — which is the entire job of a foot IK
target. They are unparented in `dune_rat_rig.py`.

The consequence to remember is that the forelimb targets are now detached from
the body too, and since they are not touching anything they have to be moved by
hand every frame or the arms tear off the chest as it bobs. `dune_rat_anim.py`
applies the trunk transform to them explicitly for that reason.

## Pole targets are wired up to nothing, on purpose

All four chains are three bones, not two, so the solver is under-determined and
Blender resolves it by staying as near the pose it starts from as it can. That
pose is the author's digitigrade rest bend — knee forward, ankle back — and
keeping it is exactly what is wanted. A pole target would override that with a
plane derived from a control bone left at an arbitrary angle.

`probe_bend` in `dune_rat_rig.py` is what establishes this rather than asserting
it: it drives every IK target through 15 samples spanning ±30% of that limb's
own reach and checks the middle joint never crosses the hip-to-foot chord, and
that the tip actually reaches the target.

```
IK metarsal.L     reach 0.990 m, 0 flip(s) over 15 samples, worst miss 0.00000 m
IK metarsal.R     reach 1.037 m, 0 flip(s) over 15 samples, worst miss 0.00000 m
IK metacarpal.L   reach 0.291 m, 0 flip(s) over 15 samples, worst miss 0.00000 m
IK metacarpal.R   reach 0.296 m, 0 flip(s) over 15 samples, worst miss 0.00000 m
```

The envelope is a fraction of each limb's **own** reach. A first version used a
flat ±0.30 m and reported both forelimbs as broken, which was the probe asking
a 0.29 m arm to cover the hind leg's stride, not a rig fault.

The pole bones stay in the file, unwired, for an animator who wants explicit
knee control later.

## Geometry

Normalised by `dune_rat_rig.py`: metres, −Y forward (the library convention,
which the exporter's axis conversion maps onto Unity's +Z), sole plane on
z = 0, origin between the hind toe tips.

| | |
|---|---|
| Nose to tail tip | 2.60 m (1.69 m of it tail) |
| Width | 0.86 m |
| Height to ear tips | 1.26 m |
| Head bone | z = 0.97 m — a standing player's chest |
| Hip (femur head) | (±0.14, 0.26, 0.76) |
| Hind toe contact | (±0.32, 0.00, 0.00) |
| Bones after cleanup | 40 |

The pivot is between the **hind** toes, not in the middle of the body. A
NavMeshAgent steers the origin, and on a two-legged animal steering anything
else reads as the creature pivoting around a point in mid-air.

The sole plane is taken off the vertices, not off `bound_box` — the bounding
box is cached against the mesh as it was before `data.transform`, and trusting
it put the feet 5 cm underground.

## The feet do not slide, by construction

Rather than swinging the femur and hoping, each clip places the **toe tip** —
the point that actually touches sand — and works backwards to the IK target:

```
ik_target = contact − R_x(toe_angle) · (hoof_tail − hoof_head)
```

That inversion is the whole trick. The toe can roll through push-off while its
tip stays welded to the same speck of sand. During stance the contact travels
backwards at a constant rate; during swing it arcs forward on a smoothstep.

Because the rate is constant and known, the ground speed each clip carries is
not a guess either:

```
speed = stance sweep / (duty × clip duration)
```

| | frames | duty | sweep | **speed** |
|---|---|---|---|---|
| Walk | 26 | 0.62 | 0.550 m | **1.109 m/s** |
| Run | 16 | 0.36 | 0.772 m | **4.595 m/s** |

Those two numbers are the blend tree thresholds in `DuneRatBuilder.cs`. Retune
the gait and they must be retyped from what `dune_rat_anim.py` prints, or the
animal moves at a speed its legs are not stepping out.

`clip duration` is **Unity's**, `(lastFrame − firstFrame) / fps`, not Blender's
frame count. Using Blender's would put the run 7% fast. Unity confirms the
figures: `DuneRat_Walk len=0.800s`, `DuneRat_Run len=0.467s`.

The stride amplitudes are set by the leg geometry. The hip sits at y = +0.26
and the foot rests at y = 0, so the animal already stands with its feet ahead
of its hips, and the sweep is nudged rearward to stop the forward extreme
running the chain out to full stretch. At the shipped values the worst-case
chord is 84% of the leg's reach, which leaves the knee visibly bent all cycle.

## Actions

Six, at 30 fps:

| Action | Frames | Loops | |
|---|---|---|---|
| `DuneRat_Idle` | 91 | yes | breathing ×3, one weight shift, a head scan, two ear flicks |
| `DuneRat_Walk` | 26 | yes | duty 0.62, toe-off 28° |
| `DuneRat_Run` | 16 | yes | duty 0.36, crouched 7.5 cm, 9° nose-down, tail up hard |
| `DuneRat_Attack` | 30 | no | coil, surge forward, forelimb swipe |
| `DuneRat_Hurt` | 16 | no | flinch, head snaps up and away, half a step back |
| `DuneRat_Death` | 50 | no | hind legs buckle, drops onto its left flank, holds |

Everything the trunk does is phased off **left-foot midstance**, because that is
the instant the body is actually being carried by that leg. The neck and head
cancel most of the trunk's bob — a head that rides the body up and down reads as
a toy.

Loops close on themselves: frame `N` is an exact copy of frame 1, and
`DuneRatBuilder` slices 1..N−1. Playing both would hold one pose for two frames
every lap, which on a 16-frame run cycle is a visible hitch at the top of every
stride.

Idle's components run whole numbers of cycles over the clip so the seam closes
— 3 breaths, 1 weight shift, 2 tail sways — except the ear flicks, which are
gaussian bumps placed at 0.21 and 0.67, nowhere near the seam.

All clips are **in place**. `NavMeshAgentMotor` owns movement and a clip that
also walked the creature forward would fight it.

### The tail

The tail is the counterweight on a jerboa-shaped biped and it should be the
loudest thing on the animal at speed. The first version had it at a twitch, and
worse, **damped exactly where it should have been strongest**. Measured off the
exported FBX as peak-to-peak range of the widest quaternion component on each
bone's own local curve:

| tail4 | before | after |
|---|---|---|
| Idle | 0.095 | 0.094 |
| Walk | 0.069 | **0.174** |
| Run | 0.104 | **0.309** |

The tell was that tail4 moved *less* at a walk (~8°) than standing still in
Idle (~11°), and barely more at a run — so the faster the animal went, the
quieter its tail got. The ordering `Idle < Walk < Run` is now asserted by
`dune_rat_export.py` on every export, because "it looks fine" is what let it
ship the first time.

What changed:

- A **fore-aft sweep in the sagittal plane** at twice stride frequency —
  once per footfall — phase-locked to the same `step` term that drives the
  trunk's pitch and bob, so the tail opposes the body instead of floating free.
  This is the readable motion; the lateral sway is kept, smaller, for life.
- `tail_sweep` is 10° at a walk and 18° at a run, against 2.0/3.5 before.
- The per-segment ramp went from linear to **`k ** 1.5`**. What an animator
  sees is the accumulated angle at the tip, but what lands in the FBX and gets
  measured is each bone's *own local* curve, and a linear ramp spreads the
  motion so evenly that no single bone reads as doing much.
- `tail_lag` delays each segment behind the one before it, which makes it a
  whip rather than a rigid see-saw. Kept under ~0.6 rad: beyond that the
  segments start cancelling and the accumulated sweep collapses even as the
  individual angles grow.

The constant `tail_lift` keeps the whole sweep above horizontal, so the tip
cannot reach the sand at full amplitude — at a run the tip travels roughly 0°
to +69° from horizontal, and the tail base sits 0.53 m behind and 0.9 m above
the hind toes, well clear of the legs.

Attack, Hurt and Death were left alone; Attack's whip already read well at
0.225.

Two decisions worth knowing about:

- **The attack strike is nearly all translation, and the pitch is small.** The
  first version drove the nose down 17° and looked more violent in isolation.
  It was wrong: the head already rests at 0.97 m, which is a standing player's
  chest, and every degree of nose-down walks the bite towards their knees. The
  shipped version surges the trunk 0.28 m forward at 8° and keeps the head
  level. The feet stay planted throughout — the reach comes from the body
  travelling over them, which is both how the animal would do it and why
  nothing slides.
- **Death abandons foot contact on purpose.** It is the only clip that does. A
  corpse is not standing on anything, and holding the toes to the sand while
  the trunk rolls 78° would stretch both legs straight and leave the animal
  apparently propped on stilts.

## Winding — why it rendered see-through

The author's mesh ships with **688 of its 1836 faces wound backwards**. Not a
uniform flip; a mixture. With backface culling that is exactly the "transparent
from one side" symptom: the near surface is culled away and you look straight
through it into the inside of the far one.

It is **inherited, not introduced** — the counts are identical measured on
`rotte.fbx` as it landed on `main`, before any of this work touched it. It is
repaired in `dune_rat_rig.py`, because that is the only place upstream of Unity
that can repair it.

The trap is the order of two operations. The mesh also carries **custom split
normals**, and those override the face normals for everything you can see.
Recalculating outside without clearing them first appears to do *nothing*: the
winding changes, the shading does not, and it is very easy to re-export a file
that looks identical and conclude the recalculation failed. Clear first, then
recalculate. And note it is the **winding**, not the normals, that culling keys
off — so clearing the custom normals alone would have fixed the shading and
left the model just as transparent.

Signed volume is the one-number check, and the export verifier prints it on the
file it just wrote:

| | signed volume |
|---|---|
| As authored | 0.031 |
| After recalculation | **0.280** |

A ninefold jump, which is what happens when a third of the surface stops
cancelling out the rest. Anything ≤ 0 means globally inside out.

The **112 non-manifold edges are left alone deliberately**. They are open
boundaries in hand-modelled geometry — ear membranes, the mouth interior — and
welding them shut is a change to the author's sculpt, not a repair.

## Materials — the second, independent reason it was see-through

The FBX's `Material.002` was a bare Principled node plus an image texture
pointing at a *directory* — the texture never shipped, and there is none
anywhere in the repo. It is replaced by `Mat_Hide_Sand_Pale`, linked from
`palette.blend`, which is the same hide the Vrescal uses. The UV map is intact,
so a texture can be dropped on later without touching the rig.

But **every hide entry in `palette.blend` has `blend_method = 'HASHED'`** —
alpha-hashed transparency — and that rides through the FBX into whatever
material Unity synthesises from it. On a creature that is simply wrong, and it
is a *second* cause of see-through that is completely independent of the
winding: repairing the geometry alone would still have left a ghost.

Fixed in two places, neither of them the palette:

- `dune_rat_export.py` forces `blend_method = 'OPAQUE'` on the **local copy**
  it makes at export, so the raw FBX is honest. The palette itself is shared by
  every model in the library and is outside this model's ownership — changing a
  palette entry to fix one creature is how a palette stops meaning anything.
- `DuneRatBuilder.EnsureHideMaterial` creates
  `Assets/Game/Art/Models/Creatures/Organic/DuneRat/DuneRat.mat`, an explicitly
  opaque URP/Lit material at `#E7B345`, and assigns it to the renderer. The
  prefab therefore does not depend on the FBX's material at all, and cannot
  inherit transparency again from this palette entry or a future one. Setting
  `_Surface` alone is not enough — render queue, blend state, `_ZWrite` and the
  keywords are separate properties and a material that has ever been
  transparent keeps them, so all of them are set.

## Pipeline

```
blender --background --python dune_rat_rig.py      # repair rig, place, save .blend
blender --background --python dune_rat_anim.py     # author the six actions
blender --background --python dune_rat_export.py   # -> Assets/.../dune_rat.fbx
# then in Unity: Tools > Creatures > Build Dune Rat Prefab
```

`dune_rat_rig.py` reads **`Assets/Game/Art/Models/_backups~/dune_rat_original.fbx`**
— the author's untouched export, preserved out of git history (it landed on
`main` as `Assets/Game/Art/Models/Creatures/rotte.fbx` in a0594505).

It deliberately does *not* read the shipped
`Creatures/Organic/DuneRat/dune_rat.fbx`. That path is what `dune_rat_export.py`
**writes**, and pointing the rig script at it makes the pipeline a loop that
eats its own output. Normalising is not idempotent — it yaws 180° and rescales
against the measured length — so running the pair twice in sequence produces an
animal **facing backwards** at the wrong size, and it does it silently: every
log line still looks plausible, just with different numbers. This happened
once during the build. `refuse_if_already_built` now checks two fingerprints of
the author's raw export (the armature is still `Armature.001`, the mesh still
carries unapplied scale) and aborts with an explanation rather than producing a
backwards rat.

`dune_rat_rig.py` is otherwise a one-shot: it overwrites `dune_rat.blend`, so
re-running it **discards any hand edits**. `dune_rat_anim.py` and
`dune_rat_export.py` are both re-runnable; the anim script deletes and
re-authors the action set from scratch, and the export never writes to the
.blend.

`dune_rat_export.py` is far thinner than `vrescal_export.py`, and the contrast
is the point. The Vrescal's .blend is a hand sculpt kept at the author's 19.94-unit
working scale with its origin at the head, so its exporter has to rescale,
rotate and re-pivot the animal on the way out. The Dune Rat had no .blend at
all, so the rig script was free to normalise the source itself and the export
fixes nothing. **If `dune_rat_export.py` ever grows a placement transform, the
bug is upstream.**

The export bakes from the **evaluated** pose, which matters more here than
usual: `femur`, `fibula`, `humerus` and `radius` have no curves on them at all,
only the IK targets and the trunk are keyed, and what lands in the FBX is the
solver's output sampled per frame. `verify_export` re-imports the file it just
wrote and proves it:

```
Re-imported dune_rat.fbx: 40 bones, 6 action(s)
  Arm_DuneRat|DuneRat_Walk    frames 1..26     80 curves, widest swing 0.3624
  Arm_DuneRat|DuneRat_Run     frames 1..16     80 curves, widest swing 0.6703
  ...
```

Nonzero curve counts on the IK-solved bones is the check that the constraint
evaluation was not skipped; zero would mean the animal ships sliding around in
a T-pose.

## Unity side

Generated by `Assets/Game/Editor/Creatures/DuneRatBuilder.cs`, re-runnable from
**Tools > Creatures > Build Dune Rat Prefab**:

- `Assets/Game/Art/Models/Creatures/Organic/DuneRat/dune_rat.fbx` — Generic
  rig, avatar from this model, six clips sliced from the takes.
  `optimizeGameObjects` and `optimizeBones` are off. Unlike the Vrescal — whose
  meshes are bone-parented, so stripping transforms would delete the very
  things its clips animate — this is a single properly skinned mesh and
  optimising would be *safe*. It is off anyway so the bones stay addressable
  for bite sockets and hit effects.
- `Assets/Game/Art/Animations/Creatures/DuneRat.controller` — one 1-D blend
  tree on `SpeedY` (idle 0 / walk 1.109 / run 4.595 m/s), plus Attack, Hurt and
  Death off Any State. The parameter names are `AgentAnimatorDriver`'s
  verbatim, misspellings (`IsImmobalized`, `Meele`) included. Blends are shorter
  than the Vrescal's because the one-shots are half the length.
- `WildlifeFaction.asset` and a `Wildlife ↔ Player = Hostile` row appended to
  `GlobalRelationships.asset` — both additively, neither clobbered.
- `Assets/Game/Prefabs/Agents/Creatures/DuneRat.prefab` — NavMeshAgent +
  `NavMeshAgentMotor` + `AgentController`, perception, chase, close combat,
  wander, health, faction, targeting and `SceneTracked` on Migrate.

`AgentAnimatorDriver`'s two scale factors are set to 1 so `SpeedY` reaches the
blend tree as true metres per second — by default it multiplies velocity by 3×
and the tree would sit pinned at Run. The walk threshold (1.109) and
`NavMeshAgentMotor.walkSpeedMultiplier` (1.109 / 4.595) are the same number
twice; change one and the creature moon-walks. `WanderModule.speedMultiplier`
is that same ratio, so an unbothered rat travels at exactly the speed the walk
clip steps out.

The collider is a **box covering the body and stopping short of the tail**
(centre 0, 0.62, 0.04; size 0.80 × 1.16 × 1.70). 1.69 m of this animal's
2.60 m is tail, and it is a whip held out behind for balance — wrapping it
would give a 0.9 m creature a collider longer than a groundcar and block the
player with empty air a body length behind it. The Vrescal makes the opposite
choice for the opposite reason: its tail is as thick as its trunk.

90 HP against the Vrescal's 260. This one is fast and soft, not a wall.

### "It doesn't move" — it is the NavMesh, not the prefab

Reported as a frozen creature. It is not an asset fault, and the assets were
never the thing to fix. Measured in Play mode in the live streamed world:

**A rat that is on the NavMesh works, immediately and completely.** Spawned at
a point validated with `NavMesh.SamplePosition`, it travelled **12.1 m in
24.6 s**, `agent.velocity` 4.97 m/s, `SpeedY` 4.602 against the Run threshold
of 4.595, `DuneRat_Run` playing at weight 1.00, bone transforms live. It also
broke off and chased the player unprompted, which incidentally confirms
perception, faction hostility and `ChaseModule`.

**A rat that is off the NavMesh does nothing at all, silently.**
`NavMeshAgent.isOnNavMesh` is false, `SetDestination` throws
*"can only be called on an active agent that has been placed on a NavMesh"*,
velocity stays zero, `AgentAnimatorDriver` therefore feeds `SpeedY = 0`, and
the blend tree sits on Idle forever. There is no error unless something calls
`SetDestination` directly. **That is the entire reported symptom.**

And it is easy to land in. In this world it happened on **two of three**
placement attempts, including once after snapping to `NavMesh.SamplePosition`'s
own result:

```
A hand-placed at (3821.62, 108.81, 1589.54)   isOnNavMesh=False
SamplePosition(r=5)  -> True (3821.62, 110.13, 1589.54)
B snapped   at (3821.62, 110.13, 1589.54)   isOnNavMesh=False
```

The NavMesh surface on a dune flank can sit metres above the terrain the
prefab is dropped onto — 4.3 m in the case above — and the streaming grid means
whether a chunk's NavMesh is loaded at all depends on where the player is.

**Diagnosing this on any future creature:** check `agent.isOnNavMesh` first,
before touching the animator. If it is false, nothing downstream can work and
no amount of animator debugging will help.

Two things were hardened while the prefab was open, neither of which was proven
to be the cause:

- **`cullingMode` is now `AlwaysAnimate`, not `CullUpdateTransforms`.**
  CullUpdateTransforms decides whether to write bone transforms from the
  SkinnedMeshRenderer's bounds, and those come from the bind pose with
  `updateWhenOffscreen` false. The clips move the tail and legs well outside
  that box, so the bounds are a poor proxy for where the animal is, and the
  failure mode is a creature frozen mid-stride while plainly on screen. It is
  set explicitly so it is recorded as a prefab override rather than silently
  inherited from the importer's default. (Measured and *ruled out* as the cause
  here: `localBounds` is 0.010 × 0.016 × 0.026 in the mesh's own ×100-shrunk
  space, which is a correct 1.0 × 1.6 × 2.6 m once the transform scale is
  applied.)
- **The avatar is now asserted at build time.** A Generic rig with a missing or
  invalid avatar plays every clip to no visible effect, which reads exactly
  like "the animator is not running". It was valid, and still is, but it is
  checked now rather than assumed.

Hypotheses that were tested and **disproved**, so nobody retests them:
`walkSpeedMultiplier = 0.2413493` is exactly 1.109 / 4.595 and is not a
rounding artifact pinning the tree at idle — `SpeedY` was measured reaching
4.602 at runtime; the avatar is valid at runtime; `AgentAnimatorDriver` does
feed `SpeedY`; and there is a NavMesh in the played region.

### Verified

- Re-imported the **exported** FBX: 40 bones, no leaf bones, all six takes at
  the right frame ranges, IK-solved bones carrying real baked motion.
- `DuneRatBuilder.Build()` run in the Editor over the MCP bridge. No warnings —
  every serialized field on every agent component resolved, so nothing on the
  prefab is silently sitting at a default.
- Prefab measured in Unity: root scale 1, renderer bounds 2.63 m long, `head`
  bone at +0.510 Z and `tail4` at −1.336 Z (facing Unity +Z), feet at y ≈ 0.
  **No ×100 lossyScale on the prefab** — the SkinnedMeshRenderer's transform
  carries the usual Blender ×100, but it is compensated and world size is
  correct in metres.
- Clip durations confirmed against the speed derivation: walk 0.800 s, run
  0.467 s, loop flags correct on all six.
- Rendered the rest pose, walk, run, attack, hurt and death in Blender, and the
  prefab in Unity.
- **Winding**: signed volume of the exported mesh 0.280, checked on the written
  file by `dune_rat_export.py` on every run.
- **Both flanks rendered in Play mode** through a RenderTexture at ±3.4 m, mid
  run pose. Solid and opaque from both sides, correct sand colour, no
  see-through — the defect that started this.
- **Play mode, live world**: travelled 12.1 m in 24.6 s at 4.97 m/s with
  `SpeedY` 4.602 and `DuneRat_Run` at weight 1.00. Both the on-NavMesh and
  off-NavMesh states reproduced deliberately.
- **Tail** re-measured off the exported FBX: Run tail4 0.104 → 0.309, ordering
  Idle 0.094 < Walk 0.174 < Run 0.309, asserted by the exporter.
- `occlusionLayers` set to Default | Ground | Interior (mask 641), silencing
  the per-spawn warning and making the rat actually occludable.

## Not done

- **No spawner.** Nothing places it. There is no wildlife spawn table in the
  project; creatures are hand-placed in
  `Assets/Game/Scenes/World/Chunks/Chunk_*.unity`. Same gap the Vrescal has.
- **No texture.** The FBX referenced one that never shipped. The animal is a
  flat palette hide with intact UVs.
- **No `NetworkObject`.** No creature or robot prefab in the project has one.
  If one is added, run **Tools > SpaceGame > Multiplayer > Sync Network
  Prefabs**.
- **No audio.** `PerceptionModule` and `CloseCombatModule` both take FMOD
  `EventReference`s that are left empty.
- **Not seen in motion in Unity.** The clips were verified numerically and
  rendered frame-by-frame in Blender, but no Play-mode pass was run — the
  Editor was shared with another build at the time.
- The two hind feet sit 2.4 cm apart in rest height, an asymmetry in the
  author's hand-posed rig (the L and R leg bones differ in length by up to
  10%). Every clip levels both toes onto z = 0, so it never shows, but the
  **bind pose** still carries it.
