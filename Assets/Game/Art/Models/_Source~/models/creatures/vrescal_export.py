"""Export vrescal.blend to the FBX Unity consumes.

Meant to be re-run -- it is an export, not a generator, and it never writes to
the .blend it opens.

Three things it does that a plain export would not:

  * **Shrinks the sculpt to shipping size.** The .blend is worked at 19.94 units
    nose to tail because that is the size it was hand-modelled at; the animal
    ships at 5.5 m. Rescaling the .blend would move every vertex of the author's
    sculpt, so the factor is applied here instead and the source stays exactly
    as modelled.

  * **Moves the pivot to under the ribcage, on the ground.** In the .blend the
    origin is at the *head*, because that is where the sculpt was started, and
    the soles are at z = -3.35. Exported as-is, a NavMeshAgent would steer the
    creature's nose to the destination and drag five metres of body behind it.
    `PIVOT` puts the origin under the middle of the trunk instead, between the
    shoulder and the hip, with y = 0 at the soles.

  * **Turns +X forward into -Y forward.** The sculpt faces +X, the library
    convention is -Y, and the FBX axis conversion below maps -Y onto Unity's
    +Z. Without the extra yaw the creature arrives in Unity walking sideways.

All three are applied by transforming `Arm_Vrescal` itself. After
`vrescal_anim.py` the armature is the only root object -- every mesh, body and
limb alike, is parented to a bone -- so moving it moves the whole animal, and
the bone animation is in armature-local space and does not care.

Do **not** reintroduce a parent empty to carry this. That was the first attempt
and it fails silently: Blender's FBX exporter drops the empty and the model
arrives in Unity unrotated, lying on its side with its length along X, with no
error anywhere to say so.

It also localises the palette materials, which are linked from
`Assets/Game/Art/Models/_Source~/palette.blend` -- outside `Assets/`, so the
reference would not resolve from a copy inside it -- and turns off leaf bones,
so the exported skeleton is exactly the bones named in `vrescal_legs.py`.

## Animation

Every action in the .blend is baked as its own FBX take, named
`Arm_Vrescal|Vrescal_Walk` and so on. `VrescalBuilder` on the Unity side slices
those takes into clips and sets the loop flags; the take names are the contract
between the two, so renaming an action means updating `CLIPS` in that file.

The clips are all **in place** -- no forward translation on the root. Movement
comes from `NavMeshAgentMotor`, and a clip that also walked the creature
forward would fight it. That is also why the prefab keeps
`m_ApplyRootMotion: 0`.

    blender --background --python vrescal_export.py
"""

import math
import os

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))

# Walk up to the Unity project root rather than counting parent directories, so
# this survives the library being moved (it already moved once, into Assets/).
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(
        os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

SRC = os.path.join(HERE, "vrescal.blend")
DST = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Creatures",
                   "Organic", "Vrescal", "vrescal.fbx")

SHIP_LENGTH = 8.31      # metres, nose to tail tip
SCULPT_LENGTH = 30.11   # working units, measured off the .blend
GROUND = -13.0          # the sole plane in the .blend, from vrescal_rebuild.py

# The point in the .blend that becomes Unity's origin: on the sole plane, under
# the middle of the trunk, between the shoulder (x = -11.8) and the hip
# (x = -21.6). Same value as `vrescal_rebuild.PIVOT_X`, which is where
# `Bone_Root` sits -- they have to agree or the root bone is not at the origin.
PIVOT = (-16.20, 0.0, GROUND)


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s" % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    factor = SHIP_LENGTH / SCULPT_LENGTH

    roots = [o for o in bpy.data.objects
             if o.parent is None and o.type in {'MESH', 'ARMATURE'}]
    if [o.name for o in roots] != ["Arm_Vrescal"]:
        raise SystemExit(
            "Expected Arm_Vrescal to be the only root object, found %s.\n"
            "Run vrescal_anim.py, which parents the sculpted body to the rig; "
            "anything still unparented would be left behind at sculpt scale "
            "and orientation." % ", ".join(sorted(o.name for o in roots)))

    # world' = scale . yaw . (world - PIVOT), applied to the one root. Reading
    # right to left: slide PIVOT onto the origin, turn +X forward into -Y
    # forward, then shrink to shipping size.
    place = (Matrix.Diagonal((factor, factor, factor, 1.0))
             @ Matrix.Rotation(math.radians(-90.0), 4, 'Z')
             @ Matrix.Translation(-Vector(PIVOT)))
    roots[0].matrix_world = place @ roots[0].matrix_world

    bpy.context.view_layer.update()

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    bones = sum(len(a.data.bones) for a in arms)
    actions = sorted(a.name for a in bpy.data.actions)
    print("Exporting %d meshes, %d bones and %d takes at %.4f scale -> %s"
          % (len(meshes), bones, len(actions), factor, DST))
    print("  takes: %s" % (", ".join(actions) or "none"))

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
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        armature_nodetype='NULL',
        bake_space_transform=False,
        path_mode='COPY',
        embed_textures=False,
    )
    print("Wrote %s (%.2f MB)" % (DST, os.path.getsize(DST) / 1e6))
    # Deliberately no save_mainfile: the .blend is the source of truth.


main()
