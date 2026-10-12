"""Export workshop.blend -> the Workshop FBX set and its layout file. Reads only: the .blend is never saved.

    blender --background --python workshop_export.py
    WORKSHOP_SRC=/path/to/other.blend blender --background --python workshop_export.py

Writes to Assets/Game/Art/Models/Environment/Structures/Workshop/:

  workshop.fbx            every part that never moves, joined into chunks of <= ~45k vertices
  workshop_moving.fbx     one node per thing game code moves: every turning room (with its convex collision
                          pieces as COLT_* children), every shaft / gear / wheel ("spinner"), the lift, bridge,
                          trolley and hook of the hoist and of the engine hall gantry ("slider"), the hoist cables,
                          the gantry chain, the control levers and the gate's two leaves ("hinge", each with one
                          COLT_ hull)
  workshop_collision.fbx  static collision as COL_* chunks: structure only, stairs as ramps (the pit stairs with
                          their rails and posts), gratings as plates
  workshop_layout.json    what the meshes cannot say: which node spins about which axis at what ratio of which
                          channel, where the wheels / controls / rooms / lamps are, and which tunnel each turning
                          room faces at what angle. Unity axes, prefab space.

Prefab space: origin at the main room's pit-floor centre. Unity = (-x, z, -y) of Blender, and a rotation of +a
about Blender axis n is -a about the converted axis, which is why every ratio here is written already negated.

The gear trains are not listed by hand. Each spinner is a graph node; edges are found from the geometry (two
gears whose pitch circles touch, two shafts on one line, the two gears of a bevel box, the yokes of a U-joint) and
ratios are walked out from the parts whose speed is known. A part the walk does not reach is reported, not guessed.

A turning room is an empty named `*_Pivot_Turn` carrying a `workshop_wheel` property (written by whatever built the
room): its id, the tunnels it can face and how far it turns to face each, where its wheel and pinion are.
"""
import bpy, bmesh, json, math, os, re, sys
from collections import defaultdict, deque
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.abspath(os.path.join(HERE, '..', '..')))
import _exportlib

SRC = os.environ.get('WORKSHOP_SRC') or os.path.join(HERE, 'workshop.blend')
OUT = _exportlib.unity_path('Environment', 'Structures', 'Workshop')
CHUNK_VERTS = 45000

# The main room keeps the node name it has always had; every other turning room is named for its own number.
def turn_node(name):
    return 'TurntableA' if name == 'MainRoom_Pivot_Turn' else 'Turntable_' + name.split('_')[1]


FORCE_TURN = {'Bay_LandingPad': 'MainRoom_Pivot_Turn', 'Bay_PadCradlesAndBolts': 'MainRoom_Pivot_Turn'}
MESH_UNITS = ('Fan_Rotor', 'Hoist_Cable_L', 'Hoist_Cable_R')
CABLES = {'Hoist_Cable_L': 'Crane', 'Hoist_Cable_R': 'Crane'}
# The main room's hoist, in the order game code counts its axes: pivot -> (channel, the pivot's own axis it travels
# along, metres between stops, stops counting the modelled one). The model stands at stop 0 of all three. It is the
# only crane: the game runs it round its stops by itself until somebody takes its console on the third-floor balcony,
# and then it goes wherever it is driven, as far as its runs allow (`travel`). Its hook step is read off the model.
HOIST = (('Hoist_Pivot_Bridge', 'HoistBridge', (-1, 0, 0), 2.0, 8),
         ('Hoist_Pivot_Trolley', 'HoistTrolley', (0, 1, 0), 2.0, 8),
         ('Hoist_Pivot_HookBlock', 'Crane', (0, 0, -1), None, 5))
# The runways ride up the four tower beams with all they carry, like a printer's gantry. The model stands at the
# lowest stop; the tower caps stop the carriages' rollers 4.69 m higher. The model has no pivot for this: the export
# makes one (`add_lift`) and hangs the runways, their carriages (auto-named in the model) and the bridge from it.
LIFT = ('Hoist_Pivot_Lift', 'HoistLift', (0, 0, 1), 1.5, 4)
RUNWAYS = tuple('Cube.%03d' % i for i in (422, 423, 428, 429) + tuple(range(434, 442))) \
    + tuple('Cylinder.%03d' % i for i in range(22, 46))
TRAVEL = {name: (channel, axis) for name, channel, axis, _, _ in HOIST[:2] + (LIFT,)}
# How far the hoist runs when driven by hand: the bridge until its trucks meet the tower carriages at the runways' ends,
# the trolley until its frame meets the bridge's trucks at the girders' ends. (bodies, stops) as name patterns.
DRIVEN_TRAVEL = {
    'Hoist_Pivot_Bridge': (r'^Hoist_Bridge_Truck(North|South)_\w+$', r'^Cube\.(43[4-9]|44[01])$'),
    'Hoist_Pivot_Trolley': (r'^Hoist_(TrolleyFrame|Motor|WheelHangers|Wheel_\d|Output_WinchDrum_Frame)$',
                            r'^Hoist_Bridge_Truck(North|South)_Plate\d$'),
}
TRAVEL_CLEARANCE = 0.15                    # a driven bridge or trolley stops this far short of what ends its run
# Marker empties the game builds things at, exported by name with their pose (layout `marks`): the vault's door face,
# its cog bench and the core's cradle (vault_build.py), and the crane console's panel, stance and loading gap
# (crane_build.py).
MARKS = re.compile(r'^(VaultDoor_(Face|Centre|CogTray)|Vault_CoreRest|CraneConsole_(Panel|Stance)|Crane_LoadingSpot)$')
HOOK_CLEARANCE = 0.2                       # the hook stops this far above whatever stands under it
MAIN_FLOOR_GROOVE_R = 14.70                # metres out where the main floor's slope meets the flat groove
LEVERS = ()                                # no crane is worked by hand any more
GRATES = (('Floor_GrateBars', 'Floor_Subfloor'), ('Bay_Floor_GrateBars', 'Bay_Floor_Subfloor'))
COL_INCLUDE = re.compile(
    r'(Shell$|_Floor$|Subfloor$|PitWall$|PitCap|_Tube(_\w+)?$|_Collar_|^Passage_|_Passage$|Passage_Floor$|_Pillars$|RockOutcrops$'
    r'|ClayPlatforms$|^Mountain$|^Rock_|_Ring(\.\d+)?$|^Cube\.|^TopPlatform_(Deck|Curb)|^Tunnel_\d+_(Clay|Rock)$'
    r'|^Tunnel_Floor$|^Tunnel_Walkway|Slab$|^Fence_|_Fence_|GrateFrame$|_Board$|^GearWall_(Plate|Motor)$|_Motor$'
    r'|^Tank_._(Body|Dome)$|^RoomC_(Floor|TestStand_\w+|Gantry_Columns)$|Towers_And_RingEdgeBeam$'
    r'|Plinth$|^Cave_Shell$|^Bay_Stair_\w+_(Rails|Posts)$|_Solid$)')
