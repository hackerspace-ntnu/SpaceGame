"""Give the hand-sculpted Vrescal its four legs, hips and shoulders.

**This is an edit script, not a generator.** `vrescal.blend` was modelled by
hand -- head, jaw, eyes, neck spikes, the whole overlapping armour stack and the
tail -- and none of that exists anywhere else. Every operation below is either
additive or one of three specific fixes the author asked for, and the script
refuses to run at all if the file is not in the state it was written against.
Re-running it after further hand edits will therefore fail loudly rather than
quietly wrecking them.

A copy of the file as it stood before this ran is at
`Assets/Game/Art/Models/_backups~/vrescal_before_legs.blend`. It was untracked
in git, so that copy was the only backup in existence.

## What it does

Authorised edits to existing geometry:

  1. Applies rotation and scale, then recalculates normals. Every sculpted
     object carried negative scale on all three axes -- a negative determinant,
     so the normals were inside out.
  2. Re-centres the armour rings on y = 0. The head sat on the centreline but
     the plate stack had drifted up to 0.62 m to starboard.
  3. Replaces the four rough front-limb blobs with built legs.

Additive:

  4. Four legs from `components/organic/`, plus shoulder and hip haunches --
     the body had one mass peak at mid-length and nothing for a rear limb to
     attach to.
  5. A mirror of the one neck spike that had no counterpart.
  6. An armature. Only the new limb geometry is bound to it; the sculpted body
     is left unparented, so binding the plate stack stays the author's call.

Housekeeping, visually identity-preserving:

  7. Descriptive names for the sculpted objects, which were all `Icosphere.0xx`.
  8. The 23 ad-hoc materials collapse onto the two palette entries they already
     matched exactly (`#E7B345` and `#987340`).

## Scale

The sculpt is worked at roughly 3.63x final size -- 19.9 m nose to tail, for an
animal intended to ship at ~5.5 m. Nothing here rescales the sculpt; the
components are authored at final size and their mesh data is scaled up by `S`
on the way in, so the file stays exactly the size the author has been working
at. `vrescal_export.py` applies the shrink on the way out to Unity.

    blender --background --python vrescal_legs.py
"""

import math
import os
import sys

import bpy
import bmesh
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
COMPONENTS = os.path.join(LIB, "components", "organic")
sys.path.insert(0, LIB)
sys.path.insert(0, COMPONENTS)

from _buildlib import link_materials                               # noqa: E402
from _organic import fan_tips                                      # noqa: E402

SRC = os.path.join(HERE, "vrescal.blend")

# Working units per final real-world metre. The sculpt is 19.936 units nose to
# tail and ships at 5.5 m.
S = 19.936 / 5.5

GROUND = -3.35          # sole plane, ~1.43 units (0.39 m) below the belly line

# ---------------------------------------------------------------------------
# Joint positions, in the sculpt's working units. +X is forward.
#
# Derived from the sculpt rather than chosen: the shoulder sits where the
# author's own rough limbs sprouted (x = -3.4), and the hip is one croc trunk
# length behind it (26% of body length), which lands at x = -8.8 -- just where
# the armour stack starts tapering into tail.
#
# The lateral figures are set by the haunches, not by the legs. A haunch is only
# visible where it reaches past the flank, so the sockets sit a little outboard
# of the body wall (2.61 at the shoulder, 1.97 at the hip) rather than inside
# it.
# ---------------------------------------------------------------------------
JOINTS = {
    "FrontP": [(-3.40, 2.60, -0.60), (-3.80, 4.30, -1.80), (-3.25, 5.05, -3.04)],
    "FrontS": [(-3.40, -2.60, -0.60), (-3.80, -4.30, -1.80), (-3.25, -5.05, -3.04)],
    "RearP": [(-8.80, 2.20, -0.62), (-9.30, 4.00, -1.92), (-8.55, 4.85, -3.04)],
    "RearS": [(-8.80, -2.20, -0.62), (-9.30, -4.00, -1.92), (-8.55, -4.85, -3.04)],
}

