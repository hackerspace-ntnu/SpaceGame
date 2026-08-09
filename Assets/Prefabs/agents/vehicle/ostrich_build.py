"""
Robot Ostrich builder - form pass.

Priority here is silhouette, not greebling: correct ostrich proportions, a
clean lofted body, a tapered neck and a small head. Surface detail is
deliberately sparse so the shape can be judged on its own.

Reuses Leg_01 from walker_legs.blend. The leg is rotated 180 degrees about Z
so its mid joint bends BACKWARD (a bird's hock) instead of forward like a mech
knee, and scaled on Y so it reads thin from the front.

Safe to re-run: wipes only its own OSTRICH_* collections, never the rest of the
scene. Set OSTRICH_OUT to save a .blend, otherwise it just builds in place.
"""

import bpy
import bmesh
import math
import os
from math import pi, cos, sin, radians, copysign
from mathutils import Vector, Matrix

# ---------------------------------------------------------------- config

LEG_SRC = os.environ.get(
    "OSTRICH_LEG_SRC",
    "/Users/ferdinandfremming/Documents/hackerspace/spillgruppen/SpaceGame/"
    "Assets/Prefabs/agents/vehicle/walker_legs.blend",
)
OUT = os.environ.get("OSTRICH_OUT", "")

LEG_COLL = "Leg_01"
LEG_Y = 1.15          # lateral half-spacing of the two legs
LEG_ROOT_X = 0.40     # root offset so the hip lands at x = 0.2
LEG_SCALE_Y = 0.55    # squeeze laterally: ostrich legs are thin head-on
LEG_YAW = pi          # flip so the mid joint bends backward

HIP = Vector((0.2, 0.0, 7.8))     # hip joint in world space, after the flip

SEG = 24              # radial segments on lofted hulls

# neck spine control points, base -> head
NECK_PTS = [
    (3.25, 0.0, 10.50),
    (3.15, 0.0, 11.65),
    (3.10, 0.0, 12.85),
    (3.22, 0.0, 14.05),
    (3.50, 0.0, 15.20),
    (3.88, 0.0, 16.28),
    (4.22, 0.0, 17.18),
    (4.45, 0.0, 17.95),
]
NECK_SEGS = 22


def torso_rings():
    """(x, cy, cz, ry, rz, boxiness) from tail root to chest.

    Ostrich body: a deep ovoid with a level back and a FULL chest that stays
    deep well forward before tapering to the neck. Tapering the front too
    early reads as a pear pointing the wrong way.
    Roughly 7.2 long x 4.5 deep x 3.2 wide against a 7.8 hip height.
    """
    return [
        (-3.55, 0, 9.62, 0.22, 0.32, 2.6),
        (-3.20, 0, 9.55, 0.58, 0.84, 2.4),
        (-2.70, 0, 9.35, 0.98, 1.42, 2.3),
        (-2.00, 0, 9.05, 1.32, 1.94, 2.2),
        (-1.20, 0, 8.80, 1.54, 2.22, 2.2),
        (-0.40, 0, 8.66, 1.63, 2.35, 2.2),
        (0.45, 0, 8.60, 1.65, 2.38, 2.2),
        (1.30, 0, 8.62, 1.62, 2.34, 2.2),
        (2.10, 0, 8.70, 1.50, 2.20, 2.2),
        # front centres stay LOW so the foremost point of the body is the
        # breast, not the neck root; letting cz rise here pulls the hull into
        # a teardrop that tapers to a point where the neck attaches
        (2.80, 0, 8.78, 1.32, 2.00, 2.3),
        (3.35, 0, 8.90, 1.05, 1.66, 2.4),
        (3.80, 0, 9.05, 0.68, 1.18, 2.5),
        (4.08, 0, 9.22, 0.28, 0.58, 2.6),
    ]


# ---------------------------------------------------------------- helpers

def get_coll(name, parent=None):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
    par = parent or bpy.context.scene.collection
    if c.name not in par.children:
        try:
            par.children.link(c)
        except RuntimeError:
            pass
    return c


def wipe():
    """Remove previous output only."""
    for cname in list(bpy.data.collections.keys()):
        if not (cname.startswith("OSTRICH") or cname.startswith(LEG_COLL)):
            continue
        c = bpy.data.collections[cname]
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(c)
    for junk in ("Cube", "Light", "Camera"):
        o = bpy.data.objects.get(junk)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)


