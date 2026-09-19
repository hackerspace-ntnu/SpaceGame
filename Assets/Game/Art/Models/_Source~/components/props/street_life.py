"""components/props/street_life - what makes a lane between shacks a street.

The sky city's lanes had houses and nothing living between them. A favela
street reads as one because of the layer people add after the walls: washing
strung across the gap, cables sagging from roof to roof, a plant in a paint tin
on every sill, a stall pushed against a wall, a stove somebody cooks on, a sign
painted by hand, colours that do not match because each panel came from
somewhere else. This kit is that layer.

| Builder | Reads as |
|---|---|
| `laundry_line` | a sagging rope with shirts, trousers and sheets pegged on it |
| `string_lights` | a cable of bare bulbs looped between two points |
| `cables` | a tangle of black lines sagging from one anchor to another |
| `plant` | `tin`, `tall`, `planter` or `hanging` - greenery in scavenged pots |
| `sign` | a hand-painted board, pastel over rust |
| `stall` | a counter with a slanted cloth awning and goods on it |
| `canopy` | a sloping cloth shade off a wall, on two brackets |
| `stool`, `crate_table` | where people sit |
| `stove` | a barrel stove with a pot on and a glow in the hatch |
| `hammock` | cloth slung between two points |
| `birdcage` | a domed cage hanging off a hook |
| `dish` | a scavenged antenna dish on a pole |
| `facade` | a street wall of mismatched painted and rusted plate, with a door, a lit window, a flower box and a pipe |

Every builder draws into a `Part` built with `MATS` from a seeded
`random.Random`, and none of them collides; placements add collision.
Library variations are built by running this file:

    blender --background --python street_life.py -- --out street_life.blend

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
    "Mat_Fabric_Sail_Red",          # 0 washing, awnings
    "Mat_Fabric_Tarp_Azure",        # 1
    "Mat_Fabric_Sail_Orange",       # 2
    "Mat_Fabric_Canvas_Sand",       # 3
    "Mat_Fabric_Flag_Bleached",     # 4
    "Mat_Fabric_Wing_Ochre",        # 5
    "Mat_Fabric_Rope_Hemp",         # 6 lines, pegs' string, hammock cords
    "Mat_Foliage_Leaf_Pale",        # 7 sunlit leaves
    "Mat_Foliage_Moss_Deep",        # 8 shaded leaves
    "Mat_Metal_Rust_Heavy",         # 9 tins, plate
    "Mat_Metal_Rust_Deep",          # 10 stoves, joints
    "Mat_Metal_HullRust_Orange",    # 11 salvaged plate
    "Mat_Metal_Steel_Dark",         # 12 brackets, cages
    "Mat_Plastic_Rubber_Black",     # 13 cables
    "Mat_Emissive_Amber",           # 14 bulbs, stove glow
    "Mat_Emissive_Cabin_Warm",      # 15 lit windows
    "Mat_Wood_Timber_Silvered",     # 16 counters, stools
    "Mat_Wood_Ply_Worn",            # 17 boards
    "Mat_Paint_Coral_Faded",        # 18 painted panels
    "Mat_Paint_Mint_Pastel",        # 19
    "Mat_Paint_Butter_Pastel",      # 20
    "Mat_Paint_Rose_Dusty",         # 21
    "Mat_Paint_Blue_Station",       # 22
    "Mat_Metal_Brass_Tarnished",    # 23 cans, fittings
    "Mat_Neutral_Black_Matte",      # 24 doorways, shadow
    "Mat_Fabric_Canvas_Faded",      # 25 curtains
]
(RED, AZURE, ORANGE, SAND, BLEACHED, OCHRE, ROPE, LEAF, MOSS, RUST, RUST_DEEP, HULLRUST,
 DARK, RUBBER, AMBER, WARM, TIMBER, PLY, CORAL, MINT, BUTTER, ROSE, BLUE, BRASS, BLACK,
 CURTAIN) = range(26)

CLOTH = (RED, AZURE, ORANGE, SAND, BLEACHED, OCHRE, CORAL, ROSE)
PAINT = (CORAL, MINT, BUTTER, ROSE, BLUE)
PLATE = (RUST, HULLRUST, RUST_DEEP)
UP = Vector((0.0, 0.0, 1.0))
PLANT_KINDS = ("tin", "tall", "planter", "hanging")


# ---------------------------------------------------------------------------
# Primitives
# ---------------------------------------------------------------------------

def frame(d):
    """Rotation whose local Y runs along horizontal direction `d`, Z up."""
    d = Vector((d[0], d[1], 0.0)).normalized()
    s = d.cross(UP).normalized()
    return Matrix((s, d, UP)).transposed().to_4x4()


def obox(p, center, size, d, mat, yaw=0.0, tilt=0.0, roll=0.0):
    """A box sized (across, along, up) against horizontal direction `d`.

    "Across" is `d x up`. For something facing `d` - a sign, a wall, a counter -
    that makes the size (width, depth, height).
    """
    rot = frame(d) @ Matrix.Rotation(yaw, 4, 'Z') @ Matrix.Rotation(tilt, 4, 'X') \
        @ Matrix.Rotation(roll, 4, 'Y')
    return p.box(center, size, mat, rot=rot)


def pole(p, a, b, r, mat, seg=6):
    a, b = Vector(a), Vector(b)
    d = b - a
    if d.length < 1e-6:
        return []
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    return p.cyl((a + b) / 2, r, d.length, 'Z', seg=seg, mat=mat, rot=rot)


def sag_points(a, b, sag, steps):
    a, b = Vector(a), Vector(b)
    pts = []
    for i in range(steps + 1):
        t = i / steps
        q = a.lerp(b, t)
        q.z -= sag * math.sin(math.pi * t)
        pts.append(q)
    return pts


def line(p, a, b, sag, r, mat, steps=6):
    pts = sag_points(a, b, sag, steps)
    for u, v in zip(pts, pts[1:]):
        pole(p, u, v, r, mat, seg=4)
    return pts


def point_on(pts, t):
    """A point a fraction t along a polyline."""
    lengths = [(v - u).length for u, v in zip(pts, pts[1:])]
    target = sum(lengths) * t
    for (u, v), n in zip(zip(pts, pts[1:]), lengths):
        if target <= n or n == lengths[-1]:
            return u.lerp(v, min(1.0, target / n if n else 0.0))
        target -= n
    return pts[-1]


# ---------------------------------------------------------------------------
# Lines strung between things
# ---------------------------------------------------------------------------

def laundry_line(p, a, b, rng, sag=None):
    a, b = Vector(a), Vector(b)
    length = (b - a).length
    sag = rng.uniform(0.12, 0.3) if sag is None else sag
    pts = line(p, a, b, sag, 0.012, ROPE, steps=8)
    d = Vector((b.x - a.x, b.y - a.y, 0.0))
    t = rng.uniform(0.05, 0.15)
    while t < 0.92:
        q = point_on(pts, t)
        colour = rng.choice(CLOTH)
        kind = rng.choices(("shirt", "trousers", "sheet", "small"), (0.35, 0.25, 0.2, 0.2))[0]
        sway = math.radians(rng.uniform(-8, 8))
        if kind == "shirt":
            w, h = 0.46, 0.55
            obox(p, q - Vector((0, 0, h / 2 + 0.02)), (0.02, w, h), d, colour, tilt=sway)
            for s in (-1, 1):
                obox(p, q + frame(d) @ Vector((0, s * (w / 2 + 0.1), -0.12)), (0.02, 0.24, 0.16), d, colour,
                     tilt=sway + s * 0.5)
            step = w + 0.12
        elif kind == "trousers":
            for s in (-1, 1):
                obox(p, q + frame(d) @ Vector((0, s * 0.1, -0.42)), (0.02, 0.17, 0.8), d, colour, tilt=sway)
            obox(p, q - Vector((0, 0, 0.06)), (0.02, 0.4, 0.1), d, colour)
            step = 0.5
        elif kind == "sheet":
            w, h = rng.uniform(0.8, 1.2), rng.uniform(0.9, 1.3)
            obox(p, q - Vector((0, 0, h / 2)), (0.015, w, h), d, colour, tilt=sway * 0.5)
            step = w + 0.08
        else:
            obox(p, q - Vector((0, 0, 0.14)), (0.02, 0.2, 0.26), d, colour, tilt=sway)
            step = 0.28
        for s in (-1, 1):                                   # pegs
            p.box(q + frame(d) @ Vector((0, s * min(step * 0.35, 0.3), 0.0)), (0.02, 0.02, 0.06), TIMBER)
        t += (step + rng.uniform(0.05, 0.3)) / max(length, 1e-3)


def string_lights(p, a, b, rng, sag=None):
    pts = line(p, a, b, rng.uniform(0.15, 0.35) if sag is None else sag, 0.008, RUBBER, steps=8)
    n = max(2, int((Vector(b) - Vector(a)).length / 0.55))
    for i in range(1, n):
        q = point_on(pts, i / n)
        p.cyl(q - Vector((0, 0, 0.05)), 0.012, 0.06, seg=4, mat=DARK)
        p.cyl(q - Vector((0, 0, 0.11)), 0.035, 0.07, seg=6, mat=rng.choice((AMBER, WARM)))


def cables(p, a, b, rng, count=None):
    a, b = Vector(a), Vector(b)
    for _ in range(count or rng.randint(2, 4)):
        j = Vector((rng.uniform(-0.08, 0.08), rng.uniform(-0.08, 0.08), rng.uniform(-0.05, 0.05)))
        line(p, a + j, b - j, rng.uniform(0.2, 0.6), rng.uniform(0.01, 0.018), RUBBER, steps=6)


def hammock(p, a, b, rng):
    a, b = Vector(a), Vector(b)
    pts = sag_points(a, b, rng.uniform(0.45, 0.65), 8)
    d = Vector((b.x - a.x, b.y - a.y, 0.0))
    colour = rng.choice(CLOTH)
    for u, v in zip(pts[1:-2], pts[2:-1]):
        mid = (u + v) / 2
        run = (v - u).length
        tilt = math.atan2(v.z - u.z, Vector((v.x - u.x, v.y - u.y)).length)
        obox(p, mid, (0.75, run + 0.02, 0.02), d, colour, tilt=tilt)
    for end, nxt in ((pts[0], pts[1]), (pts[-1], pts[-2])):
        for s in (-1, 1):
            pole(p, end, nxt + frame(d) @ Vector((s * 0.35, 0, 0)), 0.01, ROPE, seg=4)


# ---------------------------------------------------------------------------
# Things that stand or hang
# ---------------------------------------------------------------------------

def _foliage(p, center, rng, radius, count):
    center = Vector(center)
    for _ in range(count):
        q = center + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-0.4, 0.8))) * radius
        s = radius * rng.uniform(0.35, 0.7)
        p.cyl(q, s, s * rng.uniform(0.5, 0.9), seg=6, mat=rng.choice((LEAF, MOSS, LEAF)),
              radius_top=s * 0.3, rot=Matrix.Rotation(rng.uniform(-0.6, 0.6), 4, 'X')
              @ Matrix.Rotation(rng.uniform(-0.6, 0.6), 4, 'Y'))


def plant(p, base, rng, kind=None):
    """Greenery in a scavenged pot. `base` is where the pot stands (or, for
    `hanging`, the hook it hangs from)."""
    base = Vector(base)
    kind = kind or rng.choice(PLANT_KINDS)
    if kind == "tin":
        r = rng.uniform(0.1, 0.16)
        p.cyl(base + Vector((0, 0, r)), r, 2 * r, seg=8, mat=rng.choice((RUST, BRASS) + PAINT))
        _foliage(p, base + Vector((0, 0, 2 * r + 0.12)), rng, 0.18, 6)
    elif kind == "tall":
        p.cyl(base + Vector((0, 0, 0.2)), 0.2, 0.4, seg=8, mat=rng.choice(PLATE), radius_top=0.24)
        top = base + Vector((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1), rng.uniform(1.0, 1.5)))
        pole(p, base + Vector((0, 0, 0.35)), top, 0.02, MOSS)
        for k in range(3):
            _foliage(p, base.lerp(top, 0.5 + 0.25 * k) + Vector((0, 0, 0.1)), rng, 0.22, 4)
    elif kind == "planter":
        p.box(base + Vector((0, 0, 0.13)), (0.9, 0.3, 0.26), rng.choice((TIMBER, PLY, RUST)))
        for i in range(4):
            _foliage(p, base + Vector((-0.33 + i * 0.22, 0, 0.36)), rng, 0.12, 3)
    else:
        pot = base - Vector((0, 0, 0.55))
        for i in range(3):
            a = math.tau * i / 3
            pole(p, base, pot + Vector((math.cos(a) * 0.13, math.sin(a) * 0.13, 0.12)), 0.006, ROPE, seg=3)
        p.cyl(pot, 0.14, 0.2, seg=8, mat=rng.choice((RUST, BRASS) + PAINT), radius_top=0.17)
        _foliage(p, pot + Vector((0, 0, 0.15)), rng, 0.16, 5)
        for _ in range(3):
            start = pot + Vector((rng.uniform(-0.15, 0.15), rng.uniform(-0.15, 0.15), 0.0))
            pole(p, start, start + Vector((rng.uniform(-0.1, 0.1), rng.uniform(-0.1, 0.1),
                                           -rng.uniform(0.3, 0.7))), 0.015, LEAF, seg=4)


def sign(p, at, facing, rng, width=None):
    """A hand-painted board mounted proud of a wall that faces `facing`."""
    at = Vector(at)
    d = Vector((facing[0], facing[1], 0.0)).normalized()
    along = d.cross(UP)
    w = width or rng.uniform(0.7, 1.3)
    h = rng.uniform(0.35, 0.55)
    base_paint = rng.choice(PAINT)
    obox(p, at, (w + 0.08, 0.04, h + 0.08), d, rng.choice((TIMBER, RUST_DEEP)),
         roll=math.radians(rng.uniform(-3, 3)))
    obox(p, at + d * 0.03, (w, 0.02, h), d, base_paint)
    x = -w / 2 + 0.08
    while x < w / 2 - 0.12:
        gw = rng.uniform(0.06, 0.16)
        gh = rng.uniform(0.12, h - 0.14)
        obox(p, at + d * 0.045 + along * (x + gw / 2) + Vector((0, 0, rng.uniform(-0.05, 0.05))),
             (gw, 0.01, gh), d, rng.choice([c for c in PAINT + (BLACK, RED) if c != base_paint]))
        x += gw + rng.uniform(0.03, 0.08)


def stall(p, base, facing, rng):
    """A counter against a wall with a slanted cloth awning. `base` is the
    counter's front-centre on the deck; `facing` points at the customer."""
    base = Vector(base)
    d = Vector((facing[0], facing[1], 0.0)).normalized()
    along = d.cross(UP)
    width = rng.uniform(1.4, 2.0)
    top = base - d * 0.3 + Vector((0, 0, 0.95))
    obox(p, top - Vector((0, 0, 0.47)), (width, 0.6, 0.9), d, rng.choice((PLY, TIMBER, RUST)))
    obox(p, top + Vector((0, 0, 0.03)), (width + 0.1, 0.7, 0.05), d, TIMBER)
    back = base - d * 0.65
    for s in (-1, 1):
        pole(p, base + along * (s * width / 2) + d * 0.1, base + along * (s * width / 2) + d * 0.1 + UP * 2.15,
             0.035, TIMBER)
        pole(p, back + along * (s * width / 2), back + along * (s * width / 2) + UP * 2.55, 0.035, TIMBER)
    colour = rng.choice(CLOTH)
    slope = math.atan2(0.4, 0.95)
    mid = (base + d * 0.1 + back) / 2 + UP * 2.4
    obox(p, mid, (width + 0.3, 1.1, 0.025), d, colour, tilt=-slope)
    for i in range(int(width / 0.3)):
        obox(p, base + d * 0.22 + along * (-width / 2 + 0.15 + i * 0.3) + UP * 2.05,
             (0.26, 0.02, 0.18), d, colour, roll=math.radians(rng.uniform(-6, 6)))
    for _ in range(rng.randint(3, 6)):
        q = top + along * rng.uniform(-width / 2 + 0.2, width / 2 - 0.2) + d * rng.uniform(-0.2, 0.2)
        if rng.random() < 0.5:
            p.cyl(q + Vector((0, 0, 0.12)), 0.07, 0.2, seg=8, mat=rng.choice((BRASS, RUST, MINT, CORAL)))
        else:
            obox(p, q + Vector((0, 0, 0.08)), (0.3, 0.22, 0.12), d, rng.choice(CLOTH),
                 yaw=rng.uniform(-0.4, 0.4))