# How far each foot toes out from straight ahead. Front feet splay harder than
# rear, which is what the author's own rough limbs already did.
FOOT_YAW = {"FrontP": 30.0, "FrontS": -30.0, "RearP": 20.0, "RearS": -20.0}

# The sculpted rings that make up the body proper. Everything else -- head, jaw,
# eyes, neck spikes -- is deliberately absent: the spikes are arranged
# off-centre on purpose and re-centring them would destroy the arrangement.
BODY_RINGS = ["Icosphere"] + ["Icosphere.%03d" % i for i in
                              (1, 2, 3, 4, 5, 6, 17, 18, 28, 29, 30, 31, 32,
                               33, 34)]

ROUGH_LIMBS = ["Icosphere.%03d" % i for i in (12, 13, 14, 15)]

RENAMES = {
    "Cube": "Mesh_Vrescal_Skull",
    "Cube.002": "Mesh_Vrescal_Jaw",
    "Sphere": "Mesh_Vrescal_EyeS",
    "Sphere.001": "Mesh_Vrescal_EyeP",
    "Icosphere": "Mesh_Vrescal_Thorax",
    "Icosphere.001": "Mesh_Vrescal_Plate_01",
    "Icosphere.002": "Mesh_Vrescal_Plate_02",
    "Icosphere.003": "Mesh_Vrescal_Plate_03",
    "Icosphere.004": "Mesh_Vrescal_Plate_04",
    "Icosphere.005": "Mesh_Vrescal_Plate_05",
    "Icosphere.006": "Mesh_Vrescal_Plate_06",
    "Icosphere.028": "Mesh_Vrescal_Plate_07",
    "Icosphere.018": "Mesh_Vrescal_Plate_08",
    "Icosphere.017": "Mesh_Vrescal_Plate_09",
    "Icosphere.029": "Mesh_Vrescal_Plate_10",
    "Icosphere.030": "Mesh_Vrescal_Plate_11",
    "Icosphere.031": "Mesh_Vrescal_Plate_12",
    "Icosphere.032": "Mesh_Vrescal_Plate_13",
    "Icosphere.033": "Mesh_Vrescal_Plate_14",
    "Icosphere.034": "Mesh_Vrescal_TailKeel",
    "Icosphere.007": "Mesh_Vrescal_Spike_01",
    "Icosphere.008": "Mesh_Vrescal_Spike_02",
    "Icosphere.009": "Mesh_Vrescal_Spike_03",
    "Icosphere.010": "Mesh_Vrescal_Spike_04",
    "Icosphere.011": "Mesh_Vrescal_Spike_05",
    "Icosphere.016": "Mesh_Vrescal_Spike_06",
}

HIDE = "Mat_Hide_Sand_Pale"
PLATE = "Mat_Hide_Plate_Tan"
HORN = "Mat_Hide_Claw_Horn"

# The two colours the author actually used, mapped onto the palette entries that
# were created from them. Both are exact, so this changes no pixel.
COLOUR_MAP = {"E7B345": HIDE, "987340": PLATE, "4A3D2E": HORN}


# ---------------------------------------------------------------------------
# Guards
# ---------------------------------------------------------------------------

def check_expected_state():
    """Refuse to touch the file unless it is what this script was written for.

    A generator that runs blind over a hand-edited .blend destroys work that
    exists nowhere else. This cannot make that safe, but it can make it loud.
    """
    names = {o.name for o in bpy.data.objects}
    expected = set(RENAMES) | set(ROUGH_LIMBS)
    missing = sorted(expected - names)
    if missing:
        raise SystemExit(
            "vrescal.blend is not in the state this script was written "
            "against -- missing %s.\n"
            "It has been hand-edited since. Re-measure it and update the "
            "constants rather than running this." % ", ".join(missing))
    if any(n.startswith("Mesh_Vrescal_") for n in names):
        raise SystemExit(
            "This script has already been run against vrescal.blend "
            "(Mesh_Vrescal_* objects present). Running it twice would add a "
            "second set of legs. Restore from _backups~ if you need to redo "
            "it.")
    shared = [o.name for o in bpy.data.objects
              if o.type == 'MESH' and o.data.users > 1]
    if shared:
        raise SystemExit(
            "Mesh data shared between objects: %s.\n"
            "Applying transforms would corrupt every user of a shared mesh."
            % ", ".join(shared))


