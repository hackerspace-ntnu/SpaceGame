# lattice_outpost — build record

A 52 m stilted watch tower, built from a concept reference: an outpost stacked up
an X-braced steel mast in a red dust canyon — splayed legs on footings, a habitat
hull on a platform, a machine module clamped on halfway up, and a glazed
observation head under a thicket of aerials.

Brief as agreed: **52 m** to the aerial tip, the reference's **duotone** matched
with weathered variants alongside, the base carried **down to footings on
terrain** rather than cropped as the reference image is, and **one hero model**
rather than a family of background towers.

**Revised after review** (see *The sci-fi pass* below): the first build's
buildings read as terrestrial architecture — window grids, parapets, porched
doors — and the hulls and the observation head were rebuilt around a salvage
pressure-vessel language instead. The mast, the decks and the assembly logic
were unaffected; the levels moved by 0.4–1.5 m to suit the new envelopes.

- **Model:** `models/buildings/outpost/lattice_outpost.blend`
- **Extent:** 22.35 x 23.26 m footprint, ground (z=0) to **exactly 52.000 m**
- **159 objects / 45 unique meshes / 341 490 triangles**
- **Category `buildings/outpost/` reused**, not created — it appeared during this
  build from a parallel session's `relay_outpost`, and it is the right home.

## Height budget

| Range | What | Source |
|---|---|---|
| 0.00 – 6.20 | four raked legs onto footings | `LatticeMast_Splay` |
| 6.20 – 33.80 | the shaft, two modules | `LatticeMast_Bay` x2 |
| 6.15 – 9.00 | lower platform, top face 9.00 | `Truss_Deck` |
| 9.00 – 18.94 | the habitat hull and its roof farm | `OutpostBlock_Station` |
| 20.00 – 28.47 | machine module and its tank | `OutpostBlock_Plant` |
| 28.70 – 33.80 | upper deck, top face 33.80 | `LatticeMast_Collar` |
| 33.80 – 38.15 | blind service storey | `ControlCab_Annex` |
| 38.15 – 44.61 | the observation head, roof deck 43.17 | `ControlCab_Wide` |
| 43.17 – 52.00 | the aerial farm | unique geometry |

The habitat roof is at 16.40 and the upper deck starts at 28.70, so the middle
of the tower is **12 m of open lattice broken once** by the plant module. That
run is what the composition rests on. The first pass put the lower deck at 10.50
and the plant at 23.00, which left barely 6 m of visible steelwork and read as
three buildings stacked on a post rather than as a mast that carries buildings —
and it also drove the plant module's tank up through the upper deck's bracing.

## Reused from the library, unchanged

| Component | Variations used | Why it served |
|---|---|---|
| `structural/truss_frame` | Deck, Brace | `Truss_Deck` is a 20.2 x 16.4 m platform with its own understructure and an origin on the top face — exactly the lower platform. `Brace` ties it back to the splay head. |
| `structural/handrail` | Straight, Corner, Ladder | Player-scale railing round both decks, and the ground ladder under the stair. |
| `structural/catwalk_span` | Straight, Corner, Stair | The plant module's access walkway and the flight down to grade. |
| `structural/deck_plate` | Grate, Worn | Walkable decking on the platform strip, alternated so no tile repeats adjacently. |
| `structural/bulkhead_frame` | Door | The cab deck door. |
| `structural/mast_rig` | Antenna, Windvane | Small aerials on the habitat and cab roofs. |
| `mechanical/exhaust_stack` | Cowl | Habitat roof flue. |
| `mechanical/pipe_run` | Straight, CableBundle, Junction | The service riser climbing the mast. |
| `mechanical/vent_grille` | Louvre | Plant module flank. |
| `props/supply_crate` | Stack, Large, Pallet, Long, Open | Deck cargo — five different crates, none adjacent to its twin. |
| `props/fuel_barrel` | Stack, Drum, GasBottles | Deck fuel. |
| `props/field_bench` | Table, ToolRack, Generator | The deck is somewhere people work. |
| `props/floodlight_bank` | Quad, Twin | Site lighting on both decks. |
| `props/light_fixture` | Strip | Walkway lamps. |
| `props/hull_stencil` | DangerBand, Roundel, Chevron, Placard | Markings on the habitat and cab deck. |

Five of those (`exhaust_stack`, `field_bench`, `fuel_barrel`, `hull_stencil`,
`supply_crate`) were built by an earlier session in the hour before this one and
had never been placed in a model. This is the first thing that uses them.
`awning_shade/LeanTo` was placed in the first build and then removed — see the
sci-fi pass below.

Vent grilles, pipe runs and light strips are authored at vehicle scale; rather
than take the difference as object scale — which the library forbids — `scaled()`
bakes a uniform factor into one shared mesh copy per size, so every object in
the file is at scale 1.0 and a size is paid for once however often it is placed.

## New components

