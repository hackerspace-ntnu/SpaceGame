"""components/props/expedition_backpack — the astronaut's flap-top expedition pack.

Replaces `field_backpack`, which was a two-door cabinet: a good container and a
poor backpack. This one reads as a big camping pack carried across a foreign
planet — oxygen bottles, breathing tubes, cargo nets, cord lacing, bulging
pouches, nothing square.

One variation, deliberately: this is a specific piece of the player's kit rather
than set dressing, and Unity binds to its object names. Everything else in this
folder is a family; this one is a contract.

Built as ~34 separate objects, not three merged meshes
------------------------------------------------------
Every part that someone might plausibly want to restyle, move or delete on its
own is its own object: each shelf, each pouch, each oxygen bottle, the harness
per side, the lacing per side, every net, every pocket. Merging them into one
mesh per moving group is cheaper to draw and much worse to work with — you
cannot select a pouch, cannot swap a bottle, cannot delete the antenna without
editing polygons.

The cost is real but small for a hero prop: ~34 draw calls instead of 3. If that
ever matters, merge at import time rather than at build time — the split is the
useful state to keep the source in.

Three groups, by what they hang off:

  root         everything static. The carcass, shelves, exoframe, harness,
               lacing, pouches, oxygen, bedroll.
  PIVOT_Panel  the front panel and everything lashed to it.
  PIVOT_Lid    the storm flap and everything lashed to it.

How it opens, and why that shape
--------------------------------
A top-loading pack hides its contents down a vertical tube, and this pack's
whole interaction is seeing your gear as 3-D meshes and aiming at them. So it
loads the way real expedition packs do — through a full-height front panel —
and the flap on top is a second, smaller compartment rather than the only way in.

  PIVOT_Panel   bottom front edge. The front panel folds DOWN to horizontal,
                about +95 degrees, and lands as a mat in front of the pack.
  PIVOT_Lid     back top edge. The storm flap tips UP and BACK, about -105
                degrees, uncovering the brain compartment.

Both hinge lines run along the pack's X. Unity drives each one with a single
angle about the pivot's own local axis (`BackpackHinge`), so nothing here has to
agree with a hardcoded pair of doors.

Axes, which are the whole reason this file is fussy
---------------------------------------------------
Built in the library frame (+Z up, -Y forward).

  +Y  the pack's back, worn against the astronaut. The FRAME: exoframe, back
      wall, shoulder harness. It never moves.
  -Y  the pack's outer face. The front panel closes over it.
  +Z  up, both when shouldered and when standing on the ground.

Every `SOCK_*` empty obeys one rule: **local +Y points out of its own mouth.**
Unity parents an item at identity, so local +Y is the item's up.

  SOCK_Int_*  stow INSIDE THE CARCASS, standing on static shelves. Rotated
              (90, 0, 0) so local +Y is world +Z and an item stands on its shelf.
  SOCK_Ext_*  hang off outer surfaces, each pointing out of the surface it is on:
              the panel's face (-Y), the lid's top (+Z), the pouches' flanks
              (+/-X).

Interior anchors are NOT children of a hinge, and that is load-bearing rather
than incidental. An anchor parented to a moving panel seats its item along that
panel's normal, so the moment the panel swings, every item juts out into thin air
on the end of a board. Shelves are the only placement that stays natural through
the whole swing, because the item is resting on something the entire time.

Get any of this wrong and nothing looks broken in Blender — every item just lies
on its face in game — so `dump_sockets()` asserts it, closed and open, rather
than trusting it.

Size: 1.17 x 0.75 x 1.59 m over the pouches and pockets, 0.90 m across the body
alone, against a ~1.8 m astronaut.

Origin at the bottom centre of the footprint, matching the rest of this folder.
Each PART's own origin sits at its logical connection point, so moving a pouch or
a bottle in the outliner rotates and scales about something sensible.

    blender --background --python expedition_backpack.py -- --out expedition_backpack.blend

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
    "Mat_Fabric_Canvas_Faded",    # 0  the body, harness webbing, pouch cloth
    "Mat_Metal_Steel_Worn",       # 1  the bent exoframe, welds, buckle frames
    "Mat_Metal_Rust_Heavy",       # 2  scavenged plate, skid plate, tank cradles
    "Mat_Metal_Brass_Tarnished",  # 3  eyelets, buckles, valve manifold
    "Mat_Plastic_Rubber_Black",   # 4  breathing tubes, lacing cord, cargo net, pads
    "Mat_Emissive_Amber",         # 5  the gauge lamps
    "Mat_Paint_Safety_Orange",    # 6  oxygen bottles — the one high-vis note
    "Mat_Fabric_Wing_Ochre",      # 7  pouch flaps and lid, a shade off the body
]
CANVAS, STEEL, RUST, BRASS, RUBBER, AMBER, ORANGE, OCHRE = range(8)

# --- the carcass ----------------------------------------------------------
# A soft-shouldered sack, not a box: narrow and shallow at the base, widest a
# third of the way up, tapering back in at the shoulders. Every wall is lofted
# through these stations rather than extruded as a slab.
#
#            z      half-width   back face   corner radius
STATIONS = ((0.060, 0.360, 0.215, 0.055),
            (0.300, 0.428, 0.262, 0.085),
            (0.620, 0.450, 0.280, 0.100),
            (0.950, 0.437, 0.268, 0.095),
            (1.220, 0.392, 0.238, 0.070))

Y_MOUTH = -0.210      # front edge of the side walls: the plane the panel closes on
Y_PANEL = -0.292      # outer face of the closed front panel
Z_BOT = 0.060
Z_BODY = 1.220        # top rim of the main carcass, floor of the brain
Z_TOP = 1.530         # top of the closed lid
T = 0.030             # wall thickness

WALL_Z = [0.060, 0.180, 0.300, 0.460, 0.620, 0.790, 0.950, 1.090, 1.220]

# --- where gear actually goes ---------------------------------------------
SHELF_Z = (0.110, 0.490, 0.870)          # top surface of floor, then two shelves
INT_X = (-0.280, 0.0, 0.280)             # three across, pitched for a 0.28 m item
INT_Y = 0.020                            # mid-depth of the bay
BRAIN_Z = 1.258                          # floor of the brain compartment
BRAIN_X = (-0.250, 0.0, 0.250)

# Ten exterior anchors, placed by one rule: a socket has to present its gear in
# BOTH states. That keeps most of them off the front panel — the panel is hinged
# on its BOTTOM edge, the only hinge that lays it out on the ground, and a board
# that falls forward about its own base arrives outer face DOWN.
PANEL_CELLS = ((-0.205, 0.520), (0.205, 0.520))
PANEL_SOCK_Y = -0.330
LID_CELLS = (-0.185, 0.185)
LID_SOCK_Z = 1.556
POUCH_TOP_Z = 0.548

ROT_UP = (math.pi / 2.0, 0.0, 0.0)
ROT_FRONT = (0.0, 0.0, math.pi)
ROT_FLANK = (0.0, 0.0, -math.pi / 2.0)

LID_HINGE = Vector((0.0, 0.238, 1.238))
PANEL_HINGE = Vector((0.0, -0.244, 0.104))
LID_OPEN_DEG = -105.0
PANEL_OPEN_DEG = 95.0

# Pouch swell per station. Shared between the pouch body, its flap and its net so
# all three agree on where the surface is.
POUCH_Z = [0.148, 0.240, 0.340, 0.452, POUCH_TOP_Z]
POUCH_SWELL = {0.148: 0.24, 0.240: 0.74, 0.340: 1.00, 0.452: 0.84, POUCH_TOP_Z: 0.40}


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def lerp_station(z):
    """(half_width, back_face_y, corner_radius) of the carcass at height z."""
    pts = STATIONS
    if z <= pts[0][0]:
        return pts[0][1], pts[0][2], pts[0][3]
    if z >= pts[-1][0]:
        return pts[-1][1], pts[-1][2], pts[-1][3]

    for a, b in zip(pts, pts[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / (b[0] - a[0])
            return (a[1] + (b[1] - a[1]) * t,
                    a[2] + (b[2] - a[2]) * t,
                    a[3] + (b[3] - a[3]) * t)
    return pts[-1][1], pts[-1][2], pts[-1][3]


def pouch_out(z):
    """How far a side pouch stands proud of the carcass at height z."""
    hw, _, _ = lerp_station(z)
    return hw + 0.008 + 0.118 * POUCH_SWELL[round(z, 3)]


def round_rect(x0, x1, y0, y1, r, seg=3):
    """A closed convex (x, y) profile with rounded corners.

    Every lofted piece on this pack is one of these. Convex on purpose: a
    C-shaped profile would need a concave n-gon cap, which triangulates into
    overlapping faces on export, so the hollow carcass is built as separate
    convex walls instead of one shell with a bite out of it.
    """
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
    """A tube following a polyline, with a weld collar at every kink.

    The exoframe is meant to read as hand-bent over a knee, not drawn through a
    mandrel, so it is a chain of straight runs with visible lumps at the joints
    rather than a swept curve.
    """
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
    """A dashed thread run. Cheapest thing that reads as sewn rather than moulded."""
    a, b = Vector(a), Vector(b)
    d = b - a
    for i in range(count):
        p.box(a + d * ((i + 0.5) / count), size, mat)


def loop_buckle(p, c, w, h, t, mat, bar=True):
    """A rectangular hardware loop with a centre bar — strap threads through it."""
    cx, cy, cz = c
    for sz in (-1, 1):
        p.box((cx, cy, cz + sz * (h / 2 - t / 2)), (w, t, t), mat)
    for sx in (-1, 1):
        p.box((cx + sx * (w / 2 - t / 2), cy, cz), (t, t, h), mat)
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
    valid because every parent in this chain is an unrotated translation.
    """
    child.parent = parent
    child.matrix_parent_inverse = Matrix.Identity(4)
    child.location = Vector(child.location) - Vector(parent_world)
    return child


