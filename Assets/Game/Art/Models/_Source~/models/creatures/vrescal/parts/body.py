"""The Vrescal's trunk -- the barrel and both humps, and nothing else.

    blender --background --python parts/body.py -- --overwrite

## The silhouette

The concept art is a two-humped animal with a **front hump that is much the
larger**: it is the tallest point on the whole creature, taller than the head,
and it stands directly over the forelegs. Behind it the back dips into a clear
saddle, rises again into a second, smaller hump, and then falls away steeply to
a low rump. Everything in `KEYS` serves that line.

    front hump apex   3.78 m     <- tallest point on the animal
    saddle            2.93 m
    rear hump apex    3.14 m
    withers           2.57 m
    rump              2.05 m
    belly             1.33 m     <- a 1.75 m human does not fit underneath
    trunk length      3.30 m

## Getting a hump instead of a bulge

Raising `top` alone does not make a hump: on an elliptical section it widens
the whole body, and the hump arrives as broad as the animal. The fix is in the
width curve, not the height. Every hump station runs a **0.16 m half-width at
the crest over a 0.99 m half-width low down**, with the widest point pushed to
t = 0.70 -- so the mass hangs low and the hump rises out of it as a narrow
ridge. The saddle stations invert the same trick, running a comparatively broad
crest so the dip between the humps reads as a genuine hollow.

`ez` does the rest. The hump stations run 1.75 -- below 2, which draws the
section to a point at top and bottom -- while the ribcage stations run 2.5,
which squares them off into the slab-sided barrel the reference has.

## Table, then spline, then subdivide

`KEYS` holds only the stations that mean something (apex, saddle, hip, rump).
`stations()` resamples them onto a dense uniform spline before lofting, and the
mesh then gets **one** level of subdivision rather than two.

That order matters. Subdividing a sparse table is what rounds the hump apex off
by 15 cm and quietly makes the animal shorter than the table says -- the limit
surface cuts every corner. Resampling first means the corners are already
smooth, so subdivision only refines and the measured apex matches the number in
the table.
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector, noise

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import anatomy as A          # noqa: E402

NAME = "Mesh_Vrescal_Body"
SUBSURF = 1
STATIONS = 46
RING = 32


# --------------------------------------------------------------------------
# Key stations, nose end first
# --------------------------------------------------------------------------
#
#   x      top    bot    crest  mid   t_mid  low   ez    ey    lean
#
# `crest` is the half-width at the very top of the section, `mid` the widest
# half-width and `t_mid` how far down the section that widest point sits --
# 0 at the crest, 1 at the keel. `low` is the half-width down at the belly.

KEYS = [
    (0.34, 2.30, 1.62, 0.16, 0.40, 0.50, 0.26, 2.2, 2.0, 0.00),   # chest tip
    (0.16, 2.45, 1.45, 0.20, 0.58, 0.52, 0.36, 2.2, 2.0, 0.03),
    (0.00, 2.57, 1.37, 0.22, 0.72, 0.54, 0.44, 2.3, 2.1, 0.06),   # neck socket
    (-0.30, 2.94, 1.33, 0.20, 0.88, 0.60, 0.56, 2.1, 2.3, 0.11),
    (-0.58, 3.34, 1.32, 0.18, 0.97, 0.66, 0.64, 1.9, 2.5, 0.14),  # shoulder
    (-0.80, 3.62, 1.32, 0.17, 0.99, 0.69, 0.66, 1.8, 2.5, 0.13),
    (-0.98, 3.78, 1.32, 0.16, 0.99, 0.70, 0.66, 1.75, 2.5, 0.09),  # HUMP 1
    (-1.16, 3.64, 1.32, 0.17, 0.98, 0.69, 0.66, 1.8, 2.5, 0.03),
    (-1.32, 3.24, 1.33, 0.24, 0.96, 0.65, 0.65, 2.0, 2.5, 0.00),
    (-1.44, 2.93, 1.33, 0.34, 0.94, 0.60, 0.64, 2.2, 2.5, 0.00),  # SADDLE
    (-1.62, 3.01, 1.34, 0.26, 0.93, 0.63, 0.63, 2.0, 2.5, 0.00),
    (-1.89, 3.14, 1.35, 0.20, 0.92, 0.66, 0.62, 1.85, 2.5, 0.00),  # HUMP 2
    (-2.14, 2.96, 1.36, 0.24, 0.91, 0.62, 0.61, 2.0, 2.5, 0.00),
    (-2.40, 2.70, 1.38, 0.30, 0.90, 0.56, 0.60, 2.1, 2.4, -0.04),
    (-2.62, 2.50, 1.41, 0.36, 0.90, 0.50, 0.60, 2.2, 2.3, -0.07),
    (-2.80, 2.36, 1.45, 0.40, 0.89, 0.46, 0.58, 2.2, 2.2, -0.08),  # hip
    (-3.00, 2.22, 1.54, 0.42, 0.82, 0.44, 0.52, 2.2, 2.1, -0.05),
    (-3.16, 2.10, 1.68, 0.36, 0.66, 0.44, 0.40, 2.2, 2.0, 0.00),
    (-3.30, 1.97, 1.84, 0.20, 0.40, 0.46, 0.22, 2.2, 2.0, 0.00),  # rump cap
]


def stations():
    """Resample `KEYS` onto a dense uniform spline in x."""
    xs = [k[0] for k in KEYS]
    # Spline against a monotone parameter, then evaluate on even x spacing --
    # the key stations are deliberately crowded around the humps, and lofting
    # them as given would put all the mesh density there and none on the flank.
    idx = list(range(len(KEYS)))
    out = []
    for s in range(STATIONS):
        x = A.lerp(xs[0], xs[-1], s / float(STATIONS - 1))
        # Invert x -> key index, so the spline is evaluated at the right place.
        t = A.spline1d([-v for v in xs], idx, -x)
        vals = [A.spline1d(idx, [k[c] for k in KEYS], t) for c in range(1, 10)]
        top, bot, crest, mid, t_mid, low, ez, ey, lean = vals
        t_mid = min(0.84, max(0.20, t_mid))
        widths = [(0.0, crest * 0.22), (0.06, crest),
                  (t_mid * 0.5, A.lerp(crest, mid, 0.55)),
                  (t_mid, mid),
                  (A.lerp(t_mid, 1.0, 0.55), low),
                  (0.94, low * 0.42), (1.0, low * 0.10)]
        widths = sorted(set(widths), key=lambda p: p[0])
        out.append(A.Section(x, top, bot, widths, ez=ez, ey=ey, lean=lean))
    return out


# --------------------------------------------------------------------------
# Anatomy
# --------------------------------------------------------------------------
#
# Deliberately weak. The primary volumes are in the sections above; these only
# say where the surface tightens or swells locally. A blob strong enough to
# *be* a mass inflates into a sphere stuck on the flank -- the note in
# `vrescal_BUILD.md` says that mistake cost five rebuilds, and it is still true.

def muscles():
    b = A.Blob
    return [
        b((-0.62, 0.62, 2.05), (0.44, 0.26, 0.52), 0.055),   # scapula
        b((-0.50, 0.54, 1.72), (0.34, 0.24, 0.34), 0.045),   # triceps
        b((0.06, 0.30, 1.72), (0.30, 0.26, 0.34), 0.050),    # pectoral
        b((-1.55, 0.66, 1.80), (0.60, 0.22, 0.44), 0.030),   # ribcage
        b((-2.72, 0.60, 2.02), (0.42, 0.26, 0.44), 0.060),   # gluteal
        b((-2.86, 0.52, 1.66), (0.34, 0.26, 0.36), 0.050),   # upper thigh
        b((0.20, 0.0, 1.52), (0.30, 0.34, 0.18), 0.045),     # sternum keel
        b((-1.70, 0.0, 1.34), (0.90, 0.56, 0.14), 0.035),    # belly
        # The humps get a last touch of firmness along their crests so the
        # subdivided surface does not read as slack over them.
        b((-0.95, 0.0, 3.62), (0.30, 0.14, 0.30), 0.030),
        b((-1.87, 0.0, 3.00), (0.24, 0.12, 0.24), 0.024),
    ]


def folds():
    """Creases, as negative displacement.

    Without these the flank is one unbroken sheet from chest to croup, and the
    reference's animal is visibly gathered where the limbs leave the body and
    slack along the belly.
    """
    b, r = A.Blob, A.Ring
    return [
        b((-0.86, 0.68, 1.78), (0.09, 0.30, 0.46), -0.045),   # behind shoulder
        b((-2.50, 0.66, 1.80), (0.09, 0.28, 0.42), -0.040),   # ahead of hip
        b((-1.42, 0.0, 3.02), (0.16, 0.44, 0.20), -0.045),    # saddle hollow
        b((0.04, 0.0, 1.98), (0.13, 0.34, 0.30), -0.040),     # chest gather
        # Belly slack -- three shallow transverse gathers, not a smooth sheet.
        r((-1.05, 0.0, 1.40), (1, 0, 0), 0.60, 0.13, -0.026),
        r((-1.70, 0.0, 1.39), (1, 0, 0), 0.62, 0.13, -0.024),
        r((-2.30, 0.0, 1.42), (1, 0, 0), 0.60, 0.13, -0.022),
    ]


def paint(tan, teal):
    """Countershading: cold underside, warm flank, with a broken edge.

    The line is not level. In the reference the teal climbs almost to the top
    of the chest at the front, sits low along the barrel, and lifts again over
    the flank -- and it is ragged everywhere, because a clean horizontal edge
    across a flank reads as a painted stripe rather than as an animal.
    """
    def pick(c, _n):
        edge = A.spline1d([-3.30, -2.60, -1.60, -0.40, 0.34],
                          [1.86, 1.72, 1.66, 1.94, 2.24],
                          max(-3.30, min(0.34, c.x)))
        edge += noise.turbulence(c * 3.1, 2, False) * 0.17
        return teal if c.z < edge else tan
    return pick


def build(mats, coll):
    tan, teal = 0, 1

    def shape(bm):
        A.displace(bm, muscles())
        A.displace(bm, folds())
        A.skin(bm, scale=2.1, amount=0.013, octaves=3)

    return A.build(A.loft(stations(), RING), NAME, mats, coll,
                   levels=SUBSURF, shape=shape, paint=paint(tan, teal),
                   origin=(0.0, 0.0, 0.0))


# --------------------------------------------------------------------------
# Sockets -- the contract with the other part scripts
# --------------------------------------------------------------------------

NECK_SOCKET = A.REF["neck_base"]
TAIL_SOCKET = A.REF["tail_base"]
FORE_SOCKET = (A.REF["fore_x"], A.REF["fore_y"], 2.02)
HIND_SOCKET = (A.REF["hind_x"], A.REF["hind_y"], 2.06)


def main():
    A.start()
    mats = A.materials(A.SKIN_SET)
    coll = A.collection("Coll_Vrescal_Body")
    obj = build(mats, coll)

    A.measure(obj, "body")
    zs = [v.co.z for v in obj.data.vertices]
    apex = max(zs)
    saddle = max(v.co.z for v in obj.data.vertices
                 if -1.50 < v.co.x < -1.38)
    hump2 = max(v.co.z for v in obj.data.vertices
                if -1.98 < v.co.x < -1.80)
    belly = min(v.co.z for v in obj.data.vertices if -2.4 < v.co.x < -0.8)
    print("  hump1 %.2f m (table 3.78)   saddle %.2f (2.93)   "
          "hump2 %.2f (3.14)   belly %.2f (1.33)"
          % (apex, saddle, hump2, belly))
    print("  half-width %.2f m (table 0.99)"
          % max(abs(v.co.y) for v in obj.data.vertices))
    A.report([obj])
    A.save(A.part_path("body"))


if __name__ == "__main__":
    main()