# ---------------------------------------------------------------------------
# Authorised fixes to the sculpt
# ---------------------------------------------------------------------------

def apply_rotation_and_scale(obj):
    """Bake rotation and scale into the mesh and flip normals outward.

    Every sculpted object carried scale like (-3.78, -2.24, -2.02). Three
    negative axes give a negative determinant, which mirrors the mesh and turns
    it inside out; Blender's viewport hides this but Unity's lighting will not.
    Location is left alone -- the origin is meaningful.
    """
    loc = obj.location.copy()
    rot_scale = Matrix.Translation(-loc) @ obj.matrix_basis
    obj.data.transform(rot_scale)
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    obj.location = loc

    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()


def recentre_on_spine(obj):
    """Slide a body ring so its own y midpoint sits on zero.

    Per-ring rather than one global shift: the drift is not constant along the
    body (0.03 at the neck, 0.62 at the shoulder hump), so a single offset would
    fix one station and worsen another. Each ring is meant to be radially
    symmetric about the spine, which makes its own midpoint the right target.

    Reads vertices in local space and adds the object's location by hand.
    `matrix_world` would be the obvious way and it is a trap here: it is only
    refreshed when the depsgraph evaluates, so straight after
    `apply_rotation_and_scale` it still carries the old rotation and scale.
    Multiplying already-baked vertices by it applies the transform twice, and
    the ring gets shifted by roughly its own width instead of by its drift.
    """
    ys = [v.co.y for v in obj.data.vertices]
    mid = (min(ys) + max(ys)) * 0.5 + obj.location.y
    obj.location.y -= mid
    return mid


# ---------------------------------------------------------------------------
# Components
# ---------------------------------------------------------------------------

_scaled = set()


def load_component(blend_name, object_name):
    """Append one component object and scale its mesh to working units.

    The scale goes into the mesh data, not the object, so every object in the
    file still reads scale 1.0 -- which is the whole reason the sculpt's
    negative scales were a problem in the first place.
    """
    path = os.path.join(COMPONENTS, blend_name)
    with bpy.data.libraries.load(path, link=False) as (src, dst):
        if object_name not in src.objects:
            raise SystemExit("%s has no object %s" % (blend_name, object_name))
        dst.objects = [object_name]
    obj = dst.objects[0]
    mesh = obj.data
    if mesh.name not in _scaled:
        mesh.transform(Matrix.Diagonal((S, S, S, 1.0)))
        _scaled.add(mesh.name)
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    return obj


def instance(source, name, coll, location, rotation=None, mirror=False):
    """A new object over a copy of `source`'s mesh, mirrored in y if asked.

    Limb segments and feet are built symmetric about their own y = 0, so the
    port and starboard copies could share one datablock and differ only by
    rotation. They deliberately do not: the FBX exporter cannot register
    material indices for two objects over one mesh and drops materials from one
    of them, and a mesh datablock still called `Mesh_LimbSegment_FemoralHeavy`
    inside an object called `Mesh_Vrescal_RearS_Upper` is a name mismatch a
    later reader has to unpick. Copies cost a few thousand triangles.
    """
    mesh = source.data.copy()
    mesh.name = name
    if mirror:
        mesh.transform(Matrix.Diagonal((1.0, -1.0, 1.0, 1.0)))
        mesh.flip_normals()
    obj = bpy.data.objects.new(name, mesh)
    obj.location = Vector(location)
    if rotation is not None:
        obj.rotation_euler = rotation
    coll.objects.link(obj)
    return obj


