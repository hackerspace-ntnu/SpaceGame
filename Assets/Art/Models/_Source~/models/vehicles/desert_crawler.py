"""models/vehicles/desert_crawler — a six-legged habitat that walks the deep desert.

A chassis slung between six mech legs, carrying two converted container modules
with a lit vehicle bay in the slot between them, a gantry across their roofs and
four masts above that. It is the walking equivalent of a hauler that people also
live in: the top half is domestic, the bottom half is machinery, and the join
between them is where all the pipework, floodlights and ladders happen.

Authoring frame: **−Y forward, +X starboard, +Z up**, the library convention.
That matters here — Blender's default FBX axis conversion puts −Y on Unity's +Z,
and `SpiderWalkerLocomotion` sorts legs into a gait order by `HomeLocal.x`
(sides) and `HomeLocal.z` (fore/aft) in Unity space. Authoring on the convention
makes both come out right with no yaw correction anywhere.

**The rest pose is load-bearing, not decoration.** Stride is derived from how
far each foot sits from its own coxa yaw axis:

    strideLength = 2 * RestFootRadius * sin(yawRange * 0.85)

A leg modelled standing straight down puts its foot under its own axis, and the
machine then walks with a stride of a few centimetres however the gait is tuned.
So the legs are posed splayed — feet 3.5 m outboard of their hips — and the
linkage is bent to reach that, which is also what the reference silhouette does.
See the printed rig report at the end of a build for the numbers that came out.

    blender --background --python desert_crawler.py -- --out desert_crawler.blend
"""

import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))
from _buildlib import Part, collection, link_materials, parse_out, report, save, start  # noqa: E402

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
COMPONENTS = os.path.join(LIB, "components")

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0
    "Mat_Metal_Steel_Dark",      # 1
    "Mat_Metal_Steel_Worn",      # 2
    "Mat_Paint_Olive_Deep",      # 3
    "Mat_Paint_Roof_Green",      # 4
    "Mat_Metal_Rust_Heavy",      # 5
    "Mat_Paint_Warn_Red",        # 6
    "Mat_Emissive_Cabin_Warm",   # 7
    "Mat_Emissive_Amber",        # 8
    "Mat_Neutral_Black_Matte",   # 9
    "Mat_Metal_Chrome_Scuffed",  # 10
    "Mat_Plastic_Rubber_Black",  # 11
    "Mat_Glass_Canopy_Tinted",   # 12
    "Mat_Metal_Copper_Oxide",    # 13
]
(HULL, DARK, STEEL, OLIVE, GREEN, RUST, RED, WARM, AMBER, BLACK, CHROME,
 RUBBER, GLASS, COPPER) = range(14)

# ---------------------------------------------------------------------------
# Layout
# ---------------------------------------------------------------------------

HIP_Z = 6.30          # hip height above the ground at rest
FOOT_REACH = 4.30     # how far outboard of its hip each foot is planted
COXA_RISE = 0.85      # the yaw joint sits this far above the hip

CHASSIS = dict(x=3.70, y0=-6.40, y1=6.40, z0=5.55, z1=8.70)
DECK_Z = 8.70
MODULE_X = 4.15       # centre of each container module
MODULE_Y = -0.40
BAY_X = 1.75          # half-width of the slot between the modules
# Clear of the module roofs (which top out at 17.83), not level with them: the
# gantry has to bridge over the containers, and the masts have to rise off it.
GANTRY_Z = 18.60

# id, hip position, outward yaw of the leg's bend plane (degrees from +X)
LEGS = [
    ("P1", Vector((3.90, -5.40, HIP_Z)), -26.0),
    ("P2", Vector((4.30, 0.00, HIP_Z)), 0.0),
    ("P3", Vector((3.90, 5.40, HIP_Z)), 26.0),
    ("N1", Vector((-3.90, -5.40, HIP_Z)), 206.0),
    ("N2", Vector((-4.30, 0.00, HIP_Z)), 180.0),
    ("N3", Vector((-3.90, 5.40, HIP_Z)), 154.0),
]

# No two legs are dressed the same: shroud, wheel and weathering all rotate.
LEG_DRESS = {
    "P1": ("Plate", "Single"),
    "P2": ("Vented", "Twin"),
    "P3": ("Ribbed", None),
    "N1": ("Ribbed", "Single"),
    "N2": ("Plate", "Twin"),
    "N3": ("Patched", None),
}

SOURCES = {}          # collection name -> {object name: mesh datablock}


# ---------------------------------------------------------------------------
# Appending components
# ---------------------------------------------------------------------------

def load_component(path, collections):
    """Append the named collections from a component file and index their meshes.

    Only the mesh datablocks are kept. Every placement in this model is a fresh
    object pointing at shared mesh data, so six legs cost one leg's memory and
    Unity gets six renderers on one mesh rather than six copies of it.
    """
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
        # The appended objects are scaffolding; the meshes outlive them because
        # the placements below hold references.
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
        raise SystemExit("%s holds %d meshes; name one" % (coll_name, len(meshes)))
    return next(iter(meshes.values()))


PLACED = {}


