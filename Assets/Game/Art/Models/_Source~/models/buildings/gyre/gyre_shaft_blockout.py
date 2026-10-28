"""Gyre shaft blockout - the basic architecture of the shaft, at full scale.

Run:  blender --background --python gyre_shaft_blockout.py -- --out gyre_shaft_blockout.blend

A grey-box, not final art: it fixes the shaft's proportions, depths and
spaces so traversal and set pieces can be designed against them.

The shaft (Z up, rim at z = 0, origin on the shaft axis at the rim):
  - main bore, ~60 m at the rim, pinching to 30 m at -50 m, swelling to
    60 m around -95 m, pinching to 36 m near -135 m, then opening into the
    Undercroft vault from -160 m to its floor at -195 m (180 m across)
  - ring ledges (galleries) at -20, -100 and -145 m
  - Landing A: a 32 m lobe open to the sky, floor at -45 m
  - side chambers with domed ceilings: B at -85 m, C at -120 m, D at -150 m,
    on alternating sides
  - bridges across both pinches, one ramp from Landing A down to the first
  - the shaft's two hanging rings in the swell, and a placeholder of the
    machine (rings, core, pedestal) over a still-water basin at the bottom

The rock is carved by an exact boolean: one solid block minus the union of
the voids (bore, lobe, chambers, basin). Carved faces take the carved-stone
material, the crater surface the natural-rock one.

Stone colour is on trial: limestone chalk instead of the test section's pale
sandstone (it read as clay). Swap CARVED below to try another.
"""

import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GYRE = os.path.normpath(os.path.join(HERE, "..", "..", "..", "components",
                                     "structural", "gyre"))
sys.path.insert(0, GYRE)
from _gyrelib import *  # noqa: E402,F403

OUT = out_path(__file__, "gyre_shaft_blockout.blend")

CARVED = "Mat_Stone_Limestone_Chalk"
ROCK = "Mat_Stone_Redrock_Terracotta"
WATER = "Mat_Glass_Canopy_Tinted"   # placeholder until there is a water material
SEG = 56                            # around the bore

# (radius, z) down the inside of the main bore. A ledge is a horizontal step
# inward: two points at the same z.
BORE = [
    (34.0, 2.0), (33.0, 0.0), (30.0, -3.0), (28.0, -8.0), (26.0, -16.0),
    (25.0, -20.0), (22.0, -20.0),                       # rim gallery
    (20.0, -30.0), (17.0, -40.0), (15.5, -46.0), (15.0, -52.0),
    (15.5, -58.0), (17.0, -66.0), (21.0, -74.0), (26.0, -82.0),
    (30.0, -90.0), (30.5, -96.0),
    (30.0, -100.0), (26.0, -100.0),                     # swell gallery
    (25.5, -108.0), (24.0, -116.0), (21.0, -124.0), (18.5, -130.0),
    (18.0, -136.0), (18.5, -142.0),
    (19.0, -145.0), (16.5, -145.0),                     # lower gallery
    (16.5, -150.0), (18.0, -156.0), (22.0, -160.0),
    (30.0, -163.0), (45.0, -167.0), (60.0, -171.0), (75.0, -176.0),
    (86.0, -182.0), (90.0, -188.0), (90.0, -195.0),     # Undercroft vault, wall
]
FLOOR = -195.0

# (name, angle deg, distance from axis, radius, floor z, wall height, open?)
CHAMBERS = [
    ("LandingA", 210.0, 30.0, 16.0, -45.0, None, True),
    ("ChamberB", 300.0, 34.0, 13.0, -85.0, 9.0, False),
    ("ChamberC", 30.0, 30.0, 15.0, -120.0, 10.0, False),
    ("ChamberD", 120.0, 24.0, 12.0, -150.0, 8.0, False),
]


def bore_radius(z):
    """Radius of the bore at height z, ignoring ledge steps (upper side)."""
    pts = [(r, zz) for r, zz in BORE]
    for (r0, z0), (r1, z1) in zip(pts, pts[1:]):
        if z1 <= z <= z0 and z0 != z1:
            f = (z0 - z) / (z0 - z1)
            return r0 + (r1 - r0) * f
    return pts[-1][0]


def void_bore(coll):
    prof = [(0.0, 6.0), (34.0, 6.0)] + BORE + [(0.0, FLOOR)]
    p = Part(link_materials([CARVED]))
    revolve(p, prof, SEG, cap_bottom=False, cap_top=False)
    return p.finish("Void_Bore", coll)


def void_chamber(coll, name, ang, dist, rad, zf, h, open_top):
    if open_top:
        prof = [(0.0, zf), (rad, zf), (rad, 6.0), (0.0, 6.0)]
    else:
        prof = [(0.0, zf), (rad, zf), (rad, zf + h)]
        for k in range(1, 6):
            a = math.pi / 2 * k / 6
            prof.append((rad * math.cos(a), zf + h + rad * 0.55 * math.sin(a)))
        prof.append((0.0, zf + h + rad * 0.55))
    p = Part(link_materials([CARVED]))
    revolve(p, prof, 36, cap_bottom=False, cap_top=False)
    o = p.finish("Void_" + name, coll)
    a = math.radians(ang)
    o.location = (dist * math.cos(a), dist * math.sin(a), 0.0)
    return o


