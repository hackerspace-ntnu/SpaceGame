"""Shared helpers for building Collection 2's hull plating in a live session (MCP).

A / | \\ wall is a target profile (z -> outboard x) sampled at stations along Y; each station is pulled
inboard of whatever is under the overhang there (fenders, track, ladders) and meets it in a ledge, so
the hull overhangs the tracks where there is room and tucks inside them where there is not. Plates are
filled with a constrained triangulation (polyfill mis-fills the ledge notches), and holes are cut with a
boolean on one part only, never a union of parts.

Used by dune_barge_c2_castle.py. dune_barge_c2_rear_hull.py predates this module and is kept as the
record of how the main hull was made.
"""
import math

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def mat(base):
    for m in bpy.data.materials:
        if m.name == base or m.name.startswith(base + "."):
            return m
    raise RuntimeError("palette material missing: " + base)


def remove(names):
    for n in names:
        o = bpy.data.objects.get(n)
        if o is not None:
            me = o.data
            bpy.data.objects.remove(o)
            if me is not None and me.users == 0:
                bpy.data.meshes.remove(me)


def obj(name, me, material, into):
    remove([name])
    old = bpy.data.meshes.get(name)
    if old is not None and old.users == 0:
        bpy.data.meshes.remove(old)
    me.name = name
    me.materials.clear()
    me.materials.append(material)
    o = bpy.data.objects.new(name, me)
    into.objects.link(o)
    return o


def box_me(lo, hi):
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    for v in b.verts:
        v.co = Vector(((lo[i] + hi[i]) / 2 + v.co[i] * (hi[i] - lo[i]) for i in range(3)))
    me = bpy.data.meshes.new("tmp")
    b.to_mesh(me)
    b.free()
    return me


def slab(outline, y0, y1, hole=None):
    """Flat plate from a closed xz outline (concave is fine), optional hole, extruded along y."""
    b = bmesh.new()
    loops = []
    for pts in [outline] + ([hole] if hole else []):
        vs = [b.verts.new((x, y0, z)) for x, z in pts]
        loops.append((vs, [b.edges.new((vs[i], vs[(i + 1) % len(vs)])) for i in range(len(vs))]))
    tris = [g for g in bmesh.ops.triangle_fill(b, use_beauty=True, use_dissolve=False,
                                               edges=[e for _, es in loops for e in es])["geom"]
            if isinstance(g, bmesh.types.BMFace)]
    back = {v: b.verts.new((v.co.x, y1, v.co.z)) for v in list(b.verts)}
    for f in tris:
        b.faces.new([back[v] for v in reversed(f.verts)])
    for vs, _ in loops:
        for i in range(len(vs)):
            j = (i + 1) % len(vs)
            b.faces.new((vs[i], vs[j], back[vs[j]], back[vs[i]]))
    bmesh.ops.recalc_face_normals(b, faces=list(b.faces))
    me = bpy.data.meshes.new("tmp")
    b.to_mesh(me)
    b.free()
    return me


