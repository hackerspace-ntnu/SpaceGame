"""components/structural/scrap_walkway - the nomads' patched-together walking kit.

Decks, railings, stairs and lanterns in the Sky Tribe's language: nothing
matches, everything was salvaged, and every joint is lashed or bolted by hand.
Built for the sky city, whose first walkway pass was rejected for reading as
"way too industrious and modern" - one continuous steel slab, square steel
stairs, a lamp on every fifth post. The rule this kit exists to enforce:

- **No run of walkway is one thing.** A deck is laid as planks, as rusted plate,
  as grating or as scrap, and a walkway is several decks stitched end to end.
- **Every straight line wobbles a little.** Planks are yawed a degree or two and
  cut to ragged lengths, posts lean, ropes sag. Never enough to change where a
  player stands: every walking surface keeps its top face at the height asked
  for, and all the wobble goes sideways or down.
- **Rust has a ramp.** Pale where the sun hits, heavy on the flats, deep in the
  joints - three palette materials used together, never one flat repaint.

The builders draw geometry only. Collision belongs to whoever places a piece:
`sky_city_traversal` draws a deck with `deck()` and lays its own collision box
under the same numbers. Every function takes a `Part` built with `MATS` and a
seeded `random.Random`, so a rebuild reproduces the same planks.

Library variations (one collection each), built by running this file:

    blender --background --python scrap_walkway.py -- --out scrap_walkway.blend

Generation script - historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part  # noqa: E402

from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Wood_Timber_Silvered",   # 0 posts, stringers, planks - sun-greyed timber
    "Mat_Wood_Ply_Worn",          # 1 replacement boards and sheet patches
    "Mat_Fabric_Rope_Hemp",       # 2 hand lines and lashings
    "Mat_Metal_Rust_Heavy",       # 3 plate flats
    "Mat_Metal_Rust_Pale",        # 4 sun-bleached plate
    "Mat_Metal_Rust_Deep",        # 5 joints, undersides, grating frames
    "Mat_Metal_HullRust_Orange",  # 6 salvaged hull plate
    "Mat_Metal_Steel_Dark",       # 7 bolts, brackets, pipe fittings
    "Mat_Fabric_Canvas_Faded",    # 8 tarp patches and wrapped rails
    "Mat_Emissive_Amber",         # 9 lantern light
    "Mat_Paint_Hazard_Yellow",    # 10 old paint surviving on salvaged steel
    "Mat_Neutral_Black_Matte",    # 11 the dark under a grating
]
(TIMBER, PLY, ROPE, RUST, RUST_PALE, RUST_DEEP, HULLRUST, DARK, CANVAS, LAMP,
 YELLOW, BLACK) = range(12)

UP = Vector((0.0, 0.0, 1.0))
RAIL_H = 1.10                # the library's rail height, and the collision's
DECK_STYLES = ("planks", "plate", "grating", "scrap")
RAIL_STYLES = ("rope", "pipe", "sheet", "net")
STAIR_STYLES = ("timber", "scrap")
RISER = 0.2                  # drawn step height; the collision is a smooth ramp
PLANK_T = 0.055              # deck plank thickness
LADDER_W = 0.7               # between rails: narrower than the 1.0 m capsule, so a gap
                             # left for a ladder in a railing is not a gap to fall through
RUNG_PITCH = 0.3
GRAB = 1.1                   # rails carried above the top rung, to hold stepping off
STANDOFF = 0.25              # rail to wall
POST_GAP = (1.5, 2.1)        # railing post spacing, drawn per bay


# ---------------------------------------------------------------------------
# Oriented primitives
# ---------------------------------------------------------------------------

def frame(d):
    """Rotation whose local Y runs along horizontal direction `d`, Z up."""
    d = Vector((d.x, d.y, 0.0)).normalized()
    s = d.cross(UP).normalized()
    return Matrix((s, d, UP)).transposed().to_4x4()


def obox(p, center, size, d, mat, yaw=0.0, tilt=0.0):
    """A box sized (across, along, up) against horizontal direction `d`."""
    rot = frame(d) @ Matrix.Rotation(yaw, 4, 'Z') @ Matrix.Rotation(tilt, 4, 'X')
    return p.box(center, size, mat, rot=rot)


def strut(p, a, b, w, h, mat):
    """A box member between two points - posts, props, stringers, rails."""
    a, b = Vector(a), Vector(b)
    d = b - a
    if d.length < 1e-6:
        return []
    rot = d.to_track_quat('Y', 'Z').to_matrix().to_4x4()
    return p.box((a + b) / 2, (w, d.length, h), mat, rot=rot)


def pole(p, a, b, r, mat, seg=6):
    """A round member between two points - pipes, spars, rope."""
    a, b = Vector(a), Vector(b)
    d = b - a
    if d.length < 1e-6:
        return []
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    return p.cyl((a + b) / 2, r, d.length, 'Z', seg=seg, mat=mat, rot=rot)


def rope(p, a, b, sag=0.0, r=0.03, steps=4, mat=ROPE):
    a, b = Vector(a), Vector(b)
    pts = []
    for i in range(steps + 1):
        t = i / steps
        q = a.lerp(b, t)
        q.z -= sag * math.sin(math.pi * t)
        pts.append(q)
    for u, v in zip(pts, pts[1:]):
        pole(p, u, v, r, mat, seg=5)


def lashing(p, at, r, turns=3, mat=ROPE):
    """A few wraps of rope round a joint - the tribe's universal fastener."""
    at = Vector(at)
    for i in range(turns):
        p.torus(at + Vector((0, 0, (i - (turns - 1) / 2) * 0.035)), r + 0.012, 0.014,
                maj_seg=8, min_seg=4, mat=mat)


