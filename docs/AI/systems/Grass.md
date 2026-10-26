---
system: Grass
layer: presentation
summary: Stylized instanced grass grown from painted terrain layers — one GrassField, several biome presets
paths:
  - Assets/Game/Scripts/World/Environment/Foliage/
  - Assets/Game/Scripts/World/Environment/Scatter/
  - Assets/Game/Art/Shaders/Environment/StylizedGrass.shader
  - Assets/Game/Art/Shaders/Environment/StylizedGrass.hlsl
  - Assets/Game/Art/Shaders/Environment/GrassCull.compute
  - Assets/Game/Art/Shaders/Environment/GrassShared.hlsl
  - Assets/Game/Prefabs/Environment/Nature/GrassField.prefab
symptoms:
  - "the grass is twice as dense as it should be"
  - "I painted the grass layer and nothing grows"
  - "painting a Ground_ layer in gameScene shows no change on the terrain and grows no grass"
  - "the grass grows in squares"
  - "grass fields are invisible from far away"
  - "grass only renders close to the player and disappears a short way off"
  - "grass spawns right in front of me with a visible seam"
  - "the terrain is a completely different colour where grass has not loaded yet"
  - "only one painted grass biome fills in; another stays sparse near the camera"
  - "grass does not grow on a world chunk that just streamed in"
  - "grass sits on the ground like a darker carpet"
  - "every grass biome sways with the same wind no matter what I set on its profile"
  - "the game stutters when flying fast or teleporting over grass"
  - "Collection was modified; enumeration operation may not execute in GrassBiomeStream.Harvest"
  - "no grass at all and the console says the field needs GrassCull.compute"
  - "grass shows in the main camera but also costs frame time in a reflection or portal camera"
  - "the grass waves roll past too fast, in regular stripes"
reads_with: [GrassBiomes, Rocks, Foliage, Environment, NoiseHeightmap, WorldStreaming]
updated: 2026-10-25
---

# Grass

Grass is drawn, never placed: a [GrassField](Assets/Game/Scripts/World/Environment/Foliage/GrassField.cs)
scatters tufts over **every loaded Unity Terrain** wherever a biome's `TerrainLayer` is painted, and
draws them GPU-instanced with the `SpaceGame/StylizedGrass` shader. To place grass, paint the
biome's ground layer with the terrain brush.

## Model

- **Biome** = one entry on `GrassField.biomes`: a `TerrainLayer` paired with a
  [GrassFieldProfile](Assets/Game/Scripts/World/Environment/Foliage/GrassFieldProfile.cs). A null
  layer grows that biome everywhere on every terrain.
- **Profile** (ScriptableObject) = shape, density, height range + height clumping, slope limit,
  distance tiers. **Colours live on the profile's material**, not the profile.
- **Distance** (meadow; tall/tiny in [GrassBiomes.md](GrassBiomes.md)): every tuft out to
  `fullDensityDistance` 30 m, then density `(full / r)^densityFalloff` (2.6), survivors widened up to
  `maxWidthBoost` 3×; sinking from 80 % of `drawDistance` 300 m. Detail: full tuft to 8 m, one-segment
  blades to 20 m, flat 3-blade tuft to 40 m, then **one camera-facing triangle** to the horizon.
- **The 25 shipped biomes**, their sizes and colours: [GrassBiomes.md](GrassBiomes.md).

## Key types

