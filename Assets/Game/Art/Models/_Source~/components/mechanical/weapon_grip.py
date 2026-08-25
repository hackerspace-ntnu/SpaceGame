"""Weapon grips and shoulder furniture — the parts a hand or a body touches.

Split out of the launch tube rather than modelled into it, because the barrel
and the thing you hold it by have no reason to change together: the same
pistol grip belongs on a pipe launcher, a mounted gun and a crossbow, and the
same saddle belongs on anything shouldered. Every variation's origin sits on
its MOUNT FACE, so bolting one to a rail is a translation and nothing else.

Four variations, which is more than any one weapon needs — the extra pair is
built ahead because a grip is the single most reusable thing in a weapon:

  Coll_WeaponGrip_Pistol   raked grip with trigger and guard — the hero
  Coll_WeaponGrip_Fore     vertical foregrip, wrapped
  Coll_WeaponGrip_Saddle   shoulder saddle with a padded face  (built ahead)
  Coll_WeaponGrip_Spade    twin spade handles for a mounted gun (built ahead)

Front is -Y, up is +Z. Mount faces point +Z, so a grip hangs BELOW its origin
and a saddle sits above it — that is the only asymmetry, and it is what makes
"drop it on the rail at zero rotation" work for both.

Generation script — historical record. The .blend is the source of truth;
never re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix, Vector  # noqa: E402

# Index 0 is the dark machined steel the frames are made of, so bevels land on
# the colour that most edges already are.
(DARK, STEEL, RUBBER, WOOD, CANVAS, BRASS) = range(6)
MATS = [
    "Mat_Metal_Steel_Dark",       # 0  frames, trigger, guard — and bevels
    "Mat_Metal_Steel_Worn",       # 1  mount plates and bolts
    "Mat_Plastic_Rubber_Black",   # 2  moulded grip faces and pads
    "Mat_Wood_Ply_Worn",          # 3  the wooden grip cheeks
    "Mat_Fabric_Canvas_Faded",    # 4  wrap on the foregrip
    "Mat_Metal_Brass_Tarnished",  # 5  pins and ferrules
]

BEVEL_W = 0.0025


def oval(hw, hd, yc=0.0, n=12, squash=0.7):
    """A superelliptic profile in (x, y) — one station of a grip's section.

    A grip has to swell at the palm and pinch at the web of the thumb, and a
    superellipse is what gives it flat side panels that still turn a round
    corner. `yc` shifts the whole station backward, which is how the rake is
    built: see `pistol`.
    """
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        cs, sn = math.cos(a), math.sin(a)
        pts.append((hw * math.copysign(abs(cs) ** squash, cs),
                    yc + hd * math.copysign(abs(sn) ** squash, sn)))
    return pts


def plate(p, size=(0.044, 0.090), mat=STEEL):
    """The bolted mount plate every variation shares, straddling z = 0."""
    faces = p.box((0, 0, -0.005), (size[0], size[1], 0.010), mat)
    faces += p.rivets((-size[0] * 0.32, -size[1] * 0.34, 0.002),
                      (size[0] * 0.32, size[1] * 0.34, 0.002), 4,
                      radius=0.004, height=0.005, axis='Z', mat=BRASS)
    return faces


def pistol(coll, mats, name, rake=0.34, length=0.140):
    """Raked pistol grip with trigger and guard.

    The grip is ONE loft whose stations walk backward as they descend, not a
    stack of rotated boxes. The stacked version is worth naming because it
    looked reasonable in code and came out as loose confetti: `Part.box`'s
    `rot` turns a box about its OWN centre, so every slab tilted in place and
    the column never raked — it just developed gaps. Rake belongs in the
    station offsets, where the geometry is continuous by construction.

    `rake` is the backward run per unit of drop, not an angle, because that is
    the number the stations are actually written in.
    """
    p = Part(mats)
    hard = plate(p)

    top = -0.008
    stations = ((0.00, 0.019, 0.028), (0.26, 0.022, 0.032),
                (0.58, 0.022, 0.030), (0.84, 0.020, 0.026),
                (1.00, 0.017, 0.021))
    p.loft([(top - length * t, oval(hw, hd, rake * length * t))
            for t, hw, hd in stations], axis='Z', mat=RUBBER)

    # Wooden cheeks, so the grip is not one moulded lump. Placed on the raked
    # axis rather than straight down, or they slide off the back of it.
    for sx in (-1, 1):
        for t in (0.30, 0.52, 0.74):
            hard += p.box((sx * 0.019, rake * length * t, top - length * t),
                          (0.006, 0.042, length * 0.24), WOOD)

    # Trigger guard: a bow of three struts. Each runs between two points and
    # takes its length and rotation from them, because a guard runs at a
    # compound angle and a guessed Euler triple goes silently wrong. Segments
    # are overlapped 18% so the corners close.
    for a, b in (((-0.050, -0.014), (-0.064, -0.042)),
                 ((-0.064, -0.042), (-0.052, -0.070)),
                 ((-0.052, -0.070), (-0.014, -0.080))):
        mid = Vector((0, (a[0] + b[0]) / 2.0, (a[1] + b[1]) / 2.0))
        d = Vector((0, b[0] - a[0], b[1] - a[1]))
        turn = Vector((0, 1, 0)).rotation_difference(d.normalized()) \
                                .to_matrix().to_4x4()
        hard += p.box(mid, (0.014, d.length * 1.18, 0.009), DARK, rot=turn)

    # Trigger, hanging inside the bow.
    hard += p.box((0, -0.038, -0.048), (0.009, 0.013, 0.036), DARK,
                  rot=Matrix.Rotation(math.radians(-12), 4, 'X'))

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def fore(coll, mats, name, length=0.105):
    p = Part(mats)
    hard = plate(p, size=(0.040, 0.052))

    p.cyl((0, 0, -0.010 - length / 2.0), 0.020, length, 'Z', 14, RUBBER,
          radius_top=0.023)
    # Canvas wrap: alternating rings, offset so the wrap reads as wound rather
    # than machined.
    for i in range(6):
        z = -0.022 - i * 0.0145
        p.cyl((0, 0, z), 0.0215 + (i % 2) * 0.0011, 0.013, 'Z', 12, CANVAS)
    hard += p.cyl((0, 0, -0.010 - length), 0.026, 0.010, 'Z', 14, DARK)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def saddle(coll, mats, name, width=0.115, depth=0.150):
    """A shoulder saddle. Sits ABOVE the mount face — see the module docstring."""
    p = Part(mats)
    hard = plate(p, size=(0.048, depth * 0.6))

    # A shallow trough, made from three canted slabs rather than a curve: the
    # shoulder only ever sees the two side walls and the floor between them.
    hard += p.box((0, 0, 0.020), (width * 0.62, depth, 0.014), DARK)
    for sx in (-1, 1):
        hard += p.box((sx * width * 0.36, 0, 0.036),
                      (0.014, depth, 0.048), DARK,
                      rot=Matrix.Rotation(math.radians(sx * 22), 4, 'Y'))
    hard += p.box((0, 0, 0.029), (width * 0.56, depth * 0.94, 0.012), RUBBER)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def spade(coll, mats, name, span=0.150, length=0.100):
    """Twin spade handles, as a mounted gun or a heavy tripod weapon wears."""
    p = Part(mats)
    hard = plate(p, size=(span * 0.9, 0.060))

    hard += p.box((0, 0, -0.014), (span, 0.034, 0.018), STEEL)
    for sx in (-1, 1):
        rot = Matrix.Rotation(math.radians(sx * -9), 4, 'Y')
        hard += p.box((sx * span * 0.46, 0, -0.014 - length / 2.0),
                      (0.030, 0.038, length), RUBBER, rot=rot)
        hard += p.box((sx * span * 0.46, -0.030, -0.030),
                      (0.012, 0.026, 0.026), DARK)  # thumb trigger
        for i in range(4):
            p.cyl((sx * span * 0.46, 0, -0.030 - i * 0.020), 0.019, 0.008,
                  'Z', 10, WOOD)

    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    pistol(collection("Coll_WeaponGrip_Pistol"), mats, "Mesh_WeaponGrip_Pistol")
    fore(collection("Coll_WeaponGrip_Fore"), mats, "Mesh_WeaponGrip_Fore")
    saddle(collection("Coll_WeaponGrip_Saddle"), mats, "Mesh_WeaponGrip_Saddle")
    spade(collection("Coll_WeaponGrip_Spade"), mats, "Mesh_WeaponGrip_Spade")

    report()
    save(out)


main()
