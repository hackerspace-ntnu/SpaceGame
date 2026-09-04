# The staff: a rusted shaft with a vertical-axis wind turbine at its tip.
#
# STEP 4b, after hands_rebuild.py and before rustify.py:
#
#     restore_parts -> rig -> walkerize -> hands_rebuild -> STAFF -> rustify
#                                                                 -> anim -> export
#
# SUPERSEDES charger.py, which built the ring in the chest the old attack drew its
# bolt out of. That ring is gone -- this script removes its four meshes and two
# bones if it finds them, so running the pipeline over an already-chargered file
# converts it rather than leaving both. charger.py is kept beside this one as a
# record, the way hands.py and style.py are, and is no longer in the sequence.
#
# Additive apart from that removal, and re-runnable: it deletes its OWN meshes and
# bones by name and rebuilds them, which is safe only because nothing else in the
# pipeline creates or edits them.
#
# ---- the shaft is generated, and reusing the model's own was tried first -------
#
# The .blend has always carried a staff. `Weapon` -- 7250 verts, 23 units tall --
# sits in WIP_Spares at y = -44, parked there with the rest of the
# work-in-progress, and it is the only thing in this file that was drawn for this
# creature and never used. Lifting its shaft, cutting the blade head off and
# stretching it to length is the obvious move, and it does not work.
#
# Its shaft is not a tube. Cut below the head it comes to 120 verts in about
# sixty faces: a 32-vert ferrule disc at the bottom, a handful of 4- and 8-vert
# collar rings up near the top, and NOTHING BETWEEN THEM -- no faces spanning the
# fifteen units in the middle. Whatever it was drawn as, it is not closed
# geometry, and the bounding box says nothing about that: every check here passed
# on the reused version, the placement assertions passed, and it rendered as a
# turbine floating over a stub with a tiny disc hovering near the floor.
#
# So the rod below is generated, from a profile of (height, radius) pairs. That
# also buys the thing the donor could not give: the collars land where they are
# wanted -- one inside the hand, one under the turbine -- instead of wherever a
# 4.2x vertical stretch happened to drag them.
#
# `Weapon` is left exactly where it is, untouched, along with everything else in
# WIP_Spares. components/props/walking_staff.blend was the other candidate and is
# not usable either: its four variants are 1.0-1.6 m wooden hiking canes wearing
# Mat_Wood_Ply_Worn, the wrong size by a factor of fifteen and the wrong material
# for a nine-metre rusted mech.
#
# ---- where it sits ------------------------------------------------------------
#
# Vertical, running THROUGH the closed right fist, turbine above the head, butt
# hanging to about knee height, carried forward and out on a bent arm.
#
#   The shaft goes through the BORE. The arm is posed into a CARRY at rest (see
#   pose_carry_arm): the elbow bends forward and the hand turns level, so the
#   fingers curl in a horizontal plane, the hole they make stands vertical, and
#   the shaft drops straight down it. Every earlier version put the bore right and
#   the ARM wrong -- the last one cranked the wrist 90 degrees sideways on a
#   straight arm, and the two before that stood the shaft beside a fist that could
#   not close on it at all.
#
#   The turbine cannot be beside the head. Measured off the meshes rather than
#   guessed -- the head reaches radius 4.67 about (0.33, -0.06, 32.4), and the body
#   stops at z 37.48. A 3.45-radius fan on this column cuts 1.84 into the skull at
#   hub z 34.4, 0.73 at z 36, and finally clears at 37. HUB_Z is 38.0.
#
# The second of those decides the LAYOUT. Shrinking the old staff about its grip --
# keeping the hand a third of the way up, as the full-size version had it -- lands
# the hub at exactly the 34.4 that fails. A shorter staff on a creature that has not
# shrunk has to be gripped further down, and the tail stops at the knee instead of
# near the floor. How far down is not authored: GRIP is MEASURED off the carried
# arm, so the collar lands wherever the fist is -- currently a little over a third
# of the way up, against the full-size staff's third.
#
# ---- three bones, and why it is not one ---------------------------------------
#
#   Staff       the shaft. Parented to Hand.R, so the staff goes wherever the arm
#               goes and anim.py never keys it directly -- raising the staff is
#               raising the ARM, which is what solve_armroot already does.
#   StaffRotor  the fan alone, so it can SPIN. Its axis is the shaft, so a
#               rotation about world Z at rest turns the blades in their own
#               plane. This is the wind-up's tell, and it is the same trick the
#               chest rotor used and the retired halo before that.
#   StaffTip    the emitter above the blades. Deliberately NOT under StaffRotor:
#               the charge effect and the bolt hang off this, and an emitter that
#               spun with the fan would drag the lightning round with it.
import bpy, math
from mathutils import Matrix, Vector

RIG = "ConjurerRig"
PARENT_BONE = "Hand.R"
SPARES = "WIP_Spares"
# Not used, only protected -- see the header and the assertion at the bottom.
SPARE_STAFF = "Weapon"
SPARE_STAFF_VERTS = 7250

SHAFT = "Staff_Shaft"
MOUNT = "Staff_Mount"
FAN = "Staff_Fan"
CORE = "Staff_Core"
PARTS = (SHAFT, MOUNT, FAN, CORE)

BONE = "Staff"
ROTOR_BONE = "StaffRotor"
TIP_BONE = "StaffTip"
BONES = (BONE, ROTOR_BONE, TIP_BONE)

# ---- the column the staff stands on -------------------------------------------
#
# THE FIST'S BORE -- the hole the shaft passes through, not a point beside it.
# MEASURED off the posed hand rather than typed, because the carry pose below
# moves it: an arm re-posed against a hard-coded column stands the shaft next to a
# fist that is no longer there, and nothing downstream would say so.
#
# A closed finger's four joints lie on a CIRCLE, which is the useful fact: the
# bore is that circle's centre rather than a guess between the palm and the
# fingertips. measure_bore() fits one, least squares, through the index finger's
# three joints and its tip in the plane the finger curls in. On the hand as the
# old wrist roll left it, that lands within 0.02 of the figures the first version
# of this file arrived at by hand -- centre (0.500, -8.810), radius 0.760.
#
# The RADIUS is what the curl controls; the centre barely moves. A full curl looks
# like the better match against a 0.60 shaft and is not: those figures are the
# JOINTS, and the finger mesh is thick, so at 1.0 its inner face drives through
# the shaft and a fingertip pokes out the far side -- visible in a top-down
# render. 0.85 keeps the phalanges against it and the intersection buried. The
# fingers wrap about 270 degrees from there and the palm closes the last quadrant.
GRIP = None                     # both set by measure_bore(), below
BORE_R = None

