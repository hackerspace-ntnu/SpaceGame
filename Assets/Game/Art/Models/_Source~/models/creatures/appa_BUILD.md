# Appa — build record

A six-legged bison: white hide with brown dorsal stripes, a heavy mane, two
horns, a broad flat tail and six splayed feet. Modelled on Appa from *Avatar:
The Last Airbender*. The mesh and its sixteen materials were **hand-modelled by
Tobias Fremming**; this record covers the rig, the eleven clips, the Unity side
built around them and the behaviour he was given.

## Scale

The sculpt is 5.75 m long, which is a believable bison. `AppaBuilder.Scale`
multiplies the prefab root by **1.5**, so in game he is 8.4 m long and 3.7 m to
the top of the back -- a mount you look up at.

The gait speeds scale with him and the playback rate does not, which is not a
fudge: a 1.5x leg sweeping the same arc covers 1.5x the ground per cycle, so the
feet still match the floor. Turn rates are angles and stay where they are.

## Appa_Jump

An in-place hop for a rider pressing Space. 26 frames = 1.08 s, which is
`NavMeshAgentMotor.mountedJumpDuration` (0.55 s) at the 2.0 playback rate every
Appa clip runs at -- so the pose finishes landing exactly as the motor puts him
down.

The height is deliberately **not** in the clip. The motor supplies it by
animating the agent's `baseOffset`; keying a rise here as well would double it.
The front legs reach for the ground before the back ones, so he lands nose-first
the way a heavy quadruped does instead of dropping flat.

## What arrived

The `.blend` was at `C:\Users\tobia\Documents\Blender\appa.blend`, outside the
repository, open in Blender with unsaved changes. It was brought into the
library with `save_as_mainfile`, so the Documents copy was **never written to**
and stands as the pre-rig backup at its last-saved state.

Measured off the file before anything was touched:

| | |
|---|---|
| Objects | 27 meshes, all unparented, all named `Cube.*` |
| Armature | **none** |
| Actions | **none** |
| Materials | 16, all local — none linked from `palette.blend` |
| Collections | `Collection`, `Ears`, `Eyes`, `Feet`, `Fur`, `Hair`, `Horns`, `jaw_lower`, `teeth` |
| Size | 5.5 m nose to tail, soles at z = -1.76, facing **-X** |
| Triangles | 85,178 |

The author's collections are what made the mesh names legible — `Cube.026` is
the mane only because it is the sole occupant of `Hair`. Two notes on them,
neither of them acted on:

- `Cube.018` sits in the `jaw_lower` collection but is an **eye**: same
  dimensions as `Cube.017` in `Eyes`, mirrored across y. It is rigged to `head`
  with the other eye, not to `jaw`, because that is what it is. The collection
  membership was left exactly as the author set it.
- The tail is modelled **0.48 m below the sole plane**. Standing on flat ground
  with the feet planted, the tail tip therefore sinks into the terrain. That is
  the sculpt, not a placement error, and moving it would be editing the
  author's geometry. See "Known" below.

## Rig — `appa_rig.py`

28 bones. `root` on the sole plane, a four-link trunk (`spine1..3`, `neck`,
`head`) with a `jaw`, a three-link `tail`, and six three-link legs named
`femur/tibia/hoof` × `F/M/B` × `.L/.R`.

**Purely additive.** No existing geometry was moved, reshaped, renamed or
deleted, and the script refuses to run twice.

Binding is split two ways:

- **Skinned** (`ARMATURE_AUTO`, bone-heat) — the six meshes that must deform:
  legs, body, head, mane, saddle fur, ears. Heat weighting solved all six on
  the first attempt; the proximity fallback in the script never fired.
- **Bone-parented**, rigidly — the other 21: muzzle, horns, eyes, brow tuft,
  lower jaw, eight teeth and the six hooves.

