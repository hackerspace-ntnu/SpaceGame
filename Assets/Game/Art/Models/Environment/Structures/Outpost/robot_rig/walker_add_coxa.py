# Adds the two joints the walking station's legs were missing.
#
#   Coxa_<id>  azimuth (yaw) at the hip -- lets a planted foot stay planted.
#   Foot_<id>  roll at the sole         -- lets the sole lie flat across a slope.
#
# The foot bone pivots ON the sole contact, so rolling tilts the sole without moving the point
# the IK placed. Same trick as the coxa, whose axis passes through the hip: put the pivot on the
# thing you do not want to move, and the two stages of the solve stop interfering.
#
# The rig as authored gives every leg three hinge pins that are all parallel, horizontal and
# perpendicular to the bone, which traps the leg in one vertical plane. A leg with only pitch
# hinges cannot hold a planted foot while the hull moves in any direction other than its own
# plane, so the four corner legs slide their feet at 0.57x the distance travelled.
#
# This inserts a Coxa_<id> bone between Root and Hip_<id>, rotating about the vertical, plus the
# machinery that justifies it visually: a slew collar that turns with the leg and a fixed race
# above it bolted to the hull. The pin mesh matters as much as the bone -- WalkerRig recovers
# every hinge axis by measuring the longest extent of a child mesh named "*Pin*", so a correctly
# proportioned LEG3_CoxaPin_<id> is what makes the new joint discoverable with no code change.
#
# Idempotent: re-running removes what a previous run added before rebuilding.
#
#   blender --background walker_station.blend --python walker_add_coxa.py -- --save

import sys
import bmesh
import bpy
from mathutils import Matrix, Vector

LEG_IDS = ["P1", "P2", "P3", "N1", "N2", "N3"]
ARMATURE = "STATION_Rig"
ROOT_BONE = "Root"
LEG_COLLECTION = "STA_Legs"

# Heights are in the model's own units, read off the existing parts: the leg's top flange
# (LEG3_HipPlate) tops out at z=10.92 and the hip hub at 10.26, so the azimuth assembly stacks
# just above the flange where a real slew bearing would sit.
COXA_HEAD_Z = 11.90          # top of the coxa bone; the hull-side anchor
PIN_CENTRE_Z = 11.30
PIN_RADIUS = 0.22
PIN_DEPTH = 1.50             # must exceed the diameter, or the axle measurement picks the wrong axis
COLLAR_CENTRE_Z = 11.05      # turns with the leg
COLLAR_RADIUS = 1.60
COLLAR_DEPTH = 0.45
RACE_CENTRE_Z = 11.45        # fixed to the hull
RACE_RADIUS = 1.80
RACE_DEPTH = 0.35
TOOTH_COUNT = 16
TOOTH_SIZE = (0.16, 0.30, 0.26)

# Parts that carry the hip pin rather than swinging on it. They belong above the pitch hinge, so
# they move to the coxa and yaw with the leg without pitching with the thigh.
BRACKET_PARTS = ["LEG3_HipBracket_{id}", "LEG3_HipPlate_{id}"]

# The sole plate and its boss ride on the roll joint; the ankle yoke, pin and bolts do not.
SOLE_PARTS = ["LEG3_Foot_{id}", "LEG3_FootBoss_{id}", "LEG3_FootBossCap_{id}"]

FOOT_BONE_LENGTH = 1.6       # points along the leg, so the bone's own axis IS the roll axis
FOOT_PIN_RADIUS = 0.15
FOOT_PIN_DEPTH = 1.10


def log(msg):
    print("[coxa] " + msg)


def armature():
    obj = bpy.data.objects.get(ARMATURE)
    if obj is None:
        raise RuntimeError("armature %r not found" % ARMATURE)
    m = obj.matrix_world
    # Every position below is written in world coordinates. That is only the same as armature
    # space while the armature sits at the origin unrotated, so check rather than assume.
    if (m.to_translation().length > 1e-5
            or (m.to_3x3() - Matrix.Identity(3)).median_scale > 1e-5):
        raise RuntimeError("armature is not at the origin unrotated; positions would be wrong")
    return obj


def material_of(name, slot=0):
    obj = bpy.data.objects.get(name)
    if obj is None or not obj.material_slots:
        return None
    return obj.material_slots[min(slot, len(obj.material_slots) - 1)].material


# ─────────── cleanup ───────────

