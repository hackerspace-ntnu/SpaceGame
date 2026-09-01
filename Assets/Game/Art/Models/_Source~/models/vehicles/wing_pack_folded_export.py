"""Export wing_pack_folded.blend to the FBX the WingPack item prefab nests.

An export, not a generator — re-runnable, never writes to the .blend it opens.
Unlike `dune_ornithopter_export.py` there is no armature to keep: the folded
pack is one baked static mesh, because the item never articulates — unfolding
is `WingPackItem` spawning the real craft.

    blender --background --python wing_pack_folded_export.py
"""

import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)
SRC = os.path.join(HERE, "wing_pack_folded.blend")
DST = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Vehicles", "Ornithopter",
                   "wing_pack_folded.fbx")


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s" % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    mesh = bpy.data.objects.get("Mesh_WingPack_Folded")
    if mesh is None:
        raise SystemExit("Mesh_WingPack_Folded missing — the bake did not run?")

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=False,
        object_types={'MESH'},
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        bake_space_transform=False,
        path_mode='COPY',
        embed_textures=False,
    )
    print("Wrote %s (%.1f MB)" % (DST, os.path.getsize(DST) / 1e6))


main()
