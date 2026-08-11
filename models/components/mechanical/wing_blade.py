"""Fan blades for the dune ornithopter's wings and tail.

Each blade is beige sailcloth lofted over a tapering steel spine, cross-braced
with plywood battens and socketed into a brass root collar. Homemade: the cloth
is a stretched sail, not a moulded aerofoil, and the battens are wood because
whoever built this had wood.

Local axis convention, and it matters for the rig:

    origin  = the root pin, at (0, 0, 0)
    +Y      = out along the blade toward the tip
    X       = blade width (chord)
    Z       = blade thickness, camber bulges +Z

Laying a bone along local +Y makes *twist* (angle of attack) a single-axis roll
on that bone, and *splay* a rotation of the parent hub about Z. Any other axis
choice turns both into compound rotations.

    blender --background --python wing_blade.py -- --out <path>/wing_blade.blend
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "..", ".."))
from _buildlib import *  # noqa: E402,F403

MATS = [
    "Mat_Fabric_Wing_Beige",       # 0  sailcloth
    "Mat_Fabric_Flag_Bleached",    # 1  sun-bleached patch panels
    "Mat_Fabric_Canvas_Faded",     # 2  lashings, dirty webbing
    "Mat_Metal_Steel_Worn",        # 3  spine, spar
    "Mat_Metal_Steel_Dark",        # 4  fittings, clevis
    "Mat_Metal_Brass_Tarnished",   # 5  root collar, pin
    "Mat_Metal_Rust_Heavy",        # 6  weathered ferrules
    "Mat_Wood_Ply_Worn",           # 7  battens
]

# Cross-section of the cloth: a flattened lens. Six points is enough — the
# blade reads by its plan silhouette, not by its edge thickness.
LENS = ((-1.0, 0.0), (-0.62, -1.0), (0.62, -1.0),
        (1.0, 0.0), (0.62, 1.0), (-0.62, 1.0))


def lerp_profile(points):
    """Piecewise-linear half-width along the blade, from (t, half_width) pairs."""
    def f(t):
        t = min(max(t, 0.0), 1.0)
        for (t0, w0), (t1, w1) in zip(points, points[1:]):
            if t <= t1:
                k = 0.0 if t1 <= t0 else (t - t0) / (t1 - t0)
                return w0 + (w1 - w0) * k
        return points[-1][1]
    return f


def cloth(p, length, wfunc, mat=0, thick=0.010, camber=0.055, droop=0.16,
          stations=9, sweep=0.0):
    """Loft the sailcloth panel from root to tip.

    `droop` sags the tip; `sweep` bends the blade back in X so a fan of them
    curves rather than radiating dead straight.
    """
    secs = []
    for i in range(stations):
        t = i / (stations - 1)
        hw = max(wfunc(t), 0.022)
        th = thick * (1.0 - 0.5 * t)
        dz = -droop * t * t
        dx = sweep * t * t
        prof = [(hw * u + dx,
                 th * v + dz + camber * (1.0 - u * u) * min(t * 3.0, 1.0))
                for u, v in LENS]
        secs.append((t * length, prof))
    return p.loft(secs, axis='Y', mat=mat)


def spine(p, length, droop=0.16, sweep=0.0, r_root=0.030, r_tip=0.009,
          segments=3, mat=3):
    """Tapering tube following the blade's droop and sweep."""
    faces = []
    for i in range(segments):
        t0, t1 = i / segments, (i + 1) / segments
        y0, y1 = t0 * length, t1 * length
        z0, z1 = -droop * t0 * t0, -droop * t1 * t1
        x0, x1 = sweep * t0 * t0, sweep * t1 * t1
        mid = ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)
        seg_len = math.dist((x0, y0, z0), (x1, y1, z1))
        # Tilt the segment so it follows the curve instead of stair-stepping.
        rot = (Matrix.Rotation(math.atan2(z1 - z0, y1 - y0), 4, 'X')
               @ Matrix.Rotation(-math.atan2(x1 - x0, y1 - y0), 4, 'Z'))
        faces += p.cyl(mid, r_root + (r_tip - r_root) * t0, seg_len, axis='Y',
                       seg=6, mat=mat, rot=rot,
                       radius_top=r_root + (r_tip - r_root) * t1)
    return faces


def battens(p, length, wfunc, droop=0.16, sweep=0.0, count=3, mat=7):
    """Plywood cross-braces holding the cloth open."""
    faces = []
    for i in range(count):
        t = 0.28 + 0.62 * (i / max(count - 1, 1))
        hw = wfunc(t) * 0.94
        y = t * length
        z = -droop * t * t + 0.02
        x = sweep * t * t
        faces += p.box((x, y, z), (hw * 2.0, 0.045, 0.022), mat)
    return faces


def root_fitting(p, mat_collar=5, mat_clevis=4, mat_ferrule=6, lean=False):
    """Brass collar, steel clevis and the pin the blade pivots on.

    `lean` drops the ferrule and coarsens the collar. Used on the short blades,
    where the fitting is a fifth the size on screen but was costing the same as
    it does on a three-metre membrane.
    """
    faces = p.cyl((0, 0.075, 0), 0.052, 0.11, axis='Y',
                  seg=8 if lean else 10, mat=mat_collar)
    if not lean:
        faces += p.cyl((0, 0.155, 0), 0.036, 0.06, axis='Y', seg=8,
                       mat=mat_ferrule)
    # Clevis: two cheeks straddling the pin bore.
    for sx in (-1, 1):
        faces += p.box((sx * 0.040, 0.012, 0), (0.020, 0.075, 0.098),
                       mat_clevis)
    faces += p.cyl((0, 0.012, 0), 0.016, 0.115, axis='X', seg=6,
                   mat=mat_clevis)
    return faces


