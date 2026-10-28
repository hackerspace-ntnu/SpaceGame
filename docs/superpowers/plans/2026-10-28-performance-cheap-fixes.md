# Performance cheap fixes — plan, 2026-10-28

This plan implements the cheap fixes from
[2026-10-28-performance-audit.md](../specs/2026-10-28-performance-audit.md). IDs below refer
to that audit.

A fix counts as **cheap** when all four hold:

- It changes a few files.
- It needs no new art.
- It reverses no recorded design decision.
- It needs no edit to a scene that is open with unsaved changes.

The fixes are grouped by file ownership, so the four groups can be worked on in parallel
without touching the same files.

## Ground rules

- **Do not touch** files with uncommitted work from a parallel session:
  - wild-creature prefabs and builders (`Prefabs/Agents/creatures/Wild/*`, `WildCreatureBuilder.cs`)
  - `GrassField*` and `GrassBiomeStream.cs`
  - `NoiseTerrain_TerrainData.asset`
  - `QualitySettings.asset`
  - aquatic and ocean showcase files
- **Do not edit `gameScene.unity`.** It is open in the Editor with unsaved edits, so any
  gameScene fix is deferred (see the end of this plan).
- Prefabs: before writing one on disk, check that it is not open in Prefab Mode. If a builder
  generates the prefab, change the builder too, or the next rebuild reverts the fix.
- **Multiplayer:** each change says which side of the authority split it runs on.
  Presentation-only changes run on every machine.
- **Persistence:** none of these fixes add runtime state. Each change confirms that it does not
  alter a saved field.
- Each change updates its governing doc in `docs/AI/systems/`: change the affected rows, add a
  Gotcha where something was non-obvious, and bump `updated:`. The index is regenerated once at
  the end.

## Group A — rendering configuration

| Fix | ID | Change |
| --- | --- | --- |
| A1 | R01 | `PC_Renderer` SSAO: Downsample on, Blur quality High → Medium. The source stays DepthNormals (LevelOfDetail.md decision). |
| A2 | R07 | Remove the stray second "Directional Light" from `persistentScene.unity` (scene not open), after checking nothing references it. |
| A3 | C11 | `NetworkManager.prefab`: turn `NetworkMessageMetrics` / `NetworkProfilingMetrics` off, unless a runtime stats overlay reads them. |
| A4 | R11 | Planar water reflection: if `PlanarReflectionManager` exposes a renderer index, give it a lean renderer without SSAO. |

## Group B — agents

| Fix | ID | Change |
| --- | --- | --- |
| B1 | C01 | `AgentGroundConform`: skip the 5-ray probe while the agent is dormant, and re-probe only after the body has moved or turned past a serialized threshold (or after a serialized max interval). This drops the cost for idle and parked agents. |
| B2 | C08 | `UnderTerrainGuard.Update`: run the throttle before `Network.Simulates`. |
| B3 | C14 | Cache animator parameter hashes in `AgentAnimatorDriver` / `AggressionTelegraphModule`. |
| B4 | C04 | Give the Shellback villager and mechanic prefabs `DistanceDormant`, unless a documented reason excludes them. |

## Group C — runtime scripts, UI, persistence, streaming

| Fix | ID | Change |
| --- | --- | --- |
| C1 | C09 | `SaveSerializer.TryReadHeader`: stream only the header with `JsonTextReader` instead of `ReadAllText` + `JObject.Parse` of the whole save. This fixes both the autosave stall and the world-select freeze. |
| C2 | C12 | `HelmetDangerVignette`: cache shader property IDs and skip the work while idle at zero alpha. |
| C3 | C12 | `VisorGauge`: set text only when the shown integer changes. `DuneFoilHUD`: format only when the shown value changes. |
| C4 | C14 | `Interactor`: cache the `LayerMask`. `VisorOverlayController`: throttle the `FindObjectsByType<Light>` retry. |
| C5 | C14 | `SuitOxygen.Publish`: write the NetworkVariables only when a value changes past a serialized resolution. |
| C6 | C14 | `ShipDriftProbe`: install it only in the Editor and in development builds. `VolumeTrigger`: remove the per-second debug log. |
| C7 | C13 | `WorldStreamer`: use High background loading priority while the initial preload or loading screen is up, then Low. |

