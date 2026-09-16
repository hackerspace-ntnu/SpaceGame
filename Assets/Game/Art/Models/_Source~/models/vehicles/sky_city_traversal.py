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
  vertical route here is therefore a stair whose COLLISION is a smooth wedge at
  27.2 degrees (`STAIR_K`); the treads are drawn on top of it for the eye only.
- A jump reaches ~2.5 m; fall damage starts landing faster than 5 m/s, a ~1.3 m
  drop. So every edge a player can walk to and fall more than that from is railed.

## What it builds

A continuous railed walkway outboard of the houses; a hollow bow castle with a
ground floor and first floor you can enter and an inside stair between them; a
stair tower up its starboard flank to every terrace, the roof and the crown
gantry; walk-around loops past the gantry's lantern and dome/dish, two lookout
balconies and a ramp up to the stern tower's terrace; stairs up to all four
hanging skywalks; columns under the raised stern tower. And collision for all of
it, plus the houses, catwalks and pods, as `COL_*` meshes.

## Collision is authored here, not guessed in Unity

`SkyCityBuilder` used to fit boxes from renderer bounds. On this file that fails
three ways, all silently: the houses share a mesh with struts running 5 m under
the deck, so a bounds box drops a 12 m wall across the promenade; the kit
catwalks carry 1.1 m rails, and `StaticPropBuilder.AddBox(surfaceOnly)` puts the
slab at `bounds.max.y` - the rail top - so the player walks on air above every
bridge; and the pods' galleries are ~0.95 m wide against a 1.0 m capsule. So
every surface a player stands on or bumps into is a `COL_*` primitive in
`Coll_SkyCity_Collision`, each one an island of 8 (box) or 6 (wedge) vertices,
and `verify()` walks the routes against exactly that set before anyone opens
Unity.

## What it changes that was already there

Only what the walkways cannot exist without, each backed up as a fake-user mesh
named `<object>__pre_traversal`, and each checked to be byte-identical to its
generator output before the first run (it was, on 2026-09-16):

- `BowCastle` - rebuilt hollow, same outside.
- `Cage` - the ring at y = -50 and its bay braces removed, and the stringers cut
  back, because the castle was moved over them and they ran through its rooms.
- `Decks` - the eight skywalk rakes removed; they crossed the new walkway at
  knee height. Posts on the walkway carry the skywalks instead.
- `Climbs` - ladders that stood inside the new routes, or led nowhere after the
  stern tower was raised, removed.
- `Gantry` (hand-edited by the user: a second railing) - openings cut in both
  railings where the loops, lookouts, tower bridge and ramp join it.
- `Rail01..03`, `RailP01..03`, `Boarding01..02` deleted: the rails fenced the
  walkway off from the stern piers, and the boarding ladders stood inside the
  castle and under a gas bag.

## Re-running

