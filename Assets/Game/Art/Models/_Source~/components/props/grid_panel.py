"""Wall storage panels — the vertical counterpart to the expedition rig's mat.

The rig lays gear on canvas boards that carry a webbing grid; this is the same
idea stood on its edge and bolted to a bulkhead. Three variations, because a
wall of one panel repeated ten times reads as wallpaper:

    Coll_GridPanel_Webbed     canvas over a frame, webbing tapes both ways,
                              brass eyelets on the crossings — the rig's own
                              language, and the default read
    Coll_GridPanel_Pegboard   a punched steel plate with hook rails — the
                              engineered version, for machine spaces
    Coll_GridPanel_Netted     an open sub-frame with a laced cord net — the
                              cheap field version, and the only one you can
                              see through

The builders below take the rectangle to fill rather than a fixed size, which
is the whole point of the file: `models/props/inventory_wall.py` imports them
and tiles full-height 0.54 x 2.70 bays out of the same code that draws the
0.54 x 0.90 module saved here. Two copies of a webbing field that had to stay
on the same pitch is exactly the drift this avoids.

Pitch is not a style choice
---------------------------
Every tape, seam, hole and eyelet POSITION sits on a multiple of CELL. Gear
placed on one of these panels snaps to that grid, so decoration off the pitch
reads as a rendering fault — the item sits visibly between the lines it is
supposed to sit on. The rig learned this the hard way (its own stitching is at
200/260 mm against a 90 mm cell and the two have drifted ever since). Keep every
POSITION here a multiple of CELL.

Stock is not, and that is the distinction that matters
------------------------------------------------------
Frame section, tape width, plate thickness, bolt and eyelet radii, net cord — the
fifteen lengths below that are not multiples of CELL — are stock sizes, chosen
against each other rather than against the grid, and they are deliberately left
that way. A 0.024 m webbing tape is a webbing tape at any cell size.

The consequence is that **CELL is not a scale knob.** Raising it enlarges every
position and no stock size, which reproportions a panel rather than resizing it.
When the whole physical inventory was enlarged 1.5x on 2026-09-01, the wall was
therefore scaled by a separate similarity pass
(`models/props/inventory_wall_scale.py`) and this file was left authoring at
0.090 — so **CELL below is the MODELLING cell and is no longer `PackGrid.Cell`,
which is 0.135.** `grid_panel.blend` itself was not rescaled: nothing ships it,
it is a reference module, and its own 0.54 x 0.90 saved size is what the wall's
builders are documented against.

Orientation
-----------
A panel is VERTICAL. Its face lies in XZ at y = 0 and looks along -Y, which is
the library's forward; the backing grows into +Y, behind it. The origin is the
bottom CENTRE of the face — the point a wall bolts it down by — so a row of
bays is a row of x offsets and nothing else.

    blender --background --python grid_panel.py -- --out grid_panel.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

MATS = [
    "Mat_Fabric_Canvas_Sand",     # 0  panel canvas — the rig's dressed colour
    "Mat_Fabric_Wing_Ochre",      # 1  webbing tape, a shade off the canvas
    "Mat_Metal_Steel_Worn",       # 2  panel frames, hook rails, pegboard plate
    "Mat_Metal_Steel_Dark",       # 3  bolt heads, lacing posts
    "Mat_Metal_Brass_Tarnished",  # 4  eyelets and grommets
    "Mat_Fabric_Rope_Hemp",       # 5  the net's cord
]
SAND, OCHRE, STEEL, DARK, BRASS, CORD = range(6)

# The MODELLING cell — see the header. Every POSITION drawn here is a whole
# multiple of it; the stock sizes below are not, and must not be made to be.
# `PackGrid.Cell` is 0.135: this frame times `inventory_wall_scale.SCALE`.
CELL = 0.090
PITCH = 2 * CELL              # 0.180 — tape and eyelet spacing on every variant

FRAME_T = 0.030               # panel frame stock, square section
FRAME_D = 0.045               # how far the frame stands off the bulkhead
FACE_T = 0.018                # canvas / plate thickness
TAPE_W = 0.024                # webbing tape width
TAPE_P = 0.008                # how far a tape stands proud of the face


# ---------------------------------------------------------------------------
# Helpers — XZ plane, -Y normal
# ---------------------------------------------------------------------------

def _lines(a, b, pitch, margin):
    """Positions on a pitch, inset from both ends and phase-aligned to zero.

    Aligned to the global grid rather than to the rectangle: two bays side by
    side must continue each other's lines, and a per-rectangle phase makes the
    seam between them visible from across the room.
    """
    first = math.ceil((a + margin) / pitch) * pitch
    out = []
    v = first
    while v <= b - margin + 1e-6:
        out.append(round(v, 6))
        v += pitch
    return out


def _bar(p, a, b, width, depth, mat):
    """A square-section bar between two points in the XZ plane."""
    a, b = Vector(a), Vector(b)
    d = b - a
    length = d.length
    if length < 1e-6:
        return []
    d = d.normalized()
    side = Vector((0.0, -1.0, 0.0))          # out of the wall
    up = d.cross(side).normalized()
    rot = Matrix((side, d, up)).transposed().to_4x4()
    return p.box((a + b) / 2.0, (depth, length, width), mat, rot=rot)


def frame(p, x0, x1, z0, z1, mat=STEEL):
    """The steel surround every variation is stretched over.

    Built as four bars rather than a slab with a hole: a rectangular ring cut
    from a solid caps into a concave n-gon, which triangulates into overlapping
    faces on FBX export. Every profile in this library is convex for that
    reason.
    """
    t, d = FRAME_T, FRAME_D
    faces = []
    faces += p.slab((x0, 0.0, z0), (x1, d, z0 + t), mat)
    faces += p.slab((x0, 0.0, z1 - t), (x1, d, z1), mat)
    faces += p.slab((x0, 0.0, z0 + t), (x0 + t, d, z1 - t), mat)
    faces += p.slab((x1 - t, 0.0, z0 + t), (x1, d, z1 - t), mat)
    return faces


def bolts(p, x0, x1, z0, z1, mat=DARK):
    """Four corner fixings — what says 'bolted to a bulkhead' rather than 'leaning'.

    Sunk INTO the frame rather than standing proud of it. y = 0 is the plane
    gear is placed on, so anything in front of it is geometry an item lies
    across: a 16 mm bolt head at every bay corner would poke through the first
    thing hung over it.
    """
    inset = FRAME_T / 2.0
    for x in (x0 + inset, x1 - inset):
        for z in (z0 + inset, z1 - inset):
            p.cyl((x, 0.008, z), 0.011, 0.016, axis='Y', seg=6, mat=mat,
                  radius_top=0.008)


# ---------------------------------------------------------------------------
# The three variations
#
# Each fills the rectangle it is handed, frame included, and each is a pure
# function of that rectangle — no module-level state, so the wall can call them
# ten times with different bounds in one Part.
# ---------------------------------------------------------------------------

def webbed(p, x0, x1, z0, z1):
    """Canvas over a frame, webbing both ways, eyelets on the crossings."""
    frame(p, x0, x1, z0, z1)
    bolts(p, x0, x1, z0, z1)

    ix0, ix1 = x0 + FRAME_T, x1 - FRAME_T
    iz0, iz1 = z0 + FRAME_T, z1 - FRAME_T

    p.slab((ix0, FRAME_D - FACE_T, iz0), (ix1, FRAME_D, iz1), SAND)

    xs = _lines(ix0, ix1, PITCH, TAPE_W)
    zs = _lines(iz0, iz1, PITCH, TAPE_W)
    face = FRAME_D - FACE_T

    for x in xs:
        _bar(p, (x, face, iz0), (x, face, iz1), TAPE_W, TAPE_P, OCHRE)
    for z in zs:
        _bar(p, (ix0, face, z), (ix1, face, z), TAPE_W, TAPE_P, OCHRE)

    # Eyelets only where two tapes cross — that is where a real one would be
    # punched, and it keeps the count to the crossings rather than the cells.
    for x in xs:
        for z in zs:
            p.tube((x, face - TAPE_P + 0.004, z), 0.014, 0.005, 0.020,
                   axis='Y', seg=6, mat=BRASS)


def pegboard(p, x0, x1, z0, z1):
    """A punched steel plate with two hook rails across it."""
    frame(p, x0, x1, z0, z1)
    bolts(p, x0, x1, z0, z1)

    ix0, ix1 = x0 + FRAME_T, x1 - FRAME_T
    iz0, iz1 = z0 + FRAME_T, z1 - FRAME_T
    face = FRAME_D - FACE_T

    p.slab((ix0, face, iz0), (ix1, FRAME_D, iz1), STEEL)

    # Ring bosses rather than real holes: a boolean through a plate for every
    # hole is thousands of triangles and a non-manifold risk on export, and at
    # the distance a wall is read the ring alone sells the punch.
    for x in _lines(ix0, ix1, PITCH, 0.030):
        for z in _lines(iz0, iz1, PITCH, 0.030):
            p.tube((x, face - 0.004, z), 0.017, 0.005, 0.014, axis='Y',
                   seg=6, mat=DARK)

    # Two rails, on the pitch like everything else, so hung gear has a line.
    rails = _lines(iz0, iz1, (iz1 - iz0 - 2 * PITCH) / 2.0, PITCH * 2)
    for z in rails[:2]:
        _bar(p, (ix0 + PITCH, face - 0.012, z), (ix1 - PITCH, face - 0.012, z),
             0.020, 0.030, STEEL)


def netted(p, x0, x1, z0, z1):
    """An open sub-frame with a cord net laced across it."""
    frame(p, x0, x1, z0, z1)
    bolts(p, x0, x1, z0, z1)

    ix0, ix1 = x0 + FRAME_T, x1 - FRAME_T
    iz0, iz1 = z0 + FRAME_T, z1 - FRAME_T
    face = FRAME_D - 0.010

    # Lacing posts down both stiles, on the pitch: the net is tied to these.
    for z in _lines(iz0, iz1, PITCH * 2, PITCH):
        for x in (x0 + FRAME_T / 2.0, x1 - FRAME_T / 2.0):
            p.cyl((x, face, z), 0.008, 0.024, axis='Y', seg=6, mat=DARK)

    cord = 0.007
    for x in _lines(ix0, ix1, PITCH, PITCH / 2.0):
        p.cyl((x, face, (iz0 + iz1) / 2.0), cord, iz1 - iz0, axis='Z',
              seg=6, mat=CORD)
    for z in _lines(iz0, iz1, PITCH, PITCH / 2.0):
        p.cyl(((ix0 + ix1) / 2.0, face - cord, z), cord, ix1 - ix0, axis='X',
              seg=6, mat=CORD)


VARIANTS = (("Webbed", webbed), ("Pegboard", pegboard), ("Netted", netted))

# The module the .blend saves each variation at. Six cells wide, ten tall — a
# size that tiles both a bay of the inventory wall and a locker door.
MODULE_W = 6 * CELL
MODULE_H = 10 * CELL


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    for name, build in VARIANTS:
        coll = collection("Coll_GridPanel_%s" % name)
        p = Part(mats)
        build(p, -MODULE_W / 2.0, MODULE_W / 2.0, 0.0, MODULE_H)
        p.bevel(width=0.004, segments=1)
        p.finish("Mesh_GridPanel_%s" % name, coll, origin=(0.0, 0.0, 0.0))

    report()
    save(out)


if __name__ == "__main__":
    main()
