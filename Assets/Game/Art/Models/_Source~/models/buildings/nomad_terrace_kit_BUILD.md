# nomad_terrace_kit.blend — build record

Ten modular pieces for the nomad settlement: paths that can be laid procedurally, stairs that chain over long
climbs, and terrace walls that stack for big height differences. Built 2026-10-01 as a new file at the user's
request ("really nice modular paths that support procedural paths, compatible stairs that can go quite far …
modular walls that act as barriers between terraces … max 10 new models").

## Shared module

Everything snaps to the street kit's terrace step, so the pieces mix with `street_kit.blend` and
`SettlementStreetStyle.stepHeight` (1.5 m).

| Measure | Value |
| --- | --- |
| Terrace rise | 1.5 m = six 0.25 m risers on 0.45 m treads |
| Flight footprint / chain offset | 2.7 m / (0, −2.7, −1.5) |
| Landing chain offset | (0, −3.5, 0) |
| Wide path and flight walk | 3.5 m |
| Narrow path and flight walk | 2.25 m |
| Path tile length | 2.0 m along Y |
| Wall segment | 3.0 m along X, 1.5 m course, front battered 0.1 m per course |
| Course stack offset | (0, −0.1 k, −1.5 k) under a `Wall_Straight` |
| Scale | true scale; the player is 3 m (`Ref_Scale`) |

## Pieces

| Collection | Origin | Notes |
| --- | --- | --- |
| `Path_Straight` | ground centre | Ochre bed, Sand flagstones, Terracotta kerbs in 0.5 m blocks |
| `Path_Narrow` | ground centre | same parts, 2.25 m wide |
| `Path_Node` | ground centre | radial stones and a terracotta medallion, Ø 4.0 m; covers a bend of any angle |
| `Path_End` | joining edge | kerbs end in rounded terminals, one loose kerb, stones thin out and sink |
| `Stairs_Flight` / `Stairs_Flight_Narrow` | top nosing | tuck tread behind the origin, sloped cheeks + coping, mass to −6.1 m |
| `Stairs_Landing` | back edge (where the flight above ends) | flagstone deck, cheeks, four capped corner piers |
| `Wall_Straight` | top of the face | plaster face (smooth-shaded), coping, band, canale, brick patch, deck + fill, footing |
| `Wall_Course` | top of the face | rubble-stone course with a band on top, footing |
| `Wall_Pillar` | top of the face at the joint | battered pier, amber lantern niche with bars and lintel, bands at every course, domed cap; 6.65 m deep |

## Decisions

- **Seamless stone runs:** the path flagstones are a Voronoi pattern periodic along Y, and the course stones are
  periodic along X. A stone cut at a tile edge continues in the next tile with no grout line between the halves.
  This only works unmirrored; a mirrored tile meets its neighbour in symmetric V-shaped stones. `Wall_Straight`
  has no stones across its ends, so it may alternate with an X-mirrored copy to move its patch and spout.
- **Bends without curve pieces:** a node covers any angle, so a generator only needs straight tiles, nodes and ends.
  Segments stop 1.9 m from a node's centre (0.1 m under its rim), so the kerbs end at the rim.
- **Stairs reach the ground:** flight and landing masses go 6.1 m below their origin. A four-flight chain therefore
  stands on flat ground in front of a 6 m wall, and on a hillside the rest is buried.
- **Walls stack instead of stretching:** a taller terrace gets more 1.5 m courses, not a Y-scaled wall, so the
  coping and bands keep their size. The top piece keeps the street kit's 3.75 m deck, which hides the heightmap
  step (SettlementStreets.md).
- **No coincident faces, including between chained pieces:** footings sit 3 cm behind the face and 1 cm short of each
  end, path stones sit on the bed, pier bands are 1 cm taller than the wall bands, and buried bottoms are staggered.
  The whole `Demo_Assembly` (47,780 triangles) was checked for same-facing coplanar overlap: 0 hits.
- **Materials** are local copies of six `Mat_Nomad_*`, same names as the town (ArtPipeline.md). No new material.
- **No armature:** nothing moves.

## Built ahead / not done

The pieces are Blender assets only. There is no FBX, no prefab, and no code that lays them. `SettlementStreetStyle`
could take `Path_Straight` as a slab and the flights as stairs, but the node, chain and stack rules have no code yet.
