"""Cable drums — hand-sized winch spools for worn and carried equipment.

Three ways of storing a working length of cable on a device small enough to
strap to a person: a plain flanged winch drum, the same drum inside a roll cage,
and a drum with a ratchet and pawl so it holds a load without power.

This is the item-scale answer to a shape the library only had at vehicle scale.
`components/mechanical/drum_magazine.blend` and `road_wheel.blend` are both
right in spirit and an order of magnitude too big: scaling either down keeps its
bolt and panel-line density, which at this size collapses into noise. That is
the same reasoning `item_devices_BUILD.md` records for the carried gadgets, and
it applies here for the same reason.

Origin is at the **axle centre**, and the axle runs along **X**. A drum is
positioned by where its shaft passes through a mount, and nothing else about it
is a useful pivot. Whatever carries one puts a bearing at (0, 0, 0).

Sized so the whole drum is 0.066-0.078 m across the flanges and holds a
believable coil: barrel radius 0.020, cable at radius 0.0265, flange radius
0.033.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(_HERE)))
sys.path.insert(0, os.path.join(os.path.dirname(_HERE), "mechanical"))

import bmesh  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

# Index 0 is STEEL because `bmesh.ops.bevel` stamps every face it creates with
# material index 0 — see project_buildlib_traps and grapple_dart_BUILD.md. Put
# an accent colour here and every chamfer in the file wears it.
STEEL, DARK, PALE, ORANGE, CHROME, RUBBER, BLACK, BRASS = range(8)
MATS = ["Mat_Metal_Steel_Worn",        # frame, flange webs, axle housings
        "Mat_Metal_Steel_Dark",        # machined hubs, pawls, gear teeth
        "Mat_Paint_Hull_Bleached",     # painted flange faces
        "Mat_Paint_Safety_Orange",     # the one high-vis marker per drum
        "Mat_Metal_Chrome_Scuffed",    # the cable itself, axle stubs
        "Mat_Plastic_Rubber_Black",    # brake band, cage feet
        "Mat_Neutral_Black_Matte",     # shadow gaps under the flanges
        "Mat_Metal_Brass_Tarnished"]   # bearing collars

# 1.6 mm, matching `arm_cuff`. The library's 12 mm default would eat the 5 mm
# axle stubs whole; see the whole-part-bevel trap in item_devices_BUILD.md.
BEVEL_W = 0.0016

BARREL_R = 0.0200      # what the cable is wound onto
CABLE_R = 0.0265       # radius the outermost turn sits at
FLANGE_R = 0.0330      # what stops the coil walking off the end
HALF_W = 0.0240        # inside face of each flange


class TrackedPart(Part):
    """`Part` with `_absorb` fixed to identify new faces by identity.

    The bug is documented at length in `grapple_dart.py`: `Part._absorb` slices
    `self.bm.faces[n_before:]` after `bm.from_mesh`, and `from_mesh` does not
    leave the existing faces in their old index slots, so a handful of faces
    silently wear a neighbouring part's material.

    Redefined here rather than imported because `grapple_dart.py` and
    `grapple_harpoon.py` both call `main()` at module level — importing either
    rebuilds a component. This file guards its `main()`, so `gas_bottle.py` and
    the bracer assembly import the class from here instead of copying it again.
    """

    def __init__(self, materials):
        super().__init__(materials)
        self._stamps = []

    def _tag(self, faces, mat):
        faces = list(faces)
        self._stamps.append((faces, mat))
        return super()._tag(faces, mat)

    def _absorb(self, bm2, mat):
        before = set(self.bm.faces)
        n_log = len(self._stamps)
        super()._absorb(bm2, mat)
        del self._stamps[n_log:]        # drop the bogus index-slice stamp
        new = [f for f in self.bm.faces if f not in before]
        return self._tag(new, mat)

    def restamp(self):
        n = 0
        for faces, mat in self._stamps:
            for f in faces:
                if f.is_valid and f.material_index != mat:
                    f.material_index = mat
                    n += 1
        return n


# --------------------------------------------------------------------------
# Shared vocabulary — what makes the three read as one product line
# --------------------------------------------------------------------------

def barrel(p, turns=8, cable_mat=CHROME):
    """The drum barrel and the cable wound on it.

    The coil is a stack of tori rather than a helix: at this size the pitch is
    3.2 mm and nobody can tell, while a real helix costs a swept path and ends
    with two loose ends that have to be tidied. The turns stop 2 mm short of
    each flange so the coil reads as wound rather than as a solid sleeve.
    """
    p.cyl((0, 0, 0), BARREL_R, HALF_W * 2 - 0.001, 'X', 20, DARK)
    span = HALF_W * 2 - 0.008
    for i in range(turns):
        x = -span / 2 + span * i / (turns - 1)
        # 14 x 5 rather than the tempting 18 x 6: eight of these is the single
        # biggest line in the drum's triangle budget, and a 3 mm turn is one
        # pixel wide at the distance an arm-worn device is looked at. Same
        # reasoning as the harpoon's ferrule rings.
        p.torus((x, 0, 0), CABLE_R - 0.0032, 0.0032, 'X', 14, 5, cable_mat)


def flange(p, sx, painted=True, spokes=6):
    """One end plate: a painted disc, a raised rim, and radial stiffeners.

    The stiffeners are the whole read. A flat disc at this scale is a washer;
    six ribs turn it into a made wheel, and they are the only detail on the
    drum that survives being looked at from the side.
    """
    hard = []
    p.cyl((sx * (HALF_W + 0.0030), 0, 0), FLANGE_R, 0.0060, 'X', 24,
          PALE if painted else STEEL)
    p.torus((sx * (HALF_W + 0.0060), 0, 0), FLANGE_R - 0.0026, 0.0026, 'X',
            24, 6, STEEL)
    for i in range(spokes):
        a = 2 * math.pi * i / spokes
        r = (FLANGE_R + BARREL_R) / 2
        # Half-buried in the disc (which spans x 0.024-0.030 from HALF_W), so
        # each rib is a ridge ON the plate. Standing them clear of it — which
        # is what a symmetric offset does — turns six stiffeners into six tabs
        # floating beside a washer.
        hard += p.box((sx * (HALF_W + 0.0056), math.cos(a) * r,
                       math.sin(a) * r),
                      (0.0038, 0.0060, FLANGE_R - BARREL_R + 0.0040),
                      ORANGE if i == 0 else STEEL,
                      rot=Matrix.Rotation(a, 4, 'X'))
    return hard


def hub(p, sx, stub=0.0140):
    """Bearing boss and the axle stub that carries the drum in its mount."""
    hard = []
    p.cyl((sx * (HALF_W + 0.0100), 0, 0), 0.0085, 0.0080, 'X', 12, DARK)
    p.torus((sx * (HALF_W + 0.0135), 0, 0), 0.0068, 0.0018, 'X', 12, 6, BRASS)
    p.cyl((sx * (HALF_W + 0.0150 + stub / 2), 0, 0), 0.0050, stub, 'X', 10,
          CHROME)
    hard += p.box((sx * (HALF_W + 0.0150 + stub), 0, 0),
                  (0.0030, 0.0110, 0.0110), STEEL)
    return hard


def lead_off(p, to=(0.0, -0.0520, 0.0230)):
    """The tail of cable leaving the coil, so it is going somewhere.

    Without it the drum is a wheel with a stripe on it. Three points rather
    than two: a cable coming off a spool leaves tangentially and then bends.

    It starts ON the coil surface, not inside it. Starting at `CABLE_R -
    0.003` buries the first segment in the wound turns and the tail appears to
    sprout from behind the flange with no visible source.
    """
    tube_path(p, [(0.0, -CABLE_R - 0.0010, 0.0), (0.0, -0.0345, 0.0110),
                  to], 0.0030, CHROME, seg=6)


# --------------------------------------------------------------------------
# Variations
# --------------------------------------------------------------------------

def winch(coll, mats):
    """Plain flanged winch drum with a friction brake band over the top.

    The one the grapple bracer ships on. Lowest silhouette of the three, which
    is what an arm-worn device needs — anything that stands proud of the drum
    is something to catch on a doorway.
    """
    p = TrackedPart(mats)
    hard = []

    barrel(p)
    for sx in (-1, 1):
        hard += flange(p, sx)
        hard += hub(p, sx)

    # Brake band across the top of the coil, with its anchor block. A band is
    # what says the drum is meant to be stopped rather than freewheeling.
    #
    # 0.026 wide, which is INSIDE the 0.048 barrel. The first pass used
    # HALF_W * 1.7 and the band came out wider than the drum it was supposed
    # to grip, reading as four loose bars laid across the flanges.
    #
    # `R_x(a - 90)` is what puts the segment's local Z along the RADIUS and its
    # local Y along the tangent, so `size` reads (band width, arc step, band
    # thickness). Rotating by `a` instead — the intuitive thing — lays the
    # tangent along the radius, and a band becomes a ring of spikes.
    band_r = CABLE_R + 0.0034
    step = math.radians(236.0 / 15)
    for i in range(16):
        a = math.radians(-118 + 236.0 / 15 * i)
        hard += p.box((0.0, math.cos(a) * band_r, math.sin(a) * band_r),
                      (0.0260, band_r * step * 1.25, 0.0028), RUBBER,
                      rot=Matrix.Rotation(a - math.pi / 2, 4, 'X'))
    hard += p.box((0.0, 0.0, CABLE_R + 0.0120), (0.0180, 0.0130, 0.0130),
                  STEEL)
    p.cyl((0.0, 0.0, CABLE_R + 0.0195), 0.0035, 0.0090, 'Z', 10, CHROME)

    lead_off(p)

    print("  Coll_CableDrum_Winch: %d face(s) re-stamped" % p.restamp())
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_CableDrum_Winch", coll)


def caged(coll, mats):
    """Bare drum inside a four-bar roll cage — the knock-about version.

    No paint, no brake. The cage is the silhouette: a rectangle of tube around
    a circle, which is the one shape here that reads correctly from directly
    above as well as from the side.
    """
    p = TrackedPart(mats)
    hard = []

    barrel(p, turns=7, cable_mat=STEEL)
    for sx in (-1, 1):
        hard += flange(p, sx, painted=False, spokes=4)
        hard += hub(p, sx, stub=0.0100)

    # Cage: two hoops of tube joined by four longitudinals, standing 6 mm off
    # the flange rim so a dropped drum lands on steel rather than on cable.
    r = FLANGE_R + 0.0060
    for sx in (-1, 1):
        x = sx * (HALF_W + 0.0150)
        tube_path(p, [(x, math.cos(math.radians(a)) * r,
                       math.sin(math.radians(a)) * r)
                      for a in range(0, 361, 45)], 0.0028, STEEL, seg=6)
    for i in range(4):
        a = math.radians(45 + 90 * i)
        tube_path(p, [(-(HALF_W + 0.0150), math.cos(a) * r, math.sin(a) * r),
                      ((HALF_W + 0.0150), math.cos(a) * r, math.sin(a) * r)],
                  0.0028, STEEL, seg=6)
    # Feet ON the two lower longitudinals, not floating below the cage: the
    # bars sit at 45 degrees, so a foot written at (y = r, z = -r) is out at
    # radius r * sqrt(2) with nothing under it.
    for sy in (-1, 1):
        a = math.radians(180 + 45) if sy < 0 else math.radians(-45)
        hard += p.box((0.0, math.cos(a) * r, math.sin(a) * r),
                      (HALF_W * 2, 0.0130, 0.0070), RUBBER,
                      rot=Matrix.Rotation(a, 4, 'X'))

    lead_off(p, to=(0.0, -0.0560, 0.0240))

    print("  Coll_CableDrum_Caged: %d face(s) re-stamped" % p.restamp())
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_CableDrum_Caged", coll)


def ratchet(coll, mats):
    """Drum with a toothed ratchet wheel and a sprung pawl.

    The mechanical read of "holds a load with the power off". The teeth are
    16 sawtooth boxes rather than a modelled gear — at 3 mm they are a
    serrated edge in silhouette and nothing else, and a real involute profile
    would cost ten times the triangles to say the same word.
    """
    p = TrackedPart(mats)
    hard = []

    barrel(p, turns=8)
    for sx in (-1, 1):
        hard += flange(p, sx, painted=(sx < 0))
        hard += hub(p, sx, stub=0.0100 if sx > 0 else 0.0140)

    # Ratchet wheel outboard of the +X flange.
    gx = HALF_W + 0.0105
    p.cyl((gx, 0, 0), 0.0250, 0.0050, 'X', 20, DARK)
    for i in range(16):
        a = 2 * math.pi * i / 16
        hard += p.box((gx, math.cos(a) * 0.0272, math.sin(a) * 0.0272),
                      (0.0048, 0.0075, 0.0040), DARK,
                      rot=Matrix.Rotation(a + 0.30, 4, 'X'))

    # Pawl: a pivoted arm dropping onto the teeth, and the spring behind it.
    hard += p.box((gx, -0.0180, 0.0400), (0.0060, 0.0300, 0.0075), STEEL,
                  rot=Matrix.Rotation(math.radians(-28), 4, 'X'))
    p.cyl((gx, -0.0300, 0.0480), 0.0055, 0.0110, 'X', 10, BRASS)
    for i in range(5):
        p.torus((gx, -0.0055 - 0.0055 * i, 0.0480 - 0.0022 * i), 0.0038,
                0.0011, 'X', 10, 5, CHROME)

    lead_off(p)

    print("  Coll_CableDrum_Ratchet: %d face(s) re-stamped" % p.restamp())
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_CableDrum_Ratchet", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    winch(collection("Coll_CableDrum_Winch"), mats)
    caged(collection("Coll_CableDrum_Caged"), mats)
    ratchet(collection("Coll_CableDrum_Ratchet"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
