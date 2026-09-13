# Nomad tents — tensile shade sails

Ten pitched shade sails, every one a tensioned membrane on raked masts. No walls, no camp
furniture, no props — the sail and what holds it up, nothing else. Sized from a 3.4 m square to an
8.4 m ribbon, laid out on a grid the same way `models/buildings/nomad_settlement.blend` lays out its
buildings.

They replace the earlier ten pastel tarp shelters (ridge tent, lean-to, bell, hoop, market canopy,
wind break, stores drape, pavilion). Of that set only the shade sail was worth keeping; this is that
one turned into a family.

| File | What it is |
| --- | --- |
| `tents.py` | The generator. Seeded, re-runnable, all ten recipes in one place. |
| `tents.blend` | Its output: 10 sails, 144 objects, 60 meshes. |
| `../structural/sail_rig.blend` | The masts and ground anchors they are all rigged from. |
| `components_clean.blend` | The settlement kit. **No longer used by the sails.** |
| `KIT_MAP.md` | What every kit part is. |

```bash
cd Assets/Game/Art/Models/_Source~
blender --background --python components/structural/sail_rig.py -- \
    --out components/structural/sail_rig.blend
blender --background --python components/nomad_settlement/tents.py -- \
    --out components/nomad_settlement/tents.blend
```

Both refuse to overwrite an existing `.blend`; delete the file first if a rebuild is really wanted,
and only when it carries no hand edits.

## Colours

Four saturated cloth colours, **one per sail**. No stripes, no borders, no second note — the variety
is silhouette and size, not panelling.

| Material | Hex | Status |
| --- | --- | --- |
| `Mat_Fabric_Sail_Orange` | `#E2711D` | **Added.** `Mat_Fabric_Wing_Ochre` (`#C98551`) is the sun-cured dusty orange and reads washed out at sail scale; `Mat_Paint_Safety_Orange` is enamel on steel. |
| `Mat_Fabric_Tarp_Azure` | `#3E9AD0` | **Reused.** Already documented as "saturated azure tarpaulin: shade sails and awnings" — exactly this job. |
| `Mat_Fabric_Sail_Red` | `#C62F2A` | **Added, forced.** `Mat_Paint_Lacquer_Vermilion` is deltaE 4.4 away but is wet glossy lacquer at roughness 0.28 on the dragon bazooka: same hue, reads as varnished metal. |
| `Mat_Fabric_Sail_White` | `#F0EFEA` | **Added.** `Mat_Fabric_Flag_Bleached` (`#D8D2C2`) is the deliberately sun-killed white; a pitched sail needs one that still reads white in full sun. |

Ropes use the existing `Mat_Fabric_Rope_Hemp`. Masts and anchors keep the steel they carry in
`sail_rig.blend`.

**Retired with this change:** `Mat_Fabric_Tarp_Powder` and `Mat_Fabric_Tarp_Rose`. Both were added
to the palette for the old pastel tents and nothing else in the library referenced them.

## What is generated and what is reused

**Generated** — the canopies and the ropes. A sail's shape is a function of where you are on the
membrane, so ten silhouettes means ten surfaces. Each is a parametric quad grid solidified to 22 mm,
so no face in the camp is single-sided in engine.

Two surface builders cover all ten:

| Builder | Shape | Used by |
| --- | --- | --- |
| `quad_field(corners, scallop, dip)` | Bilinear patch over four corners. Alternating corner heights *are* the hypar. | QuadSmall, QuadLarge, Ribbon, TwinPeak, Kite |
| `radial_field(corners, crown, scallop, dip)` | N corners fanned out from a crown point. | Tri, TriTall, Penta, HexLow, Cone |

**Reused from `components/structural/sail_rig.blend`** — all the hardware: four masts
(`MastStraight`, `MastStepped`, `MastTripod`, `MastStub`) and three ground anchors (`AnchorPin`,
`AnchorPlate`, `AnchorBlock`).

**No longer used** — the awning kit (`components/structural/facade_awning.blend`) and the settlement
kit's crates, pads, stakes and meter box. The old tents borrowed their poles and camp furniture from
both; these sails borrow nothing from either. See *Gotchas* for why the awning poles could not stay.

## The ten

