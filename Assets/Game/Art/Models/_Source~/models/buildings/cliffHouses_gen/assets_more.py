"""Second wave of building blocks: giant-tree kit, trunk-wrapping walkways, extra roofs / walls / railings,
town infrastructure, jungle plants, more interior and lights."""
from chparts import *
from assets_kit import RS, RL, HS, HL, courses, ring_frame
from assets_props import pot, knot, span_rope, WARM, TEAL

GT, K, PP, DE, PL, IN, LI, NR = "Giant Tree", "Building Kit", "Platforms & Paths", "Decorations", "Plants", "Interior", "Lights", "Nets & Ropes"
RG = 3.0                      # giant trunk radius (author metres) that all trunk-wrapping pieces fit
GREEN3 = ("Leaf_Shade", "Leaf_Green", "Leaf_Lime")
JUNGLE3 = ("Leaf_Pine", "Leaf_Deep", "Leaf_Green")
STICKS = ("Wood_Pale", "Wood_Honey", "Straw_Dark")


# ---------------------------------------------------------------- shared routines
def leaf(b, base, tip, width, mat, droop=0.3, seg=4, lift=0.0):
    """Flat pointed leaf / frond blade as a closed diamond-section strip."""
    base, tip = Vector(base), Vector(tip)
    L = (tip - base).length
    pts, rad = [], []
    for i in range(seg + 1):
        t = i / seg
        p = base.lerp(tip, t)
        p.z += lift * L * sin(pi * t) - droop * L * t * t
        pts.append(p)
        rad.append(max(0.012, width * (0.25 + 0.75 * sin(pi * min(1.0, t * 1.5 + 0.12))) * (1 - t ** 3)))
    b.tube(pts, rad, mat, n=4, asp=0.1)
    return pts


def frond(s, l, base, d, length, mats, rnd, pairs=6, droop=0.45, w=0.5):
    """Palm frond: drooping spine with paired leaflets."""
    base = Vector(base)
    d = Vector(d).normalized()
    pts = []
    for i in range(6):
        t = i / 5
        p = base + d * length * t
        p.z += 0.25 * length * sin(pi * t * 0.6) - droop * length * t * t
        pts.append(p)
    s.tube(pts, [0.05, 0.045, 0.04, 0.03, 0.02, 0.01], "Leaf_Shade", n=4)
    side = Vector((-d.y, d.x, 0)).normalized()
    for i in range(pairs):
        t = (i + 0.8) / (pairs + 0.6)
        k = min(4, int(t * 5))
        p = pts[k].lerp(pts[k + 1], t * 5 - k)
        ww = w * sin(pi * (0.15 + 0.8 * t)) * length * 0.3
        for sg in (-1, 1):
            tip = p + side * sg * ww + d * ww * 0.5 + Vector((0, 0, -ww * 0.45))
            leaf(l, p, tip, ww * 0.28, mats[(i + (sg > 0)) % len(mats)], droop=0.0, seg=2)


def stick_rail(b, pts, h=1.0, seed=0, first=True, last=True, balusters=3):
    """Bamboo / stick railing: posts, a top and a low rail, and thin sticks between them (some crossed)."""
    rnd = random.Random(seed)
    pts = [Vector(p) for p in pts]
    tops, lows = [], []
    for i, p in enumerate(pts):
        lean = Vector((rnd.uniform(-0.03, 0.03), rnd.uniform(-0.03, 0.03), 0))
        tp = p + Vector((0, 0, h)) + lean
        tops.append(tp)
        lows.append(p + Vector((0, 0, h * 0.22)))
        if (first or i > 0) and (last or i < len(pts) - 1):
            b.tube([p - Vector((0, 0, 0.2)), p + Vector((0, 0, h * 0.5)), tp + Vector((0, 0, 0.16))], [0.06, 0.055, 0.05], "Wood_Honey", n=6)
            b.ring((p.x, p.y, p.z + h * 0.62), 0.06, 0.015, "Wood_Dark", n=6, m=3)
    b.tube([t + Vector((0, 0, -0.03)) for t in tops], 0.04, "Wood_Pale", n=5)
    b.tube(lows, 0.03, "Wood_Pale", n=4)
    for i in range(len(pts) - 1):
        for k in range(balusters):
            t = (k + 1) / (balusters + 1)
            a_ = lows[i].lerp(lows[i + 1], t)
            c_ = tops[i].lerp(tops[i + 1], min(1, max(0, t + rnd.uniform(-0.12, 0.12)))) + Vector((0, 0, -0.03))
            b.tube([a_ - Vector((0, 0, 0.04)), c_ + Vector((0, 0, rnd.uniform(0.02, 0.14)))], 0.02, rnd.choice(STICKS), n=3)
        if rnd.random() < 0.6:
            b.tube([lows[i], tops[i + 1] + Vector((0, 0, -0.03))], 0.018, "Straw_Dark", n=3)


def arc_pts(R, a0, a1, n, z=0.0):
    return [(cos(a0 + (a1 - a0) * i / n) * R, sin(a0 + (a1 - a0) * i / n) * R, z) for i in range(n + 1)]


def gtrunk(w, z0, z1, r0, r1, seed, bend=(0.0, 0.0), n=14, segs=8, ridges=9, flare=0.0):
    """Section of the giant tree: pale faceted bark with darker raised ridges, burls and moss."""
    rnd = random.Random(seed)
    H = z1 - z0
    pts, rad = [], []
    for i in range(segs + 1):
        t = i / segs
        pts.append(Vector((bend[0] * sin(pi * t), bend[1] * sin(pi * t), z0 + H * t)))
        rad.append((r0 + (r1 - r0) * t) * (1 + flare * max(0.0, 1 - t * 3.2) ** 2) * (1 + (rnd.uniform(-0.035, 0.035) if 0 < i < segs else 0)))
    w.tube(pts, rad, "Bark_Pale", n=n, a0=seed)
    for k in range(ridges):
        th = tau * k / ridges + rnd.uniform(-0.15, 0.15)
        tw = rnd.uniform(-0.25, 0.25)
        i0, i1 = (0, segs) if rnd.random() < 0.6 else sorted(rnd.sample(range(segs + 1), 2))
        if i1 - i0 < 2:
            i0, i1 = 0, segs
        rp = []
        for i in range(i0, i1 + 1):
            a_ = th + tw * i / segs
            rp.append(pts[i] + Vector((cos(a_), sin(a_), 0)) * rad[i] * 0.97)
        w.tube(rp, [0.16 * (1 + flare * max(0.0, 1 - (i / segs) * 3.2) ** 2 * 0.5) for i in range(i0, i1 + 1)], "Bark_PaleShade", n=4)
    for k in range(3):
        i = rnd.randrange(1, segs)
        th = rnd.uniform(0, tau)
        w.blob(pts[i] + Vector((cos(th), sin(th), 0)) * rad[i] * 0.93, (0.5, 0.5, 0.42), "Bark_PaleShade", n=10, seed=rnd.randint(0, 9999))
    return pts, rad


def moss_on(g, pts, rad, seed, count=5):
    rnd = random.Random(seed)
    sh = leaf_shade(GREEN3, seed)
    for k in range(count):
        i = rnd.randrange(0, len(pts))
        th = rnd.uniform(0, tau)
        g.blob(pts[i] + Vector((cos(th), sin(th), 0)) * rad[i] * 0.98, (0.6, 0.6, 0.35), "Leaf_Green", n=10, seed=rnd.randint(0, 9999), shade=sh)


def liana(b, l, top, length, seed, sway=0.3, leaves=True, r=0.035):
    rnd = random.Random(seed)
    top = Vector(top)
    pts = [top + Vector((sway * sin(k * 1.3 + seed) * k / 5, sway * cos(k * 0.9 + seed) * k / 5, -length * k / 5)) for k in range(6)]
    b.tube(pts, [r, r, r * 0.9, r * 0.8, r * 0.65, r * 0.4], "Leaf_Shade", n=4)
    if leaves:
        for k in range(int(length / 0.45)):
            t = rnd.random() * 0.98
            i = min(4, int(t * 5))
            p = pts[i].lerp(pts[i + 1], t * 5 - i)
            a_ = rnd.uniform(0, tau)
            leaf(l, p, p + Vector((cos(a_) * 0.28, sin(a_) * 0.28, -0.12)), 0.09, GREEN3[k % 3], droop=0.2, seg=2)
    return pts


# ================================================================ GIANT TREE KIT
@asset("GiantTree_Trunk_Base", GT, ("tree", "giant", "trunk", "roots"), "Foot of a giant tree: flared pale trunk with ridged bark and big buttress roots. Top is a clean 3 m (author) radius at 10 m so trunk sections stack on it. Origin: ground centre.")
def _(a):
    w, g = B(), B()
    pts, rad = gtrunk(w, -0.6, 10.0, 3.9, RG, 1, flare=0.55, segs=10)
    rnd = random.Random(2)
    for i in range(7):
        ang = tau * i / 7 + rnd.uniform(-0.2, 0.2)
        r = rnd.uniform(7.5, 10.0)
        w.tube([(cos(ang) * 2.6, sin(ang) * 2.6, 3.2), (cos(ang) * 4.4, sin(ang) * 4.4, 1.3), (cos(ang + 0.1) * r * 0.75, sin(ang + 0.1) * r * 0.75, 0.3),
                (cos(ang + 0.25) * r, sin(ang + 0.25) * r, -0.5)], [1.25, 0.9, 0.5, 0.16], "Bark_Pale" if i % 2 else "Bark_PaleShade", n=7)
        if i % 2 == 0:
            w.tube([(cos(ang + 0.1) * r * 0.7, sin(ang + 0.1) * r * 0.7, 0.3), (cos(ang - 0.4) * r * 0.95, sin(ang - 0.4) * r * 0.95, -0.4)], [0.3, 0.08], "Bark_Pale", n=5)
    moss_on(g, pts, rad, 3, 7)
    a.part("Trunk", w)
    a.part("Moss", g)


@asset("GiantTree_Trunk_Section", GT, ("tree", "giant", "trunk", "modular"), "Stackable 8 m (author) length of giant trunk with a gentle bend; both ends are the same 3 m radius on the same axis. Origin: bottom centre.")
def _(a):
    w, g = B(), B()
    pts, rad = gtrunk(w, 0.0, 8.0, RG, RG, 5, bend=(0.5, -0.25))
    moss_on(g, pts, rad, 6, 5)
    for i, (th, z) in enumerate(((0.6, 2.5), (0.95, 3.1), (3.5, 5.6))):
        p = pts[int(z)] + Vector((cos(th), sin(th), 0)) * RG * 0.98
        p.z = z
        g.lathe([(0.0, -0.12), (0.7, -0.05), (0.75, 0.05), (0.0, 0.14)], "Clay_Butter" if i % 2 else "Clay_Terracotta", n=7, M=T(*p) @ RZ(th) @ SC(1, 1.3, 1), wob=0.08, seed=i)
    a.part("Trunk", w)
    a.part("Growth", g)


@asset("GiantTree_Crown_Fork", GT, ("tree", "giant", "crown", "fork", "branches"), "Top of the giant trunk splitting into four heavy, gnarled limbs that each fork again. Bare, so you can dress it with canopy clumps. Origin: bottom centre (3 m radius).")
def _(a):
    w, l = B(), B()
    pts, rad = gtrunk(w, 0.0, 3.2, RG, 2.5, 8, segs=4, ridges=7)
    rnd = random.Random(9)
    tips = []
    for i in range(4):
        ang = tau * i / 4 + 0.4 + rnd.uniform(-0.2, 0.2)
        limb(w, (cos(ang) * 1.1, sin(ang) * 1.1, 2.6), (cos(ang) * 0.8, sin(ang) * 0.8, 0.6), 8.0, 1.25, 2, rnd, tips, "Bark_Pale" if i % 2 else "Bark_PaleShade",
             gnarl=0.22, up=0.07, spread=0.65, shrink=0.66)
    w.blob((0, 0, 3.3), (2.2, 2.2, 1.0), "Bark_Pale", n=14, seed=3)
    for i, (p, dv, r) in enumerate(tips):
        if i % 2 == 0:
            pad(l, p, 1.4, 0.45, JUNGLE3, seed=i, lumps=4)
    a.part("Limbs", w)
    a.part("Leaves", l)