def place(name, data, coll, matrix=None, loc=None, rot=None):
    """Instance a component mesh at a world transform.

    The matrix is built here and remembered, rather than being set as
    loc/rot and read back off `matrix_world` later. `matrix_world` is
    depsgraph-backed and stays stale until the view layer updates, so reading it
    inside the same pass hands back an identity matrix and every part attached
    from it lands on top of its own bone instead of where it was put.
    """
    if matrix is None:
        matrix = Matrix.Translation(Vector(loc or (0, 0, 0)))
        if rot is not None:
            matrix = matrix @ Euler(
                [math.radians(a) for a in rot], 'XYZ').to_matrix().to_4x4()
    obj = bpy.data.objects.new(name, data)
    obj.matrix_world = matrix
    coll.objects.link(obj)
    PLACED[obj.name] = matrix
    return obj


def dedupe_materials():
    """Fold any `Mat_X.001` back onto `Mat_X`.

    Appending from eight component files can bring eight copies of the same
    palette material if the library link does not resolve identically. Left
    alone that is exactly the greys problem the palette exists to prevent, and
    it also multiplies Unity's material count.
    """
    folded = 0
    for mat in list(bpy.data.materials):
        base = mat.name[:-4]
        if len(mat.name) > 4 and mat.name[-4] == '.' and mat.name[-3:].isdigit() \
                and base in bpy.data.materials:
            mat.user_remap(bpy.data.materials[base])
            bpy.data.materials.remove(mat)
            folded += 1
    if folded:
        print("  folded %d duplicate material(s) back onto the palette" % folded)


# ---------------------------------------------------------------------------
# Leg posing
# ---------------------------------------------------------------------------

class LegPose:
    """One leg's rest pose, solved so the foot lands where the gait needs it.

    The component's legs are modelled standing straight, foot under hip. Left
    like that, `RestFootRadius` is near zero and so is the stride. This bends
    the linkage until the sole is `FOOT_REACH` outboard and on the ground.
    """

    def __init__(self, leg_id, hip_world, yaw_deg, hip, knee, ankle, sole_z):
        self.id = leg_id
        self.yaw = math.radians(yaw_deg)

        a = (knee - hip).length
        b = (ankle - knee).length
        c = ankle.z - sole_z                      # ankle down to the sole

        # Where the ankle has to be for a vertical foot to reach the target.
        target = Vector((FOOT_REACH, -(hip_world.z - sole_z) + c))
        d = target.length
        if d > (a + b) * 0.995:
            raise SystemExit(
                "Leg %s cannot reach: needs %.2f m of a %.2f m linkage. Lower "
                "HIP_Z or shorten FOOT_REACH." % (leg_id, d, a + b))

        # Two-link IK, elbow on the same side of the hip-ankle line as the
        # modelled rest pose, so the knee still bulges the way the art does.
        interior = math.acos(max(-1.0, min(1.0, (a * a + d * d - b * b) / (2 * a * d))))
        to_ankle = math.atan2(target.y, target.x)
        old_upper = math.atan2(knee.z - hip.z, knee.x - hip.x)
        old_lower = math.atan2(ankle.z - knee.z, ankle.x - knee.x)
        old_line = math.atan2(ankle.z - hip.z, ankle.x - hip.x)
        side = 1.0 if math.sin(old_upper - old_line) > 0 else -1.0

        new_upper = to_ankle + side * interior
        knee_new = Vector((a * math.cos(new_upper), a * math.sin(new_upper)))
        new_lower = math.atan2(target.y - knee_new.y, target.x - knee_new.x)

        self.d_upper = new_upper - old_upper
        self.d_lower = new_lower - old_lower - self.d_upper
        self.d_foot = -(new_lower - old_lower)     # keep the sole flat on the ground

        # World transforms. A 2-D angle measured as atan2(z, x) turns the other
        # way to a Blender rotation about +Y, hence the negation.
        base = Matrix.Translation(hip_world) @ Matrix.Rotation(self.yaw, 4, 'Z')
        r1 = Matrix.Rotation(-self.d_upper, 4, 'Y')
        r2 = r1 @ Matrix.Rotation(-self.d_lower, 4, 'Y')
        r3 = r2 @ Matrix.Rotation(-self.d_foot, 4, 'Y')

        self.m_upper = base @ r1
        self.m_lower = base @ Matrix.Translation(r1 @ (knee - hip)) @ r2
        self.m_foot = (base @ Matrix.Translation(r1 @ (knee - hip))
                       @ Matrix.Translation(r2 @ (ankle - knee)) @ r3)

        self.hip = hip_world
        self.knee = self.m_lower.translation
        self.ankle = self.m_foot.translation
        self.contact = self.m_foot @ Vector((ankle.x - ankle.x, 0.0, sole_z - ankle.z))
        self.coxa = hip_world + Vector((0, 0, COXA_RISE))

        # Axes the rig has to measure: the yaw axle is vertical, the three pitch
        # axles are normal to the bend plane, and the sole rolls about the plane's
        # own forward direction.
        self.axle = Matrix.Rotation(self.yaw, 4, 'Z') @ Vector((0, 1, 0))
        self.forward = Matrix.Rotation(self.yaw, 4, 'Z') @ Vector((1, 0, 0))
        self.reach = a + b + c


# ---------------------------------------------------------------------------
# Unique geometry
# ---------------------------------------------------------------------------

