"""models/buildings/outpost/relay_outpost — a crewed desert relay station.

Built from a concept reference: a pale blue prefab block dropped on graded sand,
with a tapered octagonal mast rising off its roof to a domed sensor drum ringed
by a gallery, and a working yard of awnings, benches, crates and drums spread
round the front.

Almost nothing here is modelled. The script's job is to decide *where* ninety-odd
instances go, and to build the three pieces that are genuinely specific to this
one building — the saddle the mast stands on, the plant rack bolted to its face,
and the stoop at the door.

Height budget, and why it is what it is:

     0.00 -  0.34   graded plinth
     0.34 -  4.20   the prefab block, roof deck at 4.20
     4.20 -  5.10   tower saddle, unique geometry
     5.10 - 13.70   `StationTower_Taper`, 8.6 m of batter
    13.70 - 18.23   `SensorCupola_Dome`, gallery deck at 14.07
            18.50   the gallery aerial, which sets the final height

18.50 m to the tip over a 13 m block: the mast reads as 3.5x the building it
stands on, which is the proportion the reference has. The block is deliberately
low and wide — a taller base would cost the mast its dominance.

    blender --background --python relay_outpost.py -- --out relay_outpost.blend

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
    "Mat_Paint_Blue_Station",    # 0
    "Mat_Paint_White_Arctic",    # 1
    "Mat_Metal_Steel_Worn",      # 2
    "Mat_Metal_Steel_Dark",      # 3
    "Mat_Neutral_Slate_Dark",    # 4
    "Mat_Metal_Rust_Heavy",      # 5
    "Mat_Metal_HullRust_Orange", # 6
    "Mat_Neutral_Black_Matte",   # 7
    "Mat_Paint_Warn_Red",        # 8
    "Mat_Metal_Copper_Oxide",    # 9
    "Mat_Emissive_Amber",        # 10
    "Mat_Paint_Safety_Orange",   # 11
]
(BLUE, WHITE, STEEL, DARK, SLATE, RUST, ORANGE, BLACK, RED, COPPER, AMBER,
 SAFETY) = range(12)

# --- the building's controlling dimensions ---------------------------------
BW, BD = 13.0, 10.0          # prefab block footprint
FX, FY = BW / 2, BD / 2      # its face planes: front is -Y
Z_ROOF = 4.20                # top of the roof slab
Z_SADDLE = 5.10              # top of the saddle / foot of the mast
Z_SHAFT = 13.70              # top of the mast / foot of the cupola
Z_GALLERY = 14.07            # cupola gallery deck
TIP = 18.50                  # where the aerial ends, and the model with it
TX, TY = 3.2, 0.8            # mast centre on the roof, clear of the roof plant
Z_PAD = 4.54                 # top of the saddle's bedding pad
TAPER_R0, TAPER_R1 = 2.49, 1.73    # `StationTower_Taper`, foot and head
TAPER_H = 8.6
OCTA_FLAT = math.cos(math.pi / 8)  # across-corners -> across-flats


def shaft_face(z):
    """Half-width of the mast across its flats at height `z`.

    The mast is placed unrotated so a facet faces each of +-X and +-Y, and its
    ladder is bracketed off the +X facet. On a tapered shaft that face moves
    inward as it climbs, so each ladder lift has to be placed against the local
    face rather than all three on one plumb line — which is what real stepped
    standoff brackets do, and what a plumb ladder against a battered shaft
    visibly fails to do.
    """
    t = max(0.0, min(1.0, (z - Z_SADDLE) / TAPER_H))
    return (TAPER_R0 + (TAPER_R1 - TAPER_R0) * t) * OCTA_FLAT

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

    Several reused props — vents, pipe runs, lamps — are authored at vehicle
    scale and read too small on a building. Taking the scale on the *object*
    would leave this file full of objects at 1.8, which the library forbids and
    Unity would rather not import either. Baking it into one shared copy per
    size keeps every object at scale 1.0 and pays for the mesh once.
    """
    data = mesh_of(coll_name, contains)
    key = (data.name, round(factor, 4))
    if key not in SCALED:
        m = data.copy()
        m.transform(Matrix.Diagonal((factor, factor, factor, 1.0)))
        SCALED[key] = m
    return SCALED[key]


