"""Platforms, sloped pathways, fences and the five bridges."""
from chparts import *

PP, BR = "Platforms & Paths", "Bridges"


def deck_frame(w, R, joists=3):
    w.lathe([(R - 0.12, -0.27), (R + 0.07, -0.27), (R + 0.07, -0.075), (R - 0.12, -0.075)], "Wood_Dark", n=18, loop=True)
    for i in range(joists):
        x = -R * 0.6 + (R * 1.2) * i / max(1, joists - 1)
        hl = sqrt(R * R - x * x) - 0.12
        w.beam((x, -hl, -0.17), (x, hl, -0.17), 0.15, "Wood_Dark")


@asset("Platform_Round_S", PP, ("platform", "round", "wood", "rope"), "Small round deck (r 2 m author) with rope railing, one opening at -Y, and a socket of braces underneath that sits on a pillar. Origin: deck centre.")
def _(a):
    R = 2.0
    d, w, bp, br = B(), B(), B(), B()
    planks_round(d, R, seed=1)
    deck_frame(w, R)
    for i in range(4):
        ang = tau * i / 4 + pi / 4
        w.tube([(cos(ang) * (R - 0.25), sin(ang) * (R - 0.25), -0.25), (cos(ang) * 0.12, sin(ang) * 0.12, -1.7)], 0.065, "Wood_Honey", n=5)
    w.tube([(0, 0, -0.25), (0, 0, -2.0)], [0.16, 0.19], "Wood_Rose", n=7)
    ring_fence(bp, br, R - 0.1, 10, gaps=[(-pi / 2, 0.45)], seed=2, a_off=pi / 10)
    a.part("Deck", d); a.part("Frame", w); a.part("Posts", bp); a.part("Ropes", br)


@asset("Platform_Round_M", PP, ("platform", "round", "wood", "rope"), "Medium round deck (r 3.3 m author) with rope railing and openings front and back - big enough for a small house. Origin: deck centre.")
def _(a):
    R = 3.3
    d, w, bp, br = B(), B(), B(), B()
    planks_round(d, R, seed=3)
    deck_frame(w, R, joists=4)
    for i in range(6):
        ang = tau * i / 6
        w.tube([(cos(ang) * (R - 0.2), sin(ang) * (R - 0.2), -0.28), (cos(ang) * (R - 1.5), sin(ang) * (R - 1.5), -1.3)], 0.07, "Wood_Honey", n=5)
    w.ring((0, 0, -1.3), R - 1.5, 0.08, "Wood_Rose", n=12, m=5)
    ring_fence(bp, br, R - 0.1, 16, gaps=[(-pi / 2, 0.3), (pi / 2, 0.3)], seed=4, a_off=pi / 16)
    a.part("Deck", d); a.part("Frame", w); a.part("Posts", bp); a.part("Ropes", br)


@asset("Platform_Round_Plaza", PP, ("platform", "round", "stone", "plaza"), "Large stone-rimmed plaza (r 4.8 m author) with a plank floor and a clay mosaic centre; stone bollards carry the rope. Three openings. Origin: deck centre.")
def _(a):
    R = 4.8
    d, s, m, bp, br = B(), B(), B(), B(), B()
    planks_round(d, R - 0.55, seed=5, pw=0.36)
    s.lathe([(R - 0.6, -0.6), (R + 0.1, -0.7), (R + 0.22, -0.2), (R + 0.12, 0.03), (R - 0.6, 0.03)], ["Stone_Cool", "Stone_Warm", "Stone_Warm", "Stone_Warm"], n=20, loop=True, wob=0.015, jit=0.008, seed=6)
    s.lathe([(R - 0.6, -0.6), (R - 0.6, -0.09), (0.0, -0.09)], "Stone_Cool", n=20)
    m.lathe([(1.5, 0.0), (1.5, 0.02), (1.05, 0.024), (0.6, 0.028), (0.0, 0.032)], ["Clay_Teal", "Clay_Cream", "Clay_Pink", "Clay_Butter"], n=16)
    gaps = [(-pi / 2, 0.24), (pi / 6, 0.24), (5 * pi / 6, 0.24)]
    ring_fence(bp, br, R - 0.2, 21, gaps=gaps, seed=7, h=0.95, a_off=pi / 21)
    for g, hw in gaps:
        for sg in (-1, 1):
            ang = g + sg * (hw + 0.07)
            s.lathe([(0.2, 0.0), (0.24, 0.5), (0.16, 0.62), (0.2, 0.74), (0.0, 0.9)], "Stone_Cool", n=7, c=(cos(ang) * (R - 0.2), sin(ang) * (R - 0.2), 0), wob=0.05, seed=int(ang * 9))
    a.part("Deck", d); a.part("Stone", s); a.part("Mosaic", m); a.part("Posts", bp); a.part("Ropes", br)


