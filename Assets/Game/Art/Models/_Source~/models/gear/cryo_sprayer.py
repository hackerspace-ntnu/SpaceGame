"""Cryo sprayer — the finned-barrel one-hander of the sprayer kit.

    blender --background --python cryo_sprayer.py -- --out cryo_sprayer.blend

A moulded body with a **squat bottle riding on its spine**, a ribbed cryogenic
line arcing forward out of it, a **finned barrel** and rime frosting the last
few centimetres. Three of those four cues are about temperature, and none of
them is a colour: the sprayer has to be told apart from the foam gun by a
player who cannot see the difference between pale blue and yellow
(`GDC-L1-UX-0006`). The bottle on top rather than underneath is the silhouette
difference; the fins are the surface difference.

Design: `docs/AI/systems/Artifacts/CryoSprayer.md`.

## Scale

Bracketed against `models/gear/dragon_bazooka.blend` (1.3685 m in Blender, worn
at `holdSize` 1.25 — the anchor of `ItemScaleLadder.cs`). Authored at
**0.500 m**, the one-handed sprayer bracket, matching the foam gun exactly so
the two read as issued peers.

| Object | What it is |
|---|---|
| `Mesh_CryoSprayer_Body`    | the moulded shell with its cold-blue flank plates |
| `Mesh_CryoSprayer_Barrel`  | the finned cooling barrel — tube plus seven fins |
| `Mesh_CryoSprayer_Rime`    | frost build-up over the front fins and the tip |
| `Mesh_CryoSprayer_Bottle`  | the squat cryogen bottle, saddled on the spine |
| `Mesh_CryoSprayer_Saddle`  | the two cradles and the strap holding it there |
| `Mesh_CryoSprayer_Line`    | the corrugated line, bottle valve to barrel throat |
| `Mesh_CryoSprayer_Grip`    | the kit's moulded grip, trigger and guard |
| `Mesh_CryoSprayer_Breech`  | rear boss and regulator cap |
| `Mesh_CryoSprayer_Gauge`   | the kit's supply gauge, on the bottle's outboard flank |
| `Marker_*`                 | muzzle, grip, gauge face |

## Rime, and why it is `Mat_Paint_White_Arctic`

The palette has no ice or frost material and this build does not add one. Arctic
white is a chalky cool off-white at roughness 0.58, which is what frost looks
like; what makes the part read as *rime* rather than as paint is its shape —
five overlapping rings of different radii, so the sleeve is lumpy where every
other surface on the item is machined. Adding a fifty-sixth near-white to the
palette to say the same thing would be the "eleventh grey" the palette rules
exist to prevent.

## Frame

+Z up, −Y forward, 1 unit = 1 m. The nozzle tip is the most negative Y; the
regulator cap the most positive.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "props"))

from mathutils import Vector  # noqa: E402

from _buildlib import (collection, link_materials, parse_out,  # noqa: E402
                       report, save, start, tri_count)
from _tracked import TrackedPart  # noqa: E402

import sprayer_kit as kit  # noqa: E402
from sprayer_kit import CHROME, DARK, GREY, RUBBER, SHELL, WORN  # noqa: E402

ACCENT = 8
MATS = kit.KIT_MATS + [
    "Mat_Paint_Blue_Station",      # 8 the function colour: cold
]

# ── The long axis ────────────────────────────────────────────────────────────
BORE_Z = 0.010
TIP_Y, BARREL_Y1 = -0.310, -0.060
BORE_R = 0.020

BODY_Y0, BODY_Y1 = -0.086, 0.140
BODY_HX = 0.036
BODY_Z0, BODY_Z1 = -0.018, 0.048
BREECH_Y1 = 0.190

# ── The bottle, on the spine ─────────────────────────────────────────────────
BOTTLE_Y, BOTTLE_LEN, BOTTLE_R = 0.058, 0.152, 0.046
BOTTLE_Z = 0.098                   # 4 mm clear over the shell's roof
BOTTLE_FRONT = BOTTLE_Y - BOTTLE_LEN / 2
BOTTLE_BACK = BOTTLE_Y + BOTTLE_LEN / 2

GRIP_TOP = (0.0, 0.020, -0.014)

# See `flamethrower.py`: gauge and plumbing take opposite flanks.
GAUGE_SIDE = 1.0
PLUMB_SIDE = -1.0

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
    """The moulded shell, with the cold-blue flank plates."""
    p = TrackedPart(mats)
    hard = kit.shell(p, BODY_HX, BODY_Z0, BODY_Z1, BODY_Y0, BODY_Y1,
                     taper=0.90, r_top=0.020, r_bot=0.012)
    for sx in (-1, 1):
        hard += p.box((sx * (BODY_HX - 0.002), 0.026, 0.018),
                      (0.008, 0.110, 0.034), ACCENT)
    # A collar where the barrel enters the shell, in the accent. The blue is
    # `Mat_Paint_Blue_Station`, a pale enamel, and a single flank plate of it
    # against `Mat_Paint_White_Arctic` was too quiet to carry the colour code
    # on its own — so the accent is repeated on the collar, the bottle caps and
    # the regulator, which is what makes 'the blue one' readable at a glance
    # (`GDC-L1-UX-0003`).
    hard += p.cyl((0.0, BODY_Y0 + 0.008, BORE_Z), 0.032, 0.030, axis='Y',
                  seg=20, mat=ACCENT)
    return _emit(p, hard, "Mesh_CryoSprayer_Body", coll)


def barrel(coll, mats):
    """The finned cooling barrel — the item's identifying surface."""
    p = TrackedPart(mats)
    hard = kit.nozzle_finned(p, TIP_Y, BARREL_Y1, BORE_R, at=(0.0, BORE_Z),
                             fins=7, mat=DARK, fin_mat=CHROME, seg=18)
    # The bore itself, recessed, so the tip is not a flat disc.
    hard += p.cyl((0.0, TIP_Y + 0.012, BORE_Z), BORE_R * 0.58, 0.030,
                  axis='Y', seg=14, mat=kit.BLACK)
    return _emit(p, hard, "Mesh_CryoSprayer_Barrel", coll)


