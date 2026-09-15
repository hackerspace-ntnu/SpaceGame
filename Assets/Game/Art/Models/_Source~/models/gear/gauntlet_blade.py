"""Wrist Blade gauntlet — a sheath on the forearm that throws a blade out over the hand.

    blender --background --python gauntlet_blade.py -- --out gauntlet_blade.blend

A flat steel sheath lies along the bracer's deck with its mouth just short of
the wrist; inside it, lying in a channel, is one of `retract_blade.blend`'s
blades. A pneumatic actuator at the elbow end drives the blade out of the mouth
along the arm and over the back of the hand, and draws it back. The blade is
fully hidden at rest and reaches 0.21 m past the wrist when out.

## Frame

Family frame from `_gauntlet.py`: arm along +Y, wrist joint at y = 0, elbow +Y,
forward (toward the hand) −Y, dorsal +Z, thumb +X on a right forearm.
`_exportlib` maps Blender (x, y, z) onto Unity (−x, z, −y), so the blade slides
along Blender −Y = Unity +Z, the item's forward. Origin at the wrist bone, true
suit scale, worn at scale 1.

## The layout

| Part | y | z | Notes |
|---|---|---|---|
| `Mesh_WristBlade_Sheath`   | −0.216..0.350 | 0.247..0.283 | floor, two walls and a roof round a 0.056 × 0.018 channel; sunk 3 mm into the deck, hanging 216 mm over the back of the hand to the knuckles |
| `Mesh_WristBlade_Mouth`    | −0.220..−0.204 | 0.243..0.285 | chrome frame round the channel's opening, 4 mm proud of the sheath all round |
| `Mesh_WristBlade_Actuator` | 0.230..0.354 | axis 0.302 | the cylinder on the sheath's roof, its rod lug on the blade's root |
| `Mesh_WristBlade_Cover`    | 0.110..0.220 | 0.281..0.287 | safety-orange plate on the roof with a red arming stripe |
| `Mesh_WristBlade_Lamps`    | 0.070 | 0.283..0.293 | two amber ready lamps beside the mouth |
| `Mesh_RetractBlade_Straight` | −0.210..0.340 (rest) | 0.262 | appended; origin re-seated at `BLADE_ROOT` |

The sheath overhangs the deck's front edge (y 0.100) by 316 mm — past the wrist
and over the back of the hand to the knuckles, the last 24 mm of the family's
forward device limit left for the mouth frame — and crosses the collar at
y < 0.090 with its floor at 0.247 — 30 mm above the collar's crown (0.2165) —
which is the `audit()` rule the puncher also keeps.

## The stroke

`STROKE` is the blade's own `LENGTH` (0.550): the tip goes from 6 mm inside the
mouth at rest to −0.760 at full extension — three quarters of a metre past the
wrist, on purpose: the whole point of the item is the reach, and a blade that stopped at
the family's −0.240 device limit read as a letter opener. The device itself
(sheath, mouth, actuator) stays inside that limit; only the blade at full stroke
is allowed past it, to `BLADE_REACH_Y0`, and only along the channel axis above
the glove at z 0.262 (limit 0.200). `audit()` sweeps the blade to full stroke and
refuses to save if it leaves those bounds, or if it touches the sheath's walls,
floor or roof anywhere along the way.

## The blade pivot

The blade's origin is `BLADE_ROOT`, on the channel's axis at the tang's rear
face. Unity slides that one transform by `STROKE` along the prefab's forward.
The actuator's rod is part of the actuator, not of the blade: at 0.26 m of
stroke a rod that followed the blade would leave its cylinder, and a rod that
stays put reads as the piston inside a closed cylinder — which is what it is.

## Empties

`Marker_Grip` at the origin (the builder adopts it as GripPoint) and
`Marker_Mouth` on the channel axis at the mouth plane, where the sparks fly
when the blade comes out. Identity rotation, exported with `keep_empties=True`.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, _HERE)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))

import bpy  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from _gauntlet import LIB, append_objects, BASE_DECK_Z, BASE_WRIST_EDGE  # noqa: E402
import retract_blade  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

BLADES = os.path.join(LIB, "components", "mechanical", "retract_blade.blend")
BLADE = "Mesh_RetractBlade_Straight"

# Index 0 is a structural metal: `bmesh.ops.bevel` stamps its new faces with 0.
STEEL, DARK, CHROME, BRASS, ORANGE, RED, AMBER = range(7)
MATS = ["Mat_Metal_Steel_Worn",        # sheath walls, actuator lug
        "Mat_Metal_Steel_Dark",        # sheath floor and roof, actuator end caps
        "Mat_Metal_Chrome_Scuffed",    # mouth frame, rod, bolt heads
        "Mat_Metal_Brass_Tarnished",   # actuator cylinder
        "Mat_Paint_Safety_Orange",     # the cover plate
        "Mat_Paint_Warn_Red",          # the arming stripe
        "Mat_Emissive_Amber"]          # ready lamps

BEVEL_W = 0.0024

# ── The device envelope (the family's) ───────────────────────────────────────
ENV_Y1, ENV_Z1, ENV_HX = 0.360, 0.640, 0.210
REACH_Y0, REACH_Z0, REACH_HX = -0.240, 0.200, 0.200
COLLAR_Z = 0.2165

# ── The channel the blade lies in ────────────────────────────────────────────
CHAN_HX = retract_blade.WIDTH / 2 + 0.004                       # 0.026: 4 mm play a side
CHAN_HZ = retract_blade.THICK / 2 + retract_blade.SPINE_H + 0.003   # 0.009: the tang's collar is 2 mm proud of the spine
CHAN_Z = 0.262                                                   # channel axis
WALL, FLOOR, ROOF = 0.010, 0.008, 0.012

# ── The sheath ───────────────────────────────────────────────────────────────
SHEATH_Y0, SHEATH_Y1 = -0.216, 0.350                             # mouth at the knuckles: the last of the reach limit goes to the mouth frame
SHEATH_HX = CHAN_HX + WALL                                       # 0.036
SHEATH_Z0 = BASE_DECK_Z - 0.003                                  # 0.247, sunk into the deck
SHEATH_Z1 = CHAN_Z + CHAN_HZ + ROOF                              # 0.281

MOUTH_Y0, MOUTH_Y1 = SHEATH_Y0 - 0.004, SHEATH_Y0 + 0.012
MOUTH_PROUD = 0.004

# ── The blade and its stroke ─────────────────────────────────────────────────
STROKE = retract_blade.LENGTH                                    # 0.550
BLADE_ROOT = Vector((0.0, SHEATH_Y1 - 0.010, CHAN_Z))            # y 0.340
TIP_REST_Y = BLADE_ROOT.y - retract_blade.LENGTH                 # -0.210: 6 mm inside the mouth
BLADE_REACH_Y0 = -0.780                                          # the point at full stroke, the one thing allowed past REACH_Y0

# ── The actuator on the roof ─────────────────────────────────────────────────
ACT_R, ACT_Z = 0.020, SHEATH_Z1 + 0.021                          # axis 0.302
ACT_Y0, ACT_Y1 = 0.230, 0.340                                    # union tip at 0.354, inside the 0.360 elbow limit
ROD_R, ROD_Y0 = 0.007, 0.206
LUG_Y0, LUG_Y1 = 0.200, 0.212

# ── Dressing ─────────────────────────────────────────────────────────────────
COVER_Y0, COVER_Y1, COVER_HX = 0.110, 0.220, 0.028
STRIPE_Y0, STRIPE_Y1 = 0.196, 0.206
LAMP_X, LAMP_Y, LAMP_R = 0.024, 0.070, 0.006
MOUTH_MARK = Vector((0.0, SHEATH_Y0, CHAN_Z))


def sheath(coll, mats):
    """Floor, two walls and a roof round the channel. Four slabs in one part:
    they meet along shared edges, never on a shared plane, and the channel is
    the air between them rather than a hole cut out of a block."""
    p = TrackedPart(mats)
    hard = []
    hard += p.slab((-SHEATH_HX, SHEATH_Y0, SHEATH_Z0), (SHEATH_HX, SHEATH_Y1, CHAN_Z - CHAN_HZ), DARK)
    for sx in (-1, 1):
        hard += p.slab((sx * CHAN_HX, SHEATH_Y0, CHAN_Z - CHAN_HZ - 0.001),
                       (sx * SHEATH_HX, SHEATH_Y1, CHAN_Z + CHAN_HZ + 0.001), STEEL)
    hard += p.slab((-SHEATH_HX, SHEATH_Y0, CHAN_Z + CHAN_HZ), (SHEATH_HX, SHEATH_Y1, SHEATH_Z1), DARK)
    # Rear end cap: the channel is closed at the elbow end, where the tang sits.
    hard += p.slab((-SHEATH_HX, SHEATH_Y1 - 0.006, SHEATH_Z0), (SHEATH_HX, SHEATH_Y1, SHEATH_Z1), DARK)
    # Deck bolts, four, on the walls' tops.
    for sx in (-1, 1):
        for y in (0.130, 0.290):
            p.cyl((sx * (SHEATH_HX - 0.005), y, SHEATH_Z1 + 0.002), 0.0045, 0.006, 'Z', 6, CHROME,
                  radius_top=0.0036)
    p.restamp("sheath")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_WristBlade_Sheath", coll)


def mouth(coll, mats):
    """Chrome frame round the channel's opening: four bars, each 4 mm proud of
    the sheath's outer faces, and the frame's inner edge 1 mm outside the
    channel so the blade never touches it."""
    p = TrackedPart(mats)
    hx_in, hz_in = CHAN_HX + 0.001, CHAN_HZ + 0.001
    hx_out, z0_out, z1_out = SHEATH_HX + MOUTH_PROUD, SHEATH_Z0 - MOUTH_PROUD, SHEATH_Z1 + MOUTH_PROUD
    hard = []
    hard += p.slab((-hx_out, MOUTH_Y0, z0_out), (hx_out, MOUTH_Y1, CHAN_Z - hz_in), CHROME)
    hard += p.slab((-hx_out, MOUTH_Y0, CHAN_Z + hz_in), (hx_out, MOUTH_Y1, z1_out), CHROME)
    for sx in (-1, 1):
        hard += p.slab((sx * hx_in, MOUTH_Y0, CHAN_Z - hz_in - 0.001),
                       (sx * hx_out, MOUTH_Y1, CHAN_Z + hz_in + 0.001), CHROME)
    p.restamp("mouth")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_WristBlade_Mouth", coll)


def actuator(coll, mats):
    """The pneumatic cylinder on the roof, its rod reaching forward to a lug
    that drops through the roof onto the blade's tang. The rod does NOT ride
    the blade — see the module doc."""
    p = TrackedPart(mats)
    c = (0.0, (ACT_Y0 + ACT_Y1) / 2, ACT_Z)
    p.cyl(c, ACT_R, ACT_Y1 - ACT_Y0, 'Y', 14, BRASS)
    p.cyl((0.0, ACT_Y0 + 0.004, ACT_Z), ACT_R + 0.003, 0.010, 'Y', 14, DARK)   # gland
    p.cyl((0.0, ACT_Y1 - 0.004, ACT_Z), ACT_R + 0.003, 0.010, 'Y', 14, DARK)   # end cap
    p.cyl((0.0, ACT_Y1 + 0.006, ACT_Z), 0.008, 0.016, 'Y', 8, BRASS)            # air union
    p.cyl((0.0, (ROD_Y0 + ACT_Y0) / 2 + 0.002, ACT_Z), ROD_R, ACT_Y0 - ROD_Y0 + 0.004, 'Y', 10, CHROME)
    # The lug: a block on the roof the rod pins into, riveted down.
    hard = p.slab((-0.014, LUG_Y0, SHEATH_Z1 - 0.002), (0.014, LUG_Y1 + 0.012, ACT_Z + 0.006), STEEL)
    p.cyl((0.0, LUG_Y0 + 0.006, ACT_Z), 0.0045, 0.034, 'X', 8, CHROME)          # the pin
    # Saddle under the barrel, so it stands on the roof rather than floating.
    hard += p.slab((-0.024, ACT_Y0 + 0.020, SHEATH_Z1 - 0.002), (0.024, ACT_Y1 - 0.020, ACT_Z - ACT_R + 0.004), STEEL)
    p.restamp("actuator")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_WristBlade_Actuator", coll)


def cover(coll, mats):
    """The suit's orange accent on the roof, sunk 2 mm and 4 mm proud, with the
    arming stripe across its rear end."""
    p = TrackedPart(mats)
    hard = p.slab((-COVER_HX, COVER_Y0, SHEATH_Z1 - 0.002), (COVER_HX, COVER_Y1, SHEATH_Z1 + 0.004), ORANGE)
    p.slab((-COVER_HX + 0.004, STRIPE_Y0, SHEATH_Z1 + 0.004), (COVER_HX - 0.004, STRIPE_Y1, SHEATH_Z1 + 0.0055), RED)
    p.restamp("cover")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_WristBlade_Cover", coll)


def lamps(coll, mats):
    """Two amber ready lamps on the roof beside the mouth, domed, 3 mm into it."""
    p = TrackedPart(mats)
    for sx in (-1, 1):
        p.cyl((sx * LAMP_X, LAMP_Y, SHEATH_Z1 + 0.003), LAMP_R, 0.012, 'Z', 10, AMBER,
              radius_top=LAMP_R * 0.8)
    p.restamp("lamps")
    return p.finish("Mesh_WristBlade_Lamps", coll)


def blade(coll):
    """The straight blade, seated with its tang's rear face at `BLADE_ROOT`."""
    obj, = append_objects(BLADES, [BLADE], coll)
    world = Matrix.Translation(BLADE_ROOT) @ obj.matrix_world
    obj.data.transform(Matrix.Translation(-BLADE_ROOT) @ world)
    obj.location = BLADE_ROOT
    obj.rotation_euler = (0.0, 0.0, 0.0)
    obj.scale = (1.0, 1.0, 1.0)
    return obj


