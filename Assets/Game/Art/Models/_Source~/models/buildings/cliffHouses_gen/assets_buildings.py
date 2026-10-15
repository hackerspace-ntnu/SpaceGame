"""Assembled buildings. They are put together from the Building Kit as linked duplicates (shared mesh data),
so editing a kit mesh updates every house that uses it."""
from chparts import *
from assets_kit import RS, RL, RT, HS, HL, HT, WIN_S, WIN_L, bulb_r, tall_r, door_open, win_open

BU = "Buildings"


def openings(a, rfun, wins, zc, door="Door_Round", boxes=(0,), z0=0.0):
    a.use(door, (0, -rfun(1.0), z0))
    for i, ang in enumerate(wins):
        r = rfun(zc)
        a.use("Window_Round_Flowerbox" if i in boxes else "Window_Round", (r * cos(ang), r * sin(ang), z0 + zc), rz=ang + pi / 2)


@asset("House_Straw_Cottage", BU, ("house", "round", "straw", "small"), "Small round cottage: stone base, plaster wall, three-tier thatch, round door, chimney and an eave lantern. Origin: floor centre, door faces -Y.")
def _(a):
    a.use("Foundation_Round_S")
    a.use("Wall_Round_S")
    a.use("Roof_Straw_Cone_S", (0, 0, HS))
    openings(a, lambda z: RS, WIN_S, 1.45)
    a.use("Chimney_Clay", (-1.5, 0.9, HS + 0.75), rz=2.2)
    a.use("Lantern_Hanging", (1.55, -2.45, HS - 0.02))
    a.use("Pots_Clay_Set", (-2.2, -2.6, -0.05), rz=0.6)


@asset("House_Clay_Dome", BU, ("house", "round", "clay", "dome", "small"), "Pot-bellied clay house under a terracotta dome, with a curtained round doorway. Origin: floor centre, door faces -Y.")
def _(a):
    a.use("Foundation_Round_S")
    a.use("Wall_Round_Bulb")
    a.use("Roof_Clay_Dome", (0, 0, HS))
    openings(a, bulb_r, WIN_S, 1.45, door="Door_Round_Curtain", boxes=(1,))
    a.use("Lamp_Clay_Ground", (1.9, -2.75, -0.05))
    a.use("Chimney_Clay", (1.0, 1.4, HS + 1.2), rz=0.9)


@asset("House_Onion_Tower", BU, ("house", "tower", "clay", "onion", "balcony", "tall"), "Two-storey tower with a teal onion dome, a ring balcony and curved outside stairs up to the upper doorway. Origin: floor centre, door faces -Y.")
def _(a):
    a.use("Foundation_Round_S")
    a.use("Wall_Round_Tall")
    a.use("Balcony_Ring", (0, 0, 2.7))
    a.use("Stairs_Curved")
    a.use("Roof_Clay_Onion", (0, 0, HT))
    openings(a, tall_r, WIN_S, 1.45, boxes=())
    for ang in (-pi / 2, 0.2, pi + 0.3):
        r = tall_r(3.9)
        a.use("Window_Round", (r * cos(ang), r * sin(ang), 3.9), rz=ang + pi / 2)
    a.use("Lantern_Hanging", (2.3 * cos(-0.6), 2.3 * sin(-0.6), HT - 0.1))


@asset("House_Tiered_Hall", BU, ("house", "round", "clay", "tiered", "large"), "Large round hall under a two-tier pink clay roof with a glowing lantern drum. Origin: floor centre, door faces -Y.")
def _(a):
    a.use("Foundation_Round_L")
    a.use("Wall_Round_L")
    a.use("Roof_Clay_Tiered", (0, 0, HL))
    openings(a, lambda z: RL, WIN_L, 1.55, boxes=(0, 1))
    for sx in (-1, 1):
        a.use("Lantern_Hanging", (sx * 1.7, -(RL + 0.75), HL + 0.05))
    a.use("Chimney_Clay", (-2.6, 1.3, HL + 0.55), rz=2.6)


