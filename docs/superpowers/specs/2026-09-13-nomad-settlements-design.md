# Nomad settlements — design

Date: 2026-09-13
Status: approved, ready for implementation plan

Nine nomad settlements placed across the main world, in three size tiers, each bound to one root
GameObject that can be deleted or regenerated in a single gesture. Every settlement is built from
the forty nomad building prefabs and eighteen shade sails that shipped on 2026-09-13, sits flush on
terrain the builder chose for being flat, and is lived in by a population scaled to its tier —
residents who wander the town, flocks who move as groups, and patrols who walk its edge.

## Why a builder rather than nine hand-placed generators

The same argument `ClankerSettlementBuilder` records for the robot town. A generator dropped into a
chunk scene by hand cannot be reproduced: the ground it landed on is a click nobody can repeat, and
the layout depends on a random sequence nobody wrote down. Nine of them multiplies that by nine.

So one editor command chooses every site from constants, writes every settlement into the chunk
scene that owns its coordinates, verifies the result, and bakes the world NavMesh. Running it twice
gives the same nine towns. (`GDC-L1-CONTENT-0005`, contextual, confidence 4 — automate the
repetitive and keep builds reproducible.)

## What already exists

Nothing in this design invents a system. The inventory of parts, all verified present on
2026-09-13:

| Part | Where | Role here |
| --- | --- | --- |
| 40 building prefabs, sorted Large 12 / Medium 20 / Small 8 | `Assets/Game/Prefabs/Environment/Structures/NomadSettlement/` | The architecture. Convex colliders, LOD groups, static flags, no `NetworkObject`, no `SaveableEntity` |
| 18 sail prefabs, 8 freestanding + 10 already hung on buildings | `.../NomadSettlement/Tents/` | The scattered tents |
| `RobotSettlementGenerator` | `World/ProceduralGeneration/Settlement/Spawning/` | The "one root, children under `Generated`, Generate/Clear/Reroll" contract this copies |
| `ClankerSettlementBuilder`, `SettlementSiteScore` | `Assets/Game/Editor/Environment/` | Site search over open chunk scenes, scored by height range over a disc. Pure and tested |
| `SettlementPopulation`, `SettlementAlarm` | `Assets/Game/Scripts/agents/Faction/` | Keeps a town's people topped up; holds while the alarm is raised |
| `PatrolModule`, `BasePatrolModule`, `HerdModule` | `Assets/Game/Scripts/agents/Modules/` | The three wander behaviours |
| `WorldSiteMarker`, `SiteKind` | `Assets/Game/Scripts/World/Sites/` | Lets NPCs be sent to a settlement |
| `Nomad` + 4 colour variants + `NomadOstrich` | `Assets/Game/Prefabs/agents/` | The people. All six registered in `DefaultNetworkPrefabs.asset` with stamped prefabIds |

## Architecture — one constraint, four layers

### 0. Constraint: the existing systems are frozen

Nothing under `Settlement/` may be modified, nor `PatrolModule`, `BasePatrolModule`, `HerdModule`,
`SettlementPopulation`, `SettlementAlarm`, `WorldSiteMarker` or `ClankerSettlementBuilder`. The nomad
settlements are built entirely from new files, plus the five Nomad prefabs through their own builder.

Everything those frozen components need is a private `[SerializeField]`, and the project's existing
answer to that is `SerializedObject` — `NomadPrefabBuilder.ConfigureWatch` already sets `WatchModule`'s
private `priority`, `requiredRelationship` and `detectRadius` exactly that way. So the generator wires
them the same way and no `Configure` method has to be added to anything. The one real cost is that a
wrong field name writes nothing and reports nothing, which is why the placer's verification pass reads
every wired value back off the built town.

All new code lives in `Assets/Game/Scripts/World/ProceduralGeneration/NomadSettlement/`, a sibling of
the frozen `Settlement/` folder, so the boundary is visible in the tree rather than only on paper.

### 1. `NomadPlacementGeometry` — placement geometry, no RNG

Prefab footprint, clearance radius, spacing test, ground height. Free of random numbers, so the layout
solver owns the whole seeded sequence.

