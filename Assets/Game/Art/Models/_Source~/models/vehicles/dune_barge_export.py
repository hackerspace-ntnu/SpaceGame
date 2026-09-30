"""Export the dune-barge variants of dune_barge.blend to Unity.

    blender --background --python dune_barge_export.py [-- --src snapshot.blend] [--only <key> ...]

Each variant is one collection of the .blend (the user's hand-built barges) with its own Interior and
Exterior groups and its own rig (dune_barge_rig.py). Everything below is found in the variant by what it
is — its group, its base name without Blender's .001 suffix, its shape — never by a fixed object name,
because the variants are copies the user keeps moving, scaling and extending.

Per variant, the export ships the collection with its rig and adds, in memory only:

  LAD_<Name>            a ladder foot on the rung line, with _Top and _Exit children (Ladder's convention)
  HATCH_<Side>_Outer / _Sill / _Inner
                        the crawl path through each side hatch: fender top, the opening, the floor inside
  VOL_<n>_Min / _Max    the corners of each walkable room: a floor and the ceiling above it
  OPEN_<Name>           an opening you can see into the hull through (the stern port)
  COL_DuneBarge_####    convex collision islands, split so every opening stays open
  <name>_groups.json    which renderers are interior and which exterior (InteriorReveal reads it)

The FBX is at Blender scale; DuneBargeBuilder scales the model so the 3 m player fits. The .blend is
never written. Export after the user has saved: this opens the file on disk (or --src a snapshot).
"""

import json
import math
import os
import re
import sys

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
from _exportlib import export, unity_path  # noqa: E402

SRC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "dune_barge.blend")
_ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if "--src" in _ARGS:
    SRC = os.path.abspath(_ARGS[_ARGS.index("--src") + 1])

# key: (collection, interior group, exterior group, file stem). Keys must match DuneBargeBuilder.Variants.
VARIANTS = {
    "DuneBarge":        ("Collection 2", "Interior", "Exterior", "dune_barge"),
    "DuneBargeCompact": ("Collection 3", "Interior_Small", "Exterior_Small", "dune_barge_compact"),
    "DuneBargeLookout": ("Collection 4", "Interior_C4", "Exterior_C4", "dune_barge_lookout"),
}
COL_COLL = "COL_DuneBarge"

LADDER_EXIT_PAST_TOP = 0.6     # m beyond the ladder's top, over the floor it leads onto
HATCH_OUTSIDE = 0.3            # m outboard of the coaming where the crawl starts: the fender top past it is ~0.5 m
HATCH_INSIDE = 1.3             # m inboard of the wall where it ends: clear of the lower wall's convex collision
PROBE = 0.02                   # m, sampling step when measuring openings
WALL_SEGMENT = 2.0             # m, wall hull length, so a hull does not bridge the fender's changes
NECK_SECTORS = 6               # arch sectors: each hull's chord cuts the tunnel by r(1 - cos(15 deg))
STAIR_RAMP_T = 0.12            # m, slab under the walking line
RAMP_MAX_DEG = 32.0            # the player ship's walkable boarding ramp; friction lets go near 31
RAMP_OVERLAP = 0.05            # m, the ramp's top tucks this far under the floor it reaches
TRACK_SEGMENT = 0.8            # m, length of each running-gear box along the track
FENDER_CLEAR = 0.03            # m, running-gear boxes stop this far under the fender above them
FURNITURE_GAP = 0.05           # m, furniture parts closer than this are one piece of furniture
FLAT_RATIO = 0.3               # a floor is no thicker than this fraction of its narrower side
MIN_ROOM_AREA = 2.0            # m², smaller flat pieces are trim, not floors
ROOM_STEP = 0.5                # m, spacing of the upward probes over a floor
MIN_ROOM_H, MAX_ROOM_H = 1.2, 5.0   # m, a ceiling closer than this is furniture; further is sky
CEILING_BUCKET = 0.3           # m, probes whose ceilings agree to this are one room
ENCLOSED = 3.5                 # m, a room has a wall within this on both sides of its middle
ROOM_FILL = 0.5                # m³, a collision piece may overlap a room or the stairwell by no more than this
STAIR_CLEAR_SIDE = 0.6         # m, kept clear either side of the stair's grating: the player's shoulders
STAIR_LANDING = 0.8            # m kept clear past the stair's top edge, to step off onto the floor it reaches
BODY_HEADROOM = 1.9            # m at Blender scale, the 3 m player at DuneBargeBuilder's x1.6, kept clear over the stair
BODY_RADIUS = 0.33             # m at Blender scale, the player's 0.5 m capsule radius at x1.6, and a little
BODY_SIDES = 16                # the body's cross-section, and the directions a set-down spot is slid in
SET_DOWN_SEARCH = 2.0          # m, how far a set-down spot may slide to find room: the hold's aisle, past cargo under a hatch
SET_DOWN_STEP = 0.1            # m
SET_DOWN_LEVEL = 0.3           # m, a slid spot's floor may differ from the first by no more than this: the same floor
MARK_LIFT = 0.05               # m, a mark sits this far over its floor
SILL_REACH = 1.0               # m, a ladder whose top is this close to a hatch sill is the crawl's inside stair

