"""components/mechanical/track_bogie — the running gear between hull and track.

What hangs the road wheels off the crawler: a girder the length of the track
frame, the swing arms that carry the wheels, the leaf springs that load them,
and the riveted gear boxes that sit on the frame between them. Each is its own
part because each is placed on its own: the girder chains, the arms repeat per
wheel station, the springs and boxes go wherever the frame needs them.

Origins, per part:
- Bogie_Beam: one end, centred in X and Z; the girder runs along -Y, so
  segments chain by translating 2.0 m in -Y.
- Bogie_SwingArm: the pivot axis (along X); the wheel axle is 1.0 m along -Y.
- Bogie_LeafSpring: the centre of the leaf pack at the centre clamp.
- Bogie_GearHousing: the bottom centre, where it bolts to the frame.

+X is the show face — the side that faces outwards on the starboard track.
Mirror in X for the port side.

    blender --background --python track_bogie.py -- --out track_bogie.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bmesh

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402
from _track_kit import EMBED, bolt, drop  # noqa: E402

MATS = [
    "Mat_Metal_HullRust_Orange",  # 0 girders, arms, housings
    "Mat_Metal_Rust_Deep",        # 1 flanges, rivets
    "Mat_Metal_Steel_Dark",       # 2 bosses, covers
    "Mat_Metal_Steel_Worn",       # 3 pins, axles, spring leaves
    "Mat_Metal_Rust_Heavy",       # 4 heavy scale
    "Mat_Metal_Brass_Tarnished",  # 5 bolts
    "Mat_Neutral_Black_Matte",    # 6 bore of a lightening hole
]
HULL, DEEP, DARK, WORN, RUST, BRASS, BLACK = range(7)

# Directions the eight points of a lightening-hole cell sit at, starting at
# the bottom-left corner and running round: corner, edge, corner, ...
_CELL_DIRS = [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)]


def perforated_web(p, half_x, y0, y1, half_z, cells, hole_r, mat, bore_mat):
    """A solid block with round lightening holes punched through it along X.

    Built as quads rather than with a boolean: each hole sits in a square cell
    whose eight edge points bridge straight to an octagon, so a hole costs 48
    triangles through both walls and its bore, exactly, with no slivers. The
    block's top and bottom faces are left out — the caller buries them in the
    flanges. Cells are centred on `cells` (y values), `half_z` square.

    One bmesh with shared vertices, so `finish()` can orient every face from
    the connected shell; separate sheets would each be a coin toss.
    """
    h = half_z
    bm2 = bmesh.new()
    cache = {}

    def vert(x, y, z):
        key = (round(x, 5), round(y, 5), round(z, 5))
        if key not in cache:
            cache[key] = bm2.verts.new(key)
        return cache[key]

    def quad(pts):
        bm2.faces.new([vert(*q) for q in pts])

    def hole_pt(x, yc, k):
        du, dv = _CELL_DIRS[k]
        n = math.hypot(du, dv)
        return (x, yc + hole_r * du / n, hole_r * dv / n)

    lines = [y1] + [e for yc in sorted(cells, reverse=True) for e in (yc + h, yc - h)] + [y0]
    for x in (-half_x, half_x):
        for a, b in zip(lines[0::2], lines[1::2]):          # plain strips
            if abs(a - b) > 1e-6:
                quad([(x, a, -h), (x, b, -h), (x, b, 0), (x, a, 0)])
                quad([(x, a, 0), (x, b, 0), (x, b, h), (x, a, h)])
        for yc in cells:                                      # holed cells
            for i in range(8):
                j = (i + 1) % 8
                (ui, vi), (uj, vj) = _CELL_DIRS[i], _CELL_DIRS[j]
                quad([(x, yc + ui * h, vi * h), (x, yc + uj * h, vj * h),
                      hole_pt(x, yc, j), hole_pt(x, yc, i)])
    for yc in cells:                                          # bores
        for i in range(8):
            j = (i + 1) % 8
            quad([hole_pt(-half_x, yc, i), hole_pt(-half_x, yc, j),
                  hole_pt(half_x, yc, j), hole_pt(half_x, yc, i)])
    for y in (y0, y1):                                        # end faces
        for za, zb in ((-h, 0), (0, h)):
            quad([(-half_x, y, za), (-half_x, y, zb), (half_x, y, zb), (half_x, y, za)])
    faces = p._absorb(bm2, mat)
    for f in faces:                                           # bores: span X, not at an end
        xs = {round(v.co.x, 4) for v in f.verts}
        ys = {round(v.co.y, 4) for v in f.verts}
        if len(xs) == 2 and not ys & {round(y0, 4), round(y1, 4)}:
            f.material_index = bore_mat
    return faces


def build_beam(coll):
    """A 2 m box-girder segment: flanged top and bottom, three lightening
    holes, a rivet row along the show face, and an end plate at the origin
    that the next segment's far end buries itself in."""
    p = Part(PALETTE)
    length, half_x, half_z = 2.0, 0.20, 0.265
    p.slab((-0.25, -length, 0.26), (0.25, 0.0, 0.30), DEEP)
    p.slab((-0.25, -length, -0.30), (0.25, 0.0, -0.26), DEEP)
    # The web stops 1 cm short of the far end: flush with the flanges its end
    # face would share their plane.
    perforated_web(p, half_x, -length + 0.01, 0.0, half_z, [-0.42, -1.0, -1.58], 0.17,
                   HULL, BLACK)
    # The plate stands 4 mm proud of y=0 so a chained segment's open end sits
    # inside it rather than flush against it.
    p.slab((-0.28, -0.03, -0.33), (0.28, 0.004, 0.33), RUST)
    for i in range(8):
        y = -0.12 - i * (length - 0.24) / 7.0
        bolt(p, (half_x, y, 0.225), 'X', 1, DEEP, radius=0.022, height=0.02, seg=4)
    p.finish("Mesh_Bogie_Beam", coll)


