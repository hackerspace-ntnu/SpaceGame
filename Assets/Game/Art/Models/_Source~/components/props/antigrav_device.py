"""Antigravity emitters — the carried device behind the lift artifact.

The whole read of "this cancels gravity" rests on one trick: a solid thing held
inside a ring with a visible air gap around it. Nothing else in this library
does that, so it survives being shrunk to an inventory thumbnail where fine
greebling does not. All three variations spend their detail budget on making
that gap obvious and keep the rest of the body plain.

Sized as carried equipment, 0.22-0.30 m on the long axis. Origin sits at the
bottom of the grip so the device stands on a surface.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix  # noqa: E402

# Index 0 first, deliberately: `bmesh.ops.bevel` stamps every face it creates
# with material index 0, so this is the colour of every chamfered edge here.
SLATE, CHROME, DARK, COPPER, CRT, AMBER, GLASS, RUBBER, STEEL = range(9)
MATS = ["Mat_Neutral_Slate_Dark", "Mat_Metal_Chrome_Scuffed",
        "Mat_Metal_Steel_Dark", "Mat_Metal_Copper_Oxide",
        "Mat_Emissive_Green_CRT", "Mat_Emissive_Amber",
        "Mat_Glass_Canopy_Tinted", "Mat_Plastic_Rubber_Black",
        "Mat_Metal_Steel_Worn"]

# Bevel only boxy faces. A whole-part bevel at this scale eats thin swept
# tubes and welds their ends into blobs — see weather_station_device.py.
BEVEL_W = 0.0024


def _grip(p, hard, top_z, half=(0.026, 0.019), length=0.070):
    """Rubber-sleeved handle, origin-side. Shared so the three read as kit
    from one manufacturer rather than three unrelated props."""
    hx, hy = half
    hard += p.box((0, 0, top_z - length / 2), (hx * 2, hy * 2, length), DARK)
    for i in range(4):
        z = top_z - length + 0.012 + i * 0.015
        p.box((0, 0, z), (hx * 2.16, hy * 2.16, 0.007), RUBBER)
    hard += p.box((0, 0, top_z - length - 0.006), (hx * 2.3, hy * 2.3, 0.012),
                  STEEL)
    return top_z


def _arc(p, radius, a0, a1, tube, mat, axis='Z', centre=(0, 0, 0), steps=16):
    """A partial ring as a swept tube. `torus` can only do a closed loop, and
    the gap is the entire point of this component."""
    cx, cy, cz = centre
    pts = []
    for i in range(steps + 1):
        a = a0 + (a1 - a0) * i / steps
        c, s = math.cos(a) * radius, math.sin(a) * radius
        if axis == 'Z':
            pts.append((cx + c, cy + s, cz))
        elif axis == 'Y':
            pts.append((cx + c, cy, cz + s))
        else:
            pts.append((cx, cy + c, cz + s))
    return p.sweep(pts, tube, mat, seg=8)


def ring(coll, mats):
    """Split emitter ring with a core levitating in the gap.

    The one wired to the artifact. Two C-arcs leave a visible break at top and
    bottom, so the ring reads as held open by a field rather than as a solid
    hoop with a bead in it.
    """
    p = Part(mats)
    hard = []
    z = _grip(p, hard, 0.070)

    # Housing above the grip: the machine that drives the ring. `loft` section
    # offsets are absolute along the axis, not relative to anything already
    # built, so they have to carry `z` themselves — without it the housing is
    # generated inside the handle.
    hard += p.loft([(z + 0.000, [(-0.030, -0.023), (0.030, -0.023),
                                 (0.030, 0.023), (-0.030, 0.023)]),
                    (z + 0.042, [(-0.034, -0.026), (0.034, -0.026),
                                 (0.034, 0.026), (-0.034, 0.026)]),
                    (z + 0.058, [(-0.026, -0.020), (0.026, -0.020),
                                 (0.026, 0.020), (-0.026, 0.020)])],
                   axis='Z', mat=SLATE, cap=True)
    p.louvres((-0.024, 0.024, z + 0.008), (0.024, 0.028, z + 0.040), 4,
              mat=DARK, thickness=0.003)
    hard += p.box((0, -0.026, z + 0.030), (0.034, 0.008, 0.016), DARK)
    p.box((0, -0.030, z + 0.030), (0.026, 0.006, 0.010), CRT)

    # Fork carrying the ring, and the ring itself: two arcs with a gap.
    ring_z = z + 0.058 + 0.062
    for sx in (-1, 1):
        hard += p.box((sx * 0.028, 0, z + 0.078), (0.012, 0.026, 0.048),
                      SLATE, rot=Matrix.Rotation(sx * math.radians(12), 4, 'Y'))
        p.cyl((sx * 0.036, 0, ring_z - 0.004), 0.013, 0.016, 'X', 12, CHROME)

    # Arcs centred on +-X, so the fork meets each one mid-span and the two
    # breaks land at top and bottom where they are clearly visible. Centring
    # the gaps on +-X instead puts the fork pins inside the breaks and the ring
    # stops reading as a ring at all.
    R = 0.062
    gap = math.radians(9)
    half = math.pi / 2 - gap
    _arc(p, R, -half, half, 0.0075, CHROME, axis='Y', centre=(0, 0, ring_z))
    _arc(p, R, math.pi - half, math.pi + half, 0.0075, CHROME, axis='Y',
         centre=(0, 0, ring_z))
    # Copper windings on the arcs, kept off the breaks so they stay clean.
    for k in range(3):
        a = (k - 1) * math.radians(30)
        for base in (0.0, math.pi):
            _arc(p, R, base + a - 0.10, base + a + 0.10, 0.0098, COPPER,
                 axis='Y', centre=(0, 0, ring_z), steps=4)

    # The core: a sphere floating clear of the ring on every side.
    p.ellipsoid((0, 0, ring_z), (0.026, 0.026, 0.026), GLASS, seg=16, rings=10)
    p.ellipsoid((0, 0, ring_z), (0.018, 0.018, 0.018), CRT, seg=14, rings=8)
    for sx in (-1, 1):
        p.cyl((sx * 0.030, 0, ring_z), 0.006, 0.014, 'X', 8, AMBER)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_AntiGrav_Ring", coll)


def pylon(coll, mats):
    """Stacked-plate emitter — the industrial, bolted-together variant."""
    p = Part(mats)
    hard = []
    z = _grip(p, hard, 0.062, half=(0.022, 0.022), length=0.062)

    hard += p.box((0, 0, z + 0.020), (0.070, 0.058, 0.040), SLATE)
    p.louvres((-0.030, -0.030, z + 0.006), (0.030, -0.026, z + 0.036), 4,
              mat=DARK, thickness=0.003)

    # Four plates with air between them — the stack is the silhouette.
    for i in range(4):
        pz = z + 0.052 + i * 0.026
        r = 0.052 - i * 0.007
        p.cyl((0, 0, pz), r, 0.010, 'Z', 20, CHROME)
        p.tube((0, 0, pz), r, 0.006, 0.013, 'Z', 20, COPPER)
        if i < 3:
            for k in range(3):
                a = 2 * math.pi * k / 3 + i * 0.4
                p.cyl((math.cos(a) * (r - 0.014), math.sin(a) * (r - 0.014),
                       pz + 0.013), 0.004, 0.013, 'Z', 6, STEEL)

    cap_z = z + 0.052 + 3 * 0.026
    p.ellipsoid((0, 0, cap_z + 0.026), (0.020, 0.020, 0.022), CRT,
                seg=14, rings=8)
    p.cyl((0, 0, cap_z + 0.010), 0.016, 0.012, 'Z', 12, DARK)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_AntiGrav_Pylon", coll)


def orb(coll, mats):
    """Gimbal cage — three rings on different axes round a suspended core.

    The dressiest of the three and the least like a tool; kept for set
    dressing and shrines rather than for a belt.
    """
    p = Part(mats)
    hard = []
    z = _grip(p, hard, 0.052, half=(0.024, 0.024), length=0.052)

    hard += p.loft([(z + 0.000, [(-0.040, -0.040), (0.040, -0.040),
                                 (0.040, 0.040), (-0.040, 0.040)]),
                    (z + 0.030, [(-0.032, -0.032), (0.032, -0.032),
                                 (0.032, 0.032), (-0.032, 0.032)])],
                   axis='Z', mat=SLATE, cap=True)
    p.cyl((0, 0, z + 0.036), 0.030, 0.014, 'Z', 16, DARK)

    c_z = z + 0.036 + 0.058
    p.cyl((0, 0, z + 0.052), 0.008, 0.032, 'Z', 10, STEEL)

    # Three gimbal rings, each a full loop on its own axis.
    p.torus((0, 0, c_z), 0.052, 0.0055, 'Z', 24, 8, CHROME)
    p.torus((0, 0, c_z), 0.045, 0.0050, 'Y', 24, 8, CHROME)
    p.torus((0, 0, c_z), 0.038, 0.0045, 'X', 24, 8, COPPER)
    for sx in (-1, 1):
        p.cyl((sx * 0.052, 0, c_z), 0.008, 0.010, 'X', 8, DARK)
        p.cyl((0, sx * 0.045, c_z), 0.007, 0.010, 'Y', 8, DARK)

    p.ellipsoid((0, 0, c_z), (0.022, 0.022, 0.022), GLASS, seg=16, rings=10)
    p.ellipsoid((0, 0, c_z), (0.014, 0.014, 0.014), AMBER, seg=14, rings=8)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_AntiGrav_Orb", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    ring(collection("Coll_AntiGrav_Ring"), mats)
    pylon(collection("Coll_AntiGrav_Pylon"), mats)
    orb(collection("Coll_AntiGrav_Orb"), mats)
    save(out)
    report()


main()
