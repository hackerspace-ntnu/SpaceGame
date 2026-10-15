# cliffHouses.blend — build record

A 205-asset low-poly kit (100 in the first wave, 51 added in revision 4, 54 in revision 5) for a pastel sky city on cliffs: round clay / straw / living-tree buildings, five bridges,
round platforms and sloped rope-fenced paths, nets, ropes, pillars, cliff stones, trees, plants, lights, decorations
and interior pieces. Built 2026-10-10 as a new file. Everything is in this one .blend, every asset is a **collection
asset** (`CH_<Name>`) with a `Root_<Name>` empty and un-joined, named parts under it, a rendered thumbnail, tags and a
catalog under `CliffHouses/…` in `blender_assets.cats.txt`.

## Conventions

| | |
| --- | --- |
| Scale | true scale, player = 3 m (authored at 1.8 m and multiplied by 5/3 — `S` in `chlib.py`) |
| Small round wall | radius 3.67 m, height 4.33 m — `Foundation_Round_S`, `Wall_Round_S`, `Wall_Round_Bulb`, all `_S` roofs, `Roof_Clay_Dome`, `Roof_Clay_Onion` |
| Large round wall | radius 5.67 m, height 5.0 m — `Foundation_Round_L`, `Wall_Round_L`, `Roof_Clay_Tiered`, `Roof_Straw_Mushroom` |
| Tower wall | radius 3.33 m tapering, height 8.67 m, joist ring at 4.5 m for `Balcony_Ring` + `Stairs_Curved` |
| Doors / windows | origin on the wall face, facing −Y. Door at angle −90°; place windows at `(r·cos a, r·sin a, z)` with Z rotation `a + 90°` |
| Roofs | origin at the eave line = top of the wall |
| Bridges, ramps, rope spans | origin at the first end, run along +Y; both bridge ends are at the same height |
| Pillars, hanging things | origin at the TOP (they hang down from what they carry) |
| Cliff blocks | origin bottom centre; `Cliff_Cap_Grass`, `Cliff_Ledge_Grass`, `Cliff_Underside` have the origin on their top surface |
| Materials | 62 flat materials `Mat_CH_<Group>_<Name>`, local to this file (a new art direction, so not taken from the SpaceGame palette) |
| Rock colouring | faces are tinted by direction — cream tops and ledges, peach/rose/ochre sunlit sides, slate-blue shadow sides and cracks; a grey stone set exists too |

## How the houses are made

The 12 buildings are assembled from the Building Kit as **linked duplicates** (shared mesh data): edit
`Mesh_Wall_Round_S_Wall` once and every house using it follows. Walls have real openings (you can walk in), door
leaves are separate objects with the origin on the hinge, the pinwheel wheel has its origin on the hub.

## Chain offsets (real metres)

| Piece | Next piece at |
| --- | --- |
| `Path_Ramp_Straight` | (0, 10, 2.5) |
| `Path_Walkway_Flat` | (0, 6.67, 0) |
| `Path_Stairs_Wood` | (0, 4.27, 2.67) |
| `Path_Stone_Tile` | (0, 3.33, 0) |
| `Fence_Rope_Segment` | (0, 5, 0) |
| `Path_Ramp_Curved` | (8.33, 8.33, 2.5), turned −90° |

Bridge lengths: Rope_Plank 20 m, Rope_Net 30 m, Straw 15 m, Stone_Arch 16.7 m, Wood_Covered 16.7 m.

## Scenes

- `CliffHouses` — all assets laid out in rows per category.
- `CH_Showcase` — an example town made only of collection instances (delete it freely; nothing depends on it).

## Not done

- No armatures. The moving parts (door leaves, pinwheel) are separate objects with correct pivots instead.
- No UVs / textures — flat colours only.
- `cliffHouses_gen/` holds the generator. It always writes a NEW file; never run it over a hand-edited
  cliffHouses.blend. The .blend is the source of truth.

## Revision 2 (2026-10-10) — more detail on trees, rocks and roofs

File regenerated (it had not been edited since revision 1). About 200k triangles in total, up from 131k.

- **Rocks** are fractured cliff masses instead of plain hulls: blocks stepped in and out with crack faces, strata
  ledges, split-off slabs, rubble, moss and tufts. Stronger, less pastel stone colours. 1.3k–3.8k tris each.
- **Trees** have a gnarled trunk with root flares and burls, recursive forked limbs, and crowns of flat-bottomed
  cloud pads in three leaf tones (dark underside, mid, sunlit top). 4.9k–6.8k tris each.
- Tree houses, bushes and the crooked pillar use the same leaf pads. Thatch has five ragged tiers with hanging
  tufts; clay roofs have stepped tile courses in two tones.

## Revision 3 (2026-10-10) — plain rocks

The cliff pieces are plain base shapes again so they can be formed by hand: one clean quad-grid mesh per rock
(rings x columns, 400-700 tris), no spikes, teeth, slabs, rubble or moss. The stronger stone colours from
revision 2 are kept. Grass, tufts and flowers on the cap, ledge and stack remain separate parts. Each boulder in
`Rock_Boulders` is its own object. Trees and roofs are unchanged from revision 2.

