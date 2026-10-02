---
system: SettlementTerraceKit
layer: world
summary: "Terrace kit for planned settlements: tiled roads and stone paths, multi-level stair chains, stacked walls"
paths:
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStairChain.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementWallStack.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementRoadTiles.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStonePath.cs
  - Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStreetSurfaces.cs
  - Assets/Game/Prefabs/Environment/Structures/NomadSettlement/Terrace
  - Assets/Game/ScriptableObjects/Settlements/NomadTerraceStreets.asset
  - Assets/Game/Editor/Tests/TerraceKitLayoutTests.cs
  - Assets/Game/Editor/Tests/TerraceKitAssetTests.cs
symptoms:
  - "the sand rises over the kerbs of a terrace-kit road, so the road edge looks ragged from above"
  - "a terrace-kit road stops a metre or two short of its round junction node"
  - "a stone path is mostly the smallest stepping stone, with long bare stretches"
  - "Settlement warns that terrace walls need more than maxWallCourses courses"
  - "a road has a bare gap of sand between a stair foot or a node and the next tile"
  - "a chain of stairs floats above the hillside or sinks into it"
  - "settlement NPCs walk around a narrow stair on a one-slab street instead of climbing it"
  - "a stone path climbs the hill by stairs"
  - "the whole town is paved in flagstone road"
reads_with: [SettlementStreets, ArtPipeline, TerrainGeneration]
updated: 2026-10-02
---

# Settlement terrace kit

How a planned settlement ([SettlementStreets.md](SettlementStreets.md)) is built from `nomad_terrace_kit.blend`'s pieces
([ArtPipeline.md](ArtPipeline.md)) instead of the concrete street kit. Nothing is ever stretched: stairs **chain**, walls
**stack**, roads are **tiled** between nodes, and a stone path is **scattered**. Style:
[NomadTerraceStreets.asset](Assets/Game/ScriptableObjects/Settlements/NomadTerraceStreets.asset), the
[`SettlementStreetStyle`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Config/SettlementStreetStyle.cs) "Terrace kit"
block; `NomadSettlement.asset` uses it (the user switched it 2026-10-02).

## Model

- **Each feature switches on with its pieces**: road tiles or stones → terrace streets (`IsTileMode`), a terrace flight →
  chains (`ChainsStairs`), wall courses → stacked walls (`StacksWalls`). A style without them is the street kit, unchanged.
- **Three surfaces, ranked by importance** ([`SettlementStreetSurfaces`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStreetSurfaces.cs),
  decided with the user 2026-10-02 after the all-road town read "too official"): every 10 m segment is scored by its distance
  from the centre + `orderRankDistance` per street order; the **road** (`Path_Straight` tiles) takes the best `roadShare`
  (15 %) of the network's length, main street only, and none below `minBuildingsForRoad` (8); the street kit's **slabs** take the
  next `slabShare` (25 %), main and side streets; the rest -- outskirts, every alley -- is **stone path**. Going out along a
  street the surface only steps down, so the road is the town's spine with slabs and paths branching off it (GDC-L1-LEVEL-0005,
  contextual: a legible spine, rewarded detours). Door paths use `doorSurface` (stones, `doorPathWidth` 1.2 m).
- **Roads**: a `Path_Node`, scaled `nodeScale` (1.3) so a junction reads as a widening of the road, wherever a road meets a road
  or slab street, at a road bend over `nodeTurn` and at the centre where the main street's halves meet; a frayed `Path_End`
  wherever a road stretch stops without a node -- a dead end, or the street carrying on as slabs or path. Roads are laid first,
  then slabs, then stones; each keeps off what is laid. `Path_Narrow` is in the library but unused.
- **Stone paths never get stairs** (user, 2026-10-02): a path stretch's level changes are left unclamped and its cells are
  locked to the smoothed natural ground, so it climbs with the hill; stones sample the ground.
