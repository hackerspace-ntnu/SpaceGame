"""Append the robot helmets from robot+helmet+3d+model.fbx into nomad.blend.

ADDITIVE ONLY. Nothing pre-existing in nomad.blend is moved, renamed or deleted -
in particular Sphere.002 (the current dome over the head) is left exactly as it is.

Source FBX holds one mesh with 9 loose islands: three standalone helmets, one
helmet welded to a bust, and small detail islands that get folded into whichever
big island they sit on.

Each helmet's plane of bilateral symmetry is detected and aligned to the
character's own mirror plane (world X), so the ONLY orientation freedom left is
a rotation about X. Roll is a best guess matched against the bust helmet, which
the FBX happens to ship upright - check it and rotate about X if it is off.

Run:  blender --background nomad.blend --python nomad_add_helmets.py
"""
import bpy, bmesh, math
import numpy as np
from mathutils import Vector, Matrix

FBX = "/Users/ferdinandfremming/Downloads/robot+helmet+3d+model.fbx"
COLL_NAME = "Robot Helmets"
BUST_CUT_Z = 0.395                     # frees the helmet from the neck
BUST_TRIM_CO = (0.0, 0.0, 0.44)        # tilted second cut, drops the cloak collar
BUST_TRIM_TILT = math.radians(20.0)

# where the parked helmets go: in front of the character, at head height
HEAD_CENTER = Vector((7.107, 0.040, 2.680))    # Sphere.001, the head
PARK_Y = -1.25
PARK_SPACING = 0.55
TARGET_WIDTH = 0.45                    # helmet X extent, vs the 0.36-wide head

ROLL = {"Robot_Helmet_01": 0.0, "Robot_Helmet_02": 0.0,
        "Robot_Helmet_03": 0.0, "Robot_Helmet_Bust": 0.0}


# ------------------------------------------------------------------ symmetry
def verts_np(o):
    m = o.matrix_world
    return np.array([list(m @ v.co) for v in o.data.vertices], dtype=np.float64)


def chamfer(a, b):
    d = np.linalg.norm(a[:, None, :] - b[None, :, :], axis=2)
    return 0.5 * (d.min(axis=1).mean() + d.min(axis=0).mean())


def fib_dirs(n):
    i = np.arange(n) + 0.5
    phi = np.arccos(1 - i / n)
    theta = np.pi * (1 + 5 ** 0.5) * i
    return np.stack([np.sin(phi) * np.cos(theta),
                     np.sin(phi) * np.sin(theta), np.cos(phi)], axis=1)


def symmetry_normal(P):
    """Normal of the plane through the centroid that best mirrors the cloud."""
    c = P.mean(axis=0)
    Q = P - c
    sub = Q[np.random.default_rng(0).choice(len(Q), min(240, len(Q)), replace=False)]

    def score(n):
        return chamfer(sub, sub - 2.0 * np.outer(sub @ n, n))

    best = fib_dirs(1200)
    best = best[np.argsort([score(n) for n in best])[:6]]
    rng = np.random.default_rng(1)
    for k in range(4):
        pool = [n for n in best]
        for n in best:
            for _ in range(40):
                m = n + rng.normal(0, 0.08 / (k + 1), 3)
                pool.append(m / np.linalg.norm(m))
        pool = np.array(pool)
        best = pool[np.argsort([score(n) for n in pool])[:6]]
    n = best[0] / np.linalg.norm(best[0])
    return n, c, float(score(n))


def apply_transform(o):
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)


def bisect(o, co, no):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                           plane_co=co, plane_no=no, clear_inner=True)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()


# ------------------------------------------------------------------ guard
pre_existing = {o.name for o in bpy.data.objects}
pre_mesh_ids = {o.name: len(o.data.vertices) for o in bpy.data.objects if o.type == 'MESH'}


def ensure_object_mode():
    """nomad.blend can be saved with no active object, which fails every poll()."""
    vl = bpy.context.view_layer
    if vl.objects.active is None or vl.objects.active.name not in vl.objects:
        vl.objects.active = next(o for o in vl.objects if o.type == 'MESH')
    if bpy.context.object and bpy.context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')


ensure_object_mode()

# ------------------------------------------------------------------ import + split
bpy.ops.object.select_all(action='DESELECT')
bpy.ops.import_scene.fbx(filepath=FBX)
imported = [o for o in bpy.context.selected_objects if o.type == 'MESH']
if len(imported) != 1:
    raise RuntimeError("expected one mesh in the FBX, got %d" % len(imported))
