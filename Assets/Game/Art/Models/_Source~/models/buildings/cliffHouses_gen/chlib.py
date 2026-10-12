"""cliffHouses asset pack - shared helpers (mesh builder, materials, asset registry).

Authoring units are "human scale" metres (a 1.8 m person); everything is multiplied by S at
object creation so the pack matches the library's true scale (player = 3 m).
"""
import bpy, bmesh, math, random, uuid
from math import sin, cos, pi, tau, sqrt, asin, atan2
from mathutils import Vector, Matrix, Euler

S = 5.0 / 3.0          # 1.8 m author scale -> 3 m player
PREFIX = "CH_"

# name: (hex, roughness, emission strength)
PALETTE = {
    "Rock_Cream": ("E8D6AE", 0.95, 0), "Rock_Peach": ("DC9A78", 0.95, 0), "Rock_Rose": ("C67F6E", 0.95, 0),
    "Rock_Blue": ("7689A6", 0.95, 0), "Rock_Slate": ("56667F", 0.95, 0), "Rock_Lilac": ("8B7EA8", 0.95, 0),
    "Rock_Ochre": ("CDA467", 0.95, 0), "Rock_Grey": ("A19E96", 0.95, 0), "Rock_GreyLight": ("D3CDBD", 0.95, 0),
    "Rock_GreyDark": ("62666F", 0.95, 0),
    "Leaf_Shade": ("4E8A63", 0.9, 0), "Leaf_Olive": ("A9B354", 0.9, 0), "Leaf_Rust": ("D9793F", 0.9, 0),
    "Leaf_Rosewood": ("D97BA0", 0.9, 0), "Leaf_Pine": ("3F9A8C", 0.9, 0),
    "Bark_Pale": ("CFC6B8", 0.9, 0), "Bark_PaleShade": ("9E93A6", 0.9, 0), "Water": ("8FD3E6", 0.25, 0.25), "Cloth_Plum": ("9A84C8", 0.9, 0),
    "Glass_Clear": ("DDF5F0", 0.08, 0),
    "Clay_Rust": ("D98463", 0.7, 0), "Clay_TealDeep": ("62B2AA", 0.7, 0), "Clay_Rosy": ("E88FA3", 0.7, 0),
    "Stone_Warm": ("E9D9BC", 0.9, 0), "Stone_Cool": ("C6CCDD", 0.9, 0),
    "Wood_Honey": ("DDAE78", 0.8, 0), "Wood_Rose": ("CB9080", 0.8, 0), "Wood_Dark": ("8F6E60", 0.85, 0),
    "Wood_Pale": ("EED8AE", 0.8, 0), "Bark_Plum": ("917389", 0.9, 0), "Bark_Warm": ("AA826B", 0.9, 0),
    "Straw_Gold": ("F0D683", 0.95, 0), "Straw_Dark": ("D3AC62", 0.95, 0),
    "Clay_Terracotta": ("E79C7E", 0.7, 0), "Clay_Pink": ("F3AAB8", 0.7, 0), "Clay_Teal": ("82C9C1", 0.7, 0),
    "Clay_Lavender": ("B9A7E2", 0.7, 0), "Clay_Butter": ("F7DE90", 0.7, 0), "Clay_Cream": ("F8EDD8", 0.75, 0),
    "Plaster_Peach": ("FAD8C3", 0.9, 0), "Plaster_Mint": ("D2EDDC", 0.9, 0), "Plaster_Cream": ("FBF2E0", 0.9, 0),
    "Leaf_Green": ("9CCB6E", 0.9, 0), "Leaf_Lime": ("CEE17B", 0.9, 0), "Leaf_Yellow": ("F3DF6E", 0.9, 0),
    "Leaf_Pink": ("F5A9C2", 0.9, 0), "Leaf_Blush": ("FACFDA", 0.9, 0), "Leaf_Teal": ("70C3AA", 0.9, 0),
    "Leaf_Aqua": ("A6E0CF", 0.9, 0), "Leaf_Orange": ("F5A566", 0.9, 0), "Leaf_Deep": ("62A56D", 0.9, 0),
    "Grass": ("AAD874", 0.95, 0),
    "Flower_Red": ("F17570", 0.8, 0), "Flower_Yellow": ("FFD65A", 0.8, 0), "Flower_Purple": ("AB8FE2", 0.8, 0),
    "Flower_White": ("FFF7EA", 0.8, 0), "Flower_Blue": ("90C7F3", 0.8, 0),
    "Rope_Tan": ("DBBD8F", 0.95, 0), "Cloth_Coral": ("F39184", 0.9, 0), "Cloth_Sky": ("9DD2EE", 0.9, 0),
    "Cloth_Mint": ("B5E6C9", 0.9, 0), "Cloth_Butter": ("FBE7A3", 0.9, 0),
    "Metal_Brass": ("DDB962", 0.45, 0),
    "Glow_Warm": ("FFD07A", 0.5, 1.6), "Glow_Teal": ("7FEBD8", 0.5, 1.6), "Glow_Pink": ("FF9FC4", 0.5, 1.6),
    "Glass_Warm": ("FFE2A0", 0.3, 0.7),
}


