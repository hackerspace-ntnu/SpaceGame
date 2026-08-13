"""components/mechanical/leg_shroud — the slab armour that hangs on a leg.

A bare mech leg reads as a linkage. What turns it into a *machine* is the big
flat plate bolted over the outside of the thigh: it is the largest unbroken
surface on the whole silhouette, it is what catches the light at distance, and
it is where paint, stencils and damage live.

Authored in its own frame: origin on the top mounting edge of the inner face,
the plate hanging down −Z and facing +X. An assembly places it by moving the
origin onto the hip and mirroring in X for the other side of the machine.

Five variations, differing in outline and structure rather than paint:

  Plate    the baseline slab, folded rim, bolt rows
  Ribbed   vertical stiffeners and a heavier bottom shoe
  Patched  weld-on repairs over a torn lower corner — one leg on any machine
  Vented   a louvred radiator panel let into the middle
  Stub     a short knee cowl rather than a full thigh plate (built ahead)

    blender --background --python leg_shroud.py -- --out leg_shroud.blend
"""

import math
import os
import sys

from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0 the plate itself
    "Mat_Metal_Steel_Dark",      # 1 brackets, bolts, shadow gaps
    "Mat_Paint_Olive_Deep",      # 2 contrast band
    "Mat_Metal_Rust_Heavy",      # 3 corrosion, weld patches
    "Mat_Paint_Warn_Red",        # 4 hazard roundel
    "Mat_Neutral_Black_Matte",   # 5 louvre recess
    "Mat_Metal_Steel_Worn",      # 6 bare structural steel
]
HULL, DARK, OLIVE, RUST, RED, BLACK, STEEL = range(7)

THICK = 0.22          # plate thickness
DROP = 3.60           # how far the plate hangs below its mounting edge


def outline(top_fwd, top_aft, bot_fwd, bot_aft, drop=DROP):
    """Plate outline in the (y, z) plane, y forward-positive, hanging down."""
    return [(-top_aft, 0.0), (top_fwd, 0.0),
            (bot_fwd, -drop), (-bot_aft, -drop)]


def mounting(p, drop=DROP):
    """Brackets and the pivot boss on the inner face — the same on every variant.

    They sit at −X because the plate faces out; nothing here is ever seen once
    the shroud is on a leg, but the gap it holds is.
    """
    for z in (-0.35, -drop * 0.52, -drop + 0.45):
        p.box((-THICK / 2 - 0.16, 0.0, z), (0.34, 1.30, 0.30), DARK)
        p.box((-THICK / 2 - 0.30, 0.0, z), (0.10, 0.44, 0.44), STEEL)
    p.cyl((-THICK / 2 - 0.34, 0.0, -0.35), 0.16, 0.30, 'X', 10, STEEL)


def rim(p, top_fwd, top_aft, bot_fwd, bot_aft, drop=DROP):
    """Folded edge around the plate: a rolled lip plus its bolt row.

    Modelled as four boxes rather than a solidified outline — an edge fold is a
    silhouette feature and wants real thickness, and four boxes is cheaper than
    an inset of the whole face.
    """
    x = THICK / 2 + 0.05
    p.box((x, (top_fwd - top_aft) / 2, -0.06), (0.16, top_fwd + top_aft, 0.20), HULL)
    p.box((x, (bot_fwd - bot_aft) / 2, -drop + 0.07), (0.16, bot_fwd + bot_aft, 0.22), HULL)
    for sign, near, far in ((1, top_fwd, bot_fwd), (-1, top_aft, bot_aft)):
        mid = sign * (near + far) / 2
        lean = math.atan2(sign * (near - far), drop)
        p.box((x, mid, -drop / 2), (0.16, 0.20, drop),
              HULL, rot=Matrix.Rotation(lean, 4, 'X'))
    p.rivets((x + 0.03, top_fwd - 0.2, -0.06), (x + 0.03, -top_aft + 0.2, -0.06),
             9, 0.05, 0.05, 'X', DARK)
    p.rivets((x + 0.03, bot_fwd - 0.2, -drop + 0.07),
             (x + 0.03, -bot_aft + 0.2, -drop + 0.07), 8, 0.05, 0.05, 'X', DARK)


def build_plate(coll):
    """The baseline: a clean slab with a painted band and a hazard roundel."""
    tf, ta, bf, ba = 1.30, 1.15, 1.05, 0.95
    p = Part(PALETTE)
    p.prism(outline(tf, ta, bf, ba), THICK, 'X', HULL)
    rim(p, tf, ta, bf, ba)
    mounting(p)

    x = THICK / 2 + 0.02
    p.box((x, 0.05, -1.30), (0.06, 2.05, 0.34), OLIVE)          # contrast band
    p.cyl((x + 0.03, 0.35, -2.35), 0.34, 0.07, 'X', 16, RED)    # roundel
    p.cyl((x + 0.05, 0.35, -2.35), 0.20, 0.05, 'X', 16, HULL)
    p.seam((x, 0.9, -0.55), (x, 0.9, -DROP + 0.35), 0.05, 0.05, 'X', DARK)
    p.seam((x, -0.7, -0.55), (x, -0.7, -DROP + 0.35), 0.05, 0.05, 'X', DARK)
    p.bevel(width=0.02, segments=2)
    p.finish("Mesh_LegShroud_Plate", coll)


