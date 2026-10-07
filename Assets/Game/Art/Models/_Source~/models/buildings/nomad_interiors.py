"""Generator for nomad_interiors.blend (historical record: the .blend is the source of truth).

A round clay dwelling, seen from inside: rammed-earth wall, dark timber roof and loft,
a sunken hearth, an astronomer-alchemist's clutter from decorations.blend.
Z up, -Y is the door side, 1 unit = 1 m, built for the 3 m player.
"""
import bpy
import bmesh
import math
import random
from math import pi, sin, cos, radians
from mathutils import Vector, Matrix, noise

BASE = '/Users/ferdinandfremming/Documents/hackerspace/spillgruppen/SpaceGame/Assets/Game/Art/Models/_Source~/models/buildings/'
DECO = BASE + 'decorations.blend'
NOMAD = BASE + 'nomad_settlement.blend'
OUT = BASE + 'nomad_interiors.blend'

random.seed(11)
R_IN = 6.0            # inner wall radius
WALL_TOP = 7.0
LOFT_Z = 4.1          # loft deck top
LOFT_A0, LOFT_A1 = 20.0, 160.0
DOOR_A = 270.0
STAIR_A0 = 229.0      # stair foot angle; climbs towards decreasing angle to LOFT_A1


def nz(x, y, z, f=1.0):
    return noise.noise(Vector((x * f, y * f, z * f)))


def polar(theta_deg, r, t=0.0, z=0.0):
    th = radians(theta_deg)
    return Vector((cos(th) * r - sin(th) * t, sin(th) * r + cos(th) * t, z))


def zr(r):
    """Underside height of the conical roof at radius r."""
    return WALL_TOP + (6.95 - r) * 0.40


# ------------------------------------------------------------------ props
PROPS = [
    # name, kind, args
    # hearth
    ('CookingHearth_Pot', 'at', dict(x=0, y=0, z=-0.30, rot=0, s=0.9)),
    ('Bench_Carved', 'wallish', dict(theta=212, r=3.7, rot_extra=0)),
    ('Bench_Carved', 'wallish', dict(theta=305, r=3.55, s=0.8)),
    ('Rug_Rect', 'at', dict(x=0, y=-4.4, z=0.012, rot=90, s=0.8)),
    ('Cushions_Pile', 'wallish', dict(theta=158, r=2.9, rot_extra=0)),
    ('Cushions_Pile', 'wallish', dict(theta=24, r=2.9, rot_extra=0)),
    # kitchen (right of the door)
    ('BreadOven', 'wall', dict(theta=333, s=0.8)),
    ('Cupboard_Dishes', 'wall', dict(theta=302)),
    ('WashStand', 'wall', dict(theta=288)),
    ('KegRack', 'wall', dict(theta=357, s=0.9)),
    # workshop (left of the door)
    ('ShelvingRack_Tall', 'wall', dict(theta=246)),
    ('Basket_Round', 'wall', dict(theta=255, inset=0.3)),
    # under the loft: study, alchemy, apothecary
    ('ApothecaryShelf', 'wall', dict(theta=31)),
    ('AlchemyTable', 'wall', dict(theta=57)),
    ('Chest_Storage', 'wall', dict(theta=80)),
    ('PotteryWheel', 'wall', dict(theta=97)),
    ('UrnNiche', 'wall', dict(theta=122)),
    ('Workbench', 'wall', dict(theta=149, s=0.9)),
    ('Table_Side', 'wallish', dict(theta=45, r=3.55, rot_extra=0)),
    # loft
    ('Bed_Frame', 'loft', dict(theta=42, tangent=True)),
    ('Wardrobe', 'loft', dict(theta=76)),
    ('Chest_Storage', 'loft', dict(theta=94)),
    ('Lectern_Tome', 'loft', dict(theta=108, inset=0.1)),
    ('StarGazer', 'loft', dict(theta=124, inset=0.1)),
    ('Shelf_Scrolls', 'loft', dict(theta=146)),
    ('Rug_Rect', 'loftrug', dict(theta=45)),
]

NEED_MATS = [
    'Mat_Deco_Wood_Dark', 'Mat_Deco_Wood_Weathered', 'Mat_Deco_Fibre_StrawDark', 'Mat_Deco_Fibre_Rope',
    'Mat_Deco_Metal_DarkIron', 'Mat_Deco_Metal_Brass', 'Mat_Deco_Stone_Granite', 'Mat_Deco_Stone_Slate',
    'Mat_Deco_Plant_Sage', 'Mat_Deco_Plant_Olive', 'Mat_Deco_Plant_Rust', 'Mat_Deco_Fabric_Indigo',
    'Mat_Deco_Fabric_Rust', 'Mat_Deco_Fabric_Saffron', 'Mat_Deco_Fabric_Cream', 'Mat_Deco_Plant_Bark',
]
NOMAD_MATS = ['Mat_Nomad_Clay_Sand', 'Mat_Nomad_Clay_Ochre', 'Mat_Nomad_Clay_Terracotta', 'Mat_Nomad_Clay_Bone']

