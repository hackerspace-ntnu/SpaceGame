"""models/buildings/industrial/hulk_settlement — a 60 m derelict loader, lived in.

The reference is a bulk-handling machine the size of a district, dead in a
desert, with people living in it. Three masses read at a kilometre and they are
the only three things this script really has to get right:

    a tall block tower at the left end,
    a long horizontal hulk stepping down from it,
    and a boom cantilevered out to the right, well below the tower top.

`mining_rig_derelict` in this same folder is the *tower* read of the same
reference and deliberately scoped the boom out ("no spoil, no terrain, no
conveyor"). This is the other half of that argument: the horizontal composition,
with the cantilever that makes the silhouette asymmetric. The two share
`slab_block` and `exhaust_stack` and disagree about nothing.

The second thing this model is for is the word *settlement*. A derelict with
nothing added is abandoned; the same derelict with a dozen scrap dwellings
welded to its flanks, lit windows, water tanks and washing lines is occupied.
That reading is carried entirely by `shanty_addon`, and it is why the flanks
matter more here than the roofline does.

Height budget, and why it is what it is:

    -6.0 -  2.0   L0  four blocks, mostly buried — terrain eats this
     2.0 - 10.0   L1  four blocks, the full 64 m length
    10.0 - 18.0   L2  three blocks, first setback
    18.0 - 26.0   L3  two blocks
    26.0 - 34.0   L4  one block — the stack is now a tower
    34.0 - 42.0   L5  one block, torn corner high up
    42.0 - 43.2   crown machine deck, unique geometry
    41.6 - 60.0   Derrick_Mast, set so the tip lands on exactly 60.00

The mass is 64 m long over a 42 m block stack, and the boom carries the
silhouette out to 92 m overall. The tower is 60 m over a 16 m face — 3.75 : 1,
which is the proportion the reference has once the ground is taken out of it.

Everything instances shared mesh data, so nine catwalks cost one catwalk.

    blender --background --python hulk_settlement.py -- --out hulk_settlement.blend

Generation script — historical record. The .blend is the source of truth; never
re-run this over the file it produced.
"""

import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
sys.path.insert(0, LIB)
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

COMPONENTS = os.path.join(LIB, "components")

MATS = [
    "Mat_Metal_HullRust_Orange",   # 0 HULL
    "Mat_Metal_Rust_Heavy",        # 1 RUST
    "Mat_Metal_Steel_Worn",        # 2 STEEL
    "Mat_Metal_Steel_Dark",        # 3 DARK
    "Mat_Neutral_Black_Matte",     # 4 BLACK
    "Mat_Paint_Hull_Bleached",     # 5 BLEACH
    "Mat_Paint_Safety_Orange",     # 6 ORANGE
    "Mat_Emissive_Amber",          # 7 AMBER
]
HULL, RUST, STEEL, DARK, BLACK, BLEACH, ORANGE, AMBER = range(8)

# --- the controlling dimensions -------------------------------------------
BW, BD, BH = 16.0, 14.0, 8.0          # slab_block base envelope
COLS = (-24.0, -8.0, 8.0, 24.0)       # block centres along X
Z0 = -6.0                             # the buried course
TOP = 60.00                           # the number the brief asked for
SPAN_LEN = 26.0                       # gantry_boom's root-to-tip datum
PIVOT = Vector((26.0, 0.0, 17.0))     # boom pivot, on the L1 roof shoulder
RAKE = -5.0                           # boom nose-down, degrees about Y

SOURCES = {}
PLACED = set()


# ---------------------------------------------------------------------------
# Appending and placing
# ---------------------------------------------------------------------------

