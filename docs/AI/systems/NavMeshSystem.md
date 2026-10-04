---
system: NavMesh
layer: world
summary: One author-time bake of the whole world into a single asset, added at runtime; nothing bakes at runtime
paths:
  - Assets/Game/Scripts/World/Streaming/NavMesh/
  - Assets/Game/Settings/WorldNavMesh.asset
  - Assets/Game/Settings/SkyCityNavMesh.asset
  - Assets/Game/Scripts/agents/Modules/Movement/WanderModule.cs
  - Assets/Game/Scripts/agents/AI/Motors/
  - ProjectSettings/NavMeshAreas.asset
symptoms:
  - "an NPC spawns and then stands still forever with a clean console"
  - "penned animals never leave the pen even with the gate open"
  - "creatures path through geometry that is no longer there"
  - "the player build fails with BuildFailedException about the world NavMesh"
  - "agents refuse to cross a gap or take a jump link"
  - "a MeshCollider I added is missing from the bake and nothing errors"
  - "a save-restored creature is on the NavMesh but never moves"
  - "arena spawns are not filtered for reachability"
  - "every agent hovers a few centimetres to half a metre above the ground"
  - "creatures walk over a rained-on patch as if it were dry sand"
  - "an NPC spawned on the sky city has no NavMesh under it"
  - "sky city NPCs stand on roofs or gas bags they can never leave"
  - "sky nomads walk to a railing and stand there staring up at a roof"
  - "an NPC on wet ground turns on the spot and never gets anywhere"
  - "an agent stands still with its path pending forever and a clean console"
  - "a ridden mount slows down as if braking although I am holding forward"
  - "residents never reach a spot on a terrace across a narrow gap or a short drop"
  - "the audit says a settlement spot is off-mesh or unreachable from the walkable heart"
  - "the NavMesh audit or link rebuild froze the editor for minutes"
  - "penned animals walk out through the fence although the gate is shut"
  - "an NPC leaps over a thin wall or fence"
reads_with: [WorldStreaming, AgentSystem, Locomotion, SkyTribe]
updated: 2026-10-04
---

# NavMesh

One NavMesh for the whole streamed world, baked at author time into a single asset and added to the runtime with one `NavMesh.AddNavMeshData` call; nothing bakes at runtime. Walkable structures outside the chunk scenes (the Sky City fleet) get their own small bake, added at the structure's transform.

**Scope:** `Assets/Game/Scripts/World/Streaming/NavMesh/`, `ProjectSettings/NavMeshAreas.asset`, `Assets/Game/Settings/WorldNavMesh.asset`, `Assets/Game/Scripts/agents/AI/Motors/`
**Related:** [WorldStreaming.md](WorldStreaming.md) · [Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs](Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs) · [.claude/skills/spacegame-agent/SKILL.md](.claude/skills/spacegame-agent/SKILL.md)

## Model

