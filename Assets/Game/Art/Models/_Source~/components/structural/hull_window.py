"""components/structural/hull_window — windows bolted onto a vehicle hull.

A porthole and three versions of one rectangular slot window: plain, shuttered
and barred. The slot frame frames an opening of exactly 1.2 x 0.85 m, because
it is laid over the barge's side-wall cutouts; its frame bars are 0.12 m wide,
so the outer size is 1.44 x 1.09 m.

Every variation is a dressing piece that sits ON a hull face: its origin is at
the centre of its BACK face (the face touching the hull, y = 0) and it projects
toward -Y, so its outward normal is -Y. Glass is set back from the frame front
so it reads as recessed.

`_Window_Shuttered` has its shutter as a separate object with its origin on the
top hinge axis; the shutter is modelled propped 45 degrees open, so rotating it
+45 degrees about local X closes it.

    blender --background --python hull_window.py -- --out hull_window.blend

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
    "Mat_Metal_Steel_Dark",        # 0 DARK   frames
    "Mat_Metal_Steel_Worn",        # 1 STEEL  rivets, bars, hinges
    "Mat_Metal_HullRust_Orange",   # 2 HULL   shutter plate
    "Mat_Metal_Rust_Heavy",        # 3 RUST   dented sill, prop
    "Mat_Glass_Canopy_Tinted",     # 4 GLASS  glazing
]
DARK, STEEL, HULL, RUST, GLASS = range(5)

DEPTH = 0.08                 # frame thickness, back face at y = 0
OPEN_W, OPEN_H = 1.20, 0.85  # the slot opening, exact
BAR = 0.12                   # frame bar width
OUT_W, OUT_H = OPEN_W + 2 * BAR, OPEN_H + 2 * BAR


def attach(children, parent):
    """Parent rigidly without moving anything."""
    bpy.context.view_layer.update()
    inv = parent.matrix_world.inverted()
    for c in children:
        c.parent = parent
        c.matrix_parent_inverse = inv


def rivet(p, x, z):
    """A square-headed rivet standing proud of the frame front."""
    p.box((x, -DEPTH - 0.005, z), (0.035, 0.02, 0.035), STEEL)


def slot_frame(p, rivets, glass_y=-0.05, dented_sill=False):
    """Four frame bars round a 1.2 x 0.85 opening, glass recessed behind.

    The side bars run 5 mm into the top and bottom bars so no two faces meet
    flush; the opening edges stay exactly at +-0.6 / +-0.425.
    """
    ow, oh = OPEN_W / 2.0, OPEN_H / 2.0
    p.slab((-OUT_W / 2, -DEPTH, oh), (OUT_W / 2, 0, OUT_H / 2), DARK)
    for s in (-1, 1):
        p.slab((s * ow, -DEPTH, -oh - 0.005), (s * OUT_W / 2, 0, oh + 0.005), DARK)
    if dented_sill:
        # The sill is lofted along X so its front face can be pushed in where
        # something struck it, without touching the opening edge above.
        def section(front, drop):
            return [(0.0, -OUT_H / 2), (0.0, -oh), (front, -oh),
                    (front, -OUT_H / 2 + drop)]
        p.loft([(-OUT_W / 2, section(-DEPTH, 0.0)),
                (-0.30, section(-DEPTH, 0.0)),
                (-0.12, section(-DEPTH + 0.03, 0.02)),
                (0.10, section(-DEPTH, 0.0)),
                (OUT_W / 2, section(-DEPTH, 0.0))], axis='X', mat=RUST)
    else:
        p.slab((-OUT_W / 2, -DEPTH, -OUT_H / 2), (OUT_W / 2, 0, -oh), DARK)
    p.slab((-ow - 0.01, glass_y, -oh - 0.01), (ow + 0.01, glass_y + 0.01, oh + 0.01), GLASS)
    zt, zb = (oh + OUT_H / 2) / 2.0, -(oh + OUT_H / 2) / 2.0
    for x, z in rivets:
        rivet(p, x, zt if z > 0 else zb)


def build_porthole(coll, mats):
    """0.9 m round port: a thick bolted ring with tinted glass 3 cm back."""
    p = Part(mats)
    ring = p.tube((0, -DEPTH / 2, 0), 0.45, 0.14, DEPTH, 'Y', seg=12, mat=DARK)
    p.shade(ring)
    p.cyl((0, -DEPTH + 0.035, 0), 0.32, 0.01, 'Y', seg=12, mat=GLASS)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        p.box((0.38 * math.cos(a), -DEPTH - 0.005, 0.38 * math.sin(a)),
              (0.04, 0.02, 0.04), STEEL, rot=Matrix.Rotation(a, 4, 'Y'))
    return p.finish("Mesh_Window_Porthole", coll)


def build_slot(coll, mats):
    p = Part(mats)
    xs = (-0.54, -0.18, 0.18, 0.54)
    slot_frame(p, [(x, 1) for x in xs] + [(x, -1) for x in xs])
    return p.finish("Mesh_Window_Slot", coll)


def build_shuttered(coll, mats):
    p = Part(mats)
    slot_frame(p, [(-0.40, 1), (0.40, 1), (-0.40, -1), (0.40, -1)])
    hinge = Vector((0, -DEPTH - 0.03, OUT_H / 2 - 0.02))
    p.box(hinge, (OUT_W - 0.1, 0.05, 0.05), STEEL)                  # hinge barrel
    frame = p.finish("Mesh_Window_Shuttered_Frame", coll)

    # Shutter built closed about its hinge (hanging down, clear of the frame
    # front by 1 cm), then swung 45 degrees out toward -Y.
    s = Part(mats)
    s.slab((-OUT_W / 2 + 0.03, -0.02, -OUT_H + 0.02), (OUT_W / 2 - 0.03, 0.02, -0.02), HULL)
    s.box((0, -0.03, -OUT_H / 2), (OUT_W - 0.2, 0.03, 0.08), DARK)    # stiffener
    s.box((0, -0.035, -OUT_H + 0.08), (0.20, 0.03, 0.04), STEEL)       # pull handle
    swing = Matrix.Translation(hinge) @ Matrix.Rotation(math.radians(-45), 4, 'X')
    bmesh.ops.transform(s.bm, matrix=swing, verts=s.bm.verts)
    # Prop stay from the shutter's lower corner back to the frame's side bar.
    foot = swing @ Vector((OUT_W / 2 - 0.10, -0.02, -OUT_H + 0.06))
    rest = Vector((OPEN_W / 2 + BAR / 2, -DEPTH - 0.01, -0.25))
    d = rest - foot
    s.box((foot + rest) / 2.0, (0.03, 0.03, d.length), RUST,
          rot=Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4())
    attach([s.finish("Mesh_Window_Shuttered_Shutter", coll, origin=hinge)], frame)


def build_barred(coll, mats):
    p = Part(mats)
    slot_frame(p, [(-0.54, 1), (-0.18, 1), (0.18, 1), (0.54, 1)],
               glass_y=-0.035, dented_sill=True)
    for x in (-0.30, 0.0, 0.30):
        p.cyl((x, -0.06, 0), 0.018, OPEN_H + 0.02, 'Z', seg=6, mat=STEEL)
    return p.finish("Mesh_Window_Barred", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_porthole(collection("Coll_Window_Porthole", root), mats)
    build_slot(collection("Coll_Window_Slot", root), mats)
    build_shuttered(collection("Coll_Window_Shuttered", root), mats)
    build_barred(collection("Coll_Window_Barred", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
