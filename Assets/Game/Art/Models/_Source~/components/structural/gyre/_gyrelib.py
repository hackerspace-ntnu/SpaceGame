"""Shared geometry for the Gyre family (the ancient ruin POI).

Gyre is carved, continuous architecture: parabolic arches, stepped round
reveals, grown columns, rib fans and flush metal inlays. Every shape in the
family is one of a few constructions, and they live here so that every arch,
oculus and socket agrees on profile, step size and groove spacing:

- `arch_curve` / `offset_curve`  the parabolic arch line and its offsets
- `sweep_open` / `sweep_closed`  a closed cross-section swept along an
                                 open curve (arches) or a closed one (rings)
- `revolve`                      a profile turned about Z (columns, rods)
- `frame_sweep`                  a section carried along a 3-D path (ribs)

Design rules (ancient-ruin-poi/06-design-language.md): no semicircles, no
blocks, incised lines as 0.1 m grooves in pairs 0.3 m apart, metal inlays
recessed a few millimetres so nothing is coplanar with the stone.

Import from a component script with:

    import os, sys
    sys.path.insert(0, <folder holding this file>)
    from _gyrelib import *
"""

import math
import os
import sys

import bpy  # first: under the standalone bpy module, bmesh needs it loaded
import bmesh
from mathutils import Matrix, Vector


def _find_lib_root():
    d = os.path.dirname(os.path.abspath(__file__))
    while d != os.path.dirname(d):
        if os.path.exists(os.path.join(d, "_buildlib.py")):
            return d
        d = os.path.dirname(d)
    raise SystemExit("Could not find _buildlib.py above %s" % __file__)


LIB_ROOT = _find_lib_root()
if LIB_ROOT not in sys.path:
    sys.path.insert(0, LIB_ROOT)

from _buildlib import (Part, collection, link_materials, report, save,  # noqa: E402
                       start, tri_count)

# ---------------------------------------------------------------------------
# Palette mapping. Gyre adds nothing to the palette.
# ---------------------------------------------------------------------------

STONE = "Mat_Stone_Sandstone_Pale"      # carved sandstone, all shells/inserts
STONE_TINT = "Mat_Stone_Sandstone_Tan"  # deeper reveals, tint variant
METAL = "Mat_Metal_Steel_Dark"          # mechanisms, inlays
POLISH = "Mat_Metal_Steel_Worn"         # worn lips and edges, moving faces
GLOW = "Mat_Emissive_Amber"             # core light (not used by the kit)

GROOVE_W = 0.10     # incised line width
GROOVE_D = 0.06     # incised line depth
GROOVE_GAP = 0.30   # spacing between the two lines of a pair
INLAY_GAP = 0.005   # inlays sit this far below / inside the stone

ARCH_POWER = 0.42   # 0.5 is a true parabola; lower blunts the crown so deep
                    # reveals can step inward without folding over the apex


# ---------------------------------------------------------------------------
# 2-D curves in the (x, z) plane
# ---------------------------------------------------------------------------

def _resample(pts, n, bend=0.0):
    """Resample a dense polyline to n segments.

    With bend = 0 the segments are equal in arc length. A positive `bend`
    spends extra segments where the curve turns (cost per turned radian, in
    metres), so a crown stays round without paying for density on the legs.
    """
    d = [0.0]
    for i, (a, b) in enumerate(zip(pts, pts[1:])):
        turn = 0.0
        if bend and 0 < i < len(pts) - 1:
            t0 = (pts[i] - pts[i - 1])
            t1 = (b - a)
            if t0.length > 1e-12 and t1.length > 1e-12:
                turn = t0.angle(t1)
        d.append(d[-1] + (b - a).length + bend * turn)
    total = d[-1]
    out, j = [], 0
    for i in range(n + 1):
        t = total * i / n
        while j < len(d) - 2 and d[j + 1] < t:
            j += 1
        seg = d[j + 1] - d[j]
        f = 0.0 if seg < 1e-12 else (t - d[j]) / seg
        out.append(pts[j].lerp(pts[j + 1], f))
    return out