def load_component(path, collections):
    """Append the named collections and keep only their mesh datablocks."""
    full = os.path.join(COMPONENTS, path)
    if not os.path.exists(full):
        raise SystemExit("Missing component %s — build it first." % full)
    wanted = list(collections)
    with bpy.data.libraries.load(full, link=False) as (src, dst):
        missing = [c for c in wanted if c not in set(src.collections)]
        if missing:
            raise SystemExit("%s has no %s" % (path, missing))
        dst.collections = list(wanted)
    for name in wanted:
        coll = bpy.data.collections[name]
        SOURCES[name] = {o.name: o.data for o in coll.all_objects
                         if o.type == 'MESH'}
        for o in list(coll.all_objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(coll)


def mesh_of(coll_name, contains=None):
    meshes = SOURCES[coll_name]
    if contains is not None:
        for name, data in meshes.items():
            if contains in name:
                return data
    if len(meshes) != 1:
        raise SystemExit("%s holds %d meshes; name one" % (coll_name,
                                                           len(meshes)))
    return next(iter(meshes.values()))


SCALED = {}


def scaled(coll_name, factor, contains=None):
    """A uniformly scaled copy of a component mesh, cached per size.

    Props authored at vehicle scale are simply too small on a 60 m building.
    Baking the factor into one shared mesh copy per size keeps every object in
    the file at scale 1.0 — which the library requires and Unity prefers — and
    pays for the mesh once however many times it is placed.
    """
    data = mesh_of(coll_name, contains)
    key = (data.name, round(factor, 4))
    if key not in SCALED:
        m = data.copy()
        m.transform(Matrix.Diagonal((factor, factor, factor, 1.0)))
        SCALED[key] = m
    return SCALED[key]


def bounds(data):
    """Local-space bounding box of a mesh datablock."""
    vs = [v.co for v in data.vertices]
    return (Vector((min(v.x for v in vs), min(v.y for v in vs),
                    min(v.z for v in vs))),
            Vector((max(v.x for v in vs), max(v.y for v in vs),
                    max(v.z for v in vs))))


def sit(data, x, y, z):
    """Location putting a mesh's base on `z`, centred on (x, y).

    Reused components disagree about where their origin is — some sit on their
    base, some on a connection face — and guessing is how a 60 m model ends up
    with a stack floating 40 cm off its own roof. Measuring the datablock is
    both shorter than looking it up and correct by construction.
    """
    lo, hi = bounds(data)
    return (x - (lo.x + hi.x) / 2.0, y - (lo.y + hi.y) / 2.0, z - lo.z)


def top_at(data, z):
    """The Z location putting a mesh's highest point exactly on `z`."""
    return z - bounds(data)[1].z


def place(name, data, coll, loc=(0, 0, 0), rot=None):
    """Instance a component mesh at a translation and rotation — never a scale."""
    if name in PLACED:
        raise SystemExit("duplicate object name: %s" % name)
    PLACED.add(name)
    m = Matrix.Translation(Vector(loc))
    if rot is not None:
        m = m @ Euler([math.radians(a) for a in rot], 'XYZ').to_matrix().to_4x4()
    obj = bpy.data.objects.new(name, data)
    obj.matrix_world = m
    coll.objects.link(obj)
    return obj


def dedupe_materials():
    """Fold any `Mat_X.001` back onto `Mat_X` after appending from many files."""
    folded = 0
    for mat in list(bpy.data.materials):
        base = mat.name[:-4]
        if len(mat.name) > 4 and mat.name[-4] == '.' \
                and mat.name[-3:].isdigit() and base in bpy.data.materials:
            mat.user_remap(bpy.data.materials[base])
            bpy.data.materials.remove(mat)
            folded += 1
    if folded:
        print("  folded %d duplicate material(s) back onto the palette" % folded)


# ---------------------------------------------------------------------------
# The pieces unique to this building
# ---------------------------------------------------------------------------

def crown_deck(coll, mats):
    """The machine deck at the top of the block stack, z 42.0 - 43.2.

    Unique rather than a component because its only job is to be the junction
    between *this* block stack and *this* derrick mast: a 16 x 14 roof, a
    parapet, and a bolt circle sized to the mast's base. A component would have
    to be parameterised on both, at which point it is this function with extra
    steps.
    """
    p = Part(mats)
    x, w, d = COLS[0], BW, BD

    p.box((x, 0, 42.35), (w + 0.9, d + 0.9, 0.70), DARK)          # deck slab
    p.box((x, 0, 42.05), (w + 0.3, d + 0.3, 0.30), STEEL)
    for s in (-1, 1):                                              # parapet
        p.box((x, s * (d / 2 + 0.30), 43.15), (w + 1.1, 0.22, 1.10), RUST)
        p.box((x + s * (w / 2 + 0.35), 0, 43.15), (0.22, d + 0.2, 1.10), RUST)
    for i in range(9):                                             # posts
        p.box((x - w / 2 + 0.6 + i * (w - 1.2) / 8.0, d / 2 + 0.30, 43.90),
              (0.16, 0.30, 0.60), STEEL)

    # Bolt circle and pedestal the mast steps onto.
    p.cyl((x, 0, 43.00), 3.3, 0.60, 'Z', seg=20, mat=STEEL)
    for i in range(12):
        a = 2 * math.pi * i / 12
        p.cyl((x + 3.05 * math.cos(a), 3.05 * math.sin(a), 43.34), 0.13, 0.22,
              'Z', seg=6, mat=DARK)
    # Bleached paint surviving on the one surface that faces the sky.
    p.box((x, 0, 42.72), (w - 3.2, d - 3.2, 0.06), BLEACH)
    p.box((x - 5.2, -3.6, 42.76), (2.4, 1.6, 0.05), ORANGE)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Hulk_CrownDeck", coll)


def boom_saddle(coll, mats):
    """The plinth under the boom heel, on the L1 roof at x = 26.

    A slew bearing has to land on something that spreads its load into the
    structure, and without it an 8 m machine house sits on a roof looking like
    it was dropped there. Unique because it is shaped to the gap between this
    roof level and this pivot height and nothing else.
    """
    p = Part(mats)
    x, z = PIVOT.x, 10.0

    p.box((x, 0, z + 0.35), (11.5, 12.0, 0.70), DARK)
    p.box((x, 0, z + 0.05), (12.4, 12.8, 0.30), RUST)
    # Four raked legs from the pad up to the bearing ring.
    for sx in (-1, 1):
        for sy in (-1, 1):
            a = Vector((x + sx * 4.6, sy * 4.9, z + 0.7))
            b = Vector((x + sx * 2.7, sy * 2.7, z + 3.9))
            d = b - a
            rot = Vector((0, 0, 1)).rotation_difference(d.normalized())
            p.box((a + b) / 2.0, (0.62, 0.62, d.length), STEEL,
                  rot=rot.to_matrix().to_4x4())
    p.box((x, 0, z + 4.15), (7.2, 7.2, 0.55), STEEL)
    p.cyl((x, 0, z + 4.55), 3.6, 0.42, 'Z', seg=20, mat=DARK)
    for i in range(3):                       # stiffening ribs on the pad
        p.box((x - 3.6 + i * 3.6, 0, z + 0.9), (0.24, 11.0, 0.50), STEEL)
    p.box((x, 0, z + 1.6), (2.2, 2.2, 2.0), BLACK)   # the void under the ring
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Hulk_BoomSaddle", coll)


# ---------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    scene = bpy.context.scene.collection

    load_component("structural/slab_block.blend", [
        "Coll_SlabBlock_Plain", "Coll_SlabBlock_Cantilever",
        "Coll_SlabBlock_Stepped", "Coll_SlabBlock_Buttressed",
        "Coll_SlabBlock_Breached"])
    load_component("mechanical/gantry_boom.blend", [
        "Coll_GantryBoom_Span", "Coll_GantryBoom_Head", "Coll_GantryBoom_Heel",
        "Coll_GantryBoom_Stay", "Coll_GantryBoom_Counter"])
    load_component("structural/shanty_addon.blend", [
        "Coll_Shanty_LeanTo", "Coll_Shanty_Box", "Coll_Shanty_Stack",
        "Coll_Shanty_Awning", "Coll_Shanty_Water"])
    load_component("mechanical/exhaust_stack.blend", [
        "Coll_ExhaustStack_Flue", "Coll_ExhaustStack_Cluster",
        "Coll_ExhaustStack_Scrubber", "Coll_ExhaustStack_Cowl"])
    load_component("mechanical/drill_derrick.blend", [
        "Coll_Derrick_Mast", "Coll_Derrick_PipeRack", "Coll_Derrick_Winch"])
    load_component("structural/catwalk_span.blend", [
        "Coll_Catwalk_Straight", "Coll_Catwalk_Wall", "Coll_Catwalk_Balcony",
        "Coll_Catwalk_Corner", "Coll_Catwalk_Stair", "Coll_Catwalk_Bridge"])
    load_component("structural/truss_frame.blend", [
        "Coll_Truss_Portal", "Coll_Truss_Brace"])
    load_component("structural/cabin_module.blend", [
        "Coll_CabinModule_Cargo", "Coll_CabinModule_Workshop"])
    load_component("structural/hab_capsule.blend", ["Coll_HabCapsule_Pod"])
    load_component("mechanical/conveyor_ramp.blend", [
        "Coll_Conveyor_Ramp", "Coll_Conveyor_Trestle", "Coll_Conveyor_Hopper"])
    load_component("structural/support_leg.blend", ["Coll_SupportLeg_Strut"])
    load_component("structural/handrail.blend", [
        "Coll_Handrail_Ladder", "Coll_Handrail_Straight"])
    load_component("structural/bulkhead_frame.blend", ["Coll_BulkheadFrame_Door"])
    load_component("structural/deck_plate.blend", [
        "Coll_DeckPlate_Worn", "Coll_DeckPlate_Grate"])
    load_component("structural/mast_rig.blend", ["Coll_MastRig_Antenna"])
    load_component("mechanical/pipe_run.blend", [
        "Coll_PipeRun_Straight", "Coll_PipeRun_Elbow", "Coll_PipeRun_Junction"])
    load_component("mechanical/vent_grille.blend", [
        "Coll_Vent_Louvre", "Coll_Vent_Fan"])
    load_component("props/floodlight_bank.blend", [
        "Coll_FloodlightBank_Quad", "Coll_FloodlightBank_Twin",
        "Coll_FloodlightBank_Sweep"])
    load_component("props/light_fixture.blend", [
        "Coll_Light_Clamp", "Coll_Light_Strip"])

    c_mass = collection("Coll_Hulk_Mass", scene)
    c_boom = collection("Coll_Hulk_Boom", scene)
    c_settle = collection("Coll_Hulk_Settlement", scene)
    c_plant = collection("Coll_Hulk_Plant", scene)

    # -- the block stack ---------------------------------------------------
    # Variations are assigned so no two neighbours match, and so the three
    # envelope-breaking ones land where they buy silhouette: the Cantilever at
    # the boom shoulder, the Breached high on the tower where the sky shows
    # through it, the Buttressed low where the mass wants a wider foot.
    LEVELS = (
        (Z0,   (("Plain", 0), ("Buttressed", 0), ("Plain", 180), ("Stepped", 0))),
        (2.0,  (("Buttressed", 0), ("Plain", 180), ("Stepped", 180),
                ("Breached", 0))),
        (10.0, (("Plain", 0), ("Stepped", 0), ("Cantilever", 0), None)),
        (18.0, (("Stepped", 180), ("Plain", 0), None, None)),
        (26.0, (("Buttressed", 180), None, None, None)),
        (34.0, (("Breached", 180), None, None, None)),
    )
    for li, (z, row) in enumerate(LEVELS):
        for ci, entry in enumerate(row):
            if entry is None:
                continue
            kind, yaw = entry
            data = mesh_of("Coll_SlabBlock_%s" % kind)
            place("Mesh_Block_L%d_%s_%s" % (li, "ABCD"[ci], kind), data,
                  c_mass, loc=sit(data, COLS[ci], 0.0, z), rot=(0, 0, yaw))

    crown_deck(c_mass, mats)
    boom_saddle(c_mass, mats)

    # The mast that sets the final height. Placed by measurement so the tip
    # lands on exactly 60.00 rather than wherever its origin happens to be.
    mast = mesh_of("Coll_Derrick_Mast")
    place("Mesh_Tower_Mast", mast, c_mass,
          loc=(COLS[0], 0.0, top_at(mast, TOP)))
    place("Mesh_Tower_PipeRack", mesh_of("Coll_Derrick_PipeRack"), c_mass,
          loc=sit(mesh_of("Coll_Derrick_PipeRack"), COLS[0] + 5.6, 4.2, 43.2),
          rot=(0, 0, 24))
    place("Mesh_Tower_Winch", mesh_of("Coll_Derrick_Winch"), c_mass,
          loc=sit(mesh_of("Coll_Derrick_Winch"), COLS[0] - 4.4, -3.8, 43.2),
          rot=(0, 0, -12))

    # -- the boom ----------------------------------------------------------
    # Every part shares the pivot as its origin, so raking the whole assembly
    # is the same rotation applied five times about one point.
    R = Euler((0, math.radians(RAKE), 0), 'XYZ').to_matrix()
    rake = (0, RAKE, 0)
    for name, coll_name in (("Heel", "Coll_GantryBoom_Heel"),
                            ("Stay", "Coll_GantryBoom_Stay"),
                            ("Counter", "Coll_GantryBoom_Counter"),
                            ("Span", "Coll_GantryBoom_Span")):
        place("Mesh_Boom_%s" % name, mesh_of(coll_name), c_boom,
              loc=tuple(PIVOT), rot=rake)
    place("Mesh_Boom_Head", mesh_of("Coll_GantryBoom_Head"), c_boom,
          loc=tuple(PIVOT + R @ Vector((SPAN_LEN, 0, 0))), rot=rake)

    # Bracing from the mass into the saddle, and a portal straddling the pad.
    place("Mesh_Boom_Portal", mesh_of("Coll_Truss_Portal"), c_boom,
          loc=sit(mesh_of("Coll_Truss_Portal"), PIVOT.x - 9.0, 0.0, 10.0),
          rot=(0, 0, 90))
    for i, sy in enumerate((-1, 1)):
        place("Mesh_Boom_Brace_%d" % i, mesh_of("Coll_Truss_Brace"), c_boom,
              loc=(PIVOT.x - 4.2, sy * 6.4, 10.6), rot=(0, 0, 90 * i))

    # -- the settlement ----------------------------------------------------
    # Origin convention: mounting face at x=0, projecting +X. Yaw -90 hangs a
    # shanty on a -Y flank, +90 on a +Y flank, 0 on the +X end face.
    # Placed so no variation is ever adjacent to itself, and so the lit ones
    # (Box, Stack) sit where a low sun and a silhouette will catch them.
    # Clustered, not sprinkled. Evenly spaced dwellings read as decoration on
    # a machine; three dense terraces with empty flank between them read as
    # people choosing where to live — next to the stairs, out of the wind, on
    # the roof that already had a rail round it.
    SHANTIES = (
        # Terrace A — the L1 roof, hung off the L2 flank. The main street.
        ("LeanTo",  (-22.0,  -7.2, 10.05), -90),
        ("Box",     (-17.0,  -7.2, 12.60), -90),
        ("Stack",   (-12.0,  -7.3, 10.10), -90),
        ("Water",   ( -6.5,  -7.3, 10.15), -90),
        ("Awning",  ( -1.0,  -7.3, 10.05), -90),
        ("Box",     ( 15.0,  -7.3, 10.20), -90),
        # Terrace B — the L0 roof on the +Y side, lowest and busiest.
        ("Box",     (  2.5,   7.3,  2.40),  90),
        ("LeanTo",  (  8.0,   7.3,  2.15),  90),
        ("Stack",   ( 13.5,   7.3,  2.20),  90),
        ("Water",   ( 19.0,   7.3,  2.30),  90),
        # Terrace C — high on the tower, where they read against the sky.
        ("Awning",  (-26.5,   9.2, 26.10),  90),
        ("LeanTo",  (-20.0,   7.3, 18.15),  90),
        ("Box",     (-27.0,  -9.2, 26.20), -90),
        ("Stack",   (-21.0,  -7.3, 34.15), -90),
        # One on the far end wall, so the +X face is not blank.
        ("Water",   ( 32.4,  -2.0,  2.30),   0),
    )
    for i, (kind, loc, yaw) in enumerate(SHANTIES):
        place("Mesh_Shanty_%d_%s" % (i, kind), mesh_of("Coll_Shanty_%s" % kind),
              c_settle, loc=loc, rot=(0, 0, yaw))

    # Containers and capsules parked on the roofs — the manufactured kit the
    # scrap dwellings are built against.
    # Roof furniture goes only where the level above has already stepped back.
    # L2's roof is clear from x = 0 to 16 (L3 stops at x = 0); L3's roof is
    # clear from -14.4 to 0 (L4's buttresses reach -14.4); L1's roof is clear
    # from 16 to 19.8 before the boom saddle claims the rest.
    ROOFS = (("Cargo", (3.0, -3.5, 18.0), 8), ("Workshop", (12.0, -3.0, 18.0), -14))
    for kind, (x, y, z), yaw in ROOFS:
        data = mesh_of("Coll_CabinModule_%s" % kind)
        place("Mesh_Roof_%s" % kind, data, c_settle, loc=sit(data, x, y, z),
              rot=(0, 0, yaw))
    pod = mesh_of("Coll_HabCapsule_Pod")
    for i, (x, y, z, yaw) in enumerate(((-8.0, 4.0, 26.0, 0),
                                        (17.0, -4.0, 10.0, 90))):
        place("Mesh_Roof_Pod_%d" % i, pod, c_settle, loc=sit(pod, x, y, z),
              rot=(0, 0, yaw))

    # Catwalks wrapping the levels, and the stairs and ladders between them.
    walks = (("Straight", (-28.0, -7.9, 10.0), 0), ("Straight", (-21.0, -7.9, 10.0), 0),
             ("Wall", (-8.0, -7.9, 18.0), 0), ("Straight", (-1.0, -7.9, 18.0), 0),
             ("Balcony", (-24.0, 8.2, 26.0), 180), ("Wall", (-24.0, -7.9, 34.0), 0),
             ("Straight", (6.0, 7.9, 10.0), 180), ("Straight", (13.0, 7.9, 10.0), 180),
             ("Corner", (-32.4, -7.9, 10.0), 0), ("Balcony", (16.0, -7.9, 2.0), 0))
    for i, (kind, (x, y, z), yaw) in enumerate(walks):
        data = mesh_of("Coll_Catwalk_%s" % kind)
        place("Mesh_Walk_%d_%s" % (i, kind), data, c_settle,
              loc=sit(data, x, y, z), rot=(0, 0, yaw))
    stair = mesh_of("Coll_Catwalk_Stair")
    for i, (x, y, z, yaw) in enumerate(((-15.0, -8.4, 10.0, 0),
                                        (2.0, 8.4, 2.0, 180),
                                        (-24.0, -8.4, 26.0, 0))):
        place("Mesh_Stair_%d" % i, stair, c_settle, loc=sit(stair, x, y, z),
              rot=(0, 0, yaw))
    place("Mesh_Walk_Bridge", mesh_of("Coll_Catwalk_Bridge"), c_settle,
          loc=sit(mesh_of("Coll_Catwalk_Bridge"), 20.0, 0.0, 18.0), rot=(0, 0, 90))

    # The diagonal: a conveyor gallery from the ground up onto the L1 roof.
    # Hugging the flank at y = -11.5: its 6.9 m depth spans -14.95 to -8.05,
    # which clears the -7.3 face by 0.75 m. Standing it further out, as a first
    # pass did, reads as a separate object parked next to the building rather
    # than as the thing that feeds it.
    CONV_Y = -11.5
    ramp = mesh_of("Coll_Conveyor_Ramp")
    place("Mesh_Conveyor_Ramp", ramp, c_plant,
          loc=sit(ramp, 6.0, CONV_Y, 0.0), rot=(0, 0, 0))
    for i, x in enumerate((-2.0, 10.0)):
        trestle = mesh_of("Coll_Conveyor_Trestle")
        place("Mesh_Conveyor_Trestle_%d" % i, trestle, c_plant,
              loc=sit(trestle, x, CONV_Y, 0.0))
    hopper = mesh_of("Coll_Conveyor_Hopper")
    place("Mesh_Conveyor_Hopper", hopper, c_plant,
          loc=sit(hopper, 19.5, CONV_Y, 12.0))

    # -- plant, lighting and clutter ---------------------------------------
    # Roofline punctuation. `Flue` goes on the crown deck rather than the
    # tower top because the mast owns that and two verticals fighting for the
    # same apex is how a silhouette stops having a top.
    stacks = (("Flue", (-28.0, 4.2, 43.2)), ("Cluster", (-8.0, 3.0, 26.0)),
              ("Cowl", (-8.0, -3.5, 26.0)), ("Scrubber", (8.0, 2.0, 18.3)),
              ("Cluster", (24.0, -2.0, 10.0)), ("Cowl", (14.0, -4.0, 10.0)))
    for i, (kind, (x, y, z)) in enumerate(stacks):
        data = mesh_of("Coll_ExhaustStack_%s" % kind)
        place("Mesh_Stack_%d_%s" % (i, kind), data, c_plant,
              loc=sit(data, x, y, z))

    # Service run across the clear part of the L2 roof.
    for i in range(5):
        place("Mesh_Pipe_Run_%d" % i, scaled("Coll_PipeRun_Straight", 2.4),
              c_plant, loc=(2.0 + i * 3.0, 5.4, 18.5), rot=(0, 0, 0))
    for i in range(2):
        place("Mesh_Pipe_Elbow_%d" % i, scaled("Coll_PipeRun_Elbow", 2.4),
              c_plant, loc=(0.5 + i * 15.0, 5.4, 18.5), rot=(0, 0, i * 90))
    place("Mesh_Pipe_Junction", scaled("Coll_PipeRun_Junction", 2.4), c_plant,
          loc=(8.0, 5.4, 18.5))
    # Louvres on the Buttressed L1 flank, whose face is at y = -9.05, not -7.
    for i in range(5):
        place("Mesh_Vent_%d" % i, scaled("Coll_Vent_Louvre", 2.6), c_plant,
              loc=(-30.0 + i * 3.5, -9.15, 5.6), rot=(0, 0, 180))
    for i in range(2):
        place("Mesh_Vent_Fan_%d" % i, scaled("Coll_Vent_Fan", 2.6), c_plant,
              loc=(-32.35, -2.0 + i * 5.0, 21.0), rot=(0, 0, -90))

    floods = (("Quad", (-24.0, -7.6, 42.0), (0, 0, 0)),
              ("Twin", (-33.8, 0.0, 30.0), (0, 0, -90)),
              ("Quad", (PIVOT.x, -6.3, 13.0), (0, 0, 0)),
              ("Sweep", (-8.0, 7.6, 25.4), (0, 0, 180)),
              ("Twin", (16.0, -7.6, 9.4), (0, 0, 0)),
              ("Sweep", (32.4, 0.0, 9.4), (0, 0, 90)),
              ("Quad", (-16.0, 7.6, 17.4), (0, 0, 180)))
    for i, (kind, loc, rot) in enumerate(floods):
        place("Mesh_Flood_%d_%s" % (i, kind),
              scaled("Coll_FloodlightBank_%s" % kind, 1.9), c_plant, loc=loc,
              rot=rot)
    for i in range(12):
        lvl = i // 6
        place("Mesh_Lamp_Clamp_%d" % i, scaled("Coll_Light_Clamp", 1.7),
              c_plant, loc=(-30.0 + (i % 6) * 6.0, -7.9 if lvl == 0 else 7.9,
                            10.6 + lvl * 8.0),
              rot=(0, 0, 180 if lvl == 0 else 0))
    for i in range(4):
        place("Mesh_Lamp_Strip_%d" % i, scaled("Coll_Light_Strip", 2.1),
              c_plant, loc=(-30.0 + i * 4.0, 9.2, 26.6))
    for i, (x, y) in enumerate(((-19.5, -4.5), (-19.0, 4.5))):
        ant = scaled("Coll_MastRig_Antenna", 1.7)
        place("Mesh_Antenna_%d" % i, ant, c_plant, loc=sit(ant, x, y, 42.7))

    # Player-scale detail at the one place the player stands: the ground.
    for i in range(3):
        data = mesh_of("Coll_Handrail_Ladder")
        place("Mesh_Ladder_%d" % i, data, c_settle,
              loc=sit(data, -12.0 + i * 14.0, -7.4, 2.0))
    for i in range(4):
        place("Mesh_Rail_%d" % i, mesh_of("Coll_Handrail_Straight"), c_settle,
              loc=(-6.0 + i * 2.3, -7.6, 2.05))
    door = mesh_of("Coll_BulkheadFrame_Door")
    for i, (x, y, yaw) in enumerate(((-12.0, -7.15, 0), (14.0, -7.15, 0),
                                     (-33.6, 2.0, 90))):
        place("Mesh_Door_%d" % i, door, c_settle, loc=sit(door, x, y, 2.0),
              rot=(0, 0, yaw))
    for i in range(10):                       # deck plating on the L1 roof
        kind = "Grate" if i % 3 else "Worn"
        data = mesh_of("Coll_DeckPlate_%s" % kind)
        place("Mesh_Deck_%d_%s" % (i, kind), data, c_settle,
              loc=sit(data, -30.0 + (i % 5) * 1.05, 5.5 + (i // 5) * 1.05,
                      10.0))
    for i, sy in enumerate((-1, 1)):           # props against the lower flank
        strut = mesh_of("Coll_SupportLeg_Strut")
        place("Mesh_Strut_%d" % i, strut, c_settle,
              loc=sit(strut, 30.0, sy * 9.0, 0.0), rot=(0, 0, 90 * i))

    dedupe_materials()
    report()

    # The brief said 60 m. Assert it rather than hope.
    zmax = max((o.matrix_world @ v.co).z for o in bpy.data.objects
               if o.type == 'MESH' for v in o.data.vertices)
    print("  highest point: %.2f m" % zmax)
    if abs(zmax - TOP) > 0.02:
        raise SystemExit("Height is %.2f, expected %.2f" % (zmax, TOP))

    save(out)


build()
