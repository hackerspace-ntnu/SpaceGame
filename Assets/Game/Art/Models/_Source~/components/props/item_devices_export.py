"""Export one chosen variation from each carried-device component to Unity.

`_exportlib.export()` writes an entire .blend, which is right for a model file
but wrong for a component: these files hold three variations stacked at the
origin, and exporting all of them produces an FBX with three overlapping props
in it. So this selects a single collection and exports just that.

The export flags are copied from `_exportlib` deliberately rather than being
re-chosen — each one is load-bearing there and the reasons are documented in
that module's docstring. What is NOT copied is the backpack export's habit of
reaching `Assets/` by counting `..` segments; `unity_path()` walks up to the
folder holding ProjectSettings instead, which is what survives the library
being moved.

Exports are the one kind of script here that is meant to be re-run: they only
ever read the .blend.

Run headless, once per component:

    blender --background --python components/props/item_devices_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import unity_path, _localise_materials  # noqa: E402

# (component file, collection to ship, FBX basename)
TARGETS = [
    ("weather_station_device.blend", "Coll_WeatherStation_Field",
     "weather_station.fbx"),
    ("antigrav_device.blend", "Coll_AntiGrav_Ring", "antigrav_emitter.fbx"),
    ("leash_device.blend", "Coll_Leash_Spool", "leash_emitter.fbx"),
    ("lasso_coil.blend", "Coll_Lasso_Coil", "lasso_coil.fbx"),
]


def export_one(blend_name, coll_name, fbx_name):
    src = os.path.join(HERE, blend_name)
    if not os.path.exists(src):
        raise SystemExit("No component at %s" % src)

    bpy.ops.wm.open_mainfile(filepath=src)
    localised = _localise_materials()

    coll = bpy.data.collections.get(coll_name)
    if coll is None:
        raise SystemExit("No collection %r in %s" % (coll_name, blend_name))

    # Drop every other variation outright rather than merely deselecting it.
    # `use_selection` alone still lets a stray modifier or parent pull an
    # unselected mesh into the file.
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
    print("EXPORTED %-26s %d mesh(es) %d tri(s) %d mat(s) localised -> %s"
          % (coll_name, len(bpy.data.objects), tris, localised, dst))


def main():
    for blend_name, coll_name, fbx_name in TARGETS:
        export_one(blend_name, coll_name, fbx_name)
    # Deliberately no save_mainfile: the .blend is the source of truth and this
    # script has just deleted objects out of the in-memory copy.


main()
