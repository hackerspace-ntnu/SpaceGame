"""Nets, ropes, pillars, lights, decorations, interior."""
from chparts import *

NR, PI_, LI, DE, IN = "Nets & Ropes", "Pillars", "Lights", "Decorations", "Interior"
WARM, TEAL, PINK = (1.0, 0.78, 0.45), (0.5, 1.0, 0.9), (1.0, 0.6, 0.8)


def knot(b, p, r=0.07, mat="Rope_Tan", seed=0):
    b.blob(p, r, mat, n=8, seed=seed)


# ================================================================ NETS
@asset("Net_Large", NR, ("net", "rope", "large", "span"), "Big sagging rope net (27 x 17 m real) to stretch between cliffs. Origin: centre at corner height; four anchor ropes with stakes.")
def _(a):
    W, D = 16.0, 10.0

    def P(u, v):
        z = -1.7 * sin(pi * u) * sin(pi * v) - 0.35 * (sin(pi * u) * (1 - sin(pi * v)) + sin(pi * v) * (1 - sin(pi * u)))
        pinch = 1 - 0.06 * sin(pi * v)
        pinch2 = 1 - 0.06 * sin(pi * u)
        return Vector((W * (u - 0.5) * pinch, D * (v - 0.5) * pinch2, z))
    b = B()
    net(b, P, 16, 10, r=0.03, br=0.065)
    a.part("Net", b)
    r, w = B(), B()
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = P(0 if sx < 0 else 1, 0 if sy < 0 else 1)
            e = c + Vector((sx * 1.6, sy * 1.6, 0.35))
            r.tube(sagpath(c, e, 0.1, 3), 0.06, "Rope_Tan", n=4)
            knot(r, c, 0.13, seed=1)
            w.tube([e + Vector((0, 0, -0.9)), e + Vector((sx * 0.05, sy * 0.05, 0.35))], [0.13, 0.1], "Wood_Dark", n=6)
            rope_wrap(r, (e.x, e.y, e.z - 0.05), 0.1, turns=3, r=0.03)
    a.part("Anchors", r)
    a.part("Stakes", w)


@asset("Net_Hammock", NR, ("net", "hammock", "small"), "Small hammock net with spreader bars and a cushion; hangs between two points 4.4 m (author) apart. Origin: midpoint between the hang points.")
def _(a):
    L, Wd = 2.6, 1.1

    def P(u, v):
        return Vector(((u - 0.5) * L, (v - 0.5) * Wd * (1 - 0.15 * sin(pi * u)), -0.75 - 0.55 * sin(pi * u) - 0.18 * sin(pi * v) * sin(pi * u)))
    b = B()
    net(b, P, 9, 5, r=0.016, br=0.03)
    w = B()
    for sx in (-1, 1):
        u = 0 if sx < 0 else 1
        w.tube([P(u, 0) + Vector((0, -0.08, 0)), P(u, 1) + Vector((0, 0.08, 0))], 0.035, "Wood_Honey", n=5)
        hp = Vector((sx * 2.2, 0, 0))
        for j in range(6):
            b.tube([P(u, j / 5), hp], 0.012, "Rope_Tan", n=3)
        knot(b, hp, 0.07, seed=j)
    a.part("Net", b)
    a.part("Bars", w)
    c = B()
    c.blob((0.75, 0, -1.2), (0.3, 0.38, 0.12), "Cloth_Coral", n=12, seed=2)
    a.part("Cushion", c)


@asset("Net_Climb", NR, ("net", "climb", "small", "vertical"), "Vertical climbing net hung from a log, 3 x 4.2 m (author). Origin: top centre.")
def _(a):
    def P(u, v):
        return Vector(((u - 0.5) * 3.0 * (1 - 0.05 * sin(pi * v)), -0.35 * sin(pi * v) * (0.6 + 0.4 * sin(pi * u)), -0.15 - 4.2 * v))
    b = B()
    net(b, P, 7, 10, r=0.02, br=0.035)
    for i in range(8):
        p = P(i / 7, 0)
        b.tube([p, (p.x, 0, 0.1)], 0.02, "Rope_Tan", n=3)
    a.part("Net", b)
    w = B()
    w.tube([(-1.9, 0, 0), (0, 0, 0.03), (1.9, 0, -0.02)], [0.1, 0.11, 0.09], "Wood_Rose", n=6)
    a.part("Log", w)


@asset("Net_Round_Lounge", NR, ("net", "round", "lounge", "hanging"), "Round lounging net in a wooden hoop, hung from one point by four ropes. Origin: hoop centre.")
def _(a):
    R = 2.3
    b = B()
    for i in range(12):
        ang = tau * i / 12
        b.tube([(cos(ang) * R * t, sin(ang) * R * t, -0.75 * (1 - t * t)) for t in (0.0, 0.25, 0.5, 0.75, 1.0)], 0.02, "Rope_Tan", n=3)
    for t in (0.25, 0.5, 0.75):
        b.tube([(cos(tau * i / 12) * R * t, sin(tau * i / 12) * R * t, -0.75 * (1 - t * t)) for i in range(12)], 0.02, "Rope_Tan", n=3, closed=True)
    a.part("Net", b)
    w = B()
    w.ring((0, 0, 0), R, 0.075, "Wood_Honey", n=14, m=5, wob=0.02, seed=3)
    a.part("Hoop", w)
    r = B()
    for i in range(4):
        ang = tau * i / 4 + 0.4
        r.tube([(cos(ang) * R, sin(ang) * R, 0.02), (0, 0, 3.4)], 0.03, "Rope_Tan", n=3)
        rope_wrap(r, (cos(ang) * R, sin(ang) * R, -0.05), 0.08, turns=2, r=0.025, n=6)
    knot(r, (0, 0, 3.4), 0.12)
    a.part("Ropes", r)
    c = B()
    c.blob((0.5, 0.3, -0.55), (0.35, 0.3, 0.12), "Cloth_Sky", n=12, seed=4)
    c.blob((-0.4, -0.5, -0.5), (0.3, 0.3, 0.12), "Cloth_Butter", n=12, seed=5)
    a.part("Cushions", c)


