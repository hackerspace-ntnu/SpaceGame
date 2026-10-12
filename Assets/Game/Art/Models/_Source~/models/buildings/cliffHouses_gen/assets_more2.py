"""Third wave: more complete buildings, richer kit parts, stonework, water, extra trees and wildlife, crafts, furniture.
Built with higher segment counts than the first wave."""
from chparts import *
from assets_kit import RS, RL, RT, HS, HL, HT, WIN_S, WIN_L, bulb_r, tall_r, door_open, win_open, courses, ring_frame
from assets_props import pot, knot, span_rope, WARM, TEAL, PINK
from assets_buildings import openings
from assets_nature import tree, bush, PEACH, ROSE, GREY, OCHRE, GREENS
from assets_more import leaf, frond, stick_rail, arc_pts, liana, GREEN3, JUNGLE3, STICKS

BU, K, PP, DE, PL, IN, LI, NR, CL, TR, BR = ("Buildings", "Building Kit", "Platforms & Paths", "Decorations", "Plants", "Interior", "Lights",
                                            "Nets & Ropes", "Cliff & Rocks", "Trees", "Bridges")
STONES = ("Stone_Warm", "Stone_Cool", "Rock_Cream", "Stone_Warm", "Rock_Peach")
SIMPLE = dict(col_amp=0.05, band_amp=0.02, noise=0.015)


def block_wall(b, P, L, H, seed, thick=0.5, course=0.42, batter=0.06, mats=STONES, cap=None):
    """Masonry of individual blocks laid in courses along P(t) -> (position, tangent, outward normal)."""
    rnd = random.Random(seed)
    rows = max(1, int(round(H / course)))
    course = H / rows
    for r in range(rows):
        z0 = r * course
        x = -rnd.uniform(0.0, 0.4)
        while x < L - 0.05:
            w = rnd.uniform(0.5, 0.95)
            x0, x1 = max(0.0, x), min(L, x + w)
            x += w
            if x1 - x0 < 0.12:
                continue
            p, tan, nrm = P(((x0 + x1) / 2) / L)
            M = frame(p - nrm * (batter * z0), tan, nrm, Z)
            b.box((0, -thick / 2 + rnd.uniform(-0.03, 0.04), z0 + course / 2), (x1 - x0 - 0.035, thick, course - 0.035), rnd.choice(mats),
                  rot=(rnd.uniform(-0.02, 0.02), 0, rnd.uniform(-0.03, 0.03)), taper=(0.96, 0.97), M=M)
    if cap:
        n = max(2, int(L / 0.9))
        for i in range(n):
            p, tan, nrm = P((i + 0.5) / n)
            M = frame(p - nrm * (batter * H), tan, nrm, Z)
            b.box((0, -thick / 2 + 0.03, H + 0.07), (L / n - 0.04, thick + 0.16, 0.14), cap, rot=(0, 0, rnd.uniform(-0.03, 0.03)), taper=(0.95, 0.93), M=M)


def line_P(p0, p1, nrm):
    p0, p1 = Vector(p0), Vector(p1)
    tan = (p1 - p0).normalized()
    return lambda t: (p0.lerp(p1, t), tan, Vector(nrm).normalized())


def arc_P(R, a0, a1, z=0.0, c=(0, 0)):
    def P(t):
        a_ = a0 + (a1 - a0) * t
        sg = 1 if a1 > a0 else -1
        return Vector((c[0] + cos(a_) * R, c[1] + sin(a_) * R, z)), Vector((-sin(a_) * sg, cos(a_) * sg, 0)), Vector((cos(a_), sin(a_), 0))
    return P


def candle(w, g, c, h=0.2, r=0.035):
    w.lathe([(r, 0.0), (r, h), (0.0, h + 0.005)], "Plaster_Cream", n=6, c=c)
    g.lathe([(0.0, 0.0), (r * 0.6, 0.03), (0.0, 0.1)], "Glow_Warm", n=5, c=(c[0], c[1], c[2] + h + 0.005))


# ================================================================ BUILDINGS
@asset("House_Jungle_Hut", BU, ("house", "hut", "jungle", "open", "woven"), "Open-sided jungle hut: stone base, post-and-stick wall with cloth panels, wide woven shingle roof with a beak over the door, furnished with a table and a lantern cluster. Origin: floor centre, door faces -Y.")
def _(a):
    a.use("Foundation_Round_S")
    a.use("Wall_Stick_Open")
    a.use("Roof_Woven_Wide", (0, 0, HS))
    a.use("Table_Round_Stools", (0.2, 0.3, 0.04), rz=0.5)
    a.use("Lantern_Cluster", (0, 0, HS + 1.6))
    a.use("Plant_Broadleaf", (2.6, -2.6, -0.1), rz=1.0)
    a.use("Banner_Ribbon_Spiral", (-3.3, -1.2, HS + 0.25))


@asset("House_Palm_Hut", BU, ("house", "hut", "palm", "jungle", "small"), "Pot-bellied clay hut under a palm-frond roof with a striped porch awning. Origin: floor centre, door faces -Y.")
def _(a):
    a.use("Foundation_Round_S")
    a.use("Wall_Round_Bulb")
    a.use("Roof_Palm_Leaf", (0, 0, HS))
    openings(a, bulb_r, WIN_S, 1.45, boxes=(0, 1))
    a.use("Porch_Awning", (0, -bulb_r(1.0) - 0.15, 0.0))
    a.use("Fern_Cluster", (-2.7, -2.4, -0.1))
    a.use("Jar_Fireflies", (2.2, -2.75, -0.05), rz=0.4)


@asset("Tower_Watch_Stilts", BU, ("tower", "watchtower", "lookout", "stilts", "tall"), "Lookout tower: a railed deck and a little cone-roofed cabin on four tall braced legs, reached by a long ladder. Deck at 9 m author. Origin: ground centre, ladder on the -Y side.")
def _(a):
    w, d, r, lad = B(), B(), B(), B()
    Hd = 9.0
    rnd = random.Random(3)
    for i in range(4):
        ang = tau * i / 4 + pi / 4
        top = Vector((cos(ang) * 1.6, sin(ang) * 1.6, Hd - 0.2))
        bot = Vector((cos(ang) * 3.3, sin(ang) * 3.3, -0.4))
        w.tube([bot, bot.lerp(top, 0.5) + Vector((rnd.uniform(-0.08, 0.08), rnd.uniform(-0.08, 0.08), 0)), top], [0.22, 0.19, 0.15], "Wood_Rose" if i % 2 else "Wood_Honey", n=8)
        nx = tau * (i + 1) / 4 + pi / 4
        for (f0, f1) in ((0.1, 0.42), (0.42, 0.1), (0.5, 0.82), (0.82, 0.5)):
            p0 = bot.lerp(top, f0)
            b2 = Vector((cos(nx) * 3.3, sin(nx) * 3.3, -0.4)).lerp(Vector((cos(nx) * 1.6, sin(nx) * 1.6, Hd - 0.2)), f1)
            w.tube([p0, b2], 0.06, "Wood_Dark", n=5)
        rope_wrap(r, bot.lerp(top, 0.46), 0.2, turns=4, r=0.03, n=8)
    planks_round(d, 2.6, seed=4, z=Hd)
    w.lathe([(2.48, Hd - 0.3), (2.68, Hd - 0.3), (2.68, Hd - 0.075), (2.48, Hd - 0.075)], "Wood_Dark", n=20, loop=True)
    for x in (-1.2, 0, 1.2):
        hl = sqrt(2.5 ** 2 - x * x)
        w.beam((x, -hl, Hd - 0.18), (x, hl, Hd - 0.18), 0.16, "Wood_Dark")
    rl = B()
    stick_rail(rl, arc_pts(2.5, -pi / 2 + 0.25, -pi / 2 + tau - 0.25, 14, Hd), seed=5)
    wb = B()
    wall(wb, lambda z: 1.15, 2.1, "Plaster_Cream", "Plaster_Peach", n=14, thick=0.14, openings=[(-pi / 2, 0.42, 0.0, 1.75), (0.3, 0.25, 0.9, 1.6), (pi - 0.3, 0.25, 0.9, 1.6), (pi / 2, 0.25, 0.9, 1.6)],
         band=(0.4, "Clay_Pink"))
    cab = B()
    merge(cab, wb, T(0, 0.5, Hd))
    cab.ring((0, 0.5, Hd + 2.1), 1.17, 0.06, "Wood_Dark", n=14)
    for sx in (-0.3, 0.3):
        lad.tube([(sx * 1.3, -3.9, -0.1), (sx, -2.45, Hd + 0.9)], [0.06, 0.045], "Wood_Dark", n=6)
    for i in range(24):
        t = (i + 0.6) / 25.5
        p = Vector((0, -3.9 + 1.45 * t, -0.1 + (Hd + 1.0) * t))
        hw = 0.3 * (1.3 - 0.3 * t) + 0.07
        lad.tube([(-hw, p.y, p.z), (hw, p.y, p.z)], 0.03, WOODS[i % 4], n=5)
    a.part("Legs", w); a.part("Lashings", r); a.part("Deck", d); a.part("Railing", rl); a.part("Cabin", cab); a.part("Ladder", lad)
    a.use("Roof_Turret_Cone", (0, 0.5, Hd + 2.1))
    a.use("Lantern_Wall_Sconce", (1.0, -0.1, Hd + 1.7), rz=0.6)
    a.light("Light", (0, 0.5, Hd + 1.2), WARM, 25)


@asset("Windmill_Round", BU, ("windmill", "tower", "mill", "cloth", "tall"), "Round tower windmill with four lattice-and-cloth sails. The rotor is a separate object with its origin on the axle, ready to spin. Origin: floor centre, sails face -Y.")
def _(a):
    a.use("Foundation_Round_S")
    a.use("Wall_Round_Tall")
    a.use("Roof_Straw_Cone_S", (0, 0, HT))
    openings(a, tall_r, WIN_S, 1.45, boxes=(0,))
    for ang in (0.2, pi + 0.3):
        a.use("Window_Round", (tall_r(3.9) * cos(ang), tall_r(3.9) * sin(ang), 3.9), rz=ang + pi / 2)
    hz, hy = 4.35, -(tall_r(4.35) + 0.75)
    w = B()
    w.tube([(0, -1.2, hz), (0, hy - 0.15, hz)], 0.11, "Wood_Dark", n=8)
    w.lathe([(0.34, -0.2), (0.4, 0.0), (0.3, 0.25), (0.0, 0.3)], "Wood_Honey", n=10, M=T(0, -tall_r(4.35) + 0.05, hz) @ RX(pi / 2))
    a.part("Axle", w)
    r, c = B(), B()
    r.lathe([(0.0, -0.2), (0.3, -0.14), (0.34, 0.1), (0.0, 0.16)], "Wood_Rose", n=10, M=RX(pi / 2))
    cols = ("Cloth_Coral", "Cloth_Butter", "Cloth_Sky", "Cloth_Mint")
    for i in range(4):
        M = RY(tau * i / 4)
        r.beam((0, 0, 0.2), (0, 0, 4.3), 0.11, "Wood_Dark", M=M)
        r.beam((0.95, -0.06, 1.0), (0.95, -0.06, 4.2), 0.05, "Wood_Honey", M=M)
        for k in range(7):
            z = 1.0 + k * 0.53
            r.beam((0.0, -0.03, z), (0.98, -0.07, z), 0.045, "Wood_Pale", M=M)
        for k in range(3):
            z0, z1 = 1.05 + k * 1.06, 1.05 + (k + 1) * 1.06 - 0.08
            V = [(0.07, -0.085, z0), (0.9, -0.11, z0), (0.9, -0.11, z1), (0.07, -0.085, z1), (0.07, -0.105, z0), (0.9, -0.13, z0), (0.9, -0.13, z1), (0.07, -0.105, z1)]
            c.add(V, [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], cols[(i + k) % 4] if k != 1 else "Plaster_Cream", M=M)
    ro = a.part("Rotor", r)
    so = a.part("Sails", c)
    for ob in (ro, so):
        ob.location = Vector((0, hy, hz)) * S
        ob.rotation_euler = (0, 0.35, 0)
    a.use("Supplies_Stack", (2.9, -2.6, -0.05), rz=0.7)


@asset("Greenhouse_Dome", BU, ("greenhouse", "dome", "glass", "garden", "plants"), "Glass dome greenhouse on a low stone ring: timber ribs and hoops, see-through panes, full of jungle plants. Origin: floor centre, doorway faces -Y.")
def _(a):
    a.use("Foundation_Round_S")
    s, w, g = B(), B(), B()
    R = RS
    block_wall(s, arc_P(R, -pi / 2 + 0.42, -pi / 2 + tau - 0.42), (tau - 0.84) * R, 0.84, 3, thick=0.36, course=0.42, cap="Stone_Cool")
    dome = lambda t: (R * cos(t * pi / 2) ** 0.85, 0.95 + 2.9 * sin(t * pi / 2))
    for i in range(14):
        ang = -pi / 2 + tau * (i + 0.5) / 14
        w.tube([(cos(ang) * dome(k / 7)[0], sin(ang) * dome(k / 7)[0], dome(k / 7)[1]) for k in range(8)], 0.055, "Wood_Honey", n=5)
    for k in (0, 2, 4, 6):
        r_, z_ = dome(k / 7)
        w.ring((0, 0, z_), max(0.2, r_), 0.05, "Wood_Dark", n=28, m=4)
    for sx in (-1, 1):
        w.tube([(sx * 0.82, -R + 0.1, 0.0), (sx * 0.86, -R, 1.3), (sx * 0.5, -R + 0.15, 2.1), (0, -R + 0.25, 2.35)], 0.07, "Wood_Dark", n=6)
    w.lathe([(0.25, 3.8), (0.3, 3.95), (0.1, 4.1), (0.0, 4.45)], "Clay_Teal", n=8)
    prof = [dome(k / 7) for k in range(8)]
    g.lathe([(r_ * 0.985, z_) for r_, z_ in prof[:-1]] + [(0.0, prof[-1][1] - 0.02)] + [(0.0, prof[-1][1] - 0.05)] + [(r_ * 0.96, z_ - 0.03) for r_, z_ in reversed(prof[:-1])], "Glass_Clear", n=28, loop=True)
    a.part("StoneRing", s); a.part("Ribs", w); a.part("Glass", g, shadow=False)
    a.use("Plant_Broadleaf", (0.9, 0.7, 0.04), rz=1.0)
    a.use("Fern_Cluster", (-1.0, 0.4, 0.04))
    a.use("Flower_Giant", (-0.2, 1.2, 0.04), rz=2.0)
    a.use("Garden_Planter_Round", (0.1, -0.5, 0.0), only=("Plants", "Fruit"))
    a.use("Pots_Clay_Set", (1.2, -0.9, 0.04), rz=2.0)


