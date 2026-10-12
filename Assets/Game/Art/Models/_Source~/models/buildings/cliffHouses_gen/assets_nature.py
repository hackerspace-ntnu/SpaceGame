"""Cliff stones, trees, bushes, grass, flowers."""
from chparts import *

CL, TR, PL = "Cliff & Rocks", "Trees", "Plants"
# (top, lit, lit2, shade, deep)
PEACH = ("Rock_Cream", "Rock_Peach", "Rock_Rose", "Rock_Blue", "Rock_Slate")
ROSE = ("Rock_Cream", "Rock_Rose", "Rock_Peach", "Rock_Slate", "Rock_Slate")
OCHRE = ("Rock_Cream", "Rock_Ochre", "Rock_Peach", "Rock_Blue", "Rock_Slate")
LILAC = ("Rock_Cream", "Rock_Peach", "Rock_Ochre", "Rock_Lilac", "Rock_Slate")
GREY = ("Rock_GreyLight", "Rock_Grey", "Rock_GreyLight", "Rock_GreyDark", "Rock_GreyDark")
GREENS = ("Leaf_Shade", "Leaf_Green", "Grass")


def slabs(b, hx, hy, z0, z1, count, seed, mats, alt=None, wmin=1.0, wmax=2.4, hmin=0.25, hmax=0.6, g=None, t=None, out=0.9):
    """Angular slabs leaning against a cliff mass, like split-off sheets of rock. Optional moss + tufts on their tops."""
    rnd = random.Random(seed)
    H = z1 - z0
    for j in range(count):
        th = tau * (j + rnd.uniform(-0.35, 0.35)) / count
        w, d = rnd.uniform(wmin, wmax), rnd.uniform(0.7, 1.4)
        h = H * rnd.uniform(hmin, hmax)
        zb = z0 + (0 if rnd.random() < 0.6 else rnd.uniform(0.1, 0.45) * H)
        x, y = cos(th) * hx * out, sin(th) * hy * out
        tilt = rnd.uniform(0.04, 0.2)
        M = T(x, y, zb + h / 2) @ RZ(th) @ RY(-tilt) @ RZ(rnd.uniform(-0.25, 0.25))
        b.rock((0, 0, 0), (d, w, h), alt if (alt and rnd.random() < 0.35) else mats, n=16, seed=rnd.randint(0, 99999), p=4.5, irr=0.1,
               taper=-rnd.uniform(0.15, 0.45), M=M)
        if g is not None and rnd.random() < 0.55:
            top = M @ Vector((0, 0, h / 2 * 0.92))
            g.blob(top, (0.45, 0.4, 0.2), "Leaf_Green", n=10, seed=rnd.randint(0, 9999), shade=leaf_shade(GREENS, j))
            if t is not None:
                tuft(t, (top.x, top.y, top.z + 0.08), n=5, h=0.38, seed=rnd.randint(0, 9999), mat="Grass" if j % 2 else "Leaf_Lime")


def rubble(b, hx, hy, z, count, seed, mats):
    rnd = random.Random(seed)
    for j in range(count):
        th = rnd.uniform(0, tau)
        s = rnd.uniform(0.5, 1.2)
        b.rock((cos(th) * hx * 1.05, sin(th) * hy * 1.05, z + s * 0.3), (s * 1.3, s, s * 0.8), mats, n=12, seed=rnd.randint(0, 99999), p=2.8, irr=0.15, zbot=-s * 0.3)