@asset("Platform_Round_Hanging", PP, ("platform", "round", "hanging", "rope"), "Round deck slung from four ropes that meet at a knot overhead - hang it from a branch or an overhang. Origin: deck centre; knot 4.5 m author above.")
def _(a):
    R = 2.3
    d, w, r = B(), B(), B()
    planks_round(d, R, seed=8)
    deck_frame(w, R, joists=2)
    w.ring((0, 0, 1.0), R + 0.02, 0.045, "Wood_Honey", n=16, m=5)
    for i in range(8):
        ang = tau * i / 8
        w.tube([(cos(ang) * R, sin(ang) * R, -0.1), (cos(ang) * (R + 0.02), sin(ang) * (R + 0.02), 1.02)], 0.04, "Wood_Dark", n=5)
    for i in range(4):
        ang = tau * i / 4 + pi / 4
        r.tube([(cos(ang) * R, sin(ang) * R, -0.2), (cos(ang) * R * 0.5, sin(ang) * R * 0.5, 2.3), (0, 0, 4.5)], 0.04, "Rope_Tan", n=4)
        rope_wrap(r, (cos(ang) * R, sin(ang) * R, -0.24), 0.1, turns=3, r=0.03, n=6)
    r.blob((0, 0, 4.5), 0.16, "Rope_Tan", n=10, seed=1)
    r.ring((0, 0, 4.75), 0.14, 0.035, "Metal_Brass", n=8, M=T(0, 0, 4.75) @ RX(pi / 2) @ T(0, 0, -4.75))
    a.part("Deck", d); a.part("Frame", w); a.part("Ropes", r)


@asset("Platform_Half_Cliff", PP, ("platform", "half", "balcony", "cliff"), "Half-round balcony that bolts onto a cliff face, propped by diagonal struts. Origin: middle of the straight back edge; the deck extends toward -Y.")
def _(a):
    R = 3.0
    d, w, bp, br = B(), B(), B(), B()
    planks_round(d, R, seed=9, ymax=0.0)
    w.tube([(cos(pi + pi * i / 12) * R, sin(pi + pi * i / 12) * R, -0.17) for i in range(13)], 0.1, "Wood_Dark", sq=True)
    w.beam((-R, 0.02, -0.17), (R, 0.02, -0.17), 0.2, "Wood_Dark")
    for x in (-2.0, -0.7, 0.7, 2.0):
        y = -sqrt(R * R - x * x) * 0.82
        w.tube([(x, y, -0.25), (x * 0.8, 0.15, -2.8)], 0.08, "Wood_Honey", n=5)
        w.beam((x, y, -0.2), (x, 0.0, -0.2), 0.12, "Wood_Rose")
    pts = [(cos(pi + pi * i / 8) * (R - 0.1), sin(pi + pi * i / 8) * (R - 0.1), 0) for i in range(9)]
    fence_run(bp, br, pts, seed=10)
    a.part("Deck", d); a.part("Frame", w); a.part("Posts", bp); a.part("Ropes", br)