# What the fingers do in every clip, copied from anim.py's GRIP_CURL. Used to pose
# the hand for the bore fit and for the grip check at the bottom -- nothing here
# animates.
#
# It is duplicated rather than shared because these are two separate Blender runs
# and importing anim.py would execute the whole of it. If the two ever disagree the
# fit simply measures a hand the creature never makes, so keep them in step.
GRIP_CURL = {"Thumb": (30.0, 34.0, 30.0), "Index": (32.0, 48.0, 44.0),
             "Middle": (34.0, 50.0, 46.0), "Ring": (32.0, 48.0, 44.0),
             "Pinky": (30.0, 46.0, 42.0)}
GRIP_CURL_AMOUNT = 0.85

# ---- size, and where the length goes -------------------------------------------
# Every dimension below is authored at the staff's original size and multiplied by
# SIZE.
SIZE = 0.75
LENGTH = 39.30 * SIZE          # butt to emitter cap
HUB_TO_TOP = 5.30 * SIZE       # the fan's centre up to the cap

# The TURBINE's height is pinned and the butt falls where it falls, which is the
# opposite of how the full-size staff was laid out and is forced by the shrink.
#
# The fan cannot be beside the head. Measured off the meshes: the head reaches
# radius 4.67 about (0.33, -0.06, 32.4) and the body stops at z 37.48, and a
# 3.45-radius fan on this column has to sit at hub z 37 before it stops cutting
# into the skull -- at 36 it is 0.73 deep and at the old proportions' 34.4 it is
# 1.84 deep. Hub 38.0 clears by 1.93 and that is where it goes.
#
# Scaling the old layout instead -- keeping the grip a third of the way up a
# shorter staff -- puts the hub at 34.4, which is exactly the case that fails. A
# shorter staff on a creature that has not shrunk simply has to be gripped lower
# down, and the butt ends around knee height rather than near the floor.
HUB_Z = 38.00
TOP_Z = HUB_Z + HUB_TO_TOP
BUTT_Z = TOP_Z - LENGTH

# Radius of the plain rod, which the collars ride proud of. At SIZE it comes to
# 1.2 across, against a bore of 1.7 -- the fist closes on it rather than around
# nothing. The first pass used 0.42 and rendered as a wire.
SHAFT_R = 0.80 * SIZE

# The rod's profile: (world z, radius as a multiple of SHAFT_R). Bridged ring to
# ring in order, so a pair of entries at the same radius is a straight section and
# a tight pair either side of a wider one is a collar.
#
# Given in ABSOLUTE z, derived from the things the collars are against, rather than
# as fractions of the length. The two placements that matter are the grip collar,
# which has to land in the fist so the fingers close on a swell instead of a bare
# pole, and the bearing band, which sits under the turbine mount where a real
# machine would carry the load. As fractions those were 0.32 and 0.72 for the
# full-size staff and would both be in the wrong place now: the shrink moved the
# grip down the shaft and the carry pose moved it back up again. The grip entries
# ride GRIP.z, so they follow the fist wherever the pose puts it.
#
# The rod also tapers slightly along its length -- an even cylinder reads as pipe
# rather than as a staff.
SHAFT_SEGS = 12


def shaft_profile():
    mount_bottom = HUB_Z - HUB_H / 2.0 - MOUNT_H
    return (
        (BUTT_Z + 0.00, 0.55),          # chamfer into the ferrule
        (BUTT_Z + 0.35, 1.10),
        (BUTT_Z + 1.40, 1.10),          # the ferrule at the butt
        (BUTT_Z + 1.75, 0.98),
        (GRIP.z - 1.30, 1.00),
        (GRIP.z - 0.65, 1.32),          # the grip collar -- inside the fist
        (GRIP.z + 0.45, 1.32),
        (GRIP.z + 0.80, 0.97),
        (mount_bottom - 1.30, 0.93),
        (mount_bottom - 0.70, 1.24),    # the bearing band under the turbine
        (mount_bottom - 0.05, 1.24),
        (mount_bottom + 0.30, 0.90),
        (TOP_Z - 0.95, 0.86),
        (TOP_Z + 0.00, 0.62),           # tapers into the emitter cap
    )

# ---- the fan ------------------------------------------------------------------
# Three blades, swept. Reference: Cochrane, figure 6(g) -- curved blades splaying
# off one hub, all sweeping the same way round.
#
# Three rather than four or six for the same reason the chest rotor had six teeth
# and not twelve: this is read as a SILHOUETTE at 25 m, where a dense fan closes
# up into a disc and stops reading as blades at all. Three stays open, and open is
# what lets the emitter glow through it.
#
# Sized against the HEAD, which is the only thing on this creature at the same
# height and therefore the only thing it will be compared with: the head sphere
# is about 12 units across and the fan sweeps 9.2, so the turbine reads as
# substantial without becoming the creature's new silhouette.
BLADES = 3
FAN_R0, FAN_R1 = 1.00 * SIZE, 4.60 * SIZE    # blade root and tip radius
FAN_RISE = 4.80 * SIZE         # how far the tip climbs above the root
FAN_SWEEP = 62.0               # degrees the blade wraps around the axis, root to tip
FAN_CHORD0, FAN_CHORD1 = 2.60 * SIZE, 1.05 * SIZE   # chord at root and tip
FAN_LEAN = 52.0                # degrees the chord leans from axial toward radial
FAN_THICK = 0.24 * SIZE
FAN_SEGS = 12                  # cross-sections along each blade

