---
system: PaintedGround
layer: presentation
summary: Brush-painted StylizedTerrain grounds — material palettes plus PaintedGroundPalette assets at own scales
paths:
  - Assets/Game/Scripts/World/Environment/Ground/
  - Assets/Game/Terrain/PaintedGround/
symptoms:
  - "painting Paint_DryGrassland or Paint_ForestFloor changes nothing on the terrain"
  - "the grass is gone where I painted a dry grassland or forest floor ground"
  - "a painted ground such as Paint_RiverPebbles or Paint_CrackedMud shows nothing, and the console says the painted grounds span more than 4 splat textures"
  - "a noisy band of tiny squashed patches or pebbles runs along the border between two painted grounds"
  - "a painted ground is covered in round circle-like pebbles that look nothing like its source texture"
  - "some painted grounds look far darker than the others"
  - "a painted ground shows only flat colour and the console says its pattern is unlike the first one"
reads_with: [NoiseHeightmap, Grass]
updated: 2026-11-03
---

# Painted Ground

## Model

A painted ground is an ordinary `TerrainLayer` the terrain brush paints with. `StylizedTerrain`
([NoiseHeightmap.md](NoiseHeightmap.md) has the rest of the shader) reads only the layer's splat
weight: it paints the ground from a `GroundPalette`, and for palette grounds from the palette's
**pattern** texture too, drawn without visible repeats. Painted grounds override the material's own
ground and biomes.

| Ground kind | Palette comes from | Layers |
| --- | --- | --- |
| Material grounds | the material: `_Dry*`, `_Forest*`, its own height ramp + paint colours | `Paint_DryGrassland`, `Paint_ForestFloor`, `Paint_CanyonSand` |
| Palette grounds | a `PaintedGroundPalette` asset, one row of the palette table, plus its pattern | `Paint_<Name>`, one per `Palette_<Name>`; the layer's diffuse is the pattern, so the brush palette shows it |

| Stage (shader) | How |
| --- | --- |
| Splat read | up to four of the terrain's splat textures (`_PaintSplat0..3`), each with a ground code per channel (`_PaintSplatGround0..3`: 0 none, 1 dry, 2 forest, 3 own, 4+n table row n); UV warped ±`_PaintMapWarp` m so 16 m splat texels do not show |
| Palette | painted palettes averaged by weight (running `lerpPalette`), then over the unpainted ground by the summed weight. Colours, `detail` and `soil` blend |
| Shape + pattern | patch scale, pebble spacing and radius, ripple scale; pattern slice, tile m, strength, may-rotate. **Never blended**: the two strongest under a pixel (`GroundShapes`) each draw patches, pattern, streaks, pebbles (`paintSurface`) and ripples (`rippleSlope`), crossfaded by weight. Material grounds: shape (1, 1.4 m, 0.0929, 1), no pattern |
| Pattern | `_PaintPatterns` (texture array) slice at `tile` m, Quilez no-tile: a world noise about a tile wide picks one of eight shifted (and, if allowed, turned) variants, neighbours crossfaded along the pattern's own light/dark. Gradients come from `ddx/ddy` of world XZ taken outside every branch. The painted patches then shade it: `pattern × patchColour / base` |
| Soil | scales the near gravel and cracks and sets the gravel clump coverage ([NoiseHeightmap.md](NoiseHeightmap.md), near ground detail); material grounds use × 1, × 1, `_GravelCoverage` |

Palette table `_PaintPalettes`: RGBAFloat, `PaintedGroundPalette.TableWidth` (12) texels per row,
point-sampled with `LOAD_TEXTURE2D`: base, darker, paler, streak, pebble, cliff, strata, shadow
(linearised like material colours), detail (ripples, pebble density, dabs, streaks), soil (gravel,
cracks, pebble tone variation, gravel coverage), shape, pattern.

**The nine Poly Haven grounds (2026-11-02):** FarmSoil, ForestGround, FarmFurrows, GravellySand,
RockyTrail, RiverPebbles, ForestLeaves, CrackedMud, DampSand, from `farm_soil`, `forest_ground_06`,
`farm_furrows`, `gravelly_sand`, `rocky_trail_02`, `dry_river_pebbles`, `forest_leaves_02`,
`mud_cracked_dry_03`, `damp_beach_sand` (CC0). Their [Patterns/](Assets/Game/Terrain/PaintedGround/Patterns)
(1024², tileable) are the photos stylized offline — × AO, wide shading divided out (it repeats once
tiled; the painted patches supply large-scale variation), mean-shift into flat painted regions,
k-means to 5–6 colours, a majority filter on the colour labels (σ 1.2 px for fine grains — larger
erases them — up to 3 px for pebbles, leaves, cracks), then **every ground moved to one mean lightness**
(Lab L* 66, chroma raised with the lift). `farm_furrows` is a field patch, not a tile: its rows' middle
was cut out and cross-faded sideways. The palette is measured off the pattern (base = its mean, patch
tones halfway to its darker/paler quarter, at least ±10 %). No pebbles, gravel or cracks are added:
the pattern has its own. Tile = photo size × 1.75 (the player is 3 m). The generator scripts are not
in the repo — the PNGs are the source, as with `GroundDetail.png`.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `TerrainGroundPaint` | [TerrainGroundPaint.cs](Assets/Game/Scripts/World/Environment/Ground/TerrainGroundPaint.cs) | `[ExecuteAlways]` on the terrain. Three layer refs for the material grounds plus `PaintedGroundPalette[] grounds`; finds each layer by asset, picks the splat textures that hold them, copies the patterns into a `Texture2DArray` (GPU `CopyTexture`), writes codes, palette table, array and `_PaintMapRect` into the splat property block (Get → Set, like `GrassGroundTint`) |
| `PaintedGroundPalette` | [PaintedGroundPalette.cs](Assets/Game/Scripts/World/Environment/Ground/PaintedGroundPalette.cs) | ScriptableObject: `layer`, `pattern` + tile/strength/rotates, 8 colours, detail, shape, soil. `Write(table, row, slice)`; `Changed` (editor) when edited |

