"""Flask closures — the machined collar and whatever seals it.

The other half of the thrown-flask family. Every collar in this file seats on a
neck of outer radius `SEAT_R`, the same number `flask_body.py` builds its necks
to, so any closure fits any body. That is the whole reason the closure is a
separate file rather than geometry on each bottle: the *shell* says how much the
flask holds, the *closure* says how it opens, and the two questions have no
reason to be answered together.

Four ways of shutting a flask, and they differ in silhouette and in what moves:

  `Iris`     six leaves that rotate open in the plane of the collar
  `Stopper`  a ground-glass plug with a pull ring, which pops straight up
  `Screw`    a knurled cap that turns off (built ahead)
  `Bail`     a swing-top lid on a wire lever (built ahead)

**Every moving piece is its own object with its origin on its own axis.** An
iris leaf's origin sits on its hinge pin, so the artifact opens the iris with one
local Z rotation per leaf; the stopper's origin sits on the seat, so it pops with
one local Z translation. There is no armature, and that is a decision rather than
an omission — six rigid plates each turning about one axis is exactly the case
`dragon_bazooka.py` handles with an object pivot and calls a bone an expensive
way to store a number.

Overlapping iris leaves are staggered 0.35 mm apart in Z. They overlap by design
— that is what closes the aperture — and two 1.9 mm plates 0.35 mm apart
interpenetrate rather than sharing a plane, which is the only arrangement that
does not flicker.

Origin of every collar is the **seat plane**, on the axis, growing up **+Z**: the
same frame the bodies present their neck in, so assembly is one translation.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(_HERE)))
sys.path.insert(0, _HERE)

from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from flask_body import MATS, RING_SEG, SEAT_R  # noqa: E402

STEEL, DARK, SHELL, GLASS, RUBBER, CHROME, BLACK, YELLOW = range(8)

BORE = 0.034            # inner radius of the collar's aperture
SLEEVE_R = 0.035        # outer radius of the sleeve that slips over the neck
# 0.5 mm of clearance between the sleeve bore and the neck it slips over. Zero
# clearance is two cylinders sharing a surface, which flickers.
assert SLEEVE_R - 0.0035 > SEAT_R, "sleeve bore must clear the neck"


def sleeve(part, flange_r, flange_top):
    """The common lower half of every collar: sleeve, flange and lip.

    Every closure in this file starts with this, which is what makes four
    different lids read as four lids from one manufacturer.
    """
    part.tube((0, 0, 0.004), SLEEVE_R, 0.0035, 0.008, 'Z', RING_SEG, STEEL)
    part.tube((0, 0, (0.008 + flange_top) / 2.0), flange_r,
              flange_r - BORE, flange_top - 0.008, 'Z', RING_SEG, STEEL)
    part.torus((0, 0, flange_top - 0.0016), flange_r - 0.0026, 0.0022, 'Z',
               RING_SEG, 8, CHROME)


# --------------------------------------------------------------------------
# Iris — six leaves that rotate open about pins on the collar
# --------------------------------------------------------------------------

LEAF_N = 6
LEAF_R = 0.0335          # inside BORE, so the leaves swing within the aperture
LEAF_HALF = math.radians(37.5)
LEAF_T = 0.0019
LEAF_Z0 = 0.0118
LEAF_STAGGER = 0.00035
PIN_R = 0.0305


def iris(coll, mats):
    ring = TrackedPart(mats)
    sleeve(ring, 0.040, 0.020)
    # The bore is lined black so the aperture reads as a hole even when the
    # leaves are open and there is nothing behind them but the flask's cowl.
    ring.tube((0, 0, 0.015), BORE + 0.0006, 0.0006, 0.010, 'Z', RING_SEG, BLACK)
    ring.restamp("iris ring")
    ring.finish("Mesh_FlaskCollar_Iris_Ring", coll)

    for i in range(LEAF_N):
        phi = 2 * math.pi * i / LEAF_N
        z = LEAF_Z0 + i * LEAF_STAGGER
        p = TrackedPart(mats)
        # A sector reaching from the axis out to the aperture wall. Six of them
        # at 75 degrees each cover 450 degrees of a 360 degree hole, so the
        # aperture is shut with margin however the leaves are indexed.
        prof = [(0.0, 0.0)]
        steps = 8
        for k in range(steps + 1):
            a = phi - LEAF_HALF + (2 * LEAF_HALF) * k / steps
            prof.append((LEAF_R * math.cos(a), LEAF_R * math.sin(a)))
        p.prism(prof, LEAF_T, axis='Z', mat=STEEL,
                offset=(0, 0, z + LEAF_T / 2.0))
        # The pin the leaf turns on, and the origin the object carries. It sits
        # at the leaf's trailing outer corner, which is where an iris leaf's
        # pin actually goes — turning about it swings the blade out of the hole
        # instead of spinning it in place.
        pin = (PIN_R * math.cos(phi + LEAF_HALF * 0.8),
               PIN_R * math.sin(phi + LEAF_HALF * 0.8),
               z + LEAF_T / 2.0)
        p.cyl(pin, 0.0022, LEAF_T + 0.0016, 'Z', 8, CHROME)
        p.restamp("iris leaf %d" % (i + 1))
        p.finish("Mesh_FlaskCollar_Iris_Leaf_%d" % (i + 1), coll, origin=pin)


# --------------------------------------------------------------------------
# Stopper — a ground-glass plug on a pull ring
# --------------------------------------------------------------------------

PLUG_SEAT_Z = 0.006


def stopper(coll, mats):
    ring = TrackedPart(mats)
    sleeve(ring, 0.039, 0.018)
    ring.restamp("stopper ring")
    ring.finish("Mesh_FlaskCollar_Stopper_Ring", coll)

    p = TrackedPart(mats)
    p.cyl((0, 0, 0.017), 0.0325, 0.022, 'Z', RING_SEG, GLASS, radius_top=0.0295)
    p.torus((0, 0, 0.0105), 0.0308, 0.0034, 'Z', RING_SEG, 10, RUBBER)
    p.cyl((0, 0, 0.033), 0.0295, 0.010, 'Z', RING_SEG, GLASS, radius_top=0.0205)
    p.cyl((0, 0, 0.0405), 0.0205, 0.007, 'Z', RING_SEG, DARK, radius_top=0.0150)
    p.restamp("stopper plug")
    # Origin on the seat, on the axis: the plug pops straight up +Z and nothing
    # else has to be recomputed to animate it.
    p.finish("Mesh_FlaskCollar_Stopper_Plug", coll,
             origin=(0, 0, PLUG_SEAT_Z))

    r = TrackedPart(mats)
    r.cyl((0, 0, 0.0455), 0.005, 0.007, 'Z', 10, STEEL)
    # Yellow because it is a pull ring, which is literally what
    # Mat_Plastic_Safety_Yellow is documented for. It is also the one part of a
    # sealed flask a player is meant to notice they can grab.
    r.torus((0, 0, 0.0525), 0.0110, 0.0025, 'X', 18, 8, YELLOW)
    r.restamp("stopper pull ring")
    r.finish("Mesh_FlaskCollar_Stopper_PullRing", coll,
             origin=(0, 0, PLUG_SEAT_Z))


# --------------------------------------------------------------------------
# Built ahead: two closures nothing has asked for yet
# --------------------------------------------------------------------------

def screw(coll, mats):
    ring = TrackedPart(mats)
    sleeve(ring, 0.0385, 0.016)
    ring.restamp("screw ring")
    ring.finish("Mesh_FlaskCollar_Screw_Ring", coll)

    p = TrackedPart(mats)
    p.cyl((0, 0, 0.020), 0.0375, 0.016, 'Z', RING_SEG, DARK)
    # Knurling, as a ring of shallow flutes rather than a texture: the cap is
    # the part a hand turns, and the flutes are what say so at 256 px.
    for i in range(18):
        a = 2 * math.pi * i / 18
        p.cyl((0.0378 * math.cos(a), 0.0378 * math.sin(a), 0.020), 0.0022,
              0.014, 'Z', 6, STEEL)
    p.cyl((0, 0, 0.029), 0.0330, 0.004, 'Z', RING_SEG, DARK, radius_top=0.0300)
    p.restamp("screw cap")
    p.finish("Mesh_FlaskCollar_Screw_Cap", coll, origin=(0, 0, 0.012))


def bail(coll, mats):
    ring = TrackedPart(mats)
    sleeve(ring, 0.0390, 0.017)
    ring.restamp("bail ring")
    ring.finish("Mesh_FlaskCollar_Bail_Ring", coll)

    p = TrackedPart(mats)
    p.cyl((0, 0, 0.0215), 0.0330, 0.009, 'Z', RING_SEG, GLASS)
    p.torus((0, 0, 0.0180), 0.0308, 0.0032, 'Z', RING_SEG, 10, RUBBER)
    p.restamp("bail lid")
    p.finish("Mesh_FlaskCollar_Bail_Lid", coll, origin=(0, 0, 0.017))

    p = TrackedPart(mats)
    # A wire clamp: two arms rising off pins on the flange and a loop across
    # the lid. Built as one object because it is one bent wire.
    for s in (-1, 1):
        p.cyl((s * 0.0345, 0.0, 0.023), 0.0022, 0.020, 'Z', 8, CHROME)
        p.cyl((s * 0.0345, 0.0, 0.032), 0.0022, 0.010, 'Z', 8, CHROME)
    p.torus((0, 0, 0.035), 0.0345, 0.0022, 'Z', 20, 8, CHROME)
    p.cyl((0, -0.0345, 0.028), 0.0022, 0.016, 'Z', 8, CHROME)
    p.restamp("bail lever")
    p.finish("Mesh_FlaskCollar_Bail_Lever", coll, origin=(0, 0, 0.017))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    iris(collection("Coll_FlaskCollar_Iris"), mats)
    stopper(collection("Coll_FlaskCollar_Stopper"), mats)
    screw(collection("Coll_FlaskCollar_Screw"), mats)
    bail(collection("Coll_FlaskCollar_Bail"), mats)

    report()
    save(out)


main()
