# Lightning Conjurer — rig build record

Source: `../ConjuringRobot1 (2) (1) (1).blend`
Pre-rig backup: `../_Backup~/ConjuringRobot1.pre-rig.blend`
Export: `../LightningConjurer.fbx`

## What was wrong

The model had never been assembled into a rig. Specifically:

| Symptom | Cause |
|---|---|
| Parts don't follow the joints | No body mesh had a vertex group or an armature modifier. Only the finger meshes were attached to anything. |
| "Rotation is messed up" | The `Legs` armature object carried `rot Y = 180°` and `scale 1.8385`. Anything parented under it inherited a flipped, 1.84× transform. |
| No single skeleton | Three partial armatures — `Armature` (18 bones) and `Armature.001`/`Armature.002` (14) were two *hand* rigs; `Legs` (9) was a leg rig. No spine, no root, no connection between them. |
| Flipped shading | Negative scale on `RightLowerLeg` (y −0.74), `Cube.007`/`Cube.013` (z −0.12), `Icosphere`. |

## The rig

One armature, `ConjurerRig`, **at the world origin with an identity transform** — 19 bones
as built by `rig.py`, later 40 (walkerize.py's leg pins and hinges, then hands.py's
right-hand fingers):

```
Root
└─ Hips
   ├─ Spine ─ Head ─ Halo
   ├─ Thigh.L ─ Shin.L ─ Foot.L
   ├─ Thigh.R ─ Shin.R ─ Foot.R
   └─ ArmRoot.{L,R} ─ UpperArm ─ Forearm ─ Hand     (the free-floating arms)
```

Binding is **rigid bone-parenting**, not skinning: every one of the 52 parts is
100% attached to exactly one bone. That is the right answer for a mech — it needs
no weight painting and it never smears a hard edge — and it means *no mesh data
was touched*. All 73 meshes are bit-identical to the pre-rig backup (verified by
comparing world-space bounds of every vertex, max drift 1e-5).

The floating arms are rigged as floating. They sit at y = ±6.4, exactly symmetric
about the body centre line, so they are detached by design rather than broken.

### Two traps worth remembering

**Bone parenting anchors at the bone TAIL.** The parent matrix therefore carries a
`+Y` translation of `bone.length`. To keep an object exactly where it was:

```python
o.matrix_parent_inverse = P.inverted()   # P = bone.matrix_local @ Translation((0, len, 0))
o.matrix_basis = original_world_matrix   # NOT "leave it alone"
```

Leaving `matrix_basis` alone only works for objects that had no parent before.

**Detaching an action does not reset the pose.** Pose bones keep whatever the last
keyframe left behind — `Walk`'s final frame parks `Shin.R` at −35.7° — and the FBX
bind pose is captured from the *current evaluated state*. Zero `pb.matrix_basis`
explicitly before exporting or that bent knee becomes the rest pose.

## The fingers

`rig.py` bone-parents all 52 parts rigidly, which is the right answer for a mech and
the wrong one for a hand that has to close. The model shipped with two legacy hand
rigs — `Armature` (right, 18 bones) and `Armature.001` (left, 14) — which `rig.py`
parked in `WIP_Spares` rather than deleting, precisely so this stayed recoverable.

`hands.py` lifts the **right** one's bones into `ConjurerRig`: four fingers of
metacarpal + 3 phalanges, renamed `Meta{1..4}.R` / `Finger{1..4}{A,B,C}.R`, plus
`CastSocket.R` in the middle of the palm for the spell to charge in. The mesh
`Hand.001` moves off its bone parent onto a plain object parent with its armature
modifier re-pointed at `ConjurerRig` — bone-parenting *and* skinning to bones under
the same wrist would apply that wrist's motion twice.

Only the right hand. The left is thirteen separate meshes and `Armature.001` is
missing every metacarpal plus two orphan bones, so it keeps rigid parenting and just
aims. Giving it fingers means reconstructing those metacarpals first.

## Animation

30 fps. Idle and Walk are cycles whose last frame duplicates the first, so they loop
seamlessly. Attack is a one-shot, neutral to neutral.

