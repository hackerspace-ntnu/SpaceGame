import bpy, bmesh, math
from mathutils import Vector
from mathutils.bvhtree import BVHTree

o = bpy.data.objects["neck"]
src = bpy.data.meshes.get("Mesh_DeviceClamp_Strap_Ribs") or o.data
if src.name != "Mesh_DeviceClamp_Strap_Ribs":
    src.name = "Mesh_DeviceClamp_Strap_Ribs"; src.use_fake_user = True   # the capped rib version, kept
M = o.matrix_world
bvh = BVHTree.FromPolygons([M @ v.co for v in src.vertices], [p.vertices for p in src.polygons])
ys = [v.co.y for v in src.vertices]
Y0 = min((M @ v.co).y for v in src.vertices) + 0.04
Y1 = max((M @ v.co).y for v in src.vertices) - 0.04
ZB = min((M @ v.co).z for v in src.vertices)
ZC = 1.25
ANG = [math.radians(a) for a in range(-90, 91, 5)]
NY = 16
MIN_WALL = 0.12

def radii(y):
    out, inn = [], []
    c = Vector((0, y, ZC))
    for th in ANG:
        d = Vector((math.sin(th), 0, math.cos(th)))
        ho = bvh.ray_cast(c + d * 8, -d, 8)[0]
        hi = bvh.ray_cast(c, d, 8)[0]
        if ho is None or hi is None:
            raise RuntimeError("no hit at y=%.2f th=%.0f" % (y, math.degrees(th)))
        out.append((ho - c).length); inn.append((hi - c).length)
    return out, inn

def smooth(seq, passes=2):
    for _ in range(passes):
        seq = [seq[0]] + [(seq[i - 1] + 2 * seq[i] + seq[i + 1]) / 4 for i in range(1, len(seq) - 1)] + [seq[-1]]
    return seq

rows = []
for j in range(NY):
    y = Y0 + (Y1 - Y0) * j / (NY - 1)
    out, inn = radii(y)
    rows.append((y, out, inn))
# smooth along the angle within each station, then along y across stations
rows = [(y, smooth(o_), smooth(i_)) for y, o_, i_ in rows]
for k in range(len(ANG)):
    for arr in (1, 2):
        col = smooth([r[arr][k] for r in rows], 1)
        for j, r in enumerate(rows): r[arr][k] = col[j]
for y, out, inn in rows:
    for k in range(len(ANG)):
        inn[k] = min(inn[k], out[k] - MIN_WALL)

bm = bmesh.new()
Minv = M.inverted()
def P(y, th, r, z_override=None):
    p = Vector((math.sin(th) * r, y, ZC + math.cos(th) * r))
    if z_override is not None: p.z = z_override
    return bm.verts.new(Minv @ p)
# each angular column gets an extra leg-foot point down at ZB for both ends of the arch
grid_o, grid_i = [], []
for y, out, inn in rows:
    ro = [P(y, ANG[0], out[0], ZB)] + [P(y, th, out[k]) for k, th in enumerate(ANG)] + [P(y, ANG[-1], out[-1], ZB)]
    ri = [P(y, ANG[0], inn[0], ZB)] + [P(y, th, inn[k]) for k, th in enumerate(ANG)] + [P(y, ANG[-1], inn[-1], ZB)]
    grid_o.append(ro); grid_i.append(ri)
N = len(grid_o[0])
faces_o, faces_i, faces_cap = [], [], []
for j in range(NY - 1):
    for k in range(N - 1):
        faces_o.append(bm.faces.new((grid_o[j][k], grid_o[j][k + 1], grid_o[j + 1][k + 1], grid_o[j + 1][k])))
        faces_i.append(bm.faces.new((grid_i[j][k], grid_i[j + 1][k], grid_i[j + 1][k + 1], grid_i[j][k + 1])))
for j in (0, NY - 1):                                   # the two rims
    for k in range(N - 1):
        f = bm.faces.new((grid_o[j][k], grid_i[j][k], grid_i[j][k + 1], grid_o[j][k + 1]))
        faces_cap.append(f)
for k in (0, N - 1):                                    # the two leg soles
    for j in range(NY - 1):
        faces_cap.append(bm.faces.new((grid_o[j][k], grid_o[j + 1][k], grid_i[j + 1][k], grid_i[j][k])))
bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
for f in faces_o + faces_i: f.smooth = True
bm.faces.index_update()
cap_idx = {f.index for f in faces_cap}
old = bpy.data.meshes.get("Mesh_Neck_Shell")
if old is not None and old.users == 0: bpy.data.meshes.remove(old)
new = bpy.data.meshes.new("Mesh_Neck_Shell")
bm.to_mesh(new); bm.free()
new.materials.append(src.materials[0])
# hard edges where the smooth skins meet the flat rims and soles
sharp = new.attributes.new("sharp_edge", 'BOOLEAN', 'EDGE')
edge_faces = {}
for p in new.polygons:
    for ek in p.edge_keys: edge_faces.setdefault(ek, []).append(p.index)
for e in new.edges:
    fs = edge_faces.get(e.key, [])
    sharp.data[e.index].value = len({i in cap_idx for i in fs}) > 1
o.data = new
bpy.ops.ed.undo_push(message="Rebuild neck as one shell")

chk = bmesh.new(); chk.from_mesh(new)
print("verts", len(chk.verts), "faces", len(chk.faces), "boundary", sum(e.is_boundary for e in chk.edges), "nonmanifold", sum(not e.is_manifold for e in chk.edges))
ps = [M @ v.co for v in new.vertices]
print("bbox", [round(min(p[i] for p in ps), 2) for i in range(3)], [round(max(p[i] for p in ps), 2) for i in range(3)])
