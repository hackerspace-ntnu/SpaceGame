---
system: WorldStreaming
layer: world
summary: Server-authoritative additive loading of chunk scenes around moving anchors, plus scene membership
paths:
  - Assets/Game/Scripts/World/Streaming/
  - Assets/Game/Settings/WorldStreamingConfig.asset
  - Assets/Game/Editor/World/WorldChunkerEditor.cs
  - Assets/Game/Scripts/World/Safety/
  - Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs
symptoms:
  - "a chunk never loads and the player falls through the world"
  - "client join fails with Scene Hash N does not exist in the HashToBuildIndex table"
  - "an NPC vanishes for clients when its old chunk unloads but the host still has it"
  - "streaming stops dead — no chunk ever loads or unloads again after one error"
  - "a NavMesh or map bake silently skips chunks"
  - "chunks never unload, or a caravan drags loaded chunks around with it"
  - "a position 16 km out reads as terrain in the corner of the world"
  - "after the crash-landing intro the player walks and steers but never falls"
  - "loading takes a minute or two with several players when it takes seconds alone"
  - "the loading screen says still waiting on player spawn after 30s while chunks keep loading"
  - "the host is the last player to spawn and misses the crew gather"
reads_with: [TerrainGeneration, Persistence, SceneTransitions, NavMeshSystem]
updated: 2026-09-02
---

# World Streaming

Server-authoritative additive loading of chunk scenes around moving anchors, plus the scene-membership rules that keep runtime entities in the chunk they are standing on.

**Scope:** [Assets/Game/Scripts/World/Streaming/](Assets/Game/Scripts/World/Streaming/), [Assets/Game/Scripts/World/Safety/](Assets/Game/Scripts/World/Safety/), [Assets/Game/Editor/World/WorldChunkerEditor.cs](Assets/Game/Editor/World/WorldChunkerEditor.cs), [WorldSession.cs](Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs), [WorldIdentity.cs](Assets/Game/Scripts/Core/Persistence/Format/WorldIdentity.cs), [WorldStreamingConfig.asset](Assets/Game/Settings/WorldStreamingConfig.asset)

**Related:** [TerrainGeneration.md](TerrainGeneration.md) · [Persistence.md](Persistence.md) · [SceneTransitions.md](SceneTransitions.md) · [NavMeshSystem.md](NavMeshSystem.md)

## Model

- One [WorldStreamer](Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs) lives in the persistent scene, holds one [WorldStreamingConfig](Assets/Game/Scripts/World/Streaming/Core/WorldStreamingConfig.cs), and is the **only** thing that loads/unloads chunk scenes. Server-only (offline = host of one, `InitializeOffline`).
- Chunk geometry is pure maths in [ChunkGrid](Assets/Game/Scripts/World/Streaming/Grid/ChunkGrid.cs) — origin, chunkSize, dimensions. Coordinates are `Vector2Int (x, y)` where `y` maps to world **z**.
- **Anchors** pull chunks in: every connected client's `PlayerObject`, every `RegisterTrackedTransform`, and every [SceneTracked](Assets/Game/Scripts/World/Streaming/Core/SceneTracked.cs) with `keepChunksLoaded`. Each requires a `(2*loadRadius+1)²` box, plus a second box at `PredictAhead(pos, velocity, streamLookaheadSeconds)`.
- "Required" is decided by `TryGetStreamingCoord`, not `Contains`. Outside the grid but within `offWorldDistance` (2000 m) clamps to the nearest edge chunk and keeps it loaded; beyond that (the minigame arena, ~16.5 km east) the anchor holds nothing.
- Unload is grace-timed (`unloadGracePeriod`, 10 s) and blocked while any non-`Despawn` `SceneTracked` sits in the chunk (radius-0 anchor).
- All scene ops go through one sequential queue — NGO permits one scene event at a time. `Update` ticks the queue at 0.5 s (`updateInterval`) and paces it against [ChunkActivationQueue](Assets/Game/Scripts/World/Streaming/Core/ChunkActivationQueue.cs): no new load while a chunk is still building.
- Loaded ≠ built. Terrain features defer their GameObject+MeshCollider construction into `ChunkActivationQueue` under a ms budget (`chunkActivationBudgetMs`, 2 ms). Player spawns wait on `WhenChunkContentBuilt`, not on the scene event — and that wait is gated on the activation queue **alone**, never on the operation queue (see Gotchas).
- A preload waits for every chunk it asked for that is not yet `Loaded`, **including one somebody else is already loading** (`EnqueueLoad` attaches the callback to the in-flight op via `loadListeners`, answered by `FinishLoad` on success or failure). Six players preloading one spawn area all wait for it.
- An anchor can be **suspended** (`SuspendAnchor` / `ResumeAnchor`): it then pulls nothing in until resumed, and resuming drops its velocity sample so it does not read as having flown there. The arrival uses this for the crew in flight (see [PlayerShip](PlayerShip.md)).

