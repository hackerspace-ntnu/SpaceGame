"""Adds the scavenging tail and its cargo drums to an EXISTING desert_crawler.blend.

**This script opens a hand-edited file and saves over it.** It is deliberately
not part of `desert_crawler.py`: that script builds the crawler from nothing and
would resurrect the habitat modules the file's author deleted by hand. This one
only ever *adds*, and it verifies afterwards that every object it did not create
still sits exactly where it did before.

What it adds
------------
A portal mount straddling the aft deck, a yaw turret on top of it, seven
armoured segments arching up and forward over the machine, and a crusher claw
hanging above the prow. Plus a pair of nine-cell revolver magazines on the
flanks, each with its own funnel for the claw to drop salvage into.

Why a portal and not a pedestal
-------------------------------
There is a large hand-placed mesh sitting on the aft deck centreline (`Cube`,
x +/-2.37, y -3.41..4.93, z 8.41..10.36). Rule in this workflow is that new
geometry adapts around existing geometry rather than the other way round, so the
mount steps over it: legs land on the deck outboard of it at x +/-3.20 and the
yaw ring rides a crossbeam above its roof. That is also the better answer — a
20 m boom wants a portal, not a stalk.

Frames and conventions
----------------------
Ground is z = -4.31 (foot contacts). Deck top is z = 8.67. -Y is forward. The
armature `CRAWLER_Rig` sits at a pure translation of (0, 0, -4.31), so every
world point here is converted into armature space before it becomes a bone.

    blender --background --python desert_crawler_tail.py
"""

import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
from _buildlib import Part, link_materials, report  # noqa: E402

COMPONENTS = os.path.join(LIB, "components")


def target_path():
    """Defaults to the crawler; `-- --target <path>` points it at a copy so the
    whole run can be rehearsed before it touches the real file."""
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--target" in argv:
        return os.path.abspath(argv[argv.index("--target") + 1])
    return os.path.join(LIB, "models", "vehicles", "desert_crawler.blend")


TARGET = target_path()

MATS = [
    "Mat_Paint_Hull_Bleached",   # 0
    "Mat_Metal_Steel_Dark",      # 1
    "Mat_Metal_Steel_Worn",      # 2
    "Mat_Paint_Olive_Deep",      # 3
    "Mat_Paint_Roof_Green",      # 4
    "Mat_Metal_Rust_Heavy",      # 5
    "Mat_Paint_Warn_Red",        # 6
    "Mat_Emissive_Amber",        # 7
    "Mat_Neutral_Black_Matte",   # 8
    "Mat_Metal_Chrome_Scuffed",  # 9
    "Mat_Plastic_Rubber_Black",  # 10
    "Mat_Metal_Copper_Oxide",    # 11
]
(HULL, DARK, STEEL, OLIVE, GREEN, RUST, RED, AMBER, BLACK, CHROME, RUBBER,
 COPPER) = range(12)

DECK_Z = 8.67          # measured off Mesh_Crawler_Chassis
PORTAL_X = 3.20        # clear of the hand-placed Cube at x +/-2.37
PORTAL_Y = 4.30
BEAM_Z = 11.10         # crossbeam centre, above the Cube's 10.36 roof
YAW_Z = 11.30
J0 = Vector((0.0, PORTAL_Y, 12.05))    # first tail joint, on top of the turret

# Segment scales taper the tail. Uniform scale only — a segment placed at 0.64
# is 1.92 m long and 0.70 m across, and nothing is squashed on one axis.
SEG_NOMINAL = 3.00
SCALES = [1.00, 0.94, 0.88, 0.82, 0.76, 0.70, 0.64]
# Direction of each segment, degrees from straight up toward aft (+Y).
THETA = [15.0, -20.0, -50.0, -75.0, -95.0, -115.0, -135.0]
# Never the same variation twice in a row.
SEG_KIND = ["Root", "Heavy", "Vented", "Patched", "Heavy", "Vented", "Slim"]
CLAW_THETA = -150.0
# Bigger than the tail tip it hangs off, the way a scorpion's chela is bigger
# than its metasoma. At parity the claw disappears into the taper.
CLAW_SCALE = 0.78
JAW_OPEN = 20.0        # degrees, so the claw reads as a claw at rest

