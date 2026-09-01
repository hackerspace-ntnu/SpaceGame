"""components/structural/cottage_shell — small pastel houses you can walk into.

The outbuildings of the workshop settlement. `shanty_addon` next door is the
same idea done badly on purpose — scrap welded onto somebody else's machine.
These are the opposite: someone owned the ground, squared the plan, and painted
the result. That is why they are pastel and why the corners line up, and it is
what makes the settlement read as *lived in* rather than *squatted in*.

**These are hollow.** Every variation is a shell with real openings, a real
floor slab at each level and a stair connecting them, so a character can go in
the door and up to the first floor. That costs roughly three times the polys of
a solid block, and it is the reason the walls are separate objects: delete
`_WallFront` and the interior is open for set dressing or for a camera.

**Origin convention: centre of the ground-floor footprint, at the walking
surface (z = 0).** The floor slab hangs below into negative Z, so a cottage
dropped onto a `scaffold_bay` `Undercroft` goes at exactly +1.00 m above that
Undercroft's origin with no arithmetic.

Scale is the one thing here that was argued about. Two storeys with genuine
2.05 m and 1.95 m clear heights cannot be made small, so these are kept small
by *footprint* instead — 3.4 x 3.0 m on plan, single-bay, one room per floor.
Against a 6 m tank they read as cottages; against a bigger one they read as
sheds, which is the intended range.

Every wall, roof plane, door and stair is a separate object. See
`scaffold_bay.py` for why this component family departs from the library's
usual one-mesh-per-variation rule.

    blender --background --python cottage_shell.py -- --out cottage_shell.blend

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
    "Mat_Paint_Mint_Pastel",       #  0 MINT    pastel wall enamel
    "Mat_Paint_Butter_Pastel",     #  1 BUTTER  pastel wall enamel
    "Mat_Paint_Rose_Dusty",        #  2 ROSE    pastel wall enamel
    "Mat_Paint_White_Arctic",      #  3 WHITE   the tank's colour, reused on walls
    "Mat_Metal_HullRust_Orange",   #  4 ROOF    corrugated sheet, the warm note
    "Mat_Metal_Rust_Heavy",        #  5 RUST    gutters, roof edges, flashings
    "Mat_Metal_Steel_Worn",        #  6 STEEL   glazing bars, brackets, rails
    "Mat_Metal_Steel_Dark",        #  7 DARK    hinges, latches, flue
    "Mat_Wood_Timber_Silvered",    #  8 TIMBER  frames, stairs, balustrades
    "Mat_Wood_Ply_Worn",           #  9 PLY     door leaves, shutters
    "Mat_Glass_Canopy_Tinted",     # 10 GLASS   panes and the conservatory
    "Mat_Neutral_Black_Matte",     # 11 BLACK   reveals and shadow gaps
    "Mat_Emissive_Cabin_Warm",     # 12 WARM    one lit window per house
    "Mat_Neutral_Panel_Grey",      # 13 FLOOR   floor and stair soffit
]
(MINT, BUTTER, ROSE, WHITE, ROOF, RUST, STEEL, DARK, TIMBER, PLY, GLASS,
 BLACK, WARM, FLOOR) = range(14)

# The one set of dimensions every variation is measured from.
HX, HY = 1.70, 1.50           # half footprint
WALL = 0.15                   # wall thickness
SLAB = 0.16                   # ground floor slab
CLEAR_0 = 2.05                # ground floor clear height
DECK = 0.14                   # upper floor structure
CLEAR_1 = 1.95                # first floor clear height
LEVEL_1 = CLEAR_0 + DECK      # 2.19 — top of the first floor deck
EAVES = LEVEL_1 + CLEAR_1     # 4.14
OVER = 0.28                   # roof overhang


# ---------------------------------------------------------------------------
# Rectangle-with-holes — how every wall and floor in this file is made
# ---------------------------------------------------------------------------

def rect_minus(u0, u1, v0, v1, holes):
    """Split a rectangle into solid sub-rectangles around `holes`.

    Booleans would be the obvious tool and are the wrong one: they leave
    n-gons and stray verts that make the walls unpleasant to hand-edit, which
    is the whole point of this component. Banding the rectangle by the holes'
    v-edges and then splitting each band along u gives clean quads and costs
    twenty lines.
    """
    cuts = sorted({v0, v1} | {c for h in holes for c in (h[2], h[3])
                              if v0 < c < v1})
    out = []
    for va, vb in zip(cuts, cuts[1:]):
        spanning = sorted((h for h in holes if h[2] <= va + 1e-6
                           and h[3] >= vb - 1e-6), key=lambda h: h[0])
        cursor = u0
        for h in spanning:
            if h[0] > cursor:
                out.append((cursor, h[0], va, vb))
            cursor = max(cursor, h[1])
        if cursor < u1:
            out.append((cursor, u1, va, vb))
    return out


def _place(axis, at, thick, u_mid, v_mid, u_len, v_len):
    """Centre and size of a wall slab, in the plane whose normal is `axis`."""
    if axis == 'Y':
        return (u_mid, at, v_mid), (u_len, thick, v_len)
    if axis == 'X':
        return (at, u_mid, v_mid), (thick, u_len, v_len)
    return (u_mid, v_mid, at), (u_len, v_len, thick)


def wall(p, axis, at, u0, u1, v0, v1, holes, mat, thick=WALL):
    """A pierced wall panel. `axis` is the wall's normal; 'Z' makes a floor."""
    for a, b, c, d in rect_minus(u0, u1, v0, v1, holes):
        centre, size = _place(axis, at, thick, (a + b) / 2.0, (c + d) / 2.0,
                              b - a, d - c)
        p.box(centre, size, mat)