def aim_x(direction, up=(0.0, 0.0, 1.0)):
    """Euler angles putting local +X along `direction`, local +Z as up as it can.

    Limb segments are authored along +X, so this is what turns a pair of joint
    positions into a placed bone.
    """
    x = Vector(direction).normalized()
    u = Vector(up)
    if abs(x.dot(u)) > 0.98:
        u = Vector((0.0, 1.0, 0.0))
    y = u.cross(x).normalized()
    z = x.cross(y).normalized()
    return Matrix((x, y, z)).transposed().to_euler()


# ---------------------------------------------------------------------------
# Materials
# ---------------------------------------------------------------------------

def base_hex(mat):
    if not mat or not mat.use_nodes:
        return None
    for node in mat.node_tree.nodes:
        if node.type == 'BSDF_PRINCIPLED':
            c = node.inputs['Base Color'].default_value

            def srgb(v):
                v = max(0.0, min(1.0, v))
                return (v * 12.92 if v <= 0.0031308
                        else 1.055 * (v ** (1 / 2.4)) - 0.055)
            return "".join("%02X" % int(round(srgb(x) * 255)) for x in c[:3])
    return None


def remap_materials(palette):
    """Point the sculpt's ad-hoc materials at the palette entries made from them.

    23 separate datablocks held exactly two colours between them, both of which
    are now `Mat_Hide_*`. Remapping is a no-op visually and is what lets the
    export script localise the palette in one pass.
    """
    swapped = 0
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        for slot in obj.material_slots:
            target = COLOUR_MAP.get(base_hex(slot.material))
            if target and slot.material.name != target:
                slot.material = palette[target]
                swapped += 1
    return swapped


# ---------------------------------------------------------------------------
# Armature
# ---------------------------------------------------------------------------

SPINE = [
    ("Bone_Root", (-5.00, 0.0, GROUND), (-3.00, 0.0, GROUND), None),
    ("Bone_Spine_01", (-8.80, 0.0, 0.00), (-6.50, 0.0, 0.30), "Bone_Root"),
    ("Bone_Spine_02", (-6.50, 0.0, 0.30), (-4.20, 0.0, 0.35), "Bone_Spine_01"),
    ("Bone_Spine_03", (-4.20, 0.0, 0.35), (-2.00, 0.0, 0.30), "Bone_Spine_02"),
    ("Bone_Neck", (-2.00, 0.0, 0.30), (-0.60, 0.0, 0.25), "Bone_Spine_03"),
    ("Bone_Head", (-0.60, 0.0, 0.25), (1.60, 0.0, 0.10), "Bone_Neck"),
    ("Bone_Jaw", (-0.40, 0.0, -0.90), (2.20, 0.0, -1.20), "Bone_Head"),
    ("Bone_Tail_01", (-8.80, 0.0, 0.00), (-10.60, 0.0, 0.10), "Bone_Spine_01"),
    ("Bone_Tail_02", (-10.60, 0.0, 0.10), (-12.40, 0.0, 0.05), "Bone_Tail_01"),
    ("Bone_Tail_03", (-12.40, 0.0, 0.05), (-14.20, 0.0, 0.00), "Bone_Tail_02"),
    ("Bone_Tail_04", (-14.20, 0.0, 0.00), (-15.90, 0.0, -0.05), "Bone_Tail_03"),
    ("Bone_Tail_05", (-15.90, 0.0, -0.05), (-17.60, 0.0, -0.10), "Bone_Tail_04"),
]

LIMB_ROOT = {"FrontP": "Bone_Spine_03", "FrontS": "Bone_Spine_03",
             "RearP": "Bone_Spine_01", "RearS": "Bone_Spine_01"}