## Key types

| Type | File | Role |
| --- | --- | --- |
| `WorldStreamer` | [Core/WorldStreamer.cs](Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs) | NetworkBehaviour; anchors, op queue, terrain cache, migration RPC, `OnChunkLoaded/WillUnload/Unloaded` (static) |
| `WorldStreamingConfig` | [Core/WorldStreamingConfig.cs](Assets/Game/Scripts/World/Streaming/Core/WorldStreamingConfig.cs) | ScriptableObject: grid, tunables, `ConfigId` (asset GUID), `ChunkInfo[]` |
| `ChunkInfo` (struct) | same file | `gridCoord`, `sceneName`, `scenePath`, `worldBounds`, `hasTerrain` |
| `ChunkGrid` (struct) | [Grid/ChunkGrid.cs](Assets/Game/Scripts/World/Streaming/Grid/ChunkGrid.cs) | Pure geometry; `ToCoord` clamps, `TryGetStreamingCoord`/`DistanceOutside`/`PredictAhead`, and `WindowAround` for the chunks a view of a given span centred on a position covers (Gotchas) |
| `SceneTracked` | [Core/SceneTracked.cs](Assets/Game/Scripts/World/Streaming/Core/SceneTracked.cs) | `Pin`/`Migrate`/`Despawn` + `keepChunksLoaded`; also `IPersistentEntity` |
| `ChunkActivationQueue` | [Core/ChunkActivationQueue.cs](Assets/Game/Scripts/World/Streaming/Core/ChunkActivationQueue.cs) | Static budgeted work queue; self-drains via `ChunkActivationRunner` |
| `WorldNavMeshProvider` | [NavMesh/WorldNavMeshProvider.cs](Assets/Game/Scripts/World/Streaming/NavMesh/WorldNavMeshProvider.cs) | Adds the pre-baked [WorldNavMeshAsset](Assets/Game/Scripts/World/Streaming/NavMesh/WorldNavMeshAsset.cs); no runtime bake |
| `UnderTerrainGuard` | [Safety/Core/UnderTerrainGuard.cs](Assets/Game/Scripts/World/Safety/Core/UnderTerrainGuard.cs) | Owner-side failsafe; holds a body still while ground is owed, bounded then recovers |
| `WorldSession` | [Persistence/Runtime/WorldSession.cs](Assets/Game/Scripts/Core/Persistence/Runtime/WorldSession.cs) | Static: `WorldId`, `WorldConfigId`, `IsNew`, staged `SaveDocument` |
| `WorldChunkerEditor` | [Editor/World/WorldChunkerEditor.cs](Assets/Game/Editor/World/WorldChunkerEditor.cs) | `Tools > World Streaming > Chunk World`: slices a master scene into chunk scenes + config + build settings |
| `ChunkStreamingProbe` | [Diagnostics/ChunkStreamingProbe.cs](Assets/Game/Scripts/World/Streaming/Diagnostics/ChunkStreamingProbe.cs) | Synthetic anchor walked across 6 boundaries; writes `chunk-streaming-probe.txt` |

## Worlds

| World | Grid | Scene path pattern | Notes |
| --- | --- | --- | --- |
| Main | 8x6 = 48 chunks, 500x500 m, origin `(0, 0, -1000)`, so 4000x3000 m | `Assets/Game/Scenes/world/Chunks/Chunk_{x}_{y}.unity` | Persistent scene [world/persistentScene.unity](Assets/Game/Scenes/world/persistentScene.unity) (`SceneReference` `GameScene`). Config [WorldStreamingConfig.asset](Assets/Game/Settings/WorldStreamingConfig.asset), `configId 303234da…`. **12 of 48 chunks have `hasTerrain: 0`** — columns x=0 and x=1 (world x 0–1000) are authored-empty padding |
| Ferdinand test | 4x2 = 8 chunks, 500x500 m, origin `(2000, 0, 1000)` | `Assets/Game/Scenes/Tests/FerdinandWorld/Chunks/FerdinandChunk_{x}_{y}.unity` | Persistent scene [Tests/Ferdinand_Test_world.unity](Assets/Game/Scenes/Tests/Ferdinand_Test_world.unity). Config [FerdinandWorldStreamingConfig.asset](Assets/Game/Settings/FerdinandWorldStreamingConfig.asset), `configId 9ac62146…`. All 8 have terrain. Editor-only: no menu path reaches it |

