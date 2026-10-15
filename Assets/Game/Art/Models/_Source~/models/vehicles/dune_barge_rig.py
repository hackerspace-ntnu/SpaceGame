"""Rig a dune-barge variant (Collection 2, 3 or 4 of dune_barge.blend): rigid parts bone-parented to one
armature per variant. Live-session edit (MCP); re-runnable — it rebuilds the variant's armature.

Found by what is in the variant, never by fixed names, because the variants are hand-built copies whose
objects carry Blender's .001 suffixes and the user moves, scales and turns them:

  Bone_Pod_L / _R            steering, for any Exterior_Track_Pod* group
  Bone_Wheel_<Unit>_<Kind>n  spin, every *_Wheel_* mesh in every Exterior_Track_* group
  Bone_Door_<n>              every bulkhead door leaf: a vertical hinge on the leaf's +x edge, measured in
                             the leaf's OWN mesh space and carried out by its transform, so a copy that was
                             moved, turned or scaled (the lookout-cabin door) is hinged correctly
  Bone_Hatch_R / _L          the side hatches' lids (shut at rest; see the dressing pass for their frame)
  Bone_Lid_<n>               any other hatch lid, e.g. the stern hatch: library frame, hinge on the lid's
                             own origin along its local X
  Bone_Gun_<n>_Yaw / _Pitch  every heavy gun: yaw at the base's mount plate, pitch on the cradle trunnion
"""
import math
import re

import bpy
from mathutils import Matrix, Vector

C, O = bpy.data.collections, bpy.data.objects
HATCH_Y, HATCH_Z, LID_HINGE_UP = 1.2, 2.91, 1.085 * 0.55      # the dressing pass's coaming frame
WHEEL_BONE, HINGE_BONE = 0.3, 0.6


def base(n):
    return re.sub(r"\.\d{3}$", "", n)


def world_bbox(objs):
    ps = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    return Vector([min(p[i] for p in ps) for i in range(3)]), Vector([max(p[i] for p in ps) for i in range(3)])


def local_bbox(o):
    vs = [v.co for v in o.data.vertices]
    return Vector([min(v[i] for v in vs) for i in range(3)]), Vector([max(v[i] for v in vs) for i in range(3)])


