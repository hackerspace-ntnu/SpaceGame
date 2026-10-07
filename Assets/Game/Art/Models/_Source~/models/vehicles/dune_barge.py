"""Build models/vehicles/dune_barge.blend — a rusted tracked land-ship with a walkable interior.

    blender --background --python dune_barge.py -- --out dune_barge.blend

From the concept image of a riveted desert crawler: a long banded hull on two pairs of tracks, a
rounded bridge perched on the bow with a band of windows and a row of searchlights, an open roof
deck behind it, cables slung along the sides. Walkable for the 3 m player (see dune_barge_hull.py
for the deck heights), with an interior: crew berths and lockers forward, a mess and galley
amidships, two diesels and a generator in the engine room, a cargo bay with a stern ramp, and
the helm on the bridge.

Assembled, not modelled: the hull shell is this model's own geometry (dune_barge_hull.py), and
everything else is a library part appended from components/ (see dune_barge_BUILD.md). Parts
built for 2 m people (seats, consoles, railings, lights, stacks) are placed at x1.5 to suit the
3 m crew.

## Rig (Arm_DuneBarge)

- Bone_Chassis — root.
- Bone_Pod_L / Bone_Pod_R — the front track pods steer about a vertical axis.
- Bone_Wheel_<unit>_<kind><n> — every sprocket, idler, road wheel and return roller spins
  about its own axle (X). Pod wheels are children of their pod bone.
- Bone_Door_Boarding / _Engine / _Cargo / _Bridge (vertical hinges), Bone_Hatch_Roof and
  Bone_Ramp_Cargo (hinges along X). Rest pose is CLOSED.
- Track links are separate objects parented to their pod bone or the chassis. They are laid
  on the belt at rest; making them travel is a runtime job (move each along the belt line
  computed by dune_barge_tracks.links), not a bone per link.

Generation script — historical record. The .blend is the source of truth.
"""

import math
import os
import random
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path[:0] = [os.path.abspath(os.path.join(HERE, "..", "..")), HERE]

import bpy
from mathutils import Matrix, Vector

import _buildlib as B
import dune_barge_hull as H
import dune_barge_tracks as T

COMP = os.path.join(B.LIB_ROOT, "components")
HUMAN_TO_CREW = 1.5          # library props sized for 2 m people, placed for the 3 m crew
SEED = 7                     # link variation and prop jitter are deterministic
ENGINE_Y = 5.4               # diesels' origin: clear of the engine-room bulkhead at y 3.5

MATERIALS = {
    "hull": "Mat_Metal_HullRust_Orange", "strap": "Mat_Metal_Rust_Deep", "deck": "Mat_Metal_Steel_Dark",
    "roof": "Mat_Metal_Rust_Deep", "glass": "Mat_Glass_Canopy_Tinted", "iron": "Mat_Metal_Steel_Dark",
    "cable": "Mat_Plastic_Rubber_Black",
}
MAT_ORDER = list(MATERIALS)
M = {k: i for i, k in enumerate(MAT_ORDER)}

TURN_FORWARD = Matrix.Rotation(math.radians(-90), 4, 'Z')   # library seats/consoles face +X; bow is -Y


def rz(deg):
    return Matrix.Rotation(math.radians(deg), 4, 'Z')


def at(x, y, z, rot=None):
    return Matrix.Translation((x, y, z)) @ (rot or Matrix.Identity(4))


# ── library kits ─────────────────────────────────────────────────────────────

