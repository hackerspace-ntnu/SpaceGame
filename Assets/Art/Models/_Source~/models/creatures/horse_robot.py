"""models/creatures/horse_robot — a rideable quadruped robot horse.

A barrel slung between four mech legs, a jointed neck carrying a robot head, and
a segmented counterweight tail. It is the first machine in this library whose
FRONT AND REAR LEGS ARE DELIBERATELY DIFFERENT, and that is the point of it:
`LegMeasurement` carries a stride PER LEG and nothing had exercised that on a
real rig. Forelegs are the long, straight `Coll_WalkerLeg_Long` linkage; hind
legs are the shorter, more sharply folded `Coll_WalkerLeg_Compact` one, scaled
down again, so the hock is visibly deeper and the two pairs measure differently.

Authoring frame: **-Y forward, +X starboard, +Z up**, the library convention.
Blender's default FBX axis conversion puts -Y on Unity's +Z, which is what makes
`HomeLocal.z` read as fore/aft in Unity with no yaw correction anywhere.

**What the rest pose is for.** `HorseLocomotion` uses `HipBudgetStride`, so a
leg's stride is what its hip pitch can still reach on the ground after the hip
height has been paid for:

    stride = 2 * sqrt(MaxReach^2 - (RestHipHeight * hipHeightFraction)^2) * 0.72

A leg modelled dead straight spends its whole length holding the body up and has
nothing left to step with, so both pairs are posed with a real bend -- the
forelegs at about 86% of their linkage, the hind legs at 84% -- and the printed
rig report at the end of a build is the check that they came out different.

**Every joint has a `*Pin*` cylinder child and every leg has a yaw joint at its
base.** Both are load-bearing. `WalkerRig.MeasureAxle` takes a hinge axis from
the longest extent of the first child whose name contains "Pin", and invariant
I5 says a planar limb cannot hold a planted foot through a turn.

    blender --background --python horse_robot.py -- --out horse_robot.blend
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

# ---------------------------------------------------------------------------
# Layout
#
# Every number below that the runtime also derives is derived here too, from the
# component's own printed joint offsets, so re-scaling a pair of legs re-tunes
# the rest pose instead of needing a second edit somewhere else.
# ---------------------------------------------------------------------------

# Component joint offsets, leg-local, as printed by walker_leg.py.
LEG_SOURCE = {
    "Long":    dict(hip=Vector((0.05, 0.0, 7.60)), knee=Vector((0.80, 0.0, 4.55)),
                    ankle=Vector((0.00, 0.0, 1.95))),
    "Compact": dict(hip=Vector((0.10, 0.0, 6.90)), knee=Vector((1.35, 0.0, 4.20)),
                    ankle=Vector((0.00, 0.0, 1.70))),
}

FORE = dict(variant="Long", scale=0.300, cannon=0.62, hip_z=2.05, reach=0.12,
            x=0.42, y=-0.95, yaw=-90.0)
HIND = dict(variant="Compact", scale=0.265, cannon=0.50, hip_z=1.70, reach=0.22,
            x=0.48, y=0.95, yaw=90.0)

# id -> which pair it belongs to and which side it is on.
LEGS = [("FL", FORE, -1), ("FR", FORE, 1), ("HL", HIND, -1), ("HR", HIND, 1)]

COXA_RISE = 0.20        # the yaw joint sits this far above the hip

BARREL_Y0 = -1.18       # chest
BARREL_Y1 = 1.18        # croup
BARREL_Z0 = 1.44
BARREL_Z1 = 2.28
BARREL_HALF = 0.42

WITHERS_Z = 2.44
SADDLE_Y = -0.15
SADDLE_Z = BARREL_Z1

# Neck joints, base to poll. A horse carries its neck up and forward and then
# breaks over at the poll, which is what the flattening arc here is. Joint pitch
# is ~0.29 m against a 0.34 m vertebra, so the segments overlap slightly at rest
# and the neck reads as continuous rather than as a string of beads.
NECK = [
    Vector((0.0, -1.10, 2.30)),
    Vector((0.0, -1.22, 2.58)),
    Vector((0.0, -1.38, 2.84)),
    Vector((0.0, -1.58, 3.06)),
    Vector((0.0, -1.82, 3.22)),
    Vector((0.0, -2.08, 3.30)),
]
VERT_SCALE = 0.30
HEAD_TIP = Vector((0.0, -2.52, 2.92))

TAIL = [
    Vector((0.0, 1.16, 2.16)),
    Vector((0.0, 1.49, 2.00)),
    Vector((0.0, 1.79, 1.78)),
    Vector((0.0, 2.04, 1.50)),
]

SOURCES = {}
PLACED = {}


# ---------------------------------------------------------------------------
# Appending components
# ---------------------------------------------------------------------------

def load_component(path, collections):
    """Append the named collections and keep only their mesh datablocks.

    Every placement is a fresh object pointing at shared mesh data, so four legs
    cost one leg's memory and Unity gets four renderers on two meshes rather
    than four copies of them.
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


