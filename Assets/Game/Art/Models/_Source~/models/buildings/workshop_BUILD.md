# workshop — build record

A hand-modelled white tank on an ancient planet, dressed into a small
settlement: pastel two-storey outbuildings on scaffolding, cloth awnings hung
off the buildings, a painter's yard, and the growth that says the place is old.

`models/buildings/workshop.blend` — assembled by
`models/buildings/workshop_dress.py`, which **adds to** the file rather than
generating it. The tank and its annex are hand-built and are the source of
truth; see *What was modified* below.

## Site measurements

Everything is placed off the hand-built geometry rather than off round numbers,
because the tank was modelled first and the settlement has to fit it:

| Quantity | Value | Where it comes from |
|---|---|---|
| Ground plane | `z = -0.858` | Both existing objects bottom out there |
| Barrel radius at ground | `2.66 m` | Radial profile of `Mesh_Workshop_TankBody` |
| Lower flange | `z = 4.59` | Growth hangs off it |
| Upper flange | `z = 6.05` | Growth hangs off it; the cone springs from it |
| Cone apex | `z = 7.69` | `cone_radius()` interpolates between the two |

The bounding box reaches `x = 3.14` but the barrel is centred on the world Z
axis — the overhang is a base lug, not an offset tank. Placing things off the
bounding-box centre would have put the whole ring 0.24 m out.

## Decomposition

### New components

| Component | Variations | Why it is separate |
|---|---|---|
| `components/structural/cottage_shell.blend` | `Gable`, `Shed`, `Glasshouse`, `Corner` | The houses. Hollow, enterable shells — not a facade family, so they could not be folded into `shanty_addon` or `cabin_module`, both of which are solid |
| `components/structural/scaffold_bay.blend` | `Undercroft`, `Bay_Single`, `Bay_Double`, `Stilts`, `Ladder` | What the houses stand on, and what climbs the tank. Two different technologies (tube-and-coupler steel, lashed timber) in one file because they do the same job and get swapped for each other |
| `components/structural/facade_awning.blend` | `Shop`, `PolePorch`, `Sail`, `Stall`, `Strip` | Cottage-scale cloth. Separate from `awning_shade` on size grounds — see *Reuse* |
| `components/props/paint_station.blend` | `Trestle`, `PotStack`, `SprayRig`, `SwatchBoard`, `DripSheet` | The painter's yard. Props, not structure, so they live under `props/` and can dress any settlement |
| `components/organic/vine_drape.blend` | `RoofMat`, `DrapeLong`, `DrapeShort`, `Tuft`, `Planter` | The first plant in the library. `organic/` was creature anatomy until now |

### Reused from the library

- `components/structural/awning_shade.blend` — `Coll_Awning_Sagging`, the big
  free-standing shade over the yard.
- `components/props/supply_crate.blend` — `Stack`, `Pallet`, `Open`.
- `components/props/fuel_barrel.blend` — `Drum`, `Jerrican`.
- `components/props/field_bench.blend` — `Sawhorse`, `ToolRack`.

**`awning_shade` versus `facade_awning`.** These are close enough to be worth
justifying. `awning_shade` is free-standing tarpaulin pitched over a work area
and spans 4–6 m; `facade_awning` fixes to a wall and spans 1.8–3.4 m. Hanging a
5.5 m `Coll_Awning_LeanTo` off a 3.4 m cottage puts more cloth in the air than
house, which is why the small family exists. The rule for later: **yard span →
`awning_shade`; cloth belonging to one building → `facade_awning`.** The big one
is still used here, over the yard, exactly as intended.

## How it assembles

Four cottages ring the tank at 8–9 m, each yawed to face it, each lifted onto
its own scaffold base. They sat at 6 m in the first pass and it was wrong — the
houses crowded the barrel and the tank stopped reading as the thing the
settlement was built around. The extra two metres buy a yard.

| Placement | Cottage | Base | Lift |
|---|---|---|---|
| `HouseA` | `Gable` (mint) | `Undercroft` | +1.00 |
| `HouseB` | `Shed` (butter) | `Stilts` | +1.55 |
| `HouseC` | `Corner` (white / mint trim) | `Undercroft` | +1.00 |
| `HouseD` | `Glasshouse` (dusty rose) | `Undercroft` | +1.00 |

Cloth splits by whether it has legs. `Shop` and `Sail` carry no ground poles, so
they go on house facades where there is nothing above the scaffold deck for a
leg to stand on. `PolePorch`, `Stall` and `Strip` stand on the ground and go
against the barrel, which meets the ground all the way round.

