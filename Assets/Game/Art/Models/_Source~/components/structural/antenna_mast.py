"""components/structural/antenna_mast — roof masts for the land-ship.

The small verticals that break a crawler's roofline: a whip aerial, a dish on a
pole, a flag pole with its lantern and a squat beacon. They are cheap on
purpose (120-400 triangles each) because a roof carries several of them, and
their job is silhouette, not detail.

Each stands up +Z from a bolted base with its origin at the base centre, so it
drops onto any deck by setting Z to that deck's top face. The dish faces -Y and
tilts 20 degrees up; the pennant flies toward +X.

    blender --background --python antenna_mast.py -- --out antenna_mast.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",        # 0 STEEL  poles, rods
    "Mat_Metal_Steel_Dark",        # 1 DARK   bases, clamps, cages
    "Mat_Metal_Rust_Heavy",        # 2 RUST   junction box, streaks
    "Mat_Metal_Rust_Pale",         # 3 PALE   dish face
    "Mat_Metal_Brass_Tarnished",   # 4 BRASS  lantern frame, finials
    "Mat_Plastic_Rubber_Black",    # 5 RUBBER insulators, conduit
    "Mat_Fabric_Canvas_Faded",     # 6 CANVAS pennant
    "Mat_Fabric_Rope_Hemp",        # 7 ROPE   lashings, halyard
    "Mat_Emissive_Amber",          # 8 AMBER  lantern and beacon glow
    "Mat_Metal_HullRust_Orange",   # 9 HULL   beacon housing paint
]
STEEL, DARK, RUST, PALE, BRASS, RUBBER, CANVAS, ROPE, AMBER, HULL = range(10)


def along(a, b):
    d = Vector(b) - Vector(a)
    return Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4(), d.length


def attach(children, parent):
    """Parent rigidly without moving anything."""
    bpy.context.view_layer.update()
    inv = parent.matrix_world.inverted()
    for c in children:
        c.parent = parent
        c.matrix_parent_inverse = inv


def strut(p, a, b, size, mat):
    rot, length = along(a, b)
    return p.box((Vector(a) + Vector(b)) / 2.0, (size, size, length), mat, rot=rot)


def build_whip(coll, mats):
    """7 m tapering whip on a spring base, with a guy-wire clamp half way up."""
    p = Part(mats)
    p.box((0, 0, 0.02), (0.26, 0.26, 0.04), DARK)                        # deck plate
    p.cyl((0, 0, 0.14), 0.07, 0.20, 'Z', seg=6, mat=RUBBER)              # insulator
    p.cyl((0, 0, 0.36), 0.045, 0.26, 'Z', seg=6, mat=DARK)               # spring core
    p.torus((0, 0, 0.36), 0.05, 0.018, 'Z', maj_seg=6, min_seg=3, mat=STEEL)  # coil
    base = p.finish("Mesh_Mast_Whip_Base", coll)
    r = Part(mats)
    r.cyl((0, 0, 3.74), 0.03, 6.52, 'Z', seg=4, mat=STEEL, radius_top=0.008, cap=False)
    r.box((0, 0, 3.60), (0.08, 0.08, 0.08), DARK)                        # guy clamp
    r.box((0.07, 0, 3.60), (0.08, 0.02, 0.05), DARK)                     # guy eye
    attach([r.finish("Mesh_Mast_Whip_Rod", coll, origin=(0, 0, 0.48))], base)


def build_dish(coll, mats):
    """3 m pole, 0.9 m dish tilted 20 deg up toward -Y, junction box low down."""
    p = Part(mats)
    p.cyl((0, 0, 0.03), 0.20, 0.06, 'Z', seg=8, mat=DARK)                # base flange
    for x in (-0.14, 0.14):
        p.box((x, 0, 0.07), (0.05, 0.05, 0.04), DARK)
    p.cyl((0, 0, 1.53), 0.06, 2.96, 'Z', seg=8, mat=STEEL)               # pole
    p.box((0, -0.08, 3.0), (0.12, 0.26, 0.10), DARK)                     # head arm
    pole = p.finish("Mesh_Mast_Dish_Pole", coll)

    p = Part(mats)
    tilt = Matrix.Rotation(math.radians(-20), 3, 'X')
    vertex = Vector((0, -0.22, 3.02))
    k, seg = 0.6, 10

    def dish_pt(r, a):
        return vertex + tilt @ Vector((r * math.cos(a), -k * r * r, r * math.sin(a)))

    rows = [[dish_pt(r, 2 * math.pi * j / seg) for j in range(seg)]
            for r in (0.05, 0.26, 0.45)]
    p.sheet(rows, 0.02, PALE, closed=True)
    axis = tilt @ Vector((0, -1, 0))
    rot, _ = along((0, 0, 0), axis)
    p.cyl(vertex - axis * 0.01, 0.08, 0.06, 'Z', seg=6, mat=DARK, rot=rot)   # hub
    focus = vertex + axis * 0.36
    for j in (2, 5, 8):
        strut(p, dish_pt(0.43, 2 * math.pi * j / seg), focus, 0.015, DARK)
    p.cyl(focus, 0.04, 0.10, 'Z', seg=6, mat=BRASS, rot=rot)             # feed horn
    dish = p.finish("Mesh_Mast_Dish_Reflector", coll, origin=vertex)

    p = Part(mats)
    box = p.slab((-0.15, -0.22, 1.00), (0.15, -0.055, 1.42), RUST)       # junction box
    p.cyl((0.08, -0.12, 2.18), 0.022, 1.52, 'Z', seg=6, mat=RUBBER, cap=False)
    p.box((0.08, -0.12, 2.95), (0.06, 0.06, 0.05), DARK)                 # cable gland
    p.bevel(box, width=0.015, segments=1)
    attach([dish, p.finish("Mesh_Mast_Dish_JunctionBox", coll, origin=(0, -0.055, 1.00))], pole)


def build_flag(coll, mats):
    """4 m pole with a tattered pennant, rope lashings and a hanging lantern."""
    p = Part(mats)
    p.box((0, 0, 0.02), (0.30, 0.30, 0.04), DARK)
    p.cyl((0, 0, 2.02), 0.05, 3.96, 'Z', seg=6, mat=STEEL)
    p.box((0, 0, 4.03), (0.08, 0.08, 0.06), BRASS)                       # finial
    for z in (3.40, 3.82):                                               # lashings
        p.torus((0, 0, z), 0.06, 0.018, 'Z', maj_seg=6, min_seg=3, mat=ROPE)
    p.cyl((0.05, 0, 2.20), 0.008, 1.60, 'Z', seg=4, mat=ROPE, cap=False)  # halyard
    p.box((-0.24, 0, 2.55), (0.48, 0.04, 0.04), DARK)                    # lantern arm
    pole = p.finish("Mesh_Mast_Flag_Pole", coll)

    p = Part(mats)
    # Pennant: hoisted at 3.3-3.9 m, flying +X, frayed unevenly at the fly.
    stations = 7
    rows = []
    fray = (0.0, 0.0, 0.0, 0.0, 0.10, -0.08, 0.14)
    for i in range(stations):
        t = i / (stations - 1)
        x = 0.07 + 1.55 * t + fray[i] * 0.5
        h = 0.60 * (1 - t) + 0.10 * t
        y = 0.06 * math.sin(t * 5.5)
        top = 3.90 - 0.10 * t
        rows.append([(x, y, top), (x + fray[i] * 0.5, y * 1.2, top - h / 2.0),
                     (x - fray[i], y * 0.8, top - h)])
    p.sheet(rows, 0.012, CANVAS, smooth=False)
    pennant = p.finish("Mesh_Mast_Flag_Pennant", coll, origin=(0.05, 0, 3.60))

    p = Part(mats)
    lx = -0.42
    p.box((lx, 0, 2.47), (0.02, 0.02, 0.12), DARK)                       # hook
    p.cyl((lx, 0, 2.38), 0.10, 0.05, 'Z', seg=6, mat=BRASS, radius_top=0.04)
    p.cyl((lx, 0, 2.26), 0.075, 0.20, 'Z', seg=6, mat=AMBER)
    p.cyl((lx, 0, 2.145), 0.09, 0.04, 'Z', seg=6, mat=BRASS)
    attach([pennant, p.finish("Mesh_Mast_Flag_Lantern", coll, origin=(lx, 0, 2.53))], pole)


def build_beacon(coll, mats):
    """A 1.5 m post carrying an amber beacon lamp inside a wire cage."""
    p = Part(mats)
    p.box((0, 0, 0.02), (0.34, 0.34, 0.04), DARK)
    post = p.box((0, 0, 0.64), (0.12, 0.12, 1.22), HULL)
    for s in (-1, 1):                                                    # gussets
        p.box((s * 0.09, 0, 0.14), (0.07, 0.03, 0.20), DARK)
    p.bevel(post, width=0.012, segments=1)
    post_obj = p.finish("Mesh_Mast_Beacon_Post", coll)

    p = Part(mats)
    p.cyl((0, 0, 1.28), 0.14, 0.12, 'Z', seg=6, mat=DARK)                # lamp base
    p.cyl((0, 0, 1.40), 0.11, 0.14, 'Z', seg=8, mat=AMBER, radius_top=0.08)  # lens
    p.cyl((0, 0, 1.49), 0.09, 0.05, 'Z', seg=6, mat=DARK)                # cap
    for k in range(4):
        a = 2 * math.pi * k / 4 + math.pi / 4
        p.box((0.14 * math.cos(a), 0.14 * math.sin(a), 1.40), (0.015, 0.015, 0.18), DARK)
    p.torus((0, 0, 1.49), 0.14, 0.01, 'Z', maj_seg=8, min_seg=3, mat=DARK)
    attach([p.finish("Mesh_Mast_Beacon_Lamp", coll, origin=(0, 0, 1.22))], post_obj)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_whip(collection("Coll_Mast_Whip", root), mats)
    build_dish(collection("Coll_Mast_Dish", root), mats)
    build_flag(collection("Coll_Mast_Flag", root), mats)
    build_beacon(collection("Coll_Mast_Beacon", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
