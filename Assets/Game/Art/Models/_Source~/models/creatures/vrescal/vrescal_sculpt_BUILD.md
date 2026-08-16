# Vrescal sculpt — build record

`vrescal_sculpt.blend` — the concept art of the Vrescal put through an
image-to-3D conversion (Tripo), imported and conditioned for the library by
`vrescal_sculpt.py`.

    size     4.25 m long x 1.91 m wide x 3.85 m tall (front hump)
    belly    1.36–1.47 m of ground clearance
    mesh     one watertight shell, 22 470 verts / 44 944 tris, all triangles
    material one baked PBR set, 2048² base colour / normal / roughness / metallic
    rig      none

## Source

    ~/Downloads/dinosaur+creature+3d+model/tripo_convert_7aa16cef-…fbx

Re-runnable only against that FBX, and only as a **one-shot**: once the .blend
exists it is the source of truth and `save()` refuses to overwrite it without
`--overwrite`.

    blender --background --python vrescal_sculpt.py -- --overwrite

## What the import has to fix

Four things, none of them optional:

**The mesh contained a human.** The conversion was run against the concept
painting, which has a scale silhouette standing beside the animal, and the
reconstruction rebuilt that too — a second loose shell, 5 262 tris, 0.39 units
tall, at the head end. Separated by loose parts and deleted.

**It arrived unit-scaled**, 0.857 units tall, and **nose-first along −X** while
every other script for this creature takes +X as forward. Turned 180° about Z
and scaled, on the *mesh data* rather than the object, so the object transform
stays identity and no compensating scale reaches Unity.

**Its textures lived in `~/Downloads`.** Packed into the .blend; the file would
otherwise break the first time that folder is cleaned out.

**One pinhole**, four boundary edges near the top of the front hump at
(0.80, 0.07, 3.68). Invisible in a render, but it breaks solidify, boolean and
volume operations and would have surfaced later as a rigging artefact. Filled
and triangulated; the shell is now watertight, 0 boundary and 0 non-manifold
edges.

## The scale figure is the calibration

Rather than guessing a factor, the human is measured before it is discarded: it
is **0.390 units tall**, and a concept-art scale figure is taken as **1.75 m**.
That fixes the factor at **4.4877** and makes the animal 3.85 m at the front
hump.

That number is worth trusting because it was arrived at twice, independently.
Reading the concept art directly — a 440 px silhouette against a 950 px animal,
so 1.75 m against 3.78 m — had already put the front hump at 3.78 m before this
model existed. The two agree to within 2 %.

The consequences are worth stating plainly, because they are gameplay facts:

- The animal is **just under 4 m at the hump**, a bit over twice player height.
- **A player cannot walk under its belly** — 1.4 m of clearance against 1.75 m
  of player.
- It is **1.91 m wide**, so it has the same NavMesh problem the previous build
  had: roughly a 1 m agent radius, and it will not path between settlement
  buildings without a second agent type.

`HUMAN_H` at the top of the script is the single number all of this hangs off.

## Materials — the one palette exception

This is the only asset in the library that does **not** draw its colour from
`palette.blend`. Its entire appearance is in the baked 2048² maps; swapping them
for a flat palette colour would discard the model rather than conform it.

The material is renamed into the palette's scheme — `Mat_Hide_Vrescal_Baked` —
so it sorts with the other hide materials, and the images are renamed
`Tex_Vrescal_BaseColor` / `_Normal` / `_Roughness` / `_Metallic`. The FBX also
ships a combined `rm` map which nothing is wired to; it is not imported.

**Nothing else should copy this exception.** It is justified by baked texture
data, not by convenience.

## Rig and animation

`vrescal_rigged.blend`, built by `vrescal_rig.py` then `vrescal_sculpt_anim.py`.

    blender --background --python vrescal_rig.py -- --overwrite
    blender --background vrescal_rigged.blend --python vrescal_sculpt_anim.py
    blender --background --python vrescal_sculpt_export.py

32 bones — root, pelvis, three spine, four neck, head, jaw, five tail, and
Upper/Lower/Cannon/Foot per limb — matching the naming the existing solver and
`VrescalBuilder` expect. Bone-heat weights, **0 unweighted vertices**, no bone
driving more than 9.5 % of total weight, and every deform bone has influence.

