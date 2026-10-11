"""Building kit: foundations, walls, roofs, doors, windows - the reusable parts the houses are made of."""
from chparts import *

RS, RL, RT = 2.2, 3.4, 2.0          # wall radii: small, large, tower
HS, HL, HT = 2.6, 3.0, 5.2          # wall heights
K = "Building Kit"


def door_open(a=-pi / 2):
    return [(a, 0.6, 0.0, 0.33), (a, 1.0, 0.33, 1.63), (a, 0.6, 1.63, 1.96)]


def win_open(a, zc, hw=0.37):
    return (a, hw, zc - hw, zc + hw)


WIN_S = [0.35, pi - 0.35, pi / 2]
WIN_L = [0.3, pi - 0.3, pi / 2 - 0.55, pi / 2 + 0.55]


def bulb_r(z):
    return RS * (0.9 + 0.2 * sin(pi * min(1, z / HS) ** 0.85))


def tall_r(z):
    return RT - 0.05 * z


# ---------------------------------------------------------------- foundations
def foundation(a, R, seed):
    b = B()
    b.lathe([(R + 0.5, -0.75), (R + 0.72, -0.45), (R + 0.62, -0.12), (R + 0.45, 0.0), (R - 0.3, 0.0)],
            ["Stone_Cool", "Stone_Warm", "Stone_Warm", "Stone_Warm"], n=14, wob=0.035, jit=0.012, seed=seed)
    stone_ring(b, R + 0.7, 9, 0.3, z=-0.55, seed=seed + 1)
    a.part("Base", b)
    f = B()
    f.lathe([(R - 0.05, -0.2), (R - 0.05, 0.03), (0.9, 0.035), (0.9, -0.2)], "Wood_Pale", n=14)
    f.lathe([(0.9, -0.2), (0.9, 0.04), (0.0, 0.045)], "Clay_Terracotta", n=14)
    a.part("Floor", f)
    s = B()
    s.lathe([(0.95, -0.3), (1.0, -0.12), (0.85, -0.06)], "Stone_Cool", n=7, c=(0, -(R + 0.75), -0.12), wob=0.08, seed=seed)
    s.lathe([(0.6, -0.3), (0.62, -0.1), (0.5, -0.06)], "Stone_Warm", n=6, c=(0.25, -(R + 1.55), -0.3), wob=0.1, seed=seed + 3)
    a.part("Step", s)


@asset("Foundation_Round_S", K, ("foundation", "stone", "round"), "Stone base for small round walls (r 2.2 m author / 3.7 m real). Origin: floor centre.")
def _(a):
    foundation(a, RS, 1)


@asset("Foundation_Round_L", K, ("foundation", "stone", "round"), "Stone base for large round walls. Origin: floor centre.")
def _(a):
    foundation(a, RL, 2)


@asset("Foundation_Stilts", K, ("foundation", "wood", "stilts", "cliff"), "Round wooden deck on leaning stilts for houses built off a cliff edge. Origin: deck centre.")
def _(a):
    R = RS + 1.0
    d = B()
    planks_round(d, R, seed=3, z=0.0)
    a.part("Deck", d)
    w = B()
    w.lathe([(R - 0.1, -0.3), (R + 0.08, -0.3), (R + 0.08, -0.06), (R - 0.1, -0.06)], "Wood_Dark", n=16, loop=True)
    for x in (-1.3, 0, 1.3):
        hl = sqrt(R * R - x * x) - 0.1
        w.beam((x, -hl, -0.17), (x, hl, -0.17), 0.16, "Wood_Dark")
    rnd = random.Random(5)
    r = B()
    for i in range(6):
        ang = tau * i / 6 + 0.2
        top = Vector((cos(ang) * (R - 0.5), sin(ang) * (R - 0.5), -0.2))
        bot = Vector((cos(ang) * (R + 0.1) + rnd.uniform(-0.2, 0.2), sin(ang) * (R + 0.1) + rnd.uniform(-0.2, 0.2), -4.2 - rnd.uniform(0, 0.8)))
        mid = top.lerp(bot, 0.5) + Vector((rnd.uniform(-0.1, 0.1), rnd.uniform(-0.1, 0.1), 0))
        w.tube([top, mid, bot], [0.11, 0.12, 0.14], "Wood_Rose" if i % 2 else "Wood_Honey", n=6)
        nxt = tau * (i + 1) / 6 + 0.2
        t2 = Vector((cos(nxt) * (R - 0.5), sin(nxt) * (R - 0.5), -0.35))
        if i % 2 == 0:
            w.tube([mid, t2], 0.055, "Wood_Dark", n=5)
        rope_wrap(r, (mid.x, mid.y, mid.z - 0.05), 0.12, turns=3)
    a.part("Frame", w)
    a.part("Lashings", r)