@asset("Forge_Open", BU, ("forge", "smithy", "workshop", "open", "fire"), "Open smithy: stone hearth with glowing coals under a clay hood and chimney, anvil on a stump, bellows, quench barrel and tool rack beneath a tiled lean-to roof. Origin: floor centre, open side faces -Y.")
def _(a):
    s, w, t, g, rf = B(), B(), B(), B(), B()
    rnd = random.Random(2)
    for i in range(5):
        for j in range(4):
            flagstone(s, (-2.4 + i * 1.2 + (0.5 if j % 2 else 0) + rnd.uniform(-0.08, 0.08), -1.75 + j * 1.15 + rnd.uniform(-0.08, 0.08), -0.02 + 0.004 * ((i + j) % 3)),
                      rnd.uniform(0.44, 0.52), seed=i * 4 + j, th=0.07)
    s.box((0, 0, -0.2), (6.0, 4.8, 0.3), "Stone_Cool", taper=(0.97, 0.97))
    block_wall(s, line_P((-1.2, 1.9, 0), (1.2, 1.9, 0), (0, -1, 0)), 2.4, 1.0, 5, thick=1.1, course=0.34)
    s.box((0, 1.35, 1.02), (2.5, 1.2, 0.1), "Stone_Cool")
    g.blob((0, 1.3, 1.1), (0.6, 0.38, 0.1), "Glow_Warm", n=12, seed=1, zmin=1.07)
    for i in range(6):
        g.blob((rnd.uniform(-0.45, 0.45), 1.3 + rnd.uniform(-0.25, 0.25), 1.16), 0.09, "Glow_Pink" if i % 2 else "Glow_Warm", n=6, seed=i)
    t.lathe([(1.1, 2.0), (1.15, 2.1), (0.5, 2.9), (0.34, 3.2), (0.3, 4.9), (0.38, 5.0), (0.3, 5.1)], ["Clay_Terracotta", "Clay_Terracotta", "Clay_Rust", "Clay_Terracotta", "Clay_Cream", "Clay_Cream"], n=12, c=(0, 1.35, 0))
    for (x, y) in ((-2.6, -2.0), (2.6, -2.0), (-2.6, 2.0), (2.6, 2.0)):
        hgt = 3.0 if y < 0 else 3.9
        w.tube([(x, y, -0.05), (x * 1.01, y, hgt * 0.5), (x, y, hgt)], [0.13, 0.115, 0.1], "Wood_Dark", n=7)
    for x in (-2.6, 0.0, 2.6):
        w.beam((x, -2.5, 2.94), (x, 2.4, 4.0), 0.12, "Wood_Honey")
    for y, z in ((-2.0, 3.06), (2.0, 3.94)):
        w.beam((-2.9, y, z), (2.9, y, z), 0.13, "Wood_Dark")
    sl = atan2(1.06, 4.9)
    for r in range(8):
        for i in range(10):
            y = -2.45 + r * 0.62
            z = 3.05 + (y + 2.5) * 1.06 / 4.9
            rf.box((-2.75 + i * 0.61 + (0.3 if r % 2 else 0), y, z + 0.1), (0.57, 0.74, 0.06), ("Clay_Terracotta", "Clay_Rust", "Clay_Terracotta", "Leaf_Orange")[(i + r * 3) % 4],
                   rot=(sl + 0.1, 0, rnd.uniform(-0.03, 0.03)))
    w.lathe([(0.34, 0.0), (0.3, 0.55)], "Wood_Rose", n=9, c=(-1.3, -0.5, 0), wob=0.05)
    t.box((-1.3, -0.5, 0.72), (0.34, 0.75, 0.3), "Rock_GreyDark", taper=(1.0, 1.0))
    t.box((-1.3, -0.5, 0.9), (0.4, 0.95, 0.1), "Rock_Grey")
    t.lathe([(0.0, 0.0), (0.12, 0.12), (0.02, 0.34)], "Rock_Grey", n=6, M=T(-1.3, -1.02, 0.9) @ RX(pi / 2))
    t.tube([(-1.25, -0.3, 1.0), (-1.0, 0.05, 1.03)], 0.03, "Wood_Honey", n=5)
    t.box((-1.27, -0.33, 1.0), (0.16, 0.12, 0.12), "Rock_GreyDark", rot=(0, 0, 0.6))
    w.lathe([(0.36, 0), (0.44, 0.35), (0.44, 0.65), (0.38, 0.95), (0.33, 0.95), (0.0, 0.8)], ["Wood_Honey", "Wood_Honey", "Wood_Honey", "Wood_Dark", "Water"], n=12, c=(1.6, -0.7, 0))
    for z in (0.2, 0.75):
        w.ring((1.6, -0.7, z), 0.42, 0.025, "Metal_Brass", n=12)
    M = T(1.5, 1.2, 1.25) @ RZ(0.35)
    t.box((0, 0, 0), (0.7, 0.9, 0.12), "Wood_Dark", M=M, taper=(0.5, 1.0))
    t.box((0, 0, 0.22), (0.7, 0.9, 0.1), "Wood_Dark", M=M @ RX(0.12), taper=(0.5, 1.0))
    t.box((0, 0, 0.1), (0.62, 0.8, 0.2), "Cloth_Coral", M=M, taper=(0.5, 1.0))
    w.box((2.3, 1.95, 1.9), (0.08, 1.4, 0.08), "Wood_Honey", rot=(0, 0, pi / 2))
    for i in range(4):
        x = 1.75 + i * 0.36
        t.tube([(x, 1.92, 1.88), (x, 1.9, 1.3 + 0.1 * (i % 2))], 0.022, "Rock_Grey", n=4)
        t.box((x, 1.9, 1.25 + 0.1 * (i % 2)), (0.12, 0.05, 0.14), "Rock_GreyDark")
    a.part("Stone", s); a.part("Timber", w); a.part("Tools", t); a.part("Coals", g, shadow=False); a.part("RoofTiles", rf)
    a.light("Light", (0, 0.9, 1.5), (1.0, 0.6, 0.3), 110)


@asset("House_Grand_Tiered", BU, ("house", "grand", "three-storey", "tiered", "landmark"), "Three-tier landmark house: the stacked twin with a stepped clay skirt roof and a glowing turret room on top, tall banner at its side. Origin: floor centre, doors face -Y.")
def _(a):
    a.use("House_Stacked_Twin", skip=("Roof_Straw_Cone_S", "Chimney_Clay"))
    z2 = HL + 0.22 + HS
    sk, wl, g = B(), B(), B()
    cp, cm = courses([(3.05, 0.02), (2.75, 0.2), (2.42, 0.42), (2.08, 0.68), (1.75, 0.98), (1.45, 1.3), (1.25, 1.55)], "Clay_Lavender", "Clay_Pink", step=0.06)
    sk.lathe([(0.0, 0.18), (2.5, 0.02), (3.1, -0.08)] + cp + [(1.05, 1.55), (1.05, 0.4)], ["Clay_Cream"] * 3 + cm + ["Clay_Cream", "Clay_Cream"], n=22, c=(0, 0, z2))
    wb = B()
    wall(wb, lambda z: 1.15, 2.0, "Plaster_Cream", "Plaster_Mint", n=14, thick=0.14, openings=[(tau * i / 6 + 0.3, 0.22, 0.55, 1.5) for i in range(6)], band=(0.3, "Clay_Butter"))
    merge(wl, wb, T(0, 0, z2 + 1.5))
    wl.ring((0, 0, z2 + 3.5), 1.17, 0.06, "Wood_Dark", n=14)
    g.lathe([(0.95, 0.5), (0.95, 1.55)], "Glass_Warm", n=14, c=(0, 0, z2 + 1.5))
    a.part("SkirtRoof", sk); a.part("TurretWall", wl); a.part("TurretGlow", g, shadow=False)
    a.use("Roof_Turret_Cone", (0, 0, z2 + 3.5))
    a.use("Dormer_Round", (0, -1.9, z2 + 0.5))
    a.use("Chimney_Clay", (2.1, 1.2, z2 + 0.3), rz=0.4)
    a.use("Banner_Pole_Tall", (-5.6, -2.4, -0.1), rz=0.3)
    a.light("Light", (0, 0, z2 + 2.5), WARM, 60)


@asset("Granary_Stilts", BU, ("granary", "storage", "woven", "stilts", "straw"), "Round woven granary raised on six stone-capped stilts, with a ragged thatch hat, a hatch door and a ladder. Origin: ground centre, hatch faces -Y.")
def _(a):
    w, wk, th = B(), B(), B()
    for i in range(6):
        ang = tau * i / 6 + 0.3
        x, y = cos(ang) * 1.45, sin(ang) * 1.45
        w.tube([(x * 1.08, y * 1.08, -0.3), (x, y, 1.25)], [0.13, 0.11], "Wood_Dark", n=7)
        w.lathe([(0.0, 1.25), (0.34, 1.27), (0.36, 1.33), (0.0, 1.37)], "Stone_Cool", n=9, c=(x, y, 0), wob=0.05, seed=i)
    w.lathe([(0.0, 1.37), (1.9, 1.37), (1.9, 1.5), (0.0, 1.5)], "Wood_Rose", n=18)
    body = lambda z: 1.75 * (0.86 + 0.18 * sin(pi * min(1.0, z / 2.6) ** 0.8))
    wk.lathe([(body(k * 0.26), 1.5 + k * 0.26) for k in range(11)], ["Straw_Gold" if k % 2 else "Straw_Dark" for k in range(10)], n=18, wob=0.012, seed=2)
    for k in range(10):
        z = 1.5 + (k + 0.5) * 0.26
        wk.ring((0, 0, z), body((k + 0.5) * 0.26) + 0.015, 0.05, "Straw_Dark" if k % 2 else "Straw_Gold", n=18, m=4)
    for i in range(18):
        ang = tau * i / 18
        wk.tube([(cos(ang) * (body(k * 0.52) + 0.05), sin(ang) * (body(k * 0.52) + 0.05), 1.5 + k * 0.52) for k in range(6)], 0.03, "Wood_Honey", n=3)
    rnd = random.Random(4)
    for i, (rb, zb, rt, zt) in enumerate(((2.35, 4.05, 1.3, 5.0), (1.55, 4.75, 0.7, 5.7), (0.9, 5.45, 0.0, 6.6))):
        prof = [(0.0, zb + 0.2)] if i == 0 else [(rb * 0.5, zb + 0.25)]
        prof += [(rb * 0.9, zb + 0.02), (rb, zb - 0.12), (rb * 0.97, zb + 0.1)]
        prof += [(rt, zt)] if rt > 0 else [(0.2, zt - 0.5), (0.08, zt - 0.15), (0.0, zt)]
        th.lathe(prof, ["Straw_Dark", "Straw_Dark", "Straw_Gold", "Straw_Gold", "Straw_Gold", "Straw_Gold"], n=18, jit=0.04, wob=0.03, seed=i + 7)
        for j in range(12 - i * 3):
            ang = rnd.uniform(0, tau)
            th.lathe([(0.07, 0.05), (0.1, -0.1), (0.0, -0.32 - rnd.uniform(0, 0.12))], "Straw_Dark" if j % 2 else "Straw_Gold", n=4, c=(cos(ang) * rb * 0.97, sin(ang) * rb * 0.97, zb - 0.05))
    d = B()
    yb = -body(1.0)
    d.box((0, yb - 0.02, 2.5), (0.9, 0.1, 1.0), "Wood_Pale")
    d.box((0, yb - 0.08, 2.5), (1.0, 0.05, 0.08), "Wood_Dark")
    d.box((0, yb - 0.08, 2.85), (1.0, 0.05, 0.08), "Wood_Dark")
    d.blob((0.3, yb - 0.12, 2.5), 0.06, "Metal_Brass", n=7, seed=1)
    a.part("Frame", w); a.part("Wicker", wk); a.part("Thatch", th); a.part("Hatch", d)
    a.use("Ladder_Wood", (0, -3.0, 0.0), only=("Ladder",))


@asset("Airship_Small", DE, ("airship", "balloon", "boat", "flying", "vehicle", "magic"), "Little airship: a striped cloth balloon in a rope net carrying a wooden boat hull with a rudder fin, lantern and anchor line. The propeller is a separate object on its shaft. Origin: centre of the boat deck; bow toward +Y.")
def _(a):
    c, r, w = B(), B(), B()
    L, R, zc = 7.0, 2.1, 5.2
    N, K = 14, 16
    rings = []
    V = []
    prof = []
    for i in range(N + 1):
        t = i / N
        y = L * (t - 0.5)
        rr = R * sin(pi * t) ** 0.62 * (1 - 0.18 * (t - 0.5))
        prof.append((y, rr))
    V.append((0, prof[0][0], zc))
    for i in range(1, N):
        rings.append([len(V) + k for k in range(K)])
        for k in range(K):
            ang = tau * k / K
            V.append((cos(ang) * prof[i][1], prof[i][0], zc + sin(ang) * prof[i][1]))
    V.append((0, prof[N][0], zc))
    F, FM = [], []
    cols = ("Cloth_Coral", "Plaster_Cream", "Cloth_Butter", "Plaster_Cream")
    for k in range(K):
        j = (k + 1) % K
        m = cols[k % 4]
        F.append((0, rings[0][j], rings[0][k])); FM.append(m)
        for i in range(len(rings) - 1):
            F.append((rings[i][k], rings[i][j], rings[i + 1][j], rings[i + 1][k])); FM.append(m)
        F.append((rings[-1][k], rings[-1][j], len(V) - 1)); FM.append(m)
    c.add(V, F, FM)
    for i in (3, 6, 9, 11):
        y, rr = prof[i]
        r.ring((0, y, zc), rr + 0.03, 0.03, "Rope_Tan", n=K, m=3, M=T(0, y, zc) @ RX(pi / 2) @ T(0, -y, -zc))
    for k in range(0, K, 2):
        ang = tau * k / K
        r.tube([(cos(ang) * (prof[i][1] + 0.03), prof[i][0], zc + sin(ang) * (prof[i][1] + 0.03)) for i in range(2, N - 1)], 0.022, "Rope_Tan", n=3)
    hull = [(-2.6, 0.12, 0.25), (-2.2, 0.55, 0.05), (-1.2, 0.85, -0.1), (0.0, 0.95, -0.15), (1.2, 0.85, -0.08), (2.2, 0.5, 0.15), (2.9, 0.1, 0.5)]
    w.tube([(0, y, z - 0.35) for (y, hw, z) in hull], [hw for (y, hw, z) in hull], "Wood_Honey", n=10, asp=0.62)
    w.tube([(0, y, z + 0.1) for (y, hw, z) in hull[1:-1]], [hw * 0.82 for (y, hw, z) in hull[1:-1]], "Wood_Pale", n=10, asp=0.1)
    for sx in (-1, 1):
        w.tube([(sx * hw * 1.0, y, z + 0.12) for (y, hw, z) in hull], 0.05, "Wood_Dark", n=5)
        for (y, hw, z) in hull[1:-1:2]:
            ang = pi * (0.5 + sx * 0.35)
            r.tube([(sx * hw, y, z + 0.12), (cos(-pi / 2 + sx * 0.5) * 1.6, y * 0.8, zc + sin(-pi / 2 + sx * 0.5) * 1.6 - 0.3)], 0.022, "Rope_Tan", n=3)
    w.tube([(0, 2.9, 0.15), (0, 3.5, 0.75), (0, 3.6, 1.05)], [0.09, 0.06, 0.03], "Wood_Dark", n=5)
    c.add([(-0.02, -2.5, 0.2), (-0.02, -3.7, 0.9), (-0.02, -3.5, -0.5), (-0.02, -2.6, -0.4), (0.02, -2.5, 0.2), (0.02, -3.7, 0.9), (0.02, -3.5, -0.5), (0.02, -2.6, -0.4)],
          [(0, 1, 2, 3), (7, 6, 5, 4), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], "Cloth_Plum")
    r.tube([(0.5, 1.6, 0.0), (0.7, 1.7, -2.0), (0.6, 1.9, -4.2)], 0.025, "Rope_Tan", n=3)
    w.tube([(0.6, 1.9, -4.2), (0.6, 1.9, -4.6)], 0.03, "Metal_Brass", n=4)
    for sx in (-1, 1):
        w.tube([(0.6, 1.9, -4.6), (0.6 + sx * 0.28, 1.9, -4.45), (0.6 + sx * 0.36, 1.9, -4.25)], 0.025, "Metal_Brass", n=4)
    w.box((0.3, -0.4, 0.2), (0.5, 0.45, 0.4), "Wood_Rose", rot=(0, 0, 0.2))
    w.blob((-0.3, 0.3, 0.2), (0.3, 0.26, 0.26), "Cloth_Butter", n=11, seed=3)
    a.part("Balloon", c); a.part("Ropes", r); a.part("Hull", w)
    pr = B()
    pr.lathe([(0.0, -0.08), (0.1, -0.05), (0.1, 0.08), (0.0, 0.14)], "Metal_Brass", n=8, M=RX(pi / 2))
    for k in range(3):
        M = RY(tau * k / 3)
        bb = B()
        leaf(bb, (0, 0, 0.05), (0, 0.0, 0.75), 0.2, "Wood_Pale", droop=0.0, seg=3)
        merge(pr, bb, M @ RZ(0.5))
    po = a.part("Propeller", pr)
    po.location = Vector((0, -2.95, -0.1)) * S
    a.use("Lantern_Hanging", (0, 3.6, 1.05))


