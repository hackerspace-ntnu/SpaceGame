# Minigame Scene — Design

## Goal

Add a new minigame world that is launched from the main menu and reuses the
existing persistent-scene systems (player, entity/NPC AI, NavMesh, managers)
exactly as the main game does, without duplicating any of that code. The
minigame world is small, hand-built, and behaves like any other place in the
game — not a hidden/suspended overlay, not a stripped-down parallel system.

## Background / prior art

The project already has the exact mechanism needed, used for interiors:

- `persistentScene.unity` (`Assets/Scenes/world/persistentScene.unity`) holds
  all long-lived managers: `WorldStreamer`, `SpawnManager`,
  `NetworkGameManager`, `InteriorManager`, `MapService`, `CutsceneDirector`,
  the NavMesh root, etc. It never unloads during a play session.
- The main world is built from many `Chunk_X_Y.unity` scenes
  (`Assets/Scenes/world/Chunks/`) that `WorldStreamer` loads/unloads
  additively around tracked positions (`WorldStreamer.cs`).
- `InteriorManager` loads self-contained interior scenes
  (`Assets/Scenes/Interiors/*.unity`) additively on top of `persistentScene`,
  the same pattern being reused here.
- The player is never `DontDestroyOnLoad` — it's instantiated at runtime by
  `SpawnManager` (`Assets/Scripts/Game/SpawnManager.cs`) into whatever scene
  is active. This works because `persistentScene` itself is the thing that
  never unloads, so the player is scene-independent by construction.
- All play — including "single player" — runs through Netcode for
  GameObjects as a local host (`MainMenuUI.StartSinglePlayer()`,
  `Assets/Scripts/UI/Pages/MainMenuUI.cs:10-14`). There is no offline/local
  spawn path, so the minigame must go through the same host + NGO scene load
  flow.
- Scene loads from the menu are name-based via a `SceneReference`
  ScriptableObject (`Assets/Scripts/SceneManagement/SceneReference.cs`), not
  hardcoded build indices.

## Design

### 1. New scene

Add `Assets/Scenes/Minigames/<Name>.unity`: a single, hand-built scene (not
chunked) containing the minigame's geometry, a baked NavMesh, and at least
one `SpawnPoint` object. `SpawnPoint` objects are discovered at runtime by
`SpawnManager` via `FindObjectsByType<SpawnPoint>` — see §6 for why this
needed real changes, not just "drop a SpawnPoint in and it works."

The scene also gets its own local `NavMeshSurface` (Unity AI Navigation
package), baked once at edit time against just this scene's geometry. The
persistent scene's shared NavMesh is rebuilt incrementally by `WorldStreamer`
per-chunk (`NavMeshSystem.md`) and never scans arbitrary additively-loaded
scenes, so the minigame needs its own self-contained bake to give
`NavMeshAgent`-driven NPCs (e.g. the copied `PatrolRobot`) something to walk
on.

**Seed content:** the terrain objects from 4 specific existing chunks —
`Chunk_6_0`, `Chunk_6_1`, `Chunk_7_0`, `Chunk_7_1`
(`Assets/Scenes/world/Chunks/`) — are copied into the new minigame scene as
static content (not streamed) to give the minigame a starting terrain to
build on. `Chunk_7_1` is the dense one (~18MB, real terrain detail);
`Chunk_7_0` has moderate content; `Chunk_6_0`/`Chunk_6_1` are near-empty
baseline chunks that round out the 2x2 block. Copying is done directly in
the Unity Editor (via Editor MCP tooling) rather than by hand-editing scene
YAML, to preserve prefab links and transforms correctly. Relative positions
between the 4 source chunks are preserved so the copied terrain remains
spatially coherent in the new scene.

### 2. Menu entry point

Add a new method to `MainMenuUI` (`Assets/Scripts/UI/Pages/MainMenuUI.cs`)
alongside `StartSinglePlayer()`, e.g. `StartMinigame()`, wired to a new menu
button. It follows the same shape as the existing method:

```csharp
NetworkManager.Singleton.StartHost();
NetworkManager.Singleton.SceneManager.LoadScene(gameScene.SceneName, LoadSceneMode.Single);
```

except it loads `persistentScene` as today, then additively loads the new
minigame scene on top (via a new `[SerializeField] SceneReference
minigameScene` field), mirroring exactly how `InteriorManager` layers
interior scenes onto `persistentScene`.

### 3. Load order

1. `persistentScene` loads in `Single` mode (as it does today for the main
   game) — this brings in every manager, the player spawn flow, and the
   entity/NPC AI system for free.
2. The new minigame scene loads additively on top, becomes the active scene.
3. `SpawnManager` finds the `SpawnPoint` in the now-active minigame scene and
   spawns the player into it via the normal NGO spawn flow — no changes to
   `SpawnManager` needed.

### 4. Keeping WorldStreamer out of the way

`WorldStreamer` lives in `persistentScene` and is a **shared singleton** used
by both the main game and the minigame flow (since both load `persistentScene`
the same way). Its `WorldStreamingConfig` is always assigned — it can't be
selectively unassigned per-flow without breaking the main world — and because
`MainMenuUI.StartMinigame()` calls `NetworkManager.Singleton.StartHost()` the
same as `StartSinglePlayer()`, `WorldStreamer.OnNetworkSpawn()`
(`WorldStreamer.cs:189-205`) always fires and `isReady` always becomes true.

