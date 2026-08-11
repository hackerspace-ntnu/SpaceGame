"""models/buildings/industrial/refinery_tower — a 75 m arctic drilling refinery.

Assembles the landmark from library components. Almost nothing here is modelled:
the script's job is to decide *where* 60-odd instances go, and to build only the
handful of pieces that are genuinely unique to this one building — the podium it
stands on, the twin masts that set its final height, and the cradle under the
cantilevered capsule.

Height budget, and why it is what it is:

    0.0 - 8.0    podium: dark machinery mass, unique geometry
    8.0 - 53.0   five stacked `tower_bay` storeys, 9 m each
   53.0 - 64.5   `Coll_TowerBay_Crown`, including its stacks
   59.8 - 75.0   twin masts standing on the crown deck

75.0 m to the tip, which is where the brief put it. The white slab is 45 m over
a 14 m face — 3.2 : 1 — and the crown carries it to 3.9 : 1 read as one mass,
which is the proportion the reference actually has.

Everything instances shared mesh data, so nine catwalk spans cost one catwalk.

    blender --background --python refinery_tower.py -- --out refinery_tower.blend

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
    "Mat_Paint_White_Arctic",    # 0
    "Mat_Paint_Safety_Orange",   # 1
    "Mat_Metal_Steel_Dark",      # 2
    "Mat_Metal_Steel_Worn",      # 3
    "Mat_Neutral_Slate_Dark",    # 4
    "Mat_Neutral_Black_Matte",   # 5
    "Mat_Metal_Rust_Heavy",      # 6
    "Mat_Emissive_Amber",        # 7
    "Mat_Emissive_Red_Warn",     # 8
    "Mat_Paint_Warn_Red",        # 9
    "Mat_Glass_Canopy_Tinted",   # 10
]
WHITE, ORANGE, DARK, STEEL, SLATE, BLACK, RUST, AMBER, WARN, RED, GLASS = range(11)

# --- the building's controlling dimensions ---------------------------------
BW, BD = 14.0, 12.0          # tower bay footprint
FX, FY = BW / 2, BD / 2      # tower face planes
BAY = 9.0                    # storey height
Z_TOWER = 8.0                # top of the podium / bottom of the white stack
Z_CROWN = Z_TOWER + 5 * BAY  # 53.0
Z_DECK = 59.8                # crown machine deck, where the masts stand
PODIUM_W, PODIUM_D, PODIUM_H = 24.0, 20.0, 8.0
OUT_X, OUT_Y, OUT_Z = 26.0, -2.0, 16.2   # outrigger deck centre and top face

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
        # A copy: Blender rewrites this list in place with the loaded
        # datablocks, so handing it `wanted` would leave us iterating
        # Collections where we expect names.
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

    Several of the reused props — vent grilles, pipe runs, floodlights — are
    authored at vehicle scale and are simply too small on a building. Taking
    the scale on the *object* would leave this file full of objects at 2.4,
    which the library forbids and which Unity would rather not import either.
    Baking it into one shared copy per size keeps every object at scale 1.0 and
    still pays for the mesh once, however many times it is placed.
    """
    data = mesh_of(coll_name, contains)
    key = (data.name, round(factor, 4))
    if key not in SCALED:
        m = data.copy()
        m.transform(Matrix.Diagonal((factor, factor, factor, 1.0)))
        SCALED[key] = m
    return SCALED[key]


def place(name, data, coll, loc=(0, 0, 0), rot=None):
    """Instance a component mesh at a translation and rotation — never a scale.

    Names must be unique: `save` rejects auto-suffixed names, and a silent
    `.001` is how a model ends up with two objects nobody can tell apart.
    """
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
    """Fold any `Mat_X.001` back onto `Mat_X` after appending from eight files."""
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

