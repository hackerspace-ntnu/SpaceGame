# SkyCity — build record

The Sky Tribe's floating home. The faction design has called for it since
2026-09-07 — *"Sky Tribe… they live in **floating vehicles in the sky** (not
created yet — backlog, §8)"* — and nothing had been built.

Built 2026-09-15. This is a record of decisions, not a proposal.

---

## What it is

A salvaged industrial lifting hull with a nomad town grown along its flanks.
Three gas bags hang in an open cage; a promenade runs the length of the keel on
both sides; dwellings, awnings and hanging walkways are bolted outboard of it;
two triangular sails drive it. The fiction the shapes carry is that **somebody
else built the lattice** — hazard yellow, riveted, rectilinear — and the tribe
moved into it. Painted steel below, cloth and timber above and outboard, and the
two languages never blend.

**Envelope**

| | |
|---|---|
| overall | 30.1 × 125.0 × 52.7 m |
| hull without sails or rudder | 22.4 × 116.0 × 21.0 m |
| promenade | 6.8 m wide each side, 104 m long, walking surface z = 0 |
| clear walking lane | x 4.2 → 7.0, both sides, unobstructed end to end |
| headroom under the cage | 10.0 m at the outboard rail, 2.7 m at the keel |
| triangles | 282,008 across 109 objects |

Authoring frame is the library convention — **−Y forward, +X starboard, +Z up**
— origin on the keel centreline at the promenade's walking surface.

---

## The one structural idea

The bags hang inside an **open cage**: eleven ring frames on six longitudinal
stringers, at a constant 8.4 m radius about an axis 10 m above the deck. The
three bags are 7.0, 7.6 and 7.2 m. Each rattles inside the cage by a different
amount, and that difference is the whole salvage story in one number — the cage
was not built for these bags.

It also does the level-design work. The cage sweeps down over the side decks, so
walking inboard you duck and walking outboard opens to sky. That gradient is
what makes the deck read as a street with an edge, without a sign saying so.

---

## Decomposition

### New components

| Component | Why it is separate |
|---|---|
| [`components/structural/gas_envelope`](../../components/structural/gas_envelope.py) | The library had no envelope of any kind. `fuselage_pod` is a rigid aircraft body and `hab_capsule` a pressure vessel people live inside; neither reads as cloth holding gas. Four variations — `Plain`, `Banded`, `Patched`, `Small`. Any airship, balloon or gasbag can use it. |
| [`components/structural/lateen_sail`](../../components/structural/lateen_sail.py) | `sail_rig` is the camp's *shade* hardware — timber poles and ground anchors for cloth pitched over sand. A driving sail on a spar is a different object. Three variations — `Main`, `Patched`, `Reefed`. |

Both are built at component scale (24 m bag, 26 m luff) and the model scales
what it needs, so neither is specific to this ship.

### Reused, unchanged

`shanty_addon`, `hab_capsule`, `cabin_module`, `control_cab`, `sensor_cupola`,
`catwalk_span`, `handrail`, `awning_shade`, `mast_rig`, `window_bank`,
`supply_crate`, `fuel_barrel`, `floodlight_bank`, `camp_lantern`.

### Bespoke, in `sky_city.py`

Keel girder, cage, decks, cradles, prow, stern gear, crown gantry, sail
outriggers, and every ladder. Primary structure is bespoke because no kit part
is 116 m long; the ladders are bespoke for cost, below.

---

## Where the constitution bore on it

Both principles are `contextual`, and both are applied here rather than merely
cited — the empty apron and the two-tier railing rule are the consequences.

**GDC-L1-LEVEL-0002** — *make space legible; paths, edges, districts, nodes,
landmarks.* A 116 m walkable deck is a level, and an evenly cluttered one is a
samey maze. So:

- **Path** — the promenade is a continuous unobstructed lane at x 4.2–7.0, ply
  over steel with an orange edge strip, running the whole length. Every dwelling
  is outboard of it. A run of deck lamps alternates down it.
- **Edge** — the orange strip, then the drop.
- **Districts** — the three bags divide the ship into three, and they differ in
  what they hold: homes forward, market amidships, workshops aft.
