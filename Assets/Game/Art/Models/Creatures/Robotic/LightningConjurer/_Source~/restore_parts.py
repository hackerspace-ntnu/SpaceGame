# Puts the geometry right before anything is rigged.
#
# STEP 0 OF 6:  restore_parts.py -> rig.py -> walkerize.py -> hands_rebuild.py
#               -> rustify.py -> anim.py -> export.py
#
# Three jobs, all of them geometry and none of them rigging, which is why they
# run before rig.py rather than after:
#
#   1. Re-appends the halo and the eight forearm cable curves. The working file
#      had drifted off the committed lineage and lost them -- the Halo bone was
#      still built and still animated, driving nothing.
#
#   2. Parks the two legacy hands. `Armature` (right, 18 bones, one skinned mesh)
#      and `Armature.001` (left, 14 bones, five loose meshes) between them had no
#      thumb, and the left had no working finger bones at all. Nothing is deleted
#      -- they go to WIP_Spares like every other superseded part, so the file
#      stays recoverable.
#
#   3. Grafts two copies of components/mechanical/robot_hand.blend on in their
#      place: thumb + 4 fingers, three phalanges each, every phalanx a separate
#      object with its origin on its own hinge pin.
#
# Safe to re-run: each step checks for its own output first.
#
#     blender -b "<blend>" -P restore_parts.py -- --donor <committed.blend>
import os
import sys

import bpy
from mathutils import Matrix, Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(flag, default=None):
    return argv[argv.index(flag) + 1] if flag in argv else default


HERE = os.path.dirname(os.path.abspath(__file__))
COMPONENT = os.path.normpath(os.path.join(
    HERE, "..", "..", "..", "..", "_Source~", "components", "mechanical",
    "robot_hand.blend"))
DONOR = arg("--donor")

SPARES = "WIP_Spares"

# Parts to append back from the committed model, by name, straight out of rig.py's
# BIND table.
#
# EMPTY, and deliberately so. This list held the halo and the eight forearm cable
# curves, all of which were restored and then taken off again on request -- see
# RETIRE. The machinery is kept rather than deleted because it is the only way
# back if any of them are ever wanted again: put a name here and pass --donor.
RESTORE = []

# Parts the creature is no longer meant to wear. Parked rather than deleted, like
# everything else this pipeline supersedes, so putting one back is a one-line
# change rather than a re-model.
#
#   Cube                  the halo -- the big glowing cube that floated above the
#                         head.
#   BézierCurve.001/.003  the four cable runs on the RIGHT forearm, two either
#              .004/.005  side of it.
#   BézierCurve.007/.009  the same four on the LEFT.
#              .010/.011
#
# The upper-arm curves (BézierCurve, .002, .006, .008) are NOT here -- they stay.
#
# The halo's BONE also stays. `Halo` is still in rig.py's table and anim.py still
# spins it up through Attack; it simply drives nothing now. Leaving it costs one
# transform in the FBX and keeps the restore trivial, where tearing it out would
# mean edits to the bone table, both clips and this file. The forearm curves have
# no bones of their own -- they were rigidly parented to Forearm.L/R -- so they
# leave nothing behind at all.
RETIRE = (["Cube"]
          + [f"BézierCurve.{i:03d}" for i in (1, 3, 4, 5, 7, 9, 10, 11)])

LEGACY = {"Armature": ["Hand.001"],
          "Armature.001": ["Hand.003", "Hand.004", "Hand.011", "Hand.016",
                           "Hand.035"]}

# ---------------------------------------------------------------- placement
#
# Measured off the model rather than guessed. The legacy right hand spanned
# z 16.93..20.05 hanging under a wrist block whose underside is at z 19.99, so a
# replacement that occupies the same 3.12 units keeps the arm's proportions --
# the component is authored 0.95 m long and a metre is 3.835 conjurer units, so
# an anatomically-scaled hand would come out 17% larger than the arm was drawn
# for. Matching the model beats matching the anatomy here.
HAND_LEN_M = 0.95                     # component wrist -> middle fingertip
HAND_SCALE = 3.12 / HAND_LEN_M        # ~3.28 conjurer units per component metre
WRIST_Z = 20.20                       # collar tucks up into the wrist block
WRIST = {"R": Vector((-0.23, -6.41, WRIST_Z)),
         "L": Vector((-0.23, 6.32, WRIST_Z))}

# Component axes onto conjurer axes, as the images of X, Y, Z in the columns:
#
#     Xc (thumb side)  -> +Y
#     Yc (fingers)     -> -Z   hands hang downward off the forearms
#     Zc (dorsal)      -> -X   so the palm normal, -Zc, faces +X: FORWARD
#
# Palm-forward at rest is what the original model had too -- its hands were flat
# in X and spread in Y -- and it is the smallest roll away from the palm-to-sky
# the Attack clip needs.
ORIENT = Matrix(((0.0, 0.0, -1.0),
                 (1.0, 0.0, 0.0),
                 (0.0, -1.0, 0.0))).to_4x4()

MIRROR_Y = Matrix.Diagonal((1.0, -1.0, 1.0, 1.0))


def log(m):
    print(f"[restore] {m}")


def spares():
    coll = bpy.data.collections.get(SPARES)
    if coll is None:
        coll = bpy.data.collections.new(SPARES)
        bpy.context.scene.collection.children.link(coll)
    return coll


def park(obj):
    """Move an object to WIP_Spares without deleting or moving it."""
    coll = spares()
    if obj.name in coll.objects:
        return False
    world = obj.matrix_world.copy()
    obj.parent = None
    obj.matrix_world = world
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)
    return True