**This knowingly duplicates three private helpers** inside `RobotSettlementGenerator`
(`GetPrefabFootprint`, `BuildingClearanceRadius`, `IsTooCloseToOthers`). One shared file is the right
structure and would be a small, safe extraction — those helpers draw no random numbers, so lifting them
could not move the Clanker town's seeded layout — but it means editing a frozen file. The duplication
is deliberate, recorded in the new file's header and in `TerrainGeneration.md`, and collapsing it is a
small change the day the freeze lifts.

The ground sampling is **not** duplicated: `TerrainProbe.TryGetTerrainHeight` already answers exactly
this question for the under-terrain guard, is not frozen, and is called directly. Only the return shape
is adapted — a `float?` rather than a bool, because the solver and `SettlementSiteScore` both take
ground as a `Func` returning `float?`.

The foundation-pad path stays where it is on `RobotSettlementGenerator`. Nomad settlements reject
sloped ground instead of padding it, so they never needed it.

### 2. `NomadSettlementRecipe` — the tier data

A `ScriptableObject` with the four prefab arrays (`largeBuildings`, `mediumBuildings`,
`smallBuildings`, `tents`, auto-filled by the builder from the four folders), per-class count
ranges, the four band radii, spacing and padding, the slope tolerance and sample grid, and the
inhabitant table.

Three assets under `Assets/Game/ScriptableObjects/Settlements/`:

| Tier | Large bldgs | Medium | Small | Tents | Site radius | Core / mid / outer / tent radius |
| --- | --- | --- | --- | --- | --- | --- |
| `NomadSettlement_Large` ×2 | 6–8 | 10–12 | 6–8 | 12 | 80 m | 18 / 34 / 52 / 70 m |
| `NomadSettlement_Medium` ×3 | 2–3 | 5–6 | 4–5 | 7 | 55 m | 12 / 24 / 38 / 52 m |
| `NomadSettlement_Small` ×4 | 0–1 | 2–3 | 3–4 | 4 | 40 m | 8 / 16 / 26 / 36 m |

"Varying sizes" is therefore data, not three code paths.

### 3. `NomadSettlementGenerator` — the one object

A `MonoBehaviour` on a root named `NomadSettlement_<index>_<Name>`, with everything it creates under
a single `Generated` child. Context menus **Generate**, **Clear**, **Reroll** — the same contract as
`RobotSettlementGenerator`, so deleting or regenerating a whole town is one right-click on one
object. That is the deletability requirement, and it is met by reusing a pattern the project already
knows rather than inventing a registry.

Seeded with `System.Random(seed)` throughout, **not** global `UnityEngine.Random`. The robot
generator's use of global `Random` is the documented exception in `TerrainGeneration.md`, not a
pattern worth copying: same seed must give the same town.

**Layout.** Large buildings on the core ring facing inward — they run to 14.4 m tall and are the
landmarks. Medium in a looser second band with free yaw. Small huts scattered through the outer
band. Tents from the mid band outward past the buildings. No 90° yaw snapping: nomad adobe is not
axis-aligned, and the snapping in the robot generator exists for axis-aligned industrial prefabs.

**Ground binding.** Every candidate spot samples `Terrain.SampleHeight` on a 3×3 grid across that
prefab's measured footprint and rejects the spot if the height range exceeds the tier's tolerance,
retrying elsewhere on its band up to `placementAttempts` times. Two rules make this correct:

- Ground comes from the **terrain heightmap, never a raycast**. `RobotSettlementGenerator` raycasts
  with `terrainMask = ~0`, and because terrain and buildings share the `Default` layer, a later
  placement can land on an earlier building's roof (`TerrainGeneration.md`, Gotchas). Reading the
  heightmap cannot make that mistake.
- Terrain-feature footprints are read from `TerrainFeatureSpawner.Area.ComputeLocalBounds()` and
  kept out of, because a mesa's mesh is spawned at bake time and is **invisible to an editor
  raycast** — `Terrain.SampleHeight` reports the flat sand underneath it.

**On the root**, the generator also puts:

- A `WorldSiteMarker` — `SiteKind.Home` for large and medium, `SiteKind.Camp` for small, radius =
  the tier's outer radius, named from a fixed list so it reads as a place, not "Settlement 3".