def canopy(p, anchor, facing, width, depth, rng):
    """A cloth canopy off a wall: `anchor` is the middle of its top edge on the
    wall, `facing` points out from the wall. It slopes down and away, on two
    brackets, with a scalloped valance along the front."""
    anchor = Vector(anchor)
    d = Vector((facing[0], facing[1], 0.0)).normalized()
    along = d.cross(UP)
    drop = rng.uniform(0.25, 0.45)
    slope = math.atan2(drop, depth)
    colour = rng.choice(CLOTH)
    mid = anchor + d * (depth / 2) - UP * (drop / 2)
    obox(p, mid, (width, math.hypot(depth, drop), 0.025), d, colour, tilt=-slope)
    front = anchor + d * depth - UP * drop
    for i in range(max(2, int(width / 0.28))):
        obox(p, front + along * (-width / 2 + 0.14 + i * 0.28) - UP * 0.08, (0.24, 0.02, 0.16), d,
             colour, roll=math.radians(rng.uniform(-8, 8)))
    for s in (-1, 1):
        pole(p, anchor + along * (s * width / 2) - UP * 0.5, front + along * (s * width / 2), 0.02, DARK)


def stool(p, base, rng):
    base = Vector(base)
    h = rng.uniform(0.42, 0.5)
    p.cyl(base + Vector((0, 0, h)), 0.18, 0.05, seg=8, mat=rng.choice((TIMBER, RUST, PLY)))
    for i in range(3):
        a = math.tau * i / 3 + rng.uniform(0, 1)
        pole(p, base + Vector((math.cos(a) * 0.15, math.sin(a) * 0.15, 0)), base + Vector((0, 0, h)), 0.02, DARK)


