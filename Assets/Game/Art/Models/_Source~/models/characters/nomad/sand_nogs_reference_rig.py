"""Bind each sand nomad to a copy of the reference skeleton, without moving a single vertex.

    blender --background --python sand_nogs_reference_rig.py -- [path/to/sand_nogs.blend] [--dry-run]

WRITES to the .blend (timestamped `sand_nogs_pre_refrig_*.blend` copy beside it). Runs after
sand_nogs_fix_rig.py and sand_nogs_fix_normals.py, before sand_nogs_export.py.

Why
---
sand_nogs_fix_rig.py derives a skeleton from each body's own vertices. That rig deforms well in
Blender, but the one thing known to animate in Unity is the ORIGINAL nomad's rig, and the two
differ in bone rolls, joint connectivity and the exact rest orientation of every bone. This
script swaps the fitted rig for a copy of that original skeleton, stretched by the body's own
per-axis scale and moved onto it, and rebinds every part to it.

It does NOT pose or bake anything. An earlier version of this step moved the vertices onto the
reference pose and destroyed the artist's placement of every face; the bodies' arms are within a
few centimetres of the reference pose already, and a few centimetres of skinning error is
invisible where a moved face is not. Mesh data is read, never written.
"""

import datetime
import os
import shutil
import sys

import bpy
from mathutils import Matrix, Vector

DEFAULT_BLEND = os.path.join(os.path.expanduser("~"), "Documents", "Blender", "sand_nogs.blend")

REFERENCE_SUIT = "Suit.001"
REFERENCE_ARMATURE = "Armature.001"
EXPECTED_BONES = 65
CHARACTERS = ["Umber", "Tan", "Maroon", "StrawHat"]
SUIT_OF = {name: "%s_Suit" % name for name in CHARACTERS}

BONE_WEIGHT_CUTOFF = 0.4


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    dry = "--dry-run" in argv
    paths = [a for a in argv if not a.startswith("--")]
    return (paths[0] if paths else DEFAULT_BLEND), dry


def world_verts(obj):
    return [obj.matrix_world @ v.co for v in obj.data.vertices]


def fit_axis_scale(src, dst):
    """Per-axis scale + translation taking `src` onto `dst`. Same fit as sand_nogs_fix_rig.py."""
    n = len(src)
    cs = sum(src, Vector()) / n
    cd = sum(dst, Vector()) / n
    scale = Vector((1.0, 1.0, 1.0))
    for axis in range(3):
        var = sum((p[axis] - cs[axis]) ** 2 for p in src)
        dot = sum((p[axis] - cs[axis]) * (q[axis] - cd[axis]) for p, q in zip(src, dst))
        scale[axis] = dot / var
    translation = cd - Vector((cs[0] * scale[0], cs[1] * scale[1], cs[2] * scale[2]))
    return scale, translation


def driven_by(armature):
    return sorted((o for o in bpy.data.objects if o.type == 'MESH'
                   and any(m.type == 'ARMATURE' and m.object == armature for m in o.modifiers)),
                  key=lambda o: o.name)


def bake_world_scale_into_bones(armature, scale):
    """Stretch the rest skeleton by a per-axis WORLD scale, leaving the object's own transform
    at the family's uniform 0.01.

    Bones are disconnected while their heads and tails move -- a connected child's head IS its
    parent's tail, so moving both through the map would apply it twice -- and reconnected after,
    which is a no-op because the map keeps every joint coincident.
    """
    world = armature.matrix_world.to_3x3()
    local = world.inverted() @ Matrix.Diagonal(scale).to_3x3() @ world
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode='EDIT')
    edit = armature.data.edit_bones
    connected = [b.name for b in edit if b.use_connect]
    for b in edit:
        b.use_connect = False
    for b in edit:
        head, tail = local @ b.head, local @ b.tail
        b.head, b.tail = head, tail
    for name in connected:
        edit[name].use_connect = True
    bpy.ops.object.mode_set(mode='OBJECT')


def rebind(obj, armature):
    """Parent to `armature` and drive with it, without moving the object on screen."""
    keep = obj.matrix_world.copy()
    obj.parent = armature
    obj.parent_type = 'OBJECT'
    obj.matrix_parent_inverse = armature.matrix_world.inverted()
    obj.matrix_world = keep
    for mod in obj.modifiers:
        if mod.type == 'ARMATURE':
            mod.object = armature


