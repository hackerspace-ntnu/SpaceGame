"""components/mechanical/cargo_crane - hand-built loading gear for trader decks.

Traders swing goods aboard at every settlement, so the Sky Tribe's decks carry
cranes the way a quay does - and none of them came from a factory. The library's
`gantry_boom` and `drill_derrick` are industrial plant; these are what a
travelling people rig for themselves from spars, pipe and rope:

| Variation | Build | Reach |
|---|---|---|
| `TimberJib`    | a lashed twin-pole mast, a timber jib on a topping lift, a hand winch | 4.2 m |
| `ScrapDerrick` | an A-frame of rusted pipe, a pipe-lattice boom, a scrap counterweight | 5.0 m |
| `Davit`        | a slewing post with a curved plate arm and a handwheel - a light lift  | 2.2 m |
| `Sheerlegs`    | two timber legs splayed over the edge, a tackle, a guy to a deck cleat | 2.6 m |

Conventions: the crane stands on its origin at deck level and **reaches along
+X** - outboard, when a placement rotates it to face over the side. Each
logical part is its own object sharing that origin, so a placement stamps the
whole group with one transform. Every rig carries its load: goods from
`trade_goods`, hanging from the hook at `HOOK_Z` above the deck, so the load
dangles over the drop past the rail.

Nothing here collides; placements add their own footprint collision.

    blender --background --python cargo_crane.py -- --out cargo_crane.blend

Generation script - historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
for _p in (LIB, os.path.join(LIB, "components", "structural"), os.path.join(LIB, "components", "props")):
    if _p not in sys.path:
        sys.path.insert(0, _p)
import scrap_walkway as sw  # noqa: E402
import trade_goods as tg  # noqa: E402
from _buildlib import Part  # noqa: E402

from mathutils import Vector  # noqa: E402

MATS = sw.MATS + ["Mat_Metal_Steel_Worn"]    # 12 bare pulley sheaves and hooks
STEEL = len(sw.MATS)
HOOK_Z = 1.1                 # hook height above the deck; the load hangs below it
VARIANTS = ("TimberJib", "ScrapDerrick", "Davit", "Sheerlegs")
FOOTPRINT = {                # (x0, y0, x1, y1) on the deck, for placement collision
    "TimberJib": (-1.6, -1.2, 0.8, 1.2),
    "ScrapDerrick": (-2.4, -1.1, 0.9, 1.1),
    "Davit": (-0.45, -0.45, 0.45, 0.45),
    "Sheerlegs": (-3.2, -1.5, 0.4, 1.5),
}
REACH = {"TimberJib": 4.2, "ScrapDerrick": 5.0, "Davit": 2.2, "Sheerlegs": 2.6}


def pulley(p, at, axis_d, r=0.14):
    at = Vector(at)
    rot = Vector(axis_d).to_track_quat('Z', 'Y').to_matrix().to_4x4()
    p.cyl(at, r, 0.08, 'Z', seg=10, mat=STEEL, rot=rot)
    p.cyl(at, r * 1.25, 0.03, 'Z', seg=10, mat=sw.RUST_DEEP, rot=rot)


def hook(p, top, z_hook):
    top = Vector(top)
    eye = Vector((top.x, top.y, z_hook + 0.22))
    sw.pole(p, top, eye, 0.014, sw.ROPE, seg=5)
    p.box(eye - Vector((0, 0, 0.08)), (0.16, 0.12, 0.2), sw.DARK)
    p.torus(Vector((eye.x, eye.y, z_hook + 0.02)), 0.07, 0.022, axis='Y', maj_seg=10, min_seg=4,
            mat=STEEL)


def load(coll, mats_goods, name, kind, at, rng):
    """The goods on the hook, as their own object."""
    p = Part(mats_goods)
    at = Vector(at)
    if kind == "net":
        tg.net_bundle(p, at - Vector((0, 0, 0.6 * 1.25 + 0.02)), rng)
    elif kind == "bales":
        pallet = at - Vector((0, 0, 1.4))
        p.box(pallet, (1.3, 0.9, 0.1), tg.TIMBER)
        tg.bale(p, pallet + Vector((0, 0, 0.05)), rng)
        for sx in (-1, 1):
            for sy in (-1, 1):
                tg.pole(p, pallet + Vector((sx * 0.6, sy * 0.4, 0.05)), at, 0.012, tg.ROPE, seg=4)
    else:
        box_at = at - Vector((0, 0, 1.0))
        tg.chest(p, box_at, rng, yaw=0.3)
        for sx in (-1, 1):
            tg.pole(p, box_at + Vector((sx * 0.45, 0, 0.45)), at, 0.012, tg.ROPE, seg=4)
    p.bevel(width=0.006, segments=1)
    return p.finish(name, coll)


def timber_jib(part, rng):
    base, mast, boom, rig, winch, hk = (part(n) for n in ("Base", "Mast", "Boom", "Rigging", "Winch", "Hook"))
    for y in (-0.7, 0.7):
        base.box((-0.4, y, 0.09), (2.3, 0.22, 0.18), sw.TIMBER)
    base.box((-0.4, 0, 0.19), (1.3, 1.3, 0.04), sw.RUST)
    top = Vector((0.05, 0, 5.3))
    for y in (-0.12, 0.12):
        sw.pole(mast, (0, y, 0.2), top + Vector((0, y * 0.4, 0)), 0.11, sw.TIMBER, seg=7)
    for z in (1.0, 2.6, 4.2):
        sw.lashing(mast, (0.02, 0, z), 0.2, turns=3)
    heel = Vector((0.25, 0, 1.3))
    tip = Vector((REACH["TimberJib"], 0, 4.4))
    sw.pole(boom, heel, tip, 0.09, sw.TIMBER, seg=7)
    boom.box(heel, (0.3, 0.3, 0.2), sw.RUST_DEEP)
    pulley(rig, tip + Vector((0.08, 0, -0.15)), (0, 1, 0))
    pulley(rig, top + Vector((0, 0, 0.1)), (0, 1, 0), r=0.1)
    sw.rope(rig, top + Vector((0, 0, 0.05)), tip, sag=0.08)
    for y in (-1.1, 1.1):
        sw.rope(rig, top, (-1.45, y, 0.2), sag=0.1)
        rig.box((-1.45, y, 0.25), (0.12, 0.12, 0.12), sw.DARK)
    sw.rope(rig, (-0.35, 0, 0.75), top + Vector((0, 0, 0.12)), sag=0.03, r=0.02)
    winch.cyl((-0.4, 0, 0.75), 0.18, 0.7, 'Y', seg=10, mat=sw.TIMBER)
    for y in (-0.38, 0.38):
        winch.box((-0.4, y, 0.5), (0.1, 0.06, 0.6), sw.RUST_DEEP)
    sw.strut(winch, (-0.4, 0.45, 0.75), (-0.4, 0.6, 0.75), 0.04, 0.04, sw.DARK)
    sw.strut(winch, (-0.4, 0.6, 0.75), (-0.75, 0.6, 1.0), 0.04, 0.04, sw.DARK)
    hook(hk, tip + Vector((0.08, 0, -0.3)), HOOK_Z)
    return Vector((tip.x + 0.08, 0, HOOK_Z)), "net"


def scrap_derrick(part, rng):
    base, frame, boom, rig, weight, hk = (part(n) for n in
                                          ("Base", "AFrame", "Boom", "Rigging", "Counterweight", "Hook"))
    base.box((-0.7, 0, 0.06), (3.0, 2.0, 0.12), sw.HULLRUST)
    base.rivets((-2.1, -0.9, 0.12), (0.7, -0.9, 0.12), 8, radius=0.03, height=0.015, mat=sw.DARK)
    base.rivets((-2.1, 0.9, 0.12), (0.7, 0.9, 0.12), 8, radius=0.03, height=0.015, mat=sw.DARK)
    apex = Vector((0.35, 0, 5.0))
    for y in (-0.85, 0.85):
        sw.pole(frame, (0.2, y, 0.12), apex, 0.08, sw.RUST, seg=8)
        frame.cyl((0.2, y, 0.16), 0.16, 0.08, seg=8, mat=sw.DARK)
    sw.pole(frame, (-2.2, 0, 0.12), apex, 0.07, sw.RUST_DEEP, seg=8)
    for z in (1.6, 3.2):
        k = z / apex.z
        sw.pole(frame, Vector((0.2, -0.85, 0.12)).lerp(apex, k), Vector((0.2, 0.85, 0.12)).lerp(apex, k),
                0.04, rng.choice((sw.RUST_PALE, sw.YELLOW)), seg=6)
    heel = Vector((0.5, 0, 0.7))
    tip = Vector((REACH["ScrapDerrick"], 0, 3.4))
    for y in (-0.22, 0.22):
        sw.pole(boom, heel + Vector((0, y, 0)), tip + Vector((0, y * 0.3, 0)), 0.055, sw.RUST, seg=7)
    n = 7
    for i in range(1, n):
        a = heel.lerp(tip, i / n)
        sw.pole(boom, a + Vector((0, -0.22, 0)), a + Vector((0, 0.22, 0)), 0.025, sw.RUST_DEEP, seg=5)
    pulley(rig, apex + Vector((0.1, 0, 0)), (0, 1, 0))
    pulley(rig, tip + Vector((0.1, 0, -0.1)), (0, 1, 0))
    sw.pole(rig, apex, tip, 0.018, sw.DARK, seg=5)
    sw.pole(rig, apex + Vector((0, 0, -0.1)), (-1.6, 0, 0.7), 0.016, sw.DARK, seg=5)
    rig.cyl((-1.6, 0, 0.55), 0.28, 0.5, 'Y', seg=12, mat=sw.RUST_DEEP)
    rig.cyl((-1.6, 0.3, 0.55), 0.42, 0.05, 'Y', seg=16, mat=sw.DARK)
    sw.strut(rig, (-1.6, 0.36, 0.55), (-1.95, 0.36, 0.95), 0.04, 0.04, sw.DARK)
    weight.cyl((-2.2, 0, 0.62), 0.38, 1.0, seg=12, mat=sw.RUST_DEEP)
    weight.greeble((-2.45, -0.25, 1.05), (-1.95, 0.25, 1.25), 7, seed=rng.randint(0, 999),
                   scale=(0.12, 0.3), mat=sw.RUST)
    hook(hk, tip + Vector((0.1, 0, -0.25)), HOOK_Z)
    return Vector((tip.x + 0.1, 0, HOOK_Z)), "bales"


def davit(part, rng):
    base, post, arm, wheel, hk = (part(n) for n in ("Base", "Post", "Arm", "Handwheel", "Hook"))
    base.cyl((0, 0, 0.05), 0.42, 0.1, seg=12, mat=sw.RUST_DEEP)
    for i in range(8):
        a = math.tau * i / 8
        base.cyl((math.cos(a) * 0.34, math.sin(a) * 0.34, 0.11), 0.03, 0.04, seg=6, mat=sw.DARK)
    post.cyl((0, 0, 1.6), 0.13, 3.1, seg=12, mat=sw.RUST)
    post.cyl((0, 0, 0.35), 0.18, 0.5, seg=12, mat=sw.HULLRUST)
    r = REACH["Davit"] / 2
    center = Vector((r, 0, 3.15))
    pts = [center + Vector((-math.cos(a) * r, 0, math.sin(a) * r))
           for a in (math.radians(d) for d in range(0, 181, 20))]
    for a, b in zip(pts, pts[1:]):
        sw.strut(arm, a, b, 0.16, 0.2, sw.RUST)
        sw.strut(arm, a + Vector((0, 0, 0.12)), b + Vector((0, 0, 0.12)), 0.03, 0.03, sw.RUST_PALE)
    tip = pts[-1]
    pulley(arm, tip + Vector((0, 0, -0.1)), (0, 1, 0), r=0.12)
    wheel.torus((0.2, 0, 1.2), 0.25, 0.03, axis='X', maj_seg=14, min_seg=5, mat=sw.DARK)
    for i in range(4):
        a = math.tau * i / 4
        sw.pole(wheel, (0.2, 0, 1.2), (0.2, math.cos(a) * 0.25, 1.2 + math.sin(a) * 0.25), 0.015,
                sw.DARK, seg=5)
    sw.rope(wheel, (0.15, 0, 1.3), tip + Vector((-0.1, 0, 0)), sag=0.05, r=0.018)
    hook(hk, tip + Vector((0, 0, -0.25)), HOOK_Z)
    return Vector((tip.x, 0, HOOK_Z)), "chest"


def sheerlegs(part, rng):
    legs, tackle, guys, cleat, hk = (part(n) for n in ("Legs", "Tackle", "Guys", "Cleat", "Hook"))
    apex = Vector((REACH["Sheerlegs"], 0, 5.2))
    for y in (-1.3, 1.3):
        sw.pole(legs, (0, y, 0.0), apex + Vector((0, y * 0.05, 0)), 0.1, sw.TIMBER, seg=7)
        legs.box((0, y, 0.08), (0.4, 0.4, 0.16), sw.RUST_DEEP)
    sw.lashing(legs, apex - Vector((0.02, 0, 0.18)), 0.2, turns=4)
    upper = apex - Vector((0, 0, 0.45))
    lower = Vector((apex.x, 0, HOOK_Z + 1.1))
    pulley(tackle, upper, (0, 1, 0), r=0.16)
    pulley(tackle, lower, (0, 1, 0), r=0.14)
    for dy in (-0.05, 0.05):
        sw.pole(tackle, upper + Vector((0, dy, 0)), lower + Vector((0, dy, 0)), 0.012, sw.ROPE, seg=4)
    sw.rope(guys, apex, (-3.0, 0, 0.35), sag=0.15)
    sw.rope(guys, upper, (-2.6, 0.35, 0.3), sag=0.25)
    cleat.box((-2.9, 0.1, 0.1), (0.6, 0.8, 0.2), sw.TIMBER)
    cleat.box((-2.9, 0.1, 0.3), (0.12, 0.5, 0.2), sw.DARK)
    hook(hk, lower - Vector((0, 0, 0.2)), HOOK_Z)
    return Vector((apex.x, 0, HOOK_Z)), "net"


BUILDERS = {"TimberJib": timber_jib, "ScrapDerrick": scrap_derrick, "Davit": davit, "Sheerlegs": sheerlegs}


def build_variant(coll, mats, mats_goods, variant, rng):
    """Emit one crane as separate part objects plus its load. Returns the objects."""
    parts = {}

    def part(name):
        parts[name] = Part(mats)
        return parts[name]

    ring, kind = BUILDERS[variant](part, rng)
    made = []
    for name, p in parts.items():
        p.bevel(width=0.01, segments=1)
        made.append(p.finish("Mesh_CargoCrane_%s_%s" % (variant, name), coll))
    made.append(load(coll, mats_goods, "Mesh_CargoCrane_%s_Load" % variant, kind, ring, rng))
    return made


def build():
    from _buildlib import collection, link_materials, parse_out, report, save, start
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    mats_goods = link_materials(tg.MATS)
    for i, variant in enumerate(VARIANTS):
        build_variant(collection("Coll_CargoCrane_" + variant), mats, mats_goods, variant,
                      random.Random(20260918 + i))
    report()
    save(out)


if __name__ == "__main__":
    build()
