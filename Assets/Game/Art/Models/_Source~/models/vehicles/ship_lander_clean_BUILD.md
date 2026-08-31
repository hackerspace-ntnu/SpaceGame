# Lander (clean rebuild) — build record

Built 2026-08-30. `ship_lander_clean.blend` = the hand-built interior (from
`ship_lander_blockout.blend`, carried through `ship_lander.blend` untouched:
73 cubes, one icosphere, the ×30 wireframe reference hull) **plus every hull
piece rebuilt as clean Blender geometry** — lofts of chamfered sections and
fitted primitives, dimensioned off the sliced original so the shape holds.

Generator: `ship_lander_clean.py`. It opens `ship_lander.blend`, measures the
sliced pieces, builds the clean ones, deletes the sliced pieces and their
Tripo material, and saves under the new name. Neither source file is written.

---

## Why a rebuild

The sliced pieces in `ship_lander.blend` are the Tripo mesh cut up: they keep
its irregular triangles, slivers, self-overlaps and fanned caps. The brief was
to keep the *shape* and replace the *mesh*.

## How each hull piece is made

1. The sliced piece is cut at stations along its main axis (Y for fuselage,
   pods, nacelles and wingtips; X for the wings; Z for the fin) and the cut
   loop is sampled.
2. At each station the loop is reduced to a **convex support polygon** —
   for each of *n* directions, the farthest point; neighbouring support lines
   intersected. With *n* = 8 that is exactly a 45°-chamfered rectangle
   hugging the section, which is the example's own hard-surface language.
   *n* = 16 only on the nose and canopy, which are genuinely rounded.
3. The stations are lofted into one closed quad strip with flat end caps.
   8-facet pieces are flat-shaded; 16-facet pieces smooth.

Station spacing is 1.0 m (1.5 m nose, 2.0 m canopy, 0.7 m fin). Where a piece
steps sharply the loft is split into numbered objects at the step, so it does
not slope diagonally across it (`Clean_TailBoom_1` = aft roof cap,
`Clean_TailBoom_2` = the boom). The sliced "fin" source held both the port
blade and the tail's roof cap; a point filter at x = −2.4 m separates them
into `Clean_Fin` (lofted along Z) and `Clean_TailSpine` (along Y).

Every greeble (`CleanDetail_*`) is a primitive fitted to the original's
bounds: a 12-segment cylinder when its proportions are rod-like (length ≥
2.5 × width and the two thin dims within 35 %), otherwise a box with a 3 cm
bevel.

## What is lost, deliberately

Concavities *within one section* are filled by the convex profile — a scoop
or recess in the side of a piece becomes flat. Pieces are small enough that
this is rarely visible; where it matters (the aft body's ramp recess, the
canopy frame), those are the places to hand-model next, and the clean loft is
the right base to cut into.

## Collections and materials

```
Coll_LanderClean
├── Coll_LanderClean_Fuselage   Clean_Nose, Clean_Canopy, Clean_Cockpit_Hull, Clean_MidBody, Clean_AftBody
├── Coll_LanderClean_Wings      Clean_Wing_L/R, Clean_Wingtip_L/R
├── Coll_LanderClean_Pods       Clean_SidePod_L/R
├── Coll_LanderClean_Engines    Clean_Nacelle_L/R
├── Coll_LanderClean_Tail       Clean_TailBoom_1/2, Clean_Fin, Clean_TailSpine
└── Coll_LanderClean_Details    CleanDetail_<Region>_NN (53)
```

All palette: `Mat_Paint_Hull_Bleached` (hull, wings, tail),
`Mat_Glass_Canopy_Tinted` (canopy), `Mat_Metal_Steel_Dark` (nacelles, pods,
wingtips), `Mat_Metal_Steel_Worn` (details). Nothing added to the palette.

## Verification

- Every `Clean*` object: 0 open edges, 0 non-manifold edges (see
  `manifold_check`), so all are closed solids.
- Rendered against the original from iso, rear-iso, top and side: silhouette
  and part layout match; surfaces are crisp instead of lumpy.
- Pre-existing interior objects: all 75 still present, untouched.

## Tunables (top of the script)

`HULL_PIECES` — per piece: loft axis, material, facets, station spacing,
split stations, point filter. `FIN_BLADE_X`, `END_INSET`, `BOX_BEVEL`.
Change and run into a **new** filename; never over a hand-edited .blend.