# ---------------------------------------------------------------------------
# One object per part
# ---------------------------------------------------------------------------
#
# Everything below is built in WORLD space and then re-origined by `finish`, so
# each function reads as "where this thing is on the pack" while the resulting
# object still pivots about something sensible.

PARTS = []


def part(name, origin=(0.0, 0.0, 0.0), parent=None, bevel=0.006):
    """Register a part builder. `parent` is 'panel', 'lid', or None for static."""
    def wrap(fn):
        PARTS.append((name, fn, Vector(origin), parent, bevel))
        return fn
    return wrap


# --- shell ----------------------------------------------------------------

@part("Mesh_Pack_Carcass")
def _carcass(p):
    """Back wall and the two side walls: the box gear actually sits in.

    Three lofted walls rather than one hollow shell. The shell would be the
    obvious build, but its cross-section is a C, and a C caps into a concave
    n-gon that triangulates badly on FBX export.
    """
    def back_profile(z):
        hw, yb, r = lerp_station(z)
        return round_rect(-hw, hw, yb - T, yb, min(r, T * 0.9), seg=2)

    p.loft([(z, back_profile(z)) for z in WALL_Z], axis='Z', mat=CANVAS)

    for sx in (-1, 1):
        def side_profile(z, sx=sx):
            hw, yb, r = lerp_station(z)
            x_out, x_in = sx * hw, sx * (hw - T)
            return round_rect(min(x_out, x_in), max(x_out, x_in),
                              Y_MOUTH, yb, min(r, T * 0.9), seg=2)

        p.loft([(z, side_profile(z)) for z in WALL_Z], axis='Z', mat=CANVAS)


