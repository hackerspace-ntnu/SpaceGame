# Golem — build record

A hunched, knuckle-walking stone construct. It arrived as a **raw, unrigged
kitbash**: 30 loose boxes with no armature, no names, no materials and no
animation. This record covers what the boxes turned out to be, the skeleton
built onto them, the six clips authored on that skeleton, and the four separate
export traps that had to be found before it would land in Unity correctly.

## Files

| | |
|---|---|
| `golem_source.fbx` | The artist's untouched kitbash. **The only copy** outside git history — it came in on `main` as `Assets/Game/Art/Models/Creatures/golem.fbx`, and the shipping FBX now occupies the path it used to live at. |
| `golem.blend` | Source of truth from here on. |
| `golem_rig.py` | One-shot converter: source FBX → rigged `.blend`. Refuses to overwrite without `-- --force`. |
| `golem_anim.py` | Authors the six actions. Refuses to run if actions already exist. |
| `golem_export.py` | `.blend` → `Assets/Game/Art/Models/Creatures/Constructs/Golem/golem.fbx`. Re-runnable; never writes to the `.blend`. |
| `Assets/Game/Editor/Creatures/GolemBuilder.cs` | Importer settings, clips, animator controller, faction row, prefab. Re-runnable from **Tools > Creatures > Build Golem Prefab**. |

Pipeline order: `golem_rig.py` → `golem_anim.py` → `golem_export.py` → the
menu item.

## What the 30 boxes turned out to be

Nothing in the file said. Every object was `Cube.032` … `Cube.062` (`Cube.044`
was already missing), every one had `parent = None`, and the names carry no
anatomy at all. The assembly was identified by rendering orthographic views and
reading world-space vertex bounds, and it is unambiguous — a coherent creature,
not a pile:

| Group | Pieces | Notes |
|---|---|---|
| Head | 1 | 640 polys, the most detailed piece in the file, with a recessed face. The furthest-forward geometry. |
| Torso boulder mass | 12 | One 7.4 × 6.5 × 4.6 core plus packing rocks. |
| Pelvis | 1 | |
| Arms | 4 + 4 | Pauldron, upper, forearm, fist — the longest chains in the model. |
| Legs | 3 + 3 | Thigh, shin, foot. Short and stubby. |
| Unpaired | 2 | Extra plates on the golem's **left** forearm and fist. The kitbash is deliberately asymmetric there. |

Two facts set the whole design:

1. **The fists sit on the ground.** The lowest vertex in the file is the
   underside of a fist, at z = −1.1268; the soles are 0.14 above that. The rest
   pose is a **four-point stance**, so the rig honours it rather than standing
   the creature up. It is animated as a knuckle-walker throughout.
2. **The legs are short.** The hip sits 5.48 units above the sole plane on a
   body 10.62 units tall, and at rest the hip-to-ankle run is within 0.001
   units of a completely straight leg. That single number drives the crouch in
   every locomotion clip and caps the stride — see below.

Geometry housekeeping: **11 of the 30 pieces had a negative determinant** (the
worst was −6.29) and were therefore inside out. Rotation and scale are applied
into the mesh data and normals recalculated. This does not move a vertex in
world space: the rigged `.blend` matches the artist's original to **4 × 10⁻⁶
units**, verified by comparing every piece's bounds against a fresh import of
`golem_source.fbx`.

## Skeleton

19 bones, every position measured from piece bounds rather than guessed.

    Bone_Root                    on the ground, midway between the fists and feet
      Bone_Hips                  hip line, y = -7.93, z = 3.30
        Bone_Spine
          Bone_Chest             shoulder line, y = -3.14, z = 6.69
            Bone_Head
            Bone_Clav_{R,L}
              Bone_UpArm_{R,L}
                Bone_LoArm_{R,L}
                  Bone_Hand_{R,L}
        Bone_Thigh_{R,L}
          Bone_Shin_{R,L}
            Bone_Foot_{R,L}

**Rigid bone parenting, not skinning.** The golem is 30 separate hard rocks;
smooth weights would stretch boulders across every joint, which is the opposite
of what stone should do. The consequence lands in Unity: bone-parented meshes
are real child transforms, so `GolemBuilder.ConfigureImporter` must keep
`optimizeGameObjects` and `optimizeBones` **off**, or the importer deletes the
transforms the clips animate and the creature arrives as a motionless heap.

The arms and the legs mirror about **different planes** (−20.130 and −20.299).
That is what the geometry does, and using one averaged plane would leave every
limb bone slightly off its own rocks.

## Animation

Six actions, all **in place** — `NavMeshAgentMotor` owns movement, and the
prefab keeps `applyRootMotion = false`.

