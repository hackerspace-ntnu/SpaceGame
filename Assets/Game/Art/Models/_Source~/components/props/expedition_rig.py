"""components/props/expedition_rig — the pack that unfolds into a laid-out kit.

Supersedes `expedition_backpack` as the player's deployable pack. That file is a
top-loading rucksack whose contents live down a tube; this one comes off the
back, drops on its base and lays its gear out flat in front of the player.

Spec: docs/superpowers/specs/2026-08-23-physical-inventory-design.md, section 3.
`expedition_backpack.blend` is left untouched on disk, for the same reasons that
file gave for not editing `field_backpack`: the .blend is the source of truth,
its own header forbids re-running the generator, and the swap is one prefab
reference on `PlayerCharacter`.

Like the two packs before it this file is a **contract, not a family**: exactly
one variation, and Unity binds to the object names below, so they are
load-bearing.

Authored OPEN. Read this before animating.
-------------------------------------------------------------------------------
`expedition_backpack` is authored CLOSED and Unity swings its hinges to open it.
This file is the other way round: **every pivot is at rotation zero in the
deployed pose.** Three reasons:

  * every dimension the spec gives — open footprint, standing height, the six
    surfaces, the 65 degree panel — is a measurement of the deployed rig, and
    authoring closed would mean none of them could be checked in the file;
  * `SURF_*` empties only mean anything deployed, and their axis convention is
    stated in world terms (+Y out of the surface). Authored open, that is
    literally what the file contains;
  * the laid-out kit is the deliverable. A reviewer opening this file should see
    it, not a folded parcel.

To stow, drive the hinges to these angles (degrees, about each pivot's own local
axis, from the authored zero):

  PIVOT_Back    X  +25      panel from 65 deg up to vertical
  PIVOT_Leaf    X  -90      leaf up off the ground, against the panel
  PIVOT_Wing_L  Y  +90      wing L folds UP onto the leaf
  PIVOT_Wing_R  Y  -90      wing R folds UP onto the leaf
  PIVOT_Lid     X  -90      the apron, relative to the LEAF it rides: mid-fold
                            it stands up as the end wall, then rides the leaf
                            over to cap the stowed box

`BackpackDeployArc` and the state machine are unchanged; only the sign of the
hinge travel is.

The two wing signs above were CORRECTED on 2026-08-24 and were the other way
round before. Measured off this file: a wing hinges about +Y at x = +/-0.435 and
reaches 0.420 m outboard, so its tip lands at

    x = +/-(0.435 + 0.420 cos t),   z = 0.016 + 0.420 sin t

for a turn of t. `PIVOT_Wing_L Y +90` puts the left wing at z 0.005 .. 0.445;
`Y -90` puts it at z -0.413 .. 0.027, i.e. through the ground. These are
BLENDER-frame signs. Unity's `HingeTable` carries the MIRRORED pair
(`WingLeft -90`, `WingRight +90`) and that pair is right THERE — measured on
the imported prefab, Y rotations arrive mirrored while X rotations arrive
sign-true — which is why the wiring file insists every hinge sign be measured
on the import rather than read out of this table.

The rack: a third configuration, on no new hinge
-------------------------------------------------------------------------------
The deployed rig has a second pose. `PIVOT_Leaf` turns X -90 **while the panel,
the wings and the stakes stay where they are**, standing the front leaf up as a
vertical rack for the biggest gear. That is the same angle as the leaf's stow
travel, on the same pivot, and it is deliberate: stowed and racked are the same
place for the leaf, and the only difference is what the rest of the rig is
doing. The rack needed no hinge of its own. (The rig DOES carry a fifth hinge
since 2026-08-25 — PIVOT_Lid — but it is the stowed box's top, not a rack
member; see "The lid" below.)

Which face ends up pointing at the player is the part worth stating, because it
decides where every piece of new geometry went. Under X -90 the leaf's top face
— the mat, with `SURF_Leaf` and the lash line on it — swings round to face the
back panel, and the **underside** comes up to face the camera. So the rack is
the leaf's underside, and the underside is where the ladder frame, the two cargo
nets and `SURF_Rack` live. Gear already strapped to the mat rides round with it
and is simply behind the board until the leaf is put back down, which is what a
real loaded mat does.

Which end of the raised board is the top also follows from the fold, and it is
worth writing down because every "vertical" decision below depends on it. Under
X -90 the mat direction -Y becomes world +Z, so the leaf's LEADING edge
(y = -0.855, where the pull handle is) ends up at the top of the rack and the
hinge end (y = -0.135) is the foot. Length along the mat is height up the rack.

`SURF_Rack` is 0.81 x 0.81 m: 0.66 m^2, the largest rectangle on the rig, and
the only one with both axes over half a metre. That is what it is FOR — not
length (the 1.60 m lash line already owns that) but bulk, the wing panel and the
crate that fit nothing else.

Only one rack surface, and not the two the shape invites. Two nets side by side
plainly suggest two surfaces, and it is still the wrong cut: each bay is only
0.32 x 0.64, and a bulky item — the very thing the rack exists for — straddles
the centre post and lies across BOTH nets. The post is a 30 mm divider, not a
wall. Splitting the face would forbid exactly the load the face was added to
take. See `_rack_nets` for what a second id would have to look like if the two
bays ever do need to be addressed separately.

Axes
----
Library frame, +Z up, -Y forward, origin at the bottom centre of the frame's
footprint on the ground.

  -Y   the front. The player stands here, the leaf falls this way, the camera
       looks in from here. Everything that lies flat is at -Y.
  +Y   the back. The frame's hinge line, the standing panel, the kickstands.
  +Z   up, both worn and deployed.

The back panel stands at 65 degrees from the ground. That number is not a
stylistic choice: the panel is 0.62 m long hinged 0.12 m up the frame, and
0.12 + 0.62*sin(65) = 0.682, which is the spec's 0.68 m standing height exactly.

The five moving groups
----------------------
  root          frame, harness, hip belt.
  PIVOT_Back    back panel, its webbing ladders, the oxygen tank and its bands
                and manifold, and both kickstands.
  PIVOT_Leaf    front leaf, its grommet field, the lash rail, and the rack —
                the underside ladder and its two cargo nets.
  PIVOT_Wing_L  left wing and its spine rib.   (likewise _R)
  PIVOT_Lid     the lid apron beyond the leaf's leading edge, its corner
                grommets, the pull handle and both ground stakes. A child of
                PIVOT_Leaf, like the wing pivots: it rides the board and folds
                relative to it.

The lid (2026-08-25)
--------------------
Stowed, the folded rig used to be an open-topped box: leaf in front, wings as
flanks, panel and bedroll behind — and sky above the tank. No fixed piece of
geometry can close that, because the stow maps every candidate carrier's plane
to vertical, so the top had to be the fifth hinge. PIVOT_Lid sits on the leaf's
LEADING edge and its apron is authored deployed like everything else: LID_D
more metres of mat, coplanar with the leaf beyond LEAF_Y0. Folding it X -90
relative to the leaf mid-choreography stands it up as the end wall; riding the
leaf's own -90 it arrives flat on top, capping the box 10 mm above the folded
wing crests. In the RACK pose the same relative -90 turns it into a hood over
the board's top edge. The handle and the stakes moved out to the apron's
leading edge with it — the handle is the lid's pull now, and the stakes still
pin the assembly's front corners through the lid's own corner grommets.

The kickstands hang off PIVOT_Back rather than off a fifth hinge because the
spec names exactly four moving parts. A leg that stows flat against the back of
the panel it props is the right place for it anyway; it simply arrives at the
ground by riding the panel rather than by a hinge of its own.

The lash rail spans the leaf and BOTH wings, so it belongs to no single hinge.
It rides PIVOT_Leaf — the member its midspan is sewn to — and the buckled ends
are what a player would unclip from the wings before folding.

Surfaces
--------
Seven `SURF_*` empties, one rule each: **local +X is the surface's width, +Z is
its depth, +Y points out of the surface.** That is the convention `PackSurface`
assumes, and nothing about breaking it is visible in Blender, so
`dump_surfaces()` asserts it rather than trusting it.

Six of the seven are asserted as authored. `SURF_Rack` is asserted after the
raise, because its face is the leaf's underside — authored, it correctly points
at the sand, and checking it in that pose would fail every rule for the right
reason. `FOLDED` is the one-entry table that says so.

The empties carry no size — they are identity-scaled, because Unity parents
items under them and a scaled parent would rescale every item. The intended
extent of each is in SURFACES below and is printed at build time for whoever
fills in the `PackSurface` inspector.

Every rectangle is an EXACT multiple of PackGrid's 0.090 m cell (2026-08-25),
so the grid fills each face edge to edge with zero hem, and the decorative
stitching, grommet and webbing pitch is 0.180 m = two cells, phase-aligned to
the cell boundaries. Resize a surface only in whole cells, and move its
decoration with it.

`SURF_LongGoods` exists because the 1.35 m LaserStaff fits nothing else. The
biggest open rect is the 0.81 x 0.81 rack, whose diagonal is 1.1455 m, so the
staff does not fit at any yaw. The lash rail is 1.62 x 0.09 across the full
open width; diagonal 1.6225 m, square on with room to spare.

Convexity
---------
Every loft and prism profile here is convex. A C-shaped profile caps into a
concave n-gon that triangulates into overlapping faces on FBX export, which is
why the old pack's carcass is four convex lofts rather than one hollow shell.

    blender --background --python expedition_rig.py -- --out expedition_rig.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Fabric_Canvas_Faded",    # 0  leaf, wings, frame tray, harness webbing
    "Mat_Fabric_Wing_Ochre",      # 1  webbing tape — a shade off the panels
    "Mat_Metal_Steel_Worn",       # 2  frame, kickstands, tank bands, rib
    "Mat_Metal_Brass_Tarnished",  # 3  valve, buckles, grommets
    "Mat_Plastic_Rubber_Black",   # 4  bungee cord, grommet seals, hip pad
    "Mat_Paint_Safety_Orange",    # 5  the oxygen tank
    "Mat_Emissive_Amber",         # 6  the gauge lamp — the only emissive here
    "Mat_Metal_Rust_Heavy",       # 7  kickstand feet, skid pads
]
CANVAS, OCHRE, STEEL, BRASS, RUBBER, ORANGE, AMBER, RUST = range(8)


# ---------------------------------------------------------------------------
# The layout, in one place
# ---------------------------------------------------------------------------

BACK_DEG = 65.0
BACK_RAD = math.radians(BACK_DEG)
BACK_LEN = 0.62               # panel length along its own slope
BACK_TH = 0.09                # panel thickness

BACK_HINGE = Vector((0.0, 0.135, 0.120))     # 0.12 m up the frame's back face
LEAF_HINGE = Vector((0.0, -0.135, 0.016))    # frame's front bottom edge
WING_HINGE_L = Vector((-0.435, -0.545, 0.016))
WING_HINGE_R = Vector((0.435, -0.545, 0.016))

HALF_W = 0.430                # frame and leaf half width

# 2026-08-25: the board was DEEPENED by this much at its leading edge, and
# nothing else moved. Items roughly doubled in size (see ItemScaleLadder.cs) and
# the mat could no longer hold the gear it was drawn for: at 0.50 m deep its
# widest axis-aligned run was 8 cells, so nothing longer than 0.72 m fitted it at
# any yaw. Everything below that ends in `_Y0`, plus the lash rail and the whole
# rack band, is measured from the LEADING edge and therefore carries this term;
# the hinge end is untouched, so the frame, the panel and the fold all still meet
# the board exactly where they did.
#
# 0.200 and not more: the mat wanted 0.70 m, and the deeper rail that was drawn
# up alongside it (0.14 -> 0.27) would have cost a further 0.13 m of board. That
# stops being "a bit taller" on the wearer's back, and the rack's overhang rule
# already takes every long item, so the rail stayed as it is.
LEAF_EXTRA = 0.200

LEAF_Y0, LEAF_Y1 = -0.855 - LEAF_EXTRA, -0.135
WING_Y0, WING_Y1 = -0.845 - LEAF_EXTRA, -0.245
WING_X0, WING_X1 = 0.435, 0.855
CLOTH_T = 0.026               # leaf / wing canvas thickness

# 2026-08-25: the LID — the stowed box's top (see "The lid" in the header). A
# flat apron of the same quilted canvas, hinged on the leaf's leading edge and
# authored DEPLOYED like everything else: coplanar with the mat, LID_D deeper.
# Two 0.180 m quilt panels deep, so its seams keep the mat's two-cell pitch.
LID_D = 0.360
LID_Y0, LID_Y1 = LEAF_Y0 - LID_D, LEAF_Y0
LID_HINGE = Vector((0.0, LEAF_Y0, CLOTH_T))

RAIL_MID = -0.760 - LEAF_EXTRA   # the lash line's centre, and its fittings'
RAIL_Y = (RAIL_MID - 0.045, RAIL_MID + 0.045)   # the two webbing runs
RAIL_HALF = 0.800             # 1.60 m across
RAIL_Z = 0.034

# --- the rack: the leaf's UNDERSIDE ----------------------------------------
#
# The leaf raised on its own hinge is the rack (see "The rack" in the header).
# Everything below hangs off the leaf's underside, so it is under the mat while
# the mat is down and facing the player once it is up.
#
# Every z here is between LADDER_FLOOR and 0. That bound is the whole design
# constraint: the mat rests ON this frame, so nothing may reach further down
# than the runners do or the leaf would not sit flat, and nothing may come
# closer than 6 mm to the canvas underside at z = 0 or `_zverify.py` flags it as
# a coplanar abutment.
RACK_HALF = 0.336             # outer runners, half gauge
RACK_Y0, RACK_Y1 = -0.845 - LEAF_EXTRA, -0.180   # the runners' span along the mat
LADDER_R = 0.018
LADDER_Z = -0.024
RUNG_R = 0.013
RUNG_Z = -0.034
LADDER_FLOOR = -0.054         # the skid pads, and the rig's contact with sand

# THREE runners, not two. The middle one is what the nets' inboard edges lace
# to, and without it "two nets" is one net with a gap down the middle. It is
# thinner than the outer pair and carries no skid pad: it is a lacing post, not
# a foot, and it deliberately stops 12 mm short of the ground plane so the mat
# still rests on the two runners that were sized to carry it.
RACK_POST = 0.015             # centre post radius
RACK_POSTS = (-RACK_HALF, 0.0, RACK_HALF)

# The four rungs became two rails. With the whole face given over to netting a
# mid-span rung has nothing left to do except sit behind a net, so only the two
# cross members the nets actually need survive: a foot rail on the gussets at
# the hinge end and a head rail at the leading edge.
RAIL_FOOT_Y = -0.195          # hinge end     -> the BOTTOM of the raised rack
RAIL_HEAD_Y = -0.830 - LEAF_EXTRA   # leading edge  -> the TOP of the raised rack
RACK_RAILS = (RAIL_FOOT_Y, RAIL_HEAD_Y)

# --- the two nets ----------------------------------------------------------
#
# NET_Z is the cords' REST plane, 21 mm below the runner axis, which puts a cord
# 3 mm clear of the outer runners' surface — laced over them rather than sunk
# into them. Everything the nets own then lives in z -0.050 .. -0.023, inside
# the LADDER_FLOOR .. 0 envelope with 4 mm to spare at the bottom.
#
# The sag is `net()`'s own 0.022 and it runs +Z, INTO the frame, away from the
# player once the rack is up. Two reasons, and they agree:
#
#   * it is what a loaded net does. `SURF_Rack` sits at RACK_FACE, OUTBOARD of
#     the cords, so gear leans on the nets from the player's side and bows them
#     back toward the mat. Sagging the other way would be a net bulging into
#     its own load.
#   * it is the only direction with room. Authored down, -Z is 6 mm of gap
#     before LADDER_FLOOR and then sand; a net sagging that way would hold the
#     mat off the ground, which is the one thing the ladder exists to prevent.
#     Sagging +Z the nets tuck up under the mat with 23 mm of clearance.
#
# So the sag is authored for the RAISED pose — the only pose the rack is usable
# in — and lying down it reads as slack tucked up out of the sand, which is
# also what it should look like there.
NET_Z = -0.045
NET_CORD = 0.006
NET_GAP = 0.016               # cord centre either side of the centre post
NET_COLS, NET_ROWS = 3, 5     # ~0.107 x 0.127 m mesh

# The rack's usable plane, 4 mm proud of the rails so an item lies ON the frame
# rather than inside it, and a shade proud of the netting's deepest knot so a
# load settles onto the cords instead of through them.
RACK_FACE = -0.051

# Band of the mat the rack rectangle covers, as distance from PIVOT_Leaf.
# 9x9 cells exactly (0.810 = 9 x 0.090), y -0.200 .. -1.010: the head skid
# pads stay 5 mm clear, and the foot pads (r 0.025 now) sit 5 mm inside the
# foot edge — the same nominal overlap the old 8x8 rect already accepted.
RACK_MID_Y = -0.505 - LEAF_EXTRA / 2.0
RACK_W, RACK_D = 0.810, 0.610 + LEAF_EXTRA

TANK_S = 0.330                # tank centre along the panel's slope
TANK_R = 0.110
TANK_OFF = 0.125              # tank axis stand-off from the panel face

# Panel frame: X across the panel, U up its slope, N out of its face.
BX = Vector((1.0, 0.0, 0.0))
BU = Vector((0.0, math.cos(BACK_RAD), math.sin(BACK_RAD)))
BN = BX.cross(BU).normalized()
BROT = Matrix((BX, BU, BN)).transposed().to_4x4()        # local Z -> N
BROT_U = Matrix((BX, -BN, BU)).transposed().to_4x4()     # local Z -> U

ROT_UP = (math.pi / 2.0, 0.0, 0.0)                        # +Y -> world +Z
ROT_PANEL = (math.radians(90.0 + BACK_DEG), 0.0, 0.0)     # +Y -> panel normal

# The rack empty's frame, authored in the DEPLOYED (mat-down) pose, so that
# after PIVOT_Leaf turns -90 about X the surface reads the way PackSurface
# demands. XYZ euler, so the matrix is Ry(180) @ Rx(90):
#
#   local +X -> world -X, and after the raise still -X, which is the focus
#               camera's right. uv.x therefore grows rightward on screen.
#   local +Y -> world -Z (into the sand while down), and after the raise -Y,
#               which is straight out of the rack at the player.
#   local +Z -> world -Y (forward along the mat), and after the raise +Z, so
#               uv.y is height up the rack.
#
# Right-handed: X x Y = (-1,0,0) x (0,0,-1) = (0,-1,0) = Z. Getting the
# handedness wrong here mirrors every placement on the rack and nothing in
# Blender shows it, which is why the cross product is written out.
ROT_RACK = (math.pi / 2.0, math.pi, 0.0)

# The raise itself, as a matrix, so `dump_surfaces` can check the rack in the
# pose it is actually used in. Identical to PIVOT_Leaf's stow travel: the rack
# IS the leaf's stow angle, held while the rest of the rig stays open.
RAISE = (Matrix.Translation(LEAF_HINGE)
         @ Matrix.Rotation(math.radians(-90.0), 4, 'X')
         @ Matrix.Translation(-LEAF_HINGE))


def pface(x, s):
    """A point on the panel's FRONT face: x across, s up the slope."""
    return BACK_HINGE + BX * x + BU * s


