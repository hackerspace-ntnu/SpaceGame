---
system: EditorTooling
layer: pipeline
summary: Every custom editor window, menu command, wiring pass and importer hook in the project
paths:
  - Assets/Game/Editor/
  - Assets/Game/Scripts/Core/Persistence/Editor/
  - Assets/Game/Scripts/World/Streaming/NavMesh/Editor/
  - Assets/Game/Editor/Support/SerializedFields.cs
symptoms:
  - "a collider a tool added is on the prefab but nothing ever hits it"
  - "a tool's collider query says everything is already in the right place and nothing moves"
  - "a tool logs success but nothing actually changed on disk"
  - "which menu item wires this prefab, item, creature or vehicle"
  - "a renamed [SerializeField] and the wiring pass quietly stopped setting it"
  - "scenes are full of missing prefab instances a GUID grep cannot find"
  - "a freshly built prefab works in the editor but not on clients (GlobalObjectIdHash 0)"
  - "a build stops after [NetworkPrefabRegistrar] N added and the MCP call disconnects"
  - "the menu item a doc names is not in the Tools menu"
reads_with: [Multiplayer, Persistence, Artifacts, TerrainGeneration]
updated: 2026-09-24
---
# Editor Tooling

Every custom Unity Editor window, menu command, wiring pass and importer hook in the project.

**Scope:** [Assets/Game/Editor/](Assets/Game/Editor) (all except `Editor/Tests/`), plus `Editor/` folders nested under [Assets/Game/Scripts/](Assets/Game/Scripts) (Persistence, World Streaming, Rover, Backpack Placement).
**Related:** [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md) · [Artifacts.md](Artifacts.md) · [TerrainGeneration.md](TerrainGeneration.md) · [NavMeshSystem.md](NavMeshSystem.md)

> **Prefabs are authored, not generated.** Until 2026-09-23 most content prefabs were rebuilt
> wholesale by a `*Builder.cs` from its FBX, and the rule was "change the builder, never the
> prefab". Those builders were deleted along with the model-generator scripts. **The prefab on
> disk is now the source of truth and is edited in the Inspector.** What remains here are
> *wiring* and *audit* passes: additive edits that own a few named fields, plus the windows,
> importers and bakers.

## Model

- **Content prefabs are hand-authored assets.** Nothing regenerates them; a hand edit is safe.
- Three menu roots, inconsistently: `Tools/…` (most), `SpaceGame/…` (terrain, environment, Look Lab), `World/…` (streaming).
- Wiring passes write private `[SerializeField]`s through [SerializedFields](Assets/Game/Editor/Support/SerializedFields.cs), which **warns** on an unresolved field name instead of silently no-op'ing.
- Only a few hooks run automatically on import; everything else is an explicit menu command by design.
- `Audit`/`Report`/`Validate`/`Diagnose`/`Preview` commands are read-only; `Build`/`Wire`/`Apply`/`Fix`/`Bake`/`Setup` write assets.

## Menu items

