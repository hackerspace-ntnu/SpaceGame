"""Hologram emitter heads — the lens hardware a map hologram projects from.

Three fittings: a drum-housed dish for pedestals and tripods, a flush annular
ring for table tops, and a single-lens stud for anything small. They are one
component because they are the same idea at three sizes, and separate from any
base because a projector lens is exactly the kind of part the next console,
helmet or vehicle dashboard will want without the furniture under it.

Deliberately quiet when unlit: the lens is tinted glass over a black throat,
and the only light is a small amber standby pip. The hologram itself is a
runtime shader in Unity — the model's job is to look like the thing it could
have come from, not to glow.

Every builder faces **+Z**: the head projects upward from a mount plane at the
given z. That matches how every base mounts one — on top.

The builders are importable (`holo_base.py` calls them directly), same pattern
as `panel_control.py`: a lens drum is a few hundred triangles and one shared
definition is the point.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402

from mathutils import Matrix  # noqa: E402


# Index 0 first: `bmesh.ops.bevel` stamps new faces with material index 0.
# Indices 0-9 match panel_control.MATS index-for-index so its builders and
# these can share one Part; GLASS extends the contract at 10.
STEEL, DARK, RUBBER, CHROME, CREAM, RED, BLUE, AMBER, BLACK, CRT = range(10)
GLASS = 10
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Cream_Aged", "Mat_Paint_Warn_Red",
        "Mat_Paint_Blue_Station", "Mat_Emissive_Amber",
        "Mat_Neutral_Black_Matte", "Mat_Emissive_Green_CRT",
        "Mat_Glass_Canopy_Tinted"]


def emitter_dish(p, at, radius=0.13):
    """Drum-housed dish emitter. `radius` is the drum radius; total height is
    ~0.06 m above the mount plane, aperture (lens) at z + 0.052."""
    x0, y0, z0 = at
    hard = []
    # Mount flange, then the drum with a slight taper so it reads machined.
    p.cyl((x0, y0, z0 + 0.006), radius + 0.010, 0.012, 'Z', 20, STEEL)
    p.cyl((x0, y0, z0 + 0.032), radius, 0.042, 'Z', 20, DARK,
          radius_top=radius * 0.94)
    # Recessed throat and the lens sunk below the rim.
    p.cyl((x0, y0, z0 + 0.047), radius * 0.80, 0.014, 'Z', 20, BLACK,
          radius_top=radius * 0.52)
    p.cyl((x0, y0, z0 + 0.0455), radius * 0.30, 0.006, 'Z', 16, GLASS)
    p.torus((x0, y0, z0 + 0.052), radius * 0.86, 0.007, 'Z', 20, 8, CHROME)
    # Three clamp lugs over the rim — the read that the lens comes out.
    for i in range(3):
        a = math.radians(90 + i * 120)
        r = radius * 0.90
        hard += p.box((x0 + math.cos(a) * r, y0 + math.sin(a) * r, z0 + 0.050),
                      (0.024, 0.016, 0.012), STEEL,
                      rot=Matrix.Rotation(a, 4, 'Z'))
    # Standby pip on the front face.
    p.cyl((x0, y0 - radius * 0.97 + 0.004, z0 + 0.024), 0.006, 0.012, 'Y', 8,
          AMBER)
    return hard


def emitter_ring(p, at, radius=0.17):
    """Flush annular emitter for a table top. `radius` is the outer radius;
    the centre stays open, lens windows sit ~0.030 m above the mount plane."""
    x0, y0, z0 = at
    hard = []
    p.tube((x0, y0, z0 + 0.013), radius, 0.048, 0.026, 'Z', 28, DARK)
    p.torus((x0, y0, z0 + 0.026), radius - 0.048, 0.006, 'Z', 28, 8, CHROME)
    # Eight lens windows around the top face, sitting 0.8 mm proud so they
    # never z-fight the ring they decorate.
    rm = radius - 0.024
    for i in range(8):
        a = math.radians(i * 45)
        hard += p.box((x0 + math.cos(a) * rm, y0 + math.sin(a) * rm,
                       z0 + 0.0278), (0.034, 0.017, 0.0044), GLASS,
                      rot=Matrix.Rotation(a + math.pi / 2, 4, 'Z'))
        # Bolt between windows.
        b = a + math.radians(22.5)
        p.cyl((x0 + math.cos(b) * rm, y0 + math.sin(b) * rm, z0 + 0.028),
              0.005, 0.005, 'Z', 8, STEEL)
    p.cyl((x0, y0 - radius + 0.003, z0 + 0.013), 0.006, 0.010, 'Y', 8, AMBER)
    return hard


def emitter_stud(p, at, radius=0.05):
    """Single-lens stud — the minimal head, for pucks and small devices.
    Total height ~0.033 m, lens at z + 0.030."""
    x0, y0, z0 = at
    hard = []
    p.cyl((x0, y0, z0 + 0.004), radius + 0.008, 0.008, 'Z', 16, STEEL)
    p.cyl((x0, y0, z0 + 0.017), radius, 0.026, 'Z', 16, DARK,
          radius_top=radius * 0.92)
    p.torus((x0, y0, z0 + 0.030), radius * 0.70, 0.005, 'Z', 16, 8, CHROME)
    p.cyl((x0, y0, z0 + 0.0265), radius * 0.56, 0.007, 'Z', 12, BLACK)
    p.cyl((x0, y0, z0 + 0.028), radius * 0.50, 0.006, 'Z', 12, GLASS)
    # Three recessed screws on the flange.
    for i in range(3):
        a = math.radians(30 + i * 120)
        r = radius + 0.004
        p.cyl((x0 + math.cos(a) * r, y0 + math.sin(a) * r, z0 + 0.0085),
              0.0035, 0.004, 'Z', 6, BLACK)
    p.cyl((x0, y0 - radius * 0.95, z0 + 0.014), 0.004, 0.010, 'Y', 8, AMBER)
    return hard


# --------------------------------------------------------------------------
# Variations — each head standalone on its own mount plane
# --------------------------------------------------------------------------

def dish(coll, mats):
    p = TrackedPart(mats)
    hard = emitter_dish(p, (0, 0, 0))
    p.bevel(hard, width=0.002, segments=2)
    return p.finish("Mesh_HoloEmitter_Dish", coll)


def ring(coll, mats):
    p = TrackedPart(mats)
    hard = emitter_ring(p, (0, 0, 0))
    p.bevel(hard, width=0.0015, segments=2)
    return p.finish("Mesh_HoloEmitter_Ring", coll)


def stud(coll, mats):
    p = TrackedPart(mats)
    hard = emitter_stud(p, (0, 0, 0))
    p.bevel(hard, width=0.0015, segments=2)
    return p.finish("Mesh_HoloEmitter_Stud", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    dish(collection("Coll_HoloEmitter_Dish"), mats)
    ring(collection("Coll_HoloEmitter_Ring"), mats)
    stud(collection("Coll_HoloEmitter_Stud"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
