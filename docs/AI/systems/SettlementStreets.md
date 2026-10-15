---
system: SettlementStreets
layer: world
summary: "Planned settlement: L-system streets, buildings lining them, street terraces, walls between buildings, stairs"
paths:
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Config/SettlementStreetStyle.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStreetLayout.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStreetNetwork.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStreetPlots.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementTerraceField.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStreetPaver.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementPolyline.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementPlaza.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementEntrance.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementLayoutResult.cs
  - Assets/Game/Prefabs/Environment/Structures/NomadSettlement/Streets
  - Assets/Game/ScriptableObjects/Settlements/NomadStreets.asset
symptoms:
  - "settlement buildings are scattered instead of lining streets"
  - "a settlement is laid out along one straight street"
  - "the plaza in the middle of a settlement is a huge empty field"
  - "a settlement street ends at a terrace wall instead of stairs"
  - "Settlement warns that a street runs into another on a different terrace"
  - "terrace walls float as raised boxes with no drop in front of them"
  - "a wall block's end shows a dark face where the terrace wall bends"
  - "sand ramps up the face of a terrace wall"
  - "terrace walls appear outside the settlement on untouched ground"
  - "terrace walls stand several blocks thick or in rows, or hang short of the ground below them"
  - "terrace walls line free ground with no building on one side"
  - "a flight of stairs stops short of the ground at its foot or sinks into it"
  - "settlement buildings stand on a raised platform of flat terrain instead of sitting in the hillside"
reads_with: [SettlementTerraceKit, TerrainGeneration, ArtPipeline, NavMeshSystem]
updated: 2026-10-03
---

# Settlement streets and terraces

The planned layout of [`Settlement.Generate`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/Settlement.cs)
([TerrainGeneration.md](TerrainGeneration.md)). Give a `SettlementConfig` a `streets` style and, instead of growing a
cluster, the settlement grows a street network, lines it with its buildings, levels the ground under them, holds it up with
retaining walls where two buildings stand on different levels, and lays the streets and stairs. Empty `streets` = the cluster layout, unchanged.

**Scope:** the files under `paths:`; [`SettlementStreetLayout.Generate`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStreetLayout.cs)
runs it end to end. This doc covers the layout and the concrete **street kit** (`street_kit.blend` → 8 prefabs in
`Prefabs/Environment/Structures/NomadSettlement/Streets/`, [ArtPipeline.md](ArtPipeline.md); style
[NomadStreets.asset](Assets/Game/ScriptableObjects/Settlements/NomadStreets.asset)). The clay **terrace kit** -- tiled roads,
stone paths, multi-level stair chains, stacked walls -- is [SettlementTerraceKit.md](SettlementTerraceKit.md); it switches on
per feature when a style holds its pieces.

## Model

- **Streets grow like Parish & Müller's extended L-system**: a street module rewrites into one segment plus itself, and
  now and then a branch one order down (main street → side street → alley) at ~90°. Local constraints stop a segment at
  the first street it crosses or snaps it onto one within `snapDistance` (T junctions), and no segment runs through a
  placed building. Proposals are taken by due time; branches fall due later, so the main street grows first.
