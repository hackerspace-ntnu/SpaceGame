# gyre_test_section — build record

First batch of the Gyre family: the test set from `ancient-ruin-poi/09-model-kit.md` §3, plus the test section that assembles it. Design rules: `ancient-ruin-poi/06-design-language.md` (v2).

Built headless with Blender 5.1.2. Every file links its materials from `palette.blend`, and none was added to the palette.

## Reused from the library

- `_buildlib.py`: `Part`, `start`, `link_materials`, `append_objects`, `report`.
- Palette materials:

| Gyre role | Palette material |
|---|---|
| Carved sandstone | `Mat_Stone_Sandstone_Pale` |
| Deep reveal tint | `Mat_Stone_Sandstone_Tan` |
| Dark metal (mechanisms, inlays) | `Mat_Metal_Steel_Dark` |
| Polished metal (lips, wear bands, chamfers) | `Mat_Metal_Steel_Worn` |

No existing component fitted. The library's mechanical parts are colony and vehicle hardware, in a different language.

## New shared helper

`components/structural/gyre/_gyrelib.py` holds the Gyre constructions, so every insert agrees on profile, groove and step size:

- catenary and parabolic arch curves, with curvature-weighted sampling
- curve offsets that keep the feet on the floor
- stepped reveal sections with an incised line pair and an optional lip notch
- sweeps along open and closed curves
- revolves
- frame sweeps for ribs and inlays

It also has `gyre_save`, which keeps shared meshes named after their source.

## New components

| File | Variations | Why it is its own component |
|---|---|---|
| `components/structural/gyre/gyre_arch_reveal.blend` | Small/Mid/Large × 2- and 3-step, Mid 3-step with metal lip (7) | The opening insert; the shell only carries a plain hole |
| `components/structural/gyre/gyre_oculus_reveal.blend` | R1.5/R3/R5/R8 × centred/offset (8), families A/B/C (linked duplicates) | Round openings recur everywhere, in families |
| `components/structural/gyre/gyre_socket_reveal.blend` | Plain, Ringed (with inlay ring) | Every rod and spoke enters through one |
| `components/structural/gyre/gyre_grown_column.blend` | H12, H20, H32, H20 Lean, H20 Split, H12 Fan, H20 Fan | Band and inlay are separate objects so they take their own materials and can glow |
| `components/structural/gyre/gyre_rib_fan.blend` | 4, 6, 3Asym, 4 Rising, 6 Rising | Sits on any column top; Rising fans run on into vaults |
| `components/structural/gyre/gyre_inlay_strip.blend` | Straight 4/8 m, Arc R4/R8/R16, S-bend, Merge, End Socket, End Round | The conduit kit; one material, so it can all switch to glow together |
| `components/mechanical/gyre/gyre_rod.blend` | 6 m, 12 m, 24 m, each with `Arm_GyreRod_*` / `Bone_RodSpin` | Moves, so it is rigged; the mesh is parented to the bone |

## How the test section assembles

- **Shell (unique to this model):** `Mesh_GyreTest_VaultShell`.
  - A 22.5 m length of hall, 28 m wide at the floor and 32 m high, with 3 m thick walls.
  - Filleted wall-floor joints.
  - Incised pairs along the vault at 7 m and 15 m up each side, and over the crown.
  - Incised pairs around the vault at y = 5 and y = 11.
  - A channel at y = 16 holding `Mesh_GyreTest_InlayRib`.
- **End wall (unique):** `Mesh_GyreTest_EndWall`, at y 20 to 22.5. Its holes are cut from the same curves the inserts use, so the seams fall on the reveal edges.
- **Openings:**
  - Arch: Mid 3-step with lip, on the floor at the centre.
  - Oculus family: R5 Offset at (0, 24), R1.5 Offset at (7, 18.6), R1.5 Centred at (−7, 18.2).
  - Ringed socket at (10, 4).
- **Columns:**
  - Two `H12_Fan` columns at x = ±5.6, y = 12.
  - Each carries a `4_Rising` fan turned 45°, so the inner ribs cross over the hall's centre line and the outer ribs bury their tips in the vault.
  - The left-hand pieces are linked duplicates.
- **Mechanism:** `Arm_GyreRod_6m`. Its root is 1 m inside the socket bore and it points into the hall, along −Y.
- **Conduits:**
  - A floor line of two 8 m strips and one 4 m strip running into the arch, 2 cm proud.
  - `Mesh_GyreTest_WallConduit`, unique, from the socket's inlay ring up to the right-hand oculus.

## Numbers

- Test section: about 23.5k triangles in total. The shell is about 5k, under the 6k chunk limit.
- Components range from 28 triangles (a straight inlay) to about 2,100 (an H12 column).

## Judgement calls

- **Arches are catenaries, not parabolas.** A tall parabola reads as a gothic lancet at these proportions, and a catenary keeps the crown round. Mid is 10 × 15 m, Large 16 × 24 m.
- **Fan columns have no top flare.** Ribs on a flared top read as a capital, which breaks the not-Roman rule.
- **Inlays on flat faces sit 2 cm proud.** Inlays in channels sit 5 mm below the stone. Either way, nothing is coplanar.
- **Columns carry their conduit up the +X face.** In the test section both columns are turned so it faces the hall's centre line.
- **Oculus reveals are shallower than the end wall.** The last 0.5–1 m of each hole shows behind the reveal, which is only visible from outside the hall.
- **Dark metal is the palette's `Steel_Dark`,** a cooler grey than the design language's warm near-black. Reusing it follows the palette guard. A warm metal would be a deliberate palette addition.

## Built ahead

The test set needed:

- the Mid arch with lip
- the R5 and R1.5 oculi
- the Ringed socket
- the H12 Fan column
- the 4 Rising fan
- the straight inlays
- the 6 m rod

Everything else was built for the rest of the POI: Small and Large arches, R3 and R8 oculi, the oculus families, Plain socket, H20/H32/Lean/Split columns, 6-rib and asymmetric fans, arcs, S-bend, Merge, the end pieces, and the 12 m and 24 m rods.
