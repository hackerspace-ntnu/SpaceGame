"""components/mechanical/track_link — one shoe of a crawler's track.

A track is a hundred-odd copies of this laid end to end, so the whole budget is
sixty triangles and every one of them is spent on silhouette: the plate, two
hinge knuckles, and the cleat that bites the sand. Variations differ in the
cleat, because that is what reads on a moving track; a mix of them along one
loop is what stops the track looking like a texture scroll.

Link-local frame (see `_track_kit.py`): origin at the centre of the INNER face,
the face the wheels roll on. +Z points at the wheels, -Z at the ground (the
cleat side), -Y is the direction the lower run travels. Laying a track is
putting each origin on the wheel path with +Z towards the loop's inside.

Hinges: the +Y end carries its knuckle on the -X half, the -Y end on the +X
half, so link N's +Y knuckle and link N+1's -Y knuckle share one axis without
overlapping. Past |x| = 0.30 the joint is an open gap for the sprocket teeth.

    blender --background --python track_link.py -- --out track_link.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402
from _track_kit import (EMBED, HINGE_Y, HINGE_Z, KNUCKLE_IN, KNUCKLE_OUT,  # noqa: E402
                        KNUCKLE_R, LINK_WIDTH, PLATE_HALF_Y, PLATE_T, drop)

MATS = [
    "Mat_Metal_Rust_Heavy",      # 0 rusted plate
    "Mat_Metal_Steel_Dark",      # 1 dark plate
    "Mat_Metal_Steel_Worn",      # 2 knuckles, wear-polished cleat
    "Mat_Metal_Rust_Deep",       # 3 cleat scale
    "Mat_Plastic_Rubber_Black",  # 4 pad
]
RUST, DARK, WORN, DEEP, RUBBER = range(5)

HALF_X = LINK_WIDTH / 2.0
# Five sides is the roundest knuckle the sixty-triangle budget allows.
KNUCKLE_SEG = 5
# Cleats start this far inside the plate, and their buried top face is dropped.
CLEAT_TOP = -PLATE_T + EMBED


def plate(p, mat):
    return p.slab((-HALF_X, -PLATE_HALF_Y, -PLATE_T), (HALF_X, PLATE_HALF_Y, 0.0), mat)


def chipped_plate(p, mat, chip=0.09):
    """The plate with its +X/+Y corner broken off at 45 degrees."""
    return p.prism([(-HALF_X, -PLATE_HALF_Y), (HALF_X, -PLATE_HALF_Y),
                    (HALF_X, PLATE_HALF_Y - chip), (HALF_X - chip, PLATE_HALF_Y),
                    (-HALF_X, PLATE_HALF_Y)],
                   PLATE_T, 'Z', mat, offset=(0, 0, -PLATE_T / 2.0))


def knuckles(p):
    """One knuckle per end, on opposite halves — see the module docstring."""
    length = KNUCKLE_OUT - KNUCKLE_IN
    mid = (KNUCKLE_OUT + KNUCKLE_IN) / 2.0
    p.cyl((-mid, HINGE_Y, HINGE_Z), KNUCKLE_R, length, 'X', KNUCKLE_SEG, WORN)
    p.cyl((mid, -HINGE_Y, HINGE_Z), KNUCKLE_R, length, 'X', KNUCKLE_SEG, WORN)


def cleat_bar(p, profile, x0, x1, mat):
    """A cleat running across the track, from a (y, z) profile."""
    faces = p.prism(profile, x1 - x0, 'X', mat, offset=((x0 + x1) / 2.0, 0, 0))
    drop(p, faces, (0, 0, 1))


def build_grouser(coll):
    """The standard shoe: one tall square-shouldered grouser bar."""
    p = Part(PALETTE)
    plate(p, RUST)
    knuckles(p)
    cleat_bar(p, [(-0.05, CLEAT_TOP), (0.05, CLEAT_TOP), (0.03, -0.18), (-0.03, -0.18)],
              -HALF_X + 0.02, HALF_X - 0.02, DEEP)
    p.finish("Mesh_Link_Grouser", coll)


def build_chevron(coll):
    """A V cleat with its point towards -Y, so the lower run pushes sand out
    to both sides. The V is one prism rather than two crossed bars — crossed
    bars cost twice and z-fight where they meet."""
    p = Part(PALETTE)
    plate(p, DARK)
    knuckles(p)
    arm = HALF_X - 0.04
    faces = p.prism([(-arm, 0.06), (0.0, -0.13), (arm, 0.06),
                     (arm, 0.14), (0.0, -0.05), (-arm, 0.14)],
                    0.17 + CLEAT_TOP, 'Z', RUST,
                    offset=(0, 0, (CLEAT_TOP - 0.17) / 2.0))
    drop(p, faces, (0, 0, 1))
    p.finish("Mesh_Link_Chevron", coll)


def build_worn(coll):
    """Run down to half height and polished bright, with a corner snapped off.
    The cleat is ground lopsided and stops short of the broken corner."""
    p = Part(PALETTE)
    chipped_plate(p, RUST)
    knuckles(p)
    cleat_bar(p, [(-0.05, CLEAT_TOP), (0.05, CLEAT_TOP), (0.025, -0.15), (-0.04, -0.14)],
              -HALF_X + 0.02, HALF_X - 0.12, WORN)
    p.finish("Mesh_Link_Worn", coll)


def build_padded(coll):
    """A rubber road pad bolted over the outer face — the shoe for rock and
    hardpan, where a grouser would only skid and chew the ground."""
    p = Part(PALETTE)
    plate(p, DARK)
    knuckles(p)
    faces = p.slab((-HALF_X + 0.06, -0.12, -0.165), (HALF_X - 0.06, 0.12, CLEAT_TOP), RUBBER)
    drop(p, faces, (0, 0, 1))
    p.finish("Mesh_Link_Padded", coll)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    build_grouser(collection("Coll_Link_Grouser"))
    build_chevron(collection("Coll_Link_Chevron"))
    build_worn(collection("Coll_Link_Worn"))
    build_padded(collection("Coll_Link_Padded"))

    report()
    save(out)


build()
