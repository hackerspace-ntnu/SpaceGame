"""Foam gun — the bell-nozzled one-hander of the sprayer kit.

    blender --background --python foam_gun.py -- --out foam_gun.blend

A moulded pistol body with a **wide bell nozzle** and a stubby cartridge under
it. The bell is the whole identification: it is the only flared mouth in the
nine-item set, it is 0.176 m across on a 0.500 m item, and it says "this comes
out wide and slow" before the player has pulled the trigger — the opposite
claim to the flamethrower's narrow lance and the cryo sprayer's finned barrel
(`GDC-L1-UX-0004`).

Design: `docs/AI/systems/Artifacts/FoamGun.md`.

## Scale

Bracketed against `models/gear/dragon_bazooka.blend` (1.3685 m in Blender, worn
at `holdSize` 1.25 — the anchor of `ItemScaleLadder.cs`). Authored at
**0.500 m**: the one-handed sprayer bracket, a little over a third of the
anchor.

| Object | What it is |
|---|---|
| `Mesh_FoamGun_Body`      | the moulded shell with its safety-yellow flank plates |
| `Mesh_FoamGun_Bell`      | the flared nozzle and its rolled chrome lip |
| `Mesh_FoamGun_Iris`      | six overlapping shutter vanes, modelled part-open |
| `Mesh_FoamGun_Collar`    | chrome union where the bell throat enters the body |
| `Mesh_FoamGun_Cartridge` | the foam cartridge, slung under the barrel line |
| `Mesh_FoamGun_Saddle`    | the bridge and band holding the cartridge on |
| `Mesh_FoamGun_Grip`      | the kit's moulded grip, trigger and guard |
| `Mesh_FoamGun_Breech`    | rear boss and filler cap |
| `Mesh_FoamGun_Gauge`     | the kit's supply gauge, on the cartridge's outboard flank |
| `Marker_*`               | muzzle, grip, gauge face |

## Frame

+Z up, −Y forward, 1 unit = 1 m. The bell mouth is the most negative Y; the
filler cap the most positive.

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
from sprayer_kit import CHROME, DARK, GREY, SHELL, WORN  # noqa: E402

ACCENT = 8
MATS = kit.KIT_MATS + [
    "Mat_Plastic_Safety_Yellow",   # 8 the function colour: foam
]

# ── The long axis ────────────────────────────────────────────────────────────
BORE_Z = 0.020
BELL_THROAT_Y = -0.190
BELL_DEPTH = 0.134
MOUTH_Y = BELL_THROAT_Y - BELL_DEPTH      # -0.324
THROAT_R, MOUTH_R = 0.030, 0.082

BODY_Y0, BODY_Y1 = -0.200, 0.150
BODY_HX = 0.040
BODY_Z0, BODY_Z1 = -0.020, 0.062
BREECH_Y1 = 0.166

# ── The cartridge, forward of the trigger guard ──────────────────────────────
CART_Y, CART_LEN, CART_R = -0.115, 0.130, 0.034
CART_Z = -0.058
CART_FRONT = CART_Y - CART_LEN / 2
CART_BACK = CART_Y + CART_LEN / 2

GRIP_TOP = (0.0, 0.030, -0.016)

# See `flamethrower.py` for why the gauge and the plumbing take opposite flanks.
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
    """The moulded shell, with the yellow flank plates that colour-code it."""
    p = TrackedPart(mats)
    hard = kit.shell(p, BODY_HX, BODY_Z0, BODY_Z1, BODY_Y0, BODY_Y1,
                     taper=0.88)
    for sx in (-1, 1):
        hard += p.box((sx * (BODY_HX - 0.002), -0.040, 0.028),
                      (0.008, 0.120, 0.040), ACCENT)
    # Dorsal spine: a grey rail that gives the flat top an edge to catch light.
    hard += p.box((0.0, 0.010, BODY_Z1 - 0.002), (0.048, 0.180, 0.014), GREY)
    return _emit(p, hard, "Mesh_FoamGun_Body", coll)


def bell(coll, mats):
    """The flared nozzle. Its own object: the wave-2 rig aims foam from here,
    and the iris that sits in its mouth has to be able to turn without it."""
    p = TrackedPart(mats)
    hard = kit.nozzle_bell(p, (0.0, BELL_THROAT_Y, BORE_Z), THROAT_R, MOUTH_R,
                           BELL_DEPTH, mat=SHELL, lip=CHROME, seg=28)
    return _emit(p, hard, "Mesh_FoamGun_Bell", coll)


def iris(coll, mats):
    """The shutter, part-open. See `sprayer_kit.iris_vanes` for why part-open."""
    p = TrackedPart(mats)
    hard = kit.iris_vanes(p, (0.0, MOUTH_Y, BORE_Z), MOUTH_R, count=6,
                          mat=CHROME, open01=0.45)
    # Pivot on the bell's own axis at the mouth: the shutter turns about Y
    # there, so the prefab animates a local rotation and nothing else.
    return _emit(p, hard, "Mesh_FoamGun_Iris", coll,
                 origin=(0.0, MOUTH_Y, BORE_Z))


def collar(coll, mats):
    """Chrome union where the bell throat enters the shell's front face.

    It straddles that face by 12 mm each way rather than sitting against it —
    a ring resting exactly on the shell's front plane is the flicker every
    script in this library warns about.
    """
    p = TrackedPart(mats)
    hard = p.cyl((0.0, BELL_THROAT_Y - 0.008, BORE_Z), THROAT_R * 1.30, 0.044,
                 axis='Y', seg=24, mat=CHROME)
    # The dark throat plug, deep enough down the bell to be seen as a plug and
    # 5 mm clear of the bell's inner wall. Both rings are kept well away from
    # the shell's front plane: a disc parked 2 mm off it is a flicker, not a
    # gap, and `_zverify` says so.
    hard += p.cyl((0.0, BELL_THROAT_Y - 0.034, BORE_Z), THROAT_R * 0.87, 0.020,
                  axis='Y', seg=20, mat=DARK)
    return _emit(p, hard, "Mesh_FoamGun_Collar", coll)


def cartridge(coll, mats):
    """The stubby foam cartridge, slung under the barrel line.

    Placed entirely forward of the trigger guard: a cartridge under the
    receiver is where the guard has to be, and geometry through a hand is the
    assembled-model failure this family keeps having to design around.
    """
    p = TrackedPart(mats)
    hard = kit.tank(p, (0.0, CART_Y, CART_Z), 'Y', CART_R, CART_LEN,
                    body=WORN, cap=ACCENT, dome=0.28, bands=0)
    return _emit(p, hard, "Mesh_FoamGun_Cartridge", coll)


def saddle(coll, mats):
    """What actually holds the cartridge on: a bridge into the shell's
    underside and a band round the cartridge's front."""
    p = TrackedPart(mats)
    top = BODY_Z0 + 0.006
    bot = CART_Z + CART_R - 0.006
    hard = p.box((0.0, CART_Y + 0.020, (top + bot) / 2.0),
                 (0.034, 0.060, top - bot), DARK)
    hard += kit.clamp_band(p, (0.0, CART_FRONT + 0.026, CART_Z), 'Y', CART_R,
                           width=0.012, mat=CHROME, lug=DARK)
    return _emit(p, hard, "Mesh_FoamGun_Saddle", coll)


