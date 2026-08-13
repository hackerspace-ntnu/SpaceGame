# refinery_tower — build record

A 75 m arctic drilling refinery, built from a concept reference: a white clad
slab tower on splayed orange legs, with cantilevered modules, wrapped catwalks,
an outrigger plant deck and a conveyor gallery running out to the ground.

Brief as agreed: ~75 m tall matching the reference, arctic palette additions
permitted, hero detail for a landmark the player can walk up to, no armature.

- **Model:** `models/buildings/industrial/refinery_tower.blend`
- **Extent:** 87.2 x 47.1 m footprint, ground (z=0) to **exactly 75.00 m**
- **167 objects / 57 unique meshes / 378 288 triangles**
- **Category `buildings/industrial/` created** — nothing existing was a home
  for it. `models/buildings/tower.blend` is a separate hand-modelled file and
  was **not touched**.

## Height budget

| Range | What | Source |
|---|---|---|
| 0.0 – 8.0 | podium, dark machinery mass | unique geometry |
| 8.0 – 53.0 | five stacked storeys, 9 m each | `tower_bay` |
| 53.0 – 64.5 | crown incl. stacks | `Coll_TowerBay_Crown` |
| 59.8 – 75.0 | twin masts on the crown deck | unique geometry |

The white slab is 45 m over a 14 m face (3.2 : 1); read as one mass with the
crown it is 3.9 : 1, which is the proportion the reference actually has. The
podium is dark on purpose — a white base would cost 8 m of apparent height.

## Reused from the library, unchanged

| Component | Variations used | Why it served |
|---|---|---|
| `structural/cabin_module` | Cargo, Workshop, Comms | Converted containers on the outrigger deck. Deliberately left in the **warm desert palette** so the deck reads as older kit parked under a newer tower. |
| `structural/handrail` | Straight, Ladder, Stair | Player-scale railing at ground level, where the detail is worth paying for. |
| `structural/deck_plate` | Grate, Worn | Podium roof plating, alternated so no tile repeats adjacently. |
| `structural/mast_rig` | Antenna | Small antennae on the crown deck. |
| `mechanical/pipe_run` | Straight, Elbow, Junction | Service run across the podium roof. |
| `mechanical/vent_grille` | Louvre, Fan | Podium flanks. |
| `props/floodlight_bank` | Quad, Twin, Sweep | Site lighting on every large mass. |
| `props/light_fixture` | Clamp, Strip | Walkway and deck lamps. |

Several of these are authored at vehicle scale. Rather than take the difference
as object scale — which the library forbids and Unity dislikes — `scaled()` in
the build script bakes a uniform factor into one shared mesh copy per size, so
every object in the file is at scale 1.0 and a size is paid for once however
many times it is placed.

## New components

Seven, all in the smallest units that could plausibly recur. Each has 5–6
variations differing in **silhouette or structure**, not only colour.

| Component | Variations | Why it is separate |
|---|---|---|
| `structural/tower_bay` | Plain, Windowed, Ribbed, Buttressed, Shoulder, Crown | The stackable storey. Everything above the podium is six of these; `Shoulder` and `Crown` deliberately break the base envelope because a setback and a tapered machine deck are the only silhouette events the stack has. |
| `structural/catwalk_span` | Straight, Wall, Balcony, Corner, Bridge, Stair | Building-scale walkway. `handrail` is better up close but costs 309 tri/m of rail; this runs ~72, and brings deck and brackets with it. The two are dimensionally compatible (1.10 m rail height). |
| `structural/support_leg` | Raked, Splayed, Pier, Strut, Footing | Clad static load paths. Distinct from `mechanical/walker_leg`, which articulates and is proportioned by stride. |
| `structural/truss_frame` | Column, Beam, Portal, Brace, Deck | The open half of the same language — where a heavy structure shows its lattice instead of cladding it. |
| `structural/hab_capsule` | Long, Short, Tank, Cab, Pod | Rolled pressure hulls with domed ends. The counterpart lineage to `cabin_module`'s boxy container; the contrast between round and boxy is most of what makes the pile read as accreted rather than designed at once. |
| `mechanical/drill_derrick` | Mast, PipeRack, Winch, Antenna, Flare | The machinery that crowns a rig. `Antenna` is the building-scale sibling of `mast_rig`'s vehicle whip — at 20 m, guys and dishes start to matter. |
| `mechanical/conveyor_ramp` | Ramp, Flat, Head, Hopper, Trestle | The strong diagonal. `Ramp` is authored **inclined at 23°**, not flat-and-rotated: an inclined gallery has vertical trestles under a raked chord, which does not survive being modelled flat. |

