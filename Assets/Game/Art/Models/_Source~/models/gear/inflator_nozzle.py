"""Inflator nozzle — the pump that swells a thing until it floats, then pops it.

Design: `docs/AI/systems/Artifacts/InflatorNozzle.md`. Unmistakably a pump: a
moulded barrel with a plunger grip, a coiled hose, and a brass-collared nozzle at
the end. A round pressure dial on the barrel is the one gauge in the whole kit
that is a NEEDLE rather than a bar, because it reads the *target's* inflation and
not this device's tank — and the tank's own reading is a bar beside it, so the
two are never confused with each other (`GDC-L1-UX-0003`).

Almost none of it is new geometry. The barrel is `Mesh_SprayerTank_Lance` and the
grip is `Mesh_SprayerGrip_Moulded`, both from the kit's shared component file;
the tip is `device_nozzle`'s brass, the dial is `device_gauge`'s, the supply bar
is the kit's plate. What this file adds is the four things that are only true of
a pump: the plunger, the coiled hose, the yellow yoke that carries the function
colour, and where all of it sits.

Orientation and origin
----------------------
The barrel runs along **Y**, muzzle at −Y — the library's forward and the
direction every `Marker_Muzzle` in this project points. The origin is on the bore
axis at the barrel's own centre, which is where the appended lance puts it; the
grip hangs below in −Z and the plunger strokes along +Y.

    blender --background --python inflator_nozzle.py -- --out inflator_nozzle.blend

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

from _buildlib import (append_objects, collection, link_materials, parse_out,  # noqa: E402
                       report, save, start)
from _tracked import TrackedPart  # noqa: E402
from device_kit import (BEVEL_SEG, BEVEL_W, MATS, SEG, BRASS, CHROME, DARK,  # noqa: E402
                        GREY, RUBBER, STEEL, YELLOW)

PROPS = os.path.join(LIB, "components", "props")
NOZZLE = os.path.join(PROPS, "device_nozzle.blend")
GAUGE = os.path.join(PROPS, "device_gauge.blend")
SPRAYER = os.path.join(PROPS, "sprayer_kit.blend")

# --- the barrel, and everything measured off it ------------------------------
# BARREL_* are `Mesh_SprayerTank_Lance`'s own measurements, read off
# sprayer_kit.blend rather than assumed. A second guess at them is how a yoke
# ends up floating 5 mm off the tube it is supposed to clamp.

BARREL_R = 0.0455
BARREL_BACK, BARREL_FRONT = 0.1614, -0.1615

PLUNGER_OUT = 0.078                     # how far the rod stands proud at rest
KNOB_Z = 0.0                            # the T-knob rides on the bore axis

NOZZLE_AT = (0.0, BARREL_FRONT + 0.004, 0.0)
GRIP_AT = (0.0, -0.030, -0.038)         # 7 mm up inside the barrel, so the two
                                        # interpenetrate rather than abut
GAUGE_AT = (BARREL_R - 0.003, 0.010, 0.0)

DIAL_AT = (0.0, 0.055, BARREL_R - 0.008)
DIAL_TILT = -128.0                      # degrees about X. The component's face
                                        # looks −Y, and a rotation about X takes
                                        # −Y to (0, −cos a, −sin a): past 90 the
                                        # y term goes POSITIVE, which is what
                                        # tilts the dial up and BACK toward the
                                        # person holding it. Inside 90 it tilts
                                        # up and away, readable by the target.

HOSE_R = BARREL_R + 0.014               # coil radius: clear of the barrel by a
                                        # hose diameter, so it reads as wrapped
                                        # round rather than moulded on
HOSE_WIRE = 0.0090
HOSE_TURNS = 2.0
HOSE_BACK, HOSE_FRONT = -0.014, -0.150
# The coil lives on the FRONT half of the barrel only. Run over the whole
# length it buried the supply bar between two turns and hid the pressure dial
# behind a third — an occlusion nothing in the numbers showed and the first
# render did.


def _emit(p, hard, name, coll, origin=(0, 0, 0)):
    p.restamp()
    p.bevel(hard, width=BEVEL_W, segments=BEVEL_SEG)
    return p.finish(name, coll, origin=origin)


def _marker(name, at, coll, mats):
    """A 4 mm cube standing in for an empty — empties do not survive
    `object_types={"MESH"}`, and this library ships its sockets as meshes."""
    p = TrackedPart(mats)
    p.box((0, 0, 0), (0.004, 0.004, 0.004), STEEL)
    obj = p.finish(name, coll)
    obj.location = at
    return obj


def _run(p, a, b, wire, mat, seg=8, stretch=1.15):
    """One straight length of hose between two points, turned onto its own line.

    `to_track_quat('Z', 'Y')` is what puts the cylinder's Z on the segment; the
    stretch overlaps neighbours so a bend does not open a gap at its outside.
    """
    a, b = Vector(a), Vector(b)
    d = b - a
    if d.length < 1e-6:
        return []
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    return p.cyl((a + b) / 2.0, wire, d.length * stretch, 'Z', seg, mat, rot=rot)


# --- the parts only a pump has ----------------------------------------------

def plunger(coll, mats):
    """Rod and T-knob, with the origin on the barrel's rear face.

    The whole stroke is one local translation along −Y from there, so the FBX
    hands Unity a transform rather than a rig. The knob is yellow: it is the one
    part of the device a hand is meant to grab and pull, which is exactly what a
    signifier is for (`GDC-L1-UX-0004`).
    """
    p = TrackedPart(mats)
    y = BARREL_BACK
    hard = p.cyl((0, y + PLUNGER_OUT / 2, KNOB_Z), 0.014, PLUNGER_OUT, axis='Y',
                 seg=12, mat=CHROME)
    hard += p.cyl((0, y + PLUNGER_OUT - 0.006, KNOB_Z), 0.024, 0.014, axis='Y',
                  seg=SEG, mat=DARK)
    hard += p.box((0, y + PLUNGER_OUT + 0.012, KNOB_Z), (0.086, 0.024, 0.028),
                  YELLOW)
    for sx in (-1, 1):
        hard += p.cyl((sx * 0.043, y + PLUNGER_OUT + 0.012, KNOB_Z), 0.014,
                      0.026, axis='X', seg=12, mat=YELLOW)
    return _emit(p, hard, "Mesh_InflatorNozzle_Plunger", coll,
                 origin=(0.0, BARREL_BACK, 0.0))


def hose(coll, mats):
    """A hose coiled round the barrel and run forward into the nozzle collar.

    Coiled round the barrel rather than hanging off it. The design asks for a
    coiled hose; a free loop dangling from a 0.5 m item swings through the
    player's own arm in first person and has to be authored in a pose that is
    wrong from every other angle. Wrapped, it reads as pneumatic from all of
    them and never intersects the hand.
    """
    p = TrackedPart(mats)
    steps = int(HOSE_TURNS * 12)
    pts = []
    for i in range(steps + 1):
        t = i / steps
        a = 2 * math.pi * HOSE_TURNS * t
        pts.append(Vector((HOSE_R * math.cos(a),
                           HOSE_BACK + (HOSE_FRONT - HOSE_BACK) * t,
                           HOSE_R * math.sin(a))))
    for a, b in zip(pts, pts[1:]):
        _run(p, a, b, HOSE_WIRE, RUBBER, seg=6)

    # Take-off from the barrel at the back of the coil, and the run forward into
    # the nozzle collar at the front of it.
    hard = _run(p, (BARREL_R - 0.006, HOSE_BACK, 0.0), (HOSE_R, HOSE_BACK, 0.0),
                0.012, BRASS, seg=10, stretch=1.0)
    _run(p, pts[-1], (-0.022, BARREL_FRONT + 0.010, 0.0), HOSE_WIRE, RUBBER)
    hard += _run(p, (-0.026, BARREL_FRONT + 0.012, 0.0),
                 (-0.006, BARREL_FRONT + 0.006, 0.0), 0.012, BRASS, seg=10,
                 stretch=1.0)
    return _emit(p, hard, "Mesh_InflatorNozzle_Hose", coll)


def yoke(coll, mats):
    """The yellow hardware: muzzle collar, mid-barrel band, dial cradle.

    The kit's shells are all the same pale enamel, so the ONE saturated colour is
    what says which device this is at a distance. It is put on three parts that
    each have their own shape — a collar, a band and a cradle — so the reading
    survives shadow and colour blindness (`GDC-L1-UX-0006`). Yellow, because the
    palette documents `Mat_Plastic_Safety_Yellow` for exactly this: moulded
    high-vis plastic on the parts a hand works.
    """
    p = TrackedPart(mats)
    hard = p.tube((0, BARREL_FRONT + 0.016, 0), BARREL_R + 0.008, 0.012, 0.030,
                  axis='Y', seg=SEG, mat=YELLOW)
    hard += p.tube((0, -0.040, 0), BARREL_R + 0.006, 0.010, 0.026, axis='Y',
                   seg=SEG, mat=YELLOW)
    # Cradle under the dial, so the instrument sits on something.
    hard += p.box((0, DIAL_AT[1], BARREL_R - 0.006), (0.062, 0.070, 0.020),
                  GREY, rot=Matrix.Rotation(math.radians(38), 4, 'X'))
    # Hose clips: two saddles the coil passes under.
    for y in (-0.008, -0.112):
        hard += p.box((0, y, -(BARREL_R + 0.008)), (0.030, 0.016, 0.026), GREY)
    return _emit(p, hard, "Mesh_InflatorNozzle_Yoke", coll)


# --- assembly ---------------------------------------------------------------

def place(blend, names, coll, matrix):
    """Append and set each object's world matrix, composing with what it has.

    Composing rather than assigning matters for the dial: the needle is a second
    object whose whole point is that its own origin is the spindle, and an
    assignment would throw away the placement the component already carries.
    """
    objs = append_objects(blend, names, coll)
    for obj in objs:
        obj.matrix_world = matrix @ obj.matrix_world
    bpy.context.view_layer.update()
    return objs


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_InflatorNozzle")

    place(SPRAYER, ["Mesh_SprayerTank_Lance"], coll, Matrix.Identity(4))
    place(SPRAYER, ["Mesh_SprayerGrip_Moulded"], coll,
          Matrix.Translation(GRIP_AT))
    place(NOZZLE, ["Mesh_DeviceNozzle_Brass"], coll,
          Matrix.Translation(NOZZLE_AT))

    # The supply bar goes on the barrel's +X flank. A rotation about Z carries
    # −Y to (sin a, −cos a, 0), so +90 degrees — and only +90 — lands the face
    # on +X; −90 buries it in the barrel and reads as a missing gauge.
    place(SPRAYER, ["Mesh_SprayerGauge_Plate"], coll,
          Matrix.Translation(GAUGE_AT) @ Matrix.Rotation(math.radians(90), 4, 'Z'))

    place(GAUGE, ["Mesh_DeviceGauge_Dial", "Mesh_DeviceGauge_Needle"], coll,
          Matrix.Translation(DIAL_AT)
          @ Matrix.Rotation(math.radians(DIAL_TILT), 4, 'X'))

    yoke(coll, mats)
    hose(coll, mats)
    plunger(coll, mats)

    _marker("Marker_Muzzle", (0.0, BARREL_FRONT - 0.072, 0.0), coll, mats)
    _marker("Marker_Grip", (0.0, GRIP_AT[1] + 0.002, GRIP_AT[2] - 0.060), coll,
            mats)
    _marker("Marker_Gauge", (BARREL_R + 0.006, GAUGE_AT[1], 0.0), coll, mats)
    _marker("Marker_Dial", DIAL_AT, coll, mats)

    save(out)
    report()


if __name__ == "__main__":
    main()