def arch_curve(width, height, n=24, power=ARCH_POWER, bend=None):
    """Parabolic arch from the left foot over the crown to the right foot.

    x = +-(w/2) * (1 - z/h)^power, so the legs lean inward from the floor and
    the crown is round - the opposite of a Roman arch's vertical piers and
    semicircle.
    """
    # Sampled by u = x / (w/2) rather than by height: the crown is nearly
    # flat in z, so height steps would leave it as one long facet.
    dense = []
    steps = 2000
    for i in range(steps + 1):
        u = -1.0 + 2.0 * i / steps
        z = height * (1.0 - abs(u) ** (1.0 / power))
        dense.append(Vector((u * width / 2.0, z)))
    return _resample(dense, n, width * 0.9 if bend is None else bend)


def catenary_curve(width, height, n=24, bend=None):
    """Catenary arch, left foot over the crown to the right foot.

    z = h - a (cosh(x / a) - 1), with `a` solved so the feet land at +-w/2.
    The crown is rounder than a parabola of the same proportions and the legs
    lean less - Gaudi's line rather than a lancet, so a tall narrow opening
    never reads as gothic.
    """
    half = width / 2.0
    lo, hi = 1e-3, 1e4
    for _ in range(200):           # h(a) falls as a grows: bisect
        a = (lo + hi) / 2.0
        if a * (math.cosh(half / a) - 1.0) > height:
            lo = a
        else:
            hi = a
    dense = []
    steps = 2000
    for i in range(steps + 1):
        x = -half + width * i / steps
        dense.append(Vector((x, height - a * (math.cosh(x / a) - 1.0))))
    dense[0] = Vector((-half, 0.0))
    dense[-1] = Vector((half, 0.0))
    return _resample(dense, n, width * 0.9 if bend is None else bend)


def _normals(pts, closed):
    """Inward normals: for an arch running left-over-right this points into
    the opening; for a counter-clockwise circle it points to the centre."""
    out = []
    n = len(pts)
    for i in range(n):
        a = pts[(i - 1) % n] if closed else pts[max(i - 1, 0)]
        b = pts[(i + 1) % n] if closed else pts[min(i + 1, n - 1)]
        t = (b - a).normalized()
        out.append(Vector((t.y, -t.x)))
    return out


def offset_curve(pts, d, closed=False):
    """Offset a curve by d along its inward normal (d > 0 narrows an opening).

    For an open arch both feet are slid back onto z = 0 along their local
    tangent, so every offset still stands on the floor.
    """
    nrm = _normals(pts, closed)
    out = [p + nv * d for p, nv in zip(pts, nrm)]
    if not closed:
        for end, prev in ((0, 1), (-1, -2)):
            a, b = out[prev], out[end]
            if abs(b.y - a.y) > 1e-9:
                f = (0.0 - a.y) / (b.y - a.y)
                out[end] = a.lerp(b, f)
            else:
                out[end] = Vector((b.x, 0.0))
    return out


def circle(radius, n=32, centre=(0.0, 0.0)):
    """Counter-clockwise circle in (x, z)."""
    c = Vector(centre)
    return [c + Vector((math.cos(2 * math.pi * i / n),
                        math.sin(2 * math.pi * i / n))) * radius
            for i in range(n)]


# ---------------------------------------------------------------------------
# Reveal cross-sections: a list of (inset, depth) pairs, closed polygon
# ---------------------------------------------------------------------------

def reveal_section(margin, steps, wall, step_in, grooves=True, lip=False,
                   soften=0.10):
    """Cross-section of a stepped reveal, as (inset, depth) pairs.

    inset 0 is the opening's front edge; negative insets lie on the wall face
    outside it, out to `-margin` where the insert meets the shell (that seam
    falls on a line, by design). Depth runs from the wall face (0) to the back
    of the wall (`wall`).

    `steps` is how many times the reveal steps inward; each step is `step_in`
    deep and the step depths divide the wall evenly. With `lip`, the innermost
    step's corner is notched so a metal lip can sit in it without touching.
    """
    sec = [(-margin, 0.0)]
    if grooves:
        # an incised pair on the wall face, 0.2 and 0.5 m outside the edge
        for c in (-0.5 - GROOVE_W / 2, -0.2 - GROOVE_W / 2):
            if c - 0.05 > -margin:
                sec += [(c, 0.0), (c, GROOVE_D),
                        (c + GROOVE_W, GROOVE_D), (c + GROOVE_W, 0.0)]
    sec += [(-soften, 0.0), (0.0, soften)]
    depth_per = wall / (steps + 1)
    inset = 0.0
    for k in range(1, steps + 1):
        d = depth_per * k
        sec.append((inset, d))
        new = inset + step_in
        if lip and k == steps:
            sec += [(new - 0.15, d), (new - 0.15, d + 0.15), (new, d + 0.15)]
        else:
            sec.append((new, d))
        inset = new
    sec += [(inset, wall), (-margin, wall)]
    return sec


