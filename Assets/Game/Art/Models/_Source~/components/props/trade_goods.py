"""components/props/trade_goods - what nomad traders carry between settlements.

The Sky Tribe live by trade, and a trading town should look like one: goods
piled where they were swung aboard, lashed for the next leg, half unpacked for
the last buyer. The library had containers - `supply_crate` and `fuel_barrel` -
but nothing a trader would actually sell out of them. This is that:

| Variation | Reads as |
|---|---|
| `SackPile`     | grain and spice sacks slumped against each other |
| `BaleStack`    | pressed fibre bales, roped in pairs |
| `RugRolls`     | rolled carpets in the tribe's sail colours, leaning on a rack |
| `CanisterRack` | brass and verdigris cans on a timber rack - oil, water, gas |
| `NetBundle`    | a cargo net round mixed goods, with the lifting ring on top |
| `ChestStack`   | banded timber chests, one open with cloth spilling out |
| `TradeScale`   | a hanging balance on a tripod, over a board of weights |

Every logical piece is its own object - each sack, each bale, each rug - so a
placement can pull one out of a pile. Origins sit on the floor at the pile's
centre, so a pile is placed by dropping its origin on the deck.

Builder functions take a `Part`, a centre and a seeded `random.Random`, so a
crane can hang a `net_bundle` from its hook without owning a second copy of
the geometry.

    blender --background --python trade_goods.py -- --out trade_goods.blend

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
    "Mat_Fabric_Canvas_Faded",    # 0 grubby sacking
    "Mat_Fabric_Canvas_Sand",     # 1 fresh sacking
    "Mat_Fabric_Wing_Beige",      # 2 fibre bales
    "Mat_Fabric_Rope_Hemp",       # 3 ties, straps, nets
    "Mat_Fabric_Sail_Red",        # 4 rug dye
    "Mat_Fabric_Tarp_Azure",      # 5 rug dye
    "Mat_Fabric_Sail_Orange",     # 6 rug dye
    "Mat_Wood_Timber_Silvered",   # 7 racks, chests
    "Mat_Wood_Ply_Worn",          # 8 chest panels
    "Mat_Metal_Brass_Tarnished",  # 9 cans, bands, scale pans
    "Mat_Metal_Copper_Oxide",     # 10 old cans
    "Mat_Metal_Rust_Deep",        # 11 hoops, hinges, weights
    "Mat_Fabric_Wing_Ochre",      # 12 bale variant, cloth
]
(SACK, SACK_NEW, BALE, ROPE, RED, AZURE, ORANGE, TIMBER, PLY, BRASS, COPPER,
 IRON, OCHRE) = range(13)

SACK_COLOURS = (SACK, SACK_NEW, OCHRE)
RUG_COLOURS = (RED, AZURE, ORANGE, SACK_NEW)


def pole(p, a, b, r, mat, seg=6):
    a, b = Vector(a), Vector(b)
    d = b - a
    if d.length < 1e-6:
        return []
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    return p.cyl((a + b) / 2, r, d.length, 'Z', seg=seg, mat=mat, rot=rot)


# ---------------------------------------------------------------------------
# One piece each
# ---------------------------------------------------------------------------

def sack(p, base, rng, size=1.0, lying=False):
    """A slumped sack tied at the neck. `base` is where it sits."""
    base = Vector(base)
    r = 0.26 * size * rng.uniform(0.9, 1.1)
    h = 0.62 * size * rng.uniform(0.85, 1.1)
    yaw = rng.uniform(0, math.tau)
    tilt = math.radians(rng.uniform(70, 85) if lying else rng.uniform(-8, 8))
    rot = Matrix.Rotation(yaw, 4, 'Z') @ Matrix.Rotation(tilt, 4, 'X')
    mat = rng.choice(SACK_COLOURS)
    # A lofted body: fat at the bottom where the grain settles, pinched at the neck.
    rings = []
    for w, k in ((0.0, 0.55), (0.08, 0.92), (0.35, 1.0), (0.62, 0.84), (0.8, 0.42), (0.88, 0.2)):
        prof = []
        for i in range(8):
            a = math.tau * i / 8
            bulge = k * r * (1.0 + 0.08 * math.sin(a * 2 + yaw))
            prof.append((bulge * math.cos(a), bulge * math.sin(a) * 0.82))
        rings.append((w * h, prof))
    faces = p.loft(rings, axis='Z', mat=mat)
    lift = Matrix.Translation(base + Vector((0, 0, r * 0.82 * (1.0 if lying else 0.0))))
    _transform(p, faces, lift @ rot)
    neck = lift @ rot @ Vector((0, 0, 0.84 * h))
    p.torus(neck, 0.07 * size, 0.022, maj_seg=8, min_seg=4, mat=ROPE)
    tuft = lift @ rot @ Vector((0, 0, 0.95 * h))
    p.cyl(tuft, 0.06 * size, 0.1 * size, seg=6, mat=mat, radius_top=0.1 * size)


def _transform(p, faces, m):
    verts = {v for f in faces for v in f.verts}
    for v in verts:
        v.co = m @ v.co


def bale(p, base, rng, yaw=0.0):
    base = Vector(base)
    size = Vector((1.1, 0.7, 0.55)) * rng.uniform(0.92, 1.05)
    rot = Matrix.Rotation(yaw + math.radians(rng.uniform(-4, 4)), 4, 'Z')
    center = base + Vector((0, 0, size.z / 2))
    p.box(center, size, rng.choice((BALE, OCHRE)), rot=rot)
    for dx in (-0.3, 0.3):
        m = Matrix.Translation(center) @ rot
        p.box(m @ Vector((dx, 0, 0)), (0.05, size.y + 0.03, size.z + 0.03), ROPE, rot=rot)
    # Tufts pushed out of the ends.
    for sx in (-1, 1):
        m = Matrix.Translation(center) @ rot
        p.box(m @ Vector((sx * (size.x / 2 + 0.02), 0, 0)), (0.05, size.y * 0.8, size.z * 0.7),
              BALE, rot=rot)


def rug(p, base, rng, lean_to=None):
    """A rolled rug standing on its end, leaning toward `lean_to` if given."""
    base = Vector(base)
    length = rng.uniform(1.4, 2.1)
    r = rng.uniform(0.11, 0.17)
    colour = rng.choice(RUG_COLOURS)
    if lean_to is None:
        d = Vector((0, 0, 1))
    else:
        d = (Vector(lean_to) - base).normalized()
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    center = base + d * (length / 2)
    p.cyl(center, r, length, 'Z', seg=10, mat=colour, rot=rot)
    for t in (0.12, 0.88):
        p.cyl(base + d * (length * t), r + 0.01, 0.08, 'Z', seg=10,
              mat=rng.choice([c for c in RUG_COLOURS if c != colour]), rot=rot)
    p.cyl(base + d * (length + 0.004), r * 0.6, 0.01, 'Z', seg=10, mat=IRON, rot=rot)
    p.torus(base + d * (length * 0.5), r + 0.012, 0.018, maj_seg=10, min_seg=4, mat=ROPE)


def chest(p, base, rng, yaw=0.0, open_lid=False):
    base = Vector(base)
    size = Vector((0.95, 0.55, 0.5)) * rng.uniform(0.85, 1.1)
    rot = Matrix.Rotation(yaw, 4, 'Z')
    m = Matrix.Translation(base) @ rot
    p.box(m @ Vector((0, 0, size.z * 0.4)), (size.x, size.y, size.z * 0.8), TIMBER, rot=rot)
    for dx in (-size.x * 0.32, size.x * 0.32):
        p.box(m @ Vector((dx, 0, size.z * 0.4)), (0.05, size.y + 0.02, size.z * 0.82), BRASS, rot=rot)
    if open_lid:
        hinge = Matrix.Translation(m @ Vector((0, size.y / 2, size.z * 0.8))) @ rot \
            @ Matrix.Rotation(math.radians(-100), 4, 'X')
        p.box(hinge @ Vector((0, -size.y / 2, 0.05)), (size.x, size.y, 0.1), PLY, rot=rot @ Matrix.Rotation(math.radians(-100), 4, 'X'))
        p.box(m @ Vector((0, 0, size.z * 0.78)), (size.x * 0.9, size.y * 0.85, 0.12),
              rng.choice((RED, AZURE, ORANGE)), rot=rot)
        p.box(m @ Vector((size.x * 0.2, -size.y / 2 - 0.06, size.z * 0.55)), (0.35, 0.1, 0.5),
              rng.choice((RED, AZURE, ORANGE)), rot=rot @ Matrix.Rotation(math.radians(20), 4, 'X'))
    else:
        p.box(m @ Vector((0, 0, size.z * 0.9)), (size.x + 0.02, size.y + 0.02, size.z * 0.2), PLY, rot=rot)
        p.box(m @ Vector((0, -size.y / 2 - 0.02, size.z * 0.72)), (0.1, 0.03, 0.14), IRON, rot=rot)


def can(p, base, rng):
    base = Vector(base)
    r, h = rng.uniform(0.11, 0.16), rng.uniform(0.3, 0.5)
    mat = rng.choice((BRASS, COPPER, BRASS, IRON))
    p.cyl(base + Vector((0, 0, h / 2)), r, h, seg=10, mat=mat)
    p.cyl(base + Vector((0, 0, h + 0.03)), r * 0.35, 0.06, seg=8, mat=IRON)
    p.torus(base + Vector((0, 0, h * 0.7)), r + 0.004, 0.012, maj_seg=10, min_seg=4, mat=IRON)


def net_bundle(p, center, rng, radius=0.6):
    """Mixed goods in a cargo net, gathered to a lifting ring at the top.

    `center` is the middle of the bundle; the ring sits `radius * 1.25` above it.
    """
    center = Vector(center)
    for _ in range(6):
        q = center + Vector((rng.uniform(-0.3, 0.3), rng.uniform(-0.3, 0.3), rng.uniform(-0.35, 0.1))) * radius * 1.4
        kind = rng.random()
        if kind < 0.5:
            sack(p, q - Vector((0, 0, 0.2)), rng, size=0.8, lying=True)
        elif kind < 0.8:
            p.box(q, Vector((0.5, 0.4, 0.35)) * rng.uniform(0.8, 1.1), rng.choice((TIMBER, PLY)),
                  rot=Matrix.Rotation(rng.uniform(0, math.tau), 4, 'Z'))
        else:
            can(p, q - Vector((0, 0, 0.2)), rng)
    ring = center + Vector((0, 0, radius * 1.25))
    p.torus(ring, 0.12, 0.03, maj_seg=10, min_seg=5, mat=IRON)
    for i in range(10):
        a = math.tau * i / 10
        belly = center + Vector((math.cos(a), math.sin(a), -0.15)) * radius
        bottom = center + Vector((math.cos(a) * 0.25, math.sin(a) * 0.25, -1.0)) * radius
        pole(p, ring, belly, 0.014, ROPE, seg=4)
        pole(p, belly, bottom, 0.014, ROPE, seg=4)
    for z, k in ((-0.15, 1.0), (-0.55, 0.75), (0.3, 0.72)):
        p.torus(center + Vector((0, 0, z * radius)), radius * k, 0.013, maj_seg=12, min_seg=3, mat=ROPE)


# ---------------------------------------------------------------------------
# Library build - piles made of separate objects
# ---------------------------------------------------------------------------

def build():
    from _buildlib import collection, link_materials, parse_out, report, save, start
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    rng = random.Random(20260917)

    def piece(coll, name, fn):
        p = Part(mats)
        fn(p)
        p.bevel(width=0.008, segments=1)
        return p.finish(name, coll)

    coll = collection("Coll_TradeGoods_SackPile")
    spots = [(0, 0, 0), (0.5, 0.1, 0), (-0.45, 0.2, 0), (0.1, 0.55, 0), (0.2, -0.5, 0),
             (0.15, 0.1, 0.45), (-0.2, 0.35, 0.42)]
    for i, s in enumerate(spots, start=1):
        piece(coll, "Mesh_TradeGoods_SackPile_Sack%02d" % i,
              lambda p, s=s: sack(p, s, rng, lying=s[2] > 0 or rng.random() < 0.3))

    coll = collection("Coll_TradeGoods_BaleStack")
    for i, (x, y, z, yaw) in enumerate(((-0.58, 0, 0, 0), (0.58, 0, 0, 0.05), (0, 0.05, 0.57, 1.57),
                                        (0.0, 0.9, 0, 1.52)), start=1):
        piece(coll, "Mesh_TradeGoods_BaleStack_Bale%02d" % i,
              lambda p, x=x, y=y, z=z, yaw=yaw: bale(p, (x, y, z), rng, yaw=yaw))

    coll = collection("Coll_TradeGoods_RugRolls")
    piece(coll, "Mesh_TradeGoods_RugRolls_Rack", lambda p: (
        [pole(p, (x, 0.45, 0), (x, 0.45, 1.3), 0.05, TIMBER) for x in (-0.9, 0.9)],
        pole(p, (-1.0, 0.45, 1.25), (1.0, 0.45, 1.25), 0.045, TIMBER)))
    for i in range(5):
        x = -0.8 + i * 0.4
        piece(coll, "Mesh_TradeGoods_RugRolls_Rug%02d" % (i + 1),
              lambda p, x=x: rug(p, (x + rng.uniform(-0.05, 0.05), -0.25, 0), rng, lean_to=(x, 0.5, 1.4)))

    coll = collection("Coll_TradeGoods_CanisterRack")
    piece(coll, "Mesh_TradeGoods_CanisterRack_Rack", lambda p: (
        [pole(p, (x, y, 0), (x, y, 1.2), 0.04, TIMBER) for x in (-0.8, 0.8) for y in (-0.25, 0.25)],
        [p.box((0, 0, z), (1.7, 0.6, 0.04), PLY) for z in (0.02, 0.62)]))
    k = 1
    for z in (0.04, 0.64):
        for x in (-0.6, -0.25, 0.1, 0.45):
            piece(coll, "Mesh_TradeGoods_CanisterRack_Can%02d" % k,
                  lambda p, x=x, z=z: can(p, (x + rng.uniform(-0.05, 0.05), rng.uniform(-0.08, 0.08), z), rng))
            k += 1

    coll = collection("Coll_TradeGoods_NetBundle")
    piece(coll, "Mesh_TradeGoods_NetBundle_Load", lambda p: net_bundle(p, (0, 0, 0.6), rng))

    coll = collection("Coll_TradeGoods_ChestStack")
    for i, (x, y, z, yaw, lid) in enumerate(((0, 0, 0, 0.1, False), (1.05, 0.1, 0, -0.2, True),
                                             (0.05, 0.02, 0.5, -0.12, False)), start=1):
        piece(coll, "Mesh_TradeGoods_ChestStack_Chest%02d" % i,
              lambda p, x=x, y=y, z=z, yaw=yaw, lid=lid: chest(p, (x, y, z), rng, yaw=yaw, open_lid=lid))

    coll = collection("Coll_TradeGoods_TradeScale")
    top = Vector((0, 0, 1.7))
    piece(coll, "Mesh_TradeGoods_TradeScale_Tripod", lambda p: (
        [pole(p, (math.cos(a) * 0.6, math.sin(a) * 0.6, 0), top, 0.035, TIMBER)
         for a in (0.3, 0.3 + math.tau / 3, 0.3 + 2 * math.tau / 3)],
        p.torus(top, 0.06, 0.02, maj_seg=8, min_seg=4, mat=ROPE)))
    piece(coll, "Mesh_TradeGoods_TradeScale_Balance", lambda p: (
        pole(p, top, top - Vector((0, 0, 0.25)), 0.012, IRON),
        pole(p, top - Vector((0.45, 0, 0.25)), top - Vector((-0.45, 0, 0.22)), 0.02, BRASS),
        [(pole(p, top - Vector((sx * 0.44, 0, 0.25)), top - Vector((sx * 0.44, 0, 0.7)), 0.008, ROPE),
          p.cyl(top - Vector((sx * 0.44, 0, 0.72)), 0.16, 0.03, seg=12, mat=BRASS, radius_top=0.12))
         for sx in (-1, 1)]))
    piece(coll, "Mesh_TradeGoods_TradeScale_WeightBoard", lambda p: (
        p.box((0.9, 0.2, 0.35), (0.7, 0.45, 0.7), TIMBER),
        [p.cyl((0.72 + i * 0.12, 0.2, 0.72 + 0.02 * i), 0.03 + 0.012 * i, 0.04 + 0.02 * i, seg=8, mat=IRON)
         for i in range(4)]))
    report()
    save(out)


if __name__ == "__main__":
    build()
