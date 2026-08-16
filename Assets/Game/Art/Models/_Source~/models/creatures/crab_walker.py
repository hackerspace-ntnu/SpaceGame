"""models/creatures/crab_walker — a wide, low walking machine that goes sideways.

A shallow armoured carapace slung between four, six or eight splayed legs, with
two claw-arms carried forward. It is a salvager's machine: it scuttles along a
wreck's flank rather than walking up to it, so its whole geometry is built round
travelling ACROSS its own nose.

    blender --background --python crab_walker.py -- --out crab_walker_6.blend --legs 6

**One script, three variants.** `--legs 4|6|8` changes how many legs are laid
out and nothing else: the same carapace, the same claws, the same rig
convention, the same leg component. `CrabLocomotion` reads the leg count off
whatever the armature turns out to hold and derives its swing count and its
minimum planted count from that, so nothing downstream is authored per variant
either.

Authoring frame: **−Y forward, +X starboard, +Z up**, the library convention.
Blender's default FBX axis conversion puts −Y on Unity's +Z, which is what makes
the Unity-space measurements below come out right with no yaw correction.

─────────── why the legs point fore and aft ───────────

Stride comes from the arc the foot sweeps when its coxa turns:

    strideLength = 2 * RestFootRadius * sin(yawRange * 0.85)

so the direction the machine covers ground fastest in is the TANGENT of that
arc, which is perpendicular to the leg. The desert crawler's legs stick out to
port and starboard, its arcs sweep fore and aft, and it walks along its nose.
Turn that ninety degrees — legs radiating fore and aft, arcs sweeping across the
beam — and you have a machine whose best direction of travel is sideways. That
is the entire idea, and everything else here follows from it: the carapace is
wider than it is deep because that is the axis the feet are spread along, and
the claws go on the nose because it is the face that is not doing the walking.

A leg posed standing straight down would put its foot under its own yaw axis and
give a stride of centimetres whatever the gait is tuned to, so the rest pose is
splayed and the linkage bent to reach it. See the rig report a build prints.
"""

import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, save, start  # noqa: E402

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
COMPONENTS = os.path.join(LIB, "components")

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0
    "Mat_Metal_Steel_Dark",      # 1
    "Mat_Metal_Steel_Worn",      # 2
    "Mat_Paint_Olive_Deep",      # 3
    "Mat_Paint_Roof_Green",      # 4
    "Mat_Metal_Rust_Heavy",      # 5
    "Mat_Paint_Warn_Red",        # 6
    "Mat_Emissive_Amber",        # 7
    "Mat_Neutral_Black_Matte",   # 8
    "Mat_Metal_Chrome_Scuffed",  # 9
    "Mat_Plastic_Rubber_Black",  # 10
    "Mat_Glass_Canopy_Tinted",   # 11
    "Mat_Metal_Copper_Oxide",    # 12
]
(HULL, DARK, STEEL, OLIVE, GREEN, RUST, RED, AMBER, BLACK, CHROME, RUBBER,
 GLASS, COPPER) = range(13)

# ---------------------------------------------------------------------------
# Layout — every number here is read back by the runtime, so they are the model
# ---------------------------------------------------------------------------

# The three numbers that decide whether this machine can walk at all, and the
# relationship between them is the whole tuning problem.
#
# Stride is 2 * FOOT_REACH * sin(yawRange * 0.85), so a wide splay buys stride.
# But the foot then sits sqrt(FOOT_REACH^2 + HIP_Z^2) from its hip BEFORE it has
# taken a step, and half a stride further at the end of one — and the linkage is
# only (a + b + c) long. Measured on the first build at LEG_SCALE 0.35: a rest
# extension of 83% put the worst reach at 1.14 travelling one way and 1.17 along
# the nose, which is a foot visibly detached from its own leg.
#
# The fix is LONGER LEGS rather than a shorter stride: at 0.42 the same splay is
# 72% of the linkage at rest and about 78% at full stretch, which leaves the
# solver a bend to work with on a slope. A crab has thick legs anyway.
LEG_SCALE = 0.42      # the walker_leg component is built for a 20 m machine
CLAW_SCALE = 0.34     # ditto claw_chela, which was built for a crawler's tail

HIP_Z = 1.75          # hip height above the ground at rest
FOOT_REACH = 2.05     # how far outboard of its own coxa axis each foot plants
COXA_RISE = 0.30      # the yaw joint sits this far above the hip