@asset("House_Mushroom_Lodge", BU, ("house", "round", "straw", "mushroom", "large"), "Large round lodge under a wide mushroom-cap thatch, curtained doorway, supplies stacked outside. Origin: floor centre, door faces -Y.")
def _(a):
    a.use("Foundation_Round_L")
    a.use("Wall_Round_L")
    a.use("Roof_Straw_Mushroom", (0, 0, HL))
    openings(a, lambda z: RL, WIN_L, 1.55, door="Door_Round_Curtain", boxes=(2, 3))
    a.use("Supplies_Stack", (3.2, -3.4, -0.05), rz=0.5)
    a.use("Lantern_Post", (-2.6, -4.2, -0.1), rz=-0.4)


@asset("House_Stacked_Twin", BU, ("house", "round", "two-storey", "stacked", "large"), "A small thatched house stacked on a large one, with a railed roof terrace and a ladder between them. Origin: floor centre, doors face -Y.")
def _(a):
    zt = HL + 0.22
    a.use("Foundation_Round_L")
    a.use("Wall_Round_L")
    openings(a, lambda z: RL, WIN_L, 1.55, boxes=(1,))
    d, w, bp, br = B(), B(), B(), B()
    R = RL + 1.15
    planks_round(d, R, seed=11, z=zt)
    w.lathe([(R - 0.1, zt - 0.3), (R + 0.08, zt - 0.3), (R + 0.08, zt - 0.06), (R - 0.1, zt - 0.06)], "Wood_Dark", n=22, loop=True)
    w.lathe([(RL + 0.1, HL - 0.02), (R - 0.1, zt - 0.2), (R - 0.1, zt - 0.075), (0.0, zt - 0.075)], "Wood_Dark", n=22)
    for i in range(10):
        ang = tau * i / 10 + 0.3
        w.tube([(cos(ang) * (R - 0.15), sin(ang) * (R - 0.15), zt - 0.25), (cos(ang) * (RL + 0.02), sin(ang) * (RL + 0.02), zt - 1.3)], 0.06, "Wood_Honey", n=5)
    f2, r2 = B(), B()
    ring_fence(f2, r2, R - 0.12, 18, gaps=[(0.0, 0.2)], seed=12, a_off=pi / 18)
    merge(bp, f2, T(0, 0, zt)); merge(br, r2, T(0, 0, zt))
    a.part("Terrace", d); a.part("TerraceFrame", w); a.part("TerracePosts", bp); a.part("TerraceRopes", br)
    a.use("Wall_Round_S", (0, 0, zt))
    a.use("Roof_Straw_Cone_S", (0, 0, zt + HS))
    openings(a, lambda z: RS, WIN_S, 1.45, door="Door_Round_Curtain", boxes=(0, 1), z0=zt)
    a.use("Ladder_Wood", (R + 0.95, 0, 0.0), rz=pi / 2)
    a.use("Lantern_Hanging", (-1.6, -2.4, zt + HS - 0.02))
    a.use("Chimney_Clay", (1.4, 1.0, zt + HS + 0.75), rz=0.4)


@asset("House_Stilt_Perch", BU, ("house", "round", "stilts", "cliff", "small"), "Small onion-domed house perched on a stilted deck for cliff edges, with a rope ladder down. Origin: deck centre, door faces -Y.")
def _(a):
    a.use("Foundation_Stilts")
    a.use("Wall_Round_S")
    a.use("Roof_Clay_Onion", (0, 0, HS))
    openings(a, lambda z: RS, WIN_S, 1.45, boxes=(0, 2))
    a.use("Rope_Ladder", (1.2, -3.15, -0.05), rz=0.35)
    a.use("Lantern_Hanging", (-1.5, -2.2, HS - 0.02))
    bp, br = B(), B()
    ring_fence(bp, br, RS + 0.9, 14, gaps=[(-pi / 2 + 0.36, 0.2)], seed=3, a_off=pi / 14)
    a.part("DeckPosts", bp)
    a.part("DeckRopes", br)