# ================================================================ ROPES
@asset("Rope_Hanging_Cluster", NR, ("rope", "hanging"), "Three ropes of different lengths dangling from one knot - one with a wooden charm, one with a loop. Origin: top knot.")
def _(a):
    b, w = B(), B()
    knot(b, (0, 0, 0), 0.12)
    specs = [(0.0, -6.0, 0.15), (2.1, -3.8, 0.1), (4.2, -2.3, 0.08)]
    for i, (ang, ln, sw) in enumerate(specs):
        ox, oy = cos(ang) * 0.06, sin(ang) * 0.06
        pts = [(ox + cos(ang) * sw * sin(t * 2.5) * t, oy + sin(ang) * sw * sin(t * 2.5) * t, ln * t) for t in (0, 0.25, 0.5, 0.75, 1.0)]
        b.tube(pts, 0.035, "Rope_Tan", n=4)
        knot(b, pts[2], 0.06, seed=i)
        end = Vector(pts[-1])
        if i == 0:
            w.lathe([(0.0, 0.05), (0.14, 0.0), (0.16, -0.2), (0.05, -0.32), (0.0, -0.33)], "Wood_Rose", n=6, c=end)
            for k in range(5):
                b.tube([end + Vector((cos(k * 1.3) * 0.04, sin(k * 1.3) * 0.04, -0.3)), end + Vector((cos(k * 1.3) * 0.1, sin(k * 1.3) * 0.1, -0.7))], 0.014, "Cloth_Coral", n=3)
        elif i == 1:
            b.ring(end + Vector((0, 0, -0.16)), 0.16, 0.03, "Rope_Tan", n=8, m=4, M=T(*(end + Vector((0, 0, -0.16)))) @ RX(pi / 2) @ T(*(-(end + Vector((0, 0, -0.16))))))
        else:
            knot(b, end, 0.08, seed=9)
    a.part("Ropes", b)
    a.part("Charm", w)


def span_rope(b, L, sag, r=0.04):
    pts = sagpath((0, 0, 0), (0, L, 0), sag, 12)
    b.tube(pts, r, "Rope_Tan", n=4)
    for p in (pts[0], pts[-1]):
        b.ring(p, 0.13, 0.035, "Rope_Tan", n=8, m=4, M=T(*p) @ RY(pi / 2) @ T(*(-p)))
        knot(b, p, 0.075)
    return pts


@asset("Rope_Span", NR, ("rope", "span"), "Thick sagging rope to string between houses, 10 m author / 16.7 m real. Origin: first end; other end at +Y.")
def _(a):
    b = B()
    pts = span_rope(b, 10.0, 1.3)
    b.tube([pts[4], pts[4] + Vector((0.02, 0.05, -1.4))], 0.025, "Rope_Tan", n=3)
    knot(b, pts[4] + Vector((0.02, 0.05, -1.4)), 0.06)
    a.part("Rope", b)


@asset("Rope_Span_Bunting", NR, ("rope", "span", "flags", "bunting"), "Sagging rope hung with pastel cloth flags, 8 m author. Origin: first end; other end at +Y.")
def _(a):
    b, f = B(), B()
    L = 8.0
    span_rope(b, L, 0.9, r=0.028)
    cols = ["Cloth_Coral", "Cloth_Butter", "Cloth_Sky", "Cloth_Mint", "Clay_Lavender"]
    rnd = random.Random(2)
    for i in range(13):
        t = (i + 0.8) / 14.6
        y = L * t
        z = -0.9 * 4 * t * (1 - t) - 0.02
        sw = rnd.uniform(-0.08, 0.08)
        h = rnd.uniform(0.42, 0.55)
        V = [(-0.012, y - 0.2, z), (-0.012, y + 0.2, z), (-0.012 + sw, y, z - h), (0.012, y - 0.2, z), (0.012, y + 0.2, z), (0.012 + sw, y, z - h)]
        f.add(V, [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], cols[i % 5])
    a.part("Rope", b)
    a.part("Flags", f)


@asset("Rope_Ladder", NR, ("rope", "ladder"), "Rope ladder with wooden rungs, 5 m author drop. Origin: top centre.")
def _(a):
    b, w = B(), B()
    for sx in (-0.28, 0.28):
        b.tube([(sx, 0, 0.1), (sx * 1.02, 0.03, -2.5), (sx, -0.02, -5.1)], 0.028, "Rope_Tan", n=4)
        knot(b, (sx, 0, 0.1), 0.07)
        knot(b, (sx, -0.02, -5.1), 0.05)
    rnd = random.Random(1)
    for i in range(12):
        z = -0.35 - i * 0.4
        w.tube([(-0.38, 0.0, z + rnd.uniform(-0.03, 0.03)), (0.38, 0.0, z + rnd.uniform(-0.03, 0.03))], 0.035, WOODS[i % 4], n=5)
    a.part("Ropes", b)
    a.part("Rungs", w)


