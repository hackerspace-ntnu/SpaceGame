"""Repulsor Gauntlet — the forearm-worn concussive air-blast emitter.

    blender --background --python gauntlet_repulsor.py -- --out gauntlet_repulsor.blend

Replaces the greybox of Unity primitives the repulsor artifact wears today.
Authored against the hardpoint deck of the bracer the player wears permanently
(`components/props/gauntlet_base.blend`'s **Mount** variation, shipped on its
own by `gauntlet_base_export.py`) but containing none of it: a
0.40 m annular emitter coil held out over the back of the hand with its mouth
toward the fingers, a capacitor bank of three fat drums lying along the arm
behind it, a 0.28 m glass capacitor ball seated in a cradle on top of the
bank, and two heavy conduits looping round the bank's flanks into the coil.
Bulky, few parts, no greebles — the base's own look.

Authored at DOUBLE the first cut's device size (2026-09-03): worn on the
astronaut the first version read as a wristwatch. The numbers below are
re-derived at the new size rather than scaled, so embeds stay 2-4 mm and
bevels stay crisp — a doubled 3 mm embed is a part floating 6 mm inside its
neighbour, and a doubled bevel turns a machined edge into soap.

| Object | What it is |
|---|---|
| `Mesh_GauntletBase_*_Mount`       | the base, appended unchanged |
| `Mesh_Repulsor_Bracket`           | the deck plate: a sunk core inside the deck footprint and a wide table above the deck plane that the bank sits on |
| `Mesh_Repulsor_Throat`            | the housing between the bank and the coil; the drums plug into its back, the coil hangs off its front |
| `Mesh_Repulsor_Cover`             | the throat's orange top plate — the one suit-armour accent |
| `Mesh_Repulsor_Ring`              | the emitter coil: a thick annulus, axis along Y, rims rounded |
| `Mesh_Repulsor_Stripe`            | red arming stripe round the coil |
| `Mesh_Repulsor_Backplate`         | disc closing the rear of the annulus (the diaphragm) |
| `Mesh_Repulsor_Vanes`             | four radial vanes and a hub across the mouth |
| `Mesh_Repulsor_Glow`              | amber inner ring 4 mm inside the mouth |
| `Mesh_Repulsor_CapLeft/Mid/Right` | the three capacitors, brass rear caps and studs |
| `Mesh_Repulsor_Strap`             | clamp strap over the bank's elbow end |
| `Mesh_Repulsor_BusBar`            | brass bar joining the three rear studs |
| `Mesh_Repulsor_Cradle`            | pedestal and brass collar the capacitor ball sits in |
| `Mesh_Repulsor_Capacitor`         | the glass ball — its own object, origin at its own centre |
| `Mesh_Repulsor_ConduitLeft/Right` | bent pipes from the outer capacitors into the coil |
| `Marker_Emitter`                  | empty at the mouth centre — the blast origin |
| `Marker_Grip`                     | empty at the wrist joint, the family origin |

Every logical part is its own object, per the skill's geometry rules;
fasteners live inside the part they fasten. The first cut's separate `Foot`
plate is gone: at this size the foot and the bracket were the same slab on
the same deck, so they are one stepped part.


## The frame

**Arm along +Y, wrist joint at y = 0, elbow toward +Y, forward −Y, dorsal +Z,
thumb +X on a right forearm** — `_gauntlet.py`'s frame, at true suit scale,
origin at the wrist bone. `_exportlib` maps Blender `(x, y, z)` onto Unity
`(−x, z, −y)`, so both empties are left at identity rotation: Unity's +Z on
`Marker_Emitter` is this model's −Y, out of the coil past the hand.


## Where things sit, and why

The device grows UP and FORWARD, never past the elbow — the arm has to fold.
Its envelope: y −0.176..0.358 (limits −0.24 and 0.36), z 0.212..0.630 (limit
0.64), |x| ≤ 0.198 (limit 0.21, and 0.20 forward of the wrist).

The coil is centred at (0, 0.362 mid-depth, 0.410) with a 0.396 m outer
diameter, standing out over the back of the hand at y −0.176..−0.080. Its
height is forced from below: forward of the wrist nothing may drop under
z 0.20, and a 0.198 m radius hung on a centre lower than 0.398 does. At 0.410
the coil's bottom is 0.212 and its top 0.608, both inside the envelope, and
the mouth clears the knuckles (0.19 m from the bone) by a comfortable margin.

The bank sits on the deck but reaches forward off it: the drums are 0.104
across and run y 0.020..0.320, so the front 80 mm cantilevers over the wrist
at z 0.268 — above the deck plane, so nothing of it is under the deck
outside its footprint. The bracket is stepped for the same reason: its sunk
core stays inside the deck (x ±0.066, y 0.104..0.316) and only the table
above z 0.256 is wide enough for the bank.

The ball is 0.28 m across, centred at (0, 0.190, 0.490). Its height is forced
from above and below at once: the ceiling is z 0.64 and the floor is the
bank, and 0.490 is the one band where it clears the two OUTER drums (5 mm)
while nesting 22 mm into the middle one — the socket the cradle dresses.
Sitting it clear of all three would put its crown at 0.68.


## Unity wiring

`Mesh_Repulsor_Capacitor` is one separate object with its origin at its own
centre; the prefab swaps its material for an additive glow and scales/toggles
it. `Marker_Emitter` is the blast origin. Nothing else is read by name.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))
sys.path.insert(0, _HERE)

import bmesh  # noqa: E402
import bpy  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from _gauntlet import BASE_DECK_Z, BASE_DECK_Y0, BASE_DECK_Y1  # noqa: E402
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

# Index 0 is a structural metal because `bmesh.ops.bevel` stamps every face it
# creates with material index 0 — see `_tracked`.
STEEL, DARK, BRASS, CHROME, ORANGE, WARN, AMBER = range(7)
MATS = ["Mat_Metal_Steel_Worn",        # bracket, capacitors, conduits
        "Mat_Metal_Steel_Dark",        # housings: throat, coil, vanes, cradle
        "Mat_Metal_Brass_Tarnished",   # capacitor caps, studs, bus bar, collars
        "Mat_Metal_Chrome_Scuffed",    # bolt heads
        "Mat_Paint_Safety_Orange",     # the one accent: the throat cover
        "Mat_Paint_Warn_Red",          # arming stripe round the coil
        "Mat_Emissive_Amber"]          # the glow ring and the capacitor ball

# Bevels do NOT scale with the device: an edge chamfer is a machining
# allowance, not a proportion, and doubling it is what makes a big part look
# inflated. 4 mm on plate, 10 mm on the coil's rims.
BEVEL_W = 0.004
EMBED = 0.004                        # standard part-into-part overlap
SINK = 0.004                         # how far the bracket sinks into the deck

# ── The emitter coil ────────────────────────────────────────────────────────
# Centre height is bounded below by the forward floor (z 0.20) and above by
# the fold ceiling (z 0.64): RING_Z ∈ [0.398, 0.442] for this radius.
RING_Z = 0.410
RING_Y0, RING_Y1 = -0.176, -0.080    # mouth face .. rear face
RING_RO, RING_RI = 0.194, 0.130      # housing outer / bore
RING_SEG = 28
STRIPE_RO, STRIPE_T, STRIPE_D = 0.198, 0.008, 0.028
GLOW_Y0, GLOW_D, GLOW_T = RING_Y0 + EMBED, 0.012, 0.012
VANE_Y, VANE_D, VANE_T = RING_Y0 + 0.036, 0.024, 0.010
HUB_R, HUB_D = 0.028, 0.032
BACKPLATE_Y0, BACKPLATE_Y1, BACKPLATE_R = -0.092, -0.076, RING_RI + EMBED
EMITTER = (0.0, RING_Y0, RING_Z)

# ── The throat ──────────────────────────────────────────────────────────────
# Front face buried inside the backplate's thickness, back swallowing the
# drums' front ends. Its floor is well clear of the forward z 0.20 line.
THROAT_HX, THROAT_Y0, THROAT_Y1 = 0.088, -0.086, 0.044
THROAT_Z0, THROAT_Z1 = 0.300, 0.470
COVER_HX, COVER_Y0, COVER_Y1 = 0.072, -0.060, 0.020

# ── The deck plate ──────────────────────────────────────────────────────────
# Stepped: the sunk core is inset 4 mm inside the deck's own footprint so no
# face of the two is coplanar and nothing drops below z 0.250 off the deck;
# the table is as wide as the bank and stays above the deck plane.
CORE_HX, CORE_Y0, CORE_Y1 = 0.066, BASE_DECK_Y0 + 0.004, BASE_DECK_Y1 - 0.004
CORE_Z0, CORE_Z1 = BASE_DECK_Z - SINK, 0.262
TABLE_HX, TABLE_Y0, TABLE_Y1 = 0.150, 0.116, 0.304
TABLE_Z0, TABLE_Z1 = 0.256, 0.272

# ── The capacitor bank ──────────────────────────────────────────────────────
CAP_R, CAP_X = 0.052, 0.100          # 4 mm of overlap between neighbours
CAP_Y0, CAP_Y1 = 0.020, 0.320
CAP_Z = TABLE_Z1 + CAP_R - EMBED     # drums sunk 4 mm into the table
CAP_SEG = 20
ENDCAP_R, ENDCAP_Y0, ENDCAP_Y1 = 0.044, 0.312, 0.332
STUD_R, STUD_Y0, STUD_Y1 = 0.010, 0.330, 0.352
STRAP_HX, STRAP_Y0, STRAP_Y1, STRAP_Z0, STRAP_Z1 = 0.148, 0.290, 0.314, 0.358, 0.386
BUS_HX, BUS_Y0, BUS_Y1, BUS_Z0, BUS_Z1 = 0.116, 0.342, 0.358, 0.302, 0.330

# ── The capacitor ball and its cradle ───────────────────────────────────────
BALL = Vector((0.0, 0.190, 0.490))
BALL_R = 0.140                       # 0.28 m diameter
CRADLE_HX, CRADLE_Y0, CRADLE_Y1 = 0.046, 0.110, 0.270
CRADLE_BLOCK_Z0, CRADLE_BLOCK_Z1 = 0.354, 0.418
CRADLE_RING_Z, CRADLE_RING_MINOR = 0.418, 0.016

# ── Conduits ────────────────────────────────────────────────────────────────
CONDUIT_R = 0.018
CONDUIT_X = 0.172                    # outboard run, clear of both the throat and the ball
CONDUIT_Z = 0.404
CONDUIT_ENTRY_R = 0.156              # where they pierce the coil, inside its wall band


def _ball_radius_at(z):
    """Radius of the ball's section at height z — where a collar meets it."""
    dz = z - BALL.z
    return math.sqrt(BALL_R * BALL_R - dz * dz)