# ------------------------------------------------------------------ setup
bpy.ops.wm.read_homefile(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0

wanted = sorted({p[0] for p in PROPS})
with bpy.data.libraries.load(DECO, link=False) as (src, dst):
    dst.collections = ['Coll_Deco_' + n for n in wanted]
    dst.materials = [m for m in NEED_MATS if m in src.materials]
with bpy.data.libraries.load(NOMAD, link=False) as (src, dst):
    dst.materials = [m for m in NOMAD_MATS if m in src.materials]


def M(name):
    return bpy.data.materials[name]


def principled(mat):
    return next((n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if mat.use_nodes else None


for mat in bpy.data.materials:
    b = principled(mat)
    if not b:
        continue
    mat.diffuse_color = b.inputs['Base Color'].default_value
    # no glowing anything in this interior
    for key in ('Emission Strength',):
        if key in b.inputs:
            b.inputs[key].default_value = 0.0


def new_coll(name, parent=None):
    c = bpy.data.collections.new(name)
    (parent or scene.collection).children.link(c)
    return c


C_ROOT = new_coll('Coll_NomadInterior')
C_SHELL = new_coll('Interior_Shell', C_ROOT)
C_WALL = new_coll('Interior_Wall', C_SHELL)
C_ROOF = new_coll('Interior_Roof', C_SHELL)
C_LOFT = new_coll('Interior_Loft', C_SHELL)
C_STAIR = new_coll('Interior_Stair', C_SHELL)
C_OPEN = new_coll('Interior_Openings', C_SHELL)
C_HANG = new_coll('Interior_Hanging', C_ROOT)
C_PROPS = new_coll('Interior_Props', C_ROOT)
C_CUT = new_coll('Cutters')
C_REF = new_coll('Ref_Scale')


def empty(name, loc, coll, parent=None):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'PLAIN_AXES'
    e.empty_display_size = 0.5
    e.location = loc
    coll.objects.link(e)
    if parent:
        e.parent = parent
    return e


ROOT = empty('Root_NomadInterior', (0, 0, 0), C_ROOT)


def finish(name, bm, mats, coll, matrix=None, parent=None):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    big = [f for f in bm.faces if len(f.verts) > 4]
    if big:
        bmesh.ops.triangulate(bm, faces=big)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(M(m))
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    if matrix is not None:
        ob.matrix_world = matrix
    if parent is not None:
        ob.parent = parent
        ob.matrix_parent_inverse = parent.matrix_basis.inverted()
    else:
        ob.parent = ROOT
    return ob


def frame_matrix(p0, p1, up=None):
    x = (p1 - p0).normalized()
    u = up if up is not None else Vector((0, 0, 1))
    if abs(x.dot(u)) > 0.98:
        u = Vector((0, 1, 0)) if abs(x.y) < 0.9 else Vector((1, 0, 0))
    z = (u - x * x.dot(u)).normalized()
    y = z.cross(x)
    m = Matrix((x, y, z)).transposed().to_4x4()
    m.translation = (p0 + p1) / 2
    return m


def section(w, h, round_, chamfer):
    if round_:
        return [(cos(a) * w / 2, sin(a) * h / 2) for a in [i * pi / 4 + pi / 8 for i in range(8)]]
    c = min(w, h) * chamfer
    hw, hh = w / 2, h / 2
    return [(hw - c, hh), (-hw + c, hh), (-hw, hh - c), (-hw, -hh + c),
            (-hw + c, -hh), (hw - c, -hh), (hw, -hh + c), (hw, hh - c)]


def beam(name, p0, p1, w, h, mat, coll, round_=False, wob=0.008, sag=0.0, taper=0.0,
         chamfer=0.22, up=None, parent=None, segs=None):
    p0 = Vector(p0)
    p1 = Vector(p1)
    L = (p1 - p0).length
    n = segs or max(2, int(L / 0.9))
    pts = section(w, h, round_, chamfer)
    bm = bmesh.new()
    rings = []
    for i in range(n + 1):
        t = i / n
        x = (t - 0.5) * L
        s = 1.0 - taper * t
        jy = random.uniform(-wob, wob)
        jz = random.uniform(-wob, wob)
        ring = []
        for py, pz in pts:
            ring.append(bm.verts.new((
                x,
                py * s + jy + random.uniform(-wob, wob) * 0.5,
                pz * s + jz - sag * 4 * t * (1 - t) + random.uniform(-wob, wob) * 0.5)))
        rings.append(ring)
    for i in range(n):
        for k in range(8):
            bm.faces.new((rings[i][k], rings[i][(k + 1) % 8], rings[i + 1][(k + 1) % 8], rings[i + 1][k]))
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])
    return finish(name, bm, [mat], coll, frame_matrix(p0, p1, up), parent)


def torus(name, center, axis, R, r, mat, coll, nu=24, nv=6, parent=None):
    axis = Vector(axis).normalized()
    bm = bmesh.new()
    rings = []
    for i in range(nu):
        a = 2 * pi * i / nu
        ring = []
        for j in range(nv):
            b = 2 * pi * j / nv
            rr = R + r * cos(b)
            ring.append(bm.verts.new((0.0 + random.uniform(-0.004, 0.004), rr * cos(a), rr * sin(a) + 0 * r * sin(b))))
            ring[-1].co.x = r * sin(b)
        rings.append(ring)
    for i in range(nu):
        for j in range(nv):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % nv], rings[(i + 1) % nu][(j + 1) % nv], rings[(i + 1) % nu][j]))
    m = frame_matrix(Vector(center) - axis, Vector(center) + axis)
    m.translation = Vector(center)
    return finish(name, bm, [mat], coll, m, parent)