# The three angles are NOT scaled. Sweep, lean and the blade count are what make
# the fan read as figure 6(g) rather than as a cage, and they are shape rather
# than size -- scaling them would change the turbine's design as a side effect of
# resizing it.
HUB_R, HUB_H = 1.05 * SIZE, 1.80 * SIZE
MOUNT_R, MOUNT_H = 1.35 * SIZE, 1.00 * SIZE  # the stationary collar the hub turns on
CORE_R, CORE_H = 0.72 * SIZE, 1.10 * SIZE

arm = bpy.data.objects[RIG]


def log(m):
    print(f"[staff] {m}")


# ---------------------------------------------------------------- rest pose
# Everything below is world-space arithmetic against the creature standing at
# rest: GRIP is a rest-pose column, and the clearance checks at the bottom
# measure the body's real meshes to decide whether the staff fits.
#
# But a .blend that has been through anim.py carries an ACTION, and pose bones
# keep whatever the last keyframe left behind -- the same trap export.py
# documents. Re-running this script over a finished file would then measure the
# arm wherever some frame of Attack had flung it, and the assertions would pass
# or fail on which frame that happened to be. It is why the hand-clearance
# figure moved from -0.02 to +0.48 between two runs that built identical
# geometry.
#
# Detaching the action is not enough on its own; the basis has to be zeroed too.
def rest_pose():
    """Detach any action and zero every bone. See above for why both are needed."""
    if arm.animation_data:
        arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


# ----------------------------------------------------- the arm CARRIES the staff
# The right arm's REST pose is a carry: the elbow bent forward, the hand turned to
# close round a vertical shaft. Both live in the rest pose and both take the whole
# subtree -- the fifteen finger bones, CastSocket.R and every mesh hanging off
# them -- with them.
#
# This is geometry, not taste, and the geometry is forced.
#
# A fist's BORE -- the hole a held pole passes through -- is perpendicular to the
# plane the fingers curl in, which means it is perpendicular to the FINGERS. A
# vertical bore therefore needs horizontal fingers, and horizontal fingers on a
# vertical forearm are a wrist snapped ninety degrees out to the side.
#
# That is exactly what the previous version did: it turned Hand.R 90 degrees about
# world X and left the arm hanging straight down. It got the bore vertical, and the
# hand jutted sideways off the wrist in every clip, Idle and Walk included, with
# the staff floating 2.4 units outboard of a limb it did not look attached to.
#
# BENDING THE ELBOW is what removes the break, because it is what a person does --
# nobody holds a staff with a straight arm. With the forearm carried forward the
# hand can be level and very nearly in line with it, which is a wrist a machine can
# hold all day. CARRY_ELBOW is 50 degrees off vertical against a level hand, so the
# rest wrist sits at 40 degrees of extension: the pose in the reference art, where
# the forearm comes forward and the fist cocks up onto the shaft.
#
# It lives in the REST pose rather than in a keyed rotation in every clip, and that
# matters. Put it in the clips and the bind pose keeps the authored orientation,
# which means the staff lies horizontal through the creature's own torso whenever
# no animation is playing -- in the prefab view, and in every scene the editor
# draws without pressing play.
CARRY_ELBOW = -50.0          # forearm's rest angle off vertical; negative = forward

# ...and the arm reaches OUTBOARD as well as forward, by turning the forearm about
# the vertical at the elbow. This is a SILHOUETTE fix and it costs nothing in the
# joint, which is why it is here rather than in the hand.
#
# A staff carried straight forward stands directly in front of the creature, and
# from the three-quarter angle a player actually fights it from, forward and to the
# right project onto the same place: the shaft came out drawn across the middle of
# the body, over the head, cancelling the separation the whole design depends on --
# this thing is read as an outline at the 25 m it casts from. Splaying it 20 degrees
# puts the column back outside the body's edge without moving it forward at all,
# because the forearm's length is what sets the reach and turning it about the
# vertical does not change that.
#
# The turn takes the HAND with it, so there is no deviation at the wrist -- the
# hand stays in line with the forearm and only the shoulder reads as opening out.
# Yawing the hand alone would have bought the same clearance and paid for it with
# exactly the sideways wrist break this whole change exists to remove.
CARRY_SPLAY = 20.0           # degrees the forearm turns outboard about the vertical

# The hand's rest frame, as world directions. anim.py holds the same three and
# derives its finger-curl axes from them; see the note there.
#
#   fingers  the Hand.R bone's own direction. Along the splay and LEVEL, because
#            the bore is perpendicular to it and the bore has to stand vertical.
#   palm     inboard, so the shaft the fingers close on is drawn toward the body
#            rather than pushed further out from it.
#   thumb    up -- and that is not a free choice. For a right hand
#            thumb = fingers x palm, so palm-inboard is what makes it a thumb-up
#            grip; palm-outboard puts the thumb underneath, which is the reversed
#            grip a torch is carried in, not the one a staff is held in.
SPLAY = Matrix.Rotation(math.radians(-CARRY_SPLAY), 3, 'Z')   # -Z turn is outboard
HAND_FINGERS = SPLAY @ Vector((1.0, 0.0, 0.0))
HAND_PALM = SPLAY @ Vector((0.0, 1.0, 0.0))
HAND_THUMB = HAND_FINGERS.cross(HAND_PALM)


def hand_frame(side="R"):
    """(fingers, palm, thumb) of one hand, as world unit vectors. MEASURED.

    Read off the rig rather than written down, so it keeps telling the truth after
    the rest pose has changed -- which is the point, since this script is the thing
    that changes it.

    `fingers` is the Hand bone's own direction. The THUMB axis is the fist's bore,
    and it comes from the KNUCKLE LINE -- the row the four fingers' base joints sit
    on, running away from the thumb -- with the finger component projected out. The
    fingers splay about five degrees, and five degrees of bore tilt is most of the
    slack a 0.60 shaft has in a 0.76 hole, so the raw line will not do.

    `palm` closes the frame. thumb = fingers x palm on a right hand and the other
    way round on a left, which is the only place the two hands differ here.
    """
    b = arm.data.bones
    f = (b[f"Hand.{side}"].tail_local - b[f"Hand.{side}"].head_local).normalized()
    k = b[f"Pinky1.{side}"].head_local - b[f"Index1.{side}"].head_local
    t = -(k - f * k.dot(f)).normalized()
    return f, t.cross(f), t