def build_ribbed(coll):
    """Stiffened: five vertical ribs and a deep bottom shoe. Reads heavier and
    two hands taller than the baseline even though the outline is close."""
    tf, ta, bf, ba = 1.36, 1.20, 1.24, 1.10
    p = Part(PALETTE)
    p.prism(outline(tf, ta, bf, ba, DROP + 0.25), THICK, 'X', HULL)
    rim(p, tf, ta, bf, ba, DROP + 0.25)
    mounting(p, DROP + 0.25)

    x = THICK / 2
    for i in range(5):
        y = -0.92 + i * 0.46
        p.box((x + 0.08, y, -(DROP + 0.25) / 2 - 0.1),
              (0.18, 0.16, DROP - 0.55), HULL)
    p.box((x + 0.10, 0.05, -DROP - 0.05), (0.26, 2.10, 0.46), OLIVE)   # shoe
    p.rivets((x + 0.20, 0.95, -DROP - 0.05), (x + 0.20, -0.85, -DROP - 0.05),
             8, 0.05, 0.05, 'X', DARK)
    p.cyl((x + 0.05, -0.55, -0.95), 0.30, 0.09, 'X', 16, RED)
    p.bevel(width=0.02, segments=2)
    p.finish("Mesh_LegShroud_Ribbed", coll)


def build_patched(coll):
    """Field repair: the lower forward corner is gone and has been plated over
    with mismatched offcuts. Every machine should have exactly one of these."""
    tf, ta, bf, ba = 1.28, 1.14, 0.55, 0.98
    p = Part(PALETTE)
    # The missing corner is in the outline itself, so it shows in silhouette.
    p.prism([(-ta, 0.0), (tf, 0.0), (tf, -2.15), (bf, -2.80),
             (bf, -DROP), (-ba, -DROP)], THICK, 'X', HULL)
    mounting(p)

    x = THICK / 2
    p.box((x + 0.04, 0.62, -2.55), (0.10, 1.05, 0.95), RUST,
          rot=Matrix.Rotation(math.radians(-8), 4, 'X'))
    p.box((x + 0.06, -0.30, -3.05), (0.09, 1.20, 0.80), STEEL)
    p.rivets((x + 0.12, 0.25, -3.05), (x + 0.12, -0.85, -3.05), 7, 0.05, 0.05,
             'X', DARK)
    p.rivets((x + 0.10, 1.05, -2.10), (x + 0.10, 1.05, -3.00), 6, 0.05, 0.05,
             'X', DARK)
    p.box((x + 0.02, 0.05, -0.80), (0.05, 1.90, 0.30), OLIVE)
    p.seam((x, -0.60, -0.40), (x, -0.60, -DROP + 0.30), 0.05, 0.05, 'X', RUST)
    p.bevel(width=0.02, segments=2)
    p.finish("Mesh_LegShroud_Patched", coll)


def build_vented(coll):
    """A radiator panel let into the plate — the leg drives are cooled through
    it, and it is the only variant that reads as having something behind it."""
    tf, ta, bf, ba = 1.30, 1.15, 1.05, 0.95
    p = Part(PALETTE)
    p.prism(outline(tf, ta, bf, ba), THICK, 'X', HULL)
    rim(p, tf, ta, bf, ba)
    mounting(p)

    x = THICK / 2
    p.box((x - 0.02, 0.05, -2.05), (0.10, 1.75, 1.55), BLACK)   # recess back
    p.louvres((x + 0.02, -0.80, -2.78), (x + 0.16, 0.90, -1.32), 7, 'Y', DARK,
              0.03)
    p.box((x + 0.10, 0.05, -1.24), (0.20, 1.95, 0.16), HULL)
    p.box((x + 0.10, 0.05, -2.86), (0.20, 1.95, 0.16), HULL)
    p.box((x + 0.04, 0.05, -0.70), (0.08, 1.90, 0.34), OLIVE)
    p.cyl((x + 0.06, -0.55, -0.70), 0.24, 0.10, 'X', 16, RED)
    p.bevel(width=0.02, segments=2)
    p.finish("Mesh_LegShroud_Vented", coll)


def build_stub(coll):
    """Short knee cowl. Built ahead: a smaller walker, or the lower segment of
    this one, wants armour that does not reach the ground."""
    p = Part(PALETTE)
    p.prism([(-0.78, 0.0), (0.86, 0.0), (0.72, -1.45), (-0.66, -1.30)],
            THICK, 'X', HULL)
    p.box((-THICK / 2 - 0.14, 0.0, -0.30), (0.30, 0.90, 0.26), DARK)
    p.box((-THICK / 2 - 0.14, 0.0, -1.05), (0.30, 0.90, 0.26), DARK)

    x = THICK / 2
    p.box((x + 0.05, 0.05, -0.24), (0.12, 1.45, 0.18), HULL)
    p.box((x + 0.05, 0.05, -1.20), (0.12, 1.25, 0.18), OLIVE)
    p.rivets((x + 0.10, 0.65, -0.24), (x + 0.10, -0.60, -0.24), 6, 0.045, 0.045,
             'X', DARK)
    p.cyl((x + 0.04, 0.10, -0.72), 0.22, 0.08, 'X', 14, RED)
    p.bevel(width=0.018, segments=2)
    p.finish("Mesh_LegShroud_Stub", coll)


def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    build_plate(collection("Coll_LegShroud_Plate"))
    build_ribbed(collection("Coll_LegShroud_Ribbed"))
    build_patched(collection("Coll_LegShroud_Patched"))
    build_vented(collection("Coll_LegShroud_Vented"))
    build_stub(collection("Coll_LegShroud_Stub"))

    report()
    save(out)


build()