So `SpawnManager.SpawnWhenReady`'s call to
`WorldStreamer.PreloadChunksAroundPositions` (`WorldStreamer.cs:347`) always
runs for real, computing a chunk coordinate from the spawn position via
`config.WorldToChunkCoord` and attempting to load any matching `Chunk_X_Y`
scene. To make this a guaranteed no-op rather than relying on the target
coordinate happening to be empty, **the minigame scene's content is placed
at world coordinates entirely outside the main world's configured grid
bounds** (`worldOrigin (0,0,-1000)`, `chunkSize 500x500`,
`gridDimensions 8x6` → grid spans X:[0,4000], Z:[-1000,2000] — see
`Assets/Settings/WorldStreamingConfig.asset`). With no chunk coordinate
matching a real file, `PreloadChunksAroundPositions` computes
`chunksToLoad.Count == 0` and returns immediately
(`WorldStreamer.cs:369-374`), and `SpawnManager` proceeds straight to
spawning.

No changes to `WorldStreamer` itself are required — this is purely a matter
of the minigame's world-space placement, not a config/feature toggle.

### 5. Entity/NPC AI reuse

Because `persistentScene` is loaded as-is, the entity AI system
(`IBehaviourModule`, profiles, faction setup — see project memory
`project_entity_system.md`) is available unchanged. NPCs placed directly in
the minigame scene work exactly as they do in any chunk scene today; no
special-casing needed.

### 6. Spawn point race — required code changes

`persistentScene` already contains its own root-level `SpawnPoint` for the
main game. Two problems surfaced when actually wiring this up, both because
`SpawnManager`/`NetworkGameManager` were written assuming exactly one scene
with exactly one spawn point:

- `SpawnManager.GetSpawnPoint()` (`Assets/Scripts/Game/SpawnManager.cs`)
  originally cached `FindObjectsByType<SpawnPoint>` once in `Start()` and
  always returned `spawnPoints[0]` — array order across scenes is not
  deterministic, so it could return persistentScene's spawn point instead of
  the minigame's even after both are loaded. Fixed by re-scanning on every
  call and preferring a `SpawnPoint` in `SceneManager.GetActiveScene()`,
  falling back to any if none match — this is scene-generic, not
  minigame-specific, so it also protects any future additive scene with its
  own spawn point.
- `NetworkGameManager.SpawnWhenReady` (`Assets/Scripts/Multiplayer/NetworkGameManager.cs`)
  auto-spawns the player as soon as `SpawnManager.Instance.SpawnPointsAvailable()`
  is true — which persistentScene's own `SpawnPoint` satisfies immediately,
  well before the minigame scene has even started its additive load. Fixed
  with a static `NetworkGameManager.PendingSceneNameToWaitFor` gate:
  `MainMenuUI.StartMinigame()` sets it to the minigame scene's name *before*
  calling `StartHost()` (so it's in place before `OnNetworkSpawn` fires), and
  `SpawnWhenReady` waits for that named scene to be loaded and calls
  `SceneManager.SetActiveScene` on it before proceeding to the normal
  spawn-point wait. The main game flow never sets this field, so its
  behavior is unchanged.
- Uncovered while testing the fix above: `NetworkGameManager.OnNetworkSpawn`
  both calls `OnClientConnected(OwnerClientId)` directly **and** subscribes
  to `NetworkManager.OnClientConnectedCallback`, which also fires for the
  host's own client locally — so `SpawnWhenReady` ran twice for the same
  client. The first run correctly consumed `PendingSceneNameToWaitFor` and
  waited for the minigame scene; the second run saw it already cleared and
  spawned immediately at persistentScene's own `SpawnPoint`, which is what
  actually explained the player ending up in the wrong place during manual
  testing. Fixed by tracking already-handled client IDs in
  `OnClientConnected` (`handledClients` set) so the second, duplicate
  invocation is a no-op. This was a latent bug in the existing code,
  pre-dating the minigame work — it just had no observable effect before
  because both duplicate runs used to reach the same spawn point anyway.

## Out of scope / explicit non-goals

- No suspension/hiding of the main world — this design assumes the minigame
  is reached from the main menu, not from inside a live game session, so
  there is no "main world" to suspend.
- No portal/trigger-based entry from inside the game world (the existing
  `SceneTransition` system's not-yet-built `FullSceneSwapDestination` /
  `WorldPositionDestination` extension points are not needed for this
  design, since entry is menu-driven).
- No chunk streaming for the minigame world — it is a single scene.
- No changes to the player prefab — it is already scene-independent.

## Open items for implementation planning

- Exact new `SceneReference` field wiring and Inspector assignment for the
  new minigame scene asset.
- New scene needs to be added to Build Settings
  (`ProjectSettings/EditorBuildSettings.asset`), same as other scenes.
- UI button placement/wiring in the MainMenu scene itself (Inspector work,
  not purely code).