# --------------------------------------------------------------- 1. restore
def restore_missing():
    want = [n for n in RESTORE if n not in bpy.data.objects]
    if not want:
        log("halo and cables already present")
        return
    if not DONOR or not os.path.exists(DONOR):
        raise SystemExit(f"[restore] need --donor <committed .blend>; missing {want}")

    # `dst.objects` is filled in place on exit from the context manager, so the
    # list handed to it comes back holding Object datablocks rather than the
    # names that went in. Pass a copy and read the result off `dst`.
    names = list(want)
    with bpy.data.libraries.load(DONOR, link=False) as (src, dst):
        absent = [n for n in names if n not in src.objects]
        if absent:
            raise SystemExit(f"[restore] donor has no {absent}")
        dst.objects = names

    target = bpy.data.collections.get("Character") or bpy.context.scene.collection
    for o in names:
        # In the donor these were bone-parented by rig.py, which stores the
        # object's ORIGINAL WORLD MATRIX in matrix_basis (see its
        # rigid_bone_parent). The parent pointer does not survive an append that
        # leaves the armature behind, so matrix_basis is exactly what we want and
        # clearing the parent restores the object to where it was authored.
        o.parent = None
        target.objects.link(o)
    log(f"restored {len(names)}: {', '.join(o.name for o in names)}")


# --------------------------------------------------------- 1b. retire parts
def retire_parts():
    """Take superseded parts off the creature without deleting them.

    park() clears the parent as well as moving the collection, and that is the
    half that matters: export.py selects by walking ConjurerRig's children, so a
    part that is merely moved to another collection still reaches Unity.
    """
    moved = [n for n in RETIRE
             if (o := bpy.data.objects.get(n)) is not None and park(o)]
    if moved:
        log(f"retired {len(moved)} part(s) to {SPARES}: {', '.join(moved)}")


# ---------------------------------------------------------- 2. park legacy
def park_legacy():
    moved = 0
    for arm_name, meshes in LEGACY.items():
        for n in [arm_name] + meshes:
            o = bpy.data.objects.get(n)
            if o is not None and park(o):
                moved += 1
    log(f"parked {moved} legacy hand object(s) in {SPARES} (nothing deleted)")


# ----------------------------------------------------------- 3. graft hands
def graft_hands():
    if "Hand_Palm.R" in bpy.data.objects:
        log("hands already grafted")
        return

    if not os.path.exists(COMPONENT):
        raise SystemExit(f"[restore] no hand component at {COMPONENT}")

    appended = [n for n in src_names(COMPONENT)
                if n.startswith("Mesh_Hand_Five_") or n == "Rig_Hand_Five"]
    with bpy.data.libraries.load(COMPONENT, link=False) as (src, dst):
        dst.objects = appended            # filled in place with the datablocks

    if not appended:
        raise SystemExit("[restore] appended nothing from the hand component")

    target = bpy.data.collections.get("Character") or bpy.context.scene.collection

    # Link before reading a single matrix. A freshly appended object is not in
    # the scene, so it is not in the depsgraph, and `matrix_world` on a
    # bone-parented object that nothing has evaluated comes back as the identity
    # -- which silently collapses every phalanx onto the wrist.
    for o in appended:
        bpy.context.scene.collection.objects.link(o)
    bpy.context.view_layer.update()

    # Drop the component's own bone parenting: each piece is placed by this
    # script and re-parented onto ConjurerRig by hands_rebuild.py, and a surviving
    # parent here would apply the component armature's transform a second time.
    for o in appended:
        world = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = world
    bpy.context.view_layer.update()

    for side in ("R", "L"):
        place = (Matrix.Translation(WRIST[side])
                 @ (MIRROR_Y if side == "L" else Matrix.Identity(4))
                 @ Matrix.Scale(HAND_SCALE, 4)
                 @ ORIENT)

        for proto in appended:
            obj = proto.copy()
            obj.data = proto.data.copy()
            obj.name = rename(proto.name, side)
            obj.data.name = obj.name
            target.objects.link(obj)

            world = place @ proto.matrix_world
            # Meshes only. The armature comes along purely to carry the joint
            # positions through to hands_rebuild.py, which reads them as
            # `matrix_world @ bone.head_local` -- a mirrored matrix gives
            # correctly mirrored joints, and an armature has no normals to
            # invert.
            if side == "L" and obj.type == 'MESH':
                # `place` has a negative determinant on the left, which renders
                # every face inside out. Push the mirror down into the mesh data
                # and hand the object a proper rotation: with
                # W' = W @ diag(1,-1,1) and the vertices mirrored to match, the
                # geometry lands in exactly the same place with its normals out.
                world = world @ MIRROR_Y
                obj.data.transform(MIRROR_Y)
                obj.data.flip_normals()
            obj.matrix_world = world

    for o in appended:                       # the prototypes were only a source
        bpy.data.objects.remove(o, do_unlink=True)

    log(f"grafted both hands at scale {HAND_SCALE:.3f}")


def src_names(path):
    """Object names inside a .blend, without appending anything."""
    with bpy.data.libraries.load(path, link=False) as (src, _dst):
        return list(src.objects)


def rename(name, side):
    if name == "Rig_Hand_Five":
        return f"HandRig.{side}"
    # Mesh_Hand_Five_Index2 -> Hand_Index2.R ; ..._Palm -> Hand_Palm.R
    return f"Hand_{name[len('Mesh_Hand_Five_'):]}.{side}"


restore_missing()
retire_parts()
park_legacy()
graft_hands()

bpy.ops.wm.save_mainfile()
log("SAVED")