- **Nodes** — the ring frames at the district boundaries.
- **Landmarks** — helm cab forward, dish and dome aft, the two sails, and the
  crown gantry above everything.
- The **aft apron is deliberately empty**. It is what makes the rest read as
  crowded rather than merely noisy, and it is where a wing-pack nomad lands.

**GDC-L1-PERF-0004** — *budget the frame.* One hero landmark on screen with a
world behind it. `catwalk_span` (72 tris/m of railing) does every run above head
height; `handrail` (309 tris/m) only the apron stretch a player stands at. Every
repeated dwelling, crate and lantern is a `stamp` copy sharing one mesh
datablock, so 109 objects cost about 30 meshes.

---

## What went wrong, and what it cost

Recorded because each was silent — nothing errored, and the model looked fine in
the view that happened to be open.

- **Bands built on a circle over a squashed bag.** `Patched` is an ellipse in
  section, and its ribs and straps were offset radially. Every band floated up
  to 0.85 m off the top of the bag it was supposed to be clamping. Offsets must
  follow the **ellipse normal**; `gas_envelope.ell()` is the fix, and the bag's
  Z dimension dropping 13.90 → 12.25 m is what confirmed it.
- **Three clash tests in a row that lied.** AABB overlap calls a lamp hung under
  a bag's flank a clash, because an ovoid fills about half its own box.
  `closest_point_on_mesh` plus normal sign reported a catwalk at y = −9 inside a
  bag spanning y −40…−15 — the end collars are concave, so a point beyond the
  cap finds a face it is technically behind. Ray-crossing **parity** fails on
  `Banded`, whose sixteen straps are separate closed shells 20 mm off the skin;
  it got the bag's own centre wrong, which is how it was caught. What holds is
  the **analytic** profile, because the bags are ours — `assert_bags_clear()`
  fails the build, and it immediately found a stair's top corner inside bag 2 at
  a station that had been checked by hand and passed.
- **The walkways moved twice.** Between the houses at x = 7.8 the assertion
  caught them in a bag — the bags are fattest amidships and anything tall enough
  to be useful reaches them. Moved outboard to x = 13.4 they ran clean through
  the sails, a zone that only exists once the sails reach final size. They ended
  at x = 14.0 fore and aft of the sail zone, plus a set slung **under the keel**
  where nothing else on the ship goes at all.
- **89,000 triangles of ladder — 23% of the model.** Stamping the kit's
  `Handrail_Ladder` for ~90 vertical metres of climb. It is 1000 tris/m, which
  is right where hands are and absurd everywhere else. `climbs()` builds them at
  55 tris/m and the kit part now appears twice, at the two boarding stations.
  282k total, from 387k.
- **Seven crates inside people's houses.** Free scatter across the same outboard
  strip the dwellings stand on. Now rejection-sampled against the dwelling list.
- **A ladder scaled in Z to reach the gantry.** `k=(1,1,5.81)` spreads the rungs
  to 1.9 m apart. Stack copies, or build it — never scale a ladder.
- **Three materials that the palette could not reach.** `bpy.data.materials` is
  keyed on name *and* library, so a **local and a linked material can share a
  name with no suffix on either** — and `append` makes everything local. The
  model shipped a local `Mat_Metal_Steel_Worn` sitting beside the linked one,
  invisible to the usual `.001` fold, and `bpy.data.materials.get()` returned
  whichever it liked. Editing the palette would not have changed them. Two
  further wrinkles: linking the palette *before* appending does not help (the
  names still collide), and most of a kit part's materials are not in this
  model's own `MATS`, so there is no linked twin to remap onto — the fix links
  every local name the palette holds, then remaps. `dedupe_materials()` does
  all three steps; re-fetch `mats` afterwards, because its unused-purge will
  drop links nothing references yet and the stale list raises
  `ReferenceError: StructRNA of type Material has been removed`.

## To Unity

`sky_city_export.py` → `Assets/Game/Art/Models/Environment/Structures/sky_city.fbx`
→ [`SkyCityBuilder`](../../../../Editor/Environment/SkyCityBuilder.cs)
(`Tools > Environment > Build Sky City Prefab`) →
`Assets/Game/Prefabs/Environment/Structures/SkyCity.prefab`. Re-running the
builder after a re-export rebuilds the prefab in place.

