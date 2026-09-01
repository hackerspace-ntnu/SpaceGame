---
system: NavMesh
layer: world
summary: One author-time bake of the whole world into a single asset, added at runtime; nothing bakes at runtime
paths:
  - Assets/Game/Scripts/World/Streaming/NavMesh/
  - Assets/Game/Settings/WorldNavMesh.asset
  - Assets/Game/Scripts/agents/AI/Motors/
  - ProjectSettings/NavMeshAreas.asset
symptoms:
  - "an NPC spawns and then stands still forever with a clean console"
  - "creatures path through geometry that is no longer there"
  - "the player build fails with BuildFailedException about the world NavMesh"
  - "agents refuse to cross a gap or take a jump link"
  - "a MeshCollider I added is missing from the bake and nothing errors"
  - "a save-restored creature is on the NavMesh but never moves"
  - "arena spawns are not filtered for reachability"
reads_with: [WorldStreaming, AgentSystem, Locomotion]
updated: 2026-09-01
---

# NavMesh

One NavMesh for the whole streamed world, baked at author time into a single asset and added to the runtime with one `NavMesh.AddNavMeshData` call; nothing bakes at runtime.

**Scope:** `Assets/Game/Scripts/World/Streaming/NavMesh/`, `ProjectSettings/NavMeshAreas.asset`, `Assets/Game/Settings/WorldNavMesh.asset`, `Assets/Game/Scripts/agents/AI/Motors/`
**Related:** [WorldStreaming.md](WorldStreaming.md) · [Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs](Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs) · [.claude/skills/spacegame-agent/SKILL.md](.claude/skills/spacegame-agent/SKILL.md)

## Model

- **One mesh, not per-chunk.** [WorldNavMeshBaker](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshBaker.cs) opens all 48 chunk scenes at once, collects sources, and calls `NavMeshBuilder.BuildNavMeshData` once over their union bounds. Chunk scenes carry no `NavMeshSurface` and no NavMesh data of their own.
- **Runtime is load-only.** [WorldNavMeshProvider](Assets/Game/Scripts/World/Streaming/NavMesh/WorldNavMeshProvider.cs) on the `NavMesh` GameObject in [persistentScene.unity](Assets/Game/Scenes/world/persistentScene.unity) does `AddNavMeshData` in `OnEnable`, `Remove()` in `OnDisable`. No bake, no rebuild, no dirty flag. The `NavMeshSourceCache` / park-and-release system the older revision of this doc described is **deleted**.
- **Collision, not render meshes.** Sources are `Terrain` + non-trigger `Collider`s. Colliders on a non-kinematic `Rigidbody` are skipped (scenery that moves must not be frozen into a permanent mesh).
- **Bake mirrors the runtime.** The baker snaps each chunk's `Terrain` to its grid X/Z (mirroring `WorldStreamer.CacheTerrainForChunk`) and calls `TerrainFeatureSpawner.SpawnBaked()` before collecting, then discards those edits. Skip either and the mesh is silently offset from the ground.
- **Staleness is enforced at build time.** [WorldNavMeshStaleness](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshStaleness.cs) compares each chunk's `AssetDatabase.GetAssetDependencyHash` against the stamp recorded at bake; `WorldNavMeshBuildCheck : IPreprocessBuildWithReport` throws `BuildFailedException` when they differ.
- **Caves are separate surfaces**, not part of the world mesh — see Gotchas.
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
| stamps / sourceCount / bakedAtUtc | 48 chunks / 130 sources / 2026-08-15 23:36:55Z | |

Areas: only Unity's three built-ins, unchanged — `0 Walkable` (cost 1), `1 Not Walkable` (cost 1), `2 Jump` (cost 2). Slots 3–31 are empty. Every source is baked with `area = 0`, no code constructs a `NavMeshQueryFilter`, and every query passes `NavMesh.AllAreas` — **area costs are effectively unused**.

## Key types

