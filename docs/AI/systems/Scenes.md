---
system: Scenes
layer: world
summary: Map of every .unity scene, its role, and the build-settings order runtime scene loads depend on
paths:
  - Assets/Game/Scenes/
  - ProjectSettings/EditorBuildSettings.asset
  - Assets/Game/Scenes/References/
  - Assets/Game/Settings/WorldStreamingConfig.asset
symptoms:
  - "the deathmatch route loads an empty arena over persistentScene"
  - "pressing Play in my own scene bounces through Bootstrap and lands somewhere else"
  - "a client fails to join with 'Scene Hash N does not exist in the HashToBuildIndex table'"
  - "the NavMesh baker silently skips every chunk / LoadAssetAtPath returns null for a chunk"
  - "I added a chunk or interior scene and nothing ever loads it"
  - "which scene is build index 0 or 1, and where does the world scene live"
reads_with: [WorldStreaming, Multiplayer, SceneTransitions]
updated: 2026-09-01
---

# Scenes

Map of every `.unity` scene in the project, its role, and the build-settings order that runtime scene loads depend on.
**Scope:** all `*.unity` under [Assets/](Assets/), [ProjectSettings/EditorBuildSettings.asset](ProjectSettings/EditorBuildSettings.asset), [Assets/Game/Scenes/References/](Assets/Game/Scenes/References), [Assets/Game/Settings/WorldStreamingConfig.asset](Assets/Game/Settings/WorldStreamingConfig.asset).
**Related:** [Assets/Game/Scripts/Core/SceneManagement/](Assets/Game/Scripts/Core/SceneManagement), [Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs](Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs), [.claude/skills/spacegame-multiplayer/SKILL.md](.claude/skills/spacegame-multiplayer/SKILL.md)

## Model

- **75 scenes on disk**, 68 in build settings, all enabled, none disabled.
- **Build index 0 is [Bootstrap.unity](Assets/Game/Scenes/Core/Bootstrap.unity)** and index 1 must be MainMenu. [Bootstrapper.cs](Assets/Game/Scripts/Core/SceneManagement/Core/Bootstrapper.cs) force-loads index `0` `Single`, then loads back the scene you pressed Play in — falling back to hardcoded **index 1** when there is none. Reordering build settings so MainMenu is not index 1 silently changes the Play-from-Bootstrap destination.
- **[persistentScene](Assets/Game/Scenes/world/persistentScene.unity) is the root gameplay scene**, loaded `Single`. It holds `Managers`, `WorldStreamer`, `[SaveSystem]`, `InteriorManager`, `NpcWorldSim`, `Weather`, `SpawnPoint`, `ArrivalDirector`. Everything else in-game is **additive** on top of it.
- Three additive layers stack onto the root: **world chunks** (`WorldStreamer`, by scene *name*), **interiors** (`InteriorManager`, by scene *name* from `InteriorScene` assets), and the **minigame arena** (`MainMenuUI`, additive after the root finishes loading).
- Networked sessions load through `NetworkManager.Singleton.SceneManager.LoadScene`, not `SceneManager`. Both the streamer and `InteriorManager` branch on `Network.IsNetworked` and pick the right one.
- Scene names are indirected through `SceneReference` ScriptableObjects in [Assets/Game/Scenes/References/](Assets/Game/Scenes/References) so the menu and the lobby cannot disagree; `MainMenuUI.GameSceneName` is the single source for "the world scene".
- The exit route is one constant: `SessionExit.MenuSceneName = "MainMenu"` in [SessionExit.cs](Assets/Game/Scripts/Core/Multiplayer/Session/SessionExit.cs).

## Scenes

