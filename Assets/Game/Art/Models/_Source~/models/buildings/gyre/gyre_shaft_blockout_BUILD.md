# gyre_shaft_blockout — build record

A grey-box of the Gyre shaft at full scale. It fixes proportions, depths and spaces before any detailing. Not final art.

## What it is

| Element | Numbers |
|---|---|
| Main bore | ~60 m wide at the rim; pinches to 30 m at −50 m; swells to 60 m around −95 m; pinches to 36 m near −135 m |
| Undercroft vault | Opens out from −160 m to a 180 m wide room |
| Undercroft floor | −195 m |
| Galleries | Ledges at −20, −100 and −145 m |
| Landing A | 32 m lobe open to the sky, floor at −45 m |
| Chamber B | Domed side chamber at −85 m |
| Chamber C | Domed side chamber at −120 m |
| Chamber D | Domed side chamber at −150 m |
| Bridges | Arched bridges across both pinches (−52 m and −137 m) |
| Ramp | One ramp from Landing A to the first bridge |
| Hanging rings | Two, in the swell (−90 m, −106 m) |
| Machine placeholder | Six nested rings around a core at −176 m, a pedestal grown from a still-water basin |

The chambers alternate sides of the shaft.

## How it is built

- The rock is one revolved block, carved by an exact boolean: the block minus the bore, the lobe, the chambers and the basin. Carved faces use carved stone, and the crater surface uses natural rock.
- The bore profile, chamber positions and depths are tables at the top of `gyre_shaft_blockout.py`, so the shape is easy to tune.
- Bridges, the ramp, the rings and the machine placeholder are separate objects.
- No library components are used yet: the blockout is about volumes.

## Materials

| Use | Palette material |
|---|---|
| Carved stone (on trial; replaces pale sandstone, which read as clay) | `Mat_Stone_Limestone_Chalk` |
| Crater surface | `Mat_Stone_Redrock_Terracotta` |
| Rings | `Mat_Metal_Steel_Dark` with `Mat_Metal_Steel_Worn` wear faces |
| Core placeholder | `Mat_Emissive_Amber` |
| Water (placeholder; the palette has no water material) | `Mat_Glass_Canopy_Tinted` |

## Numbers

About 17.6k triangles. The shell is about 6.6k.

## Known blockout limits

- The shell is one mesh and has no LODs or chunks yet.
- The landing and chamber edges are faceted where the bores intersect.
- Traversal between landings is only roughed in: one ramp and two bridges.
- The water is a single plane.
