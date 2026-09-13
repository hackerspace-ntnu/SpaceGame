"""The rectangular half of the nomad settlement generator.

`nomad_settlement.py` owns the round family and the shared rules; this module
owns everything boxy: the block plans, the arcades, the terraces, and the wall
detailing that both families use.

The rectangular kit is a 2.2 m module on a 1.219 m storey. Every block, band,
roof and foundation here is the user's own hand-modelled part, scaled and
stacked — the blocks have a modelled bevel and a slight batter, so they are
scaled **uniformly** and footprint variety comes from composing several blocks
into a plan rather than from stretching one.

Rules this module adds to the settlement's set:

  X1  Blocks scale uniformly. Slabs, bands and foundations may scale per axis;
      anything with a modelled corner radius may not, or the bevel goes oval.
  X2  A plan is cells on the 1.1 m half-module grid. Upper storeys are a subset
      of the storey below, so every block lands on something.
  X3  Wall details are placed in quantity - that is the point of them - but
      never across a door and never overlapping each other.
  X5  A terrace is rare, and never appears without a door opening onto it.
"""

import math

import bmesh
import bpy
from mathutils import Matrix, Vector

import _buildlib as bl

# ------------------------------------------------------------- the rect kit
# name, x, y, z of the source part as modelled.
BLOCKS = {
    "sq":         ("rectangular_base", 2.200, 2.200, 1.219),
    "narrow":     ("small_rectangular_unit", 1.200, 2.200, 1.219),
    "long":       ("rectangular_rect_base", 3.200, 2.200, 1.219),
    "sq_taper":   ("rectangular_tilted_base", 2.200, 2.200, 1.267),
    "long_taper": ("rectangular_rect_base_titled", 3.200, 2.200, 1.219),
    "sq_small":   ("Cube.004", 1.934, 1.934, 1.071),
    # Cube.012 is the kit's TOP EXTENSION, not a storey: it pinches to a 1.2 m
    # neck at its base, so used as a storey it reads as a missing block with
    # the band and the storey above levitating over the gap. Left out of the
    # plans for that reason.
    "sq_tall":    ("Cube.012", 2.200, 2.200, 1.501),
}
BANDS = {
    "thin":  ("rectangular_square_diameter_default", 2.287, 2.287, 0.098),
    "thick": ("rectangular_square_diameter_thich", 2.233, 2.233, 0.293),
    "wide":  ("Cube.021", 3.176, 2.176, 0.133),
}
ROOFS = {
    "sq":    ("rectangular_square_roof_defualt", 2.200, 2.200, 0.564),
    "long":  ("rectangular_rect_base_roof_default", 3.200, 2.200, 0.385),
    "small": ("rectangular_roof_simple", 1.739, 1.739, 0.432),
}
TERRACE_ROOF = ("rectangular_terrace_roof", 2.200, 2.200, 0.435)
FOUNDATIONS = {
    "sq":    ("foundation_square", 3.269, 3.269, 0.500),
    "rect":  ("foundation_rectangular", 4.594, 3.158, 0.532),
    "round": ("sircular_foundation", 3.400, 3.400, 0.453),
}
TERRACES = {
    "small": ("rectangular_terrace_defualt", 1.724, 1.023, 0.411),
    "large": ("rectangular_terrace_defualt.001", 1.724, 1.823, 0.411),
}

RECT_SRC = sorted({v[0] for v in BLOCKS.values()} | {v[0] for v in BANDS.values()}
                  | {v[0] for v in ROOFS.values()}
                  | {v[0] for v in FOUNDATIONS.values()}
                  | {v[0] for v in TERRACES.values()} | {TERRACE_ROOF[0]})

MODULE = 2.200          # the plan grid
STOREY = 1.219
BAND_PROUD = 0.033      # how far the kit's own band stands off its block

# X6 - every block carries a Bevel modifier 0.3 wide in its own local space,
# which at the blocks' scale rounds 0.33 m off each vertical edge and 0.18 m
# off the top and bottom. A bevel does not move a face plane, so two blocks
# butted together still touch across the flat middle - but the rounded strip
# either side of the seam leaves a lens-shaped gap that reads as a crack.
# Neighbours therefore overlap by the bevel, horizontally and vertically.
CELL_BITE = 0.34
STOREY_BITE = 0.20


def side_by_side(a, b, axis=0):
    """Centre-to-centre offset for two blocks sharing a seam, bevel included."""
    return (BLOCKS[a][1 + axis] + BLOCKS[b][1 + axis]) * 0.5 - CELL_BITE

# X2 - plans, as cells (kind, dx, dy) on the half-module grid. Every upper
# storey must be a subset of the one below it, so plans are listed with their
# own setback sequence.
PLANS = [
    # X2 - a plan is cells on the module grid, and most of the town is one or
    # two storeys: this is a settlement of huts with a few towers in it, not a
    # skyline. The third entry in each list is only reached by the rare tall
    # roll in roll_rect.
    ("Hut",     [[("sq", 0, 0)],
                 [("sq_small", 0, 0)]]),
    ("Long",    [[("long", 0, 0)],
                 [("sq", -0.5, 0)],
                 [("sq_small", -0.5, 0)]]),
    ("Ell",     [[("sq", 0, 0), ("narrow", -1.360, 0)],
                 [("sq", 0, 0)]]),
    ("Tee",     [[("long", 0, 0), ("sq", 0, -1.860)],
                 [("long", 0, 0)],
                 [("sq", 0.5, 0)]]),
    ("Wing",    [[("sq", 0, 0), ("narrow", 1.360, 0), ("narrow", -1.360, 0)],
                 [("sq", 0, 0)]]),
    ("Yard",    [[("long", 0, 0), ("narrow", -1.000, -1.860)],
                 [("sq", -0.5, 0)]]),
    ("Stack",   [[("sq", 0, 0)],
                 [("sq", 0, 0)],
                 [("sq_small", 0, 0)]]),
]


