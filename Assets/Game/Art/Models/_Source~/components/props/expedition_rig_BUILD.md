# expedition_rig + pack_holders — build record

Built 2026-08-23. Replaces `expedition_backpack` as the player's deployable pack.
Spec: `docs/superpowers/specs/2026-08-23-physical-inventory-design.md`, section 3.

Two files, because they have different lifetimes: the rig is one authored object Unity binds
to by name, the holders are five small prefabs the code instantiates and stretches.

| File | Holds |
|---|---|
| `expedition_rig.blend` | `Coll_Rig_Expedition` — 25 meshes, 4 `PIVOT_*`, 7 `SURF_*`. 21.0k tris. |
| `pack_holders.blend` | `Coll_Holder_*` x5 — 30 meshes, 20 `HARD_*`. 8.9k tris. |

Updated 2026-08-24 with the **rack**: the front leaf flipped up. See the section below.

Updated again 2026-08-24: the rack's cradle horns were **replaced by two cargo nets**. See
"Nets, not horns".

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

`BackpackDeployArc` and the `NetMsg.PackState` machine are unchanged; only the sign of the
hinge travel is.

## The rack (added 2026-08-24)

The deployed rig has a **third configuration**: `PIVOT_Leaf` at X -90 while the panel, the wings
and the stakes stay open, standing the front leaf up as a vertical rack for the biggest gear.

**No fifth hinge, and that is the design.** The rack angle *is* the leaf's stow angle — same
pivot, same number — so racked and stowed are the same place for the leaf and the only difference
is what the rest of the rig is doing. `HingeTable` in `ExpeditionRigWiring.cs` is still four rows.

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

    root          Frame, Harness is NOT here (see below), HipBelt_L/R, Stake_L/R
    PIVOT_Back    BackPanel, BackWebbing_L/R, OxygenTank + _Bands + _Manifold,
                  Harness_L/R, Kickstand_L/R
    PIVOT_Leaf    FrontLeaf, LeafGrommets, LashRail
    PIVOT_Wing_L  Wing_L, WingRib_L        (likewise _R)

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
