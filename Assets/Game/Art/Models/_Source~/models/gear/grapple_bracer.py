"""Grapple Bracer — the forearm-mounted grappling hook.

Replaces the third-party pistol the Grappling Hook artifact used to wear. The
device is worn, not held: a webbing cuff round the forearm, a spine along the
back of it carrying a cable drum and a launch tube, a gas bottle on the outboard
flank, and a harpoon sitting in the tube waiting to be fired.

Assembly only, in the manner of `item_scanner.py`. Everything with a curved
surface on it comes from a component; what is authored here is the frame that
marries them.

| Object | Where it comes from |
|---|---|
| `Mesh_ArmCuff_Webbing`      | `components/props/arm_cuff.blend`, unchanged |
| `Mesh_CableDrum_Winch`      | `components/props/cable_drum.blend`, unchanged |
| `Mesh_GasBottle_Single`     | `components/props/gas_bottle.blend`, unchanged |
| `Mesh_GrappleHarpoon`       | `components/props/grapple_dart.blend`, scaled |
| `Mesh_GrappleBracer_Frame`  | the only geometry unique to this model |

Component names are kept rather than renamed, so the provenance of each piece is
readable straight off the outliner. Nothing in Unity binds by name — the prefab
wires serialized Transform references.


## The frame this is built in, and why it is not the component's

**Arm along Y, wrist at y = 0, elbow toward +Y, forward is −Y, dorsal is +Z.**

Forward is −Y because that is the library's standard and because
`_exportlib`'s FBX flags map Blender −Y onto **Unity +Z**, which is the axis
`ItemGrip` points an aimed item down. The harpoon already uses it, so a seated
harpoon and a flying one agree without a correction.

Dorsal is +Z, which after export lands on Unity +Y — the *thumb* side of the
hand frame, not the back of it. The bracer therefore ships with
`rotationOffset = (0, 0, -90)`, which rolls Unity +Y onto hand-frame +X, and
hand-frame +X is the back of the hand. That is one number in the prefab rather
than a whole model authored around a non-obvious axis; see the BUILD record for
the derivation.

The cuff is authored wrist-at-origin running up its own +Z, so it arrives here
through `R_x(-90) @ R_z(-90)`. The roll is not cosmetic: it puts the cuff's
mounting boss under the spine and its buckles out on the −X flank. Without it
the buckles land on top, exactly where the spine has to sit.


## Scale

Authored at real human scale, like every other item in this library, and worn
at **2.1x** — set in the prefab as `holdSize`, and the same multiplier
`lasso_coil` needed for the same reason. This rig is stylistically oversized:
its forearm is 0.393 m against roughly 0.26 m on a real person, so a true-scale
bracer is a bangle on it. 2.1 is what makes the cuff's 0.091 x 0.110 elbow
section cover the suit's ~0.19 m forearm.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "props"))
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))
sys.path.insert(0, _HERE)

from _buildlib import *  # noqa: E402,F403
from cable_drum import TrackedPart  # noqa: E402
from item_scanner import append_objects, place  # noqa: E402
from panel_control import tube_path  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

PROPS = os.path.join(_LIB, "components", "props")
CUFF = os.path.join(PROPS, "arm_cuff.blend")
DRUM = os.path.join(PROPS, "cable_drum.blend")
BOTTLE = os.path.join(PROPS, "gas_bottle.blend")
DART = os.path.join(PROPS, "grapple_dart.blend")

STEEL, DARK, PALE, ORANGE, CHROME, RUBBER, BLACK, BRASS, AMBER, WARN = range(10)
MATS = ["Mat_Metal_Steel_Worn",        # spine, cheeks, launch tube, clamps
        "Mat_Metal_Steel_Dark",        # machined collars, control pod
        "Mat_Paint_Hull_Bleached",     # painted panels on the spine
        "Mat_Paint_Safety_Orange",     # muzzle band
        "Mat_Metal_Chrome_Scuffed",    # cable, fairlead, fasteners
        "Mat_Plastic_Rubber_Black",    # cuff pads under the clamps
        "Mat_Neutral_Black_Matte",     # shadow gaps
        "Mat_Metal_Brass_Tarnished",   # bearing collars
        "Mat_Emissive_Amber",          # the two ready lamps
        "Mat_Paint_Warn_Red"]          # arming stripe

BEVEL_W = 0.0016

# --- the layout, as a table -------------------------------------------------
#
# Read down the arm from the elbow. Every number below is a Y unless it says
# otherwise, and every one of them is a real constraint rather than a taste:
# the drum has to clear the spine, the launch tube has to clear the drum, and
# the harpoon's barbs have to sit forward of the fairlead or the ring is inside
# the barb spread.
#
# The Z figures were raised 10 mm late in the build. At the first set the
# mechanism sat on the cuff, which is correct for the cuff and wrong for the
# arm: the astronaut's suit sleeve is a good deal fatter than the sleeve the
# cuff component was authored to, and the drum and the launch tube came out
# half-sunk in it. 10 mm here is 21 mm on the worn device, which is the
# daylight between the frame and the suit.
Y_ELBOW = 0.2050        # back of the cuff
Y_WRIST = 0.0060        # front of the cuff — the arm axis crosses it here
SPINE_Y0, SPINE_Y1 = -0.0300, 0.2000
SPINE_Z0, SPINE_Z1 = 0.0560, 0.0760     # channel floor and rail tops

DRUM_AT = (0.0, 0.1400, 0.1085)         # bottom lands at 0.075, over the spine
CHEEK_X = 0.0448                        # inside faces of the axle plates

TUBE_Z = 0.0880                         # the harpoon's centreline
TUBE_Y0, TUBE_Y1 = -0.0620, 0.0620      # muzzle and breech
TUBE_R = 0.0135

HARPOON_K = 0.2800                      # 0.9435 m -> 0.264 m seated
HARPOON_EYE = (0.0, 0.0750, TUBE_Z)     # rope eye, just behind the breech

FAIRLEAD = (0.0, -0.0700, 0.1040)       # the rope pays out from here

BOTTLE_K = 0.8500
BOTTLE_BASE = (0.0500, 0.1780, 0.0460)  # base of the bottle, outboard flank

# Where the spine clamps onto the cuff: (station y, half-extent in x,
# half-extent in z), read off `arm_cuff.SLEEVE`. The cuff tapers, so a band
# written at a fixed radius is off the sleeve at one end and inside it at the
# other — the same trap `arm_cuff._at` exists to dodge.
#
# Two stations, not the sleeve's three: the cuff already carries three canvas
# bands of its own, and a steel clamp over every one of them turns the sleeve
# into a barcode with no arm visible between the rings.
#
# The rear station is 0.150 rather than the sleeve's last band at 0.192: the
# suit balloons into an elbow pad over the last third of the forearm — 0.17 m
# of radius against 0.11 at mid-arm — and a band placed back there is inside
# the pad no matter how far it stands off the cuff.
CLAMPS = [(0.0300, 0.0320, 0.0390),
          (0.1500, 0.0429, 0.0519)]

# How far the clamp bands stand off the cuff, and the reason they stand off it
# at all: **the cuff is mostly invisible on the wearer.** The astronaut's suit
# sleeve is fatter than the sleeve `arm_cuff` was authored for, so the webbing
# sinks into it and what should read as the device's arm frame reads as
# nothing. The bands are mine, they are sized off the cuff rather than being
# part of it, and 10.5 mm here is 22 mm on the worn device — enough that two
# steel hoops sit visibly around the suit and carry the read the cuff cannot.
BAND_STANDOFF = 0.0200


# ---------------------------------------------------------------------------
# The frame — the only geometry unique to this model
# ---------------------------------------------------------------------------

def _clamp_band(p, y, hx, hz, arc0=60.0, arc1=-240.0, count=15):
    """A steel band round the cuff at one station, open at the top.

    It runs from 60 degrees the long way round the bottom to -240, so both
    ends finish beside the spine's side rails and the 60-degree gap is the arc
    the channel already occupies. Closing the ring instead buries a sixth of
    the band inside the spine, which costs triangles and leaves interior faces
    exactly where two parts meet.

    Each segment is laid by `R_y(90 - a)`, which is the rotation that sends the
    box's local +Z along the radius and its local +X along the tangent — so
    `size` reads (arc step, band width, band thickness). Using `atan2(z, x)`
    instead, which is the obvious thing to write, sends the tangent along the
    radius: the band comes out as a ring of splayed teeth pointing away from
    the arm, and it looks like a modelling accident because it is one.
    """
    # The segments are deliberately NOT returned for bevelling. Thirty
    # 4.4 mm blocks at two segments each is the single largest line in this
    # model's triangle budget, and a 1.6 mm chamfer on a band that reads as one
    # continuous ring is invisible.
    hard = []
    step = abs(math.radians((arc1 - arc0) / (count - 1)))
    for i in range(count):
        a = math.radians(arc0 + (arc1 - arc0) * i / (count - 1))
        # Elliptical, not circular: the cuff is 0.091 across and 0.110 deep at
        # the elbow, and a circular band fits neither dimension.
        x, z = math.cos(a) * (hx + BAND_STANDOFF), math.sin(a) * (hz + BAND_STANDOFF)
        p.box((x, y, z), (max(hx, hz) * step * 1.55, 0.0145, 0.0055), STEEL,
              rot=Matrix.Rotation(math.pi / 2 - a, 4, 'Y'))
    # Lugs bolting each end of the band to the spine's side rails.
    for sx in (-1, 1):
        hard += p.box((sx * 0.0175, y, SPINE_Z0 + 0.0050),
                      (0.0060, 0.0150, 0.0140), STEEL)
        p.cyl((sx * 0.0210, y, SPINE_Z0 + 0.0050), 0.0028, 0.0040, 'X', 8,
              CHROME)
    return hard


def frame(coll, mats):
    """Spine, axle cheeks, launch tube, muzzle, cable run and control pod."""
    p = TrackedPart(mats)
    hard = []

    # --- spine: a channel down the back of the cuff ------------------------
    hard += p.slab((-0.0180, SPINE_Y0, SPINE_Z0), (0.0180, SPINE_Y1, 0.0610),
                   STEEL)
    for sx in (-1, 1):
        hard += p.slab((sx * 0.0130, SPINE_Y0, SPINE_Z0),
                       (sx * 0.0180, SPINE_Y1, SPINE_Z1), STEEL)
    # Painted top panel, inset, so 0.23 m of channel is not one bare surface.
    hard += p.slab((-0.0120, 0.0300, 0.0610), (0.0120, 0.1750, 0.0635), PALE)
    p.rivets((0.0, 0.0400, 0.0640), (0.0, 0.1700, 0.0640), 7, radius=0.0022,
             height=0.0022, axis='Z', mat=CHROME)

    # --- clamps holding the spine onto the cuff ----------------------------
    for y, hx, hz in CLAMPS:
        hard += _clamp_band(p, y, hx, hz)
        hard += p.box((0.0, y, SPINE_Z0 - 0.0025), (0.0420, 0.0150, 0.0040),
                      RUBBER)

    # --- axle cheeks carrying the drum -------------------------------------
    # prism(axis='X') maps a profile (u, v) onto (y, z) and extrudes along X.
    # Written down rather than guessed: that mapping is trap 4 in
    # item_devices_BUILD.md and guessing it puts the plate on the wrong side.
    # A narrow strut, not a full side plate.
    #
    # The plate came first and it worked mechanically and failed completely as
    # a picture: a 0.094 m hexagon either side of a 0.066 m drum hides the
    # wound cable from every angle except straight down, and the coil of rope
    # is one of the three things this device is supposed to show. 0.040 m of
    # strut carries the same axle and leaves three quarters of the drum out in
    # the open.
    cheek = [(0.1200, 0.0570), (0.1600, 0.0570), (0.1600, 0.1220),
             (0.1490, 0.1350), (0.1310, 0.1350), (0.1200, 0.1220)]
    for sx in (-1, 1):
        hard += p.prism(cheek, 0.0060, axis='X', mat=STEEL,
                        offset=(sx * (CHEEK_X + 0.0030), 0, 0))
        p.cyl((sx * (CHEEK_X + 0.0030), DRUM_AT[1], DRUM_AT[2]), 0.0110,
              0.0080, 'X', 14, DARK)
        p.torus((sx * (CHEEK_X + 0.0062), DRUM_AT[1], DRUM_AT[2]), 0.0082,
                0.0020, 'X', 14, 6, BRASS)
    # Tie bar across the top of the two cheeks.
    hard += p.slab((-CHEEK_X, 0.1620, 0.1430), (CHEEK_X, 0.1830, 0.1500),
                   STEEL)

    # --- bottle cradle -----------------------------------------------------
    # Two arms reaching over the cuff's shoulder from the spine's side rail,
    # each ending in a clamp ring round the bottle. Without them the tank hangs
    # in space beside the arm, which is the one thing on this model that read
    # as broken rather than as unfinished.
    for y in (0.0980, 0.1600):
        hard += p.box((0.0345, y, 0.0530), (0.0350, 0.0110, 0.0060), STEEL,
                      rot=Matrix.Rotation(math.radians(19), 4, 'Y'))
        p.torus((BOTTLE_BASE[0], y, BOTTLE_BASE[2]), 0.0152, 0.0026, 'Y', 14,
                6, STEEL)
        p.cyl((BOTTLE_BASE[0], y, BOTTLE_BASE[2] + 0.0170), 0.0028, 0.0060,
              'Z', 8, CHROME)

    # --- launch tube -------------------------------------------------------
    # A closed tube rather than an open cradle. The harpoon's foregrip collar
    # is 8.2 mm at this scale and its shaft 4.9 mm, so a channel deep enough to
    # hold the collar leaves the shaft floating in mid-air above the floor.
    # A tube holds both, hides the plain half of the harpoon, and leaves the
    # head — the only part worth looking at — standing out of the muzzle.
    p.tube((0.0, (TUBE_Y0 + TUBE_Y1) / 2, TUBE_Z), TUBE_R, 0.0035,
           TUBE_Y1 - TUBE_Y0, 'Y', 16, STEEL)
    for y in (-0.0400, 0.0000, 0.0400):
        p.torus((0.0, y, TUBE_Z), TUBE_R + 0.0018, 0.0026, 'Y', 16, 6, STEEL)

    # Legs down to the spine. Two pairs, so the tube is carried rather than
    # levitating over the channel it is 10 mm clear of.
    for y in (-0.0300, 0.0480):
        for sx in (-1, 1):
            hard += p.box((sx * 0.0110, y, (SPINE_Z1 + TUBE_Z - TUBE_R) / 2),
                          (0.0050, 0.0130,
                           TUBE_Z - TUBE_R - SPINE_Z1 + 0.0060), STEEL)

    # --- muzzle: collar, high-vis band, fairlead ---------------------------
    p.cyl((0.0, TUBE_Y0 + 0.0075, TUBE_Z), TUBE_R + 0.0030, 0.0150, 'Y', 16,
          DARK)
    p.torus((0.0, TUBE_Y0 + 0.0020, TUBE_Z), TUBE_R + 0.0038, 0.0026, 'Y', 16,
            6, ORANGE)
    hard += p.box((0.0, TUBE_Y0 + 0.0080, TUBE_Z + TUBE_R + 0.0085),
                  (0.0150, 0.0130, 0.0110), STEEL)
    p.torus(FAIRLEAD, 0.0080, 0.0026, 'Y', 16, 6, CHROME)

    # --- cable: drum to fairlead ------------------------------------------
    # Picks up where `cable_drum`'s own lead-off tail stops, at the drum's
    # front-bottom, so the two read as one continuous run rather than as a
    # spool with a stub and a rope that starts from nowhere.
    #
    # Routed just clear of the launch tube's crown (0.0915) rather than
    # straight from the drum to the ring: a two-point run at this length is a
    # rod, and it read as a diagonal strut bracing the frame.
    lead = (DRUM_AT[0], DRUM_AT[1] - 0.0520, DRUM_AT[2] + 0.0230)
    tube_path(p, [lead, (0.0, 0.0700, 0.1185), (0.0, 0.0400, 0.1090),
                  (0.0, 0.0000, 0.1055), (0.0, -0.0400, 0.1045), FAIRLEAD],
              0.0030, CHROME, seg=6)

    # --- control pod on the spine, by the wrist ----------------------------
    hard += p.slab((-0.0170, -0.0260, SPINE_Z1), (0.0170, 0.0140, 0.0890),
                   DARK)
    hard += p.box((0.0, -0.0270, 0.0800), (0.0300, 0.0060, 0.0140), WARN)
    for sx in (-1, 1):
        p.cyl((sx * 0.0080, -0.0055, 0.0900), 0.0035, 0.0030, 'Z', 10, AMBER)
    p.rivets((-0.0130, 0.0120, 0.0900), (0.0130, 0.0120, 0.0900), 3,
             radius=0.0020, height=0.0020, axis='Z', mat=CHROME)

    print("  Mesh_GrappleBracer_Frame: %d face(s) re-stamped" % p.restamp())
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish("Mesh_GrappleBracer_Frame", coll)


# ---------------------------------------------------------------------------
# Placement
# ---------------------------------------------------------------------------

def cuff_matrix():
    """Cuff local (wrist at origin, running up +Z) into the assembly frame.

    `R_z(-90)` first, so the cuff's mounting boss ends up on top under the
    spine and its buckles out on the -X flank; then `R_x(-90)` to lay the
    sleeve down the arm. Applied in that order — swapping them puts the
    buckles under the spine, where they are both invisible and intersecting.
    """
    return (Matrix.Rotation(math.radians(-90), 4, 'X')
            @ Matrix.Rotation(math.radians(-90), 4, 'Z'))


def bottle_matrix():
    """Bottle local (base at origin, standing up +Z) onto the outboard flank.

    `R_z(135)` rolls the instrument before the bottle is laid down, so the dial
    ends up facing up and outboard — where a wearer glancing at their own
    forearm can read it. Rolling after the lay-down aims it at the sky.

    `R_x(90)` then maps bottle +Z onto assembly -Y: the base sits at the elbow
    and the valve, with the gauge on it, points at the wrist.
    """
    return (Matrix.Translation(Vector(BOTTLE_BASE))
            @ Matrix.Rotation(math.radians(90), 4, 'X')
            @ Matrix.Rotation(math.radians(135), 4, 'Z')
            @ Matrix.Diagonal(Vector((BOTTLE_K,) * 3)).to_4x4())


def harpoon_matrix():
    """The harpoon, shrunk and slid into the tube, eye first.

    No rotation: the component is already authored tip-down-−Y, which is this
    model's forward. The only transform is the scale and the slide.

    `HARPOON_K` is not a free parameter. Whatever it is, the artifact's
    `hookHeadScale` must be `HARPOON_K * holdSize / longest-axis` — the same
    0.588 — or the harpoon that leaves the arm is a different size from the one
    that was sitting in it a frame earlier.
    """
    return (Matrix.Translation(Vector(HARPOON_EYE))
            @ Matrix.Diagonal(Vector((HARPOON_K,) * 3)).to_4x4())


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_GrappleBracer")

    for obj in append_objects(CUFF, ["Mesh_ArmCuff_Webbing"], coll):
        place(obj, cuff_matrix())
    for obj in append_objects(DRUM, ["Mesh_CableDrum_Winch"], coll):
        place(obj, Matrix.Translation(Vector(DRUM_AT)))
    for obj in append_objects(BOTTLE, ["Mesh_GasBottle_Single"], coll):
        place(obj, bottle_matrix())
    for obj in append_objects(DART, ["Mesh_GrappleHarpoon"], coll):
        place(obj, harpoon_matrix())

    frame(coll, mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