# ================================================================ BUILDING KIT - third wave
@asset("Roof_Clay_Scales_L", K, ("roof", "clay", "scales", "tiles", "dome", "large"), "Large dome roofed in individual fish-scale clay tiles in lavender, pink and cream, with a brass-ringed finial. For large round walls. Origin: eave line centre (wall top).")
def _(a):
    c, t, f = B(), B(), B()
    Re, Hd = 4.45, 3.3
    dome = lambda u: (Re * cos(u * pi / 2) ** 0.8, 0.25 + Hd * sin(u * pi / 2))
    c.lathe([(0, 0.25), (3.9, 0.05), (4.55, -0.05), (4.6, 0.12)] + [dome(k / 9) for k in range(10)], ["Clay_Cream"] * 4 + ["Clay_Lavender"] * 9, n=24)
    rnd = random.Random(6)
    rows = 9
    for ri in range(rows):
        u = (ri + 0.35) / (rows + 0.6)
        r_, z_ = dome(u)
        r1, z1 = dome(u + 0.06)
        slope = atan2(z1 - z_, r_ - r1)
        n = max(7, int(tau * r_ / 0.62))
        for i in range(n):
            ang = tau * (i + 0.5 * (ri % 2)) / n
            w_ = tau * r_ / n * 1.1
            m = ("Clay_Lavender", "Clay_Pink", "Clay_Lavender", "Clay_Cream", "Clay_Rosy")[(i * 3 + ri * 2 + rnd.randint(0, 1)) % 5]
            M = RZ(ang) @ T(r_ + 0.05, 0, z_ + 0.05) @ RY(-(pi / 2 - slope) + 0.12)
            V = [(0.0, -w_ / 2, 0.0), (0.0, w_ / 2, 0.0), (0.0, w_ * 0.42, -0.45), (0.0, 0.0, -0.62), (0.0, -w_ * 0.42, -0.45)]
            V = [(0.03, y, z) for (x, y, z) in V] + [(-0.03, y, z) for (x, y, z) in V]
            t.add(V, [(0, 1, 2, 3, 4), (9, 8, 7, 6, 5)] + [(k, (k + 1) % 5, (k + 1) % 5 + 5, k + 5) for k in range(5)], m, M=M)
    f.lathe([(0.5, Hd + 0.15), (0.56, Hd + 0.3), (0.26, Hd + 0.45), (0.34, Hd + 0.75), (0.2, Hd + 1.0), (0.06, Hd + 1.3), (0.0, Hd + 1.7)], "Clay_Teal", n=10)
    f.ring((0, 0, Hd + 0.47), 0.3, 0.04, "Metal_Brass", n=10)
    a.part("Core", c); a.part("Scales", t); a.part("Finial", f)


@asset("Roof_Straw_Shaggy_S", K, ("roof", "straw", "thatch", "shaggy"), "Shaggy thatch cone: six rows of loose straw bundles hanging over one another, rope-bound topknot. For small round walls. Origin: eave line centre (wall top).")
def _(a):
    c, s, kn = B(), B(), B()
    c.lathe([(0.0, 0.2), (2.7, 0.05), (3.0, 0.1), (2.2, 1.0), (1.3, 1.9), (0.5, 2.7), (0.0, 3.1)], "Straw_Dark", n=18)
    rnd = random.Random(8)
    for ri in range(6):
        u = ri / 6
        R = 3.15 * (1 - u) + 0.25
        z = 0.05 + 2.85 * u
        n = max(8, int(tau * R / 0.3))
        for i in range(n):
            ang = tau * (i + rnd.uniform(-0.3, 0.3)) / n
            ln = rnd.uniform(0.75, 1.1)
            M = RZ(ang) @ T(R - 0.45, 0, z + 0.48) @ RY(-0.78 + rnd.uniform(-0.1, 0.1)) @ RZ(rnd.uniform(0, 1.5))
            s.lathe([(0.04, 0.0), (0.13, -ln * 0.5), (0.1, -ln * 0.85), (0.0, -ln)], rnd.choice(("Straw_Gold", "Straw_Gold", "Straw_Dark")), n=4, M=M)
    kn.lathe([(0.3, 2.9), (0.36, 3.1), (0.2, 3.3), (0.28, 3.55), (0.0, 3.9)], "Straw_Dark", n=9, jit=0.05)
    rope_wrap(kn, (0, 0, 3.28), 0.22, turns=3, r=0.03)
    a.part("Core", c); a.part("Straw", s); a.part("Knot", kn)


@asset("Cupola_Lantern", K, ("cupola", "lantern", "roof", "light"), "Small glowing lantern cupola to crown a roof: six posts, warm panes and a tiled cap. Origin: base centre.")
def _(a):
    w, g = B(), B()
    w.lathe([(0.75, -0.1), (0.8, 0.0), (0.7, 0.1), (0.0, 0.1)], "Wood_Dark", n=12)
    for i in range(6):
        ang = tau * i / 6
        w.beam((cos(ang) * 0.62, sin(ang) * 0.62, 0.05), (cos(ang) * 0.62, sin(ang) * 0.62, 1.05), 0.09, "Wood_Honey")
    w.ring((0, 0, 1.05), 0.64, 0.05, "Wood_Dark", n=12)
    g.lathe([(0.55, 0.12), (0.55, 1.0)], "Glass_Warm", n=6, a0=pi / 6)
    cp, cm = courses([(0.95, 1.05), (0.75, 1.3), (0.5, 1.55), (0.25, 1.8), (0.0, 2.05)], "Clay_Teal", "Clay_TealDeep", step=0.04)
    w.lathe([(0.0, 1.1), (0.8, 1.02)] + cp, ["Clay_Cream"] * 2 + cm, n=12)
    w.blob((0, 0, 2.12), 0.1, "Metal_Brass", n=8, seed=1)
    a.part("Frame", w)
    a.part("Glow", g, shadow=False)
    a.light("Light", (0, 0, 0.55), WARM, 50)


@asset("Window_Arch_Shutters", K, ("window", "arch", "shutters"), "Arched window with a glowing pane, two open plank shutters (separate, hinged objects) and a sill with a potted plant. Origin: wall face at sill height, facing -Y.")
def _(a):
    w, g, p = B(), B(), B()
    arch = [(-0.42, 0.0)] + [(cos(pi - pi * k / 8) * 0.42, 0.75 + sin(pi * k / 8) * 0.42) for k in range(9)] + [(0.42, 0.0)]
    w.tube([(x, -0.06, z) for (x, z) in arch], 0.07, "Wood_Honey", sq=True)
    w.box((0, -0.12, -0.05), (1.2, 0.34, 0.08), "Stone_Warm")
    w.box((0, -0.05, 0.6), (0.04, 0.04, 1.2), "Wood_Dark")
    w.box((0, -0.05, 0.62), (0.8, 0.04, 0.04), "Wood_Dark")
    V = [(x, 0.0, z) for (x, z) in arch] + [(x, 0.04, z) for (x, z) in arch]
    n = len(arch)
    g.add(V, [tuple(range(n)), tuple(range(2 * n - 1, n - 1, -1))] + [(k, (k + 1) % n, (k + 1) % n + n, k + n) for k in range(n)], "Glass_Warm")
    pot(p, (0.3, -0.16, -0.01), 0.36, "Clay_Teal", 2, 3)
    p.blob((0.3, -0.16, 0.24), (0.16, 0.15, 0.13), "Leaf_Green", n=10, seed=2, shade=leaf_shade(GREEN3, 1))
    p.blob((0.36, -0.2, 0.34), 0.06, "Flower_Red", n=7, seed=3)
    a.part("Frame", w); a.part("Pane", g, shadow=False); a.part("Pot", p)
    for sx in (-1, 1):
        s_ = B()
        for k in range(3):
            s_.box((sx * (0.07 + k * 0.14), 0, 0.5), (0.13, 0.04, 1.05), ("Clay_Teal", "Clay_TealDeep", "Clay_Teal")[k])
        s_.box((sx * 0.21, -0.03, 0.2), (0.42, 0.03, 0.06), "Wood_Dark")
        s_.box((sx * 0.21, -0.03, 0.85), (0.42, 0.03, 0.06), "Wood_Dark")
        mm = B()
        merge(mm, s_, T(sx * 0.5, -0.1, 0))
        a.part("Shutter_L" if sx < 0 else "Shutter_R", mm, origin=(sx * 0.5, -0.1, 0), rot=(0, 0, -sx * 0.3))