| Menu path | File | What it does |
| --- | --- | --- |
| SpaceGame/Environment/Install Volumetric Render Features | [VolumetricSetup.cs](Assets/Game/Editor/Environment/VolumetricSetup.cs) | Adds fog/cloud render features to the URP renderer asset (idempotent) |
| SpaceGame/Environment/Install Pastel Quantize Filter | [PastelQuantizeSetup.cs](Assets/Game/Editor/Environment/PastelQuantizeSetup.cs) | Installs the colour-grade render feature, **inactive**; the checkmarked twin toggles it |
| SpaceGame/Look Lab | [LookLab](docs/AI/systems/LookLab.md) | Window: retunes the palette live in play mode, in memory only |
| SpaceGame/Terrain/Apply Selected Material to All Terrains in Scene | [Assets/Game/Editor/Terrain/](Assets/Game/Editor/Terrain) | Bulk terrain material assignment |
| Tools/Icon Generator · Generate All Item Icons · Generate Icon For Selected Item | [IconGenerator.cs](Assets/Game/Editor/AssetPipeline/IconGenerator.cs) · [BatchIconGenerator.cs](Assets/Game/Editor/AssetPipeline/BatchIconGenerator.cs) | Renders inventory icons from item prefabs |
| Tools/Save System/Wire Saveable Prefabs | [SaveableWiring.cs](Assets/Game/Scripts/Core/Persistence/Editor/SaveableWiring.cs) | Adds `SaveableEntity` to prefabs that hold state |
| Tools/Save System/Wire Saveable {Scene Objects, Chunk Scenes} | [SaveableWiring.cs](Assets/Game/Scripts/Core/Persistence/Editor/SaveableWiring.cs) | Same for the open scenes, or for every streamed chunk scene |
| Tools/Save System/Validate Save Wiring | [SaveWiringValidator.cs](Assets/Game/Scripts/Core/Persistence/Editor/SaveWiringValidator.cs) | Reports wiring that would fail silently (missing ids, duplicate ids, unregistered prefabs) |
| Tools/Save System/Report Unsaved State | [SaveCoverageReport.cs](Assets/Game/Scripts/Core/Persistence/Editor/SaveCoverageReport.cs) | Heuristic: mutable component state covered by no saver |
| Tools/Save System/Drop Fallen Item Records | [Assets/Game/Scripts/Core/Persistence/Editor/](Assets/Game/Scripts/Core/Persistence/Editor) | Clears world records for items that fell out of the world |
| Tools/SpaceGame/Agents/{Build, Verify} Drifter NPCs · Update Drifter Behaviour · Place Drifter Band | [SculptCharacterBuilder.cs](Assets/Game/Editor/Agents/SculptCharacterBuilder.cs) | The five sculpt-base drifters (human, alien, crumpy, Gary, Raxy). *Build* makes only a drifter with no prefab yet — the existing ones carry hand edits and are never rebuilt; *Update Drifter Behaviour* re-applies the agent/netcode/save stack, dialogue and temperament to every existing prefab in place; *Verify* also fails if Drifters are Hostile to any core faction; *Place Drifter Band* puts them in the world |
| Tools/SpaceGame/Agents/Wire Ground Conform | [Assets/Game/Editor/Agents/](Assets/Game/Editor/Agents) | Adds ground-conform to agent prefabs |
| Tools/SpaceGame/Art/Build Stylized Eye Materials | [StylizedEyeBuilder.cs](Assets/Game/Editor/Agents/StylizedEyeBuilder.cs) | The shared eye materials the characters use |
| Tools/SpaceGame/Export Library Site Data | [LibraryExporter.cs](Assets/Game/Editor/AssetPipeline/LibraryExporter.cs) | One preview render per item/creature/vehicle + `library.json` into `docs/library/`; `tools/build_library_site.py` joins it to the hand-written blurbs |
| Tools/SpaceGame/Cleanup/{Report, Remove} Missing Prefab Instances | [MissingPrefabInstanceCleaner.cs](Assets/Game/Editor/Multiplayer/MissingPrefabInstanceCleaner.cs) | Lists scene objects whose source prefab is deleted; Remove deletes them across every scene and saves |
| Tools/SpaceGame/Items/Build Expedition Rig Prefab | [ExpeditionRigWiring.cs](Assets/Game/Editor/Backpack/ExpeditionRigWiring.cs) | Rebuilds the backpack rig + 5 holder prefabs from FBX; edits the player prefab |
| Tools/SpaceGame/Items/{Create Pack Shape Library, Reseed Undrawn Pack Shapes} | [PackShapeLibraryTool.cs](Assets/Game/Editor/Backpack/PackShapeLibraryTool.cs) | Creates/tops up `PackShapes.asset` and wires it onto every `BackpackObject`; Reseed re-derives only masks nobody drew |
| Tools/SpaceGame/Items/{Fix, Audit} Artifact Pack Orientation · Audit Pack Orientation (whole roster) | [ItemPackOrientation.cs](Assets/Game/Editor/Backpack/ItemPackOrientation.cs) | Rewrites (or reports) pack-lay rotations on artifact prefabs |
| Tools/SpaceGame/Items/{Fix, Audit} World Item Bodies | [ItemWorldPresence.cs](Assets/Game/Editor/Items/ItemWorldPresence.cs) | Rigidbody/collider setup on dropped-item prefabs |
| Tools/SpaceGame/Items/Audit Held Item Poses | [HeldItemPoseAudit.cs](Assets/Game/Editor/Items/HeldItemPoseAudit.cs) | Measures `palmDist` / `gripNorm` per item in the real hand |
| Tools/SpaceGame/Items/Preview Worn Gear | [Assets/Game/Editor/Items/](Assets/Game/Editor/Items) | Renders worn gear on the character in edit mode |
| Tools/SpaceGame/Items/Reseat Flamethrower | [FlamethrowerReseat.cs](Assets/Game/Editor/Items/FlamethrowerReseat.cs) | Re-seats the flamethrower in the hand |
| Tools/SpaceGame/Menus/Setup {Front Menu, World Select, Lobby Preview} | [FrontMenuSetup.cs](Assets/Game/Editor/Menus/FrontMenuSetup.cs) · [WorldSelectSetup.cs](Assets/Game/Editor/Menus/WorldSelectSetup.cs) · [LobbyPreviewSetup.cs](Assets/Game/Editor/Menus/LobbyPreviewSetup.cs) | Rebuild the main-menu UI, the world-select panel and `Resources/LobbyPreviewAstronaut.prefab` |
| Tools/SpaceGame/Multiplayer/Sync Network Prefabs | [NetworkPrefabRegistrar.cs](Assets/Game/Editor/Multiplayer/NetworkPrefabRegistrar.cs) | Adds every prefab with a root `NetworkObject` to the list `NetworkManager.prefab` references |
| Tools/SpaceGame/Multiplayer/Wire Agent Netcode | [Assets/Game/Editor/Multiplayer/](Assets/Game/Editor/Multiplayer) | Adds the netcode stack across agent prefabs |
| Tools/SpaceGame/Player/Build Upper Body Layer · Build Glide Layer | [PlayerUpperBodySetup.cs](Assets/Game/Editor/PlayerUpperBodySetup.cs) · [PlayerGlideLayerSetup.cs](Assets/Game/Editor/PlayerGlideLayerSetup.cs) | Rebuilds the masked layers inside `AstronautArmature.controller`: `Upper Body` (hold poses, mirrored twins, gauntlet raises), `Worn Left` (the left arm alone) and the glide layer. Idempotent |
| Tools/SpaceGame/Ragdoll/Wire Prefabs | [RagdollWiring.cs](Assets/Game/Editor/AssetPipeline/RagdollWiring.cs) | Adds `AgentRagdoll`/`PlayerRagdoll` across creature + player prefabs |
| Tools/SpaceGame/Ragdoll/{Report Candidates, Audit Skeletons, Diagnose Wired Prefabs} | [RagdollWiring.cs](Assets/Game/Editor/AssetPipeline/RagdollWiring.cs) | Read-only: felling candidates, skinned vs rigid-part rigs, broken joints on wired prefabs |
| Tools/Tests/Run EditMode Tests (headless) | [HeadlessTestRunner.cs](Assets/Game/Editor/Tests/HeadlessTestRunner.cs) | Runs the EditMode suite, writes a result file |
| Tools/Tests/{Build Multiplayer Test Player, Print Multiplayer Test Commands} | [MultiplayerTestPlayerBuilder.cs](Assets/Game/Editor/Tests/MultiplayerTestPlayerBuilder.cs) | Builds the standalone player for two-machine tests; logs the host/client CLI invocations |
| Tools/World/Bake Sandstorm Noise | [SandstormNoiseGenerator.cs](Assets/Game/Editor/Weather/SandstormNoiseGenerator.cs) | Writes `Textures/Environment/SandstormNoise.asset` |
| Tools/World Streaming/Chunk World | [WorldChunkerEditor.cs](Assets/Game/Editor/World/WorldChunkerEditor.cs) | Window: splits a master scene into 500×500 m chunk scenes + TerrainData, rewrites `WorldStreamingConfig.asset` |
| Tools/World Streaming/Bake Map Meshes | [MapMeshBaker.cs](Assets/Game/Editor/Map/MapMeshBaker.cs) | Window: one low-poly mesh per chunk into `Resources/MapMeshes` |
| World/Streaming/Bake World NavMesh | [WorldNavMeshBaker.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshBaker.cs) | Bakes all chunk collision into one NavMesh asset (edit mode only) |
| World/Streaming/Check World NavMesh Is Current | [WorldNavMeshStaleness.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshStaleness.cs) | Compares per-chunk dependency hashes against bake-time hashes |
| World/Streaming/Run Chunk Traversal Probe | [ChunkStreamingProbeMenu.cs](Assets/Game/Scripts/World/Streaming/Diagnostics/Editor/ChunkStreamingProbeMenu.cs) | Deletes the old report, arms a streaming probe run |

