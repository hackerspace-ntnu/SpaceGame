"""components/props/crew_quarters — the land-ship's crew-deck furniture.

Bunks, a hammock, the mess table, a locker and the galley stove, all sized for
the 3 m crew: a 3.4 m bunk, a 1.3 m table, a 3.2 m locker. The older `bunk`,
`wall_locker` and `galley_unit` components were built for a 2 m human and read
as doll furniture next to the crew, which is why these exist rather than
scaled copies of those.

Every piece stands on the floor with its origin at the bottom centre and its
open or working side facing -Y. The ajar locker door and the kettle are their
own objects: the door carries its origin on the hinge axis so it can be swung,
the kettle can be lifted off the hob.

    blender --background --python crew_quarters.py -- --out crew_quarters.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",        # 0 STEEL  frames, posts, carcasses
    "Mat_Metal_Steel_Dark",        # 1 DARK   pans, hinges, bolts, handles
    "Mat_Metal_HullRust_Orange",   # 2 HULL   painted doors and table top
    "Mat_Metal_Rust_Heavy",        # 3 RUST   stove iron, streaks
    "Mat_Metal_Rust_Deep",         # 4 DEEP   firebox, flue soot
    "Mat_Fabric_Canvas_Faded",     # 5 CANVAS mattresses, hammock
    "Mat_Fabric_Canvas_Sand",      # 6 SAND   pillows
    "Mat_Fabric_Seat_Ochre",       # 7 OCHRE  rolled blanket
    "Mat_Fabric_Rope_Hemp",        # 8 ROPE   hammock lines
    "Mat_Wood_Ply_Worn",           # 9 WOOD   spreader bars
    "Mat_Neutral_Black_Matte",     # 10 BLACK vent backing, firebox mouth
    "Mat_Metal_Brass_Tarnished",   # 11 BRASS kettle, knobs
    "Mat_Metal_Copper_Oxide",      # 12 COPPER kettle body
]
(STEEL, DARK, HULL, RUST, DEEP, CANVAS, SAND, OCHRE, ROPE, WOOD, BLACK, BRASS,
 COPPER) = range(13)


def along(a, b):
    d = Vector(b) - Vector(a)
    return Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4(), d.length


def rope(p, a, b, radius, mat, seg=4):
    rot, length = along(a, b)
    return p.cyl((Vector(a) + Vector(b)) / 2.0, radius, length, 'Z', seg=seg,
                 mat=mat, rot=rot, cap=False)


def attach(children, parent):
    """Parent rigidly without moving anything."""
    bpy.context.view_layer.update()
    inv = parent.matrix_world.inverted()
    for c in children:
        c.parent = parent
        c.matrix_parent_inverse = inv


def vent(p, x, y, z, width, height, slats, rot=None):
    """A black-backed vent with angled slats, on a face looking -Y."""
    faces = p.box((x, y, z), (width, 0.012, height), BLACK)
    tilt = Matrix.Rotation(math.radians(35), 4, 'X')
    for i in range(slats):
        sz = z - height / 2.0 + height * (i + 0.5) / slats
        faces += p.box((x, y - 0.012, sz), (width + 0.02, 0.012, height / slats * 0.9),
                       DARK, rot=tilt)
    return faces


# ---------------------------------------------------------------------------
# Bunk_Double — 3.4 x 1.3 x 3.0, length along X, open side -Y, ladder at +X
# ---------------------------------------------------------------------------

BUNK_X, BUNK_Y = 1.62, 0.61      # post centres
BUNK_DECKS = (0.55, 1.95)        # top of each bed pan


def bunk_frame(coll, mats):
    p = Part(mats)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * BUNK_X, sy * BUNK_Y, 1.5), (0.08, 0.08, 3.0), STEEL)
    for z in BUNK_DECKS:
        p.slab((-BUNK_X - 0.04, -BUNK_Y, z - 0.05), (BUNK_X + 0.04, BUNK_Y, z), DARK)
        for sy in (-1, 1):
            p.box((0, sy * BUNK_Y, z - 0.03), (2 * BUNK_X - 0.06, 0.07, 0.10), STEEL)
    for sx in (-1, 1):                                   # ceiling ties
        p.box((sx * BUNK_X, 0, 2.95), (0.07, 2 * BUNK_Y - 0.06, 0.07), STEEL)
    rail_z = BUNK_DECKS[1] + 0.48                        # top-bunk guard rail
    p.box((-0.25, -BUNK_Y, rail_z), (2.5, 0.06, 0.06), STEEL)
    for x in (-1.4, 0.95):
        p.box((x, -BUNK_Y, (rail_z + BUNK_DECKS[1]) / 2.0),
              (0.05, 0.05, rail_z - BUNK_DECKS[1]), STEEL)
    return p.finish("Mesh_Bunk_Double_Frame", coll)


def bunk_mattress(z, name, coll, mats, blanket):
    p = Part(mats)
    pad = p.slab((-BUNK_X + 0.06, -BUNK_Y + 0.06, z + 0.001), (BUNK_X - 0.06, BUNK_Y - 0.06, z + 0.14), CANVAS)
    pillow = p.box((-BUNK_X + 0.36, 0, z + 0.18), (0.42, 0.80, 0.11), SAND)
    if blanket:
        p.cyl((BUNK_X - 0.36, 0.02, z + 0.21), 0.09, 0.95, 'Y', seg=8, mat=OCHRE)
    p.bevel(pad + pillow, width=0.035, segments=1)
    return p.finish(name, coll, origin=(0, 0, z))


def bunk_ladder(coll, mats):
    p = Part(mats)
    x = BUNK_X + 0.055
    for y in (-0.26, 0.26):
        p.box((x, y, 1.20), (0.04, 0.05, 2.40), DARK)
    for i in range(5):
        p.box((x, 0, 0.35 + i * 0.40), (0.04, 0.54, 0.04), STEEL)
    return p.finish("Mesh_Bunk_Double_Ladder", coll, origin=(x, 0, 0))


def build_bunk_double(coll, mats):
    frame = bunk_frame(coll, mats)
    attach([bunk_mattress(BUNK_DECKS[0], "Mesh_Bunk_Double_MattressLower", coll, mats, False),
            bunk_mattress(BUNK_DECKS[1], "Mesh_Bunk_Double_MattressUpper", coll, mats, True),
            bunk_ladder(coll, mats)], frame)


# ---------------------------------------------------------------------------
# Bunk_Hammock — posts 3.8 m apart along X
# ---------------------------------------------------------------------------

HAM_POST = 1.90
HAM_END = 1.32          # spreader bars
HAM_RING_Z = 2.00


def hammock_posts(coll, mats):
    p = Part(mats)
    for s in (-1, 1):
        p.box((s * HAM_POST, 0, 1.30), (0.14, 0.14, 2.60), STEEL)
        p.box((s * HAM_POST, 0, 0.03), (0.30, 0.30, 0.06), DARK)
        p.box((s * (HAM_POST - 0.09), 0, HAM_RING_Z), (0.06, 0.10, 0.10), DARK)
    return p.finish("Mesh_Bunk_Hammock_Posts", coll)


def hammock_sag(x):
    t = (x / HAM_END) ** 2
    return 1.12 + (1.58 - 1.12) * t


def hammock_canvas(coll, mats):
    p = Part(mats)
    rows = []
    for i in range(9):
        x = -HAM_END + 2 * HAM_END * i / 8.0
        t = (x / HAM_END) ** 2
        half = 0.44 + 0.08 * (1 - t)
        dip = 0.16 * (1 - t)
        row = []
        for j in range(5):
            v = -1 + 2 * j / 4.0
            row.append((x, half * v, hammock_sag(x) - dip * (1 - v * v)))
        rows.append(row)
    p.sheet(rows, 0.02, CANVAS)
    for s in (-1, 1):
        p.box((s * (HAM_END + 0.01), 0, hammock_sag(HAM_END) + 0.02), (0.06, 0.98, 0.05), WOOD)
    return p.finish("Mesh_Bunk_Hammock_Canvas", coll, origin=(0, 0, hammock_sag(0) - 0.16))


def hammock_ropes(coll, mats):
    p = Part(mats)
    for s in (-1, 1):
        ring = (s * (HAM_POST - 0.13), 0, HAM_RING_Z)
        z = hammock_sag(HAM_END) + 0.02
        for y in (-0.46, 0.0, 0.46):
            rope(p, (s * (HAM_END + 0.02), y, z), ring, 0.012, ROPE)
        p.box(ring, (0.05, 0.06, 0.06), DARK)
    return p.finish("Mesh_Bunk_Hammock_Ropes", coll)


def build_bunk_hammock(coll, mats):
    posts = hammock_posts(coll, mats)
    attach([hammock_canvas(coll, mats), hammock_ropes(coll, mats)], posts)


# ---------------------------------------------------------------------------
# Table_Mess — 3.2 x 1.2 x 1.3, bolted down on two pedestals
# ---------------------------------------------------------------------------

def build_table_mess(coll, mats):
    p = Part(mats)
    top = p.slab((-1.60, -0.60, 1.24), (1.60, 0.60, 1.30), HULL)
    for x in (-1.0, 1.0):
        p.slab((x - 0.09, -0.25, 0.05), (x + 0.09, 0.25, 1.20), STEEL)
        p.slab((x - 0.20, -0.42, 0.0), (x + 0.20, 0.42, 0.06), DARK)
        p.slab((x - 0.15, -0.45, 1.19), (x + 0.15, 0.45, 1.245), DARK)
        for y in (-0.34, 0.34):
            p.box((x, y, 0.075), (0.06, 0.06, 0.04), DARK)
    p.slab((-1.0, -0.05, 0.28), (1.0, 0.05, 0.38), STEEL)          # foot-rail stretcher
    for y in (-0.55, 0.55):
        for x in (-1.2, 0.0, 1.2):
            p.box((x, y, 1.305), (0.035, 0.035, 0.016), DARK)       # rivet heads
    p.bevel(top, width=0.02, segments=1)
    return p.finish("Mesh_Table_Mess", coll)


# ---------------------------------------------------------------------------
# Locker_Tall — 1.4 x 0.8 x 3.2, doors on -Y, right door ajar
# ---------------------------------------------------------------------------

LK_W, LK_D, LK_H = 0.70, 0.40, 3.20
LK_DOOR_Y = -LK_D - 0.017       # door mid-plane, 2 mm proud of the carcass
LK_DOOR_Z = (0.14, 3.14)
LK_AJAR = math.radians(25)


def locker_carcass(coll, mats):
    p = Part(mats)
    p.slab((-LK_W, LK_D - 0.03, 0.10), (LK_W, LK_D, 3.19), DARK)             # back
    for s in (-1, 1):
        p.slab((s * (LK_W - 0.03), -LK_D, 0.10), (s * LK_W, LK_D - 0.02, 3.19), STEEL)
    p.slab((-LK_W + 0.02, -LK_D, 3.15), (LK_W - 0.02, LK_D - 0.02, 3.18), STEEL)
    p.slab((-LK_W + 0.02, -LK_D, 0.10), (LK_W - 0.02, LK_D - 0.02, 0.13), STEEL)
    p.slab((-0.01, -LK_D + 0.01, 0.12), (0.01, LK_D - 0.02, 3.16), STEEL)   # divider
    p.slab((0.0, -LK_D + 0.04, 2.48), (LK_W - 0.02, LK_D - 0.02, 2.51), STEEL)  # shelf
    p.slab((-LK_W - 0.02, -LK_D - 0.02, 3.17), (LK_W + 0.02, LK_D + 0.005, 3.20), DARK)
    p.slab((-LK_W + 0.04, -LK_D + 0.04, 0.0), (LK_W - 0.04, LK_D - 0.04, 0.105), DARK)
    for s in (-1, 1):                                                       # hinge knuckles
        for z in (0.55, 2.70):
            p.box((s * (LK_W + 0.005), LK_DOOR_Y, z), (0.03, 0.04, 0.16), DARK)
    return p.finish("Mesh_Locker_Tall_Carcass", coll)


def locker_door_left(coll, mats):
    """The closed door, with a kick dent pressed into its lower half."""
    p = Part(mats)
    xs = (-LK_W + 0.005, -0.47, -0.23, -0.005)
    zs = (LK_DOOR_Z[0], 0.62, 1.30, 2.20, LK_DOOR_Z[1])
    rows = []
    for i, z in enumerate(zs):
        row = []
        for j, x in enumerate(xs):
            dent = 0.03 if (i, j) == (1, 1) else (0.012 if (i, j) == (2, 1) else 0.0)
            row.append((x, LK_DOOR_Y + dent, z))
        rows.append(row)
    p.sheet(rows, 0.03, HULL, smooth=False)
    vent(p, -0.35, LK_DOOR_Y - 0.018, 2.80, 0.40, 0.26, 2)
    p.box((-0.07, LK_DOOR_Y - 0.032, 1.60), (0.035, 0.04, 0.22), DARK)       # handle
    return p.finish("Mesh_Locker_Tall_DoorLeft", coll,
                    origin=(-LK_W - 0.005, LK_DOOR_Y, LK_DOOR_Z[0]))


def locker_door_right(coll, mats):
    """The ajar door, modelled about its own hinge and swung 25 degrees open."""
    p = Part(mats)
    w = LK_W - 0.005
    p.slab((-w, -0.015, LK_DOOR_Z[0]), (-0.01, 0.015, LK_DOOR_Z[1]), HULL)
    vent(p, -0.35, -0.018, 2.80, 0.40, 0.26, 2)
    p.box((-w + 0.07, -0.032, 1.60), (0.035, 0.04, 0.22), DARK)
    hinge = Vector((LK_W + 0.005, LK_DOOR_Y, 0.0))
    bmesh.ops.transform(p.bm, matrix=Matrix.Translation(hinge) @ Matrix.Rotation(LK_AJAR, 4, 'Z'),
                        verts=p.bm.verts)
    return p.finish("Mesh_Locker_Tall_DoorRight", coll, origin=hinge)


def build_locker_tall(coll, mats):
    carcass = locker_carcass(coll, mats)
    attach([locker_door_left(coll, mats), locker_door_right(coll, mats)], carcass)


# ---------------------------------------------------------------------------
# Galley_Stove — 1.2 x 0.9 x 1.3 iron stove, flue 2.5 m, kettle on the hob
# ---------------------------------------------------------------------------

STOVE_TOP = 1.30


def stove_body(coll, mats):
    p = Part(mats)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * 0.46, sy * 0.32, 0.08), (0.10, 0.10, 0.16), DARK)
    p.slab((-0.55, -0.40, 0.15), (0.55, 0.40, 1.245), RUST)
    top = p.slab((-0.60, -0.45, 1.24), (0.60, 0.45, STOVE_TOP), DARK)
    p.slab((-0.40, -0.42, 0.55), (0.05, -0.39, 1.05), DEEP)               # firebox door
    p.box((-0.02, -0.44, 0.80), (0.05, 0.05, 0.10), BRASS)                 # latch
    p.slab((-0.40, -0.42, 0.22), (0.30, -0.39, 0.42), BLACK)               # ash drawer
    p.cyl((-0.25, -0.10, STOVE_TOP + 0.01), 0.20, 0.03, 'Z', seg=10, mat=STEEL)
    p.cyl((0.22, -0.18, STOVE_TOP + 0.01), 0.15, 0.03, 'Z', seg=10, mat=STEEL)
    p.bevel(top, width=0.02, segments=1)
    return p.finish("Mesh_Galley_Stove_Body", coll)


def stove_flue(coll, mats):
    p = Part(mats)
    x, y = 0.30, 0.24
    p.cyl((x, y, STOVE_TOP + 1.25), 0.10, 2.50, 'Z', seg=8, mat=DEEP, cap=False)
    p.cyl((x, y, STOVE_TOP + 0.05), 0.14, 0.12, 'Z', seg=8, mat=DARK)      # collar
    p.cyl((x, y, STOVE_TOP + 2.52), 0.20, 0.12, 'Z', seg=8, mat=DARK, radius_top=0.06)
    p.box((x + 0.14, y, STOVE_TOP + 0.60), (0.18, 0.03, 0.03), BRASS)     # damper key
    return p.finish("Mesh_Galley_Stove_Flue", coll, origin=(x, y, STOVE_TOP))


def stove_kettle(coll, mats):
    p = Part(mats)
    x, y, z = -0.25, -0.10, STOVE_TOP + 0.02
    p.cyl((x, y, z + 0.11), 0.16, 0.22, 'Z', seg=10, mat=COPPER, radius_top=0.10)
    p.box((x, y, z + 0.24), (0.05, 0.05, 0.05), BRASS)                     # lid knob
    rot, length = along((x - 0.12, y, z + 0.08), (x - 0.30, y, z + 0.24))
    p.cyl((x - 0.21, y, z + 0.16), 0.03, length, 'Z', seg=6, mat=COPPER, rot=rot,
          radius_top=0.018)
    p.box((x, y, z + 0.36), (0.22, 0.03, 0.03), DARK)                       # bail handle
    for s in (-1, 1):
        p.box((x + s * 0.10, y, z + 0.29), (0.03, 0.03, 0.14), DARK)
    return p.finish("Mesh_Galley_Stove_Kettle", coll, origin=(x, y, z))


def build_galley_stove(coll, mats):
    body = stove_body(coll, mats)
    attach([stove_flue(coll, mats), stove_kettle(coll, mats)], body)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_bunk_double(collection("Coll_Bunk_Double", root), mats)
    build_bunk_hammock(collection("Coll_Bunk_Hammock", root), mats)
    build_table_mess(collection("Coll_Table_Mess", root), mats)
    build_locker_tall(collection("Coll_Locker_Tall", root), mats)
    build_galley_stove(collection("Coll_Galley_Stove", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