@asset("House_Hanging_Pod", BU, ("house", "pod", "hanging", "magic", "small"), "Lavender pod house that hangs from a single brass hook by ropes, with a little porch and rope ladder. Origin: the hook at the top - hang it under an overhang or a big branch.")
def _(a):
    R, zf = 1.75, -6.8
    pod = lambda z: R * (0.86 + 0.16 * sin(pi * min(1.0, z / 2.2)))
    wl, c, w, r = B(), B(), B(), B()
    wb = B()
    wall(wb, pod, 2.2, "Plaster_Cream", "Plaster_Peach", n=18, openings=door_open() + [win_open(0.5, 1.3, 0.37), win_open(pi - 0.5, 1.3, 0.37)],
         band=(0.5, "Clay_Lavender"), zs_extra=[0.9, 1.3, 1.9])
    merge(wl, wb, T(0, 0, zf))
    c.lathe([(0.0, -0.75), (0.9, -0.6), (1.5, -0.25), (pod(0) + 0.06, 0.0), (pod(0) - 0.3, 0.0)], "Clay_Lavender", n=18, c=(0, 0, zf))
    c.lathe([(pod(0) - 0.1, -0.1), (pod(0) - 0.1, 0.03), (0.0, 0.035)], "Wood_Pale", n=18, c=(0, 0, zf))
    zr = zf + 2.2
    c.lathe([(0, 0.12), (1.75, 0.0), (2.15, -0.08), (2.2, 0.08), (1.9, 0.25), (1.55, 0.95), (0.85, 1.6), (0.3, 2.0), (0.14, 2.6), (0.0, 2.7)],
            ["Clay_Butter"] * 4 + ["Clay_Lavender"] * 5, n=14, c=(0, 0, zr))
    a.part("Wall", wl)
    a.part("Shell", c)
    r.tube([(0, 0, zr + 2.6), (0, 0, -0.15)], 0.05, "Rope_Tan", n=4)
    for i in range(4):
        ang = tau * i / 4 + pi / 4
        r.tube([(cos(ang) * 2.1, sin(ang) * 2.1, zr + 0.05), (cos(ang) * 0.6, sin(ang) * 0.6, zr + 2.9), (0, 0, -0.3)], 0.03, "Rope_Tan", n=3)
    r.blob((0, 0, -0.3), 0.14, "Rope_Tan", n=9, seed=2)
    r.ring((0, 0, 0), 0.2, 0.045, "Metal_Brass", n=10, M=RX(pi / 2))
    a.part("Ropes", r)
    planks_round(w, 1.25, seed=4, z=zf, ymax=None)
    pw = B()
    merge(pw, w, T(0, -(R + 0.75), 0))
    pw.tube([(-1.0, -(R + 1.2), zf - 0.12), (-0.5, -(R - 0.3), zf - 0.9)], 0.05, "Wood_Dark", n=5)
    pw.tube([(1.0, -(R + 1.2), zf - 0.12), (0.5, -(R - 0.3), zf - 0.9)], 0.05, "Wood_Dark", n=5)
    pw.ring((0, -(R + 0.75), zf - 0.12), 1.2, 0.06, "Wood_Dark", n=12, m=4)
    a.part("Porch", pw)
    a.use("Door_Round", (0, -pod(1.0), zf))
    for ang in (0.5, pi - 0.5):
        a.use("Window_Round", (pod(1.3) * cos(ang), pod(1.3) * sin(ang), zf + 1.3), rz=ang + pi / 2)
    a.use("Rope_Ladder", (0.6, -(R + 1.9), zf - 0.1), rz=0.2)
    a.use("Lantern_Hanging", (0, 0, zf - 0.72))


@asset("Pavilion_Round", BU, ("pavilion", "gazebo", "round", "open", "clay"), "Open round pavilion: six timber posts carry a clay dome over a table and stools. Origin: floor centre.")
def _(a):
    a.use("Foundation_Round_S")
    w, r = B(), B()
    for i in range(6):
        ang = tau * i / 6 + pi / 6
        x, y = cos(ang) * 2.1, sin(ang) * 2.1
        w.tube([(x, y, 0.0), (x * 1.01, y * 1.01, 1.3), (x, y, HS)], [0.13, 0.11, 0.1], "Wood_Honey" if i % 2 else "Wood_Rose", n=6)
        for d in (-0.5, 0.5):
            a2 = ang + d * 0.5
            w.tube([(x, y, HS - 0.75), (cos(a2) * 2.15, sin(a2) * 2.15, HS - 0.05)], 0.045, "Wood_Dark", n=4)
        rope_wrap(r, (x, y, 1.1), 0.12, turns=3, r=0.025, n=7)
    w.lathe([(1.95, HS - 0.12), (2.25, HS - 0.12), (2.25, HS + 0.04), (1.95, HS + 0.04)], "Wood_Dark", n=18, loop=True)
    a.part("Posts", w)
    a.part("Lashings", r)
    a.use("Roof_Clay_Dome", (0, 0, HS))
    a.use("Table_Round_Stools", (0, 0, 0.04))
    a.use("Lantern_Hanging", (0, 0, HS + 0.1))