def build_chassis(coll):
    """The machinery deck the legs hang from: a tapered hull with six coxa
    turrets, a ribbed belly, and the sump the whole thing drains into."""
    p = Part(PALETTE)
    x, y0, y1, z0, z1 = (CHASSIS[k] for k in ("x", "y0", "y1", "z0", "z1"))

    # Chamfered box, so the underside catches light instead of going flat black.
    p.loft([(y0, [(-x + 0.7, z0 + 0.5), (x - 0.7, z0 + 0.5), (x, z0 + 1.3),
                  (x, z1), (-x, z1), (-x, z0 + 1.3)]),
            (y0 + 1.4, [(-x - 0.5, z0 + 0.15), (x + 0.5, z0 + 0.15),
                        (x + 0.8, z0 + 1.3), (x + 0.8, z1), (-x - 0.8, z1),
                        (-x - 0.8, z0 + 1.3)]),
            (y1 - 1.4, [(-x - 0.5, z0 + 0.15), (x + 0.5, z0 + 0.15),
                        (x + 0.8, z0 + 1.3), (x + 0.8, z1), (-x - 0.8, z1),
                        (-x - 0.8, z0 + 1.3)]),
            (y1, [(-x + 0.7, z0 + 0.5), (x - 0.7, z0 + 0.5), (x, z0 + 1.3),
                  (x, z1), (-x, z1), (-x, z0 + 1.3)])],
           'Y', HULL)

    # Belly ribs and a sump: the underside is what a player standing beneath the
    # machine actually looks at.
    for i in range(9):
        yy = y0 + 1.0 + i * (y1 - y0 - 2.0) / 8.0
        p.box((0, yy, z0 + 0.10), (2 * x + 1.0, 0.26, 0.30), STEEL)
    p.box((0, 0.6, z0 - 0.18), (2.60, 3.40, 0.55), DARK)
    p.box((0, 0.6, z0 - 0.44), (1.90, 2.60, 0.30), OLIVE)
    p.greeble((-x, y0 + 1.2, z0 + 0.05), (x, y1 - 1.2, z0 + 0.45), 26, seed=7,
              scale=(0.16, 0.52), mat=DARK, flatten='Z')

    # Coxa turrets — the leg sockets, sticking proud of the flanks.
    for leg_id, hip, yaw in LEGS:
        n = Matrix.Rotation(math.radians(yaw), 4, 'Z') @ Vector((1, 0, 0))
        c = hip + n * 0.35 + Vector((0, 0, COXA_RISE * 0.5))
        p.cyl((c.x, c.y, c.z), 1.05, 2.30, 'Z', 14, HULL)
        p.cyl((c.x, c.y, c.z + 1.10), 1.20, 0.30, 'Z', 14, DARK)
        p.cyl((c.x, c.y, c.z - 1.05), 0.86, 0.34, 'Z', 14, STEEL)
        p.torus((c.x, c.y, c.z - 0.55), 1.02, 0.13, 'Z', 14, 6, DARK)
        for k in range(6):
            a = 2 * math.pi * k / 6
            p.cyl((c.x + math.cos(a) * 0.72, c.y + math.sin(a) * 0.72,
                   c.z + 1.22), 0.09, 0.24, 'Z', 6, RUST)

    # Upper skirt and the walkway edge the deck sits on.
    p.box((0, 0, z1 - 0.16), (2 * x + 1.9, y1 - y0 + 0.5, 0.34), OLIVE)
    for sx in (-1, 1):
        p.box((sx * (x + 0.92), 0, z1 - 0.80), (0.24, y1 - y0 - 1.2, 0.90),
              GREEN)
    p.bevel(width=0.03, segments=2)
    p.finish("Mesh_Crawler_Chassis", coll)


def build_deck(coll):
    """The platform the modules stand on, overhanging the chassis to port and
    starboard so the containers sit wider than the machinery."""
    p = Part(PALETTE)
    p.box((0, MODULE_Y, DECK_Z + 0.16), (14.10, 8.60, 0.32), STEEL)
    p.box((0, MODULE_Y, DECK_Z + 0.36), (13.70, 8.20, 0.14), HULL)
    for sx in (-1, 1):                                    # outrigger brackets
        for yy in (-3.1, 0.0, 3.1):
            p.box((sx * 5.55, yy + MODULE_Y, DECK_Z - 0.34),
                  (3.20, 0.34, 0.72), STEEL,
                  rot=Matrix.Rotation(math.radians(sx * 9), 4, 'Y'))
    p.box((0, MODULE_Y - 4.36, DECK_Z + 0.30), (14.10, 0.34, 0.62), OLIVE)
    p.box((0, MODULE_Y + 4.36, DECK_Z + 0.30), (14.10, 0.34, 0.62), OLIVE)
    p.bevel(width=0.025, segments=2)
    p.finish("Mesh_Crawler_Deck", coll)


