"""Gyre test section - one bay of a carved hall, built to check the design
language in 3-D before the rest of the POI is modelled.

Run:  blender --background --python gyre_test_section.py -- --out gyre_test_section.blend

What it holds (see gyre_test_section_BUILD.md):
  - the vault shell: a 22.5 m length of parabolic hall (28 m wide at the
    floor, 32 m high) with filleted wall-floor joints, incised line pairs
    along and around the vault, and a channel holding a metal inlay rib
  - the end wall, with holes cut exactly to the reveals' outer edges
  - a Mid 3-step arch reveal, an oculus family (R5 offset + two R1.5) and a
    ringed socket with a 6 m rod coming out of it
  - two H12_Fan grown columns whose rising 4-rib fans cross over the
    hall's centre line and run on into the vault
  - conduits: a floor line running into the arch, and a wall line from the
    socket up to an oculus

The hall faces -Y: stand at y < 0 and look in. Origin: floor centre of the
open front.
"""

import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GYRE = os.path.normpath(os.path.join(HERE, "..", "..", "..", "components",
                                     "structural", "gyre"))
MECH = os.path.normpath(os.path.join(HERE, "..", "..", "..", "components",
                                     "mechanical", "gyre"))
sys.path.insert(0, GYRE)
from _gyrelib import *  # noqa: E402,F403
from _buildlib import append_objects  # noqa: E402
from _gyrelib import _normals  # noqa: E402

OUT = out_path(__file__, "gyre_test_section.blend")

# hall
W, H = 28.0, 32.0          # inner width at the floor, inner height
LENGTH = 22.5              # front (y = 0) to the back of the end wall
SHELL_T = 3.0              # shell thickness
FILLET = 2.4
WALL_Y = 20.0              # end wall front face
WALL_T = 2.5               # = Mid arch reveal depth
ARCH_SEG = 30              # = gyre_arch_reveal Mid segments
RIB_GROOVES = (5.0, 11.0)  # incised pairs around the vault
INLAY_RIB = 16.0           # station of the metal inlay rib
INLAY_W = 0.5

# ---------------------------------------------------------------------------
# Profile of the hall, counter-clockwise in (x, z): floor left->right, up the
# right wall, over the crown, down the left wall.
# ---------------------------------------------------------------------------


def hall_profile():
    arch = list(reversed(arch_curve(W, H, 56)))    # right foot -> left foot
    # right fillet: first arch point above FILLET
    i_r = next(i for i, p in enumerate(arch) if p.y >= FILLET)
    i_l = len(arch) - 1 - next(i for i, p in enumerate(reversed(arch))
                               if p.y >= FILLET)
    b_r, b_l = arch[i_r], arch[i_l]

    def fillet(b, tangent_from, floor_dir):
        t = (b - tangent_from).normalized()
        s = -b.y / t.y
        c = b + t * s                        # where the wall's tangent meets the floor
        a = c + Vector((floor_dir * FILLET * 1.2, 0.0))
        pts = []
        for k in range(1, 7):
            u = k / 7.0
            pts.append(a * (1 - u) ** 2 + c * 2 * u * (1 - u) + b * u * u)
        return a, pts

    a_r, fr = fillet(b_r, arch[i_r + 1], -1.0)
    a_l, fl = fillet(b_l, arch[i_l - 1], 1.0)
    floor = [a_l.lerp(a_r, k / 8.0) for k in range(9)]
    prof = floor + fr + arch[i_r:i_l + 1] + list(reversed(fl))
    return prof


def outward(prof):
    # profile is counter-clockwise, so (t.y, -t.x) points into the stone
    return _normals(prof, True)


