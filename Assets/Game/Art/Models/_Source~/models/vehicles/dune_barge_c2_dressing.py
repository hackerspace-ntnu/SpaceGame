"""Collection 2 dressing: steel doors and hatches, a stern gun deck, rear armament and stern gear,
vision slits along the sides. Live-session edit (MCP); every part is a library component except the
gun-port mantlet, which is cut to this stern."""
import bpy, bmesh, math, os, sys
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

SRC = r"C:\Users\tobia\Documents\spaceGame\SpaceGame\Assets\Game\Art\Models\_Source~"
LIB = os.path.join(SRC, "components")
if SRC not in sys.path: sys.path.insert(0, SRC)
import _buildlib as BL

C = bpy.data.collections
DECK = 0.7156
ROOF = bpy.data.objects["Mesh_RearHull_Roof"]
ROOF_TOP = max((ROOF.matrix_world @ v.co).z for v in ROOF.data.vertices)
ROOF_BOTTOM = min((ROOF.matrix_world @ v.co).z for v in ROOF.data.vertices)
Y_STERN = 9.42
CREW = 1.5            # library props are authored for 2 m crew; the player is 3 m
DOOR_SZ = 0.85        # door frames: 4.0 m authored -> 3.4 m, header just into the 4.08 ceiling
NECK_DOOR = (0.42, 0.33)      # doorway centre x, wall centre y (Mesh_Neck_Bulkhead)
HOLD_DOOR = (0.80, 4.00)      # (Mesh_Hold_Bulkhead)
HATCH = (1.2, 2.91)           # side hatch centre y, z
HATCH_SCALE = 0.55
LID_OPEN = -110.0

def mat(base):
    for m in bpy.data.materials:
        if m.name == base or m.name.startswith(base + "."):
            return m
    raise RuntimeError("palette material missing: " + base)

def coll(name, parent):
    c = C.get(name)
    if c is None:
        c = C.new(name); C[parent].children.link(c)
    for o in list(c.all_objects):
        me = o.data; bpy.data.objects.remove(o)
        if me is not None and me.users == 0: bpy.data.meshes.remove(me)
    return c

def remove(names):
    for n in names:
        o = bpy.data.objects.get(n)
        if o is not None:
            me = o.data; bpy.data.objects.remove(o)
            if me is not None and me.users == 0: bpy.data.meshes.remove(me)

def T(x, y, z): return Matrix.Translation((x, y, z))
def R(deg, axis): return Matrix.Rotation(math.radians(deg), 4, axis)

def load(rel, cname):
    with bpy.data.libraries.load(os.path.join(LIB, rel), link=False) as (src, dst):
        dst.collections = [cname]
    return dst.collections[0]

def drop(proto):
    for o in list(proto.all_objects): bpy.data.objects.remove(o)
    bpy.data.collections.remove(proto)

def place_baked(rel, cname, placements, into, suffix):
    """Unparented parts. placements: {object name in the component: 4x4 applied after its own
    transform (may carry non-uniform scale, baked into the mesh)}; '*' for all."""
    proto = load(rel, cname); made = []
    for o in proto.all_objects:
        if o.type != 'MESH': continue
        M = placements.get(o.name, placements.get("*"))
        if M is None: continue
        n = o.copy(); n.data = o.data.copy(); n.parent = None
        n.data.transform(M @ o.matrix_world); n.matrix_world = Matrix.Identity(4)
        n.name = n.data.name = "%s_%s" % (o.name, suffix)
        into.objects.link(n); made.append(n)
    drop(proto)
    return made

