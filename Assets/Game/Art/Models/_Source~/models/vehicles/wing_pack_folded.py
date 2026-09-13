"""Build wing_pack_folded.blend — the ornithopter folded for carrying.

The in-hand Wing Pack item used to be eight primitive boxes; this replaces it
with the actual craft in a stowed configuration: arms swept hard back along the
fuselage, the five digit spars of each wing collapsed onto one another, the web
furled between them, the tail fan closed and the tail boom telescoped into
itself. The result is baked to ONE static mesh and scaled to hand size, because
the item never articulates — unfolding is `WingPackItem` spawning the real
DuneOrnithopter prefab.

Derived from `dune_ornithopter.blend`, which carries hand edits and is NEVER
written to. This script opens it, poses the rig in memory, and saves the baked
result to a NEW file. Pose conventions (axis table, per-side Z sign) come from
`dune_ornithopter_BUILD.md` and `dune_ornithopter_posetest.py`.

    # iterate on the fold, writes a render and exits without saving anything:
    blender --background dune_ornithopter.blend --python wing_pack_folded.py -- \
        --preview /tmp/fold.png [--view iso]

    # bake and write wing_pack_folded.blend (refuses to overwrite):
    blender --background dune_ornithopter.blend --python wing_pack_folded.py -- --commit
"""

import math
import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
DST = os.path.join(HERE, "wing_pack_folded.blend")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(flag, default=None):
    return argv[argv.index(flag) + 1] if flag in argv else default


PREVIEW = arg("--preview")
VIEW = arg("--view", "iso")
COMMIT = "--commit" in argv

# The held bundle's length along the craft's Y (nose-tail) axis, in metres.
# 0.95 matches the primitive bundle this model replaces.
TARGET_LENGTH = 0.95

R = math.radians
N_DIGITS = 5

# ---------------------------------------------------------------- fold pose
# Tuned by preview renders. Signs follow the rig's rule: local X/Y are mirrored
# between sides, local Z is not, so sweep and splay take a per-side sign.
FLAP = -26.0       # shoulders: droop so the swept wings hug the body sides
SWEEP = 83.0       # arms swept hard back, near-parallel with the fuselage
ARM_TUCK = 0.20    # fraction of the arm's length each wing slides inboard
SPLAY = -96.0      # digits collapsed onto the arm line ("bars go in on each other")
TWIST = 42.0       # web feathered flat against the stack
TAIL_SPLAY = -55.0 # tail fan closed
BOOM_TELESCOPE = 0.55  # fraction of Boom_1's length that Boom_2 slides back in
HUB_TELESCOPE = 0.45   # fraction of Boom_2's length that the tail hub slides back in


def rot(armature, bone_name, x=0.0, y=0.0, z=0.0):
    pb = armature.pose.bones[bone_name]
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = (R(x), R(y), R(z))


def apply_fold_pose():
    armature = bpy.data.objects["Arm_DuneOrnithopter"]
    bpy.context.view_layer.objects.active = armature

    # The boom chain is built connected — each head pinned to the parent's
    # tail — and a connected bone ignores pose translation entirely, so the
    # telescope below would silently do nothing. Unpin in memory; this never
    # reaches the source file (preview never saves it, commit saves elsewhere).
    bpy.ops.object.mode_set(mode='EDIT')
    for name in ("Bone_Boom_2", "Bone_TailHub"):
        armature.data.edit_bones[name].use_connect = False

    bpy.ops.object.mode_set(mode='POSE')

    for tag, s in (("R", 1), ("L", -1)):
        rot(armature, "Bone_Shoulder_%s" % tag, x=FLAP)
        arm_pb = armature.pose.bones["Bone_Arm_%s" % tag]
        rot(armature, "Bone_Arm_%s" % tag, z=SWEEP * s)
        # Pose translation happens in the bone's REST frame, where +Y points
        # outboard — so -Y slides the whole folded wing in toward the body,
        # closing the gap the wide shoulder pylons would otherwise leave.
        arm_pb.location = Vector((0.0, -arm_pb.bone.length * ARM_TUCK, 0.0))
        for i in range(N_DIGITS):
            # Same grading as the posetest's folded pose: the trailing digit
            # travels furthest, so the fan closes instead of swinging rigidly.
            k = i / (N_DIGITS - 1)
            rot(armature, "Bone_Digit_%s_%d" % (tag, i + 1),
                z=SPLAY * (k - 0.30) * s,
                y=TWIST * (0.35 + 0.65 * k))

    for i in range(N_DIGITS):
        k = i / (N_DIGITS - 1)
        rot(armature, "Bone_TailDigit_%d" % (i + 1),
            z=TAIL_SPLAY * (k - 0.5) * 2.0)

    # Telescope: a pose-bone translation is in bone-local space where +Y runs
    # head to tail, so a negative Y slides the segment back inside its parent.
    bones = armature.pose.bones
    bones["Bone_Boom_2"].location = Vector(
        (0.0, -bones["Bone_Boom_1"].bone.length * BOOM_TELESCOPE, 0.0))
    bones["Bone_TailHub"].location = Vector(
        (0.0, -bones["Bone_Boom_2"].bone.length * HUB_TELESCOPE, 0.0))

    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()
    return armature


