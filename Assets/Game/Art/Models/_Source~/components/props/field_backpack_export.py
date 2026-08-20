# Export field_backpack.blend to the Unity asset tree.
#
# Run headless:
#   blender -b field_backpack.blend --python field_backpack_export.py
#
# The empties are the point of the export, not an afterthought — Unity reads PIVOT_Clamshell as
# the clamshell hinge and the twelve SOCK_* transforms as the sockets items are seated in, all by
# name. 'EMPTY' therefore has to stay in object_types, and the names must survive untouched.
import os

import bpy

HERE = os.path.dirname(os.path.abspath(bpy.data.filepath))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "..", "Props", "field_backpack.fbx"))


def main():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)

    for obj in bpy.data.objects:
        obj.select_set(True)

    bpy.ops.export_scene.fbx(
        filepath=OUT,
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        # Unity's convention. Blender +Z up becomes Unity +Y up; the socket axes are rotated with
        # everything else, so what a socket points at in Unity is NOT what it points at here.
        axis_forward="-Z",
        axis_up="Y",
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_NONE",
        bake_space_transform=False,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        path_mode="COPY",
        embed_textures=False,
    )

    print("EXPORTED:", OUT, os.path.getsize(OUT), "bytes")


main()