def tcen(ds=0.0):
    """A point on the oxygen tank's axis, ds along the slope from its centre."""
    return pface(0.0, TANK_S + ds) + BN * TANK_OFF


# name, parent, location, rotation, width (local X), depth (local Z)
#
# Widths and depths are the usable rectangle, inset from the physical panel so
# an item placed at the edge does not overhang it. They are printed at build
# time; nothing in the .blend encodes them, because a scaled empty would rescale
# every item Unity parents under it.
#
# 2026-08-25: every rectangle is an EXACT multiple of PackGrid's 0.090 m cell —
# Back panels 3x6, Leaf 8x8, Wings 4x7, LongGoods 18x1, Rack 9x9 — so the grid
# fills each face with zero hem. The centres moved with the resize; each entry
# says where its rect now runs.
SURFACES = [
    # x 0.150..0.420, s 0.030..0.570 up the slope: clear of the hinge knuckles
    # at the foot, on the loft to within a 3 mm corner sliver at the head.
    ("SURF_Back_L", "back", pface(-0.285, 0.300) + BN * 0.006, ROT_PANEL, 0.270, 0.540),
    ("SURF_Back_R", "back", pface(0.285, 0.300) + BN * 0.006, ROT_PANEL, 0.270, 0.540),
    # y -0.165..-0.885: 26 mm clear of the hinge knuckles, 5 mm clear of the
    # lash rail's near webbing run.
    ("SURF_Leaf", "leaf", Vector((0.0, -0.525, CLOTH_T + 0.005)),
     ROT_UP, 0.720, 0.720),
    # x 0.475..0.835, y -0.275..-0.905: hem-tangent clear at the hinge end,
    # over the rib pads outboard exactly as the old rect already was.
    ("SURF_Wing_L", "wing_l", Vector((-0.655, -0.590, CLOTH_T + 0.005)),
     ROT_UP, 0.360, 0.630),
    ("SURF_Wing_R", "wing_r", Vector((0.655, -0.590, CLOTH_T + 0.005)),
     ROT_UP, 0.360, 0.630),
    # One 0.090 row on RAIL_MID, the keeper line; 1.62 wide runs 10 mm into
    # the flat end-buckle loops, which are the anchors.
    ("SURF_LongGoods", "leaf", Vector((0.0, RAIL_MID, RAIL_Z + 0.016)), ROT_UP, 1.620, 0.090),
    ("SURF_Rack", "leaf", Vector((0.0, RACK_MID_Y, RACK_FACE)), ROT_RACK,
     RACK_W, RACK_D),
]

