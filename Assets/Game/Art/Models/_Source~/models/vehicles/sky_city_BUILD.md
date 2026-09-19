# SkyCity — build record

The Sky Tribe's floating home. The faction design has called for it since
2026-09-07 — *"Sky Tribe… they live in **floating vehicles in the sky** (not
created yet — backlog, §8)"* — and nothing had been built.

Built 2026-09-15. This is a record of decisions, not a proposal.

> **Hand-edited by the user from 2026-09-16.** The `.blend` is now the only
> source of truth and `sky_city.py` no longer reproduces it. Never delete it to
> regenerate. Edit it in place; re-running `sky_city_export.py` and
> `SkyCityBuilder` is still safe, because neither writes to the `.blend` - but
> if the hand edits moved the decks, end structures or gantry, the authored
> collision volumes in the builder must be re-measured against them.

---

## What it is

A salvaged industrial lifting hull with a nomad town grown along its flanks.
Three gas bags hang in an open cage; a promenade runs the length of the keel on
both sides; dwellings, awnings and hanging walkways are bolted outboard of it;
two triangular sails drive it. The fiction the shapes carry is that **somebody
else built the lattice** — hazard yellow, riveted, rectilinear — and the tribe
moved into it. Painted steel below, cloth and timber above and outboard, and the
two languages never blend.

**Envelope** (revised 2026-09-16 — see "Second pass" below)

| | |
|---|---|
| overall | 62.1 × 132.7 × 35.9 m |
| hull without sails or rudder | 22.4 × 116.0 × 21.0 m |
| sail span, wingtip to wingtip | 57 m |
| promenade | 6.8 m wide each side, 104 m long, walking surface z = 0 |
| clear walking lane | x 4.2 → 7.0, both sides, unobstructed end to end |
| headroom under the cage | 10.0 m at the outboard rail, 2.7 m at the keel |
| triangles | 330,782 across 108 objects |

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

## Second pass — 2026-09-16

Six changes against the reference photograph, after the first pass was reviewed.

1. **The bags fill the cage.** They were 7.0/7.6/7.2 m inside an 8.4 m cage and
   read as loose in it. Scaled at placement (`BAG_K`) to 7.85/7.95/7.90. The
   ceiling is not the cage's centreline but the **inner face of its 0.34 m
   members at 8.23 m**, less the 0.16 m the bags' own ribs stand proud — over
   about 8.05 m a rib pushes through a stringer. Uniform scaling lengthens them
   too, which is what closes the gaps from 3.5 m to ~1.2 m; `BAG_Y` widened to
   ±30 because the scaled **end collars** would otherwise interpenetrate by
   1.56 m, and the collars reach ~1 m past the loft the radius is measured on.
2. **A bow castle and a stern block.** The reference has a commanding mass at
   each end and the model had a 7.7 m `control_cab` forward and a box aft. Both
   are new bespoke parts in the hull's plated language — `bow_castle` is three
   stepped storeys and a lattice derrick over the stem, `stern_block` is a
   plated block closed by a 16-facet domed cap with a mast cluster on it. The
   `control_cab` helm and `stern_gear`'s engine house are **gone**: a cab parked
   inside a 17 m block was what made the bow read thin, and a box inside a box
   is just a box.
3. **The sails spread outboard.** Turned from vertical onto ±X at a 22° rise,
   stepped on the cage's beam stringers at x = 9.1, z = 10 — **not** on the
   deck. A sail rooted at deck level and angled outboard passes straight through
   the dwellings, which reach 8.9 m at x = 9.6. `SAIL_K` drops 1.45 → 0.88, for
   a 22.9 m spar and a 57 m span. The component is authored luff-up, so the
   spar is laid over by rotating the **source object** about Y before stamping —
   `place_delta` has rz and rx but no ry.