class Kit:
    """One library variation, loaded once, placed as many times as needed.

    Copies share mesh data with the loaded prototype, so twenty track idlers cost one mesh.
    A scaled kit gets its own scaled meshes once, at load — never per placement.
    """
    _cache = {}

    def __init__(self, rel, coll, scale=1.0):
        path = os.path.join(COMP, rel)
        with bpy.data.libraries.load(path, link=False) as (src, dst):
            if coll not in src.collections:
                raise SystemExit("No %s in %s" % (coll, rel))
            dst.collections = [coll]
        c = dst.collections[0]
        self.objs = list(c.all_objects)
        self.worlds = {o: o.matrix_world.copy() for o in self.objs}
        if scale != 1.0:
            S = Matrix.Scale(scale, 4)
            for o in self.objs:
                if o.type == 'MESH':
                    o.data = o.data.copy()
                    o.data.transform(Matrix.Scale(scale, 4))
                w = self.worlds[o]
                self.worlds[o] = Matrix.Translation(w.translation * scale) @ w.to_quaternion().to_matrix().to_4x4()
        self.name = coll.replace("Coll_", "")

    @classmethod
    def get(cls, rel, coll, scale=1.0):
        key = (rel, coll, scale)
        if key not in cls._cache:
            cls._cache[key] = Kit(rel, coll, scale)
        return cls._cache[key]

    def place(self, matrix, tag, into, mirror_x=False):
        """Copy the kit to `matrix`; returns {prototype name: copy}. `mirror_x` for port-side parts."""
        copies = {}
        for o in self.objs:
            n = o.copy()
            if mirror_x and o.type == 'MESH':
                n.data = mirrored(o.data)
            n.name = "%s_%s" % (o.name, tag)
            into.objects.link(n)
            copies[o] = n
        flip = Matrix.Scale(-1, 4, (1, 0, 0)) if mirror_x else Matrix.Identity(4)
        for o, n in copies.items():
            n.parent = None
            w = self.worlds[o]
            if mirror_x:     # mirrored data carries the flip; mirror the pose, keep a proper rotation
                w = flip @ w @ flip
            n.matrix_world = matrix @ w
        bpy.context.view_layer.update()
        for o, n in copies.items():
            if o.parent in copies:
                p = copies[o.parent]
                w = n.matrix_world.copy()
                n.parent = p
                n.matrix_parent_inverse = p.matrix_world.inverted()
                n.matrix_world = w
        return {o.name: n for o, n in copies.items()}


_mirror_cache = {}


def mirrored(mesh):
    """A copy of `mesh` mirrored in X with its winding flipped, so normals stay outward."""
    if mesh.name not in _mirror_cache:
        m = mesh.copy()
        m.transform(Matrix.Scale(-1, 4, (1, 0, 0)))
        m.flip_normals()
        _mirror_cache[mesh.name] = m
    return _mirror_cache[mesh.name]


# ── hull ─────────────────────────────────────────────────────────────────────

def hull(coll, mats):
    def part(name, fn, *args, **kw):
        P = B.Part(mats)
        fn(P, *args, M, **kw) if args else fn(P, M, **kw)
        return P.finish(name, coll)

    objs = []
    for side, t in ((-1, "L"), (1, "R")):
        objs.append(part("Mesh_HullSide" + t, H.side_wall, side))
        objs.append(part("Mesh_HullStraps" + t, H.side_straps, side))
    objs.append(part("Mesh_BowWall", H.end_wall, H.BOW_Y))
    objs.append(part("Mesh_SternWall", H.end_wall, H.STERN_Y,
                     opening=(-H.RAMP_W / 2, H.RAMP_W / 2, H.FLOOR_Z + H.RAMP_H)))
    objs.append(part("Mesh_BulkheadEngine", H.bulkhead, H.BULKHEAD_ENGINE_Y))
    objs.append(part("Mesh_BulkheadCargo", H.bulkhead, H.BULKHEAD_CARGO_Y))
    for fn, n in ((H.floor_slab, "Mesh_DeckFloor"), (H.ceiling_slab, "Mesh_DeckCeiling"),
                  (H.belly, "Mesh_HullBelly"), (H.prow, "Mesh_HullProw"),
                  (H.bridge_walls, "Mesh_BridgeWalls"), (H.bridge_roof, "Mesh_BridgeRoof"),
                  (H.bridge_glass, "Mesh_BridgeGlass")):
        objs.append(part(n, fn))
    return objs


