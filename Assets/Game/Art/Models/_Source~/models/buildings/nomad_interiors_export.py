"""Export nomad_interiors.blend -> visual FBX (shell + props) and a collision FBX. Reads only: the .blend is never saved.

    blender --background --python nomad_interiors_export.py            # scale 1.25, the shipped one
    INTERIOR_SCALE=1.0 blender --background --python nomad_interiors_export.py

Every visible mesh instance is baked in WORLD space, uniformly scaled by INTERIOR_SCALE, so the FBX carries the size
(no scale on the Unity side) and the NavMesh, anchors and lights in NomadHome.unity must be scaled by the same factor
when it changes. Seats (`Root_Seat_*`) are left out of the collision mesh on purpose: a sitter's body is inside the
seat, and a solid seat would hide it from every line-of-sight check and leave a NavMesh island on its top.
"""
import bpy, bmesh, json, re, sys, os
from math import radians, cos, sin
from mathutils import Vector, Matrix

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), *['..'] * 7)) + '/'
SRC = ROOT + 'Assets/Game/Art/Models/_Source~/models/buildings/nomad_interiors.blend'
OUT = ROOT + 'Assets/Game/Art/Models/Environment/Structures/NomadSettlement/Interior/'
sys.path.insert(0, ROOT + 'Assets/Game/Art/Models/_Source~')
import _exportlib

SHELL_COLLS = {'Interior_Wall', 'Interior_Roof', 'Interior_Loft', 'Interior_Stair', 'Interior_Openings'}
SKIP_COLLS = {'Cutters', 'Ref_Scale'}
SOFT = ('Fabric', 'Plant_', 'Fibre', 'Paper', 'Food', 'Flower', 'Water', 'Liquid', 'Fur', 'Hide', 'Leather', 'Membrane')
SCALE = float(os.environ.get('INTERIOR_SCALE', '1.25'))
STAIR_A0, STAIR_A1, STAIR_N = 229.0, 160.0, 14
STAIR_R0, STAIR_R1, LOFT_Z = 4.55 * SCALE, 6.15 * SCALE, 4.1 * SCALE

bpy.ops.wm.open_mainfile(filepath=SRC)
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()

# ---------------------------------------------------------------- materials
BASE = re.compile(r'\.\d{3}$')
canon = {}
emissive = []


def pb(m):
    return next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if m and m.use_nodes else None


def sig(m):
    b = pb(m)
    if not b:
        return None
    g = lambda k: tuple(round(x, 3) for x in b.inputs[k].default_value) if hasattr(b.inputs[k].default_value, '__len__') else round(b.inputs[k].default_value, 3)
    return (g('Base Color'), g('Roughness'), g('Metallic'), g('Alpha'))


def canonical(m):
    if m is None:
        return None
    if m.name in canon:
        return canon[m.name]
    base = BASE.sub('', m.name)
    res = m
    if base != m.name and base in bpy.data.materials and sig(bpy.data.materials[base]) == sig(m):
        res = bpy.data.materials[base]
    canon[m.name] = res
    return res


def kill_emission(m):
    b = pb(m)
    if b and 'Emission Strength' in b.inputs and b.inputs['Emission Strength'].default_value > 0:
        col = b.inputs['Emission Color'].default_value
        if col[0] + col[1] + col[2] > 0.01:
            emissive.append(m.name)
        b.inputs['Emission Strength'].default_value = 0.0


# ---------------------------------------------------------------- gather visible mesh instances
items = []
for inst in dg.object_instances:
    ob = inst.object
    if ob.type != 'MESH':
        continue
    orig = ob.original
    colls = {c.name for c in orig.users_collection}
    if colls & SKIP_COLLS or orig.name == 'Ref_Player3m':
        continue
    items.append((orig, ob, Matrix.Scale(SCALE, 4) @ inst.matrix_world, colls))
print('visible mesh instances', len(items))


