"""models/buildings/outpost/lattice_outpost — a 52 m stilted watch tower.

Assembles the landmark from library components. Almost nothing here is modelled:
the script's job is to decide *where* a hundred-odd instances go, and to build
the two pieces that are genuinely unique to this one building — the aerial farm
that sets the final height, and the caged ladder that is the only way between
its two decks.

Built from a concept reference: a coral-and-slate outpost stacked up an X-braced
steel mast in a red dust canyon — a habitat block on a platform at the bottom, a
small machine module clamped on halfway up, and a glazed control cab under a
thicket of aerials at the top.

Height budget, and why it is what it is:

     0.00 -  6.20   `LatticeMast_Splay`, four raked legs onto footings
     6.20 - 20.00   `LatticeMast_Bay`, the first shaft module
     6.15 -  9.00   `Truss_Deck`, the lower platform — top face at 9.00
     9.00 - 16.40   `OutpostBlock_Station`, the coral habitat
    19.60 - 27.88   `OutpostBlock_Plant`, the machine module and its tank
    20.00 - 33.80   `LatticeMast_Bay`, the second shaft module
    28.70 - 33.80   `LatticeMast_Collar`, the upper deck — top face at 33.80
    33.80 - 38.15   `ControlCab_Annex`, the blind service storey
    38.15 - 43.65   `ControlCab_Wide`, the glazed cab — roof deck at 43.00
    43.00 - 52.00   the aerial farm, unique geometry

52.00 m to the tip. The habitat's roof is at 16.40 and the upper deck begins at
28.70, so the middle of the tower is 12 m of open lattice broken once by the
plant module — and that run is what the whole composition rests on. The first
pass put the lower deck at 10.50 and the plant at 23.00, which left barely 6 m
of visible steelwork and made the thing read as three buildings stacked on a
post rather than as a mast that happens to carry buildings.

The shaft passes *through* the lower habitat rather than beside it. That is
deliberate — a mast carrying a building 30 m up cannot stop at its roof and pick
up again, and the reference shows the same thing, the lattice emerging from the
block's roof rather than clearing its edge.

    blender --background --python lattice_outpost.py -- --out lattice_outpost.blend

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
    "Mat_Paint_Coral_Faded",     # 0 CORAL
    "Mat_Paint_Blue_Station",    # 1 BLUE
    "Mat_Neutral_Slate_Dark",    # 2 SLATE
    "Mat_Metal_Steel_Worn",      # 3 STEEL
    "Mat_Metal_Steel_Dark",      # 4 DARK
    "Mat_Neutral_Black_Matte",   # 5 BLACK
    "Mat_Metal_Rust_Heavy",      # 6 RUST
    "Mat_Paint_Warn_Red",        # 7 RED
    "Mat_Emissive_Amber",        # 8 AMBER
    "Mat_Emissive_Red_Warn",     # 9 WARN
    "Mat_Glass_Canopy_Tinted",   # 10 GLASS
]
CORAL, BLUE, SLATE, STEEL, DARK, BLACK, RUST, RED, AMBER, WARN, GLASS = range(11)

# --- the controlling levels -------------------------------------------------
Z_SPLAY_TOP = 6.20
Z_DECK_LO = 9.00             # top face of the lower platform
Z_BLOCK_TOP = Z_DECK_LO + 7.40
Z_BAY2 = 20.00
Z_PLANT = 20.00
Z_DECK_HI = 33.80            # top face of the upper deck
Z_ANNEX_TOP = Z_DECK_HI + 4.35
Z_CAB_ROOF = Z_ANNEX_TOP + 5.02
TOP = 52.00

MAST_S = 1.70                # half the chord spacing
DECK_LO_W, DECK_LO_D = 20.24, 16.44
DECK_HI_R = 6.30
BLOCK_Y = -1.90              # the habitat sits forward on the lower deck

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

    Several reused props are authored at vehicle scale and are simply too small
    on a 52 m building. Taking the difference as object scale would leave this
    file full of objects at 1.8, which the library forbids; baking it into one
    shared mesh copy per size keeps every object at scale 1.0 and pays for the
    size once however many times it is placed.
    """
    data = mesh_of(coll_name, contains)
    key = (data.name, round(factor, 4))
    if key not in SCALED:
        m = data.copy()
        m.transform(Matrix.Diagonal((factor, factor, factor, 1.0)))
        SCALED[key] = m
    return SCALED[key]


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
# The two pieces that are unique to this building
# ---------------------------------------------------------------------------

