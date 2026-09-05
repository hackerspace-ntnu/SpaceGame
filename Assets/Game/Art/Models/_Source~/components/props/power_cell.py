"""Power cell — the swappable battery the whole game runs on.

>>> HAND-EDITED. `power_cell.blend` is the source of truth and carries edits
>>> that exist nowhere else. NEVER re-run this script over it — see the build
>>> record next to it for what the hand edits were.


Same language as `supply_canister.py`: a pale enamel shell, ONE saturated
accent, chunky rubber corners, and a readout that means something. Three
variations, differing in silhouette rather than in trim:

    Coll_PowerCell_Slab      0.52 x 0.13 x 0.22  green   the big two-handed
                                                         brick that docks in the
                                                         oxygen generator
    Coll_PowerCell_Compact   0.26 x 0.10 x 0.18  blue    half-length, one hand,
                                                         a folding bar handle
    Coll_PowerCell_Drum      O 0.16 x 0.30       yellow  cylindrical, bayonet
                                                         collar, stands on end

The charge ladder is five bars, not a colour
--------------------------------------------
Charge reads as a COUNT of lit segments, so it survives a colour-blind player,
a dark room and a distant glance; the green is confirmation, never the message
(GDC-L1-UX-0003 explicitly: never encode critical information in colour alone).
Three of five are lit in the rest pose, so the part looks like a gauge at a
value rather than like a lamp that is simply on.

Orientation and origin
----------------------
A cell's FLAT BACK is its connector face. That face lies in XZ at y = 0, the
body grows toward -Y, and the origin is the centre of the back — the point a
dock mates it by, and the one measurement a cradle has to agree with. The long
axis is X.

    blender --background --python power_cell.py -- --out power_cell.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
sys.path.insert(0, os.path.join(LIB, "components", "mechanical"))
from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from _tracked import TrackedPart  # noqa: E402
from panel_control import connector_strip, tube_path  # noqa: E402

# Indices 0-9 are `panel_control.MATS` position for position so its builders can
# be called against this list; 10-15 are the canister family's, matched to
# `supply_canister.MATS` so parts from both files can share one material list
# when a model appends them together. Index 0 is structural steel because
# `bmesh.ops.bevel` stamps every edge it creates with it.
(STEEL, DARK, RUBBER, CHROME, CREAM, RED, BLUE, AMBER, BLACK, CRT,
 SHELL, GREY, ORANGE, YELLOW, GREEN, SLATE) = range(16)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Cream_Aged", "Mat_Paint_Warn_Red",
        "Mat_Paint_Blue_Station", "Mat_Emissive_Amber",
        "Mat_Neutral_Black_Matte", "Mat_Emissive_Green_CRT",
        "Mat_Paint_White_Arctic", "Mat_Neutral_Panel_Grey",
        "Mat_Paint_Safety_Orange", "Mat_Plastic_Safety_Yellow",
        "Mat_Paint_Cell_Green", "Mat_Neutral_Slate_Dark"]

# Wide, matching `supply_canister`: the art style's chamfer is a
# read-at-distance feature, not a technical edge break.
BEVEL_W = 0.005

# --- the docking interface -------------------------------------------------
# `components/mechanical/dock_cradle.py` imports these rather than repeating the
# numbers. Two copies of one measurement is how a cell ends up floating 4 mm off
# its shoe with nothing in either file looking wrong.
SLAB_W, SLAB_D, SLAB_H = 0.520, 0.130, 0.220
# ONE small rectangular charging port on the flat back, centred. It replaced a
# pair of blade contacts either side of a 30 mm round locating peg: at the size
# a cell is actually seen the peg was the biggest thing on the back of the
# object and read as a nozzle, and three separate fittings made a plain face
# busy for no gain. A single rectangle says 'this plugs in' with one shape.
PORT = (0.110, 0.016, 0.052)    # port block; Y is how far it stands off the back
PORT_PINS = 4


# ---------------------------------------------------------------------------
# Shared parts
# ---------------------------------------------------------------------------

def shell(p, w, d, h, mat_accent):
    """The body: pale slab, mid-grey plinth, a sunk seam all the way round.

    `w` x `d` x `h` is the full block, centred on x, sitting on the back plane
    at y = 0 and rising from z = 0. The grey plinth on the bottom fifth is the
    same trick the canister's sleeve plays — it divides a plain box where the
    eye wants a division, and it is why this does not read as a suitcase.
    """
    hard = p.slab((-w / 2, -d, 0.0), (w / 2, 0.0, h), SHELL)
    hard += p.slab((-w / 2 - 0.004, -d - 0.003, -0.005),
                   (w / 2 + 0.004, 0.0, h * 0.20), GREY)
    hard += p.slab((-w / 2 - 0.004, -d - 0.003, h * 0.86),
                   (w / 2 + 0.004, 0.0, h * 0.93), mat_accent)
    # Sunk seam: a dark band 3 mm INSIDE the shell, so it reads as a shut line
    # between two mouldings and not as a stripe painted on one.
    p.slab((-w / 2 + 0.003, -d + 0.003, h * 0.52),
           (w / 2 - 0.003, -0.003, h * 0.56), BLACK)
    return hard


def bumpers(p, w, d, h, ribs=4):
    """Ribbed rubber caps wrapping both short ends.

    The corners are what a dropped battery lands on, so they are the part that
    should look like it survives it. Each cap is 6 mm proud of the shell on
    three sides and buried 30 mm into it, which also gives the silhouette its
    stepped ends.
    """
    hard = []
    for s in (-1, 1):
        # 12 mm PROUD of the shell's end, not flush with it: flush put the
        # bumper's outer face on the shell's own end plane and the pair flickered.
        x = s * (w / 2 - 0.003)
        hard += p.box((x, -d / 2, h / 2), (0.030, d + 0.012, h + 0.012), RUBBER)
        for i in range(ribs):
            z = h * (0.18 + 0.64 * i / max(1, ribs - 1))
            hard += p.box((x + s * 0.014, -d / 2, z), (0.008, d + 0.020, 0.016),
                          RUBBER)
    return hard


def face_panel(p, w, d, h, mat_accent, lit=3, bars=5):
    """The accent plate on the front, carrying the charge ladder.

    Sits 5 mm proud of the shell on a black surround that is itself 2 mm proud —
    two steps rather than one, because a single plate flush on a flat face is
    the classic coplanar pair that flickers.
    """
    y = -d
    pw, ph = w * 0.62, h * 0.52
    zc = h * 0.54
    hard = p.box((0, y - 0.002, zc), (pw + 0.016, 0.010, ph + 0.016), BLACK)
    hard += p.box((0, y - 0.005, zc), (pw, 0.012, ph), mat_accent)
    # Charge ladder: five slots, three of them lit. A COUNT, not a colour.
    slot_w = pw * 0.13
    for i in range(bars):
        x = (i - (bars - 1) / 2.0) * pw * 0.155
        p.box((x, y - 0.010, zc + ph * 0.16), (slot_w + 0.005, 0.008,
                                               ph * 0.34 + 0.005), SLATE)
        p.box((x, y - 0.013, zc + ph * 0.16), (slot_w, 0.006, ph * 0.34),
              CRT if i < lit else BLACK)
    # Legend strip under the ladder — a moulded label, so the plate is not just
    # a lamp holder.
    p.box((0, y - 0.011, zc - ph * 0.28), (pw * 0.54, 0.006, ph * 0.16), SLATE)
    for sx in (-1, 1):
        p.cyl((sx * (pw / 2 + 0.011), y - 0.004, zc), 0.0060, 0.012, 'Y', 8,
              CHROME)
    return hard


def contacts(p, port=PORT, z=SLAB_H * 0.5, pins=PORT_PINS):
    """The charging port: one small rectangle on the flat back.

    Keyed by being off-centre in nothing and rectangular in outline — it enters
    its socket one way up, which is the whole of what the shape has to say
    (GDC-L1-UX-0004). The pins inside it are the only detail, and they are what
    stops the block reading as a bumper.
    """
    hard = p.box((0, port[1] / 2, z), port, DARK)
    hard += p.box((0, port[1] * 0.30, z),
                  (port[0] + 0.014, port[1] * 0.6, port[2] + 0.014), GREY)
    for i in range(pins):
        x = (i - (pins - 1) / 2.0) * port[0] * 0.21
        p.box((x, port[1] - 0.003, z), (0.010, 0.008, port[2] * 0.52), CHROME)
    return hard


def strap(p, w, h, mat_accent):
    """Recessed carry strap across the top, on two anchor lugs.

    Recessed rather than sitting on the lid: a handle standing proud of a
    battery that spends its life sliding into a slot would be the first thing to
    snap off, and the recess is what tells the player it slides.
    """
    hard = []
    for s in (-1, 1):
        hard += p.box((s * w * 0.30, -0.020, h - 0.008), (0.030, 0.052, 0.020),
                      DARK)
    tube_path(p, [(-w * 0.30, -0.020, h - 0.004),
                  (-w * 0.22, -0.020, h + 0.012),
                  (w * 0.22, -0.020, h + 0.012),
                  (w * 0.30, -0.020, h - 0.004)], 0.0085, RUBBER, seg=8)
    p.box((0, -0.020, h + 0.014), (w * 0.16, 0.040, 0.012), mat_accent)
    return hard


def latch(p, w, h, mat_accent, at_x=0.0):
    """Thumb latch on the top face — the part a hand pushes to release."""
    hard = p.box((at_x, -0.034, h - 0.006), (0.084, 0.060, 0.028), DARK)
    hard += p.box((at_x, -0.040, h + 0.006), (0.062, 0.044, 0.020), mat_accent)
    for sx in (-1, 1):
        p.cyl((at_x + sx * 0.030, -0.034, h + 0.012), 0.0055, 0.010, 'Z', 8,
              CHROME)
    return hard


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def _emit(p, hard, name, coll, origin=(0, 0, 0)):
    p.restamp()
    p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll, origin=origin)


def slab(coll, mats):
    """0.52 x 0.13 x 0.22 green brick — the oxygen generator's cell.

    The long side is 0.52 against the generator's 0.60 face, which is the
    proportion the brief asked for: it spans almost the whole machine, so where
    it goes is unmistakable before a prompt ever appears. Flat enough to lie
    against a wall, deep enough at 0.13 to still read as heavy.
    """
    w, d, h = SLAB_W, SLAB_D, SLAB_H

    p = TrackedPart(mats)
    _emit(p, shell(p, w, d, h, GREEN), "Mesh_PowerCell_Slab_Shell", coll)

    p = TrackedPart(mats)
    _emit(p, bumpers(p, w, d, h, ribs=4), "Mesh_PowerCell_Slab_Bumpers", coll)

    p = TrackedPart(mats)
    hard = face_panel(p, w, d, h, GREEN)
    connector_strip(p, (0.0, -d - 0.004, h * 0.13), rows=2, dots=6,
                    pitch=0.0090)
    _emit(p, hard, "Mesh_PowerCell_Slab_Face", coll)

    p = TrackedPart(mats)
    _emit(p, contacts(p), "Mesh_PowerCell_Slab_Port", coll)

    p = TrackedPart(mats)
    _emit(p, strap(p, w, h, GREEN), "Mesh_PowerCell_Slab_Strap", coll,
          origin=(0, 0, h))

    p = TrackedPart(mats)
    _emit(p, latch(p, w, h, GREEN, at_x=w * 0.34),
          "Mesh_PowerCell_Slab_Latch", coll, origin=(w * 0.34, 0, h))


def compact(coll, mats):
    """0.26 x 0.10 x 0.18 blue cell — half-length, one-handed.

    Built ahead. Swaps the recessed strap for a folding bar handle across the
    top, which changes the outline rather than only the size: at a glance this
    is a thing you pick up in one hand, and the slab is not.
    """
    w, d, h = 0.260, 0.100, 0.180

    p = TrackedPart(mats)
    _emit(p, shell(p, w, d, h, BLUE), "Mesh_PowerCell_Compact_Shell", coll)

    p = TrackedPart(mats)
    _emit(p, bumpers(p, w, d, h, ribs=3),
          "Mesh_PowerCell_Compact_Bumpers", coll)

    p = TrackedPart(mats)
    _emit(p, face_panel(p, w, d, h, BLUE, lit=2),
          "Mesh_PowerCell_Compact_Face", coll)

    p = TrackedPart(mats)
    _emit(p, contacts(p, port=(0.076, 0.014, 0.040), z=h * 0.5, pins=3),
          "Mesh_PowerCell_Compact_Port", coll)

    p = TrackedPart(mats)
    # Folding bar handle: two uprights and a crossbar, standing clear of the lid.
    hard = []
    for s in (-1, 1):
        hard += p.box((s * w * 0.34, -d * 0.5, h + 0.014), (0.020, 0.026, 0.036),
                      DARK)
    tube_path(p, [(-w * 0.34, -d * 0.5, h + 0.028),
                  (w * 0.34, -d * 0.5, h + 0.028)], 0.0080, CHROME, seg=8)
    _emit(p, hard, "Mesh_PowerCell_Compact_Handle", coll, origin=(0, 0, h))

    p = TrackedPart(mats)
    _emit(p, latch(p, w, h, BLUE), "Mesh_PowerCell_Compact_Latch", coll,
          origin=(0, 0, h))


def drum(coll, mats):
    """O 0.16 x 0.30 yellow cell — cylindrical, on a bayonet collar.

    Built ahead, and the variation that pays for itself: it is the only cell
    that is not a box, so a rack holding both never reads as one part repeated.
    Twists into a round socket instead of sliding into a shoe, which is a
    different dock and therefore a different machine.
    """
    r, h = 0.080, 0.300

    p = TrackedPart(mats)
    hard = []
    p.cyl((0, 0, h / 2), r, h, 'Z', 20, SHELL)
    p.cyl((0, 0, h * 0.14 - 0.003), r + 0.005, h * 0.28 + 0.006, 'Z', 20, GREY)
    p.cyl((0, 0, h * 0.86), r + 0.005, 0.024, 'Z', 20, YELLOW)
    for i in range(2):
        p.cyl((0, 0, h * (0.46 + 0.12 * i)), r - 0.003, 0.008, 'Z', 20, BLACK)
    _emit(p, hard, "Mesh_PowerCell_Drum_Shell", coll)

    p = TrackedPart(mats)
    hard = []
    # Bayonet collar: a ring with three lugs that turn into a socket's slots.
    p.cyl((0, 0, h - 0.015), r + 0.010, 0.036, 'Z', 20, YELLOW)
    p.cyl((0, 0, h - 0.002), r - 0.014, 0.014, 'Z', 20, DARK)
    for i in range(3):
        a = 2 * math.pi * i / 3
        hard += p.box((math.cos(a) * (r + 0.008), math.sin(a) * (r + 0.008),
                       h - 0.030), (0.024, 0.030, 0.018), CHROME,
                      rot=Matrix.Rotation(a, 4, 'Z'))
    _emit(p, hard, "Mesh_PowerCell_Drum_Collar", coll, origin=(0, 0, h - 0.018))

    p = TrackedPart(mats)
    hard = []
    # Charge ladder wrapped round the barrel instead of laid on a plate: five
    # slots on the curve, three lit, same read at a different silhouette.
    for i in range(5):
        z = h * (0.40 + 0.075 * i)
        hard += p.box((0, -(r - 0.006), z), (0.062, 0.022, 0.020), SLATE)
        p.box((0, -(r - 0.001), z), (0.048, 0.016, 0.011),
              CRT if i < 3 else BLACK)
    _emit(p, hard, "Mesh_PowerCell_Drum_Face", coll)

    p = TrackedPart(mats)
    hard = []
    p.cyl((0, 0, 0.006), r + 0.006, 0.016, 'Z', 20, RUBBER)
    hard += p.box((0, 0, 0.026), (0.092, 0.046, 0.036), DARK)
    for i in range(3):
        p.box(((i - 1) * 0.026, 0.0, 0.040), (0.010, 0.036, 0.010), CHROME)
    _emit(p, hard, "Mesh_PowerCell_Drum_Port", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    slab(collection("Coll_PowerCell_Slab"), mats)
    compact(collection("Coll_PowerCell_Compact"), mats)
    drum(collection("Coll_PowerCell_Drum"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