def crate_table(p, base, rng):
    base = Vector(base)
    yaw = rng.uniform(0, math.pi)
    p.box(base + Vector((0, 0, 0.32)), (0.6, 0.6, 0.64), rng.choice((TIMBER, PLY)),
          rot=Matrix.Rotation(yaw, 4, 'Z'))
    p.box(base + Vector((0, 0, 0.66)), (0.85, 0.7, 0.04), rng.choice((PLY, RUST) + PAINT),
          rot=Matrix.Rotation(yaw + 0.1, 4, 'Z'))
    for _ in range(rng.randint(1, 3)):
        p.cyl(base + Vector((rng.uniform(-0.25, 0.25), rng.uniform(-0.2, 0.2), 0.73)), 0.04, 0.1, seg=6,
              mat=rng.choice((BRASS, BLUE, CORAL)))


def stove(p, base, rng):
    base = Vector(base)
    p.cyl(base + Vector((0, 0, 0.42)), 0.3, 0.84, seg=12, mat=RUST_DEEP)
    p.cyl(base + Vector((0, 0, 0.86)), 0.33, 0.04, seg=12, mat=DARK)
    p.box(base + Vector((0.29, 0, 0.3)), (0.04, 0.22, 0.16), AMBER)
    p.cyl(base + Vector((0.05, 0.02, 0.98)), 0.17, 0.2, seg=10, mat=rng.choice((BRASS, DARK)))
    flue_top = base + Vector((-0.2, 0, 2.2))
    pole(p, base + Vector((-0.2, 0, 0.8)), flue_top, 0.06, RUST)
    p.cyl(flue_top + Vector((0, 0, 0.08)), 0.12, 0.05, seg=8, mat=DARK)