def build_swing_arm(coll):
    """A cranked trailing arm: pivot boss at the origin, road-wheel axle stub
    1.0 m along -Y sticking out to +X. The crank drops the wheel end so the
    arm clears the girder when the wheel rides up."""
    p = Part(PALETTE)
    p.cyl((0, 0, 0), 0.13, 0.30, 'X', 10, DARK)
    p.cyl((0, 0, 0), 0.055, 0.44, 'X', 6, WORN)
    p.prism([(0.0, 0.10), (-0.55, 0.04), (-1.0, 0.02), (-1.0, -0.14),
             (-0.55, -0.11), (0.0, -0.12)], 0.16, 'X', HULL)
    p.cyl((0, -1.0, -0.06), 0.11, 0.26, 'X', 10, DARK)
    stub = p.cyl((0.13 - EMBED + 0.19, -1.0, -0.06), 0.065, 0.38, 'X', 8, WORN)
    drop(p, stub, (-1, 0, 0))
    for y in (-0.22, -0.40, -0.62, -0.80):
        z = -0.03 - 0.07 * (-y)
        bolt(p, (0.08, y, z), 'X', 1, DEEP, radius=0.022, height=0.02, seg=4)
    p.finish("Mesh_Bogie_SwingArm", coll)


def build_leaf_spring(coll):
    """Five leaves, each shorter than the one above, clamped at the centre,
    with a rolled eye and a swinging shackle at each end.

    The leaves are bent in a straight-sided shallow V rather than a curve:
    identical slopes nest exactly when offset in Z, where a sampled curve cut
    to different lengths gaps and pokes through its neighbour."""
    p = Part(PALETTE)
    t, embed, width = 0.028, 0.003, 0.14
    flat, slope = 0.20, 0.20
    stack = 5 * (t - embed) + embed
    top = stack / 2.0
    for i, half in enumerate((0.70, 0.575, 0.45, 0.325, 0.20)):
        zt = top - i * (t - embed)
        rise = slope * (half - flat)
        if half > flat:
            prof = [(-half, zt + rise), (-flat, zt), (flat, zt), (half, zt + rise),
                    (half, zt + rise - t), (flat, zt - t), (-flat, zt - t), (-half, zt + rise - t)]
        else:
            prof = [(-half, zt), (half, zt), (half, zt - t), (-half, zt - t)]
        # Each leaf 4 mm narrower than the one above, so the sides of two
        # embedded leaves never share a plane.
        p.prism(prof, width - 0.004 * i, 'X', WORN if i == 0 else RUST)
    eye_z = top + slope * (0.70 - flat) - 0.01
    for sy in (-1, 1):
        y = sy * 0.70
        p.cyl((0, y, eye_z), 0.045, width, 'X', 5, WORN)
        for sx in (-1, 1):
            p.slab((sx * 0.072, y - 0.035, eye_z - 0.04), (sx * 0.100, y + 0.035, eye_z + 0.22), DEEP)
        p.cyl((0, y, eye_z + 0.18), 0.03, 0.24, 'X', 5, WORN)
    p.slab((-0.085, -0.05, -top - 0.02), (0.085, 0.05, top + 0.02), DARK)
    p.finish("Mesh_Bogie_LeafSpring", coll)


