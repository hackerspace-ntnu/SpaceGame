# Grapple harpoon heads — build record

`grapple_dart.blend` → four variations → `Assets/Game/Art/Models/Items/grapple_*.fbx`

The grappling hook had no head at all: the rope was a bare `LineRenderer`
ending in nothing. This is the thing on the end of it.

| Collection | Overall length | Max spread | Silhouette | Ships to |
|---|---|---|---|---|
| `Coll_GrappleDart_Light` | 0.340 m | 0.086 m | Needle point, 2 short barbs, one thin collar | `Items/grapple_dart_light.fbx` |
| `Coll_GrappleDart_Barbed` | 0.400 m | 0.140 m | Faceted spike, 4 pronounced swept barbs, heavy banded collar, forged eye — the original hero | `Items/grapple_dart_barbed.fbx` |
| `Coll_GrappleDart_Piton` | 0.358 m | 0.105 m | Broad chisel, one flared spiked barb ring, ribbed body | `Items/grapple_dart_piton.fbx` |
| `Coll_GrappleHarpoon` | **0.9435 m** | **0.3081 m** | Trilobe lance blade, 3 huge rear-swept barbs, 0.58 m of 35 mm shaft, foregrip collar, rope ferrule, two rear high-vis bands — **the current hero** | `Items/grapple_harpoon.fbx` |

Export: `grapple_dart_export.py` (the three darts) and
`grapple_harpoon_export.py` (the harpoon). Generators: `grapple_dart.py` and
`grapple_harpoon.py` — historical record only; the .blend is the source of
truth and must never be regenerated over.

## Axes — read this before writing the Unity side

**Blender: the tip points down −Y. Up is +Z.** That is the library's standard
orientation (`−Y forward, +Z up`), and it is chosen here rather than inherited,
because `_exportlib`'s FBX flags (`axis_forward='-Z'`, `axis_up='Y'`) map
Blender `(x, y, z)` onto Unity `(x, z, −y)`.

**After import, the tip points down Unity +Z.** Verified in the editor, not
inferred: `Marker_Tip_Barbed` sits at world `(0, 0, +0.3700)` on the imported
prefab. So

```csharp
transform.rotation = Quaternion.LookRotation(travelDirection);
```

orients the dart correctly with **no rotation offset and no correction child**.
Nothing needs to be added.

The imported hierarchy is:

```
grapple_dart_barbed            localScale (1,1,1)   <- put LookRotation on THIS
├── Mesh_GrappleDart_Barbed    localScale (100,100,100), localEuler (270,0,0)
├── Marker_RopeAnchor_Barbed   localPosition (0, 0, 0)
└── Marker_Tip_Barbed          localPosition (0, 0, 0.3700)
```

The 100× scale and the −90° X on the mesh child are Unity's ordinary FBX unit
and axis conversion; the root is 1.0 and the marker `localPosition` values are
already in metres, so both are safe to read directly. **Do not parent anything
to the mesh child** — it would inherit scale 100.

## Origin: the rope anchor, not the tip

The object's origin is the **centre of the eyelet hole**, at `(0, 0, 0)`, and
`Marker_RopeAnchor_*` is a marker sitting on it. The library's rule is that the
origin goes at the logical connection point, and the rope is the only thing
this object connects to.

The practical payoff is on the Unity side:

```csharp
// the rope's last vertex is just the head's position — no marker lookup,
// no offset, nothing to get wrong
line.SetPosition(line.positionCount - 1, head.transform.position);

// and to bury the tip exactly in a raycast hit:
head.transform.position = hit.point - dir * TipOffset;   // TipOffset = 0.370 m
```

The consequence to know about: **the model extends into −Y only** (Unity: +Z
only). Dropped at a hit point with no offset, the whole dart stands proud of
the surface rather than being embedded.

### Hero numbers (`Coll_GrappleHarpoon`) — measured, Aug 2026

