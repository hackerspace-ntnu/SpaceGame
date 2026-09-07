"""Turn the hand-built single jetpack pod into a mirrored pair seated on the
expedition rig's lash rail, and add a compact inventory-item variation.

Run against the existing file (it edits in place, additively):

    blender --background jetpack.blend --python jetpack_mirror.py -- --save

What it does, and nothing else:

* Leaves every hand-built mesh, modifier and material untouched. The pod is
  positioned and scaled through two new parent empties, so the BEVEL widths and
  the geometry-nodes exhausts keep their authored relationship to the geometry
  (baking the scale into the vertices would not).
* `MOUNT_Jetpack_R` / `MOUNT_Jetpack_L` sit at half the lash rail's tip span
  measured from `components/props/expedition_rig.blend` (`Mesh_Rig_LashRail`,
  x = +/-0.8925), so the pods stand 0.8925 m apart, centre to centre. Half,
  because the pack is worn at 2x its modelled size.
* The left pod is the same object set again, linked to the same mesh data, under
  an empty whose X scale is negative. Editing one side edits both.
* `Coll_Jetpack_Item` holds a third copy of the pair, pushed together shoulder to
  shoulder for the inventory / hand-held item model.
"""

import math
import sys

import bpy
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
SAVE = "--save" in argv

# The lash rail (components/props/expedition_rig.blend, Mesh_Rig_LashRail) spans
# x -0.8925 .. 0.8925, but the pods do not sit on its tips: JetpackBuilder wears
# the pack at twice its modelled size, and at 2x a pod on each tip stands nearly
# four metres across the wearer and misses the rig entirely. Half the tip span
# puts the pair back on the pack (2026-09-07, asked for directly).
RAIL_TIP_X = 0.8925 / 2.0

# Worn pod height. The rig's back panel is 0.629 m tall and its wings 0.84 m
# long, so a 0.65 m pod reads as a pack-mounted unit rather than a vehicle.
POD_HEIGHT = 0.65

# The inventory model is the two motors side by side with a hand's width of air
# between them, not the rail spacing.
ITEM_GAP = 0.03

# The pod is authored with its struts along -Y. Yawing the right pod -90 deg
# swings them to -X, pointing inboard at the pack, so they run along the lash
# rail instead of sticking out fore-and-aft with nothing to grab.
#
# The left mount is the right one mirrored: mirror(T @ R(y) @ S(s)) is
# T' @ R(-y) @ S(-s, s, s), so its yaw carries the opposite sign. Same yaw on
# both empties would swing one pod's struts outboard.
MOUNT_YAW = math.radians(-90.0)

# Role names for the hand-built parts, by their authored object name.
ROLES = {
    "Cube": "Housing",
    "Cube.001": "StrutUpper",
    "Cube.002": "StrutLower",
    "Cube.003": "Fin",
    "Cube.004": "LampOuter",
    "Cube.005": "LampInner",
    "Cylinder": "Tank",
    "Cylinder.001": "NozzleOuterCollar",
    "Cylinder.002": "NozzleInnerCone",
    "Cylinder.003": "NozzleOuterCone",
    "Cylinder.004": "NozzleInnerCollar",
    "Cylinder.007": "NozzleYoke",
    "Plane": "ExhaustInner",
    "Plane.001": "ExhaustOuter",
    "Torus": "RingFront",
    "Torus.001": "RingRear",
    "Torus.002": "RingFrontTrim",
}


def authored_points(objs):
    """Every vertex in the pod's authored space.

    Deliberately `matrix_basis`, not `matrix_world`: once the parts hang under a
    mount their world pose carries the scale and the yaw, and measuring that
    would fold this run's placement into the next one's. `matrix_basis` is the
    hand-authored local transform and never changes, so re-running the script
    lands the pods in exactly the same place.
    """
    return [o.matrix_basis @ v.co for o in objs for v in o.data.vertices]


def bounds(points):
    lo = Vector((min(p.x for p in points), min(p.y for p in points),
                 min(p.z for p in points)))
    hi = Vector((max(p.x for p in points), max(p.y for p in points),
                 max(p.z for p in points)))
    return lo, hi


def world_bounds(objs):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    return bounds(pts)


def collection(name):
    coll = bpy.data.collections.get(name)
    if coll is None:
        coll = bpy.data.collections.new(name)
    if coll.name not in {c.name for c in bpy.context.scene.collection.children}:
        bpy.context.scene.collection.children.link(coll)
    return coll


def move_to(obj, coll):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)


def empty(name, x, mirrored, scale, coll):
    obj = bpy.data.objects.get(name)
    if obj is None:
        obj = bpy.data.objects.new(name, None)
    if obj.name not in coll.objects:
        move_to(obj, coll)
    obj.empty_display_type = 'PLAIN_AXES'
    obj.empty_display_size = 0.15
    obj.location = (x, 0.0, 0.0)
    obj.rotation_euler = (0.0, 0.0, -MOUNT_YAW if mirrored else MOUNT_YAW)
    obj.scale = (-scale if mirrored else scale, scale, scale)
    return obj


def seat(obj, mount, pivot, coll):
    """Parent obj under mount so its world transform becomes
    mount.matrix_world @ translate(-pivot) @ obj.matrix_world."""
    obj.parent = mount
    obj.matrix_parent_inverse = Matrix.Translation(-pivot)
    move_to(obj, coll)


