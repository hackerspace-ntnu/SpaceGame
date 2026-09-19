"""models/vehicles/sky_city_street - the sky city's lanes as a lived-in street.

Called from `sky_city_traversal.run()` in the same live-Blender pass, under the
same ownership and re-run rules; never run on its own. It does four things the
user asked for on 2026-09-16:

1. **Crossings between the two lanes.** The lanes run either side of the keel's
   open truss, and nothing bridged it: the collision had an invisible floor
   there, the eye saw a four-metre drop. Where two gas bags meet the bag ends
   leave 7.6 m of headroom, so each join gets a plank bridge. Everywhere else
   the lanes' inner edge is railed, and the castle's aft door opens onto a porch.
2. **Ladders where you would want to go.** Up a clear shaft beside each join to
   the crown gantry; onto the roofs of the box shacks; onto the castle's first
   terrace. The game has no climbing yet, so every ladder is also written as a
   `LAD_SkyCity_##` empty for the climbing logic to come (see `ladder_marker`).
   Every ladder top is a gap of `scrap_walkway.LADDER_W` (0.7 m) in a railing,
   narrower than the 1.0 m capsule, so until then a ladder is not a hole.
3. **The broken houses.** The shanties come from `shanty_addon`, which models
   every one of them against a host wall - the mounting face is x = 0 - and the
   city stood them on the open deck with that face to the lane: bare weld pads,
   a lean-to roof leaning on nothing, brackets reaching for a hull that is not
   there. Two meshes (`Box`, `Awning`) had also been stretched to 11 and 14 m
   and are restored from the library, the lean-to's roof sheets are turned the
   right way (its generator tilts them uphill), below-deck brackets are removed,
   and every shanty gets a host: a patched, painted street wall.
4. **A favela street.** Washing strung between walls, string lights and cables
   across and along the lanes, stalls, stoves and stools in the gaps, plants on
   every sill and wall foot, birdcages, dishes, and rooftop terraces on the box
   shacks - all from `street_life` (GDC-L1-LEVEL-0007: the street should tell
   you people live here before anyone is in it).
"""

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

import _buildlib as bl
import scrap_walkway as sw
import sky_city as sc
import sky_city_traversal as t
import street_life as sl
from _buildlib import Part

SHANTY_LIB = os.path.join(t.LIB, "components", "structural", "shanty_addon.blend")
SHANTY_KINDS = ("Shanty_Box", "Shanty_Awning", "Shanty_LeanTo", "Shanty_Stack", "Shanty_Water")
RESTORE = ("Shanty_Box", "Shanty_Awning")     # stretched in the city, intact in the library
LEANTO = dict(reach=3.4, high=3.15, low=1.85)  # `shanty_addon.build_leanto`'s roof, measured
BELOW_DECK = dict(lo=-0.3, hi=0.25)            # islands wholly this low are under-deck brackets
# Two shanties the city placed a metre inside their neighbours: the lean-to at
# y 30 half through the capsule beside it, the lean-to at y -20 through the pod.
# World y, set absolutely, so a re-run leaves them where they are.
HOUSE_MOVES = {"Mesh_SkyCity_Home18_Shanty_LeanTo": 31.04, "Mesh_SkyCity_Home04_Shanty_LeanTo": -18.63}
DECK_LEVEL = 5.0                               # shanties standing higher are on the stern terrace
STILTS_FROM = 0.2                              # a shanty raised more than this stands on stilts
STILT_INSET = 0.2
LADDER_KEEP_OUT = 1.0                          # along a wall, either side of a ladder
FACADE_T = 0.25
FACADE_OVERHANG = 0.3                          # host wall past each end of the weld pad
FACADE_RISE = (0.25, 0.8)                      # host wall above the pad top, where there is no roof

LANE_IN, LANE_OUT = sc.SIDE_IN, sc.LANE_OUT
UP = Vector((0.0, 0.0, 1.0))
UNDER = UP * -1.0
# Where two bags meet: the plank bridge's y span, the line people walk it on,
# and the ladder shaft beside it (rail line y, centre x, and which way the
# climber faces). The shafts were found by search: columns clear 0.55 m round
# from the deck to the gantry, and these are the ones nearest the centreline.
CROSSINGS = (
    dict(y=(-15.9, -13.3), walk_y=-14.4, ladder_x=-2.3, ladder_y=-15.25, facing=(0.0, 1.0)),
    dict(y=(15.3, 17.6), walk_y=16.2, ladder_x=2.1, ladder_y=17.05, facing=(0.0, -1.0)),
)
SHAFT_LANDING = 1.05                           # depth of the landing behind a shaft ladder's top
PORCH_DEPTH = 2.5
LANE_RAIL_END = t.EXT_Y1
LANE_POLE_H = 2.45                             # under the bags' flanks at the lane's inner edge
LANE_POLE_GAP = (5.0, 7.5)
SIDEWALK_MIN = LANE_OUT + 0.35                 # nothing stands inboard of this
LADDER_COLL = "Coll_SkyCity_Ladders"
CLIMB_CLEAR = 0.45                             # climber's axis in front of the rail line


