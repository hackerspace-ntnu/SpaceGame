"""Export the inventory wall to Unity.

    blender --background --python models/props/inventory_wall_export.py

Not `_exportlib.export()`, for the same reason `expedition_rig_export.py` is
not: that helper passes `object_types={'MESH'}`, which would silently drop
`SURF_WallGrid` — and the surface empty is the entire point of the model. An
FBX of the meshes alone imports looking perfect and is functionally inert,
because `PackSurface` has nothing to sit on.

The library's usual `-Z` forward / `Y` up conversion applies: this is an
ordinary Z-up Blender model. Under that conversion Blender (x, y, z) arrives as
Unity (x, z, -y), so the wall's -Y face becomes Unity +Z — the prefab's own
forward — and `SURF_WallGrid` lands at Unity local (0, 1.71, 0).

Read-only, like every export here: no `save_mainfile` anywhere below, and the
object deletion happens in the in-memory copy.
"""

import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import unity_path, _localise_materials  # noqa: E402

SRC = os.path.join(HERE, "inventory_wall.blend")
COLLECTION = "Coll_InventoryWall"
DST = unity_path("Props", "inventory_wall.fbx")


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s" % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    # FIRST. A linked material does not survive into the FBX, and the model
    # arrives untextured with nothing in the log to say why.
    localised = _localise_materials()

    coll = bpy.data.collections.get(COLLECTION)
    if coll is None:
        raise SystemExit("No collection %r in %s" % (COLLECTION, SRC))

    keep = {o.name for o in coll.all_objects}
    for obj in list(bpy.data.objects):
        if obj.name not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)

    objects = list(bpy.data.objects)
    bpy.context.view_layer.update()
    for obj in objects:
        obj.select_set(True)

    meshes = [o for o in objects if o.type == 'MESH']
    empties = [o for o in objects if o.type == 'EMPTY']
    tris = sum(sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
               for o in meshes)

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=True,
        object_types={"EMPTY", "MESH"},
        apply_scale_options='FBX_SCALE_NONE',
        global_scale=1.0,
        apply_unit_scale=True,
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

    print("EXPORTED %d mesh(es) %d empt(ies) %d tri(s) %d mat(s) localised -> %s"
          % (len(meshes), len(empties), tris, localised, DST))

    for obj in sorted(empties, key=lambda o: o.name):
        w = obj.matrix_world.to_translation()
        print("    %-16s blender (%7.4f, %7.4f, %7.4f) -> unity local "
              "(%7.4f, %7.4f, %7.4f)"
              % (obj.name, w.x, w.y, w.z, w.x, w.z, -w.y))


main()
