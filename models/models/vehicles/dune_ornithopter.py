"""Assemble the dune ornithopter from its five components, and rig it.

Appends one mesh datablock per component variation, then places many *object*
instances sharing those datablocks — so twelve blades cost one mesh, not twelve.

Geometry conventions, inherited from the library: −Y forward, +Z up, wings
along ±X, 1 unit = 1 m.

    blender --background --python dune_ornithopter.py -- --out <path>/dune_ornithopter.blend
"""

import math
import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "..", ".."))
from _buildlib import *  # noqa: E402,F403

COMPONENTS = os.path.join(LIB_ROOT, "components")

# ---------------------------------------------------------------------------
# Layout constants — the assembly's single source of numbers
# ---------------------------------------------------------------------------

PYLON_ROOT_X = 0.235        # where the pylon bolts to the fuselage flank
SHOULDER_X = 0.990          # shoulder centre, |x|
SHOULDER_Y = -0.020
SHOULDER_Z = 0.050

HUB_POS = (SHOULDER_X, 0.020, 0.060)
HUB_YAW = math.radians(34.0)    # swings the fan aft so the membrane leads
HUB_PIN_R = 0.230               # radius of the blade pin on the hub lugs

WHEEL_POS = (SHOULDER_X + 0.010, 0.265, 0.165)
COG_POS = (SHOULDER_X - 0.330, 0.455, 0.165)

CORE_FRONT_Y = -1.05
CORE_REAR_Y = 1.10
BOOM_LEN = 1.92
TAILHUB_Y = CORE_REAR_Y + BOOM_LEN          # 3.02
TAIL_SPREAD = math.radians(112.0)
TAIL_PIN_R = 0.185
TAIL_PIN_Z = -0.052

# Fan hub lug geometry — must match components/structural/wing_frame.py.
LUG_COUNT = 5
LUG_SPREAD = math.radians(98.0)


def lug_angle(i):
    return -LUG_SPREAD / 2 + LUG_SPREAD * i / (LUG_COUNT - 1)


# ---------------------------------------------------------------------------
# Appending and instancing
# ---------------------------------------------------------------------------

MESHES = {}


def load_component(relpath, names):
    """Append the named objects from a component file, keeping their meshes."""
    path = os.path.join(COMPONENTS, relpath)
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        missing = [n for n in names if n not in set(src.objects)]
        if missing:
            raise SystemExit("%s lacks %s" % (relpath, missing))
        dst.objects = list(names)
    for name in names:
        obj = bpy.data.objects[name]
        MESHES[name] = obj.data
        # The appended carrier object has done its job; the mesh is what we
        # wanted. Instances are created explicitly below.
        bpy.data.objects.remove(obj)


def place(mesh_name, obj_name, coll, matrix):
    obj = bpy.data.objects.new(obj_name, MESHES[mesh_name])
    obj.matrix_world = matrix
    coll.objects.link(obj)
    return obj


def trs(loc, rot_z=0.0, rot_x=0.0, rot_y=0.0):
    return (Matrix.Translation(Vector(loc))
            @ Matrix.Rotation(rot_z, 4, 'Z')
            @ Matrix.Rotation(rot_y, 4, 'Y')
            @ Matrix.Rotation(rot_x, 4, 'X'))


def aim_x(start, end, roll=0.0):
    """Matrix putting the origin at `start` with local +X along start→end."""
    start, end = Vector(start), Vector(end)
    x = (end - start).normalized()
    up = Vector((0, 0, 1))
    if abs(x.dot(up)) > 0.98:
        up = Vector((0, 1, 0))
    z = x.cross(up).cross(x).normalized()
    y = z.cross(x).normalized()
    m = Matrix((x, y, z)).transposed().to_4x4()
    return (Matrix.Translation(start) @ m
            @ Matrix.Rotation(roll, 4, 'X'))


# ---------------------------------------------------------------------------
# Fan layout
# ---------------------------------------------------------------------------

def fan_lugs(side):
    """World-space (angle, position) of each hub lug for the given side.

    Mirroring a rotation is not a rotation, but the hub's lugs are symmetric
    about its own centreline, so yawing the hub to π − yaw puts the lug *set*
    exactly where a mirrored hub's would be.
    """
    hx = SHOULDER_X * side
    hub = Vector((hx, HUB_POS[1], HUB_POS[2]))
    out = []
    for i in range(LUG_COUNT):
        a = HUB_YAW + lug_angle(i)
        theta = a if side > 0 else math.pi - a
        pos = hub + Vector((HUB_PIN_R * math.cos(theta),
                            HUB_PIN_R * math.sin(theta), 0.0))
        out.append((theta, pos))
    # Sort front-to-back so the membrane always takes the leading socket,
    # whichever way the hub is yawed.
    out.sort(key=lambda t: math.sin(t[0]))
    return out