| Type | Role |
| --- | --- |
| [GrassField](Assets/Game/Scripts/World/Environment/Foliage/GrassField.cs) | MonoBehaviour, `[ExecuteAlways]`. Biome list, `cullShader` (**GrassCull.compute**), water cut-off (`minimumHeight`), seed, scatter budget, **wind settings**. Hooks `beginCameraRendering` (Game + SceneView cameras whose culling mask has its layer). Also publishes the wind, clock and benders as `_Foliage*` globals for [Foliage](Foliage.md). |
| [GrassBiomeStream](Assets/Game/Scripts/World/Environment/Foliage/GrassBiomeStream.cs) | One biome: a `ChunkScatterStream<GrassInstance>` with four **storage levels** (1, ¼, 1/16, 1/64 of the tufts) chosen by distance → cull dispatches → 4 indirect draws. Chunks are stored at the level they need **2 chunks closer** (look-ahead). |
| [ChunkScatterStream&lt;T&gt;](Assets/Game/Scripts/World/Environment/Scatter/ChunkScatterStream.cs) | Shared with [Rocks.md](Rocks.md): nearest-first async chunk scatter, per-level pools, re-scatter denser/sparser, eviction, invalidation. Skips its walk while an eye stays in a **settled** chunk (re-walks every 30 ticks). |
| [ChunkInstancePool&lt;T&gt;](Assets/Game/Scripts/World/Environment/Scatter/ChunkInstancePool.cs) | One storage level's GPU buffer of equal-size chunk slots + CPU copies; grows by doubling. |
| [PaintedGround](Assets/Game/Scripts/World/Environment/Scatter/PaintedGround.cs) | Active terrains (`Terrain.GetActiveTerrains` each tick), each slot's paint index per terrain (**by asset reference**, via [GroundPaint](Assets/Game/Scripts/World/Environment/Scatter/GroundPaint.cs): splat layer for grass, detail layer for rocks), changed rects from load/unload/paint/sculpt. Main thread only. |
| [PaintedGroundPatch](Assets/Game/Scripts/World/Environment/Scatter/PaintedGroundPatch.cs) | Immutable copy of heights + one channel's weights under **one chunk**, for a worker. Paint read **bilinear between cell centres** at a noise-warped position; returns the ground normal. |
| [GrassScatter](Assets/Game/Scripts/World/Environment/Foliage/GrassScatter.cs) | Worker-thread jittered grid; **each cell its own seeded generator** (`ScatterCellRandom`), rank drawn first, so a sparse level is an exact subset of a dense one. Coverage = paint × clearing noise. |
| [GrassGroundTint](Assets/Game/Scripts/World/Environment/Foliage/GrassGroundTint.cs) | Per terrain whose material declares `_GrassColorMap`: paint → RGBA map (grass colour, coverage) → terrain **splat property block**. A terrain's **first** map is built at once (≈30 ms), repaints on a worker. Read by `StylizedTerrain`. |
| [GrassInstance](Assets/Game/Scripts/World/Environment/Foliage/GrassInstance.cs) | 32-byte stored tuft; layout is a contract with **GrassCull.compute**. |
| [GrassCull.compute](Assets/Game/Art/Shaders/Environment/GrassCull.compute) | Per tuft per camera: distance, thinning, frustum; animates kept tufts (wind, gusts, flutter, benders, sinking) and appends them to near/mid/far/distant lists; turns distant triangles to the camera. |
| [GrassShared.hlsl](Assets/Game/Art/Shaders/Environment/GrassShared.hlsl) | `GrassVisible` (posed tuft, 64 bytes) + noise + `WindGust` (two drifting octaves) + `WindPush` (warped primary wave + crossing secondary wave, × gust), shared by the compute, the grass shader and StylizedFoliage. |
| [GrassBender](Assets/Game/Scripts/World/Environment/Foliage/GrassBender.cs) | Parts grass around a body; 8 nearest the camera, applied in the compute pass. |
| [GrassTuftMesh](Assets/Game/Scripts/World/Environment/Foliage/GrassTuftMesh.cs) | Near (full blades), mid (one folded segment per blade), far (few flat blades), distant (one triangle) from the profile, or an authored mesh. |

## Flows

1. **Per camera render**: `Rebuild` if dirty (also on any Inspector edit of a used profile) →
   `PaintedGround.TakeChanges` → invalidate overlapping chunks → each biome streams → shared cull
   globals (wind, benders, time, frustum) → each biome records Reset/Cull/WriteArgs into one
   command buffer → `context.ExecuteCommandBuffer` → each biome `RenderMeshIndirect` ×4 → release
   chunks unseen for `chunkLifetimeTicks`.
