"""Gauntlet Grapple — the forearm-mounted harpoon launcher, on the gauntlet base.

    blender --background --python gauntlet_grapple.py -- --out gauntlet_grapple.blend

The base-generation successor to `grapple_bracer.py`. A launch tube down the
back of the arm, a winch drum feeding it, a gas bottle on the flank and a
harpoon seated in the tube, built at TRUE suit scale on
`Coll_GauntletBase_Mount` (`components/props/gauntlet_base.py`) so it ships at
scale 1 through the same `GauntletFit` as every other gauntlet. Nothing about
the sleeve is authored here.

**Sized for the second cut (2026-09-03): the device is twice the first build's
linear size.** The first one read as jewellery on a 3 m astronaut. Every
number below was re-derived rather than multiplied through, so the parts stay
proportionate, embeds are still 2-4 mm and the bevels are the same 3 mm they
always were — a scaled embed is 8 mm of slop and a scaled chamfer reads as
melted.

| Object | Where it comes from |
|---|---|
| `Mesh_GauntletBase_*_Mount`  | `components/props/gauntlet_base.blend`, unchanged |
| `Mesh_Grapple_Tube`          | the launch tube down the deck's centreline |
| `Mesh_Grapple_TubeRail`      | the rib along the tube's crown |
| `Mesh_Grapple_MuzzleCollar`  | the orange muzzle collar — the one accent |
| `Mesh_Grapple_MuzzleBushing` | the bore bushing the shaft rides in |
| `Mesh_Grapple_CradleFront`   | cradle bar carrying the tube, sunk in the deck |
| `Mesh_Grapple_CradleRear`    | same, behind |
| `Mesh_Grapple_Breech`        | receiver block: the harpoon's tail lives in it |
| `Mesh_Grapple_PylonLeft`     | drum bearing strut, −X (little-finger) side |
| `Mesh_Grapple_PylonRight`    | drum bearing strut, +X (thumb) side |
| `Mesh_CableDrum_Caged`       | `components/props/cable_drum.blend`, at 2x, turned end for end |
| `Mesh_Grapple_Cable`         | the drum's lead-off carried down into the breech gland |
| `Mesh_GasBottle_Flask`       | `components/props/gas_bottle.blend`, at 1.8x |
| `Mesh_Grapple_BottleClamps`  | two bands round the flask, bolted to the tube |
| `Mesh_GrappleHarpoon`        | `components/props/grapple_dart.blend`, at 0.60x |
| `muzzle`                     | empty at the tube mouth, identity rotation |

Component names are kept rather than renamed, as the bracer did, so the
provenance of each piece reads straight off the outliner. Unity binds two
things by name: `Mesh_GrappleHarpoon`, which `GrapplingHookArtifact.seatedHook`
hides while the hook flies and shows again on return, and `muzzle`, which its
`muzzle` field pays the rope out from.


## The frame

Family frame from `_gauntlet.py`: **arm along +Y, wrist at y = 0, elbow toward
+Y, forward −Y, dorsal +Z, +X the thumb side of a right forearm.** The export
maps Blender `(x, y, z)` onto Unity `(−x, z, −y)`, so the tube's axis (−Y)
lands on Unity +Z and an unrotated `muzzle` empty already points out of the
tube. The origin stays at the wrist joint; the left arm is this model at a
negative X scale.


## Where everything sits, and what fixes it there

The deck is the plane z = 0.250, x ±0.070, y 0.100..0.320 (`BASE_DECK_*`).
Feet are sunk to z = 0.247. The envelope this build works to: forward to
y = −0.24 provided z ≥ 0.20 and |x| ≤ 0.20, elbow end y ≤ 0.36 (the arm has
to fold), z ≤ 0.64, |x| ≤ 0.21.

- **Tube** y −0.020..0.250, outer r 0.086, wall 16 mm, axis z 0.333 — so its
  bottom is at 0.247, 3 mm into the deck, and its crown at 0.419. Over the
  deck its footprint at the deck plane is only x ±0.023, well inside it.
- **Muzzle** at y −0.020: an orange collar (outer r 0.100, inner 0.083, so
  3 mm into the tube wall) and a dark bore bushing closing the 140 mm bore
  down to 48 mm round the shaft.
- **Harpoon** at 0.60x with its eye at y 0.302. **The head, not the tube,
  is what reaches forward**: the blade tip lands at y −0.238 and the barb
  roots at −0.034, 14 mm clear of the collar's front face. That is the
  binding constraint on the whole layout — the head is 0.336 m long at this
  scale and every millimetre of it has to be outside the muzzle, so a longer
  tube would mean a smaller harpoon, not a bigger one.
- **Cradles** at y 0.150 and 0.190, tops at the tube's axis so the tube is
  half-buried in them; x ±0.062, inside the deck and clear of the front bolt
  bosses at y 0.114.
- **Breech** y 0.220..0.345: a sunk foot at x ±0.066 (inside the deck) and a
  body at x ±0.090 starting 6 mm above the deck plane, so nothing outside
  the deck's footprint is below it. Top at 0.430, 11 mm over the tube's
  crown, which leaves a flat face for the gland, the lamp and the stripe.
  The harpoon's tail sits inside it at y 0.328.
- **Drum** (Caged, 2x) on two pylon struts above the breech, axle along X at
  (0, 0.240, 0.530): the cage clears the tube's crown by 16 mm and tops out
  at 0.615. **Turned end for end** (`R_z(180)`) so its own lead-off tail
  points at the elbow — the cable then drops down the BACK of the drum into
  the gland instead of having to cross the tube's crown, which is what the
  first routing did and it went through the barrel.
- **Bottle** (Flask, 1.8x) along the −X (little-finger, outward) flank,
  x −0.157..−0.096, z 0.283..0.407 — outside the deck's footprint but above
  its plane. Rolled −90° so its flat face is vertical against the tube and
  the recessed gauge, which faces the flask's local −Y, ends up looking
  outboard where it can be read.


## Triangles

The device is capped at 6,000 and the three appended components are 9,500 on
their own, so each appended COPY is decimated inside this file — never the
component `.blend`, which stays the library's source of truth. Ratios are the
`*_LIGHTEN` constants; the drum and the flask take the heavy cuts because
they are read as masses, the harpoon the light one because its blade and
barbs are the silhouette.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))
sys.path.insert(0, _HERE)

import bpy  # noqa: E402
from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402
from _gauntlet import (  # noqa: E402
    BASE_DECK_Z, PROPS, append_objects, place)
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

DRUM = os.path.join(PROPS, "cable_drum.blend")
BOTTLE = os.path.join(PROPS, "gas_bottle.blend")
DART = os.path.join(PROPS, "grapple_dart.blend")

# Index 0 is a structural metal because `bmesh.ops.bevel` stamps every face it
# creates with material index 0.
STEEL, DARK, CHROME, ORANGE, WARN, AMBER, BRASS = range(7)
MATS = ["Mat_Metal_Steel_Worn",        # the tube, clamp bands
        "Mat_Metal_Steel_Dark",        # breech, cradles, pylons, bushing, rail
        "Mat_Metal_Chrome_Scuffed",    # cable, gland, bolt heads
        "Mat_Paint_Safety_Orange",     # muzzle collar — the one accent
        "Mat_Paint_Warn_Red",          # arming stripe on the breech
        "Mat_Emissive_Amber",          # ready lamp on the breech
        "Mat_Metal_Brass_Tarnished"]   # bearing collars on the pylons

# Not scaled with the model: a 6 mm chamfer on a 16 mm wall reads as a melted
# edge. Same width the first build used.
BEVEL_W = 0.0030
SINK = 0.003                           # how far feet go into the deck
FOOT_Z = BASE_DECK_Z - SINK            # 0.247

# --- the launch tube --------------------------------------------------------
AXIS_Z = 0.3330                        # tube bottom lands on FOOT_Z
TUBE_R, TUBE_WALL = 0.0860, 0.0160
TUBE_Y0, TUBE_Y1 = -0.0200, 0.2500
TUBE_TOP = AXIS_Z + TUBE_R             # 0.419

RAIL_Y0, RAIL_Y1 = 0.0500, 0.2450      # crown rib: 4 mm proud, 12 mm buried
RAIL_W, RAIL_H = 0.0240, 0.0160

COLLAR_R, COLLAR_LEN = 0.1000, 0.0720
COLLAR_T = COLLAR_R - (TUBE_R - 0.003)  # inner wall 3 mm into the tube's
COLLAR_Y = TUBE_Y0 + 0.0020 + COLLAR_LEN / 2

BUSHING_R_IN, BUSHING_R_OUT = 0.0240, 0.0730   # outer 3 mm into the bore wall
BUSHING_LEN = 0.0360
BUSHING_Y = TUBE_Y0 + 0.0010 + BUSHING_LEN / 2

# --- what stands on the deck ------------------------------------------------
CRADLE_Y = (0.1500, 0.1900)            # clear of the front bosses (y <= 0.121)
CRADLE_LEN, CRADLE_HX = 0.0520, 0.0620

BREECH_Y0, BREECH_Y1 = 0.2200, 0.3450
BREECH_FOOT_HX, BREECH_FOOT_TOP = 0.0660, 0.2580   # inside the deck footprint
BREECH_FOOT_Y1 = 0.3180                # 2 mm inside the deck's rear edge: the
#                                        sunk foot must not hang off the end of
#                                        the deck, where it would be below the
#                                        deck plane with only shell under it
BREECH_HX, BREECH_BOTTOM = 0.0900, 0.2560          # body: above the deck plane
BREECH_TOP = 0.4300                                # 11 mm over the tube's crown
GLAND = (0.0, 0.3300, BREECH_TOP)

# --- the drum and its pylons ------------------------------------------------
DRUM_K = 2.0
DRUM_AT = (0.0, 0.2400, 0.5300)
# `cable_drum.lead_off` ends here in the drum's own frame; R_z(180) turns it
# toward the elbow, which is the whole reason the drum is fitted backwards.
DRUM_LEAD = (0.0, -0.0560, 0.0240)
LEAD_END = (DRUM_AT[0], DRUM_AT[1] - DRUM_K * DRUM_LEAD[1],
            DRUM_AT[2] + DRUM_K * DRUM_LEAD[2])          # (0, 0.352, 0.578)

PYLON_X0, PYLON_X1 = 0.0820, 0.0960    # outboard of the cage's ±0.078 bars
# A strut, not a plate. The bracer learned this the expensive way: a slab
# either side of the drum hides the wound cable from every angle except
# straight down, and the coil is one of the three things this device exists to
# show. The profile is (y, z) — `prism(axis='X')` maps it onto the arm plane
# and extrudes across — a post from the breech top up round the axle.
PYLON_PROFILE = [(0.2020, 0.4100), (0.2780, 0.4100), (0.2780, 0.5100),
                 (0.2680, 0.5680), (0.2120, 0.5680), (0.2020, 0.5100)]

# --- the bottle -------------------------------------------------------------
BOTTLE_K = 1.8
BOTTLE_BASE = (-0.1212, 0.2450, 0.3450)
BOTTLE_ROLL = -90.0
# The flask's placed extents, written down because the clamp bands are built
# against them rather than against the component's local frame.
BOTTLE_X0, BOTTLE_X1 = -0.1570, -0.0960
BOTTLE_Z0, BOTTLE_Z1 = 0.2829, 0.4071
BAND_Y = (0.1300, 0.2050)              # forward of the breech's front face

# --- the harpoon ------------------------------------------------------------
HARPOON_K = 0.6000
HARPOON_EYE = (0.0, 0.3020, AXIS_Z)

MUZZLE = (0.0, TUBE_Y0, AXIS_Z)

# Decimation of the APPENDED COPIES only — see the module docstring.
DRUM_LIGHTEN, BOTTLE_LIGHTEN, HARPOON_LIGHTEN = 0.30, 0.42, 0.50


def tube(coll, mats):
    """The launch tube: one thick-walled pipe from the breech to the mouth."""
    p = TrackedPart(mats)
    p.tube((0.0, (TUBE_Y0 + TUBE_Y1) / 2, AXIS_Z), TUBE_R, TUBE_WALL,
           TUBE_Y1 - TUBE_Y0, 'Y', 20, STEEL)
    p.restamp("tube")
    return p.finish("Mesh_Grapple_Tube", coll)


def tube_rail(coll, mats):
    """A rib along the tube's crown — 4 mm proud, the rest buried in the wall.

    The first build put a full reinforcing ring here. At this radius a ring
    cannot exist: anything wrapped round a 0.086 m tube whose axis is 83 mm
    over the deck reaches down to z 0.232, which is inside the deck over the
    hardpoint and inside the base's own collar and dorsal shell forward of it.
    A crown rib gives the fat barrel its length-wise line and touches nothing.
    """
    p = TrackedPart(mats)
    hard = p.box((0.0, (RAIL_Y0 + RAIL_Y1) / 2, TUBE_TOP - 0.0040),
                 (RAIL_W, RAIL_Y1 - RAIL_Y0, RAIL_H), DARK)
    p.restamp("rail")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_Grapple_TubeRail", coll)


def muzzle_collar(coll, mats):
    """Orange collar round the mouth, its inner wall 3 mm into the tube's.

    Its front face sits 2 mm behind the tube's mouth, so the two annuli never
    share a plane.
    """
    p = TrackedPart(mats)
    p.tube((0.0, COLLAR_Y, AXIS_Z), COLLAR_R, COLLAR_T, COLLAR_LEN, 'Y', 20,
           ORANGE)
    p.restamp("collar")
    return p.finish("Mesh_Grapple_MuzzleCollar", coll)


def muzzle_bushing(coll, mats):
    """Bore bushing: closes the bore down to the shaft. Its front face is 1 mm
    inside the mouth, its outer wall 3 mm into the tube's inner wall."""
    p = TrackedPart(mats)
    p.tube((0.0, BUSHING_Y, AXIS_Z), BUSHING_R_OUT,
           BUSHING_R_OUT - BUSHING_R_IN, BUSHING_LEN, 'Y', 20, DARK)
    p.restamp("bushing")
    return p.finish("Mesh_Grapple_MuzzleBushing", coll)


