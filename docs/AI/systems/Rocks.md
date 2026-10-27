---
system: Rocks
layer: presentation
summary: GPU-instanced rocks painted with the terrain's detail brush — small, big and massive, recoloured per biome
paths:
  - Assets/Game/Scripts/World/Environment/Rocks/
  - Assets/Game/Scripts/World/Environment/Scatter/
  - Assets/Game/Editor/Environment/RockPaintSetup.cs
  - Assets/Game/Art/Shaders/Environment/RockCull.compute
  - Assets/Game/Art/Shaders/Environment/RockShared.hlsl
  - Assets/Game/Art/Shaders/Environment/StylizedRock.shader
  - Assets/Game/Art/Shaders/Environment/StylizedRock.hlsl
  - Assets/Game/Art/Materials/Environment/Rocks/
  - Assets/Game/ScriptableObjects/Environment/Rocks/
  - Assets/Game/Prefabs/Environment/Nature/RockField.prefab
  - Assets/Game/Prefabs/Environment/Nature/RockPaint/
  - Assets/ThirdParty/Quaternius/StylizedNatureMegaKit/
  - Assets/Game/Scenes/Tests/RockGallery.unity
symptoms:
  - "I painted rocks with Paint Details and nothing appears"
  - "painted rocks appear twice, once as plain grey prefabs"
  - "the player walks through a big rock"
  - "NPCs walk through rocks"
  - "a rock pops out of view instead of fading"
  - "even a faint brush stroke fills the area with rocks"
  - "rocks vanish at the edge of the screen and their shadows go with them"
  - "rocks pop in all at once after loading, a teleport or a brush stroke"
  - "rocks load in too abruptly at the edge of the draw distance"
reads_with: [Grass, Foliage, LevelOfDetail]
updated: 2026-10-26
---

# Rocks

Rocks are drawn, never placed: a [RockField](Assets/Game/Scripts/World/Environment/Rocks/RockField.cs)
scatters them over **every loaded Unity Terrain** wherever a biome's **detail layer** is painted
(terrain tool *Paint Details*), and draws them GPU-instanced with `SpaceGame/StylizedRock`. A detail
layer never touches the splat map, so painting rocks never changes the ground texture or the grass.

## Model

- **Biome** = one `RockField.biomes` entry: a **paint prototype** prefab (the detail prototype the
  palette shows, matched **by asset**) + a [RockFieldProfile](Assets/Game/Scripts/World/Environment/Rocks/RockFieldProfile.cs).
- **Profile** = one look (Red Desert…): placement (full-density paint, slope, edge warp) + patch noise
  shared by all its **size classes**, so scree gathers where boulders are. Density is **linear in paint
  strength** (`paint / fullDensityPaint`), so a 5 % brush sprinkles 5 % of the rocks; an earlier
  0.2–0.4 ramp made every stroke all-or-nothing.
- **Size class** = variants `{mesh, material, weight}`, width range in metres, stretch, sink, spacing,
  chance, slope align, tilt, draw distance + `fadeBand`, LOD/cull **screen sizes**, chunk size, shadows, collides.
  Each class is its own stream (pebbles reach 70 m, massive rocks 900 m).
- **Shipped** (4 profiles × Small / Big / Massive): Red Desert, Yellow Desert, Grey Stone, Mossy.

| Class | Models | Width | Spacing × chance | Reach | Shadows / collider |
| --- | --- | --- | --- | --- | --- |
| Small | 11 pebbles + 3 rocks | 0.25–0.9 m | 1.6 m × 0.35 | 70 m | no / no |
| Big | Rock_Medium 1–3 | 1.4–3.2 m | 7 m × 0.3 | 300 m | yes / yes |
| Massive | Rock_Medium 1–3 | 6–13 m | 28 m × 0.22 | 900 m | yes / yes |

- **Models**: Quaternius Stylized Nature MegaKit (CC0, `Assets/ThirdParty/Quaternius/…`). Rock_Medium
  has importer Mesh LOD (`maximumMeshLod 2`, e.g. 342 → 192 → 128 tris); pebbles (48–136 tris) none.
  All are Read/Write (colliders) with no imported materials.
- **Colour** lives on the materials: `Mat_Rock_<Look>` (rock texture) and `Mat_Pebble_<Look>` (path
  texture). The texture's **brightness** drives a dark/mid/light ramp; `_KeepOriginal` 1 shows its own
  colours (Mossy). Up-facing faces take `_DustColor` (match the ground). `Mat_RockPaint_<Look>` are URP
  Lit, only for the palette thumbnail.

## Key types