**Bone-heat output is filtered, and must stay filtered.** Left raw it bound
24.4% of the shoulder fur and 7.4% of the mane to `femur_*` -- the mane drapes
around the front legs, so the solver called them neighbours. The rest pose hid
it completely; in play mode every stride tore the hair into floating shards.
`WEIGHT_RESTRICT` names the bones each mesh may use and `restrict_weights()`
renormalises what survives, giving any vertex left with nothing a fallback bone
so it cannot collapse to the origin. After filtering: mane = neck/head/spine3,
shoulder fur = spine2/spine3/spine1, no leg influence on either.

The legs are the reason skinning was necessary at all: **all six are a single
mesh**, so they can only move independently through per-leg vertex weights.
Splitting that mesh into six would have been easier to weight and is exactly
the edit this workflow forbids.

**`Cube` is not "the legs" — the object names lie.** `Cube` (1538 verts,
z -1.18..-0.47) is a flat, lens-shaped blob of interior filler sitting at belly
height between the legs. The leg volume you actually see is part of the *body*
mesh `Cube.016`, which spans z -1.75..+0.61 and holds the torso, the hump, the
tail **and** all six legs down to the ankles. `WEIGHT_RESTRICT` was first
written off those names, so its `Cube.016` row listed only spine/neck/tail and
dropped every leg influence the solver had found. See "Leg weights" below.

`Cube` is not exported at all any more — see "The holes behind the front legs".

## Leg weights — `appa_weights.py`

    blender --background appa.blend --python appa_weights.py            # dry run
    blender --background appa.blend --python appa_weights.py -- --save

Appa shipped skating: his hooves swung correctly and his legs did not move at
all. The hooves are bone-parented, so they follow `hoof_*` no matter what the
weights say — which is exactly what masked it. `Cube.016` measured **100%
`spine1` in the ankle band and 81% in the shin band**, against a rig that
rotates femur 64° and tibia 52° in `Appa_Walk`.

Nothing in the animation was wrong, and no keyframe- or curve-based check could
have caught it, including `appa_anim.py::verify` — that proves the *bones* move,
not the skin. The test that finds it is the evaluated mesh: sample
`obj.evaluated_get(depsgraph)` at two frames of the gait and print mean vertex
displacement per height band.

| Band (world z) | Before | After |
|---|---|---|
| ankle `-2.00..-1.40` | 0.000 | **0.608** |
| shin `-1.40..-1.15` | 0.066 | **0.497** |
| knee `-1.15..-0.95` | 0.061 | **0.320** |
| thigh `-0.95..-0.75` | 0.069 | **0.237** |
| hip `-0.55..-0.35` | 0.046 | 0.055 |
| torso `-0.35..0.00` | 0.005 | 0.005 |
| hump `0.00..1.00` | 0.000 | 0.000 |

The script re-runs bone heat on a throwaway copy — running it on the real object
would replace every group and discard the torso weighting — and transfers only
what the leg bones claim, blended against what is already there.

**The transfer is gated by height.** Ungated, bone heat claimed 2808 of 2917
vertices and gave the hump 11% `femur_M`, so the whole back rocked with the
stride. `GATE_TOP = -0.35` / `GATE_BOTTOM = -0.70` smoothsteps the leg bones out
across the hip, which is also the blend the hip joint wants; the table above
shows torso and hump landing at exactly their old values.

**It is idempotent**, because the kept weights are rebuilt from their *original*
relative shares rather than scaled in place. Scaling in place compounds: each
re-run would hand another `(1 - share)` of the body to the legs.

Every mesh lands in one bucket or the other, so `Arm_Appa` is the only root
object. The exporter depends on that.

## Animation — `appa_anim.py`

    blender --background appa.blend --python appa_anim.py -- --save