| Action | Frames | Loop | |
|---|---|---|---|
| `Golem_Idle` | 120 | yes | settling, a slow weight rock between the fists |
| `Golem_Walk` | 36 | yes | four-point lateral-sequence lumber |
| `Golem_Run` | 26 | yes | two-beat bound: both fists slam, body vaults, both feet land |
| `Golem_Attack` | 48 | no | sits back, winds both arms up, drives both fists into the ground |
| `Golem_Hurt` | 22 | no | a shock travelling up the stack of rocks, head last |
| `Golem_Death` | 72 | no | legs buckle, falls onto its fists, arms fold, dead hold for the last 14 frames |

Loop clips make frame *N* an exact copy of frame 1; `GolemBuilder` then slices
one frame short (`Last = 119, 35, 25`) so the shared pose is not held for two
frames every lap.

### IK, and why it is baked

Four contact points have to stay welded to the ground while the body rides over
them. As FK joint angles that is guesswork; as "this contact is at this point in
armature space on this frame" it is exact, and the **settle on each footfall**
— the body dropping after the contact lands, which is the single thing that
makes the creature read as stone — comes out of it for free.

FBX cannot carry a Blender IK constraint, so the chains are solved
**analytically at authoring time** and written out as bone rotations. That is
what a constraint-plus-bake would produce, without the operator-context
problems `blender --background` has. `golem_anim.py` asserts the solver lands on
its targets before authoring anything; it currently reports **3 × 10⁻⁶ units**
of error under a loaded pose.

### Stride, and the speeds in GolemBuilder.cs

Because the rest leg is essentially straight, it has no horizontal budget at
all, so every locomotion clip crouches first. The stride is then bounded by
reach, and the blend-tree thresholds follow from it rather than from taste:

    speed = 2 * half_stride / (duty * cycle_seconds)

| | half stride | duty | frames | speed |
|---|---|---|---|---|
| Walk | 1.66 | 0.72 | 36 | **0.97 m/s** |
| Run | 2.30 | 0.35 | 26 | **3.86 m/s** |

The run is quick because it is a bound: two flight phases per cycle mean the
contacts only have to track the ground for 35% of it. `golem_anim.py` prints
both figures every run — **if you change a stride or a duty factor, copy the new
numbers into `GolemBuilder.WalkSpeed` / `RunSpeed`, or the golem moon-walks.**

`golem_anim.py` also fails the build if a locomotion clip ever asks a limb past
its reach, because a clamped limb has gone straight and stopped tracking the
ground. The one-shots are allowed to clamp — a slam looks like a straight arm —
and report rather than fail.

## The four export traps

All four were found by measuring the result in Unity, and every one of them
fails **silently**. They are why `golem_export.py` is longer than
`vrescal_export.py`.

1. **`bound_box` lies about size.** It returns the eight corners of an object's
   *local* AABB; most of these rocks carry a rotation, so transforming those
   corners to world space and taking min/max inflates the model. It measured
   12.09 units tall against a true 10.62, and the golem would have shipped 12%
   short. Measure vertices.

2. **Unity keeps only the armature node's scale.** Putting the placement
   (pivot, yaw, ship scale) on the armature *object* — which is what
   `vrescal_export.py` does — produced a prefab whose root read
   `localScale = 24.48` with the visible golem standing **five metres from its
   own collider and NavMeshAgent**. The translation and the rotation were both
   discarded. `bake_placement()` pushes the placement into the bones, the mesh
   data and the object offsets instead, and the armature ships at identity.

3. **The axis conversion is a root rotation too, so it is discarded as well —
   but only for the bind pose.** This one took four measured attempts:

   | Export | Bind pose | Clips |
   |---|---|---|
   | `axis_up='Y'` (exporter converts) | on its back | on its back |
   | as above + `bake_space_transform=True` | on its back | on its back — byte-identical |
   | `axis_up='Z'` + Z→Y rotation baked into the data by hand | **upright** | on its back, rotated twice |
   | `axis_up='Z'`, no manual rotation | on its back | **upright** |

   Unity converts the *animation curves* itself, from the FBX header, but the
   bind pose's share of the conversion lives on the armature node — which it
   discards. So no export setting alone can satisfy both. The resolution is on
   the Unity side: ship the data in Blender's own Z-up axes with **no rotation
   at all** in `place`, and set **`ModelImporter.bakeAxisConversion = true`** in
   `GolemBuilder.ConfigureImporter`. Unity then converts bind pose and clips
   together.

   Two consequences worth knowing. `place` carries no yaw either — with
   `bakeAxisConversion` the golem's source +Y lands on Unity's +Z by itself, and
   adding the "obvious" 180° left it facing backwards *and* mirrored. And the
   Blender-axis bounds the export prints can no longer be hand-mapped to Unity
   axes reliably; the prefab's collider figures were measured off the built
   prefab, which is what the script now tells you to do.