4. **Twenty-four props that held nothing up.** The cantilever-platform rakes,
   the skywalk rakes and the sail stays all had their feet at x = ±11.2 below
   the deck, where the lowest real structure is the gallery bracket at z =
   −0.34. They hung in open air. All now land on the keel girder's lower
   longeron, and `assert_feet_supported` fails the build if a registered foot
   is outside the declared solid volumes.
5. **More metal.** Steel X-bracing with riveted gusset plates across every
   second bay of both keel flanks (the single biggest change to how the ship
   reads); the promenade lane is steel plate rather than ply, on two
   longitudinal stringers, with a riveted strake up the girder shoulder; every
   third cage ring is bare steel with gussets where the stringers cross; the
   bag saddles, skywalk outriggers, airscrew blades and rudder frame are steel;
   the stern dome is plated steel with meridian straps. The timber stays where
   the tribe built — shanty decking, cantilever platforms, rails, saddle
   packing.
6. **Two knock-on moves the assertions caught.** The gantry ladders were on the
   centreline at deck level; with the bags at full size there is no window
   between them, so both climbs now start on the end structures' roofs
   (`LADDER_Z`) — better architecture anyway, six metres off a roof you already
   walked up to. And the flags stood on the cage crown, which **is** the bag
   now: `assert_bags_clear` found 182 vertices of flagstaff inside one. They
   moved to the crown gantry.

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
- **A beam is perfectly happy to start in mid-air.** Twenty-four raking props
  were drawn from `(SIDE_OUT, −2.4…−3.4)` to the thing they were supposed to
  carry, and nothing at x = ±11.2 goes below z = −0.34. Nothing errors, nothing
  clashes, and `_zverify` has no opinion — an unsupported member is not a
  z-fight. It is only visible from below, which is not an angle a generator
  author looks at. `foot()` and `assert_feet_supported()` exist because the
  user found this, not the toolchain.
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
`Assets/Game/Prefabs/Environment/Structures/SkyCity.prefab` and its hull meshes in
`SkyCity_CollisionHulls.asset`. Re-running the builder after a re-export rebuilds
both in place; [`SkyCityPrefabTests`](../../../../Editor/Tests/SkyCityPrefabTests.cs)
checks the result.

**What the export ships and drops.** It splits `COL_SkyCity` into one object per
convex island (`COL_SkyCity_####`), turns each `LAD_SkyCity_##` marker's custom
properties into `_Top` and `_Exit` child empties (FBX custom properties do not
reach Unity without a postprocessor), and drops the hidden stamp sources, the
emptied gangway and everything more than 50 m off the centreline - the escort
vessels, which ship on their own (below).

**Collision - three sources, all checked in Blender first.**

| Source | Colliders | Why |
|---|---|---|
| `COL_SkyCity_####` islands | an axis-aligned 8-corner island → BoxCollider; anything else → convex MeshCollider on a saved hull | every floor, stair ramp, railing, wall, house, tower and footprint `sky_city_traversal` authors |
| Renderer rules | gas bags → convex hull; cage, cradles, old deck, stern gear → non-convex mesh; crate stacks and stern handrails → box | their own shape is the right collision |
| Nothing | cloth, rope, washing, lamps, flags, outriggers, and everything the islands cover | listed as rules, so a new name is reported, not silently skipped |

`sky_city_traversal.MESH_COLLIDED` and `BOX_COLLIDED` mirror the renderer rules,
so the Blender route check walks exactly what Unity collides with. The first time
they were mirrored, the lanes failed: the cage's ring frames curve down over the
lanes' inner edge to head height, and houses collided as world-aligned boxes
reached 0.8 m into the lanes' outer edge. Houses now collide as boxes turned with
them, and the lanes' walking lines are found, like the walkways'. The outriggers
stay uncollided because their rigging runs through the castle's rooms.