- A `SettlementAlarm` and a `SettlementPopulation` owned by `NPCFaction`.
- A `Patrol` child holding one or two waypoint routes (see Population).

### 4. `NomadSettlementPlacer` — the editor command

`Tools > SpaceGame > Settlements > Build Nomad Settlements` and a `+ Bake NavMesh` variant, modelled
directly on `ClankerSettlementBuilder`:

1. Load `WorldStreamingConfig`, find the `SpawnPoint`, open every chunk with terrain (36 of 48).
2. Build the keep-out list: the spawn point, the Clanker settlement and its outer radius, every
   terrain-feature footprint, and every nomad site already chosen.
3. Score a 50 m candidate grid with `SettlementSiteScore.TryEvaluate` over each tier's site radius.
   Assign tiers largest-first to the flattest clearing candidates, with a **minimum 800 m between
   any two settlements**.
4. Keep every centre at least its site radius clear of the chunk boundary, so a settlement never
   straddles two chunk scenes — otherwise half a village pops in and out with the neighbouring chunk.
5. Per site: place the root in the owning chunk scene, seed = base seed + index, Generate, Verify,
   mark dirty, save, close.
6. One NavMesh bake covering all nine.

**Verification per settlement** (mirroring `ClankerSettlementBuilder.Verify`): every expected count
placed, no two footprints overlapping, no building's base off the terrain beneath it, every patrol
waypoint on terrain, and a printed report of chunk, centre, counts by class, tents, population and
site height range.

If a tier finds no site that clears the slope tolerance, the placer **errors with the shortfall**. It
does not silently place fewer settlements, and it does not tip one onto a dune.

## Population

The settlements are lived in, and the population scales with the tier.

| Tier | On foot at build | Mounted | Cap | `countRadius` | Spawn ring | Refill |
| --- | --- | --- | --- | --- | --- | --- |
| Large ×2 | 30 | 2 | 38 | 110 m | 25–75 m | 4 / 45 s |
| Medium ×3 | 16 | 1 | 20 | 80 m | 18–50 m | 3 / 60 s |
| Small ×4 | 7 | 0 | 9 | 60 m | 12–34 m | 2 / 75 s |

136 nomads on foot placed across the world, plus 7 mounted, against a total cap of 172.

`SettlementPopulation` counts a mounted pair as two, so a large town starts at 34 counted against a
cap of 38 — four of slack, which is the margin a wave refills into.

**A town starts full.** The build-time population is not a seed for `SettlementPopulation` to grow
from — at the shipped 2-per-60 s a 38-person town would take nineteen minutes to fill, so a player
walking in on a fresh world would find an empty village. Waves exist only to replace losses.

The people are the four colour variants — `Nomad_Tan`, `Nomad_Umber`, `Nomad_Maroon`,
`Nomad_StrawHat`, evenly weighted, not the uncoloured base `Nomad` — plus `NomadOstrich` riders on
the larger towns. They are **neutral to the player**: `GlobalRelationships.asset` records PlayerFaction ↔
NPCFaction as `Neutral`, and the Nomad prefab carries `ProvocationModule`, so a village of
thirty-eight mills around and only fights if the player starts it.

`spawnsPerWave` is the one wave knob `SettlementPopulation.Configure` does not expose, and the three
tiers refill at different rates. Wiring through `SerializedObject` rather than `Configure` settles that
without touching the frozen component — a private serialized field is a private serialized field, and
the editor reaches all of them.

### The three wander behaviours

All three already ship as modules. Nothing new is written.

| Archetype | Modules | Wired to |
| --- | --- | --- |
| **Resident** | `PatrolModule` in `RadiusBased` | `radiusCenter` = the settlement root, `patrolRadius` = the tier's outer radius |
| **Patroller** | `PatrolModule` in `PatrolPoints`, `SequentialLoop` | `patrolPoints` = a generated perimeter route |
| **Flocker** | `BasePatrolModule` at Fallback + `HerdModule` at Social | `baseTransform` = a per-flock anchor child; a unique `herdId` |

