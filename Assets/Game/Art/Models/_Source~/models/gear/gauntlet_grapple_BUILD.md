# Gauntlet Grapple — build record

`models/gear/gauntlet_grapple.blend`, built by `gauntlet_grapple.py`, exported
by `gauntlet_grapple_export.py` to
`Assets/Game/Art/Models/Items/gauntlet_grapple.fbx`. One collection,
`Coll_GauntletGrapple`. The forearm-mounted harpoon launcher with a winch, on
the gauntlet base's Mount variation, at true suit scale (worn at scale 1,
origin at the wrist bone).

**Second cut, 2026-09-03: the device is twice the first build's linear size**
(the first read as jewellery on a 3 m astronaut) and it appends the rebuilt
base whose undersleeve rim moved to y 0.022. Every number was re-derived, not
multiplied: embeds are still 2-4 mm and `BEVEL_W` is still 3 mm. What did NOT
double is under "What the envelope would not let double".

## The bracer is not in this model (2026-09-04)

Every gauntlet built before this date appended
`components/props/gauntlet_base.blend` and shipped a copy of the bracer inside
its own FBX. It does not any more: the player wears the Mount variation on both
forearms permanently (`gauntlet_base_export.py` ships it, Unity's
`ForearmBracers` seats it), and a gauntlet is only the device that stands on its
hardpoint deck. `strip_bracer.py` took the ten `Mesh_GauntletBase_*` objects out
of this file; the device was not touched, and the generator lost its
`append_base()` call in the same change, so a regeneration lands here too —
proven by a control diff of every object's vertex count and bounds.

**Everything below still measures the device against the bracer's deck and
shells, and all of it still holds** — the bracer is in exactly the same place
relative to the arm, it is simply worn rather than carried. Only the counts and
bounds that summed the two are restated.

## Reuse

| From | Object | How |
|---|---|---|
| `components/props/cable_drum.blend` | `Mesh_CableDrum_Caged` | 2.0x, `R_z(180)`, axle (0, 0.240, 0.530) |
| `components/props/gas_bottle.blend` | `Mesh_GasBottle_Flask` | 1.8x, rolled −90°, base (−0.121, 0.245, 0.345) |
| `components/props/grapple_dart.blend` | `Mesh_GrappleHarpoon` | 0.60x, eye at (0, 0.302, 0.333), origin moved to the tail |

Component names are kept, as `grapple_bracer.py` did, so provenance reads off
the outliner. **Caged drum, not Winch**, and **Flask, not Single**: the two
lightest variations, because the appended copies dominate the triangle budget
(below), and both suit the bigger device — the cage is a shape that survives
being looked at from above, and a flat flask lies against a fat barrel where
an upright bottle stands off it.

## Decomposition (authored parts, `Mesh_Grapple_<Part>`)

| Object | What | Sits |
|---|---|---|
| `Tube` | launch tube, outer r 0.086, wall 16 mm | axis (0, y, 0.333), y −0.020..0.250; bottom z 0.247, crown 0.419 |
| `TubeRail` | crown rib, 4 mm proud, 12 mm buried | y 0.050..0.245 |
| `MuzzleCollar` | orange collar, outer r 0.100, inner 0.083 (3 mm into the tube wall) | y −0.018..0.054 |
| `MuzzleBushing` | bore bushing r 0.024..0.073, front face 1 mm inside the mouth | y −0.019..0.017 |
| `CradleFront`, `CradleRear` | bars across the deck, tops at the tube's axis | y 0.150, 0.190; x ±0.062 |
| `Breech` | receiver: sunk foot x ±0.066 (y to 0.318) plus a body x ±0.090 from z 0.256; gland, amber lamp, red arming stripe on top | y 0.220..0.345, z 0.247..0.444 |
| `PylonLeft`, `PylonRight` | drum bearing struts with brass axle collars and a bolt through the foot | x ±0.082..0.096, y 0.202..0.278, z 0.410..0.568 |
| `Cable` | chrome run, r 6 mm, from the drum's lead-off (0, 0.352, 0.578) into the gland | |
| `BottleClamps` | two four-bar bands round the flask, inboard bar bolted into the tube | y 0.130, 0.205 |
| `muzzle` (empty) | rope pay-out point, identity rotation | (0, −0.020, 0.333) |