- **Buildings line the streets, largest first**: each street side is walked with a cursor from the centre outward; at
  each stop the largest waiting building that fits (clear of every street corridor, `minBuildingGap` from other plots) is
  placed with its local +Z (the nomad buildings' main door) facing the street, `setback` from its edge. Streets and plots
  grow in rounds until every building has a frontage or growth stops; then every street is **cut back to its last house**
  (+ `trimMargin`, but as far as a kept branch leaves it) and streets nobody lives on are dropped -- growth overshoots.
- **Every planned settlement has a plaza, its town centre** (user, 2026-10-02: one main street read as "a straight line",
  and a town should have a centre whatever its size): [`SettlementPlaza`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementPlaza.cs)
  rings an open, level plaza with the largest buildings that fit within `plazaMaxRadius` (at least three), facing the centre
  and spread evenly between 2-4 lanes (`plazaHousesPerLane`); the lanes are main streets grown out of the centre through gaps
  left in the ring, held at the plaza's level across it, and the other buildings line them from the plaza's rim outward. Each
  ring building belongs to its nearest lane (level, trimming) and its door path runs to the centre.
- **Dense core, loose rim** (user, 2026-10-02): the town's radius is estimated from the buildings' area and
  `buildingCoverage`; inside `denseCore` of it houses stand `minBuildingGap` apart, outside the gap loosens toward
  `maxBuildingGap`. In the core a gap is now and then (`backRowChance`) left as a `passageWidth` passage, and a **back-row**
  house is built behind it, facing the street with its door (`SettlementEntrance`) on the passage's axis, so its door path
  runs between the two houses in front.
- **Terraces serve the streets, not the landscape**: a grid of 1 m cells, each on a whole level (`baseY + k × stepHeight`).
  Only streets (their own profile, read off the natural ground **smoothed over `groundSmoothing`**), stair corridors (terrace
  kit) and plots (+1 m padding) are locked. A street is levelled to its level; a plot to **the highest natural ground under
  its footprint** (its street's level only decides where walls and stairs go). Free ground is never terraced: it keeps its
  natural shape plus a correction toward the nearest locked level, full within `wallReach`, fading over `gradeDistance`.
- **A wall stands only where two buildings on different levels are within `wallReach` of the step** -- a plot on each side.
  Nothing else gets one: a street's level change is stairs, a bank around the settlement is a grade.
- **Stairs go where a street changes level**, on the lower side against the upper one. A profile is flat runs
  ≥ `minRunBetweenStairs` long; with the street kit neighbouring runs are one level apart, one flight each.
- **Own seed**: `seed ^ StreetSeedSalt`; only the buildings' rolled copies come from the main sequence. Every paver draw
  comes from it in street order, so a settlement regenerates identically.

## Key types

| Type | Role |
| --- | --- |
| `SettlementStreetStyle` | ScriptableObject on `SettlementConfig.streets`: growth rules, slabs across per street order, `verge`, `setback`, terrace knobs (`stepHeight`, `minRunBetweenStairs`, `groundSmoothing`, `gradeDistance`, `wallReach`, `wallStraightening`, `fieldCellSize`), street-kit pieces (`slabs`, `narrowStairs`, `wideStairs`), `terraceWalls` (either kit's wall block / top piece), then the terrace-kit block |
| `SettlementStreetLayout` | Static `Generate`: grow + plot, terrace field, sculpt, spawn buildings, lay. Returns a `SettlementLayoutResult` (the cluster layout returns one too) with `streetsEndingAtWalls`, `wallsTooTall` and `lanes` (the streets leaving the centre, on the sculpted ground, each point paved unless a stone path: the muster spot goes where one's paving ends, [Residents.md](Residents.md)); the summary counts streets by order and every piece. Pieces go under `Generated/` + `StreetsRootName`. `SettlementStreetStyle.SurfacePieces()` = every prefab walked along (not the walls), `PavedPieces()` = those minus stepping stones |
| `SettlementStreetNetwork` | The growth. `Street` = polyline + `order`, `halfWidth`, `parent`/`parentArc`, `endsOn`/`endsOnArc`; `Grow(targetFrontage, obstacles)`, `Corridors()`; static `KeepLengths` + `Trim` (renumbers streets) |
| `SettlementStreetPlots` | Lines streets with buildings; keeps a cursor per street side across rounds; `TownRadius`, `Looseness`, passages and back rows, `LastHouseArcs`, `Renumber` |
| `SettlementTerraceField` | Level grid: `Profile` (+ static `ClampSteps`), `StreetProfile.Steps()`, `Lock(street, profile, margin)`, `LockRamp`, `Lock(footprint)`, `Grade` (nearest-level assignment, correction, wall-step hold), `WallBetween`, `Contours` (marching squares per level), `TerrainHeight` for the sculptor |
| `SettlementStreetPaver` | Places pieces, either kit: `LayStairs`/`LayStairChains`, `LayWalls`, `LayStreets`/`LayTerraceStreets`, `LayDoorStub`; static `SlabSize`/`PavedWidth`/`TileSize`/`PavedHalfWidth`/`WallDepth`/`SlabsAcross` measure the style's prefabs |
| `SettlementPolyline` | `Length`, `PointAt`, `TangentAt`, `Project`, `NextChord`, `Simplify` (RDP), `Chaikin`/`Smooth` |
| `SettlementEntrance` | Door marker on a building prefab (ground, +Z outward, main door first). `MainDoorway` = first marker, else the middle of the footprint side the building faces |

## Flows

1. **Grow + plot** — buildings sorted by footprint area; the plaza is planned first (ring plots fixed, the
   network's axioms = the lanes, lanes lined from `plazaRadius`, the plaza disk locked at the centre's level); each round grows the network until frontage = current + Σ(width +
   `maxBuildingGap`) × `frontageSlack`, then `Place` lines streets in creation order, both sides, and builds a back row behind
   each passage left. A street's half-width is `PavedHalfWidth(order) + verge` (terrace kit: the widest surface the order can
   get). Then `Trim(KeepLengths(...))`, every house is lined up **again** on what is left (kept only if it places at least as
   many), and the network is trimmed once more; the plots follow each renumbering.
2. **Terraces** — field over the extent (farthest street point or plot + `outskirts`). Profiles in creation order: a
   branch starts at its parent's level at `parentArc`, the main street's second half at the first half's start; a street
   that ran into an older one ends at that one's level where the profile allows (else counted in `streetsEndingAtWalls`);
   `ClampSteps` caps each change (1 level for the street kit). Then chain ramps (terrace kit), then every street locked to
   its profile, then plots (+1 m), then `Grade`: a multi-source Dijkstra gives every free cell its nearest locked
   cell (level, distance, whether a plot); the correction `level height − natural height there` fades out over
   `gradeDistance` past `wallReach` and is smoothed by Jacobi passes where regions of different levels meet; at each
   **wall step** (`WallBetween`: upper plot-owned and lower plot-owned, both within `wallReach`) the upper cells within
   `WallDepth/2` are pinned at the lower level.
3. **Sculpt** — `SettlementTerrainSculptor.ShapeTerraces`: inside the extent a locked cell gets its own height, a
   held cell its lower level, any other `natural + correction`; past the extent the correction fades out over `blendDistance`.
4. **Buildings** spawned at their plot's ground height -- the highest natural terrain under the footprint, sampled every
   `fieldCellSize` -- so no platform shows. The prefab origin is the build's floor line (5 cm under the floor; set per
   prefab, see [ArtPipeline.md](ArtPipeline.md) Gotchas), so its deep foundation is buried too. The door can end up up to
   ~0.75 m off its street's level, since the street keeps its quantised level.
5. **Laying (street kit)** — stairs at every profile change (top `StairTuck` 0.5 m into the upper run, +Z downhill, wide on
   the main street, **scaled in length and rise until the foot meets the measured ground**, 0.5×–2×); walls every block
   length + 2 cm along each contour after Ramer-Douglas-Peucker (`wallStraightening`) + Chaikin, only where `WallBetween`
   holds, **one wall per step**: where levels differ by more than one every contour between them runs through the same
   point, so only the top contour builds, topped at the upper plot's ground height and **scaled in Y to the drop measured
   1 m in front of the face**; facing the lower side, skipped past `extent − WallDepth` and where they'd reach > 0.5 m into a
   plot; slab rows along every street (`slabGap` apart, `slabSideGap` side by side), dropped where they overlap stairs, walls
   or slabs; a door stub from each main door toward its street when the door faces it. Terrace kit: [SettlementTerraceKit.md](SettlementTerraceKit.md).

## Multiplayer

N/A — edit-time scenery baked into the chunk scene; every machine loads the same pieces. No `NetworkObject` anywhere.

## Persistence

N/A — no runtime state. Pieces are scene content; the terrain edit is backed up on the component like the cluster's.

## Gotchas

- **The street network is not kept after Generate**: only pieces remain; `SettlementMuster.TraceStreets` re-traces them (links within 6 m: the node is ~3.5 m from its tiles).
- **The heightmap cannot make a vertical step, so the wall block hides it.** Chunk terrain samples are ~1.95 m apart;
  the rise between two levels spans one sample gap, up to ~2.8 m on a diagonal. The block is a wall face plus a 3-slab
  deck (3.76 m deep; the terrace kit's top piece 3.75 m) with a solid fill under it, and the sculptor keeps the ground one
  level down for half that depth behind the contour, so the rise lands inside the block. Street-kit walls and flights are
  stretched to the ground actually sampled, so a ramp that still shows is a few cm of drift. **A shallower wall prefab
  reopens the gap**; `WallDepth` is measured from `terraceWalls`, so keep the deck.
- **Terracing free ground made every wall a landscaping wall.** The first version levelled the whole field into
  terraces `minTerraceWidth` apart: rows of walls in the outskirts, adjacent contours reading as walls several blocks
  thick, none of it serving a street. Free ground is now graded, not terraced; do not reintroduce a level for it.
- **A step between a street and a plot, or two streets, gets no wall** (a building is required on each side), so the
  ground there is only smoothed -- a steep bank where locked cells of different levels touch with no free cell between.
  `Generate` already warns when a street ends against another on a different terrace.
- **Smooth the ground before reading a street's levels off it, not the walls after.** Quantizing raw terrain gave
  wiggly runs and a stair at every bump. `groundSmoothing` (6 m) averages it first; RDP + Chaikin straighten the wall line.
- **Trimming alone left the houses ~50 m apart.** While growing, a side street branches every `branchSpacing` and its
  corridor keeps houses off the street it leaves; most of those branches end up with nobody on them and are trimmed, but
  their gaps stayed. Hence the second lining pass on the trimmed network (on Chunk_6_3's copy: extent 173 → 124 m).
- **A plaza sized for the largest buildings is a field.** Ringing the six largest nomad buildings (footprints ~25 m, sails
  included) needed a ~30 m radius and the plaza read as empty sand; the ring now takes only what fits in `plazaMaxRadius`.
- **The field is a square; walls outside the extent circle stand on untouched ground.** They are skipped beyond
  `extent − WallDepth`.
- **A wall block's end is visible wherever the wall bends**, so the fill must be the face material (a dark fill read as
  maroon slabs in every corner).
- **A plot's ground height is not its level.** `Lock(footprint, padding, level, height)` stores both: `level` (whole
  steps off `baseY`) drives walls, stairs and `WallBetween`; `height` is what the sculptor writes and the building stands
  on. Anything that wants a plot's real ground reads `TerraceHeightAt`, not `HeightOf(level)`.
- **Street-kit stairs are single flights of `stepHeight`**. Where a street runs into an older street on a different terrace
  and cannot step down in `minRunBetweenStairs`, it ends at a wall and `Generate` warns. `stepHeight` must match the street
  kit's 1.5 m rise -- nothing checks the street kit (the terrace kit is test-guarded).
- **Stairs climb by a ramp collider** (the player has no step offset), 34° from the landing edge to the foot. NavMesh
  paths cross every sampled flight (3/3 on Chunk_6_3's copy); not yet walked by the player in Play mode.
- **Plots face the street by local +Z**: a building prefab whose door is not on +Z faces the street with a blank wall.
  The nomad buildings' `Entrance_M` (main) and `Entrance_A1..A3` markers were computed from `nomad_settlement.blend`'s
  `*_Door_Door_Frame` → `*_Step_TowerTrim_DoorStep` (0.5 m past the step, ×4 about the export pivot, prefab-local =
  (−x, 0, −y)); every main door is on +Z. `NomadBuilding_B18`'s only door is 4.8 m up and has no marker, like the six
  yards. **Re-exporting a build moves its door but not its marker** — recompute it.

## Extending

**Planned layout for another settlement** — create a `SettlementStreetStyle` (`Create > Settlement > Street Style`),
fill `slabs` (origin ground centre, long side X), `narrowStairs`/`wideStairs` (origin top of the flight at the wall,
descending +Z, rise = `stepHeight`) and `terraceWalls` (origin top of the face, +Z downhill, deck toward −Z), and assign
it to the config's `streets` -- or fill the terrace-kit block ([SettlementTerraceKit.md](SettlementTerraceKit.md)).
**New growth rule** — add it to `SettlementStreetNetwork.Expand`, drawing from the rng after the existing draws.
