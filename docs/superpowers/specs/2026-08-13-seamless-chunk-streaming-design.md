# Seamless chunk streaming

**Date:** 2026-08-13
**Requirement:** moving between world chunks must be unnoticeable, including at vehicle speed.
**Acceptance:** while a tracker crosses ~6 chunk boundaries at 40 m/s, no frame exceeds **33 ms** and p99 stays under 20 ms.

## The problem

Crossing a chunk boundary freezes the game for seconds. Measured on the dev machine (editor
path — runtime magnitudes differ, the shape does not):

| what | cost |
|---|---|
| whole-world NavMesh rebuild, 9 terrains only, no props (shipped settings) | **5,296 ms** |
| same, 0.333 m voxels instead of 0.167 | 1,743 ms |
| same, one chunk's bounds only | 1,094 ms |
| content chunk scene load, warm (Chunk_7_1 / Chunk_7_5) | 332 / 394 ms |
| mesh collider cook for one content chunk (34 colliders, 360 k tris) | 110 ms |
| terrain-only chunk scene load, warm | 11 ms |

Crossing one boundary queues 3 loads and, after the grace period, 3 unloads — **6 whole-world
NavMesh rebuilds**. At 5 s each they never finish before the next chunk event, so
`navMeshRebuildPending` keeps re-firing and the streamer sits on a permanent bake treadmill.

### Measured baseline

`ChunkStreamingProbe`, walking an anchor 3000 m at 40 m/s across 6 boundaries, before any change:

```
frames=2935  wall=75.0s
median=14.2ms  p95=26.8ms  p99=40.3ms  max=5334ms
>16.7ms=1288  >33ms=41  >100ms=12  >500ms=9  >1000ms=6
VERDICT: FAIL
```

Six frames over a full second; worst 5,334 ms, which matches the 5,296 ms measured whole-world
bake almost exactly. So the bake blocks the main thread despite being issued through
`UpdateNavMeshDataAsync`. One stall is logged as `2658ms after [UNLOAD Chunk_0_4]`, confirming
unloads rebuild too. The smaller 141–783 ms stalls line up with scene loads and collider cooks.

### Four avoidable causes

1. **The whole-world NavMesh is rebuilt from scratch on every load and every unload.**
   `WorldStreamer.RebuildNavMesh` bakes a 10 km x 500 m x 10 km volume at 0.167 m voxels. The
   10 km bounds come from the fallback at `WorldStreamer.cs:1002`, taken because the
   `NavMeshSurface` is `collectObjects = All` with a default 10 m size — i.e. it was never
   configured, so the code substitutes the whole world.

2. **Every terrain feature redoes the entire chunk's work.** Chunk scenes ship with their
   feature meshes *already spawned* as children, with the meshes embedded in the scene file
   (Chunk_7_1: 34 `TerrainFeature_*` roots, 17 MB; Chunk_7_5: 40 MB). At runtime each of the 34
   `TerrainFeatureSpawner.Awake()` calls `ClearSpawned()` then `SpawnBaked()`, destroying all of
   them and recreating them from the baked assets. So every load deserializes and cooks 34 mesh
   colliders, discards them, then cooks 34 more. Each `Awake` also runs
   `FindFirstObjectByType<WorldStreamer>()` and `NotifyChunkGeometryChanged`, so the chunk's
   full-scene source scan restarts 34 times and 33 results are thrown away.

3. **Source collection allocates every mesh's index buffer.** `NavMeshSourceCache.StepJob` calls
   `mesh.triangles.Length` purely to decide whether to yield — a full managed `int[]` copy per
   mesh, per collect. With `useGeometry = RenderMeshes` and `layerMask = Everything` that is
   every MeshFilter in the chunk (164 in Chunk_7_5), 34 times over.

4. **Loads are serialised and unbudgeted.** Netcode permits one scene operation at a time, so a
   crossing plays three hitches back to back, and `Application.backgroundLoadingPriority` is
   never set, so Unity does not time-slice load integration.

### The key observation

There is no `NavMeshObstacle`, no `NavMeshLink`, and no runtime-generated walkable geometry
anywhere in the project. `SettlementSpawner.Generate` is `[ContextMenu]`-only; `CaveSpawner` and
`TerrainFeatureSpawner` both instantiate **pre-baked** mesh assets. The only caller of
`NotifyChunkGeometryChanged` is the feature spawner, spawning baked assets.