# groups (base name, suffix stripped) whose meshes are the hull's structure
STRUCTURE = ("Exterior_Hull", "Exterior_RearHull", "Exterior_SternCastle", "Exterior_CastleCabin", "Exterior_Bridge",
             "Interior_Structure", "Interior_Hold")
# never solid: they sit IN an opening (the leaves and lids get their own colliders in Unity)
NO_COLLISION = ("Mesh_Door_Bulkhead_Frame", "Mesh_Door_Bulkhead_Leaf", "Mesh_Hatch_Roof_Coaming", "Mesh_Hatch_Roof_Lid",
                "Mesh_BridgeGlass", "Mesh_Stair_", "Mesh_Neck_DoorFrame", "Mesh_Hold_DoorFrame")
FURNITURE = ("Coll_Bunk", "Coll_WallLocker", "Coll_Locker", "Coll_Engine", "Coll_Crate", "Coll_Barrel",
             "Coll_TradeGoods_ChestStack", "Coll_TradeGoods_RugRolls", "Coll_PaintStation_Trestle",
             "Coll_DrumMag", "Coll_Derrick_Winch", "Coll_Terminal", "Coll_CrewSeat")
SOLID_PROPS = ("Mesh_HeavyGun_Autocannon_Base", "Mesh_HeavyGun_RocketPod_Base", "Mesh_CargoCrane_ScrapDerrick_Base",
               "Mesh_CableDrum_Winch", "Mesh_Stern_GunPortMantlet", "Mesh_RearHull_Crown", "Mesh_Handrail_")


def base(n):
    return re.sub(r"\.\d{3}$", "", n)


def group_base(n):
    return re.sub(r"(_Small|_C4)$", "", base(n))


def world_verts(o):
    W = o.matrix_world
    return [W @ v.co for v in o.data.vertices]


def bounds(vs):
    return (Vector([min(v[i] for v in vs) for i in range(3)]), Vector([max(v[i] for v in vs) for i in range(3)]))


def frange(a, b, step):
    n = int(round((b - a) / step))
    return [a + (b - a) * i / max(n, 1) for i in range(n + 1)]


def object_bvh(o):
    return BVHTree.FromPolygons(world_verts(o), [p.vertices for p in o.data.polygons])


def bvh_of(objs):
    verts, polys = [], []
    for o in objs:
        off = len(verts)
        verts += world_verts(o)
        polys += [[i + off for i in p.vertices] for p in o.data.polygons]
    return BVHTree.FromPolygons(verts, polys)


class Variant:
    def __init__(self, key):
        self.key = key
        self.coll_name, self.interior, self.exterior, self.stem = VARIANTS[key]
        self.coll = bpy.data.collections[self.coll_name]
        self.meshes = [o for o in self.coll.all_objects if o.type == 'MESH']
        self.col_index = 0
        self.rooms = []

    def named(self, prefix):
        return [o for o in self.meshes if base(o.name).startswith(prefix)]

    def one(self, name):
        hits = [o for o in self.meshes if base(o.name) == name]
        return hits[0] if hits else None

    def groups(self, top):
        return [c for c in bpy.data.collections[top].children_recursive] + [bpy.data.collections[top]]

    def structure(self):
        """The hull's structure groups, plus the floor slabs wherever they sit: a variant's main floor may
        be loose in its Interior rather than in a structure group, and a hold with no floor collision
        drops everyone standing in it through the barge."""
        out = []
        for top in (self.interior, self.exterior):
            for g in bpy.data.collections[top].children:
                if group_base(g.name).startswith(STRUCTURE):
                    out += [o for o in g.all_objects if o.type == 'MESH']
        return out + [f for f in self.floors() if f not in out]

    def floors(self):
        """The interior's floor slabs: broad, thin pieces of its structure (or loose in it)."""
        out = []
        for o in bpy.data.collections[self.interior].all_objects:
            if o.type != 'MESH' or base(o.name).startswith(NO_COLLISION):
                continue
            lo, hi = bounds(world_verts(o))
            if (hi.x - lo.x) * (hi.y - lo.y) >= MIN_ROOM_AREA and hi.z - lo.z <= FLAT_RATIO * min(hi.x - lo.x, hi.y - lo.y)                     and (base(o.name) in ("floor", "Mesh_RearHull_Roof", "Mesh_RearHull_CastleFloor") or re.match(r"^Cube(\.\d{3})?$", o.name)):
                out.append(o)
        if not out:
            raise RuntimeError("%s: no floor in %s" % (self.key, self.interior))
        return out

    def track_groups(self):
        return [g for g in bpy.data.collections[self.exterior].children if group_base(g.name).startswith("Exterior_Track_")]

    def walkable(self):
        """What a ladder steps off onto and a crawl lands on. Props are left out: a BVH over every mesh
        in the barge is a few hundred thousand triangles and ran headless Blender out of memory."""
        tracks = [o for g in self.track_groups() for o in g.all_objects if o.type == 'MESH' and "_Fender_" in o.name]
        return [o for o in self.structure() if not base(o.name).startswith(NO_COLLISION)] + tracks + self.named("Mesh_HullBelly")

    def empty(self, name, loc, parent=None):
        e = bpy.data.objects.new(name, None)
        e.empty_display_size = 0.2
        self.coll.objects.link(e)
        e.location = loc
        if parent is not None:
            bpy.context.view_layer.update()
            e.parent = parent
            e.matrix_parent_inverse = parent.matrix_world.inverted()
        return e

    # ── collision ──
    def island(self, points, label):
        pts = [Vector(p) for p in points]
        if len(pts) < 4:
            raise RuntimeError("collision island %s has %d points" % (label, len(pts)))
        b = bmesh.new()
        for p in pts:
            b.verts.new(p)
        res = bmesh.ops.convex_hull(b, input=list(b.verts))
        loose = {g for g in res["geom_interior"] + res["geom_unused"] if isinstance(g, bmesh.types.BMVert)}
        bmesh.ops.delete(b, geom=list(loose), context='VERTS')
        self.col_index += 1
        name = "COL_DuneBarge_%04d" % self.col_index
        me = bpy.data.meshes.new(name)
        b.to_mesh(me); b.free()
        if len(me.polygons) < 4:
            print("  skipped flat island from %s" % label)
            bpy.data.meshes.remove(me); self.col_index -= 1
            return None
        o = bpy.data.objects.new(name, me)
        o["col_source"] = label
        bpy.data.collections[COL_COLL].objects.link(o)
        return o

    def box(self, lo, hi, label):
        return self.island([Vector((x, y, z)) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])], label)

    def split_hulls(self, o, keep_fns, label):
        vs = world_verts(o)
        for i, fn in enumerate(keep_fns):
            sub = [v for v in vs if fn(v)]
            if len(sub) >= 4:
                self.island(sub, "%s/%d" % (label, i))