def lashing(p, length, wfunc, droop, sweep, t, mat=2):
    """A strap wrapped round the spine — the cheapest read of 'field repair'."""
    y = t * length
    z = -droop * t * t
    x = sweep * t * t
    return p.torus((x, y, z), 0.034, 0.011, axis='Y', maj_seg=8, min_seg=4,
                   mat=mat)


def build_blade(mats, coll, name, length, widths, *, droop, sweep, camber,
                battens_n=3, tattered=False, patch=False, thick=0.010,
                lean=False):
    p = Part(mats)
    wfunc = lerp_profile(widths)

    # Only the hardware gets bevelled. Bevelling the cloth would double the
    # blade's triangle count to round off edges that should read as a taut
    # sail's cut hem — wrong visually as well as expensive, and there are
    # seventeen blades on the finished machine.
    hard = []
    cloth(p, length, wfunc, mat=0, thick=thick, camber=camber, droop=droop,
          sweep=sweep, stations=7 if lean else 9)
    hard += spine(p, length, droop=droop, sweep=sweep,
                  r_root=0.030 if length > 1.5 else 0.024,
                  r_tip=0.009, segments=2 if lean else 3)
    hard += battens(p, length, wfunc, droop=droop, sweep=sweep,
                    count=battens_n)
    hard += root_fitting(p, lean=lean)
    if not lean:
        lashing(p, length, wfunc, droop, sweep, 0.22)

    if patch:
        # A bleached replacement panel lashed over the mid-blade.
        t = 0.55
        hw = wfunc(t) * 0.8
        p.box((sweep * t * t, t * length, -droop * t * t + 0.035),
              (hw * 2.0, 0.42, 0.016), 1)
        hard += p.rivets((-hw, t * length - 0.19, -droop * t * t + 0.045),
                         (hw, t * length - 0.19, -droop * t * t + 0.045), 4,
                         radius=0.012, height=0.010, axis='Z', mat=6)

    if tattered:
        # Torn trailing edge: a couple of missing bites along the outer half,
        # faked as dark shadow gaps rather than by cutting the loft apart.
        for k, t in enumerate((0.64, 0.84)):
            hw = wfunc(t)
            p.box((hw * (0.72 + 0.06 * k) + sweep * t * t, t * length,
                   -droop * t * t), (0.10, 0.13 + 0.03 * k, 0.030), 2)

    p.bevel(faces=hard, width=0.006, segments=1)
    return p.finish(name, coll, origin=(0, 0, 0))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    # (t, half_width) — narrow at the root pin, widening to a paddle, rounded
    # off at the tip. This plan silhouette is what makes a fan of them read as
    # the reference sketch.
    primary = [(0.0, 0.048), (0.12, 0.098), (0.35, 0.150),
               (0.60, 0.192), (0.82, 0.212), (0.94, 0.188), (1.0, 0.072)]
    secondary = [(0.0, 0.046), (0.13, 0.094), (0.38, 0.146),
                 (0.63, 0.182), (0.84, 0.198), (0.95, 0.172), (1.0, 0.066)]
    covert = [(0.0, 0.044), (0.15, 0.090), (0.42, 0.138),
              (0.68, 0.166), (0.87, 0.174), (0.96, 0.150), (1.0, 0.060)]
    # The membrane is the wing's long swept "arm": much narrower for its
    # length, and it leads the fan rather than sitting in it.
    membrane = [(0.0, 0.062), (0.10, 0.150), (0.30, 0.212),
                (0.55, 0.238), (0.76, 0.220), (0.90, 0.160), (1.0, 0.048)]
    tailfan = [(0.0, 0.040), (0.16, 0.098), (0.45, 0.158),
               (0.72, 0.198), (0.90, 0.204), (0.97, 0.176), (1.0, 0.064)]

    specs = [
        ("Coll_WingBlade_Primary", "Mesh_WingBlade_Primary", 2.30, primary,
         dict(droop=0.20, sweep=0.13, camber=0.062)),
        ("Coll_WingBlade_Secondary", "Mesh_WingBlade_Secondary", 1.92,
         secondary, dict(droop=0.16, sweep=0.10, camber=0.056)),
        ("Coll_WingBlade_Covert", "Mesh_WingBlade_Covert", 1.48, covert,
         dict(droop=0.11, sweep=0.07, camber=0.048, battens_n=2, lean=True)),
        ("Coll_WingBlade_Membrane", "Mesh_WingBlade_Membrane", 2.95, membrane,
         dict(droop=0.26, sweep=0.34, camber=0.085, battens_n=4, thick=0.013)),
        ("Coll_WingBlade_Tattered", "Mesh_WingBlade_Tattered", 1.92, secondary,
         dict(droop=0.18, sweep=0.10, camber=0.050, tattered=True,
              patch=True)),
        ("Coll_WingBlade_TailFan", "Mesh_WingBlade_TailFan", 1.15, tailfan,
         dict(droop=0.08, sweep=0.05, camber=0.040, battens_n=2, lean=True)),
    ]

    for coll_name, mesh_name, length, widths, kw in specs:
        coll = collection(coll_name)
        build_blade(mats, coll, mesh_name, length, widths, **kw)

    report()
    save(out)


main()
