# expedition_rig + pack_holders — build record

Built 2026-08-23. Replaces `expedition_backpack` as the player's deployable pack.
Spec: `docs/superpowers/specs/2026-08-23-physical-inventory-design.md`, section 3.

Two files, because they have different lifetimes: the rig is one authored object Unity binds
to by name, the holders are five small prefabs the code instantiates and stretches.

| File | Holds |
|---|---|
| `expedition_rig.blend` | `Coll_Rig_Expedition` — 35 meshes, 5 `PIVOT_*`, 7 `SURF_*`. 27.3k tris. |
| `pack_holders.blend` | `Coll_Holder_*` x5 — 30 meshes, 20 `HARD_*`. 8.9k tris. |

Updated 2026-08-24 with the **rack**: the front leaf flipped up. See the section below.

Updated again 2026-08-24: the rack's cradle horns were **replaced by two cargo nets**. See
"Nets, not horns".

Updated 2026-08-25 (second pass that day): every `SURF_*` rectangle became an **exact multiple
of the 0.09 m cell**, and the rig gained a **fifth hinge — `PIVOT_Lid`**, the stowed box's top.
See "Even cells + the lid" at the bottom.

## Why new files rather than an edit

`expedition_backpack.blend` is untouched, for the three reasons that file gave for not editing
`field_backpack`: the `.blend` is the source of truth and may carry hand edits a generator
would destroy, its own header forbids re-running the generator, and the swap is a single
prefab reference on `PlayerCharacter` — so the whole change is reversible without touching
history. Spec decision 8.5 records the same choice.

## The one decision a reader will want explained: the rig is authored OPEN

`expedition_backpack` is authored closed and Unity swings its hinges to open it. This file is
the other way round — every pivot is at rotation zero in the deployed pose.

1. Every dimension the spec gives is a measurement of the deployed rig (open footprint,
   standing height, the six surfaces, the 65 degree panel). Authored closed, none of them
   could be checked in the file.
2. `SURF_*` empties only mean anything deployed, and their axis convention is stated in world
   terms — "+Y out of the surface". Authored open, that is literally what the file contains
   and `dump_surfaces()` can assert it.
3. The laid-out kit is the deliverable.

To stow, drive the hinges from the authored zero:

    PIVOT_Back    X  +25      panel from 65 deg up to vertical
    PIVOT_Leaf    X  -90      leaf up off the ground, against the panel
    PIVOT_Wing_L  Y  -90      wing folds up onto the leaf
    PIVOT_Wing_R  Y  +90
    PIVOT_Lid     X  -90      the apron, relative to the LEAF it rides: caps the box

`BackpackDeployArc` and the `NetMsg.PackState` machine are unchanged; only the sign of the
hinge travel is.

## The rack (added 2026-08-24)

The deployed rig has a **third configuration**: `PIVOT_Leaf` at X -90 while the panel, the wings
and the stakes stay open, standing the front leaf up as a vertical rack for the biggest gear.

