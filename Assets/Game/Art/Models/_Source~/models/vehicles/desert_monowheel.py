"""Build models/vehicles/desert_monowheel.blend -- the desert monowheel.

    blender --background --python desert_monowheel.py -- --out models/vehicles/desert_monowheel.blend

A sand runner from the concept sheet "Desert monowheel": a rider sits inside a
tall iron ring whose wooden paddles bite the sand as it turns, with a long
sled ski out front and a wooden tail fin behind. Three variations, one
collection each:

    Coll_Monowheel_Runner   sheet design 1 -- one ring, bare ski          (asked for)
    Coll_Monowheel_Hauler   sheet design 2 -- twin rings, rug-wrapped cargo (asked for)
    Coll_Monowheel_Patched  shorter ski, a paddle torn off, rust patches,
                            canvas sail instead of the fin               (built ahead)

## The ring spins, nothing else does

Each variation has an armature with two bones. `Bone_Ring` sits on the hub and
points along +X, so rotating it about its own Y axis spins the wheel. The ring
band is parented to that bone; every other spinning part (paddles, their iron
mounts, patch plates, the second ring on the Hauler) is parented to the ring
band, so the band object alone also spins the whole wheel. Everything static
hangs off `Bone_Chassis`. Rotating +X about the hub rolls the vehicle forward
(-Y).

## Why the frame goes round the side of the ring

The ring is a closed band, so nothing can cross it. The seat cradle lives inside
the ring, rides it on three flanged rollers, and reaches the outside world only
sideways: the two cross tubes run out along X past the paddle sweep to the side
rails at +-cheek. Each side rail is the sheet's one straight diagonal line -- it
leaves the ski's rear, climbs past the wheel at seat height and carries on in the
same line up to the tail panel -- and stays at |x| = cheek, outside the paddle
sweep, the whole way. The ski deck stops short of the ring because the paddles
sweep down to the ground plane.

## Scale

The player is 3 m tall and SITS in the ring, so the ring is 3.9 m across the
paddle tips (hub at 1.95 m) and the seat pan is at hub - 0.45 m: a seated 3 m
body (1.56 m seat-to-crown, from the cushion top) clears the inner band by ~0.25 m.
"""

import math
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))

import bmesh
import bpy
from mathutils import Matrix, Vector

import _buildlib as B


# ── Materials (all from palette.blend) ────────────────────────────────────────
MAT_NAMES = {
    "iron": "Mat_Metal_Steel_Dark",
    "steel": "Mat_Metal_Steel_Worn",
    "rust_deep": "Mat_Metal_Rust_Deep",
    "rust": "Mat_Metal_Rust_Heavy",
    "rust_pale": "Mat_Metal_Rust_Pale",
    "wood": "Mat_Wood_Ply_Worn",
    "timber": "Mat_Wood_Timber_Silvered",
    "red_paint": "Mat_Paint_Warn_Red",
    "seat": "Mat_Fabric_Seat_Ochre",
    "rug": "Mat_Fabric_Sail_Red",
    "rug_border": "Mat_Paint_Butter_Pastel",
    "bundle": "Mat_Fabric_Canvas_Sand",
    "canvas": "Mat_Fabric_Canvas_Faded",
    "rope": "Mat_Fabric_Rope_Hemp",
    "rubber": "Mat_Plastic_Rubber_Black",
}

# ── Wheel ────────────────────────────────────────────────────────────────────
HUB_Z = 1.95            # hub height; paddle tips touch z = 0
BAND_R = 1.70           # ring band outer radius
BAND_DEPTH = 0.13       # radial thickness of the band
BAND_W = 0.18           # band width along the axle (X)
BAND_SEG = 72
SIDE_BOLTS = 24
SIDE_BOLT_R = 1.645     # kept outside the roller flanges' reach (1.606)

BRACKET_R = (1.68, 1.85)    # paddle bracket, embedded in band and board
BOARD_R = (1.84, 1.94)      # paddle board radial extent
BOARD_CHORD = 0.46          # board length around the ring
BOARD_OVERHANG = 0.33       # board reaches this far past each ring's centre in X
LIP_R = (1.80, 1.95)        # iron scoop lip on the board's leading edge
PADDLE_TIP_R = 1.97         # nothing static may enter this radius at |x| < board

ROLLER_R = 0.12
ROLLER_CENTRE_R = BAND_R - BAND_DEPTH - ROLLER_R - 0.004
ROLLER_FLANGE_R = 0.16
ROLLER_ANGLES = (150.0, -150.0, -60.0)   # front-bottom, rear-bottom, rear-top
ROLLER_ARM_X = 0.15                      # arms sit this far either side of a ring