def place(name, data, coll, matrix=None, loc=None, rot=None):
    """Instance a mesh at a world transform, remembering the matrix.

    The matrix is remembered rather than read back off `matrix_world` later:
    that is depsgraph-backed and stays stale inside the same pass, which hands
    back identity and lands every attached part on top of its own bone.
    """
    if matrix is None:
        matrix = Matrix.Translation(Vector(loc or (0, 0, 0)))
        if rot is not None:
            matrix = matrix @ Euler(
                [math.radians(a) for a in rot], 'XYZ').to_matrix().to_4x4()
    obj = bpy.data.objects.new(name, data)
    obj.matrix_world = matrix
    coll.objects.link(obj)
    PLACED[obj.name] = matrix
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
# Leg posing
# ---------------------------------------------------------------------------

class LegPose:
    """One leg's rest pose, solved so the hoof lands on the ground where the
    gait needs it.

    The component's legs are modelled standing at their own height with the foot
    under the hip. This bends the linkage until the hoof is `reach` along the
    leg's own bend plane and exactly on z = 0, with the cannon left vertical.
    """

    def __init__(self, leg_id, spec, side):
        self.id = leg_id
        self.spec = spec
        s = spec["scale"]
        src = LEG_SOURCE[spec["variant"]]

        hip, knee, ankle = (src["hip"] * s, src["knee"] * s, src["ankle"] * s)
        self.a = (knee - hip).length          # upper
        self.b = (ankle - knee).length        # lower
        self.c = spec["cannon"]               # ankle down to the hoof's sole
        self.reach = self.a + self.b + self.c

        self.yaw = math.radians(spec["yaw"])
        hip_world = Vector((side * spec["x"], spec["y"], spec["hip_z"]))

        # Where the ankle has to be, in the bend plane, for a vertical cannon to
        # put the sole on the ground `reach` out from the hip.
        target = Vector((spec["reach"], -spec["hip_z"] + self.c))
        d = target.length
        if d > (self.a + self.b) * 0.995:
            raise SystemExit(
                "Leg %s cannot reach: needs %.3f m of a %.3f m linkage. Lower "
                "hip_z or lengthen the cannon." % (leg_id, d, self.a + self.b))

        # Two-link IK, elbow kept on the side the art already bends toward.
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
        d_foot = -(new_lower - old_lower)     # keep the cannon vertical

        # A 2-D angle measured as atan2(z, x) turns the other way to a Blender
        # rotation about +Y, hence the negation.
        base = Matrix.Translation(hip_world) @ Matrix.Rotation(self.yaw, 4, 'Z')
        r1 = Matrix.Rotation(-d_upper, 4, 'Y')
        r2 = r1 @ Matrix.Rotation(-d_lower, 4, 'Y')
        r3 = r2 @ Matrix.Rotation(-d_foot, 4, 'Y')

        scale = Matrix.Diagonal((s, s, s, 1.0))
        self.m_upper = base @ r1
        self.m_lower = base @ Matrix.Translation(r1 @ (knee - hip)) @ r2
        self.m_ankle = (base @ Matrix.Translation(r1 @ (knee - hip))
                        @ Matrix.Translation(r2 @ (ankle - knee)) @ r3)
        self.mesh_upper = self.m_upper @ scale
        self.mesh_lower = self.m_lower @ scale

        self.hip = hip_world
        self.knee = self.m_lower.translation
        self.ankle = self.m_ankle.translation
        self.contact = self.m_ankle @ Vector((0.0, 0.0, -self.c))
        self.coxa = hip_world + Vector((0, 0, COXA_RISE))

        # Axes the rig has to measure: the yaw axle is vertical, the three pitch
        # axles are normal to the bend plane, and the sole rolls about the
        # plane's own forward direction.
        self.axle = Matrix.Rotation(self.yaw, 4, 'Z') @ Vector((0, 1, 0))
        self.forward = Matrix.Rotation(self.yaw, 4, 'Z') @ Vector((1, 0, 0))

        # What HipBudgetStride will make of it, so the build can print it.
        self.max_reach = self.reach * 0.97
        self.hip_height = spec["hip_z"]

    def stride(self, hip_height_fraction=0.95, stride_fraction=0.72):
        h = self.hip_height * hip_height_fraction
        budget = math.sqrt(max(self.max_reach * 0.15,
                               self.max_reach ** 2 - h * h))
        return 2.0 * budget * stride_fraction


