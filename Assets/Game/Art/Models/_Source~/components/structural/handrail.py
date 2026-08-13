"""components/structural/handrail — the edge protection on any walkable deck.

Nothing sells scale like a handrail. A box on legs could be four metres tall or
forty; put a 1.05 m rail along its roof and the eye reads the whole machine
against a human body.

Modular: one straight section spans 2.00 m along +Y, origin on the deck surface
at the start post, so sections butt end to end on the library's 0.25 m grid.

    blender --background --python handrail.py -- --out handrail.blend
"""

import math
import os
import sys

from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",      # 0 posts and rails
    "Mat_Metal_Steel_Dark",      # 1 feet, clamps
    "Mat_Paint_Warn_Red",        # 2 hazard banding
    "Mat_Paint_Hull_Bleached",   # 3 kick plate
    "Mat_Metal_Rust_Heavy",      # 4 corrosion at the feet
    "Mat_Plastic_Rubber_Black",  # 5 grip, chain
]
STEEL, DARK, RED, HULL, RUST, RUBBER = range(6)

SPAN = 2.00
HEIGHT = 1.05
TUBE = 0.045


def post(p, y, height=HEIGHT, banded=False):
    p.cyl((0, y, height / 2), TUBE * 1.15, height, 'Z', 8, STEEL)
    p.box((0, y, 0.03), (0.24, 0.24, 0.06), DARK)                # foot plate
    for sx in (-1, 1):
        p.cyl((sx * 0.08, y, 0.03), 0.028, 0.08, 'Z', 6, RUST)   # foot bolts
    if banded:
        p.cyl((0, y, height * 0.62), TUBE * 1.35, 0.16, 'Z', 8, RED)


def rails(p, y0, y1, height=HEIGHT):
    """Top rail and knee rail. Two rails, not three: a third reads as a fence."""
    length = y1 - y0
    mid = (y0 + y1) / 2
    p.cyl((0, mid, height), TUBE, length, 'Y', 8, STEEL)
    p.cyl((0, mid, height * 0.52), TUBE * 0.85, length, 'Y', 8, STEEL)


def kickplate(p, y0, y1):
    p.box((0, (y0 + y1) / 2, 0.13), (0.05, y1 - y0, 0.20), HULL)


def build_straight(coll):
    p = Part(PALETTE)
    post(p, 0.0)
    post(p, SPAN, banded=True)
    rails(p, 0.0, SPAN)
    kickplate(p, 0.0, SPAN)
    p.bevel(width=0.01, segments=2)
    p.finish("Mesh_Handrail_Straight", coll)


def build_corner(coll):
    """An L. Its own variation rather than two straights butted, because a
    mitred corner post and a wrapped rail is what the eye checks for."""
    p = Part(PALETTE)
    post(p, 0.0)
    post(p, SPAN, banded=True)
    rails(p, 0.0, SPAN)
    kickplate(p, 0.0, SPAN)

    # The returning leg, along +X from the corner post.
    turn = Matrix.Rotation(math.radians(90), 4, 'Z')
    for x in (SPAN,):
        p.cyl((x / 2, SPAN, HEIGHT), TUBE, x, 'X', 8, STEEL)
        p.cyl((x / 2, SPAN, HEIGHT * 0.52), TUBE * 0.85, x, 'X', 8, STEEL)
        p.box((x / 2, SPAN, 0.13), (x, 0.05, 0.20), HULL)
        p.cyl((x, SPAN, HEIGHT / 2), TUBE * 1.15, HEIGHT, 'Z', 8, STEEL)
        p.box((x, SPAN, 0.03), (0.24, 0.24, 0.06), DARK)
    p.torus((0, SPAN, HEIGHT), TUBE * 1.6, TUBE, 'Z', 10, 6, STEEL)
    del turn
    p.bevel(width=0.01, segments=2)
    p.finish("Mesh_Handrail_Corner", coll)


