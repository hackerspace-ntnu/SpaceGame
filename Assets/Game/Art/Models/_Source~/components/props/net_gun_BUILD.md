# Net gun — build record

Chunky sci-fi capture pistol for the Net Gun artifact, modelled from the
concept illustration supplied with the request: a squat gun whose whole front
half is a two-tone drum lying on its side — charcoal nose, bright orange body —
bore left open behind four splayed petals with a bundled net crammed in, a boxy
striped receiver, a tall optic, a raked wrapped grip, an L-bracket and two hoses
under the drum, and a small roller wheel.

## Decomposition

One component file, `net_gun.blend`, holding two variations as collections —
variations of one thing belong in one file:

| Collection | Objects | Notes |
| --- | --- | --- |
| `Coll_NetGun_Loaded` | `Mesh_NetGun_Body`, `Mesh_NetGun_Bundle`, `Marker_Muzzle`, `Marker_Grip` | The hero. **Ships to Unity.** |
| `Coll_NetGun_Spent` | `Mesh_NetGun_Spent` | Same gun, empty bore. Not exported. |

Both come out of one `net_gun()` call taking a `loaded` flag, so the two cannot
drift apart structurally: `loaded` decides only whether the bundle is built.
The bundle is a **separate object** rather than part of the body mesh, which is
what lets the Unity prefab get both states out of the single exported FBX by
switching one renderer off — the spent collection exists as the reference the
loaded gun is checked against, not as a second thing to ship.

Parts, front **−Y**, up **+Z**, matching `gravel_blaster`:

| Part | What it is | Materials |
| --- | --- | --- |
| Canister | The drum. Hollow charcoal nose over the front 40%, solid orange body behind it, three steel bands, four longitudinal ribs, breech collar onto the receiver | `Steel_Dark` nose, `Paint_Safety_Orange` body, `Steel_Worn` bands, `HullRust_Orange` ribs |
| Bore | Black lining tube down the inside of the nose and a floor disc 124 mm in | `Neutral_Black_Matte` |
| Petals | Four curved shells hinged at the rim, splayed back over the drum on the diagonals, each on a tangential hinge boss | `Steel_Worn` |
| Net bundle | Lumpy lofted mass recessed 30 mm into the bore, three folded cord loops on its face | `Fabric_Rope_Hemp` |
| Receiver | Boxy body, raised top plate, rear cap, two panel-line strips a side, three diagonal stripes a side | `Neutral_Panel_Grey`, stripes `Paint_Safety_Orange` (**the same orange as the drum, deliberately**), lines `Steel_Dark`, cap `Steel_Worn` |
| Optic | Riser pedestal, horizontal tube, two collars, front lens, rear ocular, knurled turret cap | `Steel_Dark` body, `Emissive_Green_CRT` lens, `Steel_Worn` collars and turret |
| Grip | Raked core lofted from `GRIP_SECTIONS`, butt plate, five cloth wraps over four cord wraps | `Steel_Dark` core, `Fabric_Canvas_Faded` cloth, `Fabric_Rope_Hemp` cord, `Steel_Worn` butt |
| Trigger group | Three-limb guard, blade, rubber pad, detail block with rivets under the receiver | `Steel_Worn`, pad `Neutral_Black_Matte`, block `Steel_Dark` |
| Bracket and hoses | L-bracket under the drum's belly, two swept hoses looping back to the receiver's underside, two clamp collars | `Fabric_Tarp_Azure` bracket and blue hose, `Paint_Coral_Faded` red hose |
| Roller | Two fork plates, tyre and hub under the drum's rear | `Steel_Worn` fork, `Neutral_Black_Matte` tyre, `Metal_Chrome_Scuffed` hub |

No sub-components were split into their own files. Every part here is shaped by
this gun's proportions and has no plausible reuse: the library's generic drums,
brackets and wheels are vehicle scale, and the nearest handheld things —
`portal_gun`, `gravel_blaster`, `lasso_coil` — share no part with a capture
canister. **Nothing was reused as geometry.** What was reused is convention:
the marker-cube trick and the parallel-transport `sweep` come from
`portal_gun.py`, and `strut`, `octagon`, the material-index-0 rule and the
narrow selective bevel come from `gravel_blaster.py`.

## Assembly / geometry facts the Unity side relies on

- Front is **−Y**, up **+Z**. Origin at `(0, 0, 0)`, on the canister axis at its
  rear face — level with the breech and roughly where the gun balances.
- Overall, loaded, markers excluded: **0.274 × 0.629 × 0.442 m**
  (X `[−0.137, 0.137]`, Y `[−0.312, 0.317]`, Z `[−0.257, 0.185]`).
  0.629 m from bore rim to grip butt.
- The canister body is **0.260 m across** and 0.310 m long — half the gun's
  length and the shape that has to keep reading first. The gun's widest point
  is 0.274 m, at the petal tips and the drum's ribs, which stand ~7 mm proud.