| Clip | Frames @ 24 fps | Loops | Fired by |
|---|---|---|---|
| `Appa_Idle` | 192 (8.0 s) | yes | blend tree, `SpeedY` 0 |
| `Appa_Walk` | 48 (2.0 s) | yes | blend tree, `SpeedY` 1.4 |
| `Appa_Run` | 30 (1.25 s) | yes | blend tree, `SpeedY` 3.2 |
| `Appa_TurnL` | 36 (1.5 s) | yes | Turn tree, `TurnSpeed` -45 |
| `Appa_TurnR` | 36 (1.5 s) | yes | Turn tree, `TurnSpeed` +45 |
| `Appa_Graze` | 96 (4.0 s) | yes | `NpcTaskModule`, bool `IsGrazing` |
| `Appa_Happy` | 60 (2.5 s) | no | `PettableModule`, trigger `Happy` |
| `Appa_Roar` | 48 (2.0 s) | no | `FightOrFlightModule`, trigger `Roar` |
| `Appa_Ram` | 36 (1.5 s) | no | `CloseCombatModule`, trigger `Ram` |
| `Appa_Hurt` | 18 (0.75 s) | no | `AgentAnimatorDriver`, trigger `Hurt` |
| `Appa_Death` | 72 (3.0 s) | no | triggers `Die` **and** `Death` |

### Never key a raw euler component on this rig

Every bone rotates about its **local** axes, and those are wherever the bone's
roll left them. Measured: +15° about local **Y** moves the tip of *every single
bone* by nothing at all, because Y runs along the bone and keying it is a pure
twist.

The first version of this file put the entire leg swing and the entire body bob
on Y. The walk cycle therefore had **no leg motion whatsoever** — Appa slid
along with his feet locked in the rest pose. The file was full of keyframes, so
every count-based check called it healthy, and the only clip that appeared to do
anything was the idle, whose head drift happened to land on X.

Nothing sets `rotation_euler` directly any more. `pose(bone, pitch=, yaw=,
roll=)` takes angles about **world** axes and converts them into whatever local
euler that bone needs, so a line reads "nose up 20°" and cannot silently animate
nothing. `verify()` then fails the run if any clip has no curve whose value
changes, or if a gait clip moves fewer than 12 leg curves.

The gait is a **metachronal wave**: on each side the legs fire back-to-front a
third of a cycle apart, with the two sides half a cycle out of phase. That is
what a hexapod does at walking speed, and it reads as a deliberate lumbering
animal rather than the alternating-tripod scuttle an insect uses — the right
call for something built like a bison. Duty cycle is 0.6 stance / 0.4 swing, so
most legs are carrying weight at any instant.

Idle is breathing on the spine, a slow head drift, a chew on the jaw and a tail
swish, at three incommensurate rates so the four-second loop does not read as
one sine wave.

Two deliberate constraints:

- **Nothing keys `root`.** Unity's `RootMotionCurveStripper` deletes root-bound
  curves from every imported clip, so a bob authored there would silently
  vanish. The body bob lives on `spine1`.
- **Every clip is in place** — no forward travel. The prefab keeps
  `applyRootMotion = false`. `Ram` and `Death` do move the body, but on
  `spine1`: the ram is a real 0.55 m lunge and the collapse a real 1.15 m drop,
  because a charge built from neck rotation alone reads as a nod and a death
  built the same way leaves the animal folded but floating at walking height.
  `spine1` survives the import; `root` would not.

FK, not IK. `dune_rat_rig.py` drives its four limbs with IK targets and bakes
the solver output; six IK chains would be six more things to bake and verify,
and the gait is authored rather than solved. He is now a NavMesh creature and
the feet still do not plant on slopes — they interpenetrate on anything steep.
That is the open cost of this choice, and IK is where to go if it starts to
show.

## The holes behind the front legs

Reported as *"appa's mesh has holes behind both his front legs. Maybe that's an
inverted normal or something"*. It was neither a hole nor a normal.

`Cube` — the belly blob above — is on `Material.009`: **no texture**, base
colour `(0.242, 0.254, 0.305)`, a dark blue-grey nothing else on the animal
uses. It does not stay inside: **441 of its 1538 vertices (28.7%) sit outside
`Cube.016` in the rest pose**, protruding up to 0.21 m. Untextured dark grey
breaching a light tan hide reads as holes punched in the flank, symmetric
because the sculpt is mirrored, and worst behind the front legs where the body
is thinnest.

