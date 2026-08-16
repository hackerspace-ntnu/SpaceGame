"""Replaces the crawler's claw with a scaled-up, five-joint ostrich neck.

**Opens a hand-edited file and saves over it.** Additive except for the three
claw meshes and three claw bones it is explicitly asked to remove, and the tail
bones it re-fits (see below). Everything else is snapshotted on open and checked
against that snapshot before saving.

Three jobs
----------
1. **Re-fit `Tail_Yaw` and `Tail_01..07`.** The tail meshes were transformed by
   hand (uniform scale 1.315, about +34 degrees around X) but the bones were left
   where `desert_crawler_tail.py` first put them, so pivots no longer sit on
   joints. Each bone is rebuilt from its own segment's current matrix: head at
   the mesh origin, tail at the mesh's nominal far end. Moving a bone drags its
   bone-parented children, so every such mesh is re-attached afterwards at the
   world matrix it had on open — the geometry does not move, only the pivots
   underneath it.

2. **Delete the claw**, meshes and bones.

3. **Grow a neck from the tail tip.** Five vertebrae and a head, taken from
   `components/mechanical/neck_column.blend`. Those parts are baked in bone
   space with their origin on the bone's *tail*, so each one is placed at the
   frame of the bone it belongs to and the ostrich's own half-a-bone-back offset
   makes consecutive vertebrae overlap rather than butt.

The tail tip is read from `Mesh_Tail_Seg07`'s current matrix rather than
recomputed, so wherever the tail has been moved to, the neck follows.

    blender --background --python desert_crawler_neck.py [-- --target <copy>]
"""

import math
import os
import sys

import bpy
from mathutils import Euler, Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, LIB)

COMPONENTS = os.path.join(LIB, "components")


def target_path():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--target" in argv:
        return os.path.abspath(argv[argv.index("--target") + 1])
    return os.path.join(LIB, "models", "vehicles", "desert_crawler.blend")


TARGET = target_path()

SEG_NOMINAL = 3.00      # authored length of one tail segment, before scale
N_SEG = 7

# Neck shape. Theta is degrees from straight up toward aft (+Y), the same
# parametrisation the tail arch uses: -90 is straight forward, -180 straight
# down. The tail tip already points forward and 11 degrees down, so the neck
# droops gradually from there and the head levels off to look ahead.
NECK_THETA = [-105.0, -115.0, -128.0, -142.0, -155.0]
NECK_SCALE = [2.05, 1.92, 1.80, 1.67, 1.55]
HEAD_THETA = -150.0
HEAD_SCALE = 1.55
# No two adjacent alike, and roughly tapering.
NECK_KIND = ["Sensor", "Large", "Mid", "Slim", "Mid"]

OSTRICH_BONE = 0.6191   # source neck bone length, so scale -> real length
OSTRICH_HEAD = 1.2166

# Rotation about the chain axis. The ostrich's bone-space +Z points at the
# throat rather than the nape, so this is the knob that decides which way up the
# skull sits; set from the render, not from first principles.
NECK_ROLL = 180.0

# Head bone frame -> jaw bone frame, measured off the ostrich rig. Applied after
# the head's scale so the offset scales with it.
JAW_REL = (Matrix.Translation(Vector((0.0, 1.1140, 0.4606)))
           @ Euler((math.radians(-10.94), math.radians(178.55),
                    math.radians(-0.28)), 'XYZ').to_matrix().to_4x4())

CLAW_MESHES = ["Mesh_Tail_ClawPalm", "Mesh_Tail_ClawJawUpper",
               "Mesh_Tail_ClawJawLower"]
CLAW_BONES = ["Claw_JawUpper", "Claw_JawLower", "Claw_Wrist"]

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


def mesh_of(coll_name, contains=None):
    meshes = SOURCES[coll_name]
    if contains is not None:
        for n, d in meshes.items():
            if contains in n:
                return d
        raise SystemExit("%s has no mesh containing %r" % (coll_name, contains))
    if len(meshes) != 1:
        raise SystemExit("%s holds %d meshes; name one" % (coll_name, len(meshes)))
    return next(iter(meshes.values()))


def place(name, data, coll, matrix):
    if name in bpy.data.objects:
        raise SystemExit("Name collision: %s" % name)
    obj = bpy.data.objects.new(name, data)
    obj.matrix_world = matrix
    coll.objects.link(obj)
    PLACED[name] = matrix
    return obj


def bone_matrix(arm, bone_name):
    bone = arm.data.bones[bone_name]
    return (arm.matrix_world @ bone.matrix_local
            @ Matrix.Translation(Vector((0.0, bone.length, 0.0))))


