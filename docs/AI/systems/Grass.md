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
  - "grass changes shape abruptly as it goes in and out of the high-detail range"
  - "tufts pop in and out at full size a little way off as I walk"
  - "the far grass is brighter or darker depending on which way I look"
  - "the grass reads as big triangles or a sawtooth from a distance"
  - "far away the meadow looks like bare tinted ground instead of grass"
  - "a wide dark ring between grass and bare ground when seen from far away"
reads_with: [GrassBiomes, GrassInteraction, Rocks, Foliage, Environment, NoiseHeightmap, WorldStreaming]
updated: 2026-10-27
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
- **Distance** (meadow; others in [GrassBiomes.md](GrassBiomes.md)): all tufts to 30 m, then density
  `(30 / r)^2.6`, survivors widened ≤ 1.5×, each **shrinking away** over the last 25 % of its reach;
  coloured as their **tips only** from 35 to 120 m (`canopyColorStart/End`); sinking from 240 m, gone at
  300 m. Detail: full tuft to 8 m, one-segment blades to 20 m, flat 3-blade tuft to 40 m, then a
  **camera-facing row of flat spikes**, one per far blade, to the horizon.
- **Geomorph**: over the last `lodMorphBand` (30 %) before each switch a tuft eases into the next
  level's shape (UV1/UV2), so the switch shows nothing. One-segment blades are **triangles of the
  leaf's area**; dropped blades narrow to their spine; each far blade becomes its distant spike.
- **Biomes**: [GrassBiomes.md](GrassBiomes.md). **Trails, plant bases, sheen**: [GrassInteraction.md](GrassInteraction.md).

## Key types

