"""cliffHouses - reusable geometry routines shared by many assets."""
from chlib import *

Z = Vector((0, 0, 1))
WOODS = ("Wood_Honey", "Wood_Honey", "Wood_Pale", "Wood_Rose")


class Path:
    def __init__(s, P):
        s.P = P
        pts = [P(i / 40) for i in range(41)]
        s.length = sum((pts[i + 1] - pts[i]).length for i in range(40))

    def fr(s, t, level=False):
        p = s.P(t)
        f = (s.P(min(1, t + 0.01)) - s.P(max(0, t - 0.01))).normalized()
        if level:
            f = Vector((f.x, f.y, 0)).normalized()
        r = f.cross(Z).normalized()
        u = r.cross(f)
        return p, r, f, u

    def edge(s, t, side, dz=0.0, level=False):
        p, r, f, u = s.fr(t, level)
        return p + r * side + u * dz


def walkway(bd, path, N, width, seed=0, mats=WOODS, gap=0.035, thick=0.06, t0=0.0, t1=1.0, plank=None):
    rnd = random.Random(seed)
    L = plank or (path.length * (t1 - t0) / N - gap)
    for i in range(N):
        t = t0 + (t1 - t0) * (i + 0.5) / N
        p, r, f, u = path.fr(t)
        M = frame(p, r, f, u) @ RZ(rnd.uniform(-0.035, 0.035))
        bd.box((rnd.uniform(-0.03, 0.03), 0, -thick / 2), (width * (1 + rnd.uniform(-0.05, 0.05)), L, thick),
               rnd.choice(mats), M=M)


def fence_run(bp, br, pts, h=1.0, seed=0, post_mat="Wood_Dark", rope_mat="Rope_Tan", first=True, last=True):
    rnd = random.Random(seed)
    tops = []
    for i, p in enumerate(pts):
        p = Vector(p)
        lean = Vector((rnd.uniform(-0.05, 0.05), rnd.uniform(-0.05, 0.05), 0))
        top = p + Vector((0, 0, h)) + lean
        if (first or i > 0) and (last or i < len(pts) - 1):
            bp.tube([p - Vector((0, 0, 0.22)), p + Vector((0, 0, h * 0.5)) + lean * 0.5, top + Vector((0, 0, 0.1))],
                    [0.07, 0.058, 0.046], post_mat, n=5)
            bp.blob(top + Vector((0, 0, 0.14)), 0.075, post_mat, n=8, seed=rnd.randint(0, 999))
        tops.append((p + lean * 0.5, top))
    for i in range(len(tops) - 1):
        (b0, t0), (b1, t1) = tops[i], tops[i + 1]
        d = (t1 - t0).length
        br.tube(sagpath(t0, t1, 0.07 * d, 5), 0.024, rope_mat, n=3)
        br.tube(sagpath(b0.lerp(t0, 0.5), b1.lerp(t1, 0.5), 0.1 * d, 5), 0.02, rope_mat, n=3)


def net(b, P, nu, nv, r=0.022, br=0.045, mat="Rope_Tan"):
    for i in range(nu + 1):
        b.tube([P(i / nu, j / nv) for j in range(nv + 1)], br if i in (0, nu) else r, mat, n=3)
    for j in range(nv + 1):
        b.tube([P(i / nu, j / nv) for i in range(nu + 1)], br if j in (0, nv) else r, mat, n=3)


def planks_round(bd, R, seed=0, pw=0.3, ymax=None, z=0.0, thick=0.07, mats=WOODS):
    """Parallel deck planks clipped to a circle (or a half circle when ymax is given)."""
    rnd = random.Random(seed)
    k = int(2 * R / pw)
    pw = 2 * R / k
    for i in range(k):
        x = -R + pw * (i + 0.5)
        hl = sqrt(max(0.01, R * R - x * x)) * (1 + rnd.uniform(-0.02, 0.012))
        y0, y1 = -hl, (hl if ymax is None else ymax)
        bd.box((x, (y0 + y1) / 2, z - thick / 2 + rnd.uniform(-0.008, 0.008)), (pw - 0.035, y1 - y0, thick),
               rnd.choice(mats), rot=(0, 0, rnd.uniform(-0.012, 0.012)))


