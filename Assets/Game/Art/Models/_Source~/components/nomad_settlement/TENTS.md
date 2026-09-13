# Nomad tents — tensile shade sails

Eighteen pitched shade sails, every one a tensioned membrane on thin raked timber poles. No camp
furniture, no props, no metal — cloth, wood and hemp. Sized from a 2.6 m spur to an 8.4 m ribbon,
laid out on a grid the same way `models/buildings/nomad_settlement.blend` lays out its buildings.

**Eight are freestanding. Ten are wall-mounted** — they hang their wall-side corners off a building
and drop the masts those corners would have needed, down to `WallLean`, which has no mast at all.

They replace the earlier ten pastel tarp shelters (ridge tent, lean-to, bell, hoop, market canopy,
wind break, stores drape, pavilion). Of that set only the shade sail was worth keeping; this is that
one turned into a family.

| File | What it is |
| --- | --- |
| `tents.py` | The generator. Seeded, re-runnable, all eighteen recipes in one place. |
| `tents_export.py` | Ships each sail to Unity as its own FBX under `Assets/Game/Art/Models/Environment/Structures/NomadSettlement/Tents/`. |
| `tents.blend` | Its output: 18 sails, 206 objects, 78 meshes. |
| `../structural/sail_rig.blend` | The timber masts, ground anchors and wall fixings they are all rigged from. |
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
is silhouette and size, not panelling. Orange and azure carry five sails each, red and white four.

| Material | Hex | Status |
| --- | --- | --- |
| `Mat_Fabric_Sail_Orange` | `#E2711D` | **Added.** `Mat_Fabric_Wing_Ochre` (`#C98551`) is the sun-cured dusty orange and reads washed out at sail scale; `Mat_Paint_Safety_Orange` is enamel on steel. |
| `Mat_Fabric_Tarp_Azure` | `#3E9AD0` | **Reused.** Already documented as "saturated azure tarpaulin: shade sails and awnings" — exactly this job. |
| `Mat_Fabric_Sail_Red` | `#C62F2A` | **Added, forced.** `Mat_Paint_Lacquer_Vermilion` is deltaE 4.4 away but is wet glossy lacquer at roughness 0.28 on the dragon bazooka: same hue, reads as varnished metal. |
| `Mat_Fabric_Sail_White` | `#F0EFEA` | **Added.** `Mat_Fabric_Flag_Bleached` (`#D8D2C2`) is the deliberately sun-killed white; a pitched sail needs one that still reads white in full sun. |

Poles are `Mat_Wood_Timber_Silvered`, the deadman board is `Mat_Wood_Ply_Worn`, and every rope,
lashing and whipping is `Mat_Fabric_Rope_Hemp`. **Seven materials in the whole camp, none of them
metal.**

**Retired with this change:** `Mat_Fabric_Tarp_Powder` and `Mat_Fabric_Tarp_Rose`. Both were added
to the palette for the old pastel tents and nothing else in the library referenced them.

## What is generated and what is reused

**Generated** — the canopies and the ropes. A sail's shape is a function of where you are on the
membrane, so eighteen silhouettes means eighteen surfaces. Each is a parametric quad grid, which
`sheet()` solidifies to 22 mm — but **the shipped `tents.blend` predates that line and its canopies are
still single-sided**, so they are rendered double-sided in Unity instead. See *In Unity* below.

Two surface builders cover all eighteen:

| Builder | Shape | Used by |
| --- | --- | --- |
| `quad_field(corners, scallop, dip)` | Bilinear patch over four corners. Alternating corner heights *are* the hypar. | QuadSmall, QuadLarge, Ribbon, Kite, WallQuad, WallStrip, WallLean, WallPorch, WallBillow |
| `radial_field(corners, crown, scallop, dip)` | N corners fanned out from a crown point. | Tri, TriTall, Penta, HexLow, WallTri, WallCorner, WallFan, WallCanopyLong, WallSpur |

