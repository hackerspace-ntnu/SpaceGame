# NavMesh System

How the streamed world keeps a runtime NavMesh up to date without ever blocking
the main thread.

## The problem

Earlier versions of [WorldStreamer.cs](Assets/Scripts/WorldStreaming/WorldStreamer.cs)
rebuilt the NavMesh whenever a chunk loaded or unloaded. The bake itself was
asynchronous (`NavMeshBuilder.UpdateNavMeshDataAsync`), but every rebuild was
preceded by a synchronous setup that scaled with scene size:

1. `NavMeshBuilder.CollectSources(...)` — walked every collider/MeshFilter
   under the surface and built a fresh `List<NavMeshBuildSource>`. Cost grew
   linearly with loaded terrains plus any procedural meshes (notably a freshly
   generated cave with tens of thousands of triangles).
2. `FindObjectsByType<NavMeshAgent>(...)` — a full-scene object scan, also
   synchronous.
3. `sources.RemoveAll(s => !mesh.isReadable)` — another full pass.

Together this produced a 1–3 second main-thread stall about half a second
after the game started, when the first batch of chunks finished loading and
the debounced rebuild fired.

## The design

We never run a full-scene scan again. Instead, source collection is **per
chunk**, **cached**, and **incremental**.

```
        chunk loaded            chunk unloaded
              │                       │
              ▼                       ▼
       BeginCollect(coord)       Drop(coord)
              │                       │
              ▼                       ▼
  ┌──────────────────────────────────────────┐
  │  NavMeshSourceCache                       │
  │  ──────────────────                       │
  │  per-chunk committed sources              │
  │  + queued/active collection jobs          │
  │  + dirty flag                             │
  └──────────────────────────────────────────┘
              │
              │  Tick() each frame, capped at
              │  navMeshSourceCollectionBudgetMs
              ▼
   committed[coord] = freshly collected list
              │
              ▼
   navMeshDirty && !HasPendingWork
              │
              ▼
   NavMeshBuilder.UpdateNavMeshDataAsync(
        navMeshData, settings, flatSourceList, bounds)
              │
              ▼
   completed → ReleaseParkedAgents()
```

## Components

### [NavMeshSourceCache.cs](Assets/Scripts/WorldStreaming/NavMeshSourceCache.cs)

A standalone helper (not a MonoBehaviour, lives inside `WorldStreamer`).