def podium(coll, mats):
    """The dark machinery mass the white tower stands on.

    Unique rather than a component because its whole job is to be the specific
    junction between this tower, these legs and this conveyor. It is dark on
    purpose: the tower reads as tall because its base is visually buried, and a
    white podium would cost 8 m of apparent height.
    """
    p = Part(mats)
    w, d, h = PODIUM_W, PODIUM_D, PODIUM_H
    p.box((0, 0, h / 2), (w, d, h), SLATE)
    p.box((0, 0, 0.55), (w + 1.6, d + 1.6, 1.1), DARK)          # spread footing
    p.box((0, 0, 0.12), (w + 2.6, d + 2.6, 0.24), RUST)
    p.box((0, 0, h + 0.35), (w - 1.2, d - 1.2, 0.7), DARK)      # capping slab
    # Corner towers, so the podium has vertical grain against the tower's.
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.box((sx * (w / 2 - 1.1), sy * (d / 2 - 1.1), h / 2 + 0.6),
                  (2.6, 2.6, h + 1.2), DARK)
            p.box((sx * (w / 2 - 1.1), sy * (d / 2 - 1.1), h + 1.4),
                  (3.0, 3.0, 0.5), STEEL)
    # Deep bays punched into the long faces — a solid 24 m block reads as a
    # crate, and the openings are where the plant visibly lives.
    for sy in (-1, 1):
        for i in range(3):
            x = -7.4 + i * 7.4
            p.box((x, sy * (d / 2 - 0.5), 3.2), (5.2, 1.6, 5.0), BLACK)
            p.box((x, sy * (d / 2 - 1.3), 3.2), (4.4, 0.5, 4.2), SLATE)
            for k in range(4):
                p.box((x, sy * (d / 2 - 0.35), 1.5 + k * 1.15),
                      (5.0, 0.5, 0.30), DARK)
    for sx in (-1, 1):
        p.box((sx * (w / 2 - 0.5), 2.2, 3.4), (1.4, 7.0, 5.2), BLACK)
        p.box((sx * (w / 2 - 1.2), 2.2, 3.4), (0.5, 6.2, 4.4), SLATE)
    # A vehicle portal through the front face: the building has a way in.
    p.box((3.0, -(d / 2 - 0.6), 2.6), (7.0, 2.0, 5.4), BLACK)
    p.box((3.0, -(d / 2 + 0.15), 5.5), (7.8, 0.6, 0.8), ORANGE)
    for sx in (-1, 1):
        p.box((3.0 + sx * 3.6, -(d / 2 + 0.1), 2.6), (0.6, 0.7, 5.4), ORANGE)
    # Transition collar where the white tower lands on the dark mass.
    p.box((0, 0, h + 0.95), (BW + 1.9, BD + 1.9, 0.5), STEEL)
    p.box((0, 0, h - 0.9), (BW + 3.4, BD + 3.4, 0.55), DARK)
    for i in range(9):                                       # underside greeble
        p.greeble((-w / 2 + 1, -d / 2 + 1, h + 1.3), (w / 2 - 1, d / 2 - 1,
                                                      h + 1.3),
                  4, seed=i, scale=(0.8, 2.2), mat=DARK)
    p.bevel(width=0.06, segments=1)
    return p.finish("Mesh_Refinery_Podium", coll)


def crown_masts(coll, mats):
    """The twin masts on the crown deck — the last 15 m of the 75.

    Two, not one: a single mast on a symmetrical crown reads as a flagpole,
    and the pair is what the reference has. They are the cheapest possible
    geometry because at 60 m up, against sky, they are two lines.
    Origin at the deck they stand on.
    """
    p = Part(mats)
    top = 75.0 - Z_DECK
    p.box((0, 0, 0.30), (5.2, 3.0, 0.6), DARK)
    for i, (x, h) in enumerate(((-1.35, top), (1.35, top - 2.4))):
        p.cyl((x, 0, h / 2), 0.40, h, 'Z', seg=8, mat=DARK, radius_top=0.20)
        for k in range(5):                                    # collars
            p.cyl((x, 0, 1.8 + k * (h - 3.0) / 4), 0.48 - 0.04 * k, 0.30, 'Z',
                  seg=8, mat=STEEL)
        p.cyl((x, 0, h - 0.4), 0.24, 0.34, 'Z', seg=8, mat=WARN)
        p.cyl((x, 0, h * 0.55), 0.30, 0.30, 'Z', seg=8, mat=WARN)
        p.box((x, 0, 0.9), (1.3, 1.3, 1.2), DARK)
    # A ladder and a small service platform between them.
    for k in range(9):
        p.box((0, 0, 1.6 + k * 1.25), (2.7, 0.10, 0.10), STEEL)
    p.box((0, 0, top * 0.52), (4.0, 2.2, 0.14), STEEL)
    for sx in (-1, 1):
        p.box((sx * 1.95, 0, top * 0.52 + 0.55), (0.08, 2.0, 1.05), STEEL)
        p.box((sx * 1.95, 0, top * 0.52 + 1.06), (0.08, 2.0, 0.07), STEEL)
    # Dishes, because the pair should not be perfectly symmetrical.
    p.cyl((-2.1, 0, top * 0.70), 0.80, 0.26, 'X', seg=12, mat=WHITE,
          radius_top=0.62)
    p.box((-1.6, 0, top * 0.70), (0.7, 0.4, 0.4), DARK)
    p.bevel(width=0.03, segments=1)
    return p.finish("Mesh_Refinery_CrownMasts", coll)


