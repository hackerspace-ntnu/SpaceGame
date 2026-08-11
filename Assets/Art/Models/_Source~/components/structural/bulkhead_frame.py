"""Doorway surrounds for the RV ship's interior.

A door is not one component — it is a frame, a panel, a hinge and a handle. This
file is the frame: the structural surround that stays put while the panel swings.
Keeping it separate is what lets the cockpit bulkhead, the cargo doorway and the
side openings all read as the same ship without sharing a single mesh.

Built standing up: the opening's floor line is at z=0, the frame is centred on
x=0, and its depth runs along Y. Origin sits on the floor at the opening's
centre, which is where a doorway meets the deck and therefore where it is placed
from.

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import *  # noqa: E402,F403

from mathutils import Matrix  # noqa: E402

STEEL, DARK, PANEL, RUST, AMBER, RED = 0, 1, 2, 3, 4, 5
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Neutral_Panel_Grey", "Mat_Metal_Rust_Heavy",
        "Mat_Emissive_Amber", "Mat_Emissive_Red_Warn"]


def surround(p, half_w, height, depth, jamb, mat=STEEL):
    """Two jambs and a lintel around an opening of `half_w` x `height`."""
    faces = []
    faces += p.slab((-half_w - jamb, -depth / 2, 0.0),
                    (-half_w, depth / 2, height + jamb), mat)
    faces += p.slab((half_w, -depth / 2, 0.0),
                    (half_w + jamb, depth / 2, height + jamb), mat)
    faces += p.slab((-half_w, -depth / 2, height),
                    (half_w, depth / 2, height + jamb), mat)
    return faces


def door(coll, mats):
    """Standard crew doorway — the cockpit bulkhead. Opening 1.10 x 2.10 m."""
    hw, h, d, j = 0.55, 2.10, 0.26, 0.16
    p = Part(mats)
    surround(p, hw, h, d, j)
    # Rolled inner lip: a bare box edge in a doorway reads as cardboard.
    for s in (-1, 1):
        p.cyl((s * hw, 0, h / 2), 0.035, h, 'Z', 8, DARK)
    p.cyl((0, 0, h), 0.035, 2 * hw, 'X', 8, DARK)
    # Fastener rows down both jambs.
    for s in (-1, 1):
        p.rivets((s * (hw + j * 0.5), -d / 2 - 0.005, 0.12),
                 (s * (hw + j * 0.5), -d / 2 - 0.005, h - 0.05), 9,
                 radius=0.018, height=0.016, axis='Y', mat=DARK)
    # Status lamp over the lintel and a grab handle beside it.
    p.box((0.0, -d / 2 - 0.02, h + j * 0.55), (0.22, 0.05, 0.055), AMBER)
    p.cyl((hw + j * 0.5, -d / 2 - 0.06, 1.05), 0.022, 0.30, 'Z', 8, DARK)
    for z in (0.90, 1.20):
        p.cyl((hw + j * 0.5, -d / 2 - 0.03, z), 0.02, 0.07, 'Y', 6, DARK)
    p.bevel(width=0.009, segments=2)
    return p.finish("Mesh_BulkheadFrame_Door", coll)


def arch(coll, mats):
    """Wide arched opening between cargo bay and living space — no door leaf,
    so the cabin reads as one volume from the cockpit."""
    hw, h, d = 0.95, 1.95, 0.24
    p = Part(mats)
    # Arched profile built as a prism, so the curve is real geometry rather
    # than a chamfered box.
    steps = 14
    outer, inner = [], []
    ow, oh = hw + 0.18, h + 0.18
    for i in range(steps + 1):
        a = math.pi * i / steps
        outer.append((-ow * math.cos(a), min(oh, oh * math.sin(a) + 0.0)))
        inner.append((-hw * math.cos(a), min(h, h * math.sin(a))))
    # Close the profile down the two legs to the floor.
    profile = ([(-ow, 0.0)] + outer + [(ow, 0.0), (hw, 0.0)]
               + list(reversed(inner)) + [(-hw, 0.0)])
    p.prism(profile, d, 'Y', STEEL)
    # Wear strip where cargo scrapes past.
    for s in (-1, 1):
        p.slab((s * hw - 0.03 * s, -d / 2 - 0.02, 0.0),
               (s * hw + 0.03 * s, d / 2 + 0.02, 0.75), RUST)
    p.rivets((-ow + 0.08, -d / 2 - 0.005, 0.10),
             (-ow + 0.08, -d / 2 - 0.005, 1.30), 7, radius=0.018,
             height=0.014, axis='Y', mat=DARK)
    p.rivets((ow - 0.08, -d / 2 - 0.005, 0.10),
             (ow - 0.08, -d / 2 - 0.005, 1.30), 7, radius=0.018,
             height=0.014, axis='Y', mat=DARK)
    p.bevel(width=0.008, segments=2)
    return p.finish("Mesh_BulkheadFrame_Arch", coll)


def reinforced(coll, mats):
    """Heavy pressure-door frame for the cargo hatch — visibly load-bearing,
    with dogging lugs the hatch clamps against."""
    hw, h, d, j = 1.15, 2.25, 0.34, 0.26
    p = Part(mats)
    surround(p, hw, h, d, j)
    # Sill: this one has a raised threshold you step over.
    p.slab((-hw - j, -d / 2, -0.02), (hw + j, d / 2, 0.10), STEEL)
    # Dogging lugs around the opening — the mechanical read.
    for z in (0.45, 1.05, 1.65):
        for s in (-1, 1):
            p.box((s * (hw + 0.04), -d / 2 - 0.03, z), (0.10, 0.10, 0.16),
                  DARK)
            p.cyl((s * (hw + 0.04), -d / 2 - 0.10, z), 0.045, 0.10, 'Y', 8,
                  STEEL)
    for x in (-0.55, 0.0, 0.55):
        p.box((x, -d / 2 - 0.03, h + 0.04), (0.16, 0.10, 0.10), DARK)
    # Corner gussets.
    for s in (-1, 1):
        p.prism([(0.0, 0.0), (0.34, 0.0), (0.0, 0.34)], 0.06, 'Y', STEEL,
                offset=(s * (hw + j) - s * 0.34 if s > 0 else -hw - j,
                        -d / 2 + 0.05, h + j))
    # Warning beacon: this is the door that opens to vacuum.
    p.box((0.0, -d / 2 - 0.05, h + j * 0.5), (0.30, 0.06, 0.09), RED)
    p.slab((-hw - j, -d / 2 - 0.02, 0.10), (-hw, -d / 2 + 0.01, 0.34), RUST)
    p.bevel(width=0.009, segments=2)
    return p.finish("Mesh_BulkheadFrame_Reinforced", coll)


def hatch_rim(coll, mats):
    """Small round pressure rim for the engineering crawlway and the
    under-deck access — the one opening that is not man-height."""
    p = Part(mats)
    r, d = 0.42, 0.22
    p.tube((0, 0, r + 0.10), r + 0.10, 0.12, d, 'Y', 24, STEEL)
    for i in range(10):
        a = 2 * math.pi * i / 10
        p.cyl((math.cos(a) * (r + 0.05), -d / 2 - 0.02,
               r + 0.10 + math.sin(a) * (r + 0.05)), 0.03, 0.05, 'Y', 6, DARK)
    p.box((0.0, -d / 2 - 0.03, r + 0.10), (0.10, 0.06, 0.05), AMBER)
    p.bevel(width=0.007, segments=2)
    return p.finish("Mesh_BulkheadFrame_HatchRim", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, fn in (("Door", door), ("Arch", arch),
                     ("Reinforced", reinforced), ("HatchRim", hatch_rim)):
        fn(collection("Coll_BulkheadFrame_" + name), mats)

    report()
    save(out)


main()
