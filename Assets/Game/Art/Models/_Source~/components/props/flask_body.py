"""Thrown-flask shells — the moulded bottle bodies of the issued kit.

A thrown artifact in this kit is a sealed flask, not scavenged glass: a moulded
shell in the kit's off-white, a machined neck that takes any collar from
`flask_collar.blend`, and a glass band around the upper third so whatever is
stored inside is visible from every direction. The band is deliberately a full
ring rather than a porthole — a thrown bottle lands in whatever orientation it
pleases, and a window on one face is a window the player is looking at the back
of half the time.

`components/props/gas_bottle.blend` was the obvious candidate for reuse and was
rejected: it is a *pressure vessel* with a dial gauge as its largest feature —
industrial plumbing you plug a hose into. A thrown flask has no hose, no gauge
and no valve; it has a window and a closure. Sharing the part would have blunted
both. `oxygen_tank.blend` is a 0.39 m back-worn cylinder, a different object at a
different scale.

Each variation is four objects, because each has a different life:

  `_Shell`   the lower moulded body, capped — the part a hand closes on
  `_Cowl`    the upper shoulder, capped, ending in the neck seat
  `_Window`  a glass ring bridging the gap between the two
  `_Bumper`  the rubber drop ring, the only soft part

Shell and cowl are separate rather than one bored vessel because the gap between
them *is* the window aperture: two capped solids with a glass ring across the gap
gives an interior the eye reads as an interior, with no boolean and no interior
faces. Each is embedded 3 mm into the glass, never flush with it — see the
z-fighting note below.

Origin is at the **base**, on the axis, and the axis runs up **+Z**, matching
`gas_bottle.py`. A bottle is placed by where it stands or by where a hand closes
on it, and a base origin serves both.

**Every collar in `flask_collar.blend` seats on a neck of outer radius
`SEAT_R`.** That single number is what makes the two files a family rather than
two files: any body takes any closure.

No bevel pass anywhere in this file, and that is deliberate. `bmesh.ops.bevel`
stamps material index 0 on every face it makes, and at 0.13 m a 1 mm bevel on a
3 mm glass wall is half its thickness — trap 1 and trap 2 in
`project_buildlib_traps`. The chamfers are in the loft profiles instead, where
they are exact and cost nothing.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.dirname(os.path.dirname(_HERE)))

from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402

# Index 0 is a structural metal because `bmesh.ops.bevel` stamps every face it
# creates with material index 0, and a forgotten `mat=` argument lands there
# too — see project_buildlib_traps.
STEEL, DARK, SHELL, GLASS, RUBBER, CHROME, BLACK, YELLOW = range(8)
MATS = ["Mat_Metal_Steel_Worn",        # machined neck ring, hardware
        "Mat_Metal_Steel_Dark",        # moulded rib crowns, base ring
        "Mat_Paint_White_Arctic",      # the issued shell — the kit's body colour
        "Mat_Glass_Canopy_Tinted",      # the window band
        "Mat_Plastic_Rubber_Black",    # drop bumper, base foot
        "Mat_Metal_Chrome_Scuffed",    # neck lip
        "Mat_Neutral_Black_Matte",     # shadow gaps, interior backing
        "Mat_Plastic_Safety_Yellow"]   # safety tab

# The one number the whole family agrees on: the outer radius of the neck every
# collar seats over. Change it here and in flask_collar.py together, or the kit
# quietly stops fitting itself.
SEAT_R = 0.031

RING_SEG = 28          # a 0.13 m bottle read at arm's length; 28 is round enough


def ring(r, seg=RING_SEG):
    """A circular loft profile of radius `r` in the plane normal to the axis."""
    return [(r * math.cos(2 * math.pi * i / seg),
             r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]


def revolve(part, sections, mat):
    """Loft a (z, radius) silhouette into a capped solid of revolution."""
    return part.loft([(z, ring(r)) for z, r in sections], axis='Z', mat=mat)


def body(coll, mats, tag, spec):
    """Emit one flask body variation: shell, cowl, window and bumper."""
    shell = TrackedPart(mats)
    revolve(shell, spec["shell"], SHELL)
    # A crown band on the widest rib, so the moulding reads as tooled rather
    # than turned. Sits 0.4 mm proud of the rib it rides, never flush with it.
    zc, rc = spec["rib"]
    shell.torus((0, 0, zc), rc + 0.0004, 0.0022, 'Z', RING_SEG, 8, DARK)
    shell.restamp(tag + " shell")
    shell.finish("Mesh_FlaskBody_%s_Shell" % tag, coll)

    cowl = TrackedPart(mats)
    revolve(cowl, spec["cowl"], SHELL)
    # The neck lip: a chrome ring the collar's sleeve closes against. It is the
    # only bright metal on the body and it is what makes the neck read as a
    # machined fitting rather than a moulded spout.
    zn = spec["cowl"][-1][0]
    cowl.torus((0, 0, zn - 0.003), SEAT_R + 0.0016, 0.0022, 'Z', RING_SEG, 8,
               CHROME)
    cowl.restamp(tag + " cowl")
    cowl.finish("Mesh_FlaskBody_%s_Cowl" % tag, coll)

    win = TrackedPart(mats)
    z0, z1, rw, tw = spec["window"]
    win.tube((0, 0, (z0 + z1) / 2.0), rw, tw, z1 - z0, 'Z', RING_SEG, GLASS)
    win.restamp(tag + " window")
    win.finish("Mesh_FlaskBody_%s_Window" % tag, coll)

    bump = TrackedPart(mats)
    zb, rb, mb = spec["bumper"]
    bump.torus((0, 0, zb), rb, mb, 'Z', RING_SEG, 10, RUBBER)
    if spec.get("foot"):
        zf, rf, df = spec["foot"]
        bump.cyl((0, 0, zf), rf, df, 'Z', RING_SEG, RUBBER)
    bump.restamp(tag + " bumper")
    bump.finish("Mesh_FlaskBody_%s_Bumper" % tag, coll)


# --------------------------------------------------------------------------
# The four variations
#
# They differ in silhouette first, which is the axis the eye actually reads at
# a glance — a squat puck, a shouldered flask, a slim tube and an egg are four
# different objects, not one object at four sizes.
# --------------------------------------------------------------------------

SQUAT = {                       # wide and low: the bottled singularity
    "shell": [(0.000, 0.058), (0.006, 0.066), (0.030, 0.0660),
              (0.034, 0.0685), (0.040, 0.0685), (0.044, 0.0660),
              (0.080, 0.0660), (0.086, 0.0615)],
    "rib": (0.037, 0.0685),
    "cowl": [(0.121, 0.0615), (0.127, 0.0660), (0.140, 0.0645),
             (0.152, 0.0520), (0.162, 0.0380), (0.168, SEAT_R)],
    "window": (0.083, 0.124, 0.0605, 0.0035),
    "bumper": (0.002, 0.0600, 0.0070),
    "foot": (-0.002, 0.050, 0.008),
}

SHOULDERED = {                  # tall with a pronounced shoulder: the storm flask
    "shell": [(0.000, 0.042), (0.005, 0.049), (0.028, 0.0490),
              (0.032, 0.0512), (0.038, 0.0512), (0.042, 0.0490),
              (0.072, 0.0490), (0.078, 0.0450)],
    "rib": (0.035, 0.0512),
    "cowl": [(0.110, 0.0435), (0.116, 0.0450), (0.124, 0.0430),
             (0.132, 0.0370), (0.138, SEAT_R)],
    "window": (0.075, 0.113, 0.0435, 0.0035),
    "bumper": (0.002, 0.0440, 0.0065),
    "foot": (-0.002, 0.035, 0.008),
}

SLIM = {                        # a straight tube with a knurled waist
    "shell": [(0.000, 0.028), (0.005, 0.034), (0.040, 0.0340),
              (0.044, 0.0362), (0.050, 0.0362), (0.054, 0.0340),
              (0.088, 0.0340), (0.093, 0.0315)],
    "rib": (0.047, 0.0362),
    "cowl": [(0.129, 0.0315), (0.134, 0.0340), (0.160, 0.0340),
             (0.172, 0.0322), (0.180, SEAT_R)],
    "window": (0.090, 0.132, 0.0310, 0.0030),
    "bumper": (0.002, 0.0305, 0.0055),
    "foot": (-0.002, 0.024, 0.008),
}

OVOID = {                       # an egg that will not stand up on its own
    "shell": [(0.000, 0.030), (0.008, 0.042), (0.020, 0.0520),
              (0.034, 0.0575), (0.048, 0.0575), (0.054, 0.0545),
              (0.060, 0.0505)],
    "rib": (0.041, 0.0575),
    "cowl": [(0.092, 0.0495), (0.100, 0.0500), (0.112, 0.0420),
             (0.122, SEAT_R)],
    "window": (0.057, 0.095, 0.0495, 0.0032),
    "bumper": (0.006, 0.0270, 0.0060),
}


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for tag, spec in (("Squat", SQUAT), ("Shouldered", SHOULDERED),
                      ("Slim", SLIM), ("Ovoid", OVOID)):
        body(collection("Coll_FlaskBody_%s" % tag), mats, tag, spec)

    report()
    save(out)


# Guarded, because `flask_collar.py` imports MATS and SEAT_R from here so the
# two halves of the family cannot drift apart. Unguarded, that import runs this
# build and writes the bodies into the collar's file — which it did once, and
# the only symptom was a collar .blend full of bottles.
if __name__ == "__main__":
    main()
