"""components/structural/truss_frame — open lattice steelwork at building scale.

`support_leg` is clad: a solid painted box that hides its structure. This is the
other half of the same language — the parts of a heavy structure that are left
open, where you see straight through the building and read chord, lacing and
gusset. A refinery needs both, and putting them in one file would mean a leg
variation and a beam variation sharing nothing but a folder.

Members are boxes, not cylinders. A lattice is mostly members, and an 8-sided
tube costs four times a box for a silhouette that at 30 m is the same two
pixels wide. The open *gaps* are what sells a truss; the section shape does not.

Origins: `Column`, `Portal` and `Tower` sit on their base centre. `Beam` and
`Brace` start at their first end on the centreline and run along +X. `Deck` has
its origin at the top surface centre, so it drops onto a platform level.

    blender --background --python truss_frame.py -- --out truss_frame.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",      # 0 chords and lacing — nearly everything
    "Mat_Metal_Steel_Dark",      # 1 gussets, node plates, bolt clusters
    "Mat_Paint_Safety_Orange",   # 2 the odd painted member and hazard marking
    "Mat_Metal_Rust_Heavy",      # 3 weathering at the base
    "Mat_Neutral_Black_Matte",   # 4 shadow inside deep nodes
]
STEEL, DARK, ORANGE, RUST, BLACK = range(5)

CHORD = 0.34                     # main member section
LACE = 0.20                      # diagonal lacing section


def along(a, b):
    """Rotation taking local +Z onto direction a->b, plus the length."""
    d = Vector(b) - Vector(a)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    return rot.to_4x4(), d.length


def member(p, a, b, size=CHORD, mat=STEEL, overlap=0.0):
    """One straight structural member between two points."""
    rot, length = along(a, b)
    p.box((Vector(a) + Vector(b)) / 2.0, (size, size, length + overlap), mat,
          rot=rot)


def lace(p, a0, a1, b0, b1, bays, size=LACE, mat=STEEL, verticals=True):
    """Zigzag web between two parallel chords, plus optional verticals.

    `a0->a1` and `b0->b1` are the two chords. The zigzag alternates direction
    every bay, which is what a Warren truss actually is and what stops the
    lattice reading as a ladder.
    """
    a0, a1, b0, b1 = map(Vector, (a0, a1, b0, b1))
    for i in range(bays):
        t0, t1 = i / bays, (i + 1) / bays
        if i % 2:
            member(p, a0.lerp(a1, t0), b0.lerp(b1, t1), size, mat, overlap=size)
        else:
            member(p, b0.lerp(b1, t0), a0.lerp(a1, t1), size, mat, overlap=size)
        if verticals and i:
            member(p, a0.lerp(a1, t0), b0.lerp(b1, t0), size * 0.9, mat)


def node(p, at, size=0.55, mat=DARK):
    """A gusset plate cluster where members meet."""
    p.box(at, (size, size, size), mat)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def column(coll, mats):
    """A 14 m four-chord lattice column, 2.60 m square. Origin at base centre.

    The generic leg for anything standing on flat ground that does not need to
    look clad. Four of these hold the outrigger deck up.
    """
    h, s = 14.0, 1.30
    p = Part(mats)
    corners = [(-s, -s), (s, -s), (s, s), (-s, s)]
    for cx, cy in corners:
        member(p, (cx, cy, 0), (cx, cy, h), CHORD)
    for i in range(4):
        ax, ay = corners[i]
        bx, by = corners[(i + 1) % 4]
        lace(p, (ax, ay, 0), (ax, ay, h), (bx, by, 0), (bx, by, h), 7)
    for k in range(4):                      # horizontal diaphragms
        z = h * (k + 1) / 5
        for i in range(4):
            ax, ay = corners[i]
            bx, by = corners[(i + 1) % 4]
            member(p, (ax, ay, z), (bx, by, z), LACE * 1.1)
        member(p, corners[0] + (z,), corners[2] + (z,), LACE * 0.9)
    p.box((0, 0, h - 0.28), (s * 2 + 0.9, s * 2 + 0.9, 0.56), DARK)  # head
    p.box((0, 0, 0.35), (s * 2 + 1.4, s * 2 + 1.4, 0.70), DARK)      # base
    p.box((0, 0, 0.05), (s * 2 + 2.0, s * 2 + 2.0, 0.30), RUST)
    for cx, cy in corners:
        node(p, (cx, cy, 0.9))
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Truss_Column", coll)


def beam(coll, mats):
    """A 12 m horizontal lattice beam, 1.60 m deep. Origin at the +X start end.

    Spans between columns to carry a deck. Deeper than it needs to be for its
    span, because a shallow truss at this scale looks like scaffolding.
    """
    length, dep, wid = 12.0, 1.60, 1.20
    p = Part(mats)
    for sy in (-1, 1):
        y = sy * wid / 2
        member(p, (0, y, 0), (length, y, 0), CHORD)              # top chord
        member(p, (0, y, -dep), (length, y, -dep), CHORD)        # bottom chord
        lace(p, (0, y, 0), (length, y, 0), (0, y, -dep), (length, y, -dep), 8)
    for i in range(5):                                           # cross ties
        x = length * i / 4
        member(p, (x, -wid / 2, 0), (x, wid / 2, 0), LACE)
        member(p, (x, -wid / 2, -dep), (x, wid / 2, -dep), LACE)
    for x in (0.3, length - 0.3):                                # end bearings
        p.box((x, 0, -dep / 2), (0.6, wid + 0.5, dep + 0.4), DARK)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Truss_Beam", coll)


def portal(coll, mats):
    """A 16 m wide, 11 m high portal frame — two legs and a head beam.

    Vehicles drive through these on an industrial site, so it is sized for that
    rather than for structure: 16 m clear is a hauler with room to steer.
    """
    span, h, wid = 16.0, 11.0, 1.60
    p = Part(mats)
    for sx in (-1, 1):
        x = sx * span / 2
        for sy in (-1, 1):
            y = sy * wid / 2
            member(p, (x - sx * 0.7, y, 0), (x, y, h), CHORD * 1.15)
        lace(p, (x - 0.7 * sx, -wid / 2, 0), (x, -wid / 2, h),
             (x - 0.7 * sx, wid / 2, 0), (x, wid / 2, h), 6)
        p.box((x - sx * 0.35, 0, 0.45), (2.6, wid + 0.8, 0.9), DARK)
        p.box((x - sx * 0.35, 0, 0.08), (3.2, wid + 1.4, 0.30), RUST)
    for sy in (-1, 1):
        y = sy * wid / 2
        member(p, (-span / 2, y, h), (span / 2, y, h), CHORD)
        member(p, (-span / 2, y, h + 1.7), (span / 2, y, h + 1.7), CHORD)
        lace(p, (-span / 2, y, h), (span / 2, y, h),
             (-span / 2, y, h + 1.7), (span / 2, y, h + 1.7), 8)
    for i in range(5):
        x = -span / 2 + span * i / 4
        member(p, (x, -wid / 2, h + 1.7), (x, wid / 2, h + 1.7), LACE)
    # Haunches — the corners of a portal are where the moment is.
    for sx in (-1, 1):
        member(p, (sx * span / 2, 0, h - 2.4), (sx * (span / 2 - 2.4), 0, h),
               CHORD, ORANGE)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Truss_Portal", coll)


def brace(coll, mats):
    """A 7 x 7 m cross-braced panel. Origin at the bottom-left corner.

    Fills a bay in a larger frame. Separate from `Column` because bracing a
    rectangular opening is a different job from standing something up, and this
    gets used flat against walls as often as inside frames.
    """
    s = 7.0
    p = Part(mats)
    for a, b in (((0, 0, 0), (s, 0, s)), ((s, 0, 0), (0, 0, s))):
        member(p, a, b, CHORD * 0.85, overlap=0.2)
    for a, b in (((0, 0, 0), (s, 0, 0)), ((0, 0, s), (s, 0, s)),
                 ((0, 0, 0), (0, 0, s)), ((s, 0, 0), (s, 0, s))):
        member(p, a, b, CHORD)
    node(p, (s / 2, 0, s / 2), 0.85)
    for c in ((0, 0, 0), (s, 0, 0), (0, 0, s), (s, 0, s)):
        node(p, c, 0.62)
    p.box((s / 2, 0, s * 0.5), (1.4, 0.16, 0.9), ORANGE)   # a painted plate
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Truss_Brace", coll)


def deck(coll, mats):
    """A 20 x 16 m platform frame with its plated top. Origin at the top face.

    This is the outrigger deck itself: primary beams one way, secondaries the
    other, a plate over the top and the whole grillage visible from below,
    which is most of what you see of a raised platform.
    """
    w, d, dep = 20.0, 16.0, 1.70
    p = Part(mats)
    p.slab((-w / 2, -d / 2, -0.22), (w / 2, d / 2, 0.0), STEEL)
    for sy in (-1, 1):                                   # edge kerb
        p.box((0, sy * (d / 2 - 0.15), 0.22), (w, 0.30, 0.66), ORANGE)
    for sx in (-1, 1):
        p.box((sx * (w / 2 - 0.15), 0, 0.22), (0.30, d, 0.66), ORANGE)
    for i in range(5):                                   # primaries, along X
        y = -d / 2 + d * i / 4
        member(p, (-w / 2, y, -0.35), (w / 2, y, -0.35), CHORD * 1.3)
        member(p, (-w / 2, y, -dep), (w / 2, y, -dep), CHORD * 1.3)
        lace(p, (-w / 2, y, -0.35), (w / 2, y, -0.35),
             (-w / 2, y, -dep), (w / 2, y, -dep), 10, verticals=False)
    for i in range(9):                                   # secondaries, along Y
        x = -w / 2 + w * i / 8
        member(p, (x, -d / 2, -0.42), (x, d / 2, -0.42), LACE * 1.2)
    for i in range(5):
        x = -w / 2 + w * i / 4
        member(p, (x, -d / 2, -dep), (x, d / 2, -dep), LACE * 1.2)
    for sx in (-1, 1):                                   # bearing points
        for sy in (-1, 1):
            p.box((sx * (w / 2 - 2.6), sy * (d / 2 - 2.4), -dep - 0.25),
                  (2.6, 2.6, 0.7), DARK)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Truss_Deck", coll)


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Column", column), ("Beam", beam), ("Portal", portal),
                     ("Brace", brace), ("Deck", deck)):
        fn(collection("Coll_Truss_%s" % name), mats)
    report()
    save(out)


build()