# Surfaces that only mean anything in a FOLDED configuration: the fold that
# reaches it, and the direction the face must then point. Everything else on
# this rig is authored in the pose it is used in, which is the point of
# authoring open; the rack is the one exception, because its face is the leaf's
# underside and the leaf is authored down.
#
# The expected direction has to be carried here rather than assumed, because the
# unfolded rule — "+Y has an upward component" — is a statement about surfaces
# that lie DOWN, and a vertical rack's face is horizontal by definition. Left on
# the default rule the rack fails the check for being exactly what it should be.
FOLDED = {
    "SURF_Rack": ("PIVOT_Leaf X -90 (the rack raised)", RAISE,
                  Vector((0.0, -1.0, 0.0))),   # straight out at the player
}


# ---------------------------------------------------------------------------
# Helpers
#
# `bent_tube`, `ribbon`, `stitches`, `loop_buckle`, `net` and `round_rect` are
# not in _buildlib; both existing pack scripts carry their own copies and so
# does this one. `loop_buckle` gains a `plane` argument, because this rig has
# buckles lying flat on the ground as well as standing up. `net` is copied
# verbatim from `expedition_backpack.py` — same signature, same hard-coded
# 0.022 sag, same six-chord approximation of the bow — so that the two packs'
# netting is literally the same routine and reads the same in game.
# ---------------------------------------------------------------------------

def round_rect(x0, x1, y0, y1, r, seg=3):
    """A closed CONVEX (x, y) profile with rounded corners."""
    r = min(r, abs(x1 - x0) / 2.0, abs(y1 - y0) / 2.0)
    pts = []
    corners = ((x1 - r, y1 - r, 0.0), (x0 + r, y1 - r, math.pi / 2.0),
               (x0 + r, y0 + r, math.pi), (x1 - r, y0 + r, 3.0 * math.pi / 2.0))
    for cx, cy, base in corners:
        for i in range(seg + 1):
            a = base + (math.pi / 2.0) * i / seg
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def bent_tube(p, pts, r, mat, seg=10, collar=True):
    """A tube following a polyline, with a weld collar at every kink."""
    pts = [Vector(q) for q in pts]
    for a, b in zip(pts, pts[1:]):
        d = b - a
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        p.cyl((a + b) / 2.0, r, d.length, seg=seg, mat=mat, rot=rot)
    if collar:
        for i in range(1, len(pts) - 1):
            d = pts[i + 1] - pts[i - 1]
            rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
            p.cyl(pts[i], r * 1.24, r * 1.5, seg=seg, mat=mat, rot=rot)


def ribbon(p, pts, width, thick, mat, flat='X'):
    """Webbing along a polyline — `width` lies along `flat`, `thick` across it."""
    for a, b in zip(pts, pts[1:]):
        p.seam(a, b, width=thick, depth=width, axis=flat, mat=mat)


def stitches(p, a, b, count, mat, size):
    """A dashed thread run. Cheapest thing that reads as sewn."""
    a, b = Vector(a), Vector(b)
    d = b - a
    for i in range(count):
        p.box(a + d * ((i + 0.5) / count), size, mat)


def loop_buckle(p, c, w, h, t, mat, bar=True, plane='XZ'):
    """A rectangular hardware loop with a centre bar — strap threads through it.

    `plane` picks which way the loop faces: 'XZ' stands up, 'XY' lies flat on
    the ground, which is what the lash rail's end buckles need.
    """
    cx, cy, cz = c
    if plane == 'XZ':
        for sz in (-1, 1):
            p.box((cx, cy, cz + sz * (h / 2 - t / 2)), (w, t, t), mat)
        for sx in (-1, 1):
            p.box((cx + sx * (w / 2 - t / 2), cy, cz), (t, t, h), mat)
    else:
        for sy in (-1, 1):
            p.box((cx, cy + sy * (h / 2 - t / 2), cz), (w, t, t), mat)
        for sx in (-1, 1):
            p.box((cx + sx * (w / 2 - t / 2), cy, cz), (t, h, t), mat)
    if bar:
        p.box((cx, cy, cz), (w - 2 * t, t * 0.7, t * 0.7), mat)