# ===========================================================================
# Houses
# ===========================================================================

def shanty_kind(o):
    return next((k for k in SHANTY_KINDS if k in o.name), None)


def shanties():
    """The deck's shanties. The water tank up on the stern terrace is left alone:
    it stands on the terrace, not against a wall at street level."""
    return [o for o in t.city_objects("Mesh_SkyCity_Home")
            if shanty_kind(o) and len(o.data.vertices) and o.location.z < DECK_LEVEL]


def library_meshes():
    with bpy.data.libraries.load(SHANTY_LIB, link=False) as (src, dst):
        dst.meshes = ["Mesh_" + k for k in RESTORE]
    return dict(zip(RESTORE, dst.meshes))


def repair_houses(report):
    """Restore, trim and fix every shanty. Returns the object names it changed."""
    lib = library_meshes()
    changed, restored, trimmed, roofs = [], 0, 0, 0
    for name, y in HOUSE_MOVES.items():
        bpy.data.objects[name].location.y = y
    # Anything an earlier run changed that this pass now leaves alone goes back.
    for o in t.city_objects("Mesh_SkyCity_Home"):
        if o.location.z >= DECK_LEVEL and bpy.data.meshes.get(o.name + "__pre_traversal"):
            t.pristine(o)
            o.pop("sky_city_hash", None)
    for o in shanties():
        kind = shanty_kind(o)
        t.pristine(o)
        if kind in RESTORE:
            old = o.data
            o.data = lib[kind].copy()
            o.data.name = o.name
            if old.users == 0:
                bpy.data.meshes.remove(old)
            restored += 1

        def fix(bm, kind=kind):
            n = t.delete_islands(bm, lambda lo, hi: hi.z < BELOW_DECK["hi"] and lo.z < BELOW_DECK["lo"],
                                 None, "below-deck brackets")
            fixed = fix_leanto_roof(bm) if kind == "Shanty_LeanTo" else 0
            return n, fixed
        n, fixed = t.with_bmesh(o, fix)
        trimmed += n
        roofs += fixed
        changed.append(o.name)
    for m in lib.values():
        if m.users == 0:
            bpy.data.meshes.remove(m)
    report["Houses: restored from library / brackets trimmed / roof parts turned"] = \
        "%d / %d / %d" % (restored, trimmed, roofs)
    return changed


def fix_leanto_roof(bm):
    """Turn the lean-to's roof sheets and ribs to fall AWAY from the wall.

    `build_leanto` rotates them by -atan(rise/reach) about Y, which lifts their
    outer ends: the roof climbs from the wall's foot to the front and cuts
    straight through both gable walls. Each piece is turned about its own centre
    by twice that angle, and the result is asserted, not assumed.
    """
    ang = math.atan2(LEANTO["high"] - LEANTO["low"], LEANTO["reach"])
    sheets = [g for g, lo, hi in t.islands(bm)
              if hi.x - lo.x > LEANTO["reach"] * 0.9 and hi.y - lo.y > 0.5
              and lo.z > LEANTO["low"] - 0.1 and hi.z < LEANTO["high"] + 0.2]
    ribs = [g for g, lo, hi in t.islands(bm)
            if hi.x - lo.x < 0.12 and hi.y - lo.y > 3.0 and lo.z > LEANTO["low"] and hi.z < LEANTO["high"] + 0.2]
    if len(sheets) != 5 or len(ribs) != 9:
        raise RuntimeError("lean-to roof: expected 5 sheets and 9 ribs, found %d and %d" % (len(sheets), len(ribs)))
    for g in sheets + ribs:
        c = sum((v.co for v in g), Vector()) / len(g)
        m = Matrix.Translation(c) @ Matrix.Rotation(2 * ang, 4, 'Y') @ Matrix.Translation(-c)
        for v in g:
            v.co = m @ v.co
    g = sheets[0]
    near = [v.co.z for v in g if v.co.x < 1.0]
    far = [v.co.z for v in g if v.co.x > LEANTO["reach"] - 1.0]
    if sum(near) / len(near) <= sum(far) / len(far):
        raise RuntimeError("lean-to roof still rises away from the wall")
    return len(sheets) + len(ribs)


