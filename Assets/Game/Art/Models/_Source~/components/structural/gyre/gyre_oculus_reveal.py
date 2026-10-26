"""Gyre oculus reveal - the round opening insert, centred or eccentric, and
preset families of oculi.

Run:  blender --background --python gyre_oculus_reveal.py -- --out gyre_oculus_reveal.blend

Variations (one collection each, contact sheet along +X):
  Coll_GyreOculusReveal_R<radius>_Centred   steps share one centre
  Coll_GyreOculusReveal_R<radius>_Offset    each step slides up and to the
                                            side, so the far opening sits off
                                            centre inside its rings
  Coll_GyreOculusFamily_A/B/C               groups of linked duplicates: one
                                            large with smaller ones around it,
                                            never a row of equals

Origin: centre of the front opening, on the wall face; the oculus looks along
-Y and the reveal runs back into +Y. The shell's hole is the outer circle
(radius + margin).
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _gyrelib import *  # noqa: E402,F403

OUT = out_path(__file__, "gyre_oculus_reveal.blend")

SIZES = {
    #       radius, wall, margin, step, steps, segments
    "R1_5": (1.5, 1.5, 0.70, 0.25, 2, 24),
    "R3":   (3.0, 2.0, 0.90, 0.35, 2, 32),
    "R5":   (5.0, 2.5, 1.20, 0.45, 3, 40),
    "R8":   (8.0, 3.0, 1.60, 0.60, 3, 48),
}
SHIFT_DIR = Vector((0.8, 0.6))


def build(size, offset, coll, x0):
    r, wall, margin, step, steps, n = SIZES[size]
    tag = "%s_%s" % (size, "Offset" if offset else "Centred")
    shift_per_depth = (0.7 * step * (steps + 1) / wall) if offset else 0.0

    def curve(inset, depth):
        c = SHIFT_DIR * (shift_per_depth * max(depth, 0.0))
        return circle(r - inset, n, c)

    sec = reveal_section(margin, steps, wall, step, grooves=True)
    first = wall / (steps + 1)
    mats = []
    for k in range(len(sec)):
        (i0, d0), (i1, d1) = sec[k], sec[(k + 1) % len(sec)]
        mats.append(1 if min(d0, d1) >= first - 1e-6 and min(i0, i1) >= -1e-6 else 0)

    p = Part(link_materials([STONE, STONE_TINT]))
    sweep(p, curve, sec, closed_curve=True, mats=mats)
    obj = p.finish("Mesh_GyreOculusReveal_" + tag, coll)
    obj.location.x += x0
    coll.instance_offset = (x0, 0.0, 0.0)
    return obj


def main():
    start(OUT)
    meshes = {}
    x = 0.0
    prev = 0.0
    for size in SIZES:
        for offset in (False, True):
            r, _, margin = SIZES[size][:3]
            width = 2 * (r + margin)
            x += (prev / 2 + width / 2 + 3.0) if prev else 0.0
            coll = collection("Coll_GyreOculusReveal_%s_%s"
                              % (size, "Offset" if offset else "Centred"))
            meshes[(size, offset)] = build(size, offset, coll, x)
            prev = width

    # Families: linked duplicates of the reveals above, one mesh per size.
    families = {
        "A": [("R5", True, (0.0, 0.0)), ("R1_5", False, (8.2, -3.4)),
              ("R1_5", True, (5.4, 7.0))],
        "B": [("R8", False, (0.0, 0.0)), ("R3", True, (13.2, 4.8)),
              ("R1_5", False, (11.0, -4.6))],
        "C": [("R3", False, (0.0, 0.0)), ("R1_5", True, (0.4, 7.0)),
              ("R1_5", False, (-0.6, 11.4))],
    }
    fx = x + 30.0
    for key, members in families.items():
        coll = collection("Coll_GyreOculusFamily_" + key)
        for i, (size, offset, (dx, dz)) in enumerate(members):
            src = meshes[(size, offset)]
            instance(src, "Mesh_GyreOculusFamily_%s_%d_%s" % (key, i + 1, size),
                     coll, loc=(fx + dx, 0.0, dz))
        coll.instance_offset = (fx, 0.0, 0.0)
        fx += 34.0

    report()
    gyre_save(OUT)


main()