- **One mesh, not per-chunk.** [WorldNavMeshBaker](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshBaker.cs) opens all 48 chunk scenes at once, collects sources, and calls `NavMeshBuilder.BuildNavMeshData` once over their union bounds. Chunk scenes carry no `NavMeshSurface` and no NavMesh data of their own.
- **Runtime is load-only.** [WorldNavMeshProvider](Assets/Game/Scripts/World/Streaming/NavMesh/WorldNavMeshProvider.cs) on the `NavMesh` GameObject in [persistentScene.unity](Assets/Game/Scenes/world/persistentScene.unity) does `AddNavMeshData` in `OnEnable`, `Remove()` in `OnDisable`. No bake, no rebuild, no dirty flag. The `NavMeshSourceCache` / park-and-release system the older revision of this doc described is **deleted**.
- **Collision, not render meshes.** Sources are `Terrain` + non-trigger `Collider`s, filtered by [`NavMeshSources.IsBakeable`](Assets/Game/Scripts/World/Streaming/NavMesh/NavMeshSources.cs) (runtime assembly, shared with a settlement's throwaway character-placement bake — see [TerrainGeneration.md](TerrainGeneration.md)): colliders on a non-kinematic `Rigidbody` are skipped (scenery that moves must not be frozen into a permanent mesh), and so is anything under a `NavMeshAgent` (a walker is never part of what it walks on — see Gotchas).
- **Bake mirrors the runtime.** The baker snaps each chunk's `Terrain` to its grid X/Z (mirroring `WorldStreamer.CacheTerrainForChunk`) and calls `TerrainFeatureSpawner.SpawnBaked()` before collecting, then discards those edits. Skip either and the mesh is silently offset from the ground.
- **Staleness is enforced at build time.** [WorldNavMeshStaleness](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshStaleness.cs) compares each chunk's `AssetDatabase.GetAssetDependencyHash` against the stamp recorded at bake; `WorldNavMeshBuildCheck : IPreprocessBuildWithReport` throws `BuildFailedException` when they differ.
- **Caves are separate surfaces**, not part of the world mesh — see Gotchas.
- **So is the Sky City.** `SkyCityFleet` stands in `persistentScene`, which the world bake never scans. SkyCityNavMeshBaker bakes the fleet prefab's collision **in prefab space** into [SkyCityNavMesh.asset](Assets/Game/Settings/SkyCityNavMesh.asset) (a bare `NavMeshData`, ~130 KB), with the world bake's settings and layer mask read from `WorldNavMesh.asset` and its collider filter/mapping (`NavMeshSources.IsBakeable` / `TryColliderToSource`). [StaticNavMeshData](Assets/Game/Scripts/World/Streaming/NavMesh/StaticNavMeshData.cs) on the fleet prefab root adds it at the instance's position and rotation, so moving the fleet in the scene needs no re-bake.
- **The ground gets a say.** `NavMeshAgentMotor.ApplyGroundGrip` asks [GroundGrip](Assets/Game/Scripts/Gameplay/Grip/GroundGrip.cs) what the surface under the agent is worth and scales `agent.acceleration` by it, so a [SurfaceCoat](Artifacts/SurfaceCoat.md) patch slows how fast an NPC can change velocity without touching its top speed — it overshoots and cannot brake. **Acceleration only, never `angularSpeed`:** an agent that cannot turn cannot follow its path off the patch, which is stuck rather than sliding. Same reason `LeggedLocomotion.ApplyGroundGrip` leaves yaw alone.
- **Two motor families:** [NavMeshAgentMotor](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs) drives a real `NavMeshAgent`; [LeggedDriver](Assets/Game/Scripts/agents/AI/Motors/LeggedDriver.cs) has no agent component and only calls `NavMesh.CalculatePath` (the legs own the transform).

## Agent types & areas

Exactly **one** agent type is configured project-wide ([ProjectSettings/NavMeshAreas.asset](ProjectSettings/NavMeshAreas.asset)). Every `NavMeshAgent` in the project uses `m_AgentTypeID: 0`; no prefab or scene sets any other value.

| Agent type | ID | radius | height | slope | climb | cellSize | tileSize | minRegionArea |
|---|---|---|---|---|---|---|---|---|
| Humanoid | 0 | 0.5 | 2 | 45° | 0.75 | 0.1667 | 256 | 2 |

The world bake **overrides** those numbers (`WorldNavMeshBakeSettings.ToBuildSettings`). Values actually stored in [WorldNavMesh.asset](Assets/Game/Settings/WorldNavMesh.asset):

| Field | Baked value | Note |
|---|---|---|
| agentTypeID | 0 | Humanoid |
| agentRadius / agentHeight | 0.5 / 2 | matches project settings |
| agentSlope | **60°** | steeper than the project's 45° |
| agentClimb | **0.8** | taller than the project's 0.75 |
| voxelSize | **0.3333** (radius/1.5) | dominant cost knob; Unity default radius/3 costs 4x |
| tileSize | 256 | ≈85 m tiles at this voxel size |
| minRegionArea | 2 m² | |
| layerMask | `0xFFFFFC89` | excludes TransparentFX, Ignore Raycast, Water, UI, Player, Hologram, Interior |
| stamps / sourceCount / bakedAtUtc | 48 chunks / 239 sources / 2026-10-01 (re-baked for the regenerated nomad settlement in Chunk_6_3) | |

Areas: Unity's three built-ins, unchanged — `0 Walkable` (cost 1), `1 Not Walkable` (cost 1), `2 Jump` (cost 2) — plus the project's `3 Ladder` (cost 3). Slots 4–31 are empty. Every source is baked with `area = 0`; Jump and Ladder exist only on off-mesh links (see Links). Queries pass `NavMesh.AllAreas`, except a legged rig's, which passes `NavLinkAreas.GroundMask`.

## Key types

| Type | File | Role |
|---|---|---|
| `WorldNavMeshAsset` | [WorldNavMeshAsset.cs](Assets/Game/Scripts/World/Streaming/NavMesh/WorldNavMeshAsset.cs) | ScriptableObject: `NavMeshData` sub-asset + bake settings + per-chunk dependency-hash stamps |
| `WorldNavMeshBakeSettings` | same file | Serialized bake inputs (deliberately not a raw `NavMeshBuildSettings`) |
| `WorldNavMeshProvider` | [WorldNavMeshProvider.cs](Assets/Game/Scripts/World/Streaming/NavMesh/WorldNavMeshProvider.cs) | `AddNavMeshData` on enable; `LogError` (never a silent fallback) if unassigned |
| `WorldNavMeshBaker` | [Editor/WorldNavMeshBaker.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshBaker.cs) | `World/Streaming/Bake World NavMesh` menu item; asset path `Assets/Game/Settings/WorldNavMesh.asset` |
| `StaticNavMeshData` | [StaticNavMeshData.cs](Assets/Game/Scripts/World/Streaming/NavMesh/StaticNavMeshData.cs) | `AddNavMeshData(data, transform.position, transform.rotation)` on enable, remove on disable; `LogError` if unassigned. No scale |
| `SkyCityNavMeshBaker` | Editor/Environment/SkyCityNavMeshBaker.cs | `World/Streaming/Bake Sky City NavMesh`; bakes `SkyCityFleet.prefab` into `Assets/Game/Settings/SkyCityNavMesh.asset` and puts `StaticNavMeshData` on the fleet root |
| `WorldNavMeshStaleness` / `WorldNavMeshBuildCheck` | [Editor/WorldNavMeshStaleness.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshStaleness.cs) | `World/Streaming/Check World NavMesh Is Current`; fails the player build when stale |
| `WorldStreamer.SnapAgentsToNavMesh` | [WorldStreamer.cs](Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs) (~L1229) | Re-enables + `Warp`s a loaded chunk's agents onto the mesh |
| `NavMeshAgentMotor` | [NavMeshAgentMotor.cs](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs) | `IMovementMotor` over `NavMeshAgent`; `[DefaultExecutionOrder(-100)]`. Leaves `autoBraking` as the prefab authors it |
| `LeggedDriver` | [LeggedDriver.cs](Assets/Game/Scripts/agents/AI/Motors/LeggedDriver.cs) | Path-only consumer: `NavMesh.CalculatePath`, no `NavMeshAgent` |
| `DeferredNavMeshWarp` | [DeferredNavMeshWarp.cs](Assets/Game/Scripts/Core/Persistence/Runtime/DeferredNavMeshWarp.cs) | Retries a save-restore `Warp` for 10 s, sample radius 4 m |
| `CaveSpawner` | [CaveSpawner.cs](Assets/Game/Scripts/World/ProceduralGeneration/Cave/Generation/CaveSpawner.cs) | Own `NavMeshSurface`; `SpawnBaked()` adds a pre-baked `NavMeshData` instance |
| `NavMeshGraph` | [Editor/NavMeshGraph.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/NavMeshGraph.cs) | A triangulation as geometry: welded vertices, islands (union-find over shared *edges*), boundary edges, XZ lookup to the surfaces under a point. Pure data, so tests feed it hand-built meshes |
| `NavLinkAreas` | [NavLinkAreas.cs](Assets/Game/Scripts/Gameplay/Traversal/NavLinkAreas.cs) | Area IDs by name (`Jump`, `Ladder`) and `GroundMask`; [NavMeshAgentMotor.Links.cs](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.Links.cs) crosses the links |
| `NavMeshAutoLinker` | [Editor/NavMeshAutoLinker.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/NavMeshAutoLinker.cs) | Finds jump links across gaps and down ledges from a `NavMeshGraph`; run by the baker |
| `SettlementNavMeshAudit` | [Editor/SettlementNavMeshAudit.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/SettlementNavMeshAudit.cs) | `World/Streaming/Audit Settlement NavMesh`: islands, then every settlement place checked against the mesh. See Auto links and audit |
| `NavMeshReach` | [NavMeshReach.cs](Assets/Game/Scripts/World/Streaming/NavMesh/NavMeshReach.cs) | `CanWalk(from, to)`: a `CalculatePath` that is `PathComplete`. The one reachability check — `SettlementPopulation.reachableFrom` and `WanderModule.onlyReachableDestinations` both call it |

## Flows

**Bake (editor only)**
1. Close every chunk scene — the baker refuses outright if any is open (it mutates scenes and can only safely discard its own).
2. `World/Streaming/Bake World NavMesh`. Config comes from `WorldNavMesh.asset.config`; with two `WorldStreamingConfig` assets present (`WorldStreamingConfig`, `FerdinandWorldStreamingConfig`) it refuses to guess.
3. Opens all 48 chunks additively → aligns terrain → `SpawnBaked()` features → `Physics.SyncTransforms()` → collects → `BuildNavMeshData` → writes the `NavMeshData` sub-asset, stamps, source count.
4. Closes with `removeScene: true`, discarding the scaffolding.

**Bake the Sky City (editor only)**
1. `World/Streaming/Bake Sky City NavMesh` (or `Tools/Environment/Build Sky Fleet Prefabs`, which ends by calling it). No scenes need closing: it reads the fleet prefab with `LoadPrefabContents`.
2. Refuses if `WorldNavMesh.asset` is missing (it is the settings source) or the fleet root is scaled.
3. Collects every bakeable collider (686 today, all `Default` layer) with the root at the origin → builds into the existing asset with `UpdateNavMeshData`, so its GUID and the prefab's reference survive → adds/points `StaticNavMeshData` on the fleet root, saving the prefab only if that changed.

**Chunk load**
1. Chunk scene loads (`AdoptLoadedChunk` / `OnOfflineSceneLoaded` / NGO `LoadEventCompleted`).
2. `SnapAgentsToNavMesh(coord)` walks the scene's `NavMeshAgent`s; skips any already enabled *and* `isOnNavMesh`.
3. Otherwise `NavMesh.SamplePosition` within `max(radius*4, height*2, agentSnapDistance=32 m)` → set `transform.position` → `agent.enabled = true` → `agent.Warp(hit.position)` unconditionally (order matters: `Warp` is a no-op on a disabled agent, and `isOnNavMesh` is not yet usable on the frame of enable).
4. Failure logs a warning naming the agent. No bake is ever scheduled.

**Agent path**
1. Brain/module produces a `MoveIntent` on `AgentController`.
2. `NavMeshAgentMotor.Tick` → `SetDestination` / `isStopped`; stuck recovery resets the path after `stuckTime` (1.5 s) below `stuckVelocityThreshold`.
3. Legged machines instead call `NavMesh.CalculatePath` and hand corners to `WalkerPath`; `LeggedLocomotion` clamps the commanded twist to what the stride can carry.

## Multiplayer

Yes — every machine has the identical mesh. `WorldNavMeshProvider` is a plain scene component in `persistentScene`, and the baked data ships in the build, so host and client both `AddNavMeshData` the same bytes locally (`StaticNavMeshData` on the Sky City fleet likewise runs on every machine); nothing about the NavMesh is replicated. Pathing runs wherever the agent simulates: `AgentController`/motor ticks are gated by `NetAuthority`, so the **server** paths NPCs and clients see replicated transforms. A client never disagrees about the mesh, only about who is allowed to drive an agent along it.

## Persistence

The mesh itself is authored data, not save state: [Assets/Game/Settings/WorldNavMesh.asset](Assets/Game/Settings/WorldNavMesh.asset) (~36 MB, binary-serialized, `NavMeshData` stored as a sub-asset named `WorldNavMeshData`). Cave bakes live beside their scene in `CaveBakes/seed_NNNN_NavMesh.asset`. The Sky City mesh is [Assets/Game/Settings/SkyCityNavMesh.asset](Assets/Game/Settings/SkyCityNavMesh.asset) (~130 KB). Nothing NavMesh-related is written to a save file. Save/load interacts with it only through `DeferredNavMeshWarp`, which retries a restored agent's `Warp` until the mesh is reachable rather than falling through to a raw transform write (which moves the GameObject but not the agent's internal position — a silently non-moving creature).

