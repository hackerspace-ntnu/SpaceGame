# Vrescal — build record

A tall, heavy desert quadruped. The head, jaw and eyes are the author's own
sculpt and are kept byte-for-byte; everything else — body, legs, rig and all six
animations — was rebuilt in Aug 2026, replacing the low sprawling
"sand-crocodile" the file used to hold.

    height   4.68 m at the front hump (5.35 m to the top of the head)
    length   8.31 m nose to tail tip
    belly    2.41 m of ground clearance
    width    2.05 m across the shoulders
    mesh     one skinned mesh, 63 k verts / 125 k tris in Blender

## What was wrong with the old one

Measured off the file before anything was touched:

| | |
|---|---|
| Body | a **single 42-vertex icosphere**, no subdivision |
| Dorsal armour | 14 stretched 42-vertex icospheres stacked like poker chips |
| Legs | four detached blobs with visible gaps at the sockets |
| Deformation | none — every piece rigidly parented to one bone |
| Head | carried at 0.9 m, below the player's waist |

The rigid parenting is the important one. Nothing could flex, so the walk was
four legs swinging under a solid box, and no amount of animation polish fixes
that. It is why the rebuild starts at the mesh and not at the clips.

## How the body is built

`vrescal_rebuild.py` lofts a tube of elliptical cross-sections — neck, trunk,
tail and all four legs — with every vertex carrying explicit bone weights
(`_buildlib.SkinPart`), subdivides it twice, and then hands it to
`vrescal_surface` for three passes:

  MUSCLES   ellipsoidal masses displaced along the normal
  FOLDS     the same machinery with negative strength, plus ring creases
  noise     low-amplitude turbulence

**The primary volumes live in the loft, not in the muscles.** This was learned
the hard way over five passes. A muscle blob strong enough to *be* the shoulder
inflates into a sphere stuck on the flank; run at a quarter of that strength
over a cross-section that is already wide there, the same blob reads as
anatomy. Same for the humps: raising `top` to make a hump widens the whole
ellipse and the hump comes out as broad as the animal. The humps are therefore
two narrow `MUSCLES` blobs sitting on an almost level back line — roughly half
the trunk's width, which is the proportion a camel's hump actually has.

Five things that each cost a full render cycle to find:

- **Clearing a parent does not keep the world transform.** The sculpt is
  bone-parented to the armature the rebuild deletes; capture `matrix_world`
  first and put it back, or the head lands 1.7 m off the centreline silently.
- **The neck must overlap the skull, not stop against it.** Ending at the rear
  face leaves daylight wherever the two surfaces curve apart.
- **A limb has to leave the body from inside the silhouette.** The shoulder and
  hip stations are the widest points on the animal for this reason alone.
- **Limbs must not splay.** Shoulder at y 2.05 and ankle at y 2.75 reads as a
  trestle; the y values now barely change down the chain.
- **Every local bulge on a limb costs more than it earns.** Four joint condyles
  plus a rippling `SEG_PROFILE` turned each segment into its own capsule. The
  joints read from the *zigzag* in `LIMBS` — elbow back, carpus forward, stifle
  forward, hock back — not from swellings.

### The armour mosaic, and why it is switched off