def lip_section(inset, depth, size=0.15, gap=INLAY_GAP):
    """The metal lip that fills the notch `reveal_section(lip=True)` cuts."""
    a, b = inset - size + gap, inset - gap
    c, d = depth + gap, depth + size - gap
    return [(a, c), (b, c), (b, d), (a, d)]


# ---------------------------------------------------------------------------
# Sweeps
# ---------------------------------------------------------------------------

def sweep(part, curve_fn, section, closed_curve, mat=0, mats=None,
          smooth=True):
    """Sweep a closed `section` of (inset, depth) along a curve.

    `curve_fn(inset, depth)` returns the (x, z) points of the curve offset by
    that inset (it may also depend on depth, for eccentric oculi). Faces get
    `mats[k]` per section edge k if given, else `mat`. Open curves are capped
    at both ends.
    """
    bm2 = bmesh.new()
    cols = [[bm2.verts.new((p.x, depth, p.y)) for p in curve_fn(inset, depth)]
            for inset, depth in section]
    nk = len(section)
    ni = len(cols[0])
    seg = ni if closed_curve else ni - 1
    faces_by_k = []
    for k in range(nk):
        a, b = cols[k], cols[(k + 1) % nk]
        fk = []
        for i in range(seg):
            j = (i + 1) % ni
            fk.append(bm2.faces.new((a[i], a[j], b[j], b[i])))
        faces_by_k.append(fk)
    if not closed_curve:
        bm2.faces.new([cols[k][0] for k in range(nk)])
        bm2.faces.new([cols[k][-1] for k in reversed(range(nk))])
    for k, fk in enumerate(faces_by_k):
        for f in fk:
            f.material_index = (mats[k] if mats else mat)
            f.smooth = smooth
    bmesh.ops.recalc_face_normals(bm2, faces=bm2.faces)
    mesh = bpy.data.meshes.new("_scratch")
    bm2.to_mesh(mesh)
    bm2.free()
    before = set(part.bm.faces)
    part.bm.from_mesh(mesh)
    bpy.data.meshes.remove(mesh)
    faces = [f for f in part.bm.faces if f not in before]
    flat_shade_steps(faces, 40.0)
    return faces


def flat_shade_steps(faces, limit_deg=40.0):
    """Mark hard edges sharp, so step risers stay crisp while curved soffits
    and barrels shade smooth (faces stay smooth; only the edges split)."""
    for f in faces:
        for e in f.edges:
            if (len(e.link_faces) != 2
                    or e.calc_face_angle(0.0) > math.radians(limit_deg)):
                e.smooth = False


def revolve(part, profile, seg=20, mat=0, mats=None, cap_bottom=True,
            cap_top=True, axis='Z'):
    """Turn a profile of (radius, height) pairs about an axis.

    `mats[k]` colours the band between profile points k and k+1. The caps are
    left flat. axis 'Y' lays the solid along +Y (rods).
    """
    bm2 = bmesh.new()
    rings = []
    for r, h in profile:
        ring = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            x, y = r * math.cos(a), r * math.sin(a)
            co = (x, y, h) if axis == 'Z' else (x, h, y)
            ring.append(bm2.verts.new(co))
        rings.append(ring)
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(seg):
            j = (i + 1) % seg
            f = bm2.faces.new((a[i], a[j], b[j], b[i]))
            f.material_index = mats[k] if mats else mat
            f.smooth = True
    if cap_bottom:
        f = bm2.faces.new(list(reversed(rings[0])))
        f.material_index = mats[0] if mats else mat
    if cap_top:
        f = bm2.faces.new(rings[-1])
        f.material_index = mats[-1] if mats else mat
    bmesh.ops.recalc_face_normals(bm2, faces=bm2.faces)
    mesh = bpy.data.meshes.new("_scratch")
    bm2.to_mesh(mesh)
    bm2.free()
    before = set(part.bm.faces)
    part.bm.from_mesh(mesh)
    bpy.data.meshes.remove(mesh)
    faces = [f for f in part.bm.faces if f not in before]
    # crisp where the profile turns sharply (step edges), smooth elsewhere
    flat_shade_steps(faces, 50.0)
    return faces