Safe while nothing it made or modified has been hand-edited since: every such
object stores an MD5 of its vertices and transform, and `run()` refuses to touch
anything whose hash no longer matches. Modified meshes are always rebuilt from
their backup, so a second run does not cut a second hole.
"""

import array
import hashlib
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
for _p in (HERE, os.path.dirname(os.path.dirname(HERE))):
    if _p not in sys.path:
        sys.path.insert(0, _p)

import _buildlib as bl  # noqa: E402
import sky_city as sc  # noqa: E402
from _buildlib import Part  # noqa: E402

# ---------------------------------------------------------------------------
# Player metrics - see the module docstring for where these come from.
# ---------------------------------------------------------------------------
PLAYER_R = 0.5
HEADROOM = 2.1              # capsule 2.0 plus the ground probe's reach
STAIR_K = math.tan(math.radians(27.2))   # rise per metre of run, every stair
RAIL_H = sc.RAIL_H          # 1.10, the library's rail height
TREAD = 0.19                # drawn riser height; collision is the wedge

# ---------------------------------------------------------------------------
# Levels. Terraces are the storey LIP tops, not the slab tops: every castle
# storey carries a 0.22 m steel lip, and the player stands on that.
# ---------------------------------------------------------------------------
LIP = 0.22
T1 = sc.CASTLE_STEPS[0][1] + LIP     # 5.42  ground-floor roof
T2 = sc.CASTLE_STEPS[1][1] + LIP     # 10.82 first-floor roof
T3 = sc.CASTLE_STEPS[2][1] + LIP     # 16.72 castle roof
GANTRY_WALK = 19.68         # top face of the crown gantry deck, as measured
GANTRY_HW = sc.GANTRY_HW    # 1.30
GANTRY_RAIL_X = 1.18        # both railings (the port one is the user's)
GANTRY_Y0, GANTRY_Y1 = -52.5, 50.0
DECK_T = 0.12

# ---------------------------------------------------------------------------
# Walkway extension outboard of the houses.
# ---------------------------------------------------------------------------
EXT_IN, EXT_OUT = sc.SIDE_OUT, 17.0
# The user's hanging bow sails cross the extension at x 13.3..17, y -40.7..-38.3.
SAIL_SLOT_Y = (-41.0, -38.1)
SAIL_SLOT_IN = 13.1
EXT_Y1 = 43.0               # stops short of the user's stern piers at 43.4

# ---------------------------------------------------------------------------
# Castle interior, in CASTLE-LOCAL coordinates (the object carries the user's
# offset). Walls are 0.3 m; doors are 2.4 m wide on the ground floor, which is
# more than twice the capsule, because a doorway is where players bunch up.
# ---------------------------------------------------------------------------
WALL = 0.3
GF_DOOR_H, F1_DOOR_H = 3.2, 2.4
GF_CEIL = sc.CASTLE_STEPS[0][1] - 0.22      # 4.98 underside of the first floor
F1_CEIL = sc.CASTLE_STEPS[1][1] - 0.20      # 10.40
# The inside stair runs up the STARBOARD side from the aft wall, rising forward,
# so the starboard promenade lane walks straight onto it. That placement is
# forced. The user's raised prow hull stands 2.8 m tall in the middle of the
# ground floor, and the pod on the castle's front fills the first floor's
# forward centre, so a stair anywhere else either starts against a wall or tops
# out in a pocket with no way on. Here it tops out beside a first-floor door
# that opens directly onto the stair tower's first landing.
INNER_STAIR_X = (5.1, 6.9)
INNER_STAIR_FOOT_Y = -44.8                  # the aft wall's inside face
AFT_DOORS = ((-6.9, -4.5), (-1.5, 1.5), (5.0, 8.2))   # port lane, centre, stair + corridor
GF_FLANK_DOOR = (-57.0, -55.2)
F1_PORT_DOOR = (-56.0, -54.2)
F1_STBD_DOOR = (-56.9, -55.4)               # onto the tower's first landing
F1_WINDOW = ((-50.5, -48.3), (6.2, 7.7))    # (y span, z span)
STAIRWELL_MARGIN = 0.3
SAFE_DROP = 1.3                             # guard a stairwell only where it is deeper

# ---------------------------------------------------------------------------
# Starboard stair tower, castle-local y. Three columns of flights; see
# `castle_tower` for the order they are climbed in.
# ---------------------------------------------------------------------------
COL_A, COL_B, COL_C = (9.5, 11.3), (11.5, 13.3), (13.5, 15.3)
TOWER_FWD = -58.4
TOWER_POSTS_X = (9.4, 11.4, 13.4, 15.4)
TOWER_POSTS_Y = {9.4: (-58.6, -50.0, -45.0), 11.4: (-58.6, -50.0, -45.0),
                 13.4: (-58.6, -50.0, -45.6), 15.4: (-58.6, -50.0, -45.6)}

# ---------------------------------------------------------------------------
# Crown gantry additions, world y.
# ---------------------------------------------------------------------------
BEACON_LOOP = dict(lane=(3.8, 5.8), y=(-45.0, -31.0), conn=1.8)
DOME_LOOP = dict(lane=(4.9, 6.9), y=(21.5, 46.5), conn=1.8)
LOOKOUTS = ((-12.2, -7.8), (7.8, 12.2))
LOOKOUT_OUT = 7.5
RAMP_Y0 = 46.6

PASS = "sky_city_traversal"
VIS_COLL, COL_COLL = "Coll_SkyCity_Traversal", "Coll_SkyCity_Collision"
MODIFIED = ("Mesh_SkyCity_BowCastle", "Mesh_SkyCity_Cage", "Mesh_SkyCity_Decks",
            "Mesh_SkyCity_Climbs", "Mesh_SkyCity_Gantry")
RETIRED = tuple(["Mesh_SkyCity_Rail%02d_Handrail_Straight" % i for i in (1, 2, 3)]
                + ["Mesh_SkyCity_RailP%02d_Handrail_Straight" % i for i in (1, 2, 3)]
                + ["Mesh_SkyCity_Boarding01_Handrail_Ladder",
                   "Mesh_SkyCity_Boarding02_Handrail_Ladder"])

# Deck clutter the user's castle move left inside the castle's new walls:
# a crate in the starboard corridor, a barrel half through the aft wall.
# Castle-local (x, y); set absolutely, so a re-run leaves them where they are.
PROP_MOVES = {
    "Mesh_SkyCity_Stow06_Crate_Stack": (-7.6, -47.8),   # into the west room's aft corner
    "Mesh_SkyCity_Stow02_Barrel_Drum": (-8.2, -43.5),   # just outside the aft wall
}

M = sc   # material indices live on the generator module


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


def refuse_if_hand_edited():
    bad = [o.name for o in bpy.data.objects
           if o.get("sky_city_hash") and o.type == 'MESH'
           and geom_hash(o) != o["sky_city_hash"]]
    if bad:
        raise RuntimeError(
            "Hand-edited since the last traversal pass - refusing to rebuild, "
            "because that would destroy the edits: %s" % ", ".join(bad))


def ensure_collection(name, hide_render=False):
    coll = bpy.data.collections.get(name)
    if coll is None:
        coll = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(coll)
    coll.hide_render = hide_render
    return coll


def clear_owned():
    for o in [o for o in bpy.data.objects if o.get("sky_city_pass") == PASS]:
        me = o.data
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
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


def cut_away(bm, group, y, keep_above):
    """Bisect one island at plane y and drop the side that is not kept, capped."""
    geom = list({e for v in group for e in v.link_edges}) + \
        list({f for v in group for f in v.link_faces}) + list(group)
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(0.0, y, 0.0),
                                 plane_no=(0.0, 1.0, 0.0),
                                 clear_inner=keep_above, clear_outer=not keep_above)
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


def gantry_openings():
    """Railing openings on the crown gantry, per side (+1 starboard, -1 port)."""
    c_off = castle_offset().y
    tower_bridge = (-48.4 + c_off, -46.6 + c_off)
    b, d = BEACON_LOOP, DOME_LOOP
    ops = {1: [], -1: []}
    for sx in (1, -1):
        fwd = tower_bridge if sx > 0 else (b["y"][0], b["y"][0] + b["conn"])
        ops[sx] += [fwd, (b["y"][1] - b["conn"], b["y"][1])]
        ops[sx] += list(LOOKOUTS)
        ops[sx] += [(d["y"][0], d["y"][0] + d["conn"]), (d["y"][1] - d["conn"], d["y"][1])]
        ops[sx] += [(RAMP_Y0, GANTRY_Y1)]
    return ops


def surgery(report):
    O = bpy.data.objects
    castle_aft = sc.CASTLE_Y1 + castle_offset().y

    pristine(O["Mesh_SkyCity_Decks"])
    report["Decks: skywalk rakes removed"] = with_bmesh(
        O["Mesh_SkyCity_Decks"],
        lambda bm: delete_islands(bm, lambda lo, hi: (hi.x - lo.x) > 8 and (hi.z - lo.z) > 5,
                                  8, "skywalk rakes"))

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
            cut_away(bm, g, cut, keep_above=True)
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

    def gantry(bm):
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
            # Posts standing in an opening.
            cut += delete_islands(
                bm, lambda lo, hi: ((hi.y - lo.y) < 0.2 and (hi.z - lo.z) > 0.9
                                    and abs((lo.x + hi.x) / 2 - sx * GANTRY_RAIL_X) < 0.06
                                    and any(a - 0.1 < (lo.y + hi.y) / 2 < b + 0.1 for a, b in spans)),
                None, "gantry posts")
            # Whole bunting strands that sag across an opening.
            strand0 = sc.LADDER_Y[0] - 3.0 + 2.0

            def strand_hit(lo, hi, spans=spans):
                if not (lo.z > 20.4 and (hi.z - lo.z) < 1.5 and 0.8 < (hi.y - lo.y) < 3.0
                        and abs((lo.x + hi.x) / 2 - sx * GANTRY_RAIL_X) < 0.1):
                    return False
                k = math.floor(((lo.y + hi.y) / 2 - strand0) / 9.0)
                s0 = strand0 + 9.0 * k
                return any(s0 < b and s0 + 9.0 > a for a, b in spans)
            cut += delete_islands(bm, strand_hit, None, "bunting")
        return cut
    pristine(O["Mesh_SkyCity_Gantry"])
    report["Gantry: rail spans, posts and bunting opened"] = with_bmesh(O["Mesh_SkyCity_Gantry"], gantry)

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


# ===========================================================================
# Collision primitives
# ===========================================================================

class Col:
    """World-space collision primitives, one island each: 8-vertex boxes and
    6-vertex wedges. `SkyCityBuilder` turns each island into one collider."""

    def __init__(self):
        self.bm = bmesh.new()

    def box(self, lo, hi):
        lo, hi = Vector(lo), Vector(hi)
        a = Vector((min(lo.x, hi.x), min(lo.y, hi.y), min(lo.z, hi.z)))
        b = Vector((max(lo.x, hi.x), max(lo.y, hi.y), max(lo.z, hi.z)))
        if min(b - a) < 1e-4:
            return
        m = Matrix.Translation((a + b) / 2) @ Matrix.Diagonal(b - a).to_4x4()
        bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)

    def wedge(self, x0, x1, y_low, z_low, y_high, z_high):
        """Solid ramp on its base z_low: low edge at y_low, vertical back at y_high."""
        v = [self.bm.verts.new(c) for c in (
            (x0, y_low, z_low), (x1, y_low, z_low), (x1, y_high, z_low), (x0, y_high, z_low),
            (x0, y_high, z_high), (x1, y_high, z_high))]
        for f in ((v[0], v[3], v[2], v[1]), (v[3], v[4], v[5], v[2]),
                  (v[0], v[1], v[5], v[4]), (v[0], v[4], v[3]), (v[1], v[2], v[5])):
            self.bm.faces.new(f)

    def wedge_x(self, y0, y1, x_low, z_low, x_high, z_high):
        """Solid ramp running along x: low edge at x_low, vertical back at x_high."""
        v = [self.bm.verts.new(c) for c in (
            (x_low, y0, z_low), (x_low, y1, z_low), (x_high, y1, z_low), (x_high, y0, z_low),
            (x_high, y0, z_high), (x_high, y1, z_high))]
        for f in ((v[0], v[1], v[2], v[3]), (v[3], v[2], v[5], v[4]),
                  (v[0], v[4], v[5], v[1]), (v[0], v[3], v[4]), (v[1], v[5], v[2])):
            self.bm.faces.new(f)

    def wall_along(self, x, y0, z0, y1, z1, h=RAIL_H, t=0.1):
        """A thin, possibly sloped wall whose foot runs (y0,z0) -> (y1,z1) at x."""
        v = [self.bm.verts.new(c) for c in (
            (x - t / 2, y0, z0), (x + t / 2, y0, z0), (x + t / 2, y1, z1), (x - t / 2, y1, z1),
            (x - t / 2, y0, z0 + h), (x + t / 2, y0, z0 + h), (x + t / 2, y1, z1 + h),
            (x - t / 2, y1, z1 + h))]
        for f in ((v[0], v[3], v[2], v[1]), (v[4], v[5], v[6], v[7]), (v[0], v[1], v[5], v[4]),
                  (v[1], v[2], v[6], v[5]), (v[2], v[3], v[7], v[6]), (v[3], v[0], v[4], v[7])):
            self.bm.faces.new(f)

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
# Visual + collision building blocks. Every walkable thing is drawn and
# collided from the same numbers, in the same call.
# ===========================================================================

def floor(p, col, lo, hi, mat=M.STEEL, t=DECK_T):
    """A walking slab whose TOP is at hi.z."""
    lo, hi = Vector(lo), Vector(hi)
    p.slab((lo.x, lo.y, hi.z - t), (hi.x, hi.y, hi.z), mat)
    col.box((lo.x, lo.y, hi.z - max(t, 0.25)), (hi.x, hi.y, hi.z))


def rail_x(p, col, x, y0, y1, z, collide=True):
    """A straight railing running along y at fixed x, standing on walking height z."""
    if y1 - y0 < 0.15:
        return
    n = max(1, int(math.ceil((y1 - y0) / 1.6)))
    for i in range(n + 1):
        p.box((x, y0 + (y1 - y0) * i / n, z + RAIL_H / 2), (0.07, 0.07, RAIL_H), M.STEEL)
    for dz in (RAIL_H - 0.03, 0.55):
        sc.beam(p, (x, y0, z + dz), (x, y1, z + dz), 0.06, 0.06, M.STEEL)
    p.box((x, (y0 + y1) / 2, z + 0.06), (0.03, y1 - y0, 0.12), M.ORANGE)
    if collide:
        col.box((x - 0.05, y0, z), (x + 0.05, y1, z + RAIL_H))


def rail_y(p, col, y, x0, x1, z, collide=True):
    """A straight railing running along x at fixed y."""
    if x1 - x0 < 0.15:
        return
    n = max(1, int(math.ceil((x1 - x0) / 1.6)))
    for i in range(n + 1):
        p.box((x0 + (x1 - x0) * i / n, y, z + RAIL_H / 2), (0.07, 0.07, RAIL_H), M.STEEL)
    for dz in (RAIL_H - 0.03, 0.55):
        sc.beam(p, (x0, y, z + dz), (x1, y, z + dz), 0.06, 0.06, M.STEEL)
    p.box(((x0 + x1) / 2, y, z + 0.06), (x1 - x0, 0.03, 0.12), M.ORANGE)
    if collide:
        col.box((x0, y - 0.05, z), (x1, y + 0.05, z + RAIL_H))


def rail_x_spans(p, col, x, y0, y1, z, gaps):
    """rail_x from y0 to y1 with openings at `gaps` [(a, b), ...]."""
    cur = y0
    for a, b in sorted(gaps):
        if b <= y0 or a >= y1:
            continue
        rail_x(p, col, x, cur, max(cur, a), z)
        cur = max(cur, b)
    rail_x(p, col, x, cur, y1, z)


def stair(p, col, x0, x1, y_low, z_low, y_high, z_high, rails=(True, True), rails_to_z=None):
    """A stair along y. Collision is a smooth wedge; the treads are for the eye.

    `rails_to_z` stops the side rails where the flight reaches that height. A
    flight arriving up through a floor opening needs it: past that point the
    floor slab's own edge is the guard, and a rail carried to the top stands
    proud of the floor exactly where players turn off the stair.
    """
    rise = z_high - z_low
    run = abs(y_high - y_low)
    if rise / run > STAIR_K + 1e-3:
        raise RuntimeError("stair at x %.1f..%.1f is %.1f deg, steeper than the %.1f the "
                           "capsule can walk" % (x0, x1, math.degrees(math.atan(rise / run)),
                                                 math.degrees(math.atan(STAIR_K))))
    n = max(3, round(rise / TREAD))
    dy, dz = (y_high - y_low) / n, rise / n
    for i in range(n):
        p.box(((x0 + x1) / 2, y_low + dy * (i + 0.5), z_low + dz * (i + 1) - 0.035),
              (x1 - x0, abs(dy) * 1.02, 0.07), M.STEEL)
    for x in (x0 + 0.07, x1 - 0.07):
        sc.beam(p, (x, y_low, z_low - 0.12), (x, y_high, z_high - 0.12), 0.12, 0.30, M.YELLOW)
    col.wedge(x0, x1, y_low, z_low, y_high, z_high)
    rz = z_high if rails_to_z is None else min(z_high, rails_to_z)
    ry = y_low + (y_high - y_low) * (rz - z_low) / rise
    for side, x in zip(rails, (x0 - 0.05, x1 + 0.05)):
        if not side:
            continue
        n_posts = max(2, int(math.ceil(abs(ry - y_low) / 1.4)) + 1)
        for i in range(n_posts):
            t = i / (n_posts - 1)
            y = y_low + (ry - y_low) * t
            z = z_low + (rz - z_low) * t
            p.box((x, y, z + RAIL_H / 2), (0.07, 0.07, RAIL_H), M.STEEL)
        sc.beam(p, (x, y_low, z_low + RAIL_H), (x, ry, rz + RAIL_H), 0.06, 0.06, M.STEEL)
        col.wall_along(x, y_low, z_low, ry, rz)


def post(p, col, x, y, z0, z1, s=0.18, collide=True, mat=M.STEEL):
    if z1 - z0 < 0.05:
        return
    p.box((x, y, (z0 + z1) / 2), (s, s, z1 - z0), mat)
    if collide:
        col.box((x - s / 2, y - s / 2, z0), (x + s / 2, y + s / 2, z1))


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


def house_footprints():
    """World boxes of every house, awning and deck floodlight above its own base.

    Above the base only: the user's shared house mesh carries raking struts that
    run 5 m down under the deck to the keel, and a full bounds box would fill
    the promenade lane with an invisible 12 m wall.
    """
    boxes = []
    for prefix in ("Mesh_SkyCity_Home", "Mesh_SkyCity_Shade", "Mesh_SkyCity_Flood"):
        for o in city_objects(prefix):
            base = o.location.z
            lo, hi = world_bounds(o, zmin=base - 0.05)
            if hi.z - lo.z < 1.2 and prefix != "Mesh_SkyCity_Home":
                continue
            if 0.0 < base < 2.0:
                lo.z = 0.0
            boxes.append((lo, hi))
    return boxes


def world_bounds(o, zmin=None):
    mw = o.matrix_world
    pts = [mw @ v.co for v in o.data.vertices]
    if zmin is not None:
        pts = [q for q in pts if q.z >= zmin] or pts
    return (Vector((min(q.x for q in pts), min(q.y for q in pts), min(q.z for q in pts))),
            Vector((max(q.x for q in pts), max(q.y for q in pts), max(q.z for q in pts))))


# ===========================================================================
# The castle, rebuilt hollow (castle-local geometry, world collision)
# ===========================================================================

def wall_x(p, col, off, x0, x1, y0, y1, z0, z1, doors=(), mat=M.HULLRUST):
    """A wall running along y (thin in x) with openings (y_a, y_b, height)."""
    cur = y0
    for a, b, h in sorted(doors):
        p.slab((x0, cur, z0), (x1, a, z1), mat)
        col.box((x0, cur + off.y, z0 + off.z), (x1, a + off.y, z1 + off.z))
        p.slab((x0, a, z0 + h), (x1, b, z1), mat)
        col.box((x0, a + off.y, z0 + h + off.z), (x1, b + off.y, z1 + off.z))
        cur = b
    p.slab((x0, cur, z0), (x1, y1, z1), mat)
    col.box((x0, cur + off.y, z0 + off.z), (x1, y1 + off.y, z1 + off.z))


def wall_y(p, col, off, y0, y1, x0, x1, z0, z1, doors=(), mat=M.HULLRUST):
    """A wall running along x (thin in y) with openings (x_a, x_b, height)."""
    cur = x0
    for a, b, h in sorted(doors):
        p.slab((cur, y0, z0), (a, y1, z1), mat)
        col.box((cur, y0 + off.y, z0 + off.z), (a, y1 + off.y, z1 + off.z))
        p.slab((a, y0, z0 + h), (b, y1, z1), mat)
        col.box((a, y0 + off.y, z0 + h + off.z), (b, y1 + off.y, z1 + off.z))
        cur = b
    p.slab((cur, y0, z0), (x1, y1, z1), mat)
    col.box((cur, y0 + off.y, z0 + off.z), (x1, y1 + off.y, z1 + off.z))


def slab_with_hole(p, col, off, lo, hi, hole, mat):
    """A horizontal slab lo..hi with a rectangular hole (x0, x1, y0, y1)."""
    hx0, hx1, hy0, hy1 = hole
    pieces = [((lo[0], lo[1]), (hi[0], hy0)), ((lo[0], hy1), (hi[0], hi[1])),
              ((lo[0], hy0), (hx0, hy1)), ((hx1, hy0), (hi[0], hy1))]
    for (ax, ay), (bx, by) in pieces:
        if bx - ax > 1e-3 and by - ay > 1e-3:
            p.slab((ax, ay, lo[2]), (bx, by, hi[2]), mat)
            col.box((ax, ay + off.y, lo[2] + off.z), (bx, by + off.y, hi[2] + off.z))


def castle_hollow(mats, vis, col):
    """Ground floor and first floor you can walk into; top storey solid.

    Circulation, deliberately readable from outside (GDC-L1-LEVEL-0001): both
    promenade lanes run straight in through doors aligned with them; a stair
    inside climbs to the first floor; the first floor opens onto the ground-floor
    roof; and the stair tower on the starboard flank (built separately) links
    every terrace, the roof and the crown gantry.
    """
    off = castle_offset()
    (g_lo, g_hi, g_w, g_y0, g_y1, _), (f_lo, f_hi, f_w, f_y0, f_y1, _), \
        (s_lo, s_hi, s_w, s_y0, s_y1, _) = sc.CASTLE_STEPS
    p = Part(mats)
    # The stairwell opens wherever the flight comes within HEADROOM of the
    # ceiling, plus a margin: exactly at the limit, the head of a player on the
    # stair grazes the slab edge. The flight rises forward (toward -y).
    stair_top_y = INNER_STAIR_FOOT_Y - T1 / STAIR_K
    well_aft_y = INNER_STAIR_FOOT_Y - (GF_CEIL - HEADROOM - STAIRWELL_MARGIN) / STAIR_K
    inner_hole = (INNER_STAIR_X[0], INNER_STAIR_X[1], stair_top_y, well_aft_y)

    # --- ground floor -----------------------------------------------------
    p.slab((-g_w, g_y0, g_lo), (g_w, g_y1, 0.0), M.STEEL)
    wall_y(p, col, off, g_y0, g_y0 + WALL, -g_w, g_w, 0.0, GF_CEIL)
    wall_y(p, col, off, g_y1 - WALL, g_y1, -g_w, g_w, 0.0, GF_CEIL,
           doors=[(a, b, GF_DOOR_H) for a, b in AFT_DOORS])
    for sx in (-1, 1):
        x0, x1 = sorted((sx * g_w, sx * (g_w - WALL)))
        wall_x(p, col, off, x0, x1, g_y0 + WALL, g_y1 - WALL, 0.0, GF_CEIL,
               doors=[(GF_FLANK_DOOR[0], GF_FLANK_DOOR[1], GF_DOOR_H)])
    slab_with_hole(p, col, off, (-g_w, g_y0, GF_CEIL), (g_w, g_y1, g_hi), inner_hole, M.HULLRUST)
    slab_with_hole(p, col, off, (-g_w - 0.55, g_y0 - 0.55, g_hi), (g_w + 0.55, g_y1 + 0.55, T1),
                   inner_hole, M.STEEL)
    stair(p, col_local(col, off), INNER_STAIR_X[0], INNER_STAIR_X[1],
          INNER_STAIR_FOOT_Y, 0.0, stair_top_y, T1, rails_to_z=T1 + 0.3 - RAIL_H)
    for y in (g_y0 + 3.0, (g_y0 + g_y1) / 2, g_y1 - 3.0):
        for x in (-4.0, 4.0):
            p.box((x, y, GF_CEIL - 0.05), (0.7, 0.7, 0.08), M.LAMP)

    # --- first floor ------------------------------------------------------
    wall_y(p, col, off, f_y0, f_y0 + WALL, -f_w, f_w, T1, F1_CEIL, mat=M.YELLOW)
    # No aft door on the first floor: the user's stretched forward gas bag
    # pokes its nose through that wall, leaving 0.6 m of headroom outside it on
    # the ground-floor roof. A door there would promise a way out that is not
    # one. The first floor opens through its flanks instead.
    wall_y(p, col, off, f_y1 - WALL, f_y1, -f_w, f_w, T1, F1_CEIL, mat=M.YELLOW)
    for sx, door in ((-1, F1_PORT_DOOR), (1, F1_STBD_DOOR)):
        x0, x1 = sorted((sx * f_w, sx * (f_w - WALL)))
        wy, wz = F1_WINDOW
        wall_x(p, col, off, x0, x1, f_y0 + WALL, f_y1 - WALL, T1, F1_CEIL, mat=M.YELLOW,
               doors=[(door[0], door[1], F1_DOOR_H)])
        p.slab((x0 - 0.02, wy[0], wz[0]), (x1 + 0.02, wy[1], wz[1]), M.GLASS)
    p.slab((-f_w, f_y0, F1_CEIL), (f_w, f_y1, f_hi), M.YELLOW)
    col.box((-f_w - 0.55, f_y0 - 0.55 + off.y, F1_CEIL), (f_w + 0.55, f_y1 + 0.55 + off.y, T2))
    p.slab((-f_w - 0.55, f_y0 - 0.55, f_hi), (f_w + 0.55, f_y1 + 0.55, T2), M.STEEL)
    # The well is guarded only where it is deeper than a drop that hurts. Near
    # the stair's top the well is shallow, and leaving that stretch unrailed is
    # what opens the 1.2 m path between it and the pod into the rest of the room.
    hx0, hx1, hy0, hy1 = inner_hole
    guard_to = INNER_STAIR_FOOT_Y - (T1 - SAFE_DROP) / STAIR_K
    rail_x(p, col_local(col, off), hx0 - 0.05, guard_to, hy1, T1)
    rail_y(p, col_local(col, off), hy1 + 0.05, hx0, hx1, T1)
    for y in ((f_y0 + f_y1) / 2 - 2.5, (f_y0 + f_y1) / 2 + 2.5):
        p.box((-4.5, y, F1_CEIL - 0.05), (0.6, 0.6, 0.08), M.LAMP)

    # --- top storey, solid, and its lip -----------------------------------
    p.slab((-s_w, s_y0, T2), (s_w, s_y1, s_hi), M.YELLOW)
    col.box((-s_w, s_y0 + off.y, T2), (s_w, s_y1 + off.y, s_hi))
    p.slab((-s_w - 0.55, s_y0 - 0.55, s_hi), (s_w + 0.55, s_y1 + 0.55, T3), M.STEEL)
    col.box((-s_w - 0.55, s_y0 - 0.55 + off.y, s_hi), (s_w + 0.55, s_y1 + 0.55 + off.y, T3))

    # --- terrace railings, with the tower's bridge openings ---------------
    cl = col_local(col, off)
    tower = tower_levels()
    for sx in (-1, 1):
        rail_x_spans(p, cl, sx * (g_w + 0.42), g_y0 - 0.55, g_y1 + 0.55, T1,
                     [tower["t1_gap"]] if sx > 0 else [])
        rail_x_spans(p, cl, sx * (f_w + 0.42), f_y0 - 0.55, f_y1 + 0.55, T2,
                     [tower["t2_gap"]] if sx > 0 else [])
        rail_x_spans(p, cl, sx * (s_w + 0.42), s_y0 - 0.55, s_y1 + 0.55, T3,
                     [tower["t3_gap"]] if sx > 0 else [])
    rail_y(p, cl, g_y0 - 0.5, -g_w - 0.42, g_w + 0.42, T1)
    rail_y(p, cl, g_y1 + 0.5, -g_w - 0.42, g_w + 0.42, T1)
    pod_half, nose_half, beacon_half = 3.9, 1.8, 3.6
    for a, b in ((-f_w - 0.42, -pod_half), (pod_half, f_w + 0.42)):
        rail_y(p, cl, f_y0 - 0.5, a, b, T2)
    for a, b in ((-f_w - 0.42, -nose_half), (nose_half, f_w + 0.42)):
        rail_y(p, cl, f_y1 + 0.5, a, b, T2)
    for a, b in ((-s_w - 0.42, -beacon_half), (beacon_half, s_w + 0.42)):
        rail_y(p, cl, s_y0 - 0.5, a, b, T3)
    rail_y(p, cl, s_y1 + 0.5, -s_w - 0.42, s_w + 0.42, T3)

    # --- the plated language of the outside, split around every opening ---
    for sx in (-1, 1):
        for zlo, zhi, w, a, b, gaps in ((0.0, g_hi, g_w, g_y0, g_y1, [GF_FLANK_DOOR]),
                                         (T1, f_hi, f_w, f_y0, f_y1,
                                          [F1_PORT_DOOR if sx < 0 else F1_STBD_DOOR, F1_WINDOW[0]]),
                                         (T2, s_hi, s_w, s_y0, s_y1, [])):
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
    sc.castle_derrick(p, g_y0, top, stay_to=(0.0, s_y1 - 1.0, T3))
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

    # Things of the user's that stand inside the rooms. The pod bolted to the
    # castle's front (an inverted dome and a lantern) runs from the first floor
    # up through the roof, where its lantern cap stands 0.6 m proud.
    front = off.y + g_y0 + 8.0
    pod = [o for prefix in ("Mesh_SkyCity_Dome_SensorCupola_Dome", "Mesh_SkyCity_Beacon_SensorCupola_Lantern")
           for o in city_objects(prefix) if world_bounds(o)[1].y < front]
    if pod:
        bounds = [world_bounds(o) for o in pod]
        col.box((-pod_half, min(lo.y for lo, hi in bounds), T1),
                (pod_half, max(hi.y for lo, hi in bounds), max(hi.z for lo, hi in bounds)))
    prow = bpy.data.objects.get("Mesh_SkyCity_Prow")
    if prow is not None:
        mw = prow.matrix_world
        inside = [mw @ v.co for v in prow.data.vertices]
        inside = [q for q in inside if q.y > off.y + g_y0 + WALL and abs(q.x) < g_w - WALL and q.z > 0.0]
        if inside:
            col.box((min(q.x for q in inside), min(q.y for q in inside), 0.0),
                    (max(q.x for q in inside), max(q.y for q in inside), max(q.z for q in inside)))


class col_local:
    """Adapter so the shared rail/stair helpers can take castle-local y and z."""

    def __init__(self, col, off):
        self.col, self.off = col, off

    def box(self, lo, hi):
        self.col.box((lo[0], lo[1] + self.off.y, lo[2] + self.off.z),
                     (hi[0], hi[1] + self.off.y, hi[2] + self.off.z))

    def wedge(self, x0, x1, y_low, z_low, y_high, z_high):
        self.col.wedge(x0, x1, y_low + self.off.y, z_low + self.off.z,
                       y_high + self.off.y, z_high + self.off.z)

    def wall_along(self, x, y0, z0, y1, z1, h=RAIL_H, t=0.1):
        self.col.wall_along(x, y0 + self.off.y, z0 + self.off.z, y1 + self.off.y,
                            z1 + self.off.z, h, t)


# ===========================================================================
# The stair tower on the castle's starboard flank (castle-local)
# ===========================================================================

def tower_levels():
    """Every level, flight and bridge of the tower, derived from the terraces.

    Climbed in this order - deck, F-a, L1 (onto the ground-floor roof), F-b, L2
    (onto the first-floor roof), F-c, L3 (onto the castle roof), F-d, L4 and the
    bridge onto the crown gantry. Four flights in three columns, each flight
    rising the opposite way to the one before it so the tower stays 17 m long.
    """
    z0, z1, z2, z3, z4 = 0.0, T1, T2, T3, GANTRY_WALK
    fa_top = -44.9 - (z1 - z0) / STAIR_K
    fb_top = fa_top + (z2 - z1) / STAIR_K
    fc_top = fb_top - (z3 - z2) / STAIR_K
    fd_bot = -54.3
    fd_top = fd_bot + (z4 - z3) / STAIR_K
    return dict(z=(z0, z1, z2, z3, z4),
                fa=(COL_A, -44.9, z0, fa_top, z1),
                fb=(COL_B, fa_top, z1, fb_top, z2),
                fc=(COL_C, fb_top, z2, fc_top, z3),
                fd=(COL_B, fd_bot, z3, fd_top, z4),
                l1=[(COL_A, TOWER_FWD, fa_top), (COL_B, TOWER_FWD, fa_top)],
                l2=[(COL_A, -47.6, -41.5), (COL_B, fb_top, -41.5), (COL_C, fb_top, -41.5)],
                l3=[(COL_B, TOWER_FWD, fd_bot), (COL_C, TOWER_FWD, fc_top)],
                l4=[(COL_B, fd_top, -46.4)],
                t1_gap=(TOWER_FWD, fa_top), t2_gap=(-47.4, -45.6), t3_gap=(-56.05, -54.4),
                b2=((7.7, COL_A[1]), (-47.4, -45.6)),
                b3=((5.7, COL_B[0]), (-56.05, -54.4)),
                b4=((GANTRY_HW, COL_B[0]), (-48.4, -46.6)))


def castle_tower(mats, vis, col):
    off = castle_offset()
    L = tower_levels()
    z0, z1, z2, z3, z4 = L["z"]
    cl = col_local(col, off)
    p = Part(mats)

    def landing(cols, z):
        # Each column's landing reaches halfway into the 0.2 m gap beside it,
        # so adjacent landings meet edge to edge instead of leaving a slot.
        for (x0, x1), ya, yb in cols:
            a, b = sorted((ya, yb))
            floor(p, cl, (x0 - 0.1, a, 0), (x1 + 0.1, b, z))

    for key in ("fa", "fb", "fc", "fd"):
        (x0, x1), y_low, zl, y_high, zh = L[key]
        stair(p, cl, x0, x1, y_low, zl, y_high, zh)
    landing(L["l1"], z1)
    landing(L["l2"], z2)
    landing(L["l3"], z3)
    landing(L["l4"], z4)
    floor(p, cl, (sc.CASTLE_STEPS[0][2] + 0.3, TOWER_FWD, 0), (COL_A[0], L["fa"][3], z1))
    (bx0, bx1), (by0, by1) = L["b2"]
    floor(p, cl, (bx0, by0, 0), (bx1, by1, z2))
    (bx0, bx1), (by0, by1) = L["b3"]
    floor(p, cl, (bx0, by0, 0), (bx1, by1, z3))
    (bx0, bx1), (by0, by1) = L["b4"]
    floor(p, cl, (bx0, by0, 0), (bx1, by1, z4))

    # Landing railings: every edge that is neither a flight nor a bridge.
    rail_y(p, cl, TOWER_FWD - 0.05, COL_A[0], COL_B[1], z1)
    rail_x(p, cl, COL_B[1] + 0.05, TOWER_FWD, L["fa"][3], z1)
    rail_y(p, cl, -41.45, COL_A[0], COL_C[1], z2)
    rail_x(p, cl, COL_C[1] + 0.05, L["fb"][3], -41.5, z2)
    rail_x_spans(p, cl, COL_A[0] - 0.05, -47.6, -41.5, z2, [L["t2_gap"]])
    rail_x(p, cl, COL_A[1] + 0.1, -47.6, L["fb"][3], z2)
    rail_y(p, cl, -47.65, COL_A[0], COL_A[1], z2)
    rail_y(p, cl, TOWER_FWD - 0.05, COL_B[0], COL_C[1], z3)
    rail_x(p, cl, COL_C[1] + 0.05, TOWER_FWD, L["fc"][3], z3)
    rail_x_spans(p, cl, COL_B[0] - 0.05, TOWER_FWD, L["fd"][1], z3, [L["t3_gap"]])
    rail_x(p, cl, COL_B[1] + 0.1, L["fc"][3], L["fd"][1], z3)
    rail_y(p, cl, -46.35, COL_B[0], COL_B[1], z4)
    rail_x(p, cl, COL_B[1] + 0.05, L["fd"][3], -46.4, z4)
    for (bx0, bx1), (by0, by1), z in ((L["b2"][0], L["b2"][1], z2), (L["b3"][0], L["b3"][1], z3)):
        rail_y(p, cl, by0 - 0.05, bx0, bx1, z)
        rail_y(p, cl, by1 + 0.05, bx0, bx1, z)
    # The gantry bridge's aft edge is open only where the starboard lantern loop
    # continues from it; either side of that is a drop to the gas bags.
    (bx0, bx1), (by0, by1) = L["b4"]
    lane_in, lane_out = BEACON_LOOP["lane"]
    rail_y(p, cl, by0 - 0.05, GANTRY_HW + 0.2, bx1, z4)
    rail_y(p, cl, by1 + 0.05, GANTRY_HW + 0.2, lane_in, z4)
    rail_y(p, cl, by1 + 0.05, lane_out, bx1, z4)

    # Frame: posts standing on the deck, horizontal ties at each level, and
    # X-bracing on the outboard face.
    for x in TOWER_POSTS_X:
        for y in TOWER_POSTS_Y[x]:
            post(p, cl, x, y, 0.0, z4 + RAIL_H + 0.3, s=0.2)
    for z in (z1 - 0.2, z2 - 0.2, z3 - 0.2, z4 - 0.2):
        sc.beam(p, (TOWER_POSTS_X[-1], -58.6, z), (TOWER_POSTS_X[-1], -45.6, z), 0.16, 0.22, M.YELLOW)
    for za, zb in ((0.4, z1 - 0.3), (z1, z2 - 0.3), (z2, z3 - 0.3), (z3, z4 - 0.3)):
        for ya, yb in ((-58.6, -50.0), (-50.0, -45.6)):
            sc.beam(p, (15.5, ya, za), (15.5, yb, zb), 0.1, 0.1, M.STEEL)
            sc.beam(p, (15.5, yb, za), (15.5, ya, zb), 0.1, 0.1, M.STEEL)

    # Built in castle-local coordinates and parented by position to the castle's
    # origin, so moving the castle in Blender carries its stair tower with it.
    p.bevel(width=0.02, segments=1)
    o = own(p.finish("Mesh_SkyCity_CastleStairTower", vis))
    o.location = off


# ===========================================================================
# Walkway extension, skywalk access, stern supports
# ===========================================================================

def extension(mats, vis, col):
    off = castle_offset()
    y0 = sc.CASTLE_STEPS[0][3] + off.y
    slot0, slot1 = SAIL_SLOT_Y
    outer_spans = ((y0, slot0), (slot1, EXT_Y1))
    p = Part(mats)
    underwalk = {(sx, y) for y, sx in sc.UNDERWALKS}
    for sx in (-1, 1):
        a, b = sorted((sx * EXT_IN, sx * EXT_OUT))
        # Full width except a railed slot where the user's hanging bow sail
        # passes through the deck; a 1.9 m walk stays open inboard of it.
        for s0, s1 in outer_spans:
            floor(p, col, (a, s0, 0), (b, s1, 0.0))
            p.box((sx * (EXT_OUT - 0.08), (s0 + s1) / 2, 0.01), (0.16, s1 - s0, 0.02), M.ORANGE)
            sc.beam(p, (sx * (EXT_OUT - 1.0), s0, -0.28), (sx * (EXT_OUT - 1.0), s1, -0.28), 0.14, 0.26, M.STEEL)
            rail_x(p, col, sx * (EXT_OUT - 0.05), s0, s1, 0.0)
        ia, ib = sorted((sx * EXT_IN, sx * SAIL_SLOT_IN))
        floor(p, col, (ia, slot0, 0), (ib, slot1, 0.0))
        rail_x(p, col, sx * SAIL_SLOT_IN, slot0, slot1, 0.0)
        for y in SAIL_SLOT_Y:
            rail_y(p, col, y, *sorted((sx * SAIL_SLOT_IN, sx * (EXT_OUT - 0.05))), 0.0)
        # Dark grating laid under the old patchwork strip, so the gaps between
        # its planks read as grating instead of holes - the collision deck is
        # continuous there and has been since the first prefab.
        ia, ib = sorted((sx * sc.LANE_OUT, sx * EXT_IN))
        p.slab((ia, y0, -0.20), (ib, EXT_Y1, -0.08), M.DARK)
        sc.beam(p, (sx * (EXT_IN + 1.8), y0, -0.28), (sx * (EXT_IN + 1.8), EXT_Y1, -0.28), 0.14, 0.26, M.STEEL)
        y = y0 + 0.4
        i = 0
        while y < EXT_Y1:
            in_slot = slot0 - 0.2 < y < slot1 + 0.2
            reach = SAIL_SLOT_IN if in_slot else EXT_OUT
            sc.beam(p, (sx * EXT_IN, y, -0.22), (sx * (reach - 0.1), y, -0.22), 0.14, 0.24, M.STEEL)
            near_under = any(s == sx and abs(y - yu) < 4.5 for s, yu in underwalk)
            if i % 2 == 0 and not near_under:
                sc.foot(p, (sx * sc.KEEL_HW, y, sc.KEEL_BOT + 0.5), (sx * (reach - 0.4), y, -0.34),
                        0.16, 0.16, M.STEEL)
            y += 3.2
            i += 1
        rail_y(p, col, y0 + 0.05, a, b, 0.0)
        # Lamps on every fifth rail post: a run of light that draws the eye
        # along the walkway at night (GDC-L1-LEVEL-0001).
        n = int((EXT_Y1 - y0) / 8.0)
        for k in range(n + 1):
            ly = y0 + k * 8.0
            if not slot0 <= ly <= slot1:
                p.box((sx * (EXT_OUT - 0.05), ly, RAIL_H + 0.12), (0.18, 0.18, 0.18), M.LAMP)
    p.bevel(width=0.02, segments=1)
    own(p.finish("Mesh_SkyCity_WalkwayExtension", vis))


def skywalk_access(mats, vis, col):
    p = Part(mats)
    O = bpy.data.objects
    houses = house_footprints()

    def in_house(x, y):
        return any(lo.x - 0.15 <= x <= hi.x + 0.15 and lo.y - 0.15 <= y <= hi.y + 0.15
                   for lo, hi in houses)

    for i, (yc, sx, zc) in enumerate(sc.SKYWALKS, start=1):
        walk = O["Mesh_SkyCity_Walk%02d_Catwalk_Straight" % i]
        lo, hi = world_bounds(walk)
        s = walk.location.z
        # The skywalk's own surface and rails, at the height it is really walked.
        # Overlapping the stair by 0.1 m: meeting it exactly edge to edge leaves
        # a seam a downward probe falls straight through.
        col.box((lo.x, lo.y - 0.1, s - 0.25), (hi.x, hi.y + 0.1, s))
        for x in (lo.x + 0.1, hi.x - 0.1):
            col.box((x - 0.05, lo.y, s), (x + 0.05, hi.y, s + RAIL_H))
        # Stair from the walkway extension, in line with the skywalk. Aft for
        # the starboard-bow one, whose forward end is under the stair tower.
        forward = not (sx > 0 and yc < 0)
        if forward:
            top, far = lo.y, hi.y
            stair(p, col, lo.x, hi.x, top - s / STAIR_K, 0.0, top, s)
        else:
            top, far = hi.y, lo.y
            stair(p, col, lo.x, hi.x, top + s / STAIR_K, 0.0, top, s)
        rail_y(p, col, far, lo.x, hi.x, s)
        # Posts on the walkway carry the skywalk the removed rakes used to.
        bracket_bottom = sc.DECK + zc - 0.22 - 0.13
        for y in (yc - 2.9, yc + 2.9):
            for x in (sx * 11.8, sx * 13.2, sx * 14.8):
                if not in_house(x, y):
                    post(p, col, x, y, 0.0, bracket_bottom, s=0.2)
    p.bevel(width=0.02, segments=1)
    own(p.finish("Mesh_SkyCity_SkywalkAccess", vis))


def stern_supports(mats, vis, col):
    so = stern_offset()
    y = sc.BLOCK_Y1 - 2.0 + so.y
    underside = sc.BLOCK_STEPS[0][0] + so.z
    p = Part(mats)
    for sx in (-1, 1):
        post(p, col, sx * 7.6, y, 0.0, underside, s=0.55, mat=M.YELLOW)
    sc.beam(p, (-7.6, y, underside - 0.3), (7.6, y, underside - 0.3), 0.4, 0.5, M.YELLOW)
    for a, b in (((-7.6, 7.2), (7.6, underside - 0.6)), ((7.6, 7.2), (-7.6, underside - 0.6))):
        sc.beam(p, (a[0], y, a[1]), (b[0], y, b[1]), 0.2, 0.2, M.STEEL)
    p.bevel(width=0.03, segments=1)
    own(p.finish("Mesh_SkyCity_SternSupports", vis))


# ===========================================================================
# Crown gantry: loops, lookouts, ramp to the stern terrace, stub posts
# ===========================================================================

def gantry_additions(mats, vis, col):
    p = Part(mats)
    z = GANTRY_WALK
    ops = gantry_openings()
    c_off = castle_offset().y

    # The gantry deck itself and its (opened) railings.
    col.box((-GANTRY_HW, GANTRY_Y0, z - 0.25), (GANTRY_HW, GANTRY_Y1, z))
    for sx in (-1, 1):
        cur = GANTRY_Y0
        for a, b in sorted(ops[sx]):
            col.box((sx * GANTRY_RAIL_X - 0.05, cur, z), (sx * GANTRY_RAIL_X + 0.05, a, z + RAIL_H))
            post(p, col, sx * GANTRY_RAIL_X, a, z, z + RAIL_H, s=0.1, collide=False)
            post(p, col, sx * GANTRY_RAIL_X, b, z, z + RAIL_H, s=0.1, collide=False)
            cur = b
        col.box((sx * GANTRY_RAIL_X - 0.05, cur, z), (sx * GANTRY_RAIL_X + 0.05, GANTRY_Y1, z + RAIL_H))

    def support(x, y, z_foot):
        sc.foot(p, (x, y, z_foot), (x, y, z - DECK_T), 0.14, 0.14, M.STEEL)

    stringer_top = sc.CAGE_Z + sc.CAGE_R * math.sin(math.radians(60)) + 0.16

    def loop(spec, sx, joined_at=None):
        """A lane round an installation, joined to the gantry by a connector at
        each end - or, forward, by the stair tower's bridge (`joined_at` = the
        y where that bridge's aft edge is), which then does the connector's job."""
        (li, lo_), (y0, y1), c = spec["lane"], spec["y"], spec["conn"]
        xa, xb = sorted((sx * li, sx * lo_))
        ca, cb = sorted((sx * GANTRY_HW, sx * li))
        lane_y0 = y0 if joined_at is None else joined_at
        floor(p, col, (xa, lane_y0, 0), (xb, y1, z))
        floor(p, col, (ca, y1 - c, 0), (cb, y1, z))
        rail_x(p, col, sx * (lo_ - 0.05), lane_y0, y1, z)
        rail_x(p, col, sx * (li + 0.05), lane_y0 if joined_at is not None else y0 + c, y1 - c, z)
        rail_y(p, col, y1 - 0.05, min(ca, xa), max(cb, xb), z)
        rail_y(p, col, y1 - c - 0.05, ca, cb, z)
        if joined_at is None:
            floor(p, col, (ca, y0, 0), (cb, y0 + c, z))
            rail_y(p, col, y0 + 0.05, min(ca, xa), max(cb, xb), z)
            rail_y(p, col, y0 + c + 0.05, ca, cb, z)
        yy = lane_y0 + 2.0
        while yy < y1 - 1.0:
            support(sx * 4.2, yy, stringer_top)
            yy += 4.5

    loop(BEACON_LOOP, 1, joined_at=-46.6 + c_off)
    loop(BEACON_LOOP, -1)
    loop(DOME_LOOP, 1)
    loop(DOME_LOOP, -1)

    for y0, y1 in LOOKOUTS:
        for sx in (-1, 1):
            xa, xb = sorted((sx * GANTRY_HW, sx * LOOKOUT_OUT))
            floor(p, col, (xa, y0, 0), (xb, y1, z))
            rail_x(p, col, sx * (LOOKOUT_OUT - 0.05), y0, y1, z)
            rail_y(p, col, y0 + 0.05, xa, xb, z)
            rail_y(p, col, y1 - 0.05, xa, xb, z)
            for yy in (y0 + 0.8, y1 - 0.8):
                support(sx * 4.2, yy, stringer_top)
                sc.beam(p, (sx * 4.2, yy, stringer_top), (sx * (LOOKOUT_OUT - 0.3), yy, z - DECK_T),
                        0.12, 0.12, M.STEEL)

    # Installations sit across the deck: solid, so a player walks the loop.
    for prefix, pad in (("Mesh_SkyCity_Beacon_SensorCupola_Lantern", 0.1),
                        ("Mesh_SkyCity_Dome_SensorCupola_Dome", 0.1),
                        ("Mesh_SkyCity_Dish_SensorCupola_Dish", 0.1)):
        o = bpy.data.objects[prefix]
        lo, hi = world_bounds(o)
        col.box((lo.x - pad, lo.y - pad, z), (hi.x + pad, hi.y + pad, hi.z))

    # Ramp up to the stern tower's terrace, inside the gantry's width.
    so = stern_offset()
    terrace = sc.BLOCK_STEPS[0][1] + 0.24 + so.z
    ramp_top = RAMP_Y0 + (terrace - z) / STAIR_K
    stair(p, col, -GANTRY_HW + 0.2, GANTRY_HW - 0.2, RAMP_Y0, z, ramp_top, terrace)

    # Stub posts: the gantry braces over the castle roof used to land on the
    # cage's upper stringers, which now stop short of the roof walk.
    stringer_z = sc.CAGE_Z + sc.CAGE_R * math.sin(math.radians(60)) - 0.16
    for y in (-52.57, -48.11):
        for sx in (-1, 1):
            sc.foot(p, (sx * 4.22, y, T3), (sx * 4.22, y, stringer_z), 0.24, 0.24, M.STEEL)
            col.box((sx * 4.22 - 0.12, y - 0.12, T3), (sx * 4.22 + 0.12, y + 0.12, stringer_z))

    p.bevel(width=0.02, segments=1)
    own(p.finish("Mesh_SkyCity_GantryWalks", vis))


def stern_terrace(mats, vis, col):
    """Terrace railings and collision for the stern tower, bridges and pods."""
    so = stern_offset()
    (b_lo, b_hi, b_w, b_y0, b_y1, _), (u_lo, u_hi, u_w, u_y0, u_y1, _) = sc.BLOCK_STEPS
    terrace = b_hi + 0.24 + so.z
    rim = (-b_w - 0.5, b_y0 - 0.5 + so.y, b_w + 0.5, b_y1 + 0.5 + so.y)
    p = Part(mats)
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
        col.box((lo.x, lo.y, s - 0.25), (hi.x, hi.y, s))
        # The user's bridge runs 2.8 m inboard across the terrace walkway before
        # it meets the tower wall. Its rails only collide outboard of the rim,
        # or walking round the terrace means climbing over them.
        rim_edge = rim[2] if lo.x > 0 else rim[0]
        ra, rb = sorted((rim_edge, hi.x if lo.x > 0 else lo.x))
        for y in (lo.y + 0.1, hi.y - 0.1):
            col.box((ra, y - 0.05, s), (rb, y + 0.05, s + RAIL_H))
        # The bridges sit 0.11 m below the terrace. A capsule does not reliably
        # climb even that, so a short wedge meets the rim edge.
        edge = rim[2] if lo.x > 0 else rim[0]
        col.wedge_x(lo.y, hi.y, edge + math.copysign(0.6, lo.x), s, edge, terrace)
        gaps.append((math.copysign(1, lo.x), lo.y - 0.1, hi.y + 0.1))
    for sx in (-1, 1):
        rail_x_spans(p, col, sx * (rim[2] - 0.05), rim[1], rim[3], terrace,
                     [(a, b) for s, a, b in gaps if s == sx])
    tank = [o for o in city_objects("Mesh_SkyCity_Home16_Shanty_Water.") if o.location.z > 15.0]
    blocked = []
    for o in tank:
        lo, hi = world_bounds(o)
        blocked.append((lo.x - 0.2, hi.x + 0.2))
    cur = rim[0]
    ramp_gap = (-GANTRY_HW + 0.05, GANTRY_HW - 0.05)
    for a, b in sorted(blocked + [ramp_gap]):
        rail_y(p, col, rim[1] + 0.05, cur, a, terrace)
        cur = b
    rail_y(p, col, rim[1] + 0.05, cur, rim[2], terrace)

    for o in city_objects("Mesh_SkyCity_Beacon_SensorCupola_Lantern."):
        lo, hi = world_bounds(o)
        if lo.z < 15.0 or o.location.y < 40.0:
            continue
        cx, cy = o.location.x, o.location.y
        pod_lo = min(world_bounds(d)[0].z for d in city_objects("Mesh_SkyCity_Dome_SensorCupola_Dome.")
                     if abs(d.location.x - cx) < 1.0 and abs(d.location.y - cy) < 1.0)
        col.box((cx - 3.6, cy - 3.6, pod_lo), (cx + 3.6, cy + 3.6, hi.z))
    p.bevel(width=0.02, segments=1)
    own(p.finish("Mesh_SkyCity_SternTerrace", vis))


def fixed_collision(col):
    """Deck, keel, houses, deck floodlights and the user's stern piers."""
    off = castle_offset()
    y0 = sc.CASTLE_STEPS[0][3] + off.y
    col.box((-EXT_IN, y0, -0.3), (EXT_IN, EXT_Y1, 0.0))
    col.box((-sc.SIDE_OUT, EXT_Y1, -0.3), (sc.SIDE_OUT, sc.STERN + 4.4, 0.0))
    col.box((-sc.KEEL_HW, sc.BOW, sc.KEEL_BOT), (sc.KEEL_HW, sc.STERN, -0.3))

    for lo, hi in house_footprints():
        col.box(lo, hi)

    for o in city_objects("Mesh_SkyCity_Walk03_Catwalk_Straight."):
        if o.location.z > 1.0:
            continue
        lo, hi = world_bounds(o)
        s = o.location.z
        col.box((lo.x, lo.y, s - 0.25), (hi.x, hi.y, s))
        # The user's stern piers sit 0.4 m below the deck: a ramp, not a step.
        inner = math.copysign(sc.SIDE_OUT, lo.x)
        col.wedge_x(lo.y, hi.y, inner + math.copysign(1.0, lo.x), s, inner, 0.0)

    for o in city_objects("Mesh_SkyCity_Under0"):
        lo, hi = world_bounds(o)
        s = o.location.z
        col.box((lo.x, lo.y, s - 0.25), (hi.x, hi.y, s))
    g = bpy.data.objects.get("Mesh_SkyCity_Gangway_Catwalk_Bridge")
    if g is not None:
        lo, hi = world_bounds(g)
        col.box((lo.x, lo.y, g.location.z - 0.25), (hi.x, hi.y, g.location.z))


