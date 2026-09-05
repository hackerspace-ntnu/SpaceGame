"""Gauntlet Base — the armoured forearm sleeve every worn gauntlet is built on.

    blender --background --python gauntlet_base.py -- --out gauntlet_base.blend

One base, three variations, one frame, true suit scale. Every gauntlet device
in `models/gear/gauntlet_*.py` appends one of these variations unchanged and
bolts its own machinery onto the hardpoint; nothing about the sleeve is ever
re-authored per device. That is what keeps six gauntlets reading as one kit on
the astronaut.

## Sized to the suit, not to a hand

Measured off `PlayerCharacter.prefab` (2026-09-02): forearm 0.404 m from elbow
to wrist joint, sleeve radius ~0.105 m mid-forearm, ~0.09 at the wrist,
~0.135 at the elbow; glove radius 0.17 at the knuckles. The sleeve here is
modelled AT that scale — `GauntletFit.cuffScale` / `lengthScale` are 1.0 for
this family — so the numbers below are the metres the player sees.

## Frame

The gauntlet family frame (`models/gear/_gauntlet.py`): **arm along +Y, wrist
joint at y = 0, elbow toward +Y, forward −Y, dorsal +Z, thumb +X on a right
forearm** (verified against the rig's knuckle flexion on 2026-09-02). `_exportlib` maps Blender `(x, y, z)` onto Unity `(−x, z, −y)`, and
`BodyEquipmentController.WearOnForearm` puts this origin at the wrist bone with
−Z(Unity) up the arm and +Y(Unity) on the back of the arm. The left arm is the
same model at a negative X scale.

## The three variations

| Collection | What it is | Use it for |
|---|---|---|
| `Coll_GauntletBase_Plain` | undersleeve, dorsal + ventral shells, collar, hinges, latches | anything that wraps the arm itself |
| `Coll_GauntletBase_Mount` | Plain + the dorsal hardpoint deck with four bolt bosses | most devices: winches, scanners, emitters |
| `Coll_GauntletBase_Rail`  | Mount + two rails along the deck, reaching past the wrist | anything that slides: the steam ram |

Objects are `Mesh_GauntletBase_<Part>_<Variant>`, one set per collection, so a
device appends one collection's objects by name and never two copies of a
sleeve.

## The hardpoint contract (what an extension builds against)

- The deck top is the plane **z = DECK_Z (0.250)**, flat, spanning
  x ±DECK_HX (0.070) and y DECK_Y0..DECK_Y1 (0.100..0.320).
- Bolt bosses sit at the deck's four corners, inset BOSS_INSET; a device that
  covers a corner should sink its foot 2 mm into the deck rather than sit on a
  boss.
- Rails (Rail variant only) run at x = ±RAIL_X (0.048), top at RAIL_Z
  (0.272), from y = RAIL_Y0 (0.090) to RAIL_Y1 (0.330).
- Nothing on the base rises above DECK_Z except the bosses (+0.004) and the
  rails, and nothing crosses y < 0, so a device knows the glove starts at the
  origin. The shells' outline at any station is `profile()` — a device that
  wraps the arm (a fairlead, a strap) should be built against it.

## Why it looks the way it does

Bulky and simple, by request, and built the way a bracer is built: a dark
rubber undersleeve with two thick armour shells clamped over it — a dorsal
shell over the back of the arm and a ventral shell under it — with a gap down
each side where the undersleeve shows, bridged by hinge plates on the thumb
side and latch plates on the little-finger side. A rounded wrist collar in
the suit's armour orange closes the wrist end. The first cut was a plain
closed tube with strap rings and it read as a pipe coupling; the split shells
are what make it read as armour.

The cross-section is a squircle (superellipse, exponent 2.5), wider than tall,
and its bulk grows on the UNDERSIDE toward the elbow — the flexor mass, where a
forearm actually thickens — while the dorsal top runs nearly flat. That is
what lets the hardpoint be a flat deck sitting flush on the shell instead of a
wedge floating over a taper.

Materials are the suit's: its grey (`Mat_Neutral_Panel_Grey` against the
suit's #6E676C) for the shells, its armour orange (`Mat_Paint_Safety_Orange`
against #CC2F02) for the collar, its dark hardware (`Mat_Metal_Steel_Dark`
against #444444) for deck, rails, hinges and latches, and rubber black for the
undersleeve, like the gloves (#151515).

## Geometry rules honoured

Every part is its own object. Collar, deck, hinge and latch plates, rails and
bosses are embedded 2-5 mm into whatever they sit on — never coplanar. Each
shell is a closed loft of a C-shaped ring profile, so it has real thickness
with rounded rims, and reads as armour rather than a skin.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)

import bpy  # noqa: E402

from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402

from mathutils import Vector  # noqa: E402

# Index 0 is a metal because `bmesh.ops.bevel` stamps new faces with index 0.
DARK, GREY, ORANGE, CHROME, BLACK, RUBBER = range(6)
MATS = ["Mat_Metal_Steel_Dark",      # hardpoint, rails, hinges, latches
        "Mat_Neutral_Panel_Grey",    # the armour shells — the suit's own grey
        "Mat_Paint_Safety_Orange",   # wrist collar — the suit's armour orange
        "Mat_Metal_Chrome_Scuffed",  # bolt bosses, hinge pins
        "Mat_Neutral_Black_Matte",   # latch slots
        "Mat_Plastic_Rubber_Black"]  # the undersleeve

BEVEL_W = 0.005
SQUIRCLE = 2.5                       # superellipse exponent: 2 is an ellipse, 4 is near-square

# ── The forearm outline ──────────────────────────────────────────────────────
# (y, cx, cz, half-width, dorsal height, ventral depth) of the OUTER shell
# surface, with (cx, cz) the section's centre relative to the forearm BONE.
#
# Measured 2026-09-02 off the skinned suit in `PlayerCharacter.prefab`: every
# vertex bound at least half to the forearm or hand bone, binned 3 cm along the
# arm and 15 degrees around it, both arms unioned. The suit's forearm is
# 0.17-0.18 m from the bone on the sides, 0.12-0.15 on the palm side and up to
# 0.22 on the BACK toward the elbow — the padded back of the sleeve — so the
# section centre drifts 4.5 cm dorsal-ward. Radii here are that envelope plus
# 12 mm. The back line is held flat at z = TOP from the first band up, so the
# hardpoint sits flush along its whole length instead of chasing the skin; it
# ramps up to TOP under the collar.
TOP = 0.236
STATIONS = [
    (0.040, -0.015, 0.000, 0.186, 0.200, 0.192),
    (0.100, -0.015, 0.015, 0.186, TOP - 0.015, 0.185),
    (0.160, -0.014, 0.036, 0.187, TOP - 0.036, 0.184),
    (0.220, -0.015, 0.044, 0.187, TOP - 0.044, 0.176),
    (0.280, -0.018, 0.044, 0.184, TOP - 0.044, 0.176),
    (0.330, -0.012, 0.045, 0.184, TOP - 0.045, 0.183),
    (0.343, -0.012, 0.045, 0.190, TOP - 0.045 + 0.006, 0.189),    # elbow lip
    (0.352, -0.012, 0.045, 0.186, TOP - 0.045 + 0.002, 0.185),
]
WALL = 0.020                         # shell thickness — what makes it armour
Y_WRIST_END = STATIONS[0][0]
Y_ELBOW_END = STATIONS[-1][0]

# Shell arcs, in degrees from +X (thumb side) toward +Z (dorsal). The gaps at
# 0 and 180 degrees are where the undersleeve shows and the plates bridge.
DORSAL_ARC = (12.0, 168.0)
VENTRAL_ARC = (192.0, 348.0)
ARC_STEPS = 16

# The undersleeve: 3 mm inside the shells, 10 mm thick, a finger past both rims.
SLEEVE_INSET, SLEEVE_T = 0.003, 0.012
# The wrist end stands 8 mm PROUD of the collar, not flush with it: at the
# collar's own 0.030 the two rings' end caps are coplanar and flicker right
# at the cuff opening, which is the part of the gauntlet the camera sees
# most. Proud also reads better — rubber showing past the armour, as a
# bracer's liner does.
SLEEVE_Y0, SLEEVE_Y1 = 0.022, 0.360

# The collar: a rounded orange ring closing the wrist end, 8 mm proud.
COLLAR_Y0, COLLAR_Y1, COLLAR_PROUD, COLLAR_T = 0.030, 0.085, 0.006, 0.030

# Hinge (thumb, +X) and latch (little finger, -X) plates bridging the side gaps.
PLATE_Y = [0.130, 0.290]
PLATE_LEN, PLATE_HZ, PLATE_T = 0.070, 0.055, 0.010
PIN_R = 0.012

# ── The hardpoint ────────────────────────────────────────────────────────────
DECK_Z = TOP + 0.014
DECK_HX = 0.070
DECK_Y0, DECK_Y1 = 0.100, 0.320
DECK_FLOOR = TOP - 0.012             # inside the dorsal shell wall at every station
BOSS_INSET = 0.014
BOSS_R, BOSS_H = 0.007, 0.004

# ── The rails (Rail variant) ─────────────────────────────────────────────────
RAIL_X = 0.048
RAIL_W, RAIL_H = 0.014, 0.022
RAIL_Y0, RAIL_Y1 = 0.090, 0.330
RAIL_Z = DECK_Z + RAIL_H             # rail top


def station(y):
    """(cx, cz, half-width, dorsal height, ventral depth) of the outer shell at y."""
    if y <= STATIONS[0][0]:
        return STATIONS[0][1:]
    for s0, s1 in zip(STATIONS, STATIONS[1:]):
        if s0[0] <= y <= s1[0]:
            t = (y - s0[0]) / (s1[0] - s0[0])
            return tuple(a + (b - a) * t for a, b in zip(s0[1:], s1[1:]))
    return STATIONS[-1][1:]


def profile(theta_deg, st, inset=0.0):
    """A point on the squircle outline of station `st` at angle theta,
    `inset` metres inward. Angles run from +X toward +Z about the section's
    own centre (cx, cz), not the bone."""
    cx, cz, a, b_top, b_bot = st
    th = math.radians(theta_deg)
    c, s = math.cos(th), math.sin(th)
    p = 2.0 / SQUIRCLE
    x = math.copysign(abs(c) ** p, c) * (a - inset)
    b = (b_top if s >= 0 else b_bot) - inset
    z = math.copysign(abs(s) ** p, s) * b
    return (cx + x, cz + z)


def arc(th0, th1, st, inset=0.0, steps=ARC_STEPS):
    return [profile(th0 + (th1 - th0) * i / steps, st, inset)
            for i in range(steps + 1)]


def ring(st, inset=0.0, steps=ARC_STEPS * 2):
    return [profile(360.0 * i / steps, st, inset) for i in range(steps)]


def c_section(th0, th1, st, wall, inset=0.0):
    """A C-shaped closed ring: outer arc out, inner arc back."""
    outer = arc(th0, th1, st, inset)
    inner = arc(th0, th1, st, inset + wall)
    return outer + list(reversed(inner))


def shell(coll, mats, suffix, name, th0, th1):
    p = TrackedPart(mats)
    sections = [(st[0], c_section(th0, th1, st[1:], WALL)) for st in STATIONS]
    p.loft(sections, axis='Y', mat=GREY, cap=True)
    # Round the rims: only the 90-degree edges where a wall meets an end cap or
    # a side face qualify, the arcs are already smooth.
    #
    # The bevel's own faces keep material index 0 — `Mat_Metal_Steel_Dark` —
    # because `restamp` can only replay assignments that were recorded, and
    # bevel invents its faces afterwards. That is kept deliberately: a dark
    # chamfer all round a grey shell reads as a machined edge, and it is the
    # same dark the deck and the plates are in. Anything that wants a grey rim
    # has to bevel first and assign after.
    p.bevel(width=BEVEL_W, segments=2)
    p.restamp()
    return p.finish("Mesh_GauntletBase_%s_%s" % (name, suffix), coll)


def undersleeve(coll, mats, suffix):
    p = TrackedPart(mats)
    inset = WALL + SLEEVE_INSET
    sections = []
    for y in (SLEEVE_Y0, SLEEVE_Y1):
        st = station(y)
        sections.append((y, ring(st, inset), ring(st, inset + SLEEVE_T)))
    # Closed loop of sections: outer wrist -> outer elbow -> inner elbow ->
    # inner wrist -> back to outer wrist; finish() welds the seam.
    loop = [(sections[0][0], sections[0][1]), (sections[1][0], sections[1][1]),
            (sections[1][0], sections[1][2]), (sections[0][0], sections[0][2]),
            (sections[0][0], sections[0][1])]
    p.loft(loop, axis='Y', mat=RUBBER, cap=False)
    p.restamp()
    return p.finish("Mesh_GauntletBase_Undersleeve_" + suffix, coll)


def collar(coll, mats, suffix):
    p = TrackedPart(mats)
    st = station((COLLAR_Y0 + COLLAR_Y1) / 2)
    outer = ring(st, -COLLAR_PROUD)
    inner = ring(st, -COLLAR_PROUD + COLLAR_T)
    loop = [(COLLAR_Y0, outer), (COLLAR_Y1, outer), (COLLAR_Y1, inner),
            (COLLAR_Y0, inner), (COLLAR_Y0, outer)]
    p.loft(loop, axis='Y', mat=ORANGE, cap=False)
    p.bevel(width=0.009, segments=3)
    for f in p.bm.faces:             # orange through and through
        f.material_index = ORANGE
    return p.finish("Mesh_GauntletBase_Collar_" + suffix, coll)


def side_plates(coll, mats, suffix):
    """Hinge plates on the thumb side, latch plates on the little-finger side."""
    out = []
    for i, y in enumerate(PLATE_Y):
        tag = ("Front", "Rear")[i]
        cx, cz, a, bt, bb = station(y)
        for sx, kind in ((1, "Hinge"), (-1, "Latch")):
            p = TrackedPart(mats)
            x_out = cx + sx * (a + PLATE_T - 0.004)  # 4 mm sunk into the shells
            x_in = cx + sx * (a - 0.004 - PLATE_T)
            p.slab((x_in, y - PLATE_LEN / 2, cz - PLATE_HZ), (x_out, y + PLATE_LEN / 2, cz + PLATE_HZ), DARK)
            if kind == "Hinge":
                p.cyl((x_out, y, cz), PIN_R, PLATE_LEN + 0.012, axis='Y', seg=12, mat=CHROME)
            else:
                # The latch tongue slot, a black recess on the plate face.
                p.box((x_out, y, cz), (0.004, 0.030, 0.008), BLACK)
            p.bevel(width=0.003, segments=1)
            p.restamp()
            out.append(p.finish("Mesh_GauntletBase_%s%s_%s" % (kind, tag, suffix), coll))
    return out


def mount(coll, mats, suffix):
    """The dorsal hardpoint: a chamfered deck sunk into the shell, four bosses."""
    p = TrackedPart(mats)
    p.slab((-DECK_HX, DECK_Y0, DECK_FLOOR), (DECK_HX, DECK_Y1, DECK_Z), DARK)
    p.bevel(width=0.010, segments=2)
    p.restamp()
    deck = p.finish("Mesh_GauntletBase_Deck_" + suffix, coll)

    q = TrackedPart(mats)
    for sx in (-1, 1):
        for y in (DECK_Y0 + BOSS_INSET, DECK_Y1 - BOSS_INSET):
            q.cyl((sx * (DECK_HX - BOSS_INSET), y, DECK_Z + BOSS_H / 2 - 0.001),
                  BOSS_R, BOSS_H + 0.002, axis='Z', seg=10, mat=CHROME)
    q.restamp()
    bosses = q.finish("Mesh_GauntletBase_Bosses_" + suffix, coll)
    return deck, bosses


def rails(coll, mats, suffix):
    out = []
    for i, sx in enumerate((-1, 1)):
        p = TrackedPart(mats)
        x = sx * RAIL_X
        p.slab((x - RAIL_W / 2, RAIL_Y0, DECK_Z - 0.003), (x + RAIL_W / 2, RAIL_Y1, RAIL_Z), DARK)
        p.bevel(width=0.003, segments=1)
        p.restamp()
        out.append(p.finish("Mesh_GauntletBase_Rail%s_%s" % (("Left", "Right")[i], suffix), coll))
    return out


def build_variant(name, mats, with_mount, with_rails):
    coll = collection("Coll_GauntletBase_" + name)
    undersleeve(coll, mats, name)
    shell(coll, mats, name, "DorsalShell", *DORSAL_ARC)
    shell(coll, mats, name, "VentralShell", *VENTRAL_ARC)
    collar(coll, mats, name)
    side_plates(coll, mats, name)
    if with_mount:
        mount(coll, mats, name)
    if with_rails:
        rails(coll, mats, name)
    return coll


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    build_variant("Plain", mats, with_mount=False, with_rails=False)
    build_variant("Mount", mats, with_mount=True, with_rails=False)
    build_variant("Rail", mats, with_mount=True, with_rails=True)

    save(out)
    report()
    print("  DECK_Z %.3f  deck x ±%.3f  y %.3f..%.3f  rails x ±%.3f top z %.3f"
          % (DECK_Z, DECK_HX, DECK_Y0, DECK_Y1, RAIL_X, RAIL_Z))


if __name__ == "__main__":
    main()