def rope_wrap(b, p, R, turns=3, r=0.022, mat="Rope_Tan", n=10):
    for k in range(turns):
        b.ring((p[0], p[1], p[2] + k * r * 2.1), R + r * 0.6, r, mat, n=n, m=4)


def lantern(bg, bw, c, s=1.0, glow="Glow_Warm", cap="Wood_Dark", style=0, seed=0):
    """Paper / clay lantern centred at c (c = middle of the glowing body)."""
    x, y, z = c
    if style == 0:      # round paper lantern
        prof = [(0.07, -0.2), (0.16, -0.13), (0.2, 0.0), (0.16, 0.13), (0.07, 0.2)]
    elif style == 1:    # tall drum
        prof = [(0.1, -0.22), (0.14, -0.18), (0.15, 0.18), (0.1, 0.22)]
    else:               # gourd
        prof = [(0.05, -0.24), (0.17, -0.12), (0.13, 0.02), (0.1, 0.1), (0.06, 0.22)]
    bg.lathe([(r * s, zz * s) for r, zz in prof], glow, n=8, c=c, seed=seed)
    top = prof[-1][1] * s
    bot = prof[0][1] * s
    bw.lathe([(0.09 * s, 0), (0.1 * s, 0.03 * s), (0.04 * s, 0.07 * s)], cap, n=8, c=(x, y, z + top))
    bw.lathe([(0.05 * s, -0.05 * s), (0.09 * s, -0.01 * s), (0.08 * s, 0.01 * s)], cap, n=8, c=(x, y, z + bot))
    return z + top + 0.07 * s


def tuft(b, c, n=8, h=0.45, seed=0, mat="Grass", spread=0.12, r=0.035):
    rnd = random.Random(seed)
    for i in range(n):
        a = rnd.uniform(0, tau)
        d = rnd.uniform(0, spread)
        hh = h * rnd.uniform(0.6, 1.1)
        bx, by = c[0] + cos(a) * d, c[1] + sin(a) * d
        out = rnd.uniform(0.15, 0.5) * hh
        b.tube([(bx, by, c[2] - 0.03), (bx + cos(a) * out * 0.35, by + sin(a) * out * 0.35, c[2] + hh * 0.6),
                (bx + cos(a) * out, by + sin(a) * out, c[2] + hh)], [r, r * 0.7, 0.004], mat, n=3,
               a0=rnd.uniform(0, 2))


def flower(bs, bh, c, h=0.4, head="Flower_Yellow", seed=0, centre="Flower_White", size=0.07, bell=False):
    rnd = random.Random(seed)
    a = rnd.uniform(0, tau)
    lean = rnd.uniform(0.02, 0.1)
    top = Vector((c[0] + cos(a) * lean, c[1] + sin(a) * lean, c[2] + h))
    bs.tube([(c[0], c[1], c[2] - 0.03), (c[0] + cos(a) * lean * 0.3, c[1] + sin(a) * lean * 0.3, c[2] + h * 0.55), top],
            [0.014, 0.012, 0.01], "Leaf_Deep", n=3)
    # a leaf on the stem
    lp = Vector((c[0], c[1], c[2] + h * 0.35))
    bs.hull([lp, lp + Vector((cos(a + 2) * 0.1, sin(a + 2) * 0.1, 0.05)), lp + Vector((cos(a + 2.5) * 0.06, sin(a + 2.5) * 0.06, 0.09)),
             lp + Vector((cos(a + 1.5) * 0.06, sin(a + 1.5) * 0.06, 0.0)), lp + Vector((cos(a + 2) * 0.05, sin(a + 2) * 0.05, 0.07))], "Leaf_Green")
    M = T(*top) @ RZ(a) @ RY(rnd.uniform(0.1, 0.5))
    if bell:
        bh.lathe([(size * 0.25, 0.02), (size * 0.6, -0.02), (size * 0.8, -size * 1.4), (size * 0.3, -size * 0.9)], head, n=5, M=M)
    else:
        bh.lathe([(0.0, -0.01), (size, 0.012), (size * 0.75, 0.035), (0.0, 0.03)], head, n=6, M=M, wob=0.15, seed=seed)
        bh.blob((0, 0, 0.04), size * 0.36, centre, n=7, seed=seed, M=M)