So the entire runtime NavMesh-baking subsystem exists to reconstruct, on every boundary
crossing, a NavMesh that never changes. It can be baked once, offline.

## Design

### Unit 1 — `WorldNavMeshBaker` (editor)

Bakes the whole world's NavMesh into a single asset, automatically.

- Menu item under `World/Streaming/`, plus an `IPreprocessBuildWithReport` hook so a build can
  never ship a stale mesh.
- Opens every scene listed in `WorldStreamingConfig.chunks` additively in a temp editor session.
  Iterates `config.chunks` (48), **never** the chunk folder (240 scenes exist on disk; 192 are
  orphans outside the grid).
- Calls `SpawnBaked()` on any `TerrainFeatureSpawner` that has no spawned children, so the bake
  sees the same geometry the runtime will, whether or not the hygiene pass has run.
- Bakes one `NavMeshData` at 0.333 m voxels over the union of the collected geometry's bounds,
  writes `Assets/Game/Settings/WorldNavMesh.asset`, closes the temp scenes without saving.
- Writes a manifest recording each chunk scene's GUID and content hash.

**Collects collision, not render meshes.** The runtime bake used the `NavMeshSurface`'s
`useGeometry = RenderMeshes` with `layerMask = Everything`, which pulled in every renderer —
including Chunk_7_5's 66 skinned NPC bodies. Baking a character into a NavMesh that is then
permanent would carve a hole that never heals, so the baker collects non-trigger colliders on a
layer mask that excludes Player, UI, Hologram, Interior, TransparentFX, Ignore Raycast and Water,
and skips anything attached to a non-kinematic Rigidbody. Navigation now follows what the player
actually collides with.

**Result of the first bake:** 141 sources from 48 chunk scenes in 12.1 s, covering
3002 × 780 × 3002 m. That is smaller than the 4000 × 3000 m grid because the x=0 and x=1
columns — 12 of the 48 chunks, the western third of the map — contain no terrain at all. The bake
covers exactly the terrain that exists.

*Depends on:* `WorldStreamingConfig`, UnityEditor.
*Testable:* output covers the grid bounds; `IsStale()` flips when a chunk scene changes.

### Unit 2 — `WorldNavMeshProvider` (runtime, ~40 lines)

`OnEnable` → `NavMesh.AddNavMeshData(asset)`. `OnDisable` → `Remove()`. That is the whole
runtime NavMesh story.

This **removes** from `WorldStreamer`: `RebuildNavMesh`, `ScheduleNavMeshRebuild`,
`BeginChunkSourceCollection`, `NotifyChunkGeometryChanged`, `ParkAgentsForChunk`, `ParkAgent`,
`ReleaseParkedAgents`, `TryActivateAgent`, and their eight backing fields — plus all of
`NavMeshSourceCache.cs`. Roughly 400 lines deleted.

Agents no longer need parking, because the NavMesh is always present. A one-shot
`NavMesh.SamplePosition` + `Warp` per chunk load stays as insurance for agents authored slightly
off-mesh; it replaces the retry loop, not the other way round.

### Unit 3 — `ChunkActivationQueue` (runtime)

Feature spawning moves out of `Awake` and onto a streamer-driven queue with a per-frame
millisecond budget (default 2 ms). Chunk scenes get `spawnOnAwake = false`; the streamer pulls a
few spawners per frame once the load completes.

*Testable in isolation:* given N tasks and a 2 ms budget, never exceed the budget, always drain.

### Unit 4 — chunk scene hygiene pass (editor)

For each scene in `config.chunks`: `ClearSpawned()` on every `TerrainFeatureSpawner`, set
`spawnOnAwake = false`, save. Chunk_7_1 goes 17 MB → ~250 KB.

**Safety.** This deletes geometry from committed scene files, so it refuses to clear a spawner
unless the baked asset that replaces it resolves and matches. Per spawner: `HasBakedMesh` must be
true, the referenced mesh asset must load, and its vertex count and bounds must match the child
mesh being removed. Any mismatch is reported and that scene is left untouched. The chunk scenes
are committed, so git is the backstop.

Run only if the measurements after units 1–3 show it is still needed.

### Policy and settings

- `Application.backgroundLoadingPriority = ThreadPriority.Low`, set once at streamer init, so
  Unity time-slices load integration instead of doing it in one frame.