## Gotchas

- **A coat only reaches an NPC through its motor.** `GroundGrip` is a registry that must be *asked*; nothing pushes an agent. `NavMeshAgentMotor` asks once per `Tick`, downstream of the on-NavMesh, not-leaping and not-carried gates — a body being flown along a rope or through a leap arc is not standing on the patch below it. A motor that never asks walks over frost as if it were sand, which is exactly how the sprayed film used to read for every NPC in the game. **Zero acceleration is a latch, not a slide.** `minGripAcceleration` floors what grip can take away. An agent whose acceleration reached zero could never leave the patch it is standing on, and twenty seconds of sliding would become twenty seconds of nothing (GDC-L1-BAL-0004).
- **A creature hand-placed in a chunk scene used to bake in as a hole in its own mesh.** The baker kept every kinematic body as scenery, and every NavMesh creature here *is* a kinematic body with a solid collider — so the six patrol robots in the Clanker settlement each carved a robot-shaped hole exactly where they would spawn (2026-09-07: no mesh within 0.35 m of any robot, mesh everywhere around them). `NavMeshSources.IsBakeable` now refuses any collider under a `NavMeshAgent`; `WorldNavMeshBakerSourceTests` pins it. A walker driven by something other than `NavMeshAgent` (a `LeggedDriver`) is still baked if it is kinematic and sits in a chunk scene — none does today.
- **A gate leaf baked in seals its doorway for good.** A pen gate's leaf carries both a solid collider and a *carving* `NavMeshObstacle`; the obstacle already cuts the doorway while it is shut. When the bake also took the collider, the doorway was missing from the static mesh, so opening the gate never let the penned stock out. `NavMeshSources.IsBakeable` now refuses a collider that shares its object with a carving obstacle. Carving only runs in play mode, so an edit-mode bake (a settlement's throwaway placement bake included) sees the gate as open.
- **The baked mesh sits above the ground, and by a varying amount.** Measured over 1384 samples on six terrains: mean +0.264 m, median +0.257, p25 +0.199, p75 +0.321, p95 +0.480, max +0.600, min −0.262. Recast places each polygon at the top of the voxel column it came from, so the error scales with `voxelSize` (0.3333 here) and is inherent to the bake, not a fault in it. Halving `voxelSize` roughly halves it while quadrupling bake time and asset size — not worth it. `AgentGroundConform` corrects it at runtime instead; see [AgentSystem.md](AgentSystem.md).
- **No chunk seams to handle.** The mesh is one build over the union bounds; there are no per-chunk tiles to stitch. The corollary is that a chunk edit invalidates the *whole* bake — re-bake all 48, there is no per-chunk path. **The bake can be silently wrong.** Nothing at runtime checks freshness; only `World/Streaming/Check World NavMesh Is Current` and the build preprocessor do. In the Editor a stale bake just means NPCs navigate a world that no longer exists.
- **Off-mesh links are baked in now, and only as Jump.** The world bake adds auto links (see Auto links and audit) and the narrow stair flight carries one `NavMeshLink`; the baker still never sets `GenerateLinks`. Links join *different islands* only, so a drop onto the same piece of ground is not linked, and the one-way drops are traversed by `NavMeshAgentMotor`'s leap. Anything planning with `NavLinkAreas.GroundMask` (legged rigs) ignores them. The `m_AutoTraverseOffMeshLink` fields on agent prefabs stay inert: the motor sets it itself.
- **`persistentScene` still has a legacy `NavMeshSurface`** on the same `NavMesh` GameObject, `m_Enabled: 0` with `m_NavMeshData: {fileID: 0}`. It contributes nothing. Do not enable it; do not treat it as the world surface.
- **The `Interior` layer is excluded from the world bake**, so cave interiors never merge with the world mesh; each `CaveSpawner` adds its own `NavMeshData` instance and removes it in `ClearPrevious`/disable. A cave without `bakedMesh` + `bakedNavMeshData` assigned generates and bakes live on `Start` — seconds of stall. **The layer mask is only defaulted at asset creation** (`LoadOrCreateAsset`). Adding a new layer later does *not* update the existing asset's mask; a new walkable layer above bit 10 is included by accident, a new character layer must be excluded by hand.
- **`MeshCollider` sources need readable meshes** — `TryColliderToSource` silently returns `false` for `isReadable == false`, so the geometry vanishes from the bake with no error. Watch the reported source count (currently 130); a sudden drop means geometry went missing.
- **`NavMeshAgentMotor.Awake` disables its own agent** when `SamplePosition` finds nothing within `navMeshSnapDistance` (6 m), on the promise that `WorldStreamer.SnapAgentsToNavMesh` re-enables it. Nothing else keeps that promise — an agent spawned outside a streamed chunk scene, or in a scene the streamer does not own, stays dead for the session with no error.
- **`autoBraking` is the prefab's, not the motor's (since 2026-10-02).** `NavMeshAgentMotor.Awake` used to force `agent.autoBraking = false` on every agent, with no recorded reason, over 48 prefabs that author `m_AutoBraking: 1` (only `DuneRat` authors 0). The override is gone, so those agents now slow into the end of every path. **Unverified in play:** a ridden mount steers by a carrot `RiderLookahead` metres ahead (`riderStopDistance`), and with braking on it may decelerate toward that carrot. If it does, set `m_AutoBraking: 0` on the ridden NavMesh mounts (Appa, RobotHorse, Sandloper) — not back in code. Chase repaths every tick and its stop distance is small, so a moving target is not expected to be affected.
- **Bake settings diverge from project settings** (60°/0.8 vs 45°/0.75). A `NavMeshAgent` inspector preview or an editor `NavMeshSurface` bake uses the *project* numbers and will not match what ships.

