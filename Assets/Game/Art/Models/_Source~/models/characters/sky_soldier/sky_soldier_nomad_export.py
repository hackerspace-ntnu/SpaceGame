"""Export the sky soldier (sky_soldier_nomad.blend) to the FBX NomadPrefabBuilder builds SkySoldier.prefab from.

    blender --background --python models/characters/sky_soldier/sky_soldier_nomad_export.py

An export, not a generator: it opens the .blend, changes only its own in-memory copy, and never saves.

What goes in: every mesh driven by `Arm_SkySoldierN` that renders. The coat panels the poncho
replaced are hidden in the file and stay out. The scarf pieces are exported under `Cloth_` names,
because that prefix is how NomadPrefabBuilder finds the cloth it dyes and gives wind to; the poncho
is a stiff slab and keeps its own name, so it stays rigid.

Flags mirror sand_nogs_export.py exactly - Unity builds the Humanoid avatar from these 65 bones, so
leaf bones stay off and the count is checked, not assumed.
"""
import os

import bpy
from mathutils import Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "sky_soldier_nomad.blend")
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)
DST = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Characters", "Nomad", "sky_soldier.fbx")

ARMATURE = "Arm_SkySoldierN"
EXPECTED_BONES = 65
CLOTH_RENAMES = {"Mesh_SkySoldierN_Scarf_": "Cloth_SkySoldier_Scarf_"}
DROP_MODIFIERS = {"COLLISION", "CLOTH", "SOFT_BODY", "PARTICLE_SYSTEM"}


def main():
    bpy.ops.wm.open_mainfile(filepath=SRC)
    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    arm = bpy.data.objects.get(ARMATURE)
    if arm is None or len(arm.data.bones) != EXPECTED_BONES:
        raise SystemExit("%s missing or not %d bones" % (ARMATURE, EXPECTED_BONES))

    meshes = sorted((o for o in bpy.data.objects
                     if o.type == 'MESH' and not o.hide_render
                     and any(m.type == 'ARMATURE' and m.object == arm for m in o.modifiers)),
                    key=lambda o: o.name)
    unbound = [o.name for o in bpy.data.objects
               if o.type == 'MESH' and not o.hide_render and o not in meshes]
    if unbound:
        raise SystemExit("Visible but not driven by %s - fix the rig first: %s" % (ARMATURE, ", ".join(unbound)))

    for mesh in meshes:
        for mod in [m for m in mesh.modifiers if m.type in DROP_MODIFIERS]:
            mesh.modifiers.remove(mod)
        for old, new in CLOTH_RENAMES.items():
            if mesh.name.startswith(old):
                mesh.name = mesh.data.name = mesh.name.replace(old, new).replace(".", "_")

    # Skinning is in armature space, so sliding the rig to the origin cancels out - but only for
    # what moves with it. The body is parented to the rig; the helmet, poncho, scarf and waist piece
    # are not, and left where they stand they would reach Unity offset from their bones by however
    # far the rig had been moved (9 cm in the user's file).
    delta = arm.matrix_world.translation.copy()
    slide = Matrix.Translation(-delta)
    for mesh in meshes:
        if mesh.parent is None:
            mesh.matrix_world = slide @ mesh.matrix_world
    arm.matrix_world = slide @ arm.matrix_world
    bpy.context.view_layer.update()

    view_layer = bpy.context.view_layer
    for obj in view_layer.objects:
        obj.select_set(False)
    for mesh in meshes:
        mesh.select_set(True)
    arm.select_set(True)
    view_layer.objects.active = arm

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
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
    print("  %d meshes, %d bones, rig moved by (%.2f, %.2f, %.2f)" % (len(meshes), len(arm.data.bones), -delta.x, -delta.y, -delta.z))
    print("  %s" % ", ".join(m.name for m in meshes))
    print("  wrote %s (%.1f MB)" % (DST, os.path.getsize(DST) / 1e6))


main()