def pad_bounds(o):
    """The weld pad - the island standing on the mounting face - in object space."""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    pads = [(lo, hi) for g, lo, hi in t.islands(bm)
            if lo.x > -0.02 and hi.x < 0.14 and hi.z - lo.z > 2.0 and hi.y - lo.y > 2.0]
    bm.free()
    if len(pads) != 1:
        raise RuntimeError("%s: expected one weld pad, found %d" % (o.name, len(pads)))
    return pads[0]


def box_roof(o):
    """A box shack's flat roof slab and what stands on it, in object space."""
    bm = bmesh.new()
    bm.from_mesh(o.data)
    isl = t.islands(bm)
    bm.free()
    slabs = [(lo, hi) for g, lo, hi in isl if hi.x - lo.x > 3.5 and hi.y - lo.y > 3.5 and hi.z - lo.z < 0.3
             and lo.z > 2.5]
    if len(slabs) != 1:
        raise RuntimeError("%s: expected one roof slab, found %d" % (o.name, len(slabs)))
    lo, hi = slabs[0]
    fittings = [(flo, fhi) for g, flo, fhi in isl if flo.z >= hi.z - 0.05 and fhi.x - flo.x < 1.0]
    return lo, hi, fittings


class HouseFrame:
    """One shanty's object space, turned into world directions and lengths."""

    def __init__(self, o):
        self.o = o
        self.mw = o.matrix_world
        self.k = o.scale.x

    def point(self, x, y, z):
        return self.mw @ Vector((x, y, z))

    def direction(self, x, y):
        d = self.mw.to_3x3() @ Vector((x, y, 0.0))
        d.z = 0.0
        return d.normalized()


def host_walls(mats, vis, col):
    """A street wall behind every shanty's weld pad. Returns one record per wall."""
    walls = []
    for o in shanties():
        kind = shanty_kind(o)
        f = HouseFrame(o)
        plo, phi = pad_bounds(o)
        rng = t.rng_for("facade", o.name)
        along = f.direction(0.0, -1.0)                 # so `along x up` faces the lane
        n = along.cross(UP)
        width = (phi.y - plo.y) * f.k + 2 * FACADE_OVERHANG
        roof = box_roof(o) if kind == "Shanty_Box" else None
        top_local = roof[1].z if roof else phi.z + rng.uniform(*FACADE_RISE) / f.k
        height = f.point(0.0, 0.0, top_local).z - f.point(0.0, 0.0, 0.0).z
        centre_y = (plo.y + phi.y) / 2
        base = f.point(0.0, centre_y, 0.0) + n * (FACADE_T - 0.005)
        base.z = 0.0
        # The ladder goes at the far end of the window half; the door takes the other.
        door_side = rng.choice((-1, 1))
        ladder_u = -door_side * (width / 2 - 0.6) if roof else None
        p = Part(mats.street)
        sl.facade(p, base, along, width, height, rng, door=True, window=roof is None, thick=FACADE_T,
                  door_side=door_side)
        t.kit_object(p, "Mesh_SkyCity_Facade_%s" % o.name.replace("Mesh_SkyCity_", ""), vis)
        corners = [base + along * (s * width / 2) - n * dd for s in (-1, 1) for dd in (0.0, FACADE_T)]
        col.hull([c + UP * z for c in corners for z in (0.0, height)])
        walls.append(dict(house=o, kind=kind, base=base, along=along, n=n, width=width, height=height,
                          roof=roof, frame=f, ladder_u=ladder_u))
    return walls


def stilts(mats, vis, col):
    """Posts and cross-bracing under every shanty the city raised off the deck.

    Several stand 0.45-1.35 m up. They used to hang there on stretched brackets
    reaching metres under the deck; with those gone, this is what holds them up.
    """
    count = 0
    for o in shanties():
        f = HouseFrame(o)
        floor_z = f.point(0.0, 0.0, 0.0).z
        if floor_z < STILTS_FROM:
            continue
        bm = bmesh.new()
        bm.from_mesh(o.data)
        body = [v.co for v in bm.verts if -0.2 < v.co.z < 0.5]
        bm.free()
        x0, x1 = min(v.x for v in body) + STILT_INSET, max(v.x for v in body) - STILT_INSET
        y0, y1 = min(v.y for v in body) + STILT_INSET, max(v.y for v in body) - STILT_INSET
        rng = t.rng_for("stilts", o.name)
        p = Part(mats.kit)
        feet = [f.point(x, y, 0.0) for x in (x0, x1) for y in (y0, y1)]
        for q in feet:
            base = Vector((q.x, q.y, 0.0))
            t.post(p, col, base.x, base.y, 0.0, q.z, rng)
        for a, b in ((0, 3), (1, 2)):
            fa, fb = feet[a], feet[b]
            sw.strut(p, Vector((fa.x, fa.y, 0.1)), fb - UP * 0.1, 0.07, 0.07, sw.RUST_DEEP)
        t.kit_object(p, "Mesh_SkyCity_Stilts_%s" % o.name.replace("Mesh_SkyCity_", ""), vis)
        count += 1
    return count


