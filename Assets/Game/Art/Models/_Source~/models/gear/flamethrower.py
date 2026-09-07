"""Flamethrower — the two-handed lance of the sprayer kit.

    blender --background --python flamethrower.py -- --out flamethrower.blend

The only long tool in the nine-item issued set, and the silhouette says so:
0.90 m from pilot tip to butt pad, two hands on it, a fuel bottle clamped under
the barrel and a hose looping out of it and forward to the muzzle. Everything
else in the family is a 0.4–0.5 m one-hander, so length alone identifies this
one across a room (`GDC-L1-UX-0003` — rank by salience; the item that is
*serious* is the item that is big).

Design: `docs/AI/systems/Artifacts/Flamethrower.md`. Not up for renegotiation
here; this file only decides what it looks like.

## Scale

Bracketed, not multiplied. `models/gear/dragon_bazooka.blend` measures 1.3685 m
along Y and is worn at `holdSize` 1.25 — the anchor of the ladder in
`Assets/Game/Editor/Items/ItemScaleLadder.cs`. This is authored at **0.900 m**,
about two thirds of the anchor: long enough to need two hands, short enough
that it is not read as a rocket launcher.

| Object | What it is |
|---|---|
| `Mesh_Flamethrower_Shell`    | the moulded white receiver, the kit's body |
| `Mesh_Flamethrower_Barrel`   | dark-steel lance tube through the shell to the muzzle |
| `Mesh_Flamethrower_Cage`     | the vented heat shroud — six ribs and two rings, not a tube |
| `Mesh_Flamethrower_Muzzle`   | collar, hazard band, igniter arm at the bore |
| `Mesh_Flamethrower_Pilot`    | the amber pilot flame, the one emissive that is not the gauge |
| `Mesh_Flamethrower_Bottle`   | the fuel bottle, clamped under the barrel |
| `Mesh_Flamethrower_Clamps`   | two bands and their yokes up to the barrel |
| `Mesh_Flamethrower_Hose`     | bottle valve to muzzle manifold, looping out on the plumbing flank |
| `Mesh_Flamethrower_Grip`     | the kit's moulded grip, trigger and guard |
| `Mesh_Flamethrower_Foregrip` | the same kit grip, upright and unguarded, for the support hand |
| `Mesh_Flamethrower_Stock`    | shoulder comb and rubber butt pad |
| `Mesh_Flamethrower_Gauge`    | the kit's supply gauge, on the bottle's outboard flank |
| `Marker_*`                   | muzzle, pilot, both hands, gauge face — see the BUILD record |

## Frame

+Z up, −Y forward, 1 unit = 1 m, matching `net_gun`, `gravel_blaster` and
`dragon_bazooka`. The bore runs along −Y at z +0.012; the muzzle is the most
negative Y on the model and the butt pad the most positive.

## Why the shroud is a cage

A perforated sleeve needs booleans through a tube, which is both expensive and
the kind of geometry that arrives in Unity with reversed normals. Six
longitudinal ribs between two rings reads as vented from any angle, silhouettes
better than a smooth tube, and costs a fifth of the triangles.

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

from mathutils import Matrix, Vector  # noqa: E402

from _buildlib import (collection, link_materials, parse_out,  # noqa: E402
                       report, save, start, tri_count)
from _tracked import TrackedPart  # noqa: E402

import sprayer_kit as kit  # noqa: E402
from sprayer_kit import (BLACK, CHROME, DARK, GREY, RUBBER,  # noqa: E402
                         SHELL, WORN)

ACCENT, WARN, PILOT = 8, 9, 10
MATS = kit.KIT_MATS + [
    "Mat_Paint_Safety_Orange",    # 8  the function colour: fire
    "Mat_Paint_Warn_Red",         # 9  the muzzle hazard band
    "Mat_Emissive_Amber",         # 10 the pilot flame, idling
]

# ── The long axis ────────────────────────────────────────────────────────────
MUZZLE_Y = -0.520                  # bore mouth
PILOT_Y = -0.534                   # the pilot tip, the model's forward extreme
BUTT_Y = 0.366                     # butt pad rear face
BORE_Z = 0.012
BORE_R = 0.019

SHELL_Y0, SHELL_Y1 = -0.250, 0.190
SHELL_HX = 0.043
SHELL_Z0, SHELL_Z1 = -0.040, 0.058

CAGE_Y0, CAGE_Y1 = -0.446, -0.300
CAGE_R = 0.031

MUZZLE_R = 0.030

# ── The bottle, under the barrel and forward of both hands ───────────────────
BOTTLE_Y, BOTTLE_LEN, BOTTLE_R = -0.360, 0.200, 0.038
BOTTLE_Z = -0.062                  # 5 mm clear under the cage, 43 mm under the bore
BOTTLE_FRONT = BOTTLE_Y - BOTTLE_LEN / 2
BOTTLE_BACK = BOTTLE_Y + BOTTLE_LEN / 2

# ── Which flank carries what ─────────────────────────────────────────────────
# The gauge and the plumbing go on OPPOSITE flanks so neither occludes the
# other. `GAUGE_SIDE` is +X in this build frame, which the export maps onto
# Unity −X — the flank a right-handed hold turns toward the camera. If the
# wave-2 seating finds it facing away, flipping these two constants is the
# whole change (`GDC-L1-UX-0003`: a readout the player cannot see is not one).
GAUGE_SIDE = 1.0
PLUMB_SIDE = -1.0

# ── The hands ────────────────────────────────────────────────────────────────
# Both grip tops are measured against the shell's TAPERED underside, not
# against `SHELL_Z0`. The shell's front station is 0.86 of the back one, so its
# bottom edge rises from −0.040 at the breech to −0.033 at the muzzle end — and
# a grip seated on the flat number floats 2 mm clear of the gun at the front,
# which is exactly what the first build did.
GRIP_TOP = (0.0, -0.060, -0.032)   # 4 mm up inside the shell's underside
FORE_TOP = (0.0, -0.226, -0.029)   # the support hand, 166 mm ahead of the other

# ── Stock ────────────────────────────────────────────────────────────────────
STOCK_Y0 = 0.120
PAD_Y = 0.352

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

def shell_body(coll, mats):
    """The moulded receiver, plus the accent flank plates that colour-code it.

    The plates are 4 mm proud and sunk 4 mm into the shell, so neither their
    back faces nor the shell's flanks land on a shared plane.
    """
    p = TrackedPart(mats)
    hard = kit.shell(p, SHELL_HX, SHELL_Z0, SHELL_Z1, SHELL_Y0, SHELL_Y1,
                     taper=0.86)
    for sx in (-1, 1):
        hard += p.box((sx * (SHELL_HX - 0.002), -0.060, 0.012),
                      (0.008, 0.150, 0.044), ACCENT)
    # Ejection-free top rail: somewhere for the eye to land, and it is what the
    # stock's comb runs into rather than meeting the shell on a plane.
    # It stops at y 0.100, short of the stock's comb at 0.120: the rail's
    # underside and the comb's top face otherwise sit 1.95 mm apart over a
    # 10 mm overlap, which `_zverify` reports as a real clash.
    hard += p.box((0.0, 0.020, SHELL_Z1 - 0.002), (0.052, 0.160, 0.014), GREY)
    return _emit(p, hard, "Mesh_Flamethrower_Shell", coll)


def barrel(coll, mats):
    """The lance: a hollow dark-steel tube, shell to bore mouth.

    Hollow rather than a capped cylinder. A solid barrel's front disc and the
    muzzle collar's front disc land on the same plane at y = MUZZLE_Y, which is
    the flicker the whole library warns about — and a bore you cannot see into
    reads as a rod, not a barrel. The plug 60 mm back stops the player seeing
    daylight through the gun.
    """
    p = TrackedPart(mats)
    y0, y1 = MUZZLE_Y, -0.140          # rear end buried well inside the shell
    hard = p.tube((0.0, (y0 + y1) / 2.0, BORE_Z), BORE_R, 0.005, y1 - y0,
                  axis='Y', seg=20, mat=DARK)
    hard += p.cyl((0.0, MUZZLE_Y + 0.060, BORE_Z), BORE_R - 0.006, 0.010,
                  axis='Y', seg=16, mat=BLACK)
    return _emit(p, hard, "Mesh_Flamethrower_Barrel", coll)


def cage(coll, mats):
    """The vented heat shroud: six ribs between two rings. See the docstring."""
    p = TrackedPart(mats)
    hard = []
    for i in range(6):
        a = 2 * math.pi * (i + 0.5) / 6
        c = Vector((CAGE_R * math.cos(a), (CAGE_Y0 + CAGE_Y1) / 2.0,
                    BORE_Z + CAGE_R * math.sin(a)))
        hard += p.box(c, (0.010, CAGE_Y1 - CAGE_Y0, 0.010), WORN,
                      rot=Matrix.Rotation(a, 4, 'Y'))
    for y in (CAGE_Y0 + 0.010, CAGE_Y1 - 0.010):
        hard += p.cyl((0.0, y, BORE_Z), CAGE_R * 1.06, 0.014, axis='Y',
                      seg=20, mat=CHROME)
    return _emit(p, hard, "Mesh_Flamethrower_Cage", coll)


def muzzle(coll, mats):
    """Collar, hazard band, hose manifold and the igniter arm beside the bore.

    The hazard band is `Mat_Paint_Warn_Red` and not the accent orange: this is
    the end that burns things, and red-for-danger is a convention worth
    honouring rather than reinventing (`GDC-L1-UX-0004`).
    """
    p = TrackedPart(mats)
    # Both rings are TUBES: a solid disc across the bore closes the barrel, and
    # its face lands on the barrel's own. Their walls stand 3 mm clear of the
    # barrel's outside, which is more than `_zverify`'s 2 mm coplanarity
    # tolerance — two concentric cylinders a hair apart read as parallel faces.
    hard = p.tube((0.0, MUZZLE_Y + 0.026, BORE_Z), MUZZLE_R + 0.002, 0.010,
                  0.044, axis='Y', seg=20, mat=CHROME)
    hard += p.tube((0.0, MUZZLE_Y + 0.030, BORE_Z), MUZZLE_R + 0.004, 0.016,
                   0.014, axis='Y', seg=20, mat=WARN)
    # Manifold where the hose lands, so the line does not simply stop in air.
    hard += p.box((PLUMB_SIDE * 0.030, MUZZLE_Y + 0.050, BORE_Z - 0.004),
                  (0.024, 0.034, 0.024), GREY)
    # Igniter arm, standing out beside the bore and reaching past the mouth. It
    # is on the gauge's flank on purpose: the pilot flame is state the player
    # reads, so it belongs on the side turned toward them.
    hard += p.box((GAUGE_SIDE * 0.026, MUZZLE_Y + 0.006, BORE_Z),
                  (0.010, 0.044, 0.010), DARK)
    return _emit(p, hard, "Mesh_Flamethrower_Muzzle", coll)


def pilot(coll, mats):
    """The idle pilot flame: a small amber bead on the igniter arm's tip.

    Its own object so the wave-2 prefab can switch it, scale it or hang a light
    on it without touching the muzzle mesh — the model tells the truth about
    the item's state, the way the flashlight gauntlet's bulb does.
    """
    p = TrackedPart(mats)
    hard = p.cyl((GAUGE_SIDE * 0.024, PILOT_Y + 0.008, BORE_Z), 0.007, 0.018,
                 axis='Y', seg=10, mat=PILOT, radius_top=0.003)
    # Pivot at the flame's ROOT, where it leaves the igniter arm: scaling local
    # Y stretches it forward from there, so an idle bead and a roaring jet are
    # the same object at two scales.
    return _emit(p, hard, "Mesh_Flamethrower_Pilot", coll,
                 origin=(GAUGE_SIDE * 0.024, PILOT_Y + 0.017, BORE_Z))


def bottle(coll, mats):
    """The fuel bottle, entirely forward of both hands.

    Placed forward on purpose: a bottle under the receiver is where the firing
    hand has to be, and a grip that passes through a tank is the classic
    assembled-model failure. Clamped under the barrel is also what the design
    doc asks for, so the constraint and the design agree.
    """
    p = TrackedPart(mats)
    hard = kit.tank(p, (0.0, BOTTLE_Y, BOTTLE_Z), 'Y', BOTTLE_R, BOTTLE_LEN,
                    body=WORN, cap=ACCENT, dome=0.50, bands=0)
    # Valve block on the rear cap: where the hose leaves.
    hard += p.box((PLUMB_SIDE * 0.026, BOTTLE_BACK - 0.012, BOTTLE_Z + 0.012),
                  (0.026, 0.030, 0.026), DARK)
    return _emit(p, hard, "Mesh_Flamethrower_Bottle", coll)


def clamps(coll, mats):
    """Two bands round the bottle, each yoked up to the barrel above it."""
    p = TrackedPart(mats)
    hard = []
    for y in (BOTTLE_FRONT + 0.040, BOTTLE_BACK - 0.040):
        hard += kit.clamp_band(p, (0.0, y, BOTTLE_Z), 'Y', BOTTLE_R,
                               width=0.014, mat=CHROME, lug=DARK)
        # The yoke: a strut from inside the bottle band up into the barrel.
        top = BORE_Z - BORE_R + 0.004
        bot = BOTTLE_Z + BOTTLE_R - 0.004
        hard += p.box((0.0, y, (top + bot) / 2.0),
                      (0.020, 0.016, top - bot), DARK)
    return _emit(p, hard, "Mesh_Flamethrower_Clamps", coll)


def hose(coll, mats):
    """Bottle valve to muzzle manifold, looping out and down on the +X side.

    Bowed rather than straight: a straight run between two fittings reads as
    rigid pipework, and the design doc asks for a hose.
    """
    p = TrackedPart(mats)
    a = Vector((PLUMB_SIDE * 0.030, BOTTLE_BACK - 0.014, BOTTLE_Z + 0.020))
    b = Vector((PLUMB_SIDE * 0.030, MUZZLE_Y + 0.050, BORE_Z - 0.006))
    pts = kit.arc(a, b, 0.042, steps=11, up=(PLUMB_SIDE * 0.62, 0.0, -0.78))
    hard = kit.hose(p, pts, 0.009, mat=RUBBER, seg=8)
    return _emit(p, hard, "Mesh_Flamethrower_Hose", coll)


def grip(coll, mats):
    """The kit's moulded grip — the same part on every issued sprayer."""
    p = TrackedPart(mats)
    hard = kit.grip_moulded(p, GRIP_TOP, accent=ACCENT)
    return _emit(p, hard, "Mesh_Flamethrower_Grip", coll)


