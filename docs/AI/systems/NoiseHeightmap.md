---
system: NoiseHeightmap
layer: world
summary: Edit-time generator writing a whole Terrain from 4-octave Perlin layers: continentalness, erosion, ridges
paths:
  - Assets/Game/Scripts/World/ProceduralGeneration/Heightmap/
  - Assets/Game/Editor/Terrain/NoiseHeightmapGeneratorEditor.cs
  - Assets/Game/Scenes/world/NoiseTerrain.unity
  - Assets/Game/Terrain/NoiseTerrain/
  - Assets/Game/Art/Shaders/Terrain/StylizedTerrain.shader
  - Assets/Game/Settings/SpillexoWorldStreamingConfig.asset
symptoms:
  - "a settlement's flattened ground is gone after regenerating the noise terrain"
  - "noise heightmap mountains have flat tops, or the lowest ground is perfectly flat at 0 m"
  - "a terrain rendered in batch mode is solid magenta beyond about 1 km"
  - "changing octaves on a noise layer made the whole map flat or all mountains"
reads_with: [TerrainGeneration, Scenes, NavMeshSystem]
updated: 2026-10-20
---

# Noise Heightmap

Writes a Unity `Terrain`'s entire heightmap and a slope splat from stacked fractal Perlin layers,
in the editor. Ported from the procedural-playground web generator (github
`ffremming/procedural-playground`, `public/terrain-3D-generation/js/terrain-generation/terrain/noise/`):
same named layers, same per-layer `scale/octaves/persistence/lacunarity/exponent` knobs, same
normalise-to-0..1 then combine approach.

**Scope:** [Heightmap/](Assets/Game/Scripts/World/ProceduralGeneration/Heightmap), [NoiseHeightmapGeneratorEditor.cs](Assets/Game/Editor/Terrain/NoiseHeightmapGeneratorEditor.cs), the [NoiseTerrain](Assets/Game/Scenes/world/NoiseTerrain.unity) scene.

## Model

`SampleHeight(x, z)` in metres, from six [`NoiseLayer`](Assets/Game/Scripts/World/ProceduralGeneration/Heightmap/NoiseLayer.cs)s (4 octaves each by default):

| Layer | Wavelength | Becomes |
| --- | --- | --- |
| `continentalness` | 1800 m | base height through `continentalToHeight` (35 → 160 m): lowland basins vs. highlands |
| `erosion` | 1000 m | *relief* 0..1 through `erosionToRelief`: high erosion = flat plain, low = mountains allowed |
| `peaksAndValleys` | 650 m | folded `1-|2n-1|`, `^ridgeSharpness`, × `mountainHeight` × relief × inland (continentalness `mountainInlandStart..End`) |
| `baseElevation` | 300 m | ± `hillHeight`, damped to `plainHillFraction` on plains |
| `detail` | 70 m | ± `detailHeight`, damped to `plainDetailFraction` on plains |
| `warp` | 900 m | offsets the sample point of the three large layers by up to `warpDistance` |

Erosion is what makes some regions flat and others steep — the Minecraft-style split the
playground calls continentalness / erosion / peaks-and-valleys. Sampling is in **world metres**
(`transform.position` + grid), so two terrains with the same settings side by side join seamlessly.
After the heights, a slope splat: `flatTerrainLayer` below `steepBlendStart`°, `steepTerrainLayer` above `steepBlendEnd`°.

