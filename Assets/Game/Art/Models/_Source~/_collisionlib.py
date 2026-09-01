"""Approximate convex decomposition — collision proxies for the model library.

Unity can only put a *convex* MeshCollider on a Rigidbody, and a hull hull of a hollow shell
fills the shell solid. For a ship the player walks around inside, that is fatal: the hull of the
outer skin is the whole interior, and the rooms become concrete.

The usual dodges are all worse than they look:

  * **One hull per mesh.** Exact for the hand-built slabs (a cube's hull *is* the cube), a
    catastrophe for the curved hull panels — one of this lander's skin panels measures 12.8 m³ of
    metal inside an 85 m³ hull. Six times the ship's volume ends up solid.
  * **Bounding boxes per mesh.** The same lie with worse corners on anything angled.
  * **Shrink-wrapping the surface into grid cells.** Each cell has to span every surface point in
    it, so a cell where the skin curves from floor to roof becomes a pillar through the room. This
    is what the lander shipped with, and it is why you could not walk across the bay.

So instead: split each mesh until every piece really is nearly convex, and let its hull stand in
for it. Splitting is a plane bisect through the median vertex, on whichever of the three axes
leaves the least total hull volume; recursion stops when a piece's hull is within `target_fill` of
the piece's own volume (or it is too small or too deep to be worth another cut). A blind
depth-first split cuts pieces that were convex enough already, so a merge pass fuses any pair
whose union hull adds almost nothing back.

The result is measured, not hoped for: `decompose_object` returns the achieved over-fill so the
caller can assert on it. On the lander it is 1.05x — five percent of false solid across the whole
ship, against the ~6x a per-mesh hull would have cost.

    import sys, os
    sys.path.insert(0, "<_Source~>")
    import _collisionlib

The input must be a closed mesh. Volume of an open shell is meaningless, the fill test would read
garbage, and the decomposition would never terminate on merit — so open meshes are refused rather
than silently mis-fitted.
"""

import bmesh
from mathutils import Vector

# A piece is "convex enough" once its own volume fills this much of its hull. 0.88 is where the
# lander stops paying for cuts: tightening to 0.95 doubles the piece count for a further 1% of
# volume, and 0.80 starts leaving noticeable phantom volume in the corners of curved panels.
TARGET_FILL = 0.88

# Ceilings on the recursion. Depth 5 caps a single mesh at 32 pieces before merging; the span and
# volume floors stop the split chasing detail smaller than the player can feel — 0.4 m of slack on
# a greeble is not what makes a ship interior unwalkable.
MAX_DEPTH = 5
MIN_PIECE_VOLUME = 0.05
MIN_PIECE_SPAN = 0.4

# How much extra volume a merge may add, as a multiple of the two parts' hulls. Above ~1.06 the
# fused hull starts bridging real gaps.
MERGE_SLACK = 1.06

# Below this a cut has not earned its piece: the best split of a 10 m³ hull that still leaves
# 9.8 m³ is just two colliders doing one collider's job.
MIN_SPLIT_GAIN = 0.97


def hull_mesh(points):
    """Convex hull of a point cloud as a closed bmesh. Caller owns the result."""
    bm = bmesh.new()
    for p in points:
        bm.verts.new(p)
    bm.verts.ensure_lookup_table()
    result = bmesh.ops.convex_hull(bm, input=bm.verts, use_existing_faces=False)

    # convex_hull reports the same element under both keys when a vertex is interior *and*
    # unused, and bmesh.ops.delete refuses a list with a repeat in it.
    junk, seen = [], set()
    for geom in result["geom_interior"] + result["geom_unused"]:
        if geom.is_valid and geom not in seen:
            seen.add(geom)
            junk.append(geom)
    if junk:
        bmesh.ops.delete(bm, geom=junk, context="VERTS")

    bm.normal_update()
    return bm


def hull_volume(points):
    if len(points) < 4:
        return 0.0
    bm = hull_mesh(points)
    volume = abs(bm.calc_volume(signed=True))
    bm.free()
    return volume


def _clip(bm, plane_co, plane_no, keep_positive):
    """A copy of `bm` cut to one side of the plane, capped so it stays a closed solid.

    The cap matters: the fill test below divides by the piece's volume, and an uncapped piece has
    none. Capping is also why the hull of the piece's vertices is exactly the hull of the piece —
    every vertex of the clipped polyhedron, cut face included, is in the set.
    """
    out = bm.copy()
    result = bmesh.ops.bisect_plane(
        out, geom=out.verts[:] + out.edges[:] + out.faces[:], dist=1e-5,
        plane_co=plane_co, plane_no=plane_no,
        clear_outer=not keep_positive, clear_inner=keep_positive)

    cut = [g for g in result["geom_cut"] if isinstance(g, bmesh.types.BMEdge)]
    if cut:
        bmesh.ops.edgenet_fill(out, edges=cut)
    out.normal_update()
    return out