def _sphere(p, centre, radius, mat, u=24, v=14):
    """A UV sphere absorbed into `p`. `_buildlib` has no sphere primitive; this
    goes through `_tag` so `TrackedPart` logs it like any other face set."""
    res = bmesh.ops.create_uvsphere(p.bm, u_segments=u, v_segments=v,
                                    radius=radius,
                                    matrix=Matrix.Translation(Vector(centre)))
    faces = list({f for vtx in res["verts"] for f in vtx.link_faces})
    p._tag(faces, mat)
    return p.shade(faces)


# ---------------------------------------------------------------------------
# Parts
# ---------------------------------------------------------------------------

def bracket(coll, mats):
    """The deck plate, in two steps.

    The core sinks 4 mm into the deck and swallows all four bolt bosses; it is
    inset 4 mm inside the deck's own outline so no pair of faces is coplanar.
    The table is as wide as the bank — the drums reach x ±0.152 and the deck is
    only 0.140 across — and its underside is held at z 0.256, above the deck
    plane, because nothing may drop below that plane off the deck's footprint.
    """
    p = TrackedPart(mats)
    hard = p.slab((-CORE_HX, CORE_Y0, CORE_Z0), (CORE_HX, CORE_Y1, CORE_Z1), STEEL)
    hard += p.slab((-TABLE_HX, TABLE_Y0, TABLE_Z0),
                   (TABLE_HX, TABLE_Y1, TABLE_Z1), STEEL)
    p.restamp("bracket")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_Repulsor_Bracket", coll)