# ---------------------------------------------------------------- walls
def wall_asset(a, rfun, h, mo, mi, band, wins, n=20, zs_extra=(), zc=1.45, tall=False):
    b = B()
    ops = door_open() + [win_open(x, zc) for x in wins]
    if tall:
        ops += [win_open(x, 3.9) for x in (-pi / 2, 0.2, pi + 0.3)] + [(pi / 2 + 0.3, 0.5, 2.74, 4.5)]
    wall(b, rfun, h, mo, mi, n=n, openings=ops, band=band, zs_extra=zs_extra)
    a.part("Wall", b)
    t = B()
    rt = rfun(h)
    t.lathe([(rt - 0.26, h - 0.14), (rt + 0.08, h - 0.14), (rt + 0.08, h + 0.04), (rt - 0.26, h + 0.04)], "Wood_Dark", n=n, loop=True)
    if tall:
        rm = rfun(2.7)
        t.lathe([(rm - 0.02, 2.58), (rm + 0.09, 2.58), (rm + 0.09, 2.74), (rm - 0.02, 2.74)], "Wood_Dark", n=n, loop=True)
        # joist stubs poking out, to carry a balcony
        for i in range(8):
            ang = tau * i / 8 + 0.2
            t.beam((cos(ang) * (rm - 0.3), sin(ang) * (rm - 0.3), 2.5), (cos(ang) * (rm + 0.35), sin(ang) * (rm + 0.35), 2.5), 0.13, "Wood_Honey")
    a.part("Beams", t)
    if tall:
        f = B()
        f.lathe([(rfun(2.6) - 0.19, 2.6), (rfun(2.74) - 0.19, 2.745), (0.0, 2.75)], "Wood_Pale", n=n)
        a.part("UpperFloor", f)


@asset("Wall_Round_S", K, ("wall", "round", "plaster"), "Small round plaster wall with a round-door opening (-Y) and three window openings. Sits on Foundation_Round_S.")
def _(a):
    wall_asset(a, lambda z: RS, HS, "Plaster_Cream", "Plaster_Peach", (0.55, "Clay_Terracotta"), WIN_S)


@asset("Wall_Round_L", K, ("wall", "round", "plaster"), "Large round wall, door opening at -Y, four window openings. Sits on Foundation_Round_L.")
def _(a):
    wall_asset(a, lambda z: RL, HL, "Plaster_Peach", "Plaster_Cream", (0.6, "Clay_Teal"), WIN_L, n=28, zc=1.55)


@asset("Wall_Round_Bulb", K, ("wall", "round", "clay", "pot"), "Pot-bellied clay wall - bulges outward like a vase. Same footprint as Wall_Round_S.")
def _(a):
    wall_asset(a, bulb_r, HS, "Plaster_Mint", "Plaster_Cream", (0.55, "Clay_Lavender"), WIN_S,
               zs_extra=[0.9, 1.3, 2.1, 2.35])


@asset("Wall_Round_Tall", K, ("wall", "round", "tower"), "Two-storey tapered tower wall with a joist ring at 2.7 m for Balcony_Ring.")
def _(a):
    wall_asset(a, tall_r, HT, "Plaster_Cream", "Plaster_Mint", (0.55, "Clay_Pink"), WIN_S, tall=True, zs_extra=[2.6])