def reveal(p, axis, at, hole, mat=BLACK, thick=WALL):
    """The dark lining inside an opening.

    Without it a 0.15 m wall shows daylight through the reveal from any angle
    off-normal, and the house reads as cardboard.
    """
    u0, u1, v0, v1 = hole
    for a, b, c, d in ((u0, u0 + 0.03, v0, v1), (u1 - 0.03, u1, v0, v1),
                       (u0, u1, v0, v0 + 0.03), (u0, u1, v1 - 0.03, v1)):
        centre, size = _place(axis, at, thick * 0.98, (a + b) / 2.0,
                              (c + d) / 2.0, b - a, d - c)
        p.box(centre, size, mat)


def casement(p, axis, at, hole, glass=GLASS, bar=TIMBER, lit=False, thick=WALL):
    """Frame, mullion, transom and pane filling one opening."""
    u0, u1, v0, v1 = hole
    f = 0.055
    for a, b, c, d in ((u0, u0 + f, v0, v1), (u1 - f, u1, v0, v1),
                       (u0, u1, v0, v0 + f), (u0, u1, v1 - f, v1)):
        centre, size = _place(axis, at, thick + 0.05, (a + b) / 2.0,
                              (c + d) / 2.0, b - a, d - c)
        p.box(centre, size, bar)
    um, vm = (u0 + u1) / 2.0, (v0 + v1) / 2.0
    centre, size = _place(axis, at, thick + 0.04, um, vm, 0.035, v1 - v0)
    p.box(centre, size, bar)
    centre, size = _place(axis, at, thick + 0.04, um, vm, u1 - u0, 0.030)
    p.box(centre, size, bar)
    centre, size = _place(axis, at, 0.02, um, vm, u1 - u0 - 2 * f, v1 - v0 - 2 * f)
    p.box(centre, size, WARM if lit else glass)
    # Sill, proud of the wall so it catches light along the elevation.
    centre, size = _place(axis, at, thick + 0.16, um, v0 - 0.03, u1 - u0 + 0.14, 0.06)
    p.box(centre, size, bar)


def emit(coll, name, mats, build, origin=(0, 0, 0), bevel=0.012):
    p = Part(mats)
    build(p)
    if bevel:
        p.bevel(width=bevel, segments=1)
    return p.finish(name, coll, origin)


RIB_PITCH = 0.135             # corrugation wavelength — sheet metal, not battens


def corrugated(p, prof_u, prof_v, extrude, axis, mat=ROOF, ribs=None,
               offset=(0, 0, 0), rib_depth=0.022):
    """A sloped sheet with corrugations running down its fall line.

    The ribs are what make a roof read as sheet metal rather than as a painted
    wedge, and they cost less than they look: one thin box each, laid on the
    outer face. Pitch matters more than depth — at batten spacing the same
    geometry reads as an unfinished roof structure instead of a finished
    covering, which is a different building entirely.
    """
    prof = list(zip(prof_u, prof_v))
    p.prism(prof, extrude, axis=axis, mat=mat, offset=offset)
    ribs = ribs or max(8, int(round(extrude / RIB_PITCH)))
    ax, ay = prof[0], prof[1]
    d = Vector((ax[0] - ay[0], ax[1] - ay[1]))
    length = d.length * 0.985       # stop short of the ridge, or they cross
    ang = math.atan2(d.y, d.x)
    mid_u = (ax[0] + ay[0]) / 2.0
    mid_v = (ax[1] + ay[1]) / 2.0
    for i in range(ribs):
        w = -extrude / 2.0 + extrude * (i + 0.5) / ribs
        if axis == 'Y':
            centre = Vector((mid_u, w, mid_v)) + Vector(offset)
            rot = Matrix.Rotation(ang, 4, 'Y')
            size = (length, extrude / ribs * 0.5, rib_depth)
        else:
            centre = Vector((w, mid_u, mid_v)) + Vector(offset)
            rot = Matrix.Rotation(-ang, 4, 'X')
            size = (extrude / ribs * 0.5, length, rib_depth)
        p.box(centre, size, mat, rot=rot)


def stair_flight(p, x0, x1, y_mid, width, z0, z1, treads=11, mat=TIMBER):
    """A straight flight, built as separate treads on two strings."""
    rise = (z1 - z0) / treads
    run = (x1 - x0) / treads
    for i in range(treads):
        p.box((x0 + run * (i + 0.5), y_mid, z0 + rise * (i + 1) - 0.025),
              (abs(run) + 0.06, width, 0.05), mat)
        p.box((x0 + run * (i + 0.5), y_mid, z0 + rise * (i + 0.5)),
              (0.04, width, rise), mat)
    ang = math.atan2(z1 - z0, x1 - x0)
    length = math.hypot(x1 - x0, z1 - z0)
    for s in (-1, 1):
        p.box(((x0 + x1) / 2.0, y_mid + s * (width / 2.0 + 0.03),
               (z0 + z1) / 2.0 - 0.10), (length, 0.05, 0.26), mat,
              rot=Matrix.Rotation(-ang, 4, 'Y'))