### Why the rig file is not in metres

It is scaled by 3.6245 with the sole plane at z = -13, which is the space
`vrescal_anim.py` was tuned in. That solver produces all six clips with closed-
form IK and measured zero foot slide, and reusing it beat re-deriving it — but
only `stride` is expressed in metres-times-`UNITS_PER_M`. `lift`, `crouch`,
`bob`, `sway` and every literal inside the idle, attack, hurt and death frame
functions are in **raw working units**. Run it against a metre-scale rig and it
lifts each foot 1.15 *metres*.

Moving the rig into the space those literals assume is one line; patching
several dozen literals across four clip generators is not. The export scales
back to metres, so nothing downstream can tell.

The solver is reused *unmodified*. Its only coupling is a module-level
`import vrescal_rebuild as R` touching six names, so `vrescal_sculpt_anim.py`
installs a stand-in under that name in `sys.modules` before importing it.
Nothing on disk was edited.

### The stance is asymmetric

The sculpt is a reconstruction of a painting of an animal standing mid-stride.
Its hind feet are staggered by 0.73 m fore-and-aft and sit well inboard of the
fore pair:

    FrontP  x +0.80  y +0.68        FrontS  x +0.80  y -0.68
    RearP   x -0.34  y +0.29        RearS   x -1.07  y -0.22

Bones go on that real geometry regardless — weights have to follow the mesh that
exists, and a bone outside the limb it drives collapses the joint. The stagger
is fixed in the *gait scheduler* instead, by averaging each pair's rest ankle
into a symmetric pair (0.32 m per hind foot) while leaving the bind pose alone.
Ship the stagger and the two hind feet never share ground across a cycle — one
sweeps x -1.29..0.09 and the other -1.93..-0.55, and the animal reads as
dragging a leg.

One knock-on: the rest pose stands at essentially full leg extension, which is
why every locomotion clip applies `crouch` before solving. Idle does not crouch,
so its hind legs solve about 3 cm short and stand straight. Invisible, and the
alternative — forcing the bind pose symmetric — puts every hind-leg bone outside
its own mesh.

### A trap worth keeping

The first tail chain ran down to 0.74 m, but the tail tip is at 1.12 m.
`Bone_Tail_05` landed outside the mesh, drew no bone-heat weight at all, and
would have shipped as a joint that silently does nothing. Bone heat does not
warn; the check is per-bone influence, and `vrescal_rig.verify()` now reports it.

### Export

`vrescal_sculpt_export.py` → `Assets/Game/Art/Models/Creatures/Organic/Vrescal/vrescal.fbx`,
3.48 MB, verified by re-importing: 32 bones, mesh armature-parented with 31
vertex groups, six takes, 4.25 x 1.91 x 3.85 m with the feet on z = 0.

Two differences from the older `vrescal_export.py`. The scale factor is
**declared** (`1 / UNITS_PER_M`) rather than derived from two hand-measured
lengths that can silently disagree with the file. And the pivot is **imported
from `vrescal_rig`** rather than written twice — the old script's own comment
warns that the two disagreeing puts the root bone off Unity's origin.

| Action | Frames | Loops |
|---|---|---|
| `Vrescal_Idle` | 1–91 | yes |
| `Vrescal_Walk` | 1–37 | yes — 1.38 m stride at 1.6 m/s |
| `Vrescal_Run` | 1–25 | yes — 1.61 m stride at 4.2 m/s |
| `Vrescal_Attack` | 1–40 | no |
| `Vrescal_Hurt` | 1–20 | no |
| `Vrescal_Death` | 1–64 | no |

## Not done

- **The prefab's collider and agent are one rebuild behind.**
  `VrescalBuilder` hardcodes them (a bounds-derived attempt once produced a
  45 x 40 x 119 m collider, so the numbers were pinned by hand), and they were
  still the previous, larger animal's: a 2.05 x 4.70 x 5.00 box and a 1.15 m /
  4.70 m agent, against a model that is 1.91 x 3.85 x 4.25 m. In game that
  reads as being blocked by nothing. The constants are **fixed in the builder**
  — 1.95 x 3.85 x 3.60 and 1.05 m / 3.85 m — but the prefab on disk was built
  before that edit and needs one more `VrescalBuilder` run to pick them up.
  `Assets/Game/Editor/Creatures/VrescalAutoBuild.cs` is staged to do it on the
  next domain reload and then delete itself, writing
  `Logs/vrescal_build_report.txt`. Failing that: **Tools > Creatures > Build
  Vrescal Prefab**.
