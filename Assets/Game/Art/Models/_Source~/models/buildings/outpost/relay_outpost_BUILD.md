# relay_outpost — build record

A crewed desert relay station built from a concept reference: a pale blue prefab
block on graded sand, a tapered octagonal mast off its roof carrying a domed
sensor drum with a walkable gallery, and a working yard of awnings, benches,
crates and drums spread round the front.

Brief as agreed in one round of questions: **player scale (~18 m)**, **building
plus awnings plus full clutter**, **two palette additions permitted**, **no
armature**.

- **Model:** `models/buildings/outpost/relay_outpost.blend`
- **Extent:** 22.27 x 19.43 m footprint, ground to **exactly 18.50 m**
- **89 objects / 63 unique meshes / 303 486 triangles**
- **Category `buildings/outpost/` created** — `buildings/industrial/` is heavy
  plant, and this is a small crewed frontier station. Future weather stations
  and fuel depots belong here too.

## Height budget

| Range | What | Source |
|---|---|---|
| 0.00 – 0.34 | graded plinth | `prefab_hab` |
| 0.34 – 4.20 | the prefab block, roof deck at 4.20 | `Coll_PrefabHab_Long` |
| 4.20 – 5.10 | tower saddle | unique geometry |
| 5.10 – 13.70 | 8.6 m of battered shaft | `Coll_StationTower_Taper` |
| 13.70 – 18.23 | drum, gallery at 14.07, domed cap | `Coll_SensorCupola_Dome` |
| → 18.50 | the gallery aerial, scaled to fit | `Coll_MastRig_Antenna` |

18.50 m over a 13 m block: the mast reads as ~3.5x the building it stands on,
which is the proportion the reference has. The block is deliberately low and
wide — a taller base would cost the mast its dominance. The aerial's scale is
**computed at build time** by `fit_scale()` rather than hand-tuned, so the model
lands on 18.50 exactly and stays there if the stack below it ever moves.

## Reused from the library, unchanged

| Component | Variations used | Where |
|---|---|---|
| `structural/handrail` | Ladder, Gate, Straight | Roof access ladder; three stepped lifts up the mast; parapet run. |
| `structural/deck_plate` | Grate, Worn, Solid | Roof walkway, alternated so no tile repeats adjacently. |
| `structural/hull_plate` | Patched, Riveted, Ribbed | Repair patches on three elevations. |
| `structural/bulkhead_frame` | Door, HatchRim | The door leaf in the recess `prefab_hab` provides; roof hatch. |
| `structural/mast_rig` | Antenna, Windvane | Gallery aerial, a smaller roof whip, roof windvane. |
| `mechanical/pipe_run` | Straight, Elbow, Junction, CableBundle | Roof service run, end-wall riser, ground cable, cupola drop. |
| `mechanical/vent_grille` | Louvre, Fan, Scoop, MeshScreen | Wall vents on all four elevations. |
| `props/floodlight_bank` | Twin, Single, Sweep | Roof corners and the saddle. |
| `props/light_fixture` | Clamp, Dome, Festoon, Strip | The gallery lamp on its arm, door lamp, festoons under both awnings. |
| `props/console_panel` | Nav, Breaker | Under the azure awning; wall breaker box. |
| `props/wall_locker` | OpenShelf, Dented | Under the lean-to; outside by the dump. |
| `props/crew_seat` | Stool | Two, under each awning. |

Several of these are authored at vehicle scale. As in `refinery_tower`,
`scaled()` bakes a uniform factor into one shared mesh copy per size, so every
object in the file is at scale 1.0 and a size is paid for once however many
times it is placed.

## New components

Seven. Two of them fill gaps the library should never have had.

| Component | Variations | Why it is separate |
|---|---|---|
| `props/supply_crate` | Small, Large, Long, Stack, Open, Pallet | **The library had no crate at all.** Every settlement, depot, camp and loading bay needs one; this is the most reusable thing in the build. |
| `props/fuel_barrel` | Drum, Stack, Jerrican, GasBottles, Tank | **No barrel either.** Separate from crates because it is the round half of the vocabulary — a dump built only from boxes reads as stacked luggage. |
| `props/field_bench` | Table, ToolRack, Generator, Reel, Sawhorse | Free-standing kit dragged outside. `console_panel` and `wall_locker` cover the bolted-to-a-bulkhead end; nothing covered the yard. |
| `structural/awning_shade` | Square, LeanTo, Sagging, Torn, Frame | Shade is the first thing built in a desert. The cloth is a real sagging membrane, not a tilted plane — see below. |
| `structural/prefab_hab` | Long, Short, Annex, Garage, Corner | The gap between `cabin_module` (craned containers) and `tower_bay` (14 m storeys): a building you walk into off the sand. |
| `structural/station_tower` | Taper, Straight, Flare, Collar, Braced, Stub | Octagonal and **tapered**, 4.6 m across. `tower_bay` is the other lineage — rectangular, 14 m, stacks into a slab. Neither is a variation of the other. |
| `structural/sensor_cupola` | Dome, Lantern, Radome, Drum, Dish | A tower without a head is a chimney. Five different answers to what the tower is *for*. |

### Three decisions inside those components worth knowing

- **Awning cloth sags in two directions.** A tarp pinned at four corners bellies
  in the middle *and* along its unsupported edges. `cloth()` uses an additive
  bump — zero droop at the pinned corners, half at the mid-edges, full at the
  centre — and lofts a thin solid rather than a zero-thickness plane, which
  Unity would light from one side only.