# ===========================================================================
# Ladders
# ===========================================================================

def ladder(mats, vis, col, name, foot, facing, top_z, exit_point, style, rng):
    """Draw a ladder, collide its rails, and record it for the climbing logic."""
    foot = Vector(foot)
    f = Vector((facing[0], facing[1], 0.0)).normalized()
    s = f.cross(UP)
    p = Part(mats.kit)
    sw.ladder(p, foot, top_z, f, rng, style=style)
    t.kit_object(p, "Mesh_SkyCity_Ladder_%s" % name, vis)
    for sx in (-1, 1):
        c = foot + s * (sx * sw.LADDER_W / 2)
        col.box((c.x - 0.05, c.y - 0.05, foot.z), (c.x + 0.05, c.y + 0.05, top_z + sw.GRAB))
    return dict(name=name, foot=foot, facing=f, top_z=top_z, exit=Vector(exit_point))


def ladder_marker(coll, index, lad):
    """`LAD_SkyCity_##`: an empty at the ladder's foot, local +Y toward the climber.

    Custom properties carry the rest: `climb_top_z`, the height a climber steps
    off at, and `exit`, the world point they step off onto. Exported with FBX
    custom properties on, these are everything a ladder component needs; the
    rail geometry and its collision are already in the model.
    """
    e = bpy.data.objects.new("LAD_SkyCity_%02d" % index, None)
    e.empty_display_type = 'SINGLE_ARROW'
    e.empty_display_size = 1.0
    e.location = lad["foot"]
    e.rotation_euler = (0.0, 0.0, math.atan2(-lad["facing"].x, lad["facing"].y))
    e["climb_top_z"] = lad["top_z"]
    e["exit"] = list(lad["exit"])
    e["ladder_name"] = lad["name"]
    coll.objects.link(e)
    return t.own(e)


# ===========================================================================
# Crossings, porch, lane rails, shafts
# ===========================================================================

def crossings(mats, vis, col, ladders):
    kit = mats.kit
    off = t.castle_offset()
    castle_aft = sc.CASTLE_Y1 + off.y
    rng = t.rng_for("crossings")
    p = Part(kit)
    spans = []
    for i, c in enumerate(CROSSINGS, start=1):
        y0, y1 = c["y"]
        t.floor(p, col, (-LANE_IN - 0.3, y0), (LANE_IN + 0.3, y1), 0.0, rng.choice(("planks", "scrap")), rng,
                across='y')
        for y in (y0 + 0.05, y1 - 0.05):
            t.railing(p, col, (-LANE_IN, y), (LANE_IN, y), 0.0, rng.choice(("rope", "net")), rng)
        for x in (-LANE_IN + 0.8, LANE_IN - 0.8):
            sw.strut(p, (x, y0 + 0.2, -0.25), (x, y1 - 0.2, -0.25), 0.16, 0.2, sw.RUST_DEEP)
        spans.append((y0, y1))
        ladders.append(shaft(mats, vis, col, i, c, rng))
    # The castle's aft door opens onto the keel: a porch joins it to both lanes.
    t.floor(p, col, (-LANE_IN - 0.3, castle_aft), (LANE_IN + 0.3, castle_aft + PORCH_DEPTH), 0.0, "plate", rng)
    t.railing(p, col, (-LANE_IN, castle_aft + PORCH_DEPTH - 0.05), (LANE_IN, castle_aft + PORCH_DEPTH - 0.05), 0.0,
              "pipe", rng)
    spans.append((castle_aft, castle_aft + PORCH_DEPTH))
    t.railing(p, col, (-LANE_IN, LANE_RAIL_END), (LANE_IN, LANE_RAIL_END), 0.0, "rope", rng)
    t.kit_object(p, "Mesh_SkyCity_Crossings", vis)

    # The lanes' inner edges, railed except where a crossing or the porch meets
    # them, with tall poles to string things from.
    poles = []
    for sx in (-1, 1):
        p = Part(kit)
        cur = castle_aft + PORCH_DEPTH
        for a, b in sorted(spans) + [(LANE_RAIL_END, LANE_RAIL_END)]:
            if b <= cur:
                continue
            if a > cur:
                t.railing(p, col, (sx * (LANE_IN + 0.05), cur), (sx * (LANE_IN + 0.05), a), 0.0,
                          rng.choice(("rope", "rope", "pipe", "net")), rng)
                y = cur + rng.uniform(1.0, 2.5)
                while y < a - 1.0:
                    base = Vector((sx * (LANE_IN + 0.05), y, 0.0))
                    sw.pole(p, base, base + UP * LANE_POLE_H, 0.06, sw.TIMBER, seg=7)
                    sw.lashing(p, base + UP * (sw.RAIL_H - 0.1), 0.08, turns=2)
                    col.box((base.x - 0.07, y - 0.07, 0.0), (base.x + 0.07, y + 0.07, LANE_POLE_H))
                    poles.append(base + UP * LANE_POLE_H)
                    y += rng.uniform(*LANE_POLE_GAP)
            cur = max(cur, b)
        t.kit_object(p, "Mesh_SkyCity_LaneRail%s" % ("P" if sx < 0 else "S"), vis)
    return poles