# Only the pit stairs' rails: a post on a stair foot closes that stair to the world NavMesh bake (0.33 m voxels), and
# the stairs above the ledge are the residents' only way between their levels (seen 2026-10-10: 10 unusable places).

bpy.ops.wm.open_mainfile(filepath=SRC)


def add_lift():
    """An empty at the bridge's pivot, under the bridge's parent, carrying the bridge and the runways where they are."""
    missing = [n for n in RUNWAYS if n not in bpy.data.objects]
    if missing:
        raise SystemExit('the hoist\'s runways are not in the model as named: %s' % ', '.join(missing))
    bridge = bpy.data.objects[HOIST[0][0]]
    lift = bpy.data.objects.new(LIFT[0], None)
    for c in bridge.users_collection:
        c.objects.link(lift)
    lift.parent = bridge.parent
    lift.matrix_parent_inverse = Matrix.Identity(4)
    lift.matrix_world = Matrix.Translation(bridge.matrix_world.translation)
    bpy.context.view_layer.update()
    for name in (bridge.name,) + RUNWAYS:
        o = bpy.data.objects[name]
        world = o.matrix_world.copy()
        o.parent = lift
        o.matrix_parent_inverse = Matrix.Identity(4)
        o.matrix_world = world
    bpy.context.view_layer.update()


add_lift()
dg = bpy.context.evaluated_depsgraph_get()
D = bpy.data.objects
TURN = {o.name: turn_node(o.name) for o in D if o.name.endswith('_Pivot_Turn') and o.visible_get()}
WHEELS = {name: json.loads(D[name]['workshop_wheel']) for name in TURN if 'workshop_wheel' in D[name].keys()}
ROOM_CHANNEL = {name: 'Room%d' % i for i, name in enumerate(sorted(WHEELS, key=lambda n: (n != 'MainRoom_Pivot_Turn', n)))}
# A swinging leaf (the gate at the ship tunnel's mouth): a mesh whose origin is its hinge, carrying the angles it is
# shut and open at about its own `spin_axis`. It ships as its own node, modelled pose and all, so the game can swing it.
HINGES = {o.name for o in D if o.type == 'MESH' and 'open_angle_deg' in o.keys() and o.visible_get()}

ORIGIN = D['MainRoom_Pivot_Turn'].matrix_world.translation.copy()
SHIFT = Matrix.Translation(-ORIGIN)
print('origin (blender)', tuple(round(c, 4) for c in ORIGIN))


def U3(v):
    """Blender vector -> Unity (prefab space for points already shifted, plain for directions)."""
    return [round(-v[0], 5), round(v[2], 5), round(-v[1], 5)]


CONV = Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))


def UQ(m):
    """Rotation of a Blender world matrix as a Unity quaternion [x, y, z, w]."""
    r = m.to_3x3().normalized()
    q = (CONV @ r @ CONV.transposed()).to_quaternion()
    return [round(q.x, 6), round(q.y, 6), round(q.z, 6), round(q.w, 6)]


def unscaled(m):
    r = m.to_3x3().normalized().to_4x4()
    r.translation = m.translation
    return r


# ---------------------------------------------------------------- materials
SUFFIX = re.compile(r'\.\d{3,4}$')
by_sig, canon = {}, {}


def principled(m):
    return next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if m and m.use_nodes else None


def sig(m):
    b = principled(m)
    if not b:
        return ('raw', m.name)
    out = []
    for k in ('Base Color', 'Roughness', 'Metallic', 'Alpha', 'Emission Color', 'Emission Strength'):
        if k in b.inputs:
            v = b.inputs[k].default_value
            out.append(tuple(round(x, 3) for x in v) if hasattr(v, '__len__') else round(v, 3))
    return tuple(out)


def canonical(m):
    if m is None:
        return None
    if m.name in canon:
        return canon[m.name]
    res = m
    if m.name == 'Material' or SUFFIX.search(m.name):
        s = sig(m)
        res = by_sig.setdefault(s, m)
    canon[m.name] = res
    return res


