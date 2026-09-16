"""models/vehicles/sky_city_traversal - make the hand-edited sky city walkable.

`sky_city.blend` carries the user's own edits since 2026-09-16, so this is NOT a
generator. It runs INSIDE the open Blender session (Blender MCP) against that
file, and it never saves - the user inspects the result and saves it themselves:

    import sys; sys.path.insert(0, r"<_Source~>/models/vehicles")
    import sky_city_traversal as t; t.run()

## What the player can actually do

Read off `Characters/Player/Movement/Movement.cs`, and every number below is
designed against it, not against intuition:

- The player is a Rigidbody with a 0.5 m x 2.0 m CapsuleCollider. Movement
  writes horizontal velocity only.
- There is **no step-up logic and no ladder mechanic.** A staircase built from
  real risers stops the capsule at the first one, and a ladder is scenery. Every
  vertical route here is therefore a stair whose COLLISION is a smooth ramp at
  27.2 degrees (`STAIR_K`); the treads are drawn on top of it for the eye only.
- A jump reaches ~2.5 m; fall damage starts landing faster than 5 m/s, a ~1.3 m
  drop. So every edge a player can walk to and fall more than that from is railed.

## The look - second pass

The first pass was rejected as "way too industrious and modern": one steel slab
down each side, square steel stairs, a steel stair tower, square loops round the
gantry's towers. Everything drawn here now comes from `scrap_walkway` (patched
decks, leaning posts, sagging rope, rust with a ramp), `cargo_crane` and
`trade_goods` - library components, not bespoke steel:

- **Walkways are stitched platforms.** Each side is a run of separate decks -
  planks, rusted plate, grating, scrap - each its own width, railing and props,
  joined by planks thrown over the gaps.
- **You climb the castle from inside.** A switchback stair core rises through
  the first floor, the second floor and out onto the roof, where a timber stair
  climbs to the crown gantry. The stair tower is gone.
- **The gantry's towers are the destinations.** The lantern and the dome each
  stand in a round gallery; the galleries are widened to walk (they were 0.9 m,
  narrower than the capsule) and their railings carved open where the gantry
  runs in. The dish gets a gallery of its own. The square loops are gone.
- **It is a traders' town.** Cranes stand at loading bays on the walkways with
  goods on the hook; sacks, bales, rugs and chests wait beside them.

## Collision is authored here, not guessed in Unity

`SkyCityBuilder` used to fit boxes from renderer bounds. On this file that fails
three ways, all silently: the houses share a mesh with struts running 5 m under
the deck, so a bounds box drops a 12 m wall across the promenade; the kit
catwalks carry 1.1 m rails, and `StaticPropBuilder.AddBox(surfaceOnly)` puts the
slab at `bounds.max.y` - the rail top - so the player walks on air above every
bridge; and a leaning, sagging railing has no useful bounds at all. So every
surface a player stands on or bumps into is a convex `COL_*` island in
`Coll_SkyCity_Collision` - 8-vertex boxes, ramps, walls, gallery sectors and
n-gon prisms - and `verify()` walks the routes against exactly that set.

## What it changes that was already there

Only what the walkways cannot exist without, each backed up as a fake-user mesh
named `<object>__pre_traversal` and rebuilt from that backup on every run:

- `BowCastle` - rebuilt hollow on all three storeys, same outside.
- `Cage` - the ring at y = -50 and its bay braces removed, and the stringers cut
  back, because the castle was moved over them and they ran through its rooms.
- `Decks` - the eight skywalk rakes removed; they crossed the walkway at knee
  height. Posts on the walkway carry the skywalks instead.
- `Climbs` - ladders that stood inside the new routes, or led nowhere after the
  stern tower was raised, removed.
- `Gantry` (hand-edited: a second railing) - railings opened where the galleries,
  balconies and ramp join; the port half of one cross-frame cut where the roof
  stair climbs through it; repainted with rust by island.
- The gantry's lantern and dome - galleries widened and their railings opened
  (the user asked for exactly this).
- `Gangway_Catwalk_Bridge` - emptied. The user's move of the castle left it
  lying across the second floor at floor height, wholly inside the castle's
  footprint and joining nothing; with the storey hollow, its railings crossed
  the room and the stairwell.
- `Rail01..03`, `RailP01..03`, `Boarding01..02` deleted: the rails fenced the
  walkway off from the stern piers, and the boarding ladders stood inside the
  castle and under a gas bag.

## Re-running

Safe while nothing it made or modified has been hand-edited since: every such
object stores an MD5 of its vertices and transform, and `run()` refuses to touch
anything whose hash no longer matches.
"""

import array
import hashlib
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
for _p in (HERE, LIB, os.path.join(LIB, "components", "structural"),
           os.path.join(LIB, "components", "props"), os.path.join(LIB, "components", "mechanical")):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import _buildlib as bl  # noqa: E402
import cargo_crane as cc  # noqa: E402
import scrap_walkway as sw  # noqa: E402
import sky_city as sc  # noqa: E402
from _buildlib import Part  # noqa: E402

# ---------------------------------------------------------------------------
# Player metrics - see the module docstring for where these come from.
# ---------------------------------------------------------------------------
PLAYER_R = 0.5
HEADROOM = 2.1              # capsule 2.0 plus the ground probe's reach
STAIR_K = math.tan(math.radians(27.2))   # rise per metre of run, every stair
RAIL_H = sw.RAIL_H          # 1.10, the library's rail height
SAFE_DROP = 1.3             # guard an edge only where the fall is deeper
STAIRWELL_MARGIN = 0.3      # extra opening over a flight, so heads clear the slab edge
DECK_T = 0.25               # collision slab thickness under every walking surface
UP = Vector((0.0, 0.0, 1.0))
SEED = 20260916

# ---------------------------------------------------------------------------
# Levels. Terraces are the storey LIP tops, not the slab tops: every castle
# storey carries a 0.22 m lip, and the player stands on that.
# ---------------------------------------------------------------------------
LIP = 0.22
OVERLAY = 0.06              # slabs sit this far under a deck overlay's walking face
T1 = sc.CASTLE_STEPS[0][1] + LIP     # 5.42  ground-floor roof
T2 = sc.CASTLE_STEPS[1][1] + LIP     # 10.82 first-floor roof
T3 = sc.CASTLE_STEPS[2][1] + LIP     # 16.72 castle roof
GANTRY_WALK = 19.68         # top face of the crown gantry deck, as measured
GANTRY_HW = sc.GANTRY_HW    # 1.30
GANTRY_RAIL_X = 1.18        # both railings (the port one is the user's)
GANTRY_Y0, GANTRY_Y1 = -52.5, 50.0

# ---------------------------------------------------------------------------
# Castle interior, CASTLE-LOCAL (the object carries the user's offset).
# ---------------------------------------------------------------------------
WALL = 0.3
GF_DOOR_H, UPPER_DOOR_H = 3.2, 2.4
GF_CEIL = sc.CASTLE_STEPS[0][1] - 0.22      # 4.98 underside of the first floor
F1_CEIL = sc.CASTLE_STEPS[1][1] - 0.20      # 10.40
F2_CEIL = sc.CASTLE_STEPS[2][1] - 0.20      # 16.30
# Ground floor to first floor: up the starboard side from the aft wall, rising
# forward. Forced by the user's raised prow hull (2.8 m tall mid-floor) and the
# pod bolted to the castle's front, which fills the upper floors' forward centre.
INNER_STAIR_X = (5.1, 6.9)
INNER_STAIR_FOOT_Y = -44.8
AFT_DOORS = ((-6.9, -4.5), (-1.5, 1.5), (5.0, 8.2))   # port lane, centre, stair + corridor
GF_FLANK_DOOR = (-57.0, -55.2)
F1_PORT_DOOR = (-56.0, -54.2)
F1_STBD_DOOR = (-56.9, -55.4)
F1_WINDOW = ((-50.5, -48.3), (6.2, 7.7))    # (y span, z span)
F2_DOOR = (-52.9, -51.5)                    # both flanks, onto the first-floor roof
# First floor up to the roof: two switchbacks stacked in the aft third, the
# only part of the upper floors the pod leaves clear. Each runs ACROSS the
# castle: a lower flight rising to port from FOOT_X, a landing, and an upper
# flight back to starboard beside it. The upper flight of one storey arrives
# exactly where the lower flight of the next one starts, so the climb is one
# continuous zig-zag.
CORE_LOWER_Y = (-49.65, -48.3)
CORE_UPPER_Y = (-51.0, -49.65)
CORE_LANDING = 1.35
F1_CORE_FOOT_X = 3.9
F2_CORE_FOOT_X = 3.81
# The pod on the castle front, as vertical bands (z0, z1): each band collides as
# a prism of that band's measured radius.
POD_BANDS = {"Mesh_SkyCity_Dome_SensorCupola_Dome.001": ((5.2, 8.5), (8.5, 12.1)),
             "Mesh_SkyCity_Beacon_SensorCupola_Lantern.003": ((10.1, 14.5), (14.5, 17.4))}
# Castle roof to the crown gantry: a timber flight up the port side, rising
# forward to a landing that wraps round the gantry's forward end.
ROOF_STAIR_X = (-3.5, -2.2)
# The ladder up the castle's aft wall onto the first terrace, from the port
# patch strip beside the lane - clear of the aft door at x -6.9..-4.5.
CASTLE_T1_LADDER_X = -8.0
ROOF_STAIR_FOOT_Y = -48.5
ROOF_LANDING_DEPTH = 1.6       # deep enough to turn in, clear of the gantry railings' ends
# The gantry cross-frame the roof stair climbs through, as measured; its port
# half is cut away.
GANTRY_FRAME_Y = -48.04

# ---------------------------------------------------------------------------
# Walkways outboard of the houses, world.
# ---------------------------------------------------------------------------
EXT_IN, EXT_Y1 = sc.SIDE_OUT, 43.0          # stops short of the user's stern piers at 43.4
# Small platforms, as the user cut them down by hand on 2026-09-16: most reach
# only 2-3 m past the old deck edge, and each is only as wide as what stands on
# or beside it needs.
EXT_OUT_RANGE = (13.0, 14.0)
PLATFORM_RUN = (2.6, 4.8)
PLATFORM_GAP = (0.12, 0.3)
HOUSE_CLEARANCE = 1.25      # outer edge past the furthest-out house: a capsule and a rail
SKYWALK_OUTER = 15.3        # under a skywalk and its stair
JETTY_HALF = 3.0            # a crane's jetty, either side of the crane
JETTY_OUTER = {"TimberJib": 15.9, "ScrapDerrick": 16.1, "Sheerlegs": 15.6, "Davit": 15.3}
CRANE_INSET = 0.2           # crane footprint to jetty edge
GOODS_INSET = 0.9           # a pile's centre to the jetty edge
WALK_MARGIN = 0.62          # a walking line's distance inside a railing
# The user's hanging bow sails cross the walkway at x 13.3..17, y -40.7..-38.3.
SAIL_SLOT_Y = (-41.0, -38.1)
SAIL_SLOT_IN = 13.1
# Loading bays, each on a jetty in a gap between houses: (variant, side, y).
CRANES = (("TimberJib", 1, -13.0), ("ScrapDerrick", 1, 40.8), ("Sheerlegs", -1, 13.9), ("Davit", -1, 35.0))
# Trade goods on the jetties: (trade_goods collection, side, y, yaw degrees).
GOODS = (("SackPile", 1, -15.4, 0), ("NetBundle", 1, -10.6, 0), ("BaleStack", 1, 38.2, 90),
         ("ChestStack", -1, 11.3, 90), ("CanisterRack", -1, 16.5, 90), ("RugRolls", -1, 32.9, 90),
         ("TradeScale", -1, 37.4, 90))
# Stock inside the castle, castle-local: (collection, x, y, yaw degrees, floor z).
CASTLE_GOODS = (("SackPile", -2.9, -46.4, 0, 0.0), ("ChestStack", 2.7, -46.3, 0, 0.0),
                ("CanisterRack", -5.9, -47.2, 0, T1), ("RugRolls", -4.0, -53.9, 0, T2))
CRANE_GATE = 0.4            # rail opening either side of a boom that heels below rail height

# ---------------------------------------------------------------------------
# Crown gantry, world.
# ---------------------------------------------------------------------------
# Round galleries. `old_rail_r` is where the user's railing stood; `rail_r` is
# where it is moved to, which is what makes the gallery walkable; `split_r`
# separates the tower's own body from the gallery ring when widening.
TOWERS = {
    "Mesh_SkyCity_Beacon_SensorCupola_Lantern": dict(body_r=2.62, old_rail_r=3.46, rail_r=4.15,
                                                     split_r=3.0, floor_z=19.58, band_top=21.3),
    "Mesh_SkyCity_Dome_SensorCupola_Dome": dict(body_r=3.58, old_rail_r=4.56, rail_r=5.4,
                                                split_r=4.0, floor_z=19.68, band_top=21.35),
}
DISH = "Mesh_SkyCity_Dish_SensorCupola_Dish"
DISH_GALLERY = dict(body_r=2.82, rail_r=4.35)
GALLERY_OPENING = GANTRY_HW + 0.1           # |x| inside which a gallery railing is carved away
BALCONIES = ((1, -14.0, -11.0, 4.6), (-1, 8.0, 11.5, 5.0))   # (side, y0, y1, reach)
RAMP_Y0 = 46.6

PASS = "sky_city_traversal"
# Merged structure `SkyCityBuilder` gives a plain (non-convex) MeshCollider: solid
# lattice a player can walk into from the lanes and piers. Mirrored here so the
# route check tests the same set Unity will collide with. Keep the two in step.
# Not the outriggers: their rigging runs through the castle's rooms.
MESH_COLLIDED = ("Mesh_SkyCity_Cage", "Mesh_SkyCity_Cradles", "Mesh_SkyCity_Decks", "Mesh_SkyCity_SternGear")
# Kit parts it boxes by renderer bounds: (name prefix, minimum height), likewise mirrored.
BOX_COLLIDED = (("Mesh_SkyCity_Stow", 1.2), ("Mesh_SkyCity_Rail", 0.0))
WALKABLE_ROOF = "sky_city_walkable_roof"    # on a house: the world z of a roof terrace
VIS_COLL, COL_COLL = "Coll_SkyCity_Traversal", "Coll_SkyCity_Collision"
SRC_COLL = "Coll_SkyCity_TraversalSources"
GANGWAY = "Mesh_SkyCity_Gangway_Catwalk_Bridge"
MODIFIED = ("Mesh_SkyCity_BowCastle", "Mesh_SkyCity_Cage", "Mesh_SkyCity_Decks",
            "Mesh_SkyCity_Climbs", "Mesh_SkyCity_Gantry", GANGWAY) + tuple(TOWERS)
RETIRED = tuple(["Mesh_SkyCity_Rail%02d_Handrail_Straight" % i for i in (1, 2, 3)]
                + ["Mesh_SkyCity_RailP%02d_Handrail_Straight" % i for i in (1, 2, 3)]
                + ["Mesh_SkyCity_Boarding01_Handrail_Ladder",
                   "Mesh_SkyCity_Boarding02_Handrail_Ladder"])

