"""Shared FBX export for the model library.

Exports are the one kind of script here that is *meant to be re-run*. Generators
are not: the .blend is the source of truth and may carry hand edits that exist
nowhere else, so `_buildlib.start()` refuses to overwrite one. An export only
ever reads, so it can run as often as the model changes.

This module exists because `ship_rv_export.py` and `desert_crawler_export.py`
had already grown two divergent copies of the same twelve export flags, and the
buildings needed a third. The flags are not arbitrary — each one is load-bearing:

  * **Palette materials get localised.** Models link their materials from
    `_Source~/palette.blend`, which lives outside `Assets/` as far as Unity's
    importer is concerned. A linked reference does not survive into the FBX;
    without `make_local()` the meshes arrive untextured.
  * **Default axis conversion** (`-Z` forward, `Y` up). Blender's −Y forward
    lands on Unity's +Z. Everything already in the project was exported this
    way, so changing it would silently rotate the new assets relative to the
    old ones.
  * **`FBX_SCALE_NONE`** keeps 1 Blender unit = 1 Unity unit, matching the
    library's metric convention.
  * **`add_leaf_bones=False`** when a rig is kept. Blender otherwise appends a
    `<bone>_end` child to every chain tip, which shows up as a real transform
    in Unity and breaks any code that walks a bone's children by index or by
    name prefix.

Import it by path — these scripts run under `blender --background`, where the
library root is not on `sys.path`:

    import sys, os
    sys.path.insert(0, "<...>/Assets/Game/Art/Models/_Source~")
    from _exportlib import export
"""

import os

import bpy

LIB_ROOT = os.path.dirname(os.path.abspath(__file__))


def repo_root(start=None):
    """The Unity project root — the folder holding ProjectSettings.

    Found by walking up rather than by counting `os.path.dirname()` calls, for
    the same reason `_buildlib.repo_root` does: the library has already moved
    once, from `<repo>/models` to `<repo>/Assets/Game/Art/Models/_Source~`, and
    the level-counting version broke when it did.
    """
    d = os.path.dirname(os.path.abspath(start or __file__))
    while d != os.path.dirname(d):
        if os.path.isdir(os.path.join(d, "ProjectSettings")):
            return d
        d = os.path.dirname(d)
    raise SystemExit("Could not find the Unity project root above %s" % (start or __file__))


REPO_ROOT = repo_root()


def unity_path(*parts):
    """A path under `Assets/Game/Art/Models/`, the type-first game-asset tree.

    Deliberately not `Assets/Models/` — art moved under `Assets/Game/Art` in the
    domain-first restructure, and an export left on the old path writes an
    orphaned FBX that Unity never imports and nothing ever references.
    """
    return os.path.join(REPO_ROOT, "Assets", "Game", "Art", "Models", *parts)


def _localise_materials():
    n = 0
    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()
            n += 1
    return n


def _drop_armatures():
    """Un-parent meshes from the rig, in place, then delete the rig.

    Reading `matrix_world` before clearing the parent and writing it back after
    is what keeps the mesh where it was; clearing `parent` on its own drops the
    parent's transform and scatters the model.
    """
    for obj in bpy.data.objects:
        if obj.type == 'MESH' and obj.parent is not None:
            world = obj.matrix_world.copy()
            obj.parent = None
            obj.matrix_world = world
    rigs = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    for obj in rigs:
        bpy.data.objects.remove(obj, do_unlink=True)
    return len(rigs)


def _keep_only(names):
    """Delete every object except `names`. Returns how many were dropped.

    Component files hold several VARIATIONS of one thing stacked at the origin,
    which is right for the library and useless as an FBX — exported whole, the
    three rocket variations arrive in Unity as one interpenetrating lump. A
    model file needs no filter and passes none.
    """
    wanted = set(names)
    missing = wanted - {o.name for o in bpy.data.objects}
    if missing:
        raise SystemExit("Not in the file: %s" % ", ".join(sorted(missing)))

    doomed = [o for o in bpy.data.objects if o.name not in wanted]
    for obj in doomed:
        bpy.data.objects.remove(obj, do_unlink=True)
    return len(doomed)


def export(src, dst, keep_armature=False, keep=None):
    """Open `src`, export it to `dst`, and never write back to `src`.

    `keep_armature` is the one real decision per model. Keep the rig when
    something in Unity drives the bones or reads the hierarchy; drop it when the
    model is static set dressing, where a rig is dead weight, or when a builder
    script reparents the meshes itself and a bone hierarchy would get in the way
    (see `ship_rv_export.py`).

    `keep` names the objects to ship when the source is a COMPONENT file rather
    than a model — see `_keep_only`. Omit it for a model file, whose objects are
    already exactly the model.
    """
    if not os.path.exists(src):
        raise SystemExit("No model at %s" % src)

    bpy.ops.wm.open_mainfile(filepath=src)

    if keep is not None:
        print("  keeping %d object(s), dropped %d other variation object(s)"
              % (len(keep), _keep_only(keep)))

    localised = _localise_materials()

    types = {'MESH'}
    if keep_armature:
        types.add('ARMATURE')
        rigs = [o for o in bpy.data.objects if o.type == 'ARMATURE']
        bones = sum(len(a.data.bones) for a in rigs)
    else:
        dropped = _drop_armatures()
        rigs, bones = [], 0

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    tris = sum(sum(max(0, len(p.vertices) - 2) for p in o.data.polygons) for o in meshes)

    print("  %d mesh(es), %d tri(s) pre-modifier, %d material(s) localised"
          % (len(meshes), tris, localised))
    if keep_armature:
        print("  keeping %d armature(s), %d bone(s)" % (len(rigs), bones))
    else:
        print("  dropped %d armature(s); meshes flattened in place" % dropped)

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=False,
        object_types=types,
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        armature_nodetype='NULL',
        bake_space_transform=False,
        path_mode='COPY',
        embed_textures=False,
    )
    print("  wrote %s (%.1f MB)" % (dst, os.path.getsize(dst) / 1e6))
    # Deliberately no save_mainfile: the .blend is the source of truth.