def throat(coll, mats):
    """The housing the coil hangs off and the drums plug into.

    Its front face is inside the backplate's thickness — not on it: the two
    would otherwise share the plane at y −0.092, in plain view down the bore.
    Its back overlaps the drums' front ends by 24 mm.
    """
    p = TrackedPart(mats)
    hard = p.slab((-THROAT_HX, THROAT_Y0, THROAT_Z0),
                  (THROAT_HX, THROAT_Y1, THROAT_Z1), DARK)
    # Bolt heads on both flanks.
    for sx in (-1, 1):
        for y in (-0.040, 0.010):
            p.cyl((sx * (THROAT_HX + 0.001), y, 0.380), 0.008, 0.008, 'X',
                  8, CHROME)
    p.restamp("throat")
    p.bevel(hard, width=0.006, segments=2)
    return p.finish("Mesh_Repulsor_Throat", coll)


def cover(coll, mats):
    """Orange top plate on the throat, 4 mm sunk, 8 mm proud."""
    p = TrackedPart(mats)
    hard = p.slab((-COVER_HX, COVER_Y0, THROAT_Z1 - EMBED),
                  (COVER_HX, COVER_Y1, THROAT_Z1 + 0.008), ORANGE)
    p.restamp("cover")
    p.bevel(hard, width=0.003, segments=1)
    for f in p.bm.faces:             # orange through and through
        f.material_index = ORANGE
    return p.finish("Mesh_Repulsor_Cover", coll)


