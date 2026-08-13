"""Assemble the dune ornithopter from its components, and rig it.

The wings are single webbed panels — a thin spar skeleton with cloth stretched
between the digits — so unlike the rest of the machine they *deform* rather
than swinging as rigid parts. That splits the rig in two:

    rigid parts   (fuselage, gears, cranks, cradle)  -> bone-parented
    webbed panels (both wings, the tail fan)         -> armature modifier

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
from _ornithopter import (SCALE, SKIN_GROUPS, WRIST, DIGIT_TIPS,  # noqa: E402
                          DIGIT_COUNT, tail_tips, SHARED_COMPONENT_FIXUP)

COMPONENTS = os.path.join(LIB_ROOT, "components")


def sc(v):
    """Authored units -> shipped metres.

    Component meshes are already scaled on the way out of their build scripts,
    so the assembly only scales the *positions* it places them at. Every world
    coordinate in this file goes through here.
    """
    return Vector(v) * SCALE


# ---------------------------------------------------------------------------
# Layout constants, in authored units
# ---------------------------------------------------------------------------

PYLON_ROOT_X = 0.152        # where the pylon bolts to the fuselage rib
SHOULDER_X = 0.940          # shoulder pivot, |x|
SHOULDER_Y = -0.020
SHOULDER_Z = 0.040

WING_DIHEDRAL = math.radians(5.0)

WHEEL_POS = (SHOULDER_X - 0.090, 0.300, 0.150)

CORE_FRONT_Y = -1.05
CORE_REAR_Y = 1.10
BOOM_LEN = 1.92
TAILHUB_Y = CORE_REAR_Y + BOOM_LEN          # 3.02

MESHES = {}
GROUP_ORDER = {}


# ---------------------------------------------------------------------------
# Appending and instancing
# ---------------------------------------------------------------------------

def load_component(relpath, names, skinned=False, scale=1.0):
    """Append the named objects from a component file, keeping their meshes.

    For skinned parts the vertex-group *order* is kept too: weights are stored
    against group indices, so an instance can rename the groups to match a
    side-specific armature as long as it recreates them in the same order.

    `scale` exists for exactly one caller: `shoulder_gear.blend` is shared with
    the two robot models and is pinned to its own span, so the gears arrive
    smaller than the rest of this machine and are brought up here. Applied to
    the mesh *data* rather than the object, so every placement stays at object
    scale 1.0 and the file still needs no apply step.

    Applied once per unique datablock. Several of these meshes are placed
    twice — the bearings, drive wheels and cranks are one per side off shared
    data — and scaling per *name* rather than per datablock would square the
    factor on the second placement.
    """
    path = os.path.join(COMPONENTS, relpath)
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        missing = [n for n in names if n not in set(src.objects)]
        if missing:
            raise SystemExit("%s lacks %s" % (relpath, missing))
        dst.objects = list(names)
    scaled = set()
    for name in names:
        obj = bpy.data.objects[name]
        MESHES[name] = obj.data
        if scale != 1.0 and obj.data.name not in scaled:
            obj.data.transform(Matrix.Diagonal((scale, scale, scale, 1.0)))
            scaled.add(obj.data.name)
        if skinned:
            GROUP_ORDER[name] = [g.name for g in obj.vertex_groups]
            if GROUP_ORDER[name] != SKIN_GROUPS:
                raise SystemExit(
                    "%s has groups %s, expected %s — weights would bind to "
                    "the wrong bones." % (name, GROUP_ORDER[name], SKIN_GROUPS))
        bpy.data.objects.remove(obj)


def place(mesh_name, obj_name, coll, matrix):
    obj = bpy.data.objects.new(obj_name, MESHES[mesh_name])
    obj.matrix_world = matrix
    coll.objects.link(obj)
    return obj


def place_skinned(mesh_name, obj_name, coll, matrix, group_map, mirror=False):
    """Instance a webbed panel, renaming its groups onto one side's bones.

    Two things here are not obvious and both bite silently:

    - Vertex groups belong to the **mesh**, not the object, so an object made
      from an existing mesh already has them. Adding groups by name would
      append duplicates and leave every weight still bound to the old names.
      They have to be renamed in place.
    - Because the names are mesh data, each instance needs its own mesh copy —
      otherwise renaming the left wing's groups silently renames the right
      wing's too.
    """
    mesh = MESHES[mesh_name].copy()
    mesh.name = obj_name
    if mirror:
        mesh.transform(Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0)))
        mesh.flip_normals()

    obj = bpy.data.objects.new(obj_name, mesh)
    coll.objects.link(obj)

    have = [g.name for g in obj.vertex_groups]
    if have != GROUP_ORDER[mesh_name]:
        raise SystemExit("%s: groups %s, expected %s" % (obj_name, have,
                                                        GROUP_ORDER[mesh_name]))
    targets = [group_map[g] for g in have]
    if len(set(targets)) != len(targets):
        raise SystemExit(
            "%s: group map is not one-to-one (%s) — Blender would suffix the "
            "collision and the weights would bind to a bone that is not there."
            % (obj_name, targets))
    for g, target in zip(obj.vertex_groups, targets):
        g.name = target

    obj.matrix_world = matrix
    return obj


def trs(loc, rot_z=0.0, rot_x=0.0, rot_y=0.0):
    return (Matrix.Translation(sc(loc))
            @ Matrix.Rotation(rot_z, 4, 'Z')
            @ Matrix.Rotation(rot_y, 4, 'Y')
            @ Matrix.Rotation(rot_x, 4, 'X'))


def aim_x(start, end, roll=0.0):
    """Matrix putting the origin at `start` with local +X along start→end."""
    start, end = sc(start), sc(end)
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
# Wings
# ---------------------------------------------------------------------------

def wing_matrix(side):
    """Place a wing panel at its shoulder, mirrored and dihedralled per side."""
    return (Matrix.Translation(sc((SHOULDER_X * side, SHOULDER_Y, SHOULDER_Z)))
            @ Matrix.Rotation(-WING_DIHEDRAL * side, 4, 'Y'))


def wing_points(side):
    """Wrist and digit tips of one wing, in world authored units."""
    m = wing_matrix(side) @ Matrix.Diagonal(
        ((-1.0 if side < 0 else 1.0), 1.0, 1.0, 1.0))

    def to_world(p):
        # wing_matrix already carries SCALE via sc(); undo it so callers can
        # keep working in authored units and scale once, at bone creation.
        return (m @ (Vector(p) * SCALE)) / SCALE

    return to_world(WRIST), [to_world(t) for t in DIGIT_TIPS]


def group_map_wing(tag):
    m = {"VG_Root": "Bone_Body", "VG_Arm": "Bone_Arm_%s" % tag}
    for i in range(DIGIT_COUNT):
        m["VG_Digit_%d" % (i + 1)] = "Bone_Digit_%s_%d" % (tag, i + 1)
    return m


def group_map_tail():
    # VG_Root and VG_Arm have to land on *different* bones: the map must stay
    # one-to-one or Blender suffixes the collision into a dead group.
    m = {"VG_Root": "Bone_Boom_2", "VG_Arm": "Bone_TailHub"}
    for i in range(DIGIT_COUNT):
        m["VG_Digit_%d" % (i + 1)] = "Bone_TailDigit_%d" % (i + 1)
    return m


def build_wing(side, coll, variant):
    tag = "R" if side > 0 else "L"
    mirror = side < 0

    place("Mesh_WingFrame_Pylon", "Mesh_Pylon_%s" % tag, coll,
          trs((PYLON_ROOT_X * side, SHOULDER_Y, 0.020),
              rot_z=0.0 if side > 0 else math.pi))
    place("Mesh_ShoulderGear_Bearing", "Mesh_Bearing_%s" % tag, coll,
          trs((SHOULDER_X * side, SHOULDER_Y, SHOULDER_Z)))
    place("Mesh_ShoulderGear_Spoked", "Mesh_DriveWheel_%s" % tag, coll,
          trs((WHEEL_POS[0] * side, WHEEL_POS[1], WHEEL_POS[2])))
    place("Mesh_ShoulderGear_Crank", "Mesh_Crank_%s" % tag, coll,
          aim_x((WHEEL_POS[0] * side, WHEEL_POS[1], WHEEL_POS[2] - 0.040),
                (0.140 * side, 0.430, 0.070)))
    place("Mesh_WingFrame_Strut", "Mesh_Strut_%s" % tag, coll,
          aim_x((0.150 * side, 0.470, 0.120),
                (0.930 * side, -0.090, 0.045)))

    gm = group_map_wing(tag)
    m = wing_matrix(side)
    skins = []
    for part in ("Frame", "Web"):
        skins.append(place_skinned(
            "Mesh_WingPanel_%s_%s" % (variant, part),
            "Mesh_Wing_%s_%s" % (tag, part), coll, m, gm, mirror=mirror))
    return skins


def build_tail(coll):
    place("Mesh_FuselagePod_TailHub", "Mesh_TailHub", coll,
          trs((0, TAILHUB_Y, 0)))
    # The fan's own local +X points outboard; rotate it to fan aft.
    m = trs((0, TAILHUB_Y, -0.020), rot_z=math.pi / 2)
    gm = group_map_tail()
    return [place_skinned("Mesh_WingPanel_TailFan_%s" % part,
                          "Mesh_TailFan_%s" % part, coll, m, gm)
            for part in ("Frame", "Web")]


# ---------------------------------------------------------------------------
# Armature
# ---------------------------------------------------------------------------

def build_armature(coll):
    arm_data = bpy.data.armatures.new("Arm_DuneOrnithopter")
    arm = bpy.data.objects.new("Arm_DuneOrnithopter", arm_data)
    coll.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    def bone(name, head, tail_, parent=None, connect=False):
        b = eb.new(name)
        b.head = sc(head)
        b.tail = sc(tail_)
        if parent is not None:
            b.parent = eb[parent]
            b.use_connect = connect
        return b

    bone("Bone_Root", (0, 0.55, 0), (0, 0.05, 0))
    bone("Bone_Body", (0, 0.55, 0), (0, -1.05, 0), "Bone_Root")
    bone("Bone_Nose", (0, -1.05, 0), (0, -1.95, 0), "Bone_Body", True)
    bone("Bone_Cradle", (0, 0.020, -0.190), (0, 0.020, -0.560), "Bone_Body")

    bone("Bone_Boom_1", (0, 1.10, 0), (0, 2.06, 0), "Bone_Root")
    bone("Bone_Boom_2", (0, 2.06, 0), (0, 3.02, 0), "Bone_Boom_1", True)
    bone("Bone_TailHub", (0, TAILHUB_Y, 0), (0, TAILHUB_Y + 0.24, 0),
         "Bone_Boom_2", True)
    for i, t in enumerate(tail_tips()):
        # The tail fan is placed yawed 90 degrees, so its local +X runs aft.
        d = Vector((-t[1], t[0], t[2]))
        bone("Bone_TailDigit_%d" % (i + 1), (0, TAILHUB_Y, -0.020),
             (d.x, TAILHUB_Y + d.y, -0.020 + d.z), "Bone_TailHub")

    for side, tag in ((1, "R"), (-1, "L")):
        wrist, tips = wing_points(side)
        bone("Bone_Shoulder_%s" % tag,
             (PYLON_ROOT_X * side, SHOULDER_Y, SHOULDER_Z),
             (SHOULDER_X * side, SHOULDER_Y, SHOULDER_Z), "Bone_Body")
        bone("Bone_Gear_%s" % tag,
             (WHEEL_POS[0] * side, WHEEL_POS[1], WHEEL_POS[2]),
             (WHEEL_POS[0] * side, WHEEL_POS[1], WHEEL_POS[2] + 0.24),
             "Bone_Shoulder_%s" % tag)
        bone("Bone_Crank_%s" % tag,
             (WHEEL_POS[0] * side, WHEEL_POS[1], WHEEL_POS[2] - 0.040),
             (0.450 * side, 0.380, 0.100), "Bone_Gear_%s" % tag)
        # Arm and digits lie exactly along the spars they deform.
        bone("Bone_Arm_%s" % tag,
             (SHOULDER_X * side, SHOULDER_Y, SHOULDER_Z), wrist,
             "Bone_Shoulder_%s" % tag)
        for i, tip in enumerate(tips):
            bone("Bone_Digit_%s_%d" % (tag, i + 1), wrist, tip,
                 "Bone_Arm_%s" % tag)

    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def parent_to_bone(obj, arm, bone_name):
    """Bone-parent a rigid part while preserving its placed world transform.

    Bone parenting is relative to the bone *tail*, which is why the world
    matrix is re-applied afterwards rather than computed by hand.
    """
    world = obj.matrix_world.copy()
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    bpy.context.view_layer.update()
    obj.matrix_world = world


def bind_skinned(obj, arm):
    """Deform a webbed panel by the rig rather than carrying it rigidly."""
    world = obj.matrix_world.copy()
    obj.parent = arm
    obj.parent_type = 'OBJECT'
    bpy.context.view_layer.update()
    obj.matrix_world = world
    mod = obj.modifiers.new(name="Armature", type='ARMATURE')
    mod.object = arm
    mod.use_vertex_groups = True


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)

    load_component("structural/wing_panel.blend", [
        "Mesh_WingPanel_Main_Frame", "Mesh_WingPanel_Main_Web",
        "Mesh_WingPanel_Patched_Frame", "Mesh_WingPanel_Patched_Web",
        "Mesh_WingPanel_TailFan_Frame", "Mesh_WingPanel_TailFan_Web",
    ], skinned=True)
    # The one shared component. Pinned to its own span because horse_robot and
    # humanoid_robot append it too — see SHARED_COMPONENT_FIXUP.
    load_component("mechanical/shoulder_gear.blend", [
        "Mesh_ShoulderGear_Spoked", "Mesh_ShoulderGear_Toothed",
        "Mesh_ShoulderGear_Bearing", "Mesh_ShoulderGear_Crank"],
        scale=SHARED_COMPONENT_FIXUP)
    load_component("structural/wing_frame.blend", [
        "Mesh_WingFrame_Pylon", "Mesh_WingFrame_Strut"])
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
    place("Mesh_ShoulderGear_Toothed", "Mesh_DriveCog_Centre", c_body,
          trs((0, 0.560, 0.170)))

    # One wing is the patched variant, so the two sides are not mirror-perfect
    # copies of each other.
    skins = build_wing(1, c_wingR, "Main")
    skins += build_wing(-1, c_wingL, "Patched")
    skins += build_tail(c_tail)

    place("Mesh_ProneCradle_Pad", "Mesh_Cradle_Pad", c_rider,
          trs((0, 0.020, -0.190)))
    place("Mesh_ProneCradle_GripBar", "Mesh_Cradle_GripBar", c_rider,
          trs((0, -0.560, -0.190)))
    for i, sx in enumerate((-1, 1)):
        place("Mesh_ProneCradle_Stirrup", "Mesh_Cradle_Stirrup_%d" % (i + 1),
              c_rider, trs((sx * 0.145, 0.600, -0.190)))

    arm = build_armature(c_rig)

    rigid = {
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
    for tag in ("R", "L"):
        rigid["Mesh_Pylon_%s" % tag] = "Bone_Shoulder_%s" % tag
        rigid["Mesh_Bearing_%s" % tag] = "Bone_Shoulder_%s" % tag
        rigid["Mesh_Strut_%s" % tag] = "Bone_Shoulder_%s" % tag
        rigid["Mesh_DriveWheel_%s" % tag] = "Bone_Gear_%s" % tag
        rigid["Mesh_Crank_%s" % tag] = "Bone_Crank_%s" % tag

    for obj_name, bone_name in rigid.items():
        parent_to_bone(bpy.data.objects[obj_name], arm, bone_name)
    for obj in skins:
        bind_skinned(obj, arm)

    report()
    save(out)


main()