**No hinge of the rack's own, and that is the design.** The rack angle *is* the leaf's stow
angle — same pivot, same number — so racked and stowed are the same place for the leaf and the
only difference is what the rest of the rig is doing. (The `PIVOT_Lid` hinge added 2026-08-25 is
the stowed box's top, not a rack member — see "Even cells + the lid".)

**The rack is the leaf's UNDERSIDE**, and that follows from the fold rather than being a choice.
Under X -90 the mat — `SURF_Leaf` and the lash line — swings round to face the back panel, and the
underside comes up to face the player. Every piece of new geometry therefore went under the leaf:

| Object | What it is |
|---|---|
| `Mesh_Rig_RackLadder` | three runners + two rails + skid pads. The mat lies ON this; raised it is the frame the nets are stretched in. |
| `Mesh_Rig_RackNets` | two knotted cargo nets, side by side, running the full height of the board. |
| `Mesh_Rig_RackHandle` | the pull loop on the leading edge — the only part visible with the mat down. |

## Nets, not horns (2026-08-24, second pass)

`Mesh_Rig_RackHooks` — three pairs of inboard-curling brass cradle horns with shock cords — is
**gone**, replaced by `Mesh_Rig_RackNets`. The horns were a good answer to "what holds a spar";
the face was never for spars. It exists to take **bulk**, and a horn holds one thing each and only
if that thing is long and straight. Netting holds whatever is pushed into it. It also survives an
empty rack better: three bare horns read as an unfinished fitting, a taut net reads as ready.

### What survived of the ladder, and why

**The frame stays.** A net is not a self-supporting object — it is a membrane that has to be laced
to something on all four edges, so deleting the ladder with the horns would have left two nets
stapled to the back of a sheet of canvas. What changed:

- **A third runner at x = 0.** Without a centre post, "two nets" is one net with a gap down the
  middle. It is thinner stock (`RACK_POST` 0.015 against `LADDER_R` 0.018) and carries no skid pad:
  it is a lacing post, not a foot, and it stops 12 mm short of the ground plane so the mat still
  rests on the two runners that were sized to carry it.
- **Four rungs became two rails**, at `RAIL_FOOT_Y` -0.195 (hinge end, the bottom of the raised
  rack) and `RAIL_HEAD_Y` -0.830 (leading edge, the top). A mid-span rung existed to hang gear off;
  the nets do that now, and a rung behind a net is clutter you can see through.
- **No clamp boss where a rail crosses a post.** One was modelled and cut: at `RUNG_Z` its flat cap
  lands within 2 mm of the rail's own bottom facet, which is exactly the coplanar abutment
  `_zverify.py` exists to catch. The rail's surface already reaches past the post's, so the crossing
  reads welded without it.

### The sag, and which pose it is authored for

`net()` is copied **verbatim** from `expedition_backpack.py` — same signature, same hard-coded
0.022 sag, same six-chord approximation of the bow — so the two packs' netting is literally the
same routine. It is not in `_buildlib`; all three pack scripts carry their own copy.

The nets sit on a board that is **horizontal when the leaf is down and vertical when it is up**, so
the sag reads differently in each. **It is authored for the RAISED pose**, because that is the only
pose the rack is usable in — the mat-down pose hides the whole assembly under 0.62 m² of canvas.

The sag runs **+Z authored, i.e. INTO the frame and away from the player once raised.** Two
independent reasons, and they agree:

- **It is what a loaded net does.** `SURF_Rack` sits at `RACK_FACE` -0.051, *outboard* of the cords
  (deepest knot -0.050), so gear leans on the nets from the player's side and bows them back toward
  the mat. Sagging the other way would be a net bulging into its own load.
- **It is the only direction with room.** Authored down, -Z is 6 mm of gap before `LADDER_FLOOR` and
  then sand: a net sagging that way would hold the mat off the ground, which is the one thing the
  ladder exists to prevent. Sagging +Z the nets tuck up under the mat with 23 mm of clearance.

Lying down, therefore, the nets read as slack tucked up out of the sand — which is also what they
*should* look like there. Nothing had to be compromised to get both poses right.

### Numbers

| | |
|---|---|
| Bays | x -0.336 .. -0.016 and 0.016 .. 0.336, y -0.195 .. -0.830 — 0.32 x 0.635 m each |
| Mesh | `NET_COLS` 3 x `NET_ROWS` 5, about 0.107 x 0.127 m squares |
| Cord | 6 mm square, `Mat_Plastic_Rubber_Black` — the only dark material on a pale canvas board, and already the shock-cord material on this rig |
| z envelope | -0.050 .. -0.023, inside `LADDER_FLOOR` .. 0 with 4 mm to spare |
| `_zverify.py` | 0 clashing pairs |

The two nets share **no** geometry. Each has its own inboard edge cord, 1 mm clear of the centre
post on its own side; a single edge drawn twice in the same place is a z-fight, not a stronger net.

**Triangles: 20,392 -> 20,972** (+580 net). Horns -1,680, nets +2,320, ladder -60. The nets are
cheap because `net()` draws cords as `seam()` boxes and the part carries `bevel=0.0` — a chamfer on
a 6 mm cord is 3.7x the triangles for something invisible at 1.9 m. The 48 knots are 960 of the
2,320 and are the one thing to cut first if this ever needs trimming.

**Every z between `LADDER_FLOOR` (-0.054) and 0.** That bound is the whole geometric constraint —
nothing may reach past the runners or the mat does not sit flat, and nothing may come within 2 mm
of the canvas underside at z = 0 or `_zverify.py` flags it. Final count is 0 clashing pairs.

### `SURF_Rack`, and why there is only one

0.80 x 0.60 m. **0.48 m², the largest rectangle on the rig**, and the only face with both axes
over half a metre (leaf 0.39 m², long goods 0.22, wings 0.15, back panels 0.13).

Length was never the gap: `SURF_LongGoods` already spans 1.60 m. **Bulk** was — every other face
is at most 0.50 m deep, so a wing panel 0.6 m across fits nowhere at any yaw.

Two rack surfaces were drawn up and thrown away. A hook band across the top has to be carved out
of the same 0.72 m of leaf, and any band wide enough to be worth having leaves a field *smaller*
than the flat leaf it replaces — which would make raising the rack a downgrade. One uninterrupted
rectangle is the entire value of the feature.

**Still one after the nets.** Two nets side by side plainly suggest two surfaces and it is still
the wrong cut: each bay is 0.32 x 0.635, and a bulky item — the very thing the rack exists for —
straddles the centre post and lies across both nets. The post is 30 mm of steel, a divider and not
a wall. Splitting the face would forbid exactly the load the face was added to take.

If the bays ever *do* need addressing separately, the geometry already implies the answer and it is
written into `_rack_nets`' docstring:

    SURF_Rack_L   parent leaf   loc (-0.176, RACK_MID_Y, RACK_FACE)   ROT_RACK   0.30 x 0.60
    SURF_Rack_R   parent leaf   loc ( 0.176, RACK_MID_Y, RACK_FACE)   ROT_RACK   0.30 x 0.60

That needs a **new** `PackSurfaceId`, appended. It must never be swapped in over the existing
`Rack`: the seven ids are load-bearing and renumbering them silently re-points every saved
placement. Nothing of the sort has been added — the file still ships exactly seven `SURF_*`.

### `ROT_RACK`, and the one assert that had to change

The empty is authored in the deployed pose, pointing at the sand, and has to read correctly after
the raise. `ROT_RACK = (90, 180, 0)` XYZ, i.e. `Ry(180) @ Rx(90)`:

    local +X -> world -X, still -X raised   the focus camera's right; uv.x grows rightward
    local +Y -> world -Z, and -Y raised     straight out of the rack at the player
    local +Z -> world -Y, and +Z raised     uv.y is height up the rack

Right-handed: `X x Y = (-1,0,0) x (0,0,-1) = (0,-1,0) = Z`. Get the handedness wrong and every
placement on the rack is mirrored, with nothing in Blender showing it.

`dump_surfaces()` asserted "+Y has an upward component" on all six faces, which is a statement
about surfaces that lie DOWN — a vertical rack's face is horizontal by definition and failed the
check for being exactly right. The `FOLDED` table now carries, per exceptional surface, the fold
to measure it in and the direction it must then point. One entry.

### What the fold costs, and what was left alone

**The lash rail rides up with the leaf.** `Mesh_Rig_LashRail` and `SURF_LongGoods` are on
`PIVOT_Leaf`, so raising the rack lifts 1.70 m of webbing to the top of the board where it reads as
an unsupported crossbar over the wings. Left as it is: re-parenting the rail to the root would fix
the racked pose and break the shipped stow, where the rail has to fold with the member it is sewn
to. From the focus camera's own angle it reads as a hanging rail, which is not wrong.

**The raised leaf clips the frame's two front uprights** by up to 20 mm at x = ±0.410, z 0.03..0.20
— a leaf swinging to vertical coming to rest against the rail that stops it. This is not new: the
shipped stow pose puts the leaf at the same angle on the same pivot. It is on the far side of the
board from the camera.

**Bounds are unchanged**: 1.728 x 1.447 x 0.819 m. Racked, the rig stands 0.756 m against the
deployed 0.738 — the rack is not the tallest thing on it, the tank still is.

**Triangles: 17,376 -> 20,392** (+3,016) — superseded, see "Nets, not horns": 20,972 now. The
horns came in at 2,664 on the first pass and were cut
to 1,680 the way the last build of this rig was cut — one fewer tube segment and no weld collars,
which at the 1.9 m the focus camera sits at is invisible.

## Decomposition

Objects are split by what they hang off, then by what someone might plausibly restyle alone.

    root          Frame, Harness is NOT here (see below), HipBelt_L/R
    PIVOT_Back    BackPanel, BackWebbing_L/R, OxygenTank + _Bands + _Manifold,
                  Harness_L/R, Kickstand_L/R
    PIVOT_Leaf    FrontLeaf, LeafGrommets, LashRail, RackLadder + RackNets
    PIVOT_Wing_L  Wing_L, WingRib_L        (likewise _R; a CHILD of PIVOT_Leaf)
    PIVOT_Lid     Lid, LidGrommets, RackHandle, Stake_L/R   (a CHILD of PIVOT_Leaf)

(As first written this block predated the rack, the 2026-08-24 reparents and the lid; it is
kept current, the dated sections below carry the history.)

**The kickstands ride `PIVOT_Back`** rather than a fifth hinge, because the spec names exactly
four moving parts. A leg that stows flat against the back of the panel it props is where a
real one goes anyway; it reaches the ground by riding the panel instead of by a hinge of its
own. It does mean the leg's angle to the panel is fixed, which is only wrong mid-animation.

**The lash rail rides `PIVOT_Leaf`.** It spans the leaf and both wings, so it belongs to no
single hinge. The leaf is the member its midspan is sewn to, and the buckled ends are what a
player would unclip from the wings before folding.

**The harness is on the panel's back face, not on the frame.** The spec says the frame carries
it, but when the rig is worn the panel is the vertical member against the wearer's back and
the frame is a 0.22 m tray at the bottom. Straps anchored to the tray would have nothing to
run up. The hip belt IS on the frame, which is where a hip belt belongs.

## Materials

All from the shared palette, none added. `Mat_Fabric_Canvas_Faded` (leaf, wings, tray, harness),
`Mat_Fabric_Wing_Ochre` (all webbing tape, a shade off the panels), `Mat_Metal_Steel_Worn`
(frame, kickstands, tank bands, wing ribs), `Mat_Metal_Brass_Tarnished` (valve, buckles,
grommets), `Mat_Plastic_Rubber_Black` (cord, grommet seals, hip pad, hose),
`Mat_Paint_Safety_Orange` (the tank), `Mat_Emissive_Amber` (the gauge lamp),
`Mat_Metal_Rust_Heavy` (kickstand feet, skid pads).

**Exactly one emissive in the whole rig.** The first pass had a gauge plus two pilot lamps on
the manifold; three warm points meant none of them was the thing the eye returns to, which is
the gauge's entire job (spec 3.2). It also moved from the top of the bottle to the bottom —
lower and central is nearer the middle of frame at the focus camera's 38 degree pitch, and it
keeps 0.06 m off the rig's standing height.

## The holders, and the authoring rule

Each holder is modelled in the unit cube: `+X` the stretch axis (item length, -0.5..+0.5),
`+Z` across (item width, -0.5..+0.5), `+Y` up (item height, **0..1**, origin on the surface
plane). All three axes are normalised, not just the footprint — "rings at 25% / 75% height"
means nothing unless height is 1.00 here.

Every rigid part hangs off a `HARD_` empty and is authored at its **true metre size**: buckle
0.052, tensioner 0.046, hook 0.070, snap hook 0.092, eyelet 0.030 m. At unit scale that
hardware looks tiny, and it is supposed to — on the 0.26 m Leash and the 1.35 m LaserStaff
alike it comes out those sizes. Without the counter-scale a strap spanning the staff arrives
with buckles the size of dinner plates.

**No `HARD_` empty is rotated.** The builder's counter-scale is a componentwise reciprocal,
which only inverts a non-uniform scale when the child's axes line up with the parent's; under
a rotation the two do not commute and the part comes out sheared rather than restored. Every
angled buckle has its rotation baked into the mesh instead. `dump_holders()` asserts this.

Soft parts do stretch, including their width and thickness. That is the honest cost of the
scheme and only hardware is exempt.

## Traps hit while building, recorded so the next person does not

**A concave profile is still forbidden.** The scabbard's cross-section is a C, so
`Holder_Sleeve` is a floor, two walls and an end cap rather than one lofted trough — the same
reason `expedition_backpack`'s carcass is four convex lofts.

**`_zverify.py` found five real coplanar clashes and every one was an abutment.** Detail that
sits ON canvas — webbing tape, quilt welts, the rolled hem, hinge knuckles — must be sunk into
it or lifted clear of it by more than 2 mm, never placed flush. Two of the five were subtler:
a corner puck built as an upright cylinder puts its flat cap within a hair of the canvas top
face, fixed by closing the hem ring at a mid-edge so `bent_tube`'s own collars land on the
corners instead. Final count is 0 pairs.

**Mirrored parts need sorted bounds.** `quilt()` insets its seams from the panel edge; the
left wing passes its bounds the other way round, so the inset ran the seams OUT past the edge.
Visible only as the left wing measuring 0.03 m wider than the right in the object dump.

**Bevel segments, not geometry, was the triangle budget.** The first build came in at 39k
against a ~16k budget with nothing excessive modelled. A one-segment chamfer instead of a
two-segment round, plus dropping the bevel on the grommet fields entirely, took it to 17.4k
without a visible difference at the 1.9 m the focus camera sits at.

## Where the model departs from the spec

**Open depth is 1.45 m, not 0.95 m.** The part dimensions in spec 3.1 and the 0.95 m footprint
cannot both hold, and this is arithmetic rather than judgement: a 0.72 m leaf hinged off the
front of a 0.30 m frame already spans 1.02 m before the panel reclines 0.26 m behind its own
hinge. Part dimensions were kept, since those are the load-bearing numbers Unity and the
surface rectangles are built on. Width came out 1.728 m against the spec's 1.72, and the
panel's own top edge stands at 0.682 m against the spec's 0.68 — those two are exact.

Overall bounds: **1.728 x 1.447 x 0.819 m**, the height including the tank rising past the
panel head and the depth including the kickstand feet.

**`SURF_Leaf` is 0.78 x 0.50, not the leaf's full 0.86 x 0.72.** It is inset to clear the lash
rail across the front and the hem all round, so an item placed at the edge does not overhang.
The same applies to the other five. The rectangles are printed at build time, since nothing in
the `.blend` encodes them — the empties are deliberately identity-scaled, because Unity parents
items under them and a scaled parent would rescale every item.

## Export

`expedition_rig_export.py` ships both files, to `Assets/Game/Art/Models/Props/`. It is not
`_exportlib.export()`, which passes `object_types={'MESH'}` and would silently drop every
`PIVOT_`, `SURF_` and `HARD_` empty — that is, everything Unity binds to.

    blender --background --python components/props/expedition_rig_export.py

Two things a reader will want explained.

**`SURF_*` empties sit at the CENTRE of their rectangles; `PackSurface` reads its transform
origin as the `(0,0)` CORNER.** A uv runs from `(0,0)` to `Size`, which is the span
`PlacementGeometry.Contains` tests. `ExpeditionRigWiring` therefore puts the component on a
`SURF_<name>_Rect` child offset to the corner rather than on the empty itself; the empty's
rotation and scale are inherited, so the axis convention is untouched. Wired straight onto the
empty, every item lands half a surface out and a quarter of the mat hangs off the panel.

**`pack_holders.fbx` exports on IDENTITY axes** — `axis_forward='-Y', axis_up='Z'` — while the
rig uses the library's usual `-Z`/`Y`. `pack_holders.blend` is authored in Unity's frame
already (`+X` stretch, `+Z` across, `+Y` up 0..1, in Blender coordinates), so it wants no
conversion. It cannot simply be rotated instead: Blender bakes the conversion into every ROOT
object's transform, so a root that was at identity arrives at euler (270, 0, 0) — which is
harmless on the rig, whose hinges turn relative to rest, and fatal on a holder, because
`HolderBuilder` overwrites the holder root's rotation and `CounterScaleHardware` inverts the
fit componentwise. Both need zero rotation between the prefab root and the `HARD_` empties.
The wiring script checks for it and refuses rather than shipping sheared hardware.

## 2026-08-24 warm soft-goods pass (`expedition_rig_dress.py`)

Worn, the folded rig read as carried furniture — flat olive boards, legs, no soft mass.
This pass gives the stowed pack the previous pack's (`expedition_backpack`) warm colours
and silhouette, in place, without touching any mechanic:

- **Recolour, slot-level:** leaf, wings, back panel and frame tray swap their
  `Mat_Fabric_Canvas_Faded` slot for the new palette entry `Mat_Fabric_Canvas_Sand`
  (#F4BD62, derived from the old pack's hand-picked body tone). Webbing stays ochre,
  harness stays faded — the same warm/dirty split the old pack had.
- **Eight new objects, one per movable part:** `Mesh_Rig_SidePouch_L/R` +
  `_SidePouchFlap_L/R` + `_SidePouchStraps_L/R` (bulging sand pods with ochre flaps and
  brass buckles on the frame ends — stowed they fill the lower flanks below the folded
  wings, 5 mm clear of the wing ribs; deployed they flank the hub), and `Mesh_Rig_Bedroll`
  + `_BedrollStraps` riding `PIVOT_Back` along the panel crest (stowed: the classic roll
  across the pack's top; deployed: a roll on the recliner's crest, 50 mm clear of the tank).
- No `SURF_*` face is encroached; the empties and hinge lines are untouched.

**Blender stow signs are NOT the HingeTable's.** Measured in this session: Blender stow is
Back +25 / Leaf -90 / **Wing_L +90 / Wing_R -90** — the wings' signs are opposite to the
Unity table, exactly the mirror the wiring script's comment warns about. The dress script
folds the rig with the Blender signs to assert clearance, then saves at authored zero.

Triangles: 20,972 -> 25,184 (+4,212, the pouch lofts and roll). 33 meshes.

## Hand edits after generation

**2026-08-24 — the stakes ride `PIVOT_Leaf`.** `Mesh_Rig_Stake_L/R` were authored static
("a stake driven into the sand does not ride a hinge"), but each stake's CORD runs to the
leaf's corner grommet in the same rigid mesh — so the moment `PIVOT_Leaf` turned up into the
rack, the board rose and two cords plus stakes lay on the sand pointing at nothing, reading
as debris that had fallen off the pack. Both stakes were reparented under `PIVOT_Leaf`
in-place (attach() convention: identity parent-inverse, world pose preserved), so they swing
up with the board in the rack and fold with it in the stow. `BackpackObject.ApplyStakes` is
parent-relative and needed no change; the stake-drop beat plays after the leaf has landed, so
the deploy looks identical. Re-run `Tools/SpaceGame/Items/Build Expedition Rig Prefab` after
any re-export — the stake transforms' fileIDs change with the hierarchy.

## 2026-08-25 — the board was DEEPENED (`LEAF_EXTRA = 0.200`)

`ItemScaleLadder` (`Assets/Game/Editor/Items/ItemScaleLadder.cs`) roughly doubled the held
gear that day: the Dragon Bazooka's 1.25 m was adopted as the anchor and twelve items climbed
to meet it. The mat could not hold what it was drawn for any more — at 0.50 m deep its widest
axis-aligned run was 8 cells, so nothing longer than 0.72 m fitted it at any yaw, and a single
1.25 m launcher took half the rack.

So the leaf grew **0.200 m at its leading edge only**. The hinge end did not move, which is
why the frame, the panel and every fold still meet the board exactly where they did.

| | before | after |
|---|---|---|
| `SURF_Leaf` | 0.78 x 0.50 (8x5 cells) | **0.78 x 0.70** (8x7) |
| `SURF_Rack` | 0.80 x 0.60 (8x6) | **0.80 x 0.80** (8x8) |
| `SURF_Wing_L/R` | 0.38 x 0.40 | 0.38 x 0.60 |
| `SURF_LongGoods` | 1.60 x 0.14 | 1.60 x 0.14 (unchanged, moved out with the edge) |
| board inventory area | 0.87 m² | **1.19 m²** (+37%) |
| open bounds | 1.728 x 1.447 x 0.819 | 1.728 x **1.647** x 0.819 |
| triangles | 20,972 | 22,016 |

**The lash rail stayed 0.14 m deep.** Deepening it to 0.27 was drawn up alongside this and
dropped: the rail has to sit between the mat's far edge and the board's end, so a 0.27 m rail
costs a further 0.13 m of board on top of the mat's 0.20. That stops being "a bit taller" on
the wearer's back, and the rack's overhang rule already takes every long item — verified, see
below.

### What moved, and what that cost

Everything measured from the LEADING edge carries `LEAF_EXTRA`: `LEAF_Y0`, `WING_Y0`,
`RACK_Y0`, `RAIL_HEAD_Y`, the new `RAIL_MID`, and the rack band (`RACK_MID_Y`, `RACK_D`).
Three things needed more than a shift:

- **The stakes travel with the board.** `_stake`'s head and tip were hardcoded just past the
  old `LEAF_Y0`. Left alone they stayed put while their guy-cord's anchor grommet moved
  0.20 m out, stretching each cord from 0.078 m to 0.274 m — caught by the dimension diff, not
  by eye. They are authored against `LEAF_Y0` now and keep their drawn slack.
- **One more quilt line and one more grommet row**, at the pitch the existing ones use
  (0.180 m and 0.190 m). A longer quilted mat has more stitch lines, not wider panels, and
  without the extra grommet row the new 0.20 m would be untethered canvas.
- **The wings kept pace** (0.60 -> 0.80 m long, plus a third rib strap and a fourth hinge
  knuckle), so the front still closes like a box over the taller board instead of leaving its
  top fifth unhugged.

### Why re-running the generator was safe here

The header rule — never regenerate over a hand-edited `.blend` — was honoured by *proving* the
rule did not bite, not by ignoring it. Before any edit, the shipped file was rebuilt from
`expedition_rig.py` + `expedition_rig_dress.py` + the 2026-08-24 reparent above and diffed
object-by-object against what was on disk: **44 objects, 0 differences** across parent,
location, rotation, scale, vertex count, polygon count and dimensions. The file held no hand
edit those three steps do not reproduce, so the depth change is a parameter change and nothing
was lost. Anyone repeating this must re-run that control first — the answer will stop being
zero the moment somebody models on this file by hand.

Verified after the change: `_zverify.py` reports **0 clashing pairs** (same as before), the
object set is unchanged at 44 with no reparenting or rescaling, and every difference is a
shift or a stretch along -Y.

Re-run `Tools/SpaceGame/Items/Build Expedition Rig Prefab` after re-export, then
`Tools/SpaceGame/Items/Reseed Undrawn Pack Shapes` — surfaces changing size does not touch the
shape library, but the item resize that prompted this does.
## 2026-08-25 — even cells + the lid (second pass that day)

Two demands in one rebuild: **every `SURF_*` rectangle is now an exact multiple of
`PackGrid.Cell` (0.090 m)** so the placement grid fills each face edge to edge with zero hem,
and the stowed rig — until now an open-topped box — **closes, on a fifth hinge**.

### The rectangles

| SURF | was | now (cells) | centre moved |
|---|---|---|---|
| `SURF_Leaf` | 0.78 x 0.70 | **0.72 x 0.72** (8x8) | y -0.530 -> -0.525 |
| `SURF_Wing_L/R` | 0.38 x 0.60 | **0.36 x 0.63** (4x7) | y -0.580 -> -0.590 |
| `SURF_Back_L/R` | 0.26 x 0.50 | **0.27 x 0.54** (3x6) | s 0.310 -> 0.300 |
| `SURF_LongGoods` | 1.60 x 0.14 | **1.62 x 0.09** (18x1) | unchanged |
| `SURF_Rack` | 0.80 x 0.80 | **0.81 x 0.81** (9x9) | unchanged |

205 usable cells -> **255**, coverage 100%. `PackGrid.CellsOn` floors and `Hem` centres the
leftover, so exact multiples mean zero hem with **no code change**; only
`ExpeditionRigWiring.SurfaceTable` carries the new numbers.

**The decoration moved onto the same grid.** Stitch/grommet/webbing pitch is now 0.180 m = two
cells, phase-aligned to cell boundaries: leaf grommets `(-0.270..0.270, -0.255..-0.795)` and
quilt `xs (-0.18, 0, 0.18) / ys (-0.345..-0.885)` (rows interleaved with grommets), wing
grommets `(±0.565..±0.745, -0.365..-0.905)` and quilt to match, lash keepers on `±0.270/±0.540`,
and the back webbing rebuilt as **the grid itself**: vertical tapes ON the rect's outer cell
columns (x 0.150/0.420), six rungs on the row centres at the cell's own 0.090 pitch, eyelets on
row boundaries. Resize a surface only in whole cells and move its decoration with it.

Two cradle nudges paid for the wider back rects: the tank foot posts moved in to x 0.124 (outer
face 0.146, 4 mm clear of the rect edge at 0.150), and their flanges sank to `off -0.016` with
thickness 0.020 — crest +0.004, under the +0.006 item plane, **and** 6 mm clear of the webbing
tapes' inner face at -0.010, which at the first attempt (`off -0.010`) was a same-facing
coplanar abutment `_zverify.py` correctly flagged. The rack's skid pads shrank r 0.030 -> 0.025
so the 9x9 rect keeps the same 5 mm foot-pad overlap the 8x8 already accepted.

### The lid

Stowed, the folded rig was an open-topped box — leaf in front, wings as flanks, panel and
bedroll behind, sky above the tank. **No fixed geometry can close it**: the stow maps every
candidate carrier's plane to vertical, so the top had to be a hinge. Candidates measured and
rejected: a leaf-fixed wall (shadows 30-59% of the mat and the whole lash rail deployed), a
panel-fixed visor (fouls the back rects' top row and the bedroll owns the crest), wing-fixed
(their stowed free edges are vertical lines — wrong axis), frame-fixed (floats).

**`PIVOT_Lid`** sits on the leaf's leading edge at `(0, LEAF_Y0, CLOTH_T)` and is a **child of
`PIVOT_Leaf`**, exactly like the wing pivots. Its apron (`Mesh_Rig_Lid`) is authored deployed as
`LID_D = 0.360` more metres of mat — same slab, hem, and quilt language, two 0.180 m panels deep
— coplanar with the leaf beyond `LEAF_Y0`, the slab ending exactly on the leaf's end face
(opposed faces, an occluded joint, not a z-fight). Three r 0.012 knuckles straddle the seam.
Folding it **X -90 relative to the leaf** stands it up mid-choreography as the end wall, then it
rides the leaf's own -90 to horizontal, capping the box at z 0.936..0.962 — 10 mm above the wing
crests, 54 mm above the bedroll — measured on the imported prefab (stowed lid mesh: a thin
horizontal band y 0.924..0.962 across the opening). In the **rack pose** the same relative -90
turns it into a hood over the board's top edge; the "every moved part is asked for its own stow
angle" rule covers it with zero special cases.

**The leading-edge furniture moved out with the edge, onto the lid**: the pull handle (fixing a
latent bug — it was hardcoded at the pre-`LEAF_EXTRA` edge and had sat mid-board over the lash
rail since the deepening; it is authored against `LID_Y0` now), the leading corner-grommet pair
(`Mesh_Rig_LidGrommets`, at ±0.372 / `LID_Y0 - 0.055`), and **both stakes** — their guy cords
tie to those grommets, the same travel-with-the-edge rule the deepening established. Stowed the
stakes lie lashed across the lid's rear corners, tips to z ~1.03.

Unity side: `BackpackHingePart.Lid = 5` (append-only), `HingeTable` +
`("PIVOT_Lid", Lid, Vector3.right, -90f)` — sign **measured on the imported prefab** (X-hinges
arrive sign-true, unlike the wings' mirrored Y: -90 caps the box, +90 buries the apron). Until
`BackpackObject`'s beat sheet names the part, the lid swings on the shared generic ease and does
not follow the rack raise — the follow-the-board rule the wings use is the known follow-up.

### Reproducibility, proven again

Control first, as the deepening demanded: the shipped `.blend` was rebuilt from
`expedition_rig.py` + `expedition_rig_dress.py` + the two 2026-08-24 reparents and diffed
object-by-object — **44 objects, 0 differences**, so regenerating lost nothing. The rebuild was
then diffed the same way: 47 objects, **22 deltas, every one prescribed** (the lid trio new;
webbing/grommets/keepers re-gridded; cradle nudged; pads shrunk; handle and stakes re-homed;
five `SURF_*` centres moved). `_zverify.py`: **0 clashing pairs**.

**The hand-edit step is gone**: the wing-pivot reparents (and the new lid pivot's) are folded
into `expedition_rig.py`'s `main()`, and the stakes are simply authored on `PIVOT_Lid`, so a
future regeneration is generator + dress pass and nothing else. The dress script gained the lid
in its `RECOLOUR` list and its `STOW` clearance pose.

Triangles: 26,228 -> **27,284** (+1,056 lid, +288 lid grommets, -288 the migrated grommet
pair). Open bounds 1.728 x **2.007** x 0.819 (the apron and stakes extend -Y). Palette relink
after copying out of scratch was needed again (the known trap) and verified by colour.

Re-run `Tools/SpaceGame/Items/Build Expedition Rig Prefab` after re-export, then
`Tools/SpaceGame/Items/Reseed Undrawn Pack Shapes` — done 2026-08-25; the wiring verified all
7 surfaces, 5 hinges, 5 holders and the player reference off disk. Old saves: every face grew
in cells except LongGoods, hem recentring shifts stored uvs at most half a cell, and
`AdoptPlacements` first-fits any refusal — same behaviour as the deepening.
