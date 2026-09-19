"""Face masks with eyes: what shows of a face above a high collar.

    blender --background --python components/apparel/goggle_mask.py -- --out components/apparel/goggle_mask.blend

Built in place on the Slim mannequin's head (see `_fit.py`); origin between the eyes.
Binds to `Head`.

    Coll_Mask_TwinGoggles   the sky soldier's: a dark hood band round the whole head between
                            collar and cap, two round tinted lenses in black rims
    Coll_Mask_SlitVisor     one wide tinted slit across a dark plate

Generation script -- historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from mathutils import Vector  # noqa: E402

import _buildlib as B  # noqa: E402
import _fit as F  # noqa: E402

MATS = ["Mat_Neutral_Slate_Dark", "Mat_Neutral_Black_Matte", "Mat_Glass_Canopy_Tinted"]
PLATE, RIM, LENS = range(3)
HEAD_Y = -0.047
HEAD_RX, HEAD_RY = 0.17, 0.195     # the head egg's section at eye height
EYE_Z = 3.28


def face_rows(z0, z1, a0, a1, grow, count=14):
    return [F.arc(Vector((0.0, HEAD_Y, z)), HEAD_RX + grow, HEAD_RY + grow, a0, a1, count) for z in (z0, (z0 + z1) / 2, z1)]


def front_y(x, grow):
    """Where the head's front surface is at `x`, pushed out by `grow`."""
    return HEAD_Y - (HEAD_RY + grow) * math.sqrt(max(0.0, 1.0 - (x / (HEAD_RX + grow)) ** 2))


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)
    eyes = Vector((0.0, front_y(0.0, 0.0), EYE_Z))

    twin = B.collection("Coll_Mask_TwinGoggles")
    p = B.Part(mats)
    # A closed band: whatever of the head shows between the collar and the cap is dark.
    p.sheet([F.arc(Vector((0.0, HEAD_Y, z)), HEAD_RX + 0.008, HEAD_RY + 0.008, 0.0, 360.0 * 31 / 32, 32)
             for z in (EYE_Z - 0.12, EYE_Z, EYE_Z + 0.06)], 0.012, PLATE, closed=True)
    F.bind(p.finish("Mesh_Mask_TwinGoggles_Hood", twin, origin=eyes), "Head")
    for side, x in (("L", 0.066), ("R", -0.066)):
        y = front_y(x, 0.02)
        p = B.Part(mats)
        p.torus((x, y - 0.012, EYE_Z), 0.04, 0.012, 'Y', 20, 8, RIM)
        F.bind(p.finish("Mesh_Mask_TwinGoggles_Rim_" + side, twin, origin=eyes), "Head")
        p = B.Part(mats)
        p.cyl((x, y - 0.006, EYE_Z), 0.036, 0.026, 'Y', 20, LENS)
        F.bind(p.finish("Mesh_Mask_TwinGoggles_Lens_" + side, twin, origin=eyes), "Head")

    slit = B.collection("Coll_Mask_SlitVisor")
    p = B.Part(mats)
    p.sheet(face_rows(EYE_Z - 0.08, EYE_Z + 0.07, 212.0, 328.0, 0.008), 0.014, PLATE)
    F.bind(p.finish("Mesh_Mask_SlitVisor_Plate", slit, origin=eyes), "Head")
    p = B.Part(mats)
    p.sheet(face_rows(EYE_Z - 0.022, EYE_Z + 0.022, 232.0, 308.0, 0.02), 0.012, LENS)
    F.bind(p.finish("Mesh_Mask_SlitVisor_Lens", slit, origin=eyes), "Head")

    B.save(out)
    B.report()


main()