# ===========================================================================
# Verification: walk the routes against the collision set
# ===========================================================================

def collision_bvh():
    """Exactly what Unity will collide with once the builder consumes COL_*:
    the COL_ primitives, crates by bounds (the builder's Box rule, gated at
    1.2 m), and the gas bags by mesh (the Convex rule)."""
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
    for o in city_objects("Mesh_SkyCity_Stow"):
        lo, hi = world_bounds(o)
        if hi.z - lo.z >= 1.2:
            add_box(lo, hi)
    for o in city_objects("Mesh_SkyCity_Bag"):
        add(o)
    return BVHTree.FromPolygons(verts, polys)


class Solids:
    """Point-in-solid test against every convex collision primitive.

    Exists because rays cannot see a solid they start inside. The first version
    of `verify` passed a route walked straight through a house and straight
    through the gantry's lantern tower: the floor ray found the solid's bottom
    face, and the headroom and side rays found nothing within reach because they
    began inside. Each primitive is tested against its own face planes - a
    nearest-face test on the combined BVH gives the wrong sign wherever two
    primitives overlap, and on the bags' concave collars.
    """
    CELL = 4.0

    def __init__(self):
        self.prims, self.grid = [], {}
        for o in bpy.data.collections[COL_COLL].objects:
            bm = bmesh.new()
            bm.from_mesh(o.data)
            bm.transform(o.matrix_world)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            bm.normal_update()
            for group, lo, hi in islands(bm):
                faces = {f for v in group for f in v.link_faces}
                self.add([(f.calc_center_median().copy(), f.normal.copy()) for f in faces], lo, hi)
            bm.free()
        for o in city_objects("Mesh_SkyCity_Stow"):
            lo, hi = world_bounds(o)
            if hi.z - lo.z >= 1.2:
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