def big_limb(a, seed, target, up):
    w, l, g = B(), B(), B()
    rnd = random.Random(seed)
    tips = []
    limb(w, (0, -0.6, 0), target, 11.0, 1.25, 2, rnd, tips, "Bark_Pale", gnarl=0.14, up=up, spread=0.6, kids=(2, 2, 3), shrink=0.5)
    w.lathe([(1.7, -0.5), (1.35, 0.5), (1.2, 1.2)], "Bark_PaleShade", n=9, M=RX(-pi / 2) @ T(0, 0, -0.9))
    for i, (p, dv, r) in enumerate(tips):
        pad(l, p + Vector((0, 0, 0.3)), rnd.uniform(1.7, 2.4), 0.42, JUNGLE3 if i % 3 else GREEN3, seed=seed * 7 + i, lumps=5)
        for k in range(1):
            liana(g, l, p + Vector((rnd.uniform(-0.8, 0.8), rnd.uniform(-0.8, 0.8), -0.2)), rnd.uniform(2.0, 4.5), seed * 31 + i * 3 + k)
    a.part("Limb", w)
    a.part("Leaves", l)
    a.part("Lianas", g)


@asset("GiantTree_Limb_Long", GT, ("tree", "giant", "limb", "branch"), "Long, near-level giant branch (about 11 m author) thick enough to walk and build on, ending in leaf pads with hanging lianas. Origin: where it joins the trunk; grows toward +Y.")
def _(a):
    big_limb(a, 11, (0.05, 1, 0.12), 0.02)


@asset("GiantTree_Limb_Rising", GT, ("tree", "giant", "limb", "branch"), "Giant branch that sweeps outward and curls upward, ending in leaf pads with lianas. Origin: where it joins the trunk; grows toward +Y.")
def _(a):
    big_limb(a, 12, (-0.1, 1, 0.3), 0.16)


@asset("GiantTree_Canopy_Clump", GT, ("tree", "giant", "canopy", "leaves"), "Large clump of layered leaf clouds on a few twigs, to dress giant limbs and roofs. Origin: bottom centre (where the twigs meet).")
def _(a):
    w, l = B(), B()
    rnd = random.Random(21)
    tips = []
    for i in range(3):
        ang = tau * i / 3 + 0.3
        limb(w, (0, 0, 0), (cos(ang) * 0.7, sin(ang) * 0.7, 0.8), 2.6, 0.3, 1, rnd, tips, "Bark_PaleShade", gnarl=0.25, up=0.1, spread=0.7)
    for i, (p, dv, r) in enumerate(tips):
        pad(l, p, rnd.uniform(1.5, 2.0), 0.5, JUNGLE3, seed=i + 2, lumps=5, n=14)
    pad(l, (0, 0, 3.4), 2.4, 0.5, GREEN3, seed=40, lumps=7, n=15)
    a.part("Twigs", w)
    a.part("Leaves", l)


@asset("GiantTree_Canopy_Shelf", GT, ("tree", "giant", "canopy", "leaves", "wide"), "Wide, flat canopy layer about 12 m (author) across, made of many leaf clouds - stack two or three for a rainforest crown. Origin: centre underside.")
def _(a):
    l, g = B(), B()
    rnd = random.Random(23)
    for i in range(13):
        ang = tau * i / 13 * 2.4
        d = 5.2 * sqrt((i + 0.5) / 13)
        pad(l, (cos(ang) * d, sin(ang) * d * 0.85, rnd.uniform(0.0, 0.7)), rnd.uniform(1.6, 2.3), 0.36, JUNGLE3 if i % 3 else GREEN3, seed=i + 60, lumps=5)
    for i in range(7):
        ang = rnd.uniform(0, tau)
        d = rnd.uniform(1.5, 5.0)
        liana(g, l, (cos(ang) * d, sin(ang) * d * 0.85, -0.1), rnd.uniform(2.0, 5.0), i + 70)
    a.part("Leaves", l)
    a.part("Lianas", g)


@asset("GiantTree_Root_Arch", GT, ("tree", "giant", "root", "arch"), "Huge buttress root that arches over a path and plunges back into the ground, with side roots and moss. Span about 8 m author along X. Origin: ground centre under the arch.")
def _(a):
    w, g = B(), B()
    w.tube([(-4.6, 0, -0.6), (-3.9, 0.2, 1.6), (-2.2, 0.1, 3.4), (0, -0.2, 4.0), (2.3, 0.1, 3.2), (3.9, 0.3, 1.4), (4.7, 0.2, -0.6)], [1.1, 0.95, 0.8, 0.72, 0.68, 0.62, 0.5], "Bark_Pale", n=9)
    w.tube([(-2.4, 0.1, 3.2), (-1.6, 1.6, 1.5), (-1.2, 2.6, -0.4)], [0.4, 0.3, 0.12], "Bark_PaleShade", n=6)
    w.tube([(2.2, 0.1, 3.1), (2.7, -1.5, 1.4), (3.4, -2.4, -0.4)], [0.38, 0.28, 0.12], "Bark_PaleShade", n=6)
    w.tube([(-3.9, 0.2, 1.4), (-5.6, -1.0, 0.2), (-6.6, -1.6, -0.4)], [0.45, 0.28, 0.1], "Bark_Pale", n=6)
    sh = leaf_shade(GREEN3, 1)
    for (x, z) in ((-1.5, 3.95), (0.6, 4.25), (2.6, 3.4)):
        g.blob((x, 0, z), (0.9, 0.7, 0.3), "Leaf_Green", n=11, seed=int(x * 9) + 20, shade=sh)
    l = B()
    for i, x in enumerate((-1.0, 0.4, 1.7)):
        liana(g, l, (x, 0.1, 3.3), 1.6 + i * 0.5, i + 5)
    a.part("Root", w)
    a.part("Moss", g)
    a.part("Leaves", l)


@asset("GiantTree_Stump_Platform", GT, ("tree", "stump", "platform"), "Broad sawn stump with growth rings - a natural round platform or plaza centre. Origin: ground centre; top at 1.6 m author.")
def _(a):
    w, g = B(), B()
    pts, rad = gtrunk(w, -0.4, 1.6, 2.9, 2.3, 31, flare=0.3, segs=4, ridges=8, n=12)
    w.lathe([(2.25, 1.6), (2.2, 1.64), (1.6, 1.65), (1.0, 1.66), (0.45, 1.67), (0.0, 1.68)], ["Wood_Honey", "Wood_Pale", "Wood_Honey", "Wood_Pale", "Wood_Rose"], n=12)
    for i in range(6):
        ang = tau * i / 6 + 0.3
        w.tube([(cos(ang) * 2.3, sin(ang) * 2.3, 0.9), (cos(ang) * 3.6, sin(ang) * 3.6, 0.2), (cos(ang + 0.2) * 5.0, sin(ang + 0.2) * 5.0, -0.4)], [0.7, 0.42, 0.12], "Bark_Pale", n=6)
    moss_on(g, pts, rad, 4, 4)
    a.part("Stump", w)
    a.part("Moss", g)


# ---------------------------------------------------------------- pieces that wrap the giant trunk
@asset("Trunk_Walkway_Spiral", GT, ("walkway", "spiral", "trunk", "ramp", "sticks"), "Half-turn plank ramp that winds up around the giant trunk, with stick railing and brackets into the bark. Climbs 4 m author; stack copies turned 180 degrees and raised 6.67 m real to keep climbing. Origin: trunk axis.")
def _(a):
    P = lambda t: Vector((3.95 * cos(-pi / 2 + pi * t), 3.95 * sin(-pi / 2 + pi * t), 4.0 * t))
    path = Path(P)
    d, w, r = B(), B(), B()
    walkway(d, path, 34, 1.5, seed=3, plank=0.27)
    for side in (-0.55, 0.55):
        w.tube([path.edge(i / 14, side, -0.14) for i in range(15)], 0.07, "Wood_Dark", sq=True)
    for k in range(7):
        t = (k + 0.5) / 7
        o = path.edge(t, 0.6, -0.16)
        ang = -pi / 2 + pi * t
        w.tube([o, (cos(ang) * RG * 0.97, sin(ang) * RG * 0.97, o.z - 1.3)], 0.06, "Wood_Honey", n=5)
        w.beam(path.edge(t, 0.7, -0.2), (cos(ang) * RG * 0.97, sin(ang) * RG * 0.97, o.z - 0.05), 0.11, "Wood_Rose")
    stick_rail(r, [path.edge(i / 10, 0.68, 0.0) for i in range(11)], seed=4, last=False)
    a.part("Planks", d); a.part("Frame", w); a.part("Railing", r)


@asset("Trunk_Platform_Ring", GT, ("platform", "ring", "trunk", "sticks"), "Ring deck all the way round the giant trunk with radial planks, stick railing (one opening at -Y) and brackets. Origin: trunk axis at deck height.")
def _(a):
    rnd = random.Random(6)
    r0, r1 = RG + 0.05, 5.3
    d, w, r = B(), B(), B()
    n = 44
    for i in range(n):
        ang = tau * (i + 0.5) / n
        w0, w1 = tau * r0 / n - 0.02, tau * r1 / n - 0.035
        dz = rnd.uniform(-0.008, 0.008)
        ro = r1 + rnd.uniform(-0.06, 0.04)
        V = [(-w0 / 2, r0, -0.07 + dz), (w0 / 2, r0, -0.07 + dz), (w1 / 2, ro, -0.07 + dz), (-w1 / 2, ro, -0.07 + dz),
             (-w0 / 2, r0, dz), (w0 / 2, r0, dz), (w1 / 2, ro, dz), (-w1 / 2, ro, dz)]
        d.add(V, [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], rnd.choice(WOODS), M=RZ(ang - pi / 2))
    w.lathe([(r1 - 0.14, -0.28), (r1 + 0.06, -0.28), (r1 + 0.06, -0.075), (r1 - 0.14, -0.075)], "Wood_Dark", n=24, loop=True)
    w.lathe([(4.1, -0.24), (4.26, -0.24), (4.26, -0.075), (4.1, -0.075)], "Wood_Dark", n=24, loop=True)
    for i in range(10):
        ang = tau * i / 10 + 0.15
        w.tube([(cos(ang) * (r1 - 0.2), sin(ang) * (r1 - 0.2), -0.22), (cos(ang) * RG * 0.97, sin(ang) * RG * 0.97, -1.9)], 0.07, "Wood_Honey", n=5)
    gap = 0.2
    stick_rail(r, arc_pts(r1 - 0.12, -pi / 2 + gap, -pi / 2 + tau - gap, 20), seed=7)
    a.part("Deck", d); a.part("Frame", w); a.part("Railing", r)


@asset("Trunk_Platform_Nest", GT, ("platform", "nest", "trunk", "woven", "balcony"), "Woven basket balcony that clings to one side of the giant trunk, propped by struts - a lookout nest. Origin: trunk axis at floor height; nest on the -Y side.")
def _(a):
    d, w, wk = B(), B(), B()
    a0, a1 = -pi / 2 - 1.0, -pi / 2 + 1.0
    r1 = 5.2
    rnd = random.Random(8)
    n = 16
    for i in range(n):
        ang = a0 + (a1 - a0) * (i + 0.5) / n
        w0, w1 = (a1 - a0) * RG / n - 0.02, (a1 - a0) * r1 / n - 0.03
        V = [(-w0 / 2, RG, -0.07), (w0 / 2, RG, -0.07), (w1 / 2, r1, -0.07), (-w1 / 2, r1, -0.07), (-w0 / 2, RG, 0), (w0 / 2, RG, 0), (w1 / 2, r1, 0), (-w1 / 2, r1, 0)]
        d.add(V, [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], rnd.choice(WOODS), M=RZ(ang - pi / 2))
    rim = arc_pts(r1 - 0.05, a0, a1, 12)
    ends = [(cos(a0) * RG, sin(a0) * RG, 0), (cos(a1) * RG, sin(a1) * RG, 0)]
    loop = [ends[0]] + rim + [ends[1]]
    for k in range(7):
        z = 0.08 + k * 0.14
        bulge = 1 + 0.035 * sin(pi * k / 6)
        wk.tube([(x * (bulge if 0 < j < len(loop) - 1 else 1), y * (bulge if 0 < j < len(loop) - 1 else 1), z + 0.03 * ((j + k) % 2)) for j, (x, y, _) in enumerate(loop)],
                0.06, "Straw_Gold" if k % 2 else "Straw_Dark", n=4)
    for j, (x, y, _) in enumerate(loop):
        wk.tube([(x, y, -0.2), (x * 1.03, y * 1.03, 1.15)], 0.045, "Wood_Honey", n=5)
    w.tube([(x, y, -0.17) for (x, y, _) in loop], 0.09, "Wood_Dark", sq=True)
    for ang in (-pi / 2 - 0.6, -pi / 2, -pi / 2 + 0.6):
        w.tube([(cos(ang) * (r1 - 0.3), sin(ang) * (r1 - 0.3), -0.2), (cos(ang) * RG * 0.97, sin(ang) * RG * 0.97, -2.4)], 0.08, "Wood_Honey", n=5)
    a.part("Deck", d); a.part("Frame", w); a.part("Wicker", wk)
    a.use("Lantern_Hanging", (cos(a1 - 0.1) * 4.6, sin(a1 - 0.1) * 4.6, 2.3))
    p = B()
    p.tube([(cos(a1 - 0.1) * RG, sin(a1 - 0.1) * RG, 2.0), (cos(a1 - 0.1) * 4.7, sin(a1 - 0.1) * 4.7, 2.35)], [0.07, 0.04], "Wood_Dark", n=5)
    a.part("Arm", p)