def clone(src, suffix, mount, pivot, coll):
    """Linked duplicate: same mesh data, same local matrix, new mount."""
    name = src.name[:-2] + suffix     # strip the trailing "_R", never mid-name
    dup = bpy.data.objects.get(name)
    if dup is None:
        dup = src.copy()          # object copy, mesh data stays shared
        coll.objects.link(dup)
    dup.name = name
    dup.matrix_basis = src.matrix_basis.copy()
    seat(dup, mount, pivot, coll)
    return dup


# The hand-built parts on a fresh file, the right-hand pod on a re-run.
meshes = [o for o in bpy.data.objects if o.name in ROLES]
if not meshes:
    meshes = [o for o in bpy.data.objects
              if o.type == 'MESH' and o.name.startswith("Mesh_Jetpack_")
              and o.name.endswith("_R") and not o.name.endswith("_ItemR")]
if len(meshes) != len(ROLES):
    raise SystemExit("jetpack_mirror: expected %d pod parts, found %d"
                     % (len(ROLES), len(meshes)))

lo, hi = bounds(authored_points(meshes))
scale = POD_HEIGHT / (hi.z - lo.z)

# The pod hangs off the rail by its housing block: the rail line passes across
# the top of the housing, tank above it, nozzles below.
housing = next(o for o in meshes if o.name in ("Cube", "Mesh_Jetpack_Housing_R"))
h_lo, h_hi = bounds(authored_points([housing]))
pivot = Vector(((h_lo.x + h_hi.x) / 2.0, (h_lo.y + h_hi.y) / 2.0, h_hi.z))

worn = collection("Coll_Jetpack_Worn")
item = collection("Coll_Jetpack_Item")

mount_r = empty("MOUNT_Jetpack_R", RAIL_TIP_X, False, scale, worn)
mount_l = empty("MOUNT_Jetpack_L", -RAIL_TIP_X, True, scale, worn)

for src in list(meshes):
    role = ROLES.get(src.name)
    if role is not None:
        src.data.name = "Mesh_Jetpack_" + role
        src.name = "Mesh_Jetpack_" + role + "_R"
    seat(src, mount_r, pivot, worn)
    clone(src, "_L", mount_l, pivot, worn)

# Inventory item: the same two motors, shoulder to shoulder. Measure the inboard
# flank in the mount's own frame, AFTER the yaw — the struts swing into that gap,
# so the unrotated width would seat the two pods inside each other. The pod is
# not symmetric about its mount either (the nozzle yoke overhangs one side).
yaw = Matrix.Rotation(MOUNT_YAW, 3, 'Z')
inboard = -min((yaw @ (p - pivot)).x for p in authored_points(meshes)) * scale
item_x = inboard + ITEM_GAP / 2.0
item_r = empty("ITEM_Jetpack_R", item_x, False, scale, item)
item_l = empty("ITEM_Jetpack_L", -item_x, True, scale, item)

for src in [o for o in worn.objects if o.name.endswith("_R") and o.type == 'MESH']:
    clone(src, "_ItemR", item_r, pivot, item)
    clone(src, "_ItemL", item_l, pivot, item)

bpy.context.view_layer.update()
lo_w, hi_w = world_bounds([o for o in worn.objects if o.type == 'MESH'])
lo_i, hi_i = world_bounds([o for o in item.objects if o.type == 'MESH'])

# A yaw is the easiest thing in this file to get backwards, so measure it rather
# than trust it. The struts must end up between their mount and the pack centre.
strut_lo, strut_hi = world_bounds([bpy.data.objects["Mesh_Jetpack_StrutUpper_R"]])
strut_x = (strut_lo.x + strut_hi.x) / 2.0
if strut_x >= RAIL_TIP_X:
    raise SystemExit("jetpack_mirror: right strut sits outboard of its mount "
                     "(%.4f >= %.4f) — MOUNT_YAW has the wrong sign" %
                     (strut_x, RAIL_TIP_X))
print("[jetpack] strut centre x %.4f, mount x %.4f — struts point inboard"
      % (strut_x, RAIL_TIP_X))

item_r_lo, _ = world_bounds([o for o in item.objects
                             if o.type == 'MESH' and o.name.endswith("_ItemR")])
_, item_l_hi = world_bounds([o for o in item.objects
                             if o.type == 'MESH' and o.name.endswith("_ItemL")])
print("[jetpack] item gap   %.4f m (asked for %.4f)"
      % (item_r_lo.x - item_l_hi.x, ITEM_GAP))
print("[jetpack] pod scale %.5f  height %.3f m" % (scale, (hi.z - lo.z) * scale))
print("[jetpack] worn pair  %.3f x %.3f x %.3f  spacing %.4f m"
      % (hi_w.x - lo_w.x, hi_w.y - lo_w.y, hi_w.z - lo_w.z, RAIL_TIP_X * 2))
print("[jetpack] item pair  %.3f x %.3f x %.3f  spacing %.4f m"
      % (hi_i.x - lo_i.x, hi_i.y - lo_i.y, hi_i.z - lo_i.z, item_x * 2))

if SAVE:
    bpy.ops.wm.save_mainfile()