def grip(coll, mats):
    p = TrackedPart(mats)
    hard = kit.grip_moulded(p, GRIP_TOP, accent=ACCENT)
    return _emit(p, hard, "Mesh_FoamGun_Grip", coll)


def breech(coll, mats):
    """Rear boss and filler cap — where a fresh cartridge's charge goes in."""
    p = TrackedPart(mats)
    hard = p.cyl((0.0, BODY_Y1 + 0.006, 0.022), 0.028, 0.036, axis='Y',
                 seg=20, mat=GREY)
    hard += p.cyl((0.0, BREECH_Y1 - 0.004, 0.022), 0.020, 0.016, axis='Y',
                  seg=16, mat=ACCENT)
    return _emit(p, hard, "Mesh_FoamGun_Breech", coll)


def gauge(coll, mats):
    """The kit's supply gauge, on the cartridge's outboard flank."""
    p = TrackedPart(mats)
    hard = kit.gauge_plate(p, (GAUGE_SIDE * CART_R, CART_Y, CART_Z),
                           (GAUGE_SIDE, 0, 0), (0, 1, 0), length=0.084,
                           accent=ACCENT)
    return _emit(p, hard, "Mesh_FoamGun_Gauge", coll)


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
    coll = collection("Coll_FoamGun")
    mats = link_materials(MATS)

    body(coll, mats)
    bell(coll, mats)
    iris(coll, mats)
    collar(coll, mats)
    cartridge(coll, mats)
    saddle(coll, mats)
    grip(coll, mats)
    breech(coll, mats)
    gauge(coll, mats)

    kit.marker(coll, "Marker_Muzzle", (0.0, MOUTH_Y, BORE_Z), mats)
    kit.marker(coll, "Marker_Grip", tuple(kit.grip_palm(GRIP_TOP)), mats)
    kit.marker(coll, "Marker_Gauge",
               (GAUGE_SIDE * (CART_R + 0.006), CART_Y, CART_Z), mats)

    report()
    save(out)
    check(coll)


main()