def grass_cap(g, t, outline, z, seed, fl=None, flh=None):
    """Grass blanket following a cliff-top outline, with lumps spilling over the edge, tufts and flowers."""
    rnd = random.Random(seed)
    n = len(outline)
    cx = sum(p.x for p in outline) / n
    cy = sum(p.y for p in outline) / n
    sh = leaf_shade(GREENS, seed)
    V = [(cx, cy, z + 0.28)]
    ring2, ring1, ring0 = [], [], []
    for p in outline:
        dx, dy = p.x - cx, p.y - cy
        ring2.append(len(V)); V.append((cx + dx * 0.55, cy + dy * 0.55, z + 0.24 + rnd.uniform(-0.04, 0.06)))
    for p in outline:
        dx, dy = p.x - cx, p.y - cy
        ring1.append(len(V)); V.append((cx + dx * 1.0, cy + dy * 1.0, z + 0.14 + rnd.uniform(-0.03, 0.04)))
    for p in outline:
        dx, dy = p.x - cx, p.y - cy
        ring0.append(len(V)); V.append((cx + dx * 1.04, cy + dy * 1.04, z - 0.3))
    for p in outline:
        dx, dy = p.x - cx, p.y - cy
        V.append((cx + dx * 0.5, cy + dy * 0.5, z - 0.3))
    F, FM = [], []
    for i in range(n):
        j = (i + 1) % n
        F.append((0, ring2[i], ring2[j])); FM.append("Grass")
        F.append((ring2[i], ring1[i], ring1[j], ring2[j])); FM.append("Grass" if rnd.random() < 0.8 else "Leaf_Lime")
        F.append((ring1[i], ring0[i], ring0[j], ring1[j])); FM.append("Leaf_Green")
        F.append((ring0[i], ring0[i] + n, ring0[j] + n, ring0[j])); FM.append("Leaf_Shade")
    F.append(tuple(ring0[i] + n for i in range(n))); FM.append("Leaf_Shade")
    g.add(V, F, FM)
    for i in range(0, n, 2):
        p = outline[i]
        if rnd.random() < 0.8:
            s = rnd.uniform(0.4, 0.85)
            dx, dy = p.x - cx, p.y - cy
            g.blob((cx + dx * 1.02, cy + dy * 1.02, z - 0.15 - s * 0.25), (s, s, s * rnd.uniform(0.7, 1.3)), "Leaf_Green", n=11, seed=rnd.randint(0, 9999), shade=sh)
    for i in range(7):
        k = rnd.randrange(n)
        f = rnd.uniform(0.15, 0.7)
        s = rnd.uniform(0.7, 1.3)
        g.blob((cx + (outline[k].x - cx) * f, cy + (outline[k].y - cy) * f, z + 0.2), (s, s * 0.85, 0.22), "Grass", n=12, seed=rnd.randint(0, 9999), zmin=z + 0.1,
               shade=lambda nrm, ctr: "Leaf_Lime" if nrm.z > 0.8 else "Grass")
    area = sum(abs(outline[i].x * outline[(i + 1) % n].y - outline[(i + 1) % n].x * outline[i].y) for i in range(n)) / 2
    for i in range(int(area / 5)):
        k = rnd.randrange(n)
        f = sqrt(rnd.random()) * 0.85
        x, y = cx + (outline[k].x - cx) * f, cy + (outline[k].y - cy) * f
        zz = z + 0.14 + 0.1 * (1 - f)
        if fl is not None and i % 4 == 0:
            flower(fl, flh, (x, y, zz), h=0.35, head=rnd.choice(("Flower_Yellow", "Flower_White", "Flower_Red", "Flower_Purple")), seed=rnd.randint(0, 9999))
        else:
            tuft(t, (x, y, zz), n=6, h=rnd.uniform(0.35, 0.55), seed=rnd.randint(0, 9999), mat="Leaf_Lime" if i % 2 else "Leaf_Green")


# Plain, clean rock masses: an even quad grid (rings x columns) with gentle facets and no spikes, slabs or rubble,
# so they are easy to reshape by hand. SIMPLE holds the shared settings.
SIMPLE = dict(col_amp=0.05, band_amp=0.02, noise=0.015)


@asset("Cliff_Block_Tall", CL, ("cliff", "rock", "stone", "modular"), "Tall plain cliff block - a clean, evenly gridded rock mass with flat top and bottom so blocks stack, meant to be reshaped by hand. Origin: bottom centre.")
def _(a):
    b = B()
    b.crag((0, 0, 5.2), (6.2, 5.2, 10.4), PEACH, seed=11, na=24, nz=10, **SIMPLE)
    a.part("Rock", b)