# ---------------------------------------------------------------------------
# Decks
# ---------------------------------------------------------------------------

def deck(p, lo, hi, z, style, rng, across='x'):
    """A rectangular deck lo..hi (x, y) whose walking face is exactly at `z`.

    `across` is the axis the planks and plate seams run along - across the
    direction people walk, the way a real boardwalk is laid.
    """
    x0, y0 = min(lo[0], hi[0]), min(lo[1], hi[1])
    x1, y1 = max(lo[0], hi[0]), max(lo[1], hi[1])
    {"planks": _planks, "plate": _plate, "grating": _grating, "scrap": _scrap}[style](
        p, x0, y0, x1, y1, z, rng, across)


def _span(x0, y0, x1, y1, across):
    """(along-start, along-end, across-start, across-end) and a point builder."""
    if across == 'x':
        return y0, y1, x0, x1, lambda a, c, zz: Vector((c, a, zz))
    return x0, x1, y0, y1, lambda a, c, zz: Vector((a, c, zz))


def _joists(p, x0, y0, x1, y1, z, rng, across, mat, count):
    a0, a1, c0, c1, pt = _span(x0, y0, x1, y1, across)
    for i in range(count):
        c = c0 + 0.25 + (c1 - c0 - 0.5) * i / max(1, count - 1)
        c += rng.uniform(-0.12, 0.12)
        strut(p, pt(a0 + 0.05, c, z - 0.19), pt(a1 - 0.05, c, z - 0.19), 0.14, 0.2, mat)


def _planks(p, x0, y0, x1, y1, z, rng, across):
    a0, a1, c0, c1, pt = _span(x0, y0, x1, y1, across)
    _joists(p, x0, y0, x1, y1, z, rng, across, TIMBER, 3 if c1 - c0 > 3.0 else 2)
    a = a0
    while a < a1 - 0.05:
        w = min(rng.uniform(0.2, 0.32), a1 - a)
        mat = rng.choices((TIMBER, PLY, RUST), (0.55, 0.37, 0.08))[0]
        e0, e1 = c0 + rng.uniform(-0.08, 0.1), c1 - rng.uniform(-0.08, 0.1)
        # One board in eight was cut short and the gap left open to the joist.
        if rng.random() < 0.125 and e1 - e0 > 1.6:
            e1 -= rng.uniform(0.25, 0.6)
        drop = rng.uniform(0.0, 0.015)
        # 55 mm, not 60: the sky city's old cantilevered scraps top out at
        # exactly 60 mm under the walkway, and a plank bottom there z-fights.
        center = pt(a + w / 2, (e0 + e1) / 2, z - PLANK_T / 2 - drop)
        d = pt(1.0, 0.0, 0.0) - pt(0.0, 0.0, 0.0)
        obox(p, center, (e1 - e0, w - rng.uniform(0.02, 0.05), PLANK_T), Vector((d.x, d.y, 0)), mat,
             yaw=math.radians(rng.uniform(-1.2, 1.2)))
        a += w