def ring(coll, mats):
    """The coil: a thick annulus along Y with rounded rims.

    A `tube` rather than a `torus`: at 0.40 m across, a round-section torus
    reads as a tyre. The bevelled annulus reads as a coil housing.
    """
    p = TrackedPart(mats)
    depth = RING_Y1 - RING_Y0
    p.tube((0.0, (RING_Y0 + RING_Y1) / 2.0, RING_Z), RING_RO, RING_RO - RING_RI,
           depth, 'Y', RING_SEG, DARK)
    p.restamp("ring")
    p.bevel(width=0.010, segments=2)
    for f in p.bm.faces:             # the bevel's new rim faces are the housing too
        f.material_index = DARK
    return p.finish("Mesh_Repulsor_Ring", coll)


def stripe(coll, mats):
    """Red arming band round the coil, 4 mm into the housing, 4 mm proud."""
    p = TrackedPart(mats)
    p.tube((0.0, (RING_Y0 + RING_Y1) / 2.0, RING_Z), STRIPE_RO, STRIPE_T,
           STRIPE_D, 'Y', RING_SEG, WARN)
    p.restamp("stripe")
    return p.finish("Mesh_Repulsor_Stripe", coll)


def backplate(coll, mats):
    """Disc closing the rear of the annulus: 4 mm into the bore wall, standing
    4 mm proud of the coil's rear face so the throat can bury its front in it."""
    p = TrackedPart(mats)
    p.cyl((0.0, (BACKPLATE_Y0 + BACKPLATE_Y1) / 2.0, RING_Z), BACKPLATE_R,
          BACKPLATE_Y1 - BACKPLATE_Y0, 'Y', RING_SEG, DARK)
    p.restamp("backplate")
    return p.finish("Mesh_Repulsor_Backplate", coll)


def vanes(coll, mats):
    """Four radial vanes in an X across the mouth, rooted 4 mm in the bore
    wall, meeting a hub. `R_y(a)` sends a box's local +X to
    (cos a, 0, −sin a); the vane's centre is placed along that same vector so
    the rotation and the position cannot disagree."""
    p = TrackedPart(mats)
    r_in, r_out = HUB_R - 0.008, RING_RI + EMBED
    length = r_out - r_in
    for i in range(4):
        rot = Matrix.Rotation(math.radians(45.0 + 90.0 * i), 4, 'Y')
        d = rot.to_3x3() @ Vector((1.0, 0.0, 0.0))
        c = Vector((0.0, VANE_Y, RING_Z)) + d * (r_in + length / 2.0)
        p.box(c, (length, VANE_D, VANE_T), DARK, rot=rot)
    p.cyl((0.0, VANE_Y, RING_Z), HUB_R, HUB_D, 'Y', 12, DARK)
    p.restamp("vanes")
    return p.finish("Mesh_Repulsor_Vanes", coll)


