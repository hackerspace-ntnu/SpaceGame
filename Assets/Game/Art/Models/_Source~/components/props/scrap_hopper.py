"""components/props/scrap_hopper — the intake that says "feed me".

`RepairWorkstation` is repaired by handing it ship scrap one piece at a time,
and the only thing in the old prefab that said so was a text label. This is the
signifier (GDC-L1-UX-0004): an obviously open-mouthed receptacle with a hazard
rim and a lid you can see would clunk. Three variations so the same mechanic can
sit on a bench, stand on a floor, or hang on a bulkhead without repeating:

| Collection | Silhouette | Where |
|---|---|---|
| `Coll_ScrapHopper_Chute` | square funnel on a base plate, flap lid | on a worktop |
| `Coll_ScrapHopper_Drum`  | banded drum with a domed lid            | free-standing |
| `Coll_ScrapHopper_Slot`  | letterbox plate with a spring flap      | on a wall |

Every variation is TWO objects: the body and its lid, because the lid is what a
game nudges (`RepairWorkstation.clunkTarget`) and a nudge has to move a part,
not the whole machine. Lid origins sit ON THE HINGE LINE, so a rotation about
the lid's local X opens it. Chute and Drum stand on z = 0 at the centre of
their footprint; the Slot's origin is on its mounting face (y = 0, the wall),
the plate hanging toward -Y.

No armature: the lids are single rigid parts whose pivots are carried by their
origins, which is all a bone would add, and `_exportlib` drops rigs anyway
because Unity drives these transforms directly.

    blender --background --python scrap_hopper.py -- --out scrap_hopper.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)

from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from _tracked import TrackedPart  # noqa: E402

# MATS[0] is a structural metal on purpose: bevel faces land on index 0.
STEEL, DARK, RUBBER, CHROME, CREAM, GREY, ORANGE, WHITE, BLACK, GLASS = range(10)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Cream_Aged", "Mat_Neutral_Panel_Grey",
        "Mat_Paint_Safety_Orange", "Mat_Paint_White_Arctic",
        "Mat_Neutral_Black_Matte", "Mat_Glass_Canopy_Tinted"]

BEVEL = 0.003


def hazard_stripe(p, centre, width, height, y, depth=0.003, count=7):
    """Alternating orange/black blocks on a -Y face, embedded 1 mm so nothing
    is coplanar with the face behind it."""
    x0, z0 = centre
    w = width / count
    for i in range(count):
        p.box((x0 - width / 2 + w * (i + 0.5), y + depth / 2 - 0.001, z0),
              (w, depth, height), ORANGE if i % 2 == 0 else BLACK)


def chute(coll, mats):
    """Square funnel over a dark throat, on a bolted base plate, flap lid at
    the back. The worktop unit."""
    p = TrackedPart(mats)
    hard = []
    hard += p.slab((-0.15, -0.15, 0.0), (0.15, 0.15, 0.02), DARK)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.cyl((sx * 0.125, sy * 0.125, 0.023), 0.008, 0.008, 'Z', 8, STEEL)
    hazard_stripe(p, (0.0, 0.010), 0.24, 0.012, y=-0.15)
    # Throat rising off the plate, then the funnel opening out over it.
    hard += p.slab((-0.08, -0.08, 0.02), (0.08, 0.08, 0.16), BLACK)
    lo = [(-0.08, -0.08), (0.08, -0.08), (0.08, 0.08), (-0.08, 0.08)]
    hi = [(-0.15, -0.13), (0.15, -0.13), (0.15, 0.13), (-0.15, 0.13)]
    funnel = p.loft([(0.155, lo), (0.31, hi)], axis='Z', mat=ORANGE)
    p.shade(funnel, smooth=False)      # four flat walls, not a smoothed cone
    hard += funnel
    # Dark well proud of the funnel's top, and a steel rim frame around it.
    p.box((0.0, 0.0, 0.316), (0.26, 0.22, 0.016), BLACK)
    for sy in (-1, 1):
        hard += p.box((0.0, sy * 0.145, 0.322), (0.33, 0.03, 0.03), STEEL)
    for sx in (-1, 1):
        hard += p.box((sx * 0.15, 0.0, 0.322), (0.03, 0.26, 0.03), STEEL)
    # Hinge mounts at the back, either side of where the lid's barrel lies.
    for sx in (-1, 1):
        hard += p.box((sx * 0.12, 0.158, 0.322), (0.03, 0.03, 0.05), STEEL)
    p.restamp()
    p.bevel(hard, width=BEVEL, segments=2)
    p.finish("Mesh_ScrapHopper_Chute", coll)

    lid = TrackedPart(mats)
    lhard = []
    lhard += lid.slab((-0.165, -0.145, 0.339), (0.165, 0.145, 0.351), WHITE)
    lhard += lid.box((0.0, 0.0, 0.3545), (0.30, 0.02, 0.009), DARK)      # stiffener
    lid.cyl((0.0, 0.158, 0.346), 0.012, 0.20, 'X', 10, STEEL)           # barrel
    for sx in (-1, 1):
        lhard += lid.box((sx * 0.10, -0.115, 0.364), (0.012, 0.012, 0.026), DARK)
    lid.cyl((0.0, -0.115, 0.377), 0.010, 0.22, 'X', 10, ORANGE)         # handle
    lid.restamp()
    lid.bevel(lhard, width=BEVEL, segments=2)
    lid.finish("Mesh_ScrapHopper_ChuteLid", coll, origin=(0.0, 0.158, 0.346))


def drum(coll, mats):
    """Banded drum with a sight strip and a domed lid. The floor-standing one."""
    p = TrackedPart(mats)
    hard = []
    p.cyl((0.0, 0.0, 0.012), 0.17, 0.024, 'Z', 24, DARK)
    p.cyl((0.0, 0.0, 0.20), 0.155, 0.36, 'Z', 24, GREY)
    for z in (0.10, 0.30):
        p.torus((0.0, 0.0, z), 0.157, 0.008, 'Z', 24, 8, ORANGE)
    hard += p.box((0.0, -0.150, 0.20), (0.05, 0.02, 0.16), GLASS)       # sight strip
    hard += p.box((0.0, -0.152, 0.335), (0.10, 0.006, 0.04), CREAM)     # stencil plate
    p.cyl((0.0, 0.0, 0.385), 0.16, 0.014, 'Z', 24, STEEL)               # top collar
    p.cyl((0.0, 0.0, 0.393), 0.13, 0.004, 'Z', 24, BLACK)               # the mouth
    hard += p.box((0.0, 0.165, 0.37), (0.06, 0.03, 0.04), STEEL)        # hinge mount
    p.restamp()
    p.bevel(hard, width=BEVEL, segments=2)
    p.finish("Mesh_ScrapHopper_Drum", coll)

    lid = TrackedPart(mats)
    lid.cyl((0.0, 0.0, 0.400), 0.165, 0.012, 'Z', 24, WHITE)            # disc
    lid.cyl((0.0, 0.0, 0.420), 0.12, 0.03, 'Z', 24, WHITE, radius_top=0.06)
    lid.cyl((0.0, -0.06, 0.441), 0.008, 0.10, 'X', 8, ORANGE)           # handle
    lid.cyl((0.0, 0.170, 0.398), 0.010, 0.05, 'X', 8, STEEL)            # barrel
    lid.finish("Mesh_ScrapHopper_DrumLid", coll, origin=(0.0, 0.170, 0.398))


def slot(coll, mats):
    """Letterbox plate on a wall with a hanging spring flap over the slot.
    Origin on the wall plane (y = 0), plate toward -Y."""
    p = TrackedPart(mats)
    hard = []
    hard += p.slab((-0.18, -0.03, -0.11), (0.18, 0.0, 0.11), GREY)
    hard += p.slab((-0.13, -0.034, -0.026), (0.13, -0.028, 0.026), BLACK)   # recess
    hazard_stripe(p, (0.0, -0.07), 0.28, 0.014, y=-0.03)
    for sx in (-1, 1):
        for sz in (-1, 1):
            p.cyl((sx * 0.16, -0.033, sz * 0.09), 0.007, 0.008, 'Y', 8, STEEL)
    hard += p.box((0.0, -0.036, 0.034), (0.28, 0.014, 0.012), STEEL)        # hinge rail
    p.restamp()
    p.bevel(hard, width=BEVEL, segments=2)
    p.finish("Mesh_ScrapHopper_Slot", coll)

    flap = TrackedPart(mats)
    fhard = []
    fhard += flap.slab((-0.125, -0.041, -0.030), (0.125, -0.035, 0.025), WHITE)
    flap.cyl((0.0, -0.038, 0.032), 0.006, 0.24, 'X', 8, STEEL)             # barrel
    flap.restamp()
    flap.bevel(fhard, width=0.002, segments=2)
    flap.finish("Mesh_ScrapHopper_SlotFlap", coll, origin=(0.0, -0.038, 0.032))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    chute(collection("Coll_ScrapHopper_Chute"), mats)
    drum(collection("Coll_ScrapHopper_Drum"), mats)
    slot(collection("Coll_ScrapHopper_Slot"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