# ---------------------------------------------------------------- roofs
def straw_roof(a, Re, seed):
    k = Re / 3.1
    th = B()
    rnd = random.Random(seed)
    NT = 5
    for i in range(NT):
        u0, u1 = i / NT, (i + 1.35) / NT
        rb, zb = 3.1 * (1 - u0) ** 0.85 + 0.25 * u0, 3.0 * u0 ** 1.15
        rt, zt = max(0.0, 3.1 * (1 - min(1, u1)) ** 0.85), 3.0 * min(1, u1) ** 1.15 + 0.12
        rb, zb, rt, zt = rb * k, zb * k, rt * k, zt * k
        m = "Straw_Gold" if i % 2 == 0 else "Straw_Dark"
        prof = [(0.0, zb + 0.22 * k)] if i == 0 else [(rb * 0.5, zb + 0.3 * k)]
        prof += [(rb * 0.9, zb + 0.02), (rb, zb - 0.13 * k), (rb * 0.97, zb + 0.1 * k)]
        if i < NT - 1:
            prof += [(max(rt, 0.3 * k), zt)]
        else:
            prof += [(0.24 * k, zb + 0.75 * k), (0.11 * k, zb + 1.25 * k), (0.0, zb + 1.5 * k)]
        th.lathe(prof, [m, "Straw_Dark", m, m, m, m], n=18, jit=0.04, wob=0.03, seed=seed + i, a0=i * 0.3)
        # loose straw tufts hanging off each eave
        for j in range(9 if i < 3 else 5):
            ang = rnd.uniform(0, tau)
            th.lathe([(0.07 * k, 0.06), (0.1 * k, -0.1 * k), (0.0, -0.3 * k - rnd.uniform(0, 0.12))], "Straw_Dark" if j % 2 else "Straw_Gold", n=4,
                     c=(cos(ang) * rb * 0.97, sin(ang) * rb * 0.97, zb - 0.06 * k))
    a.part("Thatch", th)
    kn = B()
    ztop = 3.0 * ((NT - 1) / NT) ** 1.15 * k
    rope_wrap(kn, (0, 0, ztop + 0.72 * k), 0.24 * k, turns=3, r=0.03)
    kn.tube([(0, 0, ztop + 1.3 * k), (0.05, 0.02, ztop + 1.75 * k), (0.2, 0.05, ztop + 2.05 * k)], [0.05, 0.04, 0.02], "Wood_Dark", n=5)
    a.part("Knot", kn)


def courses(pts, m1, m2, step=0.05):
    """Turn a smooth (r, z) dome profile into stepped tile courses in two alternating clay tones."""
    prof, mats = [], []
    for i, (r, z) in enumerate(pts):
        prof.append((r, z))
        if i < len(pts) - 1:
            mats.append(m1 if i % 2 == 0 else m2)
            if r > 0.3:
                prof.append((r - step, z + step * 0.5))
                mats.append(m1 if i % 2 == 0 else m2)
    # the lip face between courses belongs to the lower course; reorder so each course = lip + face
    return prof, mats


@asset("Roof_Straw_Cone_S", K, ("roof", "straw", "thatch"), "Three-tier thatched cone for small round walls. Origin: eave line centre (wall top).")
def _(a):
    straw_roof(a, 3.1, 10)


