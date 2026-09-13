# Ruin Scanner — build record

`models/gear/ruin_scanner.blend` → `Assets/Game/Art/Models/Items/ruin_scanner.fbx`
→ the Ruin Scanner artifact.

Replaces the Unity built-in cube `RuinScanner.prefab` wears today. The device is
now a gauntlet on the right forearm — the third of the family
`docs/superpowers/specs/2026-09-02-body-equipment-design.md` §7 puts on the
webbing cuff, after the grapple bracer and the sucker puncher.

| Part | Where it comes from |
|---|---|
| `Mesh_ArmCuff_Webbing` | `components/props/arm_cuff.blend` — **reused unchanged** |
| `Mesh_RuinScanner_ClampFront`, `_ClampRear` | `grapple_bracer._clamp_band()` — **reused**, the family's cuff clamp |
| `Mesh_RuinScanner_Spine` | channel down the back of the cuff, the bracer's stations |
| `Mesh_RuinScanner_Housing` | the emitter body — unique to this model |
| `Mesh_RuinScanner_Panel` | painted top panel: two amber ready lamps, arming stripe, rivets |
| `Mesh_RuinScanner_Heatsink` | five fins at the elbow end |
| `Mesh_RuinScanner_Readout` | the readout bezel, a wedge sunk into the roof |
| `Mesh_RuinScanner_Screen` | the lit face alone, so Unity can paint it |
| `Mesh_RuinScanner_Hood` | lens hood, black-lined, brass collar and mouth ring |
| `Mesh_RuinScanner_Lens` | domed amber lens recessed 10 mm inside the hood |
| `Mesh_RuinScanner_Conduit` | cable from the housing's tail into the starboard rail |
| `Emitter` | empty on the lens face — the prefab's `muzzle` |

**4,188 triangles** assembled (the bracer is 17,720). Export: `ruin_scanner_export.py`.

## Reuse

The arm mount is `arm_cuff`'s **webbing** variation, appended and placed through
`grapple_bracer.cuff_matrix()` so it sits on the arm exactly as the bracer's
does: mounting boss under the spine, buckles on the −X flank. The spine runs the
bracer's stations (`SPINE_Z0`/`SPINE_Z1`, floor at z 0.056, rails to 0.076) and
the two clamp bands are literally the bracer's `_clamp_band()` at the bracer's
`CLAMPS` — y 0.030 and 0.150, standing `BAND_STANDOFF` = 20 mm off the cuff —
so the five gauntlets clamp onto the sleeve at the same two rings.

Importing a private helper across model scripts is a judgement call. The
alternative was a copy of the 30-line band builder; the bracer's docstring on it
already records the one rotation trap (`R_y(90 − a)`, not `atan2`) and it would
have had to be duplicated too. `ruin_scanner.py` guards the coupling it creates:
`_clamp_band` stamps faces with the bracer's `STEEL` and `CHROME` indices, so the
scanner's `MATS` keeps those two on the same slots and refuses to build if they
drift.

Nothing else in the library was a candidate. `handheld_terminal` is a screen with
a case, not an emitter; `floodlight_bank` is at vehicle scale; `gauntlet_shell`
is the sucker puncher's closed steel shell and the wrong silhouette for a thing
that should read as an instrument rather than a weapon.

## Decomposition

Every logical part is its own object, per the skill's geometry rules. The split
follows what someone would want to select: the housing can be reshaped without
touching the hood, the hood swapped for a wider one without touching the lens,
the screen given a shader without touching its bezel. Fasteners — the rivets on
the spine and panel, the four bolt heads either side of the collar — live inside
the part they fasten; they have no name of their own and never will.

## Materials

**No palette additions.** Ten materials, all already in use on the bracer or the
item scanner: `Mat_Metal_Steel_Worn` (spine, clamps, housing, hood),
`Mat_Metal_Steel_Dark` (fins, readout bezel), `Mat_Paint_Hull_Bleached` (top
panel), `Mat_Metal_Brass_Tarnished` (lens rings), `Mat_Metal_Chrome_Scuffed`
(fasteners, conduit), `Mat_Plastic_Rubber_Black` (cuff pads),
`Mat_Neutral_Black_Matte` (hood lining), `Mat_Emissive_Amber` (lens, ready lamps),
`Mat_Emissive_Green_CRT` (readout) and `Mat_Paint_Warn_Red` (arming stripe).

The lens is amber rather than a new "scanner" colour because the artifact's cone
is drawn by `RuinScannerPulse`'s own material at runtime; the lens only has to
say "this is where the light comes from", and amber is the family's lamp colour.

## The frame the model is built in

**Arm along Y, wrist at y = 0, elbow toward +Y, forward −Y, dorsal +Z** — the
grapple bracer's frame, unchanged, so the same `ItemGrip` offsets seat both.
`grapple_bracer_BUILD.md` derives `rotationOffset = (0, 0, −90)` and the 2.1x
wear; none of it is repeated here.

Measured on the exported FBX by re-importing it beside `grapple_bracer.fbx`:

```
                        ruin_scanner.fbx              grapple_bracer.fbx
cuff bounds (Blender)   (-0.066, 0.012, -0.056)       identical
                        ( 0.046, 0.205,  0.059)
whole model, Blender    lo (-0.0672, -0.0535, -0.0750)   lo (-0.0672, -0.1770, -0.0750)
                        hi ( 0.0672,  0.2050,  0.1272)   hi ( 0.0705,  0.2050,  0.1590)
size (x, y, z)          (0.1343, 0.2585, 0.2022)      (0.1376, 0.3820, 0.2340)
longest axis            0.2585 (along the arm)        0.3820
holdSize at 2.1x        0.5427                        0.80
```