def gap_along(o, origin_fn, direction, span, step=PROBE, seed=None):
    """One contiguous run of ray misses along `span` — the opening. The run containing `seed` if given,
    else the longest; None if none. (A run, not min..max of every miss: past a plate's edge also misses.)"""
    t = object_bvh(o)
    runs, cur = [], []
    for s in frange(span[0], span[1], step):
        if t.ray_cast(origin_fn(s), direction, 10)[0] is None:
            cur.append(s)
        elif cur:
            runs.append(cur); cur = []
    if cur:
        runs.append(cur)
    if not runs:
        return None
    if seed is not None:
        hit = [r for r in runs if r[0] - step <= seed <= r[-1] + step]
        return (hit[0][0], hit[0][-1]) if hit else None
    best = max(runs, key=len)
    return best[0], best[-1]


def plate_with_door(v, o, z_probe, label):
    """A plate facing +-Y with one doorway: hulls left of it, right of it and over it."""
    vs = world_verts(o)
    lo, hi = bounds(vs)
    y = sum(p.y for p in vs) / len(vs)
    g = gap_along(o, lambda x: Vector((x, y - 1, z_probe)), Vector((0, 1, 0)), (lo.x + 0.05, hi.x - 0.05))
    if g is None:
        v.island(vs, label)
        return None
    x0, x1 = g
    top = gap_along(o, lambda z: Vector(((x0 + x1) / 2, y - 1, z)), Vector((0, 1, 0)), (z_probe, hi.z + 0.1), seed=z_probe)
    z1 = top[1] if top else z_probe
    v.split_hulls(o, [lambda p: p.x <= x0 + PROBE, lambda p: p.x >= x1 - PROBE, lambda p: p.z >= z1 - PROBE], label)
    return x0, x1, z1


def plate_with_port(v, o, label):
    """The stern: a plate facing +-Y with a port somewhere in it; hulls round the port, and an OPEN_ marker."""
    vs = world_verts(o)
    lo, hi = bounds(vs)
    y = sum(p.y for p in vs) / len(vs)
    xc = (lo.x + hi.x) / 2
    zg = gap_along(o, lambda z: Vector((xc, y - 1, z)), Vector((0, 1, 0)), (lo.z + 0.05, hi.z - 0.05))
    if zg is None:
        v.island(vs, label)
        return
    xg = gap_along(o, lambda x: Vector((x, y - 1, (zg[0] + zg[1]) / 2)), Vector((0, 1, 0)), (lo.x + 0.05, hi.x - 0.05))
    v.split_hulls(o, [lambda p: p.x <= xg[0] + PROBE, lambda p: p.x >= xg[1] - PROBE,
                      lambda p: p.z >= zg[1] - PROBE, lambda p: p.z <= zg[0] + PROBE], label)
    v.empty("OPEN_Stern", Vector(((xg[0] + xg[1]) / 2, y, (zg[0] + zg[1]) / 2)))


