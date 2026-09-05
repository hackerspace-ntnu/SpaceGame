"""Gauntlet Leash — the tether launcher worn on the forearm.

    blender --background --python gauntlet_leash.py -- --out gauntlet_leash.blend

The leash artifact's worn device, rebuilt on the gauntlet base. The previous
`leash_gauntlet.py` was rejected for reading as "all metal brace": the brace
is now the base's job, and everything authored here is ROPE GEAR — a big
spool of wound hemp is the hero mass, with a brake lever on one side, a
ratchet pawl on the other, a fairlead the rope leaves through toward the
hand, and a snap hook hanging out over the hand on a short lead.

| Object | What it is |
|---|---|
| `Mesh_GauntletBase_*_Mount` | `components/props/gauntlet_base.blend`, Mount variation, unchanged |
| `Mesh_Leash_Cradle`   | base plate on the deck, two bearing feet, two stadium uprights |
| `Mesh_Leash_Spool`    | the two flanges, dark steel with safety-orange rims |
| `Mesh_Leash_Winding`  | the wound hemp — a corrugated loft, one ridge per turn |
| `Mesh_Leash_Axle`     | chrome axle through both uprights |
| `Mesh_Leash_Ratchet`  | ratchet wheel and hex boss on the little-finger end of the axle |
| `Mesh_Leash_Pawl`     | the pawl on its pin, bearing on the ratchet |
| `Mesh_Leash_Lever`    | brake hub, nut, arm and rubber grip on the thumb end |
| `Mesh_Leash_Fairlead` | pedestal on the plate, a bracket reaching forward, the steel eye |
| `Mesh_Leash_RopeLead` | the rope from the winding, through the eye, out to the hook |
| `Mesh_Leash_Hook`     | crimp ferrule, eye, shank, curve, tip and gate |
| `muzzle`              | empty at the eye's centre — `LeashArtifact` pays the rope out from it |

Every logical part is its own object; parts embed 2-4 mm into their
neighbours and share no plane. Only fasteners live inside the part they
fasten (the pawl's pin, the lever's nut, the hook's gate).


## Frame

The gauntlet family frame (`_gauntlet.py`): arm along +Y, wrist at y = 0,
elbow +Y, forward −Y, dorsal +Z, **+X the thumb side of a right forearm**.
The export maps Blender (x, y, z) onto Unity (−x, z, −y); the left arm wears
the same model at a negative X scale. Origin at the wrist bone, true suit
scale, worn at 1.0.


## Sized to be seen

Built at twice the first cut's linear size, on the note that the gauntlet
items read too small: "quite visible; do not be afraid of size". The numbers
below are re-derived rather than scaled, because three things must NOT double
— the embeds (2-4 mm, not 8), the bevels (`BEVEL_W` stays 3 mm or the
chamfers go soft) and the rope's gauge. The winding keeps its 12 mm turn
pitch and grows from ten turns to twenty, so it stays a rope rather than
becoming a hawser.

The growth goes UP and FORWARD over the back of the hand, never past the
elbow. Envelope honoured: forward to y = −0.24 while z ≥ 0.20 and |x| ≤ 0.20;
elbow y ≤ 0.36; height z ≤ 0.64; width |x| ≤ 0.21.


## Where everything sits

The base's deck is the plane z = 0.250, x ±0.070, y 0.100..0.320, and only
the cradle plate touches it — sunk 3 mm, inside the deck's 10 mm edge bevel
at x ±0.060, y 0.112..0.292. Everything else stands on the plate.

The spool axle runs across the arm at y 0.206, z 0.430. Flanges of radius
0.148 put the top of the spool at z 0.578 (limit 0.64) and its back edge at
y 0.354 (limit 0.36); the bearing feet reach x ±0.192 and the lever's nut
x 0.209 (limit 0.21). The winding's valley radius is 0.1205 and its ridge
0.124 — a 3.5 mm bulge per turn, twenty turns of 12 mm pitch across
x ±0.120 (twenty 6 mm half-steps land exactly on the flanges).

The spool overhangs the deck's front edge, so the fairlead cannot stand
under it: instead the pedestal on the plate's front carries a bracket
reaching forward over the hand to the eye at (0, 0.020, 0.330), clear of the
drum. The rope leaves the winding at the tangent point toward that eye —
solved from the geometry, not placed by eye, so it neither cuts the drum nor
floats off it — and runs on to the hook's ferrule.

The hook hangs at 35 degrees below the horizontal out over the hand, its
mouth opening to the thumb side so it reads from above; measured after the
build it spans y −0.162..−0.014 at z 0.208..0.322 — well forward of the
wrist, above z 0.20 and inside |x| 0.20, as the relaxed envelope allows.

The brake lever is on the thumb side (+X), leaning back toward the elbow at
35 degrees so the wearer's other hand reaches it; the ratchet and its pawl
are on the little-finger side (−X), the side that faces outward.


## Unity

`LeashArtifact.muzzle` is the `muzzle` empty. It has identity rotation: after
export its +Z is the model's −Y, forward through the eye toward the hand.
`GauntletFit` seats the model at scale 1 on the forearm bone.

Generation script — historical record. The .blend is the source of truth;
never re-run this over the file it produced.
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
from _gauntlet import BASE_DECK_Z, seat  # noqa: E402
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

# Index 0 is a structural metal: `bmesh.ops.bevel` stamps new faces with 0.
DARK, STEEL, CHROME, ORANGE, HEMP, RUBBER = range(6)
MATS = ["Mat_Metal_Steel_Dark",       # cradle, flanges, hook, lever arm, boss
        "Mat_Metal_Steel_Worn",       # eye, ratchet, pawl, hub
        "Mat_Metal_Chrome_Scuffed",   # axle, pins, nut
        "Mat_Paint_Safety_Orange",    # the flange rims — the one accent
        "Mat_Fabric_Rope_Hemp",       # winding and rope lead
        "Mat_Plastic_Rubber_Black"]   # lever grip

# Bevels and embeds are the two families of number that do NOT scale with the
# device: a 6 mm chamfer reads as a melted edge and an 8 mm embed as a part
# sunk into its neighbour.
BEVEL_W = 0.003
SINK = 0.003                          # how far the cradle plate goes into the deck

# ── Spool ────────────────────────────────────────────────────────────────────
SPOOL_Y = 0.206
FLANGE_R = 0.148
AXLE_Z = BASE_DECK_Z + FLANGE_R + 0.032          # 0.430; spool top 0.578
FLANGE_X0, FLANGE_X1 = 0.116, 0.144              # inner and outer face, |x|
WIND_X = 0.120                                   # winding half-width, 4 mm into each flange
WIND_R_VALLEY, WIND_R_RIDGE = 0.1205, 0.124
ROPE_PITCH = 0.012                               # one turn of 12 mm rope — deliberately not doubled
ROPE_R = 0.006
AXLE_R = 0.022
AXLE_X0, AXLE_X1 = -0.200, 0.192                 # into the boss and the hub

# ── Cradle ───────────────────────────────────────────────────────────────────
# The plate is the one part that touches the deck, and the deck's 10 mm edge
# bevel is why it stops at x ±0.060 rather than following the device's width:
# a plate over the bevel either floats at the corners or pokes through it.
PLATE = ((-0.060, 0.112, BASE_DECK_Z - SINK), (0.060, 0.292, 0.273))
FOOT_X0, FOOT_X1 = 0.048, 0.192                  # bearing feet, over the deck edge but above its plane
FOOT_Z0, FOOT_Z1 = 0.270, 0.280                  # 3 mm into the plate, 2 mm under the flanges
UP_X0, UP_X1 = 0.148, 0.176                      # uprights, 4 mm clear of the flanges
UP_Y0, UP_Y1 = SPOOL_Y - 0.072, SPOOL_Y + 0.072
UP_Z0 = FOOT_Z1 - 0.003
UP_CAP_R = (UP_Y1 - UP_Y0) / 2.0                 # stadium top about the axle

# ── Ratchet, little-finger side (−X) ─────────────────────────────────────────
RATCHET_X, RATCHET_D, RATCHET_R = -0.183, 0.018, 0.040   # 2 mm into the upright
BOSS_X, BOSS_D, BOSS_R = -0.199, 0.018, 0.034            # hex, 2 mm into the wheel
PAWL_PIN = (RATCHET_X, SPOOL_Y + 0.056, AXLE_Z + 0.040)  # inside the upright's stadium
PAWL_W, PAWL_T = 0.020, 0.016

# ── Brake lever, thumb side (+X) ─────────────────────────────────────────────
HUB_X, HUB_D, HUB_R = 0.186, 0.024, 0.040        # 2 mm into the upright
NUT_X, NUT_D, NUT_R = 0.203, 0.012, 0.016
ARM_X, ARM_T, ARM_W, ARM_LEN = 0.186, 0.020, 0.028, 0.130
ARM_ANGLE = 35.0                                 # degrees above +Y, toward the elbow
GRIP_R, GRIP_LEN, GRIP_AT = 0.022, 0.048, 0.134

# ── Fairlead ─────────────────────────────────────────────────────────────────
# Pedestal on the plate, then a bracket reaching FORWARD to the eye: the spool
# overhangs the deck's front edge, so an eye standing straight up off the
# plate would be inside the drum.
FAIRLEAD_FOOT = ((-0.046, 0.114, 0.270), (0.046, 0.150, 0.298))
FAIRLEAD_NECK = ((-0.020, 0.026, 0.272), (0.020, 0.144, 0.300))
EYE = Vector((0.0, 0.020, 0.330))
EYE_MAJOR, EYE_MINOR = 0.042, 0.018

# ── Hook ─────────────────────────────────────────────────────────────────────
HOOK_EYE = Vector((0.0, -0.070, 0.276))          # the hook's eye, world
HOOK_ANGLE = 35.0                                # degrees below horizontal, pointing −Y
HOOK_WIRE = 0.011
HOOK_CURVE_R = 0.028


def hook_frame():
    """Hook local → world. Local +Y is the shank direction (down-forward),
    local +X stays world +X (the mouth opens to the thumb side), local +Z is
    the hook plane's normal. Same construction as `_buildlib.seam`."""
    a = math.radians(HOOK_ANGLE)
    d = Vector((0.0, -math.cos(a), -math.sin(a)))
    x = Vector((1.0, 0.0, 0.0))
    n = x.cross(d).normalized()
    return Matrix((x, d, n)).transposed().to_4x4()


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def stadium(y0, y1, z0, zc, steps=16):
    """A (y, z) profile: rectangle from z0 up to zc, closed by a semicircle
    of radius (y1 − y0)/2 about (yc, zc). Counter-clockwise."""
    yc, r = (y0 + y1) / 2.0, (y1 - y0) / 2.0
    pts = [(y0, z0), (y1, z0)]
    for i in range(steps + 1):
        a = math.pi * i / steps
        pts.append((yc + r * math.cos(a), zc + r * math.sin(a)))
    return pts


