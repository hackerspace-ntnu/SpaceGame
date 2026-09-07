"""Device gauge — the four readouts the issued-equipment family reads state from.

Every device in the kit has to answer "how much is left" without a menu, and the
project already has the machinery for that: `SupplyGauge` builds a fill bar over an
emissive strip it finds by material, and `OxygenGearBuilder` measures that strip off
the model. So the *geometry* a gauge needs is small and exact — a dark housing, a
recessed plate, and a lit strip in `Mat_Emissive_Green_CRT` — and it is the same
geometry on nine devices. That is a component, not nine near-copies.

Four variations, differing in structure rather than colour:

  Bar     the SupplyGauge face — one lit strip in a housing. What a tank wears.
  Dial    a round needle instrument with a red arc, and the NEEDLE AS ITS OWN
          OBJECT so a device can turn it. What reads a *target's* state rather
          than the tank's.
  Lamp    a single indicator boss. Lit or not — the cheapest possible readout.
  Ladder  five stacked cells, three of them lit. A COUNT rather than a length,
          which is the one encoding that survives a red-green colour deficiency
          untouched (`GDC-L1-UX-0006`).

Orientation and origin
----------------------
Every variation seats on a plane at **y = 0** and faces along **−Y**, the library's
forward. That is the same convention `Mesh_OxygenTank_Gauge` already uses, so a
model places a gauge by putting its origin on the surface it is set into and turning
the surface's outward normal onto −Y.

The Bar is deliberately **symmetric about x = 0 and z = 0**, emissive included.
`SupplyGauge` mirrors the measured strip about the *gauge mesh's* middle, and the
bottle's 6 mm of asymmetry once built a bar 36% too long — see SupplyGauge.md.

    blender --background --python device_gauge.py -- --out device_gauge.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

from mathutils import Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)
sys.path.insert(0, HERE)

from _buildlib import collection, link_materials, parse_out, report, save, start  # noqa: E402
from _tracked import TrackedPart  # noqa: E402
from device_kit import (BEVEL_SEG, BEVEL_W, MATS, SEG, BLACK, BRASS, CHROME,  # noqa: E402
                        CRT, DARK, GREY, RED, REDLAMP, SLATE, STEEL)


def _emit(p, hard, name, coll, origin=(0, 0, 0)):
    p.restamp()
    p.bevel(hard, width=BEVEL_W, segments=BEVEL_SEG)
    return p.finish(name, coll, origin=origin)


# -- Bar: the SupplyGauge face ----------------------------------------------

def bar(coll, mats):
    """A dark housing, a recessed plate, and one lit strip.

    Nothing here is decoration. The HOUSING is what `SupplyGauge` draws its dark
    track over, the PLATE gives the track something to sit proud of, and the
    STRIP is the only thing in `Mat_Emissive_Green_CRT` on the whole part — which
    is how `OxygenGearBuilder.MeasureGauge` finds it. Adding a second emissive
    face anywhere on this mesh would silently enlarge every bar built from it.
    """
    p = TrackedPart(mats)

    hard = p.box((0, -0.010, 0), (0.072, 0.020, 0.040), BLACK)
    hard += p.box((0, -0.019, 0), (0.052, 0.008, 0.024), SLATE)
    # Only the housing and the plate are bevelled. A 2.5 mm chamfer on a 4 mm
    # tick swallows the tick and stamps what is left with material index 0 —
    # the family's steel — so every accent smaller than about 10 mm is kept out
    # of `hard` on purpose. This is the `_buildlib` trap the library warns about
    # and it is invisible until the part is measured.

    # 3 mm proud of the plate and 0.5 mm buried in it: nothing in this family
    # may meet another surface on a plane.
    p.box((0, -0.0255, 0), (0.040, 0.006, 0.012), CRT)

    # Four ticks along the top lip, symmetric so they cannot pull the mesh's
    # middle away from the emissive strip's.
    for sx in (-1, 1):
        for u in (0.010, 0.024):
            p.box((sx * u, -0.0215, 0.0135), (0.0025, 0.005, 0.005), DARK)

    return _emit(p, hard, "Mesh_DeviceGauge_Bar", coll)


# -- Dial: a needle instrument, with the needle free to turn -----------------

DIAL_R = 0.028          # bezel outer radius
NEEDLE_R = 0.019        # how far the needle reaches from the spindle
NEEDLE_REST = 215.0     # degrees CCW from +X in the XZ plane — lower left, the
                        # bottom of a gauge's sweep on every instrument ever made
                        # (`GDC-L1-UX-0004`: honour the convention, do not invent)


def dial(coll, mats):
    """Round pressure instrument: chrome bezel, sunk face, red arc, live needle.

    The needle is its OWN object with its origin on the spindle, so one local Y
    rotation is the whole animation and the FBX hands Unity a plain transform.
    Y is the spindle axis because the face looks along −Y; a rotation about Y
    carries +X onto −Z, which sweeps in the plane of the dial. Verified from the
    matrix, not assumed — the sign is the half that goes wrong.
    """
    p = TrackedPart(mats)

    hard = p.tube((0, -0.011, 0), DIAL_R, 0.005, 0.022, axis='Y', seg=SEG,
                  mat=CHROME)
    p.cyl((0, -0.006, 0), 0.024, 0.016, axis='Y', seg=SEG, mat=BLACK)
    # Plugs into whatever the dial is set into, so the bezel can sit flush.
    p.cyl((0, 0.004, 0), 0.020, 0.014, axis='Y', seg=SEG, mat=GREY)

    # The red arc lives at the TOP of the sweep, where an over-pressure lands.
    for i in range(5):
        a = math.radians(80.0 - i * 15.0)
        p.box((0.0205 * math.cos(a), -0.0155, 0.0205 * math.sin(a)),
              (0.005, 0.004, 0.0035), RED)

    # Minute ticks around the rest of the face, so the arc reads as part of a
    # scale rather than as a stray red mark.
    for i in range(9):
        a = math.radians(NEEDLE_REST - i * 15.0)
        p.box((0.0205 * math.cos(a), -0.0150, 0.0205 * math.sin(a)),
              (0.004, 0.003, 0.0025), GREY)

    p.cyl((0, -0.0175, 0), 0.005, 0.007, axis='Y', seg=8, mat=BRASS)
    _emit(p, hard, "Mesh_DeviceGauge_Dial", coll)

    q = TrackedPart(mats)
    a = math.radians(NEEDLE_REST)
    tip = (NEEDLE_R * math.cos(a), -0.0165, NEEDLE_R * math.sin(a))
    q.seam((0.0, -0.0165, 0.0), tip, width=0.0035, depth=0.0025, axis='Y',
           mat=RED)
    q.cyl((0, -0.0165, 0), 0.0035, 0.004, axis='Y', seg=8, mat=DARK)
    # No bevel: a 3.5 mm needle chamfered by 2.5 mm is a steel splinter.
    q.restamp()
    return q.finish("Mesh_DeviceGauge_Needle", coll)


# -- Lamp: one indicator boss ------------------------------------------------

def lamp(coll, mats):
    """A single lit boss — an arming light, a ready light, a fault light.

    The lens is a separate material index so a device can re-slot it dark
    without needing a second mesh: a spent booster wants the same boss with the
    light out.
    """
    p = TrackedPart(mats)
    hard = p.tube((0, -0.006, 0), 0.012, 0.004, 0.012, axis='Y', seg=12,
                  mat=DARK)
    p.cyl((0, 0.002, 0), 0.013, 0.008, axis='Y', seg=12, mat=GREY)
    p.cyl((0, -0.009, 0), 0.009, 0.008, axis='Y', seg=12, mat=REDLAMP)
    return _emit(p, hard, "Mesh_DeviceGauge_Lamp", coll)


# -- Ladder: a count, not a length -------------------------------------------

def ladder(coll, mats):
    """Five stacked cells, three lit.

    The one readout in the set that carries its reading as a COUNT. That is not
    a stylistic alternative to the bar — it is the encoding that stays readable
    with no colour vision at all, and it is why the project's battery was
    already authored this way. Three of five are lit in the mesh so an unpainted
    display copy still reads as part-charged rather than as blank hardware.
    """
    p = TrackedPart(mats)
    hard = p.box((0, -0.009, 0), (0.036, 0.018, 0.076), BLACK)
    for i in range(5):
        z = -0.028 + i * 0.014
        p.box((0, -0.0205, z), (0.024, 0.007, 0.010), CRT if i < 3 else SLATE)
    p.box((0, -0.014, 0.0425), (0.030, 0.010, 0.006), STEEL)
    return _emit(p, hard, "Mesh_DeviceGauge_Ladder", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    bar(collection("Coll_DeviceGauge_Bar"), mats)
    dial(collection("Coll_DeviceGauge_Dial"), mats)
    lamp(collection("Coll_DeviceGauge_Lamp"), mats)
    ladder(collection("Coll_DeviceGauge_Ladder"), mats)
    save(out)
    report()


if __name__ == "__main__":
    main()
