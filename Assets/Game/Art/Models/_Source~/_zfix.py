"""Remove z-fighting from a .blend by separating coplanar co-facing surfaces.

Two faces shimmer only when they are coplanar, overlap, AND point the same way.
Coplanar faces with opposing normals are mutually occluded — that is just how
solids stack — and are left alone.

Everything is measured exactly — no quantisation, no proxies:

  * Plane distance, not a bucket grid. Bucketing costs rounds of chasing
    ghosts: parts 1 mm apart straddle a cell edge, read as non-coplanar, and
    survive the fix only to reappear in a world-space check. Grids also
    disagree between local and world space; exact distances cannot, because a
    rigid transform preserves them.
  * True polygon intersection, not a bounding rectangle. On diagonal geometry
    a bbox proxy lies enormously — two beams of an inclined conveyor, side by
    side and touching nothing, "overlap" by 184 m2 by bbox and by 0.00000 m2
    in truth — and shoves apart parts that never clashed.

Every fix is a rigid translation of a whole connected island or a whole object,
by a few millimetres. Nothing is reshaped, so no face is bent, no normal
flipped, no silhouette changed. Parts are graph-coloured per axis, largest
first, so hosts stay where the modeller put them and only smaller details move;
distinct ranks put clashing parts at distinct depths, which is the whole trick.
Two details matter:

  * Colour per AXIS, not per signed normal. A part flush against one neighbour
    on its +X face and another on its -X face would otherwise ask for +eps and
    -eps, cancel, and never move — yet one translation does separate it from
    both, because neither neighbour moves with it.
  * Break ties on name. Identical instances have equal size, and an unstable
    order makes them swap ranks and chase each other eps per round forever.

Work is split so mesh instancing survives untouched: clashes inside one mesh
are intrinsic to it and are solved once per datablock in local space, so every
instance inherits the fix; clashes between objects are solved on the object
transform. No datablock is ever copied or split.

    blender --background file.blend --python _zfix.py -- --apply

Options: --apply (default is a dry run), --eps 0.005 (separation to open, m),
--sep 0.002 (closer than this counts as coplanar, m), --rounds 14. Re-running
on a clean file is a no-op, so it is safe to apply twice.

Check the result with _zverify.py. It is a separate, simpler implementation —
read-only, no spatial reasoning beyond the same broad-phase — but it is not an
independent oracle: both files reason about coplanarity the same way, and a
mistake in that shared reasoning can hide from both. The one that bit hardest
was a strict bounding-box compare, which discarded exactly the coincident
pairs it was meant to catch depending on which way a float rounded.
"""
import sys
import bpy
from mathutils import Vector
from collections import defaultdict

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
APPLY = "--apply" in argv
EPS = float(argv[argv.index("--eps") + 1]) if "--eps" in argv else 0.005
SEP = float(argv[argv.index("--sep") + 1]) if "--sep" in argv else 0.002
ROUNDS = int(argv[argv.index("--rounds") + 1]) if "--rounds" in argv else 14

# Strictly more sensitive than _zverify.py's threshold, so the fixer can never
# leave behind a clash the checker will report.
MIN_AREA = 5e-4
PARALLEL = 0.999
CELL = 2.0


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


def axis_key(n):
    """Canonical axis: n and -n map to the same key."""
    t = (round(n.x, 3), round(n.y, 3), round(n.z, 3))
    for c in t:
        if abs(c) > 1e-9:
            return tuple(-x for x in t) if c < 0 else t
    return t


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


def poly_area(p):
    s = 0.0
    for i in range(len(p)):
        a, b = p[i], p[(i + 1) % len(p)]
        s += a[0] * b[1] - b[0] * a[1]
    return abs(s) / 2