# ── Chassis (y, z) — the sheet's one straight diagonal line ──────────────
# Each side rail is a single straight bar: it leaves the ski's rear, climbs past
# the wheel at seat height and carries on in the same line up to the tail panel.
SPINE_FOOT_BACK = 0.9   # the rail lands on the ski this far ahead of the ski's rear end
SPINE_FOOT_Z = 0.16     # sunk into the ski's side rail
LOWER_TUBE_Y = -0.35    # cross tubes hang the cradle off the rails at these stations
REAR_TUBE_Y = 1.0
TUBE_R = 0.055
BAR_R = 0.045
FIN_Y = 2.80            # tail panel, behind the ring as on the sheet
TAIL_BAR_Y = FIN_Y + 0.1    # where the rails end and the panel is clamped
FIN_LOW_Z = HUB_Z + 0.75    # the rails' upper end
FIN_HIGH_Z = HUB_Z + 1.30

SEAT_Z = HUB_Z - 0.45
RECLINE = math.radians(5)

# ── Ski ──────────────────────────────────────────────────────────────────────
SKI_Z = 0.06            # deck mid-plane; deck is 0.03 .. 0.09
DECK_T = 0.06
CURL_R = 0.42
CURL_ANGLE = math.radians(115)
STEP = 0.12
RAIL_T = 0.16           # side rails stand proud of the deck as a raised rim
RAIL_RISE = 0.04

VARIANTS = [
    dict(name="Runner", rings=[0.0], cheek=0.44, ski_half=0.45, ski_start=-0.95,
         ski_len=5.6, planks=["red_paint", "red_paint", "wood", "red_paint", "red_paint"],
         board_mats=["wood"], missing_paddle=None, tail="fin", cargo=False,
         ornaments=[1.0, 2.8], patches=False),
    dict(name="Hauler", rings=[-0.40, 0.40], cheek=0.84, ski_half=0.86, ski_start=-1.30,
         ski_len=6.6, planks=["red_paint", "wood", "red_paint", "red_paint", "wood", "red_paint"],
         board_mats=["wood"], missing_paddle=None, tail="fin", cargo=True,
         ornaments=[0.6, 4.1], patches=False),
    dict(name="Patched", rings=[0.0], cheek=0.44, ski_half=0.42, ski_start=-0.95,
         ski_len=4.2, planks=["wood", "timber", "wood", "timber"],
         board_mats=["wood", "timber", "wood", "steel", "timber", "wood", "timber", "wood"],
         missing_paddle=5, tail="sail", cargo=False, ornaments=[], patches=True),
]


# ── Helpers ──────────────────────────────────────────────────────────────────

def part(*keys):
    """A Part whose material slots are the named palette entries, in order."""
    p = B.Part([M[k] for k in keys])
    p.keys = list(keys)
    return p


def slot(p, key):
    return p.keys.index(key)


def bar(p, a, b, r=BAR_R, mat=0, ring=8):
    """A round bar between two points, domed so joints overlap instead of gapping."""
    return p.segment(a, b, [(0.0, r, r), (1.0, r, r)], mat=mat, ring=ring)


def chain(p, pts, r, mat=0, ring=6):
    for a, b in zip(pts, pts[1:]):
        if (Vector(b) - Vector(a)).length > 1e-4:
            bar(p, a, b, r, mat, ring)


def spiral(centre, u, v, r_out, r_in, turns, start, steps_per_turn=16):
    """Flat scroll: points from the outer end inward, in the plane of u, v."""
    centre, u, v = Vector(centre), Vector(u), Vector(v)
    n = max(4, int(turns * steps_per_turn))
    pts = []
    for i in range(n + 1):
        t = i / n
        a = start + t * turns * 2 * math.pi
        r = r_out + (r_in - r_out) * t
        pts.append(centre + u * (r * math.cos(a)) + v * (r * math.sin(a)))
    return pts


def spin(p, degrees):
    """Rotate a part built at the top of the wheel round the axle (X)."""
    bmesh.ops.rotate(p.bm, cent=(0.0, 0.0, HUB_Z),
                     matrix=Matrix.Rotation(math.radians(degrees), 3, 'X'),
                     verts=p.bm.verts)


def wheel_point(x, radius, degrees):
    """A point on the wheel, 0 deg at the top, +deg rolling toward -Y."""
    a = math.radians(degrees)
    return Vector((x, -radius * math.sin(a), HUB_Z + radius * math.cos(a)))


def yz(x, pt):
    return Vector((x, pt[0], pt[1]))


def to_bone(obj, arm, bone):
    b = arm.data.bones[bone]
    rest = arm.matrix_world @ b.matrix_local @ Matrix.Translation((0.0, b.length, 0.0))
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone
    obj.matrix_parent_inverse = rest.inverted()


def to_object(obj, parent):
    obj.parent = parent
    obj.matrix_parent_inverse = parent.matrix_world.inverted()