def aerials(coll, mats):
    """The thicket on the cab roof, topping out at exactly 52.00 m.

    Unique rather than a component because it is the specific answer to "what is
    the last nine metres of *this* building". `mast_rig` covers whips at vehicle
    scale and `drill_derrick/Antenna` covers a 20 m guyed comms mast; neither is
    a 9 m cluster sitting on a 9-by-8 roof, and a cluster is what the reference
    has — one tall whip that sets the height, with enough shorter kit around its
    base that the tall one reads as the tallest of a set rather than as a pole.

    Built with its origin on the cab roof deck, so the tip is at local z = 9.00.
    """
    p = Part(mats)
    # the tall whip — three tapering stages, the last one thin enough to bend
    p.box((0, 0, 0.30), (1.30, 1.30, 0.60), DARK)                    # base plinth
    p.cyl((0, 0, 0.72), 0.30, 0.36, seg=10, mat=STEEL)
    p.cyl((0, 0, 2.60), 0.19, 3.40, seg=8, mat=STEEL, radius_top=0.13)
    p.cyl((0, 0, 5.60), 0.12, 2.60, seg=8, mat=RED, radius_top=0.08)
    p.cyl((0, 0, 7.795), 0.07, 1.79, seg=6, mat=STEEL, radius_top=0.03)
    p.cyl((0, 0, 8.76), 0.05, 0.14, seg=6, mat=WARN)                 # the tip lamp
    for z in (2.30, 4.40):                                           # stay rings
        p.torus((0, 0, z), 0.34, 0.045, maj_seg=8, min_seg=5, mat=DARK)
        for k in range(3):
            a = 2 * math.pi * k / 3 + 0.4
            p.box((0.90 * math.cos(a), 0.90 * math.sin(a), z * 0.52),
                  (0.035, 0.035, z * 1.04), STEEL,
                  rot=Vector((0, 0, 1)).rotation_difference(
                      Vector((-0.9 * math.cos(a), -0.9 * math.sin(a), z * 0.9)
                             ).normalized()).to_matrix().to_4x4())
    for k in range(4):                                               # dipole rungs
        z = 3.10 + k * 0.62
        p.box((0, 0, z), (1.44 - k * 0.18, 0.05, 0.05), STEEL)
    # the shorter kit around it
    for i, (x, y, h, r) in enumerate(((-2.40, 1.30, 4.20, 0.10),
                                      (2.60, -1.10, 3.30, 0.09),
                                      (1.90, 2.20, 2.50, 0.08),
                                      (-2.80, -1.90, 3.70, 0.09))):
        p.box((x, y, 0.22), (0.72, 0.72, 0.44), DARK)
        p.cyl((x, y, 0.44 + h / 2), r, h, seg=6, mat=STEEL, radius_top=r * 0.5)
        if i % 2 == 0:                                               # a small dish
            p.cyl((x, y, 0.44 + h), 0.62, 0.16, seg=10, mat=BLUE,
                  radius_top=0.50)
            p.cyl((x, y, 0.44 + h + 0.20), 0.09, 0.34, seg=6, mat=DARK)
        else:
            p.box((x, y, 0.44 + h + 0.16), (0.34, 0.34, 0.34), DARK)
    # cable trays and a plant box, so the aerials arrive from somewhere
    for k in range(3):
        p.box((-1.2 + k * 1.3, -2.60, 0.16), (0.30, 2.40, 0.16), STEEL)
    p.box((-3.10, 2.60, 0.46), (1.20, 0.90, 0.92), BLUE)
    p.box((-3.10, 2.60, 0.96), (0.62, 0.50, 0.12), DARK)
    p.box((2.90, 2.90, 0.30), (0.70, 0.70, 0.60), DARK)
    p.bevel(width=0.016, segments=1)
    return p.finish("Mesh_LatticeOutpost_Aerials", coll)