def build_bay(coll):
    """The lit slot between the two modules — a vehicle garage, open forward.

    This is the only warm thing on the machine and the whole reason the two
    modules are set apart rather than butted together.
    """
    p = Part(PALETTE)
    z0 = DECK_Z + 0.43
    back = MODULE_Y + 2.30
    roof = z0 + 4.40

    p.box((0, (back + MODULE_Y - 3.9) / 2, z0 - 0.08),
          (BAY_X * 2, back - (MODULE_Y - 3.9), 0.22), DARK)     # floor
    p.box((0, back + 0.18, (z0 + roof) / 2), (BAY_X * 2, 0.36, roof - z0),
          OLIVE)                                                # back wall
    p.box((0, back + 0.02, (z0 + roof) / 2 + 0.3),
          (BAY_X * 1.7, 0.10, roof - z0 - 1.5), HULL)
    p.box((0, (back + MODULE_Y - 3.9) / 2, roof), (BAY_X * 2, back - (MODULE_Y - 3.9), 0.26),
          DARK)                                                 # ceiling
    for sx in (-1, 1):                                          # side linings
        p.box((sx * (BAY_X - 0.09), (back + MODULE_Y - 3.9) / 2, (z0 + roof) / 2),
              (0.18, back - (MODULE_Y - 3.9), roof - z0), HULL)

    # Light spill: emissive strips in the ceiling coves rather than a lamp, so
    # the bay glows from an edge the eye cannot resolve.
    for sx in (-1, 1):
        p.box((sx * (BAY_X - 0.28), (back + MODULE_Y - 3.9) / 2 + 0.4, roof - 0.22),
              (0.30, back - (MODULE_Y - 3.9) - 1.4, 0.10), WARM)
    p.box((0, back - 0.14, z0 + 2.35), (BAY_X * 1.5, 0.10, 0.34), WARM)

    # Ramp and threshold at the mouth.
    lip = MODULE_Y - 3.9
    p.box((0, lip - 0.30, z0 - 0.20), (BAY_X * 2, 0.72, 0.20), STEEL,
          rot=Matrix.Rotation(math.radians(-13), 4, 'X'))
    p.box((0, lip, z0 + 0.02), (BAY_X * 2 + 0.3, 0.24, 0.30), RED)
    for sx in (-1, 1):
        p.box((sx * (BAY_X + 0.16), lip, (z0 + roof) / 2), (0.34, 0.34, roof - z0),
              STEEL)
    p.box((0, lip, roof + 0.10), (BAY_X * 2 + 0.8, 0.40, 0.44), OLIVE)
    p.bevel(width=0.022, segments=2)
    p.finish("Mesh_Crawler_Bay", coll)


def build_gantry(coll):
    """Steel frame bridging both module roofs, and the deck the masts stand on.

    Modelled rather than reused: the span is specific to how far apart these two
    modules sit, and a component that only ever fits one model is not a
    component.
    """
    p = Part(PALETTE)
    z = GANTRY_Z
    p.box((0, MODULE_Y, z + 0.14), (13.20, 4.60, 0.28), STEEL)
    p.box((0, MODULE_Y, z + 0.32), (12.80, 4.20, 0.12), HULL)

    # Stanchions down onto the module roof eaves, which is the only flat run the
    # containers offer — a leg landing mid-slope would need its own shoe.
    for sx in (-1, 1):
        for yy in (-1.7, 1.7):
            p.box((sx * 5.85, MODULE_Y + yy, z - 1.55), (0.36, 0.36, 3.10), STEEL)
            p.box((sx * 2.05, MODULE_Y + yy, z - 1.55), (0.32, 0.32, 3.10), STEEL)
            p.box((sx * 5.85, MODULE_Y + yy, z - 3.00), (0.62, 0.62, 0.22), DARK)
            p.box((sx * 2.05, MODULE_Y + yy, z - 3.00), (0.58, 0.58, 0.22), DARK)
        # Cross-braced trusses under the deck.
        for yy in (-2.15, 2.15):
            p.box((sx * 3.95, MODULE_Y + yy, z - 0.30), (4.10, 0.20, 0.20), STEEL)
            p.box((sx * 3.95, MODULE_Y + yy, z - 2.30), (4.10, 0.20, 0.20), STEEL)
            for k in range(3):
                p.box((sx * (2.4 + k * 1.55), MODULE_Y + yy, z - 1.30),
                      (0.16, 0.16, 2.40), STEEL,
                      rot=Matrix.Rotation(math.radians(34 * (-1 if k % 2 else 1)),
                                          4, 'Y'))
    p.box((0, MODULE_Y, z - 0.42), (4.60, 3.60, 0.34), OLIVE)   # centre spine
    p.box((0, MODULE_Y - 2.35, z + 0.42), (13.20, 0.24, 0.36), GREEN)
    p.box((0, MODULE_Y + 2.35, z + 0.42), (13.20, 0.24, 0.36), GREEN)
    p.bevel(width=0.025, segments=2)
    p.finish("Mesh_Crawler_Gantry", coll)


def build_prow(coll):
    """The forward face: floodlight brow, sensor head and the bull bar. This is
    the machine's face and it is the one part that reads at any distance."""
    p = Part(PALETTE)
    y = CHASSIS["y0"]
    p.box((0, y - 0.55, 7.40), (7.20, 1.10, 2.10), HULL)
    p.box((0, y - 1.05, 8.10), (7.60, 0.60, 0.80), OLIVE)        # brow
    p.box((0, y - 0.90, 6.45), (6.60, 0.50, 0.50), DARK)

    p.box((0, y - 1.30, 7.55), (1.90, 0.60, 1.05), DARK)         # sensor head
    p.box((0, y - 1.55, 7.60), (1.55, 0.24, 0.72), GLASS)
    p.box((0, y - 1.62, 8.16), (1.70, 0.34, 0.14), STEEL)
    for sx in (-1, 1):
        p.cyl((sx * 1.15, y - 1.42, 7.55), 0.14, 0.36, 'Y', 10, AMBER)

    # Bull bar, hung off the chin on two struts.
    p.cyl((0, y - 1.95, 5.95), 0.20, 7.00, 'X', 10, STEEL)
    for sx in (-1, 1):
        p.cyl((sx * 3.45, y - 1.35, 5.95), 0.18, 1.30, 'Y', 8, STEEL)
        p.box((sx * 2.30, y - 1.90, 6.45), (0.22, 0.22, 1.10), STEEL,
              rot=Matrix.Rotation(math.radians(sx * -12), 4, 'Y'))
    p.box((0, y - 1.98, 5.95), (1.60, 0.30, 0.62), RED)
    p.rivets((-3.2, y - 1.10, 8.10), (3.2, y - 1.10, 8.10), 14, 0.06, 0.05, 'Y',
             RUST)
    p.bevel(width=0.025, segments=2)
    p.finish("Mesh_Crawler_Prow", coll)


