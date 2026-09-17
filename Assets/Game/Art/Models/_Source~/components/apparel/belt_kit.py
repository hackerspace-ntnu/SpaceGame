"""Belts and what hangs off them.

    blender --background --python components/apparel/belt_kit.py -- --out components/apparel/belt_kit.blend

Built in place round the Slim mannequin's hips (see `_fit.py`); origin at the hip bone.
Binds to `Hips`.

    Coll_Belt_RadioPack   the sky soldier's: belt, a boxy radio pack on the right hip with
                          a stub antenna, a pouch on the left
    Coll_Belt_Pouches     belt and three pouches round the front

Generation script -- historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from mathutils import Matrix, Vector  # noqa: E402

import _buildlib as B  # noqa: E402
import _fit as F  # noqa: E402

MATS = ["Mat_Neutral_Slate_Dark", "Mat_Neutral_Black_Matte", "Mat_Metal_Steel_Dark"]
STRAP, KIT, METAL = range(3)
BELT_RX, BELT_RY = 0.235, 0.16
BELT_H = 0.065


def belt(coll, mats, origin, centre, name):
    p = B.Part(mats)
    p.sheet([F.arc(centre + Vector((0, 0, dz)), BELT_RX, BELT_RY, 0.0, 360.0 * 31 / 32, 32) for dz in (-BELT_H / 2, BELT_H / 2)],
            0.02, STRAP, closed=True)
    F.bind(p.finish(name, coll, origin=origin), "Hips")
    p = B.Part(mats)
    front = F.ellipse(centre, BELT_RX, BELT_RY, 270.0)
    p.box((front.x, front.y - 0.012, front.z), (0.07, 0.012, 0.05), METAL)
    F.bind(p.finish(name + "Buckle", coll, origin=origin), "Hips")


def box_at(coll, mats, origin, centre, angle, size, name, mat=KIT, drop=0.0):
    """A box hung on the belt's outside at `angle`, turned to face out."""
    at = F.ellipse(centre, BELT_RX, BELT_RY, angle)
    out_dir = Vector((math.cos(math.radians(angle)), math.sin(math.radians(angle)), 0.0))
    at = at + out_dir * (0.01 + size[1] / 2) + Vector((0, 0, -drop))
    p = B.Part(mats)
    p.box(at, size, mat, rot=Matrix.Rotation(math.radians(angle - 270.0), 4, 'Z'))
    p.bevel(width=0.006, segments=1)
    F.bind(p.finish(name, coll, origin=origin), "Hips")
    return at


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)
    j = F.body_joints()
    origin = j["Hips"][0]
    centre = Vector((0.0, origin.y + 0.005, origin.z + 0.09))

    radio = B.collection("Coll_Belt_RadioPack")
    belt(radio, mats, origin, centre, "Mesh_Belt_RadioPack_Belt")
    pack = box_at(radio, mats, origin, centre, 160.0, (0.13, 0.12, 0.2), "Mesh_Belt_RadioPack_Radio", drop=0.06)
    p = B.Part(mats)
    p.cyl((pack.x, pack.y, pack.z + 0.2), 0.006, 0.22, 'Z', 6, METAL)
    F.bind(p.finish("Mesh_Belt_RadioPack_Antenna", radio, origin=origin), "Hips")
    box_at(radio, mats, origin, centre, 330.0, (0.09, 0.06, 0.1), "Mesh_Belt_RadioPack_Pouch", STRAP, drop=0.04)

    pouches = B.collection("Coll_Belt_Pouches")
    belt(pouches, mats, origin, centre, "Mesh_Belt_Pouches_Belt")
    for k, angle in enumerate((225.0, 250.0, 330.0)):
        box_at(pouches, mats, origin, centre, angle, (0.08, 0.055, 0.1), "Mesh_Belt_Pouches_Pouch%d" % (k + 1), STRAP, drop=0.04)

    B.save(out)
    B.report()


main()