# Magazines stand ON the deck, not sunk into the hull. Slung at hull mid-height
# they end up buried in the chassis with only their funnels showing, which reads
# as two disconnected hoppers rather than as a cargo system. Feet land on the
# deck at z 8.67; y is forward of the portal legs so the cradles clear them; x
# is outboard of the hand-placed Cube.
DRUM = dict(x=3.90, y=1.20, z=10.70)

SOURCES = {}
PLACED = {}


# ---------------------------------------------------------------------------
# Component loading — same contract as desert_crawler.py
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
        raise SystemExit("%s has no mesh containing %r" % (coll_name, contains))
    if len(meshes) != 1:
        raise SystemExit("%s holds %d meshes; name one" % (coll_name, len(meshes)))
    return next(iter(meshes.values()))


def place(name, data, coll, matrix):
    if name in bpy.data.objects:
        raise SystemExit("Name collision with existing object: %s" % name)
    obj = bpy.data.objects.new(name, data)
    obj.matrix_world = matrix
    coll.objects.link(obj)
    PLACED[obj.name] = matrix
    return obj


def dedupe_materials():
    folded = 0
    for mat in list(bpy.data.materials):
        base = mat.name[:-4]
        if (len(mat.name) > 4 and mat.name[-4] == '.' and mat.name[-3:].isdigit()
                and base in bpy.data.materials):
            mat.user_remap(bpy.data.materials[base])
            bpy.data.materials.remove(mat)
            folded += 1
    if folded:
        print("  folded %d duplicate material(s) back onto the palette" % folded)


# ---------------------------------------------------------------------------
# Frames
# ---------------------------------------------------------------------------

def seg_dir(theta_deg):
    a = math.radians(theta_deg)
    return Vector((0.0, math.sin(a), math.cos(a)))


def seg_frame(theta_deg, origin, scale):
    """Rotation taking the component's +Y onto the segment direction and its +Z
    onto the outside of the arch, times a uniform scale.

    Local +X lands on world -X. That is a proper rotation (determinant +1, so
    normals stay outward) and the segments are symmetric across x, so it is
    invisible — but taking +X to +X instead would put the carapace on the inside
    of the curve, which is the wrong side.
    """
    d = seg_dir(theta_deg)
    a = math.radians(theta_deg)
    n = Vector((0.0, math.cos(a), -math.sin(a)))
    rot = Matrix((Vector((-1.0, 0.0, 0.0)), d, n)).transposed().to_4x4()
    return (Matrix.Translation(origin) @ rot
            @ Matrix.Diagonal((scale, scale, scale, 1.0)))


def joints():
    """Walk the arch, returning the eight joint positions."""
    pts = [J0.copy()]
    for theta, s in zip(THETA, SCALES):
        pts.append(pts[-1] + seg_dir(theta) * (SEG_NOMINAL * s))
    return pts


# ---------------------------------------------------------------------------
# Geometry unique to this model
# ---------------------------------------------------------------------------