- **Not decomposed.** One shell. The per-body-part `.blend` files under
  `parts/` are from the separate from-scratch build (see below) and are not
  cut from this sculpt.
- **All triangles**, so it does not subdivide cleanly and is awkward to edit by
  hand. A retopology pass would be needed before anyone sculpts on it.
- **Not exported.** `vrescal_export.py` targets the old build's scale factor
  and pivot and has not been pointed at this file.

## The stylised variant

`vrescal_stylised.blend`, built by `vrescal_stylise.py`. Same mesh, textures
simplified to suit the project's art style. The sculpt file is not modified.

    blender --background --python vrescal_stylise.py -- --overwrite

|  | source | stylised |
|---|---|---|
| maps | 4 x 2048² | 3 x 1024² |
| texels | 16.8 M | 3.1 M — **5.4x less texture memory** |
| base colour | 53 054 distinct colours | 3 099, off a 26-cluster palette |
| base gradient energy | 0.0266 | 0.0160 |
| normal gradient energy | 0.0158 | 0.0078 |
| metallic | a 2048² map averaging 0.027 | deleted; shader input pinned to 0.0 |

Passes: mask-weighted 2:1 downsample, two median passes, k-means quantisation
to 26 colours at 82 % mix, contrast/saturation restoration, normal blur and
strength reduction, roughness posterised to 5 levels.

### The padding is the whole difficulty

This is a generated atlas — hundreds of small islands on flat light-grey
background — and **any filter run over it bleeds grey into every island edge**.
Because the islands are small and numerous, that shows up as a bright halo
along every UV seam on the model: dozens of them, and they read as a lighting
bug rather than a texture problem.

So nothing filters the raw image. The UV triangles are rasterised into a
coverage mask (57.4 % of the atlas), the downsample is mask-weighted so padding
contributes nothing, and the padding is then *replaced* by dilating island
colour outward — which also stops the lowest mips bleeding grey onto the animal
at distance. The padding is rebuilt a second time after quantisation, because
quantisation moves the colours it was derived from.

### Two settings that were wrong on the first attempt

Both are recorded because both looked plausible and neither was visible in the
numbers:

- **Roughness posterised onto a global 0/¼/½/¾/1 ladder turned the animal
  glossy.** Its roughness averages 0.636 and much of the surface sits just
  below the midpoint, so a global ladder rounded it *down* to 0.33. The ladder
  now spans the map's own 2nd–98th percentile, and the script asserts the mean
  survives the pass.
- **Normal blur 3.0 at strength 0.55 erased the cracked scutes**, which are the
  animal's signature — it removed the skin pores and the plate edges together.
  Blur is what removes detail below a screen pixel; strength is what flattens
  the plates. Now 1.4 and 0.85.

A third, milder one: the median filter pulls every pixel toward its local
median, which costs contrast and saturation as well as detail, and the animal
came out hazy. `CONTRAST` and `SATURATION` put back only what the smoothing
took.

All of this is tunable from the constant block at the top of the script.

## The low-poly cut — what actually ships

`vrescal_lowpoly.blend`, built by `vrescal_lowpoly.py` from the stylised mesh.
This is what `vrescal.fbx` now contains; the stylised file is no longer exported.

    blender --background --python vrescal_lowpoly.py -- --overwrite

|  | stylised | low-poly |
|---|---|---|
| mesh verts | 22 470 | **3 003** — 7.5x fewer |
| triangles | 44 944 | **6 010** — 7.5x fewer |
| shading | smooth | flat / faceted |
| maps | 3 x 1024² | **1 x 512²**, base colour only |
| texture on disk | 1.73 MB | **151 KB** |
| colours on the model | 3 099 | **12**, hard flat regions |
| palette mean saturation | 0.19 | **0.37**, hues unchanged |
| shipped FBX | 3.48 MB | 2.21 MB |

