---
system: EditorTooling
layer: pipeline
summary: Every custom editor window, menu command, prefab/asset builder and importer hook in the project
paths:
  - Assets/Game/Editor/
  - Assets/Game/Scripts/Core/Persistence/Editor/
  - Assets/Game/Scripts/World/Streaming/NavMesh/Editor/
  - Assets/Game/Editor/Support/SerializedFields.cs
symptoms:
  - "a collider a builder added is on the prefab but nothing ever hits it"
  - "my hand edits to a prefab or the main menu disappeared after someone ran a builder"
  - "a builder logs success but nothing actually changed on disk"
  - "which menu item rebuilds this prefab, item, creature or vehicle"
  - "a renamed [SerializeField] and the builder quietly stopped setting it"
  - "scenes are full of missing prefab instances a GUID grep cannot find"
  - "a freshly built prefab works in the editor but not on clients (GlobalObjectIdHash 0)"
reads_with: [Multiplayer, Persistence, Artifacts, TerrainGeneration]
updated: 2026-09-02
---
# Editor Tooling

Every custom Unity Editor window, menu command, prefab/asset builder and importer hook in the project.

**Scope:** [Assets/Game/Editor/](Assets/Game/Editor) (all except `Editor/Tests/`), plus `Editor/` folders nested under [Assets/Game/Scripts/](Assets/Game/Scripts) (Persistence, World Streaming, Rover, Backpack Placement).
**Related:** [Multiplayer.md](Multiplayer.md) · [Persistence.md](Persistence.md) · [Artifacts.md](Artifacts.md) · [TerrainGeneration.md](TerrainGeneration.md) · [NavMeshSystem.md](NavMeshSystem.md)

## Model

- Most content is **script-generated**. A builder reads an FBX, composes a prefab, and `SaveAsPrefabAsset`s over the existing path — every hand edit to that prefab dies on the next run.
- Three menu roots, inconsistently: `Tools/…` (most), `SpaceGame/…` (portals, terrain, environment), `World/…` (streaming).
- Builders write private `[SerializeField]`s through [SerializedFields](Assets/Game/Editor/Support/SerializedFields.cs), which **warns** on an unresolved field name instead of silently no-op'ing.
- Item builders self-register their prefab in the network prefab list and re-import + reserialize to fix `GlobalObjectIdHash 0`; several then call `SaveableWiring.WirePrefabs()`.
- Only two hooks run automatically on model import; everything else is an explicit menu command by design.
- `Audit`/`Report`/`Validate`/`Diagnose` commands are read-only; `Build`/`Wire`/`Apply`/`Fix`/`Bake` write assets.

## Menu items