2. **Streaming** (biome order rotates per tick; chunks nearest first): a biome painted nowhere
   skips its loop; a chunk with no paint above the biome's `MinimumLayerWeight / 2` under it
   (`PaintedGround` keeps a per-cell mask; grass passes the threshold) is settled free. Otherwise one budget unit: `Capture` the ground, `Task.Run` the scatter at the
   level its distance needs. Too sparse for a camera → re-scatter denser at once; two levels too
   dense and unneeded for 8 ticks → sparser. The old chunk draws until the new one lands. A
   finished chunk takes a free slot, else evicts the level's chunk idle longest, else waits 16
   ticks, then the pool doubles.
3. **World chunk streams in**: its terrain appears in `GetActiveTerrains` → its rect invalidated →
   grass scattered there. Unload is the mirror.
4. **Brush stroke**: `TerrainCallbacks.textureChanged` fires unsynched while painting and once
   `synched` when the stroke ends; only then is the stroked rect (±3 texels) re-scattered and the
   terrain's colour map rebuilt. `TerrainData.SetAlphamaps` from code fires the synched callback too.
5. **Distance (per tuft, in the compute)**: kept while `rank < DensityAt(range)`, widened by
   `min(maxWidthBoost, 1/sqrt(density))`; near/mid/far switches jittered ±10 %; sinking from `FadeStart`.

## Performance (M1 Pro, 1080p, editor, grass filling the view)

| Case | GPU + CPU | CPU |
| --- | --- | --- |
| Meadow / VelvetLawn (densest), before heights ×3 | +0.4 / +1.5 ms | < 0.1 ms |
| After ×3: Meadow / MeadowAutumn / TallGrass (eye inside 2.4–4.2 m grass) | +0.5 / +0.8 / **+3.0** ms | < 0.1 ms |
| Four biomes mixed, via the 24-biome prefab (50 m reach) | +0.8 ms | 0.2 ms per camera |
| Four LODs, Meadow 300 m fully painted, settled | **+1.1 ms** | ~0 (walk skipped) |
| Same while the 300 m ring fills (load, teleport) | +2–5 ms for ~6 s | the scatter workers |
| Walking 10 m/s: near+mid tufts vs settled | min 95 %, median 102 % | — |
| Field start-up (rebuild + paint mask + first ground map, 13-layer 512² terrain) | — | ≈ 90 ms once |

Measured by toggling the grass layer in the camera mask every frame and comparing medians — the
editor's frame time drifts 7–19 ms between runs, so whole-run averages are meaningless. The
renderer it replaced cost +5–14 ms (that method also counted its synchronous chunk building).

## Multiplayer

N/A — pure presentation. Every machine derives identical grass from the terrain paint and the seed;
benders are local. Nothing is spawned or networked.

## Persistence

N/A — the grass holds no state. The paint it reads lives in the `TerrainData` asset; trampling
springs back.

## Gotchas

- **One GrassField per world.** It grows on every loaded terrain, so two enabled fields double the
  grass; `OnEnable` logs a warning. Put it in the world's main scene, not in chunk scenes.