def mat(name, rgb, metallic, rough, emit=None, emit_strength=1.0):
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1.0)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    if emit is not None:
        b.inputs["Emission Color"].default_value = (*emit, 1.0)
        b.inputs["Emission Strength"].default_value = emit_strength
    return m


def _cone(bm, r1, r2, depth, segments, matrix):
    kw = dict(cap_ends=True, cap_tris=False, segments=segments,
              depth=depth, matrix=matrix)
    try:
        bmesh.ops.create_cone(bm, radius1=r1, radius2=r2, **kw)
    except TypeError:
        bmesh.ops.create_cone(bm, diameter1=r1, diameter2=r2, **kw)


def cyl(bm, r, depth, matrix=None, segments=16, r2=None):
    _cone(bm, r, r if r2 is None else r2, depth, segments,
          matrix or Matrix.Identity(4))


def box(bm, size, matrix=None):
    """size: (x, y, z) full extents."""
    M = (matrix or Matrix.Identity(4)) @ Matrix.Diagonal(Vector((*size, 1.0)))
    bmesh.ops.create_cube(bm, size=1.0, matrix=M)


def sphere(bm, r, matrix=None, u=24, v=14):
    kw = dict(u_segments=u, v_segments=v, matrix=matrix or Matrix.Identity(4))
    try:
        bmesh.ops.create_uvsphere(bm, radius=r, **kw)
    except TypeError:
        bmesh.ops.create_uvsphere(bm, diameter=r, **kw)


def obj_from_bm(name, bm, material, coll, smooth=False, bevel=None):
    me = bpy.data.meshes.new(name)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    ob = bpy.data.objects.new(name, me)
    if material:
        me.materials.append(material)
    coll.objects.link(ob)
    if bevel:
        b = ob.modifiers.new("Bevel", "BEVEL")
        b.width = bevel
        b.segments = 2
        b.limit_method = 'ANGLE'
        b.angle_limit = radians(50)
    return ob


def align(a, b, roll=0.0):
    """Matrix placing a +Z-aligned primitive between points a and b."""
    a, b = Vector(a), Vector(b)
    d = b - a
    L = d.length
    if L < 1e-6:
        return Matrix.Translation(a), 0.0
    M = (Matrix.Translation((a + b) * 0.5)
         @ d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
         @ Matrix.Rotation(roll, 4, 'Z'))
    return M, L


def gear(bm, r_root, r_tip, teeth, width, matrix=None):
    M = matrix or Matrix.Identity(4)
    cyl(bm, r_root, width, M, segments=max(24, teeth * 2))
    tw = (2 * pi * r_root / teeth) * 0.46
    for i in range(teeth):
        a = 2 * pi * i / teeth
        T = (M @ Matrix.Rotation(a, 4, 'Z')
             @ Matrix.Translation(((r_root + r_tip) * 0.5, 0, 0)))
        box(bm, (r_tip - r_root, tw, width * 0.92), T)


def superellipse(a, ry, rz, n):
    """Point on a superellipse; n=2 is a true ellipse, higher is boxier."""
    c, s = cos(a), sin(a)
    e = 2.0 / n
    return (ry * copysign(abs(c) ** e, c),
            rz * copysign(abs(s) ** e, s))


def loft(bm, rings, seg=SEG, cap_start=True, cap_end=True):
    """rings: list of (x, cy, cz, ry, rz, n) cross sections along +X."""
    prev = None
    first = None
    for (x, cy, cz, ry, rz, n) in rings:
        row = []
        for i in range(seg):
            a = 2 * pi * i / seg
            y, z = superellipse(a, ry, rz, n)
            row.append(bm.verts.new((x, cy + y, cz + z)))
        bm.verts.ensure_lookup_table()
        if prev:
            for i in range(seg):
                j = (i + 1) % seg
                bm.faces.new((prev[i], prev[j], row[j], row[i]))
        else:
            first = row
        prev = row
    if cap_start and first:
        bm.faces.new(list(reversed(first)))
    if cap_end and prev:
        bm.faces.new(prev)


def surf(rings, x, a, offset=0.0):
    """Point on the torso skin, pushed out by an absolute normal distance.

    Absolute, not a scale factor: the hull is a faceted loft sitting inside
    the ideal superellipse, so proportional placement half-buries details at
    the narrow stations.
    """
    r = min(rings, key=lambda rr: abs(rr[0] - x))
    y, z = superellipse(a, r[3], r[4], r[5])
    n = Vector((0.0, y, z))
    n = n.normalized() if n.length > 1e-9 else Vector((0.0, 1.0, 0.0))
    return Vector((x, y, r[2] + z)) + n * offset, math.atan2(z, y)