def routes():
    """Named polylines a player must be able to walk, (x, y, surface z) each."""
    off = castle_offset().y
    L = tower_levels()
    z0, z1, z2, z3, z4 = L["z"]
    so = stern_offset()
    terrace = sc.BLOCK_STEPS[0][1] + 0.24 + so.z
    ramp_top = RAMP_Y0 + (terrace - GANTRY_WALK) / STAIR_K

    def cy(y):
        return y + off

    r = {}
    for sx, n in ((1, "starboard"), (-1, "port")):
        r["lane " + n] = [(sx * 5.7, cy(-44.0), 0.0), (sx * 5.7, 42.0, 0.0)]
    slot0, slot1 = SAIL_SLOT_Y
    # Starboard goes inboard round the sail slot, between the skywalk's support
    # posts and past its stair. To port a house fills the inboard strip, so the
    # slot splits the walkway: its forward end is reached through the castle.
    r["walkway extension starboard"] = [
        (16.2, cy(-57.6), 0.0), (16.2, slot0 - 0.9, 0.0), (12.5, slot0 - 0.9, 0.0),
        (12.5, -30.2, 0.0), (16.2, -30.2, 0.0), (16.2, 42.5, 0.0)]
    r["walkway extension port, forward of the sail"] = [(-16.2, cy(-57.6), 0.0), (-16.2, slot0 - 0.5, 0.0)]
    r["walkway extension port, aft of the sail"] = [(-16.2, slot1 + 0.5, 0.0), (-16.2, 42.5, 0.0)]
    stair_x = sum(INNER_STAIR_X) / 2
    stair_top = INNER_STAIR_FOOT_Y - T1 / STAIR_K
    f1_door = sum(F1_STBD_DOOR) / 2
    r["into castle, port lane to port flank door"] = [
        (-5.7, cy(-41.0), 0.0), (-5.7, cy(-45.5), 0.0), (-6.6, cy(-56.1), 0.0),
        (-10.4, cy(-56.1), 0.0), (-16.2, cy(-56.1), 0.0)]
    r["into castle, starboard corridor to flank door"] = [
        (7.7, cy(-41.0), 0.0), (7.7, cy(-56.1), 0.0), (16.2, cy(-56.1), 0.0)]
    r["starboard lane, inner stair, first floor, out onto the tower"] = [
        (stair_x, cy(-41.0), 0.0), (stair_x, cy(INNER_STAIR_FOOT_Y - 0.05), 0.0),
        (stair_x, cy(stair_top + 0.05), T1), (stair_x, cy(f1_door), T1),
        (10.4, cy(f1_door), T1)]
    r["first floor room past the pod"] = [
        (stair_x, cy(stair_top - 0.4), T1), (4.5, cy(stair_top - 0.4), T1),
        (4.5, cy(-49.0), T1), (-5.5, cy(-49.0), T1), (-5.5, cy(-55.1), T1),
        (-9.0, cy(-55.1), T1)]
    r["stair tower to crown gantry"] = [
        (16.2, SAIL_SLOT_Y[0] - 0.9, 0.0), (10.4, SAIL_SLOT_Y[0] - 0.9, 0.0), (10.4, cy(-44.95), 0.0),
        (10.4, cy(L["fa"][3] + 0.05), z1), (10.4, cy(-57.5), z1), (12.4, cy(-57.5), z1),
        (12.4, cy(L["fb"][1] + 0.05), z1), (12.4, cy(L["fb"][3]), z2), (12.4, cy(-42.3), z2),
        (14.4, cy(-42.3), z2), (14.4, cy(L["fc"][1] - 0.05), z2), (14.4, cy(L["fc"][3]), z3),
        (14.4, cy(-57.6), z3), (12.4, cy(-57.6), z3), (12.4, cy(L["fd"][1] - 0.05), z3),
        (12.4, cy(L["fd"][3]), z4), (12.4, cy(-47.5), z4), (0.0, cy(-47.5), z4)]
    r["tower to first-floor roof"] = [(10.4, cy(-43.0), z2), (10.4, cy(-46.5), z2),
                                      (6.6, cy(-46.5), z2), (6.6, cy(-56.5), z2)]
    r["tower to castle roof"] = [(12.4, cy(-55.2), z3), (5.1, cy(-55.2), z3), (5.1, cy(-49.0), z3),
                                 (0.0, cy(-49.0), z3)]
    r["crown gantry, starboard loops and lookouts"] = [
        (0.0, cy(-47.5), GANTRY_WALK), (4.8, cy(-47.5), GANTRY_WALK), (4.8, -31.9, GANTRY_WALK),
        (0.0, -31.9, GANTRY_WALK), (0.0, -10.0, GANTRY_WALK), (6.5, -10.0, GANTRY_WALK),
        (0.0, -10.0, GANTRY_WALK), (0.0, 10.0, GANTRY_WALK), (6.5, 10.0, GANTRY_WALK),
        (0.0, 10.0, GANTRY_WALK), (0.0, 22.4, GANTRY_WALK), (5.9, 22.4, GANTRY_WALK),
        (5.9, 45.6, GANTRY_WALK), (0.0, 45.6, GANTRY_WALK)]
    r["crown gantry, port loops and lookouts"] = [
        (0.0, -49.0, GANTRY_WALK), (0.0, -44.1, GANTRY_WALK), (-4.8, -44.1, GANTRY_WALK),
        (-4.8, -31.9, GANTRY_WALK), (0.0, -31.9, GANTRY_WALK), (0.0, -10.0, GANTRY_WALK),
        (-6.5, -10.0, GANTRY_WALK), (0.0, -10.0, GANTRY_WALK), (0.0, 10.0, GANTRY_WALK),
        (-6.5, 10.0, GANTRY_WALK), (0.0, 10.0, GANTRY_WALK), (0.0, 22.4, GANTRY_WALK),
        (-5.9, 22.4, GANTRY_WALK), (-5.9, 45.6, GANTRY_WALK), (0.0, 45.6, GANTRY_WALK)]
    r["ramp to stern terrace and pods"] = [
        (0.0, 45.6, GANTRY_WALK), (0.0, RAMP_Y0 + 0.05, GANTRY_WALK), (0.0, ramp_top, terrace),
        (0.0, 50.4, terrace), (-7.4, 50.4, terrace), (-7.4, 54.75, terrace),
        (-12.0, 54.75, 21.297)]
    for i, (yc, sx, zc) in enumerate(sc.SKYWALKS, start=1):
        walk = bpy.data.objects["Mesh_SkyCity_Walk%02d_Catwalk_Straight" % i]
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