def smooth_curved(p, faces):
    """Smooth-shade the faces of a prism that are neither its caps nor
    axis-aligned — the arc of a stadium, and nothing else."""
    p.bm.normal_update()
    for f in faces:
        n = f.normal
        if max(abs(n.x), abs(n.y), abs(n.z)) < 0.999:
            f.smooth = True


def arc_tube(p, centre, radius, r_tube, a0, a1, mat, steps=8, seg=8):
    """A partial torus in the local XY plane, from angle a0 to a1 (degrees),
    capped at both ends. `_buildlib` has a full torus and a straight tube
    but no bent one; a chain of cylinders through the arc leaves notches on
    the outside of every bend, which on a hook reads as damage."""
    bm2 = bmesh.new()
    rings = []
    for i in range(steps + 1):
        a = math.radians(a0 + (a1 - a0) * i / steps)
        ca, sa = math.cos(a), math.sin(a)
        ring = []
        for j in range(seg):
            b = 2 * math.pi * j / seg
            rr = radius + r_tube * math.cos(b)
            ring.append(bm2.verts.new((centre[0] + rr * ca, centre[1] + rr * sa,
                                       centre[2] + r_tube * math.sin(b))))
        rings.append(ring)
    for r0, r1 in zip(rings, rings[1:]):
        for j in range(seg):
            k = (j + 1) % seg
            bm2.faces.new((r0[j], r0[k], r1[k], r1[j]))
    bm2.faces.new(rings[0])
    bm2.faces.new(list(reversed(rings[-1])))
    faces = p._absorb(bm2, mat)      # TrackedPart's, so the stamp is by identity
    for f in faces:
        f.smooth = len(f.verts) == 4
    return faces


