"""Scarves: a loop round the collar and a tail of cloth.

    blender --background --python components/apparel/trail_scarf.py -- --out components/apparel/trail_scarf.blend

Built in place on the Slim mannequin (see `_fit.py`), outside the high collar; origin at
the back of the neck. Binds to `Spine2`.

    Coll_Scarf_Streaming   the sky soldier's: a long tail blown out behind and to the left,
                           rippling and narrowing to a point
    Coll_Scarf_Hanging     two tails hanging down the back

The tail is posed, not simulated: it is the shape the wind leaves it in, and a cloth
component in Unity can take over from there.

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

MATS = ["Mat_Fabric_Sail_Orange"]
CLOTH = 0
BODY_Y = -0.045
LOOP_Z = 2.97
CLOTH_T = 0.014


def loop(coll, mats, origin, name):
    rows = [F.arc(Vector((0.0, BODY_Y, z)), 0.245, 0.245, 0.0, 360.0 * 31 / 32, 32) for z in (LOOP_Z - 0.05, LOOP_Z + 0.05)]
    p = B.Part(mats)
    p.sheet(rows, 0.03, CLOTH, closed=True)
    F.bind(p.finish(name, coll, origin=origin), "Spine2")


def ribbon(path, widths, twist_deg):
    """Two edges of a ribbon along `path`; its width stands mostly upright, twisting along."""
    rows = []
    for i, (pt, w) in enumerate(zip(path, widths)):
        ahead = path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]
        ahead.normalize()
        across = Vector((0, 0, 1)) - ahead * ahead.z
        across.normalize()
        side = ahead.cross(across).normalized()
        a = math.radians(twist_deg[i])
        edge = across * math.cos(a) + side * math.sin(a)
        rows.append([pt + edge * (w / 2), pt - edge * (w / 2)])
    return rows


def curve(points, per_span=5):
    """A Catmull-Rom path through `points`."""
    pts = [Vector(p) for p in points]
    out = []
    for i in range(len(pts) - 1):
        p0, p1, p2, p3 = pts[max(i - 1, 0)], pts[i], pts[i + 1], pts[min(i + 2, len(pts) - 1)]
        for k in range(per_span):
            t = k / per_span
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(pts[-1])
    return out


def tail(coll, mats, origin, name, knots, width0, width1, twist):
    path = curve(knots)
    n = len(path)
    widths = [F.lerp(width0, width1, (i / (n - 1)) ** 1.4) for i in range(n)]
    twists = [twist * math.sin(math.pi * 1.5 * i / (n - 1)) for i in range(n)]
    p = B.Part(mats)
    p.sheet(ribbon(path, widths, twists), CLOTH_T, CLOTH)
    F.bind(p.finish(name, coll, origin=origin), "Spine2")


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)
    origin = Vector((0.0, BODY_Y + 0.245, LOOP_Z))

    streaming = B.collection("Coll_Scarf_Streaming")
    loop(streaming, mats, origin, "Mesh_Scarf_Streaming_Loop")
    tail(streaming, mats, origin, "Mesh_Scarf_Streaming_Tail",
         [(0.05, 0.19, 2.96), (0.3, 0.34, 2.9), (0.62, 0.5, 2.84), (0.95, 0.62, 2.9), (1.25, 0.76, 2.83), (1.55, 0.86, 2.9)],
         0.17, 0.03, 35.0)

    hanging = B.collection("Coll_Scarf_Hanging")
    loop(hanging, mats, origin, "Mesh_Scarf_Hanging_Loop")
    tail(hanging, mats, origin, "Mesh_Scarf_Hanging_TailLong",
         [(0.06, 0.2, 2.95), (0.1, 0.3, 2.7), (0.12, 0.33, 2.35), (0.14, 0.35, 2.05)], 0.15, 0.08, 10.0)
    tail(hanging, mats, origin, "Mesh_Scarf_Hanging_TailShort",
         [(-0.04, 0.2, 2.94), (-0.08, 0.3, 2.75), (-0.1, 0.34, 2.5)], 0.13, 0.07, -12.0)

    B.save(out)
    B.report()


main()
