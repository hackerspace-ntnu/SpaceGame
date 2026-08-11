"""Rigs the hand-built collector bucket and gives it a lift lever.

The bucket (`Cube.001` body + `Cube.002` rim) was modelled by hand and left
unparented, so it neither moves with the machine nor articulates. This puts it
on the rig and builds the minimum mechanism that makes it work as a front
loader:

    Root -> Collector_Lift -> Collector_Bucket

`Collector_Lift` raises and lowers the whole assembly on two arms pinned to the
deck. `Collector_Bucket` is the tip axis — roll it forward and the bucket swings
down to dump on whatever is in front.

Two rams per side drive it, reusing `Coll_CutterDrum_Ram`: the lift ram runs
from a deck anchor to the arm, the tilt ram from the arm to the bucket's upper
rear. Each is a barrel and a rod on *different* bones, so the pair extends as
the mechanism moves instead of stretching.

The arms sit at x = +/-4.60, just outboard of the bucket (which reaches 4.37)
and clear of the hand-placed `Cube` on the deck centreline (which reaches 2.37).

One thing is changed on the bucket itself: its scale is applied. Both halves
carry unapplied non-uniform object scale (about 4.4, 2.8, 4.4), and bone-
parenting something like that computes a basis matrix that can shear the mesh.
Baking the scale into the vertices leaves the bucket looking identical, makes
the transform rigid, and lets the parenting use an identity parent-inverse the
way the rest of this rig does — which matters because FBX drops that field.

Object names are left exactly as they are. `Cube.001` and `Cube.002` are poor
names, but they are the author's.

    blender --background --python desert_crawler_collector.py [-- --target <copy>]
"""

import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)

COMPONENTS = os.path.join(LIB, "components")


def target_path():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--target" in argv:
        return os.path.abspath(argv[argv.index("--target") + 1])
    return os.path.join(LIB, "models", "vehicles", "desert_crawler.blend")


TARGET = target_path()

BUCKET = ["Cube.001", "Cube.002"]
ARM_X = 4.60
LIFT_PIVOT = Vector((0.0, -2.60, 9.45))   # on the deck
BUCKET_PIVOT = Vector((0.0, -5.80, 9.00))  # bucket's rear, where the arm pins
BUCKET_TIP = Vector((0.0, -11.00, 9.00))   # bone tail, out along the bucket
ARM_NOMINAL = 3.20

LIFT_ANCHOR = Vector((0.0, -1.10, 9.05))   # lift ram, hull end
LIFT_FRACTION = 0.62                       # where it meets the arm
TILT_ANCHOR_FRACTION = 0.18                # tilt ram, arm end
TILT_ANCHOR_LIFT = 0.38                    # raised onto the arm's gusset
TILT_ON_BUCKET = Vector((0.0, -6.40, 12.40))

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


def corners(obj):
    """World-space bounding-box corners — a transform-independent fingerprint of
    where an object's geometry actually is."""
    if obj.type != 'MESH':
        return [obj.matrix_world.translation.copy()]
    return [obj.matrix_world @ Vector(c) for c in obj.bound_box]


