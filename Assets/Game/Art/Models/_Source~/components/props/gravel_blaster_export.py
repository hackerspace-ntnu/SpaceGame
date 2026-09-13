"""Export the Gravel Blaster's hero variation to Unity.

Only the Twin ships — it is the variation the artifact prefab references.
The Triple and Quad mods stay in the component file until something in the
game actually needs them; adding one is a line in TARGETS.

The two Marker_* cubes are exported WITH the mesh on purpose: they carry the
muzzle and grip coordinates across the FBX for GravelBlasterBuilder to adopt
and strip (empties do not survive `object_types={"MESH"}`).

Flags are copied from `_exportlib` for the reasons documented there.
Exports are the one kind of script here that is meant to be re-run: it only
ever reads the .blend.

    blender --background --python components/props/gravel_blaster_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import unity_path, _localise_materials  # noqa: E402

# (component file, collection to ship, FBX path under Assets/Game/Art/Models/)
TARGETS = [
    ("gravel_blaster.blend", "Coll_GravelBlaster_Twin",
     ("Weapons", "GravelBlaster", "gravel_blaster.fbx")),
]


def export_one(blend_name, coll_name, fbx_parts):
    src = os.path.join(HERE, blend_name)
    if not os.path.exists(src):
        raise SystemExit("No component at %s" % src)

    bpy.ops.wm.open_mainfile(filepath=src)
    localised = _localise_materials()

    coll = bpy.data.collections.get(coll_name)
    if coll is None:
        raise SystemExit("No collection %r in %s" % (coll_name, blend_name))

    # Drop every other variation outright rather than merely deselecting it:
    # `use_selection` alone still lets a stray modifier or parent pull an
    # unselected mesh into the file, and all three variations sit on the same
    # origin.
    keep = {o.name for o in coll.objects}
    for obj in list(bpy.data.objects):
        if obj.name not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)

    for obj in bpy.data.objects:
        obj.select_set(True)

    tris = sum(sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
               for o in bpy.data.objects if o.type == 'MESH')

    dst = unity_path(*fbx_parts)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=True,
        object_types={"MESH"},
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
    print("EXPORTED %-28s %d mesh(es) %d tri(s) %d mat(s) localised -> %s"
          % (coll_name, len(bpy.data.objects), tris, localised, dst))


def main():
    for blend_name, coll_name, fbx_parts in TARGETS:
        export_one(blend_name, coll_name, fbx_parts)
    # Deliberately no save_mainfile: the .blend is the source of truth and this
    # script has just deleted objects out of the in-memory copy.


main()