# ── Ski path ─────────────────────────────────────────────────────────────────

class Ski:
    """The ski's centreline: straight from the rear, then curling up at the tip."""

    def __init__(self, v):
        self.y0 = v["ski_start"]
        self.length = v["ski_len"]
        self.half = v["ski_half"]
        self.curl = CURL_R * CURL_ANGLE
        self.straight = self.length - self.curl

    def at(self, s):
        if s <= self.straight:
            return Vector((0.0, self.y0 - s, SKI_Z)), Vector((0.0, 0.0, 1.0))
        phi = (s - self.straight) / CURL_R
        p = Vector((0.0, self.y0 - self.straight - CURL_R * math.sin(phi),
                    SKI_Z + CURL_R * (1.0 - math.cos(phi))))
        return p, Vector((0.0, math.sin(phi), math.cos(phi)))

    def stations(self, sa, sb):
        n = max(2, math.ceil((sb - sa) / STEP))
        return [sa + (sb - sa) * i / n for i in range(n + 1)]

    def rows(self, sa, sb, xs, off=0.0):
        out = []
        for s in self.stations(sa, sb):
            p, n = self.at(s)
            out.append([Vector((x, p.y + n.y * off, p.z + n.z * off)) for x in xs])
        return out


# ── Wheel parts ──────────────────────────────────────────────────────────────

def build_ring(v, coll, rx, tag):
    p = part("iron")
    p.tube((rx, 0.0, HUB_Z), BAND_R, BAND_DEPTH, BAND_W, axis='X', seg=BAND_SEG)
    p.bevel(width=0.01, segments=1)
    for side in (-1, 1):
        x = rx + side * (BAND_W / 2 + 0.004)
        for i in range(SIDE_BOLTS):
            c = wheel_point(x, SIDE_BOLT_R, 360.0 * (i + 0.5) / SIDE_BOLTS)
            p.cyl(c, 0.016, 0.014, axis='X', seg=6, radius_top=0.011)
    return p.finish("Mesh_Ring%s_%s" % (tag, v["name"]), coll, origin=(rx, 0.0, HUB_Z))


def build_paddle(v, coll, idx, degrees):
    x_lo = min(v["rings"]) - BOARD_OVERHANG
    x_hi = max(v["rings"]) + BOARD_OVERHANG
    xc, width = (x_lo + x_hi) / 2, x_hi - x_lo
    torn = idx == v["missing_paddle"]

    mount = part("iron", "steel")
    for rx in v["rings"]:
        mount.box((rx, 0.0, HUB_Z + sum(BRACKET_R) / 2),
                  (0.12, 0.10, BRACKET_R[1] - BRACKET_R[0]))
    if not torn:
        mount.box((xc, 0.0, HUB_Z + 1.8275), (width - 0.10, 0.06, 0.035))
        mount.box((xc, -BOARD_CHORD / 2 - 0.005, HUB_Z + sum(LIP_R) / 2),
                  (width, 0.02, LIP_R[1] - LIP_R[0]), mat=slot(mount, "steel"))
    mount.bevel(width=0.006, segments=1)
    spin(mount, degrees)
    objs = [mount.finish("Mesh_PaddleMount%02d_%s" % (idx, v["name"]), coll,
                         origin=wheel_point(xc, BAND_R, degrees))]

    if not torn:
        mat_key = v["board_mats"][idx % len(v["board_mats"])]
        board = part(mat_key, "iron")
        board.box((xc, 0.0, HUB_Z + sum(BOARD_R) / 2),
                  (width, BOARD_CHORD, BOARD_R[1] - BOARD_R[0]))
        board.bevel(width=0.022 if mat_key != "steel" else 0.006, segments=2)
        for rx in v["rings"]:
            for dy in (-0.13, 0.13):
                board.cyl((rx, dy, HUB_Z + BOARD_R[1] + 0.003), 0.026, 0.014,
                          seg=6, mat=1, radius_top=0.015)
        spin(board, degrees)
        objs.append(board.finish("Mesh_Paddle%02d_%s" % (idx, v["name"]), coll,
                                 origin=wheel_point(xc, BOARD_R[0], degrees)))
    return objs


def build_ties(v, coll, count):
    """Hauler only: bars tying the two rings together between the paddles."""
    p = part("iron")
    x = max(v["rings"]) - 0.03
    for i in range(count):
        deg = 360.0 * (i + 0.5) / count
        bar(p, wheel_point(-x, 1.68, deg), wheel_point(x, 1.68, deg), r=0.02)
    return p.finish("Mesh_RingTies_%s" % v["name"], coll, origin=(0.0, 0.0, HUB_Z))