def ladder_run(coll, mats):
    """The caged ladder up the mast, lower deck to upper deck: 23.30 m.

    Unique because it is defined entirely by the two levels it joins. This is
    the piece that makes the tower inhabitable rather than sculptural — without
    it the upper deck is a place nobody can reach, and a 23 m gap between two
    occupied platforms is the first thing that reads as unbuilt.

    Origin at the lower deck surface. Landings every ~5.8 m, because a ladder
    run longer than that is not something a real structure is allowed to have.
    """
    p = Part(mats)
    run = Z_DECK_HI - Z_DECK_LO
    y = -(MAST_S + 0.55)                       # hung off the mast's -Y face
    for sx in (-1, 1):                         # stringers
        p.box((sx * 0.32, y, run / 2), (0.09, 0.09, run), STEEL)
    n = int(run / 0.32)
    for k in range(n):                         # rungs
        p.box((0, y, 0.28 + k * 0.32), (0.66, 0.045, 0.045), STEEL)
    # Cage hoops at 1.30 m rather than the 0.78 m a real ladder cage uses. At
    # 23 m the tighter spacing turns the run into a solid bright tube against
    # the sky and swallows the lattice behind it, which is the one thing this
    # tower cannot afford to lose.
    for k in range(int(run / 1.30)):
        z = 1.90 + k * 1.30
        if z > run - 0.6:
            break
        p.torus((0, y - 0.36, z), 0.44, 0.028, axis='Y', maj_seg=8, min_seg=4,
                mat=STEEL)
    for a in (-0.42, 0.0, 0.42):               # cage longitudinals
        p.box((a, y - 0.78, (run + 1.9) / 2), (0.04, 0.04, run - 1.9), STEEL)
    for k in range(4):                         # rest landings
        z = 5.82 * (k + 1)
        if z > run - 1.0:
            break
        p.box((0.55, y - 0.20, z), (2.20, 1.30, 0.10), STEEL)
        p.box((1.60, y - 0.20, z + 0.55), (0.08, 1.30, 1.10), STEEL)
        p.box((0.55, y - 0.82, z + 0.55), (2.20, 0.07, 1.10), STEEL)
        for sx in (-1, 1):                     # the bracket back to the mast
            p.box((0.55 + sx * 0.9, y + 0.42, z - 0.22), (0.09, 0.85, 0.55),
                  DARK)
    p.box((0, y - 0.10, run + 0.30), (1.60, 1.10, 0.12), STEEL)   # top landing
    p.bevel(width=0.014, segments=1)
    return p.finish("Mesh_LatticeOutpost_LadderRun", coll)


# ---------------------------------------------------------------------------
# Placement
# ---------------------------------------------------------------------------

def build_mast(coll):
    place("Mast_Splay", mesh_of("Coll_LatticeMast_Splay"), coll, (0, 0, 0))
    place("Mast_Bay_Lower", mesh_of("Coll_LatticeMast_Bay"), coll,
          (0, 0, Z_SPLAY_TOP))
    place("Mast_Bay_Upper", mesh_of("Coll_LatticeMast_Bay"), coll,
          (0, 0, Z_BAY2))
    place("Mast_Collar_Upper", mesh_of("Coll_LatticeMast_Collar"), coll,
          (0, 0, Z_DECK_HI))
    # Braces from the splay head out to the lower platform, so a 20 m deck is
    # not visibly hanging off a 3.4 m shaft.
    for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        place("Mast_DeckBrace_%d" % i, mesh_of("Coll_Truss_Brace"), coll,
              (sx * 4.30, sy * 3.60, Z_SPLAY_TOP - 1.10),
              rot=(0, 0, 0 if sx * sy > 0 else 90))