def net(p, corner_a, corner_b, cols, rows, cord, mat, plane='XZ', knots=True):
    """A knotted cargo net across a rectangle, with a slack sag in the middle.

    Straight cords read as a grille. The sag is what makes it cloth: every strand
    bows away from the surface it is stretched over, most at its centre.
    """
    (u0, v0, w0), (u1, v1, w1) = corner_a, corner_b

    def point(fu, fv, bow):
        u = u0 + (u1 - u0) * fu
        v = v0 + (v1 - v0) * fv
        w = w0 + (w1 - w0) * (fu if plane == 'XZ' else fv) + bow
        return (u, w, v) if plane == 'XZ' else (u, v, w)

    sag = 0.022

    for i in range(cols + 1):
        fu = i / cols
        bow = sag * math.sin(math.pi * fu)
        run = [point(fu, j / 6.0, bow * math.sin(math.pi * j / 6.0)) for j in range(7)]
        for a, b in zip(run, run[1:]):
            p.seam(a, b, width=cord, depth=cord,
                   axis='Y' if plane == 'XZ' else 'Z', mat=mat)

    for j in range(rows + 1):
        fv = j / rows
        bow = sag * math.sin(math.pi * fv)
        run = [point(i / 6.0, fv, bow * math.sin(math.pi * i / 6.0)) for i in range(7)]
        for a, b in zip(run, run[1:]):
            p.seam(a, b, width=cord, depth=cord,
                   axis='Z' if plane == 'XZ' else 'Y', mat=mat)

    if knots:
        for i in range(cols + 1):
            for j in range(rows + 1):
                fu, fv = i / cols, j / rows
                bow = sag * math.sin(math.pi * fu) * math.sin(math.pi * fv)
                p.cyl(point(fu, fv, bow), cord * 1.6, cord * 1.6, seg=6, mat=mat)


def pbox(p, x, s, sx, su, sn, mat, off=0.0):
    """A box lying on the tilted back panel.

    `sx` runs across the panel, `su` up its slope, `sn` out through its face;
    `off` is where the box's inner face sits relative to the panel surface, so a
    negative value sinks it in. Sinking rather than abutting is deliberate: two
    coincident faces are exactly what `_zverify.py` flags.
    """
    p.box(pface(x, s) + BN * (off + sn / 2.0), (sx, su, sn), mat, rot=BROT)


def grommet_field(p, x0, x1, y0, y1, nx, ny, z, loops=()):
    """Brass eyelets punched through flat canvas, with the odd webbing loop.

    This is the pack's empty state (spec 3.6): there are no fixed anchors, so
    the base holder is the surface itself. A grommet field is what makes an
    empty mat read as a mat that gear attaches TO.
    """
    for i in range(nx):
        gx = x0 + (x1 - x0) * (i / (nx - 1.0) if nx > 1 else 0.5)
        for j in range(ny):
            gy = y0 + (y1 - y0) * (j / (ny - 1.0) if ny > 1 else 0.5)
            p.tube((gx, gy, z), 0.016, 0.006, 0.022, axis='Z', seg=6, mat=BRASS)
            if (i, j) in loops:
                bent_tube(p, [(gx - 0.030, gy, z + 0.004),
                              (gx - 0.012, gy, z + 0.034),
                              (gx + 0.012, gy, z + 0.034),
                              (gx + 0.030, gy, z + 0.004)],
                          0.008, CANVAS, seg=5, collar=False)


def quilt(p, x0, x1, y0, y1, xs, ys, z, mat=CANVAS):
    """Welted quilt seams across a canvas panel.

    Corners are sorted, because the mirrored wing passes its bounds the other
    way round and an unsorted inset then runs the seams OUT past the canvas edge
    instead of in from it — visible only as the left wing measuring 0.03 m wider
    than the right.
    """
    x0, x1 = min(x0, x1), max(x0, x1)
    y0, y1 = min(y0, y1), max(y0, y1)
    for gx in xs:
        p.seam((gx, y0 + 0.02, z), (gx, y1 - 0.02, z),
               width=0.016, depth=0.016, axis='Z', mat=mat)
    for gy in ys:
        p.seam((x0 + 0.02, gy, z), (x1 - 0.02, gy, z),
               width=0.016, depth=0.016, axis='Z', mat=mat)


def hem(p, x0, x1, y0, y1, z, r=0.013, mat=CANVAS):
    """The rolled hem around a piece of canvas — what stops it reading as card."""
    # Closed at a mid-edge, not at a corner, so bent_tube's own collars land on
    # all four corners. A corner puck built as an upright cylinder instead puts
    # its flat cap within a hair of the canvas's own top face — three of the
    # clashes _zverify.py found on the first pass were exactly that.
    mx = (x0 + x1) / 2.0
    ring = [(mx, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z), (x0, y0, z),
            (mx, y0, z)]
    bent_tube(p, ring, r, mat, seg=6, collar=True)


def empty(name, loc, coll, rot=(0.0, 0.0, 0.0), size=0.06):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = 'ARROWS'
    obj.empty_display_size = size
    obj.location = Vector(loc)
    obj.rotation_euler = rot
    coll.objects.link(obj)
    return obj


def attach(child, parent, parent_world):
    """Parent with a clean local transform.

    Blender's parent-inverse would hold the child in place while hiding the
    offset in a matrix the FBX flattens away; setting the local location against
    a known parent origin keeps the hierarchy readable on Unity's side. Only
    valid because every parent here is an unrotated translation — which every
    PIVOT_* is, by construction.
    """
    child.parent = parent
    child.matrix_parent_inverse = Matrix.Identity(4)
    child.location = Vector(child.location) - Vector(parent_world)
    return child


# ---------------------------------------------------------------------------
# One object per part
# ---------------------------------------------------------------------------

PARTS = []


def part(name, origin=(0.0, 0.0, 0.0), parent=None, bevel=0.005, seg=1):
    """Register a part builder.

    `parent` is 'back', 'leaf', 'wing_l', 'wing_r', or None for static.

    `seg` is bevel segments and defaults to 1 — a chamfer, not a round. This rig
    is a field of small hardware seen from 1.9 m away through a 40 degree lens;
    a two-segment bevel on every eyelet and buckle costs more triangles than
    every mesh in the model put together and is invisible at that distance.
    """
    def wrap(fn):
        PARTS.append((name, fn, Vector(origin), parent, bevel, seg))
        return fn
    return wrap


# --- the frame ------------------------------------------------------------

@part("Mesh_Rig_Frame")
def _frame(p):
    """The base everything hinges off: a steel tube chassis with a canvas tray.

    Open at the BACK on purpose. The panel hinges there and sweeps 25 degrees
    through that space on its way to vertical, and the oxygen tank's lower end
    hangs over it — a back top rail would be inside the tank.
    """
    p.slab((-0.392, -0.108, 0.030), (0.392, 0.108, 0.062), CANVAS)

    loop = [(-0.410, -0.112, 0.030), (0.410, -0.112, 0.030),
            (0.410, 0.112, 0.030), (-0.410, 0.112, 0.030), (-0.410, -0.112, 0.030)]
    bent_tube(p, loop, 0.018, STEEL, seg=8, collar=False)
    for c in loop[:-1]:
        p.cyl(c, 0.022, 0.030, axis='Z', seg=8, mat=STEEL)

    for sx in (-1, 1):
        bent_tube(p, [(sx * 0.410, -0.112, 0.030), (sx * 0.410, -0.112, 0.200)],
                  0.016, STEEL, seg=8, collar=False)
        bent_tube(p, [(sx * 0.410, 0.112, 0.030), (sx * 0.410, 0.112, 0.130)],
                  0.016, STEEL, seg=8, collar=False)
        bent_tube(p, [(sx * 0.410, -0.112, 0.200), (sx * 0.410, 0.112, 0.130)],
                  0.015, STEEL, seg=8, collar=False)
    bent_tube(p, [(-0.410, -0.112, 0.200), (0.410, -0.112, 0.200)],
              0.016, STEEL, seg=8, collar=False)

    # Hinge knuckles. These are the physical PIVOT_Back and PIVOT_Leaf lines.
    for cx in (-0.300, 0.0, 0.300):
        p.cyl((cx, BACK_HINGE.y, BACK_HINGE.z), 0.026, 0.100, axis='X', seg=10,
              mat=STEEL)
        p.cyl((cx, LEAF_HINGE.y, LEAF_HINGE.z), 0.022, 0.100, axis='X', seg=10,
              mat=STEEL)

    for sx in (-1, 1):
        for gy in (-0.080, 0.080):
            p.cyl((sx * 0.360, gy, 0.014), 0.044, 0.028, axis='Z', seg=8, mat=RUST)


def _hipbelt(sx):
    """The wing that carries the weight when the rig is worn.

    Canvas webbing over a rubber pad rather than a slab of rubber: a black belt
    at this size reads as a moulded handle bolted to the frame, and the frame is
    already the busiest silhouette in the deployed rig.
    """
    def build(p, sx=sx):
        path = [(sx * 0.386, 0.058, 0.118), (sx * 0.452, 0.020, 0.112),
                (sx * 0.496, -0.066, 0.098)]
        ribbon(p, path, 0.104, 0.030, CANVAS, flat='Z')
        ribbon(p, path[:2], 0.078, 0.018, RUBBER, flat='Z')
        loop_buckle(p, (sx * 0.474, -0.036, 0.104), 0.062, 0.078, 0.012, BRASS,
                    plane='XZ')
    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    part("Mesh_Rig_HipBelt_" + _side,
         origin=(_sx * 0.386, 0.058, 0.118))(_hipbelt(_sx))