| Menu path | File | What it does |
| --- | --- | --- |
| SpaceGame/Environment/Build Fog Gallery Scene | [FogGallerySceneBuilder.cs](Assets/Game/Editor/Environment/FogGallerySceneBuilder.cs) | Regenerates `Scenes/Tests/FogGallery.unity` |
| SpaceGame/Environment/Install Volumetric Render Features | [VolumetricSetup.cs](Assets/Game/Editor/Environment/VolumetricSetup.cs) | Adds fog/cloud render features to the URP renderer asset (idempotent) |
| SpaceGame/Portals/Build Portal Gun Content | [PortalContentBuilder.cs](Assets/Game/Editor/Portals/PortalContentBuilder.cs) | Builds PortalGun prefab + item asset, registers network prefab |
| SpaceGame/Portals/Build Portal Test Scene | [PortalTestSceneBuilder.cs](Assets/Game/Editor/Portals/PortalTestSceneBuilder.cs) | Regenerates `Scenes/Tests/PortalTest.unity` |
| SpaceGame/Terrain/Apply Selected Material to All Terrains in Scene | [ApplyTerrainMaterial.cs](Assets/Game/Editor/Terrain/ApplyTerrainMaterial.cs) | Applies the Project-window-selected material to every Terrain |
| Tools/Build Dragon Bazooka Artifact | [DragonBazookaBuilder.cs](Assets/Game/Editor/AssetPipeline/DragonBazookaBuilder.cs) | Bazooka prefab + rockets + item asset + shake assets |
| Tools/Build Gravel Blaster Artifact | [GravelBlasterBuilder.cs](Assets/Game/Editor/AssetPipeline/GravelBlasterBuilder.cs) | GravelBlaster prefab + item + `GravelBlastShake.asset` |
| Tools/Build Laser Staff Artifact | [LaserStaffBuilder.cs](Assets/Game/Editor/AssetPipeline/LaserStaffBuilder.cs) | LaserStaff prefab + item + lightning VFX prefab |
| Tools/Build Sucker Puncher Artifact | [SuckerPuncherBuilder.cs](Assets/Game/Editor/AssetPipeline/SuckerPuncherBuilder.cs) | SuckerPuncher prefab + item + shock-ring material |
| Tools/Icon Generator | [IconGenerator.cs](Assets/Game/Editor/AssetPipeline/IconGenerator.cs) | Manual window: frame one prefab by eye, write a sprite |
| Tools/Generate {All Item Icons, Icon For Selected Item} | [BatchIconGenerator.cs](Assets/Game/Editor/AssetPipeline/BatchIconGenerator.cs) | Renders each `InventoryItem`'s own prefab into its `icon` at one house 3/4 framing |
| Tools/Creatures/Build Crab Walker Prefabs | [CrabWalkerBuilder.cs](Assets/Game/Editor/Creatures/CrabWalkerBuilder.cs) | All crab-walker variant prefabs under `Prefabs/Agents/Creatures` |
| Tools/Creatures/Build Dune Rat Prefab | [DuneRatBuilder.cs](Assets/Game/Editor/Creatures/DuneRatBuilder.cs) | `DuneRat.prefab` + material; sets ModelImporter rig options |
| Tools/Creatures/Build Golem Prefab | [GolemBuilder.cs](Assets/Game/Editor/Creatures/GolemBuilder.cs) | `Golem.prefab` (rigid-part rig) |
| Tools/Creatures/Build Vrescal Prefab | [VrescalBuilder.cs](Assets/Game/Editor/Creatures/VrescalBuilder.cs) | `Vrescal.prefab` (hexapod) |
| Tools/Environment/Build Building Prefabs | [BuildingPrefabBuilder.cs](Assets/Game/Editor/Environment/BuildingPrefabBuilder.cs) | Structure prefabs: colliders, LODGroup, static flags, ambient motion |
| Tools/Items/Build Jumping Rod | [JumpingRodBuilder.cs](Assets/Game/Editor/Items/JumpingRodBuilder.cs) | Rod + deployed prefabs + item; model in `.Model.cs` partial |
| Tools/Items/Build Net Gun | [NetGunBuilder.cs](Assets/Game/Editor/Items/NetGunBuilder.cs) | NetGun prefab, item, rope textures + `Net_Cord.mat` |
| Tools/Items/{Build Ship Parts, Place Ship Parts In Test World} | [ShipPartItemBuilder.cs](Assets/Game/Editor/Items/ShipPartItemBuilder.cs) | 7 prefabs + items, net registration, pack shapes, save wiring, verified off disk; then scatters them into `Scenes/Tests/Ferdinand_Test_world.unity` |
| Tools/Save System/Wire Saveable Prefabs | [SaveableWiring.cs](Assets/Game/Scripts/Core/Persistence/Editor/SaveableWiring.cs) | Adds `SaveableEntity` to prefabs that hold state |
| Tools/Save System/Wire Saveable {Scene Objects, Chunk Scenes} | [SaveableWiring.cs](Assets/Game/Scripts/Core/Persistence/Editor/SaveableWiring.cs) | Same for the open scenes, or for every streamed chunk scene |
| Tools/Save System/Validate Save Wiring | [SaveWiringValidator.cs](Assets/Game/Scripts/Core/Persistence/Editor/SaveWiringValidator.cs) | Reports wiring that would fail silently (missing ids, duplicate ids, unregistered prefabs) |
| Tools/Save System/Report Unsaved State | [SaveCoverageReport.cs](Assets/Game/Scripts/Core/Persistence/Editor/SaveCoverageReport.cs) | Heuristic: mutable component state covered by no saver |
| Tools/SpaceGame/Agents/Build Nomad NPC [(prefab only)] | [NomadPrefabBuilder.cs](Assets/Game/Editor/Agents/NomadPrefabBuilder.cs) | Builds `Nomad.prefab`, and (first variant only) places it in `persistentScene` |
| Tools/SpaceGame/Cleanup/{Report, Remove} Missing Prefab Instances | [MissingPrefabInstanceCleaner.cs](Assets/Game/Editor/Multiplayer/MissingPrefabInstanceCleaner.cs) | Lists scene objects whose source prefab is deleted; Remove deletes them across every scene and saves |
| Tools/SpaceGame/Items/{Build Repulsor Gauntlet, Wire FlungBody Into Player} | [RepulsorGauntletBuilder.cs](Assets/Game/Editor/AssetPipeline/RepulsorGauntletBuilder.cs) | Gauntlet prefab + item + 3 materials + 2 shake assets; then adds `FlungBody` to `PlayerCharacterNetworked.prefab` |
| Tools/SpaceGame/Items/Build Expedition Rig Prefab | [ExpeditionRigWiring.cs](Assets/Game/Editor/Backpack/ExpeditionRigWiring.cs) | Rebuilds the backpack rig + 5 holder prefabs from FBX; edits the player prefab |
| Tools/SpaceGame/Items/{Create Pack Shape Library, Reseed Undrawn Pack Shapes} | [PackShapeLibraryTool.cs](Assets/Game/Editor/Backpack/PackShapeLibraryTool.cs) | Creates/tops up `PackShapes.asset` and wires it onto every `BackpackObject`; Reseed re-derives only masks nobody drew |
| Tools/SpaceGame/Items/{Fix, Audit} Artifact Pack Orientation | [ItemPackOrientation.cs](Assets/Game/Editor/Backpack/ItemPackOrientation.cs) | Rewrites (or reports) pack-lay rotations on artifact prefabs |
| Tools/SpaceGame/Items/{Apply, Audit} Item Scale Ladder | [ItemScaleLadder.cs](Assets/Game/Editor/Items/ItemScaleLadder.cs) | Writes (or reports) each item's bracketed size; anchor is the bazooka at 1.25 m |
| Tools/SpaceGame/Items/Audit Held Item Poses | [HeldItemPoseAudit.cs](Assets/Game/Editor/Items/HeldItemPoseAudit.cs) | Measures `palmDist` / `gripNorm` per item in the real hand |
| Tools/SpaceGame/Menus/Setup Front Menu | [FrontMenuSetup.cs](Assets/Game/Editor/Menus/FrontMenuSetup.cs) | Rebuilds the main-menu UI in `Scenes/Core/MainMenu.unity` |
| Tools/SpaceGame/Menus/Setup World Select | [WorldSelectSetup.cs](Assets/Game/Editor/Menus/WorldSelectSetup.cs) | Rebuilds the world-select panel in the same scene |
| Tools/SpaceGame/Menus/Setup Lobby Preview | [LobbyPreviewSetup.cs](Assets/Game/Editor/Menus/LobbyPreviewSetup.cs) | Builds `Resources/LobbyPreviewAstronaut.prefab` + wires the scene |
| Tools/SpaceGame/Multiplayer/Sync Network Prefabs | [NetworkPrefabRegistrar.cs](Assets/Game/Editor/Multiplayer/NetworkPrefabRegistrar.cs) | Adds every prefab with a root `NetworkObject` to the list `NetworkManager.prefab` references |
| Tools/SpaceGame/Player/Build Upper Body Layer | [PlayerUpperBodySetup.cs](Assets/Game/Editor/PlayerUpperBodySetup.cs) | Rebuilds the aim/hold layer inside `AstronautArmature.controller` |
| Tools/SpaceGame/Ragdoll/Wire Prefabs | [RagdollWiring.cs](Assets/Game/Editor/AssetPipeline/RagdollWiring.cs) | Adds `AgentRagdoll`/`PlayerRagdoll` across creature + player prefabs |
| Tools/SpaceGame/Ragdoll/{Report Candidates, Audit Skeletons, Diagnose Wired Prefabs} | [RagdollWiring.cs](Assets/Game/Editor/AssetPipeline/RagdollWiring.cs) | Read-only: felling candidates, skinned vs rigid-part rigs, broken joints on wired prefabs |
| Tools/Tests/Run EditMode Tests (headless) | [HeadlessTestRunner.cs](Assets/Game/Editor/Tests/HeadlessTestRunner.cs) | Runs the EditMode suite, writes a result file |
| Tools/Tests/{Build Multiplayer Test Player, Print Multiplayer Test Commands} | [MultiplayerTestPlayerBuilder.cs](Assets/Game/Editor/Tests/MultiplayerTestPlayerBuilder.cs) | Builds the standalone player for two-machine tests; logs the host/client CLI invocations |
| Tools/Vehicles/Build PlayerShip Prefab | [PlayerShipBuilder.cs](Assets/Game/Editor/Vehicles/PlayerShipBuilder.cs) | `PlayerShip.prefab` from `player_ship.fbx` + baked convex collision proxy |
| Tools/Vehicles/Build ShipRV Prefab | [ShipRVBuilder.cs](Assets/Game/Editor/Vehicles/ShipRVBuilder.cs) | `ShipRV.prefab` incl. the world's only `SpawnPoint` and workstation |
| Tools/Vehicles/Build Desert Crawler Prefab | [DesertCrawlerBuilder.cs](Assets/Game/Editor/Vehicles/DesertCrawlerBuilder.cs) | `DesertCrawler.prefab` (six-legged habitat) |
| Tools/Vehicles/Build Dune Foil Prefab | [DuneFoilBuilder.cs](Assets/Game/Editor/Vehicles/DuneFoilBuilder.cs) | `DuneFoil.prefab` + wind prefab reference |
| Tools/Vehicles/Build Dune Ornithopter Prefab | [OrnithopterBuilder.cs](Assets/Game/Editor/Vehicles/OrnithopterBuilder.cs) | `DuneOrnithopter.prefab` |
| Tools/Vehicles/Build Wing Pack Item | [WingPackBuilder.cs](Assets/Game/Editor/Vehicles/WingPackBuilder.cs) | `WingPack.prefab` + item asset (folded ornithopter) |
| Tools/World/Bake Sandstorm Noise | [SandstormNoiseGenerator.cs](Assets/Game/Editor/Weather/SandstormNoiseGenerator.cs) | Writes `Textures/Environment/SandstormNoise.asset` |
| Tools/World/Build Sandstorm Grit | [SandstormNearDetailBuilder.cs](Assets/Game/Editor/Weather/SandstormNearDetailBuilder.cs) | `SandstormGrit.prefab` + material |
| Tools/World Streaming/Chunk World | [WorldChunkerEditor.cs](Assets/Game/Editor/World/WorldChunkerEditor.cs) | Window: splits a master scene into 500×500 m chunk scenes + TerrainData, rewrites `WorldStreamingConfig.asset` |
| Tools/World Streaming/Bake Map Meshes | [MapMeshBaker.cs](Assets/Game/Editor/Map/MapMeshBaker.cs) | Window: one low-poly mesh per chunk into `Resources/MapMeshes` |
| World/Streaming/Bake World NavMesh | [WorldNavMeshBaker.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshBaker.cs) | Bakes all chunk collision into one NavMesh asset (edit mode only) |
| World/Streaming/Check World NavMesh Is Current | [WorldNavMeshStaleness.cs](Assets/Game/Scripts/World/Streaming/NavMesh/Editor/WorldNavMeshStaleness.cs) | Compares per-chunk dependency hashes against bake-time hashes |
| World/Streaming/Run Chunk Traversal Probe | [ChunkStreamingProbeMenu.cs](Assets/Game/Scripts/World/Streaming/Diagnostics/Editor/ChunkStreamingProbeMenu.cs) | Deletes the old report, arms a streaming probe run |

