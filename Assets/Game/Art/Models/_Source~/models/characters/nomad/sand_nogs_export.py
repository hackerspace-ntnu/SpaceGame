"""Export each sand-nomad character in sand_nogs.blend to its own FBX.

    blender --background --python sand_nogs_export.py -- [path/to/sand_nogs.blend]

An export, not a generator: it never writes to the .blend it opens. Run `sand_nogs_fix_rig.py`
first -- this script refuses a file whose characters have not been fitted, because an unfitted
character exports with a skeleton 1.3 m above its body and flies apart on the first clip.

One FBX per character, next to nomad.fbx:

    Assets/Game/Art/Models/Characters/Nomad/nomad_umber.fbx
    Assets/Game/Art/Models/Characters/Nomad/nomad_tan.fbx
    Assets/Game/Art/Models/Characters/Nomad/nomad_maroon.fbx
    Assets/Game/Art/Models/Characters/Nomad/nomad_strawhat.fbx

What goes in each file is everything driven by that character's `Armature_Nomad<Name>` -- which
is what the rig fix rebound, so a mesh that was left out of the fix stays out of the export and is
listed rather than silently shipped as static geometry. The rig is slid to the world origin before
export for the same reason nomad_export.py does it: exported where it stands, the character would
be built into a prefab whose body stands metres from its own transform.

Export flags mirror nomad_export.py exactly. Unity's Humanoid avatar is generated from the bone
count, so leaf bones stay OFF and the count is checked, not assumed.
"""

import os
import sys

import bpy
from mathutils import Matrix

DEFAULT_BLEND = os.path.join(os.path.expanduser("~"), "Documents", "Blender", "sand_nogs.blend")

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)
DST_DIR = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Characters", "Nomad")

CHARACTERS = ["Umber", "Tan", "Maroon", "StrawHat"]
EXPECTED_BONES = 65

# Sim-only modifiers: no geometry, and a COLLISION modifier means nothing to Unity.
DROP_MODIFIERS = {"COLLISION", "CLOTH", "SOFT_BODY", "PARTICLE_SYSTEM"}


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    paths = [a for a in argv if not a.startswith("--")]
    return paths[0] if paths else DEFAULT_BLEND


def driven_by(armature):
    return sorted((o for o in bpy.data.objects
                   if o.type == 'MESH'
                   and any(m.type == 'ARMATURE' and m.object == armature for m in o.modifiers)),
                  key=lambda o: o.name)


def export_character(name, blend):
    arm = bpy.data.objects.get("Armature_Nomad%s" % name)
    if arm is None or arm.type != 'ARMATURE':
        raise SystemExit("No Armature_Nomad%s in %s -- run sand_nogs_fix_rig.py first." % (name, blend))
    if len(arm.data.bones) != EXPECTED_BONES:
        raise SystemExit("Armature_Nomad%s has %d bones, expected %d." % (name, len(arm.data.bones), EXPECTED_BONES))

    meshes = driven_by(arm)
    if not meshes:
        raise SystemExit("Nothing is driven by Armature_Nomad%s." % name)

    stray = [o.name for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm and o not in meshes]
    if stray:
        print("  NOTE: parented to the rig but not driven by it, NOT exported: %s" % ", ".join(stray))

    for mesh in meshes:
        for mod in list(mesh.modifiers):
            if mod.type in DROP_MODIFIERS:
                mesh.modifiers.remove(mod)

    # Slide the rig to the origin. Its children ride along; skinning is computed in armature
    # space, so the same translation appears on both sides and cancels.
    delta = arm.matrix_world.translation.copy()
    arm.matrix_world = Matrix.Translation(-delta) @ arm.matrix_world
    bpy.context.view_layer.update()

    view_layer = bpy.context.view_layer
    view_layer.objects.active = arm
    if arm.mode != 'OBJECT':
        try:
            bpy.ops.object.mode_set(mode='OBJECT')
        except RuntimeError:
            pass
    for obj in view_layer.objects:
        obj.select_set(False)
    for mesh in meshes:
        mesh.select_set(True)
    arm.select_set(True)

    dst = os.path.join(DST_DIR, "nomad_%s.fbx" % name.lower())
    os.makedirs(DST_DIR, exist_ok=True)
    verts = sum(len(m.data.vertices) for m in meshes)
    print("  %d meshes (%d verts), %d bones, rig moved from (%.2f, %.2f, %.2f) -> %s"
          % (len(meshes), verts, len(arm.data.bones), delta.x, delta.y, delta.z, dst))

    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=True,
        object_types={'MESH', 'ARMATURE'},
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        armature_nodetype='NULL',
        bake_space_transform=False,
        path_mode='COPY',
        embed_textures=False,
    )
    print("  wrote %s (%.1f MB)" % (dst, os.path.getsize(dst) / 1e6))

    # Put the rig back so the next character is measured in the file's own coordinates.
    arm.matrix_world = Matrix.Translation(delta) @ arm.matrix_world
    bpy.context.view_layer.update()


def main():
    blend = parse_args()
    if not os.path.exists(blend):
        raise SystemExit("No model at %s" % blend)

    bpy.ops.wm.open_mainfile(filepath=blend)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    for name in CHARACTERS:
        print("=== %s" % name)
        export_character(name, blend)
    # Deliberately no save_mainfile: the .blend is the source of truth.


main()
