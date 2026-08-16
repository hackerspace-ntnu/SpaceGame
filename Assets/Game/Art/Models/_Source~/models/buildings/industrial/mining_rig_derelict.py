"""models/buildings/industrial/mining_rig_derelict — an abandoned desert hulk.

A 61 m stacked-slab mining structure, canted where the ground gave way under
one side, rusted through, and still carrying its access catwalks. This is the
*building only*: the tracked undercarriage, the boom conveyor and the spoil the
real machine would sit in are all deliberately absent, so the model drops into
a scene at whatever depth and angle the terrain wants.

Assembly script. Almost nothing here is modelled — the job is deciding where
sixty-odd component instances go, and building the two things unique to this
one structure: the crown machine house and its parapet.

Height budget, and why:

     0.0 -  8.0   L0  SlabBlock_Buttressed   widest foot, raked ribs
     8.0 - 16.0   L1  SlabBlock_Plain        turned 90 deg -> a 1 m step
    16.0 - 24.0   L2  SlabBlock_Cantilever   the overhang, toward +X
    24.0 - 32.0   L3  SlabBlock_Breached     torn corner, high and lit
    32.0 - 40.0   L4  SlabBlock_Stepped      setback + ledge, bleached paint
    40.0 - 43.4   Crown machine house, unique geometry
    43.4 - 60.9   ExhaustStack_Tall on the crown deck

61 m over a 16 m face is 3.8 : 1, which is what the reference has once the
undercarriage is taken out of it. Every level is turned or offset from the one
below: five identical boxes stacked square would read as a texture error, and
the turn at L1 is what gives the catwalks a ledge to stand on.

**The cant lives on `Empty_MiningRig_Root`**, not baked into the geometry, so
the building can be stood upright again by zeroing one empty's rotation. That
is the one transform in this file that is meant to be edited.

    blender --background --python mining_rig_derelict.py -- --out mining_rig_derelict.blend

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
    "Mat_Paint_Warn_Red",          # 6 RED
    "Mat_Glass_Canopy_Tinted",     # 7 GLASS
    "Mat_Metal_Chrome_Scuffed",    # 8 CHROME
    "Mat_Emissive_Amber",          # 9 AMBER
]
HULL, RUST, STEEL, DARK, BLACK, BLEACH, RED, GLASS, CHROME, AMBER = range(10)

SW, SD, SH = 16.0, 14.0, 8.0          # slab_block footprint and storey height

# (variation, base z, rotation about Z). The turn at L1 and L4 is what stops
# the stack reading as one extruded box.
LEVELS = [
    ("Buttressed", 0.0, 0),
    ("Plain", 8.0, 90),
    ("Cantilever", 16.0, 0),
    ("Breached", 24.0, 0),
    ("Stepped", 32.0, 180),
]

Z_CROWN = 40.0
CROWN_W, CROWN_D, CROWN_H = 9.4, 8.0, 3.4
CX, CY = 1.3, 1.25                    # L4's upper mass centre, in world XY
OVER_X = SW / 2 + 4.6                 # +X reach of the L2 overhang
PROUD = 0.16                          # clearance past the 0.11 m plate field
Z_SADDLE = Z_CROWN + CROWN_H + 0.68   # top face of the crown stack saddles
SADDLE_FLUE = (2.4, -1.8)             # crown-local; the tall guyed flue
SADDLE_SCRUBBER = (-2.8, 1.6)         # crown-local; the fat vessel

SOURCES = {}
SCALED = {}
PLACED = set()


def half(level):
    """Face half-extents of a level, after its own turn."""
    rz = LEVELS[level][2]
    return (SD / 2, SW / 2) if rz % 180 else (SW / 2, SD / 2)


# ---------------------------------------------------------------------------
# Appending and placing
# ---------------------------------------------------------------------------

def load_component(path, collections):
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


def scaled(coll_name, factor, contains=None):
    """A uniformly scaled copy of a component mesh, cached per size.

    Vents, pipes, lamps and deck plates are authored at vehicle scale and are
    simply too small on a 61 m building. Baking the factor into one shared mesh
    per size keeps every object at scale 1.0, which is what the library
    requires and what Unity prefers to import.
    """
    data = mesh_of(coll_name, contains)
    key = (data.name, round(factor, 4))
    if key not in SCALED:
        m = data.copy()
        m.transform(Matrix.Diagonal((factor, factor, factor, 1.0)))
        SCALED[key] = m
    return SCALED[key]


def place(name, data, coll, loc=(0, 0, 0), rot=None):
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
    """Fold any `Mat_X.001` back onto `Mat_X` after appending from nine files."""
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
# The one piece unique to this building
# ---------------------------------------------------------------------------

def crown(coll, mats):
    """The machine house on the roof, and the parapet around it.

    Unique rather than a component because its whole job is to be the specific
    junction between L4's setback, the stack saddle and the deck the catwalks
    arrive at. It is low and wide on purpose: after five 8 m storeys the eye
    needs the stack to be the thing that finishes the silhouette, and a sixth
    tall box would fight it.
    """
    p = Part(mats)
    w, d, h = CROWN_W, CROWN_D, CROWN_H

    p.box((0, 0, h / 2), (w, d, h), HULL)
    for sx in (-1, 1):                                   # corner posts
        for sy in (-1, 1):
            p.box((sx * (w / 2 - 0.55), sy * (d / 2 - 0.55), h / 2),
                  (1.1, 1.1, h), STEEL)
    for side, span, nx in (('Y-', w, 3), ('Y+', w, 3), ('X-', d, 2), ('X+', d, 2)):
        for i in range(nx):
            u = -(span - 1.6) / 2 + (span - 1.6) * (i + 0.5) / nx
            pw = (span - 1.6) / nx - 0.16
            if side[0] == 'Y':
                c = (u, (d / 2 + 0.05) * (1 if side[1] == '+' else -1), h / 2)
                s = (pw, 0.11, h - 1.0)
            else:
                c = ((w / 2 + 0.05) * (1 if side[1] == '+' else -1), u, h / 2)
                s = (0.11, pw, h - 1.0)
            p.box(c, s, RUST if i == 1 else HULL)

    # Deck slab and parapet — the surface the stack and the catwalks land on.
    p.box((0, 0, h + 0.16), (w + 1.5, d + 1.5, 0.32), DARK)
    for sx, sy, sw_, sd_ in ((0, 1, w + 1.5, 0.22), (0, -1, w + 1.5, 0.22),
                             (1, 0, 0.22, d + 1.5), (-1, 0, 0.22, d + 1.5)):
        p.box((sx * (w + 1.3) / 2, sy * (d + 1.3) / 2, h + 0.62),
              (sw_, sd_, 0.60), STEEL)
    # A gap in the parapet where the catwalk arrives, so the ring is not a
    # sealed box the walkways plainly cannot get into.
    p.box((-1.6, -(d + 1.3) / 2, h + 0.62), (2.4, 0.30, 0.62), BLACK)

    p.bevel(width=0.03, segments=1)
    p.finish("Mesh_Crown_MachineHouse", coll, origin=(0, 0, 0))

    # Stack saddles, so the flues land on something rather than on paint.
    # Spaced far enough apart that the flue's guy anchors clear the scrubber's
    # skirt and both stay inside the parapet.
    p2 = Part(mats)
    for x, y in (SADDLE_FLUE, SADDLE_SCRUBBER):
        p2.box((x, y, h + 0.5), (3.8, 3.8, 0.36), DARK)
    p2.bevel(width=0.02, segments=1)
    p2.finish("Mesh_Crown_StackSaddles", coll, origin=(0, 0, 0))


# ---------------------------------------------------------------------------
# Access — catwalks, ladders, stairs
# ---------------------------------------------------------------------------

def wrap_level(coll, z, tag, fx, fy, faces="NSEW", spans=2):
    """Ring a level with wall-mounted catwalks and corner knuckles.

    A `Catwalk_Wall` has its building side on +Y and runs +X, so rz=0 hangs it
    on the -Y face, 180 on +Y, 90 on +X and -90 on -X.
    """
    wall = mesh_of("Coll_Catwalk_Wall")
    corner = mesh_of("Coll_Catwalk_Corner")
    off = 0.95
    if "N" in faces:
        for i in range(spans):
            place("Mesh_Walk_%s_N%d" % (tag, i), wall, coll,
                  loc=(-3.1 * (spans - 1) + i * 6.2, -(fy + off), z))
    if "S" in faces:
        for i in range(spans):
            place("Mesh_Walk_%s_S%d" % (tag, i), wall, coll,
                  loc=(3.1 * (spans - 1) - i * 6.2, fy + off, z), rot=(0, 0, 180))
    if "E" in faces:
        for i in range(spans):
            place("Mesh_Walk_%s_E%d" % (tag, i), wall, coll,
                  loc=(fx + off, -3.1 * (spans - 1) + i * 6.2, z), rot=(0, 0, 90))
    if "W" in faces:
        for i in range(spans):
            place("Mesh_Walk_%s_W%d" % (tag, i), wall, coll,
                  loc=(-(fx + off), 3.1 * (spans - 1) - i * 6.2, z),
                  rot=(0, 0, -90))
    for i, (sx, sy, rz, need) in enumerate(((-1, -1, 0, "NW"), (1, -1, 90, "NE"),
                                            (1, 1, 180, "SE"),
                                            (-1, 1, 270, "SW"))):
        if all(f in faces for f in need):
            place("Mesh_Walk_%s_C%d" % (tag, i), corner, coll,
                  loc=(sx * (fx + off), sy * (fy + off), z), rot=(0, 0, rz))


# ---------------------------------------------------------------------------
# Rig
# ---------------------------------------------------------------------------

def build_rig(coll, movers):
    """Root empty carrying the cant, plus bones for everything that can move.

    Bones drive whole objects by rigid bone-parenting rather than vertex
    weights: every mover here is a hinge, a fan or a hanging bundle, and
    weighted deformation on a rigid part smears it at the pivot.

    The bar for adding a bone is deliberately low — an armature costs almost
    nothing and its absence means the asset cannot be animated without being
    rebuilt — but it is not zero: nothing gets a bone unless it is a separate
    object that could physically move.
    """
    root = bpy.data.objects.new("Empty_MiningRig_Root", None)
    root.empty_display_type = 'PLAIN_AXES'
    root.empty_display_size = 6.0
    coll.objects.link(root)

    arm_data = bpy.data.armatures.new("Arm_MiningRig")
    arm = bpy.data.objects.new("Arm_MiningRig", arm_data)
    coll.objects.link(arm)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for bone_name, (obj, axis) in movers.items():
        b = arm_data.edit_bones.new(bone_name)
        head = obj.matrix_world.translation.copy()
        b.head = head
        b.tail = head + Vector(axis) * 1.2
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()

    for bone_name, (obj, _) in movers.items():
        world = obj.matrix_world.copy()
        obj.parent = arm
        obj.parent_type = 'BONE'
        obj.parent_bone = bone_name
        obj.matrix_world = world

    arm.parent = root
    for o in bpy.data.objects:
        if o.parent is None and o is not root and o.type in {'MESH', 'EMPTY'}:
            world = o.matrix_world.copy()
            o.parent = root
            o.matrix_world = world

    # The cant. One side of whatever this stood on gave way; the machine went
    # over and stopped when the base dug in. Kept on the empty so it is a
    # single value to zero, not a bake to undo.
    root.rotation_euler = Euler((math.radians(-3.2), math.radians(7.6), 0.0),
                                'XYZ')
    return root


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    load_component("structural/slab_block.blend",
                   ["Coll_SlabBlock_Plain", "Coll_SlabBlock_Cantilever",
                    "Coll_SlabBlock_Stepped", "Coll_SlabBlock_Buttressed",
                    "Coll_SlabBlock_Breached"])
    load_component("mechanical/exhaust_stack.blend",
                   ["Coll_ExhaustStack_Flue", "Coll_ExhaustStack_Cluster",
                    "Coll_ExhaustStack_Scrubber", "Coll_ExhaustStack_Cowl"])
    load_component("structural/window_bank.blend",
                   ["Coll_WindowBank_Porthole", "Coll_WindowBank_SlotRow",
                    "Coll_WindowBank_Shuttered", "Coll_WindowBank_Blown"])
    load_component("props/hull_stencil.blend",
                   ["Coll_HullStencil_Chevron", "Coll_HullStencil_DangerBand",
                    "Coll_HullStencil_Arrow", "Coll_HullStencil_Roundel",
                    "Coll_HullStencil_Placard"])
    load_component("structural/catwalk_span.blend",
                   ["Coll_Catwalk_Wall", "Coll_Catwalk_Corner",
                    "Coll_Catwalk_Balcony", "Coll_Catwalk_Stair",
                    "Coll_Catwalk_Straight"])
    load_component("structural/handrail.blend",
                   ["Coll_Handrail_Straight", "Coll_Handrail_Ladder",
                    "Coll_Handrail_Corner", "Coll_Handrail_Gate"])
    load_component("structural/deck_plate.blend",
                   ["Coll_DeckPlate_Grate", "Coll_DeckPlate_Worn",
                    "Coll_DeckPlate_Hatch"])
    load_component("structural/hull_plate.blend",
                   ["Coll_HullPlate_Patched", "Coll_HullPlate_Riveted"])
    load_component("structural/bulkhead_frame.blend",
                   ["Coll_BulkheadFrame_Door"])
    load_component("mechanical/vent_grille.blend",
                   ["Coll_Vent_Louvre", "Coll_Vent_Fan", "Coll_Vent_Scoop"])
    load_component("mechanical/pipe_run.blend",
                   ["Coll_PipeRun_Straight", "Coll_PipeRun_Elbow",
                    "Coll_PipeRun_CableBundle"])
    load_component("props/floodlight_bank.blend",
                   ["Coll_FloodlightBank_Quad", "Coll_FloodlightBank_Twin",
                    "Coll_FloodlightBank_Sweep"])
    load_component("props/light_fixture.blend",
                   ["Coll_Light_Clamp", "Coll_Light_Strip"])

    root_coll = collection("Coll_MiningRigDerelict")
    c_comp = collection("Coll_Components", root_coll)
    c_uniq = collection("Coll_Unique", root_coll)
    c_rig = collection("Coll_Rig", root_coll)

    # -- the stack ----------------------------------------------------------
    for i, (kind, z, rz) in enumerate(LEVELS):
        place("Mesh_L%d_%s" % (i, kind), mesh_of("Coll_SlabBlock_%s" % kind),
              c_comp, loc=(0, 0, z), rot=(0, 0, rz))

    crown(c_uniq, mats)
    for o in c_uniq.objects:
        o.location = (CX, CY, Z_CROWN)

    # -- flues --------------------------------------------------------------
    # The guyed flue is the vertical accent that finishes the silhouette; the
    # scrubber beside it is the mass that stops the roofline reading as a
    # pincushion. The cluster stands lower, out on L4's shelf, so the roof has
    # three different heights rather than one line of equal spikes.
    stack_flue = place("Mesh_Stack_Flue", mesh_of("Coll_ExhaustStack_Flue"),
                       c_comp, loc=(CX + SADDLE_FLUE[0], CY + SADDLE_FLUE[1],
                                    Z_SADDLE))
    place("Mesh_Stack_Scrubber", mesh_of("Coll_ExhaustStack_Scrubber"), c_comp,
          loc=(CX + SADDLE_SCRUBBER[0], CY + SADDLE_SCRUBBER[1], Z_SADDLE))
    place("Mesh_Stack_Cluster", mesh_of("Coll_ExhaustStack_Cluster"), c_comp,
          loc=(-6.0, -4.9, 37.0), rot=(0, 0, 18))
    # Cowls are the filler: one on the shelf the L2 overhang leaves (L3 above
    # it is only 16 m wide), one on L4's shelf, two on the crown deck.
    for i, (x, y, z, rz) in enumerate(((SW / 2 + 2.4, -1.2, 24.0, 20),
                                       (-6.4, 3.8, 37.0, -14),
                                       (CX - 4.2, CY - 3.2, Z_CROWN + CROWN_H + 0.32, 8),
                                       (CX + 4.4, CY + 3.0, Z_CROWN + CROWN_H + 0.32, -22))):
        place("Mesh_Cowl_%d" % i, mesh_of("Coll_ExhaustStack_Cowl"), c_comp,
              loc=(x, y, z), rot=(0, 0, rz))

    # -- openings -----------------------------------------------------------
    # Offset PROUD past the plate field so frames clear the plating; each
    # component's own dark reveal covers whatever plate sits behind it.
    fx1, fy1 = half(1)
    fx2, fy2 = half(2)
    fx3, fy3 = half(3)
    fx4, fy4 = half(4)
    place("Mesh_Win_Porthole_L1", mesh_of("Coll_WindowBank_Porthole"), c_comp,
          loc=(-2.4, -(fy1 + PROUD), 12.2))
    place("Mesh_Win_Shuttered_L2", mesh_of("Coll_WindowBank_Shuttered"), c_comp,
          loc=(2.6, -(fy2 + PROUD), 20.2))
    place("Mesh_Win_Blown_L3", mesh_of("Coll_WindowBank_Blown"), c_comp,
          loc=(-(fx3 + PROUD), -1.8, 28.4), rot=(0, 0, -90))
    place("Mesh_Win_Porthole_L4", mesh_of("Coll_WindowBank_Porthole"), c_comp,
          loc=(3.2, -(fy4 + PROUD), 34.6))
    place("Mesh_Win_SlotRow_Crown", mesh_of("Coll_WindowBank_SlotRow"), c_comp,
          loc=(CX, CY - CROWN_D / 2 - PROUD, Z_CROWN + 1.9))
    place("Mesh_Door_Base", mesh_of("Coll_BulkheadFrame_Door"), c_comp,
          loc=(-4.2, -(SD / 2 + PROUD), 1.2))

    # -- markings -----------------------------------------------------------
    marks = (
        ("Chevron", "Coll_HullStencil_Chevron", (4.4, -(fy1 + PROUD), 13.4), 0),
        ("DangerBand_Base", "Coll_HullStencil_DangerBand",
         (0.0, -(SD / 2 + PROUD), 6.4), 0),
        ("DangerBand_Over", "Coll_HullStencil_DangerBand",
         (OVER_X + PROUD, 0.0, 22.6), 90),
        ("Arrow", "Coll_HullStencil_Arrow", (-4.6, -(fy2 + PROUD), 21.4), 0),
        ("Roundel", "Coll_HullStencil_Roundel",
         (-(fx1 + PROUD), 1.4, 13.0), -90),
        ("Chevron_L4", "Coll_HullStencil_Chevron",
         (fx4 + PROUD, -2.0, 34.8), 90),
    )
    for name, coll_name, loc, rz in marks:
        place("Mesh_Mark_%s" % name, mesh_of(coll_name), c_comp, loc=loc,
              rot=(0, 0, rz))
    for i, (x, y, z, rz) in enumerate(((-5.0, -(SD / 2 + PROUD), 2.6, 0),
                                       (-3.4, -(SD / 2 + PROUD), 2.6, 0),
                                       (-(fx2 + PROUD), 3.0, 18.4, -90))):
        place("Mesh_Mark_Placard_%d" % i, mesh_of("Coll_HullStencil_Placard"),
              c_comp, loc=(x, y, z), rot=(0, 0, rz))

    # -- weld-on repair plates ---------------------------------------------
    # Scattered where the corrosion is worst, and never two of the same kind
    # adjacent: a repeated patch reads as a tiling artefact.
    patches = ((-6.2, -(fy1 + 0.1), 10.4, 0), (5.8, -(fy2 + 0.1), 17.8, 0),
               (-(fx3 + 0.1), 3.2, 26.4, -90), (fx3 + 0.1, -4.0, 30.2, 90),
               (2.0, -(fy4 + 0.1), 38.0, 0), (-(fx1 + 0.1), -3.6, 14.6, -90),
               (7.2, SD / 2 + 0.1, 4.6, 180), (-2.8, SD / 2 + 0.1, 6.8, 180))
    for i, (x, y, z, rz) in enumerate(patches):
        kind = "Patched" if i % 2 else "Riveted"
        place("Mesh_Patch_%d_%s" % (i, kind),
              scaled("Coll_HullPlate_%s" % kind, 2.4), c_comp,
              loc=(x, y, z), rot=(0, 0, rz))

    # -- access -------------------------------------------------------------
    # Full wraps only where the building actually steps; partial runs between.
    # Ringing every level identically would read as a multi-storey car park.
    wrap_level(c_comp, 15.4, "L1", *half(1))
    wrap_level(c_comp, 23.4, "L2", *half(2), faces="NW", spans=2)
    wrap_level(c_comp, 31.4, "L3", *half(3), faces="NEW")
    wrap_level(c_comp, 36.6, "L4", *half(4), faces="NE", spans=2)
    wrap_level(c_comp, Z_CROWN - 0.4, "L5", SW / 2, SD / 2, faces="NW")

    # A balcony carries its building edge at y = +0.96 in its own frame, so it
    # has to be hung at -(fy + 0.9) to touch the wall. Hanging it at a round
    # offset instead leaves a 1.4 m gap that reads as a floating platform.
    for i, (x, z) in enumerate(((-2.2, 17.8), (3.4, 26.2), (-4.6, 33.0))):
        place("Mesh_Balcony_%d" % i, mesh_of("Coll_Catwalk_Balcony"), c_comp,
              loc=(x, -(half(2)[1] + 0.9), z))
    # Stair flights down the -X face. The component's origin is at its **top
    # landing** and it descends 5.2 m, so the placement Z is the walkway it
    # arrives at, not the floor it leaves. Each is set one stair-width outboard
    # of that walkway's deck edge so the two overlap and visibly connect.
    #
    # The lower ends stop in mid-air on purpose: the flights that once carried
    # on down are gone, which is the same story the breach and the toppled
    # cowl are telling. The continuous climb is the ladder run on the +X side.
    for i, z_top in enumerate((15.4, 23.4, 31.4)):
        fx = half(i + 1)[0]
        place("Mesh_Stair_%d" % i, mesh_of("Coll_Catwalk_Stair"), c_comp,
              loc=(-(fx + 1.9), 3.0 if i % 2 == 0 else -3.0, z_top),
              rot=(0, 0, -90 if i % 2 == 0 else 90))
    # Ladder runs, offset a little each storey so the climb reads as a zigzag.
    for i in range(6):
        place("Mesh_Ladder_%d" % i, mesh_of("Coll_Handrail_Ladder"), c_comp,
              loc=(SW / 2 + 1.1, -2.0 + (i % 2) * 2.4, 8.4 + i * 3.4))
    for i in range(5):
        place("Mesh_Rail_Crown_%d" % i, mesh_of("Coll_Handrail_Straight"),
              c_comp, loc=(CX - 4.0 + i * 2.24, CY + CROWN_D / 2 + 0.5,
                           Z_CROWN + CROWN_H + 0.35), rot=(0, 0, 90))
    place("Mesh_Rail_Crown_Gate", mesh_of("Coll_Handrail_Gate"), c_comp,
          loc=(CX - 1.6, CY - CROWN_D / 2 - 0.5, Z_CROWN + CROWN_H + 0.35),
          rot=(0, 0, 90))
    for i, rz in enumerate((0, 90)):
        place("Mesh_Rail_Corner_%d" % i, mesh_of("Coll_Handrail_Corner"),
              c_comp, loc=(CX + (-1) ** i * (CROWN_W / 2 + 0.5),
                           CY + CROWN_D / 2 + 0.5, Z_CROWN + CROWN_H + 0.35),
              rot=(0, 0, rz))

    # Crown decking: grate and worn alternated, never the same tile twice in a
    # row, because a solid patch of one tile is a texture rather than a floor.
    for i in range(12):
        kind = ("Grate", "Worn")[(i + i // 4) % 2]
        place("Mesh_Deck_%d_%s" % (i, kind),
              scaled("Coll_DeckPlate_%s" % kind, 2.0), c_comp,
              loc=(CX - 2.9 + (i % 4) * 1.94, CY - 1.94 + (i // 4) * 1.94,
                   Z_CROWN + CROWN_H + 0.34))
    hatch = place("Mesh_Deck_Hatch", scaled("Coll_DeckPlate_Hatch", 2.0),
                  c_comp, loc=(CX + 3.05, CY + 1.94, Z_CROWN + CROWN_H + 0.34))

    # -- services -----------------------------------------------------------
    for i in range(5):
        place("Mesh_Vent_Louvre_%d" % i, scaled("Coll_Vent_Louvre", 2.4),
              c_comp, loc=(-4.8 + i * 2.4, -(fy1 + 0.05), 9.6))
    for i in range(3):
        place("Mesh_Vent_Scoop_%d" % i, scaled("Coll_Vent_Scoop", 2.4), c_comp,
              loc=(fx2 + 0.05, -3.6 + i * 3.6, 18.6), rot=(0, 0, 90))
    fan_a = place("Mesh_Vent_Fan_A", scaled("Coll_Vent_Fan", 2.6), c_comp,
                  loc=(CX - 3.2, CY - CROWN_D / 2 - 0.05, Z_CROWN + 2.1))
    fan_b = place("Mesh_Vent_Fan_B", scaled("Coll_Vent_Fan", 2.6), c_comp,
                  loc=(CX + 3.2, CY - CROWN_D / 2 - 0.05, Z_CROWN + 2.1))

    # A pipe run climbing the +Y face, elbowing over at the top.
    for i in range(6):
        place("Mesh_Pipe_%d" % i, scaled("Coll_PipeRun_Straight", 2.2), c_comp,
              loc=(-6.0, SD / 2 + 0.35, 9.0 + i * 2.2), rot=(0, 90, 0))
    place("Mesh_Pipe_Elbow", scaled("Coll_PipeRun_Elbow", 2.2), c_comp,
          loc=(-6.0, SD / 2 + 0.35, 22.4), rot=(0, 90, 0))
    # Cable bundles hanging free off the overhang's lip — the reference's loose
    # lines. Chained end to end down two vertical runs rather than scattered:
    # a bundle is 2.65 m long, so consecutive pieces have to step a full length
    # in Z or they read as five separate stubs instead of one dropped cable.
    cables = []
    run = 2.65
    for i in range(6):
        lane = i % 2
        cables.append(place(
            "Mesh_Cable_%d" % i, scaled("Coll_PipeRun_CableBundle", 2.6),
            c_comp, loc=(OVER_X + 0.35 + lane * 0.55, -2.4 + lane * 3.6,
                         22.2 - (i // 2) * run),
            rot=(0, 90, 4 - lane * 8)))

    # -- lighting -----------------------------------------------------------
    floods = (("Quad", (0.0, -(SD / 2 + 0.6), 39.4), (0, 0, 0)),
              ("Quad", (CX, CY - CROWN_D / 2 - 0.6, Z_CROWN + CROWN_H + 1.4),
               (0, 0, 0)),
              ("Twin", (-(fx2 + 0.6), 2.0, 23.0), (0, 0, -90)),
              ("Twin", (fx1 + 0.6, 1.0, 15.0), (0, 0, 90)),
              ("Quad", (-6.4, -(SD / 2 + 0.6), 7.2), (0, 0, 0)))
    for i, (kind, loc, rz) in enumerate(floods):
        place("Mesh_Flood_%d_%s" % (i, kind),
              scaled("Coll_FloodlightBank_%s" % kind, 1.8), c_comp, loc=loc,
              rot=rz)
    sweep_a = place("Mesh_Flood_Sweep_A",
                    scaled("Coll_FloodlightBank_Sweep", 1.8), c_comp,
                    loc=(CX - 3.4, CY - 2.6, Z_CROWN + CROWN_H + 1.1))
    sweep_b = place("Mesh_Flood_Sweep_B",
                    scaled("Coll_FloodlightBank_Sweep", 1.8), c_comp,
                    loc=(-(SW / 2 + 0.6), -4.0, 31.6), rot=(0, 0, -90))
    for i in range(8):
        place("Mesh_Lamp_Clamp_%d" % i, scaled("Coll_Light_Clamp", 1.6),
              c_comp, loc=(-5.4 + (i % 4) * 3.6, -(SD / 2 + 1.5),
                           15.0 + (i // 4) * 16.0), rot=(0, 0, 180))
    for i in range(3):
        place("Mesh_Lamp_Strip_%d" % i, scaled("Coll_Light_Strip", 2.0),
              c_comp, loc=(CX - 3.0 + i * 3.0, CY + CROWN_D / 2 - 0.3,
                           Z_CROWN + CROWN_H - 0.2))

    # -- rig ----------------------------------------------------------------
    movers = {
        "Bone_VentFan_A": (fan_a, (0, -1, 0)),
        "Bone_VentFan_B": (fan_b, (0, -1, 0)),
        "Bone_FloodSweep_Crown": (sweep_a, (0, 0, 1)),
        "Bone_FloodSweep_West": (sweep_b, (0, 0, 1)),
        "Bone_RoofHatch": (hatch, (0, 1, 0)),
        "Bone_StackFlue": (stack_flue, (0, 0, 1)),
    }
    for i, c in enumerate(cables):
        movers["Bone_CableSway_%d" % i] = (c, (0, 0, -1))
    build_rig(c_rig, movers)

    dedupe_materials()
    report()
    save(out)


build()