- `loadRadius` stays at 1 for now. The design proposed raising it to 2 for more lead time, but the
  baseline shows the problem is unbudgeted work, not insufficient lead time: at 40 m/s a 500 m
  chunk gives 12.5 s between crossings, which is ample once the work is paced. Raising the radius
  would load 25 chunks instead of 9 and add 2 more loads per crossing. Revisit only if the
  measurements show loads failing to keep up.
- `TerrainFeatureMeshSpawn.AttachMesh` drops `EnableMeshCleaning | WeldColocatedVertices` from
  `MeshCollider.cookingOptions` — the baked meshes are already clean — and *Prebake Collision
  Meshes* goes on in Player Settings so builds ship cooked data.
- The scene-operation queue gains one rule: do not start the next operation until the activation
  queue has drained, so integration and activation never land in the same frame.

### Error handling

A missing or stale baked NavMesh is a loud error at startup **and** a failed build — never a
silent fallback to runtime baking, which is the thing being removed. A spawner that throws inside
the activation queue is logged and skipped; the queue continues.

## Verification

`ChunkStreamingProbe` — a runtime diagnostic that registers a synthetic anchor with the streamer,
walks it 40 m/s across ~6 chunk boundaries, and records `unscaledDeltaTime` per frame, attributing
each stall to the scene event in flight. Results go to a file so they can be read back over the
MCP bridge. It installs itself only when an editor menu item has armed it, so it can never run in
a real game.

Sequence, so every step is attributable:

0. Baseline probe on current code.
1. World NavMesh bake + provider; delete the runtime bake subsystem. Measure.
2. Activation queue and settings. Measure.
3. Hygiene pass, if still needed. Measure.

EditMode tests cover `ChunkActivationQueue` budget behaviour and manifest staleness. `ChunkGrid`
maths is already covered by `ChunkStreamingAnchorTests`.

## What the implementation changed about the design

**The bake must be told which world it is for.** The baker originally resolved its config with
`AssetDatabase.FindAssets("t:WorldStreamingConfig")` and took the first result. A second config
(`FerdinandWorldStreamingConfig`, 8 chunks) appeared in the project during this work, and the baker
silently switched to it — producing a staleness report about a completely different world's chunks.
`WorldNavMeshAsset` now carries an explicit `config` reference, written at bake time and preferred
by both the baker and the staleness check. With several configs present and none named, the baker
refuses rather than guesses.

**`loadRadius` was left at 1.** See Policy and settings.

## Verification status

Complete and self-consistent: the code compiles as a unit, no references to the removed members
remain, and `WorldStreamer` is down from 1255 to 1049 lines with `NavMeshSourceCache` (349 lines)
deleted outright.

**Not yet verified end to end.** The acceptance run is blocked, for two reasons that are both about
concurrent work rather than this change:

1. The project does not compile. Other sessions are mid-refactor on Persistence
   (`SaveFileStore.cs:118`) and on the terrain-feature system.
2. The world content the bake consumed is being replaced right now. The entire terrain-feature
   library (`Features/`, `Spline/`, `ArchingCave/`, `BadlandsMaze/`, `Boulders/`) and the
   `TerrainFeatureBakes/*.asset` meshes the first bake collected have been deleted, and
   `Chunk_7_0` / `Chunk_7_1` have been modified.

The staleness guard already reports the bake as stale, which is exactly its purpose. **The bake must
be re-run and the probe re-measured once the tree compiles and the terrain-feature refactor lands.**

## Risks

- **World-wide `NavMeshData` memory at 0.333 m voxels is still unmeasured.** If it is too large, the
  fallbacks are coarser voxels or per-chunk baked assets plus seam links.
- **Two `WorldStreamingConfig` assets now exist** and only one can be the world the shipped game
  streams. Until `WorldNavMesh.asset`'s `config` field is assigned, the baker will refuse to run.
- **12 of the 48 chunks (the x=0 and x=1 columns) contain no terrain at all.** The NavMesh
  therefore covers 3000 x 3000 m of a nominal 4000 x 3000 m grid. That is correct for the world as
  it stands, but it means the western third is unwalkable void, which may be unintended.
- 192 orphan chunk scenes sit outside the grid. Whether to delete them is a separate question.
- The scene folder was renamed `Scenes/World` -> `Scenes/world` by another session. The baker reads
  paths from the config, so it follows automatically, but git sees the whole tree as renamed.