def stone_ring(b, R, count, size, z=0.0, seed=0, mats=("Stone_Warm", "Stone_Cool", "Rock_Peach"), a0=0.0, a1=tau):
    rnd = random.Random(seed)
    for i in range(count):
        a = a0 + (a1 - a0) * (i + rnd.uniform(0.2, 0.8)) / count
        s = size * rnd.uniform(0.7, 1.25)
        b.blob((R * cos(a), R * sin(a), z + s * 0.25), (s, s * rnd.uniform(0.7, 1.0), s * 0.6), rnd.choice(mats), n=10,
               seed=rnd.randint(0, 9999), zmin=z - 0.05)


def flagstone(b, c, r, seed=0, mats=("Stone_Warm", "Stone_Cool", "Rock_Peach", "Stone_Warm"), th=0.08, sx=1.0, sy=1.0, rz=None):
    rnd = random.Random(seed)
    n = rnd.choice((5, 6, 7))
    M = T(*c) @ RZ(rnd.uniform(0, tau) if rz is None else rz) @ SC(sx, sy, 1)
    b.lathe([(r * 0.93, 0.0), (r, th * 0.6), (r * 0.9, th)], rnd.choice(mats), n=n, M=M, wob=0.14, seed=seed, zjit=0.0)


def ring_fence(bp, br, R, n, gaps=(), seed=0, h=1.0, a_off=0.0):
    """Posts + sagging ropes around a circle, leaving openings at gaps = [(angle, half_width_rad)]."""
    runs, cur = [], []
    for i in range(n + 1):
        ang = a_off + tau * i / n
        blocked = any(abs((ang - g + pi) % tau - pi) < hw for g, hw in gaps)
        if blocked:
            if len(cur) > 1:
                runs.append(cur)
            cur = []
        else:
            cur.append((cos(ang) * R, sin(ang) * R, 0.0))
    if len(cur) > 1:
        runs.append(cur)
    if not gaps and runs:
        fence_run(bp, br, runs[0], h=h, seed=seed, last=False)
        return
    for k, r in enumerate(runs):
        fence_run(bp, br, r, h=h, seed=seed + k)



def merge(dst, src, M=None):
    dst.add(src.v, [tuple(f) for f in src.f], [src.mats[i] for i in src.fm], M=M)


def leaf_shade(mats, seed=0):
    """Three-tone foliage: dark underside, mid sides, light sun-facing tops. mats = (dark, mid, light)."""
    rnd = random.Random(seed)

    def sh(nrm, ctr):
        if nrm.z < -0.25:
            return mats[0]
        k = nrm.z * 0.7 + (-0.55 * nrm.x - 0.75 * nrm.y) * 0.45 + rnd.uniform(-0.12, 0.12)
        if k > 0.42:
            return mats[2]
        return mats[1] if k > -0.25 else mats[0]
    return sh


