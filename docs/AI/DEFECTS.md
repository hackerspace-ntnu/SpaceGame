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
| Trading has no content | The trade flow is code-complete, but no `TraderProfile` asset exists and no prefab or scene references `TraderInteraction` (verified by GUID grep). | [Interaction](systems/InteractionSystem.md) |
| Camera shake is inert | The only `CameraShaker` component sits on a prefab whose GUID has zero references, so every `CameraShakerHandler.Shake(...)` call silently no-ops. That path also never reads the accessibility intensity setting. | [Cutscenes](systems/Cutscenes.md) |
| Crosshair hover never runs | `CrosshairUI.playerInteractor` is unwired on the HUD prefab, so hover-brightening has never executed. | [UI](systems/UI.md), [Interaction](systems/InteractionSystem.md) |
| Rock prefabs are unusable | Every prefab under `Prefabs/Environment/Nature/Rocks/` has no collider, and the 100× FBX scale sits one level down inconsistently: `BoulderLarge_A`'s mesh child is at 160×, `BoulderSmall_A`'s at 89×, `BoulderSmall_C`'s root at 0.01×. Placed at scale 1 a "large" boulder is 590–860 m across (measured 2026-09-07 when `RobotSettlementGenerator` put three of them over the Clanker settlement, each wider than the town). `BoulderLarge_B` is in one scene today. Rebuild them through a builder before using them anywhere. | [TerrainGeneration](systems/TerrainGeneration.md), [ArtPipeline](systems/ArtPipeline.md) |

## Wiring that does nothing

| Defect | Detail | Doc |
| --- | --- | --- |
| Layer 6 `Player` is assigned to nothing | No prefab, scene object or runtime code puts anything on it. `Interactor` does `~LayerMask.GetMask("Player")`, which therefore excludes nothing — interaction rays can hit the player's own colliders. The NavMesh baker's `"Player"` exclusion is likewise a no-op. Layer 4 `Water` is unused too. | [ProjectConfig](systems/ProjectConfig.md) |
| Dangling render features | `PC_Renderer.asset` carries two enabled feature rows (`LensDistortionRenderFeature`, `NewURPRenderFeature`) whose script GUIDs exist nowhere in `Assets/` or `Packages/`. | [Environment](systems/Environment.md), [ProjectConfig](systems/ProjectConfig.md) |
| Orphan mobile render pipeline | `Mobile_RPAsset.asset` / `Mobile_Renderer.asset` are referenced by nothing but their own `.meta`, and the single quality level excludes Android and iOS. | [ProjectConfig](systems/ProjectConfig.md) |
| `InputManager` binds a nonexistent action | It binds `"Attack"`, which is not in the action asset. | [CoreServices](systems/CoreServices.md) |
| `CameraShakeIntensity` is not reset | It is missing from `GameSettings.ResetToDefaults`. | [CoreServices](systems/CoreServices.md) |
| Fast enter-play-mode is a no-op | `m_EnterPlayModeOptionsEnabled: 1` with `m_EnterPlayModeOptions: 0` — enabled, but neither reload is actually disabled. | [ProjectConfig](systems/ProjectConfig.md) |
| `PatrolRobot 2.prefab` has no builder | Its faction was moved from Humans to Clankers by a one-off `SerializedObject` edit (2026-09-15) because no editor script owns the four `PatrolRobot` prefabs. Nothing will put it back if someone re-authors them by hand. They are slated for deletion once Clankers are client-verified (faction plan Task 6.2). | [AgentSystem](systems/AgentSystem.md) |

## Correctness

