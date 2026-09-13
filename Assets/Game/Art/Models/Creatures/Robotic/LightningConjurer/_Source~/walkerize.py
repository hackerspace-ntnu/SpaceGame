# Converts ConjurerRig's legs to the walker convention, so SpaceGame's procedural IK
# locomotion (Assets/Game/Scripts/Locomotion) can discover and solve them.
#
# The baked Walk clip this replaces was never foot-locked -- see the StrideSpeed note in
# LightningConjurerBuilder.cs, which measures the planted foot sliding between 6.6 and
# 11.5 m/s about its mean. LeggedLocomotion places footholds against the real ground
# instead, so the skating goes away and the creature walks up terrain it is standing on.
#
# ─────────── what WalkerRig needs, and why each piece is here ───────────
#
# WalkerRig.Build discovers a limb by a root-bone PREFIX and assembles its chain by the
# names Coxa_/Hip_/Knee_/Ankle_/Foot_. The rig as authored uses Thigh/Shin/Foot, which
# matches none of them, so nothing would be discovered at all. Hence the renames:
#
#     Thigh.{s} -> Hip_{s}      the first pitch joint
#     Shin.{s}  -> Knee_{s}     the second
#     Foot.{s}  -> Ankle_{s}    the third; its meshes are what touch the ground
#
# Two joints are then INSERTED, because a three-hinge planar leg cannot do the two things
# a walking machine needs most:
#
#   Coxa_{s}  azimuth at the hip. Without it the leg is trapped in one vertical plane and
#             a planted foot is dragged sideways every time the body turns. Its axis
#             passes through the hip joint on purpose -- WalkerRig.Measure assumes the
#             first pitch joint lies ON the yaw axis and warns when it does not.
#   Foot_{s}  roll at the sole. Pivots ON the sole contact point, so rolling the foot to
#             meet a cross-slope tilts it without moving the point the IK just placed.
#
# ─────────── the pins are load-bearing, not decoration ───────────
#
# WalkerAxle recovers each hinge axis by measuring the longest extent of a child mesh
# named "*Pin*", and falls back to the rest pose's own plane normal when there is none.
# On THIS rig the fallback is not good enough: the leg is modelled all but straight
# (1.7 degrees of knee offset over an 11.5-unit thigh), so the cross product that the
# fallback is built on is dominated by noise. Measured against the real bone table it
# returns a hip axle 8.6 degrees off true and a knee axle 30 degrees off -- and
# WalkerRig.Classify keeps only the longest run of MUTUALLY-PARALLEL axles (0.99 dot,
# about 8 degrees). The pitch chain would collapse from three joints to one and the leg
# would solve as a stub.
#
# So every joint gets a modelled pin, and they are sized to sit INSIDE the leg hardware
# that is already there -- the knee hub is 3.1 units across and swallows a 1.4-unit pin
# whole. Only the axis is measured, never the size, so a hidden pin measures exactly as
# well as a visible one.
#
# Idempotent: re-running undoes what the previous run added before rebuilding.
#
#   blender --background "ConjuringRobot1 (2) (1) (1).blend" --python walkerize.py -- --save

import re
import sys
import bpy
from mathutils import Matrix, Vector

ARM_NAME = "ConjurerRig"

# (side, hip_y, knee_y, ankle_y, sole_y). Read off the rest pose with the action detached
# -- the saved file has Idle applied, and Idle's own keys move every one of these.
SIDES = [
    ("L",  2.12,  2.17,  2.06,  2.165),
    ("R", -2.24, -2.22, -2.12, -2.170),
]

RENAMES = [("Thigh.{s}", "Hip_{s}"), ("Shin.{s}", "Knee_{s}"), ("Foot.{s}", "Ankle_{s}")]

# Joint positions in the model's own units, Blender Z-up, straight off the bone table.
HIP_Z, KNEE_Z, ANKLE_Z = 25.42, 13.93, 5.10
HIP_X, KNEE_X, ANKLE_X = 0.04, 0.37, 0.36

# The sole contact: centre of the foot mesh's footprint, at the lowest point of the model.
# BlenderFloor in LightningConjurerBuilder.cs is this same 2.757.
SOLE_X, SOLE_Z = 1.915, 2.757

COXA_TOP_Z = 26.60      # coxa runs down from inside the hip block to the hip joint
FOOT_LENGTH = 1.60      # points along +X, so the bone's own axis IS the roll axis

