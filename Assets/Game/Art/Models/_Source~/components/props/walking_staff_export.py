"""Export the Nomad's stave from walking_staff.blend to Unity.

Only ONE of the four variations ships. The component file is the library's
copy and keeps the whole family; Unity gets what something in the game actually
references, because an unreferenced FBX is not an asset, it is a file the next
person has to work out whether they can delete.

Adding another is one line in TARGETS — that is the point of keeping the other
three in the .blend rather than exporting them speculatively.

Flags are copied from `_exportlib` for the reasons documented there. `Weapons/`
rather than `Props/` because the game reads this as the Nomad's melee weapon,
and that is where someone looking for it will look; the library still files it
under components/props, where it belongs as a component.

Exports are the one kind of script here that is meant to be re-run: it only
ever reads the .blend.

    blender --background --python components/props/walking_staff_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import unity_path, _localise_materials  # noqa: E402

# (component file, collection to ship, FBX path under Assets/Game/Art/Models/)
TARGETS = [
    ("walking_staff.blend", "Coll_Staff_Nomad",
     ("Weapons", "WalkingStaff", "walking_staff.fbx")),
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
    # unselected mesh into the file, and this component stacks all four
    # variations on the same origin.
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
    print("EXPORTED %-24s %d mesh(es) %d tri(s) %d mat(s) localised -> %s"
          % (coll_name, len(bpy.data.objects), tris, localised, dst))


def main():
    for blend_name, coll_name, fbx_parts in TARGETS:
        export_one(blend_name, coll_name, fbx_parts)
    # Deliberately no save_mainfile: the .blend is the source of truth and this
    # script has just deleted objects out of the in-memory copy.


main()