def build_lower_level(coll):
    place("Deck_Lower", mesh_of("Coll_Truss_Deck"), coll, (0, 0, Z_DECK_LO))
    place("Block_Station", mesh_of("Coll_OutpostBlock_Station"), coll,
          (0, BLOCK_Y, Z_DECK_LO))
    # railing the full perimeter of the platform
    hx, hy = DECK_LO_W / 2 - 0.20, DECK_LO_D / 2 - 0.20
    for i in range(7):
        y = -hy + 1.18 + i * 2.24
        for sx, tag in ((-1, "W"), (1, "E")):
            place("Rail_Lo_%s%d" % (tag, i), mesh_of("Coll_Handrail_Straight"),
                  coll, (sx * hx, y, Z_DECK_LO))
    for i in range(8):
        x = -hx + 1.30 + i * 2.24
        for sy, tag in ((-1, "S"), (1, "N")):
            place("Rail_Lo_%s%d" % (tag, i), mesh_of("Coll_Handrail_Straight"),
                  coll, (x, sy * hy, Z_DECK_LO), rot=(0, 0, 90))
    for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        place("Rail_Lo_Corner%d" % i, mesh_of("Coll_Handrail_Corner"), coll,
              (sx * hx, sy * hy, Z_DECK_LO), rot=(0, 0, 90 * i))
    # walkable decking on the strip in front of the habitat
    grate, worn = mesh_of("Coll_DeckPlate_Grate"), mesh_of("Coll_DeckPlate_Worn")
    for ix in range(9):
        for iy in range(4):
            x = -8.0 + ix * 2.0
            y = 3.60 + iy * 1.10
            place("DeckPlate_%d_%d" % (ix, iy),
                  grate if (ix + iy) % 2 else worn, coll, (x, y, Z_DECK_LO))
    # Ground access, descending off the front edge. `Catwalk_Stair` carries its
    # origin 1.21 m below its top landing rather than at its foot, so the flight
    # is hung from the deck it arrives at: placing it at deck level would bury
    # 2.6 m of steps under the terrain.
    place("Stair_Lower", mesh_of("Coll_Catwalk_Stair"), coll,
          (-6.20, hy + 0.90, Z_DECK_LO - 1.21), rot=(0, 0, 90))
    place("Ladder_Ground", mesh_of("Coll_Handrail_Ladder"), coll,
          (-6.20, hy + 1.80, 0.0))
    # yard kit on the deck — this is a place people work
    place("Crate_Stack_A", mesh_of("Coll_Crate_Stack"), coll, (6.40, 5.30, Z_DECK_LO))
    place("Crate_Large_A", mesh_of("Coll_Crate_Large"), coll, (7.90, 4.20, Z_DECK_LO), rot=(0, 0, 24))
    place("Crate_Pallet_A", mesh_of("Coll_Crate_Pallet"), coll, (5.10, 6.60, Z_DECK_LO), rot=(0, 0, -12))
    place("Crate_Long_A", mesh_of("Coll_Crate_Long"), coll, (-8.30, 4.10, Z_DECK_LO), rot=(0, 0, 78))
    place("Crate_Open_A", mesh_of("Coll_Crate_Open"), coll, (-7.10, 6.30, Z_DECK_LO), rot=(0, 0, 8))
    place("Barrel_Stack_A", mesh_of("Coll_Barrel_Stack"), coll, (8.60, 6.70, Z_DECK_LO), rot=(0, 0, 15))
    place("Barrel_Drum_A", mesh_of("Coll_Barrel_Drum"), coll, (-4.60, 6.90, Z_DECK_LO))
    place("Barrel_Bottles_A", mesh_of("Coll_Barrel_GasBottles"), coll, (-5.80, 5.40, Z_DECK_LO), rot=(0, 0, -30))
    place("Bench_Table_A", mesh_of("Coll_FieldBench_Table"), coll, (1.90, 6.40, Z_DECK_LO), rot=(0, 0, 6))
    place("Bench_ToolRack_A", mesh_of("Coll_FieldBench_ToolRack"), coll, (3.30, 7.30, Z_DECK_LO), rot=(0, 0, -84))
    place("Bench_Generator_A", mesh_of("Coll_FieldBench_Generator"), coll, (-2.20, 7.10, Z_DECK_LO), rot=(0, 0, 20))
    # aerials on the habitat roof
    place("Roof_Windvane", mesh_of("Coll_MastRig_Windvane"), coll,
          (-5.40, BLOCK_Y - 3.10, Z_BLOCK_TOP + 0.20))
    place("Roof_Antenna", mesh_of("Coll_MastRig_Antenna"), coll,
          (5.90, BLOCK_Y - 2.60, Z_BLOCK_TOP + 0.20))
    place("Roof_Stack", mesh_of("Coll_ExhaustStack_Cowl"), coll,
          (2.80, BLOCK_Y + 3.10, Z_BLOCK_TOP + 0.20))


def build_mid_level(coll):
    place("Block_Plant", mesh_of("Coll_OutpostBlock_Plant"), coll,
          (0, -4.55, Z_PLANT))
    place("Plant_Catwalk", mesh_of("Coll_Catwalk_Straight"), coll,
          (0, -1.10, Z_PLANT + 0.30), rot=(0, 0, 90))
    place("Plant_Corner", mesh_of("Coll_Catwalk_Corner"), coll,
          (3.40, -1.10, Z_PLANT + 0.30), rot=(0, 0, 90))
    place("Plant_Flood", mesh_of("Coll_FloodlightBank_Twin"), coll,
          (-2.30, -7.30, Z_PLANT + 3.60), rot=(0, 0, 200))
    for k in range(3):
        place("Plant_Vent_%d" % k, scaled("Coll_Vent_Louvre", 1.8), coll,
              (2.10, -7.55, Z_PLANT + 1.10 + k * 0.95))