def cables(coll, mats):
    """Cables slung along both sides in shallow catenaries, as in the image. One object per side."""
    objs = []
    for side, t in ((-1, "L"), (1, "R")):
        P = B.Part(mats)
        x = side * (H.HALF_W + H.WALL_T + 0.09)
        for z0, sag, y0, y1 in ((6.55, 0.35, -9.8, 9.8), (6.35, 0.55, -8.0, 4.0), (4.1, 0.25, 4.3, 9.9)):
            span = 4.0
            y = y0
            while y < y1 - 0.1:
                y_next = min(y + span, y1)
                pts = []
                for i in range(9):
                    u = i / 8
                    yy = y + (y_next - y) * u
                    pts.append(Vector((x, yy, z0 - sag * 4 * u * (1 - u))))
                for a, b in zip(pts, pts[1:]):
                    P.segment(a, b, [(0, 0.045, 0.045), (1, 0.045, 0.045)], mat=M["cable"], ring=6, dome=False)
                P.cyl((x, y_next, z0), 0.06, 0.12, axis='X', seg=6, mat=M["iron"])   # clamp
                y = y_next
        objs.append(P.finish("Mesh_Cables" + t, coll))
    return objs


def posts(coll, mats, points, height, name):
    """Plain steel posts the searchlights stand on (the image's light masts)."""
    P = B.Part(mats)
    for x, y, z in points:
        P.cyl((x, y, z + height / 2), 0.07, height, seg=8, mat=M["iron"])
        P.cyl((x, y, z + 0.03), 0.22, 0.06, seg=8, mat=M["iron"])
    return P.finish(name, coll)


def exhaust_pipes(coll, mats, runs):
    P = B.Part(mats)
    for a, b in runs:
        P.segment(a, b, [(0, 0.16, 0.16), (1, 0.16, 0.16)], mat=M["strap"], ring=10)
    return P.finish("Mesh_ExhaustPipes", coll)


# ── tracks ───────────────────────────────────────────────────────────────────

def track_unit(coll, unit, wheels, x, mirror, rng):
    """Wheels, links, fenders and suspension for one track unit. Returns (objects, wheel bones)."""
    link_kits = [Kit.get("mechanical/track_link.blend", "Coll_Link_" + v)
                 for v in ("Grouser", "Grouser", "Grouser", "Worn", "Grouser", "Chevron", "Grouser", "Padded")]
    wheel_kit = {"Sprocket": Kit.get("mechanical/track_wheel.blend", "Coll_Wheel_Sprocket"),
                 "Idler": Kit.get("mechanical/track_wheel.blend", "Coll_Wheel_Idler"),
                 "Road": Kit.get("mechanical/track_wheel.blend", "Coll_Wheel_Idler"),
                 "Roller": Kit.get("mechanical/track_wheel.blend", "Coll_Wheel_ReturnRoller")}
    placed, wheel_bones = [], []
    count = {}
    for kind, y, z in wheels:
        count[kind] = count.get(kind, 0) + 1
        tag = "%s_%s%d" % (unit, kind, count[kind])
        # the return roller's bracket is on its +X side and must reach the hull, i.e. inboard
        flip = (not mirror) if kind == "Roller" else mirror
        objs = wheel_kit[kind].place(at(x, y, z), tag, coll, mirror_x=flip)
        placed += list(objs.values())
        wheel_bones.append(("Bone_Wheel_" + tag, Vector((x, y, z)), list(objs.values())))

    poses, _, _ = T.links(wheels)
    for i, (y, z, ang) in enumerate(poses):
        kit = link_kits[rng.randrange(len(link_kits))] if i % 3 == 0 else link_kits[0]
        # Link frame: +Y along the belt, +Z into the loop (inner face). The belt runs
        # counter-clockwise in (y, z), so the inward normal is the tangent turned +90 deg.
        rot = Matrix.Rotation(ang, 4, 'X')
        objs = kit.place(at(x, y, z) @ rot, "%s_%03d" % (unit, i), coll, mirror_x=mirror)
        placed += list(objs.values())
    return placed, wheel_bones


