"""Slick can — the aerosol can of the sprayer kit.

    blender --background --python slick_can.py -- --out slick_can.blend

The smallest item in the nine-piece set and the only one that is **not a gun**:
an upright can with a wide flat fan head on top, a thumb trigger on a collar,
and the supply gauge running *up* the flank instead of along it. Nothing else
in the family shares that silhouette, which is what identifies the can in a
hotbar at icon size — it is the one that stands rather than points
(`GDC-L1-UX-0003`, `GDC-L1-UX-0004`).

Design: `docs/AI/systems/Artifacts/SlickCan.md`.

## Scale

Bracketed against `models/gear/dragon_bazooka.blend` (1.3685 m in Blender, worn
at `holdSize` 1.25 — the anchor of `ItemScaleLadder.cs`). Authored at
**0.400 m**, which is the size the design doc names for this item
specifically: "the smallest of the sprayers, closest to an aerosol can in the
hand". The other three sit in the 0.50/0.90 brackets; this one is deliberately
a step below them, and the step is the point.

| Object | What it is |
|---|---|
| `Mesh_SlickCan_Body`    | the can — a domed pressure vessel with two accent hoops |
| `Mesh_SlickCan_Foot`    | rubber base ring, so it stands where it is put down |
| `Mesh_SlickCan_Neck`    | grey shoulder collar between can and head |
| `Mesh_SlickCan_Trigger` | the kit's thumb trigger and its guard, on the collar |
| `Mesh_SlickCan_Head`    | the moulded head block the fan is cut into |
| `Mesh_SlickCan_Fan`     | the wide flat fan nozzle — the only non-round mouth in the set |
| `Mesh_SlickCan_Cap`     | pressure cap on the crown |
| `Mesh_SlickCan_Gauge`   | the kit's supply gauge, running UP the flank |
| `Marker_*`              | muzzle, grip, gauge face |

## The accent, and why it is a metal

`Mat_Metal_Copper_Oxide` is documented for verdigris pipework, and this is the
one place it is used as an item's function colour. The reason is specific: the
item's whole subject is a **frictionless, iridescent film**, and the palette has
no violet, no iridescent and no wet-look colour. Copper oxide is the only cool
teal in the set and the only one that is *metallic*, so it shifts with the
viewing angle the way the film it dispenses is supposed to. Every other option
was worse for a stated reason: `Mat_Paint_Cell_Green` is the power colour and
sits next to the gauge's own green; `Mat_Paint_Rose_Dusty` reads as a pastel
cottage wall; `Mat_Paint_Lacquer_Vermilion` is the only glossy paint but red
means danger and is already the flamethrower's muzzle band.

## Frame

+Z up, −Y forward, 1 unit = 1 m — the family's frame, kept even though the can
stands rather than points, so the fan still fires along −Y and the wave-2
seating has one convention to learn instead of two. The foot is z 0 and the
cap is the top of the model.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "props"))

from _buildlib import (collection, link_materials, parse_out,  # noqa: E402
                       report, save, start, tri_count)
from _tracked import TrackedPart  # noqa: E402

import sprayer_kit as kit  # noqa: E402
from sprayer_kit import CHROME, DARK, GREY, RUBBER, SHELL  # noqa: E402

ACCENT = 8
MATS = kit.KIT_MATS + [
    "Mat_Metal_Copper_Oxide",      # 8 the function colour: the slick film
]

# ── The upright axis ─────────────────────────────────────────────────────────
CAN_R = 0.052
CAN_Z, CAN_LEN = 0.140, 0.260      # centre and length -> z 0.010 .. 0.270
FOOT_Z = 0.014
HOOP_LOW_Z, HOOP_HIGH_Z = 0.048, 0.236

NECK_Z0, NECK_Z1 = 0.250, 0.318
NECK_R = 0.028
COLLAR_Z = 0.300
COLLAR_R = 0.044

HEAD_Z0, HEAD_Z1 = 0.306, 0.372
HEAD_HX, HEAD_HY = 0.034, 0.031

FAN_Z = 0.340
FAN_BACK_Y = -0.026
FAN_HW, FAN_H, FAN_D = 0.040, 0.026, 0.042
MOUTH_Y = FAN_BACK_Y - FAN_D       # -0.068

CAP_Z0, CAP_Z1 = 0.370, 0.400

# The hand wraps the can body rather than a grip, so the palm point is on the
# can's own axis at the height a fist closes.
PALM_Z = 0.150

# See `flamethrower.py`: the gauge takes the flank turned toward the camera.
GAUGE_SIDE = 1.0

BEVEL_W = kit.BEVEL_W


def _emit(p, hard, name, coll, origin=(0.0, 0.0, 0.0)):
    """Restamp, bevel, emit.

    `origin` is the part's pivot, in the frame it was built in. Fixed parts keep
    the model origin; a part something in Unity turns or scales gets the point
    it turns or scales about, so the prefab needs no offset transform to make
    the motion look right.
    """
    p.restamp(name)
    if hard:
        p.bevel(hard, width=BEVEL_W, segments=2)
    return p.finish(name, coll, origin=origin)


# ---------------------------------------------------------------------------
# Parts
# ---------------------------------------------------------------------------

def body(coll, mats):
    """The can: a domed pressure vessel with two accent hoops.

    `kit.tank`'s own `bands` are not used. Its bands are evenly spaced along
    the vessel, and evenly spaced on a 0.26 m can puts one straight through the
    gauge — the hoops are placed by hand above and below the instrument
    instead. `SupplyGauge.md`'s rule applies here as much as to the bar: the
    reading must not be crossed by decoration.
    """
    p = TrackedPart(mats)
    hard = kit.tank(p, (0.0, 0.0, CAN_Z), 'Z', CAN_R, CAN_LEN, body=SHELL,
                    cap=CHROME, dome=0.40, bands=0)
    for z in (HOOP_LOW_Z, HOOP_HIGH_Z):
        hard += p.cyl((0.0, 0.0, z), CAN_R * 1.04, 0.018, axis='Z', seg=24,
                      mat=ACCENT)
    return _emit(p, hard, "Mesh_SlickCan_Body", coll)


def foot(coll, mats):
    """A rubber base ring. It is what makes the can readable as a *can* when it
    is lying on the sand next to a gun that has a stock."""
    p = TrackedPart(mats)
    hard = p.torus((0.0, 0.0, FOOT_Z), CAN_R * 0.90, 0.009, axis='Z',
                   maj_seg=24, min_seg=8, mat=RUBBER)
    return _emit(p, hard, "Mesh_SlickCan_Foot", coll)


def neck(coll, mats):
    """Shoulder collar between the can's dome and the head.

    The neck runs from well inside the can's dome (z 0.250) to well inside the
    head (0.318), and its dark ring sits at 0.286 — chosen against the planes
    that are already there. The can's rolled cap has discs at 0.256 and 0.274
    and its dome closes at 0.270, and a first cut that put the neck's own faces
    a millimetre from those produced three `_zverify` clashes at once. Nothing
    here is within 4 mm of anything parallel.
    """
    p = TrackedPart(mats)
    hard = p.cyl((0.0, 0.0, (NECK_Z0 + NECK_Z1) / 2.0), NECK_R,
                 NECK_Z1 - NECK_Z0, axis='Z', seg=20, mat=GREY)
    hard += p.cyl((0.0, 0.0, 0.286), NECK_R * 1.32, 0.016, axis='Z',
                  seg=20, mat=DARK)
    return _emit(p, hard, "Mesh_SlickCan_Neck", coll)


def trigger(coll, mats):
    """The kit's thumb trigger and guard, on a collar round the neck.

    The can has no pistol grip on purpose — the hand wraps the body, the way a
    hand wraps a spray can, and a grip would make it a fourth gun. The guard is
    the part that earns its place: without it a fist closed round a 0.10 m can
    rests on the lever.
    """
    p = TrackedPart(mats)
    hard = kit.trigger_collar(p, (0.0, 0.0, COLLAR_Z), COLLAR_R, axis='Z',
                              accent=ACCENT)
    return _emit(p, hard, "Mesh_SlickCan_Trigger", coll)


def head(coll, mats):
    """The moulded head the fan is set into."""
    p = TrackedPart(mats)
    prof = kit.rounded_rect(HEAD_HX, HEAD_Z0, HEAD_Z1, 0.012, 0.010)
    hard = p.prism(prof, 2 * HEAD_HY, axis='Y', mat=SHELL)
    for f in hard:
        f.smooth = abs(f.normal.y) < 0.9 and max(abs(f.normal.x),
                                                 abs(f.normal.z)) < 0.999
    # Accent shoulder across the head's crown, tying it to the can's hoops.
    hard += p.box((0.0, 0.0, HEAD_Z1 - 0.004), (2 * HEAD_HX - 0.006,
                                                2 * HEAD_HY - 0.006, 0.012),
                  ACCENT)
    return _emit(p, hard, "Mesh_SlickCan_Head", coll)


def fan(coll, mats):
    """The wide flat fan nozzle — the only non-round mouth in the family."""
    p = TrackedPart(mats)
    hard = kit.nozzle_fan(p, (0.0, FAN_BACK_Y, FAN_Z), FAN_HW, FAN_H, FAN_D,
                          mat=SHELL, lip=ACCENT)
    # Pivot at the mouth, on the slot's centre line: scaling local X widens the
    # fan from its own lip, which is the moving part the design doc names.
    return _emit(p, hard, "Mesh_SlickCan_Fan", coll,
                 origin=(0.0, MOUTH_Y, FAN_Z))


def cap(coll, mats):
    """Pressure cap on the crown."""
    p = TrackedPart(mats)
    hard = p.cyl((0.0, 0.0, (CAP_Z0 + CAP_Z1) / 2.0), 0.024, CAP_Z1 - CAP_Z0,
                 axis='Z', seg=16, mat=CHROME, radius_top=0.019)
    return _emit(p, hard, "Mesh_SlickCan_Cap", coll)


def gauge(coll, mats):
    """The kit's supply gauge, running UP the flank rather than along it.

    Same plate, same lit strip, rotated a quarter turn — which is all
    `gauge_plate` needs, because it takes the bar direction as a vector rather
    than an axis letter. `OxygenGearBuilder` measures the strip's own vertices
    and takes the longest in-plane axis as the bar's, so a vertical bar is
    built, filled and coloured with nothing further written.
    """
    p = TrackedPart(mats)
    hard = kit.gauge_plate(p, (GAUGE_SIDE * CAN_R, 0.0, CAN_Z),
                           (GAUGE_SIDE, 0, 0), (0, 0, 1), accent=ACCENT)
    return _emit(p, hard, "Mesh_SlickCan_Gauge", coll)


# ---------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------

def check(coll):
    """Measure the result rather than trusting the build log.

    The can's long axis is Z, not Y — so the bracket is asserted against the
    largest dimension rather than against a named one.
    """
    lo = [1e9] * 3
    hi = [-1e9] * 3
    total = 0
    for o in sorted(coll.objects, key=lambda o: o.name):
        pts = [o.matrix_world @ v.co for v in o.data.vertices]
        for q in pts:
            for i in range(3):
                lo[i], hi[i] = min(lo[i], q[i]), max(hi[i], q[i])
        n = tri_count(o)
        total += n
        print("  %-28s y %.3f..%.3f  z %.3f..%.3f  tris %d"
              % (o.name, min(q.y for q in pts), max(q.y for q in pts),
                 min(q.z for q in pts), max(q.z for q in pts), n))
    size = [hi[i] - lo[i] for i in range(3)]
    print("  MODEL x %.4f..%.4f  y %.4f..%.4f  z %.4f..%.4f  tris %d"
          % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2], total))
    print("  LONGEST %.4f m (z)" % max(size))
    if max(size) != size[2]:
        raise SystemExit("The can's longest axis is not Z: %s" % size)
    if not 0.38 <= size[2] <= 0.42:
        raise SystemExit("Height %.4f m is outside the aerosol bracket "
                         "0.38..0.42" % size[2])
    print("  bracket OK")


def main():
    out = parse_out()
    start(out)
    coll = collection("Coll_SlickCan")
    mats = link_materials(MATS)

    body(coll, mats)
    foot(coll, mats)
    neck(coll, mats)
    trigger(coll, mats)
    head(coll, mats)
    fan(coll, mats)
    cap(coll, mats)
    gauge(coll, mats)

    kit.marker(coll, "Marker_Muzzle", (0.0, MOUTH_Y, FAN_Z), mats)
    kit.marker(coll, "Marker_Grip", (0.0, 0.0, PALM_Z), mats)
    kit.marker(coll, "Marker_Gauge", (GAUGE_SIDE * (CAN_R + 0.006), 0.0,
                                      CAN_Z), mats)

    report()
    save(out)
    check(coll)


main()
