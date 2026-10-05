import bpy, bmesh, math, os
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

LIB = r"C:\Users\tobia\Documents\spaceGame\SpaceGame\Assets\Game\Art\Models\_Source~\components"
C = bpy.data.collections
DECK = 0.7156                    # top of 'floor'
COCKPIT = 2.2825                 # top of Cube.002
EDGE_Y = -2.0924                 # rear edge of Cube.002
PITCH = 32.0                     # walkable: the player ship's boarding ramp is 32 degrees
STAIR_RISE, STAIR_STEP_RISE, STAIR_STEP_RUN, STAIR_LEN = 2.1, 0.3, 0.4286, 3.249   # Coll_Stair_Grating as authored
WALL_T = 0.08
STAIR_X0, STAIR_X1 = -0.07, 0.91          # the gap between the user's V8 (port) and Inline6 (starboard)
STAIR_X = (STAIR_X0 + STAIR_X1) / 2
STAIR_W_AUTHORED = 1.6
DOOR_HALF_W, DOOR_TOP = 0.6, DECK + 3.1   # 3.1 m clear: the player is 3 m tall

def coll(name, parent):
    c = C.get(name)
    if c is None:
        c = C.new(name); C[parent].children.link(c)
    for o in list(c.objects): bpy.data.objects.remove(o)   # only this script's previous pass
    return c

def mat(base):
    for m in bpy.data.materials:
        if m.name == base or m.name.startswith(base + "."):
            return m
    raise RuntimeError("palette material missing: " + base)

def place(rel, cname, matrix, suffix, scale, into):
    with bpy.data.libraries.load(os.path.join(LIB, rel), link=False) as (src, dst):
        dst.collections = [cname]
    proto = dst.collections[0]; objs = list(proto.all_objects)
    worlds = {o: o.matrix_world.copy() for o in objs}; made = {}
    S = Matrix.Diagonal(Vector(scale).to_4d()) if hasattr(scale, "__len__") else Matrix.Scale(scale, 4)
    for o in objs:
        n = o.copy()
        if o.type == 'MESH':
            n.data = o.data.copy()
            n.data.transform(S @ worlds[o])          # bake proto transform + scale into the mesh
        n.name = "%s_%s" % (o.name, suffix); into.objects.link(n); made[o] = n
    for o, n in made.items():
        n.parent = None
        n.matrix_world = matrix if o.type == 'MESH' else matrix @ S @ worlds[o]
    for o in objs: bpy.data.objects.remove(o)
    bpy.data.collections.remove(proto)
    bpy.context.view_layer.update()
    return list(made.values())

def rz(deg): return Matrix.Rotation(math.radians(deg), 4, 'Z')
def T(x, y, z): return Matrix.Translation((x, y, z))

stair_c = coll("Interior_Stairwell", "Interior")
mach_c = coll("Interior_Machinery", "Interior")

# ── stair: the Grating flight fitted to the deck-to-cockpit rise at PITCH ──
sz = (COCKPIT - DECK) / STAIR_RISE
sy = (STAIR_STEP_RISE * sz / math.tan(math.radians(PITCH))) / STAIR_STEP_RUN
foot_y = EDGE_Y + STAIR_LEN * sy
sx = (STAIR_X1 - STAIR_X0) / STAIR_W_AUTHORED
stair = place("structural/stair_flight.blend", "Coll_Stair_Grating", T(STAIR_X, foot_y, DECK - 0.002), "Neck", (sx, sy, sz), stair_c)

# ── bulkhead across the neck's open rear end, with a 3.1 m doorway ──
neck = bpy.data.objects["neck"]
M = neck.matrix_world
bvh = BVHTree.FromPolygons([M @ v.co for v in neck.data.vertices], [p.vertices for p in neck.data.polygons])
rear = max((M @ v.co).y for v in neck.data.vertices)
wall_y1 = rear + 0.05; wall_y0 = wall_y1 - WALL_T   # just aft of the user's engines, which reach y 0.28
ZC = 1.25
arch = []
for a in range(-90, 91, 5):
    th = math.radians(a); d = Vector((math.sin(th), 0, math.cos(th)))
    c = Vector((0, wall_y0 - 0.01, ZC))
    hit = bvh.ray_cast(c, d, 8)[0]
    if hit is None: raise RuntimeError("bulkhead: no neck wall at %d deg" % a)
    r = (hit - c).length + 0.03                      # 3 cm into the neck wall, never short of it
    arch.append((math.sin(th) * r, ZC + math.cos(th) * r))
outline = [(arch[0][0], DECK - 0.01), (arch[-1][0], DECK - 0.01)] + list(reversed(arch))
hull_c = C["Exterior_Hull"]
for o in [o for o in bpy.data.objects if o.name.startswith(("Mesh_Neck_Bulkhead", "Mesh_Neck_DoorFrame", "Tmp_DoorCutter"))]:
    bpy.data.objects.remove(o)
for m in [m for m in bpy.data.meshes if m.name.startswith(("Mesh_Neck_Bulkhead", "Mesh_Neck_DoorFrame", "Tmp_DoorCutter")) and m.users == 0]:
    bpy.data.meshes.remove(m)
