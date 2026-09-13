"""Resizer remote — the handset that shrinks or enlarges whatever you point it at.

Design: `docs/AI/systems/ResizerRemote.md`. A signalling handset, not a
tool that touches anything: an armoured control case held by a pistol grip, a
long whip antenna standing off the top, and a chin panel carrying the one control
that matters — the polarity dial that says which way the signal runs.

Almost none of it is new geometry. The case and its display are
`handheld_terminal`'s **Rugged** variation, built ahead in that file and never
shipped until now; the grip is `weapon_grip`'s **Pistol**, whose origin already
sits on its mount face so bolting it under the case is a translation and nothing
else. What this file adds is the four things only a transmitter has: the whip,
the chin panel the dial and the charge bar live on, the polarity dial itself, and
the lamp that repeats the dial's reading in colour.

**The dial is a separate object, and so is the whip.** Both move in Unity — the
dial turns between the two settings, the whip sways and telescopes while the
signal is going out — and each carries its origin on its own axis of motion, so
the game drives a transform rather than a rig. Nothing here deforms; an armature
would be complexity Unity has to unpick.

**Why the whip is as tall as it is.** It is the read at a distance. Another
player has to be able to tell, across a camp, that the thing in your hand is the
resizer and not the item scanner — which is the same family, the same cream
shell, and the same size (`GDC-L1-UX-0004`). The scanner's mast is a 0.14 m stub
angled back over the case; this one stands 0.2 m straight up out of a blue
collar, and the silhouettes do not read as each other.

Orientation and origin
----------------------
The case faces **−Y** and up is **+Z**, which is `handheld_terminal`'s frame and
therefore the frame every appended part is already in. The origin is the case's
own, at the bottom-centre of its back face; the grip hangs below in −Z and the
whip stands above in +Z.

    blender --background --python resizer_remote.py -- --out resizer_remote.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)
sys.path.insert(0, os.path.join(LIB, "components", "props"))
sys.path.insert(0, os.path.join(LIB, "components", "mechanical"))

from _buildlib import (Part, append_objects, collection, link_materials,  # noqa: E402
                       parse_out, report, save, start)
from handheld_terminal import MATS, planar_uv  # noqa: E402
from panel_control import ribbed_knob, tube_path  # noqa: E402

# `handheld_terminal.MATS`, unpacked here so this file reads in names rather than
# in indices. Imported rather than restated: the appended Rugged case carries
# material INDICES, so a second list that drifted by one would silently repaint
# the whole device.
STEEL, DARK, RUBBER, CHROME, CREAM, RED, BLUE, AMBER, BLACK, CRT = range(10)

PROPS = os.path.join(LIB, "components", "props")
MECH = os.path.join(LIB, "components", "mechanical")
TERMINAL = os.path.join(PROPS, "handheld_terminal.blend")
GRIP = os.path.join(MECH, "weapon_grip.blend")

BEVEL_W = 0.0014                        # handheld_terminal's, so the new parts
BEVEL_SEG = 2                           # are moulded like the case they sit on

# --- the case, and everything measured off it --------------------------------
# CASE_* are `Mesh_Terminal_Rugged_Case`'s own numbers, read off
# handheld_terminal.py's `rugged()` rather than assumed. A second guess at them
# is how a chin panel ends up floating 3 mm below the shell it is bolted to.

CASE_X = 0.062                          # half-width of the shell
CASE_FRONT = -0.038                     # the face the screen is in
CASE_BACK = 0.040
CASE_TOP = 0.086

GRIP_AT = (0.0, 0.004, 0.002)           # 2 mm up inside the shell, so grip and
                                        # case interpenetrate rather than abut

# The chin: a block under the front of the case carrying the polarity setting.
# It exists because the Rugged deck is full — a toggle, a selector and a knob
# already occupy the strip under the screen — and because a reading the holder
# has to tilt the device to see is a reading they will not take
# (`GDC-L1-UX-0003`).
CHIN_LO = (-0.056, CASE_FRONT - 0.003, -0.024)
CHIN_HI = (0.056, 0.012, 0.002)
CHIN_FACE = CHIN_LO[1]

# The grip occupies the middle 50 mm of the chin's width — its top station is
# 38 mm across and the wooden cheeks stand 6 mm proud of that either side. So
# everything on the chin sits OUTBOARD of x = ±0.026 or it is a readout behind
# a hand. Measured off `weapon_grip.pistol`'s own stations rather than eyeballed
# from a render, which is how a gauge ends up 2 mm clear at build time and
# covered the first time somebody nudges the grip.
GRIP_SHADOW = 0.026

CHIN_Z = -0.011                         # both controls ride the chin's midline

DIAL_R = 0.0118
DIAL_AT = (-(GRIP_SHADOW + DIAL_R), CHIN_FACE + 0.001, CHIN_Z)

LAMP_R = 0.0062
LAMP_AT = (GRIP_SHADOW + LAMP_R + 0.008, CHIN_FACE + 0.001, CHIN_Z)

# The charge bar is on the case's TOP face, not on the chin — there is no span
# of chin left that a hand does not cover, and the top is the face that turns
# toward the holder at exactly the moment the reading is wanted: the handset is
# held up to aim it. Left of the vent grille (x ±0.030) and well clear of the
# antenna collar.
GAUGE_LO = (-0.056, -0.028, CASE_TOP + 0.0004)
GAUGE_HI = (-0.034, 0.034, CASE_TOP + 0.0012)

# The whip. Straight up out of the case's back half, clear of the propped lid
# (which occupies the upper FRONT) and clear of the vent grille (x ±0.030).
WHIP_AT = (0.040, 0.014, CASE_TOP)
WHIP_H = 0.200
WHIP_R = 0.0040                         # at the collar; it tapers to the bead


def _emit(p, hard, name, coll, origin=(0, 0, 0)):
    p.bevel(hard, width=BEVEL_W, segments=BEVEL_SEG)
    return p.finish(name, coll, origin=origin)


def _marker(name, at, coll, mats):
    """A 4 mm cube standing in for an empty — empties do not survive
    `object_types={"MESH"}`, and this library ships its sockets as meshes."""
    p = Part(mats)
    p.box((0, 0, 0), (0.004, 0.004, 0.004), STEEL)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


# --- the parts only a transmitter has ---------------------------------------

def yoke(coll, mats):
    """The blue hardware and the chin block: everything that is not the case.

    The device family's shells are all the same pale cream, so the ONE saturated
    colour is what says which device this is at arm's length. It goes on three
    parts that each have their own shape — the antenna collar, the chin's lip,
    and a band round the grip's throat — so the reading survives shadow and
    colour blindness (`GDC-L1-UX-0003`: never encode information in colour
    alone). Blue rather than the nozzle's yellow or the booster's red, because
    those two are taken and a fourth device in either would be two items the
    player has to read twice.
    """
    p = Part(mats)

    # Chin block, and the lip that stands the polarity controls proud of it.
    hard = list(p.slab(CHIN_LO, CHIN_HI, CREAM))
    hard += p.slab((CHIN_LO[0], CHIN_FACE - 0.004, CHIN_LO[2] - 0.001),
                   (CHIN_HI[0], CHIN_FACE, CHIN_LO[2] + 0.004), BLUE)

    # A black well under the charge bar, so the bar reads as lit rather than
    # painted. Same trick `_bezel` plays on the screen, at a tenth the size.
    hard += p.slab((GAUGE_LO[0] - 0.003, GAUGE_LO[1] - 0.003, CASE_TOP - 0.004),
                   (GAUGE_HI[0] + 0.003, GAUGE_HI[1] + 0.003, CASE_TOP + 0.0006),
                   BLACK)

    # Flank plates. The third and fourth carriers of the colour, and the ones
    # that do the work in PROFILE — which is the angle another player sees the
    # handset from, the case being held edge-on to them while it is pointed at
    # somebody.
    for sx in (-1, 1):
        inner, outer = sx * (CASE_X - 0.002), sx * (CASE_X + 0.004)
        hard += p.slab((min(inner, outer), -0.030, 0.020),
                       (max(inner, outer), 0.020, 0.070), BLUE)

    # Antenna collar: the boss the whip screws into, and the blue ring round it.
    hard += p.cyl((WHIP_AT[0], WHIP_AT[1], CASE_TOP - 0.004), 0.0135, 0.016,
                  'Z', 12, STEEL)
    hard += p.tube((WHIP_AT[0], WHIP_AT[1], CASE_TOP + 0.005), 0.0135, 0.0035,
                   0.008, 'Z', 12, BLUE)

    # Grip throat band. The third carrier of the colour, and the part a hand is
    # actually on — which is what a signifier is for (`GDC-L1-UX-0004`).
    hard += p.slab((-0.026, -0.006, -0.014), (0.026, 0.034, -0.006), BLUE)

    # A guard hoop over the whip's root. Bumping a 0.2 m whip on a doorframe is
    # the ordinary case for a thing carried on a belt, and the hoop is what says
    # the designers of this handset knew that.
    tube_path(p, [(WHIP_AT[0] - 0.026, WHIP_AT[1], CASE_TOP + 0.004),
                  (WHIP_AT[0] - 0.026, WHIP_AT[1], CASE_TOP + 0.030),
                  (WHIP_AT[0], WHIP_AT[1], CASE_TOP + 0.042),
                  (WHIP_AT[0] + 0.026, WHIP_AT[1], CASE_TOP + 0.030),
                  (WHIP_AT[0] + 0.026, WHIP_AT[1], CASE_TOP + 0.004)],
              0.0030, STEEL, seg=6)

    return _emit(p, hard, "Mesh_ResizerRemote_Yoke", coll)


def whip(coll, mats):
    """The antenna, with its origin at the collar it stands out of.

    Three telescoping sections rather than one taper, because the game extends
    it: `ResizerRemoteRig` scales this object along its own Z while the signal
    is going out, and a segmented whip sells that as sections sliding where a
    smooth cone only reads as the whole thing getting longer.

    Straight, not raked. The item scanner's mast leans back over its case and
    this one must not, or the two devices share a silhouette — see the module
    docstring.
    """
    p = Part(mats)
    x, y, z0 = WHIP_AT

    # Rubber boot at the root, so the whip reads as sprung rather than welded.
    hard = list(p.cyl((x, y, z0 + 0.010), 0.0058, 0.020, 'Z', 10, RUBBER))

    sections = ((0.020, 0.086, WHIP_R), (0.106, 0.062, WHIP_R * 0.72),
                (0.168, 0.026, WHIP_R * 0.48))
    for base, length, radius in sections:
        hard += p.cyl((x, y, z0 + base + length / 2.0), radius, length, 'Z', 8,
                      CHROME)
        # The collar each section slides out of. Four triangles' worth of detail
        # that is the whole reason the telescoping reads at all.
        hard += p.tube((x, y, z0 + base), radius + 0.0016, 0.0008, 0.004, 'Z',
                       8, DARK)

    # The bead. Tipped in the function colour, so the far end of the longest
    # part of the device is the part that names it.
    hard += p.cyl((x, y, z0 + WHIP_H - 0.004), 0.0052, 0.009, 'Z', 10, BLUE)

    return _emit(p, hard, "Mesh_ResizerRemote_Whip", coll, origin=WHIP_AT)


def dial(coll, mats):
    """The polarity selector, origin on its own spindle.

    `ribbed_knob`'s pointer is the point of it. The setting is carried by where
    the pointer is aimed — a position, which a photograph would show — and the
    lamp beside it only repeats that in colour (`GDC-L1-UX-0003`). A player who
    cannot separate the two lamp colours reads the knob instead.
    """
    p = Part(mats)
    hard = ribbed_knob(p, DIAL_AT, radius=DIAL_R, depth=0.020, ribs=14)

    # Detents either side of the spindle: the two settings, engraved. They stay
    # with the CASE, not the knob, so they do not turn with the pointer — which
    # is why they are emitted here on the dial's own part but outside its origin
    # arc, and why a third setting would need a third mark cut here.
    for sx in (-1, 1):
        hard += p.box((DIAL_AT[0] + sx * (DIAL_R + 0.005), CHIN_FACE + 0.0004,
                       DIAL_AT[2] + 0.012), (0.006, 0.0018, 0.0026), CHROME)

    return _emit(p, hard, "Mesh_ResizerRemote_Dial", coll, origin=DIAL_AT)


def lamp(coll, mats):
    """The polarity lamp: one lens Unity tints, on its own object.

    Its own object because the tint is per-instance — two players can carry two
    remotes set opposite ways — and a `MaterialPropertyBlock` addresses a
    renderer, not a face on somebody else's mesh.
    """
    p = Part(mats)
    hard = list(p.tube((LAMP_AT[0], CHIN_FACE + 0.0015, LAMP_AT[2]),
                       LAMP_R + 0.0022, 0.0022, 0.006, 'Y', 12, CHROME))
    p.cyl((LAMP_AT[0], CHIN_FACE + 0.0008, LAMP_AT[2]), LAMP_R, 0.004, 'Y', 12,
          AMBER)
    return _emit(p, hard, "Mesh_ResizerRemote_Lamp", coll, origin=LAMP_AT)


def gauge(coll, mats):
    """The charge bar plate: a flat 0..1 strip the SupplyGauge shader fills.

    Deliberately not bevelled and given planar UVs, for `screen_plate`'s two
    reasons: a bevel folds new faces into the UV island and drags the fill's own
    edge pixels round the rim, and a shader addressed in 0..1 screen space
    samples (0, 0) on every fragment without a UV layer and renders as one flat
    colour.
    """
    p = Part(mats)
    p.slab(GAUGE_LO, GAUGE_HI, CRT)
    # The plate faces +Z, so its 0..1 runs along Y (the bar's length, and the
    # direction the fill travels) and across X. Projecting along the thin axis
    # is `planar_uv`'s whole contract; naming the wrong pair here gives the
    # shader a degenerate island and a bar that is full or empty and nothing in
    # between.
    return planar_uv(p.finish("Mesh_ResizerRemote_Gauge", coll), u_axis=1,
                     v_axis=0)


# --- assembly ---------------------------------------------------------------

def place(blend, names, coll, matrix, rename=None):
    """Append objects and set each world matrix, composing with what it has.

    Composing rather than assigning, because an appended part may already carry
    a placement of its own — and `rename` exists because the two donors are
    COMPONENT files whose names say where they came from. `Mesh_Terminal_Rugged_Case`
    in a Unity prefab called ResizerRemote is a name that sends the next reader
    to the wrong file.
    """
    objs = append_objects(blend, names, coll)
    for obj in objs:
        obj.matrix_world = matrix @ obj.matrix_world
        if rename:
            obj.name = rename.get(obj.name, obj.name)
    bpy.context.view_layer.update()
    return objs


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_ResizerRemote")

    place(TERMINAL, ["Mesh_Terminal_Rugged_Case", "Mesh_Terminal_Rugged_Screen"],
          coll, Matrix.Identity(4),
          rename={"Mesh_Terminal_Rugged_Case": "Mesh_ResizerRemote_Case",
                  "Mesh_Terminal_Rugged_Screen": "Mesh_ResizerRemote_Screen"})

    # The grip's mount face points +Z and the grip hangs below it, so a bare
    # translation to the case's underside is most of the placement. The rest is
    # standing it back UP: `pistol` rakes 0.34 backward per unit of drop, which
    # over its 0.14 m puts the heel 48 mm behind the case — right for a rifle
    # levelled at the shoulder, wrong for a handset held up at eye height and
    # pointed, where the wrist is under the device rather than behind it. −14°
    # about X carries the heel 34 mm of that back forward and leaves a grip
    # that still rakes, just not like a drill.
    place(GRIP, ["Mesh_WeaponGrip_Pistol"], coll,
          Matrix.Translation(GRIP_AT) @ Matrix.Rotation(math.radians(-14), 4, 'X'),
          rename={"Mesh_WeaponGrip_Pistol": "Mesh_ResizerRemote_Grip"})

    yoke(coll, mats)
    whip(coll, mats)
    dial(coll, mats)
    lamp(coll, mats)
    gauge(coll, mats)

    # Sockets. `Marker_Emitter` is the whip's TIP rather than its collar: the
    # signal leaves the far end of the antenna, and a beam drawn from the collar
    # comes out of the player's own knuckles.
    _marker("Marker_Emitter", (WHIP_AT[0], WHIP_AT[1], WHIP_AT[2] + WHIP_H),
            coll, mats)
    _marker("Marker_Grip", (0.0, GRIP_AT[1] + 0.010, GRIP_AT[2] - 0.058), coll,
            mats)
    _marker("Marker_Dial", DIAL_AT, coll, mats)
    _marker("Marker_Lamp", LAMP_AT, coll, mats)
    _marker("Marker_Gauge", ((GAUGE_LO[0] + GAUGE_HI[0]) / 2.0,
                             (GAUGE_LO[1] + GAUGE_HI[1]) / 2.0,
                             GAUGE_HI[2]), coll, mats)

    save(out)
    report()


if __name__ == "__main__":
    main()
