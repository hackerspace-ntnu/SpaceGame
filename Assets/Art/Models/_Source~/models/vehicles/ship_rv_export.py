"""Export ship_rv.blend to the FBX that ShipRVBuilder consumes.

Unlike the other scripts in this library this one is meant to be re-run: it is
an export, not a generator. It never writes to the .blend it opens.

Three things it does that a plain FBX export would not:

  * Localises the palette materials. The model links them from
    Assets/Art/Models/_Source~/palette.blend, which sits outside Assets/ and would not resolve from
    a copy inside it.
  * Un-parents the meshes from the armature and drops the rig. ShipRVBuilder
    finds parts with Transform.Find, which only searches direct children, and
    it reparents meshes into its own hinge pivots anyway. A bone hierarchy in
    the FBX would break the first and be redundant against the second — the
    armature stays in the .blend for animating there.
  * Exports with Blender's default axis conversion, so the nose still arrives
    along Unity's +X and the builder's existing 90-degree ModelYaw still lands
    it on +Z.

    blender --background --python ship_rv_export.py
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
# Walk up to the Unity project root rather than counting parent directories, so
# this survives the library being moved (it already moved once, into Assets/).
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)
SRC = os.path.join(HERE, "ship_rv.blend")
DST = os.path.join(REPO, "Assets", "Models", "Vehicles", "RV", "ship_rv.fbx")


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s — run ship_rv.py first." % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    # Flatten: keep every mesh exactly where it is, then discard the rig.
    for obj in bpy.data.objects:
        if obj.type == 'MESH' and obj.parent is not None:
            world = obj.matrix_world.copy()
            obj.parent = None
            obj.matrix_world = world
    for obj in [o for o in bpy.data.objects if o.type == 'ARMATURE']:
        bpy.data.objects.remove(obj, do_unlink=True)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    print("Exporting %d meshes -> %s" % (len(meshes), DST))

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=False,
        object_types={'MESH'},
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=True,
        bake_space_transform=False,
        path_mode='COPY',
        embed_textures=False,
    )
    print("Wrote %s" % DST)
    # Deliberately no save_mainfile: the .blend is the source of truth.


main()