- **The longest axis is Y**, and comfortably so (0.629 against 0.442 and 0.274).
  That matters: `EquipItemSocket.Seat` rescales a held item so its longest axis
  equals `ItemGrip.holdSize`, so the bracket lands on the gun's length rather
  than on its height.
- `Marker_Muzzle` at Blender `(0, −0.3100, 0.0000)` — the centre of the bore
  rim, on the canister axis.
- `Marker_Grip` at Blender `(0, 0.2018, −0.1070)` — **on the grip's core axis,
  62 mm down from its top, inside the cloth wrap.** Deliberately not on the mesh
  surface: `EquipItemSocket` seats an item by putting its grip point in the
  palm, so a marker on the skin of the grip holds the gun a centimetre clear of
  the hand.
- By `portal_gun`'s verified import mapping (Blender `(x, y, z)` arrives as
  prefab-local `(x, z, −y)`), those land at Unity `(0, 0, 0.310)` and
  `(0, −0.107, −0.2018)`, with the prefab root's **+Z the way the gun points
  and +Y its up**. Confirm against the real import in Task 12 rather than
  trusting this line — it is derived, not measured in the Editor.
- Only the loaded collection is exported (`net_gun_export.py`), to
  `Assets/Game/Art/Models/Items/net_gun.fbx`.

## The scale, and what the ladder actually says

The plan asked for roughly 0.62 m muzzle to butt and 0.26 m across; the file is
**0.629 m** and **0.260 m**. Checking that against the ladder first, because the
two numbers answer different questions and it would be easy to read them as one:

`Assets/Game/Editor/Items/ItemScaleLadder.cs` is a table of
**`ItemGrip.holdSize`** values, and `holdSize` is *the metres of the item's
longest axis after Unity rescales the mesh* — see
`ItemFootprintTests.HoldSizeScalesTheMeshToTrueMetresKeepingProportions` and
`EquipItemSocket.Seat`, which scales before it seats. So the size authored here
does **not** set how big the gun is in the hand; the bracket does, and the
authored size only has to be proportionally right. That is why the source models
already disagree wildly — `gravel_blaster` is 1.22 m in Blender and
`portal_gun` 0.43 m, and both ship at `holdSize` 1.25.

The bracket a net gun belongs in is therefore **`Gun`, 1.25 m** — the anchor
itself, shared by `Gun.prefab`, `PortalGun` and `GravelBlaster`. Nothing new was
invented and nothing was rounded to a nearby number. **Task 12 must set
`ItemGrip.holdSize` to 1.25** and leave `packSize` at 0; guns stay at the anchor
on the mat on purpose, because big gear goes on the rack with overhang.

0.62 m in Blender is then a modelling convention, not a game size, and it is the
right one: it is a plausible real-world capture pistol, so the proportions the
1.25 m scale-up inherits are a gun's rather than a toy's.

## Materials

Twelve, **all from the existing palette; nothing added and nothing defined
locally** — every material in the file is linked from `palette.blend`.

`Mat_Metal_Steel_Worn` is deliberately material index 0, because
`bmesh.ops.bevel` stamps every face it creates with index 0, so index 0 is the
colour of every chamfered edge in the file.

| Index | Material | Where |
| --- | --- | --- |
| 0 | `Mat_Metal_Steel_Worn` | Petals, bands, trigger group, turret, butt, fork — and every bevel |
| 1 | `Mat_Metal_Steel_Dark` | Canister's charcoal nose, optic body and riser, grip core, detail block |
| 2 | `Mat_Paint_Safety_Orange` | Canister's body **and** the receiver's diagonal stripes |
| 3 | `Mat_Neutral_Panel_Grey` | Receiver |
| 4 | `Mat_Metal_HullRust_Orange` | The four longitudinal ribs on the canister body — weathering over the paint |
| 5 | `Mat_Emissive_Green_CRT` | Optic lens |
| 6 | `Mat_Fabric_Canvas_Faded` | Cloth wraps at the top of the grip |
| 7 | `Mat_Fabric_Rope_Hemp` | Net bundle, cord wraps at the grip's base |
| 8 | `Mat_Fabric_Tarp_Azure` | L-bracket and the blue hose |
| 9 | `Mat_Paint_Coral_Faded` | The red hose |
| 10 | `Mat_Metal_Chrome_Scuffed` | Roller hub |
| 11 | `Mat_Neutral_Black_Matte` | Bore lining and floor, roller tyre, trigger pad |

## Judgement calls

- **No armature, and the mouth is modelled open rather than animated.** Loaded
  and spent are a *silhouette* difference: the bore is a real hole with a real
  bundle in it, and the prefab switches the bundle off. That reads at any
  distance, survives being a 256 px inventory icon, costs nothing on the wire
  and needs no rig, no clip and no state to keep in sync between machines. An
  animated iris would have needed all four and would have been invisible past
  about ten metres. Nothing else on the gun moves: recoil, muzzle flash and the
  net itself are Unity-side transforms and particles.