def sector(name, r0, r1, a0, a1, z0, z1, coll, mat, nseg=12, nr=1, jit=0.0, gap=0.0, parent=None):
    """Annular sector prism(s); nr radial strips (planks) separated by gap."""
    bm = bmesh.new()
    for s in range(nr):
        ra = r0 + (r1 - r0) * s / nr + gap / 2
        rb = r0 + (r1 - r0) * (s + 1) / nr - gap / 2
        dz = random.uniform(-jit, jit)
        rails = []
        for r, z in ((ra, z0), (ra, z1 + dz), (rb, z1 + dz), (rb, z0)):
            col = []
            for k in range(nseg + 1):
                a = radians(a0 + (a1 - a0) * k / nseg)
                zz = z + (random.uniform(-jit, jit) * 0.3 if z == z1 + dz else 0)
                col.append(bm.verts.new((cos(a) * r, sin(a) * r, zz)))
            rails.append(col)
        for q in range(4):
            A, B = rails[q], rails[(q + 1) % 4]
            for k in range(nseg):
                bm.faces.new((A[k], A[k + 1], B[k + 1], B[k]))
        bm.faces.new([rails[q][0] for q in range(4)])
        bm.faces.new([rails[q][nseg] for q in range(4)][::-1])
    return finish(name, bm, [mat], coll, None, parent)


def prism_cutter(name, theta, poly, r0, r1):
    bm = bmesh.new()
    A = [bm.verts.new(polar(theta, r0, t, z)) for t, z in poly]
    B = [bm.verts.new(polar(theta, r1, t, z)) for t, z in poly]
    n = len(poly)
    for i in range(n):
        bm.faces.new((A[i], A[(i + 1) % n], B[(i + 1) % n], B[i]))
    bm.faces.new(A[::-1])
    bm.faces.new(B)
    ob = finish(name, bm, [], C_CUT)
    ob.parent = None
    ob.display_type = 'WIRE'
    ob.hide_render = True
    return ob


def arch_poly(hw, z0, zspring, nseg=12, rise=None):
    pts = [(-hw, z0), (hw, z0), (hw, zspring)]
    for i in range(1, nseg):
        a = pi * i / nseg
        pts.append((cos(a) * hw, zspring + sin(a) * (rise or hw)))
    pts.append((-hw, zspring))
    return pts


def circle_poly(r, z, n=16):
    return [(cos(2 * pi * i / n) * r, z + sin(2 * pi * i / n) * r) for i in range(n)]


# ------------------------------------------------------------------ floor
def build_floor():
    prof = [(0, -0.30), (1.97, -0.30), (2.00, -0.16), (2.38, -0.15), (2.42, -0.01),
            (3.2, 0.0), (4.0, 0.0), (5.0, 0.0), (6.0, 0.0), (6.5, 0.0), (6.5, -0.5), (0, -0.5)]
    segmat = [1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0]   # 0 clay, 1 slate (hearth pit)
    segmat[4] = 0
    NT = 72
    bm = bmesh.new()
    rows = []
    for i, (r, z) in enumerate(prof):
        if r == 0:
            rows.append([bm.verts.new((0, 0, z))] * NT)
            continue
        row = []
        for k in range(NT):
            a = 2 * pi * k / NT
            zz = z + (0.007 * nz(cos(a) * r * 0.5, sin(a) * r * 0.5, 3) if 2.4 < r < 6.4 and z == 0.0 else 0)
            rr = r + (0.01 * nz(a * 3, r, 1) if r > 2 else 0)
            row.append(bm.verts.new((cos(a) * rr, sin(a) * rr, zz)))
        rows.append(row)
    for i in range(len(prof) - 1):
        for k in range(NT):
            k2 = (k + 1) % NT
            vs = [rows[i][k], rows[i][k2], rows[i + 1][k2], rows[i + 1][k]]
            uniq = list(dict.fromkeys(vs))
            if len(uniq) < 3:
                continue
            f = bm.faces.new(uniq)
            f.material_index = segmat[i]
    return finish('Mesh_Floor', bm, ['Mat_Nomad_Clay_Ochre', 'Mat_Deco_Stone_Slate'], C_WALL)


# ------------------------------------------------------------------ wall
def build_wall():
    NT = 120
    zlist = [-0.6, 0.0, 0.5, 1.0, 1.4, 2.0, 2.6, 3.2, 3.8, 4.4, 5.0, 5.6, 6.2, 6.6, None]

    def rin(a, z):
        return R_IN - 0.05 * max(z, 0) / 7 + 0.05 * nz(cos(a) * 2, sin(a) * 2, z * 0.3) + 0.018 * nz(cos(a) * 7, sin(a) * 7, z * 1.2)

    def rout(a, z):
        return 6.95 + 0.30 * (1 - max(z, 0) / 7) + 0.07 * nz(cos(a) * 2.3, sin(a) * 2.3, z * 0.3 + 5)

    def ztop(a):
        return WALL_TOP + 0.10 * nz(cos(a) * 3, sin(a) * 3, 9)

    bm = bmesh.new()
    inner, outer = [], []
    for zj in zlist:
        ri, ro = [], []
        for k in range(NT):
            a = 2 * pi * k / NT
            z = ztop(a) if zj is None else zj
            ri.append(bm.verts.new((cos(a) * rin(a, z), sin(a) * rin(a, z), z)))
            ro.append(bm.verts.new((cos(a) * rout(a, z), sin(a) * rout(a, z), z)))
        inner.append(ri)
        outer.append(ro)
    for j in range(len(zlist) - 1):
        mi = 1 if zlist[j] is not None and zlist[j] < 1.4 else 0
        for k in range(NT):
            k2 = (k + 1) % NT
            f = bm.faces.new((inner[j][k], inner[j][k2], inner[j + 1][k2], inner[j + 1][k]))
            f.material_index = mi
            f = bm.faces.new((outer[j][k], outer[j + 1][k], outer[j + 1][k2], outer[j][k2]))
            f.material_index = mi
    top = len(zlist) - 1
    for k in range(NT):
        k2 = (k + 1) % NT
        bm.faces.new((inner[top][k], outer[top][k], outer[top][k2], inner[top][k2]))
        bm.faces.new((inner[0][k], inner[0][k2], outer[0][k2], outer[0][k]))
    return finish('Mesh_Wall', bm, ['Mat_Nomad_Clay_Sand', 'Mat_Nomad_Clay_Ochre'], C_WALL)