@asset("Cliff_Block_Wide", CL, ("cliff", "rock", "stone", "modular"), "Wide, squat plain cliff block in rose stone. Flat top and bottom. Origin: bottom centre.")
def _(a):
    b = B()
    b.crag((0, 0, 2.7), (10.4, 6.4, 5.4), ROSE, seed=21, na=28, nz=6, **SIMPLE)
    a.part("Rock", b)


@asset("Cliff_Slab_Leaning", CL, ("cliff", "rock", "slab", "grey"), "Plain grey leaning slab with a smaller one beside it - prop against a cliff face or stand alone. Origin: bottom centre.")
def _(a):
    b, b2 = B(), B()
    b.crag((0, 0, 4.2), (3.0, 5.2, 8.4), GREY, seed=31, na=18, nz=8, lean=(0.2, 0.0), prof=lambda t_: 1.0 - 0.4 * t_ ** 1.5, p=4.0, **SIMPLE)
    b2.crag((-2.3, 1.1, 2.6), (2.0, 3.0, 5.2), GREY, seed=32, na=14, nz=6, lean=(0.28, 0.04), prof=lambda t_: 1.0 - 0.45 * t_ ** 1.4, p=4.0, **SIMPLE)
    a.part("Rock", b)
    a.part("RockSmall", b2)


@asset("Cliff_Spire", CL, ("cliff", "rock", "spire"), "Slender plain rock needle with a blunt top, 12 m author. Origin: bottom centre.")
def _(a):
    b = B()
    b.crag((0, 0, 6.2), (3.6, 3.2, 12.4), LILAC, seed=41, na=16, nz=10, lean=(0.03, -0.02), prof=lambda t_: 1.0 - 0.7 * t_ ** 1.3, cap_in=0.45, **SIMPLE)
    a.part("Rock", b)


@asset("Cliff_Cap_Grass", CL, ("cliff", "rock", "grass", "top", "modular"), "Flat grassy cliff top to finish a stack of blocks - build the town on this. Plain rock rim; grass, tufts and flowers are separate parts. Origin: centre of the top surface.")
def _(a):
    b, g, t, fs, fh = B(), B(), B(), B(), B()
    top = b.crag((0, 0, -1.7), (11.0, 9.0, 3.4), PEACH, seed=51, na=30, nz=4, prof=lambda t_: 0.86 + 0.14 * t_, **SIMPLE)
    grass_cap(g, t, top, 0.0, 53, fs, fh)
    a.part("Rock", b); a.part("Grass", g); a.part("Tufts", t); a.part("Stems", fs); a.part("Flowers", fh)


@asset("Cliff_Ledge_Grass", CL, ("cliff", "rock", "ledge", "grass"), "Small grassy rock shelf that juts out of a cliff face; plain rock underneath. Origin: back edge at the top surface.")
def _(a):
    b, g, t, fs, fh = B(), B(), B(), B(), B()
    top = b.crag((0, -2.1, -1.4), (5.6, 4.4, 2.8), ROSE, seed=61, na=20, nz=5, prof=lambda t_: 0.35 + 0.65 * t_ ** 0.7, cap_in=0.4, **SIMPLE)
    grass_cap(g, t, top, 0.0, 63, fs, fh)
    a.part("Rock", b); a.part("Grass", g); a.part("Tufts", t); a.part("Stems", fs); a.part("Flowers", fh)


@asset("Cliff_Underside", CL, ("cliff", "rock", "floating", "island", "underside"), "Plain tapering underside of a sky island, rounded at the bottom - no teeth, ready to be shaped. Put it under a Cliff_Cap_Grass or a block. Origin: centre of the flat top.")
def _(a):
    b = B()
    b.crag((0, 0, -4.5), (10.2, 9.0, 9.0), PEACH, seed=71, na=28, nz=9, prof=lambda t_: 0.16 + 0.84 * t_ ** 0.7, cap_in=0.35, **SIMPLE)
    a.part("Rock", b)


def bulb_prof(t):
    if t < 0.4:
        return 0.6 + 0.07 * sin(pi * t / 0.4)
    u = min(1.0, (t - 0.4) / 0.3)
    return 0.6 + 0.4 * (u * u * (3 - 2 * u)) - (0.07 * ((t - 0.9) / 0.1) if t > 0.9 else 0.0)