def shaft(mats, vis, col, index, c, rng):
    """A ladder from a crossing up beside the bags' join to the crown gantry."""
    side = 1 if c["ladder_x"] > 0 else -1
    f = Vector((c["facing"][0], c["facing"][1], 0.0))
    back = -f
    ly0, ly1 = sorted((c["ladder_y"], c["ladder_y"] + back.y * SHAFT_LANDING))
    gw = t.GANTRY_WALK
    inner, outer = t.GANTRY_HW, abs(c["ladder_x"]) + sw.LADDER_W / 2 + 0.35
    xa, xb = sorted((side * inner, side * outer))
    p = Part(mats.kit)
    t.floor(p, col, (xa, ly0), (xb, ly1), gw, "plate", rng)
    t.railing(p, col, (side * outer, ly0), (side * outer, ly1), gw, "pipe", rng)
    far = ly0 if back.y < 0 else ly1
    t.railing(p, col, (xa, far), (xb, far), gw, "pipe", rng)
    front = c["ladder_y"]
    lx0, lx1 = sorted((c["ladder_x"] - sw.LADDER_W / 2, c["ladder_x"] + sw.LADDER_W / 2))
    for a, b in ((xa, lx0), (lx1, xb)):
        t.railing(p, col, (a, front), (b, front), gw, "pipe", rng)
    for x in (xa + 0.15, xb - 0.15):
        sw.knee(p, (x, (ly0 + ly1) / 2, gw - 0.1), (side * t.GANTRY_HW, (ly0 + ly1) / 2, gw - 1.2), sw.RUST_DEEP,
                w=0.1)
    t.kit_object(p, "Mesh_SkyCity_ShaftLanding%02d" % index, vis)
    exit_point = Vector((c["ladder_x"], (ly0 + ly1) / 2, gw))
    return ladder(mats, vis, col, "Shaft%02d" % index, (c["ladder_x"], c["ladder_y"], 0.0), f, gw, exit_point, "rust", rng)


def shaft_openings():
    """Gantry railing openings where the shaft landings join it, per side."""
    ops = {1: [], -1: []}
    for c in CROSSINGS:
        side = 1 if c["ladder_x"] > 0 else -1
        a = c["ladder_y"]
        b = a - c["facing"][1] * SHAFT_LANDING
        ops[side].append(tuple(sorted((a, b))))
    return ops


def castle_ladder(mats, vis, col, rng):
    off = t.castle_offset()
    aft = sc.CASTLE_Y1 + off.y
    x = t.CASTLE_T1_LADDER_X
    foot = (x, aft + sw.STANDOFF + 0.03, 0.0)
    return ladder(mats, vis, col, "CastleTerrace", foot, (0.0, 1.0), t.T1, (x, aft - 0.6, t.T1), "timber", rng)


# ===========================================================================
# Rooftops
# ===========================================================================