Normal and roughness maps are dropped outright — roughness becomes a scalar
0.68 on the shader, metallic stays pinned at 0. A photographic normal map
fights a poster-flat albedo, and the scute relief it carried is now in the
silhouette rather than in the shading.

### The two vertex counts disagree, and the flattering one is not the honest one

Faceted shading splits a vertex per adjacent face, so the GPU sees roughly
3 verts per triangle no matter how few mesh vertices there are:

    mesh vertices     22 470 -> 3 003     7.5x fewer
    triangles         44 944 -> 6 010     7.5x fewer
    GPU vertices     ~25 300 -> 18 030    1.4x fewer

The old mesh was smooth-shaded (44 914 of 44 944 polys), so its GPU count was
its vertex count plus its UV-seam splits. Triangles are what cost skinning,
fill and shadow passes, and those drop the full 7.5x — but anyone reading
"7.5x fewer vertices" off this file should know which number that is.

### Why the UV atlas is rebuilt rather than carried over

Decimating and keeping the existing UVs does not work here. The stylised atlas
has 99 islands and **12.5 % of its vertices sit on a UV seam**. Collapsing
across seams smears texture between islands; protecting them with a zero-weight
vertex group (which Decimate supports) cannot go below 2 811 verts and spends
the whole budget on dense island borders around sparse interiors.

So the mesh is decimated freely, Smart-UV-projected, and the colour is **baked
back off the high-poly** — the standard retopo-and-bake trade, and the only
option that gets clean topology *and* clean UVs. Verified faithful: island mean
`#565652` on the source against `#50544F` on the bake, a 4 % luminance
difference that is the 12-colour flattening rather than a bake error.

Smart UV Project's own packing filled only 17 % of the atlas — its
`island_margin` is an absolute UV distance and these islands are small enough
that a 6 px border costs more than the island encloses. Projecting at zero
margin and repacking separately gets 37.7 %.

### Two things that were wrong at first and are worth not repeating

- **`Image.pixels` is not linear for byte images.** It returns the stored
  buffer, already gamma-encoded, whatever the datablock's colorspace says. An
  earlier version wrapped the saturation pass in linear↔sRGB conversions; they
  cancelled, so the shipped PNG was roughly right, but the multiplier acted on
  doubly-encoded values and applied about half the boost it claimed — and the
  palette *printout* gamma'd a third time and reported washed-out hexes that
  did not match the file. The numbers lied before the image did.
- **Saturation goes on the palette, not on the image.** Boosting a continuous
  image and then clustering lets k-means average the boost back out. Boosting
  the K centres after they are found guarantees the shipped colours are exactly
  those K tones. The same applies to `VALUE_GAIN`: k-means puts every centre at
  its cluster *mean*, so a hard assignment discards both ends of the range and
  the sand tones come back olive unless the palette is lifted afterwards.

### Constraints the rest of the pipeline imposes

`vrescal_rig.py` skins with bone heat, which needs a **closed manifold**
surface — the sculpt's build notes record bones being dropped outright on the
pre-repair mesh with its single four-edge hole. `vrescal_lowpoly.py` therefore
asserts boundary, non-manifold and loose-vertex counts are all zero after
decimation and refuses to save otherwise.

Bone positions are hardcoded in metres in `vrescal_rig.py`, so they survive
decimation untouched; only `measure_feet()` reads geometry, and it still finds
67–149 verts under each foot. The rig came out with **0 unweighted vertices**
and every bone carrying influence.

### Pipeline

`vrescal_rig.py` and `vrescal_sculpt_export.py` take optional environment
overrides so the low-poly reuses the same verified rig and export path rather
than a copy of it. Unset, both behave exactly as before.

    VRESCAL_RIG_SRC=$PWD/vrescal_lowpoly.blend \
    VRESCAL_RIG_OUT=$PWD/vrescal_lowpoly_rigged.blend \
        blender --background --python vrescal_rig.py -- --overwrite
    blender --background vrescal_lowpoly_rigged.blend --python vrescal_sculpt_anim.py
    VRESCAL_EXPORT_SRC=$PWD/vrescal_lowpoly_rigged.blend \
        blender --background --python vrescal_sculpt_export.py