def cradle(coll, mats, name, y):
    """A bar across the deck the tube sits in.

    Spans the deck's width inside its chamfer; its top is at the tube's axis,
    so the tube's lower half is inside it and it reads as a clamped barrel
    rather than a barrel resting on a plinth.
    """
    p = TrackedPart(mats)
    hard = p.slab((-CRADLE_HX, y - CRADLE_LEN / 2, FOOT_Z),
                  (CRADLE_HX, y + CRADLE_LEN / 2, AXIS_Z), DARK)
    p.restamp("cradle")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def breech(coll, mats):
    """The receiver: a sunk foot inside the deck's footprint and a wider body
    above the deck plane, with the cable gland, a ready lamp and the arming
    stripe on its top face.

    Two slabs rather than one because the body is wider than the deck: a
    single block sunk 3 mm would put its outboard corners below the deck
    plane outside the deck's footprint, which the base contract forbids.
    """
    p = TrackedPart(mats)
    hard = p.slab((-BREECH_FOOT_HX, BREECH_Y0, FOOT_Z),
                  (BREECH_FOOT_HX, BREECH_FOOT_Y1, BREECH_FOOT_TOP), DARK)
    hard += p.slab((-BREECH_HX, BREECH_Y0, BREECH_BOTTOM),
                   (BREECH_HX, BREECH_Y1, BREECH_TOP), DARK)
    # Gland: a chrome boss the cable enters through, half buried in the top.
    p.cyl(GLAND, 0.0140, 0.0280, 'Z', 12, CHROME)
    # Stripe and lamp: 3 mm into the top face, 3 mm proud.
    p.box((0.0, 0.2600, BREECH_TOP), (0.1400, 0.0120, 0.0060), WARN)
    p.cyl((0.0, 0.2350, BREECH_TOP + 0.0005), 0.0100, 0.0080, 'Z', 12, AMBER)
    p.restamp("breech")
    p.bevel(hard, width=BEVEL_W + 0.001, segments=2)
    return p.finish("Mesh_Grapple_Breech", coll)


