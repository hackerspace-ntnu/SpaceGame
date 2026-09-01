"""Export `Coll_GrappleHarpoon` from grapple_dart.blend to Unity.

    blender --background --python components/props/grapple_harpoon_export.py

    -> Assets/Game/Art/Models/Items/grapple_harpoon.fbx

## Why this is a separate file from `grapple_dart_export.py`

They read the same .blend, and the obvious move is a fourth entry in that
script's `TARGETS`. It is the wrong move: running it would rewrite
`grapple_dart_barbed.fbx`, `_light.fbx` and `_piton.fbx` as a side effect of
shipping the harpoon. `grapple_dart_barbed.fbx` is wired into the grappling
hook prefab, the repo has another session editing that prefab right now, and
an FBX rewrite that changes nothing visible still shows up as a diff and still
makes Unity reimport the asset the other session is holding.

One collection in, one FBX out, nothing else touched. Add a `TARGETS` entry to
`grapple_dart_export.py` later if the darts and the harpoon ever need to ship
together.

## Flags

Copied from `_exportlib` and from `grapple_dart_export.py`, for the reasons
documented there. The two that matter:

  * `axis_forward='-Z', axis_up='Y'` — the default conversion, which maps
    Blender `(x, y, z)` onto Unity `(x, z, −y)`. The harpoon is built with its
    tip down Blender −Y precisely so it lands on Unity **+Z**, which is what
    `Quaternion.LookRotation(travelDirection)` produces. Changing either flag
    silently rotates the model out from under the grappling hook's C#.
  * `apply_scale_options='FBX_SCALE_NONE'` — 1 Blender unit stays 1 Unity
    unit, so the 0.900 m harpoon arrives 0.900 m long at import scale 1 and
    the instantiated root sits at lossyScale 1.

The two marker cubes export with the mesh. Their renderers are disabled on the
Unity side — see `grapple_dart_BUILD.md`.

Exports are the one kind of script in this library that is meant to be re-run:
this one only ever reads the .blend.
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import unity_path, _localise_materials  # noqa: E402

BLEND = "grapple_dart.blend"
COLL = "Coll_GrappleHarpoon"
FBX = ("Items", "grapple_harpoon.fbx")


def main():
    src = os.path.join(HERE, BLEND)
    if not os.path.exists(src):
        raise SystemExit("No component at %s" % src)

    bpy.ops.wm.open_mainfile(filepath=src)
    localised = _localise_materials()

    coll = bpy.data.collections.get(COLL)
    if coll is None:
        raise SystemExit("No collection %r in %s" % (COLL, BLEND))

    # Drop every other variation outright rather than merely deselecting it:
    # `use_selection` alone still lets a stray modifier or parent pull an
    # unselected mesh into the file, and all four variations of this component
    # sit on the same origin.
    keep = {o.name for o in coll.objects}
    for obj in list(bpy.data.objects):
        if obj.name not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)

    for obj in bpy.data.objects:
        obj.select_set(True)

    tris = sum(sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
               for o in bpy.data.objects if o.type == 'MESH')

    dst = unity_path(*FBX)
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
    print("EXPORTED %-24s %d object(s) %d tri(s) %d mat(s) localised -> %s"
          % (COLL, len(bpy.data.objects), tris, localised, dst))
    # Print the marker positions so the numbers the Unity side needs come out
    # of the export itself rather than out of a document that can go stale.
    for obj in sorted(bpy.data.objects, key=lambda o: o.name):
        if obj.name.startswith("Marker_"):
            print("    %-28s blender (%.4f, %.4f, %.4f)  ->  unity local "
                  "(%.4f, %.4f, %.4f)"
                  % (obj.name, obj.location.x, obj.location.y, obj.location.z,
                     obj.location.x, obj.location.z, -obj.location.y))
    # Deliberately no save_mainfile: the .blend is the source of truth and this
    # script has just deleted objects out of the in-memory copy.


main()