## Flows

Bind (on enable, inspector change, edit-mode layer/splat-texture change, or a listed palette's
`OnValidate`) → each layer's index in `terrainData.terrainLayers` → splat texture `index / 4`,
channel `index % 4` → first four distinct textures get slots → codes per slot → patterns copied
into the array (one slice per distinct texture) → palette table rebuilt with each row's slice →
property block set. Disable zeroes `_PaintMapRect` (the shader's "nothing painted") and destroys the
table and the array.

## Multiplayer

N/A — presentation only. The splat weights live in the `TerrainData` asset and the palettes are
assets, so every machine renders the same ground.

## Persistence

N/A — no runtime state. Paint is saved in the `TerrainData` asset by the editor; the palette table is
rebuilt on every bind.

## Gotchas

- **A `Paint_*` layer only shows through `TerrainGroundPaint`.** The layer must be on the terrain's layer list (painting with it adds it) **and** referenced by the component: the material grounds' three refs, or a palette in `grounds`. Otherwise the paint is in the asset but the shader never sees it. `_PaintMapRect` is (0, 0, 1/8000, 1/8000) in gameScene, which prints as all zeros at two decimals — read `.z` with more digits before deciding it is unbound.
- **Painted grounds may span at most four splat textures.** Each texture holds four layers in list order; the shader reads only the four that hold painted grounds. gameScene's terrain already references 16 layers (generator pair, `MeadowShowcase_Sand`, 10 `Ground_*`, 3 `Paint_*`), so the nine palette layers land in textures 4–6 when added at the end: four in all only while the three older `Paint_*` share one texture. A tenth palette or a layer inserted between them pushes one out; `TerrainGroundPaint` logs an error naming it. Keep `Paint_*` layers together at the end of the list.
- **Never blend a ground's scale.** Lerping patch size (or pebble spacing, ripple scale) across a 16 m painted border made `p / size` sweep hundreds of noise cells inside the border at 4 km from the origin: a band of squashed noise. Any new scale-like value goes into `shape`, which is drawn per shape and crossfaded, never into the blended palette.
- **Painting a ground erases grass paint under it.** Splat weights sum to 1, so a full-strength stroke zeroes the `Ground_*` grass layers there and the tufts go ([Grass.md](Grass.md)). Paint ground first, grass over it with a softer brush, or accept bare ground.
- **Procedural pebbles read as circles, not as the source.** The first nine grounds faked their sources with Voronoi pebbles and the near gravel domes; the user rejected the circles. Grounds with a pattern keep pebbles, gravel and cracks at 0.
- **Equal lightness is set in the pattern, not the palette.** The photos' albedo ran from L* 24 (farm soil) to 61 (cracked mud); lifting only the palette left the pattern dark. Re-stylize to the common L* rather than tinting a palette.
- **Every pattern must share size, format and mip count** — a texture array holds one layout. Import alike (the Patterns' metas: sRGB, mips, Repeat, trilinear, aniso 8, default compression); a mismatch is logged and that ground drawn without its pattern. The array's own sampler (trilinear, Repeat, aniso 8) is what the shader uses, not the source's import settings.
- **`StylizedTerrain` is `#pragma target 3.5`** for the texture array.
- **Ripple relief is lighting only, and the toon steps crush it.** Below ~2 it barely reads. Small relief by normal reads as holes from low angles, so pebbles stay painted ([NoiseHeightmap.md](NoiseHeightmap.md)).
- **Shadow tint is entered like any material colour** (sRGB in the inspector, linearised by `Write`), so (0.12, 0.10, 0.20) is a faint lilac, matching `_DryShadow`.

## Extending

- **New ground:** Create → SpaceGame → World → Painted Ground Palette; give it a `TerrainLayer` (diffuse = its pattern, for the brush palette) and a 1024² tileable pattern at the common lightness, imported like the others; add the palette to the terrain's `TerrainGroundPaint.grounds`, then paint. Table row = array index, so reordering `grounds` is free. A directional pattern (furrows) needs `patternRotates` off.
- **A fifth splat texture** would need a fifth `_PaintSplat`/`_PaintSplatGround` pair and branch in `StylizedTerrain`, and `MaxSplats` raised; each costs one more splat sample per pixel where painted.
