"""Gyre shaft - the shaft and the Undercroft beneath it, built as architecture.

Run:  blender --background --python gyre_shaft.py -- --out gyre_shaft.blend

Supersedes gyre_shaft_blockout (kept as the proportions study). Z up, rim at
z = 0, origin on the shaft axis at the rim.

The shaft, top to bottom (radii are of the bore's wall line):
  crown      terraced stone rings rising out of the crater rock, 30 m bore
  tier 1     carved wall with incised line pairs; gallery G1 (-18..-27 m)
  tier 2     narrowing wall articulated by stone fins, pipe bundles between
  pinch 1    a stone collar (-54 m, bore 31 m across) carrying a segmented
             metal bearing ring; a bridge spans it
  tier 3     fins again as the bore swells to 60 m
  swell      gallery G2 (-86..-98 m), a deep arcade of lens-shaped openings;
             two hanging rings on chains and sheaves; a long bridge across
  tier 4     carved wall with families of stepped round recesses
  pinch 2    collar (-137 m) and bridge; gallery G3 (-144..-152 m)
  foot       the bore opens into the Undercroft vault at -162 m
  Undercroft 176 m across, 40 m walls, a 76 m high dome over the machine;
             twelve metal rib frames run down the vault and walls; four tech
             ring portals; terraces step down to a still-water basin; rods
             run in from the portals; the machine hangs at -204 m.
  Landing A (open lobe, -45 m) and chambers B, C, D (-84, -118, -156 m) open
  off the bore through catenary arch mouths.
"""

import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
GYRE = os.path.normpath(os.path.join(HERE, "..", "..", "..", "components",
                                     "structural", "gyre"))
sys.path.insert(0, GYRE)
from _gyrelib import *  # noqa: E402,F403
from mathutils import noise  # noqa: E402

OUT = out_path(__file__, "gyre_shaft.blend")

CARVED = "Mat_Stone_Limestone_Chalk"
RECESS = "Mat_Stone_Granite_Pale"
ROCK = "Mat_Stone_Redrock_Mesa"
WATER = "Mat_Glass_Canopy_Tinted"
SEG = 112

# ---------------------------------------------------------------------------
# Bore line: radius as a smooth function of depth (monotone cubic)
# ---------------------------------------------------------------------------

CP = [(0.0, 30.0), (-10.0, 28.5), (-24.0, 24.0), (-40.0, 18.0),
      (-50.0, 15.5), (-58.0, 15.5), (-68.0, 19.0), (-80.0, 26.0),
      (-92.0, 30.0), (-104.0, 30.0), (-116.0, 26.5), (-128.0, 20.0),
      (-136.0, 18.0), (-146.0, 18.5), (-156.0, 20.5), (-162.0, 22.0)]


def _pchip():
    zs = [-z for z, _ in CP]          # increasing
    rs = [r for _, r in CP]
    n = len(zs)
    h = [zs[i + 1] - zs[i] for i in range(n - 1)]
    d = [(rs[i + 1] - rs[i]) / h[i] for i in range(n - 1)]
    m = [0.0] * n
    m[0], m[-1] = d[0], d[-1]
    for i in range(1, n - 1):
        if d[i - 1] * d[i] <= 0:
            m[i] = 0.0
        else:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])

    def f(z):
        x = -z
        if x <= zs[0]:
            return rs[0]
        if x >= zs[-1]:
            return rs[-1]
        i = max(k for k in range(n - 1) if zs[k] <= x)
        t = (x - zs[i]) / h[i]
        h00 = 2 * t ** 3 - 3 * t ** 2 + 1
        h10 = t ** 3 - 2 * t ** 2 + t
        h01 = -2 * t ** 3 + 3 * t ** 2
        h11 = t ** 3 - t ** 2
        return h00 * rs[i] + h10 * h[i] * m[i] + h01 * rs[i + 1] + h11 * h[i] * m[i + 1]
    return f


R = _pchip()

GALLERIES = [  # top, bottom, depth, piers
    (-18.0, -27.0, 6.0, 16),
    (-86.0, -98.0, 8.0, 20),
    (-144.0, -152.0, 5.0, 14),
]
COLLARS = [(-54.0, 6.0), (-137.0, 5.0)]          # centre, half height
BANDS = [-6.5, -70.0, -114.0]                    # projecting ring bands
GROOVES = [-9.5, -13.0, -31.0, -37.0, -43.0, -63.0, -75.0, -81.0,
           -103.0, -107.0, -111.0, -120.0, -125.0, -157.0]
FIN_TIERS = [  # top, bottom, count, angle offset
    (-28.5, -47.0, 12, 0.0),
    (-61.5, -84.0, 16, 7.5),
    (-117.5, -130.5, 12, 15.0),
]
FOOT = -162.0
DOME_R, DOME_SPRING = 88.0, -192.0
DOME_BANDS, DOME_STEP = 6, 1.8
UNDER_GALLERY = (-194.0, -203.0, 7.0, 40)   # top, bottom, depth, piers
LOBE_ANG, LOBE_FLOOR = 210.0, -46.0
FLOOR = -228.0
TERRACES = [(60.0, -231.0), (46.0, -234.0), (30.0, -238.0)]
WATER_Z = -236.5
MACHINE_Z = -204.0


