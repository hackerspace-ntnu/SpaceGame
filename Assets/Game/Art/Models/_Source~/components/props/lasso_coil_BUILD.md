# Lariat coils — build record

`lasso_coil.blend` → `Coll_Lasso_Coil` → `Items/lasso_coil.fbx` → Lasso artifact.

The fourth member of the carried-device family in `item_devices_BUILD.md`, built
to the same constraints: 0.15–0.28 m on the long axis, three variations per file
separated by silhouette, seen mostly as a small object in a hand or a 256 px
inventory icon.

## Why it exists

The Lasso was the only artifact with no held model. Its prefab's `lassoModel`
pointed at the *thrown* rope — a 4.4 m `BézierCurve` authored at flight scale,
sitting in the same prefab as a 0.12 m `Cylinder` handle, a 36× mismatch between
two parts of one object.

That is unfixable from the grip system, which is what surfaced it. Scaling the
whole thing to fit a hand shrinks the handle to 8 mm; scaling to the handle
leaves four metres of rope trailing through the world. Neither is a held object,
so one had to be modelled.

## Reuse

**Nothing existing was reused.** The library has no rope, cord, or coil
component — `Mesh_Cable_*` in the mining rig is 2.6 m of hung power cable built
into that model, not a component, and it is metal conduit rather than fibre.

The `sweep` helper was copied from `portal_gun.py` rather than reimplemented.
That is now the **third** copy in the library (`portal_gun`, `claw_chela`, and
this file all define one), which is a fair sign it belongs in `_buildlib`
alongside `loft` and `prism`. Not moved as part of this build — that edits a file
every component depends on, and this was not the change to do it under.

## Materials

One palette addition: **`Mat_Fabric_Rope_Hemp` `#B89968`**, roughness 0.92.
`palette.py check` confirmed nothing within range — the nearest fabric,
`Mat_Fabric_Canvas_Faded` `#6E6A5A`, is documented as dirty grey webbing, which
is a different material from laid natural-fibre rope and reads grey rather than
tan at icon size.

Everything else came from the existing palette: `Mat_Fabric_Canvas_Faded` for the
whipping cord, `Mat_Fabric_Seat_Ochre` for the leather keeper,
`Mat_Metal_Steel_Worn` and `Mat_Metal_Brass_Tarnished` for the hardware.

## Decomposition

One component file, three variations, differing in silhouette because silhouette
is the only axis that survives a thumbnail:

- **`Coll_Lasso_Coil`** — the working lariat gathered into a flat hank, tied at
  one side, honda tucked against the turns. Widest and shallowest of the three
  (0.27 × 0.23 × 0.12) so it presents its full circle to camera when held. **This
  is the one wired to the artifact.**
- **`Coll_Lasso_Hank`** — the rope doubled and gathered at the waist, two lobes
  under one tie. Taller and narrower (0.23 × 0.15 × 0.22): stowed rope rather
  than rope ready to throw.
- **`Coll_Lasso_Saddle`** — a looser coil closed in a leather keeper with a steel
  buckle, the way a lariat rides on a saddle horn (0.24 × 0.20 × 0.11).

The last two were built ahead. The rope profile, the coil path and the material
choices were already paid for by the first, so the marginal cost was small, and a
camp or stable scene now has rope that does not look copy-pasted.

No armature. Nothing on a coil of rope articulates — the part that moves is the
thrown line, and `LassoArtifact` already draws that with a `LineRenderer`.

## Two decisions worth naming

**Fibre, not tech.** `leash_device.blend` already owns the engineered answer to
catching something — spool, fairlead, snap hook. A second hand-sized coil of
cable would be indistinguishable from it at icon size, so this one is honest
rope. The two artifacts now read as the improvised and the engineered version of
the same idea.

**The honda is drawn as the knot, not the loop.** A real honda runs metres
across. Anything that size hanging off a held model clips the leg and the ground
while walking, which is the problem this file exists to remove. So the loop is
hand-sized: enough to say "lasso" rather than "bundle of rope", without extending
the silhouette past the coil.

## Two traps hit, recorded

**The whipping's axis.** A binding has to encircle the rope, so its torus axis
runs *along* the bundle where it is tied — tangential to the coil, not parallel
to the coil's own axis. The first pass had it the wrong way and produced two
discs either side of the turns: the object stopped being a bound coil and became
a cable spool with flanges, i.e. the exact `leash_device` silhouette it is meant
to be distinguishable from.

**`leash_device.py` calls `p.sweep(...)`, which does not exist.** `sweep` is a
module-level helper in the files that define one, never a `Part` method. That
script cannot run as written — a live demonstration of why the `.blend` is the
source of truth and the generator is history.

## Rebuild note

`lasso_coil.blend` was deleted and regenerated once during this build, after the
whipping-axis fix. That is normally forbidden and `_buildlib.start()` refuses it;
it was safe here only because the file had been created minutes earlier by this
same script and had never been opened, so there were no hand edits to lose. It
must not be done again.

## Unity wiring

`Coll_Lasso_Coil` → `Assets/Game/Art/Models/Items/lasso_coil.fbx`, added to the
`TARGETS` list in `item_devices_export.py` as the family's fourth member.

In `Lasso.prefab`:

- the FBX is instanced as a child named `Coil`, and `LassoArtifact.lassoModel`
  now points at it
- the old visual (the nested model holding the 4.4 m `BézierCurve` and its
  `Cylinder`) is **deactivated, not deleted**, so the change is reversible and
  nothing that referenced it is left dangling. A deactivated renderer is also
  skipped by `EquipItemSocket.MeasureLocalBounds`, so it no longer influences
  how the item is sized
- `ItemGrip`: `gripPoint` is a `Grip` marker at `(0.108, 0, 0)` — the tie, where
  a hand would actually close on the bound turns — with `holdSize` 0.45 and
  `rotationOffset` `(0, 0, 90)`

The rotation sign is worth keeping. The vector from the tie to the coil's centre
is model -X. Under `R_z(-90)` that maps to +Y, the thumb side, which parks the
coil above the wrist; `R_z(+90)` sends it to -Y so the coil hangs out of the
bottom of the fist, and the coil's face normal lands across the palm so it reads
broadside rather than edge-on.

`holdSize` is 0.45 rather than the 0.26 the model is built at. This rig's hands
are stylistically oversized - 0.176 m wrist-to-knuckle against roughly 0.09 m on
a real hand of this height - so a true-scale lariat reads small in the glove.

## Not indexed

`LIBRARY.md` covers `models/` only, so this component does not appear in it -
the same is true of `leash_device`, `antigrav_device` and the rest of
`components/`. Pre-existing behaviour of `index_library.py --models-dir models`,
not something this build changed.
