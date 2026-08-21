"""Split the fitted robot helmet into separately-colourable parts.

Reads Robot_Helmet_Bust out of nomad.blend, repairs it, segments it along its
own creases, groups the clusters into named parts, and rebuilds each part as a
closed manifold solid whose OUTER surface is the original surface untouched -
so the reassembled parts occupy exactly the same space as the source helmet.

ADDITIVE: the source helmet object is not modified. It is only hidden, so the
parts are what you see; unhide Robot_Helmet_Bust to get back to the original.

Run:  blender --background nomad.blend --python nomad_split_helmet.py -- <out.blend>
      (pass the nomad.blend path itself as <out.blend> to write in place)
"""
import bpy, bmesh, math, sys, json
from mathutils import Vector

SRC = "Robot_Helmet_Bust"
COLL_NAME = "Robot Helmet Parts"
CREASE_DEG = 32.0          # crease angle that separates one panel from the next
MIN_CLUSTER = 18           # smaller clusters get absorbed by their biggest neighbour
THICKNESS = 0.014          # inward shell thickness, in Blender units
LENS_FACING = 0.40         # normal.dot(-Y) above which a visor face counts as the lens plate.
#                            Panel and rim share a smooth fillet, so no threshold splits
#                            them; 0.40 takes the whole front plate, which is the real part.

# part name -> (placeholder colour, roughness, metallic)
PART_STYLE = {
    "Shell":     ((0.355, 0.375, 0.400, 1.0), 0.45, 0.60),
    "Brow":      ((0.230, 0.245, 0.265, 1.0), 0.40, 0.70),
    "Lens":      ((0.055, 0.130, 0.155, 1.0), 0.12, 0.30),
    "Face":      ((0.330, 0.345, 0.365, 1.0), 0.48, 0.55),
    "Jaw":       ((0.300, 0.315, 0.335, 1.0), 0.50, 0.55),
    "EarPods":   ((0.420, 0.330, 0.190, 1.0), 0.55, 0.35),
    "TopRail":   ((0.185, 0.195, 0.210, 1.0), 0.35, 0.75),
    "NeckRim":   ((0.250, 0.255, 0.270, 1.0), 0.60, 0.40),
    "Studs":     ((0.560, 0.520, 0.300, 1.0), 0.30, 0.90),
}


def classify(c, ctr):
    """Cluster -> part name. cx is signed left/right, -y is the helmet's front."""
    cx = c["center"][0] - ctr.x
    cy = c["center"][1] - ctr.y
    cz = c["center"][2]
    sx, sy, sz = c["size"]
    if c["faces"] <= 25 and max(sx, sy, sz) < 0.10:
        return "Studs"
    if sz < 0.12 and cz > 2.80 and cy > 0.0:
        return "TopRail"
    if cy < -0.26 and cz > 2.65:
        return "Brow"
    if abs(cx) > 0.15 and 2.45 < cz < 2.70 and sx < 0.20:
        return "EarPods"
    if cz < 2.50:
        return "Jaw" if cy < -0.10 else "NeckRim"
    if cy < -0.12:
        return "Face"
    return "Shell"


def ensure_object_mode():
    vl = bpy.context.view_layer
    if vl.objects.active is None or vl.objects.active.name not in vl.objects:
        vl.objects.active = next(o for o in vl.objects if o.type == 'MESH')
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')


ensure_object_mode()
src = bpy.data.objects[SRC]

# ------------------------------------------------------------------ working copy
work = src.copy()
work.data = src.data.copy()
work.name = work.data.name = "Helmet_Work"
bpy.context.scene.collection.objects.link(work)
for c in list(work.users_collection):
    if c is not bpy.context.scene.collection:
        c.objects.unlink(work)
bpy.ops.object.select_all(action='DESELECT')
work.select_set(True)
bpy.context.view_layer.objects.active = work
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# ------------------------------------------------------------------ repair
bm = bmesh.new()
bm.from_mesh(work.data)
before = (len(bm.verts), len(bm.faces))
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
nm = [e for e in bm.edges if len(e.link_faces) > 2]
if nm:
    bmesh.ops.split_edges(bm, edges=nm)