## What writes what

| Script | Writes |
| --- | --- |
| [ExpeditionRigWiring](Assets/Game/Editor/Backpack/ExpeditionRigWiring.cs) | Backpack rig + 5 holder prefabs; also edits `PlayerCharacter.prefab` |
| [SculptCharacterBuilder](Assets/Game/Editor/Agents/SculptCharacterBuilder.cs) · [StylizedEyeBuilder](Assets/Game/Editor/Agents/StylizedEyeBuilder.cs) | The four drifter prefabs and the shared eye materials |
| [PackShapeLibraryTool](Assets/Game/Editor/Backpack/PackShapeLibraryTool.cs) | `ScriptableObjects/Items/PackShapes.asset` — `Reseed` **preserves** hand-drawn masks |
| [SandstormNoiseGenerator](Assets/Game/Editor/Weather/SandstormNoiseGenerator.cs) | `SandstormNoise.asset` |
| [FrontMenuSetup](Assets/Game/Editor/Menus/FrontMenuSetup.cs) · [WorldSelectSetup](Assets/Game/Editor/Menus/WorldSelectSetup.cs) · [LobbyPreviewSetup](Assets/Game/Editor/Menus/LobbyPreviewSetup.cs) | UI subtrees in `Scenes/Core/MainMenu.unity`, `Resources/LobbyPreviewAstronaut.prefab` |
| [WorldChunkerEditor](Assets/Game/Editor/World/WorldChunkerEditor.cs) · [MapMeshBaker](Assets/Game/Editor/Map/MapMeshBaker.cs) | `Scenes/World/Chunks/*`, `Terrain/ChunkData/*`, `Settings/WorldStreamingConfig.asset`, `Resources/MapMeshes/*` |
| [DoubleSidedMaterials](Assets/Game/Editor/Support/DoubleSidedMaterials.cs) (library, no menu) | `Art/Materials/{Vehicles,Settlement}/* (DoubleSided).mat` |
| [SaveableWiring](Assets/Game/Scripts/Core/Persistence/Editor/SaveableWiring.cs) · [RagdollWiring](Assets/Game/Editor/AssetPipeline/RagdollWiring.cs) · [ItemPackOrientation](Assets/Game/Editor/Backpack/ItemPackOrientation.cs) · [ItemWorldPresence](Assets/Game/Editor/Items/ItemWorldPresence.cs) | Additive `LoadPrefabContents` edits — safe to hand-edit except the fields they own |
| [LibraryExporter](Assets/Game/Editor/AssetPipeline/LibraryExporter.cs) | `docs/library/` PNGs + `library.json` — outside `Assets/`, deliberately |

