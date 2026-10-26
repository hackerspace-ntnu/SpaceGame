"""Gyre grown column - rises out of the floor through a wide fillet, narrows to
an hourglass waist that carries a flush dark-metal band, and flares at the top
to meet a vault or a rib fan. No base, no capital, no fluting.

Run:  blender --background --python gyre_grown_column.py -- --out gyre_grown_column.blend

Variations:
  Coll_GyreColumn_H12 / H20 / H32   straight, three heights
  Coll_GyreColumn_H20_Lean          sheared 6 degrees off vertical
  Coll_GyreColumn_H20_Split         splits above the waist into two leaning
                                    branches (base and branches are separate
                                    objects, so nothing intersects in one mesh)
  Coll_GyreColumn_H12_Fan / H20_Fan no top flare - for carrying a rib fan,
                                    whose ribs then grow straight out of the
                                    shaft instead of sitting on a capital

Each variation is the stone body, its waist band (metal, polished chamfers)
and one vertical conduit inlay up the +X face. Origin: floor centre.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _gyrelib import *  # noqa: E402,F403

OUT = out_path(__file__, "gyre_grown_column.blend")

SPECS = {
    #        height, waist r, belly r, base r, top r
    "H12": (12.0, 0.75, 1.40, 3.2, 2.6),
    "H20": (20.0, 1.00, 1.95, 4.4, 3.4),
    "H32": (32.0, 1.35, 2.70, 5.8, 4.4),
    # for carrying a rib fan: no top flare, the ribs grow out of the shaft
    "H12_Fan": (12.0, 0.75, 1.40, 3.2, 1.30),
    "H20_Fan": (20.0, 1.00, 1.95, 4.4, 1.75),
}
BAND_H = 0.9
CHANNEL = 0.10
SEG = 18


def radius(z, h, rw, rm, rb, rt):
    zw = 0.55 * h
    span = max(zw, h - zw)
    body = rw + (rm - rw) * ((z - zw) / span) ** 2
    fb = 0.11 * h            # base fillet reach
    ft = 0.09 * h            # top flare reach
    base = (rb - body) * math.exp(-z / (fb * 0.55)) if z < fb * 4.0 else 0.0
    top = (rt - body) * math.exp(-(h - z) / (ft * 0.45)) if h - z < ft * 4.0 else 0.0
    return body + max(base, 0.0) + max(top, 0.0)


def profile(spec, z_from=0.0, z_to=None, scale=1.0):
    """(r, z) list with a waist channel and two incised pairs."""
    h, rw, rm, rb, rt = spec
    z_to = h if z_to is None else z_to
    zw = 0.55 * h
    zs = set()
    # dense near the fillets, coarser along the body
    for i in range(14):
        zs.add(h * 0.16 * (i / 13.0) ** 1.6)
        zs.add(h - h * 0.14 * (i / 13.0) ** 1.6)
    for i in range(1, 12):
        zs.add(h * (0.16 + 0.70 * i / 12.0))
    notch = {}
    for zc in (0.20 * h, 0.20 * h + GROOVE_GAP, 0.86 * h, 0.86 * h + GROOVE_GAP):
        a, b = zc - GROOVE_W / 2, zc + GROOVE_W / 2
        zs |= {a, b}
        notch[(a, b)] = GROOVE_D
    a, b = zw - BAND_H / 2, zw + BAND_H / 2
    zs |= {a, b}
    notch[(a, b)] = CHANNEL
    zs = sorted(z for z in zs if z_from - 1e-9 <= z <= z_to + 1e-9)
    if zs[0] > z_from + 1e-6:
        zs.insert(0, z_from)
    if zs[-1] < z_to - 1e-6:
        zs.append(z_to)

    out = []
    for z in zs:
        r = radius(z, h, rw, rm, rb, rt) * scale
        cut = None
        for (a, b), d in notch.items():
            if abs(z - a) < 1e-9:
                out.append((r, z))
                out.append((r - d, z))
                cut = True
            elif abs(z - b) < 1e-9:
                out.append((r - d, z))
                out.append((r, z))
                cut = True
        if not cut:
            out.append((r, z))
    return out


def band(spec, coll, name):
    h, rw, rm, rb, rt = spec
    zw = 0.55 * h
    r = radius(zw, h, rw, rm, rb, rt)
    ri = r - CHANNEL + 0.004
    ro = r - INLAY_GAP
    z0, z1 = zw - BAND_H / 2 + INLAY_GAP, zw + BAND_H / 2 - INLAY_GAP
    c = 0.06
    prof = [(ri, z0), (ro - c, z0), (ro, z0 + c), (ro, z1 - c), (ro - c, z1),
            (ri, z1), (ri, z0)]
    # chamfers polished, the rest dark
    mats = [0, 1, 0, 1, 0, 0]
    p = Part(link_materials([METAL, POLISH]))
    revolve(p, prof, SEG, mats=mats, cap_bottom=False, cap_top=False)
    return p.finish(name, coll)


def inlay(spec, coll, name, z_from=0.4, z_to=None):
    h, rw, rm, rb, rt = spec
    z_to = h - 0.4 if z_to is None else z_to
    path = []
    n = 40
    for i in range(n + 1):
        z = z_from + (z_to - z_from) * i / n
        path.append((radius(z, h, rw, rm, rb, rt) - 0.006, 0.0, z))
    p = Part(link_materials([METAL]))
    frame_sweep(p, path, lambda t: chamfer_rect(0.5, 0.04, 0.008),
                side=(0, 1, 0))
    return p.finish(name, coll)


def column(key, x0, lean=0.0, split=False):
    spec = SPECS[key]
    h = spec[0]
    tag = key + ("_Lean" if lean else "") + ("_Split" if split else "")
    coll = collection("Coll_GyreColumn_" + tag)
    objs = []
    if not split:
        p = Part(link_materials([STONE]))
        revolve(p, profile(spec), SEG)
        objs.append(p.finish("Mesh_GyreColumn_" + tag, coll))
        objs.append(band(spec, coll, "Mesh_GyreColumn_%s_WaistBand" % tag))
        objs.append(inlay(spec, coll, "Mesh_GyreColumn_%s_Inlay" % tag))
    else:
        zw = 0.55 * h
        cut = zw + BAND_H / 2 + 1.2
        p = Part(link_materials([STONE]))
        revolve(p, profile(spec, 0.0, cut), SEG)
        objs.append(p.finish("Mesh_GyreColumn_%s_Base" % tag, coll))
        objs.append(band(spec, coll, "Mesh_GyreColumn_%s_WaistBand" % tag))
        objs.append(inlay(spec, coll, "Mesh_GyreColumn_%s_Inlay" % tag,
                          z_to=zw - BAND_H / 2 - 0.1))
        # branches: the upper body re-profiled at 70 %, tipped apart about
        # a pivot just below the cut so their roots stay buried in the base
        h, rw, rm, rb, rt = spec
        length = (h - cut + 1.5) / math.cos(math.radians(14))
        r0 = radius(cut, h, rw, rm, rb, rt) * 0.62
        for side, ang in (("L", -14.0), ("R", 14.0)):
            bp = Part(link_materials([STONE]))
            prof = []
            for i in range(17):
                z = length * i / 16.0
                r = r0 * (1.0 - 0.18 * math.sin(math.pi * z / length))
                r += (rt * 0.62 - r) * math.exp(-(length - z) / (0.06 * length))
                prof.append((r, z))
            revolve(bp, prof, SEG - 4)
            o = bp.finish("Mesh_GyreColumn_%s_Branch%s" % (tag, side), coll)
            o.location = (0.0, 0.0, cut - 1.0)
            o.rotation_euler = (0.0, math.radians(ang), 0.0)
            objs.append(o)
    if lean:
        k = math.tan(math.radians(lean))
        for o in objs:
            for v in o.data.vertices:
                v.co.x += k * (v.co.z + o.location.z)
    for o in objs:
        o.location.x += x0
    coll.instance_offset = (x0, 0.0, 0.0)


def main():
    start(OUT)
    column("H12", 0.0)
    column("H20", 14.0)
    column("H32", 32.0)
    column("H20", 50.0, lean=6.0)
    column("H20", 66.0, split=True)
    column("H12_Fan", 80.0)
    column("H20_Fan", 92.0)
    # rotations on the branches are part of their placement inside the
    # variation; apply them so every object ships with identity transforms
    for o in bpy.data.objects:
        if any(abs(a) > 1e-9 for a in o.rotation_euler):
            o.data.transform(o.rotation_euler.to_matrix().to_4x4())
            o.rotation_euler = (0, 0, 0)
    report()
    gyre_save(OUT)


main()
