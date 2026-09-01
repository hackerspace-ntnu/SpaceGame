"""Export the expedition rig and the five pack holders to Unity.

    blender --background --python components/props/expedition_rig_export.py

Two collections' worth of art out of two .blend files:

    expedition_rig.blend   Coll_Rig_Expedition   -> Props/expedition_rig.fbx
    pack_holders.blend     Coll_Holder_*         -> Props/pack_holders.fbx

Exports are the one kind of script in this library that is meant to be re-run.
This one only ever READS the .blend files; there is no `save_mainfile` anywhere
below, and the object surgery in `_stand_up()` happens in the in-memory copy.

Why this is not `_exportlib.export()`
-------------------------------------------------------------------------------
`export()` passes `object_types={'MESH'}`, which would silently drop every
`PIVOT_` and `SURF_` empty in the rig and every `HARD_` empty in the holders —
and those empties are the entire point of both files. `PIVOT_*` is what
`BackpackObject` swings, `SURF_*` is what `PackSurface` sits on, and `HARD_*` is
what `HolderBuilder.CounterScaleHardware` walks. An FBX of meshes alone imports
looking perfect and is functionally inert.

So the flags below are copied out of `_exportlib` — each one is load-bearing and
the reasons are in that module's docstring — with `object_types` widened to
`{"EMPTY", "MESH"}`.

`_localise_materials()` runs FIRST, before anything else touches the file. Both
models LINK their materials from `_Source~/palette.blend`, which lives outside
Unity's view; a linked reference does not survive into an FBX and the meshes
arrive untextured. That failure looks like a shader problem, not an export one.

The destination comes from `_exportlib.unity_path()` rather than from counting
`..` segments, so the library can move again without orphaning the write.

The one departure: the holders export on IDENTITY axes
-------------------------------------------------------------------------------
`expedition_rig.blend` is an ordinary Blender Z-up model — bounds z -0.081 ..
0.738, which is its standing height — so the library's usual `-Z` forward /
`Y` up conversion is exactly right for it and it uses it.

`pack_holders.blend` is not a Z-up model. Its authoring rule (see the header of
`pack_holders.py`) is stated in the frame `HolderBuilder` works in — `+X` the
stretch axis, `+Z` across, `+Y` up, 0..1 — and the .blend takes that literally,
in BLENDER coordinates: measured off the file, every holder spans about ±0.5 in
X and Z and **0 .. 1 in Y**, with `HARD_Clip_Hook` at y = 1.01. It is the Unity
frame, sitting in a Blender file. So it wants no conversion at all, and
`axis_forward='-Y', axis_up='Z'` — Blender's own axes — is how you ask for none.

Why the conversion cannot simply be left on, or worked around by rotating the
model before export: Blender bakes the axis conversion into the transform of
every ROOT object, so a root that was at identity arrives in Unity at euler
(270, 0, 0). That is not a guess — it is what `PIVOT_Clamshell` did on the last
pack out of this library, and the rig below still does it. On the rig it costs
nothing, because `BackpackObject` turns its hinges RELATIVE to whatever rest
rotation the FBX hands back. On a holder it is fatal twice over:

  * `HolderBuilder` sets the holder root's rotation itself, to the surface's
    orientation. Any correction living on that transform is overwritten, and
    the holder lands on its face.
  * `CounterScaleHardware` divides each `HARD_` subtree by the componentwise
    reciprocal of the fit. A componentwise reciprocal only inverts a
    non-uniform scale when the child's axes line up with the frame the scale
    was applied in. With 90 degrees between them, height and width swap: every
    buckle on a 1.35 m staff comes out the wrong size on two axes, and it looks
    like a modelling mistake rather than an export one.

Both of those need the same thing — **zero rotation anywhere between the prefab
root and the `HARD_` empties, with the geometry already in Unity's frame** — and
the only way to get it is for the file's own coordinates to be the final ones.
Hence identity axes for this one file, and a check in
`ExpeditionRigWiring.cs` that the imported holder roots really did arrive
unrotated, since nothing else downstream would notice if they had not.
"""

import os
import sys

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))

from _exportlib import unity_path, _localise_materials  # noqa: E402


# The library default: a Z-up Blender model, converted on the way out.
BLENDER_TO_UNITY = ('-Z', 'Y')