# --- the back panel -------------------------------------------------------

@part("Mesh_Rig_BackPanel", origin=BACK_HINGE, parent="back")
def _backpanel(p):
    """The panel that stays standing at 65 degrees.

    A lofted parallelogram in the panel's own YZ section, swept across X with
    the thickness swelling toward the middle so the edges read as a hem rather
    than as the end of a plank. The profile is a convex quad at every station.
    """
    def prof(tf, lf):
        s0 = BACK_LEN * (1.0 - lf) * 0.35
        s1 = BACK_LEN * lf
        th = BACK_TH * tf
        a = BACK_HINGE + BU * s0
        b = BACK_HINGE + BU * s1
        pts = (a, b, b - BN * th, a - BN * th)
        return [(q.y, q.z) for q in pts]

    p.loft([(x, prof(tf, lf)) for x, tf, lf in
            ((-0.430, 0.26, 0.88), (-0.394, 0.90, 0.99), (-0.150, 1.00, 1.00),
             (0.150, 1.00, 1.00), (0.394, 0.90, 0.99), (0.430, 0.26, 0.88))],
           axis='X', mat=CANVAS, cap=True)

    # Rolled hem along the head and both flanks.
    top = pface(0.0, BACK_LEN - 0.012) - BN * 0.030
    bent_tube(p, [(-0.404, top.y, top.z), (0.404, top.y, top.z)],
              0.024, CANVAS, seg=8, collar=False)
    for sx in (-1, 1):
        lo = pface(sx * 0.408, 0.030) - BN * 0.036
        hi = pface(sx * 0.408, BACK_LEN - 0.030) - BN * 0.036
        bent_tube(p, [tuple(lo), tuple(hi)], 0.022, CANVAS, seg=8, collar=False)

    # Two brass lash eyelets per flank, for the wings' tie-downs when stowed.
    for sx in (-1, 1):
        for s in (0.180, 0.440):
            p.tube(pface(sx * 0.396, s) - BN * 0.006, 0.018, 0.006, 0.020,
                   axis='Z', seg=8, mat=BRASS, )


def _back_webbing(sx):
    """The webbing-ladder field either side of the tank.

    This is the panel's base holder (spec 3.6): free placement means there is
    nowhere to put a bare empty holder, so the surface itself is the holder and
    what an empty pack shows is this ladder.
    """
    def build(p, sx=sx):
        # The ladder IS the grid (2026-08-25): verticals ON the rect's outer
        # cell columns (x 0.150 / 0.420), rungs on the six ROW CENTRES at the
        # cell's own 0.090 pitch spanning tape to tape, eyelets on row
        # boundaries. su covers the full 0.540 rect depth.
        for x in (0.150, 0.420):
            pbox(p, sx * x, 0.300, 0.052, 0.540, 0.018, OCHRE, off=-0.010)
        for s in (0.075, 0.165, 0.255, 0.345, 0.435, 0.525):
            pbox(p, sx * 0.285, s, 0.322, 0.036, 0.026, OCHRE, off=-0.002)
        for s in (0.120, 0.390):
            p.tube(pface(sx * 0.285, s) + BN * 0.020, 0.020, 0.007, 0.014,
                   axis='Z', seg=6, mat=BRASS)
    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    part("Mesh_Rig_BackWebbing_" + _side,
         origin=pface(_sx * 0.285, 0.300),
         parent="back")(_back_webbing(_sx))


def _harness(sx):
    """One shoulder strap, on the panel's BACK face — the wearer's side."""
    def build(p, sx=sx):
        path = [tuple(pface(sx * 0.120, BACK_LEN - 0.040) - BN * 0.100),
                tuple(pface(sx * 0.190, 0.430) - BN * 0.170),
                tuple(pface(sx * 0.255, 0.230) - BN * 0.205),
                tuple(pface(sx * 0.300, 0.030) - BN * 0.120)]
        ribbon(p, path, 0.108, 0.026, CANVAS, flat='X')
        pad = [tuple(Vector(q) - BN * 0.024) for q in path[0:3]]
        ribbon(p, pad, 0.132, 0.022, RUBBER, flat='X')
        for a, b in zip(pad, pad[1:]):
            for sw in (-1, 1):
                stitches(p, (a[0] + sw * 0.058, a[1], a[2]),
                         (b[0] + sw * 0.058, b[1], b[2]), 3, CANVAS,
                         (0.009, 0.007, 0.018))
        loop_buckle(p, tuple(pface(sx * 0.278, 0.150) - BN * 0.190),
                    0.108, 0.062, 0.014, BRASS, plane='XZ')
    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    part("Mesh_Rig_Harness_" + _side,
         origin=pface(_sx * 0.120, BACK_LEN - 0.040) - BN * 0.100,
         parent="back")(_harness(_sx))


# --- the oxygen tank ------------------------------------------------------

@part("Mesh_Rig_OxygenTank", origin=tcen(), parent="back")
def _tank(p):
    """A fixed fitting, not an item.

    The composition's landmark: it is in the same place every single time the
    pack opens, which is what lets a player orient instantly.
    """
    p.cyl(tcen(), TANK_R, 0.440, axis='Z', seg=18, mat=ORANGE, rot=BROT_U)
    p.cyl(tcen(0.240), TANK_R, 0.040, axis='Z', seg=18, mat=ORANGE,
          rot=BROT_U, radius_top=0.072)
    p.cyl(tcen(-0.240), 0.072, 0.040, axis='Z', seg=18, mat=ORANGE,
          rot=BROT_U, radius_top=TANK_R)
    # A painted collar band, so the bottle is not one flat orange tube.
    p.cyl(tcen(0.190), TANK_R + 0.002, 0.026, axis='Z', seg=18, mat=CANVAS,
          rot=BROT_U)


@part("Mesh_Rig_OxygenTank_Bands", origin=tcen(), parent="back")
def _tank_bands(p):
    """Two over-centre steel bands and the cradle feet bolting them down.

    The only hard mechanism on the panel, which is what makes the webbing
    either side of it read as soft by contrast.
    """
    for ds in (-0.150, 0.150):
        p.cyl(tcen(ds), TANK_R + 0.010, 0.034, axis='Z', seg=18, mat=STEEL,
              rot=BROT_U)
        c = tcen(ds) + BN * (TANK_R + 0.014)
        p.box(c, (0.062, 0.062, 0.026), STEEL, rot=BROT)
        p.box(c + BN * 0.020, (0.030, 0.088, 0.016), BRASS, rot=BROT)
        for sx in (-1, 1):
            # 0.124, not 0.128 (2026-08-25): the grown back rects reach in to
            # x 0.150, and at 0.128 the posts' outer faces sat flush on that
            # line. 4 mm of air now. The flange sinks to crest +0.004, under
            # the rect plane's +0.006 — and its INNER face sits at -0.016, not
            # the webbing tapes' -0.010: the tapes moved onto x 0.150 and two
            # same-facing faces on one plane is exactly what _zverify.py flags.
            pbox(p, sx * 0.124, TANK_S + ds, 0.044, 0.052, 0.124, STEEL, off=0.0)
            pbox(p, sx * 0.124, TANK_S + ds, 0.076, 0.076, 0.020, STEEL, off=-0.016)


@part("Mesh_Rig_OxygenTank_Manifold", origin=pface(0.0, 0.040) + BN * 0.125,
      parent="back")
def _tank_manifold(p):
    """Brass valve block under the bottle, carrying the one amber gauge lamp.

    The lamp is the only emissive in the whole rig, so it is the warm focal
    point the eye returns to. It sits LOW and central rather than on top of the
    bottle: at the focus camera's 38 degree pitch that is nearer the middle of
    frame, and it keeps the rig's standing height down.
    """
    p.cyl(tcen(-0.272), 0.046, 0.048, axis='Z', seg=12, mat=BRASS, rot=BROT_U)
    base = pface(0.0, 0.040) + BN * 0.125
    p.box(base, (0.176, 0.078, 0.086), BRASS, rot=BROT)
    for sx in (-1, 1):
        p.cyl(base + BX * (sx * 0.104), 0.026, 0.040, axis='Z', seg=10,
              mat=BRASS, rot=BROT_U)

    # ONE amber lamp in the whole rig. A second warm point anywhere would stop
    # this being the thing the eye returns to, which is its entire job.
    gauge = base + BN * 0.050
    p.cyl(gauge, 0.044, 0.028, axis='Z', seg=14, mat=BRASS, rot=BROT)
    p.cyl(gauge + BN * 0.017, 0.035, 0.012, axis='Z', seg=14, mat=AMBER, rot=BROT)
    for sx in (-1, 1):
        p.cyl(base + BX * (sx * 0.062) + BN * 0.044, 0.012, 0.016, axis='Z',
              seg=8, mat=BRASS, rot=BROT)

    bent_tube(p, [tuple(base + BX * 0.104 + BU * 0.024),
                  tuple(base + BX * 0.180 + BU * 0.010 - BN * 0.020),
                  tuple(base + BX * 0.230 - BU * 0.060 - BN * 0.050)],
              0.014, RUBBER, seg=8, collar=False)