# ---------------------------------------------------------------------------
# Unique geometry
# ---------------------------------------------------------------------------

def ellipse(half_x, z0, z1, sides=12):
    """A closed profile in the plane across the body: an ellipse from z0 to z1."""
    cz = (z0 + z1) * 0.5
    hz = (z1 - z0) * 0.5
    return [(half_x * math.cos(2 * math.pi * i / sides),
             cz + hz * math.sin(2 * math.pi * i / sides)) for i in range(sides)]


def build_barrel(coll):
    """The body: a lofted barrel, a withers hump, a breast and a croup."""
    p = Part(PALETTE)

    # Lofted along Y (fore-aft). The chest is deep and narrow, the middle is
    # widest, the croup rises and narrows again -- a horse in section.
    sections = [
        (BARREL_Y0 - 0.06, ellipse(0.30, BARREL_Z0 + 0.20, BARREL_Z1 - 0.06)),
        (BARREL_Y0 + 0.28, ellipse(0.39, BARREL_Z0 + 0.02, WITHERS_Z)),
        (-0.30, ellipse(BARREL_HALF, BARREL_Z0, BARREL_Z1 + 0.04)),
        (0.30, ellipse(BARREL_HALF, BARREL_Z0 + 0.04, BARREL_Z1 + 0.02)),
        (BARREL_Y1 - 0.26, ellipse(0.40, BARREL_Z0 + 0.16, BARREL_Z1 + 0.06)),
        (BARREL_Y1 + 0.04, ellipse(0.28, BARREL_Z0 + 0.34, BARREL_Z1 - 0.02)),
    ]
    p.loft(sections, axis='Y', mat=HULL)

    # Shoulder and haunch plates, where a horse's muscle actually reads.
    for sx in (-1, 1):
        p.box((sx * 0.40, FORE["y"] + 0.06, 1.90), (0.16, 0.62, 0.62), OLIVE)
        p.box((sx * 0.44, HIND["y"] - 0.12, 1.78), (0.16, 0.70, 0.66), OLIVE)

    # Breast plate and a rump cap: the two ends of the hull that get hit.
    p.box((0.0, BARREL_Y0 - 0.10, 1.90), (0.52, 0.16, 0.60), DARK)
    p.box((0.0, BARREL_Y1 + 0.06, 1.90), (0.46, 0.16, 0.56), DARK)

    # Belly spine and a service seam down each flank.
    p.box((0.0, 0.0, BARREL_Z0 - 0.02), (0.24, 1.90, 0.10), STEEL)
    for sx in (-1, 1):
        p.seam((sx * 0.41, BARREL_Y0 + 0.30, 2.10), (sx * 0.41, BARREL_Y1 - 0.30, 2.10),
               width=0.05, depth=0.03, axis='X', mat=RUST)

    return p.finish("Mesh_Horse_Barrel", coll)