@asset("Trunk_Stairs_Pegs", GT, ("stairs", "trunk", "pegs", "logs"), "Split-log steps pegged straight into the giant trunk, spiralling up a third of a turn with a rope hand-line. Climbs 4 m author. Origin: trunk axis at the foot.")
def _(a):
    s, r = B(), B()
    n = 15
    pts = []
    for i in range(n):
        ang = -pi / 2 + (tau / 3) * i / (n - 1)
        z = 4.0 * (i + 1) / n
        M = RZ(ang - pi / 2) @ T(0, RG - 0.3, z)
        s.tube([(0, 0, -0.06), (0, 1.45, -0.03)], 0.2, WOODS[i % 4], n=6, asp=0.4, M=M)
        s.tube([(0, 0.3, -0.12), (0, 0.0, -0.75)], 0.045, "Wood_Dark", n=4, M=M)
        if i % 3 == 0 or i == n - 1:
            p = M @ Vector((0, 1.35, 0))
            pts.append(p)
    bp, br = B(), B()
    fence_run(bp, br, pts, h=1.0, seed=3)
    a.part("Steps", s); a.part("Posts", bp); a.part("Rope", br)


@asset("Trunk_Door_Hollow", GT, ("door", "trunk", "hollow", "arch"), "Arched door set into a swelling of bark, to suggest a room inside the giant trunk; lantern and step included. Origin: trunk axis at floor level; door faces -Y on the 3 m radius.")
def _(a):
    w, d, s = B(), B(), B()
    y = -RG
    for sx in (-1, 1):
        w.tube([(sx * 1.25, y + 0.25, -0.3), (sx * 1.15, y - 0.15, 1.3), (sx * 0.75, y - 0.2, 2.35), (0, y - 0.22, 2.75)], [0.42, 0.34, 0.3, 0.3], "Bark_PaleShade", n=7)
    w.blob((0, y - 0.1, 2.8), (0.5, 0.35, 0.35), "Bark_Pale", n=10, seed=2)
    d.lathe([(0.0, 0.0), (0.82, 0.0), (0.82, 0.1), (0.0, 0.1)], "Clay_Butter", n=14, M=T(0, y - 0.02, 1.45) @ RX(-pi / 2) @ SC(1, 1.25, 1))
    d.box((0, y - 0.04, 0.72), (1.64, 0.1, 1.45), "Clay_Butter")
    for x in (-0.5, 0.0, 0.5):
        d.box((x + 0.25, y - 0.1, 1.1), (0.03, 0.03, 2.1 if abs(x) < 0.3 else 1.8), "Wood_Dark")
    d.box((0, y - 0.11, 0.55), (1.6, 0.05, 0.1), "Wood_Dark")
    d.box((0, y - 0.11, 1.6), (1.6, 0.05, 0.1), "Wood_Dark")
    d.blob((0.55, y - 0.16, 1.05), 0.08, "Metal_Brass", n=8, seed=1)
    s.lathe([(1.1, -0.3), (1.15, -0.08), (1.0, 0.0)], "Stone_Warm", n=8, c=(0, y - 0.7, 0), wob=0.07, seed=4)
    a.part("Frame", w); a.part("Door", d); a.part("Step", s)
    a.use("Lantern_Hanging", (1.35, y - 0.55, 2.7))
    arm = B()
    arm.tube([(1.2, y + 0.1, 2.5), (1.35, y - 0.6, 2.72)], [0.06, 0.035], "Wood_Dark", n=5)
    a.part("Arm", arm)


@asset("Trunk_Window_Knot", GT, ("window", "trunk", "knot"), "Glowing round window in a bark burl for the giant trunk. Origin: trunk axis at window centre height; faces -Y on the 3 m radius.")
def _(a):
    w, g = B(), B()
    M = T(0, -RG + 0.1, 0) @ RX(-pi / 2)
    w.lathe([(0.42, -0.3), (0.8, -0.22), (0.95, 0.1), (0.42, 0.1)], "Bark_PaleShade", n=10, loop=True, M=M, wob=0.07, seed=3)
    w.box((0, -RG - 0.12, 0), (0.05, 0.05, 0.84), "Wood_Dark")
    w.box((0, -RG - 0.12, 0), (0.84, 0.05, 0.05), "Wood_Dark")
    g.lathe([(0.0, -0.1), (0.43, -0.1), (0.43, -0.05), (0.0, -0.05)], "Glass_Warm", n=10, M=M)
    a.part("Burl", w)
    a.part("Pane", g, shadow=False)


@asset("Trunk_Mushroom_Steps", GT, ("mushroom", "steps", "trunk", "shelf", "magic"), "Seven bracket-fungus shelves climbing the giant trunk as steps; every other one glows. Origin: trunk axis at the foot; climbs 3 m author.")
def _(a):
    m, g = B(), B()
    for i in range(7):
        ang = -pi / 2 + 0.24 * i
        z = 0.45 + i * 0.43
        M = T(cos(ang) * (RG - 0.15), sin(ang) * (RG - 0.15), z) @ RZ(ang) @ SC(1.25, 1.0, 1)
        tgt = g if i % 2 else m
        tgt.lathe([(0.0, -0.22), (0.5, -0.12), (0.92, -0.04), (0.95, 0.03), (0.55, 0.1), (0.0, 0.14)],
                  ["Flower_White", "Flower_White", "Clay_Cream", "Glow_Teal" if i % 2 else "Clay_Terracotta", "Glow_Teal" if i % 2 else "Clay_Rust"], n=9, M=M, wob=0.06, seed=i)
    a.part("Shelves", m)
    a.part("GlowShelves", g, shadow=False)
    a.light("Light", (0.5, -RG - 0.9, 1.6), TEAL, 20)


# ================================================================ BUILDING KIT - second wave
@asset("Roof_Woven_Wide", K, ("roof", "woven", "shingle", "thatch", "wide"), "Wide, low roof of overlapping woven shingles with butter-yellow accent panels and an upturned beak over the door - the jungle hut roof. For small round walls. Origin: eave line centre (wall top); beak points -Y.")
def _(a):
    c, sh, t = B(), B(), B()
    c.lathe([(0.0, 0.2), (3.4, 0.05), (3.6, 0.25), (2.6, 0.95), (1.5, 1.6), (0.5, 2.0), (0.0, 2.1)], "Straw_Dark", n=18)
    rnd = random.Random(5)
    rows = [(3.75, 0.2, 22), (3.2, 0.62, 20), (2.62, 1.0, 17), (2.02, 1.36, 14), (1.42, 1.68, 11), (0.85, 1.94, 8)]
    for ri, (R, z, n) in enumerate(rows):
        for i in range(n):
            ang = tau * (i + 0.5 * (ri % 2)) / n + rnd.uniform(-0.04, 0.04)
            w_ = tau * R / n * 1.12
            m = "Clay_Butter" if (ri in (1, 3) and i % 4 == 0) else rnd.choice(("Straw_Gold", "Straw_Dark", "Wood_Honey", "Straw_Gold"))
            M = RZ(ang - pi / 2) @ T(0, R - 0.36, z + 0.2) @ RX(0.62 + rnd.uniform(-0.06, 0.06))
            V = [(-w_ / 2, -0.42, -0.03), (w_ / 2, -0.42, -0.03), (w_ * 0.36, 0.5, -0.03), (-w_ * 0.36, 0.5, -0.03),
                 (-w_ / 2, -0.42, 0.04), (w_ / 2, -0.42, 0.04), (w_ * 0.36, 0.5, 0.04), (-w_ * 0.36, 0.5, 0.04)]
            sh.add([(x, -y, zz) for (x, y, zz) in V], [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], m, M=M)
    t.lathe([(0.55, 1.95), (0.62, 2.1), (0.3, 2.3), (0.14, 2.75), (0.0, 2.85)], "Straw_Dark", n=9, jit=0.04, seed=2)
    rope_wrap(t, (0, 0, 2.28), 0.3, turns=2, r=0.03)
    t.tube([(0, -3.3, 0.4), (0, -4.2, 0.55), (0, -4.9, 1.0), (0, -5.15, 1.55)], [0.3, 0.24, 0.16, 0.05], "Wood_Honey", n=5, asp=0.5)
    cl = B()
    for sx in (-1, 1):
        cl.add([(sx * 0.1, -3.5, 0.3), (sx * 1.9, -3.2, 0.2), (sx * 1.2, -4.3, 0.05), (sx * 0.1, -3.5, 0.26), (sx * 1.9, -3.2, 0.16), (sx * 1.2, -4.3, 0.01)],
               [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], "Cloth_Plum")
    a.part("Core", c); a.part("Shingles", sh); a.part("Trim", t); a.part("Cloth", cl)


@asset("Roof_Palm_Leaf", K, ("roof", "palm", "leaf", "jungle"), "Roof thatched with two layers of big drooping palm fronds over a light cone. For small round walls. Origin: eave line centre (wall top).")
def _(a):
    c, s, l = B(), B(), B()
    c.lathe([(0.0, 0.2), (2.6, 0.05), (2.75, 0.2), (1.6, 1.3), (0.5, 2.2), (0.0, 2.45)], "Straw_Dark", n=14)
    rnd = random.Random(4)
    for layer, (cnt, z, ln, tilt) in enumerate(((11, 2.2, 4.3, 0.55), (8, 2.5, 3.2, 0.75))):
        for i in range(cnt):
            ang = tau * i / cnt + layer * 0.3 + rnd.uniform(-0.08, 0.08)
            frond(s, l, (cos(ang) * 0.3, sin(ang) * 0.3, z), (cos(ang), sin(ang), -tilt * 0.35), ln, ("Leaf_Deep", "Leaf_Green", "Leaf_Olive"), rnd, pairs=7, droop=0.3, w=0.85)
    s.lathe([(0.34, 2.3), (0.4, 2.55), (0.2, 2.8), (0.0, 3.2)], "Straw_Gold", n=7, jit=0.05)
    rope_wrap(s, (0, 0, 2.5), 0.38, turns=2, r=0.03)
    a.part("Core", c); a.part("Spines", s); a.part("Fronds", l)


@asset("Roof_Turret_Cone", K, ("roof", "turret", "cone", "clay", "small"), "Small steep orange clay cone with tile courses and a pennant, for turrets and tiny huts (fits a 1.1 m author radius wall). Origin: eave line centre.")
def _(a):
    d, f = B(), B()
    cp, cm = courses([(1.45, 0.05), (1.2, 0.5), (0.95, 0.95), (0.7, 1.4), (0.45, 1.85), (0.22, 2.3), (0.0, 2.75)], "Leaf_Orange", "Clay_Rust", step=0.05)
    d.lathe([(0.0, 0.15), (1.2, 0.02), (1.5, -0.06)] + cp, ["Clay_Cream"] * 3 + cm, n=12)
    f.tube([(0, 0, 2.7), (0, 0, 3.6)], 0.025, "Wood_Dark", n=4)
    f.add([(0, 0, 3.55), (0.7, 0.0, 3.4), (0, 0, 3.25), (0, 0.02, 3.55), (0.7, 0.02, 3.4), (0, 0.02, 3.25)], [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], "Cloth_Coral")
    a.part("Cone", d)
    a.part("Pennant", f)