def fix_attribute_colours():
    """A base colour driven by a vertex attribute exports as the socket's unused default. Write the attribute's
    mean there so the FBX material carries the colour the model is seen in."""
    for m in bpy.data.materials:
        b = principled(m)
        if not b or not b.inputs['Base Color'].is_linked:
            continue
        node = b.inputs['Base Color'].links[0].from_node
        if node.type != 'ATTRIBUTE':
            continue
        acc, n = Vector((0, 0, 0)), 0
        for o in bpy.data.objects:
            if o.type == 'MESH' and m.name in o.data.materials and node.attribute_name in o.data.attributes:
                a = o.data.attributes[node.attribute_name]
                for i in range(0, len(a.data), max(1, len(a.data) // 64)):
                    c = a.data[i].color
                    acc += Vector((c[0], c[1], c[2]))
                    n += 1
                if n > 2000:
                    break
        if n:
            mean = acc / n
            b.inputs['Base Color'].default_value = (mean.x, mean.y, mean.z, 1.0)
            print('  attribute colour -> %s base colour (%.3f %.3f %.3f)' % (m.name, mean.x, mean.y, mean.z))


fix_attribute_colours()


# ---------------------------------------------------------------- classification
def ancestors(o):
    while o is not None:
        yield o
        o = o.parent


def is_unit(o):
    n = o.name
    if n in TURN:
        return True
    if any(a.name == 'Fan_Rotor' for a in ancestors(o.parent)):
        return False
    if o.type == 'EMPTY':
        if 'Pivot' in n:
            return 'HookSwivel' not in n
        kids = [c.name for c in o.children if c.type == 'MESH']
        return any(k.endswith('_Bar') or k.endswith('_Rim') for k in kids)
    return n in MESH_UNITS or n in HINGES


def unit_of(o):
    if o.name in FORCE_TURN:
        return D[FORCE_TURN[o.name]]
    for a in ancestors(o):
        if is_unit(a):
            return a
    return None


def unit_kind(n):
    if n in TURN:
        return 'turntable'
    if 'Pivot_HookBlock' in n or n in TRAVEL:
        return 'slider'
    if n in CABLES:
        return 'cable'
    if n.endswith('Pivot_Lever'):
        return 'lever'
    if n in HINGES:
        return 'hinge'
    return 'spinner'


def unit_node_name(u):
    return TURN.get(u.name, u.name)


def group_of(o):
    c = o.users_collection[0].name if o.users_collection else 'Loose'
    c = {'Collection': 'Gantry'}.get(c, c)
    return re.sub(r'^Cave_', '', c)


# ---------------------------------------------------------------- gather
items = []
for inst in dg.object_instances:
    ob = inst.object
    if ob.type != 'MESH':
        continue
    orig = ob.original
    if not orig.visible_get() or orig.name.startswith('Cutter_'):
        continue
    items.append((orig, ob, SHIFT @ inst.matrix_world.copy()))
print('visible mesh instances', len(items))


def eval_bm(ob, mat):
    """Evaluated mesh in the space `mat` maps to, n-gons triangulated, winding repaired. Returns (bm, colour layer)."""
    me = ob.to_mesh()
    bm = bmesh.new()
    bm.from_mesh(me)
    ob.to_mesh_clear()
    bm.transform(mat)
    if mat.to_3x3().determinant() < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces[:])
    big = [f for f in bm.faces if len(f.verts) > 4]
    if big:
        bmesh.ops.triangulate(bm, faces=big, quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.verts.index_update()
    bm.faces.index_update()
    bm.normal_update()
    return bm, bm.verts.layers.float_color.get('StyleColor')


def hull_in_place(bm):
    """Replace a point cloud by its convex hull, dropping the points the hull did not use."""
    res = bmesh.ops.convex_hull(bm, input=bm.verts[:])
    junk = {e for e in res.get('geom_interior', []) + res.get('geom_unused', []) if isinstance(e, bmesh.types.BMVert)}
    junk = [v for v in junk if v.is_valid]
    if junk:
        bmesh.ops.delete(bm, geom=junk, context='VERTS')


def place(ob, matrix, parent=None, parent_frame=None):
    """Pose a new object without asking the depsgraph where its (equally new) parent is."""
    if parent is not None:
        ob.parent = parent
        ob.matrix_parent_inverse = Matrix.Identity(4)
        ob.matrix_basis = parent_frame.inverted() @ matrix
    elif matrix is not None:
        ob.matrix_basis = matrix


class Joiner:
    def __init__(self):
        self.v, self.f, self.m, self.s, self.c, self.mats, self.idx, self.coloured = [], [], [], [], [], [], {}, False

    def slot(self, m):
        if m not in self.idx:
            self.idx[m] = len(self.mats)
            self.mats.append(m)
        return self.idx[m]

    def add(self, bm, slots, layer=None):
        off = len(self.v)
        for v in bm.verts:
            self.v.append(v.co[:])
            if layer is not None:
                c = v[layer]
                self.c.append((c[0], c[1], c[2], 1.0))
            else:
                self.c.append((1.0, 1.0, 1.0, 1.0))
        self.coloured |= layer is not None
        for f in bm.faces:
            self.f.append([off + v.index for v in f.verts])
            mi = f.material_index
            m = canonical(slots[mi]) if slots and mi < len(slots) else None
            self.m.append(self.slot(m) if m else 0)
            self.s.append(f.smooth)

    def build(self, name, matrix=None, parent=None, parent_frame=None):
        me = bpy.data.meshes.new(name)
        me.from_pydata(self.v, [], self.f)
        me.polygons.foreach_set('material_index', self.m)
        me.polygons.foreach_set('use_smooth', self.s)
        for m in self.mats:
            if m:
                me.materials.append(m)
        if self.coloured:
            a = me.color_attributes.new('Col', 'FLOAT_COLOR', 'POINT')
            a.data.foreach_set('color', [x for c in self.c for x in c])
        me.update()
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        place(ob, matrix, parent, parent_frame)
        return ob

    @property
    def tris(self):
        return sum(len(f) - 2 for f in self.f)


def slots_of(orig):
    return [s.material for s in orig.material_slots]


# ---------------------------------------------------------------- static visual + unit meshes
static_chunks = defaultdict(list)          # group -> [Joiner]
unit_join = {}                             # unit object -> Joiner (unit-local space)
unit_members = defaultdict(list)
unit_frame = {}


def frame_of(u):
    if u in unit_frame:
        return unit_frame[u]
    m = unscaled(SHIFT @ u.matrix_world)
    if u.name in CABLES:
        pts = [SHIFT @ u.matrix_world @ Vector(c) for c in u.bound_box]
        top = max(p.z for p in pts)
        m = Matrix.Translation((sum(p.x for p in pts) / 8, sum(p.y for p in pts) / 8, top))
    unit_frame[u] = m
    return m


for orig, ob, world in items:
    u = unit_of(orig)
    if u is None:
        bm, layer = eval_bm(ob, world)
        js = static_chunks[group_of(orig)]
        if not js or len(js[-1].v) + len(bm.verts) > CHUNK_VERTS:
            js.append(Joiner())
        js[-1].add(bm, slots_of(orig), layer)
        bm.free()
    else:
        f = frame_of(u)
        bm, layer = eval_bm(ob, f.inverted() @ world)
        unit_join.setdefault(u, Joiner()).add(bm, slots_of(orig), layer)
        unit_members[u].append((orig, ob, world))
        bm.free()

# ---------------------------------------------------------------- turntable collision (convex pieces)
def polar(p):
    return math.hypot(p.x, p.y), math.atan2(p.y, p.x)


def sector(r0, r1, a0, a1, z0, z1):
    """Convex annular-sector prism in a tangent-aligned local frame. Returns (matrix, verts-local)."""
    am = 0.5 * (a0 + a1)
    c = Vector((math.cos(am) * 0.5 * (r0 + r1), math.sin(am) * 0.5 * (r0 + r1), 0.5 * (z0 + z1)))
    rad = Vector((math.cos(am), math.sin(am), 0))
    tan = Vector((-rad.y, rad.x, 0))
    m = Matrix((tan, rad, Vector((0, 0, 1)))).transposed().to_4x4()
    m.translation = c
    inv = m.inverted()
    pts = [inv @ Vector((math.cos(a) * r, math.sin(a) * r, z)) for a in (a0, a1) for r in (r0, r1) for z in (z0, z1)]
    return m, pts


colt_specs = []                            # (name, points, world matrix, turntable unit)
for u, tag in sorted(((D[name], TURN[name].replace('Turntable_', '').replace('Turntable', '')) for name in TURN), key=lambda x: x[1]):
    pivot = frame_of(u)
    inv = pivot.inverted()
    n = defaultdict(int)

    def piece(kind, pts_local_to_piece, m_piece, u=u, tag=tag, pivot=pivot, n=n):
        nm = 'COLT_%s_%s_%02d' % (tag, kind, n[kind])
        n[kind] += 1
        colt_specs.append((nm, [Vector(p) for p in pts_local_to_piece], pivot @ m_piece, u))

    for orig, ob, world in unit_members[u]:
        bm, _ = eval_bm(ob, inv @ world)
        name = orig.name
        if name.endswith('ClayWall') and name.startswith('WheelRoom_'):
            # A hand-shaped wall: no two faces share a height, so take what it covers, ten degrees at a time.
            # It is a whole pot, ceiling and all: only what stands below head height is wall to walk into. The
            # cave shell round it stays the roof nobody gets through.
            low = [v for v in bm.verts if v.co.z < 2.6]
            rs = [polar(v.co)[0] for v in low]
            r0, r1 = min(rs), max(rs)
            z0, z1 = min(v.co.z for v in low), 3.4
            covered = sorted({int(math.floor(math.degrees(polar(v.co)[1]) % 360.0)) for v in low})
            runs = []
            for deg in covered:
                if runs and deg - runs[-1][1] <= 2 and deg + 1 - runs[-1][0] <= 10:
                    runs[-1][1] = deg + 1
                else:
                    runs.append([deg, deg + 1])
            for a0, a1 in runs:
                m, pts = sector(r0, r1, math.radians(a0), math.radians(a1), round(z0, 2), round(z1, 2))
                piece('Wall', pts, m)
        elif name.endswith('ClayWall'):
            rs = [polar(v.co)[0] for v in bm.verts]
            r0, r1 = min(rs), max(rs)
            strips = []
            for f in bm.faces:
                c = f.calc_center_median()
                rad = Vector((c.x, c.y, 0)).normalized()
                zs = [v.co.z for v in f.verts]
                if f.normal.dot(rad) < 0.7 or max(zs) - min(zs) < 0.4:
                    continue
                ref = polar(c)[1]
                an = [ref + ((polar(v.co)[1] - ref + math.pi) % (2 * math.pi) - math.pi) for v in f.verts]
                strips.append([min(an), max(an), round(min(zs), 2), round(max(zs), 2)])
            strips.sort()
            merged = []
            for s in strips:
                last = merged[-1] if merged else None
                near = last is not None and abs(((s[0] - last[1] + math.pi) % (2 * math.pi)) - math.pi) < 0.002
                if near and s[2] == last[2] and s[3] == last[3] and (s[1] - last[0]) <= math.radians(10.01):
                    last[1] = s[1]
                else:
                    merged.append(list(s))
            for a0, a1, z0, z1 in merged:
                m, pts = sector(r0, r1, a0, a1, z0, z1)
                piece('Wall', pts, m)
        elif name.endswith('Gangway_Deck') or 'Gangway' in name and name.endswith('_Deck'):
            c = sum((v.co for v in bm.verts), Vector()) / len(bm.verts)
            piece('Deck', [v.co - c for v in bm.verts], Matrix.Translation(c))
        elif name == 'MainRoom_Drum_Floor':
            # The floor between the cog ring and the wall: a convex wedge every 15 degrees, down to under the pit
            # floor so a thin plate is never all that holds somebody up. Two bands, the slope off the cog ring and
            # the flat groove the main pinion runs over: one hull across both would fill the groove in.
            columns = defaultdict(list)
            for v in bm.verts:
                r, a = polar(v.co)
                k = round(math.degrees(a) / 5.0) % 72
                if r <= MAIN_FLOOR_GROOVE_R + 0.05:
                    columns[(0, k)].append(v.co.copy())
                if r >= MAIN_FLOOR_GROOVE_R - 0.05:
                    columns[(1, k)].append(v.co.copy())
            for band in (0, 1):
                for k in range(24):
                    pts = [p for c in range(3 * k, 3 * k + 4) for p in columns[(band, c % 72)]]
                    pts += [Vector((p.x, p.y, -0.6)) for p in pts]
                    piece('Floor', pts, Matrix.Identity(4))
        elif name.endswith('_Drum_HubSleeve'):
            # The post, its hand cog and its gears: one drum as wide as the hand cog's teeth reach and taller than
            # its grip, so nobody walks through them. Measured off the sleeve: the main room's is twice the others'.
            size = max(polar(v.co)[0] for v in bm.verts) / 0.5
            piece('Hub', [Vector((math.cos(k * math.pi / 8) * 1.55 * size, math.sin(k * math.pi / 8) * 1.55 * size, z))
                          for k in range(16) for z in (0.0, 2.0 * size)], Matrix.Identity(4))
        elif name == 'Bay_LandingPad' or name.startswith('WheelRoom_') and name.endswith('_Drum_Floor'):
            r = max(polar(v.co)[0] for v in bm.verts)
            top = max(v.co.z for v in bm.verts)
            piece('Pad', [Vector((math.cos(k * math.pi / 16) * r, math.sin(k * math.pi / 16) * r, z))
                          for k in range(32) for z in (-0.6, top)], Matrix.Identity(4))
        elif name.endswith('CogRing'):
            rs = [polar(v.co)[0] for v in bm.verts]
            top = max(v.co.z for v in bm.verts)
            for k in range(24):
                m, pts = sector(min(rs), max(rs) - 0.15, k * math.pi / 12, (k + 1) * math.pi / 12, -0.2, top)
                piece('Ring', pts, m)
        bm.free()
    print('turntable %s: %s' % (tag, dict(n)))

# A gate leaf is a flat slab: one hull round all of it, so a shut gate is a wall and an open one is bumped into.
for name in sorted(HINGES):
    u = D[name]
    inv = frame_of(u).inverted()
    pts = []
    for orig, ob, world in unit_members[u]:
        bm, _ = eval_bm(ob, inv @ world)
        pts += [v.co.copy() for v in bm.verts]
        bm.free()
    colt_specs.append(('COLT_%s' % name, pts, frame_of(u), u))
    print('hinge %s: one hull of %d points' % (name, len(pts)))


# ---------------------------------------------------------------- static collision
col_chunks = defaultdict(list)


def col_add(group, bm):
    js = col_chunks[group]
    if not js or len(js[-1].v) + len(bm.verts) > CHUNK_VERTS:
        js.append(Joiner())
    js[-1].add(bm, None)


by_name = {orig.name: (orig, ob, world) for orig, ob, world in items}
ramps = 0
for orig, ob, world in items:
    if unit_of(orig) is not None:
        continue
    name = orig.name
    if name.endswith('_Treads'):
        bm, _ = eval_bm(ob, world)
        seen, islands = set(), []
        for v in bm.verts:
            if v.index in seen:
                continue
            stack, isl = [v], []
            seen.add(v.index)
            while stack:
                x = stack.pop()
                isl.append(x.co.copy())
                for e in x.link_edges:
                    y = e.other_vert(x)
                    if y.index not in seen:
                        seen.add(y.index)
                        stack.append(y)
            islands.append(isl)
        islands.sort(key=lambda isl: sum(p.z for p in isl) / len(isl))
        for a, b in zip(islands, islands[1:]):
            hb = bmesh.new()
            for p in a + b:
                hb.verts.new(p)
            hull_in_place(hb)
            hb.verts.index_update()
            col_add(group_of(orig), hb)
            hb.free()
            ramps += 1
        bm.free()
    elif COL_INCLUDE.search(name):
        bm, _ = eval_bm(ob, world)
        col_add(group_of(orig), bm)
        bm.free()

for bars, sub in GRATES:
    if bars in by_name and sub in by_name:
        bo, bob, bw = by_name[bars]
        so, sob, sw = by_name[sub]
        top = max((bw @ Vector(c)).z for c in bo.bound_box)
        sub_top = max((sw @ Vector(c)).z for c in so.bound_box)
        bm, _ = eval_bm(sob, Matrix.Translation((0, 0, top - sub_top)) @ sw)
        col_add(group_of(bo), bm)
        bm.free()
        print('  grate plate: %s raised %.2f m to the top of %s' % (sub, top - sub_top, bars))
    else:
        print('  WARNING grate pair missing: %s / %s' % (bars, sub))

# A hand cog nobody may walk through: where no turning hub stands round it (the main room's hangs by the entrance),
# a still drum its own size, from under the cog to over the grip.
for name, wheel in WHEELS.items():
    if any(o.name.endswith('_Drum_HubSleeve') for o in D[name].children):
        continue
    pivot = D[wheel['wheel']]
    rim = max((max(math.hypot(v.co.x, v.co.z) for v in c.data.vertices) * c.matrix_world.to_scale().x
               for c in pivot.children if c.type == 'MESH' and c.name.endswith('_Rim')), default=0.6)
    at = pivot.matrix_world.translation
    hb = bmesh.new()
    for z in (at.z - 0.3, at.z + 0.75):
        for k in range(12):
            hb.verts.new(Vector((at.x + rim * math.cos(k * math.pi / 6), at.y + rim * math.sin(k * math.pi / 6), z)))
    hull_in_place(hb)
    col_add(group_of(pivot), hb)
    hb.free()
    print('  still drum round the hand cog %s, r %.2f' % (pivot.name, rim))

# ---------------------------------------------------------------- the gear graph
class Node:
    def __init__(self, u):
        self.u, self.name = u, unit_node_name(u)
        f = frame_of(u)
        self.p = f.translation.copy()
        zed = u.name == 'Fan_Rotor'
        self.a = Vector((0, 0, 1)) if zed else f.col[1].to_3d().normalized()
        ts, rmax = [], 0.0
        for orig, ob, world in unit_members[u]:
            for c in orig.bound_box:
                d = world @ Vector(c) - self.p
                t = d.dot(self.a)
                ts.append(t)
                rmax = max(rmax, (d - self.a * t).length)
        self.t0, self.t1 = (min(ts), max(ts)) if ts else (0.0, 0.0)
        self.teeth, self.rp = None, None
        for orig, ob, world in unit_members[u]:
            mn = orig.data.name
            k = re.match(r'Mesh_Gear_Puzzle_(XS|S|M|L|XL)_Rim', mn)
            if k:
                # A unit turns about its own origin and Y axis: a gear off them swings round the room instead.
                d = world.translation - self.p
                if (d - self.a * d.dot(self.a)).length > 0.05 or abs(world.col[1].to_3d().normalized().dot(self.a)) < 0.999:
                    raise SystemExit('%s turns about %s, not about its gear %s: put the pivot on the gear'
                                     % (self.name, tuple(round(c, 2) for c in self.p), orig.name))
                rl = max(math.hypot(v.co.x, v.co.z) for v in orig.data.vertices)
                self.teeth = round((rl - 0.04) / 0.02)
                self.rp = self.teeth * 0.02 * orig.matrix_world.to_scale().x
        self.channel, self.ratio, self.parent = None, 0.0, None


nodes = {unit_node_name(u): Node(u) for u in unit_join if unit_kind(u.name) == 'spinner'}
edges = defaultdict(list)                  # name -> [(other, factor)]   w_other = factor * w_self


def link(a, b, factor):
    edges[a.name].append((b.name, factor))
    edges[b.name].append((a.name, 1.0 / factor))


names = sorted(nodes)
for i, na in enumerate(names):
    A = nodes[na]
    for nb in names[i + 1:]:
        B = nodes[nb]
        s = A.a.dot(B.a)
        d = B.p - A.p
        ax = d.dot(A.a)
        perp = (d - A.a * ax).length
        if A.teeth and B.teeth and abs(s) > 0.99 and abs(ax) < 0.6 and abs(perp - (A.rp + B.rp)) < 0.05 * (A.rp + B.rp):
            link(A, B, -math.copysign(1.0, s) * A.teeth / B.teeth)
            continue
        if abs(s) > 0.995 and perp < 0.06:
            lo, hi = sorted((B.t0 * s + ax, B.t1 * s + ax))
            if max(0.0, max(lo, A.t0) - min(hi, A.t1)) < 0.3:
                link(A, B, math.copysign(1.0, s))
for na in names:
    if na.endswith('_Pivot_A') and na[:-1] + 'B' in nodes:
        link(nodes[na], nodes[na[:-1] + 'B'], -1.0)          # bevel pair, both axes pointing out of the box
    if na.endswith('_Pivot_In') and 'UJoint' in na:
        base = na[:-len('In')]
        for other in ('Out', 'Cross'):
            if base + other in nodes:
                link(nodes[na], nodes[base + other], math.copysign(1.0, nodes[na].a.dot(nodes[base + other].a)))


def walk(seed, channel, ratio):
    if seed not in nodes or nodes[seed].channel:
        print('  seed %s: %s' % (seed, 'missing' if seed not in nodes else 'already reached'))
        return
    nodes[seed].channel, nodes[seed].ratio = channel, ratio
    q = deque([seed])
    while q:
        cur = nodes[q.popleft()]
        for other, factor in edges[cur.name]:
            o = nodes[other]
            if o.channel is None:
                o.channel, o.ratio, o.parent = cur.channel, cur.ratio * factor, cur.name
                q.append(other)


# A turning room is turned from its own middle: the pinion on the fixed post bites the gear on the room's floor, so
# it turns the other way and faster by the tooth count. The hand cog sits on the pinion's own shaft and the small
# tell-tale cog on the post's top is bitten by it; the walk finds both from the pinion. The main room's pinion
# runs inside a toothed ring on the wall's top instead, so it turns the same way as the room ('same_way').
for name, wheel in WHEELS.items():
    walk(wheel['pinion'], ROOM_CHANNEL[name], wheel['ratio'] if wheel.get('same_way') else -wheel['ratio'])
    if nodes.get(wheel['wheel']) is None or nodes[wheel['wheel']].channel is None:
        raise SystemExit('the hand cog %s is not on its pinion\'s shaft' % wheel['wheel'])

drum = nodes.get('Hoist_Output_WinchDrum_Pivot_Drum')
if drum:
    r = max(math.hypot(v.co.x, v.co.z) for v in D['Hoist_Output_WinchDrum_Drum'].data.vertices)
    walk(drum.name, 'Crane', 2.0 * 360.0 / (2 * math.pi * max(r, 0.05)))
for g in ('Hoist_Puzzle_M', 'HookBlock_Puzzle_L'):
    if g in nodes and nodes[g].channel is None:
        walk(g, 'Crane', 360.0 / (2 * math.pi * nodes[g].rp))
walk('Fan_Rotor', 'Ambient', 90.0)
for n in nodes.values():                                    # the gear wall runs on its own motor and never stops
    if n.channel is None and n.name.startswith('GearWall_') and n.teeth:
        walk(n.name, 'Idle', 7.2 * 14.0 / n.teeth)
for name in sorted(nodes):                                  # the trains up the gantry columns roll with the bridge:
    n = nodes[name]                                         # every gear of a train has the rim speed of its travel
    if n.channel is None and name.startswith('RoomC_GantryGear_') and n.rp:
        walk(name, 'GantryBridge', 360.0 / (2 * math.pi * n.rp))
unreached = sorted(n.name for n in nodes.values() if n.channel is None)
print('  spinners: %d wired, %d not reached%s' % (len(nodes) - len(unreached), len(unreached),
                                                 (': ' + ', '.join(unreached)) if unreached else ''))
by_channel = defaultdict(int)
for n in nodes.values():
    by_channel[n.channel] += 1
print('  per channel:', dict(by_channel))


# ---------------------------------------------------------------- layout
def pos(name):
    return U3(SHIFT @ D[name].matrix_world.translation)


layout = {'origin_blender': [round(c, 4) for c in ORIGIN], 'turntables': [], 'spinners': [], 'sliders': [],
          'cables': [], 'levers': [], 'hinges': [], 'wheels': [], 'tunnels': [], 'controls': [], 'rooms': [], 'lamps': []}
for u in unit_join:
    kind, node = unit_kind(u.name), unit_node_name(u)
    if kind == 'turntable':
        layout['turntables'].append({'node': node})
    elif kind == 'slider':
        if u.name in TRAVEL:
            channel, axis = TRAVEL[u.name]
            along = (u.matrix_world.to_3x3() @ Vector(axis)).normalized()
            layout['sliders'].append({'node': node, 'channel': channel, 'dir': U3(along), 'perUnit': 1.0})
        else:
            layout['sliders'].append({'node': node, 'channel': 'Crane', 'dir': [0, -1, 0], 'perUnit': 1.0})
    elif kind == 'cable':
        zs = [(SHIFT @ u.matrix_world @ Vector(c)).z for c in u.bound_box]
        layout['cables'].append({'node': node, 'channel': CABLES[u.name], 'length': round(max(zs) - min(zs), 4)})
    elif kind == 'lever':
        channel = next(c for prefix, c in LEVERS if u.name.startswith(prefix))
        layout['levers'].append({'node': node, 'channel': channel, 'axis': U3(frame_of(u).col[1].to_3d()), 'degrees': -22.0})
    elif kind == 'hinge':
        # The leaf is modelled standing open. `shut` is how far it turns from there, in degrees about `axis` (Unity,
        # prefab space), to stand shut. A turn of +a about a Blender axis is -a about the converted one.
        spin = frame_of(u).col['XYZ'.index(u['spin_axis'][-1])].to_3d().normalized()
        layout['hinges'].append({'node': node, 'axis': U3(spin),
                                 'shut': round(float(u['open_angle_deg']) - float(u['closed_angle_deg']), 3),
                                 'door': str(u.get('gear_door', ''))})
for n in sorted(nodes.values(), key=lambda n: n.name):
    if n.channel:
        layout['spinners'].append({'node': n.name, 'channel': n.channel, 'axis': U3(n.a), 'ratio': round(-n.ratio, 5)})
# A turning room, as the game needs it: where its wheel is held and stood at, and how far (its own way round, the
# way a positive angle turns it in the model) it must turn for its one entrance to face each tunnel it can face.
for name in sorted(WHEELS, key=lambda n: ROOM_CHANNEL[n]):
    wheel = WHEELS[name]
    centre = SHIFT @ D[name].matrix_world.translation
    grip = SHIFT @ D[wheel['wheel']].matrix_world.translation
    stance = SHIFT @ Vector((wheel['stance'][0], wheel['stance'][1], D[name].matrix_world.translation.z))
    layout['wheels'].append({'id': wheel['id'], 'turntable': TURN[name], 'channel': ROOM_CHANNEL[name],
                             'pos': U3(grip), 'stance': U3(stance), 'centre': U3(centre), 'radius': wheel['radius'],
                             'ports': [{'tunnel': t, 'degrees': a} for t, a in sorted(wheel['ports'].items())]})
for tunnel, entry in sorted(json.loads(bpy.context.scene.get('workshop_net', '{}')).items()):
    at = SHIFT @ Vector((entry['pos'][0], entry['pos'][1], ORIGIN.z))
    layout['tunnels'].append({'id': tunnel, 'pos': U3(at), 'rooms': entry['members']})
def hook_limits(machine, hook_mesh, hook_step):
    """How low the hook may go at each place the bridge and trolley can stand: it lands on what is under it. Rays
    over the hook's footprint, from just under the hook as modelled (raised) down to the floor. A row per bridge
    stop, a column per trolley stop."""
    (bridge, _, a_b, step_b, bridge_stops), (trolley, _, a_t, step_t, trolley_stops), (hook, _, _, _, hook_stops) = machine
    span_b = (D[bridge].matrix_world.to_3x3() @ Vector(a_b)).normalized() * step_b
    span_t = (D[trolley].matrix_world.to_3x3() @ Vector(a_t)).normalized() * step_t
    low = min((hook_mesh.matrix_world @ v.co).z for v in hook_mesh.data.vertices)
    limits = []
    for b in range(bridge_stops):
        for t in range(trolley_stops):
            at = D[hook].matrix_world.translation + span_b * b + span_t * t
            top = -1e9
            for i in range(-4, 5):
                for j in range(-4, 5):
                    hit, where, _, _, _, _ = bpy.context.scene.ray_cast(
                        dg, Vector((at.x + i * 0.1, at.y + j * 0.1, low - 0.05)), Vector((0, 0, -1)), distance=30.0)
                    if hit:
                        top = max(top, where.z)
            limits.append(max(0, min(hook_stops - 1, int(math.floor((low - top - HOOK_CLEARANCE) / hook_step)))))
    return limits


def driven_travel(pivot, axis):
    """How far `pivot` may be driven along its own `axis`, least and most, in metres from where it is modelled: its
    body (DRIVEN_TRAVEL) runs until it meets the first stop on either side."""
    bodies, stops = (re.compile(x) for x in DRIVEN_TRAVEL[pivot])
    along = (D[pivot].matrix_world.to_3x3() @ Vector(axis)).normalized()
    at = D[pivot].matrix_world.translation

    def span(o):
        ds = [(o.matrix_world @ Vector(c) - at).dot(along) for c in o.bound_box]
        return min(ds), max(ds)

    body = [span(o) for o in D if o.type == 'MESH' and bodies.match(o.name)]
    ends = [span(o) for o in D if o.type == 'MESH' and stops.match(o.name)]
    if not body or not ends:
        raise SystemExit('cannot measure how far %s runs: no %s or no %s in the model' % (pivot, bodies.pattern, stops.pattern))
    low, high = min(b[0] for b in body), max(b[1] for b in body)
    before = [e[1] for e in ends if e[1] <= low + 1e-3]
    after = [e[0] for e in ends if e[0] >= high - 1e-3]
    if not before or not after:
        raise SystemExit('%s has nothing to stop it on one side of its run' % pivot)
    return [round(max(before) - low + TRAVEL_CLEARANCE, 3), round(min(after) - high - TRAVEL_CLEARANCE, 3)]


def travel_block(machine, hook_mesh, hook_step, label):
    (bridge, _, a_b, step_b, bridge_stops), (trolley, _, a_t, step_t, trolley_stops), (_, _, _, _, hook_stops) = machine
    limits = hook_limits(machine, hook_mesh, hook_step)
    print('  %s hook limits (rows: bridge stops):' % label,
          [limits[b * trolley_stops:(b + 1) * trolley_stops] for b in range(bridge_stops)])
    bridge_travel, trolley_travel = driven_travel(bridge, a_b), driven_travel(trolley, a_t)
    print('  %s driven travel: bridge %s m, trolley %s m' % (label, bridge_travel, trolley_travel))
    return {'node': bridge, 'metresPerStop': [step_b, step_t, round(hook_step, 4), LIFT[3]],
            'stops': [bridge_stops, trolley_stops, hook_stops, LIFT[4]], 'hookLimits': limits,
            'bridgeTravel': bridge_travel, 'trolleyTravel': trolley_travel}


if all(name in D for name, _, _, _, _ in HOIST) and 'Hoist_Hook' in D:
    # The hoist hook's lowest stop leaves it 2.4 m above the pit floor, as it always has.
    hook_mesh = D['Hoist_Hook']
    low = min((SHIFT @ hook_mesh.matrix_world @ Vector(c)).z for c in hook_mesh.bound_box)
    layout['hoist'] = travel_block(HOIST, hook_mesh, (low - 2.4) / (HOIST[2][4] - 1), 'hoist')
layout['marks'] = []
for o in sorted((o for o in D if o.type == 'EMPTY' and MARKS.match(o.name)), key=lambda o: o.name):
    m = SHIFT @ o.matrix_world
    layout['marks'].append({'name': o.name, 'pos': U3(m.translation), 'fwd': U3(m.to_3x3().normalized().col[1]),
                            'up': U3(m.to_3x3().normalized().col[2])})
print('marks:', [k['name'] for k in layout['marks']])
for orig, ob, world in items:
    k = (re.match(r'^(Room)_(.+?)_Floor$', orig.name) or re.match(r'^(RoomC|Vault)_()Floor$', orig.name)
         or re.match(r'^(WheelRoom)_(\d+)_Drum_Floor$', orig.name))
    if k:
        pts = [world @ Vector(c) for c in orig.bound_box]
        c = sum(pts, Vector()) / 8
        top = max(p.z for p in pts)
        half = 0.5 * min(max(p.x for p in pts) - min(p.x for p in pts), max(p.y for p in pts) - min(p.y for p in pts))
        layout['rooms'].append({'name': (k.group(1) + '_' + k.group(2)).rstrip('_'), 'pos': U3(Vector((c.x, c.y, top))),
                                'radius': round(half, 3)})
for orig, ob, world in items:
    glow = [i for i, s in enumerate(orig.material_slots) if s.material and 'Glow' in s.material.name]
    if not glow or unit_of(orig) is not None:
        continue
    bm, _ = eval_bm(ob, world)
    seen = set()
    for f in bm.faces:
        if f.index in seen or f.material_index not in glow:
            continue
        stack, acc, cnt = [f], Vector(), 0
        seen.add(f.index)
        while stack:
            x = stack.pop()
            acc += x.calc_center_median()
            cnt += 1
            for e in x.edges:
                for y in e.link_faces:
                    if y.index not in seen and y.material_index in glow:
                        seen.add(y.index)
                        stack.append(y)
        layout['lamps'].append({'pos': U3(acc / cnt)})
    bm.free()
allv = [v for js in static_chunks.values() for j in js for v in j.v]
lo = [min(v[i] for v in allv) for i in range(3)]
hi = [max(v[i] for v in allv) for i in range(3)]
ulo, uhi = U3(lo), U3(hi)
layout['bounds'] = {'min': [min(ulo[i], uhi[i]) for i in range(3)], 'max': [max(ulo[i], uhi[i]) for i in range(3)]}

# ================================================================ everything above only READ the file.
# New datablocks are made from here on: creating one earlier would invalidate the evaluated objects still in use.
static_objs = []
for group, js in sorted(static_chunks.items()):
    for i, j in enumerate(js):
        static_objs.append(j.build('Static_%s_%02d' % (group, i)))
print('static: %d chunk(s), %d verts, %d tris' % (len(static_objs), sum(len(o.data.vertices) for o in static_objs),
                                                 sum(len(p.vertices) - 2 for o in static_objs for p in o.data.polygons)))

unit_objs = {}
# A new object may not take the name of one that exists: Blender would hand it "<name>.001", and the layout file
# and the prefab builder address every node by name. So the originals give theirs up first.
node_name = {u: unit_node_name(u) for u in unit_join}
for u in unit_join:
    u.name = '_src_' + u.name


def build_unit(u):
    if u in unit_objs:
        return unit_objs[u]
    parent = next((a for a in ancestors(u.parent) if a in unit_join), None)
    pobj = build_unit(parent) if parent is not None else None
    unit_objs[u] = unit_join[u].build(node_name[u], frame_of(u), pobj, frame_of(parent) if parent is not None else None)
    return unit_objs[u]


for u in list(unit_join):
    build_unit(u)
bpy.context.view_layer.update()
print('moving: %d unit(s), %d tris' % (len(unit_objs), sum(j.tris for j in unit_join.values())))


def hull_object(name, pts, matrix, parent, parent_frame):
    bm = bmesh.new()
    for p in pts:
        bm.verts.new(p)
    hull_in_place(bm)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    place(ob, matrix, parent, parent_frame)
    return ob


colt = [hull_object(nm, pts, m, unit_objs[u], frame_of(u)) for nm, pts, m, u in colt_specs]
col_objs = []
for group, js in sorted(col_chunks.items()):
    for i, j in enumerate(js):
        col_objs.append(j.build('COL_%s_%02d' % (group, i)))
print('collision: %d chunk(s), %d tris, %d ramp piece(s)' % (
    len(col_objs), sum(len(p.vertices) - 2 for o in col_objs for p in o.data.polygons), ramps))


# ---------------------------------------------------------------- write
os.makedirs(OUT, exist_ok=True)


def write(path, objs):
    for o in bpy.data.objects:
        o.select_set(False)
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.update()
    _exportlib._write_fbx(os.path.join(OUT, path), {'MESH'}, use_selection=True)


renamed = [o.name for o in list(unit_objs.values()) + colt + static_objs + col_objs if SUFFIX.search(o.name)]
if renamed:
    raise SystemExit('Blender renamed %d exported node(s), the layout would not find them: %s' % (len(renamed), ', '.join(renamed[:8])))
write('workshop.fbx', static_objs)
write('workshop_moving.fbx', list(unit_objs.values()) + colt)
write('workshop_collision.fbx', col_objs)
with open(os.path.join(OUT, 'workshop_layout.json'), 'w') as fh:
    json.dump(layout, fh, indent=1)
print('layout: %d spinner(s), %d slider(s), %d cable(s), %d lever(s), %d room(s), %d lamp(s)' % (
    len(layout['spinners']), len(layout['sliders']), len(layout['cables']), len(layout['levers']),
    len(layout['rooms']), len(layout['lamps'])))
print('WORKSHOP EXPORT DONE')
