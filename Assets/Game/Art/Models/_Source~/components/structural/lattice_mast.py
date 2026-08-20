"""components/structural/lattice_mast — an X-braced square mast that carries buildings.

`truss_frame` already holds this library's open steelwork, and `Coll_Truss_Column`
is a 14 m four-chord lattice that looks, in a thumbnail, like this one. It is not
this one. That column is **Warren-laced**: a single zigzag web that alternates
direction every bay, which is what you build when a member only ever has to carry
load one way and you are paying for a leg under a deck. This is an **X-braced
tower mast**: every bay is crossed both ways, because a free-standing mast with
habitable modules hung off it is loaded in torsion and in wind from any bearing,
and the second diagonal is the member that resists it.

That difference is not decoration. It changes the count of members per bay from
one to two, it puts a node in the middle of every face where the diagonals cross,
and it is the single most visible thing about a mast seen against a bright sky —
which is exactly how this component is always seen. Parameterising `truss_frame`
to emit both webs would have meant a flag threaded through `lace()` that changes
the member count, the node positions and the bay proportion, i.e. a second
component wearing the first one's name.

The other four variations are the parts that make a mast a *building*: a splayed
base that gets the load out to four footings, a collar that clamps a floor to the
shaft, a taper for the light upper run, and a head that presents a flat pad.

Plan is 3.40 m across chord centres throughout, so `Splay` -> `Bay` -> `Bay` ->
`Taper` -> `Cap` stack by adding H to Z and nothing else. Origins sit on the base
centre of each piece, on its own splice plane, except `Collar`, whose origin is
the **top surface of its deck** so a floor level drops onto it directly.

    blender --background --python lattice_mast.py -- --out lattice_mast.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

from mathutils import Vector  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",      # 0 STEEL  chords, diagonals, the whole lattice
    "Mat_Metal_Steel_Dark",      # 1 DARK   gussets, splice flanges, node plates
    "Mat_Neutral_Slate_Dark",    # 2 SLATE  deep members read as shadow at distance
    "Mat_Metal_Rust_Heavy",      # 3 RUST   weathering where the mast meets ground
    "Mat_Paint_Coral_Faded",     # 4 CORAL  the painted band tying mast to blocks
    "Mat_Neutral_Black_Matte",   # 5 BLACK  inside deep nodes and footing recesses
]
STEEL, DARK, SLATE, RUST, CORAL, BLACK = range(6)

S = 1.70                         # half the chord spacing — 3.40 m square
CHORD = 0.26                     # main leg section
DIAG = 0.15                      # X-brace diagonal section
HOOP = 0.17                      # horizontal ring section
BAY = 4.60                       # one X-braced bay


def along(a, b):
    """Rotation taking local +Z onto direction a->b, plus the length.

    Kept local rather than pushed into `_buildlib` so this script stays
    independently runnable as the historical record it is meant to be.
    """
    d = Vector(b) - Vector(a)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    return rot.to_4x4(), d.length


def member(p, a, b, size=CHORD, mat=STEEL, overlap=0.0):
    """One straight structural member between two points."""
    rot, length = along(a, b)
    p.box((Vector(a) + Vector(b)) / 2.0, (size, size, length + overlap), mat,
          rot=rot)


def corners(s=S):
    """The four chord positions in plan, counter-clockwise from -X-Y."""
    return [(-s, -s), (s, -s), (s, s), (-s, s)]


def cross_bay(p, z0, z1, s_lo=S, s_hi=None, size=DIAG, mat=STEEL):
    """X-brace all four faces of one bay, with a node where the diagonals meet.

    The crossing node is a real gusset rather than two members passing through
    each other. At the distance this is seen from it is two extra pixels, but it
    is the difference between a mast that was detailed and one that was arrayed.
    """
    s_hi = s_lo if s_hi is None else s_hi
    lo, hi = corners(s_lo), corners(s_hi)
    for i in range(4):
        ax, ay = lo[i]
        bx, by = lo[(i + 1) % 4]
        cx, cy = hi[i]
        dx, dy = hi[(i + 1) % 4]
        member(p, (ax, ay, z0), (dx, dy, z1), size, mat, overlap=size)
        member(p, (bx, by, z0), (cx, cy, z1), size, mat, overlap=size)
        p.box(((ax + bx + cx + dx) / 4.0, (ay + by + cy + dy) / 4.0,
               (z0 + z1) / 2.0), (size * 2.2, size * 2.2, size * 2.2), DARK)


def hoop(p, z, s=S, size=HOOP, mat=STEEL, diagonal=True):
    """A horizontal ring at one level, optionally with a plan diagonal."""
    c = corners(s)
    for i in range(4):
        member(p, c[i] + (z,), c[(i + 1) % 4] + (z,), size, mat)
    if diagonal:
        member(p, c[0] + (z,), c[2] + (z,), size * 0.85, SLATE)


def flange(p, z, s=S, mat=DARK, thick=0.30, over=0.85):
    """A splice joint — where one stacked section bolts onto the next.

    Four corner gusset plates and a perimeter angle, *not* a full-width plate.
    A solid diaphragm here would be cheaper and is what a first pass reaches
    for, but it closes the shaft off every 13.8 m, and seeing daylight through
    the lattice all the way up is the entire reason to build an open mast
    instead of a clad one.
    """
    c = corners(s)
    for cx, cy in c:
        p.box((cx * 1.06, cy * 1.06, z), (0.92, 0.92, thick), mat)
    for i in range(4):
        member(p, c[i] + (z,), c[(i + 1) % 4] + (z,), thick * 0.8, mat)


def node_cluster(p, at, size=0.42, mat=DARK):
    p.box(at, (size, size, size), mat)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def mast_bay(coll, mats):
    """The stackable shaft module: three X-braced bays, 13.80 m. Origin at base.

    Three bays rather than one, because a mast is bought by the storey and a
    one-bay module would mean nine objects where three will do. The splice
    flanges land only at the ends, so a stack reads as continuous lattice with a
    collar every 13.8 m rather than as a pile of short pieces.
    """
    p = Part(mats)
    h = BAY * 3
    for cx, cy in corners():
        member(p, (cx, cy, 0), (cx, cy, h), CHORD)
    for k in range(3):
        cross_bay(p, BAY * k, BAY * (k + 1))
    for k in range(4):
        hoop(p, BAY * k, diagonal=(k % 2 == 0))
    flange(p, 0.17)
    flange(p, h - 0.17)
    for cx, cy in corners():                 # bolt clusters at the splices
        node_cluster(p, (cx, cy, 0.62))
        node_cluster(p, (cx, cy, h - 0.62))
    p.box((0, 0, h * 0.5), (S * 2 + 0.5, 0.10, 0.55), CORAL)   # painted ident band
    # Oxide on the members, so the shaft weathers with the hulls it carries
    # rather than staying factory-grey underneath them.
    rng = random.Random(7)
    for _ in range(16):
        cx, cy = rng.choice(corners())
        z = rng.uniform(0.6, h - 0.6)
        if rng.random() < 0.5:
            p.box((cx * 1.02, cy * 1.02, z), (CHORD * 1.35, CHORD * 1.35,
                  rng.uniform(0.5, 1.4)), RUST)
        else:
            p.box((cx * rng.uniform(-0.4, 0.4), cy * 1.03, z),
                  (rng.uniform(0.3, 0.8), 0.10, 0.20), RUST)
    p.bevel(width=0.025, segments=1)
    return p.finish("Mesh_LatticeMast_Bay", coll)


def mast_splay(coll, mats):
    """The base: four legs raking out from the shaft to footings 9.6 m apart.

    A mast this slender cannot land on a 3.4 m square without a foundation the
    size of the building it holds up, so the load goes out sideways first. The
    rake is authored, not a rotated copy of `Bay` — the diagonals between raked
    legs are not the diagonals between parallel ones, and the footings have to
    sit flat on ground while the legs arrive at an angle.
    """
    p = Part(mats)
    h = 6.20
    spread = 4.80                            # half-spacing at the ground
    top, bot = corners(S), corners(spread)
    for i in range(4):
        tx, ty = top[i]
        bx, by = bot[i]
        member(p, (bx, by, 0.30), (tx, ty, h), CHORD * 1.25)
        # knee brace back to the shaft centreline, the member that stops the
        # legs spreading further under load
        member(p, (bx * 0.62, by * 0.62, 0.30), (tx, ty, h * 0.62), DIAG, SLATE)
    for k in range(2):                       # two raked X-braced bays
        t0, t1 = k / 2.0, (k + 1) / 2.0
        cross_bay(p, 0.30 + (h - 0.30) * t0, 0.30 + (h - 0.30) * t1,
                  s_lo=spread + (S - spread) * t0,
                  s_hi=spread + (S - spread) * t1, size=DIAG * 1.1)
    hoop(p, 0.30 + (h - 0.30) * 0.5, s=(S + spread) / 2.0, diagonal=False)
    hoop(p, h, s=S)
    flange(p, h - 0.17)
    for bx, by in bot:                       # footing pads on the ground plane
        p.box((bx, by, 0.55), (1.70, 1.70, 1.10), STEEL)
        p.box((bx, by, 0.14), (2.60, 2.60, 0.28), RUST)
        p.box((bx, by, 1.18), (1.05, 1.05, 0.22), DARK)
        for sx in (-1, 1):                   # holding-down bolt pairs
            p.cyl((bx + sx * 0.92, by, 0.34), 0.085, 0.30, seg=6, mat=DARK)
            p.cyl((bx, by + sx * 0.92, 0.34), 0.085, 0.30, seg=6, mat=DARK)
    p.bevel(width=0.025, segments=1)
    return p.finish("Mesh_LatticeMast_Splay", coll)


def mast_collar(coll, mats):
    """A floor bracket: knee braces off the shaft carrying a 12.6 m ring beam.

    This is the piece that makes the mast a building rather than a pylon. Origin
    is the **top of the deck steel**, so a floor level is placed at the z it
    actually occupies instead of at the bracket's underside.

    12.6 m is sized off what stands on it, not off the shaft: a 9 m cab on a
    9.8 m deck leaves a 0.4 m ledge, which is not a walkway and reads as a
    building that overhangs its own floor. 1.8 m clear each side is a gallery
    somebody can actually get round the cab on.
    """
    p = Part(mats)
    r = 6.30                                 # half the ring beam span
    deck_z = 0.0                             # origin plane — top of the beam
    beam_z = deck_z - 0.30
    ring = corners(r)
    for i in range(4):                       # the ring beam itself
        member(p, ring[i] + (beam_z,), ring[(i + 1) % 4] + (beam_z,), 0.42, STEEL)
    for i in range(4):                       # corner cross-beams to the shaft
        rx, ry = ring[i]
        sx, sy = corners()[i]
        member(p, (rx, ry, beam_z), (sx, sy, beam_z), 0.34, STEEL)
        member(p, (rx, ry, beam_z), (sx * 1.02, sy * 1.02, beam_z - 4.30),
               0.30, STEEL)                  # the raking knee brace
        node_cluster(p, (sx, sy, beam_z - 4.30), 0.60)
    for i in range(4):                       # mid-span outriggers
        ax, ay = ring[i]
        bx, by = ring[(i + 1) % 4]
        mx, my = (ax + bx) / 2.0, (ay + by) / 2.0
        member(p, (mx, my, beam_z), (mx * 0.34, my * 0.34, beam_z), 0.26, STEEL)
        member(p, (mx, my, beam_z), (mx * 0.28, my * 0.28, beam_z - 3.30),
               0.24, SLATE)
    p.box((0, 0, beam_z - 0.02), (S * 2 + 1.5, S * 2 + 1.5, 0.52), DARK)  # shaft cuff
    p.box((0, 0, deck_z - 0.09), (r * 2, r * 2, 0.18), SLATE)             # deck pan
    for i in range(4):                       # coral kerb, tying deck to the blocks
        ax, ay = ring[i]
        bx, by = ring[(i + 1) % 4]
        member(p, (ax, ay, deck_z + 0.10), (bx, by, deck_z + 0.10), 0.20, CORAL)
    p.bevel(width=0.025, segments=1)
    return p.finish("Mesh_LatticeMast_Collar", coll)


def mast_taper(coll, mats):
    """A 9.20 m section narrowing 3.40 -> 2.30 m, for the light run above the deck.

    Above the top floor the mast carries only the cab and the aerials, and a
    shaft that keeps its full width all the way up reads as a chimney. The taper
    is authored at its true angle: the chords lean, so the diagonals between them
    and the hoops that brace them are all different from the parallel case.
    """
    p = Part(mats)
    h = BAY * 2
    s_top = 1.15
    lo, hi = corners(S), corners(s_top)
    for i in range(4):
        member(p, lo[i] + (0.0,), hi[i] + (h,), CHORD * 0.92)
    for k in range(2):
        t0, t1 = k / 2.0, (k + 1) / 2.0
        cross_bay(p, h * t0, h * t1,
                  s_lo=S + (s_top - S) * t0, s_hi=S + (s_top - S) * t1,
                  size=DIAG * 0.9)
    for k in range(3):
        t = k / 2.0
        hoop(p, h * t, s=S + (s_top - S) * t, size=HOOP * 0.9,
             diagonal=(k != 1))
    flange(p, 0.17)
    p.box((0, 0, h - 0.14), (s_top * 2 + 0.55, s_top * 2 + 0.55, 0.28), DARK)
    p.bevel(width=0.022, segments=1)
    return p.finish("Mesh_LatticeMast_Taper", coll)


def mast_cap(coll, mats):
    """The head: a railed maintenance pad and the flat plate the cab sits on.

    2.30 m square to meet `Taper`. Everything above this is aerials, so the cap
    carries the cable gland cluster that a mast full of feeders needs somewhere
    to terminate.
    """
    p = Part(mats)
    s = 1.15
    p.box((0, 0, 0.16), (s * 2 + 0.7, s * 2 + 0.7, 0.32), DARK)      # pad plate
    p.box((0, 0, 0.40), (s * 2 + 0.2, s * 2 + 0.2, 0.20), STEEL)
    c = corners(s + 0.28)
    for cx, cy in c:                                                  # rail posts
        p.box((cx, cy, 0.95), (0.09, 0.09, 1.10), STEEL)
    for i in range(4):                                                # top rail
        member(p, c[i] + (1.46,), c[(i + 1) % 4] + (1.46,), 0.07, STEEL)
        member(p, c[i] + (0.98,), c[(i + 1) % 4] + (0.98,), 0.055, STEEL)
    p.box((0, 0, 1.72), (s * 2 - 0.3, s * 2 - 0.3, 0.36), SLATE)      # cab bearing
    p.box((0, 0, 1.94), (s * 2 - 0.9, s * 2 - 0.9, 0.14), CORAL)
    for k in range(5):                                                # cable glands
        a = 2 * math.pi * k / 5
        p.cyl((0.62 * math.cos(a), 0.62 * math.sin(a), 0.62), 0.11, 0.44,
              seg=8, mat=DARK)
    p.box((s * 0.5, -s - 0.30, 0.78), (0.52, 0.30, 0.66), SLATE)      # feeder box
    p.bevel(width=0.02, segments=1)
    return p.finish("Mesh_LatticeMast_Cap", coll)


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Bay", mast_bay), ("Splay", mast_splay),
                     ("Collar", mast_collar), ("Taper", mast_taper),
                     ("Cap", mast_cap)):
        fn(collection("Coll_LatticeMast_%s" % name), mats)
    report()
    save(out)


main()
