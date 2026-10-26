# gyre_shaft — build record

The Gyre shaft and the Undercroft beneath it, built as architecture. It supersedes `gyre_shaft_blockout`, which stays as the proportions study.

Built headless with Blender 5.1.2 from `gyre_shaft.py`, using `components/structural/gyre/_gyrelib.py`. All materials are linked from `palette.blend`; nothing was added to the palette.

## Layout (rim at z = 0, Z up, origin on the shaft axis)

| Zone | Depth | What is there |
|---|---|---|
| Crown | +4 to 0 m | Three stone terraces stepping up out of the crater rock; 60 m bore |
| Tier 1 | 0 to −28 m | Carved wall with incised line pairs; projecting band at −6.5 m; gallery G1 (−18 to −27 m, 16 lens openings) |
| Tier 2 | −28 to −47 m | Bore narrows; 12 stone fins with pipe bundles in every third bay |
| Landing A | floor −45 m | Open lobe at 210°, reaching the sky; piers, fins and pipes inside it are left out |
| Pinch 1 | −54 m | 31 m bore; stone collar with a segmented metal bearing ring; bridge across |
| Tier 3 | −61 to −84 m | Bore swells; 16 fins with pipe bundles; band at −70 m |
| Swell | −86 to −98 m | Gallery G2 (20 lens openings, 8 m deep); two hanging rings on sheaves and chains; long bridge at the gallery floor |
| Tier 4 | −98 to −117 m | Carved wall with three families of stepped round recesses; band at −114 m |
| Pinch 2 | −137 m | Collar, bearing ring and bridge; 12 fins above it |
| G3 | −144 to −152 m | Gallery, 14 lens openings |
| Chambers | −84, −118, −156 m | Domed rooms B, C, D behind catenary arch mouths |
| Undercroft | −162 to −238 m | 176 m across. Dome of six stepped shells; 12 metal rib frames; a 40-opening ring arcade; four tech ring portals; terraces down to a still-water basin; six rods running in to the basin edge; the machine at −204 m |

## How it is built

- **Rock:** one revolved solid whose inner face is the whole carved interior:
  - crown
  - bore
  - galleries
  - grooves
  - collar channels
  - stepped dome
  - arcade
  - terraces
  
  An exact boolean then carves the landing lobe, the chambers and their arch mouths, the recess families and the portal tunnels. Faces use carved stone, or recess stone in the deep cuts.
- **Bore line:** a monotone cubic through control points, which is the `CP` table at the top of the script.
- **Repeated pieces are linked duplicates:** gallery piers, fins, pipe bundles, rib frames and portal rings. One mesh each, instanced around the axis.
- **Metal is machinery:**
  - bearing rings cut into plates with real gaps
  - pipe bundles with couplings and clamp bars that elbow into the wall
  - I-section rib frames
  - sheaves and chains of real links
  - segmented machine rings
  
  There are no flat inlay stripes.
- **Moving parts are separate objects:** hanging rings, machine rings and the core, so they can be rigged or animated.

## Materials

| Use | Palette material |
|---|---|
| Carved stone (on trial) | `Mat_Stone_Limestone_Chalk` |
| Deep recesses, gallery backs, steps | `Mat_Stone_Granite_Pale` |
| Crater rock | `Mat_Stone_Redrock_Mesa` |
| Machinery | `Mat_Metal_Steel_Dark`, with `Mat_Metal_Steel_Worn` on chamfers and races |
| Core placeholder | `Mat_Emissive_Amber` |
| Water placeholder | `Mat_Glass_Canopy_Tinted` |

## Numbers

About 264k triangles in total:

| Part | Triangles |
|---|---|
| Rock | 93k |
| Crater | 7k |
| Architecture | 28k |
| Systems | 42k |
| Mechanisms | 41k |
| Undercroft | 52k |

## Known limits

- **The rock is one mesh.** It must be split into chunks by depth band before it goes into Unity, with LODs for the chain links and bearing plates.
- **Crater rock and water are open surfaces by design.** They are reported as non-manifold.
- **Traversal is not routed yet.** The bridges and galleries are placed, but ledges, ramps and the paths between landings are not.
- **The core is a placeholder.**
