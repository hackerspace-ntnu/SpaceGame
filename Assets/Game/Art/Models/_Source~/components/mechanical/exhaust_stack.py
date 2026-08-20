"""components/mechanical/exhaust_stack — flues, scrubbers and roof vents.

`drill_derrick` already has a `Flare`, and this is not that. A flare is a burner
on a tall open pole: it exists to be seen alight from a distance and it is bare
steel all the way up. These are *ducts* — things gas travels through and
condenses in — so they are lagged, banded, streaked below every joint, and short
enough to stand on a roof rather than above a rig.

The distinction matters because a roofline is where a big industrial silhouette
either gets interesting or stays a box. Stacks are the cheapest vertical
punctuation there is: four of these on a roof cost under 3 k triangles together
and break a 60 m horizontal mass into something with a rhythm.

Origins are at the base centre, standing up +Z, so they drop onto any roof by
setting Z to that roof's top face.

    blender --background --python exhaust_stack.py -- --out exhaust_stack.blend

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
    "Mat_Metal_Steel_Worn",        # 0 STEEL  the flue barrels, frames, ladders
    "Mat_Metal_Steel_Dark",        # 1 DARK   collars, cowls, fittings
    "Mat_Metal_Rust_Heavy",        # 2 RUST   the streak below every joint
    "Mat_Metal_HullRust_Orange",   # 3 HULL   lagging jackets and vessel shells
    "Mat_Neutral_Black_Matte",     # 4 BLACK  the hole at the top
    "Mat_Metal_Chrome_Scuffed",    # 5 CHROME worn bare at the handrails
    "Mat_Emissive_Amber",          # 6 AMBER  obstruction lamp
]
STEEL, DARK, RUST, HULL, BLACK, CHROME, AMBER = range(7)


def along(a, b):
    d = Vector(b) - Vector(a)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    return rot.to_4x4(), d.length


def member(p, a, b, size=0.09, mat=STEEL, overlap=0.0):
    rot, length = along(a, b)
    p.box((Vector(a) + Vector(b)) / 2.0, (size, size, length + overlap), mat,
          rot=rot)


def rod(p, a, b, radius=0.03, mat=STEEL, seg=6):
    rot, length = along(a, b)
    p.cyl((Vector(a) + Vector(b)) / 2.0, radius, length, 'Z', seg=seg, mat=mat,
          rot=rot)


def barrel(p, x, y, height, radius, seg=12, base_z=0.0, mat=STEEL,
           bands=True):
    """One flue: barrel, flanged joints, and the rust streak below each one.

    The streaks are the reason this is worth a component rather than a
    cylinder. A clean pipe reads as new; a pipe with a dark run of corrosion
    trailing down from every flange reads as forty years old, and it is four
    thin boxes.
    """
    p.cyl((x, y, base_z + height / 2.0), radius, height, 'Z', seg=seg, mat=mat)
    if not bands:
        return
    joints = max(2, int(height / 2.6))
    for i in range(joints):
        z = base_z + height * (i + 1) / (joints + 1)
        p.cyl((x, y, z), radius * 1.16, 0.16, 'Z', seg=seg, mat=DARK)
        for k in range(3):                      # streaks below the flange
            a = 2 * math.pi * (k / 3.0 + 0.1 * i)
            p.box((x + radius * 1.02 * math.cos(a),
                   y + radius * 1.02 * math.sin(a), z - 0.62),
                  (radius * 0.34, radius * 0.34, 1.1), RUST)


def cowl(p, x, y, z, radius, mat=DARK):
    """A rain cap on three legs, with the dark hole visible under it."""
    p.cyl((x, y, z - 0.10), radius * 0.86, 0.16, 'Z', seg=10, mat=BLACK)
    for k in range(3):
        a = 2 * math.pi * k / 3.0
        member(p, (x + radius * 0.8 * math.cos(a), y + radius * 0.8 *
                   math.sin(a), z - 0.05),
               (x + radius * 0.8 * math.cos(a), y + radius * 0.8 *
                math.sin(a), z + 0.34), 0.05)
    p.cyl((x, y, z + 0.40), radius * 1.35, 0.10, 'Z', seg=12, mat=mat,
          radius_top=radius * 0.5)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def build_flue(coll, mats):
    """A single 9 m guyed flue with a rain cowl. Base 1.9 m square.

    The tall thin one. Guy wires rather than a lattice frame, because at 9 m a
    guyed stack is what an operator would actually put up, and three taut
    diagonals give the silhouette something to do above the roofline.
    """
    p = Part(mats)
    h, r = 9.0, 0.44

    p.box((0, 0, 0.16), (1.9, 1.9, 0.32), DARK)             # base plate
    p.box((0, 0, 0.46), (1.35, 1.35, 0.34), STEEL)          # transition box
    for s in ((-1, -1), (-1, 1), (1, -1), (1, 1)):          # hold-down bolts
        p.cyl((s[0] * 0.78, s[1] * 0.78, 0.36), 0.07, 0.22, 'Z', seg=6,
              mat=CHROME)
    barrel(p, 0, 0, h - 0.7, r, seg=12, base_z=0.66)
    cowl(p, 0, 0, h - 0.04, r)

    ring_z = h * 0.62                                        # guy collar
    p.cyl((0, 0, ring_z), r * 1.3, 0.14, 'Z', seg=12, mat=DARK)
    for k in range(3):
        a = 2 * math.pi * k / 3.0 + 0.3
        top = (r * 1.3 * math.cos(a), r * 1.3 * math.sin(a), ring_z)
        rod(p, top, (2.9 * math.cos(a), 2.9 * math.sin(a), 0.12), 0.028)
        p.box((2.9 * math.cos(a), 2.9 * math.sin(a), 0.14), (0.34, 0.34, 0.28),
              STEEL)

    p.box((0, 0, h + 0.30), (0.22, 0.22, 0.30), DARK)        # lamp mast
    p.cyl((0, 0, h + 0.50), 0.11, 0.16, 'Z', seg=8, mat=AMBER)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_ExhaustStack_Flue", coll)


def build_cluster(coll, mats):
    """Three flues of unequal height on one base. 3.6 x 2.2 m, 7.4 m tall.

    Unequal on purpose. Three stacks the same height read as a manifold
    somebody designed; three different heights read as things added one at a
    time, which is the story the whole building is telling.
    """
    p = Part(mats)
    p.box((0, 0, 0.17), (3.6, 2.2, 0.34), DARK)
    p.box((0, 0, 0.70), (3.2, 1.8, 0.72), HULL)              # shared plenum
    for i in range(4):
        p.box((-1.4 + i * 0.93, 0, 0.70), (0.09, 1.9, 0.76), STEEL)

    for x, h, r in ((-1.15, 6.3, 0.30), (0.0, 7.0, 0.34), (1.2, 4.6, 0.26)):
        barrel(p, x, 0, h, r, seg=10, base_z=1.06)
        cowl(p, x, 0, 1.06 + h + 0.06, r)
    # Cross-ties between the three, which is why they are still standing.
    for z in (4.0, 5.6):
        member(p, (-1.15, 0, z), (1.2, 0, z), 0.07)
    member(p, (0.0, 0, 6.6), (1.2, 0, 5.4), 0.06)

    p.cyl((-1.95, 0, 1.4), 0.20, 2.0, 'Z', seg=8, mat=RUST)  # a dead riser
    p.cyl((-1.95, 0, 2.45), 0.24, 0.14, 'Z', seg=8, mat=BLACK)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_ExhaustStack_Cluster", coll)


def build_scrubber(coll, mats):
    """A fat vessel with an access ladder and top platform. 3.0 m dia, 8.2 m.

    The wide one. Between this and `Flue` the roofline gets both a vertical
    accent and a mass, and a roof with only thin stacks looks like a pincushion.
    """
    p = Part(mats)
    r, h = 1.5, 6.4

    p.cyl((0, 0, 0.22), r * 1.28, 0.44, 'Z', seg=20, mat=DARK)     # skirt
    for k in range(8):
        a = 2 * math.pi * k / 8
        p.box((r * 1.2 * math.cos(a), r * 1.2 * math.sin(a), 0.22),
              (0.26, 0.26, 0.44), STEEL)
    p.cyl((0, 0, 0.44 + h / 2.0), r, h, 'Z', seg=20, mat=HULL)
    p.cyl((0, 0, 0.44 + h + 0.34), r * 0.92, 0.70, 'Z', seg=20, mat=HULL,
          radius_top=r * 0.55)                                     # domed top
    for i in range(3):                                             # lagging
        p.torus((0, 0, 1.6 + i * 2.0), r + 0.04, 0.10, 'Z', 20, 6, mat=STEEL)
    for i in range(2):                                             # streaks
        for k in range(4):
            a = 2 * math.pi * k / 4 + i
            p.box((r * 1.01 * math.cos(a), r * 1.01 * math.sin(a),
                   1.6 + i * 2.0 - 0.75), (0.20, 0.20, 1.3), RUST)

    barrel(p, 0, 0, 1.5, 0.34, seg=10, base_z=0.44 + h + 0.62, bands=False)
    cowl(p, 0, 0, 0.44 + h + 2.16, 0.34)

    # Top platform with a kick rail, and the ladder that reaches it.
    pz = 0.44 + h + 0.10
    p.torus((0, 0, pz), r * 1.15, 0.07, 'Z', 20, 6, mat=STEEL)
    for k in range(10):
        a = 2 * math.pi * k / 10
        p.box((r * 1.15 * math.cos(a), r * 1.15 * math.sin(a), pz - 0.42),
              (0.09, 0.09, 0.84), STEEL)
    p.torus((0, 0, pz + 0.55), r * 1.15, 0.05, 'Z', 20, 6, mat=CHROME)
    for k in range(10):
        a = 2 * math.pi * k / 10
        p.box((r * 1.15 * math.cos(a), r * 1.15 * math.sin(a), pz + 0.30),
              (0.07, 0.07, 0.60), STEEL)

    lx = -(r + 0.42)
    for s in (-1, 1):
        member(p, (lx, s * 0.28, 0.3), (lx, s * 0.28, pz + 0.5), 0.06)
    for i in range(13):
        member(p, (lx, -0.28, 0.6 + i * 0.52), (lx, 0.28, 0.6 + i * 0.52),
               0.045)
    for i in range(4):                                            # ladder ties
        p.box((lx / 2.0 - 0.2, 0, 1.2 + i * 1.7), (0.5, 0.10, 0.10), STEEL)

    p.cyl((r + 0.3, 0, 1.1), 0.26, 1.6, 'X', seg=10, mat=RUST)    # inlet spool
    p.cyl((r + 1.05, 0, 1.1), 0.32, 0.16, 'X', seg=10, mat=DARK)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_ExhaustStack_Scrubber", coll)


def build_cowl(coll, mats):
    """A short capped roof vent, 1.5 m tall on a 1.4 m flashed kerb.

    The filler. Every roof needs three or four small things that are not
    interesting, or the interesting ones stop reading as special.
    """
    p = Part(mats)
    p.box((0, 0, 0.12), (1.4, 1.4, 0.24), RUST)              # flashed kerb
    p.box((0, 0, 0.34), (1.05, 1.05, 0.30), DARK)
    barrel(p, 0, 0, 0.85, 0.30, seg=10, base_z=0.49, bands=False)
    p.cyl((0, 0, 1.30), 0.36, 0.12, 'Z', seg=10, mat=DARK)
    p.cyl((0, 0, 1.42), 0.30, 0.16, 'Z', seg=10, mat=BLACK)
    p.cyl((0, 0, 1.56), 0.40, 0.09, 'Z', seg=10, mat=STEEL,
          radius_top=0.18)                                    # the cowl itself
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.4
        p.box((0.42 * math.cos(a), 0.42 * math.sin(a), 0.30),
              (0.10, 0.10, 0.34), STEEL)
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_ExhaustStack_Cowl", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_flue(collection("Coll_ExhaustStack_Flue", root), mats)
    build_cluster(collection("Coll_ExhaustStack_Cluster", root), mats)
    build_scrubber(collection("Coll_ExhaustStack_Scrubber", root), mats)
    build_cowl(collection("Coll_ExhaustStack_Cowl", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