| Scene | Path | Role | In build? |
| --- | --- | --- | --- |
| Bootstrap | [Core/Bootstrap.unity](Assets/Game/Scenes/Core/Bootstrap.unity) | Index 0. One `Bootstrapper` object; entry gate for every Play mode | 0 |
| MainMenu | [Core/MainMenu.unity](Assets/Game/Scenes/Core/MainMenu.unity) | Front end: `MainMenuUI`, lobby preview camera, world select | 1 |
| persistentScene | [world/persistentScene.unity](Assets/Game/Scenes/world/persistentScene.unity) | **Root gameplay scene.** Managers, streamer, save system, spawn point, arrival cutscene | 7 |
| AlgeaCave | [Interiors/AlgeaCave.unity](Assets/Game/Scenes/Interiors/AlgeaCave.unity) | Additive interior; target of `Interior_AlgeaCave` (note the `Algea` spelling) | 8 |
| SandstoneCaveInterior | [Interiors/SandstoneCaveInterior.unity](Assets/Game/Scenes/Interiors/SandstoneCaveInterior.unity) | Additive interior, ~3.5 MB, 20+ `AlgaeLight_*`; target of `Interior_SandstoneCave` | 9 |
| MinigameArena | [Minigames/MinigameArena.unity](Assets/Game/Scenes/Minigames/MinigameArena.unity) | Deathmatch arena, loaded **additively** over persistentScene. **Currently empty — see Gotchas** | 58 |
| Ferdinand_Test_world | [Tests/Ferdinand_Test_world.unity](Assets/Game/Scenes/Tests/Ferdinand_Test_world.unity) | Second-world root; own `WorldStreamer` + `InteriorManager`. Editor-only entry, no menu route | 59 |
| Blocking test | [Tests/Blocking test.unity](Assets/Game/Scenes/Tests/Blocking%20test.unity) | Terrain + ProBuilder blockout; `Blocking scene` SceneReference points here | 2 |
| Aleksander test scene | [Tests/Aleksander test scene.unity](Assets/Game/Scenes/Tests/Aleksander%20test%20scene.unity) | Personal sandbox; visor overlay + volumetric explosion + waypoints | 3 |
| Tommy test scene | [Tests/Tommy test scene.unity](Assets/Game/Scenes/Tests/Tommy%20test%20scene.unity) | Personal sandbox; floor + camera only | 4 |
| Emil test scene | [Tests/Emil test scene.unity](Assets/Game/Scenes/Tests/Emil%20test%20scene.unity) | Personal sandbox; plane + light | 5 |
| Marius test scene | [Tests/Marius test scene.unity](Assets/Game/Scenes/Tests/Marius%20test%20scene.unity) | Personal sandbox; artifact + particle/movement cameras | 6 |
| CaveTest | [Tests/CaveTest.unity](Assets/Game/Scenes/Tests/CaveTest.unity) | Empty since creation (125 lines, no roots) — a stub, not a regression | no |
| DuneFoilTest | [Tests/DuneFoilTest.unity](Assets/Game/Scenes/Tests/DuneFoilTest.unity) | Sand plane + `PlayerStandIn` + preview cam for the dune foil sailer | no |
| FogGallery | [Tests/FogGallery.unity](Assets/Game/Scenes/Tests/FogGallery.unity) | Volumetric fog reference gallery: 8 named volumes + overlap lamps | no |
| PortalTest | [Tests/PortalTest.unity](Assets/Game/Scenes/Tests/PortalTest.unity) | Portal traversal box with `Traveller_0..2`, crates, pillars | no |
| Markus Music Test Scene | [Tests/Markus Music Test Scene.unity](Assets/Game/Scenes/Tests/Markus%20Music%20Test%20Scene.unity) | Audio sandbox | no |
| SpriteRenderScene | [Utility/SpriteRenderScene.unity](Assets/Game/Scenes/Utility/SpriteRenderScene.unity) | Camera + light rig used by the inventory icon bakers ([IconGenerator.cs](Assets/Game/Editor/AssetPipeline/IconGenerator.cs)) | no |
| 0 | [_Recovery/0.unity](Assets/_Recovery/0.unity) | Byte-identical copy of Bootstrap left by a Unity crash recovery. Dead — delete | no |

