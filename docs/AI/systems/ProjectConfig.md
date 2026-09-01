---
system: ProjectConfig
layer: core
summary: Engine and package versions, physics layers and collision matrix, tags, URP assets, git attribute rules
paths:
  - ProjectSettings/
  - Packages/manifest.json
  - Assets/Game/Settings/PC_RPAsset.asset
  - .gitattributes
  - .gitignore
symptoms:
  - "jump heights and ballistic arcs are wrong — gravity here is -18, not -9.81"
  - "a raycast right after moving something reads the collider's old position"
  - "I put things on a new layer and they still collide with everything"
  - "a .asset or .unity file fails with 'Unknown error occurred while loading'"
  - "legacy Input.GetKey compiles fine but throws at runtime"
  - "which Unity, URP or Netcode version is this project on, and what packages are installed"
reads_with: [Multiplayer, NavMeshSystem, WorldStreaming, Environment]
updated: 2026-09-01
---

# Project Config

Project-wide Unity settings: engine version, packages, physics layers and the collision matrix, tags, URP render assets, and version-control rules.

**Scope:** `ProjectSettings/`, `Packages/manifest.json`, `.gitattributes`, `.gitignore`, `Assets/Game/Settings/*_RPAsset.asset`, `Assets/Game/Settings/*_Renderer.asset`
**Related:** [Multiplayer.md](Multiplayer.md), [NavMeshSystem.md](NavMeshSystem.md), [WorldStreaming.md](WorldStreaming.md), [Environment.md](Environment.md), [ArtPipeline.md](ArtPipeline.md)

## Versions

| Setting | Value | Source |
| --- | --- | --- |
| Unity editor | **6000.3.11f1** (rev `3000ef702840`) | [ProjectSettings/ProjectVersion.txt](ProjectSettings/ProjectVersion.txt) |
| Render pipeline | **URP 17.3.0**, asset `Assets/Game/Settings/PC_RPAsset.asset` | `GraphicsSettings.m_CustomRenderPipeline` + `QualitySettings.customRenderPipeline` |
| Color space | Linear (`m_ActiveColorSpace: 1`) | ProjectSettings.asset |
| Input handling | **New Input System only** (`activeInputHandler: 1`) — legacy `Input.*` is dead | ProjectSettings.asset |
| Scripting API | .NET Standard 2.1 (`apiCompatibilityLevel: 6`), incremental GC on | ProjectSettings.asset |
| Asset serialization | ForceText (`m_SerializationMode: 2`), asset pipeline v2 | EditorSettings.asset |
| Company / product | Hackerspace NTNU / SpaceGame | ProjectSettings.asset |
| Target | **Desktop standalone only.** Default 1080x720, `fullscreenMode: 1` (borderless), non-resizable, runs in background. iOS/Android icon blocks exist but no mobile quality level applies. | ProjectSettings.asset, QualitySettings.asset |
| Fixed timestep | 0.02 s (50 Hz), max allowed 0.333 | TimeManager.asset |
| Build scenes | 68 entries incl. `Core/Bootstrap.unity`, `Core/MainMenu.unity`, `world/persistentScene.unity`, per-dev test scenes | EditorBuildSettings.asset |

## Packages

From [Packages/manifest.json](Packages/manifest.json) — modules and IDE packages omitted.

