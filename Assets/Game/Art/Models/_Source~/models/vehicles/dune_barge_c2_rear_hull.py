"""Rear hull v2 for Collection 2, after the user's hand edit of the port wall.

The user's port-wall rear ring is the target / | \\ profile. Both walls are rebuilt as dense grids
(stations along Y, samples up Z); every sample is pulled inboard of the track and fenders at that
station, so the hull overhangs the tracks where there is room and tucks inside them where there is
not. Collar, stern and hold bulkhead follow the new profile; the roof stays where the user put it.
Hatches sit where the user moved them (y 0.6..1.8), and the user's port ladder is mirrored to
starboard. Live-session edit (MCP)."""
import bpy, bmesh, math, os
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

LIB = r"C:\Users\tobia\Documents\spaceGame\SpaceGame\Assets\Game\Art\Models\_Source~\components"
C = bpy.data.collections
DECK = 0.7156
FLOOR_BOTTOM = 0.50
Y_FRONT, Y_STERN = 0.30, 9.42
ROOF = bpy.data.objects["Mesh_RearHull_Roof"]
ROOF_TOP = max((ROOF.matrix_world @ v.co).z for v in ROOF.data.vertices)
ROOF_BOTTOM = min((ROOF.matrix_world @ v.co).z for v in ROOF.data.vertices)
# the user's port-wall rear ring (x outboard, z), mirrored to +x
TARGET = [(0.50, 1.96), (1.25, 2.46), (2.00, 2.97), (2.63, 2.95), (3.26, 2.92), (3.68, 2.44), (ROOF_TOP - 0.01, 1.95)]
T_WALL = 0.08
MITRE_MAX = 2.0
CLEAR = 0.03                 # gap kept to the track and fenders
STATION_STEP = 0.3
Z_STEP = 0.12
HOLD_Y = 4.0
HOLD_DOOR = (0.2, 1.4, DECK + 3.1)
HATCH_Y0, HATCH_Y1 = 0.6, 1.8          # where the user moved the port hatch frame
HATCH_SILL_MIN, HATCH_TOP = 2.17, 3.50   # the user's sill; raised to clear the fender under it
FW = 0.12

def mat(base):
    for m in bpy.data.materials:
        if m.name == base or m.name.startswith(base + "."):
            return m
    raise RuntimeError("palette material missing: " + base)
rust_orange, rust_deep = mat("Mat_Metal_HullRust_Orange"), mat("Mat_Metal_Rust_Deep")

def remove(names):
    for n in names:
        o = bpy.data.objects.get(n)
        if o is not None:
            me = o.data; bpy.data.objects.remove(o)
            if me is not None and me.users == 0: bpy.data.meshes.remove(me)

def obj(name, me, material, into):
    remove([name])
    old = bpy.data.meshes.get(name)
    if old is not None and old.users == 0: bpy.data.meshes.remove(old)
    me.name = name; me.materials.clear(); me.materials.append(material)
    o = bpy.data.objects.new(name, me); into.objects.link(o); return o

def box_me(lo, hi):
    b = bmesh.new(); bmesh.ops.create_cube(b, size=1.0)
    for v in b.verts:
        v.co = Vector(((lo[i] + hi[i]) / 2 + v.co[i] * (hi[i] - lo[i]) for i in range(3)))
    me = bpy.data.meshes.new("tmp"); b.to_mesh(me); b.free(); return me

def slab(outline, y0, y1, hole=None):
    """Flat plate from a closed xz outline (concave is fine), optionally with a hole, extruded along y.
    Filled with a constrained triangulation, which handles the ledge notches that polyfill mis-filled."""
    b = bmesh.new()
    loops = []
    for pts in [outline] + ([hole] if hole else []):
        vs = [b.verts.new((x, y0, z)) for x, z in pts]
        loops.append((vs, [b.edges.new((vs[i], vs[(i + 1) % len(vs)])) for i in range(len(vs))]))
    tris = [g for g in bmesh.ops.triangle_fill(b, use_beauty=True, use_dissolve=False, edges=[e for _, es in loops for e in es])["geom"]
            if isinstance(g, bmesh.types.BMFace)]
    back = {v: b.verts.new((v.co.x, y1, v.co.z)) for v in list(b.verts)}
    for f in tris:
        b.faces.new([back[v] for v in reversed(f.verts)])
    for vs, _ in loops:
        for i in range(len(vs)):
            j = (i + 1) % len(vs)
            b.faces.new((vs[i], vs[j], back[vs[j]], back[vs[i]]))
    bmesh.ops.recalc_face_normals(b, faces=list(b.faces))
    me = bpy.data.meshes.new("tmp"); b.to_mesh(me); b.free(); return me