def birdcage(p, hook, rng):
    hook = Vector(hook)
    top = hook - Vector((0, 0, 0.25))
    pole(p, hook, top, 0.008, DARK, seg=3)
    r, h = 0.16, 0.34
    for i in range(8):
        a = math.tau * i / 8
        rim = top + Vector((math.cos(a) * r, math.sin(a) * r, -0.1))
        pole(p, top, rim, 0.006, BRASS, seg=3)
        pole(p, rim, rim - Vector((0, 0, h)), 0.006, BRASS, seg=3)
    p.cyl(top - Vector((0, 0, 0.1 + h)), r + 0.01, 0.02, seg=10, mat=BRASS)
    p.box(top - Vector((0, 0, 0.3)), (0.07, 0.12, 0.08), rng.choice((BUTTER, CORAL, AZURE)))


def dish(p, base, rng, height=None):
    base = Vector(base)
    h = height or rng.uniform(1.0, 1.6)
    top = base + Vector((0, 0, h))
    pole(p, base, top, 0.03, DARK)
    aim = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(0.4, 1.0))).normalized()
    rot = aim.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    p.cyl(top + aim * 0.1, 0.34, 0.08, 'Z', seg=10, mat=rng.choice((BLEACHED, BLUE, RUST)), radius_top=0.05,
          rot=rot)
    pole(p, top + aim * 0.1, top + aim * 0.4, 0.012, DARK)