# Deck clutter the user's castle move left inside the castle's new walls.
# Castle-local (x, y); set absolutely, so a re-run leaves them where they are.
PROP_MOVES = {
    "Mesh_SkyCity_Stow06_Crate_Stack": (-7.6, -47.8),   # into the west room's aft corner
    "Mesh_SkyCity_Stow02_Barrel_Drum": (-8.2, -43.5),   # just outside the aft wall
}

M = sc   # the castle's material indices live on the generator module


# ===========================================================================
# Ownership and re-run safety
# ===========================================================================

def geom_hash(o):
    """MD5 of an object's vertex coordinates and world matrix.

    Raw float32 bytes rather than rounded values, and hashlib rather than hash():
    Python's hash() is salted per process, so a hash stored in one Blender
    session would never match the next one.
    """
    me = o.data
    a = array.array('f', [0.0]) * (len(me.vertices) * 3)
    me.vertices.foreach_get("co", a)
    h = hashlib.md5(a.tobytes())
    h.update(array.array('f', [v for row in o.matrix_world for v in row]).tobytes())
    return h.hexdigest()


def refuse_if_hand_edited(discard_own=False):
    bad = [o.name for o in bpy.data.objects
           if o.get("sky_city_hash") and o.type == 'MESH'
           and geom_hash(o) != o["sky_city_hash"]
           and not (discard_own and o.get("sky_city_pass") == PASS)]
    if bad:
        raise RuntimeError(
            "Hand-edited since the last traversal pass - refusing to rebuild, "
            "because that would destroy the edits: %s" % ", ".join(bad))


def ensure_collection(name, hide_render=False, exclude=False):
    coll = bpy.data.collections.get(name)
    if coll is None:
        coll = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(coll)
    coll.hide_render = hide_render
    layer = bpy.context.view_layer.layer_collection.children.get(name)
    if layer is not None:
        layer.exclude = exclude
    return coll


def clear_owned():
    doomed = [o for o in bpy.data.objects if o.get("sky_city_pass") == PASS]
    meshes = {o.data for o in doomed if o.data is not None}
    for o in doomed:
        bpy.data.objects.remove(o, do_unlink=True)
    for me in meshes:
        if me.users == 0:
            bpy.data.meshes.remove(me)


def own(o):
    o["sky_city_pass"] = PASS
    return o


def pristine(obj):
    """Give `obj` a fresh copy of its pre-traversal mesh and return it.

    The first run records the backup; every run starts from it, so surgery is
    applied once to known geometry instead of compounding on its own output.
    """
    key = obj.name + "__pre_traversal"
    backup = bpy.data.meshes.get(key)
    if backup is None:
        backup = obj.data.copy()
        backup.name = key
        backup.use_fake_user = True
    fresh = backup.copy()
    old = obj.data
    obj.data = fresh
    if old is not backup and old.users == 0:
        bpy.data.meshes.remove(old)
    fresh.name = obj.name
    fresh.use_fake_user = False
    return fresh


def rng_for(*key):
    return random.Random(hash_seed(key))


def hash_seed(key):
    return int(hashlib.md5(repr((SEED,) + tuple(key)).encode()).hexdigest()[:8], 16)


# ===========================================================================
# Mesh surgery - island-level, with expected counts asserted
# ===========================================================================

def islands(bm):
    bm.verts.ensure_lookup_table()
    seen, out = set(), []
    for v in bm.verts:
        if v in seen:
            continue
        stack, group = [v], []
        seen.add(v)
        while stack:
            a = stack.pop()
            group.append(a)
            for e in a.link_edges:
                b = e.other_vert(a)
                if b not in seen:
                    seen.add(b)
                    stack.append(b)
        lo = Vector((min(p.co.x for p in group), min(p.co.y for p in group),
                     min(p.co.z for p in group)))
        hi = Vector((max(p.co.x for p in group), max(p.co.y for p in group),
                     max(p.co.z for p in group)))
        out.append((group, lo, hi))
    return out


def delete_islands(bm, pred, expected, what):
    hit = [g for g, lo, hi in islands(bm) if pred(lo, hi)]
    if expected is not None and len(hit) != expected:
        raise RuntimeError("%s: expected %d islands, found %d - the mesh is not "
                           "the one this pass was written against"
                           % (what, expected, len(hit)))
    bmesh.ops.delete(bm, geom=[v for g in hit for v in g], context='VERTS')
    return len(hit)


def cut_plane(bm, group, co, no, keep_positive):
    """Bisect one island at a plane and drop the side not kept, capped."""
    geom = list({e for v in group for e in v.link_edges}) + \
        list({f for v in group for f in v.link_faces}) + list(group)
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no,
                                 clear_inner=keep_positive, clear_outer=not keep_positive)
    cut_edges = [e for e in res["geom_cut"] if isinstance(e, bmesh.types.BMEdge)]
    if cut_edges:
        bmesh.ops.holes_fill(bm, edges=cut_edges, sides=0)


def open_span(bm, group, y0, y1):
    """Remove the stretch y0..y1 from one long island (a rail) and cap both ends."""
    for y in (y0, y1):
        live = [v for v in group if v.is_valid]
        geom = list({e for v in live for e in v.link_edges}) + \
            list({f for v in live for f in v.link_faces}) + live
        res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0.0, y, 0.0),
                                     plane_no=(0.0, 1.0, 0.0))
        group = live + [v for v in res["geom_cut"] if isinstance(v, bmesh.types.BMVert)]
    inside = [f for f in bm.faces
              if y0 + 1e-4 < f.calc_center_median().y < y1 - 1e-4
              and any(v in group for v in f.verts)]
    bmesh.ops.delete(bm, geom=inside, context='FACES_ONLY')
    loose = [v for v in bm.verts if v.is_valid and not v.link_faces
             and y0 - 1e-4 <= v.co.y <= y1 + 1e-4]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    edges = [e for e in bm.edges if e.is_boundary
             and all(abs(v.co.y - y0) < 1e-3 or abs(v.co.y - y1) < 1e-3 for v in e.verts)]
    if edges:
        bmesh.ops.holes_fill(bm, edges=edges, sides=0)


def with_bmesh(obj, fn):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    try:
        result = fn(bm)
        bm.to_mesh(obj.data)
    finally:
        bm.free()
    obj.data.update()
    return result


def gallery_span(yc, r):
    """The y range a round gallery of radius r takes out of a gantry railing."""
    h = math.sqrt(max(0.0, r * r - GANTRY_RAIL_X * GANTRY_RAIL_X)) - 0.05
    return (yc - h, yc + h)


def gantry_openings():
    """Railing openings on the crown gantry, per side (+1 starboard, -1 port)."""
    O = bpy.data.objects
    ops = {1: [], -1: []}
    rings = [(O[n].location.y, s["rail_r"]) for n, s in TOWERS.items()]
    rings.append((O[DISH].location.y, DISH_GALLERY["rail_r"]))
    for sx in (1, -1):
        ops[sx] += [gallery_span(yc, r) for yc, r in rings]
        ops[sx] += [(y0, y1) for s, y0, y1, _ in BALCONIES if s == sx]
        ops[sx] += [(RAMP_Y0, GANTRY_Y1)]
    import sky_city_street as street     # imports this module; deferred to break the cycle
    for sx, spans in street.shaft_openings().items():
        ops[sx] += spans
    return ops


def surgery(report):
    O = bpy.data.objects
    castle_aft = sc.CASTLE_Y1 + castle_offset().y

    pristine(O["Mesh_SkyCity_Decks"])
    castle = castle_offset()
    g_y0, g_y1 = sc.CASTLE_STEPS[0][3] + castle.y, sc.CASTLE_STEPS[0][4] + castle.y

    def decks(bm):
        n = delete_islands(bm, lambda lo, hi: (hi.x - lo.x) > 8 and (hi.z - lo.z) > 5, 8, "skywalk rakes")
        # The lane lamps' posts and heads that the user's castle move left
        # standing inside the ground-floor room.
        n += delete_islands(bm, lambda lo, hi: (hi.x - lo.x) < 0.5 and (hi.y - lo.y) < 0.5 and lo.z > -0.1
                            and g_y0 < (lo.y + hi.y) / 2 < g_y1 and abs(lo.x + hi.x) / 2 < sc.CASTLE_HW,
                            None, "lamps inside the castle")
        return n
    report["Decks: skywalk rakes and castle-room lamps removed"] = with_bmesh(O["Mesh_SkyCity_Decks"], decks)

    def cage(bm):
        n = delete_islands(bm, lambda lo, hi: lo.y > -50.4 and hi.y < -49.6, 24, "ring at y=-50")
        n += delete_islands(bm, lambda lo, hi: lo.y < -49.5 and -40.6 < hi.y < -39.4
                            and (hi.y - lo.y) > 9, 3, "bay 0 braces")
        stringers = [(g, lo, hi) for g, lo, hi in islands(bm) if (hi.y - lo.y) > 90]
        if len(stringers) != 6:
            raise RuntimeError("cage: expected 6 stringers, found %d" % len(stringers))
        for g, lo, hi in stringers:
            z = (lo.z + hi.z) / 2
            if z < 6.0:        # lower pair: stop at the castle's aft wall
                cut = castle_aft
            elif z < 14.0:     # beam pair: stop at the next ring frame aft
                cut = -40.0
            else:              # upper pair: clear the castle roof walk
                cut = -45.4
            cut_plane(bm, g, (0.0, cut, 0.0), (0.0, 1.0, 0.0), keep_positive=True)
        return n
    pristine(O["Mesh_SkyCity_Cage"])
    report["Cage: ring/braces removed, 6 stringers cut"] = with_bmesh(O["Mesh_SkyCity_Cage"], cage)

    def climbs(bm):
        def near(x, y, z0, z1):
            return lambda lo, hi: (abs((lo.x + hi.x) / 2 - x) < 0.9 and abs((lo.y + hi.y) / 2 - y) < 0.9
                                   and lo.z >= z0 - 0.2 and hi.z <= z1 + 0.2)
        n = delete_islands(bm, near(1.3, sc.LADDER_Y[0], sc.LADDER_Z[0], sc.GANTRY_Z), 14,
                           "castle-roof ladder")
        n += delete_islands(bm, near(1.3, sc.LADDER_Y[1], sc.LADDER_Z[1], sc.GANTRY_Z), 30,
                            "stale stern ladder")
        for (y, sx, z), count in zip(sc.SKYWALKS, (13, 17, 15, 19)):
            n += delete_islands(bm, near(sx * 12.6, y, sc.DECK, sc.DECK + z + RAIL_H), count,
                                "skywalk ladder")
        return n
    pristine(O["Mesh_SkyCity_Climbs"])
    report["Climbs: ladder islands removed"] = with_bmesh(O["Mesh_SkyCity_Climbs"], climbs)

    pristine(O["Mesh_SkyCity_Gantry"])
    report["Gantry: rail spans, posts and bunting opened"] = with_bmesh(O["Mesh_SkyCity_Gantry"], gantry_cuts)
    report["Gantry: islands repainted with rust"] = rust_gantry(O["Mesh_SkyCity_Gantry"])

    for name, spec in TOWERS.items():
        pristine(O[name])
        report["%s: gallery widened and opened" % name.split("_")[2]] = widen_gallery(O[name], spec)

    gangway = O.get(GANGWAY)
    if gangway is not None:
        pristine(gangway)
        report["Gangway: islands inside the castle removed"] = with_bmesh(
            gangway, lambda bm: delete_islands(bm, lambda lo, hi: True, None, "gangway"))

    castle = castle_offset()
    moved = []
    for name, (x, y) in PROP_MOVES.items():
        prop = O.get(name)
        if prop is not None:
            prop.location = (x, y + castle.y, prop.location.z)
            moved.append(name.replace("Mesh_SkyCity_", ""))
    report["props moved out of the castle walls"] = ", ".join(moved)

    gone = []
    for name in RETIRED:
        o = O.get(name)
        if o is not None:
            me = o.data
            bpy.data.objects.remove(o, do_unlink=True)
            if me.users == 0:
                bpy.data.meshes.remove(me)
            gone.append(name)
    report["retired objects deleted"] = len(gone)


def gantry_cuts(bm):
    """Railing openings, the posts and bunting inside them, and the cross-frame
    the roof stair climbs through. The Gantry object sits at the origin, so its
    mesh coordinates are world coordinates."""
    cut = 0
    for sx, spans in gantry_openings().items():
        def on_side(lo, hi, sx=sx):
            return math.copysign(1, (lo.x + hi.x) / 2) == sx
        # Rail heights are read once, before any cut: cutting invalidates the
        # vertices of the island it cuts.
        rail_zs = sorted({round((lo.z + hi.z) / 2, 2) for g, lo, hi in islands(bm)
                          if (hi.y - lo.y) > 90 and (hi.x - lo.x) < 0.2 and on_side(lo, hi)})
        if len(rail_zs) != 2:
            raise RuntimeError("gantry: expected 2 rails on side %+d, found %d" % (sx, len(rail_zs)))
        # Ascending, so each span is found in the piece the previous cut left.
        for a, b in sorted(spans):
            for rz in rail_zs:
                rail = next(g for g, lo, hi in islands(bm)
                            if (hi.y - lo.y) > 1.0 and (hi.x - lo.x) < 0.2 and on_side(lo, hi)
                            and abs((lo.z + hi.z) / 2 - rz) < 0.05
                            and lo.y < a + 0.01 and hi.y > b - 0.01)
                open_span(bm, rail, a, b)
                cut += 1
        cut += delete_islands(
            bm, lambda lo, hi: ((hi.y - lo.y) < 0.2 and (hi.z - lo.z) > 0.9
                                and abs((lo.x + hi.x) / 2 - sx * GANTRY_RAIL_X) < 0.06
                                and any(a - 0.1 < (lo.y + hi.y) / 2 < b + 0.1 for a, b in spans)),
            None, "gantry posts")
        strand0 = sc.LADDER_Y[0] - 3.0 + 2.0

        def strand_hit(lo, hi, spans=spans):
            if not (lo.z > 20.4 and (hi.z - lo.z) < 1.5 and 0.8 < (hi.y - lo.y) < 3.0
                    and abs((lo.x + hi.x) / 2 - sx * GANTRY_RAIL_X) < 0.1):
                return False
            k = math.floor(((lo.y + hi.y) / 2 - strand0) / 9.0)
            s0 = strand0 + 9.0 * k
            return any(s0 < b and s0 + 9.0 > a for a, b in spans)
        cut += delete_islands(bm, strand_hit, None, "bunting")
    frames = [g for g, lo, hi in islands(bm)
              if abs((lo.y + hi.y) / 2 - GANTRY_FRAME_Y) < 0.2 and (hi.x - lo.x) > 8.0 and hi.z < GANTRY_WALK]
    if len(frames) != 1:
        raise RuntimeError("gantry: expected 1 cross-frame at y %.2f, found %d" % (GANTRY_FRAME_Y, len(frames)))
    cut_plane(bm, frames[0], (-GANTRY_HW, 0.0, 0.0), (1.0, 0.0, 0.0), keep_positive=True)
    return cut + 1