def _shelf(index):
    """One tier: the board plus the lip that stops gear sliding off it.

    These are what the interior anchors stand on, so they are load-bearing to the
    design and not interior dressing — an item with nothing under it is the whole
    failure this layout avoids.
    """
    z = SHELF_Z[index]

    def build(p, z=z, index=index):
        hw, yb, _ = lerp_station(z)
        inset = T if index else 0.0
        p.slab((-(hw - inset), Y_MOUTH + 0.010, z - 0.026),
               (hw - inset, yb - T, z), CANVAS)
        p.box((0.0, Y_MOUTH + 0.022, z + 0.018),
              (2 * (hw - inset) - 0.02, 0.024, 0.036), CANVAS)

    return build


for _i in range(len(SHELF_Z)):
    part("Mesh_Pack_Shelf_%d" % _i, origin=(0.0, 0.0, SHELF_Z[_i]))(_shelf(_i))


@part("Mesh_Pack_BrainDeck", origin=(0.0, 0.0, Z_BODY))
def _brain(p):
    """The fabric floor and walls of the top compartment.

    Without it the lid opens onto a 1.2 m shaft rather than onto something you
    can see gear in.
    """
    hw, yb, _ = lerp_station(Z_BODY)
    p.slab((-hw, Y_MOUTH + 0.030, Z_BODY), (hw, yb - T, Z_BODY + 0.026), CANVAS)

    for sx in (-1, 1):
        p.slab((sx * hw, Y_MOUTH + 0.03, Z_BODY),
               (sx * (hw - T), yb - T, Z_TOP - 0.06), CANVAS)
    p.slab((-hw, yb - T, Z_BODY), (hw, yb - T - 0.026, Z_TOP - 0.06), CANVAS)
    p.slab((-hw, Y_MOUTH + 0.030, Z_BODY), (hw, Y_MOUTH + 0.056, Z_TOP - 0.10), CANVAS)


@part("Mesh_Pack_Collar", origin=(0.0, 0.0, Z_BODY + 0.065))
def _collar(p):
    """The rolled hem around the brain mouth — the roll-top read."""
    hw, yb, _ = lerp_station(Z_BODY)

    for sx in (-1, 1):
        bent_tube(p, [(sx * (hw - 0.02), Y_MOUTH + 0.05, Z_BODY + 0.055),
                      (sx * (hw + 0.01), 0.02, Z_BODY + 0.075),
                      (sx * (hw - 0.02), yb - 0.03, Z_BODY + 0.055)],
                  0.030, CANVAS, seg=8, collar=False)
    for sy, y in ((1, yb - 0.03), (-1, Y_MOUTH + 0.05)):
        bent_tube(p, [(-(hw - 0.02), y, Z_BODY + 0.055),
                      (0.0, y + sy * 0.012, Z_BODY + 0.068),
                      (hw - 0.02, y, Z_BODY + 0.055)],
                  0.030, CANVAS, seg=8, collar=False)


# --- frame ----------------------------------------------------------------

@part("Mesh_Pack_Exoframe")
def _exoframe(p):
    """The bent steel skeleton: verticals, skids, cross-braces, top lash bar."""
    for sx in (-1, 1):
        hw_lo, yb_lo, _ = lerp_station(0.30)
        hw_mid, yb_mid, _ = lerp_station(0.62)
        hw_hi, yb_hi, _ = lerp_station(1.10)

        bent_tube(p, [
            (sx * (hw_lo + 0.014), yb_lo - 0.02, 0.040),
            (sx * (hw_mid + 0.020), yb_mid - 0.01, 0.330),
            (sx * (hw_mid + 0.022), yb_mid + 0.005, 0.760),
            (sx * (hw_hi + 0.018), yb_hi - 0.01, 1.140),
            (sx * 0.352, 0.150, 1.330),
            (sx * 0.300, -0.070, 1.395),
        ], 0.026, STEEL)

        bent_tube(p, [
            (sx * (hw_lo + 0.014), yb_lo - 0.02, 0.040),
            (sx * 0.372, 0.020, 0.028),
            (sx * 0.330, -0.150, 0.042),
            (sx * 0.296, -0.226, 0.086),
        ], 0.024, STEEL)

    bent_tube(p, [(-0.300, -0.070, 1.395), (-0.120, -0.098, 1.418),
                  (0.120, -0.098, 1.418), (0.300, -0.070, 1.395)], 0.021, STEEL)

    for z in (0.330, 0.760, 1.140):
        hw, yb, _ = lerp_station(z)
        bent_tube(p, [(-(hw + 0.018), yb + 0.004, z), (hw + 0.018, yb + 0.004, z)],
                  0.019, STEEL)
        for sx in (-1, 1):
            p.cyl((sx * (hw + 0.010), yb + 0.004, z), 0.035, 0.046,
                  axis='X', seg=8, mat=STEEL)

    hw_a, yb_a, _ = lerp_station(0.36)
    hw_b, yb_b, _ = lerp_station(0.74)
    bent_tube(p, [(-(hw_a - 0.01), yb_a + 0.026, 0.360),
                  (hw_b - 0.01, yb_b + 0.026, 0.740)], 0.016, STEEL)
    bent_tube(p, [(hw_a - 0.01, yb_a + 0.034, 0.360),
                  (-(hw_b - 0.01), yb_b + 0.034, 0.740)], 0.016, STEEL)