def capsule_cradle(coll, mats):
    """The steelwork that visibly carries the cantilevered capsule.

    A 17 m module hanging off a wall is the one place this assembly could
    collapse into 'boxes floating near boxes'. Two brackets and a hoop, all
    reading as load path, are what stop it.
    Origin at the tower's -Y face, at the capsule's axis height.
    """
    p = Part(mats)
    for sx in (-1, 1):
        p.box((sx * 3.3, -3.2, -2.9), (0.55, 6.6, 0.55), STEEL)
        p.box((sx * 3.3, -1.9, -1.6), (0.5, 4.6, 0.5), STEEL,
              rot=Matrix.Rotation(math.radians(38), 4, 'X'))
        p.box((sx * 3.3, -0.4, -3.9), (1.2, 1.4, 2.6), DARK)
    p.box((0, -6.3, -2.9), (7.2, 0.6, 0.55), STEEL)
    p.box((0, -0.5, -1.4), (7.6, 1.4, 4.6), DARK)              # wall spreader
    for i in range(7):
        p.rivets((-3.2, -1.25, -3.4 + i * 0.75), (3.2, -1.25, -3.4 + i * 0.75),
                 9, radius=0.10, height=0.09, axis='Y', mat=STEEL)
    # A hoop round the capsule's waist.
    for sx in (-1, 1):
        p.box((sx * 2.95, -8.4, 0.0), (0.5, 0.6, 5.6), STEEL)
    p.box((0, -8.4, 2.75), (6.4, 0.6, 0.5), STEEL)
    p.box((0, -8.4, -2.75), (6.4, 0.6, 0.5), STEEL)
    p.bevel(width=0.04, segments=1)
    return p.finish("Mesh_Refinery_CapsuleCradle", coll)


# ---------------------------------------------------------------------------
# Access: wrapping the tower in walkways
# ---------------------------------------------------------------------------

def wrap_level(coll, z, tag, faces="NSEW", spans=2):
    """Ring a tower level with wall-mounted catwalks and corner pieces.

    Placed by rotation about Z so one authored span serves all four faces:
    a `Catwalk_Wall` has its building side on +Y and runs +X, so rz=0 hangs it
    on the -Y face, 180 on +Y, 90 on +X and -90 on -X.
    """
    wall = mesh_of("Coll_Catwalk_Wall")
    corner = mesh_of("Coll_Catwalk_Corner")
    off = 0.95                       # deck half-width plus a little clearance
    if "N" in faces:                 # -Y face
        for i in range(spans):
            place("Mesh_Walk_%s_N%d" % (tag, i), wall, coll,
                  loc=(-6.6 + i * 6.0, -(FY + off), z))
    if "S" in faces:                 # +Y face
        for i in range(spans):
            place("Mesh_Walk_%s_S%d" % (tag, i), wall, coll,
                  loc=(6.6 - i * 6.0, FY + off, z), rot=(0, 0, 180))
    if "E" in faces:                 # +X face
        for i in range(spans):
            place("Mesh_Walk_%s_E%d" % (tag, i), wall, coll,
                  loc=(FX + off, -5.6 + i * 6.0, z), rot=(0, 0, 90))
    if "W" in faces:                 # -X face
        for i in range(spans):
            place("Mesh_Walk_%s_W%d" % (tag, i), wall, coll,
                  loc=(-(FX + off), 5.6 - i * 6.0, z), rot=(0, 0, -90))
    # A corner piece only where two railed faces actually meet. Placing all
    # four unconditionally leaves knuckles hanging off nothing on a partial
    # wrap, which is the kind of thing that reads as a bug from 100 m away.
    for i, (sx, sy, rz, need) in enumerate(((-1, -1, 0, "NW"), (1, -1, 90, "NE"),
                                            (1, 1, 180, "SE"),
                                            (-1, 1, 270, "SW"))):
        if all(f in faces for f in need):
            place("Mesh_Walk_%s_C%d" % (tag, i), corner, coll,
                  loc=(sx * (FX + off), sy * (FY + off), z), rot=(0, 0, rz))


# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    mats = link_materials(MATS)

    load_component("structural/tower_bay.blend",
                   ["Coll_TowerBay_Plain", "Coll_TowerBay_Windowed",
                    "Coll_TowerBay_Ribbed", "Coll_TowerBay_Buttressed",
                    "Coll_TowerBay_Shoulder", "Coll_TowerBay_Crown"])
    load_component("structural/catwalk_span.blend",
                   ["Coll_Catwalk_Wall", "Coll_Catwalk_Corner",
                    "Coll_Catwalk_Balcony", "Coll_Catwalk_Bridge",
                    "Coll_Catwalk_Stair", "Coll_Catwalk_Straight"])
    load_component("structural/support_leg.blend",
                   ["Coll_SupportLeg_Raked", "Coll_SupportLeg_Splayed",
                    "Coll_SupportLeg_Strut", "Coll_SupportLeg_Footing"])
    load_component("structural/truss_frame.blend",
                   ["Coll_Truss_Column", "Coll_Truss_Beam", "Coll_Truss_Deck",
                    "Coll_Truss_Brace", "Coll_Truss_Portal"])
    load_component("structural/hab_capsule.blend",
                   ["Coll_HabCapsule_Long", "Coll_HabCapsule_Short",
                    "Coll_HabCapsule_Tank", "Coll_HabCapsule_Cab",
                    "Coll_HabCapsule_Pod"])
    load_component("mechanical/drill_derrick.blend",
                   ["Coll_Derrick_Mast", "Coll_Derrick_PipeRack",
                    "Coll_Derrick_Winch", "Coll_Derrick_Flare"])
    load_component("mechanical/conveyor_ramp.blend",
                   ["Coll_Conveyor_Ramp", "Coll_Conveyor_Flat",
                    "Coll_Conveyor_Head", "Coll_Conveyor_Hopper",
                    "Coll_Conveyor_Trestle"])
    # Reused from the existing library, unchanged.
    load_component("structural/cabin_module.blend",
                   ["Coll_CabinModule_Cargo", "Coll_CabinModule_Workshop",
                    "Coll_CabinModule_Comms"])
    load_component("structural/handrail.blend",
                   ["Coll_Handrail_Straight", "Coll_Handrail_Ladder",
                    "Coll_Handrail_Stair"])
    load_component("structural/deck_plate.blend",
                   ["Coll_DeckPlate_Grate", "Coll_DeckPlate_Worn"])
    load_component("props/floodlight_bank.blend",
                   ["Coll_FloodlightBank_Quad", "Coll_FloodlightBank_Twin",
                    "Coll_FloodlightBank_Sweep"])
    load_component("props/light_fixture.blend",
                   ["Coll_Light_Clamp", "Coll_Light_Strip"])
    load_component("mechanical/pipe_run.blend",
                   ["Coll_PipeRun_Straight", "Coll_PipeRun_Elbow",
                    "Coll_PipeRun_Junction"])
    load_component("mechanical/vent_grille.blend",
                   ["Coll_Vent_Louvre", "Coll_Vent_Fan"])
    load_component("structural/mast_rig.blend", ["Coll_MastRig_Antenna"])

    root = collection("Coll_RefineryTower")
    c_base = collection("Refinery_Base", root)
    c_tower = collection("Refinery_Tower", root)
    c_mod = collection("Refinery_Modules", root)
    c_access = collection("Refinery_Access", root)
    c_out = collection("Refinery_Outrigger", root)
    c_plant = collection("Refinery_Plant", root)

    # -- podium and tower stack --------------------------------------------
    podium(c_base, mats)
    stack = ["Ribbed", "Buttressed", "Plain", "Shoulder", "Windowed"]
    for i, kind in enumerate(stack):
        place("Mesh_Bay_%d_%s" % (i, kind), mesh_of("Coll_TowerBay_%s" % kind),
              c_tower, loc=(0, 0, Z_TOWER + i * BAY))
    place("Mesh_Bay_5_Crown", mesh_of("Coll_TowerBay_Crown"), c_tower,
          loc=(0, 0, Z_CROWN))
    crown_masts(c_tower, mats)
    bpy.data.objects["Mesh_Refinery_CrownMasts"].location = (0.0, 0.0, Z_DECK)

    # -- legs ---------------------------------------------------------------
    # Two raked legs off the front face, one splayed A-frame on the west face:
    # the same load solved twice, which is what stops the base reading as a kit.
    for sx, tag in ((-1, "P"), (1, "S")):
        place("Mesh_Leg_Raked_%s" % tag, mesh_of("Coll_SupportLeg_Raked"),
              c_base, loc=(sx * 5.4, -(FY + 0.2), 19.4))
    place("Mesh_Leg_Splayed_W", mesh_of("Coll_SupportLeg_Splayed"), c_base,
          loc=(-(FX + 0.2), 1.5, 16.2), rot=(0, 0, -90))
    for i, (sx, sy) in enumerate(((-1, 1), (1, 1))):
        place("Mesh_Leg_Strut_%d" % i, mesh_of("Coll_SupportLeg_Strut"),
              c_base, loc=(sx * 6.2, FY + 0.2, 15.0), rot=(0, 0, 180))
    # Ground anchors immediately outboard of each foot. Every leg carries its
    # own pad, so these are tie-downs, not footings — and they only belong
    # where a foot actually lands, which is why they are derived from the leg
    # geometry rather than eyeballed.
    for i, (x, y) in enumerate(((-5.4, -24.2), (5.4, -24.2), (-20.5, 1.5))):
        place("Mesh_Anchor_%d" % i, mesh_of("Coll_SupportLeg_Footing"),
              c_base, loc=(x, y, 0.0), rot=(0, 0, 90 * (i % 2)))

    # -- cantilevered modules ----------------------------------------------
    capsule_cradle(c_mod, mats)
    bpy.data.objects["Mesh_Refinery_CapsuleCradle"].location = (0, -FY, 34.5)
    place("Mesh_Capsule_Long", mesh_of("Coll_HabCapsule_Long"), c_mod,
          loc=(0, -FY + 0.3, 34.5))
    # Ties from the tower above down onto the capsule's back. The cradle takes
    # the shear at the wall; these take the moment. Nothing else supports it,
    # so both have to be present and visibly land on the module.
    for i, sx in enumerate((-1, 1)):
        place("Mesh_Capsule_Tie_%d" % i, mesh_of("Coll_SupportLeg_Strut"),
              c_mod, loc=(sx * 2.9, -(FY + 0.1), 45.3))
    # The control cab high on the front, and pods where a walkway reaches them.
    place("Mesh_Capsule_Cab", mesh_of("Coll_HabCapsule_Cab"), c_mod,
          loc=(-3.4, -FY + 0.3, 50.6))
    place("Mesh_Capsule_Short_E", mesh_of("Coll_HabCapsule_Short"), c_mod,
          loc=(FX - 0.3, -2.6, 24.2), rot=(0, 0, 90))
    # A strut is authored running -Y and down. rz=90 swings that onto +X for
    # the east module; (180, 0, 180) flips it to run -Y and *up*, which is the
    # only way to prop the cab from a tower face that stops below it.
    for i, sy in enumerate((-1, 1)):
        place("Mesh_Short_Prop_%d" % i, mesh_of("Coll_SupportLeg_Strut"),
              c_mod, loc=(FX + 0.1, -2.6 + sy * 2.0, 35.6), rot=(0, 0, 90))
        place("Mesh_Cab_Prop_%d" % i, mesh_of("Coll_SupportLeg_Strut"), c_mod,
              loc=(-3.4 + sy * 1.9, -(FY + 0.1), 40.2), rot=(180, 0, 180))
    place("Mesh_Capsule_Pod_W", mesh_of("Coll_HabCapsule_Pod"), c_mod,
          loc=(-(FX - 0.3), 2.4, 28.6), rot=(0, 0, -90))
    place("Mesh_Capsule_Pod_N", mesh_of("Coll_HabCapsule_Pod"), c_mod,
          loc=(4.6, -FY + 0.3, 42.4))
    # The process tank belongs on the ground on its own saddles, not hung off
    # a wall 21 m up with nothing under it.
    place("Mesh_Tank_Ground", mesh_of("Coll_HabCapsule_Tank"), c_mod,
          loc=(-8.0, 16.0, 3.8), rot=(0, 0, -90))

    # -- access -------------------------------------------------------------
    # Full wraps at the two floors that matter, partial runs between them.
    # Every level ringed identically would cost 16 k triangles a floor and make
    # the tower read as a car park.
    wrap_level(c_access, Z_TOWER + BAY - 0.4, "L1")
    wrap_level(c_access, Z_TOWER + 2 * BAY - 0.4, "L2", faces="NE", spans=2)
    wrap_level(c_access, Z_TOWER + 3 * BAY - 0.4, "L3", faces="NEW")
    wrap_level(c_access, Z_TOWER + 4 * BAY - 0.4, "L4", faces="NW", spans=2)
    wrap_level(c_access, Z_CROWN - 0.4, "L5")
    for i in range(4):                       # balconies at the interesting bits
        place("Mesh_Balcony_%d" % i, mesh_of("Coll_Catwalk_Balcony"), c_access,
              loc=((-3.0, 3.0, -3.0, 2.4)[i], -(FY + 3.4),
                   (21.6, 30.6, 48.1, 39.4)[i]), rot=(0, 0, 0))
    place("Mesh_Bridge_Outrigger", mesh_of("Coll_Catwalk_Bridge"), c_access,
          loc=(FX - 0.5, OUT_Y, OUT_Z))
    for i in range(3):                       # walkways across the podium roof
        place("Mesh_Walk_Podium_%d" % i, mesh_of("Coll_Catwalk_Straight"),
              c_access, loc=(-11.0, -6.4 + i * 6.2, PODIUM_H + 0.95),
              rot=(0, 0, 90))
    for i, z in enumerate((PODIUM_H + 1.0, PODIUM_H + 5.5)):
        place("Mesh_Stair_%d" % i, mesh_of("Coll_Catwalk_Stair"), c_access,
              loc=(-(FX + 3.4), -(FY + 1.6) + i * 2.2, z),
              rot=(0, 0, 90 if i % 2 else -90))
    for i in range(4):
        place("Mesh_Ladder_%d" % i, mesh_of("Coll_Handrail_Ladder"), c_access,
              loc=((FX + 1.2), -1.0 + i * 0.2, Z_TOWER + 0.6 + i * BAY))
    for i in range(6):                       # ground-level rails, player scale
        place("Mesh_Rail_Ground_%d" % i, mesh_of("Coll_Handrail_Straight"),
              c_access, loc=(-14.0 + (i % 3) * 2.24, -12.4 - (i // 3) * 0.0,
                             0.9), rot=(0, 0, 90))
    # Ground-level plating: grate and worn solid alternated, never the same
    # tile twice in a row, because a 4 x 2 patch of one tile is a texture.
    for i in range(8):
        kind = "Grate" if (i + i // 4) % 2 else "Worn"
        place("Mesh_Deck_Plate_%d_%s" % (i, kind),
              mesh_of("Coll_DeckPlate_%s" % kind), c_access,
              loc=(-3.5 + (i % 4) * 1.0, 11.0 + (i // 4) * 1.0,
                   PODIUM_H + 0.72))
    place("Mesh_Rail_Stair_Podium", mesh_of("Coll_Handrail_Stair"), c_access,
          loc=(-10.6, -(PODIUM_D / 2 + 1.2), 0.0), rot=(0, 0, 90))

    # -- outrigger deck -----------------------------------------------------
    place("Mesh_Outrigger_Deck", mesh_of("Coll_Truss_Deck"), c_out,
          loc=(OUT_X, OUT_Y, OUT_Z))
    for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (1, 1), (-1, 1))):
        place("Mesh_Outrigger_Col_%d" % i, mesh_of("Coll_Truss_Column"), c_out,
              loc=(OUT_X + sx * 7.4, OUT_Y + sy * 5.6, 0.0))
    for i, sy in enumerate((-1, 1)):
        place("Mesh_Outrigger_Beam_%d" % i, mesh_of("Coll_Truss_Beam"), c_out,
              loc=(OUT_X - 6.0, OUT_Y + sy * 5.6, OUT_Z - 2.9))
        place("Mesh_Outrigger_Brace_%d" % i, mesh_of("Coll_Truss_Brace"), c_out,
              loc=(OUT_X - 3.5, OUT_Y + sy * 5.6, 3.0))
    place("Mesh_Outrigger_Portal", mesh_of("Coll_Truss_Portal"), c_out,
          loc=(OUT_X + 1.0, OUT_Y - 11.5, 0.0), rot=(0, 0, 90))
    # Reused container modules — deliberately the warm desert palette, so the
    # deck reads as older kit parked under a newer tower.
    for i, (kind, x, y, rz) in enumerate((("Cargo", -6.2, 3.4, 0),
                                          ("Workshop", -0.4, 3.6, 180),
                                          ("Comms", 5.6, 3.2, 0))):
        place("Mesh_Deck_Module_%d_%s" % (i, kind),
              mesh_of("Coll_CabinModule_%s" % kind), c_out,
              loc=(OUT_X + x, OUT_Y + y, OUT_Z + 0.02), rot=(0, 0, rz))
    place("Mesh_Deck_Tank", mesh_of("Coll_HabCapsule_Tank"), c_out,
          loc=(OUT_X - 8.2, OUT_Y - 4.4, OUT_Z + 2.9), rot=(0, 0, -90))
    place("Mesh_Deck_Pod", mesh_of("Coll_HabCapsule_Pod"), c_out,
          loc=(OUT_X + 8.4, OUT_Y - 3.0, OUT_Z + 2.4), rot=(0, 0, 90))
    for i in range(4):
        place("Mesh_Deck_Rail_%d" % i, mesh_of("Coll_Catwalk_Straight"), c_out,
              loc=(OUT_X - 9.4 + i * 6.0, OUT_Y - 7.4, OUT_Z))

    # -- plant --------------------------------------------------------------
    place("Mesh_Derrick_Mast_Deck", mesh_of("Coll_Derrick_Mast"), c_plant,
          loc=(OUT_X + 6.4, OUT_Y - 4.8, OUT_Z + 0.1))
    place("Mesh_Derrick_PipeRack", mesh_of("Coll_Derrick_PipeRack"), c_plant,
          loc=(OUT_X + 0.6, OUT_Y - 5.4, OUT_Z + 0.1))
    place("Mesh_Derrick_Winch", mesh_of("Coll_Derrick_Winch"), c_plant,
          loc=(OUT_X - 4.0, OUT_Y - 6.2, OUT_Z + 0.1), rot=(0, 0, 24))
    place("Mesh_Flare_Stack", mesh_of("Coll_Derrick_Flare"), c_plant,
          loc=(-22.0, 12.0, 0.0))
    # Conveyor train, west of the tower: a flat feed run into the hopper, the
    # 23-degree incline out of it, and a head house landing on the podium roof.
    #
    # The ramp's top is 23.94 m along and 10.16 m up from its origin, and the
    # head house reaches 6.1 m beyond its own origin — so the origin is set
    # back far enough that the head stops just clear of the tower's west face
    # instead of growing through it.
    head_x = -(FX + 0.15) - 6.1
    ramp_o = Vector((head_x - 23.94, -7.0, 0.0))
    place("Mesh_Conveyor_Ramp", mesh_of("Coll_Conveyor_Ramp"), c_plant,
          loc=ramp_o)
    place("Mesh_Conveyor_Head", mesh_of("Coll_Conveyor_Head"), c_plant,
          loc=ramp_o + Vector((23.94, 0.0, 10.16)))
    place("Mesh_Conveyor_Hopper", mesh_of("Coll_Conveyor_Hopper"), c_plant,
          loc=ramp_o + Vector((-5.4, 0.0, -0.4)))
    # The head house overhangs the podium edge; this carries that end down.
    place("Mesh_Conveyor_Trestle", mesh_of("Coll_Conveyor_Trestle"), c_plant,
          loc=(head_x - 1.0, -7.0, 9.5))
    place("Mesh_Conveyor_Flat", mesh_of("Coll_Conveyor_Flat"), c_plant,
          loc=(ramp_o.x - 5.4, -21.0, 5.8), rot=(0, 0, 90))
    # Pipework off the podium, and the vents it breathes through.
    for i in range(5):
        place("Mesh_Pipe_Run_%d" % i, scaled("Coll_PipeRun_Straight", 2.2),
              c_plant, loc=(-12.6, -6.0 + i * 3.0, 9.4), rot=(0, 0, 90))
    for i in range(2):
        place("Mesh_Pipe_Elbow_%d" % i, scaled("Coll_PipeRun_Elbow", 2.2),
              c_plant, loc=(-12.6, 7.2 - i * 14.4, 9.4), rot=(0, 0, 90 + i * 90))
    place("Mesh_Pipe_Junction", scaled("Coll_PipeRun_Junction", 2.2), c_plant,
          loc=(-12.6, 0.6, 9.4))
    for i in range(4):
        place("Mesh_Vent_%d" % i, scaled("Coll_Vent_Louvre", 2.4), c_plant,
              loc=(-4.0 + i * 4.0, PODIUM_D / 2 + 0.15, 5.4), rot=(0, 0, 180))
    for i in range(2):
        place("Mesh_Vent_Fan_%d" % i, scaled("Coll_Vent_Fan", 2.4), c_plant,
              loc=(PODIUM_W / 2 + 0.15, -5.0 + i * 10.0, 6.0), rot=(0, 0, 90))
    # Lighting: floods on the big masses, clamp lamps along the walkways.
    floods = (("Quad", (0, -(FY + 0.6), Z_CROWN + 5.2), (0, 0, 0)),
              ("Quad", (OUT_X, OUT_Y - 7.8, OUT_Z + 1.2), (0, 0, 0)),
              ("Twin", (-(FX + 0.6), 0.0, 30.2), (0, 0, -90)),
              ("Twin", (FX + 0.6, 0.0, 39.2), (0, 0, 90)),
              ("Sweep", (0, -(FY + 0.6), 46.4), (0, 0, 0)),
              ("Sweep", (OUT_X + 9.0, OUT_Y + 6.0, OUT_Z + 1.0), (0, 0, 150)),
              ("Quad", (-9.0, -(PODIUM_D / 2 + 0.4), 7.2), (0, 0, 0)),
              ("Twin", (9.0, -(PODIUM_D / 2 + 0.4), 7.2), (0, 0, 0)))
    for i, (kind, loc, rot) in enumerate(floods):
        place("Mesh_Flood_%d_%s" % (i, kind),
              scaled("Coll_FloodlightBank_%s" % kind, 1.8), c_plant, loc=loc,
              rot=rot)
    for i in range(10):
        z = Z_TOWER + BAY - 0.9 + (i % 3) * 2 * BAY
        place("Mesh_Lamp_Clamp_%d" % i, scaled("Coll_Light_Clamp", 1.6),
              c_plant, loc=(-6.0 + (i % 4) * 4.0, -(FY + 1.5), z),
              rot=(0, 0, 180))
    for i in range(4):
        place("Mesh_Lamp_Strip_%d" % i, scaled("Coll_Light_Strip", 2.0),
              c_plant, loc=(OUT_X - 7.0 + i * 4.5, OUT_Y + 6.4, OUT_Z + 3.4))
    for i in range(3):
        place("Mesh_Antenna_%d" % i, scaled("Coll_MastRig_Antenna", 1.6),
              c_plant,
              loc=((-4.2, 4.4, 1.6)[i], (3.2, -3.6, 4.4)[i], Z_DECK + 0.4))

    dedupe_materials()
    report()
    save(out)


build()
