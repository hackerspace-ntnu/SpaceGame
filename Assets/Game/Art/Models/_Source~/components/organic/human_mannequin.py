"""A general human body: a jointed mannequin on the Nomads' own skeleton.

    blender --background --python components/organic/human_mannequin.py -- --out components/organic/human_mannequin.blend

The template every humanoid in the game is dressed on. It is built on a COPY of
`models/characters/nomad/nomad.blend`'s live armature (`Armature.001`: 65
`mixamorig:` bones, A-pose, armature scale 0.01 exactly as the Nomad ships), so a
character built on it has the Nomads' proportions and plays the Nomads' animations
through Unity's humanoid avatar with no retargeting. The armature is only moved:
hips over x = 0, boot soles on z = 0 (the Nomad's soles sit at z = -0.587).

A stylized mannequin in the Nomads' chunky language, not an anatomical sculpt:
every body segment is its own named object - head, neck, chest, abdomen, pelvis,
clavicles, upper arms, forearms, hands, thumbs, thighs, shins, feet - with a dark
ball at each joint. Each part is bound RIGIDLY to one bone (an Armature modifier and
one full-weight vertex group), the way the Nomad's own parts are bound, so it
animates in Unity as a skinned mesh while staying a separate part to edit, swap or
hide. No object parenting: transforms stay at scale 1.

Three builds, one collection and one armature each, side by side on X:

    Coll_Mannequin_Standard   the Nomad's build
    Coll_Mannequin_Heavy      broad torso, thick limbs
    Coll_Mannequin_Slim       narrow torso, thin long-reading limbs (the sky soldier's)

Origins sit at each part's bone head, the joint it turns about. Forward is -Y.

Generation script -- historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import _buildlib as B  # noqa: E402

NOMAD = os.path.join(LIB, "models", "characters", "nomad", "nomad.blend")
SOURCE_ARMATURE = "Armature.001"
NOMAD_SOLE_Z = -0.587        # lowest point of the Nomad's boots, measured
BONE = "mixamorig:"

MATS = ["Mat_Plastic_Cream_Aged", "Mat_Neutral_Black_Matte"]
SHELL, JOINT = range(2)
RING = 16                    # vertices round every section

# (torso width x, limb width x, X offset of the build)
BUILDS = (
    ("Standard", 1.00, 1.00, 0.0),
    ("Heavy", 1.28, 1.22, 2.8),
    ("Slim", 0.84, 0.70, -2.8),
)


# -- geometry ---------------------------------------------------------------

def ball(part, centre, radius, mat):
    """A joint ball: a short domed segment along Z."""
    c = Vector(centre)
    return part.segment(c - Vector((0, 0, radius * 0.5)), c + Vector((0, 0, radius * 0.5)),
                        [(0.0, radius * 0.87, radius * 0.87), (0.5, radius, radius),
                         (1.0, radius * 0.87, radius * 0.87)], mat, ring=RING)


# -- skeleton ---------------------------------------------------------------

def load_template_armature(coll):
    with bpy.data.libraries.load(NOMAD, link=False) as (src, dst):
        if SOURCE_ARMATURE not in src.objects:
            raise SystemExit("No %s in %s" % (SOURCE_ARMATURE, NOMAD))
        dst.objects = [SOURCE_ARMATURE]
    arm = bpy.data.objects[SOURCE_ARMATURE]
    coll.objects.link(arm)
    return arm


def place_armature(template, build, x_offset, coll):
    arm = template.copy()
    arm.data = template.data.copy()
    arm.name = arm.data.name = "Arm_Mannequin_%s" % build
    coll.objects.link(arm)
    bpy.context.view_layer.update()
    hips = arm.matrix_world @ arm.data.bones[BONE + "Hips"].head_local
    arm.location.x += x_offset - hips.x
    arm.location.z += -NOMAD_SOLE_Z
    bpy.context.view_layer.update()
    return arm


def joints(arm):
    return {b.name[len(BONE):]: (arm.matrix_world @ b.head_local, arm.matrix_world @ b.tail_local)
            for b in arm.data.bones}


def emit(part, name, coll, origin, arm, bone):
    obj = part.finish(name, coll, origin=origin)
    group = obj.vertex_groups.new(name=BONE + bone)
    group.add(range(len(obj.data.vertices)), 1.0, 'REPLACE')
    mod = obj.modifiers.new(name="Armature", type='ARMATURE')
    mod.object = arm
    return obj


# -- the body ---------------------------------------------------------------

def build_body(build, torso, limb, arm, coll, mats):
    j = joints(arm)
    name = lambda part: "Mesh_Mannequin_%s_%s" % (build, part)  # noqa: E731
    up = Vector((0, 0, 1))

    def seg(label, bone, a, b, stations, hint=Vector((1, 0, 0)), mat=SHELL):
        p = B.Part(mats)
        p.segment(a, b, stations, mat, hint, ring=RING)
        emit(p, name(label), coll, j[bone][0], arm, bone)

    def joint(label, bone, centre, radius):
        p = B.Part(mats)
        ball(p, centre, radius, JOINT)
        emit(p, name("Joint_" + label), coll, j[bone][0], arm, bone)

    hips, spine, spine1, spine2 = j["Hips"][0], j["Spine"][0], j["Spine1"][0], j["Spine2"]
    neck, head = j["Neck"][0], j["Head"][0]

    # Torso: three stacked shells, each riding its own spine bone.
    seg("Pelvis", "Hips", hips - up * 0.20, spine,
        [(0.0, 0.24 * torso, 0.15 * torso), (0.55, 0.27 * torso, 0.17 * torso), (1.0, 0.22 * torso, 0.15 * torso)])
    seg("Abdomen", "Spine", spine - up * 0.02, spine1 + up * 0.04,
        [(0.0, 0.20 * torso, 0.14 * torso), (1.0, 0.23 * torso, 0.155 * torso)])
    seg("Chest", "Spine1", spine1 + up * 0.02, spine2[1] - up * 0.02,
        [(0.0, 0.25 * torso, 0.17 * torso), (0.55, 0.31 * torso, 0.20 * torso), (1.0, 0.27 * torso, 0.17 * torso)])
    seg("Neck", "Neck", neck + up * 0.02, head,
        [(0.0, 0.07 * limb, 0.07 * limb), (1.0, 0.062 * limb, 0.062 * limb)])

    # Head: an egg, deeper than wide, carried slightly forward of the neck.
    crown = j["HeadTop_End"][1]
    face = Vector((0, -0.03, 0))
    # The domes add each end section's size again, so the core runs short of the crown.
    seg("Head", "Head", head + face + up * 0.05, crown + face - up * 0.12,
        [(0.0, 0.14, 0.16), (0.5, 0.17, 0.195), (1.0, 0.15, 0.17)], hint=Vector((1, 0, 0)))

    for side, s in (("L", "Left"), ("R", "Right")):
        shoulder, arm_head, arm_tail = j[s + "Shoulder"][0], j[s + "Arm"][0], j[s + "Arm"][1]
        fore_head, fore_tail = j[s + "ForeArm"]
        hand_head = j[s + "Hand"][0]
        finger_tip = j[s + "HandMiddle4"][1]
        thumb_head, thumb_tail = j[s + "HandThumb1"][0], j[s + "HandThumb4"][1]
        index, pinky = j[s + "HandIndex1"][0], j[s + "HandPinky1"][0]
        up_leg_head, up_leg_tail = j[s + "UpLeg"]
        leg_tail = j[s + "Leg"][1]
        foot_head = j[s + "Foot"][0]
        toe_end = j[s + "Toe_End"][1]

        seg("Clavicle_" + side, s + "Shoulder", shoulder, arm_head,
            [(0.0, 0.07 * torso, 0.07 * torso), (1.0, 0.09 * limb, 0.09 * limb)], hint=Vector((0, 0, 1)))
        joint("Shoulder_" + side, s + "Arm", arm_head, 0.105 * limb)
        seg("UpperArm_" + side, s + "Arm", arm_head, arm_tail,
            [(0.08, 0.1 * limb, 0.1 * limb), (0.9, 0.078 * limb, 0.078 * limb)], hint=Vector((0, -1, 0)))
        joint("Elbow_" + side, s + "ForeArm", fore_head, 0.08 * limb)
        seg("Forearm_" + side, s + "ForeArm", fore_head, fore_tail,
            [(0.1, 0.082 * limb, 0.082 * limb), (0.92, 0.06 * limb, 0.055 * limb)], hint=Vector((0, 0, 1)))
        joint("Wrist_" + side, s + "Hand", hand_head, 0.058 * limb)
        # A mitten across the knuckles, and the thumb on its own bone chain's line.
        seg("Hand_" + side, s + "Hand", hand_head, finger_tip,
            [(0.12, 0.075, 0.035), (0.55, 0.085, 0.04), (1.0, 0.06, 0.03)], hint=index - pinky)
        seg("Thumb_" + side, s + "Hand", thumb_head, thumb_tail,
            [(0.0, 0.03, 0.03), (1.0, 0.022, 0.022)], hint=Vector((0, 0, 1)))

        joint("Hip_" + side, s + "UpLeg", up_leg_head, 0.135 * limb)
        seg("Thigh_" + side, s + "UpLeg", up_leg_head, up_leg_tail,
            [(0.08, 0.15 * limb, 0.145 * limb), (0.9, 0.1 * limb, 0.1 * limb)])
        joint("Knee_" + side, s + "Leg", up_leg_tail, 0.1 * limb)
        seg("Shin_" + side, s + "Leg", up_leg_tail, leg_tail,
            [(0.08, 0.1 * limb, 0.105 * limb), (0.35, 0.105 * limb, 0.115 * limb), (0.92, 0.065 * limb, 0.07 * limb)])
        joint("Ankle_" + side, s + "Foot", foot_head, 0.07 * limb)

        # The foot: a boot-shaped blob from behind the heel to the toe tip, sole on the ground.
        forward = Vector((toe_end.x - foot_head.x, toe_end.y - foot_head.y, 0.0)).normalized()
        heel = Vector((foot_head.x, foot_head.y, 0.0)) - forward * 0.02
        toe = Vector((toe_end.x, toe_end.y, 0.0)) - forward * 0.09
        lift = Vector((0, 0, 0.09))
        seg("Foot_" + side, s + "Foot", heel + lift, toe + lift,
            [(0.0, 0.1, 0.09), (0.5, 0.115, 0.088), (1.0, 0.1, 0.07)], hint=forward.cross(up))


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)

    scratch = B.collection("Coll_Mannequin_Source")
    template = load_template_armature(scratch)
    for build, torso, limb, x in BUILDS:
        coll = B.collection("Coll_Mannequin_%s" % build)
        arm = place_armature(template, build, x, coll)
        build_body(build, torso, limb, arm, coll, mats)

    # The appended Nomad armature was only a template to copy; it and its
    # scratch collection were created by this run.
    data = template.data
    bpy.data.objects.remove(template, do_unlink=True)
    bpy.data.armatures.remove(data)
    bpy.data.collections.remove(scratch)

    B.save(out)
    B.report()


main()