def skin_frame(rings, x, a, offset=0.0):
    """Frame on the torso skin whose local +Y is the outward normal."""
    p, t = surf(rings, x, a, offset)
    return Matrix.Translation(p) @ Matrix.Rotation(t, 4, 'X')


def catmull(pts, n):
    P = [Vector(p) for p in pts]
    P = [P[0] * 2 - P[1]] + P + [P[-1] * 2 - P[-2]]
    segs = len(P) - 3
    out = []
    for i in range(n):
        t = (i / (n - 1)) * segs
        k = min(int(t), segs - 1)
        u = t - k
        p0, p1, p2, p3 = P[k], P[k + 1], P[k + 2], P[k + 3]
        out.append(0.5 * ((2 * p1) + (-p0 + p2) * u
                          + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u
                          + (-p0 + 3 * p1 - 3 * p2 + p3) * u ** 3))
    return out


# ---------------------------------------------------------------- materials

def materials():
    M = {}
    for key, name, fallback in (
        ("hull", "LEG_Hull", ((0.404, 0.373, 0.318), 0.35, 0.62)),
        ("dark", "LEG_DarkMetal", ((0.113, 0.113, 0.125), 0.85, 0.42)),
        ("steel", "LEG_Piston", ((0.760, 0.780, 0.800), 1.00, 0.16)),
        ("accent", "LEG_Accent", ((0.482, 0.208, 0.067), 0.55, 0.55)),
    ):
        m = bpy.data.materials.get(name)
        if m is None:
            m = mat(name, *fallback)
        M[key] = m
    M["lens"] = mat("OSTRICH_Lens", (0.9, 0.25, 0.06), 0.0, 0.12,
                    emit=(1.0, 0.35, 0.08), emit_strength=6.0)
    return M


# ---------------------------------------------------------------- assembly

def import_legs(root):
    """Append Leg_01 twice, flipped to bend backward and thinned laterally."""
    legs = get_coll("OSTRICH_Legs", root)
    for side, y in (("L", LEG_Y), ("R", -LEG_Y)):
        with bpy.data.libraries.load(LEG_SRC, link=False) as (src, dst):
            if LEG_COLL not in src.collections:
                raise RuntimeError(f"{LEG_COLL} not found in {LEG_SRC}")
            dst.collections = [LEG_COLL]
        appended = dst.collections[0]
        for o in list(appended.objects):
            appended.objects.unlink(o)
            legs.objects.link(o)
            if o.parent is None:
                o.name = f"OSTRICH_Leg_{side}_Root"
                o.location = (LEG_ROOT_X, y, 0.0)
                o.rotation_euler = (0.0, 0.0, LEG_YAW)
                o.scale = (1.0, LEG_SCALE_Y, 1.0)
        bpy.data.collections.remove(appended)
    # collapse the duplicated LEG_* materials back onto the originals
    for m in list(bpy.data.materials):
        if "." not in m.name or not m.name.startswith("LEG_"):
            continue
        base = bpy.data.materials.get(m.name.rsplit(".", 1)[0])
        if base and base is not m:
            m.user_remap(base)
            bpy.data.materials.remove(m)
    return legs


def build_pelvis(root, M):
    """Just enough structure to carry the hips; stays inside the hull."""
    c = get_coll("OSTRICH_Pelvis", root)

    bm = bmesh.new()
    box(bm, (2.4, 2.0, 1.2), Matrix.Translation((HIP.x, 0, HIP.z + 0.25)))
    obj_from_bm("OSTRICH_PelvisHull", bm, M["hull"], c, bevel=0.05)

    bm = bmesh.new()
    for y in (LEG_Y, -LEG_Y):
        s = 1 if y > 0 else -1
        cyl(bm, 0.52, 0.55,
            Matrix.Translation((HIP.x, y - s * 0.10, HIP.z))
            @ Matrix.Rotation(pi / 2, 4, 'X'), 18)
    obj_from_bm("OSTRICH_HipHubs", bm, M["dark"], c, bevel=0.04)

    # no added hip gear: Leg_01 already carries LEG_HipGear at this exact
    # joint, and a second one just clashes with it
    return c