# ================================================================ PILLARS  (origin = TOP, they hang down from what they carry)
def base_stones(b, z, seed):
    stone_ring(b, 0.35, 5, 0.26, z=z, seed=seed)


@asset("Pillar_Wood_Straight", PI_, ("pillar", "wood"), "Straight timber pillar with rope bindings and a forked head, 6 m author. Origin: top - drop it under a platform.")
def _(a):
    w, r, s = B(), B(), B()
    w.tube([(0, 0, 0), (0.02, 0.0, -3.0), (0, 0, -6.0)], [0.17, 0.19, 0.24], "Wood_Honey", n=7)
    for sx in (-1, 1):
        w.tube([(sx * 0.1, 0, -0.9), (sx * 0.55, 0, -0.02)], 0.08, "Wood_Dark", n=5)
    for z in (-1.0, -3.6):
        rope_wrap(r, (0, 0, z), 0.19, turns=4, r=0.028)
    base_stones(s, -6.0, 1)
    a.part("Post", w)
    a.part("Lashings", r)
    a.part("Stones", s)


@asset("Pillar_Wood_Bundle", PI_, ("pillar", "wood", "bundle"), "Three poles lashed together that splay at the top. Origin: top.")
def _(a):
    w, r, s = B(), B(), B()
    for i in range(3):
        ang = tau * i / 3 + 0.3
        w.tube([(cos(ang) * 0.5, sin(ang) * 0.5, 0.0), (cos(ang) * 0.14, sin(ang) * 0.14, -1.6), (cos(ang) * 0.13, sin(ang) * 0.13, -4.2),
                (cos(ang) * 0.45, sin(ang) * 0.45, -6.2)], [0.1, 0.11, 0.12, 0.14], ("Wood_Honey", "Wood_Rose", "Wood_Pale")[i], n=6)
    for z in (-1.7, -4.1):
        rope_wrap(r, (0, 0, z), 0.25, turns=5, r=0.03)
    base_stones(s, -6.2, 2)
    a.part("Poles", w)
    a.part("Lashings", r)
    a.part("Stones", s)


@asset("Pillar_Wood_Braced", PI_, ("pillar", "wood", "braced"), "Heavy post with four diagonal braces and a ring beam to carry a round platform, 7 m author. Origin: top.")
def _(a):
    w, r, s = B(), B(), B()
    w.tube([(0, 0, -0.1), (0, 0, -7.0)], [0.22, 0.3], "Wood_Rose", n=8)
    for i in range(4):
        ang = tau * i / 4 + pi / 4
        w.tube([(cos(ang) * 0.15, sin(ang) * 0.15, -2.2), (cos(ang) * 1.5, sin(ang) * 1.5, -0.12)], 0.08, "Wood_Honey", n=5)
    w.ring((0, 0, -0.1), 1.5, 0.09, "Wood_Dark", n=12, m=4)
    rope_wrap(r, (0, 0, -2.3), 0.24, turns=4, r=0.03)
    rope_wrap(r, (0, 0, -5.0), 0.27, turns=3, r=0.03)
    base_stones(s, -7.0, 3)
    a.part("Post", w)
    a.part("Lashings", r)
    a.part("Stones", s)


@asset("Pillar_Wood_Crooked", PI_, ("pillar", "wood", "organic"), "Bent, living trunk pillar with a sprouting branch. Origin: top.")
def _(a):
    w, l = B(), B()
    w.tube([(0, 0, 0), (0.18, 0.05, -1.4), (-0.12, 0.1, -3.0), (0.15, -0.05, -4.6), (0.0, 0, -6.2)], [0.16, 0.18, 0.2, 0.23, 0.32], "Bark_Warm", n=7)
    w.tube([(-0.1, 0.1, -3.0), (-0.7, 0.3, -2.5), (-1.0, 0.35, -1.9)], [0.09, 0.06, 0.03], "Bark_Warm", n=5)
    for i in range(4):
        ang = tau * i / 4 + 0.5
        w.tube([(cos(ang) * 0.15, sin(ang) * 0.15, -5.7), (cos(ang) * 0.6, sin(ang) * 0.6, -6.25)], [0.12, 0.05], "Bark_Warm", n=5)
    pad(l, (-1.05, 0.35, -1.8), 0.55, 0.6, ("Leaf_Shade", "Leaf_Green", "Leaf_Lime"), seed=1, lumps=4)
    a.part("Trunk", w)
    a.part("Leaves", l)


# ================================================================ LIGHTS
@asset("Lantern_Hanging", LI, ("light", "lantern", "hanging"), "Round paper lantern on a short rope with a tassel. Origin: top of the rope. Contains a point light.")
def _(a):
    g, w, r = B(), B(), B()
    top = lantern(g, w, (0, 0, -1.0), s=1.3, style=0)
    r.tube([(0, 0, 0), (0, 0, top - 0.02)], 0.018, "Rope_Tan", n=3)
    knot(r, (0, 0, 0), 0.05)
    for k in range(5):
        r.tube([(cos(k * 1.3) * 0.03, sin(k * 1.3) * 0.03, -1.3), (cos(k * 1.3) * 0.07, sin(k * 1.3) * 0.07, -1.62)], 0.012, "Cloth_Coral", n=3)
    a.part("Glow", g, shadow=False)
    a.part("Caps", w)
    a.part("Rope", r)
    a.light("Light", (0, 0, -1.0), WARM, 45)


