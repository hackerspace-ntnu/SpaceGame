"""Gyre rib fan - ribs that grow out of the top of a grown column and spread
over a vault. Each rib leaves the column vertically and bends outward until it
runs level, so it lands tangent to a vault. Parabolic, never pointed: no
gothic arches, no bosses where ribs meet.

Run:  blender --background --python gyre_rib_fan.py -- --out gyre_rib_fan.blend

Variations:
  Coll_GyreRibFan_4       four even ribs, two carrying conduit inlays
  Coll_GyreRibFan_6       six even ribs, alternate ones inlaid
  Coll_GyreRibFan_3Asym   three ribs at uneven angles and lengths - the
                          machine's rhythm, not a human one
  Coll_GyreRibFan_4_Rising / 6_Rising
                          ribs still climbing at the tip, so they run on
                          into a vault and bury their ends in it

Origin: the top centre of the column it sits on (put it on a column's top,
the ribs' roots are buried 1 m into the flare). Ribs rise along +Z.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _gyrelib import *  # noqa: E402,F403

OUT = out_path(__file__, "gyre_rib_fan.blend")

N = 18


def rib_path(phi, reach, rise, r0=0.45, sink=1.6, u_end=1.0):
    """Quarter-curve rib: leaves the column vertically. With u_end = 1 it
    lands level (for flat soffits); below 1 it is still climbing at its tip,
    so it can run on into a vault."""
    c, s = math.cos(phi), math.sin(phi)
    ku = 1.0 - math.cos(u_end * math.pi / 2)
    kz = math.sin(u_end * math.pi / 2)
    pts = []
    for i in range(N + 1):
        u = u_end * i / N
        rad = r0 + reach * (1.0 - math.cos(u * math.pi / 2)) / ku
        z = -sink + (rise + sink) * math.sin(u * math.pi / 2) / kz
        pts.append((rad * c, rad * s, z))
    return pts, (-s, c, 0.0)


def rib_section(t):
    w = 1.5 - 0.85 * t
    d = 1.9 - 1.15 * t
    return chamfer_rect(w, d, min(w, d) * 0.36)


def inlay_section(t):
    d = 1.9 - 1.15 * t
    # sits on the convex face of the rib (+v), half buried
    return [(u, v + d / 2 - 0.015) for u, v in chamfer_rect(0.32, 0.04, 0.008)]


def fan(name, ribs, x0, u_end=1.0):
    coll = collection("Coll_GyreRibFan_" + name)
    stone = Part(link_materials([STONE]))
    metal = Part(link_materials([METAL]))
    any_inlay = False
    for deg, reach, rise, inlaid in ribs:
        path, side = rib_path(math.radians(deg), reach, rise, u_end=u_end)
        frame_sweep(stone, path, rib_section, side=side)
        if inlaid:
            frame_sweep(metal, path, inlay_section, side=side)
            any_inlay = True
    objs = [stone.finish("Mesh_GyreRibFan_%s" % name, coll)]
    if any_inlay:
        objs.append(metal.finish("Mesh_GyreRibFan_%s_Inlay" % name, coll))
    else:
        metal.bm.free()
    for o in objs:
        o.location.x += x0
    coll.instance_offset = (x0, 0.0, 0.0)


def main():
    start(OUT)
    fan("4", [(0, 7.0, 5.0, True), (90, 7.0, 5.0, False),
              (180, 7.0, 5.0, True), (270, 7.0, 5.0, False)], 0.0)
    fan("6", [(a, 7.5, 5.5, i % 2 == 0) for i, a in
              enumerate(range(0, 360, 60))], 22.0)
    fan("3Asym", [(10, 8.5, 5.8, True), (128, 6.0, 4.6, False),
                  (247, 7.2, 5.2, False)], 44.0)
    # rising: tips still climb ~22 degrees, to run on into a vault
    fan("4_Rising", [(0, 8.0, 7.0, True), (90, 8.0, 7.0, False),
                     (180, 8.0, 7.0, True), (270, 8.0, 7.0, False)], 66.0,
        u_end=0.75)
    fan("6_Rising", [(a, 8.5, 7.5, i % 2 == 0) for i, a in
                     enumerate(range(0, 360, 60))], 90.0, u_end=0.75)
    report()
    gyre_save(OUT)


main()