def rig_variant(coll_name, exterior_name, arm_name):
    coll = C[coll_name]
    objs = [o for o in coll.all_objects if o.type == 'MESH']
    old = O.get(arm_name)
    if old is not None:
        for ch in list(old.children):
            w = ch.matrix_world.copy(); ch.parent = None; ch.matrix_world = w
        bpy.data.objects.remove(old)
    arm = bpy.data.objects.new(arm_name, bpy.data.armatures.new(arm_name))
    coll.objects.link(arm)
    bones, parents_of = [], {}           # (name, head, tail, parent), object -> bone

    def bone(name, head, tail, parent):
        bones.append((name, Vector(head), Vector(tail), parent))
        return name

    bone("Bone_Chassis", (0, 0, 0.5), (0, 1.0, 0.5), None)

    # tracks: a steering bone per pod, a spin bone per wheel
    for unit_coll in C[exterior_name].children:
        if not unit_coll.name.startswith("Exterior_Track_"):
            continue
        unit = re.match(r"Exterior_Track_(\w+?)(_.*)?$", unit_coll.name).group(1)
        parts = [o for o in unit_coll.all_objects if o.type == 'MESH']
        if not parts:
            continue
        parent = "Bone_Chassis"
        if unit.startswith("Pod"):
            lo, hi = world_bbox(parts); c = (lo + hi) / 2
            parent = bone("Bone_Pod_" + unit[-1], (c.x, c.y, lo.z), (c.x, c.y, lo.z + 1.0), "Bone_Chassis")
        counts = {}
        for o in sorted((o for o in parts if "_Wheel_" in base(o.name)), key=lambda o: world_bbox([o])[0].y):
            kind = base(o.name).split("_Wheel_")[1].split("_")[0]
            counts[kind] = counts.get(kind, 0) + 1
            lo, hi = world_bbox([o]); c = (lo + hi) / 2
            parents_of[o] = bone("Bone_Wheel_%s_%s%d" % (unit, kind, counts[kind]), c, c + Vector((WHEEL_BONE, 0, 0)), parent)
        for o in parts:
            if o not in parents_of and o.parent is None:
                parents_of[o] = parent

    # doors: vertical hinge on the +x edge of the leaf, in the leaf's own mesh space
    leaves = sorted((o for o in objs if base(o.name).startswith("Mesh_Door_Bulkhead_Leaf")),
                    key=lambda o: tuple(round(v, 2) for v in o.matrix_world.translation) + (o.name,))
    for i, leaf in enumerate(leaves):
        lo, hi = local_bbox(leaf)
        W = leaf.matrix_world
        h = W @ Vector((hi.x, (lo.y + hi.y) / 2, lo.z))
        axis = (W.to_3x3() @ Vector((0, 0, 1))).normalized()
        parents_of[leaf] = bone("Bone_Door_%d" % (i + 1), h, h + axis * HINGE_BONE, "Bone_Chassis")

    # hatch lids: the side hatches in the dressing frame, anything else in the library's own frame
    stern_n = 0
    for lid in sorted((o for o in objs if base(o.name).startswith("Mesh_Hatch_Roof_Lid")), key=lambda o: o.name):
        W = lid.matrix_world
        if base(lid.name) == "Mesh_Hatch_Roof_Lid_HatchR":
            coaming = min((o for o in objs if base(o.name) == "Mesh_Hatch_Roof_Coaming_HatchR"),
                          key=lambda o: (o.matrix_world.translation - W.translation).length)
            clo, chi = local_bbox(coaming)
            h = W @ Vector((chi.x, HATCH_Y, HATCH_Z + LID_HINGE_UP))
            axis = (W.to_3x3() @ Vector((0, 1, 0))).normalized()
            name = "Bone_Hatch_" + ("R" if h.x > 0 else "L")
        else:
            h = W.translation.copy()
            axis = (W.to_3x3() @ Vector((1, 0, 0))).normalized()
            stern_n += 1
            name = "Bone_Lid_%d" % stern_n
        parents_of[lid] = bone(name, h, h + axis * HINGE_BONE, "Bone_Chassis")

    # guns: each base with the cradle nearest it
    gun_n = 0
    for b in (o for o in objs if re.match(r"Mesh_HeavyGun_\w+_Base", base(o.name))):
        cradles = [o for o in objs if re.match(r"Mesh_HeavyGun_\w+_Cradle", base(o.name))]
        if not cradles:
            continue
        cr = min(cradles, key=lambda o: (o.matrix_world.translation - b.matrix_world.translation).length)
        gun_n += 1
        p, t = b.matrix_world.translation, cr.matrix_world.translation
        xa = (cr.matrix_world.to_3x3() @ Vector((1, 0, 0))).normalized()
        yaw = bone("Bone_Gun_%d_Yaw" % gun_n, p, p + Vector((0, 0, HINGE_BONE)), "Bone_Chassis")
        parents_of[b] = yaw
        parents_of[cr] = bone("Bone_Gun_%d_Pitch" % gun_n, t, t + xa * WHEEL_BONE, yaw)

    for o in objs:
        if base(o.name).startswith("Mesh_HullBelly"):
            parents_of[o] = "Bone_Chassis"

    bpy.context.view_layer.objects.active = arm
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    arm.select_set(True)
    with bpy.context.temp_override(active_object=arm, object=arm, selected_objects=[arm]):
        bpy.ops.object.mode_set(mode='EDIT')
        eb = arm.data.edit_bones
        for name, head, tail, parent in bones:
            e = eb.new(name); e.head = head; e.tail = tail
            if parent:
                e.parent = eb[parent]; e.use_connect = False
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()
    for o, bname in parents_of.items():
        w = o.matrix_world.copy()
        o.parent = arm; o.parent_type = 'BONE'; o.parent_bone = bname
        b = arm.data.bones[bname]
        o.matrix_parent_inverse = (arm.matrix_world @ b.matrix_local @ Matrix.Translation((0, b.length, 0))).inverted()
        o.matrix_world = w
    return arm


VARIANTS = (("Collection 2", "Exterior", "Arm_DuneBarge2"),
            ("Collection 3", "Exterior_Small", "Arm_DuneBarge3"),
            ("Collection 4", "Exterior_C4", "Arm_DuneBarge4"))

if __name__ in ("__main__", "rig"):
    for coll_name, exterior, arm_name in VARIANTS:
        arm = rig_variant(coll_name, exterior, arm_name)
        names = [b.name for b in arm.data.bones]
        print(coll_name, arm_name, len(names), "bones:", [n for n in names if not n.startswith("Bone_Wheel")],
              "+", sum(n.startswith("Bone_Wheel") for n in names), "wheels")
    bpy.ops.ed.undo_push(message="Rig all dune barge variants")