Everything the report suggested was ruled out first, and all of it measured
clean on `Cube.016`:

| Test | Result |
|---|---|
| boundary edges (holes) | **0** |
| self-intersecting faces | **0** |
| winding-inconsistent edges | **0** |
| Laplacian outlier vertices | none beyond 1x the median edge |
| idle pose vs. before the re-weight | **identical to 5 decimal places** |

**The diagnostic that actually works is flat shading.** Workbench cavity and
URP's AO both darken creases, so every fold looks like a hole and you go hunting
topology that is fine. Render each mesh in its own colour with `light='FLAT'`
and `show_cavity=False`: the body alone comes out perfectly smooth, and the
intruding blob is unmistakable.

Fixed by `appa_export.py::EXCLUDE`, which drops the object from the FBX before
anything measures or rewrites it. The `.blend` keeps it, so this is one line to
undo. `Cube.016` is closed and complete, so nothing visible is lost — verified
by rendering the body on its own from eight angles against a loud background.

## Turning — `Appa_TurnL` / `Appa_TurnR`

He used to pivot like a turret. A NavMeshAgent choosing a new heading rotates
the transform without translating it, so `SpeedY` stays at 0, the blend tree
sits on Idle, and five and a half metres of animal swings round with its feet
planted.

Three pieces:

1. **The clips.** `appa_anim.py::_build_turn`, 36 frames, looping, **in place**
   — the agent owns the yaw, and a clip that rotated the root too would turn him
   twice (GDC-L1-ANIM-0004). Head and neck lead the turn, tail counterswings
   (GDC-L1-ANIM-0005).
2. **The measurement.** `AgentAnimatorDriver` measures yaw off the transform the
   same way it measures velocity — so it works on a machine that is only
   watching — and publishes `TurnSpeed` in **degrees/second, positive turning
   right**. Optional: looked up once per controller, skipped where absent.
3. **The controller.** `AppaBuilder` adds a `Turn` state, a 1-D blend tree on
   `TurnSpeed` with TurnL at −45, Idle at 0 and TurnR at +45, and enter/exit
   thresholds that are deliberately apart (18 vs 9 °/s) because an agent's yaw
   rate crosses any single number several times a second while it settles.

### Two things that were measured, not guessed

**Do not sweep the femur with yaw.** Yaw turns the leg about a vertical axis
through its own hip, and the foot hangs almost *on* that axis. Measured, ±15° of
yaw moved the front hoof 0.23 m sideways while the knee fold alone moved it
0.36 m fore-aft — the clip read as marching in place. Each foot instead gets the
tangent of its own arc about the turn centre, decomposed onto femur pitch
(fore-aft) and roll (sideways).

**Each leg's step scales with its own radius**, because a rigid rotation moves a
point at `ω·r`. A fixed step in metres made the middle legs — which sit almost on
the turn centre — imply 61° of body rotation per cycle against the outer legs'
36°. The turn also uses a much lower foot lift than the gait (22° of knee fold
against 40°), because folding the knee swings the shin fore-and-aft whether you
want it to or not, and on the middle legs that parasitic travel dominated:

| Leg | radius | foot arc | implied body rotation / cycle |
|---|---|---|---|
| F.L / B.L | 1.03 m | 0.62 m | 34° |
| F.R / B.R | 1.03 m | 0.56 m | 31° |
| M.L / M.R | 0.65 m | 0.52 m | 46° |

At 36 frames / 24 fps and the prefab's 2.0 playback rate that is one cycle every
0.75 s, so the outer legs place ~44 °/s. **`NavMeshAgent.angularSpeed` is set to
match** (45, down from 130) — the clip and the agent are one number authored in
two files, and the difference between them is skating.