@asset("Cliff_Stack_Bulb", CL, ("cliff", "rock", "stack", "pillar"), "Free-standing plain rock stack that swells toward a grassy top - the leaning sea-stack from the reference. Origin: bottom centre.")
def _(a):
    b, g, t, fs, fh = B(), B(), B(), B(), B()
    top = b.crag((0, 0, 6.0), (5.6, 5.0, 12.0), PEACH, seed=91, na=22, nz=12, lean=(0.035, 0.01), prof=bulb_prof, **SIMPLE)
    grass_cap(g, t, top, 12.0, 95, fs, fh)
    a.part("Rock", b); a.part("Grass", g); a.part("Tufts", t); a.part("Stems", fs); a.part("Flowers", fh)


@asset("Rock_Boulders", CL, ("rock", "boulder", "small"), "Five plain loose boulders in mixed stone, each its own object. Origin: ground centre.")
def _(a):
    specs = [((0, 0), (1.9, 1.6, 1.4), PEACH), ((1.5, 0.5), (1.2, 1.1, 0.9), GREY), ((-1.2, -0.7), (0.9, 1.0, 0.7), ROSE),
             ((0.6, -1.2), (0.7, 0.6, 0.45), OCHRE), ((-0.9, 0.9), (0.6, 0.7, 0.4), GREY)]
    for i, ((x, y), sz, m) in enumerate(specs):
        b = B()
        b.crag((x, y, sz[2] * 0.42), sz, m, seed=100 + i, na=10, nz=4, col_amp=0.05, band_amp=0.02, noise=0.03,
               prof=lambda t_: 0.55 + 0.45 * sin(pi * (0.12 + 0.8 * t_)), p=2.6, cap_in=0.5)
        a.part("Boulder%d" % (i + 1), b, origin=(x, y, sz[2] * 0.42))


# ================================================================ TREES
def tree(a, seed, H, tr, lean, bark, mats, limbs, depth, length, spread, up, padR, zs, lumps, gnarl=0.25, sx=1.0, bias=None, whorls=None,
         low_limbs=1, kids=(2, 2, 3)):
    rnd = random.Random(seed)
    w, l = B(), B()
    pts, rad = trunk(w, H, tr, lean, bark, rnd, gnarl=0.06 + gnarl * 0.15)
    top = pts[-1]
    tips = []
    r_top = rad[-1]
    if whorls:
        # central leader keeps going; rings of side branches at several heights
        leader = [top + Vector((rnd.uniform(-0.1, 0.1), rnd.uniform(-0.1, 0.1), 0))]
        for (frac, cnt, ln) in whorls:
            base = pts[int(frac * (len(pts) - 1))]
            for j in range(cnt):
                ang = tau * j / cnt + rnd.uniform(0, 1.5)
                limb(w, base, (cos(ang), sin(ang), 0.35), ln, tr * 0.42, depth, rnd, tips, bark, gnarl, up, spread, kids)
        limb(w, top, (0, 0, 1), length, r_top, 1, rnd, tips, bark, gnarl * 0.6, 0.5, spread * 0.6, kids)
    else:
        for j in range(limbs):
            ang = tau * j / limbs + rnd.uniform(-0.35, 0.35)
            limb(w, top, (cos(ang) * 0.75, sin(ang) * 0.75, 0.75), length, r_top * 0.82, depth, rnd, tips, bark, gnarl, up, spread, kids, bias=bias)
        for j in range(low_limbs):
            base = pts[int(len(pts) * (0.5 + 0.12 * j))]
            ang = rnd.uniform(0, tau)
            limb(w, base, (cos(ang), sin(ang), 0.45), length * 0.8, tr * 0.4, max(0, depth - 1), rnd, tips, bark, gnarl, up, spread, kids, bias=bias)
    rz = atan2(bias.y, bias.x) if bias is not None else 0.0
    for i, (p, dv, r) in enumerate(tips):
        R = padR * rnd.uniform(0.8, 1.2)
        pad(l, p + Vector((0, 0, R * zs * 0.15)), R, zs, mats, seed=seed * 100 + i, lumps=lumps, sx=sx, rz=rz if bias is not None else rnd.uniform(0, tau))
    a.part("Trunk", w)
    a.part("Leaves", l)