## Generator scripts

Everything below overwrites its output **wholesale** (`SaveAsPrefabAsset`/`CreateAsset`). Change the builder, never the output. Hand edits are never safe unless noted.

| Script | Generates/overwrites |
| --- | --- |
| [PlayerShipBuilder](Assets/Game/Editor/Vehicles/PlayerShipBuilder.cs) · [ShipRVBuilder](Assets/Game/Editor/Vehicles/ShipRVBuilder.cs) · [DesertCrawlerBuilder](Assets/Game/Editor/Vehicles/DesertCrawlerBuilder.cs) · [DuneFoilBuilder](Assets/Game/Editor/Vehicles/DuneFoilBuilder.cs) · [OrnithopterBuilder](Assets/Game/Editor/Vehicles/OrnithopterBuilder.cs) | `Prefabs/Agents/Vehicles/**` and `Prefabs/agents/Vehicles/Spacecraft/PlayerShip.prefab`; ShipRV also owns `RepairWorkstation.prefab` and the world's only `SpawnPoint` |
| [CrabWalkerBuilder](Assets/Game/Editor/Creatures/CrabWalkerBuilder.cs) · [DuneRatBuilder](Assets/Game/Editor/Creatures/DuneRatBuilder.cs) · [GolemBuilder](Assets/Game/Editor/Creatures/GolemBuilder.cs) · [VrescalBuilder](Assets/Game/Editor/Creatures/VrescalBuilder.cs) · [NomadPrefabBuilder](Assets/Game/Editor/Agents/NomadPrefabBuilder.cs) | `Prefabs/Agents/Creatures/*.prefab`, `Prefabs/Agents/Characters/Nomad.prefab` |
| [DragonBazookaBuilder](Assets/Game/Editor/AssetPipeline/DragonBazookaBuilder.cs) · [GravelBlasterBuilder](Assets/Game/Editor/AssetPipeline/GravelBlasterBuilder.cs) · [LaserStaffBuilder](Assets/Game/Editor/AssetPipeline/LaserStaffBuilder.cs) · [SuckerPuncherBuilder](Assets/Game/Editor/AssetPipeline/SuckerPuncherBuilder.cs) · [RepulsorGauntletBuilder](Assets/Game/Editor/AssetPipeline/RepulsorGauntletBuilder.cs) · [PortalContentBuilder](Assets/Game/Editor/Portals/PortalContentBuilder.cs) | `Prefabs/Items/Artifacts/**` + `Resources/Items/Artifacts/*.asset` + shake and material assets |
| [NetGunBuilder](Assets/Game/Editor/Items/NetGunBuilder.cs) · [JumpingRodBuilder](Assets/Game/Editor/Items/JumpingRodBuilder.cs) · [ShipPartItemBuilder](Assets/Game/Editor/Items/ShipPartItemBuilder.cs) · [WingPackBuilder](Assets/Game/Editor/Vehicles/WingPackBuilder.cs) | Item prefabs + item assets (+ rope textures, pack shapes, test-world placement) |
| [ExpeditionRigWiring](Assets/Game/Editor/Backpack/ExpeditionRigWiring.cs) | Backpack rig + 5 holder prefabs; also edits `PlayerCharacter.prefab` |
| [BuildingPrefabBuilder](Assets/Game/Editor/Environment/BuildingPrefabBuilder.cs) · [SandstormNearDetailBuilder](Assets/Game/Editor/Weather/SandstormNearDetailBuilder.cs) · [SandstormNoiseGenerator](Assets/Game/Editor/Weather/SandstormNoiseGenerator.cs) | `Prefabs/Environment/Structures/*`, `Effects/SandstormGrit.prefab`, `SandstormNoise.asset` |
| [FrontMenuSetup](Assets/Game/Editor/Menus/FrontMenuSetup.cs) · [WorldSelectSetup](Assets/Game/Editor/Menus/WorldSelectSetup.cs) · [LobbyPreviewSetup](Assets/Game/Editor/Menus/LobbyPreviewSetup.cs) · [PortalTestSceneBuilder](Assets/Game/Editor/Portals/PortalTestSceneBuilder.cs) · [FogGallerySceneBuilder](Assets/Game/Editor/Environment/FogGallerySceneBuilder.cs) | UI subtrees in `Scenes/Core/MainMenu.unity`, `Resources/LobbyPreviewAstronaut.prefab`, `Scenes/Tests/{PortalTest,FogGallery}.unity` |
| [WorldChunkerEditor](Assets/Game/Editor/World/WorldChunkerEditor.cs) · [MapMeshBaker](Assets/Game/Editor/Map/MapMeshBaker.cs) | `Scenes/World/Chunks/*`, `Terrain/ChunkData/*`, `Settings/WorldStreamingConfig.asset`, `Resources/MapMeshes/*` |
| [DoubleSidedMaterials](Assets/Game/Editor/Support/DoubleSidedMaterials.cs) (library, no menu) | `Art/Materials/Vehicles/* (DoubleSided).mat`, refreshed from source on every vehicle build |
| [PackShapeLibraryTool](Assets/Game/Editor/Backpack/PackShapeLibraryTool.cs) | `ScriptableObjects/Items/PackShapes.asset` — `Reseed` **preserves** hand-drawn masks |
| [SaveableWiring](Assets/Game/Scripts/Core/Persistence/Editor/SaveableWiring.cs) · [RagdollWiring](Assets/Game/Editor/AssetPipeline/RagdollWiring.cs) · [ItemScaleLadder](Assets/Game/Editor/Items/ItemScaleLadder.cs) · [ItemPackOrientation](Assets/Game/Editor/Backpack/ItemPackOrientation.cs) | Additive `LoadPrefabContents` edits — safe to hand-edit except the fields they own |