## Importers & postprocessors

| Type | File | Applies to |
| --- | --- | --- |
| `AssetPostprocessor.OnPreprocessModel` | [MeshReadablePostprocessor.cs](Assets/Game/Editor/AssetPipeline/MeshReadablePostprocessor.cs) | Every imported model — forces `isReadable = true` so runtime NavMesh/collider code sees real geometry |
| `AssetPostprocessor.OnPostprocessAnimation` | [RootMotionCurveStripper.cs](Assets/Game/Editor/AssetPipeline/RootMotionCurveStripper.cs) | Every imported clip — deletes empty-path `m_Local*` curves that would teleport the object to the origin (hits rigid-part rigs, not skinned ones) |
| `AssetPostprocessor.OnPostprocessAllAssets` | [ItemFootprintCacheInvalidator.cs](Assets/Game/Scripts/Items/Backpack/Placement/Editor/ItemFootprintCacheInvalidator.cs) | Any import/move/delete — clears `ItemFootprint`'s size cache unconditionally |
| `[InitializeOnLoad]` | [PlayModeTransportTeardown.cs](Assets/Game/Editor/Multiplayer/PlayModeTransportTeardown.cs) | Shuts the NGO session down at `ExitingPlayMode` to limit UDP socket leaks |
| `[InitializeOnLoadMethod]` | [HeadlessTestRunner.cs](Assets/Game/Editor/Tests/HeadlessTestRunner.cs) | Resumes a `SessionState`-pending test run across domain reloads |