Measured on the saved .blend and then **re-measured on the instantiated FBX
inside the Editor**, which is the only way the Unity column is worth anything.

| | Blender | Unity local |
|---|---|---|
| Bounding box | 0.3081 (x) × 0.9435 (y) × 0.2668 (z) | 0.3081 (x) × 0.2668 (y) × 0.9435 (z) |
| Bounds extent | x ±0.1540, y −0.9000…+0.0435, z −0.0936…+0.1733 | x ±0.1540, y −0.0936…+0.1733, z −0.0435…+0.9000 |
| **Tip** | `(0, −0.900, 0)` | **`(0, 0, +0.900)`** ← `hookHeadTipOffset` |
| ROPE_ANCHOR / origin | `(0, 0, 0)` | `(0, 0, 0)` |
| Rear-most metal (eye) | `y = +0.0435` | `z = −0.0435` |
| Shaft diameter | 0.0350 (ray-probed at f = 0.56/0.62/0.66) | same |
| Max barb spread | 0.3081 across x (3 barbs at 120°, tips at r = 0.1734) | same |
| Eyelet hole | ⌀ 0.064 outer, ⌀ 0.041 clear, axis along Blender X / Unity X | |

Root imports at **localScale (1, 1, 1)** and **lossyScale (1, 1, 1)** —
verified in the Editor, not assumed. The hierarchy is the same shape as the
darts':

```
grapple_harpoon                localScale (1,1,1)   <- put LookRotation on THIS
├── Mesh_GrappleHarpoon        localScale (100,100,100), localEuler (270,0,0)
├── Marker_RopeAnchor_Harpoon  localPosition (0, 0, 0)
└── Marker_Tip_Harpoon         localPosition (0, 0, 0.9000)
```

`grapple_harpoon.fbx.meta` is byte-identical to `grapple_dart_barbed.fbx.meta`
apart from the GUID (`globalScale: 1`, `useFileScale: 1`, `useFileUnits: 1`,
`bakeAxisConversion: 0`). Five materials, all from the palette, all with
`_EMISSION` off — checked on the imported materials, not on the .blend.

### Why the harpoon exists

The barbed dart shipped as the hero and **could not be seen in play**. Two
separate problems, and only one of them is size:

1. It is 0.40 m at the far end of a 30 m rope.
2. It is *shaped* like a dart — mostly head, with a stub of body behind it.
   Scaling that up gives a big dart, not a harpoon.

So the proportion is inverted rather than multiplied. The harpoon is 2.36x the
length but **0.58 m of it is plain 35 mm shaft**, which is the piece that
actually carries the silhouette at range; the barbed dart's whole body is
0.15 m and you cannot see it past its own barbs.

Three decisions in it worth not re-deriving:

* **Three barbs, not four, and each one 2.5x the size.** 245 mm long, 60 mm at
  the root, a 16 mm plate, reaching 0.173 m off the axis. Four smaller barbs
  is the shape that failed. Three rather than two because a two-barbed harpoon
  viewed down its barb plane has no barbs at all.
* **The lance blade is trilobed, not a flat leaf.** The first pass built the
  obvious flat leaf with a lens cross-section — 65 mm wide in x, 17 mm thick in
  z — and it renders as a *needle* from every direction but one. A flat blade
  is only broad when you are looking at its face, and a flying harpoon is never
  obliging about that. Three fins at 120°, phased 60° off the barbs so the
  lobes fill the gaps, put a broad edge in front of the eye from any bearing.
  This is the single biggest legibility win in the build and it cost nothing.
* **The high-vis bands are at the BACK** — f 0.360-0.285 and 0.268-0.240, i.e.
  the rear third. A harpoon that has done its job is buried tip-first in a
  wall, so paint near the tip is paint inside the wall. Everything from
  f = 0.37 back stays outside for any plausible embed depth.

Two things were also tuned by looking at a render rather than by arithmetic,
and reverting them will reintroduce the fault:

* **Collars are 56-58 mm against the 35 mm shaft.** At 48 mm, a rubber band
  over the top swallowed the step entirely and the shaft read as one unbroken
  pipe with stripes painted on it.
* **Five ferrule rings, not seven.** Seven turned the rear half into a
  barcode.

### Hero numbers (`Coll_GrappleDart_Barbed`) — superseded

| | Blender | Unity local |
|---|---|---|
| Bounding box | 0.1395 (x) × 0.4000 (y) × 0.1395 (z) | 0.1395 (x) × 0.1395 (y) × 0.4000 (z) |
| Bounds extent | x ±0.0698, y −0.3700…+0.0300, z ±0.0698 | x ±0.0698, y ±0.0698, z −0.0300…+0.3700 |
| Tip | `(0, −0.3700, 0)` | `(0, 0, +0.3700)` |
| ROPE_ANCHOR / origin | `(0, 0, 0)` | `(0, 0, 0)` |
| Rear-most metal (eye) | `y = +0.0300` | `z = −0.0300` |
| Eyelet hole | ⌀ 0.044 outer, ⌀ 0.028 clear, axis along Blender X / Unity X | |

The other two darts: `light` tip at Blender `(0, −0.3190, 0)` → Unity
`(0, 0, 0.3190)`; `piton` tip at Blender `(0, −0.3320, 0)` → Unity
`(0, 0, 0.3320)`. All four share the same origin convention.

Proportion check against the brief: the rope trailing it is 0.062 m wide and
the launcher's `holdSize` is 0.45, so the hero is about 6.5 rope-widths across
the barbs and a bit under one launcher-length long. Barb spread is 35% of the
body length, which is what keeps the outline legible at 50 m — the whole
family deliberately carries no detail finer than about 4 mm.

## Import settings

Left at Unity's defaults, which is what every sibling item model uses: the
generated `.fbx.meta` is **byte-identical** to `Items/lasso_coil.fbx.meta`
apart from the GUID. The relevant fields are `globalScale: 1`,
`useFileScale: 1`, `useFileUnits: 1`, `bakeAxisConversion: 0`.

**Scale Factor 1**, and the instantiated root's `lossyScale` is **(1, 1, 1)** —
measured, not assumed. The repo's known "FBXs import at lossyScale 100" trap
does not apply: it belongs to assets exported some other way. What is true here
is that the *mesh child* is at 100 and the *root* is at 1 (see the hierarchy
above), which is the ordinary shape of a `FBX_SCALE_NONE` export and is why the
root is the transform to drive.

The five materials are embedded in the FBX, same as every sibling.

## Decomposition

One component file with four variations, following `portal_gun` /
`walking_staff` / `lasso_coil`: variations of one thing live together, and the
"components" of a 0.4 m prop are shared *functions*, not separate .blend files.
A tip and a barb are 30 mm features; giving each its own file would produce
five files nobody can assemble without this document.

The shared vocabulary, all in `grapple_dart.py`:

| Function | Used by | Why it is its own piece |
|---|---|---|
| `tip_point` | light, barbed | Faceted ogive penetrator. One loft, apex-first, flat-shaded. |
| `tip_chisel` | piton | Broad chisel edge. The one thing that makes the piton read as mining gear before you notice anything else. |
| `barb_swept` | light (×2), barbed (×4) | One flat swept blade, built in the axial-radial plane and rotated about Y — which is what lets two and four barbs come off the same eight lines. |
| `barb_ring` | piton | A single flared cone with spikes on its rim: the industrial answer to four blades, and one shape instead of four, which is what buys the piton its ribs. |
| `collar` / `band` / `ferrule` / `shank` | all three darts | The body vocabulary. `ferrule` is a torus rather than a box specifically so the bevel pass skips it. |
| `eyelet` | all three darts | Ring, lug and optional cheek washers, centred on the origin. |