## Importers & postprocessors

| Type | File | Applies to |
| --- | --- | --- |
| `AssetPostprocessor.OnPreprocessModel` | [MeshReadablePostprocessor.cs](Assets/Game/Editor/AssetPipeline/MeshReadablePostprocessor.cs) | Every imported model — forces `isReadable = true` so runtime NavMesh/collider code sees real geometry |
| `AssetPostprocessor.OnPostprocessAnimation` | [RootMotionCurveStripper.cs](Assets/Game/Editor/AssetPipeline/RootMotionCurveStripper.cs) | Every imported clip — deletes empty-path `m_Local*` curves that would teleport the object to the origin (hits rigid-part rigs, not skinned ones) |
| `AssetPostprocessor.OnPostprocessAllAssets` | [ItemFootprintCacheInvalidator.cs](Assets/Game/Scripts/Items/Backpack/Placement/Editor/ItemFootprintCacheInvalidator.cs) | Any import/move/delete — clears `ItemFootprint`'s size cache unconditionally |
| `[InitializeOnLoad]` | [PlayModeTransportTeardown.cs](Assets/Game/Editor/Multiplayer/PlayModeTransportTeardown.cs) | Shuts the NGO session down at `ExitingPlayMode` to limit UDP socket leaks |
| `[InitializeOnLoadMethod]` | [HeadlessTestRunner.cs](Assets/Game/Editor/Tests/HeadlessTestRunner.cs) | Resumes a `SessionState`-pending test run across domain reloads |
| Per-builder `ModelImporter` writes | Portal/Building/ExpeditionRig/DuneRat/Golem/Vrescal builders | Their own FBX only (rig type, scale, readability) |

