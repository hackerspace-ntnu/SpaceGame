"""Gyre inlay strips - the flush dark-metal conduits laid in the line system.

Run:  blender --background --python gyre_inlay_strip.py -- --out gyre_inlay_strip.blend

Every strip is 0.5 m wide and 0.05 m deep, authored lying flat with its top
at z = 0 and running along +Y from its origin. Lay it into a 0.06 m channel
(or straight onto a face, rotated to it) and its top sits a few millimetres
below the stone, so it never z-fights. The material is the one surface that
switches to the conduit glow when the core is taken.

Variations (contact sheet along +X):
  Straight_4m, Straight_8m
  Arc_R4_45, Arc_R8_45, Arc_R16_30   bends to the right (+X)
  SBend_8m                           shifts 2 m sideways over 8 m
  Merge                              two strips joining - lines merge, never cross
  End_Socket                         runs into a ring that circles a socket
  End_Round                          rounded end
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _gyrelib import *  # noqa: E402,F403

OUT = out_path(__file__, "gyre_inlay_strip.blend")

W, D = 0.5, 0.05


def section(t):
    return [(u, v - D / 2) for u, v in chamfer_rect(W, D, 0.01)]


def straight(length, n=1):
    return [(0.0, length * i / n, -0.0) for i in range(n + 1)]


def arc(radius, deg, n=None):
    n = n or max(4, int(deg / 6))
    pts = []
    for i in range(n + 1):
        a = math.radians(deg) * i / n
        pts.append((radius - radius * math.cos(a), radius * math.sin(a), 0.0))
    return pts


def s_bend(length, shift, n=16):
    return [(shift * (0.5 - 0.5 * math.cos(math.pi * i / n)),
             length * i / n, 0.0) for i in range(n + 1)]


def strip(part, path):
    frame_sweep(part, path, section, side=None, up=(0, 0, 1))


def build(name, paths, x0, extra=None):
    coll = collection("Coll_GyreInlay_" + name)
    p = Part(link_materials([METAL]))
    for path in paths:
        strip(p, path)
    if extra:
        extra(p)
    o = p.finish("Mesh_GyreInlay_" + name, coll)
    o.location.x += x0
    coll.instance_offset = (x0, 0.0, 0.0)
    return o


def ring_end(p):
    # a ring around a socket, its near edge meeting the strip's end
    ring_r = 1.1
    cy = 3.0 + ring_r
    path = [(ring_r * math.sin(2 * math.pi * i / 32),
             cy - ring_r * math.cos(2 * math.pi * i / 32), 0.0)
            for i in range(32)]
    frame_sweep(p, path, section, up=(0, 0, 1), closed=True)


def round_strip(length):
    """A strip whose last W/2 tapers along a half circle to a rounded tip."""
    r = W / 2
    ys = [0.0, length - r] + [length - r + r * math.sin(math.pi / 2 * k / 8)
                              for k in range(1, 9)]

    def sec(t):
        y = ys[min(int(round(t * (len(ys) - 1))), len(ys) - 1)]
        if y <= length - r:
            w = W
        else:
            w = max(2 * math.sqrt(max(r * r - (y - (length - r)) ** 2, 0.0)), 0.04)
        return [(u, v - D / 2) for u, v in chamfer_rect(w, D, min(0.01, w / 5))]

    return [(0.0, y, 0.0) for y in ys], sec


def main():
    start(OUT)
    x = 0.0
    build("Straight_4m", [straight(4.0)], x); x += 3
    build("Straight_8m", [straight(8.0)], x); x += 3
    build("Arc_R4_45", [arc(4.0, 45)], x); x += 5
    build("Arc_R8_45", [arc(8.0, 45)], x); x += 6
    build("Arc_R16_30", [arc(16.0, 30)], x); x += 6
    build("SBend_8m", [s_bend(8.0, 2.0)], x); x += 6
    build("Merge", [straight(8.0, 2),
                    [(1.6 * (1 - math.cos(math.pi / 2 * (1 - i / 10))),
                      4.0 * i / 10 - 0.0, 0.0) for i in range(11)]], x)
    x += 6
    build("End_Socket", [straight(3.0)], x, extra=ring_end); x += 6
    coll = collection("Coll_GyreInlay_End_Round")
    rp = Part(link_materials([METAL]))
    path, sec = round_strip(3.0)
    frame_sweep(rp, path, sec, up=(0, 0, 1))
    o = rp.finish("Mesh_GyreInlay_End_Round", coll)
    o.location.x += x
    coll.instance_offset = (x, 0.0, 0.0)
    report()
    gyre_save(OUT)


main()