## Group D — the two cities

| Fix | ID | Change |
| --- | --- | --- |
| D1 | R10 | `DistantGroupSilhouette`: hide beyond a serialized max draw distance. |
| D2 | R14 | Looping engine-smoke ParticleSystems: `AlwaysSimulate` → `Automatic` (prefabs and their builders). Manual-emit systems keep `AlwaysSimulate`. |
| D3 | M01 | The 16 merged far-LOD meshes become non-readable (both the assets and the merge builder), once verified that nothing reads them on the CPU at runtime. |

## Not cheap — deferred, with the reason

| ID | Why not now |
| --- | --- |
| R04 (terrain TwoSided → On), R07 (gameScene light), R09 (occlusion bake), static SkyCity copy | Each needs a `gameScene.unity` save, and the scene has unsaved user edits. Do them after you save. |
| R05 tree prototypes → prefab variants | Edits `NoiseTerrain_TerrainData.asset`, which has uncommitted parallel work. |
| R02 barge material remap, R03 Sky City LOD retune / rebuild, R06 merge character SMRs | Need an asset rebuild and an art review. |
| C04 `AlwaysAnimate` on creatures | Recorded decision: bone-parented rigs freeze off-screen (AgentSystem.md). |
| C02 time-sliced city spawn, C03 AI level of detail, C05 pathing, C07 RigWalker / Sky City collider compounds | Behaviour or physics changes that need Play Mode and client verification. |
| C10 layer matrix / `queriesHitTriggers` | Each layer pair needs proof that nothing relies on the contact or on trigger hits. |
| C11 NetworkTransform compression | Touches 305 prefabs and needs client verification. |
| C12 HUD canvas split, C13 ragdoll prebuild, M02–M05, R08, R12, R13 | Medium-sized refactors. |

## Outcome, 2026-10-28

The type-check is clean: SpaceGame.Persistence, Assembly-CSharp, Assembly-CSharp-Editor and
SpaceGame.Tests.EditMode all compile with 0 errors (a headless Roslyn pass over Unity's own
`.rsp` files). The docs validate with 0 errors.

**Not yet verified:**

- No EditMode test was run.
- Nothing was checked in Play Mode.
- Nothing was checked on a real client.

| Fix | Status | Notes |
| --- | --- | --- |
| A1 | done | SSAO Downsample on, Blur Medium. One renderer serves all PC tiers. |
| A2 | done | 4 YAML docs and the `graphics` child entry removed from `persistentScene`. The gameScene copy is still there. |
| A3 | done | Both metric flags are off. They are not part of the NetworkConfig hash. |
| A4 | skipped | `PlanarReflectionManager` hard-codes `SetRenderer(0)`. The renderer index would have to match across 4 pipeline assets, and someone has to decide which features the reflection keeps. |
| B1 | done | Probe result is cached. Re-probe on 2 cm / 2° / 0.25 s; no interval re-probe while `IsParked`. |
| B2 | done | Throttle runs before `Network.Owns`. |
| B3 | done | Hashes cached. `NpcTaskModule`, `CreatureTrust` and `NestPresentation` still call `SetBoolByName` every frame. |
| B4 | done | `DistanceDormant` added to the Shellback base prefabs (5 prefabs inherit it). The save also dropped stale keys. |
| C1 | done | Header is read by streaming. A truncated body now returns the live header (documented). |
| C2–C6 | done | See the Persistence, Visor, InteractionSystem, Vehicles and Environment docs. |
| C7 | done | Only the server's blocking preload uses High priority. A client's loading screen is unchanged. |
| D1 | done | `maxDrawDistance` 1500 m. It errors if set at or below the despawn radius. |
| D2 | partial | 10 sky engine-smoke systems set to Automatic, and the builder fixed. DesertCrawler's `FX_FootfallDust` is manual-emit; `FX_FarDust` is shared across all strider vehicles and needs its own fix. |
| D3 | done | 16 merged meshes are non-readable, and the baker keeps them that way (tests added). |