**NoiseTerrain scene:** 8000 × 8000 m at the origin, 600 m tall, 4097² heightmap (Unity's maximum;
~2 m, like the main world's chunks), 512² alphamap (unused by the shader), seed 1 → heights 13–332 m;
slope <5° 19 %, 5–15° 41 %, 15–30° 22 %, >30° 18 %. Its 0–4 km quarter is the original 4 km map
unchanged (same settings, world-space sampling). Peaks above `_MaxHeight` (300) just take the peak colour.
Trilight ambient and the main world's [Skybox.mat](Assets/Game/Art/Materials/Environment/Skybox.mat)
(`Custom/DesertSkybox`, as in `persistentScene`). Ambient stays trilight (persistentScene's is
skybox-driven) because the terrain material was tuned under it.

**It is the playable Spillexo world** (main menu → *Spillexo*; [Scenes.md](Scenes.md)). Its gameplay
objects are a copy of persistentScene's — Managers, gameSystem, graphics (Sun with `DayNightCycle`,
`RenderSettings.sun`), `[SaveSystem]`, NpcWorldSim, Weather, Objectives, ArrivalDirector, InteriorManager,
DialogePanel, Wind, PlayerCharacter, NavMesh — **without** the `WorldStreamer`, the sky fleet, the
near-spawn sandstorm and the map hologram ([WorldStreaming.md](WorldStreaming.md) lists what a
streamerless world loses). Rewired for this map: `SpawnPoint` on flat ground ~160 m from the
settlement, `SandstormDirector` area = the terrain, `MapService.config` empty, `SaveHotkeys` reloads
`NoiseTerrain`. Save identity: [SpillexoWorldStreamingConfig.asset](Assets/Game/Settings/SpillexoWorldStreamingConfig.asset)
(no chunks; identity only; its site catalog `SpillexoWorldStreamingConfigSites.asset` is baked from this scene).
NavMesh: [NoiseTerrain_NavMesh.asset](Assets/Game/Terrain/NoiseTerrain/NoiseTerrain_NavMesh.asset),
baked from the scene ([NavMeshSystem.md](NavMeshSystem.md)). The `settlement` (id `3cea8227…`, stamped 2026-10-20)
owns the bounty hunt `Bounty_NimQuickhands`, whose `BountyHuntSite_NimQuickhands` stands 400 m north-east
([Bounties.md](Bounties.md)).

**Look:** the terrain's material template is
[NoiseTerrain_StylizedMars.mat](Assets/Game/Terrain/NoiseTerrain/NoiseTerrain_StylizedMars.mat), a
saturated stylized Mars on [StylizedTerrain.shader](Assets/Game/Art/Shaders/Terrain/StylizedTerrain.shader).
The shader colours **procedurally from world position and normal — no textures, no splat**:

| Stage | Driven by | Knobs |
| --- | --- | --- |
| 4-stop height ramp valley → mid → high → peak | world Y between `_MinHeight`/`_MaxHeight` (20/300 here), edges jittered by value noise | `_BandSoftness`, `_BandNoise`, `_BandNoiseScale` |
| Macro patches (dust drifts) | 2-octave rotated value noise in XZ | `_ColorMacro`, `_MacroStrength`, `_MacroScale`, `_MacroCoverage` |
| Cliff overlay, layered into strata | `dot(N, up)` between `_SlopeStart`/`_SlopeEnd`; strata are per-layer hashed tones along noise-warped world Y | `_ColorCliff`, `_ColorStrata`, `_StrataStrength`, `_StrataSpacing`, `_StrataEdge`, `_StrataWarp`, `_StrataWarpScale` |
| Lighting | wrapped main + additional lights, flashlight, SH ambient + `_AmbientBoost` + `_ShadowTint` | `_LightWrap`, `_FlatShading` |
| Atmosphere | rim term and distance tint toward `_FogTint` | `_Rim*`, `_Fog*` |

Strata, macro patches and shadow tint default to off, so the older
[StylizedTerrain_Mars.mat](Assets/Game/Art/Materials/Terrain/StylizedTerrain_Mars.mat) (on `Chunk_7_3`
and `ClankerSettlement`) renders as it did before they were added.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `NoiseLayer` | [NoiseLayer.cs](Assets/Game/Scripts/World/ProceduralGeneration/Heightmap/NoiseLayer.cs) | Serialized fBm: `wavelength`, `octaves`, `persistence`, `lacunarity`, `contrast`, `exponent`, `seedSalt`. `Sample(x, z, seed)` → 0..1; per-octave offsets hashed from seed+salt+octave |
| `NoiseHeightmapGenerator` | [NoiseHeightmapGenerator.cs](Assets/Game/Scripts/World/ProceduralGeneration/Heightmap/NoiseHeightmapGenerator.cs) | On a `Terrain`. `SampleHeight`, `Generate()` (heights + splat, returns highest point). Inert at runtime |
| `NoiseHeightmapGeneratorEditor` | [NoiseHeightmapGeneratorEditor.cs](Assets/Game/Editor/Terrain/NoiseHeightmapGeneratorEditor.cs) | **Generate** / **New Seed + Generate**; undoable; saves the `TerrainData` asset |

## Flows

Inspector **Generate** → `Undo.RegisterCompleteObjectUndo(terrainData)` → `Generate()` samples
every heightmap vertex (4097² ≈ 17 M samples, ~27 s, editor blocked) → `SetHeights` → `GetSteepness` per
alphamap texel → `SetAlphamaps` → `AssetDatabase.SaveAssetIfDirty(terrainData)`.

## Multiplayer

