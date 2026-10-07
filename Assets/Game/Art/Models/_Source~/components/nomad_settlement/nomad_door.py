"""Rectangular nomad door builder, shared by the interior remodel and the door component.
All coordinates are given in a door frame: t across the opening, y depth (outward is -y), z up.
"""
import bpy
import bmesh
import random
from math import pi, sin, cos
from mathutils import Vector, Matrix

OPEN_HW = 1.15       # half width of the opening in the wall
OPEN_H = 3.6         # opening height
WOOD = 'Mat_Deco_Wood_Dark'
IRON = 'Mat_Deco_Metal_DarkIron'
STONE = 'Mat_Deco_Stone_Granite'


def _section(w, h, chamfer=0.2):
    c = min(w, h) * chamfer
    hw, hh = w / 2, h / 2
    return [(hw - c, hh), (-hw + c, hh), (-hw, hh - c), (-hw, -hh + c),
            (-hw + c, -hh), (hw - c, -hh), (hw, -hh + c), (hw, hh - c)]


def _frame(p0, p1, up):
    x = (p1 - p0).normalized()
    u = up
    if abs(x.dot(u)) > 0.98:
        u = Vector((0, 1, 0)) if abs(x.y) < 0.9 else Vector((1, 0, 0))
    z = (u - x * x.dot(u)).normalized()
    y = z.cross(x)
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = (p0 + p1) / 2
    return m


def beam(name, p0, p1, w, h, mat, coll, up=(0, 0, 1), wob=0.008, round_=False, segs=None):
    p0, p1, up = Vector(p0), Vector(p1), Vector(up)
    L = (p1 - p0).length
    n = segs or max(2, int(L / 0.9))
    if round_:
        pts = [(cos(a * pi / 4 + pi / 8) * w / 2, sin(a * pi / 4 + pi / 8) * h / 2) for a in range(8)]
    else:
        pts = _section(w, h)
    bm = bmesh.new()
    rings = []
    for i in range(n + 1):
        x = (i / n - 0.5) * L
        jy, jz = random.uniform(-wob, wob), random.uniform(-wob, wob)
        rings.append([bm.verts.new((x, py + jy + random.uniform(-wob, wob) * .5, pz + jz + random.uniform(-wob, wob) * .5))
                      for py, pz in pts])
    for i in range(n):
        for k in range(8):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % 8], rings[i + 1][(k + 1) % 8], rings[i + 1][k]))
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(bpy.data.materials[mat])
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    ob.matrix_world = _frame(p0, p1, up)
    return ob


def rect_cutter_mesh(me, to_world, hw=OPEN_HW, z0=-0.2, z1=OPEN_H, d0=-1.0, d1=2.0):
    """Fill `me` with a box prism: t in +-hw, z in z0..z1, depth d0..d1 (door frame -> world via to_world)."""
    bm = bmesh.new()
    pts = [(-hw, z0), (hw, z0), (hw, z1), (-hw, z1)]
    A = [bm.verts.new(to_world(t, d0, z)) for t, z in pts]
    B = [bm.verts.new(to_world(t, d1, z)) for t, z in pts]
    for i in range(4):
        bm.faces.new((A[i], A[(i + 1) % 4], B[(i + 1) % 4], B[i]))
    bm.faces.new(A[::-1])
    bm.faces.new(B)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bm.to_mesh(me)
    bm.free()