def rooftops(mats, vis, col, walls, ladders):
    """Terraces on the box shacks' flat roofs, each reached by a ladder up its
    street wall. Worked in the house's own space - the street edge is the roof
    slab's low-x side, over the host wall - and turned into world at the end."""
    terraces = []
    for w in walls:
        if not w["roof"]:
            continue
        o, f = w["house"], w["frame"]
        lo, hi, fittings = w["roof"]
        rng = t.rng_for("roof", o.name)
        z = f.point(0.0, 0.0, hi.z).z
        o[t.WALKABLE_ROOF] = z

        def at(x, y, f=f, hi=hi):
            q = f.point(x, y, hi.z)
            return (q.x, q.y)
        corners = [f.point(x, y, hi.z) for x in (lo.x, hi.x) for y in (lo.y, hi.y)]
        col.hull([c + UP * dz for c in corners for dz in (-t.DECK_T, 0.0)])
        for flo, fhi in fittings:
            col.hull([f.point(x, y, zz) for x in (flo.x, fhi.x) for y in (flo.y, fhi.y) for zz in (flo.z, fhi.z)])
        # `along` is object -Y, so a distance u along the wall is local y = centre - u / k.
        plo, phi = pad_bounds(o)
        ly = (plo.y + phi.y) / 2 - w["ladder_u"] / f.k
        gap = sw.LADDER_W / 2 / f.k
        p = Part(mats.kit)
        t.railing(p, col, at(lo.x, lo.y), at(lo.x, max(lo.y, ly - gap)), z, "rope", rng)
        t.railing(p, col, at(lo.x, min(hi.y, ly + gap)), at(lo.x, hi.y), z, "rope", rng)
        t.railing(p, col, at(hi.x, lo.y), at(hi.x, hi.y), z, rng.choice(("rope", "sheet", "net")), rng)
        t.railing(p, col, at(lo.x, lo.y), at(hi.x, lo.y), z, rng.choice(("rope", "sheet")), rng)
        t.railing(p, col, at(lo.x, hi.y), at(hi.x, hi.y), z, rng.choice(("rope", "net")), rng)
        t.kit_object(p, "Mesh_SkyCity_RoofTerrace_%s" % o.name.replace("Mesh_SkyCity_", ""), vis)
        street = w["base"] + w["along"] * w["ladder_u"]
        foot = street + w["n"] * sw.STANDOFF
        foot.z = 0.0
        exit_point = f.point(lo.x + 0.55, ly, hi.z)
        ladders.append(ladder(mats, vis, col, "Roof_%s" % o.name.replace("Mesh_SkyCity_", ""), foot, w["n"], z,
                              exit_point, "timber", rng))
        centre = sum(corners, Vector()) / 4
        terraces.append(dict(house=o.name, frame=f, lo=lo, hi=hi, ly=ly, z=z, centre=centre,
                             exit=exit_point, corners=corners))
    return terraces


# ===========================================================================
# The street
# ===========================================================================

def street_dressing(mats, vis, col, walls, poles, terraces, ladders):
    """Washing, lights, cables, canopies, signs, stalls, stoves, plants and birds -
    against the walls, in the gaps, strung over the sidewalk and along the lane's
    poles, never in the lane. One object per side of the ship and one for the
    roofs, so each can be culled on its own."""
    keep_clear = [(lad["foot"] + lad["facing"] * CLIMB_CLEAR, 1.1) for lad in ladders]
    obstacles = t.house_footprints() + [t.world_bounds(o) for o in t.city_objects("Mesh_SkyCity_Stow")]

    def free(q, r):
        if abs(q.x) - r < SIDEWALK_MIN:
            return False
        if any((Vector((q.x, q.y)) - Vector((c.x, c.y))).length < r + rad for c, rad in keep_clear):
            return False
        return not any(lo.x - r < q.x < hi.x + r and lo.y - r < q.y < hi.y + r and lo.z < 1.0
                       for lo, hi in obstacles)

    def claim(q, r, h):
        col.box((q.x - r, q.y - r, 0.0), (q.x + r, q.y + r, h))
        keep_clear.append((q, r))

    by_side = {1: [], -1: []}
    for w in walls:
        by_side[1 if w["base"].x > 0 else -1].append(w)
    for sx, ws in by_side.items():
        rng = t.rng_for("street", sx)
        p = Part(mats.street)
        ws.sort(key=lambda w: w["base"].y)
        for i, w in enumerate(ws):
            dress_wall(p, w, rng, free, claim)
            near = [q for q in sorted(poles, key=lambda q: (q - w["base"]).length)
                    if math.copysign(1, q.x) == sx][:2]
            along, base, width, height = w["along"], w["base"], w["width"], w["height"]
            if near:
                sl.string_lights(p, base + along * rng.uniform(-width / 3, width / 3) + UP * min(height, 3.2),
                                 near[0], rng)
            if len(near) > 1:
                sl.cables(p, base + along * rng.uniform(-width / 3, width / 3) + UP * min(height - 0.1, 3.4),
                          near[1], rng)
            if i + 1 < len(ws):
                dress_gap(p, w, ws[i + 1], rng, free, claim)
        # Along the lane's poles: lights on every span, washing on some.
        side_poles = sorted((q for q in poles if math.copysign(1, q.x) == sx), key=lambda q: q.y)
        for a, b in zip(side_poles, side_poles[1:]):
            if (b - a).length > LANE_POLE_GAP[1] + 3.0:
                continue
            if rng.random() < 0.45:
                sl.laundry_line(p, a - UP * 0.1, b - UP * 0.1, rng, sag=0.15)
            else:
                sl.string_lights(p, a, b, rng)
        t.kit_object(p, "Mesh_SkyCity_StreetLife%s" % ("P" if sx < 0 else "S"), vis)

    p = Part(mats.street)
    for tr in terraces:
        dress_roof(p, col, tr)
    t.kit_object(p, "Mesh_SkyCity_StreetLifeRoofs", vis)