Custom inspectors: [TerrainGenManagerEditor](Assets/Game/Editor/Terrain/TerrainGenManagerEditor.cs) (Bake All / Regenerate / Clear), [TerrainFeatureSpawnerEditor](Assets/Game/Editor/Terrain/TerrainFeatureSpawnerEditor.cs) + [TerrainFeatureHandles](Assets/Game/Editor/Terrain/TerrainFeatureHandles.cs) (scene-view footprint handles, live preview, Bake & Save Mesh via [TerrainFeatureBakeUtility](Assets/Game/Editor/Terrain/TerrainFeatureBakeUtility.cs)), [CaveSpawnerEditor](Assets/Game/Editor/Terrain/CaveSpawnerEditor.cs) (Bake & Save NavMesh), [WorldStreamerEditor](Assets/Game/Scripts/World/Streaming/Editor/WorldStreamerEditor.cs), [PackShapeLibraryEditor](Assets/Game/Scripts/Items/Backpack/Placement/Editor/PackShapeLibraryEditor.cs) (paintable mask grid), [EntityProfileEditors](Assets/Game/Editor/Agents/EntityProfileEditors.cs) (Generate → adds+configures AI modules), [BehaviourModuleEditor](Assets/Game/Editor/Agents/BehaviourModuleEditor.cs), [RoverBogieIKEditor](Assets/Game/Scripts/Vehicles/Rover/Editor/RoverBogieIKEditor.cs).

