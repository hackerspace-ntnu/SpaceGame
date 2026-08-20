"""Removes the crawler's tail arm entirely and fits a digging head to the front.

The whole appendage goes: turret, seven tail segments, five neck vertebrae and
collars, head and jaw — 24 meshes — along with every bone that served it. The
four `Drum_*` bones from the deleted cargo magazines go too; they have had no
children since those meshes were removed by hand, and they belonged to the same
abandoned idea. That returns `CRAWLER_Rig` to its original 31 bones before the
two digger bones are added.

In its place, a transverse cutter drum on two arms under the prow.

Where it can go
---------------
Almost nowhere, as it turns out. The front legs sweep inboard to x = 2.09 and
occupy y −8.68…−3.86 down to z = −3.14, so the only clear route from the hull to
the ground is the centreline corridor |x| < 2. The arms run down that corridor
at x = ±1.55, and the drum — which has to be wide to be worth anything — sits at
y = −11.00, forward of everything the legs reach. It overhangs its own bearings
either side, which is what a through-axle drum does anyway.

The pivot sits at y = −7.90, just forward of `Mesh_Crawler_Gantry.003` (which
starts at −7.63) and just below the prow (which bottoms out at 5.64), so the
mount hangs under the nose without penetrating either.

Drum teeth reach z = −4.10 against a ground plane of −4.31: 0.21 m of clearance
at rest, so it reads as poised to cut and the boom pitches down into the sand.

    blender --background --python desert_crawler_digger.py [-- --target <copy>]
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)
from _buildlib import Part, link_materials  # noqa: E402

COMPONENTS = os.path.join(LIB, "components")


def target_path():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--target" in argv:
        return os.path.abspath(argv[argv.index("--target") + 1])
    return os.path.join(LIB, "models", "vehicles", "desert_crawler.blend")


TARGET = target_path()

MATS = ["Mat_Paint_Hull_Bleached", "Mat_Metal_Steel_Dark",
        "Mat_Metal_Steel_Worn", "Mat_Paint_Olive_Deep", "Mat_Metal_Rust_Heavy",
        "Mat_Paint_Warn_Red", "Mat_Metal_Chrome_Scuffed"]
HULL, DARK, STEEL, OLIVE, RUST, RED, CHROME = range(7)

PIVOT = Vector((0.0, -7.90, 5.20))
DRUM_AXIS = Vector((0.0, -11.00, -2.20))
ARM_X = 1.55
ARM_NOMINAL = 8.00
RAM_ANCHOR_Y, RAM_ANCHOR_Z = -6.60, 6.40
RAM_FRACTION = 0.40

DEAD_PREFIXES = ("Mesh_Tail_", "Mesh_Neck_")
DEAD_BONES = (["Tail_Yaw"] + ["Tail_%02d" % i for i in range(1, 8)]
              + ["Neck_%02d" % i for i in range(1, 6)]
              + ["Neck_Head", "Neck_Jaw"]
              + ["Drum_SpinP", "Drum_SpinN", "Drum_GateP", "Drum_GateN"])
DEAD_COLLECTIONS = ("Crawler_Tail", "Crawler_Neck", "Crawler_Cargo")

SOURCES = {}
PLACED = {}


def load_component(path, collections):
    full = os.path.join(COMPONENTS, path)
    if not os.path.exists(full):
        raise SystemExit("Missing component %s — build it first." % full)
    with bpy.data.libraries.load(full, link=False) as (src, dst):
        missing = [c for c in collections if c not in set(src.collections)]
        if missing:
            raise SystemExit("%s has no %s" % (path, missing))
        dst.collections = list(collections)
    for name in collections:
        coll = bpy.data.collections[name]
        SOURCES[name] = {o.name: o.data for o in coll.all_objects
                         if o.type == 'MESH'}
        for o in list(coll.all_objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(coll)


def mesh_of(coll, contains=None):
    meshes = SOURCES[coll]
    if contains is not None:
        for n, d in meshes.items():
            if contains in n:
                return d
        raise SystemExit("%s has no mesh containing %r" % (coll, contains))
    if len(meshes) != 1:
        raise SystemExit("%s holds %d meshes; name one" % (coll, len(meshes)))
    return next(iter(meshes.values()))


def place(name, data, coll, matrix):
    if name in bpy.data.objects:
        raise SystemExit("Name collision: %s" % name)
    obj = bpy.data.objects.new(name, data)
    obj.matrix_world = matrix
    coll.objects.link(obj)
    PLACED[name] = matrix
    return obj


def bone_matrix(arm, name):
    b = arm.data.bones[name]
    return (arm.matrix_world @ b.matrix_local
            @ Matrix.Translation(Vector((0.0, b.length, 0.0))))


def attach(obj, arm, bone_name, world):
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = bone_matrix(arm, bone_name).inverted() @ world


def frame(direction, origin, scale=1.0):
    """+Y along `direction`, +Z on the upper side, determinant +1."""
    d = direction.normalized()
    theta = math.atan2(d.y, d.z)
    n = Vector((0.0, math.cos(theta), -math.sin(theta)))
    rot = Matrix((Vector((-1.0, 0.0, 0.0)), d, n)).transposed().to_4x4()
    return (Matrix.Translation(origin) @ rot
            @ Matrix.Diagonal((scale, scale, scale, 1.0)))


def main():
    bpy.ops.wm.open_mainfile(filepath=TARGET)
    bpy.context.view_layer.update()
    arm = bpy.data.objects.get("CRAWLER_Rig")
    if arm is None:
        raise SystemExit("CRAWLER_Rig missing — refusing to touch this file.")

    before = {o.name: o.matrix_world.copy() for o in bpy.data.objects}
    bone_kids = {o.name: o.parent_bone for o in bpy.data.objects
                 if o.parent == arm and o.parent_type == 'BONE'}
    print("Opened %s — %d objects, %d bones"
          % (os.path.basename(TARGET), len(before), len(arm.data.bones)))

    # ---- the arm goes ----------------------------------------------------
    doomed = [o.name for o in bpy.data.objects if o.name.startswith(DEAD_PREFIXES)]
    for name in doomed:
        obj = bpy.data.objects[name]
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if data.users == 0:
            bpy.data.meshes.remove(data)
    print("  removed %d arm meshes" % len(doomed))

    global PALETTE
    PALETTE = link_materials(MATS)
    load_component("mechanical/cutter_drum.blend",
                   ["Coll_CutterDrum_Drum", "Coll_CutterDrum_Arm",
                    "Coll_CutterDrum_Hood", "Coll_CutterDrum_Ram"])

    dig = bpy.data.collections.get("Crawler_Digger")
    if dig is None:
        dig = bpy.data.collections.new("Crawler_Digger")
        bpy.context.scene.collection.children.link(dig)

    # ---- rig -------------------------------------------------------------
    inv = arm.matrix_world.inverted()
    bpy.context.view_layer.objects.active = arm
    arm.hide_set(False)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    gone = 0
    for name in DEAD_BONES:
        if name in eb:
            eb.remove(eb[name])
            gone += 1
    boom = eb.new("Dig_Boom")
    boom.head = inv @ PIVOT
    boom.tail = inv @ DRUM_AXIS
    boom.roll = 0.0
    boom.parent = eb["Root"]
    boom.use_connect = False
    spin = eb.new("Dig_Drum")
    spin.head = inv @ DRUM_AXIS
    spin.tail = inv @ (DRUM_AXIS + Vector((0.90, 0.0, 0.0)))
    spin.roll = 0.0
    spin.parent = boom
    spin.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    print("  removed %d bones, added Dig_Boom + Dig_Drum" % gone)

    for name, bone in bone_kids.items():
        if name in doomed:
            continue
        attach(bpy.data.objects[name], arm, bone, before[name])

    # ---- geometry --------------------------------------------------------
    boom_vec = DRUM_AXIS - PIVOT
    arm_scale = boom_vec.length / ARM_NOMINAL
    for label, sx in (("P", 1.0), ("N", -1.0)):
        origin = Vector((sx * ARM_X, PIVOT.y, PIVOT.z))
        place("Mesh_Dig_Arm%s" % label, mesh_of("Coll_CutterDrum_Arm"), dig,
              frame(boom_vec, origin, arm_scale))

    place("Mesh_Dig_Drum", mesh_of("Coll_CutterDrum_Drum"), dig,
          Matrix.Translation(DRUM_AXIS))
    place("Mesh_Dig_Hood", mesh_of("Coll_CutterDrum_Hood"), dig,
          Matrix.Translation(DRUM_AXIS))

    # Cross-brace tying the two arms together, the one piece of geometry that
    # is specific to this machine.
    p = Part(PALETTE)
    mid = PIVOT + boom_vec * 0.52
    p.box((0.0, mid.y, mid.z), (ARM_X * 2, 0.52, 0.34), OLIVE)
    p.box((0.0, PIVOT.y - 0.10, PIVOT.z), (ARM_X * 2 + 0.9, 0.62, 0.44), HULL)
    p.bevel(width=0.02, segments=1)
    brace = p.finish("Mesh_Dig_Brace", dig)
    PLACED["Mesh_Dig_Brace"] = Matrix.Identity(4)

    d_arm = boom_vec.normalized()
    for label, sx in (("P", 1.0), ("N", -1.0)):
        anchor = Vector((sx * ARM_X, RAM_ANCHOR_Y, RAM_ANCHOR_Z))
        attach_pt = (Vector((sx * ARM_X, PIVOT.y, PIVOT.z))
                     + d_arm * (boom_vec.length * RAM_FRACTION))
        ram_vec = attach_pt - anchor
        place("Mesh_Dig_RamBarrel%s" % label,
              mesh_of("Coll_CutterDrum_Ram", "Barrel"), dig,
              frame(ram_vec, anchor))
        place("Mesh_Dig_RamRod%s" % label,
              mesh_of("Coll_CutterDrum_Ram", "Rod"), dig,
              frame(ram_vec, attach_pt))

    on_boom = ["Mesh_Dig_ArmP", "Mesh_Dig_ArmN", "Mesh_Dig_Hood",
               "Mesh_Dig_Brace", "Mesh_Dig_RamRodP", "Mesh_Dig_RamRodN"]
    for n in on_boom:
        attach(bpy.data.objects[n], arm, "Dig_Boom", PLACED[n])
    attach(bpy.data.objects["Mesh_Dig_Drum"], arm, "Dig_Drum",
           PLACED["Mesh_Dig_Drum"])
    for label in ("P", "N"):
        n = "Mesh_Dig_RamBarrel%s" % label
        attach(bpy.data.objects[n], arm, "Root", PLACED[n])

    # ---- tidy ------------------------------------------------------------
    for name in DEAD_COLLECTIONS:
        c = bpy.data.collections.get(name)
        if c is not None and not c.all_objects:
            bpy.data.collections.remove(c)
            print("  removed empty collection %s" % name)

    folded = 0
    for mat in list(bpy.data.materials):
        base = mat.name[:-4]
        if (len(mat.name) > 4 and mat.name[-4] == '.' and mat.name[-3:].isdigit()
                and base in bpy.data.materials):
            mat.user_remap(bpy.data.materials[base])
            bpy.data.materials.remove(mat)
            folded += 1
    for mat in list(bpy.data.materials):
        if mat.users == 0 and mat.library is None:
            bpy.data.materials.remove(mat)
    local = [m.name for m in bpy.data.materials if m.library is None]
    if local:
        raise SystemExit("Non-palette materials present: %s" % local)

    # ---- verify ----------------------------------------------------------
    bpy.context.view_layer.update()
    print("\nVerification")
    for name, m0 in before.items():
        if name in doomed:
            continue
        obj = bpy.data.objects.get(name)
        if obj is None:
            raise SystemExit("LOST pre-existing object %s" % name)
        delta = max(abs(obj.matrix_world[r][c] - m0[r][c])
                    for r in range(4) for c in range(4))
        if delta > 1e-4:
            raise SystemExit("MOVED %s by %.5f" % (name, delta))
    print("  %d pre-existing objects untouched, %d removed as asked"
          % (len(before) - len(doomed), len(doomed)))
    added = sorted(set(bpy.data.objects.keys()) - set(before))
    tris = sum(sum(len(pl.vertices) - 2 for pl in bpy.data.objects[n].data.polygons)
               for n in added if bpy.data.objects[n].type == 'MESH')
    print("  added %d objects, %d triangles" % (len(added), tris))
    print("  bones now %d" % len(arm.data.bones))
    lo = min((o.matrix_world @ Vector(c)).z
             for n in added for o in [bpy.data.objects[n]]
             if o.type == 'MESH' for c in o.bound_box)
    print("  lowest point of the digger z = %.2f (ground is -4.31)" % lo)

    bpy.ops.wm.save_as_mainfile(filepath=TARGET)
    print("\nWrote %s" % TARGET)


main()