def fit_scale(coll_name, base_z, target_top, contains=None):
    """The factor that lands a component's top at `target_top` when its foot
    sits at `base_z`. Used once, for the aerial that sets the model's height."""
    data = mesh_of(coll_name, contains)
    zs = [v.co.z for v in data.vertices]
    return (target_top - base_z) / (max(zs) - min(zs))


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
    """Fold any `Mat_X.001` back onto `Mat_X` after appending from twelve files."""
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
# The three pieces that are unique to this building
# ---------------------------------------------------------------------------

def tower_saddle(coll, mats):
    """The 0.9 m plinth between a flat roof and a round mast.

    Unique rather than a component because its whole job is to be the specific
    junction between *this* roof and *this* shaft. `StationTower_Flare` is the
    library's general answer, but it is 1.9 m tall and spreads to 6.8 m across,
    which on a 10 m deep roof would leave no roof at all.
    """
    p = Part(mats)
    n, r0, r1 = 8, 3.05, 2.62
    ph = math.pi / n

    def octa(r):
        return [(r * math.cos(2 * math.pi * i / n + ph),
                 r * math.sin(2 * math.pi * i / n + ph)) for i in range(n)]

    p.box((0, 0, 0.1), (6.3, 6.3, 0.2), SLATE)                # bedding pad
    p.box((0, 0, 0.26), (5.9, 5.9, 0.16), STEEL)
    p.shade(p.loft([(0.3, octa(r0)), (0.9, octa(r1))], axis='Z', mat=WHITE),
            False)
    p.shade(p.loft([(0.84, octa(r1 + 0.14)), (0.9, octa(r1 + 0.14))], axis='Z',
                   mat=STEEL), False)
    for i in range(n):                                        # holding-down cleats
        a = 2 * math.pi * i / n + ph
        cx, cy = r0 * 0.98 * math.cos(a), r0 * 0.98 * math.sin(a)
        p.box((cx, cy, 0.42), (0.42, 0.5, 0.34), STEEL,
              rot=Matrix.Rotation(a, 4, 'Z'))
        for s in (-0.3, 0.3):
            p.cyl((cx - math.sin(a) * s, cy + math.cos(a) * s, 0.62), 0.05, 0.12,
                  seg=6, mat=DARK)
    p.box((0, 0, 0.08), (6.9, 6.9, 0.1), DARK)                # spread footing
    for sx in (-1, 1):                                        # kerb, to catch runoff
        p.box((sx * 3.3, 0, 0.16), (0.16, 6.6, 0.32), SLATE)
        p.box((0, sx * 3.3, 0.16), (6.6, 0.16, 0.32), SLATE)
    p.box((-2.3, -2.3, 0.2), (1.1, 1.1, 0.4), RUST)           # a weathered corner
    p.bevel(width=0.016)
    return p.finish("Mesh_Outpost_TowerSaddle", coll, origin=(0, 0, 0))