# ---------------------------------------------------------------- sloped pathways
def ramp(a, P, N, width, posts_t, seed, plank=None, legs=None, stringer=True):
    path = Path(P)
    d, w, bp, br = B(), B(), B(), B()
    walkway(d, path, N, width, seed=seed, plank=plank)
    if stringer:
        for side in (-1, 1):
            w.tube([path.edge(i / 12, side * (width / 2 - 0.22), -0.14) for i in range(13)], 0.07, "Wood_Dark", sq=True)
    for side in (-1, 1):
        fence_run(bp, br, [path.edge(t, side * (width / 2 - 0.07), 0.0) for t in posts_t], seed=seed + side, last=False)
    if legs:
        for (t, side, drop) in legs:
            p = path.edge(t, side * (width / 2 - 0.15), -0.1)
            w.tube([p, p + Vector((0, 0, -drop))], [0.07, 0.09], "Wood_Honey", n=5)
    a.part("Planks", d); a.part("Frame", w); a.part("Posts", bp); a.part("Ropes", br)


@asset("Path_Ramp_Straight", PP, ("path", "ramp", "sloped", "wood", "rope"), "Straight plank ramp with rope fence. Rises 1.5 over 6 m author. Chains end to end: next piece at (0, 10, 2.5) real metres.")
def _(a):
    ramp(a, lambda t: Vector((0, 6 * t, 1.5 * t)), 18, 1.6, [0, 1 / 3, 2 / 3, 1], 1)


@asset("Path_Ramp_Curved", PP, ("path", "ramp", "sloped", "curved", "wood", "rope"), "Quarter-turn plank ramp (radius 5 m author) climbing 1.5 m; starts heading +Y, ends heading +X.")
def _(a):
    ramp(a, lambda t: Vector((5 - 5 * cos(t * pi / 2), 5 * sin(t * pi / 2), 1.5 * t)), 24, 1.6, [0, 1 / 3, 2 / 3, 1], 2, plank=0.26)


@asset("Path_Ramp_Spiral", PP, ("path", "ramp", "sloped", "spiral", "wood", "rope"), "Half-turn spiral ramp that winds up around a small round house or pillar, on legs. Climbs 2.7 m author. Origin: spiral axis at ground.")
def _(a):
    ramp(a, lambda t: Vector((3.75 * cos(-pi / 2 + pi * t), 3.75 * sin(-pi / 2 + pi * t), 2.7 * t)), 32, 1.3, [0, 0.25, 0.5, 0.75, 1], 3, plank=0.28,
         legs=[(0.3, 1, 1.2), (0.55, 1, 1.9), (0.8, 1, 2.6), (0.97, 1, 3.0), (0.6, -1, 2.0), (0.95, -1, 3.0)])


@asset("Path_Walkway_Flat", PP, ("path", "walkway", "wood", "rope"), "Level boardwalk segment with rope fence, 4 m author. Chains at (0, 6.67, 0) real metres.")
def _(a):
    ramp(a, lambda t: Vector((0, 4 * t, 0)), 12, 1.6, [0, 0.5, 1], 4)


@asset("Path_Stairs_Wood", PP, ("path", "stairs", "wood", "rope"), "Eight open timber steps with rope fence. Rises 1.6 over 2.56 m author.")
def _(a):
    d, w, bp, br = B(), B(), B(), B()
    for i in range(8):
        d.box((0, 0.32 * (i + 0.5), 0.2 * (i + 1) - 0.035), (1.5, 0.36, 0.07), WOODS[(i * 3) % 4], rot=(0, 0, 0.02 * ((i % 3) - 1)))
    for sx in (-0.62, 0.62):
        w.beam((sx, -0.1, -0.05), (sx, 2.66, 1.68), 0.1, "Wood_Dark", h=0.2)
        fence_run(bp, br, [(sx * 1.13, 0.1, 0.1), (sx * 1.13, 1.33, 0.87), (sx * 1.13, 2.5, 1.6)], seed=int(sx * 10))
    a.part("Steps", d); a.part("Frame", w); a.part("Posts", bp); a.part("Ropes", br)