def plinth(p, mat, height=0.34, proud=0.05):
    """The painted skirt every one of these has, because a pastel wall that
    runs straight into the dirt looks unfinished and gets dirty first."""
    p.box((0, 0, height / 2.0), (2 * HX + proud, 2 * HY + proud, height), mat)


def gutter(p, axis, at, u0, u1, z, lip=1, mat=RUST):
    """A gutter run along an eaves line. `axis` is the eaves' normal.

    It has to know its axis: a gable roof's eaves run along Y and a mono-pitch
    roof's along X, and a gutter laid on the wrong one ends up crossing the
    roof under the ridge.
    """
    centre, size = _place(axis, at, 0.13, (u0 + u1) / 2.0, z, u1 - u0, 0.10)
    p.box(centre, size, mat)
    centre, size = _place(axis, at + lip * 0.07, 0.02, (u0 + u1) / 2.0,
                          z + 0.05, u1 - u0, 0.14)
    p.box(centre, size, mat)


def downpipe(p, x, y, z_top, mat=RUST):
    p.cyl((x, y, z_top / 2.0), 0.05, z_top, 'Z', seg=8, mat=mat)
    for z in (0.6, z_top - 0.5):
        p.torus((x, y, z), 0.07, 0.018, 'Z', 8, 5, mat=DARK)


# ---------------------------------------------------------------------------
# Shared sub-assemblies
# ---------------------------------------------------------------------------

def floors(coll, tag, mats, hole=(0.10, 1.55, 0.55, 1.45)):
    """Ground slab, first-floor deck with its stair well, and the stair."""
    emit(coll, "Mesh_%s_FloorGround" % tag, mats,
         lambda p: (p.box((0, 0, -SLAB / 2.0), (2 * HX, 2 * HY, SLAB), FLOOR),
                    p.box((0, 0, -SLAB - 0.03), (2 * HX + 0.10, 2 * HY + 0.10, 0.06),
                          BLACK)))

    def deck(p):
        wall(p, 'Z', LEVEL_1 - DECK / 2.0, -HX, HX, -HY, HY, [hole], FLOOR,
             thick=DECK)
        # Trimmer joists round the well — the edge you would actually see.
        p.box(((hole[0] + hole[1]) / 2.0, hole[2] - 0.05, LEVEL_1 - DECK / 2.0),
              (hole[1] - hole[0], 0.10, DECK + 0.03), TIMBER)
        p.box((hole[0] - 0.05, (hole[2] + hole[3]) / 2.0, LEVEL_1 - DECK / 2.0),
              (0.10, hole[3] - hole[2], DECK + 0.03), TIMBER)
    emit(coll, "Mesh_%s_FloorUpper" % tag, mats, deck)

    emit(coll, "Mesh_%s_Stair" % tag, mats,
         lambda p: stair_flight(p, -1.45, 1.05, (hole[2] + hole[3]) / 2.0,
                                0.80, 0.0, LEVEL_1))


def door(coll, tag, mats, axis, at, hole, swing=0.0, leaf=PLY):
    """Leaf, frame and hardware for one doorway, as one object.

    `swing` in radians opens it; a door standing ajar is the cheapest signal
    that a building is in use rather than sealed.
    """
    u0, u1, v0, v1 = hole

    def build(p):
        f = 0.07
        for a, b, c, d in ((u0 - f, u0, v0, v1 + f), (u1, u1 + f, v0, v1 + f),
                           (u0 - f, u1 + f, v1, v1 + f)):
            centre, size = _place(axis, at, WALL + 0.06, (a + b) / 2.0,
                                  (c + d) / 2.0, b - a, d - c)
            p.box(centre, size, TIMBER)
        w, h = u1 - u0, v1 - v0
        rot = Matrix.Rotation(swing, 4, 'Z')
        hinge_u = u0
        cu = hinge_u + math.cos(swing) * w / 2.0
        off = math.sin(swing) * w / 2.0
        if axis == 'Y':
            p.box((cu, at + off, v0 + h / 2.0), (w, 0.05, h), leaf, rot=rot)
        else:
            p.box((at - off, cu, v0 + h / 2.0), (0.05, w, h), leaf, rot=rot)
        for k in range(3):        # ledged and braced, boards showing
            t = (k + 0.5) / 3.0
            bu = hinge_u + math.cos(swing) * w * t
            boff = math.sin(swing) * w * t
            if axis == 'Y':
                p.box((bu, at + boff, v0 + h / 2.0), (0.03, 0.07, h - 0.10),
                      DARK, rot=rot)
            else:
                p.box((at - boff, bu, v0 + h / 2.0), (0.07, 0.03, h - 0.10),
                      DARK, rot=rot)
    return emit(coll, "Mesh_%s_Door" % tag, mats, build, bevel=0.008)