| Type | File | Role |
|---|---|---|
| `WorldNavMeshAsset` | [WorldNavMeshAsset.cs](Assets/Game/Scripts/World/Streaming/NavMesh/WorldNavMeshAsset.cs) | ScriptableObject: `NavMeshData` sub-asset + bake settings + per-chunk dependency-hash stamps |
| `WorldNavMeshBakeSettings` | same file | Serialized bake inputs (deliberately not a raw `NavMeshBuildSettings`) |
| `WorldNavMeshProvider` | [WorldNavMeshProvider.cs](Assets/Game/Scripts/World/Streaming/NavMesh/WorldNavMeshProvider.cs) | `AddNavMeshData` on enable; `LogError` (never a silent fallback) if unassigned |
| `WorldNavMeshBaker` | [Editor/WorldNavMeshBaker.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshBaker.cs) | `World/Streaming/Bake World NavMesh` menu item; asset path `Assets/Game/Settings/WorldNavMesh.asset` |
| `WorldNavMeshStaleness` / `WorldNavMeshBuildCheck` | [Editor/WorldNavMeshStaleness.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshStaleness.cs) | `World/Streaming/Check World NavMesh Is Current`; fails the player build when stale |
| `WorldStreamer.SnapAgentsToNavMesh` | [WorldStreamer.cs](Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs) (~L1229) | Re-enables + `Warp`s a loaded chunk's agents onto the mesh |
| `NavMeshAgentMotor` | [NavMeshAgentMotor.cs](Assets/Game/Scripts/agents/AI/Motors/NavMeshAgentMotor.cs) | `IMovementMotor` over `NavMeshAgent`; `[DefaultExecutionOrder(-100)]` |
| `LeggedDriver` | [LeggedDriver.cs](Assets/Game/Scripts/agents/AI/Motors/LeggedDriver.cs) | Path-only consumer: `NavMesh.CalculatePath`, no `NavMeshAgent` |
| `DeferredNavMeshWarp` | [DeferredNavMeshWarp.cs](Assets/Game/Scripts/Core/Persistence/Runtime/DeferredNavMeshWarp.cs) | Retries a save-restore `Warp` for 10 s, sample radius 4 m |
| `CaveSpawner` | [CaveSpawner.cs](Assets/Game/Scripts/World/ProceduralGeneration/Cave/Generation/CaveSpawner.cs) | Own `NavMeshSurface`; `SpawnBaked()` adds a pre-baked `NavMeshData` instance |
| `MatchManager` / `SpawnReachability` | [MatchManager.cs](Assets/Game/Scripts/Gameplay/Minigame/Runtime/MatchManager.cs), [SpawnReachability.cs](Assets/Game/Scripts/Gameplay/Minigame/Core/SpawnReachability.cs) | Snaps arena spawns to the mesh, keeps only the largest mutually-pathable group |

## Flows

**Bake (editor only)**
1. Close every chunk scene — the baker refuses outright if any is open (it mutates scenes and can only safely discard its own).
2. `World/Streaming/Bake World NavMesh`. Config comes from `WorldNavMesh.asset.config`; with two `WorldStreamingConfig` assets present (`WorldStreamingConfig`, `FerdinandWorldStreamingConfig`) it refuses to guess.
3. Opens all 48 chunks additively → aligns terrain → `SpawnBaked()` features → `Physics.SyncTransforms()` → collects → `BuildNavMeshData` → writes the `NavMeshData` sub-asset, stamps, source count.
4. Closes with `removeScene: true`, discarding the scaffolding.

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

Yes — every machine has the identical mesh. `WorldNavMeshProvider` is a plain scene component in `persistentScene`, and the baked data ships in the build, so host and client both `AddNavMeshData` the same bytes locally; nothing about the NavMesh is replicated. Pathing runs wherever the agent simulates: `AgentController`/motor ticks are gated by `NetAuthority`, so the **server** paths NPCs and clients see replicated transforms. `MatchManager` spawn reachability is server-side. A client never disagrees about the mesh, only about who is allowed to drive an agent along it.

## Persistence

The mesh itself is authored data, not save state: [Assets/Game/Settings/WorldNavMesh.asset](Assets/Game/Settings/WorldNavMesh.asset) (~36 MB, binary-serialized, `NavMeshData` stored as a sub-asset named `WorldNavMeshData`). Cave bakes live beside their scene in `CaveBakes/seed_NNNN_NavMesh.asset`. Nothing NavMesh-related is written to a save file. Save/load interacts with it only through `DeferredNavMeshWarp`, which retries a restored agent's `Warp` until the mesh is reachable rather than falling through to a raw transform write (which moves the GameObject but not the agent's internal position — a silently non-moving creature).