def build_torso(root, M):
    c = get_coll("OSTRICH_Torso", root)
    rings = torso_rings()

    bm = bmesh.new()
    loft(bm, rings)
    obj_from_bm("OSTRICH_TorsoHull", bm, M["hull"], c, smooth=True, bevel=0.02)

    # single accent stripe for colour; no seam rings in the form pass, they
    # fringe the silhouette and fight the shape being judged
    bm = bmesh.new()
    for s in (1, -1):
        a = radians(4) if s > 0 else pi - radians(4)
        F = skin_frame(rings, 1.80, a, 0.03)
        box(bm, (1.10, 0.09, 0.22), F @ Matrix.Translation((0, 0.04, 0)))
    obj_from_bm("OSTRICH_ShoulderStripe", bm, M["accent"], c, bevel=0.02)
    return c


def build_neck(root, M):
    c = get_coll("OSTRICH_Neck", root)
    path = catmull(NECK_PTS, NECK_SEGS)

    def tangent(i):
        a = path[max(0, i - 1)]
        b = path[min(len(path) - 1, i + 1)]
        d = b - a
        return d.normalized() if d.length > 1e-6 else Vector((0, 0, 1))

    def rad(i):
        t = i / (NECK_SEGS - 1)
        return 0.50 - 0.24 * t

    hubs = bmesh.new()
    collars = bmesh.new()
    for i, p in enumerate(path):
        B = (Matrix.Translation(p)
             @ tangent(i).to_track_quat('Z', 'Y').to_matrix().to_4x4())
        r = rad(i)
        # depth exceeds spacing so the stack never gaps
        cyl(hubs, r, 0.44, B, 16)
        if i % 3 == 1:
            cyl(collars, r * 1.10, 0.08, B @ Matrix.Translation((0, 0, 0.16)), 16)
    obj_from_bm("OSTRICH_NeckSegments", hubs, M["dark"], c, bevel=0.03)
    obj_from_bm("OSTRICH_NeckCollars", collars, M["accent"], c)

    # base housing where the neck meets the chest
    bm = bmesh.new()
    B = (Matrix.Translation(path[0])
         @ tangent(0).to_track_quat('Z', 'Y').to_matrix().to_4x4())
    cyl(bm, 0.66, 0.55, B @ Matrix.Translation((0, 0, -0.16)), 18)
    obj_from_bm("OSTRICH_NeckBase", bm, M["hull"], c, bevel=0.04)
    return c, path, tangent


def build_head(root, M, path):
    """Head frame is world-aligned: local +X is the beak, +Z is up.

    Deriving it from the neck tangent would aim the beak along the neck,
    which points almost straight up at the top.
    """
    c = get_coll("OSTRICH_Head", root)
    neck_end = path[-1]
    centre = neck_end + Vector((0.52, 0, 0.45))
    H = Matrix.Translation(centre) @ Matrix.Rotation(radians(10), 4, 'Y')

    def h(x, y, z):
        return H @ Matrix.Translation((x, y, z))

    # cranium: small, rounded
    bm = bmesh.new()
    sphere(bm, 1.0, h(0, 0, 0) @ Matrix.Diagonal(Vector((0.82, 0.50, 0.48, 1.0))))
    obj_from_bm("OSTRICH_Skull", bm, M["hull"], c, smooth=True, bevel=0.02)

    # coupler down to the last vertebra so the head never floats
    bm = bmesh.new()
    Mc, Lc = align(neck_end, centre + Vector((-0.40, 0, -0.08)))
    cyl(bm, 0.28, Lc + 0.24, Mc, 14)
    obj_from_bm("OSTRICH_HeadCoupler", bm, M["dark"], c, bevel=0.02)

    # upper beak: short, broad, blunt
    bm = bmesh.new()
    loft(bm, [
        (0.34, 0, 0.02, 0.40, 0.28, 2.6),
        (0.72, 0, 0.00, 0.38, 0.25, 2.8),
        (1.06, 0, -0.06, 0.31, 0.19, 3.0),
        (1.32, 0, -0.14, 0.21, 0.12, 3.0),
        (1.48, 0, -0.24, 0.09, 0.06, 3.0),
    ], seg=14)
    bm.transform(H)
    obj_from_bm("OSTRICH_BeakUpper", bm, M["dark"], c, smooth=True, bevel=0.015)

    # lower beak
    bm = bmesh.new()
    loft(bm, [
        (0.34, 0, -0.25, 0.34, 0.16, 2.6),
        (0.76, 0, -0.27, 0.30, 0.14, 2.8),
        (1.10, 0, -0.32, 0.22, 0.10, 3.0),
        (1.34, 0, -0.39, 0.10, 0.06, 3.0),
    ], seg=14)
    bm.transform(H)
    obj_from_bm("OSTRICH_BeakLower", bm, M["hull"], c, smooth=True, bevel=0.015)

    # eyes: ostrich eyes are large relative to the skull
    housings = bmesh.new()
    lenses = bmesh.new()
    for s in (1, -1):
        E = h(0.04, s * 0.44, 0.16)
        R = Matrix.Rotation(pi / 2, 4, 'X')
        cyl(housings, 0.30, 0.22, E @ R, 16)
        sphere(lenses, 0.225, E @ Matrix.Translation((0.02, s * 0.07, 0)))
    obj_from_bm("OSTRICH_EyeHousings", housings, M["dark"], c, bevel=0.015)
    obj_from_bm("OSTRICH_EyeLenses", lenses, M["lens"], c, smooth=True)
    return c, H