def slab(name, outline_xz, y0, y1):
    b = bmesh.new()
    f0 = [b.verts.new((x, y0, z)) for x, z in outline_xz]
    f1 = [b.verts.new((x, y1, z)) for x, z in outline_xz]
    b.faces.new(f0); b.faces.new(list(reversed(f1)))
    for i in range(len(outline_xz)):
        j = (i + 1) % len(outline_xz)
        b.faces.new((f0[i], f0[j], f1[j], f1[i]))
    bmesh.ops.recalc_face_normals(b, faces=list(b.faces))
    m = bpy.data.meshes.new(name); b.to_mesh(m); b.free()
    return m
me = slab("Mesh_Neck_Bulkhead", outline, wall_y0, wall_y1)
me.materials.append(mat("Mat_Metal_HullRust_Orange"))
wall = bpy.data.objects.new("Mesh_Neck_Bulkhead", me); hull_c.objects.link(wall)
cut_me = slab("Tmp_DoorCutter", [(STAIR_X - DOOR_HALF_W, DECK - 0.5), (STAIR_X + DOOR_HALF_W, DECK - 0.5), (STAIR_X + DOOR_HALF_W, DOOR_TOP), (STAIR_X - DOOR_HALF_W, DOOR_TOP)], wall_y0 - 0.2, wall_y1 + 0.2)
cutter = bpy.data.objects.new("Tmp_DoorCutter", cut_me); hull_c.objects.link(cutter)
mod = wall.modifiers.new("DoorCut", 'BOOLEAN'); mod.operation = 'DIFFERENCE'; mod.solver = 'EXACT'; mod.object = cutter
bpy.context.view_layer.update()
cut = bpy.data.meshes.new_from_object(wall.evaluated_get(bpy.context.evaluated_depsgraph_get()))
wall.modifiers.remove(mod); wall.data = cut; bpy.data.meshes.remove(me); cut.name = "Mesh_Neck_Bulkhead"
cut.materials.clear(); cut.materials.append(mat("Mat_Metal_HullRust_Orange"))   # new_from_object drops the slot
bpy.data.objects.remove(cutter); bpy.data.meshes.remove(cut_me)

# doorway frame: jambs and head proud of the wall on both faces
def box(name, lo, hi, material, into):
    b = bmesh.new(); bmesh.ops.create_cube(b, size=1.0)
    for v in b.verts:
        v.co = Vector(((lo[0] + hi[0]) / 2 + v.co.x * (hi[0] - lo[0]), (lo[1] + hi[1]) / 2 + v.co.y * (hi[1] - lo[1]), (lo[2] + hi[2]) / 2 + v.co.z * (hi[2] - lo[2])))
    m = bpy.data.meshes.new(name); b.to_mesh(m); b.free(); m.materials.append(material)
    o = bpy.data.objects.new(name, m); into.objects.link(o); return o
FW, FP = 0.12, 0.04          # frame bar width, how far it stands proud of the outer face (inside, the engines sit close)
rust = mat("Mat_Metal_Rust_Deep")
box("Mesh_Neck_DoorFrame_JambL", (STAIR_X - DOOR_HALF_W - FW, wall_y0 - 0.01, DECK), (STAIR_X - DOOR_HALF_W + 0.005, wall_y1 + FP, DOOR_TOP + FW), rust, hull_c)
box("Mesh_Neck_DoorFrame_JambR", (STAIR_X + DOOR_HALF_W - 0.005, wall_y0 - 0.01, DECK), (STAIR_X + DOOR_HALF_W + FW, wall_y1 + FP, DOOR_TOP + FW), rust, hull_c)
box("Mesh_Neck_DoorFrame_Head", (STAIR_X - DOOR_HALF_W - 0.005, wall_y0 - 0.012, DOOR_TOP - 0.005), (STAIR_X + DOOR_HALF_W + 0.005, wall_y1 + FP + 0.002, DOOR_TOP + FW - 0.002), rust, hull_c)

if not mach_c.objects: C["Interior"].children.unlink(mach_c); C.remove(mach_c); mach_c = None

bpy.ops.ed.undo_push(message="Neck stairwell, bulkhead and machinery")

# ── report ──
def bb(objs):
    ps = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    return [round(min(p[i] for p in ps), 2) for i in range(3)] + [round(max(p[i] for p in ps), 2) for i in range(3)]
print("stair", bb(stair), "sy %.3f sz %.3f foot_y %.2f" % (sy, sz, foot_y))
print("pitch check %.1f deg, riser %.3f, going %.3f" % (math.degrees(math.atan((STAIR_STEP_RISE * sz) / (STAIR_STEP_RUN * sy))), STAIR_STEP_RISE * sz, STAIR_STEP_RUN * sy))
print("wall", bb([wall]), "rear", round(rear, 3))
dg = bpy.context.evaluated_depsgraph_get()
def tree(o):
    m = o.evaluated_get(dg).data
    return BVHTree.FromPolygons([o.matrix_world @ v.co for v in m.vertices], [p.vertices for p in m.polygons])
mine = list(stair_c.objects) + [o for o in hull_c.objects if o.name.startswith("Mesh_Neck_")]
others = [o for o in C["Collection 2"].all_objects if o.type == 'MESH' and o not in mine and o.visible_get()]
hits = set()
for a in mine:
    ta = tree(a)
    for b in others:
        if ta.overlap(tree(b)): hits.add((a.name, b.name))
print("overlaps", sorted(hits))