@asset("Turret_Round_Small", K, ("turret", "tower", "small", "round"), "Small round turret with slit windows and its own cone roof, carried on a bracket so it can hang off a wall or a trunk. Origin: where the bracket meets the wall; turret stands out toward -Y.")
def _(a):
    b, g = B(), B()
    cy = -1.25
    wb = B()
    wall(wb, lambda z: 1.1, 2.3, "Plaster_Cream", "Plaster_Peach", n=12, thick=0.14, openings=[(-pi / 2, 0.22, 0.9, 1.75), (0.2, 0.22, 0.9, 1.75), (pi - 0.2, 0.22, 0.9, 1.75)], band=(0.4, "Clay_Teal"))
    merge(b, wb, T(0, cy, 0))
    b.lathe([(0.25, -1.1), (0.75, -0.55), (1.2, 0.0), (1.2, 0.06), (0.0, 0.08)], ["Wood_Rose", "Wood_Rose", "Wood_Dark", "Wood_Pale"], n=12, c=(0, cy, 0))
    b.tube([(0, 0.2, -1.4), (0, cy + 0.2, -0.75)], 0.1, "Wood_Dark", n=5)
    b.tube([(0, 0.2, -0.2), (0, cy + 0.9, -0.1)], 0.1, "Wood_Dark", n=5)
    b.ring((0, cy, 2.3), 1.12, 0.06, "Wood_Dark", n=12)
    g.lathe([(0.9, 0.95), (0.9, 1.7)], "Glass_Warm", n=12, c=(0, cy, 0))
    a.part("Turret", b)
    a.part("Glow", g, shadow=False)
    a.use("Roof_Turret_Cone", (0, cy, 2.3))
    a.light("Light", (0, cy, 1.3), WARM, 25)


@asset("Wall_Stick_Open", K, ("wall", "open", "sticks", "posts", "jungle"), "Open-sided round wall: twelve timber posts under a ring beam with a stick balustrade, hanging cloth panels in three bays and a doorway at -Y. Same footprint as Wall_Round_S.")
def _(a):
    w, r, c = B(), B(), B()
    n = 12
    for i in range(n):
        ang = -pi / 2 + tau * (i + 0.5) / n
        x, y = cos(ang) * (RS - 0.1), sin(ang) * (RS - 0.1)
        w.tube([(x, y, 0.0), (x * 1.01, y * 1.01, 1.3), (x, y, HS)], [0.11, 0.095, 0.09], WOODS[i % 4], n=6)
        w.tube([(x, y, HS - 0.6), (cos(ang + 0.2) * (RS - 0.1), sin(ang + 0.2) * (RS - 0.1), HS - 0.08)], 0.04, "Wood_Dark", n=4)
        w.tube([(x, y, HS - 0.6), (cos(ang - 0.2) * (RS - 0.1), sin(ang - 0.2) * (RS - 0.1), HS - 0.08)], 0.04, "Wood_Dark", n=4)
    w.lathe([(RS - 0.26, HS - 0.14), (RS + 0.08, HS - 0.14), (RS + 0.08, HS + 0.04), (RS - 0.26, HS + 0.04)], "Wood_Dark", n=24, loop=True)
    a0 = -pi / 2 + tau * 0.5 / n
    stick_rail(r, arc_pts(RS - 0.1, a0, a0 + tau - tau / n, n - 1), h=0.95, seed=3, first=False, last=False, balusters=2)
    cols = ("Cloth_Coral", "Cloth_Butter", "Cloth_Plum")
    for k, bay in enumerate((3, 6, 9)):
        b0 = -pi / 2 + tau * (bay + 0.5) / n
        for j in range(4):
            a_ = b0 + (tau / n) * (j + 0.5) / 4
            x, y = cos(a_) * (RS - 0.12), sin(a_) * (RS - 0.12)
            c.tube([(x, y, HS - 0.15), (x * 1.01, y * 1.01, HS - 0.9), (x * 1.0, y * 1.0, 1.05 + 0.1 * (j % 2))], [0.13, 0.13, 0.12], cols[k] if j % 2 else "Plaster_Cream", n=4, asp=0.25)
    a.part("Posts", w); a.part("Railing", r); a.part("Cloth", c)


@asset("Floor_Upper_Round", K, ("floor", "upper", "loft", "interior"), "Upper floor disc for small round walls: joists, planks and a ladder hatch with a short rail. Sits on the ring beam at the wall top. Origin: centre at floor height.")
def _(a):
    d, w, r = B(), B(), B()
    R = RS - 0.22
    rnd = random.Random(3)
    k = 14
    pw = 2 * R / k
    for i in range(k):
        x = -R + pw * (i + 0.5)
        hl = sqrt(max(0.01, R * R - x * x))
        segs = [(-hl, hl)]
        if 0.3 < x < 1.5:
            segs = [(-hl, -1.5), (-0.45, hl)]
        for (y0, y1) in segs:
            if y1 - y0 > 0.1:
                d.box((x, (y0 + y1) / 2, -0.035 + rnd.uniform(-0.006, 0.006)), (pw - 0.03, y1 - y0, 0.07), rnd.choice(WOODS))
    for x in (-1.2, 0.0, 1.65):
        hl = sqrt(R * R - x * x)
        w.beam((x, -hl, -0.16), (x, hl, -0.16), 0.14, "Wood_Dark")
    w.beam((0.2, -0.45, -0.16), (1.6, -0.45, -0.16), 0.12, "Wood_Dark")
    stick_rail(r, [(0.28, -1.5, 0), (0.28, -0.45, 0), (1.55, -0.45, 0)], h=0.85, seed=2, balusters=2)
    a.part("Planks", d); a.part("Joists", w); a.part("Rail", r)


@asset("Porch_Awning", K, ("porch", "awning", "cloth", "door"), "Striped cloth awning on two crooked posts with a scalloped edge and a lantern - put it over a door or a shop front. Origin: wall face at ground, facing -Y.")
def _(a):
    w, c = B(), B()
    for sx in (-1, 1):
        w.tube([(sx * 1.5, -1.7, -0.2), (sx * 1.55, -1.75, 1.2), (sx * 1.5, -1.7, 2.25)], [0.08, 0.07, 0.06], "Wood_Dark", n=6)
    w.tube([(-1.75, -1.7, 2.22), (1.75, -1.7, 2.22)], 0.05, "Wood_Honey", n=5)
    w.tube([(-1.75, -0.05, 2.8), (1.75, -0.05, 2.8)], 0.05, "Wood_Honey", n=5)
    n = 7
    for i in range(n):
        x0, x1 = -1.7 + 3.4 * i / n, -1.7 + 3.4 * (i + 1) / n
        m = "Cloth_Coral" if i % 2 else "Cloth_Butter"
        sag = 0.12
        V = []
        for (y, z) in ((-0.05, 2.84), (-0.9, 2.52 - sag), (-1.75, 2.26)):
            V += [(x0, y, z), (x1, y, z), (x1, y, z - 0.04), (x0, y, z - 0.04)]
        F = [(0, 1, 2, 3)]
        for k in range(2):
            o = k * 4
            F += [(o + j, o + (j + 1) % 4, o + 4 + (j + 1) % 4, o + 4 + j) for j in range(4)]
        F.append((11, 10, 9, 8))
        c.add(V, F, m)
        xm = (x0 + x1) / 2
        c.add([(x0, -1.75, 2.24), (x1, -1.75, 2.24), (xm, -1.78, 1.95), (x0, -1.72, 2.24), (x1, -1.72, 2.24), (xm, -1.75, 1.95)], [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], m)
    a.part("Frame", w)
    a.part("Cloth", c)
    a.use("Lantern_Hanging", (1.1, -1.7, 2.2))


@asset("Dormer_Round", K, ("dormer", "window", "roof"), "Little round-window dormer with its own thatch hood, to stick onto roofs. Origin: back centre at its base; faces -Y.")
def _(a):
    w, t, g = B(), B(), B()
    w.lathe([(0.0, -0.9), (0.62, -0.9), (0.62, 0.0), (0.0, 0.0)], "Plaster_Cream", n=10, M=T(0, 0, 0.6) @ RX(-pi / 2))
    ring = B()
    ring_frame(ring, 0.3, 0.46, -0.98, -0.85, 0.6, "Wood_Honey", n=10, seed=2)
    merge(w, ring)
    w.box((0, -0.95, 0.6), (0.04, 0.04, 0.6), "Wood_Dark")
    w.box((0, -0.95, 0.6), (0.6, 0.04, 0.04), "Wood_Dark")
    g.lathe([(0.0, -0.95), (0.31, -0.95), (0.31, -0.92), (0.0, -0.92)], "Glass_Warm", n=10, M=T(0, 0, 0.6) @ RX(-pi / 2))
    t.lathe([(0.66, -1.1), (0.86, -1.15), (0.84, -1.0), (0.78, 0.0), (0.6, 0.0)], ["Straw_Dark", "Straw_Gold", "Straw_Gold", "Straw_Dark"], n=10, M=T(0, 0, 0.62) @ RX(-pi / 2), jit=0.03, loop=True)
    a.part("Dormer", w); a.part("Thatch", t); a.part("Pane", g, shadow=False)


@asset("Railing_Stick_Straight", K, ("railing", "fence", "sticks", "bamboo"), "Chainable stick-and-bamboo railing, 3 m author: two posts, rails run on to the next segment's post (+Y).")
def _(a):
    r = B()
    stick_rail(r, [(0, 0, 0), (0, 1.5, 0), (0, 3.0, 0)], seed=11, last=False)
    a.part("Railing", r)


@asset("Railing_Stick_Curved", K, ("railing", "fence", "sticks", "curved"), "Quarter-circle stick railing that fits the rim of Platform_Round_M (radius 3.2 m author); four make a full ring. Origin: circle centre.")
def _(a):
    r = B()
    stick_rail(r, arc_pts(3.2, 0, pi / 2, 4), seed=12, last=False)
    a.part("Railing", r)


@asset("Stairs_Spiral_Inner", K, ("stairs", "spiral", "interior", "tower"), "Compact spiral stair around a carved newel post for inside towers and stacked houses. One full turn, rises 2.82 m author (one storey). Origin: post base.")
def _(a):
    s, w, r = B(), B(), B()
    n, H = 14, 2.82
    w.tube([(0, 0, -0.1), (0, 0, H + 1.0)], [0.14, 0.11], "Wood_Dark", n=7)
    w.blob((0, 0, H + 1.08), 0.15, "Wood_Honey", n=9, seed=1)
    tops = []
    for i in range(n):
        ang = tau * i / n
        z = H * (i + 1) / n
        M = RZ(ang) @ T(0, 0, z)
        V = [(0.08, -0.06, -0.06), (1.3, -0.3, -0.06), (1.3, 0.3, -0.06), (0.08, 0.06, -0.06), (0.08, -0.06, 0), (1.3, -0.3, 0), (1.3, 0.3, 0), (0.08, 0.06, 0)]
        s.add(V, [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], WOODS[i % 4], M=M)
        if i % 2 == 0:
            p = M @ Vector((1.22, 0, 0))
            r.tube([p, p + Vector((0, 0, 0.92))], 0.025, "Wood_Pale", n=4)
        tops.append(M @ Vector((1.22, 0, 0.92)))
    r.tube(tops, 0.035, "Wood_Honey", n=5)
    a.part("Steps", s); a.part("Post", w); a.part("Rail", r)