# ------------------------------------------------------------------ roof
def build_roof():
    rs = [7.8, 7.2, 6.45, 5.6, 4.7, 3.8, 3.0, 2.3, 1.7, 1.3]
    NT = 60
    bm = bmesh.new()
    under, upper = [], []
    for r in rs:
        u, t = [], []
        for k in range(NT):
            a = 2 * pi * k / NT
            d = 0.02 * nz(cos(a) * r * 0.3, sin(a) * r * 0.3, 2)
            u.append(bm.verts.new((cos(a) * r, sin(a) * r, zr(r) + d)))
            t.append(bm.verts.new((cos(a) * r, sin(a) * r, zr(r) + d + 0.14)))
        under.append(u)
        upper.append(t)
    for i in range(len(rs) - 1):
        for k in range(NT):
            k2 = (k + 1) % NT
            f = bm.faces.new((under[i][k], under[i + 1][k], under[i + 1][k2], under[i][k2]))
            f.material_index = 0
            f = bm.faces.new((upper[i][k], upper[i][k2], upper[i + 1][k2], upper[i + 1][k]))
            f.material_index = 1
    for k in range(NT):
        k2 = (k + 1) % NT
        bm.faces.new((under[0][k], under[0][k2], upper[0][k2], upper[0][k]))
        bm.faces.new((under[-1][k], upper[-1][k], upper[-1][k2], under[-1][k2]))
    finish('Mesh_RoofDeck', bm, ['Mat_Deco_Fibre_StrawDark', 'Mat_Deco_Wood_Weathered'], C_ROOF)

    NR = 20
    WOOD = 'Mat_Deco_Wood_Dark'
    for j in range(NR):
        a = 360.0 * j / NR + 3.0 * nz(j, 0, 0)
        p0 = polar(a, 7.4, 0, zr(7.4) - 0.12)
        p1 = polar(a, 1.3, 0, zr(1.3) - 0.10)
        beam('Mesh_Rafter_%02d' % j, p0, p1, 0.24, 0.30, WOOD, C_ROOF, wob=0.01, chamfer=0.2, segs=6)
    for ring_r, w, nm in ((4.8, 0.2, 'PurlinOuter'), (3.0, 0.18, 'PurlinInner')):
        for j in range(NR):
            a0 = 360.0 * j / NR - 1
            a1 = 360.0 * (j + 1) / NR + 1
            p0 = polar(a0, ring_r, 0, zr(ring_r) - 0.32)
            p1 = polar(a1, ring_r, 0, zr(ring_r) - 0.32)
            beam('Mesh_%s_%02d' % (nm, j), p0, p1, w, 0.2, WOOD, C_ROOF, round_=True, wob=0.01)
    for j in range(NR):
        a0 = 360.0 * j / NR
        a1 = 360.0 * (j + 1) / NR
        beam('Mesh_CompressionRing_%02d' % j, polar(a0, 1.32, 0, zr(1.3) - 0.16), polar(a1, 1.32, 0, zr(1.3) - 0.16),
             0.30, 0.34, WOOD, C_ROOF, wob=0.01)
        beam('Mesh_WallPlate_%02d' % j, polar(a0 - 2, 6.53, 0, WALL_TOP + 0.02), polar(a1 + 2, 6.53, 0, WALL_TOP + 0.02),
             0.5, 0.32, WOOD, C_ROOF, wob=0.012, chamfer=0.2)
    # tie beams across the room
    for n, y in (('A', -3.7), ('B', 0.0), ('C', 3.7)):
        hx = math.sqrt(6.5 ** 2 - y * y)
        beam('Mesh_TieBeam_%s' % n, (-hx, y, 6.35), (hx, y, 6.35), 0.42, 0.50, WOOD, C_ROOF, wob=0.014, sag=0.04, segs=10)