def build_patches(v, coll):
    objs = []
    for i, (deg, side) in enumerate(((40.0, 1), (170.0, -1), (250.0, 1))):
        p = part("rust_pale", "iron")
        x = side * (BAND_W / 2 + 0.008)
        r = 1.6825
        p.box((x, 0.0, HUB_Z + r), (0.016, 0.30, 0.075))
        for dy in (-0.12, 0.12):
            p.cyl((x + side * 0.009, dy, HUB_Z + r), 0.013, 0.01, axis='X', seg=6, mat=1)
        spin(p, deg)
        objs.append(p.finish("Mesh_RingPatch%02d_%s" % (i, v["name"]), coll,
                             origin=wheel_point(x, r, deg)))
    return objs


def build_roller(v, coll, rx, idx, degrees):
    p = part("rubber", "iron")
    top = HUB_Z + ROLLER_CENTRE_R
    p.cyl((rx, 0.0, top), ROLLER_R, BAND_W - 0.01, axis='X', seg=16)
    for side in (-1, 1):
        fx = rx + side * (BAND_W / 2 + 0.015)
        p.cyl((fx, 0.0, top), ROLLER_FLANGE_R, 0.02, axis='X', seg=20, mat=1)
        for t in range(12):
            a = 2 * math.pi * t / 12
            p.box((fx, math.sin(a) * (ROLLER_FLANGE_R - 0.005),
                   top + math.cos(a) * (ROLLER_FLANGE_R - 0.005)),
                  (0.018, 0.035, 0.03), mat=1,
                  rot=Matrix.Rotation(-a, 4, 'X'))
    p.cyl((rx, 0.0, top), 0.03, 2 * ROLLER_ARM_X + 0.03, axis='X', seg=10, mat=1)
    spin(p, degrees)
    return p.finish("Mesh_Roller%s_%s" % (idx, v["name"]), coll,
                    origin=wheel_point(rx, ROLLER_CENTRE_R, degrees))


def build_roller_arms(v, coll, rx, idx, degrees):
    p = part("iron")
    anchor = v["rear"] if degrees == -60.0 else v["lower"]
    for side in (-1, 1):
        x = rx + side * ROLLER_ARM_X
        bar(p, wheel_point(x, ROLLER_CENTRE_R, degrees), yz(x, anchor), r=0.03)
    return p.finish("Mesh_RollerArm%s_%s" % (idx, v["name"]), coll,
                    origin=yz(rx, anchor))


# ── Chassis ──────────────────────────────────────────────────────────────────

def build_tubes(v, coll):
    c = v["cheek"]
    objs = []
    for label, pt in (("Lower", v["lower"]), ("Rear", v["rear"])):
        p = part("iron")
        bar(p, yz(-c, pt), yz(c, pt), r=TUBE_R, ring=12)
        objs.append(p.finish("Mesh_Tube%s_%s" % (label, v["name"]), coll, origin=yz(0.0, pt)))
    return objs


def spine_z(v, y):
    """Height of the straight side rail at station y."""
    (y0, z0), (y1, z1) = v["spine_foot"], (TAIL_BAR_Y, FIN_LOW_Z)
    return z0 + (z1 - z0) * (y - y0) / (y1 - y0)


def on_spine(v, x, y):
    return Vector((x, y, spine_z(v, y)))


def build_spine(v, coll, side, ski):
    """One straight rail per side, ski to tail panel, clear of the paddle sweep at |x| = cheek."""
    c = side * v["cheek"]
    p = part("iron")
    y0 = v["spine_foot"][0]
    bar(p, on_spine(v, c, y0), on_spine(v, c, TAIL_BAR_Y), r=TUBE_R, ring=12)
    tag = "L" if side < 0 else "R"
    return p.finish("Mesh_Spine%s_%s" % (tag, v["name"]), coll, origin=on_spine(v, c, 0.0))


def build_cheek(v, coll, side, ski):
    """Braces and scrollwork hung off one rail: a truss down to the ski's rear, a
    strut up to the panel's top clamp."""
    c = side * v["cheek"]
    p = part("iron")
    ski_end = Vector((c, ski.y0 - 0.02, SPINE_FOOT_Z))
    brace_top = on_spine(v, c, ski.y0 + 0.1)
    strut_foot = on_spine(v, c, 1.9)
    panel_top = Vector((c, TAIL_BAR_Y, FIN_HIGH_Z))
    bar(p, brace_top, ski_end)
    bar(p, ski_end, Vector((c, ski.y0 - 0.5, SPINE_FOOT_Z)))
    bar(p, strut_foot, panel_top)
    u, w = Vector((0, 1, 0)), Vector((0, 0, 1))
    for centre, r_out, anchor in (((ski.y0 - 0.32, SPINE_FOOT_Z + 0.2), 0.13, ski_end),
                                  ((ski.y0 - 1.25, SPINE_FOOT_Z + 0.2), 0.14,
                                   on_spine(v, c, ski.y0 - 0.75)),
                                  ((2.35, HUB_Z + 0.95), 0.2, strut_foot)):
        pts = spiral(yz(c, centre), u, w, r_out, 0.035, 1.4, math.pi * 1.25)
        chain(p, [anchor] + pts, 0.022)
    tag = "L" if side < 0 else "R"
    return p.finish("Mesh_Cheek%s_%s" % (tag, v["name"]), coll, origin=ski_end)