def rust_gantry(obj):
    """Repaint the gantry island by island: some paint survives, most of it rusts."""
    me = obj.data
    extra = bl.link_materials(["Mat_Metal_Rust_Pale", "Mat_Metal_Rust_Deep"])
    for m in extra:
        me.materials.append(m)
    pale, deep = len(me.materials) - 2, len(me.materials) - 1
    swaps = {M.YELLOW: ((M.YELLOW, 0.3), (M.RUST, 0.3), (pale, 0.25), (M.HULLRUST, 0.15)),
             M.STEEL: ((M.STEEL, 0.45), (deep, 0.35), (M.RUST, 0.2))}

    def paint(bm):
        n = 0
        for i, (group, lo, hi) in enumerate(islands(bm)):
            rng = rng_for("gantry", i)
            faces = {f for v in group for f in v.link_faces}
            picks = {src: rng.choices([m for m, _ in opts], [w for _, w in opts])[0]
                     for src, opts in swaps.items()}
            for f in faces:
                if f.material_index in picks:
                    f.material_index = picks[f.material_index]
            n += 1
        return n
    return with_bmesh(obj, paint)


def loose_on_floor(lo, hi, spec, loc, scale):
    """An island (object-local bounds) standing loose on a gallery floor: low,
    small, and not the gallery's own kerb."""
    floor = (spec["floor_z"] - loc.z) / scale.z
    return (lo.z >= floor - 0.05 / scale.z and hi.z <= floor + 1.0 / scale.z
            and max(hi.x - lo.x, hi.y - lo.y) * scale.x < 1.5)


def gallery_loose(obj, spec):
    """World bounds of every fitting standing loose on a tower's gallery floor."""
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    body = (spec["body_r"] + 0.01) / obj.scale.x
    out = []
    for group, lo, hi in islands(bm):
        if min(math.hypot(v.co.x, v.co.y) for v in group) > body and \
                loose_on_floor(lo, hi, spec, obj.location, obj.scale):
            pts = [obj.matrix_world @ v.co for v in group]
            out.append((Vector((min(q.x for q in pts), min(q.y for q in pts), min(q.z for q in pts))),
                        Vector((max(q.x for q in pts), max(q.y for q in pts), max(q.z for q in pts)))))
    bm.free()
    return out


def gallery_inner_r(obj, spec):
    """The inner edge of a gallery's clear walk: the tower wall, or the far side
    of whatever fitting stands against it."""
    c = obj.location
    r = spec["body_r"]
    for lo, hi in gallery_loose(obj, spec):
        for x in (lo.x, hi.x):
            for y in (lo.y, hi.y):
                r = max(r, math.hypot(x - c.x, y - c.y))
    return r


def widen_gallery(obj, spec):
    """Push a tower's gallery railing out to `rail_r` and carve it where the
    gantry runs in.

    Measured, not guessed: the lantern's gallery is 0.87 m between body and
    railing and the dome's 0.95 m, both narrower than the 1.0 m capsule. Every
    vertex of the gallery ring outboard of `split_r` moves out by the same
    distance, so railings and posts keep their section; islands wholly outboard
    of the body (posts, rails, the kerb) move whole.
    """
    loc, scale = obj.location, obj.scale
    delta = (spec["rail_r"] - spec["old_rail_r"]) / scale.x
    split = spec["split_r"] / scale.x
    body = (spec["body_r"] + 0.1) / scale.x
    top = (spec["band_top"] - loc.z) / scale.z
    floor = (spec["floor_z"] - loc.z) / scale.z
    opening = GALLERY_OPENING / scale.x

    def radial(v):
        return math.hypot(v.co.x, v.co.y)

    def work(bm):
        moved = 0
        for group, lo, hi in islands(bm):
            if hi.z > top:
                continue
            rs = [radial(v) for v in group]
            whole = min(rs) > body
            if whole and loose_on_floor(lo, hi, spec, loc, scale):
                # A fitting standing loose on the gallery floor goes back against
                # the tower wall instead: anywhere mid-gallery it blocks the lap.
                shift = min(rs) - (spec["body_r"] + 0.02) / scale.x
                for v, r in zip(group, rs):
                    k = (r - shift) / r
                    v.co.x *= k
                    v.co.y *= k
                continue
            for v, r in zip(group, rs):
                if (whole or r > split) and r > 1e-6:
                    k = (r + delta) / r
                    v.co.x *= k
                    v.co.y *= k
                    moved += 1
        # The railing where the gantry runs in: posts inside the opening go,
        # rail rings and the kerb are cut back to it and capped.
        rim = (spec["body_r"] + 0.1) / scale.x
        above = floor - 0.2 / scale.z
        delete_islands(bm, lambda lo, hi: (hi.z - lo.z) * scale.z > 0.9 and (hi.x - lo.x) * scale.x < 0.3
                       and abs((lo.x + hi.x) / 2) < opening and math.hypot((lo.x + hi.x) / 2, (lo.y + hi.y) / 2) > rim
                       and lo.z > above, None, "gallery posts")
        for sx in (-1, 1):
            rings = [g for g, lo, hi in islands(bm)
                     if (hi.x - lo.x) * scale.x > 3.0 and lo.z > above and hi.z < top
                     and min(radial(v) for v in g) > rim]
            for g in rings:
                geom = list({e for v in g for e in v.link_edges}) + \
                    list({f for v in g for f in v.link_faces}) + list(g)
                bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(sx * opening, 0.0, 0.0),
                                       plane_no=(1.0, 0.0, 0.0))
        rings = [g for g, lo, hi in islands(bm)
                 if (hi.x - lo.x) * scale.x > 3.0 and lo.z > above and hi.z < top
                 and min(radial(v) for v in g) > rim]
        members = {v for g in rings for v in g}
        doomed = [f for f in bm.faces if abs(f.calc_center_median().x) < opening - 1e-4
                  and all(v in members for v in f.verts)]
        bmesh.ops.delete(bm, geom=doomed, context='FACES_ONLY')
        loose = [v for v in bm.verts if v.is_valid and not v.link_faces and v in members]
        bmesh.ops.delete(bm, geom=loose, context='VERTS')
        edges = [e for e in bm.edges if e.is_boundary
                 and all(abs(abs(v.co.x) - opening) < 1e-3 for v in e.verts)]
        if edges:
            bmesh.ops.holes_fill(bm, edges=edges, sides=0)
        return moved
    return with_bmesh(obj, work)


# ===========================================================================
# Collision primitives - every one a convex island
# ===========================================================================

class Col:
    """World-space collision, one convex island per primitive. `at(offset)` gives
    a view that adds an offset to every point, for building in castle-local
    coordinates. `SkyCityBuilder` turns each 8-vertex axis-aligned island into a
    BoxCollider and every other island into a convex MeshCollider."""

    def __init__(self, bm=None, origin=None):
        self.bm = bm if bm is not None else bmesh.new()
        self.origin = Vector(origin) if origin is not None else Vector()

    def at(self, offset):
        return Col(self.bm, self.origin + Vector(offset))

    def hull(self, points):
        pts = []
        for p in points:
            q = Vector(p) + self.origin
            if all((q - r).length > 1e-5 for r in pts):
                pts.append(q)
        if len(pts) < 4:
            return
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        if min(hi - lo) < 1e-4:
            return
        verts = [self.bm.verts.new(p) for p in pts]
        res = bmesh.ops.convex_hull(self.bm, input=verts)
        spare = [v for v in res["geom_interior"] + res["geom_unused"] if isinstance(v, bmesh.types.BMVert)]
        if spare:
            bmesh.ops.delete(self.bm, geom=spare, context='VERTS')

    def box(self, lo, hi):
        lo, hi = Vector(lo), Vector(hi)
        self.hull([Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)])

    def ramp(self, foot, d, width, z_low, z_high, base=None):
        """A flight's solid: low edge through `foot` (x, y), rising along `d`.

        `base` is the flat underside. None makes the ramp a wedge sitting on its
        own low edge - a flight in the air, which people can walk under.
        """
        foot, d = Vector((foot[0], foot[1], 0.0)), Vector((d[0], d[1], 0.0))
        s = d.normalized().cross(UP) * (width / 2)
        base = z_low if base is None else base
        pts = []
        for e in (s, -s):
            for q, z in ((foot + e, z_low), (foot + d + e, z_high)):
                pts += [Vector((q.x, q.y, z)), Vector((q.x, q.y, base))]
        self.hull(pts)

    def wedge_x(self, y0, y1, x_low, z_low, x_high, z_high):
        """Solid ramp running along x: low edge at x_low, vertical back at x_high."""
        self.ramp((x_low, (y0 + y1) / 2), (x_high - x_low, 0.0), abs(y1 - y0), z_low, z_high)

    def wall(self, a, b, h=RAIL_H, t=0.1, h_b=None):
        """A thin wall whose foot runs a -> b ((x, y, z) each), h tall (h_b at b)."""
        a, b = Vector(a), Vector(b)
        d = Vector((b.x - a.x, b.y - a.y, 0.0))
        if d.length < 1e-4:
            return
        s = d.normalized().cross(UP) * (t / 2)
        hb = h if h_b is None else h_b
        pts = []
        for e in (s, -s):
            pts += [a + e, a + e + UP * h, b + e, b + e + UP * hb]
        self.hull(pts)

    def prism(self, cx, cy, r, z0, z1, n=16):
        pts = []
        for i in range(n):
            a = math.tau * (i + 0.5) / n
            for z in (z0, z1):
                pts.append(Vector((cx + r * math.cos(a), cy + r * math.sin(a), z)))
        self.hull(pts)

    def sector(self, cx, cy, r0, r1, a0, a1, z0, z1):
        pts = []
        for r in (r0, r1):
            for a in (a0, a1):
                for z in (z0, z1):
                    pts.append(Vector((cx + r * math.cos(a), cy + r * math.sin(a), z)))
        self.hull(pts)

    def finish(self, name, coll):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        o = bpy.data.objects.new(name, me)
        o.display_type = 'WIRE'
        o.hide_render = True
        coll.objects.link(o)
        return own(o)


# ===========================================================================
# Drawn-and-collided building blocks. The drawing is `scrap_walkway`'s; the
# collision is laid from the same numbers in the same call.
# ===========================================================================

def floor(p, col, lo, hi, z, style, rng, across='x'):
    """A deck whose walking face is at z, collided as a slab under it."""
    sw.deck(p, lo, hi, z, style, rng, across)
    col.box((lo[0], lo[1], z - DECK_T), (hi[0], hi[1], z))


def railing(p, col, a, b, z, style, rng, collide=True):
    """A railing on the line a -> b at walking height z."""
    sw.railing(p, a, b, z, style, rng)
    if collide:
        col.wall((a[0], a[1], z), (b[0], b[1], z))


def flight(p, col, foot, d, width, z_low, z_high, style, rng, rails=(), base=None,
           rail_cap=None):
    """A stair: drawn treads over a smooth collision ramp.

    `foot` is the (x, y) centre of the low edge, `d` the run vector. `rails`
    names the sides that get a railing by the world direction they face -
    "+x", "-x", "+y", "-y". `rail_cap` is a ceiling: side rails are DRAWN only
    while their top is under it, so no railing pokes through the slab a flight
    rises through. They collide their whole length; past the cap that is inside
    the opening, where it keeps people from stepping into the well.
    """
    foot, d = Vector((foot[0], foot[1], 0.0)), Vector((d[0], d[1], 0.0))
    rise = z_high - z_low
    if rise / d.length > STAIR_K + 1e-3:
        raise RuntimeError("flight at %s is %.1f deg, steeper than the %.1f the capsule can walk"
                           % (tuple(round(c, 2) for c in foot), math.degrees(math.atan(rise / d.length)),
                              math.degrees(math.atan(STAIR_K))))
    s = d.normalized().cross(UP)
    wanted = []
    for sx in (-1, 1):
        e = s * sx
        label = ("+" if (e.x if abs(e.x) > abs(e.y) else e.y) > 0 else "-") + ("x" if abs(e.x) > abs(e.y) else "y")
        wanted.append(label in rails)
    drawn_rise = rise if rail_cap is None else max(0.0, min(rise, rail_cap - RAIL_H - 0.05 - z_low))
    sw.stair(p, (foot.x, foot.y, z_low), d, width, rise, style, rng,
             rails=tuple(wanted), rail_rise=drawn_rise)
    col.ramp(foot, d, width, z_low, z_high, base=base)
    for want, sx in zip(wanted, (-1, 1)):
        if want:
            e = s * (sx * (width / 2 + 0.05))
            col.wall(Vector((foot.x + e.x, foot.y + e.y, z_low)),
                     Vector((foot.x + d.x + e.x, foot.y + d.y + e.y, z_high)))


def post(p, col, x, y, z0, z1, rng, collide=True):
    """A timber or rusted-pipe post standing on z0."""
    if z1 - z0 < 0.05:
        return
    if rng.random() < 0.5:
        sw.strut(p, (x, y, z0), (x, y, z1), 0.2, 0.2, sw.TIMBER)
        sw.lashing(p, (x, y, z1 - 0.25), 0.12, turns=3)
    else:
        sw.pole(p, (x, y, z0), (x, y, z1), 0.1, rng.choice((sw.RUST, sw.RUST_DEEP)), seg=8)
        p.cyl((x, y, z0 + 0.03), 0.18, 0.06, seg=8, mat=sw.DARK)
    if collide:
        col.box((x - 0.1, y - 0.1, z0), (x + 0.1, y + 0.1, z1))


def top_plate(col, xs, ys, z):
    """A thin plate where a flight meets the floor it arrives on.

    A ramp that ends exactly where a slab begins leaves a seam a downward probe
    can fall through; overlapping by 0.1 m either side closes it.
    """
    col.box((min(xs), min(ys), z - DECK_T), (max(xs), max(ys), z))


def kit_object(p, name, coll, location=None):
    p.bevel(width=0.01, segments=1)
    o = own(p.finish(name, coll))
    if location is not None:
        o.location = location
    return o


# ===========================================================================
# Scene facts
# ===========================================================================

def castle_offset():
    return bpy.data.objects["Mesh_SkyCity_BowCastle"].location.copy()


def stern_offset():
    return bpy.data.objects["Mesh_SkyCity_SternBlock"].location.copy()


def city_objects(prefix):
    """Mesh objects with `prefix`, on the city (not the escort ships at x > 50)."""
    out = []
    for o in bpy.data.objects:
        if o.type != 'MESH' or not o.name.startswith(prefix):
            continue
        xs = [(o.matrix_world @ Vector(c)).x for c in o.bound_box]
        if sum(xs) / 8.0 < 50.0:
            out.append(o)
    return out