Three, each in the smallest unit that could plausibly recur.

| Component | Variations | Why it is separate |
|---|---|---|
| `structural/lattice_mast` | Bay, Splay, Collar, Taper, Cap | `truss_frame/Column` is **Warren-laced** — one zigzag web, which is what you build for a leg under a deck. This is an **X-braced tower mast**: crossed both ways in every bay, because a free-standing mast with modules hung off it is loaded in torsion and in wind from any bearing. That changes members per bay from one to two, puts a node mid-face where the diagonals cross, and is the most visible thing about a mast seen against a bright sky. Parameterising `truss_frame` to emit both webs would have been a second component wearing the first one's name. |
| `structural/control_cab` | Wide, Compact, Annex, Drum, Derelict | The reference's signature, and nothing in the library had it. Every other habitable box here — `cabin_module`, `hab_capsule`, `tower_bay`, `slab_block`, `outpost_block` — is a hull you look out of through as few holes as possible. This is the one place on an outpost that exists to *see*, so it is the one place that spends its armour budget on a continuous band of glass, **canted outward** so somebody at it looks down without the lit room reflecting back. The cant does not survive being faked — the mullions lean with it and the visor genuinely overhangs the floor. |
| `structural/outpost_block` | Station, Plant, Hab, Annex, Bleached, Breached | A **sealed pressure hull hung up a mast** on a world trying to get in: battered chamfered mass, salvage-plate skin, external ribs, and almost no openings. `tower_bay` is clad refinery frame, `slab_block` is a rusted plate-field hulk, `cabin_module` is a 5 m container conversion, `prefab_hab` is a single-storey unit you walk into off the sand. None of them is a sealed hull 10–30 m in the air. |

### Built ahead of the request

16 new variations exist; **7 are placed** in this model. Nine are pure overshoot
with no home yet — a deliberately high ratio, because the marginal cost of
another variation once a component's structure and materials exist is small:

- `LatticeMast_Taper` / `Cap` — a narrowing upper run and a railed mast head.
  The composition ended with the cab on the collar deck and the mast stopping
  there, which is what the reference shows; a tapered section above it would
  have pushed the tip past 52 m.
- `ControlCab_Compact` / `Drum` / `Derelict` — a three-sided watch cab, an
  octagonal approach cab, and the blown-out weathered one.
- `OutpostBlock_Hab` / `Annex` / `Bleached` / `Breached` — a tall hull with a
  vertical slit and a setback, a low blind store behind an armoured shutter, and
  the two sun-killed ones.

`Bleached` and `Breached` are what the brief's "weathered variants" asked for.
They are sun-killed rather than oxidised **and** differ structurally — heavier
over-plating, a radiator with half its fins gone, an unlit slit, a torn-out
corner with the frame showing — because a variation that is only a repaint is
not a variation.

## Unique to this model

Two pieces, both justified by being the specific answer to *this* building:

- `Mesh_LatticeOutpost_Aerials` — the last nine metres. `mast_rig` covers whips
  at vehicle scale and `drill_derrick/Antenna` is a 20 m guyed mast; neither is
  a 9 m cluster on a 9-by-8 roof. It is a cluster rather than one pole because a
  single mast on a symmetrical roof reads as a flagpole. Its tip sets the
  building's final height exactly.
- `Mesh_LatticeOutpost_LadderRun` — the caged ladder joining the two decks,
  23.30 m with landings every 5.82 m. Defined entirely by the two levels it
  connects. Without it the upper deck is a place nobody can reach, and that is
  the first thing that reads as unbuilt.

## The sci-fi pass

The first build was reviewed as *"way too human, not sci fi — I don't want a
bunch of windows in a grid"*, and that was correct. Reading it back, the
buildings were carrying four separate cues that all say **terrestrial civic
architecture**, none of which had anything to do with the reference:

| Cue | Why it reads as Earth | Replaced with |
|---|---|---|
| Regular grid of punched square windows | An office block. It says people expect to open a window, on a world where opening one kills you. | **One** long canted armoured slit per hull, plus two or three tiny bolted ports placed in ones and twos |
| Flat roof behind a parapet capping band | A municipal roofline | A dark equipment farm: tanks, fin banks, stack hoods, duckboards |
| Door with a projecting porch canopy | A domestic threshold | A pressure hatch with cut top corners, dogging lugs and a hood |
| Plain vertical box, symmetrical elevations | A hut | A **battered** section (~6°) on a **chamfered** octagonal plan, everything placed once instead of mirrored |

And four things were added that have no terrestrial equivalent at all: radiator
fin banks, verdigris conduit wrapping the hull, strapped pressure-bottle
clusters, and a skin of mismatched salvage plate in four states of oxidation.