Verified on the shipped FBX by re-importing it: 3 003 verts, 6 010 tris, **0 of
6 010 polys smooth-shaded**, 32 bones, 31 vertex groups, six takes at 91/37/25/
40/20/64, one 512² map, 1.91 x 4.25 x 3.83 m with the feet on z = 0.

No Unity-side change was needed. The prefab nests the FBX as a prefab instance,
so mesh and material resolve live at import and the material rename
(`Mat_Hide_Vrescal_Stylised` → `Mat_Hide_Vrescal_LowPoly`) is transparent; the
FBX take names are byte-identical to the previous export, so `Vrescal.controller`
keeps its clips; and `VrescalBuilder`'s hardcoded collider (1.95 x 3.85 x 3.60)
and agent (r 1.05, h 3.85) still fit a 1.91 x 4.25 x 3.83 m animal.

The three `Tex_Styl_Vrescal_*.png` maps in `vrescal.fbm/` were deleted with
their `.meta` files. They were orphaned by this change — verified by GUID search
across `Assets/` **and** by checking the FBX binary for filename references,
which is the check that matters, because `materialLocation: 1` means embedded
materials resolve textures by filename and a GUID-only search would have missed
a live reference.

## The animal has six legs, and every rig before this one had four

`vrescal_hexapod_rigged.blend`, built by `vrescal_hexapod_rig.py` then
`vrescal_hexapod_anim.py` from the hand-edited `vrescal_lowpoly_rigged.blend`.
**This is what `vrescal.fbx` now contains.**

    blender --background --python vrescal_hexapod_rig.py -- --overwrite
    blender --background vrescal_hexapod_rigged.blend --python vrescal_hexapod_anim.py
    VRESCAL_EXPORT_SRC=$PWD/vrescal_hexapod_rigged.blend \
        blender --background --python vrescal_sculpt_export.py

Measured off the mesh, the six limbs sit at:

    FrontP  x +1.09  y +0.67      FrontS  x +1.09  y -0.66
    MidP    x +0.58  y +0.62      MidS    x +0.58  y -0.62
    RearP   x -0.56  y +0.29      RearS   x -1.17  y -0.22

Four at the front in two ranks, two at the back. `vrescal_rig.py` carries a
hardcoded four-entry `LIMBS` table inherited from the earlier *lofted*
quadruped, so the middle pair never got bones — in the sculpt, in the low-poly
cut, and in everything exported from either. Bone heat still weighted those
vertices, to whatever chain happened to be nearest, so the rig verified as
**"0 unweighted vertices" while two entire legs were being dragged along by the
front pair.** Per-limb ownership is now checked explicitly and every limb must
hold >80 % of its own weight; the middle legs came back at 99–100 %.

### Spatial clustering cannot count these legs, and that is what hid them

Single-link clustering on vertex positions chain-merges two limbs whose
*surfaces* pass within the threshold. The front and middle legs are 1.57 units
apart with a gap of about 0.57 between their surfaces, so any threshold loose
enough to hold one leg together also welds it to its neighbour — it reports four
limbs at every height and looks entirely convincing.

Connected components over the mesh's **own edges** cannot chain across empty
space. That reports six at every cut height from 1.43 m down, and it is what
`limbs()` uses. Ground-contact clusters are also not a leg count: each foot
lands as a separate toe and heel blob, which is a different way to get the wrong
answer.

### The gaits, and why the pretty ones do not work

| | walk | run |
|---|---|---|
| pattern | front+middle in unison, lateral sequence | same, two cycles per clip |
| duty | 0.72 | 0.48 |
| feet down | 4.3 | 2.9 |
| cycles per clip | 1 | **2** |
| stride | 1.38 m @ 1.6 m/s | 0.81 m x 2 @ 4.2 m/s |

    FrontP 0.00  MidP 0.00  RearP 0.90
    FrontS 0.50  MidS 0.50  RearS 0.40

**Each side's front and middle legs step in unison, and that is forced by
geometry, not chosen for looks.** Measured off this mesh: the two ankles are
1.75 working units apart, their soles need 1.60 units between centres before
the geometry overlaps, and each foot sweeps a 5.01-unit stride. The margin for
any phase difference at all is 0.15 units. A search over (pair offset, rear
offset) maximising the tightest gap returns **pair = 0.00** at every setting.