def house_solids():
    """Every house, awning and deck floodlight as a box turned with it: the eight
    world corners of its object-space bounds above its own base.

    Above the base only: the user's shared house mesh carries raking struts that
    run 5 m down under the deck to the keel, and a full bounds box would fill the
    promenade lane with an invisible 12 m wall. Turned with the house, not
    world-aligned: a house rotated 20 degrees has an axis-aligned box reaching
    0.8 m further, which put it into the lane opposite the cage's ring frames and
    left the lane narrower than a player.
    """
    solids = []
    for prefix in ("Mesh_SkyCity_Home", "Mesh_SkyCity_Shade", "Mesh_SkyCity_Flood"):
        for o in city_objects(prefix):
            if not len(o.data.vertices):
                continue
            mw, base, kz = o.matrix_world, o.location.z, o.scale.z
            local = [v.co for v in o.data.vertices if v.co.z * kz >= -0.05] or [v.co for v in o.data.vertices]
            lo = Vector((min(q.x for q in local), min(q.y for q in local), min(q.z for q in local)))
            hi = Vector((max(q.x for q in local), max(q.y for q in local), max(q.z for q in local)))
            # A roof people walk on is the top of the house's solid; what stands
            # on the roof collides on its own.
            if WALKABLE_ROOF in o:
                hi.z = min(hi.z, (o[WALKABLE_ROOF] - base) / kz)
            if (hi.z - lo.z) * kz < 1.2 and prefix != "Mesh_SkyCity_Home":
                continue
            if 0.0 < base < 2.0:
                lo.z = -base / kz
            solids.append([mw @ Vector((x, y, z)) for x in (lo.x, hi.x) for y in (lo.y, hi.y) for z in (lo.z, hi.z)])
    return solids


def house_footprints():
    """`house_solids` as world-aligned boxes, for placement tests that want a
    cheap, conservative overlap check."""
    return [(Vector((min(p.x for p in c), min(p.y for p in c), min(p.z for p in c))),
             Vector((max(p.x for p in c), max(p.y for p in c), max(p.z for p in c)))) for c in house_solids()]


def world_bounds(o, zmin=None):
    mw = o.matrix_world
    pts = [mw @ v.co for v in o.data.vertices]
    if zmin is not None:
        pts = [q for q in pts if q.z >= zmin] or pts
    return (Vector((min(q.x for q in pts), min(q.y for q in pts), min(q.z for q in pts))),
            Vector((max(q.x for q in pts), max(q.y for q in pts), max(q.z for q in pts))))


def radius_in_band(o, z0, z1, pad=0.08):
    """Largest horizontal radius of an object's vertices between z0 and z1."""
    mw, c = o.matrix_world, o.matrix_world.translation
    rs = [math.hypot(q.x - c.x, q.y - c.y) for q in (mw @ v.co for v in o.data.vertices) if z0 <= q.z <= z1]
    return (max(rs) + pad) if rs else 0.0


# ===========================================================================
# The castle, rebuilt hollow (castle-local geometry, world collision)
# ===========================================================================

def wall_x(p, col, x0, x1, y0, y1, z0, z1, doors=(), mat=M.HULLRUST):
    """A wall running along y (thin in x) with openings (y_a, y_b, height)."""
    cur = y0
    for a, b, h in sorted(doors):
        p.slab((x0, cur, z0), (x1, a, z1), mat)
        col.box((x0, cur, z0), (x1, a, z1))
        p.slab((x0, a, z0 + h), (x1, b, z1), mat)
        col.box((x0, a, z0 + h), (x1, b, z1))
        cur = b
    p.slab((x0, cur, z0), (x1, y1, z1), mat)
    col.box((x0, cur, z0), (x1, y1, z1))


def wall_y(p, col, y0, y1, x0, x1, z0, z1, doors=(), mat=M.HULLRUST):
    """A wall running along x (thin in y) with openings (x_a, x_b, height)."""
    cur = x0
    for a, b, h in sorted(doors):
        p.slab((cur, y0, z0), (a, y1, z1), mat)
        col.box((cur, y0, z0), (a, y1, z1))
        p.slab((a, y0, z0 + h), (b, y1, z1), mat)
        col.box((a, y0, z0 + h), (b, y1, z1))
        cur = b
    p.slab((cur, y0, z0), (x1, y1, z1), mat)
    col.box((cur, y0, z0), (x1, y1, z1))


def rect_minus(lo, hi, hole):
    """lo..hi (x, y) split into up to four rectangles around `hole` (x0, x1, y0, y1)."""
    if hole is None:
        return [(lo, hi)]
    hx0, hx1, hy0, hy1 = hole
    pieces = [((lo[0], lo[1]), (hi[0], hy0)), ((lo[0], hy1), (hi[0], hi[1])),
              ((lo[0], hy0), (hx0, hy1)), ((hx1, hy0), (hi[0], hy1))]
    return [(a, b) for a, b in pieces if b[0] - a[0] > 1e-3 and b[1] - a[1] > 1e-3]


def slab(p, col, lo, hi, z0, z1, hole, mat, collide=True):
    for (ax, ay), (bx, by) in rect_minus(lo, hi, hole):
        p.slab((ax, ay, z0), (bx, by, z1), mat)
        if collide:
            col.box((ax, ay, z0), (bx, by, z1))


def core_levels():
    """Both switchbacks, derived from the terraces. Castle-local."""
    def storey(foot_x, z0, z1):
        run = (z1 - z0) / 2 / STAIR_K
        top_x = foot_x - run
        return dict(foot_x=foot_x, top_x=top_x, mid_z=(z0 + z1) / 2, z0=z0, z1=z1, run=run,
                    landing=(top_x - CORE_LANDING, top_x))
    f1, f2 = storey(F1_CORE_FOOT_X, T1, T2), storey(F2_CORE_FOOT_X, T2, T3)

    def hole(st, ceiling):
        # Opens over the upper flight wherever a head on it would reach the
        # ceiling slab, plus a margin.
        clear_z = ceiling - HEADROOM - STAIRWELL_MARGIN
        x0 = st["top_x"] + max(0.0, clear_z - st["mid_z"]) / STAIR_K
        return (x0 - 0.05, st["foot_x"], CORE_UPPER_Y[0] - 0.05, CORE_UPPER_Y[1] + 0.05)

    def guard_to(st, floor_z):
        # The upper floor's hole is railed only where the drop into it hurts.
        return st["top_x"] + (floor_z - SAFE_DROP - st["mid_z"]) / STAIR_K
    return dict(f1=f1, f2=f2, hole1=hole(f1, F1_CEIL), hole2=hole(f2, F2_CEIL),
                guard1=guard_to(f1, T2), guard2=guard_to(f2, T3))


def roof_stair():
    """The castle-roof flight up to the crown gantry, and where it lands. Castle-local."""
    top_y = ROOF_STAIR_FOOT_Y - (GANTRY_WALK - T3) / STAIR_K
    return dict(top_y=top_y, landing=(top_y - ROOF_LANDING_DEPTH, top_y),
                gantry_end_local=GANTRY_Y0 - castle_offset().y)


def castle_hollow(mats, vis, col):
    """All three storeys you can walk into, the outside unchanged.

    Circulation, readable from outside (GDC-L1-LEVEL-0001): both promenade lanes
    run straight in through doors aligned with them; a stair up the starboard
    side climbs to the first floor; a switchback core in the aft third climbs on
    through the second floor to the roof; every floor opens onto its terrace; and
    a timber stair on the roof reaches the crown gantry.
    """
    off = castle_offset()
    cl = col.at((0.0, off.y, off.z))
    (g_lo, g_hi, g_w, g_y0, g_y1, _), (f_lo, f_hi, f_w, f_y0, f_y1, _), \
        (s_lo, s_hi, s_w, s_y0, s_y1, _) = sc.CASTLE_STEPS
    L = core_levels()
    p = Part(mats)
    stair_top_y = INNER_STAIR_FOOT_Y - T1 / STAIR_K
    well_aft_y = INNER_STAIR_FOOT_Y - (GF_CEIL - HEADROOM - STAIRWELL_MARGIN) / STAIR_K
    inner_hole = (INNER_STAIR_X[0], INNER_STAIR_X[1], stair_top_y, well_aft_y)

    # --- ground floor: slab under a deck overlay, walls, doors ----------------
    p.slab((-g_w, g_y0, g_lo), (g_w, g_y1, -OVERLAY), M.STEEL)
    wall_y(p, cl, g_y0, g_y0 + WALL, -g_w, g_w, 0.0, GF_CEIL)
    wall_y(p, cl, g_y1 - WALL, g_y1, -g_w, g_w, 0.0, GF_CEIL,
           doors=[(a, b, GF_DOOR_H) for a, b in AFT_DOORS])
    for sx in (-1, 1):
        x0, x1 = sorted((sx * g_w, sx * (g_w - WALL)))
        wall_x(p, cl, x0, x1, g_y0 + WALL, g_y1 - WALL, 0.0, GF_CEIL,
               doors=[(GF_FLANK_DOOR[0], GF_FLANK_DOOR[1], GF_DOOR_H)])
    slab(p, cl, (-g_w, g_y0), (g_w, g_y1), GF_CEIL, g_hi, inner_hole, M.HULLRUST)
    slab(p, cl, (-g_w - 0.55, g_y0 - 0.55), (g_w + 0.55, g_y1 + 0.55), g_hi, T1 - OVERLAY,
         inner_hole, M.STEEL, collide=False)
    for (ax, ay), (bx, by) in rect_minus((-g_w - 0.55, g_y0 - 0.55), (g_w + 0.55, g_y1 + 0.55), inner_hole):
        cl.box((ax, ay, g_hi), (bx, by, T1))

    # --- first floor ----------------------------------------------------------
    wall_y(p, cl, f_y0, f_y0 + WALL, -f_w, f_w, T1, F1_CEIL, mat=M.YELLOW)
    # No aft door on the first floor: the user's stretched forward gas bag
    # pokes its nose through that wall, leaving 0.6 m of headroom outside it.
    wall_y(p, cl, f_y1 - WALL, f_y1, -f_w, f_w, T1, F1_CEIL, mat=M.YELLOW)
    for sx, door in ((-1, F1_PORT_DOOR), (1, F1_STBD_DOOR)):
        x0, x1 = sorted((sx * f_w, sx * (f_w - WALL)))
        wy, wz = F1_WINDOW
        wall_x(p, cl, x0, x1, f_y0 + WALL, f_y1 - WALL, T1, F1_CEIL, mat=M.YELLOW,
               doors=[(door[0], door[1], UPPER_DOOR_H)])
        p.slab((x0 - 0.02, wy[0], wz[0]), (x1 + 0.02, wy[1], wz[1]), M.GLASS)
    slab(p, cl, (-f_w, f_y0), (f_w, f_y1), F1_CEIL, f_hi, L["hole1"], M.YELLOW)
    slab(p, cl, (-f_w - 0.55, f_y0 - 0.55), (f_w + 0.55, f_y1 + 0.55), f_hi, T2 - OVERLAY,
         L["hole1"], M.STEEL, collide=False)
    for (ax, ay), (bx, by) in rect_minus((-f_w - 0.55, f_y0 - 0.55), (f_w + 0.55, f_y1 + 0.55), L["hole1"]):
        cl.box((ax, ay, f_hi), (bx, by, T2))

    # --- second floor ---------------------------------------------------------
    wall_y(p, cl, s_y0, s_y0 + WALL, -s_w, s_w, T2, F2_CEIL, mat=M.YELLOW)
    wall_y(p, cl, s_y1 - WALL, s_y1, -s_w, s_w, T2, F2_CEIL, mat=M.YELLOW)
    for sx in (-1, 1):
        x0, x1 = sorted((sx * s_w, sx * (s_w - WALL)))
        wall_x(p, cl, x0, x1, s_y0 + WALL, s_y1 - WALL, T2, F2_CEIL, mat=M.YELLOW,
               doors=[(F2_DOOR[0], F2_DOOR[1], UPPER_DOOR_H)])
    slab(p, cl, (-s_w, s_y0), (s_w, s_y1), F2_CEIL, s_hi, L["hole2"], M.YELLOW)
    slab(p, cl, (-s_w - 0.55, s_y0 - 0.55), (s_w + 0.55, s_y1 + 0.55), s_hi, T3 - OVERLAY,
         L["hole2"], M.STEEL, collide=False)
    for (ax, ay), (bx, by) in rect_minus((-s_w - 0.55, s_y0 - 0.55), (s_w + 0.55, s_y1 + 0.55), L["hole2"]):
        cl.box((ax, ay, s_hi), (bx, by, T3))

    # --- the plated language of the outside, split around every opening -------
    for sx in (-1, 1):
        for zlo, zhi, w, a, b, gaps in ((0.0, g_hi, g_w, g_y0, g_y1, [GF_FLANK_DOOR]),
                                         (T1, f_hi, f_w, f_y0, f_y1,
                                          [F1_PORT_DOOR if sx < 0 else F1_STBD_DOOR, F1_WINDOW[0]]),
                                         (T2, s_hi, s_w, s_y0, s_y1, [F2_DOOR])):
            zc = (zlo + zhi) / 2 + 0.4
            cur = a + 0.2
            for ga, gb in sorted(gaps) + [(b - 0.2, b)]:
                if ga - cur > 0.4:
                    p.box((sx * (w + 0.07), (cur + ga) / 2, zc), (0.14, ga - cur, 0.85), M.HULLRUST)
                    p.rivets((sx * (w + 0.16), cur + 0.2, zc), (sx * (w + 0.16), ga - 0.2, zc),
                             max(2, int((ga - cur) / 1.4)), radius=0.055, height=0.04,
                             axis='X', mat=M.DARK)
                cur = gb
        p.louvres((sx * (g_w - 0.1), g_y0 + 4.2, 0.6), (sx * (g_w + 0.2), g_y0 + 8.4, 4.2), 7,
                  axis='Y', mat=M.DARK)
        for i in range(6):
            yy = g_y0 + 1.0 + i * ((g_y1 - g_y0 - 2.0) / 5.0)
            p.box((sx * (g_w + 0.22), yy, 2.5), (0.30, 0.34, 5.0), M.STEEL)
        sc.beam(p, (sx * (g_w + 0.22), g_y0 + 0.8, 4.9), (sx * (g_w + 0.22), g_y1 - 0.8, 4.9),
                0.36, 0.30, M.STEEL)
        for a, b in ((g_y0 + 0.8, GF_FLANK_DOOR[0] - 0.1), (GF_FLANK_DOOR[1] + 0.1, g_y1 - 0.8)):
            sc.beam(p, (sx * (g_w + 0.22), a, 0.4), (sx * (g_w + 0.22), b, 0.4), 0.36, 0.26, M.STEEL)
    hw, top = sc.CASTLE_HW, sc.CASTLE_TOP
    p.slab((-hw + 3.6, g_y0 + 2.8, 11.6), (hw - 3.6, g_y0 + 3.1, top - 1.2), M.GLASS)
    for sx in (-1, 1):
        p.slab((sx * (hw - 3.5), g_y0 + 3.0, 11.6), (sx * (hw - 3.8), g_y1 - 3.8, top - 1.2), M.GLASS)
    p.greeble((-4.0, s_y0 + 0.2, T3), (4.0, s_y0 + 1.2, T3 + 1.0), 8, seed=sc.SEED + 7,
              scale=(0.4, 1.0), mat=M.STEEL)
    sc.castle_derrick(p, g_y0, top, stay_to=(0.0, s_y0 + 1.5, T3))
    p.bevel(width=0.03, segments=1)

    castle = bpy.data.objects["Mesh_SkyCity_BowCastle"]
    tmp = p.finish("Mesh_SkyCity_BowCastle_hollow", vis)
    pristine(castle)
    old = castle.data
    castle.data = tmp.data
    bpy.data.objects.remove(tmp, do_unlink=True)
    if old.users == 0:
        bpy.data.meshes.remove(old)
    castle.data.name = castle.name

    # Things of the user's that stand inside the rooms: the pod on the castle's
    # front, collided band by band at its measured radius, and the raised prow.
    for name, bands in POD_BANDS.items():
        o = bpy.data.objects[name]
        c = o.matrix_world.translation
        for z0, z1 in bands:
            col.prism(c.x, c.y, radius_in_band(o, z0, z1), z0, z1)
    prow = bpy.data.objects.get("Mesh_SkyCity_Prow")
    if prow is not None:
        mw = prow.matrix_world
        inside = [mw @ v.co for v in prow.data.vertices]
        inside = [q for q in inside if q.y > off.y + g_y0 + WALL and abs(q.x) < g_w - WALL and q.z > 0.0]
        if inside:
            col.box((min(q.x for q in inside), min(q.y for q in inside), 0.0),
                    (max(q.x for q in inside), max(q.y for q in inside), max(q.z for q in inside)))