@asset("Tree_Oak_Green", TR, ("tree", "oak", "green"), "Broad leafy oak in fresh green: gnarled trunk with root flares, forked limbs and a crown of cloud-shaped leaf pads. About 7 m author / 11 m real. Origin: base of trunk.")
def _(a):
    tree(a, 1, 3.2, 0.36, (0.3, 0.1), "Bark_Warm", ("Leaf_Shade", "Leaf_Green", "Leaf_Lime"), limbs=4, depth=2, length=1.9, spread=0.62, up=0.2,
         padR=1.15, zs=0.62, lumps=4)


@asset("Tree_Oak_Golden", TR, ("tree", "oak", "yellow", "umbrella"), "Tall golden oak with a bare twisting trunk and wide, flat, layered cloud pads on spreading branches - the cliff-top tree from the reference. Origin: base of trunk.")
def _(a):
    tree(a, 2, 5.0, 0.3, (0.8, -0.3), "Bark_Plum", ("Leaf_Olive", "Leaf_Lime", "Leaf_Yellow"), limbs=5, depth=2, length=2.5, spread=0.78, up=0.06,
         padR=1.35, zs=0.34, lumps=5, gnarl=0.3, low_limbs=2)


@asset("Tree_Oak_Blossom", TR, ("tree", "oak", "pink", "blossom"), "Small twisting oak smothered in pink blossom clouds. Origin: base of trunk.")
def _(a):
    tree(a, 3, 2.3, 0.26, (-0.4, 0.3), "Bark_Plum", ("Leaf_Rosewood", "Leaf_Pink", "Leaf_Blush"), limbs=4, depth=2, length=1.5, spread=0.75, up=0.14,
         padR=0.82, zs=0.75, lumps=4, gnarl=0.4)


@asset("Tree_Oak_Teal", TR, ("tree", "oak", "teal", "tall"), "Tall teal oak: a straight leader with whorls of branches, each carrying leaf clouds, narrowing to the top. Origin: base of trunk.")
def _(a):
    tree(a, 4, 6.4, 0.32, (0.15, 0.2), "Bark_Warm", ("Leaf_Pine", "Leaf_Teal", "Leaf_Aqua"), limbs=0, depth=1, length=1.3, spread=0.6, up=0.12,
         padR=1.0, zs=0.6, lumps=4, whorls=[(0.45, 4, 2.0), (0.62, 4, 1.7), (0.8, 3, 1.4), (0.93, 3, 1.0)], kids=(2, 2))


@asset("Tree_Oak_Amber", TR, ("tree", "oak", "orange", "windswept", "cliff"), "Wind-bent amber oak leaning hard out over a cliff edge, crown streaming downwind in flat layers. Origin: base of trunk; leans toward +X.")
def _(a):
    tree(a, 5, 3.0, 0.3, (2.3, 0.4), "Bark_Warm", ("Leaf_Rust", "Leaf_Orange", "Leaf_Yellow"), limbs=4, depth=2, length=2.0, spread=0.55, up=0.05,
         padR=1.1, zs=0.34, lumps=4, gnarl=0.3, sx=1.5, bias=Vector((0.32, 0.05, 0.0)))


# ================================================================ PLANTS
def bush(a, seed, mats, lumps, twigs=True):
    l, w = B(), B()
    rnd = random.Random(seed)
    sh = leaf_shade(mats, seed)
    for i, (x, y, z, r) in enumerate(lumps):
        l.blob((x, y, z), (r, r * rnd.uniform(0.85, 1.0), r * 0.85), mats[1], n=16, seed=seed * 20 + i, zmin=0.0, shade=sh)
        for k in range(2):
            ang = rnd.uniform(0, tau)
            l.blob((x + cos(ang) * r * 0.55, y + sin(ang) * r * 0.55, z + r * 0.45), r * 0.45, mats[2], n=10, seed=seed * 40 + i * 3 + k, shade=sh)
        if twigs:
            w.tube([(x * 0.3, y * 0.3, -0.05), (x * 0.7, y * 0.7, z * 0.6), (x, y, z)], [0.05, 0.035, 0.02], "Bark_Warm", n=4)
    a.part("Leaves", l)
    a.part("Twigs", w)
    return rnd