# ------------------------------------------------------------------ loft & stair
def build_loft():
    WOOD = 'Mat_Deco_Wood_Dark'
    sector('Mesh_LoftDeck', 3.45, 6.2, LOFT_A0, LOFT_A1 + 1.0, LOFT_Z - 0.26, LOFT_Z, C_LOFT,
           'Mat_Deco_Wood_Weathered', nseg=28, nr=10, jit=0.008, gap=0.012)
    for a in range(int(LOFT_A0) + 5, int(LOFT_A1) + 1, 10):
        beam('Mesh_LoftJoist_%03d' % a, polar(a, 3.3, 0, LOFT_Z - 0.46), polar(a, 6.3, 0, LOFT_Z - 0.46),
             0.26, 0.36, WOOD, C_LOFT, round_=True, wob=0.01)
    a = LOFT_A0
    while a < LOFT_A1:
        beam('Mesh_LoftFascia_%03d' % a, polar(a - 1, 3.5, 0, LOFT_Z - 0.46), polar(min(a + 10, LOFT_A1) + 1, 3.5, 0, LOFT_Z - 0.46),
             0.30, 0.42, WOOD, C_LOFT, wob=0.01)
        a += 10
    for n, a in enumerate((22, 58, 94, 130, 158)):
        beam('Mesh_LoftPost_%d' % n, polar(a, 3.45, 0, 0.18), polar(a, 3.45, 0, LOFT_Z - 0.2), 0.34, 0.34,
             WOOD, C_LOFT, round_=True, wob=0.012)
        beam('Mesh_LoftPostBase_%d' % n, polar(a, 3.45, 0, -0.05), polar(a, 3.45, 0, 0.28), 0.58, 0.58,
             'Mat_Deco_Stone_Granite', C_LOFT, round_=True, wob=0.02, taper=0.12)
    RAIL = LOFT_Z + 1.35
    a = 24
    while a <= 150:
        beam('Mesh_LoftRailPost_%03d' % a, polar(a, 3.62, 0, LOFT_Z - 0.1), polar(a, 3.62, 0, RAIL), 0.13, 0.13,
             WOOD, C_LOFT, round_=True, wob=0.006)
        a += 9
    a = 22
    while a < 150:
        for lvl, z in (('Top', RAIL), ('Mid', LOFT_Z + 0.65)):
            beam('Mesh_LoftRail%s_%03d' % (lvl, a), polar(a, 3.62, 0, z), polar(min(a + 9, 150), 3.62, 0, z),
                 0.14, 0.14, WOOD, C_LOFT, round_=True, wob=0.006)
        a += 9


def build_stair():
    N = 14
    rise = (LOFT_Z - 0.02) / N
    span = STAIR_A0 - LOFT_A1
    da = span / N
    for i in range(N):
        a_hi = STAIR_A0 - i * da + (1.2 if i > 0 else 0.0)
        a_lo = STAIR_A0 - (i + 1) * da
        sector('Mesh_StairStep_%02d' % i, 4.55, 6.15, a_lo, a_hi, -0.1, rise * (i + 1), C_STAIR,
               'Mat_Nomad_Clay_Ochre' if i % 2 else 'Mat_Nomad_Clay_Sand', nseg=3, jit=0.006)
    WOOD = 'Mat_Deco_Wood_Dark'
    pts = []
    for i in range(N):
        a = STAIR_A0 - (i + 0.5) * da
        pts.append((a, rise * (i + 1)))
    for i in range(0, N, 2):
        a, z = pts[i]
        beam('Mesh_StairRailPost_%02d' % i, polar(a, 4.7, 0, z - 0.05), polar(a, 4.7, 0, z + 1.15), 0.14, 0.14,
             WOOD, C_STAIR, round_=True, wob=0.006)
    for i in range(N - 1):
        a0, z0 = pts[i]
        a1, z1 = pts[i + 1]
        beam('Mesh_StairHandrail_%02d' % i, polar(a0, 4.7, 0, z0 + 1.15), polar(a1, 4.7, 0, z1 + 1.15), 0.15, 0.15,
             WOOD, C_STAIR, round_=True, wob=0.006)


# ------------------------------------------------------------------ door, windows
def build_door():
    WOOD = 'Mat_Deco_Wood_Dark'
    th = DOOR_A
    hw, zs = 1.15, 2.9
    rad = polar(th, 1, 0, 0)
    prism_cutter('Cut_Door', th, arch_poly(hw, -0.2, zs, 14), 5.0, 8.0)
    beam('Mesh_DoorJamb_L', polar(th, 5.98, -1.22, -0.05), polar(th, 5.98, -1.22, zs), 0.42, 0.46, WOOD, C_OPEN,
         wob=0.01, up=rad)
    beam('Mesh_DoorJamb_R', polar(th, 5.98, 1.22, -0.05), polar(th, 5.98, 1.22, zs), 0.42, 0.46, WOOD, C_OPEN,
         wob=0.01, up=rad)
    N = 8
    for i in range(N):
        a0 = pi * i / N
        a1 = pi * (i + 1) / N
        p0 = polar(th, 5.98, cos(a0) * 1.22, zs + sin(a0) * 1.22)
        p1 = polar(th, 5.98, cos(a1) * 1.22, zs + sin(a1) * 1.22)
        beam('Mesh_DoorLintel_%02d' % i, p0, p1, 0.42, 0.46, WOOD, C_OPEN, wob=0.008, up=rad)
    beam('Mesh_DoorThreshold', polar(th, 5.7, 0, -0.12), polar(th, 7.4, 0, -0.12), 2.9, 0.34,
         'Mat_Deco_Stone_Granite', C_OPEN, wob=0.012, up=Vector((0, 0, 1)))
    hinge = polar(th, 6.38, -1.1, 0)
    leaf = empty('Root_DoorLeaf', hinge, C_OPEN, ROOT)
    nb = 6
    for i in range(nb):
        t = -1.1 + 0.35 * (i + 0.5)
        top = zs + math.sqrt(max(hw * hw - min(t, hw - 0.02) ** 2, 0.01)) - 0.04
        beam('Mesh_DoorBoard_%d' % i, polar(th, 6.38, t, 0.0), polar(th, 6.38, t, top), 0.335, 0.12, WOOD, C_OPEN,
             wob=0.006, up=rad, parent=leaf)
    for n, z in (('Low', 0.9), ('High', 2.3)):
        beam('Mesh_DoorStrap_%s' % n, polar(th, 6.30, -1.1, z), polar(th, 6.30, 0.95, z), 0.05, 0.15,
             'Mat_Deco_Metal_DarkIron', C_OPEN, wob=0.004, up=Vector((0, 0, 1)), parent=leaf)
    leaf.rotation_euler = (0, 0, radians(72))
    # bar rested against the wall
    beam('Mesh_DoorBar', polar(th, 5.82, -1.9, 0.9), polar(th, 5.82, -1.9, 3.1), 0.18, 0.18, WOOD, C_OPEN,
         round_=True, wob=0.01)