def castle_climb(kit, vis, col):
    """Everything you walk on inside and on top of the castle, in the tribe's kit:
    deck overlays on every floor and terrace, the stairs, the rails round every
    opening and landing, and the lanterns. One object, castle-local."""
    off = castle_offset()
    cl = col.at((0.0, off.y, off.z))
    (g_lo, g_hi, g_w, g_y0, g_y1, _), (f_lo, f_hi, f_w, f_y0, f_y1, _), \
        (s_lo, s_hi, s_w, s_y0, s_y1, _) = sc.CASTLE_STEPS
    L = core_levels()
    R = roof_stair()
    p = Part(kit)
    rng = rng_for("castle")
    stair_top_y = INNER_STAIR_FOOT_Y - T1 / STAIR_K
    well_aft_y = INNER_STAIR_FOOT_Y - (GF_CEIL - HEADROOM - STAIRWELL_MARGIN) / STAIR_K
    inner_hole = (INNER_STAIR_X[0], INNER_STAIR_X[1], stair_top_y, well_aft_y)

    def overlay(lo, hi, z, hole=None):
        """Patchwork decking over a floor or terrace, laid in pieces a few metres long."""
        for (ax, ay), (bx, by) in rect_minus(lo, hi, hole):
            y = ay
            while y < by - 1e-3:
                run = min(by - y, rng.uniform(2.5, 4.5))
                if by - (y + run) < 1.0:
                    run = by - y
                sw.deck(p, (ax, y), (bx, y + run), z, rng.choice(sw.DECK_STYLES), rng)
                y += run

    # Floors, inside the walls. The ground floor's collision is the promenade
    # deck box laid in `fixed_collision`.
    overlay((-g_w + WALL, g_y0 + WALL), (g_w - WALL, g_y1 - WALL), 0.0)
    overlay((-g_w - 0.55, g_y0 - 0.55), (g_w + 0.55, g_y1 + 0.55), T1, inner_hole)
    overlay((-f_w - 0.55, f_y0 - 0.55), (f_w + 0.55, f_y1 + 0.55), T2, L["hole1"])
    overlay((-s_w - 0.55, s_y0 - 0.55), (s_w + 0.55, s_y1 + 0.55), T3, L["hole2"])

    # Ground floor to first floor.
    flight(p, cl, (sum(INNER_STAIR_X) / 2, INNER_STAIR_FOOT_Y), (0.0, stair_top_y - INNER_STAIR_FOOT_Y),
           INNER_STAIR_X[1] - INNER_STAIR_X[0], 0.0, T1, "timber", rng, rails=("-x", "+x"),
           base=0.0, rail_cap=GF_CEIL)
    top_plate(cl, INNER_STAIR_X, (stair_top_y - 0.1, stair_top_y + 0.1), T1)
    guard_to = INNER_STAIR_FOOT_Y - (T1 - SAFE_DROP) / STAIR_K
    railing(p, cl, (inner_hole[0] - 0.05, guard_to), (inner_hole[0] - 0.05, inner_hole[3]), T1, "rope", rng)
    railing(p, cl, (inner_hole[0], inner_hole[3] + 0.05), (inner_hole[1], inner_hole[3] + 0.05), T1, "rope", rng)

    # The switchback core: first floor to second, second to the roof.
    for key, hole_key, guard_key, floor_z, ceiling, upper_floor, spine_style in (
            ("f1", "hole1", "guard1", T1, F1_CEIL, T2, "sheet"),
            ("f2", "hole2", "guard2", T2, F2_CEIL, T3, "sheet")):
        st = L[key]
        lower_c = sum(CORE_LOWER_Y) / 2
        upper_c = sum(CORE_UPPER_Y) / 2
        width = CORE_LOWER_Y[1] - CORE_LOWER_Y[0]
        # Lower flight: solid to the floor, railed on its open aft side unless
        # the aft wall is right there.
        aft_open = (f_y1 if key == "f1" else s_y1) - WALL - CORE_LOWER_Y[1] > 0.3
        flight(p, cl, (st["foot_x"], lower_c), (-st["run"], 0.0), width, floor_z, st["mid_z"],
               "timber", rng, rails=("+y",) if aft_open else (), base=floor_z)
        # Landing, in the air, railed on its three open edges.
        lx0, lx1 = st["landing"]
        floor(p, cl, (lx0, CORE_UPPER_Y[0]), (lx1, CORE_LOWER_Y[1]), st["mid_z"], "planks", rng, across='y')
        railing(p, cl, (lx0 - 0.05, CORE_UPPER_Y[0]), (lx0 - 0.05, CORE_LOWER_Y[1]), st["mid_z"], "rope", rng)
        railing(p, cl, (lx0, CORE_UPPER_Y[0] - 0.05), (lx1, CORE_UPPER_Y[0] - 0.05), st["mid_z"], "rope", rng)
        if aft_open:
            railing(p, cl, (lx0, CORE_LOWER_Y[1] + 0.05), (lx1, CORE_LOWER_Y[1] + 0.05), st["mid_z"], "rope", rng)
        # Upper flight, in the air, guarded up to the ceiling on its open side.
        flight(p, cl, (st["top_x"], upper_c), (st["run"], 0.0), width, st["mid_z"], st["z1"],
               "scrap", rng, rails=("-y",), rail_cap=ceiling)
        top_plate(cl, (st["foot_x"] - 0.1, st["foot_x"] + 0.1), CORE_UPPER_Y, upper_floor)
        # A plank spine between the two flights, floor to ceiling.
        spine_y = CORE_LOWER_Y[0]
        cl.wall((st["top_x"], spine_y, floor_z), (st["foot_x"], spine_y, floor_z), h=ceiling - floor_z)
        x = st["top_x"]
        while x < st["foot_x"] - 0.05:
            w = min(rng.uniform(0.25, 0.4), st["foot_x"] - x)
            p.box((x + w / 2, spine_y, (floor_z + ceiling) / 2 - rng.uniform(0.0, 0.1)),
                  (w - 0.02, 0.05, ceiling - floor_z - rng.uniform(0.05, 0.3)),
                  rng.choices((sw.PLY, sw.TIMBER, sw.RUST), (0.5, 0.35, 0.15))[0])
            x += w
        # The floor above is railed round its opening where the drop hurts.
        hx0, hx1, hy0, hy1 = L[hole_key]
        g = L[guard_key]
        # On the second floor the lower flight above covers the opening's aft
        # side; only the roof needs that side railed.
        if g > hx0:
            railing(p, cl, (hx0, hy0 - 0.05), (g, hy0 - 0.05), upper_floor, "pipe", rng)
            if key == "f2":
                railing(p, cl, (hx0, hy1 + 0.05), (g, hy1 + 0.05), upper_floor, "pipe", rng)
            railing(p, cl, (hx0 - 0.05, hy0), (hx0 - 0.05, hy1), upper_floor, "pipe", rng)

    # Terrace railings, with the core's roof opening and the pod's gaps.
    for sx in (-1, 1):
        for w, a, b, z in ((g_w, g_y0, g_y1, T1), (f_w, f_y0, f_y1, T2), (s_w, s_y0, s_y1, T3)):
            railing(p, cl, (sx * (w + 0.42), a - 0.55), (sx * (w + 0.42), b + 0.55), z,
                    rng.choice(sw.RAIL_STYLES), rng)
    pod_half, nose_half, beacon_half = 3.9, 1.8, 3.6
    railing(p, cl, (-g_w - 0.42, g_y0 - 0.5), (g_w + 0.42, g_y0 - 0.5), T1, "pipe", rng)
    ladder_gap = (CASTLE_T1_LADDER_X - sw.LADDER_W / 2, CASTLE_T1_LADDER_X + sw.LADDER_W / 2)
    railing(p, cl, (-g_w - 0.42, g_y1 + 0.5), (ladder_gap[0], g_y1 + 0.5), T1, "rope", rng)
    railing(p, cl, (ladder_gap[1], g_y1 + 0.5), (g_w + 0.42, g_y1 + 0.5), T1, "rope", rng)
    for a, b in ((-f_w - 0.42, -pod_half), (pod_half, f_w + 0.42)):
        railing(p, cl, (a, f_y0 - 0.5), (b, f_y0 - 0.5), T2, "sheet", rng)
    for a, b in ((-f_w - 0.42, -nose_half), (nose_half, f_w + 0.42)):
        railing(p, cl, (a, f_y1 + 0.5), (b, f_y1 + 0.5), T2, "rope", rng)
    for a, b in ((-s_w - 0.42, -beacon_half), (beacon_half, s_w + 0.42)):
        railing(p, cl, (a, s_y0 - 0.5), (b, s_y0 - 0.5), T3, "pipe", rng)
    railing(p, cl, (-s_w - 0.42, s_y1 + 0.5), (s_w + 0.42, s_y1 + 0.5), T3, "rope", rng)

    # Castle roof to the crown gantry, and the landing round the gantry's end.
    xs = ROOF_STAIR_X
    width = xs[1] - xs[0]
    flight(p, cl, (sum(xs) / 2, ROOF_STAIR_FOOT_Y), (0.0, R["top_y"] - ROOF_STAIR_FOOT_Y), width,
           T3, GANTRY_WALK, "timber", rng, rails=("-x", "+x"), base=T3)
    ly0, ly1 = R["landing"]
    end = R["gantry_end_local"]
    floor(p, cl, (xs[0], ly0), (-GANTRY_HW, ly1), GANTRY_WALK, "planks", rng)
    floor(p, cl, (-GANTRY_HW, ly0), (GANTRY_HW, end), GANTRY_WALK, "plate", rng)
    railing(p, cl, (xs[0], ly0 - 0.05), (GANTRY_HW, ly0 - 0.05), GANTRY_WALK, "rope", rng)
    railing(p, cl, (xs[0] - 0.05, ly0), (xs[0] - 0.05, ly1), GANTRY_WALK, "rope", rng)
    railing(p, cl, (GANTRY_HW + 0.05, ly0), (GANTRY_HW + 0.05, end), GANTRY_WALK, "rope", rng)
    railing(p, cl, (xs[1] + 0.05, ly1 + 0.05), (-GANTRY_HW, ly1 + 0.05), GANTRY_WALK, "rope", rng)
    for x in (xs[0] + 0.1, -GANTRY_HW - 0.1):
        post(p, cl, x, ly0 + 0.1, T3, GANTRY_WALK - 0.12, rng)

    # Lanterns hung from every ceiling.
    for x, y, z in ((-4.0, g_y0 + 3.0, GF_CEIL), (4.0, (g_y0 + g_y1) / 2, GF_CEIL), (-3.0, g_y1 - 3.0, GF_CEIL),
                    (-4.5, (f_y0 + f_y1) / 2, F1_CEIL), (5.5, f_y1 - 2.0, F1_CEIL),
                    (-3.8, s_y0 + 3.5, F2_CEIL), (3.0, s_y1 - 1.2, F2_CEIL)):
        sw.lantern(p, (x, y, z), rng, drop=rng.uniform(0.4, 0.7))

    kit_object(p, "Mesh_SkyCity_CastleClimb", vis, location=off)


# ===========================================================================
# Walkways outboard of the houses: stitched platforms
# ===========================================================================

def platform_plan(sx):
    """The run of platforms down one side: [(y0, y1, outer edge, style, rail style)]."""
    rng = rng_for("platforms", sx)
    y0 = sc.CASTLE_STEPS[0][3] + castle_offset().y
    needs = platform_needs(sx)
    breaks = sorted(SAIL_SLOT_Y)
    plan, y, last_style, last_rail = [], y0, None, None
    while y < EXT_Y1 - 1e-3:
        run = rng.uniform(*PLATFORM_RUN)
        end = min(EXT_Y1, y + run)
        for b in breaks:
            if y < b < end:
                end = b
        if EXT_Y1 - end < PLATFORM_RUN[0] * 0.6 and end < EXT_Y1:
            end = EXT_Y1
        if SAIL_SLOT_Y[0] <= y < SAIL_SLOT_Y[1]:
            y = SAIL_SLOT_Y[1]
            continue
        outer = rng.uniform(*EXT_OUT_RANGE)
        for a, b, need in needs:
            if a < end and b > y:
                outer = max(outer, need)
        style = rng.choice([s for s in sw.DECK_STYLES if s != last_style])
        rail = rng.choice([s for s in sw.RAIL_STYLES if s != last_rail])
        plan.append((y, end, outer, style, rail))
        last_style, last_rail = style, rail
        y = end
    return plan


def platform_needs(sx):
    """Where a platform on side `sx` must reach further out: (y0, y1, outer edge)."""
    needs = []
    for lo, hi in house_footprints():
        reach = max(abs(lo.x), abs(hi.x))
        if math.copysign(1, lo.x + hi.x) == sx and reach > EXT_IN:
            needs.append((lo.y, hi.y, reach + HOUSE_CLEARANCE))
    for i, (yc, s, _) in enumerate(sc.SKYWALKS, start=1):
        if s != sx:
            continue
        walk = bpy.data.objects["Mesh_SkyCity_Walk%02d_Catwalk_Straight" % i]
        lo, hi = world_bounds(walk)
        run = walk.location.z / STAIR_K
        needs.append((lo.y - run - 0.5, hi.y + run + 0.5, SKYWALK_OUTER))
    for variant, s, y in CRANES:
        if s == sx:
            needs.append((y - JETTY_HALF, y + JETTY_HALF, JETTY_OUTER[variant]))
    return needs


def jetty_outer(sx, y):
    return max(JETTY_OUTER[v] for v, s, yc in CRANES if s == sx and abs(yc - y) <= JETTY_HALF)


def crane_gates(sx):
    """Railing openings where a crane's boom heels below the rail, per side."""
    gates = []
    for variant, s, y in CRANES:
        if s == sx and variant == "ScrapDerrick":
            gates.append((y - CRANE_GATE, y + CRANE_GATE))
    return gates