# Pin proportions. Depth must EXCEED the diameter or the longest-extent measurement picks
# a radial axis instead of the hinge. Each is checked against the mesh meant to hide it.
# A pin's POSITION is never measured, only the direction of its longest axis -- so each is
# placed wherever it is best hidden rather than exactly on its joint. The sole pin is the one
# exception that matters: it must clear the bottom of the foot, because WalkerRig takes the
# contact point from the LOWEST renderer under the ankle and a pin hanging below the sole
# would become the thing the creature stands on.
PINS = [
    # (bone,      x,       z,              radius, depth, axis)
    ("Coxa_{s}",  HIP_X,   HIP_Z,          0.18,   1.30,  'Z'),   # inside Hips block
    ("Hip_{s}",   HIP_X,   HIP_Z,          0.20,   1.00,  'Y'),   # inside Hips block
    ("Knee_{s}",  KNEE_X,  KNEE_Z,         0.20,   1.40,  'Y'),   # inside the knee hub
    ("Ankle_{s}", ANKLE_X, ANKLE_Z,        0.18,   1.20,  'Y'),   # inside the lower leg
    ("Foot_{s}",  SOLE_X,  SOLE_Z + 0.20,  0.15,   1.10,  'X'),   # inside the sole plate
]

SOLE_MESH = {"L": "LeftFoot", "R": "RightFoot"}

# Pins inherit the leg's own material so the import's name-based remap in
# LightningConjurerBuilder.BuildMaterials picks them up like every other part.
PIN_MATERIAL_DONOR = "LeftLowerLeg"

SAVE = "--save" in sys.argv


def log(msg):
    print("[walkerize] " + msg)


def armature():
    arm = bpy.data.objects.get(ARM_NAME)
    if arm is None:
        raise RuntimeError("armature %r not found" % ARM_NAME)
    m = arm.matrix_world
    # Every coordinate below is written in world space, which is only the same as armature
    # space while the armature sits at the origin unrotated. rig.py and export.py both
    # depend on that too, so check rather than assume.
    if (m.to_translation().length > 1e-5
            or (m.to_3x3() - Matrix.Identity(3)).median_scale > 1e-5):
        raise RuntimeError("armature is not at the origin unrotated; positions would be wrong")
    return arm


def rest_pose(arm):
    """Detach the action and zero every pose bone.

    The .blend is saved by anim.py with Idle assigned, and bone-parented objects follow
    the POSE. Measuring or re-parenting against that would bake Idle's frame-1 offsets
    into the result. export.py zeroes the pose for the same reason.
    """
    if arm.animation_data:
        arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.scene.frame_set(1)
    bpy.context.view_layer.update()


def bone_parent(obj, arm, bone_name):
    """Bone-parent `obj` to `bone_name`, leaving its world transform bit-identical.

    Same arithmetic as rig.py's rigid_bone_parent: Blender anchors bone parenting at the
    bone TAIL, so the effective parent matrix carries a +Y translation of bone.length.
    """
    world = obj.matrix_world.copy()
    bone = arm.data.bones[bone_name]
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    P = arm.matrix_world @ bone.matrix_local @ Matrix.Translation((0.0, bone.length, 0.0))
    obj.matrix_parent_inverse = P.inverted()
    obj.matrix_basis = world


# ─────────── cleanup ───────────

def clear_previous(arm):
    """Undo a previous run, so the script can be applied repeatedly to the same file."""
    doomed = [o for o in bpy.data.objects if "Pin_" in o.name and o.parent is arm]
    for obj in doomed:
        bpy.data.objects.remove(obj, do_unlink=True)
    if doomed:
        log("removed %d pin(s) from a previous run" % len(doomed))

    # Sole meshes go back to the ankle BEFORE the bone carrying them is deleted. Their
    # parent-inverse refers to that bone; deleting it first would strand them.
    restored = 0
    for side, _, _, _, _ in SIDES:
        obj = bpy.data.objects.get(SOLE_MESH[side])
        if obj is not None and obj.parent_bone == "Foot_" + side:
            bone_parent(obj, arm, "Ankle_" + side)
            restored += 1
    if restored:
        log("returned %d sole mesh(es) to the ankle" % restored)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    edit = arm.data.edit_bones
    removed = 0
    for side, _, _, _, _ in SIDES:
        coxa = edit.get("Coxa_" + side)
        if coxa is not None:
            hip = edit.get("Hip_" + side)
            if hip is not None:
                hip.parent = edit.get("Hips")
            edit.remove(coxa)
            removed += 1
        foot = edit.get("Foot_" + side)
        if foot is not None:
            edit.remove(foot)
            removed += 1
    bpy.ops.object.mode_set(mode='OBJECT')
    if removed:
        log("removed %d inserted bone(s) from a previous run" % removed)