`_y(f)` converts a forward distance into a Y coordinate. Every dimension in
the file is written as a forward distance, because that is how the thing is
actually measured ("the barbs start 27 cm up the shaft") and because a stray
sign is the single most likely way to ship a dart that flies backwards. Doing
the flip in one function means it is done once.

### The harpoon's vocabulary lives in `grapple_harpoon.py`, deliberately

`grapple_harpoon.py` re-declares `_y`, `TrackedPart`, `barb_swept`, `markers`
and a reduced body vocabulary rather than importing them. That is not an
oversight and it is not laziness:

* **`grapple_dart.py` runs `main()` at import time**, and `main()` calls
  `start()`, which refuses to overwrite an existing .blend. Importing it to
  reuse `barb_swept` aborts the build. Refactoring it into an importable
  module would mean editing a generator whose output is a shipped, shared,
  possibly hand-edited .blend — a change with no way to verify it that does
  not risk the three darts.
* The functions **diverged anyway**. `barb_swept` needed a bigger minimum
  half-width (2.5 mm, not 1.0 mm) to keep the 8 mm detail floor at harpoon
  scale; `eyelet` needed a parameterised cheek thickness for the same reason;
  `collar` / `band` / `shank` collapsed into one `sleeve`, because at this
  size there are eleven of them and one named function plus a comment per call
  reads better than three aliases; and `lance_blade` has no dart counterpart
  at all. What is left in common is about forty lines.

| Function | Why it is here and not shared |
|---|---|
| `lance_blade` | Trilobe loft. New — no dart has a blade with a width. |
| `barb_swept` | Same shape, 2.5x the size, and a thicker minimum edge. |
| `sleeve` | `collar` + `band` + `shank` collapsed into one call. |
| `ring` | `ferrule` at 16 x 6 instead of the darts' 12-16 x 6, and a comment about why it is not 24 x 10. |
| `eyelet` | Cheek washer thickness is a parameter; the darts hard-code 4 mm. |

## Variation, and what was built ahead

Only the hero was asked for. The other two exist because the tip, barb, collar
and eyelet generators were already paid for, and because the choice between
them is a look-at-it-in-the-editor decision that a render cannot settle.

They differ in **silhouette first**, per the library's variation rule:

- a needle with two barbs and almost no body,
- a spike with four barbs and a heavy banded collar,
- a chisel with a spiked flare and a rib stack.

Length differs too (0.34 / 0.40 / 0.36) but that is a consequence, not the
variation — three darts differing only in length would be one dart and a scale
bug.

The harpoon is the fourth, added later and for a different reason: the barbed
dart lost the look-at-it-in-play test, and the fix was a different *shape*, not
a scale factor. It sits in the same file because it is the same component
doing the same job with the same origin and the same axes — a fifth .blend
holding one head would be a file nobody can find.

Note that the length rule above still holds and the harpoon does not break it.
It is 2.36x the barbed dart, but the thing that makes it read differently is
the **proportion**: 62% of it is bare shaft, against the dart's 37%. Scaling
`Coll_GrappleDart_Barbed` to 0.9 m would have produced a large dart, which is
not what was asked for and would not have fixed anything.

All four ship as FBX, which is a deliberate departure from
`walking_staff_export.py`'s rule that an unreferenced FBX is not an asset. Once
the choice is made, deleting a loser is one line in `TARGETS` plus the FBX and
its `.meta`.

The harpoon has its own export script rather than a fourth `TARGETS` row on
purpose: running `grapple_dart_export.py` rewrites all three dart FBXs as a
side effect, and `grapple_dart_barbed.fbx` is wired into a prefab that other
sessions have open. One collection in, one FBX out.

## Materials — nothing added to the palette

| Slot | Material | Used for |
|---|---|---|
| 0 | `Mat_Metal_Steel_Worn` | shank, collar, eyelet — the body |
| 1 | `Mat_Metal_Steel_Dark` | tip and barbs: the hardened, machined parts |
| 2 | `Mat_Metal_Brass_Tarnished` | ferrule rings, the one warm note |
| 3 | `Mat_Plastic_Rubber_Black` | damper band on the hero's collar |
| 4 | `Mat_Paint_Safety_Orange` | one high-vis band per dart; **two** on the harpoon |

