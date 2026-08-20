"""models/creatures/humanoid_robot — an upright two-legged, two-armed robot.

Two legs, two arms, a torso that counter-rotates, and a head. It is the first
machine in this library built for `LeggedLocomotion`'s ARM seam, and the first
whose **knee bends forward**: both shipping machines (the ostrich and the
six-legged crawler) have reverse knees, and `WalkerLimbGeometry.BendSign` is
measured from the rest pose rather than authored, so the only way to find out
whether it handles the other sign is to build one.

Authoring frame: **-Y forward, +X starboard, +Z up**, the library convention.
Blender's default FBX axis conversion puts -Y on Unity's +Z, which is what makes
`HomeLocal.z` read as fore/aft in Unity with no yaw correction anywhere.

─────────── how the knee comes out forward ───────────

`components/mechanical/walker_leg.blend` is authored with **+X as the knee
side** -- the thigh runs down-and-outboard to the knee and the shin runs
down-and-back to the ankle. Which way that reads on the finished machine is
purely which way the leg is yawed about Z:

    yaw = -90 deg  ->  leg-local +X lands on world -Y = FORWARD  -> human knee
    yaw = +90 deg  ->  leg-local +X lands on world +Y = REARWARD -> hock

The horse uses both (forelegs -90, hind legs +90). Every limb here is at -90, so
knees and elbows both break forward, which is the humanoid's whole silhouette.

─────────── why the rest pose is crouched ───────────

`HipBudgetStride` sizes a stride from what is left of the leg once the hip
height has been paid for:

    stride = 2 * sqrt(MaxReach^2 - (RestHipHeight * hipHeightFraction)^2) * 0.72

A leg modelled dead straight has `RestHipHeight ~= MaxReach`, the square root
collapses onto its degenerate floor, and the machine cannot step at all. So the
hip rides at about 94% of the linkage in the rest pose here, and
`HumanoidLocomotion` stands it down further -- the trade the brief calls out,
and the reason `hipHeightFraction` is derived by measurement rather than typed
in. The knee flexion that buys the stride is real and visible; that is what a
hip-budget machine looks like.

─────────── the arm is a leg that is not walked on ───────────

An arm is discovered by WALKING the bone hierarchy from its `Arm_` root (the
classic `Coxa_/Hip_/Knee_/Ankle_/Foot_` name lookup is barred for arms, or a
humanoid sharing ids across the two roles would build its arm out of its leg's
bones). So the arm's joints are named what they are -- Shoulder, Elbow, Wrist --
and every one of them carries a `*Pin*` mesh, because a bone without one is not
followed.

**The shoulder's base axle is VERTICAL, exactly like a coxa's.** That is not
cosmetic. `WalkerRig.Measure` signs the base axle with
`MatchSense(axle, body.up)`, which is arbitrary for an axle lying near-square to
up -- a horizontally-axled shoulder can measure either way between one export
and the next. A vertical shoulder yaw is the known-good shape, and it is also
what invariant I5 asks for: a planar limb cannot hold a target through a turn.

    blender --background --python humanoid_robot.py -- --out humanoid_robot.blend
"""

import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
COMPONENTS = os.path.join(LIB, "components")

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0
    "Mat_Metal_Steel_Dark",      # 1
    "Mat_Metal_Steel_Worn",      # 2
    "Mat_Paint_Olive_Deep",      # 3
    "Mat_Metal_Rust_Heavy",      # 4
    "Mat_Paint_Warn_Red",        # 5
    "Mat_Emissive_Amber",        # 6
    "Mat_Neutral_Black_Matte",   # 7
    "Mat_Metal_Chrome_Scuffed",  # 8
    "Mat_Plastic_Rubber_Black",  # 9
    "Mat_Glass_Canopy_Tinted",   # 10
    "Mat_Metal_Copper_Oxide",    # 11
]
(HULL, DARK, STEEL, OLIVE, RUST, RED, AMBER, BLACK, CHROME, RUBBER, GLASS,
 COPPER) = range(12)

PALETTE = []
SOURCES = {}

# ---------------------------------------------------------------------------
# Layout
#
# Everything the runtime derives is derived here too, off the component's own
# printed joint offsets, so rescaling a limb re-poses the rest pose instead of
# needing a second edit somewhere else.
# ---------------------------------------------------------------------------

# Component joint offsets, limb-local, as printed by walker_leg.py. +X is the
# knee side.
LIMB_SOURCE = {
    "Heavy":   dict(hip=Vector((0.20, 0.0, 7.80)), knee=Vector((1.95, 0.0, 4.35)),
                    ankle=Vector((0.00, 0.0, 1.45)), sole=0.0),
    "Compact": dict(hip=Vector((0.10, 0.0, 6.90)), knee=Vector((1.35, 0.0, 4.20)),
                    ankle=Vector((0.00, 0.0, 1.70)), sole=0.0),
}