@asset("Bush_Round", PL, ("bush", "green"), "Rounded green bush built from leafy lumps in three tones. Origin: ground centre.")
def _(a):
    bush(a, 1, ("Leaf_Shade", "Leaf_Green", "Leaf_Lime"),
         [(0, 0, 0.55, 0.72), (0.62, 0.2, 0.42, 0.55), (-0.58, 0.3, 0.4, 0.5), (0.1, -0.58, 0.36, 0.5), (-0.2, 0.5, 0.75, 0.42), (0.45, -0.4, 0.7, 0.38), (-0.5, -0.35, 0.3, 0.4)])


@asset("Bush_Flowering", PL, ("bush", "flowers", "pink"), "Teal bush speckled with pink and white blossoms. Origin: ground centre.")
def _(a):
    lumps = [(0, 0, 0.5, 0.7), (0.55, -0.2, 0.38, 0.5), (-0.5, -0.25, 0.36, 0.48), (0.05, 0.5, 0.4, 0.5), (0.4, 0.4, 0.7, 0.36), (-0.35, 0.3, 0.72, 0.36)]
    rnd = bush(a, 2, ("Leaf_Pine", "Leaf_Teal", "Leaf_Aqua"), lumps)
    f = B()
    for i, (x, y, z, r) in enumerate(lumps):
        for k in range(7):
            d = fib(11, rnd, 0.4)[k]
            if d.z < -0.05:
                continue
            f.blob((x + d.x * r * 0.98, y + d.y * r * 0.98, z + d.z * r * 0.85), 0.08, "Leaf_Pink" if k % 3 else "Flower_White", n=7, seed=rnd.randint(0, 999))
    a.part("Blossoms", f)


@asset("Grass_Tuft", PL, ("grass", "tuft"), "Single clump of bladed grass. Origin: ground centre.")
def _(a):
    g = B()
    tuft(g, (0, 0, 0), n=13, h=0.62, seed=1, mat="Grass", spread=0.15, r=0.04)
    tuft(g, (0.12, 0.08, 0), n=7, h=0.42, seed=2, mat="Leaf_Lime", spread=0.12, r=0.035)
    tuft(g, (-0.1, -0.06, 0), n=5, h=0.5, seed=3, mat="Leaf_Green", spread=0.1, r=0.035)
    a.part("Blades", g)


@asset("Grass_Patch", PL, ("grass", "patch", "meadow"), "Low grassy mound with many tufts and a few daisies - scatter to carpet cliff tops. Origin: ground centre.")
def _(a):
    m, g, bs, bh = B(), B(), B(), B()
    m.blob((0, 0, 0.0), (1.5, 1.2, 0.2), "Grass", n=22, seed=4, zmin=-0.03, shade=leaf_shade(GREENS, 4))
    rnd = random.Random(5)
    for i in range(18):
        ang, d = rnd.uniform(0, tau), rnd.uniform(0, 1.0)
        z = 0.18 * (1 - d * d * 0.8)
        tuft(g, (cos(ang) * d * 1.3, sin(ang) * d * 1.05, z), n=6, h=rnd.uniform(0.3, 0.55), seed=i, mat=("Leaf_Green", "Leaf_Lime", "Grass")[i % 3])
    for i in range(5):
        ang, d = rnd.uniform(0, tau), rnd.uniform(0.2, 0.9)
        flower(bs, bh, (cos(ang) * d * 1.2, sin(ang) * d, 0.12), h=0.4, head="Flower_White", centre="Flower_Yellow", seed=i + 30)
    a.part("Mound", m)
    a.part("Blades", g)
    a.part("Stems", bs)
    a.part("Flowers", bh)