## Chunk scenes

| World | Path pattern | Grid | Count | Config |
| --- | --- | --- | --- | --- |
| Main | [Assets/Game/Scenes/world/Chunks/](Assets/Game/Scenes/world/Chunks)`Chunk_{x}_{y}.unity`, x 0–7, y 0–5 | 8 × 6, 500 m cells, origin (0, 0, −1000) | **48** | [WorldStreamingConfig.asset](Assets/Game/Settings/WorldStreamingConfig.asset) |
| Ferdinand (2nd) | [Assets/Game/Scenes/Tests/FerdinandWorld/Chunks/](Assets/Game/Scenes/Tests/FerdinandWorld/Chunks)`FerdinandChunk_{x}_{y}.unity`, x 0–3, y 0–1 | 4 × 2, 500 m cells, origin (2000, 0, 1000) | **8** | [FerdinandWorldStreamingConfig.asset](Assets/Game/Settings/FerdinandWorldStreamingConfig.asset) |

Chunk scene count on disk matches the config chunk count and the build-settings entry count exactly for both worlds (48/48/48 and 8/8/8).

## Build settings order

| Index | Entry |
| --- | --- |
| 0 | `Core/Bootstrap` |
| 1 | `Core/MainMenu` |
| 2–6 | `Tests/Blocking test`, `Aleksander test scene`, `Tommy test scene`, `Emil test scene`, `Marius test scene` |
| 7 | `world/persistentScene` |
| 8–9 | `Interiors/AlgeaCave`, `Interiors/SandstoneCaveInterior` |
| 10–57 | `world/Chunks/Chunk_0_0` … `Chunk_7_5` (x-major: all six y for x=0, then x=1, …) |
| 58 | `Minigames/MinigameArena` |
| 59 | `Tests/Ferdinand_Test_world` |
| 60–67 | `Tests/FerdinandWorld/Chunks/FerdinandChunk_0_0` … `FerdinandChunk_3_1` |

All 68 entries have `enabled: 1`. Five personal test scenes (indices 2–6) ship in every build — they push persistentScene off a low index and bloat player builds.

## Loaded-by-name audit

| Name | Loaded from | In build? |
| --- | --- | --- |
| `MainMenu` | `SessionExit.MenuSceneName`; [MatchResultUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MatchResultUI.cs) fallback; `Main Menu` SceneReference | yes (1) |
| `persistentScene` | `GameScene` SceneReference → [MainMenuUI.cs](Assets/Game/Scripts/Presentation/UI/Pages/MainMenuUI.cs); `SaveHotkeys.worldSceneName`; [AutotestRunner.cs](Assets/Game/Scripts/Core/Multiplayer/Autotest/AutotestRunner.cs); `ChunkStreamingProbeMenu` | yes (7) |
| `MinigameArena` | `Minigame` SceneReference → `MainMenuUI` additive load + `NetworkGameManager.PendingSceneNameToWaitFor` | yes (58), **but empty** |
| `AlgeaCave`, `SandstoneCaveInterior` | `InteriorScene` assets in [Assets/Game/Resources/Interiors/](Assets/Game/Resources/Interiors) → `InteriorManager` | yes (8, 9) |
| `Chunk_{x}_{y}`, `FerdinandChunk_{x}_{y}` | `chunks[].sceneName` in the two streaming configs → `WorldStreamer.ExecuteLoad` | yes (10–57, 60–67) |
| `Blocking test`, `Ferdinand_Test_world` | `SceneReference` assets only; no C# path reaches them at runtime | yes (2, 59) |

No scene is loaded by a string that is missing from build settings. [InteriorScene.cs](Assets/Game/Scripts/Core/SceneManagement/Interiors/InteriorScene.cs) already validates its own target against `EditorBuildSettings.scenes` and warns; nothing does the equivalent for chunks or for `SceneReference`.

