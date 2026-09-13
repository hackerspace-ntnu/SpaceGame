"""components/mechanical/robot_hand — an articulated mechanical hand.

Built for the Lightning Conjurer, whose shipped hands were two salvaged rigs with
no thumb between them: `Armature` drove four fingers of metacarpal + 3 phalanges
on the right, and the left was thirteen loose meshes with no finger bones at all.
Neither could make the shape the creature's attack needs — a palm turned to the
sky with the digits closing into a cup around something.

Nothing in the library served. The golem has mitts (`Bone_Hand_L/R`, no digits)
and the only five-digit hand in the project is the astronaut's Mixamo rig, which
is a skinned human hand on a 1.8 m biped — useful as a proportion reference and
useless as geometry for a 9 m machine.

**Every phalanx is its own object with its origin on its own hinge pin.** That is
the whole trick and it is why this reads as a hand rather than a glove: rotating a
bone rotates one rigid segment about the joint it actually turns on, with no
skinning, no weight painting, and no chance of a knuckle smearing. The same reason
`claw_chela` splits its jaws off the palm.

Authoring frame, matching the rest of `components/mechanical`:

    origin      the WRIST pivot, where the hand mates onto a forearm
    +Y          out along the fingers
    +Z          dorsal (back of the hand); the palm faces -Z
    -X          the thumb side, which makes this a RIGHT hand
                (mirror across X for a left — `mirror_y` is the wrong axis here)

The variation tables below are written thumb-at-+X because that reads more
naturally, and `as_right_hand` mirrors them on the way in. See its docstring.

Metre scale, like everything else in the library: a 0.95 m hand, which is a human
hand scaled to a 9 m frame. Consumers scale it to their own units.

Rest pose is a flat open hand, fingers straight with a slight fan. Deliberately
NOT authored with a natural curl: the curl is what the animation is for, and a
rest pose that already carries some would add to every pose keyed on top of it.

Three variations:

    Five      thumb + 4 fingers, the anatomical hand. What the conjurer uses.
    Gripper   thumb + 2 heavy digits, short and thick — a machine that holds
              rather than gestures
    Splayed   5 long thin digits fanned wide, more spider than hand

    blender --background --python robot_hand.py -- --out robot_hand.blend
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import (Part, collection, link_materials, parse_out, report,  # noqa: E402
                       save, start)

MATS = [
    "Mat_Metal_Rust_Heavy",      # 0  plating
    "Mat_Metal_Steel_Dark",      # 1  joint housings, hinge barrels
    "Mat_Metal_Chrome_Scuffed",  # 2  pins, piston rods
    "Mat_Metal_Steel_Worn",      # 3  structural knuckle block
    "Mat_Neutral_Black_Matte",   # 4  seals and shadow gaps
    "Mat_Emissive_Portal_Blue",  # 5  palm emitter
]
RUST, DARK, CHROME, STEEL, BLACK, GLOW = range(6)


# ---------------------------------------------------------------------------
# Orientation helpers
# ---------------------------------------------------------------------------

def direction(fan_deg, tilt_deg):
    """A digit's pointing direction from two angles.

    `fan` swings it sideways in the plane of the palm (about Z, +ve toward the
    thumb); `tilt` lifts it dorsally or drops it palmward (about X, -ve toward
    the palm). Composed fan-after-tilt so `fan` always means the same thing
    regardless of how far the digit is dropped — which is what lets the thumb be
    written as "60 degrees round and 22 down" rather than as a quaternion.

    Note the negated fan: rotating +Y about +Z by a positive angle carries it
    toward -X, which is the far side from the thumb. Negating here is what makes
    the sign in the variation tables mean what it says, and its absence is what
    had every digit converging on the centreline instead of splaying.
    """
    return (Matrix.Rotation(math.radians(-fan_deg), 4, 'Z')
            @ Matrix.Rotation(math.radians(tilt_deg), 4, 'X')
            @ Vector((0.0, 1.0, 0.0))).normalized()


def align_y(d):
    """Rotation taking +Y onto `d`, for placing a cylinder along a digit."""
    return Vector((0.0, 1.0, 0.0)).rotation_difference(d).to_matrix().to_4x4()


def hinge_axis(d):
    """The axis a digit bends about: perpendicular to the digit and to the palm
    normal, so the segment folds toward the palm rather than twisting."""
    a = d.cross(Vector((0.0, 0.0, 1.0)))
    if a.length < 1e-6:                      # a digit pointing straight at +Z
        a = Vector((1.0, 0.0, 0.0))
    return a.normalized()


# ---------------------------------------------------------------------------
# One phalanx
# ---------------------------------------------------------------------------

def phalanx(coll, name, a, b, r_base, r_tip, plate=True):
    """One rigid segment, origin on its own hinge pin at `a`.

    The origin is the entire point: the object's pivot IS the joint, so a bone
    whose head sits at the same place rotates it correctly with an identity
    parent inverse and nothing slides.
    """
    p = Part(PALETTE)
    d = (b - a)
    length = d.length
    d = d.normalized()
    rot = align_y(d)
    hz = hinge_axis(d)
    rot_h = align_y(hz)
    mid = a + d * (length / 2.0)

    # Hinge barrel at the base, lying across the bend axis, with its pin.
    # Kept slightly narrower than the segment it drives: a barrel wider than the
    # digit reads as a spool threaded onto a rod rather than as a knuckle.
    p.cyl(tuple(a), r_base * 0.98, r_base * 1.8, 'Y', 10, DARK, rot=rot_h)
    p.cyl(tuple(a), r_base * 0.34, r_base * 2.2, 'Y', 8, CHROME, rot=rot_h)

    # The bone of the segment: a tapered shaft from joint to joint.
    p.cyl(tuple(mid), r_base * 0.60, length, 'Y', 12, STEEL,
          radius_top=r_tip * 0.60, rot=rot)

    if plate:
        # Dorsal armour riding on top of the shaft, and the plate is what the
        # digit actually reads as — the shaft is just what holds it up. Offset
        # along the palm normal rather than +Z so a dropped digit keeps its
        # plate on its own back.
        up = d.cross(hz).normalized()
        frame = Matrix((hz, d, up)).transposed().to_4x4()
        p.box(tuple(mid + up * r_base * 0.34),
              (r_base * 2.2, length * 0.90, r_base * 1.30), RUST, rot=frame)
        # Actuator rod running under the segment, on the flexor side.
        p.cyl(tuple(mid - up * r_base * 0.66), r_base * 0.20, length * 0.86,
              'Y', 6, CHROME, rot=rot)
        # Rounded cap on the far end, so a digit terminates in a fingertip
        # rather than in the flat cut end of a box.
        p.cyl(tuple(b), r_tip * 0.85, r_tip * 0.9, 'Y', 10, CHROME, rot=rot_h)

    p.bevel(width=0.004, segments=1)
    return p.finish(name, coll, origin=tuple(a))


def digit(coll, prefix, base, fan, tilt, lengths, r0, r1):
    """A chain of phalanges. Returns [(object, head, tail), ...] root first."""
    d = direction(fan, tilt)
    joints = [Vector(base)]
    for L in lengths:
        joints.append(joints[-1] + d * L)

    out = []
    n = len(lengths)
    for i in range(n):
        t0, t1 = i / n, (i + 1) / n
        obj = phalanx(coll, f"{prefix}{i + 1}", joints[i], joints[i + 1],
                      r0 + (r1 - r0) * t0, r0 + (r1 - r0) * t1)
        out.append((obj, joints[i], joints[i + 1]))
    return out


# ---------------------------------------------------------------------------
# The palm
# ---------------------------------------------------------------------------

def rounded(hw, hh, corner=0.05, n=3):
    c = min(corner, hw * 0.9, hh * 0.9)
    pts = []
    for cx, cz, a0 in ((hw - c, hh - c, 0.0), (-(hw - c), hh - c, 90.0),
                       (-(hw - c), -(hh - c), 180.0), (hw - c, -(hh - c), 270.0)):
        for i in range(n + 1):
            a = math.radians(a0 + 90.0 * i / n)
            pts.append((cx + c * math.cos(a), cz + c * math.sin(a)))
    return pts


def build_palm(coll, name, hw, plen, hh, knuckles, thumb_mount):
    """Wrist collar, palm chassis, knuckle bosses and the emitter.

    `knuckles` are the finger base points; each gets a boss so the joint reads as
    seated in the chassis rather than floating off the end of it.
    """
    p = Part(PALETTE)

    # Wrist: the mating collar a forearm plugs into.
    p.tube((0, 0.02, 0), hw * 0.44, 0.030, 0.08, 'Y', 18, DARK)
    p.cyl((0, 0.070, 0), hw * 0.37, 0.05, 'Y', 18, CHROME)

    # Chassis: widens from the wrist to the knuckle line, thinning as it goes.
    p.loft([(0.06, rounded(hw * 0.56, hh * 1.05, 0.04)),
            (plen * 0.34, rounded(hw * 0.92, hh, 0.05)),
            (plen * 0.74, rounded(hw, hh * 0.92, 0.05)),
            (plen, rounded(hw * 0.90, hh * 0.80, 0.04))], axis='Y', mat=RUST)

    # Knuckle bosses across the top of the chassis.
    for k in knuckles:
        p.cyl(tuple(k), hh * 0.62, hw * 0.30, 'X', 12, STEEL)
        p.cyl(tuple(k), hh * 0.26, hw * 0.36, 'X', 8, CHROME)

    # Thumb mount: a raised block on the +X flank carrying the first joint.
    p.box((thumb_mount.x * 0.82, thumb_mount.y, thumb_mount.z),
          (hw * 0.34, plen * 0.30, hh * 1.5), STEEL)

    # Palm emitter, recessed into the -Z face. This is what the charge sits in
    # front of, and the reason the palm has to face the sky in the Attack clip.
    p.cyl((0, plen * 0.52, -hh * 0.86), hw * 0.36, hh * 0.30, 'Z', 20, BLACK)
    p.cyl((0, plen * 0.52, -hh * 0.98), hw * 0.26, hh * 0.16, 'Z', 20, GLOW)

    # Tendon conduits running out of the wrist toward the knuckles.
    for sx in (-1, 1):
        p.cyl((sx * hw * 0.44, plen * 0.46, hh * 0.52), hw * 0.055, plen * 0.72,
              'Y', 8, CHROME)

    p.rivets((-hw * 0.62, plen * 0.20, hh * 0.78),
             (hw * 0.62, plen * 0.20, hh * 0.78), 5, 0.012, 0.008, 'Z', CHROME)

    p.bevel(width=0.006, segments=2)
    return p.finish(name, coll)


# ---------------------------------------------------------------------------
# Armature
# ---------------------------------------------------------------------------

def rigid_bone_parent(obj, arm, bone_name):
    """Bone-parent without moving the object.

    Blender anchors bone parenting at the bone TAIL, so the effective parent
    matrix carries a +Y translation of the bone's length. Cancelling it through
    `matrix_parent_inverse` collapses the chain to `matrix_basis`, which must
    therefore be set to the object's original world matrix. This is the same trap
    the conjurer's own rig.py documents, and it bites identically here.
    """
    world = obj.matrix_world.copy()
    bone = arm.data.bones[bone_name]
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    P = arm.matrix_world @ bone.matrix_local @ Matrix.Translation(
        (0.0, bone.length, 0.0))
    obj.matrix_parent_inverse = P.inverted()
    obj.matrix_basis = world


def build_armature(coll, name, palm_obj, chains, plen):
    """One armature per variation, bones following the segments exactly."""
    data = bpy.data.armatures.new(name)
    arm = bpy.data.objects.new(name, data)
    coll.objects.link(arm)
    arm.matrix_world = Matrix.Identity(4)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    ebs = data.edit_bones

    wrist = ebs.new("Bone_Wrist")
    wrist.head, wrist.tail, wrist.roll = Vector((0, 0, 0)), Vector((0, plen, 0)), 0.0

    for prefix, segs in chains:
        parent = wrist
        for i, (_obj, head, tail) in enumerate(segs):
            eb = ebs.new(f"Bone_{prefix}{i + 1}")
            eb.head, eb.tail, eb.roll = head, tail, 0.0
            eb.parent = parent
            # Connected only within a digit. The first phalanx starts away from
            # the wrist bone's tail, so connecting it would drag that tail onto
            # the knuckle and shorten the palm.
            eb.use_connect = i > 0
            parent = eb

    bpy.ops.object.mode_set(mode='OBJECT')

    rigid_bone_parent(palm_obj, arm, "Bone_Wrist")
    for prefix, segs in chains:
        for i, (obj, _h, _t) in enumerate(segs):
            rigid_bone_parent(obj, arm, f"Bone_{prefix}{i + 1}")
    return arm


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def as_right_hand(spec):
    """Mirror a variation table across the YZ plane.

    The tables are written thumb-at-+X because that is the easier thing to read,
    but a hand with fingers along +Y, dorsal +Z and thumb +X is a LEFT hand —
    lay your right hand palm-down with the fingers pointing away and the thumb
    falls on the -X side. Mirroring the table rather than negative-scaling the
    object is what keeps normals outward and the object scale at 1.0.
    """
    out = dict(spec)
    out["digits"] = [dict(d, base=(-d["base"][0], d["base"][1], d["base"][2]),
                          fan=-d["fan"])
                     for d in spec["digits"]]
    return out


def build_variation(spec):
    spec = as_right_hand(spec)
    coll = collection(f"Coll_RobotHand_{spec['name']}")
    pre = f"Mesh_Hand_{spec['name']}_"
    hw, plen, hh = spec["palm"]

    knuckles = [Vector(d["base"]) for d in spec["digits"] if d["name"] != "Thumb"]
    thumb = next(d for d in spec["digits"] if d["name"] == "Thumb")

    palm = build_palm(coll, pre + "Palm", hw, plen, hh, knuckles,
                      Vector(thumb["base"]))

    chains = []
    for d in spec["digits"]:
        segs = digit(coll, pre + d["name"], d["base"], d["fan"], d["tilt"],
                     d["lengths"], d["r0"], d["r1"])
        chains.append((d["name"], segs))

    build_armature(coll, f"Rig_Hand_{spec['name']}", palm, chains, plen)
    return coll


# Five — the anatomical hand, and the one the conjurer takes.
#
# Finger lengths run middle > index ~ ring > pinky, which is what reads as a hand
# at a glance; four equal digits read as a machine. The fan angles splay them
# very slightly so the silhouette is not a comb.
FIVE = {
    "name": "Five",
    "palm": (0.26, 0.44, 0.085),
    "digits": [
        # Set well forward of the wrist and swung further round than a human
        # thumb, because it has to reach across a palm this wide to make the cup
        # the Attack clip closes into.
        {"name": "Thumb",  "base": (0.26, 0.185, -0.03), "fan": 34, "tilt": -28,
         "lengths": (0.19, 0.15, 0.11), "r0": 0.058, "r1": 0.038},
        {"name": "Index",  "base": (0.17, 0.425, 0.01), "fan": 7, "tilt": 0,
         "lengths": (0.20, 0.15, 0.11), "r0": 0.055, "r1": 0.037},
        {"name": "Middle", "base": (0.06, 0.44, 0.01), "fan": 2, "tilt": 0,
         "lengths": (0.22, 0.17, 0.12), "r0": 0.057, "r1": 0.038},
        {"name": "Ring",   "base": (-0.06, 0.425, 0.01), "fan": -3, "tilt": 0,
         "lengths": (0.20, 0.15, 0.11), "r0": 0.054, "r1": 0.036},
        {"name": "Pinky",  "base": (-0.17, 0.395, 0.01), "fan": -9, "tilt": 0,
         "lengths": (0.16, 0.12, 0.09), "r0": 0.047, "r1": 0.032},
    ],
}

# Gripper — three heavy digits. Built ahead: nothing needs it yet, but a machine
# that picks up cargo wants a hand that closes hard rather than one that gestures.
GRIPPER = {
    "name": "Gripper",
    "palm": (0.26, 0.34, 0.11),
    "digits": [
        {"name": "Thumb", "base": (0.22, 0.10, -0.02), "fan": 44, "tilt": -18,
         "lengths": (0.16, 0.13), "r0": 0.080, "r1": 0.058},
        {"name": "Index", "base": (0.12, 0.34, 0.01), "fan": 6, "tilt": 0,
         "lengths": (0.19, 0.15), "r0": 0.078, "r1": 0.055},
        {"name": "Ring",  "base": (-0.12, 0.34, 0.01), "fan": -6, "tilt": 0,
         "lengths": (0.19, 0.15), "r0": 0.078, "r1": 0.055},
    ],
}

# Splayed — long thin digits fanned wide. Built ahead for anything that should
# read as a caster or a manipulator rather than a fist.
SPLAYED = {
    "name": "Splayed",
    "palm": (0.28, 0.36, 0.065),
    "digits": [
        {"name": "Thumb",  "base": (0.24, 0.11, -0.02), "fan": 46, "tilt": -26,
         "lengths": (0.19, 0.16, 0.13), "r0": 0.040, "r1": 0.026},
        {"name": "Index",  "base": (0.19, 0.36, 0.01), "fan": 20, "tilt": 0,
         "lengths": (0.24, 0.20, 0.15), "r0": 0.038, "r1": 0.024},
        {"name": "Middle", "base": (0.07, 0.37, 0.01), "fan": 7, "tilt": 0,
         "lengths": (0.26, 0.21, 0.16), "r0": 0.039, "r1": 0.025},
        {"name": "Ring",   "base": (-0.07, 0.36, 0.01), "fan": -7, "tilt": 0,
         "lengths": (0.24, 0.20, 0.15), "r0": 0.037, "r1": 0.024},
        {"name": "Pinky",  "base": (-0.18, 0.34, 0.01), "fan": -21, "tilt": 0,
         "lengths": (0.21, 0.17, 0.13), "r0": 0.034, "r1": 0.022},
    ],
}


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    for spec in (FIVE, GRIPPER, SPLAYED):
        build_variation(spec)

    print("\nRobot hand variations:")
    report()
    save(out)


build()