def side_walls(v, o, label):
    """Walls: each side on its own (the user's castle wall is BOTH sides in one mesh), in segments along
    the hull, split at a hatch hole so it stays open."""
    vs = world_verts(o)
    for s in (1, -1):
        side = [p for p in vs if p.x * s > 0]
        if len(side) < 4:
            continue
        y0w, y1w = min(p.y for p in side), max(p.y for p in side)
        mid_z = (min(p.z for p in side) + max(p.z for p in side)) / 2
        hy = gap_along(o, lambda y: Vector((0, y, mid_z + 0.5)), Vector((s, 0, 0)), (y0w + 0.05, y1w - 0.05))
        hz = None
        if hy:
            hz = gap_along(o, lambda z: Vector((0, (hy[0] + hy[1]) / 2, z)), Vector((s, 0, 0)),
                           (min(p.z for p in side) + 0.05, max(p.z for p in side) - 0.05))
            if hz is None or hy[1] - hy[0] > 0.8 * (y1w - y0w):
                hy = hz = None        # the ray found no wall at that height, not a hatch
        cuts = frange(y0w, y1w, WALL_SEGMENT) if y1w - y0w > WALL_SEGMENT else [y0w, y1w]
        preds = []
        on = (lambda p, s=s: p.x * s > 0)
        for a, b in zip(cuts, cuts[1:]):
            if hy and a < hy[1] and b > hy[0]:
                preds += [lambda p, a=a: on(p) and a - PROBE <= p.y <= hy[0] + PROBE,
                          lambda p, b=b: on(p) and hy[1] - PROBE <= p.y <= b + PROBE,
                          lambda p: on(p) and hy[0] - PROBE <= p.y <= hy[1] + PROBE and p.z <= hz[0] + PROBE,
                          lambda p: on(p) and hy[0] - PROBE <= p.y <= hy[1] + PROBE and p.z >= hz[1] - PROBE]
            else:
                preds.append(lambda p, a=a, b=b: on(p) and a - PROBE <= p.y <= b + PROBE)
        v.split_hulls(o, preds, "%s/%s" % (label, "R" if s > 0 else "L"))


def arch_sectors(v, o, label):
    vs = world_verts(o)
    zc = min(p.z for p in vs) + 0.09
    def sector(i):
        a0 = -90 + 180 * i / NECK_SECTORS; a1 = -90 + 180 * (i + 1) / NECK_SECTORS
        return lambda p: a0 - 1 <= math.degrees(math.atan2(p.x, p.z - zc)) <= a1 + 1
    v.split_hulls(o, [sector(i) for i in range(NECK_SECTORS)], label)


def bow_band(v, o, label):
    vs = world_verts(o)
    cy = sum(p.y for p in vs) / len(vs)
    v.split_hulls(o, [lambda p, i=i: -90 + 30 * i - 1 <= math.degrees(math.atan2(p.x, cy - p.y)) <= -60 + 30 * i + 1
                      for i in range(6)], label)


def top_hit_owner(v, hit_point):
    """Which walkable piece a ray hit: the one whose surface is nearest the hit point."""
    best, dist = None, 1e9
    for o in v.walkable():
        loc = object_bvh(o).find_nearest(hit_point)
        if loc[0] is not None and loc[3] < dist:
            best, dist = o.name, loc[3]
    return best


def stair_ramp(v):
    """An invisible ramp on the line through the stair's tread tops, pinned to the floor it starts on and
    the floor it reaches, capped at a walkable pitch. Returns the stairwell a body needs clear over it, and
    the pitch."""
    stair = v.one("Mesh_Stair_Grating_Neck")
    if stair is None:
        return None, None
    W = stair.matrix_world
    treads = sorted(((W @ p.center) for p in stair.data.polygons if (W.to_3x3() @ p.normal).z > 0.95 and p.area > 0.02), key=lambda c: c.z)
    lo_t, hi_t = treads[0], treads[-1]
    slope = (hi_t.z - lo_t.z) / (hi_t.y - lo_t.y)
    floors = bvh_of(v.walkable())
    s_lo, s_hi = bounds(world_verts(stair))
    xm = (s_lo.x + s_hi.x) / 2
    top_hit = floors.ray_cast(Vector((xm, hi_t.y - math.copysign(0.4, lo_t.y - hi_t.y), hi_t.z + 0.5)), Vector((0, 0, -1)), 2.0)[0]
    foot_hit = floors.ray_cast(Vector((xm, lo_t.y + math.copysign(0.4, lo_t.y - hi_t.y), lo_t.z + 0.2)), Vector((0, 0, -1)), 3.0)[0]
    if top_hit is None or foot_hit is None:
        raise RuntimeError("stair: no floor at its top or its foot")
    deck_z, cock_z = foot_hit.z, top_hit.z
    # the floor the stair climbs to may overhang its upper treads: pin the ramp to that floor's edge
    toward = math.copysign(1.0, lo_t.y - hi_t.y)          # +1: the stair's foot is at +y
    top_floor = next(o for o in v.walkable() if o.name == top_hit_owner(v, top_hit))
    edge_y = (max if toward > 0 else min)(p.y for p in world_verts(top_floor))
    y_top = edge_y - toward * RAMP_OVERLAP
    rise = cock_z - deck_z
    run = max(abs(rise / slope), rise / math.tan(math.radians(RAMP_MAX_DEG)))
    y_foot = y_top + math.copysign(run, lo_t.y - hi_t.y)
    pts = []
    for x in (s_lo.x, s_hi.x):
        for y, z in ((y_foot, deck_z), (y_top, cock_z)):
            pts += [Vector((x, y, z)), Vector((x, y, z - STAIR_RAMP_T))]
    v.island(pts, "stair ramp")
    y_landing = y_top - toward * STAIR_LANDING
    well = (Vector((s_lo.x - STAIR_CLEAR_SIDE, min(y_foot, y_landing), deck_z)),
            Vector((s_hi.x + STAIR_CLEAR_SIDE, max(y_foot, y_landing), cock_z + BODY_HEADROOM)))
    return well, math.degrees(math.atan2(rise, run))


