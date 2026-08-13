"""Closes the gaps in the crawler's tail chain, anchoring it back on the turret.

The tail was transformed by hand and came apart: 4.95 m between the turret and
the first segment, 3.12 m between segments 2 and 3, 0.37 m between 3 and 4.

**Only translations are applied.** Every segment keeps the direction and the
uniform scale it was given by hand — the chain is walked from the turret and
each segment is slid along until its start meets the previous one's end. That is
what makes the neck cheap to carry: no rotation changes, so the whole neck moves
rigidly by whatever the tail tip moved, and none of its internal geometry has to
be recomputed.

The mount point is the turret's own first-joint position — turret-local
(0, 0, 0.75), which is where `desert_crawler_tail.py` put J0 above the yaw ring
— pushed through the turret's current world matrix. So wherever the turret has
been moved to on the deck, the tail starts on it.

Bones are re-fitted from the moved meshes afterwards, and every bone-parented
mesh is re-attached at its intended world matrix, so nothing drifts.

    blender --background --python desert_crawler_reseat.py [-- --target <copy>]
"""

import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def target_path():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--target" in argv:
        return os.path.abspath(argv[argv.index("--target") + 1])
    return os.path.join(LIB, "models", "vehicles", "desert_crawler.blend")


TARGET = target_path()
SEG_NOMINAL = 3.00
N_SEG = 7
# Where the first tail joint sits in the turret's own space: the turret mesh was
# authored with its origin on the yaw axis and J0 0.75 above it.
TURRET_JOINT = Vector((0.0, 0.0, 0.75))
NECK_BONES = ["Neck_01", "Neck_02", "Neck_03", "Neck_04", "Neck_05",
              "Neck_Head", "Neck_Jaw"]


def bone_matrix(arm, name):
    b = arm.data.bones[name]
    return (arm.matrix_world @ b.matrix_local
            @ Matrix.Translation(Vector((0.0, b.length, 0.0))))


def attach(obj, arm, bone_name, world):
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = bone_matrix(arm, bone_name).inverted() @ world


def main():
    bpy.ops.wm.open_mainfile(filepath=TARGET)
    bpy.context.view_layer.update()

    arm = bpy.data.objects.get("CRAWLER_Rig")
    if arm is None:
        raise SystemExit("CRAWLER_Rig missing — refusing to touch this file.")

    before = {o.name: o.matrix_world.copy() for o in bpy.data.objects}
    bone_kids = {o.name: o.parent_bone for o in bpy.data.objects
                 if o.parent == arm and o.parent_type == 'BONE'}

    turret = bpy.data.objects["Mesh_Tail_Turret"]
    mount = turret.matrix_world @ TURRET_JOINT
    print("Opened %s — mount point on turret = (%.3f, %.3f, %.3f)"
          % (os.path.basename(TARGET), *mount))

    # ---- walk the chain, translation only -------------------------------
    target = dict(before)
    cursor = mount.copy()
    old_tip = None
    print("\n  gaps closed:")
    for i in range(1, N_SEG + 1):
        name = "Mesh_Tail_Seg%02d" % i
        m = before[name]
        scale = m.to_scale().x
        direction = (m.to_3x3() @ Vector((0.0, 1.0, 0.0))).normalized()
        length = SEG_NOMINAL * scale
        moved = (cursor - m.translation).length
        new_m = m.copy()
        new_m.translation = cursor
        target[name] = new_m
        print("    Seg%02d slid %5.3f m  (scale %.3f, length %.2f)"
              % (i, moved, scale, length))
        cursor = cursor + direction * length
        if i == N_SEG:
            old_tip = before[name] @ Vector((0.0, SEG_NOMINAL, 0.0))
    new_tip = cursor
    delta = new_tip - old_tip
    print("  tail tip moves by (%.3f, %.3f, %.3f), |d| = %.3f m"
          % (*delta, delta.length))

    # ---- the neck rides along -------------------------------------------
    neck_meshes = [n for n in bone_kids if n.startswith("Mesh_Neck_")]
    for name in neck_meshes:
        m = before[name].copy()
        m.translation = m.translation + delta
        target[name] = m
    print("  %d neck meshes translated with it" % len(neck_meshes))

    # ---- rig -------------------------------------------------------------
    inv = arm.matrix_world.inverted()
    bpy.context.view_layer.objects.active = arm
    arm.hide_set(False)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones

    eb["Tail_Yaw"].tail = inv @ mount
    for i in range(1, N_SEG + 1):
        m = target["Mesh_Tail_Seg%02d" % i]
        b = eb["Tail_%02d" % i]
        b.head = inv @ m.translation
        b.tail = inv @ (m @ Vector((0.0, SEG_NOMINAL, 0.0)))
    for name in NECK_BONES:
        b = eb[name]
        b.head = b.head + (inv.to_3x3() @ delta)
        b.tail = b.tail + (inv.to_3x3() @ delta)
    bpy.ops.object.mode_set(mode='OBJECT')
    print("  re-fitted Tail_Yaw, Tail_01..%02d and shifted %d neck bones"
          % (N_SEG, len(NECK_BONES)))

    for name, bone in bone_kids.items():
        attach(bpy.data.objects[name], arm, bone, target[name])

    # ---- verify ----------------------------------------------------------
    bpy.context.view_layer.update()
    print("\nVerification")
    touched = set(["Mesh_Tail_Seg%02d" % i for i in range(1, N_SEG + 1)])
    touched |= set(neck_meshes)
    moved_wrong = []
    for name, m0 in before.items():
        obj = bpy.data.objects.get(name)
        if obj is None:
            raise SystemExit("LOST object %s" % name)
        want = target[name]
        delta_m = max(abs(obj.matrix_world[r][c] - want[r][c])
                      for r in range(4) for c in range(4))
        if delta_m > 1e-4:
            moved_wrong.append((name, round(delta_m, 5)))
    if moved_wrong:
        raise SystemExit("Objects not at their intended transform: %s"
                         % moved_wrong)
    untouched = len(before) - len(touched)
    print("  %d objects left exactly alone, %d moved as intended"
          % (untouched, len(touched)))

    prev_end = None
    worst = 0.0
    for i in range(1, N_SEG + 1):
        o = bpy.data.objects["Mesh_Tail_Seg%02d" % i]
        start = o.matrix_world.translation
        if prev_end is not None:
            worst = max(worst, (start - prev_end).length)
        prev_end = o.matrix_world @ Vector((0.0, SEG_NOMINAL, 0.0))
    print("  turret -> Seg01 gap = %.4f m"
          % (bpy.data.objects["Mesh_Tail_Seg01"].matrix_world.translation
             - mount).length)
    print("  worst gap between consecutive segments = %.4f m" % worst)

    bpy.ops.wm.save_as_mainfile(filepath=TARGET)
    print("\nWrote %s" % TARGET)


main()
