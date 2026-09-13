"""Export the grapple harpoon heads from grapple_dart.blend to Unity.

All three variations ship, which is a deliberate departure from
`walking_staff_export.py`'s rule that an unreferenced FBX is not an asset. The
grappling hook is being wired by hand right now and the choice between a light
dart, the barbed hero and the industrial piton is a look-at-it-in-the-editor
decision, not one that can be made from a render. Once the choice is made,
deleting the two losers is one line here plus the FBX and its .meta.

`grapple_dart_barbed.fbx` is the intended hero.

Flags are copied from `_exportlib` for the reasons documented there; the two
that matter here are:

  * `axis_forward='-Z', axis_up='Y'` — the default conversion, which maps
    Blender `(x, y, z)` onto Unity `(x, z, −y)`. The dart is built with its tip
    down Blender −Y precisely so that it lands on Unity **+Z**, which is what
    `Quaternion.LookRotation(travelDirection)` produces. Changing either flag
    silently rotates the model out from under that code.
  * `apply_scale_options='FBX_SCALE_NONE'` — 1 Blender unit stays 1 Unity unit,
    so the 0.400 m dart arrives 0.400 m long at import scale 1 and the
    instantiated root sits at lossyScale 1. The repo's "FBXs import at
    lossyScale 100" trap belongs to assets exported some other way; nothing in
    this library has it.

The marker cubes are exported along with the mesh. Their renderers have to be
disabled on the Unity side — see the BUILD record.

Exports are the one kind of script here that is meant to be re-run: this one
only ever reads the .blend.

    blender --background --python components/props/grapple_dart_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import unity_path, _localise_materials  # noqa: E402

# (component file, collection to ship, FBX path under Assets/Game/Art/Models/)
TARGETS = [
    ("grapple_dart.blend", "Coll_GrappleDart_Barbed",
     ("Items", "grapple_dart_barbed.fbx")),
    ("grapple_dart.blend", "Coll_GrappleDart_Light",
     ("Items", "grapple_dart_light.fbx")),
    ("grapple_dart.blend", "Coll_GrappleDart_Piton",
     ("Items", "grapple_dart_piton.fbx")),
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
    # unselected mesh into the file, and this component stacks all three
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
    print("EXPORTED %-26s %d object(s) %d tri(s) %d mat(s) localised -> %s"
          % (coll_name, len(bpy.data.objects), tris, localised, dst))
    # Print the marker positions so the numbers the Unity side needs come out
    # of the export itself rather than out of a document that can go stale.
    for obj in sorted(bpy.data.objects, key=lambda o: o.name):
        if obj.name.startswith("Marker_"):
            print("    %-28s blender (%.4f, %.4f, %.4f)  ->  unity local "
                  "(%.4f, %.4f, %.4f)"
                  % (obj.name, obj.location.x, obj.location.y, obj.location.z,
                     obj.location.x, obj.location.z, -obj.location.y))


def main():
    for blend_name, coll_name, fbx_parts in TARGETS:
        export_one(blend_name, coll_name, fbx_parts)
    # Deliberately no save_mainfile: the .blend is the source of truth and this
    # script has just deleted objects out of the in-memory copy.


main()