def verify(step=0.25):
    """Walk every route: floor under the feet, headroom, and side clearance."""
    bvh = collision_bvh()
    solids = Solids()
    up, down = Vector((0, 0, 1)), Vector((0, 0, -1))
    report = {}
    for name, pts in routes().items():
        fails = []
        for (ax, ay, az), (bx, by, bz) in zip(pts, pts[1:]):
            a, b = Vector((ax, ay, az)), Vector((bx, by, bz))
            length = (Vector((bx, by)) - Vector((ax, ay))).length
            n = max(1, int(length / step))
            d = (b - a)
            side = Vector((-d.y, d.x, 0.0))
            side = side.normalized() if side.length > 1e-6 else Vector((1, 0, 0))
            for i in range(n + 1):
                q = a.lerp(b, i / n)
                if any(solids.contains(q + up * h) for h in (0.5, 1.0, 1.6)):
                    fails.append(("in solid", q, None))
                    continue
                hit, nrm, _, dist = bvh.ray_cast(q + up * 1.0, down, 1.7)
                if hit is None or abs(hit.z - q.z) > 0.3:
                    fails.append(("no floor", q, None if hit is None else round(hit.z, 2)))
                    continue
                if nrm.z < math.cos(math.radians(35)):
                    fails.append(("too steep", q, round(math.degrees(math.acos(max(-1, min(1, nrm.z)))), 1)))
                h2, _, _, _ = bvh.ray_cast(hit + up * 0.05, up, HEADROOM)
                if h2 is not None:
                    fails.append(("headroom", q, round(h2.z - hit.z, 2)))
                for s in (side, -side):
                    for zz in (0.35, 1.2, 1.8):
                        h3, _, _, _ = bvh.ray_cast(hit + up * zz, s, PLAYER_R - 0.05)
                        if h3 is not None:
                            fails.append(("pinched", q, round(zz, 2)))
                            break
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