Every part is its own object; nothing joined. Embeds are 2-4 mm at every
authored joint (collar and rail into the tube wall, bushing into the bore,
cradles and breech foot into the deck, pylon feet into the breech body, band
bars into flask and tube). Deliberate clear gaps: drum cage to tube crown
16 mm, drum cage to breech top 15 mm, flask to breech 6 mm, rear cradle to
breech 4 mm, cable to cage 9 mm at the closest station.

## Envelope

Checked vertex by vertex against the relaxed envelope
(`scratchpad/gb/check_envelope.py`): **0 violations**.

| Rule | Worst point |
|---|---|
| forward y ≥ −0.240 | harpoon blade tip at −0.2382 |
| forward window z ≥ 0.20, \|x\| ≤ 0.20 for y < 0.03 | muzzle collar bottom z 0.2330, \|x\| ≤ 0.100 |
| elbow y ≤ 0.360 | cable tail at 0.3580, drum cage 0.3551 |
| height z ≤ 0.640 | drum cage top 0.6145 |
| width \|x\| ≤ 0.210 | drum axle end blocks ±0.0981 |
| nothing below the deck plane outside its footprint | breech foot ends at y 0.318, 2 mm inside the deck's rear edge |

The base's own top between the wrist rim and the deck is z 0.2165 (measured,
not assumed), so the muzzle collar's 0.2330 clears the base's orange collar by
16 mm.

## Materials (palette only, no additions)

`Mat_Metal_Steel_Worn` (index 0: tube, clamp bands), `Mat_Metal_Steel_Dark`
(breech, cradles, pylons, bushing, rail), `Mat_Metal_Chrome_Scuffed` (cable,
gland, bolt heads), `Mat_Paint_Safety_Orange` (muzzle collar — the one
accent), `Mat_Paint_Warn_Red` (arming stripe), `Mat_Emissive_Amber` (ready
lamp), `Mat_Metal_Brass_Tarnished` (bearing collars). Appended components
bring their own palette links.

## Triangles

| | tris |
|---|---|
| authored device parts | 2,188 |
| `Mesh_CableDrum_Caged` | 1,274 (from 4,252, ratio 0.30) |
| `Mesh_GasBottle_Flask` | 1,220 (from 2,908, ratio 0.42) |
| `Mesh_GrappleHarpoon` | 1,174 (from 2,348, ratio 0.50) |
| **device total** | **5,856** (cap 6,000) |
| file total | 9,780 |

The appended components are 9,508 triangles at full density, so each appended
**copy** carries a Collapse Decimate applied inside this file (`lighten()`).
The component `.blend`s are never touched — they stay the library's source of
truth and other models use them at full density. The harpoon gets the lightest
cut because its blade and barbs are the silhouette; the drum and flask read as
masses and take the heavy ones.

## Measured

FBX, meshes only (`describe`):

| | min | max | size |
|---|---|---|---|
| Blender (x, y, z) | (−0.209, −0.238, −0.192) | (0.190, 0.360, 0.615) | (0.399, 0.598, 0.806) |
| Unity (x, y, z) | (−0.190, −0.192, −0.360) | (0.209, 0.615, 0.238) | (0.399, 0.806, 0.598) |

Re-measured 2026-09-04, after the bracer left the model: Blender min
(−0.1620, −0.2382, 0.2330) max (0.1023, 0.3580, 0.6145), size
(0.2643, 0.5962, 0.3815). Longest axis **0.5962** (Blender Y, along the arm),
`holdSize` at 1x wear = 0.5962.

