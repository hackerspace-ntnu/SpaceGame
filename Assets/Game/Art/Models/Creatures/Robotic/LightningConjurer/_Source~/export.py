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
import numpy as np
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

# ------------------------------------------- modifiers -> shape keys (in memory)
# Blender's FBX exporter DROPS every shape key off any mesh it has to EVALUATE. The
# evaluation goes through bpy.data.meshes.new_from_object(), which strips them -- the
# exporter says so itself, in its own comment on the branch it takes when it does NOT
# have to evaluate ("removes shape keys (see #104714)"). With use_mesh_modifiers=True
# it evaluates every mesh carrying an enabled non-armature modifier, and the Eyelid
# carries a Solidify. So the lid shipped as ONE FROZEN MESH, baked at whatever its
# sliders happened to read at export time -- the FBX had zero BlendShape deformers and
# Unity had nothing to drive.
#
# Armature modifiers are exempt and must be left alone: the exporter parks those at the
# REST pose instead of evaluating, so they never trip its `do_evaluate`. Only the others
# have to go, and the only way to lose a modifier without losing the keys is to run
# every key block through the stack here and rebuild the keys from the results.
#
# In memory only. The .blend is never saved, so the artist keeps the live Solidify.

# Shape keys whose DEFORMED state, not their Basis, is the creature's normal
# appearance. Only the eyelid's two, whose Basis is the closed lid.
EXPORT_OPEN = {"Top open", "Bottom Open"}


def bake_modifiers_into_shape_keys(ob):
    """Apply ob's non-armature modifiers to every shape key, then remove them.

    Returns the number of keys baked, or 0 if the object needed nothing. Each key is
    evaluated as its own temporary object because a modifier reads the BASE vertex
    positions -- evaluating once and offsetting afterwards would apply the stack to the
    closed lid and then slide the result, which is not the same shape.
    """
    me = ob.data
    keys = getattr(me, "shape_keys", None)
    live = [m for m in ob.modifiers
            if m.type != 'ARMATURE' and (m.show_viewport or m.show_render)]
    if not keys or len(keys.key_blocks) < 2 or not live:
        return 0

    blocks = list(keys.key_blocks)
    nverts = len(me.vertices)
    src = []
    for kb in blocks:
        a = np.empty(nverts * 3, dtype=np.float32)
        kb.data.foreach_get("co", a)
        src.append(a)

    baked = []
    for co in src:
        tmp = ob.copy()
        tmp.data = me.copy()
        bpy.context.scene.collection.objects.link(tmp)
        tmp.shape_key_clear()                       # base verts are now editable
        for m in [m for m in tmp.modifiers if m.type == 'ARMATURE']:
            tmp.modifiers.remove(m)                 # rest pose, exactly as the exporter would
        tmp.data.vertices.foreach_set("co", co)
        tmp.data.update()
        bpy.context.view_layer.update()

        dg = bpy.context.evaluated_depsgraph_get()
        ev = bpy.data.meshes.new_from_object(
            tmp.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
        out = np.empty(len(ev.vertices) * 3, dtype=np.float32)
        ev.vertices.foreach_get("co", out)
        baked.append((out, ev, tmp))

    counts = {len(b[0]) for b in baked}
    assert len(counts) == 1, (
        f"{ob.name}: the modifier stack produced a different vertex count for different "
        f"shape keys {counts} -- shape keys cannot be rebuilt from it")

    # Key 0 is the Basis, so its evaluated mesh IS the new base: it already carries the
    # solidified topology, the material slots and -- because of preserve_all_data_layers
    # -- the vertex colour attribute rustify.py wrote, which the creature's whole look
    # depends on.
    ob.data = baked[0][1]
    for m in live:
        ob.modifiers.remove(m)

    ob.shape_key_add(name=blocks[0].name, from_mix=False)
    for kb, (co, _, _) in zip(blocks[1:], baked[1:]):
        nk = ob.shape_key_add(name=kb.name, from_mix=False)
        nk.data.foreach_set("co", co)
        nk.slider_min, nk.slider_max = kb.slider_min, kb.slider_max
        # Set explicitly rather than inherited, because the value a key carries AT EXPORT
        # is what the exporter bakes into every animation stack as that channel's constant
        # -- and Unity reads those, on the armature take, for every clip in the file.
        # 'Key 3' used to sit at 1.0 and that is what froze the lid open.
        #
        # The two lid keys are exported OPEN, and that is load-bearing rather than a
        # preference. Every clip in this FBX therefore holds the eye open, which is what
        # they should hold: the creature is awake in all three of them. Export them at 0
        # and Idle drives the lid shut on the frame after the Awakening clip finishes
        # opening it -- a clip's curve always beats what anything else wrote. See
        # LightningConjurerBuilder.BuildEyeClips, which checks this and refuses to build
        # quietly if it stops being true.
        nk.value = 1.0 if nk.name in EXPORT_OPEN else 0.0

    for i, (_, ev, tmp) in enumerate(baked):
        bpy.data.objects.remove(tmp, do_unlink=True)
        if i != 0:
            bpy.data.meshes.remove(ev)
    return len(blocks) - 1


for o in sorted(sel, key=lambda x: x.name):
    if o.type != 'MESH':
        continue
    n = bake_modifiers_into_shape_keys(o)
    if n:
        print(f"baked modifiers into {n} shape keys on {o.name}: "
              f"{[k.name for k in o.data.shape_keys.key_blocks]}")

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
    # The weathering lives in a vertex colour attribute, so this is not optional:
    # without it the creature exports with no colour variation at all and imports
    # as one flat grey.
    #
    # SRGB, and the difference is very visible. rustify.py writes the palette's
    # own base-colour values, which are stored as hex/255 -- display-referred
    # numbers. Unity converts incoming vertex colours from sRGB to linear, so
    # exporting them as LINEAR means that conversion is applied to values that
    # were never encoded, and the whole creature imports about 30% too dark.
    # Exporting as SRGB has Blender encode on the way out so Unity's decode lands
    # back on the authored value.
    colors_type='SRGB',
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