def rope_tangent_point():
    """Where the rope leaves the winding on its way to the eye: the lower
    tangent from the eye's centre to the circle of radius WIND_R_LEAVE about
    the axle, in the (y, z) plane.

    The eye must lie outside that circle for a tangent to exist at all — the
    reason the fairlead reaches forward rather than standing under the drum.
    """
    c = Vector((SPOOL_Y, AXLE_Z))
    f = Vector((EYE.y, EYE.z))
    r = (WIND_R_VALLEY + WIND_R_RIDGE) / 2.0
    cf = f - c
    if cf.length <= r:
        raise SystemExit("The fairlead eye is inside the winding: no tangent")
    base = math.atan2(cf.y, cf.x)
    spread = math.acos(r / cf.length)
    a = base + spread                # the lower of the two tangent points
    return Vector((0.0, c.x + r * math.cos(a), c.y + r * math.sin(a))), a


# ---------------------------------------------------------------------------
# Parts
# ---------------------------------------------------------------------------

def cradle(coll, mats):
    """Base plate sunk into the deck, a bearing foot under each upright that
    overhangs the deck's side above its plane, and two stadium uprights whose
    semicircle is centred on the axle."""
    p = TrackedPart(mats)
    hard = p.slab(*PLATE, DARK)
    for sx in (-1, 1):
        hard += p.slab((sx * FOOT_X0, UP_Y0 - 0.008, FOOT_Z0),
                       (sx * FOOT_X1, UP_Y1 + 0.008, FOOT_Z1), DARK)
        faces = p.prism(stadium(UP_Y0, UP_Y1, UP_Z0, AXLE_Z), UP_X1 - UP_X0,
                        axis='X', mat=DARK,
                        offset=(sx * (UP_X0 + UP_X1) / 2.0, 0.0, 0.0))
        smooth_curved(p, faces)
    p.restamp("cradle")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_Leash_Cradle", coll)


