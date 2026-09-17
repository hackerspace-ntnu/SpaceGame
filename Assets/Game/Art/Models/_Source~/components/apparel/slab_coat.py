"""Stiff long coats: panels hung from the shoulders, open at the sides for the arms.

    blender --background --python components/apparel/slab_coat.py -- --out components/apparel/slab_coat.blend

Built in place on the Slim mannequin (see `_fit.py`); origin at the base of the neck.
Panels bind to `Spine2` and swing with the chest; the tunic under them binds to the spine
bone it covers.

The coat is not one wrap. It is separate panels round an elliptical hang line - a
back panel, and front panels either side of an opening - with gaps at the sides where
the arms come out. A front panel that overlaps another hangs on a slightly smaller
radius, so the two lie in layers and never share a surface.

    Coll_Coat_Slab    the sky soldier's: a great flat right-front slab to the shins,
                      a narrow left front, a back panel, orange edge trims, dark tunic
    Coll_Coat_Tails   short fronts at the hip, two long split tails behind

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

MATS = ["Mat_Fabric_Tarp_Azure", "Mat_Paint_Teal_Deep", "Mat_Paint_Safety_Orange"]
CLOTH, TUNIC, TRIM = range(3)
BODY_Y = -0.04
PANEL = 0.025
TRIM_W = 0.035               # trim width, laid along the panel face
TRIM_T = 0.012

# The hang line: (z, rx, ry). Over the shoulders it widens to clear the joint balls,
# then falls away from the hips as an A-line.
HANG = ((2.86, 0.23, 0.19), (2.72, 0.37, 0.25), (2.45, 0.36, 0.26), (2.10, 0.34, 0.27),
        (1.75, 0.36, 0.30), (1.40, 0.38, 0.32), (1.05, 0.41, 0.34), (0.70, 0.44, 0.36))


def radius_at(z):
    for (z0, rx0, ry0), (z1, rx1, ry1) in zip(HANG, HANG[1:]):
        if z1 <= z <= z0:
            t = (z0 - z) / (z0 - z1)
            return F.lerp(rx0, rx1, t), F.lerp(ry0, ry1, t)
    return HANG[-1][1], HANG[-1][2]


def point(z, angle, inset=0.0):
    rx, ry = radius_at(z)
    return F.ellipse(Vector((0.0, BODY_Y, z)), rx - inset, ry - inset, angle)


def heights(top, hem, count):
    return [top + (hem - top) * i / (count - 1) for i in range(count)]


def panel_rows(a0, a1, hem, inset=0.0, top=2.86, columns=10):
    return [[point(z, a0 + (a1 - a0) * c / (columns - 1), inset) for c in range(columns)]
            for z in heights(top, hem, 12)]


def outward(z, angle, amount):
    """Push off the panel face: the hang line's radial direction at that point."""
    a = math.radians(angle)
    return Vector((math.cos(a), math.sin(a), 0.0)) * amount


def edge_trim(p, angle, toward, hem, inset, top=2.86):
    """A strip down a panel's vertical edge, lying on its outer face."""
    lift = PANEL / 2 + TRIM_T / 2 + 0.002
    span = math.degrees(TRIM_W / radius_at(2.0)[0])
    a1 = angle + span * toward
    rows = [[point(z, angle, inset) + outward(z, angle, lift), point(z, a1, inset) + outward(z, a1, lift)]
            for z in heights(top - 0.02, hem + 0.01, 12)]
    p.sheet(rows, TRIM_T, TRIM)


def hem_trim(p, a0, a1, hem, inset):
    lift = PANEL / 2 + TRIM_T / 2 + 0.002
    count = 10
    angles = [a0 + (a1 - a0) * c / (count - 1) for c in range(count)]
    rows = [[point(z, a, inset) + outward(z, a, lift) for a in angles] for z in (hem + 0.01 + TRIM_W, hem + 0.01)]
    p.sheet(rows, TRIM_T, TRIM)


def panel(coll, mats, origin, name, a0, a1, hem, inset=0.0, trims=("a0", "a1", "hem"), top=2.86):
    p = B.Part(mats)
    p.sheet(panel_rows(a0, a1, hem, inset, top), PANEL, CLOTH)
    F.bind(p.finish("Mesh_Coat_%s" % name, coll, origin=origin), "Spine2")
    t = B.Part(mats)
    if "a0" in trims:
        edge_trim(t, a0, +1, hem, inset, top)
    if "a1" in trims:
        edge_trim(t, a1, -1, hem, inset, top)
    if "hem" in trims:
        hem_trim(t, a0, a1, hem, inset)
    F.bind(t.finish("Mesh_Coat_%s_Trim" % name, coll, origin=origin), "Spine2")


def tunic(coll, mats, origin, prefix, j):
    """The dark under-layer the side gaps show: the mannequin's torso shells, a little bigger."""
    up = Vector((0, 0, 1))
    torso = 0.84 * 1.12
    shells = (
        ("Chest", "Spine1", j["Spine1"][0] + up * 0.02, j["Spine2"][1] - up * 0.02,
         [(0.0, 0.25 * torso, 0.17 * torso), (0.55, 0.31 * torso, 0.20 * torso), (1.0, 0.27 * torso, 0.17 * torso)]),
        ("Waist", "Spine", j["Spine"][0] - up * 0.02, j["Spine1"][0] + up * 0.04,
         [(0.0, 0.20 * torso, 0.14 * torso), (1.0, 0.23 * torso, 0.155 * torso)]),
        ("Hips", "Hips", j["Hips"][0] - up * 0.2, j["Spine"][0],
         [(0.0, 0.24 * torso, 0.15 * torso), (0.55, 0.27 * torso, 0.17 * torso), (1.0, 0.22 * torso, 0.15 * torso)]),
    )
    for label, bone, a, b, stations in shells:
        p = B.Part(mats)
        p.segment(a, b, stations, TUNIC)
        F.bind(p.finish("Mesh_Coat_%s_Tunic_%s" % (prefix, label), coll, origin=j[bone][0]), bone)


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)
    j = F.body_joints()
    origin = Vector((0.0, BODY_Y, j["Neck"][0].z))

    # Angles: 270 is dead front (-Y), 90 dead back; the body's right is -X (180), left +X (0).
    slab = B.collection("Coll_Coat_Slab")
    panel(slab, mats, origin, "Slab_Back", 28.0, 152.0, 0.85)
    panel(slab, mats, origin, "Slab_FrontRight", 198.0, 292.0, 0.70)
    panel(slab, mats, origin, "Slab_FrontLeft", 286.0, 338.0, 1.10, inset=0.03, trims=("a1", "hem"))
    tunic(slab, mats, origin, "Slab", j)

    tails = B.collection("Coll_Coat_Tails")
    panel(tails, mats, origin, "Tails_FrontRight", 200.0, 266.0, 1.72)
    panel(tails, mats, origin, "Tails_FrontLeft", 274.0, 340.0, 1.72)
    panel(tails, mats, origin, "Tails_BackRight", 92.0, 150.0, 0.72)
    panel(tails, mats, origin, "Tails_BackLeft", 30.0, 88.0, 0.72)
    tunic(tails, mats, origin, "Tails", j)

    B.save(out)
    B.report()


main()