Whether 45 °/s is the right *feel* for an animal this size is a play-test
question, not a derivation (GDC-L1-BAL-0005).

## The jaw, and why closing it used to move his eyes

He should hold his mouth shut and work it now and then; open it to roar, to
graze, and while he is enjoying being petted.

The motion was never the problem -- the **weights** were, and the answer turned
out to be that the head mesh should not follow the jaw at all.

His mouth is two interlocking pieces. `Cube.003` -- the lower jaw, carrying the
eight teeth -- is bone-parented to `jaw` and hinges rigidly (z -1.06..-0.74). The
head mesh `Cube.004` (z -0.89..+0.10) is the skull around it. The lower lip is
part of the jaw piece, so the skull has nothing to follow.

Three repairs were tried on the head mesh before that was clear, and each broke
something different:

| Attempt | What broke |
|---|---|
| raw bone heat | influence as far up as the brow -- opening his mouth dragged his eyes down |
| clamp by a horizontal Z band | creased the muzzle; a mandible runs diagonally and a horizontal cut crosses it |
| keep bone heat's shape, cut its weak tail | dragged the nose; heat reaches it at 0.45, and no volumetric rule separates an upper lip from a lower one a centimetre below |

`clear_face_jaw` takes the jaw off `Cube.004` altogether and hands those 3870
vertices' share back to head and neck.

Posing the jaw 25 deg open and measuring the face, after:

| Band (world z) | What is there | Movement |
|---|---|---|
| -0.90..-0.60 | jaw and mouth | **0.0000 m** |
| -0.50..-0.40 | eyes | **0.0000 m** |
| -0.40 and above | brow, horns, neck | **0.0000 m** |

The whole face is now rigid against the jaw, which is correct: the part that
should move is a different object.

**His mouth rests 38 deg OPEN.** That is the sculpt, and it means an unposed jaw
is a gaping one -- `Appa_Walk`, `Appa_Run` and both turns shipped him ambling
around with his mouth hanging open, because `_build_gait` only ever keyed legs,
spine and tail. It also means "closed" is not "unrotated", so the idle's gape was
being measured from an already-open mouth.

So nothing poses `jaw` directly any more. `set_jaw(arm, open_angle)` takes how far
open the mouth should be **measured from shut** and applies the `JAW_CLOSED = 38
deg` bias itself; every clip calls it, including the ones that just want it shut,
and `verify()` fails any clip that does not key the jaw at all.

The 38 deg was measured, not guessed: rotate the bone on a live instance in Unity
until the lip line meets. Measure it only once the weights are right: the first reading said 26 deg, but
the head mesh was still being dragged along and closed the gap early.

The idle loop is **8 s and opens once**, not 4 s opening twice: at the old rate he
worked his jaw about a third of the time and read as a nervous animal rather than
a resting one.

Openings, all measured from shut: idle 30 deg once per eight-second loop

### The jaw geometry sits forward of the muzzle

`appa_export.py::JAW_SHIFT` moves the lower jaw and its eight teeth back and up
by (0.06, 0, 0.04) m -- +X is toward the tail, the head being at -X -- because
the assembly sat slightly proud of the muzzle and a closed mouth read as an
underbite. Author request.

The mesh **data** is translated, not the objects: these parts are bone-parented
to `jaw`, so moving the object would be undone by the bone it hangs off, and
moving the bone would drag the hinge, which is where it should be. Applied at
export, so the sculpt is untouched; set the constant to zero to turn it off.

## Petting

Look at his **head** and press E.

That the head is the only thing that offers it costs no raycast filtering:
`Interactor.ResolveAlongRay` only lets a trigger answer when the
`IInteractable` is on that same GameObject, and never inherits one from a
parent. So `PettableModule` lives on a 0.75 m trigger sphere parented to the
`head` bone — `AppaBuilder.AttachPetTarget` — and his body's solid collider,
which is not a parent of it, offers nothing. Parented to the bone rather than
placed at an offset, so it follows his head down when he grazes.