4. **`Armature.transform()` does not refresh the depsgraph.** After it,
   `object.matrix_world` keeps handing back the *old* bone positions. Assigning
   `matrix_world` to re-seat the rocks silently left 27 of 30 of them 1.2 m
   adrift, and a bounds check built on `matrix_world` reported 26 m of drift
   that was not there. `golem_export.py` composes rest-pose world matrices from
   data instead (`rest_world`), and re-seats each rock with the identity that
   needs no depsgraph at all: `basis_new = Translation(f · loc_old) · Rot_old`.

   The same staleness reaches the FBX exporter, which bakes the animation takes
   from the *evaluated* armature: the bind pose came out right and every clip
   came out carrying the pre-bake orientation. `view_layer.update()` does not
   clear it. The export therefore saves the baked file to a scratch path and
   **reopens it** before exporting, which is the one thing guaranteed to
   rebuild the depsgraph from data. It never writes to `golem.blend`.

Two further notes:

- **Pose-bone locations are in armature units.** Baking the ship scale into the
  bones turns those units into metres, so `Bone_Root`'s location F-curves are
  multiplied by the same factor — 603 keys. Without it the crouch, every
  footfall settle and the whole death collapse would be four times too deep.
- Blender 5.1 dropped `Action.fcurves`; curves now live under
  layers → strips → channelbags. `action_fcurves()` walks whichever exists.

`bake_placement` asserts the whole point cloud lands where `place` says it
should. It currently reports **1 × 10⁻⁶ m**.

## Verified

- Rigged `.blend` matches the artist's raw FBX to 4 × 10⁻⁶ units, per piece.
- Exported FBX re-imported into Blender: 1 root (no parent empty), 19 bones with
  the expected parenting, 30 meshes on the expected bones, 6 takes named
  `Arm_Golem|Golem_*` with the expected frame ranges, 3 materials. 16 of 3174
  faces fail a centroid-outwardness test, which is the concave brow of the head
  piece, not flipped normals.
- Rendered before and after rigging, plus sample frames of walk, run, attack and
  death: no piece left at the origin, no chunk detached, no inside-out rock.
- In Unity, measured off the built prefab: true vertex bounds
  2.60 × 2.57 × 2.51 m centred (−0.02, 1.29, 0.07) with the soles on y = 0,
  head at +Z, back hump at −Z, the golem's right foot at +X (not mirrored), and
  **zero** negative-determinant transforms. The `BoxCollider` is those exact
  numbers.
- Every clip sampled at six points through its length on the built prefab: all
  six stay upright, the top of the back stays at 2.3–2.6 m, and nothing leaves
  a 2 m radius. Feet dip below y = 0 by at most 12 cm in the locomotion clips
  (a foot corner rolling through the toe-off) and 35 cm in `Golem_Death`, which
  is the collapse.
- Importer reads Generic / `bakeAxisConversion` on / `optimizeGameObjects` off /
  `optimizeBones` off, the avatar is valid, and the six clips import at the
  right lengths with the right loop flags.
- Prefab carries all ten `AgentAnimatorDriver` parameter names verbatim and the
  four states Locomotion / Attack / Hurt / Death; 50 transforms, 30 renderers,
  `applyRootMotion = false`.

## Open

- **The palette has no stone family.** All 35 entries are Emissive, Fabric,
  Glass, Hide, Metal, Neutral, Paint, Plastic or Wood; the only non-metallic
  greys are an interior wall panel and two Fabric entries. Rather than paint a
  boulder with a material called `Mat_Fabric_*` — a lie the library index would
  then repeat — `golem_rig.py` authors three locally: `Mat_Stone_Pale`
  (`#A9A296`), `Mat_Stone_Shadow` (`#6A655C`) and `Mat_Stone_Grit` (`#4A463F`),
  all non-metallic and rough. **This is the only model in the library that does
  not link its materials from `palette.blend`.** They should be promoted into
  the palette by whoever owns that file; it was left alone here because it was
  being modified by a concurrent session. Once promoted, swap the
  `make_materials()` call for `link_materials()`.
- **`Mesh_Golem_Torso_Core` cannot bend.** It is one boulder spanning y −9.41 to
  −1.66, pelvis to head, and it is bone-parented to `Bone_Spine`. Spine and hip
  flex are capped near 6° for that reason; past that a seam opens between it and
  the rocks around it. A heavy construct should barely flex anyway, so it reads
  as intent — but splitting that boulder is the real fix, and it means cutting
  the artist's geometry.
- **The death pose still clamps one arm.** `Golem_Death`'s final stage asks the
  left `Bone_UpArm` about 0.35 m past its reach, so that arm goes straight
  instead of folding. It is a dead golem lying on its face, so it reads fine,
  but the target could be pulled in.
- The prefab root carries `localScale = 100`. That is the FBX's centimetre unit
  factor cancelling the mesh data, not an error — the measured world size is
  correct — but it is worth knowing before anyone "fixes" it.
- `VrescalBuilder`/`vrescal_export.py` do **not** carry any of trap 2, 3 or 4's
  workarounds. Either the Vrescal is affected too and nobody has measured it, or
  something about its rig makes it immune. Worth checking; it was out of scope
  here.
- Not verified in Play mode: the golem has never actually walked. The clips,
  controller, blend tree and prefab wiring are verified as assets only.
