"""Cabin and bay lighting.

Small, cheap, and used more times than anything else in the ship — which is why
they are their own component rather than being modelled into the ceiling. Four
different fittings down one corridor is most of what makes an interior look
accumulated rather than installed.

Ceiling fittings hang from an origin on the ceiling plane, pointing down -Z.
The clamp lamp mounts on a wall or pipe, origin at its bracket.

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

STEEL, DARK, CREAM, WARM, AMBER, RED, RUST, RUBBER = range(8)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Cream_Aged", "Mat_Emissive_Cabin_Warm",
        "Mat_Emissive_Amber", "Mat_Emissive_Red_Warn",
        "Mat_Metal_Rust_Heavy", "Mat_Plastic_Rubber_Black"]


def strip(coll, mats):
    """1.2 m batten with a yellowed diffuser — the cabin's main light, run in
    a line down the ceiling."""
    p = Part(mats)
    L = 1.20
    p.slab((-0.06, -L / 2, -0.09), (0.06, L / 2, 0.0), STEEL)
    # End caps, then the diffuser slung under the batten.
    for s in (-1, 1):
        p.box((0, s * (L / 2 - 0.02), -0.07), (0.13, 0.04, 0.10), DARK)
    p.box((0, 0, -0.13), (0.11, L - 0.08, 0.05), CREAM)
    p.box((0, 0, -0.115), (0.085, L - 0.14, 0.03), WARM)
    # One dead section at the far end, taped over. The RV read.
    p.box((0, L * 0.36, -0.128), (0.115, L * 0.22, 0.045), RUST)
    p.rivets((-0.045, -L / 2 + 0.06, 0.0), (-0.045, L / 2 - 0.06, 0.0), 4,
             radius=0.015, height=0.012, mat=DARK)
    p.bevel(width=0.005, segments=2)
    return p.finish("Mesh_Light_Strip", coll)


def dome(coll, mats):
    """Recessed dome in a caged bezel — the fitting used where a batten would
    be knocked off, over the cargo doorway and the workstation."""
    p = Part(mats)
    p.cyl((0, 0, -0.03), 0.19, 0.06, 'Z', 16, STEEL)
    p.cyl((0, 0, -0.075), 0.15, 0.05, 'Z', 16, CREAM)
    p.cyl((0, 0, -0.10), 0.115, 0.03, 'Z', 16, WARM)
    # Protective cage.
    p.torus((0, 0, -0.10), 0.175, 0.012, 'Z', 16, 6, DARK)
    for i in range(4):
        a = math.pi / 2 * i
        p.box((math.cos(a) * 0.09, math.sin(a) * 0.09, -0.085),
              (0.20, 0.014, 0.014), DARK, rot=Matrix.Rotation(a, 4, 'Z'))
    p.cyl((0, 0, -0.115), 0.028, 0.02, 'Z', 8, DARK)
    p.bevel(width=0.004, segments=2)
    return p.finish("Mesh_Light_Dome", coll)


def clamp(coll, mats):
    """Work lamp clamped to a pipe, aimed by hand. Its arm gives it a
    silhouette nothing else in the set has."""
    p = Part(mats)
    # Jaw clamped onto a 55 mm pipe.
    p.tube((0, 0, 0), 0.055, 0.018, 0.07, 'Y', 12, DARK)
    p.box((0.06, 0, -0.03), (0.09, 0.06, 0.06), DARK)
    p.cyl((0.10, 0, -0.04), 0.012, 0.07, 'Z', 6, STEEL)
    p.cyl((0.10, 0, -0.08), 0.024, 0.02, 'Z', 8, RUBBER)
    # Two-section arm with a knuckle.
    arm1 = Matrix.Rotation(math.radians(-38), 4, 'Y')
    p.cyl((0.13, 0, 0.11), 0.014, 0.26, 'Z', 8, STEEL, rot=arm1)
    p.cyl((0.21, 0, 0.21), 0.026, 0.05, 'Y', 10, DARK)
    arm2 = Matrix.Rotation(math.radians(58), 4, 'Y')
    p.cyl((0.30, 0, 0.25), 0.014, 0.22, 'Z', 8, STEEL, rot=arm2)
    # Conical shade with a scorched rim.
    head = Matrix.Rotation(math.radians(122), 4, 'Y')
    p.cyl((0.39, 0, 0.28), 0.055, 0.15, 'Z', 14, STEEL, rot=head,
          radius_top=0.11)
    p.torus((0.45, 0, 0.32), 0.11, 0.012, 'Z', 14, 6, RUST)
    p.cyl((0.43, 0, 0.31), 0.075, 0.02, 'Z', 12, WARM, rot=head)
    # Flex trailing back to the clamp.
    for i in range(6):
        t = i / 5.0
        p.cyl((0.10 + t * 0.05, 0.035, 0.02 + t * 0.20 - math.sin(t * 3.1) * 0.05),
              0.009, 0.06, 'Z', 6, RUBBER)
    p.bevel(width=0.004, segments=1)
    return p.finish("Mesh_Light_Clamp", coll)


def emergency(coll, mats):
    """Caged red beacon over the doors — reads as a warning even unlit, which
    is what a bay full of moving panels needs."""
    p = Part(mats)
    p.box((0, 0, -0.045), (0.16, 0.20, 0.09), DARK)
    p.rivets((0, -0.07, 0.0), (0, 0.07, 0.0), 2, radius=0.016, height=0.012,
             mat=STEEL)
    # Fresnel drum.
    p.cyl((0, 0, -0.14), 0.075, 0.11, 'Z', 14, RED)
    for i in range(4):
        p.torus((0, 0, -0.10 - i * 0.026), 0.078, 0.008, 'Z', 14, 6, RED)
    p.cyl((0, 0, -0.20), 0.058, 0.02, 'Z', 12, DARK)
    # Cage.
    for i in range(4):
        a = math.pi / 2 * i + math.pi / 4
        p.box((math.cos(a) * 0.085, math.sin(a) * 0.085, -0.14),
              (0.016, 0.016, 0.20), STEEL)
    p.torus((0, 0, -0.21), 0.085, 0.011, 'Z', 12, 6, STEEL)
    p.bevel(width=0.004, segments=2)
    return p.finish("Mesh_Light_Emergency", coll)


def festoon(coll, mats):
    """A string of mismatched bulbs on a sagging flex, taped up along the bay.
    Nobody fitted this; somebody hung it. Nothing else in the library says
    'lived in' as directly."""
    p = Part(mats)
    L, sag = 1.40, 0.16
    steps = 10
    for i in range(steps):
        t0, t1 = i / steps, (i + 1) / steps
        z0 = -sag * math.sin(math.pi * t0)
        z1 = -sag * math.sin(math.pi * t1)
        p.cyl((0, -L / 2 + (t0 + t1) / 2 * L, (z0 + z1) / 2), 0.008,
              L / steps * 1.2, 'Y', 6, RUBBER)
    for i, mat in enumerate((WARM, AMBER, WARM, RED, WARM)):
        t = (i + 0.5) / 5
        y = -L / 2 + t * L
        z = -sag * math.sin(math.pi * t)
        p.cyl((0, y, z - 0.03), 0.022, 0.05, 'Z', 8, DARK)
        p.cyl((0, y, z - 0.075), 0.036, 0.07, 'Z', 10, mat)
        p.cyl((0, y, z - 0.115), 0.022, 0.02, 'Z', 8, mat)
    for s in (-1, 1):
        p.box((0, s * L / 2, 0.0), (0.06, 0.05, 0.03), RUST)
    p.bevel(width=0.003, segments=1)
    return p.finish("Mesh_Light_Festoon", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Strip", strip), ("Dome", dome), ("Clamp", clamp),
                     ("Emergency", emergency), ("Festoon", festoon)):
        fn(collection("Coll_Light_" + name), mats)

    report()
    save(out)


main()
