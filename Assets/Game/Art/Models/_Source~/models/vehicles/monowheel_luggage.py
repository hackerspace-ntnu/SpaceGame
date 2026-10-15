"""Luggage for the desert monowheels -- one implementation, every variant that carries any.

Run from inside an open .blend (Blender MCP); a variant script builds a config and calls
`add(cfg)`, which returns the new objects for the caller to parent. Nothing here saves.

- **Ski** -- the rug-wrapped bundle, bedroll and lashing from the single-wheel generator's
  own `build_cargo`, plus optional trade-goods sacks. The deck is not level in world space
  (the rigs are tilted), so everything is set on a plane fitted to the deck's measured top.
- **Sides** -- a saddlebag slung by loops over each side hoop, measured off the hoop's
  own tube, with a per-side kit on top of it: a tied bedroll, or canisters roped to the
  hoop behind the bag. Different per side on purpose; mirrored luggage reads as moulded-on.

Config keys:

| key | meaning |
| --- | --- |
| `tag` | variant suffix, e.g. `"Hauler"` |
| `coll` | collection the pieces go in |
| `deck` | ski deck object whose top surface the cargo sits on |
| `deck_fit` | (y0, y1): straight stretch of the ski used to fit the deck plane |
| `bundle_y` | world y of the bundle's centre |
| `sacks` | [(x, y, trade-goods sack name)] -- side from the sign of x, none when centred |
| `sack_rest` | optional ([object names], (y0, y1), max abs x): seat the sacks on these objects' top instead of the ski, e.g. a rear rack |
| `hoops` | {+1: left hoop name, -1: right hoop name} |
| `bag_y`, `bag_size` | saddlebag centre along the hoop; (depth, length, height) |
| `kits` | {+1: "bedroll" or "canisters", -1: ...} |
| `canister_y` | stations along the hoop for the canisters |
"""

import math
import os

import bpy
from mathutils import Matrix, Vector

import _buildlib as B
import desert_monowheel as DM

TRADE_GOODS = os.path.join(B.LIB_ROOT, "components", "props", "trade_goods.blend")
CANISTERS = ("Mesh_TradeGoods_CanisterRack_Can01", "Mesh_TradeGoods_CanisterRack_Can04")
DECK_INNER = 0.6    # |x| inside the narrowest ski's rails, so only deck planks are fitted
MAX_TUBE_R = 0.1    # a side hoop's tube is ~0.035 m; anything past this is a mis-measure


def use_palette():
    DM.M = dict(zip(DM.MAT_NAMES, B.link_materials(list(DM.MAT_NAMES.values()))))
    DM.M["brass"] = B.link_materials(["Mat_Metal_Brass_Tarnished"])[0]


def side_tag(sign):
    return "L" if sign > 0 else "R"


# ── measured frames ──────────────────────────────────────────────────────────

def deck_frame(deck, fit):
    """Least-squares line through the deck's top surface: z = a + b*y."""
    return surface_frame([deck], fit, DECK_INNER)   # |x| limit skips the raised rails


def surface_frame(objs, fit, x_max):
    """Least-squares line z = a + b*y through the top of `objs` within |x| < x_max."""
    tops = {}
    for o in objs:
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            if abs(p.x) < x_max and fit[0] <= p.y <= fit[1]:
                k = round(p.y * 10)
                tops[k] = max(tops.get(k, -1e9), p.z)
    ys, zs = [k / 10 for k in tops], list(tops.values())
    n, sy, sz = len(ys), sum(ys), sum(zs)
    b = (n * sum(y * z for y, z in zip(ys, zs)) - sy * sz) / (n * sum(y * y for y in ys) - sy * sy)
    a = (sz - b * sy) / n
    return (lambda y: a + b * y), Matrix.Rotation(math.atan(b), 4, 'X')


