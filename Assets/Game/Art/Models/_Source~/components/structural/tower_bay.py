"""components/structural/tower_bay — one storey of a clad industrial slab tower.

A 75 m refinery tower is not a mesh, it is a stack. This is the unit that gets
stacked: a 14 x 12 x 9 m clad storey whose only job is to read as *one floor of
something enormous* from 200 m away and as riveted plating from 3 m away. Six of
these stacked make the white spine; the variations decide what the silhouette
does on the way up, because a stack of six identical bays reads as a texture
error rather than a building.

Authored with the origin at the bottom centre — the face it sits on — so an
assembly stacks them by adding H to Z and nothing else.

The base envelope is 14.0 (X) x 12.0 (Y) x 9.0 (Z). `Shoulder` and `Crown`
deliberately break it: a setback and a tapered machine deck are the two places
where a slab tower stops being a slab, and clamping them to the base box would
throw away the only silhouette events the stack has.

    blender --background --python tower_bay.py -- --out tower_bay.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

from mathutils import Matrix  # noqa: E402

MATS = [
    "Mat_Paint_White_Arctic",    # 0 the cladding — the tower's whole read
    "Mat_Paint_Safety_Orange",   # 1 spines, buttresses, hazard modules
    "Mat_Metal_Steel_Dark",      # 2 floor bands, frames, fittings
    "Mat_Metal_Steel_Worn",      # 3 bare structure where cladding stops
    "Mat_Neutral_Black_Matte",   # 4 window reveals, recesses, shadow gaps
    "Mat_Neutral_Slate_Dark",    # 5 contrast panels
    "Mat_Glass_Canopy_Tinted",   # 6 glazing
    "Mat_Metal_Rust_Heavy",      # 7 streaks and weld-on repairs
    "Mat_Emissive_Amber",        # 8 obstruction lights
    "Mat_Paint_Warn_Red",        # 9 stencils and danger bands
]
WHITE, ORANGE, DARK, STEEL, BLACK, SLATE, GLASS, RUST, AMBER, RED = range(10)

W, D, H = 14.0, 12.0, 9.0            # width (X), depth (Y), storey height (Z)


# ---------------------------------------------------------------------------
# Shared cladding language
# ---------------------------------------------------------------------------

def core(p, w=W, d=D, h=H, z0=0.0, mat=WHITE):
    """The solid storey box plus its four corner pilasters.

    Solid rather than hollow: nothing ever sees inside a tower bay, and a solid
    box is a third of the triangles with no interior faces to clean up. The
    pilasters stand 0.30 m proud, which is what stops a 14 m blank face from
    reading as a single untextured plane at distance.
    """
    p.box((0, 0, z0 + h / 2), (w, d, h), mat)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * (w / 2 - 0.35), sy * (d / 2 - 0.35), z0 + h / 2),
                  (1.30, 1.30, h), mat)


def seams(p, w=W, d=D, h=H, z0=0.0, count=5, mat=WHITE, margin=0.45):
    """Vertical panel joints down all four faces.

    These are the cheapest possible statement of scale: they tell the eye how
    wide a fabricated plate is, and everything else on the tower gets measured
    against that. Without them the bay could be 4 m or 40 m tall.
    """
    lo, hi = z0 + margin, z0 + h - margin
    for sy in (-1, 1):
        for i in range(count):
            x = -w / 2 + w * (i + 1) / (count + 1)
            p.box((x, sy * (d / 2 + 0.06), (lo + hi) / 2),
                  (0.22, 0.12, hi - lo), mat)
    for sx in (-1, 1):
        for i in range(count - 2):
            y = -d / 2 + d * (i + 1) / (count - 1)
            p.box((sx * (w / 2 + 0.06), y, (lo + hi) / 2),
                  (0.12, 0.22, hi - lo), mat)


def band(p, z, w=W, d=D, thickness=0.50, mat=DARK, proud=0.16):
    """A floor-line band right round the bay.

    Two bays meeting at a shared band read as one building with storeys. Two
    bays meeting at a bare joint read as two boxes touching.
    """
    for sy in (-1, 1):
        p.box((0, sy * (d / 2 + proud / 2), z), (w + 0.9, proud, thickness), mat)
    for sx in (-1, 1):
        p.box((sx * (w / 2 + proud / 2), 0, z), (proud, d + 0.9, thickness), mat)


def recess(p, face, u, z, uw, zh, depth=0.22, w=W, d=D,
           back=BLACK, fill=None):
    """A shallow inset panel on one face, optionally glazed.

    `face` is one of '+Y', '-Y', '+X', '-X'; `u` runs along the face. The recess
    is a dark backing box sunk into the cladding with an optional lighter fill
    sitting just inside it — enough to read as a real opening rather than a
    painted rectangle, for two boxes.
    """
    if face in ('+Y', '-Y'):
        sy = 1 if face == '+Y' else -1
        y = sy * (d / 2 - depth / 2)
        p.box((u, y, z), (uw, depth, zh), back)
        if fill is not None:
            p.box((u, sy * (d / 2 - depth * 0.35), z),
                  (uw - 0.35, depth * 0.3, zh - 0.35), fill)
    else:
        sx = 1 if face == '+X' else -1
        x = sx * (w / 2 - depth / 2)
        p.box((x, u, z), (depth, uw, zh), back)
        if fill is not None:
            p.box((sx * (w / 2 - depth * 0.35), u, z),
                  (depth * 0.3, uw - 0.35, zh - 0.35), fill)


def conduit(p, face, u, z0, z1, w=W, d=D, radius=0.16, mat=STEEL, count=3):
    """A bundle of service pipes climbing one face, with its clamp collars."""
    for i in range(count):
        off = (i - (count - 1) / 2.0) * radius * 2.6
        if face in ('+Y', '-Y'):
            sy = 1 if face == '+Y' else -1
            c = (u + off, sy * (d / 2 + radius + 0.08), (z0 + z1) / 2)
        else:
            sx = 1 if face == '+X' else -1
            c = (sx * (w / 2 + radius + 0.08), u + off, (z0 + z1) / 2)
        p.cyl(c, radius, z1 - z0, 'Z', seg=8, mat=mat)
    for k in range(3):
        z = z0 + (z1 - z0) * (k + 0.5) / 3
        span = radius * 2.6 * count
        if face in ('+Y', '-Y'):
            sy = 1 if face == '+Y' else -1
            p.box((u, sy * (d / 2 + radius * 0.6), z),
                  (span, radius * 1.6, 0.22), DARK)
        else:
            sx = 1 if face == '+X' else -1
            p.box((sx * (w / 2 + radius * 0.6), u, z),
                  (radius * 1.6, span, 0.22), DARK)


def stencil(p, face, u, z, w=W, d=D, size=1.5, mat=RED):
    """A painted marking — hazard roundel, level number, lifting point.

    Flat-ish geometry rather than a texture, because this library ships
    untextured meshes into Unity and the paint has to be in the mesh.
    """
    t = 0.05
    if face in ('+Y', '-Y'):
        sy = 1 if face == '+Y' else -1
        p.box((u, sy * (d / 2 + t / 2), z), (size, t, size * 0.62), mat)
    else:
        sx = 1 if face == '+X' else -1
        p.box((sx * (w / 2 + t / 2), u, z), (t, size, size * 0.62), mat)


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def plain(coll, mats):
    """The workhorse. Blank clad storey, panel seams, one service riser.

    Four of the six stacked bays are this one, so it carries the least incident
    and the most careful proportions.
    """
    p = Part(mats)
    core(p)
    seams(p)
    band(p, 0.36)
    band(p, H - 0.36)
    # A single shallow inspection panel per long face keeps the blankness
    # deliberate rather than unfinished.
    recess(p, '-Y', -3.4, 4.6, 2.2, 2.8, fill=SLATE)
    recess(p, '+Y', 3.4, 4.6, 2.2, 2.8, fill=SLATE)
    conduit(p, '+X', -3.2, 0.7, H - 0.7)
    stencil(p, '-Y', 4.6, 6.4, size=1.4)
    p.bevel(width=0.05, segments=1)
    return p.finish("Mesh_TowerBay_Plain", coll)


def windowed(coll, mats):
    """Occupied storey: a continuous glazed strip plus punched portholes.

    The glazing runs the full face rather than sitting as isolated windows,
    because a control level on a structure this size is a band you can see
    lit from the valley floor.
    """
    p = Part(mats)
    core(p)
    seams(p, count=4)
    band(p, 0.36)
    band(p, H - 0.36)
    for face, sign in (('-Y', -1), ('+Y', 1)):
        recess(p, face, 0.0, 5.5, W - 3.6, 2.0, depth=0.34, fill=GLASS)
        # Mullions break the strip into panes — without them a 10 m sheet of
        # glass reads as a black hole punched in the tower.
        for i in range(5):
            x = -W / 2 + W * (i + 1) / 6
            p.box((x, sign * (D / 2 - 0.10), 5.5), (0.20, 0.30, 2.1), DARK)
    for face in ('+X', '-X'):
        for i, y in enumerate((-3.0, 0.0, 3.0)):
            recess(p, face, y, 5.5, 1.7, 1.7, depth=0.30, fill=GLASS)
    # Sunshade hoods over the front strip — the detail that says this glass is
    # on a working structure, not an office block.
    for i in range(4):
        x = -W / 2 + W * (i + 1) / 5
        p.box((x, -(D / 2 + 0.42), 6.75), (W / 6.5, 0.95, 0.14), STEEL,
              rot=Matrix.Rotation(math.radians(-12), 4, 'X'))
    conduit(p, '+X', 4.2, 0.7, H - 0.7, count=2)
    stencil(p, '-Y', -5.2, 2.2, size=1.2, mat=RED)
    p.bevel(width=0.05, segments=1)
    return p.finish("Mesh_TowerBay_Windowed", coll)


def ribbed(coll, mats):
    """Structural storey: external ribs, exposed bare steel, heavy plating.

    Where the cladding gives way to what is holding the tower up. Used at the
    bottom of the white stack, so the eye reads load being gathered downward.
    """
    p = Part(mats)
    core(p)
    band(p, 0.36, thickness=0.70)
    band(p, H - 0.36, thickness=0.70)
    # Pilaster ribs instead of flush seams — deeper, fewer, structural.
    for sy in (-1, 1):
        for i in range(4):
            x = -W / 2 + W * (i + 1) / 5
            p.box((x, sy * (D / 2 + 0.22), H / 2), (0.85, 0.44, H - 1.4), WHITE)
            p.box((x, sy * (D / 2 + 0.30), H / 2), (0.30, 0.60, H - 2.4), STEEL)
    for sx in (-1, 1):
        for y in (-3.4, 0.0, 3.4):
            p.box((sx * (W / 2 + 0.22), y, H / 2), (0.44, 0.85, H - 1.4), WHITE)
    # Diagonal wind bracing let into the -Y face.
    for s in (-1, 1):
        p.box((0, -(D / 2 + 0.30), H / 2), (0.34, 0.34, H * 1.28), STEEL,
              rot=Matrix.Rotation(math.radians(s * 38), 4, 'Y'))
    conduit(p, '+X', 0.0, 0.7, H - 0.7, count=4, radius=0.20)
    conduit(p, '-Y', -5.0, 0.7, H - 0.7, count=2, radius=0.13, mat=DARK)
    stencil(p, '+Y', 0.0, 4.4, size=1.8, mat=RED)
    p.bevel(width=0.05, segments=1)
    return p.finish("Mesh_TowerBay_Ribbed", coll)


def buttressed(coll, mats):
    """Storey carrying the orange cantilever spine down its front face.

    The spine is the single strongest colour event on the tower, so it is
    modelled as a real load path — a tapered box with a shoe at the bottom and
    a haunch at the top — rather than an orange stripe.
    """
    p = Part(mats)
    core(p)
    seams(p, count=3)
    band(p, 0.36)
    band(p, H - 0.36)
    # The spine: a tapered orange box standing off the face on two shoulders.
    p.loft([(-(D / 2 + 0.15), [(-2.3, 0.4), (2.3, 0.4), (2.0, H - 0.4),
                               (-2.0, H - 0.4)]),
            (-(D / 2 + 1.55), [(-1.9, 1.1), (1.9, 1.1), (1.6, H - 1.0),
                               (-1.6, H - 1.0)])],
           axis='Y', mat=ORANGE)
    for s in (-1, 1):
        p.box((s * 2.05, -(D / 2 + 0.75), 1.1), (0.5, 1.8, 1.3), DARK)
        p.box((s * 1.75, -(D / 2 + 0.75), H - 1.0), (0.5, 1.8, 1.1), DARK)
    # Bolt flanges up the spine — the read that it is fabricated in sections.
    for k in range(3):
        z = 1.8 + k * (H - 3.4) / 2.0
        p.box((0, -(D / 2 + 0.85), z), (4.4, 1.7, 0.30), ORANGE)
        p.rivets((-1.9, -(D / 2 + 1.60), z), (1.9, -(D / 2 + 1.60), z),
                 8, radius=0.09, height=0.10, axis='Y', mat=DARK)
    recess(p, '+Y', 0.0, 5.0, 5.5, 2.4, fill=SLATE)
    conduit(p, '-X', 2.0, 0.7, H - 0.7, count=3)
    stencil(p, '-X', -3.6, 6.0, size=1.5)
    p.bevel(width=0.05, segments=1)
    return p.finish("Mesh_TowerBay_Buttressed", coll)


def shoulder(coll, mats):
    """The setback storey — where the tower steps out over a lower roof.

    A slab tower is boring in silhouette until something interrupts it once.
    This is that interruption: a 5 m corbel out to +X carrying a plant deck,
    with the underside left as bare structure because that is what you see from
    below and it is where the weight visibly goes.
    """
    p = Part(mats)
    core(p)
    seams(p, count=4)
    band(p, 0.36)
    band(p, H - 0.36)

    ow = 5.6                                     # how far the corbel reaches
    ox = W / 2 + ow / 2
    p.box((ox, 0.6, H - 2.6), (ow, D - 1.2, 5.2), WHITE)
    p.box((ox, 0.6, H - 5.35), (ow + 0.5, D - 0.7, 0.5), DARK)   # its floor slab
    # Raking struts under the corbel, landing on the tower face.
    for y in (-3.6, 0.6, 4.8):
        p.box((W / 2 + ow * 0.42, y, H - 7.0), (ow * 1.35, 0.45, 0.55), STEEL,
              rot=Matrix.Rotation(math.radians(34), 4, 'Y'))
    p.box((W / 2 + 0.25, 0.6, H - 8.1), (0.5, D - 0.7, 2.0), STEEL)
    # The corbel's own cladding language, at the smaller module it deserves.
    for i in range(3):
        y = -3.8 + i * 3.8
        p.box((ox + ow / 2 + 0.05, y, H - 2.6), (0.14, 1.1, 4.6), WHITE)
    p.box((ox, 0.6, H + 0.2), (ow + 0.4, D - 0.9, 0.44), DARK)   # parapet cap
    for i in range(4):                                            # roof plant
        p.box((ox - 1.6 + i * 1.1, -3.4 + (i % 2) * 6.6, H + 0.9),
              (0.9, 1.2, 1.0), SLATE)
    recess(p, '-Y', -3.8, 5.2, 3.0, 2.6, fill=GLASS)
    stencil(p, '-Y', 3.2, 2.0, size=1.4)
    p.bevel(width=0.05, segments=1)
    return p.finish("Mesh_TowerBay_Shoulder", coll)


def crown(coll, mats):
    """The top of the stack: tapered machine deck, stacks, obstruction lights.

    Shorter than a standard bay (6.5 m) and tapered, so the tower closes rather
    than being cut off. Everything above this belongs to the derrick.
    """
    ch = 6.5
    p = Part(mats)
    # A taper, not a box — the whole point of the crown.
    p.loft([(0.0, [(-W / 2, -D / 2), (W / 2, -D / 2), (W / 2, D / 2),
                   (-W / 2, D / 2)]),
            (ch - 1.6, [(-W / 2, -D / 2), (W / 2, -D / 2), (W / 2, D / 2),
                        (-W / 2, D / 2)]),
            (ch, [(-W / 2 + 1.5, -D / 2 + 1.5), (W / 2 - 1.5, -D / 2 + 1.5),
                  (W / 2 - 1.5, D / 2 - 1.5), (-W / 2 + 1.5, D / 2 - 1.5)])],
           axis='Z', mat=WHITE)
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * (W / 2 - 0.35), sy * (D / 2 - 0.35), (ch - 1.6) / 2),
                  (1.30, 1.30, ch - 1.6), WHITE)
    band(p, 0.36)
    band(p, ch - 1.9, thickness=0.62)
    # Machine deck on the tapered top, with a rail kerb round it.
    p.box((0, 0, ch + 0.12), (W - 3.4, D - 3.4, 0.34), DARK)
    for sx in (-1, 1):
        p.box((sx * (W / 2 - 1.75), 0, ch + 0.55), (0.22, D - 3.4, 0.55), STEEL)
    for sy in (-1, 1):
        p.box((0, sy * (D / 2 - 1.75), ch + 0.55), (W - 3.4, 0.22, 0.55), STEEL)
    # Exhaust stacks and a cooling block on the deck.
    for i, x in enumerate((-3.4, -1.9)):
        p.cyl((x, 2.6, ch + 2.4), 0.62, 4.4, 'Z', seg=10, mat=STEEL)
        p.tube((x, 2.6, ch + 4.7), 0.72, 0.12, 0.5, 'Z', seg=10, mat=DARK)
    p.box((2.6, -1.6, ch + 1.5), (3.6, 3.2, 2.4), SLATE)
    p.louvres((1.0, -3.2, ch + 0.5), (4.2, -3.0, ch + 2.6), 6, mat=DARK)
    # Obstruction lights on the corners — required kit, and it sells the height.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * (W / 2 - 1.9), sy * (D / 2 - 1.9), ch + 0.95),
                  (0.4, 0.4, 0.55), DARK)
            p.cyl((sx * (W / 2 - 1.9), sy * (D / 2 - 1.9), ch + 1.32),
                  0.24, 0.34, 'Z', seg=8, mat=AMBER)
    conduit(p, '+X', 0.0, 0.7, ch - 1.0, count=3)
    stencil(p, '-Y', 0.0, 3.4, size=2.0, mat=RED)
    p.bevel(width=0.05, segments=1)
    return p.finish("Mesh_TowerBay_Crown", coll)


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for name, fn in (("Plain", plain), ("Windowed", windowed),
                     ("Ribbed", ribbed), ("Buttressed", buttressed),
                     ("Shoulder", shoulder), ("Crown", crown)):
        fn(collection("Coll_TowerBay_%s" % name), mats)
    report()
    save(out)


build()
