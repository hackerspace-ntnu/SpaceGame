# Item Scanner — build record

A salvage finder: a cream field instrument built around a green CRT, now
**worn on the forearm** with the screen facing up. Ships to Unity as
`Assets/Game/Art/Models/Items/item_scanner.fbx` and drives the
`ItemScannerArtifact` gameplay component.

Built 2026-08-21 from a Fallout-style Pip-Boy reference as a hand-held unit on
`arm_cuff`'s `Grip` variation. **Reworked 2026-09-02** into a gauntlet for the
body-equipment rework (`docs/superpowers/specs/2026-09-02-body-equipment-design.md`
§7): the grip is gone, the terminal lies on the family's webbing cuff at the
same seating as the grapple bracer and the leash gauntlet.

`models/gear/` was created for the first build — the first carried thing that
*assembles* from more than one component, so it needed to be a model rather
than a component.

## Decomposition

| Piece | Source | Why separate |
|---|---|---|
| `Mesh_ArmCuff_Webbing`          | `components/props/arm_cuff.blend`, via `_gauntlet.append_cuff` | the family mount, reused unchanged |
| `Mesh_Terminal_Scanner_Case`    | `components/props/handheld_terminal.blend`, scaled 0.8 | the static body |
| `Mesh_Terminal_Scanner_Screen`  | same | Unity paints the radar shader on this renderer alone |
| `Mesh_Terminal_Scanner_Dial`    | same | the game spins it while scanning; origin on its axis |
| `Mesh_Terminal_Scanner_Antenna` | same | the game sways it; origin at its root |
| `Mesh_ItemScanner_Bracket`      | authored here | the only geometry unique to this model |

Component names are kept rather than renamed to `Mesh_ItemScanner_*`, so each
piece's provenance is readable straight off the outliner — and so the prefab's
serialized references to the screen, dial and antenna survive the re-export:
FBX sub-object ids are derived from object names, and all three names are
unchanged. `Mesh_ArmCuff_Grip` and the old bracket are gone; nothing
referenced either.

## The rebuild, and why it was allowed

`item_scanner.blend` was deleted and regenerated from the rewritten
`item_scanner.py`. `start()` refuses this and it is normally forbidden, because
a shipped `.blend` may carry hand edits the generator would destroy. It was
proved safe first: the *unmodified* generator was rebuilt into a scratch file
and diffed vertex-for-vertex, object-for-object, against the shipped file
(scales, polygon counts and per-material histograms included) — **identical**.
The file had never been touched by hand. It must not be done again without the
same control diff.

## The family seating — `_gauntlet.py`

The cuff transform, frame convention, spine and clamp bands come from
`models/gear/_gauntlet.py`, lifted out of `grapple_bracer.py` so all three
gauntlets share one seating — see `leash_gauntlet_BUILD.md` for the module and
the bracer control diff.

**Frame: arm along Y, wrist at y = 0, elbow +Y, forward −Y, dorsal +Z.**
The export maps Blender `(x, y, z)` onto Unity `(−x, z, −y)`: Blender +X is the
thumb side of a right forearm, −X the little-finger side. The cuff's bounding
box is the same as in the other two FBXs: x −0.066..0.046, y 0.012..0.205,
z −0.056..0.059.

## Orientation — the three constraints the code imposes

`ItemScannerArtifact.cs` and `ItemScannerScreen.cs` were not touched, so the
meshes satisfy what they already assume. Each was verified from the built mesh
(`landmarks.py` in the session scratchpad), not from the matrix.

1. **The screen faces +Z, `v` toward the wrist.** The radar is a 180-degree
   forward arc with `v` up meaning "ahead", and ahead is where the arm points.
   The case is placed by `R_z(180) @ R_x(−90)` scaled 0.8: the plate's normal
   (local −Y) → +Z, `v` (local +Z) → −Y, `u` (local +X) → −X, which is Unity
   +X — the viewer's right when looking down their own arm. Measured: normal
   (0, 0, 1), u+ (−1, 0, 0), v+ (0, −1, 0). Same handedness the hand-held
   build shipped with, so `ItemScannerScreen.mat` keeps `_FlipX = 0`.
   `AspectOf` finds the thin axis on Unity Y and reads width = x, height = z,
   which is what the plate now is: 0.090 x 0.074 m at z = 0.150..0.152.