def foregrip(coll, mats):
    """The support hand: the kit grip again, upright, shorter, and unguarded.

    `components/mechanical/weapon_grip.blend`'s `Coll_WeaponGrip_Fore` was
    built and looked at first, because reuse beats rebuilding and the dragon
    bazooka already appends exactly that object. It is rejected here and the
    reason is visible in one render: its canvas wrap and ply cheeks are a
    scavenged weapon's language, and on a white issued shell they read as a
    part off a different model. Reuse still wins — this is the kit's own grip
    with `rake` near zero and no guard, so the two hands hold the same part.
    """
    p = TrackedPart(mats)
    hard = kit.grip_moulded(p, FORE_TOP, accent=ACCENT, length=0.096,
                            rake=0.05, trigger=False, guard=False)
    return _emit(p, hard, "Mesh_Flamethrower_Foregrip", coll)


def stock(coll, mats):
    """A skeleton stock: a comb rail over an open bay over a lower rail.

    Open rather than solid on purpose. A filled stock at this length is a
    240 mm plank and reads as an unfinished blockout; the hole is what makes
    the back half of the item legible as a *stock* in silhouette, which is the
    only cue at distance that this one is shouldered (`GDC-L1-UX-0003`).
    """
    p = TrackedPart(mats)
    # The comb's crest RISES over the shell's roof rather than continuing it.
    # Level with it, the two top surfaces sit 0.7 mm apart over 66 mm of
    # overlap — `_zverify` calls that a clash and it is right to. A comb that
    # steps up is also what a shoulder stock actually does.
    comb = [(STOCK_Y0, 0.024), (PAD_Y - 0.010, 0.014), (PAD_Y - 0.010, 0.048),
            (STOCK_Y0, 0.066)]
    hard = p.prism(comb, 0.050, axis='X', mat=SHELL)
    lower = [(STOCK_Y0 + 0.024, -0.030), (PAD_Y - 0.010, -0.034),
             (PAD_Y - 0.010, -0.008), (STOCK_Y0 + 0.048, 0.004)]
    hard += p.prism(lower, 0.042, axis='X', mat=SHELL)
    # Butt plate closing the bay, then the rubber pad standing proud of it.
    hard += p.box((0.0, PAD_Y - 0.002, 0.008), (0.050, 0.024, 0.088), GREY)
    hard += p.box((0.0, PAD_Y + 0.014, 0.008), (0.046, 0.022, 0.084), RUBBER)
    # Sling loop on the lower rail.
    hard += p.torus((0.0, 0.230, -0.026), 0.016, 0.004, axis='X', maj_seg=16,
                    min_seg=6, mat=CHROME)
    return _emit(p, hard, "Mesh_Flamethrower_Stock", coll)