def _plate(p, x0, y0, x1, y1, z, rng, across):
    a0, a1, c0, c1, pt = _span(x0, y0, x1, y1, across)
    _joists(p, x0, y0, x1, y1, z, rng, across, RUST_DEEP, 3)
    a = a0
    while a < a1 - 0.05:
        run = min(rng.uniform(1.1, 2.4), a1 - a)
        c = c0
        while c < c1 - 0.05:
            w = min(rng.uniform(1.0, 2.2), c1 - c)
            mat = rng.choices((RUST, HULLRUST, RUST_PALE, YELLOW), (0.4, 0.3, 0.22, 0.08))[0]
            drop = rng.uniform(0.0, 0.01)
            p.slab(pt(a + 0.01, c + 0.01, z - 0.05 - drop), pt(a + run - 0.01, c + w - 0.01, z - drop), mat)
            c += w
        p.rivets(pt(a + 0.06, c0 + 0.1, z), pt(a + 0.06, c1 - 0.1, z), max(3, int((c1 - c0) / 0.35)),
                 radius=0.025, height=0.012, mat=DARK)
        a += run
    if a1 - a0 > 2.0 and rng.random() < 0.7:
        pa = rng.uniform(a0 + 0.3, a1 - 1.3)
        pc = rng.uniform(c0 + 0.2, max(c0 + 0.21, c1 - 1.2))
        p.slab(pt(pa, pc, z - 0.012), pt(pa + rng.uniform(0.6, 1.0), pc + rng.uniform(0.5, 0.9), z),
               RUST_DEEP)


def _grating(p, x0, y0, x1, y1, z, rng, across):
    a0, a1, c0, c1, pt = _span(x0, y0, x1, y1, across)
    p.slab(pt(a0, c0, z - 0.34), pt(a1, c1, z - 0.3), BLACK)
    for c in (c0, c1 - 0.12):
        p.slab(pt(a0, c, z - 0.1), pt(a1, c + 0.12, z), RUST_DEEP)
    for a in (a0, a1 - 0.12):
        p.slab(pt(a, c0, z - 0.1), pt(a + 0.12, c1, z), RUST_DEEP)
    a = a0 + 0.12
    while a < a1 - 0.15:
        p.slab(pt(a, c0 + 0.12, z - 0.07), pt(a + 0.03, c1 - 0.12, z - rng.uniform(0.0, 0.006)),
               rng.choices((RUST, RUST_DEEP), (0.7, 0.3))[0])
        a += 0.11
    _joists(p, x0, y0, x1, y1, z - 0.05, rng, across, RUST_DEEP, 2)
    # A board thrown over the worst of it.
    if a1 - a0 > 1.5:
        pa = rng.uniform(a0 + 0.2, a1 - 1.0)
        p.slab(pt(pa, c0 + 0.1, z - 0.05), pt(pa + 0.28, c1 - 0.1, z - 0.002), TIMBER)


def _scrap(p, x0, y0, x1, y1, z, rng, across):
    a0, a1, c0, c1, pt = _span(x0, y0, x1, y1, across)
    _joists(p, x0, y0, x1, y1, z, rng, across, TIMBER, 2)
    a = a0
    while a < a1 - 0.05:
        run = min(rng.uniform(0.6, 1.8), a1 - a)
        kind = rng.choice(("ply", "hull", "planks"))
        if kind == "planks":
            u, v = pt(a, c0, 0.0), pt(a + run, c1, 0.0)
            _planks(p, min(u.x, v.x), min(u.y, v.y), max(u.x, v.x), max(u.y, v.y), z,
                    random.Random(rng.random()), across)
        else:
            mat = PLY if kind == "ply" else rng.choice((HULLRUST, RUST))
            p.slab(pt(a + 0.02, c0 + rng.uniform(0.0, 0.1), z - 0.05 - rng.uniform(0.0, 0.01)),
                   pt(a + run - 0.02, c1 - rng.uniform(0.0, 0.1), z - rng.uniform(0.0, 0.008)), mat)
            if kind == "ply":
                for c in (c0 + 0.15, c1 - 0.15):
                    p.rivets(pt(a + 0.1, c, z), pt(a + run - 0.1, c, z), 3, radius=0.02,
                             height=0.01, mat=DARK)
        a += run