**Scale (2026-09-17).** The route check above walks a 2.0 m capsule under 2.1 m of
headroom, but the game's player is **3.0 m tall** (a 2 m capsule on a transform
stretched 1.5 in Y). At 1 unit = 1 m every ceiling was lower than the player, which is
why the lanes and doors were too tight in play. The prefab root ships at
`SkyCityBuilder.Scale` = **1.5**: 2.1 x 1.5 = 3.15 m of headroom. The model and the
Blender check stay at 1.0. Costs: every lip between surfaces grows 1.5x (the player has
no step-up), and each ladder-top gap becomes 1.05 m, wider than the 1.0 m-wide player -
so ladders can be taken hold of from the top ([Ladders.md](../../../../../../../docs/AI/systems/Ladders.md)).
First shipped at 1.3 on the 2 m assumption; corrected the same day. The fleet's escort
stations are written in the city's modelled metres and held at `Position x Scale`, so
they move out with it.

**Ladders in play.** Each `LAD_SkyCity_##` carries a `Ladder`. On a 3 m body the head
meets the top floor before the feet reach the step-off on ladders 03, 04 and 07, so the
climber steps over the lip when blocked within a body height of the top;
`SkyCityPrefabTests.ThePlayerCanClimbEveryLadderAndStepOffAtTheTop` checks every column
below that with the real capsule and every step-off (ladder 05's exit is 0.10 m inside
Bag1's hull, cleared by depenetration).

**Measured in-engine 2026-09-16** (numbers below are modelled metres; multiply by
Scale for world size): root scale 1, 310 renderers, 1,216,518
triangles; 645 island colliders (449 box, 196 convex hull, every hull saved) plus
13 box, 3 convex and 4 mesh from the rules; no renderer left unmatched. A Blender
point `(x, y, z)` arrives at Unity `(-x, z, -y)` - ladder 01's foot, Blender
`(-2.3, -15.25, 0)`, is Unity `(2.30, 0, 15.25)`. Every ladder's exit has floor
under it at exactly its step-off height (19.68 at both shafts, 5.42 at the castle
terrace, 2.54-4.67 on the roofs), and a 0.45 m capsule clears both keel crossings
end to end.

## The fleet - 2026-09-17

The user built three smaller ships beside the city in this file, out past
x = 50: a three-bag caged **freighter** with a gondola, a single-bag **skiff**
with sails and a lantern tower, and a single-bag **tug** with twin engine pods.
The city is the flagship; the others are their own ships that keep company with
it.

**Scale.** They were assembled from the city's parts at the city's scale, so the
freighter was half the flagship's length. `sky_fleet_export.py` shrinks each by
`VESSEL_SCALE` = 0.5 about its own centre, in the export only - the `.blend` keeps
the user's full-size originals. Crew-scale parts shrink too (rails, doors), so
these are ships to see and to land on, not yet ships to walk.

**Export.** A vessel is *found*, not listed: every mesh beyond 50 m of the
centreline is grouped with whatever its bounds touch (within 3 m), and each ship
is picked out by one object only it contains. A part added to a ship ships with
it. The freighter's hull block (`Cube`) had no material and is painted palette
white, as it looks in Blender. Output: `Environment/Structures/SkyFleet/`
`sky_freighter.fbx` (12 parts), `sky_skiff.fbx` (10), `sky_tug.fbx` (11).

**Unity.** [`SkyFleetBuilder`](../../../../Editor/Environment/SkyFleetBuilder.cs)
(`Tools > Environment > Build Sky Fleet Prefabs`, after the city) makes one prefab
per ship in `Prefabs/Environment/Structures/SkyFleet/` and
`SkyCityFleet.prefab`, which nests `SkyCity.prefab` - not a copy - with the three
round it. Collision is by renderer rule through the same
`StaticPropBuilder.ApplyFits` the city uses: bags, cabin, prow and lantern tower
convex; cage, engine ducts and gondola deck mesh; the freighter's hull block a
box; sails, flags and outriggers nothing.

| Ship | Size (m) | Colliders | Held at (Unity, from the city's origin) |
|---|---|---|---|
| SkyFreighter | 36.2 x 15.2 x 31.5 | 1 box, 5 convex, 4 mesh | (78, 26, -8) x Scale, yaw 8 - high, starboard |
| SkySkiff | 31.0 x 13.3 x 21.3 | 3 convex, 2 mesh | (-80, 8, 38) x Scale, yaw -12 - forward, port |
| SkyTug | 31.0 x 21.2 x 23.9 | 3 convex, 3 mesh | (-78, -14, -40) x Scale, yaw 20 - low astern, port |

The city's *drawn* reach is x -49..46 (sails and outriggers), not its deck's
+/-20, and the first placement, cleared against the deck, overlapped all three.
The builder warns on overlap and
[`SkyFleetPrefabTests`](../../../../Editor/Tests/SkyFleetPrefabTests.cs) fails on
it. Static geometry: no NetworkObject, no saver.

**Triangle count is the thing to watch.** 1.2 million, four times the first
city: the walkways (200 k) and the castle interior (168 k) are mostly rope
lashings and railings, then street life (88 k) and lane railings (72 k). The
prefab has one cull LODGroup and no decimated meshes; if frame time suffers,
lighter lashings in `scrap_walkway` are the first cut.

**It holds no runtime state**, so there is nothing to network and nothing to
persist: it is authored static content, like the other structure prefabs. The
ladders are data only until the game can climb.

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
- **Unreachable on purpose:** the underwalks (no stairs, cluttered under-deck),
  the prow lookout (raised by the user to z ≈ 3.1 with the pod over it), and
  the escort ships at x ≈ 87, which `sky_city_export.py` would still export -
  as it would the hidden stamp sources in `Coll_SkyCity_TraversalSources`.

## Traversal pass — 2026-09-16

The `.blend` has been hand-edited since this date, so `sky_city.py` is no longer
run as a generator. [`sky_city_traversal.py`](sky_city_traversal.py) makes it
walkable instead, run inside live Blender, together with
[`sky_city_street.py`](sky_city_street.py). It is safe to re-run: it owns what
it builds, rebuilds every mesh it changed from a `<object>__pre_traversal`
backup, and refuses to touch anything hand-edited since its last run.
`run(discard_edits=True)` rebuilds the pieces it made even if they were edited -
only when the user has said to; edits to the user's own objects always stop it.

### The first pass was rejected, and why

It made everything reachable and looked "way too industrious and modern": one
continuous steel slab down each side, square steel stairs, a steel stair tower
up the castle's flank, square loops round the gantry's towers, a lamp on every
fifth post. The user liked the first sky city's jankiness - things feeling
alive and put together - and wanted it rusty and worn. The second pass keeps
every route and redraws everything in the tribe's own kit.

### What the second pass builds

| Where | What | From |
|---|---|---|
| Both sides, castle to stern piers | 20-30 small platforms a side: planks, rusted plate, grating or scrap; each its own width, railing and props; planks thrown over the gaps | [`scrap_walkway`](../../components/structural/scrap_walkway.py) |
| Loading bays, on jetties in the gaps between houses | four cranes, each a different build, with goods on the hook | [`cargo_crane`](../../components/mechanical/cargo_crane.py) |
| On the jetties, and stock in the castle | sacks, bales, chests, rugs, cans, a trade scale | [`trade_goods`](../../components/props/trade_goods.py) |
| Inside the castle | a starboard stair to the first floor; a switchback core through the first and second floors to the roof; patchwork floors; lanterns; stock | `scrap_walkway`, `trade_goods` |
| Castle roof | a timber stair to a landing wrapped round the gantry's forward end | `scrap_walkway` |
| Crown gantry | the lantern and dome galleries widened and opened; a new gallery round the dish; two plank balconies; patches over the deck; the gantry repainted with rust | surgery, `scrap_walkway` |
| Skywalks, stern | scrap stairs to all four skywalks, rusted pipe stern supports, a ramp to the stern terrace | `scrap_walkway` |

With the street pass below, 51 routes and ladder checks pass `verify()`, and
the negative controls - through the sail slot, off an edge, into a house,
through a crane, through the castle's front wall, into a stairwell, off a
landing, through a tower, over a gallery rail, off a balcony, into the keel
truss, through a street wall, off a roof terrace, through the porch rail - all
fail as they must.

**Platforms are small** because the user cut them down by hand and asked for
exactly that: each reaches 2-3 m past the old deck edge, and only as far as it
must - past the furthest-out house by `HOUSE_CLEARANCE`, under a skywalk and its
stair, and out to a jetty where a crane stands. The walkways' walking lines are
not written by hand any more: `walkway_routes` walks down each side at 0.25 m
steps, takes the standable point nearest the outer rail, and joins it only by a
leg that passes the same `check_segment` the verifier uses. Where no leg passes,
the line breaks; the run prints how much of each side the stretches cover (about
90 of 99 m: the breaks are the sail slots, the crane jetties' footprints and a
house to port).

### Why it is laid out the way it is

- **Stairs carry every route; ladders are extra.** `Movement` has no climb and
  no step-up, and landing from more than about 1.3 m hurts, so every route a
  player needs today is a stair on a smooth ramp at 27.2°. The ladders the street
  pass adds are for climbing logic that does not exist yet - see below.
- **The castle core runs across the castle, in its aft third.** The pod bolted
  to the castle's front fills the upper floors' forward centre and the second
  floor is only 7 m deep, so a switchback along the length does not fit. Across
  the width it does: the upper flight of one storey arrives beside the foot of
  the lower flight of the next, so the climb is one zig-zag. A plank spine
  between the flights runs floor to ceiling.
- **The galleries were too narrow to walk.** The lantern's was 0.87 m and the
  dome's 0.95 m, both under the 1.0 m capsule. Their railings are pushed out to
  4.15 m and 5.4 m radius and carved open where the gantry runs in. A fitting on
  the dome's gallery floor was moved against the tower wall - anywhere else on
  the ring it blocks the lap.
- **The walkway has a slot where each hanging bow sail passes through it.** To
  starboard you walk round it inboard; to port a house fills that strip, so the
  forward part is reached through the castle.

### Changed on the user's objects (all restorable from the backups)

`BowCastle` (hollow), `Cage`, `Decks`, `Climbs`, `Gantry` (openings, the port
half of one cross-frame, rust by island), the gantry's lantern and dome
(galleries), and `Gangway_Catwalk_Bridge` - **emptied**: moving the castle left
it lying across the second floor at floor height, wholly inside the castle's
footprint, joining nothing, and with the storey hollow its railings crossed the
room and the stairwell. Two props were moved out of the castle's walls.

### Collision

`COL_SkyCity` in `Coll_SkyCity_Collision` holds every collider as a convex
island: 8-vertex axis-aligned boxes (for BoxColliders) and ramps, walls, gallery
sectors and n-gon prisms (for convex MeshColliders). Bounds boxes cannot be
used: catwalk bounds reach the rail tops, the houses' shared mesh has struts to
the keel, and a leaning, sagging railing has no useful bounds at all.

### Lessons

- **A ray-only route check passes routes through walls.** Add a point-in-solid
  test, and prove the checker with routes that must fail before trusting a pass.
- **Hash after `view_layer.update()`.** `matrix_world` is stale until then, and
  the pass flagged its own objects as hand-edited.
- **`BVHTree.FromObject` works in local space.** Clash tests between objects
  need a BVH built from world-space vertices.
- **`Part._absorb` returned the wrong faces** whenever the mesh already held
  geometry - see the ArtPipeline gotcha. Found because a sack's body transform
  flung earlier sacks four metres out of the cargo net.
- **A route crossing a thin railing head-on passed.** A 0.1 m wall falls between
  0.25 m samples, and the side rays look sideways. `check_segment` now also casts
  ahead to the next sample. Found by a negative control walking off the porch.
- **Probe points on the capsule's rim step over a railing.** Clearance is tested
  with rays, not points - the walkway line finder picked spots 0.3 m from a
  skywalk stair's rail until it did.

## Street pass - 2026-09-16

The user's next request: paths between the two lanes, "more life ... almost like
a favela street", fix the buildings with messed-up geometry, and ladders "where
you might want to go ... ladder logic later". [`sky_city_street.py`](sky_city_street.py),
called from the traversal run:

| What | How |
|---|---|
| **Crossings** | The lanes run either side of the keel's open truss, and nothing bridged it (the collision had an invisible floor there). Where two bags meet (y -14.6 and 16.45) there is 7.6 m of headroom: a plank bridge each, railed. The lanes' inner edges are railed everywhere else, and the castle's aft door opens onto a porch. |
| **Ladders** | A rusted ladder up a clear shaft beside each join to a landing on the crown gantry (the shafts were found by search: columns clear 0.55 m round from deck to gantry); timber ladders up the street walls onto the four box shacks' roof terraces; one up the castle's aft wall onto its first terrace. |
| **Houses** | See below. |
| **Street life** | Street walls of patched pastel and rusted plate, with doors, lit windows and flower boxes; washing, string lights and cables strung between walls, along the lane poles and over the sidewalks; canopies, blade signs, stalls, stoves, stools, plants and birdcages; rooftop terraces with washing, plants and dishes. From [`street_life`](../../components/props/street_life.py). |

### The houses

The shanties come from `shanty_addon`, which models every one of them to hang on
a host wall: its mounting face is x = 0 and it projects into +X. The city stood
them on the open deck with that face to the lane, so the lane saw bare grey weld
pads, a lean-to roof leaning on nothing and brackets reaching for a hull that was
not there. On top of that:

- `Shanty_Box` and `Shanty_Awning` meshes in the city had been **stretched** - a
  bracket reaching 7 m inboard and 4.8 m under the deck, a guy line 13 m long.
  Both are restored from the library's intact meshes.
- **The lean-to's roof was built tilted the wrong way.** `build_leanto` rotated
  the sheets by `-ang` about Y, lifting their outer ends, so the roof climbed
  away from the wall and cut through both gables. The city's copies are turned
  back in place (and the generator record now reads `+ang`); the library's
  `shanty_addon.blend` still has the old tilt.
- Seven shanties stand 0.45-1.35 m above the deck. With the stretched brackets
  gone they get **stilts** - posts and a cross-brace.
- Two stood a metre inside a neighbour and were moved along the ship
  (`HOUSE_MOVES`).
- Every shanty gets a **host wall** - its street face. The water tank up on the
  stern terrace is left as it was.

A box shack whose roof is a terrace records the roof's height as
`sky_city_walkable_roof` on the house object; its collision box stops there, so
the roof is floor rather than the inside of a solid.

### Ladders, for the climbing logic to come

Every ladder is drawn plumb (`scrap_walkway.ladder`), collides only as its two
rails, and is recorded as an empty in `Coll_SkyCity_Ladders`:

| `LAD_SkyCity_##` | |
|---|---|
| location | the ladder's foot, centre of the rail line |
| local +Y | toward the climber |
| `climb_top_z` | the height a climber steps off at |
| `exit` | the world point they step off onto |
| `ladder_name` | e.g. `Shaft01`, `Roof_Home02_Shanty_Box`, `CastleTerrace` |

Export them with FBX custom properties on. Every ladder top is a gap of
`LADDER_W` (0.7 m) in a railing or behind the ladder's own grab rails - narrower
than the 1.0 m capsule, so until climbing exists a ladder is not a hole to fall
through. `ladder_checks` asserts each ladder's climb column is clear and its
exit is floor.

