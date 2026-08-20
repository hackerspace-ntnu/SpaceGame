"""Export golem.blend to the FBX Unity consumes.

Meant to be re-run -- it is an export, not a generator, and it never writes to
the .blend it opens.

It does four things a plain export would not, and every one of them is here
because the plain version failed *silently* -- correct .blend, broken prefab,
no error anywhere. `golem_BUILD.md` records how each was found.

  * **Shrinks the kitbash to shipping size.** The artist's FBX is 10.62 units
    tall, measured from its vertices; the golem ships at 2.60 m, so the factor
    is 0.2448. Rescaling the .blend would move every vertex of the source, so
    the factor is applied here and the source stays as assembled.

  * **Moves the pivot onto the sole plane, between the contacts.** In the
    source the origin is far off at (-20, -5, 0) and the soles are at
    z = -1.1268. Exported as-is a NavMeshAgent would steer a point in mid-air
    five metres from the creature. `PIVOT` is `Bone_Root`'s head: on the
    ground, on the centreline, midway between the fists and the feet -- the
    point a knuckle-walker actually turns about.

  * **Turns the golem to face Unity's +Z, and tips Z-up into Y-up.** The golem
    faces +Y in the source (its face is the most +Y geometry in the file).

  * **Bakes all of the above into the rig data** rather than onto the armature
    object, because Unity's FBX importer folds the armature node into the model
    root and keeps **only its scale**. See `bake_placement`.

Do **not** reintroduce a parent empty to carry any of this. Blender's FBX
exporter drops empties silently, which is the same class of failure again.

Leaf bones are off, so the exported skeleton is exactly the 19 bones named in
`golem_rig.py`.

## Animation

Every action is baked as its own take, named `Arm_Golem|Golem_Walk` and so on.
`GolemBuilder.cs` slices those takes into clips and sets the loop flags; the
take names are the contract between the two files, so renaming an action means
updating `Clips[]` there.

The clips are all **in place**. Movement comes from `NavMeshAgentMotor`, which
is also why the prefab keeps `applyRootMotion = false`.

    blender --background --python golem_export.py
"""

import math
import os
import tempfile

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))