- **A level change on a road or slab stretch is a chain**: one flight per level, a landing after every `flightsPerLanding`,
  straight down the street's tangent on the lower side of the change. Side streets climb up to `maxStairLevels` (4) at once,
  the main street `mainStreetStairLevels` (1) so it keeps winding. A change shrinks until its chain fits on its lower run with
  `minRunBetweenStairs/2` to spare. Wide flight on a road or a slab street over one slab wide, narrow on one slab.
- **A wall is a stack**: a `terraceWalls` top piece over as many 1.5 m courses as the drop in front needs (capped at
  `maxWallCourses`, 3: the pillar's depth), pillars at run ends, every `pillarEvery` blocks and past `pillarTurn`° of turn.
  Walls stand only between two buildings, as in the street kit (decided 2026-10-02: no walls along cut streets).

## Key types

| Type | Role |
| --- | --- |
| [`SettlementStreetSurfaces`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStreetSurfaces.cs) | `Assign(streets, centre, style, buildings)` → per street, `Section`s (arc from, to, surface) covering it; `At`, `IsTerraced`, `Rank` |
| [`SettlementStairChain`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStairChain.cs) | `Length(Δ) = Δ·flightRun + ⌊(Δ−1)/flightsPerLanding⌋·landingRun`, `Pieces`, `DropAt` (walking line below the top), `FlightFor(order, surface)` (null on a path), `Plan` → one `Placement` per road/slab profile step: top (change point + `StairTuck` uphill), forward, `topY`, corridor half-width, the street arcs it occupies (incl. its head: tuck tread or pillar) |
| [`SettlementWallStack`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementWallStack.cs) | `CourseCount(drop)` = ⌈(drop − 5 cm)/step⌉ − 1, capped, flags too tall; `PillarJoints(blockYaws, every, turn)` |
| [`SettlementRoadTiles`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementRoadTiles.cs) | `FitRun` (round(len/tile) tiles, Z-stretched to fit, none past `tileFitRange`), `FreeRuns` (a street minus blocked intervals) |
| [`SettlementStonePath`](Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Core/SettlementStonePath.cs) | `Scatter`: jittered steps along the line, centre-weighted sideways offset (mean of two draws), random yaw/piece/scale/sink, a pair stone at `pairChance`, `retries` new sideways draws on a clash then skip, smaller and sparser over the last `fadeStones` at a dead end |
| `SettlementTerraceField.ClampSteps` / `LockRamp` / `Lock(..., terraced)` | Multi-level profile changes that fit (paths unclamped); a chain corridor locked to `stairGroundBelowNosing` under its walking line; path stretches locked to the smoothed ground |
| `SettlementStreetPaver.LayStairChains` / `LayWalls` (stacked) / `LayTerraceStreets` | Place the pieces and count them for the summary |

## Flows

1. **Surfaces** — after the streets are trimmed ([SettlementStreets.md](SettlementStreets.md)), `Assign` the sections.
2. **Profiles** — `ClampSteps(runs, StairLevels(order), Length, minRun, terraced)`: going along the street, clamp each road or
   slab change to the order's cap, then shrink it while its chain + `minRun/2` overruns the lower run (before a climb: the run's
   length net of a descending chain already at its start; after a descent: the next run); a path change is left as the ground has it.
3. **Ground** — `Plan` the chains, `LockRamp` each corridor (`max(street, flight) half-width` + one heightmap sample) before
   the streets lock theirs (half-width + one heightmap sample; path stretches at the smoothed natural height), so the sculptor
   cuts a ramp under the stairs and lets paths slope.
4. **Stairs** — per chain, flights and landings at `top + forward·along`, `topY − drop`, unscaled; a `wallPillar` at each top
   corner (outside the flight's width) unless > 0.5 m into a plot. `ChainsByLevels` feeds the summary.
5. **Walls** — per contour (RDP + Chaikin), blocks chord to chord (`NextChord`, block length = widest top piece), each through
   the street kit's `WallFits` test; a failing block ends the run. Per run: top pieces (every other one's model mirrored in X),
   `CourseCount` courses at `(0, −k·step, k·courseBatter)`, then pillars at `PillarJoints` on the bisector at the higher top.