def _lin(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def mat(name):
    full = "Mat_CH_" + name
    m = bpy.data.materials.get(full)
    if m:
        return m
    hx, rough, emi = PALETTE[name]
    col = (_lin(int(hx[0:2], 16)), _lin(int(hx[2:4], 16)), _lin(int(hx[4:6], 16)), 1.0)
    m = bpy.data.materials.new(full)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = col
    bsdf.inputs["Roughness"].default_value = rough
    if "Metal" in name:
        bsdf.inputs["Metallic"].default_value = 0.8
    if emi:
        bsdf.inputs["Emission Color"].default_value = col
        bsdf.inputs["Emission Strength"].default_value = emi
    if name == "Glass_Clear":
        bsdf.inputs["Alpha"].default_value = 0.28
        col = (col[0], col[1], col[2], 0.28)
        try:
            m.surface_render_method = 'BLENDED'
        except Exception:
            pass
    m.diffuse_color = col
    m.roughness = rough
    return m


def frame(p, right, fwd, up):
    M = Matrix.Identity(4)
    for i, ax in enumerate((right, fwd, up)):
        M[0][i], M[1][i], M[2][i] = ax[0], ax[1], ax[2]
    M[0][3], M[1][3], M[2][3] = p[0], p[1], p[2]
    return M


def T(x=0, y=0, z=0):
    return Matrix.Translation((x, y, z))


def RX(a): return Matrix.Rotation(a, 4, 'X')
def RY(a): return Matrix.Rotation(a, 4, 'Y')
def RZ(a): return Matrix.Rotation(a, 4, 'Z')
def SC(x, y, z): return Matrix.Diagonal((x, y, z, 1))


def sagpath(p0, p1, sag, seg=6):
    p0, p1 = Vector(p0), Vector(p1)
    out = []
    for i in range(seg + 1):
        t = i / seg
        p = p0.lerp(p1, t)
        p.z -= sag * 4 * t * (1 - t)
        out.append(p)
    return out


def fib(n, rnd=None, jit=0.0):
    out = []
    ga = pi * (3 - sqrt(5))
    for i in range(n):
        z = 1 - 2 * (i + 0.5) / n
        r = sqrt(max(0, 1 - z * z))
        a = i * ga
        d = Vector((r * cos(a), r * sin(a), z))
        if rnd and jit:
            d += Vector((rnd.uniform(-jit, jit), rnd.uniform(-jit, jit), rnd.uniform(-jit, jit)))
            d.normalize()
        out.append(d)
    return out


class B:
    """Accumulates verts / faces / per-face material names for one mesh object."""

    def __init__(s):
        s.v, s.f, s.fm, s.mats = [], [], [], []

    def _m(s, name):
        if name not in s.mats:
            s.mats.append(name)
        return s.mats.index(name)

    def add(s, verts, faces, mat, M=None):
        o = len(s.v)
        for p in verts:
            p = Vector(p)
            s.v.append(M @ p if M is not None else p)
        for i, f in enumerate(faces):
            s.f.append(tuple(o + k for k in f))
            s.fm.append(s._m(mat if isinstance(mat, str) else mat[i]))

    # -- revolve a (r, z) profile around Z ------------------------------------------------
    def lathe(s, prof, mat, n=12, c=(0, 0, 0), M=None, wob=0.0, jit=0.0, seed=0, a0=0.0, loop=False,
              zjit=None):
        rnd = random.Random(seed)
        wobs = [1 + wob * rnd.uniform(-1, 1) for _ in range(n)]
        V, rings = [], []
        for (r, z) in prof:
            if r < 1e-5:
                rings.append([len(V)])
                V.append((c[0], c[1], c[2] + z))
            else:
                ids = []
                for i in range(n):
                    a = a0 + tau * i / n
                    rr = r * wobs[i] * (1 + jit * rnd.uniform(-1, 1))
                    zz = z + (jit if zjit is None else zjit) * r * 0.35 * rnd.uniform(-1, 1)
                    ids.append(len(V))
                    V.append((c[0] + rr * cos(a), c[1] + rr * sin(a), c[2] + zz))
                rings.append(ids)
        F, FM = [], []
        bands = list(range(len(rings) - 1))
        pairs = [(k, k + 1) for k in bands]
        if loop:
            pairs.append((len(rings) - 1, 0))
        for bi, (k0, k1) in enumerate(pairs):
            A, C = rings[k0], rings[k1]
            if len(A) == 1 and len(C) == 1:
                continue
            m = mat if isinstance(mat, str) else mat[min(bi, len(mat) - 1)]
            for i in range(n):
                j = (i + 1) % n
                if len(A) == 1:
                    F.append((A[0], C[j], C[i]))
                elif len(C) == 1:
                    F.append((A[i], A[j], C[0]))
                else:
                    F.append((A[i], A[j], C[j], C[i]))
                FM.append(m)
        if not loop:
            if len(rings[0]) > 1:
                F.append(tuple(reversed(rings[0])))
                FM.append(mat if isinstance(mat, str) else mat[0])
            if len(rings[-1]) > 1:
                F.append(tuple(rings[-1]))
                FM.append(mat if isinstance(mat, str) else mat[-1])
        s.add(V, F, FM, M)

    def cyl(s, c, r, h, mat, n=10, r2=None, **kw):
        s.lathe([(r, 0), (r if r2 is None else r2, h)], mat, n=n, c=c, **kw)

    def box(s, c, size, mat, rot=(0, 0, 0), taper=(1, 1), M=None):
        sx, sy, sz = size[0] / 2, size[1] / 2, size[2] / 2
        tx, ty = taper
        V = [(-sx, -sy, -sz), (sx, -sy, -sz), (sx, sy, -sz), (-sx, sy, -sz),
             (-sx * tx, -sy * ty, sz), (sx * tx, -sy * ty, sz), (sx * tx, sy * ty, sz), (-sx * tx, sy * ty, sz)]
        F = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        X = Matrix.Translation(c) @ Euler(rot).to_matrix().to_4x4()
        s.add(V, F, mat, X if M is None else M @ X)

    # -- sweep a regular n-gon along a polyline -------------------------------------------
    def tube(s, path, r, mat, n=5, M=None, sq=False, asp=1.0, closed=False, a0=0.0):
        pts = [Vector(p) for p in path]
        m = len(pts)
        rad = list(r) if isinstance(r, (list, tuple)) else [r] * m
        if sq:
            n, a0 = 4, pi / 4
            rad = [x * sqrt(2) for x in rad]
        tans = []
        for i in range(m):
            if closed:
                t = pts[(i + 1) % m] - pts[i - 1]
            else:
                t = pts[min(i + 1, m - 1)] - pts[max(i - 1, 0)]
            tans.append(t.normalized())
        t0 = tans[0]
        nrm = Vector((0, 0, 1)).cross(t0)
        if nrm.length < 1e-3:
            nrm = Vector((1, 0, 0))
        nrm.normalize()
        V, rings = [], []
        prev = t0
        for i in range(m):
            t = tans[i]
            ax = prev.cross(t)
            if ax.length > 1e-6:
                ang = prev.angle(t)
                nrm = Matrix.Rotation(ang, 3, ax.normalized()) @ nrm
            nrm = (nrm - t * nrm.dot(t)).normalized()
            bn = t.cross(nrm)
            prev = t
            ids = []
            for k in range(n):
                a = a0 + tau * k / n
                ids.append(len(V))
                V.append(pts[i] + (nrm * cos(a) + bn * sin(a) * asp) * rad[i])
            rings.append(ids)
        F = []
        segs = m if closed else m - 1
        for i in range(segs):
            A, C = rings[i], rings[(i + 1) % m]
            for k in range(n):
                j = (k + 1) % n
                F.append((A[k], A[j], C[j], C[k]))
        if not closed:
            F.append(tuple(reversed(rings[0])))
            F.append(tuple(rings[-1]))
        s.add(V, F, mat, M)

    def beam(s, p0, p1, w, mat, h=None, M=None):
        s.tube([p0, p1], w / 2, mat, sq=True, asp=(h / w if h else 1.0), M=M)

    def ring(s, c, R, r, mat, n=14, m=4, M=None, wob=0.0, seed=0):
        """Torus-ish loop (rope binding, hoop) in the XY plane around c."""
        rnd = random.Random(seed)
        path = []
        for i in range(n):
            a = tau * i / n
            rr = R * (1 + wob * rnd.uniform(-1, 1))
            path.append((c[0] + rr * cos(a), c[1] + rr * sin(a), c[2]))
        s.tube(path, r, mat, n=m, closed=True, M=M)

    # -- convex hull based faceted shapes -------------------------------------------------
    def hull(s, pts, mat, M=None, shade=None):
        bm = bmesh.new()
        for p in pts:
            bm.verts.new(p)
        bmesh.ops.convex_hull(bm, input=bm.verts)
        for v in [v for v in bm.verts if not v.link_faces]:
            bm.verts.remove(v)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.verts.index_update()
        V = [v.co.copy() for v in bm.verts]
        F = [tuple(v.index for v in f.verts) for f in bm.faces]
        if shade:
            FM = [shade(f.normal, f.calc_center_median()) for f in bm.faces]
        else:
            FM = mat
        bm.free()
        s.add(V, F, FM, M)

    def blob(s, c, r, mat, n=16, seed=0, irr=0.16, M=None, mat2=None, zmin=None, shade=None):
        """Faceted leafy / pebbly lump. mat2 tints the upward faces."""
        rnd = random.Random(seed)
        rx, ry, rz = (r, r, r) if not isinstance(r, (tuple, list)) else r
        pts = []
        for d in fib(n, rnd, 0.25):
            k = 1 + irr * rnd.uniform(-1, 1)
            p = Vector((c[0] + d.x * rx * k, c[1] + d.y * ry * k, c[2] + d.z * rz * k))
            if zmin is not None and p.z < zmin:
                p.z = zmin
            pts.append(p)
        sh = shade
        if mat2 and not shade:
            sh = lambda nrm, ctr: mat2 if (nrm.z > 0.45 and nrm.x - nrm.y > -0.9) else mat
        s.hull(pts, mat, M=M, shade=sh)

    def rock(s, c, size, mats, n=28, seed=0, p=3.5, irr=0.1, ztop=None, zbot=None, lean=(0, 0), taper=0.0, M=None):
        """Boxy faceted hull rock. mats = (top, lit, lit2, shade, deep); faces are tinted by direction."""
        rnd = random.Random(seed)
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        pts = []
        for d in fib(n, rnd, 0.3):
            k = 1.0 / (abs(d.x) ** p + abs(d.y) ** p + abs(d.z) ** p) ** (1.0 / p)
            k *= 1 + irr * rnd.uniform(-1, 1)
            x, y, z = d.x * k * hx, d.y * k * hy, d.z * k * hz
            f = 1 + taper * (z / hz)
            x, y = x * f + lean[0] * z, y * f + lean[1] * z
            if ztop is not None:
                z = min(z, ztop)
            if zbot is not None:
                z = max(z, zbot)
            pts.append((c[0] + x, c[1] + y, c[2] + z))
        Mr = M.to_3x3() if M is not None else None
        tone = rnd.random()

        def shade(nrm, ctr):
            if Mr is not None:
                nrm = (Mr @ nrm).normalized()
            return rock_tone(nrm, mats, tone if rnd.random() < 0.7 else rnd.random())
        s.hull(pts, mats[1], M=M, shade=shade)

    def crag(s, c, size, mats, seed=0, na=34, nz=14, prof=None, p=3.0, col_amp=0.13, band_amp=0.05, top_jag=0.0,
             bot_jag=0.0, lean=(0, 0), noise=0.03, cap_in=0.55, M=None, moss=0.0, moss_mat="Leaf_Shade"):
        """Fractured cliff mass: vertical columns stepped in and out (dark crack faces between them), horizontal
        strata bands, jagged or flat top / bottom. Star-shaped around its axis so it never self-intersects."""
        rnd = random.Random(seed)
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        prof = prof or (lambda t: 0.9 + 0.1 * sin(pi * t))

        def groups(n, choices):
            g, gid = [], 0
            while len(g) < n:
                for _ in range(rnd.choice(choices)):
                    if len(g) < n:
                        g.append(gid)
                gid += 1
            return g, gid
        col, nc = groups(na, (2, 3, 3, 4, 5))
        band, nb = groups(nz + 1, (2, 3, 3, 4))
        cbase = [rnd.uniform(-col_amp, col_amp) * 0.5 for _ in range(nc)]
        cell = [[cbase[c_] + rnd.uniform(-col_amp, col_amp) * 0.8 for _ in range(nb)] for c_ in range(nc)]
        tcol = [rnd.random() for _ in range(nc)]
        tonec = [[min(1.0, max(0.0, tcol[c_] + rnd.uniform(-0.12, 0.12))) for _ in range(nb)] for c_ in range(nc)]
        ph1, ph2, ph3 = rnd.uniform(0, tau), rnd.uniform(0, tau), rnd.uniform(0, tau)
        ctop = [rnd.uniform(-1, 1) * top_jag for _ in range(nc)]
        cbot = [rnd.uniform(-1, 1) * bot_jag for _ in range(nc)]
        boff = [rnd.uniform(-band_amp, band_amp) for _ in range(nb)]
        ath = [tau * (i + rnd.uniform(-0.25, 0.25)) / na for i in range(na)]
        V, ring = [], []
        for k in range(nz + 1):
            t = k / nz
            ids = []
            for i in range(na):
                cs, sn = cos(ath[i]), sin(ath[i])
                r0 = 1.0 / ((abs(cs) / hx) ** p + (abs(sn) / hy) ** p) ** (1.0 / p)
                lob = 1 + 0.09 * sin(2 * ath[i] + ph1 + 1.6 * t) + 0.06 * sin(3 * ath[i] + ph2 - 2.2 * t) + 0.035 * sin(5 * ath[i] + ph3 + 3 * t)
                r = r0 * prof(t) * lob * (1 + cell[col[i]][band[k]]) * (1 + boff[band[k]]) * (1 + noise * rnd.uniform(-1, 1))
                z = -hz + 2 * hz * t
                if k == nz:
                    z += ctop[col[i]]
                elif k == nz - 1:
                    z += ctop[col[i]] * 0.5
                if k == 0:
                    z += cbot[col[i]]
                elif k == 1:
                    z += cbot[col[i]] * 0.5
                if 1 < k < nz - 1:
                    z += noise * hz * 0.25 * rnd.uniform(-1, 1)
                ids.append(len(V))
                V.append(Vector((cs * r + lean[0] * (z + hz), sn * r + lean[1] * (z + hz), z)))
            ring.append(ids)
        F, kind = [], []
        for k in range(nz):
            for i in range(na):
                j = (i + 1) % na
                F.append((ring[k][i], ring[k][j], ring[k + 1][j], ring[k + 1][i]))
                kind.append(("side", i, j, k))
        for (rg, zc, jag, tag) in ((ring[nz], hz, top_jag, "top"), (ring[0], -hz, bot_jag, "bot")):
            ax = Vector((lean[0] * (zc + hz), lean[1] * (zc + hz), 0))
            inner = []
            for i in range(na):
                v = V[rg[i]]
                q = ax + (Vector((v.x, v.y, 0)) - ax) * cap_in
                q.z = v.z + (rnd.uniform(0, abs(jag) * 0.6) * (1 if tag == "top" else -1)) + rnd.uniform(-0.04, 0.04)
                inner.append(len(V))
                V.append(q)
            zm = sum(V[i].z for i in inner) / na + (abs(jag) * 0.5 * (1 if tag == "top" else -1))
            ci = len(V)
            V.append(Vector((ax.x, ax.y, zm)))
            for i in range(na):
                j = (i + 1) % na
                F.append((rg[i], rg[j], inner[j], inner[i]))
                kind.append((tag, i, j, 0))
                F.append((inner[i], inner[j], ci))
                kind.append((tag, i, j, 0))
        Lg = Vector((-0.55, -0.75, 0.35)).normalized()
        Mr = M.to_3x3() if M is not None else None
        FM = []
        for f, (tag, i, j, k) in zip(F, kind):
            a_, b_, c_ = V[f[0]], V[f[1]], V[f[2]]
            d_ = V[f[3]] if len(f) == 4 else c_
            nrm = (c_ - a_).cross(d_ - b_) if len(f) == 4 else (b_ - a_).cross(c_ - a_)
            if nrm.length < 1e-9:
                nrm = Vector((0, 0, 1))
            nrm.normalize()
            ctr = (a_ + b_ + c_ + d_) / 4
            out = Vector((ctr.x - lean[0] * (ctr.z + hz), ctr.y - lean[1] * (ctr.z + hz), 0))
            if tag == "top":
                out = Vector((0, 0, 1))
            elif tag == "bot":
                out = Vector((0, 0, -1))
            if nrm.dot(out) < 0:
                nrm = -nrm
            if Mr is not None:
                nrm = (Mr @ nrm).normalized()
            if tag != "side":
                FM.append(mats[0] if tag == "top" else mats[4])
                continue
            bk = band[k]
            if col[i] != col[j] and abs(cell[col[i]][bk] - cell[col[j]][bk]) > 0.05 and abs(nrm.z) < 0.5:
                FM.append(mats[2] if nrm.dot(_LG) > -0.15 else mats[4])
                continue
            if nrm.z > 0.5 and moss and rnd.random() < moss:
                FM.append(moss_mat)
                continue
            FM.append(rock_tone(nrm, mats, tonec[col[i]][bk]))
        X = Matrix.Translation(c)
        s.add(V, F, FM, X if M is None else M @ X)
        return [Vector(c) + V[i] for i in ring[nz]]


_LG = Vector((-0.55, -0.75, 0.35)).normalized()


def rock_tone(nrm, mats, tone):
    """mats = (top, lit, lit2, shade, deep)."""
    if nrm.z > 0.6:
        return mats[0]
    if nrm.z < -0.45:
        return mats[4]
    if nrm.dot(_LG) > -0.1:
        return mats[2] if tone < 0.25 else mats[1]
    return mats[4] if tone < 0.25 else mats[3]


# ---------------------------------------------------------------------------------------------
# cylindrical wall with real openings
# ---------------------------------------------------------------------------------------------
def wall(b, rfun, h, mat_out, mat_in, n=20, thick=0.2, openings=(), zs_extra=(), band=None, z0=0.0,
         cap_mat=None):
    """Thick round wall. openings = [(angle, half_width_m, z_lo, z_hi)]. band = (z, material)."""
    def norm(a):
        return a % tau
    A = [tau * i / n for i in range(n)]
    for (ac, hw, zl, zh) in openings:
        da = asin(min(0.99, hw / rfun((zl + zh) / 2)))
        A = [a for a in A if abs((a - ac + pi) % tau - pi) > da + 0.07]
        A += [norm(ac - da), norm(ac + da)]
    A = sorted(set(round(a, 5) for a in A))
    zs = {z0, h}
    for (ac, hw, zl, zh) in openings:
        zs.update((zl, zh))
    zs.update(zs_extra)
    if band:
        zs.add(band[0])
    zs = sorted(z for z in zs if z0 <= z <= h)
    na, nz = len(A), len(zs)

    def is_open(i, k):
        a0, a1 = A[i], A[(i + 1) % na]
        if a1 < a0:
            a1 += tau
        am = (a0 + a1) / 2
        zm = (zs[k] + zs[k + 1]) / 2
        for (ac, hw, zl, zh) in openings:
            da = asin(min(0.99, hw / rfun((zl + zh) / 2)))
            if abs((am - ac + pi) % tau - pi) < da and zl < zm < zh:
                return True
        return False
    V = []
    out = [[0] * nz for _ in range(na)]
    inn = [[0] * nz for _ in range(na)]
    for i in range(na):
        for k in range(nz):
            r = rfun(zs[k])
            out[i][k] = len(V)
            V.append((r * cos(A[i]), r * sin(A[i]), zs[k]))
            inn[i][k] = len(V)
            V.append(((r - thick) * cos(A[i]), (r - thick) * sin(A[i]), zs[k]))
    F, FM = [], []
    cm = cap_mat or mat_out
    for i in range(na):
        j = (i + 1) % na
        for k in range(nz - 1):
            if is_open(i, k):
                continue
            zm = (zs[k] + zs[k + 1]) / 2
            mo = band[1] if (band and zm < band[0]) else mat_out
            F.append((out[i][k], out[j][k], out[j][k + 1], out[i][k + 1])); FM.append(mo)
            F.append((inn[j][k], inn[i][k], inn[i][k + 1], inn[j][k + 1])); FM.append(mat_in)
            if k == nz - 2 or is_open(i, k + 1):
                F.append((out[i][k + 1], out[j][k + 1], inn[j][k + 1], inn[i][k + 1])); FM.append(cm)
            if k == 0 or is_open(i, k - 1):
                F.append((out[j][k], out[i][k], inn[i][k], inn[j][k])); FM.append(cm)
            if is_open((i + 1) % na, k):
                F.append((out[j][k], inn[j][k], inn[j][k + 1], out[j][k + 1])); FM.append(cm)
            if is_open((i - 1) % na, k):
                F.append((inn[i][k], out[i][k], out[i][k + 1], inn[i][k + 1])); FM.append(cm)
    b.add(V, F, FM)


# ---------------------------------------------------------------------------------------------
# asset registry
# ---------------------------------------------------------------------------------------------
REG = {}
ORDER = []
CATS = {}


def cat_uuid(cat):
    return str(uuid.uuid5(uuid.NAMESPACE_DNS, "cliffhouses/" + cat))


def _top_coll():
    c = bpy.data.collections.get("CliffHouses")
    if not c:
        c = bpy.data.collections.new("CliffHouses")
        bpy.context.scene.collection.children.link(c)
    return c


class Asset:
    def __init__(s, name, cat, tags=(), desc=""):
        s.name, s.cat, s.tags, s.desc = name, cat, tags, desc
        s.coll = bpy.data.collections.new(PREFIX + name)
        if cat not in CATS:
            cc = bpy.data.collections.new("CH_Cat_" + cat)
            _top_coll().children.link(cc)
            CATS[cat] = cc
        CATS[cat].children.link(s.coll)
        s.root = bpy.data.objects.new("Root_" + name, None)
        s.root.empty_display_type = 'PLAIN_AXES'
        s.root.empty_display_size = 0.5 * S
        s.coll.objects.link(s.root)
        s.names = set()
        REG[name] = s
        ORDER.append(s)

    def _name(s, pname):
        base = f"{s.name}_{pname}"
        nm, i = base, 2
        while nm in s.names:
            nm = f"{base}_{i}"
            i += 1
        s.names.add(nm)
        return nm

    def part(s, pname, b, origin=None, rot=None, shadow=True, smooth=False):
        if not b.v:
            return None
        nm = s._name(pname)
        o = Vector(origin) if origin else Vector((0, 0, 0))
        me = bpy.data.meshes.new("Mesh_" + nm)
        me.from_pydata([((v - o) * S)[:] for v in b.v], [], b.f)
        for m in b.mats:
            me.materials.append(mat(m))
        me.polygons.foreach_set("material_index", b.fm)
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.dissolve_degenerate(bm, dist=1e-5, edges=bm.edges)
        for v in [v for v in bm.verts if not v.link_faces]:
            bm.verts.remove(v)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
        if smooth:
            me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
        me.update()
        ob = bpy.data.objects.new(nm, me)
        ob.location = o * S
        if rot:
            ob.rotation_euler = rot
        ob.parent = s.root
        if not shadow:
            ob.visible_shadow = False
        s.coll.objects.link(ob)
        return ob

    def light(s, pname, loc, color=(1.0, 0.78, 0.45), watts=60, radius=0.12):
        nm = s._name(pname)
        ld = bpy.data.lights.new("Light_" + nm, 'POINT')
        ld.color = color
        ld.energy = watts * S * S
        ld.shadow_soft_size = radius * S
        ob = bpy.data.objects.new(nm, ld)
        ob.location = Vector(loc) * S
        ob.parent = s.root
        s.coll.objects.link(ob)
        return ob

    def use(s, comp, loc=(0, 0, 0), rz=0.0, rot=None, only=None, skip=()):
        """Place another asset's parts here as linked duplicates (shared mesh data)."""
        src = REG[comp]
        M = Matrix.Translation(Vector(loc) * S) @ (Euler(rot).to_matrix().to_4x4() if rot else RZ(rz))
        made = []
        for ob in src.coll.objects:
            if ob is src.root:
                continue
            pn = ob.name[len(src.name) + 1:]
            if only and not any(pn.startswith(k) for k in only):
                continue
            if any(pn.startswith(k) for k in skip):
                continue
            nm = s._name(f"{comp}_{pn}")
            new = bpy.data.objects.new(nm, ob.data)
            new.parent = s.root
            new.matrix_basis = M @ ob.matrix_basis
            new.visible_shadow = ob.visible_shadow
            s.coll.objects.link(new)
            made.append(new)
        return made


BUILDERS = []


def asset(name, cat, tags=(), desc=""):
    def deco(fn):
        BUILDERS.append((name, cat, tags, desc, fn))
        return fn
    return deco


def build_all(only=None):
    for (name, cat, tags, desc, fn) in BUILDERS:
        if only and name not in only:
            continue
        a = Asset(name, cat, tags, desc)
        fn(a)
    bpy.context.view_layer.update()


def bbox(a):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for ob in a.coll.objects:
        if ob.type != 'MESH':
            continue
        mw = ob.matrix_world
        for c in ob.bound_box:
            p = mw @ Vector(c)
            for i in range(3):
                lo[i] = min(lo[i], p[i])
                hi[i] = max(hi[i], p[i])
    return lo, hi