def turn_subtree(root, pivot, R3):
    """Turn `root` and every bone under it about `pivot` by `R3`, in the rest pose.

    CONNECTED bones have to be broken apart first. Moving an edit bone's tail drags
    the head of any child connected to it, so transforming a parent and then that
    child applies the rotation to the child TWICE -- which came out as phalanges
    four units long, fingers turned inside out, and a hand that looked shattered.
    Eleven of the twenty bones in this hand are connected.
    """
    names = [root]

    def descend(name):
        for b in arm.data.bones[name].children:
            names.append(b.name)
            descend(b.name)

    descend(root)
    M = (Matrix.Translation(pivot) @ R3.to_4x4()
         @ Matrix.Translation(-Vector(pivot)))

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    ebs = arm.data.edit_bones
    conn = {n: ebs[n].use_connect for n in names}
    for n in names:
        ebs[n].use_connect = False
    for n in names:
        ebs[n].transform(M)        # transform(), not head/tail: it carries roll
    for n in names:
        ebs[n].use_connect = conn[n]
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()
    return names


def forearm_rot(splay, pitch):
    """The rest orientation of a forearm splayed `splay` out and pitched `pitch`.

    Pitch first, then splay, both from the authored straight-down bone: pitch is a
    turn about world Y and swings the forearm forward, splay is a turn about world
    Z and carries that swing outboard. Composed in the other order the splay would
    spin a still-vertical bone about its own axis and do nothing at all.
    """
    return (Matrix.Rotation(math.radians(-splay), 3, 'Z')
            @ Matrix.Rotation(math.radians(pitch), 3, 'Y'))


def forearm_rest():
    """(splay, pitch) the right forearm currently sits at. The inverse of the above.

    Recovered from the bone's direction, so pose_carry_arm can work out its own
    delta and converge from any state this pipeline produces -- the authored arm,
    the old wrist-roll build, or its own output. It assumes the forearm carries no
    roll about its own axis, which holds because nothing in the pipeline has ever
    given it one; pose_carry_arm asserts the round trip rather than trusting that.
    """
    d = (arm.data.bones["Forearm.R"].tail_local
         - arm.data.bones["Forearm.R"].head_local).normalized()
    return (-math.degrees(math.atan2(d.y, d.x)),
            math.degrees(math.atan2(-math.hypot(d.x, d.y), -d.z)))


def pose_carry_arm():
    """Bend the right elbow and set the hand's rest frame. Idempotent.

    Both steps are applied as the DELTA from whatever the rig currently holds to
    the target, measured off the rig itself, so this converges on the same pose
    from the hand as authored, from the old 90-degree wrist roll, and from its own
    output. There is no flag and no state to get wrong: run it twice and the second
    pass moves nothing, because the delta comes out as the identity.

    Order matters. The elbow turns first and carries the hand with it; the wrist
    correction is then computed against where the hand actually ended up.
    """
    fb = arm.data.bones["Forearm.R"]
    turn_subtree("Forearm.R", fb.head_local.copy(),
                 forearm_rot(CARRY_SPLAY, CARRY_ELBOW)
                 @ forearm_rot(*forearm_rest()).inverted())

    f, p, t = hand_frame("R")
    cur = Matrix((f, p, t)).transposed()           # columns: the measured frame
    tgt = Matrix((HAND_FINGERS, HAND_PALM, HAND_THUMB)).transposed()
    turn_subtree("Hand.R", arm.data.bones["Hand.R"].head_local.copy(),
                 tgt @ cur.inverted())

    f, p, t = hand_frame("R")
    assert (f - HAND_FINGERS).length < 1e-3 and (t - HAND_THUMB).length < 1e-3, (
        f"the hand came out fingers {f}, thumb {t}, not {HAND_FINGERS} and "
        f"{HAND_THUMB}")

    # The wrist's BEND, measured off the posed rig rather than taken from the
    # arithmetic. It is the number this whole change is about, and the one anim.py
    # then has to hold on to through the cast.
    fd = (arm.data.bones["Forearm.R"].tail_local
          - arm.data.bones["Forearm.R"].head_local).normalized()
    bend = math.degrees(fd.angle(f))
    assert bend < 55.0, (
        f"the rest wrist is bent {bend:.0f} degrees, which is a break rather than "
        "a grip; CARRY_ELBOW is what sets it -- a forearm nearer the horizontal "
        "needs less of the hand")
    log(f"carry pose: elbow {-CARRY_ELBOW:.0f} deg forward, {CARRY_SPLAY:.0f} deg "
        f"outboard; wrist bent {bend:.0f} deg; bore vertical")


def curl_hand(side="R"):
    """Close a hand to GRIP_CURL, for the bore fit and the grip check.

    The curl axes come out of hand_frame() rather than being written down. A finger
    closes about the knuckle line -- the THUMB axis -- and the thumb closes about
    the FINGER axis, and both signs flip with the hand's chirality. Those are facts
    about a hand, so they survive the rest pose being changed; the world axis
    LETTERS they happen to equal do not, and the previous version wrote down the
    letters. Curling on a stale axis barely moves the fingers at all, which is a
    failure that reports nothing.
    """
    rest_pose()
    f, p, t = hand_frame(side)
    fingers, thumb = (t, -f) if side == "R" else (-t, f)
    for digit, degrees in GRIP_CURL.items():
        axis = thumb if digit == "Thumb" else fingers
        for i, deg in enumerate(degrees, start=1):
            pb = arm.pose.bones[f"{digit}{i}.{side}"]
            M = pb.bone.matrix_local.to_3x3()
            Rw = Matrix.Rotation(math.radians(deg * GRIP_CURL_AMOUNT), 3, axis)
            pb.rotation_quaternion = (M.inverted() @ Rw @ M).to_quaternion()
    bpy.context.view_layer.update()