def rime(coll, mats):
    """Frost on the working end: five overlapping lumps of uneven radius.

    Its own object so the wave-2 prefab can grow it while spraying and clear it
    when idle, which is the moving part the design doc asks for. See the module
    docstring for why it is a palette white and not a new material.

    Built from TORI rather than short cylinders. Rings were the first cut and
    every one of them put a flat disc perpendicular to the barrel, half a
    millimetre from a fin's own flat disc — `_zverify` found two such pairs and
    nudging the rings apart only moved the problem to the next fin. A torus has
    no flat face anywhere on it, so no placement can produce one, and lumps
    read as frost better than rings do.
    """
    p = TrackedPart(mats)
    hard = []
    for y, major, minor in ((-0.306, 0.024, 0.010), (-0.296, 0.028, 0.009),
                            (-0.286, 0.023, 0.011), (-0.276, 0.030, 0.008),
                            (-0.264, 0.026, 0.009)):
        hard += p.torus((0.0, y, BORE_Z), major, minor, axis='Y', maj_seg=16,
                        min_seg=8, mat=SHELL)
    # Pivot at the nozzle tip, on the bore axis: scaling local Y grows the frost
    # BACK along the barrel from the coldest point, which is the direction the
    # design doc describes it creeping.
    return _emit(p, hard, "Mesh_CryoSprayer_Rime", coll,
                 origin=(0.0, TIP_Y, BORE_Z))


def bottle(coll, mats):
    """The squat cryogen bottle, lying along the spine.

    On TOP, not underneath. Underneath is where the foam gun's cartridge is and
    where the flamethrower's fuel bottle is, and three items with a tank in the
    same place are three recolours of one gun. It is also the honest place for
    it: a cryogen bottle is the heaviest thing on the item and sitting it over
    the hand is what a designer would do.
    """
    p = TrackedPart(mats)
    hard = kit.tank(p, (0.0, BOTTLE_Y, BOTTLE_Z), 'Y', BOTTLE_R, BOTTLE_LEN,
                    body=WORN, cap=ACCENT, dome=0.45, bands=0)
    # Valve block on the front cap, where the line leaves.
    hard += p.box((PLUMB_SIDE * 0.026, BOTTLE_FRONT + 0.012, BOTTLE_Z + 0.010),
                  (0.026, 0.028, 0.026), DARK)
    return _emit(p, hard, "Mesh_CryoSprayer_Bottle", coll)