def suspension(coll, x, side, rng):
    """Main-unit bogies: a girder inboard of the wheels, swing arms to each road wheel, a gear
    housing hung outboard with an exposed gear — the image's visible running gear."""
    placed = []
    mirror = side < 0
    beam = Kit.get("mechanical/track_bogie.blend", "Coll_Bogie_Beam")
    arm = Kit.get("mechanical/track_bogie.blend", "Coll_Bogie_SwingArm")
    housing = Kit.get("mechanical/track_bogie.blend", "Coll_Bogie_GearHousing")
    gear = Kit.get("mechanical/track_wheel.blend", "Coll_Wheel_Gear")
    xb = side * (abs(x) - 0.9)                  # inboard of the track, clear of the belly
    for i, y in enumerate((9.2, 7.2, 5.2, 3.2, 1.2, -0.8)):
        placed += list(beam.place(at(xb, y, 1.35), "%s%d" % ("L" if mirror else "R", i), coll, mirror_x=mirror).values())
    for kind, y, z in T.MAIN_WHEELS:
        if kind != "Road":
            continue
        placed += list(arm.place(at(xb, y + 1.0, z + 0.06), "%s_%.0f" % ("L" if mirror else "R", y * 10),
                                 coll, mirror_x=mirror).values())
    xo = side * (abs(x) + 0.95)
    placed += list(housing.place(at(xo, 3.0, 0.95), "L" if mirror else "R", coll, mirror_x=mirror).values())
    placed += list(gear.place(at(xo + side * 0.36, 3.0, 1.45), "L" if mirror else "R", coll, mirror_x=mirror).values())
    return placed


def fenders(coll, x, side, pod):
    placed = []
    mirror = side < 0
    t = "L" if mirror else "R"
    if pod:
        k = Kit.get("structural/track_fender.blend", "Coll_Fender_Curved")
        placed += list(k.place(at(x, -8.2, 2.5), "Pod" + t, coll, mirror_x=mirror).values())
        return placed
    order = [("Curved", -1.2), ("Flat", 1.8), ("Torn", 4.8), ("Flat", 7.8), ("Flat", 10.8)]
    for variant, y in order:
        k = Kit.get("structural/track_fender.blend", "Coll_Fender_" + variant)
        placed += list(k.place(at(x, y, 2.55), "%s%.0f" % (t, y * 10), coll, mirror_x=mirror).values())
    skirt = Kit.get("structural/track_fender.blend", "Coll_Fender_Skirt")
    for y in (3.0, 7.5):          # the one at y 3.0 carries the gear housing
        placed += list(skirt.place(at(side * (abs(x) + 0.72), y, 2.5), "%s%.0f" % (t, y * 10),
                                   coll, mirror_x=mirror).values())
    return placed


# ── outfitting ───────────────────────────────────────────────────────────────