def insert_grooves(prof, centres, width=GROOVE_W, depth=GROOVE_D):
    """Cut grooves running along the vault: each centre is an arc-length
    position on the profile; 4 points per groove."""
    cum = [0.0]
    for a, b in zip(prof, prof[1:] + prof[:1]):
        cum.append(cum[-1] + (b - a).length)
    nrm = outward(prof)
    marks = []
    for c in centres:
        for s, d in ((c - width / 2, 0), (c - width / 2, depth),
                     (c + width / 2, depth), (c + width / 2, 0)):
            marks.append((s, d))
    out = []
    flags = []  # True for points on a groove floor
    n = len(prof)
    j = 0
    marks.sort()
    for i in range(n):
        a, b = prof[i], prof[(i + 1) % n]
        out.append(a)
        flags.append(0.0)
        while j < len(marks) and marks[j][0] < cum[i + 1]:
            s, d = marks[j]
            f = (s - cum[i]) / max(cum[i + 1] - cum[i], 1e-9)
            p = a.lerp(b, f)
            nv = nrm[i].lerp(nrm[(i + 1) % n], f).normalized()
            out.append(p + nv * d)
            flags.append(d)
            j += 1
    return out


def arc_positions(prof):
    """Arc-length positions for the long grooves: pairs at 7 m and 15 m up
    each wall and one pair over the crown."""
    cum = [0.0]
    for a, b in zip(prof, prof[1:]):
        cum.append(cum[-1] + (b - a).length)

    def at_height(z, right):
        idx = range(len(prof) - 1)
        for i in (idx if right else reversed(idx)):
            a, b = prof[i], prof[i + 1]
            if (a.y - z) * (b.y - z) <= 0 and abs(a.y - b.y) > 1e-9 and \
                    ((a.x > 0) == right):
                f = (z - a.y) / (b.y - a.y)
                return cum[i] + f * (cum[i + 1] - cum[i])
        raise ValueError(z)

    crown = max(range(len(prof)), key=lambda i: prof[i].y)
    out = []
    for z in (7.0, 15.0):
        for right in (True, False):
            s = at_height(z, right)
            out += [s, s + GROOVE_GAP]
    out += [cum[crown] - GROOVE_GAP / 2, cum[crown] + GROOVE_GAP / 2]
    return out


def build_shell(coll):
    base = hall_profile()
    prof = insert_grooves(base, arc_positions(base))
    nrm = outward(prof)
    n = len(prof)

    # stations along Y; grooves around the vault are inset rows
    rows = [(0.0, 0.0)]
    for yc in RIB_GROOVES:
        for c in (yc, yc + GROOVE_GAP):
            rows += [(c - GROOVE_W / 2, 0.0), (c - GROOVE_W / 2, GROOVE_D),
                     (c + GROOVE_W / 2, GROOVE_D), (c + GROOVE_W / 2, 0.0)]
    a, b = INLAY_RIB - INLAY_W / 2 - 0.01, INLAY_RIB + INLAY_W / 2 + 0.01
    rows += [(a, 0.0), (a, 0.06), (b, 0.06), (b, 0.0), (LENGTH, 0.0)]

    bm = bmesh.new()
    inner = []
    for y, d in rows:
        ring = []
        for p, nv in zip(prof, nrm):
            q = p + nv * d if (d and p.y > 0.6) else p
            ring.append(bm.verts.new((q.x, y, q.y)))
        inner.append(ring)
    outer = [[bm.verts.new(((p + nv * SHELL_T).x, y, (p + nv * SHELL_T).y))
              for p, nv in zip(base, outward(base))] for y in (0.0, LENGTH)]
    # inner surface
    for r0, r1 in zip(inner, inner[1:]):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
    # outer surface and end caps (outer uses the groove-free profile, so the
    # caps are built as quads strip by strip from the base profile points)
    m = len(base)
    o0, o1 = outer
    for i in range(m):
        j = (i + 1) % m
        bm.faces.new((o0[i], o1[i], o1[j], o0[j]))
    # caps: bridge inner ring (with grooves) to outer ring by filling the
    # annulus between the two loops
    for ring_in, ring_out in ((inner[0], o0), (inner[-1], o1)):
        edges = []
        for ring in (ring_in, ring_out):
            for i in range(len(ring)):
                e = bm.edges.get((ring[i], ring[(i + 1) % len(ring)])) or \
                    bm.edges.new((ring[i], ring[(i + 1) % len(ring)]))
                edges.append(e)
        bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False,
                                edges=edges)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = True
        f.material_index = 0
    p = Part(link_materials([STONE]))
    p._absorb(bm, 0)
    flat_shade_steps(list(p.bm.faces), 40.0)
    shell = p.finish("Mesh_GyreTest_VaultShell", coll)

    # metal inlay rib in its channel
    ip = Part(link_materials([METAL]))
    up = [q for q in base if q.y > 0.6]
    nv_up = [nv for q, nv in zip(base, outward(base)) if q.y > 0.6]
    path = [(q + nv * (0.06 - 0.025 - INLAY_GAP)) for q, nv in zip(up, nv_up)]
    path3 = [(q.x, INLAY_RIB, q.y) for q in path]
    frame_sweep(ip, path3, lambda t: chamfer_rect(INLAY_W, 0.05, 0.01),
                side=(0, 1, 0))
    rib = ip.finish("Mesh_GyreTest_InlayRib", coll)
    return shell, rib, base