def pylon(coll, mats, name, sx):
    """One drum bearing strut: a foot 20 mm inside the breech body, a post
    rising round the axle, a brass collar on its outer face where the drum's
    axle stub comes through, and a bolt head through the foot."""
    p = TrackedPart(mats)
    hard = p.prism(PYLON_PROFILE, PYLON_X1 - PYLON_X0, axis='X', mat=DARK,
                   offset=(sx * (PYLON_X0 + PYLON_X1) / 2, 0, 0))
    p.torus((sx * (PYLON_X1 + 0.0020), DRUM_AT[1], DRUM_AT[2]), 0.0150, 0.0045,
            'X', 10, 5, BRASS)
    p.cyl((sx * (PYLON_X0 + PYLON_X1) / 2, 0.2200, PYLON_PROFILE[0][1] + 0.0130),
          0.0060, 0.0180, 'X', 8, CHROME)
    p.restamp("pylon")
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll)


def cable(coll, mats):
    """From the end of the drum's own lead-off tail down into the breech gland.

    Starts exactly where `cable_drum.lead_off` stops, so the two read as one
    run; ends inside the gland, so it enters the block rather than stopping at
    it. It runs down the drum's elbow side, which is only possible because the
    drum is fitted end for end — see the module docstring.
    """
    p = TrackedPart(mats)
    tube_path(p, [LEAD_END, (0.0, 0.3500, 0.5200), (0.0, 0.3440, 0.4700),
                  (0.0, 0.3340, 0.4240)], 0.0060, CHROME, seg=8)
    p.restamp("cable")
    return p.finish("Mesh_Grapple_Cable", coll)