def spool(coll, mats):
    """Two flanges. Dark steel discs whose barrel — the rim seen edge-on from
    above — is the device's one orange accent."""
    p = TrackedPart(mats)
    for sx in (-1, 1):
        faces = p.cyl((sx * (FLANGE_X0 + FLANGE_X1) / 2.0, SPOOL_Y, AXLE_Z),
                      FLANGE_R, FLANGE_X1 - FLANGE_X0, 'X', 24, DARK)
        p.bm.normal_update()
        p._tag([f for f in faces if abs(f.normal.x) < 0.9], ORANGE)
    p.restamp("spool")
    return p.finish("Mesh_Leash_Spool", coll)


def winding(coll, mats):
    """The wound rope: a loft along the axle whose radius alternates between
    valley and ridge every half pitch, so each turn is a smooth-shaded bulge.

    The pitch is a rope's, not the device's — doubling the model added turns
    (ten to twenty) rather than fattening them into a hawser. Solid and
    capped; the caps are 4 mm inside the flanges.
    """
    p = TrackedPart(mats)
    n_turns = int(round(2 * WIND_X / ROPE_PITCH))
    sections = []
    for i in range(2 * n_turns + 1):
        x = -WIND_X + i * ROPE_PITCH / 2.0
        r = WIND_R_VALLEY if i % 2 == 0 else WIND_R_RIDGE
        prof = [(SPOOL_Y + r * math.cos(2 * math.pi * k / 20),
                 AXLE_Z + r * math.sin(2 * math.pi * k / 20)) for k in range(20)]
        sections.append((x, prof))
    p.loft(sections, axis='X', mat=HEMP, cap=True)
    p.restamp("winding")
    return p.finish("Mesh_Leash_Winding", coll)


def axle(coll, mats):
    p = TrackedPart(mats)
    p.cyl(((AXLE_X0 + AXLE_X1) / 2.0, SPOOL_Y, AXLE_Z), AXLE_R, AXLE_X1 - AXLE_X0,
          'X', 12, CHROME)
    p.restamp("axle")
    return p.finish("Mesh_Leash_Axle", coll)