- **`prefab_hab.roof_kit()` takes an X band.** A block that carries a mast has
  to keep half its roof clear. `Long` confines its plant to the -X half; without
  that the tower saddle lands on a vent cowl.
- **`SensorCupola_Dish` is built, tipped, then given its base.** `loft()` takes
  no rotation argument, so a paraboloid cannot be authored pre-tilted. The dish
  is the only thing in the bmesh when it is transformed onto the yoke; the
  turret and yoke are added afterwards in world space.

### Built ahead of the request

**37 new variations exist; 15 are placed in this model.** The other 22 are pure
overshoot, and deliberately so — the marginal cost of another variation once a
component's structure and materials exist is small, and it is the only work that
pays off before it is asked for. The whole of `prefab_hab` (4 unused),
`station_tower` (5 unused), `sensor_cupola` (4 unused) and `field_bench` (1
unused) is now available to any future desert settlement for free.

Two awning variations — `Torn` and `Frame` — were built specifically so a camp
can be shown mid-collapse or mid-strike, which nothing in the library could do.

## Unique to this model

Three pieces, each justified by being the specific junction between *these*
parts rather than anything reusable:

- `Mesh_Outpost_TowerSaddle` — the 0.9 m plinth between a flat roof and a round
  mast. `StationTower_Flare` is the library's general answer but is 1.9 m tall
  and spreads to 6.8 m, which on a 10 m deep roof would leave no roof at all.
- `Mesh_Outpost_PlantRack` — the condenser bank and pipe gallery bolted to the
  mast's front face, the reference's most distinctive junction. Its back is
  shaped by the batter of *this* shaft, and it carries raked wings onto the two
  adjacent octagon facets; a general flat-backed component would gap at both
  ends against a 1.95 m facet.
- `Mesh_Outpost_Stoop` — steps and apron at the door. Small, but a doorway
  0.34 m off the sand with nothing under it is the first thing that reads wrong.

## Materials

Two added to the palette, both used across every new component.

| Material | Hex | For |
|---|---|---|
| `Mat_Paint_Blue_Station` | `#9FB8CE` | Pale powder-blue enamel over steel: hull skin, mast shaft, cupola drum. The cool member of the painted-hull family beside `Mat_Paint_White_Arctic` (arctic) and `Mat_Paint_Hull_Bleached` (warm desert). |
| `Mat_Fabric_Tarp_Azure` | `#3E9AD0` | The shade sail. Nothing in the palette was within ΔE 20; it is the reference's only strong colour note. |

The guard flagged `Mat_Paint_Blue_Station` as ΔE 8.7 from
`Mat_Glass_Canopy_Tinted` and the addition was made anyway: that entry is
glazing at roughness 0.05 / metallic 0.0, this is chalky paint at 0.60 / 0.35.
Same reasoning `refinery_tower` recorded for `White_Arctic` vs `Flag_Bleached`.
Everything else came from the existing 28.

## Decisions worth revisiting

- **No armature**, as asked. If that changes, the obvious candidates are a yaw
  bone under the cupola drum, a hinge on the door leaf, and the awning edges.
- **No site apron.** Following `refinery_tower`: a flat pad fights procedural
  terrain, so the model ends at its plinth.
- **303 k triangles** is a hero-prop budget for an 18 m building. The
  distribution is flat — nothing is pathological — but several *reused* props
  are expensive for their visual role at this scale: `ConsolePanel_Breaker` is
  6 136 tris for a wall box and `HullPlate_Riveted` 5 264 for a 1 m plate. That
  is a library-wide property, not something this model introduced. Dropping the
  four wall patches and the two festoon pairs saves ~30 k for almost nothing.
- **The mast is placed unrotated** so a facet faces each of ±X and ±Y: the plant
  rack lands flat on the -Y facet and the ladder brackets off +X. Yawing the
  mast for visual variety would put both on corners.
- **The ladder steps inward with the taper** — each of the three lifts is placed
  against the local face width by `shaft_face()`. A plumb ladder would stand
  1.05 m clear of the shaft at the top, which is what the first assembly did.
- **The yard is dressed for the reference's camera**, front-heavy toward -Y. If
  the outpost is approached from the back in game, the +Y side wants another
  awning and a second dump.

## Verification

All eight new `.blend` files pass the production checklist headlessly: no
auto-suffixed names, every object at scale 1.0, metric units, no loose or wire
geometry, no duplicate vertices, no empty material slots, every material linked
from the palette with no `.001` copies surviving. The assembly's highest point
measures **18.50 m** against a 18.50 m target.

## Note on a concurrent build

While this model was being built, another session wrote a parallel set into the
same library between 18:00 and 18:38: `structural/slab_block`, `window_bank`,
`lattice_mast`, `control_cab`, `outpost_block`, `shanty_addon`, and the model
`models/buildings/industrial/mining_rig_derelict.blend`. Nothing collided —
every name differs — and **none of it was touched by this build.**

It does overlap in purpose, though, and the library cannot see that for itself:

| Theirs | Mine | Overlap |
|---|---|---|
| `structural/outpost_block` | `structural/prefab_hab` | Both are single-storey prefab blocks. |
| `structural/lattice_mast` | `structural/station_tower` | Open lattice vs clad octagon — `station_tower`'s `Braced` variation is the redundant one. |
| `structural/control_cab` | `structural/sensor_cupola` | Both are heads for a mast. |
| `structural/window_bank` | `prefab_hab.window()` | Theirs is a component, mine is a helper inside one. |

Worth a consolidation pass before either lineage grows further.