def build_seat(v, coll):
    objs = []
    pan = part("wood", "iron")
    pan.box((0.0, 0.15, SEAT_Z + 0.02), (0.90, 0.80, 0.12))
    pan.bevel(width=0.015)
    objs.append(pan.finish("Mesh_SeatPan_%s" % v["name"], coll, origin=(0.0, 0.15, SEAT_Z)))

    cushion = part("seat")
    cushion.box((0.0, 0.15, SEAT_Z + 0.13), (0.82, 0.74, 0.16))
    cushion.bevel(width=0.045, segments=3, angle=30.0)
    objs.append(cushion.finish("Mesh_SeatCushion_%s" % v["name"], coll,
                               origin=(0.0, 0.15, SEAT_Z + 0.05)))

    tilt = Matrix.Rotation(-RECLINE, 4, 'X')
    back_c = Vector((0.0, 0.55 + 0.625 * math.sin(RECLINE), SEAT_Z + 0.05 + 0.625 * math.cos(RECLINE)))
    forward = (tilt.to_3x3() @ Vector((0.0, -1.0, 0.0))).normalized()
    back = part("wood")
    back.box(back_c, (0.86, 0.09, 1.25), rot=tilt)
    back.bevel(width=0.015)
    objs.append(back.finish("Mesh_Backrest_%s" % v["name"], coll,
                            origin=(0.0, 0.55, SEAT_Z + 0.05)))

    pad = part("seat")
    pad.box(back_c + forward * 0.08, (0.76, 0.10, 1.08), rot=tilt)
    pad.bevel(width=0.035, segments=3, angle=30.0)
    objs.append(pad.finish("Mesh_BackCushion_%s" % v["name"], coll,
                           origin=back_c + forward * 0.08))

    posts = part("iron")
    for x in (-0.32, 0.32):
        for y in (-0.2, 0.45):
            bar(posts, yz(x, v["lower"]), (x, y, SEAT_Z - 0.03), r=0.032)
        bar(posts, (x, 0.62, v["rear"][1]), yz(x, v["rear"]), r=0.032)
    objs.append(posts.finish("Mesh_SeatPosts_%s" % v["name"], coll, origin=yz(0.0, v["lower"])))

    tiller = part("iron")
    top = Vector((0.0, -0.85, HUB_Z + 0.05))
    bar(tiller, yz(0.0, v["lower"]), top, r=0.045)
    bar(tiller, top + Vector((-0.5, 0, 0)), top + Vector((0.5, 0, 0)), r=0.035)
    objs.append(tiller.finish("Mesh_Tiller_%s" % v["name"], coll, origin=yz(0.0, v["lower"])))

    grips = part("wood")
    for x in (-0.4, 0.4):
        grips.cyl(top + Vector((x, 0, 0)), 0.055, 0.22, axis='X', seg=10)
    objs.append(grips.finish("Mesh_TillerGrips_%s" % v["name"], coll, origin=top))

    foot = part("iron")
    rest = (-1.1, v["lower"][1] + 0.1)
    bar(foot, yz(-0.42, rest), yz(0.42, rest), r=0.04)
    for x in (-0.36, 0.36):
        bar(foot, yz(x, v["lower"]), yz(x, rest), r=0.032)
    objs.append(foot.finish("Mesh_Footrest_%s" % v["name"], coll, origin=yz(0.0, v["lower"])))

    socket = bpy.data.objects.new("Socket_Rider_%s" % v["name"], None)
    socket.empty_display_type = 'ARROWS'
    socket.empty_display_size = 0.3
    socket.location = (0.0, 0.15, SEAT_Z + 0.21)
    coll.objects.link(socket)
    objs.append(socket)
    return objs


# (y offset from FIN_Y, z offset from the hub)
FIN_PROFILE = [(-0.23, -0.10), (0.05, -0.15), (0.30, 0.40), (0.47, 1.20), (0.45, 1.55),
               (0.30, 1.70), (0.10, 1.65), (-0.05, 1.20), (-0.20, 0.50)]