def build_portal(coll):
    """Legs, crossbeam and yaw turret — the only wholly new geometry here."""
    p = Part(PALETTE)
    for sx in (-1, 1):
        x = sx * PORTAL_X
        # Tapered leg, deck to crossbeam.
        p.loft([(DECK_Z + 0.02, [(-0.40, -0.42), (0.40, -0.42),
                                 (0.40, 0.42), (-0.40, 0.42)]),
                (DECK_Z + 1.10, [(-0.34, -0.36), (0.34, -0.36),
                                 (0.34, 0.36), (-0.34, 0.36)]),
                (BEAM_Z - 0.20, [(-0.30, -0.32), (0.30, -0.32),
                                 (0.30, 0.32), (-0.30, 0.32)])],
               axis='Z', mat=HULL, cap=True)
        for s in (-1, 1):
            p.box((x, PORTAL_Y + s * 0.36, DECK_Z + 0.02), (0.96, 0.30, 0.16),
                  STEEL)
        # Base flange bolted to the deck, and fore/aft knee braces.
        p.box((x, PORTAL_Y, DECK_Z + 0.10), (1.24, 1.30, 0.20), STEEL)
        p.rivets((x - 0.50, PORTAL_Y - 0.56, DECK_Z + 0.21),
                 (x + 0.50, PORTAL_Y - 0.56, DECK_Z + 0.21), 5, 0.055, 0.045,
                 'Z', CHROME)
        p.rivets((x - 0.50, PORTAL_Y + 0.56, DECK_Z + 0.21),
                 (x + 0.50, PORTAL_Y + 0.56, DECK_Z + 0.21), 5, 0.055, 0.045,
                 'Z', CHROME)
        for s in (-1, 1):
            d = Vector((0.0, s * 1.55, 1.75))
            rot = Matrix.Rotation(math.atan2(d.y, d.z), 4, 'X')
            p.box((x, PORTAL_Y + s * 0.78, DECK_Z + 0.90),
                  (0.34, 0.26, d.length), OLIVE, rot=rot)
        # Cable and hydraulic runs climbing the leg.
        for dy, mat in ((-0.46, COPPER), (0.46, RUBBER)):
            p.cyl((x + sx * 0.32, PORTAL_Y + dy, DECK_Z + 1.30), 0.075,
                  BEAM_Z - DECK_Z - 1.0, 'Z', 8, mat)
        p.box((x + sx * 0.32, PORTAL_Y, DECK_Z + 1.90), (0.22, 1.20, 0.14),
              STEEL)

    # Crossbeam over the top, with a lattice web so it does not read as a slab.
    p.box((0, PORTAL_Y, BEAM_Z), (2 * PORTAL_X + 0.90, 0.94, 0.34), HULL)
    p.box((0, PORTAL_Y, BEAM_Z - 0.26), (2 * PORTAL_X + 0.60, 0.72, 0.20), OLIVE)
    for k in range(9):
        x = -PORTAL_X - 0.20 + (2 * PORTAL_X + 0.40) * k / 8.0
        p.box((x, PORTAL_Y, BEAM_Z + 0.22), (0.16, 1.02, 0.14), STEEL)
    for s in (-1, 1):
        p.seam((-PORTAL_X, PORTAL_Y + s * 0.50, BEAM_Z + 0.18),
               (PORTAL_X, PORTAL_Y + s * 0.50, BEAM_Z + 0.18),
               width=0.10, depth=0.08, axis='Z', mat=RED)

    # Slew ring and turret can.
    p.tube((0, PORTAL_Y, YAW_Z), 1.34, 0.22, 0.36, 'Z', 28, STEEL)
    p.tube((0, PORTAL_Y, YAW_Z + 0.16), 1.10, 0.14, 0.30, 'Z', 24, CHROME)
    for k in range(16):
        a = math.radians(k * 22.5)
        p.cyl((1.22 * math.cos(a), PORTAL_Y + 1.22 * math.sin(a), YAW_Z + 0.20),
              0.06, 0.14, 'Z', 6, CHROME)
    # Slew drive hanging off the ring.
    p.cyl((0.0, PORTAL_Y - 1.42, YAW_Z - 0.10), 0.30, 0.72, 'Z', 14, DARK)
    p.cyl((0.0, PORTAL_Y - 1.42, YAW_Z + 0.34), 0.13, 0.30, 'Z', 10, CHROME)
    p.box((0.0, PORTAL_Y - 1.10, YAW_Z - 0.30), (0.80, 0.50, 0.40), GREEN)
    p.bevel(width=0.016, segments=2)
    return p.finish("Mesh_Tail_Portal", coll)