- **The concept's "TONY BOY" lettering is not reproduced.** It is the
  illustrator's signature, not part of the prop.
- **Charcoal nose over orange body, not the other way round.** Built inverted
  first and corrected against the sheet. It is the single most recognisable
  thing about this silhouette, so the seam that carries it is a real edge loop
  rather than a guessed plane: `SPLIT_Y` is one constant that ends the hollow
  mouth section *and* changes the colour, at exactly 40.0% of the drum's length
  back from the rim. Note that an iso view foreshortens the nose and makes the
  dark look like two thirds of the drum; the split is 40/60 measured
  orthographically, which is what the sheet asks for.
- **The drum is `Mat_Paint_Safety_Orange`, not `Mat_Metal_HullRust_Orange`.**
  The first build used the rust (`#764E2A`), which renders brown; the sheet's
  drum is a considerably brighter, more saturated orange, and a painted
  canister belongs in the paint family rather than in bare oxidised steel
  anyway. That makes the drum **the same orange as the receiver's stripes,
  which is correct and not a clash** — they are one colour on the sheet, and
  the stripes read against the grey receiver, not against the drum.
- **The rust stayed, on the ribs.** `Mat_Paint_Safety_Orange` is a flat bright
  paint with no texture of its own at this size, so the body needed a second
  tone. Steel-grey ribs on bright orange read as a decal stuck on a cylinder; a
  warm, darker, related tone reads as scuffed strapping over the paint. Ribs
  rather than a weathering band, because a band eats into an orange field that
  is already the smaller half of the two-tone and would spend the silhouette
  read to buy the texture. This also keeps the material list honest: nothing in
  it is a dead slot.
- **Petals are clocked to the diagonals, not to the vertical and horizontal.**
  Built the other way first, and it failed twice: a petal on the horizontal put
  the gun's widest point 6 cm outside the drum, taking "the canister is the
  widest thing on this gun" away from the silhouette, and a petal on the bottom
  ran straight through the L-bracket under the drum's belly.
- **Petals are curved shells, not flat plates.** A flat plate wide enough to
  frame the bore stands its own corners 11 mm off a 0.13 m barrel, which reads
  as damage rather than as design.
- **The bundle is a lumpy mass, not a woven net.** At the depth it sits, a real
  mesh is a few hundred triangles of moire that resolves into nothing. What has
  to read is "something bulky and fibrous is in there", and a jittered profile
  plus three folded cord loops does that at every distance. It is recessed 30 mm
  and kept 10 mm clear of the bore lining so the black ring shows all the way
  round it — flush with the rim it stopped being *in* a barrel and became a cap
  on the end of one.
- **Grip wraps are lofted from the grip's own cross-section, not rings.** A
  circular ring only ever touches the grip's two flat sides, so the first
  version read as a row of windows cut in a magazine. `GRIP_SECTIONS` is one
  table feeding both the core and the wraps, so a wrap can never end up
  narrower than the handle it goes round.
- **Two sign traps, both found by rendering rather than by reading.** A box or
  cylinder laid on the drum needs `Rotation(90° − a, 'Y')` to put its local +Z
  radial, not `Rotation(a, 'Y')`, which lays every rib flat; and a tangential
  hinge pin needs `Rotation(−a, 'Y')`, where `Rotation(a, 'Y')` points it
  straight out of the drum. Both are in the comments at the call sites.
- **`BEVEL_W` is 0.0016 and applied only to an accumulated `hard` list.** The
  hoses are 9 mm tubes and the stripes 3 mm proud; a whole-part bevel at this
  scale exceeds half of either and `finish()`'s `remove_doubles` welds the
  result into a blob. Same trap as `portal_gun` and `gravel_blaster`; not
  rediscovered.
- **Proportions were chosen by eye against the illustration**, which carries no
  dimensions. The two numbers that were not free are the ones the brief pinned:
  the canister at 0.26 m across and the gun at ~0.62 m overall.

## Counts

| Object | Triangles |
| --- | --- |
| `Mesh_NetGun_Body` | 6 180 |
| `Mesh_NetGun_Bundle` | 568 |
| `Mesh_NetGun_Spent` | 6 180 |
| `Marker_Muzzle` / `Marker_Grip` | 12 each |

Exported FBX: 4 objects, **6 772 triangles**, 12 materials localised, 137 KB.

## Unity side

Task 12 builds `NetGun.prefab` from this FBX. What it needs from here:
`Marker_Muzzle` for the muzzle transform, `Marker_Grip` for the `ItemGrip`
point, `Mesh_NetGun_Bundle` for `loadedBundle`, and `holdSize` 1.25 per the
`Gun` bracket above. The markers are 4 mm cubes and should be kept with their
renderers disabled rather than deleted — a disabled renderer is skipped by
`EquipItemSocket`'s bounds measurement, so a marker cannot quietly influence
how large the gun is held, and keeping the link is what lets a re-export from
Blender reach the prefab.