def gauge(coll, mats):
    """The supply gauge, on the bottle's outboard flank where the carrying hand
    does not cover it. `Mesh_*_Gauge` and `Mat_Emissive_Green_CRT` are the two
    handles `OxygenGearBuilder` needs; see `docs/AI/systems/SupplyGauge.md`."""
    p = TrackedPart(mats)
    hard = kit.gauge_plate(p, (GAUGE_SIDE * BOTTLE_R, BOTTLE_Y, BOTTLE_Z),
                           (GAUGE_SIDE, 0, 0), (0, 1, 0), accent=ACCENT)
    return _emit(p, hard, "Mesh_Flamethrower_Gauge", coll)


# ---------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------

def check(coll):
    """Measure what was built rather than trusting the log.

    A builder run can execute stale code and report success; only the numbers
    tell. Asserts the item's own length bracket.
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
        print("  %-32s y %.3f..%.3f  z %.3f..%.3f  tris %d"
              % (o.name, min(q.y for q in pts), max(q.y for q in pts),
                 min(q.z for q in pts), max(q.z for q in pts), n))
    length = hi[1] - lo[1]
    print("  MODEL x %.4f..%.4f  y %.4f..%.4f  z %.4f..%.4f  tris %d"
          % (lo[0], hi[0], lo[1], hi[1], lo[2], hi[2], total))
    print("  LENGTH %.4f m" % length)
    if not 0.86 <= length <= 0.94:
        raise SystemExit("Length %.4f m is outside the two-handed bracket "
                         "0.86..0.94" % length)
    print("  bracket OK")


def main():
    out = parse_out()
    start(out)
    coll = collection("Coll_Flamethrower")
    mats = link_materials(MATS)

    shell_body(coll, mats)
    barrel(coll, mats)
    cage(coll, mats)
    muzzle(coll, mats)
    pilot(coll, mats)
    bottle(coll, mats)
    clamps(coll, mats)
    hose(coll, mats)
    grip(coll, mats)
    foregrip(coll, mats)
    stock(coll, mats)
    gauge(coll, mats)

    kit.marker(coll, "Marker_Muzzle", (0.0, MUZZLE_Y, BORE_Z), mats)
    kit.marker(coll, "Marker_Pilot", (GAUGE_SIDE * 0.024, PILOT_Y, BORE_Z),
               mats)
    kit.marker(coll, "Marker_Grip", tuple(kit.grip_palm(GRIP_TOP)), mats)
    kit.marker(coll, "Marker_GripFore",
               tuple(kit.grip_palm(FORE_TOP, length=0.096, rake=0.05)), mats)
    kit.marker(coll, "Marker_Gauge",
               (GAUGE_SIDE * (BOTTLE_R + 0.006), BOTTLE_Y, BOTTLE_Z), mats)

    report()
    save(out)
    check(coll)


main()