def build_armature(coll):
    arm_data = bpy.data.armatures.new("Arm_Vrescal")
    arm_obj = bpy.data.objects.new("Arm_Vrescal", arm_data)
    coll.objects.link(arm_obj)

    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    for name, head, tail, parent in SPINE:
        b = eb.new(name)
        b.head, b.tail = Vector(head), Vector(tail)
        if parent:
            b.parent = eb[parent]
            b.use_connect = (Vector(head) - eb[parent].tail).length < 1e-6

    for limb, (a, b, c) in JOINTS.items():
        toe = Vector(c) + Vector((0.90, math.copysign(0.55, c[1]), -0.24))
        chain = [("Upper", a, b), ("Lower", b, c), ("Foot", c, tuple(toe))]
        prev = LIMB_ROOT[limb]
        for part, head, tail in chain:
            bone = eb.new("Bone_Limb%s_%s" % (limb, part))
            bone.head, bone.tail = Vector(head), Vector(tail)
            bone.parent = eb[prev]
            bone.use_connect = (part != "Upper")
            prev = bone.name

    bpy.ops.object.mode_set(mode='OBJECT')
    return arm_obj


def bind(obj, arm_obj, bone_name):
    """Rigid-parent an object to a bone, keeping it exactly where it is.

    Bone parenting measures from the bone's tail, so the transform has to be
    resolved after the parent is set -- assigning `matrix_world` back does that
    for us. Weighted deformation would smear these: they are rigid parts.
    """
    world = obj.matrix_world.copy()
    obj.parent = arm_obj
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    bpy.context.view_layer.update()
    obj.matrix_world = world


# ---------------------------------------------------------------------------