# --- the kickstands -------------------------------------------------------

def _kickstand(sx):
    """The visible reason the panel sits at 65 degrees rather than falling over.

    A panel that holds an angle with nothing holding it reads as a UI element
    that happens to be in 3D. Splayed outward as well as back: a leg raked
    straight back would put its foot further behind the rig than the panel's own
    head, and the open footprint is already the tightest dimension here.
    """
    def build(p, sx=sx):
        top = pface(sx * 0.300, 0.400) - BN * BACK_TH
        foot = Vector((sx * 0.500, 0.455, 0.028))
        bent_tube(p, [tuple(top), tuple(foot)], 0.019, STEEL, seg=8, collar=False)
        p.cyl(tuple(top), 0.029, 0.054, axis='X', seg=10, mat=STEEL)
        p.cyl((foot.x, foot.y, 0.014), 0.046, 0.030, axis='Z', seg=10, mat=RUST)
        mid = top.lerp(foot, 0.46)
        p.box(tuple(mid), (0.038, 0.034, 0.034), RUST)
    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    part("Mesh_Rig_Kickstand_" + _side,
         origin=pface(_sx * 0.300, 0.400) - BN * BACK_TH,
         parent="back")(_kickstand(_sx))


# --- the front leaf -------------------------------------------------------

@part("Mesh_Rig_FrontLeaf", origin=LEAF_HINGE, parent="leaf")
def _leaf(p):
    """Quilted stiffened canvas that falls forward onto the ground."""
    p.slab((-HALF_W, LEAF_Y0, 0.0), (HALF_W, LEAF_Y1, CLOTH_T), CANVAS)
    hem(p, -0.416, 0.416, LEAF_Y0 + 0.014, LEAF_Y1 - 0.014, 0.024, r=0.013)
    # Quilt lines on cell boundaries (0.180 pitch = two cells), interleaved
    # with the grommet rows.
    quilt(p, -HALF_W, HALF_W, LEAF_Y0, LEAF_Y1,
          (-0.180, 0.0, 0.180), (-0.345, -0.525, -0.705, -0.885), CLOTH_T)
    for cx in (-0.300, 0.0, 0.300):
        p.cyl((cx, LEAF_Y1 - 0.004, 0.018), 0.014, 0.096, axis='X', seg=10,
              mat=STEEL)


@part("Mesh_Rig_LeafGrommets", origin=(0.0, -0.525, CLOTH_T),
      parent="leaf",
      bevel=0.0)
def _leaf_grommets(p):
    """The grommet-and-loop field: the leaf's base holder, and its rear corners.

    The field sits ON the cell grid (2026-08-25): 0.180 pitch both ways, every
    eyelet on a cell boundary. The LEADING corner pair moved out to the lid —
    the assembly's leading edge is the lid's now (see Mesh_Rig_LidGrommets).
    """
    grommet_field(p, -0.270, 0.270, -0.255, -0.795, 4, 4, CLOTH_T,
                  loops={(0, 0), (3, 0), (1, 2), (2, 1)})
    for sx in (-1, 1):
        gy = LEAF_Y1 + 0.055
        p.tube((sx * 0.372, gy, CLOTH_T), 0.024, 0.008, 0.026, axis='Z',
               seg=10, mat=BRASS)
        p.tube((sx * 0.372, gy, CLOTH_T - 0.004), 0.030, 0.010, 0.014,
               axis='Z', seg=8, mat=RUBBER)


@part("Mesh_Rig_LashRail", origin=(0.0, RAIL_MID, RAIL_Z), parent="leaf")
def _lash_rail(p):
    """The lash line for long tools, across the full open width.

    The only surface a 1.35 m LaserStaff fits: the open faces top out at a
    1.1216 m diagonal, so the staff fits none of them at any yaw. 1.60 m of
    webbing across the leaf and both wings takes it square on.
    """
    for gy in RAIL_Y:
        p.seam((-RAIL_HALF, gy, RAIL_Z), (RAIL_HALF, gy, RAIL_Z),
               width=0.050, depth=0.020, axis='Z', mat=OCHRE)

    for sx in (-1, 1):
        loop_buckle(p, (sx * 0.775, RAIL_MID, RAIL_Z + 0.010), 0.086, 0.130, 0.014,
                    BRASS, plane='XY')
        for gy in RAIL_Y:
            p.seam((sx * 0.700, gy, RAIL_Z), (sx * 0.828, RAIL_MID, RAIL_Z + 0.008),
                   width=0.042, depth=0.016, axis='Z', mat=OCHRE)
        p.tube((sx * 0.830, RAIL_MID, CLOTH_T), 0.020, 0.007, 0.026, axis='Z',
               seg=8, mat=BRASS)

    # Keepers: short tabs pinching both runs down onto the canvas, so the rail
    # reads as lashed to the mat rather than hovering over it.
    # On cell boundaries: three and six cells out from the mat's centre line.
    for cx in (-0.540, -0.270, 0.270, 0.540):
        p.box((cx, RAIL_MID, RAIL_Z + 0.006), (0.048, 0.128, 0.018), CANVAS)
        p.tube((cx, RAIL_MID, CLOTH_T + 0.002), 0.015, 0.006, 0.024, axis='Z',
               seg=8, mat=BRASS)


# --- the rack (the leaf's underside) ---------------------------------------

@part("Mesh_Rig_RackLadder", origin=(0.0, RACK_MID_Y, LADDER_Z), parent="leaf")
def _rack_ladder(p):
    """The ladder frame the mat lies on, and the rack's structure once it is up.

    Two things at once, and that is why it is here rather than being invented as
    a separate fold-out part. Lying down it is what keeps 0.62 m^2 of quilted
    canvas flat and clear of rocky ground — a mat this size needs runners, and
    the leaf had none. Raised it is the border the two cargo nets are stretched
    in: three vertical posts and two cross rails, which is exactly the frame a
    pair of nets needs and nothing more.

    The frame SURVIVED the move from hooks to nets, and that is the one thing
    worth saying here. A net is not a self-supporting object; it is a membrane
    that has to be laced to something along all four edges. Deleting the ladder
    with the horns would have left two nets stapled to the back of a sheet of
    canvas. What did NOT survive is the two mid-span rungs: they existed to hang
    gear off, the nets do that job now, and a rung behind a net is clutter you
    can see through.

    Nothing here reaches below LADDER_FLOOR, because the runners ARE the ground
    contact: anything deeper would stop the mat sitting flat.
    """
    for sx in (-1, 1):
        bent_tube(p, [(sx * RACK_HALF, RACK_Y1, LADDER_Z),
                      (sx * RACK_HALF, RACK_Y0, LADDER_Z)],
                  LADDER_R, STEEL, seg=8, collar=False)

        # The gusset at the hinge end. The runner takes the whole rack's load in
        # bending once the leaf is up, and it is carried by the leaf's own hinge
        # knuckles, so the joint has to be visibly stiffened.
        p.box((sx * RACK_HALF, RACK_Y1 - 0.014, LADDER_Z + 0.006),
              (0.048, 0.056, 0.028), STEEL)

        for gy in (RACK_Y1, RACK_Y0):
            # r 0.025 (was 0.030, 2026-08-25): the 9x9 rack rect reaches the
            # pads now, and the smaller foot keeps the overlap at the 5 mm the
            # 8x8 rect already accepted.
            p.cyl((sx * RACK_HALF, gy, LADDER_FLOOR + 0.008), 0.025, 0.016,
                  axis='Z', seg=8, mat=RUST)

        # Webbing tape down the inboard face of each runner: the one soft line
        # in an otherwise all-steel frame, and what stops the rack reading as a
        # different object bolted to a canvas rig.
        p.seam((sx * 0.310, RACK_Y1 + 0.020, -0.012),
               (sx * 0.310, RACK_Y0 - 0.020, -0.012),
               width=0.030, depth=0.012, axis='Z', mat=OCHRE)

    # The centre lacing post. Thinner stock and no skid pad — see RACK_POST.
    bent_tube(p, [(0.0, RACK_Y1, LADDER_Z), (0.0, RACK_Y0, LADDER_Z)],
              RACK_POST, STEEL, seg=8, collar=False)

    # Head and foot rails, running post to post across all three. No clamp boss
    # where they cross a post: at RUNG_Z the rail's own surface already reaches
    # past the post's, so the crossing reads welded, and a boss added there
    # lands its flat cap within 2 mm of the rail's bottom facet — which is
    # precisely the coplanar abutment `_zverify.py` exists to catch.
    for gy in RACK_RAILS:
        bent_tube(p, [(-RACK_HALF, gy, RUNG_Z), (RACK_HALF, gy, RUNG_Z)],
                  RUNG_R, STEEL, seg=8, collar=False)