# ---------------------------------------------------------------------------
# Railings
# ---------------------------------------------------------------------------

def railing(p, a, b, z, style, rng, lean=True, z_b=None):
    """A railing standing on the line a -> b (x, y), at walking height z.

    `z_b` makes it follow a slope - a stair's side - rising to z_b at b.
    """
    a3 = Vector((a[0], a[1], z))
    b3 = Vector((b[0], b[1], z if z_b is None else z_b))
    length = (b3.xy - a3.xy).length
    if length < 0.15:
        return
    bays = max(1, round(length / rng.uniform(*POST_GAP)))
    posts = []
    for i in range(bays + 1):
        t = i / bays
        base = a3.lerp(b3, t)
        tip = base + Vector((rng.uniform(-0.03, 0.03), rng.uniform(-0.03, 0.03), RAIL_H)) \
            if lean else base + Vector((0, 0, RAIL_H))
        posts.append((base, tip))
    {"rope": _rail_rope, "pipe": _rail_pipe, "sheet": _rail_sheet, "net": _rail_net}[style](
        p, posts, rng)


def _timber_posts(p, posts, rng):
    for base, tip in posts:
        strut(p, base - Vector((0, 0, 0.08)), tip + Vector((0, 0, 0.06)), 0.13, 0.13, TIMBER)
        lashing(p, tip - Vector((0, 0, 0.08)), 0.075, turns=2)


def _rail_rope(p, posts, rng):
    _timber_posts(p, posts, rng)
    for (_, t0), (_, t1) in zip(posts, posts[1:]):
        rope(p, t0 - Vector((0, 0, 0.06)), t1 - Vector((0, 0, 0.06)), sag=rng.uniform(0.05, 0.12))
        rope(p, t0 - Vector((0, 0, 0.55)), t1 - Vector((0, 0, 0.55)), sag=rng.uniform(0.06, 0.14))


def _rail_pipe(p, posts, rng):
    for base, tip in posts:
        pole(p, base - Vector((0, 0, 0.05)), tip, 0.045, rng.choice((RUST, RUST_DEEP, HULLRUST)))
        p.cyl(base + Vector((0, 0, 0.02)), 0.07, 0.04, mat=DARK)
    for (_, t0), (_, t1) in zip(posts, posts[1:]):
        mid = t0.lerp(t1, rng.uniform(0.35, 0.65)) + Vector((0, 0, rng.uniform(-0.04, 0.02)))
        mat = rng.choice((RUST, RUST_PALE, YELLOW))
        pole(p, t0, mid, 0.04, mat)
        pole(p, mid, t1, 0.04, mat)
        rope(p, t0 - Vector((0, 0, 0.5)), t1 - Vector((0, 0, 0.5)), sag=0.08, r=0.018, mat=DARK)


def _rail_sheet(p, posts, rng):
    _timber_posts(p, posts, rng)
    for (b0, t0), (b1, t1) in zip(posts, posts[1:]):
        d = (b1 - b0)
        d.z = 0.0
        if d.length < 1e-4:
            continue
        mid = (b0 + b1) / 2
        h = rng.uniform(0.6, 0.85)
        obox(p, mid + Vector((0, 0, 0.12 + h / 2)), (0.03, d.length * 0.96, h), d,
             rng.choice((RUST, HULLRUST, RUST_PALE)), yaw=0.0,
             tilt=math.radians(rng.uniform(-3, 3)))
        strut(p, t0 - Vector((0, 0, 0.04)), t1 - Vector((0, 0, 0.04)), 0.08, 0.08, TIMBER)


def _rail_net(p, posts, rng):
    _timber_posts(p, posts, rng)
    for (b0, t0), (b1, t1) in zip(posts, posts[1:]):
        rope(p, t0 - Vector((0, 0, 0.05)), t1 - Vector((0, 0, 0.05)), sag=0.06)
        cells = max(2, int((b1.xy - b0.xy).length / 0.35))
        for i in range(cells + 1):
            u = i / cells
            top = t0.lerp(t1, u) - Vector((0, 0, 0.1))
            bot = b0.lerp(b1, min(1.0, u + 0.5 / cells)) + Vector((0, 0, 0.12))
            pole(p, top, bot, 0.012, ROPE, seg=4)
            bot2 = b0.lerp(b1, max(0.0, u - 0.5 / cells)) + Vector((0, 0, 0.12))
            pole(p, top, bot2, 0.012, ROPE, seg=4)