**Measured in-engine 2026-09-15**, not taken from the source: root `lossyScale`
1.000, 109 renderers, 281,976 triangles, world size 30.06 × 52.74 × 125.04 m,
45 box + 3 convex colliders, zero negative-scale renderers. Ray tests confirm
the promenade is solid at y = 0 for its whole length on both sides, the clear
lane is unobstructed to 2.2 m at all 38 sampled points, and the cage closes to
about 2.7 m of headroom inboard — the gradient the layout was designed around.

Collision comes from two places, and the reason is the model's own shape: the
primary structure is nine **merged** meshes each spanning the whole ship, so a
per-renderer box over `Mesh_SkyCity_Decks` is a 22 × 112 m slab hanging in the
air. Five walkable volumes are authored by hand in the builder from this file's
constants; the kit parts, which are one renderer each, go through a name rule
table exactly as `BuildingPrefabBuilder` does. Splitting the structure objects
in `sky_city.py` would let the rule table do all of it — worth doing if those
meshes are ever revisited.

**It holds no runtime state**, so there is nothing to network and nothing to
persist: it is authored static content addressed by identity, like the other
structure prefabs. When `TerritoryZone` lands it plugs in via `owner` with no
change to the prefab; there is no `SkyFaction.asset` yet.

## Gotchas for anyone editing this

- **`assert_bags_clear()` must stay in step with `gas_envelope.stations()`.** It
  re-implements that profile analytically. If the component's `TIP` or `power`
  changes, change `ENV_TIP` / `ENV_POWER` here too, or the assertion silently
  starts checking the wrong shape.
- **The port sail is placed with a negative X scale.** `_buildlib.mirror_y`
  mirrors across y = 0, which is correct for the library's *other* convention
  (+X forward, as `ship_rv` uses) and would flip this ship end for end. A
  negative scale on a **rigid** prop survives the FBX — Unity reverses the
  culling mode on a negative-determinant renderer and cancels it exactly. It
  would **not** be safe if the sails were skinned; see
  [ArtPipeline](../../../../../docs/AI/systems/ArtPipeline.md).
- **The gas bag skin is flat-shaded on purpose.** The creases are the
  silhouette. Smoothing it turns the bag into a CG primitive.
- **`SKYWALKS` and `UNDERWALKS` are read by two functions** — `decks()` builds
  the brackets, `place_town()` stamps the catwalks. Add an entry and the
  supports follow; edit one list and not the other and a walkway hangs on air.

## Known gaps

- **One material is local, and it is inherited.** `Mat_Glass_Amber_Warm` on the
  deck lanterns is not in `palette.blend` at all:
  [`camp_lantern.py`](../../components/props/camp_lantern.py) **defines its three
  materials inline** (`MATERIALS = [(name, hex, roughness, metallic), …]`) rather
  than linking them, against the library's rule that nothing defines a local
  material. Two of the three exist in the palette and are now relinked on
  append; the amber glass has no palette entry to link to. Left as-is
  deliberately — adding it to the shared palette, or relinking `camp_lantern`,
  changes a component this model does not own and every other model that uses
  it. Worth fixing at the source, as its own change.

- **No armature, and the moving parts are not all separable.** The two sails are
  their own objects and could be rigged today. The **airscrews and the rudder
  are not** — they are merged into the single `Mesh_SkyCity_SternGear` mesh,
  along with the engine house and pylons. Driving them (with
  `StructureAmbientMotion`, which spins and sweeps named child transforms) means
  splitting `stern_gear()` into separate objects first. An earlier note in this
  file claimed all three were separate; they are not.
- **No UVs.** Flat palette colours only, like the rest of the blockout kit.
- The airscrew blades are modelled stopped, at one fixed angle.
- **No LOD meshes.** The prefab gets a single-level cull `LODGroup`, like the
  five building prefabs, because no decimated variants were authored.
- **Nothing is walkable outboard of x = 11.2.** The seven cantilevered timber
  platforms per side are set dressing over the void — see `SkyCityBuilder`.
