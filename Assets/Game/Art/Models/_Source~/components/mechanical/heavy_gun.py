"""Vehicle-scale heavy weapons on pintle mounts — riveted steel, not wood.

A family for crawlers, barges and walking cities: something a crew stands
behind, that yaws on a pedestal and pitches on trunnions. Every variation is cut
the same way so any of them can be rigged by the same two bones:

  Base    pedestal ring + column + yoke     origin at the mount plate (yaw pivot)
  Cradle  everything that elevates          origin on the trunnion axis (pitch pivot)
  ...     barrels, shield, ammo, grips      parented to the Cradle

Variations (silhouette first):

  Coll_HeavyGun_Autocannon  twin barrels, jacketed, muzzle brakes, a bent
                            three-facet gun shield and a side ammo box — the
                            crewed defensive gun
  Coll_HeavyGun_RocketPod   a slab-sided 2 x 3 launcher box with ribbed armour
                            and a sight box — reads at distance as "rockets"
  Coll_HeavyGun_Harpoon     one long bore, barbed harpoon head proud of the
                            muzzle, a cable drum and a gas bottle — a salvage
                            gun, built ahead of need

Authored for 2 m crew (grips at ~1.0 m, trunnion 1.15 m); models place it at
their own crew scale. Front is -Y, up is +Z.

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

(STEEL, DARK, RUST, HULL, BLACK, RUBBER, BRASS, AMBER) = range(8)
MATS = [
    "Mat_Metal_Steel_Worn",       # 0  structure — and bevels
    "Mat_Metal_Steel_Dark",       # 1  barrels, hardware
    "Mat_Metal_Rust_Heavy",       # 2  weathered armour
    "Mat_Metal_HullRust_Orange",  # 3  shield / pod faces, matching crawler hulls
    "Mat_Neutral_Black_Matte",    # 4  bores, tube mouths
    "Mat_Plastic_Rubber_Black",   # 5  grips
    "Mat_Metal_Brass_Tarnished",  # 6  gauges, valve wheels
    "Mat_Emissive_Amber",         # 7  sight lamp
]

TRUNNION_Z = 1.15
BEVEL_W = 0.008


def base(name, coll, mats):
    """Pedestal: bolted foot ring, column, and a yoke whose cheeks carry the trunnions."""
    p = Part(mats)
    p.cyl((0, 0, 0.04), 0.42, 0.08, 'Z', 24, DARK)
    p.cyl((0, 0, 0.12), 0.30, 0.10, 'Z', 24, STEEL)
    for i in range(12):
        a = 2 * math.pi * i / 12
        p.cyl((0.36 * math.cos(a), 0.36 * math.sin(a), 0.09), 0.022, 0.03, 'Z', 6, STEEL)
    p.cyl((0, 0, 0.55), 0.13, 0.80, 'Z', 16, STEEL)
    p.cyl((0, 0, 0.93), 0.22, 0.06, 'Z', 20, DARK)          # traverse ring
    p.slab((-0.24, -0.16, 0.95), (0.24, 0.16, 1.00), STEEL)  # yoke floor
    for sx in (-1, 1):
        p.slab((sx * 0.21, -0.14, 0.96), (sx * 0.25, 0.14, TRUNNION_Z + 0.12), STEEL)
        p.cyl((sx * 0.26, 0, TRUNNION_Z), 0.06, 0.05, 'X', 12, DARK)
        p.rivets((sx * 0.255, -0.1, 1.0), (sx * 0.255, -0.1, TRUNNION_Z + 0.08), 3, 0.012, 0.01, 'X', STEEL)
    # traverse handwheel on the right cheek
    p.torus((0.30, 0.10, 1.02), 0.09, 0.012, 'X', 16, 6, BRASS)
    p.cyl((0.28, 0.10, 1.02), 0.015, 0.06, 'X', 6, DARK)
    p.bevel(width=BEVEL_W)
    return p.finish(name, coll, origin=(0, 0, 0))


def attach(children, parent):
    """Parent keeping world transforms. Updates first: a fresh object's matrix_world
    is identity until the view layer is evaluated."""
    bpy.context.view_layer.update()
    for o in children:
        o.parent = parent
        o.matrix_parent_inverse = parent.matrix_world.inverted()


def spade_grips(p, y, z, half_w=0.16):
    for sx in (-1, 1):
        p.cyl((sx * half_w, y, z), 0.022, 0.20, 'Z', 8, RUBBER)
    p.cyl((0, y, z + 0.10), 0.018, half_w * 2 + 0.04, 'X', 8, DARK)
    p.slab((-0.05, y - 0.02, z - 0.12), (0.05, y + 0.14, z - 0.08), DARK)   # thumb-trigger bar


def autocannon(coll, mats):
    b = base("Mesh_HeavyGun_Autocannon_Base", coll, mats)
    c = Part(mats)
    z = TRUNNION_Z
    # receiver: a riveted box with a feed cover
    c.slab((-0.19, -0.35, z - 0.14), (0.19, 0.55, z + 0.16), STEEL)
    c.slab((-0.16, -0.30, z + 0.16), (0.16, 0.40, z + 0.22), DARK)
    for sx in (-1, 1):
        c.rivets((sx * 0.192, -0.3, z + 0.1), (sx * 0.192, 0.5, z + 0.1), 7, 0.012, 0.01, 'X', DARK)
        c.rivets((sx * 0.192, -0.3, z - 0.08), (sx * 0.192, 0.5, z - 0.08), 7, 0.012, 0.01, 'X', DARK)
        c.cyl((sx * 0.215, 0, z), 0.05, 0.05, 'X', 12, DARK)        # trunnion pins
    spade_grips(c, 0.66, z + 0.02)
    c.slab((-0.03, 0.55, z - 0.02), (0.03, 0.66, z + 0.04), DARK)
    c.bevel(width=BEVEL_W)
    cradle = c.finish("Mesh_HeavyGun_Autocannon_Cradle", coll, origin=(0, 0, z))

    br = Part(mats)
    for sx in (-1, 1):
        x = sx * 0.085
        br.cyl((x, -1.15, z), 0.045, 1.60, 'Y', 12, DARK)                     # barrel
        br.tube((x, -0.62, z), 0.075, 0.02, 0.55, 'Y', 14, STEEL)            # cooling jacket
        for k in range(4):
            br.cyl((x, -0.40 - k * 0.15, z), 0.08, 0.025, 'Y', 14, DARK)     # jacket bands
        br.slab((x - 0.06, -2.05, z - 0.05), (x + 0.06, -1.88, z + 0.05), STEEL)  # muzzle brake
        for k in range(3):
            br.slab((x - 0.065, -2.02 + k * 0.05, z - 0.03), (x + 0.065, -2.0 + k * 0.05, z + 0.03), BLACK)
        br.cyl((x, -2.06, z), 0.028, 0.02, 'Y', 10, BLACK)                    # bore
    br.bevel(width=0.005)
    barrels = br.finish("Mesh_HeavyGun_Autocannon_Barrels", coll, origin=(0, 0, z))

    sh = Part(mats)
    # three-facet bent shield: centre plate + two raked wings, with a barrel slot
    t = 0.03
    sh.slab((-0.36, -0.42, z - 0.36), (0.36, -0.42 + t, z - 0.02), HULL)
    sh.slab((-0.36, -0.42, z + 0.07), (0.36, -0.42 + t, z + 0.42), HULL)
    sh.slab((-0.36, -0.42, z - 0.02), (-0.16, -0.42 + t, z + 0.07), HULL)
    sh.slab((0.16, -0.42, z - 0.02), (0.36, -0.42 + t, z + 0.07), HULL)
    for sx in (-1, 1):
        rot = Matrix.Rotation(sx * math.radians(-28), 4, 'Z')
        sh.box((sx * 0.47, -0.37, z + 0.03), (0.24, t, 0.78), HULL, rot)
    sh.rivets((-0.32, -0.425, z + 0.38), (0.32, -0.425, z + 0.38), 9, 0.013, 0.012, 'Y', DARK)
    sh.rivets((-0.32, -0.425, z - 0.32), (0.32, -0.425, z - 0.32), 9, 0.013, 0.012, 'Y', DARK)
    sh.slab((-0.30, -0.39, z + 0.30), (0.30, -0.35, z + 0.34), RUST)        # stiffener strap
    sh.bevel(width=0.005)
    shield = sh.finish("Mesh_HeavyGun_Autocannon_Shield", coll, origin=(0, 0, z))

    am = Part(mats)
    am.slab((-0.52, -0.05, z - 0.22), (-0.22, 0.40, z + 0.10), RUST)          # ammo box
    am.slab((-0.54, -0.07, z + 0.08), (-0.20, 0.42, z + 0.12), DARK)          # lid
    am.slab((-0.40, 0.10, z + 0.12), (-0.34, 0.24, z + 0.16), STEEL)          # latch handle
    am.box((-0.21, 0.12, z + 0.06), (0.06, 0.20, 0.10), DARK,
           Matrix.Rotation(math.radians(20), 4, 'Y'))                         # feed chute
    am.bevel(width=0.006)
    ammo = am.finish("Mesh_HeavyGun_Autocannon_Ammo", coll, origin=(0, 0, z))

    attach((barrels, shield, ammo), cradle)
    attach((cradle,), b)


def rocket_pod(coll, mats):
    b = base("Mesh_HeavyGun_RocketPod_Base", coll, mats)
    z = TRUNNION_Z + 0.10
    c = Part(mats)
    for sx in (-1, 1):
        c.slab((sx * 0.20, -0.10, z - 0.20), (sx * 0.26, 0.10, z + 0.05), STEEL)   # trunnion arms
        c.cyl((sx * 0.23, 0, TRUNNION_Z), 0.05, 0.06, 'X', 12, DARK)
    c.slab((-0.26, -0.12, z - 0.02), (0.26, 0.12, z + 0.04), STEEL)
    spade_grips(c, 0.62, TRUNNION_Z + 0.02, 0.22)
    c.slab((-0.04, 0.10, TRUNNION_Z - 0.02), (0.04, 0.62, TRUNNION_Z + 0.04), DARK)
    c.bevel(width=BEVEL_W)
    cradle = c.finish("Mesh_HeavyGun_RocketPod_Cradle", coll, origin=(0, 0, TRUNNION_Z))

    pod = Part(mats)
    w, h, y0, y1 = 0.46, 0.30, -0.95, 0.45
    zc = z + 0.04 + h
    pod.slab((-w, y0, zc - h), (w, y1, zc + h), HULL)
    for i in range(3):                                           # 2 x 3 tube mouths
        for j in range(2):
            x = -0.30 + 0.30 * i; zz = zc - 0.14 + 0.28 * j
            pod.tube((x, y0 - 0.01, zz), 0.115, 0.025, 0.06, 'Y', 16, DARK)
            pod.cyl((x, y0 + 0.005, zz), 0.092, 0.02, 'Y', 16, BLACK)
    for k in range(5):                                           # ribbed side armour
        yk = y0 + 0.15 + k * 0.28
        for sx in (-1, 1):
            pod.slab((sx * w, yk - 0.03, zc - h - 0.01), (sx * (w + 0.035), yk + 0.03, zc + h + 0.01), RUST)
        pod.slab((-w - 0.01, yk - 0.03, zc + h), (w + 0.01, yk + 0.03, zc + h + 0.03), RUST)
    for sx in (-1, 1):
        pod.rivets((sx * (w + 0.002), y0 + 0.05, zc + h - 0.05), (sx * (w + 0.002), y1 - 0.05, zc + h - 0.05), 9, 0.012, 0.01, 'X', DARK)
    pod.slab((w - 0.02, -0.45, zc + h + 0.03), (w + 0.16, -0.15, zc + h + 0.20), STEEL)   # sight box
    pod.cyl((w + 0.07, -0.455, zc + h + 0.12), 0.045, 0.02, 'Y', 12, AMBER)
    pod.slab((-w - 0.02, y1 - 0.02, zc - h + 0.05), (w + 0.02, y1 + 0.06, zc + h - 0.05), DARK)  # rear blast plate
    pod.bevel(width=0.006)
    box = pod.finish("Mesh_HeavyGun_RocketPod_Pod", coll, origin=(0, 0, TRUNNION_Z))
    attach((box,), cradle)
    attach((cradle,), b)


def harpoon(coll, mats):
    b = base("Mesh_HeavyGun_Harpoon_Base", coll, mats)
    z = TRUNNION_Z
    c = Part(mats)
    c.slab((-0.17, -0.30, z - 0.12), (0.17, 0.45, z + 0.12), STEEL)
    for sx in (-1, 1):
        c.cyl((sx * 0.20, 0, z), 0.05, 0.08, 'X', 12, DARK)
        c.rivets((sx * 0.172, -0.25, z), (sx * 0.172, 0.40, z), 6, 0.012, 0.01, 'X', DARK)
    spade_grips(c, 0.58, z + 0.02)
    c.cyl((0, 0.30, z - 0.26), 0.10, 0.55, 'Y', 14, RUST)            # gas bottle under the breech
    c.cyl((0, 0.60, z - 0.26), 0.04, 0.06, 'Y', 8, BRASS)
    c.torus((0.0, 0.66, z - 0.26), 0.06, 0.01, 'Y', 12, 6, BRASS)      # valve wheel
    c.bevel(width=BEVEL_W)
    cradle = c.finish("Mesh_HeavyGun_Harpoon_Cradle", coll, origin=(0, 0, z))

    br = Part(mats)
    br.cyl((0, -1.25, z), 0.075, 1.90, 'Y', 16, DARK)
    for k in range(5):
        br.cyl((0, -0.45 - k * 0.38, z), 0.095, 0.05, 'Y', 16, STEEL)
    br.cyl((0, -2.22, z), 0.045, 0.10, 'Y', 12, BLACK)
    br.bevel(width=0.004)
    barrel = br.finish("Mesh_HeavyGun_Harpoon_Barrel", coll, origin=(0, 0, z))

    hp = Part(mats)
    hp.cyl((0, -2.45, z), 0.035, 0.40, 'Y', 10, STEEL)                 # shaft proud of the muzzle
    hp.cyl((0, -2.78, z), 0.075, 0.26, 'Y', 10, DARK, radius_top=0.0)  # point
    for k in range(4):                                                  # barbs
        a = math.pi / 2 * k
        rot = Matrix.Rotation(a, 4, 'Y') @ Matrix.Rotation(math.radians(25), 4, 'X')
        hp.box((0.07 * math.cos(a), -2.62, z + 0.07 * math.sin(a)), (0.02, 0.18, 0.05), DARK, rot)
    head = hp.finish("Mesh_HeavyGun_Harpoon_Head", coll, origin=(0, 0, z))

    dr = Part(mats)
    dr.cyl((0.30, 0.05, z - 0.05), 0.16, 0.18, 'X', 18, STEEL)         # cable drum on the left cheek
    dr.cyl((0.30, 0.05, z - 0.05), 0.12, 0.19, 'X', 18, RUST)
    for sx in (0.20, 0.40):
        dr.cyl((sx, 0.05, z - 0.05), 0.19, 0.02, 'X', 18, DARK)
    drum = dr.finish("Mesh_HeavyGun_Harpoon_CableDrum", coll, origin=(0, 0, z))

    attach((barrel, head, drum), cradle)
    attach((cradle,), b)


out = parse_out()
start(out)
mats = link_materials(MATS)
for name, build in (("Coll_HeavyGun_Autocannon", autocannon),
                    ("Coll_HeavyGun_RocketPod", rocket_pod),
                    ("Coll_HeavyGun_Harpoon", harpoon)):
    build(collection(name), mats)
report()
save(out)