def rail_at(hoop, y, side):
    """Centre and radius of a side hoop's tube at station y.

    The hoop's long straight runs have no vertices between their end sections, so
    the tube is measured at its cross-sections and interpolated between the two
    either side of `y`.
    """
    sections = {}
    for v in hoop.data.vertices:
        p = hoop.matrix_world @ v.co
        sections.setdefault(round(p.y / 0.02), []).append(p)

    def measure(pts):
        out = max(side * p.x for p in pts)
        near = [p for p in pts if side * p.x > out - 0.2]
        z0, z1 = min(p.z for p in near), max(p.z for p in near)
        r = (z1 - z0) / 2
        return Vector((side * (out - r), 0.0, (z0 + z1) / 2)), r

    keys = sorted(sections)
    lo = max((k for k in keys if k * 0.02 <= y), default=keys[0])
    hi = min((k for k in keys if k * 0.02 >= y), default=keys[-1])
    (c0, r0), (c1, r1) = measure(sections[lo]), measure(sections[hi])
    if max(r0, r1) > MAX_TUBE_R:
        # Where the hoop curves up, one y-section cuts both of its runs and the
        # "tube" reads a metre thick; a loop sized from that wraps through the seat.
        raise RuntimeError("Hoop %s is not a single tube at y = %.2f (radius %.2f) -- "
                         "move this station off the bend" % (hoop.name, y, max(r0, r1)))
    t = 0.0 if hi == lo else (y - lo * 0.02) / ((hi - lo) * 0.02)
    c = c0.lerp(c1, t)
    return Vector((c.x, y, c.z)), r0 + (r1 - r0) * t


# ── placing appended goods ───────────────────────────────────────────────────

def seat(obj, anchor, frame):
    """Origin to the object's bottom centre, then stand it on `anchor` in `frame`."""
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    vs = [v.co for v in obj.data.vertices]
    c = Vector(((min(v.x for v in vs) + max(v.x for v in vs)) / 2,
                (min(v.y for v in vs) + max(v.y for v in vs)) / 2, min(v.z for v in vs)))
    obj.data.transform(Matrix.Translation(-c))
    obj.matrix_world = Matrix.Translation(anchor) @ frame


def trade_good(cfg, src, name, anchor, frame):
    obj = B.append_objects(TRADE_GOODS, [src], cfg["coll"])[0]
    obj.name = obj.data.name = name
    seat(obj, anchor, frame)
    return obj


# ── ski ──────────────────────────────────────────────────────────────────────

def ski_luggage(cfg):
    O = bpy.data.objects
    deck_z, frame = deck_frame(O[cfg["deck"]], cfg["deck_fit"])
    before = set(O)

    class Stub:            # build_cargo centres its bundle at y0 - 2.1 on a deck at SKI_Z
        y0 = 2.1
    DM.build_cargo({"name": cfg["tag"]}, cfg["coll"], Stub)
    gen_deck = DM.SKI_Z + DM.DECK_T / 2
    y = cfg["bundle_y"]
    place = (Matrix.Translation((0.0, y, deck_z(y) - 0.005)) @ frame
             @ Matrix.Translation((0.0, 0.0, -gen_deck)))
    made = [o for o in O if o not in before]
    # A new object reports an identity matrix_world until the view layer updates;
    # composing onto that would drop every piece's own origin offset.
    bpy.context.view_layer.update()
    for o in made:
        o.matrix_world = place @ o.matrix_world

    if cfg.get("sack_rest"):                       # sacks ride somewhere other than the ski
        names, fit, x_max = cfg["sack_rest"]
        deck_z, frame = surface_frame([O[n] for n in names], fit, x_max)
    for x, sy, src in cfg["sacks"]:
        side = "" if abs(x) < 0.05 else side_tag(x)
        sack = trade_good(cfg, src, "Mesh_CargoSack%s_%s" % (side, cfg["tag"]),
                          Vector((x, sy, deck_z(sy) - 0.01)), frame)
        if sack.dimensions.x > sack.dimensions.y:        # narrow side across the ski
            sack.matrix_world = (Matrix.Translation(sack.matrix_world.translation) @ frame
                                 @ Matrix.Rotation(math.radians(90), 4, 'Z'))
        made.append(sack)
    return made


# ── sides ────────────────────────────────────────────────────────────────────