stray = [v for v in bm.verts if not v.link_faces]
if stray:
    bmesh.ops.delete(bm, geom=stray, context='VERTS')
degen = [f for f in bm.faces if f.calc_area() < 1e-9]
if degen:
    bmesh.ops.delete(bm, geom=degen, context='FACES')
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bmesh.ops.triangulate(bm, faces=bm.faces)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
print("REPAIR %s -> (%d verts, %d faces), split %d non-manifold, dropped %d degenerate"
      % (before, len(bm.verts), len(bm.faces), len(nm), len(degen)))
bm.to_mesh(work.data)
bm.free()
work.data.update()

# ------------------------------------------------------------------ segment
bm = bmesh.new()
bm.from_mesh(work.data)
bm.faces.ensure_lookup_table()
lim = math.cos(math.radians(CREASE_DEG))
label = [-1] * len(bm.faces)
clusters = []
for f in bm.faces:
    if label[f.index] >= 0:
        continue
    cid = len(clusters)
    label[f.index] = cid
    stack, mem = [f], []
    while stack:
        cur = stack.pop()
        mem.append(cur.index)
        for e in cur.edges:
            if len(e.link_faces) != 2:
                continue
            nb = e.link_faces[0] if e.link_faces[1] is cur else e.link_faces[1]
            if label[nb.index] < 0 and cur.normal.dot(nb.normal) >= lim:
                label[nb.index] = cid
                stack.append(nb)
    clusters.append(mem)

changed = True
while changed:
    changed = False
    for ci in sorted(range(len(clusters)), key=lambda i: len(clusters[i])):
        if not clusters[ci] or len(clusters[ci]) >= MIN_CLUSTER:
            continue
        tally = {}
        for fi in clusters[ci]:
            for e in bm.faces[fi].edges:
                for nb in e.link_faces:
                    if label[nb.index] != ci:
                        tally[label[nb.index]] = tally.get(label[nb.index], 0) + 1
        if not tally:
            continue
        host = max(tally, key=tally.get)
        for fi in clusters[ci]:
            label[fi] = host
        clusters[host].extend(clusters[ci])
        clusters[ci] = []
        changed = True
        break

live = [c for c in clusters if c]
live.sort(key=len, reverse=True)
bb = [Vector(c) for c in work.bound_box]
ctr = work.matrix_world @ (sum(bb, Vector()) / 8)

desc = []
for c in live:
    pts = [v.co for fi in c for v in bm.faces[fi].verts]
    mn = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    mx = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    desc.append({"faces": len(c), "members": c,
                 "center": list((mn + mx) / 2), "size": list(mx - mn)})
bm.free()

part_faces = {}
for c in desc:
    part_faces.setdefault(classify(c, ctr), []).extend(c["members"])

# The lens is the big inset panel in the visor band. No crease separates it from
# its own rim - the rim is a smooth fillet - so it cannot come out of clustering.
# It is instead the largest connected patch of strongly forward-facing faces.
if "Brow" in part_faces:
    brow = set(part_faces["Brow"])
    bm = bmesh.new()
    bm.from_mesh(work.data)
    bm.faces.ensure_lookup_table()
    flat = {fi for fi in brow
            if bm.faces[fi].normal.dot(Vector((0, -1, 0))) > LENS_FACING}
    seen, comps = set(), []
    for fi in flat:
        if fi in seen:
            continue
        stack, comp = [fi], []
        seen.add(fi)
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for e in bm.faces[cur].edges:
                for nb in e.link_faces:
                    if nb.index in flat and nb.index not in seen:
                        seen.add(nb.index)
                        stack.append(nb.index)
        comps.append(comp)
    bm.free()
    comps.sort(key=len, reverse=True)
    if comps and len(comps[0]) >= 8:
        lens = set(comps[0])
        part_faces["Lens"] = sorted(lens)
        part_faces["Brow"] = sorted(brow - lens)
        print("LENS %d faces (next largest forward patch: %d)"
              % (len(lens), len(comps[1]) if len(comps) > 1 else 0))

print("PARTS " + json.dumps({k: len(v) for k, v in sorted(part_faces.items())}))

# ------------------------------------------------------------------ build parts
coll = bpy.data.collections.get(COLL_NAME)
if coll is None:
    coll = bpy.data.collections.new(COLL_NAME)
    bpy.context.scene.collection.children.link(coll)