def main():
    if not os.path.exists(SRC):
        raise SystemExit("No sculpt at %s" % SRC)
    bpy.ops.wm.open_mainfile(filepath=SRC)
    check_expected_state()

    palette = {m.name: m for m in link_materials([HIDE, PLATE, HORN])}

    # -- 1. transforms and normals ------------------------------------------
    for obj in bpy.data.objects:
        if obj.type == 'MESH':
            apply_rotation_and_scale(obj)
    print("Applied rotation/scale and recalculated normals on every mesh.")

    # -- 2. centreline -------------------------------------------------------
    worst = 0.0
    for name in BODY_RINGS:
        worst = max(worst, abs(recentre_on_spine(bpy.data.objects[name])))
    print("Re-centred %d body rings on y=0 (worst drift was %.3f)."
          % (len(BODY_RINGS), worst))

    # -- 3. drop the rough limbs --------------------------------------------
    for name in ROUGH_LIMBS:
        obj = bpy.data.objects[name]
        mesh = obj.data
        bpy.data.objects.remove(obj)
        bpy.data.meshes.remove(mesh)
    print("Removed the four rough front-limb blobs.")

    # -- 4. names ------------------------------------------------------------
    for old, new in RENAMES.items():
        obj = bpy.data.objects[old]
        obj.name = new
        obj.data.name = new

    # -- 5. mirror the unpaired neck spike ----------------------------------
    body = bpy.data.objects["Mesh_Vrescal_Spike_06"].users_collection[0]
    src = bpy.data.objects["Mesh_Vrescal_Spike_06"]
    mesh = src.data.copy()
    mesh.name = "Mesh_Vrescal_Spike_07"
    mesh.transform(Matrix.Diagonal((1.0, -1.0, 1.0, 1.0)))
    mesh.flip_normals()
    twin = bpy.data.objects.new("Mesh_Vrescal_Spike_07", mesh)
    twin.location = (src.location.x, -src.location.y, src.location.z)
    body.objects.link(twin)

    # -- 6. limbs and haunches ----------------------------------------------
    limbs = bpy.data.collections.new("Coll_Vrescal_Limbs")
    bpy.context.scene.collection.children.link(limbs)
    rig_coll = bpy.data.collections.new("Coll_Vrescal_Rig")
    bpy.context.scene.collection.children.link(rig_coll)

    src_upper_f = load_component("limb_segment.blend",
                                 "Mesh_LimbSegment_BrachialHeavy")
    src_lower_f = load_component("limb_segment.blend",
                                 "Mesh_LimbSegment_AntebrachialPlated")
    src_upper_r = load_component("limb_segment.blend",
                                 "Mesh_LimbSegment_FemoralHeavy")
    src_lower_r = load_component("limb_segment.blend",
                                 "Mesh_LimbSegment_CruralRibbed")
    src_manus = load_component("foot_splayed.blend", "Mesh_FootSplayed_Manus4")
    src_pes = load_component("foot_splayed.blend", "Mesh_FootSplayed_Pes5")
    src_claw = load_component("claw_talon.blend", "Mesh_ClawTalon_Digging")
    src_shoulder = load_component("haunch.blend", "Mesh_Haunch_ShoulderBroad")
    src_hip = load_component("haunch.blend", "Mesh_Haunch_HipHeavy")

    placed = []
    for limb, (a, b, c) in JOINTS.items():
        front = limb.startswith("Front")
        port = limb.endswith("P")
        upper = src_upper_f if front else src_upper_r
        lower = src_lower_f if front else src_lower_r
        foot_src = src_manus if front else src_pes

        placed.append((instance(upper, "Mesh_Vrescal_%s_Upper" % limb, limbs,
                                a, aim_x(Vector(b) - Vector(a))),
                       "Bone_Limb%s_Upper" % limb))
        placed.append((instance(lower, "Mesh_Vrescal_%s_Lower" % limb, limbs,
                                b, aim_x(Vector(c) - Vector(b))),
                       "Bone_Limb%s_Lower" % limb))

        yaw = math.radians(FOOT_YAW[limb])
        foot = instance(foot_src, "Mesh_Vrescal_%s_Foot" % limb, limbs, c,
                        (0.0, 0.0, yaw))
        placed.append((foot, "Bone_Limb%s_Foot" % limb))

        # Digging claws on the front feet only. Tip positions come from
        # `_organic.TOE_FANS`, the same table the foot was built from, so they
        # cannot drift out of register with the toes.
        if front:
            spin = Matrix.Rotation(yaw, 4, 'Z')
            for i, (tip, angle) in enumerate(fan_tips("Manus4")):
                pos = Vector(c) + spin @ (Vector(tip) * S)
                claw = instance(src_claw,
                                "Mesh_Vrescal_%s_Claw_%d" % (limb, i + 1),
                                limbs, pos,
                                (0.0, 0.0, yaw + angle))
                placed.append((claw, "Bone_Limb%s_Foot" % limb))

        haunch_src = src_shoulder if front else src_hip
        socket = (a[0], a[1], a[2])
        haunch = instance(haunch_src, "Mesh_Vrescal_%s_Haunch" % limb, limbs,
                          socket, mirror=not port)
        placed.append((haunch, LIMB_ROOT[limb]))

    for src_obj in (src_upper_f, src_lower_f, src_upper_r, src_lower_r,
                    src_manus, src_pes, src_claw, src_shoulder, src_hip):
        bpy.data.objects.remove(src_obj)

    # -- 7. armature ---------------------------------------------------------
    arm_obj = build_armature(rig_coll)
    for obj, bone_name in placed:
        bind(obj, arm_obj, bone_name)
    print("Placed %d limb objects and bound them to Arm_Vrescal." % len(placed))

    # -- 8. materials --------------------------------------------------------
    print("Remapped %d material slots onto the palette." % remap_materials(palette))

    for mat in list(bpy.data.materials):
        if mat.users == 0 and mat.library is None:
            bpy.data.materials.remove(mat)

    bpy.ops.wm.save_as_mainfile(filepath=SRC)
    print("Saved %s" % SRC)


main()
