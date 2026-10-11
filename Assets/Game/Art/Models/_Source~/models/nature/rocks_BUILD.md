# rocks.blend — build record

100 realistic rock building blocks for building up terrain features: overlap, rotate, scale and bury them to
make cliffs, canyon walls, boulder fields and outcrops. Built 2026-10-10 as a new file. Every rock is one
closed mesh object, marked as an asset with a rendered thumbnail, tags and a catalog under `Rocks/…`
(`blender_assets.cats.txt`). The folder is registered in Blender as the asset library **SpaceGame Nature**.

## Conventions

| | |
| --- | --- |
| Scale | true metres, sized for the 3 m player. Rubble 2–6 m, boulders 5–16 m, blocks and strata 10–40 m, cliffs up to 44 × 18 × 43 m |
| Triangles | 200–1 484 per rock, 943 on average, 94 336 in total |
| Origin | bottom centre of the bounding box; transforms applied |
| Shading | smooth, with sharp edges marked wherever two faces meet at more than 26–36° |
| Materials | one per rock, linked from `../../palette.blend` (`Mat_Stone_*`); no UVs, no textures |
| Names | object `Rock_<Type>_<nn>`, mesh `Mesh_Rock_<Type>_<nn>`, collection `Coll_Rock_<Type>` |
| Scene | `Rocks` — one row per colour group, sorted by type |

## Types

| Type | Count | What it is for |
| --- | --- | --- |
| Boulder | 10 | weathered boulders with worn edges |
| Chunk | 10 | freshly broken angular rock |
| Block | 12 | jointed, roughly box-like masses; stack them for a broken rock face |
| Slab | 10 | thick plates: ledges, floors, tilted beds |
| Strata | 12 | layered sedimentary masses with stepped beds |
| Cliff | 12 | tall wall masses with vertical faces; overlap several for a rock wall |
| Outcrop | 8 | long low bedrock spines with dipping layers |
| Pillar | 8 | stout free-standing stacks |
| Wedge | 6 | tilted masses thrusting out of the ground |
| Ledge | 6 | wall pieces whose hard cap overhangs on one side |
| Rubble | 6 | small loose rocks for talus and scatter |

## Colours (fixed per rock)

| Catalog | Rocks | Materials |
| --- | --- | --- |
| Rocks/Yellow Desert | 25 | Sandstone_Pale, _Gold, _Ochre, _Tan, _Mustard |
| Rocks/Red Desert | 28 | Redrock_Terracotta, _Rust, _Crimson, _Salmon, _Mesa, _Umber |
| Rocks/Grey Stone | 33 | Granite_Pale, _Mid, _Dark, Slate_Blue, Greystone_Warm, Basalt_Charcoal, Limestone_Chalk, Mudstone_Brown |
| Rocks/Pastel | 14 | Pastel_Butter, _Pink, _Lilac, _Mint, _Sky, _Peach |

All 25 are new `Mat_Stone_*` entries in the shared palette (it had no stone before), added by
`rocks_palette.py`. Three of them (Redrock_Crimson, Greystone_Warm, Mudstone_Brown) sit within deltaE 5 of an
existing paint / wood / hide material; they were added anyway because those surfaces render wrong on rock.

## How a rock is made (`rocks.py`)

A dense sphere (20k–80k triangles) is shaped by a radial function: a noisy ball intersected with 15–30 fracture
planes, scooped by a few concave bites, stretched to real size, then "jointed" — along three joint directions
the surface is snapped onto stepped planes, which gives flat fracture faces, bedding and ledges. The result is
collapsed down to its triangle budget. Every type uses different plane sets, joint spacing and proportions.

## Not done / known

- No UVs or textures, no LODs, no colliders, no Unity export.
- No armature: nothing on a rock moves.
- A few rocks keep a small sliver left by the collapse (e.g. the underside of one Strata piece).
- `rocks.py` always writes a NEW file and refuses to run while rocks.blend exists. The .blend is the source of
  truth; never regenerate over hand edits.
- An earlier stylised version (hoodoos, crystals, arches, basalt columns, 10–500 triangles) was dropped on
  request and is not in the file.