Config vs disk: **48 declared / 48 on disk** (main), **8 / 8** (Ferdinand). All 56 chunk scenes are enabled in [EditorBuildSettings](ProjectSettings/EditorBuildSettings.asset). No count mismatch as of this writing — but see Gotchas for the casing mismatch.

## Flows

**Load a chunk**
1. `UpdateChunkLoading` (0.5 s tick) builds `requiredChunks` from all anchors + lookahead; clears any unload timer on them.
2. `EnqueueLoad` sets state `Loading` and queues a `SceneOperation`.
3. `ProcessNextOperation` waits for `operationInProgress == false` **and** `ChunkActivationQueue.PendingCount == 0`, then `NetworkManager.SceneManager.LoadScene(sceneName, Additive)` (offline: `SceneManager.LoadSceneAsync`).
4. `SceneEventProgressStatus.SceneEventInProgress` → `RetryOperation` re-queues the *same* op after 0.2 s (never advances the queue; the flag is global to NGO).
5. `LoadEventCompleted` matching `pendingSceneName` → state `Loaded`, `CacheTerrainForChunk` (snaps terrain X/Z to `ChunkToWorldPosition`, keeps its baked Y), `RefreshTerrainNeighborsAround`, `SnapAgentsToNavMesh`, then `OnChunkLoaded(coord, scene)` inside a try/catch.
6. Terrain features enqueue their build work; `FlushContentCallbacks` fires preload callbacks only once the activation queue drains → `MarkInitialChunksLoaded` → `OnInitialChunksReady`.

**Unload a chunk**
1. Chunk is `Loaded` and absent from `requiredChunks` → timer set to `Time.time + unloadGracePeriod`; timer expiry enqueues an unload.
2. `ExecuteUnload` raises `OnChunkWillUnload` **before** issuing the unload — last frame anything in the scene can be read ([WorldSaveStore.Dehydrate](Assets/Game/Scripts/Core/Persistence/Runtime/WorldSaveStore.cs)).
3. `UnloadEventCompleted` → drop terrain cache, state `NotLoaded`, refresh neighbours, `OnChunkUnloaded`.

**Entity crosses a chunk boundary**
1. `UpdateSceneMembership` (same 0.5 s tick) walks the static `SceneTracked` registry and computes `ResolveDesiredScene`: `Pin` → the streamer's own persistent scene; `Migrate` → the loaded scene at `WorldToChunkCoord(pos)`, else stay put; `Despawn` → stay put.
2. Non-root objects are skipped — Unity rejects `MoveGameObjectToScene` on a child, and a rider parented to a mount follows it anyway.
3. `MoveTracked` moves the server copy, then announces it: dynamically-spawned NetworkObjects are handled by NGO's `SceneMigrationSynchronization`; **in-scene-placed** ones are explicitly excluded by NGO, so `MigrateObjectRpc(networkObjectId, sceneName)` is sent to non-servers. No NetworkObject at all → one-shot `WarnUnreplicatedOnce`.
4. Clients apply by `NetworkObjectId` + scene **name** (handles are per-process). Unresolvable announcements park in `pendingMigrations` and retry every client `Update`; `ReplayMigrationsTo` replays the session's whole migration set to each late joiner.

## Multiplayer

- Only the server runs streaming. A client's `Update` does exactly one thing: `DrainPendingMigrations`. Chunk state dictionaries are empty on clients forever — never ask `IsChunkLoadedAt` there; ask `IsInsideWorldGrid` (which also honours `hasTerrain`).
- Chunk loads are NGO scene events, so client scenes arrive asynchronously and *after* the server's. `UnderTerrainGuard.IsAwaitingGround` exists for exactly this window.
- NGO matches scenes by a hash of the **build-settings path**, case-sensitively. Folder-casing drift between machines produces `Scene Hash N does not exist in the HashToBuildIndex table` on client join. Chunk scenes must stay in build settings and keep the on-disk casing.
- [NetworkGameManager](Assets/Game/Scripts/Core/Multiplayer/Joining/NetworkGameManager.cs) waits for `IsReady`, calls `PreloadChunksAroundPositions(spawnPositions)`, and only spawns players in the callback; [LoadingScreenUI](Assets/Game/Scripts/Presentation/UI/Pages/LoadingScreenUI.cs) waits on `InitialChunksLoaded`.
- **Every chunk load is a scene event every client must finish before the next can start**, so with N players the cost of a load is the slowest client's, serialised. The count of loads is therefore the lever: anything that pulls chunks in that nobody will stand on (a body two kilometres up) is a join delay for everyone. `SuspendAnchor` exists for exactly that.