def outfit(coll, mats, rng):
    """Doors, stairs, interior and roof. Returns (objects, door hinges for the rig)."""
    placed, hinges = [], []
    F, R = H.FLOOR_Z, H.ROOF_Z
    s = HUMAN_TO_CREW

    def put(rel, c, matrix, tag, scale=1.0, mirror=False):
        objs = Kit.get(rel, c, scale).place(matrix, tag, coll, mirror_x=mirror)
        placed.extend(objs.values())
        return objs

    # doors — frames at the openings, leaves hinged; rest pose closed
    wx = -(H.HALF_W + H.WALL_T / 2)
    d = put("structural/hull_door.blend", "Coll_Door_Boarding", at(wx, H.BOARDING_Y, F, rz(-90)), "Boarding")
    hinges.append(("Bone_Door_Boarding", d["Mesh_Door_Boarding_Leaf"], 'Z'))
    for y, tag in ((H.BULKHEAD_ENGINE_Y, "Engine"), (H.BULKHEAD_CARGO_Y, "Cargo")):
        d = put("structural/hull_door.blend", "Coll_Door_Bulkhead", at(0, y, F), tag)
        hinges.append(("Bone_Door_" + tag, d["Mesh_Door_Bulkhead_Leaf"], 'Z'))
    d = put("structural/hull_door.blend", "Coll_Door_Bulkhead", at(-1.5, H.BRIDGE_REAR_Y + H.WALL_T / 2, R, rz(180)), "Bridge")
    hinges.append(("Bone_Door_Bridge", d["Mesh_Door_Bulkhead_Leaf"], 'Z'))
    hx, hy = H.ROOF_HATCH
    d = put("structural/hull_door.blend", "Coll_Hatch_Roof", at(hx, hy, R), "Roof")
    hinges.append(("Bone_Hatch_Roof", d["Mesh_Hatch_Roof_Lid"], 'X'))
    d = put("structural/hull_door.blend", "Coll_Ramp_Cargo",
            at(0, H.STERN_Y + H.WALL_T / 2 + 0.05, F) @ Matrix.Rotation(math.radians(90), 4, 'X'), "Stern")
    hinges.append(("Bone_Ramp_Cargo", d["Mesh_Ramp_Cargo_Ramp"], 'X'))

    # stairs: flight, landing, flight up the stairwell to the bridge
    sx = (H.STAIRWELL[0] + H.STAIRWELL[1]) / 2
    put("structural/stair_flight.blend", "Coll_Stair_Grating", at(sx, 1.2, F), "Lower")
    put("structural/stair_flight.blend", "Coll_Stair_Landing", at(sx, 1.2 - 3.25 - 0.9, F + 2.1), "Mid")
    put("structural/stair_flight.blend", "Coll_Stair_Plate", at(sx, 1.2 - 3.25 - 1.8, F + 2.1), "Upper")
    # ladder under the roof hatch, stretched to the 4.2 m deck-to-roof rise
    put("structural/stair_flight.blend", "Coll_Stair_Ladder", at(hx, hy + 0.9, F), "Hatch", scale=4.2 / 3.0)

    # forward berths
    for i, y in enumerate((-9.45, -7.85)):
        put("props/crew_quarters.blend", "Coll_Bunk_Double", at(-1.9, y, F, rz(180)), "B%d" % i)
    for i, y in enumerate((-5.9, -4.5)):
        put("props/crew_quarters.blend", "Coll_Locker_Tall", at(-3.45, y, F, rz(90)), "L%d" % i)
    put("props/crew_quarters.blend", "Coll_Bunk_Hammock", at(-1.4, -5.2, F), "H")

    # mess and galley amidships
    put("props/crew_quarters.blend", "Coll_Table_Mess", at(0.4, 0.9, F), "Mess")
    for i, y in enumerate((-0.05, 1.85)):
        put("props/crew_seat.blend", "Coll_CrewSeat_Bench", at(0.4, y, F, rz(-90 if i else 90)), "M%d" % i, scale=s)
    put("props/crew_quarters.blend", "Coll_Galley_Stove", at(-3.3, 2.5, F, rz(90)), "Galley")

    # engine room: two diesels flanking a generator, stacks through the roof
    for x, t in ((-2.35, "L"), (2.35, "R")):
        put("mechanical/engine_block.blend", "Coll_Engine_DieselV8", at(x, ENGINE_Y, F), "Eng" + t)
    put("mechanical/engine_block.blend", "Coll_Engine_Generator", at(0.0, 5.9, F), "Gen")
    stacks = [(-3.05, ENGINE_Y + 1.07), (3.05, ENGINE_Y + 1.07)]   # outer manifold risers
    placed.append(exhaust_pipes(coll, mats, [((x, y, F + 1.73), (x, y, R + 0.3)) for x, y in stacks]))
    for (x, y), t in zip(stacks, "LR"):
        put("mechanical/exhaust_stack.blend", "Coll_ExhaustStack_Cowl", at(x, y, R + 0.25), "Stack" + t)

    # cargo bay
    for rel, c, m, t in (("props/supply_crate.blend", "Coll_Crate_Large", at(1.9, 9.3, F, rz(8)), "C1"),
                         ("props/supply_crate.blend", "Coll_Crate_Small", at(2.2, 9.4, F + 1.03, rz(-14)), "C2"),
                         ("props/supply_crate.blend", "Coll_Crate_Long", at(-0.2, 9.9, F, rz(90)), "C3"),
                         ("props/cargo_bundle.blend", "Coll_CargoBundle_Rug", at(0.3, 8.9, F), "C4")):
        put(rel, c, m, t)

    # bridge: helm across the front, pilot and copilot behind it, a nav station to port
    put("props/console_panel.blend", "Coll_ConsolePanel_Helm", at(0, -9.2, R, TURN_FORWARD), "Helm", scale=s)
    put("props/crew_seat.blend", "Coll_CrewSeat_Pilot", at(-0.9, -7.7, R, TURN_FORWARD), "Pilot", scale=s)
    put("props/crew_seat.blend", "Coll_CrewSeat_Copilot", at(0.9, -7.7, R, TURN_FORWARD), "Copilot", scale=s)
    put("props/console_panel.blend", "Coll_ConsolePanel_Nav", at(-2.6, -5.2, R, rz(90)), "Nav", scale=s)

    # roof: searchlights on posts across the bridge roof, railings, masts
    top = H.BRIDGE_TOP + 0.22
    lights = [(-2.4, -8.6), (-1.2, -9.4), (0.0, -9.6), (1.2, -9.4), (2.4, -8.6)]
    placed.append(posts(coll, mats, [(x, y, top) for x, y in lights], 1.0, "Mesh_LightPosts"))
    for i, (x, y) in enumerate(lights):
        v = "Coll_FloodlightBank_Sweep" if i % 2 else "Coll_FloodlightBank_Single"
        put("props/floodlight_bank.blend", v, at(x, y, top + 1.0 + 0.55 * s, rz(rng.uniform(-12, 12))), "SL%d" % i, scale=s)
    rail_x = H.HALF_W + H.WALL_T - 0.1
    rail_len = 2.24 * s - 0.24
    for side, t in ((-1, "L"), (1, "R")):
        y = H.BRIDGE_REAR_Y + 0.3
        i = 0
        while y + rail_len < H.STERN_Y - 0.2:
            put("structural/handrail.blend", "Coll_Handrail_Straight", at(side * rail_x, y, R), "%s%d" % (t, i), scale=s)
            y += rail_len
            i += 1
    for i, x in enumerate((-3.9, -0.7)):
        put("structural/handrail.blend", "Coll_Handrail_Straight", at(x, H.STERN_Y - 0.15, R, rz(-90)), "S%d" % i, scale=s)
    put("structural/antenna_mast.blend", "Coll_Mast_Whip", at(3.0, -2.6, top), "Whip")
    put("structural/antenna_mast.blend", "Coll_Mast_Dish", at(-3.1, -1.2, R), "Dish")
    put("structural/antenna_mast.blend", "Coll_Mast_Flag", at(3.2, 10.0, R), "Flag")
    put("structural/antenna_mast.blend", "Coll_Mast_Beacon", at(-3.1, -9.4, top), "Beacon")

    # hull dressing: framed side windows, vented plates by the engine room, patches
    variants = ["Slot", "Slot", "Shuttered", "Slot", "Barred", "Slot", "Slot"]
    for side, t in ((-1, "L"), (1, "R")):
        x = side * (H.HALF_W + H.WALL_T + 0.001)
        for i, y in enumerate(H.SIDE_WINDOWS_Y):
            v = variants[(i + (3 if side > 0 else 0)) % len(variants)]
            put("structural/hull_window.blend", "Coll_Window_" + v,
                at(x, y, sum(H.WINDOW_BAND) / 2, rz(90 * side)), "%s%d" % (t, i))
        tile_x = side * (H.HALF_W + H.WALL_T + 0.002)
        for i, (y, z, v) in enumerate(((4.9, 4.9, "Vented"), (5.9, 4.9, "Vented"))):   # engine-room vents
            # hull_plate tiles: plate normal +Z, extending +X and +Y from a corner origin.
            # Turned about Y so +Z faces outward; the tile then hangs DOWN from z.
            put("structural/hull_plate.blend", "Coll_HullPlate_" + v,
                at(tile_x, y, z, Matrix.Rotation(math.radians(90 * side), 4, 'Y')), "%s%d" % (t, i))
    return placed, hinges