@asset("Roof_Straw_Mushroom", K, ("roof", "straw", "thatch", "mushroom"), "Wide, low mushroom-cap thatch for large walls, with a rope-bound top bundle.")
def _(a):
    th = B()
    th.lathe([(0, 0.3), (3.7, 0.12), (4.75, -0.3), (4.65, 0.08), (4.0, 0.95), (3.0, 1.62)],
             ["Straw_Dark", "Straw_Dark", "Straw_Gold", "Straw_Gold", "Straw_Gold"], n=22, jit=0.03, wob=0.04, seed=31)
    th.lathe([(2.2, 1.5), (3.35, 1.25), (3.3, 1.5), (2.6, 1.95), (1.5, 2.3), (0.0, 2.45)], ["Straw_Dark", "Straw_Dark", "Straw_Gold", "Straw_Gold", "Straw_Gold"], n=20, jit=0.035, wob=0.04, seed=33)
    rnd = random.Random(34)
    for j in range(26):
        ang = rnd.uniform(0, tau)
        R_ = 4.68 if j % 3 else 3.3
        z_ = -0.3 if j % 3 else 1.28
        th.lathe([(0.09, 0.06), (0.12, -0.12), (0.0, -0.36 - rnd.uniform(0, 0.15))], "Straw_Dark" if j % 2 else "Straw_Gold", n=4, c=(cos(ang) * R_, sin(ang) * R_, z_))
    th.lathe([(0.9, 2.1), (1.75, 1.95), (1.7, 2.15), (0.9, 2.75), (0.3, 3.2), (0.0, 3.35)], "Straw_Dark", n=11, jit=0.04, seed=32)
    a.part("Thatch", th)
    kn = B()
    rope_wrap(kn, (0, 0, 2.95), 0.5, turns=2, r=0.035)
    a.part("Knot", kn)


@asset("Roof_Clay_Dome", K, ("roof", "clay", "dome"), "Terracotta dome with cream rim and teal finial for small walls.")
def _(a):
    d = B()
    arc = [(2.62 * cos(u) ** 0.9, 0.22 + 2.3 * sin(u)) for u in [pi / 2 * i / 8 for i in range(8)]] + [(0.0, 2.54)]
    cp, cm = courses(arc, "Clay_Terracotta", "Clay_Rust")
    d.lathe([(0, 0.15), (2.45, 0.02), (2.85, -0.06), (2.9, 0.12)] + cp, ["Clay_Cream"] * 4 + cm, n=18)
    a.part("Dome", d)
    f = B()
    f.lathe([(0.34, 2.4), (0.4, 2.52), (0.16, 2.62), (0.22, 2.8), (0.3, 2.95), (0.2, 3.12), (0.05, 3.3), (0.0, 3.6)], "Clay_Teal", n=8)
    for i in range(7):
        ang = tau * i / 7
        prof = [(2.62, 0.22), (2.5, 0.95), (1.95, 1.75), (1.05, 2.3), (0.3, 2.47)]
        f.tube([(cos(ang) * (r + 0.02), sin(ang) * (r + 0.02), z + 0.02) for r, z in prof], 0.06, "Clay_Cream", n=4)
    a.part("Trim", f)


@asset("Roof_Clay_Onion", K, ("roof", "clay", "onion"), "Teal onion dome with butter-yellow rim and brass finial for small / tower walls.")
def _(a):
    d = B()
    arc = [(2.5, 0.25), (2.34, 0.6), (2.32, 0.95), (2.42, 1.3), (2.46, 1.65), (2.36, 2.0), (2.12, 2.4), (1.72, 2.8), (1.25, 3.15),
           (0.8, 3.45), (0.45, 3.75), (0.22, 4.05), (0.0, 4.3)]
    cp, cm = courses(arc, "Clay_Teal", "Clay_TealDeep")
    d.lathe([(0, 0.15), (2.4, 0.02), (2.72, -0.05), (2.78, 0.12)] + cp, ["Clay_Butter"] * 4 + cm, n=18)
    a.part("Dome", d)
    f = B()
    f.blob((0, 0, 4.5), 0.2, "Metal_Brass", n=10, seed=4)
    f.lathe([(0.05, 4.6), (0.03, 5.1), (0.0, 5.2)], "Metal_Brass", n=5)
    f.ring((0, 0, 1.5), 2.47, 0.05, "Clay_Butter", n=14, m=4)
    a.part("Trim", f)


