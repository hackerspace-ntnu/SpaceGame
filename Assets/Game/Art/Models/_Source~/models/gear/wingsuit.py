"""Wingsuit — a membrane wing worn on the back that turns a fall into a glide.

Two cloth panels that run from the arms down to the hips, carried folded on a
slim spar unit that clips to the expedition rig's lash rail. The spar ends stick
out past both flanks, which is the only part of it visible while it is stowed.

Authored in the WEARER's frame: +Z up, −Y forward, origin at the point the spar
unit clips to the rail. The membranes are built where they sit when spread, so
the file reads as a wingsuit when it is opened — but each one carries its ORIGIN
at the shoulder end of its own leading edge, because that is the point Unity
straps to the upper-arm bone. Everything else about where a membrane sits is a
serialized fit on the prefab, tuned by eye.

Nine objects, nothing joined, no armature: a membrane deforms in the shader
(SpaceGame/ClothWind) rather than on bones, and the spars and clamps are rigid.
**The decomposition and the reasoning behind it are recorded once, in
`wingsuit_BUILD.md` beside this file.**

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.

    blender --background --python models/gear/wingsuit.py -- \
        --out models/gear/wingsuit.blend
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, _HERE)

import bpy  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from _wingsuit import Wing  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

# STEEL is deliberately index 0. `Part.bevel` stamps material index 0 onto every
# face it creates, so whatever sits first is what all the softened edges come out
# as -- and with the cloth first, the bevelled corners of the clamps and the case
# rendered as beige sailcloth. Both bevelled parts are metal, so steel is the
# right default; the membranes carry one material and never bevel.
STEEL, DARK, CLOTH, CANVAS, STRAP = range(5)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark", "Mat_Fabric_Wing_Beige",
        "Mat_Fabric_Canvas_Sand", "Mat_Fabric_Canvas_Faded"]

# ---------------------------------------------------------------------------
# The wearer. This astronaut is a big one — the capsule stands 3 m and the hand
# measured 1.7x human — so every span below is a human proportion scaled by that
# and NOT a figure anybody should recognise. They set the membrane's shape; its
# final placement is the prefab's fit, so being a few centimetres out here costs
# a tweak in the Inspector rather than a re-export.
# ---------------------------------------------------------------------------
ARM_SPAN = 0.95           # shoulder joint to wrist, the membrane's leading edge
ROOT_CHORD = 0.86         # shoulder to hip: how deep the wing is at the body
TIP_CHORD = 0.12          # what is left of it at the wrist
CHORD_FALLOFF = 1.35      # >1 sweeps the trailing edge back rather than straight

CAMBER = 0.085            # how far the panel bows up at its deepest
SKIN_ROOT = 0.014         # cloth thickness at the body...
SKIN_TIP = 0.008          # ...and at the wrist

SPAN_STATIONS = 9         # lofted sections along the arm
CHORD_POINTS = 6          # points down one surface of a section

# The spar unit on the rail.
PACK_WIDTH = 0.44
PACK_DEPTH = 0.115
PACK_HEIGHT = 0.24
STUB_LENGTH = 0.30        # how far a folded spar sticks out past the flank
STUB_ROOT_R = 0.034
STUB_TIP_R = 0.016

SHOULDER_X = 0.34         # where a membrane's origin sits, left/right of spine
SHOULDER_Z = 0.30         # ...and above the rail
SHOULDER_Y = -0.06        # ...and how far in front of it


# The membrane's shape maths lives in `_wingsuit.py`, shared with
# `wingsuit_worn.py` — the worn form is the same cloth at a different size and
# droop, and two copies of the loft would have drifted apart the first time
# either was tuned. The numbers above are still this model's own; only the
# curves are shared. Verified behaviour-identical by a fingerprint diff of every
# vertex and face against the shipped .blend before the extraction landed.
FLIGHT_WING = Wing(span=ARM_SPAN, root_chord=ROOT_CHORD, tip_chord=TIP_CHORD,
                   camber=CAMBER, chord_falloff=CHORD_FALLOFF,
                   skin_root=SKIN_ROOT, skin_tip=SKIN_TIP,
                   span_stations=SPAN_STATIONS, chord_points=CHORD_POINTS)


def build_membrane(coll, mats, name, span_sign):
    """One cloth panel, lofted along the arm.

    `span_sign` is +1 for the panel that runs toward +X and −1 for its mirror.
    Built as two real meshes rather than one mesh used twice, because the .blend
    is meant to open as a wingsuit rather than as half of one — and because a
    mirrored copy placed by a negative scale in Unity is the trap the gauntlets
    already documented once.
    """
    part = Part(mats)
    part.loft(FLIGHT_WING.sections(span_sign), axis='X', mat=CLOTH, cap=True)
    return part.finish(name, coll, origin=(0, 0, 0))


def build_batten(coll, mats, name, span_sign):
    """The leading-edge spar: a tapered tube along the arm.

    It is what stops the panel reading as a bare sheet, and it is what the cloth
    is actually stretched over. Set slightly PROUD of the leading edge rather
    than flush with it — a tube whose surface is tangent to the panel's would be
    two coincident surfaces down the whole span.
    """
    part = Part(mats)

    length = ARM_SPAN * 0.98
    part.cyl(center=(span_sign * length * 0.5, 0.016, 0.0),
             radius=STUB_ROOT_R * 0.75, radius_top=STUB_TIP_R * 0.7,
             depth=length, axis='X', seg=10, mat=STEEL)

    return part.finish(name, coll, origin=(0, 0, 0))


def build_pack(coll, mats):
    """The spar unit itself: the slim chassis that carries the folded wings.

    Lofted rather than boxed, and tapered at both ends and toward the back, so
    what sits on the rail reads as a machined case rather than as a brick.
    """
    part = Part(mats)

    # The whole case lives BEHIND the wearer, so every profile is pushed back off
    # the spine here rather than by translating the mesh afterwards.
    back = PACK_DEPTH * 0.5 + 0.012

    def profile(scale_y, scale_z, inset):
        """Rounded-rectangle cross-section in the (y, z) plane."""
        hy, hz = PACK_DEPTH * 0.5 * scale_y, PACK_HEIGHT * 0.5 * scale_z
        c = inset
        return [
            (back - hy + c, -hz), (back + hy - c, -hz),
            (back + hy, -hz + c), (back + hy, hz - c),
            (back + hy - c, hz), (back - hy + c, hz),
            (back - hy, hz - c), (back - hy, -hz + c),
        ]

    half = PACK_WIDTH * 0.5
    sections = [
        (-half, profile(0.72, 0.68, 0.018)),
        (-half * 0.62, profile(0.94, 0.93, 0.022)),
        (0.0, profile(1.0, 1.0, 0.024)),
        (half * 0.62, profile(0.94, 0.93, 0.022)),
        (half, profile(0.72, 0.68, 0.018)),
    ]

    # Pushed back off the spine: the whole case lives behind the wearer, and the
    # origin is the rail it clips to.
    part.loft(sections, axis='X', mat=DARK, cap=True)

    # A canvas cover over the top face. It SINKS into the lid rather than resting
    # on it — a panel laid exactly on the surface it covers is two coincident
    # faces and will flicker.
    part.slab((-half * 0.82, back - PACK_DEPTH * 0.34, PACK_HEIGHT * 0.5 - 0.008),
              (half * 0.82, back + PACK_DEPTH * 0.44, PACK_HEIGHT * 0.5 + 0.022),
              mat=CANVAS)

    part.bevel(width=0.006, segments=2)
    return part.finish("Mesh_Wingsuit_Pack", coll, origin=(0, 0, 0))


def build_stub(coll, mats, name, span_sign):
    """A folded spar end, sticking out past the flank.

    This is the whole of what a stowed wingsuit looks like, so it carries the
    silhouette on its own: swept back and tipped up rather than square out, and
    tapered to a real point.
    """
    part = Part(mats)

    sweep = (Matrix.Rotation(math.radians(span_sign * -14), 4, 'Z')
             @ Matrix.Rotation(math.radians(span_sign * 9), 4, 'Y'))

    start = PACK_WIDTH * 0.5 - 0.02
    part.cyl(center=(span_sign * (start + STUB_LENGTH * 0.5), 0.055, 0.02),
             radius=STUB_ROOT_R, radius_top=STUB_TIP_R,
             depth=STUB_LENGTH, axis='X', seg=10, mat=STEEL, rot=sweep)

    # The collar where the spar leaves the case, sunk into the tube so the two
    # surfaces intersect instead of meeting flush.
    part.cyl(center=(span_sign * (start + 0.026), 0.055, 0.02),
             radius=STUB_ROOT_R * 1.28, depth=0.030, axis='X', seg=10, mat=DARK,
             rot=sweep)

    return part.finish(name, coll, origin=(span_sign * start, 0.055, 0.02))


def build_clamp(coll, mats, name, span_sign):
    """One of the two jaws that grip the lash rail.

    Origin on the rail's own axis, which is what the fit is measured from.
    """
    part = Part(mats)

    x = span_sign * PACK_WIDTH * 0.28

    # Jaw block, tapered so it is not a cube.
    part.loft([
        (-0.026, [(-0.030, -0.028), (0.030, -0.028), (0.026, 0.030), (-0.026, 0.030)]),
        (0.000, [(-0.036, -0.034), (0.036, -0.034), (0.031, 0.036), (-0.031, 0.036)]),
        (0.026, [(-0.030, -0.028), (0.030, -0.028), (0.026, 0.030), (-0.026, 0.030)]),
    ], axis='X', mat=STEEL, cap=True)

    # The strap that closes it, standing off the jaw.
    part.slab((-0.034, -0.040, -0.006), (0.034, -0.030, 0.006), mat=STRAP)

    part.bevel(width=0.004, segments=2)
    return part.finish(name, coll, origin=(0, 0, 0)), x


def main():
    out = parse_out()
    start(out)

    mats = link_materials(MATS)
    root = collection("Wingsuit")

    build_pack(root, mats)

    for sign, side in ((-1, "R"), (1, "L")):
        stub = build_stub(root, mats, "Mesh_Wingsuit_SparStub_%s" % side, sign)
        clamp, x = build_clamp(root, mats, "Mesh_Wingsuit_Clamp_%s" % side, sign)
        clamp.location = (x, 0.0, -PACK_HEIGHT * 0.5 + 0.02)

        membrane = build_membrane(root, mats, "Mesh_Wingsuit_Membrane_%s" % side, sign)
        membrane.location = (sign * SHOULDER_X, SHOULDER_Y, SHOULDER_Z)

        batten = build_batten(root, mats, "Mesh_Wingsuit_Batten_%s" % side, sign)
        batten.location = membrane.location

    save(out)
    report()


main()