**Reused from `components/structural/sail_rig.blend`** — all the hardware: four masts (`MastPole`,
`MastLashed`, `MastTripod`, `MastStub`), three ground anchors (`AnchorStake`, `AnchorCleat`,
`AnchorLog`) and three wall fixings (`WallHook`, `WallCleat`, `WallBracket`). See
[SAIL_RIG.md](../structural/SAIL_RIG.md).

**No longer used** — the awning kit (`components/structural/facade_awning.blend`) and the settlement
kit's crates, pads, stakes and meter box. The old tents borrowed their poles and camp furniture from
both; these sails borrow nothing from either. See *Gotchas* for why the awning poles could not stay.

## The twelve

### Freestanding

| Sail | Footprint (with guys) | Height | Cloth | What it is |
| --- | --- | --- | --- | --- |
| `QuadSmall` | 6.01 × 6.01 | 2.95 | orange | A 3.4 m square hypar on four plain poles — the plain one |
| `QuadLarge` | 11.59 × 11.59 | 4.90 | azure | A 7 m square with a deep twist on lashed masts — the camp's biggest |
| `Tri` | 7.23 × 7.99 | 3.40 | red | Three corners, the fewest a membrane can have |
| `TriTall` | 6.96 × 8.29 | 4.53 | white | One 4.4 m peak, two corners pulled near the ground — a wing |
| `Penta` | 8.91 × 9.36 | 3.67 | orange | Five corners at alternating heights, shear legs and plain poles |
| `HexLow` | 9.29 × 9.02 | 2.42 | azure | Six corners, wide and low — walk under it without ducking |
| `Ribbon` | 4.47 × 11.72 | 3.04 | red | A 2.3 × 8.4 m strip — a shaded corridor, not a shaded room |
| `Kite` | 9.50 × 6.73 | 4.07 | white | Four corners at four different heights — the most twisted sheet |

### Wall-mounted

The building carries the wall-side corners, so each of these stands on about half the masts its
freestanding equivalent needs. Footprints are measured from the wall face outward.

| Sail | Footprint (with guys) | Height | Cloth | Masts | Fixings | What it is |
| --- | --- | --- | --- | --- | --- | --- |
| `WallQuad` | 5.49 × 5.20 | 3.40 | orange | **2** (was 4) | 2 cleat | The wall counterpart of QuadSmall: a hypar on two cleats and two poles |
| `WallTri` | 4.03 × 5.77 | 3.49 | azure | **1** (was 3) | 2 hook | Three corners, two on hooks in the wall |
| `WallStrip` | 6.32 × 3.21 | 3.05 | red | **2** (was 4) | 2 bracket | A shaded walkway down a facade, on brackets that clear whatever the wall carries |
| `WallLean` | 4.08 × 4.10 | 3.80 | white | **0** | 2 cleat | Wall holds the high edge, the low edge is roped straight to the sand |
| `WallPorch` | 3.90 × 6.06 | 3.55 | red | **2** (was 4) | 2 cleat | Narrow and deep — a porch over a doorway rather than a shaded yard |
| `WallCorner` | 5.52 × 5.98 | 3.40 | orange | **1** (was 5) | 4 cleat | Pinned to **two** walls where a building turns a corner |
| `WallFan` | 5.48 × 5.26 | 3.34 | azure | **1** (was 4) | 3 hook | Three fixings spread along one wall gathering to a single pole |
| `WallCanopyLong` | 8.56 × 4.12 | 3.10 | white | **3** (was 6) | 3 bracket | 7.2 m of facade under cover — the long one |
| `WallBillow` | 7.59 × 5.27 | 4.17 | orange | **2** (was 4) | 2 hook | The inverse of WallLean: the wall holds the LOW edge and the sail lifts away |
| `WallSpur` | 2.68 × 3.03 | 2.59 | azure | **1** (was 3) | 2 hook | The smallest thing in the camp — a scrap of shade on one stub |

`WallCorner` is the only one that needs **two** walls: one running along X at y = 0, one running
along Y at x = +3.10. Its fixings on the second wall are turned −90°.

Laid out 5 × 4 at 12 m pitch. That is a contact sheet, not a camp plan.