def build_window(theta, idx):
    WOOD = 'Mat_Deco_Wood_Dark'
    hw, z0, zs = 0.5, 2.0, 3.1
    rad = polar(theta, 1, 0, 0)
    prism_cutter('Cut_Window_%d' % idx, theta, arch_poly(hw, z0, zs, 10), 5.0, 8.0)
    beam('Mesh_WinJamb_%dL' % idx, polar(theta, 5.98, -0.6, z0 - 0.02), polar(theta, 5.98, -0.6, zs), 0.22, 0.4,
         WOOD, C_OPEN, wob=0.006, up=rad)
    beam('Mesh_WinJamb_%dR' % idx, polar(theta, 5.98, 0.6, z0 - 0.02), polar(theta, 5.98, 0.6, zs), 0.22, 0.4,
         WOOD, C_OPEN, wob=0.006, up=rad)
    beam('Mesh_WinSill_%d' % idx, polar(theta, 5.9, -0.85, z0 - 0.09), polar(theta, 5.9, 0.85, z0 - 0.09), 0.5, 0.16,
         'Mat_Deco_Stone_Granite', C_OPEN, wob=0.008, up=Vector((0, 0, 1)))
    N = 6
    for i in range(N):
        a0 = pi * i / N
        a1 = pi * (i + 1) / N
        beam('Mesh_WinLintel_%d_%d' % (idx, i),
             polar(theta, 5.98, cos(a0) * 0.6, zs + sin(a0) * 0.6),
             polar(theta, 5.98, cos(a1) * 0.6, zs + sin(a1) * 0.6), 0.22, 0.4, WOOD, C_OPEN, wob=0.005, up=rad)
    for side, openang in ((-1, 150), (1, 68)):
        hinge = polar(theta, 5.86, side * 0.58, 0)
        leaf = empty('Root_Shutter_%d_%s' % (idx, 'L' if side < 0 else 'R'), hinge, C_OPEN, ROOT)
        for b in range(3):
            t = side * (0.58 - 0.17 * (b + 0.5))
            beam('Mesh_Shutter_%d_%s%d' % (idx, 'L' if side < 0 else 'R', b),
                 polar(theta, 5.86, t, 2.06), polar(theta, 5.86, t, 3.45), 0.165, 0.05, WOOD, C_OPEN,
                 wob=0.004, up=rad, parent=leaf)
        for n, z in (('Low', 2.3), ('High', 3.2)):
            beam('Mesh_ShutterBatten_%d_%s%s' % (idx, 'L' if side < 0 else 'R', n),
                 polar(theta, 5.83, side * 0.58, z), polar(theta, 5.83, side * 0.07, z), 0.04, 0.1, WOOD, C_OPEN,
                 wob=0.003, up=Vector((0, 0, 1)), parent=leaf)
        leaf.rotation_euler = (0, 0, radians(-side * openang))


def build_porthole(theta, idx):
    WOOD = 'Mat_Deco_Wood_Dark'
    prism_cutter('Cut_Porthole_%d' % idx, theta, circle_poly(0.42, 5.5, 16), 5.0, 8.0)
    torus('Mesh_PortholeFrame_%d' % idx, polar(theta, 5.97, 0, 5.5), polar(theta, 1, 0, 0), 0.5, 0.08, WOOD, C_OPEN, nu=18, nv=5)


def build_niche(theta, idx):
    prism_cutter('Cut_Niche_%d' % idx, theta, arch_poly(0.42, 1.2, 1.9, 10, rise=0.5), 5.55, 6.4)
    beam('Mesh_NicheShelf_%d' % idx, polar(theta, 5.78, -0.5, 1.15), polar(theta, 5.78, 0.5, 1.15), 0.7, 0.1,
         'Mat_Deco_Wood_Dark', C_OPEN, wob=0.005, up=Vector((0, 0, 1)))