def skin_fit(mesh, armature):
    """Mean distance from each vertex to its dominant bone's segment, in metres."""
    total, count = 0.0, 0
    bones, mw = armature.data.bones, armature.matrix_world
    for vert in mesh.data.vertices:
        best, best_w = None, BONE_WEIGHT_CUTOFF
        for entry in vert.groups:
            if entry.group < len(mesh.vertex_groups) and entry.weight > best_w:
                best, best_w = mesh.vertex_groups[entry.group].name, entry.weight
        if best is None or best not in bones:
            continue
        bone = bones[best]
        head, tail = mw @ bone.head_local, mw @ bone.tail_local
        p = mesh.matrix_world @ vert.co
        axis = tail - head
        t = max(0.0, min(1.0, (p - head).dot(axis) / max(axis.length_squared, 1e-9)))
        total += (p - (head + axis * t)).length
        count += 1
    return total / max(count, 1)


def main():
    blend, dry = parse_args()
    if not os.path.exists(blend):
        raise SystemExit("No model at %s" % blend)
    if not dry:
        stamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        backup = os.path.join(os.path.dirname(blend), "sand_nogs_pre_refrig_%s.blend" % stamp)
        shutil.copyfile(blend, backup)
        print("Backed up -> %s" % backup)

    bpy.ops.wm.open_mainfile(filepath=blend)
    ref_arm = bpy.data.objects[REFERENCE_ARMATURE]
    ref_suit = bpy.data.objects[REFERENCE_SUIT]
    if len(ref_arm.data.bones) != EXPECTED_BONES:
        raise SystemExit("Reference rig has %d bones." % len(ref_arm.data.bones))
    ref_verts = world_verts(ref_suit)

    for name in CHARACTERS:
        old = bpy.data.objects.get("Armature_Nomad%s" % name)
        suit = bpy.data.objects.get(SUIT_OF[name])
        if old is None or suit is None:
            raise SystemExit("%s: run sand_nogs_fix_rig.py first." % name)
        meshes = driven_by(old)
        vertex_hash_before = sum(v.co.x + v.co.y * 3.0 + v.co.z * 7.0 for m in meshes for v in m.data.vertices)
        print("\n=== %s: %d meshes on %s" % (name, len(meshes), old.name))

        scale, translation = fit_axis_scale(ref_verts, world_verts(suit))
        print("  affine: scale (%.4f, %.4f, %.4f), translation (%.3f, %.3f, %.3f)"
              % (scale.x, scale.y, scale.z, translation.x, translation.y, translation.z))

        final_name = "Armature_Nomad%s" % name
        old.name = "Armature_Fitted_%s" % name
        final = ref_arm.copy()
        final.data = ref_arm.data.copy()
        final.name = final_name
        final.data.name = final_name
        final.animation_data_clear()
        for coll in ref_arm.users_collection:
            coll.objects.link(final)
        # The affine map q = scale * p + translation is about the WORLD origin, and the
        # reference rig does not stand there (x ~ 7). So the armature's own origin has to go
        # through the map too, or every joint lands scale-times-seven metres to one side: 10 cm
        # on three bodies, 31 cm on the straw hat. The bones are then stretched about that origin.
        origin = ref_arm.matrix_world.translation
        mapped = Vector((origin.x * scale.x, origin.y * scale.y, origin.z * scale.z)) + translation
        final.matrix_world = Matrix.Translation(mapped - origin) @ ref_arm.matrix_world
        bpy.context.view_layer.update()
        bake_world_scale_into_bones(final, scale)
        bpy.context.view_layer.update()

        for obj in meshes:
            rebind(obj, final)
        bpy.data.objects.remove(old, do_unlink=True)
        for data in [a for a in bpy.data.armatures if a.users == 0]:
            bpy.data.armatures.remove(data)

        vertex_hash_after = sum(v.co.x + v.co.y * 3.0 + v.co.z * 7.0 for m in meshes for v in m.data.vertices)
        print("  vertices untouched: %s" % (abs(vertex_hash_after - vertex_hash_before) < 1e-6))

        # Every joint must land exactly where the affine map puts the reference joint; a bone
        # that does not has been dragged by a connected neighbour during the stretch.
        S = Matrix.Diagonal((scale.x, scale.y, scale.z, 1.0))
        T = Matrix.Translation(translation)
        worst = max(((T @ S @ (ref_arm.matrix_world @ b.head_local)) - (final.matrix_world @ final.data.bones[b.name].head_local)).length
                    for b in ref_arm.data.bones)
        print("  joints off the affine map by at most %.4f m" % worst)
        print("  skin fit: suit vertices sit %.3f m from their bones here, %.3f m on the reference"
              % (skin_fit(suit, final), skin_fit(ref_suit, ref_arm)))
        for bname in ("mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand"):
            b = final.data.bones[bname]
            h = final.matrix_world @ b.head_local
            print("      %-14s head at (%.2f, %.2f, %.2f)" % (bname.split(':')[1], h.x, h.y, h.z))

    if dry:
        print("\nDRY RUN -- nothing saved.")
        return
    bpy.ops.wm.save_mainfile(filepath=blend)
    print("Saved %s" % blend)


main()