def flue(coll, tag, mats, x, y, base_z, top_z):
    def build(p):
        p.cyl((x, y, (base_z + top_z) / 2.0), 0.10, top_z - base_z, 'Z', seg=10,
              mat=DARK)
        p.cyl((x, y, base_z + 0.10), 0.14, 0.20, 'Z', seg=10, mat=RUST)
        p.cyl((x, y, top_z + 0.03), 0.16, 0.10, 'Z', seg=10, mat=DARK)
        p.cyl((x, y, top_z + 0.16), 0.13, 0.18, 'Z', seg=10, mat=DARK,
              radius_top=0.05)
        for s in (-1, 1):        # stays, or it would not survive a wind
            a = Vector((x, y, top_z - 0.25))
            b = Vector((x + s * 0.55, y + 0.35, base_z + 0.10))
            d = b - a
            rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
            p.cyl((a + b) / 2.0, 0.014, d.length, 'Z', seg=4, mat=STEEL,
                  rot=rot.to_4x4())
    return emit(coll, "Mesh_%s_Flue" % tag, mats, build, bevel=None)


# ---------------------------------------------------------------------------
# V1 — Gable. The one that looks like a house.
# ---------------------------------------------------------------------------

def build_gable(coll, mats):
    tag = "CottageGable"
    body = MINT
    ridge_z = EAVES + 0.85
    door_hole = (-0.45, 0.45, 0.0, 2.00)
    win_lo = (0.80, 1.50, 0.95, 1.80)
    win_up_f = (-0.42, 0.42, 2.74, 3.55)
    win_side = (-0.50, 0.35, 0.95, 1.80)
    win_side_up = (-0.45, 0.40, 2.74, 3.50)

    floors(coll, tag, mats)

    emit(coll, "Mesh_%s_WallFront" % tag, mats, lambda p: (
        wall(p, 'Y', -HY, -HX, HX, 0.0, EAVES, [door_hole, win_lo, win_up_f], body),
        reveal(p, 'Y', -HY, door_hole), reveal(p, 'Y', -HY, win_lo),
        reveal(p, 'Y', -HY, win_up_f)))

    emit(coll, "Mesh_%s_WallBack" % tag, mats, lambda p: (
        wall(p, 'Y', HY, -HX, HX, 0.0, EAVES, [win_side], body),
        reveal(p, 'Y', HY, win_side)))

    for s, side in ((-1, "Left"), (1, "Right")):
        holes = [win_side_up] if s > 0 else [win_side, win_side_up]
        emit(coll, "Mesh_%s_Wall%s" % (tag, side), mats,
             lambda p, s=s, holes=holes: (
                 wall(p, 'X', s * HX, -HY, HY, 0.0, EAVES, holes, body),
                 [reveal(p, 'X', s * HX, h) for h in holes]))

    # Gable triangles, as prisms because rect_minus cannot do a slope.
    for s, side in ((-1, "Front"), (1, "Back")):
        emit(coll, "Mesh_%s_Gable%s" % (tag, side), mats,
             lambda p, s=s: (
                 p.prism([(-HX, EAVES), (HX, EAVES), (0.0, ridge_z)],
                         WALL, axis='Y', mat=body, offset=(0, s * HY, 0)),
                 p.cyl((0, s * (HY + 0.02), EAVES + 0.44), 0.20, WALL + 0.10,
                       'Y', seg=10, mat=TIMBER),
                 p.cyl((0, s * (HY + 0.02), EAVES + 0.44), 0.15, WALL + 0.14,
                       'Y', seg=10, mat=BLACK)))

    for s, side in ((-1, "West"), (1, "East")):
        emit(coll, "Mesh_%s_Roof%s" % (tag, side), mats,
             lambda p, s=s: corrugated(
                 p, [s * (HX + OVER), 0.0, 0.0, s * (HX + OVER)],
                 [EAVES - 0.06, ridge_z, ridge_z - 0.10, EAVES - 0.16],
                 2 * HY + 2 * OVER, 'Y'), bevel=0.008)

    emit(coll, "Mesh_%s_RoofRidge" % tag, mats, lambda p: (
        p.prism([(-0.26, ridge_z - 0.10), (0.26, ridge_z - 0.10),
                 (0.0, ridge_z + 0.06)], 2 * HY + 2 * OVER + 0.04, axis='Y',
                mat=RUST)), bevel=0.008)

    emit(coll, "Mesh_%s_Barge" % tag, mats, lambda p: [
        (p.prism([(-HX - OVER, EAVES - 0.16), (0.0, ridge_z - 0.02),
                  (0.0, ridge_z - 0.18), (-HX - OVER, EAVES - 0.30)], 0.05,
                 axis='Y', mat=TIMBER, offset=(0, s * (HY + OVER), 0)),
         p.prism([(HX + OVER, EAVES - 0.16), (0.0, ridge_z - 0.02),
                  (0.0, ridge_z - 0.18), (HX + OVER, EAVES - 0.30)], 0.05,
                 axis='Y', mat=TIMBER, offset=(0, s * (HY + OVER), 0)))
        for s in (-1, 1)], bevel=0.006)

    door(coll, tag, mats, 'Y', -HY, door_hole, swing=math.radians(24))

    emit(coll, "Mesh_%s_Windows" % tag, mats, lambda p: (
        casement(p, 'Y', -HY, win_lo, lit=True),
        casement(p, 'Y', -HY, win_up_f),
        casement(p, 'Y', HY, win_side),
        casement(p, 'X', -HX, win_side),
        casement(p, 'X', -HX, win_side_up),
        casement(p, 'X', HX, win_side_up)), bevel=0.008)

    emit(coll, "Mesh_%s_Shutters" % tag, mats, lambda p: [
        p.box((u, -HY - 0.12, (win_lo[2] + win_lo[3]) / 2.0),
              (0.36, 0.05, win_lo[3] - win_lo[2] + 0.08), PLY)
        for u in (win_lo[0] - 0.20, win_lo[1] + 0.20)], bevel=0.008)

    emit(coll, "Mesh_%s_Plinth" % tag, mats, lambda p: plinth(p, WHITE))

    # A gable's eaves run along Y, one down each long side, and the pipe drops
    # off the low corner of the right-hand one.
    emit(coll, "Mesh_%s_Gutter" % tag, mats, lambda p: (
        [gutter(p, 'X', s * (HX + OVER - 0.03), -HY - OVER, HY + OVER,
                EAVES - 0.14, lip=s) for s in (-1, 1)],
        downpipe(p, HX + OVER - 0.03, HY + OVER - 0.14, EAVES - 0.14)),
        bevel=0.008)

    flue(coll, tag, mats, -HX + 0.45, HY - 0.50, EAVES - 1.2, ridge_z + 0.55)