def build_services(coll):
    """Plumbing, tanks and cable trays where the domestic half meets the
    machinery: the band that makes the join look accumulated rather than
    designed."""
    p = Part(PALETTE)
    z = DECK_Z + 0.55
    for sx in (-1, 1):
        p.cyl((sx * 6.55, MODULE_Y - 1.2, z + 0.55), 0.62, 4.60, 'Y', 12, HULL)
        p.cyl((sx * 6.55, MODULE_Y - 3.5, z + 0.55), 0.68, 0.24, 'Y', 12, OLIVE)
        p.cyl((sx * 6.55, MODULE_Y + 1.1, z + 0.55), 0.68, 0.24, 'Y', 12, OLIVE)
        for k in range(3):
            p.cyl((sx * 6.55, MODULE_Y - 2.6 + k * 1.4, z + 1.20), 0.09, 0.55,
                  'Z', 8, COPPER)
        p.box((sx * 6.30, MODULE_Y + 3.05, z + 0.40), (1.05, 1.30, 1.10), DARK)
        p.cyl((sx * 6.30, MODULE_Y + 3.05, z + 1.10), 0.30, 0.55, 'Z', 10, STEEL)
        p.box((sx * 5.95, MODULE_Y, z - 0.10), (0.34, 8.00, 0.22), STEEL)
        p.greeble((sx * 5.60, MODULE_Y - 3.6, z - 0.05),
                  (sx * 6.20, MODULE_Y + 3.6, z + 0.28), 14,
                  seed=11 + sx, scale=(0.10, 0.34), mat=RUBBER, flatten='Z')
    p.bevel(width=0.02, segments=2)
    p.finish("Mesh_Crawler_Services", coll)


# ---------------------------------------------------------------------------
# Rig
# ---------------------------------------------------------------------------

def build_armature(poses, coll):
    """`CRAWLER_Rig`: a root, six Coxa/Hip/Knee/Ankle/Foot chains and four mast
    bones, named to the convention `WalkerRig.Build` discovers at runtime."""
    data = bpy.data.armatures.new("CRAWLER_RigData")
    arm = bpy.data.objects.new("CRAWLER_Rig", data)
    coll.objects.link(arm)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    edit = data.edit_bones

    root = edit.new("Root")
    root.head = Vector((0, 0, DECK_Z - 1.2))
    root.tail = Vector((0, 0, DECK_Z + 0.6))

    for pose in poses:
        coxa = edit.new("Coxa_%s" % pose.id)
        coxa.head, coxa.tail, coxa.parent = pose.coxa, pose.hip, root

        hip = edit.new("Hip_%s" % pose.id)
        hip.head, hip.tail, hip.parent = pose.hip, pose.knee, coxa
        hip.use_connect = True

        knee = edit.new("Knee_%s" % pose.id)
        knee.head, knee.tail, knee.parent = pose.knee, pose.ankle, hip
        knee.use_connect = True

        ankle = edit.new("Ankle_%s" % pose.id)
        ankle.head, ankle.tail, ankle.parent = pose.ankle, pose.contact, knee
        ankle.use_connect = True

        foot = edit.new("Foot_%s" % pose.id)
        foot.head = pose.contact
        foot.tail = pose.contact + pose.forward * 1.10
        foot.parent, foot.use_connect = ankle, True

    for name, x, y in (("Mast_FP", 5.20, -1.40), ("Mast_FN", -5.20, -1.40),
                       ("Mast_RP", 5.20, 1.40), ("Mast_RN", -5.20, 1.40)):
        mast = edit.new(name)
        mast.head = Vector((x, MODULE_Y + y, GANTRY_Z + 0.38))
        mast.tail = Vector((x, MODULE_Y + y, GANTRY_Z + 4.0))
        mast.parent = mast.parent = root

    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def bone_matrix(arm, bone_name):
    """The transform Blender applies to a BONE-parented child.

    Bone parenting hangs the child off the bone's *tail*, with the bone's local
    Y running head-to-tail — not off its head, which is the trap.
    """
    bone = arm.data.bones[bone_name]
    return (arm.matrix_world @ bone.matrix_local
            @ Matrix.Translation(Vector((0, bone.length, 0))))


def attach(obj, arm, bone_name, world):
    """Bone-parent `obj`, keeping it at `world`.

    `matrix_parent_inverse` is deliberately left at identity: FBX has no such
    concept and silently drops whatever is parked there, which puts every part
    of the model somewhere else the moment it reaches Unity. Everything goes in
    `matrix_basis` instead, which survives because these are rigid transforms
    with uniform scale and so decompose cleanly to loc/rot/scale.
    """
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = bone_matrix(arm, bone_name).inverted() @ world