## Gotchas

1. **MinigameArena is an empty scene.** `SceneRoots: m_Roots: []`, 3539 bytes. It was 14 754 248 bytes until commit `7cbccf9f` ("chore: update .gitignore and remove obsolete terrain data assets…") reduced it to a bare scene. Nothing rebuilds it at runtime, so the deathmatch route loads a void additively over persistentScene. Restore from `7cbccf9f^` before touching the minigame.
2. **Casing drift exists right now.** Disk and git both say `Assets/Game/Scenes/world/` (lowercase); [WorldStreamingConfig.asset](Assets/Game/Settings/WorldStreamingConfig.asset) stores all 48 `scenePath` values as `Assets/Game/Scenes/World/Chunks/…` (capital W), as does the comment in [WorldStreamingConfig.cs:130](Assets/Game/Scripts/World/Streaming/Core/WorldStreamingConfig.cs). `scenePath` is editor-only (NavMesh baker, staleness check, `WorldStreamerEditor`), so client joins survive — but `AssetDatabase.LoadAssetAtPath` on those paths can resolve null and the NavMesh baker will silently *skip every chunk*. Fix the asset, not the folder.
3. **NGO hashes scene PATHS, case-sensitively** (`XXHash32` of the full path, resolved from each machine's on-disk casing, not from the `path:` string in EditorBuildSettings). Two machines whose folder casing differs compute different hashes and fail to sync with `Scene Hash N does not exist in the HashToBuildIndex table`. `core.ignorecase = true` hides this from `git status`. Never rename a scene folder's case casually; if you must, do it index-only and have everyone verify.
4. **The casing repair tooling described in the team's notes is not present on this branch** — no `Tools/fix-asset-casing.sh`, no `.githooks/`, no `AssetCasingGuard.cs`. There is nothing automatically checking for drift.
5. **Build index 0 and 1 are load-bearing.** `Bootstrapper` hardcodes both. Insert new scenes at the end, never at the top.
6. **Adding a chunk scene means three edits, not one**: the `.unity` file, the config's `chunks[]` entry, and build settings. A chunk missing from build settings fails only over the network, and only for the client.
7. `Assets/_Recovery/0.unity` is a duplicate Bootstrap that Unity's crash recovery left behind; it is not in the build and should be deleted.
8. Five personal test scenes are in build settings. They are shipped content today.

## Extending

1. Create the scene under the right folder: `Core/` (flow), `Interiors/` (additive interior), `Minigames/`, `Tests/`, `Utility/` (editor tooling). Match existing casing exactly.
2. Add it to **the end** of [EditorBuildSettings.asset](ProjectSettings/EditorBuildSettings.asset) with `enabled: 1`. Never reorder indices 0–1.
3. If any C# or asset will name it, wire it through a `SceneReference` asset in [Assets/Game/Scenes/References/](Assets/Game/Scenes/References) rather than a bare string literal.
4. Decide the load mode: additive over `persistentScene` (interiors, arenas, chunks) or `Single` (only Bootstrap → MainMenu → world). Additive is the default; a `Single` load tears down the managers.
5. Load it through `NetworkManager.Singleton.SceneManager.LoadScene` when `Network.IsNetworked`, plain `SceneManager` otherwise — copy the branch in [WorldStreamer.ExecuteLoad](Assets/Game/Scripts/World/Streaming/Core/WorldStreamer.cs) and handle `SceneEventProgressStatus.SceneEventInProgress` by retrying.
6. For an interior, add an `InteriorScene` asset under [Assets/Game/Resources/Interiors/](Assets/Game/Resources/Interiors); it self-validates against build settings and warns in the Console.
7. For a chunk, register it in the world's `WorldStreamingConfig` with matching `sceneName`, `scenePath` (**check the folder casing**), `gridCoord` and `worldBounds`.
8. Verify **on a real client**, not just the host — a missing or mis-cased scene path fails only there.