Custom inspectors: [TerrainGenManagerEditor](Assets/Game/Editor/Terrain/TerrainGenManagerEditor.cs) (Bake All / Regenerate / Clear), [TerrainFeatureSpawnerEditor](Assets/Game/Editor/Terrain/TerrainFeatureSpawnerEditor.cs) + [TerrainFeatureHandles](Assets/Game/Editor/Terrain/TerrainFeatureHandles.cs) (scene-view footprint handles, live preview, Bake & Save Mesh via [TerrainFeatureBakeUtility](Assets/Game/Editor/Terrain/TerrainFeatureBakeUtility.cs)), [CaveSpawnerEditor](Assets/Game/Editor/Terrain/CaveSpawnerEditor.cs) (Bake & Save NavMesh), [WorldStreamerEditor](Assets/Game/Scripts/World/Streaming/Editor/WorldStreamerEditor.cs), [PackShapeLibraryEditor](Assets/Game/Scripts/Items/Backpack/Placement/Editor/PackShapeLibraryEditor.cs) (paintable mask grid), [BehaviourModuleEditor](Assets/Game/Editor/Agents/BehaviourModuleEditor.cs), [RoverBogieIKEditor](Assets/Game/Scripts/Vehicles/Rover/Editor/RoverBogieIKEditor.cs).

## Flows

Change an artifact end to end:

1. Edit the Blender source, export the FBX to `Assets/Game/Art/Models/…` ([ArtPipeline](ArtPipeline.md)); the postprocessors run on import.
2. Edit the **prefab** in the Inspector — assign the new mesh, fix submesh materials, re-seat the grip.
3. `Items ▸ Audit Held Item Poses` and `Items ▸ Audit Artifact Pack Orientation`; `Tools ▸ Generate Icon For Selected Item`.
4. `Save System ▸ Wire Saveable Prefabs` → `Validate Save Wiring`; `Multiplayer ▸ Sync Network Prefabs`, read the report.

Regenerate the streamed world: `Tools ▸ World Streaming ▸ Chunk World` → `World ▸ Streaming ▸ Bake World NavMesh` → `Tools ▸ World Streaming ▸ Bake Map Meshes` → `Tools ▸ Save System ▸ Wire Saveable Chunk Scenes`.

## Multiplayer

- [NetworkPrefabRegistrar](Assets/Game/Editor/Multiplayer/NetworkPrefabRegistrar.cs) is the sweep: it adds every prefab with a root `NetworkObject` to the list referenced by `Prefabs/Systems/NetworkManager.prefab`, choosing the **largest** existing `NetworkPrefabsList` because several near-duplicates survive from a restructure.
- The list NetworkManager actually reads is `ScriptableObjects/Networking/DefaultNetworkPrefabs.asset`. `Assets/DefaultNetworkPrefabs.asset` at the project root is Netcode's own regenerated file and is **not** consulted.
- A script-added `NetworkObject` ships `GlobalObjectIdHash 0` and NGO silently keeps one prefab per hash, so a tool that adds one must `ImportAsset(ForceUpdate)` then `ForceReserializeAssets`. Missing registration fails on **clients only**.

## Persistence

- [SaveableWiring](Assets/Game/Scripts/Core/Persistence/Editor/SaveableWiring.cs) stamps `SaveableEntity` (identity ids) onto prefabs, open scenes and every chunk scene.
- [SaveWiringValidator](Assets/Game/Scripts/Core/Persistence/Editor/SaveWiringValidator.cs) catches duplicate instance ids, unsaved scene-placed objects and restored prefabs missing from the network list — none of which any compiler or unit test can see.
- [SaveCoverageReport](Assets/Game/Scripts/Core/Persistence/Editor/SaveCoverageReport.cs) heuristically lists mutable state with no saver — a starting point, not a defect list.

## Gotchas