def bottle_clamps(coll, mats):
    """Two bands round the flask, each bolted through to the tube's flank.

    Four bars per band rather than a ring: the flask is a flat oval, and a
    torus round it either floats off the flats or cuts through the curves.
    The inboard bar is also the mount — it reaches 5 mm into the flask and
    3.5 mm into the tube.
    """
    p = TrackedPart(mats)
    hard = []
    for y in BAND_Y:
        hard += p.slab((-0.1010, y - 0.0080, 0.3240),
                       (-0.0800, y + 0.0080, 0.3540), STEEL)      # inboard mount
        hard += p.slab((BOTTLE_X0 - 0.0050, y - 0.0080, BOTTLE_Z1 - 0.0040),
                       (BOTTLE_X1 - 0.0040, y + 0.0080, BOTTLE_Z1 + 0.0080), STEEL)
        hard += p.slab((BOTTLE_X0 - 0.0050, y - 0.0080, BOTTLE_Z0 - 0.0080),
                       (BOTTLE_X1 - 0.0040, y + 0.0080, BOTTLE_Z0 + 0.0040), STEEL)
        hard += p.slab((BOTTLE_X0 - 0.0050, y - 0.0080, BOTTLE_Z0 - 0.0040),
                       (BOTTLE_X0 + 0.0050, y + 0.0080, BOTTLE_Z1 + 0.0040), STEEL)
        p.cyl(((BOTTLE_X0 + BOTTLE_X1) / 2, y, BOTTLE_Z1 + 0.0080), 0.0055,
              0.0120, 'Z', 8, CHROME)
    p.restamp("clamps")
    p.bevel(hard, width=BEVEL_W, segments=1)
    return p.finish("Mesh_Grapple_BottleClamps", coll)