def _outrigger(sx):
    """A D of tube standing off the flank — what SOCK_Ext_6/7 clip onto."""
    def build(p, sx=sx):
        hw, yb, _ = lerp_station(0.98)
        bent_tube(p, [
            (sx * (hw + 0.018), yb - 0.02, 1.062),
            (sx * (hw + 0.062), 0.010, 1.010),
            (sx * (hw + 0.058), -0.180, 0.958),
            (sx * (hw + 0.020), -0.232, 0.902),
            (sx * (hw + 0.048), -0.040, 0.856),
            (sx * (hw + 0.018), yb - 0.02, 0.836),
        ], 0.021, STEEL)

    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    _hw, _, _ = lerp_station(0.98)
    part("Mesh_Pack_OutriggerLoop_" + _side,
         origin=(_sx * (_hw + 0.018), 0.0, 0.949))(_outrigger(_sx))


@part("Mesh_Pack_SkidPlate", origin=(0.0, 0.0, Z_BOT))
def _skid(p):
    """Riveted plate under the floor, where a pack dumped on rock wears out."""
    hw, yb, _ = lerp_station(Z_BOT)
    p.slab((-(hw - 0.03), Y_MOUTH + 0.02, Z_BOT - 0.028),
           (hw - 0.03, yb - 0.02, Z_BOT), RUST)
    for sy in (-1, 1):
        p.rivets((-(hw - 0.07), sy * 0.090, Z_BOT - 0.024),
                 (hw - 0.07, sy * 0.090, Z_BOT - 0.024), 8,
                 radius=0.016, height=0.018, axis='Z', mat=STEEL)


# --- harness --------------------------------------------------------------

def _harness(sx):
    """One shoulder strap: webbing, pad, stitching and its two buckles."""
    def build(p, sx=sx):
        path = [
            (sx * 0.150, 0.216, 1.246), (sx * 0.196, 0.256, 1.180),
            (sx * 0.232, 0.286, 1.020), (sx * 0.246, 0.282, 0.810),
            (sx * 0.232, 0.268, 0.596), (sx * 0.262, 0.262, 0.372),
            (sx * 0.318, 0.244, 0.166),
        ]
        ribbon(p, path, 0.128, 0.034, CANVAS)

        pad = [(x, y + 0.030, z) for x, y, z in path[1:4]]
        ribbon(p, pad, 0.152, 0.024, RUBBER)
        for a, b in zip(pad, pad[1:]):
            for sw in (-1, 1):
                stitches(p, (a[0] + sw * 0.070, a[1] + 0.013, a[2]),
                         (b[0] + sw * 0.070, b[1] + 0.013, b[2]),
                         5, CANVAS, (0.010, 0.007, 0.022))

        loop_buckle(p, (sx * 0.250, 0.284, 0.488), 0.140, 0.078, 0.017, BRASS)
        loop_buckle(p, (sx * 0.300, 0.268, 0.252), 0.124, 0.066, 0.015, BRASS)

    return build


def _hipbelt(sx):
    """The wing that actually carries a big pack's weight.

    Kept inside the side pouches' own width so it does not set the silhouette.
    """
    def build(p, sx=sx):
        hw, yb, _ = lerp_station(0.26)
        ribbon(p, [(sx * (hw - 0.05), yb + 0.020, 0.250),
                   (sx * (hw + 0.056), yb + 0.010, 0.232),
                   (sx * (hw + 0.104), yb - 0.056, 0.208)],
               0.148, 0.046, RUBBER)

    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    part("Mesh_Pack_Harness_" + _side, origin=(_sx * 0.150, 0.216, 1.246))(_harness(_sx))
    _hw, _yb, _ = lerp_station(0.26)
    part("Mesh_Pack_HipBelt_" + _side,
         origin=(_sx * (_hw - 0.05), _yb, 0.235))(_hipbelt(_sx))


@part("Mesh_Pack_HaulHandle", origin=(0.0, 0.190, 1.330))
def _handle(p):
    ribbon(p, [(-0.115, 0.176, 1.300), (-0.048, 0.196, 1.356),
               (0.048, 0.196, 1.356), (0.115, 0.176, 1.300)],
           0.078, 0.030, CANVAS, flat='Y')


@part("Mesh_Pack_RepairPatch", origin=(0.120, 0.270, 0.860))
def _patch(p):
    """A sewn-on square, because nothing on this pack was bought new."""
    hw, yb, _ = lerp_station(0.86)
    p.box((0.120, yb + 0.008, 0.860), (0.240, 0.014, 0.210), OCHRE)
    for z in (0.962, 0.758):
        stitches(p, (0.002, yb + 0.016, z), (0.240, yb + 0.016, z),
                 9, RUBBER, (0.014, 0.007, 0.007))


