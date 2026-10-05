"""components/mechanical/track_wheel — the wheels a crawler track runs around.

The road wheels that carry the weight are `road_wheel.py`; this file is the
rest of the loop: the sprocket that drives it, the idler that turns it at the
front, the roller that holds the top run up, and the big spur gear that the
crawler's side frames show off.

Every wheel has its axle along X and its origin on the hub centre, so an
assembly puts the origin on the axle line and nothing else.

The sprocket is derived from the link in `_track_kit.py`, not dimensioned by
eye: its 13 teeth sit on the pitch circle of a 0.36 m link, and they are thin
enough to pass through the open joint gaps the link leaves past |x| = 0.30.

    blender --background --python track_wheel.py -- --out track_wheel.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bmesh
from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402
from _track_kit import (EMBED, LINK_SEAT_R, SPROCKET_RING_X,  # noqa: E402
                        SPROCKET_TEETH, bolt_circle, drop)

MATS = [
    "Mat_Metal_Steel_Dark",       # 0 discs, rims
    "Mat_Metal_Steel_Worn",       # 1 teeth, running surfaces, axles
    "Mat_Metal_Rust_Heavy",       # 2 drums, spokes, brackets
    "Mat_Metal_Rust_Deep",        # 3 hubs
    "Mat_Metal_Brass_Tarnished",  # 4 bolts
]
DARK, WORN, RUST, DEEP, BRASS = range(5)

# Sprocket. The ring stops 8 mm inside where a wrapped link's inner face sits,
# so the track rides the teeth, not the disc.
RING_R = LINK_SEAT_R - 0.008
RING_T = 0.10
TOOTH_ROOT = RING_R - 0.017      # buried in the ring
TOOTH_TIP = 0.77                 # just short of the wrapped plates' outer faces
TOOTH_HALF_ROOT, TOOTH_HALF_TIP = 0.025, 0.010


def spin(p, faces, angle):
    """Rotate freshly made faces about the X axle."""
    verts = list({v for f in faces for v in f.verts})
    bmesh.ops.rotate(p.bm, verts=verts, cent=(0, 0, 0),
                     matrix=Matrix.Rotation(angle, 3, 'X'))


def about_x(radius, angle):
    """(y, z) of a point `radius` out from the axle at `angle` from +Z,
    matching the sense of `Matrix.Rotation(angle, 'X')`."""
    return -math.sin(angle) * radius, math.cos(angle) * radius


def build_sprocket(coll):
    """Two toothed rings on a drum. Tooth 0 points straight up (+Z); the rest
    follow every 360/13 degrees, so a link seats half a pitch off a tooth."""
    p = Part(PALETTE)
    for x in (-SPROCKET_RING_X, SPROCKET_RING_X):
        p.cyl((x, 0, 0), RING_R, RING_T, 'X', 2 * SPROCKET_TEETH, DARK)
        for k in range(SPROCKET_TEETH):
            tooth = p.prism([(-TOOTH_HALF_ROOT, TOOTH_ROOT), (TOOTH_HALF_ROOT, TOOTH_ROOT),
                             (TOOTH_HALF_TIP, TOOTH_TIP), (-TOOTH_HALF_TIP, TOOTH_TIP)],
                            RING_T - 0.01, 'X', WORN, offset=(x, 0, 0))
            tooth = drop(p, tooth, (0, 0, -1))
            spin(p, tooth, 2 * math.pi * k / SPROCKET_TEETH)
    drum_x = SPROCKET_RING_X - RING_T / 2.0 + 0.01
    p.cyl((0, 0, 0), 0.50, 2 * drum_x, 'X', 16, RUST)
    face_x = SPROCKET_RING_X + RING_T / 2.0
    for sign in (-1, 1):
        boss = p.cyl((sign * (face_x + 0.05 - EMBED), 0, 0), 0.20, 0.10, 'X', 10, DEEP)
        drop(p, boss, (-sign, 0, 0))
        bolt_circle(p, (sign * face_x, 0, 0), 'X', sign, 0.34, 6, BRASS,
                    radius=0.035, height=0.035, seg=4)
    p.finish("Mesh_Wheel_Sprocket", coll)


def build_idler(coll):
    """A plain rim on two six-spoke discs — the front wheel the track turns
    round. Spokes are clocked 30 degrees apart side to side so the wheel does
    not read as one solid spider from an angle."""
    p = Part(PALETTE)
    p.tube((0, 0, 0), 0.65, 0.08, 1.0, 'X', 18, DARK)
    p.cyl((0, 0, 0), 0.18, 1.12, 'X', 10, DEEP)
    for x, phase in ((-0.42, 0.0), (0.42, math.pi / 6)):
        for i in range(6):
            a = phase + 2 * math.pi * i / 6
            y, z = about_x(0.37, a)
            p.box((x, y, z), (0.08, 0.10, 0.46), RUST, rot=Matrix.Rotation(a, 4, 'X'))
    for sign in (-1, 1):
        cap = p.cyl((sign * 0.57, 0, 0), 0.25, 0.06, 'X', 10, RUST)
        drop(p, cap, (-sign, 0, 0))
        bolt_circle(p, (sign * 0.60, 0, 0), 'X', sign, 0.17, 4, BRASS,
                    radius=0.03, height=0.03, seg=4)
    p.finish("Mesh_Wheel_Idler", coll)


def build_return_roller(coll):
    """Top-run roller: two short drums on a through axle, hung from a bracket
    stub at +X that bolts to the hull above. Mirror in X for the far side."""
    p = Part(PALETTE)
    for x0, x1 in ((-0.60, -0.03), (0.03, 0.60)):
        p.cyl(((x0 + x1) / 2.0, 0, 0), 0.225, x1 - x0, 'X', 10, DARK)
    p.cyl((0.08, 0, 0), 0.06, 1.44, 'X', 6, WORN)
    p.slab((0.66, -0.08, -0.10), (0.76, 0.08, 0.42), RUST)
    p.slab((0.62, -0.14, 0.40), (0.80, 0.14, 0.46), RUST)
    p.finish("Mesh_Wheel_ReturnRoller", coll)


def gear_profile(teeth, root, tip):
    """A spur-gear outline: four points a tooth, flanks tapering to the tip."""
    step = 2 * math.pi / teeth
    pts = []
    for i in range(teeth):
        a = step * i
        for da, r in ((-0.30, root), (-0.14, tip), (0.14, tip), (0.30, root)):
            pts.append(about_x(r, a + da * step))
    return pts


def build_gear(coll):
    """An exposed spur gear, 24 teeth, bolted hub on the +X (show) face.

    The gear body is one prism of the toothed outline: its faces are a single
    concave polygon each, which costs a third of what the same outline built
    as a disc plus 24 tooth blocks would."""
    p = Part(PALETTE)
    p.prism(gear_profile(24, 0.48, 0.55), 0.12, 'X', RUST)
    p.cyl((0, 0, 0), 0.17, 0.24, 'X', 8, WORN)
    p.cyl((0, 0, 0), 0.26, 0.14, 'X', 10, DEEP)
    bolt_circle(p, (0.07, 0, 0), 'X', 1, 0.215, 5, BRASS,
                radius=0.03, height=0.03, seg=4)
    p.finish("Mesh_Wheel_Gear", coll)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    build_sprocket(collection("Coll_Wheel_Sprocket"))
    build_idler(collection("Coll_Wheel_Idler"))
    build_return_roller(collection("Coll_Wheel_ReturnRoller"))
    build_gear(collection("Coll_Wheel_Gear"))

    report()
    save(out)


build()
