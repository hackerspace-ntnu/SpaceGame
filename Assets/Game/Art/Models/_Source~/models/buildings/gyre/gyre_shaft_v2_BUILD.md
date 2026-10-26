# gyre_shaft_v2 — build record

The Gyre shaft rebuilt as a machine. This follows the review of `gyre_shaft`, which read as too much carved stone and not enough technology. Reference: the shaft illustration with towers and gantries, added to the POI repo's inspiration on 2026-10-22.

- `gyre_shaft` stays as the stone-led version.
- The carved bore, galleries, chambers, collars, stepped Undercroft dome, rib frames, arcade and portals carry over from it unchanged.

Built headless with Blender 5.1.2 from `gyre_shaft_v2.py`, using `_gyrelib.py`. All materials are linked from `palette.blend`.

## What is new

| Element | What it is |
|---|---|
| Drive towers A, B, C | Three towers at 90°, 210° and 330°. Each rises out of the basin on a stone plinth: a 5 m radius Undercroft stage at 16 m off-axis, then two ball joints and a sloped sleeve with hydraulic struts that bring it in to 8 m off-axis at the shaft foot. Two stages follow (radius 3.6 m, then 3.2 m above a collar at −97 m). They end in caps and masts at −7, −37 and −17 m; tower A's mast stands above the rim. |
| Tower drums | 10 m segments (one mesh per radius, instanced). Each has a dark flange, bleached plating with two grooves, a recessed band with red light arcs, ribs, clamp blocks, plates with indicator lights, and two pipes facing the shaft axis. |
| Gantries | 13 Warren-truss walkways at nine levels, linking towers to each other, to the galleries and to Landing A. They have red end lights. |
| Chains | Five heavy chains slung between towers and wall anchors |
| Wall tech | Plated tech rings with red strips at −11, −15, −100, −131, −157 and −193 m. There is a red strip under every gallery opening. |
| Core | A hexagonal housing on a drive spindle rising from the basin, with a cage of six posts and a mast above. Inside, an elongated hexagonal crystal (`Mesh_GyreShaft_Core`, the part the player takes) glows amber. |
| Clamp arms | Six arms, two per tower at −199 and −209 m. Each has a shoulder, upper arm, elbow, forearm, a pad against the housing and twin hydraulic rams. One arm per task: turning a rod releases one arm. Separate objects so each can be animated. |
| Rods | Six rods, two per tower, run from wall anchors over the terraces (saddles at the drops, a bearing pier at the basin edge) into the tower bases. |

The v1 hanging rings, stone bridges and gimbal machine rings are gone.

## Materials

| Use | Palette material |
|---|---|
| Carved stone | `Mat_Stone_Greystone_Warm` (darker, so the machinery reads) |
| Recesses | `Mat_Stone_Granite_Dark` |
| Tower plating, arm shoulders | `Mat_Paint_Hull_Bleached` |
| Structure | `Mat_Metal_Steel_Dark` |
| Wear faces, races, pads | `Mat_Metal_Steel_Worn` |
| Ram rods, spindle | `Mat_Metal_Chrome_Scuffed` |
| Light strips | `Mat_Emissive_Red_Warn` |
| Core | `Mat_Emissive_Amber` |
| Crater | `Mat_Stone_Redrock_Mesa` |
| Water placeholder | `Mat_Glass_Canopy_Tinted` |

## Numbers

About 376k triangles in the scene with instances. The unique meshes are much smaller:

| Part | Triangles |
|---|---|
| Rock | 93k |
| Crater | 7k |
| Tower drum (each of 3 meshes) | 1.9k |
| Tower joints (each of 3 meshes) | 4k |
| Gantries | 11k |
| Wall tech | 13k |
| Core housing | 0.8k |
| Each clamp arm | 0.4k |

## Known limits

- **The rock is still one mesh.** It needs chunking by depth band before Unity.
- **Crater rock and water are open surfaces by design.**
- **Gantry ends simply run into the tower drums and the wall.** Proper landing collars and wall doors are still to do.
- **Traversal between gantries is not routed yet.** There are no ladders or lifts.
- **The light strips are lit in this file.** For the dormant state they need a dark variant until the core is taken.
