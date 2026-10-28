---
system: LevelOfDetail
layer: presentation
summary: Three-tier LODGroups on the nomad settlement, every Raxy, and every piece of the mountain workshop
paths:
  - Assets/Game/Editor/Agents/CharacterLodWiring.cs
  - Assets/Game/Editor/Agents/CharacterLodMeshes.cs
  - Assets/Game/Editor/Agents/CharacterLodProfile.cs
  - Assets/Game/ScriptableObjects/Characters/CharacterLodProfile.asset
  - Assets/Game/Prefabs/Environment/Structures/NomadSettlement
  - Assets/Game/Prefabs/Environment/Decorations
  - Assets/Game/Prefabs/Agents/Characters/Raxy
  - Assets/Game/Editor/Workshop/WorkshopLodBuilder.cs
  - Assets/Game/Scripts/Core/Rendering/DistanceCulledLights.cs
symptoms:
  - "a settlement piece's Mesh LODs never kick in in play mode, but forcing a level works on the prefab in the editor"
  - "a renderer with generated Mesh LODs still draws its full triangle count at 200 m"
  - "LOD copy renders full detail in play mode"
  - "a building looks perfect up close but its foundation smears or its deck has a hole at mid distance"
  - "decoration renderer is registered with more than one LODGroup"
  - "z-fighting doubled mesh on a settlement prop"
  - "a hand-made decoration part reappears on an outpost at a distance, or disappears up close"
  - "a decoration's net, pole or tree trunk turns into a solid sheet or a wedge a little way off"
  - "flat decoration walls show vertical streaks only at a distance"
  - "OutpostPrefabTests says a part the blend removed is shown, naming a decoration part"
  - "street slabs and path tiles stop in a ring around the camera while walls and tents carry on"
  - "a sail tent's anchors and wall hooks disappear at a distance"
  - "LOD transitions in a preview-scene render show the wrong LOD or both at once"
  - "a Raxy's eyes and bandit mask vanish at mid range"
  - "a garment added in Prefab Mode never disappears at distance"
  - "Fix Drifter Scale makes a hatted Raxy shorter"
  - "a distant Raxy's coat edge sits a couple of centimetres off its near-tier pose"
  - "a small crate or pot loses its contact shadow about 20 m away"
  - "the workshop costs as much from the far desert as standing at its gate"
  - "a workshop room has no walls or ceiling when seen down a tunnel"
  - "the workshop's lamps are all lit while I am far outside the mountain"
reads_with: [ArtPipeline, CharacterClothes, SettlementStreets, Workshop]
updated: 2026-10-28
---

# Level of detail

## Model

Everything a nomad settlement places, and every Raxy, has a root `LODGroup` with up to three
tiers and a cull (2026-10-05). Before that, settlement prefabs had a one-level LODGroup (a pure
cull at 2 %/1 %), decorations had none, and nothing had reduced meshes.

| Tier | Contents |
| --- | --- |
| LOD0 | the authored renderers, shadows as authored |
| LOD1 | ~50 % triangles (less where quality gates refused), small detail parts dropped, small parts cast no shadow |
| LOD2 | silhouette parts only, ~20–50 % triangles, shadows mostly off |
| cull | — |

Size classes by the bounds' largest dimension (thresholds are `screenRelativeHeight`, with
`QualitySettings.lodBias = 2`, so they trigger at half the screen height):

| Class | LOD0 / LOD1 / LOD2 (cull below last) |
| --- | --- |
| Large > 6 m (builds, towers, big tents) | 0.30 / 0.10 / 0.01 |
| Medium 1.5–6 m | 0.25 / 0.08 / 0.025 |
| Small < 1.5 m (decorations, two tiers) | 0.10 / 0.035 |
| Raxy (from `CharacterLodProfile.asset`) | 0.25 / 0.08 / 0.02 |

Four different reduction techniques are in use, each chosen for its slice:

| Slice | How the triangles drop |
| --- | --- |
| 31 builds directly in `NomadSettlement/` | importer Mesh LOD (`generateMeshLods`, `maximumMeshLod 3`); `<renderer>_LOD1/_LOD2` child copies share the mesh with `forceMeshLod` set |
| `Tents/`, `Terrace/`, `Streets/`, `nomad_door.fbx` | importer Mesh LOD with **automatic** selection on the one renderer; the LODGroup only drops parts and culls |
| 402 `Deco_*` prefabs | offline: `MeshLodUtility.GenerateMeshLods` on a copy, one level extracted to a plain mesh in `<fbx folder>/deco_<item>_lod.asset`; LOD children named **exactly like their part** |
| 77 Raxy prefabs | `SkinnedMeshRenderer` copies under `Model/LODs` on compacted meshes in `<model folder>/LOD/`, same bones |
| `Workshop.prefab` (`workshop.fbx`, `workshop_moving.fbx`) | importer Mesh LOD (`maximumMeshLod 3`, set by `WorkshopPrefabBuilder.ImportWithMeshLods`); a LODGroup **on every drawn renderer**, `<renderer>_LOD1`/`_LOD2` child copies with `forceMeshLod` 1 / 3 |

**The workshop** (2026-10-12): tiers by metres past the piece's own edge (half its bounds' diagonal),
via `SettlementLodBaker.ScreenHeightAt` at FOV 60; measured gains in [Workshop.md](Workshop.md) Gotchas.

| Piece | LOD0 / LOD1 (level 1) / LOD2 (level 3) / cull, metres past its edge |
| --- | --- |
| Inside (rooms, tunnels, machinery, furniture, vault door) | 25 / to cull / — / 150 |
| Outside (`Static_Mountain*`, `Static_ShipTunnel*`, everything under `GearDoor`) | 100 / 300 / beyond / 5000 (never on the map) |

The 116 lamps go out by distance (`DistanceCulledLights`: 150 + 18 m lamp range, back 10 m nearer).

## Key types