def top_group(o):
    """Name of the outermost non-root ancestor (the prop a part belongs to)."""
    while o.parent is not None and o.parent.name != 'Root_NomadInterior':
        o = o.parent
    return o.name


def world_bm(ob, mat):
    me = ob.to_mesh()
    bm = bmesh.new()
    bm.from_mesh(me)
    ob.to_mesh_clear()
    bm.transform(mat)
    if mat.to_3x3().determinant() < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    big = [f for f in bm.faces if len(f.verts) > 4]
    if big:
        bmesh.ops.triangulate(bm, faces=big, quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.verts.index_update()
    return bm


class Joiner:
    def __init__(self):
        self.v, self.f, self.m, self.s, self.mats, self.idx = [], [], [], [], [], {}

    def slot(self, m):
        if m not in self.idx:
            self.idx[m] = len(self.mats)
            self.mats.append(m)
        return self.idx[m]

    def add(self, bm, slots):
        off = len(self.v)
        self.v.extend(v.co[:] for v in bm.verts)
        for f in bm.faces:
            self.f.append([off + v.index for v in f.verts])
            mi = f.material_index
            m = canonical(slots[mi]) if mi < len(slots) else None
            self.m.append(self.slot(m) if m else 0)
            self.s.append(f.smooth)

    def build(self, name):
        bpy.context.view_layer.update()
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.v, [], self.f)
        me.polygons.foreach_set('material_index', self.m)
        me.polygons.foreach_set('use_smooth', self.s)
        for m in self.mats:
            if m:
                kill_emission(m)
                me.materials.append(m)
        me.update()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


shell, props = Joiner(), Joiner()
groups = {}
for orig, ob, mat, colls in items:
    bm = world_bm(ob, mat)
    slots = [s.material for s in orig.material_slots]
    (shell if colls & SHELL_COLLS else props).add(bm, slots)
    if not (colls & SHELL_COLLS) and 'Interior_Hanging' not in colls:
        groups.setdefault(top_group(orig), []).append((bm, slots))
        continue
    bm.free()

vis_shell = shell.build('NomadHome_Shell')
vis_props = props.build('NomadHome_Props')
print('emission zeroed on', sorted(set(emissive)))
for o in (vis_shell, vis_props):
    print(o.name, 'verts', len(o.data.vertices), 'tris', sum(len(p.vertices) - 2 for p in o.data.polygons), 'mats', len(o.data.materials))

# ---------------------------------------------------------------- visual FBX
for o in bpy.data.objects:
    o.select_set(False)
vis_shell.select_set(True)
vis_props.select_set(True)
_exportlib._write_fbx(OUT + 'nomad_interior.fbx', {'MESH'}, use_selection=True)
vis_shell.select_set(False)
vis_props.select_set(False)

# ---------------------------------------------------------------- collision
col = Joiner()
KEEP_SHELL = ('Mesh_Wall', 'Mesh_Floor', 'Mesh_LoftDeck', 'Mesh_LoftPost_', 'Mesh_LoftRail', 'Mesh_Door_Jamb', 'Mesh_Door_Lintel',
              'Mesh_Door_Board', 'Mesh_DoorThreshold')
for orig, ob, mat, colls in items:
    if colls & SHELL_COLLS and orig.name.startswith(KEEP_SHELL):
        bm = world_bm(ob, mat)
        col.add(bm, [])
        bm.free()