# No conversion. For a file whose coordinates ARE Unity's already.
AS_AUTHORED = ('-Y', 'Z')

# (component file, collections to ship, FBX path parts under Models/, axes)
TARGETS = [
    ("expedition_rig.blend", ["Coll_Rig_Expedition"],
     ("Props", "expedition_rig.fbx"), BLENDER_TO_UNITY),
    ("pack_holders.blend",
     ["Coll_Holder_Cord", "Coll_Holder_Webbing", "Coll_Holder_Bungee",
      "Coll_Holder_Sleeve", "Coll_Holder_Clip"],
     ("Props", "pack_holders.fbx"), AS_AUTHORED),
]


def _bounds(objects):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))

    for obj in objects:
        if obj.type != 'MESH':
            continue
        for corner in obj.bound_box:
            world = obj.matrix_world @ Vector(corner)
            for i in range(3):
                lo[i] = min(lo[i], world[i])
                hi[i] = max(hi[i], world[i])

    return lo, hi


def export_one(blend_name, coll_names, fbx_parts, axes):
    src = os.path.join(HERE, blend_name)
    if not os.path.exists(src):
        raise SystemExit("No component at %s" % src)

    bpy.ops.wm.open_mainfile(filepath=src)

    # FIRST. A linked material does not survive into the FBX, and the model
    # arrives untextured with nothing in the log to say why.
    localised = _localise_materials()

    keep = set()
    for coll_name in coll_names:
        coll = bpy.data.collections.get(coll_name)
        if coll is None:
            raise SystemExit("No collection %r in %s" % (coll_name, blend_name))
        # `all_objects`, not `objects`: a nested collection's contents are not
        # in the latter, and dropping them would delete half the model.
        keep.update(o.name for o in coll.all_objects)

    # Dropped outright rather than merely deselected: `use_selection` alone
    # still lets a parent or a modifier pull an unselected object into the file.
    for obj in list(bpy.data.objects):
        if obj.name not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)

    objects = list(bpy.data.objects)
    bpy.context.view_layer.update()

    for obj in objects:
        obj.select_set(True)

    meshes = [o for o in objects if o.type == 'MESH']
    empties = [o for o in objects if o.type == 'EMPTY']
    tris = sum(sum(max(0, len(p.vertices) - 2) for p in o.data.polygons) for o in meshes)

    dst = unity_path(*fbx_parts)
    os.makedirs(os.path.dirname(dst), exist_ok=True)

    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=True,
        # The whole reason this script exists instead of _exportlib.export().
        object_types={"EMPTY", "MESH"},
        apply_scale_options='FBX_SCALE_NONE',
        global_scale=1.0,
        apply_unit_scale=True,
        axis_forward=axes[0],
        axis_up=axes[1],
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        bake_space_transform=False,
        path_mode='COPY',
        embed_textures=False,
    )

    lo, hi = _bounds(objects)
    converted = axes == BLENDER_TO_UNITY

    print("EXPORTED %-22s %2d mesh(es) %2d empt(ies) %6d tri(s) %d mat(s) "
          "localised  axes %s/%s%s -> %s"
          % (blend_name, len(meshes), len(empties), tris, localised,
             axes[0], axes[1], "" if converted else "  (AS AUTHORED)", dst))
    print("    blender bounds  x[%7.3f %7.3f]  y[%7.3f %7.3f]  z[%7.3f %7.3f]"
          % (lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))

    # The names Unity binds to, printed so a rename in the .blend shows up here
    # rather than as a null reference in the wiring script.
    for obj in sorted(empties, key=lambda o: o.name):
        if not obj.name.startswith(("PIVOT_", "SURF_", "Holder_")):
            continue

        world = obj.matrix_world.to_translation()
        unity = (world.x, world.z, -world.y) if converted else tuple(world)

        print("    %-16s blender (%7.4f, %7.4f, %7.4f) -> unity local "
              "(%7.4f, %7.4f, %7.4f)"
              % ((obj.name, world.x, world.y, world.z) + unity))


def main():
    for blend_name, coll_names, fbx_parts, axes in TARGETS:
        export_one(blend_name, coll_names, fbx_parts, axes)
    # Deliberately no save_mainfile: the .blend is the source of truth and this
    # script has just deleted objects out of the in-memory copy.


main()
