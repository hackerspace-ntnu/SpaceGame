"""ship_lander.blend — the hand-built interior from `ship_lander_blockout.blend`
plus the example hull cut into tweakable components.

The example (`models/example/futuristic+spacecraft+3d+model.fbx 3`) is one
connected shell with 53 loose greebles. Nothing here is remodelled: the shell
is sliced with axis-aligned planes into fuselage sections, wings, pods,
nacelles, tail and fin, every cut and every pre-existing hole is capped, so
each piece is a closed solid that can be moved, scaled or deleted on its own.
Where two pieces meet, their caps coincide and are invisible from outside, so
the assembled set is the original surface exactly.

All cut positions are in the example's normalised units (hull length 1.0,
nose at -Y, ground at z = 0); the emitted meshes are scaled by SCALE so they
overlay the ×30 reference hull already in the interior file.

    blender --background --python ship_lander.py -- --out ship_lander.blend

Never re-run over an existing output — the .blend is the source of truth.
"""

import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
import _buildlib as B  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
INTERIOR_FILE = os.path.join(HERE, "ship_lander_blockout.blend")
EXAMPLE_FBX = os.path.join(B.LIB_ROOT, "models", "example",
                           "futuristic+spacecraft+3d+model.fbx 3")
SCALE = 30.0

# --- cut plan ---------------------------------------------------------------
# Each entry: (name, list of (axis, value, keep_side)) applied to the body in
# order. keep_side +1 keeps the side where coord > value, -1 keeps coord < value.
# The body is carved as a tree: every cut leaves a remainder that the next
# entry carves from, so the pieces tile the original exactly.
X_WING = 0.15     # fuselage side / wing root
X_TIP = 0.28      # wing / wingtip pod
Y_POD_WING = 0.00  # forward side pod / wing
Y_NOSE = -0.36
Y_COCKPIT = -0.14
Y_MID = 0.08
Y_AFT = 0.27
Z_CANOPY = 0.19   # canopy hump over the cockpit
Z_NACELLE = 0.17  # aft-body nacelles under the wing carry-through
Z_FIN = 0.31

CUT_TREE = [
    # side pieces first, so the wings come off the whole length
    ("Wingtip_R",       [('X', X_TIP, +1)]),
    ("Wingtip_L",       [('X', -X_TIP, -1)]),
    ("Wing_R",          [('X', X_WING, +1), ('Y', Y_POD_WING, +1)]),
    ("Wing_L",          [('X', -X_WING, -1), ('Y', Y_POD_WING, +1)]),
    ("SidePod_R",       [('X', X_WING, +1)]),
    ("SidePod_L",       [('X', -X_WING, -1)]),
    # then the fuselage, nose to tail
    ("Nose",            [('Y', Y_NOSE, -1)]),
    ("Canopy",          [('Y', Y_COCKPIT, -1), ('Z', Z_CANOPY, +1)]),
    ("Cockpit_Hull",    [('Y', Y_COCKPIT, -1)]),
    ("MidBody",         [('Y', Y_MID, -1)]),
    ("Nacelle_R",       [('Y', Y_AFT, -1), ('Z', Z_NACELLE, -1), ('X', 0.0, +1)]),
    ("Nacelle_L",       [('Y', Y_AFT, -1), ('Z', Z_NACELLE, -1)]),
    ("AftBody",         [('Y', Y_AFT, -1)]),
    ("Fin",             [('Z', Z_FIN, +1)]),
    ("TailBoom",        []),   # whatever is left
]

# Loose greebles are named by the fuselage region their centre falls in.
REGIONS = [
    ("Nose",     lambda c: c.y < Y_NOSE),
    ("Cockpit",  lambda c: c.y < Y_COCKPIT),
    ("Wing_R",   lambda c: c.x > X_WING and c.y > Y_POD_WING),
    ("Wing_L",   lambda c: c.x < -X_WING and c.y > Y_POD_WING),
    ("SidePod_R", lambda c: c.x > X_WING),
    ("SidePod_L", lambda c: c.x < -X_WING),
    ("MidBody",  lambda c: c.y < Y_MID),
    ("AftBody",  lambda c: c.y < Y_AFT),
    ("Fin",      lambda c: c.z > Z_FIN),
    ("TailBoom", lambda c: True),
]