- **The Sky City mesh is many islands, and most are unreachable.** Measured 2026-09-17: 9 644 m² of mesh in ~120 welded islands; the connected region under both promenades is ~3 300 m² (both lanes, the crossings, the ground-level decks). The rest — the y≈23–39 gantry/roof/tower decks reached by ladders (each ladder now registers a link NPCs climb — see Links — once its ends are on a mesh), house roofs, gas-bag tops (convex hulls under the 60° slope limit) and the escort ships' decks (~190 m²) — are islands an agent spawned on can never leave. Anything sampling random points on the city must filter them through `NavMeshReach.CanWalk`: the city's `SettlementPopulation` spawns only where its `PromenadeAnchor` reaches, and the sky nomads wander with `onlyReachableDestinations` (measured in play 2026-09-17: 16/16 residents and every walking destination on the promenade region, before and after a save/reload) — see [AgentSystem.md](AgentSystem.md).
- **On an islanded NavMesh a random wander point is a railing, not a walk.** `SamplePosition` happily returns a roof or gas-bag top beside the Sky City promenades (44 % of a sky nomad's 10 m wander picks, measured); the agent gets a partial path, walks to the nearest edge of its own island, reports arrival and stands there. `WanderModule.onlyReachableDestinations` refuses any point `NavMeshReach.CanWalk` does not reach from the agent; `NomadRecipe.WanderReachableOnly` sets it on the four sky nomads (and the not-yet-built sky soldier's recipe — see [SkyTribe.md](SkyTribe.md)); off everywhere else, since it refuses every point for an agent that is off the mesh. With `onlyReachableDestinations` on, `TryPickDestination` can fail every one of its `maxSampleAttempts` on a bad frame (an agent off the mesh, or boxed in by islands, refuses every candidate); `Tick` backs off to `minWaitTime` on that failure instead of re-rolling next frame, so a permanently-stuck agent costs one NavMesh query per wait interval, not one per frame.
- **A city-only rebuild leaves the Sky City mesh stale, silently.** `Build Sky Fleet Prefabs` re-bakes; `Build Sky City Prefab` alone does not, and nothing checks. **Follow-up (not built):** a `WorldNavMeshBuildCheck`-style staleness stamp for `SkyCityNavMesh.asset` (dependency hash of `SkyCityFleet.prefab`). **`StaticNavMeshData` ignores scale.** `AddNavMeshData` takes position and rotation only; a scaled fleet instance would walk on a mesh of the wrong size. The baker refuses a scaled prefab root; a scaled scene instance is not checked.