def detect(faces):
    """faces: (part, normal, d, verts, lo, hi). -> {axis: (adj, normal)}, stats.

    Faces are binned by the cells their bounding box covers, not by centroid:
    a 24 m podium face and its clash partner can have centroids far apart and
    would never meet in a centroid-only bin.
    """
    grid = defaultdict(list)
    for i, f in enumerate(faces):
        # Pad by SEP before binning. A face is flat — zero thickness along its
        # own normal — so without padding, a face lying exactly on a cell
        # boundary occupies one cell and its coincident partner, a hair to the
        # other side, occupies the next. They never share a bin and the worst
        # clashes of all, the exactly-coincident ones, are silently missed.
        lo = f[4] - Vector((SEP, SEP, SEP))
        hi = f[5] + Vector((SEP, SEP, SEP))
        for cx in range(int(lo.x // CELL), int(hi.x // CELL) + 1):
            for cy in range(int(lo.y // CELL), int(hi.y // CELL) + 1):
                for cz in range(int(lo.z // CELL), int(hi.z // CELL) + 1):
                    grid[(cx, cy, cz)].append(i)

    axes = defaultdict(lambda: (defaultdict(set), None))
    seen = set()
    pairs = 0
    area = 0.0
    for idxs in grid.values():
        for a in range(len(idxs)):
            i = idxs[a]
            fi = faces[i]
            for b in range(a + 1, len(idxs)):
                j = idxs[b]
                if (i, j) in seen:
                    continue
                fj = faces[j]
                if fi[0] == fj[0]:
                    continue
                if fi[1].dot(fj[1]) < PARALLEL:
                    continue
                if abs(fi[2] - fj[2]) > SEP:
                    continue
                # SEP slack, not a strict compare. Along its own normal a flat
                # face has zero thickness, so two coincident faces 6e-8 m apart
                # have boxes that miss by 6e-8 m — and a strict test throws away
                # precisely the worst clashes there are. Whether it fires at all
                # comes down to float rounding, which is why the same pair is
                # caught in world space and missed in local space.
                if (fi[4].x > fj[5].x + SEP or fj[4].x > fi[5].x + SEP or
                        fi[4].y > fj[5].y + SEP or fj[4].y > fi[5].y + SEP or
                        fi[4].z > fj[5].z + SEP or fj[4].z > fi[5].z + SEP):
                    continue
                seen.add((i, j))
                # True polygon intersection, not a bounding rectangle. On
                # diagonal geometry a bbox proxy lies enormously: two beams of
                # the inclined conveyor, side by side and touching nothing,
                # "overlap" by 184 m2 by bbox and by 0.00000 m2 in truth. Using
                # the proxy here would shove apart parts that never clashed.
                u, v = basis(fi[1])
                pa = [(w.dot(u), w.dot(v)) for w in fi[3]]
                pb = [(w.dot(u), w.dot(v)) for w in fj[3]]
                ar = poly_area(clip(pb, pa))
                if ar < MIN_AREA:
                    continue
                k = axis_key(fi[1])
                adj, nrm = axes[k]
                axes[k] = (adj, nrm or fi[1].copy())
                adj[fi[0]].add(fj[0])
                adj[fj[0]].add(fi[0])
                pairs += 1
                area += ar
    return axes, pairs, area


def demands(axes, weight, eps):
    """part -> [(outward normal, distance it must clear), ...]"""
    per_part = defaultdict(list)
    for _k, (adj, n) in axes.items():
        rank = {}
        for p in sorted(adj, key=lambda p: (-weight.get(p, 0.0), str(p))):
            used = {rank[m] for m in adj[p] if m in rank}
            r = 0
            while r in used:
                r += 1
            rank[p] = r
        for p, r in rank.items():
            if r:
                per_part[p].append((n.copy(), r * eps))
    return per_part


def resolve(reqs, verts_of, co):
    """Turn demands into a rigid move, or a scale when they cancel.

    A collar flush around an octagonal mast clashes on eight axes at once and
    their outward normals sum to zero — no translation can separate it, and
    only growing it a few millimetres proud of its host will. A scale about the
    island centre is affine, so every face stays planar and flat stays flat.
    """
    out = {}
    for part, rs in reqs.items():
        net = Vector((0.0, 0.0, 0.0))
        for n, a in rs:
            net += n * a
        biggest = max(a for _n, a in rs)
        if net.length >= 0.5 * biggest:
            out[part] = ("move", net)
            continue
        vs = verts_of(part)
        lo = Vector((min(co[i].x for i in vs), min(co[i].y for i in vs),
                     min(co[i].z for i in vs)))
        hi = Vector((max(co[i].x for i in vs), max(co[i].y for i in vs),
                     max(co[i].z for i in vs)))
        c = (lo + hi) / 2
        f = 1.0
        for n, a in rs:
            h = max(abs((co[i] - c).dot(n)) for i in vs)
            if h > 1e-6:
                f = max(f, (h + a) / h)
        out[part] = ("scale", (c, f))
    return out


def face_data(me, mw=None, part_of=None):
    out = []
    nm = mw.to_3x3().inverted_safe().transposed() if mw else None
    for poly in me.polygons:
        n = (nm @ poly.normal) if nm else poly.normal.copy()
        if n.length < 1e-9:
            continue
        n.normalize()
        vs = [(mw @ me.vertices[i].co) if mw else me.vertices[i].co.copy()
              for i in poly.vertices]
        lo = Vector((min(w.x for w in vs), min(w.y for w in vs),
                     min(w.z for w in vs)))
        hi = Vector((max(w.x for w in vs), max(w.y for w in vs),
                     max(w.z for w in vs)))
        out.append((part_of(poly), n, n.dot(vs[0]), vs, lo, hi))
    return out


objs = [o for o in bpy.data.objects if o.type == 'MESH']
ISL = {me.name: islands(me) for me in {o.data for o in objs}}

# How far each part has been shifted so far. Fixing one clash can open the
# next, and without a memory the same catwalk corner gets nudged every round
# and drifts 8 cm — far enough to show as a gap. Feeding this back into the
# ranking makes an already-shifted part hold still and spends the next move on
# a neighbour that has not moved yet.
#
# Weight is a bbox diagonal in metres, so STICK sets how many metres of
# apparent size a metre of travel buys. It has to stay small: at 1000, a
# detail that had drifted 4 cm outranked the 24 m podium and the fix started
# shoving the building's base around. This is a tiebreak between comparable
# parts, never a licence to move a host.
moved = defaultdict(float)
STICK = 50.0

m_scaled = set()

for rnd in range(1, ROUNDS + 1):
    # Grow the step each round. A fixed step can ping-pong: a part nudged 5 mm
    # lands flush against a neighbour that was 5 mm away, gets nudged back next
    # round, and the pair alternates forever. A step that changes size cannot
    # retrace its own path, so the cycle breaks and the run settles.
    eps = EPS * (1.0 + 0.2 * (rnd - 1))
    # ---- intra-mesh: local space, once per datablock -----------------------
    m_pairs = m_area = m_isl = 0
    for me in sorted({o.data for o in objs}, key=lambda m: m.name):
        vi = ISL[me.name]
        faces = face_data(me, None, lambda p: vi[p.vertices[0]])
        axes, pairs, area = detect(faces)
        m_pairs += pairs
        m_area += area
        if not pairs:
            continue
        bounds = {}
        for i, v in enumerate(me.vertices):
            b = bounds.setdefault(vi[i], [v.co.copy(), v.co.copy()])
            for k in range(3):
                b[0][k] = min(b[0][k], v.co[k])
                b[1][k] = max(b[1][k], v.co[k])
        weight = {i: (hi - lo).length + STICK * moved[(me.name, i)]
                  for i, (lo, hi) in bounds.items()}
        reqs = demands(axes, weight, eps)
        m_isl += len(reqs)
        if APPLY:
            co = [v.co.copy() for v in me.vertices]
            by_isl = defaultdict(list)
            for i in range(len(co)):
                by_isl[vi[i]].append(i)
            plan = resolve(reqs, lambda p: by_isl[p], co)
            for p, act in plan.items():
                moved[(me.name, p)] += (act[1].length if act[0] == "move"
                                        else eps)
            for i, v in enumerate(me.vertices):
                act = plan.get(vi[i])
                if act is None:
                    continue
                if act[0] == "move":
                    v.co += act[1]
                else:
                    c, f = act[1]
                    v.co = c + (v.co - c) * f
                    m_scaled.add((me.name, vi[i]))
            me.update()

    # ---- inter-object: on the object transform -----------------------------
    faces = []
    weight = {}
    for ob in objs:
        mw = ob.matrix_world
        faces += face_data(ob.data, mw, lambda p, n=ob.name: n)
        lo, hi = Vector((1e9,) * 3), Vector((-1e9,) * 3)
        for v in ob.data.vertices:
            w = mw @ v.co
            for k in range(3):
                lo[k] = min(lo[k], w[k])
                hi[k] = max(hi[k], w[k])
        weight[ob.name] = (hi - lo).length + STICK * moved[ob.name]
    axes, o_pairs, o_area = detect(faces)
    reqs = demands(axes, weight, eps)
    offs = {}
    for name, rs in reqs.items():
        net = Vector((0.0, 0.0, 0.0))
        for n, a in rs:
            net += n * a
        if net.length < 0.5 * max(a for _n, a in rs):
            print("  NOTE: %s is boxed in on all sides; an object cannot be "
                  "scaled without breaking the unit-scale rule" % name)
            continue
        offs[name] = net
    if APPLY:
        for name, d in offs.items():
            bpy.data.objects[name].location += d
            moved[name] += d.length
        bpy.context.view_layer.update()

    print("round %2d: intra-mesh %5d pairs / %8.1f m2 (%3d islands) | "
          "inter-object %5d pairs / %7.1f m2 (%2d objects)"
          % (rnd, m_pairs, m_area, m_isl, o_pairs, o_area, len(offs)))
    if not APPLY:
        print("DRY RUN — nothing written")
        break
    if m_pairs == 0 and o_pairs == 0:
        print("CONVERGED: nothing coplanar within %.1f mm" % (SEP * 1000))
        break

if APPLY:
    if m_scaled:
        print("grown (flush on all sides, could not be moved): %s"
              % ", ".join("%s#%s" % k for k in sorted(m_scaled)))
    print("meshes: %d datablocks for %d objects (instancing intact)"
          % (len({o.data.name for o in objs}), len(objs)))
    bpy.ops.wm.save_mainfile()
    print("SAVED")