def _lacing(sx):
    """Cord lacing down one flank through punched brass eyelets.

    The 'thread going down' — a zig-zag that visibly ties the canvas body to the
    steel frame, which is the join the whole pack hangs on.
    """
    def build(p, sx=sx):
        prev = None
        for z in [0.200 + 0.145 * i for i in range(8)]:
            hw, yb, _ = lerp_station(z)
            inner = (sx * (hw - 0.006), yb - 0.130, z)
            outer = (sx * (hw + 0.020), yb - 0.020, z + 0.072)

            p.tube(inner, 0.020, 0.008, 0.018, axis='X', seg=8, mat=BRASS)
            p.seam(inner, outer, width=0.013, depth=0.013, axis='X', mat=RUBBER)
            if prev is not None:
                p.seam(prev, inner, width=0.013, depth=0.013, axis='X', mat=RUBBER)
            prev = outer

    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    _hw, _, _ = lerp_station(0.70)
    part("Mesh_Pack_Lacing_" + _side, origin=(_sx * _hw, 0.0, 0.700))(_lacing(_sx))


# --- side pouches ---------------------------------------------------------

def _pouch(sx):
    """A short, deep-bellied blister, not a slab bolted to the flank.

    It has to stay well inside the pack's own depth: stretched across the full
    0.45 m it reads as a shelf from the side, which is the one silhouette this
    whole model exists to avoid.
    """
    def build(p, sx=sx):
        def profile(z, sx=sx):
            hw, _, _ = lerp_station(z)
            s = POUCH_SWELL[round(z, 3)]
            x0, x1 = sorted((sx * (hw - 0.010), sx * pouch_out(z)))
            return round_rect(x0, x1, -0.020 - 0.108 * s, 0.128 + 0.078 * s,
                              0.036 + 0.042 * s, seg=3)

        p.loft([(z, profile(z)) for z in POUCH_Z], axis='Z', mat=CANVAS, cap=True)

        hw_mid, _, _ = lerp_station(0.340)
        ribbon(p, [(sx * (hw_mid - 0.010), -0.110, 0.340),
                   (sx * (pouch_out(0.340) - 0.004), -0.096, 0.348),
                   (sx * (pouch_out(0.340) - 0.004), 0.180, 0.348),
                   (sx * (hw_mid - 0.010), 0.194, 0.340)],
               0.062, 0.014, CANVAS, flat='Z')

    return build


def _pouch_flap(sx):
    """Storm flap folded over the pouch mouth, with its tail and buckle."""
    def build(p, sx=sx):
        hw_top, _, _ = lerp_station(POUCH_TOP_Z)
        top_out = pouch_out(POUCH_TOP_Z)

        p.slab((sx * (hw_top - 0.010), -0.070, POUCH_TOP_Z - 0.004),
               (sx * (top_out + 0.014), 0.166, POUCH_TOP_Z + 0.024), OCHRE)
        p.slab((sx * (top_out - 0.006), -0.056, 0.448),
               (sx * (top_out + 0.016), 0.140, POUCH_TOP_Z + 0.010), OCHRE)
        loop_buckle(p, (sx * (top_out + 0.012), 0.040, 0.454), 0.060, 0.046, 0.012, BRASS)

    return build


def _pouch_net(sx):
    """Net over the pouch's top face — what SOCK_Ext_4/5 stow under."""
    def build(p, sx=sx):
        hw_top, _, _ = lerp_station(POUCH_TOP_Z)
        net(p, (sx * (hw_top + 0.006), -0.052, POUCH_TOP_Z + 0.030),
            (sx * (pouch_out(POUCH_TOP_Z) + 0.008), 0.150, POUCH_TOP_Z + 0.030),
            3, 3, 0.009, RUBBER, plane='XY')

    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    _hw, _, _ = lerp_station(0.340)
    part("Mesh_Pack_Pouch_" + _side, origin=(_sx * _hw, 0.05, 0.340))(_pouch(_sx))
    part("Mesh_Pack_PouchFlap_" + _side,
         origin=(_sx * pouch_out(POUCH_TOP_Z), 0.05, POUCH_TOP_Z))(_pouch_flap(_sx))
    part("Mesh_Pack_PouchNet_" + _side,
         origin=(_sx * pouch_out(POUCH_TOP_Z), 0.05, POUCH_TOP_Z + 0.030),
         bevel=0.0)(_pouch_net(_sx))


# --- oxygen ---------------------------------------------------------------

def _tank(sx):
    """One bottle and the two cradle bands clamping it to the frame."""
    def build(p, sx=sx):
        hw, _, _ = lerp_station(0.86)
        cx = sx * (hw + 0.062)

        p.cyl((cx, 0.020, 0.860), 0.072, 0.400, axis='Z', seg=14, mat=ORANGE)
        p.cyl((cx, 0.020, 1.072), 0.072, 0.040, axis='Z', seg=14, mat=ORANGE,
              radius_top=0.044)
        p.cyl((cx, 0.020, 0.652), 0.072, 0.048, axis='Z', seg=14, mat=ORANGE,
              radius_top=0.050)
        p.cyl((cx, 0.020, 1.108), 0.030, 0.048, axis='Z', seg=10, mat=BRASS)

        for z in (0.740, 0.990):
            p.tube((cx, 0.020, z), 0.084, 0.016, 0.038, axis='Z', seg=14, mat=RUST)
            p.box((sx * (hw + 0.008), 0.020, z), (0.052, 0.070, 0.030), RUST)

    return build