## Links

Every off-mesh link is crossed by `NavMeshAgentMotor`: `autoTraverseOffMeshLink` is off in its `Awake`, the agent halts at the link start and the motor takes the body by hand (`updatePosition` off, `CompleteOffMeshLink`, `Warp` onto the mesh at the far end so the next `MoveTo` re-plans; `AgentGroundConform` stands down via `IsRidingLink` / `IsLeaping`). The link's **owner** picks the crossing:
- **Owner is a `Ladder`** (area `Ladder`, registered by `Ladder.OnEnable`, bidirectional): a climb at `ladderClimbSpeed` / `ladderDescendSpeed` — [Ladders.md](Ladders.md).
- **Owner is an `INavLinkGate`** (a colony airlock's `AirlockPassage`, registered like a ladder's link, area Walkable, `costModifier` 4): the motor stops the traveller at the link's start and asks the gate each tick what to do (wait, walk to a point, done); the gate works the hatches. A nomad door or a pen gate could implement it later. `NavLinkGates.TransitSecondsBetween` lets `SettlementSociety.TravelMinutes` charge the crossing. See [ColonyInterior.md](ColonyInterior.md).
- **A `NavMeshLink` component outside `Jump`** (the narrow stair flight): a straight walk at `plainLinkSpeed`.
- **Anything else** (area `Jump`, no owner, a baked drop — the auto linker's): a leap through the mounted-leap machinery, arc `jumpLinkArcHeight + horizontal * jumpLinkArcPerMetre` at `jumpLinkSpeed`.
- **Your own link:** `NavMesh.AddLink` in `NavLinkAreas.Jump`, both ends snapped onto the mesh, `costModifier = -1` (the default 0 is free), no owner. **"Can it get there via links?"** `NavMeshReach.CanWalk` already says yes (`AllAreas`); a walker that cannot cross links queries with `NavLinkAreas.GroundMask`; a creature that cannot climb sets `usesLadders = false`.
- **Server-authoritative; nothing persisted.** A ladder ride is not saved (a mid-ride save loads through `DeferredNavMeshWarp`); a jump in flight is a leap and uses the leap's saved state. No NPC climbing pose. Test: `LadderTraversalTests`.

## Auto links and audit
**Auto links.** The world bake builds from raw sources, so Unity generates no jump or ledge links, and an island one slab or one metre away from the next is unreachable. After `BuildNavMeshData`, [WorldNavMeshBaker](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshBaker.cs) puts the new mesh on the NavMesh for a moment, triangulates it, and [NavMeshAutoLinker](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/NavMeshAutoLinker.cs) walks every **boundary edge** (an edge one triangle uses). At every `edgeSampleSpacing` along it, it looks straight out from the edge, requires the first `voidProbeDistance` to be empty, and walks outward until it meets mesh on a **different island**. Rules, all serialized in `WorldNavMeshAsset.linkSettings`:
- **Gap:** far side within `maxGap` 1.5 m and between `-minDrop` and `+maxRise` 0.5 m of the near edge. Bidirectional.
- **Drop:** far side `minDrop` 0.8 to `maxDrop` 2.0 m lower, within `maxDropReach` 2.0 m, landing island at least `minDropLandingArea` 20 m² (a smaller one is a trap). **One way**, high side to low.
Too wide, too deep, same island, or an island under `minIslandArea` 2 m² gets nothing. Distances are between mesh *edges*, which sit one agent radius back from the real ledge, so a sheer ledge is never closer than ~1 m however thin the geometry gap. Links are thinned to one per `linkSpacing` 6 m per island pair and capped at `maxLinks` 20 000 (hitting it is reported). They are made in the `Jump` area ([NavLinkAreas.Jump](Assets/Game/Scripts/Gameplay/Traversal/NavLinkAreas.cs)) with **no component**, which `NavMeshAgentMotor` crosses as a leap (`IsPlainLink` is only true for a `NavMeshLink` component outside `Jump`). `WorldNavMeshAsset.AddAutoLinks` (called by `WorldNavMeshProvider.OnEnable`) does `NavMesh.AddLink` per entry and `OnDisable` removes them.
- **Persistence: none.** Links are derived from the bake and stored in `WorldNavMesh.asset` (`autoLinks`, appended after the older fields so an unlinked asset still loads); every machine adds the same list. Nothing is written to a save, and every bake discards and regenerates the list, so a link never outlives its geometry. **2026-10-03 bake:** 309 links (195 drops, 114 gaps).
- **A wall is not a gap.** The mesh edges either side of a fence post line or a thin wall look exactly like the edges of a gap, so the first auto links leapt over every pen fence (a closed pen, in play, had a complete path from each rat to the settlement's heart; five of ten rats were outside within minutes). The bake therefore hands the linker a `LinkBlocked` test: a raycast `linkSettings.wallProbeHeight` (0.5 m) above the two ends that hits a collider the bake treats as an obstacle (`NavMeshSources.IsBakeable`, gate leaves counted as shut) drops the candidate (13 of 322 went). It needs the chunk scenes open, which only the bake has, so the old `Rebuild World NavMesh Links` menu is gone: re-bake instead (8 s warm).
- **`Terrace_Stairs_Flight_Narrow` carries its own `NavMeshLink`**, top nosing (0.4 m back) to the foot of the ramp (0.4 m beyond), 1.5 m wide, bidirectional, **Walkable** area, because a flight whose ramp fragments is the one case where the whole ramp (3 m, past `maxGap`) has no mesh to link *to*. Walkable area on a `NavMeshLink` component makes the motor walk it, not leap.
**Audit.** `World/Streaming/Audit Settlement NavMesh` (or `SettlementNavMeshAudit.Run()`, which returns the report) needs the chunk scene holding a settlement open, or Play mode. With no NavMesh live it puts `WorldNavMesh.asset` and its auto links on for the run and removes them after; in edit mode the scenes sit where they were saved, not where the streamer puts them. It reports islands (count, those under 10 m², the twelve largest with centres), then per `Settlement`: every `SettlementSpot`, `SettlementEntrance`, `Ladder` foot and top, and pen gate (any outermost transform named `*Gate*`, tried at 1.5 m either side) as **ok**, **OFFMESH** (no mesh within 1 m, or within `ResidentTuning.standSnapRadius` for a spot and `doorThresholdSnap` for an entrance, the radii residents use; says how far the nearest is) or **UNREACHABLE** (on mesh but `CalculatePath` from `WalkableHeart` is not complete; says which island and how big). Each `Terrace_Stairs_Flight_Narrow` is checked for mesh at mid-ramp and for a walkable top-to-bottom path no longer than 2x the ramp. Failures are listed by hierarchy path and position, and drawn as red/orange discs in the Scene view (`Show Settlement NavMesh Audit Findings` toggles it).

- **Baseline, 2026-10-02, old bake, Chunk_6_3 opened additively in edit mode, 2.2 s:** 327 325 triangles, 6 285 islands (2 925 under 10 m²), 8.3 km² walkable, the mainland 6.65 km². One settlement, 276 targets: 216 ok, **41 off-mesh** (38 spots, 2 pen gates, 1 ladder end), **19 unreachable** (all spots, on islands of 2 to 52 m²), 2 narrow flights of which 1 has mesh but is not walkable end to end. Most off-mesh spots sit 1.1 to 1.8 m from the mesh, beside props the bake carved out; spot placement is the residents' work, not the mesh's. `NavMeshGraph` is array-and-sort based on purpose: its dictionary-per-edge first version held the editor ~13 minutes at 100 % CPU on 327 k triangles (links cost ~20 s now).
- **Play-mode audit, 2026-10-03, new layout (14 houses, 30 buildings), after the spot calibration below:** 295 targets: 224 ok, 49 off-mesh, 22 unreachable. What is left is layout, not meshing: most of the unreachable are spots inside a walled courtyard or house (B07, B10, B18, B22, B29, B35, the animal pen's cart) whose floor is an island, the rest sit in decor packed closer than two agent radii (the farm plot's beds, the garden terrace's hydroponic walls). Generate lists the same 41 spots as unusable; the planner skips them.
- **Spot calibration.** 41 spots in 14 build prefabs were moved back along their own facing (never more than 2 m) until they stand on NavMesh when the build is alone on a flat floor. It rarely changed which spots are usable (the snap radius already accepted them) but they now stand exactly where residents do. The 58 that no push fixed are the dense ones above.

## Extending

1. **Add walkable geometry:** give it a non-trigger collider (or `Terrain`) on an included layer, no non-kinematic `Rigidbody`, mesh read/write enabled if it is a `MeshCollider`. Put it in a chunk scene listed in the config, or spawn it from a `TerrainFeatureSpawner` with a baked mesh.
2. **Re-bake:** close all chunk scenes → `World/Streaming/Bake World NavMesh` → read the report (source count, features spawned, any `WITHOUT baked meshes`) → commit `Assets/Game/Settings/WorldNavMesh.asset`. For a settlement, right-click it → **Generate + Bake World NavMesh** instead: it saves, closes the chunk scenes, bakes and reopens them in one step ([TerrainGeneration.md](TerrainGeneration.md)).
3. **Verify:** `World/Streaming/Check World NavMesh Is Current` must say up to date, and the console must show `[WorldNavMeshProvider] world NavMesh live (N sources, ..., N auto links)` on play. Then open the settlement's chunk scene and run `World/Streaming/Audit Settlement NavMesh`.
4. **Add a second agent type** (none exists today): create it in `Navigation > Agents`, then (a) set `m_AgentTypeID` on the prefabs' `NavMeshAgent`, (b) bake a *second* `WorldNavMeshAsset` for it — `WorldNavMeshBaker.AssetPath` is a `const` single path, so it must be parameterised first, (c) add a second `WorldNavMeshProvider` instance, (d) extend `WorldNavMeshBuildCheck` to cover it. Until all four are done, an agent with a non-zero type ID has no mesh and `Awake` will disable it silently.
5. **Give another off-chunk structure a NavMesh:** follow `SkyCityNavMeshBaker` — collect the prefab's colliders with `NavMeshSources.IsBakeable`/`TryColliderToSource` at the origin, bake with `WorldNavMesh.asset`'s settings, and put `StaticNavMeshData` on the unscaled prefab root from code.
6. **Change bake tuning:** edit the fields on `WorldNavMesh.asset` in the Inspector (not the project agent settings) and re-bake. Halving `voxelSize` roughly quadruples bake time and asset size.