It was 0.8061 on the dorsal axis while the bracer was in the file — that
extreme was the bracer's ventral shell at z −0.19, which is worn now and no
longer part of this item. The device itself is unchanged.

Pivots (both frames):

| Object | Blender | Unity |
|---|---|---|
| `muzzle` (empty) | (0, −0.020, 0.333) | (0, 0.333, 0.020) |
| `Mesh_GrappleHarpoon` (tail) | (0, 0.3279, 0.333) | (0, 0.333, −0.3279) |
| `Mesh_CableDrum_Caged` (axle) | (0, 0.240, 0.530) | (0, 0.530, −0.240) |
| `Mesh_GasBottle_Flask` (base) | (−0.1212, 0.245, 0.345) | (0.1212, 0.345, −0.245) |
| everything else | (0, 0, 0) | (0, 0, 0) |

Harpoon extents: y −0.2382..0.3279, x ±0.092, z 0.277..0.437.

## What the envelope would not let double

**The harpoon went 0.45x → 0.60x, not 0.90x, and the tube got fatter, not
longer.** The head alone is 0.336 m at 0.60x and every millimetre of it has to
be forward of the muzzle, while the tail has to stay inside the receiver at
y ≤ 0.36 and the tip inside y ≥ −0.24. That fixes the arithmetic:
`K ≤ 0.595/0.9435 = 0.63`, and the muzzle has to sit at `y ≥ 0.340K − 0.240`,
which at K = 0.60 is y ≥ −0.036. So a longer tube would mean a *smaller*
harpoon. The tube is therefore the same length as the first build (0.270 m)
at twice the diameter, and the forward growth is all head — the tip moved from
y −0.197 to −0.238.

## Decisions the lead may want reversed

- **Drum moved off the deck onto struts above the receiver** (axle z 0.530,
  top 0.615). Behind the receiver at deck level, a 2x drum caps the harpoon at
  0.45x — it eats exactly the length the receiver needs. Up is the only
  direction with room, which is what the raised height limit bought.
- **Drum turned end for end** (`R_z(180)`). Its own lead-off tail points
  forward; the first routing tried to bring the cable down in front of it and
  the run went straight through the barrel. Turned round, the cable drops
  down the elbow face into the gland.
- **Pylon struts, not plates.** The first cut used slabs and they hid the
  wound cable from every side view — the same failure `grapple_bracer.py`
  records for its axle cheeks.
- **Flask at 1.8x, not 2.2x**: at 2.2 it covered the barrel's whole −X flank
  and its decimated silhouette read as a lump.
- **The reinforcing ring became a crown rail.** Any full ring round an 0.086 m
  tube whose axis is 83 mm over the deck reaches down to z 0.232, which is
  inside the deck over the hardpoint and inside the base's collar and dorsal
  shell forward of it. A rib on the crown says the same thing and touches
  nothing.
- **Harpoon origin at its tail** (the rearmost vertex, inside the receiver),
  as the brief asked — not at the rope eye where the component and the bracer
  put it. `hookHeadScale` in the artifact must track `HARPOON_K = 0.60` or the
  flying head is a different size from the seated one.
- **Appended copies are decimated.** If the lead would rather keep full
  density, the budget needs a different lever — the ratios are three constants
  at the top of the generator.
- No armature: nothing articulates; the drum does not turn in the model.

## Verification

- Rebuilt from the script into a fresh file (the previous `.blend` deleted
  first, since `start()` refuses to clobber); every object at rotation 0,
  scale 1, transforms applied; no auto-suffixed names.
- Bounds, pivots and the envelope read back from the file, not from intention;
  base clearance measured off the appended base, not assumed.
- Rendered headless from six angles into `scratchpad/gb/grapple_*.png` and
  `grapple_dev_*.png` after the cameras were pulled back — the first pass'
  framing no longer contained the device, which is itself the point of the
  resize. The drum's coil, the gauge, the harpoon head clear of the collar and
  the cable's run from drum to gland all read.
- Export ran; the FBX carries 24 meshes and the `muzzle` empty.
