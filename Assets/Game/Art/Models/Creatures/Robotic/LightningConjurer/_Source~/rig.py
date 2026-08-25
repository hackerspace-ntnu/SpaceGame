# Additive rig for ConjuringRobot / LightningConjurer.
# Creates one armature at the world origin with an identity transform and rigid
# bone-parents every existing part to it. No mesh data, object transform, or
# existing armature is modified.
import bpy, math
from mathutils import Matrix, Vector

ARM_NAME = "ConjurerRig"
if ARM_NAME in bpy.data.objects:
    raise SystemExit(f"{ARM_NAME} already exists - refusing to rebuild over it")

# ---------------------------------------------------------------- bone table
# (name, head, tail, parent, connected). World coords, Blender Z-up, model faces +X.
L, R = 2.12, -2.24          # leg centre lines
AL, AR = 6.32, -6.41        # floating-arm centre lines
BONES = [
    ("Root",   (0.19, -0.06,  2.76), (0.19, -0.06,  8.00), None,   False),
    ("Hips",   (0.19, -0.06, 24.07), (0.19, -0.06, 26.40), "Root", False),
    ("Spine",  (0.19, -0.06, 26.40), (0.28, -0.06, 28.95), "Hips", True),
    ("Head",   (0.28, -0.06, 28.95), (0.28, -0.06, 36.51), "Spine", True),
    ("Halo",   (0.28, -0.02, 47.72), (0.28, -0.02, 56.24), "Head", False),
]
for s, y, yk, ys, yf in (("L", L, 2.17, 2.06, 2.16), ("R", R, -2.22, -2.12, -2.17)):
    BONES += [
        (f"Thigh.{s}", (0.04, y,  25.42), (0.37, yk, 13.93), "Hips",       False),
        (f"Shin.{s}",  (0.37, yk, 13.93), (0.36, ys,  5.10), f"Thigh.{s}", True),
        (f"Foot.{s}",  (0.36, ys,  5.10), (4.00, yf,  3.20), f"Shin.{s}",  True),
    ]
for s, y in (("L", AL), ("R", AR)):
    BONES += [
        (f"ArmRoot.{s}",  (-0.22, y, 32.95), (-0.22, y, 31.93), "Spine",        False),
        (f"UpperArm.{s}", (-0.22, y, 31.93), (-0.22, y, 26.97), f"ArmRoot.{s}",  True),
        (f"Forearm.{s}",  (-0.22, y, 26.97), (-0.23, y, 21.22), f"UpperArm.{s}", True),
        (f"Hand.{s}",     (-0.23, y, 21.22), (-0.23, y, 19.00), f"Forearm.{s}",  True),
    ]

# ------------------------------------------------------- mesh -> bone binding
BIND = {
    "Hips": "Hips", "Neck": "Spine",
    "Sphere": "Head", "Eye": "Head", "Eyelid": "Head",
    "Cube": "Halo",
    "LeftLeg": "Thigh.L", "LeftKnee": "Shin.L", "LeftLowerLeg": "Shin.L", "LeftFoot": "Foot.L",
    "RightLeg": "Thigh.R", "RightKnee": "Shin.R", "RightLowerLeg": "Shin.R", "RightFoot": "Foot.R",
    # right floating arm (y ~ -6.4)
    "Cylinder.001": "UpperArm.R", "Cube.002": "UpperArm.R", "Cylinder.002": "UpperArm.R",
    "LowerArm.002": "Forearm.R", "Cube.006": "Hand.R", "Cube.007": "Hand.R",
    "BézierCurve": "UpperArm.R", "BézierCurve.002": "UpperArm.R",
    "BézierCurve.001": "Forearm.R", "BézierCurve.003": "Forearm.R",
    "BézierCurve.004": "Forearm.R", "BézierCurve.005": "Forearm.R",
    "Armature": "Hand.R",                       # existing right-hand finger rig, rides along
    # left floating arm (y ~ +6.3)
    "Cylinder.004": "UpperArm.L", "Cube.009": "UpperArm.L", "Cylinder.005": "UpperArm.L",
    "LowerArm.003": "Forearm.L", "Cube.011": "Hand.L", "Cube.013": "Hand.L",
    "BézierCurve.006": "UpperArm.L", "BézierCurve.008": "UpperArm.L",
    "BézierCurve.007": "Forearm.L", "BézierCurve.009": "Forearm.L",
    "BézierCurve.010": "Forearm.L", "BézierCurve.011": "Forearm.L",
    "Armature.001": "Hand.L",                   # existing left-hand finger rig
}