def plant_rack(coll, mats):
    """The dark machinery mass bolted to the mast's front face at roof level.

    The reference's most distinctive junction: a condenser stack, a pipe gallery
    and a switch cabinet hung off the shaft rather than standing on the roof.
    It is unique because it is shaped by the batter of *this* shaft — a general
    component would have to be flat-backed and would gap.
    """
    p = Part(mats)
    w, d, h = 4.4, 1.9, 2.5
    p.box((0, -d / 2, h / 2), (w, d, h), SLATE)               # the mass
    p.box((0, -d / 2, h - 0.1), (w + 0.2, d + 0.2, 0.2), STEEL)
    p.box((0, -d / 2, 0.1), (w + 0.3, d + 0.3, 0.2), DARK)
    # Condenser bank: four finned drums across the front, cowled.
    for i in range(4):
        x = (i / 3.0 - 0.5) * (w - 1.0)
        p.cyl((x, -d - 0.06, h * 0.6), 0.42, 0.5, axis='Y', seg=14, mat=STEEL)
        for k in range(5):
            p.cyl((x, -d - 0.2 + k * 0.07, h * 0.6), 0.5, 0.02, axis='Y',
                  seg=14, mat=DARK)
        p.cyl((x, -d - 0.34, h * 0.6), 0.46, 0.1, axis='Y', seg=14, mat=BLACK)
        p.box((x, -d - 0.3, h * 0.6), (0.9, 0.06, 0.9), DARK)  # guard mesh
    # Pipe gallery along the top, elbowing up toward the shaft.
    for i in range(3):
        y = -d - 0.1 + i * 0.42
        p.cyl((0, y, h + 0.24), 0.11, w * 0.92, axis='X', seg=10,
              mat=(COPPER, STEEL, RUST)[i])
    for sx in (-1, 1):
        p.cyl((sx * w * 0.44, -d + 0.4, h + 0.9), 0.11, 1.4, seg=10, mat=COPPER)
        p.cyl((sx * w * 0.44, -d + 0.4, h + 0.24), 0.15, 0.18, seg=10, mat=STEEL)
    # Switch cabinet and a gauge board on the flank.
    p.box((-w / 2 - 0.28, -d * 0.7, h * 0.52), (0.56, 1.0, 1.5), SLATE)
    p.box((-w / 2 - 0.58, -d * 0.7, h * 0.52), (0.06, 0.8, 1.2), DARK)
    p.cyl((-w / 2 - 0.62, -d * 0.7 - 0.24, h * 0.72), 0.09, 0.05, axis='X',
          seg=10, mat=WHITE)
    p.cyl((-w / 2 - 0.62, -d * 0.7 + 0.24, h * 0.72), 0.06, 0.05, axis='X',
          seg=10, mat=AMBER)
    p.box((w / 2 + 0.18, -d * 0.6, h * 0.4), (0.36, 0.8, 1.0), DARK)
    p.box((0, -d - 0.42, h * 0.16), (w * 0.9, 0.06, 0.3), SAFETY)  # hazard band
    # Back wings raked to the octagon's next facet either side. Without them the
    # rack's flat 4.4 m back meets a 1.95 m facet and stands off at both ends.
    for sx in (-1, 1):
        p.box((sx * w * 0.40, -d * 0.30, h * 0.5), (1.3, 1.1, h * 0.92), SLATE,
              rot=Matrix.Rotation(math.radians(sx * 45), 4, 'Z'))
        p.box((sx * w * 0.46, -d * 0.16, h * 0.5), (0.16, 0.7, h * 0.8), STEEL,
              rot=Matrix.Rotation(math.radians(sx * 45), 4, 'Z'))
    p.greeble((-w * 0.4, -d * 0.2, h + 0.06), (w * 0.4, -0.1, h + 0.2), 5,
              seed=7, scale=(0.16, 0.4), mat=DARK)
    p.bevel(width=0.014)
    return p.finish("Mesh_Outpost_PlantRack", coll, origin=(0, 0, 0))


def stoop(coll, mats):
    """Two steps and a scuffed apron at the door. Small, but a doorway 0.34 m
    off the sand with nothing under it is the first thing that reads as wrong."""
    p = Part(mats)
    p.box((0, -0.34, 0.24), (2.5, 1.5, 0.1), STEEL)           # top landing
    p.box((0, -0.34, 0.1), (2.3, 1.3, 0.2), SLATE)
    p.box((0, -1.1, 0.08), (2.1, 0.62, 0.16), STEEL)          # lower step
    p.box((0, -1.1, 0.02), (1.9, 0.5, 0.12), SLATE)
    p.box((0, -1.7, 0.02), (2.9, 0.7, 0.06), DARK)            # scuffed apron
    for sx in (-1, 1):                                        # grab rails
        p.cyl((sx * 1.16, -0.34, 0.62), 0.045, 0.76, seg=8, mat=STEEL)
        p.cyl((sx * 1.16, -0.72, 0.98), 0.045, 0.78, axis='Y', seg=8, mat=STEEL)
        p.cyl((sx * 1.16, -1.1, 0.66), 0.045, 0.7, seg=8, mat=STEEL,
              rot=Matrix.Rotation(math.radians(18), 4, 'X'))
        p.cyl((sx * 1.16, -0.34, 0.3), 0.08, 0.12, seg=8, mat=RUST)
    p.box((0.9, -0.34, 0.3), (0.5, 1.3, 0.02), RUST)          # tread wear
    p.box((-0.4, -1.72, 0.06), (0.9, 0.5, 0.04), SAFETY)      # a mat, half buried
    p.bevel(width=0.01)
    return p.finish("Mesh_Outpost_Stoop", coll, origin=(0, 0, 0))