def _tube(sx):
    """The braided line from the bottle's valve up into the shoulder harness."""
    def build(p, sx=sx):
        hw, _, _ = lerp_station(0.86)
        cx = sx * (hw + 0.062)

        bent_tube(p, [
            (cx, 0.020, 1.128), (cx, 0.086, 1.176),
            (sx * 0.286, 0.170, 1.238), (sx * 0.196, 0.238, 1.276),
            (sx * 0.116, 0.244, 1.246),
        ], 0.017, RUBBER, seg=8)

        for i in range(4):
            t = 0.2 + 0.2 * i
            p.cyl((cx + (sx * 0.116 - cx) * t, 0.020 + 0.224 * t, 1.128 + 0.118 * t),
                  0.021, 0.014, axis='Z', seg=8, mat=BRASS)

    return build


for _sx, _side in ((-1, "L"), (1, "R")):
    _hw, _, _ = lerp_station(0.86)
    part("Mesh_Pack_OxygenTank_" + _side,
         origin=(_sx * (_hw + 0.062), 0.020, 0.860))(_tank(_sx))
    part("Mesh_Pack_OxygenTube_" + _side,
         origin=(_sx * (_hw + 0.062), 0.020, 1.128))(_tube(_sx))


@part("Mesh_Pack_Manifold", origin=(0.0, 0.232, 1.286))
def _manifold(p):
    """Valve block across the top back, with a gauge that reads amber."""
    p.box((0.0, 0.232, 1.286), (0.180, 0.070, 0.076), STEEL)
    p.cyl((0.0, 0.272, 1.286), 0.044, 0.026, axis='Y', seg=14, mat=BRASS)
    p.cyl((0.0, 0.286, 1.286), 0.032, 0.010, axis='Y', seg=14, mat=AMBER)
    for sx in (-1, 1):
        p.cyl((sx * 0.064, 0.270, 1.322), 0.014, 0.014, axis='Y', seg=8, mat=AMBER)


@part("Mesh_Pack_Antenna", origin=(0.086, 0.246, 1.320))
def _antenna(p):
    """Whip off the back of the manifold.

    Kept under the lid's own height so the pack's silhouette stays 1.53 m rather
    than becoming a 1.8 m spike.
    """
    bent_tube(p, [(0.086, 0.246, 1.320), (0.100, 0.228, 1.430), (0.112, 0.202, 1.516)],
              0.008, STEEL, seg=6, collar=False)
    p.cyl((0.112, 0.202, 1.526), 0.015, 0.022, axis='Z', seg=8, mat=AMBER)


@part("Mesh_Pack_Bedroll", origin=(0.0, -0.078, 0.078))
def _bedroll(p):
    """A roll lashed under the base — the classic expedition silhouette.

    Sits ON the ground plane rather than through it: the origin is the bottom
    centre of the footprint, and a deployed pack is placed at a raycast hit, so
    anything below z=0 buries itself in the sand.
    """
    hw, _, _ = lerp_station(Z_BOT)
    r = 0.078
    cz = r
    p.cyl((0.0, -0.078, cz), r, 2 * (hw - 0.02), axis='X', seg=14, mat=OCHRE)
    for sx in (-1, 1):
        p.cyl((sx * (hw - 0.02), -0.078, cz), r, 0.020, axis='X', seg=14, mat=CANVAS)
        ribbon(p, [(sx * 0.180, -0.078, cz + r + 0.016), (sx * 0.180, -0.168, cz),
                   (sx * 0.180, -0.078, cz - r - 0.010)],
               0.052, 0.012, CANVAS, flat='X')


# --- the front panel ------------------------------------------------------

PANEL_Z = [0.104, 0.260, 0.500, 0.760, 0.980, 1.170]
PANEL_BULGE = {0.104: 0.20, 0.260: 0.72, 0.500: 1.00,
               0.760: 0.94, 0.980: 0.72, 1.170: 0.26}


@part("Mesh_Pack_Panel", origin=PANEL_HINGE, parent="panel")
def _panel(p):
    """The full-height front panel, hinged on its BOTTOM edge."""
    def profile(z):
        hw, _, r = lerp_station(z)
        b = PANEL_BULGE[round(z, 3)]
        return round_rect(-(hw - 0.004), hw - 0.004, Y_PANEL - 0.052 * b,
                          Y_MOUTH + 0.006, min(r, 0.075), seg=3)

    p.loft([(z, profile(z)) for z in PANEL_Z], axis='Z', mat=CANVAS, cap=True)


@part("Mesh_Pack_PanelPlate", origin=(0.0, Y_PANEL - 0.060, 0.395), parent="panel")
def _panel_plate(p):
    """Scavenged plate riveted over the lower half, where a pack takes knocks."""
    p.slab((-0.230, Y_PANEL - 0.070, 0.230), (0.215, Y_PANEL - 0.052, 0.560), RUST)
    for z in (0.248, 0.542):
        p.rivets((-0.212, Y_PANEL - 0.062, z), (0.198, Y_PANEL - 0.062, z), 7,
                 radius=0.013, height=0.014, axis='Y', mat=STEEL)


