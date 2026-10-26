---
system: VegetationFamilies
layer: presentation
summary: Four biome families of new plants (Frill, Lattice, Starburst, Scalewood) on the StylizedFoliage contract
paths:
  - Assets/Game/Art/Models/_Source~/models/nature/vegetation_families.blend
symptoms:
  - "the weepfan's paddles are half red, as if the red rib were painted across the leaf"
  - "a midrib or rib smears off its leaf into a wide fin"
  - "a family prefab has no LODs: one mesh on the root and an empty LODGroup"
reads_with: [Foliage, VegetationKit, BarkMaps, LevelOfDetail]
updated: 2026-10-25
---

# Vegetation Families

Four families of plants made to stand together as a biome: a few tree shapes, a bush or two, small
plants and a ground prop per family, plus colour variants. Same shader, wind, LODs and per-vertex
contract as every other plant in [Foliage.md](Foliage.md); what is new is the *shapes* — ruffled
fan leaves, Voronoi lattices, curled star petals, needle brushes and arching strap leaves.

## Model

- **Source**: [`vegetation_families.blend`](Assets/Game/Art/Models/_Source~/models/nature/vegetation_families.blend),
  one `Coll_<Prefab>` per plant with `<Prefab>_LOD0/1/2`, slots `Foliage_Bark/Leaves/Fruit`, UV0–UV3
  as in Foliage.md, custom normals. It is the only copy; the generator lives in no repo.
- **Prefabs** `Prefabs/Environment/Nature/Vegetation/<Prefab>`, FBX `Environment/nature/Vegetation/<lowercase>.fbx`.

Sizes are deliberately huge for the 3 m player (user: "the plants should be very large"):
trees 20–38 m, bushes 4–6 m, small plants 3–13 m.

| Family | Biome | Prefabs (height, LOD0 tris) | Look |
| --- | --- | --- | --- |
| **Frill** | wet: swamp, marsh | Tree_Frill_A (26 m, 10.7k), _B (24 m, 8.4k), Tree_Frill_Tower_A (36 m, 12.8k), Bush_Frill_A (5 m, 3.4k), Plant_Frill_Sprout_A (12 m, 2k), Plant_Frill_Roots_A (3 m, 1.7k, ground prop) | gnarled slate trunks on stilt roots wrapped in ochre vines, holding rosettes of big ruffled sage-teal fan leaves |
| **Lattice** | dry: badlands, red desert | Tree_Lattice_A (24 m, 2.6k, domed), _B (20 m, 2.3k, dished), Tree_Lattice_Tiered_A (35 m, 5.1k), Plant_Latticepod_A/B (6.5 m, 3.4k/2.3k), Bush_Bonecoral_A (4.3 m, 1.1k) | bone-pale fluted strands twisting up into perforated (Voronoi) tan shelves; sage cage pods on root webs; dry rust coral |
| **Starburst** | dry: savanna, steppe | Tree_Starburst_A (26 m, 5.7k), _B (22 m, 4.2k), Tree_Starburst_Spent_A (24 m, 2.6k, dead heads), Plant_Starburst_Single_A (13 m, 0.9k), Plant_Starburst_Rosette_A (3 m, 0.7k) | grey leaning stalks fanning from a sprawled burl, each topped by a coral-rust star over a shaggy straw collar |
| **Scalewood** | wet: primeval forest | Tree_Scalepole_A (38 m, 3.5k), _B (26 m, 2.1k), Plant_Scalepole_Shoot_A (9.5 m, 1.1k), Plant_Scalepole_Stumps_A (5.4 m, 0.5k), Tree_Weepfan_A/B (20 m, 5.2k/4.2k), Bush_Spoonleaf_A (12.5 m, 4k) | lepidodendron-like scaled poles forking into bottlebrush tips; slender stems with hubs of hanging teal paddles on red ribs; a rope-trunked bush of slate spoon leaves with pale tips |

- **Size is a uniform per-model factor (1.8–2.5×) baked into the meshes** after building, so the
  wind/colour data (all relative) is unchanged and prefabs keep scale 1. Because LOD switching is
  screen-relative, these plants hold LOD0 about twice as far as a same-shape plant half their size.