# ------------------------------------------------------------------ hanging things
def herb_bundle(name, top, mat, length=0.55):
    top = Vector(top)
    tie = top + Vector((0, 0, -0.3))
    beam(name + '_Cord', top, tie, 0.025, 0.025, 'Mat_Deco_Fibre_Rope', C_HANG, round_=True, wob=0.001, segs=2)
    beam(name + '_Binding', tie + Vector((0, 0, 0.04)), tie + Vector((0, 0, -0.08)), 0.1, 0.1, 'Mat_Deco_Fibre_Rope',
         C_HANG, round_=True, wob=0.004, segs=2)
    for k in range(5):
        a = 2 * pi * k / 5 + random.uniform(-0.3, 0.3)
        spread = random.uniform(0.07, 0.15)
        L = length * random.uniform(0.7, 1.1)
        tip = tie + Vector((cos(a) * spread, sin(a) * spread, -0.06 - L))
        beam('%s_Stalk%d' % (name, k), tie + Vector((cos(a) * 0.02, sin(a) * 0.02, -0.04)), tip, 0.07, 0.04, mat,
             C_HANG, round_=True, wob=0.006, taper=0.6, segs=3)


def cloth(name, top, width, height, mat, yaw, coll):
    NX, NZ = 8, 10
    bm = bmesh.new()
    grid = []
    for j in range(NZ + 1):
        row = []
        for i in range(NX + 1):
            u = i / NX - 0.5
            v = j / NZ
            fold = 0.05 * sin(u * 2 * pi * 2.5) * (0.3 + v)
            row.append(bm.verts.new((u * width * (1 - 0.08 * v), fold, -v * height)))
        grid.append(row)
    for j in range(NZ):
        for i in range(NX):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    m = Matrix.Translation(top) @ Matrix.Rotation(radians(yaw), 4, 'Z')
    ob = finish(name, bm, [mat], coll, m)
    ob.modifiers.new('Thickness', 'SOLIDIFY').thickness = 0.015
    return ob


def build_hanging():
    for n, (x, y, mat) in enumerate((
            (-3.9, -3.7, 'Mat_Deco_Plant_Sage'), (-3.5, -3.7, 'Mat_Deco_Plant_Olive'), (-2.9, -3.7, 'Mat_Deco_Plant_Rust'),
            (3.0, -3.7, 'Mat_Deco_Plant_Sage'), (3.4, -3.7, 'Mat_Deco_Plant_Rust'),
            (-4.1, 3.7, 'Mat_Deco_Plant_Olive'), (-3.6, 3.7, 'Mat_Deco_Plant_Sage'), (3.8, 3.7, 'Mat_Deco_Plant_Rust'),
            (2.2, 0.0, 'Mat_Deco_Plant_Olive'), (2.6, 0.0, 'Mat_Deco_Plant_Sage'), (3.1, 0.0, 'Mat_Deco_Plant_Rust'))):
        herb_bundle('Mesh_HerbBundle_%02d' % n, (x + random.uniform(-0.05, 0.05), y, 6.2), mat, 0.45 + random.uniform(0, 0.25))
    cloth('Mesh_Banner_Indigo', Vector((-1.6, -3.7, 6.2)), 1.4, 2.6, 'Mat_Deco_Fabric_Indigo', 0, C_HANG)
    cloth('Mesh_Banner_Rust', Vector((0.2, -3.7, 6.2)), 1.1, 2.1, 'Mat_Deco_Fabric_Rust', 10, C_HANG)
    cloth('Mesh_Banner_Saffron', Vector((-1.2, 3.7, 6.2)), 1.5, 2.4, 'Mat_Deco_Fabric_Saffron', 180, C_HANG)

    # armillary sphere hung over the hearth side from the middle tie beam
    cx, cy, cz = -1.6, 0.0, 4.7
    root = empty('Root_Armillary', (cx, cy, cz), C_HANG, ROOT)
    for n, dx in enumerate((-0.25, 0.25)):
        beam('Mesh_ArmillaryChain_%d' % n, (cx + dx, cy, cz + 0.85), (cx + dx * 0.1, cy, 6.2), 0.03, 0.03,
             'Mat_Deco_Metal_DarkIron', C_HANG, round_=True, wob=0.002, segs=3)
    c = Vector((cx, cy, cz))
    torus('Mesh_ArmillaryRing_Equator', c, (0, 0, 1), 0.78, 0.035, 'Mat_Deco_Metal_Brass', C_HANG, nu=36, nv=6)
    torus('Mesh_ArmillaryRing_Meridian', c, (0, 1, 0), 0.74, 0.03, 'Mat_Deco_Metal_Brass', C_HANG, nu=36, nv=6)
    torus('Mesh_ArmillaryRing_Ecliptic', c, (0.4, 0.2, 0.89), 0.7, 0.03, 'Mat_Deco_Metal_Brass', C_HANG, nu=36, nv=6)
    torus('Mesh_ArmillaryRing_Colure', c, (1, 0.35, 0), 0.66, 0.025, 'Mat_Deco_Metal_Brass', C_HANG, nu=36, nv=6)
    beam('Mesh_ArmillaryAxis', c + Vector((0, 0, -0.9)), c + Vector((0, 0, 0.9)), 0.04, 0.04, 'Mat_Deco_Metal_DarkIron',
         C_HANG, round_=True, wob=0.001, segs=2)
    beam('Mesh_ArmillaryGlobe', c + Vector((0, 0, -0.16)), c + Vector((0, 0, 0.16)), 0.32, 0.32, 'Mat_Deco_Stone_Slate',
         C_HANG, round_=True, wob=0.01, taper=0.0, segs=3)


# ------------------------------------------------------------------ props
SRC = {}