def bump(z, c, half):
    """Smooth 0..1..0 over [c-half, c+half]."""
    x = (z - c) / half
    if abs(x) >= 1.0:
        return 0.0
    return 0.5 + 0.5 * math.cos(math.pi * x)


def wall_r(z):
    r = R(z)
    for c, half in COLLARS:
        r -= 2.4 * min(1.0, 1.6 * bump(z, c, half))
    for c in BANDS:
        r -= 0.9 * (1.0 if abs(z - c) <= 0.8 else
                    max(0.0, 1.0 - (abs(z - c) - 0.8) / 0.5))
    return r


def in_gallery(z):
    return any(b < z < t for t, b, _, _ in GALLERIES)


# ---------------------------------------------------------------------------
# The rock: a revolved solid whose inner face is the whole carved interior
# ---------------------------------------------------------------------------

def interior_profile():
    """(r, z, material) from the crown down to the basin floor."""
    pts = []
    # crown: three terraces stepping up and out of the crater
    crown = [(42.0, 4.2), (36.0, 4.2), (36.0, 3.0), (34.0, 3.0), (34.0, 1.8),
             (32.0, 1.8), (32.0, 0.6), (R(0.0) + 0.6, 0.6), (R(0.0), 0.0)]
    pts += [(r, z, 0) for r, z in crown]

    events = set()
    z = 0.0
    while z > FOOT:
        events.add(round(z, 3))
        z -= 0.8
    for t, b, _, _ in GALLERIES:
        events |= {t, b}
    for c, half in COLLARS:
        events |= {c + 1.2, c - 1.2, c + half, c - half}
    for c in BANDS:
        events |= {c + 1.3, c + 0.8, c - 0.8, c - 1.3}
    events.add(FOOT)
    zs = sorted((e for e in events if FOOT <= e <= 0.0), reverse=True)

    groove_marks = []
    for g in GROOVES:
        for c in (g, g - GROOVE_GAP):
            groove_marks.append((c + GROOVE_W / 2, c - GROOVE_W / 2))
    channel_marks = [(c + 1.2, c - 1.2) for c, _ in COLLARS]

    gal = {t: (t, b, d) for t, b, d, _ in GALLERIES}
    skip_until = None
    for z in zs:
        if skip_until is not None and z > skip_until:
            continue
        if skip_until is not None and abs(z - skip_until) < 1e-6:
            skip_until = None
            continue
        if z in gal:
            t, b, d = gal[z]
            pts.append((wall_r(t), t, 0))
            pts.append((wall_r(t) + d, t, 1))          # gallery ceiling
            zz = t - 0.8
            while zz > b:
                pts.append((R(zz) + d, zz, 1))         # back wall
                zz -= 0.8
            pts.append((wall_r(b) + d, b, 1))
            pts.append((wall_r(b), b, 0))              # gallery floor
            skip_until = b
            continue
        r = wall_r(z)
        pts.append((r, z, 0))
        # grooves and collar channels that fall between this sample and next
    # insert grooves and channels as notches (outward = +r)
    out = []
    for r, z, m in pts:
        out.append((r, z, m))
    for top, bot in groove_marks + channel_marks:
        depth = GROOVE_D if (top - bot) < 1.0 else 0.4
        if in_gallery(top) or in_gallery(bot):
            continue
        # find insertion index (pts are ordered by descending z on the wall)
        for i in range(len(out) - 1):
            z0, z1 = out[i][1], out[i + 1][1]
            if z0 >= top > z1 and out[i][2] == 0 and out[i + 1][2] == 0 \
                    and out[i + 1][1] <= bot:
                ra, rb = wall_r(top), wall_r(bot)
                mat = 1 if depth > 0.1 else 0
                out[i + 1:i + 1] = [(ra, top, 0), (ra + depth, top, mat),
                                    (rb + depth, bot, mat), (rb, bot, 0)]
                break
    pts = out

    # Undercroft: dome from the bore foot to the spring line, walls, terraces
    a, b = DOME_R - R(FOOT), DOME_SPRING - FOOT
    # the dome is a stack of shells: each band leans out of the one above
    # and ends in a downward-facing step, so from below it reads as layers
    per = 7
    for band in range(DOME_BANDS):
        for k in range(per + 1):
            s = k / per
            th = math.pi / 2 * (band + s) / DOME_BANDS
            if k == 0:
                continue
            x, z, nx, nz = dome_point(th, DOME_STEP * s)
            pts.append((x, z, 0))
        x, z, _, _ = dome_point(th, 0.0)
        pts.append((x, z, 1))                           # the step's riser
    t, b2, d, _ = UNDER_GALLERY
    wall = [(DOME_R, t, 0), (DOME_R + d, t, 1), (DOME_R + d, b2, 1),
            (DOME_R, b2, 0), (DOME_R, -219.0, 0), (DOME_R + 0.08, -219.0, 0),
            (DOME_R + 0.08, -219.1, 0), (DOME_R, -219.1, 0), (DOME_R, FLOOR, 0)]
    pts += wall
    prev_r = DOME_R
    z = FLOOR
    for r, zn in TERRACES:
        pts += [(r, z, 0), (r, zn, 0)]
        z = zn
    pts.append((0.0, z, 0))
    return pts