def touching_clusters(objs, gap=FURNITURE_GAP):
    """Groups of objects whose bounds touch (within `gap`), by union-find."""
    boxes = [bounds(world_verts(o)) for o in objs]
    parent = list(range(len(objs)))
    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]; i = parent[i]
        return i
    for i in range(len(objs)):
        for j in range(i + 1, len(objs)):
            (alo, ahi), (blo, bhi) = boxes[i], boxes[j]
            if all(alo[k] - gap <= bhi[k] and blo[k] - gap <= ahi[k] for k in range(3)):
                parent[find(i)] = find(j)
    groups = {}
    for i, o in enumerate(objs):
        groups.setdefault(find(i), []).append(o)
    return list(groups.values())


def track_boxes(v, g, parts):
    """Keeps anyone out of the wheels: boxes over a track unit's running gear, in short lengths, each
    trimmed to just under the fender above it. One box over the whole unit rose above the fender's low
    front and left an invisible step exactly where the fender ladders arrive."""
    fenders = [o for o in parts if "_Fender_" in o.name]
    fender_bvh = bvh_of(fenders) if fenders else None
    body = [p for o in parts if "_Fender_" not in o.name for p in world_verts(o)]
    if not body:
        return
    y0, y1 = min(p.y for p in body), max(p.y for p in body)
    cuts = frange(y0, y1, TRACK_SEGMENT)
    for a, b in zip(cuts, cuts[1:]):
        seg = [p for p in body if a - PROBE <= p.y <= b + PROBE]
        if len(seg) < 4:
            continue
        lo, hi = bounds(seg)
        if fender_bvh is not None:
            tops = [h[0].z for x in (lo.x, (lo.x + hi.x) / 2, hi.x) for y in (a, (a + b) / 2, b)
                    for h in [fender_bvh.ray_cast(Vector((x, y, hi.z + 5)), Vector((0, 0, -1)), 10)] if h[0] is not None]
            if tops:
                hi.z = min(hi.z, min(tops) - FENDER_CLEAR)
        if hi.z - lo.z > 0.1:
            v.box(lo, hi, "%s/%.1f" % (g.name, a))


def overlap_volume(a, b):
    d = [min(a[1][k], b[1][k]) - max(a[0][k], b[0][k]) for k in range(3)]
    return d[0] * d[1] * d[2] if all(x > 0 for x in d) else 0.0


def carve(v, points, clear, label):
    """Convex islands of a piece that stay out of every clear box (the rooms and the stairwell). A concave
    shell round a room - the tub of hull under the cockpit - is one convex hull that fills the room; so is
    the ring of vertices at its walls' knee, which hulls into a slab across the room above. While a piece
    still intrudes, it is cut into the parts beyond each face of the box it intrudes into; each cut lies
    exactly on that box's face, so the box is gone from every part and this ends. What lies inside a box
    is dropped, and said so."""
    if len(points) < 4:
        return
    lo, hi = bounds(points)
    box = next((c for c in clear if overlap_volume((lo, hi), c) > ROOM_FILL), None)
    if box is None:
        v.island(points, label)
        return
    blo, bhi = box
    outside = [[p for p in points if p.x >= bhi.x], [p for p in points if p.x <= blo.x],
               [p for p in points if p.y >= bhi.y], [p for p in points if p.y <= blo.y],
               [p for p in points if p.z <= blo.z], [p for p in points if p.z >= bhi.z]]
    inside = len(points) - len({id(p) for part in outside for p in part})
    if inside:
        print("  kept clear: %d vertices of %s" % (inside, label))
    for i, part in enumerate(outside):
        carve(v, part, clear, "%s/%d" % (label, i))