def saddlebag(cfg, side, body_mat, flap_mat):
    hoop = bpy.data.objects[cfg["hoops"][side]]
    tag, coll, bag_y = side_tag(side), cfg["coll"], cfg["bag_y"]
    rail, r = rail_at(hoop, bag_y, side)
    u_rail = abs(rail.x)
    depth, length, height = cfg["bag_size"]
    u_in, u_out = u_rail + r + 0.02, u_rail + r + 0.02 + depth
    z_top = rail.z - 0.02
    X = lambda u: side * u
    made = []

    body = DM.part(body_mat)
    body.box((X((u_in + u_out) / 2), bag_y, z_top - height / 2), (depth, length, height))
    body.bevel(width=0.06, segments=3, angle=30.0)
    made.append(body.finish("Mesh_Saddlebag%s_%s" % (tag, cfg["tag"]), coll, origin=(X(u_in), bag_y, z_top)))

    # The flap starts behind the hoop, rolls over it, across the bag top and down its face.
    path = [(u_rail - 0.03, rail.z + r + 0.012), (u_rail + r * 0.9, rail.z + r * 0.55),
            (u_in + 0.03, z_top + 0.018), (u_out - 0.04, z_top + 0.018),
            (u_out + 0.018, z_top - 0.07), (u_out + 0.018, z_top - 0.30)]
    rows = [[Vector((X(u), bag_y + dy, z)) for u, z in path]
            for dy in (-length / 2 + 0.03, 0.0, length / 2 - 0.03)]
    flap = DM.part(flap_mat)
    flap.sheet(rows, 0.02)
    made.append(flap.finish("Mesh_SaddlebagFlap%s_%s" % (tag, cfg["tag"]), coll, origin=rail))

    fit = DM.part("seat", "brass")
    for dy in (-0.2, 0.2):
        y = bag_y + dy
        fit.box((X(u_out + 0.032), y, z_top - 0.27), (0.012, 0.06, 0.42))
        fit.box((X(u_out + 0.042), y, z_top - 0.36), (0.016, 0.085, 0.06), mat=1)
        fit.torus((rail.x, y, rail.z), r + 0.022, 0.016, axis='Y', maj_seg=14, min_seg=6)
    made.append(fit.finish("Mesh_SaddlebagFittings%s_%s" % (tag, cfg["tag"]), coll, origin=rail))
    return made, rail, r, u_in, u_out, z_top


def bedroll_kit(cfg, side):
    made, rail, r, u_in, u_out, z_top = saddlebag(cfg, side, "bundle", "seat")
    tag, coll = side_tag(side), cfg["coll"]
    roll_c = Vector((side * (u_in + u_out) / 2, cfg["bag_y"], z_top + 0.018 + 0.14))
    roll = DM.part("canvas")
    roll.cyl(roll_c, 0.15, 0.9, axis='Y', seg=16)
    roll.bevel(width=0.03, segments=2)
    made.append(roll.finish("Mesh_SideBedroll%s_%s" % (tag, cfg["tag"]), coll, origin=roll_c))
    ties = DM.part("rope")
    for dy in (-0.28, 0.28):
        ties.torus(roll_c + Vector((0, dy, 0)), 0.152, 0.015, axis='Y', maj_seg=18, min_seg=6)
        DM.bar(ties, roll_c + Vector((-side * 0.1, dy, 0.08)), rail + Vector((0, dy, r * 0.8)), r=0.012)
    made.append(ties.finish("Mesh_SideBedrollTies%s_%s" % (tag, cfg["tag"]), coll, origin=roll_c))
    return made


def canister_kit(cfg, side):
    made, *_ = saddlebag(cfg, side, "seat", "canvas")
    tag, hoop = side_tag(side), bpy.data.objects[cfg["hoops"][side]]
    ropes = DM.part("rope")
    for i, (y, src) in enumerate(zip(cfg["canister_y"], CANISTERS)):
        rail, r = rail_at(hoop, y, side)
        top = rail.z - r - 0.12
        can = trade_good(cfg, src, "Mesh_Canister%d%s_%s" % (i, tag, cfg["tag"]),
                         Vector((0, 0, 0)), Matrix.Identity(4))
        u = abs(rail.x) + r + can.dimensions.x / 2 + 0.03
        can.matrix_world = Matrix.Translation((side * u, y, top - can.dimensions.z))
        ropes.torus((rail.x, y, rail.z), r + 0.018, 0.012, axis='Y', maj_seg=12, min_seg=6)
        DM.bar(ropes, Vector((rail.x + side * r * 0.7, y, rail.z - r * 0.7)),
               Vector((side * u, y, top + 0.01)), r=0.012)
        made.append(can)
    first = rail_at(hoop, cfg["canister_y"][0], side)[0]
    made.append(ropes.finish("Mesh_CanisterRopes%s_%s" % (tag, cfg["tag"]), cfg["coll"], origin=first))
    return made


KITS = {"bedroll": bedroll_kit, "canisters": canister_kit}


def add(cfg):
    """Build every piece the config asks for; returns them, unparented."""
    use_palette()
    made = ski_luggage(cfg)
    for side, kit in cfg["kits"].items():
        made += KITS[kit](cfg, side)
    bpy.context.view_layer.update()
    return made