| Type | Role |
| --- | --- |
| [GrassField](Assets/Game/Scripts/World/Environment/Foliage/GrassField.cs) | MonoBehaviour, `[ExecuteAlways]`. Biome list, `cullShader` (**GrassCull.compute**), water cut-off (`minimumHeight`), seed, **`densityScale`** (0.6, every biome's tufts/m²), scatter budget, **wind settings**. Hooks `beginCameraRendering` (Game + SceneView cameras whose culling mask has its layer). Also publishes the wind, clock and benders as `_Foliage*` globals for [Foliage](Foliage.md). |
| [GrassBiomeStream](Assets/Game/Scripts/World/Environment/Foliage/GrassBiomeStream.cs) | One biome: a `ChunkScatterStream<GrassInstance>` with four **storage levels** (1, ¼, 1/16, 1/64 of the tufts) chosen by distance → cull dispatches → 4 indirect draws. Chunks are stored at the level they need **2 chunks closer** (look-ahead). |
| [ChunkScatterStream&lt;T&gt;](Assets/Game/Scripts/World/Environment/Scatter/ChunkScatterStream.cs) | Shared with [Rocks.md](Rocks.md): nearest-first async chunk scatter, per-level pools, re-scatter denser/sparser, eviction, invalidation. Skips its walk while an eye stays in a **settled** chunk (re-walks every 30 ticks). |
| [ChunkInstancePool&lt;T&gt;](Assets/Game/Scripts/World/Environment/Scatter/ChunkInstancePool.cs) | One storage level's GPU buffer of equal-size chunk slots + CPU copies + fill time (`_SlotFilled`, rocks only); grows by doubling. |
| [PaintedGround](Assets/Game/Scripts/World/Environment/Scatter/PaintedGround.cs) | Active terrains (`Terrain.GetActiveTerrains` each tick), each slot's paint index per terrain (**by asset reference**, via [GroundPaint](Assets/Game/Scripts/World/Environment/Scatter/GroundPaint.cs): splat layer for grass, detail layer for rocks), changed rects from load/unload/paint/sculpt. Main thread only. |
| [PaintedGroundPatch](Assets/Game/Scripts/World/Environment/Scatter/PaintedGroundPatch.cs) | Immutable copy of heights + one channel's weights under **one chunk**, for a worker. Paint read **bilinear between cell centres** at a noise-warped position; returns the ground normal. |
| [GrassScatter](Assets/Game/Scripts/World/Environment/Foliage/GrassScatter.cs) | Worker-thread jittered grid; **each cell its own seeded generator** (`ScatterCellRandom`), rank drawn first, so a sparse level is an exact subset of a dense one. Coverage = paint × clearing noise. |
| [GrassGroundTint](Assets/Game/Scripts/World/Environment/Foliage/GrassGroundTint.cs) | Per terrain whose material declares `_GrassColorMap`: paint → RGBA map (grass colour **premultiplied** by coverage, linear texture, sRGB values) → terrain **splat property block**. A terrain's **first** map is built at once (≈30 ms), repaints on a worker. Read by `StylizedTerrain`. |
| [GrassInstance](Assets/Game/Scripts/World/Environment/Foliage/GrassInstance.cs) | 32-byte stored tuft; layout is a contract with **GrassCull.compute**. `y` packs tint and ground contact as two halves ([GrassInteraction](GrassInteraction.md)). |
| [GrassCull.compute](Assets/Game/Art/Shaders/Environment/GrassCull.compute) | Per tuft per camera: distance, thinning, frustum; animates kept tufts (wind, gusts, flutter, benders, sinking) and appends them to near/mid/far/distant lists; turns distant triangles to the camera. |
| [GrassShared.hlsl](Assets/Game/Art/Shaders/Environment/GrassShared.hlsl) | `GrassVisible` (posed tuft, 64 bytes) + noise + `WindGust` (two drifting octaves) + `WindPush` (warped primary wave + crossing secondary wave, × gust), shared by the compute, the grass shader and StylizedFoliage. |
| [GrassTuftMesh](Assets/Game/Scripts/World/Environment/Foliage/GrassTuftMesh.cs) | Near (full blades), mid (one folded triangle per blade), far (few flat blades), distant (a row of spikes, normal up) from the profile, or an authored mesh. Each mesh carries the next level's shape in UV1/UV2 for the morph. |

## Flows

1. **Per camera render**: `Rebuild` if dirty (also on any Inspector edit of a used profile) →
   `PaintedGround.TakeChanges` → invalidate overlapping chunks → each biome streams → shared cull
   globals (wind, benders, time, frustum) → trail map update + bind ([GrassInteraction](GrassInteraction.md))
   → each biome records Reset/Cull/WriteArgs into one
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
5. **Distance (compute, per tuft)**: kept while `rank < DensityAt(range)`, shrunk over the last
   `thinningFade` of its reach, widened by `min(maxWidthBoost, 1/sqrt(density))`; level by
   `GrassLodRange`; sinking from `FadeStart`. **Morph (vertex)**: `_LodMorph` per draw (start,
   1/length, next-is-billboard) → lerp toward UV1/UV2, turned by `GrassFaceEye` toward distant.

## Performance (M1 Pro, 1080p, editor, grass filling the view; per-frame A/B medians — frame time drifts 7–19 ms)

| Case | GPU + CPU | CPU |
| --- | --- | --- |
| After ×3: Meadow / MeadowAutumn / TallGrass (eye inside 2.4–4.2 m grass) | +0.5 / +0.8 / **+3.0** ms | < 0.1 ms |
| Four biomes mixed, via the 24-biome prefab (50 m reach) | +0.8 ms | 0.2 ms per camera |
| Four LODs, Meadow 300 m fully painted, settled | **+1.1 ms** | ~0 (walk skipped) |
| Same while the 300 m ring fills (load, teleport) | +2–5 ms for ~6 s | the scatter workers |
| Walking 10 m/s: near+mid tufts vs settled | min 95 %, median 102 % | — |
| Field start-up (rebuild + paint mask + first ground map, 13-layer 512² terrain) | — | ≈ 90 ms once |
| Morph + shrink-out vs previous shader, gameScene meadow, whole frame (2026-10-26) | 15.19 vs 15.54 ms: none | — |
| gameScene meadow, eye 2.4 m inside the grass, `densityScale` 0.6 / 1.0 | +3.6 / +3.8 ms (near overdraw dominates) | — |

## Multiplayer

N/A — pure presentation. Every machine derives identical grass from the terrain paint, the painted
plants and the seed; benders and trails are local ([GrassInteraction](GrassInteraction.md)). Nothing is
spawned or networked.

## Persistence

N/A — the grass holds no state. The paint and plants it reads live in the `TerrainData` asset;
trails are deliberately not saved.

## Gotchas

- **One GrassField per world.** It grows on every loaded terrain, so two enabled fields double the
  grass; `OnEnable` logs a warning. Put it in the world's main scene, not in chunk scenes.
- **`StylizedTerrain` (gameScene's NoiseTerrain) ignores the alphamap**; painted ground is coloured only
  by `GrassGroundTint` (`Ground_*` paint, `groundTint` > 0). Its 512² alphamap over 8 km (15.6 m
  texels) grew grass in **squares** until read bilinear + warped. Texel i's centre is `(i + 0.5) / res`; heights at `i / (res − 1)`.
- `StylizedTerrain` is shared with the Mars chunk: the tint is off unless `_GrassMapRect.z > 0`.
- **The ground map must stay premultiplied.** Straight colour let bilinear/mips blend edges toward
  the empty texels' black: a dark ring round every field, widening with distance (coarser mips).
- **Biomes match by `TerrainLayer` asset**, not index or name: a *duplicate* of `Ground_Meadow` grows nothing.
- `minimumHeight` is a **world** height (MeadowShowcase: 7.25, the pond): a terrain placed low stays bald.
- **Needs compute shaders and `cullShader`** (the prefab has it); else `Rebuild` logs an error, draws nothing.
- **A camera draws grass only if its culling mask has the field's layer.** That is the switch for
  keeping grass out of reflection, portal or minimap cameras — each one re-culls and re-draws it.
- **Four contracts move together:** `GrassInstance.cs` ↔ the compute's `GrassInstance`; `GrassVisible`
  (compute + shader); `GrassBiomeStream.MaterialFloatIds`; every tuft mesh carries UV1/UV2 morph data.
- **`GrassLodRange` is the one switch distance** — compute picks the level, vertex shader morphs by
  it; disagree and tufts pop mid-morph. Jitter keys off **tint** (rank is not in `GrassVisible`).
- **Measure LOD pops**: probe camera in 2 cm steps, pixels jumping > 0.12 luma minus the still-camera count.
- An authored `tuftMesh` still pops mid→far: it has no blade correspondence to the far tuft.
- **Distant spikes are lit as the ground (normal up)**; camera-facing normals shaded the far field by view.
- **Far tufts read as triangles** when widened past ~1.5× (3× made 1 m-tall, 5 m-wide fins) or
  outlined by dark roots; far tufts take tip colour all the way down.
- **Far grass hides the ground; it must not blend into it** (that read as bare tinted ground).
  `StylizedTerrain` lifts painted cover `c` to `1 − (1 − c)^layers`, layers 1 → `groundCoverLayers`
  (4) over the GrassField's `groundCoverStart/End` (40–200 m), via global `_GrassGroundCover`.
- **The scatter runs on worker threads.** `GrassScatter` and `PaintedGroundPatch` must never touch a
  Unity object; anything they need is captured on the main thread first.
- **`ChunkScatterStream.scratch` is iterated by `Harvest`**; eviction runs inside that loop, so it
  must remove through `Remove`, never `RemoveAll(scratch)` ("Collection was modified" on teleport).
- Wind response is on the **GrassField** (one `_GrassWind`); per-biome sway is `_WindStrength`.
- **Wind pace lives on the materials** (`Mat_Grass_*` and `Mat_Foliage_*` share `_WaveLength` 9 /
  `_WaveSpeed` 1.8 / `_WindFrequency` 0.12 / `_WindSpeed` 4 m/s — unequal, trees lean off-beat).
  `WindPush` warps and crosses the crests (one sine read as fast stripes). A new compute-read float
  also goes in `MaterialFloatIds` and the `StylizedGrass.hlsl` per-material CBUFFER.
- Lighting is per vertex; shadows are **one hard tap** (no soft-shadow keywords); tufts never cast.
- **Scatter budget is shared** (biome order rotates, nearest chunks first). `Carries` must check
  **paint**, not just the layer: a terrain-wide layer once spent every unit and starved the rest.
- Levels need `ScatterCellRandom` (else sparse ≠ subset). Workers: half the cores, ≥ 4 (two: squares ahead).
- **Faint brush washes** (1–30 % paint) must not count as painted: they starved the near chunks.
- A terrain's **first** ground map is built at once: on a worker, ground showed bare red until it landed.
- `MeadowShowcaseBuilder` instantiates the prefab and loads `Ground_Meadow`; moving either breaks it.
- Seen only in the editor on Metal (24-biome stress test; gameScene's 8 km NoiseTerrain); D3D11/
  Vulkan only compile via `ShaderUtil.Compile*Variant`. **Never run on Windows or in play mode.**

## Extending

- **New biome**: copy a `Mat_Grass_*` material and a `GrassProfile_*`, point the profile at the new
  material, tune colours/heights. Make a `Ground_<Biome>` TerrainLayer whose colour is the grass's
  average colour. Add `{terrainLayer, profile}` to the prefab's `biomes`.
- **Grass on a terrain**: add + paint `Ground_*` layers; one `GrassField.prefab` per scene, `minimumHeight` = water.
- **Look knobs** (free): `lodMorphBand`, `thinningFade`, `canopyColorStart/End` (profile);
  `groundCover*` (field: more layers = less bare ground far away, also for sparse biomes).
- **Cost knobs**, in order of effect: `densityFalloff` (2 → 2.6 halved Meadow's GPU cost at 300 m),
  `distantTuftDistance`, `fullDensityDistance`. `drawDistance` past 300 m is nearly free.
- **Depth priming** (renderer asset, project-wide) would let the forward pass skip shading hidden
  blades; not enabled, because it changes every opaque shader in the project.