Three things happen, in this order, and the middle one is why it is not just a
local animation:

1. The presser sends `NetMsg.PetRequest` with **their own player** in the
   payload, and starts their local cooldown immediately — the round trip is long
   enough to press E three more times into it.
2. The server re-checks his mood (the presser's copy may not have seen him start
   charging) and answers everyone with `NetMsg.Petted`.
3. Every machine plays `Appa_Happy` on him and the `Pet` gesture on the player
   who reached out. A reaction played only locally would leave him inert for the
   other players standing there watching.

He refuses while enraged or fleeing, and the prompt *hides* rather than
appearing and doing nothing — that is what `IContextualInteractable` is for.

**No persistent state.** Petting has no lasting effect: no affection counter, no
taming. If it ever should, that is a value on the creature and it needs a saver;
today there is deliberately nothing to save.

The player's half is `astronaut_pet.py` -> `PetCreature.fbx` ->
`PlayerPetGestureBuilder`, which puts a `Pet` one-shot on the **Upper Body**
layer so he can pet while walking.


## Export — `appa_export.py`

Writes `Assets/Game/Art/Models/Creatures/Organic/Appa/appa.fbx` (3.7 MB).

Three repairs are applied on the way out, none of them to the `.blend`:

- **Textures unpacked.** All six images are packed inside the `.blend` with an
  empty filepath, which exports as *no texture reference at all* -- the whole of
  "my texturing disappeared". They are written to `<fbx dir>/Textures/` and
  unpacked; setting `filepath` alone is not enough, because a still-packed image
  makes the exporter fall back to embedded bytes and skip the reference.
- **Duplicate shader chains collapsed.** Five materials carry both a flat and a
  textured Principled BSDF. Blender renders the textured one (it is the active
  output); the FBX exporter takes the *first* BSDF it finds, which is the flat
  one. The dead chain is deleted so there is no wrong node to pick.
- **Face winding made consistent.** The mane (378 of 6618 faces), the shoulder
  fur (378 of 7290) and the ears (96 of 192, an exact half) had faces wound
  against their neighbours. Blender draws both sides so it never shows; Unity
  lights the side the normal points at, so those patches came out dark while the
  rest of the same mesh looked fine. This is what read as "the fur on the head
  and shoulders is messed up and the UVs seem broken" — the UVs were always
  clean, and are untouched by this (verified by comparing every (vertex, uv)
  pair before and after).
- **Textures dilated.** Every map is a UV layout painted on a black canvas with
  no margin, so Unity's mipmaps average the islands against black and the black
  bleeds inward at distance. Painted pixels are grown outward before writing.
  `BackHair` needs it most and gains least honestly: it is **99.2% unpainted**,
  so dilation floods the 0.8% that was painted. It stops the shoulder fur going
  black, but that map wants painting or re-unwrapping properly.

- **Object transforms baked, so no mirror reaches Unity.** Sixteen meshes were
  duplicated by negating a scale axis and carry a negative scale determinant.
  Thirteen of those are rigid props and are genuinely harmless — they arrive as
  MeshRenderers still carrying the negative scale, and Unity reverses the culling
  mode on a negative-determinant renderer, cancelling the winding flip exactly.
  **The other three are skinned**: the legs, the mane and the shoulder fur. A
  SkinnedMeshRenderer does not deform through its own transform, so nothing
  reverses the culling for them and they light by a normal pointing into the
  mesh. `_apply_object_transforms()` bakes rotation and scale into the vertices
  so the mirror is gone before the FBX is written; `_make_normals_consistent()`
  then runs and recalculates outward, which is why it now reports 6240 of 6618
  faces on the mane rather than the author's own 378.

  Do **not** replace this with a pass that reverses the mirrored meshes by hand.
  One existed, it hit all sixteen, and it broke the thirteen that were fine — the
  mane rendered as a black dome lit from below.