# ---------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------

COMPONENT_SET = {
    "structural/prefab_hab.blend": ["Coll_PrefabHab_Long"],
    "structural/station_tower.blend": ["Coll_StationTower_Taper"],
    "structural/sensor_cupola.blend": ["Coll_SensorCupola_Dome"],
    "structural/awning_shade.blend": ["Coll_Awning_Square", "Coll_Awning_LeanTo",
                                      "Coll_Awning_Sagging"],
    "structural/handrail.blend": ["Coll_Handrail_Ladder", "Coll_Handrail_Gate",
                                  "Coll_Handrail_Straight"],
    "structural/deck_plate.blend": ["Coll_DeckPlate_Grate", "Coll_DeckPlate_Worn",
                                    "Coll_DeckPlate_Solid"],
    "structural/hull_plate.blend": ["Coll_HullPlate_Patched",
                                    "Coll_HullPlate_Riveted",
                                    "Coll_HullPlate_Ribbed"],
    "structural/bulkhead_frame.blend": ["Coll_BulkheadFrame_Door",
                                        "Coll_BulkheadFrame_HatchRim"],
    "structural/mast_rig.blend": ["Coll_MastRig_Antenna", "Coll_MastRig_Windvane"],
    "mechanical/pipe_run.blend": ["Coll_PipeRun_Straight", "Coll_PipeRun_Elbow",
                                  "Coll_PipeRun_Junction", "Coll_PipeRun_Duct",
                                  "Coll_PipeRun_CableBundle"],
    "mechanical/vent_grille.blend": ["Coll_Vent_Louvre", "Coll_Vent_Fan",
                                     "Coll_Vent_Scoop", "Coll_Vent_MeshScreen"],
    "props/floodlight_bank.blend": ["Coll_FloodlightBank_Twin",
                                    "Coll_FloodlightBank_Single",
                                    "Coll_FloodlightBank_Sweep"],
    "props/light_fixture.blend": ["Coll_Light_Clamp", "Coll_Light_Dome",
                                  "Coll_Light_Festoon", "Coll_Light_Strip"],
    "props/supply_crate.blend": ["Coll_Crate_Small", "Coll_Crate_Large",
                                 "Coll_Crate_Long", "Coll_Crate_Stack",
                                 "Coll_Crate_Open", "Coll_Crate_Pallet"],
    "props/fuel_barrel.blend": ["Coll_Barrel_Drum", "Coll_Barrel_Stack",
                                "Coll_Barrel_Jerrican", "Coll_Barrel_GasBottles",
                                "Coll_Barrel_Tank"],
    "props/field_bench.blend": ["Coll_FieldBench_Table", "Coll_FieldBench_ToolRack",
                                "Coll_FieldBench_Generator", "Coll_FieldBench_Reel",
                                "Coll_FieldBench_Sawhorse"],
    "props/console_panel.blend": ["Coll_ConsolePanel_Nav",
                                  "Coll_ConsolePanel_Breaker"],
    "props/wall_locker.blend": ["Coll_WallLocker_OpenShelf",
                                "Coll_WallLocker_Dented"],
    "props/crew_seat.blend": ["Coll_CrewSeat_Stool"],
}