AXIS = {'X': Vector((1, 0, 0)), 'Y': Vector((0, 1, 0)), 'Z': Vector((0, 0, 1))}


def bm_copy(bm):
    out = bmesh.new()
    out.from_mesh(bm_to_mesh(bm))
    return out


_scratch = []


def bm_to_mesh(bm):
    m = bpy.data.meshes.new("_scratch")
    bm.to_mesh(m)
    _scratch.append(m)
    return m


def split(bm, axis, value, keep):
    """Return (kept, remainder) halves of bm across the plane. Both halves
    are copies; bm itself is left untouched."""
    halves = []
    for side in (keep, -keep):
        h = bm_copy(bm)
        geom = h.verts[:] + h.edges[:] + h.faces[:]
        bmesh.ops.bisect_plane(h, geom=geom, dist=1e-6,
                               plane_co=AXIS[axis] * value,
                               plane_no=AXIS[axis] * side,
                               clear_inner=True, clear_outer=False)
        halves.append(h)
    return halves[0], halves[1]


def carve(bm, cuts):
    """Apply a sequence of cuts. Returns (piece, remainder)."""
    remainder_parts = []
    cur = bm
    for axis, value, keep in cuts:
        kept, rest = split(cur, axis, value, keep)
        remainder_parts.append(rest)
        cur = kept
    return cur, remainder_parts


def merge(parts):
    out = bmesh.new()
    for p in parts:
        out.from_mesh(bm_to_mesh(p))
        p.free()
    bmesh.ops.remove_doubles(out, verts=out.verts, dist=1e-6)
    return out


def seal(bm):
    """Cap every boundary loop (cuts and the model's own holes), triangulate
    the caps with ear clipping so concave loops stay planar, and face outward."""
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    before = set(bm.faces)
    bmesh.ops.holes_fill(bm, edges=bm.edges, sides=0)
    caps = [f for f in bm.faces if f not in before and len(f.verts) > 4]
    if caps:
        bmesh.ops.triangulate(bm, faces=caps, quad_method='BEAUTY', ngon_method='EAR_CLIP')
    fan_remaining_loops(bm)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bm


def fan_remaining_loops(bm):
    """holes_fill skips loops that touch themselves or pass through
    non-manifold vertices — the example has a few. Close each such loop with
    a fan of triangles to its centroid, which never fails to produce a cap."""
    for _ in range(4):
        open_edges = [e for e in bm.edges if len(e.link_faces) == 1]
        if not open_edges:
            return
        # triangle_fill copes with branching edge nets that holes_fill rejects
        bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False, edges=open_edges)
        open_edges = [e for e in bm.edges if len(e.link_faces) == 1]
        if not open_edges:
            return
        # A hole boundary may run through the example's non-manifold edges
        # (three faces on one edge), so those are walkable too.
        walkable = set(e for e in bm.edges if len(e.link_faces) % 2 == 1)
        unvisited = set(open_edges)
        while unvisited:
            start = unvisited.pop()
            chain = [start]
            v0, cur = start.verts[0], start.verts[1]
            while cur is not v0:
                nxt = next((e for e in cur.link_edges if e in walkable and e not in chain), None)
                if nxt is None:
                    break
                unvisited.discard(nxt)
                chain.append(nxt)
                cur = nxt.other_vert(cur)
            verts = [v0]
            for e in chain:
                verts.append(e.other_vert(verts[-1]))
            if verts[-1] is v0:
                verts.pop()
            # an unclosed chain is fanned anyway; the closing segment bridges
            # its two ends, which is the only way to leave no gap at all
            centre = bm.verts.new(sum((v.co for v in verts), Vector()) / len(verts))
            for a, b in zip(verts, verts[1:] + verts[:1]):
                if a is b:
                    continue
                tri = (a, b, centre)
                if bm.faces.get(tri) is None:
                    bm.faces.new(tri)
        bm.verts.ensure_lookup_table()
        bm.edges.ensure_lookup_table()


def boundary_edges(bm):
    return sum(1 for e in bm.edges if len(e.link_faces) == 1)


def emit(bm, name, coll, material):
    seal(bm)
    open_edges = boundary_edges(bm)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.transform(Matrix.Diagonal((SCALE, SCALE, SCALE, 1.0)))
    mesh.materials.append(material)
    for p in mesh.polygons:
        p.use_smooth = True
    obj = bpy.data.objects.new(name, mesh)
    coll.objects.link(obj)
    print("  %-28s faces=%5d open_edges=%d" % (name, len(mesh.polygons), open_edges))
    return obj


