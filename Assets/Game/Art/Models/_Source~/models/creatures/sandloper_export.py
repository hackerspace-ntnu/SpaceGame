"""Export sandloper.blend to the FBX Unity consumes.

    blender --background --python models/creatures/sandloper_export.py

Re-runnable, and it never writes to the .blend it opens.

The options mirror `dune_rat_export.py` because the rig IS the dune rat's --
same 40 bones, same IK-solved limbs, same six actions. The two notes that
matter there matter identically here:

  * **Leaf bones off.** Blender otherwise adds a bone past every chain tip,
    which is how this skeleton acquired the fifteen `*_end` bones the rat's rig
    script had to strip.

  * **Takes are baked from the EVALUATED pose.** All four limbs are driven by
    IK and the femur/fibula/humerus/radius carry no curves at all; what lands
    in the FBX is the solver's output sampled per frame. If the bake stopped
    evaluating constraints the legs would arrive stone still.

Unlike the rat, the materials here are already local -- `sandloper.py` creates
them rather than linking `palette.blend`, which Blender 4.2 cannot open -- so
there is no material localisation step.
"""

import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))

REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(
        os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

SRC = os.path.join(HERE, "sandloper.blend")
DST = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Creatures",
                   "Organic", "Sandloper", "sandloper.fbx")

ARM = "Arm_Sandloper"


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s -- run sandloper.py first." % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()
        # A creature is opaque. The hide materials in the palette ship alpha
        # hashed, and that transparency rides through the FBX into whatever
        # material Unity builds from it.
        if hasattr(mat, "blend_method"):
            mat.blend_method = 'OPAQUE'

    arm = bpy.data.objects[ARM]
    actions = sorted(a.name for a in bpy.data.actions)
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    tris = sum(sum(max(0, len(p.vertices) - 2) for p in o.data.polygons) for o in meshes)

    print("Exporting %d mesh(es), %d tris, %d bones, %d takes"
          % (len(meshes), tris, len(arm.data.bones), len(actions)))
    print("  takes: %s" % ", ".join(actions))

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=False,
        object_types={'MESH', 'ARMATURE'},
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        mesh_smooth_type='FACE',
        use_mesh_modifiers=False,
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        armature_nodetype='NULL',
        bake_space_transform=False,
        path_mode='AUTO',
        embed_textures=False,
    )
    print("Wrote %s (%.2f MB)" % (DST, os.path.getsize(DST) / 1e6))


main()