# --------------------------------------------------------------- build rig
arm_data = bpy.data.armatures.new(ARM_NAME)
arm = bpy.data.objects.new(ARM_NAME, arm_data)
bpy.context.scene.collection.objects.link(arm)
arm.matrix_world = Matrix.Identity(4)          # identity: nothing inherits a stray transform

bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
for name, head, tail, parent, conn in BONES:
    eb = arm_data.edit_bones.new(name)
    eb.head, eb.tail, eb.roll = Vector(head), Vector(tail), 0.0
    if parent:
        eb.parent = arm_data.edit_bones[parent]
        eb.use_connect = conn
bpy.ops.object.mode_set(mode='OBJECT')
print(f"created {len(arm_data.bones)} bones")

# ------------------------------------------------- rigid bone-parent binding
# Blender bone-parenting anchors at the bone TAIL, so the parent matrix carries a
# +Y translation of bone.length. Setting matrix_parent_inverse to its inverse keeps
# every object exactly where the artist left it.
def rigid_bone_parent(o, bone_name):
    """Bone-parent `o` to `bone_name`, leaving its world transform bit-identical.

    Blender anchors bone parenting at the bone TAIL, so the effective parent matrix
    P carries a +Y translation of bone.length. With matrix_parent_inverse = P^-1 the
    chain collapses to  world = P @ P^-1 @ matrix_basis = matrix_basis, so basis must
    be set to the ORIGINAL WORLD matrix. Objects that already had a parent hold a
    local offset in matrix_basis, which is why it cannot simply be left alone.
    """
    world = o.matrix_world.copy()
    bone = arm.data.bones[bone_name]
    o.parent = arm
    o.parent_type = 'BONE'
    o.parent_bone = bone_name
    P = arm.matrix_world @ bone.matrix_local @ Matrix.Translation((0.0, bone.length, 0.0))
    o.matrix_parent_inverse = P.inverted()
    o.matrix_basis = world

bound, missing = 0, []
for obj_name, bone_name in BIND.items():
    o = bpy.data.objects.get(obj_name)
    if o is None:
        missing.append(obj_name); continue
    rigid_bone_parent(o, bone_name)
    bound += 1
print(f"bound {bound} objects; missing: {missing}")

# --- collapse the two legacy hand rigs out of the deform hierarchy -------------
# Exactly one armature must reach Unity: nested armatures produce an FBX that even
# Blender's own importer cannot read back. The legacy rigs stay in the file (parked
# in WIP_Spares below); only the finger MESHES move onto ConjurerRig's hand bones.
for legacy_name, hand_bone in (("Armature", "Hand.R"), ("Armature.001", "Hand.L")):
    legacy = bpy.data.objects.get(legacy_name)
    if legacy is None:
        continue
    for child in list(legacy.children):
        rigid_bone_parent(child, hand_bone)
        for m in child.modifiers:
            if m.type == 'ARMATURE':          # now a double transform - mute, keep
                m.show_viewport = m.show_render = False
        bound += 1
    legacy.parent = None
print(f"flattened hand rigs; {bound} objects bound to {ARM_NAME} in total")

# ------------------------------------------------------ tidy: park the spares
# Moved between collections only - nothing is deleted.
SPARES = ["Head", "Shoulder", "Shoulder.001", "Elbow", "Elbow.001", "UpperArm", "UpperArm.001",
          "LowerArm", "LowerArm.001", "Hand", "Weapon", "NeckThing", "Cube.001", "Cube.003",
          "Cube.004", "Cube.005", "Cube.008", "Cube.010", "Cube.012", "Cube.014", "Cube.016",
          "Cube.018", "Cube.019", "Cube.020", "Cube.033", "Cube.034", "Cylinder.003",
          "Cylinder.007", "Cylinder.010", "Cylinder.012", "Icosphere", "Sphere.001", "Sphere.002",
          "Legs", "Armature", "Armature.001"]
spare_col = bpy.data.collections.get("WIP_Spares")
if spare_col is None:
    spare_col = bpy.data.collections.new("WIP_Spares")
    bpy.context.scene.collection.children.link(spare_col)
moved = 0
for n in SPARES:
    o = bpy.data.objects.get(n)
    if o is None or o.name in spare_col.objects:
        continue
    for c in list(o.users_collection):
        c.objects.unlink(o)
    spare_col.objects.link(o)
    moved += 1
print(f"parked {moved} spare objects in WIP_Spares (nothing deleted)")

# put the rig in the Character collection alongside the body
char = bpy.data.collections.get("Character")
if char:
    bpy.context.scene.collection.objects.unlink(arm)
    char.objects.link(arm)

bpy.ops.wm.save_mainfile()
print("SAVED")