def glow(coll, mats):
    """Amber ring 4 mm inside the mouth, 4 mm into the bore wall."""
    p = TrackedPart(mats)
    p.tube((0.0, GLOW_Y0 + GLOW_D / 2.0, RING_Z), RING_RI + EMBED, GLOW_T,
           GLOW_D, 'Y', RING_SEG, AMBER)
    p.restamp("glow")
    return p.finish("Mesh_Repulsor_Glow", coll)


def capacitor(coll, mats, name, x):
    """One fat drum along Y with a brass rear cap and terminal stud. The front
    end is inside the throat, so it carries no cap."""
    p = TrackedPart(mats)
    yc = (CAP_Y0 + CAP_Y1) / 2.0
    p.cyl((x, yc, CAP_Z), CAP_R, CAP_Y1 - CAP_Y0, 'Y', CAP_SEG, STEEL)
    p.cyl((x, (ENDCAP_Y0 + ENDCAP_Y1) / 2.0, CAP_Z), ENDCAP_R,
          ENDCAP_Y1 - ENDCAP_Y0, 'Y', CAP_SEG, BRASS)
    p.cyl((x, (STUD_Y0 + STUD_Y1) / 2.0, CAP_Z), STUD_R, STUD_Y1 - STUD_Y0,
          'Y', 8, BRASS)
    p.restamp(name)
    return p.finish(name, coll)


def strap(coll, mats):
    """Clamp strap across the three drums at the elbow end. Held forward of the
    ball's waterline: at y 0.290 the ball's underside is still 20 mm above it."""
    p = TrackedPart(mats)
    hard = p.slab((-STRAP_HX, STRAP_Y0, STRAP_Z0),
                  (STRAP_HX, STRAP_Y1, STRAP_Z1), DARK)
    p.restamp("strap")
    p.bevel(hard, width=0.003, segments=1)
    return p.finish("Mesh_Repulsor_Strap", coll)


def bus_bar(coll, mats):
    """Brass bar joining the three rear studs — the device's last part along
    the arm, at y 0.358, inside the fold limit."""
    p = TrackedPart(mats)
    p.slab((-BUS_HX, BUS_Y0, BUS_Z0), (BUS_HX, BUS_Y1, BUS_Z1), BRASS)
    p.restamp("busbar")
    for f in p.bm.faces:
        f.material_index = BRASS
    return p.finish("Mesh_Repulsor_BusBar", coll)


def cradle(coll, mats):
    """A dark pedestal standing on the middle drum and a brass collar on it.

    The collar's major radius is the ball's own section radius at that height,
    so half its tube is inside the glass and the seam where the ball enters
    its socket is covered.
    """
    p = TrackedPart(mats)
    p.slab((-CRADLE_HX, CRADLE_Y0, CRADLE_BLOCK_Z0),
           (CRADLE_HX, CRADLE_Y1, CRADLE_BLOCK_Z1), DARK)
    p.torus((BALL.x, BALL.y, CRADLE_RING_Z), _ball_radius_at(CRADLE_RING_Z),
            CRADLE_RING_MINOR, 'Z', 28, 8, BRASS)
    p.restamp("cradle")
    return p.finish("Mesh_Repulsor_Cradle", coll)


def capacitor_ball(coll, mats):
    """The glass ball, 0.28 m across. One object, origin at its own centre, so
    Unity can scale and toggle it about the middle."""
    p = TrackedPart(mats)
    _sphere(p, BALL, BALL_R, AMBER)
    p.restamp("capacitor")
    return p.finish("Mesh_Repulsor_Capacitor", coll, origin=tuple(BALL))