def flush_edit_mode():
    """The source file was once saved mid-edit; flush so meshes are current."""
    for obj in bpy.data.objects:
        if obj.mode != 'OBJECT':
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.mode_set(mode='OBJECT')


def bake(armature):
    """Bake the pose into a single static mesh and drop the rig."""
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']

    # Skinned panels: applying the Armature modifier freezes the deformation.
    # Modifier apply needs single-user data; the six panels already are, but
    # copy defensively so a shared datablock never gets baked twice.
    for obj in meshes:
        mods = [m for m in obj.modifiers if m.type == 'ARMATURE']
        if not mods:
            continue
        if obj.data.users > 1:
            obj.data = obj.data.copy()
        bpy.context.view_layer.objects.active = obj
        for m in mods:
            bpy.ops.object.modifier_apply(modifier=m.name)

    # Bone-parented rigid parts: keep the posed world transform, lose the bone.
    for obj in meshes:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world

    bpy.data.objects.remove(armature, do_unlink=True)
    for obj in [o for o in bpy.data.objects if o.type not in ('MESH',)]:
        bpy.data.objects.remove(obj, do_unlink=True)

    # Join wants single-user data too — six rigid meshes are placed twice off
    # one datablock (one per side).
    for obj in meshes:
        if obj.data.users > 1:
            obj.data = obj.data.copy()

    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    baked = bpy.context.view_layer.objects.active
    baked.name = "Mesh_WingPack_Folded"
    baked.data.name = "Mesh_WingPack_Folded"

    # The port-side parts are mirrored placements; joining bakes any negative
    # scale into the geometry, so rebuild normal consistency once, at the end.
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    return baked


def recenter_and_scale(baked):
    """Origin to the bundle's centre, length along Y to TARGET_LENGTH."""
    bpy.context.view_layer.update()
    corners = [baked.matrix_world @ Vector(c) for c in baked.bound_box]
    lo = Vector((min(v[i] for v in corners) for i in range(3)))
    hi = Vector((max(v[i] for v in corners) for i in range(3)))
    center = (lo + hi) / 2.0
    length = hi.y - lo.y
    s = TARGET_LENGTH / length

    baked.matrix_world.translation -= center
    bpy.context.view_layer.update()
    baked.scale *= s
    baked.location *= s
    bpy.ops.object.select_all(action='DESELECT')
    baked.select_set(True)
    bpy.context.view_layer.objects.active = baked
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    print("  folded craft: %.2f x %.2f x %.2f m (was %.2f m long, scaled %.4f)"
          % ((hi.x - lo.x) * s, TARGET_LENGTH, (hi.z - lo.z) * s, length, s))


def main():
    flush_edit_mode()
    armature = apply_fold_pose()

    if PREVIEW:
        # Render via the library previewer and exit WITHOUT saving — the open
        # file is the hand-edited assembly and must never be written.
        sys.argv = [sys.argv[0], "--", "--out", PREVIEW, "--view", VIEW,
                    "--res", "1000"]
        # Fresh globals: exec'd inside a function, _preview.py's own nested
        # functions could not see its module-level names otherwise.
        exec(open(os.path.join(HERE, "..", "..", "_preview.py")).read(),
             {"__name__": "__preview__"})
        return

    if not COMMIT:
        raise SystemExit("pass --preview <png> or --commit")
    if os.path.exists(DST):
        raise SystemExit("%s already exists — it may hold hand edits; delete "
                         "it yourself if a rebuild is really wanted." % DST)

    # Localise palette links so the saved copy stands alone like an export.
    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    baked = bake(armature)
    recenter_and_scale(baked)

    coll = bpy.data.collections.new("Coll_WingPack_Folded")
    bpy.context.scene.collection.children.link(coll)
    for c in baked.users_collection:
        c.objects.unlink(baked)
    coll.objects.link(baked)

    bpy.ops.wm.save_as_mainfile(filepath=DST)
    print("Wrote %s" % DST)


main()