def pin_mesh():
    """One shared mesh for all thirty axle pins: a stubby bar, long on +Z.

    `WalkerRig.MeasureAxle` takes a joint's hinge axis from the longest extent
    of the first child whose name contains "Pin", so the pin's *proportions* are
    the data — it has to be unambiguously longer on one axis than the others.
    """
    p = Part(PALETTE)
    p.cyl((0, 0, 0), 0.17, 1.30, 'Z', 10, DARK)
    p.cyl((0, 0, 0.60), 0.23, 0.16, 'Z', 10, CHROME)
    p.cyl((0, 0, -0.60), 0.23, 0.16, 'Z', 10, CHROME)
    obj = p.finish("_PinProto", bpy.context.scene.collection)
    data = obj.data
    bpy.data.objects.remove(obj, do_unlink=True)
    return data


def axis_matrix(origin, axis):
    """Place a pin so its long (+Z) side lies along `axis`."""
    return (Matrix.Translation(origin)
            @ Vector(axis).normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4())


# --- dressing a limb ------------------------------------------------------
#
# A segment's own axis is not the leg's vertical: the thigh leans 27 degrees off
# it before the pose rotation is applied at all. Anything bolted to a limb has
# to be built off the limb's direction, or it hangs at an angle and reads as
# having fallen off.

def limb_axis(joint_a, joint_b):
    """Unit direction from one joint to the next, in the component's frame."""
    return (joint_b - joint_a).normalized()


def limb_out(axis):
    """The in-plane perpendicular to `axis`, pointing to the knee (+X) side."""
    p = Vector((axis.z, 0.0, -axis.x))
    return p if p.x >= 0 else -p


def limb_tilt(axis):
    """Rotation about Y taking a part's own −Z onto `axis`."""
    return Matrix.Rotation(-math.atan2(axis.x, -axis.z), 4, 'Y')


# ---------------------------------------------------------------------------
# Assembly
# ---------------------------------------------------------------------------