def clear_previous(arm):
    """Undo a previous run so the script can be applied repeatedly to the same file."""
    doomed = [o for o in bpy.data.objects if "Coxa" in o.name or "FootPin" in o.name]
    for obj in doomed:
        bpy.data.objects.remove(obj, do_unlink=True)
    if doomed:
        log("removed %d object(s) from a previous run" % len(doomed))

    # Moved parts go back to the joints they came from before the bones that carry them are
    # deleted. Without this a re-run with different bone placement leaves them subtly misplaced,
    # because their parent-inverse still refers to the bone from the previous run.
    restored = 0
    for leg in LEG_IDS:
        for pattern in SOLE_PARTS:
            obj = bpy.data.objects.get(pattern.format(id=leg))
            if obj is not None and obj.parent_bone == "Foot_" + leg:
                bone_parent(obj, arm, "Ankle_" + leg)
                restored += 1
        for pattern in BRACKET_PARTS:
            obj = bpy.data.objects.get(pattern.format(id=leg))
            if obj is not None and obj.parent_bone == "Coxa_" + leg:
                bone_parent(obj, arm, "Hip_" + leg)
                restored += 1
    if restored:
        log("returned %d part(s) to their original joints" % restored)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    edit_bones = arm.data.edit_bones
    removed = 0
    for leg in LEG_IDS:
        coxa = edit_bones.get("Coxa_" + leg)
        if coxa is not None:
            hip = edit_bones.get("Hip_" + leg)
            if hip is not None:
                hip.parent = edit_bones.get(ROOT_BONE)
            edit_bones.remove(coxa)
            removed += 1
        foot = edit_bones.get("Foot_" + leg)
        if foot is not None:
            edit_bones.remove(foot)
            removed += 1
    bpy.ops.object.mode_set(mode="OBJECT")
    if removed:
        log("removed %d bone(s) from a previous run" % removed)


# ─────────── bones ───────────

def insert_coxa_bones(arm):
    """Insert Root -> Coxa_<id> -> Hip_<id>, leaving every existing bone's rest pose untouched."""
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    edit_bones = arm.data.edit_bones
    root = edit_bones.get(ROOT_BONE)

    pivots = {}
    for leg in LEG_IDS:
        hip = edit_bones.get("Hip_" + leg)
        if hip is None:
            log("WARNING: no Hip_%s, skipped" % leg)
            continue

        # The bone runs straight down the yaw axis onto the hip, so its own axis IS the axis of
        # rotation. Edit-bone heads and tails are absolute, so inserting a parent here cannot
        # disturb the rest pose of anything below it.
        head = Vector((hip.head.x, hip.head.y, COXA_HEAD_Z))
        coxa = edit_bones.new("Coxa_" + leg)
        coxa.head = head
        coxa.tail = hip.head.copy()
        coxa.roll = 0.0
        coxa.parent = root
        coxa.use_connect = False

        hip.parent = coxa
        hip.use_connect = False
        pivots[leg] = Vector((hip.head.x, hip.head.y, 0.0))

    bpy.ops.object.mode_set(mode="OBJECT")
    log("inserted %d coxa bone(s)" % len(pivots))
    return pivots


def insert_foot_bones(arm):
    """Insert Ankle_<id> -> Foot_<id>, a roll hinge pivoting on the sole contact.

    The bone runs horizontally along the leg, so rotating about its own axis tilts the sole from
    side to side -- the one thing a stack of parallel pitch hinges cannot do, and the reason the
    sole could not lie flat across a slope. Placing the head at the ankle's tail, which is the
    designed ground contact, means the roll turns the foot without moving the contact point.
    """
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    edit_bones = arm.data.edit_bones

    axes = {}
    for leg in LEG_IDS:
        ankle = edit_bones.get("Ankle_" + leg)
        hip = edit_bones.get("Hip_" + leg)
        if ankle is None or hip is None:
            continue

        # The leg's outboard direction, flattened: the axis a sole rolls about is the fore-aft
        # line lying in the sole, which for this rig is the direction the leg reaches out along.
        out = Vector((ankle.head.x - hip.head.x, ankle.head.y - hip.head.y, 0.0))
        if out.length < 1e-4:
            out = Vector((1.0, 0.0, 0.0))
        out.normalize()

        foot = edit_bones.new("Foot_" + leg)
        foot.head = ankle.tail.copy()
        foot.tail = ankle.tail + out * FOOT_BONE_LENGTH
        foot.roll = 0.0
        foot.parent = ankle
        foot.use_connect = False
        axes[leg] = out

    bpy.ops.object.mode_set(mode="OBJECT")
    log("inserted %d foot roll bone(s)" % len(axes))
    return axes