- **Idle** — frames 1–120 (4.0 s). Hover: body breathes, arms drift out of phase,
  halo turns 90°.
- **Walk** — frames 1–73 (2.4 s). Thighs ±24°, knees bend on the back-swing,
  feet pitch, pelvis rolls, body drops onto each contact.
- **Attack** — frames 1–90 (3.0 s). Right arm comes up and rolls palm-to-sky, fingers
  close into a cup, holds and trembles while the halo spins up, then opens as the body
  drives forward. Left arm points 28° below horizontal down the model's +X.

  The 3.0 s is load-bearing: `ConjurerCastModule` times its strike off
  `LightningConjurerBuilder.CastSeconds`, which is derived from this frame count.
  Re-time the clip and the bolt lands against the wrong frame.

  The left arm points at **nothing in particular** — a baked clip cannot aim. The
  module claims `IFacingModule` for the whole cast so the *body* tracks the target and
  the authored forward-point lands on it.

The halo turns 90° per loop, not 120°, because the cube has 4-fold symmetry — 120°
would visibly pop at the seam.

**Not foot-locked.** There is no IK holding a contact to the ground, so the ideal
no-skate ground speed is a calculation rather than a guarantee:

```
leg  = (hip 25.42 − ankle 5.10) × scale = 5.30 m
step = 2 × leg × sin(24°)               = 4.31 m
speed = 2 × step / 1.333 s              ≈ 6.47 m/s
```

`LightningConjurerBuilder.StrideSpeed` recomputes this from the constants. When
this creature gets a motor, set `AgentAnimatorDriver.animatorSpeedScale =
groundSpeed / 6.47`.

## Scale and orientation

Height **9.06 m** — "3× the player model", where the player (`AstronautArmature`)
measures 3.019 m to the top of the head (confirmed two ways: skeleton
`HeadTop_End` at y 3.019, mesh bounds 3.024). Scale factor 0.2607607, applied via
`ModelImporter.globalScale`.

The model faces Blender **+X**, which lands on Unity **+X**. The prefab's model
child is yawed −90° so the prefab *root's* forward is the creature's forward.

**The armature is deliberately left at identity in the FBX.** For a bone-parented
(non-skinned) rig Unity discards the armature node's own transform, so a rotation
or scale parked there survives in the animation curves but vanishes from the bind
pose — the creature stands correctly only while a clip plays. `GolemBuilder.cs`
documents hitting exactly this. So: export Blender's native Z-up axes untouched,
let `bakeAxisConversion` do the conversion, put metre scale on `globalScale`, and
put the yaw on the prefab child.

## Nothing was deleted

36 loose work-in-progress parts — a spare head dome, an older arm set parked at
y = 38/−21, the staff at y = −48, and the three legacy armatures — were **moved**
into a `WIP_Spares` collection so they stay out of the export. They are all still
in the file. `Cylinder` was left where it was, in an unlinked collection.

The legacy hand rigs (`Armature`, `Armature.001`) are kept intact; only the finger
*meshes* were re-parented onto `Hand.L`/`Hand.R`. Their now-redundant armature
modifiers are muted, not removed — they were a double transform on top of the bone
parenting, which is where a pre-existing sub-millimetre wobble on four finger
meshes came from.

## Rebuilding

```bash
BLENDER="/c/Program Files/Blender Foundation/Blender 5.1/blender.exe"
BLEND="../ConjuringRobot1 (2) (1) (1).blend"
"$BLENDER" -b "$BLEND" -P rig.py       # armature + binding        (refuses to run twice)
"$BLENDER" -b "$BLEND" -P walkerize.py # leg naming, pins, hinges  (safe to re-run)
"$BLENDER" -b "$BLEND" -P hands.py     # right-hand fingers + socket (safe to re-run)
"$BLENDER" -b "$BLEND" -P anim.py      # Idle + Walk + Attack      (safe to re-run)
"$BLENDER" -b "$BLEND" -P export.py -- ../LightningConjurer.fbx
```

`_Backup~/ConjuringRobot1.pre-hands.blend` is the file as it stood before the fingers
were merged.

Then in Unity: **Tools > Creatures > Build Lightning Conjurer**.