2. **The dial's axis is Blender Y.** The code spins it with `Euler(0, 0, a)`,
   Unity local Z = Blender −Y, and overwrites the node's rotation every frame,
   so the mesh itself has to be right at identity. The knob was authored
   protruding from the deck along −Y; the deck now faces up, so the knob
   **moved off it** to the case's elbow-end face (the terminal's base,
   local z = 0), rolled by `R_z(180)` so it protrudes toward +Y. It sits at
   (0.030, 0.169, 0.130), 1 mm inside the face so its base shares no plane,
   clear of the hazard stencil (x −0.040..−0.005) and the status lamps
   (x 0.022..0.042, z 0.154). Measured extents (0.026, 0.026, 0.026) with the
   protrusion along +Y. The deck keeps a bare spot where the knob was; it is
   flat, so nothing reads as missing.
3. **The antenna stands up +Z.** The code sways it with `Euler(x, 0, z)`,
   Unity X and Z, both transverse to a mast on Unity Y = Blender Z. It is
   placed with a roll only, `R_z(−90)`, sending its authored lean (+X, −Y) to
   (−X, −Y): outboard and forward, away from the wearer's face. Measured
   root→tip (−0.17, −0.15, 0.97). It stands on its own boss on the bracket
   plate at (−0.067, 0.150) because the only flat upward surface left on the
   case is the screen; the case's original mast socket, now facing the wrist
   at (−0.045, 0.045, 0.088), stays as a stub.

## Placement

The terminal is worn at **0.8x** (`TERMINAL_K`). Authored at 0.174 x 0.185 m
it is wider than the forearm it lies on; at 0.8 the screen is still 0.090 x
0.074 m, a tablet on the arm at the rig's 2.1x wear. It was scaled the way the
bracer scales its harpoon: in the placement matrix, component untouched.

`CASE_AT = (0, 0.170, 0.1116)`: the case is centred on the cuff along the
arm (y 0.022..0.172), the back housing's underside lands at z = 0.070, 1.5 mm
inside the plate, and the screen at z = 0.152. Controls and deck are at the
elbow end (y 0.146..0.167); bail handle, grille and the old mast socket at the
wrist end (y 0.027..0.056). The housing, coil and connectors land on +X, the
thumb side — the more visible flank when a player looks down at their own
right arm, as the bracer's build record already found for the gas bottle.

## The bracket

- **Spine** (`_gauntlet.spine`) y 0.020..0.170, floor sunk 0.5 mm into the
  cuff's boss, rails to z 0.068 — lower than the bracer's 0.076 because the
  plate sits on them and the case on the plate, and every millimetre here is
  2.1 on the arm. No painted panel: the plate covers it.
- **Clamp bands** at both of the bracer's stations, pads 0.5 mm inside the floor.
- **Plate** x −0.078..0.080, y 0.062..0.174, z 0.0675..0.0715, on the rail
  tops. It starts at y = 0.062 rather than at the case's wrist end because the
  bail handle and its chrome mounts hang down to z = 0.064 at y = 0.027..0.060;
  a plate under them was a plate through them. The case's wrist-end 40 mm
  overhangs, which a bail handle can do.
- **Cradle** x −0.056..−0.023, z 0.071..0.1073 under the front block: only the
  back housing reaches the plate, so without this the −X half of the case
  floated 35 mm above it. Top 0.5 mm inside the case, +X face 1 mm inside the
  housing's flank, bottom 0.5 mm in the plate.