def flower_cluster(a, heads, n, seed, h=0.45, bell=False, size=0.08, R=0.45):
    bs, bh, g = B(), B(), B()
    rnd = random.Random(seed)
    for i in range(n):
        ang, d = rnd.uniform(0, tau), sqrt(rnd.random()) * R
        flower(bs, bh, (cos(ang) * d, sin(ang) * d, 0), h=h * rnd.uniform(0.7, 1.2), head=heads[i % len(heads)], seed=seed * 50 + i, size=size * rnd.uniform(0.85, 1.2), bell=bell,
               centre="Flower_Yellow" if heads[i % len(heads)] != "Flower_Yellow" else "Flower_White")
    for i in range(4):
        ang = tau * i / 4 + seed
        tuft(g, (cos(ang) * R * 0.5, sin(ang) * R * 0.5, 0), n=5, h=0.22, seed=seed * 9 + i, mat="Leaf_Green" if i % 2 else "Leaf_Lime", spread=0.1)
    a.part("Stems", bs)
    a.part("Flowers", bh)
    a.part("Leaves", g)


@asset("Flowers_Sunny", PL, ("flowers", "yellow", "white"), "Cluster of yellow and white daisies. Origin: ground centre.")
def _(a):
    flower_cluster(a, ["Flower_Yellow", "Flower_Yellow", "Flower_White"], 11, 1)


@asset("Flowers_Poppy", PL, ("flowers", "red", "orange"), "Cluster of red and orange poppies on taller stems. Origin: ground centre.")
def _(a):
    flower_cluster(a, ["Flower_Red", "Leaf_Orange", "Flower_Red", "Leaf_Pink"], 10, 2, h=0.6, size=0.1)


@asset("Flowers_Bells", PL, ("flowers", "purple", "blue", "bell"), "Tall stalks of purple and blue bell flowers. Origin: ground centre.")
def _(a):
    flower_cluster(a, ["Flower_Purple", "Flower_Blue", "Flower_Purple"], 10, 3, h=0.75, bell=True, size=0.09, R=0.4)


@asset("Vine_Hanging", PL, ("vine", "ivy", "hanging", "cliff"), "Curtain of leafy vines to drape over cliff edges, roofs and bridges. Origin: top centre; hangs down -Z along X.")
def _(a):
    s, l, f = B(), B(), B()
    rnd = random.Random(6)
    for i in range(9):
        x = -1.3 + i * 0.32 + rnd.uniform(-0.1, 0.1)
        ln = rnd.uniform(1.4, 3.8)
        pts = [Vector((x + rnd.uniform(-0.08, 0.08) * k, rnd.uniform(-0.06, 0.06) * k - 0.02 * k, -ln * k / 4)) for k in range(5)]
        pts[0].z = 0.08
        s.tube(pts, [0.03, 0.026, 0.022, 0.018, 0.01], "Leaf_Shade", n=3)
        for k in range(int(ln / 0.28)):
            t = (k + 0.5) / (ln / 0.28)
            idx = min(3, int(t * 4))
            p = pts[idx].lerp(pts[idx + 1], t * 4 - idx)
            ang = rnd.uniform(0, tau)
            sz = rnd.uniform(0.1, 0.16)
            c = p + Vector((cos(ang) * 0.08, -0.05 - abs(sin(ang)) * 0.05, 0))
            l.hull([c + Vector((0, 0, sz * 0.6)), c + Vector((sz, 0, 0)), c + Vector((-sz, 0, 0)), c + Vector((0, -0.05, -sz)), c + Vector((0, 0.04, -sz * 0.3)), c + Vector((0, -0.07, 0))],
                   ("Leaf_Green", "Leaf_Lime", "Leaf_Shade")[k % 3])
        if i % 2 == 0:
            f.blob(pts[-1] + Vector((0, -0.04, 0)), 0.07, "Flower_Purple" if i % 4 else "Flower_White", n=7, seed=i)
    s.tube([(-1.5, 0, 0.05), (0, 0.03, 0.1), (1.5, 0, 0.05)], 0.04, "Leaf_Shade", n=4)
    a.part("Stems", s)
    a.part("Leaves", l)
    a.part("Buds", f)