# ---------------------------------------------------------------------------
# V2 — Shed. Mono-pitch, with the first floor set back over a veranda.
# ---------------------------------------------------------------------------

def build_shed(coll, mats):
    tag = "CottageShed"
    body = BUTTER
    high, low = EAVES + 0.42, EAVES - 0.52
    setback = 0.95                      # first floor pulled back from the front
    door_hole = (0.30, 1.20, 0.0, 2.00)
    win_lo = (-1.25, -0.30, 0.95, 1.85)
    win_up = (-0.95, 0.95, 2.74, 3.62)
    win_side = (-0.55, 0.45, 1.00, 1.85)

    floors(coll, tag, mats, hole=(0.10, 1.55, -0.30, 0.60))

    emit(coll, "Mesh_%s_WallFront" % tag, mats, lambda p: (
        wall(p, 'Y', -HY, -HX, HX, 0.0, CLEAR_0, [door_hole, win_lo], body),
        reveal(p, 'Y', -HY, door_hole), reveal(p, 'Y', -HY, win_lo)))

    emit(coll, "Mesh_%s_WallUpperFront" % tag, mats, lambda p: (
        wall(p, 'Y', -HY + setback, -HX, HX, LEVEL_1, EAVES, [win_up], body),
        reveal(p, 'Y', -HY + setback, win_up)))

    emit(coll, "Mesh_%s_WallBack" % tag, mats, lambda p: (
        wall(p, 'Y', HY, -HX, HX, 0.0, EAVES, [win_side], body),
        reveal(p, 'Y', HY, win_side)))

    for s, side in ((-1, "Left"), (1, "Right")):
        emit(coll, "Mesh_%s_Wall%s" % (tag, side), mats,
             lambda p, s=s: (
                 wall(p, 'X', s * HX, -HY, HY, 0.0, CLEAR_0, [win_side], body),
                 reveal(p, 'X', s * HX, win_side),
                 # Upper storey follows the set-back and the roof's fall.
                 p.prism([(-HY + setback, LEVEL_1), (HY, LEVEL_1),
                          (HY, low - 0.10),
                          (-HY + setback, high - 0.10)], WALL, axis='X',
                         mat=body, offset=(s * HX, 0, 0))))

    emit(coll, "Mesh_%s_Roof" % tag, mats, lambda p: corrugated(
        p, [-HY + setback - OVER, HY + OVER, HY + OVER, -HY + setback - OVER],
        [high, low, low - 0.09, high - 0.09], 2 * HX + 2 * OVER, 'X'), bevel=0.008)

    # The veranda roof — a second, shallower plane over the open front.
    emit(coll, "Mesh_%s_VerandaRoof" % tag, mats, lambda p: corrugated(
        p, [-HY - OVER, -HY + setback + 0.05, -HY + setback + 0.05, -HY - OVER],
        [LEVEL_1 - 0.30, LEVEL_1 + 0.02, LEVEL_1 - 0.07, LEVEL_1 - 0.39],
        2 * HX, 'X'), bevel=0.008)

    emit(coll, "Mesh_%s_VerandaPosts" % tag, mats, lambda p: [
        p.box((x, -HY + 0.12, (LEVEL_1 - 0.35) / 2.0),
              (0.11, 0.11, LEVEL_1 - 0.35), TIMBER)
        for x in (-HX + 0.18, 0.0, HX - 0.18)], bevel=0.008)

    emit(coll, "Mesh_%s_VerandaRail" % tag, mats, lambda p: (
        p.box((0, -HY + 0.12, 0.98), (2 * HX - 0.30, 0.06, 0.08), TIMBER),
        p.box((0, -HY + 0.12, 0.56), (2 * HX - 0.30, 0.05, 0.05), TIMBER),
        [p.box((-HX + 0.30 + i * 0.28, -HY + 0.12, 0.72), (0.04, 0.04, 0.52),
               TIMBER) for i in range(10)]), bevel=0.006)

    # First-floor balcony on the roof of the set-back — the reason for it.
    emit(coll, "Mesh_%s_BalconyDeck" % tag, mats, lambda p: (
        p.box((0, -HY + setback / 2.0 - 0.10, LEVEL_1 - 0.05),
              (2 * HX - 0.10, setback + 0.20, 0.10), TIMBER)), bevel=0.008)

    emit(coll, "Mesh_%s_BalconyRail" % tag, mats, lambda p: (
        p.box((0, -HY + 0.06, LEVEL_1 + 0.46), (2 * HX - 0.10, 0.05, 0.06), STEEL),
        [p.cyl((-HX + 0.14 + i * 0.30, -HY + 0.06, LEVEL_1 + 0.24), 0.016, 0.50,
               'Z', seg=6, mat=STEEL) for i in range(11)]), bevel=None)

    door(coll, tag, mats, 'Y', -HY, door_hole, swing=math.radians(-14))
    door(coll, tag + "Upper", mats, 'Y', -HY + setback,
         (-0.42, 0.44, LEVEL_1, LEVEL_1 + 1.90), swing=math.radians(8), leaf=TIMBER)

    emit(coll, "Mesh_%s_Windows" % tag, mats, lambda p: (
        casement(p, 'Y', -HY, win_lo),
        casement(p, 'Y', -HY + setback, win_up, lit=True),
        casement(p, 'Y', HY, win_side),
        casement(p, 'X', -HX, win_side),
        casement(p, 'X', HX, win_side)), bevel=0.008)

    emit(coll, "Mesh_%s_Plinth" % tag, mats, lambda p: plinth(p, WHITE, 0.28))

    # A mono-pitch has one eaves line, at the bottom of the fall.
    emit(coll, "Mesh_%s_Gutter" % tag, mats, lambda p: (
        gutter(p, 'Y', HY + OVER - 0.06, -HX - OVER, HX + OVER, low - 0.14),
        downpipe(p, HX + OVER - 0.14, HY + OVER - 0.06, low - 0.14)),
        bevel=0.008)

    flue(coll, tag, mats, HX - 0.40, HY - 0.55, CLEAR_0, low + 0.95)