def build_collision(v):
    coll = bpy.data.collections.new(COL_COLL)
    v.coll.children.link(coll)
    stair_box, ramp_deg = stair_ramp(v)
    clear = list(v.rooms) + ([stair_box] if stair_box else [])
    special = {
        "Mesh_Neck_Bulkhead": lambda o: plate_with_door(v, o, 1.5, o.name),
        "Mesh_Hold_Bulkhead": lambda o: plate_with_door(v, o, 1.5, o.name),
        "Mesh_RearHull_Stern": lambda o: plate_with_port(v, o, o.name),
        "neck": lambda o: arch_sectors(v, o, o.name),
        "Mesh_BridgeWalls": lambda o: bow_band(v, o, o.name),
        "Mesh_RearHull_WallR": lambda o: side_walls(v, o, o.name),
        "Mesh_RearHull_WallL": lambda o: side_walls(v, o, o.name),
        "Mesh_RearHull_CastleWallR": lambda o: side_walls(v, o, o.name),
        "Mesh_RearHull_CastleWallL": lambda o: side_walls(v, o, o.name),
        "Mesh_RearHull_CastleStep": lambda o: v.split_hulls(o, [lambda p: p.x <= -1.9, lambda p: p.x >= 1.9, lambda p: p.z >= 4.1], o.name),
        "Mesh_RearHull_Collar": lambda o: v.split_hulls(o, [lambda p: p.x <= -2.0, lambda p: p.x >= 2.0,
                                                            lambda p: p.z >= max(q.z for q in world_verts(o)) - 0.6], o.name),
    }
    floors = set(v.floors())
    for o in v.structure():
        b = base(o.name)
        if b.startswith(NO_COLLISION) or b.startswith("Mesh_HullBelly"):   # the belly has its own rule, below
            continue
        if o in floors:
            # whole, always: the stair's foot sinks into the hold floor, and the stairwell rule below cut
            # the slab into flat slivers that were dropped — a hold with no floor at all
            v.island(world_verts(o), o.name)
            continue
        handler = next((f for k, f in special.items() if b == k or b.startswith(k)), None)
        if handler is not None:
            handler(o)
            continue
        carve(v, world_verts(o), clear, o.name)
    for g in v.track_groups():
        parts = [o for o in g.all_objects if o.type == 'MESH']
        for o in parts:
            if "_Fender_" in o.name:
                v.island(world_verts(o), o.name)
        track_boxes(v, g, parts)
    # the belly: under the floors only, in short segments. It rises through the floor at the stern, and
    # one hull over all of it would be an invisible block standing on the hold floor.
    clip = min(bounds(world_verts(f))[1].z for f in v.floors()) - 0.02
    for o in v.named("Mesh_HullBelly"):
        vs = [p for p in world_verts(o) if p.z <= clip]
        if vs:
            y0, y1 = min(p.y for p in vs), max(p.y for p in vs)
            cuts = frange(y0, y1, WALL_SEGMENT)
            for a, b in zip(cuts, cuts[1:]):
                seg = [p for p in vs if a - PROBE <= p.y <= b + PROBE]
                if len(seg) >= 4:
                    v.island(seg, "%s/%.1f" % (o.name, a))
    # one box per cluster of touching parts within one furniture collection. Not the whole collection: the
    # user nests and merges them (one held all 26 engine parts, and its box spanned the stair corridor). Not
    # across collections either: an engine touching the jerrican beside it boxed the free corner between them.
    for c in [c for top in (v.interior, v.exterior) for c in v.groups(top)]:
        if group_base(c.name).startswith(FURNITURE):
            for cluster in touching_clusters([o for o in c.objects if o.type == 'MESH']):
                v.box(*bounds([p for o in cluster for p in world_verts(o)]), cluster[0].name)
    for o in v.meshes:
        if base(o.name).startswith(SOLID_PROPS):
            v.island(world_verts(o), o.name)
    print("  %d collision island(s)%s" % (v.col_index, "; stair ramp %.1f deg" % ramp_deg if ramp_deg else "; no stair"))


# ── set-down spots ─────────────────────────────────────────────────────────────

class Obstacles:
    """The collision islands, and the shut door leaves and hatch lids (Unity gives those their colliders),
    for asking whether the player's standing body fits at a spot. A ladder's step-off and either end of a
    hatch crawl set the body down; set down inside a collider, physics shoves it out sideways - through the
    hull, or off the fender."""

    def __init__(self, leaves):
        self.islands = []
        verts, faces = [], []
        for pts in [[v.co.copy() for v in o.data.vertices] for o in bpy.data.collections[COL_COLL].objects]                 + [world_verts(o) for o in leaves]:
            b = bmesh.new()
            for p in pts:
                b.verts.new(p)
            bmesh.ops.convex_hull(b, input=list(b.verts))
            b.normal_update()
            c = sum(pts, Vector()) / len(pts)
            planes = []
            for f in b.faces:
                n, d = f.normal.copy(), f.normal.dot(f.verts[0].co)
                planes.append((n, d) if n.dot(c) <= d else (-n, -d))
            off = len(verts)
            verts += [v.co.copy() for v in b.verts]
            faces += [[off + v.index for v in f.verts] for f in b.faces]
            b.free()
            self.islands.append((bounds(pts), c, planes))
        self.bvh = BVHTree.FromPolygons(verts, faces)

    def blocks(self, feet):
        # from the centre of the capsule's rounded foot up: a flat-bottomed body would dig into a sloped floor
        # (the curved fender under a hatch) that the capsule stands on
        base_z, top_z = feet.z + BODY_RADIUS, feet.z + BODY_HEADROOM
        ring = [Vector((feet.x + BODY_RADIUS * math.cos(a), feet.y + BODY_RADIUS * math.sin(a), 0))
                for a in (2 * math.pi * k / BODY_SIDES for k in range(BODY_SIDES))]
        verts = [Vector((p.x, p.y, base_z)) for p in ring] + [Vector((p.x, p.y, top_z)) for p in ring]
        n = len(ring)
        faces = [[k, (k + 1) % n, n + (k + 1) % n, n + k] for k in range(n)] + [list(range(n)), list(range(2 * n - 1, n - 1, -1))]
        if self.bvh.overlap(BVHTree.FromPolygons(verts, faces)):
            return True
        centre = Vector((feet.x, feet.y, (base_z + top_z) / 2))
        for (lo, hi), c, planes in self.islands:
            if all(nrm.dot(centre) <= d for nrm, d in planes):
                return True                                            # the body inside an island
            if (Vector((c.x, c.y, 0)) - Vector((feet.x, feet.y, 0))).length < BODY_RADIUS and base_z < c.z < top_z:
                return True                                            # an island inside the body
        return False

    def set_down(self, walk, feet, label):
        """The nearest spot to `feet`, on the same floor, with room for the body; an error if there is none."""
        for d in [0.0] + frange(SET_DOWN_STEP, SET_DOWN_SEARCH, SET_DOWN_STEP):
            for k in range(1 if d == 0 else BODY_SIDES):
                a = 2 * math.pi * k / BODY_SIDES
                probe = Vector((feet.x + d * math.cos(a), feet.y + d * math.sin(a), feet.z + SET_DOWN_LEVEL))
                hit = walk.ray_cast(probe, Vector((0, 0, -1)), 2 * SET_DOWN_LEVEL)[0]
                if hit is not None and not self.blocks(hit):
                    if d:
                        print("  %s: slid %.1f m to find room for the body" % (label, d))
                    return hit + Vector((0, 0, MARK_LIFT))
        raise RuntimeError("%s: no room for the player's body within %.1f m of (%.2f, %.2f, %.2f)"
                           % (label, SET_DOWN_SEARCH, feet.x, feet.y, feet.z))


