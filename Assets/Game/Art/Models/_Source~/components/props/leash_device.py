"""Tether emitters — the carried device behind the leash artifact.

A leash is a hard thing to make legible as a static prop, because the part that
does the work is a line that does not exist until it is fired. So all three
variations put the *cable* on the model: a wound drum with visible turns, a
fairlead it feeds through, and a hook hanging off the end. A bare box with a
muzzle would read as a gun.

Sized as carried equipment, 0.20-0.28 m on the long axis. Origin sits at the
bottom of the grip or mount, so the device stands on a surface.

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

# Index 0 first: `bmesh.ops.bevel` stamps new faces with material index 0, so
# this is the colour of every chamfered edge in the file.
STEEL, ORANGE, DARK, RUBBER, BRASS, AMBER, CHROME, CRT = range(8)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Paint_Safety_Orange",
        "Mat_Metal_Steel_Dark", "Mat_Plastic_Rubber_Black",
        "Mat_Metal_Brass_Tarnished", "Mat_Emissive_Amber",
        "Mat_Metal_Chrome_Scuffed", "Mat_Emissive_Green_CRT"]

# Bevel only boxy faces — a whole-part bevel at this scale destroys thin swept
# cable and hook geometry. See weather_station_device.py.
BEVEL_W = 0.0022


def _drum(p, centre, r, width, turns, mat_core=DARK, mat_cable=BRASS):
    """A cable drum: two flanges, a core, and visible wound turns.

    The turns are what sell it. A smooth cylinder between two discs reads as a
    roller; a stack of separate rings reads as something with rope on it.
    """
    cx, cy, cz = centre
    p.cyl((cx, cy, cz), r * 0.62, width, 'X', 16, mat_core)
    for sx in (-1, 1):
        p.cyl((cx + sx * width / 2, cy, cz), r, 0.008, 'X', 20, CHROME)
    span = width - 0.014
    for i in range(turns):
        x = cx - span / 2 + span * (i + 0.5) / turns
        p.torus((x, cy, cz), r * 0.72, 0.0055, 'X', 16, 6, mat_cable)


def _hook(p, tip, scale=1.0, mat=CHROME):
    """A snap hook drawn as a J: shank, curve, and a barb turning back."""
    tx, ty, tz = tip
    pts = [(tx, ty + 0.030 * scale, tz + 0.010 * scale)]
    for i in range(9):
        a = math.pi * 1.15 * i / 8 - math.pi * 0.12
        pts.append((tx,
                    ty + math.cos(a) * 0.019 * scale,
                    tz - 0.006 * scale + math.sin(a) * 0.019 * scale))
    p.sweep(pts, 0.0038 * scale, mat, seg=6)
    p.torus((tx, ty + 0.034 * scale, tz + 0.012 * scale),
            0.009 * scale, 0.0032 * scale, 'X', 12, 6, mat)


def spool(coll, mats):
    """Pistol-grip tether gun with the drum mounted on the side.

    The one wired to the artifact. The exposed wound drum is the whole read,
    so it sits proud of the body rather than being enclosed.
    """
    p = Part(mats)
    hard = []

    # Grip, canted back like a tool handle rather than standing vertical.
    tilt = Matrix.Rotation(math.radians(-12), 4, 'X')
    hard += p.box((0, 0.010, 0.044), (0.034, 0.042, 0.088), DARK, rot=tilt)
    for i in range(4):
        p.box((0, 0.010 + i * 0.0035, 0.018 + i * 0.016),
              (0.037, 0.046, 0.008), RUBBER, rot=tilt)
    hard += p.box((0, 0.016, 0.004), (0.040, 0.050, 0.010), STEEL)

    body_z = 0.104
    # Receiver, kept narrow in X so the drum beside it stays proud of the
    # silhouette. A wider body simply hides the drum, which is the one feature
    # that stops this reading as a pistol.
    hard += p.loft([(-0.024, [(-0.040, body_z - 0.024), (0.052, body_z - 0.030),
                              (0.052, body_z + 0.026), (-0.040, body_z + 0.022)]),
                    (0.024, [(-0.040, body_z - 0.024), (0.052, body_z - 0.030),
                             (0.052, body_z + 0.026), (-0.040, body_z + 0.022)])],
                   axis='X', mat=ORANGE, cap=True)
    # Contrast panel so the orange does not read as one flat slab.
    hard += p.box((0, -0.012, body_z + 0.020), (0.046, 0.052, 0.014), DARK)
    p.box((0, -0.040, body_z + 0.006), (0.026, 0.006, 0.016), CRT)
    p.rivets((-0.018, 0.030, body_z + 0.028), (0.018, 0.030, body_z + 0.028), 3,
             radius=0.004, height=0.003, mat=STEEL)
    p.seam((-0.024, 0.026, body_z - 0.014), (0.024, 0.026, body_z - 0.014),
           width=0.010, depth=0.006, axis='Y', mat=DARK)

    # Drum on the left flank, clear of the receiver's outer face at x=-0.024.
    _drum(p, (-0.046, 0.006, body_z), 0.040, 0.030, 4)
    p.cyl((-0.064, 0.006, body_z), 0.012, 0.010, 'X', 10, STEEL)
    for i in range(3):
        a = 2 * math.pi * i / 3
        p.box((-0.068, 0.006 + math.cos(a) * 0.018, body_z + math.sin(a) * 0.018),
              (0.008, 0.028, 0.007), DARK,
              rot=Matrix.Rotation(a, 4, 'X'))

    # Fairlead the cable feeds through, then the cable and hook.
    p.torus((0, -0.050, body_z - 0.008), 0.014, 0.0055, 'Y', 14, 6, CHROME)
    hard += p.box((0, -0.044, body_z - 0.008), (0.030, 0.014, 0.030), STEEL)
    p.sweep([(-0.040, 0.006, body_z - 0.034), (-0.020, -0.030, body_z - 0.020),
             (0, -0.050, body_z - 0.008), (0, -0.076, body_z - 0.014)],
            0.0034, BRASS, seg=6)
    _hook(p, (0, -0.086, body_z - 0.030))
    p.cyl((0.048, -0.006, body_z + 0.024), 0.006, 0.014, 'Z', 8, AMBER)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_Leash_Spool", coll)


def gauntlet(coll, mats):
    """Wrist-cuff tether — the hands-free variant.

    A short wide cuff, not a long sleeve: `cyl_patch` builds about +Z, so a
    tall shell reads as a bucket with the drum lost down inside it. Keeping the
    shell shorter than it is wide, and hanging the drum off the front face
    rather than over the mouth, is what makes it read as worn hardware.
    """
    p = Part(mats)
    hard = []

    az = (math.radians(-128), math.radians(128))
    p.cyl_patch((0, 0, 0.038), 0.050, 0.009, az, (0.010, 0.066),
                mat=ORANGE, seg=16, rows=3, taper=0.94)
    for z in (0.012, 0.058):
        p.cyl_patch((0, 0, 0.038), 0.052, 0.007, az, (z, z + 0.008),
                    mat=DARK, seg=16, rows=1, taper=1.0)
    # Padding at the open side, where the cuff would bear on the arm.
    for sx in (-1, 1):
        p.box((sx * 0.030, 0.044, 0.038), (0.026, 0.014, 0.048), RUBBER)

    # Drum on the front face, outside the cuff's radius so it is never hidden.
    _drum(p, (0, -0.062, 0.040), 0.028, 0.030, 3)
    hard += p.box((0, -0.050, 0.040), (0.044, 0.020, 0.040), DARK)
    hard += p.box((0, -0.030, 0.062), (0.036, 0.026, 0.014), STEEL)
    p.box((0, -0.030, 0.070), (0.024, 0.014, 0.005), CRT)

    p.torus((0, -0.062, 0.006), 0.010, 0.0042, 'Z', 12, 6, CHROME)
    p.sweep([(0, -0.062, 0.020), (0, -0.062, 0.006), (0, -0.066, -0.008)],
            0.0030, BRASS, seg=6)
    _hook(p, (0, -0.058, -0.020), scale=0.85)
    p.cyl((0.030, -0.046, 0.062), 0.005, 0.012, 'Z', 8, AMBER)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_Leash_Gauntlet", coll)


def winch(coll, mats):
    """Deck winch on feet — the heavy, emplaced variant.

    Built for anchoring something rather than carrying it, so it gets a wider
    drum and a bolt-down frame instead of a grip.
    """
    p = Part(mats)
    hard = []

    hard += p.slab((-0.070, -0.048, 0.000), (0.070, 0.048, 0.014), DARK)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.cyl((sx * 0.058, sy * 0.036, 0.014), 0.008, 0.008, 'Z', 8, STEEL)

    # Two side frames carrying the drum between them.
    for sx in (-1, 1):
        hard += p.box((sx * 0.052, 0, 0.056), (0.016, 0.076, 0.084), ORANGE)
        p.cyl((sx * 0.052, 0, 0.078), 0.020, 0.020, 'X', 12, DARK)
    _drum(p, (0, 0, 0.078), 0.042, 0.076, 7)

    hard += p.box((0, 0.044, 0.036), (0.060, 0.024, 0.044), DARK)
    p.box((0, 0.058, 0.040), (0.032, 0.006, 0.018), CRT)
    p.cyl((0, 0.044, 0.062), 0.007, 0.016, 'Z', 8, AMBER)

    # Fairlead across the front and the cable over it.
    for sx in (-1, 1):
        p.cyl((sx * 0.026, -0.048, 0.040), 0.010, 0.026, 'Z', 10, CHROME)
    p.sweep([(0, 0, 0.114), (0, -0.030, 0.100), (0, -0.048, 0.056),
             (0, -0.054, 0.024)], 0.0040, BRASS, seg=6)
    _hook(p, (0, -0.062, 0.008), scale=1.1)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_Leash_Winch", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    spool(collection("Coll_Leash_Spool"), mats)
    gauntlet(collection("Coll_Leash_Gauntlet"), mats)
    winch(collection("Coll_Leash_Winch"), mats)
    save(out)
    report()


main()