The generator: N/A — its output is a `TerrainData` asset every machine loads identically. The Spillexo
world scene is loaded through Netcode like persistentScene (it is in build settings, index 70), and its
gameplay objects follow their own systems' rules; the one difference is the spawn flow skipping the
chunk preload ([Multiplayer.md](Multiplayer.md) Flows).

## Persistence

The generator: N/A — regenerating is a designer action. Spillexo saves are ordinary world saves stamped
with `SpillexoWorldStreamingConfig`'s id, so a Story save is refused in Spillexo and the reverse
(`WorldIdentity.AcceptsConfig`). Both worlds' saves share one folder and one world list; picking the
other world's save shows "This save belongs to a different world".

## Gotchas

- **Heights are clipped flat at both ends of the terrain's range.** `TerrainData` stores 0..1 of `size.y`: above it a flat-topped mountain (Generate logs a `[NoiseHeightmap]` warning with the highest point), below 0 a dead-flat floor with **no** warning. Hills and detail are *signed*, so the lowest `continentalToHeight` key must stay above `hillHeight + detailHeight` — the first build had 12 m against ±30 m hills and its basins came out flat at 0 m.
- **An octave sum bunches around 0.5, which is why `contrast` exists.** Four octaves at persistence 0.5 rarely leave ~0.25..0.75, so curves keyed over 0..1 would only see their middle. Changing `octaves`/`persistence` changes that spread — re-check `continentalToHeight` and `erosionToRelief` afterwards.
- **Wavelengths are in metres, so they must suit the terrain size.** At 2 km with the first defaults (continental 2600 m, peaks 900 m) the whole tile was one mountain range on one plain; the defaults were cut until 4 km shows several distinct ranges. The 8 km map kept them, so it has more ranges of the same size, not bigger regions.
- **Batch-mode camera renders are magenta beyond `basemapDistance` (default 1000 m).** The URP TerrainLit basemap pass is not ready in `-batchmode`; the terrain is fine. Raise `basemapDistance` only for the preview, after saving the scene.
- **The slope splat and the two terrain layers are not what you see.** `StylizedTerrain` ignores the alphamap; the layers stay only because `Generate()` needs two to paint. Retune the look on the material, not the layers.
- **`StylizedTerrain` has no terrain-instancing path.** It reads mesh positions/normals, so the terrain's *Draw Instanced* must stay off (it is).
- **Height bands are in world metres.** `_MinHeight`/`_MaxHeight` must track the generated range; regenerating with taller mountains or moving the terrain shifts every band.
- **Shared shader:** a new `StylizedTerrain` property must default to a no-op, or `StylizedTerrain_Mars.mat` on the main-world chunk changes look silently.
- **Generate wipes a `Settlement`'s terrain sculpting on the same terrain**, with no warning — the settlement still sits where its flattened ground was. Its backup patch holds the pre-sculpt noise heights, so press the settlement's Generate again afterwards (restore, then re-sculpt; seeded by position, same layout). Seen 2026-10-19 on `settlement` at (1560, 75, 838).
- **Beyond 4097² a bigger map means tiles, not a bigger terrain.** Neighbouring terrains with the same settings join without seams. The far corner of this one is 11 km from the origin, already at the edge of float-precision jitter.
- **Spillexo's NavMesh goes stale with every edit to the world.** It is one author-time bake of the scene; regenerating the terrain, placing a settlement or a building leaves agents on the old mesh. Re-bake with `World/Bake Open World Scene NavMesh`, or a settlement's **Generate + Bake World NavMesh** (which routes here in this scene). The 2026-10-19 bake predates the `Workshop` placed afterwards.
- **"Bake World NavMesh" (the Streaming one) bakes the OTHER world.** It reads `WorldNavMesh.asset`'s config — the chunked Story world — whatever scene is open. Spillexo is `World/Bake Open World Scene NavMesh`.
- Saving the scene does not write `TerrainData`; the inspector saves it explicitly. A `Generate()` called from your own code must `SetDirty` + save the asset too.

## Extending

- **On another terrain:** add `NoiseHeightmapGenerator` to any `Terrain` with at least two terrain layers and press Generate. Tune in this order: `continentalToHeight`, `erosionToRelief` (how much is flat), `mountainHeight`/`ridgeSharpness`, then hills and detail.
- **New layer:** a new `NoiseLayer` field with its own unused `seedSalt`, combined in `SampleHeight`. A new world only changes `seed`, never the salts.
- **Using it in a played world:** the world NavMesh must be re-baked after generating ([NavMeshSystem.md](NavMeshSystem.md)), and the scene needs the streaming/manager setup of [Scenes.md](Scenes.md).
