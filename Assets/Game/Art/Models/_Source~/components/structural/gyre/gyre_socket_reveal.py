"""Gyre socket reveal - where a rod or a ring spoke enters the stone.

Run:  blender --background --python gyre_socket_reveal.py -- --out gyre_socket_reveal.blend

Sized for the Gyre rod (radius 0.65 m): the bore is 0.85 m, so the rod turns
inside it with clearance and never touches the lip.

Variations:
  Coll_GyreSocketReveal_Plain    stepped bore, polished lip, incised pair
  Coll_GyreSocketReveal_Ringed   wider face carrying a flush metal inlay ring
                                 - the end point conduits run into

Origin: centre of the bore on the wall face; the socket faces -Y.
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _gyrelib import *  # noqa: E402,F403

OUT = out_path(__file__, "gyre_socket_reveal.blend")

BORE = 0.85
WALL = 2.0
STEP = 0.20
STEPS = 2
SEG = 28


def section(margin, ring_channel):
    sec = reveal_section(margin, STEPS, WALL, STEP, grooves=True, lip=True)
    if not ring_channel:
        return sec
    # Cut a 0.5 m channel for the inlay ring into the outer face band.
    a, b = ring_channel
    out = [sec[0]]
    out += [(a, 0.0), (a, 0.05), (b, 0.05), (b, 0.0)]
    out += sec[1:]
    return out


def build(name, margin, ring_channel, x0):
    coll = collection("Coll_GyreSocketReveal_" + name)

    def curve(inset, depth):
        return circle(BORE - inset, SEG)

    sec = section(margin, ring_channel)
    first = WALL / (STEPS + 1)
    mats = []
    for k in range(len(sec)):
        (i0, d0), (i1, d1) = sec[k], sec[(k + 1) % len(sec)]
        mats.append(1 if min(d0, d1) >= first - 1e-6 and min(i0, i1) >= -1e-6 else 0)
    p = Part(link_materials([STONE, STONE_TINT]))
    sweep(p, curve, sec, closed_curve=True, mats=mats)
    objs = [p.finish("Mesh_GyreSocketReveal_" + name, coll)]

    lp = Part(link_materials([POLISH]))
    sweep(lp, curve, lip_section(STEP * STEPS, first * STEPS),
          closed_curve=True, mat=0)
    objs.append(lp.finish("Mesh_GyreSocketReveal_%s_MetalLip" % name, coll))

    if ring_channel:
        a, b = ring_channel
        ring = [(a + INLAY_GAP, INLAY_GAP), (b - INLAY_GAP, INLAY_GAP),
                (b - INLAY_GAP, 0.05 - INLAY_GAP), (a + INLAY_GAP, 0.05 - INLAY_GAP)]
        ip = Part(link_materials([METAL]))
        sweep(ip, curve, ring, closed_curve=True, mat=0)
        objs.append(ip.finish("Mesh_GyreSocketReveal_%s_InlayRing" % name, coll))

    for o in objs:
        o.location.x += x0
    coll.instance_offset = (x0, 0.0, 0.0)


def main():
    start(OUT)
    build("Plain", 0.75, None, 0.0)
    build("Ringed", 1.6, (-1.35, -0.85), 6.0)
    report()
    save(OUT)


main()