| Defect | Detail | Doc |
| --- | --- | --- |
| Restored world entities and old player bodies survive an in-session reload | `SaveNetworking.SpawnIfNetworked` network-spawns every restored runtime record with plain `NetworkObject.Spawn()` (`destroyWithScene: false`), and `SpawnManager`'s `SpawnAsPlayerObject` does the same for player bodies. NGO's `LoadScene(Single)` carries all of them into the new world's `persistentScene`; `WorldSaveStore.SpawnEntities` then skips each record as "already live", so the entity keeps its pre-reload state and leaves its chunk scene. Measured 2026-09-17 over three F9-style reloads: a Battery moved 4 m after the save came back at the moved spot and fell out of the world (y −36, no terrain under `persistentScene`); the 16 Sky City residents, Clankers, dropped items and the `PlayerShip` survived; `PlayerCharacterNetworked` bodies accumulated (4 after 3 reloads, only one the player object). Only the first reload after a fresh spawn looks clean, because `World.Spawn` passes `true`. Not fixed: `destroyWithScene: true` there also changes what a networked chunk unload does to every restored entity (NGO rescues `false` objects into DontDestroyOnLoad and despawns `true` ones), which needs its own verification. | [Persistence](systems/Persistence.md), [Multiplayer](systems/Multiplayer.md) |
| The grapple's pendulum swing is capped at ~1.5 s | `GrapplingHookArtifact` never overrides `WantsHold`, so `UseChannel.Release` ends its hold stream on the frame the trigger comes up. `Update`'s `holdTimeout` net then fires on a rope the player is deliberately swinging on and drops it, roughly a second and a half into every swing — the mode the item documents at length as "let go to trade the climb for a swing". Not the same bug as the tow's exemption, which is already handled. A fix has to keep the release meaning "stop winching": the stream carries `active: true` only, so `WantsHold => _isGrappling` alone would winch forever. | [Artifacts](systems/Artifacts.md) |
| Suit and ship recolours are probably linearised twice | `PaletteRecolor.ToUploadSpace` converts each colour with `.linear` and then hands it to `MaterialPropertyBlock.SetColor`, whose comment says a property block "does NOT" linearise. On Unity 6000.3.11 it does: measured 2026-09-24, `SetColor(0.5)` stores 0.214 in the block. The eyelids made exactly this mistake and rendered visibly darker and more saturated than the skin they were sampled from, fixed by dropping the `.linear`. Not fixed here: the suit and ship livery have been tuned by eye against what they render as today, so removing the conversion lightens every swatch and needs someone to look at the result. | [PlayerCharacter](systems/PlayerCharacter.md), [StylizedEyes](systems/StylizedEyes.md) |
| Sandstorm jitters against the wrong resolution | `Sandstorm.shader` jitters against `_ScreenParams` rather than its own march-target texel size — the exact stipple bug the fog and cloud shaders were already fixed for. | [Environment](systems/Environment.md) |
| Chunk scene-path casing drift | Every `scenePath` in the streaming config and the chunker's output folder say `Scenes/World/Chunks`, while disk, git and build settings are lowercase `Scenes/world/Chunks`. Runtime is unaffected (loads go by scene *name*), but every `AssetDatabase`-driven editor tool — NavMesh baker, staleness check, map baker — silently skips every chunk. No tooling guards against this recurring. | [WorldStreaming](systems/WorldStreaming.md), [Scenes](systems/Scenes.md) |
| `Camera.main` is null while mounted | **Corrected 2026-09-06** — the earlier wording here was wrong on both halves. The player camera is a nested `Main Camera.prefab` instance inside `PlayerCharacter.prefab`, it *is* tagged `MainCamera`, and `PlayerController` activates it for the local player only — so on foot `Camera.main` is this machine's own view, which is what `MapHologramTerrain` already relies on. What is true is the mount case: the orbit camera is explicitly `Untagged` when spawned and the player's own is deactivated while riding, so `Camera.main` is **null** for as long as anyone is mounted. `GravelBlastFx`, `RepulsorGauntletArtifact` and `SuckerPuncherArtifact` then skip their shake entirely. `SnareCatch.Redraw` falls back to `Vector3.back`, which does **not** hide the net — `Net_Cord.mat` is `_Cull: 0` — but does orient every ribbon against a fixed world axis instead of the viewer, so a 0.028 m cord reads as a hairline from many angles. The fix is one shared "camera this machine is drawing from", not three more fallbacks. | [Artifacts](systems/Artifacts.md), [PlayerCharacter](systems/PlayerCharacter.md) |
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

