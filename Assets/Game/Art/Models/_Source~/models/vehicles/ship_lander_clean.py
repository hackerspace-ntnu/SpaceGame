"""ship_lander_clean.blend — the hand-built interior plus every hull piece
REBUILT as clean geometry.

`ship_lander.blend` holds the example hull sliced into components, but those
carry the Tripo mesh's own quality: irregular triangles, slivers, fanned caps.
This pass keeps the *shape* of every piece and throws the *mesh* away:

- each Hull_* piece is measured at stations along its main axis (the sliced
  mesh is cut at each station and the cut loop is sampled radially) and
  rebuilt as a quad loft through those profiles — smooth, even, closed;
- each Detail_* greeble is replaced by a bevelled box or a cylinder fitted to
  its bounds, whichever its proportions say it is;
- everything gets palette materials.

Profiles are convex support polygons (8 facets = a 45-degree chamfered
rectangle, the example's own design language), so a concavity within one
section is filled — that is the price of one clean loft per piece.

    blender --background --python ship_lander_clean.py -- --out ship_lander_clean.blend
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
import _buildlib as B  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
SLICED_FILE = os.path.join(HERE, "ship_lander.blend")

END_INSET = 0.02          # metres in from each end for the first/last station
BOX_BEVEL = 0.03          # metres, greeble box edge softening

# clean piece -> (source sliced piece, loft axis, material, facets per section,
#                 station spacing, splits, point filter)
# 8 facets = a 45-degree chamfered box section, the design language of the
# example; 16 only where the original is genuinely rounded, with wider
# stations so the loft follows the curve rather than the mesh noise.
# `splits` are stations where the piece steps sharply (the tail boom leaving
# the full-width aft fuselage): the loft is broken there into separate
# objects so it does not slope diagonally across the step.
# A point filter carves one sliced source into several clean pieces: the
# sliced "fin" is the port blade plus the tail's roof cap, which a single
# convex loft would smear into a slab.
FIN_BLADE_X = -2.4        # metres: everything port of this is the fin blade
HULL_PIECES = {
    "Clean_Nose":         ("Hull_Nose", 'Y', "hull", 16, 1.5, (), None),
    "Clean_Canopy":       ("Hull_Canopy", 'Y', "glass", 16, 2.0, (), None),
    "Clean_Cockpit_Hull": ("Hull_Cockpit_Hull", 'Y', "hull", 8, 1.0, (), None),
    "Clean_MidBody":      ("Hull_MidBody", 'Y', "hull", 8, 1.0, (), None),
    "Clean_AftBody":      ("Hull_AftBody", 'Y', "hull", 8, 1.0, (), None),
    "Clean_TailBoom":     ("Hull_TailBoom", 'Y', "hull", 8, 1.0, (9.3,), None),
    "Clean_Fin":          ("Hull_Fin", 'Z', "hull", 8, 0.7, (), lambda p: p.x < FIN_BLADE_X),
    "Clean_TailSpine":    ("Hull_Fin", 'Y', "hull", 8, 1.0, (), lambda p: p.x >= FIN_BLADE_X),
    "Clean_Wing_L":       ("Hull_Wing_L", 'X', "hull", 8, 1.0, (), None),
    "Clean_Wing_R":       ("Hull_Wing_R", 'X', "hull", 8, 1.0, (), None),
    "Clean_Wingtip_L":    ("Hull_Wingtip_L", 'Y', "engine", 8, 1.0, (), None),
    "Clean_Wingtip_R":    ("Hull_Wingtip_R", 'Y', "engine", 8, 1.0, (), None),
    "Clean_SidePod_L":    ("Hull_SidePod_L", 'Y', "engine", 8, 1.0, (), None),
    "Clean_SidePod_R":    ("Hull_SidePod_R", 'Y', "engine", 8, 1.0, (), None),
    "Clean_Nacelle_L":    ("Hull_Nacelle_L", 'Y', "engine", 8, 1.0, (), None),
    "Clean_Nacelle_R":    ("Hull_Nacelle_R", 'Y', "engine", 8, 1.0, (), None),
}
MATERIALS = {
    "hull":   "Mat_Paint_Hull_Bleached",
    "glass":  "Mat_Glass_Canopy_Tinted",
    "engine": "Mat_Metal_Steel_Dark",
    "detail": "Mat_Metal_Steel_Worn",
}
AX = {'X': 0, 'Y': 1, 'Z': 2}


# --- measuring the sliced pieces --------------------------------------------

def cut_points(obj, axis, value):
    """Points along the loop where the plane axis=value cuts the mesh."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    no = Vector((0, 0, 0))
    no[AX[axis]] = 1.0
    res = bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:],
                                 plane_co=no * value, plane_no=no, dist=1e-6)
    pts = []
    for g in res['geom_cut']:
        if isinstance(g, bmesh.types.BMEdge):
            a, b = g.verts[0].co.copy(), g.verts[1].co.copy()
            for t in (0.0, 0.25, 0.5, 0.75, 1.0):
                pts.append(a.lerp(b, t))
    bm.free()
    return pts


def support_profile(pts, axis, n):
    """Tight convex outline with n facets: for each of n directions take the
    farthest point (the support value), then intersect neighbouring support
    lines. With n = 8 this is exactly a chamfered rectangle hugging the
    section — the hard-surface language of the example, minus its noise."""
    u_i, v_i = [i for i in range(3) if i != AX[axis]]
    uv = [(p[u_i], p[v_i]) for p in pts]
    dirs = [(math.cos(2 * math.pi * k / n), math.sin(2 * math.pi * k / n)) for k in range(n)]
    h = [max(u * du + v * dv for u, v in uv) for du, dv in dirs]
    poly = []
    for k in range(n):
        (a1, b1), c1 = dirs[k], h[k]
        (a2, b2), c2 = dirs[(k + 1) % n], h[(k + 1) % n]
        det = a1 * b2 - a2 * b1
        poly.append(((c1 * b2 - c2 * b1) / det, (a1 * c2 - a2 * c1) / det))
    return poly