### Built ahead of the request

37 new variations exist; **35 are placed in this model**. Only two are pure
overshoot with no home yet:

- `SupportLeg_Pier` — a plain 14 m clad pier. The outrigger deck ended up on
  lattice `Truss_Column`s instead, which suited an open steel deck better.
- `Derrick_Antenna` — a 20 m guyed comms mast. The crown's own twin masts
  cover that job here at the height the budget allowed.

The wider overshoot is not in the unused count but in the *breadth*: roughly a
dozen of the placed variations (`Catwalk_Straight`, `Truss_Portal`,
`Truss_Brace`, `HabCapsule_Cab`/`Pod`, `Derrick_Winch`/`Flare`,
`Conveyor_Flat`/`Trestle`/`Hopper`) were built because the component's
structure and materials already existed, and then found work. Any future
industrial or arctic asset gets all 37 for free.

## Unique to this model

Three pieces, all justified by being the specific junction between *these*
parts rather than anything reusable:

- `Mesh_Refinery_Podium` — the dark mass the tower stands on, punched with
  service bays and a vehicle portal.
- `Mesh_Refinery_CrownMasts` — the last 15 m. Two masts, not one: a single
  mast on a symmetrical crown reads as a flagpole.
- `Mesh_Refinery_CapsuleCradle` — brackets and a waist hoop under the 17 m
  cantilevered capsule. Without visible load path a module that size reads as
  a box floating near a box.

## Materials

Two added to the palette; a third was **refused by the guard and the refusal
was correct** — `#4A5560` came out ΔE 9.5 from `Mat_Neutral_Panel_Grey`, and
between `Mat_Metal_Steel_Dark`, `Mat_Metal_Steel_Worn` and
`Mat_Neutral_Slate_Dark` the dark understructure was already covered.

| Material | Hex | For |
|---|---|---|
| `Mat_Paint_White_Arctic` | `#D6DAD9` | Cool off-white enamel: the slab cladding and module skins. Deliberately cooler than `Mat_Paint_Hull_Bleached`, which is warm desert sun-bleach, and than `Mat_Fabric_Flag_Bleached` (ΔE 9.0, but fabric at roughness 0.9 vs painted steel at 0.58/0.35). |
| `Mat_Paint_Safety_Orange` | `#D9541F` | Legs, cantilever spine, conveyor, accents. Nothing in the palette was within ΔE 20. The weathered counterpart `Mat_Metal_HullRust_Orange` already existed and is used alongside it. |

Every mesh in every file links its materials from `palette.blend`; the assembly
folds any `Mat_X.001` back onto `Mat_X` after appending from fifteen files.

## Decisions worth revisiting

- **No site apron.** A flat concrete pad would fight procedural terrain, so the
  building ends at its footings. Every leg brings its own buried pad.
- **No armature**, per the brief. The crane-less derrick, the hopper gate and
  the conveyor drum are the obvious candidates if that changes.
- **378 k triangles** is a hero-landmark budget. The cheapest reduction is the
  catwalk wraps (~60 k across five levels); dropping the two partial levels
  costs little visually.
- **Desert-palette cabin modules on the outrigger deck** is a deliberate
  contrast, not an oversight. Swap to `Mat_Paint_White_Arctic` variants if the
  arctic reading should be uniform.
- Each generator script keeps its own local `along()` rotation helper rather
  than extending `_buildlib`, so every script stays independently runnable as
  the historical record it is.

## Verification

All eight new `.blend` files pass the production checklist: no auto-suffixed
names, every object at scale 1.0, metric units, no loose geometry, no empty
material slots, every material from the palette. The assembly's highest point
measures 75.00 m.