def build_turret(coll):
    """The yawing can the tail grows out of. Origin on the yaw axis so its bone
    spins it cleanly."""
    p = Part(PALETTE)
    z0, z1 = YAW_Z + 0.18, J0.z
    p.loft([(z0, [(-1.06, -1.06), (1.06, -1.06), (1.06, 1.06), (-1.06, 1.06)]),
            (z0 + 0.30, [(-1.14, -1.14), (1.14, -1.14), (1.14, 1.14),
                         (-1.14, 1.14)]),
            (z1, [(-0.94, -0.98), (0.94, -0.98), (0.94, 0.98), (-0.94, 0.98)])],
           axis='Z', mat=HULL)
    # Trunnion cheeks the first segment pins into.
    for sx in (-1, 1):
        p.box((sx * 1.02, 0.0, z1 - 0.16), (0.20, 1.30, 0.72), STEEL)
        p.cyl((sx * 1.02, 0.0, z1), 0.34, 0.24, 'X', 14, STEEL)
        p.cyl((sx * 1.16, 0.0, z1), 0.14, 0.16, 'X', 10, CHROME)
    # Pitch ram driving segment one, anchored low on the can.
    for sx in (-1, 1):
        d = Vector((0.0, -1.30, z1 - z0 - 0.30))
        rot = Matrix.Rotation(math.atan2(d.y, d.z), 4, 'X')
        p.cyl((sx * 0.70, -0.44, z0 + 0.55), 0.19, 0.90, 'Z', 12, DARK, rot=rot)
        p.cyl((sx * 0.70, -0.72, z0 + 1.05), 0.10, 1.00, 'Z', 10, CHROME,
              rot=rot)
    # Housekeeping: vents, a lamp, and hazard paint on the aft face.
    for sy in (-1, 1):
        p.louvres((-0.70, sy * 1.10 - 0.06, z0 + 0.30),
                  (0.70, sy * 1.10 + 0.06, z0 + 0.90), 5, 'Y', OLIVE)
    p.cyl((0.0, 1.16, z1 - 0.30), 0.11, 0.14, 'Y', 10, BLACK)
    p.cyl((0.0, 1.24, z1 - 0.30), 0.08, 0.10, 'Y', 10, AMBER)
    for k in range(0, 5, 2):
        p.box((-0.80 + k * 0.40, 1.15, z0 + 1.15), (0.34, 0.10, 0.26), RED)
    p.bevel(width=0.015, segments=2)
    obj = p.finish("Mesh_Tail_Turret", coll, origin=(0.0, 0.0, YAW_Z))
    obj.location = (0.0, PORTAL_Y, YAW_Z)
    return obj


# ---------------------------------------------------------------------------
# Armature
# ---------------------------------------------------------------------------

def bone_matrix(arm, bone_name):
    bone = arm.data.bones[bone_name]
    return (arm.matrix_world @ bone.matrix_local
            @ Matrix.Translation(Vector((0, bone.length, 0))))


def attach(obj, arm, bone_name, world):
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = bone_matrix(arm, bone_name).inverted() @ world


def add_bones(arm, pts):
    """Add the tail and magazine bones to the existing rig, touching nothing
    that is already on it."""
    inv = arm.matrix_world.inverted()
    new = []

    bpy.context.view_layer.objects.active = arm
    arm.hide_set(False)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    if "Root" not in eb:
        raise SystemExit("CRAWLER_Rig has no Root bone — wrong file?")
    root = eb["Root"]

    def bone(name, head, tail, parent):
        if name in eb:
            raise SystemExit("Bone already exists: %s" % name)
        b = eb.new(name)
        b.head = inv @ Vector(head)
        b.tail = inv @ Vector(tail)
        b.roll = 0.0
        b.parent = parent
        b.use_connect = False
        new.append(name)
        return b

    yaw = bone("Tail_Yaw", (0, PORTAL_Y, YAW_Z), (0, PORTAL_Y, J0.z), root)
    parent = yaw
    for i in range(len(SCALES)):
        parent = bone("Tail_%02d" % (i + 1), pts[i], pts[i + 1], parent)
    seg_last = parent

    # Wrist, then the two jaw hinges. The jaw bones point along +X so that
    # rotating about their own Y opens the claw about world X.
    claw_m = seg_frame(CLAW_THETA, pts[-1], CLAW_SCALE)
    wrist_end = claw_m @ Vector((0.0, 1.85, 0.0))
    wrist = bone("Claw_Wrist", pts[-1], wrist_end, seg_last)
    for label, sz in (("Upper", 1.0), ("Lower", -1.0)):
        hinge = claw_m @ Vector((0.0, 1.85, sz * 0.95))
        bone("Claw_Jaw%s" % label, hinge, hinge + Vector((0.90, 0, 0)), wrist)

    # Magazine bones: a spin axis and a gate hinge per side, both along X.
    for label, sx in (("P", 1.0), ("N", -1.0)):
        axis = Vector((sx * DRUM["x"], DRUM["y"], DRUM["z"]))
        bone("Drum_Spin%s" % label, axis, axis + Vector((0.90, 0, 0)), root)
        hinge = drum_frame(sx) @ Vector((0.0, -1.68, 0.10))
        bone("Drum_Gate%s" % label, hinge, hinge + Vector((0.90, 0, 0)), root)

    bpy.ops.object.mode_set(mode='OBJECT')
    print("  added %d bones: %s" % (len(new), ", ".join(new)))
    return new


