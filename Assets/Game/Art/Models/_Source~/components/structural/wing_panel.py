"""One-piece webbed wings for the dune ornithopter.

This replaces the earlier fan of separate paddle blades. A wing here is a
single continuous structure, built the way a bat's is: a thin arm bone out to a
wrist, five slender digits fanning aft from it, and cloth stretched between the
digits in bays. The cloth is the wing — the spars only hold it open.

Two objects per variation, which is the split the brief asked for rather than a
subdivision of the wing itself:

    Mesh_WingPanel_<Var>_Frame   the skeleton — spars, wrist, joints, lashings
    Mesh_WingPanel_<Var>_Web     the cloth — sagging, hemmed, double-sided

Both carry the same vertex groups, so both fold together off one armature.

Local space (built as the RIGHT wing; the assembly mirrors it for the left):

    origin = shoulder pivot at (0, 0, 0)
    +X     = outboard toward the tip
    +Y     = aft
    +Z     = up

    blender --background --python wing_panel.py -- --out <path>/wing_panel.blend
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "..", ".."))
import _buildlib  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from _ornithopter import (SCALE as ORNI_SCALE, SKIN_GROUPS,  # noqa: E402
                          WRIST as WRIST_T, DIGIT_TIPS as TIPS_T,
                          ROOT_ANCHOR as ANCHOR_T, tail_tips)

_buildlib.SCALE = ORNI_SCALE

MATS = [
    "Mat_Fabric_Wing_Ochre",       # 0  the membrane
    "Mat_Fabric_Seat_Ochre",       # 1  hem along the free edge, lashings
    # Patches use the old beige rather than a bleached white: against orange
    # cloth, white reads as a paper sticker, beige as cloth the sun has had
    # longer to work on.
    "Mat_Fabric_Wing_Beige",       # 2  faded repair patches
    "Mat_Metal_Steel_Worn",        # 3  spars
    "Mat_Metal_Steel_Dark",        # 4  joints, ferrules
    "Mat_Metal_Brass_Tarnished",   # 5  wrist and knuckle pins
    "Mat_Wood_Ply_Worn",           # 6  splints — homemade repairs
    "Mat_Metal_Rust_Heavy",        # 7  weathered fittings
]

GROUPS = SKIN_GROUPS

# Skeleton layout comes from _ornithopter so the assembly's bones land on the
# same spars. The inner membrane's anchor is kept short on purpose: run it as
# far aft as a real bat's and the inner bay alone out-areas all four digit bays
# put together, which buries the fan the sketch is built around.
WRIST = Vector(WRIST_T)
DIGIT_TIPS = [Vector(t) for t in TIPS_T]
ROOT_ANCHOR = Vector(ANCHOR_T)

CLOTH_T = 0.0065        # half-thickness of the membrane sheet


# --------------------------------------------------------------------------
# Weighting
# --------------------------------------------------------------------------

def arm_falloff(v):
    """How much a point at span fraction `v` still belongs to the arm.

    Non-zero only near the wrist, so the membrane pivots about the digits
    rather than smearing back along the arm when the wing folds.
    """
    return max(0.0, min(1.0, 1.0 - v * 2.4))


def bay_weights(u, v, ga, gb):
    """Weights inside a bay bounded by digit groups `ga` and `gb`."""
    a = arm_falloff(v) * 0.85
    r = 1.0 - a
    return {"VG_Arm": a, ga: r * (1.0 - u), gb: r * u}


def inner_weights(u, v):
    """Inner bay: digit 5 on one edge, the fuselage on the other."""
    a = (1.0 - u) * arm_falloff(v)
    return {"VG_Root": u, "VG_Arm": a, "VG_Digit_5": (1.0 - u) - a}


# --------------------------------------------------------------------------
# Cloth
# --------------------------------------------------------------------------

def bay(sp, edge_a, edge_b, wfn, nu=7, nv=6, sag=0.115, scallop=0.20,
        v0=0.055, mat=0, hem=1, patch=None):
    """One membrane bay, stretched between two bounding edges.

    `edge_a` and `edge_b` are (start, end) pairs. The free trailing edge is
    scalloped — pulled back toward the wrist at mid-bay — which is what makes
    stretched cloth read as cloth rather than as a flat triangle, and the sheet
    is built double-sided because a wing gets seen from underneath.
    """
    a0, a1 = Vector(edge_a[0]), Vector(edge_a[1])
    b0, b1 = Vector(edge_b[0]), Vector(edge_b[1])

    def surface(u, v):
        # Scallop shortens the bay at mid-chord, so the free edge bows in.
        vv = v0 + (v * (1.0 - scallop * math.sin(math.pi * u))) * (1.0 - v0)
        p = a0.lerp(a1, vv).lerp(b0.lerp(b1, vv), u)
        # Cloth sags between the spars, most at mid-bay and toward the tips.
        p = p + Vector((0, 0, -sag * math.sin(math.pi * u) * (0.25 + 0.75 * vv)))
        return p, vv

    top, bot = [], []
    for j in range(nv):
        v = j / (nv - 1)
        rt, rb = [], []
        for i in range(nu):
            u = i / (nu - 1)
            p, vv = surface(u, v)
            w = wfn(u, vv)
            rt.append(sp.vert(p + Vector((0, 0, CLOTH_T)), w))
            rb.append(sp.vert(p - Vector((0, 0, CLOTH_T)), w))
        top.append(rt)
        bot.append(rb)

    # Hem the outermost band along the free edge.
    def rowmat(r, n):
        return hem if r == n - 1 else mat

    sp.bridge(top, mat=mat, mat_rows=rowmat)
    sp.bridge([list(reversed(r)) for r in bot], mat=mat, mat_rows=rowmat)

    # Close the sheet's four boundaries so it is a solid, not a plane.
    for j in range(nv - 1):
        sp.face((top[j][0], bot[j][0], bot[j + 1][0], top[j + 1][0]), mat)
        sp.face((top[j][-1], top[j + 1][-1], bot[j + 1][-1], bot[j][-1]), mat)
    for i in range(nu - 1):
        sp.face((top[0][i], top[0][i + 1], bot[0][i + 1], bot[0][i]), mat)
        sp.face((top[-1][i], bot[-1][i], bot[-1][i + 1], top[-1][i + 1]), hem)

    if patch is not None:
        # A bleached square sewn over the cloth, offset just clear of it.
        pu, pv, half = patch
        quad = []
        for du, dv in ((-half, -half), (half, -half), (half, half),
                       (-half, half)):
            p, vv = surface(min(max(pu + du, 0.02), 0.98),
                            min(max(pv + dv, 0.05), 0.95))
            quad.append(sp.vert(p + Vector((0, 0, CLOTH_T + 0.006)),
                                wfn(pu, vv)))
        sp.face(quad, 2)


# --------------------------------------------------------------------------
# Skeleton
# --------------------------------------------------------------------------

def rod(sp, points, radii, weights, seg=6, mat=3, taper_cap=True):
    """Tapered tube following a polyline, with per-station weights."""
    # Build an orthonormal frame per station from the local tangent.
    rings = []
    n = len(points)
    for k in range(n):
        p = Vector(points[k])
        if k == 0:
            t = (Vector(points[1]) - p)
        elif k == n - 1:
            t = (p - Vector(points[k - 1]))
        else:
            t = (Vector(points[k + 1]) - Vector(points[k - 1]))
        t = t.normalized()
        up = Vector((0, 0, 1))
        if abs(t.dot(up)) > 0.95:
            up = Vector((0, 1, 0))
        s1 = t.cross(up).normalized()
        s2 = t.cross(s1).normalized()
        r = radii[k]
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            ring.append(sp.vert(p + s1 * (r * math.cos(a))
                                + s2 * (r * math.sin(a)), weights[k]))
        rings.append(ring)
    sp.bridge(rings, mat=mat, closed=True)

    if taper_cap:
        # Cap both ends with a fan to a centre vertex.
        for idx, ring in ((0, rings[0]), (n - 1, rings[-1])):
            c = sp.vert(Vector(points[idx]), weights[idx])
            for i in range(seg):
                j = (i + 1) % seg
                if idx == 0:
                    sp.face((c, ring[j], ring[i]), mat)
                else:
                    sp.face((c, ring[i], ring[j]), mat)
    return rings


def lerp_pts(a, b, n):
    a, b = Vector(a), Vector(b)
    return [a.lerp(b, i / (n - 1)) for i in range(n)]


def build_frame(mats, coll, name, tips, wrist, splint=None):
    sp = SkinPart(mats)

    # Arm: shoulder to wrist. Slim, and the heaviest member on the wing.
    n = 4
    pts = lerp_pts(Vector((0, 0, 0)), wrist, n)
    ws = [{"VG_Arm": 1.0} for _ in range(n)]
    rod(sp, pts, [0.052, 0.044, 0.038, 0.034], ws, seg=7, mat=3)

    # Wrist knuckle — the pivot every digit hangs off.
    kn = lerp_pts(wrist - Vector((0.05, 0, 0)), wrist + Vector((0.05, 0, 0)), 2)
    rod(sp, kn, [0.062, 0.062], [{"VG_Arm": 1.0}] * 2, seg=8, mat=5)

    for d, tip in enumerate(tips, start=1):
        g = "VG_Digit_%d" % d
        n = 5
        pts = lerp_pts(wrist, tip, n)
        # Blend at the knuckle so the joint bends instead of shearing.
        ws = [{"VG_Arm": 0.55, g: 0.45},
              {"VG_Arm": 0.12, g: 0.88}] + [{g: 1.0}] * (n - 2)
        r0 = 0.034 if d == 1 else 0.028
        radii = [r0, r0 * 0.80, r0 * 0.62, r0 * 0.45, r0 * 0.26]
        rod(sp, pts, radii, ws, seg=6, mat=3)

        # Knuckle pin at the root of each digit.
        kp = lerp_pts(wrist + (tip - wrist).normalized() * 0.055
                      - Vector((0, 0, 0.030)),
                      wrist + (tip - wrist).normalized() * 0.055
                      + Vector((0, 0, 0.030)), 2)
        rod(sp, kp, [0.026, 0.026], [{"VG_Arm": 0.5, g: 0.5}] * 2, seg=5,
            mat=5, taper_cap=False)

    # Root fitting where the wing bolts to the shoulder yoke.
    rf = lerp_pts(Vector((-0.03, -0.07, 0)), Vector((-0.03, 0.07, 0)), 2)
    rod(sp, rf, [0.072, 0.072], [{"VG_Arm": 1.0}] * 2, seg=8, mat=4)

    # A tension cable from the arm out to digit 3, holding the fan open.
    mid3 = wrist.lerp(tips[2], 0.62)
    cab = lerp_pts(wrist.lerp(Vector((0, 0, 0)), 0.45), mid3, 3)
    rod(sp, cab, [0.011, 0.010, 0.009],
        [{"VG_Arm": 1.0}, {"VG_Arm": 0.5, "VG_Digit_3": 0.5},
         {"VG_Digit_3": 1.0}], seg=4, mat=4, taper_cap=False)

    if splint is not None:
        # Plywood splint lashed over a cracked digit.
        d, t0, t1 = splint
        g = "VG_Digit_%d" % d
        a = wrist.lerp(tips[d - 1], t0)
        b = wrist.lerp(tips[d - 1], t1)
        rod(sp, lerp_pts(a, b, 2), [0.046, 0.046], [{g: 1.0}] * 2, seg=4,
            mat=6, taper_cap=False)
        for t in (t0 + 0.02, t1 - 0.02):
            c = wrist.lerp(tips[d - 1], t)
            rod(sp, lerp_pts(c - Vector((0, 0, 0.05)), c + Vector((0, 0, 0.05)),
                             2), [0.052, 0.052], [{g: 1.0}] * 2, seg=5, mat=1,
                taper_cap=False)

    return sp.finish(name, coll, GROUPS)


def build_web(mats, coll, name, tips, wrist, patched=False, torn=False):
    sp = SkinPart(mats)

    for i in range(4):
        ga = "VG_Digit_%d" % (i + 1)
        gb = "VG_Digit_%d" % (i + 2)
        patch = None
        if patched and i == 1:
            patch = (0.45, 0.55, 0.16)
        # The torn bay stops short and loses its hem.
        sc = 0.20 if not (torn and i == 0) else 0.46
        bay(sp, (wrist, tips[i]), (wrist, tips[i + 1]),
            lambda u, v, ga=ga, gb=gb: bay_weights(u, v, ga, gb),
            sag=0.115 - 0.012 * i, scallop=sc, patch=patch)

    # Inner membrane: digit 5 on one edge, the fuselage line on the other.
    bay(sp, (wrist, tips[4]), (Vector((0, 0, 0)), ROOT_ANCHOR),
        inner_weights, nu=6, nv=6, sag=0.085, scallop=0.14,
        patch=(0.5, 0.5, 0.14) if patched else None)

    return sp.finish(name, coll, GROUPS)


# --------------------------------------------------------------------------

def tail_layout():
    """A smaller webbed fan for the tail, radiating from a single hub."""
    return Vector((0.0, 0.0, 0.0)), [Vector(t) for t in tail_tips()]


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for coll_name, var, kw_frame, kw_web in (
            ("Coll_WingPanel_Main", "Main", {}, {}),
            ("Coll_WingPanel_Patched", "Patched", {}, dict(patched=True)),
            ("Coll_WingPanel_Torn", "Torn", dict(splint=(2, 0.30, 0.52)),
             dict(torn=True))):
        coll = collection(coll_name)
        build_frame(mats, coll, "Mesh_WingPanel_%s_Frame" % var,
                    DIGIT_TIPS, WRIST, **kw_frame)
        build_web(mats, coll, "Mesh_WingPanel_%s_Web" % var,
                  DIGIT_TIPS, WRIST, **kw_web)

    # Tail fan: same construction, no arm — the digits radiate off the hub.
    coll = collection("Coll_WingPanel_TailFan")
    hub, ttips = tail_layout()
    sp = SkinPart(mats)
    rod(sp, lerp_pts(hub - Vector((0, 0, 0.05)), hub + Vector((0, 0, 0.05)), 2),
        [0.070, 0.070], [{"VG_Arm": 1.0}] * 2, seg=8, mat=5)
    for d, tip in enumerate(ttips, start=1):
        g = "VG_Digit_%d" % d
        pts = lerp_pts(hub, tip, 4)
        ws = [{"VG_Arm": 0.5, g: 0.5}, {g: 1.0}, {g: 1.0}, {g: 1.0}]
        rod(sp, pts, [0.028, 0.022, 0.016, 0.009], ws, seg=5, mat=3)
    sp.finish("Mesh_WingPanel_TailFan_Frame", coll, GROUPS)

    sp = SkinPart(mats)
    for i in range(4):
        ga, gb = "VG_Digit_%d" % (i + 1), "VG_Digit_%d" % (i + 2)
        bay(sp, (hub, ttips[i]), (hub, ttips[i + 1]),
            lambda u, v, ga=ga, gb=gb: bay_weights(u, v, ga, gb),
            nu=6, nv=5, sag=0.060, scallop=0.17, v0=0.075)
    sp.finish("Mesh_WingPanel_TailFan_Web", coll, GROUPS)

    report()
    save(out)


main()
