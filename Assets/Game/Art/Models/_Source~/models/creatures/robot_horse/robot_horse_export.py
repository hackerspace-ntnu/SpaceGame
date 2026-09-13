"""Export robot_horse.blend to Unity, rig and clips included.

    blender --background --python Assets/Game/Art/Models/_Source~/models/creatures/robot_horse/robot_horse_export.py

Writes Assets/Game/Art/Models/Creatures/Robotic/RobotHorse/robot_horse.fbx. Never writes the
.blend. Every action becomes its own take, named `Arm_RobotHorse|RobotHorse_<Clip>`, and the
Unity builder (RobotHorseBuilder.cs) crops each take to its own frame count.

Two things happen in memory before the write:

  * The armature's object transform is applied, so the horse's origin is on the ground
    between its hooves rather than 1.36 m up at the pelvis where the author left the object.
    Every agent prefab here is authored soles-at-pivot.
  * The scene frame range is set to the longest clip, which is what the exporter bakes when
    it is told to bake every action (ArtPipeline.md: the take is otherwise cut to Blender's
    1..250 default, which would pad every clip with dead frames).
"""
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.abspath(os.path.join(HERE, "..", "..", "..")))
import _exportlib  # noqa: E402

SRC = os.path.join(HERE, "robot_horse.blend")
DST = _exportlib.unity_path("Creatures", "Robotic", "RobotHorse", "robot_horse.fbx")
ARM = "Arm_RobotHorse"


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s -- run robot_horse_rig.py first." % SRC)
    bpy.ops.wm.open_mainfile(filepath=SRC)

    arm = bpy.data.objects.get(ARM)
    if arm is None:
        raise SystemExit("No %s in %s" % (ARM, SRC))
    actions = [a for a in bpy.data.actions if a.name.startswith("RobotHorse_")]
    if not actions:
        raise SystemExit("No RobotHorse_* actions -- run robot_horse_anim.py -- --save first.")

    # Origin to the ground.
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    longest = max(int(a.frame_range[1]) for a in actions)
    bpy.context.scene.frame_start = 0
    bpy.context.scene.frame_end = longest
    arm.animation_data.action = None

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    print("  %d mesh(es), %d bone(s), %d take(s): %s"
          % (len(meshes), len(arm.data.bones), len(actions), ", ".join(a.name for a in actions)))

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=False,
        object_types={"MESH", "ARMATURE"},
        apply_scale_options="FBX_SCALE_NONE",
        axis_forward="-Z",
        axis_up="Y",
        mesh_smooth_type="FACE",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        armature_nodetype="NULL",
        bake_space_transform=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode="COPY",
        embed_textures=False,
    )
    print("  wrote %s (%.1f MB)" % (DST, os.path.getsize(DST) / 1e6))


if __name__ == "__main__":
    main()