**Cut on review:** `TwinPeak` (a marquee tented up from below by two tripods) and `Cone` (a parasol
on one centre mast). Both worked, neither was wanted. Their recipes are gone from `tents.py`, not
commented out — `git log` is where a deleted recipe lives.

## In Unity

`tents_export.py` writes one FBX per sail; `NomadSettlementBuilder` (menu
*Tools ▸ Environment ▸ Build Nomad Settlement Prefabs*) turns each into a prefab under
`Assets/Game/Prefabs/Environment/Structures/NomadSettlement/Tents/`, and hangs the wall-mounted ones
off the forty building prefabs. What it relies on, and what it had to work around:

- **A `Wall*` sail's wall plane is its local z = 0 and it reaches out along +z**, which is this
  file's y = 0 and −y through the exporter's axis conversion. Seating one is a yaw and a distance.
- **The `*WallFix*` parts are the contract.** The builder measures them for how wide a wall the sail
  needs and how high up it hangs — per sail, because the ten differ by more than a metre in both,
  and per fixing, because the two fixings of one sail sit up to 0.95 m apart in height.
- **`WallCorner` is left out of the automatic hanging**, being the only one that needs two walls.
- **Each sail is scaled to the building it hangs on**, until its fixings sit three storeys up and
  never below twice its authored size — 2× to 4.5× over the settlement, cloth 4–18 m across. The
  buildings are themselves scaled to the astronaut, and a sail left at the size it was authored reads
  as a parasol nailed to the wall. What caps it is the wall: a fixing span cannot be wider than the
  drum its straight edge crosses, so the long sails are held by width and the narrow ones by height.
- **A sail is seated against the colliders, one ray per fixing**, and tilted to the slope those rays
  describe so its wall edge follows the drum's 6.4° batter. A vertical edge on a coned wall can only
  touch at one height.
- **The canopies ship single-sided.** `sheet()` solidifies 22 mm, but the shipped `tents.blend`
  predates that line: every canopy in it is one quad grid, 196 faces for a 14 × 14 sail. Back-face
  culled, a sail is invisible from underneath — the side a player stands on — so the builder renders
  the cloth double-sided (`Art/Materials/Settlement/*(DoubleSided).mat`). Regenerating the `.blend`
  would fix it at the source; `_buildlib.start` refuses to overwrite it, deliberately.
- **No colliders on a sail.** It is cloth over head height on 60 mm hemp guys.

## Placing them

Each sail has an **`Empty_SNN_<Name>_Root` with every part parented to it**, and its collection's
`instance_offset` set to the same point — so `Add ▸ Collection Instance` drops a sail on the 3D
cursor, exactly like the settlement buildings. Instance rather than duplicate.

The root sits at a **different place for the two families**, and this is the thing to get right:

| Family | Root is at | Drop it on |
| --- | --- | --- |
| Freestanding | the ground centre of the pitch | open sand |
| Wall-mounted | the **wall face** at ground level, centred on the sail's width | the foot of a facade |

A wall-mounted sail occupies y ≤ 0 in its own space and hangs toward **−Y**, so rotate the instance
about Z until −Y points away from the building. Nothing but a fixing's pegs crosses y = 0, which is
checked on every build. `WallCorner` additionally needs a return wall along Y at x = +3.10.

The facade has to be tall enough to take the fixings: the highest wall mount in the set is
`WallLean`'s at 3.75 m. The settlement's own buildings run 3.3–14.4 m tall on 3–6 m plans, so every
one of these fits on all but the very smallest.

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
| S8 | Wood and rope only. No metal anywhere in the camp, and poles stay thin — see `GIRTH_POW` |
| S9 | A wall sail's wall edge is on y = 0, it hangs toward −Y, and its root is on the wall face |

## Knobs

`SEED`, `GRID_PITCH`/`GRID_COLS`, `CLOTH_THICK`, `GUY_RADIUS`, `RAKE`, `GUY_RUN`, `GIRTH_POW`, and the
four colour constants at the top. Each sail is one function — edit its corner list to resize and
reshape it, and its `scallop`/`dip`/`crown` arguments to change how hard the cloth reads as tensioned.

## Gotchas found building this