def build_structure(coll):
    """Block, mast, cupola — the four instances that are the building."""
    place("Outpost_Block", mesh_of("Coll_PrefabHab_Long"), coll, (0, 0, 0))
    place("Outpost_Mast", mesh_of("Coll_StationTower_Taper"), coll,
          (TX, TY, Z_SADDLE))
    place("Outpost_Cupola", mesh_of("Coll_SensorCupola_Dome"), coll,
          (TX, TY, Z_SHAFT), rot=(0, 0, -18.0))


def build_roof(coll):
    """Everything on the roof deck: walkway, ladders, lights, aerials, pipework."""
    # Walkway from the roof ladder across to the saddle. Three plate variations
    # alternated so no tile repeats next to itself.
    plates = ("Coll_DeckPlate_Worn", "Coll_DeckPlate_Grate", "Coll_DeckPlate_Solid",
              "Coll_DeckPlate_Grate", "Coll_DeckPlate_Worn", "Coll_DeckPlate_Solid",
              "Coll_DeckPlate_Grate")
    for i, name in enumerate(plates):
        place("Roof_Walk_%02d" % i, mesh_of(name), coll,
              (-5.4 + i * 0.98, 3.7, Z_ROOF + 0.04))
    for i, name in enumerate(("Coll_DeckPlate_Grate", "Coll_DeckPlate_Worn")):
        place("Roof_WalkSpur_%02d" % i, mesh_of(name), coll,
              (0.48, 2.72 - i * 0.98, Z_ROOF + 0.04))

    # Roof access: a caged ladder up the front wall, a gate at the top, and a
    # crate under it — the ladder starts at 0.9 m, as caged ladders do.
    place("Roof_Ladder", mesh_of("Coll_Handrail_Ladder"), coll,
          (-5.6, -FY - 0.34, 0.90))
    place("Roof_LadderGate", mesh_of("Coll_Handrail_Gate"), coll,
          (-5.6, -FY + 0.30, Z_ROOF + 0.06), rot=(0, 0, 90))
    place("Roof_LadderStep", mesh_of("Coll_Crate_Large"), coll,
          (-5.6, -FY - 0.95, 0.0), rot=(0, 0, -8))
    for i in range(2):
        place("Roof_ParapetRail_%d" % i, mesh_of("Coll_Handrail_Straight"), coll,
              (-4.2 + i * 2.24, -FY + 0.30, Z_ROOF + 0.06), rot=(0, 0, 90))

    # Mast ladder: three lifts off the +X facet, from the saddle pad to just
    # above the gallery. The -Y facet is taken by the plant rack.
    for i in range(3):
        z0 = Z_PAD + i * 3.39
        place("Mast_Ladder_%d" % i, mesh_of("Coll_Handrail_Ladder"), coll,
              (TX + shaft_face(z0 + 1.70) + 0.44, TY, z0), rot=(0, 0, -90))

    # Aerials. The gallery mast is scaled to land the model's tip at 18.50.
    f = fit_scale("Coll_MastRig_Antenna", Z_GALLERY, TIP)
    place("Mast_Aerial_Main", scaled("Coll_MastRig_Antenna", f), coll,
          (TX - 1.9, TY + 1.5, Z_GALLERY), rot=(0, 0, 40))
    place("Mast_Aerial_Small", scaled("Coll_MastRig_Antenna", 0.34), coll,
          (5.8, -3.4, Z_ROOF + 0.2), rot=(0, 0, -25))
    place("Roof_Windvane", scaled("Coll_MastRig_Windvane", 0.62), coll,
          (5.9, 3.9, Z_ROOF + 0.2), rot=(0, 0, 15))

    # Site lighting, aimed off the corners and off the saddle.
    place("Roof_Flood_West", scaled("Coll_FloodlightBank_Twin", 1.3), coll,
          (-6.1, -4.3, Z_ROOF + 0.7), rot=(0, 0, 34))
    place("Roof_Flood_East", scaled("Coll_FloodlightBank_Single", 1.3), coll,
          (6.1, -4.3, Z_ROOF + 0.7), rot=(0, 0, -38))
    place("Mast_Flood_Sweep", scaled("Coll_FloodlightBank_Sweep", 1.2), coll,
          (TX - 2.6, TY - 2.0, Z_PAD), rot=(0, 0, 200))

    # Service pipework: plant box to saddle, then a riser down the end wall.
    run = ("Coll_PipeRun_Junction", "Coll_PipeRun_Straight", "Coll_PipeRun_Straight",
           "Coll_PipeRun_Elbow")
    for i, name in enumerate(run):
        place("Roof_Pipe_%02d" % i, scaled(name, 1.5), coll,
              (-2.6 + i * 1.5, 1.9, Z_ROOF + 0.28))
    # Pipe modules carry their origin at one end, so a vertical run reads
    # downward from where it is placed: three 1.3 m lifts land it on 0.30.
    for i in range(3):
        place("Roof_Riser_%02d" % i, scaled("Coll_PipeRun_Straight", 1.3), coll,
              (FX + 0.26, 2.2, 1.6 + i * 1.3), rot=(0, 90, 0))
    place("Roof_RiserHead", scaled("Coll_PipeRun_Elbow", 1.3), coll,
          (FX + 0.26, 2.2, Z_ROOF), rot=(0, 0, 180))
    place("Roof_HatchRim", mesh_of("Coll_BulkheadFrame_HatchRim"), coll,
          (-0.4, 3.4, Z_ROOF + 0.06), rot=(90, 0, 0))