| Package | Version | Used for |
| --- | --- | --- |
| `com.unity.netcode.gameobjects` | 2.9.1 | All netcode; `NetworkBehaviour`, `[Rpc]`, network prefab list |
| `com.unity.services.multiplayer` | 2.1.3 | Lobby + Relay services (join codes, session hosting) |
| `com.unity.multiplayer.tools` | 2.2.8 | Runtime net stats / profiling overlay |
| `com.unity.multiplayer.playmode` | 2.0.1 | MPPM virtual players (`VirtualProjectsConfig.json`) |
| `com.unity.multiplayer.center` | 1.0.1 | Setup wizard only |
| `com.veriorpies.parrelsync` | git (`ParrelSync`) | Second editor clone for client testing — **git URL dep, no version pin** |
| `com.unity.inputsystem` | 1.19.0 | `InputControls.cs` generated bindings |
| `com.unity.render-pipelines.universal` | 17.3.0 | URP; all custom render features subclass its API |
| `com.unity.visualeffectgraph` | 17.3.0 | VFX graphs |
| `com.unity.nuget.newtonsoft-json` | 3.2.2 | Save/load serialization |
| `com.unity.ai.navigation` | 2.0.11 | `NavMeshSurface`, runtime + baked navmesh |
| `com.unity.probuilder` | 6.0.9 | Greyboxing interiors |
| `com.unity.timeline` | 1.8.11 | Cutscenes |
| `com.unity.test-framework` | 1.6.0 | EditMode tests under `Assets/Game/Editor/Tests` |
| `com.unity.ai.assistant` | 2.6.0-pre.1 | Editor AI sidecar — **pre-release**; its `Unity.Relay.Editor` assembly is unrelated to multiplayer Relay |
| `com.unity.visualscripting` | 1.9.10 | Unused by gameplay; ships generated files (gitignored) |

FMOD is **not** a package — it is vendored under `Assets/Plugins/FMOD/`.

## Layers

From [ProjectSettings/TagManager.asset](ProjectSettings/TagManager.asset). Index 3 and 11–31 are empty.

| Index | Layer | Used for |
| --- | --- | --- |
| 0 | `Default` | Everything, including the player root and all terrain by default |
| 1 | `TransparentFX` | Builtin; excluded from the world NavMesh bake |
| 2 | `Ignore Raycast` | Builtin; excluded from the world NavMesh bake |
| 4 | `Water` | Declared, **zero authored objects and zero code references** |
| 5 | `UI` | Canvases (8 prefabs); excluded from the NavMesh bake |
| 6 | `Player` | Declared, **nothing is on it** — see Gotchas |
| 7 | `Ground` | Terrain/world collision. One scene object; mostly a *name* used in `LayerMask.GetMask("Default","Ground","Interior")` occlusion masks |
| 8 | `Hologram` | Map hologram visuals; assigned at runtime by `MapHologramTerrain.ApplyHologramLayer()` |
| 9 | `Interior` | Cave meshes; assigned at runtime by `CaveSpawner` from `CaveMaterialSettings.caveLayer` |
| 10 | `PackItem` | Stowed backpack gear; assigned at runtime by `BackpackItemVisual` (`ItemLayerName`) |

Rendering layers are URP defaults (`Default` + `Light Layer 1..7`). Both `PC_Renderer` and `Mobile_Renderer` use `m_Bits: 4294967295` for prepass/opaque/transparent — no layer-based render filtering.

## Collision matrix

`PhysicsManager.m_LayerCollisionMatrix` is a 128-byte hex blob: 32 little-endian `uint32` masks, one per layer, bit *j* = "layer *i* collides with layer *j*".

**Decoded from the bitmask: every one of the 32 masks is `0xffffffff`. There are zero exclusions — all 528 layer pairs collide.**

Consequences:
- The layers above are **presentation/query tags only**. Nothing is separated *physically* by layer.
- Every "don't hit X" behaviour is a per-query `LayerMask` in C# (e.g. `Interactor`, `PerceptionModule.occlusionLayers`, `LeggedLocomotion` ground probes), not a matrix rule.
- Adding a layer therefore costs nothing collision-wise, and *removing* an unwanted collision requires editing the raycast/overlap call, not the matrix.

## Tags & sorting layers