def walkways(kit, vis, col):
    """Each side a run of separate platforms: its own deck, width, railing and
    props, and planks thrown over the gap to the next one."""
    for sx in (-1, 1):
        plan = platform_plan(sx)
        gates = crane_gates(sx)
        for i, (y0, y1, outer, style, rail) in enumerate(plan):
            rng = rng_for("platform", sx, i)
            p = Part(kit)
            gap = rng.uniform(*PLATFORM_GAP) if i > 0 and abs(plan[i - 1][1] - y0) < 1e-3 else 0.0
            a, b = sorted((sx * EXT_IN, sx * outer))
            sw.deck(p, (a, y0 + gap / 2), (b, y1), 0.0, style, rng)
            col.box((a, y0, -DECK_T), (b, y1, 0.0))
            # The outer railing, opened at a derrick's boom.
            cur = y0 + gap / 2
            for ga, gb in sorted(gates) + [(y1, y1)]:
                if gb <= y0 or ga > y1:
                    continue
                railing(p, col, (sx * (outer - 0.05), cur), (sx * (outer - 0.05), max(cur, min(ga, y1))), 0.0,
                        rail, rng)
                cur = max(cur, gb)
            if i == 0:
                railing(p, col, (a, y0 + 0.05), (b, y0 + 0.05), 0.0, rail, rng)
            # Where this platform is wider than the one before, rail the step.
            if i > 0 and abs(plan[i - 1][1] - y0) < 1e-3 and abs(plan[i - 1][2] - outer) > 0.15:
                narrow, wide = sorted((plan[i - 1][2], outer))
                railing(p, col, (sx * narrow, y0), (sx * (wide - 0.05), y0), 0.0, rail, rng)
            # Stitch planks over the gap to the previous platform.
            if gap > 0.0:
                for _ in range(rng.randint(2, 3)):
                    cx = sx * rng.uniform(EXT_IN + 0.8, min(outer, plan[i - 1][2]) - 0.8)
                    sw.obox(p, Vector((cx, y0, 0.02)), (rng.uniform(0.25, 0.4), gap + 0.5, 0.04),
                            Vector((0, 1, 0)), rng.choice((sw.TIMBER, sw.PLY)),
                            yaw=math.radians(rng.uniform(-6, 6)))
            # Props under it, from the keel girder's lower longeron.
            y = y0 + rng.uniform(0.6, 1.4)
            prop = rng.choice((sw.TIMBER, sw.RUST_DEEP))
            while y < y1 - 0.4:
                sw.knee(p, (sx * (outer - 0.45), y, -0.22), (sx * sc.KEEL_HW, y, sc.KEEL_BOT + 0.5), prop,
                        w=0.14 if prop == sw.TIMBER else 0.11)
                y += rng.uniform(2.6, 3.6)
            if i % 2 == 1:
                sw.bracket_lantern(p, (sx * (outer - 0.05), (y0 + y1) / 2, RAIL_H + 0.05), (sx, 0), rng)
            kit_object(p, "Mesh_SkyCity_Walkway%s%02d" % ("P" if sx < 0 else "S", i + 1), vis)

        # The strip inboard of the sail slot, and the slot's own railing.
        rng = rng_for("slot", sx)
        p = Part(kit)
        ia, ib = sorted((sx * EXT_IN, sx * SAIL_SLOT_IN))
        floor(p, col, (ia, SAIL_SLOT_Y[0]), (ib, SAIL_SLOT_Y[1]), 0.0, "scrap", rng)
        railing(p, col, (sx * SAIL_SLOT_IN, SAIL_SLOT_Y[0]), (sx * SAIL_SLOT_IN, SAIL_SLOT_Y[1]), 0.0, "rope", rng)
        for y in SAIL_SLOT_Y:
            outer = next(o for a, b, o, _, _ in plan if a - 1e-3 <= y <= b + 1e-3)
            if outer - 0.05 > SAIL_SLOT_IN + 0.15:
                railing(p, col, (sx * SAIL_SLOT_IN, y), (sx * (outer - 0.05), y), 0.0, "rope", rng)
        kit_object(p, "Mesh_SkyCity_WalkwaySlot%s" % ("P" if sx < 0 else "S"), vis)

        # Dark grating laid under the old patchwork strip, so the gaps between
        # its planks read as grating instead of holes.
        p = Part(kit)
        ia, ib = sorted((sx * sc.LANE_OUT, sx * EXT_IN))
        p.slab((ia, sc.CASTLE_STEPS[0][3] + castle_offset().y, -0.20), (ib, EXT_Y1, -0.08), sw.BLACK)
        kit_object(p, "Mesh_SkyCity_WalkwayUnderlay%s" % ("P" if sx < 0 else "S"), vis)


# ===========================================================================
# Loading bays: cranes and trade goods
# ===========================================================================

def library_sources(src_coll):
    """Append the crane and goods variations once, hidden, as stamp sources."""
    blends = {
        os.path.join(LIB, "components", "mechanical", "cargo_crane.blend"):
            ["Coll_CargoCrane_" + v for v in {c[0] for c in CRANES}],
        os.path.join(LIB, "components", "props", "trade_goods.blend"):
            ["Coll_TradeGoods_" + g for g in {g[0] for g in GOODS} | {g[0] for g in CASTLE_GOODS}],
    }
    groups = {}
    for blend, colls in blends.items():
        with bpy.data.libraries.load(blend, link=False) as (src, dst):
            dst.collections = sorted(colls)
        for coll in dst.collections:
            objs = []
            for o in list(coll.all_objects):
                src_coll.objects.link(o)
                own(o)
                objs.append(o)
            groups[coll.name.split(".")[0].split("_", 2)[2]] = objs
            bpy.data.collections.remove(coll)
    bpy.context.view_layer.update()
    return groups


def place_group(srcs, coll, prefix, location, yaw):
    delta = Matrix.Translation(location) @ Matrix.Rotation(yaw, 4, 'Z')
    made = bl.stamp(srcs, delta, coll, prefix, [])
    for o in made:
        own(o)
    bpy.context.view_layer.update()
    return made


def footprint_box(objs, col, pad=0.05, z_top=None):
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    lo = Vector((min(q.x for q in pts) - pad, min(q.y for q in pts) - pad, max(0.0, min(q.z for q in pts))))
    hi = Vector((max(q.x for q in pts) + pad, max(q.y for q in pts) + pad,
                 max(q.z for q in pts) if z_top is None else z_top))
    col.box(lo, hi)
    return lo, hi


def loading_bays(vis, col, groups):
    placed = []
    for i, (variant, sx, y) in enumerate(CRANES, start=1):
        out = JETTY_OUTER[variant] - CRANE_INSET - max(x1 for _, _, x1, _ in cc.FOOTPRINTS[variant])
        yaw = 0.0 if sx > 0 else math.pi
        made = place_group(groups[variant], vis, "Mesh_SkyCity_Crane%02d" % i, Vector((sx * out, y, 0.0)), yaw)
        m = Matrix.Translation((sx * out, y, 0.0)) @ Matrix.Rotation(yaw, 4, 'Z')
        for x0, y0, x1, y1 in cc.FOOTPRINTS[variant]:
            pts = [m @ Vector((x, yy, 0.0)) for x in (x0, x1) for yy in (y0, y1)]
            col.box((min(q.x for q in pts), min(q.y for q in pts), 0.0),
                    (max(q.x for q in pts), max(q.y for q in pts), cc.FOOTPRINT_H))
        placed.append((variant, made))
    for i, (kind, sx, y, yaw) in enumerate(GOODS, start=1):
        out = jetty_outer(sx, y) - GOODS_INSET
        made = place_group(groups[kind], vis, "Mesh_SkyCity_Goods%02d" % i, Vector((sx * out, y, 0.0)),
                           math.radians(yaw) + (0.0 if sx > 0 else math.pi))
        footprint_box(made, col)
        placed.append((kind, made))
    off = castle_offset()
    for i, (kind, x, y, yaw, z) in enumerate(CASTLE_GOODS, start=1):
        made = place_group(groups[kind], vis, "Mesh_SkyCity_Stock%02d" % i, Vector((x, y + off.y, z)),
                           math.radians(yaw))
        footprint_box(made, col)
        placed.append((kind, made))
    return len(placed)


# ===========================================================================
# Skywalk stairs and stern supports
# ===========================================================================

def skywalk_access(kit, vis, col):
    O = bpy.data.objects
    houses = house_footprints()

    def in_house(x, y):
        return any(lo.x - 0.15 <= x <= hi.x + 0.15 and lo.y - 0.15 <= y <= hi.y + 0.15
                   for lo, hi in houses)

    for i, (yc, sx, zc) in enumerate(sc.SKYWALKS, start=1):
        rng = rng_for("skywalk", i)
        p = Part(kit)
        walk = O["Mesh_SkyCity_Walk%02d_Catwalk_Straight" % i]
        lo, hi = world_bounds(walk)
        s = walk.location.z
        # The skywalk's own surface and rails, at the height it is really walked.
        # Overlapping the stair by 0.1 m: meeting it exactly edge to edge leaves
        # a seam a downward probe falls straight through.
        col.box((lo.x, lo.y - 0.1, s - DECK_T), (hi.x, hi.y + 0.1, s))
        for x in (lo.x + 0.1, hi.x - 0.1):
            col.box((x - 0.05, lo.y, s), (x + 0.05, hi.y, s + RAIL_H))
        # Aft for the starboard-bow one, whose forward end is over the castle's flank.
        forward = not (sx > 0 and yc < 0)
        cx = (lo.x + hi.x) / 2
        run = s / STAIR_K
        if forward:
            flight(p, col, (cx, lo.y - run), (0.0, run), hi.x - lo.x, 0.0, s, "scrap", rng,
                   rails=("-x", "+x"), base=0.0)
            far = hi.y
        else:
            flight(p, col, (cx, hi.y + run), (0.0, -run), hi.x - lo.x, 0.0, s, "scrap", rng,
                   rails=("-x", "+x"), base=0.0)
            far = lo.y
        railing(p, col, (lo.x, far), (hi.x, far), s, "rope", rng)
        # Posts on the walkway carry the skywalk the removed rakes used to.
        bracket_bottom = sc.DECK + zc - 0.22 - 0.13
        for y in (yc - 2.9, yc + 2.9):
            for x in (sx * 11.8, sx * 13.2, sx * 14.8):
                if not in_house(x, y):
                    post(p, col, x, y, 0.0, bracket_bottom, rng)
        kit_object(p, "Mesh_SkyCity_SkywalkStair%02d" % i, vis)


def stern_supports(kit, vis, col):
    so = stern_offset()
    y = sc.BLOCK_Y1 - 2.0 + so.y
    underside = sc.BLOCK_STEPS[0][0] + so.z
    p = Part(kit)
    for sx in (-1, 1):
        sw.pole(p, (sx * 7.6, y, 0.0), (sx * 7.6, y, underside), 0.28, sw.RUST, seg=10)
        p.cyl((sx * 7.6, y, 0.08), 0.5, 0.16, seg=10, mat=sw.RUST_DEEP)
        for z in (1.2, underside * 0.5, underside - 1.0):
            sw.lashing(p, (sx * 7.6, y, z), 0.3, turns=4)
        col.box((sx * 7.6 - 0.3, y - 0.3, 0.0), (sx * 7.6 + 0.3, y + 0.3, underside))
    sw.strut(p, (-7.9, y, underside - 0.3), (7.9, y, underside - 0.3), 0.4, 0.5, sw.RUST_DEEP)
    for a, b in (((-7.4, 7.2), (7.4, underside - 0.6)), ((7.4, 7.2), (-7.4, underside - 0.6))):
        sw.rope(p, (a[0], y, a[1]), (b[0], y, b[1]), sag=0.1, r=0.045, mat=sw.DARK)
    kit_object(p, "Mesh_SkyCity_SternSupports", vis)


# ===========================================================================
# Crown gantry: galleries, balconies, ramp to the stern terrace
# ===========================================================================

def gallery_collision(col, cx, cy, body_r, rail_r, top_z):
    """A round gallery at the gantry's walking height: floor disc, solid tower,
    and a railing ring with the gantry's two openings left out."""
    z = GANTRY_WALK
    col.prism(cx, cy, rail_r + 0.05, z - DECK_T, z, n=24)
    col.prism(cx, cy, body_r, z, top_z, n=16)
    n = 32
    for i in range(n):
        a0, a1 = math.tau * i / n, math.tau * (i + 1) / n
        xm = rail_r * math.cos((a0 + a1) / 2)
        if abs(xm) < GALLERY_OPENING:
            continue
        col.sector(cx, cy, rail_r - 0.05, rail_r + 0.05, a0, a1, z, z + RAIL_H)