`PatrolModule.RadiusBased` is "pick random NavMesh destinations within a radius of a base point",
with an anchor the save system already persists. That is the centre-of-wandering requirement, met by
an existing, tested module. `BasePatrolModule`'s own header prescribes pairing it with `HerdModule`
at Social priority for group movement, and `HerdModule` broadcasts the highest-priority move intent
to every member and fans them into evenly spaced slots when they stop — so a flock crosses the town
together and spreads out when it arrives.

`herdId` is a **global** string key: without per-settlement ids, two towns' flocks would join one
herd and share movement broadcasts across kilometres. Ids are `nomad_s<settlement>_flock<n>`.

| Tier | Flocks | Patrol routes | Residents |
| --- | --- | --- | --- |
| Large | 3 × 4 = 12 | 2 × 3 = 6 | 12 |
| Medium | 2 × 3 = 6 | 1 × 3 = 3 | 7 |
| Small | 1 × 3 = 3 | — | 4 |

Patrol routes are emitted as a `Patrol/Route<n>` child of empty waypoint transforms on a ring at the
town edge, terrain-snapped at build; large towns get a second inner loop through the core. They are
plain GameObjects in the Hierarchy, so a designer can drag one afterwards.

**Wave refills spawn residents only.** `SettlementPopulation` spawns a prefab and knows nothing about
flocks or routes, so a flock that loses members stays short-handed while the general population
refills. That is a deliberate limit, not an oversight; lifting it means a per-archetype inhabitant
table.

### The base-prefab change this forces

Five prefabs carry `WanderModule` at Fallback: `Nomad` and its four colour variants. They are
independent prefab copies, not Unity prefab variants, so each has to be edited — a change to
`Nomad.prefab` does not propagate. (`NomadOstrich` has no `WanderModule` and is untouched.)
`AgentController` evaluates
movement-claiming modules highest-priority first and takes the first non-null result, so adding
`PatrolModule` beside `WanderModule` would put two modules at priority 0 and let component order
decide between them — exactly the ambiguity the priority ladder exists to prevent.

So `WanderModule` is **replaced** by `PatrolModule` (`RadiusBased`, `radiusCenter` empty) on all five.
With no centre assigned the module anchors to the spawn position, which also solves a
problem the old module had: `WanderModule` picks destinations within `wanderRadius` of the agent's
*current* position, making it a random walk with no home. Over an hour a nomad diffuses a few hundred
metres, leaves `countRadius`, stops counting toward the cap, and the town spawns a replacement —
forever. A cap of fourteen robots hides that; thirty-eight nomads across nine towns would not.

This changes behaviour for every nomad, caravan members included. Their fallback rarely runs —
`NpcWorldSim` and the formation sit far above priority 0 — but it is a change to a shipped prefab and
**caravan travel must be verified before this is called done**.

## Multiplayer

Buildings, tents and patrol waypoints are plain scene content inside chunk scenes. Every machine
loads identical bytes, so no netcode is involved and none of it is a `NetworkBehaviour` — the same
position `TerrainGeneration.md` records for the robot settlement.

The people are different, and already solved:

- The six nomad prefabs are registered in `DefaultNetworkPrefabs.asset` with stamped prefabIds
  (verified, all six).
- The build-time population is hand-placed instances of networked, saveable prefabs inside a chunk
  scene — the documented "scene-placed chunk instance" path the Clanker garrison already uses.
- `SettlementPopulation` decides and spawns on `!Network.IsNetworked || Network.Server`, deliberately
  **not** `Network.Simulates(this)`, because a settlement root is scenery with no `NetworkObject` of
  its own and `Simulates` would be true on every client. This is existing, correct code; the design
  only has to avoid breaking it.

Verification on an actual client, not just the host, is part of the acceptance criteria: a joining
client must see the same nine towns with the same people standing in them.

## Persistence

- **The geometry saves nothing, deliberately.** Buildings and tents are scene content with no
  `SaveableEntity`; regeneration is a designer action, not a load-time one. No saver is registered.
- **The people save themselves.** Each nomad carries `SaveableEntity` and a stamped prefabId, and
  `SettlementPopulation`'s spawns go through `GameServices.World.Spawn`, which opts them into the
  world save. The component itself persists only its clock, and a reload re-arming that clock costs
  one interval, not a population.