def sweep(r0, r1, zfn, thick_below, n=36):
    """Closed solid swept along the stair arc. zfn(a) gives the top height."""
    bm = bmesh.new()
    rows = []
    for k in range(n + 1):
        a = radians(STAIR_A0 + (STAIR_A1 - STAIR_A0) * k / n)
        z = zfn(STAIR_A0 + (STAIR_A1 - STAIR_A0) * k / n)
        rows.append([bm.verts.new((cos(a) * r0, sin(a) * r0, z)), bm.verts.new((cos(a) * r1, sin(a) * r1, z)),
                     bm.verts.new((cos(a) * r1, sin(a) * r1, z - thick_below)), bm.verts.new((cos(a) * r0, sin(a) * r0, z - thick_below))])
    for k in range(n):
        for q in range(4):
            bm.faces.new((rows[k][q], rows[k][(q + 1) % 4], rows[k + 1][(q + 1) % 4], rows[k + 1][q]))
    bm.faces.new(rows[0][::-1])
    bm.faces.new(rows[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm


da = (STAIR_A0 - STAIR_A1) / STAIR_N
rise = (LOFT_Z - 0.02) / STAIR_N
ramp_z = lambda a: min(LOFT_Z, rise * ((STAIR_A0 - a) / da) + 0.5 * rise)
bm = sweep(STAIR_R0, STAIR_R1, ramp_z, 4.4 * SCALE)
col.add(bm, [])
bm.free()
bm = sweep(STAIR_R0 - 0.02 * SCALE, STAIR_R0 + 0.1 * SCALE, lambda a: ramp_z(a) + 1.15 * SCALE, 1.2 * SCALE)
col.add(bm, [])
bm.free()

hulls = []
for name, parts in sorted(groups.items()):
    pts = []
    for bm, slots in parts:
        for f in bm.faces:
            mi = f.material_index
            m = slots[mi] if mi < len(slots) else None
            if m is not None and any(t in BASE.sub('', m.name) for t in SOFT):
                continue
            pts.extend(v.co.copy() for v in f.verts)
    if len(pts) < 4 or name.startswith('Root_Seat_'):
        continue   # a seat is not solid: sitters stand inside it, and its top would be a NavMesh island
    zmin = min(p.z for p in pts)
    zmax = max(p.z for p in pts)
    xs, ys = [p.x for p in pts], [p.y for p in pts]
    span = max(max(xs) - min(xs), max(ys) - min(ys))
    on_loft = abs(zmin - LOFT_Z) < 0.3
    if zmax - zmin < 0.25 or (zmin > 3.2 and not on_loft) or span < 0.3 or 'Cable' in name:
        continue
    hb = bmesh.new()
    for p in pts:
        hb.verts.new(p)
    res = bmesh.ops.convex_hull(hb, input=hb.verts[:], use_existing_faces=False)
    keep = {g for g in res['geom'] if isinstance(g, bmesh.types.BMVert)}
    bmesh.ops.delete(hb, geom=[v for v in hb.verts if v not in keep], context='VERTS')
    hb.faces.ensure_lookup_table()
    if len(hb.faces) < 4:
        hb.free()
        continue
    bmesh.ops.recalc_face_normals(hb, faces=hb.faces[:])
    col.add(hb, [])
    hulls.append((name, round(zmax - zmin, 2), round(span, 2)))
    hb.free()
print('collision hulls', len(hulls), hulls[:80])

cob = bpy.data.objects.new('NomadHome_Collision', None)
me = bpy.data.meshes.new('NomadHome_Collision')
me.from_pydata(col.v, [], col.f)
me.update()
cob = bpy.data.objects.new('NomadHome_Collision', me)
bpy.context.scene.collection.objects.link(cob)
cob.select_set(True)
print('collision tris', len(me.polygons))
_exportlib._write_fbx(OUT + 'nomad_interior_collision.fbx', {'MESH'}, use_selection=True)

seats = {}
for orig, ob, mat, colls in items:
    root = orig
    while root.parent is not None:
        root = root.parent
    if not root.name.startswith('Root_Seat_'):
        continue
    me = ob.to_mesh()
    pts = [mat @ v.co for v in me.vertices]
    ob.to_mesh_clear()
    d = seats.setdefault(root.name, {'lo': [1e9] * 3, 'hi': [-1e9] * 3})
    for p in pts:
        for i in range(3):
            d['lo'][i] = min(d['lo'][i], p[i])
            d['hi'][i] = max(d['hi'][i], p[i])
print('seats (blender space, scaled)', json.dumps(seats))
print('DONE')