6. **Streets** (`LayTerraceStreets`) — centre lines smoothed by two Chaikin passes. Node places first (each blocks
   `node radius × nodeScale − nodeClearance` of every street through it; one node per spot), chains block what they occupy;
   per road section an end piece's length is reserved at each end without a node; tile the free runs at the street's level
   (odd tiles + `tileLift`), end pieces on the reserved ends, **then** the nodes. Then slab rows over each slab section
   (`SlabsAcross(order)`, on the ground like the street kit), then `Scatter` each street's path sections (other sections and
   chains blocked, fading at a dead end), stones grounded by raycast, sunk, tilted ≤ `maxTilt`.

## Multiplayer

N/A — edit-time scenery baked into the chunk scene; no scripts, no `NetworkObject`.

## Persistence

N/A — no runtime state; the terrain edit is backed up on the `Settlement` component as for the street kit.

## Gotchas

- **Roads lie at their level, not on the ground, so the band held level must reach past the heightmap's interpolation.**
  Locked only to its half-width (2.75 m), the next sample out was hillside and the sand rose 15 cm over the kerbs; from above
  the road read ragged. Streets and corridors lock one extra `heightmapScale.x` in tile mode.
- **A node's footprint is a square; nodes go down after the tiles.** `laid` holds rectangles, and the round node's square
  corners rejected every tile reaching its rim at an angle, so roads stopped 1-2 m short.
- **Tiles are seamless only unmirrored and never turned 180°** (their stones are periodic along Z); every tile keeps its
  street's forward. Hence the node at the centre, where the main street's halves start back to back.
- **Curved tiles overlap a few cm on the inside of a bend**; overlapping coplanar tops z-fight, so odd tiles sit 3 mm higher.
  A run's tiles are only tested against what was laid before the run.
- **A run that one tile cannot fill within `tileFitRange` is left bare**, typically 2-3 m between a stair foot and a node.
- **Chains are never stretched, so the meshes and the style must agree.** `flightRun` 2.7, `landingRun` 3.5 and `stepHeight`
  1.5 are guarded by `TerraceKitAssetTests` against footprints and the `Collision/Ramp`, `Deck`, `Body` and `Shaft` boxes
  (GDC-L1-CONTENT-0004). Rename those boxes and the test fails, not the game.
- **The pillar caps the stack at 4 levels** (6.65 m deep). Raise `maxWallCourses` only with a deeper pillar; the test checks it.
- **Stone spacing must clear the stones.** At the planned 0.9 m most 0.8-1.0 m stones clashed with their neighbour after their
  retries and only `Stone_Chunky` got through; the default is 1.2 m ± 25 %.
- **Mirror a wall top's model, never its root**: a negative root scale reaches its `BoxCollider`s, which Unity warns about per block.
- **Narrow flights often get no NavMesh** (DEFECTS.md): their 2.25 m walk, eroded by a 0.5 m agent at 0.333 m voxels on
  a 29° ramp, fragments -- 16 of 54 had none mid-ramp on a 20° test hill, against 1 of 17 wide ones. NPCs cannot cross those.
- **Surfaces are ranked per 10 m segment, so the shares are approximate** (a 15 % road on a 160 m network came out 30 m).
  The ranking runs after trimming: a street cut back to its houses changes the total the shares are taken of.
- **Not yet verified:** a player walking a chain in Play mode, on host and client. The evidence is the asset and pure-rule
  tests and generation on terrain copies.

## Extending

**Another kit** — fill the "Terrace kit" block of a style: `terraceWalls` = wall top, `road.tiles` (one length, along Z),
`road.node`, `road.end`, `stones.pieces`, `flightWide`/`flightNarrow` (origin top nosing, next at `(0, −stepHeight, flightRun)`),
`landing` (origin back edge, next flight at `(0, 0, landingRun)`), `wallCourses` (one step tall, top at the origin),
`wallPillar`; name their collision boxes as `TerraceKitAssetTests` expects and run it. **A new surface** — append a
`StreetSurface` value (serialized: never reorder), a rank and budget in `SettlementStreetSurfaces`, its width in
`PavedHalfWidth` and a branch in `LayTerraceStreets`, laid after roads if roads must win.
