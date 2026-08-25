# Exports ConjurerRig + everything bound to it as an FBX for Unity.
# The .blend is NEVER saved by this script.
#
# The armature object is left at IDENTITY on purpose. Unity discards the armature
# node's own transform for a bone-parented (non-skinned) rig, so any rotation or
# scale parked there survives in the animation curves but vanishes from the bind
# pose - the model then stands correctly only while a clip is playing. See the
# ConfigureImporter comment in GolemBuilder.cs, which hit exactly this.
#
# So: write Blender's native Z-up axes untouched and let Unity's
# bakeAxisConversion do the conversion, with globalScale carrying the metre scale
# and the prefab's model child carrying the +X -> +Z yaw.
import bpy, sys
from mathutils import Matrix

out = sys.argv[sys.argv.index('--')+1]
arm = bpy.data.objects["ConjurerRig"]

# The FBX bind pose is captured from the CURRENT evaluated state. Detaching the
# action is not enough - pose bones keep whatever the last keyframe left behind
# (Walk's final frame parks Shin.R at -35.7 deg), and that bent knee becomes the
# bind pose. Zero them explicitly.
if arm.animation_data:
    arm.animation_data.action = None
for pb in arm.pose.bones:
    pb.matrix_basis = Matrix.Identity(4)
bpy.context.scene.frame_set(1)
bpy.context.view_layer.update()
assert arm.matrix_world == Matrix.Identity(4), "armature must stay at identity"

sel = {arm}
def walk(o):
    for c in o.children:
        sel.add(c); walk(c)
walk(arm)

# Armature modifiers that do not point at ConjurerRig are muted in the .blend, but
# the FBX exporter reads modifiers regardless of visibility and would emit a second,
# dangling skin deformer. In-memory only.
stripped = 0
for o in sel:
    for m in [m for m in getattr(o, "modifiers", []) if m.type == 'ARMATURE' and m.object is not arm]:
        o.modifiers.remove(m); stripped += 1

bpy.ops.object.select_all(action='DESELECT')
for o in sel:
    o.select_set(True)
bpy.context.view_layer.objects.active = arm
print(f"exporting {len(sel)} objects; stripped {stripped} legacy armature modifiers")

bpy.ops.export_scene.fbx(
    filepath=out,
    use_selection=True,
    object_types={'ARMATURE', 'MESH', 'OTHER'},   # OTHER carries the Bezier cable curves
    use_mesh_modifiers=True,
    add_leaf_bones=False,
    primary_bone_axis='Y', secondary_bone_axis='X',
    armature_nodetype='NULL',
    bake_anim=True,
    bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.0,
    bake_space_transform=False,
    apply_scale_options='FBX_SCALE_NONE',
    global_scale=1.0,
    axis_forward='Y', axis_up='Z',               # Blender-native; Unity bakes the conversion
    path_mode='COPY',
)
print("EXPORTED", out)