Two mounting conventions do the placement work, both inherited rather than
invented:

- **Wall-mounted things** follow `shanty_addon`: mounting face at `x = 0`,
  projecting into `+X`. `on_tank()` and `on_wall()` are the only two places the
  trigonometry lives.
- **A cottage on an `Undercroft`** goes at exactly `+1.00 m`. That number is
  round on purpose so the arithmetic never needs checking.

### Objects, not instances

Every placement **appends real objects and renames them** under a placement
prefix (`HouseA_WallFront`, `TankScaffold_Standard_00`) rather than linking a
collection instance. An instance would be one un-editable empty per building.
The requirement was that every wall, pole, plank and awning stay individually
selectable and movable, so the file carries 291 mesh objects instead of about
30 empties. That is the intended trade.

For the same reason, the five new components break the library's usual
one-merged-mesh-per-variation rule: a scaffold brace or a cottage wall has to be
movable without entering edit mode.

## Materials

Six added to the palette; everything else reused.

| Added | Hex | For |
|---|---|---|
| `Mat_Paint_Mint_Pastel` | `#B9D2BE` | Cottage walls. Forced past `Mat_Fabric_Flag_Bleached` (ΔE 11.8), which is off-white awning cloth at metallic 0, not a green painted wall |
| `Mat_Paint_Butter_Pastel` | `#E8CE8C` | Cottage walls |
| `Mat_Paint_Rose_Dusty` | `#D6A79C` | Cottage walls |
| `Mat_Foliage_Moss_Deep` | `#4E6B3A` | Shadowed foliage mass |
| `Mat_Foliage_Leaf_Pale` | `#7E9B55` | Lit foliage tips |
| `Mat_Wood_Timber_Silvered` | `#9A9186` | Scaffold planks, poles, toe boards |

The painted-hull family had no pastel and no green member, `Foliage` did not
exist as a category at all, and `Wood` held only `Mat_Wood_Ply_Worn` — a warm
brown scavenged plywood. Bare timber left in the sun goes grey, and having both
makes the settlement read as two ages of building.

Two tones of green is the minimum: one flat green has no form. The foliage is
massed from small rotated slabs over a solid dome rather than modelled as
leaves — filling the gaps with more leaves costs ten times the triangles for
the same read at any distance a building is actually seen from.

## What was modified

The only pre-existing things touched were the two hand-built objects' **names**
and **material slots**, which was explicitly authorised:

| Was | Is | Material was | Material is |
|---|---|---|---|
| `Cylinder` | `Mesh_Workshop_TankBody` | `Material.001` | `Mat_Paint_White_Arctic` |
| `Cube` | `Mesh_Workshop_Annex` | `Material` | `Mat_Paint_Blue_Station` |

The default names were misleading — the *Cylinder* is the tank and the *Cube* is
the annex. `Mat_Paint_Blue_Station` was chosen over a neutral because it is
within a couple of points of the pale blue already on the annex, so the hand-set
look survives.

Geometry is provably unchanged: vertex positions, face indices, object location
and object scale all hash identically before and after
(`Cube` 234 v / 208 f, `Cylinder` 1120 v / 1004 f).

## Judgement calls worth revisiting

- **Scale.** "Fully enterable" and "build the houses small" pull against each
  other: two storeys with real 2.05 m and 1.95 m clear heights cannot be small.
  Enterable won on absolute height and "small" was honoured through footprint —
  3.4 × 3.0 m, one room per floor. Ridge lands at ~5.0 m against the tank's
  8.55 m. Shrinking further means giving up walking around inside.
- **`Coll_Vine_RoofMat` is not used here.** It is a flat disc and this tank has
  a conical roof, so the crown is dressed with `Tuft`s bedded into the slope
  instead. The mat is built ahead for a flat-topped structure.
- **Armatures: none.** Nothing on the settlement articulates — the doors are
  modelled already ajar rather than rigged, since these are background
  buildings and a hinged door on each of four houses is rig cost with no
  gameplay behind it. If a cottage ever becomes enterable *during play* rather
  than merely hollow, the doors are the thing to rig first.

## Built ahead

Not needed by this model, made because the component's structure already
existed:

- `scaffold_bay` — `Bay_Single`, `Ladder` (used) but also `Stilts` at a second
  size and the `Bay_Double` sheeting.
- `facade_awning` — all five; this model uses five placements across four
  variations.
- `paint_station` — all five used here.
- `vine_drape` — `RoofMat` and `Planter` beyond what the tank needed.
- `cottage_shell` — four variations; a settlement of this size needs three.