LEG_SPAN_X = 3.00     # |x| of the outermost coxa
ROW_Y = 1.15          # fore row at -ROW_Y, aft row at +ROW_Y
FAN_PER_M = 12.0      # degrees a leg fans outboard per metre of x

SHELL = dict(x=3.40, y=1.95, z0=1.05, z1=2.55)

# The walker_leg component's own joint offsets, printed by walker_leg.py, in the
# component's unscaled space. Scaled here rather than in six places below.
LEG_HIP = Vector((0.20, 0.0, 7.80)) * LEG_SCALE
LEG_KNEE = Vector((1.95, 0.0, 4.35)) * LEG_SCALE
LEG_ANKLE = Vector((0.00, 0.0, 1.45)) * LEG_SCALE

# The claw arm, in its own bend plane: (outward, up) from the shoulder. Three
# pitch segments, which leaves the IK two free links and so the analytic solve.
ARM_SHOULDER = Vector((0.00, 0.00))
ARM_ELBOW = Vector((0.85, 0.30))
ARM_WRIST = Vector((1.60, -0.05))
ARM_TIP = Vector((2.15, -0.45))
# Inboard of the fore legs and further forward than them, so the claws read as
# the machine's face rather than as two more limbs in a crowded corner.
ARM_ROOT = dict(x=1.30, y=-2.10, z=2.35)
ARM_YAW = 74.0        # degrees outboard of dead ahead

# Nothing is dressed the same twice: shroud and weathering rotate down the rows.
SHROUDS = ["Plate", "Ribbed", "Vented", "Patched"]
CHELAE = ["Heavy", "Cutter"]

SOURCES = {}
PLACED = {}


def parse_legs():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    n = int(argv[argv.index("--legs") + 1]) if "--legs" in argv else 6
    if n not in (4, 6, 8):
        raise SystemExit("--legs must be 4, 6 or 8; got %d" % n)
    return n


# ---------------------------------------------------------------------------
# Appending components
# ---------------------------------------------------------------------------

