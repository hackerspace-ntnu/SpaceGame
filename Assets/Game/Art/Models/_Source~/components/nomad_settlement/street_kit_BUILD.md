# street_kit.blend — build record

Concrete street, stair and terrace-wall pieces for a planned settlement (docs/AI/systems/SettlementStreets.md).
Built 2026-10-01 as a new file. It replaced `brick_paths.blend` (the user rejected the brick look and asked for
"just a slab of concrete about 2.5 x 1 with a gap between each other"); that file, its FBX and prefabs were deleted.

## Materials

New, local to this file: `Mat_Nomad_Concrete` (warm light concrete, lighter than the settlement's clay so streets read
as their own material) and `Mat_Nomad_Concrete_Dark` (stair cheeks, expansion joint). A local copy of
`Mat_Nomad_Metal_Charcoal` from `nomad_settlement_decoration.blend` for the weep pipe. Local on purpose, like the other
nomad kits (ArtPipeline.md).

## Components

Each is a `Coll_Street_<Name>` collection with a `Root_Street_<Name>` empty at its origin; every part its own object.

| Collection | What | Origin |
| --- | --- | --- |
| `Slab_Plain` / `Slab_Cracked` / `Slab_Chipped` / `Slab_Worn` | 2.5 × 1.0 × 0.16 m slab; condition varies: split with the small part settled, a chipped corner, rounded and sunk | ground centre, top 4 cm proud |
| `Stairs_Narrow` / `Stairs_Wide` | six 0.25 m risers on 0.45 m treads (one 1.5 m terrace), 2.5 / 5.15 m wide between sloped cheek walls | top landing's front edge at the upper terrace; descends toward −Y |
| `TerraceWall_Plain` / `TerraceWall_Weephole` | 2.5 m of 0.5 m retaining wall (2.5 m tall incl. 1 m foundation), coping, a deck of three slabs 3.76 m deep over a solid fill; weep pipe and expansion joint on the second | top of the face; face toward −Y, deck toward +Y |

The request needed one slab, stairs and a wall; the slab conditions and the weep-hole wall were built ahead so long
streets and walls do not repeat one piece.

## Decisions

- Heights are fixed to a 1.5 m terrace (half the 3 m player) — `SettlementStreetStyle.stepHeight` must match.
- The wall deck is deep on purpose: it hides the terrain heightmap's step (~2 m sample spacing) behind the wall face.
- The fill under the deck uses the face concrete, because a block's end shows wherever a wall bends.
- No armature: nothing moves. `Ref_Scale` holds a 3 m player cylinder and never exports.