def build_upper_level(coll):
    place("Cab_Annex", mesh_of("Coll_ControlCab_Annex"), coll, (0, 0, Z_DECK_HI))
    place("Cab_Wide", mesh_of("Coll_ControlCab_Wide"), coll, (0, 0, Z_ANNEX_TOP))
    # The gallery is the collar's own 12.6 m deck with a rail round it, not a
    # ring of cantilevered catwalk spans. Spans hung off the edge read as four
    # separate shelves with gaps at the corners — the deck is already there, so
    # what it needed was a handrail, not more structure.
    gx = DECK_HI_R - 0.22
    for i in range(5):
        u = -gx + 1.30 + i * 2.24
        for s, tag in ((-1, "W"), (1, "E")):
            place("Rail_Hi_%s%d" % (tag, i), mesh_of("Coll_Handrail_Straight"),
                  coll, (s * gx, u, Z_DECK_HI))
        for s, tag in ((-1, "S"), (1, "N")):
            place("Rail_Hi_%s%d" % (tag, i), mesh_of("Coll_Handrail_Straight"),
                  coll, (u, s * gx, Z_DECK_HI), rot=(0, 0, 90))
    for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        place("Rail_Hi_Corner%d" % i, mesh_of("Coll_Handrail_Corner"), coll,
              (sx * gx, sy * gx, Z_DECK_HI), rot=(0, 0, 90 * i))
    place("Cab_Door", mesh_of("Coll_BulkheadFrame_Door"), coll,
          (2.60, -4.06, Z_DECK_HI + 0.10), rot=(0, 0, 180))
    for i, (x, y, r) in enumerate(((-5.40, -5.40, 215), (5.40, -5.40, 145),
                                   (5.40, 5.40, 35), (-5.40, 5.40, -35))):
        place("Deck_Flood_%d" % i, mesh_of("Coll_FloodlightBank_Quad"), coll,
              (x, y, Z_DECK_HI + 1.30), rot=(0, 0, r))
    # the cab roof
    place("Aerial_Farm", mesh_of("Coll_LatticeOutpost_Aerials"), coll,
          (0, 0, Z_CAB_ROOF))
    place("Roof_Windvane_Cab", mesh_of("Coll_MastRig_Windvane"), coll,
          (3.90, -3.60, Z_CAB_ROOF + 0.18))


def build_services(coll):
    """Pipes, cables, lamps and markings — the stuff that says it is connected."""
    place("Ladder_Run", mesh_of("Coll_LatticeOutpost_LadderRun"), coll,
          (0, 0, Z_DECK_LO))
    # a service riser climbing the mast beside the ladder
    straight = scaled("Coll_PipeRun_Straight", 1.9)
    bundle = scaled("Coll_PipeRun_CableBundle", 1.9)
    z = Z_DECK_LO + 0.60
    i = 0
    while z < Z_DECK_HI - 1.0:
        place("Riser_Pipe_%d" % i, straight, coll,
              (MAST_S + 0.30, 0.95, z), rot=(0, 90, 0))
        place("Riser_Cable_%d" % i, bundle, coll,
              (MAST_S + 0.30, 1.55, z), rot=(0, 90, 0))
        z += 3.80
        i += 1
    place("Riser_Junction", scaled("Coll_PipeRun_Junction", 1.9), coll,
          (MAST_S + 0.30, 0.95, Z_DECK_HI - 1.2), rot=(0, 90, 0))
    for k, z in enumerate((Z_DECK_LO + 0.35, Z_DECK_HI + 0.35)):
        place("Lamp_Strip_%d" % k, scaled("Coll_Light_Strip", 2.2), coll,
              (-2.40, -2.60, z + 2.4))
    for k, (x, y, z, r) in enumerate((
            (-7.5, BLOCK_Y - 5.52, Z_DECK_LO + 5.9, 0),
            (7.5, BLOCK_Y - 5.52, Z_DECK_LO + 2.6, 0))):
        place("Stencil_Danger_%d" % k, mesh_of("Coll_HullStencil_DangerBand"),
              coll, (x, y, z), rot=(0, 0, r))
    place("Stencil_Roundel", mesh_of("Coll_HullStencil_Roundel"), coll,
          (4.30, BLOCK_Y - 5.52, Z_DECK_LO + 5.6))
    place("Stencil_Chevron", mesh_of("Coll_HullStencil_Chevron"), coll,
          (-3.20, -4.06, Z_DECK_HI + 2.30))
    place("Stencil_Placard", mesh_of("Coll_HullStencil_Placard"), coll,
          (1.10, BLOCK_Y - 5.52, Z_DECK_LO + 2.5))