def _panel_pocket(sx, cz, cw):
    """A bulging cargo pocket on the panel's outer face, with its own flap."""
    def build(p, sx=sx, cz=cz, cw=cw):
        cx = sx * 0.190
        p.loft([(y, round_rect(cx - cw / 2 * s, cx + cw / 2 * s,
                               cz - 0.130 * s, cz + 0.130 * s, 0.040, seg=3))
                for y, s in ((Y_PANEL - 0.048, 0.55),
                             (Y_PANEL - 0.086, 0.98),
                             (Y_PANEL - 0.118, 0.80))],
               axis='Y', mat=OCHRE, cap=True)
        p.slab((cx - cw / 2 - 0.014, Y_PANEL - 0.126, cz + 0.118),
               (cx + cw / 2 + 0.014, Y_PANEL - 0.040, cz + 0.146), OCHRE)
        loop_buckle(p, (cx, Y_PANEL - 0.122, cz + 0.070), 0.056, 0.044, 0.011, BRASS)

    return build


for _i, (_cz, _cw) in enumerate(((0.740, 0.190), (1.010, 0.150))):
    for _sx, _side in ((-1, "L"), (1, "R")):
        part("Mesh_Pack_PanelPocket_%s%d" % (_side, _i),
             origin=(_sx * 0.190, Y_PANEL - 0.086, _cz),
             parent="panel")(_panel_pocket(_sx, _cz, _cw))


@part("Mesh_Pack_PanelNet", origin=(0.0, Y_PANEL - 0.085, 0.670),
      parent="panel", bevel=0.0)
def _panel_net(p):
    """The cargo net over the whole face.

    Decoration by design: the panel is bottom-hinged, so its outer face ends up
    against the ground when the pack opens. Gear that has to stay presentable in
    both states rides the lid, the pouches and the outrigger loops instead.
    """
    net(p, (-0.330, 0.220, Y_PANEL - 0.078), (0.330, 1.120, Y_PANEL - 0.092),
        5, 6, 0.011, RUBBER, plane='XZ')


@part("Mesh_Pack_PanelHardware", origin=(0.0, Y_PANEL - 0.030, 0.690), parent="panel")
def _panel_hardware(p):
    """Lash eyelets down both edges and the buckles that latch it to the collar."""
    for sx in (-1, 1):
        for z in (0.300, 0.560, 0.820, 1.080):
            p.tube((sx * 0.352, Y_PANEL - 0.030, z), 0.019, 0.008, 0.016,
                   axis='Y', seg=8, mat=BRASS)

        loop_buckle(p, (sx * 0.180, Y_PANEL - 0.050, 1.186), 0.086, 0.056, 0.013, BRASS)
        ribbon(p, [(sx * 0.180, Y_PANEL - 0.030, 1.150), (sx * 0.180, Y_MOUTH, 1.216)],
               0.070, 0.016, CANVAS, flat='X')


# --- the lid --------------------------------------------------------------

@part("Mesh_Pack_Lid", origin=LID_HINGE, parent="lid")
def _lid(p):
    """A domed storm flap rather than a board, plus the skirt that hangs over
    the collar when it is shut."""
    sections = []
    for z, s in ((1.238, 1.00), (1.400, 0.94), (1.492, 0.74), (1.530, 0.40)):
        hw, yb, _ = lerp_station(min(z, Z_BODY))
        sections.append((z, round_rect(-(hw + 0.026) * s, (hw + 0.026) * s,
                                       (Y_MOUTH - 0.052) * s, (yb + 0.024) * s,
                                       0.090 * s + 0.02, seg=3)))
    p.loft(sections, axis='Z', mat=OCHRE, cap=True)

    hw, _, _ = lerp_station(Z_BODY)
    p.slab((-(hw + 0.020), Y_MOUTH - 0.056, 1.238),
           (hw + 0.020, Y_MOUTH - 0.026, 1.150), OCHRE)


@part("Mesh_Pack_LidPocket", origin=(0.0, 0.020, 1.470), parent="lid")
def _lid_pocket(p):
    """A flat map pocket on the lid's top face."""
    p.loft([(y, round_rect(-0.180 * s, 0.180 * s, 1.406, 1.406, 0.03, seg=2))
            for y, s in ((-0.120, 0.6), (0.020, 1.0), (0.150, 0.7))],
           axis='Y', mat=CANVAS, cap=True)
    p.slab((-0.190, -0.130, 1.470), (0.190, 0.160, 1.492), CANVAS)
    stitches(p, (-0.180, -0.126, 1.466), (0.180, -0.126, 1.466), 13, RUBBER,
             (0.014, 0.008, 0.008))


@part("Mesh_Pack_LidNet", origin=(0.0, 0.015, 1.548), parent="lid", bevel=0.0)
def _lid_net(p):
    """Net over the lid, so gear rides on top as well as on the front."""
    net(p, (-0.300, -0.150, 1.548), (0.300, 0.180, 1.548), 4, 4, 0.010, RUBBER,
        plane='XY')


@part("Mesh_Pack_LidHardware", origin=(0.0, Y_MOUTH - 0.060, 1.200), parent="lid")
def _lid_hardware(p):
    """Two buckles and their tails, latching the lid down to the panel."""
    for sx in (-1, 1):
        loop_buckle(p, (sx * 0.180, Y_MOUTH - 0.062, 1.176), 0.086, 0.058, 0.013, BRASS)
        ribbon(p, [(sx * 0.180, Y_MOUTH - 0.040, 1.240),
                   (sx * 0.180, Y_MOUTH - 0.058, 1.150)],
               0.070, 0.016, CANVAS, flat='X')


