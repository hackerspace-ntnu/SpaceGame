"""components/structural/scaffold_bay — the stuff buildings stand on before they stand on their own.

Two unrelated technologies live in this one file on purpose. `Bay_Single`,
`Bay_Double` and `Undercroft` are tube-and-coupler scaffold: manufactured
steel, repeated at fixed centres, the same in every settlement because it came
off a rack. `Stilts` is lashed timber cut on site and tied with rope, and
`Ladder` is somewhere in between. A settlement that shows both reads as a place
where the industrial kit ran out and people carried on building anyway — which
is the whole story of the workshop these were made for.

`Undercroft` is the load-bearing one. It is a squat 1.00 m grillage sized to
carry a `cottage_shell` variation: open joists rather than a planked deck, so
the underside stays visible from ground level, which is the entire point of
putting a house on scaffolding rather than on a plinth. **A cottage placed on an
Undercroft goes at exactly +1.00 m above the Undercroft's origin** — that number
is round so the arithmetic never needs checking.

**Origin convention: centre of the footprint, at ground level (z = 0).** Every
variation grows in +Z from there and is centred on X and Y, so dropping one onto
uneven terrain is a single Z move. `Ladder` is the exception and says so.

Every member is its own object. That is a deliberate departure from the rest of
this library, where a variation is usually one merged mesh: these get pulled
apart and re-fitted by hand around whatever they end up carrying, so a single
brace has to be movable without entering edit mode.

    blender --background --python scaffold_bay.py -- --out scaffold_bay.blend

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
from mathutils import Vector  # noqa: E402

MATS = [
    "Mat_Metal_Steel_Worn",        # 0 STEEL   scaffold tube, the bright stuff
    "Mat_Metal_Steel_Dark",        # 1 DARK    couplers, wedges, fixings
    "Mat_Metal_Rust_Heavy",        # 2 RUST    tube ends, base plates in the wet
    "Mat_Wood_Timber_Silvered",    # 3 TIMBER  planks, stilts, toe boards
    "Mat_Wood_Ply_Worn",           # 4 PLY     patched decking, packing shims
    "Mat_Fabric_Rope_Hemp",        # 5 ROPE    lashings on the timber work
    "Mat_Fabric_Tarp_Azure",       # 6 TARP    sheeting lashed to a face
    "Mat_Neutral_Black_Matte",     # 7 BLACK   shadow gaps under the decks
]
STEEL, DARK, RUST, TIMBER, PLY, ROPE, TARP, BLACK = range(8)

TUBE = 0.048          # standard (vertical) tube radius
LEDGE = 0.038         # ledger / transom radius
BRACE = 0.030         # diagonal brace radius
PLANK_W = 0.225
PLANK_T = 0.038


# ---------------------------------------------------------------------------
# Local geometry helpers
# ---------------------------------------------------------------------------

def along(a, b):
    """Rotation that points a Z-aligned primitive from `a` to `b`, and its length."""
    d = Vector(b) - Vector(a)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    return rot.to_4x4(), d.length


def rod(p, a, b, radius=LEDGE, mat=STEEL, seg=8, over=0.0):
    rot, length = along(a, b)
    p.cyl((Vector(a) + Vector(b)) / 2.0, radius, length + over, 'Z', seg=seg,
          mat=mat, rot=rot)


def timber(p, a, b, size=0.13, mat=TIMBER, over=0.0):
    rot, length = along(a, b)
    p.box((Vector(a) + Vector(b)) / 2.0, (size, size, length + over), mat,
          rot=rot)


def coupler(p, at, axis='Z', mat=DARK):
    """The cast fitting where two tubes cross. Cheap, but its absence is what
    makes scaffold read as a wireframe rather than as hardware."""
    p.cyl(at, TUBE + 0.032, 0.10, axis, seg=8, mat=mat)


def emit(coll, name, mats, build, origin=(0, 0, 0), bevel=0.010):
    """One object, built and finished in one call.

    Every part of this component is a separate object, so this wrapper is what
    keeps the variation builders readable instead of drowning in Part
    boilerplate.
    """
    p = Part(mats)
    build(p)
    if bevel:
        p.bevel(width=bevel, segments=1)
    return p.finish(name, coll, origin)


def deck(coll, name, mats, cx, cy, z, span_x, span_y, mat=TIMBER):
    """A run of loose boards laid across a lift, as one object.

    Boards are modelled individually inside the object with a 6 mm gap, because
    a single slab reads as a table top and a scaffold deck never does.
    """
    n = max(1, int(round(span_y / (PLANK_W + 0.006))))
    step = span_y / n

    def build(p):
        for i in range(n):
            y = cy - span_y / 2.0 + step * (i + 0.5)
            p.box((cx, y, z + PLANK_T / 2.0),
                  (span_x, step - 0.006, PLANK_T), mat)
            p.box((cx - span_x / 2.0 + 0.05, y, z + PLANK_T / 2.0),
                  (0.05, step - 0.006, PLANK_T + 0.006), DARK)   # end band
            p.box((cx + span_x / 2.0 - 0.05, y, z + PLANK_T / 2.0),
                  (0.05, step - 0.006, PLANK_T + 0.006), DARK)
    return emit(coll, name, mats, build, bevel=0.006)


def guard_run(coll, name, mats, posts, z, radius=LEDGE, mat=STEEL, close=True):
    """A rail threaded through a ring of post positions at height `z`."""
    def build(p):
        pairs = list(zip(posts, posts[1:] + posts[:1] if close else posts[1:]))
        for (ax, ay), (bx, by) in pairs:
            rod(p, (ax, ay, z), (bx, by, z), radius, mat)
        for x, y in posts:
            coupler(p, (x, y, z), 'Z')
    return emit(coll, name, mats, build)


# ---------------------------------------------------------------------------
# Undercroft — the grillage a cottage sits on
# ---------------------------------------------------------------------------

def build_undercroft(coll, mats):
    """Squat open-joisted table, 3.6 x 3.2 m plan, top of joists at 1.00 m.

    Sized to take the 3.4 x 3.0 m `cottage_shell` footprint with a 0.10 m
    margin all round, so the house visibly sits *on* something rather than
    growing out of it.
    """
    hx, hy, top = 1.80, 1.60, 0.88
    cols = (-hx + 0.14, 0.0, hx - 0.14)
    rows = (-hy + 0.14, hy - 0.14)

    for i, x in enumerate(cols):
        for j, y in enumerate(rows):
            emit(coll, "Mesh_Undercroft_Post_%d%d" % (i, j), mats,
                 lambda p, x=x, y=y: (
                     p.cyl((x, y, top / 2.0), TUBE, top, 'Z', seg=8, mat=STEEL),
                     p.cyl((x, y, 0.06), TUBE + 0.02, 0.12, 'Z', seg=8, mat=RUST)),
                 origin=(x, y, 0.0))

    # Two ring beams. The low one is what stops the whole table racking; the
    # high one carries the joists.
    for k, z in enumerate((0.24, top - 0.06)):
        guard_run(coll, "Mesh_Undercroft_Ledger_%s" % ("Low", "High")[k], mats,
                  [(cols[0], rows[0]), (cols[2], rows[0]),
                   (cols[2], rows[1]), (cols[0], rows[1])], z)

    # Diagonals, one per face, alternating hand so the thing does not look
    # machine-placed.
    faces = (("N", (cols[0], rows[1]), (cols[2], rows[1])),
             ("S", (cols[2], rows[0]), (cols[0], rows[0])),
             ("E", (cols[2], rows[0]), (cols[2], rows[1])),
             ("W", (cols[0], rows[1]), (cols[0], rows[0])))
    for name, a, b in faces:
        emit(coll, "Mesh_Undercroft_Brace_%s" % name, mats,
             lambda p, a=a, b=b: rod(p, (a[0], a[1], 0.10), (b[0], b[1], top - 0.10),
                                     BRACE, STEEL, seg=6))

    def joists(p):
        for i in range(5):
            y = -hy + 0.30 + (2 * hy - 0.60) * i / 4.0
            p.box((0.0, y, top + 0.06), (2 * hx - 0.10, 0.10, 0.12), TIMBER)
        p.box((0.0, -hy + 0.05, top + 0.06), (2 * hx, 0.10, 0.14), TIMBER)
        p.box((0.0, hy - 0.05, top + 0.06), (2 * hx, 0.10, 0.14), TIMBER)
    emit(coll, "Mesh_Undercroft_Joists", mats, joists, bevel=0.008)

    def footings(p):
        for x in cols:
            for y in rows:
                p.box((x, y, 0.015), (0.30, 0.30, 0.03), RUST)      # base plate
                p.box((x, y, 0.045), (0.22, 0.22, 0.03), PLY)       # packing shim
    emit(coll, "Mesh_Undercroft_Footings", mats, footings, bevel=0.006)


# ---------------------------------------------------------------------------
# Tube bays — the working scaffold
# ---------------------------------------------------------------------------

def _tube_bay(coll, mats, tag, length, lifts, sheeted=False):
    """Shared body of Bay_Single and Bay_Double.

    They differ only in how many bays wide they are, so they share a builder;
    what makes them read as different assets is the sheeting and the ladder,
    added by the caller's flags.
    """
    hx, hy = length / 2.0, 0.60
    top = lifts[-1] + 1.05
    n_bays = max(1, int(round(length / 2.0)))
    cols = [-hx + (2 * hx) * i / n_bays for i in range(n_bays + 1)]

    for i, x in enumerate(cols):
        for j, y in enumerate((-hy, hy)):
            emit(coll, "Mesh_%s_Standard_%d%d" % (tag, i, j), mats,
                 lambda p, x=x, y=y: (
                     p.cyl((x, y, top / 2.0), TUBE, top, 'Z', seg=8, mat=STEEL),
                     p.cyl((x, y, top - 0.10), TUBE + 0.012, 0.20, 'Z', seg=8,
                           mat=RUST),
                     p.box((x, y, 0.015), (0.26, 0.26, 0.03), RUST)),
                 origin=(x, y, 0.0))

    for k, z in enumerate(lifts):
        # Transoms carry the boards, ledgers tie the frame together.
        def frame(p, z=z, cols=cols):
            for x in cols:
                rod(p, (x, -hy, z), (x, hy, z), LEDGE, STEEL)
                coupler(p, (x, -hy, z), 'Z')
                coupler(p, (x, hy, z), 'Z')
            for y in (-hy, hy):
                rod(p, (-hx, y, z), (hx, y, z), LEDGE, STEEL)
        emit(coll, "Mesh_%s_Lift_%d" % (tag, k), mats, frame)

        deck(coll, "Mesh_%s_Deck_%d" % (tag, k), mats, 0.0, 0.0,
             z + 0.03, 2 * hx, 2 * hy)

        def toe(p, z=z):
            for y in (-hy, hy):
                p.box((0.0, y, z + 0.18), (2 * hx, 0.035, 0.22), TIMBER)
        emit(coll, "Mesh_%s_ToeBoard_%d" % (tag, k), mats, toe, bevel=0.006)

        guard_run(coll, "Mesh_%s_Guard_%d" % (tag, k), mats,
                  [(-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy)], z + 0.98)

    def braces(p):
        for i in range(n_bays):
            a, b = cols[i], cols[i + 1]
            for k, z in enumerate(lifts):
                lo = 0.10 if k == 0 else lifts[k - 1] + 0.10
                # Alternate the hand bay to bay — a scaffold whose diagonals all
                # lean the same way reads as wallpaper.
                p0, p1 = ((a, lo), (b, z)) if (i + k) % 2 else ((b, lo), (a, z))
                rod(p, (p0[0], -hy, p0[1]), (p1[0], -hy, p1[1]), BRACE, STEEL, seg=6)
    emit(coll, "Mesh_%s_Braces" % tag, mats, braces)

    if sheeted:
        # One storey of one bay, not the whole face. A sheet that clads the
        # entire scaffold hides the scaffold, and the structure is the asset.
        sx0, sx1 = cols[0] - 0.10, cols[1] + 0.30
        sz0, sz1 = lifts[0] + 0.12, lifts[1] - 0.06

        def sheet(p):
            # Billowing across the run and sagging along its hem, because a
            # flat quad reads as a painted board however blue it is.
            sections = []
            steps = 15
            for i in range(steps):
                t = i / (steps - 1.0)
                x = sx0 + (sx1 - sx0) * t
                billow = 0.085 * math.sin(t * math.pi * 2.6)
                hem = 0.17 * math.sin(t * math.pi * 3.4 + 1.1)
                y = -hy - 0.05 - billow
                sections.append((x, [(y - 0.010, sz0 - hem),
                                     (y + 0.010, sz0 - hem),
                                     (y + 0.010, sz1),
                                     (y - 0.010, sz1)]))
            p.loft(sections, axis='X', mat=TARP)
        emit(coll, "Mesh_%s_Sheet" % tag, mats, sheet, bevel=None)

        def lashings(p):
            for i in range(5):
                x = sx0 + (sx1 - sx0) * i / 4.0
                p.torus((x, -hy - 0.03, sz1 - 0.02), 0.06, 0.014, 'Z', 10, 5,
                        mat=ROPE)
        emit(coll, "Mesh_%s_Lashings" % tag, mats, lashings, bevel=None)


def build_bay_single(coll, mats):
    """One 2 m bay, two lifts. The unit the others are multiples of."""
    _tube_bay(coll, mats, "BaySingle", 2.0, (1.95, 3.90))


def build_bay_double(coll, mats):
    """Two bays with sheeting lashed to the outer face — a work screen."""
    _tube_bay(coll, mats, "BayDouble", 4.0, (1.95, 3.90), sheeted=True)


# ---------------------------------------------------------------------------
# Stilts — the same job done with trees and rope
# ---------------------------------------------------------------------------

def build_stilts(coll, mats):
    """Raked timber stilts under a plank platform at 1.40 m.

    The rake is what separates this from the tube bays: every pole leans in
    toward the load, so the silhouette is a splay rather than a grid, and it
    reads at distance as hand-built.
    """
    top = 1.40
    feet = [(-1.45, -1.15), (0.0, -1.30), (1.45, -1.15),
            (-1.45, 1.15), (0.0, 1.30), (1.45, 1.15)]
    for i, (fx, fy) in enumerate(feet):
        head = (fx * 0.72, fy * 0.72)
        emit(coll, "Mesh_Stilts_Pole_%d" % i, mats,
             lambda p, fx=fx, fy=fy, head=head: (
                 timber(p, (fx, fy, 0.0), (head[0], head[1], top), 0.135),
                 p.box((fx, fy, 0.05), (0.34, 0.34, 0.10), PLY)),
             origin=(fx, fy, 0.0))

    def bearers(p):
        for y in (-0.94, 0.0, 0.94):
            p.box((0.0, y, top + 0.07), (2.60, 0.14, 0.14), TIMBER)
    emit(coll, "Mesh_Stilts_Bearers", mats, bearers, bevel=0.008)

    def lashings(p):
        for fx, fy in feet:
            hx_, hy_ = fx * 0.72, fy * 0.72
            for t in (0.55, 0.80):
                x = fx + (hx_ - fx) * t
                y = fy + (hy_ - fy) * t
                p.torus((x, y, top * t), 0.115, 0.022, 'Z', 12, 5, mat=ROPE)
    emit(coll, "Mesh_Stilts_Lashings", mats, lashings, bevel=None)

    def crosspieces(p):
        # Scrap boards nailed across the splay, at the angles that were handy.
        timber(p, (-1.45, -1.15, 0.55), (0.0, -1.30, 1.05), 0.09, PLY)
        timber(p, (1.45, -1.15, 0.50), (0.0, -1.30, 1.00), 0.09, PLY)
        timber(p, (-1.45, 1.15, 0.62), (1.45, 1.15, 0.48), 0.09, PLY)
    emit(coll, "Mesh_Stilts_Crosspieces", mats, crosspieces, bevel=0.008)

    deck(coll, "Mesh_Stilts_Deck", mats, 0.0, 0.0, top + 0.14, 2.90, 2.50, PLY)


# ---------------------------------------------------------------------------
# Ladder — the access piece
# ---------------------------------------------------------------------------

def build_ladder(coll, mats):
    """A leaning ladder to a small landing.

    **Origin exception: the ladder foot, on the floor, at x = 0.** It leans
    into +X and the landing is at the top, so placing one is a matter of
    standing it where the feet go rather than working out where its centre
    would be.
    """
    rise, run = 3.10, 0.92
    rails = ((-0.24, 0.24))

    for i, y in enumerate((-0.24, 0.24)):
        emit(coll, "Mesh_Ladder_Rail_%d" % i, mats,
             lambda p, y=y: timber(p, (0.0, y, 0.0), (run, y, rise), 0.075,
                                   TIMBER, over=0.12),
             origin=(0.0, y, 0.0))

    def rungs(p):
        n = 11
        for i in range(1, n):
            t = i / float(n)
            p.cyl((run * t, 0.0, rise * t), 0.026, 0.50, 'Y', seg=6, mat=TIMBER)
    emit(coll, "Mesh_Ladder_Rungs", mats, rungs, bevel=0.006)

    def landing(p):
        p.box((run + 0.42, 0.0, rise + 0.05), (1.00, 1.10, 0.10), TIMBER)
        for x, y in ((run - 0.02, -0.52), (run - 0.02, 0.52),
                     (run + 0.90, -0.52), (run + 0.90, 0.52)):
            p.cyl((x, y, rise / 2.0), TUBE, rise, 'Z', seg=8, mat=STEEL)
    emit(coll, "Mesh_Ladder_Landing", mats, landing, bevel=0.008)

    guard_run(coll, "Mesh_Ladder_Guard", mats,
              [(run - 0.02, -0.52), (run + 0.90, -0.52),
               (run + 0.90, 0.52), (run - 0.02, 0.52)], rise + 1.02)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_undercroft(collection("Coll_Scaffold_Undercroft", root), mats)
    build_bay_single(collection("Coll_Scaffold_Bay_Single", root), mats)
    build_bay_double(collection("Coll_Scaffold_Bay_Double", root), mats)
    build_stilts(collection("Coll_Scaffold_Stilts", root), mats)
    build_ladder(collection("Coll_Scaffold_Ladder", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