def ratchet(coll, mats):
    """Ratchet wheel against the little-finger upright, hex boss outboard of
    it closing the axle end."""
    p = TrackedPart(mats)
    p.cyl((RATCHET_X, SPOOL_Y, AXLE_Z), RATCHET_R, RATCHET_D, 'X', 12, STEEL)
    p.cyl((BOSS_X, SPOOL_Y, AXLE_Z), BOSS_R, BOSS_D, 'X', 6, DARK)
    p.restamp("ratchet")
    return p.finish("Mesh_Leash_Ratchet", coll)


def pawl(coll, mats):
    """A bar from its pin on the upright down onto the ratchet's rim, in the
    wheel's own plane so its tooth genuinely enters the wheel.

    `R_x(theta)` sends the box's local +Y to (0, cos, sin), so theta is the
    bearing from the pin to the wheel's centre; the box is centred so it
    starts 4 mm behind the pin and ends 6 mm inside the rim.
    """
    p = TrackedPart(mats)
    pin = Vector(PAWL_PIN)
    to_hub = Vector((0.0, SPOOL_Y - pin.y, AXLE_Z - pin.z))
    theta = math.atan2(to_hub.z, to_hub.y)
    d = to_hub.normalized()
    reach = to_hub.length - RATCHET_R + 0.006
    centre = pin + d * (reach - 0.004) / 2.0
    hard = p.box(centre, (PAWL_T, reach + 0.004, PAWL_W), STEEL,
                 rot=Matrix.Rotation(theta, 4, 'X'))
    p.cyl((RATCHET_X + 0.008, pin.y, pin.z), 0.008, 0.040, 'X', 8, CHROME)
    p.restamp("pawl")
    p.bevel(hard, width=0.0015, segments=1)
    return p.finish("Mesh_Leash_Pawl", coll)


def lever(coll, mats):
    """Brake hub on the thumb end of the axle, its nut, and the arm leaning
    back toward the elbow with a rubber grip on the end."""
    p = TrackedPart(mats)
    p.cyl((HUB_X, SPOOL_Y, AXLE_Z), HUB_R, HUB_D, 'X', 16, STEEL)
    p.cyl((NUT_X, SPOOL_Y, AXLE_Z), NUT_R, NUT_D, 'X', 6, CHROME)
    a = math.radians(ARM_ANGLE)
    d = Vector((0.0, math.cos(a), math.sin(a)))
    rot = Matrix.Rotation(a, 4, 'X')
    root = Vector((ARM_X, SPOOL_Y, AXLE_Z))
    hard = p.box(root + d * ARM_LEN / 2.0, (ARM_T, ARM_LEN, ARM_W), DARK, rot=rot)
    p.cyl(root + d * GRIP_AT, GRIP_R, GRIP_LEN, 'Y', 10, RUBBER, rot=rot)
    p.restamp("lever")
    p.bevel(hard, width=0.002, segments=1)
    return p.finish("Mesh_Leash_Lever", coll)


def fairlead(coll, mats):
    """Pedestal on the cradle plate, a bracket reaching forward over the hand,
    and a rounded steel eye on its end, axis along the arm.

    The bracket meets the eye at the BOTTOM of the ring, not through its
    centre: a neck run to the eye's axis would fill the hole the rope goes
    through. Its top face is 6 mm below the hole.
    """
    p = TrackedPart(mats)
    hard = p.slab(*FAIRLEAD_FOOT, DARK)
    hard += p.slab(*FAIRLEAD_NECK, DARK)
    p.torus(EYE, EYE_MAJOR, EYE_MINOR, 'Y', 20, 8, STEEL)
    p.restamp("fairlead")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_Leash_Fairlead", coll)


def rope_lead(coll, mats):
    """From inside the winding, off it at the tangent point, through the eye,
    then down into the hook's ferrule. Both ends are buried."""
    p = TrackedPart(mats)
    leave, a = rope_tangent_point()
    inside = Vector((0.0, SPOOL_Y + 0.090 * math.cos(a), AXLE_Z + 0.090 * math.sin(a)))
    R = hook_frame()
    ferrule = HOOK_EYE + R.to_3x3() @ Vector((0.0, -0.040, 0.0))
    sag = Vector((0.0, EYE.y - 0.032, EYE.z - 0.034))
    tube_path(p, [inside, leave, EYE, sag, ferrule], ROPE_R, HEMP, seg=8)
    p.restamp("rope lead")
    return p.finish("Mesh_Leash_RopeLead", coll)


