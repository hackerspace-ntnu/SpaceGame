---
system: Environment
layer: world
summary: Sandstorms, volumetric fog and clouds, sky time-of-day, and the URP render features that draw them
paths:
  - Assets/Game/Scripts/World/Environment/
  - Assets/Game/Art/Shaders/Environment/
  - Assets/Game/Editor/Environment/
  - Assets/Game/Settings/PC_Renderer.asset
  - Assets/Game/Prefabs/Camera/
  - Assets/Game/Scripts/Core/Persistence/Adapters/SandstormSaveable.cs
symptoms:
  - "a render feature is in the renderer asset but never runs"
  - "the screen goes black wherever the ray misses the volume"
  - "the fog or cloud pass looks stippled or dithered"
  - "the sandstorm is in a different place on the host than on the client"
  - "the storm interior renders almost black"
  - "the sun jumps to a different time of day after loading a save"
  - "fog draws over every particle and pane of glass"
  - "distant terrain/objects are missing ahead of me but appear when I turn around"
  - "the ground does not appear when I look at it from far away, even though the chunks are loaded"
  - "the whole world is missing from high up and the console is clean"
  - "Newtonsoft stack-overflows saving a storm"
  - "fog and clouds do not render at all and the renderer logs that it is missing RendererFeatures"
  - "a renderer feature is null even though its sub-asset and script both exist"
  - "the storm's fog and blowing grit are drawn inside a ship or a cave, where the sand cannot reach"
  - "the storm vanishes completely from the window of a ship I am sheltering in"
  - "an authored interior fog volume fades out as soon as I step into the room it is in"
  - "high up during the intro descent the skybox still shows ground-level mountains at eye level"
reads_with: [Persistence, AgentSystem, ArtPipeline]
updated: 2026-09-02
---

# Environment

Sandstorms, volumetric fog/clouds, sky time-of-day and the URP render features that draw them — all derived from one shared clock rather than replicated per frame.

**Scope:** [Assets/Game/Scripts/World/Environment/](Assets/Game/Scripts/World/Environment/) (Sandstorm, Fog, Sky, Visor, ColorGrade, Props), [Assets/Game/Art/Shaders/Environment/](Assets/Game/Art/Shaders/Environment/), [Assets/Game/Editor/Environment/](Assets/Game/Editor/Environment/), [Assets/Game/Editor/Weather/](Assets/Game/Editor/Weather/), [PC_Renderer.asset](Assets/Game/Settings/PC_Renderer.asset) / [Mobile_Renderer.asset](Assets/Game/Settings/Mobile_Renderer.asset)
**Related:** [Sandstorm README](Assets/Game/Scripts/World/Environment/Sandstorm/README.md) · [Fog README](Assets/Game/Scripts/World/Environment/Fog/README.md) · [Persistence.md](Persistence.md) · [AgentSystem.md](AgentSystem.md)

## Model