# ---------------------------------------------------------------------------
# Verification
# ---------------------------------------------------------------------------

def dump_sockets(lid_deg=LID_OPEN_DEG, panel_deg=PANEL_OPEN_DEG):
    """Print every socket's world position and mouth direction, closed and open.

    The rule being checked is that local +Y points out of each socket's own
    mouth. Nothing about breaking it is visible in Blender — the pack looks
    right and every item merely lies on its face in game — so it is asserted
    here rather than trusted.
    """
    deps = bpy.context.evaluated_depsgraph_get()
    deps.update()

    pivots = {"Lid": (bpy.data.objects["PIVOT_Lid"], lid_deg),
              "Panel": (bpy.data.objects["PIVOT_Panel"], panel_deg)}

    for state in ("closed", "open"):
        for pivot, deg in pivots.values():
            pivot.rotation_euler = (math.radians(deg) if state == "open" else 0.0, 0.0, 0.0)

        bpy.context.view_layer.update()
        print("  --- sockets, %s ---" % state)

        for obj in sorted(bpy.data.objects, key=lambda o: o.name):
            if not obj.name.startswith("SOCK_"):
                continue
            m = obj.matrix_world
            loc = m.to_translation()
            up = (m.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
            print("    %-13s pos=(%6.3f,%6.3f,%6.3f)  mouth=(%5.2f,%5.2f,%5.2f)"
                  % (obj.name, loc.x, loc.y, loc.z, up.x, up.y, up.z))

    for pivot, _ in pivots.values():
        pivot.rotation_euler = (0.0, 0.0, 0.0)
    bpy.context.view_layer.update()

    pts = [Vector(c) @ o.matrix_world.transposed().to_3x3() + o.matrix_world.to_translation()
           for o in bpy.data.objects if o.type == 'MESH' for c in o.bound_box]
    lo = Vector((min(q.x for q in pts), min(q.y for q in pts), min(q.z for q in pts)))
    hi = Vector((max(q.x for q in pts), max(q.y for q in pts), max(q.z for q in pts)))
    print("  BOUNDS W=%.3f D=%.3f H=%.3f  (%.3f..%.3f, %.3f..%.3f, %.3f..%.3f)"
          % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z, lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_Backpack_Expedition")

    pivot_panel = empty("PIVOT_Panel", PANEL_HINGE, coll, size=0.14)
    pivot_lid = empty("PIVOT_Lid", LID_HINGE, coll, size=0.14)
    hosts = {"panel": (pivot_panel, PANEL_HINGE), "lid": (pivot_lid, LID_HINGE)}

    for name, fn, origin, parent, bevel in PARTS:
        p = Part(mats)
        fn(p)
        if bevel:
            p.bevel(width=bevel)
        obj = p.finish(name, coll, origin=origin)

        if parent is not None:
            host, host_world = hosts[parent]
            attach(obj, host, host_world)

    # Exterior 0-1: on top of the lid, under its net. They ride the lid back as
    # it opens and finish leaning away from the player but still face-up-ish, so
    # gear stays visible and aimable in both states.
    for i, cx in enumerate(LID_CELLS):
        sock = empty("SOCK_Ext_%d" % i, (cx, 0.020, LID_SOCK_Z), coll, rot=ROT_UP)
        attach(sock, pivot_lid, LID_HINGE)

    # Exterior 2-7: the STATIC flanks — the outer face and the top of each side
    # pouch, and each outrigger loop. Six anchors that never move, which is what
    # makes a loaded pack visibly carry gear on the astronaut's back.
    hw_pouch, _, _ = lerp_station(0.33)
    hw_top, _, _ = lerp_station(POUCH_TOP_Z)
    hw_loop, _, _ = lerp_station(0.98)

    for i, sx in enumerate((-1, 1)):
        flank = (ROT_FLANK[0], ROT_FLANK[1], -sx * math.pi / 2.0)
        empty("SOCK_Ext_%d" % (2 + i), (sx * (hw_pouch + 0.140), 0.040, 0.330),
              coll, rot=flank)
        empty("SOCK_Ext_%d" % (4 + i), (sx * (hw_top + 0.036), 0.048, POUCH_TOP_Z + 0.040),
              coll, rot=ROT_UP)
        empty("SOCK_Ext_%d" % (6 + i), (sx * (hw_loop + 0.074), -0.090, 0.958),
              coll, rot=flank)

    # Exterior 8-9: the pair on the front panel, high enough to clear the plate.
    for i, (cx, cz) in enumerate(PANEL_CELLS):
        sock = empty("SOCK_Ext_%d" % (8 + i), (cx, PANEL_SOCK_Y, cz), coll, rot=ROT_FRONT)
        attach(sock, pivot_panel, PANEL_HINGE)

    # Interior 0-8: three across on each of three STATIC shelves.
    for tier, sz in enumerate(SHELF_Z):
        for col, sxx in enumerate(INT_X):
            empty("SOCK_Int_%d" % (tier * len(INT_X) + col),
                  (sxx, INT_Y, sz + 0.010), coll, rot=ROT_UP)

    # Interior 9-11: the brain compartment, revealed by the lid rather than the
    # panel. Static too — the deck they stand on never moves.
    for col, sxx in enumerate(BRAIN_X):
        empty("SOCK_Int_%d" % (9 + col), (sxx, INT_Y, BRAIN_Z), coll, rot=ROT_UP)

    report()
    dump_sockets()
    save(out)


main()
