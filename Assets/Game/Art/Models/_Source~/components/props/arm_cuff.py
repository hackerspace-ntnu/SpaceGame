"""Forearm cuffs — the mount that puts equipment on an arm.

Three ways of strapping something to a forearm: a moulded leather sleeve, an
open webbing harness, and a plated bracer. Anything wrist-carried can sit on one
of these instead of growing its own mount, which is the whole reason this is a
component rather than part of the scanner.

Origin is at the **wrist end**, geometry runs up +Z toward the elbow. That is
the connection point that matters: whatever wears one of these is positioned by
its hand, not by its elbow, so an origin at the wrist means a cuff can be
dropped onto a hand socket with a zero offset.

Sized for an adult forearm inside a suit: 0.21 m long, tapering from 0.11 m
across at the elbow end to 0.07 m at the wrist.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(_HERE)))
sys.path.insert(0, os.path.join(os.path.dirname(_HERE), "mechanical"))

from _buildlib import *  # noqa: E402,F403
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix  # noqa: E402

# Index 0 is STEEL because `bmesh.ops.bevel` stamps new faces with index 0.
# Only the metal fittings are bevelled here — see `hard` in each builder — so
# the leather never picks up a steel edge.
STEEL, DARK, RUBBER, CHROME, LEATHER, PALE, CANVAS, BRASS, BLACK = range(9)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Fabric_Seat_Ochre", "Mat_Paint_Hull_Bleached",
        "Mat_Fabric_Canvas_Faded", "Mat_Metal_Brass_Tarnished",
        "Mat_Neutral_Black_Matte"]

BEVEL_W = 0.0016


def rrect(width, depth, corner=0.28, per_corner=3):
    """A rounded-rectangle profile in (u, v), for `Part.loft` along Z.

    `_plane_point('Z', u, v, w)` maps straight through to (x, y, z), so u is
    width and v is depth with no surprises. The other axes do not map so
    politely — check `_buildlib` before switching this to X or Y.
    """
    hw, hd = width / 2.0, depth / 2.0
    r = min(hw, hd) * corner
    pts = []
    for cx, cy, a0 in ((hw - r, hd - r, 0.0), (-(hw - r), hd - r, math.pi / 2),
                       (-(hw - r), -(hd - r), math.pi),
                       (hw - r, -(hd - r), 3 * math.pi / 2)):
        for i in range(per_corner + 1):
            a = a0 + (math.pi / 2) * i / per_corner
            pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


# Stations up the sleeve: (z, width, depth). The taper is what makes it read as
# an arm rather than a pipe, so it is not linear — the swell sits high, where a
# forearm's muscle is.
SLEEVE = [(0.000, 0.070, 0.058), (0.026, 0.077, 0.063), (0.070, 0.089, 0.073),
          (0.120, 0.100, 0.083), (0.170, 0.109, 0.090),
          (0.200, 0.112, 0.092), (0.215, 0.106, 0.087)]


def _sleeve_sections(scale=1.0, corner=0.28, zmax=None):
    return [(z, rrect(w * scale, d * scale, corner)) for z, w, d in SLEEVE
            if zmax is None or z <= zmax + 1e-9]


def _at(z):
    """Sleeve width and depth at height `z`, linearly between stations.

    Every fitting on the cuff is placed against the surface, and the surface
    moves: a strap keeper written at a fixed y is outside the leather at the
    wrist and buried inside it at the elbow. Both failures look like a modelling
    mistake and neither shows up until the thing is rendered.
    """
    if z <= SLEEVE[0][0]:
        return SLEEVE[0][1], SLEEVE[0][2]
    for (z0, w0, d0), (z1, w1, d1) in zip(SLEEVE, SLEEVE[1:]):
        if z <= z1:
            t = (z - z0) / (z1 - z0)
            return w0 + (w1 - w0) * t, d0 + (d1 - d0) * t
    return SLEEVE[-1][1], SLEEVE[-1][2]


def _buckle(p, at, width=0.030, ring=0.011):
    """A D-ring on a keeper, with a strap tail hanging through it."""
    x0, y0, z0 = at
    hard = list(p.box((x0, y0, z0), (width, 0.008, 0.012), CANVAS))
    hard += p.box((x0, y0 - 0.004, z0), (width * 0.34, 0.006, 0.016), BRASS)
    p.torus((x0, y0 - 0.012, z0 - 0.004), ring, 0.0030, 'X', 14, 6, CHROME)
    hard += p.box((x0, y0 - 0.014, z0 - 0.030), (0.016, 0.005, 0.048), CANVAS,
                  rot=Matrix.Rotation(math.radians(6), 4, 'X'))
    return hard


def _seam_run(p, z0, z1, count, mat=LEATHER, stud=BRASS):
    """A raised seam up the front of a tapering sleeve, plus its stitching.

    Placed station by station against `_at`, so it stays on the surface the
    whole way up instead of diving into it.
    """
    faces = []
    for i in range(count):
        z = z0 + (z1 - z0) * i / (count - 1)
        y = -_at(z)[1] / 2.0
        step = (z1 - z0) / (count - 1)
        faces += p.box((0.0, y, z), (0.011, 0.007, step * 1.25), mat)
        for sx in (-0.0058, 0.0058):
            p.cyl((sx, y - 0.0042, z), 0.0014, 0.0022, 'Y', 6, stud)
    return faces


def _stitch(p, z0, z1, x, y, count, mat=BRASS):
    """A run of stitching studs — the detail that says 'sewn', at 1.4 mm."""
    return p.rivets((x, y, z0), (x, y, z1), count, radius=0.0014,
                    height=0.0018, axis='Y', mat=mat)


# --------------------------------------------------------------------------
# Variations
# --------------------------------------------------------------------------

def leather(coll, mats):
    """Moulded leather sleeve with a bleached toe cap and a buckled strap.

    The one the item scanner ships on. The pale band at the wrist is doing real
    work: an unbroken brown taper reads as a boot, and the tonal break at the
    narrow end is what turns it back into a cuff.
    """
    p = Part(mats)
    hard = []

    p.loft(_sleeve_sections(), axis='Z', mat=LEATHER)

    # Bleached toe cap over the wrist third, a hair proud of the leather so it
    # reads as a second layer rather than a painted-on stripe. Its own stations
    # rather than a slice of the sleeve's, so the tonal break lands where the
    # taper is steepest.
    cap = [(0.000, 0.070), (0.026, 0.077), (0.048, 0.083)]
    p.loft([(z, rrect(w * 1.03, _at(z)[1] * 1.03)) for z, w in cap],
           axis='Z', mat=PALE)
    p.torus((0, 0, 0.048), 0.040, 0.0040, 'Z', 20, 6, PALE)

    # Front seam, riding the taper. A straight strip at a fixed y is inside the
    # leather at one end and floating off it at the other — see `_at`.
    _seam_run(p, 0.034, 0.206, 9)

    # Elbow-end welt: a leather roll finishing the wide opening.
    p.torus((0, 0, 0.213), 0.050, 0.0055, 'Z', 24, 6, LEATHER)

    # Wrist ferrule and the buckled strap that closes the cuff.
    p.torus((0, 0, 0.012), 0.035, 0.0045, 'Z', 20, 6, CHROME)
    hard += _buckle(p, (0, -_at(0.062)[1] / 2 - 0.002, 0.062))
    hard += p.box((0, _at(0.062)[1] / 2 - 0.002, 0.062), (0.052, 0.008, 0.014),
                  CANVAS)

    # Two keeper loops higher up, so the strap has somewhere to have come from.
    for z in (0.116, 0.170):
        p.torus((0, -_at(z)[1] / 2 - 0.003, z), 0.013, 0.0034, 'Y', 12, 5,
                CANVAS)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_ArmCuff_Leather", coll)


def webbing(coll, mats):
    """Open harness: three bands on two side rails, no shell.

    Reads as improvised kit rather than issued kit, and its open silhouette is
    the point — nothing here can be mistaken for the leather sleeve.
    """
    p = Part(mats)
    hard = []

    for sx in (-1, 1):
        tube_path(p, [(sx * 0.048, 0.004, 0.012), (sx * 0.052, 0.004, 0.110),
                      (sx * 0.050, 0.004, 0.202)], 0.0038, STEEL, seg=6)
    for z, w, d in ((0.030, 0.078, 0.064), (0.112, 0.098, 0.081),
                    (0.192, 0.110, 0.091)):
        p.loft([(z - 0.011, rrect(w, d)), (z + 0.011, rrect(w, d))],
               axis='Z', mat=CANVAS, cap=False)
        hard += p.box((0, -d / 2 - 0.002, z), (0.020, 0.007, 0.026), BRASS)
        p.torus((0, -d / 2 - 0.008, z), 0.010, 0.0028, 'X', 12, 5, CHROME)
    hard += p.box((0, 0.030, 0.108), (0.030, 0.012, 0.040), CANVAS)
    hard += _side_plate(p, 0.048, 0.108)
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_ArmCuff_Webbing", coll)


def _side_plate(p, x, z, mat=STEEL):
    """A small mounting boss on a rail — where kit bolts onto the harness."""
    hard = list(p.box((x, 0.000, z), (0.016, 0.030, 0.044), mat))
    for sz in (-0.014, 0.014):
        p.cyl((x + 0.008, 0.000, z + sz), 0.0028, 0.006, 'X', 8, CHROME)
    return hard


def plated(coll, mats):
    """Armoured bracer: overlapping steel lames over a dark liner.

    The heavy end of the family. Lames step outward as they climb so the
    silhouette is serrated, which is the only thing that survives at distance.
    """
    p = Part(mats)
    hard = []

    p.loft(_sleeve_sections(scale=0.94, corner=0.42), axis='Z', mat=RUBBER)
    for i in range(6):
        t = i / 5.0
        z = 0.020 + t * 0.170
        w = 0.076 + t * 0.040
        d = 0.063 + t * 0.032
        p.loft([(z - 0.016, rrect(w, d, 0.40)),
                (z + 0.014, rrect(w * 1.005, d * 1.005, 0.40))],
               axis='Z', mat=STEEL if i % 2 else PALE, cap=False)
        hard += p.box((0, -d / 2 - 0.001, z + 0.010), (0.022, 0.006, 0.008),
                      DARK)
    hard += p.box((0, 0, 0.010), (0.078, 0.066, 0.010), DARK)
    for sx in (-1, 1):
        hard += p.box((sx * 0.052, 0.010, 0.106), (0.010, 0.044, 0.150),
                      DARK)
        _stitch(p, 0.040, 0.174, sx * 0.056, 0.010, 7, mat=CHROME)
    hard += _buckle(p, (0, -0.040, 0.196), width=0.036)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_ArmCuff_Plated", coll)


# The grip variation's own stations. A forearm sleeve and a thing you close a
# hand around are not the same object at different scales: the sleeve has to
# clear a forearm (0.09-0.11 m across) and the grip has to fit inside a closed
# fist, which is 0.045-0.055 m and does not change with how big the device is.
# So the waist is held at 0.050 and the flare is confined to the two ends.
GRIP = [(0.000, 0.062, 0.055), (0.014, 0.066, 0.058), (0.030, 0.055, 0.049),
        (0.070, 0.050, 0.045), (0.110, 0.052, 0.047), (0.146, 0.062, 0.055),
        (0.172, 0.074, 0.065), (0.184, 0.070, 0.061)]


def _grip_at(z):
    """`_at`, for the grip's stations."""
    if z <= GRIP[0][0]:
        return GRIP[0][1], GRIP[0][2]
    for (z0, w0, d0), (z1, w1, d1) in zip(GRIP, GRIP[1:]):
        if z <= z1:
            t = (z - z0) / (z1 - z0)
            return w0 + (w1 - w0) * t, d0 + (d1 - d0) * t
    return GRIP[-1][1], GRIP[-1][2]