`vrescal_surface.plate_mosaic` builds the reference's cracked-scute armour as a
Voronoi tessellation *of the body surface itself* — each cell lifted into a
plate with a wall dropping back to the hide, gaps showing skin between. It
works, it deforms perfectly (the plates are welded into the same skinned mesh
and inherit their vertices' weights), and it is currently **not called**.

It was switched off because a scale field over a form that is not yet right
hides the form rather than decorating it: every judgement about the silhouette
got harder to make with 400 plates on top of it. Shape first. Re-enabling it is
two calls in `main()`, once the body underneath is worth armouring.

## Rig and animation

32 bones: root, pelvis, three spine, four neck, head, jaw, five tail, and
Upper/Lower/Cannon/Foot per limb. **Weights come from arc position along the
bone chain, not 3-D distance** — on a body whose radius exceeds the spacing
between its spine bones, a proximity solve gives a belly vertex a third of three
bones each and the trunk turns to rubber.

`vrescal_anim.py` **solves** every frame rather than posing it. Body motion is a
stack of sinusoids, feet are placed on an explicit gait schedule, and the legs
are solved backwards from the feet with closed-form two-bone IK. No constraints,
so nothing needs baking and the result is identical every run.

| Action | Frames | Loops | |
|---|---|---|---|
| `Vrescal_Idle` | 91 | yes | breathing, weight shift, head scan, tail — four periods that do not divide |
| `Vrescal_Walk` | 37 | yes | lateral-sequence walk, duty 0.72, 1.6 m/s |
| `Vrescal_Run` | 25 | yes | **amble** — ipsilateral pair 0.12 apart, duty 0.48, 4.2 m/s |
| `Vrescal_Attack` | 40 | no | rocks back on the hind legs, drops the mass forward, jaw snaps |
| `Vrescal_Hurt` | 20 | no | flinch onto the forelegs, head recoils |
| `Vrescal_Death` | 64 | no | legs splay, body settles, holds the corpse pose |

Measured foot slide through both gaits: **0.0 mm**. That is the whole point of
solving rather than posing — a planted foot is given a fixed world position for
its entire stance and the leg bends to whatever the body does above it, so
contact is exact by construction rather than by interpolation luck.

Three traps the animation hit, all silent:

- **`aim()` must carry the rest roll.** Building a fresh orthonormal frame from
  a reference vector gives a bone that is twisted about its own axis even when
  pointed in its rest direction. Invisible on a circular leg shaft; on the foot
  it rotated a pad hanging 1.4 units below the ankle and drove a toe 0.2 m into
  the sand in *every clip*. Fixed by taking the shortest arc from the rest
  direction and applying it to the whole rest matrix.
- **Foot roll pivots on the contact edge, not the ankle.** Rolling about the
  ankle swings the pad through the ground; the ankle has to rise by exactly
  enough to keep the lowest point of the rotated sole on the sand. That rise
  *is* the heel lifting.
- **The rest pose stands at 99.5 % leg extension**, which leaves IK no headroom
  at all. Every locomotion clip drops the root by `CROUCH` first.

Looping clips bake one extra frame identical to their first, so Unity's range
closes the cycle with no hitch. The frame numbers are the contract with
`VrescalBuilder.Clips`; `vrescal_anim.py` prints them on every run.

## Components

New, in `components/organic/`:

| Component | Variations | Used here |
|---|---|---|
| `foot_pad.blend` | `Round4Toe`, `Broad3Toe`, `Splayed5Toe`, `Cloven_Heavy` | 2 of 4 |
| `scute_plate.blend` | `Cracked_Hex`, `Pebble_Round`, `Keeled_Ridge`, `Shard_Angular`, `Spike_Low`, `Spike_Tall` (12 meshes, two seeded ×4) | 0 of 6 — see mosaic note |

`foot_pad` is the graviportal counterpart to the existing `foot_splayed`: a
compression pad rather than a gripping manus. `scute_plate` was built for the
scatter approach that the mosaic replaced; it is kept because it is a genuinely
reusable set of armour scales and nothing else in the library has any.

Palette additions: `Mat_Hide_Dune_Tan`, `Mat_Hide_Slate_Teal`,
`Mat_Hide_Scute_Umber`. The sculpt's `Mat_Hide_Sand_Pale` slots are remapped
onto Dune_Tan — a slot swap, no geometry touched — because a saturated
orange-yellow head on a desaturated body reads as a different animal's head
grafted on.

## Pipeline

    blender --background vrescal.blend --python vrescal_rebuild.py   # one-shot, destructive
    blender --background vrescal.blend --python vrescal_anim.py      # re-runnable
    blender --background --python vrescal_export.py                  # -> Assets/.../vrescal.fbx
    # then in Unity: Tools > Creatures > Build Vrescal Prefab

`vrescal_rebuild.py` refuses to run against a file it has already rebuilt, and
refuses to run against anything that is not the pre-rebuild Vrescal. Restore
from `_backups~/vrescal_before_rebuild_2026-08-15.blend` to start over.
`vrescal_anim.py` imports `vrescal_rebuild` for its geometry constants, which is
why that module's `main()` is behind an `if __name__` guard.

`vrescal_export.PIVOT` and `vrescal_rebuild.PIVOT_X` are the same number twice —
where `Bone_Root` sits — and must agree or the root bone is not at Unity's
origin. `SHIP_LENGTH / SCULPT_LENGTH` encodes the 0.2759 export factor; it is
unchanged from the old model, which is what keeps the author's skull at exactly
the size it has always been.

## Unity side

Verified on the built prefab (instantiated without forcing its transform):

    root scale      (1, 1, 1)          -- no x27.59 compensating-transform trap
    world bounds    2.43 x 5.35 x 8.42 m
    renderer        1 SkinnedMeshRenderer
    fwd -> local    (0, 0, 1)          -- SpeedY reaches the blend tree correctly
    collider        2.05 x 4.70 x 5.00 @ (0, 2.35, 0.30)
    agent           radius 1.15, height 4.70

`AgentAnimatorDriver` derives forward speed from
`animator.transform.worldToLocalMatrix.MultiplyVector(velocity).z`, so that
`fwd -> local` reading is the check that matters: on the rotated model child it
comes out `(0, 0, 0)` and the blend tree never leaves Idle.

The blend-tree thresholds (walk 1.6, run 4.2 m/s) are **not free numbers** —
`vrescal_anim.py` derives its stride lengths from them so a planted foot travels
backwards at exactly the speed the agent moves forwards. Change one and the feet
skate until the Blender side is re-run.

## Known and not done

- **150 k triangles in Unity** (125 k in Blender; Unity splits verts at UV and
  smoothing seams). That is heavy for anything that spawns more than once.
  `SUBSURF = 1` in `vrescal_rebuild.py` takes it to roughly 35 k and is a
  one-line change.
- The agent is 1.15 m in radius and needs 2.3 m of clearance. It will not path
  between settlement buildings; the NavMesh may need a second agent type.
- Nothing spawns it. There is still no wildlife spawn table in the project.
- No `NetworkObject`, matching every other creature prefab. No audio — the
  `PerceptionModule` and `CloseCombatModule` FMOD `EventReference`s are empty.