def measure_bore():
    """Where the closed fist's hole is, and how wide. World space, rest pose.

    Fitted rather than eyeballed: a closed finger's joints lie on a circle about
    the bore, so the column is that circle's centre. Kasa least squares through the
    index finger's three joints and its tip, in the (fingers, palm) plane the
    finger sweeps, with the height taken as the four knuckles' mean along the bore
    -- the middle of the FIST rather than of one finger.
    """
    import numpy as np
    curl_hand("R")
    f, p, t = hand_frame("R")
    pb = arm.pose.bones
    pts = [pb[f"Index{i}.R"].head.copy() for i in (1, 2, 3)] + [pb["Index3.R"].tail]
    uv = np.array([[q.dot(f), q.dot(p)] for q in pts])
    sol, *_ = np.linalg.lstsq(
        np.column_stack([uv[:, 0], uv[:, 1], np.ones(len(uv))]),
        (uv ** 2).sum(1), rcond=None)
    cu, cv = sol[0] / 2.0, sol[1] / 2.0
    r = math.sqrt(max(0.0, sol[2] + cu * cu + cv * cv))
    residual = max(abs(math.hypot(u - cu, v - cv) - r) for u, v in uv)
    h = sum(pb[f"{d}1.R"].head.dot(t)
            for d in ("Index", "Middle", "Ring", "Pinky")) / 4.0
    rest_pose()

    assert residual < 0.10, (
        f"the closed index finger's joints are {residual:.2f} off any circle, and "
        "they are supposed to lie on one: either GRIP_CURL has drifted from "
        "anim.py's or this is not the hand component the fit expects")
    assert 0.5 <= r <= 1.2, (
        f"the fist's bore came out at radius {r:.2f}, which is nothing like the "
        f"{SHAFT_R:.2f} shaft it has to close on")
    return f * cu + p * cv + t * h, r


rest_pose()

# ---------------------------------------------------------------- re-runnable
def drop(names):
    """Remove these objects and any mesh they were the last user of."""
    gone = 0
    for n in names:
        o = bpy.data.objects.get(n)
        if o is None:
            continue
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
        gone += 1
    return gone


def drop_bones(names):
    """Remove these bones. Pass children before parents."""
    if not any(n in arm.data.bones for n in names):
        return 0
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    gone = 0
    for n in names:
        eb = arm.data.edit_bones.get(n)
        if eb is not None:
            arm.data.edit_bones.remove(eb)
            gone += 1
    bpy.ops.object.mode_set(mode='OBJECT')
    return gone


_n = drop(PARTS)
if _n:
    log(f"removed {_n} part(s) from a previous run")
_n = drop_bones((TIP_BONE, ROTOR_BONE, BONE))
if _n:
    log(f"removed {_n} bone(s) from a previous run")

# The chest charger, superseded. Removed rather than parked, because unlike the
# halo it has no hand-made mesh worth keeping -- charger.py GENERATED all four of
# these and can generate them again from the constants still in it.
_n = drop(("Charger_Housing", "Charger_Rotor", "Charger_Teeth", "Charger_Core"))
if _n:
    log(f"removed {_n} superseded charger part(s)")
_n = drop_bones(("ChargerRotor", "Charger"))
if _n:
    log(f"removed {_n} superseded charger bone(s)")


# The arm is posed and the bore measured HERE, after the previous run's staff has
# been removed. pose_carry_arm turns everything under Hand.R, and on a re-run that
# subtree still holds the last run's Staff bones: dragging three bones round the
# wrist moments before deleting them is harmless, but it is harmless by accident,
# and the next thing parented to that hand would not be.
pose_carry_arm()
GRIP, BORE_R = measure_bore()
log(f"bore at ({GRIP.x:.2f}, {GRIP.y:.2f}, {GRIP.z:.2f}), radius {BORE_R:.2f}, "
    f"round a {SHAFT_R:.2f} shaft")