def grip(coll, mats):
    """Hand grip: a leather-wrapped haft with a pommel and a mounting collar.

    The variation the item scanner ships on, and the reason it exists is that
    the reference's cuff is worn, not held — a 0.11 m sleeve is something you
    strap to a forearm, and a hand cannot close around it. This keeps the whole
    leather-and-brass language and puts a waist in the middle you can actually
    hold, with the flare pushed out to the pommel and the collar so the hand has
    something to stop against at both ends.
    """
    p = Part(mats)
    hard = []

    p.loft([(z, rrect(w, d, 0.34)) for z, w, d in GRIP], axis='Z', mat=LEATHER)

    # Pommel cap in the bleached tone, so the grip has the family's two-tone
    # break even though it is a third the sleeve's size.
    p.loft([(z, rrect(w * 1.03, d * 1.03, 0.34)) for z, w, d in GRIP[:2]]
           + [(0.024, rrect(0.060 * 1.03, 0.053 * 1.03, 0.34))],
           axis='Z', mat=PALE)
    p.torus((0, 0, 0.024), 0.028, 0.0035, 'Z', 18, 6, PALE)
    p.torus((0, 0, 0.004), 0.026, 0.0040, 'Z', 18, 6, CHROME)

    # Wrap rings up the waist: the read that says 'this is the part you hold'.
    for i in range(9):
        z = 0.036 + i * 0.0092
        w, d = _grip_at(z)
        p.torus((0, 0, z), max(w, d) / 2.0 - 0.0016, 0.0028, 'Z', 16, 5, LEATHER)

    # Mounting collar the bracket bolts to, and the studs that show it bolted.
    hard += p.slab((-0.038, -0.033, 0.168), (0.038, 0.033, 0.178), STEEL)
    for sx in (-1, 1):
        p.cyl((sx * 0.030, 0.0, 0.178), 0.0042, 0.005, 'Z', 8, CHROME)
    p.torus((0, 0, 0.182), 0.033, 0.0042, 'Z', 20, 6, LEATHER)

    # Wrist lanyard off the pommel — the thing that stops a dropped scanner
    # being a lost scanner.
    hard += _buckle(p, (0, -_grip_at(0.030)[1] / 2 - 0.002, 0.030), width=0.022,
                    ring=0.009)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_ArmCuff_Grip", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    leather(collection("Coll_ArmCuff_Leather"), mats)
    webbing(collection("Coll_ArmCuff_Webbing"), mats)
    plated(collection("Coll_ArmCuff_Plated"), mats)
    grip(collection("Coll_ArmCuff_Grip"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