The harpoon uses the same five in the same order and adds nothing. Its only
departures are placement: the rubber appears twice (a grip band on the foregrip
collar and a damper under the rope lashing) and the orange appears twice, both
in the rear third.

Nothing was added and nothing needed to be. The brief asked for salvaged spacer
hardware, and the existing Metal category already carries exactly the
structural/machined/brass triplet that reads as machined-from-scrap.

**No emissive material appears anywhere in this file, on purpose.** The art
direction for the grapple head is deliberately plain physical hardware, and the
palette's six emissive entries are all in easy reach — so the absence is
recorded here rather than left to be re-litigated.

`Mat_Metal_Steel_Worn` is deliberately index 0: `bmesh.ops.bevel` stamps every
face it creates with material index 0, so the slot has to hold a structural
metal. That trap is documented in `item_devices_BUILD.md` and
`project_buildlib_traps`; it was not rediscovered.

## Armature

None, on any of the four. A harpoon head is one solid piece of metal — the
barbs on a real one are fixed, and the flying and the sticking are both done by
the transform. An armature would be cost with no capability.

The one thing that *would* justify a bone is a toggle barb — the rocking
crossbar on a real whaling iron that flips 90° once it is through the hide. It
was considered for `Coll_GrappleHarpoon` and left out: it is a mechanism nobody
can see at 30 m, and three fixed barbs already say "will not come out".

## A `_buildlib` bug this build found

**`Part._absorb` mis-identifies the geometry it just created, and has been
doing so for every `torus`, `tube`, `prism` and `loft` in the library.**

It records `n_before = len(self.bm.faces)`, calls `bm.from_mesh(scratch)`, and
then claims `self.bm.faces[n_before:]` as the new faces. `from_mesh` does not
leave the existing faces in their old index slots. Measured on Blender 5.1.1
with a Part holding a 6-face cone and an 8-face cone, absorbing a 72-face
torus:

```
torus's returned face list overlaps the second cone by 6 faces
final material counts   {DARK: 8, STEEL: 6, BRASS: 72}
```

Six faces of the cone were stamped with the torus's material, and six faces of
the torus were left on index 0. The slice has the right *length* every time,
which is why this has never been caught: the model builds, the counts look
plausible, and the only symptom is a handful of faces wearing a neighbouring
part's material.

It cost a build here. The first pass shipped a harpoon whose shoulder cone was
brass instead of hardened steel — nothing errored, the preview render just had
a gold nose.

`grapple_dart.py` works around it locally with `TrackedPart`, which overrides
`_absorb` to diff the face set by identity instead of by index, and replays
every `(faces, index)` pair before the bevel pass as a backstop. The replay
still corrects 20-45 faces per variation, so `from_mesh` is clobbering
material indices as well as reordering — the log line prints that count
deliberately, so a future regression shows up as a number instead of as a
mysterious gold nose.

`grapple_harpoon.py` carries the same `TrackedPart` verbatim, and the bug is
still live in Blender 5.1.1: **the replay corrects 68 faces** on
`Coll_GrappleHarpoon` (80 on the first pass, before the ferrule rings were
cut from 24 x 6 to 16 x 6 — the count scales with how much torus geometry goes
through `_absorb`, which is consistent with tori being the shapes that trip
it). Anyone who "fixes" `_buildlib` centrally should expect that number to go
to zero and should check these four renders before believing it.

**Not fixed in `_buildlib` itself.** Every component in the library imports it,
and some will have been tuned by eye against the wrong colours; correcting it
centrally would silently restyle models nobody asked to change. That is a
deliberate, separate job with its own review of the affected renders.

## Two other traps, avoided rather than hit