def prep_sources():
    for n in wanted:
        coll = bpy.data.collections['Coll_Deco_' + n]
        objs = list(coll.all_objects)
        root = next(o for o in objs if o.type == 'EMPTY' and o.parent is None)
        meshes = [o for o in objs if o.type == 'MESH']
        mn = Vector((1e9,) * 3)
        mx = Vector((-1e9,) * 3)
        rw = root.matrix_world
        for o in meshes:
            for v in o.bound_box:
                w = rw.inverted() @ (o.matrix_world @ Vector(v))
                mn = Vector(map(min, mn, w))
                mx = Vector(map(max, mx, w))
        SRC[n] = dict(objs=objs, root=root, mn=mn, mx=mx)


PLACED = []


def place(name, x, y, z, rotz_deg, s=1.0, coll=None, tag=''):
    src = SRC[name]
    coll = coll or C_PROPS
    idx = sum(1 for p in PLACED if p['name'] == name)
    mapping = {}
    for o in src['objs']:
        c = o.copy()
        coll.objects.link(c)
        mapping[o] = c
    for o, c in mapping.items():
        if o.parent in mapping:
            c.parent = mapping[o.parent]
            c.matrix_parent_inverse = o.matrix_parent_inverse.copy()
        for mod in c.modifiers:
            if getattr(mod, 'object', None) in mapping:
                mod.object = mapping[mod.object]
    root = mapping[src['root']]
    root.name = 'Root_%s_%d' % (name, idx)
    root.location = (x, y, z)
    root.rotation_euler = (0, 0, radians(rotz_deg))
    root.scale = (s, s, s)
    root.parent = ROOT
    PLACED.append(dict(name=name, root=root, mn=src['mn'] * s, mx=src['mx'] * s, rot=rotz_deg, loc=(x, y, z)))
    return root


def place_wall(name, theta, s=1.0, inset=0.14, z=0.0, r=None, coll=None):
    """Back of the item against radius R_IN - inset, front facing the room centre."""
    src = SRC[name]
    mn, mx = src['mn'] * s, src['mx'] * s
    phi = theta - 90.0
    rr = (r if r is not None else R_IN - inset - mx.y)
    cx = (mn.x + mx.x) / 2
    pos = polar(theta, rr, 0) - cx * Vector((sin(radians(theta)), -cos(radians(theta)), 0))
    return place(name, pos.x, pos.y, z, phi, s, coll)


def build_props():
    prep_sources()
    for name, kind, a in PROPS:
        s = a.get('s', 1.0)
        if kind == 'at':
            place(name, a['x'], a['y'], a['z'], a['rot'], s)
        elif kind == 'wall':
            if 'inset' in a and a['inset'] > 1.0:
                place_wall(name, a['theta'], s, z=0.0, r=R_IN - a['inset'])
            else:
                place_wall(name, a['theta'], s, inset=a.get('inset', 0.14))
        elif kind == 'wallish':
            place_wall(name, a['theta'], s, r=a['r'])
        elif kind == 'loft':
            if a.get('tangent'):
                src = SRC[name]
                mn, mx = src['mn'], src['mx']
                # long axis along the tangent: rotate a further 90 degrees
                depth = mx.x - mn.x
                rr = R_IN - 0.12 - depth / 2
                p = polar(a['theta'], rr, 0)
                place(name, p.x, p.y, LOFT_Z, a['theta'], s)
            else:
                place_wall(name, a['theta'], s, inset=a.get('inset', 0.14), z=LOFT_Z)
        elif kind == 'loftrug':
            p = polar(a['theta'], 4.9, 0)
            place(name, p.x, p.y, LOFT_Z + 0.012, a['theta'] - 90.0 + 90.0, 0.7)


def strip_sources():
    for n in wanted:
        coll = bpy.data.collections['Coll_Deco_' + n]
        for o in list(coll.all_objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(coll)


# ------------------------------------------------------------------ build
floor = build_floor()
wall = build_wall()
build_roof()
build_loft()
build_stair()
build_door()
for i, a in enumerate((4, 55, 100, 145)):
    build_window(a, i)
for i, a in enumerate((30, 62, 90, 120, 150, 200, 240, 300, 330, 358)):
    build_porthole(a, i)
for i, a in enumerate((77, 291)):
    build_niche(a, i)
build_hanging()
build_props()

# live Boolean: door, windows, portholes and niches stay editable
mod = wall.modifiers.new('Cut_Openings', 'BOOLEAN')
mod.operand_type = 'COLLECTION'
mod.collection = C_CUT
mod.solver = 'EXACT'
for o in C_CUT.objects:
    o.hide_set(True)

# 3 m player silhouette for scale (never exported)
bm = bmesh.new()
bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.5, radius2=0.5, depth=3.0)
bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, 1.5))
ref = finish('Ref_Player3m', bm, ['Mat_Deco_Stone_Slate'], C_REF)
ref.location = polar(DOOR_A, 4.2, 0)
ref.parent = None
ref.display_type = 'WIRE'

strip_sources()

SMOOTH = ('Mesh_Wall', 'Mesh_Floor', 'Mesh_RoofDeck')
for o in bpy.data.objects:
    if o.type == 'MESH':
        for p in o.data.polygons:
            p.use_smooth = o.name in SMOOTH
bpy.ops.wm.save_as_mainfile(filepath=OUT)
print('SAVED', OUT, len(bpy.data.objects), 'objects')