def build_gear_housing(coll):
    """A riveted final-drive box: chamfered body, bolted base and split
    flanges, a round inspection cover on the show face and a breather cap."""
    p = Part(PALETTE)
    hy, hx, hz = 0.80, 0.30, 0.90
    body_x = 0.24
    p.prism([(-hy + 0.03, 0.03), (hy - 0.03, 0.03), (hy - 0.03, 0.72),
             (hy - 0.21, hz), (-hy + 0.21, hz), (-hy + 0.03, 0.72)],
            2 * body_x, 'X', HULL)
    p.slab((-hx, -hy, 0.0), (hx, hy, 0.05), DEEP)
    p.slab((-hx, -hy + 0.01, 0.40), (hx, hy - 0.01, 0.46), DEEP)
    cover = p.cyl((body_x + 0.02 - EMBED, 0.22, 0.64), 0.16, 0.04, 'X', 12, DARK)
    drop(p, cover, (-1, 0, 0))
    for i in range(6):
        a = 2 * math.pi * i / 6
        bolt(p, (body_x + 0.04 - EMBED, 0.22 + 0.125 * math.cos(a), 0.64 + 0.125 * math.sin(a)),
             'X', 1, BRASS, radius=0.018, height=0.018, seg=4)
    for i in range(6):
        y = -0.65 + i * 0.26
        bolt(p, (hx, y, 0.43), 'X', 1, BRASS, radius=0.022, height=0.022, seg=4)
    for sx in (-1, 1):
        for y in (-0.70, -0.35, 0.35, 0.70):
            bolt(p, (sx * 0.27, y, 0.05), 'Z', 1, BRASS, radius=0.02, height=0.02, seg=4)
    for i in range(6):
        y = -0.55 + i * 0.22
        bolt(p, (body_x, y, 0.84), 'X', 1, RUST, radius=0.02, height=0.016, seg=4)
    cap = p.cyl((0, -0.30, hz + 0.03 - EMBED), 0.05, 0.06, 'Z', 6, DARK)
    drop(p, cap, (0, 0, -1))
    p.finish("Mesh_Bogie_GearHousing", coll)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    build_beam(collection("Coll_Bogie_Beam"))
    build_swing_arm(collection("Coll_Bogie_SwingArm"))
    build_leaf_spring(collection("Coll_Bogie_LeafSpring"))
    build_gear_housing(collection("Coll_Bogie_GearHousing"))

    report()
    save(out)


build()