# ---------------------------------------------------------------------------
# A street wall
# ---------------------------------------------------------------------------

def facade(p, base, along, width, height, rng, door=True, window=True, thick=0.25, door_side=None):
    """A wall of mismatched plate with a street face.

    `base` is the bottom-centre of the STREET face, `along` runs the wall's
    length; the street normal is `along x up`, and the wall's thickness goes
    away from the street. Everything proud of the wall - sign, flower box,
    pipe - stands on the street side. `door_side` (-1 or +1 along `along`) puts
    the door on that half and the window on the other.
    """
    base = Vector(base)
    along = Vector((along[0], along[1], 0.0)).normalized()
    n = along.cross(UP)                          # street normal
    back = -n
    # Plates in columns, each column cut into two or three at random heights.
    x = -width / 2
    while x < width / 2 - 0.05:
        cw = min(rng.uniform(0.6, 1.3), width / 2 - x)
        z = 0.0
        while z < height - 0.05:
            ch = min(rng.uniform(0.7, 1.6), height - z)
            mat = rng.choices((PLATE + PAINT), (3, 3, 1, 1.4, 1.4, 1.4, 1.4, 1.4))[0]
            depth = thick - rng.uniform(0.0, 0.04)
            c = base + along * (x + cw / 2) + back * (depth / 2) + UP * (z + ch / 2)
            obox(p, c, (cw - 0.015, depth, ch - 0.015), n, mat)
            z += ch
        x += cw
    # Seam straps across the joints.
    for _ in range(rng.randint(2, 4)):
        zz = rng.uniform(0.6, height - 0.3)
        obox(p, base + UP * zz + n * 0.01, (width, 0.03, 0.06), n, DARK)
    # Door on one half, window on the other.
    side = door_side if door_side is not None else rng.choice((-1, 1))
    if door:
        dc = base + along * (side * rng.uniform(0.2, max(0.21, width / 2 - 0.7)))
        obox(p, dc + UP * 1.0 + n * 0.005, (0.9, 0.03, 2.0), n, BLACK)
        obox(p, dc + UP * 1.05 + n * 0.02, (1.02, 0.04, 2.1), n, rng.choice((TIMBER, RUST_DEEP)))
        for i in range(4):
            obox(p, dc + along * (-0.33 + i * 0.22) + UP * 1.0 + n * 0.05, (0.2, 0.02, 1.9), n, CURTAIN,
                 roll=math.radians(rng.uniform(-3, 3)))
    if window and width > 2.4:
        wc = base + along * (-side * rng.uniform(0.5, width / 2 - 0.6)) + UP * 1.6
        obox(p, wc + n * 0.005, (0.75, 0.03, 0.6), n, WARM)
        obox(p, wc + n * 0.02, (0.85, 0.04, 0.08), n, DARK)
        for s in (-1, 1):
            obox(p, wc + along * (s * 0.62) + n * 0.03, (0.42, 0.03, 0.66), n, rng.choice(PAINT),
                 yaw=math.radians(s * rng.uniform(10, 40)))
        obox(p, wc - UP * 0.4 + n * 0.14, (0.8, 0.25, 0.14), n, rng.choice((TIMBER, RUST)))
        for i in range(3):
            _foliage(p, wc - UP * 0.25 + n * 0.14 + along * (-0.25 + i * 0.25), rng, 0.1, 3)
    if rng.random() < 0.8:
        sign(p, base + along * rng.uniform(-width / 2 + 0.6, width / 2 - 0.6) + UP * rng.uniform(2.3, height - 0.3)
             + n * 0.03, n, rng)
    px = rng.choice((-1, 1)) * (width / 2 - 0.2)
    pole(p, base + along * px + n * 0.08, base + along * px + n * 0.08 + UP * height, 0.05, rng.choice((RUST, DARK)))