Follows `vrescal_export.py`, not `dune_rat_export.py`, because Appa is a hand
sculpt: the normalisation is applied **on the way out**, never to the `.blend`.

- **Yaw +90° about Z**, turning the sculpt's -X forward into the library's -Y,
  which the FBX axis conversion maps onto Unity's +Z.
- **Pivot** moved to `(1.44, 0, -1.76)` — the sole plane, midway between the
  front and back feet.

Both are applied by transforming `Arm_Appa` itself. **Not** by a parent empty:
Blender's FBX exporter drops empties and the model arrives unrotated with no
error anywhere to say so.

`verify_export` re-imports the written file and fails the run if the bone count
is not 28, if fewer than two takes survived, or if any `*_end` leaf bone
appears.

## Unity — `AppaBuilder.cs`

`Tools > Creatures > Build Appa Prefab`. Re-runnable, and it re-runs the network
and saveable passes itself — see below for why that is not optional.

| | |
|---|---|
| FBX | `Creatures/Organic/Appa/appa.fbx`, Generic rig, avatar from this model |
| Clips | seven; see the Animation table |
| Controller | `Animations/Creatures/Appa.controller` — `SpeedY` blend tree + Roar/Ram/Hurt/Death one-shots |
| Prefab | `Prefabs/Agents/creatures/Appa.prefab` |
| In the world | `NpcWorldSim` template `wild-appa` in `persistentScene`, plus one hand-placed instance at `(3790, 100, 1588)` |
| Renderers | 27 -- 6 skinned, 21 rigid props, all single-sided |

`optimizeGameObjects` is **off and must stay off**: 21 of the 27 meshes are
bone-parented props, and optimising the hierarchy strips the transforms they
hang from. For the same reason `cullingMode` is `AlwaysAnimate` — the bind-pose
bounds of 21 small boxes are not a volume that follows the animation, so with
the default he freezes mid-stride while still on screen.

The three looping clips stop one frame short of the authored length because
`appa_anim.py` makes their last frame a copy of the first so the cycle closes;
playing both would hold that pose for two frames every lap. The one-shots keep
their last frame, which is their final pose.

### Speeds are derived from the clips, not chosen

Feet skate when ground speed and the clip's own stride disagree, and the knob
that fixes it (`animatorSpeedScale`) sets the playback **rate**, not which clip
plays. So the order is: measure the stride, pick the scale, let the speeds fall
out.

Hip to sole is 1.25 m. The femur swings ±26° over a 2.0 s walk and ±40° over a
1.25 s run, so one cycle covers 1.10 m in 2.00 s (0.55 m/s) and 1.61 m in 1.25 s
(1.29 m/s) at rate 1. `animatorSpeedScale = 2.5` lifts those to **1.4** and
**3.2 m/s**, which are the `NavMeshAgent` speed, the `walkSpeedMultiplier` and
the blend-tree thresholds. They make the feet match the ground; whether they are
the right *feel* is a play-test question.

### The builder must run the two registration passes itself

`Wire Saveable Prefabs` adds eight more savers to Appa and stamps his
`prefabId`, and this builder overwrites the prefab wholesale — so running it by
hand once is worthless, because the next build throws all of it away. `Build()`
therefore calls `SaveableWiring.TryWirePrefabs()` and **checks the result**: the
pass refuses outright in Play mode, and a builder that ignored that would save an
unwired prefab and report success. That is exactly how `PlayerShipBuilder` once
shipped a hull with no `prefabId` and five missing savers, with nothing visibly
wrong until the wreck stopped surviving a reload.

## Behaviour

Friendly, and no threat to anyone until something makes him one.

He is `FaunaFaction`, which has **zero rows** in `GlobalRelationships.asset`.
`FactionRelationshipTable.Get` returns Neutral for any pair it has no row for and
`AgentTargeting` only queries for Hostile candidates, so he can never acquire a
target on his own: the combat modules are wired and simply never get a frame.
Adding a Hostile row toward the player to "make him react" would make every Fauna
creature in the world attack on sight.