def build_foot_parts(arm, axes, collection):
    """The roll pin, plus moving the sole plate onto the joint that now carries it."""
    made = moved = 0
    for leg, axis in axes.items():
        ankle = arm.data.bones.get("Ankle_" + leg)
        centre = arm.matrix_world @ ankle.tail_local

        pin = new_mesh_object("LEG3_FootPin_" + leg, material_of("LEG3_AnklePin_" + leg), collection)
        build_cylinder(pin, FOOT_PIN_RADIUS, FOOT_PIN_DEPTH, Vector((0.0, 0.0, 0.0)))
        # Lay the cylinder down along the roll axis; its longest extent is what gets measured.
        pin.matrix_world = (Matrix.Translation(centre)
                            @ axis.to_track_quat("Z", "Y").to_matrix().to_4x4())
        bone_parent(pin, arm, "Foot_" + leg)
        made += 1

        for pattern in SOLE_PARTS:
            obj = bpy.data.objects.get(pattern.format(id=leg))
            if obj is None or obj.parent_bone != "Ankle_" + leg:
                continue
            bone_parent(obj, arm, "Foot_" + leg)
            moved += 1

    log("built %d foot pin(s), moved %d sole part(s) onto the roll joint" % (made, moved))


# ─────────── geometry ───────────

def new_mesh_object(name, material, collection):
    mesh = bpy.data.meshes.new(name)
    obj = bpy.data.objects.new(name, mesh)
    if material is not None:
        obj.data.materials.append(material)
    collection.objects.link(obj)
    return obj


def build_cylinder(obj, radius, depth, centre):
    bm = bmesh.new()
    bmesh.ops.create_cone(
        bm, cap_ends=True, cap_tris=False, segments=24,
        radius1=radius, radius2=radius, depth=depth,
        matrix=Matrix.Translation(centre))
    bm.to_mesh(obj.data)
    bm.free()


def build_toothed_ring(obj, radius, depth, centre, teeth):
    """A collar with gear teeth around it, so the joint reads as driven rather than free."""
    bm = bmesh.new()
    bmesh.ops.create_cone(
        bm, cap_ends=True, cap_tris=False, segments=28,
        radius1=radius, radius2=radius, depth=depth,
        matrix=Matrix.Translation(centre))
    for i in range(teeth):
        angle = (i / float(teeth)) * 6.283185307
        spoke = Matrix.Rotation(angle, 4, "Z")
        offset = Matrix.Translation(Vector((radius, 0.0, 0.0)))
        bmesh.ops.create_cube(
            bm, size=1.0,
            matrix=Matrix.Translation(centre) @ spoke @ offset @ Matrix.Diagonal(
                Vector(TOOTH_SIZE + (1.0,))))
    bm.to_mesh(obj.data)
    bm.free()


def bone_parent(obj, arm, bone_name):
    """Parent to a bone while holding the object exactly where it already is."""
    world = obj.matrix_world.copy()
    obj.parent = arm
    obj.parent_type = "BONE"
    obj.parent_bone = bone_name
    obj.matrix_world = world


def build_azimuth_parts(arm, pivots, collection):
    made = 0
    for leg, pivot in pivots.items():
        pin_mat = material_of("LEG3_HipPin_" + leg)
        hub_mat = material_of("LEG3_HipHub_" + leg)
        plate_mat = material_of("LEG3_HipPlate_" + leg)

        # The pin is the joint's declaration of intent: WalkerRig measures its longest extent to
        # recover the hinge axis, so it must be clearly longer than it is wide, and vertical.
        pin = new_mesh_object("LEG3_CoxaPin_" + leg, pin_mat, collection)
        build_cylinder(pin, PIN_RADIUS, PIN_DEPTH,
                       Vector((pivot.x, pivot.y, PIN_CENTRE_Z)))
        bone_parent(pin, arm, "Coxa_" + leg)

        collar = new_mesh_object("LEG3_CoxaCollar_" + leg, hub_mat, collection)
        build_toothed_ring(collar, COLLAR_RADIUS, COLLAR_DEPTH,
                           Vector((pivot.x, pivot.y, COLLAR_CENTRE_Z)), TOOTH_COUNT)
        bone_parent(collar, arm, "Coxa_" + leg)

        # The race stays with the hull: a joint you can see turning needs a still half to turn
        # against, and parenting it to Root is what makes the collar's motion legible.
        race = new_mesh_object("LEG3_CoxaRace_" + leg, plate_mat, collection)
        build_cylinder(race, RACE_RADIUS, RACE_DEPTH,
                       Vector((pivot.x, pivot.y, RACE_CENTRE_Z)))
        bone_parent(race, arm, ROOT_BONE)

        made += 3
    log("built %d azimuth part(s)" % made)


