"""Ship the Appa saddle to Unity.

    blender --background --python models/gear/saddle_export.py

Exports ONE collection out of a file that holds two, so this cannot use
`_exportlib.export` the way `item_scanner_export.py` does — that ships the whole
file, and here the second variation (`Coll_Saddle_Pack`, the cargo pad for a
future animal) would ride along inside the same FBX.

No armature: nothing on the saddle articulates. The `SURF_` and `SEAT_` empties
are exported, because Unity reads them — `PackContainer` resolves its faces from
every `PackSurface` in its children, and `SaddleFitting` seats the rider on
`SEAT_Rider`.

Exports are meant to be re-run; this only ever reads the .blend.
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import unity_path  # noqa: E402

SRC = os.path.join(HERE, "saddle.blend")

# One collection per animal, exported to its own FBX. `saddle.blend` holds three
# and shipping the whole file would put every other animal's saddle inside each.
EXPORTS = [
    ("Coll_Saddle_Appa", unity_path("Items", "saddle_appa.fbx")),
    ("Coll_Saddle_Loper", unity_path("Items", "saddle_loper.fbx")),
]


def export_one(collection, dst):
    bpy.ops.wm.open_mainfile(filepath=SRC)

    coll = bpy.data.collections.get(collection)
    if coll is None:
        raise SystemExit("No %s in %s" % (collection, SRC))

    keep = {o.name for o in coll.objects}
    for obj in [o for o in bpy.data.objects if o.name not in keep]:
        bpy.data.objects.remove(obj, do_unlink=True)

    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.data.objects:
        obj.select_set(True)

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=True,
        object_types={'MESH', 'EMPTY'},
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode='COPY',
        embed_textures=False,
    )

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    empties = [o for o in bpy.data.objects if o.type == 'EMPTY']
    tris = sum(sum(max(0, len(p.vertices) - 2) for p in o.data.polygons) for o in meshes)
    print("  wrote %s" % dst)
    print("  %d meshes, %d tris, %d empties" % (len(meshes), tris, len(empties)))
    for obj in sorted(empties, key=lambda o: o.name):
        loc = obj.location
        print("  EMPTY %-24s (%.3f, %.3f, %.3f)" % (obj.name, loc.x, loc.y, loc.z))


def main():
    for collection, dst in EXPORTS:
        export_one(collection, dst)


main()