- **Colour variants** (prefab variants that swap one leaves material): `Tree_Frill_A/B_Plum`,
  `Bush_Frill_A_Plum` (muted mauve), `Tree_Lattice_A_Bone`, `Tree_Lattice_Tiered_A_Bone` (pale shelf),
  `Tree_Scalepole_A/B_Rust` (dry season), `Tree_Weepfan_A_Dry` (straw).
- **Materials** `Mat_Foliage_<name>`: each family's bark/leaves/fruit share one wind profile from
  Foliage.md — Frill trees *tree* (bush + sprout *bush*), Lattice + coral *rigid*, Starburst *tree*,
  Scalepole *conifer*, Weepfan *willow*, Spoonleaf *bush*. Bark maps ([BarkMaps.md](BarkMaps.md)):
  Frill Furrowed, Lattice Weathered, Bonecoral Pored, Starburst Weathered, Scalepole Shingled,
  Weepfan Ringed, Spoonleaf Coiled. No emission anywhere; colours are muted.
- **LODs**: thresholds from `Tree_Broadleaf_A` (Tree_*) or `Bush_Round_A` (the rest). LOD1 is the
  same shapes at lower resolution (frill leaves 3×8, straps 4 segments, 40 % of the needles ×1.45);
  LOD2 drops inner rosette layers, every second strap and swaps each lattice for a solid wavy disc
  (shelves) or lumpy ball (pods); brushes keep 12 % of the needles ×2.3.
- **Colliders**: root `CapsuleCollider` on trees (trunk / fluted bundle / burl), the stumps and the
  root sprawl; none on bushes, pods or small plants.

## Key types

| Asset | Role |
| --- | --- |
| [vegetation_families.blend](Assets/Game/Art/Models/_Source~/models/nature/vegetation_families.blend) | The 24 models; export with `_exportlib.export_collections`. |
| `StylizedFoliage` (see [Foliage.md](Foliage.md)) | Draws them; nothing family-specific in the shader. |

## Flows

How the shapes are built (all on the shared vertex contract):

1. **Frill leaf**: a polar fan about its petiole, opening over the first third; the rim is
   scalloped in-plane and ruffled out of plane, growing toward the edge. Rosettes are 3 layers
   (splayed → cupped heart), each nodding a little off vertical.
2. **Lattice**: Voronoi edges of Poisson points in a wobbly disk (`delaunay_2d_cdt` →
   circumcentres), clipped to the rim, draped on a bowl/dome height field; every edge is a flattened
   4-sided band, every junction a 6-sided lens. Pods use the convex hull of points on a sphere.
3. **Star / collar**: folded curved spikes (3 verts per ring) curling toward the head's axis.
4. **Strap leaf** (paddle, club, blade): a V-folded strip arching under its own droop.
5. **Brush**: needles whorled along a branch at a golden-angle twist, leaning toward its tip.

## Multiplayer

N/A — static scene props, no NetworkObject; wind comes from the shared clock.

## Persistence

N/A — no runtime state; placement lives in the scene or TerrainData.

## Gotchas

- **A rib on a leaf must carry the leaf's normal, not its tube normals.** The shader flutters every
  vertex along its *normal*; a 3-sided rib with radial normals flies apart into wide coloured fins
  (the weepfan's paddles looked half red). Ribs and midribs pass the blade's normal per ring.
- **The FBX exporter skips viewport-hidden objects** (`export_collections` selects them). Hiding
  LOD1/2 with `hide_set` in the .blend shipped LOD0-only FBX; Unity then put the single mesh on the
  root with an empty LODGroup. Hide previews with `hide_render` only.
- **Unity adds a LODGroup itself** to a model whose children end in `_LODn`: `AddComponent<LODGroup>`
  on the instance returns null — reuse the existing one.
- Not seen in play mode, with live wind, on a client or on Windows; not placed in any scene yet.

## Extending

- **New member of a family**: add a species to the generator (or hand-model in the .blend) keeping the
  slots and UV layout, export its collection, then a prefab variant of the FBX with the LOD/collider
  rules above and the family's three materials (their wind profile must match).
- **New look**: copy the family's leaves material, recolour, and make a prefab variant that swaps it.