def markers(coll):
    for name, at in (("Marker_Grip", (0.0, 0.0, 0.0)), ("Marker_Mouth", tuple(MOUTH_MARK))):
        obj = bpy.data.objects.new(name, None)
        obj.empty_display_type = 'ARROWS'
        obj.empty_display_size = 0.05
        obj.location = Vector(at)
        coll.objects.link(obj)


def audit():
    """Prove the geometry before saving: the blade's pivot, the envelope at rest
    and at full stroke, the collar rule, and that the blade never touches the
    sheath anywhere along its travel. Raised, not reported."""
    bpy.context.view_layer.update()
    bl = bpy.data.objects[BLADE]
    if (bl.location - BLADE_ROOT).length > 1e-6:
        raise SystemExit("%s origin %s is not BLADE_ROOT" % (BLADE, tuple(bl.location)))

    tip_rest = min((bl.matrix_world @ v.co).y for v in bl.data.vertices)
    if tip_rest < SHEATH_Y0:
        raise SystemExit("the blade tip shows at rest (y %.3f, mouth %.3f)" % (tip_rest, SHEATH_Y0))
    if tip_rest - STROKE < BLADE_REACH_Y0:
        raise SystemExit("full stroke reaches y %.3f, past the limit %.3f" % (tip_rest - STROKE, BLADE_REACH_Y0))

    device = [o for o in bpy.data.objects if o.type == 'MESH']
    for o in device:
        poses = [(0.0, 0.0, 0.0), (0.0, -STROKE, 0.0)] if o is bl else [(0.0, 0.0, 0.0)]
        for shift in poses:
            where = "at full stroke" if shift[1] else "at rest"
            bad = 0
            for v in o.data.vertices:
                q = (o.matrix_world @ v.co) + Vector(shift)
                if q.y > ENV_Y1 or q.z > ENV_Z1 or abs(q.x) > ENV_HX:
                    bad += 1
                elif o is bl and shift[1] and (q.y < BLADE_REACH_Y0 or q.z < REACH_Z0 or abs(q.x) > REACH_HX):
                    bad += 1   # the blade out: past the device limit by design, never below the glove
                elif q.y < BASE_WRIST_EDGE and not (o is bl and shift[1]) and (q.y < REACH_Y0 or q.z < REACH_Z0 or abs(q.x) > REACH_HX):
                    bad += 1
                elif q.y < 0.090 and q.z < COLLAR_Z + 0.004:
                    bad += 1
                elif o is bl and SHEATH_Y0 <= q.y <= SHEATH_Y1 and (
                        abs(q.x) > CHAN_HX - 0.0005 or abs(q.z - CHAN_Z) > CHAN_HZ - 0.0005):
                    bad += 1   # the blade touches its own channel
            if bad:
                raise SystemExit("%s puts %d vertices out of bounds %s" % (o.name, bad, where))
    print("  audit: stroke %.3f; tip y %.3f at rest, %.3f out; envelope, collar and channel clear"
          % (STROKE, tip_rest, tip_rest - STROKE))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_GauntletBlade")

    sheath(coll, mats)
    mouth(coll, mats)
    actuator(coll, mats)
    cover(coll, mats)
    lamps(coll, mats)
    blade(coll)
    markers(coll)

    audit()
    save(out)
    report()

    u = (-BLADE_ROOT.x, BLADE_ROOT.z, -BLADE_ROOT.y)
    print("  BLADE_ROOT blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)  "
          "stroke %.3f m along Blender -Y / Unity +Z" % (*BLADE_ROOT, *u, STROKE))
    for n in ("Marker_Grip", "Marker_Mouth"):
        b = bpy.data.objects[n].location
        print("  %-12s blender (%.4f, %.4f, %.4f)  unity (%.4f, %.4f, %.4f)"
              % (n, *b, -b.x, b.z, -b.y))


if __name__ == "__main__":
    main()