# ================================================================ living tree buildings
@asset("TreeHouse_Woven", BU, ("treehouse", "tree", "grown", "round", "organic"), "Round house grown from nine living trunks trained into a dome, with wicker walls between them and a leafy crown. Origin: floor centre, doorway faces -Y.")
def _(a):
    n = 9
    t, l, f = B(), B(), B()
    zs = [-0.35, 0.3, 1.2, 2.3, 3.3, 4.2, 4.95]
    rs = [3.2, 2.75, 2.58, 2.52, 1.95, 1.05, 0.22]
    rad = [0.3, 0.23, 0.18, 0.16, 0.15, 0.14, 0.13]
    rnd = random.Random(2)
    for i in range(n):
        base = -pi / 2 + tau * (i + 0.5) / n
        pts = []
        for z, r in zip(zs, rs):
            ang = base + 0.28 * max(0.0, z - 2.1)
            rr = r * (1 + rnd.uniform(-0.02, 0.02))
            pts.append((cos(ang) * rr, sin(ang) * rr, z))
        t.tube(pts, rad, "Bark_Warm" if i % 3 else "Bark_Plum", n=6)
        t.tube([pts[1], (cos(base + 0.35) * 3.4, sin(base + 0.35) * 3.4, -0.3)], [0.14, 0.05], "Bark_Warm", n=5)
        if i % 2 == 0:
            p3 = Vector(pts[3])
            tip = p3 + Vector((cos(base) * 1.2, sin(base) * 1.2, 0.9))
            t.tube([p3, p3.lerp(tip, 0.5) + Vector((0, 0, 0.2)), tip], [0.1, 0.07, 0.04], "Bark_Warm", n=5)
            pad(l, tip + Vector((0, 0, 0.1)), 0.85, 0.6, ("Leaf_Shade", "Leaf_Green", "Leaf_Lime"), seed=i, lumps=4)
    a.part("Trunks", t)
    wk = B()
    wall(wk, lambda z: 2.46, 2.5, "Straw_Dark", "Plaster_Cream", n=18, thick=0.12,
         openings=[(-pi / 2, 0.55, 0.0, 1.55), (-pi / 2, 0.36, 1.55, 1.95)] + [win_open(x, 1.4, 0.34) for x in (-pi / 2 + tau * 3 / 9, -pi / 2 + tau * 6 / 9)],
         band=(0.6, "Straw_Gold"))
    a.part("Wicker", wk)
    dm = B()
    dm.lathe([(2.48, 2.45), (1.9, 3.3), (1.0, 4.2), (0.0, 4.92), (0.0, 4.75), (0.9, 4.02), (1.76, 3.2), (2.3, 2.45)],
             ["Leaf_Deep", "Leaf_Green", "Leaf_Green", "Plaster_Cream", "Plaster_Cream", "Plaster_Cream", "Plaster_Cream", "Leaf_Deep"], n=18, loop=True, a0=0.1)
    a.part("LeafDome", dm)
    GM = ("Leaf_Shade", "Leaf_Green", "Leaf_Lime")
    for i, (x, y, z, r) in enumerate([(0, 0, 5.3, 1.6), (1.2, 0.6, 4.9, 1.05), (-1.1, 0.8, 4.85, 1.0), (0.1, -1.3, 4.8, 0.95), (-0.6, -0.5, 6.1, 0.9), (0.7, -0.3, 6.0, 0.8)]):
        pad(l, (x, y, z), r, 0.62, GM, seed=i + 20, lumps=5)
    for i in range(9):
        d = fib(11)[i]
        f.blob((d.x * 1.5, d.y * 1.45, 5.5 + abs(d.z) * 1.0), 0.11, "Leaf_Pink" if i % 2 else "Flower_White", n=7, seed=i)
    a.part("Leaves", l)
    a.part("Blossoms", f)
    fl = B()
    fl.lathe([(2.6, -0.3), (2.6, 0.02), (1.0, 0.035), (0.0, 0.04)], ["Stone_Warm", "Wood_Pale", "Clay_Teal"], n=18)
    a.part("Floor", fl)
    for x in (-pi / 2 + tau * 3 / 9, -pi / 2 + tau * 6 / 9):
        a.use("Window_Round", (2.46 * cos(x), 2.46 * sin(x), 1.4), rz=x + pi / 2)
    a.use("Lantern_Hanging", (0, 0, 4.5))
    a.use("Vine_Hanging", (cos(0.6) * 2.75, sin(0.6) * 2.75, 2.6), rz=0.6 + pi / 2 + pi)