def build_tail(v, coll):
    c = v["cheek"]
    objs = []
    mount = part("iron")
    for z in (FIN_LOW_Z, FIN_HIGH_Z):
        bar(mount, (-c, TAIL_BAR_Y, z), (c, TAIL_BAR_Y, z))
    for x in (-c, c):
        bar(mount, (x, TAIL_BAR_Y, FIN_LOW_Z), (x, TAIL_BAR_Y, FIN_HIGH_Z))
    objs.append(mount.finish("Mesh_TailMount_%s" % v["name"], coll,
                             origin=(0.0, TAIL_BAR_Y, FIN_LOW_Z)))

    if v["tail"] == "fin":
        fin = part("wood")
        fin.prism([(FIN_Y + y, HUB_Z + z) for y, z in reversed(FIN_PROFILE)], 0.06, axis='X')
        fin.bevel(width=0.012)
        objs.append(fin.finish("Mesh_TailFin_%s" % v["name"], coll, origin=(0.0, FIN_Y, FIN_LOW_Z)))

        trim = part("iron")
        edge = [Vector((0.0, FIN_Y + y, HUB_Z + z)) for y, z in FIN_PROFILE + FIN_PROFILE[:1]]
        chain(trim, edge, 0.024, ring=8)
        for x in (-0.034, 0.034):
            pts = spiral((x, FIN_Y + 0.15, HUB_Z + 1.05), (0, 1, 0), (0, 0, 1), 0.2, 0.035, 1.5, math.pi * 1.5)
            chain(trim, [Vector((x, FIN_Y + 0.05, HUB_Z + 0.2))] + pts, 0.018)
        objs.append(trim.finish("Mesh_TailFinTrim_%s" % v["name"], coll,
                                origin=(0.0, FIN_Y, FIN_LOW_Z)))
    else:
        poles = part("timber", "rope")
        a0, a1 = Vector((0.0, TAIL_BAR_Y, FIN_LOW_Z - 0.05)), Vector((0.0, TAIL_BAR_Y + 0.02, HUB_Z + 2.3))
        b0, b1 = Vector((0.0, TAIL_BAR_Y, FIN_HIGH_Z)), Vector((0.0, TAIL_BAR_Y + 0.75, HUB_Z + 2.0))
        bar(poles, a0, a1, r=0.035)
        bar(poles, b0, b1, r=0.03)
        for pt, r in ((FIN_LOW_Z, 0.05), (FIN_HIGH_Z, 0.05)):
            poles.torus((0.0, TAIL_BAR_Y, pt), r, 0.012, axis='Y', maj_seg=12, min_seg=6, mat=1)
        objs.append(poles.finish("Mesh_SailPoles_%s" % v["name"], coll, origin=a0))

        sail = part("canvas")
        rows = []
        for i in range(9):
            t = i / 8
            top, bot = a1.lerp(b1, t), a0.lerp(b0, t) + Vector((0, 0.1 * t, 0.3 + 0.2 * t))
            rows.append([top.lerp(bot, k / 6) + Vector((0.06 * math.sin(math.pi * k / 6)
                                                        * math.sin(math.pi * t), 0.0, 0.0))
                         for k in range(7)])
        sail.sheet(rows, 0.012)
        objs.append(sail.finish("Mesh_Sail_%s" % v["name"], coll, origin=a1))
    return objs


# ── Ski ──────────────────────────────────────────────────────────────────────