| Type | Role |
| --- | --- |
| [RockField](Assets/Game/Scripts/World/Environment/Rocks/RockField.cs) | `[ExecuteAlways]`. Biomes, `cullShader`, seed, water cut-off, sun, `arrivalSeconds` (0.6), collider radius/layer/interval. Hooks `beginCameraRendering`; in play, `Update` streams around players and syncs colliders. |
| [RockClassStream](Assets/Game/Scripts/World/Environment/Rocks/RockClassStream.cs) | One size class: a one-level `ChunkScatterStream<RockInstance>`, the visible buffer split into **segments** (variant × LOD), cull dispatches, one `RenderMeshIndirect` per variant per LOD the mesh has. |
| [RockScatter](Assets/Game/Scripts/World/Environment/Rocks/RockScatter.cs) | Worker-thread jittered grid. Keep = chance × paint × patch noise; picks variant, width, stretch, tilt, sink. Rotation maths in plain C#. |
| [RockInstance](Assets/Game/Scripts/World/Environment/Rocks/RockInstance.cs) | 64-byte stored rock (pivot+scale, quaternion, stretch+variant/tint, bounding sphere); contract with **RockCull.compute**. |
| [RockColliders](Assets/Game/Scripts/World/Environment/Rocks/RockColliders.cs) | Pool of convex `MeshCollider`s moved onto colliding rocks within `colliderRadius` of any player. |
| [RockCull.compute](Assets/Game/Art/Shaders/Environment/RockCull.compute) | Per rock per camera: draw distance, screen size vs cull size, frustum **capsule** (rock + its shadow), LOD by screen size (±10 % jitter), **eased** shrink into the ground near cut-offs (last `fadeBand` 25 % of draw distance; screen size) and for `arrivalSeconds` after its chunk lands, append to segment. |
| [StylizedRock.shader](Assets/Game/Art/Shaders/Environment/StylizedRock.shader) | ForwardLit (wrap sun, SH, one hard shadow tap, fog), ShadowCaster, DepthOnly, DepthNormals. Reads `_RockVisible[_SegmentOffset + SV_InstanceID]`. |
| [RockPaintSetup](Assets/Game/Editor/Environment/RockPaintSetup.cs) | *Tools/World/Rocks/Add Rock Paint To Selected Terrains*: detail map if none (8 m cells, 128–2048), adds every biome's prototype, Detail Distance → 0. |
| Shared with [Grass.md](Grass.md) | `GroundPaint`, `PaintedGround`, `PaintedGroundPatch`, `ChunkScatterStream<T>`, `ChunkInstancePool<T>`, `ScatterCellRandom`, `ScatterGrid` in `Scatter/`. |

## Flows

1. **Per camera**: `Rebuild` if dirty → `PaintedGround.TakeChanges` → invalidate overlapping chunks
   → each class streams nearest-first → cull globals (eye, frustum, screen scale =
   `0.5 / tan(fov/2) × lodBias`, sun direction) → each class Reset/Cull/WriteArgs → indirect draws.
2. **Detail stroke (editor)**: no terrain callback exists for detail paint. `PaintedGround` polls
   `EditorUtility.GetDirtyCount(terrainData)`; once unchanged for 0.3 s it re-reads the detail maps and
   diffs against its byte copy; the changed cells become the invalidated rect.
3. **Play**: every `colliderInterval`, `SessionPlayers.Collect` → each player is an extra eye for the
   colliding classes (so the host has rocks around players its camera is far from) → `RockColliders.Sync`.

## Multiplayer

No netcode. Every machine derives identical rocks from paint + seed (`ScatterCellRandom`), and every
machine puts colliders around **every** player it knows of, so host and client collide alike.

## Persistence

N/A — rocks hold no state. The paint lives in the `TerrainData` asset.

## Gotchas

- **Detail Distance must be 0** on painted terrains: the paint prototypes are real detail prototypes,
  and Unity draws plain copies of them otherwise. RockField warns once per terrain.
- **A terrain with `detailResolution` 0 cannot hold detail paint** (gameScene's NoiseTerrain shipped so).
  The setup menu gives it a map; that edits the `TerrainData` asset.
- **No NavMesh, no colliders far from players.** NPCs path through rocks and, away from every player,
  pass through them. Keep massive rocks out of NPC routes or bake obstacles by hand.
- **Mesh LOD ranges index the whole index buffer** (`GetLod(0, l).indexStart` 0 / 1026 / 1602 on
  Rock_Medium_1). Variants must have **one submesh**; others are rejected with an error.
- StylizedRock materials **cannot be put on a MeshRenderer** — every pass reads `_RockVisible`. That is
  why the paint prototypes use separate Lit materials.
- Off-screen rocks are kept while their **shadow capsule** (up to 6 radii along the sun) reaches the
  frustum. No sun or `LightShadows.None` → sphere test only.
- The scatter runs on worker threads: `RockScatter` must not call engine maths (`Vector3.Slerp`,
  `Quaternion.AngleAxis` are native); it builds quaternions itself.
- Three contracts move together: `RockInstance.cs` ↔ RockCull's `RockInstance`; `RockVisible` in
  `RockShared.hlsl` ↔ `VisibleStride` in RockClassStream; `LOD_COUNT` ↔ `RockClassStream.LodCount`.
- **One RockField per world**, like GrassField; two double every rock.
- **Rocks fade by scale, eased (smoothstep).** Linear scale showed most of a rock's area at the very
  end — a pop. A chunk landing in view grows in over `arrivalSeconds` from `ChunkInstancePool`'s
  `_SlotFilled` / `_ScatterClock`: after a forced rebuild the largest one-frame jump fell from 1345 to
  649 px (960×540). Grass must never use `_SlotFilled`: a level swap would regrow existing tufts.
- Seen in editor renders on Metal ([RockGallery](Assets/Game/Scenes/Tests/RockGallery.unity), four
  zones, one per look). D3D11/Vulkan only via `CompileVariant`. Colliders proven in a preview scene's
  physics (20 placed within 40 m, raycasts hit, all released on leaving). **No Play-mode or client run.**

## Extending

- **New look**: copy `Mat_Rock_*` + `Mat_Pebble_*` (+ `Mat_RockPaint_*`), recolour the ramp and dust;
  copy a `RockProfile_*` and point its variants at the new materials; add a paint prefab
  (`RockPaint/`, Lit material) and a `{paintPrototype, profile}` entry on `RockField.prefab`; run the
  setup menu on the terrains.
- **New model**: import with Read/Write, no materials, one submesh, origin at its base, importer Mesh
  LOD for anything over ~200 tris; add it as a variant.
- **In a world**: drop `RockField.prefab` in the main scene, select the terrains, run the setup menu,
  paint with *Paint Details*.