def build_saddle(coll):
    """The seat. A rider sits here, so it is a real pad with a cantle and a
    pommel rather than a decal on the back."""
    p = Part(PALETTE)
    p.box((0.0, SADDLE_Y, SADDLE_Z + 0.05), (0.62, 0.72, 0.10), RUBBER)
    p.box((0.0, SADDLE_Y - 0.38, SADDLE_Z + 0.16), (0.44, 0.10, 0.22), DARK)   # pommel
    p.box((0.0, SADDLE_Y + 0.38, SADDLE_Z + 0.19), (0.50, 0.10, 0.28), DARK)   # cantle
    for sx in (-1, 1):
        p.box((sx * 0.34, SADDLE_Y, SADDLE_Z + 0.02), (0.06, 0.60, 0.16), STEEL)
        p.cyl((sx * 0.36, SADDLE_Y - 0.02, SADDLE_Z - 0.20), 0.035, 0.34, 'Z', 8, CHROME)
        p.torus((sx * 0.36, SADDLE_Y - 0.02, SADDLE_Z - 0.40), 0.09, 0.022, 'Y',
                16, 6, CHROME)
    return p.finish("Mesh_Horse_Saddle", coll)


def build_neck_frame(coll):
    """The spine tube the vertebra components thread onto, plus the mane rail.

    Bone-parented to `Neck_01`, so it is a single rigid part rather than
    something spanning joints -- anything spanning two moving bones would have
    to be skinned, and this is a robot with visible gaps between its vertebrae.
    """
    p = Part(PALETTE)
    axis = (NECK[1] - NECK[0]).normalized()
    p.cyl(NECK[0] + axis * 0.10, 0.075, 0.28, 'Z', 10, DARK,
          rot=axis.to_track_quat('Z', 'Y').to_matrix().to_4x4())
    p.box((0.0, NECK[0].y + 0.06, NECK[0].z - 0.06), (0.34, 0.20, 0.26), HULL)
    return p.finish("Mesh_Horse_NeckBase", coll)


def build_head(coll):
    """The head: a wedge cranium, a long muzzle, two ears and lit eyes.

    Built here rather than taken from `neck_column.blend`, whose head is a
    beaked bird skull. A horse's head is the one part of this machine that has
    to be its own shape or the whole silhouette reads as something else.
    """
    poll = NECK[-1]
    axis = (HEAD_TIP - poll).normalized()
    length = (HEAD_TIP - poll).length
    up = Vector((0, 0, 1)) - axis * axis.z
    up.normalize()
    side = Vector((1, 0, 0))

    def at(along, lift, lateral=0.0):
        return poll + axis * along + up * lift + side * lateral

    p = Part(PALETTE)
    # Cranium: the wide back third.
    p.box(at(length * 0.16, 0.02), (0.30, 0.30, 0.28), HULL,
          rot=axis.to_track_quat('Y', 'Z').to_matrix().to_4x4())
    # Muzzle: a tapering barrel down to the nose.
    p.cyl(at(length * 0.58, -0.02), 0.115, length * 0.66, 'Z', 12, HULL,
          radius_top=0.085,
          rot=axis.to_track_quat('Z', 'Y').to_matrix().to_4x4())
    # Nose cap and nostril vents.
    p.cyl(at(length * 0.98, -0.02), 0.088, 0.05, 'Z', 12, DARK,
          rot=axis.to_track_quat('Z', 'Y').to_matrix().to_4x4())
    for sx in (-1, 1):
        p.cyl(at(length * 0.90, 0.02, sx * 0.055), 0.022, 0.03, 'X', 8, BLACK)
    # Cheek plates and a browband.
    for sx in (-1, 1):
        p.box(at(length * 0.30, -0.04, sx * 0.135), (0.05, 0.22, 0.20), OLIVE,
              rot=axis.to_track_quat('Y', 'Z').to_matrix().to_4x4())
    p.box(at(length * 0.06, 0.13), (0.30, 0.07, 0.06), COPPER,
          rot=axis.to_track_quat('Y', 'Z').to_matrix().to_4x4())
    # Ears: two swept blades off the poll.
    for sx in (-1, 1):
        p.cyl(at(-0.02, 0.20, sx * 0.09), 0.045, 0.20, 'Z', 8, DARK,
              radius_top=0.012)
    # Eyes.
    for sx in (-1, 1):
        p.cyl(at(length * 0.24, 0.06, sx * 0.13), 0.045, 0.05, 'X', 10, AMBER)

    # The geometry is authored in world space and then pivoted on the poll, so the
    # object's own world transform is a translation to the poll -- which is what
    # `attach` has to be handed. Passing identity instead lands the whole head at
    # the bone's tail with its offset thrown away, and it ends up under the belly.
    return p.finish("Mesh_Horse_Head", coll, origin=tuple(poll)), Matrix.Translation(poll)