So the forearm runs along **Blender +Y** (wrist at y = 0, elbow at +0.205), which
`_exportlib` lands on **Unity −Z**; model forward — out of the lens — is Blender
−Y, **Unity +Z**. Dorsal is Blender +Z, Unity +Y. The cuff's origin is at the
wrist end on the arm axis, at the model origin, in both files.

The one number that changes between the two prefabs is `holdSize`: 0.2585 × 2.1
= **0.54**, against the bracer's 0.80. It is smaller because the bracer's harpoon
sticks 0.177 m out past the wrist and the scanner's hood stops at 0.054.

### The `Emitter` empty

```
Blender   (0.0000, -0.0440, 0.0930)   identity rotation
Unity     (0.0000,  0.0930,  0.0440)  (x, y, z) → (−x, z, −y)
```

That is the centre of the lens face, on the hood's axis, 44 mm past the wrist
and 93 mm dorsal of the arm axis — 92 mm forward and 195 mm dorsal once worn.
Identity rotation on purpose: the exporter carries the model's −Y onto Unity +Z,
so the empty's Unity forward is out of the lens. `RuinScannerArtifact` uses
`muzzle.position` as the cone's root and only falls back to `muzzle.forward` when
it has no camera to aim by, so the position is the number that matters.

Shipping it needed one flag `_exportlib.export()` did not have: `keep_empties`.
Added, default off, so nothing already exported changes. Without it the FBX has
no `Emitter` and the cone would root on the prefab root, in the middle of the
forearm.

## Layout

Read down the arm from the elbow; every figure is a Y in model metres.

```
 +0.205  back of the cuff
 +0.200  back of the spine
 +0.186  conduit drops into the starboard rail
 +0.172  tail of the housing (roof at z 0.109)
 +0.162 → +0.122  five heat-sink fins
 +0.150  rear clamp band
 +0.100  readout, tilted 20° to face up and toward the elbow
 +0.078 → -0.010  painted panel; ready lamps at +0.068, stripe at -0.003
 +0.030  front clamp band
 +0.012  front of the cuff
 -0.020  front of the spine
 -0.030  front of the housing (roof at z 0.117, lens axis z 0.093)
 -0.044  lens face — the Emitter
 -0.054  mouth of the hood
```

**The housing's floor is held flat, not its centreline.** Stations are (y, width,
depth) with the bottom pinned at `HOUSING_Z0` = 0.073, 3 mm into the rail tops.
Centring the loft instead lifts the floor with the tail taper and the rails poke
out from under the body at the elbow end — and at the first corner radius tried
(0.45) they did even with a flat floor, by 1.2 mm. Corner 0.35 and a 42 mm tail
width put the body's underside 2 mm below the rail tops everywhere.

**The readout tilts by `R_x(−20)`.** Negative, because `R_x` carries local +Z
toward −Y for a positive angle; the memory note on the crew seats records the
whole family of raked panels that got this sign wrong. Verified from the file:
the lit face's normal is (0, 0.342, 0.940) — up and toward the elbow.

**The lens narrows toward the mouth.** `cyl`'s `radius_top` is the +Y end, so
the 13.5 mm face is at −Y (the front) and the 17.5 mm base sits 0.2 mm into the
hood lining. Verified: ring radius 0.0135 at y −0.044, 0.0175 at y −0.038.

**The housing overhangs the cuff by 30 mm and the hood by another 24.** The cone
is spawned from the lens face, and anything of the cuff or the hand in front of
that point would sit inside the beam.

## Z-fighting

Zero coplanar overlapping faces between objects, checked pairwise on every
axis-aligned face in the file. The places it had to be designed out:

- the spine floor is 1 mm narrower than the rail gap and its underside 0.5 mm
  above the rails', so no face of one lies in a plane of the other (the bracer's
  spine builds these flush);
- the panel is sunk 1.5 mm into the roof and stands 1.5 proud; the lamps and
  stripe sink 0.5 mm into the panel;
- the screen's back is 0.2 mm inside the bezel and its face 1 mm proud;
- the fins root 5–8 mm into the taper and stand 6–9 proud;
- the hood's black lining is 0.3 mm inside the steel wall and 0.5 mm shorter
  at each end; the brass collar straddles the housing's front face and hides
  the seam.

## Two things decided that could go the other way

**The fins are not bevelled.** The first build bevelled them at 0.8 mm and the
bevel's index-0 faces (see `_buildlib` trap 1) turned each 2.5 mm fin two-thirds
steel. The readout bezel *is* bevelled and wears a steel chamfer round its dark
face for the same reason — it is the bracer's control-pod look and was kept.

**One regeneration.** `ruin_scanner.blend` was deleted and rebuilt once, for the
fin bevel, minutes after it was first written and before it had ever been opened
in Blender — the same grounds `grapple_bracer_BUILD.md` records. It must not be
done again: from here the .blend is the source of truth.

## No armature

Nothing on the device articulates. The readout could be made to swivel and
nothing in the game swivels it; the beam is not part of this mesh at all.

## Not done here

- `RuinScanner.prefab` still wears the cube: the mesh swap, `muzzle` → `Emitter`,
  `holdSize` 0.54 and the `Fitted` bracket are the prefab session's, per §7.
- `LIBRARY.md` / `library_index.json` were **not** regenerated — another session
  regenerates them once for the whole gauntlet batch.
- Not yet seen on the rig. `grapple_bracer_BUILD.md` measured its fit through the
  real `EquipItemSocket`; this model shares its frame and cuff so the same
  offsets should seat it, but "should" is not a measurement.