# ─────────── bones ───────────

def rename_bones(arm):
    """Thigh/Shin/Foot -> Hip_/Knee_/Ankle_.

    Blender remaps every object's `parent_bone` on rename, so nothing moves and nothing
    needs rebinding. Already-renamed bones are skipped, which is what makes a re-run safe.
    """
    done = 0
    for side, _, _, _, _ in SIDES:
        for old_pat, new_pat in RENAMES:
            old, new = old_pat.format(s=side), new_pat.format(s=side)
            bone = arm.data.bones.get(old)
            if bone is None:
                if arm.data.bones.get(new) is None:
                    raise RuntimeError("neither %s nor %s exists; rig is not as expected"
                                       % (old, new))
                continue
            bone.name = new
            done += 1
    log("renamed %d bone(s)" % done)


def insert_bones(arm):
    """Hips -> Coxa_{s} -> Hip_{s}, and Ankle_{s} -> Foot_{s}."""
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    edit = arm.data.edit_bones

    for side, hip_y, _, _, sole_y in SIDES:
        # The coxa points DOWN the vertical through the hip joint, so the bone's own axis
        # is the yaw axis and Hip_ sits exactly on it -- the condition WalkerRig.Measure
        # checks and warns about.
        coxa = edit.new("Coxa_" + side)
        coxa.head = Vector((HIP_X, hip_y, COXA_TOP_Z))
        coxa.tail = Vector((HIP_X, hip_y, HIP_Z))
        coxa.roll = 0.0
        coxa.parent = edit["Hips"]
        coxa.use_connect = False

        hip = edit["Hip_" + side]
        hip.parent = coxa            # head/tail are absolute, so the rest pose is untouched
        hip.use_connect = False

        # The sole hinge pivots ON the contact point and points forward, so its own axis
        # is the roll axis and a roll does not move the foothold the IK just chose.
        foot = edit.new("Foot_" + side)
        foot.head = Vector((SOLE_X, sole_y, SOLE_Z))
        foot.tail = Vector((SOLE_X + FOOT_LENGTH, sole_y, SOLE_Z))
        foot.roll = 0.0
        foot.parent = edit["Ankle_" + side]
        foot.use_connect = False

    bpy.ops.object.mode_set(mode='OBJECT')
    log("inserted %d coxa + %d sole bone(s)" % (len(SIDES), len(SIDES)))


def iter_fcurves(act):
    """Blender 4.4+ keeps fcurves in slotted layers/strips/channelbags. Same helper anim.py uses."""
    if hasattr(act, "fcurves") and len(act.fcurves):
        yield from act.fcurves
        return
    for layer in getattr(act, "layers", []):
        for strip in layer.strips:
            for cb in getattr(strip, "channelbags", []):
                yield from cb.fcurves


def retarget_actions():
    """Point every existing action's fcurves at the renamed bones.

    Blender remaps an action's bone paths when you rename a bone -- but only for actions its
    animation data can see, and rest_pose() above has just DETACHED the action so that nothing
    measured here is contaminated by Idle's frame-1 offsets. So the rename lands on the bones
    and misses the curves, and Idle and Walk are left keying `pose.bones["Thigh.L"]` on a rig
    that no longer has a Thigh.L.

    That failure is silent and total. The FBX exporter skips any action whose paths do not
    resolve, so `bake_anim_use_all_actions` cheerfully exports NOTHING and the creature arrives
    in Unity with no clips at all -- which is how it got there the first time.
    """
    mapping = {}
    for side, _, _, _, _ in SIDES:
        for old_pat, new_pat in RENAMES:
            mapping[old_pat.format(s=side)] = new_pat.format(s=side)

    fixed = 0
    for act in bpy.data.actions:
        for fc in iter_fcurves(act):
            for old, new in mapping.items():
                needle = 'pose.bones["%s"]' % old
                if needle in fc.data_path:
                    fc.data_path = fc.data_path.replace(needle, 'pose.bones["%s"]' % new)
                    fixed += 1
                    break
    log("retargeted %d fcurve(s) across %d action(s)" % (fixed, len(bpy.data.actions)))


# ─────────── pins ───────────

def pin_material():
    donor = bpy.data.objects.get(PIN_MATERIAL_DONOR)
    if donor is None or not donor.material_slots:
        log("WARNING: no material donor %r; pins will import unremapped"
            % PIN_MATERIAL_DONOR)
        return None
    return donor.material_slots[0].material