def build_tail(root, M):
    """A short upward tuft, not a display fan."""
    c = get_coll("OSTRICH_Tail", root)
    base = Vector((-3.30, 0, 9.95))

    bm = bmesh.new()
    cyl(bm, 0.26, 0.42,
        Matrix.Translation(base) @ Matrix.Rotation(pi / 2, 4, 'X'), 14)
    obj_from_bm("OSTRICH_TailHub", bm, M["dark"], c, bevel=0.03)

    bm = bmesh.new()
    for i in range(5):
        f = (i - 2) / 2.0
        yaw = radians(20) * f
        pitch = radians(30) - radians(14) * abs(f)
        L = 1.60 - 0.30 * abs(f)
        Mx = (Matrix.Translation(base)
              @ Matrix.Rotation(yaw, 4, 'Z')
              @ Matrix.Rotation(-pitch, 4, 'Y')
              @ Matrix.Translation((-L / 2 - 0.18, 0, 0)))
        box(bm, (L, 0.14, 0.34), Mx)
    obj_from_bm("OSTRICH_TailBlades", bm, M["hull"], c, bevel=0.025)
    return c


def build_wings(root, M):
    """Folded wings: a smooth mass lying along the flank.

    Flat slabs on the skin read as fins and break the silhouette, so this is
    a squashed ellipsoid blended into the body instead.
    """
    c = get_coll("OSTRICH_Wings", root)

    for side, s in (("L", 1), ("R", -1)):
        bm = bmesh.new()
        W = (Matrix.Translation((0.60, s * 1.42, 9.45))
             @ Matrix.Rotation(radians(-8), 4, 'Y')
             @ Matrix.Rotation(radians(6) * s, 4, 'Z'))
        sphere(bm, 1.0, W @ Matrix.Diagonal(Vector((1.55, 0.42, 0.95, 1.0))))
        obj_from_bm(f"OSTRICH_Wing{side}", bm, M["hull"], c, smooth=True)

        # a couple of blade tips trailing off the back of the wing
        bm = bmesh.new()
        for i in range(3):
            f = (i - 1) / 1.0
            L = 1.05 - 0.18 * abs(f)
            Mb = (Matrix.Translation((-0.85, s * 1.44, 9.35 + 0.20 * f))
                  @ Matrix.Rotation(radians(6) * s, 4, 'Z')
                  @ Matrix.Rotation(radians(10 + 8 * f), 4, 'Y')
                  @ Matrix.Translation((-L / 2, 0, 0)))
            box(bm, (L, 0.11, 0.30), Mb)
        obj_from_bm(f"OSTRICH_Wing{side}_Tips", bm, M["hull"], c, bevel=0.03)
    return c


# ---------------------------------------------------------------- main

def main():
    wipe()
    M = materials()
    root = get_coll("OSTRICH")

    import_legs(root)
    build_pelvis(root, M)
    build_torso(root, M)
    build_tail(root, M)
    build_wings(root, M)
    _, path, _ = build_neck(root, M)
    build_head(root, M, path)

    n_obj = sum(len(c.objects) for c in bpy.data.collections
                if c.name.startswith("OSTRICH"))
    print(f"OSTRICH_BUILD_OK objects={n_obj}")

    if OUT:
        os.makedirs(os.path.dirname(OUT), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=OUT)
        print("OSTRICH_SAVED", OUT)


main()