def build_walls(coll):
    """Door, vents, patches and wall-mounted kit on the four elevations."""
    place("Wall_Door", mesh_of("Coll_BulkheadFrame_Door"), coll,
          (-2.6, -FY + 0.12, 0.34))
    place("Wall_DoorLamp", scaled("Coll_Light_Dome", 1.9), coll,
          (-2.6, -FY - 0.22, 3.02), rot=(90, 0, 0))
    place("Wall_Breaker", mesh_of("Coll_ConsolePanel_Breaker"), coll,
          (0.5, -FY - 0.12, 1.92), rot=(0, 0, 90))

    for nm, loc, rot, s in (
            ("Louvre", (3.6, -FY - 0.10, 1.62), (0, 0, 0), 1.5),
            ("Fan", (5.1, -FY - 0.10, 1.62), (0, 0, 0), 1.5),
            ("Scoop", (FX + 0.12, -2.6, 2.90), (0, 0, 90), 1.4),
            ("MeshScreen", (-5.2, FY + 0.10, 1.50), (0, 0, 180), 1.5)):
        place("Wall_Vent_%s" % nm, scaled("Coll_Vent_%s" % nm, s), coll, loc, rot)

    # Repair patches — three different plates, none adjacent to its own kind.
    for nm, loc, rot in (
            ("Patched", (4.7, -FY - 0.05, 3.05), (90, 0, 0)),
            ("Riveted", (-4.4, -FY - 0.05, 1.55), (90, 0, 12)),
            ("Ribbed", (FX + 0.06, 1.2, 1.90), (90, 0, 90)),
            ("Patched", (-FX - 0.06, 2.4, 2.60), (90, 0, 90))):
        place("Wall_Patch_%s_%d" % (nm, int(loc[0] * 10) % 97),
              mesh_of("Coll_HullPlate_%s" % nm), coll, loc, rot)

    place("Wall_Locker_Dented", mesh_of("Coll_WallLocker_Dented"), coll,
          (-6.0, FY + 0.6, 0.0), rot=(0, 0, 172))