def pad(l, c, R, zs, mats, seed=0, lumps=5, sx=1.0, rz=0.0, n=13):
    """Cloud-shaped leaf pad: a flat-bottomed cluster of lumps with a bumpy top, like the painted tree crowns."""
    rnd = random.Random(seed)
    c = Vector(c)
    M = T(*c) @ RZ(rz) @ SC(sx, 1, 1)
    zb = -R * zs * 0.3
    sh = leaf_shade(mats, seed)
    l.blob((0, 0, R * zs * 0.15), (R * 0.72, R * 0.68, R * zs), mats[1], n=n + 4, seed=rnd.randint(0, 99999), M=M, zmin=zb, shade=sh, irr=0.12)
    for i in range(lumps):
        a = tau * (i + rnd.uniform(-0.3, 0.3)) / lumps
        d = R * rnd.uniform(0.5, 0.68)
        r = R * rnd.uniform(0.36, 0.52)
        l.blob((cos(a) * d, sin(a) * d, r * zs * rnd.uniform(-0.1, 0.35)), (r, r * rnd.uniform(0.85, 1.0), r * zs * 1.25), mats[1], n=n,
               seed=rnd.randint(0, 99999), M=M, zmin=zb, shade=sh, irr=0.14)
    for i in range(max(1, lumps // 3)):
        a = rnd.uniform(0, tau)
        d = R * rnd.uniform(0.0, 0.35)
        r = R * rnd.uniform(0.3, 0.42)
        l.blob((cos(a) * d, sin(a) * d, R * zs * 0.75), (r, r, r * max(0.5, zs) * 1.1), mats[2], n=n - 2, seed=rnd.randint(0, 99999), M=M, shade=sh)


def limb(w, start, d, length, r0, depth, rnd, tips, bark, gnarl=0.25, up=0.15, spread=0.7, kids=(2, 2, 3), shrink=0.72, bias=None):
    """Recursive gnarled branch. Collects (tip position, direction, depth-0 radius) in tips."""
    from mathutils import Quaternion
    nseg = 4 if depth > 0 else 3
    p = Vector(start)
    dv = Vector(d).normalized()
    pts = [p.copy()]
    for s_ in range(nseg):
        dv = dv + Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * gnarl + Vector((0, 0, up))
        if bias is not None:
            dv += bias
        dv.normalize()
        p = p + dv * (length / nseg)
        pts.append(p.copy())
    r1 = r0 * 0.62
    rad = [r0 + (r1 - r0) * i / nseg for i in range(nseg + 1)]
    w.tube(pts, rad, bark, n=7 if r0 > 0.16 else (6 if r0 > 0.08 else 4))
    if depth == 0:
        tips.append((p, dv, r1))
        return
    k = rnd.choice(kids)
    base = dv.orthogonal().normalized()
    ph = rnd.uniform(0, tau)
    for j in range(k):
        ax = base.copy()
        ax.rotate(Quaternion(dv, ph + tau * j / k + rnd.uniform(-0.3, 0.3)))
        cd = dv.copy()
        cd.rotate(Quaternion(ax, spread * rnd.uniform(0.7, 1.25)))
        limb(w, p, cd, length * shrink * rnd.uniform(0.85, 1.1), r1 * rnd.uniform(0.82, 0.98), depth - 1, rnd, tips, bark, gnarl, up, spread, kids, shrink, bias)


def trunk(w, H, tr, lean, bark, rnd, n=8, segs=9, gnarl=0.1, roots=6, root_len=3.6):
    ph = rnd.uniform(0, 6)
    pts, rad = [], []
    for i in range(segs + 1):
        t = i / segs
        pts.append(Vector((lean[0] * t ** 1.6 + gnarl * H * sin(t * 5.5 + ph) * t * (1 - t * 0.4), lean[1] * t ** 1.6 + gnarl * H * cos(t * 4.3 + ph) * t * (1 - t * 0.4), H * t)))
        flare = 1.0 + 0.9 * max(0.0, 1 - t * 5) ** 2
        rad.append(tr * flare * (1 - 0.32 * t) * (1 + rnd.uniform(-0.06, 0.06)))
    w.tube(pts, rad, bark, n=n)
    for i in range(roots):
        a = tau * i / roots + ph + rnd.uniform(-0.25, 0.25)
        r = tr * rnd.uniform(root_len * 0.75, root_len * 1.2)
        w.tube([(cos(a) * tr * 0.7, sin(a) * tr * 0.7, H * 0.1 + tr), (cos(a) * r * 0.42, sin(a) * r * 0.42, tr * 0.55), (cos(a + 0.15) * r * 0.75, sin(a + 0.15) * r * 0.75, tr * 0.12),
                (cos(a + 0.3) * r, sin(a + 0.3) * r, -0.3)], [tr * 0.62, tr * 0.46, tr * 0.3, tr * 0.12], bark, n=5)
    for i in range(2):
        q = pts[2 + i * 2]
        a = rnd.uniform(0, tau)
        w.blob((q.x + cos(a) * tr * 0.85, q.y + sin(a) * tr * 0.85, q.z), tr * 0.42, bark, n=9, seed=rnd.randint(0, 9999))
    return pts, rad
