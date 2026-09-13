"""Ghost device — the body screen's placeholder for an EMPTY forearm slot.

A footplate and a blank module standing on it: the shape a gauntlet takes when
you stop looking at any particular one. Not a bracer, because since 2026-09-04
the bracer is real and permanent — the player is already wearing one on each
arm, so a ghost bracer would be a translucent copy of a solid thing six
centimetres away. What an empty slot is missing is the *device*, and this is
the silhouette of a device.

It replaced `ghost_gauntlet_export.py`, which shipped `gauntlet_base.blend`'s
Plain variation for the same job under the old rules.

## Frame and origin

The gauntlet family frame (`_gauntlet.py`): arm along Y, wrist at y = 0, elbow
toward +Y, forward −Y, dorsal +Z. Authored 1:1 at true suit scale, like the
base, so `GauntletFit`'s family defaults seat it with no numbers of its own —
`ForearmSeat.Apply` puts it exactly where a real device would land.

## Sitting on the deck, not floating over it

Every constant is derived from the base's hardpoint contract, imported rather
than retyped, so a deck that moves takes the ghost with it. The foot sinks
`SINK` into the deck plane the way every real device's foot does, and is inset
inside the deck footprint so it clears the deck's own top bevel.

## Height

`BODY_TOP` is the family's median device top, not its tallest. A ghost that
stood as tall as the repulsor would promise more than most gauntlets deliver;
one flush with the deck would not read as a device at all.

## Seating the two parts

The body is embedded `EMBED` into the foot rather than resting flush on it.
Flush is the trap: two boxes meeting on a shared plane leave coplanar faces,
which z-fight — and a translucent ghost shows the fight twice over, because
both faces are drawn.

    blender --background --python models/gear/ghost_device.py -- --out models/gear/ghost_device.blend
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, _HERE)

from _buildlib import *  # noqa: E402,F403
from _gauntlet import (BASE_DECK_HX, BASE_DECK_Y0, BASE_DECK_Y1,  # noqa: E402
                       BASE_DECK_Z)

MATS = ["Mat_Paint_Hull_Bleached"]

SINK = 0.004                      # into the deck, exactly as a real device's foot does
INSET = 0.004                     # inside the deck footprint, clear of its top bevel
FOOT_TOP = BASE_DECK_Z + 0.018    # 0.268
BODY_TOP = 0.520                  # the family's median device top
BODY_HX = 0.098                   # cantilevers past the foot, as most of the family does
BODY_INSET = 0.012                # the body is shorter along the arm than its foot
EMBED = 0.006                     # how far the body sits inside the foot


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_GhostDevice")

    p = Part(mats)
    p.slab((-BASE_DECK_HX + INSET, BASE_DECK_Y0 + INSET, BASE_DECK_Z - SINK),
           (BASE_DECK_HX - INSET, BASE_DECK_Y1 - INSET, FOOT_TOP), 0)
    p.finish("Mesh_GhostDevice_Foot", coll)

    p = Part(mats)
    p.slab((-BODY_HX, BASE_DECK_Y0 + BODY_INSET, FOOT_TOP - EMBED),
           (BODY_HX, BASE_DECK_Y1 - BODY_INSET, BODY_TOP), 0)
    p.finish("Mesh_GhostDevice_Body", coll)

    report()
    save(out)


if __name__ == "__main__":
    main()