@asset("Lantern_Post", LI, ("light", "lantern", "post"), "Crooked wooden post with a hanging gourd lantern. Origin: base.")
def _(a):
    g, w, r, s = B(), B(), B(), B()
    w.tube([(0, 0, -0.3), (0.05, 0, 1.2), (-0.05, 0, 2.3), (0.15, 0, 2.95), (0.7, 0, 3.15), (0.95, 0, 3.0)], [0.11, 0.09, 0.08, 0.07, 0.055, 0.04], "Wood_Dark", n=6)
    w.tube([(0.0, 0, 2.0), (0.45, 0, 2.95)], 0.035, "Wood_Dark", n=4)
    top = lantern(g, w, (0.9, 0, 2.45), s=1.25, style=2)
    r.tube([(0.9, 0, 3.02), (0.9, 0, top - 0.02)], 0.016, "Rope_Tan", n=3)
    rope_wrap(r, (0.02, 0, 1.0), 0.1, turns=3, r=0.02, n=8)
    stone_ring(s, 0.25, 4, 0.2, z=0.0, seed=4)
    a.part("Glow", g, shadow=False)
    a.part("Post", w)
    a.part("Rope", r)
    a.part("Stones", s)
    a.light("Light", (0.9, 0, 2.45), WARM, 60)


@asset("Lamp_Clay_Ground", LI, ("light", "lamp", "clay", "ground"), "Squat clay path lamp with a glowing crown. Origin: base.")
def _(a):
    c, g = B(), B()
    c.lathe([(0.2, 0.0), (0.3, 0.12), (0.26, 0.4), (0.16, 0.52), (0.19, 0.56)], "Clay_Lavender", n=8, wob=0.03, seed=1)
    c.lathe([(0.24, 0.92), (0.3, 0.9), (0.14, 1.06), (0.0, 1.18)], "Clay_Teal", n=8)
    for i in range(4):
        ang = tau * i / 4
        c.beam((cos(ang) * 0.15, sin(ang) * 0.15, 0.54), (cos(ang) * 0.2, sin(ang) * 0.2, 0.93), 0.04, "Clay_Lavender")
    g.lathe([(0.1, 0.55), (0.17, 0.62), (0.18, 0.85), (0.1, 0.92)], "Glow_Warm", n=8)
    a.part("Clay", c)
    a.part("Glow", g, shadow=False)
    a.light("Light", (0, 0, 0.74), WARM, 30)


@asset("Lantern_String", LI, ("light", "lantern", "string", "span"), "Rope strung with five mixed lanterns, 8 m author span. Origin: first end; other end at +Y.")
def _(a):
    b, w = B(), B()
    gs = {"Glow_Warm": B(), "Glow_Teal": B(), "Glow_Pink": B()}
    L, sag = 8.0, 1.0
    span_rope(b, L, sag, r=0.03)
    specs = [(0.14, 0, "Glow_Warm", 0.6), (0.32, 1, "Glow_Pink", 0.4), (0.5, 2, "Glow_Warm", 0.75), (0.68, 0, "Glow_Teal", 0.45), (0.86, 1, "Glow_Warm", 0.55)]
    for i, (t, st, gm, drop) in enumerate(specs):
        y = L * t
        z = -sag * 4 * t * (1 - t)
        top = lantern(gs[gm], w, (0, y, z - drop - 0.3), s=1.15, style=st, glow=gm, seed=i)
        b.tube([(0, y, z), (0, y, top - 0.02)], 0.014, "Rope_Tan", n=3)
        if i in (0, 2, 4):
            a.light("Light", (0, y, z - drop - 0.3), WARM, 35)
    a.part("Rope", b)
    a.part("Caps", w)
    for gm, gb in gs.items():
        a.part(gm, gb, shadow=False)
    a.light("Light_Teal", (0, L * 0.68, -1.5), TEAL, 25)


@asset("Glow_Mushrooms", LI, ("light", "mushroom", "magic", "plant"), "Cluster of softly glowing teal mushrooms for cliff faces and roots. Origin: base.")
def _(a):
    s, g = B(), B()
    rnd = random.Random(4)
    for i, (x, y, h, r) in enumerate([(0, 0, 0.75, 0.3), (0.32, 0.12, 0.45, 0.2), (-0.25, 0.2, 0.36, 0.17), (0.1, -0.3, 0.26, 0.14), (-0.3, -0.18, 0.18, 0.1), (0.42, -0.2, 0.15, 0.09)]):
        lx, ly = rnd.uniform(-0.08, 0.08), rnd.uniform(-0.08, 0.08)
        s.tube([(x, y, -0.05), (x + lx * 0.5, y + ly * 0.5, h * 0.5), (x + lx, y + ly, h)], [r * 0.3, r * 0.22, r * 0.2], "Flower_White", n=5)
        g.lathe([(r * 0.3, -0.03), (r, -0.02), (r * 0.85, r * 0.3), (r * 0.4, r * 0.62), (0.0, r * 0.72)], "Glow_Teal" if i % 3 else "Glow_Pink", n=7,
                M=T(x + lx, y + ly, h) @ RX(rnd.uniform(-0.25, 0.25)) @ RY(rnd.uniform(-0.25, 0.25)), wob=0.06, seed=i)
    a.part("Stems", s)
    a.part("Caps", g, shadow=False)
    a.light("Light", (0, 0, 0.5), TEAL, 20)