# ---------------------------------------------------------------------------
# Stairs
# ---------------------------------------------------------------------------

def stair(p, foot, d, width, rise, style, rng, rails=(True, True), rail_rise=None,
          rail_style=None):
    """A flight whose foot centre is `foot` (x, y, z), climbing along horizontal
    direction `d` by `rise` at whatever run the caller's slope dictates.

    The run is passed implicitly by `d`'s length. `rail_rise` stops the side
    rails at that height above the foot - a flight arriving through a floor
    opening is guarded by the floor edge past that point.
    """
    foot = Vector(foot)
    d = Vector((d[0], d[1], 0.0))
    run = d.length
    dn = d.normalized()
    s = dn.cross(UP).normalized()
    top = foot + d + Vector((0, 0, rise))
    n = max(3, round(rise / RISER))
    timber = style == "timber"
    for sx in (-1, 1):
        edge = s * (sx * (width / 2 - 0.08))
        a = foot + edge - Vector((0, 0, 0.16))
        b = top + edge - Vector((0, 0, 0.16))
        strut(p, a - dn * 0.1, b, 0.1 if timber else 0.08, 0.28, TIMBER if timber else RUST)
        if not timber:
            strut(p, a - dn * 0.1 + Vector((0, 0, -0.1)), b + Vector((0, 0, -0.1)), 0.02, 0.08,
                  RUST_DEEP)
    for i in range(n):
        t = (i + 0.5) / n
        c = foot + d * t + Vector((0, 0, rise * (i + 1) / n - 0.03 - rng.uniform(0.0, 0.015)))
        w = width - rng.uniform(0.0, 0.12)
        c += s * rng.uniform(-0.04, 0.04)
        if timber:
            mat = rng.choices((TIMBER, PLY, RUST), (0.82, 0.12, 0.06))[0]
        else:
            mat = rng.choices((RUST, HULLRUST, RUST_PALE, TIMBER), (0.35, 0.25, 0.2, 0.2))[0]
        obox(p, c, (w, run / n * 0.94, 0.06), dn, mat, yaw=math.radians(rng.uniform(-2.5, 2.5)))
        if i % 3 == 1 and not timber:
            p.rivets(c + s * (-w / 2 + 0.06), c + s * (w / 2 - 0.06), 2, radius=0.02,
                     height=0.012, mat=DARK)
    rr = rise if rail_rise is None else max(0.0, min(rise, rail_rise))
    k = rr / rise
    style_r = rail_style or ("rope" if timber else "pipe")
    for side, sx in zip(rails, (-1, 1)):
        if not side or k <= 0.0:
            continue
        edge = s * (sx * (width / 2 + 0.05))
        a = foot + edge
        b = foot + edge + d * k
        railing(p, (a.x, a.y), (b.x, b.y), a.z, style_r, rng, z_b=a.z + rr)


def ladder(p, foot, top_z, facing, rng, style="timber", width=None):
    """A straight vertical ladder whose rails stand on `foot` (x, y, z, the
    centre of the rail line) and rise GRAB above `top_z`, the height a climber
    steps off at. `facing` points from the ladder toward the climber.

    Straight and plumb on purpose, however scrappy the rungs: a ladder is a
    gameplay route, and whatever climbing logic comes later wants a line.
    Stand-off brackets reach back from the rails every few metres, to whatever
    the ladder is fixed to.
    """
    foot = Vector(foot)
    f = Vector((facing[0], facing[1], 0.0)).normalized()
    s = f.cross(UP)
    w = LADDER_W if width is None else width
    height = top_z - foot.z + GRAB
    timber = style == "timber"
    for sx in (-1, 1):
        base = foot + s * (sx * w / 2)
        top = base + UP * height
        if timber:
            strut(p, base, top, 0.08, 0.06, TIMBER)
        else:
            pole(p, base, top, 0.04, rng.choice((RUST, RUST_DEEP)), seg=8)
        z = 2.5
        while z < height - 0.5:
            strut(p, base + UP * z, base + UP * z - f * STANDOFF, 0.05, 0.05, DARK)
            z += rng.uniform(2.5, 3.5)
    z = RUNG_PITCH
    while z < top_z - foot.z + 0.05:
        c = foot + UP * z
        if timber:
            obox(p, c, (w + 0.1, 0.05, 0.05), f, rng.choices((TIMBER, PLY, RUST), (0.8, 0.12, 0.08))[0],
                 yaw=math.radians(rng.uniform(-2, 2)))
            if rng.random() < 0.3:
                for sx in (-1, 1):
                    lashing(p, c + s * (sx * w / 2), 0.05, turns=2)
        else:
            pole(p, c - s * (w / 2), c + s * (w / 2), 0.022, rng.choice((RUST, DARK, RUST_PALE)), seg=6)
        z += RUNG_PITCH


