"""components/structural/slab_block — one storey of a derelict mining hulk.

`tower_bay` is the other slab-stacking component in this library and it is not
this one. That one is *clad*: white enamel over a tidy refinery frame, corner
pilasters, the language of a machine somebody still maintains. This is the same
structural idea — a storey-sized box you stack by adding H to Z — rendered as a
rusted hulk instead: bolted-on plate with dark gaps between the plates, corner
armour rather than pilasters, and weld-on repair patches that are visibly newer
than the thing they are patching.

The distinction is worth two components rather than one parameterised one
because the plating language differs all the way down. A tower bay's face is
one smooth surface with seams scribed into it; a slab block's face is a *field
of separate plates* with the structure showing through the gaps, which is what
makes it read as something built in a shipyard and left in a desert for forty
years. Recessed bays are built by raising the plating around them rather than
cutting into the core — no booleans, and the silhouette gets the depth for free.

Base envelope 16.0 (X) x 14.0 (Y) x 8.0 (Z), origin at bottom centre, so a
stack is `z += H` and nothing else. `Cantilever`, `Stepped` and `Breached`
deliberately break that envelope: an overhang, a setback and a torn corner are
the only three silhouette events a stack of boxes gets, and clamping them to
the base box would throw all three away.

    blender --background --python slab_block.py -- --out slab_block.blend

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

import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

MATS = [
    "Mat_Metal_HullRust_Orange",   # 0 HULL   the body plating — the whole read
    "Mat_Metal_Rust_Heavy",        # 1 RUST   corrosion, weld-on repair patches
    "Mat_Metal_Steel_Worn",        # 2 STEEL  corner armour, frames, exposed ribs
    "Mat_Metal_Steel_Dark",        # 3 DARK   bands, brackets, fittings
    "Mat_Neutral_Black_Matte",     # 4 BLACK  plate gaps, recess backing, shadow
    "Mat_Paint_Hull_Bleached",     # 5 BLEACH sun-bleached paint surviving up top
    "Mat_Metal_Chrome_Scuffed",    # 6 CHROME bare-worn edges, grab fittings
]
HULL, RUST, STEEL, DARK, BLACK, BLEACH, CHROME = range(7)

W, D, H = 16.0, 14.0, 8.0        # width (X), depth (Y), storey height (Z)

# Each face is (point(u, z) -> Vector, seam axis, outward normal). The seam axis
# is always the face's own normal axis: _buildlib.seam() lays `width` across the
# strip and `depth` along the axis, so passing the face normal is what makes a
# strip sit proud of the face rather than sunk sideways into it.
FACES = {
    'Y+': (lambda u, z, w, d: Vector((u, d / 2.0, z)), 'Y', Vector((0, 1, 0))),
    'Y-': (lambda u, z, w, d: Vector((u, -d / 2.0, z)), 'Y', Vector((0, -1, 0))),
    'X+': (lambda u, z, w, d: Vector((w / 2.0, u, z)), 'X', Vector((1, 0, 0))),
    'X-': (lambda u, z, w, d: Vector((-w / 2.0, u, z)), 'X', Vector((-1, 0, 0))),
}


def face_len(side, w, d):
    """How far the face runs along its own surface."""
    return w if side[0] == 'Y' else d


# ---------------------------------------------------------------------------
# Shared plating language
# ---------------------------------------------------------------------------

def core(p, w=W, d=D, h=H, z0=0.0, mat=HULL):
    """The solid storey box.

    Solid, not hollow: nothing ever sees inside a slab block, and a solid box
    costs a third of the triangles with no interior faces to clean up later.
    """
    return p.box((0, 0, z0 + h / 2.0), (w, d, h), mat)


def corner_armour(p, w=W, d=D, h=H, z0=0.0, thick=1.5, mat=STEEL):
    """Four vertical armour posts standing proud at the corners.

    These do the same job the refinery tower's pilasters do — stop a 16 m blank
    face reading as one untextured plane — but they are bare structural steel
    wrapped around the corner rather than clad trim, so the corner reads as the
    strongest part of the box instead of the most decorated.
    """
    faces = []
    for sx in (-1, 1):
        for sy in (-1, 1):
            x = sx * (w / 2.0 - thick / 2.0 + 0.12)
            y = sy * (d / 2.0 - thick / 2.0 + 0.12)
            faces += p.box((x, y, z0 + h / 2.0), (thick, thick, h), mat)
    return faces


def plate_field(p, side, w=W, d=D, h=H, z0=0.0, cols=4, rows=3, margin=1.7,
                proud=0.11, mat=HULL, gap_mat=BLACK, offset=(0, 0, 0), seed=0):
    """A grid of separately bolted-on plates covering one face.

    The gaps between plates are the point. A face scribed with thin seam lines
    reads as one surface with lines on it; a face of discrete plates standing
    0.11 m proud of a dark backing reads as *plating*, and it survives being
    lit from a low desert sun, which is the only lighting this asset will ever
    get. The backing plate is what stops the gaps showing bare core colour.

    `offset` shifts the whole field in world space, so a mass that is not
    centred on the origin — the two halves either side of a breach — can still
    be plated by the same call.
    """
    pt, ax, out = FACES[side]
    off = Vector(offset)
    L = face_len(side, w, d)
    span_u = L - 2 * margin
    span_z = h - 1.5
    if span_u <= 0 or span_z <= 0:
        return []

    # Dark backing across the whole plated area, so every gap reads as shadow.
    back_c = pt(0.0, z0 + 0.75 + span_z / 2.0, w, d) + off + out * 0.015
    faces = p.box(tuple(back_c),
                  _face_size(side, span_u + 0.2, span_z + 0.2, 0.03), gap_mat)

    rng = random.Random(seed)
    pw = span_u / cols
    ph = span_z / rows
    for i in range(cols):
        for j in range(rows):
            u = -span_u / 2.0 + pw * (i + 0.5)
            z = z0 + 0.75 + ph * (j + 0.5)
            c = pt(u, z, w, d) + off + out * (proud / 2.0 - 0.01)
            # Some plates have been cut out and welded back in; those sit a
            # little prouder and are visibly newer corrosion than their
            # neighbours. Deterministic per seed so rebuilds match.
            patched = rng.random() < 0.18
            m = RUST if patched else mat
            faces += p.box(
                tuple(c + out * (0.03 if patched else 0.0)),
                _face_size(side, pw - 0.16, ph - 0.16, proud), m)
    return faces


def _face_size(side, along, up, thick):
    """Turn (along-face, vertical, out-of-face) extents into an XYZ box size."""
    if side[0] == 'Y':
        return (along, thick, up)
    return (thick, along, up)


def belt(p, z, w=W, d=D, height=0.44, proud=0.16, mat=DARK):
    """A strapping band wrapping all four faces at one height.

    Wrapped rather than per-face, because a band that stops at the corners
    reads as decoration and a band that turns the corner reads as structure.
    """
    faces = p.box((0, 0, z), (w + 2 * proud, d - 0.5, height), mat)
    faces += p.box((0, 0, z), (w - 0.5, d + 2 * proud, height), mat)
    return faces


def recess_bay(p, side, u, z, bw, bh, w=W, d=D, frame=0.26, mat=STEEL,
               back=BLACK):
    """A dark bay framed by proud steel — a recess without a boolean.

    Raising a frame around a patch of dark backing gives the same silhouette
    read as cutting a hole, costs eight boxes, and cannot go wrong the way a
    boolean on a bevelled box can.
    """
    pt, ax, out = FACES[side]
    faces = p.box(tuple(pt(u, z, w, d) + out * 0.06),
                  _face_size(side, bw, bh, 0.05), back)
    for du, dz, su, sz in ((0, bh / 2.0, bw + 2 * frame, frame),
                           (0, -bh / 2.0, bw + 2 * frame, frame),
                           (bw / 2.0, 0, frame, bh),
                           (-bw / 2.0, 0, frame, bh)):
        faces += p.box(tuple(pt(u + du, z + dz, w, d) + out * 0.09),
                       _face_size(side, su, sz, 0.20), mat)
    return faces


def ibeam(p, center, length, section=(0.62, 0.70), axis='Z', mat=STEEL,
          web=0.11):
    """An I-section beam — web plus two flanges, three boxes.

    Used only where a plate has torn away and the frame behind it shows. An
    I-beam silhouette is the single clearest signal that a structure is built
    rather than moulded, so it is worth the extra two boxes over a bar.
    """
    fw, fd = section
    cx, cy, cz = center
    if axis == 'Z':
        faces = p.box((cx, cy, cz), (web, fd, length), mat)
        for s in (-1, 1):
            faces += p.box((cx, cy + s * fd / 2.0, cz), (fw, 0.11, length), mat)
    elif axis == 'X':
        faces = p.box((cx, cy, cz), (length, fd, web), mat)
        for s in (-1, 1):
            faces += p.box((cx, cy + s * fd / 2.0, cz), (length, 0.11, fw), mat)
    else:
        faces = p.box((cx, cy, cz), (fd, length, web), mat)
        for s in (-1, 1):
            faces += p.box((cx + s * fd / 2.0, cy, cz), (0.11, length, fw), mat)
    return faces


def cap_deck(p, w=W, d=D, z=H, mat=DARK, lip=0.30):
    """The roof slab a block presents to whatever stacks on top of it."""
    faces = p.box((0, 0, z - 0.22), (w + 2 * lip, d + 2 * lip, 0.44), mat)
    faces += p.box((0, 0, z - 0.52), (w + 0.9 * lip, d + 0.9 * lip, 0.30), STEEL)
    return faces


def plinth(p, w=W, d=D, z0=0.0, mat=DARK):
    """The heavy foot band — where a hulk meets whatever is holding it up."""
    return p.box((0, 0, z0 + 0.34), (w + 0.5, d + 0.5, 0.68), mat)


def clad_all(p, w=W, d=D, h=H, z0=0.0, cols=4, rows=3, sides=None,
             offset=(0, 0, 0), mat=HULL, seed=0, margin=1.7):
    faces = []
    for n, side in enumerate(sides or FACES):
        faces += plate_field(p, side, w, d, h, z0, cols=cols, rows=rows,
                             offset=offset, mat=mat, seed=seed + n,
                             margin=margin)
    return faces


def weathering(p, w=W, d=D, h=H, z0=0.0, seed=0, count=9):
    """Streaks and rot patches, deterministic per seed.

    Thin proud slivers of RUST hung below the plate bands — corrosion in real
    life runs *downward from a fixing*, so these are anchored to band heights
    rather than scattered freely, which is the difference between weathering
    and noise.
    """
    rng = random.Random(seed)
    faces = []
    for _ in range(count):
        side = rng.choice(list(FACES))
        pt, ax, out = FACES[side]
        L = face_len(side, w, d)
        u = rng.uniform(-L / 2.0 + 2.0, L / 2.0 - 2.0)
        top = z0 + rng.uniform(h * 0.35, h * 0.92)
        run = rng.uniform(0.7, 2.4)
        z = top - run / 2.0
        faces += p.box(tuple(pt(u, z, w, d) + out * 0.16),
                       _face_size(side, rng.uniform(0.28, 0.75), run, 0.05),
                       RUST)
    return faces


# ---------------------------------------------------------------------------
# Variations
# ---------------------------------------------------------------------------

def build_plain(mats, parent):
    """The workhorse. Everything the family shares and nothing it does not."""
    coll = collection("Coll_SlabBlock_Plain", parent)
    p = Part(mats)

    core(p)
    plinth(p)
    corner_armour(p)
    clad_all(p)
    belt(p, 5.9)
    recess_bay(p, 'Y-', -3.4, 3.5, 3.0, 2.4)
    recess_bay(p, 'X+', 2.6, 4.6, 2.6, 2.0)
    cap_deck(p)
    weathering(p, seed=11)

    p.bevel(width=0.035, segments=1)
    p.finish("Mesh_SlabBlock_Plain", coll)
    return coll


def build_cantilever(mats, parent):
    """The overhang. This is the reference silhouette's signature move.

    The bracket corbels underneath are load-bearing visually as well as
    structurally: an overhang with nothing under it reads as a modelling
    mistake, and three raked brackets is the smallest number that reads as
    engineering rather than as one accidental strut.
    """
    coll = collection("Coll_SlabBlock_Cantilever", parent)
    p = Part(mats)

    core(p)
    plinth(p)
    corner_armour(p)
    clad_all(p, sides=('Y+', 'Y-', 'X-'))

    over_w, over_d, over_z0 = 4.6, D - 1.6, 2.6
    ox = W / 2.0 + over_w / 2.0
    oh = H - over_z0
    p.box((ox, 0, over_z0 + oh / 2.0), (over_w, over_d, oh), HULL)
    # Plate the overhang's own three exposed faces. It is a mass in its own
    # right, so it gets its own envelope and its own offset rather than being
    # smeared onto the core's plate grid.
    clad_all(p, w=over_w, d=over_d, h=oh, z0=over_z0, cols=2, rows=2,
             sides=('X+', 'Y+', 'Y-'), offset=(ox, 0, 0), margin=0.6, seed=5)

    # Underside ribs, then the corbels that carry the whole thing.
    for i in range(4):
        y = -D / 2.0 + 2.2 + i * (D - 4.4) / 3.0
        p.box((ox, y, over_z0 + 0.12), (over_w - 0.3, 0.34, 0.30), DARK)
    for i in range(3):
        y = -D / 2.0 + 2.6 + i * (D - 5.2) / 2.0
        prof = [(0.0, 0.0), (over_w + 0.2, 0.0), (0.0, -2.9)]
        p.prism([(W / 2.0 - 0.15 + u, over_z0 + v) for u, v in prof],
                0.46, axis='Y', mat=STEEL, offset=(0, y, 0))

    belt(p, 1.5)
    recess_bay(p, 'Y-', -4.2, 4.4, 3.2, 2.4)
    cap_deck(p)
    weathering(p, seed=23)

    p.bevel(width=0.035, segments=1)
    p.finish("Mesh_SlabBlock_Cantilever", coll)
    return coll


def build_stepped(mats, parent):
    """A setback with an occupiable ledge — where catwalks get somewhere to go."""
    coll = collection("Coll_SlabBlock_Stepped", parent)
    p = Part(mats)

    low_h = 5.0
    up_w, up_d, up_h = 11.0, 9.5, H - low_h
    ux, uy = -(W - up_w) / 2.0 + 1.2, -(D - up_d) / 2.0 + 1.0

    core(p, h=low_h)
    plinth(p)
    corner_armour(p, h=low_h)
    clad_all(p, h=low_h, rows=2)
    belt(p, low_h - 0.45)

    # The upper mass keeps its paint: it is the part of a derelict that is not
    # standing in blown sand, so BLEACH over RUST rather than the other way up.
    p.box((ux, uy, low_h + up_h / 2.0), (up_w, up_d, up_h), BLEACH)
    clad_all(p, w=up_w, d=up_d, h=up_h + 0.6, z0=low_h - 0.3, cols=3, rows=2,
             offset=(ux, uy, 0), mat=BLEACH, margin=0.8, seed=17)

    # The ledge itself: grating, kick plate, stanchions along the two open sides.
    p.box((0, 0, low_h + 0.09), (W - 0.4, D - 0.4, 0.18), DARK)
    for i in range(7):
        x = -W / 2.0 + 1.2 + i * (W - 2.4) / 6.0
        p.box((x, D / 2.0 - 0.5, low_h + 0.72), (0.13, 0.13, 1.10), STEEL)
    p.box((0, D / 2.0 - 0.5, low_h + 1.24), (W - 2.0, 0.09, 0.09), CHROME)
    p.box((0, D / 2.0 - 0.35, low_h + 0.24), (W - 1.6, 0.06, 0.30), STEEL)

    recess_bay(p, 'Y-', 3.0, 3.0, 2.8, 2.2)
    cap_deck(p, w=up_w, d=up_d, z=H)
    weathering(p, h=low_h, seed=37, count=7)

    p.bevel(width=0.035, segments=1)
    p.finish("Mesh_SlabBlock_Stepped", coll)
    return coll


def build_buttressed(mats, parent):
    """Raked external ribs — the block that looks like it is bracing something."""
    coll = collection("Coll_SlabBlock_Buttressed", parent)
    p = Part(mats)

    core(p)
    plinth(p)
    corner_armour(p, thick=1.2)
    clad_all(p, cols=3, rows=3)

    for side in ('Y+', 'Y-'):
        pt, ax, out = FACES[side]
        sgn = 1 if side == 'Y+' else -1
        for i in range(3):
            x = -W / 2.0 + 3.4 + i * (W - 6.8) / 2.0
            prof = [(0.0, 0.0), (2.15, 0.0), (0.55, H - 0.6), (0.0, H - 0.6)]
            p.prism([(sgn * (D / 2.0 - 0.1 + u), v) for u, v in prof],
                    0.70, axis='X', mat=STEEL, offset=(x, 0, 0))
    for side in ('X+', 'X-'):
        sgn = 1 if side == 'X+' else -1
        for i in range(2):
            y = -D / 2.0 + 4.0 + i * (D - 8.0)
            prof = [(0.0, 0.0), (1.75, 0.0), (0.45, H - 1.4), (0.0, H - 1.4)]
            p.prism([(sgn * (W / 2.0 - 0.1 + u), v) for u, v in prof],
                    0.62, axis='Y', mat=STEEL, offset=(0, y, 0))

    belt(p, 2.2, height=0.55)
    belt(p, 6.4, height=0.55)
    cap_deck(p)
    weathering(p, seed=53)

    p.bevel(width=0.035, segments=1)
    p.finish("Mesh_SlabBlock_Buttressed", coll)
    return coll


def build_breached(mats, parent):
    """A torn corner with the frame showing — the derelict read.

    The notch is composed from three boxes rather than cut with a boolean, so
    the break edge is exactly where it was designed to be. What sells it is not
    the hole, it is the four things inside the hole: floor beams that clearly
    continued through what is now missing, a column sheared mid-span, torn plate
    fragments still bolted along the break line, and one panel hanging off it.
    """
    coll = collection("Coll_SlabBlock_Breached", parent)
    p = Part(mats)

    split = 3.4                          # the intact lower storey height
    nx, ny = 6.2, 5.4                    # notch extent in +X / +Y

    p.box((0, 0, split / 2.0), (W, D, split), HULL)
    up_h = H - split
    p.box((-nx / 2.0, 0, split + up_h / 2.0), (W - nx, D, up_h), HULL)
    p.box((W / 2.0 - nx / 2.0, -ny / 2.0, split + up_h / 2.0),
          (nx, D - ny, up_h), HULL)

    plinth(p)
    for sx, sy in ((-1, -1), (-1, 1), (1, -1)):
        x = sx * (W / 2.0 - 0.63)
        y = sy * (D / 2.0 - 0.63)
        p.box((x, y, H / 2.0), (1.5, 1.5, H), STEEL)
    p.box((W / 2.0 - 0.63, D / 2.0 - 0.63, split / 2.0), (1.5, 1.5, split),
          STEEL)

    # Below the break the box is still whole, so all four faces get plated.
    clad_all(p, h=split + 0.4, rows=1, cols=4, seed=61)
    # Above it there are two separate masses left, each plated in its own frame.
    clad_all(p, w=W - nx, d=D, h=up_h, z0=split, cols=3, rows=2,
             sides=('X-', 'Y-', 'Y+'), offset=(-nx / 2.0, 0, 0), seed=64)
    clad_all(p, w=nx, d=D - ny, h=up_h, z0=split, cols=2, rows=2,
             sides=('X+', 'Y-'), offset=(W / 2.0 - nx / 2.0, -ny / 2.0, 0),
             margin=1.0, seed=67)

    # Inside the void: the floor that used to be there, and a sheared column.
    for i in range(3):
        y = D / 2.0 - ny + 0.9 + i * (ny - 1.8) / 2.0
        ibeam(p, (W / 2.0 - nx / 2.0, y, split + 0.35), nx, axis='X', mat=STEEL)
    ibeam(p, (W / 2.0 - nx + 1.1, D / 2.0 - 1.5, split + up_h * 0.55),
          up_h * 1.1, axis='Z', mat=STEEL)
    ibeam(p, (W / 2.0 - nx / 2.0, D / 2.0 - ny + 0.5, split + up_h - 0.5), nx,
          axis='X', mat=RUST)

    # Torn plate fragments still bolted along the break line, and one panel
    # hanging by a corner. Angles are hand-picked, not random: debris that all
    # leans the same way reads as wind, debris at scattered angles reads as
    # something that failed.
    for u, z, ang, size in ((-1.9, 0.7, 24, (1.5, 0.06, 1.1)),
                            (0.6, 2.1, -37, (1.1, 0.06, 1.6)),
                            (2.4, 0.3, 13, (0.9, 0.06, 0.8))):
        p.box((W / 2.0 - nx + 0.1, D / 2.0 - ny + 2.6 + u, split + z), size,
              RUST, rot=Matrix.Rotation(math.radians(ang), 4, 'Y'))
    p.box((W / 2.0 - nx - 0.22, D / 2.0 - 2.4, split + up_h - 2.1),
          (0.07, 2.3, 3.0), RUST,
          rot=Matrix.Rotation(math.radians(17), 4, 'Y'))

    belt(p, split - 0.5)
    p.box((-nx / 2.0, 0, H - 0.22), (W - nx + 0.6, D + 0.6, 0.44), DARK)
    weathering(p, seed=71, count=13)

    p.bevel(width=0.035, segments=1)
    p.finish("Mesh_SlabBlock_Breached", coll)
    return coll


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    root = bpy.context.scene.collection

    build_plain(mats, root)
    build_cantilever(mats, root)
    build_stepped(mats, root)
    build_buttressed(mats, root)
    build_breached(mats, root)

    report()
    save(out)


if __name__ == "__main__":
    main()