# ---------------------------------------------------------------------------
# Library build - one collection per variation
# ---------------------------------------------------------------------------

def build():
    from _buildlib import collection, link_materials, parse_out, report, save, start
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    seed = 20260917

    def emit(name, fn):
        coll = collection("Coll_StreetLife_" + name)
        p = Part(mats)
        fn(p, random.Random(seed + len(name) * 7))
        p.bevel(width=0.006, segments=1)
        p.finish("Mesh_StreetLife_" + name, coll)

    emit("LaundryLine", lambda p, r: (pole(p, (0, -2.5, 0), (0, -2.5, 2.6), 0.04, TIMBER),
                                       pole(p, (0, 2.5, 0), (0, 2.5, 2.6), 0.04, TIMBER),
                                       laundry_line(p, (0, -2.5, 2.5), (0, 2.5, 2.5), r)))
    emit("StringLights", lambda p, r: string_lights(p, (0, -2.5, 2.8), (0, 2.5, 2.6), r))
    emit("Cables", lambda p, r: cables(p, (0, -2.5, 3.0), (0, 2.5, 2.4), r))
    emit("Hammock", lambda p, r: hammock(p, (0, -1.4, 1.2), (0, 1.4, 1.25), r))
    for kind in PLANT_KINDS:
        emit("Plant" + kind.capitalize(), lambda p, r, k=kind: plant(p, (0, 0, 1.2 if k == "hanging" else 0.0), r, k))
    emit("Sign", lambda p, r: sign(p, (0, 0, 1.0), (-1, 0), r))
    emit("Stall", lambda p, r: stall(p, (0, 0, 0), (-1, 0), r))
    emit("Canopy", lambda p, r: canopy(p, (0, 0, 2.4), (-1, 0), 1.6, 0.9, r))
    emit("Stool", lambda p, r: stool(p, (0, 0, 0), r))
    emit("CrateTable", lambda p, r: crate_table(p, (0, 0, 0), r))
    emit("Stove", lambda p, r: stove(p, (0, 0, 0), r))
    emit("Birdcage", lambda p, r: birdcage(p, (0, 0, 1.2), r))
    emit("Dish", lambda p, r: dish(p, (0, 0, 0), r))
    emit("Facade", lambda p, r: facade(p, (0, 0, 0), (0, 1), 4.5, 3.4, r))
    report()
    save(out)


if __name__ == "__main__":
    build()
