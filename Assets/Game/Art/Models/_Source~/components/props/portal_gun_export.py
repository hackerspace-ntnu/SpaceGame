"""Ship one portal gun variation from the component file to Unity.

`_exportlib.export()` writes a whole .blend, which is right for a model file and
wrong for a component: this one holds four variations stacked at the origin, so
exporting everything produces an FBX with four overlapping guns in it. This
picks a single collection and exports only that, exactly as
`item_devices_export.py` does — the export flags are copied from `_exportlib`
deliberately rather than re-chosen, since each one is load-bearing there and the
reasons are written down in that module.

Unlike the device exports this ships two collections, because the spent bottle
is a world prop in its own right rather than an alternative for the same slot.

Exports are the one kind of script here meant to be re-run: they only ever read
the .blend.

    blender --background --python components/props/portal_gun_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import unity_path, _localise_materials  # noqa: E402

BLEND = "portal_gun.blend"

# (collection to ship, FBX basename)
TARGETS = [
    ("Coll_PortalGun_Extinguisher", "portal_gun.fbx"),
    ("Coll_PortalGun_Spent", "portal_gun_spent.fbx"),
]


def export_one(coll_name, fbx_name):
    src = os.path.join(HERE, BLEND)
    if not os.path.exists(src):
        raise SystemExit("No component at %s" % src)

    bpy.ops.wm.open_mainfile(filepath=src)
    localised = _localise_materials()

    coll = bpy.data.collections.get(coll_name)
    if coll is None:
        raise SystemExit("No collection %r in %s" % (coll_name, BLEND))

    # Drop every other variation outright rather than merely deselecting it: a
    # stray modifier or parent can still pull an unselected mesh into the file
    # even with use_selection set.
    keep = {o.name for o in coll.objects}
    for obj in list(bpy.data.objects):
        if obj.name not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)

    for obj in bpy.data.objects:
        obj.select_set(True)

    tris = sum(sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
               for o in bpy.data.objects if o.type == 'MESH')

    dst = unity_path("Items", fbx_name)
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
    print("EXPORTED %-30s %d object(s) %d tri(s) %d mat(s) localised -> %s"
          % (coll_name, len(bpy.data.objects), tris, localised, dst))


def main():
    for coll_name, fbx_name in TARGETS:
        export_one(coll_name, fbx_name)
    # Deliberately no save_mainfile: the .blend is the source of truth and this
    # script has just deleted objects out of the in-memory copy.


main()