# ── ladders, hatches, rooms ───────────────────────────────────────────────────

def build_ladders(v, walk, obstacles, sills):
    """A LAD_ marker per ladder, except one that climbs from inside to a hatch sill: the crawl's inside stair,
    and the crawl (HatchPassage) is how a player uses it - a Ladder there would step a standing body off
    into the hatch hole."""
    made = []
    for o in sorted(v.named("Mesh_Stair_Ladder_"), key=lambda o: o.name):
        vs = world_verts(o)
        zmin, zmax = min(p.z for p in vs), max(p.z for p in vs)
        band = 0.12
        foot_pts = [p for p in vs if p.z <= zmin + band]; top_pts = [p for p in vs if p.z >= zmax - band]
        foot = sum(foot_pts, Vector()) / len(foot_pts); top = sum(top_pts, Vector()) / len(top_pts)
        lean = Vector((top.x - foot.x, top.y - foot.y, 0))
        if lean.length < 0.05:
            raise RuntimeError("%s: vertical ladder, cannot tell which way it leads" % o.name)
        lean.normalize()
        if any((sill - top).length < SILL_REACH and abs(foot.x) < abs(sill.x) for sill in sills):   # foot inboard
            print("  %s: the inside stair of a hatch; no Ladder" % o.name)
            continue
        probe = top + lean * LADDER_EXIT_PAST_TOP
        hit = walk.ray_cast(Vector((probe.x, probe.y, zmax + 1.0)), Vector((0, 0, -1)), zmax + 3.0)[0]
        if hit is None:
            raise RuntimeError("%s: nothing to step onto past its top" % o.name)
        name = "LAD_%02d" % (len(made) + 1)
        root = v.empty(name, foot)
        v.empty(name + "_Top", Vector((top.x, top.y, zmax)), root)
        v.empty(name + "_Exit", obstacles.set_down(walk, hit, name + " exit"), root)
        made.append("%s (%s)" % (name, o.name))
    print("  ladders: %s" % ", ".join(made))


def build_hatch_marks(v, walk, obstacles):
    sills = []
    for coaming in v.named("Mesh_Hatch_Roof_Coaming_HatchR"):
        lo, hi = bounds(world_verts(coaming))
        s = 1 if lo.x + hi.x > 0 else -1
        side = "R" if s > 0 else "L"
        wall = v.one("Mesh_RearHull_Wall" + side)
        yc = (lo.y + hi.y) / 2
        hz = gap_along(wall, lambda z: Vector((0, yc, z)), Vector((s, 0, 0)), (0.6, 4.1))
        if hz is None:
            raise RuntimeError("hatch %s: no opening in %s" % (side, wall.name))
        inner = object_bvh(wall).ray_cast(Vector((0, hi.y + 0.3, (hz[0] + hz[1]) / 2)), Vector((s, 0, 0)), 5)[0]
        out_hit = walk.ray_cast(Vector((s * max(abs(lo.x), abs(hi.x)) + s * HATCH_OUTSIDE, yc, hz[1] + 1)), Vector((0, 0, -1)), 6)[0]
        in_hit = walk.ray_cast(Vector((inner.x - s * HATCH_INSIDE, yc, hz[0] - 0.05)), Vector((0, 0, -1)), 6)[0]
        if out_hit is None or in_hit is None:
            raise RuntimeError("hatch %s: no floor outside or inside" % side)
        sill = Vector((inner.x + s * 0.1, yc, hz[0] + MARK_LIFT))
        v.empty("HATCH_%s_Outer" % side, obstacles.set_down(walk, out_hit, "hatch %s outer" % side))
        v.empty("HATCH_%s_Sill" % side, sill)
        v.empty("HATCH_%s_Inner" % side, obstacles.set_down(walk, in_hit, "hatch %s inner" % side))
        sills.append(sill)
        print("  hatch %s: opening z %.2f-%.2f, crawl %.2f -> %.2f -> %.2f" % (side, hz[0], hz[1], out_hit.z, hz[0], in_hit.z))
    return sills