@asset("Roof_Clay_Tiered", K, ("roof", "clay", "tiered", "pagoda"), "Two flared pink clay tiers with a glowing lantern drum between them, for large walls.")
def _(a):
    d = B()
    cp, cm = courses([(4.4, 0.4), (3.85, 0.48), (3.3, 0.62), (2.85, 0.85), (2.5, 1.15), (2.25, 1.45), (2.05, 1.75)], "Clay_Pink", "Clay_Rosy", step=0.06)
    d.lathe([(0, 0.25), (3.9, 0.05), (4.55, 0.22)] + cp, ["Clay_Cream"] * 3 + cm, n=22)
    cp, cm = courses([(2.8, 2.6), (2.3, 2.72), (1.85, 2.92), (1.4, 3.22), (0.95, 3.62), (0.6, 4.0), (0.3, 4.35), (0.0, 4.5)], "Clay_Pink", "Clay_Rosy", step=0.05)
    d.lathe([(0.0, 2.3), (2.5, 2.3), (2.95, 2.45)] + cp, ["Clay_Cream"] * 3 + cm, n=22)
    a.part("Tiers", d)
    dr = B()
    dr.lathe([(1.85, 1.6), (1.85, 2.45)], "Plaster_Cream", n=16)
    for i in range(8):
        ang = tau * (i + 0.5) / 8
        dr.beam((cos(ang) * 1.88, sin(ang) * 1.88, 1.6), (cos(ang) * 1.88, sin(ang) * 1.88, 2.45), 0.14, "Wood_Dark")
    dr.lathe([(0.3, 4.3), (0.36, 4.45), (0.12, 4.6), (0.2, 4.85), (0.0, 5.3)], "Clay_Teal", n=8)
    a.part("Drum", dr)
    g = B()
    for i in range(8):
        ang = tau * i / 8
        g.box((cos(ang) * 1.86, sin(ang) * 1.86, 2.02), (0.1, 0.7, 0.5), "Glass_Warm", rot=(0, 0, ang))
    a.part("Glow", g, shadow=False)
    a.light("Light", (0, 0, 2.0), watts=40)


# ---------------------------------------------------------------- doors & windows
def ring_frame(b, r_in, r_out, y0, y1, zc, mat, n=16, seed=0):
    M = T(0, 0, zc) @ RX(-pi / 2)
    b.lathe([(r_in, y0), (r_out, y0), (r_out, y1), (r_in, y1)], mat, n=n, loop=True, M=M, wob=0.02, seed=seed)


@asset("Door_Round", K, ("door", "round", "wood"), "Round hobbit-style door in a chunky wooden ring. Origin: wall face at floor level, facing -Y. The leaf is a separate object hinged on its left edge.")
def _(a):
    f = B()
    ring_frame(f, 0.92, 1.27, -0.14, 0.5, 0.98, "Wood_Honey", seed=2)
    for i in range(8):
        ang = tau * i / 8 + 0.39
        f.blob((cos(ang) * 1.1, -0.16, 0.98 + sin(ang) * 1.1), 0.06, "Metal_Brass", n=7, seed=i)
    a.part("Frame", f)
    l = B()
    M = T(0, 0, 0.98) @ RX(-pi / 2)
    l.lathe([(0.0, 0.04), (0.88, 0.04), (0.88, 0.13), (0.0, 0.13)], "Clay_Teal", n=16, M=M)
    for x in (-0.45, 0.0, 0.45):
        hl = sqrt(0.84 ** 2 - x * x)
        l.box((x + 0.21, 0.035, 0.98), (0.03, 0.03, 2 * hl * 0.96), "Wood_Dark")
    l.box((0, 0.02, 1.4), (1.25, 0.05, 0.1), "Wood_Dark")
    l.box((0, 0.02, 0.56), (1.25, 0.05, 0.1), "Wood_Dark")
    l.blob((0.0, -0.04, 0.98), 0.09, "Metal_Brass", n=9, seed=3)
    l.ring((0, -0.05, 0.98), 0.14, 0.02, "Metal_Brass", n=8, m=4, M=T(0, -0.05, 0.98) @ RX(pi / 2) @ T(0, 0.05, -0.98))
    a.part("Leaf", l, origin=(-0.88, 0.085, 0.98), rot=(0, 0, -0.5))