**The tip is one loft, not two stacked cones.** The first pass stacked a
point-first cone on a shoulder cone with matching radii at the join; `finish()`'s
`remove_doubles` welded the coincident rings into six non-manifold edges with
the second cone's cap sealed inside. One loft has no seam to weld. The apex is
a 0.6 mm ring rather than a true point — invisible at any distance this is seen
from, and it avoids the degenerate fan a zero-radius cone produces.

**`bevel` is given an explicit face list, never the whole part.** `BEVEL_W` is
0.0012. Everything here is 10-30 mm thick, and the library's 12 mm default
exceeds half the radius of the shank; a whole-part pass welds the barb blades
and the shaft into a lump. Only the eyelet lugs and the piton's chisel — the
genuinely boxy pieces — are bevelled.

## Verification

Measured on the saved .blend, not on the generator's intent:

| | Light | Barbed | Piton | **Harpoon** |
|---|---|---|---|---|
| Triangles (mesh only) | 1 096 | 1 668 | 1 960 | **2 348** |
| Triangles (with markers) | 1 120 | 1 692 | 1 984 | **2 372** |
| Loose verts / edges | 0 / 0 | 0 / 0 | 0 / 0 | 0 / 0 |
| Boundary edges | 0 | 0 | 0 | 0 |
| Non-manifold edges | 0 | 0 | 0 | 0 |
| Zero-area faces | 0 | 0 | 0 | 0 |
| Duplicate verts | 0 | 0 | 0 | 0 |
| Object scale / rotation | 1.0 / 0 | 1.0 / 0 | 1.0 / 0 | 1.0 / 0 |

The harpoon is 2.36x the length of the barbed dart for 1.4x the triangles,
which is the whole argument for spending the budget on a shaft. It nearly was
not: the first pass came in at **3 776** triangles because seven brass ferrule
rings were authored at `maj_seg=24, min_seg=6`, costing 2 240 triangles — more
than the rest of the harpoon put together — for a 5 mm bead that is one pixel
wide at any distance this is seen from. A torus is the most expensive shape
per unit of silhouette in `_buildlib`; 16 x 6 is the right resolution for a
bead and it is smooth-shaded, so the facets do not show.

Every mesh is watertight. Material assignment was checked by bucketing every
polygon by `material_index` and printing each bucket's Y range and maximum
radius — which is how the `_absorb` bug was found, and the only reliable way to
see it.

## Markers, and the one manual Unity step

`Marker_RopeAnchor_*` and `Marker_Tip_*` are 4 mm cubes. They are geometry
rather than empties because `object_types={'MESH'}` does not export empties —
same reason `portal_gun.blend` carries `Marker_Muzzle`.

`Marker_RopeAnchor_*` is redundant by construction: it sits on the origin. It
is there so the anchor is named and visible in the Unity hierarchy instead of
being a fact you have to read this document to learn. `Marker_Tip_*` is not
redundant — it carries the embed offset.

They are suffixed per variation because Blender object names are global and all
four variations live in one file. Each FBX contains only its own pair —
`Marker_RopeAnchor_Harpoon` and `Marker_Tip_Harpoon` for the harpoon, the
latter at Unity local `(0, 0, 0.9000)`.

**Manual step: disable the two `Marker_*` MeshRenderers** on whichever variant
is wired up (as `PortalContentBuilder` does for the portal gun's markers). A
4 mm cube is small but it is not invisible, and `Marker_Tip` sits half outside
the point. Disable rather than delete — a child of a prefab instance cannot be
removed without unpacking it, and keeping the link is what lets a re-export
from Blender reach the prefab.

## Not wired to anything

Both builds stop at the FBX, deliberately: the grappling hook's C# and prefab
were being edited concurrently, in both passes. No script, prefab or scene was
touched.

The one number the Unity side needs from the harpoon build is
**`hookHeadTipOffset = 0.900`** (Unity local +Z, measured on the instantiated
FBX). `Marker_Tip_Harpoon.localPosition.z` is the same number if you would
rather read it at runtime than hard-code it.