| Type | Role |
| --- | --- |
| [CharacterLodProfile.cs](Assets/Game/Editor/Agents/CharacterLodProfile.cs) | editor-only tunables: thresholds, mesh levels, garment roles, shadows, skin quality, folders |
| [CharacterLodWiring.cs](Assets/Game/Editor/Agents/CharacterLodWiring.cs) | *Tools ▸ SpaceGame ▸ Agents ▸ Wire Character LODs*; `Wire`, `EnsureIfCovered`, `IsLodCopy` |
| [CharacterLodMeshes.cs](Assets/Game/Editor/Agents/CharacterLodMeshes.cs) | builds compacted reduced meshes, stamped with the FBX's dependency hash so they regenerate after a re-export |
| [SculptCharacterBuilder.cs](Assets/Game/Editor/Agents/SculptCharacterBuilder.cs) | `BuildAll` wires new prefabs; `UpdateBehaviourAll` calls `EnsureIfCovered`; `TryMeasure` skips LOD copies |
| [NomadBuildPrefabTests.cs](Assets/Game/Editor/Tests/NomadBuildPrefabTests.cs) | `IsLowerDetailStandIn` exempts renderers listed only in non-LOD0 tiers from collider coverage |
| [WorkshopLodBuilder.cs](Assets/Game/Editor/Workshop/WorkshopLodBuilder.cs) | `Attach(root)`, the last step of *Rebuild Workshop Prefab*: a group per enabled `MeshRenderer` not already in one (the Home's `Deco_*` keep theirs); reach tunables are its constants |
| [DistanceCulledLights.cs](Assets/Game/Scripts/Core/Rendering/DistanceCulledLights.cs) | runtime, presentation only: switches the lights under it (those authored on) off beyond `lightsOutBeyond` from `ViewCamera`, with hysteresis; all back on in `OnDisable` |

The settlement passes (builds, decorations, kits) are **throw-away scripts, not builders**, like
the rest of the model pipeline ([ArtPipeline.md](ArtPipeline.md)).

## Flows

- **Raxy:** garment role = largest face of its rest-pose mesh bounds as a share of the body's.
  < 0.06 accessory (LOD0 only); ≥ 0.1 silhouette (kept to LOD2); ≥ 0.2 casts shadow at LOD1. Eyes
  stay in LOD1 as the same renderers (blink and dead lids still reach them), drop at LOD2. LOD1
  mesh level 1 at skin `Auto`, LOD2 level 2 at `Bone2`; only the body casts at LOD2. Meshes under
  1000 tris are not reduced.
- **Builds:** per renderer, the coarsest level that pops ≤ 5 % of the object's pixels at the
  LOD1 switch, ≤ 10 % at the LOD2 switch; none passing = full mesh. LOD1 drops `C__` renderers
  < 1.5 m, LOD2 keeps shell, door/gate and `C__` ≥ 6 m. Nested `NomadDoor`/`NomadFenceGate`
  renderers are in all three tiers.
- **Decorations:** LOD1 drops parts < max(0.10 m, 0.12·L), unshadows parts < max(0.4 m, 0.2·L);
  LOD2 keeps parts ≥ max(0.3 m, 0.25·L) plus the biggest and any emissive part. A reduced level
  is kept only if each material's area changes ≤ 12 % (LOD1) / 18 % (LOD2) and mis-shaded area
  (> 40° off vertex normals) grows ≤ 6 % / 15 %. Items < 2000 tris or parts < 256 are not reduced.
- **Tents:** LOD1 drops `*_Anchor*`, `*_WallFix*`; LOD2 keeps `*_Canopy`, `*_Mast*`.
  **Structural kit pieces** (walls, courses, pillar, flights, landing, street stairs/walls): one
  level culled at 0.01, because they hide heightmap steps. **Flat paving**: threshold
  `size × lodBias / (2·tan30°·250 m)` so the whole network culls on one 250 m ring; stepping
  stones the same at 50 m. Kits use CrossFade with `animateCrossFading`.

## Multiplayer

N/A — client-local rendering only (`DistanceCulledLights` reads its own `ViewCamera`). No NetworkObject was added or changed; Raxy
`GlobalObjectIdHash` values are unchanged and still in `DefaultNetworkPrefabs.asset`.

## Persistence

N/A — no runtime state. LOD is authored on prefabs; a lamp put out by distance is recomputed, not saved.

## Gotchas

- **Workshop: Mesh LOD level 2+ sheds the thin clay walls and ceilings** (StoreHall's whole shell at
  level 3); level 1 matches level 0. Inside pieces stop at level 1; only outside pieces reach level 3.
- **Workshop: screen-size culling never culls a room inside the mountain** (a 30 m room stays above
  1 % screen height to ~5 km), hence distance past its edge; from outside only the skin and gate show.
- **Workshop LOD lives in the builder:** tune `WorkshopLodBuilder`'s constants, never the prefab.

- **Batching Static silently removes Mesh LOD.** The batched renderer draws `Combined Mesh`
  (`lodCount 1`); `forceMeshLod` and `meshLodSelectionBias` are ignored. A renderer whose mesh has
  `lodCount > 1` must not be Batching Static (kit pieces 94 → 90). `StaticBatchingMeshSizeTests`
  does not catch this. Decoration LOD meshes are plain single-level meshes and stay batched.
- **Automatic Mesh LOD barely acts at `QualitySettings.meshLodThreshold = 1`**: a 37 m build stays
  at L0 to 640 m, masts never leave L0, paths stay L0 to ~200 m. Bias shifts every distance, near
  ones too — hence forced levels on copies for builds.
- **A LODGroup tier cannot choose a mesh level**; that needs a copy renderer with `forceMeshLod`.
- **Every renderer the prefab owns must be in a tier.** A LOD copy in no tier draws at every
  distance on top of its original (z-fighting).
- **A build or outpost LODGroup must never list renderers inside a nested `Deco_*` instance** — it
  culls itself, and a renderer in two groups is toggled by both. Nested Door/Gate renderers *do*
  belong in the build's group; never give `NomadDoor`/`NomadFenceGate` a LODGroup of their own.
- **Decoration LOD children carry their part's exact name, parented under the part.**
  `OutpostPrefabBuilder.HideRemovedParts` and `OutpostPrefabTests` match renderers by name; an
  `X_LOD1` would stay visible when its part is hidden. Never merge an item's parts into one LOD mesh.
- **Unchecked simplification wrecks this flat-shaded kit**: nets fill into sheets, trunks fold
  into wedges, walls streak, B16's foundation smears, HearthPlaza/MarketPlaza decks vanish at
  L2, L3+ collapses masts and lattices. FarmPlot and AnimalPen generate no levels. Look first.
- **Climbable tower frames keep full detail** (`C__` BellFrame/Watchtower with a `<name>_Ladder`
  sibling): `EveryTowerMeshCopyHasALadderBesideIt` would fail on LOD copies.
- **Built-in Mesh LOD saves no skinning on a `SkinnedMeshRenderer`** — it deforms LOD0 regardless —
  hence the Raxy SMR copies. Re-run *Wire Character LODs* after any outfit change, including the
  external `raxy_garment_kit` outfit script; a missing garment draws at every distance.
- **Garment size comes from mesh bounds, never `lossyScale`**: garments and the body sit under
  differently scaled transforms.
- **`SkinQuality.Bone2` moves far-tier vertices up to 2 cm** against the full mesh; compare with
  `Bone4` before blaming mesh extraction.
- **Tools that sum every SMR now see the copies:** `RagdollRig.MeasureRig` importance totals and
  `HandToolRig.Bake` renders include them.
- **A tent LODGroup's `m_Size` is in root-local space**; multiply by the root scale (0.3–0.65).
- **In a preview scene an animated cross-fade never settles** (edit-mode time does not advance):
  set fade mode None on the instance to judge steady-state tiers.
- **Mesh LOD roughly doubles a shell's index buffer** (L1–L3 add ~87 % of L0's indices).
- **SSAO stays on the DepthNormals source ([PC_Renderer.asset](Assets/Game/Settings/PC_Renderer.asset)).** Tried 2026-10-05 to drop the normals prepass: `Source: Depth` with `AfterOpaque` removes the pass but puts visible grain and dark halos on flat walls (normals reconstructed from depth, even at High), and `Depth` *before* opaques keeps a depth-only prepass — same draw count — so it saves almost nothing. Nothing else samples `_CameraNormalsTexture`. The cost is cut instead in the AO pass itself (2026-10-28): `Downsample` on and `BlurQuality` Medium (see [Environment.md](Environment.md) Gotchas). The prepass stays.
- **The LOD mesh assets are in Git LFS** (`deco_*_lod.asset`, `*_LOD[0-9].asset` in `.gitattributes`):
  ~43 MB Raxy + ~88 MB decorations of YAML. A clone without `git-lfs` gets pointer files and every
  LOD tier renders nothing.

## Extending

- **New Raxy outfit or garment:** run *Wire Character LODs*; tune `CharacterLodProfile.asset`,
  never the code defaults. Other species: add their folder to `characterFolders`.
- **New decoration:** give it a root LODGroup by size class, with LOD children named like their
  parts; do not enable `generateMeshLods` on its FBX (its parts are Batching Static).
- **New or re-exported build shell:** enable `generateMeshLods`, re-measure the forced levels
  against pop, clear Batching Static on renderers with levels, list every owned renderer in a
  tier, and keep nested `Deco_*` out.
- **New kit tile:** keep it batched if it is under ~256 tris (no levels are generated); cull
  paving by distance, not by size, so a network ends on one ring.