@part("Mesh_Rig_RackNets", origin=(0.0, RACK_MID_Y, NET_Z), parent="leaf",
      bevel=0.0)
def _rack_nets(p):
    """Two knotted cargo nets, side by side, filling the rack's whole face.

    This replaces the three pairs of cradle horns the first rack shipped with.
    Horns hold ONE thing each and only if it is the right shape — a spar, a
    cane, something long and straight laid in the crook. Netting holds whatever
    you push into it, which is the honest answer for the face that exists to
    take bulk. It is also the answer that survives an empty rack: three bare
    brass horns read as an unfinished fitting, a taut net reads as ready.

    Two nets rather than one, and the divider is the point rather than a
    compromise. A single 0.67 m net over that span would need cords long enough
    to belly out under any real load, and it would leave the centre of the board
    with nothing to lace to. The centre post is 30 mm of steel; anything wide
    enough to care simply lies across both bays.

    Vertical is the pose this is authored for. Length along the mat becomes
    height up the rack, so each net runs from the foot rail at the hinge end to
    the head rail at the leading edge — the full board — and the two of them sit
    side by side across it. See NET_Z for why the sag goes the way it does.

    If the two bays ever DO need to be addressed separately, the second surface
    is already implied by the geometry and would be:

        SURF_Rack_L   parent leaf   loc (-0.176, RACK_MID_Y, RACK_FACE)
        SURF_Rack_R   parent leaf   loc ( 0.176, RACK_MID_Y, RACK_FACE)
        ROT_RACK on both, 0.30 x 0.60 m each

    i.e. `SURF_Rack` split down x = 0 with its declared width narrowed from 0.80
    to the bay gauge. That is a NEW `PackSurfaceId` — it must be appended, never
    swapped in over the existing `Rack`, because the seven ids are load-bearing
    and renumbering them silently re-points every saved placement. It is not
    added here: see the header for why one surface is still the right cut.
    """
    for x0, x1 in ((-RACK_HALF, -NET_GAP), (NET_GAP, RACK_HALF)):
        # The outboard edge cord lands on the runner's axis, 3 mm outside its
        # surface at this z, so it reads as laced OVER the runner; the inboard
        # one clears the centre post by a millimetre. The two nets share no
        # geometry, which is deliberate — an edge cord drawn twice in the same
        # place is a z-fight, not a stronger net.
        net(p, (x0, RAIL_FOOT_Y, NET_Z), (x1, RAIL_HEAD_Y, NET_Z),
            NET_COLS, NET_ROWS, NET_CORD, RUBBER, plane='XY')


@part("Mesh_Rig_RackHandle", origin=(0.0, LID_Y0 - 0.015, 0.030), parent="lid")
def _rack_handle(p):
    """The pull loop on the assembly's leading edge: how you know the mat lifts.

    The whole rack is under the mat while the mat is down, so without something
    on the visible side there is nothing at all to say the board does anything
    but lie there. This is the one part of the feature that reads from the
    focus camera in the deployed pose, and it becomes the grab handle at the top
    of the rack once it is up.

    On the LID since 2026-08-25 — and back on the true leading edge: the loop
    was hardcoded at the pre-LEAF_EXTRA edge and had sat mid-board over the
    lash rail's near webbing run since the deepening. Authored against LID_Y0
    now, so the next edge move carries it automatically.
    """
    loop = [(-0.092, LID_Y0 - 0.001, 0.028), (-0.064, LID_Y0 - 0.037, 0.050),
            (0.064, LID_Y0 - 0.037, 0.050), (0.092, LID_Y0 - 0.001, 0.028)]
    bent_tube(p, loop, 0.011, OCHRE, seg=6, collar=True)

    for sx in (-1, 1):
        p.tube((sx * 0.092, LID_Y0 + 0.005, 0.026), 0.019, 0.007, 0.022,
               axis='Y', seg=8, mat=BRASS)


# --- the lid (the stowed box's top) -----------------------------------------

@part("Mesh_Rig_Lid", origin=LID_HINGE, parent="lid")
def _lid(p):
    """The apron that closes the stowed box — see "The lid" in the header.

    Deployed it is simply LID_D more metres of mat past the leaf's leading
    edge: same slab, same rolled hem, same quilt language, two 0.180 m quilt
    panels deep so the seams keep the mat's two-cell pitch. Its slab ends
    EXACTLY on LEAF_Y0 — the leaf's end face and the lid's are opposed there,
    which _zverify.py correctly reads as an occluded joint, and the two
    chamfered edges groove into the hinge crease. Three steel knuckles
    straddle the seam the way the leaf's own knuckles straddle its hinge line.
    """
    p.slab((-HALF_W, LID_Y0, 0.0), (HALF_W, LID_Y1, CLOTH_T), CANVAS)
    hem(p, -0.416, 0.416, LID_Y0 + 0.014, LID_Y1 - 0.014, 0.024, r=0.013)
    quilt(p, -HALF_W, HALF_W, LID_Y0, LID_Y1,
          (-0.180, 0.0, 0.180), (-1.235,), CLOTH_T)
    for cx in (-0.300, 0.0, 0.300):
        p.cyl((cx, LID_Y1 - 0.004, 0.018), 0.012, 0.096, axis='X', seg=10,
              mat=STEEL)


@part("Mesh_Rig_LidGrommets", origin=(0.0, LID_Y0 - 0.055, CLOTH_T),
      parent="lid", bevel=0.0)
def _lid_grommets(p):
    """The assembly's leading corner grommets, carried out from the leaf.

    Same brass-plus-washer pair the leaf's rear corners wear. The stakes' guy
    cords tie to these, so they had to travel with the edge or the cords would
    have stretched by LID_D — the exact failure the deepening already hit once
    with LEAF_EXTRA.
    """
    for sx in (-1, 1):
        p.tube((sx * 0.372, LID_Y0 - 0.055, CLOTH_T), 0.024, 0.008, 0.026,
               axis='Z', seg=10, mat=BRASS)
        p.tube((sx * 0.372, LID_Y0 - 0.055, CLOTH_T - 0.004), 0.030, 0.010,
               0.014, axis='Z', seg=8, mat=RUBBER)


# --- the wings ------------------------------------------------------------

def _wing(sx):
    """Same canvas as the leaf, hinged on the leaf's outer edge."""
    def build(p, sx=sx):
        p.slab((sx * WING_X0, WING_Y0, 0.0), (sx * WING_X1, WING_Y1, CLOTH_T),
               CANVAS)
        hem(p, sx * (WING_X0 + 0.014), sx * (WING_X1 - 0.014),
            WING_Y0 + 0.014, WING_Y1 - 0.014, 0.024, r=0.013)
        quilt(p, sx * WING_X0, sx * WING_X1, WING_Y0, WING_Y1,
              (sx * 0.645,), (-0.395, -0.695, -0.995), CLOTH_T)
        grommet_field(p, sx * 0.520, sx * 0.780, -0.310, -0.850, 2, 4, CLOTH_T,
                      loops={(0, 0), (1, 2)})
        for gy in (-0.975, -0.760, -0.545, -0.330):
            p.cyl((sx * 0.437, gy, 0.018), 0.014, 0.088, axis='Y', seg=10,
                  mat=STEEL)
    return build


def _wing_rib(sx):
    """The stiffened spine rib along the outer edge.

    Without it a wing reads as limp cloth that should be rippling, and the
    silhouette of the open rig goes soft exactly where it needs to be crisp.
    """
    def build(p, sx=sx):
        a = (sx * 0.842, WING_Y0 + 0.020, 0.020)
        b = (sx * 0.842, WING_Y1 - 0.020, 0.020)
        bent_tube(p, [a, b], 0.013, STEEL, seg=8, collar=False)
        p.seam(a, b, width=0.044, depth=0.020, axis='Z', mat=OCHRE)
        for c in (a, b):
            p.cyl(c, 0.020, 0.034, axis='Y', seg=8, mat=STEEL)
        for gy in (-0.400, -0.690, -0.980):
            p.box((sx * 0.800, gy, 0.024), (0.100, 0.038, 0.026), OCHRE)
    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    _hinge = WING_HINGE_L if _sx < 0 else WING_HINGE_R
    part("Mesh_Rig_Wing_" + _side, origin=_hinge,
         parent="wing_" + _side.lower())(_wing(_sx))
    part("Mesh_Rig_WingRib_" + _side, origin=(_sx * 0.842, -0.545, 0.020),
         parent="wing_" + _side.lower())(_wing_rib(_sx))


# --- the stakes -----------------------------------------------------------