def load_component(path, collections):
    """Append the named collections and keep only their mesh datablocks.

    Every placement below is a fresh object pointing at shared mesh data, so
    eight legs cost one leg's memory and Unity gets eight renderers on one mesh
    rather than eight copies of it.
    """
    full = os.path.join(COMPONENTS, path)
    if not os.path.exists(full):
        raise SystemExit("Missing component %s — build it first." % full)
    wanted = list(collections)
    with bpy.data.libraries.load(full, link=False) as (src, dst):
        missing = [c for c in wanted if c not in set(src.collections)]
        if missing:
            raise SystemExit("%s has no %s" % (path, missing))
        dst.collections = list(wanted)

    for name in wanted:
        coll = bpy.data.collections[name]
        SOURCES[name] = {o.name: o.data for o in coll.all_objects if o.type == 'MESH'}
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
    """Instance a component mesh at a world transform.

    The matrix is built here and remembered rather than being set as loc/rot and
    read back off `matrix_world` later: `matrix_world` is depsgraph-backed and
    stays stale until the view layer updates, so reading it inside the same pass
    hands back an identity and every part attached from it lands on the origin.
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
    """Fold any `Mat_X.001` back onto `Mat_X`, so appending from four component
    files does not leave four copies of the same palette entry."""
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
# Where the legs go
# ---------------------------------------------------------------------------

def leg_layout(count):
    """(id, hip world position, outward yaw in degrees from +X), for `count` legs.

    Two rows, fore and aft, each spread along X — the axis the machine travels
    on. The rows are what the gait waves between and the X spread is what it
    waves ALONG, so a layout with one row, or with every leg at the same x,
    would leave the wave nothing to march down.

    Legs fan outboard with |x| so the outer arcs do not sit on top of one
    another: a row of parallel legs sweeps a row of parallel arcs, and the
    machine ends up narrower at the feet than at the hips.
    """
    per = count // 2
    legs = []
    for row, sy, tag in ((0, -1.0, "F"), (1, 1.0, "A")):
        for k in range(per):
            x = 0.0 if per == 1 else (k / (per - 1.0) - 0.5) * 2.0 * LEG_SPAN_X
            base = -90.0 if sy < 0 else 90.0
            yaw = base + (x * FAN_PER_M if sy < 0 else -x * FAN_PER_M)
            legs.append(("%s%d" % (tag, k + 1),
                         Vector((x, sy * ROW_Y, HIP_Z)), yaw))
    return legs


class LegPose:
    """One leg's rest pose, solved so the foot lands where the gait needs it.

    The component's leg is modelled standing straight, foot under hip. Left like
    that `RestFootRadius` is near zero and so is the stride; this bends the
    linkage until the sole is `FOOT_REACH` outboard of the coxa axis and flat on
    the ground.
    """

    def __init__(self, leg_id, hip_world, yaw_deg, hip, knee, ankle, sole_z=0.0):
        self.id = leg_id
        self.yaw = math.radians(yaw_deg)

        a = (knee - hip).length
        b = (ankle - knee).length
        c = ankle.z - sole_z

        target = Vector((FOOT_REACH, -(hip_world.z - sole_z) + c))
        d = target.length
        if d > (a + b) * 0.995:
            raise SystemExit(
                "Leg %s cannot reach: needs %.2f m of a %.2f m linkage. Lower "
                "HIP_Z or shorten FOOT_REACH." % (leg_id, d, a + b))

        interior = math.acos(max(-1.0, min(1.0, (a * a + d * d - b * b) / (2 * a * d))))
        to_ankle = math.atan2(target.y, target.x)
        old_upper = math.atan2(knee.z - hip.z, knee.x - hip.x)
        old_lower = math.atan2(ankle.z - knee.z, ankle.x - knee.x)
        old_line = math.atan2(ankle.z - hip.z, ankle.x - hip.x)
        side = 1.0 if math.sin(old_upper - old_line) > 0 else -1.0

        new_upper = to_ankle + side * interior
        knee_new = Vector((a * math.cos(new_upper), a * math.sin(new_upper)))
        new_lower = math.atan2(target.y - knee_new.y, target.x - knee_new.x)

        self.d_upper = new_upper - old_upper
        self.d_lower = new_lower - old_lower - self.d_upper
        self.d_foot = -(new_lower - old_lower)

        base = Matrix.Translation(hip_world) @ Matrix.Rotation(self.yaw, 4, 'Z')
        r1 = Matrix.Rotation(-self.d_upper, 4, 'Y')
        r2 = r1 @ Matrix.Rotation(-self.d_lower, 4, 'Y')
        r3 = r2 @ Matrix.Rotation(-self.d_foot, 4, 'Y')

        self.m_upper = base @ r1
        self.m_lower = base @ Matrix.Translation(r1 @ (knee - hip)) @ r2
        self.m_foot = (base @ Matrix.Translation(r1 @ (knee - hip))
                       @ Matrix.Translation(r2 @ (ankle - knee)) @ r3)

        self.hip = hip_world
        self.knee = self.m_lower.translation
        self.ankle = self.m_foot.translation
        self.contact = self.m_foot @ Vector((0.0, 0.0, sole_z - ankle.z))
        self.coxa = hip_world + Vector((0, 0, COXA_RISE))

        # The axes the rig measures: the yaw axle is vertical, the pitch axles
        # are normal to the bend plane, and the sole rolls about the plane's own
        # forward direction.
        self.axle = Matrix.Rotation(self.yaw, 4, 'Z') @ Vector((0, 1, 0))
        self.forward = Matrix.Rotation(self.yaw, 4, 'Z') @ Vector((1, 0, 0))
        self.reach = a + b + c


class ArmPose:
    """One claw-arm's rest pose, in the same shape a LegPose comes out in.

    Built rather than solved: unlike a leg there is no ground to reach, so the
    joint positions are simply the authored bend plane rotated onto the arm's
    yaw. Three pitch joints, which is what leaves the IK two free links.
    """

    def __init__(self, arm_id, root_world, yaw_deg):
        self.id = arm_id
        self.yaw = math.radians(yaw_deg)
        rot = Matrix.Rotation(self.yaw, 4, 'Z')

        def at(p):
            return root_world + rot @ Vector((p.x, 0.0, p.y))

        self.root = root_world + Vector((0, 0, COXA_RISE))
        self.shoulder = at(ARM_SHOULDER)
        self.elbow = at(ARM_ELBOW)
        self.wrist = at(ARM_WRIST)
        self.tip = at(ARM_TIP)

        self.axle = rot @ Vector((0, 1, 0))
        self.forward = rot @ Vector((1, 0, 0))


# ---------------------------------------------------------------------------
# Unique geometry
# ---------------------------------------------------------------------------

def build_carapace(coll, legs):
    """The shell: a wide, shallow, chamfered dome with a coxa turret per leg.

    Lofted across X rather than along Y, which is what makes it read as a crab
    rather than as a short bus — the widest section is amidships and it tapers
    to both beams, so the silhouette from the front is the interesting one.
    """
    p = Part(PALETTE)
    hx, hy = SHELL["x"], SHELL["y"]
    z0, z1 = SHELL["z0"], SHELL["z1"]

    def ring(sy, sz):
        """Rounded rectangle in (y, z) at a station along X."""
        y, zl, zh = hy * sy, z0, z0 + (z1 - z0) * sz
        return [(-y * 0.82, zl + 0.10), (y * 0.82, zl + 0.10),
                (y, zl + 0.34), (y * 0.86, zh), (0.0, zh + 0.22 * sz),
                (-y * 0.86, zh), (-y, zl + 0.34)]

    p.loft([(-hx, ring(0.42, 0.30)),
            (-hx * 0.74, ring(0.80, 0.74)),
            (-hx * 0.30, ring(1.00, 1.00)),
            (hx * 0.30, ring(1.00, 1.00)),
            (hx * 0.74, ring(0.80, 0.74)),
            (hx, ring(0.42, 0.30))], 'X', HULL)

    # Plating: bands across the shell, which is the cheapest read of armour and
    # also what breaks up a lofted surface that is otherwise one smooth sweep.
    for i in range(5):
        xx = -hx * 0.66 + i * (hx * 1.32 / 4.0)
        p.box((xx, 0.0, z1 + 0.08), (0.16, hy * 1.55, 0.10), OLIVE)
    p.box((0.0, 0.0, z1 + 0.16), (hx * 1.10, 0.52, 0.14), GREEN)
    p.greeble((-hx * 0.55, -hy * 0.7, z1 - 0.06), (hx * 0.55, hy * 0.7, z1 + 0.06),
              22, seed=5, scale=(0.07, 0.24), mat=DARK, flatten='Z')

    # Coxa turrets: the leg sockets, standing proud of the shell so the yaw
    # joint has somewhere to be that is not inside the hull.
    for pose in legs:
        n = Matrix.Rotation(pose.yaw, 4, 'Z') @ Vector((1, 0, 0))
        c = pose.hip + n * 0.16 + Vector((0, 0, COXA_RISE * 0.5))
        p.cyl((c.x, c.y, c.z), 0.42, 0.86, 'Z', 14, HULL)
        p.cyl((c.x, c.y, c.z + 0.42), 0.48, 0.12, 'Z', 14, DARK)
        p.torus((c.x, c.y, c.z - 0.20), 0.41, 0.055, 'Z', 14, 6, DARK)
        for k in range(5):
            ang = 2 * math.pi * k / 5
            p.cyl((c.x + math.cos(ang) * 0.29, c.y + math.sin(ang) * 0.29,
                   c.z + 0.48), 0.037, 0.10, 'Z', 6, RUST)

    p.bevel(width=0.02, segments=2)
    p.finish("Mesh_Crab_Carapace", coll)


def build_underbelly(coll):
    """What a player standing beside a low machine actually looks at: the ribbed
    underside, the sump, and the skirt that hides where the legs meet the hull."""
    p = Part(PALETTE)
    hx, hy, z0 = SHELL["x"], SHELL["y"], SHELL["z0"]

    p.box((0.0, 0.0, z0 - 0.06), (hx * 1.72, hy * 1.52, 0.16), STEEL)
    for i in range(9):
        xx = -hx * 0.80 + i * (hx * 1.60 / 8.0)
        p.box((xx, 0.0, z0 - 0.16), (0.13, hy * 1.42, 0.14), DARK)
    p.box((0.0, 0.30, z0 - 0.26), (1.35, 1.05, 0.26), OLIVE)
    p.box((0.0, 0.30, z0 - 0.40), (0.95, 0.75, 0.14), DARK)
    p.greeble((-hx * 0.7, -hy * 0.6, z0 - 0.20), (hx * 0.7, hy * 0.6, z0 - 0.06),
              18, seed=13, scale=(0.06, 0.20), mat=RUBBER, flatten='Z')

    # Skirt round the beam ends, where the shell would otherwise stop dead.
    for sx in (-1, 1):
        p.box((sx * (hx + 0.05), 0.0, z0 + 0.30), (0.16, hy * 1.10, 0.62), OLIVE)
        p.cyl((sx * (hx + 0.10), 0.0, z0 + 0.30), 0.11, hy * 1.05, 'Y', 10, STEEL)

    p.bevel(width=0.018, segments=2)
    p.finish("Mesh_Crab_Underbelly", coll)


def build_prow(coll):
    """The face. Eye stalks, a sensor bar and the bumper the claws fold over.

    This is the one part of a machine that reads at any distance, and on a crab
    it is also the part that is NOT walking — the nose points across the
    direction of travel, so it is free to be the thing that looks at you.
    """
    p = Part(PALETTE)
    hx, hy, z1 = SHELL["x"], SHELL["y"], SHELL["z1"]
    y = -hy

    p.box((0.0, y - 0.22, z1 - 0.34), (hx * 1.15, 0.44, 0.62), HULL)
    p.box((0.0, y - 0.40, z1 - 0.10), (hx * 1.02, 0.24, 0.26), OLIVE)
    p.box((0.0, y - 0.44, z1 - 0.40), (hx * 0.70, 0.18, 0.24), GLASS)

    # Eye stalks — the crab read, and the only tall thing on the machine.
    for sx in (-1, 1):
        sx_x = sx * 0.62
        p.cyl((sx_x, y - 0.10, z1 + 0.10), 0.075, 0.46, 'Z', 10, STEEL)
        p.cyl((sx_x, y - 0.10, z1 + 0.34), 0.135, 0.20, 'Z', 12, DARK)
        p.cyl((sx_x, y - 0.20, z1 + 0.36), 0.095, 0.10, 'Y', 12, AMBER)
        p.torus((sx_x, y - 0.10, z1 + 0.24), 0.12, 0.028, 'Z', 12, 6, CHROME)

    # Bumper, hung off the chin on two struts.
    p.cyl((0.0, y - 0.62, SHELL["z0"] + 0.22), 0.085, hx * 1.30, 'X', 10, STEEL)
    for sx in (-1, 1):
        p.cyl((sx * hx * 0.52, y - 0.40, SHELL["z0"] + 0.22), 0.07, 0.46, 'Y', 8, STEEL)
    p.box((0.0, y - 0.66, SHELL["z0"] + 0.22), (0.70, 0.13, 0.20), RED)
    p.rivets((-hx * 0.62, y - 0.40, z1 - 0.10), (hx * 0.62, y - 0.40, z1 - 0.10),
             12, 0.026, 0.022, 'Y', RUST)

    p.bevel(width=0.016, segments=2)
    p.finish("Mesh_Crab_Prow", coll)


def build_stern(coll):
    """Vents, tanks and a tow eye on the aft face. Small, but a machine whose
    back is a blank wall reads as half-modelled from every angle behind it."""
    p = Part(PALETTE)
    hx, hy, z0, z1 = SHELL["x"], SHELL["y"], SHELL["z0"], SHELL["z1"]
    y = hy

    p.box((0.0, y + 0.16, z1 - 0.42), (hx * 1.05, 0.32, 0.56), HULL)
    for sx in (-1, 1):
        p.cyl((sx * 1.35, y + 0.24, z1 - 0.42), 0.24, 0.36, 'Y', 12, OLIVE)
        p.cyl((sx * 1.35, y + 0.42, z1 - 0.42), 0.26, 0.06, 'Y', 12, DARK)
        p.cyl((sx * 2.25, y + 0.10, z0 + 0.55), 0.20, 0.72, 'Y', 12, COPPER)
    p.box((0.0, y + 0.30, z0 + 0.40), (0.42, 0.20, 0.30), STEEL)
    p.torus((0.0, y + 0.40, z0 + 0.40), 0.16, 0.045, 'Y', 14, 6, CHROME)
    p.greeble((-hx * 0.5, y + 0.06, z0 + 0.70), (hx * 0.5, y + 0.20, z1 - 0.60),
              12, seed=21, scale=(0.06, 0.18), mat=DARK, flatten='Y')

    p.bevel(width=0.016, segments=2)
    p.finish("Mesh_Crab_Stern", coll)


# ---------------------------------------------------------------------------
# Rig
# ---------------------------------------------------------------------------

def build_armature(legs, arms, coll):
    """`CRAB_Rig`: a root, one Coxa/Hip/Knee/Ankle/Foot chain per leg, and one
    Arm/Shoulder/Elbow/Wrist chain per claw.

    The two naming schemes are deliberate and they are not interchangeable.
    `WalkerRig` assembles a LEG by name across the whole armature, so a leg has
    to use the classic vocabulary. An ARM is found by WALKING the hierarchy from
    its `Arm_` root — and its joints must NOT be called `Arm_*`, because `Arm_`
    is a root prefix and every joint carrying it would be claimed as an arm of
    its own. A shoulder is not a coxa.
    """
    data = bpy.data.armatures.new("CRAB_RigData")
    arm = bpy.data.objects.new("CRAB_Rig", data)
    coll.objects.link(arm)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    edit = data.edit_bones

    root = edit.new("Root")
    root.head = Vector((0, 0, SHELL["z0"]))
    root.tail = Vector((0, 0, SHELL["z1"]))

    for pose in legs:
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
        foot.tail = pose.contact + pose.forward * 0.40
        foot.parent, foot.use_connect = ankle, True

    for pose in arms:
        base = edit.new("Arm_%s" % pose.id)
        base.head, base.tail, base.parent = pose.root, pose.shoulder, root

        shoulder = edit.new("Shoulder_%s" % pose.id)
        shoulder.head, shoulder.tail, shoulder.parent = pose.shoulder, pose.elbow, base
        shoulder.use_connect = True

        elbow = edit.new("Elbow_%s" % pose.id)
        elbow.head, elbow.tail, elbow.parent = pose.elbow, pose.wrist, shoulder
        elbow.use_connect = True

        wrist = edit.new("Wrist_%s" % pose.id)
        wrist.head, wrist.tail, wrist.parent = pose.wrist, pose.tip, elbow
        wrist.use_connect = True

    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def bone_matrix(arm, bone_name):
    """The transform Blender applies to a BONE-parented child. Bone parenting
    hangs the child off the bone's *tail*, with local Y running head to tail —
    not off its head, which is the trap."""
    bone = arm.data.bones[bone_name]
    return (arm.matrix_world @ bone.matrix_local
            @ Matrix.Translation(Vector((0, bone.length, 0))))


def attach(obj, arm, bone_name, world):
    """Bone-parent `obj`, keeping it at `world`.

    `matrix_parent_inverse` is left at identity on purpose: FBX has no such
    concept and silently drops whatever is parked there, which puts every part
    somewhere else the moment it reaches Unity. Everything goes in
    `matrix_basis` instead, which survives.
    """
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = bone_matrix(arm, bone_name).inverted() @ world


def pin_mesh():
    """One shared mesh for every axle pin: a stubby bar, long on +Z.

    `WalkerRig.MeasureAxle` takes a joint's hinge axis from the longest extent
    of the first child whose name contains "Pin", so the pin's PROPORTIONS are
    the data — it has to be unambiguously longer on one axis than the others.
    """
    p = Part(PALETTE)
    p.cyl((0, 0, 0), 0.055, 0.44, 'Z', 10, DARK)
    p.cyl((0, 0, 0.20), 0.076, 0.055, 'Z', 10, CHROME)
    p.cyl((0, 0, -0.20), 0.076, 0.055, 'Z', 10, CHROME)
    obj = p.finish("_PinProto", bpy.context.scene.collection)
    data = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    return data


def axis_matrix(origin, axis):
    """Place a pin so its long (+Z) side lies along `axis`."""
    return (Matrix.Translation(origin)
            @ Vector(axis).normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4())


def frame_matrix(origin, forward, up, scale=1.0):
    """A transform putting a component's +Y on `forward` and +Z on `up`."""
    fwd = Vector(forward).normalized()
    upv = (Vector(up) - fwd * Vector(up).dot(fwd)).normalized()
    side = fwd.cross(upv)
    basis = Matrix((side, fwd, upv)).transposed().to_4x4()
    return (Matrix.Translation(origin) @ basis
            @ Matrix.Diagonal(Vector((scale, scale, scale, 1.0))))