def hollow_r(z):
    return 2.05 + 0.6 * (1 - z / 1.5) ** 2 if z < 1.5 else 2.05 + 0.3 * ((z - 1.5) / 2.7) ** 2


@asset("TreeHouse_Hollow", BU, ("treehouse", "tree", "hollow", "organic", "oak"), "A giant hollow oak you live inside: round door and windows cut into the trunk, amber crown for a roof, glowing mushrooms on the bark. Origin: floor centre, door faces -Y.")
def _(a):
    t, l = B(), B()
    wb = B()
    wall(wb, hollow_r, 4.2, "Bark_Warm", "Wood_Pale", n=14, thick=0.3,
         openings=door_open() + [win_open(0.45, 1.5), win_open(pi - 0.6, 1.5), win_open(-pi / 2, 3.3), win_open(pi / 2 + 0.2, 3.3)],
         zs_extra=[0.5, 1.0, 2.2, 2.75, 3.75])
    a.part("Trunk", wb)
    t.lathe([(2.36, 4.15), (1.9, 4.75), (0.9, 5.2), (0.0, 5.3)], "Bark_Warm", n=14)
    t.lathe([(2.2, -0.3), (2.2, 0.02), (0.0, 0.04)], "Wood_Pale", n=14)
    rnd = random.Random(7)
    for i in range(8):
        ang = tau * i / 8 + 0.2
        if abs((ang + pi / 2 + pi) % tau - pi) < 0.5:
            continue
        r = rnd.uniform(3.6, 4.6)
        t.tube([(cos(ang) * 2.3, sin(ang) * 2.3, 0.9), (cos(ang) * 3.0, sin(ang) * 3.0, 0.2), (cos(ang + 0.2) * r, sin(ang + 0.2) * r, -0.35)], [0.4, 0.28, 0.1], "Bark_Warm", n=6)
    tips = []
    for i in range(6):
        ang = tau * i / 6 + 0.5
        st = Vector((cos(ang) * 1.5, sin(ang) * 1.5, 4.6))
        limb(t, st, (cos(ang) * 0.8, sin(ang) * 0.8, 0.6), 2.6, 0.42, 2, rnd, tips, "Bark_Warm" if i % 2 else "Bark_Plum", gnarl=0.28, up=0.1, spread=0.7)
    a.part("Limbs", t)
    AM = ("Leaf_Rust", "Leaf_Orange", "Leaf_Yellow")
    pad(l, (0, 0, 6.9), 2.4, 0.5, AM, seed=1, lumps=7, n=15)
    for i, (p, dv, r) in enumerate(tips):
        R = rnd.uniform(1.0, 1.45)
        pad(l, p + Vector((0, 0, 0.15)), R, 0.4, AM, seed=i + 5, lumps=4)
    a.part("Leaves", l)
    a.use("Door_Round", (0, -hollow_r(1.0), 0))
    for ang, z in ((0.45, 1.5), (pi - 0.6, 1.5), (-pi / 2, 3.3), (pi / 2 + 0.2, 3.3)):
        a.use("Window_Round", (hollow_r(z) * cos(ang), hollow_r(z) * sin(ang), z), rz=ang + pi / 2)
    a.use("Glow_Mushrooms", (cos(-0.9) * 2.75, sin(-0.9) * 2.75, 0.0), rz=0.5)
    a.use("Lantern_Hanging", (cos(-pi / 2 + 0.7) * 2.9, sin(-pi / 2 + 0.7) * 2.9, 4.3))