# Legs: the heavy linkage, which has the squarest thigh-to-shin ratio of the
# four and so reads as a human leg rather than a bird's.
#
# `ankle` is the ONE number the component cannot supply. Its own foot is a
# splayed pad whose height and width are locked together by a uniform scale, and
# a plantigrade sole wants them separated: a foot long enough to stand on
# (0.30 m) scaled from that pad puts the ankle 0.17 m off the ground, and every
# centimetre of ankle height is a centimetre the KNEE has to give back --
#
#     mid-stance knee break = 2 * acos((hipHeight - ankle) / (thigh + shin))
#
# -- so a tall ankle is a permanent squat. A human ankle sits just above its
# sole; this one is 0.07 m, and the foot below it is modelled here.
LEG = dict(variant="Heavy", scale=0.1135, ankle=0.070,
           foot_toe=0.150, foot_heel=0.150, foot_half_width=0.058,
           x=0.160, hip_z=0.860, reach=0.030, yaw=-90.0)

# Arms: the compact linkage, small enough that the elbow keeps a visible break
# at rest and the IK has somewhere to go.
ARM = dict(variant="Compact", scale=0.098, hand=0.100,
           x=0.235, shoulder_z=1.470, hand_z=0.830, reach=0.020, yaw=-90.0)

COXA_RISE = 0.100       # the leg's yaw joint, above the hip
SHOULDER_RISE = 0.100   # the arm's yaw joint, above the shoulder pitch

PELVIS_Z0, PELVIS_Z1 = 0.795, 1.090
PELVIS_HALF, PELVIS_DEPTH = 0.185, 0.135

CHEST_Z0, CHEST_Z1 = 1.090, 1.560
CHEST_HALF, CHEST_DEPTH = 0.205, 0.150

NECK = [Vector((0.0, 0.00, 1.560)), Vector((0.0, -0.010, 1.660)),
        Vector((0.0, -0.020, 1.750))]
HEAD_TIP = Vector((0.0, -0.070, 1.900))
VERT_SCALE = 0.105


# ---------------------------------------------------------------------------
# Appending components
# ---------------------------------------------------------------------------