def build_ski(v, coll, ski):
    objs = []
    n = len(v["planks"])
    gap = 0.012
    w = (2 * ski.half - gap * (n - 1)) / n
    keys = sorted(set(v["planks"]))
    deck = part(*keys)
    for k, key in enumerate(v["planks"]):
        x0 = -ski.half + k * (w + gap)
        stagger = (0.0, 0.05, 0.02, 0.07, 0.03, 0.06)[k % 6]
        deck.sheet(ski.rows(stagger, ski.length - 0.01, [x0, x0 + w]), DECK_T,
                   mat=slot(deck, key), smooth=False)
    objs.append(deck.finish("Mesh_SkiDeck_%s" % v["name"], coll, origin=ski.at(0.0)[0]))

    rails = part("iron")
    for side in (-1, 1):
        xs = sorted([side * (ski.half - 0.05), side * (ski.half + 0.006)])
        rails.sheet(ski.rows(0.0, ski.length, xs, off=RAIL_RISE), RAIL_T, smooth=False)
    objs.append(rails.finish("Mesh_SkiRails_%s" % v["name"], coll, origin=ski.at(0.0)[0]))

    straps = part("iron")
    s = 0.3
    while s < ski.length - 0.2:
        straps.sheet(ski.rows(s - 0.03, s + 0.03, [-ski.half - 0.01, ski.half + 0.01]),
                     0.075, smooth=False)
        p, nrm = ski.at(s)
        rot = nrm.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        for x in (-(ski.half - 0.07), 0.0, ski.half - 0.07):
            straps.cyl(p + Vector((x, 0, 0)) + nrm * 0.04, 0.016, 0.012, seg=6,
                       radius_top=0.011, rot=rot)
        s += 0.9 if v["name"] != "Patched" else 1.3
    objs.append(straps.finish("Mesh_SkiStraps_%s" % v["name"], coll, origin=ski.at(0.0)[0]))

    runners = part("rust_deep")
    for side in (-1, 1):
        xc = side * (ski.half - 0.14)
        runners.sheet(ski.rows(0.0, ski.length - 0.15, [xc - 0.03, xc + 0.03], off=-0.04),
                      0.04, smooth=False)
    objs.append(runners.finish("Mesh_SkiRunners_%s" % v["name"], coll, origin=ski.at(0.0)[0]))

    if v["ornaments"]:
        orn = part("iron")
        z = SKI_Z + DECK_T / 2 + 0.01
        for s0 in v["ornaments"]:
            yc = ski.at(s0)[0].y
            for side in (-1, 1):
                pts = spiral((side * 0.16, yc, z), (side, 0, 0), (0, 1, 0), 0.12, 0.03, 1.3, math.pi)
                chain(orn, [Vector((0.0, yc + 0.28, z))] + pts, 0.016)
                pts = spiral((side * 0.16, yc - 0.34, z), (side, 0, 0), (0, -1, 0), 0.09, 0.025, 1.1, math.pi)
                chain(orn, [Vector((0.0, yc - 0.1, z))] + pts, 0.014)
        objs.append(orn.finish("Mesh_SkiScrolls_%s" % v["name"], coll,
                               origin=ski.at(v["ornaments"][0])[0]))

    if v["patches"]:
        patch = part("rust", "iron")
        patch.sheet(ski.rows(1.4, 2.1, [-0.30, 0.08]), DECK_T + 0.012, smooth=False)
        for s1 in (1.47, 2.03):
            p, nrm = ski.at(s1)
            for x in (-0.26, 0.04):
                patch.cyl(p + Vector((x, 0, 0)) + nrm * 0.038, 0.014, 0.01, seg=6, mat=1)
        objs.append(patch.finish("Mesh_SkiPatch_%s" % v["name"], coll, origin=ski.at(1.4)[0]))
    return objs


def build_cargo(v, coll, ski):
    objs = []
    yc = ski.y0 - 2.1
    deck_top = SKI_Z + DECK_T / 2
    hx, hz = 0.525, 0.30
    zc = deck_top + hz - 0.01

    bundle = part("bundle")
    bundle.box((0.0, yc, zc), (2 * hx, 1.2, 2 * hz))
    bundle.bevel(width=0.1, segments=3)
    objs.append(bundle.finish("Mesh_CargoBundle_%s" % v["name"], coll,
                              origin=(0.0, yc, deck_top)))

    def drape(off, t_lo=0.0, t_hi=1.0, samples=24):
        """Cross-section over the bundle: down the -X side, over, down the +X side."""
        r = 0.12
        path = [(-hx - off, deck_top + 0.13), (-hx - off, zc + hz - r)]
        for k in range(1, 6):
            a = math.pi - k * (math.pi / 2) / 6
            path.append((-hx + r + (r + off) * math.cos(a), zc + hz - r + (r + off) * math.sin(a)))
        path += [(-hx + r, zc + hz + off), (hx - r, zc + hz + off)]
        for k in range(1, 6):
            a = math.pi / 2 - k * (math.pi / 2) / 6
            path.append((hx - r + (r + off) * math.cos(a), zc + hz - r + (r + off) * math.sin(a)))
        path += [(hx + off, zc + hz - r), (hx + off, deck_top + 0.13)]
        seg = [(Vector(a) - Vector(b)).length for a, b in zip(path, path[1:])]
        total = sum(seg)
        out = []
        for i in range(samples + 1):
            d = total * (t_lo + (t_hi - t_lo) * i / samples)
            for (a, b), L in zip(zip(path, path[1:]), seg):
                if d <= L or (a, b) == (path[-2], path[-1]):
                    q = Vector(a).lerp(Vector(b), min(1.0, d / L))
                    out.append(q)
                    break
                d -= L
        return out

    def rug_rows(off, t_lo, t_hi, samples):
        rows = []
        for j in range(12):
            y = yc - 0.65 + 1.3 * j / 11
            wob = 0.012 * math.sin(j * 1.7)
            rows.append([Vector((q.x + (wob if q.x > 0 else -wob), y, q.y))
                         for q in drape(off, t_lo, t_hi, samples)])
        return rows

    rug = part("rug")
    rug.sheet(rug_rows(0.025, 0.0, 1.0, 30), 0.02)
    objs.append(rug.finish("Mesh_CargoRug_%s" % v["name"], coll, origin=(0.0, yc, zc + hz)))

    border = part("rug_border")
    for lo, hi in ((0.03, 0.06), (0.94, 0.97), (0.46, 0.54)):
        border.sheet(rug_rows(0.025, lo, hi, 3), 0.028)
    objs.append(border.finish("Mesh_CargoRugBorder_%s" % v["name"], coll,
                              origin=(0.0, yc, zc + hz)))

    fringe = part("rope")
    for j in range(22):
        y = yc - 0.63 + 1.26 * j / 21
        for side in (-1, 1):
            x = side * (hx + 0.025)
            fringe.cyl((x, y, deck_top + 0.075), 0.008, 0.11, seg=5)
    objs.append(fringe.finish("Mesh_CargoRugFringe_%s" % v["name"], coll,
                              origin=(0.0, yc, deck_top)))

    lash = part("rope")
    for dy in (-0.4, 0.4):
        loop = [Vector((q.x, yc + dy, q.y)) for q in drape(0.047, 0.0, 1.0, 26)]
        loop.append(Vector((0.0, yc + dy, deck_top + 0.02)))
        loop.append(loop[0])
        chain(lash, loop, 0.02)
    objs.append(lash.finish("Mesh_CargoLashing_%s" % v["name"], coll, origin=(0.0, yc, deck_top)))

    roll_c = Vector((0.0, yc + 0.1, zc + hz + 0.035 + 0.15))
    roll = part("canvas")
    roll.cyl(roll_c, 0.16, 0.9, axis='X', seg=16)
    roll.bevel(width=0.03, segments=2)
    objs.append(roll.finish("Mesh_CargoBedroll_%s" % v["name"], coll, origin=roll_c))

    ties = part("rope")
    for x in (-0.3, 0.3):
        ties.torus(roll_c + Vector((x, 0, 0)), 0.162, 0.016, axis='X', maj_seg=18, min_seg=6)
    objs.append(ties.finish("Mesh_CargoBedrollTies_%s" % v["name"], coll, origin=roll_c))
    return objs