@asset("Fire_Bowl", LI, ("light", "fire", "brazier"), "Clay fire bowl on a wooden tripod with faceted flames. Origin: base.")
def _(a):
    c, w, g = B(), B(), B()
    c.lathe([(0.1, 0.78), (0.42, 0.95), (0.55, 1.2), (0.5, 1.24), (0.38, 1.02), (0.0, 0.95)], "Clay_Terracotta", n=9)
    c.ring((0, 0, 1.2), 0.54, 0.035, "Clay_Cream", n=9)
    for i in range(3):
        ang = tau * i / 3
        w.tube([(cos(ang) * 0.55, sin(ang) * 0.55, -0.1), (cos(ang) * 0.12, sin(ang) * 0.12, 0.95)], 0.045, "Wood_Dark", n=5)
    w.ring((0, 0, 0.45), 0.33, 0.025, "Rope_Tan", n=9)
    for i, (x, y, r, h) in enumerate([(0, 0, 0.2, 0.7), (0.16, 0.08, 0.13, 0.45), (-0.14, 0.1, 0.12, 0.4), (0.0, -0.17, 0.11, 0.35)]):
        g.lathe([(r * 0.6, 0.0), (r, h * 0.25), (r * 0.5, h * 0.65), (0.0, h)], "Glow_Warm" if i else "Glow_Pink", n=5, c=(x, y, 1.0), wob=0.15, seed=i)
    a.part("Bowl", c)
    a.part("Tripod", w)
    a.part("Flames", g, shadow=False)
    a.light("Light", (0, 0, 1.45), (1.0, 0.65, 0.35), 90)


# ================================================================ DECORATIONS
def pot(b, c, s, mat, shape=0, seed=0, rim="Clay_Cream"):
    profs = [
        [(0.16, 0), (0.3, 0.15), (0.32, 0.4), (0.2, 0.58), (0.23, 0.66), (0.17, 0.66), (0.14, 0.58), (0.0, 0.2)],
        [(0.2, 0), (0.24, 0.5), (0.3, 0.9), (0.2, 1.1), (0.24, 1.2), (0.17, 1.2), (0.0, 0.3)],
        [(0.22, 0), (0.42, 0.18), (0.44, 0.32), (0.36, 0.42), (0.3, 0.42), (0.0, 0.12)],
        [(0.12, 0), (0.22, 0.25), (0.12, 0.5), (0.1, 0.75), (0.16, 0.82), (0.1, 0.82), (0.0, 0.3)],
    ]
    p = profs[shape]
    mats = [mat] * (len(p) - 1)
    b.lathe([(r * s, z * s) for r, z in p], mats, n=8, c=c, wob=0.03, seed=seed)


@asset("Pots_Clay_Set", DE, ("pots", "clay", "decoration"), "Five clay pots and jars in pastel glazes, one planted. Origin: ground centre.")
def _(a):
    b, l = B(), B()
    pot(b, (0, 0, 0), 1.3, "Clay_Teal", 1, 1)
    pot(b, (0.65, 0.2, 0), 1.2, "Clay_Terracotta", 0, 2)
    pot(b, (-0.6, 0.15, 0), 1.1, "Clay_Pink", 2, 3)
    pot(b, (0.3, -0.55, 0), 1.0, "Clay_Butter", 3, 4)
    pot(b, (-0.35, -0.5, 0), 0.8, "Clay_Lavender", 0, 5)
    l.blob((-0.6, 0.15, 0.62), (0.4, 0.38, 0.28), "Leaf_Green", n=12, seed=6, mat2="Leaf_Lime")
    l.blob((-0.45, 0.3, 0.82), 0.16, "Flower_Red", n=8, seed=7)
    l.blob((-0.8, 0.05, 0.78), 0.13, "Flower_Yellow", n=8, seed=8)
    a.part("Pots", b)
    a.part("Plant", l)


@asset("Supplies_Stack", DE, ("barrel", "crate", "sack", "decoration"), "Barrel, two crates, a grain sack and a rope coil. Origin: ground centre.")
def _(a):
    w, r, c = B(), B(), B()
    w.lathe([(0.3, 0), (0.38, 0.3), (0.38, 0.6), (0.3, 0.9), (0.0, 0.86)], "Wood_Honey", n=9)
    for z in (0.18, 0.72):
        w.ring((0, 0, z), 0.355, 0.025, "Metal_Brass", n=9)
    w.box((0.85, 0.1, 0.3), (0.7, 0.65, 0.6), "Wood_Pale", rot=(0, 0, 0.3), taper=(0.96, 0.96))
    w.box((0.85, 0.1, 0.62), (0.76, 0.71, 0.06), "Wood_Dark", rot=(0, 0, 0.3))
    w.box((0.8, 0.15, 0.9), (0.5, 0.45, 0.45), "Wood_Rose", rot=(0, 0, -0.25), taper=(0.95, 0.95))
    c.blob((-0.7, -0.1, 0.32), (0.36, 0.3, 0.4), "Cloth_Butter", n=14, seed=1, zmin=0.0)
    c.blob((-0.72, -0.1, 0.76), 0.1, "Cloth_Butter", n=7, seed=2)
    r.ring((-0.7, -0.1, 0.66), 0.11, 0.02, "Rope_Tan", n=7)
    for k in range(4):
        r.ring((0.1, -0.75, 0.04 + k * 0.065), 0.3 - k * 0.012, 0.035, "Rope_Tan", n=10, m=4, wob=0.03, seed=k)
    a.part("Wood", w)
    a.part("Sack", c)
    a.part("Rope", r)