built = []
for part, faces in sorted(part_faces.items()):
    if not faces:
        continue
    me = work.data.copy()
    o = bpy.data.objects.new("Helmet_" + part, me)
    coll.objects.link(o)

    keep = set(faces)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.index not in keep], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    # A patch can pinch itself where it wraps round. Two kinds: an edge already
    # shared by 3+ faces, and - the one solidify trips over - a boundary vertex
    # where two separate boundary loops touch. Cut both before thickening, or the
    # rim it builds lands 3 faces on one edge.
    pinch = [e for e in bm.edges if len(e.link_faces) > 2]
    if pinch:
        bmesh.ops.split_edges(bm, edges=pinch)
    fan = {}
    for e in [e for e in bm.edges if len(e.link_faces) == 1]:
        for v in e.verts:
            fan.setdefault(v, []).append(e)
    pinch_v = [v for v, es in fan.items() if len(es) > 2]
    if pinch_v:
        bmesh.ops.split_edges(bm, edges=[e for v in pinch_v for e in fan[v]],
                              verts=pinch_v, use_verts=True)
    if pinch or pinch_v:
        print("  %s: cut %d pinched edge(s), %d pinched vertex/vertices"
              % (part, len(pinch), len(pinch_v)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()

    mod = o.modifiers.new("Solidify", 'SOLIDIFY')
    mod.thickness = THICKNESS
    mod.offset = -1.0                     # grow inward; outer surface stays put
    mod.use_rim = True
    mod.use_rim_only = False
    mod.use_even_offset = False           # even offset spikes on sharp creases
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_apply(modifier="Solidify")

    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    open_e = len([e for e in bm.edges if len(e.link_faces) == 1])
    nonman = len([e for e in bm.edges if len(e.link_faces) > 2])
    loose = len([v for v in bm.verts if not v.link_faces])
    bm.to_mesh(me)
    bm.free()
    me.update()

    col, rough, metal = PART_STYLE[part]
    mname = "Helmet_" + part
    mat = bpy.data.materials.get(mname)
    if mat is None:
        mat = bpy.data.materials.new(mname)
        mat.use_nodes = True
        b = mat.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = col
        b.inputs["Roughness"].default_value = rough
        b.inputs["Metallic"].default_value = metal
    mat.diffuse_color = col
    me.materials.clear()
    me.materials.append(mat)

    built.append({"part": part, "verts": len(me.vertices), "faces": len(me.polygons),
                  "open_edges": open_e, "non_manifold": nonman, "loose_verts": loose,
                  "dims": [round(v, 4) for v in o.dimensions]})
    print("BUILT %-9s verts=%-5d faces=%-5d open=%d nonmanifold=%d loose=%d dims=%s"
          % (part, len(me.vertices), len(me.polygons), open_e, nonman, loose,
             [round(v, 4) for v in o.dimensions]))

bpy.data.objects.remove(work, do_unlink=True)

# assembled extent must match the source helmet (measured off vertices: the
# source's bound_box is a rotated box, so its world AABB would overstate it)
pts = [o.matrix_world @ v.co for o in coll.objects for v in o.data.vertices]
amin = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
amax = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
sb = [src.matrix_world @ v.co for v in src.data.vertices]
smin = Vector((min(p.x for p in sb), min(p.y for p in sb), min(p.z for p in sb)))
smax = Vector((max(p.x for p in sb), max(p.y for p in sb), max(p.z for p in sb)))
print("EXTENT source min%s max%s" % ([round(v, 4) for v in smin], [round(v, 4) for v in smax]))
print("EXTENT parts  min%s max%s" % ([round(v, 4) for v in amin], [round(v, 4) for v in amax]))
print("EXTENT delta  min%s max%s" % ([round(a - b, 5) for a, b in zip(amin, smin)],
                                     [round(a - b, 5) for a, b in zip(amax, smax)]))

src.hide_viewport = True
src.hide_render = True
print("hid source object %s (unhide to compare)" % SRC)

out = sys.argv[-1]
bpy.ops.wm.save_as_mainfile(filepath=out)
print("SAVED", out)