- **The prefabs are the only copy now.** There is no builder to re-run, so a prefab overwritten or broken by hand cannot be regenerated — recover it from git.
- **Never wipe an animator controller's sub-assets by hand; empty it through the API.** Two ways of "starting over" on a controller a prefab references have each produced a controller with no states and a clean console: `DeleteAsset` + `CreateAnimatorControllerAtPath`, and keeping the asset but `DestroyImmediate`-ing every sub-asset and resetting `layers`. The second is the nasty one: the write saves and reads back correctly, and *later in the session* the in-memory controller turns up **empty and dirty**, so the next `AssetDatabase` save writes that empty object over the good file. Clear through `RemoveState` / `RemoveAnyStateTransition` / `RemoveParameter`, keep the layer and its state machine, then force-reimport and count the states on disk.
- **`*Menu` entry points end in a modal dialog, and a modal parks the editor until a human clicks.** `NetworkPrefabRegistrar.SyncMenu` and `WorldNavMeshBaker.BakeMenu` both finish with `EditorUtility.DisplayDialog`; anything calling one from code blocks every tool, test and MCP call behind an OK button. Call the worker — `NetworkPrefabRegistrar.Sync(out _, out _)`, `WorldNavMeshBaker.Bake(config)` — and log its report.
- **AssetDatabase goes read-only in some sessions and discards prefab/asset saves without raising anything.** A tool can report success having written nothing. Re-read everything off disk and assert. **The same shape of silence hides a stale physics pose:** a tool that queries colliders it created or moved in the same call reads where they were BEFORE it touched them, because nothing re-syncs transforms inside one editor call. Call `Physics.SyncTransforms()` immediately before any `ClosestPoint`/`Raycast`/`Overlap*` on geometry the tool itself just placed.
- **A renamed `[SerializeField]` breaks a wiring pass silently** unless it goes through [SerializedFields](Assets/Game/Editor/Support/SerializedFields.cs), which warns.
- Materials that are sub-assets of an FBX are regenerated on reimport, so flags written onto them revert; that is why [DoubleSidedMaterials](Assets/Game/Editor/Support/DoubleSidedMaterials.cs) writes standalone copies beside the prefabs.
- Hardcoded paths everywhere: `WorldChunkerEditor`'s chunk size (500×500), output folders and config path are deliberately not exposed — changing them orphans every existing chunk. `WorldNavMeshBaker` iterates `WorldStreamingConfig.chunks`, never the chunk folder, which holds far more scenes.
- **Never parent a primitive collider to an imported mesh's own transform.** An FBX child arrives non-uniformly scaled *and* rotated — `Mesh_CanopyDome` on the lander is (233, 409, 59) with 66° about X — and a `BoxCollider` under both is **sheared**, which Unity's physics cannot represent. It fails in the worst possible way: the component is there, `Collider.bounds` reports exactly the box you asked for, and every raycast passes straight through it. Measure the bounds off the renderer and put the collider on the **root** (unrotated, unscaled).
- `Prefabs/agents/…` (lowercase `a`) is the real PlayerShip path; casing drift in asset paths matters here.
- Deleted prefabs leave `PrefabInstance` blocks in scene YAML that a component-GUID grep can never find — use `Tools ▸ SpaceGame ▸ Cleanup ▸ Report Missing Prefab Instances`.
- A stuck "address already in use" UDP port survives [PlayModeTransportTeardown](Assets/Game/Editor/Multiplayer/PlayModeTransportTeardown.cs); restarting the Editor is the only known cure.

## Extending

1. Put the script under [Assets/Game/Editor/](Assets/Game/Editor) in the matching subfolder, namespace `SpaceGame.EditorTools`.
2. Name the menu `Tools/SpaceGame/<Area>/<Verb Noun>`; separate a destructive `Fix`/`Wire` from a read-only `Audit`/`Report` twin.
3. **Prefer an additive pass over a regenerating one.** Load the prefab with `PrefabUtility.LoadPrefabContents(path)`, mutate only the fields the pass owns, `SaveAsPrefabAsset`, `UnloadPrefabContents` in a `finally`. Never `SaveAsPrefabAsset` a prefab you composed from scratch over an authored one.
4. Write private fields with `SerializedFields.Set(...)` so a rename warns instead of silently doing nothing.
5. For a scene-placed prefab instance, call `PrefabUtility.RecordPrefabInstancePropertyModifications` before `EditorSceneManager.SaveScene`.
6. Added a `NetworkObject`? Register it, then `ImportAsset(path, ForceUpdate)` + `ForceReserializeAssets`. Holds state? Call `SaveableWiring.WirePrefabs()` and re-run `Validate Save Wiring`.
7. End with a `Verify()` that re-loads every written asset from disk and logs a report; do not trust a silent success.