def dress_wall(p, w, rng, free, claim):
    """One street wall: its foot, its face, its top."""
    n, along, base, width, height = w["n"], w["along"], w["base"], w["width"], w["height"]
    u = -width / 2 + 0.35
    while u < width / 2 - 0.35:
        q = base + along * u + n * 0.3
        kind = rng.choices(("plant", "sit", "stove", "planter", "none"), (0.4, 0.2, 0.1, 0.2, 0.1))[0]
        if kind in ("plant", "planter") and free(q, 0.3):
            sl.plant(p, q, rng, "planter" if kind == "planter" else rng.choice(("tin", "tall")))
            claim(q, 0.3, 1.0)
        elif kind == "sit" and free(q + n * 0.3, 0.45):
            sl.crate_table(p, q + n * 0.3, rng)
            sl.stool(p, q + n * 0.3 + along * 0.6, rng)
            claim(q + n * 0.3, 0.45, 0.8)
        elif kind == "stove" and free(q + n * 0.1, 0.4):
            sl.stove(p, q + n * 0.1, rng)
            claim(q + n * 0.1, 0.4, 1.0)
        u += rng.uniform(0.7, 1.2)
    # Nothing hangs in front of a ladder up this wall.
    def clear_of_ladder(u0, u1):
        lu = w["ladder_u"]
        return lu is None or u1 < lu - LADDER_KEEP_OUT or u0 > lu + LADDER_KEEP_OUT
    # A canopy or two over the sidewalk, high enough to walk under.
    for _ in range(rng.randint(1, 2)):
        cw = rng.uniform(1.2, 2.0)
        cu = rng.uniform(-width / 2 + cw / 2, width / 2 - cw / 2)
        if clear_of_ladder(cu - cw / 2, cu + cw / 2):
            sl.canopy(p, base + along * cu + UP * min(height - 0.3, rng.uniform(2.6, 2.9)), n, cw,
                      rng.uniform(0.7, 1.0), rng)
    # Washing along the wall, over the sidewalk.
    if width > 2.2 and rng.random() < 0.8 and clear_of_ladder(-width / 2, width / 2):
        z = min(height - 0.1, rng.uniform(3.0, 3.3))
        sl.laundry_line(p, base + along * (-width / 2 + 0.3) + n * 1.05 + UP * z,
                        base + along * (width / 2 - 0.3) + n * 1.05 + UP * z, rng)
    blade_u = rng.choice((-1, 1)) * (width / 2 - 0.2)
    if rng.random() < 0.5 and clear_of_ladder(blade_u - 0.4, blade_u + 0.4):
        blade = base + along * blade_u + n * 0.55 + UP * rng.uniform(2.3, 2.7)
        sl.sign(p, blade, along * rng.choice((-1, 1)), rng, width=0.7)
    top = base + UP * (height - 0.25)
    for _ in range(rng.randint(0, 2)):
        sl.plant(p, top + n * 0.35 + along * rng.uniform(-width / 2.5, width / 2.5), rng, "hanging")
    if rng.random() < 0.35:
        sl.birdcage(p, top + n * 0.45 + along * rng.uniform(-width / 3, width / 3), rng)
    if rng.random() < 0.5:
        sl.plant(p, base + UP * height + along * rng.uniform(-width / 3, width / 3) - n * 0.12, rng, "tin")


def dress_gap(p, w, nxt, rng, free, claim):
    """The gap between two neighbouring walls: washing and cables across it, and
    a stall in it if it is wide enough."""
    n, along, base, width, height = w["n"], w["along"], w["base"], w["width"], w["height"]
    a = max((base + along * (s * width / 2) for s in (-1, 1)), key=lambda q: q.y) + n * 0.3
    b = min((nxt["base"] + nxt["along"] * (s * nxt["width"] / 2) for s in (-1, 1)), key=lambda q: q.y) \
        + nxt["n"] * 0.3
    gap = (b - a).length
    if not 1.0 < gap < 9.0:
        return
    zt = min(height, nxt["height"])
    sl.laundry_line(p, a + UP * min(zt - 0.1, 3.0), b + UP * min(zt - 0.1, 3.0), rng)
    sl.cables(p, a + UP * (zt + 0.1), b + UP * (zt + 0.1), rng, count=rng.randint(1, 3))
    stall_at = (a + b) / 2 + n * 0.2
    stall_at.z = 0.0
    if gap > 3.2 and rng.random() < 0.75 and free(stall_at, 0.9):
        sl.stall(p, stall_at + n * 0.35, n, rng)
        claim(stall_at, 0.9, 2.6)


