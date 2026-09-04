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
  * **`fix_inverted`** (opt-in) bakes out a negative-determinant transform and
    recalculates normals, in memory only. A hand edit that drags a scale gizmo
    past zero leaves the object mirrored; Blender draws it correctly and Unity
    renders it inside-out, with nothing anywhere reporting a problem.
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

import bmesh
import bpy
from mathutils import Matrix

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


def _unmirror():
    """Bake out any transform that INVERTS handedness, and fix the winding.

    An object scaled negatively on one axis — the usual way a hand edit ends up
    mirrored, e.g. dragging a scale gizmo past zero — has a negative-determinant
    matrix. Blender still draws it correctly because the viewport respects the
    flip, but the FBX carries the negative scale straight through and Unity
    renders the mesh inside-out: you see its back faces and look through the
    front of it. Nothing errors, and it looks fine right up until it is in the
    game.

    Detected by the determinant, not by eye, and confirmed by the mesh's
    world-space signed volume going negative. The fix is to apply the transform
    to the mesh data and recalculate normals outward — the world geometry is
    identical afterwards, only the winding is repaired.

    Opt-in via `export(fix_inverted=True)`, so no model that already ships is
    changed by this existing. It mutates the in-memory scene only; `export`
    never writes back to the .blend.
    """
    fixed = []
    for obj in [o for o in bpy.data.objects if o.type == 'MESH']:
        if obj.matrix_world.to_3x3().determinant() >= 0.0:
            continue
        if obj.data.users > 1:          # never mutate a mesh two objects share
            obj.data = obj.data.copy()
        obj.data.transform(obj.matrix_world)
        obj.matrix_world = Matrix.Identity(4)
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(obj.data)
        bm.free()
        fixed.append(obj.name)
    return fixed


def export(src, dst, keep_armature=False, keep=None, keep_empties=False,
           fix_inverted=False):
    """Open `src`, export it to `dst`, and never write back to `src`.

    `keep_armature` is the one real decision per model. Keep the rig when
    something in Unity drives the bones or reads the hierarchy; drop it when the
    model is static set dressing, where a rig is dead weight, or when a builder
    script reparents the meshes itself and a bone hierarchy would get in the way
    (see `ship_rv_export.py`).

    `keep` names the objects to ship when the source is a COMPONENT file rather
    than a model — see `_keep_only`. Omit it for a model file, whose objects are
    already exactly the model.

    `keep_empties` ships the file's empties as well. Off by default because an
    empty in a model file is usually a build helper, not a socket; turn it on
    when Unity reads one by reference — a muzzle, a hinge pivot, a seat — the
    way `ruin_scanner_export.py` ships its `Emitter`.

    `fix_inverted` bakes out negative-determinant transforms and repairs the
    winding — see `_unmirror`. Off by default so nothing already shipping
    changes; turn it on for a file whose hand edits left an object mirrored.
    """
    if not os.path.exists(src):
        raise SystemExit("No model at %s" % src)

    bpy.ops.wm.open_mainfile(filepath=src)

    if keep is not None:
        print("  keeping %d object(s), dropped %d other variation object(s)"
              % (len(keep), _keep_only(keep)))

    if fix_inverted:
        unmirrored = _unmirror()
        if unmirrored:
            print("  un-mirrored %d inside-out object(s): %s"
                  % (len(unmirrored), ", ".join(sorted(unmirrored))))

    localised = _localise_materials()

    types = {'MESH'}
    if keep_empties:
        types.add('EMPTY')
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
    if keep_empties:
        empties = [o.name for o in bpy.data.objects if o.type == 'EMPTY']
        print("  keeping %d empt(ies): %s" % (len(empties), ", ".join(sorted(empties))))
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


def to_unity(v):
    """Blender `(x, y, z)` as it arrives in Unity through `export`'s flags:
    `(−x, z, −y)`. The X flip is the handedness change and was measured on an
    asymmetric model in `grapple_bracer_BUILD.md`; the dart family, being
    symmetric about x = 0, could not tell and documents it without the flip."""
    return (-v[0], v[2], -v[1])


def describe(worn_scale=None):
    """Print every pivot and the whole model's bounds, in both frames.

    Run after `export`, on the still-open file. A prefab wires pivots by
    serialized reference and needs to know where each landed, and `ItemGrip`'s
    `holdSize` is the model's longest axis times the wear scale — printing both
    here beats measuring them in the editor afterwards.
    """
    objs = [o for o in bpy.data.objects if o.type in ('MESH', 'EMPTY')]
    for obj in sorted(objs, key=lambda o: o.name):
        b = obj.location
        u = to_unity(b)
        tag = "empty" if obj.type == 'EMPTY' else (
            "uv" if obj.data.uv_layers else "--")
        print("  PIVOT %-30s blender (%8.4f, %8.4f, %8.4f)  unity (%8.4f, %8.4f, %8.4f)  %s"
              % (obj.name, b.x, b.y, b.z, u[0], u[1], u[2], tag))

    meshes = [o for o in objs if o.type == 'MESH']
    pts = [obj.matrix_world @ v.co for obj in meshes for v in obj.data.vertices]
    lo = [min(p[i] for p in pts) for i in range(3)]
    hi = [max(p[i] for p in pts) for i in range(3)]
    size = [hi[i] - lo[i] for i in range(3)]
    ulo, uhi = to_unity(lo), to_unity(hi)
    print("  BOUNDS blender min (%.4f, %.4f, %.4f) max (%.4f, %.4f, %.4f)"
          % (lo[0], lo[1], lo[2], hi[0], hi[1], hi[2]))
    print("  BOUNDS unity   min (%.4f, %.4f, %.4f) max (%.4f, %.4f, %.4f)"
          % (min(ulo[0], uhi[0]), min(ulo[1], uhi[1]), min(ulo[2], uhi[2]),
             max(ulo[0], uhi[0]), max(ulo[1], uhi[1]), max(ulo[2], uhi[2])))
    print("  BOUNDS size (%.4f, %.4f, %.4f) — longest %.4f"
          % (size[0], size[1], size[2], max(size)))
    if worn_scale is not None:
        print("  holdSize for a %.1fx wear = %.4f" % (worn_scale, max(size) * worn_scale))