# ================================================================ TOWN INFRASTRUCTURE
@asset("Gate_Arch_Town", DE, ("gate", "arch", "entrance", "sign"), "Town gate: two crooked timber posts under a bowed beam with a hanging name board, bunting and two lanterns. Opening 3.4 m author wide. Origin: ground centre; you walk through along Y.")
def _(a):
    w, c, s = B(), B(), B()
    for sx in (-1, 1):
        w.tube([(sx * 1.9, 0, -0.4), (sx * 2.0, 0.05, 1.5), (sx * 1.8, 0, 3.0), (sx * 1.95, 0, 4.0)], [0.2, 0.17, 0.15, 0.12], "Wood_Dark" if sx > 0 else "Wood_Rose", n=7)
        rope_wrap(s, (sx * 1.82, 0, 2.8), 0.17, turns=4, r=0.03)
    w.tube([(-2.6, 0, 3.55), (-1.2, 0, 3.95), (0, 0, 4.1), (1.2, 0, 3.95), (2.6, 0, 3.6)], [0.11, 0.14, 0.15, 0.14, 0.1], "Wood_Honey", n=7)
    V = [(-1.0, -0.04, -0.3), (1.0, -0.04, -0.3), (1.15, -0.04, 0.0), (1.0, -0.04, 0.3), (-1.0, -0.04, 0.3), (-1.15, -0.04, 0.0)]
    V += [(x, 0.04, z) for (x, y, z) in V]
    w.add(V, [(0, 1, 2, 3, 4, 5), (11, 10, 9, 8, 7, 6)] + [(i, (i + 1) % 6, (i + 1) % 6 + 6, i + 6) for i in range(6)], "Wood_Pale", M=T(0, 0, 3.25) @ RY(0.04))
    for i in range(5):
        w.blob((-0.6 + i * 0.3, -0.06, 3.25), (0.07, 0.03, 0.1), ("Clay_Teal", "Clay_Pink", "Clay_Butter")[i % 3], n=7, seed=i)
    for x in (-0.8, 0.8):
        s.tube([(x, 0, 3.55), (x, 0, 4.0)], 0.02, "Rope_Tan", n=3)
    cols = ["Cloth_Coral", "Cloth_Butter", "Cloth_Sky", "Cloth_Mint", "Cloth_Plum"]
    for i in range(9):
        t = (i + 0.5) / 9
        x = -2.4 + 4.8 * t
        z = 4.25 - 0.3 * 4 * t * (1 - t) + 0.0
        c.add([(x - 0.18, -0.012, z), (x + 0.18, -0.012, z), (x, -0.012, z - 0.42), (x - 0.18, 0.012, z), (x + 0.18, 0.012, z), (x, 0.012, z - 0.42)],
              [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], cols[i % 5])
    s.tube(sagpath((-2.4, 0, 4.25), (2.4, 0, 4.25), 0.3, 8), 0.02, "Rope_Tan", n=3)
    a.part("Timber", w); a.part("Ropes", s); a.part("Bunting", c)
    for sx in (-1, 1):
        a.use("Lantern_Hanging", (sx * 2.45, 0, 3.55))
    st = B()
    for sx in (-1, 1):
        b2 = B()
        stone_ring(b2, 0.4, 5, 0.26, seed=int(sx) + 5)
        merge(st, b2, T(sx * 1.9, 0, 0))
    a.part("Stones", st)


@asset("Lift_Basket_Crane", DE, ("lift", "crane", "basket", "pulley", "rope"), "Goods lift: braced post with a boom, pulley wheel and a woven basket on a rope, plus a stone counterweight. The basket is its own object so it can be animated. Origin: post base; boom reaches -Y.")
def _(a):
    w, r = B(), B()
    w.tube([(0, 0, -0.3), (0, 0.03, 2.0), (0, 0, 4.2)], [0.17, 0.15, 0.12], "Wood_Dark", n=7)
    w.tube([(0, 0.8, 3.7), (0, -1.5, 4.25), (0, -3.2, 4.5)], [0.11, 0.1, 0.08], "Wood_Honey", n=6)
    w.tube([(0, 0, 2.6), (0, -1.7, 4.2)], 0.06, "Wood_Rose", n=5)
    for sx in (-1, 1):
        w.tube([(sx * 0.9, 0.3, -0.2), (sx * 0.05, 0, 1.7)], 0.07, "Wood_Rose", n=5)
    M = T(0, -3.2, 4.3) @ RY(pi / 2)
    w.lathe([(0.0, -0.05), (0.22, -0.05), (0.26, -0.02), (0.2, 0.0), (0.26, 0.02), (0.22, 0.05), (0.0, 0.05)], "Wood_Pale", n=10, M=M)
    rope_wrap(r, (0, 0, 3.6), 0.13, turns=4, r=0.03)
    r.tube([(0, -3.2, 4.1), (0, -3.2, 1.75)], 0.022, "Rope_Tan", n=3)
    r.tube([(0, -3.0, 4.45), (0, 0.75, 3.85), (0, 0.8, 1.2)], 0.022, "Rope_Tan", n=3)
    w.blob((0, 0.8, 0.95), (0.3, 0.3, 0.36), "Stone_Cool", n=11, seed=4)
    a.part("Crane", w)
    a.part("Ropes", r)
    bk = B()
    for k in range(5):
        bk.ring((0, 0, -0.75 + k * 0.14), 0.42 + 0.05 * sin(pi * k / 4), 0.055, "Straw_Gold" if k % 2 else "Straw_Dark", n=10, m=4)
    bk.lathe([(0.0, -0.84), (0.4, -0.82), (0.4, -0.76), (0.0, -0.76)], "Straw_Dark", n=10)
    for i in range(4):
        ang = tau * i / 4 + 0.4
        bk.tube([(cos(ang) * 0.44, sin(ang) * 0.44, -0.2), (0, 0, 0.0)], 0.014, "Rope_Tan", n=3)
    bk.blob((0.1, 0.05, -0.55), (0.22, 0.2, 0.2), "Cloth_Butter", n=9, seed=2)
    bk.blob((-0.12, -0.08, -0.5), 0.13, "Flower_Red", n=8, seed=3)
    a.part("Basket", bk, origin=(0, -3.2, 1.75))


@asset("Landing_Pier", PP, ("pier", "landing", "dock", "platform", "flying"), "Plank pier that juts 7 m (author) out into open air on knee braces, with rope rails, mooring posts, a wind sock and a lantern - somewhere to land. Origin: the land end at deck level; runs toward -Y.")
def _(a):
    path = Path(lambda t: Vector((0, -7.0 * t, 0)))
    d, w, bp, br, c = B(), B(), B(), B(), B()
    d2 = B()
    planks_round(d2, 1.7, seed=6, z=0.0)
    d = B()
    walkway(d, path, 20, 2.0, seed=5, t1=0.8)
    merge(d, d2, T(0, -7.0, 0))
    for x in (-0.7, 0.7):
        w.tube([(x, 0.4, -0.24), (x, -6.2, -0.24)], 0.14, "Wood_Rose", n=6)
        w.tube([(x, -4.3, -0.35), (x, 0.3, -3.0)], 0.09, "Wood_Dark", n=5)
        w.tube([(x, -1.8, -0.35), (x, 0.3, -1.6)], 0.07, "Wood_Dark", n=5)
    w.ring((0, -7.0, -0.17), 1.66, 0.08, "Wood_Dark", n=14, m=4)
    for sx in (-1, 1):
        fence_run(bp, br, [(sx * 0.93, -0.1, 0), (sx * 0.93, -2.6, 0), (sx * 0.93, -5.2, 0)], seed=int(sx) + 2)
        w.tube([(sx * 1.25, -8.05, -0.3), (sx * 1.25, -8.05, 0.75)], [0.13, 0.11], "Wood_Dark", n=7)
        br.ring((sx * 1.25, -8.05, 0.45), 0.2, 0.035, "Rope_Tan", n=8, m=4)
    w.tube([(-1.2, -6.2, -0.2), (-1.25, -6.25, 2.2), (-1.2, -6.2, 4.3)], [0.08, 0.07, 0.05], "Wood_Dark", n=6)
    for k in range(5):
        r0 = 0.3 - k * 0.045
        c.lathe([(r0, 0.0), (r0 - 0.045, 0.42)], "Cloth_Coral" if k % 2 else "Plaster_Cream", n=7, M=T(-1.2, -6.2, 4.15) @ RY(pi / 2 - 0.25) @ RZ(0.3) @ T(0, 0, 0.42 * k + 0.1))
    a.part("Planks", d); a.part("Frame", w); a.part("Posts", bp); a.part("Ropes", br); a.part("Windsock", c)
    a.use("Lantern_Post", (1.3, -5.9, -0.05), rz=pi)


@asset("Water_Tower_Barrel", DE, ("water", "tank", "barrel", "tower"), "Big water barrel on four splayed stilts with a little thatch lid, a ladder and a bamboo spout. Origin: ground centre.")
def _(a):
    w, t, r = B(), B(), B()
    for i in range(4):
        ang = tau * i / 4 + pi / 4
        w.tube([(cos(ang) * 1.5, sin(ang) * 1.5, -0.3), (cos(ang) * 1.0, sin(ang) * 1.0, 3.0)], [0.13, 0.1], "Wood_Dark", n=6)
        nx = ang + pi / 2
        w.tube([(cos(ang) * 1.38, sin(ang) * 1.38, 0.6), (cos(nx) * 1.1, sin(nx) * 1.1, 2.4)], 0.05, "Wood_Rose", n=4)
    w.lathe([(0.0, 2.95), (1.25, 2.95), (1.25, 3.08), (0.0, 3.08)], "Wood_Dark", n=12)
    n = 14
    for i in range(n):
        ang = tau * i / n
        M = RZ(ang) @ T(1.12, 0, 3.08)
        w.tube([(0, 0, 0), (0.12, 0, 1.0), (0.0, 0, 2.0)], 0.26, WOODS[i % 4], n=4, asp=0.22, M=M)
    for z in (3.45, 4.1, 4.75):
        r.ring((0, 0, z), 1.2 + 0.1 * sin(pi * (z - 3.08) / 2), 0.035, "Metal_Brass", n=14, m=4)
    w.lathe([(0.0, 3.1), (1.0, 3.1), (1.08, 4.9), (0.0, 4.92)], ["Wood_Dark", "Wood_Dark", "Water"], n=12)
    t.lathe([(0.0, 5.05), (1.55, 4.95), (1.5, 5.15), (0.8, 5.65), (0.0, 6.0)], ["Straw_Dark", "Straw_Gold", "Straw_Gold", "Straw_Gold"], n=12, jit=0.04, seed=3)
    w.tube([(1.1, 0, 3.4), (1.9, 0, 3.25), (2.5, 0, 3.0)], 0.09, "Wood_Pale", n=6)
    a.part("Tank", w); a.part("Hoops", r); a.part("Lid", t)
    a.use("Ladder_Wood", (0, -2.0, 0.0))


@asset("Aqueduct_Bamboo", DE, ("water", "aqueduct", "bamboo", "pipe", "chainable"), "Chainable bamboo water trough on a forked post, 4 m author, gently sloping with water running in it. Next piece at (0, 6.67, -0.25) real metres. Origin: trough start at the post top.")
def _(a):
    w, wt = B(), B()
    V, F = [], []
    N = 6
    for i in range(N + 1):
        y = 4.0 * i / N
        z = -0.15 * i / N
        for (x, zz) in ((-0.24, 0.12), (-0.2, -0.06), (0.0, -0.14), (0.2, -0.06), (0.24, 0.12), (0.18, 0.1), (0.0, -0.06), (-0.18, 0.1)):
            V.append((x, y, z + zz))
    for i in range(N):
        for j in range(8):
            F.append((i * 8 + j, i * 8 + (j + 1) % 8, (i + 1) * 8 + (j + 1) % 8, (i + 1) * 8 + j))
    F += [tuple(range(7, -1, -1)), tuple(N * 8 + j for j in range(8))]
    w.add(V, F, "Wood_Pale")
    for y in (1.3, 2.7):
        zc = -0.02 - 0.15 * y / 4
        w.ring((0, y, zc), 0.27, 0.025, "Straw_Dark", n=8, M=T(0, y, zc) @ RX(pi / 2) @ T(0, -y, -zc))
    wt.add([(-0.15, 0.05, 0.02), (0.15, 0.05, 0.02), (0.15, 3.98, -0.13), (-0.15, 3.98, -0.13), (-0.02, 0.05, -0.05), (0.02, 0.05, -0.05), (0.02, 3.98, -0.2), (-0.02, 3.98, -0.2)],
           [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], "Water")
    w.tube([(0, 0.1, -3.4), (0.03, 0.1, -1.4), (0, 0.1, -0.5)], [0.13, 0.11, 0.1], "Wood_Dark", n=6)
    for sx in (-1, 1):
        w.tube([(0, 0.1, -0.6), (sx * 0.32, 0.1, 0.25)], [0.08, 0.05], "Wood_Dark", n=5)
    a.part("Trough", w)
    a.part("Water", wt)