def gantry_walks(kit, vis, col):
    z = GANTRY_WALK
    ops = gantry_openings()
    O = bpy.data.objects

    # The gantry deck itself and its (opened) railings.
    col.box((-GANTRY_HW, GANTRY_Y0, z - DECK_T), (GANTRY_HW, GANTRY_Y1, z))
    for sx in (-1, 1):
        cur = GANTRY_Y0
        for a, b in sorted(ops[sx]):
            col.box((sx * GANTRY_RAIL_X - 0.05, cur, z), (sx * GANTRY_RAIL_X + 0.05, a, z + RAIL_H))
            cur = b
        col.box((sx * GANTRY_RAIL_X - 0.05, cur, z), (sx * GANTRY_RAIL_X + 0.05, GANTRY_Y1, z + RAIL_H))

    # The towers' own galleries, widened in surgery.
    for name, spec in TOWERS.items():
        o = O[name]
        lo, hi = world_bounds(o)
        gallery_collision(col, o.location.x, o.location.y, spec["body_r"], spec["rail_r"], hi.z)
        for glo, ghi in gallery_loose(o, spec):
            col.box((glo.x, glo.y, z), (ghi.x, ghi.y, max(ghi.z, z + 0.1)))

    rng = rng_for("gantry")
    p = Part(kit)

    # A gallery for the dish, in the tribe's kit: it had a plinth and nothing
    # round it, standing across the whole gantry.
    dish = O[DISH]
    dlo, dhi = world_bounds(dish)
    cx, cy = dish.location.x, dish.location.y
    br, rr = DISH_GALLERY["body_r"], DISH_GALLERY["rail_r"]
    gallery_collision(col, cx, cy, br, rr, dhi.z)
    segs = 20
    c = Vector((cx, cy, 0.0))
    ri, ro = br - 0.05, rr + 0.05
    for i in range(segs):
        a0, a1 = math.tau * i / segs, math.tau * (i + 1) / segs
        mid = (a0 + a1) / 2
        radial = Vector((math.cos(mid), math.sin(mid), 0.0))
        chord = 2 * ro * math.sin((a1 - a0) / 2)
        sw.obox(p, c + radial * ((ri + ro) / 2) + Vector((0, 0, z - 0.03)), (chord + 0.03, ro - ri, 0.06 - 0.01 * (i % 2)),
                radial, rng.choice((sw.RUST, sw.HULLRUST, sw.RUST_PALE, sw.PLY)))
        if abs(rr * math.cos(mid)) >= GALLERY_OPENING:
            a = c + Vector((math.cos(a0), math.sin(a0), 0.0)) * rr
            b = c + Vector((math.cos(a1), math.sin(a1), 0.0)) * rr
            sw.railing(p, (a.x, a.y), (b.x, b.y), z, "pipe", rng)
            # Knee braces back to the gantry's flank, where the ring overhangs it.
            if abs(math.cos(mid)) > 0.45:
                under = c + radial * (rr - 0.3)
                sw.knee(p, (under.x, under.y, z - 0.08),
                        (math.copysign(GANTRY_HW, math.cos(mid)), under.y, z - 1.3), sw.RUST_DEEP, w=0.1)

    # Balconies off the gantry: two small decks each, stepped out over the bags.
    stringer_top = sc.CAGE_Z + sc.CAGE_R * math.sin(math.radians(60)) + 0.16
    for sx, y0, y1, reach in BALCONIES:
        mid = GANTRY_HW + (reach - GANTRY_HW) * 0.6
        inner = sorted((sx * GANTRY_HW, sx * mid))
        outer = sorted((sx * mid, sx * reach))
        oy0, oy1 = y0 + 0.4, y1 - 0.3
        floor(p, col, (inner[0], y0), (inner[1], y1), z, "planks", rng)
        floor(p, col, (outer[0], oy0), (outer[1], oy1), z, "plate", rng)
        railing(p, col, (sx * (reach - 0.05), oy0), (sx * (reach - 0.05), oy1), z, "rope", rng)
        railing(p, col, (sx * mid, oy0 - 0.05), (sx * (reach - 0.05), oy0 - 0.05), z, "rope", rng)
        railing(p, col, (sx * mid, oy1 + 0.05), (sx * (reach - 0.05), oy1 + 0.05), z, "rope", rng)
        railing(p, col, (sx * GANTRY_HW, y0 - 0.05), (sx * mid, y0 - 0.05), z, "net", rng)
        railing(p, col, (sx * GANTRY_HW, y1 + 0.05), (sx * mid, y1 + 0.05), z, "net", rng)
        railing(p, col, (sx * mid, y0), (sx * mid, oy0), z, "net", rng)
        railing(p, col, (sx * mid, oy1), (sx * mid, y1), z, "net", rng)
        for yy in (y0 + 0.6, y1 - 0.6):
            sw.knee(p, (sx * (reach - 0.4), yy, z - 0.2), (sx * 4.2, yy, stringer_top), sw.TIMBER)
        sw.bracket_lantern(p, (sx * (reach - 0.05), (oy0 + oy1) / 2, z + RAIL_H + 0.05), (sx, 0), rng)

    # Patches over the worst of the gantry deck, clear of the galleries.
    rings = [(O[n].location.y, s["rail_r"]) for n, s in TOWERS.items()] + [(cy, rr)]
    y = GANTRY_Y0 + 1.5
    while y < RAMP_Y0 - 2.0:
        run = rng.uniform(0.8, 2.2)
        if all(abs(y - yc) > r + 0.3 and abs(y + run - yc) > r + 0.3 and not (yc - r < y < yc + r)
               for yc, r in rings) and rng.random() < 0.85:
            x0 = rng.uniform(-GANTRY_HW + 0.1, 0.0)
            x1 = rng.uniform(0.1, GANTRY_HW - 0.1)
            p.slab((x0, y, z - 0.012), (x1, y + run, z + 0.012),
                   rng.choice((sw.PLY, sw.TIMBER, sw.RUST_DEEP, sw.RUST_PALE, sw.YELLOW, sw.RUST)))
        y += run + rng.uniform(0.3, 2.5)

    # Ramp up to the stern tower's terrace, inside the gantry's width.
    so = stern_offset()
    terrace = sc.BLOCK_STEPS[0][1] + 0.24 + so.z
    ramp_run = (terrace - z) / STAIR_K
    flight(p, col, (0.0, RAMP_Y0), (0.0, ramp_run), 2 * GANTRY_HW - 0.4, z, terrace, "scrap", rng,
           rails=("-x", "+x"))

    kit_object(p, "Mesh_SkyCity_GantryWalks", vis)


def stern_terrace(kit, vis, col):
    """Terrace railings and collision for the stern tower, bridges and pods."""
    so = stern_offset()
    (b_lo, b_hi, b_w, b_y0, b_y1, _), (u_lo, u_hi, u_w, u_y0, u_y1, _) = sc.BLOCK_STEPS
    terrace = b_hi + 0.24 + so.z
    rim = (-b_w - 0.5, b_y0 - 0.5 + so.y, b_w + 0.5, b_y1 + 0.5 + so.y)
    rng = rng_for("stern terrace")
    p = Part(kit)
    col.box((-b_w, b_y0 + so.y, b_lo + so.z), (b_w, b_y1 + so.y, b_hi + so.z))
    col.box((rim[0], rim[1], b_hi + so.z), (rim[2], rim[3], terrace))
    col.box((-u_w, u_y0 + so.y, terrace), (u_w, u_y1 + so.y, u_hi + so.z))
    stern = bpy.data.objects["Mesh_SkyCity_SternBlock"]
    cap = [stern.matrix_world @ v.co for v in stern.data.vertices]
    cap = [q for q in cap if q.y > b_y1 + so.y + 0.1 and q.z < u_hi + so.z]
    if cap:
        col.box((min(q.x for q in cap), b_y1 + so.y, min(q.z for q in cap)),
                (max(q.x for q in cap), max(q.y for q in cap), max(q.z for q in cap)))

    bridges = [o for o in city_objects("Mesh_SkyCity_Walk03_Catwalk_Straight.")
               if o.location.z > 15.0]
    gaps = []
    for o in bridges:
        lo, hi = world_bounds(o)
        s = o.location.z
        col.box((lo.x, lo.y, s - DECK_T), (hi.x, hi.y, s))
        # The user's bridge runs 2.8 m inboard across the terrace walkway before
        # it meets the tower wall. Its rails only collide outboard of the rim,
        # or walking round the terrace means climbing over them.
        rim_edge = rim[2] if lo.x > 0 else rim[0]
        ra, rb = sorted((rim_edge, hi.x if lo.x > 0 else lo.x))
        for y in (lo.y + 0.1, hi.y - 0.1):
            col.box((ra, y - 0.05, s), (rb, y + 0.05, s + RAIL_H))
        # The bridges sit 0.11 m below the terrace. A capsule does not reliably
        # climb even that, so a short wedge meets the rim edge.
        col.wedge_x(lo.y, hi.y, rim_edge + math.copysign(0.6, lo.x), s, rim_edge, terrace)
        gaps.append((math.copysign(1, lo.x), lo.y - 0.1, hi.y + 0.1))
    for sx in (-1, 1):
        cur = rim[1]
        for a, b in sorted((a, b) for s, a, b in gaps if s == sx) + [(rim[3], rim[3])]:
            railing(p, col, (sx * (rim[2] - 0.05), cur), (sx * (rim[2] - 0.05), max(cur, a)), terrace,
                    rng.choice(sw.RAIL_STYLES), rng)
            cur = max(cur, b)
    tank = [o for o in city_objects("Mesh_SkyCity_Home16_Shanty_Water.") if o.location.z > 15.0]
    blocked = []
    for o in tank:
        lo, hi = world_bounds(o)
        blocked.append((lo.x - 0.2, hi.x + 0.2))
    cur = rim[0]
    ramp_gap = (-GANTRY_HW + 0.05, GANTRY_HW - 0.05)
    for a, b in sorted(blocked + [ramp_gap]) + [(rim[2], rim[2])]:
        railing(p, col, (cur, rim[1] + 0.05), (max(cur, a), rim[1] + 0.05), terrace, "rope", rng)
        cur = max(cur, b)

    for o in city_objects("Mesh_SkyCity_Beacon_SensorCupola_Lantern."):
        lo, hi = world_bounds(o)
        if lo.z < 15.0 or o.location.y < 40.0:
            continue
        cx, cy = o.location.x, o.location.y
        pod_lo = min(world_bounds(d)[0].z for d in city_objects("Mesh_SkyCity_Dome_SensorCupola_Dome.")
                     if abs(d.location.x - cx) < 1.0 and abs(d.location.y - cy) < 1.0)
        col.box((cx - 3.6, cy - 3.6, pod_lo), (cx + 3.6, cy + 3.6, hi.z))
    kit_object(p, "Mesh_SkyCity_SternTerrace", vis)


def fixed_collision(col):
    """Deck, keel, houses, deck floodlights and the user's stern piers."""
    off = castle_offset()
    y0 = sc.CASTLE_STEPS[0][3] + off.y
    col.box((-EXT_IN, y0, -0.3), (EXT_IN, EXT_Y1, 0.0))
    col.box((-sc.SIDE_OUT, EXT_Y1, -0.3), (sc.SIDE_OUT, sc.STERN + 4.4, 0.0))
    col.box((-sc.KEEL_HW, sc.BOW, sc.KEEL_BOT), (sc.KEEL_HW, sc.STERN, -0.3))

    for corners in house_solids():
        col.hull(corners)

    for o in city_objects("Mesh_SkyCity_Walk03_Catwalk_Straight."):
        if o.location.z > 1.0:
            continue
        lo, hi = world_bounds(o)
        s = o.location.z
        col.box((lo.x, lo.y, s - DECK_T), (hi.x, hi.y, s))
        # The user's stern piers sit 0.4 m below the deck: a ramp, not a step.
        inner = math.copysign(sc.SIDE_OUT, lo.x)
        col.wedge_x(lo.y, hi.y, inner + math.copysign(1.0, lo.x), s, inner, 0.0)

    for o in city_objects("Mesh_SkyCity_Under0"):
        lo, hi = world_bounds(o)
        s = o.location.z
        col.box((lo.x, lo.y, s - DECK_T), (hi.x, hi.y, s))


# ===========================================================================
# Verification: walk the routes against the collision set
# ===========================================================================

def boxed_parts():
    """World boxes of the kit parts in `BOX_COLLIDED`."""
    boxes = []
    for prefix, min_height in BOX_COLLIDED:
        for o in city_objects(prefix):
            lo, hi = world_bounds(o)
            if hi.z - lo.z >= min_height:
                boxes.append((lo, hi))
    return boxes


def collision_bvh():
    """Exactly what Unity will collide with once the builder consumes COL_*:
    the COL_ primitives, `BOX_COLLIDED` by bounds, the gas bags by mesh (the
    Convex rule) and `MESH_COLLIDED`."""
    verts, polys = [], []

    def add(obj):
        mw = obj.matrix_world
        base = len(verts)
        verts.extend(mw @ v.co for v in obj.data.vertices)
        polys.extend([base + i for i in p.vertices] for p in obj.data.polygons)

    def add_box(lo, hi):
        base = len(verts)
        for x in (lo.x, hi.x):
            for y in (lo.y, hi.y):
                for zz in (lo.z, hi.z):
                    verts.append(Vector((x, y, zz)))
        for f in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
            polys.append([base + i for i in f])

    for o in bpy.data.collections[COL_COLL].objects:
        add(o)
    for lo, hi in boxed_parts():
        add_box(lo, hi)
    for o in city_objects("Mesh_SkyCity_Bag"):
        add(o)
    for name in MESH_COLLIDED:
        add(bpy.data.objects[name])
    return BVHTree.FromPolygons(verts, polys)


class Solids:
    """Point-in-solid test against every convex collision primitive.

    Exists because rays cannot see a solid they start inside. The first version
    of `verify` passed a route walked straight through a house and straight
    through the gantry's lantern tower: the floor ray found the solid's bottom
    face, and the headroom and side rays found nothing within reach because they
    began inside. Each primitive is tested against its own face planes - a
    nearest-face test on the combined BVH gives the wrong sign wherever two
    primitives overlap.
    """
    CELL = 4.0

    def __init__(self):
        self.prims, self.grid = [], {}
        for o in bpy.data.collections[COL_COLL].objects:
            bm = bmesh.new()
            bm.from_mesh(o.data)
            bm.transform(o.matrix_world)
            bm.normal_update()
            for group, lo, hi in islands(bm):
                faces = {f for v in group for f in v.link_faces}
                c = sum((v.co for v in group), Vector()) / len(group)
                planes = []
                for f in faces:
                    n = f.normal.copy()
                    q = f.calc_center_median().copy()
                    if (c - q).dot(n) > 0:      # normals point out of the primitive
                        n = -n
                    planes.append((q, n))
                self.add(planes, lo, hi)
            bm.free()
        for lo, hi in boxed_parts():
            self.add([(lo, Vector((-1, 0, 0))), (lo, Vector((0, -1, 0))), (lo, Vector((0, 0, -1))),
                      (hi, Vector((1, 0, 0))), (hi, Vector((0, 1, 0))), (hi, Vector((0, 0, 1)))], lo, hi)

    def _cells(self, lo, hi):
        c = self.CELL
        for i in range(int(math.floor(lo.x / c)), int(math.floor(hi.x / c)) + 1):
            for j in range(int(math.floor(lo.y / c)), int(math.floor(hi.y / c)) + 1):
                yield i, j

    def add(self, planes, lo, hi):
        self.prims.append((planes, lo.copy(), hi.copy()))
        for cell in self._cells(lo, hi):
            self.grid.setdefault(cell, []).append(len(self.prims) - 1)

    def contains(self, q, eps=0.02):
        cell = (int(math.floor(q.x / self.CELL)), int(math.floor(q.y / self.CELL)))
        for idx in self.grid.get(cell, ()):
            planes, lo, hi = self.prims[idx]
            if not (lo.x - eps <= q.x <= hi.x + eps and lo.y - eps <= q.y <= hi.y + eps
                    and lo.z - eps <= q.z <= hi.z + eps):
                continue
            if all((q - c).dot(n) < -eps for c, n in planes):
                return True
        return False


def circle(cx, cy, r, z, n=32):
    return [(cx + r * math.sin(math.tau * i / n), cy - r * math.cos(math.tau * i / n), z) for i in range(n + 1)]


def standable(q, solids, bvh):
    """Could the capsule stand at deck point q: floor under it, nothing within its
    radius, nothing over its head?"""
    up = Vector((0, 0, 1))
    hit, _, _, _ = bvh.ray_cast(q + up * 1.0, Vector((0, 0, -1)), 1.4)
    if hit is None or abs(hit.z - q.z) > 0.3:
        return False
    for h in (0.5, 1.0, 1.6):
        if solids.contains(q + up * h):
            return False
    # Rays, not probe points: a probe on the capsule's rim steps straight over a
    # 0.1 m railing wall standing between it and the centre.
    for i in range(8):
        d = Vector((math.cos(math.tau * i / 8), math.sin(math.tau * i / 8), 0))
        for h in (0.35, 1.2, 1.8):
            if bvh.ray_cast(hit + up * h, d, PLAYER_R)[0] is not None:
                return False
    h2, _, _, _ = bvh.ray_cast(hit + up * 0.05, up, HEADROOM)
    return h2 is None