@asset("Path_Stone_Tile", PP, ("path", "stone", "repeatable", "tile"), "Repeatable flagstone path tile, 1.7 x 2 m author. Tiles end to end along Y every 3.33 real metres.")
def _(a):
    b = B()
    lay = [(-0.5, 0.25, 0.36), (0.12, 0.2, 0.3), (0.62, 0.38, 0.3), (-0.22, 0.72, 0.3), (0.42, 0.9, 0.34), (-0.6, 1.1, 0.3),
           (-0.05, 1.32, 0.33), (0.6, 1.5, 0.3), (-0.5, 1.72, 0.3), (0.2, 1.84, 0.26)]
    for i, (x, y, r) in enumerate(lay):
        flagstone(b, (x, y, 0.0), r, seed=i + 3)
    a.part("Stones", b)


@asset("Fence_Rope_Segment", PP, ("fence", "rope", "wood"), "Chainable rope-and-post fence, 3 m author: two posts, ropes run on to where the next segment's post stands (+Y).")
def _(a):
    bp, br = B(), B()
    fence_run(bp, br, [(0, 0, 0), (0, 1.5, 0), (0, 3.0, 0)], seed=5, last=False)
    a.part("Posts", bp)
    a.part("Ropes", br)


# ================================================================ BRIDGES  (origin = first end at deck level, runs +Y)
def gate(w, r, y, hw, h=1.5, seed=0, bar=False):
    rnd = random.Random(seed)
    tops = []
    for sx in (-1, 1):
        top = Vector((sx * hw + rnd.uniform(-0.04, 0.04), y + rnd.uniform(-0.05, 0.05), h))
        w.tube([(sx * hw, y, -0.6), (sx * hw, y, h * 0.5), top], [0.13, 0.11, 0.09], "Wood_Dark", n=6)
        rope_wrap(r, (top.x, top.y, h - 0.25), 0.1, turns=3, r=0.03, n=6)
        tops.append(top)
    if bar:
        w.tube([tops[0] + Vector((-0.25, 0, -0.1)), tops[1] + Vector((0.25, 0, -0.1))], 0.07, "Wood_Honey", n=5)


@asset("Bridge_Rope_Plank", BR, ("bridge", "rope", "wood", "hanging"), "Classic sagging rope-and-plank suspension bridge, 12 m author / 20 m real. Origin: first end at deck level; other end at +Y, same height.")
def _(a):
    L, sag, wd = 12.0, 1.0, 1.4
    path = Path(lambda t: Vector((0, L * t, -sag * 4 * t * (1 - t))))
    d, r, w = B(), B(), B()
    walkway(d, path, 34, wd, seed=1)
    hand = lambda t, s: path.P(t) + Vector((s * 0.78, 0, 0.95 + 0.4 * (2 * t - 1) ** 2))
    for s in (-1, 1):
        r.tube([path.edge(i / 16, s * 0.52, -0.075) for i in range(17)], 0.04, "Rope_Tan", n=4)
        r.tube([hand(i / 16, s) for i in range(17)], 0.045, "Rope_Tan", n=4)
        r.tube([path.edge(i / 16, s * 0.68, 0.0).lerp(hand(i / 16, s), 0.5) for i in range(1, 16)], 0.025, "Rope_Tan", n=3)
        for k in range(1, 12):
            t = k / 12
            r.tube([path.edge(t, s * 0.56, -0.07), hand(t, s)], 0.018, "Rope_Tan", n=3)
    gate(w, r, 0.0, 0.78, seed=1)
    gate(w, r, L, 0.78, seed=2)
    a.part("Planks", d); a.part("Ropes", r); a.part("Posts", w)