@asset("Fountain_Clay", DE, ("fountain", "water", "clay", "plaza"), "Three-tier clay fountain in a round stone basin, water in every bowl. Origin: ground centre.")
def _(a):
    s, c, wt = B(), B(), B()
    s.lathe([(1.9, -0.1), (2.0, 0.35), (1.85, 0.6), (1.6, 0.6), (1.6, 0.15), (0.0, 0.15)], ["Stone_Cool", "Stone_Warm", "Stone_Warm", "Stone_Cool", "Stone_Cool"], n=14, wob=0.02, seed=1)
    stone_ring(s, 2.05, 9, 0.22, seed=2)
    wt.lathe([(0.0, 0.4), (1.6, 0.4), (1.6, 0.46), (0.0, 0.47)], "Water", n=14)
    c.lathe([(0.3, 0.15), (0.2, 0.8), (0.9, 1.15), (0.95, 1.3), (0.85, 1.3), (0.25, 1.1)], "Clay_Teal", n=10)
    wt.lathe([(0.0, 1.2), (0.85, 1.2), (0.85, 1.25), (0.0, 1.26)], "Water", n=10)
    c.lathe([(0.16, 1.1), (0.12, 1.6), (0.5, 1.85), (0.54, 1.96), (0.46, 1.96), (0.14, 1.8)], "Clay_Pink", n=9)
    wt.lathe([(0.0, 1.88), (0.46, 1.88), (0.46, 1.92), (0.0, 1.93)], "Water", n=9)
    c.lathe([(0.08, 1.8), (0.07, 2.2), (0.16, 2.3), (0.1, 2.42), (0.0, 2.5)], "Clay_Butter", n=7)
    for i in range(4):
        ang = tau * i / 4 + 0.5
        wt.tube([(cos(ang) * 0.5, sin(ang) * 0.5, 1.9), (cos(ang) * 0.68, sin(ang) * 0.68, 1.7), (cos(ang) * 0.74, sin(ang) * 0.74, 1.24)], [0.03, 0.035, 0.045], "Water", n=4)
        wt.tube([(cos(ang + 0.7) * 0.9, sin(ang + 0.7) * 0.9, 1.24), (cos(ang + 0.7) * 1.12, sin(ang + 0.7) * 1.12, 1.0), (cos(ang + 0.7) * 1.18, sin(ang + 0.7) * 1.18, 0.45)], [0.035, 0.04, 0.05], "Water", n=4)
    a.part("Basin", s); a.part("Bowls", c); a.part("Water", wt)


@asset("Garden_Planter_Round", DE, ("garden", "planter", "crops", "vegetables"), "Round stone-edged garden bed planted with cabbages, sprouts and a bean pole. Origin: ground centre.")
def _(a):
    s, e, l, f = B(), B(), B(), B()
    stone_ring(s, 1.45, 13, 0.26, seed=3)
    e.lathe([(1.4, -0.1), (1.4, 0.16), (0.7, 0.24), (0.0, 0.26)], "Wood_Dark", n=12, wob=0.03, seed=1)
    rnd = random.Random(4)
    sh = leaf_shade(GREEN3, 3)
    for i in range(6):
        ang = tau * i / 6
        x, y = cos(ang) * 0.9, sin(ang) * 0.9
        l.blob((x, y, 0.36), (0.26, 0.26, 0.2), "Leaf_Green", n=11, seed=i, shade=sh)
        for k in range(5):
            a_ = tau * k / 5 + i
            leaf(l, (x, y, 0.26), (x + cos(a_) * 0.36, y + sin(a_) * 0.36, 0.34), 0.13, "Leaf_Deep" if k % 2 else "Leaf_Lime", droop=0.15, seg=2, lift=0.3)
    for i in range(5):
        ang = tau * i / 5 + 0.6
        tuft(l, (cos(ang) * 0.38, sin(ang) * 0.38, 0.24), n=5, h=0.4, seed=i + 9, mat="Leaf_Lime")
        f.blob((cos(ang) * 0.38, sin(ang) * 0.38, 0.3), 0.07, "Leaf_Orange", n=7, seed=i)
    l.tube([(0, 0, 0.2), (0.03, 0, 1.9)], 0.03, "Wood_Pale", n=4)
    pts = [(cos(k * 1.1) * 0.1, sin(k * 1.1) * 0.1, 0.25 + k * 0.16) for k in range(10)]
    l.tube(pts, 0.018, "Leaf_Shade", n=3)
    for k in range(1, 10, 2):
        leaf(l, pts[k], (pts[k][0] + cos(k) * 0.26, pts[k][1] + sin(k) * 0.26, pts[k][2] + 0.05), 0.1, "Leaf_Green", droop=0.2, seg=2)
        f.blob((pts[k][0] - cos(k) * 0.1, pts[k][1] - sin(k) * 0.1, pts[k][2]), 0.05, "Flower_Red" if k % 4 == 1 else "Flower_White", n=6, seed=k)
    a.part("Stones", s); a.part("Soil", e); a.part("Plants", l); a.part("Fruit", f)


@asset("Garden_Trellis", DE, ("garden", "trellis", "vines", "flowers"), "A-frame stick trellis overgrown with flowering climbers, 3 m author long. Origin: ground centre; runs along Y.")
def _(a):
    w, v, l, f = B(), B(), B(), B()
    rnd = random.Random(5)
    for y in (0.0, 1.5, 3.0):
        for sx in (-1, 1):
            w.tube([(sx * 0.8, y, -0.15), (-sx * 0.1, y, 2.15)], 0.04, STICKS[int(y) % 3], n=5)
    w.tube([(0, -0.25, 2.0), (0, 3.25, 2.0)], 0.04, "Wood_Honey", n=5)
    for sx in (-1, 1):
        for z in (0.6, 1.2):
            xx = sx * 0.8 * (1 - z / 2.15) + sx * -0.1 * (z / 2.15) * 0
            w.tube([(sx * (0.8 - 0.42 * z), -0.2, z), (sx * (0.8 - 0.42 * z), 3.2, z)], 0.025, "Straw_Dark", n=4)
        for i in range(6):
            y0 = rnd.uniform(0.1, 2.9)
            pts = [(sx * (0.82 - 0.42 * z_) + rnd.uniform(-0.05, 0.05), y0 + 0.25 * sin(z_ * 3 + i), z_) for z_ in (0.0, 0.5, 1.0, 1.5, 1.95)]
            v.tube(pts, 0.022, "Leaf_Shade", n=3)
            for k, p in enumerate(pts[1:]):
                leaf(l, p, (p[0] + sx * 0.25, p[1] + rnd.uniform(-0.2, 0.2), p[2] - 0.05), 0.1, GREEN3[(i + k) % 3], droop=0.2, seg=2)
                if (i + k) % 3 == 0:
                    f.blob((p[0] + sx * 0.1, p[1], p[2] + 0.08), 0.07, ("Flower_Purple", "Flower_Yellow", "Leaf_Pink")[i % 3], n=7, seed=i * 7 + k)
    a.part("Sticks", w); a.part("Vines", v); a.part("Leaves", l); a.part("Flowers", f)


@asset("Stall_Market_Long", DE, ("stall", "market", "shop", "awning"), "Straight market stall: plank counter on barrels, shelf of pots behind, striped awning on poles and baskets of fruit. 3.4 m author wide. Origin: ground centre; customers stand on the -Y side.")
def _(a):
    w, c, f = B(), B(), B()
    w.box((0, 0, 0.92), (3.4, 0.9, 0.08), "Wood_Pale", rot=(0, 0, 0.01))
    for x in (-1.25, 1.25):
        w.lathe([(0.3, 0), (0.38, 0.3), (0.38, 0.6), (0.3, 0.88)], "Wood_Honey", n=9, c=(x, 0, 0))
        w.ring((x, 0, 0.2), 0.355, 0.025, "Metal_Brass", n=9)
        w.ring((x, 0, 0.68), 0.355, 0.025, "Metal_Brass", n=9)
    for sx in (-1, 1):
        w.tube([(sx * 1.7, 0.75, -0.2), (sx * 1.72, 0.75, 2.9)], [0.07, 0.055], "Wood_Dark", n=6)
        w.tube([(sx * 1.7, -0.85, -0.2), (sx * 1.72, -0.85, 2.3)], [0.07, 0.055], "Wood_Dark", n=6)
    w.box((0, 0.85, 1.6), (3.3, 0.3, 0.05), "Wood_Honey")
    w.box((0, 0.85, 2.15), (3.3, 0.3, 0.05), "Wood_Honey")
    cols = ["Clay_Teal", "Clay_Pink", "Clay_Butter", "Clay_Lavender", "Clay_Terracotta"]
    for i in range(6):
        pot(w, (-1.3 + i * 0.52, 0.85, 1.63 if i % 2 else 2.18), 0.42, cols[i % 5], i % 4, i)
    n = 8
    for i in range(n):
        x0, x1 = -1.85 + 3.7 * i / n, -1.85 + 3.7 * (i + 1) / n
        m = "Cloth_Sky" if i % 2 else "Plaster_Cream"
        V = [(x0, 0.8, 2.95), (x1, 0.8, 2.95), (x1, -0.95, 2.32), (x0, -0.95, 2.32), (x0, 0.8, 2.91), (x1, 0.8, 2.91), (x1, -0.95, 2.28), (x0, -0.95, 2.28)]
        c.add(V, [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], m)
        xm = (x0 + x1) / 2
        c.add([(x0, -0.96, 2.3), (x1, -0.96, 2.3), (xm, -0.98, 2.02), (x0, -0.93, 2.3), (x1, -0.93, 2.3), (xm, -0.95, 2.02)], [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], m)
    for k, (x, col) in enumerate(((-0.9, "Flower_Red"), (-0.1, "Flower_Yellow"), (0.7, "Leaf_Orange"), (1.3, "Flower_Purple"))):
        w.lathe([(0.2, 0.96), (0.3, 1.12), (0.26, 1.12), (0.0, 1.0)], "Straw_Dark", n=8, c=(x, -0.1, 0))
        for j in range(6):
            f.blob((x + cos(j * 1.2) * 0.13, -0.1 + sin(j * 1.2) * 0.13, 1.14 + (j % 2) * 0.07), 0.085, col, n=7, seed=k * 9 + j)
    a.part("Stall", w); a.part("Awning", c); a.part("Fruit", f)


@asset("Cart_Hand", DE, ("cart", "wheels", "sacks", "market"), "Two-wheeled hand cart loaded with sacks, a crate and melons. The wheels are separate objects with origins on the axle. Origin: ground under the axle; handles toward -Y.")
def _(a):
    w, c = B(), B()
    w.box((0, 0.1, 0.72), (1.3, 2.0, 0.08), "Wood_Pale")
    for sx in (-1, 1):
        w.box((sx * 0.65, 0.1, 0.95), (0.06, 2.0, 0.4), "Wood_Honey")
        w.tube([(sx * 0.6, -0.9, 0.72), (sx * 0.55, -2.2, 0.95)], 0.04, "Wood_Dark", n=5)
    w.box((0, 1.1, 0.95), (1.3, 0.06, 0.4), "Wood_Honey")
    w.tube([(-0.85, 0.1, 0.55), (0.85, 0.1, 0.55)], 0.05, "Wood_Dark", n=6)
    w.tube([(0, -0.8, 0.7), (0, -0.85, 0.0)], 0.04, "Wood_Dark", n=4)
    c.blob((-0.25, 0.5, 1.02), (0.36, 0.42, 0.3), "Cloth_Butter", n=12, seed=1, zmin=0.77)
    c.blob((0.3, 0.2, 1.0), (0.32, 0.4, 0.28), "Plaster_Cream", n=12, seed=2, zmin=0.77)
    c.box((0.2, -0.5, 0.98), (0.55, 0.5, 0.42), "Wood_Rose", rot=(0, 0, 0.2))
    for i in range(3):
        c.blob((-0.35 + i * 0.12, -0.45 - i * 0.1, 0.92 + (i == 1) * 0.18), 0.17, "Leaf_Green" if i % 2 else "Leaf_Lime", n=9, seed=i + 4)
    a.part("Cart", w)
    a.part("Load", c)
    for sx in (-1, 1):
        wh = B()
        M = RY(pi / 2)
        wh.lathe([(0.5, -0.05), (0.58, -0.05), (0.58, 0.05), (0.5, 0.05)], "Wood_Dark", n=12, loop=True, M=M)
        wh.lathe([(0.0, -0.08), (0.11, -0.08), (0.11, 0.08), (0.0, 0.08)], "Wood_Honey", n=8, M=M)
        for k in range(6):
            ang = tau * k / 6
            wh.tube([(0, cos(ang) * 0.1, sin(ang) * 0.1), (0, cos(ang) * 0.52, sin(ang) * 0.52)], 0.03, "Wood_Pale", n=4)
        mm = B()
        merge(mm, wh, T(sx * 0.82, 0.1, 0.57))
        a.part("Wheel_L" if sx < 0 else "Wheel_R", mm, origin=(sx * 0.82, 0.1, 0.57))


@asset("Banner_Ribbon_Spiral", NR, ("banner", "ribbon", "spiral", "hanging", "cloth"), "Long curly ribbon that hangs in a loose spiral, like the streamers on the jungle tree. 6 m author drop. Origin: top.")
def _(a):
    c, r = B(), B()
    pts, pts2 = [], []
    for i in range(49):
        t = i / 48
        ang = t * tau * 5
        R = 0.42 * (0.5 + 0.5 * sin(pi * min(1.0, t * 1.3 + 0.1)))
        pts.append((cos(ang) * R, sin(ang) * R, -0.3 - 5.7 * t))
    c.tube(pts, [0.2 * (1 - 0.7 * (i / 48) ** 2) for i in range(49)], "Leaf_Orange", n=4, asp=0.07)
    knot(r, (0, 0, 0), 0.07)
    r.tube([(0, 0, 0), (0.21, 0.0, -0.3)], 0.018, "Rope_Tan", n=3)
    a.part("Ribbon", c)
    a.part("Knot", r)