def dome_point(th, inset):
    """Point on the Undercroft dome ellipse, moved `inset` toward the air;
    returns x, z and the air-side normal."""
    a, b = DOME_R - R(FOOT), DOME_SPRING - FOOT
    x = R(FOOT) + a * math.sin(th)
    z = FOOT + b * (1 - math.cos(th))
    nx, nz = b * math.sin(th), -a * math.cos(th)
    ln = math.hypot(nx, nz)
    nx, nz = nx / ln, nz / ln
    return x + nx * inset, z + nz * inset, nx, nz


def in_lobe(ang_deg, top):
    """True for wall pieces that would hang in Landing A's open lobe."""
    d = abs((ang_deg - LOBE_ANG + 180.0) % 360.0 - 180.0)
    return top > LOBE_FLOOR and d < 38.0


def build_rock(coll):
    prof = interior_profile()
    closed = ([(170.0, 4.2, 2)] + prof +
              [(0.0, -250.0, 2), (170.0, -250.0, 2)])
    pr = [(r, z) for r, z, _ in closed] + [(closed[0][0], closed[0][1])]
    mats = [m for _, _, m in closed]
    p = Part(link_materials([CARVED, RECESS, ROCK]))
    revolve(p, pr, SEG, mats=mats, cap_bottom=False, cap_top=False)
    return p.finish("Mesh_GyreShaft_Rock", coll)


# ---------------------------------------------------------------------------
# Cutters (boolean voids)
# ---------------------------------------------------------------------------

def cyl_void(coll, name, a, b, radius, seg=32):
    """Cylinder from point a to point b."""
    a, b = Vector(a), Vector(b)
    p = Part(link_materials([CARVED]))
    d = b - a
    rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
    p.cyl(tuple((a + b) / 2), radius, d.length, axis='Z', seg=seg, rot=rot)
    return p.finish(name, coll)


