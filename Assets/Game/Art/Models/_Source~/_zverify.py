"""Exact z-fighting verification. Read-only. No proxies, no quantisation.

A pair z-fights only if all four hold:
  same-facing   normals within 0.999 of parallel (opposed faces occlude)
  coplanar      plane offsets within SEP metres
  overlapping   TRUE polygon intersection area, by Sutherland-Hodgman clip

That last one is the point. An axis-aligned bounding rectangle in the shared
plane is a cheap stand-in for overlap, and on diagonal geometry it lies
enormously: two parallel beams of an inclined conveyor, lying side by side and
touching nothing, report a 184 m2 'overlap' by bbox and 0.00000 m2 in truth.
Anything that measures z-fighting by bbox will invent clashes that are not there.
"""
import bpy
from mathutils import Vector
from collections import defaultdict

SEP = 0.002
MIN_AREA = 1e-3
PARALLEL = 0.999
CELL = 2.0

objs = [o for o in bpy.data.objects if o.type == 'MESH']


def islands(me):
    parent = list(range(len(me.vertices)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    for e in me.edges:
        a, b = find(e.vertices[0]), find(e.vertices[1])
        if a != b:
            parent[b] = a
    return [find(i) for i in range(len(me.vertices))]


def basis(n):
    a = Vector((1, 0, 0)) if abs(n.x) < 0.9 else Vector((0, 1, 0))
    u = n.cross(a).normalized()
    return u, n.cross(u).normalized()


def clip(sub, cl):
    """Sutherland-Hodgman: clip polygon sub against convex polygon cl."""
    out = list(sub)
    for i in range(len(cl)):
        if not out:
            return []
        a, b = cl[i], cl[(i + 1) % len(cl)]
        ex, ey = b[0] - a[0], b[1] - a[1]
        inp, out = out, []
        for j in range(len(inp)):
            c = inp[j]
            d = inp[(j - 1) % len(inp)]
            sc = ex * (c[1] - a[1]) - ey * (c[0] - a[0])
            sd = ex * (d[1] - a[1]) - ey * (d[0] - a[0])
            if sc >= 0:
                if sd < 0:
                    t = sd / (sd - sc)
                    out.append((d[0] + t * (c[0] - d[0]),
                                d[1] + t * (c[1] - d[1])))
                out.append(c)
            elif sd >= 0:
                t = sd / (sd - sc)
                out.append((d[0] + t * (c[0] - d[0]),
                            d[1] + t * (c[1] - d[1])))
    return out


def area2(p):
    s = 0.0
    for i in range(len(p)):
        a, b = p[i], p[(i + 1) % len(p)]
        s += a[0] * b[1] - b[0] * a[1]
    return abs(s) / 2


ISL = {me.name: islands(me) for me in {o.data for o in objs}}
faces = []
for ob in objs:
    mw = ob.matrix_world
    nm = mw.to_3x3().inverted_safe().transposed()
    me = ob.data
    vi = ISL[me.name]
    for poly in me.polygons:
        n = nm @ poly.normal
        if n.length < 1e-9:
            continue
        n.normalize()
        wv = [mw @ me.vertices[i].co for i in poly.vertices]
        lo = Vector((min(w.x for w in wv), min(w.y for w in wv),
                     min(w.z for w in wv)))
        hi = Vector((max(w.x for w in wv), max(w.y for w in wv),
                     max(w.z for w in wv)))
        faces.append(((ob.name, vi[poly.vertices[0]]), n, n.dot(wv[0]),
                      wv, lo, hi))

grid = defaultdict(list)
for i, f in enumerate(faces):
    # Pad by SEP: a face has zero thickness along its normal, so an unpadded
    # bin puts a face lying on a cell boundary in one cell and its coincident
    # partner in the next — missing precisely the worst clashes.
    lo = f[4] - Vector((SEP, SEP, SEP))
    hi = f[5] + Vector((SEP, SEP, SEP))
    for cx in range(int(lo.x // CELL), int(hi.x // CELL) + 1):
        for cy in range(int(lo.y // CELL), int(hi.y // CELL) + 1):
            for cz in range(int(lo.z // CELL), int(hi.z // CELL) + 1):
                grid[(cx, cy, cz)].append(i)

found = {}
seen = set()
for idxs in grid.values():
    for a in range(len(idxs)):
        i = idxs[a]
        fi = faces[i]
        for b in range(a + 1, len(idxs)):
            j = idxs[b]
            fj = faces[j]
            if fi[0] == fj[0]:
                continue
            if fi[1].dot(fj[1]) < PARALLEL:
                continue
            if abs(fi[2] - fj[2]) > SEP:
                continue
            # SEP slack, not a strict compare: a flat face has zero thickness
            # along its normal, so the boxes of two coincident faces a hair
            # apart miss by that hair, and a strict test discards exactly the
            # clashes worth finding.
            if (fi[4].x > fj[5].x + SEP or fj[4].x > fi[5].x + SEP or
                    fi[4].y > fj[5].y + SEP or fj[4].y > fi[5].y + SEP or
                    fi[4].z > fj[5].z + SEP or fj[4].z > fi[5].z + SEP):
                continue
            # Dedup only survivors. A large face spans many cells, so pairs do
            # recur — but recording every pair examined, before the filters
            # reject 99.99% of them, grows this set to hundreds of millions of
            # entries and the run dies swapping instead of finishing.
            if (i, j) in seen:
                continue
            seen.add((i, j))
            u, v = basis(fi[1])
            pa = [(w.dot(u), w.dot(v)) for w in fi[3]]
            pb = [(w.dot(u), w.dot(v)) for w in fj[3]]
            ar = area2(clip(pb, pa))
            if ar < MIN_AREA:
                continue
            k = tuple(sorted((fi[0], fj[0])))
            sep = abs(fi[2] - fj[2])
            if k not in found or ar > found[k][0]:
                found[k] = (ar, sep, fi[4])

print("=== TRUE z-fighting: same-facing, coplanar, really overlapping ===")
for k, (ar, sep, at) in sorted(found.items(), key=lambda kv: -kv[1][0])[:15]:
    print("  %8.3f m2  sep %5.3f mm  %s#%s / %s#%s  near (%.1f,%.1f,%.1f)"
          % (ar, sep * 1000, k[0][0], k[0][1], k[1][0], k[1][1],
             at.x, at.y, at.z))
print("TOTAL: %d clashing pairs, %.3f m2"
      % (len(found), sum(v[0] for v in found.values())))
coin = [v for v in found.values() if v[1] <= 0.00005]
print("of which truly COINCIDENT (<0.05mm): %d pairs, %.3f m2"
      % (len(coin), sum(v[0] for v in coin)))