def conduit(coll, mats, name, sx):
    """A bent pipe from the outer drum's top, out round the bank's flank, then
    forward past the ball and into the coil's rear rim.

    Both ends are buried, so it reads as a run between two things rather than a
    stub. The outboard leg is at x ±0.172: outside the ball's 0.140 equator and
    outside the throat's ±0.088, so it fouls neither, and its outer surface
    still lands inside the forward |x| ≤ 0.20 line.
    """
    p = TrackedPart(mats)
    pts = [(sx * CAP_X, 0.150, CAP_Z + 0.030),
           (sx * CONDUIT_X, 0.120, CONDUIT_Z),
           (sx * CONDUIT_X, -0.030, CONDUIT_Z),
           (sx * CONDUIT_ENTRY_R, -0.100, RING_Z)]
    tube_path(p, pts, CONDUIT_R, STEEL, seg=10)
    # Brass collars where the pipe leaves the drum and enters the coil.
    a, b = Vector(pts[0]), Vector(pts[1])
    d = (b - a).normalized()
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    p.cyl(a + d * 0.044, CONDUIT_R + 0.006, 0.028, 'Z', 10, BRASS, rot=rot)
    a, b = Vector(pts[2]), Vector(pts[3])
    d = (b - a).normalized()
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    p.cyl(b - d * 0.048, CONDUIT_R + 0.006, 0.028, 'Z', 10, BRASS, rot=rot)
    p.restamp(name)
    return p.finish(name, coll)


def marker(coll, name, at):
    """A socket empty. Identity rotation on purpose — see the module docstring."""
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = 'ARROWS'
    obj.empty_display_size = 0.05
    obj.location = Vector(at)
    coll.objects.link(obj)
    return obj


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_GauntletRepulsor")

    bracket(coll, mats)
    throat(coll, mats)
    cover(coll, mats)
    ring(coll, mats)
    stripe(coll, mats)
    backplate(coll, mats)
    vanes(coll, mats)
    glow(coll, mats)
    for name, x in (("Mesh_Repulsor_CapLeft", -CAP_X),
                    ("Mesh_Repulsor_CapMid", 0.0),
                    ("Mesh_Repulsor_CapRight", CAP_X)):
        capacitor(coll, mats, name, x)
    strap(coll, mats)
    bus_bar(coll, mats)
    cradle(coll, mats)
    capacitor_ball(coll, mats)
    conduit(coll, mats, "Mesh_Repulsor_ConduitLeft", -1)
    conduit(coll, mats, "Mesh_Repulsor_ConduitRight", 1)
    marker(coll, "Marker_Emitter", EMITTER)
    marker(coll, "Marker_Grip", (0.0, 0.0, 0.0))

    save(out)
    report()

    device = [o for o in bpy.data.objects
              if o.type == 'MESH' and o.name.startswith("Mesh_Repulsor_")]
    print("  DEVICE TRIS: %d" % sum(tri_count(o) for o in device))
    lo = [min((v.co[i] + o.location[i]) for o in device for v in o.data.vertices)
          for i in range(3)]
    hi = [max((v.co[i] + o.location[i]) for o in device for v in o.data.vertices)
          for i in range(3)]
    print("  DEVICE BOUNDS blender (%.3f, %.3f, %.3f)..(%.3f, %.3f, %.3f)"
          % (*lo, *hi))
    for o in sorted(bpy.data.objects, key=lambda o: o.name):
        if o.type == 'EMPTY':
            print("  %-30s origin (%.3f, %.3f, %.3f)  EMPTY" % (o.name, *o.location))
        elif o.name.startswith("Mesh_Repulsor_"):
            a = [min(v.co[i] for v in o.data.vertices) + o.location[i] for i in range(3)]
            b = [max(v.co[i] for v in o.data.vertices) + o.location[i] for i in range(3)]
            print("  %-30s origin (%.3f, %.3f, %.3f)  bounds (%.3f, %.3f, %.3f)..(%.3f, %.3f, %.3f)"
                  % (o.name, *o.location, *a, *b))


if __name__ == "__main__":
    main()