def make_pin(name, centre, radius, depth, axis, material):
    """A cylinder whose LONGEST axis is the hinge. That axis is the whole measurement."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=radius, depth=depth,
                                        location=centre)
    obj = bpy.context.active_object
    obj.name = name
    # primitive_cylinder_add builds along local Z; rotate the two other cases onto theirs.
    if axis == 'X':
        obj.rotation_euler = (0.0, 1.5707963267948966, 0.0)
    elif axis == 'Y':
        obj.rotation_euler = (1.5707963267948966, 0.0, 0.0)
    if material is not None:
        obj.data.materials.append(material)
    # rotation_euler does not reach matrix_world until the depsgraph is evaluated, and
    # bone_parent below reads matrix_world to preserve the pin's placement. Without this
    # every pin is bone-parented as though it were still unrotated -- which is not an
    # error anywhere, just ten hinges all measuring the same wrong axis.
    bpy.context.view_layer.update()
    return obj


def build_pins(arm):
    material = pin_material()
    made = 0
    for side, hip_y, knee_y, ankle_y, sole_y in SIDES:
        y_of = {"Coxa_{s}": hip_y, "Hip_{s}": hip_y, "Knee_{s}": knee_y,
                "Ankle_{s}": ankle_y, "Foot_{s}": sole_y}
        for bone_pat, x, z, radius, depth, axis in PINS:
            if depth <= radius * 2.0:
                raise RuntimeError("pin %s is not longer than it is wide; the axle "
                                   "measurement would pick a radial axis" % bone_pat)
            bone = bone_pat.format(s=side)
            name = bone_pat.format(s=side).split("_")[0] + "Pin_" + side
            obj = make_pin(name, Vector((x, y_of[bone_pat], z)), radius, depth, axis,
                           material)
            bone_parent(obj, arm, bone)
            made += 1
    log("built %d pin(s)" % made)


def rebind_soles(arm):
    """Move each sole plate onto its roll joint, so the whole foot pivots with it.

    The ankle keeps its other children, and Foot_ is itself a child of Ankle_, so
    WalkerRig's contact measurement -- the lowest renderer under the ANKLE -- still finds
    the sole exactly where it did.
    """
    for side, _, _, _, _ in SIDES:
        obj = bpy.data.objects.get(SOLE_MESH[side])
        if obj is None:
            log("WARNING: no sole mesh %r" % SOLE_MESH[side])
            continue
        bone_parent(obj, arm, "Foot_" + side)
    log("rebound %d sole mesh(es) onto the roll joint" % len(SIDES))


# ─────────── verification ───────────

def verify(arm):
    """Re-measure what was built the way WalkerRig will, and fail loudly if it is wrong.

    This is the whole point of doing the re-rig in a script: the failure mode of a bad
    pin is not an error, it is a leg that silently solves with a shorter linkage than the
    model has. Checking it here costs nothing and catches it before Unity ever sees it.
    """
    rest_pose(arm)
    ok = True

    for side, _, _, _, _ in SIDES:
        chain = ["Coxa_" + side, "Hip_" + side, "Knee_" + side,
                 "Ankle_" + side, "Foot_" + side]
        for name in chain:
            if arm.data.bones.get(name) is None:
                log("FAIL: bone %s missing" % name)
                ok = False

        # Every pitch pin must be parallel to the others within WalkerRig's 0.99 dot, or
        # Classify keeps only part of the chain.
        axes = {}
        for name in chain:
            pin = bpy.data.objects.get(name.split("_")[0] + "Pin_" + side)
            if pin is None:
                log("FAIL: no pin for %s" % name)
                ok = False
                continue
            axes[name] = longest_extent(pin)

        # Mutual parallelism is what WalkerRig.Classify actually tests, but testing only
        # that passes trivially when every pin shares one WRONG axis -- which is exactly
        # what a stale matrix produced the first time this ran. So check the absolute
        # axis too: the legs swing in the XZ plane, so every pitch hinge is along Y.
        pitch = [axes.get(n) for n in chain[1:4]]
        if all(v is not None for v in pitch):
            for name, other in zip(chain[1:4], pitch):
                if abs(other.dot(Vector((0, 1, 0)))) < 0.99:
                    log("FAIL: %s pin is not along Y (dot=%.4f); the leg swings in XZ"
                        % (name, other.dot(Vector((0, 1, 0)))))
                    ok = False
            for other in pitch[1:]:
                dot = abs(pitch[0].dot(other))
                if dot < 0.99:
                    log("FAIL: %s pitch pins only %.4f parallel (need 0.99)" % (side, dot))
                    ok = False

        yaw = axes.get("Coxa_" + side)
        if yaw is not None and abs(yaw.dot(Vector((0, 0, 1)))) < 0.99:
            log("FAIL: %s coxa pin is not vertical (%.4f)" % (side, yaw.dot(Vector((0, 0, 1)))))
            ok = False

        roll = axes.get("Foot_" + side)
        if roll is not None and abs(roll.dot(Vector((1, 0, 0)))) < 0.99:
            log("FAIL: %s sole pin is not along X (dot=%.4f); a roll hinge runs fore-aft"
                % (side, roll.dot(Vector((1, 0, 0)))))
            ok = False

        # The condition WalkerRig.Measure warns about: Hip_ must lie on the yaw axis.
        coxa_b, hip_b = arm.data.bones["Coxa_" + side], arm.data.bones["Hip_" + side]
        off = (Vector(hip_b.head_local) - Vector(coxa_b.head_local)).xy.length
        if off > 1e-3:
            log("FAIL: %s hip sits %.4f off the yaw axis" % (side, off))
            ok = False

    # Every action must still resolve against the rig. A dangling bone path is not an error
    # anywhere in Blender -- it is an action the FBX exporter quietly declines to export.
    bones = set(b.name for b in arm.data.bones)
    for act in bpy.data.actions:
        referenced = set()
        for fc in iter_fcurves(act):
            m = re.search(r'pose\.bones\["([^"]+)"\]', fc.data_path)
            if m:
                referenced.add(m.group(1))
        dangling = sorted(n for n in referenced if n not in bones)
        if dangling:
            log("FAIL: action %r targets bones that do not exist: %s" % (act.name, dangling))
            ok = False

    # The sole pin must not hang below the sole. WalkerRig measures the contact as the lowest
    # renderer under the ankle, so a pin poking through the bottom of the foot silently becomes
    # the point the creature balances on -- and it is 0.15 units wide, so it would balance on a
    # rod. Caught here because nothing downstream would ever report it.
    for side, _, _, _, _ in SIDES:
        pin = bpy.data.objects.get("FootPin_" + side)
        if pin is None:
            continue
        low = min((pin.matrix_world @ Vector(c)).z for c in pin.bound_box)
        if low <= SOLE_Z:
            log("FAIL: FootPin_%s reaches z=%.4f, at or below the sole at %.4f"
                % (side, low, SOLE_Z))
            ok = False

    # Nothing may have MOVED. Every step here re-parents meshes, and a parent-inverse
    # computed against a stale matrix misplaces a part without erroring -- the same class
    # of bug the pins hit. The floor is the sharpest witness: LightningConjurerBuilder
    # drops the prefab onto BlenderFloor = 2.757, so a sole that shifted takes the whole
    # creature off the ground with it.
    for side, _, _, _, _ in SIDES:
        obj = bpy.data.objects.get(SOLE_MESH[side])
        if obj is None:
            continue
        low = min((obj.matrix_world @ Vector(c)).z for c in obj.bound_box)
        if abs(low - SOLE_Z) > 1e-3:
            log("FAIL: %s sole sits at z=%.4f, expected the floor at %.4f"
                % (side, low, SOLE_Z))
            ok = False

    if not ok:
        raise RuntimeError("verification failed; see the FAIL lines above")
    log("verified: both legs classify as coxa + 3 pitch joints + sole roll, "
        "soles still on the floor, every action still resolves")


def longest_extent(obj):
    """The pin's hinge axis, measured exactly as WalkerAxle.LongestExtent does."""
    m = obj.matrix_world
    corners = [Vector(c) for c in obj.bound_box]
    centre = sum(corners, Vector()) / 8.0
    extents = Vector((max(c.x for c in corners) - centre.x,
                      max(c.y for c in corners) - centre.y,
                      max(c.z for c in corners) - centre.z))
    axes = [m.to_3x3() @ Vector((extents.x, 0, 0)),
            m.to_3x3() @ Vector((0, extents.y, 0)),
            m.to_3x3() @ Vector((0, 0, extents.z))]
    best = max(axes, key=lambda v: v.length_squared)
    return best.normalized()


# ─────────── run ───────────

arm = armature()
rest_pose(arm)
clear_previous(arm)
rest_pose(arm)
rename_bones(arm)
retarget_actions()
insert_bones(arm)
build_pins(arm)
rebind_soles(arm)
verify(arm)

if SAVE:
    bpy.ops.wm.save_mainfile()
    log("SAVED")
else:
    log("dry run; pass -- --save to write the .blend")