def frame_sweep(part, path, section_at, mat=0, cap=True, up=(0, 0, 1),
                side=None, closed=False):
    """Carry a 2-D section along a 3-D polyline.

    `section_at(t)` returns the section as (u, v) points for t in 0..1, where
    u runs along the path's side vector and v along its normal. The frame is
    built from the path tangent and `up`, so a rib rising out of a column
    keeps its sides vertical.
    """
    bm2 = bmesh.new()
    path = [Vector(p) for p in path]
    if closed and (path[0] - path[-1]).length < 1e-6:
        path = path[:-1]
    n = len(path)
    rings = []
    for i, p in enumerate(path):
        if closed:
            a, b = path[(i - 1) % n], path[(i + 1) % n]
        else:
            a = path[max(i - 1, 0)]
            b = path[min(i + 1, n - 1)]
        t = (b - a).normalized()
        if side is not None:
            sd = Vector(side)
            sd = (sd - t * sd.dot(t)).normalized()
        else:
            sd = t.cross(Vector(up))
            if sd.length < 1e-6:
                sd = t.cross(Vector((1, 0, 0)))
            sd.normalize()
        nrm = sd.cross(t).normalized()
        sec = section_at(i / max(n - 1, 1))
        rings.append([bm2.verts.new(p + sd * u + nrm * v) for u, v in sec])
    m = len(rings[0])
    pairs = list(zip(rings, rings[1:]))
    if closed:
        pairs.append((rings[-1], rings[0]))
    for a, b in pairs:
        for i in range(m):
            j = (i + 1) % m
            bm2.faces.new((a[i], a[j], b[j], b[i]))
    if cap and not closed:
        bm2.faces.new(rings[0])
        bm2.faces.new(list(reversed(rings[-1])))
    for f in bm2.faces:
        f.material_index = mat
        f.smooth = True
    bmesh.ops.recalc_face_normals(bm2, faces=bm2.faces)
    mesh = bpy.data.meshes.new("_scratch")
    bm2.to_mesh(mesh)
    bm2.free()
    before = set(part.bm.faces)
    part.bm.from_mesh(mesh)
    bpy.data.meshes.remove(mesh)
    faces = [f for f in part.bm.faces if f not in before]
    flat_shade_steps(faces, 50.0)
    return faces


def chamfer_rect(w, h, c):
    """Rectangle w x h centred on the origin with chamfered corners."""
    x, y = w / 2.0, h / 2.0
    return [(-x + c, -y), (x - c, -y), (x, -y + c), (x, y - c),
            (x - c, y), (-x + c, y), (-x, y - c), (-x, -y + c)]


# ---------------------------------------------------------------------------
# File helpers
# ---------------------------------------------------------------------------

def out_path(script_file, default_name):
    """`--out <path>` after `--` if given, else next to the script."""
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--out" in argv:
        return os.path.abspath(argv[argv.index("--out") + 1])
    return os.path.join(os.path.dirname(os.path.abspath(script_file)),
                        default_name)


def gyre_save(out_path):
    """`_buildlib.save`, except a mesh shared by linked duplicates keeps the
    name it was built with instead of taking the last duplicate's name."""
    for o in bpy.data.objects:
        if o.data is not None and getattr(o.data, "users", 1) == 1:
            o.data.name = o.name
        if len(o.name) > 4 and o.name[-4] == '.' and o.name[-3:].isdigit():
            raise SystemExit("Auto-suffixed object name reached save: %s" % o.name)
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out_path))
    print("Wrote %s" % out_path)


def instance(src, name, coll, loc=(0, 0, 0), rot=(0, 0, 0)):
    """A linked duplicate: shares the mesh, so a family of openings costs one
    mesh per size."""
    o = bpy.data.objects.new(name, src.data)
    o.location = loc
    o.rotation_euler = rot
    coll.objects.link(o)
    return o