def drum_matrix():
    """The drum, doubled and turned end for end.

    `R_z(180)` keeps the axle on X and the cage's feet down while sending the
    lead-off tail toward the elbow, which is what lets the cable reach the
    gland without crossing the tube.
    """
    return (Matrix.Translation(Vector(DRUM_AT))
            @ Matrix.Rotation(math.radians(180), 4, 'Z')
            @ Matrix.Diagonal(Vector((DRUM_K,) * 3)).to_4x4())


def bottle_matrix():
    """Flask local (base at origin, axis +Z, flats on local ±Y) onto the flank.

    `R_z(-90)` first, so the roll happens before the flask is laid down:
    local +X (the broad axis) ends up vertical against the tube and local −Y,
    which is the face the gauge is recessed into, ends up looking outboard.
    `R_x(90)` then maps local +Z onto −Y — base at the elbow, valve at the
    wrist. Rolling after the lay-down instead turns the flask on edge and
    points the gauge at the sky.
    """
    return (Matrix.Translation(Vector(BOTTLE_BASE))
            @ Matrix.Rotation(math.radians(90), 4, 'X')
            @ Matrix.Rotation(math.radians(BOTTLE_ROLL), 4, 'Z')
            @ Matrix.Diagonal(Vector((BOTTLE_K,) * 3)).to_4x4())