@asset("TreeHouse_Spiral", BU, ("treehouse", "tree", "spiral", "tower", "organic", "blossom"), "Three trunks grown twisted around each other into a tower that cradles a round pod room under a pink blossom crown; rope ladder up. Origin: ground at the tower axis, door faces -Y.")
def _(a):
    t, l, f = B(), B(), B()
    zp = 5.6
    pod = lambda z: 1.9 * (0.84 + 0.2 * sin(pi * min(1.0, z / 2.4)))
    for i in range(3):
        ph = tau * i / 3
        pts, rad = [], []
        for k in range(15):
            z = -0.4 + k * 0.45
            u = k / 14
            ang = ph + u * tau * 1.15
            r = 1.5 * (1 - u) ** 2 + 0.55 if u < 0.8 else 0.61 + (u - 0.8) * 6.5
            pts.append((cos(ang) * r, sin(ang) * r, z))
            rad.append(0.42 - 0.2 * u)
        t.tube(pts, rad, ("Bark_Plum", "Bark_Warm", "Bark_Plum")[i], n=7)
        e = Vector(pts[-1])
        ang = atan2(e.y, e.x)
        t.tube([e, (cos(ang) * 2.35, sin(ang) * 2.35, zp + 1.3), (cos(ang + 0.5) * 2.2, sin(ang + 0.5) * 2.2, zp + 2.6), (cos(ang + 0.9) * 1.3, sin(ang + 0.9) * 1.3, zp + 3.4)],
               [0.22, 0.17, 0.13, 0.08], "Bark_Warm", n=6)
        t.tube([pts[1], (cos(ph - 0.4) * 2.6, sin(ph - 0.4) * 2.6, -0.35)], [0.26, 0.08], "Bark_Plum", n=5)
        t.tube([pts[1], (cos(ph + 0.5) * 2.3, sin(ph + 0.5) * 2.3, -0.35)], [0.22, 0.07], "Bark_Plum", n=5)
    a.part("Trunks", t)
    wb, wl, c = B(), B(), B()
    wall(wb, pod, 2.4, "Plaster_Peach", "Plaster_Cream", n=18, openings=door_open() + [win_open(0.4, 1.35), win_open(pi - 0.4, 1.35), win_open(pi / 2, 1.35)],
         band=(0.5, "Clay_Pink"), zs_extra=[0.95, 1.9])
    merge(wl, wb, T(0, 0, zp))
    a.part("Wall", wl)
    c.lathe([(0.0, -0.7), (0.9, -0.55), (1.45, -0.2), (pod(0) + 0.05, 0.0), (pod(0) - 0.3, 0.0)], "Wood_Rose", n=18, c=(0, 0, zp))
    c.lathe([(pod(0) - 0.1, -0.1), (pod(0) - 0.1, 0.03), (0.0, 0.035)], "Wood_Pale", n=18, c=(0, 0, zp))
    c.lathe([(0.0, 2.3), (1.7, 2.3), (1.9, 2.38), (1.5, 2.85), (0.7, 3.2), (0.0, 3.3)], "Bark_Warm", n=14, c=(0, 0, zp))
    pw = B()
    planks_round(pw, 1.15, seed=6, z=zp)
    pp = B()
    merge(pp, pw, T(0, -2.55, 0))
    pp.ring((0, -2.55, zp - 0.12), 1.1, 0.06, "Wood_Dark", n=12, m=4)
    for sx in (-1, 1):
        pp.tube([(sx * 0.9, -3.0, zp - 0.15), (sx * 0.5, -1.5, zp - 0.95)], 0.05, "Wood_Dark", n=5)
    a.part("Shell", c)
    a.part("Porch", pp)
    for i, (x, y, z, r) in enumerate([(0, 0, 3.7, 1.8), (1.6, 0.6, 3.2, 1.25), (-1.5, 0.8, 3.2, 1.2), (0.2, -1.6, 3.3, 1.15), (-0.9, -1.0, 4.4, 1.0), (1.0, -0.5, 4.5, 0.95),
                                      (2.2, 0.9, 3.0, 0.8), (-2.0, -0.6, 3.1, 0.8)]):
        pad(l, (x, y, zp + z), r, 0.7, ("Leaf_Rosewood", "Leaf_Pink", "Leaf_Blush"), seed=i + 40, lumps=5)
    a.part("Leaves", l)
    a.use("Door_Round_Curtain", (0, -pod(1.0), zp))
    for ang in (0.4, pi - 0.4, pi / 2):
        a.use("Window_Round", (pod(1.35) * cos(ang), pod(1.35) * sin(ang), zp + 1.35), rz=ang + pi / 2)
    a.use("Rope_Ladder", (0.0, -3.72, zp - 0.05))
    a.use("Lantern_Hanging", (1.6, -1.4, zp + 2.3))
    a.use("Lantern_Hanging", (0, 0, zp - 0.7))