Two things give him a target, and both go through `AgentTargeting`:

| | |
|---|---|
| **Being hurt** | `ProvocationModule` hands him his attacker |
| **Being shot at** | `NoiseReceiverModule` aggros on `Gunshot`, handing him the shooter |

`investigateOn` is deliberately `None`. A spooked animal does not walk toward the
bang.

`FightOrFlightModule` then decides which stock behaviour answers. It sits at
Override + 1, one above `FleeModule`, so it ticks first every frame:

    CALM ──provoked──> FLEE ──damage ≥ 60──────> ENRAGED
                        │   ──threat within 9 m─>   │
                        └<──── 14 s without a hit ──┘

Fleeing is `FleeModule` at Override (30) out-ranking everything below it.
Enraging switches that module **off**, which is the whole mechanism: `ChaseModule`
(20) and `CloseCombatModule` (23) become the highest live modules and the same
creature closes and rams, with no second target system and no state threaded
through the stack.

`FleeModule` needs `fleeFromCurrentTarget`. Its default path resolves a threat by
faction relationship, and a Fauna creature is Neutral toward everything — the scan
returns null and he stands there being shot, with a clean console.

The roar is a **telegraph, not a flourish**. It fires on the transition into
ENRAGED, before the first charge, and `FightOrFlightModule` returns `StopAndFace`
for its duration so he actually holds still for it — it is the only warning the
player gets that something friendly has stopped being friendly
(`GDC-L1-ANIM-0003`). Do not move it to the moment of impact.

He roars with `SfxId.EntityAggro`, the generic creature aggro slot. There is no
Appa-specific FMOD event; adding one is an audio-side job, and an unmapped
`SfxId` is silent with a single warning.

## Rejected — an animated prop

He shipped first as scenery: no `AgentController`, faction, perception,
`NavMeshAgent` or `HealthComponent`, and a one-bool `IsWalking` controller. That
was the right scope for "put him in the world so I can see him" and it is gone
now — the promotion was additive exactly as predicted, and the rig needed no
new bones for it.

## Known

- **The tail sinks into the ground.** 0.67 m below the sole plane once placed.
  It is how the model is sculpted; fixing it means either editing the author's
  geometry or lifting Appa so his feet float. Neither was done.
- **Materials are the author's own**, not palette-linked, so Appa does not share
  the project's material vocabulary. Deliberate — converting the 16 would change
  how he looks. `appa_export.py` still calls `make_local()` in case one is ever
  linked in.
- **`SfxId` has no Appa entries.** He is silent.
- **Unity discards one self-intersecting polygon** in `Cube.016` on import. One
  poly, pre-existing in the sculpt, cosmetic.

## Rejected — art

**Double-sided hair materials.** The mane, shoulder fur, brow tuft and ears were
given `_Cull Off` copies of their materials via `DoubleSidedMaterials.Apply`, on
the assumption that hair is modelled as open sheets. It is not: all four are
**closed volumes, 0 boundary edges apiece**, measured rather than eyeballed. What
the flag actually did was let URP draw the interior of every lock, and URP —
unlike Blender's viewport — does not flip a back face's shading normal, so those
interiors lit black and won the depth test wherever locks interpenetrate. That is
the mottled dark-and-pale patchwork the author reported. It was also masking the
real cause, the mirrored-skin bug in Export above. Everything on Appa is
single-sided; `AppaBuilder` says so and why, so it does not get re-added a third
time.

**Loose fur tufts.** Ten generated clumps (`appa_fur.py`) were added along the
shoulders, neck, back, flanks and haunches as separate bone-parented objects,
so they could be moved and scaled by hand. The author did not like the look and
they were removed; the generator was deleted with them. If fur chunks are wanted
again, the shape is the thing to rethink -- the strands read as spikes rather
than hair -- not the placement or the parenting, both of which worked.