def attach(obj, arm, bone_name, world):
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    obj.matrix_parent_inverse = Matrix.Identity(4)
    obj.matrix_basis = bone_matrix(arm, bone_name).inverted() @ world


def chain_frame(theta_deg, origin, scale):
    """Frame with +Y along the chain and +Z on the outside of the curve.

    Local +X lands on world -X: that keeps the determinant at +1 so normals
    stay outward, and the parts are laterally symmetric so it does not show.
    """
    a = math.radians(theta_deg)
    d = Vector((0.0, math.sin(a), math.cos(a)))
    n = Vector((0.0, math.cos(a), -math.sin(a)))
    rot = Matrix((Vector((-1.0, 0.0, 0.0)), d, n)).transposed().to_4x4()
    roll = Matrix.Rotation(math.radians(NECK_ROLL), 4, 'Y')
    return (Matrix.Translation(origin) @ rot @ roll
            @ Matrix.Diagonal((scale, scale, scale, 1.0)))


def main():
    if not os.path.exists(TARGET):
        raise SystemExit("No such file: %s" % TARGET)
    bpy.ops.wm.open_mainfile(filepath=TARGET)
    bpy.context.view_layer.update()

    arm = bpy.data.objects.get("CRAWLER_Rig")
    if arm is None or arm.type != 'ARMATURE':
        raise SystemExit("CRAWLER_Rig missing — refusing to touch this file.")
    for n in CLAW_MESHES:
        if n not in bpy.data.objects:
            raise SystemExit("Expected %s; has the claw already been replaced?" % n)
    for n in CLAW_BONES:
        if n not in arm.data.bones:
            raise SystemExit("Expected bone %s on CRAWLER_Rig" % n)

    before = {o.name: o.matrix_world.copy() for o in bpy.data.objects}
    bone_kids = {o.name: o.parent_bone for o in bpy.data.objects
                 if o.parent == arm and o.parent_type == 'BONE'}
    print("Opened %s — %d objects, %d bones"
          % (os.path.basename(TARGET), len(before), len(arm.data.bones)))

    # ---- where the tail actually is now --------------------------------
    segs = []
    for i in range(1, N_SEG + 1):
        o = bpy.data.objects["Mesh_Tail_Seg%02d" % i]
        m = o.matrix_world.copy()
        s = m.to_scale().x
        segs.append((m, s))
    tip_m, tip_s = segs[-1]
    tip = tip_m @ Vector((0.0, SEG_NOMINAL, 0.0))
    print("  tail tip at (%.3f, %.3f, %.3f), last segment scale %.3f"
          % (tip.x, tip.y, tip.z, tip_s))

    load_component("mechanical/neck_column.blend",
                   ["Coll_NeckColumn_VertSensor", "Coll_NeckColumn_VertLarge",
                    "Coll_NeckColumn_VertMid", "Coll_NeckColumn_VertSlim",
                    "Coll_NeckColumn_Joint", "Coll_NeckColumn_Head",
                    "Coll_NeckColumn_Jaw"])

    neck_coll = bpy.data.collections.get("Crawler_Neck")
    if neck_coll is None:
        neck_coll = bpy.data.collections.new("Crawler_Neck")
        bpy.context.scene.collection.children.link(neck_coll)

    # ---- walk the neck ---------------------------------------------------
    joints = [tip]
    for theta, s in zip(NECK_THETA, NECK_SCALE):
        a = math.radians(theta)
        d = Vector((0.0, math.sin(a), math.cos(a)))
        joints.append(joints[-1] + d * (OSTRICH_BONE * s))
    a = math.radians(HEAD_THETA)
    head_end = joints[-1] + Vector((0.0, math.sin(a), math.cos(a))) * (
        OSTRICH_HEAD * HEAD_SCALE)
    print("  neck ends (%.2f, %.2f, %.2f), head tip (%.2f, %.2f, %.2f)"
          % (*joints[-1], *head_end))

    # ---- claw out --------------------------------------------------------
    # Before the bones go, or Blender warns about every dangling parent.
    for name in CLAW_MESHES:
        obj = bpy.data.objects[name]
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if data.users == 0:
            bpy.data.meshes.remove(data)
    print("  removed %s" % CLAW_MESHES)

    # ---- rig -------------------------------------------------------------
    inv = arm.matrix_world.inverted()
    bpy.context.view_layer.objects.active = arm
    arm.hide_set(False)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones

    # 1. re-fit the tail bones onto the moved meshes
    for i, (m, s) in enumerate(segs, start=1):
        b = eb["Tail_%02d" % i]
        b.head = inv @ m.translation
        b.tail = inv @ (m @ Vector((0.0, SEG_NOMINAL, 0.0)))
        b.roll = 0.0
    eb["Tail_Yaw"].tail = inv @ segs[0][0].translation
    print("  re-fitted Tail_Yaw + Tail_01..%02d onto the moved meshes" % N_SEG)

    # 2. drop the claw bones (children first)
    for name in CLAW_BONES:
        eb.remove(eb[name])

    # 3. grow the neck
    parent = eb["Tail_%02d" % N_SEG]
    for i in range(len(NECK_THETA)):
        b = eb.new("Neck_%02d" % (i + 1))
        b.head = inv @ joints[i]
        b.tail = inv @ joints[i + 1]
        b.roll = 0.0
        b.parent = parent
        b.use_connect = False
        parent = b
    hb = eb.new("Neck_Head")
    hb.head = inv @ joints[-1]
    hb.tail = inv @ head_end
    hb.roll = 0.0
    hb.parent = parent
    jb = eb.new("Neck_Jaw")
    jaw_m = chain_frame(HEAD_THETA, head_end, HEAD_SCALE) @ JAW_REL
    jb.head = inv @ jaw_m.translation
    jb.tail = inv @ (jaw_m @ Vector((0.0, 1.0908 * HEAD_SCALE, 0.0)))
    jb.roll = 0.0
    jb.parent = hb
    bpy.ops.object.mode_set(mode='OBJECT')
    print("  added Neck_01..05, Neck_Head, Neck_Jaw; removed %s" % CLAW_BONES)

    # 4. every bone-parented mesh goes back exactly where it was
    for name, bone in bone_kids.items():
        if name in CLAW_MESHES:
            continue
        attach(bpy.data.objects[name], arm, bone, before[name])

    # ---- neck geometry ---------------------------------------------------
    for i, (theta, s, kind) in enumerate(zip(NECK_THETA, NECK_SCALE, NECK_KIND),
                                         start=1):
        f = chain_frame(theta, joints[i], s)
        place("Mesh_Neck_Vert%02d" % i,
              mesh_of("Coll_NeckColumn_Vert%s" % kind), neck_coll, f)
        place("Mesh_Neck_Collar%02d" % i,
              mesh_of("Coll_NeckColumn_Joint"), neck_coll, f)

    head_f = chain_frame(HEAD_THETA, head_end, HEAD_SCALE)
    for part in ("Cranium", "Beak", "EyeL", "EyeR", "Plate"):
        place("Mesh_Neck_Head%s" % part,
              mesh_of("Coll_NeckColumn_Head", part), neck_coll, head_f)
    place("Mesh_Neck_Jaw", mesh_of("Coll_NeckColumn_Jaw"), neck_coll,
          head_f @ JAW_REL)

    for i in range(1, len(NECK_THETA) + 1):
        for stem in ("Vert", "Collar"):
            n = "Mesh_Neck_%s%02d" % (stem, i)
            attach(bpy.data.objects[n], arm, "Neck_%02d" % i, PLACED[n])
    for part in ("Cranium", "Beak", "EyeL", "EyeR", "Plate"):
        n = "Mesh_Neck_Head%s" % part
        attach(bpy.data.objects[n], arm, "Neck_Head", PLACED[n])
    attach(bpy.data.objects["Mesh_Neck_Jaw"], arm, "Neck_Jaw",
           PLACED["Mesh_Neck_Jaw"])

    # ---- materials + verify ---------------------------------------------
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
    local = [m.name for m in bpy.data.materials if m.library is None]
    if local:
        raise SystemExit("Non-palette materials present: %s" % local)

    bpy.context.view_layer.update()
    print("\nVerification")
    moved, missing = [], []
    for name, m0 in before.items():
        if name in CLAW_MESHES:
            continue
        obj = bpy.data.objects.get(name)
        if obj is None:
            missing.append(name)
            continue
        delta = max(abs(obj.matrix_world[r][c] - m0[r][c])
                    for r in range(4) for c in range(4))
        if delta > 1e-4:
            moved.append((name, round(delta, 5)))
    if missing:
        raise SystemExit("LOST objects: %s" % missing)
    if moved:
        raise SystemExit("MOVED objects: %s" % moved)
    print("  %d pre-existing objects still exactly where they were"
          % (len(before) - len(CLAW_MESHES)))
    added = sorted(set(bpy.data.objects.keys()) - set(before))
    tris = sum(sum(len(p.vertices) - 2 for p in bpy.data.objects[n].data.polygons)
               for n in added if bpy.data.objects[n].type == 'MESH')
    print("  added %d objects, %d triangles" % (len(added), tris))
    print("  bones now %d" % len(arm.data.bones))

    bpy.ops.wm.save_as_mainfile(filepath=TARGET)
    print("\nWrote %s" % TARGET)


main()
