"""Gyre catenary arch reveal - the insert that sits in an arch-shaped hole in
a carved shell and carries all the opening's detail.

Run:  blender --background --python gyre_arch_reveal.py -- --out gyre_arch_reveal.blend

One collection per variation, laid out along +X as a contact sheet; each
collection's instance_offset is its own origin so it can be appended or
exported on its own.

Origin of every variation: centre of the opening at floor level, on the wall
face. The opening faces -Y; the reveal runs back into +Y through the wall.
The shell's hole follows the outer edge (`margin` outside the opening), so the
seam between shell and insert falls on a line, never on a flat face.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _gyrelib import *  # noqa: E402,F403

OUT = out_path(__file__, "gyre_arch_reveal.blend")

SIZES = {
    #        opening w, h,  wall, margin, step, arch segments
    "Small": (7.0, 10.0, 2.0, 0.9, 0.35, 26),
    "Mid":   (10.0, 15.0, 2.5, 1.2, 0.45, 30),
    "Large": (16.0, 24.0, 3.0, 1.6, 0.60, 38),
}

VARIANTS = [
    ("Small", 2, False), ("Small", 3, False),
    ("Mid", 2, False), ("Mid", 3, False), ("Mid", 3, True),
    ("Large", 2, False), ("Large", 3, False),
]


def build(size, steps, lip, x0):
    w, h, wall, margin, step, n = SIZES[size]
    tag = "%s_%dStep%s" % (size, steps, "_Lip" if lip else "")
    coll = collection("Coll_GyreArchReveal_" + tag)
    base = catenary_curve(w, h, n)

    def curve(inset, depth):
        return offset_curve(base, inset)

    sec = reveal_section(margin, steps, wall, step, grooves=True, lip=lip)
    first = wall / (steps + 1)
    mats = []
    for k in range(len(sec)):
        (i0, d0), (i1, d1) = sec[k], sec[(k + 1) % len(sec)]
        deep = min(d0, d1) >= first - 1e-6 and min(i0, i1) >= -1e-6
        mats.append(1 if deep else 0)

    p = Part(link_materials([STONE, STONE_TINT]))
    sweep(p, curve, sec, closed_curve=False, mats=mats)
    obj = p.finish("Mesh_GyreArchReveal_" + tag, coll)
    objs = [obj]

    if lip:
        last_inset = step * steps
        last_depth = first * steps
        lp = Part(link_materials([POLISH]))
        sweep(lp, curve, lip_section(last_inset, last_depth),
              closed_curve=False, mat=0)
        objs.append(lp.finish("Mesh_GyreArchReveal_" + tag + "_MetalLip", coll))

    for o in objs:
        o.location.x += x0
    coll.instance_offset = (x0, 0.0, 0.0)
    return w + 2 * margin


def main():
    start(OUT)
    x = 0.0
    prev = 0.0
    for size, steps, lip in VARIANTS:
        width = SIZES[size][0] + 2 * SIZES[size][3]
        x += prev / 2 + width / 2 + 4.0 if prev else 0.0
        build(size, steps, lip, x)
        prev = width
    report()
    save(OUT)


main()