# ── rig ──────────────────────────────────────────────────────────────────────

def rig(coll, chassis, pods, wheel_bones, hinges):
    arm = bpy.data.objects.new("Arm_DuneBarge", bpy.data.armatures.new("Arm_DuneBarge"))
    coll.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    root = eb.new("Bone_Chassis")
    root.head, root.tail = (0, 0, 0), (0, 0, 1.0)
    for name, pivot in pods:
        b = eb.new(name)
        b.head, b.tail, b.parent = pivot, pivot + Vector((0, 0, 1.0)), root
    for name, hub, _, parent in wheel_bones:
        b = eb.new(name)
        b.head, b.tail, b.parent = hub, hub + Vector((0.5, 0, 0)), eb[parent]
    for name, leaf, axis in hinges:
        b = eb.new(name)
        p = leaf.matrix_world.translation
        b.head = p
        b.tail = p + (Vector((0, 0, 1.0)) if axis == 'Z' else Vector((0.6, 0, 0)))
        b.parent = root
    bpy.ops.object.mode_set(mode='OBJECT')

    def bind(obj, bone):
        b = arm.data.bones[bone]
        rest = arm.matrix_world @ b.matrix_local @ Matrix.Translation((0, b.length, 0))
        w = obj.matrix_world.copy()
        obj.parent, obj.parent_type, obj.parent_bone = arm, 'BONE', bone
        obj.matrix_parent_inverse = rest.inverted()
        obj.matrix_world = w

    bound = set()
    for name, _, objs, _ in wheel_bones:
        for o in objs:
            if o.parent is None:
                bind(o, name)
            bound.add(o)
    for name, leaf, _ in hinges:
        bind(leaf, name)
        bound.add(leaf)
    for obj, bone in chassis:
        if obj not in bound and obj.parent is None:
            bind(obj, bone)
    return arm