@asset("Door_Arch_Double", K, ("door", "arch", "double", "stone"), "Tall arched double door in a stone surround with a keystone; each plank leaf is its own object hinged on its outer edge, one standing ajar. For flat or large-radius walls. Origin: wall face at floor level, facing -Y.")
def _(a):
    s, tr = B(), B()
    n = 13
    for k in range(n):
        ang = pi - pi * k / (n - 1)
        key = (k == n // 2)
        s.box((cos(ang) * 1.12, -0.08 - (0.03 if key else 0.012 * (k % 2)), 1.7 + sin(ang) * 1.12), (0.275 if not key else 0.3, 0.46, 0.5 if not key else 0.62), "Clay_Terracotta" if key else STONES[k % 5], rot=(0, -(ang - pi / 2), 0))
    for sx in (-1, 1):
        for r in range(4):
            s.box((sx * 1.12, -0.08, 0.2 + r * 0.42), (0.52, 0.46 + 0.04 * (r % 2), 0.4), STONES[(r + (sx > 0)) % 5], rot=(0, 0, 0.02 * (r % 3 - 1)))
    s.box((0, -0.45, -0.1), (2.9, 0.9, 0.2), "Stone_Cool", taper=(0.96, 0.9))
    a.part("Surround", s)
    for sx in (-1, 1):
        d = B()
        for k in range(4):
            x = sx * (0.11 + k * 0.215)
            top = 1.7 + sqrt(max(0.01, 0.88 ** 2 - (abs(x)) ** 2))
            d.box((x, 0, top / 2), (0.2, 0.08, top), ("Wood_Honey", "Wood_Rose", "Wood_Honey", "Wood_Pale")[k])
        for z in (0.5, 1.5):
            d.box((sx * 0.44, -0.05, z), (0.84, 0.03, 0.1), "Wood_Dark")
        d.ring((sx * 0.16, -0.08, 1.05), 0.09, 0.02, "Metal_Brass", n=8, m=4, M=T(sx * 0.16, -0.08, 1.05) @ RX(pi / 2) @ T(-sx * 0.16, 0.08, -1.05))
        a.part("Leaf_L" if sx < 0 else "Leaf_R", d, origin=(sx * 0.88, 0, 0), rot=(0, 0, 0.0 if sx < 0 else 0.55))


@asset("Balcony_Half", K, ("balcony", "small", "sticks", "window"), "Small half-round balcony with stick railing, brackets and a flower pot, to hang under a window or upper door. Origin: wall face at deck height; extends toward -Y.")
def _(a):
    d, w, r = B(), B(), B()
    R = 1.3
    planks_round(d, R, seed=3, ymax=0.0, pw=0.24)
    w.tube([(cos(pi + pi * i / 10) * R, sin(pi + pi * i / 10) * R, -0.15) for i in range(11)], 0.08, "Wood_Dark", sq=True)
    w.beam((-R, 0.02, -0.15), (R, 0.02, -0.15), 0.14, "Wood_Dark")
    for x in (-0.8, 0.8):
        w.tube([(x, -0.9, -0.2), (x * 0.9, 0.05, -1.2)], 0.06, "Wood_Honey", n=5)
        w.tube([(x * 0.95, -0.45, -0.7), (x, -0.05, -0.25)], 0.04, "Wood_Rose", n=4)
    stick_rail(r, arc_pts(R - 0.08, pi, tau, 6), h=0.9, seed=4)
    pot(d, (0.75, -0.55, 0.0), 0.4, "Clay_Pink", 0, 2)
    d.blob((0.75, -0.55, 0.36), (0.2, 0.2, 0.16), "Leaf_Green", n=10, seed=5, shade=leaf_shade(GREEN3, 2))
    a.part("Deck", d); a.part("Frame", w); a.part("Railing", r)


@asset("Chimney_Stone", K, ("chimney", "stone"), "Stacked-stone chimney that tapers upward, topped with two clay pots. Origin: base centre.")
def _(a):
    s = B()
    rnd = random.Random(5)
    for r in range(9):
        z = -0.3 + r * 0.3
        hw = 0.42 - r * 0.015
        for i in range(4):
            ang = tau * i / 4 + (pi / 4 if r % 2 else 0)
            s.box((cos(ang) * hw * 0.55, sin(ang) * hw * 0.55, z + 0.15), (hw * 1.25, hw * 1.05, 0.28), STONES[(r + i) % 5], rot=(0, 0, ang + rnd.uniform(-0.08, 0.08)), taper=(0.95, 0.95))
    s.box((0, 0, 2.47), (0.9, 0.9, 0.12), "Stone_Cool", rot=(0, 0, 0.1), taper=(0.92, 0.92))
    for (x, m) in ((-0.17, "Clay_Terracotta"), (0.18, "Clay_Rust")):
        s.lathe([(0.14, 2.5), (0.17, 2.75), (0.13, 3.0), (0.16, 3.05), (0.1, 3.05), (0.09, 2.6)], m, n=8, c=(x, 0.02 * (x > 0), 0), loop=True)
    a.part("Stones", s)


@asset("Foundation_Rock_Steps", K, ("foundation", "rock", "steps", "stone"), "Rock-mound base for small round houses that lifts the floor 1.6 m (author) and has seven carved stone steps with low side walls leading up to the door. Origin: floor centre (top of the mound); steps descend toward -Y.")
def _(a):
    b, s, f = B(), B(), B()
    b.crag((0, 0, -1.3), (6.6, 6.4, 2.6), PEACH, seed=3, na=26, nz=5, prof=lambda t_: 1.08 - 0.12 * t_, **SIMPLE)
    f.lathe([(RS - 0.05, -0.1), (RS - 0.05, 0.03), (0.9, 0.035), (0.9, -0.1)], "Wood_Pale", n=20)
    f.lathe([(0.9, -0.1), (0.9, 0.04), (0.0, 0.045)], "Clay_Terracotta", n=20)
    rnd = random.Random(4)
    for i in range(7):
        z = -0.23 - i * 0.23
        y = -(RS + 0.75) - i * 0.42
        s.box((rnd.uniform(-0.03, 0.03), y, z), (1.9 + rnd.uniform(-0.08, 0.08), 0.5, 0.24), STONES[i % 5], rot=(0, 0, rnd.uniform(-0.02, 0.02)), taper=(0.97, 0.94))
    for sx in (-1, 1):
        for i in range(4):
            z = -0.2 - i * 0.42
            y = -(RS + 0.95) - i * 0.76
            s.box((sx * 1.15, y, z - 0.15), (0.36, 0.82, 0.7), STONES[(i + 2) % 5], rot=(0, 0, rnd.uniform(-0.04, 0.04)), taper=(0.92, 0.95))
        s.blob((sx * 1.15, -(RS + 0.9), 0.32), 0.2, "Clay_Teal", n=10, seed=int(sx) + 3)
    a.part("Rock", b); a.part("Floor", f); a.part("Steps", s)


@asset("Support_Bracket_Big", K, ("bracket", "support", "timber", "cantilever"), "Heavy carved timber knee bracket to carry platforms and houses that stick out from a cliff or trunk. Reaches 3 m out and 3 m down (author). Origin: top corner at the wall; arm toward -Y.")
def _(a):
    w, r = B(), B()
    w.beam((0, 0.15, -0.14), (0, -3.1, -0.14), 0.26, "Wood_Rose")
    w.beam((0, 0.05, 0.0), (0, 0.05, -3.2), 0.26, "Wood_Rose")
    w.tube([(0, -2.75, -0.3), (0, -1.9, -1.0), (0, -0.9, -2.05), (0, -0.08, -2.95)], [0.13, 0.15, 0.15, 0.13], "Wood_Honey", n=8)
    w.tube([(0, -1.3, -0.3), (0, -0.75, -0.8), (0, -0.2, -1.25)], 0.08, "Wood_Dark", n=6)
    w.lathe([(0.0, -0.1), (0.2, -0.05), (0.14, 0.12), (0.0, 0.18)], "Wood_Dark", n=8, M=T(0, -3.15, -0.14) @ RX(pi / 2))
    for (y, z) in ((-2.75, -0.3), (-0.1, -2.95)):
        rope_wrap(r, (0, y, z), 0.2, turns=3, r=0.03, n=8)
    a.part("Timber", w)
    a.part("Lashings", r)


@asset("Wall_Round_Arcade_L", K, ("wall", "arcade", "arches", "columns", "stone", "large"), "Open arcade ring for large round buildings: ten turned stone columns carrying arches and a ring beam. Same footprint as Wall_Round_L. Origin: floor centre.")
def _(a):
    s, w = B(), B()
    n = 10
    R = RL - 0.1
    for i in range(n):
        ang = -pi / 2 + tau * (i + 0.5) / n
        x, y = cos(ang) * R, sin(ang) * R
        s.lathe([(0.32, 0.0), (0.34, 0.12), (0.24, 0.2), (0.2, 0.9), (0.19, 1.8), (0.26, 1.92), (0.34, 2.0), (0.34, 2.12), (0.0, 2.12)],
                ["Stone_Cool", "Stone_Cool", "Stone_Warm", "Stone_Warm", "Stone_Warm", "Stone_Cool", "Stone_Cool", "Stone_Cool"], n=10, c=(x, y, 0))
        a1 = -pi / 2 + tau * (i + 1.5) / n
        arc = []
        for k in range(9):
            t = k / 8
            a_ = ang + (a1 - ang) * t
            arc.append((cos(a_) * R, sin(a_) * R, 2.1 + 0.72 * sin(pi * t)))
        s.tube(arc, 0.15, "Rock_Cream", sq=True)
        m = len(arc) // 2
        s.box((arc[m][0], arc[m][1], arc[m][2] + 0.02), (0.3, 0.4, 0.36), "Clay_Terracotta", rot=(0, 0, (ang + a1) / 2 + pi / 2))
    w.lathe([(R - 0.2, HL - 0.2), (R + 0.2, HL - 0.2), (R + 0.2, HL + 0.04), (R - 0.2, HL + 0.04)], "Wood_Dark", n=30, loop=True)
    s.lathe([(R - 0.17, 2.75), (R + 0.17, 2.75), (R + 0.17, HL - 0.2), (R - 0.17, HL - 0.2)], "Plaster_Cream", n=30, loop=True)
    a.part("Stone", s)
    a.part("Beam", w)


# ================================================================ STONEWORK, PATHS, LIFTS
@asset("Stairs_Stone_Straight", PP, ("stairs", "stone", "steps", "path"), "Eight worn stone steps between low stepped side walls with cap stones and grass in the joints. Rises 1.6 over 2.9 m author, 2 m wide. Origin: foot centre; climbs toward +Y.")
def _(a):
    s, g = B(), B()
    rnd = random.Random(2)
    for i in range(8):
        for j in range(2):
            wd = rnd.uniform(0.85, 1.15) if j == 0 else None
            x0 = -1.0 if j == 0 else -1.0 + w0
            w0 = wd if j == 0 else 2.0 - w0
            s.box((x0 + w0 / 2, 0.18 + i * 0.36, 0.1 + i * 0.2 - 0.25), (w0 - 0.03, 0.4, 0.7), STONES[(i + j * 2) % 5], rot=(0, 0, rnd.uniform(-0.015, 0.015)), taper=(0.99, 0.97))
        if i % 3 == 1:
            tuft(g, (rnd.uniform(-0.8, 0.8), i * 0.36 + 0.02, i * 0.2 + 0.2), n=5, h=0.25, seed=i)
    for sx in (-1, 1):
        for i in range(4):
            s.box((sx * 1.22, 0.36 + i * 0.72, 0.3 + i * 0.4), (0.42, 0.74, 1.2), STONES[(i + 1 + (sx > 0)) % 5], rot=(0, 0, rnd.uniform(-0.03, 0.03)), taper=(0.94, 0.96))
            s.box((sx * 1.22, 0.36 + i * 0.72, 0.96 + i * 0.4), (0.54, 0.8, 0.12), "Stone_Cool", rot=(0, 0, rnd.uniform(-0.04, 0.04)), taper=(0.93, 0.93))
    a.part("Stone", s)
    a.part("Grass", g)


@asset("Wall_Retaining_Stone", PP, ("wall", "retaining", "stone", "terrace", "chainable"), "Chainable dry-stone retaining wall of individual blocks with cap stones, 4 m author long and 1.7 m high, leaning back slightly. Origin: left end at the base of the face; runs along +X, face toward -Y.")
def _(a):
    s, g = B(), B()
    block_wall(s, line_P((0, 0, 0), (4, 0, 0), (0, -1, 0)), 4.0, 1.7, 7, thick=0.6, course=0.42, batter=0.1, cap="Stone_Cool")
    rnd = random.Random(8)
    for i in range(4):
        tuft(g, (rnd.uniform(0.3, 3.7), -0.05 - 0.1 * 0, rnd.choice((0.42, 0.85, 1.27))), n=4, h=0.22, seed=i, mat="Leaf_Lime")
    g.blob((2.9, -0.5, 0.9), (0.4, 0.14, 0.3), "Leaf_Green", n=10, seed=3, shade=leaf_shade(GREEN3, 3))
    a.part("Stones", s)
    a.part("Growth", g)


@asset("Terrace_Crescent_Grass", PP, ("terrace", "crescent", "stone", "grass", "garden"), "Half-moon garden terrace: a curved block retaining wall (radius 4 m author) filled level with grass, tufts and flowers. Push its straight back against a slope. Origin: middle of the back edge at the grass surface; bulges toward -Y.")
def _(a):
    s, g, t, fs, fh = B(), B(), B(), B(), B()
    R, H = 4.0, 1.7
    P0 = arc_P(R, pi, tau, -H)
    block_wall(s, P0, pi * R, H, 9, thick=0.55, course=0.42, batter=0.07, cap="Stone_Cool")
    V = [(0, 0, 0.02)] + [(cos(pi + pi * i / 16) * (R - 0.5), sin(pi + pi * i / 16) * (R - 0.5), 0.0) for i in range(17)]
    V += [(0, 0, -H)] + [(cos(pi + pi * i / 16) * (R - 0.5), sin(pi + pi * i / 16) * (R - 0.5), -H) for i in range(17)]
    F, FM = [], []
    rnd = random.Random(3)
    for i in range(16):
        F.append((0, 1 + i, 2 + i)); FM.append("Grass" if rnd.random() < 0.8 else "Leaf_Lime")
        F.append((18, 20 + i, 19 + i)); FM.append("Leaf_Shade")
        F.append((1 + i, 19 + i, 20 + i, 2 + i)); FM.append("Wood_Dark")
    F.append((0, 17, 35, 18)); FM.append("Wood_Dark")
    F.append((1, 0, 18, 19)); FM.append("Wood_Dark")
    g.add(V, F, FM)
    for i in range(16):
        ang = pi + pi * rnd.random()
        d = sqrt(rnd.random()) * (R - 0.9)
        x, y = cos(ang) * d, sin(ang) * d
        if i % 3 == 0:
            flower(fs, fh, (x, y, 0.0), h=0.35, head=rnd.choice(("Flower_Yellow", "Flower_White", "Flower_Purple", "Flower_Red")), seed=i)
        else:
            tuft(t, (x, y, 0.0), n=6, h=rnd.uniform(0.3, 0.5), seed=i, mat="Leaf_Green" if i % 2 else "Leaf_Lime")
    a.part("Stones", s); a.part("Grass", g); a.part("Tufts", t); a.part("Stems", fs); a.part("Flowers", fh)


@asset("Path_Plank_Short", PP, ("path", "plank", "bridge", "short"), "Simple 3 m (author) plank crossing on two logs, lashed with rope, no rails - for small gaps and gullies. Origin: first end at deck level; runs +Y.")
def _(a):
    d, w, r = B(), B(), B()
    path = Path(lambda t: Vector((0, 3.0 * t, -0.06 * 4 * t * (1 - t))))
    walkway(d, path, 10, 1.3, seed=7)
    for x in (-0.45, 0.45):
        w.tube([(x, -0.35, -0.2), (x * 1.03, 1.5, -0.27), (x, 3.35, -0.2)], [0.14, 0.13, 0.12], "Wood_Rose" if x > 0 else "Wood_Dark", n=8)
        for y in (0.15, 2.85):
            rope_wrap(r, (x, y, -0.22), 0.15, turns=3, r=0.025, n=8)
    a.part("Planks", d); a.part("Logs", w); a.part("Lashings", r)


@asset("Zipline_Cable", NR, ("zipline", "rope", "cable", "travel"), "Zip-line: a high anchor post and a low one 14 m (author) away and 3.5 m lower, joined by a sagging rope with a hanging trolley bar (separate object). Origin: foot of the high post; line runs toward +Y.")
def _(a):
    w, r = B(), B()
    for (y, zt) in ((0.0, 4.2), (14.0, 0.7 - 3.5 + 3.5)):
        base = -0.4 if y == 0 else -3.9
        top = zt if y == 0 else -3.5 + 4.2
        w.tube([(0, y, base), (0.05, y, (base + top) / 2), (0, y, top)], [0.17, 0.15, 0.12], "Wood_Dark", n=8)
        for sx in (-1, 1):
            w.tube([(sx * 1.2, y + (0.6 if y else -0.6), base + 0.1), (sx * 0.05, y, base + 2.6)], 0.07, "Wood_Rose", n=5)
        rope_wrap(r, (0, y, top - 0.35), 0.14, turns=5, r=0.03, n=8)
    p0, p1 = Vector((0, 0, 3.85)), Vector((0, 14.0, 0.35))
    pts = sagpath(p0, p1, 0.9, 16)
    r.tube(pts, 0.035, "Rope_Tan", n=5)
    a.part("Posts", w)
    a.part("Rope", r)
    tb = B()
    q = pts[2]
    tb.lathe([(0.0, -0.05), (0.13, -0.05), (0.15, 0.0), (0.13, 0.05), (0.0, 0.05)], "Wood_Pale", n=10, M=T(0, 0, 0.14) @ RY(pi / 2))
    tb.tube([(0, 0, 0.1), (0, 0, -0.7)], 0.02, "Rope_Tan", n=4)
    tb.tube([(-0.35, 0, -0.72), (0.35, 0, -0.72)], 0.035, "Wood_Honey", n=6)
    for sx in (-1, 1):
        tb.box((sx * 0.07, 0, 0.12), (0.03, 0.2, 0.36), "Wood_Dark")
    mm = B()
    merge(mm, tb, T(*q))
    a.part("Trolley", mm, origin=q)


@asset("Lift_Platform_Rope", PP, ("lift", "elevator", "platform", "rope", "pulley"), "Rope elevator: a timber gantry with a big spoked winding wheel and a round railed platform hung on four ropes with a counterweight. The platform is its own object so it can travel. Origin: ground centre under the gantry; platform shown 1 m (author) up.")
def _(a):
    w, r = B(), B()
    Hg = 7.5
    for sx in (-1, 1):
        w.tube([(sx * 2.6, 0, -0.4), (sx * 2.5, 0, Hg * 0.5), (sx * 2.4, 0, Hg)], [0.2, 0.18, 0.15], "Wood_Dark", n=8)
        for sy in (-1, 1):
            w.tube([(sx * 2.7, sy * 1.6, -0.3), (sx * 2.48, 0, 3.4)], 0.08, "Wood_Rose", n=5)
        w.tube([(sx * 2.42, 0, Hg - 1.6), (sx * 1.0, 0, Hg + 0.05)], 0.08, "Wood_Honey", n=5)
        rope_wrap(r, (sx * 2.48, 0, 3.3), 0.2, turns=4, r=0.03, n=8)
    w.beam((-2.9, 0, Hg + 0.1), (2.9, 0, Hg + 0.1), 0.24, "Wood_Rose")
    M = T(0, 0, Hg - 0.75) @ RX(pi / 2)
    w.lathe([(0.78, -0.07), (0.9, -0.07), (0.86, 0.0), (0.9, 0.07), (0.78, 0.07)], "Wood_Honey", n=16, loop=True, M=M)
    w.lathe([(0.0, -0.12), (0.14, -0.12), (0.14, 0.12), (0.0, 0.12)], "Wood_Dark", n=8, M=M)
    for k in range(8):
        ang = tau * k / 8
        w.tube([(cos(ang) * 0.12, 0, Hg - 0.75 + sin(ang) * 0.12), (cos(ang) * 0.8, 0, Hg - 0.75 + sin(ang) * 0.8)], 0.035, "Wood_Pale", n=4)
    for sy in (-1, 1):
        w.beam((0, sy * 0.3, Hg - 0.75), (0, sy * 0.3, Hg + 0.05), 0.09, "Wood_Dark")
    r.tube([(0.86, 0, Hg - 0.75), (0.86, 0, 3.6)], 0.03, "Rope_Tan", n=4)
    r.tube([(-0.86, 0, Hg - 0.75), (-0.86, 0, 5.2)], 0.03, "Rope_Tan", n=4)
    w.blob((-0.86, 0, 4.85), (0.34, 0.34, 0.42), "Stone_Cool", n=12, seed=2)
    r.ring((-0.86, 0, 5.05), 0.3, 0.03, "Rope_Tan", n=10)
    a.part("Gantry", w)
    a.part("Ropes", r)
    pl, rl = B(), B()
    planks_round(pl, 1.5, seed=3, z=0.0, pw=0.25)
    pl.lathe([(1.4, -0.26), (1.56, -0.26), (1.56, -0.07), (1.4, -0.07)], "Wood_Dark", n=18, loop=True)
    stick_rail(pl, arc_pts(1.42, -pi / 2 + 0.4, -pi / 2 + tau - 0.4, 8), h=0.95, seed=4)
    for i in range(4):
        ang = tau * i / 4 + pi / 4
        pl.tube([(cos(ang) * 1.45, sin(ang) * 1.45, -0.1), (cos(ang) * 0.5, sin(ang) * 0.5, 2.1), (0.86 * 0, 0, 2.6)], 0.028, "Rope_Tan", n=3)
    pl.blob((0, 0, 2.6), 0.1, "Rope_Tan", n=8, seed=1)
    mm = B()
    merge(mm, pl, T(0.86, 0, 1.0))
    a.part("Platform", mm, origin=(0.86, 0, 1.0))


@asset("Rock_Arch_Gate", CL, ("rock", "arch", "gate", "tunnel"), "Plain natural rock arch you can walk or fly through: two rounded pillars and a lintel slab, each its own evenly gridded object so it can be reshaped by hand. Opening about 4 m author wide. Origin: ground centre.")
def _(a):
    for i, (c, sz, m, lean) in enumerate((((-3.4, 0, 3.0), (3.0, 3.4, 6.4), PEACH, (0.08, 0)), ((3.5, 0.2, 2.8), (3.2, 3.6, 6.0), ROSE, (-0.07, 0)))):
        b = B()
        b.crag(c, sz, m, seed=20 + i, na=18, nz=8, lean=lean, prof=lambda t_: 1.0 - 0.18 * t_, **SIMPLE)
        a.part("Pillar%d" % (i + 1), b, origin=(c[0], c[1], 0))
    b = B()
    b.crag((0, 0.1, 6.6), (10.6, 4.0, 2.4), PEACH, seed=25, na=24, nz=5, prof=lambda t_: 0.9 + 0.1 * sin(pi * t_), **SIMPLE)
    a.part("Lintel", b, origin=(0, 0.1, 6.6))


@asset("Bridge_Root_Living", BR, ("bridge", "root", "living", "organic", "grown"), "Living root bridge: three thick roots trained across the gap and woven together, with root handrails, cross-ties, moss and hanging rootlets. 10 m author / 16.7 m real, slightly arched. Origin: first end at deck level; other end at +Y.")
def _(a):
    w, g, l = B(), B(), B()
    L = 10.0
    arch = lambda t: 0.55 * sin(pi * t)
    rnd = random.Random(6)
    for k, x in enumerate((-0.5, 0.0, 0.5)):
        pts = [(x + 0.28 * sin(t * tau * 1.5 + k * 2.1), L * t, arch(t) - 0.2 + 0.06 * cos(t * 9 + k)) for t in [i / 20 for i in range(-1, 22)]]
        w.tube(pts, [0.26 - 0.05 * sin(pi * min(1, max(0, i / 22))) for i in range(len(pts))], "Bark_Pale" if k % 2 else "Bark_Warm", n=8)
    for k in range(24):
        t = (k + 0.5) / 24
        w.tube([(-0.72, L * t, arch(t) - 0.05), (0.0, L * t + 0.05, arch(t) + 0.02), (0.72, L * t, arch(t) - 0.05)], 0.07, "Bark_PaleShade" if k % 2 else "Bark_Warm", n=5)
    for sx in (-1, 1):
        rail = [(sx * (0.85 + 0.06 * sin(i * 0.9)), L * i / 16, arch(i / 16) + 0.95 + 0.3 * (2 * i / 16 - 1) ** 2 + 0.05 * sin(i * 1.7)) for i in range(17)]
        w.tube(rail, 0.075, "Bark_Warm", n=6)
        for i in range(1, 16, 2):
            t = i / 16
            w.tube([(sx * 0.72, L * t + 0.2, arch(t) - 0.1), (sx * 0.9, L * t - 0.05, arch(t) + 0.5), rail[i]], [0.06, 0.05, 0.045], "Bark_PaleShade", n=5)
        for q in (0.0, L):
            w.tube([(sx * 0.9, q, -0.9), (sx * 0.95, q, 0.4), (sx * 0.85, q, 1.3)], [0.2, 0.15, 0.09], "Bark_Pale", n=7)
    sh = leaf_shade(GREEN3, 5)
    for i in range(9):
        t = rnd.uniform(0.05, 0.95)
        sx = rnd.choice((-1, 1))
        g.blob((sx * 0.8, L * t, arch(t) + 0.02), (0.3, 0.5, 0.14), "Leaf_Green", n=10, seed=i, shade=sh)
        liana(g, l, (sx * 0.7, L * t, arch(t) - 0.3), rnd.uniform(0.8, 2.4), i + 30, r=0.025)
    a.part("Roots", w); a.part("Moss", g); a.part("Leaves", l)


# ================================================================ WATER, TREES, WILDLIFE
@asset("Waterfall_Cliff", PL, ("waterfall", "water", "cliff", "falls"), "Waterfall that spills over a stone lip and drops 9 m (author) in three overlapping sheets into foam and splash rocks. Origin: centre of the lip at the top; falls toward -Y and down.")
def _(a):
    wt, f, s = B(), B(), B()
    rnd = random.Random(3)
    for k, (x, wd, dz) in enumerate(((0.0, 2.6, 0.0), (-0.9, 1.1, 0.05), (0.8, 1.3, 0.08))):
        pts = [(x, 0.9, -0.06 + dz), (x, 0.0, -0.02 + dz), (x, -0.55 - 0.1 * k, -0.35), (x * 1.05, -0.95 - 0.12 * k, -2.0), (x * 1.1, -1.1 - 0.14 * k, -5.0), (x * 1.15, -1.2 - 0.15 * k, -9.0)]
        wt.tube(pts, [wd * 0.5, wd * 0.5, wd * 0.5, wd * 0.54, wd * 0.6, wd * 0.68], "Water" if k == 0 else "Flower_White" if k == 1 else "Cloth_Sky", n=6, asp=0.09 + 0.02 * k)
    for sx in (-1, 1):
        s.rock((sx * 1.75, 0.2, 0.0), (1.2, 1.6, 0.9), GREY, n=14, seed=int(sx) + 5, p=2.8)
    for i in range(7):
        ang = pi + pi * (i + 0.5) / 7
        s.rock((cos(ang) * 2.4, -1.2 + sin(ang) * 1.6, -9.05), (1.0, 0.9, 0.7), GREY if i % 2 else PEACH, n=12, seed=i + 20, p=2.6)
    for i in range(14):
        x, y = rnd.uniform(-1.6, 1.6), -1.2 + rnd.uniform(-0.9, 0.6)
        f.blob((x, y, -8.95 + rnd.uniform(0, 0.3)), rnd.uniform(0.25, 0.5), "Flower_White", n=9, seed=i + 40)
    wt.lathe([(0.0, -9.3), (2.9, -9.3), (2.9, -9.18), (0.0, -9.16)], "Water", n=14, c=(0, -1.5, 0), wob=0.05, seed=2)
    a.part("Water", wt); a.part("Foam", f); a.part("Rocks", s)


@asset("Pond_Lily", PL, ("pond", "water", "lily", "garden"), "Small stone-rimmed pond with lily pads, pink water lilies, reeds and a stepping stone. Origin: ground centre.")
def _(a):
    wt, s, l, fs, fh = B(), B(), B(), B(), B()
    wt.lathe([(0.0, -0.3), (2.3, -0.3), (2.3, -0.06), (0.0, -0.05)], "Water", n=16, wob=0.08, seed=3)
    stone_ring(s, 2.35, 17, 0.34, z=-0.1, seed=4)
    flagstone(s, (0.5, -0.4, -0.06), 0.42, seed=3, th=0.1)
    rnd = random.Random(5)
    for i in range(9):
        ang, d = rnd.uniform(0, tau), rnd.uniform(0.5, 1.9)
        x, y = cos(ang) * d, sin(ang) * d
        r_ = rnd.uniform(0.2, 0.34)
        a0 = rnd.uniform(0, tau)
        V = [(x, y, -0.035)] + [(x + cos(a0 + 5.4 * k / 7) * r_, y + sin(a0 + 5.4 * k / 7) * r_, -0.035) for k in range(8)]
        V += [(vx, vy, -0.05) for (vx, vy, vz) in V]
        F = [(0, k + 1, k + 2) for k in range(7)] + [(9, 11 + k, 10 + k) for k in range(7)] + [(k + 1, k + 10, k + 11, k + 2) for k in range(7)] + [(0, 9, 10, 1), (8, 17, 9, 0)]
        l.add(V, F, "Leaf_Green" if i % 3 else "Leaf_Lime")
        if i % 3 == 0:
            M = T(x, y, -0.02)
            for k in range(6):
                bb = B()
                leaf(bb, (0, 0, 0), (cos(tau * k / 6) * 0.16, sin(tau * k / 6) * 0.16, 0.12), 0.06, "Leaf_Pink" if k % 2 else "Leaf_Blush", droop=0.0, seg=2, lift=0.3)
                merge(fh, bb, M)
            fh.blob((x, y, 0.03), 0.04, "Flower_Yellow", n=6, seed=i)
    for i in range(3):
        ang = 1.0 + i * 0.5
        tuft(l, (cos(ang) * 1.9, sin(ang) * 1.9, -0.05), n=7, h=1.0, seed=i, mat="Leaf_Deep", spread=0.15, r=0.03)
    a.part("Water", wt); a.part("Stones", s); a.part("Pads", l); a.part("Lilies", fh)


@asset("Tree_Willow", TR, ("tree", "willow", "weeping", "water"), "Weeping willow: forked trunk and arching limbs trailing long curtains of pale leaf ribbons. About 7 m author. Origin: base of trunk.")
def _(a):
    w, l = B(), B()
    rnd = random.Random(12)
    pts, rad = trunk(w, 2.6, 0.38, (0.3, 0.1), "Bark_Warm", rnd, gnarl=0.1)
    tips = []
    for j in range(5):
        ang = tau * j / 5 + rnd.uniform(-0.3, 0.3)
        limb(w, pts[-1], (cos(ang) * 0.7, sin(ang) * 0.7, 0.85), 2.6, rad[-1] * 0.8, 2, rnd, tips, "Bark_Warm", gnarl=0.25, up=0.02, spread=0.7, bias=Vector((0, 0, -0.04)))
    mats = ("Leaf_Olive", "Leaf_Lime", "Leaf_Green")
    for i, (p, dv, r) in enumerate(tips):
        pad(l, p + Vector((0, 0, 0.1)), 0.7, 0.5, ("Leaf_Deep", "Leaf_Green", "Leaf_Lime"), seed=i + 3, lumps=3, n=11)
        for k in range(5):
            a_ = tau * k / 5 + rnd.uniform(0, 1)
            ln = rnd.uniform(2.0, 4.2)
            q = p + Vector((cos(a_) * 0.45, sin(a_) * 0.45, 0.0))
            l.tube([q, q + Vector((cos(a_) * 0.3, sin(a_) * 0.3, -ln * 0.3)), q + Vector((cos(a_) * 0.42, sin(a_) * 0.42, -ln * 0.65)), q + Vector((cos(a_) * 0.46, sin(a_) * 0.46, -ln))],
                   [0.1, 0.13, 0.11, 0.02], mats[(i + k) % 3], n=4, asp=0.15)
    a.part("Trunk", w)
    a.part("Leaves", l)


@asset("Tree_Pine_Cloud", TR, ("tree", "pine", "cloud", "bonsai", "cliff"), "Wind-shaped cliff pine like the one in the reference: dark twisting trunk, long level branches and wide, flat yellow-green cloud layers, with one pink bough. Origin: base of trunk.")
def _(a):
    tree(a, 21, 5.6, 0.34, (-0.9, 0.5), "Wood_Dark", ("Leaf_Shade", "Leaf_Olive", "Leaf_Yellow"), limbs=0, depth=2, length=2.4, spread=0.55, up=0.0,
         padR=1.5, zs=0.27, lumps=6, gnarl=0.32, whorls=[(0.55, 2, 3.4), (0.72, 3, 3.0), (0.9, 3, 2.4)], kids=(2, 3))
    l = B()
    pad(l, (-3.4, -1.2, 2.9), 1.1, 0.4, ("Leaf_Rosewood", "Leaf_Pink", "Leaf_Blush"), seed=5, lumps=5)
    w = B()
    rnd = random.Random(4)
    w.tube([(-0.25, 0.1, 2.0), (-1.4, -0.4, 2.3), (-2.6, -0.9, 2.55), (-3.4, -1.2, 2.75)], [0.16, 0.13, 0.09, 0.05], "Wood_Dark", n=6)
    a.part("PinkBough", w)
    a.part("PinkLeaves", l)


@asset("Tree_Saplings", TR, ("tree", "sapling", "young", "small"), "Three young trees of different heights and colours, each its own trunk and crown, for filling gaps and gardens. Origin: ground centre.")
def _(a):
    specs = [((0, 0), 2.4, ("Leaf_Shade", "Leaf_Green", "Leaf_Lime"), "Bark_Warm"), ((1.5, 0.6), 1.7, ("Leaf_Rosewood", "Leaf_Pink", "Leaf_Blush"), "Bark_Plum"),
             ((-1.2, 0.9), 2.0, ("Leaf_Olive", "Leaf_Lime", "Leaf_Yellow"), "Bark_Warm")]
    for i, ((x, y), H, mats, bark) in enumerate(specs):
        w, l = B(), B()
        rnd = random.Random(30 + i)
        pts, rad = trunk(w, H, 0.09, (rnd.uniform(-0.3, 0.3), rnd.uniform(-0.3, 0.3)), bark, rnd, n=6, segs=6, gnarl=0.06, roots=4, root_len=2.5)
        tips = []
        for j in range(3):
            ang = tau * j / 3 + rnd.uniform(0, 1)
            limb(w, pts[-1], (cos(ang) * 0.6, sin(ang) * 0.6, 0.9), H * 0.32, rad[-1] * 0.8, 1, rnd, tips, bark, gnarl=0.2, up=0.15, spread=0.6, kids=(2, 2))
        for k, (p, dv, r) in enumerate(tips):
            pad(l, p, H * 0.2, 0.7, mats, seed=i * 9 + k, lumps=3, n=11)
        mw, ml = B(), B()
        merge(mw, w, T(x, y, 0)); merge(ml, l, T(x, y, 0))
        a.part("Trunk%d" % (i + 1), mw, origin=(x, y, 0))
        a.part("Leaves%d" % (i + 1), ml, origin=(x, y, 0))


@asset("Bush_Berry", PL, ("bush", "berries", "red"), "Dense green bush heavy with red and orange berries. Origin: ground centre.")
def _(a):
    lumps = [(0, 0, 0.6, 0.78), (0.65, 0.15, 0.45, 0.58), (-0.6, 0.25, 0.42, 0.55), (0.15, -0.6, 0.4, 0.55), (-0.25, 0.55, 0.8, 0.45), (0.5, -0.4, 0.78, 0.42), (-0.55, -0.4, 0.35, 0.45), (0.1, 0.2, 1.05, 0.4)]
    rnd = bush(a, 7, ("Leaf_Shade", "Leaf_Deep", "Leaf_Green"), lumps)
    f = B()
    for i, (x, y, z, r) in enumerate(lumps):
        for k in range(9):
            d = fib(13, rnd, 0.4)[k]
            if d.z < -0.2:
                continue
            f.blob((x + d.x * r * 1.0, y + d.y * r * 1.0, z + d.z * r * 0.86), 0.06, "Flower_Red" if k % 4 else "Leaf_Orange", n=7, seed=rnd.randint(0, 999))
    a.part("Berries", f)


@asset("Mushroom_Giant", PL, ("mushroom", "giant", "platform", "magic"), "Giant pink-capped mushroom you can stand on (cap 4.5 m author across), with gills, cream spots and two smaller ones at its foot. Origin: ground centre.")
def _(a):
    s, c, g = B(), B(), B()
    specs = [((0, 0), 3.4, 2.25, 0.5, 0.0), ((1.9, -0.9), 1.4, 0.95, 0.24, 0.3), ((-1.5, 1.2), 0.9, 0.6, 0.17, -0.25)]
    rnd = random.Random(4)
    for i, ((x, y), H, R, sr, tilt) in enumerate(specs):
        M = T(x, y, 0) @ RY(tilt * 0.4)
        s.lathe([(sr * 1.45, -0.2), (sr * 1.2, H * 0.12), (sr * 0.92, H * 0.5), (sr * 0.82, H * 0.82), (sr * 1.05, H * 0.93), (0.0, H * 0.95)], "Flower_White", n=12, M=M, wob=0.04, seed=i)
        s.lathe([(sr * 1.1, H * 0.62), (sr * 1.75, H * 0.57), (sr * 1.7, H * 0.63), (sr * 0.95, H * 0.7)], "Plaster_Cream", n=12, M=M, loop=True)
        Mc = M @ T(0, 0, H * 0.86) @ RX(tilt)
        c.lathe([(sr, 0.0), (R * 0.9, -R * 0.03), (R, R * 0.04), (R * 0.93, R * 0.2), (R * 0.68, R * 0.36), (R * 0.36, R * 0.46), (0.0, R * 0.5)],
                ["Plaster_Cream", "Clay_Rosy", "Clay_Pink", "Clay_Pink", "Clay_Rosy", "Clay_Pink"], n=20, M=Mc, wob=0.03, seed=i + 5)
        for k in range(16 if i == 0 else 10):
            ang = tau * k / (16 if i == 0 else 10)
            g.tube([(cos(ang) * sr * 1.05, sin(ang) * sr * 1.05, -0.02), (cos(ang) * R * 0.9, sin(ang) * R * 0.9, -R * 0.05)], 0.03 * (R / 2.2 + 0.3), "Leaf_Blush", n=3, M=Mc)
        for k in range(9 if i == 0 else 4):
            ang, d = rnd.uniform(0, tau), rnd.uniform(0.15, 0.85)
            zz = R * (0.5 - 0.42 * d ** 2.2)
            c.blob((cos(ang) * d * R, sin(ang) * d * R, zz), (R * 0.1, R * 0.1, R * 0.035), "Plaster_Cream", n=8, seed=k + i * 10, M=Mc)
    a.part("Stems", s); a.part("Caps", c); a.part("Gills", g)


@asset("Log_Fallen_Mossy", PL, ("log", "fallen", "moss", "forest"), "Hollow fallen log with broken branch stubs, moss, bracket fungi and ferns growing out of it. About 5 m author long. Origin: ground centre; lies along X.")
def _(a):
    w, g, s, l = B(), B(), B(), B()
    pts = [(-2.6, 0.1, 0.55), (-1.2, -0.05, 0.6), (0.3, 0.05, 0.56), (1.6, -0.08, 0.5), (2.6, 0.05, 0.46)]
    w.tube(pts, [0.62, 0.58, 0.55, 0.5, 0.46], "Bark_Warm", n=12)
    w.lathe([(0.0, 0.0), (0.46, 0.0), (0.5, 0.03), (0.4, 0.035), (0.0, 0.03)], ["Wood_Dark", "Wood_Pale", "Wood_Pale", "Wood_Dark"], n=12, M=T(-2.62, 0.1, 0.55) @ RY(-pi / 2))
    w.lathe([(0.0, 0.0), (0.34, 0.0), (0.38, 0.03), (0.3, 0.035), (0.0, 0.03)], ["Wood_Dark", "Wood_Pale", "Wood_Pale", "Wood_Dark"], n=12, M=T(2.62, 0.05, 0.46) @ RY(pi / 2))
    for (x, ang, ln) in ((-1.4, 0.9, 0.9), (0.4, 2.3, 0.7), (1.5, 0.4, 1.1)):
        w.tube([(x, 0, 0.6), (x + 0.15, cos(ang) * ln * 0.6, 0.6 + sin(ang) * ln * 0.6), (x + 0.2, cos(ang) * ln, 0.6 + sin(ang) * ln)], [0.16, 0.12, 0.09], "Bark_Warm", n=7)
    sh = leaf_shade(GREEN3, 2)
    for i, x in enumerate((-2.0, -0.9, 0.2, 1.1, 2.1)):
        g.blob((x, 0.1 * (i % 2), 1.08 - abs(x) * 0.03), (0.55, 0.42, 0.14), "Leaf_Green", n=11, seed=i, shade=sh)
    for i in range(4):
        x = -1.9 + i * 1.2
        g.lathe([(0.0, -0.06), (0.3, -0.02), (0.3, 0.03), (0.0, 0.08)], "Clay_Terracotta" if i % 2 else "Clay_Butter", n=7, M=T(x, -0.55, 0.6 + 0.1 * (i % 2)) @ SC(1.2, 1, 1), wob=0.08, seed=i)
    rnd = random.Random(3)
    for i, x in enumerate((-0.4, 1.9)):
        for k in range(6):
            ang = tau * k / 6
            frond(s, l, (x, 0.5, 0.2), (cos(ang), sin(ang) * 0.6 + 0.4, 0.8), rnd.uniform(0.7, 1.0), ("Leaf_Green", "Leaf_Lime"), rnd, pairs=4, droop=0.5, w=0.6)
    a.part("Log", w); a.part("Growth", g); a.part("Spines", s); a.part("Fronds", l)


@asset("Reeds_Cattails", PL, ("reeds", "cattails", "water", "pond"), "Clump of tall reeds with brown cattail heads for pond edges and wet ground. Origin: ground centre.")
def _(a):
    l, c = B(), B()
    rnd = random.Random(7)
    for i in range(16):
        ang, d = rnd.uniform(0, tau), rnd.uniform(0, 0.4)
        x, y = cos(ang) * d, sin(ang) * d
        h = rnd.uniform(1.1, 1.9)
        leaf(l, (x, y, -0.05), (x + cos(ang) * 0.4, y + sin(ang) * 0.4, h), 0.07, ("Leaf_Deep", "Leaf_Green", "Leaf_Lime")[i % 3], droop=0.12, seg=4)
    for i in range(6):
        ang, d = rnd.uniform(0, tau), rnd.uniform(0, 0.3)
        x, y = cos(ang) * d, sin(ang) * d
        h = rnd.uniform(1.5, 2.1)
        tx, ty = x + cos(ang) * 0.15, y + sin(ang) * 0.15
        l.tube([(x, y, -0.05), (tx, ty, h)], 0.02, "Leaf_Olive", n=4)
        c.lathe([(0.02, 0.0), (0.06, 0.04), (0.06, 0.3), (0.02, 0.34), (0.008, 0.5)], "Wood_Dark", n=6, c=(tx, ty, h - 0.1))
    a.part("Reeds", l)
    a.part("Heads", c)


@asset("Birds_Flock", DE, ("birds", "flock", "flying", "wildlife", "alive"), "Seven birds in flight, loosely in a V and caught at different points of the wing-beat. Each bird is its own object (origin at its body) so they can be animated or scattered. Origin: centre of the flock.")
def _(a):
    pos = [(0, 0, 0), (-1.1, -0.9, 0.3), (1.2, -1.0, -0.2), (-2.3, -1.9, 0.6), (2.5, -2.1, 0.1), (-3.4, -3.1, 0.2), (0.4, -2.6, 0.9)]
    for i, p in enumerate(pos):
        b = B()
        body, wing = ("Plaster_Cream", "Flower_White") if i % 3 else ("Cloth_Coral", "Plaster_Cream")
        b.tube([(0, -0.3, 0.0), (0, -0.05, 0.02), (0, 0.2, 0.03), (0, 0.33, 0.06)], [0.03, 0.1, 0.085, 0.05], body, n=7)
        b.lathe([(0.0, 0.0), (0.035, 0.02), (0.0, 0.13)], "Leaf_Orange", n=5, M=T(0, 0.36, 0.06) @ RX(-pi / 2))
        flap = (0.55, -0.35, 0.15, 0.7, -0.1, 0.4, -0.5)[i]
        for sx in (-1, 1):
            mid = Vector((sx * 0.42, 0.02, 0.42 * flap))
            leaf(b, (sx * 0.06, 0.05, 0.03), mid, 0.2, wing, droop=0.0, seg=2)
            leaf(b, mid, mid + Vector((sx * 0.5, -0.12, 0.5 * flap * 0.35 - 0.05)), 0.17, wing, droop=0.0, seg=3)
        for k in (-1, 0, 1):
            leaf(b, (0, -0.28, 0.0), (k * 0.1, -0.58, 0.02), 0.06, wing, droop=0.0, seg=2)
        mm = B()
        merge(mm, b, T(*p))
        a.part("Bird%d" % (i + 1), mm, origin=p)


# ================================================================ CRAFTS AND TOWN LIFE
@asset("Statue_Owl_Stone", DE, ("statue", "owl", "stone", "guardian", "magic"), "Mossy stone owl guardian on a stepped plinth, with faintly glowing eyes. About 3 m author tall. Origin: ground centre; faces -Y.")
def _(a):
    s, g, m = B(), B(), B()
    block_wall(s, arc_P(0.95, 0, tau), tau * 0.95, 0.42, 3, thick=0.5, course=0.42, batter=0.0)
    s.lathe([(0.0, 0.0), (0.7, 0.0), (0.7, 0.44), (0.0, 0.44)], "Stone_Cool", n=10)
    s.lathe([(0.8, 0.42), (0.85, 0.5), (0.72, 0.62), (0.0, 0.62)], "Stone_Warm", n=12)
    s.lathe([(0.45, 0.6), (0.62, 0.9), (0.68, 1.4), (0.6, 1.95), (0.5, 2.25), (0.56, 2.5), (0.5, 2.8), (0.3, 2.98), (0.0, 3.02)], "Rock_GreyLight", n=14, wob=0.02, seed=2)
    for sx in (-1, 1):
        s.tube([(sx * 0.35, 0.0, 2.85), (sx * 0.47, 0.02, 3.1), (sx * 0.5, 0.04, 3.32)], [0.14, 0.1, 0.02], "Rock_Grey", n=5)
        s.tube([(sx * 0.62, 0.05, 2.1), (sx * 0.76, 0.12, 1.5), (sx * 0.66, 0.1, 0.95)], [0.1, 0.2, 0.06], "Rock_Grey", n=6, asp=2.0)
        s.lathe([(0.0, 0.0), (0.2, 0.02), (0.22, 0.06), (0.0, 0.08)], "Rock_Grey", n=10, M=T(sx * 0.23, -0.5, 2.55) @ RX(pi / 2))
        g.lathe([(0.0, 0.0), (0.09, 0.0), (0.09, 0.04), (0.0, 0.05)], "Glow_Teal", n=8, M=T(sx * 0.23, -0.57, 2.55) @ RX(pi / 2))
        for k in range(3):
            s.tube([(sx * (0.15 + k * 0.1), -0.5, 0.75), (sx * (0.15 + k * 0.1), -0.72, 0.64)], [0.05, 0.02], "Rock_Grey", n=4)
    s.lathe([(0.0, 0.0), (0.07, 0.02), (0.0, 0.22)], "Rock_Ochre", n=5, M=T(0, -0.55, 2.45) @ RX(pi / 2 + 0.5))
    for r in range(3):
        for k in range(5 - r % 2):
            x = -0.36 + k * 0.18 + (0.09 if r % 2 else 0)
            s.blob((x, -0.6 + 0.02 * r, 1.9 - r * 0.3), (0.1, 0.05, 0.13), "Rock_Grey", n=7, seed=r * 7 + k)
    sh = leaf_shade(GREEN3, 3)
    for (x, y, z) in ((0.45, 0.2, 2.2), (-0.6, 0.1, 1.2), (0.3, 0.55, 0.62), (-0.2, 0.0, 3.0)):
        m.blob((x, y, z), (0.25, 0.25, 0.12), "Leaf_Green", n=9, seed=int(z * 9), shade=sh)
    a.part("Stone", s); a.part("Eyes", g, shadow=False); a.part("Moss", m)
    a.light("Light", (0, -0.9, 2.55), TEAL, 8)


@asset("Bell_Frame", DE, ("bell", "frame", "signal", "town"), "Town bell hung in a braced timber frame under a little shingle roof; the bell (with clapper and pull rope) is a separate object pivoting at its yoke. Origin: ground centre.")
def _(a):
    w, sh = B(), B()
    for sx in (-1, 1):
        w.tube([(sx * 1.3, 0, -0.3), (sx * 1.25, 0, 1.6), (sx * 1.2, 0, 3.2)], [0.16, 0.14, 0.12], "Wood_Dark", n=8)
        for sy in (-1, 1):
            w.tube([(sx * 1.32, sy * 0.9, -0.2), (sx * 1.24, 0, 1.5)], 0.07, "Wood_Rose", n=5)
        w.tube([(sx * 1.2, 0, 2.4), (sx * 0.5, 0, 3.15)], 0.06, "Wood_Honey", n=5)
    w.beam((-1.6, 0, 3.2), (1.6, 0, 3.2), 0.18, "Wood_Rose")
    for sy in (-1, 1):
        for r in range(3):
            for i in range(7):
                sh.box((-1.5 + i * 0.5 + (0.25 if r % 2 else 0) - 0.12, sy * (0.2 + r * 0.27), 3.72 - r * 0.2), (0.47, 0.4, 0.05), ("Wood_Honey", "Straw_Dark", "Wood_Pale")[(i + r) % 3], rot=(sy * 0.65, 0, 0))
    w.beam((-1.75, 0, 3.86), (1.75, 0, 3.86), 0.1, "Wood_Dark")
    a.part("Frame", w)
    a.part("Shingles", sh)
    b = B()
    b.lathe([(0.0, -0.1), (0.2, -0.12), (0.3, -0.3), (0.36, -0.75), (0.5, -1.05), (0.56, -1.12), (0.5, -1.14), (0.42, -1.05), (0.28, -0.7), (0.0, -0.3)],
            ["Metal_Brass"] * 6 + ["Clay_Rust"] * 3, n=16)
    b.ring((0, 0, -0.9), 0.44, 0.025, "Clay_Rust", n=16)
    b.tube([(-0.25, 0, 0.0), (0.25, 0, 0.0)], 0.07, "Wood_Dark", n=6)
    b.tube([(0, 0, -0.3), (0, 0, -1.0)], 0.02, "Wood_Dark", n=4)
    b.blob((0, 0, -1.05), 0.09, "Rock_GreyDark", n=8, seed=1)
    b.tube([(0, 0, -1.1), (0.05, -0.05, -1.9), (0, 0, -2.4)], 0.02, "Rope_Tan", n=3)
    b.blob((0, 0, -2.42), 0.05, "Rope_Tan", n=7, seed=2)
    mm = B()
    merge(mm, b, T(0, 0, 3.05))
    a.part("Bell", mm, origin=(0, 0, 3.05), rot=(0, 0.18, 0))


@asset("Notice_Board", DE, ("notice", "board", "sign", "town"), "Village notice board under a small plank roof, pinned with coloured notes and a map, lantern on the side. Origin: ground centre; faces -Y.")
def _(a):
    w, p = B(), B()
    for sx in (-1, 1):
        w.tube([(sx * 1.0, 0, -0.3), (sx * 1.03, 0, 1.2), (sx * 1.0, 0, 2.5)], [0.09, 0.08, 0.07], "Wood_Dark", n=6)
    for k in range(5):
        w.box((0, 0, 1.05 + k * 0.26), (1.9, 0.06, 0.25), WOODS[k % 4], rot=(0, 0.01 * (k % 3 - 1), 0))
    for sy in (-1, 1):
        w.box((0, sy * 0.22, 2.55), (2.5, 0.5, 0.05), "Wood_Rose", rot=(sy * 0.5, 0, 0))
    w.beam((-1.3, 0, 2.68), (1.3, 0, 2.68), 0.07, "Wood_Dark")
    notes = [(-0.6, 1.9, 0.4, 0.5, "Plaster_Cream", 0.1), (-0.05, 1.75, 0.5, 0.36, "Cloth_Butter", -0.08), (0.55, 1.95, 0.36, 0.44, "Cloth_Mint", 0.05), (-0.5, 1.3, 0.34, 0.3, "Cloth_Coral", -0.12),
             (0.1, 1.28, 0.6, 0.42, "Plaster_Cream", 0.03), (0.68, 1.4, 0.26, 0.34, "Cloth_Sky", 0.15)]
    for i, (x, z, wd, ht, m, rot) in enumerate(notes):
        p.box((x, -0.045, z), (wd, 0.015, ht), m, rot=(0, rot, 0))
        p.blob((x, -0.06, z + ht * 0.4), 0.025, "Flower_Red" if i % 2 else "Metal_Brass", n=6, seed=i)
    p.tube([(-0.12, -0.06, 1.2), (0.0, -0.06, 1.32), (0.2, -0.06, 1.25), (0.3, -0.06, 1.38)], 0.012, "Flower_Red", n=3)
    a.part("Board", w)
    a.part("Notes", p)
    a.use("Lantern_Wall_Sconce", (1.08, -0.05, 2.1), rz=0.0)


@asset("Bench_Tree_Ring", DE, ("bench", "ring", "tree", "seat"), "Circular bench with a stick backrest that wraps around a tree trunk or pillar (inner radius 1.1 m author), two cushions on it. Origin: centre at ground.")
def _(a):
    w, c = B(), B()
    rnd = random.Random(3)
    n = 20
    for i in range(n):
        ang = tau * (i + 0.5) / n
        r0, r1 = 1.2, 1.85
        w0, w1 = tau * r0 / n - 0.02, tau * r1 / n - 0.03
        V = [(-w0 / 2, r0, 0.44), (w0 / 2, r0, 0.44), (w1 / 2, r1, 0.44), (-w1 / 2, r1, 0.44), (-w0 / 2, r0, 0.5), (w0 / 2, r0, 0.5), (w1 / 2, r1, 0.5), (-w1 / 2, r1, 0.5)]
        w.add(V, [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)], rnd.choice(WOODS), M=RZ(ang - pi / 2))
    for i in range(8):
        ang = tau * i / 8
        w.tube([(cos(ang) * 1.75, sin(ang) * 1.75, -0.05), (cos(ang) * 1.7, sin(ang) * 1.7, 0.45)], 0.06, "Wood_Dark", n=6)
        w.tube([(cos(ang) * 1.25, sin(ang) * 1.25, -0.05), (cos(ang) * 1.25, sin(ang) * 1.25, 0.45)], 0.05, "Wood_Dark", n=6)
    w.ring((0, 0, 0.4), 1.72, 0.045, "Wood_Dark", n=20)
    r = B()
    stick_rail(r, arc_pts(1.22, 0, tau, 12, 0.5), h=0.6, seed=5, last=False, balusters=2)
    c.blob((1.5, 0.3, 0.56), (0.28, 0.3, 0.08), "Cloth_Coral", n=11, seed=1)
    c.blob((-0.6, -1.4, 0.56), (0.3, 0.28, 0.08), "Cloth_Sky", n=11, seed=2)
    a.part("Bench", w); a.part("Back", r); a.part("Cushions", c)