def harpoon_matrix():
    """The harpoon shrunk and slid into the tube, eye first. No rotation: the
    component is authored tip-down −Y, which is this model's forward."""
    return (Matrix.Translation(Vector(HARPOON_EYE))
            @ Matrix.Diagonal(Vector((HARPOON_K,) * 3)).to_4x4())


def lighten(obj, ratio):
    """Decimate one APPENDED COPY in this file. The component .blend is never
    touched — it is the library's source of truth and other models use it at
    full density."""
    if ratio >= 1.0:
        return obj
    before = sum(len(f.vertices) - 2 for f in obj.data.polygons)
    bpy.context.view_layer.objects.active = obj
    mod = obj.modifiers.new("Lighten", 'DECIMATE')
    mod.decimate_type = 'COLLAPSE'
    mod.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=mod.name)
    after = sum(len(f.vertices) - 2 for f in obj.data.polygons)
    print("  lighten %-24s %5d -> %5d tris (ratio %.2f)"
          % (obj.name, before, after, ratio))
    return obj


def set_origin(obj, at):
    """Move an object's origin to world point `at` without moving its mesh."""
    delta = Vector(at) - obj.location
    obj.data.transform(Matrix.Translation(-delta))
    obj.location = Vector(at)
    return obj


def muzzle(coll):
    """Where the rope pays out: the mouth's centre. Identity rotation — after
    export the empty's Unity +Z is Blender −Y, out of the tube."""
    obj = bpy.data.objects.new("muzzle", None)
    obj.empty_display_type = 'ARROWS'
    obj.empty_display_size = 0.05
    obj.location = Vector(MUZZLE)
    coll.objects.link(obj)
    return obj


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_GauntletGrapple")

    tube(coll, mats)
    tube_rail(coll, mats)
    muzzle_collar(coll, mats)
    muzzle_bushing(coll, mats)
    cradle(coll, mats, "Mesh_Grapple_CradleFront", CRADLE_Y[0])
    cradle(coll, mats, "Mesh_Grapple_CradleRear", CRADLE_Y[1])
    breech(coll, mats)
    pylon(coll, mats, "Mesh_Grapple_PylonLeft", -1)
    pylon(coll, mats, "Mesh_Grapple_PylonRight", 1)
    cable(coll, mats)
    bottle_clamps(coll, mats)

    for obj in append_objects(DRUM, ["Mesh_CableDrum_Caged"], coll):
        place(obj, drum_matrix())
        lighten(obj, DRUM_LIGHTEN)
    for obj in append_objects(BOTTLE, ["Mesh_GasBottle_Flask"], coll):
        place(obj, bottle_matrix())
        lighten(obj, BOTTLE_LIGHTEN)
    for obj in append_objects(DART, ["Mesh_GrappleHarpoon"], coll):
        place(obj, harpoon_matrix())
        lighten(obj, HARPOON_LIGHTEN)
        # Origin at the tail (breech end): the rearmost vertex on the axis.
        # `matrix_world` is stale right after `place`; location + local is not.
        tail_y = obj.location.y + max(v.co.y for v in obj.data.vertices)
        set_origin(obj, (0.0, tail_y, AXIS_Z))

    muzzle(coll)

    save(out)
    report()
    device = sum(sum(len(f.vertices) - 2 for f in o.data.polygons)
                 for o in bpy.data.objects
                 if o.type == 'MESH' and not o.name.startswith("Mesh_GauntletBase_"))
    print("  DEVICE TRIS: %d" % device)
    print("  muzzle at %s, harpoon origin at %s, HARPOON_K %.3f"
          % (MUZZLE,
             tuple(round(v, 4) for v in bpy.data.objects["Mesh_GrappleHarpoon"].location),
             HARPOON_K))


if __name__ == "__main__":
    main()