src = imported[0]
apply_transform(src)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.separate(type='LOOSE')
bpy.ops.object.mode_set(mode='OBJECT')

new_parts = [o for o in bpy.data.objects if o.type == 'MESH' and o.name not in pre_existing]
new_parts.sort(key=lambda o: -len(o.data.vertices))
big, small = new_parts[:4], new_parts[4:]
for s in small:                                    # fold detail islands into their host
    sc = Vector(verts_np(s).mean(axis=0))
    host = min(big, key=lambda b: (Vector(verts_np(b).mean(axis=0)) - sc).length)
    bpy.ops.object.select_all(action='DESELECT')
    s.select_set(True)
    host.select_set(True)
    bpy.context.view_layer.objects.active = host
    bpy.ops.object.join()
big = [o for o in bpy.data.objects if o.type == 'MESH' and o.name not in pre_existing]
big.sort(key=lambda o: -len(o.data.vertices))
bust, loose = big[0], big[1:]

# ------------------------------------------------------------------ free the bust helmet
bisect(bust, (0, 0, BUST_CUT_Z), (0, 0, 1))
bisect(bust, BUST_TRIM_CO, (0, -math.sin(BUST_TRIM_TILT), math.cos(BUST_TRIM_TILT)))
bust.name = bust.data.name = "Robot_Helmet_Bust"
print("bust helmet freed -> %d verts" % len(bust.data.vertices))

# ------------------------------------------------------------------ orient
for i, o in enumerate(loose, start=1):
    o.name = o.data.name = "Robot_Helmet_%02d" % i
helmets = [bust] + loose

for o in helmets:
    if o is bust:
        n, c = np.array([1.0, 0.0, 0.0]), verts_np(o).mean(axis=0)   # already upright
        res = 0.0
    else:
        n, c, res = symmetry_normal(verts_np(o))
    x = Vector(n).normalized()
    tmp = Vector((0, 0, 1)) if abs(x.z) < 0.9 else Vector((0, 1, 0))
    y = tmp.cross(x).normalized()
    z = x.cross(y).normalized()
    R = Matrix((x, y, z))
    if R.determinant() < 0:
        R = Matrix((-x, y, z))
    o.matrix_world = (Matrix.Rotation(ROLL[o.name], 4, 'X')
                      @ R.to_4x4() @ Matrix.Translation(-Vector(c)))
    apply_transform(o)
    print("%-20s symmetry normal %s  residual %.5f" %
          (o.name, [round(float(v), 3) for v in n], res))

# ------------------------------------------------------------------ clean, scale, park
mat = bpy.data.materials.get("Robot_Helmet")
if mat is None:
    mat = bpy.data.materials.new("Robot_Helmet")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (0.32, 0.33, 0.35, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.45
    bsdf.inputs["Metallic"].default_value = 0.6

coll = bpy.data.collections.get(COLL_NAME)
if coll is None:
    coll = bpy.data.collections.new(COLL_NAME)
    bpy.context.scene.collection.children.link(coll)

for idx, o in enumerate(helmets):
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()

    o.data.materials.clear()
    o.data.materials.append(mat)

    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.origin_set(type='ORIGIN_GEOMETRY', center='BOUNDS')

    s = TARGET_WIDTH / o.dimensions.x
    o.scale = (s, s, s)
    apply_transform(o)

    o.location = Vector((HEAD_CENTER.x + (idx - 1.5) * PARK_SPACING,
                         PARK_Y, HEAD_CENTER.z))
    for c in list(o.users_collection):
        c.objects.unlink(o)
    coll.objects.link(o)
    print("%-20s dims %s  parked at %s" %
          (o.name, [round(v, 3) for v in o.dimensions],
           [round(v, 3) for v in o.location]))

# ------------------------------------------------------------------ verify additive
for name, nverts in pre_mesh_ids.items():
    o = bpy.data.objects.get(name)
    if o is None:
        raise RuntimeError("pre-existing object %s went missing" % name)
    if len(o.data.vertices) != nverts:
        raise RuntimeError("pre-existing object %s was modified" % name)
print("pre-existing objects intact: %d" % len(pre_mesh_ids))

bpy.ops.wm.save_mainfile()
print("SAVED nomad.blend")