- **Everything is a pure function of a clock + an anchor.** `StormClock.Shared` = `NetworkManager.ServerTime.Time` in a session, `Time.timeAsDouble` offline. Storm position, intensity, fog drift/churn and sun angle are all recomputed from that. No per-frame weather traffic exists.
- **Two independent clocks over one shared reading.** Weather anchors via [StormClock](Assets/Game/Scripts/World/Environment/Sandstorm/StormClock.cs); the sun anchors itself inside [DayNightCycle](Assets/Game/Scripts/World/Environment/Sky/DayNightCycle.cs). Deliberately separate so restoring a saved storm cannot swing the sun.
- **Replicated:** the ~30-byte [StormInstance](Assets/Game/Scripts/World/Environment/Sandstorm/StormInstance.cs) records (written at birth, never touched again) and two anchor doubles each for weather and sky. Nothing else.
- **Derived locally:** where a storm is, how hard it blows, all rendering, audio, VFX, visibility, AI sight factor.
- **One shape function drives damage *and* pixels.** [StormShape](Assets/Game/Scripts/World/Environment/Sandstorm/StormShape.cs) (C#) must stay identical to the shape in [SandstormVolume.hlsl](Assets/Game/Art/Shaders/Environment/SandstormVolume.hlsl); only the eroding noise is GPU-only.
- **Fog and clouds hold zero runtime state** — authored in scenes, so they need no netcode and no saver at all.
- **Shelter is one volume with two consumers.** `SandstormShelter` is a point query, not a trigger, and it answers both halves of "inside": the server stops charging storm damage (`SandstormVictim`), and each machine stops drawing the storm interior, its near grit and the volumetric fog for its own camera (`SandstormVisuals`, `FogVolumes.Push`). The rendering scales by the *same* `1 - shelter` the damage does, so what is drawn and what hurts cannot drift apart.
- **Facade discipline:** consumers only ever call [Sandstorms](Assets/Game/Scripts/World/Environment/Sandstorm/Sandstorms.cs). Every query answers sensibly with no manager, no session and no storms.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `Sandstorms` | [Sandstorms.cs](Assets/Game/Scripts/World/Environment/Sandstorm/Sandstorms.cs) | The only public surface: `IntensityAt` / `ExposureAt` / `VisibilityAt` / `SightFactorAt` / `TrySample` / `TrySpawn` |
| `SandstormManager` | [SandstormManager.cs](Assets/Game/Scripts/World/Environment/Sandstorm/SandstormManager.cs) | Owns `NetworkList<StormInstance>` + anchor `NetworkVariable`; resolves storms once per frame (`[DefaultExecutionOrder(-90)]`) |
| `StormInstance` / `StormState` | [StormInstance.cs](Assets/Game/Scripts/World/Environment/Sandstorm/StormInstance.cs) | The wire record; `Evaluate(profile, now)` is the deterministic position+intensity function |
| `StormClock` / `StormClockAnchor` | [StormClock.cs](Assets/Game/Scripts/World/Environment/Sandstorm/StormClock.cs) | Weather time as `(weather, clock)` anchor pair; re-states itself when the clock source changes identity |
| `StormShape` / `StormFootprint` | [StormShape.cs](Assets/Game/Scripts/World/Environment/Sandstorm/StormShape.cs) | Cell/Wall density, feathering, `HeadingFromDegrees` (0=+Z, 90=+X) |
| `StormNoise` | [StormNoise.cs](Assets/Game/Scripts/World/Environment/Sandstorm/StormNoise.cs) | Integer avalanche hash — `Mathf.PerlinNoise` has no cross-platform promise |
| `SandstormProfile` / `SandstormCatalog` | [SandstormProfile.cs](Assets/Game/Scripts/World/Environment/Sandstorm/SandstormProfile.cs), [SandstormCatalog.cs](Assets/Game/Scripts/World/Environment/Sandstorm/SandstormCatalog.cs) | Per-kind tuning; catalog turns a profile into a `byte` index for the wire (max 256) |
| `SandstormDirector` / `SandstormZone` | [SandstormDirector.cs](Assets/Game/Scripts/World/Environment/Sandstorm/SandstormDirector.cs), [SandstormZone.cs](Assets/Game/Scripts/World/Environment/Sandstorm/SandstormZone.cs) | Server-only weighted roll on an interval / a permanently parked hazard storm |
| `SandstormVisuals` | [SandstormVisuals.cs](Assets/Game/Scripts/World/Environment/Sandstorm/Rendering/SandstormVisuals.cs) | Client-only; owns all 3 layers, publishes `CameraDensity` and `PushSkyLight()` |
| `SandstormWall` / `SandstormVfx` | [SandstormWall.cs](Assets/Game/Scripts/World/Environment/Sandstorm/Rendering/SandstormWall.cs), [SandstormVfx.cs](Assets/Game/Scripts/World/Environment/Sandstorm/Rendering/SandstormVfx.cs) | Closed bounding shell per storm; near-grit VFX driven by one float |
| `SandstormVictim` / `SandstormShelter` / `SandstormProtection` | [Effects/](Assets/Game/Scripts/World/Environment/Sandstorm/Effects/) | Server-only damage ticks; local-space shelter box + doors; one-float gear via `ISandProtection` |
| `FogVolumes` | [FogVolumes.cs](Assets/Game/Scripts/World/Environment/Fog/FogVolumes.cs) | Static registry; uploads the 8 nearest volumes + 8 nearest lights as shader globals |
| `FogVolume` / `FogLight` / `FogShapeKind` | [Fog/](Assets/Game/Scripts/World/Environment/Fog/) | Authored body of air (Ellipsoid/Box/Cylinder/GroundLayer); opt-in lamp |
| `CloudLayer` | [CloudLayer.cs](Assets/Game/Scripts/World/Environment/Sky/CloudLayer.cs) | One per scene; spherical shell parameters, pushes `_VolumetricCloudFade` |
| `DayNightCycle` / `SkyNetwork` | [DayNightCycle.cs](Assets/Game/Scripts/World/Environment/Sky/DayNightCycle.cs), [SkyNetwork.cs](Assets/Game/Scripts/Core/Multiplayer/Joining/SkyNetwork.cs) | Hour from clock+anchor; SkyNetwork carries the anchor only (Sun prefab has no NetworkObject) |
| `WindField` / `ClothWindDriver` | [WindField.cs](Assets/Game/Scripts/Vehicles/DuneFoil/Core/WindField.cs), [ClothWindDriver.cs](Assets/Game/Scripts/Presentation/Cloth/ClothWindDriver.cs) | The only wind source; resolved **reflectively** across the asmdef boundary |
| `StructureAmbientMotion` | [StructureAmbientMotion.cs](Assets/Game/Scripts/World/Environment/Props/StructureAmbientMotion.cs) | Procedural idle motion on bone-parented props (no clips, per-instance phase) |

## Shaders & render features

| Asset | File | Purpose |
| --- | --- | --- |
| `SandstormRenderFeature` | [SandstormRenderFeature.cs](Assets/Game/Scripts/World/Environment/Sandstorm/Rendering/SandstormRenderFeature.cs) | Interior fog + composite warp/grain. Skipped unless `SandstormVisuals.CameraDensity > 0.002` |
| `FogRenderFeature` | [FogRenderFeature.cs](Assets/Game/Scripts/World/Environment/Fog/Rendering/FogRenderFeature.cs) | Single march over ≤8 volumes + bilateral composite. Skipped when `FogVolumes.Push` returns 0 |
| `VolumetricCloudsRenderFeature` | [VolumetricCloudsRenderFeature.cs](Assets/Game/Scripts/World/Environment/Sky/VolumetricCloudsRenderFeature.cs) | Cloud shell march at `AfterRenderingSkybox`. Skipped when no `CloudLayer.Active` |
| `GlassDistortionRenderFeature` | [GlassDistortionRenderFeature.cs](Assets/Game/Scripts/World/Environment/Visor/GlassDistortionRenderFeature.cs) | Visor lens warp + chromatic aberration; play-mode only, `RuntimeEnabled` gate |
| `PastelQuantizeRenderFeature` | [PastelQuantizeRenderFeature.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PastelQuantizeRenderFeature.cs) | Posterises the finished frame to the nearest of ≤128 pastel colours (Oklab distance; the ~100-colour default is an Oklch lattice generated by [PastelPalette.cs](Assets/Game/Scripts/World/Environment/ColorGrade/PastelPalette.cs)); `AfterRenderingPostProcessing` so it sees tonemapped LDR; Game cameras only; installed **inactive** via `SpaceGame ▸ Environment ▸ Install Pastel Quantize Filter`, opt-in via the checkmarked `SpaceGame ▸ Environment ▸ Pastel Quantize Filter` menu item |
| `SpaceGame/Sandstorm` | [Sandstorm.shader](Assets/Game/Art/Shaders/Environment/Sandstorm.shader) | Fullscreen storm interior (pass 0 march, pass 1 composite) |
| `SpaceGame/SandstormWall` | [SandstormWall.shader](Assets/Game/Art/Shaders/Environment/SandstormWall.shader) | Back-face shell, depth test off, march clipped by scene depth |
| `SpaceGame/VolumetricFog` | [VolumetricFog.shader](Assets/Game/Art/Shaders/Environment/VolumetricFog.shader) | Volume march + 3×3 depth-aware upsample |
| `SpaceGame/VolumetricClouds` | [VolumetricClouds.shader](Assets/Game/Art/Shaders/Environment/VolumetricClouds.shader) | Spherical-shell cloud march |
| `VolumetricCore.hlsl` | [VolumetricCore.hlsl](Assets/Game/Art/Shaders/Environment/VolumetricCore.hlsl) | Shared physics: Beer-Lambert, 3-term multi-scatter, Henyey-Greenstein, powder, `VolJitter` |
| `SandstormVolume.hlsl` / `VolumetricFog.hlsl` | [SandstormVolume.hlsl](Assets/Game/Art/Shaders/Environment/SandstormVolume.hlsl), [VolumetricFog.hlsl](Assets/Game/Art/Shaders/Environment/VolumetricFog.hlsl) | Per-effect shape + multi-volume mixing |
| `Custom/DesertSkybox` | [DesertSkybox.shader](Assets/Game/Art/Shaders/Environment/DesertSkybox.shader) | Sky gradient; stands down its painted dust bands via `_VolumetricCloudFade`; altitude-aware — over the `_AltitudeFadeStart`→`_AltitudeFadeEnd` camera band the mountains and dust fade out, the horizon sinks by `_AirborneHorizonDrop`, and a `_GroundColor*` haze fills the lower hemisphere |
| `Hidden/PastelQuantize` | [PastelQuantize.shader](Assets/Game/Art/Shaders/Environment/PastelQuantize.shader) | Fullscreen nearest-neighbour palette snap; its `LinearToOklab` must stay in lockstep with `PastelPalette.LinearToOklab`, and its palette arrays are set from C# every frame (material array sizes freeze on first set, so they are always padded to 128) |
| `Custom/EVA_Visor` / `Hidden/GlassLensDistortion` | [EVA_Visor.shader](Assets/Game/Art/Shaders/Environment/EVA_Visor.shader), [GlassLensDistortion.shader](Assets/Game/Art/Shaders/Environment/GlassLensDistortion.shader) | Helmet glass, flare, lens warp |
| `SpaceGame/ClothWind` | [ClothWind.shader](Assets/Game/Art/Shaders/Effects/ClothWind.shader) | Vertex-deformed capes/sails driven by `WindField` |

## Flows

**Storm lifecycle**
1. Server: `SandstormDirector` rolls a weighted profile on an interval, or `SandstormZone` registers/`TryAdopt`s its parked storm at startup.
2. `SandstormManager.TrySpawn` writes one `StormInstance` (id, profile byte, seed, origin XZ, bearing, `StartTime` = weather time, duration) into the `NetworkList`.
3. Every machine mirrors the list via `OnListChanged` — a late joiner gets the filled list plus the anchor `NetworkVariable` and lands in identical weather.
4. Each frame `EnsureResolved()` evaluates each record once into a `ResolvedStorm` (footprint + intensity); every consumer reads that one snapshot, so damage and pixels agree.
5. `SandstormVictim` (server only) ticks damage on `exposure = density × (1 − shelter) × (1 − protection)`; `SandstormVisuals`, `SandstormAudio`, `SandstormVfx` read the same snapshot client-side.
6. `Update` collects expired ids (`IsExpired`) into a scratch list first, then removes — removing mid-iteration would skip entries.

**Per-frame volumetric march** (fog; clouds and storm are the same shape)
1. `AddRenderPasses`: bail if no material, wrong camera type, or nothing to draw (`FogVolumes.Push(...) == 0`).
2. `Push` sorts registered volumes by *distance to surface* (`centre distance − boundingRadius`), uploads the nearest 8 as fixed-length global arrays.
3. Pass 0 marches into a reduced-resolution `R16G16B16A16_SFloat` target: rgb = scattered colour, **a = coverage**.
4. Pass 1 composites at full resolution — 3×3 bilateral (depth-weighted) upsample, then `resources.cameraColor = output`.
5. `AllowPassCulling(false)` + `AllowGlobalStateModification(true)` are both required; the depth and march textures are bound as globals inside `SetRenderFunc`.

## Multiplayer

- **Payload:** one `StormInstance` per live storm (`int` + `byte` + `uint` + 2 floats + float + double + float ≈ 30 B), written once at birth, removed at death. Plus `StormClockAnchor` (`bool` + 2 doubles) and `SkyNetwork`'s sky anchor. That is the entire weather bandwidth.
- **Derived on every machine:** centre (`origin + heading × speed × age + across × wander`), intensity (`intensityOverLife(age/duration) × gust`), all rendering, audio and visibility.
- Determinism is the contract: `StormNoise` (not `Mathf.PerlinNoise`), and [SandstormTests.cs](Assets/Game/Editor/Tests/SandstormTests.cs) is the guard.
- Damage is **server-only**; visuals are authoritative over nothing — delete `SandstormVisuals` and a dedicated server runs the same scene.
- Fog volumes and cloud layers replicate **nothing** — scene-authored, motion derived from `Sandstorms.Now`.

## Persistence

- [SandstormSaveable](Assets/Game/Scripts/Core/Persistence/Adapters/SandstormSaveable.cs), key `"weather"` (global saver, on the `SandstormManager`/`NetworkGameManager` object): weather-clock reading, every storm as a flat `StormRecord`, `nextStormId`, and the director's countdown/active id.
- [DayNightSaveable](Assets/Game/Scripts/Core/Persistence/Adapters/DayNightSaveable.cs), key `"sky"`: `timeOfDay`.
- `StormRecord` deliberately flattens `Vector2` to two floats — Newtonsoft walks `Vector2.normalized` into a `StackOverflowException` without a converter.
- Restore order matters: `Sandstorms.ResetClock()` runs before registration so a quickload cannot inherit the previous world's weather time; `RestoreStorms` also restores `nextId` so restored and freshly-rolled storms cannot collide.
- Fog volumes, `FogLight` and `CloudLayer` hold **no runtime state** — nothing to save.

## Gotchas

- **Renderer feature install is not a list append.** URP keeps `m_RendererFeatures` *and* a parallel `m_RendererFeatureMap` of instance ids; growing one without the other yields a feature that exists in the asset and never runs. Use `SpaceGame ▸ Environment ▸ Install Volumetric Render Features` ([VolumetricSetup.cs](Assets/Game/Editor/Environment/VolumetricSetup.cs)) — idempotent, and also the repair tool. Its `FindRenderers`/`AddFeature` helpers are `internal` so other installers reuse them; [PastelQuantizeSetup.cs](Assets/Game/Editor/Environment/PastelQuantizeSetup.cs) does. **The map entry must be the sub-asset's persistent local file id** — `AddFeature` once wrote `GetInstanceID()`, a transient id, and URP's validation culled the row on the next reload, leaving the feature as a dangling sub-asset that never runs; it now saves first and writes the id from `TryGetGUIDAndLocalFileIdentifier`.
- **`FindAssets` also returns URP's in-package renderer.** Writing there appears to work and is reverted the next time the package resolves; the installer filters to `Assets/`.
- **The march target must have alpha.** Never inherit the camera colour format — URP's 32-bit HDR mode is `B10G11R11_UFloatPack32`; coverage writes vanish silently and the composite reads `a = 1`, painting the screen black wherever the ray missed.
- **Jitter scales by the march target, not the screen.** Fog/clouds use `_FogTexelSize.zw` / `_CloudTexelSize.zw`; a half-res pass jittered against `_ScreenParams` advances the dither two steps per texel and stipples. **[Sandstorm.shader](Assets/Game/Art/Shaders/Environment/Sandstorm.shader) still jitters against `_ScreenParams`** — it predates the fix and is the one place that pattern survives.
- **Never clamp a march to scene depth on a sky pixel.** A sky pixel's depth is the camera's far *plane*, and a plane is nearest along the forward axis — 1000 m at the centre of the screen and past 1600 m in the corner of a 60° view. Clamp to it and the effect is cut hardest exactly where the player is looking, so a distant bank of cloud or wall of sand is visible out of the corner of your eye and **vanishes the moment you turn to face it**, with the cut sweeping across it as the head turns. All four march shaders now branch on `rawDepth` (`<= 0` under `UNITY_REVERSED_Z`) and clamp only where there is geometry. [SandstormWall.shader](Assets/Game/Art/Shaders/Environment/SandstormWall.shader)'s vertex-side far-plane clamp fixes only the *rasterisation* half of this; the depth clamp in the fragment is a separate one.
- **A short far clip is unmasked here — the far plane is now 5000 m, and that is load-bearing.** `RenderSettings.m_Fog` is off and no `FogVolume` or `CloudLayer` is authored in [persistentScene](Assets/Game/Scenes/world/persistentScene.unity), so **nothing fades the far plane out**: whatever it is set to is a hard edge the player can see. It was 1000 m in a 4000 × 3000 m world, which is shorter than the world is wide and shorter than the crash-landing arrival is high — the descent starts at 2200 m and the whole planet was simply clipped away, with a clean console (see [PlayerShip](PlayerShip.md)). Both cameras that a player ever looks through are now **5000 m**, the world's diagonal: [Main Camera.prefab](Assets/Game/Prefabs/Camera/Main%20Camera.prefab) (nested in `PlayerCharacter` → `PlayerCharacterNetworked`) and [Mount Third Person Camera.prefab](Assets/Game/Resources/Cameras/Mount%20Third%20Person%20Camera.prefab). Raising it costs essentially nothing here because **streaming, not clipping, is what bounds the drawn world** — `loadRadius` 1 over 500 m chunks means no terrain exists past ~1414 m of the anchor anyway — and it does not cost depth precision either, because URP is reverse-Z on every platform this ships to. The "plane, not sphere" geometry still applies to ordinary frustum culling: a far plane is nearest along the forward axis, so anything past that *forward depth* pops as the view turns, at 5000 m as it did at 1000. Two stale `1000f` fallbacks survive, in `PackFocusCamera.cs:191` and `ThirdPersonWalkThroughCutscene.cs:51`; they are only reached when there is no player camera at all.
- **Do not "fix" a too-short far clip by switching fog on.** The authored-but-unused fallback in persistentScene is `ExponentialSquared` at density 0.01, which is ~99 % opaque by **215 m** — it would hide far more than the far plane ever did.
- **The upsample must be 3×3.** A 2×2 kernel spans exactly one march texel and passes the jitter straight through.
- **Depth Texture must be on in the URP asset** or both features log a warning and bail.
- **Volume ordering is by render pass event, not list order:** clouds `AfterRenderingSkybox`, fog and sandstorm `BeforeRenderingTransparents`. Fog after transparents draws over every particle and pane of glass.
- **A sub-asset with a missing script takes working features down with it.** `PC_Renderer.asset` carried two dangling rows — `LensDistortionRenderFeature` and `NewURPRenderFeature`, whose script GUIDs are present nowhere in `Assets/` or `Packages/`. They were *not* in `m_RendererFeatures` and looked harmless, but with them in the file `VolumetricCloudsRenderFeature` and `FogRenderFeature` deserialised as **null** — each is the sub-asset that directly follows a broken one — so fog and clouds silently did not render at all, and `ScriptableRendererData.OnValidate` logged `PC_Renderer is missing RendererFeatures / This could be due to missing scripts or compile error`. **That error means a feature is null, not merely that a script is missing**, and URP's own repair could not fix it because `m_RendererFeatureMap` had drifted (a duplicate id and two — `-2066`, `-2068` — matching no sub-asset). The fix is both halves: delete the dead sub-asset blocks *and* rewrite the map so it matches `m_RendererFeatures` element for element. A `ForceUpdate` reimport alone does nothing.
- **[Mobile_Renderer.asset](Assets/Game/Settings/Mobile_Renderer.asset) has only fog and clouds** — no sandstorm, no glass distortion.
- **Assign materials in the feature; never `Shader.Find`** — the shaders get stripped from a build otherwise.
- **`MaxVolumes`/`MaxLights` (8) must match `FOG_MAX_VOLUMES`/`FOG_MAX_LIGHTS`** in the hlsl. Unity fixes a global array's size at first upload and silently truncates a later longer one.
- **Statics survive domain reload.** `FogVolumes`, `SandstormVisuals.CameraDensity` and `StormClock` all reset via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`; a new static here needs the same.
- **`Network.IsNetworked` reads false during teardown** — `StormClock.Sync` is written to survive a spurious clock-source handover by preserving its reading.
- **Promotion iterates a snapshot.** Each `NetworkList.Add` fires `OnListChanged`, which rebuilds `records` underneath the loop; the same applies to removals in `Update`.
- **A sheltered camera has to be told it is NOT in the storm, or the storm disappears entirely.** `SandstormVisuals.CameraDensity` is now `sample.Exposure` (density after shelter), which is what skips the fullscreen pass indoors — but the silhouette shell *dissolves* as the camera enters a storm, and it dissolves on the density at the eye. Left unsheltered, standing in a ship in the middle of a storm showed neither the interior fog (killed by the shelter) nor the wall outside the window (dissolved by a density the shelter was keeping off you). Both `UpdateSilhouettes` and `UpdateFog` are handed the same `exposed` factor for that reason: the shell stays up, the interior goes away, and the storm is visible through the glass while the cabin air is clear.
- **Fog thinning under shelter is opt-out per volume.** `FogVolume.thinsUnderShelter` (default on) is right for weather — outdoor air does not follow you indoors — and **wrong for an authored interior atmosphere**, which is exactly the fog that must still be there once you step inside. `FogVolumes.Push` drops a volume whose sheltered density reaches zero rather than uploading it at zero, so a fully sheltered viewer skips the march entirely instead of paying for one that draws nothing. Clouds are deliberately untouched: they are a sky shell at `AfterRenderingSkybox`, the roof already occludes them, and suppressing them would empty the sky seen through a canopy.
- **A `SandstormShelter` is a box, not a trigger** — a live trigger on a drivable vehicle would enter every `SceneTransition` and `VolumeTrigger` it drove through.
- **`SandstormVisuals.PushSkyLight` is load-bearing.** A fullscreen blit has no per-draw SH and the shell has probes off; without it the storm interior renders near-black.
- **The skybox's painted vista is anchored to VIEW angles, not world height.** `_MountainHeight`, `_DustHeight` and `_HorizonHeight` are view-direction Y thresholds, so on their own the mountains and dust bands sit at the same angular height from any altitude — a camera 2 km up (the crash-landing descent) still saw a ground-level sky with peaks beside it at eye level. The shader now reads `_WorldSpaceCameraPos.y` and fades that vista out (and a below-horizon ground haze in) across the airborne band; any **new** painted band added to this shader must also be multiplied by `1 - airborne` or it will hang in the sky during flight. This is per-camera local presentation — nothing to sync, nothing to save.
- **Wind has no coupling to storms.** `WindField` is the DuneFoil's, reached reflectively from `ClothWindDriver`; a storm does not move a cape or a sail.

## Extending

**A new weather event (new kind of storm)**
1. Duplicate a `SandstormProfile` asset and tune shape / motion / lifetime / gameplay / look. No code.
2. Add it to the `SandstormCatalog` assigned on the `SandstormManager` — a profile missing from the list cannot be spawned (and the byte index caps at 256).
3. To have it roll naturally, add a weighted row to the `SandstormDirector`; to park it, drop a `SandstormZone` with a non-zero seed so `TryAdopt` can recognise it after a load.
4. New *gameplay* reactions are new files calling `Sandstorms.*` — never new fields on the manager.
5. If the event needs state the shape function cannot derive, extend `StormInstance` (keep it a value struct), extend `NetworkSerialize`, extend `SandstormSaveable.StormRecord`, and add a determinism case to `SandstormTests.cs`.

**A new volumetric volume**
1. If an existing `FogShapeKind` fits: drop a `FogVolume` in the scene, set shape/look/detail/motion. Nothing to register or wire.
2. For a genuinely new shape, add the enum case **at the end** of [FogShapeKind.cs](Assets/Game/Scripts/World/Environment/Fog/FogShapeKind.cs) (the numbers are the shader contract — reordering changes every authored volume), add the matching branch in [VolumetricFog.hlsl](Assets/Game/Art/Shaders/Environment/VolumetricFog.hlsl), and evaluate it in the volume's own unit space so transform rotation and scale come free.
3. Raising the volume budget means changing `FogVolumes.MaxVolumes` **and** `FOG_MAX_VOLUMES` together, and accepting linear shader cost.
4. Verify in `SpaceGame ▸ Environment ▸ Build Fog Gallery Scene` ([FogGallerySceneBuilder.cs](Assets/Game/Editor/Environment/FogGallerySceneBuilder.cs)) — walk into it, and check overlap, since every claim here is a claim about a viewing angle.
5. A wholly new effect: add a `ScriptableRendererFeature` that includes `VolumetricCore.hlsl`, bail early when there is nothing to draw, use an alpha-bearing march target, jitter against your own texel size, composite 3×3 — then extend `VolumetricSetup.Install` rather than hand-editing the renderer asset.