def place_rigged(rel, cname, matrix, scale, into, suffix):
    """Keeps the component's parent chain (e.g. Base > Cradle > barrels) so it can still be rigged.
    Uniform scale is baked into each mesh; every object keeps its own pivot."""
    proto = load(rel, cname); objs = list(proto.all_objects)
    def world(o):
        return (world(o.parent) @ o.matrix_parent_inverse if o.parent else Matrix.Identity(4)) @ o.matrix_basis
    worlds = {o: world(o) for o in objs}; made = {}
    for o in objs:
        n = o.copy()
        if o.type == 'MESH':
            n.data = o.data.copy(); n.data.transform(Matrix.Scale(scale, 4))
        n.name = "%s_%s" % (o.name, suffix)
        if n.data is not None: n.data.name = n.name
        into.objects.link(n); made[o] = n
    for o, n in made.items():
        w = worlds[o]; n.parent = None
        n.matrix_world = matrix @ Matrix.Translation(w.translation * scale) @ w.to_quaternion().to_matrix().to_4x4()
    bpy.context.view_layer.update()
    for o, n in made.items():
        if o.parent in made:
            p = made[o.parent]; w = n.matrix_world.copy(); n.parent = p
            n.matrix_parent_inverse = p.matrix_world.inverted(); n.matrix_world = w
    drop(proto)
    bpy.context.view_layer.update()
    return list(made.values())

def bbox(objs):
    ps = [o.matrix_world @ Vector(c) for o in objs if o.type == 'MESH' for c in o.bound_box]
    return (Vector([min(p[i] for p in ps) for i in range(3)]), Vector([max(p[i] for p in ps) for i in range(3)]))

def shift(objs, d):
    for o in objs:
        if o.parent is None or o.parent not in objs:
            o.matrix_world = Matrix.Translation(d) @ o.matrix_world
    bpy.context.view_layer.update()

def surface_bvh(names):
    verts, polys, off = [], [], 0
    for n in names:
        o = bpy.data.objects[n]; W = o.matrix_world
        verts += [W @ v.co for v in o.data.vertices]; polys += [[i + off for i in p.vertices] for p in o.data.polygons]
        off += len(o.data.vertices)
    return BVHTree.FromPolygons(verts, polys)