@asset("Wind_Chime", DE, ("chime", "hanging", "decoration"), "Hanging wind chime of clay tubes and a cloth sail. Origin: top.")
def _(a):
    w, r = B(), B()
    r.tube([(0, 0, 0), (0, 0, -0.4)], 0.014, "Rope_Tan", n=3)
    w.lathe([(0.0, -0.36), (0.26, -0.4), (0.28, -0.45), (0.0, -0.47)], "Wood_Honey", n=8)
    cols = ["Clay_Teal", "Clay_Pink", "Clay_Butter", "Clay_Lavender", "Clay_Terracotta"]
    for i in range(5):
        ang = tau * i / 5
        ln = 0.35 + (i * 3 % 5) * 0.09
        x, y = cos(ang) * 0.2, sin(ang) * 0.2
        r.tube([(x, y, -0.45), (x, y, -0.6)], 0.008, "Rope_Tan", n=3)
        w.lathe([(0.035, 0), (0.035, -ln)], cols[i], n=5, c=(x, y, -0.6))
    r.tube([(0, 0, -0.45), (0, 0, -1.25)], 0.008, "Rope_Tan", n=3)
    w.lathe([(0.0, 0.03), (0.07, 0.0), (0.0, -0.03)], "Wood_Dark", n=6, c=(0, 0, -0.9))
    w.add([(-0.01, -0.12, -1.25), (-0.01, 0.12, -1.25), (-0.01, 0, -1.6), (0.01, -0.12, -1.25), (0.01, 0.12, -1.25), (0.01, 0, -1.6)],
          [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], "Cloth_Coral")
    a.part("Chime", w)
    a.part("Strings", r)


@asset("Well_Round", DE, ("well", "stone", "straw", "decoration"), "Round stone well with a little thatched cap, bucket and rope. Origin: ground centre.")
def _(a):
    s, w, t, r = B(), B(), B(), B()
    s.lathe([(1.0, -0.1), (1.05, 0.4), (0.95, 0.85), (0.7, 0.85), (0.7, 0.2), (0.0, 0.2)],
            ["Stone_Cool", "Stone_Warm", "Stone_Warm", "Rock_Blue", "Rock_Slate"], n=11, wob=0.04, jit=0.015, seed=1)
    stone_ring(s, 1.1, 7, 0.2, seed=2)
    for sx in (-1, 1):
        w.tube([(sx * 0.9, 0, 0.5), (sx * 0.95, 0, 2.4)], 0.075, "Wood_Dark", n=6)
    w.tube([(-1.05, 0, 2.1), (1.2, 0, 2.1)], 0.055, "Wood_Honey", n=6)
    w.tube([(1.2, 0, 2.1), (1.2, 0, 1.85), (1.4, 0, 1.85)], 0.03, "Wood_Dark", n=4)
    t.lathe([(0.0, 2.45), (1.5, 2.25), (1.45, 2.45), (0.7, 2.95), (0.0, 3.35)], ["Straw_Dark", "Straw_Gold", "Straw_Gold", "Straw_Gold"], n=10, jit=0.04, seed=4)
    r.tube([(0.2, 0, 2.08), (0.22, 0, 1.2)], 0.018, "Rope_Tan", n=3)
    w.lathe([(0.15, 0.85), (0.2, 1.15), (0.17, 1.15), (0.13, 0.9)], "Wood_Rose", n=7, c=(0.22, 0, 0), loop=True)
    a.part("Stone", s)
    a.part("Frame", w)
    a.part("Thatch", t)
    a.part("Rope", r)


@asset("Market_Stall", DE, ("stall", "market", "cloth", "decoration"), "Round market table under a striped cloth parasol, with fruit baskets. Origin: ground centre.")
def _(a):
    w, c, f = B(), B(), B()
    w.lathe([(0.0, 0.85), (1.1, 0.85), (1.1, 0.93), (0.0, 0.93)], "Wood_Pale", n=12)
    for i in range(3):
        ang = tau * i / 3
        w.tube([(cos(ang) * 0.8, sin(ang) * 0.8, 0.0), (cos(ang) * 0.6, sin(ang) * 0.6, 0.86)], 0.05, "Wood_Dark", n=5)
    w.tube([(0, 0, 0.9), (0.03, 0, 2.75)], 0.045, "Wood_Dark", n=6)
    n = 10
    for i in range(n):
        a0, a1 = tau * i / n, tau * (i + 1) / n
        R, z0, zt = 1.75, 2.3, 2.8
        V = [(0, 0, zt), (cos(a0) * R, sin(a0) * R, z0), (cos(a1) * R, sin(a1) * R, z0),
             (0, 0, zt - 0.05), (cos(a0) * R, sin(a0) * R, z0 - 0.04), (cos(a1) * R, sin(a1) * R, z0 - 0.04),
             (cos((a0 + a1) / 2) * R * 0.98, sin((a0 + a1) / 2) * R * 0.98, z0 - 0.3)]
        c.add(V, [(0, 1, 2), (3, 5, 4), (0, 3, 4, 1), (2, 5, 3, 0), (1, 4, 6), (2, 6, 5), (1, 6, 2), (4, 5, 6)], "Cloth_Coral" if i % 2 else "Cloth_Butter")
    for (x, y, col, k) in ((0.45, 0.3, "Flower_Red", 0), (-0.5, 0.2, "Flower_Yellow", 1), (0.0, -0.55, "Flower_Purple", 2)):
        w.lathe([(0.2, 0.93), (0.3, 1.1), (0.26, 1.1), (0.0, 0.97)], "Straw_Dark", n=8, c=(x, y, 0))
        for j in range(5):
            f.blob((x + cos(j * 1.4) * 0.12, y + sin(j * 1.4) * 0.12, 1.12 + (j % 2) * 0.06), 0.085, col, n=7, seed=k * 9 + j)
    a.part("Table", w)
    a.part("Parasol", c)
    a.part("Fruit", f)


