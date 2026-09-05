"""Shared kit for the console family: CRT monitors, keyboard decks, pedestals
and the standing terminal assembled from them.

Three things live here rather than in each component script, for the same
reason `panel_control.py` holds the rockers and knobs: one definition, not
three drifting copies.

**One material list.** Indices 0-15 are the props family's order — the same
sixteen, index for index, as `oxygen_generator.py`, `power_cell.py` and
`dock_cradle.py` — so parts appended from any of these files into one model
share a single slot table. Index 0 is structural steel because
`bmesh.ops.bevel` stamps every face it creates with index 0; a forgotten
`mat=` lands somewhere harmless.

**Rounded outlines built as outlines, not as overlapping slabs.** The moulded
cassette-futurism look these consoles copy is a rounded-rectangle bezel with
no seam anywhere on its face. Four slabs bevelled separately put a chamfer
groove across the face at every joint, so `rounded_frame()` builds the bezel
as one ring and `rounded_slab()` a tray as one prism — the corner arcs are
real geometry and the bevel runs unbroken around them.

**Keycaps as tapered lofts, flat-shaded.** `_buildlib.loft` smooth-shades
everything that is not an end cap, which on a four-sided cap smears the 90°
corners into a blob; every cap here is set flat after the loft.

Importable, like `panel_control.py`: the component scripts and the model
script call these builders directly.
"""

import math
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_LIB = os.path.dirname(os.path.dirname(_HERE))
sys.path.insert(0, _LIB)
sys.path.insert(0, os.path.join(_LIB, "components", "mechanical"))

import bmesh  # noqa: E402
from mathutils import Vector  # noqa: E402

from _tracked import TrackedPart  # noqa: E402,F401  (re-exported for callers)

# 0-15 match the props family — see the module docstring. 16 is this
# family's own: RED (index 5) is matte stencil paint, and a status lamp
# beside two emissive ones has to glow like them.
(STEEL, DARK, RUBBER, CHROME, CREAM, RED, BLUE, AMBER, BLACK, CRT,
 SHELL, GREY, ORANGE, YELLOW, GREEN, SLATE, LAMP_RED) = range(17)
MATS = ["Mat_Metal_Steel_Worn", "Mat_Metal_Steel_Dark",
        "Mat_Plastic_Rubber_Black", "Mat_Metal_Chrome_Scuffed",
        "Mat_Plastic_Cream_Aged", "Mat_Paint_Warn_Red",
        "Mat_Paint_Blue_Station", "Mat_Emissive_Amber",
        "Mat_Neutral_Black_Matte", "Mat_Emissive_Green_CRT",
        "Mat_Paint_White_Arctic", "Mat_Neutral_Panel_Grey",
        "Mat_Paint_Safety_Orange", "Mat_Plastic_Safety_Yellow",
        "Mat_Paint_Cell_Green", "Mat_Neutral_Slate_Dark",
        "Mat_Emissive_Red_Warn"]

# Two widths, and the split matters (see `oxygen_generator.py`): the wide
# chamfer is the art style on the big moulded blocks and has to read across a
# cabin; on a 10 mm slot or a 6 mm vent bar it swells the part past its own
# bounds. Fine set first, or the coarse pass re-walks edges already rounded.
BEVEL_W = 0.010
FINE_W = 0.003

# Anything that sits on a surface is buried this far into it. Meeting the
# surface exactly is what z-fights; 2 mm is `_zverify`'s own tolerance and is
# reported as a clash, so 3 mm is the smallest safe embed.
EMBED = 0.003


# --------------------------------------------------------------------------
# Rounded outlines
# --------------------------------------------------------------------------

def rounded_rect(x0, x1, z0, z1, r, seg=6):
    """CCW (u, v) outline of a rounded rectangle, `seg` facets per corner.

    The radius is clamped just under half the shorter side, so a caller asking
    for a pill gets a pill rather than a pair of coincident points that
    `remove_doubles` folds into a degenerate face.
    """
    r = max(1e-4, min(r, (x1 - x0) / 2 - 1e-4, (z1 - z0) / 2 - 1e-4))
    corners = (((x1 - r, z1 - r), 0), ((x0 + r, z1 - r), 90),
               ((x0 + r, z0 + r), 180), ((x1 - r, z0 + r), 270))
    pts = []
    for (cx, cz), a0 in corners:
        for i in range(seg + 1):
            a = math.radians(a0 + 90.0 * i / seg)
            pts.append((cx + r * math.cos(a), cz + r * math.sin(a)))
    return pts


def _shade_walls(faces, axis):
    """Corner arcs smooth, everything flat stays flat.

    The library's cylinder convention (walls smooth, caps flat) is wrong for
    an extruded rounded rectangle: its long straight walls would take the
    arcs' tilted vertex normals and pillow across their whole width. So a
    wall is smooth only if it is NOT axis-aligned — i.e. it is an arc facet —
    and every edge where smooth meets flat is marked sharp so the arc's
    normals stop at the boundary instead of leaking into the flat face.
    """
    axis = Vector(axis)
    for f in faces:
        n = f.normal.normalized()
        arc = abs(n.dot(axis)) < 0.9 and max(abs(n.x), abs(n.y), abs(n.z)) < 0.995
        f.smooth = arc
    for f in faces:
        for e in f.edges:
            if len(e.link_faces) == 2 and \
                    e.link_faces[0].smooth != e.link_faces[1].smooth:
                e.smooth = False
    return faces