- `BeginCollect(coord, scene, layerMask, geometry, defaultArea)` — queues a
  collection job. The job snapshots root GameObjects from the scene immediately
  (so later destruction can't corrupt iteration) and then runs in slices.
- `Tick()` — called every frame by the streamer. Advances the active job
  until either it finishes or the `perFrameBudgetMs` budget is exhausted.
  Default budget is 1 ms, configurable on the inspector.
- `Drop(coord)` — removes a chunk's sources and any queued/in-flight work
  for that coord.
- `ConsumeDirty()` — single-shot dirty flag the streamer reads before kicking
  a new bake.
- `BuildFlatSourceList(output)` — appends the union of all committed sources
  into a reusable buffer.

Sources are derived from:

- **Terrains** — one source per terrain (cheap, done in one slice).
- **Physics colliders** — for surfaces configured with
  `NavMeshCollectGeometry.PhysicsColliders`. Box/sphere/capsule/mesh/terrain
  colliders are converted directly into `NavMeshBuildSource` so the engine
  doesn't have to re-derive them.
- **MeshFilters** — for `RenderMeshes` geometry. A mesh whose triangle count
  exceeds the per-slice batch size (default 4096) is allowed to push past
  the budget on its own — but yields immediately after, so the worst-case
  per-frame cost is one large mesh per frame.

### [WorldStreamer.cs](Assets/Scripts/WorldStreaming/WorldStreamer.cs)

Owns one `NavMeshSourceCache`. Hooks:

- `OnOfflineSceneLoaded` / `HandleSceneEvent (LoadEventCompleted)` →
  `BeginChunkSourceCollection(coord)` followed by `ScheduleNavMeshRebuild()`.
- `OnOfflineSceneUnloaded` / `HandleSceneEvent (UnloadEventCompleted)` →
  `sourceCache.Drop(coord)` followed by `ScheduleNavMeshRebuild()`.
- `Update()` calls `sourceCache.Tick()` every frame, then checks
  `navMeshDirty && !sourceCache.HasPendingWork && sourceCache.ConsumeDirty()
  && Time.time >= navMeshRebuildTime` before calling `RebuildNavMesh()`.
- `RebuildNavMesh()` now just calls
  `sourceCache.BuildFlatSourceList(buffer)` and hands the buffer to
  `NavMeshBuilder.UpdateNavMeshDataAsync`. No scene walk, no `FindObjectsByType`,
  no `RemoveAll`.

### Agent parking

When a chunk is about to be swapped in or out, agents inside that chunk are
disabled and their target positions cached
([WorldStreamer.cs `ParkAgentsForChunk`](Assets/Scripts/WorldStreaming/WorldStreamer.cs)).
After the async bake completes, `ReleaseParkedAgents()` re-enables them and
`NavMesh.SamplePosition`s them onto the new surface.

We **removed** the previous global `FindObjectsByType<NavMeshAgent>` scan. The
NavMesh swap inside `NavMeshBuilder.UpdateNavMeshDataAsync` is atomic at the
engine level — agents not in a streamed chunk don't need to be parked, and the
periodic `ReleaseParkedAgents` tick in `Update()` cleans up any agent that
ends up off-mesh.

## Settings

Inspector fields on the WorldStreamer GameObject:

| Field | Default | Meaning |
|---|---|---|
| `navMeshRebuildDelay` | 0.5s | Debounce after the last chunk load/unload. Coalesces a flurry of chunk events into a single bake. |
| `parkedAgentActivationDistance` | 32m | Max distance from the parked position when re-sampling agents onto the new surface. |
| `navMeshSourceCollectionBudgetMs` | 1.0 ms | Per-frame time budget for `NavMeshSourceCache.Tick()`. Raise it for faster source readiness, lower it if you start to see frame-time spikes. |

Inside `NavMeshSourceCache`:

| Constant | Default | Meaning |
|---|---|---|
| `triangleBatchSize` | 4096 | Colliders/meshes processed before re-checking the budget. A single mesh larger than this still goes through in one slice — `Tick` then yields. |

## Performance characteristics

- **Worst case per frame**: 1 ms collection budget + the async bake's own
  per-frame cost. The async bake runs on Unity's background NavMesh thread,
  so its main-thread contribution is bounded.
- **First-frame cost on game start**: bounded to the collection budget. With
  ~5 starting chunks and ~10 sources each, collection finishes in 1–3
  frames. The first NavMesh bake fires once the cache reports clean and the
  debounce window elapses.
- **Per-chunk load**: synchronous part is just the `GetComponentsInChildren`
  pass that snapshots roots; collection of actual sources is sliced.
- **Per-chunk unload**: O(1) — drops a dictionary entry.

## Cave generation (separate freeze surface)

Cave interiors (e.g. `SandstoneCaveInterior.unity`) generate procedural meshes
through [CaveSpawner.cs](Assets/Scripts/ProceduralGeneration/Cave/CaveSpawner.cs).
That generation is still synchronous on `Start()` and is unrelated to the
streamed world's NavMesh.

The cave already supports a **baked path**: assign `bakedMesh` and
`bakedNavMeshData` on the inspector and the runtime just instantiates them
(`SpawnBaked()`). Pre-baked assets currently exist in
[Assets/Scenes/Interiors/CaveBakes/](Assets/Scenes/Interiors/CaveBakes/).

If a runtime cave is loaded by `WorldStreamer` (e.g. baked into a chunk
scene), its mesh enters the source cache through the normal per-chunk path
and contributes to the shared NavMesh without any extra plumbing.

If the cave is loaded by `InteriorManager` as a player transitions into an
interior, the 1–2 second generation cost is expected and falls inside the
allowed "load before going into a new scene" window.

## Adding new NavMesh-relevant geometry

Three rules:

1. Live in a streamed chunk scene (or the persistent scene; persistent
   sources need a one-shot `BeginCollect` call — currently the streamer only
   does this for chunk scenes).
2. Use a `MeshFilter`/`Collider` on the layer mask configured on the
   `NavMeshSurface`.
3. Make any non-collider meshes **read/write enabled** (the importer sets
   this; see `MeshReadablePostprocessor`).

The cache picks the new geometry up automatically on the next chunk load.
There is no manual rebuild call.

## Failure modes & diagnostics

- **NavMesh missing under fresh chunk**: open the console — the rebuild log
  line `"[WorldStreamer] NavMesh async rebuild started (N sources, ...)"`
  shows the source count. If N is unexpectedly low, the chunk's geometry is
  likely on the wrong layer or its meshes aren't readable.
- **Frame-time spike during loading**: open the Profiler and look at
  `NavMeshSourceCache.Tick()`. If a single mesh is dominating, raise
  `triangleBatchSize` to release the per-mesh slice earlier, or lower
  `navMeshSourceCollectionBudgetMs` to spread the work more aggressively.
- **Agents stuck after a rebuild**: `ReleaseParkedAgents()` retries every
  500 ms in `Update()`. If an agent never reattaches, check that
  `parkedAgentActivationDistance` is wide enough to cover the displacement
  between its parked position and the new NavMesh.
