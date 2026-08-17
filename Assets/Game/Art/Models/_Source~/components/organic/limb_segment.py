"""Build components/organic/limb_segment.blend -- one bone of a sprawling limb.

Six variations covering the front and rear limbs of a heavy sprawling desert
quadruped, plus a slim and a stub form for lighter creatures.

Every segment is built along +X with the proximal joint at the origin and the
dorsal side at +Z, and is kept symmetric about its own local y = 0 so the same
mesh serves both sides of the animal. See `_organic.py` for the full convention.

The joint bulges are lofted into the profile rather than added as separate
cylinders. A crossways cylinder is the obvious way to make a condyle and it is
wrong: it caps flat, so the limb reads as a machined dumbbell rather than as
something with muscle over it. Ending the profile on a collapsed ring (see
`_organic.rounded`) costs one station and domes the end properly.

Authored at final real-world scale for a ~5.5 m animal. The Vrescal's own
.blend is worked at ~4x that, so it scales the mesh data on the way in.

    blender --background --python limb_segment.py -- \
        --out <lib>/components/organic/limb_segment.blend
"""

import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from _buildlib import (Part, collection, link_materials, parse_out, report,
                       save, start)                                # noqa: E402
from _organic import bone, rounded, scutes, shaped                 # noqa: E402

HIDE, PLATE, HORN = 0, 1, 2
MATERIALS = ["Mat_Hide_Sand_Pale", "Mat_Hide_Plate_Tan", "Mat_Hide_Claw_Horn"]


def brachial(mats, heavy=True):
    """Front upper limb. The deltoid mass sits proximally, so the thick end is
    at the shoulder and it necks down to the elbow."""
    k = 1.0 if heavy else 0.70
    pts = [(0.000, 0.118 * k, 0.140 * k),      # shoulder ball
           (0.055, 0.158 * k, 0.188 * k),      # deltoid mass
           (0.150, 0.148 * k, 0.176 * k),
           (0.290, 0.104 * k, 0.126 * k),      # shaft waist
           (0.430, 0.098 * k, 0.118 * k),
           (0.520, 0.120 * k, 0.142 * k),      # elbow flare
           (0.580, 0.104 * k, 0.124 * k)]
    part = Part(mats)
    part.loft(bone(shaped(rounded(pts), bow=0.026, ridge=0.014 * k,
                          flat_bottom=0.18)), axis='X', mat=HIDE, cap=True)
    if heavy:
        # Only the front limb carries armour on its upper surface -- it is the
        # one that meets the ground first when the animal drops onto a dune.
        scutes(part, (0.11, 0.0, 0.150), (0.44, 0.0, 0.108), 4, 0.050, PLATE)
    part.bevel(width=0.006, segments=1)
    return part


def antebrachial(mats):
    """Front lower limb, with an olecranon spur behind the elbow and a row of
    scutes down the leading edge."""
    pts = [(0.000, 0.104, 0.122),
           (0.048, 0.116, 0.134),              # elbow head
           (0.150, 0.092, 0.106),
           (0.300, 0.074, 0.086),
           (0.390, 0.070, 0.082),
           (0.440, 0.076, 0.088)]              # wrist flare
    part = Part(mats)
    part.loft(bone(shaped(rounded(pts), bow=-0.016, ridge=0.011,
                          flat_bottom=0.22)), axis='X', mat=HIDE, cap=True)
    # Olecranon -- the elbow point, projecting back past the joint.
    part.cyl((-0.046, 0.0, 0.050), 0.048, 0.088, axis='Y', seg=8, mat=HIDE,
             radius_top=0.026)
    scutes(part, (0.07, 0.0, 0.104), (0.39, 0.0, 0.070), 5, 0.038, PLATE)
    part.bevel(width=0.005, segments=1)
    return part


def femoral(mats):
    """Rear upper limb. Much heavier than the front -- this is the drive limb,
    and the thigh mass is what sells a crawler as pushing rather than dragging.
    """
    pts = [(0.000, 0.150, 0.172),              # hip ball
           (0.070, 0.204, 0.236),              # thigh mass
           (0.190, 0.194, 0.222),
           (0.360, 0.140, 0.168),
           (0.500, 0.114, 0.136),
           (0.590, 0.132, 0.156),              # knee flare
           (0.640, 0.116, 0.138)]
    part = Part(mats)
    part.loft(bone(shaped(rounded(pts), bow=0.032, ridge=0.018,
                          flat_bottom=0.16)), axis='X', mat=HIDE, cap=True)
    # Trochanter: the muscle anchor lump on the outer face of the hip end.
    part.cyl((0.115, 0.0, 0.150), 0.066, 0.150, axis='Y', seg=8, mat=HIDE,
             radius_top=0.044)
    part.bevel(width=0.006, segments=1)
    return part


def crural(mats):
    """Rear lower limb, banded with transverse scale ridges."""
    pts = [(0.000, 0.112, 0.128),
           (0.050, 0.124, 0.140),              # knee head
           (0.160, 0.098, 0.112),
           (0.310, 0.078, 0.090),
           (0.410, 0.070, 0.082),
           (0.460, 0.078, 0.090)]              # ankle flare
    part = Part(mats)
    part.loft(bone(shaped(rounded(pts), bow=-0.020, keel=0.009,
                          flat_bottom=0.20)), axis='X', mat=HIDE, cap=True)
    # Scale bands rather than a scute row -- the rear limb reads as hide over
    # bone where the front reads as armoured. The band radius tracks the taper,
    # sitting just proud of the shaft so it catches a highlight.
    for i, (x, r) in enumerate([(0.10, 0.108), (0.20, 0.096), (0.29, 0.086),
                                (0.37, 0.078)]):
        part.torus((x, 0.0, 0.0), r, 0.010, axis='X', maj_seg=12, min_seg=6,
                   mat=PLATE)
    part.bevel(width=0.005, segments=1)
    return part


def stub(mats):
    """A short vestigial limb -- for smaller creatures, or a Vrescal juvenile."""
    pts = [(0.000, 0.108, 0.126),
           (0.045, 0.134, 0.154),
           (0.150, 0.116, 0.132),
           (0.260, 0.104, 0.118)]
    part = Part(mats)
    part.loft(bone(shaped(rounded(pts), bow=0.010, ridge=0.010,
                          flat_bottom=0.20)), axis='X', mat=HIDE, cap=True)
    scutes(part, (0.06, 0.0, 0.132), (0.21, 0.0, 0.108), 3, 0.042, PLATE)
    part.bevel(width=0.005, segments=1)
    return part


VARIANTS = [
    ("BrachialHeavy", lambda m: brachial(m, heavy=True)),
    ("BrachialSlim", lambda m: brachial(m, heavy=False)),
    ("AntebrachialPlated", antebrachial),
    ("FemoralHeavy", femoral),
    ("CruralRibbed", crural),
    ("Stub", stub),
]


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATERIALS)
    for name, builder in VARIANTS:
        coll = collection("Coll_LimbSegment_%s" % name)
        builder(mats).finish("Mesh_LimbSegment_%s" % name, coll)
    report()
    save(out)


main()