def found_lines(sx, y0, y1, outer_at, inner, solids, bvh, step=0.25, min_run=3.0):
    """Walking lines down one side, found rather than hand-written.

    Every `step` metres from y0 to y1, the standable point closest to
    `outer_at(y)` (an |x|, or None where there is nothing to walk) and no further
    in than `inner`. Each new point joins the line only if the leg to it passes
    the same `check_segment` the verifier uses - directly, or round one corner.
    Where no leg passes - a sail slot, a house, a crane - the line breaks, and
    every unbroken stretch longer than `min_run` is kept. So these lines pass by
    construction; what they tell you is coverage: (lines, metres covered).
    """
    def leg_ok(a, b):
        return not check_segment(Vector(a), Vector(b), solids, bvh)
    runs, line = [], []
    y = y0
    while y <= y1:
        outer = outer_at(y)
        point = None
        if outer is not None:
            x = outer
            while x >= inner and point is None:
                if standable(Vector((sx * x, y, 0.0)), solids, bvh):
                    point = (sx * x, y, 0.0)
                x -= 0.1
        joined = False
        if point is not None and line:
            prev = line[-1]
            if leg_ok(prev, point):
                line.append(point)
                joined = True
            else:
                for corner in ((point[0], prev[1], 0.0), (prev[0], point[1], 0.0)):
                    if leg_ok(prev, corner) and leg_ok(corner, point):
                        line += [corner, point]
                        joined = True
                        break
        if not joined:
            runs.append(line)
            line = [point] if point is not None else []
        y += step
    runs.append(line)
    runs = [ln for ln in runs if len(ln) > 1 and abs(ln[-1][1] - ln[0][1]) >= min_run]
    return runs, sum(abs(ln[-1][1] - ln[0][1]) for ln in runs)


def walkway_routes(solids, bvh):
    """The walkways' and lanes' walking lines (see `found_lines`), and a line per
    side saying how much of each they cover - so a blockage shows up as a
    shorter walkway rather than as silence.

    Lanes are found too: the lane is 2.8 m between its inner railing and the
    houses, but the cage's ring frames curve down over its inner edge to head
    height and some houses reach into its outer edge, so its clear line wanders.
    """
    r, cover = {}, []
    lane_start = sc.CASTLE_Y1 + castle_offset().y + 0.6
    for sx, side in ((1, "starboard"), (-1, "port")):
        plan = platform_plan(sx)

        def platform_edge(y, plan=plan):
            seg = next((q for q in plan if q[0] - 1e-3 <= y <= q[1] + 1e-3), None)
            return None if seg is None else seg[2] - 0.05 - WALK_MARGIN
        runs, walked = found_lines(sx, plan[0][0] + 0.6, plan[-1][1] - 0.5, platform_edge, EXT_IN - 2.0,
                                   solids, bvh)
        for k, ln in enumerate(runs, start=1):
            r["walkway %s, stretch %d" % (side, k)] = ln
        cover.append("walkway %s: %d stretches, %.0f of %.0f m"
                     % (side, len(runs), walked, plan[-1][1] - plan[0][0]))
        lane_end = EXT_Y1 - 1.0
        runs, walked = found_lines(sx, lane_start, lane_end, lambda y: sc.LANE_OUT - 0.4,
                                   sc.SIDE_IN + 0.55, solids, bvh)
        for k, ln in enumerate(runs, start=1):
            r["lane %s, stretch %d" % (side, k)] = ln
        cover.append("lane %s: %d stretches, %.0f of %.0f m" % (side, len(runs), walked, lane_end - lane_start))
    return r, cover


def routes():
    """Named polylines a player must be able to walk, (x, y, surface z) each.

    The walkways' own lines are not here: `walkway_routes` finds them against
    the collision, so it needs the checker `run` builds."""
    off = castle_offset().y
    L = core_levels()
    R = roof_stair()
    f1, f2 = L["f1"], L["f2"]
    so = stern_offset()
    terrace = sc.BLOCK_STEPS[0][1] + 0.24 + so.z
    ramp_top = RAMP_Y0 + (terrace - GANTRY_WALK) / STAIR_K
    gw = GANTRY_WALK
    O = bpy.data.objects

    def cy(y):
        return y + off

    r = {}
    r["into castle, port lane to port flank door"] = [
        (-6.2, cy(-41.0), 0.0), (-6.2, cy(-45.5), 0.0), (-6.6, cy(-56.1), 0.0),
        (-10.4, cy(-56.1), 0.0), (-12.2, cy(-56.1), 0.0)]
    r["into castle, starboard corridor to flank door"] = [
        (7.7, cy(-41.0), 0.0), (7.7, cy(-56.1), 0.0), (12.2, cy(-56.1), 0.0)]
    stair_x = sum(INNER_STAIR_X) / 2
    stair_top = INNER_STAIR_FOOT_Y - T1 / STAIR_K
    lower_c, upper_c = sum(CORE_LOWER_Y) / 2, sum(CORE_UPPER_Y) / 2
    turn_x = F1_CORE_FOOT_X + 0.6
    r["castle climb, ground floor to crown gantry"] = [
        (stair_x, cy(-41.0), 0.0), (stair_x, cy(INNER_STAIR_FOOT_Y + 0.05), 0.0),
        (stair_x, cy(stair_top - 0.05), T1), (stair_x, cy(-55.95), T1), (turn_x, cy(-55.95), T1),
        (turn_x, cy(lower_c), T1), (f1["foot_x"] + 0.05, cy(lower_c), T1),
        (f1["top_x"] - 0.05, cy(lower_c), f1["mid_z"]), (f1["top_x"] - 0.65, cy(lower_c), f1["mid_z"]),
        (f1["top_x"] - 0.65, cy(upper_c), f1["mid_z"]), (f1["top_x"] - 0.05, cy(upper_c), f1["mid_z"]),
        (f1["foot_x"] + 0.05, cy(upper_c), T2), (turn_x, cy(upper_c), T2), (turn_x, cy(lower_c), T2),
        (f2["foot_x"] + 0.05, cy(lower_c), T2), (f2["top_x"] - 0.05, cy(lower_c), f2["mid_z"]),
        (f2["top_x"] - 0.65, cy(lower_c), f2["mid_z"]), (f2["top_x"] - 0.65, cy(upper_c), f2["mid_z"]),
        (f2["top_x"] - 0.05, cy(upper_c), f2["mid_z"]), (f2["foot_x"] + 0.05, cy(upper_c), T3),
        (turn_x, cy(upper_c), T3), (turn_x, cy(-47.95), T3), (sum(ROOF_STAIR_X) / 2, cy(-47.95), T3),
        (sum(ROOF_STAIR_X) / 2, cy(ROOF_STAIR_FOOT_Y + 0.05), T3),
        (sum(ROOF_STAIR_X) / 2, cy(R["top_y"] - 0.05), gw), (sum(ROOF_STAIR_X) / 2, cy(R["landing"][0] + 0.65), gw),
        (0.0, cy(R["landing"][0] + 0.65), gw), (0.0, GANTRY_Y0 + 2.0, gw)]
    r["first floor, both doors onto the terrace"] = [
        (turn_x, cy(-52.3), T1), (-5.5, cy(-52.3), T1), (-5.5, cy(-55.1), T1), (-8.3, cy(-55.1), T1)]
    r["first floor starboard door"] = [(stair_x, cy(-55.95), T1), (stair_x, cy(-56.15), T1),
                                       (8.3, cy(-56.15), T1), (8.3, cy(-45.3), T1)]
    r["second floor, both doors onto the terrace"] = [
        (6.4, cy(-52.2), T2), (turn_x, cy(-52.2), T2), (-6.4, cy(-52.2), T2)]
    # The crown gantry: the spine, and a lap of every gallery.
    towers = [(O[n].location.y, (gallery_inner_r(O[n], s) + s["rail_r"]) / 2) for n, s in TOWERS.items()]
    dish = O[DISH]
    towers.append((dish.location.y, (DISH_GALLERY["body_r"] + DISH_GALLERY["rail_r"]) / 2))
    towers.sort()
    prev = GANTRY_Y0 + 1.0
    for yc, rm in towers:
        r["gantry to the gallery at y %.0f" % yc] = [(0.0, prev, gw), (0.0, yc - rm, gw)]
        r["lap of the gallery at y %.0f" % yc] = circle(0.0, yc, rm, gw)
        prev = yc + rm
    r["gantry to the ramp"] = [(0.0, prev, gw), (0.0, RAMP_Y0 - 0.2, gw)]
    for sx, y0, y1, reach in BALCONIES:
        r["balcony %s" % ("starboard" if sx > 0 else "port")] = [
            (0.0, (y0 + y1) / 2, gw), (sx * (reach - 0.6), (y0 + y1) / 2, gw)]
    r["ramp to stern terrace and pods"] = [
        (0.0, RAMP_Y0 - 0.5, gw), (0.0, RAMP_Y0 + 0.05, gw), (0.0, ramp_top, terrace),
        (0.0, 50.4, terrace), (-7.4, 50.4, terrace), (-7.4, 54.75, terrace),
        (-12.0, 54.75, 21.297)]
    for i, (yc, sx, zc) in enumerate(sc.SKYWALKS, start=1):
        walk = O["Mesh_SkyCity_Walk%02d_Catwalk_Straight" % i]
        lo, hi = world_bounds(walk)
        s = walk.location.z
        x = (lo.x + hi.x) / 2
        if not (sx > 0 and yc < 0):
            r["skywalk %d" % i] = [(x, lo.y - s / STAIR_K - 0.6, 0.0), (x, lo.y - s / STAIR_K + 0.05, 0.0),
                                   (x, lo.y, s), (x, hi.y - 0.8, s)]
        else:
            r["skywalk %d" % i] = [(x, hi.y + s / STAIR_K + 0.6, 0.0), (x, hi.y + s / STAIR_K - 0.05, 0.0),
                                   (x, hi.y, s), (x, lo.y + 0.8, s)]
    return r


def check_segment(a, b, solids, bvh, step=0.25):
    """Walk one leg: floor under the feet, headroom, side clearance, no solid."""
    up, down = Vector((0, 0, 1)), Vector((0, 0, -1))
    fails = []
    length = (b.xy - a.xy).length
    n = max(1, int(length / step))
    d = b - a
    side = Vector((-d.y, d.x, 0.0))
    side = side.normalized() if side.length > 1e-6 else Vector((1, 0, 0))
    for i in range(n + 1):
        q = a.lerp(b, i / n)
        if any(solids.contains(q + up * h) for h in (0.5, 1.0, 1.6)):
            fails.append(("in solid", q, None))
            continue
        hit, nrm, _, _ = bvh.ray_cast(q + up * 1.0, down, 1.7)
        if hit is None or abs(hit.z - q.z) > 0.3:
            fails.append(("no floor", q, None if hit is None else round(hit.z, 2)))
            continue
        if nrm.z < math.cos(math.radians(35)):
            fails.append(("too steep", q, round(math.degrees(math.acos(max(-1, min(1, nrm.z)))), 1)))
        if bvh.ray_cast(hit + up * 0.05, up, HEADROOM)[0] is not None:
            fails.append(("headroom", q, None))
        for sd in (side, -side):
            for zz in (0.35, 1.2, 1.8):
                if bvh.ray_cast(hit + up * zz, sd, PLAYER_R - 0.05)[0] is not None:
                    fails.append(("pinched", q, round(zz, 2)))
                    break
        # Ahead, to the next sample: a railing crossed head-on is thinner than
        # the step, so neither the solid test nor the side rays ever land on it.
        if i < n and length > 1e-6:
            ahead = Vector((d.x, d.y, 0.0)).normalized()
            for zz in (0.35, 1.2):
                if bvh.ray_cast(hit + up * zz, ahead, length / n)[0] is not None:
                    fails.append(("walled", q, round(zz, 2)))
                    break
    return fails


def verify(step=0.25, route_set=None, solids=None, bvh=None):
    """Walk every route: floor under the feet, headroom, and side clearance."""
    bvh = bvh or collision_bvh()
    solids = solids or Solids()
    report = {}
    for name, pts in (route_set or routes()).items():
        fails = []
        for a, b in zip(pts, pts[1:]):
            fails += check_segment(Vector(a), Vector(b), solids, bvh, step)
        report[name] = fails
    return report


def print_verify(report):
    ok = 0
    for name, fails in report.items():
        if not fails:
            ok += 1
            print("  PASS  %s" % name)
            continue
        kinds = {}
        for kind, q, info in fails:
            kinds.setdefault(kind, []).append((round(q.x, 2), round(q.y, 2), round(q.z, 2), info))
        print("  FAIL  %s" % name)
        for kind, items in kinds.items():
            print("        %-9s x%-3d first at %s" % (kind, len(items), items[:3]))
    print("  %d of %d routes pass" % (ok, len(report)))
    return ok == len(report)


# ===========================================================================

def run(discard_edits=False):
    """`discard_edits=True` rebuilds pieces this pass made even if they were edited
    by hand - only when the user has said so. Edits to their own objects always
    stop the run."""
    if not bpy.data.filepath.lower().endswith("sky_city.blend"):
        raise RuntimeError("Open sky_city.blend first - this pass is written against it.")
    refuse_if_hand_edited(discard_edits)
    mats = bl.link_materials(sc.MATS)
    kit = bl.link_materials(sw.MATS)
    vis = ensure_collection(VIS_COLL)
    col_coll = ensure_collection(COL_COLL, hide_render=True)
    clear_owned()
    src_coll = ensure_collection(SRC_COLL, hide_render=True, exclude=True)

    report = {}
    surgery(report)
    col = Col()
    import sky_city_street as street     # imports this module; deferred to break the cycle
    street_changed, ladders, street_routes = street.build(vis, col, report)
    castle_hollow(mats, vis, col)
    castle_climb(kit, vis, col)
    walkways(kit, vis, col)
    report["cranes, goods and castle stock placed"] = loading_bays(vis, col, library_sources(src_coll))
    skywalk_access(kit, vis, col)
    stern_supports(kit, vis, col)
    gantry_walks(kit, vis, col)
    stern_terrace(kit, vis, col)
    fixed_collision(col)
    col.finish("COL_SkyCity", col_coll)

    # matrix_world is stale until the view layer updates. Hashing before this
    # records placed objects at the origin, and the NEXT run then refuses to
    # rebuild them as "hand-edited" - which is how this line came to exist.
    bpy.context.view_layer.update()
    for name in MODIFIED + tuple(street_changed):
        o = bpy.data.objects[name]
        o["sky_city_hash"] = geom_hash(o)
    for o in bpy.data.objects:
        if o.get("sky_city_pass") == PASS and o.type == 'MESH':
            o["sky_city_hash"] = geom_hash(o)

    print("sky_city_traversal:")
    for k, v in report.items():
        print("  %-48s %s" % (k, v))
    solids, bvh = Solids(), collision_bvh()
    walk, cover = walkway_routes(solids, bvh)
    result = verify(route_set={**routes(), **walk, **street_routes}, solids=solids, bvh=bvh)
    result.update(street.ladder_checks(ladders, solids, bvh))
    for line in cover:
        print("  %s" % line)
    return print_verify(result)