- **The archetype modules persist their own state.** `PatrolModule` and `BasePatrolModule` both latch
  a spawn anchor once and restore it rather than re-deriving it, precisely so a load does not
  re-centre a patrol area on the save position.
- **Save ids are hierarchy paths.** The build-time population are scene-authored, so their ids derive
  from scene + hierarchy path. Renaming a settlement root or its `Generated` child after a world has
  been saved orphans them. Same constraint as the Clanker settlement; it goes in the doc.

## NavMesh

Nine settlements make the single author-time world bake stale, and nothing in a chunk says so: the
garrison stands still on ground the mesh still thinks is open, and `WorldNavMeshBuildCheck` fails the
next build. The `+ Bake NavMesh` variant of the command does both in one run and is the intended
path.

Patrol waypoints are terrain-snapped at build, not NavMesh-snapped, because on a first run the
NavMesh covering the new town does not exist yet. `PatrolModule` samples the NavMesh at runtime.

## Design rationale

- `GDC-L1-LEVEL-0002` (contextual, confidence 4) — make space legible. Large buildings up to 14.4 m
  tall are the landmarks, and 800 m minimum separation plus three distinct tier silhouettes keeps
  each village its own reference point instead of one continuous smear of adobe. The principle's
  stated exception — deliberate disorientation for horror or mastery — does not apply to a desert
  the player is meant to learn to navigate.
- `GDC-L1-LEVEL-0006` (contextual, confidence 3) — show before you go. The tallest buildings sit on
  the core ring, visible across the dunes before arrival, so a settlement plants itself as a
  self-directed goal. The principle's own caveat is that a shown destination must pay off on
  arrival; that is why the population is in scope rather than deferred.
- `GDC-L1-CONTENT-0005` (contextual, confidence 4) — automate the repetitive, keep builds
  reproducible. Fixed seeds, one command, re-runnable, verified.
- `GDC-L1-LEVEL-0003` (pacing) is already honoured by `SettlementPopulation`, which holds waves while
  the alarm is raised so reinforcements never arrive mid-fight. This design inherits it.

## Risks and open questions

1. **Site starvation.** Whether nine sites of the required flatness exist at 800 m separation across
   36 terrain chunks, after the spawn point, the Clanker settlement and every mesa footprint are
   removed, is not knowable until the search runs. The placer reports the shortfall rather than
   degrading quietly; the fix is then a widened tolerance or a shrunk tier, and it is the designer's
   call.
2. **Concurrent agent load.** Chunks stream with `loadRadius` 1 over 500 m chunks, so a 1500 m window
   can hold two settlements — worst case roughly 58 live `NavMeshAgent`s plus their behaviour
   modules. 800 m separation reduces this but does not bound it. **This needs a profiler number, not
   an estimate**, and lowering the caps is the lever if it does not hold.
3. **`herdId` collisions.** Global string keys mean a typo merges two towns' flocks silently — every
   flock in both towns walking to the same place. Ids are unique by construction
   (`nomad_s<index>_flock<n>`), and the built scenes are checked for duplicates after the placer runs.
4. **Flock attrition.** Wave refills are residents only, so flocks and patrols shrink permanently
   under losses. Accepted for this change.

## Out of scope

- The second world. `WorldChunkerEditor` paths are hardcoded to the main world; extending there is
  its own change.
- Interiors. The buildings are exterior shells with convex colliders; nobody goes inside.
- Nomad tasks and errands. `NpcTaskModule` and `AgentGoal` exist and a `WorldSiteMarker` on each root
  means settlements are already addressable as destinations, but wiring errands between them is a
  separate feature.
- Trade, dialogue, quests, or anything the player can do *to* a settlement beyond walking in.

## Documentation

`TerrainGeneration.md` gains the new recipe, generator and placer in Key types, a Flows entry, and
Gotchas for the heightmap-not-raycast rule, the chunk-boundary clearance, the global `herdId` key and
the hierarchy-path save ids. `AgentSystem.md` gains the three archetypes and the `WanderModule` →
`PatrolModule` swap on the Nomad prefabs. `symptoms:` entries for anything that costs real time.
`docs/Human/the-systems.md` gains a plain-language paragraph. Then
`python3 tools/docs_check.py --index`.