def loose_parts(bm):
    seen = set()
    parts = []
    for v in bm.verts:
        if v in seen:
            continue
        comp, stack = [], [v]
        seen.add(v)
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for e in cur.link_edges:
                w = e.other_vert(cur)
                if w not in seen:
                    seen.add(w)
                    stack.append(w)
        parts.append(comp)
    return parts


def build(out_path):
    if os.path.exists(out_path):
        raise SystemExit("Refusing to overwrite %s — the .blend is the source of truth." % out_path)

    # Start from the hand-built interior. Opened, extended, saved under a NEW
    # name; the interior file itself is never written.
    bpy.ops.wm.open_mainfile(filepath=INTERIOR_FILE)
    existing = {o.name for o in bpy.data.objects}

    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=EXAMPLE_FBX)
    src = [o for o in bpy.data.objects if o not in before and o.type == 'MESH'][0]
    material = src.data.materials[0]
    material.name = "Mat_Lander_Example"
    material.use_fake_user = True

    root = B.collection("Coll_Lander_Components")
    colls = {k: B.collection("Coll_Lander_" + k, root)
             for k in ("Fuselage", "Wings", "Pods", "Engines", "Tail", "Details")}
    coll_of = {
        "Nose": "Fuselage", "Canopy": "Fuselage", "Cockpit_Hull": "Fuselage",
        "MidBody": "Fuselage", "AftBody": "Fuselage",
        "Wing_R": "Wings", "Wing_L": "Wings", "Wingtip_R": "Wings", "Wingtip_L": "Wings",
        "SidePod_R": "Pods", "SidePod_L": "Pods",
        "Nacelle_R": "Engines", "Nacelle_L": "Engines",
        "Fin": "Tail", "TailBoom": "Tail",
    }

    whole = bmesh.new()
    whole.from_mesh(src.data)
    whole.transform(src.matrix_world)
    whole.verts.ensure_lookup_table()

    parts = loose_parts(whole)
    parts.sort(key=lambda p: -len(p))
    body_verts, greebles = parts[0], parts[1:]

    # -- body -> components ---------------------------------------------------
    body = bmesh.new()
    body.from_mesh(bm_to_mesh(whole))
    body.verts.ensure_lookup_table()
    drop = [body.verts[i] for i in range(len(body.verts))
            if whole.verts[i] not in set(body_verts)]
    bmesh.ops.delete(body, geom=drop, context='VERTS')

    print("Carving body:")
    remainder = body
    for name, cuts in CUT_TREE:
        if not cuts:
            emit(remainder, "Hull_" + name, colls[coll_of[name]], material)
            remainder = None
            break
        piece, rest_parts = carve(remainder, cuts)
        remainder = merge(rest_parts)
        emit(piece, "Hull_" + name, colls[coll_of[name]], material)

    # -- greebles -------------------------------------------------------------
    print("Greebles:")
    counters = {}
    for comp in greebles:
        idx = {v.index for v in comp}
        g = bmesh.new()
        g.from_mesh(bm_to_mesh(whole))
        g.verts.ensure_lookup_table()
        drop = [g.verts[i] for i in range(len(g.verts)) if i not in idx]
        bmesh.ops.delete(g, geom=drop, context='VERTS')
        centre = sum((v.co for v in g.verts), Vector()) / len(g.verts)
        region = next(r for r, test in REGIONS if test(centre))
        counters[region] = counters.get(region, 0) + 1
        emit(g, "Detail_%s_%02d" % (region, counters[region]), colls["Details"], material)

    # -- tidy -----------------------------------------------------------------
    bpy.data.objects.remove(src)
    for m in _scratch:
        if m.users == 0:
            bpy.data.meshes.remove(m)
    for m in list(bpy.data.meshes):
        if m.users == 0 and m.name.startswith("_scratch"):
            bpy.data.meshes.remove(m)
    for o in bpy.data.objects:
        if o.data and hasattr(o.data, "name") and o.name not in existing:
            o.data.name = o.name

    touched = {o.name for o in bpy.data.objects} & existing
    assert touched == existing, "pre-existing objects went missing"
    bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(out_path))
    print("Wrote %s" % out_path)


if __name__ == "__main__":
    build(B.parse_out())