# ---------------------------------------------------------------------------
# V3 — Glasshouse. Painted ground floor, glazed lean-to above.
# ---------------------------------------------------------------------------

def build_glasshouse(coll, mats):
    tag = "CottageGlass"
    body = ROSE
    high, low = EAVES + 0.30, LEVEL_1 + 0.70
    door_hole = (-0.45, 0.45, 0.0, 2.00)
    win_lo = (0.85, 1.50, 0.95, 1.80)
    win_lo2 = (-1.50, -0.85, 0.95, 1.80)

    floors(coll, tag, mats)

    emit(coll, "Mesh_%s_WallFront" % tag, mats, lambda p: (
        wall(p, 'Y', -HY, -HX, HX, 0.0, CLEAR_0, [door_hole, win_lo, win_lo2], body),
        [reveal(p, 'Y', -HY, h) for h in (door_hole, win_lo, win_lo2)]))

    emit(coll, "Mesh_%s_WallBack" % tag, mats, lambda p: (
        wall(p, 'Y', HY, -HX, HX, 0.0, EAVES - 0.30, [], body),
        # The back is the tall side of the lean-to, so it carries the whole
        # height and the glass leans off it.
        p.box((0, HY, EAVES - 0.15), (2 * HX, WALL, 0.30), body)))

    for s, side in ((-1, "Left"), (1, "Right")):
        emit(coll, "Mesh_%s_Wall%s" % (tag, side), mats,
             lambda p, s=s: (
                 wall(p, 'X', s * HX, -HY, HY, 0.0, CLEAR_0, [], body),
                 p.prism([(-HY, LEVEL_1), (HY, LEVEL_1), (HY, high),
                          (-HY, low)], WALL * 0.6, axis='X', mat=STEEL,
                         offset=(s * HX, 0, 0))))

    emit(coll, "Mesh_%s_GlazingBars" % tag, mats, lambda p: (
        # Uprights round the conservatory, then rafters up its slope.
        [p.box((x, y, (LEVEL_1 + (high if y > 0 else low)) / 2.0),
               (0.06, 0.06, (high if y > 0 else low) - LEVEL_1), STEEL)
         for x in (-HX + 0.05, 0.0, HX - 0.05) for y in (-HY + 0.05, HY - 0.05)],
        [p.box((x, 0.0, (low + high) / 2.0 + 0.02),
               (0.055, math.hypot(2 * HY, high - low), 0.055), STEEL,
               rot=Matrix.Rotation(math.atan2(high - low, 2 * HY), 4, 'X'))
         for x in (-HX + 0.05, -0.85, 0.0, 0.85, HX - 0.05)],
        # Purlins across the fall, one per pane course. Two of these read as a
        # ladder leaning on a roof; five read as glazing.
        [p.box((0, -HY + 2 * HY * t, low + (high - low) * t - 0.08),
               (2 * HX, 0.045, 0.045), STEEL)
         for t in (0.12, 0.31, 0.50, 0.69, 0.88)]),
        bevel=0.006)

    emit(coll, "Mesh_%s_GlassRoof" % tag, mats, lambda p: p.prism(
        [(-HY - 0.14, low - 0.06), (HY + 0.10, high - 0.06),
         (HY + 0.10, high - 0.10), (-HY - 0.14, low - 0.10)],
        2 * HX - 0.06, axis='X', mat=GLASS), bevel=None)

    emit(coll, "Mesh_%s_GlassWalls" % tag, mats, lambda p: (
        [p.box((s * (HX - 0.03), 0.0, (LEVEL_1 + (low + high) / 2.0) / 2.0 + 0.30),
               (0.03, 2 * HY - 0.16, 1.20), GLASS) for s in (-1, 1)],
        p.box((0, -HY + 0.05, (LEVEL_1 + low) / 2.0),
              (2 * HX - 0.16, 0.03, low - LEVEL_1 - 0.10), GLASS)), bevel=None)

    emit(coll, "Mesh_%s_RoofEave" % tag, mats, lambda p: (
        p.box((0, -HY - 0.14, low - 0.14), (2 * HX + 0.10, 0.14, 0.10), RUST),
        p.box((0, HY + 0.12, high - 0.06), (2 * HX + 0.10, 0.16, 0.12), RUST)),
        bevel=0.008)

    door(coll, tag, mats, 'Y', -HY, door_hole, swing=math.radians(-30))

    emit(coll, "Mesh_%s_Windows" % tag, mats, lambda p: (
        casement(p, 'Y', -HY, win_lo, lit=True),
        casement(p, 'Y', -HY, win_lo2)), bevel=0.008)

    # Staging benches under the glass — this is a growing house, so say so.
    emit(coll, "Mesh_%s_Staging" % tag, mats, lambda p: [
        (p.box((0, s * (HY - 0.42), LEVEL_1 + 0.78), (2 * HX - 0.24, 0.70, 0.05), TIMBER),
         [p.box((x, s * (HY - 0.42), LEVEL_1 + 0.39), (0.07, 0.07, 0.78), TIMBER)
          for x in (-HX + 0.30, HX - 0.30)])
        for s in (-1, 1)], bevel=0.008)

    emit(coll, "Mesh_%s_Plinth" % tag, mats, lambda p: plinth(p, WHITE, 0.40))