def build_wing(side, coll, blade_plan):
    tag = "R" if side > 0 else "L"
    yaw = HUB_YAW if side > 0 else math.pi - HUB_YAW

    place("Mesh_WingFrame_Pylon", "Mesh_Pylon_%s" % tag, coll,
          trs((PYLON_ROOT_X * side, -0.020, 0.030),
              rot_z=0.0 if side > 0 else math.pi))
    place("Mesh_ShoulderGear_Bearing", "Mesh_Bearing_%s" % tag, coll,
          trs((SHOULDER_X * side, SHOULDER_Y, SHOULDER_Z)))
    place("Mesh_WingFrame_Hub", "Mesh_WingHub_%s" % tag, coll,
          trs((SHOULDER_X * side, HUB_POS[1], HUB_POS[2]), rot_z=yaw))
    place("Mesh_ShoulderGear_Spoked", "Mesh_DriveWheel_%s" % tag, coll,
          trs((WHEEL_POS[0] * side, WHEEL_POS[1], WHEEL_POS[2])))

    # Crank: rod runs inboard and slightly aft, into the shoulder pad.
    place("Mesh_ShoulderGear_Crank", "Mesh_Crank_%s" % tag, coll,
          aim_x((WHEEL_POS[0] * side, WHEEL_POS[1], WHEEL_POS[2] - 0.055),
                (0.200 * side, 0.450, 0.100)))

    # Bracing tie-rod from the upper body out to the shoulder.
    place("Mesh_WingFrame_Strut", "Mesh_Strut_%s" % tag, coll,
          aim_x((0.210 * side, 0.520, 0.180),
                (1.000 * side, -0.140, 0.060)))

    lugs = fan_lugs(side)
    blades = []
    for i, ((theta, pos), (mesh, twist)) in enumerate(zip(lugs, blade_plan)):
        name = "Mesh_Blade_%s_%d" % (tag, i + 1)
        m = (Matrix.Translation(pos)
             @ Matrix.Rotation(theta - math.pi / 2, 4, 'Z')
             @ Matrix.Rotation(math.radians(6.0), 4, 'X')      # dihedral
             @ Matrix.Rotation(math.radians(twist), 4, 'Y'))   # rest twist
        place(mesh, name, coll, m)
        blades.append((name, theta, pos, mesh))
    return blades


def build_tail(coll):
    place("Mesh_FuselagePod_TailHub", "Mesh_TailHub", coll,
          trs((0, TAILHUB_Y, 0)))
    out = []
    for i in range(5):
        theta = math.pi / 2 + (-TAIL_SPREAD / 2 + TAIL_SPREAD * i / 4)
        pos = Vector((TAIL_PIN_R * math.cos(theta),
                      TAILHUB_Y + TAIL_PIN_R * math.sin(theta),
                      TAIL_PIN_Z))
        name = "Mesh_TailBlade_%d" % (i + 1)
        m = (Matrix.Translation(pos)
             @ Matrix.Rotation(theta - math.pi / 2, 4, 'Z')
             @ Matrix.Rotation(math.radians(-4.0 + 2.0 * i), 4, 'Y'))
        place("Mesh_WingBlade_TailFan", name, coll, m)
        out.append((name, theta, pos))
    return out


# ---------------------------------------------------------------------------
# Armature
# ---------------------------------------------------------------------------