# ── build ────────────────────────────────────────────────────────────────────

def main():
    out = B.parse_out()
    B.start(out)
    rng = random.Random(SEED)
    mats = B.link_materials([MATERIALS[k] for k in MAT_ORDER])
    coll = B.collection("Coll_DuneBarge")

    chassis = [(o, "Bone_Chassis") for o in hull(coll, mats) + cables(coll, mats)]
    wheel_bones = []
    pods = []
    for side, t in ((-1, "L"), (1, "R")):
        mirror = side < 0
        # main tracks — on the chassis
        objs, wb = track_unit(coll, "Main" + t, T.MAIN_WHEELS, side * T.MAIN_X, mirror, rng)
        wheel_bones += [(n, hub, o, "Bone_Chassis") for n, hub, o in wb]
        wheel_objs = {id(x) for _, _, os_, _ in wheel_bones for x in os_}
        chassis += [(o, "Bone_Chassis") for o in objs if id(o) not in wheel_objs]
        chassis += [(o, "Bone_Chassis") for o in suspension(coll, side * T.MAIN_X, side, rng)]
        chassis += [(o, "Bone_Chassis") for o in fenders(coll, side * T.MAIN_X, side, pod=False)]
        # front pods — everything on the pod bone, which steers
        pod = "Bone_Pod_" + t
        pods.append((pod, Vector((side * T.POD_X, T.POD_PIVOT_Y, 1.2))))
        objs, wb = track_unit(coll, "Pod" + t, T.POD_WHEELS, side * T.POD_X, mirror, rng)
        wheel_bones += [(n, hub, o, pod) for n, hub, o in wb]
        wheel_objs = {id(x) for _, _, os_, _ in wheel_bones for x in os_}
        chassis += [(o, pod) for o in objs if id(o) not in wheel_objs]
        chassis += [(o, pod) for o in fenders(coll, side * T.POD_X, side, pod=True)]

    placed, hinges = outfit(coll, mats, rng)
    chassis += [(o, "Bone_Chassis") for o in placed]
    rig(coll, chassis, pods, wheel_bones, hinges)

    # every copy shares its kit's meshes; the prototypes and their appended collections go
    for kit in Kit._cache.values():
        for o in kit.objs:
            bpy.data.objects.remove(o)
    for c in list(bpy.data.collections):
        if c is not coll and c.name not in bpy.context.scene.collection.children:
            bpy.data.collections.remove(c)
    tris = sum(B.tri_count(o) for o in coll.objects if o.type == 'MESH')
    print("DUNE BARGE: %d objects, %d triangles" % (len(coll.objects), tris))
    B.save(out)


main()