def rounded_slab(p, x0, x1, z0, z1, y0, y1, r, mat, seg=6):
    """A solid rounded-rectangle slab in XZ, spanning `y0`..`y1`."""
    prof = rounded_rect(x0, x1, z0, z1, r, seg)
    faces = p.prism(prof, abs(y1 - y0), axis='Y', mat=mat,
                    offset=(0, (y0 + y1) / 2.0, 0))
    return _shade_walls(faces, (0, 1, 0))


def rounded_slab_z(p, x0, x1, y0, y1, z0, z1, r, mat, seg=6):
    """The same slab lying flat: outline in XY, thickness along Z — a tray."""
    prof = rounded_rect(x0, x1, y0, y1, r, seg)
    faces = p.prism(prof, abs(z1 - z0), axis='Z', mat=mat,
                    offset=(0, 0, (z0 + z1) / 2.0))
    return _shade_walls(faces, (0, 0, 1))


def rounded_frame(p, outer, inner, y0, y1, mat, seg=6):
    """A rounded-rectangle ring in XZ: `outer` and `inner` are
    `(x0, x1, z0, z1, r)`, the inner one is the aperture, `y0`..`y1` the depth.

    One closed surface, so the bevel runs round the whole bezel and round the
    whole aperture with no joint anywhere on the face. Goes through the Part's
    absorb path so a `TrackedPart` records the faces by identity.
    """
    op = rounded_rect(*outer, seg=seg)
    ip = rounded_rect(*inner, seg=seg)
    yf, yb = min(y0, y1), max(y0, y1)
    bm2 = bmesh.new()
    of = [bm2.verts.new((u, yf, v)) for u, v in op]
    if_ = [bm2.verts.new((u, yf, v)) for u, v in ip]
    ob = [bm2.verts.new((u, yb, v)) for u, v in op]
    ib = [bm2.verts.new((u, yb, v)) for u, v in ip]
    n = len(op)
    for i in range(n):
        j = (i + 1) % n
        bm2.faces.new((of[i], of[j], if_[j], if_[i]))
        bm2.faces.new((ob[j], ib[j], ib[i], ob[i]))
        bm2.faces.new((of[i], ob[i], ob[j], of[j]))
        bm2.faces.new((if_[j], ib[j], ib[i], if_[i]))
    faces = p._absorb(bm2, mat)
    return _shade_walls(faces, (0, 1, 0))


# --------------------------------------------------------------------------
# Fittings
# --------------------------------------------------------------------------

def keycap(p, cx, cy, w, d, z0, h, mat, taper=0.80):
    """A keycap standing on z = `z0`: a box that narrows toward its top.

    The taper is the whole read — a straight box is a tile, a tapered one is
    a key. Flat-shaded, see the module docstring.
    """
    base = [(cx - w / 2, cy - d / 2), (cx + w / 2, cy - d / 2),
            (cx + w / 2, cy + d / 2), (cx - w / 2, cy + d / 2)]
    top = [(cx + (u - cx) * taper, cy + (v - cy) * taper) for u, v in base]
    faces = p.loft([(z0, base), (z0 + h, top)], axis='Z', mat=mat)
    return p.shade(faces, smooth=False)


# Buttons and lamps are usually planted through a socket plate that is itself
# EMBED deep in the face, so they go twice as deep — a base that stopped on
# the plate's own back plane would share it.
PLANT = 2 * EMBED


def square_key(p, cx, cz, size, y_face, mat, proud=0.008):
    """A backlit square button standing `proud` of a vertical face at
    `y_face` (which faces -Y), its base planted through that face."""
    return p.box((cx, y_face - (proud - PLANT) / 2.0, cz),
                 (size, proud + PLANT, size), mat)


def lamp(p, cx, cz, y_face, mat, radius=0.007, proud=0.010):
    """A round indicator lamp on a -Y-facing surface, base planted."""
    return p.cyl((cx, y_face - (proud - PLANT) / 2.0, cz), radius,
                 proud + PLANT, 'Y', 12, mat)


def slot(p, x0, x1, z0, z1, y_face, proud=EMBED, facing=-1):
    """A dark slit standing a hair proud of a face — the library's read of a
    recess, since a real hole needs a boolean and at this depth nobody can
    tell (see `panel_control.rotary_selector`). Proud by EMBED, not the
    handheld family's 1.5 mm: `_zverify` calls anything within 2 mm of the
    face a clash.

    `facing` is the side the face is seen from: -1 for a front (-Y) face,
    +1 for a back face. The slit is buried on the other side.
    """
    return p.slab((x0, y_face - facing * EMBED, z0),
                  (x1, y_face + facing * proud, z1), BLACK)


def vent(p, x0, x1, z0, z1, y_face, bars=5, mat_bar=GREY, facing=-1):
    """A slotted vent: a dark well with horizontal bars standing in it."""
    hard = list(slot(p, x0, x1, z0, z1, y_face, facing=facing))
    span = z1 - z0
    for i in range(bars):
        z = z0 + span * (i + 0.5) / bars
        hard += p.box(((x0 + x1) / 2.0, y_face + facing * 0.004, z),
                      (x1 - x0 - 0.010, 0.008, span / bars * 0.5), mat_bar)
    return hard


# --------------------------------------------------------------------------
# Finishing
# --------------------------------------------------------------------------

def emit(p, name, coll, hard=(), fine=(), origin=(0, 0, 0)):
    """Restamp, bevel the fine set narrow and the big blocks wide, emit."""
    p.restamp()
    if fine:
        p.bevel(list(fine), width=FINE_W, segments=2)
    if hard:
        p.bevel(list(hard), width=BEVEL_W, segments=2)
    return p.finish(name, coll, origin=origin)