def build_armature(coll, wings, tail):
    arm_data = bpy.data.armatures.new("Arm_DuneOrnithopter")
    arm = bpy.data.objects.new("Arm_DuneOrnithopter", arm_data)
    coll.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def bone(name, head, tail_, parent=None, connect=False):
        b = eb.new(name)
        b.head = Vector(head)
        b.tail = Vector(tail_)
        if parent is not None:
            b.parent = eb[parent]
            b.use_connect = connect
        return b

    bone("Bone_Root", (0, 0.55, 0), (0, 0.05, 0))
    bone("Bone_Body", (0, 0.55, 0), (0, -1.05, 0), "Bone_Root")
    bone("Bone_Nose", (0, -1.05, 0), (0, -1.95, 0), "Bone_Body", True)
    bone("Bone_Cradle", (0, 0.020, -0.268), (0, 0.020, -0.640), "Bone_Body")

    bone("Bone_Boom_1", (0, 1.10, 0), (0, 2.06, 0), "Bone_Root")
    bone("Bone_Boom_2", (0, 2.06, 0), (0, 3.02, 0), "Bone_Boom_1", True)
    bone("Bone_TailHub", (0, 3.02, 0), (0, 3.30, 0), "Bone_Boom_2", True)
    for i, (name, theta, pos) in enumerate(tail):
        d = Vector((math.cos(theta), math.sin(theta), 0)) * 1.05
        bone("Bone_TailBlade_%d" % (i + 1), pos, pos + d, "Bone_TailHub")

    for side, tag, blades in wings:
        sx = SHOULDER_X * side
        bone("Bone_Shoulder_%s" % tag,
             (PYLON_ROOT_X * side, SHOULDER_Y, SHOULDER_Z),
             (sx, SHOULDER_Y, SHOULDER_Z), "Bone_Body")
        bone("Bone_Gear_%s" % tag,
             (WHEEL_POS[0] * side, WHEEL_POS[1], WHEEL_POS[2]),
             (WHEEL_POS[0] * side, WHEEL_POS[1], WHEEL_POS[2] + 0.30),
             "Bone_Shoulder_%s" % tag)
        bone("Bone_Crank_%s" % tag,
             (WHEEL_POS[0] * side, WHEEL_POS[1], WHEEL_POS[2] - 0.055),
             (0.500 * side, 0.380, 0.120), "Bone_Gear_%s" % tag)
        bone("Bone_WingHub_%s" % tag,
             (sx, HUB_POS[1], HUB_POS[2]),
             (sx, HUB_POS[1], HUB_POS[2] + 0.30), "Bone_Shoulder_%s" % tag)
        for i, (name, theta, pos, mesh) in enumerate(blades):
            length = {"Mesh_WingBlade_Membrane": 2.95,
                      "Mesh_WingBlade_Primary": 2.30,
                      "Mesh_WingBlade_Secondary": 1.92,
                      "Mesh_WingBlade_Tattered": 1.92,
                      "Mesh_WingBlade_Covert": 1.48}[mesh]
            d = Vector((math.cos(theta), math.sin(theta), -0.09)) * length
            bone("Bone_Blade_%s_%d" % (tag, i + 1), pos, pos + d,
                 "Bone_WingHub_%s" % tag)

    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def parent_to_bone(obj, arm, bone_name):
    """Bone-parent while preserving the object's placed world transform.

    Bone parenting is relative to the bone *tail*, which is why the world
    matrix is re-applied after the parent is set rather than computed by hand.
    """
    world = obj.matrix_world.copy()
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    bpy.context.view_layer.update()
    obj.matrix_world = world


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)

    load_component("mechanical/wing_blade.blend", [
        "Mesh_WingBlade_Primary", "Mesh_WingBlade_Secondary",
        "Mesh_WingBlade_Covert", "Mesh_WingBlade_Membrane",
        "Mesh_WingBlade_Tattered", "Mesh_WingBlade_TailFan"])
    load_component("mechanical/shoulder_gear.blend", [
        "Mesh_ShoulderGear_Spoked", "Mesh_ShoulderGear_Toothed",
        "Mesh_ShoulderGear_Bearing", "Mesh_ShoulderGear_Crank"])
    load_component("structural/wing_frame.blend", [
        "Mesh_WingFrame_Hub", "Mesh_WingFrame_Pylon", "Mesh_WingFrame_Strut"])
    load_component("structural/fuselage_pod.blend", [
        "Mesh_FuselagePod_Core", "Mesh_FuselagePod_Nose",
        "Mesh_FuselagePod_Boom", "Mesh_FuselagePod_TailHub"])
    load_component("props/prone_cradle.blend", [
        "Mesh_ProneCradle_Pad", "Mesh_ProneCradle_GripBar",
        "Mesh_ProneCradle_Stirrup"])

    root = collection("Coll_DuneOrnithopter")
    c_body = collection("Coll_Ornithopter_Body", root)
    c_wingL = collection("Coll_Ornithopter_WingL", root)
    c_wingR = collection("Coll_Ornithopter_WingR", root)
    c_tail = collection("Coll_Ornithopter_Tail", root)
    c_rider = collection("Coll_Ornithopter_Rider", root)
    c_rig = collection("Coll_Ornithopter_Rig", root)

    place("Mesh_FuselagePod_Core", "Mesh_Fuselage_Core", c_body, trs((0, 0, 0)))
    place("Mesh_FuselagePod_Nose", "Mesh_Fuselage_Nose", c_body,
          trs((0, CORE_FRONT_Y, 0)))
    place("Mesh_FuselagePod_Boom", "Mesh_Fuselage_Boom", c_tail,
          trs((0, CORE_REAR_Y, 0)))
    # One central cog, driven off the body, meshing toward the port wheel.
    place("Mesh_ShoulderGear_Toothed", "Mesh_DriveCog_Centre", c_body,
          trs((0, 0.560, 0.250)))

    # Blade plan per wing: (mesh, rest twist in degrees), leading socket first.
    # The tattered blade sits in a different socket on each side so the two
    # wings do not read as mirrored copies of one another.
    plan_r = [("Mesh_WingBlade_Membrane", -3.0),
              ("Mesh_WingBlade_Primary", 2.0),
              ("Mesh_WingBlade_Secondary", 5.0),
              ("Mesh_WingBlade_Tattered", 8.0),
              ("Mesh_WingBlade_Covert", 11.0)]
    plan_l = [("Mesh_WingBlade_Membrane", -4.0),
              ("Mesh_WingBlade_Primary", 1.0),
              ("Mesh_WingBlade_Tattered", 6.0),
              ("Mesh_WingBlade_Secondary", 9.0),
              ("Mesh_WingBlade_Covert", 12.0)]

    blades_r = build_wing(1, c_wingR, plan_r)
    blades_l = build_wing(-1, c_wingL, plan_l)
    tail = build_tail(c_tail)

    place("Mesh_ProneCradle_Pad", "Mesh_Cradle_Pad", c_rider,
          trs((0, 0.020, -0.268)))
    place("Mesh_ProneCradle_GripBar", "Mesh_Cradle_GripBar", c_rider,
          trs((0, -0.560, -0.268)))
    # One stirrup per foot, side by side — a prone rider's feet do not sit
    # one behind the other.
    for i, sx in enumerate((-1, 1)):
        place("Mesh_ProneCradle_Stirrup", "Mesh_Cradle_Stirrup_%d" % (i + 1),
              c_rider, trs((sx * 0.145, 0.600, -0.268)))

    wings = [(1, "R", blades_r), (-1, "L", blades_l)]
    arm = build_armature(c_rig, wings, tail)

    # --- rig binding -------------------------------------------------------
    bind = {
        "Mesh_Fuselage_Core": "Bone_Body",
        "Mesh_Fuselage_Nose": "Bone_Nose",
        "Mesh_DriveCog_Centre": "Bone_Body",
        "Mesh_Fuselage_Boom": "Bone_Boom_1",
        "Mesh_TailHub": "Bone_TailHub",
        "Mesh_Cradle_Pad": "Bone_Cradle",
        "Mesh_Cradle_GripBar": "Bone_Cradle",
        "Mesh_Cradle_Stirrup_1": "Bone_Cradle",
        "Mesh_Cradle_Stirrup_2": "Bone_Cradle",
    }
    for i in range(5):
        bind["Mesh_TailBlade_%d" % (i + 1)] = "Bone_TailBlade_%d" % (i + 1)
    for side, tag, blades in wings:
        bind["Mesh_Pylon_%s" % tag] = "Bone_Shoulder_%s" % tag
        bind["Mesh_Bearing_%s" % tag] = "Bone_Shoulder_%s" % tag
        bind["Mesh_Strut_%s" % tag] = "Bone_Shoulder_%s" % tag
        bind["Mesh_DriveWheel_%s" % tag] = "Bone_Gear_%s" % tag
        bind["Mesh_Crank_%s" % tag] = "Bone_Crank_%s" % tag
        bind["Mesh_WingHub_%s" % tag] = "Bone_WingHub_%s" % tag
        for i in range(len(blades)):
            bind["Mesh_Blade_%s_%d" % (tag, i + 1)] = \
                "Bone_Blade_%s_%d" % (tag, i + 1)

    for obj_name, bone_name in bind.items():
        parent_to_bone(bpy.data.objects[obj_name], arm, bone_name)

    report()
    save(out)


main()