@asset("Door_Round_Curtain", K, ("door", "round", "cloth"), "Round doorway with a clay ring and a parted cloth curtain. Origin: wall face at floor level, facing -Y.")
def _(a):
    f = B()
    ring_frame(f, 0.92, 1.27, -0.14, 0.5, 0.98, "Clay_Pink", seed=5)
    ring_frame(f, 1.2, 1.36, -0.2, -0.05, 0.98, "Clay_Cream", seed=6)
    a.part("Frame", f)
    c = B()
    for sgn in (-1, 1):
        for i in range(4):
            x0 = sgn * (0.25 + i * 0.17)
            top = sqrt(max(0.05, 0.9 ** 2 - x0 * x0))
            xb = sgn * (0.55 + i * 0.09)
            c.tube([(x0, 0.2, 0.98 + top), (x0 * 1.1, 0.2, 1.3), (xb, 0.2, 0.75), (xb + sgn * 0.02, 0.2, 0.22)],
                   [0.085, 0.085, 0.07, 0.075], "Cloth_Coral" if i % 2 else "Cloth_Butter", n=4)
    c.tube([(-0.72, 0.17, 0.8), (-0.55, 0.12, 0.74), (-0.42, 0.17, 0.8)], 0.03, "Rope_Tan", n=3)
    c.tube([(0.72, 0.17, 0.8), (0.55, 0.12, 0.74), (0.42, 0.17, 0.8)], 0.03, "Rope_Tan", n=3)
    a.part("Curtain", c)


def window(a, flowers):
    f = B()
    ring_frame(f, 0.34, 0.56, -0.1, 0.36, 0.0, "Wood_Honey", n=12, seed=7)
    f.box((0, 0.1, 0), (0.05, 0.05, 0.7), "Wood_Dark")
    f.box((0, 0.1, 0), (0.7, 0.05, 0.05), "Wood_Dark")
    a.part("Frame", f)
    g = B()
    g.lathe([(0.0, 0.14), (0.35, 0.14), (0.35, 0.18), (0.0, 0.18)], "Glass_Warm", n=12, M=RX(-pi / 2))
    a.part("Pane", g, shadow=False)
    if flowers:
        p = B()
        p.box((0, -0.3, -0.62), (1.05, 0.34, 0.24), "Wood_Rose", taper=(1.08, 1.15))
        p.box((0, -0.3, -0.52), (0.98, 0.28, 0.06), "Wood_Dark")
        p.beam((-0.4, -0.12, -0.74), (-0.4, 0.05, -0.95), 0.06, "Wood_Dark")
        p.beam((0.4, -0.12, -0.74), (0.4, 0.05, -0.95), 0.06, "Wood_Dark")
        a.part("Box", p)
        bs, bh = B(), B()
        heads = ["Flower_Red", "Flower_Yellow", "Flower_Purple", "Flower_White"]
        for i in range(7):
            flower(bs, bh, (-0.42 + i * 0.14, -0.3 + (i % 2) * 0.08 - 0.04, -0.5), h=0.2 + (i * 7 % 3) * 0.06, head=heads[i % 4], seed=i, size=0.07)
        bs.blob((0, -0.42, -0.55), (0.45, 0.1, 0.12), "Leaf_Green", n=10, seed=3)
        a.part("Stems", bs)
        a.part("Flowers", bh)


@asset("Window_Round", K, ("window", "round"), "Round porthole window with a softly glowing pane. Origin: wall face at window centre, facing -Y.")
def _(a):
    window(a, False)


@asset("Window_Round_Flowerbox", K, ("window", "round", "flowers"), "Round window with a planted flower box below.")
def _(a):
    window(a, True)


@asset("Chimney_Clay", K, ("chimney", "clay"), "Crooked clay chimney pipe with a little hat. Origin: base.")
def _(a):
    b = B()
    b.tube([(0, 0, -0.3), (0, 0, 0.5), (0.12, 0, 0.95), (0.42, 0, 1.25), (0.5, 0, 1.75)], [0.24, 0.22, 0.2, 0.19, 0.2], "Clay_Terracotta", n=8)
    b.lathe([(0.2, 0), (0.28, 0.05), (0.26, 0.14), (0.2, 0.16)], "Clay_Cream", n=8, c=(0.5, 0, 1.7))
    b.ring((0, 0, 0.45), 0.24, 0.04, "Clay_Cream", n=8)
    for i in range(3):
        ang = tau * i / 3
        b.beam((0.5 + cos(ang) * 0.18, sin(ang) * 0.18, 1.84), (0.5 + cos(ang) * 0.18, sin(ang) * 0.18, 2.06), 0.04, "Wood_Dark")
    b.lathe([(0.34, 2.04), (0.3, 2.1), (0.0, 2.3)], "Clay_Teal", n=8, c=(0.5, 0, 0))
    a.part("Pipe", b)