def cut(o, cutter_me):
    """Boolean a cutter out of one part."""
    cutter = bpy.data.objects.new("Tmp_Cutter", cutter_me)
    o.users_collection[0].objects.link(cutter)
    m = o.modifiers.new("Cut", 'BOOLEAN')
    m.operation = 'DIFFERENCE'
    m.solver = 'EXACT'
    m.object = cutter
    bpy.context.view_layer.update()
    new = bpy.data.meshes.new_from_object(o.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    o.modifiers.remove(m)
    old = o.data
    name = old.name
    mats = list(old.materials)
    o.data = new
    bpy.data.meshes.remove(old)
    new.name = name
    new.materials.clear()
    for mt in mats:                      # new_from_object drops the slots
        new.materials.append(mt)
    bpy.data.objects.remove(cutter)
    bpy.data.meshes.remove(cutter_me)


def target_x(target, z):
    """Outboard x of a profile [(z, x), ...] at height z."""
    for (z0, x0), (z1, x1) in zip(target, target[1:]):
        if z0 <= z <= z1:
            return x0 + (x1 - x0) * (z - z0) / (z1 - z0)
    return target[0][1] if z < target[0][0] else target[-1][1]


def side_bvh(objects, sign):
    """One BVH over the obstacles on one side of the hull (x * sign > 0)."""
    verts, polys, off = [], [], 0
    for o in objects:
        W = o.matrix_world
        if (W @ Vector(o.bound_box[0])).x * sign < 0 and (W @ Vector(o.bound_box[6])).x * sign < 0:
            continue
        verts += [W @ v.co for v in o.data.vertices]
        polys += [[i + off for i in p.vertices] for p in o.data.polygons]
        off += len(o.data.vertices)
    return BVHTree.FromPolygons(verts, polys)


def fit_profile(bvh, sign, y, zs, target, station_step, y_span, roof_top, clear=0.03, floor_bottom=0.5):
    """The target profile at station y, tucked inside whatever is under the overhang, with a ledge."""
    wide = max(x for _, x in target)
    y_probe = [min(max(y + station_step * d / 3, y_span[0] - 0.1), y_span[1] + 0.1) for d in range(-3, 4)]
    xs = [2.0 + (wide + clear - 2.0) * i / 23 for i in range(24)]
    cap, inner = None, wide
    for yy in y_probe:
        for xx in xs:
            hit = bvh.ray_cast(Vector((sign * xx, yy, roof_top + 1)), Vector((0, 0, -1)), roof_top + 3)[0]
            if hit is not None and hit.z < roof_top - 0.3:
                cap = hit.z if cap is None else max(cap, hit.z)
                inner = min(inner, xx - 0.02 - clear)
    if cap is not None:
        z = floor_bottom
        while z <= cap + clear:
            for yy in y_probe:
                hit = bvh.ray_cast(Vector((sign * 1.5, yy, z)), Vector((sign, 0, 0)), 4)[0]
                if hit is not None:
                    inner = min(inner, abs(hit.x) - clear)
            z += 0.03
    ledge = None if cap is None else cap + clear
    pts = []
    for z in zs:
        x = target_x(target, z)
        if ledge is not None and z <= ledge:
            x = min(x, inner)
        pts.append([max(x, 1.90), z])
    if ledge is not None:
        below = [k for k, (_, z) in enumerate(pts) if z <= ledge]
        above = [k for k, (_, z) in enumerate(pts) if z > ledge]
        if below and above and above[0] < len(pts) - 1:
            pts[below[-1]][1] = ledge
            pts[above[0]][1] = ledge + 0.01
            pts[above[0]][0] = target_x(target, ledge + 0.01)
    return [tuple(p) for p in pts]


def offset_in(poly, t, mitre_max=2.0):
    """Offset an xz polyline (x outboard, bottom to top) by t inboard, mitre capped."""
    def nrm(a, b):
        dx, dz = b[0] - a[0], b[1] - a[1]
        L = math.hypot(dx, dz) or 1e-9
        n = (-dz / L, dx / L)
        return n if n[0] < 0 else (-n[0], -n[1])
    segs = [nrm(poly[i], poly[i + 1]) for i in range(len(poly) - 1)]
    out = []
    for i, p in enumerate(poly):
        if i == 0:
            n = segs[0]
        elif i == len(poly) - 1:
            n = segs[-1]
        else:
            a, b = segs[i - 1], segs[i]
            s = (a[0] + b[0], a[1] + b[1])
            L = math.hypot(*s)
            if L < 1e-6:
                n = a
            else:
                k = min(mitre_max, 1.0 / max((a[0] * s[0] + a[1] * s[1]) / L, 1e-6))
                n = (s[0] / L * k, s[1] / L * k)
        out.append((p[0] + n[0] * t, p[1] + n[1] * t))
    return out


def wall_mesh(rows, ys, sign):
    """rows: per station, (outer, inner) xz lists. A closed plate: outer skin, inner skin, rims."""
    b = bmesh.new()
    O = [[b.verts.new((sign * x, y, z)) for x, z in outer] for y, (outer, inner) in zip(ys, rows)]
    I = [[b.verts.new((sign * x, y, z)) for x, z in inner] for y, (outer, inner) in zip(ys, rows)]
    nz = len(rows[0][0])
    for j in range(len(ys) - 1):
        for k in range(nz - 1):
            b.faces.new((O[j][k], O[j][k + 1], O[j + 1][k + 1], O[j + 1][k]))
            b.faces.new((I[j][k], I[j + 1][k], I[j + 1][k + 1], I[j][k + 1]))
        for k in (0, nz - 1):
            b.faces.new((O[j][k], O[j + 1][k], I[j + 1][k], I[j][k]))
    for j in (0, len(ys) - 1):
        for k in range(nz - 1):
            b.faces.new((O[j][k], I[j][k], I[j][k + 1], O[j][k + 1]))
    bmesh.ops.remove_doubles(b, verts=list(b.verts), dist=0.0005)
    bmesh.ops.dissolve_degenerate(b, dist=0.0005, edges=list(b.edges))
    bmesh.ops.recalc_face_normals(b, faces=list(b.faces))
    me = bpy.data.meshes.new("tmp")
    b.to_mesh(me)
    b.free()
    return me


def closed_outline(right, bottom=None):
    """A symmetric hull section from its starboard polyline (bottom to top)."""
    r = list(right)
    if bottom is not None:
        r[0] = (r[0][0], bottom)
    return r + [(-x, z) for x, z in reversed(r)]