The first attempt used the textbook hexapod gaits and both were wrong here:

| phasing | min front-mid gap |
|---|---|
| metachronal wave, 1/3 apart | **−0.57** |
| alternating tripod | **−1.72** (walk), −4.08 (run) |
| front+middle in unison | **+1.75** |

A negative gap means the middle foot swings clean through the front one. That
is what "the second pair doesn't move naturally" was: not a missing animation —
the legs were interpenetrating. The animal is therefore phased as a quadruped
whose forelimb happens to be a pair of legs.

`REAR_OFFSET` stays near 0 because the middle-to-rear gap shrinks as it
approaches 0.5. At 0.90 each rear foot lands just before its own side's front
pair — a lateral-sequence walk — leaving a 3.30-unit middle-rear gap against
the 1.61 needed.

**The run takes two gait cycles per clip.** At one cycle its 4.2 m/s forces a
5.85-unit stride against a 4.00-unit middle-to-rear spacing, and *no* phasing
avoids a crossing. Stride is `speed x duty x period` and both speed and duty are
fixed by the Unity blend tree, so the only lever is the period: two cycles
inside the same 24-frame clip halve the stride to 2.92 units. The animal still
covers 4.2 m/s and a planted foot still tracks the ground exactly — it just
takes two strides per clip. Frame counts are unchanged, so the Unity contract
holds.

Verified end-to-end on the shipped FBX, foot vertices selected by vertex group
and the mesh evaluated per frame:

    walk   front-mid gap +1.79 / +1.80    mid-rear +2.49 / +5.53
    run    front-mid gap +1.80 / +1.80    mid-rear +2.72 / +5.65

Body sway and roll are cut to roughly half the quadruped values. Ground
penetration through the walk fell from 9.3 cm to 2.1 cm as a side effect.

One bug worth recording: the wave phases were written as `0.5 + 2/3`, shipping
a phase of **1.167**. `foot_track` takes the fractional part so it survived, but
nothing guaranteed that and it made the gait table unreadable. `phases()` now
wraps into [0, 1).

### Measuring a pose in background Blender

`PoseBone.head`, `.tail` and `.matrix` **do not refresh** under
`--background`, even on the depsgraph-evaluated copy and even after
`view_layer.update()` — the animated `rotation_quaternion` changes while the
derived positions stay at rest. A diagnostic built on them reports every leg as
perfectly static with a knee-angle range of exactly 0.0, which looks like a
catastrophic rig failure and is not one.

Two things do work, and every measurement here uses one of them: evaluating the
**mesh** (`obj.evaluated_get(depsgraph).data.vertices` reflects the armature
deformation correctly), or calling `vrescal_anim.solve()` directly and reading
the world matrices it returns.

Strides are unchanged, which matters: `vrescal_anim.py` derives them from the
Unity blend-tree thresholds so a planted foot travels backwards at exactly agent
speed. Body sway and roll are cut to roughly half the quadruped values — three
ranks of legs do not need to throw the mass across a support polygon, and the
sway was the largest single contributor to the head swinging. Ground
penetration through the walk fell from 9.3 cm to 2.1 cm as a side effect.

**Nothing on disk was edited to get from four legs to six.** The solver reads
`LEGS`, `POLE` and `Gait.phases` as module globals at call time and takes all
geometry off whatever armature it is handed, so `vrescal_hexapod_anim.py`
patches those three names on the imported module. `vrescal_anim.py` is shared
with the lofted quadruped and still drives it unchanged.

## Head centring

The sculpt's head sits **0.42 m to port** of the centreline and the snout
0.59 m, while every neck bone sat on y = 0. A bone that is not inside the
geometry it drives makes rotation swing the mesh through an arc instead of
turning it in place — measured, the head tip swung **0.75 m** across a walk
cycle.

The fix is in two places, deliberately:

- **The rig** re-derives the neck/head/jaw bones' **Y only**, from a y(x) curve
  sampled off the mesh. X and Z were measured for this sculpt and sit within
  half a unit of their geometry; moving them too would be re-rigging what was
  not broken. The rest pose therefore still holds the sculpted curve.