@asset("Bridge_Rope_Net", BR, ("bridge", "rope", "net", "hanging", "long"), "Long, narrow V-shaped rope bridge with netted sides and crossed A-frame gates, 18 m author / 30 m real. Origin: first end at deck level; other end at +Y.")
def _(a):
    L, sag = 18.0, 1.9
    path = Path(lambda t: Vector((0, L * t, -sag * 4 * t * (1 - t))))
    d, r, w = B(), B(), B()
    walkway(d, path, 34, 0.8, seed=3, plank=0.24)
    hand = lambda t, s: path.P(t) + Vector((s * 0.68, 0, 1.0 + 0.35 * (2 * t - 1) ** 2))
    for x in (-0.3, 0.0, 0.3):
        r.tube([path.edge(i / 20, x, -0.08) for i in range(21)], 0.04, "Rope_Tan", n=4)
    for s in (-1, 1):
        r.tube([hand(i / 20, s) for i in range(21)], 0.045, "Rope_Tan", n=4)
        K = 40
        for ph in (0, 1):
            pts = []
            for k in range(K + 1):
                t = k / K
                pts.append(hand(t, s) if (k + ph) % 2 else path.edge(t, s * 0.36, -0.05))
            r.tube(pts, 0.016, "Rope_Tan", n=3)
    for y, sd in ((0.0, 4), (L, 5)):
        for s in (-1, 1):
            w.tube([(s * 1.0, y, -0.7), (-s * 0.28, y + 0.05 * s, 2.35)], [0.12, 0.08], "Wood_Dark" if s > 0 else "Wood_Rose", n=6)
        rope_wrap(r, (0, y, 1.5), 0.2, turns=4, r=0.03, n=8)
    a.part("Planks", d); a.part("Ropes", r); a.part("Posts", w)


@asset("Bridge_Straw", BR, ("bridge", "straw", "woven", "hanging"), "Bridge woven from fat straw bundles bound with rope, with straw-rope rails and tassels, 9 m author / 15 m real. Origin: first end at deck level; other end at +Y.")
def _(a):
    L, sag = 9.0, 0.45
    path = Path(lambda t: Vector((0, L * t, -sag * 4 * t * (1 - t))))
    s, r, tz = B(), B(), B()
    rnd = random.Random(7)
    for i, x in enumerate((-0.62, -0.31, 0.0, 0.31, 0.62)):
        s.tube([path.edge(k / 12, x, -0.17 + 0.02 * sin(k * 1.7 + i)) for k in range(-0, 13)], 0.185, "Straw_Gold" if i % 2 == 0 else "Straw_Dark", n=6, a0=i * 0.4)
    rail = lambda t, sd: path.P(t) + Vector((sd * 0.86, 0, 0.75 + 0.3 * (2 * t - 1) ** 2))
    for sd in (-1, 1):
        s.tube([rail(k / 12, sd) for k in range(13)], 0.1, "Straw_Dark", n=6)
        for k in range(7):
            t = k / 6
            base = path.edge(t, sd * 0.78, -0.1)
            s.tube([base, rail(t, sd) + Vector((0, 0, 0.12))], [0.085, 0.065], "Straw_Gold", n=5)
            rope_wrap(r, rail(t, sd) + Vector((0, 0, -0.14)), 0.085, turns=2, r=0.022, n=6)
        for k in range(18):
            t = (k + 0.5) / 18
            p = path.edge(t, sd * 0.8, -0.3)
            tz.lathe([(0.05, 0.02), (0.085, -0.2 - rnd.uniform(0, 0.12)), (0.0, -0.38 - rnd.uniform(0, 0.15))], "Straw_Dark" if k % 2 else "Straw_Gold", n=5, c=p)
    for k in range(10):
        t = k / 9
        p, rt, f, u = path.fr(t)
        loop = [p + rt * x + u * z for (x, z) in ((-0.84, -0.38), (0.84, -0.38), (0.84, 0.04), (-0.84, 0.04))]
        r.tube(loop, 0.03, "Rope_Tan", n=3, closed=True)
    for y in (-0.05, L + 0.05):
        s.tube([(-1.0, y, -0.15), (1.0, y, -0.15)], 0.25, "Straw_Dark", n=7)
    a.part("Straw", s); a.part("Bindings", r); a.part("Tassels", tz)