## Flows

Regenerate an artifact prefab (e.g. Net Gun) end to end:

1. Edit the Blender source, export the FBX to `Assets/Game/Art/Models/…`; the postprocessors run on import.
2. Edit the **builder** ([NetGunBuilder.cs](Assets/Game/Editor/Items/NetGunBuilder.cs)) — not the prefab.
3. Run `Tools ▸ Items ▸ Build Net Gun`. It rebuilds the prefab + `InventoryItem`, registers the network prefab, then `ImportAsset(ForceUpdate)` + `ForceReserializeAssets` so `GlobalObjectIdHash` reaches the YAML.
4. `Items ▸ Apply Item Scale Ladder`, then `Audit Held Item Poses`; `Tools ▸ Generate Icon For Selected Item`.
5. `Save System ▸ Wire Saveable Prefabs` → `Validate Save Wiring`; `Multiplayer ▸ Sync Network Prefabs`, read the report.

Regenerate the streamed world: `Tools ▸ World Streaming ▸ Chunk World` → `World ▸ Streaming ▸ Bake World NavMesh` → `Tools ▸ World Streaming ▸ Bake Map Meshes` → `Tools ▸ Save System ▸ Wire Saveable Chunk Scenes`.

## Multiplayer

- [NetworkPrefabRegistrar](Assets/Game/Editor/Multiplayer/NetworkPrefabRegistrar.cs) is the sweep: it adds every prefab with a root `NetworkObject` to the list referenced by `Prefabs/Systems/NetworkManager.prefab`, choosing the **largest** existing `NetworkPrefabsList` because several near-duplicates survive from a restructure.
- Item builders register individually into `ScriptableObjects/Networking/DefaultNetworkPrefabs.asset` — the list NetworkManager actually reads. `Assets/DefaultNetworkPrefabs.asset` at the project root is Netcode's own regenerated file and is **not** consulted.
- A script-added `NetworkObject` ships `GlobalObjectIdHash 0` and NGO silently keeps one prefab per hash, so builders must `ImportAsset(ForceUpdate)` then `ForceReserializeAssets`. Missing registration fails on **clients only**.