def arch_mouth(coll, name, ang, z_floor, width, height, r_from, r_to):
    """A radial catenary-arch prism: the chamber's mouth onto the bore."""
    curve = catenary_curve(width, height, 24)
    a = math.radians(ang)
    out = Vector((math.cos(a), math.sin(a), 0.0))
    side = Vector((-math.sin(a), math.cos(a), 0.0))
    bm = bmesh.new()
    rings = []
    for rr in (r_from, r_to):
        rings.append([bm.verts.new(out * rr + side * p.x +
                                   Vector((0, 0, z_floor + p.y)))
                      for p in curve])
    n = len(curve)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((rings[0][i], rings[0][j], rings[1][j], rings[1][i]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    p = Part(link_materials([CARVED]))
    p._absorb(bm, 0)
    return p.finish(name, coll)


def dome_void(coll, name, ang, dist, rad, zf, h):
    prof = [(0.0, zf), (rad, zf), (rad, zf + h)]
    for k in range(1, 8):
        t = math.pi / 2 * k / 8
        prof.append((rad * math.cos(t), zf + h + rad * 0.6 * math.sin(t)))
    prof.append((0.0, zf + h + rad * 0.6))
    p = Part(link_materials([CARVED]))
    revolve(p, prof, 40, cap_bottom=False, cap_top=False)
    o = p.finish(name, coll)
    a = math.radians(ang)
    o.data.transform(Matrix.Translation((dist * math.cos(a), dist * math.sin(a), 0)))
    return o


def cutters():
    c = collection("Coll_Cutters")
    # Landing A: open lobe from the sky to -45 m
    p = Part(link_materials([CARVED]))
    revolve(p, [(0.0, -45.0), (15.0, -45.0), (15.0, 8.0), (0.0, 8.0)], 48,
            cap_bottom=False, cap_top=False)
    o = p.finish("Cut_LandingA", c)
    a = math.radians(210.0)
    o.data.transform(Matrix.Translation((31 * math.cos(a), 31 * math.sin(a), 0)))
    # chambers: domed rooms behind the wall, catenary mouths onto the bore
    for name, ang, zf, w, h, rad in (("B", 300.0, -84.0, 12.0, 15.0, 13.0),
                                     ("C", 30.0, -118.0, 13.0, 16.0, 15.0),
                                     ("D", 120.0, -156.0, 10.0, 12.0, 11.0)):
        dist = R(zf + 5) + rad + 3.0
        dome_void(c, "Cut_Chamber" + name, ang, dist, rad, zf, h * 0.7)
        arch_mouth(c, "Cut_Mouth" + name, ang, zf, w, h, R(zf + 5) - 4.0,
                   dist)
    # stepped blind oculi in the swell's lower wall, in families
    fams = [(40.0, -106.0, [(0.0, 0.0, 3.2), (6.8, 2.6, 1.4), (5.2, -3.8, 1.0)]),
            (165.0, -109.0, [(0.0, 0.0, 2.4), (-5.4, 3.0, 1.3)]),
            (255.0, -104.0, [(0.0, 0.0, 3.6), (-7.0, -1.5, 1.6), (6.4, 3.2, 1.1)])]
    for fi, (ang, zc, members) in enumerate(fams):
        a = math.radians(ang)
        out = Vector((math.cos(a), math.sin(a), 0.0))
        side = Vector((-math.sin(a), math.cos(a), 0.0))
        for k, (dx, dz, rad) in enumerate(members):
            z = zc + dz
            base = R(z)
            ctr = side * dx + Vector((0, 0, z))
            cyl_void(c, "Cut_Oculus_%d_%d_a" % (fi, k), ctr + out * (base - 2),
                     ctr + out * (base + 0.5), rad + 0.6, 28)
            cyl_void(c, "Cut_Oculus_%d_%d_b" % (fi, k), ctr + out * (base - 2),
                     ctr + out * (base + 4.5), rad, 28)
    # Undercroft portals: round tunnels through the wall
    for i, ang in enumerate((15.0, 105.0, 195.0, 285.0)):
        a = math.radians(ang)
        out = Vector((math.cos(a), math.sin(a), 0.0))
        z = -216.0
        cyl_void(c, "Cut_Portal_%d" % i, out * (DOME_R - 4) + Vector((0, 0, z)),
                 out * (DOME_R + 26) + Vector((0, 0, z)), 7.6, 40)
    return c


def carve(rock, cut_coll):
    for o in cut_coll.objects:
        o.data.transform(o.matrix_world)
        o.matrix_world = Matrix.Identity(4)
    mod = rock.modifiers.new("Carve", 'BOOLEAN')
    mod.operation = 'DIFFERENCE'
    mod.solver = 'EXACT'
    mod.operand_type = 'COLLECTION'
    mod.collection = cut_coll
    mod.material_mode = 'INDEX'
    dg = bpy.context.evaluated_depsgraph_get()
    mesh = bpy.data.meshes.new_from_object(rock.evaluated_get(dg))
    rock.modifiers.clear()
    old = rock.data
    rock.data = mesh
    bpy.data.meshes.remove(old)
    for o in list(cut_coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.collections.remove(cut_coll)
    bm = bmesh.new()
    bm.from_mesh(rock.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    for f in bm.faces:
        f.smooth = True
        if f.material_index > 2:
            f.material_index = 1
    flat_shade_steps(bm.faces, 32.0)
    bm.to_mesh(rock.data)
    bm.free()
    rock.data.materials.clear()
    for m in link_materials([CARVED, RECESS, ROCK]):
        rock.data.materials.append(m)


# ---------------------------------------------------------------------------
# Crater rock: natural, striated, rising away from the crown
# ---------------------------------------------------------------------------

def crater(coll):
    bm = bmesh.new()
    n_a, n_r = 128, 28
    rows = []
    random.seed(7)
    for j in range(n_r + 1):
        r = 41.0 + (190.0 - 41.0) * (j / n_r) ** 1.4
        row = []
        for i in range(n_a):
            a = 2 * math.pi * i / n_a
            x, y = r * math.cos(a), r * math.sin(a)
            n1 = noise.noise(Vector((x / 38.0, y / 38.0, 0.3)))
            n2 = noise.noise(Vector((x / 11.0, y / 11.0, 4.1)))
            rise = 3.6 + (r - 41.0) * 0.11 + 6.0 * ((r - 41.0) / 150.0) ** 2
            z = rise + n1 * 3.5 * min(1.0, (r - 41) / 25.0) + n2 * 0.9
            # strata: terrace the rock into ledges
            z = math.floor(z / 1.6) * 1.6 + 0.35 * (z / 1.6 - math.floor(z / 1.6))
            row.append(bm.verts.new((x, y, z)))
        rows.append(row)
    for j in range(n_r):
        for i in range(n_a):
            k = (i + 1) % n_a
            bm.faces.new((rows[j][i], rows[j][k], rows[j + 1][k], rows[j + 1][i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in bm.faces:
        f.smooth = False
        if f.normal.z < 0:
            f.normal_flip()
    p = Part(link_materials([ROCK]))
    p._absorb(bm, 0)
    return p.finish("Mesh_GyreShaft_CraterRock", coll)


# ---------------------------------------------------------------------------
# Repeated architecture: piers, fins, pipe bundles (linked duplicates)
# ---------------------------------------------------------------------------

def pier_mesh(name, coll, t, b, depth, n, rf=None):
    rf = rf or R
    """A gallery pier: narrow at mid-height, flaring into floor and ceiling,
    so the openings between piers read as lenses."""
    zc = (t + b) / 2
    r_line = rf(zc)
    spacing = 2 * math.pi * r_line / n
    rows = []
    bm = bmesh.new()
    k_n = 14
    for k in range(k_n + 1):
        s = k / k_n
        z = b + (t - b) * s
        w = 1.5 + (spacing - 2.6) * abs(2 * s - 1) ** 3.2
        r0 = rf(z) - 0.2
        r1 = rf(z) + depth * 0.45
        hw = w / 2
        rows.append([bm.verts.new((r0, -hw, z)), bm.verts.new((r1, -hw, z)),
                     bm.verts.new((r1, hw, z)), bm.verts.new((r0, hw, z))])
    for a, c in zip(rows, rows[1:]):
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((a[i], a[j], c[j], c[i]))
    bm.faces.new(rows[0])
    bm.faces.new(list(reversed(rows[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    p = Part(link_materials([CARVED]))
    p._absorb(bm, 0)
    for f in p.bm.faces:
        f.smooth = True
    flat_shade_steps(list(p.bm.faces), 35.0)
    return p.finish(name, coll)


def fin_mesh(name, coll, t, b):
    path = []
    n = 30
    for k in range(n + 1):
        z = t + (b - t) * k / n
        path.append((R(z) - 0.25, 0.0, z))

    def sec(s):
        e = min(1.0, min(s, 1 - s) / 0.12)          # fins emerge and sink
        proud = 0.4 + 1.6 * (0.5 - 0.5 * math.cos(math.pi * e))
        return [(u, -(v * (proud + 0.8) - (proud - 0.8) / 2))
                for u, v in reversed(chamfer_rect(1.2, 1.0, 0.22))]

    p = Part(link_materials([CARVED]))
    frame_sweep(p, path, sec, side=(0, 1, 0))
    return p.finish(name, coll)


def bundle_mesh(name, coll, t, b):
    """Three pipes in a bay, couplings every 4 m, elbows into the wall."""
    p = Part(link_materials([METAL, POLISH]))
    n = 36
    for off in (-0.85, 0.0, 0.85):
        path = []
        zt, zb = t - 0.6, b + 0.6
        path.append((R(zt) + 1.2, off, zt + 0.9))
        path.append((R(zt) + 0.2, off, zt + 0.5))
        for k in range(n + 1):
            z = zt + (zb - zt) * k / n
            path.append((R(z) - 0.42, off, z))
        path.append((R(zb) + 0.2, off, zb - 0.5))
        path.append((R(zb) + 1.2, off, zb - 0.9))
        frame_sweep(p, path, lambda s: circle_pts(0.3, 10), side=(0, 1, 0))
        z = zt - 2.0
        while z > zb + 1.0:
            cz = [(R(z) - 0.42, off, z + 0.2), (R(z - 0.4) - 0.42, off, z - 0.2)]
            fs = frame_sweep(p, cz, lambda s: circle_pts(0.4, 10),
                             side=(0, 1, 0))
            for f in fs:
                f.material_index = 1
            z -= 4.0
    # clamp bars across the bundle
    z = t - 4.0
    while z > b + 2.0:
        r = R(z) - 0.42
        p.box((r, 0.0, z), (0.5, 2.6, 0.35), mat=0)
        z -= 8.0
    return p.finish(name, coll)


def circle_pts(r, n):
    return [(r * math.cos(2 * math.pi * i / n), r * math.sin(2 * math.pi * i / n))
            for i in range(n)]


def ring_around(src, coll, n, a0=0.0, prefix=None, skip=()):
    out = []
    for i in range(n):
        if i in skip:
            continue
        ang = math.radians(a0 + 360.0 * i / n)
        o = instance(src, "%s_%02d" % (prefix or src.name, i), coll,
                     rot=(0.0, 0.0, ang))
        out.append(o)
    return out


# ---------------------------------------------------------------------------
# Metal: segmented bearing rings, hanging rings, chains, the machine
# ---------------------------------------------------------------------------

def band_segments(p, r_in, r_out, z0, z1, n_seg, gap_deg=0.5, steps=48,
                  local=None):
    """A ring band cut into plates with real gaps; chamfered edges polished.
    `local` maps (x, y, z) -> Vector for rings not lying flat."""
    c = min(0.12, (z1 - z0) * 0.08, (r_out - r_in) * 0.15)
    prof = [(r_in, z0), (r_out - c, z0), (r_out, z0 + c), (r_out, z1 - c),
            (r_out - c, z1), (r_in, z1)]
    pm = [0, 1, 0, 1, 0, 0]
    per = steps // n_seg or 1
    for s in range(n_seg):
        a0 = 2 * math.pi * s / n_seg + math.radians(gap_deg / 2)
        a1 = 2 * math.pi * (s + 1) / n_seg - math.radians(gap_deg / 2)
        bm = bmesh.new()
        rings = []
        for k in range(per + 1):
            a = a0 + (a1 - a0) * k / per
            ring = []
            for r, z in prof:
                co = Vector((r * math.cos(a), r * math.sin(a), z))
                ring.append(bm.verts.new(local(co) if local else co))
            rings.append(ring)
        m = len(prof)
        for ra, rb in zip(rings, rings[1:]):
            for i in range(m):
                j = (i + 1) % m
                f = bm.faces.new((ra[i], ra[j], rb[j], rb[i]))
                f.material_index = pm[i]
                f.smooth = True
        f = bm.faces.new(rings[0]); f.material_index = 1
        f = bm.faces.new(list(reversed(rings[-1]))); f.material_index = 1
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        mesh = bpy.data.meshes.new("_scratch")
        bm.to_mesh(mesh)
        bm.free()
        before = set(p.bm.faces)
        p.bm.from_mesh(mesh)
        bpy.data.meshes.remove(mesh)
        flat_shade_steps([f for f in p.bm.faces if f not in before], 40.0)


def collar_rings(coll):
    out = []
    for i, (c, half) in enumerate(COLLARS):
        p = Part(link_materials([METAL, POLISH]))
        r_face = wall_r(c)
        band_segments(p, r_face + 0.06, r_face + 0.39, c - 1.15, c + 1.15, 32)
        # an inner shoulder ring that stands proud, the bearing's race
        band_segments(p, r_face - 0.35, r_face + 0.1, c - 0.35, c + 0.35, 64,
                      gap_deg=0.3)
        out.append(p.finish("Mesh_GyreShaft_CollarRing_%d" % (i + 1), coll))
    return out


def chain(p, a, b, sag, link=1.05, mat=0):
    """Links along a hanging curve from a to b."""
    a, b = Vector(a), Vector(b)
    n = max(4, int((b - a).length / link * 1.06))
    pts = []
    for k in range(n + 1):
        t = k / n
        q = a.lerp(b, t)
        q.z -= sag * 4 * t * (1 - t)
        pts.append(q)
    for k in range(n):
        q0, q1 = pts[k], pts[k + 1]
        d = (q1 - q0)
        mid = (q0 + q1) / 2
        rot = d.to_track_quat('X', 'Z').to_matrix().to_4x4()
        if k % 2:
            rot = rot @ Matrix.Rotation(math.pi / 2, 4, 'X')
        bm = bmesh.new()
        res = bmesh.ops.create_circle(bm, cap_ends=False, radius=1.0, segments=8)
        # a stretched torus-ish link: two rings of 8 verts, scaled
        bm.free()
        faces = p.torus((0, 0, 0), 0.42, 0.13, axis='Z', maj_seg=10, min_seg=5,
                        mat=mat)
        vs = list({v for f in faces for v in f.verts})
        bmesh.ops.scale(p.bm, vec=(d.length * 0.68 / 0.42, 1.0, 1.0), verts=vs)
        bmesh.ops.transform(p.bm, matrix=Matrix.Translation(mid) @ rot, verts=vs)


def hanging_ring(coll, name, z, r_mid, width, height, tilt, n_hang, a0):
    p = Part(link_materials([METAL, POLISH]))
    band_segments(p, r_mid - width / 2, r_mid + width / 2, -height / 2,
                  height / 2, 24, gap_deg=0.6, steps=96)
    # inner race, polished
    band_segments(p, r_mid - width / 2 - 0.25, r_mid - width / 2 + 0.05,
                  -height * 0.18, height * 0.18, 48, gap_deg=0.2, steps=96)
    anchors = []
    for k in range(n_hang):
        a = math.radians(a0 + 360.0 * k / n_hang)
        out = Vector((math.cos(a), math.sin(a), 0.0))
        tang = Vector((-math.sin(a), math.cos(a), 0.0))
        base = out * r_mid + Vector((0, 0, height / 2))
        # sheave: a wheel on a bracket on top of the ring
        p.box(tuple(base + Vector((0, 0, 0.6))), (1.4, 0.9, 1.2), mat=0,
              rot=Matrix.Rotation(a, 4, 'Z'))
        rot = (tang.to_track_quat('Z', 'Y')).to_matrix().to_4x4()
        p.cyl(tuple(base + Vector((0, 0, 1.7))), 1.05, 0.5, axis='Z', seg=20,
              rot=rot, mat=1)
        p.cyl(tuple(base + Vector((0, 0, 1.7))), 0.35, 1.1, axis='Z', seg=10,
              rot=rot, mat=0)
        anchors.append((base + Vector((0, 0, 2.6)), a))
    o = p.finish("Mesh_GyreShaft_HangingRing_" + name, coll)
    o.location = (0, 0, z)
    o.rotation_euler = (math.radians(tilt), 0.0, 0.0)
    bpy.context.view_layer.update()
    # chains from each sheave up to an anchor plate in the wall
    cp = Part(link_materials([METAL]))
    mw = o.matrix_world
    for top_local, a in anchors:
        top = mw @ top_local
        za = top.z + 19.0
        wall = R(za) - 0.1
        anchor = Vector((wall * math.cos(a), wall * math.sin(a), za))
        chain(cp, top, anchor, sag=1.2)
        out = Vector((math.cos(a), math.sin(a), 0.0))
        rot = out.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        cp.cyl(tuple(anchor - out * 0.2), 1.4, 0.5, axis='Z', seg=20, rot=rot)
        cp.cyl(tuple(anchor - out * 0.6), 0.5, 0.9, axis='Z', seg=12, rot=rot)
    cp.finish("Mesh_GyreShaft_HangingRing_%s_Chains" % name, coll)
    return o


def machine(coll):
    specs = [  # r_mid, width, height, euler deg
        (21.6, 1.8, 3.0, (8, 0, 0)), (17.4, 1.5, 2.6, (68, 0, 20)),
        (13.6, 1.3, 2.2, (-35, 28, 0)), (10.2, 1.1, 1.9, (112, 8, 60)),
        (7.2, 0.9, 1.5, (28, -60, 0)), (4.6, 0.7, 1.1, (-70, 0, 40)),
    ]
    for i, (rm, w, h, eul) in enumerate(specs):
        p = Part(link_materials([METAL, POLISH]))
        band_segments(p, rm - w / 2, rm + w / 2, -h / 2, h / 2,
                      [24, 20, 16, 16, 12, 10][i], gap_deg=0.8, steps=72)
        band_segments(p, rm + w / 2 - 0.05, rm + w / 2 + 0.22, -h * 0.2,
                      h * 0.2, 48, gap_deg=0.2, steps=72)
        o = p.finish("Mesh_GyreShaft_MachineRing_%d" % (i + 1), coll)
        o.location = (0, 0, MACHINE_Z)
        o.rotation_euler = tuple(math.radians(v) for v in eul)
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
    o.location = (0, 0, MACHINE_Z)
    # pedestal grown from the basin, a bearing collar near the top
    p = Part(link_materials([CARVED, METAL, POLISH]))
    prof, mats = [], []
    z0, z1 = TERRACES[-1][1], MACHINE_Z - 4.5
    for k in range(17):
        t = k / 16.0
        z = z0 + (z1 - z0) * t
        r = 1.9 + 4.6 * math.exp(-t * 5.5) + 1.6 * math.exp(-(1 - t) * 7)
        prof.append((r, z))
    revolve(p, prof, 24)
    band_segments(p, 1.85, 2.55, z1 - 5.2, z1 - 3.4, 12, gap_deg=1.0, steps=48)
    p.finish("Mesh_GyreShaft_Pedestal", coll)


def bridge(coll, name, z, ang, offset, embed, width=4.2):
    r = R(z)
    half = math.sqrt(max(r * r - offset * offset, 1.0)) + embed
    a = math.radians(ang)
    d = Vector((math.cos(a), math.sin(a), 0.0))
    nrm = Vector((-d.y, d.x, 0.0))
    path = [tuple(nrm * offset + d * (-half + 2 * half * i / 24) +
                  Vector((0, 0, z))) for i in range(25)]

    def deck(t):
        return [(u, v - 0.6) for u, v in chamfer_rect(width, 1.2, 0.18)]

    def keel(t):
        depth = 0.8 + 3.4 * (2 * t - 1) ** 4
        return [(u, v - 1.2 - depth / 2)
                for u, v in chamfer_rect(width * 0.55, depth, 0.3)]

    p = Part(link_materials([CARVED, METAL, POLISH]))
    frame_sweep(p, path, deck, side=tuple(-nrm))
    frame_sweep(p, path, keel, side=tuple(-nrm))
    # a metal rail inlaid along each edge of the deck: a conduit, not a railing
    for sgn in (-1, 1):
        rail = [tuple(Vector(q) + nrm * sgn * (width / 2 - 0.35)) for q in path]
        fs = frame_sweep(p, rail, lambda t: [(u, v + 0.02) for u, v in
                                             chamfer_rect(0.36, 0.2, 0.05)],
                         side=tuple(-nrm))
        for f in fs:
            f.material_index = 1
    return p.finish("Mesh_GyreShaft_Bridge_" + name, coll)


# ---------------------------------------------------------------------------
# Undercroft furnishings
# ---------------------------------------------------------------------------

def vault_rib(coll):
    """One metal rib frame: down the dome and on down the wall to the floor."""
    path = []
    for k in range(1, 60):
        th = math.pi / 2 * k / 59
        x, z, _, _ = dome_point(th, 0.0)
        path.append((x, 0.0, z))
    for k in range(1, 9):
        path.append((DOME_R, 0.0, DOME_SPRING + (UNDER_GALLERY[0] - 0.2 - DOME_SPRING) * k / 8))
    # nrm of the frame is side x tangent; with side +Y it points into the air
    p = Part(link_materials([METAL, POLISH]))
    sd = (0, 1, 0)
    frame_sweep(p, path, lambda t: [(u, v + 1.3) for u, v in
                                    chamfer_rect(1.1, 3.4, 0.14)], side=sd)
    fs = frame_sweep(p, path, lambda t: [(u, v + 3.15) for u, v in
                                         chamfer_rect(2.4, 0.45, 0.1)],
                     side=sd)
    for f in fs:
        f.material_index = 0
    edge = frame_sweep(p, path, lambda t: [(u, v + 3.42) for u, v in
                                           chamfer_rect(1.6, 0.12, 0.03)],
                       side=sd)
    for f in edge:
        f.material_index = 1
    return p.finish("Mesh_GyreShaft_VaultRib", coll)


def portal_ring(coll):
    """A tech ring framing a portal tunnel, flared feet into the floor.
    Built facing +X at the wall line; instanced around the Undercroft."""
    p = Part(link_materials([METAL, POLISH, CARVED]))
    zc = -216.0

    def loc(co):
        # band authored about +Z; stand it up so its axis runs along +X
        return Vector((DOME_R - 1.6 - co.z, co.y, zc + co.x))

    band_segments(p, 8.0, 10.6, -1.5, 1.5, 18, gap_deg=0.6, steps=72, local=loc)
    band_segments(p, 7.5, 8.15, -0.6, 2.1, 36, gap_deg=0.3, steps=72, local=loc)
    # flared stone feet
    for s in (-1, 1):
        prof = [(0.0, 0.0), (5.2, 0.0), (1.4, 6.0), (0.0, 7.2)]
        bm = bmesh.new()
        lo = [bm.verts.new((DOME_R - 4.2, s * (7.2 + u), FLOOR + v)) for u, v in prof]
        hi = [bm.verts.new((DOME_R + 0.5, s * (7.2 + u), FLOOR + v)) for u, v in prof]
        bm.faces.new(lo)
        bm.faces.new(list(reversed(hi)))
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        p._absorb(bm, 2)
    return p.finish("Mesh_GyreShaft_PortalRing", coll)


def rods(coll):
    out = []
    p = Part(link_materials([METAL, POLISH, CARVED]))
    z = FLOOR + 1.1
    for i, ang in enumerate((15.0, 105.0, 195.0, 285.0, 60.0, 240.0)):
        a = math.radians(ang)
        d = Vector((math.cos(a), math.sin(a), 0.0))
        r0 = DOME_R - 6.0 if i < 4 else DOME_R - 0.5
        r1 = TERRACES[-1][0] + 1.5
        rot = d.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        mid = d * ((r0 + r1) / 2) + Vector((0, 0, z))
        p.cyl(tuple(mid), 0.65, r0 - r1, axis='Z', seg=16, rot=rot, mat=0)
        rr = r1 + 3.0
        while rr < r0 - 1.0:
            p.cyl(tuple(d * rr + Vector((0, 0, z))), 0.67, 0.3, axis='Z', seg=16,
                  rot=rot, mat=1)
            rr += 3.0
        # bearing block at the basin edge, saddles where it crosses terraces
        p.box(tuple(d * (r1 + 0.8) + Vector((0, 0, z))), (2.4, 2.4, 2.4), mat=0,
              rot=Matrix.Rotation(a, 4, 'Z'))
        p.cyl(tuple(d * (r1 + 0.8) + Vector((0, 0, z))), 1.3, 2.6, axis='Z',
              seg=20, rot=rot, mat=1)
        for rs, zt in ((TERRACES[0][0], FLOOR), (TERRACES[1][0], TERRACES[0][1])):
            h = (z - 0.65) - (zt - 3.0)
            p.box(tuple(d * (rs - 1.5) + Vector((0, 0, zt - 3.0 + h / 2))),
                  (2.0, 1.6, h), mat=2, rot=Matrix.Rotation(a, 4, 'Z'))
    return p.finish("Mesh_GyreShaft_Rods", coll)


def water(coll):
    bm = bmesh.new()
    ring = [bm.verts.new((29.9 * math.cos(2 * math.pi * i / 64),
                          29.9 * math.sin(2 * math.pi * i / 64), WATER_Z))
            for i in range(64)]
    bm.faces.new(ring)
    p = Part(link_materials([WATER]))
    p._absorb(bm, 0)
    return p.finish("Mesh_GyreShaft_Water", coll)


# ---------------------------------------------------------------------------

def main():
    start(OUT)
    c_rock = collection("Coll_GyreShaft_Rock")
    c_arch = collection("Coll_GyreShaft_Architecture")
    c_metal = collection("Coll_GyreShaft_Systems")
    c_mech = collection("Coll_GyreShaft_Mechanisms")
    c_under = collection("Coll_GyreShaft_Undercroft")

    rock = build_rock(c_rock)
    carve(rock, cutters())
    crater(c_rock)

    for gi, (t, b, d, n) in enumerate(GALLERIES):
        src = pier_mesh("Mesh_GyreShaft_Pier_G%d" % (gi + 1), c_arch, t, b, d, n)
        a0 = 180.0 / n
        skip = {i for i in range(n) if in_lobe(a0 + 360.0 * i / n, t)}
        keep = [i for i in range(n) if i not in skip]
        ring_around(src, c_arch, n, a0=a0, prefix=src.name,
                    skip=skip | {keep[0]})
        src.rotation_euler = (0, 0, math.radians(a0 + 360.0 * keep[0] / n))
    t, b, d, n = UNDER_GALLERY
    src = pier_mesh("Mesh_GyreShaft_Pier_Undercroft", c_under, t, b, d, n,
                    rf=lambda z: DOME_R)
    ring_around(src, c_under, n, a0=180.0 / n, prefix=src.name, skip=(0,))
    src.rotation_euler = (0, 0, math.radians(180.0 / n))

    for ti, (t, b, n, a0) in enumerate(FIN_TIERS):
        fin = fin_mesh("Mesh_GyreShaft_Fin_T%d" % (ti + 1), c_arch, t, b)
        skip = {i for i in range(n) if in_lobe(a0 + 360.0 * i / n, t)}
        keep = [i for i in range(n) if i not in skip]
        ring_around(fin, c_arch, n, a0=a0, prefix=fin.name, skip=skip | {keep[0]})
        fin.rotation_euler = (0, 0, math.radians(a0 + 360.0 * keep[0] / n))
        bun = bundle_mesh("Mesh_GyreShaft_Pipes_T%d" % (ti + 1), c_metal, t, b)
        bays = [k for k in range(0, n, 3)
                if not in_lobe(a0 + 360.0 / n * (k + 0.5), t)]
        for k in bays[1:]:
            instance(bun, "%s_%02d" % (bun.name, k), c_metal,
                     rot=(0, 0, math.radians(a0 + 360.0 / n * (k + 0.5))))
        bun.rotation_euler = (0, 0, math.radians(a0 + 360.0 / n * (bays[0] + 0.5)))

    collar_rings(c_metal)
    bridge(c_arch, "Pinch1", -55.0, 105.0, 7.0, 3.5)
    bridge(c_arch, "Swell", -98.0, 60.0, 17.0, 9.5, width=5.0)
    bridge(c_arch, "Pinch2", -138.0, 20.0, -6.0, 3.5)

    hanging_ring(c_mech, "Upper", -91.0, 22.0, 1.6, 2.8, 4.0, 4, 20.0)
    hanging_ring(c_mech, "Lower", -108.0, 17.5, 1.3, 2.4, -6.0, 3, 75.0)
    machine(c_mech)

    rib = vault_rib(c_under)
    ring_around(rib, c_under, 12, a0=0.0, prefix=rib.name, skip=(0,))
    port = portal_ring(c_under)
    for i, ang in enumerate((105.0, 195.0, 285.0)):
        instance(port, "%s_%d" % (port.name, i + 1), c_under,
                 rot=(0, 0, math.radians(ang)))
    port.rotation_euler = (0, 0, math.radians(15.0))
    rods(c_mech)
    water(c_under)

    report()
    gyre_save(OUT)


main()