# ---------------------------------------------------------------------------

def main():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    load_component("structural/lattice_mast.blend",
                   ["Coll_LatticeMast_Splay", "Coll_LatticeMast_Bay",
                    "Coll_LatticeMast_Collar"])
    load_component("structural/outpost_block.blend",
                   ["Coll_OutpostBlock_Station", "Coll_OutpostBlock_Plant"])
    load_component("structural/control_cab.blend",
                   ["Coll_ControlCab_Wide", "Coll_ControlCab_Annex"])
    load_component("structural/truss_frame.blend",
                   ["Coll_Truss_Deck", "Coll_Truss_Brace"])
    load_component("structural/handrail.blend",
                   ["Coll_Handrail_Straight", "Coll_Handrail_Corner",
                    "Coll_Handrail_Ladder"])
    load_component("structural/catwalk_span.blend",
                   ["Coll_Catwalk_Straight", "Coll_Catwalk_Corner",
                    "Coll_Catwalk_Stair"])
    load_component("structural/deck_plate.blend",
                   ["Coll_DeckPlate_Grate", "Coll_DeckPlate_Worn"])
    load_component("structural/bulkhead_frame.blend", ["Coll_BulkheadFrame_Door"])
    load_component("structural/mast_rig.blend",
                   ["Coll_MastRig_Antenna", "Coll_MastRig_Windvane"])
    load_component("mechanical/exhaust_stack.blend", ["Coll_ExhaustStack_Cowl"])
    load_component("mechanical/pipe_run.blend",
                   ["Coll_PipeRun_Straight", "Coll_PipeRun_CableBundle",
                    "Coll_PipeRun_Junction"])
    load_component("mechanical/vent_grille.blend", ["Coll_Vent_Louvre"])
    load_component("props/supply_crate.blend",
                   ["Coll_Crate_Stack", "Coll_Crate_Large", "Coll_Crate_Pallet",
                    "Coll_Crate_Long", "Coll_Crate_Open"])
    load_component("props/fuel_barrel.blend",
                   ["Coll_Barrel_Stack", "Coll_Barrel_Drum",
                    "Coll_Barrel_GasBottles"])
    load_component("props/field_bench.blend",
                   ["Coll_FieldBench_Table", "Coll_FieldBench_ToolRack",
                    "Coll_FieldBench_Generator"])
    load_component("props/floodlight_bank.blend",
                   ["Coll_FloodlightBank_Quad", "Coll_FloodlightBank_Twin"])
    load_component("props/light_fixture.blend", ["Coll_Light_Strip"])
    load_component("props/hull_stencil.blend",
                   ["Coll_HullStencil_DangerBand", "Coll_HullStencil_Roundel",
                    "Coll_HullStencil_Chevron", "Coll_HullStencil_Placard"])

    unique = collection("Coll_LatticeOutpost_Unique")
    aerials(unique, mats)
    ladder_run(unique, mats)
    SOURCES["Coll_LatticeOutpost_Aerials"] = {
        "Mesh_LatticeOutpost_Aerials":
            bpy.data.meshes["Mesh_LatticeOutpost_Aerials"]}
    SOURCES["Coll_LatticeOutpost_LadderRun"] = {
        "Mesh_LatticeOutpost_LadderRun":
            bpy.data.meshes["Mesh_LatticeOutpost_LadderRun"]}
    for o in list(unique.all_objects):
        bpy.data.objects.remove(o, do_unlink=True)

    tower = collection("Coll_LatticeOutpost")
    build_mast(tower)
    build_lower_level(tower)
    build_mid_level(tower)
    build_upper_level(tower)
    build_services(tower)

    dedupe_materials()
    report()
    save(out)


main()