def strip(b, ys, zlo, zhi, x0, x1, mat):
    V, F = [], []
    for y in ys:
        yy = y[0] if isinstance(y, tuple) else y
        k = y[1] if isinstance(y, tuple) else 0
        V += [(x0, yy, zlo(yy, k)), (x1, yy, zlo(yy, k)), (x1, yy, zhi(yy, k)), (x0, yy, zhi(yy, k))]
    n = len(ys)
    for i in range(n - 1):
        for j in range(4):
            F.append((i * 4 + j, i * 4 + (j + 1) % 4, (i + 1) * 4 + (j + 1) % 4, (i + 1) * 4 + j))
    F += [(0, 1, 2, 3), tuple((n - 1) * 4 + j for j in (3, 2, 1, 0))]
    b.add(V, F, mat)


@asset("Bridge_Stone_Arch", BR, ("bridge", "stone", "arch", "solid", "constructed"), "Solid masonry arch bridge - built, not natural: voussoir ring with keystone, parapets, paved deck and capped corner piers. 10 m author / 16.7 m real. Origin: first end at deck level; other end at +Y.")
def _(a):
    L, wd, ab = 10.0, 2.8, 1.3
    zt = lambda y, k=0: 0.55 * sin(pi * min(1, max(0, y / L)))
    arch = lambda y: -3.0 + 2.75 * sqrt(max(0.0, 1 - ((y - L / 2) / (L / 2 - ab)) ** 2))

    def zb(y, k=0):
        return -3.3 if k else arch(y)
    ys = [(0.0, 1), (ab, 1)] + [ab + (L - 2 * ab) * i / 14 for i in range(15)] + [(L - ab, 1), (L, 1)]
    body, par, pave, trim = B(), B(), B(), B()
    strip(body, ys, zb, zt, -wd / 2, wd / 2, "Stone_Warm")
    for y0 in (ab / 2 - 0.05, L - ab / 2 + 0.05):
        body.box((0, y0, -2.0), (wd + 0.4, ab + 0.2, 2.8), "Rock_Cream", taper=(0.94, 0.92))
        body.box((0, y0, -3.5), (wd + 0.9, ab + 0.6, 0.5), "Stone_Cool", taper=(0.94, 0.94))
    yy = [L * i / 16 for i in range(17)]
    for s in (-1, 1):
        x0, x1 = sorted((s * (wd / 2 - 0.3), s * (wd / 2 + 0.08)))
        strip(par, yy, lambda y, k=0: zt(y) - 0.1, lambda y, k=0: zt(y) + 0.78, x0, x1, "Rock_Cream")
        x0, x1 = sorted((s * (wd / 2 - 0.36), s * (wd / 2 + 0.14)))
        strip(par, yy, lambda y, k=0: zt(y) + 0.78, lambda y, k=0: zt(y) + 0.9, x0, x1, "Clay_Terracotta")
        n = 13
        for k in range(n):
            y = ab + 0.05 + (L - 2 * ab - 0.1) * (k + 0.5) / n
            dz = (arch(y + 0.05) - arch(y - 0.05)) / 0.1
            ang = atan2(dz, 1.0)
            key = (k == n // 2)
            trim.box((s * (wd / 2 + 0.02), y, arch(y) + (0.3 if key else 0.24)), (0.16, 0.66 if not key else 0.6, 0.5 if not key else 0.72),
                     "Clay_Terracotta" if key else ("Stone_Cool" if k % 2 else "Rock_Peach"), rot=(ang, 0, 0))
        for y in (0.2, L - 0.2):
            trim.lathe([(0.36, -0.2), (0.33, 1.1), (0.42, 1.16), (0.42, 1.28), (0.3, 1.34)], "Stone_Warm", n=8, c=(s * (wd / 2 - 0.1), y, 0), wob=0.03, seed=int(y))
            trim.blob((s * (wd / 2 - 0.1), y, 1.52), 0.22, "Clay_Teal", n=10, seed=int(y) + 3)
    strip(pave, yy, lambda y, k=0: zt(y) - 0.04, lambda y, k=0: zt(y) + 0.045, -wd / 2 + 0.34, wd / 2 - 0.34, "Stone_Cool")
    rnd = random.Random(3)
    for i in range(16):
        y = 0.4 + i * 0.6
        flagstone(pave, (rnd.uniform(-0.6, 0.6), y, zt(y) + 0.04), 0.3, seed=i, th=0.03, mats=("Stone_Warm", "Rock_Peach", "Rock_Cream", "Stone_Warm"))
    a.part("Body", body); a.part("Parapets", par); a.part("Paving", pave); a.part("Trim", trim)


@asset("Bridge_Wood_Covered", BR, ("bridge", "wood", "covered", "straw", "solid"), "Rigid timber beam bridge under a thatched roof, with knee braces into the cliff at each end. 10 m author / 16.7 m real. Origin: first end at deck level; other end at +Y.")
def _(a):
    L, wd = 10.0, 1.9
    path = Path(lambda t: Vector((0, L * t, 0)))
    d, w, th, r = B(), B(), B(), B()
    walkway(d, path, 30, wd, seed=9)
    for x in (-0.62, 0.62):
        w.tube([(x, -0.4, -0.25), (x, L + 0.4, -0.25)], 0.17, "Wood_Rose", n=7)
        w.tube([(x, 1.5, -0.4), (x, -0.15, -2.0)], 0.09, "Wood_Dark", n=5)
        w.tube([(x, L - 1.5, -0.4), (x, L + 0.15, -2.0)], 0.09, "Wood_Dark", n=5)
    ypost = [0.3, 2.65, 5.0, 7.35, 9.7]
    for i, y in enumerate(ypost):
        for s in (-1, 1):
            w.tube([(s * 0.98, y, -0.4), (s * 0.98, y, 2.32)], [0.09, 0.075], "Wood_Dark", n=6)
            w.tube([(s * 0.98, y, 1.75), (s * 0.5, y, 2.3)], 0.045, "Wood_Honey", n=4)
            if i < len(ypost) - 1:
                y2 = ypost[i + 1]
                r.tube(sagpath((s * 0.98, y, 1.0), (s * 0.98, y2, 1.0), 0.14, 5), 0.026, "Rope_Tan", n=3)
                r.tube(sagpath((s * 0.98, y, 0.52), (s * 0.98, y2, 0.52), 0.16, 5), 0.022, "Rope_Tan", n=3)
        w.beam((-1.15, y, 2.3), (1.15, y, 2.3), 0.11, "Wood_Honey")
    for s in (-1, 1):
        w.beam((s * 0.98, 0.0, 2.38), (s * 0.98, L, 2.38), 0.1, "Wood_Dark")
        th.box((s * 0.78, L / 2, 2.78), (1.9, L + 0.9, 0.16), "Straw_Gold", rot=(0, -s * 0.6, 0))
        th.box((s * 0.42, L / 2, 3.1), (1.0, L + 0.5, 0.14), "Straw_Dark", rot=(0, -s * 0.6, 0))
    rnd = random.Random(5)
    for s in (-1, 1):
        for k in range(22):
            y = -0.3 + (L + 0.6) * (k + 0.5) / 22
            th.lathe([(0.1, 0.05), (0.12, -0.12), (0.0, -0.3 - rnd.uniform(0, 0.15))], "Straw_Dark" if k % 2 else "Straw_Gold", n=5, c=(s * 1.52, y, 2.27))
    w.tube([(0, -0.5, 3.42), (0, L + 0.5, 3.42)], 0.07, "Wood_Dark", n=5)
    a.part("Planks", d); a.part("Frame", w); a.part("Thatch", th); a.part("Ropes", r)
