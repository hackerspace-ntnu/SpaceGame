"""Officer caps: a band that sits on the head and a crown above it.

    blender --background --python components/apparel/officer_cap.py -- --out components/apparel/officer_cap.blend

Built in place on the Slim mannequin's head (see `_fit.py`); origin at the centre of
the band's lower edge, which is where a cap meets the head. Binds to `Head`.

    Coll_Cap_FlatTop   the sky soldier's: a short band flaring to a wide flat disc,
                       dark top, orange rim, stub visor
    Coll_Cap_Kepi      tall forward-leaning band, small flat top, visor
    Coll_Cap_Peaked    low band, puffed crown, long peak

Every part is its own object: band, crown, top, rim trim, visor.

Generation script -- historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from mathutils import Vector  # noqa: E402

import _buildlib as B  # noqa: E402
import _fit as F  # noqa: E402

MATS = ["Mat_Fabric_Tarp_Azure", "Mat_Neutral_Slate_Dark", "Mat_Paint_Safety_Orange"]
CLOTH, DARK, TRIM = range(3)
AROUND = 28
SHELL = 0.012


def rings(base, profile):
    """Closed rings up the cap: `profile` is (height above base, rx, ry, forward shift)."""
    return [F.arc(base + Vector((0.0, -fwd, h)), rx, ry, 0.0, 360.0 * (AROUND - 1) / AROUND, AROUND)
            for h, rx, ry, fwd in profile]


def disc(base, height, rx, ry, fwd=0.0):
    steps = (0.02, 0.35, 0.7, 1.0)
    return [F.arc(base + Vector((0.0, -fwd, height)), rx * s, ry * s, 0.0, 360.0 * (AROUND - 1) / AROUND, AROUND)
            for s in steps]


def visor(base, rx, ry, reach, droop, span=70.0):
    rows = []
    for k in range(4):
        t = k / 3.0
        rows.append(F.arc(base + Vector((0.0, 0.0, -droop * t)), rx + reach * t, ry + reach * t,
                          270.0 - span / 2.0, 270.0 + span / 2.0, 12))
    return rows


def emit(coll, mats, name, origin, build):
    p = B.Part(mats)
    build(p)
    return F.bind(p.finish(name, coll, origin=origin), "Head")


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)
    j = F.body_joints()
    head, crown = j["Head"][0], j["HeadTop_End"][1]
    # The band sits where the head egg is still wide: about 45 % of the way from the
    # head bone to the crown, centred on the egg's depth (it is carried 3 cm forward).
    base = Vector((0.0, head.y - 0.02, F.lerp(head.z, crown.z, 0.62)))
    band_rx, band_ry = 0.165, 0.19

    flat = B.collection("Coll_Cap_FlatTop")
    emit(flat, mats, "Mesh_Cap_FlatTop_Band", base,
         lambda p: p.sheet(rings(base, [(0.0, band_rx, band_ry, 0), (0.09, band_rx + 0.004, band_ry + 0.004, 0)]),
                           SHELL, CLOTH, closed=True))
    emit(flat, mats, "Mesh_Cap_FlatTop_Crown", base,
         lambda p: p.sheet(rings(base, [(0.085, band_rx + 0.002, band_ry + 0.002, 0), (0.13, 0.22, 0.245, 0.01),
                                        (0.17, 0.265, 0.29, 0.02)]), SHELL, CLOTH, closed=True))
    emit(flat, mats, "Mesh_Cap_FlatTop_Top", base,
         lambda p: p.sheet(disc(base, 0.18, 0.268, 0.293, 0.02), 0.03, DARK, closed=True))
    emit(flat, mats, "Mesh_Cap_FlatTop_RimTrim", base,
         lambda p: p.sheet(rings(base, [(0.158, 0.27, 0.295, 0.02), (0.176, 0.272, 0.297, 0.02)]),
                           0.012, TRIM, closed=True))
    emit(flat, mats, "Mesh_Cap_FlatTop_Visor", base,
         lambda p: p.sheet(visor(base + Vector((0, 0, 0.012)), band_rx, band_ry, 0.07, 0.03), 0.014, DARK))

    kepi = B.collection("Coll_Cap_Kepi")
    emit(kepi, mats, "Mesh_Cap_Kepi_Band", base,
         lambda p: p.sheet(rings(base, [(0.0, band_rx, band_ry, 0), (0.1, 0.158, 0.182, 0.02),
                                        (0.21, 0.148, 0.17, 0.05)]), SHELL, CLOTH, closed=True))
    emit(kepi, mats, "Mesh_Cap_Kepi_Top", base,
         lambda p: p.sheet(disc(base, 0.215, 0.15, 0.172, 0.05), 0.02, DARK, closed=True))
    emit(kepi, mats, "Mesh_Cap_Kepi_BandTrim", base,
         lambda p: p.sheet(rings(base, [(0.02, band_rx + 0.004, band_ry + 0.004, 0.001),
                                        (0.045, 0.165, 0.19, 0.004)]), 0.01, TRIM, closed=True))
    emit(kepi, mats, "Mesh_Cap_Kepi_Visor", base,
         lambda p: p.sheet(visor(base + Vector((0, 0, 0.01)), band_rx, band_ry, 0.09, 0.05, 80.0), 0.014, DARK))

    peaked = B.collection("Coll_Cap_Peaked")
    emit(peaked, mats, "Mesh_Cap_Peaked_Band", base,
         lambda p: p.sheet(rings(base, [(0.0, band_rx, band_ry, 0), (0.07, band_rx + 0.003, band_ry + 0.003, 0)]),
                           SHELL, DARK, closed=True))
    emit(peaked, mats, "Mesh_Cap_Peaked_Crown", base,
         lambda p: p.sheet(rings(base, [(0.065, band_rx + 0.001, band_ry + 0.001, 0), (0.12, 0.215, 0.245, 0.02),
                                        (0.165, 0.2, 0.23, 0.03), (0.19, 0.1, 0.12, 0.03)]), SHELL, CLOTH, closed=True))
    emit(peaked, mats, "Mesh_Cap_Peaked_Top", base,
         lambda p: p.sheet(disc(base, 0.19, 0.102, 0.122, 0.03), 0.012, CLOTH, closed=True))
    emit(peaked, mats, "Mesh_Cap_Peaked_Visor", base,
         lambda p: p.sheet(visor(base + Vector((0, 0, 0.01)), band_rx, band_ry, 0.15, 0.07, 100.0), 0.016, DARK))

    B.save(out)
    B.report()


main()