def frame(direction, origin, scale=1.0):
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
    for n in BUCKET:
        if n not in bpy.data.objects:
            raise SystemExit("Expected bucket object %s" % n)

    # Snapshot world-space corners, not matrices. Applying the bucket's scale
    # legitimately rewrites its matrix_world while leaving the geometry exactly
    # where it was, so a matrix comparison would report a 3.4 m "move" that did
    # not happen. Corners measure what actually matters.
    before = {o.name: corners(o) for o in bpy.data.objects}
    print("Opened %s — %d objects, %d bones"
          % (os.path.basename(TARGET), len(before), len(arm.data.bones)))

    # ---- make the bucket rigid ------------------------------------------
    for n in BUCKET:
        obj = bpy.data.objects[n]
        if obj.data.users != 1:
            raise SystemExit("%s shares its mesh with %d objects; applying "
                             "scale would alter them too."
                             % (n, obj.data.users))
        s = obj.scale.copy()
        if max(abs(s.x - 1), abs(s.y - 1), abs(s.z - 1)) > 1e-6:
            obj.data.transform(Matrix.Diagonal(s).to_4x4())
            obj.scale = (1.0, 1.0, 1.0)
            print("  applied scale (%.3f, %.3f, %.3f) on %s" % (*s, n))

    load_component("mechanical/collector_lever.blend",
                   ["Coll_CollectorLever_Arm", "Coll_CollectorLever_Mount"])
    load_component("mechanical/cutter_drum.blend", ["Coll_CutterDrum_Ram"])

    coll = bpy.data.collections.get("Crawler_Collector")
    if coll is None:
        coll = bpy.data.collections.new("Crawler_Collector")
        bpy.context.scene.collection.children.link(coll)

    # ---- bones -----------------------------------------------------------
    inv = arm.matrix_world.inverted()
    bpy.context.view_layer.objects.active = arm
    arm.hide_set(False)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    for n in ("Collector_Lift", "Collector_Bucket"):
        if n in eb:
            raise SystemExit("Bone %s already exists" % n)
    lift = eb.new("Collector_Lift")
    lift.head = inv @ LIFT_PIVOT
    lift.tail = inv @ BUCKET_PIVOT
    lift.roll = 0.0
    lift.parent = eb["Root"]
    lift.use_connect = False
    tip = eb.new("Collector_Bucket")
    tip.head = inv @ BUCKET_PIVOT
    tip.tail = inv @ BUCKET_TIP
    tip.roll = 0.0
    tip.parent = lift
    tip.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    print("  added Collector_Lift and Collector_Bucket")

    # ---- geometry --------------------------------------------------------
    arm_vec = BUCKET_PIVOT - LIFT_PIVOT
    arm_scale = arm_vec.length / ARM_NOMINAL
    d = arm_vec.normalized()
    for label, sx in (("P", 1.0), ("N", -1.0)):
        off = Vector((sx * ARM_X, 0.0, 0.0))
        place("Mesh_Coll_Arm%s" % label,
              mesh_of("Coll_CollectorLever_Arm"), coll,
              frame(arm_vec, LIFT_PIVOT + off, arm_scale))
        place("Mesh_Coll_Mount%s" % label,
              mesh_of("Coll_CollectorLever_Mount"), coll,
              frame(arm_vec, LIFT_PIVOT + off))

        lift_end = LIFT_PIVOT + off + d * (arm_vec.length * LIFT_FRACTION)
        lift_vec = lift_end - (LIFT_ANCHOR + off)
        place("Mesh_Coll_LiftBarrel%s" % label,
              mesh_of("Coll_CutterDrum_Ram", "Barrel"), coll,
              frame(lift_vec, LIFT_ANCHOR + off))
        place("Mesh_Coll_LiftRod%s" % label,
              mesh_of("Coll_CutterDrum_Ram", "Rod"), coll,
              frame(lift_vec, lift_end))

        tilt_anchor = (LIFT_PIVOT + off
                       + d * (arm_vec.length * TILT_ANCHOR_FRACTION)
                       + Vector((0.0, 0.0, TILT_ANCHOR_LIFT)))
        tilt_end = TILT_ON_BUCKET + off
        tilt_vec = tilt_end - tilt_anchor
        place("Mesh_Coll_TiltBarrel%s" % label,
              mesh_of("Coll_CutterDrum_Ram", "Barrel"), coll,
              frame(tilt_vec, tilt_anchor))
        place("Mesh_Coll_TiltRod%s" % label,
              mesh_of("Coll_CutterDrum_Ram", "Rod"), coll,
              frame(tilt_vec, tilt_end))

    # ---- parenting -------------------------------------------------------
    for n in BUCKET:
        attach(bpy.data.objects[n], arm, "Collector_Bucket",
               bpy.data.objects[n].matrix_world.copy())
    on_root = ["Mesh_Coll_MountP", "Mesh_Coll_MountN",
               "Mesh_Coll_LiftBarrelP", "Mesh_Coll_LiftBarrelN"]
    on_lift = ["Mesh_Coll_ArmP", "Mesh_Coll_ArmN",
               "Mesh_Coll_LiftRodP", "Mesh_Coll_LiftRodN",
               "Mesh_Coll_TiltBarrelP", "Mesh_Coll_TiltBarrelN"]
    on_tip = ["Mesh_Coll_TiltRodP", "Mesh_Coll_TiltRodN"]
    for n in on_root:
        attach(bpy.data.objects[n], arm, "Root", PLACED[n])
    for n in on_lift:
        attach(bpy.data.objects[n], arm, "Collector_Lift", PLACED[n])
    for n in on_tip:
        attach(bpy.data.objects[n], arm, "Collector_Bucket", PLACED[n])

    folded = 0
    for mat in list(bpy.data.materials):
        base = mat.name[:-4]
        if (len(mat.name) > 4 and mat.name[-4] == '.' and mat.name[-3:].isdigit()
                and base in bpy.data.materials):
            mat.user_remap(bpy.data.materials[base])
            bpy.data.materials.remove(mat)
            folded += 1
    if folded:
        print("  folded %d duplicate material(s)" % folded)

    # ---- verify ----------------------------------------------------------
    bpy.context.view_layer.update()
    print("\nVerification")
    worst = 0.0
    for name, c0 in before.items():
        obj = bpy.data.objects.get(name)
        if obj is None:
            raise SystemExit("LOST object %s" % name)
        c1 = corners(obj)
        delta = max((a - b).length for a, b in zip(c0, c1))
        worst = max(worst, delta)
        if delta > 1e-4:
            raise SystemExit("MOVED %s by %.5f m" % (name, delta))
    print("  %d pre-existing objects still exactly where they were "
          "(worst drift %.2e m)" % (len(before), worst))
    added = sorted(set(bpy.data.objects.keys()) - set(before))
    tris = sum(sum(len(p.vertices) - 2 for p in bpy.data.objects[n].data.polygons)
               for n in added if bpy.data.objects[n].type == 'MESH')
    print("  added %d objects, %d triangles" % (len(added), tris))
    print("  bones now %d" % len(arm.data.bones))

    bpy.ops.wm.save_as_mainfile(filepath=TARGET)
    print("\nWrote %s" % TARGET)


main()
