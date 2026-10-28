---
system: StylizedWater
layer: presentation
summary: Ponds and lakes — our own layered URP water shader on baked noise: depth colour, caustics, reflection, foam
paths:
  - Assets/Game/Art/Shaders/Environment/StylizedWater.shader
  - Assets/Game/Art/Shaders/Environment/StylizedWater.hlsl
  - Assets/Game/Art/Shaders/Environment/StylizedWaterNoise.hlsl
  - Assets/Game/Art/Textures/Environment/WaterNoise3D.asset
  - Assets/Game/Art/Textures/Environment/WaterCaustics.asset
  - Assets/Game/Editor/Environment/WaterNoiseBaker.cs
  - Assets/Game/Scripts/World/Environment/Water/WaterBody.cs
  - Assets/Game/Scripts/World/Environment/Water/UnderwaterVolume.cs
  - Assets/Game/Scripts/World/Environment/Water/UnderwaterRenderFeature.cs
  - Assets/Game/Art/Shaders/Environment/UnderwaterView.shader
  - Assets/Game/Editor/Environment/UnderwaterSetup.cs
  - Assets/Game/Prefabs/Environment/Water/Water.prefab
  - Assets/Game/Art/Materials/Environment/Water/Mat_Water.mat
  - Assets/ThirdParty/UberStylizedWater/Planner Reflection/
symptoms:
  - "the water pattern never changes, it just slides"
  - "the water's bed reads as black or the water is one flat colour"
  - "the water's swells are invisible on a plane"
  - "screenshots of the water show the wrong reflection or none"
  - "a tuned water default does nothing on an existing material"
  - "the caustic web smears into double lines"
  - "the water costs several milliseconds"
  - "the grass or fog runs twice when a planar reflection is on screen"
  - "a Planar Reflection Manager appeared in a scene I did not add it to"
  - "a second pond reflects the sky but not the scene"
  - "every kind of water looks the same blue from underneath"
  - "water has no foam along the edges in one scene but does in another"
  - "under water the view is normal, no haze or caustics, in most of a lake"
  - "looking up from under water the surface is completely white"
reads_with: [Environment, ArtPipeline]
updated: 2026-10-28
---

# Stylized Water

`SpaceGame/StylizedWater` is our own hand-written URP shader for ponds and lakes. Its
**defaults are the cozy pond look** — a material only names the shader. It replaced Uber
Stylized Water on the meadow pond because texture-panned patterns read as static and fake.

**To add water, drop [Water.prefab](Assets/Game/Prefabs/Environment/Water/Water.prefab) — or
a biome variant beside it — in a scene, put it at the water level and set `WaterBody ▸ Size`.**
Everything else follows.

| Prefab variant | Material variant | Biome | Look |
| --- | --- | --- | --- |
| `Water` | `Mat_Water` | meadow, default | cozy clear blue |
| `Water_Muddy` | `Mat_Water_Muddy` | rivers, farmland | silty ochre-brown, near opaque, beige froth, faster drift |
| `Water_Swamp` | `Mat_Water_Swamp` | marsh | murky olive, glassy still, algae scum patches instead of shore lines |
| `Water_Rust` | `Mat_Water_Rust` | red desert | iron-rich: terracotta shallows to oxblood depth, peach foam, warm glints |
| `Water_Glacial` | `Mat_Water_Glacial` | mountains, tundra | milky glacial turquoise, cold shadows |
| `Water_Lagoon` | `Mat_Water_Lagoon` | tropical coast | very clear aqua, strong caustics, deep falloff |
| `Water_Blackwater` | `Mat_Water_Blackwater` | dark forest, peat bog | tea-amber shallows to near-black, strong mirror |
| `Water_Ocean` | `Mat_Water_Ocean` (child of Lagoon) | open sea, dives | Lagoon with 4× clarity (6), deeper blues, deep colour at 9 m, caustics to 12 m |

The variants are **prefab variants of `Water`** that only swap the material, and **material
variants of `Mat_Water`** that only override colours and tuning — so a fix to the base prefab,
the base material or the shader reaches all of them.

## Model