def measure(obj, axis, facets, spacing, splits, filt):
    """Profiles at evenly spaced stations, returned as one list per split
    segment; each split station is measured twice, just either side of it, so
    neighbouring segments end on the step rather than sloping across it."""
    a = AX[axis]
    verts = [v.co for v in obj.data.vertices if filt is None or filt(v.co)]
    lo = min(v[a] for v in verts) + END_INSET
    hi = max(v[a] for v in verts) - END_INSET
    edges = [lo] + sorted(splits) + [hi]
    segments = []
    for s0, s1 in zip(edges, edges[1:]):
        count = max(3, int(round((s1 - s0) / spacing)) + 1)
        sections = []
        for i in range(count):
            w = s0 + (s1 - s0) * i / (count - 1)
            pts = cut_points(obj, axis, min(max(w, s0 + 1e-3), s1 - 1e-3))
            if filt is not None:
                pts = [p for p in pts if filt(p)]
            if len(pts) < 6:
                continue
            sections.append((w, support_profile(pts, axis, facets)))
        segments.append(sections)
    return segments


def bounds(obj):
    xs = [v.co for v in obj.data.vertices]
    lo = Vector([min(c[i] for c in xs) for i in range(3)])
    hi = Vector([max(c[i] for c in xs) for i in range(3)])
    return lo, hi


# --- building clean pieces ---------------------------------------------------

def build_loft(name, sections, axis, material, coll, facets):
    part = B.Part([material])
    faces = part.loft(sections, axis=axis)
    # chamfered sections are hard-surface: flat facets, no smoothing
    part.shade(faces, smooth=facets > 8)
    return part.finish(name, coll)


def build_detail(name, src, material, coll):
    lo, hi = bounds(src)
    size = hi - lo
    centre = (lo + hi) / 2
    order = sorted(range(3), key=lambda i: -size[i])
    long_axis, mid, short = order
    part = B.Part([material])
    rod_like = (size[long_axis] >= 2.5 * size[mid]
                and size[short] >= 0.65 * size[mid])
    if rod_like:
        radius = (size[mid] + size[short]) / 4
        part.cyl(centre, radius, size[long_axis], axis='XYZ'[long_axis], seg=12)
    else:
        part.box(centre, size)
        part.bevel(width=min(BOX_BEVEL, min(size) * 0.3), segments=2)
    return part.finish(name, coll)


def build(out_path):
    if os.path.exists(out_path):
        raise SystemExit("Refusing to overwrite %s — the .blend is the source of truth." % out_path)
    bpy.ops.wm.open_mainfile(filepath=SLICED_FILE)

    sliced_root = bpy.data.collections["Coll_Lander_Components"]
    sliced = {}

    def walk(c):
        for o in c.objects:
            sliced[o.name] = o
        for k in c.children:
            walk(k)
    walk(sliced_root)

    mats = dict(zip(MATERIALS.keys(), B.link_materials(list(MATERIALS.values()))))
    root = B.collection("Coll_LanderClean")
    colls = {k: B.collection("Coll_LanderClean_" + k, root)
             for k in ("Fuselage", "Wings", "Pods", "Engines", "Tail", "Details")}
    coll_of = {
        "Hull_Nose": "Fuselage", "Hull_Canopy": "Fuselage", "Hull_Cockpit_Hull": "Fuselage",
        "Hull_MidBody": "Fuselage", "Hull_AftBody": "Fuselage",
        "Hull_Wing_R": "Wings", "Hull_Wing_L": "Wings", "Hull_Wingtip_R": "Wings", "Hull_Wingtip_L": "Wings",
        "Hull_SidePod_R": "Pods", "Hull_SidePod_L": "Pods",
        "Hull_Nacelle_R": "Engines", "Hull_Nacelle_L": "Engines",
        "Hull_Fin": "Tail", "Hull_TailBoom": "Tail",
    }

    print("Lofting hull pieces:")
    for clean, (src, axis, mkey, facets, spacing, splits, filt) in HULL_PIECES.items():
        segments = measure(sliced[src], axis, facets, spacing, splits, filt)
        for i, sections in enumerate(segments):
            name = clean + ("_%d" % (i + 1) if len(segments) > 1 else "")
            obj = build_loft(name, sections, axis, mats[mkey], colls[coll_of[src]], facets)
            print("  %-24s stations=%2d faces=%4d" % (obj.name, len(sections), len(obj.data.polygons)))

    print("Fitting details:")
    for name, src in sorted(sliced.items()):
        if not name.startswith("Detail_"):
            continue
        obj = build_detail(name.replace("Detail_", "CleanDetail_"), src, mats["detail"], colls["Details"])
        print("  %-28s faces=%4d" % (obj.name, len(obj.data.polygons)))

    # The sliced pieces were the measuring jig; they do not ship in this file.
    for o in list(sliced.values()):
        mesh = o.data
        bpy.data.objects.remove(o)
        bpy.data.meshes.remove(mesh)

    def drop(c):
        for k in list(c.children):
            drop(k)
        bpy.data.collections.remove(c)
    drop(sliced_root)
    if "Mat_Lander_Example" in bpy.data.materials:
        bpy.data.materials.remove(bpy.data.materials["Mat_Lander_Example"])

    for o in bpy.data.objects:
        if o.name.startswith("Clean"):
            o.data.name = o.name
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out_path))
    print("Wrote %s" % out_path)


if __name__ == "__main__":
    build(B.parse_out())
