"""Export humanoid_robot.blend to the FBX Unity consumes.

Meant to be re-run -- it is an export, not a generator, and it never writes to
the .blend it opens.

**It keeps the armature.** `WalkerRig.Build` walks the LIVE bone hierarchy every
time the machine initialises: legs by the `Coxa_/Hip_/Knee_/Ankle_/Foot_` names,
arms by following pins down from each `Arm_` root. Strip the armature and there
is no humanoid, just a pile of limbs.

Three things it does that a plain export would not:

  * **Localises the palette materials.** The model links them from
    `Assets/Game/Art/Models/_Source~/palette.blend`, outside `Assets/`, which would not resolve from a
    copy inside it.
  * **Turns off leaf bones.** Blender otherwise appends a `<bone>_end` child to
    every chain tip. `WalkerRig.IsJoint` skips `_end` explicitly, but the arms
    are discovered by WALKING the hierarchy and a leaf bone arriving ahead of
    the real pin is exactly the class of silent wrong answer that costs a day.
  * **Exports on the default axis conversion**, so the model's -Y forward
    arrives on Unity's +Z. `LeggedLocomotion` sorts legs by `HomeLocal.x`/`.z`
    in Unity space and `AlternatingGait` alternates them off that order, so this
    is what makes left and right come out as left and right.

    blender --background --python humanoid_robot_export.py
"""

import os

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
# Walk up to the Unity project root rather than counting parent directories, so
# this survives the library being moved (it already moved once, into Assets/).
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)
SRC = os.path.join(HERE, "humanoid_robot.blend")
DST = os.path.join(REPO, "Assets", "Models", "Creatures", "Robotic",
                   "Humanoid", "humanoid_robot.fbx")


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s -- run humanoid_robot.py first." % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    bones = sum(len(a.data.bones) for a in arms)
    print("Exporting %d meshes and %d bone(s) across %d armature(s) -> %s"
          % (len(meshes), bones, len(arms), DST))

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=False,
        object_types={'MESH', 'ARMATURE'},
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
    print("Wrote %s (%.1f MB)" % (DST, os.path.getsize(DST) / 1e6))
    # Deliberately no save_mainfile: the .blend is the source of truth.


main()
