"""Stern castle for Collection 2: a taller section over the end of the base, with a crenellated crown.

Live-session edit (MCP), run after dune_barge_c2_rear_hull.py and dune_barge_c2_dressing.py:
  * the old stern wall goes; a castle section runs from it (y 9.34) to Y_END over the belly's end,
    with the same / | \\ walls fitted to the tracks but standing CASTLE_RISE above the main roof;
  * a front step closes the castle above the main roof; a roof and a floor close it top and bottom;
  * a new stern wall at the end carries the gun port, and the stern gear (mantlet, spare links, tail
    lights, winch) moves there;
  * the stern Autocannon (via its rig bones) moves back into the castle and down onto the user's
    lowered floor, the rocket pod (via its bones) and the user's crane go up onto the castle roof;
  * a crenellated steel parapet — the crown — rings the castle roof;
  * library railings along the main roof's edges, gapped where the roof ladders arrive.
Re-runnable for the parts it creates; moves are guarded so a re-run does not move things twice.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dune_barge_c2_hullkit as K  # noqa: E402

LIB = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "components")
C, O = bpy.data.collections, bpy.data.objects

OLD_STERN_Y = 9.42
Y0 = 9.34                 # the castle starts at the old stern's inner face
Y_END = 11.5              # over the belly's end (10.68), inside the tracks' end (11.58)
DELTA = Y_END - OLD_STERN_Y
CASTLE_RISE = 1.04        # m above the main roof: the vertical band grows by this much
T_WALL, T_ROOF = 0.08, 0.08
STATION_STEP, Z_STEP = 0.3, 0.12
MAIN_TARGET = [(0.50, 1.96), (1.25, 2.46), (2.00, 2.97), (2.63, 2.95), (3.26, 2.92), (3.68, 2.44), (4.15, 1.95)]
CASTLE_TARGET = [(z + (CASTLE_RISE if z > 3.0 else 0.0), x) for z, x in MAIN_TARGET]
CASTLE_TOP = CASTLE_TARGET[-1][0] + 0.01
FLOOR_NOW = O["floor"]
FLOOR_TOP = max((FLOOR_NOW.matrix_world @ v.co).z for v in FLOOR_NOW.data.vertices)
FLOOR_BOTTOM = min((FLOOR_NOW.matrix_world @ v.co).z for v in FLOOR_NOW.data.vertices)
FLOOR_DROP = 0.7156 - FLOOR_TOP          # the user lowered the floor; the gun deck follows it down
PORT_HALF_W, PORT_BELOW, PORT_ABOVE = 0.55, 0.38, 0.76      # the gun port around the trunnion
GUN_TRUNNION_Z = 0.7056 + 1.15 * 1.2 - FLOOR_DROP
MERLON_W, MERLON_GAP, MERLON_H, PARAPET_H, PARAPET_T = 0.30, 0.30, 0.42, 0.22, 0.10
RAIL_SCALE = 1.3
RAIL_X = 1.80
RAIL_SEGMENTS = ((0.55, 2.75), (4.25, 6.55), (6.65, 9.15))     # gap y 2.75..4.25: the roof ladders
RAIL_CLEAR = 0.25         # gap left round anything already on the roof edge
RAIL_MIN = 0.8            # shorter than this is not worth a railing
ARM = O["Arm_DuneBarge2"]

hull_c = C["Exterior_RearHull"]
castle_c = C.get("Exterior_SternCastle") or C.new("Exterior_SternCastle")
if castle_c.name not in C["Exterior"].children: C["Exterior"].children.link(castle_c)
for o in list(castle_c.objects): K.remove([o.name])
rust_orange, rust_deep, rust_heavy, steel_dark = (K.mat(n) for n in (
    "Mat_Metal_HullRust_Orange", "Mat_Metal_Rust_Deep", "Mat_Metal_Rust_Heavy", "Mat_Metal_Steel_Dark"))

# ── walls ──
track = [o for o in C["Collection 2"].all_objects if o.type == 'MESH' and o.visible_get() and "TrackAssembly" in o.name]
zs = sorted(set([round(0.5 + Z_STEP * i, 3) for i in range(int((CASTLE_TOP - 0.51) / Z_STEP) + 1)] + [z for z, _ in CASTLE_TARGET]))
n_st = int(round((Y_END - Y0) / STATION_STEP))
ys = [Y0 + (Y_END - Y0) * j / n_st for j in range(n_st + 1)]
rows = {}
for side, sign in (("R", 1), ("L", -1)):
    bvh = K.side_bvh(track, sign)
    rows[side] = []
    for y in ys:
        outer = K.fit_profile(bvh, sign, y, zs, CASTLE_TARGET, STATION_STEP, (Y0, Y_END), CASTLE_TOP)
        rows[side].append((outer, K.offset_in(outer, T_WALL)))
    K.obj("Mesh_RearHull_CastleWall" + side, K.wall_mesh(rows[side], ys, sign), rust_orange, castle_c)

# ── front step over the main roof, roof, floor ──
top_r = [(x, z) for z, x in CASTLE_TARGET if z >= 3.26]
main_r = [(x, z) for z, x in MAIN_TARGET if z >= 3.26]
# castle top down the port side to the main hull's shoulder, across under the main roof line, up to starboard
step = top_r + [(-x, z) for x, z in reversed(top_r)] + [(-x, z) for x, z in main_r] + list(reversed(main_r))
K.obj("Mesh_RearHull_CastleStep", K.slab(step, Y0, Y0 + T_WALL), rust_orange, castle_c)
roof_x = CASTLE_TARGET[-1][1] - 0.02
K.obj("Mesh_RearHull_CastleRoof", K.box_me((-roof_x, Y0, CASTLE_TOP - T_ROOF), (roof_x, Y_END, CASTLE_TOP - 0.001)), rust_deep, castle_c)
K.obj("Mesh_RearHull_CastleFloor", K.box_me((-1.9, Y0 - 0.02, FLOOR_BOTTOM), (1.9, Y_END - T_WALL, FLOOR_TOP)), steel_dark, C["Interior_Structure"])

# ── the stern moves to the end of the castle, with its gun port ──
K.remove(["Mesh_RearHull_Stern"])
end_outline = K.closed_outline(rows["R"][-1][0])
stern = K.obj("Mesh_RearHull_Stern", K.slab(end_outline, Y_END - T_WALL, Y_END), rust_orange, hull_c)
K.cut(stern, K.box_me((-PORT_HALF_W, Y_END - 0.3, GUN_TRUNNION_Z - PORT_BELOW), (PORT_HALF_W, Y_END + 0.3, GUN_TRUNNION_Z + PORT_ABOVE)))

def shift(names, d):
    for n in names:
        o = O[n]
        if o.get("castle_moved"): continue
        o.matrix_world = Matrix.Translation(d) @ o.matrix_world
        o["castle_moved"] = True

stern_gear = [o.name for o in C["Exterior_Stern"].objects]
shift([n for n in stern_gear if n.startswith(("Mesh_Stern_GunPortMantlet", "Mesh_Link_Grouser_Spare"))], Vector((0, DELTA, -FLOOR_DROP)))
shift([n for n in stern_gear if n.startswith("Mesh_Light_Emergency_Tail")], Vector((0, DELTA, 0)))
winch = O["Mesh_CableDrum_Winch_Stern"]
if not winch.get("castle_moved"):
    ps = [winch.matrix_world @ Vector(c) for c in winch.bound_box]
    lo = Vector([min(p[i] for p in ps) for i in range(3)]); hi = Vector([max(p[i] for p in ps) for i in range(3)])
    c = (lo + hi) / 2
    k = 9.0 / 11.0                                   # a size down, to sit under the gun port
    winch.data.transform(winch.matrix_world.inverted() @ Matrix.Translation(c) @ Matrix.Scale(k, 4) @ Matrix.Translation(-c) @ winch.matrix_world)
    target = Vector((0.0, Y_END + 0.01 + (hi.y - lo.y) * k / 2, FLOOR_TOP + 0.02 + (hi.z - lo.z) * k / 2))
    winch.matrix_world = Matrix.Translation(target - c) @ winch.matrix_world
    winch["castle_moved"] = True

# ── guns and crane: bones for the rigged guns, a translation for the user's crane ──
def move_bones(prefix, d):
    if ARM.get(prefix + "_moved"): return
    bpy.context.view_layer.objects.active = ARM
    with bpy.context.temp_override(active_object=ARM, object=ARM, selected_objects=[ARM]):
        bpy.ops.object.mode_set(mode='EDIT')
        for eb in ARM.data.edit_bones:
            if eb.name.startswith(prefix):
                eb.head += d; eb.tail += d
        bpy.ops.object.mode_set(mode='OBJECT')
    ARM[prefix + "_moved"] = True

move_bones("Bone_Gun_Stern", Vector((0, DELTA, -FLOOR_DROP)))
shift([o.name for o in C["Interior_GunDeck"].objects if o.parent is None], Vector((0, 0, -FLOOR_DROP)))
pod_base = O["Mesh_HeavyGun_RocketPod_Base_Roof"].matrix_world.translation
roof_mid_y = (Y0 + Y_END) / 2
move_bones("Bone_Gun_RoofPod", Vector((1.1, roof_mid_y, CASTLE_TOP - 0.01)) - pod_base)
crane = [o for o in C["Coll_CargoCrane_ScrapDerrick"].all_objects if o.parent is None]
cb = [o.matrix_world @ Vector(c) for o in crane if o.type == 'MESH' and "Base" in o.name for c in o.bound_box]
base_ctr = Vector(((min(p.x for p in cb) + max(p.x for p in cb)) / 2, (min(p.y for p in cb) + max(p.y for p in cb)) / 2, min(p.z for p in cb)))
shift([o.name for o in crane], Vector((-0.95, roof_mid_y, CASTLE_TOP)) - base_ctr)

# ── the crown: a crenellated parapet round the castle roof, and corner posts ──
def parapet(name, a, b, inward):
    """One side: a low wall from a to b (on the roof edge) with merlons along it, as one plate."""
    bm = bmesh.new()
    d = (b - a); L = d.length; u = d / L
    n = Vector((-u.y, u.x, 0)) * (1 if inward else -1)
    def add_box(p0, length, h):
        c = p0 + u * (length / 2) + n * (PARAPET_T / 2) + Vector((0, 0, h / 2))
        m = Matrix.Translation(c) @ Matrix(((u.x, n.x, 0, 0), (u.y, n.y, 0, 0), (0, 0, 1, 0), (0, 0, 0, 1))) @ Matrix.Diagonal((length, PARAPET_T, h, 1))
        bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    add_box(a, L, PARAPET_H)
    count = int((L + MERLON_GAP) // (MERLON_W + MERLON_GAP))
    spare = L - (count * MERLON_W + (count - 1) * MERLON_GAP)
    for i in range(count):
        add_box(a + u * (spare / 2 + i * (MERLON_W + MERLON_GAP)) + Vector((0, 0, PARAPET_H - 0.005)), MERLON_W, MERLON_H)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    me = bpy.data.meshes.new("tmp"); bm.to_mesh(me); bm.free()
    return K.obj(name, me, rust_heavy, castle_c)

z = CASTLE_TOP - 0.01
e = roof_x - 0.02
corners = [Vector((-e, Y0 + 0.02, z)), Vector((e, Y0 + 0.02, z)), Vector((e, Y_END - 0.02, z)), Vector((-e, Y_END - 0.02, z))]
for i, nm in enumerate(("Fore", "Starboard", "Aft", "Port")):
    parapet("Mesh_RearHull_Crown" + nm, corners[i], corners[(i + 1) % 4], inward=True)
for i, p in enumerate(corners):
    post = bmesh.new()
    bmesh.ops.create_cone(post, cap_ends=True, segments=12, radius1=0.14, radius2=0.11, depth=0.95,
                          matrix=Matrix.Translation(p + Vector((0, 0, 0.47))))
    bmesh.ops.create_cone(post, cap_ends=True, segments=12, radius1=0.18, radius2=0.02, depth=0.22,
                          matrix=Matrix.Translation(p + Vector((0, 0, 1.05))))
    me = bpy.data.meshes.new("tmp"); post.to_mesh(me); post.free()
    K.obj("Mesh_RearHull_CrownPost%d" % i, me, steel_dark, castle_c)

# ── railings along the main roof ──
rail_c = C.get("Exterior_Railings") or C.new("Exterior_Railings")
if rail_c.name not in C["Exterior"].children: C["Exterior"].children.link(rail_c)
for o in list(rail_c.objects): K.remove([o.name])
roof = O["Mesh_RearHull_Roof"]
main_top = max((roof.matrix_world @ v.co).z for v in roof.data.vertices)
with bpy.data.libraries.load(os.path.join(LIB, "structural", "handrail.blend"), link=False) as (src, dst):
    dst.collections = ["Coll_Handrail_Straight"]
proto = dst.collections[0]
rail = next(o for o in proto.all_objects if o.type == 'MESH')
rv = [rail.matrix_world @ v.co for v in rail.data.vertices]
r_lo = Vector([min(p[i] for p in rv) for i in range(3)]); r_hi = Vector([max(p[i] for p in rv) for i in range(3)])
def rail_spans(sign):
    """RAIL_SEGMENTS minus a gap round anything already standing on that roof edge (the user's sail
    masts, guys and stakes), dropping pieces too short to be a railing."""
    blocked = []
    for o in C["Collection 2"].all_objects:
        if o.type != 'MESH' or o.name.startswith("Mesh_Handrail_Roof"): continue
        ps = [o.matrix_world @ Vector(c) for c in o.bound_box]
        xs = [p.x * sign for p in ps]; zs_ = [p.z for p in ps]
        if max(xs) >= RAIL_X - RAIL_CLEAR and min(xs) <= RAIL_X + RAIL_CLEAR and max(zs_) > main_top + 0.05 and min(zs_) < main_top + 1.5:
            blocked.append((min(p.y for p in ps) - RAIL_CLEAR, max(p.y for p in ps) + RAIL_CLEAR))
    spans = list(RAIL_SEGMENTS)
    for b0, b1 in blocked:
        spans = [piece for a, b in spans for piece in ((a, min(b, b0)), (max(a, b1), b)) if piece[1] - piece[0] > 0]
    return [(a, b) for a, b in spans if b - a >= RAIL_MIN]

for side, s in (("R", 1), ("L", -1)):
    for i, (a, b) in enumerate(rail_spans(s)):
        sy = (b - a) / (r_hi.y - r_lo.y)
        me = rail.data.copy()
        M = (Matrix.Translation((s * RAIL_X, (a + b) / 2, main_top - 0.005))
             @ Matrix.Diagonal((RAIL_SCALE, sy, RAIL_SCALE, 1))
             @ Matrix.Translation((-(r_lo.x + r_hi.x) / 2, -(r_lo.y + r_hi.y) / 2, -r_lo.z)) @ rail.matrix_world)
        me.transform(M)
        o = bpy.data.objects.new("Mesh_Handrail_Roof%s%d" % (side, i), me); me.name = o.name
        rail_c.objects.link(o)
for o in list(proto.all_objects): bpy.data.objects.remove(o)
bpy.data.collections.remove(proto)

bpy.ops.ed.undo_push(message="Stern castle with crown; stern gear, stern gun, rocket pod and crane moved; roof railings")

# ── report ──
bpy.context.view_layer.update()
def bb(objs):
    ps = [o.matrix_world @ Vector(c) for o in objs if o.type == 'MESH' for c in o.bound_box]
    return [round(min(p[i] for p in ps), 2) for i in range(3)] + [round(max(p[i] for p in ps), 2) for i in range(3)]
print("castle", bb(list(castle_c.objects)), "top", round(CASTLE_TOP, 2), "floor drop", round(FLOOR_DROP, 3))
print("stern", bb([O["Mesh_RearHull_Stern"]]), "stern gear", bb(list(C["Exterior_Stern"].objects)))
print("gun deck", bb(list(C["Interior_GunDeck"].all_objects)), "crane", bb(crane), "railings", bb(list(rail_c.objects)))
from mathutils.bvhtree import BVHTree
dg = bpy.context.evaluated_depsgraph_get()
def tree(o):
    me = o.evaluated_get(dg).data
    return BVHTree.FromPolygons([o.matrix_world @ v.co for v in me.vertices], [q.vertices for q in me.polygons])
mine = list(castle_c.objects) + list(rail_c.objects) + [O["Mesh_RearHull_Stern"], O["Mesh_RearHull_CastleFloor"]] \
       + list(C["Exterior_Stern"].objects) + list(C["Interior_GunDeck"].all_objects) + crane
mine = [o for o in mine if o.type == 'MESH']
names = {o.name for o in mine}
others = [o for o in C["Collection 2"].all_objects if o.type == 'MESH' and o.name not in names and o.visible_get()]
hits = set()
for a in mine:
    ta = tree(a)
    for b in others:
        if ta.overlap(tree(b)): hits.add((a.name, b.name))
embed = ("Mesh_RearHull_", "Mesh_HullBelly", "floor", "Mesh_Neck")
print("overlaps (non-embed):", sorted(h for h in hits if not h[1].startswith(embed)))
