"""Retractable blades — the steel a wrist sheath throws out.

    blender --background --python retract_blade.py -- --out retract_blade.blend

Three blades that ride the same sheath, in three collections, so one device can
be a stabbing spike, a sweeping cutter or a saw without rebuilding the sheath:

| Collection | Object | Silhouette |
|---|---|---|
| `Coll_RetractBlade_Straight` | `Mesh_RetractBlade_Straight` | a dagger: parallel tang, long taper to a point |
| `Coll_RetractBlade_Curved`   | `Mesh_RetractBlade_Curved`   | a kukri: the edge sweeps out and back to a dropped point |
| `Coll_RetractBlade_Serrated` | `Mesh_RetractBlade_Serrated` | the dagger with a row of teeth down one edge |

Every blade is one flat plate with a raised spine on both faces, so it has a
thickness a sheath's channel can be sized from: `THICK` at the edges, `THICK +
2 * SPINE_H` at the spine.

## Axes and origin

The library builds **−Y forward, +Z up**. A blade points along −Y: its tip is at
the most negative Y. The origin is the **root of the tang**, `(0, 0, 0)` — the
plane a sheath's actuator pushes on. A device seats a blade by that point and
slides it along −Y by up to `LENGTH`, so the tip lands at a known −LENGTH.

Width is across X, thickness across Z, and the spine is symmetric in Z so the
blade reads the same from the back of the hand and from underneath.

## Sizing

`LENGTH` 0.55 m, `WIDTH` 0.048, `THICK` 0.006. Sized for the gauntlet family's
forearm (a 0.36 m deck) rather than a human one: the sheath runs the deck's
length and hangs over the back of the hand to hide it, and anything shorter on a
3 m astronaut reads as a letter opener. At full stroke the point stands three
quarters of a metre past the wrist.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)

from _buildlib import *  # noqa: E402,F403
from _tracked import TrackedPart  # noqa: E402

# Index 0 is a structural metal because `bmesh.ops.bevel` stamps every face it
# creates with material index 0 — see `_buildlib` trap notes.
CHROME, DARK, STEEL = range(3)
MATS = ["Mat_Metal_Chrome_Scuffed",    # the plate: bright, scuffed steel
        "Mat_Metal_Steel_Dark",        # the spine
        "Mat_Metal_Steel_Worn"]        # the tang and the teeth

LENGTH = 0.550
WIDTH = 0.048
THICK = 0.006
TANG = 0.050                           # parallel-sided root that stays in the sheath
TAPER_START = 0.370                    # forward of here the dagger narrows to its point
SPINE_W, SPINE_H = 0.010, 0.003        # the ridge on each face
BEVEL_W = 0.0012

TEETH = 26
TOOTH = 0.010                          # tooth pitch along the edge
TOOTH_D = 0.006                        # how far a tooth stands out of the edge


def straight_profile():
    """(x, y) outline, counter-clockwise: tang, taper, point."""
    hx = WIDTH / 2
    return [(-hx, 0.0), (hx, 0.0), (hx, -TAPER_START), (0.0, -LENGTH), (-hx, -TAPER_START)]


def curved_profile(n=10):
    """A kukri: the spine stays straight, the edge (−X side) bellies out to 1.6x
    the width then sweeps back into a point dropped 12 mm below the spine line."""
    hx = WIDTH / 2
    pts = [(-hx, 0.0), (hx, 0.0), (hx, -LENGTH + 0.030), (hx - 0.012, -LENGTH)]
    # The edge, from the point back to the tang, as a bulge.
    for i in range(1, n):
        t = i / n
        y = -LENGTH + t * (LENGTH - TANG)
        bulge = math.sin(math.pi * t) * WIDTH * 0.6
        pts.append((-hx - bulge, y))
    pts.append((-hx, -TANG))
    return pts


def plate(p, profile, mat=CHROME):
    """The blade proper: a flat prism of the outline, `THICK` across Z."""
    return p.prism(profile, THICK, axis='Z', mat=mat)


def spine(p, y0, y1):
    """The raised ridge on both faces, down the centreline."""
    hard = []
    for sz in (-1, 1):
        z0 = sz * THICK / 2 - (0.001 if sz < 0 else -0.001)     # 1 mm into the plate
        z1 = sz * (THICK / 2 + SPINE_H)
        hard += p.slab((-SPINE_W / 2, y0, z0), (SPINE_W / 2, y1, z1), DARK)
    return hard


def tang(p):
    """The root the actuator pushes on: a collar round the tang, 2 mm proud all
    round, worn steel so it reads as the part that lives in the machine."""
    hx = WIDTH / 2 + 0.002
    return p.slab((-hx, -TANG + 0.006, -THICK / 2 - SPINE_H - 0.002),
                  (hx, 0.0, THICK / 2 + SPINE_H + 0.002), STEEL)


def straight(coll, mats):
    p = TrackedPart(mats)
    hard = plate(p, straight_profile())
    hard += spine(p, -LENGTH + 0.030, -TANG)
    p.restamp("straight")
    p.bevel(hard, width=BEVEL_W, segments=1)
    tang(p)
    return p.finish("Mesh_RetractBlade_Straight", coll)


def curved(coll, mats):
    p = TrackedPart(mats)
    hard = plate(p, curved_profile())
    hard += spine(p, -LENGTH + 0.040, -TANG)
    p.restamp("curved")
    p.bevel(hard, width=BEVEL_W, segments=1)
    tang(p)
    return p.finish("Mesh_RetractBlade_Curved", coll)


def serrated(coll, mats):
    """The dagger with teeth down its −X edge: small triangular prisms standing
    out of the plate, each sunk 1.5 mm into it so the join is solid."""
    p = TrackedPart(mats)
    hard = plate(p, straight_profile())
    hard += spine(p, -LENGTH + 0.030, -TANG)
    hx = WIDTH / 2
    for i in range(TEETH):
        y1 = -TANG - 0.010 - i * TOOTH
        y0 = y1 - TOOTH
        if y0 < -TAPER_START:
            break
        p.prism([(-hx + 0.0015, y1), (-hx - TOOTH_D, (y0 + y1) / 2), (-hx + 0.0015, y0)],
                THICK - 0.001, axis='Z', mat=STEEL)
    p.restamp("serrated")
    p.bevel(hard, width=BEVEL_W, segments=1)
    tang(p)
    return p.finish("Mesh_RetractBlade_Serrated", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    straight(collection("Coll_RetractBlade_Straight"), mats)
    curved(collection("Coll_RetractBlade_Curved"), mats)
    serrated(collection("Coll_RetractBlade_Serrated"), mats)

    save(out)
    report()
    for name in ("Mesh_RetractBlade_Straight", "Mesh_RetractBlade_Curved", "Mesh_RetractBlade_Serrated"):
        o = bpy.data.objects[name]
        ys = [v.co.y for v in o.data.vertices]
        zs = [v.co.z for v in o.data.vertices]
        print("  %-28s tip y %.3f  root y %.3f  thickness %.3f" % (name, min(ys), max(ys), max(zs) - min(zs)))


if __name__ == "__main__":
    import bpy  # noqa: E402  (only the report needs it here)
    main()