def cut(o, cutter_me):
    cutter = bpy.data.objects.new("Tmp_Cutter", cutter_me); o.users_collection[0].objects.link(cutter)
    m = o.modifiers.new("Cut", 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.solver = 'EXACT'; m.object = cutter
    bpy.context.view_layer.update()
    new = bpy.data.meshes.new_from_object(o.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    o.modifiers.remove(m); old = o.data; name = old.name; mats = list(old.materials)
    o.data = new; bpy.data.meshes.remove(old); new.name = name
    new.materials.clear()
    for mt in mats: new.materials.append(mt)
    bpy.data.objects.remove(cutter); bpy.data.meshes.remove(cutter_me)

def target_x(z):
    for (z0, x0), (z1, x1) in zip(TARGET, TARGET[1:]):
        if z0 <= z <= z1: return x0 + (x1 - x0) * (z - z0) / (z1 - z0)
    return TARGET[0][1] if z < TARGET[0][0] else TARGET[-1][1]

# ── clearance: the starboard track and fenders; port is its mirror within a few mm ──
track = [o for o in C["Collection 2"].all_objects if o.type == 'MESH' and o.visible_get() and "TrackAssembly" in o.name]
OBSTACLE_EXTRA = ["Mesh_Stair_Ladder_FenderL", "Mesh_Stair_Ladder_FenderR"]
def side_bvh(sign):
    verts, polys, off = [], [], 0
    for o in track + [bpy.data.objects[n] for n in OBSTACLE_EXTRA if n in bpy.data.objects]:
        W = o.matrix_world
        if (W @ Vector(o.bound_box[0])).x * sign < 0 and (W @ Vector(o.bound_box[6])).x * sign < 0: continue
        verts += [W @ v.co for v in o.data.vertices]
        polys += [[i + off for i in p.vertices] for p in o.data.polygons]
        off += len(o.data.vertices)
    return BVHTree.FromPolygons(verts, polys)

zs = sorted(set([round(FLOOR_BOTTOM + Z_STEP * i, 3) for i in range(int((ROOF_TOP - 0.01 - FLOOR_BOTTOM) / Z_STEP) + 1)]
                + [z for z, _ in TARGET]))
n_st = int(round((Y_STERN - Y_FRONT) / STATION_STEP))
ys = [Y_FRONT + (Y_STERN - Y_FRONT) * j / n_st for j in range(n_st + 1)]

def profile(bvh, sign, y):
    """Target profile, except below the top of whatever is under the overhang at this station
    (fender, track, the user's ladder): there the wall tucks inside it and meets it in a ledge.
    Probes span the whole gap to the neighbouring stations, so the quads between them clear too."""
    wide = max(x for _, x in TARGET)
    y_probe = [min(max(y + STATION_STEP * d / 3, Y_FRONT - 0.1), Y_STERN + 0.1) for d in range(-3, 4)]
    xs = [2.0 + (wide + CLEAR - 2.0) * i / 23 for i in range(24)]
    cap, inner = None, wide
    for yy in y_probe:
        for xx in xs:
            hit = bvh.ray_cast(Vector((sign * xx, yy, ROOF_TOP + 1)), Vector((0, 0, -1)), ROOF_TOP + 3)[0]
            if hit is not None and hit.z < ROOF_TOP - 0.3:
                cap = hit.z if cap is None else max(cap, hit.z)
                inner = min(inner, xx - 0.02 - CLEAR)
    if cap is not None:
        z = FLOOR_BOTTOM
        while z <= cap + CLEAR:
            for yy in y_probe:
                hit = bvh.ray_cast(Vector((sign * 1.5, yy, z)), Vector((sign, 0, 0)), 4)[0]
                if hit is not None: inner = min(inner, abs(hit.x) - CLEAR)
            z += 0.03
    ledge = None if cap is None else cap + CLEAR
    CAPS[(sign, round(y, 4))] = ledge
    pts = []
    for z in zs:
        x = target_x(z)
        if ledge is not None and z <= ledge: x = min(x, inner)
        pts.append([max(x, 1.90), z])
    if ledge is not None:
        below = [k for k, (_, z) in enumerate(pts) if z <= ledge]
        above = [k for k, (_, z) in enumerate(pts) if z > ledge]
        if below and above and above[0] < len(pts) - 1:
            pts[below[-1]][1] = ledge
            pts[above[0]][1] = ledge + 0.01
            pts[above[0]][0] = target_x(ledge + 0.01)
    return [tuple(p) for p in pts]
CAPS = {}

def offset_in(poly, t):
    """Offset an xz polyline (x outboard, bottom to top) by t inboard, mitre capped."""
    def nrm(a, b):
        dx, dz = b[0] - a[0], b[1] - a[1]; L = math.hypot(dx, dz) or 1e-9
        n = (-dz / L, dx / L)
        return n if n[0] < 0 else (-n[0], -n[1])
    segs = [nrm(poly[i], poly[i + 1]) for i in range(len(poly) - 1)]
    out = []
    for i, p in enumerate(poly):
        if i == 0: n = segs[0]
        elif i == len(poly) - 1: n = segs[-1]
        else:
            a, b = segs[i - 1], segs[i]; s = (a[0] + b[0], a[1] + b[1]); L = math.hypot(*s)
            if L < 1e-6: n = a
            else:
                k = min(MITRE_MAX, 1.0 / max((a[0] * s[0] + a[1] * s[1]) / L, 1e-6))
                n = (s[0] / L * k, s[1] / L * k)
        out.append((p[0] + n[0] * t, p[1] + n[1] * t))
    return out

def wall_mesh(rows, sign):
    """rows: per station, (outer, inner) xz lists. A closed plate: outer skin, inner skin, rims."""
    b = bmesh.new()
    O = [[b.verts.new((sign * x, y, z)) for x, z in outer] for y, (outer, inner) in zip(ys, rows)]
    I = [[b.verts.new((sign * x, y, z)) for x, z in inner] for y, (outer, inner) in zip(ys, rows)]
    nz = len(zs)
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
    me = bpy.data.meshes.new("tmp"); b.to_mesh(me); b.free(); return me

hull = C["Exterior_RearHull"]; ladders = C["Exterior_Ladders"]; hold = C["Interior_Hold"]

# keep the user's edit of the port wall, then clear everything this rebuild replaces
if "Mesh_RearHull_WallL_UserEdit" not in bpy.data.meshes:
    ue = bpy.data.objects["Mesh_RearHull_WallL"].data.copy(); ue.name = "Mesh_RearHull_WallL_UserEdit"; ue.use_fake_user = True
remove([o.name for o in list(hull.objects) if o.name.startswith(("Mesh_RearHull_Hatch", "Mesh_RearHull_Wall", "Mesh_RearHull_Collar", "Mesh_RearHull_Stern"))])
remove(["Mesh_Stair_Ladder_FenderR", "Mesh_RearHull_FenderStepL", "Mesh_RearHull_FenderStepR"])
remove([o.name for o in list(hold.objects)])

# ── the user's port ladder, mirrored to starboard ──
src = bpy.data.objects["Mesh_Stair_Ladder_FenderL"]
lad = src.copy(); lad.data = src.data.copy(); lad.name = "Mesh_Stair_Ladder_FenderR"; lad.data.name = "Mesh_Stair_Ladder_FenderR"
lad.location = (-src.location.x, src.location.y, src.location.z)
lad.rotation_euler = (src.rotation_euler.x, -src.rotation_euler.y, -src.rotation_euler.z)
ladders.objects.link(lad)

# ── walls ──
profiles = {}
for side, sign in (("R", 1), ("L", -1)):
    bvh = side_bvh(sign)
    rows = []
    for y in ys:
        outer = profile(bvh, sign, y)
        rows.append((outer, offset_in(outer, T_WALL)))
    profiles[side] = rows
    obj("Mesh_RearHull_Wall%s" % side, wall_mesh(rows, sign), rust_orange, hull)

def clip_bottom(pts, zb):
    out = []
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        if z0 < zb <= z1: out.append((x0 + (x1 - x0) * (zb - z0) / (z1 - z0), zb))
        if z1 >= zb: out.append((x1, z1))
    return out

def outline(side_rows_R, side_rows_L, j, inner=False, pad=0.0, bottom=None):
    r = side_rows_R[j][1 if inner else 0]; l = side_rows_L[j][1 if inner else 0]
    if bottom is not None: r, l = clip_bottom(r, bottom), clip_bottom(l, bottom)
    right = [(x + pad, z) for x, z in r]; left = [(-(x + pad), z) for x, z in l]
    return right + list(reversed(left))

# ── stern and collar ──
obj("Mesh_RearHull_Stern", slab(outline(profiles["R"], profiles["L"], len(ys) - 1), Y_STERN - 0.08, Y_STERN), rust_orange, hull)
neck = bpy.data.objects["neck"]; M = neck.matrix_world
nbvh = BVHTree.FromPolygons([M @ v.co for v in neck.data.vertices], [p.vertices for p in neck.data.polygons])
ZC, yr = 1.25, Y_FRONT - 0.02
def neck_r(th, outer):
    d = Vector((math.sin(th), 0, math.cos(th))); c = Vector((0, yr, ZC))
    h = nbvh.ray_cast(c + d * 8, -d, 8)[0] if outer else nbvh.ray_cast(c, d, 8)[0]
    if h is None: raise RuntimeError("collar: neck not found at %.0f deg" % math.degrees(th))
    return (h - c).length
xi = neck_r(math.radians(90), False) + 0.02
hole = [(-xi, DECK - 0.005), (-xi, 1.20)]
for a in range(-90, 91, 5):
    th = math.radians(a); r = neck_r(th, True) - 0.03
    hole.append((math.sin(th) * r, ZC + math.cos(th) * r))
hole += [(xi, 1.20), (xi, DECK - 0.005)]
collar = obj("Mesh_RearHull_Collar", slab(outline(profiles["R"], profiles["L"], 0), Y_FRONT, Y_FRONT + 0.08, hole), rust_orange, hull)
bm_c = bmesh.new(); bm_c.from_mesh(collar.data)
print("collar faces", len(bm_c.faces), "boundary", sum(e.is_boundary for e in bm_c.edges), "nonmanifold", sum(not e.is_manifold for e in bm_c.edges)); bm_c.free()

# ── hold bulkhead: the station nearest HOLD_Y, inner skins, 3 cm into the walls, up into the roof ──
jh = min(range(len(ys)), key=lambda j: abs(ys[j] - HOLD_Y))
ol = outline(profiles["R"], profiles["L"], jh, inner=True, pad=0.03, bottom=DECK - 0.01)
ol = [(x, min(z, ROOF_BOTTOM + 0.03)) for x, z in ol]
bh = obj("Mesh_Hold_Bulkhead", slab(ol, HOLD_Y - 0.04, HOLD_Y + 0.04), rust_orange, hold)
x0, x1, top = HOLD_DOOR
cut(bh, box_me((x0, HOLD_Y - 0.3, DECK - 0.3), (x1, HOLD_Y + 0.3, top)))
for nm, lo, hi in (("JambL", (x0 - FW, HOLD_Y - 0.08, DECK), (x0 + 0.005, HOLD_Y + 0.08, top + FW)),
                   ("JambR", (x1 - 0.005, HOLD_Y - 0.08, DECK), (x1 + FW, HOLD_Y + 0.08, top + FW)),
                   ("Head", (x0 - 0.005, HOLD_Y - 0.078, top - 0.005), (x1 + 0.005, HOLD_Y + 0.078, top + FW - 0.002))):
    obj("Mesh_Hold_DoorFrame_" + nm, box_me(lo, hi), rust_deep, hold)

# ── hatches where the user put the port one, both sides ──
def span(rows, z0, z1):
    js = [j for j, y in enumerate(ys) if HATCH_Y0 - STATION_STEP <= y <= HATCH_Y1 + STATION_STEP]
    ks = [k for k, z in enumerate(zs) if z0 - Z_STEP <= z <= z1 + Z_STEP]
    return (min(rows[j][1][k][0] for j in js for k in ks), max(rows[j][0][k][0] for j in js for k in ks))
for side, sign in (("R", 1), ("L", -1)):
    ledges = [CAPS[(sign, round(y, 4))] for y in ys if HATCH_Y0 - STATION_STEP <= y <= HATCH_Y1 + STATION_STEP and CAPS[(sign, round(y, 4))] is not None]
    HATCH_SILL = max([HATCH_SILL_MIN] + [l + 0.04 for l in ledges])
    print(side, "hatch sill", round(HATCH_SILL, 3))
    xin, xout = span(profiles[side], HATCH_SILL, HATCH_TOP)
    w = bpy.data.objects["Mesh_RearHull_Wall%s" % side]
    a, b = sorted((sign * (xin - 0.3), sign * (xout + 0.3)))
    cut(w, box_me((a, HATCH_Y0, HATCH_SILL), (b, HATCH_Y1, HATCH_TOP)))
    xa, xb = sorted((sign * (xin - 0.01), sign * (xout + 0.04)))
    for nm, lo, hi in (("JambF", (xa, HATCH_Y0 - FW, HATCH_SILL - 0.03), (xb, HATCH_Y0 + 0.005, HATCH_TOP + FW)),
                       ("JambA", (xa, HATCH_Y1 - 0.005, HATCH_SILL - 0.03), (xb, HATCH_Y1 + FW, HATCH_TOP + FW)),
                       ("Head", (xa + 0.002, HATCH_Y0 - 0.005, HATCH_TOP - 0.005), (xb - 0.002, HATCH_Y1 + 0.005, HATCH_TOP + FW - 0.002)),
                       ("Sill", (xa + 0.002, HATCH_Y0 - 0.005, HATCH_SILL - 0.03), (xb - 0.002, HATCH_Y1 + 0.005, HATCH_SILL + 0.005))):
        obj("Mesh_RearHull_Hatch%s_%s" % (side, nm), box_me(lo, hi), rust_deep, hull)

# ── inside ladder, starboard only: the port side under the hatch is the user's jerrican and gas bottles ──
LAD_H, LAD_LEAN = 3.9, 1.59
with bpy.data.libraries.load(os.path.join(LIB, "structural/stair_flight.blend"), link=False) as (s, d): d.collections = ["Coll_Stair_Ladder"]
proto = d.collections[0]; p = proto.all_objects[0]
xin_R = span(profiles["R"], HATCH_SILL, HATCH_SILL)[0]
run_in = 0.9; k_in = (HATCH_SILL - DECK) / LAD_H
m = p.data.copy(); m.transform(Matrix.Diagonal((1.0, run_in / LAD_LEAN, k_in, 1.0)) @ p.matrix_world)
inside = obj("Mesh_Stair_Ladder_HoldR", m, rust_deep, hold)
inside.data.materials.clear()
for mt in p.data.materials: inside.data.materials.append(mt)
inside.matrix_world = Matrix.Translation((xin_R - 0.13 - run_in,   # rails' top hooks clear the fender lip
                                           (HATCH_Y0 + HATCH_Y1) / 2, DECK - 0.002)) @ Matrix.Rotation(math.radians(90), 4, 'Z')
for o in list(proto.all_objects): bpy.data.objects.remove(o)
bpy.data.collections.remove(proto)

bpy.ops.ed.undo_push(message="Rear hull v2: refitted walls, collar, stern, hold wall, hatches, ladders")

# ── report ──
bpy.context.view_layer.update()
def bb(objs):
    ps = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    return [round(min(p[i] for p in ps), 2) for i in range(3)] + [round(max(p[i] for p in ps), 2) for i in range(3)]
for c in (hull, ladders, hold): print(c.name, len(c.objects), bb(list(c.objects)))
for side in ("R", "L"):
    w = bpy.data.objects["Mesh_RearHull_Wall%s" % side]
    bm = bmesh.new(); bm.from_mesh(w.data)
    print(side, "faces", len(bm.faces), "boundary", sum(e.is_boundary for e in bm.edges), "nonmanifold", sum(not e.is_manifold for e in bm.edges)); bm.free()
mid = profiles["R"][len(ys) // 2][0]
print("mid-station outer profile (x, z)", [(round(x, 2), z) for x, z in mid])
print("front-station outer profile", [(round(x, 2), z) for x, z in profiles["R"][0][0]])
dg = bpy.context.evaluated_depsgraph_get()
def tree(o):
    me = o.evaluated_get(dg).data
    return BVHTree.FromPolygons([o.matrix_world @ v.co for v in me.vertices], [q.vertices for q in me.polygons])
mine = list(hull.objects) + list(hold.objects) + [lad]
others = [o for o in C["Collection 2"].all_objects if o.type == 'MESH' and o not in mine and o.visible_get()]
hits = set()
for a in mine:
    ta = tree(a)
    for b in others:
        if ta.overlap(tree(b)): hits.add((a.name, b.name))
print("overlaps", sorted(hits))
