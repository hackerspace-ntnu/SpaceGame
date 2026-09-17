"""Limb armour and hand-and-foot wear, both sides, sized over the Slim mannequin.

    blender --background --python components/apparel/limb_armour.py -- --out components/apparel/limb_armour.blend

Built in place (see `_fit.py`). Each piece is a shell over the mannequin segment it
covers - the same stations the mannequin uses, grown by a clearance - so it can never sit
inside the body. Each binds to that segment's bone and has its origin at the joint.

    Coll_Armour_Greaves      shin plates with a knee cop             -> LeftLeg / RightLeg
    Coll_Armour_Cuisses      thigh plates                            -> LeftUpLeg / RightUpLeg
    Coll_Armour_Sleeves      upper-arm sleeves                       -> LeftArm / RightArm
    Coll_Armour_Vambraces    forearm guards flaring at the elbow     -> LeftForeArm / RightForeArm
    Coll_Armour_Boots        boots with a shaft to the shin          -> LeftFoot / RightFoot
    Coll_Armour_Gloves       mittens and thumbs                      -> LeftHand / RightHand

Generation script -- historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(HERE)))
sys.path.insert(0, HERE)

from mathutils import Vector  # noqa: E402

import _buildlib as B  # noqa: E402
import _fit as F  # noqa: E402

MATS = ["Mat_Paint_Teal_Deep", "Mat_Neutral_Slate_Dark"]
PLATE, LEATHER = range(2)
LIMB = 0.70                   # the Slim build's limb factor, as in human_mannequin.py
CLEAR = 0.022                 # how far a shell stands off the body


def grow(stations, by=CLEAR):
    return [(t, w * LIMB + by, d * LIMB + by) for t, w, d in stations]


def piece(coll, mats, name, bone, joint, a, b, stations, mat, hint=Vector((1, 0, 0))):
    p = B.Part(mats)
    p.segment(a, b, stations, mat, hint)
    F.bind(p.finish(name, coll, origin=joint), bone)


def main():
    out = B.parse_out()
    B.start(out)
    mats = B.link_materials(MATS)
    j = F.body_joints()
    up = Vector((0, 0, 1))

    greaves = B.collection("Coll_Armour_Greaves")
    cuisses = B.collection("Coll_Armour_Cuisses")
    sleeves = B.collection("Coll_Armour_Sleeves")
    vambraces = B.collection("Coll_Armour_Vambraces")
    boots = B.collection("Coll_Armour_Boots")
    gloves = B.collection("Coll_Armour_Gloves")

    for side, s in (("L", "Left"), ("R", "Right")):
        knee, ankle = j[s + "Leg"]
        hip = j[s + "UpLeg"][0]
        piece(greaves, mats, "Mesh_Armour_Greave_" + side, s + "Leg", knee, knee - up * 0.04, ankle + up * 0.1,
              grow([(0.0, 0.1, 0.105), (0.3, 0.105, 0.118), (1.0, 0.07, 0.072)]), PLATE)
        piece(greaves, mats, "Mesh_Armour_KneeCop_" + side, s + "Leg", knee,
              knee + Vector((0, -0.07, 0.05)), knee + Vector((0, -0.075, -0.07)),
              [(0.0, 0.07, 0.04), (0.5, 0.085, 0.05), (1.0, 0.065, 0.035)], PLATE)
        # From just above the hip joint, so the plate covers the thigh's domed top under the tunic.
        piece(cuisses, mats, "Mesh_Armour_Cuisse_" + side, s + "UpLeg", hip, hip + up * 0.04, knee + up * 0.1,
              grow([(0.0, 0.15, 0.145), (1.0, 0.1, 0.1)]), PLATE)

        shoulder, elbow = j[s + "Arm"]
        piece(sleeves, mats, "Mesh_Armour_Sleeve_" + side, s + "Arm", shoulder,
              F.lerp(shoulder, elbow, 0.12), F.lerp(shoulder, elbow, 0.9),
              grow([(0.0, 0.1, 0.1), (1.0, 0.078, 0.078)], CLEAR * 0.8), PLATE, Vector((0, -1, 0)))
        fore, wrist = j[s + "ForeArm"]
        piece(vambraces, mats, "Mesh_Armour_Vambrace_" + side, s + "ForeArm", fore,
              F.lerp(fore, wrist, 0.18), F.lerp(fore, wrist, 0.95),
              grow([(0.0, 0.1, 0.1), (0.35, 0.082, 0.082), (1.0, 0.062, 0.058)], CLEAR * 0.8), PLATE, Vector((0, 0, 1)))

        foot = j[s + "Foot"][0]
        toe_end = j[s + "Toe_End"][1]
        forward = Vector((toe_end.x - foot.x, toe_end.y - foot.y, 0.0)).normalized()
        heel = Vector((foot.x, foot.y, 0.0)) - forward * 0.02
        toe = Vector((toe_end.x, toe_end.y, 0.0)) - forward * 0.09
        lift = Vector((0, 0, 0.09))
        piece(boots, mats, "Mesh_Armour_Boot_" + side, s + "Foot", foot, heel + lift, toe + lift,
              [(0.0, 0.1 + CLEAR, 0.09 + CLEAR * 0.5), (0.5, 0.115 + CLEAR, 0.088 + CLEAR * 0.5), (1.0, 0.1 + CLEAR, 0.07 + CLEAR * 0.5)],
              LEATHER, forward.cross(up))
        piece(boots, mats, "Mesh_Armour_BootShaft_" + side, s + "Foot", foot, foot + up * 0.02, foot + up * 0.26,
              [(0.0, 0.085, 0.09), (1.0, 0.078, 0.08)], LEATHER)

        hand = j[s + "Hand"][0]
        tip = j[s + "HandMiddle4"][1]
        index, pinky = j[s + "HandIndex1"][0], j[s + "HandPinky1"][0]
        piece(gloves, mats, "Mesh_Armour_Glove_" + side, s + "Hand", hand, hand, tip,
              [(0.12, 0.075 + 0.012, 0.035 + 0.012), (0.55, 0.085 + 0.012, 0.04 + 0.012), (1.0, 0.06 + 0.012, 0.03 + 0.012)],
              LEATHER, index - pinky)
        thumb_a, thumb_b = j[s + "HandThumb1"][0], j[s + "HandThumb4"][1]
        piece(gloves, mats, "Mesh_Armour_GloveThumb_" + side, s + "Hand", hand, thumb_a, thumb_b,
              [(0.0, 0.03 + 0.01, 0.03 + 0.01), (1.0, 0.022 + 0.01, 0.022 + 0.01)], LEATHER, Vector((0, 0, 1)))

    B.save(out)
    B.report()


main()