def build_jaw(coll):
    """The lower jaw, on its own bone so the head can work its mouth."""
    poll = NECK[-1]
    axis = (HEAD_TIP - poll).normalized()
    length = (HEAD_TIP - poll).length
    up = Vector((0, 0, 1)) - axis * axis.z
    up.normalize()
    hinge = poll + axis * (length * 0.20) - up * 0.10

    p = Part(PALETTE)
    p.box(poll + axis * (length * 0.60) - up * 0.11, (0.17, 0.16, 0.09), DARK,
          rot=axis.to_track_quat('Y', 'Z').to_matrix().to_4x4())
    p.box(poll + axis * (length * 0.26) - up * 0.13, (0.21, 0.20, 0.14), STEEL,
          rot=axis.to_track_quat('Y', 'Z').to_matrix().to_4x4())
    return p.finish("Mesh_Horse_Jaw", coll, origin=tuple(hinge)), Matrix.Translation(hinge)


def cannon_mesh(spec):
    """The cannon: ankle down to the fetlock, built with its origin ON the ankle.

    This replaces the component's splayed walking pad, which at horse scale is a
    1.8 m dinner plate. The third pitch segment's LENGTH is what the runtime
    measures as part of `MaxReach`, so it is authored here rather than inherited.
    """
    c = spec["cannon"]
    p = Part(PALETTE)
    p.cyl((0, 0, -c * 0.42), 0.052, c * 0.80, 'Z', 10, CHROME, radius_top=0.044)
    p.box((0, 0, -c * 0.10), (0.13, 0.11, 0.16), DARK)          # ankle block
    p.box((0, 0, -c * 0.82), (0.11, 0.10, 0.10), STEEL)         # fetlock
    for sx in (-1, 1):
        p.cyl((sx * 0.052, 0, -c * 0.50), 0.014, c * 0.52, 'Z', 6, RUST)
    return p


def hoof_mesh(spec):
    """The hoof, origin ON the contact point.

    `WalkerRig.LowestRendererPoint` takes the contact from the lowest renderer
    under the ankle and reports its bounds CENTRE horizontally, so the hoof is
    centred on the cannon's axis: anything else moves the measured contact away
    from the point the leg is actually standing on.
    """
    p = Part(PALETTE)
    p.cyl((0, 0, 0.055), 0.105, 0.11, 'Z', 12, DARK, radius_top=0.082)
    p.cyl((0, 0, 0.012), 0.098, 0.024, 'Z', 12, RUBBER)         # the shoe
    p.box((0, 0, 0.135), (0.13, 0.12, 0.06), STEEL)             # pastern collar
    return p