def _best_split(bm, points, lo, hi, span):
    """The (cost, low_half, high_half) split that leaves the least total hull volume."""
    best = None
    for axis in range(3):
        if span[axis] < MIN_PIECE_SPAN:
            continue

        # Through the median vertex rather than the midpoint: a panel whose detail is bunched at
        # one end otherwise gets cut through empty space and one half comes back unchanged.
        coords = sorted(p[axis] for p in points)
        cut = coords[len(coords) // 2]
        if cut - lo[axis] < 1e-3 or hi[axis] - cut < 1e-3:
            cut = (lo[axis] + hi[axis]) * 0.5

        normal = Vector((0.0, 0.0, 0.0))
        normal[axis] = 1.0
        origin = lo.copy()
        origin[axis] = cut

        high = _clip(bm, origin, normal, keep_positive=True)
        low = _clip(bm, origin, normal, keep_positive=False)
        cost = (hull_volume([v.co for v in high.verts])
                + hull_volume([v.co for v in low.verts]))

        if best is None or cost < best[0]:
            if best is not None:
                best[1].free()
                best[2].free()
            best = (cost, low, high)
        else:
            high.free()
            low.free()
    return best


def _decompose(bm, depth, pieces):
    """Split `bm` until every piece is convex enough, appending each piece's points. Frees `bm`."""
    points = [v.co.copy() for v in bm.verts]
    if len(points) < 4:
        bm.free()
        return

    volume = abs(bm.calc_volume(signed=True))
    hull = hull_volume(points)
    lo = Vector(min(p[i] for p in points) for i in range(3))
    hi = Vector(max(p[i] for p in points) for i in range(3))
    span = hi - lo

    if (hull < 1e-6 or volume / hull >= TARGET_FILL or depth >= MAX_DEPTH
            or hull < MIN_PIECE_VOLUME or max(span) < MIN_PIECE_SPAN):
        pieces.append(points)
        bm.free()
        return

    best = _best_split(bm, points, lo, hi, span)
    if best is None or best[0] > hull * MIN_SPLIT_GAIN:
        if best is not None:
            best[1].free()
            best[2].free()
        pieces.append(points)
        bm.free()
        return

    bm.free()
    _decompose(best[1], depth + 1, pieces)
    _decompose(best[2], depth + 1, pieces)


def _merge(pieces):
    """Fuse piece pairs whose union hull adds almost no volume, cheapest ratio first."""
    volumes = [hull_volume(p) for p in pieces]
    while len(pieces) > 1:
        best = None
        for i in range(len(pieces)):
            for j in range(i + 1, len(pieces)):
                apart = volumes[i] + volumes[j]
                if apart <= 1e-9:
                    continue
                union = hull_volume(pieces[i] + pieces[j])
                if union <= apart * MERGE_SLACK and (best is None or union / apart < best[0]):
                    best = (union / apart, i, j, union)
        if best is None:
            return pieces

        _, i, j, union = best
        pieces[i] = pieces[i] + pieces[j]
        volumes[i] = union
        del pieces[j]
        del volumes[j]
    return pieces


def decompose_object(obj, depsgraph):
    """Convex collision pieces for one mesh object, in world space.

    Returns `(pieces, volume, hull_volume)` where each piece is a point list whose convex hull is
    one collider. The two volumes are the caller's quality check: their ratio is the phantom solid
    the proxy adds, and a build should assert on it rather than trust these constants to have
    stayed right through a model change.
    """
    mesh = obj.evaluated_get(depsgraph).to_mesh()
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.transform(obj.matrix_world)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)

    open_edges = sum(1 for e in bm.edges if len(e.link_faces) != 2)
    if open_edges:
        bm.free()
        obj.evaluated_get(depsgraph).to_mesh_clear()
        raise SystemExit(
            "_collisionlib: '%s' has %d open edge(s). Volume is undefined on an open shell, so "
            "the fill test cannot tell a convex piece from a flat one. Close the mesh, or exclude "
            "it from the collision bake and give it a collider of its own."
            % (obj.name, open_edges))

    volume = abs(bm.calc_volume(signed=True))
    pieces = []
    _decompose(bm, 0, pieces)
    pieces = _merge(pieces)
    obj.evaluated_get(depsgraph).to_mesh_clear()
    return pieces, volume, sum(hull_volume(p) for p in pieces)