@asset("Pinwheel_Pole", DE, ("pinwheel", "wind", "cloth", "decoration"), "Tall pole topped with a cloth pinwheel - the wheel is its own object, origin on the hub, ready to spin. Origin: base.")
def _(a):
    w, c = B(), B()
    w.tube([(0, 0, -0.3), (0.04, 0, 2.0), (0, 0, 4.2)], [0.08, 0.065, 0.045], "Wood_Dark", n=6)
    w.tube([(0, 0.02, 4.0), (0, -0.25, 4.0)], 0.03, "Wood_Honey", n=5)
    a.part("Pole", w)
    cols = ["Cloth_Coral", "Cloth_Butter", "Cloth_Sky", "Cloth_Mint", "Clay_Lavender", "Cloth_Butter"]
    for i in range(6):
        ang = tau * i / 6
        ca, sa = cos(ang), sin(ang)
        cb, sb = cos(ang + 0.9), sin(ang + 0.9)
        V = [(0, -0.01, 0), (ca * 0.85, -0.01, sa * 0.85), (cb * 0.6, -0.14, sb * 0.6), (0, 0.02, 0), (ca * 0.85, 0.02, sa * 0.85), (cb * 0.6, -0.11, sb * 0.6)]
        c.add(V, [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], cols[i], M=T(0, -0.2, 4.0))
    c.blob((0, -0.26, 4.0), 0.07, "Metal_Brass", n=8, seed=1)
    a.part("Wheel", c, origin=(0, -0.2, 4.0))


# ================================================================ INTERIOR
@asset("Table_Round_Stools", IN, ("table", "stool", "furniture", "interior"), "Round table with a teapot and three mushroom stools. Origin: floor centre.")
def _(a):
    w, c = B(), B()
    w.lathe([(0.0, 0.7), (0.8, 0.7), (0.82, 0.76), (0.0, 0.78)], "Wood_Honey", n=12, wob=0.02)
    w.lathe([(0.32, 0.0), (0.12, 0.12), (0.1, 0.6), (0.3, 0.7)], "Wood_Dark", n=8)
    for i in range(3):
        ang = tau * i / 3 + 0.5
        x, y = cos(ang) * 1.25, sin(ang) * 1.25
        w.lathe([(0.16, 0.0), (0.1, 0.1), (0.1, 0.36)], "Wood_Dark", n=7, c=(x, y, 0))
        c.lathe([(0.12, 0.34), (0.3, 0.36), (0.26, 0.46), (0.0, 0.5)], ("Cloth_Coral", "Cloth_Sky", "Cloth_Butter")[i], n=8, c=(x, y, 0))
    pot(c, (0.1, 0.05, 0.78), 0.45, "Clay_Teal", 2, 3)
    c.tube([(0.28, 0.05, 0.9), (0.42, 0.05, 0.98)], 0.025, "Clay_Teal", n=5)
    for (x, y) in ((-0.35, 0.2), (0.3, -0.4)):
        c.lathe([(0.05, 0.78), (0.075, 0.88), (0.06, 0.88), (0.0, 0.8)], "Clay_Cream", n=7, c=(x, y, 0))
    a.part("Wood", w)
    a.part("Soft", c)


@asset("Bed_Nest", IN, ("bed", "furniture", "interior"), "Round woven nest bed with blanket and pillows. Origin: floor centre.")
def _(a):
    w, c = B(), B()
    w.lathe([(1.0, 0.0), (1.2, 0.2), (1.22, 0.55), (1.05, 0.6), (0.98, 0.35), (0.0, 0.3)], ["Straw_Dark", "Straw_Gold", "Straw_Gold", "Straw_Dark", "Straw_Dark"], n=12, wob=0.03, jit=0.02, seed=1)
    w.ring((0, 0, 0.58), 1.14, 0.07, "Rope_Tan", n=12, m=5)
    c.lathe([(0.0, 0.3), (0.98, 0.33), (0.9, 0.5), (0.0, 0.56)], "Plaster_Cream", n=12)
    c.blob((0.2, -0.25, 0.56), (0.75, 0.62, 0.12), "Cloth_Sky", n=14, seed=2, zmin=0.5)
    c.blob((-0.45, 0.5, 0.62), (0.3, 0.22, 0.1), "Cloth_Coral", n=10, seed=3)
    c.blob((-0.7, 0.1, 0.62), (0.22, 0.3, 0.1), "Cloth_Butter", n=10, seed=4)
    a.part("Nest", w)
    a.part("Bedding", c)