@asset("Table_Feast_Long", DE, ("table", "feast", "bench", "food", "gathering"), "Long feast table (4 m author) on trestles with two benches, laid with plates, bowls of fruit, jugs, bread and lit candles. Origin: ground centre; runs along X.")
def _(a):
    w, t, f, g = B(), B(), B(), B()
    for k in range(4):
        w.box((0, -0.36 + k * 0.24, 0.82), (4.0, 0.23, 0.07), WOODS[k], rot=(0, 0, 0.004 * (k - 1.5)))
    for x in (-1.5, 1.5):
        for sy in (-1, 1):
            w.tube([(x, sy * 0.55, -0.05), (x, -sy * 0.15, 0.8)], 0.055, "Wood_Dark", n=6)
        w.beam((x, -0.4, 0.3), (x, 0.4, 0.3), 0.06, "Wood_Dark")
    w.beam((-1.5, 0, 0.3), (1.5, 0, 0.3), 0.07, "Wood_Rose")
    for sy in (-1, 1):
        w.box((0, sy * 0.95, 0.46), (3.8, 0.32, 0.07), "Wood_Honey", rot=(0, 0, 0.005 * sy))
        for x in (-1.5, 0.0, 1.5):
            w.lathe([(0.13, -0.05), (0.11, 0.43)], "Wood_Dark", n=7, c=(x, sy * 0.95, 0))
    rnd = random.Random(5)
    for i in range(8):
        x = -1.7 + (i // 2) * 1.13
        y = -0.3 if i % 2 else 0.3
        t.lathe([(0.0, 0.855), (0.17, 0.86), (0.2, 0.89), (0.16, 0.885), (0.0, 0.875)], ("Clay_Cream", "Clay_Teal", "Clay_Pink", "Clay_Butter")[i % 4], n=10, c=(x, y, 0))
        t.lathe([(0.04, 0.86), (0.06, 0.96), (0.045, 0.96), (0.0, 0.88)], "Clay_Cream", n=7, c=(x + 0.27, y + 0.05 * (i % 3 - 1), 0))
    for i, (x, col) in enumerate(((-1.1, "Flower_Red"), (0.0, "Leaf_Orange"), (1.15, "Flower_Purple"))):
        t.lathe([(0.12, 0.86), (0.3, 0.95), (0.33, 1.05), (0.28, 1.05), (0.0, 0.9)], "Clay_Terracotta" if i % 2 else "Clay_Lavender", n=10, c=(x, 0, 0))
        for j in range(7):
            f.blob((x + cos(j * 0.9) * 0.14 * (j < 6), sin(j * 0.9) * 0.14 * (j < 6), 1.07 + (0.1 if j == 6 else 0.0)), 0.085, col if j % 3 else "Flower_Yellow", n=7, seed=i * 9 + j)
    for x in (-0.55, 0.6):
        pot(t, (x, 0.02, 0.855), 0.5, "Clay_Teal" if x < 0 else "Clay_Butter", 3, 4)
        f.blob((x + 0.3, -0.1, 0.92), (0.2, 0.12, 0.08), "Straw_Dark", n=9, seed=int(x * 9))
        candle(t, g, (x - 0.3, 0.05, 0.855), h=0.22)
    candle(t, g, (1.75, 0.0, 0.855), h=0.16)
    candle(t, g, (-1.8, 0.02, 0.855), h=0.26)
    a.part("Table", w); a.part("Tableware", t); a.part("Food", f); a.part("Flames", g, shadow=False)
    a.light("Light", (0, 0, 1.4), WARM, 35)


@asset("Beehives_Straw", DE, ("beehive", "bees", "straw", "garden"), "Three coiled-straw skep hives on a plank stand under a tiny thatch roof, with a few bees and flowers around. Origin: ground centre; faces -Y.")
def _(a):
    w, h, b, bs, bh = B(), B(), B(), B(), B()
    for sx in (-1, 1):
        for sy in (-1, 1):
            w.tube([(sx * 1.1, sy * 0.35, -0.2), (sx * 1.05, sy * 0.3, 0.6)], 0.06, "Wood_Dark", n=6)
        w.tube([(sx * 1.15, 0.3, 0.5), (sx * 1.15, 0.38, 1.9)], 0.05, "Wood_Dark", n=5)
    w.box((0, 0, 0.62), (2.5, 0.8, 0.07), "Wood_Pale")
    h.box((0, 0.1, 1.98), (2.8, 1.1, 0.12), "Straw_Gold", rot=(0.25, 0, 0))
    h.box((0, 0.1, 2.07), (2.5, 0.8, 0.08), "Straw_Dark", rot=(0.25, 0, 0))
    for i, x in enumerate((-0.8, 0.0, 0.8)):
        for k in range(7):
            r_ = 0.34 * sin(pi * (0.5 + 0.47 * k / 7)) + 0.03
            h.ring((x, 0, 0.7 + k * 0.1), r_, 0.06, "Straw_Gold" if (k + i) % 2 else "Straw_Dark", n=12, m=5)
        h.blob((x, 0, 1.4), (0.12, 0.12, 0.08), "Straw_Dark", n=8, seed=i)
        h.lathe([(0.0, 0.0), (0.07, 0.0), (0.07, 0.03), (0.0, 0.03)], "Wood_Dark", n=7, M=T(x, -0.37, 0.78) @ RX(pi / 2))
    rnd = random.Random(2)
    for i in range(9):
        b.blob((rnd.uniform(-1.2, 1.2), rnd.uniform(-0.9, -0.3), rnd.uniform(0.7, 1.6)), 0.03, "Glow_Warm", n=6, seed=i)
    for i in range(6):
        flower(bs, bh, (rnd.uniform(-1.4, 1.4), rnd.uniform(-1.0, -0.6), 0.0), h=rnd.uniform(0.3, 0.5), head=("Flower_Purple", "Flower_Yellow", "Flower_White")[i % 3], seed=i)
    a.part("Stand", w); a.part("Hives", h); a.part("Bees", b, shadow=False); a.part("Stems", bs); a.part("Flowers", bh)


@asset("Pottery_Kiln_Wheel", DE, ("pottery", "kiln", "wheel", "craft", "clay"), "Potter's corner: a beehive kiln with a glowing mouth and smoke pipe, a kick wheel with a half-thrown pot (the wheel head is a separate object), and a drying rack of pots. Origin: ground centre.")
def _(a):
    s, w, g, p = B(), B(), B(), B()
    rnd = random.Random(4)
    kx = -1.4
    for r in range(7):
        R = 1.05 * cos(r / 7 * pi / 2 * 0.95)
        z = 0.15 + 1.5 * sin(r / 7 * pi / 2)
        n = max(5, int(tau * R / 0.42))
        for i in range(n):
            ang = tau * (i + 0.5 * (r % 2)) / n
            if r < 3 and abs((ang + pi / 2 + pi) % tau - pi) < 0.42:
                continue
            s.box((kx + cos(ang) * R, sin(ang) * R, z), (0.3, tau * R / n * 0.98, 0.26), ("Clay_Terracotta", "Clay_Rust", "Clay_Cream", "Clay_Terracotta")[(i + r) % 4], rot=(0, -(r / 7) * 1.2, ang))
    s.lathe([(0.0, 0.0), (1.2, 0.0), (1.2, 0.14), (0.0, 0.14)], "Stone_Cool", n=14, c=(kx, 0, -0.1), wob=0.03)
    s.lathe([(0.2, 1.55), (0.17, 2.5), (0.22, 2.55), (0.13, 2.55), (0.13, 1.6)], "Clay_Rust", n=8, c=(kx, 0, 0), loop=True)
    g.blob((kx, -0.75, 0.45), (0.3, 0.2, 0.26), "Glow_Warm", n=10, seed=1)
    g.blob((kx, -0.5, 0.4), (0.4, 0.3, 0.2), "Glow_Pink", n=10, seed=2)
    w.lathe([(0.5, 0.0), (0.55, 0.12), (0.5, 0.14), (0.0, 0.14)], "Stone_Warm", n=12, c=(1.0, -0.2, 0))
    w.tube([(1.0, -0.2, 0.1), (1.0, -0.2, 0.85)], 0.05, "Wood_Dark", n=6)
    for sx in (-1, 1):
        w.tube([(1.0 + sx * 0.55, 0.3, -0.05), (1.0 + sx * 0.5, 0.3, 0.6)], 0.05, "Wood_Dark", n=5)
    w.box((1.0, 0.35, 0.62), (1.3, 0.3, 0.06), "Wood_Honey")
    for i in range(2):
        for k in range(3):
            w.box((2.4, -0.5 + i * 0.0, 0.5 + k * 0.5), (0.9, 0.34, 0.04), "Wood_Pale") if i == 0 else None
    for sx in (-1, 1):
        w.tube([(2.4 + sx * 0.42, -0.5, -0.05), (2.4 + sx * 0.42, -0.5, 1.7)], 0.035, "Wood_Dark", n=5)
    cols = ("Clay_Teal", "Clay_Pink", "Clay_Butter", "Clay_Lavender", "Clay_Cream", "Clay_Terracotta")
    for k in range(3):
        for i in range(3):
            pot(p, (2.12 + i * 0.28, -0.5, 0.52 + k * 0.5), 0.36, cols[(k * 3 + i) % 6], (k + i) % 4, k * 3 + i)
    a.part("Kiln", s); a.part("Wood", w); a.part("Fire", g, shadow=False); a.part("Pots", p)
    wh = B()
    wh.lathe([(0.0, 0.0), (0.34, 0.0), (0.34, 0.06), (0.0, 0.06)], "Wood_Rose", n=14)
    wh.lathe([(0.1, 0.06), (0.17, 0.14), (0.2, 0.3), (0.15, 0.42), (0.17, 0.46), (0.13, 0.46), (0.0, 0.12)], "Clay_Cream", n=12)
    mm = B()
    merge(mm, wh, T(1.0, -0.2, 0.85))
    a.part("WheelHead", mm, origin=(1.0, -0.2, 0.85))
    a.light("Light", (kx, -1.1, 0.5), (1.0, 0.6, 0.3), 45)


@asset("Kite_String", DE, ("kite", "wind", "cloth", "flying", "alive"), "Diamond kite with a bow-tie tail flying high on a curved string from a ground stake. The kite is a separate object. Origin: the stake; kite about 9 m author up and downwind (+Y).")
def _(a):
    r, w = B(), B()
    w.tube([(0, 0, -0.3), (0.03, 0.05, 0.5)], [0.06, 0.04], "Wood_Dark", n=6)
    rope_wrap(r, (0.02, 0.03, 0.3), 0.05, turns=4, r=0.015, n=6)
    kp = Vector((0.5, 6.5, 9.0))
    r.tube([(0.02, 0.04, 0.4), (0.15, 2.4, 2.4), (0.32, 4.7, 5.6), kp], 0.012, "Rope_Tan", n=3)
    a.part("Stake", w)
    a.part("String", r)
    k = B()
    pts = [(0, 0, 0.9), (0.6, 0.0, 0.15), (0, 0, -0.9), (-0.6, 0, 0.15)]
    ctr = (0, -0.12, 0.1)
    cols = ("Cloth_Coral", "Cloth_Butter", "Cloth_Sky", "Cloth_Mint")
    for i in range(4):
        p0, p1 = pts[i], pts[(i + 1) % 4]
        k.add([p0, p1, ctr, (p0[0], 0.02, p0[2]), (p1[0], 0.02, p1[2]), (0, -0.1, 0.1)], [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], cols[i])
    k.tube([pts[0], pts[2]], 0.015, "Wood_Pale", n=3)
    k.tube([pts[1], (0, -0.1, 0.15), pts[3]], 0.015, "Wood_Pale", n=3)
    tail = [(0, 0, -0.9), (0.1, -0.3, -1.6), (-0.1, -0.5, -2.3), (0.12, -0.8, -3.0), (0.0, -1.0, -3.7)]
    k.tube(tail, 0.008, "Rope_Tan", n=3)
    for i, p in enumerate(tail[1:]):
        for sx in (-1, 1):
            k.add([(p[0], p[1], p[2]), (p[0] + sx * 0.16, p[1], p[2] + 0.08), (p[0] + sx * 0.16, p[1], p[2] - 0.08), (p[0], p[1] + 0.015, p[2]), (p[0] + sx * 0.16, p[1] + 0.015, p[2] + 0.08), (p[0] + sx * 0.16, p[1] + 0.015, p[2] - 0.08)],
                  [(0, 1, 2), (5, 4, 3), (0, 3, 4, 1), (1, 4, 5, 2), (2, 5, 3, 0)], cols[i % 4])
    mm = B()
    merge(mm, k, T(*kp) @ RX(-0.6) @ RZ(0.2))
    a.part("Kite", mm, origin=kp)


@asset("Campfire_Circle", LI, ("campfire", "fire", "light", "seats", "gathering"), "Stone-ringed campfire with a log tipi, faceted flames and embers, three log seats and a kettle on a tripod. Origin: ground centre.")
def _(a):
    s, w, g = B(), B(), B()
    stone_ring(s, 0.75, 11, 0.24, seed=2)
    for i in range(6):
        ang = tau * i / 6
        w.tube([(cos(ang) * 0.55, sin(ang) * 0.55, 0.02), (cos(ang + 0.3) * 0.05, sin(ang + 0.3) * 0.05, 0.75)], 0.07, "Wood_Dark" if i % 2 else "Wood_Rose", n=6)
    for i, (x, y, r, h) in enumerate([(0, 0, 0.26, 1.05), (0.2, 0.1, 0.17, 0.7), (-0.18, 0.12, 0.16, 0.6), (0.02, -0.22, 0.15, 0.55), (-0.1, -0.05, 0.12, 0.85)]):
        g.lathe([(r * 0.6, 0.0), (r, h * 0.25), (r * 0.55, h * 0.6), (r * 0.2, h * 0.85), (0.0, h)], ("Glow_Warm", "Glow_Pink", "Glow_Warm")[i % 3], n=6, c=(x, y, 0.15), wob=0.15, seed=i)
    rnd = random.Random(3)
    for i in range(7):
        g.blob((rnd.uniform(-0.4, 0.4), rnd.uniform(-0.4, 0.4), 0.08), 0.06, "Glow_Pink", n=6, seed=i + 10)
    for i in range(3):
        ang = tau * i / 3 + 0.5
        M = T(cos(ang) * 2.1, sin(ang) * 2.1, 0.28) @ RZ(ang + pi / 2)
        w.tube([(-0.7, 0, 0), (0.7, 0, 0.02)], 0.28, "Wood_Honey" if i % 2 else "Wood_Rose", n=9, M=M)
        w.tube([(0.3, 0.0, 0.1), (0.45, 0.25, 0.4)], [0.08, 0.05], "Wood_Dark", n=5, M=M)
    for i in range(3):
        ang = tau * i / 3 + 1.5
        w.tube([(cos(ang) * 1.0, sin(ang) * 1.0, -0.05), (cos(ang) * 0.08, sin(ang) * 0.08, 1.75)], 0.03, "Rock_GreyDark", n=4)
    w.tube([(0, 0, 1.7), (0, 0, 1.3)], 0.012, "Rock_GreyDark", n=3)
    w.lathe([(0.1, 0.0), (0.2, 0.08), (0.19, 0.22), (0.1, 0.28), (0.0, 0.29)], "Rock_GreyDark", n=10, c=(0, 0, 1.02))
    a.part("Stones", s); a.part("Wood", w); a.part("Flames", g, shadow=False)
    a.light("Light", (0, 0, 0.8), (1.0, 0.62, 0.32), 140)


# ================================================================ FURNITURE
@asset("Bed_Bunk", IN, ("bed", "bunk", "furniture", "interior"), "Two-level timber bunk with patchwork blankets, pillows, a little ladder and a hanging lantern. Origin: floor centre; ladder on the -Y side.")
def _(a):
    w, c = B(), B()
    for (x, y) in ((-1.0, -0.5), (1.0, -0.5), (-1.0, 0.5), (1.0, 0.5)):
        w.tube([(x, y, 0.0), (x * 1.01, y, 1.1), (x, y, 2.25)], [0.07, 0.065, 0.06], "Wood_Dark", n=6)
    for z in (0.45, 1.55):
        w.box((0, 0, z), (2.0, 1.0, 0.08), "Wood_Honey")
        for sy in (-1, 1):
            w.beam((-1.0, sy * 0.5, z + 0.22), (1.0, sy * 0.5, z + 0.22), 0.06, "Wood_Rose")
        c.box((0, 0, z + 0.1), (1.9, 0.92, 0.14), "Plaster_Cream", taper=(0.98, 0.96))
        cols = ("Cloth_Coral", "Cloth_Sky", "Cloth_Butter", "Cloth_Mint", "Cloth_Plum", "Cloth_Coral")
        for i in range(3):
            for j in range(2):
                c.box((-0.1 + i * 0.42, -0.23 + j * 0.46, z + 0.185), (0.41, 0.45, 0.05), cols[(i + j * 2 + int(z * 2)) % 6])
        c.blob((-0.72, 0.0, z + 0.24), (0.2, 0.32, 0.08), "Flower_White", n=11, seed=int(z * 9))
    for sx in (-0.2, 0.2):
        w.tube([(0.55 + sx, -0.75, 0.0), (0.55 + sx, -0.56, 1.7)], 0.03, "Wood_Dark", n=5)
    for k in range(5):
        t = (k + 0.7) / 5.6
        w.tube([(0.33, -0.75 + 0.19 * t, 1.7 * t), (0.77, -0.75 + 0.19 * t, 1.7 * t)], 0.025, WOODS[k % 4], n=5)
    a.part("Frame", w)
    a.part("Bedding", c)
    a.use("Lantern_Wall_Sconce", (-1.0, -0.56, 2.1))


@asset("Bookshelf_Tall", IN, ("bookshelf", "books", "furniture", "interior"), "Tall, slightly crooked bookcase crammed with coloured books, scrolls, a jar and a trailing plant. Origin: floor centre against the wall; faces -Y.")
def _(a):
    w, bk = B(), B()
    for sx in (-1, 1):
        w.box((sx * 0.72, 0, 1.1), (0.07, 0.36, 2.2), "Wood_Dark", rot=(0, 0.012 * sx, 0))
    for k in range(6):
        w.box((0, 0, 0.06 + k * 0.42), (1.42, 0.36, 0.05), "Wood_Honey")
    w.box((0, 0.17, 1.1), (1.42, 0.03, 2.2), "Wood_Rose")
    w.lathe([(0.0, 2.2), (0.8, 2.2), (0.84, 2.26), (0.0, 2.3)], "Wood_Dark", n=4, a0=pi / 4, M=SC(1, 0.3, 1))
    cols = ("Cloth_Coral", "Cloth_Sky", "Cloth_Butter", "Cloth_Mint", "Cloth_Plum", "Clay_Teal", "Clay_Terracotta", "Plaster_Cream")
    rnd = random.Random(6)
    for k in range(5):
        z0 = 0.085 + k * 0.42
        x = -0.66
        while x < 0.62:
            if rnd.random() < 0.12:
                x += rnd.uniform(0.08, 0.2)
                continue
            th = rnd.uniform(0.04, 0.09)
            h = rnd.uniform(0.22, 0.34)
            lean = rnd.uniform(-0.03, 0.03) if rnd.random() < 0.8 else 0.25
            bk.box((x + th / 2, rnd.uniform(-0.04, 0.0), z0 + h / 2), (th - 0.006, 0.24, h), rnd.choice(cols), rot=(0, lean, 0))
            x += th + (0.07 if abs(lean) > 0.1 else 0.0)
    pot(bk, (0.45, -0.02, 2.31), 0.4, "Clay_Pink", 0, 1)
    bk.tube([(-0.5, -0.05, 2.34), (-0.1, -0.05, 2.36)], 0.045, "Plaster_Cream", n=6)
    bk.tube([(-0.45, 0.03, 2.4), (-0.05, 0.0, 2.43)], 0.04, "Cloth_Butter", n=6)
    l = B()
    liana(l, l, (0.45, -0.12, 2.5), 1.2, 4, sway=0.12, r=0.02)
    a.part("Case", w); a.part("Books", bk); a.part("Plant", l)


@asset("Desk_Writing", IN, ("desk", "writing", "study", "furniture", "interior"), "Writing desk with drawers, a stool, open book, scrolls, ink pot with quill and a lit candle. Origin: floor centre; you sit on the -Y side.")
def _(a):
    w, t, g = B(), B(), B()
    w.box((0, 0, 0.76), (1.5, 0.7, 0.06), "Wood_Honey", rot=(0, 0, 0.01))
    for (x, y) in ((-0.68, -0.28), (-0.68, 0.28), (0.3, 0.28), (0.3, -0.28)):
        w.tube([(x, y, 0.0), (x, y, 0.74)], [0.04, 0.05], "Wood_Dark", n=6)
    w.box((0.5, 0, 0.5), (0.46, 0.62, 0.48), "Wood_Rose")
    for k in range(2):
        w.box((0.5, -0.32, 0.38 + k * 0.23), (0.4, 0.03, 0.2), "Wood_Pale")
        w.blob((0.5, -0.35, 0.38 + k * 0.23), 0.03, "Metal_Brass", n=6, seed=k)
    w.box((0, 0.3, 0.95), (1.5, 0.08, 0.34), "Wood_Rose")
    for i in range(3):
        w.box((-0.5 + i * 0.5, 0.25, 0.95), (0.44, 0.03, 0.26), "Wood_Pale")
    w.lathe([(0.2, 0.0), (0.13, 0.08), (0.11, 0.42), (0.22, 0.46), (0.2, 0.52), (0.0, 0.54)], ["Wood_Dark", "Wood_Dark", "Wood_Dark", "Cloth_Coral", "Cloth_Coral"], n=10, c=(-0.2, -0.75, 0))
    t.box((-0.3, -0.08, 0.8), (0.28, 0.36, 0.03), "Plaster_Cream", rot=(0, -0.1, 0.15))
    t.box((-0.02, -0.04, 0.8), (0.28, 0.36, 0.03), "Plaster_Cream", rot=(0, 0.1, 0.15))
    t.box((-0.16, -0.06, 0.785), (0.6, 0.38, 0.015), "Cloth_Plum", rot=(0, 0, 0.15))
    t.tube([(0.35, 0.1, 0.83), (0.62, -0.1, 0.83)], 0.04, "Plaster_Cream", n=6)
    t.tube([(0.3, -0.18, 0.82), (0.6, -0.25, 0.82)], 0.035, "Cloth_Butter", n=6)
    t.lathe([(0.05, 0.79), (0.07, 0.83), (0.045, 0.88), (0.05, 0.9), (0.0, 0.9)], "Clay_TealDeep", n=8, c=(-0.58, 0.12, 0))
    bb = B()
    leaf(bb, (-0.58, 0.12, 0.9), (-0.42, 0.2, 1.28), 0.06, "Flower_White", droop=0.05, seg=3)
    merge(t, bb)
    candle(t, g, (0.62, 0.15, 0.79), h=0.2)
    t.lathe([(0.0, 0.79), (0.09, 0.79), (0.1, 0.81), (0.0, 0.815)], "Metal_Brass", n=8, c=(0.62, 0.15, 0))
    a.part("Desk", w); a.part("Things", t); a.part("Flame", g, shadow=False)
    a.light("Light", (0.62, 0.1, 1.15), WARM, 18)


@asset("Armchair_Wicker", IN, ("armchair", "chair", "wicker", "furniture", "interior"), "Round woven armchair with a high curved back, a fat cushion and a folded blanket over the arm. Origin: floor centre; faces -Y.")
def _(a):
    w, c = B(), B()
    w.lathe([(0.42, 0.0), (0.5, 0.1), (0.52, 0.36), (0.46, 0.42), (0.0, 0.42)], "Straw_Dark", n=14)
    for k in range(9):
        z = 0.42 + k * 0.09
        a0 = -0.25 - min(k, 5) * 0.0
        a1 = pi + 0.25
        hgt = 1.0 if k < 4 else 1.0
        span0 = -0.35 if k < 4 else 0.25 + (k - 4) * 0.07
        span1 = pi + 0.35 if k < 4 else pi - 0.25 - (k - 4) * 0.07
        R = 0.52 + 0.03 * sin(pi * k / 8)
        w.tube([(cos(span0 + (span1 - span0) * i / 10) * R, sin(span0 + (span1 - span0) * i / 10) * R, z + 0.012 * (i % 2)) for i in range(11)], 0.05, "Straw_Gold" if k % 2 else "Straw_Dark", n=4)
    for i in range(9):
        ang = -0.3 + (pi + 0.6) * i / 8
        top = 0.78 if (ang < 0.3 or ang > pi - 0.3) else 1.24
        w.tube([(cos(ang) * 0.5, sin(ang) * 0.5, 0.4), (cos(ang) * 0.55, sin(ang) * 0.55, top)], 0.028, "Wood_Honey", n=4)
    c.blob((0, 0.0, 0.5), (0.42, 0.42, 0.13), "Cloth_Sky", n=14, seed=2, zmin=0.42)
    c.blob((0, 0.34, 0.8), (0.3, 0.1, 0.22), "Cloth_Butter", n=11, seed=3)
    c.box((0.52, -0.02, 0.8), (0.14, 0.4, 0.07), "Cloth_Coral", rot=(0, 0.2, 0.1))
    c.box((0.56, -0.02, 0.66), (0.05, 0.36, 0.26), "Cloth_Coral", rot=(0, 0.1, 0.1))
    a.part("Wicker", w)
    a.part("Cushions", c)


@asset("Bathtub_Wood", IN, ("bathtub", "bath", "wood", "furniture", "interior"), "Oval stave bathtub full of water, brass-banded, with a bucket, a stool and a towel. Origin: floor centre.")
def _(a):
    w, wt, c = B(), B(), B()
    n = 20
    for i in range(n):
        ang = tau * i / n
        x, y = cos(ang) * 0.95, sin(ang) * 0.6
        nxt = tau * (i + 1) / n
        x2, y2 = cos(nxt) * 0.95, sin(nxt) * 0.6
        rz = atan2(y2 - y, x2 - x)
        wd = sqrt((x2 - x) ** 2 + (y2 - y) ** 2)
        w.box(((x + x2) / 2 * 1.02, (y + y2) / 2 * 1.02, 0.36), (wd + 0.01, 0.05, 0.72), WOODS[i % 4], rot=(0, 0, rz), taper=(1.0, 1.0))
    for z in (0.14, 0.58):
        w.tube([(cos(tau * i / n) * 1.0, sin(tau * i / n) * 0.65, z) for i in range(n)], 0.02, "Metal_Brass", n=4, closed=True)
    w.lathe([(0.0, 0.0), (0.93, 0.0), (0.93, 0.06), (0.0, 0.06)], "Wood_Dark", n=n, M=SC(1, 0.63, 1))
    wt.lathe([(0.0, 0.06), (0.92, 0.06), (0.92, 0.6), (0.0, 0.61)], "Water", n=n, M=SC(1, 0.62, 1))
    for i in range(6):
        wt.blob((cos(i * 1.1) * 0.5, sin(i * 1.1) * 0.3, 0.62), 0.09, "Flower_White", n=7, seed=i)
    w.lathe([(0.16, 0.0), (0.2, 0.3), (0.17, 0.3), (0.14, 0.04)], "Wood_Rose", n=10, c=(1.45, 0.3, 0), loop=True)
    w.lathe([(0.0, 0.0), (0.15, 0.0), (0.15, 0.04), (0.0, 0.04)], "Wood_Dark", n=10, c=(1.45, 0.3, 0))
    w.tube([(1.28, 0.3, 0.3), (1.45, 0.3, 0.48), (1.62, 0.3, 0.3)], 0.015, "Rope_Tan", n=4)
    w.lathe([(0.0, 0.42), (0.22, 0.42), (0.22, 0.47), (0.0, 0.47)], "Wood_Honey", n=9, c=(-1.4, -0.3, 0))
    for i in range(3):
        ang = tau * i / 3
        w.tube([(-1.4 + cos(ang) * 0.22, -0.3 + sin(ang) * 0.22, 0.0), (-1.4 + cos(ang) * 0.13, -0.3 + sin(ang) * 0.13, 0.43)], 0.03, "Wood_Dark", n=5)
    c.box((-1.4, -0.3, 0.5), (0.34, 0.28, 0.06), "Cloth_Mint", rot=(0, 0, 0.3))
    c.box((-1.4, -0.3, 0.56), (0.3, 0.24, 0.05), "Plaster_Cream", rot=(0, 0, 0.5))
    a.part("Tub", w); a.part("Water", wt); a.part("Towels", c)


# ================================================================ LIGHTS
@asset("Lamp_Street_Double", LI, ("light", "lamp", "street", "post", "double"), "Tall street lamp: a carved post that forks into two curled arms, each hanging a lantern, with a stone foot and a small flower basket. Origin: base.")
def _(a):
    w, g, r, s, f = B(), B(), B(), B(), B()
    w.tube([(0, 0, -0.3), (0.03, 0, 1.5), (-0.02, 0, 3.0), (0, 0, 4.0)], [0.14, 0.11, 0.09, 0.08], "Wood_Dark", n=8)
    for z in (0.9, 2.4):
        w.lathe([(0.1, -0.08), (0.17, 0.0), (0.1, 0.08)], "Wood_Honey", n=8, c=(0, 0, z))
    w.blob((0, 0, 4.12), 0.12, "Metal_Brass", n=9, seed=1)
    for sx in (-1, 1):
        w.tube([(0, 0, 3.4), (sx * 0.4, 0, 3.95), (sx * 0.85, 0, 4.1), (sx * 1.15, 0, 3.9), (sx * 1.2, 0, 3.65), (sx * 1.08, 0, 3.55)], [0.06, 0.055, 0.05, 0.04, 0.03, 0.02], "Wood_Dark", n=6)
        top = lantern(g, w, (sx * 1.0, 0, 3.0), s=1.25, style=0 if sx < 0 else 1, seed=int(sx) + 2)
        r.tube([(sx * 1.0, 0, 4.04), (sx * 1.0, 0, top - 0.02)], 0.015, "Rope_Tan", n=3)
        a.light("Light", (sx * 1.0, 0, 3.0), WARM, 55)
    stone_ring(s, 0.32, 6, 0.22, seed=3)
    w.lathe([(0.12, 1.75), (0.24, 1.9), (0.22, 1.92), (0.0, 1.8)], "Straw_Dark", n=9, c=(0, -0.22, 0))
    f.blob((0, -0.22, 1.98), (0.2, 0.18, 0.12), "Leaf_Green", n=10, seed=2, shade=leaf_shade(GREEN3, 2))
    for i in range(5):
        f.blob((cos(i * 1.3) * 0.14, -0.22 + sin(i * 1.3) * 0.12, 2.06), 0.05, ("Flower_Red", "Flower_Yellow", "Flower_Purple")[i % 3], n=6, seed=i)
    a.part("Post", w); a.part("Glow", g, shadow=False); a.part("Ropes", r); a.part("Stones", s); a.part("Flowers", f)


@asset("Candles_Set", LI, ("light", "candles", "small", "interior"), "Cluster of seven dripping candles of different heights on a clay dish - a small warm light for tables, shelves and shrines. Origin: base.")
def _(a):
    w, g = B(), B()
    w.lathe([(0.0, 0.0), (0.26, 0.0), (0.32, 0.05), (0.28, 0.06), (0.0, 0.03)], "Clay_Teal", n=12)
    rnd = random.Random(2)
    for i in range(7):
        ang = tau * i / 6 if i < 6 else 0
        d = 0.16 if i < 6 else 0.0
        h = rnd.uniform(0.12, 0.36) if i < 6 else 0.44
        r_ = rnd.uniform(0.03, 0.045)
        c = (cos(ang) * d, sin(ang) * d, 0.03)
        w.lathe([(r_ * 1.3, 0.0), (r_, 0.03), (r_, h), (r_ * 0.5, h + 0.01), (0.0, h + 0.005)], "Plaster_Cream", n=7, c=c)
        w.blob((c[0] + r_, c[1], h * 0.7), (0.015, 0.015, 0.05), "Plaster_Cream", n=6, seed=i)
        g.lathe([(0.0, 0.0), (r_ * 0.6, 0.035), (0.0, 0.11)], "Glow_Warm", n=5, c=(c[0], c[1], c[2] + h + 0.01))
    a.part("Candles", w)
    a.part("Flames", g, shadow=False)
    a.light("Light", (0, 0, 0.55), WARM, 20)


@asset("Lanterns_Floating", LI, ("light", "lantern", "floating", "magic", "sky"), "Nine paper sky-lanterns drifting upward at different heights over a 5 m (author) wide patch - release them over plazas and bridges. Each colour group is its own object. Origin: ground centre below them.")
def _(a):
    gs = {"Glow_Warm": B(), "Glow_Pink": B(), "Glow_Teal": B()}
    w = B()
    rnd = random.Random(9)
    for i in range(9):
        x, y, z = rnd.uniform(-2.5, 2.5), rnd.uniform(-2.5, 2.5), 1.6 + i * 0.75 + rnd.uniform(-0.3, 0.3)
        gm = ("Glow_Warm", "Glow_Warm", "Glow_Pink", "Glow_Warm", "Glow_Teal")[i % 5]
        s_ = rnd.uniform(0.8, 1.3)
        M = T(x, y, z) @ RX(rnd.uniform(-0.15, 0.15)) @ RY(rnd.uniform(-0.15, 0.15))
        gs[gm].lathe([(0.11 * s_, 0.0), (0.2 * s_, 0.12 * s_), (0.22 * s_, 0.36 * s_), (0.15 * s_, 0.5 * s_), (0.0, 0.54 * s_)], gm, n=8, M=M)
        w.ring((0, 0, 0), 0.115 * s_, 0.012, "Wood_Pale", n=8, m=3, M=M)
        if i in (1, 4, 7):
            a.light("Light", (x, y, z + 0.2), WARM if gm != "Glow_Teal" else TEAL, 30)
    for gm, gb in gs.items():
        a.part(gm, gb, shadow=False)
    a.part("Rims", w)
