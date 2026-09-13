"""Net gun — a chunky sci-fi capture pistol built around a net canister.

The design brief is a concept illustration: a squat pistol whose whole front
half is a two-tone drum lying on its side, a charcoal nose over the front 40
percent and a bright orange body behind it, its bore left open and framed by
four petals splayed back off the rim with a bundled net crammed in behind
them. Dark nose over orange body is the single most recognisable thing about
the silhouette, so the seam that carries it is a real edge loop, not a guess. Behind the drum sits a boxy grey
receiver with orange diagonal stripes, a tall optic riser with a green lens and
a knurled turret, a raked cloth-and-cord wrapped grip, a trigger in its guard,
a blue L-bracket under the drum with two hoses looping back into the receiver's
underside, and a small roller wheel under the drum's rear.

The drum reads first and must keep reading first: it is half the gun's length
and the widest thing on it, which is what lets a player tell a net gun from
every other sidearm in the hotbar at a glance.

Two variations, differing in the one thing gameplay has to show:

  Coll_NetGun_Loaded  the hero — net bundle in the bore, ships to Unity
  Coll_NetGun_Spent   the same gun with an empty bore

Loaded and spent are a *silhouette* difference rather than an animation: the
bore is modelled open, so the bundle is simply an object the prefab switches
off. Nothing has to move, nothing has to be rigged, and the state survives
being seen from across the map or as a 256 px inventory icon.

Built at about 0.62 m from bore rim to butt, with the canister 0.26 m across.
Front is -Y, up is +Z, matching the library convention and gravel_blaster. The
origin sits on the canister axis at its rear face, level with the breech, which
is roughly where the gun's mass balances. Marker_Muzzle and Marker_Grip cubes
carry the firing point and the hand point across the FBX for the Unity prefab
to adopt (see portal_gun.py for why markers, not empties).

Generation script — historical record. The .blend is the source of truth;
never re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

import bmesh  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

# Index 0 first and structural: `bmesh.ops.bevel` stamps every face it creates
# with material index 0, so whatever sits here is the colour of every chamfered
# edge in the file.
(STEEL, DARK, ORANGE, GREY, RUST, GREEN, CANVAS, HEMP, BLUE, CORAL,
 CHROME, BLACK) = range(12)
MATS = [
    "Mat_Metal_Steel_Worn",       #  0  petals, bands, trigger, every bevel
    "Mat_Metal_Steel_Dark",       #  1  canister nose, optic, grip core
    "Mat_Paint_Safety_Orange",    #  2  canister body AND receiver stripes
    "Mat_Neutral_Panel_Grey",     #  3  the receiver
    "Mat_Metal_HullRust_Orange",  #  4  weathering on the canister body
    "Mat_Emissive_Green_CRT",     #  5  the optic lens
    "Mat_Fabric_Canvas_Faded",    #  6  cloth wrap at the top of the grip
    "Mat_Fabric_Rope_Hemp",       #  7  the net bundle, cord at the grip's base
    "Mat_Fabric_Tarp_Azure",      #  8  the L-bracket and the blue hose
    "Mat_Paint_Coral_Faded",      #  9  the red hose
    "Mat_Metal_Chrome_Scuffed",   # 10  roller hub
    "Mat_Neutral_Black_Matte",    # 11  bore interior, roller tyre, trigger pad
]

# Bevel only the boxy faces, and narrowly. The hoses are 9 mm tubes and the
# receiver's stripes are 3 mm proud; a whole-part bevel at this scale exceeds
# half of either and finish()'s remove_doubles welds the result into a blob.
BEVEL_W = 0.0016

# ── The dimensions the rest of the gun is measured from ────────────────────
CAN_R = 0.130          # canister radius: 0.26 m across, the widest thing here
MUZZLE_Y = -0.310      # the bore rim — the front of the whole gun
CAN_BACK_Y = 0.0       # canister rear face, and the build origin
# The charcoal nose ends and the orange body begins here, 40 percent of the
# drum back from the rim. The hollow mouth section ends on exactly the same
# plane on purpose: one constant, so the colour change always lands on a real
# edge loop instead of somewhere in the middle of a smooth-shaded cylinder.
SPLIT_Y = -0.186
BORE_R = 0.1095        # clear radius inside the black bore lining

GRIP_TOP = Vector((0.0, 0.168, -0.055))
GRIP_RAKE = math.radians(33.0)
GRIP_LEN = 0.205

# The grip's half-width and half-depth down its own axis. One table rather
# than two, because the cloth and cord wraps are lofted from the same numbers
# as the core and have to taper with it.
GRIP_SECTIONS = [
    (0.000, 0.026, 0.042),
    (-0.045, 0.025, 0.038),
    (-0.110, 0.023, 0.032),
    (-0.172, 0.024, 0.036),
    (-GRIP_LEN, 0.025, 0.040),
]


# --------------------------------------------------------------------------
# Local helpers
# --------------------------------------------------------------------------

def sweep(p, pts, radius, mat, seg=8):
    """A swept tube through a polyline, using parallel-transport frames.

    Copied from portal_gun.py, which needed it for the same reason the hoses
    do: _buildlib has no sweep, and a chain of overlapping `cyl` calls pinches
    visibly where the path bends. Transporting the normal from ring to ring
    rather than rebuilding it per segment is what stops the tube twisting once
    the path leaves a single plane.
    """
    pts = [Vector(q) for q in pts]
    n_pts = len(pts)

    tangents = []
    for i in range(n_pts):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == n_pts - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        tangents.append(t.normalized())

    seed = Vector((0.0, 0.0, 1.0))
    if abs(tangents[0].dot(seed)) > 0.95:
        seed = Vector((0.0, 1.0, 0.0))
    normal = (seed - tangents[0] * seed.dot(tangents[0])).normalized()

    rings = []
    for i, (q, t) in enumerate(zip(pts, tangents)):
        if i > 0:
            prev = tangents[i - 1]
            axis = prev.cross(t)
            if axis.length > 1e-6:
                normal = (Matrix.Rotation(prev.angle(t), 4, axis.normalized())
                          @ normal)
            normal = (normal - t * normal.dot(t)).normalized()
        binormal = t.cross(normal).normalized()
        rings.append([
            q + normal * (math.cos(2 * math.pi * k / seg) * radius)
              + binormal * (math.sin(2 * math.pi * k / seg) * radius)
            for k in range(seg)])

    bm2 = bmesh.new()
    vrings = [[bm2.verts.new(tuple(c)) for c in ring] for ring in rings]
    for a, b in zip(vrings, vrings[1:]):
        for i in range(seg):
            j = (i + 1) % seg
            bm2.faces.new((a[i], a[j], b[j], b[i]))
    bm2.faces.new(vrings[0])
    bm2.faces.new(list(reversed(vrings[-1])))

    faces = p._absorb(bm2, mat)
    for f in faces:
        f.smooth = True
    return faces


def catmull(ctrl, per_span=5):
    """Resample a control polyline into a smooth path.

    The hoses are given as five or six waypoints because that is how a sagging
    loop is legible to write down; swept straight through, five rings around a
    bend faceted badly enough to read as a folded straw. Catmull-Rom puts as
    many rings into the curve as it needs while still passing through every
    waypoint that was typed.
    """
    q = [Vector(ctrl[0])] + [Vector(c) for c in ctrl] + [Vector(ctrl[-1])]
    out = []
    for i in range(len(q) - 3):
        p0, p1, p2, p3 = q[i], q[i + 1], q[i + 2], q[i + 3]
        for s in range(per_span):
            t = s / per_span
            out.append(0.5 * ((2 * p1)
                              + (-p0 + p2) * t
                              + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t))
    out.append(q[-1])
    return out


def placed(p, faces, matrix):
    """Move a group of just-built faces into a rotated frame.

    _buildlib's `loft` only runs along a world axis and the grip is raked 33
    degrees, so the grip is lofted upright and then transported. Deriving the
    transform beats hand-typing tilted cross-sections, for the same reason
    gravel_blaster's `strut` derives its rotation from its endpoints: a guessed
    compound angle is exactly the kind of number that goes silently wrong.
    """
    verts = list({v for f in faces for v in f.verts})
    bmesh.ops.transform(p.bm, matrix=matrix, verts=verts)
    return faces


def octagon(vc, hw, hh, c=0.32):
    """A chamfered-rectangle profile in (u, v) for lofting the grip.

    `c` is how much of each corner is cut. A plain rectangle reads as a plank
    and a full ellipse reads as a broom handle; a grip is neither.
    """
    cw, ch = hw * c, hh * c
    return [(-hw + cw, vc - hh), (hw - cw, vc - hh), (hw, vc - hh + ch),
            (hw, vc + hh - ch), (hw - cw, vc + hh), (-hw + cw, vc + hh),
            (-hw, vc + hh - ch), (-hw, vc - hh + ch)]


def strut(p, a, b, size, mat):
    """A box run between two points — the trigger guard's limbs.

    Length and rotation come from the endpoints rather than being typed;
    copied from gravel_blaster.py, where the same shape had the same problem.
    """
    a, b = Vector(a), Vector(b)
    d = b - a
    rot = Vector((0, 1, 0)).rotation_difference(d.normalized()).to_matrix() \
                           .to_4x4()
    return p.box((a + b) / 2.0, (size[0], d.length, size[1]), mat, rot=rot)


def marker(coll, name, at, mats, size=0.004):
    """A tiny cube whose only job is to carry a coordinate across the FBX.

    Blender empties are not exported (`object_types={"MESH"}`), and the FBX
    arrives in Unity rotated and rescaled, so hand-deriving a muzzle position
    on the Unity side means composing two conventions and hoping. A named 4 mm
    mesh survives the trip and the prefab reads its transform. Copied from
    portal_gun.py, which documents the whole reasoning.
    """
    p = Part(mats)
    p.box((0, 0, 0), (size, size, size), STEEL)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


# --------------------------------------------------------------------------
# Canister — the shape the whole silhouette hangs on
# --------------------------------------------------------------------------

def canister(p):
    """The two-tone drum, its open mouth and the four petals off the rim."""
    # Charcoal nose in front, orange body behind — dark muzzle end over a
    # bright body is what makes this drum recognisable at a glance.
    #
    # The nose IS the hollow mouth section: a hollow tube rather than a capped
    # cylinder with a dark disc on the end, because the bore has to survive
    # being looked into from an angle, which a painted-on disc does not.
    p.tube((0, (MUZZLE_Y + SPLIT_Y) / 2.0, 0), CAN_R, 0.016,
           SPLIT_Y - MUZZLE_Y, 'Y', 24, DARK)
    p.cyl((0, (SPLIT_Y + CAN_BACK_Y) / 2.0, 0), CAN_R,
          CAN_BACK_Y - SPLIT_Y, 'Y', 24, ORANGE)

    p.tube((0, (MUZZLE_Y + SPLIT_Y) / 2.0 + 0.001, 0), BORE_R + 0.004, 0.004,
           SPLIT_Y - MUZZLE_Y - 0.002, 'Y', 24, BLACK)
    # Bore floor, 0.124 m in. Deep enough that the opening reads as a barrel
    # even when the bundle has been switched off.
    p.cyl((0, SPLIT_Y + 0.002, 0), BORE_R + 0.004, 0.005, 'Y', 24, BLACK)

    # Reinforcing bands: the rim lip, the two-tone seam and one on the body.
    p.torus((0, MUZZLE_Y + 0.004, 0), CAN_R - 0.005, 0.006, 'Y', 24, 8, STEEL)
    p.torus((0, SPLIT_Y, 0), CAN_R + 0.001, 0.006, 'Y', 24, 8, STEEL)
    p.torus((0, -0.055, 0), CAN_R + 0.001, 0.006, 'Y', 24, 8, STEEL)

    # Four longitudinal ribs down the orange body, clocked between the petals.
    # In the older, duller orange rather than in steel: Safety_Orange is a flat
    # bright paint with no texture of its own at this size, and a cold grey
    # strip across it read as a decal stuck on a cylinder. A warm, darker,
    # related tone reads as scuffed strapping over the paint instead, which is
    # the second tone the body needs without spending the dark-nose-over-
    # bright-body silhouette on a weathering band.
    for k in range(4):
        a = math.radians(90 * k)
        # The rotation is 90 degrees MINUS the clock angle, not the angle: the
        # box's local +Z has to end up radial and its width tangential, and
        # feeding the angle straight in lays every rib flat on the drum.
        p.box(((CAN_R - 0.002) * math.cos(a), (SPLIT_Y + CAN_BACK_Y) / 2.0,
               (CAN_R - 0.002) * math.sin(a)),
              (0.014, CAN_BACK_Y - SPLIT_Y - 0.030, 0.010),
              RUST, rot=Matrix.Rotation(math.radians(90) - a, 4, 'Y'))

    petals(p)

    # Breech collar joining the drum's rear face to the receiver.
    p.cyl((0, CAN_BACK_Y + 0.010, 0), 0.076, 0.024, 'Y', 20, DARK)
    p.torus((0, CAN_BACK_Y + 0.020, 0), 0.078, 0.006, 'Y', 20, 8, STEEL)


def petals(p, count=4, length=0.088, splay_deg=21.0, stations=4, across=5):
    """Four plates hinged at the bore rim and folded back over the drum.

    Built as curved shells that follow the drum rather than as flat plates on
    chords. A flat plate wide enough to frame the bore stands its own corners
    11 mm off a 0.13 m barrel, which reads as damage rather than as design, and
    it puts the gun's widest point outside the drum.

    Clocked to the diagonals rather than to the vertical and horizontal, for
    two reasons that both came out of building it the other way first: a petal
    on the horizontal put the gun's widest point 6 cm outside the drum, and a
    petal on the bottom ran straight through the L-bracket under the drum's
    belly.
    """
    splay = math.radians(splay_deg)
    for k in range(count):
        a0 = math.radians(45 + 90 * k)

        rings = []
        for i in range(stations):
            t = i / (stations - 1)
            y = MUZZLE_Y + length * math.cos(splay) * t
            r_in = CAN_R - 0.004 + length * math.sin(splay) * t
            r_out = r_in + 0.008 - 0.003 * t
            dth = (0.052 - 0.022 * t) / r_in

            outer, inner = [], []
            for j in range(across):
                a = a0 + dth * (-1.0 + 2.0 * j / (across - 1))
                c, sn = math.cos(a), math.sin(a)
                outer.append(Vector((r_out * c, y, r_out * sn)))
                inner.append(Vector((r_in * c, y, r_in * sn)))
            rings.append(outer + list(reversed(inner)))

        n = 2 * across
        bm2 = bmesh.new()
        vr = [[bm2.verts.new(tuple(c)) for c in ring] for ring in rings]
        for lo, hi in zip(vr, vr[1:]):
            for i in range(n):
                j = (i + 1) % n
                bm2.faces.new((lo[i], lo[j], hi[j], hi[i]))
        bm2.faces.new(vr[0])
        bm2.faces.new(list(reversed(vr[-1])))
        p._absorb(bm2, STEEL)

        # Hinge boss across the petal root, lying tangentially like a real
        # hinge pin. Rotating by the clock angle instead of its negative points
        # it straight out of the drum, which is the same sign trap as the ribs.
        p.cyl(Vector((math.cos(a0), 0.0, math.sin(a0))) * (CAN_R + 0.002)
              + Vector((0, MUZZLE_Y + 0.009, 0)),
              0.009, 0.030, 'Z', 8, STEEL, rot=Matrix.Rotation(-a0, 4, 'Y'))


def bundle(coll, mats, name):
    """The compressed net crammed into the bore — the loaded/spent tell.

    A lumpy lofted mass rather than a woven net: at the depth it sits, a real
    mesh would be a few hundred triangles of moire that resolves into nothing.
    What has to read is *something bulky and fibrous is in there*, and a
    jittered profile plus three folded cord loops on its face does that at
    every distance the gun is seen from.
    """
    p = Part(mats)

    seg = 14
    # Recessed 30 mm behind the rim and 10 mm clear of the bore lining, so the
    # black ring reads all the way round it. Flush with the rim, the bundle
    # stopped being *in* a barrel and became a cap on the end of one.
    front, back = MUZZLE_Y + 0.030, SPLIT_Y - 0.001
    profile_r = [0.058, 0.086, 0.094, 0.090, 0.082]
    # Deterministic jitter: rebuilding the file must produce the same lumps.
    jitter = [0.972, 1.048, 0.944, 1.031, 0.958, 1.062, 0.936, 1.014,
              0.979, 1.043, 0.951, 1.026, 0.990, 1.055]

    sections = []
    for i, r in enumerate(profile_r):
        t = i / (len(profile_r) - 1)
        y = front + (back - front) * t
        prof = []
        for k in range(seg):
            a = 2 * math.pi * k / seg
            rr = r * jitter[(k + i * 3) % len(jitter)]
            prof.append((rr * math.cos(a), rr * math.sin(a)))
        sections.append((y, prof))
    p.loft(sections, axis='Y', mat=HEMP)

    # Folded cord loops on the exposed face, so the mass reads as *net* rather
    # than as a wad of foam.
    for x, z, major in ((-0.034, 0.026, 0.032), (0.038, -0.014, 0.028),
                        (0.004, -0.048, 0.024)):
        p.torus((x, front - 0.002, z), major, 0.010, 'Y', 12, 6, HEMP)

    return p.finish(name, coll)


# --------------------------------------------------------------------------
# Receiver, optic, grip, furniture
# --------------------------------------------------------------------------

def receiver(p, hard):
    """The boxy grey body, its stripes and its panel lines."""
    hard += p.box((0, 0.115, 0.005), (0.088, 0.250, 0.134), GREY)
    hard += p.box((0, 0.130, 0.076), (0.070, 0.200, 0.014), GREY)
    hard += p.box((0, 0.246, 0.005), (0.078, 0.014, 0.120), STEEL)

    # Panel lines: two shallow dark strips let the flat grey slab read as
    # assembled plate rather than one moulded block.
    for z in (0.040, -0.026):
        for sx in (-1, 1):
            p.box((sx * 0.0445, 0.115, z), (0.002, 0.230, 0.006), DARK)
    p.rivets((-0.030, 0.232, 0.070), (0.030, 0.232, 0.070), 2,
             radius=0.005, height=0.003, axis='Z', mat=STEEL)

    # Diagonal hazard stripes. Kept off the `hard` bevel list on purpose: they
    # are 3 mm proud and BEVEL_W would eat the corners off them.
    for sx in (-1, 1):
        for i in range(3):
            p.box((sx * 0.0455, 0.070 + i * 0.052, 0.006),
                  (0.003, 0.020, 0.110), ORANGE,
                  rot=Matrix.Rotation(math.radians(38), 4, 'X'))


def optic(p, hard):
    """The riser, the tube, the green lens and the knurled turret cap."""
    hard += p.box((0, 0.080, 0.092), (0.044, 0.110, 0.046), DARK)
    p.cyl((0, 0.075, 0.142), 0.030, 0.150, 'Y', 18, DARK)
    p.torus((0, 0.002, 0.142), 0.028, 0.005, 'Y', 18, 8, STEEL)
    p.torus((0, 0.148, 0.142), 0.028, 0.005, 'Y', 18, 8, STEEL)

    # The lens sits a few millimetres inside the ring so it catches a shadow
    # rather than floating on the front face.
    p.cyl((0, 0.006, 0.142), 0.024, 0.004, 'Y', 18, GREEN)
    p.cyl((0, 0.152, 0.142), 0.021, 0.006, 'Y', 14, BLACK)

    # Turret: a low-segment cylinder under a coarse torus reads as knurled at
    # a tenth of the triangles a real knurl costs.
    p.cyl((0, 0.070, 0.170), 0.019, 0.030, 'Z', 12, STEEL)
    p.torus((0, 0.070, 0.181), 0.019, 0.004, 'Z', 12, 6, STEEL)


def grip(p):
    """The raked grip, lofted upright and then transported into its rake.

    Nothing here joins the `hard` bevel list: the core and both wraps are
    lofted octagons, already chamfered by the profile itself, and bevelling a
    13 mm-tall wrap band at BEVEL_W closes it up.
    """
    frame = (Matrix.Translation(GRIP_TOP)
             @ Matrix.Rotation(GRIP_RAKE, 4, 'X'))

    core = [(z, octagon(0.0, hw, hh)) for z, hw, hh in GRIP_SECTIONS]
    placed(p, p.loft(core, axis='Z', mat=DARK), frame)
    placed(p, p.loft([
        (-GRIP_LEN, octagon(0.0, 0.026, 0.041)),
        (-GRIP_LEN - 0.012, octagon(0.0, 0.024, 0.037)),
    ], axis='Z', mat=STEEL), frame)

    # Cloth at the top where the hand actually closes, cord below it. Each
    # wrap is a short loft of the grip's OWN cross-section, inflated a few
    # millimetres, rather than a ring: a circular ring only ever touches the
    # grip's two flat sides, so the first version read as a row of windows cut
    # in a magazine instead of as cloth going round a handle.
    for i in range(5):
        wrap(p, frame, -0.012 - i * 0.021, 0.016, 0.005 + (i % 2) * 0.001,
             CANVAS)
    for i in range(4):
        wrap(p, frame, -0.124 - i * 0.018, 0.013, 0.004, HEMP)


def wrap(p, frame, z0, length, pad, mat):
    """One band of wrapping around the grip, following its taper."""
    z1 = z0 - length
    placed(p, p.loft([
        (z0, octagon(0.0, *grip_section(z0, pad))),
        (z1, octagon(0.0, *grip_section(z1, pad))),
    ], axis='Z', mat=mat), frame)


def grip_section(z, pad=0.0):
    """The grip's half-width and half-depth at `z`, inflated by `pad`.

    Interpolated from the same table the core is lofted from, so a wrap can
    never end up narrower than the handle it is supposed to be going round.
    """
    z = min(max(z, GRIP_SECTIONS[-1][0]), GRIP_SECTIONS[0][0])
    for (za, wa, ha), (zb, wb, hb) in zip(GRIP_SECTIONS, GRIP_SECTIONS[1:]):
        if zb <= z <= za:
            t = (za - z) / (za - zb)
            return wa + (wb - wa) * t + pad, ha + (hb - ha) * t + pad
    raise ValueError("grip section table does not cover z=%r" % z)


def furniture(p, hard):
    """Trigger, guard, the detail block, the L-bracket, hoses and roller."""
    # Detail block under the receiver's front, between the breech and the
    # trigger — the concept's small box of mechanism.
    hard += p.box((0, 0.038, -0.080), (0.060, 0.066, 0.038), DARK)
    p.rivets((0, 0.014, -0.099), (0, 0.062, -0.099), 3, radius=0.005,
             height=0.003, axis='Z', mat=STEEL)

    # Trigger guard: a three-limb loop hung off the receiver's underside.
    strut(p, (0, 0.070, -0.058), (0, 0.075, -0.130), (0.016, 0.011), STEEL)
    strut(p, (0, 0.075, -0.130), (0, 0.121, -0.134), (0.016, 0.011), STEEL)
    strut(p, (0, 0.121, -0.134), (0, 0.128, -0.058), (0.016, 0.011), STEEL)
    p.box((0, 0.100, -0.090), (0.009, 0.013, 0.040), STEEL,
          rot=Matrix.Rotation(math.radians(12), 4, 'X'))
    p.box((0, 0.097, -0.086), (0.012, 0.009, 0.017), BLACK,
          rot=Matrix.Rotation(math.radians(12), 4, 'X'))

    # L-bracket under the drum: a plate against the drum's belly with a flange
    # dropping at its rear, which is what the hoses clamp to.
    hard += p.box((0, -0.194, -0.138), (0.078, 0.088, 0.016), BLUE)
    hard += p.box((0, -0.157, -0.170), (0.078, 0.018, 0.052), BLUE)
    p.rivets((-0.026, -0.226, -0.129), (0.026, -0.226, -0.129), 2,
             radius=0.005, height=0.004, axis='Z', mat=STEEL)

    # Two hoses looping from the bracket back into the receiver's underside,
    # outboard of the roller so nothing intersects.
    sweep(p, catmull([
        (-0.026, -0.152, -0.178), (-0.034, -0.110, -0.218),
        (-0.038, -0.050, -0.228), (-0.038, 0.000, -0.190),
        (-0.036, 0.014, -0.120), (-0.036, 0.014, -0.066),
    ]), 0.010, BLUE)
    sweep(p, catmull([
        (0.030, -0.152, -0.172), (0.040, -0.104, -0.204),
        (0.044, -0.046, -0.212), (0.042, 0.004, -0.176),
        (0.038, 0.016, -0.116), (0.038, 0.016, -0.066),
    ]), 0.009, CORAL)
    for sx in (-1, 1):
        p.cyl((sx * 0.037, 0.016, -0.062), 0.013, 0.014, 'Z', 10, STEEL)

    # Roller under the drum's rear: a small wheel in a two-plate fork.
    for sx in (-1, 1):
        hard += p.box((sx * 0.024, -0.055, -0.150), (0.007, 0.034, 0.044),
                      STEEL)
    p.cyl((0, -0.055, -0.176), 0.028, 0.022, 'X', 16, BLACK)
    p.cyl((0, -0.055, -0.176), 0.015, 0.028, 'X', 12, CHROME)


# --------------------------------------------------------------------------
# The gun, parameterised by what is in the bore
# --------------------------------------------------------------------------

def net_gun(coll, mats, name, loaded, bundle_name=None):
    """One net gun.

    `loaded` decides only whether the net bundle is built; the gun itself is
    the same geometry either way, which is the point of building both
    variations from one function rather than two scripts that drift.
    """
    p = Part(mats)
    hard = []

    canister(p)
    receiver(p, hard)
    optic(p, hard)
    grip(p)
    furniture(p, hard)

    p.bevel(hard, width=BEVEL_W, segments=2)
    body = p.finish(name, coll)

    if loaded:
        bundle(coll, mats, bundle_name)
    return body


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    # The hero: net in the bore. This is the collection that ships.
    hero = collection("Coll_NetGun_Loaded")
    net_gun(hero, mats, "Mesh_NetGun_Body", loaded=True,
            bundle_name="Mesh_NetGun_Bundle")
    # Where the net leaves the bore, and where the firing hand closes. Read on
    # the Unity side to place the muzzle transform and the ItemGrip point; the
    # grip marker sits on the grip's core axis, inside the wrap, because the
    # hand closes *around* the grip and a marker on the cloth holds the gun a
    # centimetre clear of the palm.
    marker(hero, "Marker_Muzzle", (0.0, MUZZLE_Y, 0.0), mats)
    palm = GRIP_TOP + Vector((0.0, math.sin(GRIP_RAKE),
                              -math.cos(GRIP_RAKE))) * 0.062
    marker(hero, "Marker_Grip", tuple(palm), mats)

    # Same gun, empty bore — the spent read, and the reason the mouth is
    # modelled open rather than animated.
    net_gun(collection("Coll_NetGun_Spent"), mats, "Mesh_NetGun_Spent",
            loaded=False)

    report()
    save(out)


main()