def cut(o, lo, hi):
    b = bmesh.new(); bmesh.ops.create_cube(b, size=1.0)
    for v in b.verts:
        v.co = Vector(((lo[i] + hi[i]) / 2 + v.co[i] * (hi[i] - lo[i]) for i in range(3)))
    cm = bpy.data.meshes.new("Tmp_Cutter"); b.to_mesh(cm); b.free()
    cutter = bpy.data.objects.new("Tmp_Cutter", cm); o.users_collection[0].objects.link(cutter)
    m = o.modifiers.new("Cut", 'BOOLEAN'); m.operation = 'DIFFERENCE'; m.solver = 'EXACT'; m.object = cutter
    bpy.context.view_layer.update()
    new = bpy.data.meshes.new_from_object(o.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    o.modifiers.remove(m); old = o.data; name = old.name; mats = list(old.materials)
    o.data = new; bpy.data.meshes.remove(old); new.name = name
    new.materials.clear()
    for mt in mats: new.materials.append(mt)
    bpy.data.objects.remove(cutter); bpy.data.meshes.remove(cm)

# ── clear what this replaces: the plain box door and hatch frames ──
remove([o.name for o in bpy.data.objects if o.name.startswith(("Mesh_RearHull_Hatch", "Mesh_Hold_DoorFrame_", "Mesh_Neck_DoorFrame_"))
        or o.name.endswith(("_NeckDoor", "_HoldDoor", "_HatchR", "_HatchL"))])
remove(["Mesh_Stair_Ladder_HoldR"])

# ── 1. steel bulkhead doors in both doorways (hull_door Bulkhead: frame + leaf, leaf pivot on its hinge) ──
FRAME_CLEAR_HALF, LEAF_HINGE_X = 1.1, 1.3
def door(center_x, wall_y, into, suffix):
    sx = 1.2 / (2 * FRAME_CLEAR_HALF)                    # 1.2 m clear, like the openings cut for them
    S = Matrix.Diagonal((sx, 0.55, DOOR_SZ, 1.0))
    base = T(center_x, wall_y + 0.095 * 0.55, DECK - 0.005)   # frame's own depth is centred on the wall
    hinge = base @ T(LEAF_HINGE_X * sx, 0, 0)                # right-hand jamb
    leaf = hinge @ R(180, 'Z') @ S                          # shut: the user's bunks and hammock sit where it would swing
    parts = place_baked("structural/hull_door.blend", "Coll_Door_Bulkhead",
                        {"Mesh_Door_Bulkhead_Frame": base @ S, "Mesh_Door_Bulkhead_Leaf": leaf}, into, suffix)
    return parts
door_parts = door(NECK_DOOR[0], NECK_DOOR[1], C["Exterior_Hull"], "NeckDoor")
door_parts += door(HOLD_DOOR[0], HOLD_DOOR[1], C["Interior_Hold"], "HoldDoor")

# ── 2. armoured side hatches (hull_door Hatch_Roof turned onto each wall, lid propped open) ──
walls_bvh = surface_bvh(["Mesh_RearHull_WallR", "Mesh_RearHull_WallL"])
hatch_parts = []
for side, s in (("R", 1), ("L", -1)):
    hit = walls_bvh.ray_cast(Vector((s * 6, HATCH[0] - 0.7, HATCH[1])), Vector((-s, 0, 0)), 6)[0]   # beside the hole, same height
    base_x = abs(hit.x) - 0.12                                # coaming sits 12 cm into the wall
    Rw = R(90 * s, 'Y') @ R(90 * s, 'Z')                      # local +Z outboard, local +X along the hull
    Cm = T(s * base_x, HATCH[0], HATCH[1]) @ Rw @ Matrix.Scale(HATCH_SCALE, 4)
    Lm = Cm @ T(0, 1.085, 0.49) @ R(LID_OPEN, 'X')
    hatch_parts += place_baked("structural/hull_door.blend", "Coll_Hatch_Roof",
                               {"Mesh_Hatch_Roof_Coaming": Cm, "Mesh_Hatch_Roof_Lid": Lm}, C["Exterior_RearHull"], "Hatch" + side)

# inside ladder, starboard, clear of the wall (port is the user's jerrican and gas bottles)
hit = walls_bvh.ray_cast(Vector((0.5, HATCH[0], 2.15)), Vector((1, 0, 0)), 4)[0]   # just under the hatch sill: the hole starts at 2.32
LAD_H, LAD_LEAN = 3.9, 1.59
k_in = (2.32 - DECK) / LAD_H; run_in = 0.9
place_baked("structural/stair_flight.blend", "Coll_Stair_Ladder",
            {"*": T(hit.x - 0.15 - run_in, HATCH[0], DECK - 0.002) @ R(90, 'Z') @ Matrix.Diagonal((1.0, run_in / LAD_LEAN, k_in, 1.0))},
            C["Interior_Hold"], "HoldR")

# ── 3. gun deck: the Autocannon at the stern, firing aft through a port ──
gun_c = coll("Interior_GunDeck", "Interior")
GUN_Y = 8.82          # the strip aft of the user's paint workshop
GUN_SCALE = 1.2
gun = place_rigged("mechanical/heavy_gun.blend", "Coll_HeavyGun_Autocannon", T(0, GUN_Y, DECK - 0.01) @ R(180, 'Z'), GUN_SCALE, gun_c, "Stern")
trunnion_z = DECK - 0.01 + 1.15 * GUN_SCALE
stern = bpy.data.objects["Mesh_RearHull_Stern"]
PORT = (-0.55, 0.55, trunnion_z - 0.38, 2.85)   # top matches the first pass's cut, already in the stern
cut(stern, (PORT[0], Y_STERN - 0.3, PORT[2]), (PORT[1], Y_STERN + 0.3, PORT[3]))
# armoury against the starboard wall, behind the user's paint pots: an open rack and upright launch tubes
hit = walls_bvh.ray_cast(Vector((0.5, 7.8, 0.75)), Vector((1, 0, 0)), 4)[0]   # at floor level: the lower wall slants out above it
place_baked("props/wall_locker.blend", "Coll_WallLocker_OpenShelf",
            {"*": T(hit.x - 0.02, 7.8, DECK - 0.005) @ R(180, 'Z') @ Matrix.Scale(1.3, 4)}, gun_c, "Armoury")
for i, (x, y, cname) in enumerate(((1.62, 8.72, "Coll_LaunchTube_Banded"), (1.66, 9.08, "Coll_LaunchTube_Vented"), (1.30, 8.98, "Coll_LaunchTube_Banded"))):
    place_baked("mechanical/launch_tube.blend", cname, {"Mesh_" + cname[5:]: T(x, y, DECK + 0.005) @ R(-90, 'X') @ Matrix.Scale(CREW, 4)}, gun_c, "Rack%d" % i)

# ── 4. the back: gun-port mantlet, winch, spare links, tail lights; rocket pod on the roof ──
stern_c = coll("Exterior_Stern", "Exterior")
steel_dark, steel_worn, rust_heavy = mat("Mat_Metal_Steel_Dark"), mat("Mat_Metal_Steel_Worn"), mat("Mat_Metal_Rust_Heavy")
p = BL.Part([steel_dark, steel_worn, rust_heavy])
y0, d = Y_STERN - 0.01, 0.16
x0, x1, z0, z1 = PORT
bw = 0.16
for lo, hi in (((x0 - bw, y0, z0 - bw), (x0, y0 + d, z1 + bw)), ((x1, y0, z0 - bw), (x1 + bw, y0 + d, z1 + bw)),
               ((x0 - 0.002, y0, z1), (x1 + 0.002, y0 + d, z1 + bw)), ((x0 - 0.002, y0, z0 - bw), (x1 + 0.002, y0 + d, z0))):
    p.slab(lo, hi, 0)
p.slab((x0 - bw - 0.1, y0 + d - 0.001, z0 - bw - 0.04), (x1 + bw + 0.1, y0 + d + 0.03, z0 - bw + 0.02), 2)   # drip ledge
for zz in (z0 - bw / 2, z1 + bw / 2):
    p.rivets((x0 - bw / 2, y0 + d, zz), (x1 + bw / 2, y0 + d, zz), 8, 0.022, 0.018, 'Y', 1)
for xx in (x0 - bw / 2, x1 + bw / 2):
    p.rivets((xx, y0 + d, z0), (xx, y0 + d, z1), 4, 0.022, 0.018, 'Y', 1)
p.bevel(width=0.01)
p.finish("Mesh_Stern_GunPortMantlet", stern_c)

stern_bvh = surface_bvh(["Mesh_RearHull_Stern"])
def on_stern(x, z):
    h = stern_bvh.ray_cast(Vector((x, Y_STERN + 3, z)), Vector((0, -1, 0)), 5)[0]
    return h.y if h is not None else Y_STERN
# winch drum, low and centred (cable_drum Winch is authored ~0.1 m; a tow winch is ~1.2 m)
w = place_baked("props/cable_drum.blend", "Coll_CableDrum_Winch", {"*": Matrix.Scale(11.0, 4)}, stern_c, "Stern")
belly = surface_bvh(["Mesh_HullBelly.001"])
ledge = belly.ray_cast(Vector((1.3, 10.0, 5)), Vector((0, 0, -1)), 6)[0].z
lo, hi = bbox(w); shift(w, Vector((1.3 - (lo.x + hi.x) / 2, Y_STERN + 0.25 - lo.y, ledge - 0.01 - lo.z)))
# spare track links bolted either side of the mantlet, three high
for side, s in (("L", -1), ("R", 1)):
    for k in range(3):
        z = trunnion_z - 0.55 + k * 0.48
        l = place_baked("mechanical/track_link.blend", "Coll_Link_Grouser", {"*": R(-90, 'X') @ Matrix.Scale(0.9, 4)}, stern_c, "Spare%s%d" % (side, k))
        lo, hi = bbox(l); shift(l, Vector((s * 1.55 - (lo.x + hi.x) / 2, on_stern(s * 1.55, z) - 0.02 - lo.y, z - (lo.z + hi.z) / 2)))
# tail lights at the stern's lower corners, facing aft
for side, s in (("L", -1), ("R", 1)):
    t = place_baked("props/light_fixture.blend", "Coll_Light_Emergency", {"*": R(-90, 'X') @ Matrix.Scale(1.4, 4)}, stern_c, "Tail" + side)
    lo, hi = bbox(t); shift(t, Vector((s * 2.25 - (lo.x + hi.x) / 2, on_stern(s * 2.25, 1.9) - 0.02 - lo.y, 1.9 - (lo.z + hi.z) / 2)))
# rocket pod on the rear roof, trained aft
pod = place_rigged("mechanical/heavy_gun.blend", "Coll_HeavyGun_RocketPod", T(1.15, 8.75, ROOF_TOP - 0.01) @ R(180, 'Z'), 1.0, stern_c, "Roof")   # beside the user's crane, aft of the canopy

# ── 5. vision slits and shutters along both sides (decorative, no openings) ──
side_c = coll("Exterior_SideDressing", "Exterior")
SLITS = [(2.45, "Coll_Window_Shuttered"), (3.35, "Coll_Window_Slot"), (5.0, "Coll_Window_Barred"),
         (5.95, "Coll_Window_Shuttered"), (6.9, "Coll_Window_Slot"), (7.8, "Coll_Window_Barred")]
SLIT_Z, SLIT_SCALE = 2.93, 0.5
for side, s in (("L", -1), ("R", 1)):
    for i, (y, cname) in enumerate(SLITS):
        cname = SLITS[(i + (1 if s > 0 else 0)) % len(SLITS)][1]      # the two sides don't mirror
        hit = walls_bvh.ray_cast(Vector((s * 6, y, SLIT_Z)), Vector((-s, 0, 0)), 6)[0]
        M = T(hit.x - s * 0.01, y, SLIT_Z) @ R(90 * s, 'Z') @ Matrix.Scale(SLIT_SCALE, 4)
        place_baked("structural/hull_window.blend", cname, {"*": M}, side_c, "%s%d" % (side, i))

bpy.ops.ed.undo_push(message="Collection 2 dressing: doors, hatches, gun deck, stern, vision slits")

# ── report ──
for c in ("Interior_GunDeck", "Exterior_Stern", "Exterior_SideDressing"):
    print(c, len(C[c].objects), [tuple(round(v, 2) for v in b) for b in bbox(list(C[c].objects))])
print("doors", [tuple(round(v, 2) for v in b) for b in bbox(door_parts)], "hatches", [tuple(round(v, 2) for v in b) for b in bbox(hatch_parts)])
print("gun", [tuple(round(v, 2) for v in b) for b in bbox(gun)], "pod", [tuple(round(v, 2) for v in b) for b in bbox(pod)])
dg = bpy.context.evaluated_depsgraph_get()
def tree(o):
    me = o.evaluated_get(dg).data
    return BVHTree.FromPolygons([o.matrix_world @ v.co for v in me.vertices], [q.vertices for q in me.polygons])
mine = [o for n in ("Interior_GunDeck", "Exterior_Stern", "Exterior_SideDressing") for o in C[n].objects if o.type == 'MESH'] \
       + door_parts + hatch_parts + [bpy.data.objects["Mesh_Stair_Ladder_HoldR"]]
others = [o for o in C["Collection 2"].all_objects if o.type == 'MESH' and o not in mine and o.visible_get()]
hits = set()
for a in mine:
    ta = tree(a)
    for b in others:
        if ta.overlap(tree(b)): hits.add((a.name, b.name))
print("overlaps", sorted(hits))