## Persistence

Detail lives in [Persistence.md](Persistence.md); the streaming contract is:

- [SaveManager](Assets/Game/Scripts/Core/Persistence/Runtime/SaveManager.cs) is the sole subscriber to the three static chunk events and drives `WorldSaveStore` hydrate/dehydrate from them. A subscriber that throws is caught in `RaiseChunk*` — otherwise `operationInProgress` would stick and streaming would stop dead.
- Records are keyed by entity **identity**, never by scene, precisely because the streamer relocates `SceneTracked` entities between chunk scenes.
- `SceneTracked` is `IPersistentEntity`: "moves between chunks" and "must survive a save" are the same declaration.
- A save records `WorldConfigId`; [WorldIdentity.AcceptsConfig](Assets/Game/Scripts/Core/Persistence/Format/WorldIdentity.cs) refuses to load it into a different world (an empty id is legacy and accepted). `WorldSession.StageNew/StageExisting/Consume` carries the choice across the menu→world scene load.

## Gotchas

- **Casing mismatch (live).** Every `scenePath` in `WorldStreamingConfig.asset` and `WorldChunkerEditor.outputFolder` say `Assets/Game/Scenes/World/Chunks`, but git and disk are lowercase `.../Scenes/world/Chunks` (build settings agree with disk). Runtime is unaffected — `ExecuteLoad` loads by `sceneName` and only logs `scenePath` — but every editor tool that consumes `scenePath` (`WorldNavMeshBaker`, `WorldNavMeshStaleness`, `MapMeshBaker`, `WorldStreamerEditor`) goes through `AssetDatabase`, which is case-sensitive. If a bake silently skips chunks, check this first.
- **Scenes are matched by name, not path.** Two worlds must not share a chunk scene name; chunk deltas in a save are keyed by scene name, which is why cross-world loads are refused outright.
- **A window to DRAW around somebody is not `ToCoord` plus a radius.** That window is symmetric about the *chunk*, so it reaches up to half a chunk further on one side of the position than the other and swaps which side at every boundary. Nothing notices when the window decides what to LOAD; the map hologram, which uses one to decide what to draw around the player, sat visibly off the centre of its own plate and could leave a bald edge inside the view. `ChunkGrid.WindowAround` is the drawing form: every chunk a rectangle of a given span centred on the position touches, min inclusive, max exclusive, unclamped. It never falls short of the requested span — it overhangs instead, by up to a chunk on one side, since the window is still whole chunks. Covered by [ChunkWindowTests](Assets/Game/Tests/EditMode/ChunkWindowTests.cs).
- **`WorldToChunkCoord` clamps.** Anything outside the grid maps to the nearest edge chunk. Call `IsWithinGrid` or `TryGetStreamingCoord` first, or the arena 16.5 km east reads as the world's corner terrain.
- **`hasTerrain: 0` is not "not loaded yet".** Twelve main-world chunks are authored empty; `IsInsideWorldGrid` returns false there on purpose so `UnderTerrainGuard` does not pin a body waiting for ground nobody owes it.
- **A park is a *claim*, not a private edit.** `UnderTerrainGuard.EnterPark` suspends gravity through [`CarriedBody.SuspendGravity`](Assets/Game/Scripts/agents/Modules/Riding/CarriedBody.cs) and `ExitPark` gives it back through `CarriedBody.Release`. It must never write `useGravity` itself again. It did once, and the crash landing is what that cost: `ArrivalDirector` spawns the crew at the top of the descent — 2200 m up, 900 m out, over chunks the streamer has not reached — and seats them **one frame later**. `IsInsideWorldGrid` ignores altitude, so the guard parks a body two kilometres in the sky; `SeatedRider` then captures `useGravity == false` as that player's normal state, and hands it back thirty seconds later when they stand up out of the wreck. Pinned by [ParkedBodyCarryTests](Assets/Game/Editor/Tests/ParkedBodyCarryTests.cs).
- **`IsHeldByOther`, not `IsHeld`, inside the guard.** The guard is a holder itself now, so the unqualified question finds its own park and leaves it standing aside from a body only it is holding — forever.
- **A guard disabled mid-park must give the park back.** `OnDisable` does it. `ArrivalDirector.QuietHull` switches every ship's guard off for the length of a descent, and a leaked claim is worse than the old leaked flag: `CarriedBody.IsHeld` then answers true for that body for the rest of the session, so no later carrier's release is ever the last one. Not unit-testable — Unity raises no `OnDisable` outside play mode.
- **Chunker is hardcoded to the main world.** `outputFolder`, `configOutputPath`, `chunkSize` (500) are `const`/`static readonly` in [WorldChunkerEditor](Assets/Game/Editor/World/WorldChunkerEditor.cs) and deliberately not exposed. It cannot regenerate the Ferdinand world, and running it while that world is open writes the main config.
- **Regeneration drops tunables.** The chunker writes only `chunkSize`, `gridDimensions`, `worldOrigin`, `loadRadius`, `unloadGracePeriod`, `chunks`. `WorldStreamingConfig.asset` currently has **no** serialized `offWorldDistance`/`streamLookaheadSeconds` keys (the Ferdinand config does) — they fall back to the C# field initialisers (2000 / 2).
- **World selection reaches one world only.** `MainMenuUI.worldConfig` is a serialized reference pinned to the main config; `WorldSelectUI` reads it through `menu.WorldConfig`. The Ferdinand world is reachable only by opening its scene in the editor — and the `MapService` in that scene still points at the *main* config.
- **Pre-opened chunk scenes are adopted, not ignored.** `InitializeChunkStates` calls `AdoptLoadedChunk` for anything already open (common when editing chunks additively), which also fires `OnChunkLoaded` so persistence still hydrates them.
- **A preload's callback waits for its own content, not for the world to go quiet.** `FlushContentCallbacks` used to also require `operationQueue` empty and no operation in progress — and in a six-player arrival that queue never emptied: five crew already seated were pulling chunks in around themselves, every load refilled the activation queue, and the host's spawn callback (first in, its four chunks long since built) sat behind twenty of somebody else's loads. Seen as `[LoadingScreen] Still waiting on player spawn after 30s` on a world that was ready, and the host seated last, after the crew-gather timeout. The flush now runs **before** `ProcessNextOperation` in `Update` and is gated on `ChunkActivationQueue.PendingCount == 0` alone.
- **A second preload of the same area used to skip the wait.** `PreloadChunksAroundPositions` only counted `NotLoaded` chunks, so every caller after the first found them `Loading`, was told there was nothing to load, and went looking for ground that was not there yet. Now anything not `Loaded` is waited for, through `loadListeners`.
- **`SceneEventInProgress` is not an error.** NGO's busy flag is global; the retry path is load-bearing. Never "fix" it by dequeuing the next op.
- **Domain-reload-off leaks the activation queue.** `ChunkActivationQueue` clears itself via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`; anything else static in this subsystem must do the same.
- **`SnapAgentsToNavMesh` re-enables agents.** `NavMeshAgentMotor.Awake` disables its own agent when no mesh is under it and relies on this to switch it back on. Nothing else keeps that promise.

## Extending

**Add / regenerate main-world chunks**
1. Open the master world scene; `Tools > World Streaming > Chunk World`; the window auto-detects bounds and grid dimensions.
2. Use *Selective Update* to regenerate only the chunks you touched — unselected chunks keep their existing `ChunkInfo` and their scene files are left alone.
3. Generate: chunk scenes are written to `outputFolder`, `WorldStreamingConfig.asset` is rewritten, and the scenes are added to build settings (`AddScenesToBuildSettings`).
4. Re-bake the NavMesh (`World/Streaming/Bake World NavMesh`) — `WorldNavMeshStaleness` compares per-chunk asset dependency hashes.
5. Verify the new chunk streams on a real client, not just the host.

**Add a new world**
1. Duplicate a `WorldStreamingConfig` asset; `OnValidate` stamps a fresh `configId` from the new asset GUID (never hand-edit it — every existing save for that world is keyed to it).
2. Author chunk scenes under a dedicated folder with a **globally unique** scene-name prefix, and fill `chunks[]` (`gridCoord`, `sceneName`, `scenePath`, `worldBounds`, `hasTerrain`). The chunker cannot do this for you (see Gotchas).
3. Add every chunk scene to build settings, matching on-disk casing exactly.
4. Create the world's persistent scene with a `WorldStreamer` (config assigned), a `WorldNavMeshProvider` + its own baked `WorldNavMeshAsset`, at least one `SpawnPoint`, and the usual networking objects.
5. Set `worldOrigin` so the grids do not overlap, and keep any off-grid scene (arena, interiors) further than `offWorldDistance` from every grid.
6. To make it menu-reachable, the world config must be selectable rather than a fixed serialized field on `MainMenuUI` — that indirection does not exist yet.
