"""Ghost mount frame — the body screen's placeholder for an EMPTY back slot.

Two uprights and a crossbar: a rack for something big, and deliberately NOT a
pack — every player already wears the expedition rig on their back, so a pack
silhouette peeking over the shoulders would read as a second backpack. The
frame is built to be seen PAST THE SHOULDERS, which is the only part of the
back the body screen's front view shows: the crossbar rises above the
shoulder line and the uprights drop behind the shoulders.

## Frame and origin

Origin at the bottom centre of the uprights. Width along X, height along +Z
(up), depth along Y. `_exportlib` maps Blender Z onto Unity Y and −Y onto +Z,
so in Unity the frame stands up along the spine bone's Y with the crossbar
across the shoulders and its thin dimension along the bone's Z (out of the
back) — the same frame the wing pack's `WornFit` at localEuler (0, 0, 0) uses.
If a screenshot shows the frame edge-on, the fix is `localEuler` on the
prefab's WornFit in `GearGhostBuilder`, not this file.

## Scale

Authored 1:1. The prefab's `WornFit.size` = WIDTH keeps it 1:1 on the spine.

## Seating the members

The uprights set every outer bound: the crossbar is `2 * EMBED` narrower in Y
and its top sits `EMBED` below theirs, and the lower rail is thinner still, so
each member's ends are buried inside an upright rather than finishing flush
with one. Flush is the trap — two boxes of the same section meeting at a shared
top or a shared flank leave coplanar overlapping faces, which z-fight. Nothing
here shares a plane with anything, and the overall size is unchanged: the
uprights alone decide it.

    blender --background --python models/gear/ghost_mount_frame.py -- --out models/gear/ghost_mount_frame.blend
"""

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)

from _buildlib import *  # noqa: E402,F403

MATS = ["Mat_Paint_Hull_Bleached"]

WIDTH = 0.90        # crossbar span, shoulder to shoulder with room — WornFit.size
HEIGHT = 0.55       # uprights, from the spine bone up past the shoulder line
BAR = 0.05          # square section of the uprights, and the frame's depth
UPRIGHT_X = 0.36    # half the distance between the uprights
EMBED = 0.002       # how far a member's faces sit inside the uprights'


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    coll = collection("Coll_GhostMountFrame")

    p = Part(mats)
    for x in (-UPRIGHT_X, UPRIGHT_X):
        p.box((x, 0.0, HEIGHT / 2.0), (BAR, BAR, HEIGHT), 0)
    p.box((0.0, 0.0, HEIGHT - EMBED - BAR / 2.0),
          (WIDTH, BAR - 2.0 * EMBED, BAR), 0)
    # A lower rail, so an empty frame reads as a rack and not two sticks.
    p.box((0.0, 0.0, HEIGHT * 0.45), (UPRIGHT_X * 2.0, BAR * 0.6, BAR * 0.6), 0)
    p.finish("Mesh_GhostMountFrame", coll)

    report()
    save(out)


if __name__ == "__main__":
    main()