def build_rooms(v):
    """Each walkable room as a box, measured rather than guessed. From every floor-like piece (anything
    broad and flat-ish in the interior or the structure), rays go straight up to the first piece of
    structure above; the spots are grouped by that ceiling's height, and a group is a room only if rays
    from its middle hit a wall on both sides — which rejects open space, like the main roof under the
    castle's overhang. InteriorReveal draws the fit-out for a camera in any of these."""
    struct = [o for o in v.structure() if not base(o.name).startswith(NO_COLLISION)]
    above = bvh_of([o for o in struct if not base(o.name).startswith("Mesh_HullBelly")])   # it rises through floors
    candidates = struct + [o for o in bpy.data.collections[v.interior].all_objects if o.type == 'MESH']
    rooms = []
    for f in candidates:
        lo, hi = bounds(world_verts(f))
        sx, sy, sz = hi.x - lo.x, hi.y - lo.y, hi.z - lo.z
        if sx * sy < MIN_ROOM_AREA or sz > FLAT_RATIO * min(sx, sy):
            continue
        clusters = {}
        for x in frange(lo.x + 0.2, hi.x - 0.2, ROOM_STEP):
            for y in frange(lo.y + 0.2, hi.y - 0.2, ROOM_STEP):
                hit = above.ray_cast(Vector((x, y, hi.z + 0.05)), Vector((0, 0, 1)), MAX_ROOM_H)[0]
                if hit is not None and hit.z - hi.z > MIN_ROOM_H:
                    clusters.setdefault(round(hit.z / CEILING_BUCKET), []).append((x, y, hit.z))
        for pts in clusters.values():
            if len(pts) < 4:
                continue
            x0, x1 = min(p[0] for p in pts), max(p[0] for p in pts)
            y0, y1 = min(p[1] for p in pts), max(p[1] for p in pts)
            top = min(p[2] for p in pts)
            mid = Vector(((x0 + x1) / 2, (y0 + y1) / 2, (hi.z + top) / 2))
            if any(above.ray_cast(mid, Vector((s, 0, 0)), ENCLOSED)[0] is None for s in (1, -1)):
                continue
            box = (Vector((x0 - ROOM_STEP / 2, y0 - ROOM_STEP / 2, hi.z)), Vector((x1 + ROOM_STEP / 2, y1 + ROOM_STEP / 2, top)))
            if any(r[0].x <= box[0].x + 0.3 and r[1].x >= box[1].x - 0.3 and r[0].y <= box[0].y + 0.3 and r[1].y >= box[1].y - 0.3
                   and r[0].z <= box[0].z + 0.3 and r[1].z >= box[1].z - 0.3 for r in rooms):
                continue           # already inside a room found from a lower floor
            rooms.append(box)
    v.rooms = rooms
    for n, (lo, hi) in enumerate(rooms, 1):
        v.empty("VOL_%02d_Min" % n, lo)
        v.empty("VOL_%02d_Max" % n, hi)
        print("  room %d: x %.1f..%.1f  y %.1f..%.1f  z %.2f..%.2f" % (n, lo.x, hi.x, lo.y, hi.y, lo.z, hi.z))
    if not rooms:
        raise RuntimeError("%s: no rooms found" % v.key)


def write_groups(v, path):
    def members(name):
        return sorted({o.name for o in bpy.data.collections[name].all_objects if o.type == 'MESH'})
    groups = {"interior": members(v.interior), "exterior": members(v.exterior)}
    loose = sorted({o.name for o in v.meshes if not o.name.startswith("COL_")} - set(groups["interior"]) - set(groups["exterior"]))
    if loose:
        raise RuntimeError("meshes in neither %s nor %s: %s" % (v.interior, v.exterior, ", ".join(loose)))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(groups, fh, indent=1)
    print("  groups: %d interior, %d exterior -> %s" % (len(groups["interior"]), len(groups["exterior"]), path))


def export_variant(key):
    coll_name, _, _, stem = VARIANTS[key]
    dst = unity_path("Vehicles", "DuneBarge", stem + ".fbx")
    groups = unity_path("Vehicles", "DuneBarge", stem + "_groups.json")

    def prepare():
        bpy.context.view_layer.update()
        v = Variant(key)
        walk = bvh_of(v.walkable())
        write_groups(v, groups)
        build_rooms(v)
        build_collision(v)
        obstacles = Obstacles(v.named("Mesh_Door_Bulkhead_Leaf") + v.named("Mesh_Hatch_Roof_Lid"))   # marks go where the body fits
        sills = build_hatch_marks(v, walk, obstacles)
        build_ladders(v, walk, obstacles, sills)

    print("== %s (%s)" % (key, coll_name))
    export(SRC, dst, keep_armature=True, keep_empties=True, fix_inverted=True, keep_collection=coll_name, prepare=prepare)


if __name__ == "__main__":
    only = _ARGS[_ARGS.index("--only") + 1:] if "--only" in _ARGS else list(VARIANTS)
    for key in only:
        export_variant(key)