# ---------------------------------------------------------------------------
# V4 — Corner. L-plan with an outside stair to a first-floor door.
# ---------------------------------------------------------------------------

def build_corner(coll, mats):
    tag = "CottageCorner"
    body = WHITE
    trim = MINT
    tower = 0.85                        # half-width of the stair tower
    tx = HX + tower                     # tower centre on X
    tower_top = EAVES + 1.30
    ridge_z = EAVES + 0.72
    door_hole = (-0.40, 0.50, 0.0, 2.00)
    win_lo = (0.90, 1.50, 0.95, 1.80)
    win_up = (-1.20, -0.40, 2.74, 3.55)
    win_side = (-0.45, 0.45, 1.05, 1.85)

    floors(coll, tag, mats, hole=(HX - 0.55, HX, -0.45, 0.45))

    emit(coll, "Mesh_%s_WallFront" % tag, mats, lambda p: (
        wall(p, 'Y', -HY, -HX, HX, 0.0, EAVES, [door_hole, win_lo, win_up], body),
        [reveal(p, 'Y', -HY, h) for h in (door_hole, win_lo, win_up)]))

    emit(coll, "Mesh_%s_WallBack" % tag, mats, lambda p: (
        wall(p, 'Y', HY, -HX, HX, 0.0, EAVES, [win_side], body),
        reveal(p, 'Y', HY, win_side)))

    emit(coll, "Mesh_%s_WallLeft" % tag, mats, lambda p: (
        wall(p, 'X', -HX, -HY, HY, 0.0, EAVES, [win_side], body),
        reveal(p, 'X', -HX, win_side)))

    # The right wall is shared with the tower, so it is pierced at both levels.
    link_lo = (-0.45, 0.45, 0.0, 2.00)
    link_up = (-0.45, 0.45, LEVEL_1, LEVEL_1 + 1.85)
    emit(coll, "Mesh_%s_WallRight" % tag, mats, lambda p: (
        wall(p, 'X', HX, -HY, HY, 0.0, EAVES, [link_lo, link_up], body),
        reveal(p, 'X', HX, link_lo), reveal(p, 'X', HX, link_up)))

    for s, side in ((-1, "Front"), (1, "Back")):
        emit(coll, "Mesh_%s_Gable%s" % (tag, side), mats,
             lambda p, s=s: p.prism([(-HX, EAVES), (HX, EAVES), (0.0, ridge_z)],
                                    WALL, axis='Y', mat=body,
                                    offset=(0, s * HY, 0)))

    for s, side in ((-1, "West"), (1, "East")):
        emit(coll, "Mesh_%s_Roof%s" % (tag, side), mats,
             lambda p, s=s: corrugated(
                 p, [s * (HX + OVER), 0.0, 0.0, s * (HX + OVER)],
                 [EAVES - 0.06, ridge_z, ridge_z - 0.09, EAVES - 0.15],
                 2 * HY + 2 * OVER, 'Y'), bevel=0.008)

    # --- the tower -------------------------------------------------------
    tower_win = (-0.32, 0.32, 2.30, 3.90)
    emit(coll, "Mesh_%s_TowerWalls" % tag, mats, lambda p: (
        wall(p, 'Y', -tower, tx - tower, tx + tower, 0.0, tower_top,
             [(tx - 0.45, tx + 0.45, LEVEL_1, LEVEL_1 + 1.85)], body),
        reveal(p, 'Y', -tower, (tx - 0.45, tx + 0.45, LEVEL_1, LEVEL_1 + 1.85)),
        wall(p, 'Y', tower, tx - tower, tx + tower, 0.0, tower_top, [], body),
        wall(p, 'X', tx + tower, -tower, tower, 0.0, tower_top, [tower_win], body),
        reveal(p, 'X', tx + tower, tower_win)))

    emit(coll, "Mesh_%s_TowerRoof" % tag, mats, lambda p: corrugated(
        p, [tx - tower - 0.22, tx + tower + 0.22, tx + tower + 0.22,
            tx - tower - 0.22],
        [tower_top + 0.30, tower_top - 0.02, tower_top - 0.11, tower_top + 0.21],
        2 * tower + 0.44, 'Y'), bevel=0.008)

    emit(coll, "Mesh_%s_TowerWindow" % tag, mats,
         lambda p: casement(p, 'X', tx + tower, tower_win, lit=True), bevel=0.008)

    # The outside stair. This is the silhouette move: it puts a diagonal on a
    # building that is otherwise all verticals, and it is why the tower exists.
    # `stair_flight` runs it downhill in -X, so the slope is rise over a
    # negative run and the boxes laid along it rotate by -angle about Y, the
    # same convention the flight's own strings use.
    stair_foot, stair_head = tx + tower + 1.85, tx + tower + 0.05
    stair_run, stair_rise = stair_head - stair_foot, LEVEL_1 + 0.02
    stair_ang = -math.atan2(stair_rise, stair_run)
    stair_len = math.hypot(stair_run, stair_rise)

    emit(coll, "Mesh_%s_OuterStair" % tag, mats, lambda p: (
        stair_flight(p, stair_foot, stair_head, -tower + 0.45, 0.90, 0.0,
                     stair_rise, treads=12),
        p.box(((stair_foot + stair_head) / 2.0, -tower + 0.45, stair_rise / 2.0 - 0.20),
              (stair_len, 0.94, 0.12), TIMBER,
              rot=Matrix.Rotation(stair_ang, 4, 'Y'))),
        bevel=0.008)

    emit(coll, "Mesh_%s_OuterStairRail" % tag, mats, lambda p: (
        [p.cyl((stair_head - stair_run * i / 6.0, -tower - 0.02,
                stair_rise * (1.0 - i / 6.0) + 0.48), 0.018, 0.95, 'Z',
               seg=6, mat=STEEL) for i in range(7)],
        p.box(((stair_foot + stair_head) / 2.0, -tower - 0.02,
               stair_rise / 2.0 + 0.95), (stair_len, 0.05, 0.05), STEEL,
              rot=Matrix.Rotation(stair_ang, 4, 'Y'))),
        bevel=None)

    emit(coll, "Mesh_%s_Landing" % tag, mats, lambda p: (
        p.box((tx + tower + 0.35, 0.0, LEVEL_1 - 0.06), (0.80, 2 * tower, 0.12),
              TIMBER),
        [p.box((tx + tower + 0.68, s * (tower - 0.08), (LEVEL_1 - 0.12) / 2.0),
               (0.10, 0.10, LEVEL_1 - 0.12), TIMBER) for s in (-1, 1)]),
        bevel=0.008)

    door(coll, tag, mats, 'Y', -HY, door_hole, swing=math.radians(18))
    door(coll, tag + "Tower", mats, 'Y', -tower,
         (tx - 0.45, tx + 0.45, LEVEL_1, LEVEL_1 + 1.85), swing=math.radians(-22),
         leaf=TIMBER)

    emit(coll, "Mesh_%s_Windows" % tag, mats, lambda p: (
        casement(p, 'Y', -HY, win_lo, lit=True),
        casement(p, 'Y', -HY, win_up),
        casement(p, 'Y', HY, win_side),
        casement(p, 'X', -HX, win_side)), bevel=0.008)

    # Trim band at first-floor level, in the pastel, tying the two masses
    # together — without it the tower reads as a separate building.
    emit(coll, "Mesh_%s_TrimBand" % tag, mats, lambda p: (
        p.box((0, 0, LEVEL_1 - 0.10), (2 * HX + 0.09, 2 * HY + 0.09, 0.20), trim),
        p.box((tx, 0, LEVEL_1 - 0.10), (2 * tower + 0.09, 2 * tower + 0.09, 0.20),
              trim)), bevel=0.008)

    emit(coll, "Mesh_%s_Plinth" % tag, mats, lambda p: (
        plinth(p, trim, 0.30),
        p.box((tx, 0, 0.15), (2 * tower + 0.05, 2 * tower + 0.05, 0.30), trim)))

    flue(coll, tag, mats, -HX + 0.40, HY - 0.45, EAVES - 1.0, ridge_z + 0.60)


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_gable(collection("Coll_Cottage_Gable", root), mats)
    build_shed(collection("Coll_Cottage_Shed", root), mats)
    build_glasshouse(collection("Coll_Cottage_Glasshouse", root), mats)
    build_corner(collection("Coll_Cottage_Corner", root), mats)

    report()
    save(out)


if __name__ == "__main__":
    main()