- **`StylizedTerrain` (gameScene's NoiseTerrain) ignores the alphamap**; the colour on painted ground
  comes only from `GrassGroundTint`, and only after `Ground_*` paint with `groundTint` > 0. Its
  alphamap is 512² over 8 km — a texel is 15.6 m, which grew grass in **squares** until the paint was
  read bilinear + warped. Paint texel i's centre is at `(i + 0.5) / res`; heights are at `i / (res − 1)`.
- `StylizedTerrain` is shared with the Mars chunk: the tint is off unless `_GrassMapRect.z > 0`.
- **Biomes match by `TerrainLayer` asset, not by index or name.** A terrain painted with a
  *duplicate* of `Ground_Meadow` grows nothing. Layer order on the terrain does not matter.
- `minimumHeight` is a **world** height (MeadowShowcase: 7.25, the pond surface). Grass below it
  never grows — a terrain placed low silently stays bald.
- **Needs compute shaders and `cullShader` set** (the prefab has it). Without either, `Rebuild` logs
  an error and draws nothing.
- **A camera draws grass only if its culling mask has the field's layer.** That is the switch for
  keeping grass out of reflection, portal or minimap cameras — each one re-culls and re-draws it.
- **Three contracts move together:** `GrassInstance.cs` ↔ `GrassCull.compute`'s `GrassInstance`;
  `GrassVisible` in `GrassShared.hlsl` is shared by compute and shader; the material floats the
  compute reads are listed in `GrassBiomeStream.MaterialFloatIds`.
- **The scatter runs on worker threads.** `GrassScatter` and `PaintedGroundPatch` must never touch a
  Unity object; anything they need is captured on the main thread first.
- **`ChunkScatterStream.scratch` is iterated by `Harvest`**; eviction runs inside that loop, so it
  must remove through `Remove`, never `RemoveAll(scratch)` ("Collection was modified" on teleport).
- Wind response (`referenceWindSpeed`, fallback bearing/speed, `maxWindScale`) is on the
  **GrassField** — one `_GrassWind` for all biomes. Per-biome sway is the material's `_WindStrength`.
- **Wind pace lives on the materials** (`Mat_Grass_*` and `Mat_Foliage_*` carry the same
  `_WaveLength` 9 / `_WaveSpeed` 1.8 / `_WindFrequency` 0.12 / `_WindSpeed` 4; keep them equal or
  trees lean off-beat with the meadow). `_WindSpeed` is real m/s since 2026-10-25 — before, it was
  noise units/s, so 2.5 swept gusts at ~21 m/s. A single sine along the wind read as fast,
  regular stripes; `WindPush` now warps and crosses the crests. Grass flutter rate is the
  material's `_FlutterFrequency` (±25 % per tuft); a new compute-read float also goes in
  `MaterialFloatIds` and the `StylizedGrass.hlsl` per-material CBUFFER.
- Lighting is per vertex and shadows are **one hard tap**: soft-shadow keywords are deliberately not
  compiled. Tufts never cast shadows.
- **Scatter budget is shared**: biome order rotates per tick and chunks go nearest first; before
  that, one biome whose layer covered the terrain spent every unit on unpainted chunks and starved
  the painted biome. `Carries` must check **paint**, not just that the layer exists.
- Storage levels need `ScatterCellRandom`: a sequential generator would make a sparse
  build a different field, and chunks would visibly re-shuffle as they fill in.
- Workers: half the cores, at least 4. A quarter (2) left a 300 m ring unfilled for 20 s and the
  near chunks waiting — which the player saw as grass spawning in squares right in front of them.
- **Faint brush washes** (1–30 % paint) used to count as painted: hundreds of chunks scattered to
  nothing and starved the near ones. `PaintedGround` now takes each slot's real threshold.
- The ground map used to be built on a worker even the first time: the painted ground showed bare
  Mars red until it landed, i.e. "the terrain is a different colour where grass hasn't loaded".
- `MeadowShowcaseBuilder` instantiates the prefab and loads `Ground_Meadow`; moving either breaks it.
- Seen only in the editor on Metal (24-biome stress test; gameScene's 8 km NoiseTerrain); D3D11/
  Vulkan only compile via `ShaderUtil.Compile*Variant`. **Never run on Windows or in play mode.**

## Extending

- **New biome**: copy a `Mat_Grass_*` material and a `GrassProfile_*`, point the profile at the new
  material, tune colours/heights. Make a `Ground_<Biome>` TerrainLayer whose colour is the grass's
  average colour. Add `{terrainLayer, profile}` to the prefab's `biomes`.
- **Grass on a terrain**: add + paint `Ground_*` layers; one `GrassField.prefab` per scene, `minimumHeight` = water.
- **Cost knobs**, in order of effect: `densityFalloff` (2 → 2.6 halved Meadow's GPU cost at 300 m),
  `distantTuftDistance`, `fullDensityDistance`. `drawDistance` past 300 m is nearly free.
- **Depth priming** (renderer asset, project-wide) would let the forward pass skip shading hidden
  blades; not enabled, because it changes every opaque shader in the project.
