"""components/mechanical/gantry_boom — the long cantilevered boom of a bulk handler.

`truss_frame` is the other lattice component in this library and it is not this
one. That one is *static*: columns that stand on the ground and beams that span
between two supports, sized by the load above them. This is a **cantilever** —
held at one end only — and everything about it follows from that. It tapers,
because the bending moment falls to nothing at the tip. It carries a stay mast
and tie bars, because 26 m of unsupported steel does not hold itself up. It has
a heel housing with a slew bearing, because the whole assembly turns. A truss
beam has none of those and would look absurd with them bolted on.

The five variations are the five parts of one machine rather than five
alternatives, and they are in one file for the same reason a door's panel and
hinge are: they are meaningless apart and always placed together.

**Every variation shares one datum — the pivot point** — which is the origin of
`Heel`, `Stay` and `Counter`, and the root end of `Span`. Assembly is therefore:
put Heel, Stay and Counter at the pivot, put Span at the pivot, and put Head at
pivot + (SPAN_LEN, 0, 0). Nothing has to be measured twice, and raking the whole
boom down means rotating five objects about one shared point.

Members are boxes, not cylinders, for the reason `truss_frame` gives: a lattice
is mostly members, and at 30 m the section shape is not what the eye reads. The
tie bars and guy wires *are* cylinders, because a rod under tension reading as
round is the one place the section does matter.

    blender --background --python gantry_boom.py -- --out gantry_boom.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",        # 0 STEEL  chords, lacing, the bulk of it
    "Mat_Metal_Steel_Dark",        # 1 DARK   gussets, node plates, machinery
    "Mat_Metal_HullRust_Orange",   # 2 HULL   plated housings and hoods
    "Mat_Metal_Rust_Heavy",        # 3 RUST   corrosion streaks, repair patches
    "Mat_Neutral_Black_Matte",     # 4 BLACK  shadow inside nodes and chutes
    "Mat_Paint_Safety_Orange",     # 5 ORANGE hazard paint that survived
    "Mat_Emissive_Amber",          # 6 AMBER  the one lamp still burning
]
STEEL, DARK, HULL, RUST, BLACK, ORANGE, AMBER = range(7)

SPAN_LEN = 26.0                  # root-to-tip of one Span, the assembly datum
ROOT_HALF = 1.60                 # half-width of the lattice at the root
TIP_HALF = 0.90                  # ... and at the tip
CHORD = 0.30
LACE = 0.17


# ---------------------------------------------------------------------------
# Local geometry helpers
#
# Kept local rather than pushed into _buildlib, matching the rest of the
# library: every generation script stays independently runnable as the
# historical record it is meant to be.
# ---------------------------------------------------------------------------

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


def rod(p, a, b, radius=0.075, mat=STEEL, seg=6):
    """A round tension member — tie bar, guy wire, hoist rope."""
    rot, length = along(a, b)
    p.cyl((Vector(a) + Vector(b)) / 2.0, radius, length, 'Z', seg=seg, mat=mat,
          rot=rot)


def node(p, at, size=0.46, mat=DARK):
    """A gusset plate cluster where members meet."""
    p.box(at, (size, size, size), mat)


def corners(t):
    """The four chord positions at fraction `t` along the boom.

    The taper is linear in half-width, which is what a real boom does — the
    bending moment it resists falls off linearly toward a free tip.
    """
    h = ROOT_HALF + (TIP_HALF - ROOT_HALF) * t
    x = SPAN_LEN * t
    return [Vector((x, -h, -h)), Vector((x, h, -h)),
            Vector((x, h, h)), Vector((x, -h, h))]


def rust_patches(p, lo, hi, count, seed):
    """Weld-on repair plates scattered over a region.

    Deterministic per seed, so a rebuild produces the same machine.
    """
    import random
    rng = random.Random(seed)
    lo, hi = Vector(lo), Vector(hi)
    for _ in range(count):
        c = Vector((rng.uniform(lo.x, hi.x), rng.uniform(lo.y, hi.y),
                    rng.uniform(lo.z, hi.z)))
        p.box(c, (rng.uniform(0.5, 1.4), rng.uniform(0.4, 1.0), 0.07), RUST)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def build_span(coll, mats):
    """26 m of tapering four-chord lattice. Origin at the root end centreline.

    The belt trough along the top is what makes this a *conveyor* boom rather
    than a generic truss, and it is the only part of the span the player will
    ever be close enough to read individually. The walkway down the +Y side is
    the other: a boom with no way to walk out to the head is a boom nobody ever
    maintained, which is the wrong story for a settlement that still uses it.
    """
    p = Part(mats)
    bays = 11

    # Four chords, root to tip.
    for i in range(4):
        member(p, corners(0.0)[i], corners(1.0)[i], CHORD, STEEL)

    # Warren lacing on all four faces. Alternating diagonals per bay is what
    # makes it a truss and not a ladder.
    for i in range(4):
        j = (i + 1) % 4
        for b in range(bays):
            t0, t1 = b / bays, (b + 1) / bays
            a0, b0 = corners(t0)[i], corners(t0)[j]
            a1, b1 = corners(t1)[i], corners(t1)[j]
            if b % 2:
                member(p, a0, b1, LACE, STEEL, overlap=LACE)
            else:
                member(p, b0, a1, LACE, STEEL, overlap=LACE)
            if b:
                member(p, a0, b0, LACE * 0.9, STEEL)

    # Transverse diaphragms every other bay, and gussets at the nodes.
    for b in range(0, bays + 1, 2):
        t = b / bays
        c = corners(t)
        member(p, c[0], c[2], LACE * 0.8, STEEL)
        for v in c:
            node(p, v, 0.40)

    # The belt trough: two side plates and the belt surface between them.
    top = ROOT_HALF
    for s in (-1, 1):
        p.box((SPAN_LEN / 2.0, s * (top - 0.18), top + 0.42),
              (SPAN_LEN, 0.10, 0.72), HULL)
    p.box((SPAN_LEN / 2.0, 0, top + 0.16), (SPAN_LEN, top * 2 - 0.5, 0.09),
          BLACK)
    for i in range(14):                       # idler rollers under the belt
        p.cyl((0.9 + i * (SPAN_LEN - 1.8) / 13.0, 0, top + 0.08), 0.13,
              top * 1.7, 'Y', seg=8, mat=DARK)

    # Maintenance walkway down the +Y flank, with toe plate and stanchions.
    wy = ROOT_HALF + 0.62
    p.box((SPAN_LEN / 2.0, wy, -0.30), (SPAN_LEN, 1.10, 0.07), STEEL)
    p.box((SPAN_LEN / 2.0, wy + 0.52, -0.16), (SPAN_LEN, 0.06, 0.24), DARK)
    for i in range(9):
        x = 0.8 + i * (SPAN_LEN - 1.6) / 8.0
        member(p, (x, wy + 0.5, -0.30), (x, wy + 0.5, 0.80), 0.07, STEEL)
        member(p, (x, ROOT_HALF, -0.34), (x, wy + 0.5, -0.30), 0.09, STEEL)
    for h in (0.44, 0.80):                    # two rails, not one
        p.box((SPAN_LEN / 2.0, wy + 0.5, h), (SPAN_LEN, 0.06, 0.06), STEEL)

    rust_patches(p, (2, -ROOT_HALF, -ROOT_HALF), (SPAN_LEN - 2, ROOT_HALF,
                 ROOT_HALF), 9, seed=11)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_GantryBoom_Span", coll)


def build_head(coll, mats):
    """The outboard end: discharge drum, chute and guard hood. 8 m long.

    Origin at its inboard end on the centreline, so it lands at
    `pivot + (SPAN_LEN, 0, 0)`.

    This is where a boom stops being a truss and becomes a machine, and it is
    the silhouette event that tells you which way the thing points. The chute
    hangs *below* the centreline on purpose — the one part that says material
    came out here and fell.
    """
    p = Part(mats)
    L = 8.0
    h = TIP_HALF

    for i in range(4):                        # stub chords continuing the span
        member(p, corners(1.0)[i] - Vector((SPAN_LEN, 0, 0)),
               corners(1.0)[i] - Vector((SPAN_LEN - 2.6, 0, 0)), CHORD, STEEL)

    # The head housing — plated, not latticed.
    p.box((4.2, 0, 0.30), (4.6, h * 2 + 0.7, h * 2 + 1.1), HULL)
    p.box((4.2, 0, h + 0.95), (4.9, h * 2 + 1.0, 0.34), DARK)     # cap
    for s in (-1, 1):                          # side access doors
        p.box((4.2, s * (h + 0.36), 0.10), (2.2, 0.09, 1.5), DARK)
        p.box((4.2, s * (h + 0.42), 0.10), (1.9, 0.05, 1.2), BLACK)

    # Discharge drum on its shaft, and the guard hood over it.
    p.cyl((6.1, 0, 0.42), 0.86, h * 2 + 0.2, 'Y', seg=14, mat=DARK)
    p.cyl((6.1, 0, 0.42), 0.16, h * 2 + 1.5, 'Y', seg=8, mat=STEEL)
    for s in (-1, 1):
        p.box((6.1, s * (h + 0.55), 0.42), (1.5, 0.22, 1.5), STEEL)
    p.box((6.4, 0, 1.42), (2.6, h * 2 + 0.5, 0.10), HULL,
          rot=Matrix.Rotation(math.radians(-14), 4, 'Y'))

    # The chute: a tapered hopper hanging under the drum. Four walls rather
    # than a loft, so the seams stay crisp against a low sun.
    for s, ax in ((-1, 'Y'), (1, 'Y')):
        p.box((6.2, s * 0.78, -1.35), (2.3, 0.09, 2.4), HULL,
              rot=Matrix.Rotation(math.radians(s * 11), 4, 'X'))
    for s in (-1, 1):
        p.box((6.2 + s * 1.05, 0, -1.35), (0.09, 1.5, 2.4), HULL,
              rot=Matrix.Rotation(math.radians(-s * 9), 4, 'Y'))
    p.box((6.2, 0, -2.62), (1.5, 1.1, 0.30), DARK)
    p.box((6.2, 0, -2.80), (1.2, 0.85, 0.14), BLACK)

    # The last working lamp on the machine, and its bracket.
    member(p, (2.4, h + 0.4, 0.9), (2.4, h + 1.15, 1.35), 0.10, STEEL)
    p.box((2.4, h + 1.25, 1.42), (0.44, 0.30, 0.40), DARK)
    p.box((2.4, h + 1.41, 1.42), (0.30, 0.05, 0.28), AMBER)

    p.box((1.1, 0, h + 0.55), (0.9, h * 1.6, 0.12), ORANGE)   # hazard marking
    rust_patches(p, (2.4, -h, -1.0), (6.6, h, 1.2), 7, seed=23)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_GantryBoom_Head", coll)


def build_heel(coll, mats):
    """The pivot housing and slew bearing. **Origin at the pivot point.**

    The house sits below the pivot because that is where the machinery has to
    be: the boom has to swing over it. Getting that the wrong way round is the
    single most common way a modelled bulk handler stops reading as a machine
    that works.
    """
    p = Part(mats)

    # Trunnion brackets either side of the pivot, and the pin through them.
    for s in (-1, 1):
        p.box((0, s * 2.15, -0.20), (2.9, 0.42, 3.2), STEEL)
        p.cyl((0, s * 2.15, 0), 0.78, 0.52, 'Y', seg=14, mat=DARK)
        p.cyl((0, s * 2.15, 0), 0.30, 0.66, 'Y', seg=10, mat=STEEL)
    p.cyl((0, 0, 0), 0.26, 4.3, 'Y', seg=10, mat=DARK)

    # The machine house under the pivot.
    p.box((0.3, 0, -3.85), (7.6, 6.4, 4.5), HULL)
    p.box((0.3, 0, -1.52), (8.1, 6.9, 0.42), DARK)          # roof cap
    p.box((0.3, 0, -6.14), (8.4, 7.2, 0.50), DARK)          # base flange
    for s in (-1, 1):                                        # louvred flanks
        p.louvres((-2.4, s * 3.19, -5.0), (2.4, s * 3.24, -2.6), 7, mat=DARK)
    p.box((3.9, 0, -3.6), (0.30, 2.6, 2.9), DARK)            # end door
    p.box((4.02, 0, -3.6), (0.10, 2.1, 2.4), BLACK)

    # Slew ring the whole house stands on — the reason it can turn.
    p.cyl((0.3, 0, -6.55), 3.5, 0.60, 'Z', seg=24, mat=STEEL)
    p.cyl((0.3, 0, -6.90), 4.1, 0.34, 'Z', seg=24, mat=DARK)
    for i in range(16):
        a = 2 * math.pi * i / 16
        p.cyl((0.3 + 3.75 * math.cos(a), 3.75 * math.sin(a), -6.72), 0.13,
              0.30, 'Z', seg=6, mat=DARK)

    # Ladder up the side, because the house has a door 4 m off the deck.
    for s in (-1, 1):
        member(p, (2.2, s * 0.62, -6.2), (2.2, s * 0.62, -1.7), 0.09, STEEL)
    for i in range(7):
        member(p, (2.2, -0.62, -5.9 + i * 0.62), (2.2, 0.62, -5.9 + i * 0.62),
               0.06, STEEL)

    rust_patches(p, (-2.5, -3.2, -5.8), (3.5, 3.2, -1.8), 8, seed=37)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_GantryBoom_Heel", coll)


def build_stay(coll, mats):
    """A-frame stay mast with tie bars out to the boom. Origin at the pivot.

    The triangle above the boom line is the whole point: it is the silhouette
    that says *cantilever* from a kilometre away, and it is the only part of
    the machine that explains why 26 m of steel is not on the ground. Two legs
    splayed in Y rather than one post, because a single mast can only resist
    bending in one plane and looks it.
    """
    p = Part(mats)
    apex = Vector((0.4, 0, 9.2))

    for s in (-1, 1):
        base = Vector((-0.6, s * 2.5, 0.6))
        member(p, base, apex, 0.34, STEEL)
        # Lacing up each leg, so the mast is a truss rather than a stick.
        for i in range(7):
            t0, t1 = i / 7.0, (i + 1) / 7.0
            inner0 = base.lerp(apex, t0) + Vector((0.62, 0, 0))
            inner1 = base.lerp(apex, t1) + Vector((0.62, 0, 0))
            member(p, base.lerp(apex, t0), inner1, 0.13, STEEL, overlap=0.13)
            member(p, inner0, inner1, 0.20, STEEL)
        member(p, base + Vector((0.62, 0, 0)), apex + Vector((0.62, 0, 0)),
               0.24, STEEL)
    for i in range(4):                       # transverse bracing between legs
        z = 1.8 + i * 1.9
        t = (z - 0.6) / (apex.z - 0.6)
        y = 2.5 * (1 - t)
        member(p, (-0.6 + t, -y, z), (-0.6 + t, y, z), 0.15, STEEL)
    node(p, apex, 0.85)
    p.box((apex.x, 0, apex.z + 0.55), (1.0, 1.5, 0.44), DARK)

    # Forward tie bars to the boom, and the back stay that balances them.
    for s in (-1, 1):
        rod(p, apex + Vector((0, s * 0.55, 0)),
            Vector((SPAN_LEN * 0.62, s * 1.15, 0.9)), 0.085, STEEL)
        rod(p, apex + Vector((0, s * 0.55, -0.3)),
            Vector((SPAN_LEN - 1.5, s * 0.75, 0.6)), 0.070, STEEL)
        rod(p, apex + Vector((0, s * 0.55, -0.1)),
            Vector((-9.4, s * 1.6, -0.4)), 0.085, STEEL)

    p.box((apex.x, 0, 5.2), (0.55, 0.55, 0.55), ORANGE)   # a painted node
    p.bevel(width=0.028, segments=1)
    return p.finish("Mesh_GantryBoom_Stay", coll)


def build_counter(coll, mats):
    """Counterweight box on a short back-arm. Origin at the pivot, runs -X.

    A cantilever needs its moment balanced somewhere, and putting the ballast
    where the machine's own weight can carry it is what the back-arm is for.
    Modelled as a crate of poured ballast with the frame showing through,
    because a smooth block reads as a prop and a visibly *filled* box reads as
    something with mass in it.
    """
    p = Part(mats)

    for s in (-1, 1):                          # the back-arm itself
        member(p, (0, s * 1.5, 0.55), (-9.8, s * 1.9, -0.35), 0.32, STEEL)
        member(p, (0, s * 1.5, -0.95), (-9.8, s * 1.9, -1.55), 0.32, STEEL)
        for i in range(6):
            t0, t1 = i / 6.0, (i + 1) / 6.0
            a = Vector((0, s * 1.5, 0.55)).lerp(Vector((-9.8, s * 1.9, -0.35)),
                                                t0)
            b = Vector((0, s * 1.5, -0.95)).lerp(Vector((-9.8, s * 1.9, -1.55)),
                                                  t1)
            member(p, a, b, 0.15, STEEL, overlap=0.15)
    for i in range(4):
        x = -1.6 - i * 2.5
        t = -x / 9.8
        y = 1.5 + 0.4 * t
        member(p, (x, -y, 0.55 - 0.9 * t), (x, y, 0.55 - 0.9 * t), 0.15, STEEL)

    # The ballast crate.
    p.box((-11.4, 0, -1.05), (3.9, 5.2, 3.6), HULL)
    for s in (-1, 1):                          # frame ribs showing through
        for i in range(3):
            p.box((-11.4, s * 2.62, -2.35 + i * 1.30), (4.1, 0.10, 0.30), STEEL)
        p.box((-11.4 + s * 1.98, 0, -1.05), (0.10, 5.4, 3.8), STEEL)
    p.box((-11.4, 0, 0.85), (4.2, 5.5, 0.34), DARK)
    p.box((-11.4, 0, -2.92), (4.2, 5.5, 0.30), DARK)
    for i in range(4):                         # lifting eyes on the cap
        p.torus((-12.7 + i * 0.9, 0, 1.10), 0.16, 0.05, 'Y', 12, 6, mat=STEEL)

    rust_patches(p, (-13.0, -2.4, -2.6), (-9.6, 2.4, 0.6), 6, seed=53)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_GantryBoom_Counter", coll)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_span(collection("Coll_GantryBoom_Span", root), mats)
    build_head(collection("Coll_GantryBoom_Head", root), mats)
    build_heel(collection("Coll_GantryBoom_Heel", root), mats)
    build_stay(collection("Coll_GantryBoom_Stay", root), mats)
    build_counter(collection("Coll_GantryBoom_Counter", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