def build_cupola_kit(coll):
    """The lamp on its arm and the emergency strip, up on the gallery."""
    # The cupola is yawed -18 deg, so its bracket boss — authored at 243 deg in
    # the component — presents at 225 deg in world. Reading the angle off the
    # component and forgetting the yaw is how the lamp ends up hanging on air.
    ba = math.radians(243.0 - 18.0)
    place("Cupola_Lamp", scaled("Coll_Light_Clamp", 2.1), coll,
          (TX + 2.75 * math.cos(ba), TY + 2.75 * math.sin(ba), Z_SHAFT + 0.62),
          rot=(0, 0, 225))
    place("Cupola_Strip", scaled("Coll_Light_Strip", 1.6), coll,
          (TX + 1.2, TY - 2.3, Z_GALLERY + 0.9), rot=(0, 0, 12))
    # Cable drop hugging the shaft face, not standing off it.
    place("Cupola_Pipe", scaled("Coll_PipeRun_CableBundle", 1.4), coll,
          (TX - 0.75, TY - shaft_face(12.6) + 0.06, Z_SHAFT - 0.5),
          rot=(0, 74, 0))


def build_yard(coll):
    """The working yard: three awnings, the kit under them, and the supply dump.

    Placement rules followed throughout — no variation is ever adjacent to
    itself, and nothing sits square to the building.
    """
    # -- Awnings. Azure out front-left, bleached hung off the wall, canvas
    #    behind. Three of the five variations; Torn and Frame are built ahead.
    place("Yard_Awning_Azure", mesh_of("Coll_Awning_Square"), coll,
          (-8.9, -4.4, 0.0), rot=(0, 0, -6))
    place("Yard_Awning_Wall", mesh_of("Coll_Awning_LeanTo"), coll,
          (1.7, -FY - 0.06, 0.0))
    place("Yard_Awning_Store", mesh_of("Coll_Awning_Sagging"), coll,
          (-7.4, 5.8, 0.0), rot=(0, 0, 14))

    # -- Under the azure awning: the repair bay.
    place("Yard_Bench", mesh_of("Coll_FieldBench_Table"), coll,
          (-8.9, -3.5, 0.0), rot=(0, 0, 8))
    place("Yard_Genset", mesh_of("Coll_FieldBench_Generator"), coll,
          (-10.2, -5.4, 0.0), rot=(0, 0, -22))
    place("Yard_Stool_A", mesh_of("Coll_CrewSeat_Stool"), coll,
          (-7.8, -4.5, 0.0), rot=(0, 0, 30))
    place("Yard_Nav", mesh_of("Coll_ConsolePanel_Nav"), coll,
          (-10.3, -3.3, 0.0), rot=(0, 0, 18))
    for i in range(2):
        place("Yard_Festoon_A%d" % i, scaled("Coll_Light_Festoon", 1.6), coll,
              (-10.0 + i * 2.2, -5.9, 2.28), rot=(0, 0, 84))

    # -- Under the wall lean-to: the bench end of the yard.
    place("Yard_Sawhorse", mesh_of("Coll_FieldBench_Sawhorse"), coll,
          (1.1, -6.8, 0.0), rot=(0, 0, -14))
    place("Yard_ToolRack", mesh_of("Coll_FieldBench_ToolRack"), coll,
          (3.1, -5.5, 0.0), rot=(0, 0, 187))
    place("Yard_Shelf", mesh_of("Coll_WallLocker_OpenShelf"), coll,
          (-0.1, -5.4, 0.0), rot=(0, 0, 184))
    place("Yard_CrateOpen", mesh_of("Coll_Crate_Open"), coll,
          (2.4, -7.5, 0.0), rot=(0, 0, 24))
    place("Yard_Stool_B", mesh_of("Coll_CrewSeat_Stool"), coll,
          (0.7, -6.2, 0.0), rot=(0, 0, -40))
    for i in range(2):
        place("Yard_Festoon_B%d" % i, scaled("Coll_Light_Festoon", 1.4), coll,
              (0.4 + i * 2.0, -7.9, 2.06), rot=(0, 0, 96))
    place("Yard_Reel", mesh_of("Coll_FieldBench_Reel"), coll,
          (-4.7, -6.6, 0.0), rot=(0, 0, 36))

    # -- The supply dump, +X flank. Round against square, alternating.
    place("Yard_Tank", mesh_of("Coll_Barrel_Tank"), coll,
          (8.7, -1.0, 0.0), rot=(0, 0, 9))
    place("Yard_Bottles", mesh_of("Coll_Barrel_GasBottles"), coll,
          (8.1, 1.9, 0.0), rot=(0, 0, -16))
    place("Yard_DrumStack", mesh_of("Coll_Barrel_Stack"), coll,
          (8.4, -3.6, 0.0), rot=(0, 0, 26))
    place("Yard_CrateStack", mesh_of("Coll_Crate_Stack"), coll,
          (7.6, 3.6, 0.0), rot=(0, 0, -11))
    place("Yard_CrateLarge", mesh_of("Coll_Crate_Large"), coll,
          (6.2, 5.1, 0.0), rot=(0, 0, 14))
    place("Yard_CrateLong", mesh_of("Coll_Crate_Long"), coll,
          (9.2, 2.8, 0.0), rot=(0, 0, 62))
    place("Yard_Jerrican", mesh_of("Coll_Barrel_Jerrican"), coll,
          (-3.2, -6.4, 0.0), rot=(0, 0, -28))

    # -- Loose drums, deliberately not in a row.
    for i, (x, y, a) in enumerate(((7.3, -5.4, 12), (6.4, -6.3, -34),
                                   (-1.2, -10.8, 8), (9.4, 0.2, 47),
                                   (-6.6, 6.9, -19))):
        place("Yard_Drum_%d" % i, mesh_of("Coll_Barrel_Drum"), coll, (x, y, 0.0),
              rot=(0, 0, a))

    # -- Out on the sand: the pieces that stop the footprint ending abruptly.
    place("Yard_CrateSmall_A", mesh_of("Coll_Crate_Small"), coll,
          (-5.6, -8.6, 0.0), rot=(0, 0, 29))
    place("Yard_CrateSmall_B", mesh_of("Coll_Crate_Small"), coll,
          (-8.4, 6.6, 0.0), rot=(0, 0, -52))
    place("Yard_Pallet", mesh_of("Coll_Crate_Pallet"), coll,
          (4.8, -9.6, 0.0), rot=(0, 0, -18))
    place("Yard_CrateLarge_Store", mesh_of("Coll_Crate_Large"), coll,
          (-6.9, 5.6, 0.0), rot=(0, 0, 41))

    # -- Ground cable from the genset to the block, in three sagging bundles.
    for i in range(3):
        place("Yard_Cable_%d" % i, scaled("Coll_PipeRun_CableBundle", 1.6), coll,
              (-9.4 + i * 1.9, -5.2 + i * 0.35, 0.08), rot=(0, 0, 12 + i * 9))