def build():
    out = parse_out()
    start(out)
    global PALETTE
    PALETTE = link_materials(MATS)

    load_component("mechanical/walker_leg.blend", ["Coll_WalkerLeg_Heavy"])
    load_component("mechanical/leg_shroud.blend",
                   ["Coll_LegShroud_Plate", "Coll_LegShroud_Ribbed",
                    "Coll_LegShroud_Patched", "Coll_LegShroud_Vented"])
    load_component("mechanical/road_wheel.blend",
                   ["Coll_RoadWheel_Twin", "Coll_RoadWheel_Single"])
    load_component("structural/cabin_module.blend",
                   ["Coll_CabinModule_Habitat", "Coll_CabinModule_Workshop"])
    load_component("structural/mast_rig.blend",
                   ["Coll_MastRig_Flag", "Coll_MastRig_Pennant",
                    "Coll_MastRig_Antenna"])
    load_component("structural/handrail.blend",
                   ["Coll_Handrail_Straight", "Coll_Handrail_Gate",
                    "Coll_Handrail_Ladder"])
    load_component("props/floodlight_bank.blend",
                   ["Coll_FloodlightBank_Quad", "Coll_FloodlightBank_Twin",
                    "Coll_FloodlightBank_Single"])
    load_component("structural/deck_plate.blend",
                   ["Coll_DeckPlate_Grate", "Coll_DeckPlate_Worn",
                    "Coll_DeckPlate_Hatch"])
    load_component("mechanical/pipe_run.blend",
                   ["Coll_PipeRun_Straight", "Coll_PipeRun_Elbow",
                    "Coll_PipeRun_CableBundle", "Coll_PipeRun_Duct"])
    load_component("mechanical/vent_grille.blend",
                   ["Coll_Vent_Louvre", "Coll_Vent_Scoop"])
    load_component("props/light_fixture.blend",
                   ["Coll_Light_Clamp", "Coll_Light_Strip"])
    load_component("props/wall_locker.blend",
                   ["Coll_WallLocker_Dented", "Coll_WallLocker_OpenShelf"])

    body = collection("Crawler_Body")
    legs_coll = collection("Crawler_Legs")
    fit = collection("Crawler_Fittings")
    rig_coll = collection("Crawler_Rig")

    build_chassis(body)
    build_deck(body)
    build_bay(body)
    build_gantry(body)
    build_prow(body)
    build_services(body)

    # ---- legs -----------------------------------------------------------
    hip = Vector((0.20, 0.0, 7.80))       # the component's own joint offsets,
    knee = Vector((1.95, 0.0, 4.35))      # printed by walker_leg.py
    ankle = Vector((0.00, 0.0, 1.45))
    poses = [LegPose(i, p, y, hip, knee, ankle, 0.0) for i, p, y in LEGS]

    thigh = limb_axis(hip, knee)
    thigh_out = limb_out(thigh)
    shank = limb_axis(knee, ankle)
    shank_out = limb_out(shank)

    arm = build_armature(poses, rig_coll)
    pins = pin_mesh()

    upper = mesh_of("Coll_WalkerLeg_Heavy", "Upper")
    lower = mesh_of("Coll_WalkerLeg_Heavy", "Lower")
    foot = mesh_of("Coll_WalkerLeg_Heavy", "Foot")

    for pose in poses:
        shroud_name, wheel_name = LEG_DRESS[pose.id]

        u = place("Mesh_Leg_%s_Upper" % pose.id, upper, legs_coll, pose.m_upper)
        low = place("Mesh_Leg_%s_Lower" % pose.id, lower, legs_coll, pose.m_lower)
        ft = place("Mesh_Leg_%s_Foot" % pose.id, foot, legs_coll, pose.m_foot)
        attach(u, arm, "Hip_%s" % pose.id, pose.m_upper)
        attach(low, arm, "Knee_%s" % pose.id, pose.m_lower)
        attach(ft, arm, "Foot_%s" % pose.id, pose.m_foot)

        # Shroud lying along the outer face of the thigh, hung from the hip.
        shroud = mesh_of("Coll_LegShroud_%s" % shroud_name)
        m = (pose.m_upper
             @ Matrix.Translation(thigh * 0.28 + thigh_out * 1.02)
             @ limb_tilt(thigh))
        s = place("Mesh_Leg_%s_Shroud" % pose.id, shroud, legs_coll, m)
        attach(s, arm, "Hip_%s" % pose.id, m)

        if wheel_name:
            # Outboard of the shank, axle across the leg's bend plane: the
            # component is authored with its axle on +X, the bend axis is +Y.
            wheel = mesh_of("Coll_RoadWheel_%s" % wheel_name)
            m = (pose.m_lower
                 @ Matrix.Translation(shank * 1.35 + shank_out * 0.98)
                 @ Matrix.Rotation(math.radians(90), 4, 'Z'))
            w = place("Mesh_Leg_%s_Wheel" % pose.id, wheel, legs_coll, m)
            attach(w, arm, "Knee_%s" % pose.id, m)

        # Axle pins. These are what the rig measures its hinge axes from, so
        # they are geometry with a job, not decoration.
        # The foot's pin is lifted clear of the sole on purpose. Only its
        # *direction* is ever read, but `WalkerRig.LowestRendererPoint` takes
        # the foot's length from the lowest renderer under the ankle and skips
        # nothing but `COL_` — a pin bar centred on the contact point hangs
        # 0.23 m through the ground and the machine then stands that much high.
        for bone, origin, axis in (
                ("Coxa_%s" % pose.id, pose.coxa, Vector((0, 0, 1))),
                ("Hip_%s" % pose.id, pose.hip, pose.axle),
                ("Knee_%s" % pose.id, pose.knee, pose.axle),
                ("Ankle_%s" % pose.id, pose.ankle, pose.axle),
                ("Foot_%s" % pose.id, pose.contact + Vector((0, 0, 0.42)),
                 pose.forward)):
            m = axis_matrix(origin, axis)
            name = "%sPin_%s" % (bone.split("_")[0], pose.id)
            pin = place(name, pins, legs_coll, m)
            attach(pin, arm, bone, m)

    # ---- superstructure --------------------------------------------------
    static = []

    static.append(place("Mesh_Module_Starboard",
                        mesh_of("Coll_CabinModule_Habitat"), body,
                        loc=(MODULE_X, MODULE_Y, DECK_Z + 0.43)))
    static.append(place("Mesh_Module_Port",
                        mesh_of("Coll_CabinModule_Workshop"), body,
                        loc=(-MODULE_X, MODULE_Y, DECK_Z + 0.43), rot=(0, 0, 180)))

    # Masts: three different rigs, so the skyline is not four of one thing.
    masts = [("Mast_FP", "Flag", 5.20, -1.40, 0), ("Mast_FN", "Flag", -5.20, -1.40, 180),
             ("Mast_RP", "Antenna", 5.20, 1.40, 0), ("Mast_RN", "Pennant", -5.20, 1.40, 155)]
    for bone, kind, x, y, yaw in masts:
        m = (Matrix.Translation(Vector((x, MODULE_Y + y, GANTRY_Z + 0.38)))
             @ Matrix.Rotation(math.radians(yaw), 4, 'Z'))
        obj = place("Mesh_%s" % bone, mesh_of("Coll_MastRig_%s" % kind), body, m)
        attach(obj, arm, bone, m)

    # Handrails down both long edges of the gantry, with a gate where the ladder
    # arrives. Only the long edges: the short ends butt against the masts.
    rail_z = GANTRY_Z + 0.38
    for sx in (-1, 1):
        side = "P" if sx > 0 else "N"
        for i, yy in enumerate((-2.10, -0.10)):
            kind = "Gate" if (sx == 1 and i == 1) else "Straight"
            static.append(place(
                "Mesh_Rail_%s%d" % (side, i),
                mesh_of("Coll_Handrail_%s" % kind), fit,
                loc=(sx * 6.20, MODULE_Y + yy, rail_z)))
    static.append(place("Mesh_Rail_Fore", mesh_of("Coll_Handrail_Straight"),
                        fit, loc=(1.2, MODULE_Y - 2.20, rail_z), rot=(0, 0, -90)))
    static.append(place("Mesh_Ladder_Gantry", mesh_of("Coll_Handrail_Ladder"),
                        fit, loc=(6.05, MODULE_Y - 0.10, GANTRY_Z - 3.0),
                        rot=(0, 0, 180)))
    static.append(place("Mesh_Ladder_Deck", mesh_of("Coll_Handrail_Ladder"),
                        fit, loc=(-6.90, MODULE_Y + 2.60, DECK_Z + 0.45),
                        rot=(0, 0, 180)))

    # Floodlights: a quad brow forward, twins on the flanks, one on the gantry.
    static.append(place("Mesh_Flood_Brow", mesh_of("Coll_FloodlightBank_Quad"),
                        fit, loc=(0, CHASSIS["y0"] - 1.35, 8.55)))
    for sx in (-1, 1):
        static.append(place("Mesh_Flood_Brow%s" % ("P" if sx > 0 else "N"),
                            mesh_of("Coll_FloodlightBank_Twin"), fit,
                            loc=(sx * 2.85, CHASSIS["y0"] - 1.10, 7.10),
                            rot=(0, 0, sx * -14)))
        static.append(place("Mesh_Flood_Flank%s" % ("P" if sx > 0 else "N"),
                            mesh_of("Coll_FloodlightBank_Single"), fit,
                            loc=(sx * 4.60, -2.10, 8.30), rot=(0, 0, sx * -90)))
    static.append(place("Mesh_Flood_Gantry",
                        mesh_of("Coll_FloodlightBank_Twin"), fit,
                        loc=(0, MODULE_Y - 2.30, GANTRY_Z + 0.90)))

    # Deck plating where the gantry is actually walked, not wall to wall: these
    # are 2 k triangles each and only the centre span is ever stood on.
    for i, (xx, yy, kind) in enumerate((
            (-2.8, -1.1, "Grate"), (0.0, -1.1, "Worn"),
            (0.0, 1.1, "Grate"), (2.8, 1.1, "Hatch"))):
        static.append(place("Mesh_GantryPlate_%d" % i,
                            mesh_of("Coll_DeckPlate_%s" % kind), fit,
                            loc=(xx, MODULE_Y + yy, GANTRY_Z + 0.30),
                            rot=(0, 0, 90 * (i % 4))))

    # Services: pipes and cable trays threading the join, vents on the flanks.
    runs = [("Straight", (-6.9, -2.4, 9.6), (0, 0, 90)),
            ("Elbow", (6.9, 2.4, 9.6), (0, 0, 180)),
            ("CableBundle", (6.9, -1.4, 9.9), (0, 0, 90)),
            ("Duct", (0.0, 5.9, 10.4), (0, 0, 0))]
    for i, (kind, loc, rot) in enumerate(runs):
        static.append(place("Mesh_Service_%s%d" % (kind, i),
                            mesh_of("Coll_PipeRun_%s" % kind), fit,
                            loc=loc, rot=rot))
    for sx in (-1, 1):
        for i, yy in enumerate((-3.2, 0.8)):
            static.append(place(
                "Mesh_Vent_%s%d" % ("P" if sx > 0 else "N", i),
                mesh_of("Coll_Vent_%s" % ("Scoop" if i else "Louvre")),
                fit, loc=(sx * 4.62, yy, 7.30), rot=(0, 0, sx * 90)))

    # The bay's fit-out — what makes it read as a place rather than a hole.
    bay_floor = DECK_Z + 0.50
    static.append(place("Mesh_Bay_LockerA", mesh_of("Coll_WallLocker_Dented"),
                        fit, loc=(-1.35, 1.85, bay_floor), rot=(0, 0, 90)))
    static.append(place("Mesh_Bay_LockerB", mesh_of("Coll_WallLocker_OpenShelf"),
                        fit, loc=(1.35, 1.20, bay_floor), rot=(0, 0, -90)))
    for i, yy in enumerate((-2.2, 0.8)):
        static.append(place("Mesh_Bay_Light%d" % i, mesh_of("Coll_Light_Strip"),
                            fit, loc=(0, yy, bay_floor + 4.05)))
    for i, (xx, yy) in enumerate(((-1.5, -3.0), (1.5, -3.0))):
        static.append(place("Mesh_Bay_Lamp%d" % i, mesh_of("Coll_Light_Clamp"),
                            fit, loc=(xx, yy, bay_floor + 3.60),
                            rot=(0, 0, 90 if xx < 0 else -90)))

    for obj in static:
        attach(obj, arm, "Root", PLACED[obj.name])

    dedupe_materials()

    # ---- report ----------------------------------------------------------
    print("\nRig report — what SpiderWalkerLocomotion will measure:")
    for pose in poses:
        radius = math.hypot(pose.contact.x - pose.coxa.x,
                            pose.contact.y - pose.coxa.y)
        extension = ((pose.contact - pose.hip).length / pose.reach)
        stride = 2 * radius * math.sin(math.radians(40 * 0.85))
        print("  %s  foot=(%6.2f,%6.2f,%5.2f)  radius=%.2f  extension=%.0f%%  "
              "stride=%.2f m" % (pose.id, *pose.contact, radius,
                                 extension * 100, stride))
    drop = max(abs(p.contact.z) for p in poses)
    print("  worst sole-to-ground error: %.4f m" % drop)
    print("  hull half-width %.2f m, foot span %.2f m"
          % (CHASSIS["x"], max(abs(p.contact.x) for p in poses) * 2))

    # Where the triangles actually go, grouped by the mesh they share. Six legs
    # on one mesh datablock is one entry, which is the number worth knowing.
    print("\nTriangle cost by shared mesh:")
    by_mesh = {}
    for obj in bpy.data.objects:
        if obj.type != 'MESH':
            continue
        tris = sum(len(p.vertices) - 2 for p in obj.data.polygons)
        entry = by_mesh.setdefault(obj.data.name, [0, tris])
        entry[0] += 1
    total = 0
    for name, (uses, tris) in sorted(by_mesh.items(), key=lambda kv: -kv[1][0] * kv[1][1]):
        total += uses * tris
        print("  %-40s %2d x %6d = %7d" % (name, uses, tris, uses * tris))
    print("  %-40s %19d" % ("TOTAL", total))

    save(out)


build()