- **Custom tags: none.** `TagManager.tags: []`. Only Unity's 7 builtins exist (`Untagged`, `Respawn`, `Finish`, `EditorOnly`, `MainCamera`, `Player`, `GameController`).
- `CompareTag("Player")` is used in ~8 gameplay files (`SpawnClearance`, `VolumeTrigger`, `SnareCatch`, `LassoArtifact`, `LeashEnd`, `DamageNumbers`, `CaveExitCover`). It works because `PlayerCharacter.prefab` carries the **builtin** `Player` tag, and `PlayerCharacterNetworked.prefab` is a prefab instance of it.
- **Sorting layers: one**, `Default` (uniqueID 0). Nothing 2D depends on ordering.
- NavMesh areas ([NavMeshAreas.asset](ProjectSettings/NavMeshAreas.asset)): only the 3 builtins — `Walkable`, `Not Walkable`, `Jump`.

## Quality & graphics

Physics ([DynamicsManager.asset](ProjectSettings/DynamicsManager.asset)) — the values that bite:

| Setting | Value | Why it matters |
| --- | --- | --- |
| `m_Gravity` | **(0, -18, 0)** | Nearly 2x Earth. Any hand-tuned jump/ballistic/arc math must assume -18, not -9.81. Cloth keeps -9.81. |
| `m_AutoSyncTransforms` | 0 | A raycast right after `transform.position = …` sees the **stale** collider pose unless you call `Physics.SyncTransforms()`. |
| `m_ReuseCollisionCallbacks` | 1 | The `Collision` object is recycled — never cache it past the callback. |
| `m_QueriesHitTriggers` | 1 | Raycasts hit triggers by default; pass `QueryTriggerInteraction.Ignore` when you mean solids. |
| `m_SimulationMode` / timestep | FixedUpdate @ 50 Hz | |
| `m_DefaultSolverIterations` | 6 | |
| `m_WorldBounds` extent | 250 m | Ignored: `m_BroadphaseType: 0` (Sweep-and-Prune). See Gotchas. |

URP `PC_RPAsset` (`PC_Renderer`, deferred `m_RenderingMode: 2`, native render pass on): HDR on, MSAA off (`m_MSAA: 1`), render scale 1, depth **and** opaque textures required, shadow distance 50 m / 4 cascades / soft shadows, 2048 shadowmaps, 4 additional lights per object, SRP Batcher on, dynamic batching off, light layers on.
Renderer features on `PC_Renderer`: `VolumetricCloudsRenderFeature`, `SandstormRenderFeature`, `FogRenderFeature`, `NewURPRenderFeature`, `ScreenSpaceAmbientOcclusion` **enabled**; `LensDistortionRenderFeature`, `GlassDistortionRenderFeature` **disabled**.
QualitySettings has exactly **one** level, `PC` (index 0): shadow distance 40, `antiAliasing: 0`, `vSyncCount: 0`, `lodBias: 2`, realtime reflection probes off, terrain tree distance 5000.

## Version control

[.gitattributes](.gitattributes): `* text=auto eol=lf` globally; `*.cs/.shader/.cginc/.meta/.json/.xml/.yml` forced text+LF; `*.png/.jpg/.fbx/.mp4` marked `binary`.
It **deliberately does not** force `*.asset` / `*.unity` / `*.prefab` to text — a leading comment block explains that doing so previously let git's clean filter strip `0x0D` bytes out of genuinely-binary Unity assets until the editor reported "Unknown error occurred while loading". Filename-matched belt-and-braces `binary` rules cover `TerrainData*.asset`, `*[Tt]errain*.asset`, `*NavMesh*.asset`, `LightingData.asset`, `ReflectionProbe*.exr`, `Lightmap*.exr`, with `ProjectSettings/*.asset text eol=lf` re-added afterwards so `NavMeshAreas.asset` stays diffable. Verified with `git check-attr -a`: the rules resolve as intended.

**No Git LFS is configured** (no `.gitattributes` filter rules, no `.lfsconfig`) against ~1.1 GB of `Assets/` including FBX, EXR and terrain data.

[.gitignore](.gitignore) beyond the stock Unity list: `Assets/Scenes/Chunks/` (generated chunk scenes), `/Assets/Terrain/TerrainData_*.asset`, `__pycache__/` + `*.pyc` (Blender build scripts under `Assets/Models/_Source~/`), `*.blend1`, `fmod_editor.log`, `.superpowers/`, `.agent-staging/`.