def _stake(sx):
    """Pins the assembly's front corner to the ground.

    Not decoration: it is the answer to "why is this mat lying flat on rocky
    ground". It RIDES the lid (2026-08-25; a 2026-08-24 hand edit had it riding
    PIVOT_Leaf before that): its cord and grommet are one rigid mesh with it,
    so the moment the board moved without it the pair read as debris on the
    sand. Stowed, the two lie lashed across the lid's rear corners.
    """
    def build(p, sx=sx):
        # Pinned to the LID's leading corner, so the stake travels with the
        # assembly's true edge rather than staying put and paying for a deeper
        # board in cord: authored against LID_Y0, the guy line keeps the slack
        # it was drawn with instead of stretching by LID_D — the same trap the
        # deepening hit and fixed once already with LEAF_EXTRA.
        head = Vector((sx * 0.492, LID_Y0 - 0.031, 0.062))
        tip = Vector((sx * 0.452, LID_Y0 + 0.009, -0.078))
        bent_tube(p, [tuple(head), tuple(tip)], 0.011, STEEL, seg=6, collar=False)
        p.box(tuple(head + Vector((0.0, 0.0, 0.008))), (0.026, 0.046, 0.016),
              STEEL)
        p.cyl(tuple(head), 0.019, 0.012, axis='Z', seg=8, mat=RUST)
        g = Vector((sx * 0.372, LID_Y0 - 0.055, CLOTH_T + 0.010))
        mid = (head + g) / 2.0 + Vector((0.0, 0.0, 0.026))
        bent_tube(p, [tuple(head), tuple(mid), tuple(g)], 0.007, RUBBER, seg=6,
                  collar=False)
    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    part("Mesh_Rig_Stake_" + _side,
         origin=(_sx * 0.492, LID_Y0 - 0.031, 0.062),
         parent="lid")(_stake(_sx))


# ---------------------------------------------------------------------------
# Verification
# ---------------------------------------------------------------------------

def dump_surfaces():
    """Print and ASSERT every surface's axis convention, and the fold states.

    `PackSurface` assumes local +X is the width, +Z the depth and +Y the
    outward normal. Nothing about breaking that is visible in Blender — every
    item simply lies on its face in game — so it is checked here.
    """
    bpy.context.view_layer.update()
    sizes = {n: (w, d) for n, _, _, _, w, d in SURFACES}
    bad = []

    print("  --- surfaces (deployed, every pivot at zero) ---")
    for obj in sorted(bpy.data.objects, key=lambda o: o.name):
        if not obj.name.startswith("SURF_"):
            continue
        # A folded surface is checked in the pose it is USED in, not the pose it
        # is authored in. SURF_Rack's face is the leaf's underside, so authored
        # it points at the sand and every assert below would fail on it — which
        # would be the check misfiring, not the model being wrong.
        fold = FOLDED.get(obj.name)
        m = (fold[1] @ obj.matrix_world) if fold else obj.matrix_world
        loc = m.to_translation()
        b = m.to_3x3()
        ax = (b @ Vector((1, 0, 0))).normalized()
        ay = (b @ Vector((0, 1, 0))).normalized()
        az = (b @ Vector((0, 0, 1))).normalized()
        w, d = sizes[obj.name]
        print("    %-15s pos=(%6.3f,%6.3f,%6.3f)  %.2fx%.2f m  "
              "out=(%5.2f,%5.2f,%5.2f) width=(%5.2f,%5.2f,%5.2f) "
              "depth=(%5.2f,%5.2f,%5.2f)%s"
              % (obj.name, loc.x, loc.y, loc.z, w, d,
                 ay.x, ay.y, ay.z, ax.x, ax.y, ax.z, az.x, az.y, az.z,
                 ("   [measured at " + fold[0] + "]") if fold else ""))
        if abs(obj.scale.x - 1.0) + abs(obj.scale.y - 1.0) + abs(obj.scale.z - 1.0) > 1e-6:
            bad.append("%s is not identity-scaled" % obj.name)
        if fold:
            # A folded surface knows exactly which way it must face, so it is
            # held to that rather than to the loose "somewhat upward" rule the
            # flat and reclined faces share.
            if ay.dot(fold[2]) < 0.95:
                bad.append("%s: +Y is (%.2f,%.2f,%.2f), not the %s it must "
                           "point out in at %s"
                           % ((obj.name, ay.x, ay.y, ay.z, fold[2], fold[0])))
        elif ay.z <= 0.05:
            bad.append("%s: +Y does not point out of the surface" % obj.name)
        if abs(ax.z) > 0.05:
            bad.append("%s: +X is not the surface width" % obj.name)
        if ay.dot(az) > 1e-5 or ay.dot(ax) > 1e-5:
            bad.append("%s: axes are not orthogonal" % obj.name)

    # The 1.35 m LaserStaff, which is the whole reason SURF_LongGoods exists,
    # and the area, which is the whole reason SURF_Rack does.
    biggest = max(w * d for _, _, _, _, w, d in SURFACES)
    for name, _, _, _, w, d in SURFACES:
        diag = math.hypot(w, d)
        note = "<-- takes the 1.35 m staff" if diag > 1.35 else ""
        if w * d >= biggest - 1e-9:
            note = (note + "  ").lstrip() + "<-- largest rectangle on the rig"
        print("    %-15s %.2f m^2  longest item that fits = %.4f m %s"
              % (name, w * d, diag, note))

    print("  --- fold angles from the authored (deployed) zero ---")
    print("    PIVOT_Back    X %+6.1f    PIVOT_Leaf    X %+6.1f" % (25.0, -90.0))
    print("    PIVOT_Wing_L  Y %+6.1f    PIVOT_Wing_R  Y %+6.1f"
          "  (Blender frame; Unity mirrors Y)" % (90.0, -90.0))
    print("    PIVOT_Lid     X %+6.1f  (relative to the LEAF it rides)" % -90.0)
    print("    the RACK is PIVOT_Leaf X -90 plus the lid's own relative -90, "
          "with the other three held at zero")

    # Where the rack actually stands, since nothing else in this dump shows it.
    rack = bpy.data.objects.get("Mesh_Rig_RackLadder")
    leaf = bpy.data.objects.get("Mesh_Rig_FrontLeaf")
    if rack is not None and leaf is not None:
        pts = [RAISE @ (o.matrix_world @ Vector(c))
               for o in (rack, leaf) for c in o.bound_box]
        print("  RACK RAISED  x[%6.3f %6.3f] y[%6.3f %6.3f] z[%6.3f %6.3f]"
              % (min(q.x for q in pts), max(q.x for q in pts),
                 min(q.y for q in pts), max(q.y for q in pts),
                 min(q.z for q in pts), max(q.z for q in pts)))

    pts = [o.matrix_world @ Vector(c)
           for o in bpy.data.objects if o.type == 'MESH' for c in o.bound_box]
    lo = Vector((min(q.x for q in pts), min(q.y for q in pts), min(q.z for q in pts)))
    hi = Vector((max(q.x for q in pts), max(q.y for q in pts), max(q.z for q in pts)))
    print("  BOUNDS W=%.3f D=%.3f H=%.3f  (%.3f..%.3f, %.3f..%.3f, %.3f..%.3f)"
          % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z, lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))

    if bad:
        raise SystemExit("Surface convention violated:\n  " + "\n  ".join(bad))


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_Rig_Expedition")

    pivots = {
        "back": (empty("PIVOT_Back", BACK_HINGE, coll, size=0.16), BACK_HINGE),
        "leaf": (empty("PIVOT_Leaf", LEAF_HINGE, coll, size=0.16), LEAF_HINGE),
        "wing_l": (empty("PIVOT_Wing_L", WING_HINGE_L, coll, size=0.13), WING_HINGE_L),
        "wing_r": (empty("PIVOT_Wing_R", WING_HINGE_R, coll, size=0.13), WING_HINGE_R),
        "lid": (empty("PIVOT_Lid", LID_HINGE, coll, size=0.13), LID_HINGE),
    }

    # The wing pivots and the lid ride the BOARD: children of PIVOT_Leaf, so
    # their fold is relative to it. The wings were reparented by hand on
    # 2026-08-24 (ground-hinged wings read as the board abandoning its sides);
    # folded into the build on 2026-08-25 so a regeneration needs no hand-edit
    # pass any more.
    leaf_host, leaf_world = pivots["leaf"]
    for rider in ("wing_l", "wing_r", "lid"):
        attach(pivots[rider][0], leaf_host, leaf_world)

    for name, fn, origin, parent, bevel, seg in PARTS:
        p = Part(mats)
        fn(p)
        if bevel:
            p.bevel(width=bevel, segments=seg)
        obj = p.finish(name, coll, origin=origin)
        if parent is not None:
            host, host_world = pivots[parent]
            attach(obj, host, host_world)

    for name, parent, loc, rot, _w, _d in SURFACES:
        surf = empty(name, loc, coll, rot=rot, size=0.09)
        host, host_world = pivots[parent]
        attach(surf, host, host_world)

    report()
    dump_surfaces()
    save(out)


main()