def load_component(path, collections):
    """Append the named collections and keep only their mesh datablocks.

    Every placement is a fresh object pointing at shared mesh data, so the two
    legs cost one leg's memory and Unity gets two renderers over one mesh.
    """
    full = os.path.join(COMPONENTS, path)
    if not os.path.exists(full):
        raise SystemExit("Missing component %s -- build it first." % full)
    wanted = list(collections)
    with bpy.data.libraries.load(full, link=False) as (src, dst):
        missing = [c for c in wanted if c not in set(src.collections)]
        if missing:
            raise SystemExit("%s has no %s" % (path, missing))
        dst.collections = list(wanted)

    for name in wanted:
        coll = bpy.data.collections[name]
        SOURCES[name] = {o.name: o.data for o in coll.all_objects
                         if o.type == 'MESH'}
        for o in list(coll.all_objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(coll)


def mesh_of(coll_name, contains=None):
    meshes = SOURCES[coll_name]
    if contains is not None:
        for name, data in meshes.items():
            if contains in name:
                return data
        raise SystemExit("%s has no mesh containing %r" % (coll_name, contains))
    if len(meshes) != 1:
        raise SystemExit("%s holds %d meshes; name one" % (coll_name, len(meshes)))
    return next(iter(meshes.values()))


def place(name, data, coll, matrix):
    obj = bpy.data.objects.new(name, data)
    obj.matrix_world = matrix
    coll.objects.link(obj)
    return obj


def dedupe_materials():
    """Fold any `Mat_X.001` back onto `Mat_X`."""
    folded = 0
    for mat in list(bpy.data.materials):
        base = mat.name[:-4]
        if len(mat.name) > 4 and mat.name[-4] == '.' and mat.name[-3:].isdigit() \
                and base in bpy.data.materials:
            mat.user_remap(bpy.data.materials[base])
            bpy.data.materials.remove(mat)
            folded += 1
    if folded:
        print("  folded %d duplicate material(s) back onto the palette" % folded)


# ---------------------------------------------------------------------------
# Limb posing
# ---------------------------------------------------------------------------

class LimbPose:
    """One limb's rest pose, bent so its tip lands where the machine needs it.

    The component's limbs are modelled standing at their own height with the
    tip under the base. This bends the linkage until the contact is `reach`
    along the limb's own bend plane and `drop` below the first pitch joint,
    with the last segment left vertical.

    Which side the middle joint breaks toward is taken from the ART -- the
    source's own bend -- and never chosen here. That is what puts the knee and
    the elbow on the same side as the mesh's own knuckle, and it is why the
    forward knee is a matter of yawing the whole limb rather than of solving it
    differently.
    """

    def __init__(self, limb_id, spec, side, base_z, tip_z, tip_len, tip_scale):
        self.id = limb_id
        self.spec = spec
        s = spec["scale"]
        src = LIMB_SOURCE[spec["variant"]]

        hip, knee, ankle = (src["hip"] * s, src["knee"] * s, src["ankle"] * s)
        self.a = (knee - hip).length          # upper: thigh / upper arm
        self.b = (ankle - knee).length        # lower: shin / forearm
        self.c = tip_len                      # last joint down to the contact
        self.reach = self.a + self.b + self.c
        self.tip_scale = tip_scale

        self.yaw = math.radians(spec["yaw"])
        base_world = Vector((side * spec["x"], 0.0, base_z))
        drop = base_z - tip_z

        # Where the last pitch joint has to be, in the bend plane, for a
        # vertical last segment to put the contact `drop` below the base.
        target = Vector((spec["reach"], -drop + self.c))
        d = target.length
        if d > (self.a + self.b) * 0.995:
            raise SystemExit(
                "Limb %s cannot reach: needs %.3f m of a %.3f m linkage."
                % (limb_id, d, self.a + self.b))

        interior = math.acos(max(-1.0, min(1.0,
                   (self.a * self.a + d * d - self.b * self.b) / (2 * self.a * d))))
        to_ankle = math.atan2(target.y, target.x)
        old_upper = math.atan2(knee.z - hip.z, knee.x - hip.x)
        old_lower = math.atan2(ankle.z - knee.z, ankle.x - knee.x)
        old_line = math.atan2(ankle.z - hip.z, ankle.x - hip.x)
        side_sign = 1.0 if math.sin(old_upper - old_line) > 0 else -1.0

        new_upper = to_ankle + side_sign * interior
        knee_new = Vector((self.a * math.cos(new_upper), self.a * math.sin(new_upper)))
        new_lower = math.atan2(target.y - knee_new.y, target.x - knee_new.x)

        d_upper = new_upper - old_upper
        d_lower = new_lower - old_lower - d_upper
        d_tip = -(new_lower - old_lower)      # keep the last segment vertical

        # A 2-D angle measured as atan2(z, x) turns the other way to a Blender
        # rotation about +Y, hence the negation.
        base = Matrix.Translation(base_world) @ Matrix.Rotation(self.yaw, 4, 'Z')
        r1 = Matrix.Rotation(-d_upper, 4, 'Y')
        r2 = r1 @ Matrix.Rotation(-d_lower, 4, 'Y')
        r3 = r2 @ Matrix.Rotation(-d_tip, 4, 'Y')

        scale = Matrix.Diagonal((s, s, s, 1.0))
        self.m_upper = base @ r1
        self.m_lower = base @ Matrix.Translation(r1 @ (knee - hip)) @ r2
        self.m_ankle = (base @ Matrix.Translation(r1 @ (knee - hip))
                        @ Matrix.Translation(r2 @ (ankle - knee)) @ r3)
        self.mesh_upper = self.m_upper @ scale
        self.mesh_lower = self.m_lower @ scale
        self.mesh_tip = self.m_ankle @ Matrix.Diagonal(
            (tip_scale, tip_scale, tip_scale, 1.0))

        self.base = base_world                # first pitch joint (hip/shoulder)
        self.knee = self.m_lower.translation
        self.ankle = self.m_ankle.translation
        self.contact = self.m_ankle @ Vector((0.0, 0.0, -self.c))

        # The axes the rig has to measure: the base yaw axle is VERTICAL, the
        # three pitch axles are normal to the bend plane, and the sole rolls
        # about the plane's own forward direction.
        self.axle = Matrix.Rotation(self.yaw, 4, 'Z') @ Vector((0, 1, 0))
        self.forward = Matrix.Rotation(self.yaw, 4, 'Z') @ Vector((1, 0, 0))

        self.max_reach = self.reach * 0.97
        self.base_height = base_z - tip_z

    def flexion(self, hip_height_fraction=1.0):
        """Middle-joint break, in degrees, at a given working height. 0 is a
        dead-straight limb; this is the crouch the stride is bought with."""
        h = self.base_height * hip_height_fraction - self.c
        cos_half = min(1.0, max(-1.0, h / (self.a + self.b)))
        return 2.0 * math.degrees(math.acos(cos_half))

    def stride(self, hip_height_fraction=0.92, stride_fraction=0.72):
        h = self.base_height * hip_height_fraction
        budget = math.sqrt(max(self.max_reach * 0.15,
                               self.max_reach ** 2 - h * h))
        return 2.0 * budget * stride_fraction


# ---------------------------------------------------------------------------
# Unique geometry
# ---------------------------------------------------------------------------

def rounded_rect(half_x, half_y, chamfer):
    """A box profile with its four corners cut. Cheaper than a bevel modifier
    and it survives the FBX round trip with no modifier to apply."""
    hx, hy, c = half_x, half_y, chamfer
    return [(-hx + c, -hy), (hx - c, -hy), (hx, -hy + c), (hx, hy - c),
            (hx - c, hy), (-hx + c, hy), (-hx, hy - c), (-hx, -hy + c)]


def build_pelvis(coll):
    """The hip yoke. Origin on the machine's own origin so `Root` carries it."""
    p = Part(PALETTE)
    p.loft([
        (PELVIS_Z0, rounded_rect(PELVIS_HALF * 0.72, PELVIS_DEPTH * 0.80, 0.035)),
        (PELVIS_Z0 + 0.09, rounded_rect(PELVIS_HALF, PELVIS_DEPTH, 0.045)),
        (PELVIS_Z1 - 0.05, rounded_rect(PELVIS_HALF * 0.95, PELVIS_DEPTH, 0.045)),
        (PELVIS_Z1, rounded_rect(PELVIS_HALF * 0.70, PELVIS_DEPTH * 0.85, 0.035)),
    ], axis='Z', mat=HULL)

    # Hip bearing housings, one either side, on the coxa axis.
    for side in (-1, 1):
        p.cyl((side * LEG["x"], 0.0, LEG["hip_z"] + COXA_RISE * 0.5),
              0.062, COXA_RISE + 0.06, 'Z', 12, DARK)
    p.slab((-PELVIS_HALF * 0.9, -PELVIS_DEPTH - 0.012, PELVIS_Z0 + 0.14),
           (PELVIS_HALF * 0.9, -PELVIS_DEPTH + 0.010, PELVIS_Z0 + 0.24), OLIVE)
    p.rivets((-PELVIS_HALF * 0.8, -PELVIS_DEPTH - 0.014, PELVIS_Z1 - 0.09),
             (PELVIS_HALF * 0.8, -PELVIS_DEPTH - 0.014, PELVIS_Z1 - 0.09),
             7, radius=0.009, height=0.007, axis='Y', mat=CHROME)
    p.bevel(width=0.006)
    return p.finish("Mesh_Humanoid_Pelvis", coll)


def build_chest(coll):
    """Torso shell. This is the piece `HumanoidSpineMotion` counter-rotates, so
    it is one object on one bone rather than plating spread over the rig."""
    p = Part(PALETTE)
    p.loft([
        (CHEST_Z0, rounded_rect(CHEST_HALF * 0.76, CHEST_DEPTH * 0.86, 0.040)),
        (CHEST_Z0 + 0.12, rounded_rect(CHEST_HALF * 0.94, CHEST_DEPTH, 0.050)),
        (CHEST_Z1 - 0.14, rounded_rect(CHEST_HALF, CHEST_DEPTH * 0.96, 0.050)),
        (CHEST_Z1 - 0.03, rounded_rect(CHEST_HALF * 0.82, CHEST_DEPTH * 0.72, 0.040)),
        (CHEST_Z1, rounded_rect(CHEST_HALF * 0.50, CHEST_DEPTH * 0.55, 0.030)),
    ], axis='Z', mat=HULL)

    # Shoulder yokes, out to the arm yaw axis.
    for side in (-1, 1):
        p.slab((side * CHEST_HALF * 0.55, -0.055, CHEST_Z1 - 0.20),
               (side * (ARM["x"] + 0.030), 0.055, CHEST_Z1 - 0.09), STEEL)
        p.cyl((side * ARM["x"], 0.0, ARM["shoulder_z"] + SHOULDER_RISE * 0.5),
              0.055, SHOULDER_RISE + 0.055, 'Z', 12, DARK)

    # Chest face: a sunken panel with a warning stripe and a lamp.
    p.slab((-CHEST_HALF * 0.62, -CHEST_DEPTH - 0.012, CHEST_Z0 + 0.16),
           (CHEST_HALF * 0.62, -CHEST_DEPTH + 0.008, CHEST_Z0 + 0.30), OLIVE)
    p.slab((-CHEST_HALF * 0.62, -CHEST_DEPTH - 0.014, CHEST_Z0 + 0.30),
           (CHEST_HALF * 0.62, -CHEST_DEPTH - 0.004, CHEST_Z0 + 0.34), RED)
    p.cyl((0.0, -CHEST_DEPTH - 0.012, CHEST_Z1 - 0.16), 0.030, 0.024, 'Y', 12, AMBER)
    p.rivets((-CHEST_HALF * 0.86, -CHEST_DEPTH - 0.010, CHEST_Z1 - 0.24),
             (CHEST_HALF * 0.86, -CHEST_DEPTH - 0.010, CHEST_Z1 - 0.24),
             9, radius=0.008, height=0.006, axis='Y', mat=CHROME)
    # Back: a battery hump, so the silhouette is not symmetric front to back.
    p.slab((-CHEST_HALF * 0.55, CHEST_DEPTH - 0.010, CHEST_Z0 + 0.10),
           (CHEST_HALF * 0.55, CHEST_DEPTH + 0.055, CHEST_Z1 - 0.16), STEEL)
    p.bevel(width=0.006)
    return p.finish("Mesh_Humanoid_Chest", coll)


def build_head(coll):
    """A visored sensor head. Origin at the poll, so the `Head` bone carries it
    and `HumanoidSpineMotion` can aim it without a second offset."""
    poll = NECK[-1]
    p = Part(PALETTE)
    up = HEAD_TIP.z - poll.z
    fwd = HEAD_TIP.y - poll.y

    p.loft([
        (0.0, rounded_rect(0.062, 0.058, 0.018)),
        (up * 0.34, rounded_rect(0.088, 0.082, 0.024)),
        (up * 0.80, rounded_rect(0.092, 0.086, 0.024)),
        (up * 1.00, rounded_rect(0.070, 0.066, 0.020)),
    ], axis='Z', mat=HULL, cap=True)

    # Visor: a dark band across the front, with the sensor bar inside it.
    p.slab((-0.078, fwd - 0.030, up * 0.46), (0.078, fwd + 0.062, up * 0.70), BLACK)
    p.slab((-0.058, fwd - 0.036, up * 0.52), (0.058, fwd - 0.026, up * 0.64), GLASS)
    p.cyl((0.0, fwd - 0.040, up * 0.58), 0.014, 0.016, 'Y', 10, AMBER)
    # Ear pods and a crest, so it reads as a head from behind too.
    for side in (-1, 1):
        p.cyl((side * 0.094, 0.010, up * 0.58), 0.026, 0.020, 'X', 10, DARK)
    p.slab((-0.016, -0.010, up * 0.96), (0.016, 0.070, up * 1.10), STEEL)
    p.bevel(width=0.005)
    return p.finish("Mesh_Humanoid_Head", coll, origin=(0, 0, 0))


def foot_mesh(spec):
    """A plantigrade sole, origin at the ankle, standing on z = 0.

    `WalkerRig` takes the contact from the LOWEST point of the meshes under the
    last pitch joint and reports its bounds CENTRE horizontally, so the toe and
    the heel are given equal reach: an asymmetric footprint would move the
    measured contact off the ankle's axis, and every stride would then be aimed
    at a point the leg is not standing on.

    The toe still reads as a toe -- it is the shallower, tapered end, and it is
    where `ArticulatedSole` rolls the machine onto at push-off.
    """
    c, toe, heel, hw = (spec["ankle"], spec["foot_toe"], spec["foot_heel"],
                        spec["foot_half_width"])
    p = Part(PALETTE)
    # Sole plate: full footprint, thin. -Y is forward, so the toe is at -Y.
    p.slab((-hw, -toe, -c), (hw, heel, -c + 0.020), RUBBER)
    # Midfoot block, carrying the ankle.
    p.slab((-hw * 0.86, -toe * 0.42, -c + 0.018), (hw * 0.86, heel * 0.55, -0.004), HULL)
    # Toe ramp: tapers down and forward, which is the shape that rolls.
    p.prism([(-toe, -c + 0.018), (-toe * 0.34, -c + 0.018), (-toe * 0.34, -c + 0.062),
             (-toe * 0.80, -c + 0.048)], hw * 1.5, axis='X', mat=DARK)
    # Heel counter.
    p.slab((-hw * 0.72, heel * 0.50, -c + 0.018), (hw * 0.72, heel * 0.96, -c + 0.058), DARK)
    # Ankle collar, on the joint itself.
    p.cyl((0.0, 0.0, -0.014), 0.036, 0.030, 'Z', 12, CHROME)
    p.rivets((-hw * 0.7, -toe * 0.5, -c + 0.021), (hw * 0.7, -toe * 0.5, -c + 0.021),
             4, radius=0.007, height=0.005, axis='Z', mat=CHROME)
    p.bevel(width=0.005)
    obj = p.finish("_FootProto", bpy.context.scene.collection)
    data = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    return data


def hand_mesh(length):
    """A three-finger gripper, origin at the wrist, palm at -Z.

    `WalkerRig` takes an arm's contact from the LOWEST point of the last pitch
    joint's meshes and reports its bounds CENTRE horizontally, so the palm has
    to be the lowest thing here and it has to be centred on the wrist axis --
    anything else moves the commanded target away from the hand.
    """
    p = Part(PALETTE)
    p.slab((-0.040, -0.026, -length * 0.55), (0.040, 0.026, -0.010), HULL)
    p.cyl((0.0, 0.0, -0.006), 0.032, 0.028, 'Z', 12, CHROME)
    for i, x in enumerate((-0.026, 0.0, 0.026)):
        p.slab((x - 0.010, -0.022 - 0.004 * i, -length),
               (x + 0.010, 0.006, -length * 0.52), DARK)
    p.slab((-0.030, 0.020, -length * 0.86), (0.030, 0.034, -length * 0.50), DARK)
    p.bevel(width=0.004)
    obj = p.finish("_HandProto", bpy.context.scene.collection)
    data = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    return data


def pin_mesh():
    """One shared mesh for every axle pin: a stubby bar, long on +Z.

    `WalkerRig.MeasureAxle` takes a joint's hinge axis from the longest extent
    of the first child whose name contains "Pin", so the pin's PROPORTIONS are
    the data -- it has to be unambiguously longer on one axis than the others.
    """
    p = Part(PALETTE)
    p.cyl((0, 0, 0), 0.017, 0.130, 'Z', 10, DARK)
    p.cyl((0, 0, 0.060), 0.024, 0.018, 'Z', 10, CHROME)
    p.cyl((0, 0, -0.060), 0.024, 0.018, 'Z', 10, CHROME)
    obj = p.finish("_PinProto", bpy.context.scene.collection)
    data = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    return data


def axis_matrix(origin, axis):
    """Place a pin so its long (+Z) side lies along `axis`."""
    return (Matrix.Translation(Vector(origin))
            @ Vector(axis).normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4())


# ---------------------------------------------------------------------------
# Rig
# ---------------------------------------------------------------------------

def build_armature(legs, arms, coll):
    """`HUMANOID_Rig`: a root, two Coxa/Hip/Knee/Ankle/Foot chains, two
    Arm/Shoulder/Elbow/Wrist chains, a chest, a neck and a head.

    Only the leg chains carry names `WalkerRig` assembles BY NAME. The arms are
    rooted `Arm_` and found by walking the hierarchy, which is what leaves their
    joints free to be called what they are. `Chest`, `Neck_*` and `Head` are
    outside the vocabulary entirely: they are driven by `HumanoidSpineMotion`.
    """
    data = bpy.data.armatures.new("HUMANOID_RigData")
    arm = bpy.data.objects.new("HUMANOID_Rig", data)
    coll.objects.link(arm)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    edit = data.edit_bones

    root = edit.new("Root")
    root.head = Vector((0, 0, PELVIS_Z0))
    root.tail = Vector((0, 0, PELVIS_Z1))

    chest = edit.new("Chest")
    chest.head = Vector((0, 0, CHEST_Z0))
    chest.tail = Vector((0, 0, CHEST_Z1))
    chest.parent, chest.use_connect = root, True

    for pose in legs:
        coxa = edit.new("Coxa_%s" % pose.id)
        coxa.head = pose.base + Vector((0, 0, COXA_RISE))
        coxa.tail, coxa.parent = pose.base, root

        hip = edit.new("Hip_%s" % pose.id)
        hip.head, hip.tail, hip.parent = pose.base, pose.knee, coxa
        hip.use_connect = True

        knee = edit.new("Knee_%s" % pose.id)
        knee.head, knee.tail, knee.parent = pose.knee, pose.ankle, hip
        knee.use_connect = True

        ankle = edit.new("Ankle_%s" % pose.id)
        ankle.head, ankle.tail, ankle.parent = pose.ankle, pose.contact, knee
        ankle.use_connect = True

        foot = edit.new("Foot_%s" % pose.id)
        foot.head = pose.contact
        foot.tail = pose.contact + pose.forward * 0.13
        foot.parent, foot.use_connect = ankle, True

    for pose in arms:
        shoulder_root = edit.new("Arm_%s" % pose.id)
        shoulder_root.head = pose.base + Vector((0, 0, SHOULDER_RISE))
        shoulder_root.tail, shoulder_root.parent = pose.base, chest

        upper = edit.new("Shoulder_%s" % pose.id)
        upper.head, upper.tail, upper.parent = pose.base, pose.knee, shoulder_root
        upper.use_connect = True

        elbow = edit.new("Elbow_%s" % pose.id)
        elbow.head, elbow.tail, elbow.parent = pose.knee, pose.ankle, upper
        elbow.use_connect = True

        wrist = edit.new("Wrist_%s" % pose.id)
        wrist.head, wrist.tail, wrist.parent = pose.ankle, pose.contact, elbow
        wrist.use_connect = True

    parent = chest
    for i in range(len(NECK) - 1):
        bone = edit.new("Neck_%02d" % (i + 1))
        bone.head, bone.tail = NECK[i], NECK[i + 1]
        bone.parent = parent
        bone.use_connect = i > 0
        parent = bone

    head = edit.new("Head")
    head.head, head.tail = NECK[-1], HEAD_TIP
    head.parent, head.use_connect = parent, True

    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def bone_matrix(arm, bone_name):
    """The transform Blender applies to a BONE-parented child.

    Bone parenting hangs the child off the bone's TAIL, with the bone's local Y
    running head to tail -- not off its head, which is the trap.
    """
    bone = arm.data.bones[bone_name]
    return (arm.matrix_world @ bone.matrix_local
            @ Matrix.Translation(Vector((0, bone.length, 0))))


def attach(obj, arm, bone_name, world):
    """Bone-parent `obj`, keeping it at `world`.

    `matrix_parent_inverse` is left at identity on purpose: FBX has no such
    concept and silently drops whatever is parked there, which puts every part
    somewhere else the moment it reaches Unity.
    """
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = bone_matrix(arm, bone_name).inverted() @ world


# ---------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    load_component("mechanical/walker_leg.blend",
                   ["Coll_WalkerLeg_Heavy", "Coll_WalkerLeg_Compact"])
    load_component("mechanical/shoulder_gear.blend",
                   ["Coll_ShoulderGear_Bearing", "Coll_ShoulderGear_Spoked"])
    load_component("mechanical/neck_column.blend",
                   ["Coll_NeckColumn_VertSlim", "Coll_NeckColumn_Joint"])

    body_coll = collection("Humanoid_Body")
    limb_coll = collection("Humanoid_Limbs")
    rig_coll = collection("Humanoid_Rig")

    legs = [LimbPose(i, LEG, side, LEG["hip_z"], 0.0, LEG["ankle"], 1.0)
            for i, side in (("L", -1), ("R", 1))]
    arms = [LimbPose(i, ARM, side, ARM["shoulder_z"], ARM["hand_z"],
                     ARM["hand"], 1.0)
            for i, side in (("L", -1), ("R", 1))]

    rig = build_armature(legs, arms, rig_coll)
    pins = pin_mesh()
    hands = hand_mesh(ARM["hand"])
    feet = foot_mesh(LEG)

    # ---- torso, neck and head -------------------------------------------
    attach(build_pelvis(body_coll), rig, "Root", Matrix.Identity(4))
    attach(build_chest(body_coll), rig, "Chest", Matrix.Identity(4))

    for i in range(len(NECK) - 1):
        a, b = NECK[i], NECK[i + 1]
        direction = (b - a).normalized()
        # The vertebra components are authored long on +X; lay that along the
        # neck and let the bone carry the rest.
        m = (Matrix.Translation(a.lerp(b, 0.5))
             @ direction.to_track_quat('X', 'Z').to_matrix().to_4x4()
             @ Matrix.Diagonal((VERT_SCALE,) * 3 + (1.0,)))
        attach(place("Mesh_Humanoid_Neck_%02d" % (i + 1),
                     mesh_of("Coll_NeckColumn_VertSlim"), body_coll, m),
               rig, "Neck_%02d" % (i + 1), m)

        j = (Matrix.Translation(a)
             @ direction.to_track_quat('X', 'Z').to_matrix().to_4x4()
             @ Matrix.Diagonal((VERT_SCALE * 1.25,) * 3 + (1.0,)))
        attach(place("Mesh_Humanoid_NeckCollar_%02d" % (i + 1),
                     mesh_of("Coll_NeckColumn_Joint"), body_coll, j),
               rig, "Neck_%02d" % (i + 1), j)

    head = build_head(body_coll)
    attach(head, rig, "Head", Matrix.Identity(4))

    # ---- legs ------------------------------------------------------------
    for pose in legs:
        u = place("Mesh_Leg_%s_Thigh" % pose.id,
                  mesh_of("Coll_WalkerLeg_Heavy", "Upper"), limb_coll, pose.mesh_upper)
        low = place("Mesh_Leg_%s_Shin" % pose.id,
                    mesh_of("Coll_WalkerLeg_Heavy", "Lower"), limb_coll, pose.mesh_lower)
        attach(u, rig, "Hip_%s" % pose.id, pose.mesh_upper)
        attach(low, rig, "Knee_%s" % pose.id, pose.mesh_lower)

        # The sole hangs off Foot_, the ROLL joint, so the whole foot pivots
        # about the point it is standing on. Its lowest vertex is the contact,
        # and `LowestRendererPoint` searches the Ankle_'s children -- of which
        # Foot_ is one -- so this is what the contact gets measured from.
        f = place("Mesh_Leg_%s_Foot" % pose.id, feet, limb_coll, pose.m_ankle)
        attach(f, rig, "Foot_%s" % pose.id, pose.m_ankle)

        # Hip bearing on the coxa, so the yaw joint reads as a driven one.
        m = (Matrix.Translation(pose.base + Vector((0, 0, COXA_RISE)))
             @ Matrix.Rotation(pose.yaw, 4, 'Z')
             @ Matrix.Diagonal((0.42,) * 3 + (1.0,)))
        attach(place("Mesh_Leg_%s_Bearing" % pose.id,
                     mesh_of("Coll_ShoulderGear_Bearing"), limb_coll, m),
               rig, "Coxa_%s" % pose.id, m)

        # Axle pins. Geometry with a job, not decoration: these are what every
        # hinge axis is measured from. The foot's is lifted clear of the sole,
        # because a pin centred on the contact would hang through the ground and
        # stand the machine that much high.
        for bone, origin, axis in (
                ("Coxa_%s" % pose.id, pose.base + Vector((0, 0, COXA_RISE)),
                 Vector((0, 0, 1))),
                ("Hip_%s" % pose.id, pose.base, pose.axle),
                ("Knee_%s" % pose.id, pose.knee, pose.axle),
                ("Ankle_%s" % pose.id, pose.ankle, pose.axle),
                ("Foot_%s" % pose.id, pose.contact + Vector((0, 0, 0.042)),
                 pose.forward)):
            m = axis_matrix(origin, axis)
            attach(place("%sPin_%s" % (bone.split("_")[0], pose.id), pins,
                         limb_coll, m), rig, bone, m)

    # ---- arms ------------------------------------------------------------
    for pose in arms:
        u = place("Mesh_Arm_%s_Upper" % pose.id,
                  mesh_of("Coll_WalkerLeg_Compact", "Upper"), limb_coll, pose.mesh_upper)
        low = place("Mesh_Arm_%s_Fore" % pose.id,
                    mesh_of("Coll_WalkerLeg_Compact", "Lower"), limb_coll, pose.mesh_lower)
        attach(u, rig, "Shoulder_%s" % pose.id, pose.mesh_upper)
        attach(low, rig, "Elbow_%s" % pose.id, pose.mesh_lower)

        hand_m = pose.m_ankle
        attach(place("Mesh_Arm_%s_Hand" % pose.id, hands, limb_coll, hand_m),
               rig, "Wrist_%s" % pose.id, hand_m)

        m = (Matrix.Translation(pose.base + Vector((0, 0, SHOULDER_RISE)))
             @ Matrix.Rotation(pose.yaw, 4, 'Z')
             @ Matrix.Diagonal((0.30,) * 3 + (1.0,)))
        attach(place("Mesh_Arm_%s_Gear" % pose.id,
                     mesh_of("Coll_ShoulderGear_Spoked"), limb_coll, m),
               rig, "Arm_%s" % pose.id, m)

        for bone, origin, axis in (
                ("Arm_%s" % pose.id, pose.base + Vector((0, 0, SHOULDER_RISE)),
                 Vector((0, 0, 1))),
                ("Shoulder_%s" % pose.id, pose.base, pose.axle),
                ("Elbow_%s" % pose.id, pose.knee, pose.axle),
                ("Wrist_%s" % pose.id, pose.ankle, pose.axle)):
            m = axis_matrix(origin, axis)
            attach(place("%sPin_%s" % (bone.split("_")[0], pose.id), pins,
                         limb_coll, m), rig, bone, m)

    dedupe_materials()

    # ---- report ----------------------------------------------------------
    print("\nRig report -- what HumanoidLocomotion will measure:")
    for pose in legs + arms:
        print("  %-4s linkage=%.4f maxReach=%.4f base=%.4f (%.1f%% of linkage) "
              "restRadius=%.4f"
              % (pose.id, pose.reach, pose.max_reach, pose.base_height,
                 100 * pose.base_height / pose.reach,
                 math.hypot(pose.contact.x - pose.base.x,
                            pose.contact.y - pose.base.y)))
    # The stride budget is sqrt(MaxReach^2 - h^2) with a FLOOR at
    # sqrt(MaxReach * 0.15). Above the height where those meet, the stride
    # stops being geometry and becomes the floor -- the machine is standing too
    # tall to step and the number no longer moves. That crossing is a property
    # of the rig, so it is the honest upper bound on `hipHeightFraction` and it
    # is printed rather than guessed at in Unity.
    leg = legs[0]
    h_floor = math.sqrt(max(0.0, leg.max_reach ** 2 - leg.max_reach * 0.15))
    print("\n  hipHeightFraction sweep (leg L): the trade the brief names --")
    print("  stride budget collapses onto its floor at h=%.4f, i.e. f=%.3f"
          % (h_floor, h_floor / leg.base_height))
    print("  %-6s %-9s %-9s %-9s %-6s"
          % ("f", "workingH", "stride", "kneeBend", "floored"))
    for f in (0.86, 0.88, 0.90, 0.92, 0.94, 0.96, 0.98):
        h = leg.base_height * f
        print("  %-6.2f %-9.4f %-9.4f %-9.1f %-6s"
              % (f, h, leg.stride(f), leg.flexion(f), "YES" if h > h_floor else ""))
    print("\n  worst sole-to-ground error: %.6f m"
          % max(abs(p.contact.z) for p in legs))
    print("  stance width %.3f m, hand rest height %.3f m, head top %.3f m"
          % (max(abs(p.contact.x) for p in legs) * 2, arms[0].contact.z,
             HEAD_TIP.z))
    print("  knee side: leg-local +X yawed by %.0f deg -> world %s"
          % (LEG["yaw"], tuple(round(v, 3) for v in legs[0].forward)))

    report()
    save(out)


build()