@asset("Banner_Pole_Tall", DE, ("banner", "pole", "flag", "cloth"), "Tall leaning pole with a long forked cloth banner, cross-spar and streamers. Origin: base.")
def _(a):
    w, c = B(), B()
    w.tube([(0, 0, -0.4), (0.06, 0, 2.5), (0.0, 0, 5.4)], [0.1, 0.08, 0.05], "Wood_Dark", n=6)
    w.tube([(-0.1, 0, 5.0), (1.15, 0, 5.1)], 0.035, "Wood_Honey", n=5)
    w.blob((0, 0, 5.48), 0.09, "Metal_Brass", n=8, seed=1)
    V = []
    rows = [(5.05, 0.0), (4.2, 0.06), (3.3, -0.05), (2.5, 0.08)]
    for (z, s_) in rows:
        V += [(0.12 + s_, -0.015, z), (1.05 + s_, -0.015, z), (1.05 + s_, 0.015, z), (0.12 + s_, 0.015, z)]
    F = [(0, 1, 2, 3)]
    for k in range(3):
        o = k * 4
        F += [(o + j, o + (j + 1) % 4, o + 4 + (j + 1) % 4, o + 4 + j) for j in range(4)]
    F.append((15, 14, 13, 12))
    c.add(V, F, ["Cloth_Plum"] + ["Cloth_Plum"] * 4 + ["Cloth_Butter"] * 4 + ["Cloth_Plum"] * 4 + ["Cloth_Plum"])
    for sx in (0.25, 0.85):
        c.add([(sx + 0.0, -0.012, 2.5), (sx + 0.3, -0.012, 2.5), (sx + 0.15, -0.012, 1.9), (sx, 0.012, 2.5), (sx + 0.3, 0.012, 2.5), (sx + 0.15, 0.012, 1.9)],
              [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], "Cloth_Butter")
    for i, col in enumerate(("Cloth_Coral", "Cloth_Sky", "Cloth_Mint")):
        c.tube([(0, 0, 5.3), (-0.3 - i * 0.2, 0.1 * i, 4.9 - i * 0.3), (-0.5 - i * 0.3, 0.2 * i - 0.1, 4.2 - i * 0.4)], [0.06, 0.05, 0.02], col, n=4, asp=0.1)
    a.part("Pole", w)
    a.part("Cloth", c)


@asset("Laundry_Line", NR, ("laundry", "rope", "span", "cloth"), "Washing line with shirts, cloths and a blanket pegged out, 7 m author. Origin: first end; other end at +Y.")
def _(a):
    b, c = B(), B()
    L, sag = 7.0, 0.7
    span_rope(b, L, sag, r=0.022)
    rnd = random.Random(8)
    items = [(0.12, 0.55, 0.8, "Cloth_Sky"), (0.26, 0.5, 0.6, "Plaster_Cream"), (0.4, 0.9, 1.0, "Cloth_Coral"), (0.56, 0.45, 0.7, "Cloth_Butter"), (0.7, 0.6, 0.85, "Cloth_Mint"), (0.86, 0.5, 0.55, "Cloth_Plum")]
    for (t, wd, ln, m) in items:
        y = L * t
        z = -sag * 4 * t * (1 - t) - 0.02
        sw = rnd.uniform(-0.12, 0.12)
        V = [(-0.012, y - wd / 2, z), (-0.012, y + wd / 2, z), (-0.012 + sw, y + wd / 2 * 0.95, z - ln), (-0.012 + sw, y - wd / 2 * 0.95, z - ln),
             (0.012, y - wd / 2, z), (0.012, y + wd / 2, z), (0.012 + sw, y + wd / 2 * 0.95, z - ln), (0.012 + sw, y - wd / 2 * 0.95, z - ln)]
        c.add(V, [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], m)
        for dy in (-wd * 0.35, wd * 0.35):
            b.box((0, y + dy, z + 0.02), (0.05, 0.03, 0.1), "Wood_Pale")
    a.part("Rope", b)
    a.part("Laundry", c)


# ================================================================ JUNGLE PLANTS
@asset("Palm_Small", PL, ("palm", "tree", "jungle"), "Small curved palm with a ringed trunk, a crown of drooping fronds and a cluster of nuts. About 4.5 m author. Origin: base of trunk.")
def _(a):
    w, s, l = B(), B(), B()
    rnd = random.Random(3)
    pts = [Vector((0.9 * (i / 8) ** 2, 0.15 * sin(i * 0.5), 3.6 * i / 8)) for i in range(9)]
    w.tube(pts, [0.26, 0.2, 0.18, 0.17, 0.16, 0.15, 0.15, 0.16, 0.19], "Bark_Warm", n=8)
    for i in range(1, 9):
        w.ring(pts[i] - Vector((0, 0, 0.2)), 0.19 if i < 8 else 0.2, 0.03, "Bark_Plum", n=8, m=3)
    top = pts[-1]
    for i in range(10):
        ang = tau * i / 10 + rnd.uniform(-0.15, 0.15)
        frond(s, l, top + Vector((0, 0, 0.1)), (cos(ang), sin(ang), 0.55 if i % 2 else 0.2), rnd.uniform(2.3, 2.9), ("Leaf_Deep", "Leaf_Green", "Leaf_Lime"), rnd, pairs=6, droop=0.5)
    for i in range(4):
        w.blob(top + Vector((cos(i * 1.6) * 0.2, sin(i * 1.6) * 0.2, -0.22)), 0.13, "Leaf_Orange", n=8, seed=i)
    a.part("Trunk", w); a.part("Spines", s); a.part("Fronds", l)


@asset("Plant_Broadleaf", PL, ("plant", "jungle", "leaves", "broadleaf"), "Clump of big paddle-shaped jungle leaves around a purple flower spike. Origin: ground centre.")
def _(a):
    l, s, f = B(), B(), B()
    rnd = random.Random(4)
    for i in range(11):
        ang = tau * i / 11 * 1.9
        ln = rnd.uniform(1.0, 1.7)
        up = rnd.uniform(0.5, 1.3)
        base = Vector((cos(ang) * 0.08, sin(ang) * 0.08, 0.0))
        mid = base + Vector((cos(ang) * ln * 0.35, sin(ang) * ln * 0.35, up * 0.7))
        s.tube([base, mid], [0.035, 0.025], "Leaf_Shade", n=4)
        leaf(l, mid, mid + Vector((cos(ang) * ln, sin(ang) * ln, up * 0.25)), ln * 0.3, JUNGLE3[i % 3] if i % 4 else "Leaf_Lime", droop=0.35, seg=4, lift=0.15)
    s.tube([(0, 0, 0), (0.03, 0.02, 0.9), (0.0, 0.05, 1.6)], [0.04, 0.03, 0.02], "Leaf_Shade", n=4)
    for k in range(7):
        z = 1.0 + k * 0.1
        f.blob((0.02 + cos(k * 2.2) * 0.07, 0.03 + sin(k * 2.2) * 0.07, z), 0.075 - k * 0.006, "Flower_Purple" if k % 2 else "Clay_Lavender", n=7, seed=k)
    a.part("Leaves", l); a.part("Stems", s); a.part("Flower", f)


@asset("Fern_Cluster", PL, ("fern", "jungle", "undergrowth"), "Low rosette of arching fern fronds with a few curled fiddleheads. Origin: ground centre.")
def _(a):
    s, l = B(), B()
    rnd = random.Random(6)
    for i in range(9):
        ang = tau * i / 9 + rnd.uniform(-0.2, 0.2)
        frond(s, l, (0, 0, 0.02), (cos(ang), sin(ang), 0.75), rnd.uniform(1.0, 1.5), ("Leaf_Green", "Leaf_Lime", "Leaf_Deep"), rnd, pairs=5, droop=0.55, w=0.6)
    for i in range(3):
        ang = tau * i / 3 + 0.5
        pts = [(cos(ang) * 0.1 * k / 3, sin(ang) * 0.1 * k / 3, 0.2 * k) for k in range(4)]
        pts += [(cos(ang) * (0.1 + 0.09 * sin(q)), sin(ang) * (0.1 + 0.09 * sin(q)), 0.6 + 0.09 * (1 - cos(q))) for q in (0.8, 1.6, 2.4, 3.2, 4.0)]
        s.tube(pts, [0.03] * 4 + [0.028, 0.025, 0.022, 0.02, 0.016], "Leaf_Lime", n=4)
    a.part("Spines", s)
    a.part("Fronds", l)


@asset("Flower_Giant", PL, ("flower", "giant", "blue", "magic"), "Person-high blue star flower on a curved stem with two big leaves and a softly glowing heart. Origin: ground centre.")
def _(a):
    s, l, f, g = B(), B(), B(), B()
    pts = [(0, 0, -0.05), (0.12, 0.0, 0.8), (0.05, 0.05, 1.5), (-0.15, 0.0, 2.0)]
    s.tube(pts, [0.06, 0.05, 0.045, 0.04], "Leaf_Shade", n=5)
    leaf(l, (0.08, 0, 0.45), (0.95, 0.3, 0.75), 0.26, "Leaf_Green", droop=0.3, seg=4, lift=0.2)
    leaf(l, (0.1, 0, 0.75), (-0.6, -0.55, 1.15), 0.22, "Leaf_Deep", droop=0.3, seg=4, lift=0.2)
    c = Vector((-0.15, 0.0, 2.0))
    M = T(*c) @ RY(-0.5)
    for i in range(7):
        ang = tau * i / 7
        bb = B()
        leaf(bb, (cos(ang) * 0.08, sin(ang) * 0.08, 0.02), (cos(ang) * 0.78, sin(ang) * 0.78, 0.28), 0.2, "Flower_Blue" if i % 2 else "Cloth_Sky", droop=0.25, seg=3, lift=0.25)
        merge(f, bb, M)
    g.blob((0, 0, 0.1), 0.14, "Glow_Warm", n=9, seed=1, M=M)
    a.part("Stem", s); a.part("Leaves", l); a.part("Petals", f); a.part("Heart", g, shadow=False)
    a.light("Light", (-0.2, 0, 2.15), (0.7, 0.85, 1.0), 12)


# ================================================================ MORE INTERIOR
@asset("Workbench_Tools", IN, ("workbench", "tools", "furniture", "interior"), "Chunky workbench with a vice, mallet, saw and a rolled plan, shelf of jars underneath. Origin: floor centre; you stand on the -Y side.")
def _(a):
    w, t = B(), B()
    w.box((0, 0, 0.86), (1.9, 0.75, 0.1), "Wood_Honey", rot=(0, 0, 0.01))
    for (x, y) in ((-0.82, -0.28), (0.82, -0.28), (-0.82, 0.28), (0.82, 0.28)):
        w.tube([(x * 1.03, y, 0.0), (x, y, 0.82)], [0.07, 0.06], "Wood_Dark", n=5)
    w.box((0, 0, 0.3), (1.6, 0.6, 0.05), "Wood_Pale")
    for i in range(3):
        pot(t, (-0.5 + i * 0.45, 0.05, 0.33), 0.4, ("Clay_Teal", "Clay_Butter", "Clay_Pink")[i], (0, 3, 2)[i], i)
    t.box((0.75, -0.3, 0.98), (0.18, 0.2, 0.16), "Stone_Cool")
    t.tube([(0.75, -0.42, 0.98), (0.75, -0.6, 0.98)], 0.03, "Metal_Brass", n=5)
    t.tube([(-0.6, -0.05, 0.94), (-0.25, 0.1, 0.94)], 0.03, "Wood_Dark", n=5)
    t.box((-0.2, 0.12, 0.96), (0.2, 0.14, 0.12), "Wood_Pale", rot=(0, 0, 0.4))
    t.tube([(0.1, 0.2, 0.95), (0.55, 0.26, 0.95)], 0.045, "Plaster_Cream", n=6)
    t.box((0.2, -0.15, 0.925), (0.5, 0.1, 0.02), "Stone_Cool", rot=(0, 0, -0.3))
    t.tube([(0.42, -0.24, 0.94), (0.54, -0.28, 0.94)], 0.035, "Wood_Rose", n=5)
    a.part("Bench", w)
    a.part("Tools", t)