# ── Assembly ─────────────────────────────────────────────────────────────────

def build_armature(v, coll):
    name = "Arm_Monowheel_%s" % v["name"]
    arm = bpy.data.objects.new(name, bpy.data.armatures.new(name))
    coll.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    chassis = arm.data.edit_bones.new("Bone_Chassis")
    chassis.head, chassis.tail = (0.0, 0.0, 0.0), (0.0, 0.0, 0.6)
    ring = arm.data.edit_bones.new("Bone_Ring")
    ring.head, ring.tail = (0.0, 0.0, HUB_Z), (0.5, 0.0, HUB_Z)
    ring.parent = chassis
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def build_variant(v):
    coll = B.collection("Coll_Monowheel_%s" % v["name"])
    arm = build_armature(v, coll)
    ski = Ski(v)
    v["spine_foot"] = (ski.y0 - SPINE_FOOT_BACK, SPINE_FOOT_Z)
    v["lower"] = (LOWER_TUBE_Y, spine_z(v, LOWER_TUBE_Y))
    v["rear"] = (REAR_TUBE_Y, spine_z(v, REAR_TUBE_Y))

    tags = [""] if len(v["rings"]) == 1 else ["L", "R"]
    rings = [build_ring(v, coll, rx, tag) for rx, tag in zip(v["rings"], tags)]
    spinning = rings[1:]
    for i in range(8):
        spinning += build_paddle(v, coll, i, 360.0 * i / 8)
    if len(v["rings"]) > 1:
        spinning.append(build_ties(v, coll, 8))
    if v["patches"]:
        spinning += build_patches(v, coll)

    static = []
    for ri, rx in enumerate(v["rings"]):
        for ai, deg in enumerate(ROLLER_ANGLES):
            idx = "%s%d" % (tags[ri], ai)
            static.append(build_roller(v, coll, rx, idx, deg))
            static.append(build_roller_arms(v, coll, rx, idx, deg))
    static += build_tubes(v, coll)
    static += [build_spine(v, coll, s, ski) for s in (-1, 1)]
    static += [build_cheek(v, coll, s, ski) for s in (-1, 1)]
    static += build_seat(v, coll)
    static += build_tail(v, coll)
    static += build_ski(v, coll, ski)
    if v["cargo"]:
        static += build_cargo(v, coll, ski)

    to_bone(rings[0], arm, "Bone_Ring")
    for o in static:
        to_bone(o, arm, "Bone_Chassis")
    bpy.context.view_layer.update()
    for o in spinning:
        to_object(o, rings[0])
    return coll


if __name__ == "__main__":
    out = B.parse_out()
    B.start(out)
    M = dict(zip(MAT_NAMES, B.link_materials(list(MAT_NAMES.values()))))
    colls = [build_variant(v) for v in VARIANTS]
    layer = bpy.context.view_layer.layer_collection
    for c in colls[1:]:
        layer.children[c.name].hide_viewport = True
    B.save(out)