# ---------------------------------------------------------------------------
# Lights and props
# ---------------------------------------------------------------------------

def lantern(p, hook, rng, drop=0.35):
    """A caged lantern hanging off `hook` on a short chain of rope."""
    hook = Vector(hook)
    body = hook - Vector((0, 0, drop))
    rope(p, hook, body + Vector((0, 0, 0.16)), r=0.012, steps=1, mat=DARK)
    p.cyl(body, 0.09, 0.2, seg=6, mat=LAMP)
    for a in range(4):
        ang = math.radians(45 + 90 * a)
        off = Vector((math.cos(ang), math.sin(ang), 0.0)) * 0.11
        strut(p, body + off - Vector((0, 0, 0.12)), body + off + Vector((0, 0, 0.12)), 0.02, 0.02, DARK)
    p.cyl(body + Vector((0, 0, 0.14)), 0.13, 0.05, seg=6, mat=RUST_DEEP, radius_top=0.05)
    p.cyl(body - Vector((0, 0, 0.13)), 0.11, 0.03, seg=6, mat=RUST_DEEP)


def bracket_lantern(p, post_top, out, rng):
    """A bent arm off a post top with a lantern swinging from it."""
    post_top = Vector(post_top)
    out = Vector((out[0], out[1], 0.0)).normalized()
    elbow = post_top + Vector((0, 0, 0.25))
    tip = elbow + out * 0.45 + Vector((0, 0, -0.05))
    strut(p, post_top, elbow, 0.04, 0.04, DARK)
    strut(p, elbow, tip, 0.04, 0.04, DARK)
    lantern(p, tip, rng, drop=rng.uniform(0.25, 0.4))


def knee(p, top, foot, mat=TIMBER, w=0.12):
    """A raking prop from structure below up under a deck edge, lashed at the top."""
    strut(p, foot, top, w, w, mat)
    if mat == TIMBER:
        lashing(p, Vector(top) - Vector((0, 0, 0.1)), w * 0.6, turns=2)


# ---------------------------------------------------------------------------
# Library build - one collection per variation
# ---------------------------------------------------------------------------

def build():
    from _buildlib import collection, link_materials, parse_out, report, save, start
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    seed = 20260916

    def emit(name, fn):
        coll = collection("Coll_" + name)
        p = Part(mats)
        fn(p, random.Random(seed + len(name)))
        p.bevel(width=0.01, segments=1)
        p.finish("Mesh_" + name, coll)

    for style in DECK_STYLES:
        emit("ScrapDeck_" + style.capitalize(),
             lambda p, rng, s=style: deck(p, (-1.5, -2.5), (1.5, 2.5), 0.0, s, rng))
    for style in RAIL_STYLES:
        emit("ScrapRail_" + style.capitalize(),
             lambda p, rng, s=style: railing(p, (0.0, -2.0), (0.0, 2.0), 0.0, s, rng))
    for style in STAIR_STYLES:
        emit("ScrapStair_" + style.capitalize(),
             lambda p, rng, s=style: stair(p, (0.0, 0.0, 0.0), (0.0, 5.45), 1.4, 2.8, s, rng))
    for style in ("timber", "rust"):
        emit("ScrapLadder_" + style.capitalize(),
             lambda p, rng, s=style: ladder(p, (0.0, 0.0, 0.0), 4.0, (0.0, -1.0), rng, style=s))
    emit("ScrapLantern_Bracket",
         lambda p, rng: (strut(p, (0, 0, 0), (0, 0, 1.2), 0.1, 0.1, TIMBER),
                         bracket_lantern(p, (0, 0, 1.2), (1, 0), rng)))
    report()
    save(out)


if __name__ == "__main__":
    build()
