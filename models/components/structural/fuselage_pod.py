"""Body sections for the dune ornithopter.

Built to the library convention: −Y is forward, so the nose points along −Y and
the tail boom runs out along +Y. Cut into four rather than modelled as one hull
so the boom and nose can be restated at other lengths without reopening the
body.

    blender --background --python fuselage_pod.py -- --out <path>/fuselage_pod.blend
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "..", ".."))
from _buildlib import *  # noqa: E402,F403

MATS = [
    "Mat_Paint_Hull_Bleached",     # 0  main body paint
    "Mat_Metal_Steel_Worn",        # 1  frames, bands
    "Mat_Metal_Steel_Dark",        # 2  fittings
    "Mat_Metal_Brass_Tarnished",   # 3  collars
    "Mat_Metal_Rust_Heavy",        # 4  rivets, corrosion
    "Mat_Paint_Olive_Deep",        # 5  shadow panels
    "Mat_Paint_Warn_Red",          # 6  stencil bands
    "Mat_Glass_Canopy_Tinted",     # 7  small instrument port
    "Mat_Neutral_Black_Matte",     # 8  vent recesses
]

# Where the core's front and rear faces sit, so nose and boom mate exactly.
CORE_FRONT_Y = -1.05
CORE_REAR_Y = 1.10
END_HW, END_HZ = 0.088, 0.104


def hull_profile(hw, hz):
    """Octagonal body section — cheap, and holds a crisp chine line."""
    return [(-hw, -hz * 0.35), (-hw * 0.62, -hz), (hw * 0.62, -hz),
            (hw, -hz * 0.35), (hw * 0.80, hz * 0.55), (hw * 0.34, hz),
            (-hw * 0.34, hz), (-hw * 0.80, hz * 0.55)]


def core(mats, coll):
    """Main body. Origin at the centre of mass, on the shoulder axis."""
    p = Part(mats)

    stations = [(-1.05, 0.088, 0.104), (-0.72, 0.170, 0.190),
                (-0.30, 0.222, 0.248), (0.10, 0.228, 0.255),
                (0.50, 0.196, 0.222), (0.85, 0.140, 0.165),
                (1.10, 0.092, 0.108)]
    p.loft([(y, hull_profile(hw, hz)) for y, hw, hz in stations],
           axis='Y', mat=0)

    # Spine ridge along the back — the sketch's central keel line.
    for i in range(5):
        t = i / 4
        y = -0.80 + 1.75 * t
        hz = 0.255 - 0.12 * abs(t - 0.35) * 1.6
        p.box((0, y, hz + 0.028), (0.085, 0.34, 0.055), 1)

    # Flank chine seams and rivet rows.
    for sx in (-1, 1):
        p.seam((sx * 0.205, -0.62, 0.010), (sx * 0.205, 0.72, 0.010),
               width=0.030, depth=0.022, axis='X', mat=1)
        p.rivets((sx * 0.196, -0.55, 0.098), (sx * 0.196, 0.66, 0.098), 5,
                 radius=0.018, height=0.012, axis='X', mat=4)

    # Shoulder mounting pads: flat bosses the pylons bolt to.
    for sx in (-1, 1):
        p.box((sx * 0.222, -0.02, 0.030), (0.055, 0.40, 0.40), 5)
        p.rivets((sx * 0.245, -0.16, 0.185), (sx * 0.245, 0.14, 0.185), 2,
                 radius=0.021, height=0.014, axis='X', mat=4)
        p.rivets((sx * 0.245, -0.16, -0.130), (sx * 0.245, 0.14, -0.130), 2,
                 radius=0.021, height=0.014, axis='X', mat=4)

    # Belly rail — what the rider cradle hangs from.
    p.box((0, 0.02, -0.238), (0.115, 1.42, 0.060), 1)
    for y in (-0.56, -0.10, 0.36, 0.72):
        p.box((0, y, -0.268), (0.165, 0.070, 0.048), 2)

    # Cooling louvres and a small instrument port.
    p.box((0, -0.52, 0.242), (0.150, 0.30, 0.030), 8)
    p.louvres((-0.070, -0.64, 0.238), (0.070, -0.40, 0.286), 3, mat=1)
    p.cyl((0, -0.86, 0.150), 0.052, 0.055, axis='Z', seg=10, mat=3)
    p.cyl((0, -0.86, 0.172), 0.040, 0.022, axis='Z', seg=10, mat=7)

    p.box((0, 0.92, 0.150), (0.075, 0.18, 0.030), 6)   # stencil band

    p.bevel(width=0.010, segments=1)
    return p.finish("Mesh_FuselagePod_Core", coll, origin=(0, 0, 0))


def nose(mats, coll):
    """Tapered nose plus the sketch's forward spike.

    Origin on the mating face; geometry runs forward along −Y.
    """
    p = Part(mats)

    stations = [(0.02, END_HW, END_HZ), (-0.22, 0.078, 0.092),
                (-0.46, 0.058, 0.070), (-0.66, 0.034, 0.042),
                (-0.78, 0.016, 0.019)]
    p.loft([(y, hull_profile(hw, hz)) for y, hw, hz in stations],
           axis='Y', mat=0)

    # Reinforcing collars where a homemade nose gets banded together.
    for y, r in ((-0.20, 0.086), (-0.48, 0.062)):
        p.tube((0, y, 0), r, 0.016, 0.036, axis='Y', seg=12, mat=1)

    # The spike: a long tapering probe, brass ferrule at its root.
    p.cyl((0, -0.86, 0), 0.020, 0.175, axis='Y', seg=8, mat=3)
    p.cyl((0, -1.10, 0), 0.013, 0.315, axis='Y', seg=8, mat=2,
          radius_top=0.003)
    for sx in (-1, 1):
        p.box((sx * 0.026, -0.80, 0), (0.016, 0.13, 0.052), 2)

    p.bevel(width=0.006, segments=1)
    return p.finish("Mesh_FuselagePod_Nose", coll, origin=(0, 0, 0))


def boom(mats, coll):
    """Tapered tail tube. Origin at the body end, running aft along +Y."""
    p = Part(mats)
    L = 1.92

    stations = [(0.0, END_HW, END_HZ), (0.42, 0.072, 0.086),
                (0.90, 0.058, 0.068), (1.40, 0.048, 0.056),
                (L, 0.044, 0.050)]
    p.loft([(y, hull_profile(hw, hz)) for y, hw, hz in stations],
           axis='Y', mat=0)

    # Collar bands with lug plates — the boom reads as segments bolted up.
    for y in (0.42, 0.98, 1.56):
        r = 0.078 - 0.020 * (y / L)
        p.tube((0, y, 0), r + 0.014, 0.018, 0.052, axis='Y', seg=10, mat=1)
        for sx in (-1, 1):
            p.box((sx * (r + 0.020), y, 0), (0.030, 0.070, 0.062), 2)

    # A pair of external control cables running the length of the boom.
    for sz in (-1, 1):
        p.cyl((0.0, L * 0.5, sz * 0.070), 0.010, L * 0.92, axis='Y', seg=6,
              mat=2)

    p.box((0, 0.10, 0.108), (0.060, 0.16, 0.026), 6)

    p.bevel(width=0.006, segments=1)
    return p.finish("Mesh_FuselagePod_Boom", coll, origin=(0, 0, 0))


def tail_hub(mats, coll):
    """Small spoked wheel plus the knuckle plate the tail fan pins into.

    Origin on the shaft; lugs radiate aft in the XY plane, matching the wing
    hub's convention so the same splay maths drives both.
    """
    p = Part(mats)
    R = 0.245

    p.tube((0, 0, 0), R, 0.042, 0.050, axis='Z', seg=14, mat=1)
    p.tube((0, 0, 0), 0.088, 0.024, 0.038, axis='Z', seg=10, mat=1)
    for i in range(5):
        a = 2 * math.pi * i / 5
        rmid = (0.088 + R - 0.042) / 2
        p.box((rmid * math.cos(a), rmid * math.sin(a), 0),
              (R - 0.088 - 0.04, 0.028, 0.022), 1,
              rot=Matrix.Rotation(a, 4, 'Z'))
    p.cyl((0, 0, 0), 0.062, 0.082, axis='Z', seg=12, mat=3)
    p.cyl((0, 0, 0), 0.024, 0.140, axis='Z', seg=8, mat=2)

    # Fan sockets, spread aft.
    spread = math.radians(112.0)
    for i in range(5):
        a = math.pi / 2 + (-spread / 2 + spread * i / 4)
        rot = Matrix.Rotation(a, 4, 'Z')
        p.box((0.145 * math.cos(a), 0.145 * math.sin(a), -0.052),
              (0.115, 0.075, 0.055), 1, rot=rot)
        p.cyl((0.185 * math.cos(a), 0.185 * math.sin(a), -0.052),
              0.022, 0.082, axis='Z', seg=6, mat=3)

    p.bevel(width=0.006, segments=1)
    return p.finish("Mesh_FuselagePod_TailHub", coll, origin=(0, 0, 0))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Coll_FuselagePod_Core", core),
                     ("Coll_FuselagePod_Nose", nose),
                     ("Coll_FuselagePod_Boom", boom),
                     ("Coll_FuselagePod_TailHub", tail_hub)):
        fn(mats, collection(name))

    report()
    save(out)


main()