def build_gate(coll):
    """A gap closed by a slack chain — where a ladder comes up through a deck.
    The catenary is faked with three segments; at this scale nobody counts."""
    p = Part(PALETTE)
    post(p, 0.0, banded=True)
    post(p, SPAN, banded=True)
    kickplate(p, 0.0, 0.35)
    kickplate(p, SPAN - 0.35, SPAN)

    sag = [(0.10, HEIGHT), (0.72, HEIGHT - 0.16), (1.28, HEIGHT - 0.16),
           (1.90, HEIGHT)]
    for (ya, za), (yb, zb) in zip(sag, sag[1:]):
        length = math.hypot(yb - ya, zb - za)
        lean = math.atan2(zb - za, yb - ya)
        p.cyl((0, (ya + yb) / 2, (za + zb) / 2), 0.03, length, 'Y', 6, RUBBER,
              rot=Matrix.Rotation(lean, 4, 'X'))
    for y in (0.10, 1.90):
        p.cyl((0, y, HEIGHT), 0.05, 0.10, 'Y', 8, DARK)
    p.bevel(width=0.01, segments=2)
    p.finish("Mesh_Handrail_Gate", coll)


def build_ladder(coll):
    """Vertical access ladder with a back cage. Built ahead: every deck this
    rail guards has to be reached somehow, and a caged ladder is the read."""
    p = Part(PALETTE)
    rungs, rise = 9, 0.32
    top = rungs * rise
    for sy in (-1, 1):
        p.cyl((0, sy * 0.28, top / 2), 0.04, top, 'Z', 8, STEEL)
    for i in range(rungs):
        z = 0.22 + i * rise
        p.cyl((0, 0, z), 0.028, 0.56, 'Y', 6, STEEL)
    for i in range(4):                                    # cage hoops
        z = 1.10 + i * 0.62
        p.torus((0.34, 0, z), 0.40, 0.03, 'X', 12, 5, STEEL)
    for sy in (-1, 1):
        p.box((0.70, sy * 0.30, 2.05), (0.06, 0.06, 2.10), STEEL)
    p.box((0, 0, top + 0.08), (0.10, 0.62, 0.10), RED)
    p.bevel(width=0.01, segments=2)
    p.finish("Mesh_Handrail_Ladder", coll)


def build_stair(coll):
    """A short flight with its own rail. Built ahead, for decks that step."""
    p = Part(PALETTE)
    steps, tread, rise = 6, 0.30, 0.24
    for i in range(steps):
        y = 0.20 + i * tread
        z = 0.14 + i * rise
        p.box((0, y, z), (0.90, tread * 0.92, 0.06), HULL)
        p.box((0, y - tread * 0.42, z - 0.05), (0.90, 0.05, 0.10), DARK)
    for sx in (-1, 1):
        length = math.hypot(steps * tread, steps * rise)
        lean = math.atan2(steps * rise, steps * tread)
        p.box((sx * 0.46, 0.20 + steps * tread / 2, 0.02 + steps * rise / 2),
              (0.06, length, 0.30), STEEL, rot=Matrix.Rotation(lean, 4, 'X'))
        p.cyl((sx * 0.46, 0.20 + steps * tread / 2,
               HEIGHT * 0.86 + steps * rise / 2), TUBE, length, 'Y', 8, STEEL,
              rot=Matrix.Rotation(lean, 4, 'X'))
        p.cyl((sx * 0.46, 0.24, HEIGHT * 0.44), TUBE * 1.1, HEIGHT * 0.88, 'Z',
              8, STEEL)
        p.cyl((sx * 0.46, 0.20 + steps * tread, HEIGHT * 0.44 + steps * rise),
              TUBE * 1.1, HEIGHT * 0.88, 'Z', 8, STEEL)
    p.bevel(width=0.01, segments=2)
    p.finish("Mesh_Handrail_Stair", coll)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    build_straight(collection("Coll_Handrail_Straight"))
    build_corner(collection("Coll_Handrail_Corner"))
    build_gate(collection("Coll_Handrail_Gate"))
    build_ladder(collection("Coll_Handrail_Ladder"))
    build_stair(collection("Coll_Handrail_Stair"))

    report()
    save(out)


build()