# --------------------------------------------------------------- wall details
def discover_details(objs, link=0.26):
    """X3 - group the kit's loose detail parts into panels by proximity.

    The kit's collections do not separate them - one holds eighty objects of
    four different kinds - so the grouping has to come from where the parts
    actually sit. Single-link clustering on XY: parts of one panel touch, and
    panels sit half a metre apart.
    """
    pts = []
    for o in objs:
        b = [o.matrix_world @ Vector(c) for c in o.bound_box]
        pts.append(Vector((sum(p.x for p in b) / 8, sum(p.y for p in b) / 8,
                           sum(p.z for p in b) / 8)))
    parent = list(range(len(objs)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for i in range(len(objs)):
        for j in range(i + 1, len(objs)):
            if (math.hypot(pts[i].x - pts[j].x, pts[i].y - pts[j].y) <= link
                    and abs(pts[i].z - pts[j].z) <= 1.2):
                parent[find(i)] = find(j)
    groups = {}
    for i, o in enumerate(objs):
        groups.setdefault(find(i), []).append(o)

    panels = []
    for g in groups.values():
        if len(g) < 2:
            continue
        lo, hi = bl.bbox(g)
        panels.append(dict(objs=g, w=hi.x - lo.x, d=hi.y - lo.y, h=hi.z - lo.z))
    panels.sort(key=lambda p: (round(p["w"], 3), round(p["h"], 3)))
    return panels


# Arcades were built here and removed at the user's request. `nomad_rect` no
# longer generates arches of any kind; the kit has no arch part either, so
# nothing in the settlement is arched.

# ------------------------------------------------------------------- plans
def plan_footprint(cells, k):
    """The storey's outer rectangle, in world metres, for a plan scaled by k."""
    xs, ys = [], []
    for kind, dx, dy in cells:
        _n, w, d, _h = BLOCKS[kind]
        xs += [(dx - w * 0.5) * k, (dx + w * 0.5) * k]
        ys += [(dy - d * 0.5) * k, (dy + d * 0.5) * k]
    return min(xs), max(xs), min(ys), max(ys)



def cell_rect(cell, k):
    """One block's own footprint: x0, x1, y0, y1."""
    kind, dx, dy = cell
    _n, w, d, _h = BLOCKS[kind]
    return (dx * k - w * k * 0.5, dx * k + w * k * 0.5,
            dy * k - d * k * 0.5, dy * k + d * k * 0.5)


def cell_covered(cell, cells_above, k):
    """X7 - is there a block sitting on top of this one?

    A block with nothing above it needs its own roof; without this an L-plan's
    wing was left open and the storey above's roof was stretched across the
    notch, hanging over thin air.
    """
    x0, x1, y0, y1 = cell_rect(cell, k)
    cx, cy = (x0 + x1) * 0.5, (y0 + y1) * 0.5
    for c in cells_above:
        a0, a1, b0, b1 = cell_rect(c, k)
        if a0 - 0.08 <= cx <= a1 + 0.08 and b0 - 0.08 <= cy <= b1 + 0.08:
            return True
    return False


def cell_faces(cells, k, z0, z1):
    """X7 - the outward faces of every block, internal ones dropped.

    Faces used to come from the plan's outer rectangle, which is a lie for any
    plan that is not a single box: on an L it put walls, windows and trim
    across the notch, over nothing.
    """
    out = []
    for cell in cells:
        x0, x1, y0, y1 = cell_rect(cell, k)
        cand = [
            (Vector((0, -1, 0)), Vector(((x0 + x1) * 0.5, y0, 0)), x1 - x0, 0.0),
            (Vector((1, 0, 0)), Vector((x1, (y0 + y1) * 0.5, 0)), y1 - y0,
             math.pi * 0.5),
            (Vector((0, 1, 0)), Vector(((x0 + x1) * 0.5, y1, 0)), x1 - x0,
             math.pi),
            (Vector((-1, 0, 0)), Vector((x0, (y0 + y1) * 0.5, 0)), y1 - y0,
             -math.pi * 0.5),
        ]
        for n, c, w, alpha in cand:
            probe = c + n * 0.12
            internal = False
            for other in cells:
                if other is cell:
                    continue
                a0, a1, b0, b1 = cell_rect(other, k)
                if a0 <= probe.x <= a1 and b0 <= probe.y <= b1:
                    internal = True
                    break
            if not internal:
                out.append(dict(n=n, c=c, w=w, alpha=alpha, z0=z0, z1=z1,
                                used=[]))
    return out

def storey_height(cells, k):
    return max(BLOCKS[c[0]][3] for c in cells) * k


def faces_of(cells, k):
    """The four outward faces of a storey: (normal, offset, centre, width).

    Openings and details go on these. A plan made of several blocks is treated
    as its outer rectangle, which is what the eye reads anyway and what keeps a
    detail from landing in the notch of an L.
    """
    x0, x1, y0, y1 = plan_footprint(cells, k)
    return [
        (Vector((0, -1, 0)), Vector(((x0 + x1) * 0.5, y0, 0)), x1 - x0, 0.0),
        (Vector((1, 0, 0)), Vector((x1, (y0 + y1) * 0.5, 0)), y1 - y0, math.pi * 0.5),
        (Vector((0, 1, 0)), Vector(((x0 + x1) * 0.5, y1, 0)), x1 - x0, math.pi),
        (Vector((-1, 0, 0)), Vector((x0, (y0 + y1) * 0.5, 0)), y1 - y0, -math.pi * 0.5),
    ]