**The batter is what made this expensive, and it is what makes it work.** Once
the walls lean, every detail placed at a fixed half-width hangs 200–800 mm off
the face somewhere up its height, which reads as decals floating beside the
building. So `outpost_block` was refactored so that nothing takes `w, d` — every
helper takes a hull tuple `(z0, z1, w0, d0, w1, d1)` and asks `hull_at()` for
the half-width at the exact height it is being placed. Ribs and slits are built
between two such queries so they lean with the wall they are on.

The observation head kept its canted glazing — that was always the right idea
and it is what the reference has — but gained a projecting **visor** on angled
stays, a chamfered plan, a flared bearing, corner sensor pods and a rusted
plated skirt. Its `Annex` went from twenty little windows to plate, ribs, a
louvre bank and a conduit spine.

Two lighting mistakes were made and corrected in the same pass, both the same
mistake: **emissive is an accent, not a fill.** The first attempt made the whole
slit amber, which stopped being a window and became a lightbox; the second lit
facet 0 of the cab band, which is the *largest* panel on the head. Slits are now
dark with two of four bays lit, and the cab's glow sits on the small corner
chamfers with the four long faces dark.

The mast was left alone structurally — an X-braced steel lattice was already the
least terrestrial thing in the model — and only gained oxide patches so it
weathers with the hulls it carries instead of staying factory-grey under them.
The canvas awning on the lower deck was deleted: a tarp over a camp table was
the last object on the model that said *people camping* rather than *people
surviving*. The crates and drums stayed; they read as freight.

## Materials

One added to the palette; the guard was consulted first and cleared it —
`#D9705E` had nothing within its threshold.

| Material | Hex | For |
|---|---|---|
| `Mat_Paint_Coral_Faded` | `#D9705E` | Sun-faded coral enamel: the habitat blocks, cab roof band and machine module skins. Distinct from `Mat_Paint_Safety_Orange` (fresh high-vis construction paint) and `Mat_Metal_HullRust_Orange` (oxidised bare steel, not a painted surface). |

The rest of the duotone came out of the palette unchanged: `Mat_Paint_Blue_Station`
for the cool bodies, `Mat_Neutral_Slate_Dark` for the dark trim and the window
panes, `Mat_Metal_Steel_Worn` for all the steelwork. The reference's grey-blue
structure and near-black glazing were already covered.

**The glazed band is dark by default, not glass.** One continuous sheet of
`Mat_Glass_Canopy_Tinted` is the obvious build and it is wrong: that material is
a pale tinted grey, correct for a canopy at arm's length, and at building
distance it turns the band into a light stripe. A control cab seen from outside
is near-black with the odd pane catching the sky, so the panes are slate and one
in three is glass.

## Decisions worth revisiting

- **The shaft passes through the lower habitat**, not beside it. A mast carrying
  a building 30 m up cannot stop at a roof and pick up again, and the reference
  shows the lattice emerging from the block's roof rather than clearing its
  edge. The cost is mast members hidden inside a solid block.
- **`outpost_block` overlaps `prefab_hab`**, which a parallel session added to
  the library at 18:23 while this was being built. That one is single-storey
  ground-level prefabs (3.9–5.7 m) in pale blue; this one is 7.4–9.2 m
  deck-mounted blocks with window grids and the coral skin. They are genuinely
  different products of the same idea and are worth consolidating later — that
  is a merge nobody should do blind, so it is flagged rather than done.
- **344 k triangles** is a hero-landmark budget, in the same class as
  `refinery_tower`'s 378 k. The cheapest reductions are the habitat's window
  grid (~7 k of the block's 9.5 k, four boxes per window) and the 36 deck plates.
- **No armature.** Nothing on the reference moves. The candidates if that
  changes are the cab door, the roof dishes and the plant module's louvres.
- **The upper deck was widened from 9.8 m to 12.6 m** inside `lattice_mast` after
  the first assembly: a 9 m cab on a 9.8 m deck leaves a 0.4 m ledge, which is
  not a walkway. The first pass tried to fix it with cantilevered catwalk spans
  hung off the edge, and those read as four separate shelves with gaps at the
  corners. The deck was already there; what it needed was a handrail.
- **Non-manifold edges** (3+ faces) exist in `ControlCab_Wide`/`Derelict` and
  `OutpostBlock_Hab`/`Breached`. This is endemic to `_buildlib`'s box-union
  style — `remove_doubles` welds separately-created boxes that happen to touch —
  and pre-existing shipped components have it at the same rate
  (`Truss_Deck` 24, `Crate_Stack` 40, `ExhaustStack_Cowl` 10). It does not
  affect rendering or the Unity import, but it would affect booleans or
  solidify.

## Verification

All three new component files and the assembly pass the library's checklist:
every object deliberately named with no auto-suffixes, every object at scale
1.0, metric units at 1.0, no loose or coincident vertices, no empty material
slots, every material linked from the palette with no `.001` duplicates. The
assembly measures **0.000 to 52.000 m** — the aerial tip lamp was trimmed by
10 mm to land it exactly.