def move_brackets_to_coxa(arm, pivots):
    """The bracket carries the hip pin, so it must yaw with the leg but not pitch with the thigh."""
    moved = 0
    for leg in pivots:
        for pattern in BRACKET_PARTS:
            obj = bpy.data.objects.get(pattern.format(id=leg))
            if obj is None or obj.parent_bone != "Hip_" + leg:
                continue
            bone_parent(obj, arm, "Coxa_" + leg)
            moved += 1
    log("re-parented %d bracket part(s) onto the coxa" % moved)


# ─────────── verification ───────────

def verify(arm, pivots, axes):
    """Prove the new joints are discoverable the same way the existing ones are."""
    ok = True

    for leg in axes:
        bone = arm.data.bones.get("Foot_" + leg)
        ankle = arm.data.bones.get("Ankle_" + leg)
        if bone is None or ankle is None or bone.parent is None or bone.parent.name != ankle.name:
            log("FAIL %s: foot/ankle parenting wrong" % leg)
            ok = False
            continue

        # The roll axis must be horizontal, or it is a yaw joint wearing a foot's name.
        axis = (bone.tail_local - bone.head_local).normalized()
        if abs(axis.z) > 1e-3:
            log("FAIL %s: roll axis is not horizontal (%s)" % (leg, axis))
            ok = False

        # And the pivot must sit on the sole, so rolling cannot drag the contact sideways.
        if (bone.head_local - ankle.tail_local).length > 1e-4:
            log("FAIL %s: roll pivot is not on the sole contact" % leg)
            ok = False

        pin = bpy.data.objects.get("LEG3_FootPin_" + leg)
        if pin is None or pin.parent_bone != "Foot_" + leg:
            log("FAIL %s: foot pin missing or not parented to the roll bone" % leg)
            ok = False
            continue
        corners = [Vector(c) for c in pin.bound_box]
        extents = [max(c[i] for c in corners) - min(c[i] for c in corners) for i in range(3)]
        longest = extents.index(max(extents))
        basis = Vector((0.0, 0.0, 0.0))
        basis[longest] = 1.0
        world_axis = (pin.matrix_world.to_3x3() @ basis).normalized()
        if abs(world_axis.dot(axis)) < 0.99:
            log("FAIL %s: measured foot pin axis %s does not match the roll axis %s"
                % (leg, world_axis, axis))
            ok = False

    for leg in pivots:
        bone = arm.data.bones.get("Coxa_" + leg)
        hip = arm.data.bones.get("Hip_" + leg)
        # Compared by name, not identity: Blender hands out a fresh Python wrapper per access,
        # so `hip.parent is bone` is false even when the parenting is correct.
        if bone is None or hip is None or hip.parent is None or hip.parent.name != bone.name:
            log("FAIL %s: coxa/hip parenting wrong" % leg)
            ok = False
            continue

        axis = (bone.tail_local - bone.head_local).normalized()
        if abs(abs(axis.z) - 1.0) > 1e-4:
            log("FAIL %s: coxa axis is not vertical (%s)" % (leg, axis))
            ok = False

        pin = bpy.data.objects.get("LEG3_CoxaPin_" + leg)
        if pin is None or pin.parent_bone != "Coxa_" + leg:
            log("FAIL %s: pin missing or not parented to the coxa" % leg)
            ok = False
            continue

        # Same measurement WalkerRig performs: longest local extent, taken into world space.
        corners = [Vector(c) for c in pin.bound_box]
        extents = [max(c[i] for c in corners) - min(c[i] for c in corners) for i in range(3)]
        longest = extents.index(max(extents))
        if sorted(extents)[-1] <= sorted(extents)[-2] * 1.5:
            log("FAIL %s: pin is not clearly elongated (%s)" % (leg, extents))
            ok = False
        basis = Vector((0.0, 0.0, 0.0))
        basis[longest] = 1.0
        world_axis = (pin.matrix_world.to_3x3() @ basis).normalized()
        if abs(abs(world_axis.z) - 1.0) > 1e-3:
            log("FAIL %s: measured pin axis %s is not vertical" % (leg, world_axis))
            ok = False

    log("verification %s" % ("PASSED" if ok else "FAILED"))
    return ok


def main():
    arm = armature()
    collection = bpy.data.collections.get(LEG_COLLECTION) or bpy.context.scene.collection

    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")

    clear_previous(arm)
    pivots = insert_coxa_bones(arm)
    build_azimuth_parts(arm, pivots, collection)
    move_brackets_to_coxa(arm, pivots)

    axes = insert_foot_bones(arm)
    build_foot_parts(arm, axes, collection)

    if not verify(arm, pivots, axes):
        raise RuntimeError("coxa insertion failed verification; file NOT saved")

    if "--save" in sys.argv:
        bpy.ops.wm.save_mainfile()
        log("saved %s" % bpy.data.filepath)
    else:
        log("dry run; pass --save to write the file")


main()