- **Hold-down clips**, four steel blocks 2 mm into the case's flanks and
  1.5 mm into the plate. The +X pair straddle the coil and connectors
  (y 0.079..0.125); the −X pair at y 0.068 and 0.130 sit clear of the fire
  button (y 0.104, x to −0.0624) and the mast boss (y 0.138..0.162).
- **Mast boss** z 0.0712..0.0792 round the antenna's root; the antenna's own
  rubber base is sunk 2 mm into the plate, deeper than the boss's 0.3 mm, so
  their undersides never share a plane (the first build had them 0.1 mm apart
  and the z-fight pass caught it).
- Six chrome rivets along the plate's edges.

## The screen is its own object, and it carries UVs

Unchanged from the first build: the display is a separate renderer so the
radar shader repaints only the plate and a `MaterialPropertyBlock` can address
it alone; it is the one mesh in the library with a UV layer, because a
procedural display shader is authored in 0..1 screen space and `_buildlib`
writes none. The plate is not bevelled, so no rim faces fold into the island.
The export reports it as the only object with `uv`.

## No armature

The dial and antenna are rigid and are already separate objects with their
origins on their own axis of motion — the cleaner form of the same
capability, skipping a bone hierarchy Unity would have to unpick.

## Materials

**No palette additions.** The bracket uses the family list (`_gauntlet.MATS`);
the terminal and cuff bring their own, all already in `PALETTE.md`.

## Verification

- Rendered iso/front/side/top from `_preview.py` after each build and looked
  at: screen up, controls at the elbow, knob on the elbow face, mast outboard,
  handle overhanging the wrist end clear of the plate.
- Exact z-fight pass (`_zverify.py` at item-scale thresholds, cross-object
  pairs only): one pair, 0.4 mm, between the screen plate's front face and the
  bezel's black cavity floor **inside `handheld_terminal`** — the component's
  own design (the plate stands 0.5 mm proud of the cavity, which it fully
  covers) and identical in the hand-held build. Nothing between the bracket
  and anything else.
- `TrackedPart.restamp()` corrected 0 faces.
- FBX bounds, Blender axes: min (−0.0868, 0.0118, −0.0750) max (0.0800,
  0.2050, 0.1870), size (0.1668, 0.1932, 0.2620), longest 0.2620 (up, the
  antenna tip). Unity axes: min (−0.0800, −0.0750, −0.2050) max (0.0868,
  0.1870, −0.0118). `holdSize` at the bracer's 2.1x wear would be 0.550; the
  spec puts gauntlets in the `Fitted` bracket, so treat that as a reference
  figure.
- Pivots (Unity local, from the export): Case and Screen (0, 0.1116, −0.170),
  Dial (−0.030, 0.130, −0.169), Antenna (0.067, 0.071, −0.150).

## Unity wiring (not done here — prefab work belongs to the equipment session)

The prefab's `screen`, `dial` and `antenna` references survive because the
node names did. What changes is the fit: the `Grip` instance goes, the FBX is
instanced at identity on the worn socket with the bracer's
`rotationOffset = (0, 0, −90)`, and the dial now sits on the elbow-end face.
Verify the radar's left/right on a real contact before trusting `_FlipX = 0`;
the handedness argument above is derived, not played.

## Judgement calls worth naming

- **Screen "up" is toward the wrist.** A watch is read across the arm; this is
  read along it, with the far edge as the top, because the radar's top is
  "ahead" and the arm points ahead. If it should read the other way, the case
  matrix drops the `R_z(180)` — but the dial then needs a new home and `u`
  flips, so `_FlipX` becomes 1.
- **The dial is on the elbow-end face.** Forced by the code's spin axis; the
  alternative that kept the knob on the deck was tilting the whole case so the
  deck faced the wearer, which put the screen on edge.
- **0.8x.** At 1.0 the case is a 0.37 m brick across a 0.19 m forearm on the
  rig; at 0.7 the screen dips under the first build's "too small to read"
  threshold. If the screen must be bigger, scale `TERMINAL_K` and re-check the
  handle against the plate edge, which is the first thing to collide.
