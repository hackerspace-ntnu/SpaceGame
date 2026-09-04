# Known defects

Things found to be broken while reading the code, verified against source, **not fixed**. Each
is described in context in the doc named beside it.

This file exists so an agent does not spend an hour rediscovering a known problem, and does not
"fix" a symptom whose cause is already understood. When you fix one, delete its row in the same
commit — see [CONTRIBUTING.md](CONTRIBUTING.md).

Recorded 2026-09-01 during the full documentation pass.

## Content missing or orphaned

| Defect | Detail | Doc |
| --- | --- | --- |
| The deathmatch arena scene is empty | `MinigameArena.unity` has `m_Roots: []` and no baked NavMesh. It is build index 58 and is loaded additively for the deathmatch route, so that whole path loads a void. Dropped from 14.7 MB to 3.5 KB in commit `7cbccf9f`; restore from `7cbccf9f^`. | [Scenes](systems/Scenes.md), [GameModes](systems/GameModes.md) |
| Trading has no content | The trade flow is code-complete, but no `TraderProfile` asset exists and no prefab or scene references `TraderInteraction` (verified by GUID grep). | [Interaction](systems/InteractionSystem.md) |
| Camera shake is inert | The only `CameraShaker` component sits on a prefab whose GUID has zero references, so every `CameraShakerHandler.Shake(...)` call silently no-ops. That path also never reads the accessibility intensity setting. | [Cutscenes](systems/Cutscenes.md) |
| Crosshair hover never runs | `CrosshairUI.playerInteractor` is unwired on the HUD prefab, so hover-brightening has never executed. | [UI](systems/UI.md), [Interaction](systems/InteractionSystem.md) |

## Wiring that does nothing

| Defect | Detail | Doc |
| --- | --- | --- |
| Layer 6 `Player` is assigned to nothing | No prefab, scene object or runtime code puts anything on it. `Interactor` does `~LayerMask.GetMask("Player")`, which therefore excludes nothing — interaction rays can hit the player's own colliders. The NavMesh baker's `"Player"` exclusion is likewise a no-op. Layer 4 `Water` is unused too. | [ProjectConfig](systems/ProjectConfig.md) |
| Dangling render features | `PC_Renderer.asset` carries two enabled feature rows (`LensDistortionRenderFeature`, `NewURPRenderFeature`) whose script GUIDs exist nowhere in `Assets/` or `Packages/`. | [Environment](systems/Environment.md), [ProjectConfig](systems/ProjectConfig.md) |
| Orphan mobile render pipeline | `Mobile_RPAsset.asset` / `Mobile_Renderer.asset` are referenced by nothing but their own `.meta`, and the single quality level excludes Android and iOS. | [ProjectConfig](systems/ProjectConfig.md) |
| `InputManager` binds a nonexistent action | It binds `"Attack"`, which is not in the action asset. | [CoreServices](systems/CoreServices.md) |
| `CameraShakeIntensity` is not reset | It is missing from `GameSettings.ResetToDefaults`. | [CoreServices](systems/CoreServices.md) |
| Fast enter-play-mode is a no-op | `m_EnterPlayModeOptionsEnabled: 1` with `m_EnterPlayModeOptions: 0` — enabled, but neither reload is actually disabled. | [ProjectConfig](systems/ProjectConfig.md) |
| Arena NavMesh filtering is dead code | `MatchManager`'s island filtering runs against arena content that no longer exists. | [NavMesh](systems/NavMeshSystem.md) |
| `EntitySystemSetup.cs` is a stale comment-only file | It still names six `EntityProfile_*` variants that were deleted. | [EntitySystem](systems/EntitySystem.md) |

## Correctness

