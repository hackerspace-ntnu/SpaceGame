"""High collars: stiff cloth rising from the shoulders round the neck.

    blender --background --python components/apparel/high_collar.py -- --out components/apparel/high_collar.blend

Built in place on the Slim mannequin (see `_fit.py`); origin at the base of the neck.
Binds to `Spine2`, so the head turns inside it.

    Coll_Collar_FaceWrap   the sky soldier's: up past the mouth to just under the eyes,
                           orange-trimmed top and base
    Coll_Collar_Folded     chin height, the top folded out over itself

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

MATS = ["Mat_Fabric_Tarp_Azure", "Mat_Paint_Safety_Orange"]
CLOTH, TRIM = range(2)
AROUND = 32
SHELL = 0.02
BODY_Y = -0.045              # the torso and head centre line sits forward of y = 0


def ring(z, rx, ry, rake=0.0):
    """A closed ring; `rake` lifts the front (-Y) and drops the back by that much."""
    pts = []
    for i in range(AROUND):
        a = 360.0 * i / AROUND
        p = F.ellipse(Vector((0.0, BODY_Y, z)), rx, ry, a)
        p.z += rake * -math.sin(math.radians(a))
        pts.append(p)
    return pts


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)
    j = F.body_joints()
    neck_base = Vector((0.0, BODY_Y, j["Neck"][0].z))

    wrap = B.collection("Coll_Collar_FaceWrap")
    p = B.Part(mats)
    p.sheet([ring(2.80, 0.27, 0.22), ring(2.95, 0.225, 0.215), ring(3.10, 0.205, 0.24),
             ring(3.17, 0.208, 0.25), ring(3.21, 0.21, 0.252)], SHELL, CLOTH, closed=True)
    F.bind(p.finish("Mesh_Collar_FaceWrap_Shell", wrap, origin=neck_base), "Spine2")
    p = B.Part(mats)
    # Sits on the shell's outer face: centred 6 mm outside it, so it is proud of the cloth, never flush.
    p.sheet([ring(3.185, 0.229, 0.271), ring(3.215, 0.23, 0.272)], 0.012, TRIM, closed=True)
    F.bind(p.finish("Mesh_Collar_FaceWrap_TopTrim", wrap, origin=neck_base), "Spine2")
    p = B.Part(mats)
    p.sheet([ring(2.79, 0.29, 0.24), ring(2.82, 0.286, 0.237)], 0.012, TRIM, closed=True)
    F.bind(p.finish("Mesh_Collar_FaceWrap_BaseTrim", wrap, origin=neck_base), "Spine2")

    folded = B.collection("Coll_Collar_Folded")
    p = B.Part(mats)
    p.sheet([ring(2.80, 0.27, 0.22), ring(2.95, 0.225, 0.215), ring(3.06, 0.2, 0.22)], SHELL, CLOTH, closed=True)
    F.bind(p.finish("Mesh_Collar_Folded_Stand", folded, origin=neck_base), "Spine2")
    p = B.Part(mats)
    p.sheet([ring(3.07, 0.215, 0.235), ring(3.04, 0.26, 0.275), ring(2.96, 0.29, 0.3)], 0.016, CLOTH, closed=True)
    F.bind(p.finish("Mesh_Collar_Folded_Fold", folded, origin=neck_base), "Spine2")

    B.save(out)
    B.report()


main()