def dress_roof(p, col, tr):
    """A roof terrace: a washing line on two poles, plants, a stool, a dish."""
    rng = t.rng_for("roof life", tr["house"])
    f, lo, hi = tr["frame"], tr["lo"], tr["hi"]
    mid_x = (lo.x + hi.x) / 2
    far_x = hi.x - 0.45
    pa, pb = f.point(far_x, lo.y + 0.45, hi.z), f.point(far_x, hi.y - 0.45, hi.z)
    for q in (pa, pb):
        sl.pole(p, q, q + UP * 2.1, 0.035, sl.TIMBER)
    sl.laundry_line(p, pa + UP * 2.0, pb + UP * 2.0, rng)
    for (x, y), kind, r in (((mid_x, lo.y + 0.5), "tall", 0.35), ((mid_x + 0.4, hi.y - 0.5), "planter", 0.5)):
        q = f.point(x, y, hi.z)
        sl.plant(p, q, rng, kind)
        col.box((q.x - r, q.y - r, tr["z"]), (q.x + r, q.y + r, tr["z"] + 1.0))
    sl.stool(p, f.point(mid_x, (lo.y + hi.y) / 2 + 0.6, hi.z), rng)
    sl.dish(p, f.point(far_x - 0.2, (lo.y + hi.y) / 2, hi.z), rng, height=0.9)


# ===========================================================================
# Orchestration, called by sky_city_traversal.run
# ===========================================================================

class Materials:
    """The two material lists the street's Parts are built with."""

    def __init__(self):
        self.kit = bl.link_materials(sw.MATS)
        self.street = bl.link_materials(sl.MATS)


def build(vis, col, report):
    """Everything above, in order. Returns (changed object names, ladders, walk routes)."""
    mats = Materials()
    changed = repair_houses(report)
    bpy.context.view_layer.update()
    raised = stilts(mats, vis, col)
    walls = host_walls(mats, vis, col)
    ladders = []
    poles = crossings(mats, vis, col, ladders)
    ladders.append(castle_ladder(mats, vis, col, t.rng_for("castle ladder")))
    terraces = rooftops(mats, vis, col, walls, ladders)
    street_dressing(mats, vis, col, walls, poles, terraces, ladders)
    coll = t.ensure_collection(LADDER_COLL, hide_render=True)
    for i, lad in enumerate(ladders, start=1):
        ladder_marker(coll, i, lad)
    report["Street: stilted / walls / ladders / roof terraces"] =         "%d / %d / %d / %d" % (raised, len(walls), len(ladders), len(terraces))
    return changed, ladders, street_routes(terraces, ladders)


def street_routes(terraces, ladders):
    gw = t.GANTRY_WALK
    r = {}
    for i, c in enumerate(CROSSINGS, start=1):
        r["lane to lane over the keel, join %d" % i] = [(-5.7, c["walk_y"], 0.0), (5.7, c["walk_y"], 0.0)]
    aft = sc.CASTLE_Y1 + t.castle_offset().y
    r["castle aft porch"] = [(-5.7, aft + 1.0, 0.0), (5.7, aft + 1.0, 0.0)]
    for lad in ladders:
        e = lad["exit"]
        if lad["name"].startswith("Shaft"):
            r["off the %s ladder onto the gantry" % lad["name"]] = [(e.x, e.y, gw), (0.0, e.y, gw)]
        elif lad["name"] == "CastleTerrace":
            r["off the castle ladder onto the terrace"] = [(e.x, e.y, e.z), (e.x + 2.0, e.y, e.z)]
    for tr in terraces:
        c = tr["centre"]
        r["roof terrace %s" % tr["house"]] = [(tr["exit"].x, tr["exit"].y, tr["z"]), (c.x, c.y, tr["z"])]
    return r


def ladder_checks(ladders, solids, bvh):
    """For each ladder: the climber's column clear all the way up, and floor at the exit."""
    report = {}
    for lad in ladders:
        fails = []
        axis = lad["foot"] + lad["facing"] * CLIMB_CLEAR
        z = lad["foot"].z + 0.3
        while z < lad["top_z"] - 0.1:
            q = Vector((axis.x, axis.y, z))
            if solids.contains(q + UP * 1.0):
                fails.append(("column blocked", q, None))
            z += 0.4
        e = lad["exit"]
        hit, _, _, _ = bvh.ray_cast(e + UP * 1.0, UNDER, 1.5)
        if hit is None or abs(hit.z - e.z) > 0.3:
            fails.append(("no floor at exit", e, None))
        if solids.contains(e + UP * 1.0):
            fails.append(("exit in solid", e, None))
        report["ladder " + lad["name"]] = fails
    return report