def hook(coll, mats):
    """Built in its own frame — eye at the origin, shank along +Y, mouth
    toward +X — then seated by `hook_frame()`.

    The J: eye torus, crimp ferrule behind it that the rope buries into,
    shank, a 210 degree arc from the shank's end round the bottom and back
    up, a squared tip, and a gate bar across the mouth.
    """
    p = TrackedPart(mats)
    w = HOOK_WIRE
    p.torus((0.0, 0.0, 0.0), 0.022, 0.009, 'Z', 12, 6, DARK)
    p.cyl((0.0, -0.032, 0.0), 0.017, 0.048, 'Y', 10, DARK)
    p.cyl((0.0, 0.044, 0.0), w, 0.060, 'Y', 8, DARK)
    cx, cy = HOOK_CURVE_R, 0.072
    arc_tube(p, (cx, cy, 0.0), HOOK_CURVE_R, w, 180.0, -30.0, DARK)
    a = math.radians(-30.0)
    tip = (cx + HOOK_CURVE_R * math.cos(a), cy + HOOK_CURVE_R * math.sin(a))
    hard = p.box((tip[0], tip[1] - 0.010, 0.0), (0.014, 0.024, 0.014), DARK)
    hard += p.box((0.026, 0.042, 0.0), (0.048, 0.008, 0.007), STEEL)
    p.restamp("hook")
    p.bevel(hard, width=0.001, segments=1)
    obj = p.finish("Mesh_Leash_Hook", coll)
    return seat(obj, HOOK_EYE, rotation=hook_frame())


def muzzle(coll):
    obj = bpy.data.objects.new("muzzle", None)
    obj.empty_display_type = 'ARROWS'
    obj.empty_display_size = 0.05
    obj.location = EYE
    coll.objects.link(obj)
    return obj


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_GauntletLeash")

    cradle(coll, mats)
    spool(coll, mats)
    winding(coll, mats)
    axle(coll, mats)
    ratchet(coll, mats)
    pawl(coll, mats)
    lever(coll, mats)
    fairlead(coll, mats)
    rope_lead(coll, mats)
    hook(coll, mats)
    muzzle(coll)

    save(out)
    report()
    leave, _ = rope_tangent_point()
    print("  rope leaves the winding at (%.4f, %.4f, %.4f)" % tuple(leave))

    device = 0
    lo = [1e9] * 3
    hi = [-1e9] * 3
    for o in sorted(bpy.data.objects, key=lambda o: o.name):
        if o.type == 'EMPTY':
            print("  %-24s loc (%.3f, %.3f, %.3f)  rot %s"
                  % (o.name, *o.location, tuple(o.rotation_euler)))
            continue
        if not o.name.startswith("Mesh_Leash"):
            continue
        device += sum(len(p.vertices) - 2 for p in o.data.polygons)
        pts = [o.matrix_world @ v.co for v in o.data.vertices]
        a = [min(q[i] for q in pts) for i in range(3)]
        b = [max(q[i] for q in pts) for i in range(3)]
        lo = [min(lo[i], a[i]) for i in range(3)]
        hi = [max(hi[i], b[i]) for i in range(3)]
        print("  %-24s loc (%.3f, %.3f, %.3f)  min (%.3f, %.3f, %.3f)  max (%.3f, %.3f, %.3f)"
              % (o.name, *o.location, *a, *b))
    print("  DEVICE TRIS: %d" % device)
    print("  DEVICE bounds min (%.3f, %.3f, %.3f) max (%.3f, %.3f, %.3f)"
          % (*lo, *hi))
    print("  envelope: |x| %.3f<=0.210  y %.3f>=-0.240 .. %.3f<=0.360  z %.3f<=0.640"
          % (max(abs(lo[0]), abs(hi[0])), lo[1], hi[1], hi[2]))


if __name__ == "__main__":
    main()