## Revision 4 (2026-10-10) — 51 more building blocks (151 assets, about 250k triangles)

New catalog **CliffHouses/Giant Tree** plus additions to the existing ones. Generator: `cliffHouses_gen/assets_more.py`.

| Group | Assets |
| --- | --- |
| Giant tree (trunk radius 5 m real) | `GiantTree_Trunk_Base` (16.7 m tall), `GiantTree_Trunk_Section` (stackable, 13.3 m), `GiantTree_Crown_Fork`, `GiantTree_Limb_Long`, `GiantTree_Limb_Rising`, `GiantTree_Canopy_Clump`, `GiantTree_Canopy_Shelf`, `GiantTree_Root_Arch`, `GiantTree_Stump_Platform` |
| Wraps the giant trunk (origin on the trunk axis) | `Trunk_Walkway_Spiral` (half turn, climbs 6.67 m; next one turned 180 degrees), `Trunk_Platform_Ring`, `Trunk_Platform_Nest`, `Trunk_Stairs_Pegs`, `Trunk_Door_Hollow`, `Trunk_Window_Knot`, `Trunk_Mushroom_Steps` |
| Building kit | `Roof_Woven_Wide`, `Roof_Palm_Leaf`, `Roof_Turret_Cone`, `Turret_Round_Small`, `Wall_Stick_Open`, `Floor_Upper_Round`, `Porch_Awning`, `Dormer_Round`, `Railing_Stick_Straight`, `Railing_Stick_Curved`, `Stairs_Spiral_Inner` |
| Town | `Gate_Arch_Town`, `Lift_Basket_Crane`, `Landing_Pier`, `Water_Tower_Barrel`, `Aqueduct_Bamboo`, `Fountain_Clay`, `Garden_Planter_Round`, `Garden_Trellis`, `Stall_Market_Long`, `Cart_Hand`, `Banner_Pole_Tall`, `Banner_Ribbon_Spiral`, `Laundry_Line` |
| Jungle plants | `Palm_Small`, `Plant_Broadleaf`, `Fern_Cluster`, `Flower_Giant` |
| Interior | `Workbench_Tools`, `Storage_Chest_Round`, `Kitchen_Counter_Curved`, `Hanging_Rack_Pots` |
| Lights | `Lantern_Cluster`, `Lantern_Wall_Sconce`, `Jar_Fireflies` |

Separate movable parts with pivots: lift basket, cart wheels, chest lid.

## Revision 5 (2026-10-10) — 54 more assets at higher detail (205 assets, about 400k triangles)

Generator: `cliffHouses_gen/assets_more2.py`. Higher segment counts than the first wave; stonework is laid as
individual blocks, roofs as individual tiles / scales / straw bundles.

| Catalog | Assets |
| --- | --- |
| Buildings | `House_Jungle_Hut`, `House_Palm_Hut`, `Tower_Watch_Stilts`, `Windmill_Round`, `Greenhouse_Dome`, `Forge_Open`, `House_Grand_Tiered`, `Granary_Stilts` |
| Decorations | `Airship_Small`, `Birds_Flock`, `Statue_Owl_Stone`, `Bell_Frame`, `Notice_Board`, `Bench_Tree_Ring`, `Table_Feast_Long`, `Beehives_Straw`, `Pottery_Kiln_Wheel`, `Kite_String` |
| Building Kit | `Roof_Clay_Scales_L`, `Roof_Straw_Shaggy_S`, `Cupola_Lantern`, `Window_Arch_Shutters`, `Door_Arch_Double`, `Balcony_Half`, `Chimney_Stone`, `Foundation_Rock_Steps`, `Support_Bracket_Big`, `Wall_Round_Arcade_L` |
| Platforms & Paths | `Stairs_Stone_Straight`, `Wall_Retaining_Stone`, `Terrace_Crescent_Grass`, `Path_Plank_Short`, `Lift_Platform_Rope` |
| Nets & Ropes | `Zipline_Cable` |
| Cliff & Rocks | `Rock_Arch_Gate` |
| Bridges | `Bridge_Root_Living` |
| Plants | `Waterfall_Cliff`, `Pond_Lily`, `Bush_Berry`, `Mushroom_Giant`, `Log_Fallen_Mossy`, `Reeds_Cattails` |
| Trees | `Tree_Willow`, `Tree_Pine_Cloud`, `Tree_Saplings` |
| Lights | `Campfire_Circle`, `Lamp_Street_Double`, `Candles_Set`, `Lanterns_Floating` |
| Interior | `Bed_Bunk`, `Bookshelf_Tall`, `Desk_Writing`, `Armchair_Wicker`, `Bathtub_Wood` |

Separate movable parts with pivots: windmill rotor and sails, airship propeller, bell, window shutters, double-door
leaves, lift platform, zip-line trolley, potter's wheel head, kite, each bird, each sapling, each rock of the arch.
`Glass_Clear` (greenhouse) is the only see-through material (alpha 0.28, blended).