@asset("Balcony_Ring", K, ("balcony", "wood", "rope", "round"), "Ring balcony with rope railing that wraps a small round or tower wall. Origin: wall centre at deck height.")
def _(a):
    rnd = random.Random(8)
    r0, r1 = 1.95, 3.3
    d = B()
    n = 34
    for i in range(n):
        ang = tau * (i + 0.5) / n
        w0, w1 = tau * r0 / n - 0.02, tau * r1 / n - 0.035
        dz = rnd.uniform(-0.008, 0.008)
        ro = r1 + rnd.uniform(-0.05, 0.03)
        V = [(-w0 / 2, r0, -0.07 + dz), (w0 / 2, r0, -0.07 + dz), (w1 / 2, ro, -0.07 + dz), (-w1 / 2, ro, -0.07 + dz),
             (-w0 / 2, r0, dz), (w0 / 2, r0, dz), (w1 / 2, ro, dz), (-w1 / 2, ro, dz)]
        F = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        d.add(V, F, rnd.choice(WOODS), M=RZ(ang - pi / 2))
    a.part("Deck", d)
    w = B()
    w.lathe([(r1 - 0.12, -0.26), (r1 + 0.06, -0.26), (r1 + 0.06, -0.07), (r1 - 0.12, -0.07)], "Wood_Dark", n=20, loop=True)
    for i in range(8):
        ang = tau * i / 8 + 0.2
        w.tube([(cos(ang) * (r1 - 0.2), sin(ang) * (r1 - 0.2), -0.2), (cos(ang) * (r0 + 0.02), sin(ang) * (r0 + 0.02), -1.25)], 0.06, "Wood_Honey", n=5)
    bp, br = B(), B()
    ring_fence(bp, br, r1 - 0.1, 14, gaps=[(pi / 2 + 0.3, 0.26)], seed=9, a_off=pi / 2 + 0.3 + pi / 14)
    a.part("Frame", w)
    a.part("Posts", bp)
    a.part("Ropes", br)


@asset("Stairs_Curved", K, ("stairs", "wood", "curved"), "Wooden steps curling up around a small round house, from the ground to a Balcony_Ring at 2.7 m. Origin: house centre at ground.")
def _(a):
    st, fr = B(), B()
    bp, br = B(), B()
    n, rise = 13, 2.7
    a0, a1 = -pi / 2 + 0.35, pi / 2 + 0.3
    pts = []
    for i in range(n):
        t = (i + 0.5) / n
        ang = a0 + (a1 - a0) * t
        z = rise * (i + 1) / n
        M = RZ(ang - pi / 2) @ T(0, 3.95, z)
        st.box((0, 0, -0.04), (0.5, 1.3, 0.08), WOODS[i % 4], taper=(1, 1), M=M)
        if i % 3 == 0 or i == n - 1:
            p = Vector((cos(ang) * 4.5, sin(ang) * 4.5, z))
            pts.append(p)
            fr.tube([(cos(ang) * 4.4, sin(ang) * 4.4, z - 0.1), (cos(ang) * 4.45, sin(ang) * 4.45, -0.4)], 0.07, "Wood_Dark", n=5)
    for r in (3.4, 4.5):
        fr.tube([(cos(a0 + (a1 - a0) * i / 10) * r, sin(a0 + (a1 - a0) * i / 10) * r, rise * (i / 10) - 0.05 + 0.1) for i in range(11)],
                0.075, "Wood_Dark", sq=True)
    fence_run(bp, br, pts, h=1.0, seed=4)
    a.part("Steps", st)
    a.part("Frame", fr)
    a.part("Posts", bp)
    a.part("Ropes", br)