- **Scaling a mast to length also scales its girth, and that is wrong.** A mast is stretched from a
  3.60 m master, so the 4.9 m masts on `QuadLarge` came out 36% fatter than the 2.4 m ones on
  `QuadSmall` — the big sails stood on piles and the small ones on twigs. Timber comes in one size
  in a camp. `Camp.mast()` now scales `(girth, girth, stretch)` with
  `girth = stretch ** GIRTH_POW` (0.35), so girth follows length only weakly. The non-uniform scale
  is safe here **only because `place_delta` applies scale before rotation** — `R @ S` shears nothing,
  `S @ R` would.
- **The awning kit's poles cannot be raked.** `Mesh_AwningStrip_Pole` and
  `Mesh_AwningPorch_PoleLeft` each carry an outrigger brace modelled for a vertical stance — the
  strip pole's brace alone is 1.68 m across. Tipping one lays its brace through the sail it is
  holding up. That is why `sail_rig.blend` exists at all.
- **A raked mast tips its own foot with it.** Rotating about the foot centre lifts one side of the
  butt by `foot_radius × sin(rake)`, so the pole hangs in the air on that side. The mast is bedded
  into the sand by exactly that much — the `foot radius` column in `MASTS`. The first version of the
  rig had square steel base plates and needed round shoes for the same reason; a bare timber butt is
  already round, which is one more reason the wooden rig is the simpler one.
- **Shear legs cannot rake at all.** A tripod's feet reach 0.62 m from the axis, so a 13° rake lifts
  one leg 0.14 m. `MASTS` carries a `rakeable` flag and `Camp.mast()` forces `rake = 0` for it.
- **Scalloping a radial sail must pull in XY only.** The first version pulled the whole edge point
  toward the crown, which dragged the edge's *height* up with it; on `TriTall`, whose corners sit
  well below the crown, that folded the free edge into a crease and the sail rendered as a paper
  dart. `radial_field` now moves `x` and `y` toward the crown and leaves `z` on the chord, with its
  own catenary droop.
- **`quad_field` scallops by remapping the parameter domain**, not by displacing the surface. That
  is what keeps the four corners exactly on their mast heads while the edges between them bow in —
  displacing the boundary would drag the corners off the whippings they are tied to.
- **Author the wall MOUNT, never the wall corner.** A wall sail is built the opposite way round
  from a pitched one: you choose where the fixing is nailed and the corner follows, because the
  corner is wherever that fixing's rope eye ends up. Working the other way — authoring the corner
  and deriving the fixing — put the `WallBracket`'s whole 0.46 m knee brace *behind* the facade with
  only its ring showing. `hang()` does both halves from one list of mounts, and `fix_corner()` is the
  only thing that should ever compute a wall corner.
- **A fixing's eye offset rotates with the fixing.** On a wall that runs along Y the fixing is turned
  −90°, and its eye goes with it. Adding the raw offset to the object's position instead of pushing
  it through the object's matrix puts the eye 0.153 m off — which is exactly the false failure the
  verification pass reported on `WallCorner` before it was fixed to use the full matrix.
- **`bl.start()` refuses to overwrite an existing `.blend`.** Rebuilding either file means deleting
  it first. That guard is there because the `.blend` is the source of truth; only delete one you
  know carries no hand edits.

## Known gaps

- **No LOD, no collision, no UVs.** Silhouette blockouts, like the settlement buildings.
- **Cloth is geometry, not simulated.** The tension is baked into the surface function, so a sail
  looks the same from every angle and in every wind.
- **Nothing is exported to Unity yet.** There is no `.fbx` under `Assets/Game/Art/Models/` for these;
  they exist only as library source.
- `_zverify` reports 2 near-coplanar pairs (0.032 m², none truly coincident) inside the two
  `MastLashed` butts, where the splice meets the ground. Both are below the mast's bedding depth.
- **A wall sail ships no wall.** The building it pins to is whatever the level puts it against, so
  nothing here checks that a real facade is present, tall enough or wide enough — only that the sail
  keeps to its own side of the plane. `WallCorner` silently looks wrong if the return wall is absent.