@asset("Storage_Chest_Round", IN, ("chest", "storage", "furniture", "interior"), "Barrel-topped chest with brass bands, lid ajar on a glint of treasure, plus two stacked baskets. The lid is its own object hinged at the back. Origin: floor centre.")
def _(a):
    w, g, k = B(), B(), B()
    w.box((0, 0, 0.3), (1.1, 0.65, 0.6), "Wood_Rose", taper=(1.03, 1.03))
    for x in (-0.35, 0.35):
        w.box((x, 0, 0.3), (0.08, 0.68, 0.62), "Metal_Brass", taper=(1.0, 1.03))
    g.blob((0, 0, 0.6), (0.42, 0.24, 0.1), "Glow_Warm", n=10, seed=1)
    a.part("Chest", w)
    a.part("Treasure", g, shadow=False)
    lid = B()
    M = RY(pi / 2)
    lid.tube([(-0.57, 0, 0), (0.57, 0, 0)], 0.33, "Wood_Rose", n=8, asp=0.55, M=T(0, 0, 0.62))
    for x in (-0.35, 0.35):
        lid.tube([(x - 0.04, 0, 0), (x + 0.04, 0, 0)], 0.345, "Metal_Brass", n=8, asp=0.56, M=T(0, 0, 0.62))
    a.part("Lid", lid, origin=(0, 0.33, 0.62), rot=(-0.5, 0, 0))
    for i in range(2):
        k.lathe([(0.22, 0.0), (0.33, 0.25), (0.3, 0.3), (0.26, 0.26), (0.0, 0.04)], "Straw_Gold" if i else "Straw_Dark", n=9, c=(1.0, 0.1 * i, 0.3 * i), jit=0.02, seed=i)
    k.blob((1.0, 0.1, 0.62), (0.2, 0.2, 0.1), "Cloth_Mint", n=9, seed=4)
    a.part("Baskets", k)


@asset("Kitchen_Counter_Curved", IN, ("kitchen", "counter", "furniture", "interior"), "Curved kitchen counter that hugs the inside of a small round wall, with a basin, chopping board, bowls and hanging herbs. Origin: room centre at floor - rotate to place.")
def _(a):
    w, t = B(), B()
    R = 1.95
    a0, a1 = pi / 2 - 0.62, pi / 2 + 0.62

    def slab(b, r0, r1, z0, z1, mat, n=8):
        V = []
        for k in range(n + 1):
            ang = a0 + (a1 - a0) * k / n
            for (rr, zz) in ((r1, z0), (r0, z0), (r0, z1), (r1, z1)):
                V.append((cos(ang) * rr, sin(ang) * rr, zz))
        F = []
        for k in range(n):
            for j in range(4):
                F.append((k * 4 + j, k * 4 + (j + 1) % 4, (k + 1) * 4 + (j + 1) % 4, (k + 1) * 4 + j))
        F += [(0, 1, 2, 3), tuple(n * 4 + j for j in (3, 2, 1, 0))]
        b.add(V, F, mat)
    slab(w, R - 0.62, R, 0.84, 0.92, "Wood_Honey")
    slab(w, R - 0.56, R - 0.04, 0.0, 0.84, "Plaster_Mint")
    slab(w, R - 0.3, R, 1.7, 1.75, "Wood_Honey")
    for k in range(4):
        ang = a0 + (a1 - a0) * (k + 0.5) / 4
        w.box((cos(ang) * (R - 0.58), sin(ang) * (R - 0.58), 0.45), (0.5, 0.04, 0.6), "Wood_Pale", rot=(0, 0, ang + pi / 2))
        w.blob((cos(ang) * (R - 0.62), sin(ang) * (R - 0.62), 0.5), 0.035, "Metal_Brass", n=6, seed=k)
    ang = pi / 2 - 0.3
    t.lathe([(0.12, 0.92), (0.3, 1.0), (0.32, 1.1), (0.27, 1.1), (0.0, 0.96)], "Clay_Teal", n=9, c=(cos(ang) * (R - 0.3), sin(ang) * (R - 0.3), 0))
    ang = pi / 2 + 0.25
    t.box((cos(ang) * (R - 0.3), sin(ang) * (R - 0.3), 0.94), (0.42, 0.28, 0.03), "Wood_Pale", rot=(0, 0, ang + 1.2))
    t.blob((cos(ang) * (R - 0.28), sin(ang) * (R - 0.28), 1.0), 0.07, "Flower_Red", n=7, seed=1)
    t.blob((cos(ang + 0.05) * (R - 0.36), sin(ang + 0.05) * (R - 0.36), 0.99), 0.06, "Leaf_Orange", n=7, seed=2)
    for i in range(4):
        ang = a0 + 0.12 + i * 0.32
        pot(t, (cos(ang) * (R - 0.15), sin(ang) * (R - 0.15), 1.75), 0.4, ("Clay_Pink", "Clay_Butter", "Clay_Lavender", "Clay_Terracotta")[i], (0, 2, 3, 0)[i], i)
        x, y = cos(ang + 0.14) * (R - 0.22), sin(ang + 0.14) * (R - 0.22)
        t.tube([(x, y, 1.7), (x, y, 1.5)], 0.008, "Rope_Tan", n=3)
        t.lathe([(0.02, 1.52), (0.08, 1.4), (0.05, 1.22), (0.0, 1.15)], "Leaf_Green" if i % 2 else "Leaf_Olive", n=5, c=(x, y, 0), jit=0.1, seed=i)
    a.part("Counter", w)
    a.part("Things", t)


@asset("Hanging_Rack_Pots", IN, ("rack", "hanging", "pots", "kitchen", "interior"), "Wooden hoop hung from the ceiling with pans, ladles, garlic and dried herbs dangling from it. Origin: top (ceiling hook).")
def _(a):
    w, t = B(), B()
    w.ring((0, 0, -0.8), 0.6, 0.035, "Wood_Honey", n=12, m=5)
    for i in range(3):
        ang = tau * i / 3
        t.tube([(cos(ang) * 0.6, sin(ang) * 0.6, -0.8), (0, 0, 0)], 0.012, "Rope_Tan", n=3)
    knot(t, (0, 0, 0), 0.05)
    for i in range(7):
        ang = tau * i / 7 + 0.2
        x, y = cos(ang) * 0.6, sin(ang) * 0.6
        ln = 0.2 + (i * 3 % 4) * 0.07
        t.tube([(x, y, -0.82), (x, y, -0.82 - ln)], 0.008, "Rope_Tan", n=3)
        z = -0.82 - ln
        k = i % 4
        if k == 0:
            w.lathe([(0.0, -0.02), (0.17, 0.0), (0.2, 0.07), (0.17, 0.07), (0.0, 0.02)], "Metal_Brass", n=9, M=T(x, y, z - 0.2) @ RZ(ang) @ RY(pi / 2))
        elif k == 1:
            w.lathe([(0.03, 0.0), (0.09, -0.12), (0.06, -0.3), (0.0, -0.36)], "Leaf_Olive", n=5, c=(x, y, z), jit=0.1, seed=i)
        elif k == 2:
            for j in range(4):
                w.blob((x + cos(j * 1.6) * 0.04, y + sin(j * 1.6) * 0.04, z - 0.06 - j * 0.07), 0.055, "Flower_White", n=7, seed=i * 5 + j)
        else:
            w.tube([(x, y, z), (x, y, z - 0.32)], 0.014, "Wood_Dark", n=4)
            w.lathe([(0.0, 0.0), (0.07, -0.02), (0.06, -0.08), (0.0, -0.1)], "Wood_Pale", n=7, c=(x, y, z - 0.32))
    a.part("Rack", w)
    a.part("Strings", t)


# ================================================================ MORE LIGHTS
@asset("Lantern_Cluster", LI, ("light", "lantern", "cluster", "hanging"), "Five mixed lanterns hung at different heights from a wooden hoop - a chandelier for halls, plazas and big branches. Origin: top hook.")
def _(a):
    w, r = B(), B()
    gs = {"Glow_Warm": B(), "Glow_Teal": B(), "Glow_Pink": B()}
    w.ring((0, 0, -0.7), 0.75, 0.04, "Wood_Honey", n=12, m=5)
    for i in range(3):
        ang = tau * i / 3 + 0.5
        r.tube([(cos(ang) * 0.75, sin(ang) * 0.75, -0.7), (0, 0, 0)], 0.016, "Rope_Tan", n=3)
    knot(r, (0, 0, 0), 0.06)
    specs = [(0.0, 0, "Glow_Warm", 0.5, 1.2), (1.3, 1, "Glow_Pink", 0.95, 1.0), (2.5, 2, "Glow_Warm", 0.7, 1.05), (3.8, 0, "Glow_Teal", 1.2, 0.95), (5.0, 1, "Glow_Warm", 0.4, 0.9)]
    for i, (ang, st, gm, drop, sc_) in enumerate(specs):
        x, y = cos(ang) * 0.75, sin(ang) * 0.75
        top = lantern(gs[gm], w, (x, y, -0.7 - drop - 0.3), s=sc_, style=st, glow=gm, seed=i)
        r.tube([(x, y, -0.7), (x, y, top - 0.02)], 0.012, "Rope_Tan", n=3)
    top = lantern(gs["Glow_Warm"], w, (0, 0, -2.3), s=1.5, style=0)
    r.tube([(0, 0, 0), (0, 0, top - 0.02)], 0.016, "Rope_Tan", n=3)
    a.part("Hoop", w); a.part("Ropes", r)
    for gm, gb in gs.items():
        a.part(gm, gb, shadow=False)
    a.light("Light", (0, 0, -2.3), WARM, 80)
    a.light("Light_Teal", (cos(3.8) * 0.75, sin(3.8) * 0.75, -2.2), TEAL, 20)


@asset("Lantern_Wall_Sconce", LI, ("light", "lantern", "wall", "sconce"), "Small lantern on a curled wooden bracket for walls, posts and trunks. Origin: where the bracket meets the wall; lantern hangs out toward -Y.")
def _(a):
    w, g, r = B(), B(), B()
    w.tube([(0, 0.05, -0.25), (0, -0.05, 0.1), (0, -0.35, 0.32), (0, -0.62, 0.25), (0, -0.7, 0.12)], [0.05, 0.045, 0.04, 0.03, 0.02], "Wood_Dark", n=5)
    w.lathe([(0.0, -0.03), (0.12, -0.03), (0.12, 0.03), (0.0, 0.03)], "Wood_Honey", n=8, M=T(0, 0.02, -0.05) @ RX(pi / 2))
    top = lantern(g, w, (0, -0.62, -0.25), s=0.9, style=2)
    r.tube([(0, -0.62, 0.24), (0, -0.62, top - 0.02)], 0.012, "Rope_Tan", n=3)
    a.part("Bracket", w); a.part("Glow", g, shadow=False); a.part("Rope", r)
    a.light("Light", (0, -0.62, -0.25), WARM, 30)


@asset("Jar_Fireflies", LI, ("light", "jar", "fireflies", "magic", "small"), "Three glass jars of fireflies on a little shelf-stump - a faint teal-gold glow for corners and window sills. Origin: base.")
def _(a):
    w, g, f = B(), B(), B()
    w.lathe([(0.3, 0.0), (0.26, 0.3), (0.0, 0.32)], "Wood_Dark", n=8, wob=0.06, seed=1)
    for i, (x, y, s_) in enumerate(((0, 0, 1.0), (0.42, 0.1, 0.75), (-0.35, 0.22, 0.6))):
        z0 = 0.32 if i == 0 else 0.0
        g.lathe([(0.1 * s_, 0.0), (0.14 * s_, 0.04 * s_), (0.14 * s_, 0.26 * s_), (0.09 * s_, 0.32 * s_), (0.09 * s_, 0.36 * s_)], "Glass_Warm", n=8, c=(x, y, z0))
        w.lathe([(0.1 * s_, 0.36 * s_), (0.1 * s_, 0.4 * s_), (0.0, 0.41 * s_)], "Wood_Pale", n=8, c=(x, y, z0))
        for k in range(4):
            f.blob((x + cos(k * 1.9) * 0.06 * s_, y + sin(k * 1.9) * 0.06 * s_, z0 + (0.1 + 0.05 * k) * s_), 0.022, "Glow_Teal" if k % 2 else "Glow_Warm", n=6, seed=i * 4 + k)
    a.part("Wood", w); a.part("Jars", g, shadow=False); a.part("Fireflies", f, shadow=False)
    a.light("Light", (0, 0, 0.5), (0.8, 1.0, 0.7), 10)