## Gotchas

- **No chunk seams to handle.** The mesh is one build over the union bounds; there are no per-chunk tiles to stitch. The corollary is that a chunk edit invalidates the *whole* bake — re-bake all 48, there is no per-chunk path.
- **The bake can be silently wrong.** Nothing at runtime checks freshness; only `World/Streaming/Check World NavMesh Is Current` and the build preprocessor do. In the Editor a stale bake just means NPCs navigate a world that no longer exists.
- **No off-mesh links exist.** Zero `NavMeshLink` / `OffMeshLink` components in any scene or prefab; the baker never sets `GenerateLinks`. The `m_AutoTraverseOffMeshLink` fields on agent prefabs are inert. Agents cannot cross a gap — jumps and leaps are `NavMeshAgentMotor`'s `baseOffset`/arc simulation, not navigation.
- **`persistentScene` still has a legacy `NavMeshSurface`** on the same `NavMesh` GameObject, `m_Enabled: 0` with `m_NavMeshData: {fileID: 0}`. It contributes nothing. Do not enable it; do not treat it as the world surface.
- **`MinigameArena.unity` is an empty scene** (`SceneRoots: []`) — no geometry, no surface, no baked data. `MatchManager.KeepMutuallyReachable` therefore hits its "no NavMesh at all" branch and returns the authored spawn positions unfiltered. The code comments about steep arena terrain splitting the mesh into islands describe an arena that is no longer in the scene.
- **The `Interior` layer is excluded from the world bake**, so cave interiors never merge with the world mesh; each `CaveSpawner` adds its own `NavMeshData` instance and removes it in `ClearPrevious`/disable. A cave without `bakedMesh` + `bakedNavMeshData` assigned generates and bakes live on `Start` — seconds of stall.
- **The layer mask is only defaulted at asset creation** (`LoadOrCreateAsset`). Adding a new layer later does *not* update the existing asset's mask; a new walkable layer above bit 10 is included by accident, a new character layer must be excluded by hand.
- **`MeshCollider` sources need readable meshes** — `TryColliderToSource` silently returns `false` for `isReadable == false`, so the geometry vanishes from the bake with no error. Watch the reported source count (currently 130); a sudden drop means geometry went missing.
- **`NavMeshAgentMotor.Awake` disables its own agent** when `SamplePosition` finds nothing within `navMeshSnapDistance` (6 m), on the promise that `WorldStreamer.SnapAgentsToNavMesh` re-enables it. Nothing else keeps that promise — an agent spawned outside a streamed chunk scene, or in a scene the streamer does not own, stays dead for the session with no error.
- **Bake settings diverge from project settings** (60°/0.8 vs 45°/0.75). A `NavMeshAgent` inspector preview or an editor `NavMeshSurface` bake uses the *project* numbers and will not match what ships.

## Extending

1. **Add walkable geometry:** give it a non-trigger collider (or `Terrain`) on an included layer, no non-kinematic `Rigidbody`, mesh read/write enabled if it is a `MeshCollider`. Put it in a chunk scene listed in the config, or spawn it from a `TerrainFeatureSpawner` with a baked mesh.
2. **Re-bake:** close all chunk scenes → `World/Streaming/Bake World NavMesh` → read the report (source count, features spawned, any `WITHOUT baked meshes`) → commit `Assets/Game/Settings/WorldNavMesh.asset`.
3. **Verify:** `World/Streaming/Check World NavMesh Is Current` must say up to date, and the console must show `[WorldNavMeshProvider] world NavMesh live (N sources, ...)` on play.
4. **Add a second agent type** (none exists today): create it in `Navigation > Agents`, then (a) set `m_AgentTypeID` on the prefabs' `NavMeshAgent`, (b) bake a *second* `WorldNavMeshAsset` for it — `WorldNavMeshBaker.AssetPath` is a `const` single path, so it must be parameterised first, (c) add a second `WorldNavMeshProvider` instance, (d) extend `WorldNavMeshBuildCheck` to cover it. Until all four are done, an agent with a non-zero type ID has no mesh and `Awake` will disable it silently.
5. **Change bake tuning:** edit the fields on `WorldNavMesh.asset` in the Inspector (not the project agent settings) and re-bake. Halving `voxelSize` roughly quadruples bake time and asset size.