def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)
    for path, colls in COMPONENT_SET.items():
        load_component(path, colls)

    root = collection("Coll_RelayOutpost")
    structure = collection("Coll_RelayOutpost_Structure", root)
    unique = collection("Coll_RelayOutpost_Unique", root)
    yard = collection("Coll_RelayOutpost_Yard", root)

    build_structure(structure)
    build_roof(structure)
    build_walls(structure)
    build_cupola_kit(structure)
    build_yard(yard)

    saddle = tower_saddle(unique, mats)
    saddle.location = (TX, TY, Z_ROOF)
    rack = plant_rack(unique, mats)
    # Pushed 0.3 m into the shaft: the rack's back is a 4.4 m flat and the
    # facet it lands on is only 1.95 m wide, so a tangent placement would gap
    # at both ends. Overlapping the centre hides the difference.
    rack.location = (TX - 0.4, TY - shaft_face(5.6) + 0.30, Z_ROOF + 0.1)
    rack.rotation_euler = Euler((0, 0, math.radians(-3)), 'XYZ')
    st = stoop(unique, mats)
    st.location = (-2.6, -FY, 0.0)

    dedupe_materials()
    total = report()
    zmax = max((o.matrix_world @ Vector(c)).z
               for o in bpy.data.objects if o.type == 'MESH'
               for c in o.bound_box)
    print("  highest point: %.2f m  (target %.2f)" % (zmax, TIP))
    print("  objects: %d   meshes: %d   tris: %d"
          % (len(bpy.data.objects), len(bpy.data.meshes), total))
    save(out)


main()