# ------------------------------------------------------------------- helpers
def rigid_bone_parent(obj, bone_name):
    """Bone-parent `obj` to `bone_name`, leaving its world transform identical.

    Blender anchors bone parenting at the bone TAIL, so the effective parent
    matrix P carries a +Y translation of bone.length. With matrix_parent_inverse
    = P^-1 the chain collapses to matrix_basis, which must therefore be set to
    the object's ORIGINAL WORLD matrix. Same helper and same trap as rig.py,
    hands_rebuild.py and charger.py.
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


def finish(obj, name, origin):
    """Name it, bake its transform, put its origin on `origin`, file it.

    Transforms are applied rather than left on the object because bone-parenting
    multiplies them back in -- an object carrying a rotation would be re-rotated
    by the bone it hangs off.

    Moving the origin is two steps and doing it in one is a trap charger.py
    already paid for: assigning matrix_world sets the LOCATION and drags the
    geometry with it. Bake the offset into the mesh first, then hand it back to
    the object, and nothing moves.

    transform_apply works on the SELECTION, not on the active object, so the
    explicit select is load-bearing rather than tidy. charger.py got away without
    it because every one of its parts came out of a bpy.ops primitive, and those
    leave the new object selected; the parts here are built with
    bpy.data.objects.new and arrive selected by nothing. For the generated pieces
    that is invisible -- their matrix is already identity, so the apply has
    nothing to bake -- but the SHAFT carries the whole scale-and-place matrix
    computed above, and skipping the apply threw it away and left the staff
    buried under the floor at a third of its length.
    """
    obj.name = name
    obj.data.name = name
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.data.transform(Matrix.Translation(-origin))
    obj.matrix_world = Matrix.Translation(origin)
    char = bpy.data.collections.get("Character")
    if char and obj.name not in char.objects:
        for c in list(obj.users_collection):
            c.objects.unlink(obj)
        char.objects.link(obj)
    return obj


def mesh_from(name, verts, faces):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.validate()
    me.update()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def tube(radius, height, segments=16):
    """A closed cylinder about the origin, axis on +Z. Verts and faces."""
    verts, faces = [], []
    for sgn in (+1, -1):
        for i in range(segments):
            a = 2.0 * math.pi * i / segments
            verts.append(Vector((math.cos(a) * radius, math.sin(a) * radius,
                                 sgn * height / 2.0)))
    for i in range(segments):
        j = (i + 1) % segments
        faces.append((i, j, segments + j, segments + i))
    faces.append(tuple(range(segments)))
    faces.append(tuple(segments + i for i in reversed(range(segments))))
    return verts, faces


# ============================================================ the shaft
# A ring per shaft_profile() entry, bridged in order and capped at both ends. See
# the header for why this is generated rather than lifted off `Weapon`.
def rod():
    verts, faces = [], []
    for z, rmul in shaft_profile():
        base = len(verts)
        r = SHAFT_R * rmul
        for i in range(SHAFT_SEGS):
            a = 2.0 * math.pi * i / SHAFT_SEGS
            verts.append(Vector((GRIP.x + math.cos(a) * r,
                                 GRIP.y + math.sin(a) * r, z)))
        if base:
            for i in range(SHAFT_SEGS):
                j = (i + 1) % SHAFT_SEGS
                faces.append((base - SHAFT_SEGS + i, base - SHAFT_SEGS + j,
                              base + j, base + i))
    faces.append(tuple(reversed(range(SHAFT_SEGS))))            # butt
    top = len(verts) - SHAFT_SEGS
    faces.append(tuple(top + i for i in range(SHAFT_SEGS)))     # under the cap
    return verts, faces


sverts, sfaces = rod()
shaft = finish(mesh_from(SHAFT, sverts, sfaces), SHAFT, GRIP)
log(f"shaft: {len(shaft_profile())} rings of {SHAFT_SEGS}, "
    f"z {BUTT_Z:.2f}..{TOP_Z:.2f}, radius {SHAFT_R}")


# ============================================================ the fan
# One blade, built as a ribbon of cross-sections along a swept curve, then copied
# round the axis. Written out as vertices rather than grown from a primitive
# because every one of the five things that shape it -- radius, rise, sweep,
# chord taper and lean -- has to vary along the SAME parameter, and there is no
# modifier stack that does that as legibly as the loop below.
#
# The frame at each sample is the cylindrical one: u radially out, v tangential,
# world Z axial. The blade's flat face points along v, which is what makes it a
# turbine blade rather than a spoke -- a surface presented to a wind going round
# the axis.
#
# LEAN is the part that reads. At the root the chord is axial, so the blade
# stands up parallel to the shaft; by the tip it has leaned FAN_LEAN degrees
# toward radial, so the blade lies over and the three of them splay outward like
# a hand rather than standing in a cage. That lean is the whole difference
# between figure 6(g) and figure 6(a).
def blade_mesh():
    verts, faces = [], []
    for i in range(FAN_SEGS + 1):
        t = i / float(FAN_SEGS)
        r = FAN_R0 + (FAN_R1 - FAN_R0) * t
        z = FAN_RISE * (t ** 1.35)          # climbs slowly, then faster
        a = math.radians(FAN_SWEEP) * t
        chord = FAN_CHORD0 + (FAN_CHORD1 - FAN_CHORD0) * t
        lean = math.radians(FAN_LEAN) * t

        u = Vector((math.cos(a), math.sin(a), 0.0))
        v = Vector((-math.sin(a), math.cos(a), 0.0))
        c = u * r + Vector((0.0, 0.0, z))
        # Chord direction rolls from axial (+Z) at the root toward radial (+u).
        d = Vector((0.0, 0.0, 1.0)) * math.cos(lean) + u * math.sin(lean)

        base = len(verts)
        for sc, st in ((+1, +1), (+1, -1), (-1, -1), (-1, +1)):
            verts.append(c + d * (sc * chord / 2.0) + v * (st * FAN_THICK / 2.0))
        if i:
            p = base - 4
            for k in range(4):
                faces.append((p + k, p + (k + 1) % 4,
                              base + (k + 1) % 4, base + k))
    faces.append((0, 1, 2, 3))                                  # root cap
    last = len(verts) - 4
    faces.append((last + 3, last + 2, last + 1, last))          # tip cap
    return verts, faces


bverts, bfaces = blade_mesh()
verts, faces = [], []
for b in range(BLADES):
    R = Matrix.Rotation(2.0 * math.pi * b / BLADES, 3, 'Z')
    off = len(verts)
    verts += [R @ v for v in bverts]
    faces += [tuple(i + off for i in f) for f in bfaces]

# The hub the blades are welded to, folded into the SAME object: the rotor never
# moves in pieces, and three blades plus a hub as four objects would be four
# entries in rustify's EXCEPT table and four draw calls for one shape.
hverts, hfaces = tube(HUB_R, HUB_H)
off = len(verts)
verts += hverts
faces += [tuple(i + off for i in f) for f in hfaces]

hub = Vector((GRIP.x, GRIP.y, HUB_Z))
fan = finish(mesh_from(FAN, [v + hub for v in verts], faces), FAN, hub)

# ---- the stationary collar ---------------------------------------------------
# A rotor turning with nothing fixed beside it barely reads -- the same point
# charger.py made about its housing. This is the bracket the hub spins on, and it
# stays on the Staff bone while the fan goes round.
mount_c = Vector((GRIP.x, GRIP.y, HUB_Z - HUB_H / 2.0 - MOUNT_H / 2.0))
mverts, mfaces = tube(MOUNT_R, MOUNT_H)
mount = finish(mesh_from(MOUNT, [v + mount_c for v in mverts], mfaces),
               MOUNT, mount_c)

# ---- the emitter --------------------------------------------------------------
# The lens at the very top, above the blades, and the only part of the staff that
# glows. This is what the wind-up lights and what the bolt is drawn from, so it
# has to be clear of the fan -- an emitter down among the blades is occluded by
# them for a third of every turn.
core_c = Vector((GRIP.x, GRIP.y, TOP_Z - CORE_H / 2.0))
cverts, cfaces = tube(CORE_R, CORE_H)
core = finish(mesh_from(CORE, [v + core_c for v in cverts], cfaces),
              CORE, core_c)


# ============================================================ bones
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT')
ebs = arm.data.edit_bones

# All three point +Z, the shaft's own axis, so a rotation of StaffRotor about
# world Z at rest spins the fan in its own plane instead of tumbling it -- the
# same reason charger.py laid its rotor bone along the ring's axis.
#
# Staff hangs off Hand.R, so raising the staff is raising the ARM and anim.py
# never keys this bone at all.
for name, parent, head, tail in (
        (BONE, PARENT_BONE, GRIP, Vector((GRIP.x, GRIP.y, HUB_Z))),
        (ROTOR_BONE, BONE, Vector((GRIP.x, GRIP.y, HUB_Z)),
         Vector((GRIP.x, GRIP.y, HUB_Z + HUB_H))),
        (TIP_BONE, BONE, Vector((GRIP.x, GRIP.y, TOP_Z)),
         Vector((GRIP.x, GRIP.y, TOP_Z + 1.0)))):
    eb = ebs.new(name)
    eb.head, eb.tail, eb.roll = head, tail, 0.0
    eb.parent = ebs[parent]
    eb.use_connect = False

bpy.ops.object.mode_set(mode='OBJECT')

rigid_bone_parent(shaft, BONE)
rigid_bone_parent(mount, BONE)
rigid_bone_parent(core, BONE)
rigid_bone_parent(fan, ROTOR_BONE)

log(f"built {SHAFT}/{MOUNT}/{CORE} on {BONE} and {FAN} on {ROTOR_BONE}; "
    f"rig now has {len(arm.data.bones)} bones")


# ============================================================ verify
problems = []

for n in BONES:
    if n not in arm.data.bones:
        problems.append(f"bone {n} missing")
if BONE in arm.data.bones and arm.data.bones[BONE].parent.name != PARENT_BONE:
    problems.append(f"{BONE} is not parented to {PARENT_BONE}")

for n, want in ((SHAFT, BONE), (MOUNT, BONE), (CORE, BONE), (FAN, ROTOR_BONE)):
    o = bpy.data.objects.get(n)
    if o is None:
        problems.append(f"mesh {n} missing")
    elif o.parent_type != 'BONE' or o.parent_bone != want:
        problems.append(f"{n} is not bone-parented to {want}")

# `Weapon` is not used any more, but it must not be COLLATERAL either: it is
# hand-made geometry that exists nowhere else, and it stays whole in WIP_Spares.
# Cheap to assert, and the earlier version of this script did edit a copy of it.
d = bpy.data.objects.get(SPARE_STAFF)
if d is None:
    problems.append(f"{SPARE_STAFF} has gone from the .blend; it must be left "
                    f"whole in {SPARES}")
elif len(d.data.vertices) != SPARE_STAFF_VERTS:
    problems.append(f"{SPARE_STAFF} lost vertices (expected "
                    f"{SPARE_STAFF_VERTS}, found {len(d.data.vertices)}); it "
                    f"must be left untouched in {SPARES}")

# The shaft has to have LANDED. Everything above computes a matrix for it and
# then hands that matrix to finish(), and a finish() that quietly declines to
# bake it leaves a staff that is the right shape in the wrong place -- which is
# exactly what happened, and which nothing else here would have noticed.
sv = [shaft.matrix_world @ v.co for v in shaft.data.vertices]
s_lo, s_hi = min(v.z for v in sv), max(v.z for v in sv)
if abs(s_lo - BUTT_Z) > 0.5 or abs(s_hi - TOP_Z) > 0.5:
    problems.append(f"the shaft spans z {s_lo:.2f}..{s_hi:.2f}, not "
                    f"{BUTT_Z:.2f}..{TOP_Z:.2f}")
off_axis = max((Vector((v.x, v.y, 0.0)) - Vector((GRIP.x, GRIP.y, 0.0))).length
               for v in sv)
if off_axis > SHAFT_R * 4.0:
    problems.append(f"the shaft strays {off_axis:.2f} off the grip column; "
                    f"the widest collar should be well inside {SHAFT_R * 4.0:.2f}")

# Clearance against the BODY, measured off the real meshes rather than against the
# numbers quoted in the header. These are the constraints that placed the staff, so
# they are worth failing the build over: geometry buried inside the creature is
# invisible in a wireframe and extremely visible in a render, and earlier versions
# of this script shipped both failures.
#
# The fan and the shaft are checked differently, because they are avoiding
# different things.
#
#   The SHAFT is held on the arm's own centre line -- that is the whole point of a
#   palm grip -- so what keeps it out of the creature is being in FRONT of the arm.
#   It is compared on x. An earlier version compared on y, which was right while
#   the staff was carried out to the side and says nothing at all now: it would
#   happily pass a shaft driven straight down the middle of the forearm.
#
#   The FAN is up beside the head and can foul it from any direction, so a
#   one-sided test will not do. It is compared against a STAR PROFILE of the body:
#   per z band and per azimuth around that band's centre, how far out the body
#   actually reaches. A fan vertex nearer the centre than the body's surface in its
#   own direction is inside the creature. That is exact for a convex cross-section
#   and conservative for this one, which is what a build-stopping check should be.
#
# The HAND is excluded from both. It is supposed to be touching the staff.
#
# Excluded by BONE, not by the z band a previous version used. The band was a pair
# of numbers describing where the fist happened to sit, and the comment on it had
# to warn that a stale one "silently makes the grip and clearance checks measure
# nothing" -- which is exactly what the carry pose would have done to it, since it
# moves the fist four units up and five forward. What the checks actually mean by
# "the hand" is everything hanging off Hand.R, and the rig already knows that.
BANDS = 2.0        # z band height
AZIMUTHS = 24      # star profile resolution


def under(bone_name):
    """Names of `bone_name` and every bone below it."""
    out = {bone_name}

    def descend(n):
        for b in arm.data.bones[n].children:
            out.add(b.name)
            descend(b.name)

    descend(bone_name)
    return out


HAND_BONES = under(PARENT_BONE) - set(BONES)
DIGIT_BONES = {b for b in HAND_BONES
               if any(b.startswith(d) for d in GRIP_CURL)}

body = [o for o in bpy.data.objects
        if o.type == 'MESH' and o.parent_bone and not o.name.startswith("Staff_")]
limb = [o for o in body if o.parent_bone not in HAND_BONES]


def band_of(z):
    return int(z // BANDS)


def azimuth_of(dx, dy):
    a = math.atan2(dy, dx) + math.pi
    return min(AZIMUTHS - 1, int(a / (2.0 * math.pi) * AZIMUTHS))


if body:
    # The star profile: a centre per band, then a reach per azimuth about it.
    pts = {}
    for o in limb:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            pts.setdefault(band_of(w.z), []).append(w)

    centre, reach = {}, {}
    for k, ps in pts.items():
        cx = sum(p.x for p in ps) / len(ps)
        cy = sum(p.y for p in ps) / len(ps)
        centre[k] = (cx, cy)
        r = [0.0] * AZIMUTHS
        for p in ps:
            dx, dy = p.x - cx, p.y - cy
            i = azimuth_of(dx, dy)
            r[i] = max(r[i], math.hypot(dx, dy))
        reach[k] = r

    worst, worst_z = None, None
    for v in (fan.matrix_world @ v.co for v in fan.data.vertices):
        k = band_of(v.z)
        if k not in centre:
            continue                       # nothing of the body at this height
        cx, cy = centre[k]
        dx, dy = v.x - cx, v.y - cy
        gap = math.hypot(dx, dy) - reach[k][azimuth_of(dx, dy)]
        if worst is None or gap < worst:
            worst, worst_z = gap, v.z

    if worst_z is None:
        log("fan sits entirely above the body")
    elif worst < 0.0:
        problems.append(f"the fan is inside the body by {-worst:.2f} units at "
                        f"z {worst_z:.1f}")
    else:
        log(f"fan clears the body by {worst:.2f} units "
            f"(closest at z {worst_z:.1f})")

    # The shaft is tested as a COLUMN against the body's vertices, not the other
    # way round, and that is not a stylistic choice.
    #
    # The rod carries vertex rings only at its fourteen profile heights, and the
    # arm sits in the fifteen-unit gap between the ring above the grip and the ring
    # under the turbine. Sampling the SHAFT's vertices therefore looks at nothing
    # at all in the region that matters and reports a clean pass -- which is
    # exactly what it did, and it is the same sparse-rod trap that made the
    # hand-reach check pass by doing nothing an earlier time round.
    #
    # Walking the body's vertices instead needs the shaft's radius as a function of
    # height, which the profile already is.
    prof = shaft_profile()

    def shaft_radius(z):
        if z < prof[0][0] or z > prof[-1][0]:
            return None                    # above or below the staff
        for (z0, r0), (z1, r1) in zip(prof, prof[1:]):
            if z0 <= z <= z1:
                t = 0.0 if z1 == z0 else (z - z0) / (z1 - z0)
                return SHAFT_R * (r0 + (r1 - r0) * t)
        return None

    worst, worst_z, worst_who = None, None, None
    for o in limb:
        for v in o.data.vertices:
            w = o.matrix_world @ v.co
            r = shaft_radius(w.z)
            if r is None:
                continue
            gap = math.hypot(w.x - GRIP.x, w.y - GRIP.y) - r
            if worst is None or gap < worst:
                worst, worst_z, worst_who = gap, w.z, o.name

    if worst is None:
        log("the staff spans none of the body's height")
    elif worst < 0.0:
        problems.append(f"the shaft is inside {worst_who} by {-worst:.2f} units "
                        f"at z {worst_z:.1f}; it has to pass clear of the arm, "
                        "not through it")
    else:
        log(f"shaft clears the body by {worst:.2f} units "
            f"({worst_who} at z {worst_z:.1f})")

    # ...and the opposite failure: a shaft the fingers cannot reach is a staff
    # standing beside the creature rather than one it is holding.
    #
    # Measured with the hand CLOSED, because that is the only pose it is ever seen
    # in -- anim.py holds GRIP_CURL in Idle, Walk and Attack alike. At rest the
    # fingers hang straight down and stop 0.9 short of where they finish once
    # curled, which is enough to fail a grip that is actually fine.
    # Measured RADIALLY about the shaft's axis, and around it by azimuth. The old
    # check compared x alone, which was the right question while the shaft stood in
    # front of the palm and the fingers could only press at it from one side. Now
    # the fingers come round it, so what matters is how close they get and HOW FAR
    # ROUND they reach -- a hand that touches the shaft at one point is still
    # pinching it.
    # DIGITS only, not the whole hand. The palm is a slab three units across whose
    # far corners sit at every azimuth around the column, so including it reports a
    # confident 360 degrees for a hand that is doing nothing at all -- which is
    # exactly what the first version of this check said about a grip the render
    # showed to be one-sided.
    curl_hand("R")
    digits = [o.matrix_world @ v.co
              for o in body if o.parent_bone in DIGIT_BONES
              for v in o.data.vertices]
    rest_pose()

    if digits:
        sectors = set()
        nearest = 1e9
        for w in digits:
            dx, dy = w.x - GRIP.x, w.y - GRIP.y
            d = math.hypot(dx, dy)
            nearest = min(nearest, d)
            # An ANNULUS about the surface. Vertices well inside the shaft are
            # excluded as well as those well outside: a point near the axis has an
            # essentially arbitrary azimuth, so a fingertip that has driven through
            # the middle scatters across every sector and reports a confident 360
            # for a grip that is one-sided.
            if SHAFT_R * 0.6 <= d <= SHAFT_R * 1.6:
                sectors.add(int((math.atan2(dy, dx) + math.pi) / (math.pi / 6.0)))

        around = len(sectors) * 30
        if nearest > SHAFT_R:
            problems.append(f"the closed fingers' nearest point is {nearest:.2f} "
                            f"from the shaft's axis, outside its {SHAFT_R:.2f} "
                            "radius; they are not touching it")
        elif around < 150:
            problems.append(f"the closed fingers reach only {around} degrees round "
                            "the shaft; that is a pinch, not a grip")
        else:
            log(f"closed fingers wrap {around} degrees of the shaft, "
                f"nearest point {nearest:.2f} against a {SHAFT_R:.2f} radius")

if problems:
    for p in problems:
        print(f"[staff] FAIL: {p}")
    raise SystemExit("[staff] verification failed")

bpy.ops.wm.save_mainfile()
log("OK, SAVED")