# ---------------------------------------------------------------------------
# Magazines
# ---------------------------------------------------------------------------

def drum_frame(sx):
    """The starboard unit is turned end-for-end so its drive motor and chain
    case face outboard. Left as-is they would reach 1.98 m inboard and foul the
    hand-placed Cube; mirrored, the widest inboard part is 1.28 m and clears it.
    A 180 degree turn about Z is a rotation, not a mirror, so normals hold."""
    m = Matrix.Translation(Vector((sx * DRUM["x"], DRUM["y"], DRUM["z"])))
    if sx > 0:
        m = m @ Euler((0.0, 0.0, math.pi), 'XYZ').to_matrix().to_4x4()
    return m


# ---------------------------------------------------------------------------

def main():
    if not os.path.exists(TARGET):
        raise SystemExit("No such file: %s" % TARGET)
    bpy.ops.wm.open_mainfile(filepath=TARGET)
    bpy.context.view_layer.update()

    arm = bpy.data.objects.get("CRAWLER_Rig")
    if arm is None or arm.type != 'ARMATURE':
        raise SystemExit("CRAWLER_Rig missing — refusing to touch this file.")
    if len(arm.data.bones) != 31:
        raise SystemExit("Expected 31 bones on CRAWLER_Rig, found %d"
                         % len(arm.data.bones))

    # Snapshot every pre-existing object so the verify pass can prove that
    # nothing moved. Names and world matrices both.
    before = {o.name: o.matrix_world.copy() for o in bpy.data.objects}
    print("Opened %s — %d objects before" % (os.path.basename(TARGET), len(before)))

    global PALETTE
    PALETTE = link_materials(MATS)

    load_component("mechanical/tail_segment.blend",
                   ["Coll_TailSeg_%s" % k for k in
                    ("Root", "Heavy", "Vented", "Patched", "Slim")])
    load_component("mechanical/claw_chela.blend", ["Coll_Chela_Heavy"])
    load_component("mechanical/drum_magazine.blend", ["Coll_DrumMag_Nine"])

    tail_coll = bpy.data.collections.new("Crawler_Tail")
    cargo_coll = bpy.data.collections.new("Crawler_Cargo")
    bpy.context.scene.collection.children.link(tail_coll)
    bpy.context.scene.collection.children.link(cargo_coll)

    pts = joints()
    print("  arch apex z=%.2f (%.2f m above ground), tip at y=%.2f z=%.2f"
          % (max(p.z for p in pts), max(p.z for p in pts) + 4.31,
             pts[-1].y, pts[-1].z))

    portal = build_portal(tail_coll)
    turret = build_turret(tail_coll)

    # --- tail segments ---------------------------------------------------
    for i, (kind, s, theta) in enumerate(zip(SEG_KIND, SCALES, THETA)):
        place("Mesh_Tail_Seg%02d" % (i + 1),
              mesh_of("Coll_TailSeg_%s" % kind),
              tail_coll, seg_frame(theta, pts[i], s))

    # --- claw ------------------------------------------------------------
    claw_m = seg_frame(CLAW_THETA, pts[-1], CLAW_SCALE)
    place("Mesh_Tail_ClawPalm", mesh_of("Coll_Chela_Heavy", "Palm"),
          tail_coll, claw_m)
    for label, sz in (("Upper", 1.0), ("Lower", -1.0)):
        hinge = Matrix.Translation(Vector((0.0, 1.85, sz * 0.95)))
        open_rot = Matrix.Rotation(math.radians(sz * JAW_OPEN), 4, 'X')
        place("Mesh_Tail_ClawJaw%s" % label,
              mesh_of("Coll_Chela_Heavy", "Jaw%s" % label),
              tail_coll, claw_m @ hinge @ open_rot)

    # --- magazines -------------------------------------------------------
    for label, sx in (("P", 1.0), ("N", -1.0)):
        m = drum_frame(sx)
        for part in ("Cradle", "Drum", "Hopper"):
            place("Mesh_Drum_%s%s" % (part, label),
                  mesh_of("Coll_DrumMag_Nine", part), cargo_coll, m)
        gate = Matrix.Translation(Vector((0.0, -1.68, 0.10)))
        place("Mesh_Drum_Gate%s" % label,
              mesh_of("Coll_DrumMag_Nine", "Gate"), cargo_coll, m @ gate)

    # --- rig -------------------------------------------------------------
    add_bones(arm, pts)

    attach(portal, arm, "Root", Matrix.Translation(Vector((0, 0, 0))))
    attach(turret, arm, "Tail_Yaw",
           Matrix.Translation(Vector((0.0, PORTAL_Y, YAW_Z))))
    for i in range(len(SCALES)):
        name = "Mesh_Tail_Seg%02d" % (i + 1)
        attach(bpy.data.objects[name], arm, "Tail_%02d" % (i + 1), PLACED[name])
    attach(bpy.data.objects["Mesh_Tail_ClawPalm"], arm, "Claw_Wrist",
           PLACED["Mesh_Tail_ClawPalm"])
    for label in ("Upper", "Lower"):
        n = "Mesh_Tail_ClawJaw%s" % label
        attach(bpy.data.objects[n], arm, "Claw_Jaw%s" % label, PLACED[n])
    for label in ("P", "N"):
        for part, bone in (("Cradle", "Root"), ("Hopper", "Root"),
                           ("Drum", "Drum_Spin%s" % label),
                           ("Gate", "Drum_Gate%s" % label)):
            n = "Mesh_Drum_%s%s" % (part, label)
            attach(bpy.data.objects[n], arm, bone, PLACED[n])

    # --- the two duplicate armatures ------------------------------------
    # Authorised explicitly. Their one child each is re-parented onto the
    # matching bone of the real rig at its exact current world transform first,
    # so deleting the duplicates loses nothing and moves nothing.
    for dup_name in ("CRAWLER_Rig.001", "CRAWLER_Rig.002"):
        dup = bpy.data.objects.get(dup_name)
        if dup is None:
            print("  %s not present, skipping" % dup_name)
            continue
        for child in [o for o in bpy.data.objects if o.parent == dup]:
            world = before[child.name]
            bone = child.parent_bone
            if bone not in arm.data.bones:
                raise SystemExit("%s hangs off bone %r which the real rig lacks"
                                 % (child.name, bone))
            attach(child, arm, bone, world)
            print("  re-parented %s onto CRAWLER_Rig[%s]" % (child.name, bone))
        data = dup.data
        bpy.data.objects.remove(dup, do_unlink=True)
        if data.users == 0:
            bpy.data.armatures.remove(data)
        print("  removed %s" % dup_name)

    dedupe_materials()
    bpy.context.view_layer.update()

    # --- verify -----------------------------------------------------------
    print("\nVerification")
    removed = {"CRAWLER_Rig.001", "CRAWLER_Rig.002"}
    moved, missing = [], []
    for name, m0 in before.items():
        if name in removed:
            continue
        obj = bpy.data.objects.get(name)
        if obj is None:
            missing.append(name)
            continue
        delta = max(abs(obj.matrix_world[r][c] - m0[r][c])
                    for r in range(4) for c in range(4))
        if delta > 1e-5:
            moved.append((name, delta))
    print("  pre-existing objects still present: %d / %d"
          % (len(before) - len(missing) - len(removed), len(before) - len(removed)))
    if missing:
        raise SystemExit("LOST pre-existing objects: %s" % missing)
    if moved:
        raise SystemExit("MOVED pre-existing objects: %s" % moved)
    print("  none moved, none lost")

    added = sorted(set(bpy.data.objects.keys()) - set(before.keys()))
    print("  added %d objects" % len(added))
    tris = 0
    for name in added:
        o = bpy.data.objects[name]
        if o.type == 'MESH':
            tris += sum(len(p.vertices) - 2 for p in o.data.polygons)
    print("  added %d triangles" % tris)

    for o in bpy.data.objects:
        if len(o.name) > 4 and o.name[-4] == '.' and o.name[-3:].isdigit() \
                and o.name not in before:
            raise SystemExit("Auto-suffixed new object: %s" % o.name)

    bpy.ops.wm.save_as_mainfile(filepath=TARGET)
    print("\nWrote %s" % TARGET)


main()