## Gotchas

1. **The collision matrix is completely default.** Do not add a layer expecting physical separation — you must also flip matrix bits, and no precedent exists in this project. Prefer the established pattern: a serialized `LayerMask` on the component doing the query.
2. **Layer 6 `Player` is empty.** No prefab, no scene object, and no runtime code assigns it. `Interactor.cs:156` does `int layerMask = ~LayerMask.GetMask("Player")` — that mask excludes nothing, so interaction raycasts can hit the player's own colliders. `WorldNavMeshBaker`'s `"Player"` exclusion is likewise a no-op. Same for `Water` (4) and authored `PackItem` (10).
3. **`Interior` is excluded from the world NavMesh bake** (`WorldNavMeshBaker.ExcludedLayerNames`) but *included* in AI occlusion masks (`PerceptionModule.FallbackOcclusionLayerNames`). Cave geometry blocks sight yet contributes no walkable surface to the world mesh — caves get their own bake.
4. **Gravity is -18.** Anything ported from a tutorial or another project will fall roughly twice as fast as its author intended.
5. **`m_AutoSyncTransforms: 0`.** Teleport-then-raycast in the same frame reads stale physics. Cross-reference: `transform.position` does not move a Rigidbody here either.
6. **`m_WorldBounds` is 250 m** while the streamed world is 4000x3000 m. Harmless *today* because `m_BroadphaseType: 0` (SAP) ignores it — but switching to multibox pruning for perf would silently break physics outside a 500 m cube.
7. **`Assets/Game/Settings/Mobile_RPAsset.asset` + `Mobile_Renderer.asset` are orphans.** No quality level or graphics setting references them; the single `PC` quality level excludes Android and iPhone. Editing them changes nothing.
8. **`NewURPRenderFeature` on `PC_Renderer` is an enabled feature with a placeholder name.** Identify what it actually does before touching it.
9. **Legacy `Input.GetKey` will not compile-warn — it throws at runtime** (`activeInputHandler: 1`). Bindings live in the generated `InputControls.cs`, which only regenerates on `.inputactions` reimport.
10. **`m_EnterPlayModeOptionsEnabled: 1` with `m_EnterPlayModeOptions: 0`** means fast enter-play-mode is switched on but neither domain nor scene reload is actually disabled — statics *do* reset. Do not assume otherwise.

## Extending

1. **Add a layer.** Edit `ProjectSettings/TagManager.asset`, filling the lowest empty slot (3, then 11+). Never renumber an existing entry — prefabs and scenes store `m_Layer` as an **integer**, so a renumber silently relabels every object.
2. **Reference it by name, not index.** Follow `BackpackItemVisual.ItemLayerName` / `CaveMaterialSettings.caveLayer`: a `const string` or serialized field resolved once via `LayerMask.NameToLayer`. Guard the `-1` result — `LayerMask.GetMask` returns 0 for the *entire* call if any single name is unknown (see `GolemBuilder.cs:577`).
3. **Decide where the filtering lives.** Default to a serialized `LayerMask` on the querying component. Only touch the matrix if two sets of *rigidbodies* must pass through each other.
4. **If you must change the matrix**, do it in Edit > Project Settings > Physics and commit the resulting `m_LayerCollisionMatrix` diff on its own. Then update the decoded statement in this file — the blob is unreviewable by eye, so the prose is the actual documentation.
5. **Add it to the NavMesh exclusion list** (`WorldNavMeshBaker.ExcludedLayerNames`) if the new layer carries moving or non-world geometry, otherwise a bake will freeze it into the permanent mesh.
6. **Verify on a client and across a reload.** A layer assigned in `Awake` on the host only is invisible to clients; a layer assigned at runtime is not saved, so the assigning code must re-run on load.
7. **New `.asset` types:** check `git check-attr -a <path>` before committing. If the file is genuinely binary, add a filename-matched `binary` rule near the existing ones — do **not** add a broad `*.asset text` rule.