def limb_axis(a, b):
    return (b - a).normalized()


def limb_out(axis):
    """The in-plane perpendicular to `axis`, pointing to the knee (+X) side."""
    p = Vector((axis.z, 0.0, -axis.x))
    return p if p.x >= 0 else -p


def limb_tilt(axis):
    """Rotation about Y taking a part's own −Z onto `axis`."""
    return Matrix.Rotation(-math.atan2(axis.x, -axis.z), 4, 'Y')


# ---------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    leg_count = parse_legs()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    load_component("mechanical/walker_leg.blend", ["Coll_WalkerLeg_Heavy"])
    load_component("mechanical/leg_shroud.blend",
                   ["Coll_LegShroud_Plate", "Coll_LegShroud_Ribbed",
                    "Coll_LegShroud_Patched", "Coll_LegShroud_Vented"])
    load_component("mechanical/claw_chela.blend",
                   ["Coll_Chela_Heavy", "Coll_Chela_Cutter"])
    load_component("mechanical/vent_grille.blend",
                   ["Coll_Vent_Louvre", "Coll_Vent_Scoop"])

    body = collection("Crab_Body")
    legs_coll = collection("Crab_Legs")
    claws_coll = collection("Crab_Claws")
    fit = collection("Crab_Fittings")
    rig_coll = collection("Crab_Rig")

    poses = [LegPose(i, hp, yaw, LEG_HIP, LEG_KNEE, LEG_ANKLE)
             for i, hp, yaw in leg_layout(leg_count)]
    arms = [ArmPose("P", Vector((ARM_ROOT["x"], ARM_ROOT["y"], ARM_ROOT["z"])),
                    -ARM_YAW),
            ArmPose("N", Vector((-ARM_ROOT["x"], ARM_ROOT["y"], ARM_ROOT["z"])),
                    -180.0 + ARM_YAW)]

    build_carapace(body, poses)
    build_underbelly(body)
    build_prow(body)
    build_stern(body)

    arm = build_armature(poses, arms, rig_coll)
    pins = pin_mesh()

    scale_m = Matrix.Diagonal(Vector((LEG_SCALE, LEG_SCALE, LEG_SCALE, 1.0)))
    upper = mesh_of("Coll_WalkerLeg_Heavy", "Upper")
    lower = mesh_of("Coll_WalkerLeg_Heavy", "Lower")
    foot = mesh_of("Coll_WalkerLeg_Heavy", "Foot")

    thigh = limb_axis(LEG_HIP, LEG_KNEE)
    thigh_out = limb_out(thigh)

    # ---- legs -----------------------------------------------------------
    for i, pose in enumerate(poses):
        m_u = pose.m_upper @ scale_m
        m_l = pose.m_lower @ scale_m
        m_f = pose.m_foot @ scale_m

        u = place("Mesh_Leg_%s_Upper" % pose.id, upper, legs_coll, m_u)
        low = place("Mesh_Leg_%s_Lower" % pose.id, lower, legs_coll, m_l)
        ft = place("Mesh_Leg_%s_Foot" % pose.id, foot, legs_coll, m_f)
        attach(u, arm, "Hip_%s" % pose.id, m_u)
        attach(low, arm, "Knee_%s" % pose.id, m_l)
        attach(ft, arm, "Foot_%s" % pose.id, m_f)

        shroud = mesh_of("Coll_LegShroud_%s" % SHROUDS[i % len(SHROUDS)])
        m = (pose.m_upper
             @ Matrix.Translation(thigh * 0.28 + thigh_out * 0.36)
             @ limb_tilt(thigh) @ scale_m)
        s = place("Mesh_Leg_%s_Shroud" % pose.id, shroud, legs_coll, m)
        attach(s, arm, "Hip_%s" % pose.id, m)

        # Axle pins. These are geometry with a job, not decoration: they are
        # what the rig measures its hinge axes from. The foot's pin is lifted
        # clear of the sole on purpose — only its DIRECTION is ever read, but
        # `LowestRendererPoint` takes the foot's length from the lowest renderer
        # under the ankle and skips nothing but `COL_`, so a pin bar centred on
        # the contact point hangs through the ground and stands the machine up
        # by its own radius.
        for bone, origin, axis in (
                ("Coxa_%s" % pose.id, pose.coxa, Vector((0, 0, 1))),
                ("Hip_%s" % pose.id, pose.hip, pose.axle),
                ("Knee_%s" % pose.id, pose.knee, pose.axle),
                ("Ankle_%s" % pose.id, pose.ankle, pose.axle),
                ("Foot_%s" % pose.id, pose.contact + Vector((0, 0, 0.16)),
                 pose.forward)):
            m = axis_matrix(origin, axis)
            pin = place("%sPin_%s" % (bone.split("_")[0], pose.id), pins,
                        legs_coll, m)
            attach(pin, arm, bone, m)

    # ---- claws ----------------------------------------------------------
    for i, pose in enumerate(arms):
        kind = CHELAE[i % len(CHELAE)]
        fwd = (pose.tip - pose.wrist).normalized()
        up = pose.axle.cross(fwd).normalized()
        m = frame_matrix(pose.wrist, fwd, up, CLAW_SCALE)

        palm = place("Mesh_Claw_%s_Palm" % pose.id,
                     mesh_of("Coll_Chela_%s" % kind, "Palm"), claws_coll, m)
        attach(palm, arm, "Wrist_%s" % pose.id, m)

        for part in (("JawUpper", "JawLower") if kind == "Heavy" else ("Blade",)):
            jaw = place("Mesh_Claw_%s_%s" % (pose.id, part),
                        mesh_of("Coll_Chela_%s" % kind, part), claws_coll, m)
            attach(jaw, arm, "Wrist_%s" % pose.id, m)

        # The arm's own linkage: a shoulder housing and two tapered segments,
        # modelled here because their proportions are this machine's and a
        # component that only ever fits one model is not a component.
        seg = Part(PALETTE)
        for a, b, r0, r1, mat in ((pose.shoulder, pose.elbow, 0.20, 0.15, HULL),
                                  (pose.elbow, pose.wrist, 0.16, 0.12, OLIVE)):
            d = b - a
            seg.cyl(tuple((a + b) / 2.0), r0, d.length, 'Z', 12, mat,
                    radius_top=r1,
                    rot=d.normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4())
        seg.cyl(tuple(pose.shoulder), 0.26, 0.34, 'Z', 12, DARK,
                rot=pose.axle.to_track_quat('Z', 'Y').to_matrix().to_4x4())
        seg.bevel(width=0.014, segments=2)
        limb = seg.finish("Mesh_Claw_%s_Limb" % pose.id, claws_coll,
                          origin=tuple(pose.shoulder))
        limb_m = Matrix.Translation(pose.shoulder)
        limb.matrix_world = limb_m
        PLACED[limb.name] = limb_m
        attach(limb, arm, "Shoulder_%s" % pose.id, limb_m)

        for bone, origin, axis in (
                ("Arm_%s" % pose.id, pose.shoulder, Vector((0, 0, 1))),
                ("Shoulder_%s" % pose.id, pose.shoulder, pose.axle),
                ("Elbow_%s" % pose.id, pose.elbow, pose.axle),
                ("Wrist_%s" % pose.id, pose.wrist, pose.axle)):
            m = axis_matrix(origin, axis)
            pin = place("%sPin_%s" % (bone.split("_")[0], pose.id), pins,
                        claws_coll, m)
            attach(pin, arm, bone, m)

    # ---- fittings -------------------------------------------------------
    static = []
    for sx in (-1, 1):
        static.append(place("Mesh_Vent_%s" % ("P" if sx > 0 else "N"),
                            mesh_of("Coll_Vent_Louvre"), fit,
                            loc=(sx * SHELL["x"] * 0.55, SHELL["y"] * 0.72,
                                 SHELL["z1"] - 0.30),
                            rot=(0, 0, 0)))
        static.append(place("Mesh_Scoop_%s" % ("P" if sx > 0 else "N"),
                            mesh_of("Coll_Vent_Scoop"), fit,
                            loc=(sx * SHELL["x"] * 0.82, -SHELL["y"] * 0.30,
                                 SHELL["z1"] - 0.10),
                            rot=(0, 0, sx * 90)))
    for obj in static:
        attach(obj, arm, "Root", PLACED[obj.name])

    dedupe_materials()

    # ---- report ---------------------------------------------------------
    print("\nRig report — what CrabLocomotion will measure (%d legs):" % leg_count)
    for pose in poses:
        radius = math.hypot(pose.contact.x - pose.coxa.x, pose.contact.y - pose.coxa.y)
        extension = (pose.contact - pose.hip).length / pose.reach
        stride = 2 * radius * math.sin(math.radians(40 * 0.85))
        print("  %-3s foot=(%6.2f,%6.2f,%5.2f) radius=%.2f extension=%3.0f%% stride=%.2f m"
              % (pose.id, pose.contact.x, pose.contact.y, pose.contact.z,
                 radius, extension * 100, stride))
    print("  worst sole-to-ground error: %.4f m"
          % max(abs(p.contact.z) for p in poses))
    print("  foot span  X %.2f m (travel axis)   Y %.2f m"
          % (max(p.contact.x for p in poses) - min(p.contact.x for p in poses),
             max(p.contact.y for p in poses) - min(p.contact.y for p in poses)))
    print("  hip plane %.2f m, shell %.2f x %.2f m"
          % (HIP_Z, SHELL["x"] * 2, SHELL["y"] * 2))

    print("\nTriangle cost by shared mesh:")
    by_mesh = {}
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        tris = sum(len(pg.vertices) - 2 for pg in obj.data.polygons)
        entry = by_mesh.setdefault(obj.data.name, [0, tris])
        entry[0] += 1
    total = 0
    for name, (uses, tris) in sorted(by_mesh.items(), key=lambda kv: -kv[1][0] * kv[1][1]):
        total += uses * tris
        print("  %-42s %2d x %6d = %7d" % (name, uses, tris, uses * tris))
    print("  %-42s %19d" % ("TOTAL", total))

    save(out)


build()