| Sail | Footprint (with guys) | Height | Cloth | What it is |
| --- | --- | --- | --- | --- |
| `QuadSmall` | 6.11 × 6.11 | 2.94 | orange | A 3.4 m square hypar on four straight masts — the plain one |
| `QuadLarge` | 11.57 × 11.57 | 4.88 | azure | A 7 m square with a deep twist on stepped masts — the camp's biggest |
| `Tri` | 7.32 × 8.08 | 3.39 | red | Three corners, the fewest a membrane can have |
| `TriTall` | 6.92 × 8.36 | 4.52 | white | One 4.4 m peak, two corners pulled near the ground — a wing |
| `Penta` | 9.00 × 9.45 | 3.51 | orange | Five corners at alternating heights, tripods and straight masts |
| `HexLow` | 9.38 × 9.10 | 2.42 | azure | Six corners, wide and low — walk under it without ducking |
| `Ribbon` | 4.44 × 11.77 | 3.03 | red | A 2.3 × 8.4 m strip — a shaded corridor, not a shaded room |
| `TwinPeak` | 7.72 × 5.12 | 3.44 | white | Two peaks pushed up from under the cloth by a pair of tripods |
| `Cone` | 5.05 × 5.74 | 3.47 | orange | An umbrella: one centre mast under a twelve-point hem |
| `Kite` | 9.48 × 6.83 | 4.06 | azure | Four corners at four different heights — the most twisted sheet |

Colours run orange ×3, azure ×3, red ×2, white ×2. Laid out 5 × 2 at 12 m pitch. That is a contact
sheet, not a camp plan.

## Placing them

Each sail has an **`Empty_SNN_<Name>_Root` at its ground centre with every part parented to it**, and
its collection's `instance_offset` set to the same point — so `Add ▸ Collection Instance` drops a
sail with its base on the 3D cursor, exactly like the settlement buildings. Instance rather than
duplicate.

## Rules

| # | Rule |
| --- | --- |
| S1 | One cloth colour per sail. No stripes, no borders, no second note |
| S2 | Four saturated cloth colours only. These are sails in full sun, not sun-killed tarps |
| S3 | Every edge is scalloped. A membrane pulls its free edges concave; straight edges read as a billboard |
| S4 | Every sail is a saddle. Corner heights alternate and the field dishes between them |
| S5 | Masts rake away from the sail they carry, 13°, so the cloth's pull brings them back upright |
| S6 | Every corner is tied off — a mast head with a guy to an anchor, or a rope straight down to one |
| S7 | One `SEED` reproduces the whole camp |

## Knobs

`SEED`, `GRID_PITCH`/`GRID_COLS`, `CLOTH_THICK`, `GUY_RADIUS`, `RAKE`, `GUY_RUN`, and the four colour
constants at the top. Each sail is one function — edit its corner list to resize and reshape it, and
its `scallop`/`dip`/`crown` arguments to change how hard the cloth reads as tensioned.

## Gotchas found building this

- **The awning kit's poles cannot be raked.** `Mesh_AwningStrip_Pole` and
  `Mesh_AwningPorch_PoleLeft` each carry an outrigger brace modelled for a vertical stance — the
  strip pole's brace alone is 1.68 m across. Tipping one lays its brace through the sail it is
  holding up. That is why `sail_rig.blend` exists at all.
- **A raked mast tips its own foot with it.** Rotating about the foot centre lifts one side of the
  base by `foot_radius × sin(rake)`, so the mast hangs in the air on that side. Two things fix it
  together: bed the mast into the sand by exactly that much (the `foot radius` column in `MASTS`),
  and make every foot a **round shoe, never a square plate** — a tilted disc reads as a shoe bedded
  in sand, a tilted square reads as a dropped object lying on it.
- **A tripod cannot rake at all.** Its feet reach 0.68 m from the shaft, so a 13° rake lifts one leg
  0.15 m. `MASTS` carries a `rakeable` flag and `Camp.mast()` forces `rake = 0` for the tripod.
- **Scalloping a radial sail must pull in XY only.** The first version pulled the whole edge point
  toward the crown, which dragged the edge's *height* up with it; on `TriTall`, whose corners sit
  well below the crown, that folded the free edge into a crease and the sail rendered as a paper
  dart. `radial_field` now moves `x` and `y` toward the crown and leaves `z` on the chord, with its
  own catenary droop.
- **`quad_field` scallops by remapping the parameter domain**, not by displacing the surface. That
  is what keeps the four corners exactly on their mast heads while the edges between them bow in —
  displacing the boundary would drag the corners off the eyes they are shackled to.
- **`bl.start()` refuses to overwrite an existing `.blend`.** Rebuilding either file means deleting
  it first. That guard is there because the `.blend` is the source of truth; only delete one you
  know carries no hand edits.

## Known gaps

- **No LOD, no collision, no UVs.** Silhouette blockouts, like the settlement buildings.
- **Cloth is geometry, not simulated.** The tension is baked into the surface function, so a sail
  looks the same from every angle and in every wind.
- **Nothing is exported to Unity yet.** There is no `.fbx` under `Assets/Game/Art/Models/` for these;
  they exist only as library source.