def pin_mesh():
    """One shared mesh for all twenty axle pins: a stubby bar, long on +Z.

    `WalkerRig.MeasureAxle` takes a joint's hinge axis from the longest extent
    of the first child whose name contains "Pin", so the pin's PROPORTIONS are
    the data -- it has to be unambiguously longer on one axis than the others.
    """
    p = Part(PALETTE)
    p.cyl((0, 0, 0), 0.030, 0.230, 'Z', 10, DARK)
    p.cyl((0, 0, 0.105), 0.042, 0.030, 'Z', 10, CHROME)
    p.cyl((0, 0, -0.105), 0.042, 0.030, 'Z', 10, CHROME)
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

def build_armature(poses, coll):
    """`HORSE_Rig`: a root, four Coxa/Hip/Knee/Ankle/Foot chains, a neck ending
    in a head and jaw, and a tail.

    Only the leg chains carry a name `WalkerRig` recognises. The neck and tail
    are deliberately outside that vocabulary: they are driven by
    `HorseSpineMotion`, not walked on.
    """
    data = bpy.data.armatures.new("HORSE_RigData")
    arm = bpy.data.objects.new("HORSE_Rig", data)
    coll.objects.link(arm)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    edit = data.edit_bones

    root = edit.new("Root")
    root.head = Vector((0, 0, BARREL_Z0))
    root.tail = Vector((0, 0, BARREL_Z1))

    spine = edit.new("Spine")
    spine.head = Vector((0, BARREL_Y1 - 0.10, 2.06))
    spine.tail = Vector((0, BARREL_Y0 + 0.10, 2.16))
    spine.parent = root

    for pose in poses:
        coxa = edit.new("Coxa_%s" % pose.id)
        coxa.head, coxa.tail, coxa.parent = pose.coxa, pose.hip, root

        hip = edit.new("Hip_%s" % pose.id)
        hip.head, hip.tail, hip.parent = pose.hip, pose.knee, coxa
        hip.use_connect = True

        knee = edit.new("Knee_%s" % pose.id)
        knee.head, knee.tail, knee.parent = pose.knee, pose.ankle, hip
        knee.use_connect = True

        ankle = edit.new("Ankle_%s" % pose.id)
        ankle.head, ankle.tail, ankle.parent = pose.ankle, pose.contact, knee
        ankle.use_connect = True

        foot = edit.new("Foot_%s" % pose.id)
        foot.head = pose.contact
        foot.tail = pose.contact + pose.forward * 0.22
        foot.parent, foot.use_connect = ankle, True

    parent = spine
    for i in range(len(NECK) - 1):
        bone = edit.new("Neck_%02d" % (i + 1))
        bone.head, bone.tail = NECK[i], NECK[i + 1]
        bone.parent = parent
        bone.use_connect = i > 0
        parent = bone

    head = edit.new("Head")
    head.head, head.tail = NECK[-1], HEAD_TIP
    head.parent, head.use_connect = parent, True

    poll = NECK[-1]
    axis = (HEAD_TIP - poll).normalized()
    up = (Vector((0, 0, 1)) - axis * axis.z).normalized()
    jaw = edit.new("Jaw")
    jaw.head = poll + axis * ((HEAD_TIP - poll).length * 0.20) - up * 0.10
    jaw.tail = poll + axis * ((HEAD_TIP - poll).length * 0.80) - up * 0.13
    jaw.parent = head

    parent = root
    for i in range(len(TAIL) - 1):
        bone = edit.new("Tail_%02d" % (i + 1))
        bone.head, bone.tail = TAIL[i], TAIL[i + 1]
        bone.parent = parent
        bone.use_connect = i > 0
        parent = bone

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
                   ["Coll_WalkerLeg_Long", "Coll_WalkerLeg_Compact"])
    load_component("mechanical/neck_column.blend",
                   ["Coll_NeckColumn_VertMid", "Coll_NeckColumn_VertSlim",
                    "Coll_NeckColumn_Joint"])
    load_component("mechanical/tail_segment.blend",
                   ["Coll_TailSeg_Slim", "Coll_TailSeg_Patched"])
    load_component("mechanical/shoulder_gear.blend",
                   ["Coll_ShoulderGear_Bearing", "Coll_ShoulderGear_Toothed"])

    body_coll = collection("Horse_Body")
    legs_coll = collection("Horse_Legs")
    neck_coll = collection("Horse_Neck")
    rig_coll = collection("Horse_Rig")

    poses = [LegPose(i, spec, side) for i, spec, side in LEGS]
    arm = build_armature(poses, rig_coll)
    pins = pin_mesh()

    # ---- body -----------------------------------------------------------
    static = [build_barrel(body_coll), build_saddle(body_coll),
              build_neck_frame(body_coll)]
    for obj in static:
        attach(obj, arm, "Root", Matrix.Identity(4))

    # ---- legs -----------------------------------------------------------
    cannons = {k: cannon_mesh(spec).finish("Mesh_Horse_Cannon_%s" % k, legs_coll).data
               for k, spec in (("Fore", FORE), ("Hind", HIND))}
    hooves = {k: hoof_mesh(spec).finish("Mesh_Horse_Hoof_%s" % k, legs_coll).data
              for k, spec in (("Fore", FORE), ("Hind", HIND))}
    # finish() linked a prototype object for each; they are replaced by the
    # per-leg placements below.
    for name in ("Mesh_Horse_Cannon_Fore", "Mesh_Horse_Cannon_Hind",
                 "Mesh_Horse_Hoof_Fore", "Mesh_Horse_Hoof_Hind"):
        bpy.data.objects.remove(bpy.data.objects[name], do_unlink=True)

    for pose in poses:
        pair = "Fore" if pose.spec is FORE else "Hind"
        variant = pose.spec["variant"]

        u = place("Mesh_Leg_%s_Upper" % pose.id,
                  mesh_of("Coll_WalkerLeg_%s" % variant, "Upper"),
                  legs_coll, pose.mesh_upper)
        low = place("Mesh_Leg_%s_Lower" % pose.id,
                    mesh_of("Coll_WalkerLeg_%s" % variant, "Lower"),
                    legs_coll, pose.mesh_lower)
        attach(u, arm, "Hip_%s" % pose.id, pose.mesh_upper)
        attach(low, arm, "Knee_%s" % pose.id, pose.mesh_lower)

        can = place("Mesh_Leg_%s_Cannon" % pose.id, cannons[pair],
                    legs_coll, pose.m_ankle)
        attach(can, arm, "Ankle_%s" % pose.id, pose.m_ankle)

        hoof_m = pose.m_ankle @ Matrix.Translation(Vector((0, 0, -pose.c)))
        hf = place("Mesh_Leg_%s_Hoof" % pose.id, hooves[pair], legs_coll, hoof_m)
        attach(hf, arm, "Foot_%s" % pose.id, hoof_m)

        # Shoulder gear on the coxa, so the yaw joint reads as a driven one.
        gear = mesh_of("Coll_ShoulderGear_%s"
                       % ("Bearing" if pair == "Fore" else "Toothed"))
        gear_scale = 0.55 if pair == "Hind" else 1.10
        m = (Matrix.Translation(pose.coxa)
             @ Matrix.Rotation(pose.yaw, 4, 'Z')
             @ Matrix.Diagonal((gear_scale,) * 3 + (1.0,)))
        g = place("Mesh_Leg_%s_Gear" % pose.id, gear, legs_coll, m)
        attach(g, arm, "Coxa_%s" % pose.id, m)

        # Axle pins. Geometry with a job, not decoration: these are what the
        # rig measures every hinge axis from. The foot's pin is lifted clear of
        # the sole, because `LowestRendererPoint` skips nothing but `COL_` and a
        # pin centred on the contact point would hang through the ground and
        # stand the machine that much high.
        for bone, origin, axis in (
                ("Coxa_%s" % pose.id, pose.coxa, Vector((0, 0, 1))),
                ("Hip_%s" % pose.id, pose.hip, pose.axle),
                ("Knee_%s" % pose.id, pose.knee, pose.axle),
                ("Ankle_%s" % pose.id, pose.ankle, pose.axle),
                ("Foot_%s" % pose.id, pose.contact + Vector((0, 0, 0.16)),
                 pose.forward)):
            m = axis_matrix(origin, axis)
            name = "%sPin_%s" % (bone.split("_")[0], pose.id)
            pin = place(name, pins, legs_coll, m)
            attach(pin, arm, bone, m)

    # ---- neck, head and tail --------------------------------------------
    vert_kinds = ["VertMid", "VertSlim", "VertMid", "VertSlim", "VertMid"]
    for i in range(len(NECK) - 1):
        a, b = NECK[i], NECK[i + 1]
        direction = (b - a).normalized()
        # The vertebra components are authored long on +X; lay that along the
        # neck and let the bone carry the rest.
        m = (Matrix.Translation(a.lerp(b, 0.5))
             @ direction.to_track_quat('X', 'Z').to_matrix().to_4x4()
             @ Matrix.Diagonal((VERT_SCALE, VERT_SCALE, VERT_SCALE, 1.0)))
        v = place("Mesh_Neck_%02d" % (i + 1),
                  mesh_of("Coll_NeckColumn_%s" % vert_kinds[i]), neck_coll, m)
        attach(v, arm, "Neck_%02d" % (i + 1), m)

        j = (Matrix.Translation(a)
             @ direction.to_track_quat('X', 'Z').to_matrix().to_4x4()
             @ Matrix.Diagonal((VERT_SCALE * 1.2,) * 3 + (1.0,)))
        c = place("Mesh_NeckCollar_%02d" % (i + 1),
                  mesh_of("Coll_NeckColumn_Joint"), neck_coll, j)
        attach(c, arm, "Neck_%02d" % (i + 1), j)

    head, head_world = build_head(neck_coll)
    attach(head, arm, "Head", head_world)
    jaw, jaw_world = build_jaw(neck_coll)
    attach(jaw, arm, "Jaw", jaw_world)

    for i in range(len(TAIL) - 1):
        a, b = TAIL[i], TAIL[i + 1]
        direction = (b - a).normalized()
        scale = 0.115 - 0.022 * i
        m = (Matrix.Translation(a.lerp(b, 0.5))
             @ direction.to_track_quat('Y', 'Z').to_matrix().to_4x4()
             @ Matrix.Diagonal((scale, scale, scale, 1.0)))
        t = place("Mesh_Tail_%02d" % (i + 1),
                  mesh_of("Coll_TailSeg_%s" % ("Slim" if i % 2 == 0 else "Patched")),
                  neck_coll, m)
        attach(t, arm, "Tail_%02d" % (i + 1), m)

    dedupe_materials()

    # ---- report ----------------------------------------------------------
    print("\nRig report -- what HorseLocomotion will measure:")
    for pose in poses:
        print("  %-3s linkage=%.3f  maxReach=%.3f  hipHeight=%.3f (%.0f%% of "
              "linkage)  restFootRadius=%.3f  stride=%.3f"
              % (pose.id, pose.reach, pose.max_reach, pose.hip_height,
                 100 * pose.hip_height / pose.reach,
                 math.hypot(pose.contact.x - pose.coxa.x,
                            pose.contact.y - pose.coxa.y),
                 pose.stride()))
    fore = poses[0].stride()
    hind = poses[2].stride()
    print("  fore/hind stride difference: %.3f m (%.1f%%)"
          % (abs(fore - hind), 100 * abs(fore - hind) / max(fore, hind)))
    print("  worst sole-to-ground error: %.5f m"
          % max(abs(p.contact.z) for p in poses))
    print("  withers %.2f m, hip plane %.3f m, foot span %.2f m"
          % (WITHERS_Z, sum(p.hip.z for p in poses) / len(poses),
             max(abs(p.contact.x) for p in poses) * 2))

    report()
    save(out)


build()