- **The prefab is one GameObject** on the `Water` layer: `MeshRenderer` (`Mat_Water`, shadows off) + `PlanarReflectionVolume` (targets itself; interval 2, 300 m, half res) + `UnderwaterVolume` (6 m deep) + [WaterBody](Assets/Game/Scripts/World/Environment/Water/WaterBody.cs). `WaterBody` builds the surface grid (`cellSize`, default 0.5 m) whenever it is enabled or resized, and sizes the reflection volume (water + `reflectionReach` each side, `reflectionReach` × 2 tall) and the underwater volume to match.
- **Four parts.** [StylizedWater.shader](Assets/Game/Art/Shaders/Environment/StylizedWater.shader) — properties grouped by layer, a fragment that only stacks the layers. [StylizedWater.hlsl](Assets/Game/Art/Shaders/Environment/StylizedWater.hlsl) — one function per layer, listed with the per-pixel cost in its header. [StylizedWaterNoise.hlsl](Assets/Game/Art/Shaders/Environment/StylizedWaterNoise.hlsl) — reads the baked textures. [WaterNoiseBaker](Assets/Game/Editor/Environment/WaterNoiseBaker.cs) (`SpaceGame ▸ Environment ▸ Bake Water Noise`) — bakes them and sets them as the shader's default textures, so new materials need no assignment.
- **The patterns are baked, not evaluated.** `WaterNoise3D` (64³ RGBAHalf, 2 MB): gradient noise A + its x/y slope in RGB, an independent noise B in A — the **third axis is time**. `WaterCaustics` (256² R8, mipped): a static Voronoi edge-distance web.
- **Nothing scrolls.** Ripples, foam and colour drift move *through* the volume's time axis, so shapes re-form while they drift. The caustics are two layers of the static web drifting apart, each warped differently by noise A. Never add a panned texture.
- **Layers, in stacking order:** swells (vertex Gerstner ×3) → ripples (normal from noise A's slope, faded with distance) → scene read (bed placed along the view ray at scene eye depth, refracted through the ripples) → bed + caustics → body colour (shore/shallow/deep by vertical depth, drift per vertex) over it by view-depth opacity → sun shadow → reflection by fresnel → foam (shore rim, inbound shore lines, open-water streaks in per-vertex patches) → glints. Back faces draw the underside.
- **Underwater takes on the water above.** While a camera is inside an `UnderwaterVolume`, `UnderwaterRenderFeature` draws one fullscreen pass (`Hidden/UnderwaterView`) whose colours come from that volume's surface material: haze up/down = `_ShallowColor` / `_DeepColor` × `deepHazeBrightness`, density = `hazeDensityTimesClarity` ÷ `_Clarity`, scene tint = `_BedTint`, caustics = `_CausticColor` (black = none), and `_WaterNoise` plus the five `_Ripple*` values are copied as-is so the surface ripples identically from both sides. The two haze factors are on the feature in the renderer assets; wobble, caustic scale and the window tunables stay on `UnderwaterView.mat`, which is also the whole look under a volume with no StylizedWater material. The water shader culls back faces — from below, this pass is the only thing that draws the surface.
- **The surface from below is Snell's window.** The view ray is refracted (IOR 1.333) through the rippled normal (`WaterRippleSlope` in `StylizedWaterNoise.hlsl`, shared with the surface shader; scaled by `_UndersideRipple`). Inside the ~49° critical cone it passes out and reads the world above back from the frame at `_WindowParallax` metres along the bent ray; outside it is totally internally reflected and shows the haze looking down. Schlick fresnel (F0 0.02) blends the two toward the rim.
- **It composites itself.** It reads `_CameraOpaqueTexture` and `_CameraDepthTexture` and writes the final colour with `Blend Off` in the transparent queue.
- **Reflection is Uber's planar system, with SpaceGame edits** marked `// SpaceGame` in the vendored manager/volume: the mirror camera is `CameraType.Reflection`; it renders only when a target is in the camera's frustum; in play mode every `renderInterval` frames (pond: 2); and to `reflectionDistance` (pond: 300 m). The manager publishes `_PlanarReflectionTexture` and writes `_PlannerReflectionBlend` (0 planar, 1 probe); the shader reads only the source in use.

## Performance

Measured on an M1 Pro (Metal) in the editor, 1080p: the water **filling the screen costs ~0.6–1.0 ms** (5.5 ms when the noise was procedural). The reflection costs **~1.7 ms per render** (about 1 ms of it is URP's fixed per-camera overhead), so ~0.85 ms a frame in play mode at interval 2, and nothing while the water is off screen. D3D11 compiles to ~330–360 math instructions, 10–11 registers, per pixel.

## Flows

`Build Meadow Showcase` → `CreateWater` → instantiates `Water.prefab` as `Pond Water`, sets `size` 60 × 60 → `WaterBody.OnEnable` builds the grid and fits the volumes → the volume's `OnEnable` registers with `PlanarReflectionManager.Instance` → bubbles.

## Multiplayer

N/A — pure rendering from `_Time`; identical on every machine.

## Persistence

N/A — authored scene content with no runtime state.

## Gotchas

- **Depth Texture and Opaque Texture must be on in the URP asset** (both are on `PC_RPAsset`). Without them the bed reads black and the water is one flat colour.
- **The ground must be in the depth texture, which here means a `DepthNormals` pass.** SSAO reads depth-normals, so URP fills the depth texture from that prepass; a ground shader with only `DepthOnly` is missing from it and the water sees bottomless depth — no shore foam, no shallows. `StylizedTerrain` lacked it (fixed); check any custom ground shader first when foam vanishes.
- **Rim and shore-line widths are in metres of *depth*, not distance.** On a gentle beach a 0.2 m depth band is metres wide; lower `_RimWidth` / `_ShoreLineReach` on a material for flat shores.
- **`#define _SURFACE_TYPE_TRANSPARENT` before `Lighting.hlsl` is load-bearing.** Without it `_MAIN_LIGHT_SHADOWS_SCREEN` samples the screen-space shadow texture, which holds only opaques.
- **Shadows are hard on purpose.** The soft-shadow keywords are not compiled in: the filter cost ~1 ms full screen and the ripples hide the difference. Don't add `_SHADOWS_SOFT` back.
- **A moving pattern must not live on the volume's time axis if it is thin.** The caustic web baked that way smeared into double lines between time slices; thin, sharp patterns go in a static mipped 2D texture and are animated by warping.
- **Streak width comes from value ÷ slope**, the true distance to the noise's zero crossing. Thresholding the filtered value alone made lumpy blobs.
- **Glints need `_GlintPower` in the thousands.** The baked normals are smooth, so a looser highlight merges into white blotches.
- **Mipmapped fetches stay outside branches.** `WaterCausticEdge` uses implicit derivatives; only the bed's shadow sample sits in the caustics branch. Volume fetches are LOD 0 and safe anywhere, including the vertex stage.
- **The baker keeps the textures readable.** A texture asset is serialized from its CPU copy; `Apply(makeNoLongerReadable: true)` before saving wrote an empty asset.
- **`PlanarReflectionVolume.OnEnable` overwrites `reflectionLayer`** with "everything but Water" — the Inspector mask never sticks.
- **Transparents under the water sort wrong.** The water writes no depth and draws with `Blend Off`, so a particle below the surface (the pond's bubbles) is either overwritten or drawn over the water.
- **Refraction drops bent reads that land in front of the water**, falling back to the straight read, or a reed above the surface would smear into the pond.
- **A material keeps the values it was saved with.** Changing a default in the `.shader` changes nothing on an existing `.mat`; rebuild the showcase or reset the property.
- **The planar reflection follows ONE camera.** In edit mode `runOnEditMode` makes it the Scene view's; otherwise `Camera.main`. A screenshot from any other camera shows a reflection rendered for someone else — move the main camera instead.
- **Distance fades are anti-shimmer, not style** (`_RippleFadeDistance`, `_CausticFadeDistance`).
- **Swells displace vertices.** Unity's built-in Plane has vertices metres apart — `WaterBody` builds the grid.
- **The surface mesh is `DontSave`.** Scenes and the prefab hold no mesh (the MeshFilter reads empty in the prefab asset); each instance builds its own on enable and destroys it on disable. Don't assign a mesh asset by hand — it is replaced.
- **A reflection volume creates the manager.** `RegisterVolume` calls `PlanarReflectionManager.Instance`, which spawns one into the *active* scene if none exists — opening the prefab in Prefab Mode can drop a manager into whatever scene is active.
- **The underwater pass draws with a runtime copy of `UnderwaterView.mat`.** It used to write per-camera values into the shared asset, which in the editor dirties and saves it; per-water colours on the asset would have stuck. Tune the look on the asset, never on the copy.
- **Both volumes live in the water's local space.** A scaled water object (the gameScene lagoon is scaled −2.77 × 1.49) stretches the surface mesh, so `UnderwaterVolume.Find` and `PlanarReflectionVolume` test the camera with `InverseTransformPoint` against `size`. Only the underwater *depth* is world metres. When `Find` used the unscaled `size` in world space, the lagoon's underwater view worked only in its middle 300 × 300 m. Prefer `WaterBody ▸ Size` over scaling: scale also stretches the swell grid.
- **Under water, the sky probe is not our sky.** `_GlossyEnvironmentCubeMap` is Unity's `Default-Skybox-Cubemap` (the `DesertSkybox` is never baked into a probe), so a window that fell back to it showed a grey rectangle where the bent read left the screen. The fallback is the unbent frame through the same pixel — always the world above, always the right sky.
- **Refraction from below needs weaker ripples than reflection from above.** At full `_RippleStrength` the window's rim shattered into camouflage-like islands; `_UndersideRipple` (0.4) scales it down.
- **One planar reflection per camera.** The manager renders only the highest-priority volume the camera is in; other water in view gets `_PlannerReflectionBlend` 1 and shows the reflection probe. Raise `priority` on the water that matters most.

## Extending

**Water on a new pond or lake:** the biome's prefab variant, `Size` large enough to run under the shore all round.

**A new biome look:** a material variant of `Mat_Water` (set `parent`, override only what differs — usually the three depth colours, `_Clarity`, foam colour, swell steepness, caustic colour; black caustic colour turns them off), then a prefab variant of `Water` that swaps only the material. Don't edit `Mat_Water` for one biome — every variant inherits it.

**A new layer:** a function in `StylizedWater.hlsl` under its own header plus a line in the header list, its properties under a `[Header]` in the `.shader` *and* in `UnityPerMaterial` (or the SRP batcher breaks), one call in `frag`. Read patterns from the baked textures — a pattern slower than the 0.5 m grid goes in `SlowPatterns` (per vertex). Re-bake after changing the baker; its cell counts must match the `#define`s in `StylizedWaterNoise.hlsl`.