def build_door(prefix, to_world, coll, parent, leaf_empty, frame_depth=0.46, leaf_y=0.0, frame_y=0.0, inward=1.0, threshold=True):
    """Frame (jambs, lintel, header log, threshold) + closed leaf. Returns created objects.

    to_world(t, y, z): door frame -> world. leaf_y / frame_y: depth of leaf and frame centres (door frame).
    inward: +1 when the frame stands on the room side of the wall plane (so the hinge empty offsets match).
    """
    random.seed(21)
    out = []
    zt = OPEN_H
    side_w = 0.44
    for s, nm in ((-1, 'L'), (1, 'R')):
        o = beam('%s_Jamb_%s' % (prefix, nm), to_world(s * 1.22, frame_y, -0.05), to_world(s * 1.22, frame_y, zt + 0.1),
                 side_w, frame_depth, WOOD, coll, up=to_world(0, 1, 0) - to_world(0, 0, 0), wob=0.01)
        out.append(o)
    # lintel: one heavy beam whose ends overhang the jambs, plus a thinner header log above it
    o = beam('%s_Lintel' % prefix, to_world(-1.62, frame_y, zt + 0.3), to_world(1.62, frame_y, zt + 0.3),
             0.48, frame_depth + 0.04, WOOD, coll, up=(0, 0, 1), wob=0.012, segs=6)
    out.append(o)
    o = beam('%s_Header' % prefix, to_world(-1.45, frame_y, zt + 0.66), to_world(1.45, frame_y, zt + 0.66),
             0.2, 0.3, WOOD, coll, up=(0, 0, 1), wob=0.01, round_=True, segs=5)
    out.append(o)
    if threshold:
        o = beam('%s_Threshold' % prefix, to_world(0, frame_y - 0.5, -0.12), to_world(0, frame_y + 0.5, -0.12), 2.9, 0.34,
                 STONE, coll, up=(0, 0, 1), wob=0.012)
        out.append(o)
    # leaf: six boards hinged at the left jamb
    nb = 6
    bw = (2 * OPEN_HW - 0.1) / nb
    for i in range(nb):
        t = -OPEN_HW + 0.05 + bw * (i + 0.5)
        top = zt - 0.08 + random.uniform(-0.03, 0.02)
        o = beam('%s_Board_%d' % (prefix, i), to_world(t, leaf_y, 0.0), to_world(t, leaf_y, top), bw - 0.012, 0.12, WOOD, coll,
                 up=to_world(0, 1, 0) - to_world(0, 0, 0), wob=0.006)
        out.append(o)
    for nmm, z in (('Low', 0.8), ('Mid', 1.9), ('High', 2.9)):
        o = beam('%s_Strap_%s' % (prefix, nmm), to_world(-OPEN_HW + 0.05, leaf_y + 0.08 * -inward, z),
                 to_world(OPEN_HW - 0.05, leaf_y + 0.08 * -inward, z), 0.05, 0.16, IRON, coll, up=(0, 0, 1), wob=0.004)
        out.append(o)
    o = beam('%s_Handle' % prefix, to_world(0.8, leaf_y + 0.14 * -inward, 1.35), to_world(0.8, leaf_y + 0.14 * -inward, 1.95),
             0.06, 0.06, IRON, coll, up=(0, 0, 1), wob=0.002, round_=True, segs=2)
    out.append(o)
    for o in out:
        o.parent = parent
        o.matrix_parent_inverse = parent.matrix_world.inverted()
    # leaf parts hang from the hinge empty
    for o in out:
        if '_Board_' in o.name or '_Strap_' in o.name or '_Handle' in o.name:
            mw = o.matrix_world.copy()
            o.parent = leaf_empty
            o.matrix_parent_inverse = leaf_empty.matrix_world.inverted()
    return out


# ------------------------------------------------------------------ build the component file
DECO = '/Users/ferdinandfremming/Documents/hackerspace/spillgruppen/SpaceGame/Assets/Game/Art/Models/_Source~/models/buildings/decorations.blend'
OUT = '/Users/ferdinandfremming/Documents/hackerspace/spillgruppen/SpaceGame/Assets/Game/Art/Models/_Source~/components/nomad_settlement/nomad_door.blend'

bpy.ops.wm.read_homefile(use_empty=True)
with bpy.data.libraries.load(DECO, link=False) as (src, dst):
    dst.materials = [WOOD, IRON, STONE]
for m in bpy.data.materials:
    b = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if b:
        m.diffuse_color = b.inputs['Base Color'].default_value

scene = bpy.context.scene
C = bpy.data.collections.new('Coll_NomadDoor')
scene.collection.children.link(C)
R = bpy.data.objects.new('Root_NomadDoor', None)
R.empty_display_type = 'PLAIN_AXES'
C.objects.link(R)
H = bpy.data.objects.new('Root_DoorLeaf', None)       # hinge: left jamb, the leaf swings about +Z here
H.location = (-OPEN_HW, 0, 0)
H.empty_display_type = 'ARROWS'
H.empty_display_size = 0.4
C.objects.link(H)
H.parent = R
bpy.context.view_layer.update()
build_door('Mesh_NomadDoor', lambda t, d, z: Vector((t, d, z)), C, R, H,
           frame_depth=0.7, leaf_y=0.0, frame_y=0.0, inward=-1.0, threshold=False)
# stone sill proud of the wall on the outside (-Y) and flush inside
beam('Mesh_NomadDoor_Sill', Vector((0, -0.55, -0.1)), Vector((0, 0.45, -0.1)), 2.9, 0.34, STONE, C, up=(0, 0, 1), wob=0.012)
sill = bpy.data.objects['Mesh_NomadDoor_Sill']
sill.parent = R
sill.matrix_parent_inverse = R.matrix_world.inverted()

# 3 m player for scale; never exported
ref = bpy.data.collections.new('Ref_Scale')
scene.collection.children.link(ref)
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.5, radius2=0.5, depth=3.0)
bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, 1.5))
me = bpy.data.meshes.new('Ref_Player3m')
bm.to_mesh(me)
bm.free()
po = bpy.data.objects.new('Ref_Player3m', me)
po.location = (2.8, -1.5, 0)
po.display_type = 'WIRE'
ref.objects.link(po)

for o in bpy.data.objects:
    if o.type == 'MESH':
        for p in o.data.polygons:
            p.use_smooth = False
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print('SAVED', OUT, len(bpy.data.objects))