## Persistence

- [SaveableWiring](Assets/Game/Scripts/Core/Persistence/Editor/SaveableWiring.cs) stamps `SaveableEntity` (identity ids) onto prefabs, open scenes and every chunk scene; several item builders call `WirePrefabs()` at the end of a build.
- [SaveWiringValidator](Assets/Game/Scripts/Core/Persistence/Editor/SaveWiringValidator.cs) catches duplicate instance ids, unsaved scene-placed objects and restored prefabs missing from the network list — none of which any compiler or unit test can see.
- [SaveCoverageReport](Assets/Game/Scripts/Core/Persistence/Editor/SaveCoverageReport.cs) heuristically lists mutable state with no saver — a starting point, not a defect list.

## Gotchas

- **AssetDatabase goes read-only in some sessions and discards prefab/asset saves without raising anything.** A builder can report success having written nothing. [ShipPartItemBuilder](Assets/Game/Editor/Items/ShipPartItemBuilder.cs)'s `Verify()` is the pattern: re-read everything off disk and assert.
- **Hand edits to generated prefabs and to builder-owned `MainMenu.unity` subtrees are wiped on the next run.** Change the builder — this has bitten `PlayerShip.prefab` specifically. A renamed `[SerializeField]` also breaks a builder silently unless it goes through [SerializedFields](Assets/Game/Editor/Support/SerializedFields.cs), which warns.
- Materials that are sub-assets of an FBX are regenerated on reimport, so flags written onto them revert; that is why [DoubleSidedMaterials](Assets/Game/Editor/Support/DoubleSidedMaterials.cs) writes standalone copies beside the prefabs and refreshes them every build.
- Hardcoded paths everywhere: `WorldChunkerEditor`'s chunk size (500×500), output folders and config path are deliberately not exposed — changing them orphans every existing chunk. `PlayerShipBuilder` and `ShipPartItemBuilder` write into `Scenes/Tests/Ferdinand_Test_world.unity` by name. `WorldNavMeshBaker` iterates `WorldStreamingConfig.chunks`, never the chunk folder, which holds far more scenes.
- **Never parent a primitive collider to an imported mesh's own transform.** An FBX child arrives non-uniformly scaled *and* rotated — `Mesh_CanopyDome` on the lander is (233, 409, 59) with 66° about X — and a `BoxCollider` under both is **sheared**, which Unity's physics cannot represent. It fails in the worst possible way: the component is there, `Collider.bounds` reports exactly the box you asked for, and every raycast passes straight through it, so the symptom you were fixing is unchanged and the fix looks applied. Measure the bounds off the renderer and put the collider on the **root** (unrotated, unscaled), the way `PlayerShipBuilder.BlockReachThroughCanopy` and the chairs' seat volumes do.
- `Prefabs/agents/…` (lowercase `a`) is the real PlayerShip path; casing drift in asset paths matters here.
- Deleted prefabs leave `PrefabInstance` blocks in scene YAML that a component-GUID grep can never find — use `Tools ▸ SpaceGame ▸ Cleanup ▸ Report Missing Prefab Instances`.
- A stuck "address already in use" UDP port survives [PlayModeTransportTeardown](Assets/Game/Editor/Multiplayer/PlayModeTransportTeardown.cs); restarting the Editor is the only known cure.

## Extending

1. Put the script under [Assets/Game/Editor/](Assets/Game/Editor) in the matching subfolder, namespace `SpaceGame.EditorTools`.
2. Name the menu `Tools/SpaceGame/<Area>/<Verb Noun>`; separate a destructive `Build`/`Fix` from a read-only `Audit`/`Report` twin.
3. Load the prefab with `PrefabUtility.LoadPrefabContents(path)` for an additive edit (never `SaveAsPrefabAsset` over someone else's prefab), mutate, `SaveAsPrefabAsset`, `UnloadPrefabContents` in a `finally`.
4. Write private fields with `SerializedFields.Set(...)` so a rename warns instead of silently doing nothing.
5. For a scene-placed prefab instance, call `PrefabUtility.RecordPrefabInstancePropertyModifications` before `EditorSceneManager.SaveScene`.
6. Added a `NetworkObject`? Register it, then `ImportAsset(path, ForceUpdate)` + `ForceReserializeAssets`. Holds state? Call `SaveableWiring.WirePrefabs()` and re-run `Validate Save Wiring`.
7. End with a `Verify()` that re-loads every written asset from disk and logs a report; do not trust a silent success.