def void_basin(coll):
    p = Part(link_materials([CARVED]))
    revolve(p, [(0.0, FLOOR - 3.0), (26.0, FLOOR - 3.0), (26.0, FLOOR + 0.5),
                (0.0, FLOOR + 0.5)], 48, cap_bottom=False, cap_top=False)
    return p.finish("Void_Basin", coll)


def carve(coll):
    voids = collection("Coll_GyreShaft_Voids")
    void_bore(voids)
    for c in CHAMBERS:
        void_chamber(voids, *c)
    void_basin(voids)
    for o in voids.objects:
        o.data.transform(Matrix.Translation(o.location))
        o.location = (0, 0, 0)

    p = Part(link_materials([CARVED, ROCK]))
    # the block: a disc of rock, its top a shallow crater dish
    prof = [(0.0, -215.0), (125.0, -215.0), (125.0, 6.0), (80.0, 4.0),
            (50.0, 1.0), (34.0, 0.0)]
    revolve(p, prof + [(0.0, 0.0)], 64, cap_bottom=False, cap_top=False)
    block = p.finish("Mesh_GyreShaft_Shell", coll)

    mod = block.modifiers.new("Carve", 'BOOLEAN')
    mod.operation = 'DIFFERENCE'
    mod.solver = 'EXACT'
    mod.operand_type = 'COLLECTION'
    mod.collection = voids
    bpy.context.view_layer.objects.active = block
    dg = bpy.context.evaluated_depsgraph_get()
    ev = block.evaluated_get(dg)
    mesh = bpy.data.meshes.new_from_object(ev)
    block.modifiers.clear()
    old = block.data
    block.data = mesh
    bpy.data.meshes.remove(old)
    for o in list(voids.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(voids)

    # materials: everything carved, except the crater top outside the rim
    import bmesh as _bm
    bm = _bm.new()
    bm.from_mesh(block.data)
    _bm.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    for f in bm.faces:
        c = f.calc_center_median()
        top = f.normal.z > 0.5 and c.z > -0.5 and math.hypot(c.x, c.y) > 33.5
        outside = math.hypot(c.x, c.y) > 120 or c.z < -210
        f.material_index = 1 if (top or outside) else 0
        f.smooth = True
    flat_shade_steps(bm.faces, 35.0)
    bm.to_mesh(block.data)
    bm.free()
    block.data.materials.clear()
    for m in link_materials([CARVED, ROCK]):
        block.data.materials.append(m)
    return block


def chord_path(z, ang_deg, offset, embed=3.0):
    """A straight chord across the bore at height z: perpendicular to the
    direction ang, `offset` metres off the axis, ends buried `embed` metres."""
    r = bore_radius(z)
    half = math.sqrt(max(r * r - offset * offset, 1.0)) + embed
    a = math.radians(ang_deg)
    d = Vector((math.cos(a), math.sin(a), 0.0))
    n = Vector((-d.y, d.x, 0.0))
    c = n * offset
    return [tuple(c + d * (-half + 2 * half * i / 16) + Vector((0, 0, z)))
            for i in range(17)], d


def bridge(coll, name, z, ang, offset):
    path, d = chord_path(z, ang, offset)

    def sec(t):
        depth = 1.2 + 2.6 * (2 * t - 1) ** 4        # thick at the ends
        return [(u, v - depth / 2) for u, v in chamfer_rect(4.0, depth, 0.35)]

    p = Part(link_materials([CARVED]))
    frame_sweep(p, path, sec, side=(-d.y, d.x, 0.0))
    return p.finish("Mesh_GyreShaft_Bridge_" + name, coll)


def ramp(coll, name, a0, a1, z0, z1, width=4.4, thick=1.4, n=40):
    """A ramp clinging to the bore wall from angle a0 to a1 (degrees)."""
    bm = bmesh.new()
    rings = []
    for i in range(n + 1):
        t = i / n
        a = math.radians(a0 + (a1 - a0) * t)
        z = z0 + (z1 - z0) * t
        r = bore_radius(z)
        out = Vector((math.cos(a), math.sin(a), 0.0))
        pts = [(r + 1.5, z), (r - width, z), (r - width, z - thick * 0.6),
               (r - width * 0.5, z - thick), (r + 1.5, z - thick * 1.8)]
        rings.append([bm.verts.new(out * rr + Vector((0, 0, zz)))
                      for rr, zz in pts])
    m = len(rings[0])
    for a, b in zip(rings, rings[1:]):
        for k in range(m):
            j = (k + 1) % m
            bm.faces.new((a[k], a[j], b[j], b[k]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    for f in bm.faces:
        f.smooth = True
    p = Part(link_materials([CARVED]))
    p._absorb(bm, 0)
    flat_shade_steps(list(p.bm.faces), 35.0)
    return p.finish("Mesh_GyreShaft_Ramp_" + name, coll)


def hanging_ring(coll, name, z, major, minor, tilt_deg, spokes, spoke_ang0):
    p = Part(link_materials([METAL, POLISH]))
    faces = p.torus((0, 0, 0), major, minor, axis='Z', maj_seg=64, min_seg=10)
    # a polished band on the ring's inner face, where it has turned
    for f in faces:
        c = f.calc_center_median()
        if math.hypot(c.x, c.y) < major - minor * 0.6:
            f.material_index = 1
    wall = bore_radius(z)
    for k in range(spokes):
        a = math.radians(spoke_ang0 + 360.0 * k / spokes)
        a_dir = Vector((math.cos(a), math.sin(a), 0.0))
        length = wall + 1.5 - major
        mid = a_dir * (major + length / 2)
        rot = Matrix.Rotation(a, 4, 'Z') @ Matrix.Rotation(math.radians(90), 4, 'Y')
        p.cyl(tuple(mid), minor * 0.55, length, axis='Z', seg=10, rot=rot)
    o = p.finish("Mesh_GyreShaft_HangingRing_" + name, coll)
    o.location = (0, 0, z)
    o.rotation_euler = (math.radians(tilt_deg), 0.0, 0.0)
    return o


def machine(coll):
    zc = -176.0
    specs = [  # major, minor, euler (deg)
        (21.0, 1.6, (8, 0, 0)), (17.0, 1.4, (70, 0, 20)),
        (13.5, 1.2, (-35, 30, 0)), (10.0, 1.0, (110, 10, 60)),
        (7.0, 0.8, (30, -60, 0)), (4.5, 0.6, (-70, 0, 40)),
    ]
    for i, (maj, mi, eul) in enumerate(specs):
        p = Part(link_materials([METAL, POLISH]))
        faces = p.torus((0, 0, 0), maj, mi, axis='Z', maj_seg=56, min_seg=10)
        for f in faces:
            c = f.calc_center_median()
            if math.hypot(c.x, c.y) > maj + mi * 0.6:
                f.material_index = 1
        o = p.finish("Mesh_GyreShaft_MachineRing_%d" % (i + 1), coll)
        o.location = (0, 0, zc)
        o.rotation_euler = tuple(math.radians(v) for v in eul)
    # core: an octahedron placeholder (shape still open)
    bm = bmesh.new()
    s = 2.2
    v = [bm.verts.new(c) for c in ((s, 0, 0), (-s, 0, 0), (0, s, 0), (0, -s, 0),
                                    (0, 0, s * 1.3), (0, 0, -s * 1.3))]
    for a, b in ((0, 2), (2, 1), (1, 3), (3, 0)):
        bm.faces.new((v[a], v[b], v[4]))
        bm.faces.new((v[b], v[a], v[5]))
    p = Part(link_materials([GLOW]))
    p._absorb(bm, 0)
    o = p.finish("Mesh_GyreShaft_CorePlaceholder", coll)
    o.location = (0, 0, zc)
    # pedestal grown from the basin floor up to just under the core
    p = Part(link_materials([CARVED]))
    prof = []
    for k in range(13):
        t = k / 12.0
        z = (FLOOR - 3.0) + (zc - 3.6 - (FLOOR - 3.0)) * t
        r = 1.6 + 3.4 * math.exp(-t * 6) + 1.2 * math.exp(-(1 - t) * 8)
        prof.append((r, z))
    revolve(p, prof, 20)
    p.finish("Mesh_GyreShaft_Pedestal", coll)


def water(coll):
    bm = bmesh.new()
    ring = [bm.verts.new((25.9 * math.cos(2 * math.pi * i / 48),
                          25.9 * math.sin(2 * math.pi * i / 48), FLOOR - 0.8))
            for i in range(48)]
    bm.faces.new(ring)
    p = Part(link_materials([WATER]))
    p._absorb(bm, 0)
    return p.finish("Mesh_GyreShaft_Water", coll)


def main():
    start(OUT)
    c_shell = collection("Coll_GyreShaft_Shell")
    c_paths = collection("Coll_GyreShaft_Paths")
    c_mech = collection("Coll_GyreShaft_Mechanisms")

    carve(c_shell)
    bridge(c_paths, "Pinch1", -52.0, 105.0, 6.0)
    bridge(c_paths, "Pinch2", -137.0, 20.0, -5.0)
    ramp(c_paths, "LandingA", 228.0, 368.0, -45.0, -52.0)
    hanging_ring(c_mech, "Upper", -90.0, 22.0, 1.3, 4.0, 4, 15.0)
    hanging_ring(c_mech, "Lower", -106.0, 18.0, 1.1, -6.0, 3, 70.0)
    machine(c_mech)
    water(c_mech)

    report()
    gyre_save(OUT)


main()