| Defect | Detail | Doc |
| --- | --- | --- |
| The grapple's pendulum swing is capped at ~1.5 s | `GrapplingHookArtifact` never overrides `WantsHold`, so `UseChannel.Release` ends its hold stream on the frame the trigger comes up. `Update`'s `holdTimeout` net then fires on a rope the player is deliberately swinging on and drops it, roughly a second and a half into every swing — the mode the item documents at length as "let go to trade the climb for a swing". Not the same bug as the tow's exemption, which is already handled. A fix has to keep the release meaning "stop winching": the stream carries `active: true` only, so `WantsHold => _isGrappling` alone would winch forever. | [Artifacts](systems/Artifacts.md) |
| Sandstorm jitters against the wrong resolution | `Sandstorm.shader` jitters against `_ScreenParams` rather than its own march-target texel size — the exact stipple bug the fog and cloud shaders were already fixed for. | [Environment](systems/Environment.md) |
| Chunk scene-path casing drift | Every `scenePath` in the streaming config and the chunker's output folder say `Scenes/World/Chunks`, while disk, git and build settings are lowercase `Scenes/world/Chunks`. Runtime is unaffected (loads go by scene *name*), but every `AssetDatabase`-driven editor tool — NavMesh baker, staleness check, map baker — silently skips every chunk. No tooling guards against this recurring. | [WorldStreaming](systems/WorldStreaming.md), [Scenes](systems/Scenes.md) |
| World bake diverges from the configured agent | The world NavMesh bake overrides the single Humanoid agent type with slope 60° / climb 0.8 / voxel 0.333, against the project's 45° / 0.75. Editor previews will not reflect what ships. | [NavMesh](systems/NavMeshSystem.md) |
| Physics world bounds are 250 m | Against a 4000 × 3000 m streamed world. Harmless only because the broadphase is SAP and ignores it — changing broadphase would silently break physics outside a 500 m cube. | [ProjectConfig](systems/ProjectConfig.md) |
| Ornithopter prefab path casing | On disk it is `Prefabs/agents/...` while the builder writes `Prefabs/Agents/...`. Works only because macOS is case-insensitive. | [Ornithopter](systems/Ornithopter.md) |
| `RuinScanner.prefab` predates its orientation entry | `ItemPackOrientation` now carries a `Reframe` row for it (-90 about X, lay it on the dial flank, 8x3 cells -> 8x6), but the prefab on disk is still stood on the rear face of its body slab with the emitter at the sky. Run `Tools/SpaceGame/Items/Fix Artifact Pack Orientation` and read its `verify` lines; `PackOrientationTests` fails until then. `PortalGun` was measured and is **not** an orientation defect — it is an extinguisher with a real base ring and stands on it deliberately; its real defect was its mat SIZE and is fixed in `PortalContentBuilder.PackSize`, so that prefab needs `SpaceGame/Portals/Build Portal Gun Content` re-run too. See [Backpack](systems/Backpack.md). | [Backpack](systems/Backpack.md), [Inventory](systems/Inventory.md) |
| `JumpingRod.prefab` predates its own builder | `JumpingRodBuilder` lays the carried rod down (`LieDown`), but the prefab on disk still has `Model` at identity, `CapsuleCollider m_Direction: 1` and `rotationOffset (0,0,0)`. Run `Tools/Items/Build Jumping Rod` and check `m_Direction` becomes 2. | [Inventory](systems/Inventory.md) |

## Hygiene

| Defect | Detail | Doc |
| --- | --- | --- |
| No Git LFS | Against roughly 1.1 GB of `Assets/`. | [ProjectConfig](systems/ProjectConfig.md) |
| Personal test scenes ship | Five (`Blocking test`, and four named `<person> test scene`) occupy build indices 2–6 in every build. | [Scenes](systems/Scenes.md) |
| `Assets/_Recovery/0.unity` is dead | A byte-identical duplicate of `Bootstrap.unity`, a crash-recovery leftover, not in build. | [Scenes](systems/Scenes.md) |
| Stale duplicate network prefab list | A `DefaultNetworkPrefabs.asset` at the repo root duplicates the real list. Confirm which one `NetworkManager.prefab` references before editing either. | [Multiplayer](systems/Multiplayer.md), [Artifacts](systems/Artifacts.md) |
| Orphan model exports | Some exports still write to the pre-restructure `Assets/Models/` path. | [ArtPipeline](systems/ArtPipeline.md) |

## Test coverage gaps

The suite is edit-mode only — there are no play-mode tests at all, so no runtime behaviour is
covered. Subsystems with **zero** tests: procedural world generation (68 source files), all of
`Weapons/`, audio, cutscenes, most of the UI, the backpack display layer, `Vehicles/Rover`, and
agent perception. See [Testing](systems/Testing.md).


defekt: 
grappling hook does not hook where it is pointet at while riding orniecopter, it hoks streaight down. all items must hit whatever it is pointet at when mounted, riding a ornecopter, gliding etc.