@asset("Shelf_Curved", IN, ("shelf", "furniture", "interior"), "Curved wall shelf that hugs the inside of a small round wall, stocked with jars and books. Origin: room centre at floor - rotate to place.")
def _(a):
    w, c = B(), B()
    R = 1.95
    a0, a1 = pi / 2 - 0.42, pi / 2 + 0.42
    for z in (0.5, 1.05, 1.6):
        V = []
        for k in range(6):
            ang = a0 + (a1 - a0) * k / 5
            for (rr, zz) in ((R, z), (R - 0.34, z), (R - 0.34, z + 0.05), (R, z + 0.05)):
                V.append((cos(ang) * rr, sin(ang) * rr, zz))
        F = []
        for k in range(5):
            for j in range(4):
                F.append((k * 4 + j, k * 4 + (j + 1) % 4, (k + 1) * 4 + (j + 1) % 4, (k + 1) * 4 + j))
        F += [(0, 1, 2, 3), (23, 22, 21, 20)]
        w.add(V, F, "Wood_Honey")
    for ang in (a0 + 0.02, a1 - 0.02):
        w.beam((cos(ang) * (R - 0.17), sin(ang) * (R - 0.17), 0.0), (cos(ang) * (R - 0.17), sin(ang) * (R - 0.17), 1.75), 0.09, "Wood_Dark")
    items = [(0.5, -0.3, 0), (0.5, 0.0, 3), (0.5, 0.28, 2), (1.05, -0.25, 3), (1.05, 0.2, 0), (1.6, -0.1, 2), (1.6, 0.25, 3)]
    cols = ["Clay_Teal", "Clay_Pink", "Clay_Butter", "Clay_Lavender", "Clay_Terracotta"]
    for i, (z, da, sh) in enumerate(items):
        ang = pi / 2 + da
        pot(c, (cos(ang) * (R - 0.17), sin(ang) * (R - 0.17), z + 0.05), 0.42, cols[i % 5], sh, i)
    for i in range(4):
        ang = pi / 2 + 0.02 + i * 0.045
        c.box((cos(ang) * (R - 0.17), sin(ang) * (R - 0.17), 1.05 + 0.05 + 0.14), (0.07, 0.22, 0.28 - (i % 2) * 0.04), ("Cloth_Coral", "Cloth_Sky", "Cloth_Butter", "Cloth_Mint")[i], rot=(0, 0, ang - pi / 2))
    a.part("Shelf", w)
    a.part("Items", c)


@asset("Rug_Round_Cushions", IN, ("rug", "cushion", "interior"), "Round woven rug in pastel rings with three floor cushions. Origin: floor centre.")
def _(a):
    r, c = B(), B()
    r.lathe([(1.5, 0.0), (1.5, 0.03), (1.15, 0.032), (0.8, 0.034), (0.4, 0.036), (0.0, 0.038)],
            ["Cloth_Butter", "Cloth_Coral", "Cloth_Mint", "Cloth_Sky", "Cloth_Butter"], n=14)
    for i in range(14):
        ang = tau * (i + 0.5) / 14
        r.tube([(cos(ang) * 1.48, sin(ang) * 1.48, 0.012), (cos(ang) * 1.68, sin(ang) * 1.68, 0.008)], 0.018, "Rope_Tan", n=3)
    for i, (x, y, m) in enumerate(((0.7, 0.5, "Cloth_Coral"), (-0.8, 0.3, "Clay_Lavender"), (0.1, -0.9, "Cloth_Sky"))):
        c.blob((x, y, 0.15), (0.34, 0.32, 0.13), m, n=12, seed=i)
    a.part("Rug", r)
    a.part("Cushions", c)


@asset("Stove_Clay", IN, ("stove", "fireplace", "clay", "interior", "light"), "Pot-bellied clay stove with a glowing mouth and a crooked flue. Origin: floor centre, mouth faces -Y.")
def _(a):
    c, g = B(), B()
    c.lathe([(0.45, 0.0), (0.6, 0.25), (0.62, 0.7), (0.42, 1.05), (0.2, 1.2), (0.0, 1.22)], "Clay_Cream", n=10, wob=0.02)
    c.lathe([(0.5, 0.0), (0.66, 0.0), (0.6, 0.12), (0.5, 0.12)], "Clay_Terracotta", n=10, loop=True)
    M = T(0, -0.56, 0.48) @ RX(-pi / 2)
    c.lathe([(0.2, -0.1), (0.32, -0.1), (0.32, 0.12), (0.2, 0.12)], "Clay_Terracotta", n=8, loop=True, M=M)
    g.lathe([(0.0, 0.0), (0.21, 0.0), (0.21, 0.05), (0.0, 0.05)], "Glow_Warm", n=8, M=M)
    c.tube([(0, 0, 1.15), (0, 0.05, 1.8), (0.0, 0.3, 2.3), (0, 0.4, 2.9)], [0.14, 0.12, 0.12, 0.12], "Clay_Terracotta", n=7)
    c.box((0.75, -0.2, 0.12), (0.3, 0.5, 0.2), "Wood_Dark", rot=(0, 0, 0.2))
    c.tube([(0.6, -0.4, 0.3), (0.9, 0.0, 0.3)], 0.07, "Wood_Honey", n=5)
    a.part("Clay", c)
    a.part("Glow", g, shadow=False)
    a.light("Light", (0, -0.75, 0.5), (1.0, 0.6, 0.3), 40)


@asset("Ladder_Wood", IN, ("ladder", "wood"), "Leaning wooden ladder lashed with rope, reaches 3 m author. Origin: foot centre; leans toward +Y.")
def _(a):
    w, r = B(), B()
    top = Vector((0, 0.9, 3.2))
    for sx in (-0.3, 0.3):
        w.tube([(sx * 1.15, 0, -0.05), (sx, top.y, top.z)], [0.05, 0.04], "Wood_Dark", n=5)
    for i in range(8):
        t = (i + 0.7) / 8.6
        p = Vector((0, top.y * t, top.z * t))
        hw = 0.3 * (1.15 - 0.15 * t) + 0.06
        w.tube([(-hw, p.y, p.z), (hw, p.y, p.z)], 0.03, WOODS[i % 4], n=5)
    a.part("Ladder", w)