def run():
    if not bpy.data.filepath.lower().endswith("sky_city.blend"):
        raise RuntimeError("Open sky_city.blend first - this pass is written against it.")
    refuse_if_hand_edited()
    mats = bl.link_materials(sc.MATS)
    vis = ensure_collection(VIS_COLL)
    col_coll = ensure_collection(COL_COLL, hide_render=True)
    clear_owned()

    report = {}
    surgery(report)
    col = Col()
    castle_hollow(mats, vis, col)
    castle_tower(mats, vis, col)
    extension(mats, vis, col)
    skywalk_access(mats, vis, col)
    stern_supports(mats, vis, col)
    gantry_additions(mats, vis, col)
    stern_terrace(mats, vis, col)
    fixed_collision(col)
    col.finish("COL_SkyCity", col_coll)

    # matrix_world is stale until the view layer updates. Hashing before this
    # records the stair tower at the origin, and the NEXT run then refuses to
    # rebuild it as "hand-edited" - which is how this line came to exist.
    bpy.context.view_layer.update()
    for name in MODIFIED:
        o = bpy.data.objects[name]
        o["sky_city_hash"] = geom_hash(o)
    for o in bpy.data.objects:
        if o.get("sky_city_pass") == PASS and o.type == 'MESH':
            o["sky_city_hash"] = geom_hash(o)

    print("sky_city_traversal:")
    for k, v in report.items():
        print("  %-48s %s" % (k, v))
    return print_verify(verify())