def saddle(coll, mats):
    """Two cradles under the bottle and a strap over it."""
    p = TrackedPart(mats)
    hard = []
    top = BOTTLE_Z - BOTTLE_R + 0.006
    bot = BODY_Z1 - 0.006
    for y in (BOTTLE_FRONT + 0.030, BOTTLE_BACK - 0.026):
        hard += p.box((0.0, y, (top + bot) / 2.0),
                      (0.040, 0.020, top - bot), DARK)
    hard += kit.clamp_band(p, (0.0, BOTTLE_BACK - 0.026, BOTTLE_Z), 'Y',
                           BOTTLE_R, width=0.012, mat=CHROME, lug=DARK)
    return _emit(p, hard, "Mesh_CryoSprayer_Saddle", coll)


def line(coll, mats):
    """The corrugated cryogenic line: bottle valve, out and down, barrel throat.

    Ribbed, which `sprayer_kit.hose` does by alternating the swept radius —
    `Part.torus` takes an axis letter and no rotation, so its rings cannot be
    made to follow a curve's tangent.
    """
    p = TrackedPart(mats)
    a = Vector((PLUMB_SIDE * 0.030, BOTTLE_FRONT + 0.006, BOTTLE_Z + 0.006))
    b = Vector((PLUMB_SIDE * 0.024, BARREL_Y1 - 0.030, BORE_Z + 0.006))
    pts = kit.arc(a, b, 0.030, steps=13, up=(PLUMB_SIDE * 0.80, 0.0, -0.60))
    hard = kit.hose(p, pts, 0.008, mat=RUBBER, seg=8, ribbed=True,
                    rib_scale=1.45)
    return _emit(p, hard, "Mesh_CryoSprayer_Line", coll)


def grip(coll, mats):
    p = TrackedPart(mats)
    hard = kit.grip_moulded(p, GRIP_TOP, accent=ACCENT)
    return _emit(p, hard, "Mesh_CryoSprayer_Grip", coll)


def breech(coll, mats):
    """Rear boss and regulator cap."""
    p = TrackedPart(mats)
    hard = p.cyl((0.0, BODY_Y1 + 0.010, 0.014), 0.026, 0.044, axis='Y',
                 seg=20, mat=GREY)
    hard += p.cyl((0.0, BREECH_Y1 - 0.008, 0.014), 0.019, 0.020, axis='Y',
                  seg=16, mat=ACCENT)
    return _emit(p, hard, "Mesh_CryoSprayer_Breech", coll)


def gauge(coll, mats):
    """The kit's supply gauge, on the bottle's outboard flank."""
    p = TrackedPart(mats)
    hard = kit.gauge_plate(p, (GAUGE_SIDE * BOTTLE_R, BOTTLE_Y, BOTTLE_Z),
                           (GAUGE_SIDE, 0, 0), (0, 1, 0), accent=ACCENT)
    return _emit(p, hard, "Mesh_CryoSprayer_Gauge", coll)


# ---------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------

def check(coll):
    """Measure the result rather than trusting the build log."""
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
        print("  %-30s y %.3f..%.3f  z %.3f..%.3f  tris %d"
              % (o.name, min(q.y for q in pts), max(q.y for q in pts),
                 min(q.z for q in pts), max(q.z for q in pts), n))
    length = hi[1] - lo[1]
    print("  MODEL x %.4f..%.4f  y %.4f..%.4f  z %.4f..%.4f  tris %d"
          % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2], total))
    print("  LENGTH %.4f m" % length)
    if not 0.47 <= length <= 0.53:
        raise SystemExit("Length %.4f m is outside the one-handed sprayer "
                         "bracket 0.47..0.53" % length)
    print("  bracket OK")


def main():
    out = parse_out()
    start(out)
    coll = collection("Coll_CryoSprayer")
    mats = link_materials(MATS)

    body(coll, mats)
    barrel(coll, mats)
    rime(coll, mats)
    bottle(coll, mats)
    saddle(coll, mats)
    line(coll, mats)
    grip(coll, mats)
    breech(coll, mats)
    gauge(coll, mats)

    kit.marker(coll, "Marker_Muzzle", (0.0, TIP_Y, BORE_Z), mats)
    kit.marker(coll, "Marker_Grip", tuple(kit.grip_palm(GRIP_TOP)), mats)
    kit.marker(coll, "Marker_Gauge",
               (GAUGE_SIDE * (BOTTLE_R + 0.006), BOTTLE_Y, BOTTLE_Z), mats)

    report()
    save(out)
    check(coll)


main()