def wall_outline(base):
    """The end wall's outline: the hall profile pushed 0.08 m into the shell,
    with the arch reveal's outer edge notched out of its floor line."""
    outer = [p + nv * 0.08 for p, nv in zip(base, outward(base))]
    arch_hole = offset_curve(catenary_curve(10.0, 15.0, ARCH_SEG), -1.2)
    x_l, x_r = arch_hole[0].x, arch_hole[-1].x
    floor = [p for p in outer if abs(p.y - outer[0].y) < 1e-6]
    rest = [p for p in outer if abs(p.y - outer[0].y) >= 1e-6]
    left_floor = [p for p in floor if p.x < x_l - 0.3]
    right_floor = [p for p in floor if p.x > x_r + 0.3]
    # outer floor sits 0.08 below 0 - drop the notch feet to meet it
    zf = outer[0].y
    notch = [Vector((x_l, zf))] + arch_hole + [Vector((x_r, zf))]
    return left_floor + notch + right_floor + rest


def build_wall(coll, base, holes):
    outline = wall_outline(base)
    bm = bmesh.new()

    def loop(pts, y):
        vs = [bm.verts.new((p.x, y, p.y)) for p in pts]
        es = [bm.edges.new((vs[i], vs[(i + 1) % len(vs)])) for i in range(len(vs))]
        return vs, es

    fronts, backs = [], []
    loops = [outline] + holes
    for pts in loops:
        fronts.append(loop(pts, WALL_Y))
        backs.append(loop(pts, WALL_Y + WALL_T))
    for side in (fronts, backs):
        bmesh.ops.triangle_fill(bm, use_beauty=True, use_dissolve=False,
                                edges=[e for _, es in side for e in es])
    for (fv, _), (bv, _) in zip(fronts, backs):
        k = len(fv)
        for i in range(k):
            j = (i + 1) % k
            bm.faces.new((fv[i], fv[j], bv[j], bv[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    p = Part(link_materials([STONE]))
    p._absorb(bm, 0)
    flat_shade_steps(list(p.bm.faces), 30.0)
    return p.finish("Mesh_GyreTest_EndWall", coll)


def place(objs, loc, rz=0.0):
    for o in objs:
        if o.parent is None:
            o.location = loc
            o.rotation_euler = (0.0, 0.0, rz)


def main():
    start(OUT)
    c_shell = collection("Coll_GyreTest_Shell")
    c_open = collection("Coll_GyreTest_Openings")
    c_cols = collection("Coll_GyreTest_Columns")
    c_cond = collection("Coll_GyreTest_Conduits")
    c_mech = collection("Coll_GyreTest_Mechanisms")

    shell, rib, base = build_shell(c_shell)

    # --- openings ---------------------------------------------------------
    arch = append_objects(os.path.join(GYRE, "gyre_arch_reveal.blend"),
                          ["Mesh_GyreArchReveal_Mid_3Step_Lip",
                           "Mesh_GyreArchReveal_Mid_3Step_Lip_MetalLip"], c_open)
    place(arch, (0.0, WALL_Y, 0.0))

    oc = append_objects(os.path.join(GYRE, "gyre_oculus_reveal.blend"),
                        ["Mesh_GyreOculusReveal_R5_Offset",
                         "Mesh_GyreOculusReveal_R1_5_Offset",
                         "Mesh_GyreOculusReveal_R1_5_Centred"], c_open)
    oculi = [((0.0, 24.0), oc[0], 6.2, 40), ((7.0, 18.6), oc[1], 2.2, 24),
             ((-7.0, 18.2), oc[2], 2.2, 24)]
    holes = []
    for (x, z), o, r_out, seg in oculi:
        o.location = (x, WALL_Y, z)
        holes.append(circle(r_out, seg, (x, z)))

    sock = append_objects(os.path.join(GYRE, "gyre_socket_reveal.blend"),
                          ["Mesh_GyreSocketReveal_Ringed",
                           "Mesh_GyreSocketReveal_Ringed_MetalLip",
                           "Mesh_GyreSocketReveal_Ringed_InlayRing"], c_open)
    sx, sz = 10.0, 4.0
    place(sock, (sx, WALL_Y, sz))
    holes.append(circle(0.85 + 1.6, 28, (sx, sz)))

    build_wall(c_shell, base, holes)

    # --- columns and rib fans ---------------------------------------------
    col = append_objects(os.path.join(GYRE, "gyre_grown_column.blend"),
                         ["Mesh_GyreColumn_H12_Fan", "Mesh_GyreColumn_H12_Fan_WaistBand",
                          "Mesh_GyreColumn_H12_Fan_Inlay"], c_cols)
    fan = append_objects(os.path.join(GYRE, "gyre_rib_fan.blend"),
                         ["Mesh_GyreRibFan_4_Rising",
                          "Mesh_GyreRibFan_4_Rising_Inlay"], c_cols)
    cy = 12.0
    col = [(o, o.name) for o in col]
    fan = [(o, o.name) for o in fan]
    for side, x in (("R", 5.6), ("L", -5.6)):
        # the inlay faces the hall's centre line on both columns
        rz = math.pi if x > 0 else 0.0
        for parts, z, r in ((col, 0.0, rz), (fan, 12.0, math.radians(45.0))):
            for o, base_name in parts:
                inst = o if side == "R" else instance(o, base_name + "_L", c_cols)
                if side == "R":
                    inst.name = base_name + "_R"
                inst.location = (x, cy, z)
                inst.rotation_euler = (0.0, 0.0, r)

    # --- mechanism: a 6 m rod out of the socket -----------------------------
    rod = append_objects(os.path.join(MECH, "gyre_rod.blend"),
                         ["Arm_GyreRod_6m", "Mesh_GyreRod_6m"], c_mech)
    arm = next(o for o in rod if o.type == 'ARMATURE')
    arm.location = (sx, WALL_Y + 1.0, sz)
    arm.rotation_euler = (0.0, 0.0, math.pi)

    # --- conduits ---------------------------------------------------------
    strips = append_objects(os.path.join(GYRE, "gyre_inlay_strip.blend"),
                            ["Mesh_GyreInlay_Straight_8m",
                             "Mesh_GyreInlay_Straight_4m"], c_cond)
    s8, s4 = strips
    s8.name = "Mesh_GyreInlay_Straight_8m_Floor1"
    s8.location = (0.0, 1.0, 0.02)
    instance(s8, "Mesh_GyreInlay_Straight_8m_Floor2", c_cond, loc=(0.0, 9.0, 0.02))
    s4.name = "Mesh_GyreInlay_Straight_4m_Floor3"
    s4.location = (0.0, 17.0, 0.02)

    # unique to this model: the wall conduit from the socket ring up to the
    # right-hand oculus, laid on the wall face 2 cm proud
    start_pt = Vector((sx, sz + 2.45))
    end_c = Vector((7.0, 18.6))
    end_pt = end_c + (start_pt - end_c).normalized() * 2.2
    path = [(start_pt.lerp(end_pt, k / 4.0).x, WALL_Y + 0.005,
             start_pt.lerp(end_pt, k / 4.0).y) for k in range(5)]
    wp = Part(link_materials([METAL]))
    frame_sweep(wp, path, lambda t: chamfer_rect(INLAY_W, 0.05, 0.01),
                up=(0, -1, 0))
    wp.finish("Mesh_GyreTest_WallConduit", c_cond)

    report()
    gyre_save(OUT)


main()