# Walk up to the Unity project root looking for ProjectSettings/ rather than
# counting parent directories, so this survives the library being moved (it
# already moved once, into Assets/).
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(
        os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

SRC = os.path.join(HERE, "golem.blend")
DST = os.path.join(REPO, "Assets", "Game", "Art", "Models", "Creatures",
                   "Constructs", "Golem", "golem.fbx")

ARM = "Arm_Golem"

SHIP_HEIGHT = 2.60        # metres, sole plane to the top of the back
# Working units, measured from the raw FBX's *vertices*. Not from `bound_box`:
# that returns the eight corners of each object's local AABB, and most of these
# rocks carry a rotation, so transforming those corners to world space and
# taking min/max inflates the figure -- it reads 12.09 rather than 10.62, and
# the golem would have shipped 12% short.
SOURCE_HEIGHT = 10.623
GROUND = -1.1268          # the sole plane in the source: the underside of the fists

# On the ground, on the body centreline, midway between the fists (y = -2.3) and
# the feet (y = -8.3). This is `Bone_Root`'s head, deliberately.
PIVOT = (-20.20, -5.30, GROUND)


def action_fcurves(act):
    """Every F-curve in an action, on both the old and the slotted API.

    Blender 5.1 dropped `Action.fcurves`; curves now live under
    layers > strips > channelbags. This walks whichever one exists rather than
    pinning the library to a Blender version.
    """
    if hasattr(act, "fcurves"):
        return list(act.fcurves)
    out = []
    for layer in getattr(act, "layers", []):
        for strip in getattr(layer, "strips", []):
            for bag in getattr(strip, "channelbags", []):
                out.extend(bag.fcurves)
    return out


def rest_world(arm, o):
    """A bone-parented object's rest-pose world matrix, from data alone.

    `o.matrix_world` cannot be used anywhere downstream of `bake_placement`,
    because the thing that needs measuring is precisely what the depsgraph
    fails to refresh: after `Armature.transform` it keeps handing back the
    *old* bone positions. That made the first version of the bake check report
    26 m of drift that was not there, and it made the shipping-bounds print --
    the numbers the prefab's collider is cut from -- report the creature still
    sitting at its source coordinates.

    This composes the same product Blender does (armature, bone, bone tail,
    object basis) straight off the data, and agrees with `matrix_world` to
    2e-6 when the depsgraph is clean.
    """
    bone = arm.data.bones[o.parent_bone]
    return (arm.matrix_world @ bone.matrix_local
            @ Matrix.Translation((0.0, bone.length, 0.0)) @ o.matrix_basis)


def bake_placement(arm, meshes, place, factor):
    """Move the whole creature by `place` without leaving anything on the
    armature *object*, which has to ship at identity.

    The obvious implementation -- `arm.matrix_world = place @ arm.matrix_world`
    -- produces a correct .blend and a broken prefab. Unity's FBX importer
    folds the armature node into the model root and keeps **only its scale**,
    silently discarding its translation and rotation; the first build of this
    creature came into Unity with `localScale = 24.48` on the root, the visible
    golem standing five metres from its own collider and NavMeshAgent, and no
    error anywhere to say so. `bpy.ops.object.transform_apply` is not the fix
    either: it does not compensate bone-parented children, and it dismembered
    the model by 4.6 units when tried.

    So the placement is pushed into the data by hand, in three parts:

      1. **The bones** carry the whole of `place`, so armature space becomes
         Unity space -- metres, origin at the pivot, -Y forward.
      2. **The mesh data** is scaled by the same factor, and each rock's own
         transform is re-derived so it lands back where it was. The scale has
         to leave the object transforms because every one of them must read
         1.0: `golem_rig.py` went to some trouble to get them there.
      3. **`Bone_Root`'s location F-curves** are multiplied by the factor.
         Pose-bone locations are in armature units, and armature units have
         just become metres -- without this the golem's crouch, its settle on
         every footfall and its whole death collapse would be four times too
         deep, which is the kind of thing that looks like a physics bug rather
         than a units bug.

    The bounds check at the end is what makes any of this safe to believe.
    """
    # Where every vertex *should* end up: exactly `place` applied to where it
    # is now. Checking the whole point cloud rather than a bounding box is the
    # difference between proving the creature moved and proving its silhouette
    # happens to still be the same size.
    want = [place @ (rest_world(arm, o) @ v.co)
            for o in meshes for v in o.data.vertices]

    if not hasattr(arm.data, "transform"):
        raise SystemExit("This Blender has no Armature.transform(); the "
                         "placement cannot be baked into the bones.")
    arm.data.transform(place)
    arm.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()

    # Each rock's transform relative to its bone is unchanged by `place` except
    # for the scale, so the whole correction is "scale the offset, leave the
    # rotation alone":
    #
    #     basis_new = Scale(f) . basis_old . Scale(1/f)
    #               = Translation(f * loc_old) . Rot_old
    #
    # Assigning `matrix_world` instead is the obvious route and it silently
    # does not work here: the setter solves for the basis using the parent
    # bone's *evaluated* matrix, and `Armature.transform` does not push the new
    # bone positions through the depsgraph in a background Blender. 27 of the
    # 30 rocks came out 1.2 m adrift, with no error raised.
    up = Matrix.Diagonal((factor, factor, factor, 1.0))
    scaled = set()
    for o in meshes:
        if o.data.name not in scaled:          # never scale one datablock twice
            o.data.transform(up)
            scaled.add(o.data.name)
        o.location = o.location * factor
    bpy.context.view_layer.update()

    keys = 0
    for act in bpy.data.actions:
        for fc in action_fcurves(act):
            if not fc.data_path.endswith(".location"):
                continue
            for kp in fc.keyframe_points:
                kp.co.y *= factor
                kp.handle_left.y *= factor
                kp.handle_right.y *= factor
                keys += 1

    got = [rest_world(arm, o) @ v.co for o in meshes for v in o.data.vertices]
    drift = max((a - b).length for a, b in zip(want, got))
    worst_scale = max(abs(v - 1.0) for o in meshes for v in o.scale)
    print("Baked the placement into the rig: worst vertex off target by %.6f m, "
          "%d root-location keys rescaled, worst mesh object scale error %.2e"
          % (drift, keys, worst_scale))
    if drift > 1e-4 or worst_scale > 1e-4:
        raise SystemExit(
            "Baking the placement left a vertex %.4f m off target and a scale "
            "error of %.4f. The golem would ship dismembered." % (drift, worst_scale))


def main():
    if not os.path.exists(SRC):
        raise SystemExit("No model at %s -- run golem_rig.py first." % SRC)

    bpy.ops.wm.open_mainfile(filepath=SRC)

    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    factor = SHIP_HEIGHT / SOURCE_HEIGHT

    roots = [o for o in bpy.data.objects
             if o.parent is None and o.type in {'MESH', 'ARMATURE'}]
    if [o.name for o in roots] != [ARM]:
        raise SystemExit(
            "Expected %s to be the only root object, found %s.\n"
            "Anything still unparented would be left behind at source scale "
            "and orientation." % (ARM, ", ".join(sorted(o.name for o in roots))))

    arm = roots[0]
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']

    # world' = scale . (world - PIVOT): slide PIVOT onto the origin, then
    # shrink to shipping size. Nothing else.
    #
    # There is deliberately **no yaw and no axis conversion here**. The golem
    # faces +Y in the source, which is the library's *backward*, and the
    # temptation is to spin it 180 degrees and tip Z-up into Y-up on the way
    # out. Both were tried and both are wrong, for the same reason: Unity
    # discards the armature node's rotation, so anything rotational left on it
    # vanishes, and anything baked into the data instead gets applied a second
    # time by `ModelImporter.bakeAxisConversion`.
    #
    # What works, measured: ship the data in Blender's own Z-up axes, and let
    # `GolemBuilder.ConfigureImporter` set `bakeAxisConversion = true`. Unity
    # then converts the bind pose and the clips together, and lands the golem
    # upright, facing +Z, with its right side on +X. See golem_BUILD.md.
    place = (Matrix.Diagonal((factor, factor, factor, 1.0))
             @ Matrix.Translation(-Vector(PIVOT)))

    # Everything is measured and re-seated in the rest pose. The .blend is
    # saved sitting on frame 1 of the idle, and every rock is bone-parented, so
    # `matrix_world` would otherwise report wherever the idle put it.
    arm.data.pose_position = 'REST'
    bpy.context.view_layer.update()

    bake_placement(arm, meshes, place, factor)

    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in meshes:
        mw = rest_world(arm, o)
        for vert in o.data.vertices:
            w = mw @ vert.co
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])

    bones = sum(len(a.data.bones) for a in bpy.data.objects if a.type == 'ARMATURE')
    actions = sorted(bpy.data.actions, key=lambda a: a.name)
    print("Exporting %d meshes, %d bones and %d takes at %.4f scale"
          % (len(meshes), bones, len(actions), factor))
    for a in actions:
        print("  take Arm_Golem|%-14s frames %d..%d"
              % (a.name, a.frame_start, a.frame_end))
    print("Shipping bounds, metres, Blender axes (x right, y forward, z up):")
    print("  x %.3f .. %.3f  (width  %.3f)" % (lo.x, hi.x, hi.x - lo.x))
    print("  y %.3f .. %.3f  (depth  %.3f)" % (lo.y, hi.y, hi.y - lo.y))
    print("  z %.3f .. %.3f  (height %.3f)" % (lo.z, hi.z, hi.z - lo.z))
    # Deliberately not converted to Unity axes here. `bakeAxisConversion` on
    # the Unity side decides that mapping, and hand-deriving it from these
    # numbers is how the collider ended up 5 cm off twice. The prefab's
    # BoxCollider figures in GolemBuilder.cs were measured off the built
    # prefab instead; re-measure them the same way if the model changes.
    print("Unity-side collider: measure it off the built prefab, not from the "
          "numbers above.")

    arm.data.pose_position = 'POSE'

    # Round-trip the baked file through disk before exporting.
    #
    # `Armature.transform()` leaves the depsgraph holding the pre-bake bone
    # matrices, and `view_layer.update()` does not shift it. The rest pose the
    # exporter writes is read from data and comes out right; the animation
    # takes are *baked from the evaluated armature* and came out carrying the
    # old orientation, so the golem's bind pose stood up correctly and every
    # clip laid it back down 1.3 m below the floor. Nothing in the export
    # reported a problem -- it was only visible by sampling the imported clips
    # in Unity.
    #
    # Reopening the file is the one thing that is guaranteed to rebuild the
    # depsgraph from the data. It is written to a scratch path, never over
    # golem.blend: the .blend is the source of truth and this script does not
    # get to edit it.
    baked = os.path.join(tempfile.gettempdir(), "golem_baked_for_export.blend")
    bpy.ops.wm.save_as_mainfile(filepath=baked)
    bpy.ops.wm.open_mainfile(filepath=baked)
    bpy.context.view_layer.update()

    os.makedirs(os.path.dirname(DST), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=DST,
        use_selection=False,
        object_types={'MESH', 'ARMATURE'},
        # NOT 'FBX_SCALE_NONE', which is the exporter's default and what every
        # other script in this library uses. That option puts Blender's
        # metre -> centimetre unit conversion onto the *object transforms*, and
        # Unity keeps it: the prefab root came in at `localScale = 100`. The
        # rendering was still correct, because Unity scales the vertex data
        # down by the same 100 -- so it looks fine and is a trap. Everything
        # authored in root-local units after that is silently 100x out, and the
        # BoxCollider written in metres became a 260 x 258 x 251 metre box
        # centred 129 m in the air.
        #
        # 'FBX_SCALE_UNITS' puts the unit conversion in the FBX's own scale
        # header instead, where Unity consumes it properly. Measured result:
        # root `localScale = 1`, identical world geometry to the millimetre.
        apply_scale_options='FBX_SCALE_UNITS',
        axis_forward='Y',      # identity: the conversion is already in the data
        axis_up='Z',
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
    if os.path.exists(baked):
        os.remove(baked)
    # Deliberately no save_mainfile: the .blend is the source of truth.


main()