- **The animation** centres the head, because that is where "centred while
  moving" actually lives. A constant rest correction is solved once and added to
  every clip, so the head reads forward-facing everywhere while the idle
  head-scan still layers on top; the locomotion clips additionally cancel the
  residual per frame.

Result: head sideways swing **0.750 m → 0.002 m** in walk, 0.003 m in run.
`Vrescal_Death` is exempt — holding a corpse's head level looks wrong.

**The yaw axis is measured, not assumed.** The neck bones have roll 0 and point
along the animal, so which of `(rx, ry, rz)` produces world yaw is not obvious,
and guessing wrong is silent — the correction simply does nothing while every
number still looks plausible. `NeckCentre._find_axis` perturbs each axis, solves,
and takes whichever actually moves the head tip in y. It reports 0.0345 /
0.0127 / **0.2086** units, so axis 2. The Newton step then uses a measured
derivative rather than an assumed neck length.

## Two things about the hand-edited input

The input `vrescal_lowpoly_rigged.blend` carries hand sculpting — same 3 003
verts and same topology, but moved (bounds 4.21 × 1.88 × 3.80 m against the
generated 4.25 × 1.91 × 3.83). `vrescal_hexapod_rig.py` rebuilds **only the
armature** and asserts the mesh did not move: it prints vertex drift and refuses
to save on anything above 1e-9. It reads 0.000000000.

It also carries a hand-added **Subdivision Surface** modifier, level 1 viewport
/ 2 render. Two traps came with it:

- **`parent_set` appends the Armature modifier to the end of the stack**, which
  put it *after* the Subdivision and silently reversed the order the file had.
  Subdividing and then deforming the dense result is more expensive and worse
  looking than deforming the cage and smoothing the result. The rig script now
  moves Armature back to index 0 and reports when it does.
- **The FBX export applies modifiers**, so the Subdivision silently multiplied
  the shipped mesh from 6 010 to **36 060 triangles** — undoing the entire
  low-poly cut without a word. `vrescal_sculpt_export.py` now disables
  Subdivision for the export and says so; `VRESCAL_EXPORT_SUBSURF=1` ships it
  instead. Every earlier build had no Subdivision, so this changes nothing for
  them.

### Verified on the shipped FBX

Re-imported: 3 003 verts, 6 010 tris, **0 of 6 010 polys smooth-shaded**,
**40 bones**, 39 vertex groups, six takes at 91/37/25/40/20/64, one 512² map,
1.88 × 4.21 × 3.80 m with the feet on z = 0. All six FBX take names are
byte-identical to the previous export, so `Vrescal.controller` keeps its clips
and the `.fbx.meta` clip ranges still match. All six feet travel 1.18–1.71 m in
walk and 1.40–1.91 m in run.

## Relationship to the other files in this folder

| File | What it is |
|---|---|
| `vrescal_sculpt.blend` | **this** — the converted concept art, metre-scaled, raw photographic maps |
| `vrescal_stylised.blend` | same mesh, simplified textures. The high-poly master the low-poly is cut and baked from. |
| `vrescal_lowpoly.blend` | 3 003 verts, faceted, one 512² 12-colour map |
| `vrescal_lowpoly_rigged.blend` | the above, **hand-edited** (sculpting + a Subdivision modifier), four-legged rig. The input to the hexapod rig; superseded as a deliverable. |
| `vrescal_hexapod_rigged.blend` | six-legged rig, centred head, wave/tripod gaits. **The one that ships.** |
| `textures/` | the stylised and low-poly maps as PNGs, for repainting by hand |
| `vrescal.blend` | the previous rebuild: lofted, rigged, six animations. Untouched. |
| `anatomy.py`, `parts/body.py` | a from-scratch parametric rebuild, started before this sculpt arrived and **paused**. `parts/body.blend` is the trunk and both humps, lofted and measured against the same concept art — hump apex 3.78 m against this sculpt's 3.85 m. |
| `common.py`, `body.py`, `vrescal_wip.blend` | an earlier WIP describing a *six-legged* animal the concept art does not have. Superseded, left in place. |
