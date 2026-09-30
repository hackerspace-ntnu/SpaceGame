"""Add luggage to desert_monowheel_double.blend -- run INSIDE the open file (Blender MCP):

    exec(open(r"<this file>").read())

Historical record, like every generator in the library: the .blend is the source of
truth, and this refuses to run twice (it stops if the cargo bundle already exists).
The luggage itself is built by `monowheel_luggage.py`, shared with the Runner Hauler;
this file is the Double's configuration of it.

- **Ski** -- the rug-wrapped bundle and bedroll, plus one trade-goods sack either side.
- **Sides** -- a saddlebag on each outer side hoop (x = +-1.8 m, clear of the paddle
  sweep at |x| <= 1.51 m): canvas + bedroll on the left, leather + canisters on the right.

Everything was parented to Bone_Chassis. The side luggage has since moved onto the
wheel pods (`Bone_PodL`/`R`) with the camber work -- see desert_monowheel_double_BUILD.md.
"""

import os
import sys

import bpy
from mathutils import Matrix

HERE = os.path.dirname(os.path.abspath(bpy.data.filepath))
for p in (os.path.abspath(os.path.join(HERE, "..", "..")), HERE):
    if p not in sys.path:
        sys.path.insert(0, p)
import monowheel_luggage as L

TAG = "Double"
O = bpy.data.objects
if "Mesh_CargoBundle_%s" % TAG in O:
    raise RuntimeError("Luggage already added -- edit it in place instead of re-running.")

coll = bpy.data.collections["Coll_Monowheel_%s" % TAG]
arm = O["Arm_Monowheel_%s" % TAG]
new = L.add({
    "tag": TAG, "coll": coll,
    "deck": "Mesh_SkiDeck_%s" % TAG, "deck_fit": (-3.0, -1.6), "bundle_y": -2.5,
    "sacks": [(0.84, -2.45, "Mesh_TradeGoods_SackPile_Sack01"),
              (-0.84, -2.33, "Mesh_TradeGoods_SackPile_Sack04")],
    "hoops": {1: "Mesh_SideHoopOuterL_%s" % TAG, -1: "Mesh_SideHoopOuterR_%s" % TAG},
    "bag_y": 0.05, "bag_size": (0.30, 0.72, 0.58),
    "kits": {1: "bedroll", -1: "canisters"}, "canister_y": (0.72, 1.02),
})
bone = arm.data.bones["Bone_Chassis"]
rest = arm.matrix_world @ bone.matrix_local @ Matrix.Translation((0, bone.length, 0))
for o in new:
    w = o.matrix_world.copy()
    o.parent, o.parent_type, o.parent_bone = arm, 'BONE', "Bone_Chassis"
    o.matrix_parent_inverse = rest.inverted()
    o.matrix_world = w
print("luggage:", sorted(o.name for o in new))
