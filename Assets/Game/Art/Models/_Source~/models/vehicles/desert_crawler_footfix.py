"""Un-does the shared-foot accident and rigs the hand-built scorpion claw.

The problem
-----------
All six feet pointed at one mesh datablock, `Mesh_Leg_P3_Foot`. Editing one foot
to add a hook for the claw therefore edited all six, and the machine ended up
walking on grabbers. The original geometry was never lost — a pristine copy
survives in the file as `Mesh_Leg_P3_Foot.001` (1,664 tris against the hooked
version's 1,736).

So this does not reconstruct anything. It re-points the six feet at the original
datablock and leaves the hooked one to the claw alone, where it becomes a
single-user mesh that can be edited freely without touching the legs again.
The two are renamed afterwards so the trap cannot spring silently a second time:
the hooked one becomes `Mesh_Claw_Grabber`, and the original takes back the name
`Mesh_Leg_P3_Foot` that the legs have always used.

The claw
--------
It is seven duplicated leg parts arcing over the machine, and every one of them
hangs off a *different* duplicate of the armature — `CRAWLER_Rig.003`, `.005`,
`.006`, `.007` — which is why it cannot be posed: there is no chain, just seven
independent objects on seven copies of the same rig.

A real seven-bone chain `Claw_01..06 -> Claw_Grab` is built along the arm and
each part re-parented onto its own bone, so the whole thing articulates from the
base. Order is taken from the parts' own positions, walking up the aft side,
over the top, and down to the grabber.

Three more parts (`Mesh_Leg_N1_Lower.002`, `Mesh_Leg_N1_Upper.001`,
`Mesh_Leg_P2_Foot.001`) are the replacements put back on the real legs; they are
also stranded on duplicate armatures and get moved to the matching bone of the
real rig. Then all eight duplicates go.

Nothing moves. Every object is re-attached at the world matrix it had on open.

    blender --background --python desert_crawler_footfix.py [-- --target <copy>]
"""

import os
import sys

import bpy
from mathutils import Matrix, Vector

LIB = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def target_path():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if "--target" in argv:
        return os.path.abspath(argv[argv.index("--target") + 1])
    return os.path.join(LIB, "models", "vehicles", "desert_crawler.blend")


TARGET = target_path()

HOOKED = "Mesh_Leg_P3_Foot"        # the edited mesh the claw wants
ORIGINAL = "Mesh_Leg_P3_Foot.001"  # the pristine one the legs want back
GRABBER_NAME = "Mesh_Claw_Grabber"

# The six feet that are actually on legs. Mesh_Leg_P2_Foot is not among them —
# it was taken to be the claw's pincer, and Mesh_Leg_P2_Foot.001 replaced it.
LEG_FEET = ["Mesh_Leg_N1_Foot", "Mesh_Leg_N2_Foot", "Mesh_Leg_N3_Foot",
            "Mesh_Leg_P1_Foot", "Mesh_Leg_P2_Foot.001", "Mesh_Leg_P3_Foot"]
CLAW_TIP = "Mesh_Leg_P2_Foot"

# The claw, base first. Order comes from where the parts actually sit.
CLAW_CHAIN = [
    ("Mesh_Leg_N1_Upper",       "Claw_01"),
    ("Mesh_Leg_N1_Lower.003",   "Claw_02"),
    ("Mesh_Leg_N1_Lower.001",   "Claw_03"),
    ("Mesh_Leg_N1_Upper.002",   "Claw_04"),
    ("Mesh_Leg_N1_Lower.004",   "Claw_05"),
    ("Mesh_Leg_N1_Lower",       "Claw_06"),
    (CLAW_TIP,                  "Claw_Grab"),
]

# Stranded leg parts: object -> the bone on the real rig they belong on.
STRANDED = {
    "Mesh_Leg_N1_Lower.002": "Knee_N1",
    "Mesh_Leg_N1_Upper.001": "Hip_N1",
    "Mesh_Leg_P2_Foot.001":  "Foot_P2",
}


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


def main():
    bpy.ops.wm.open_mainfile(filepath=TARGET)
    bpy.context.view_layer.update()

    arm = bpy.data.objects.get("CRAWLER_Rig")
    if arm is None:
        raise SystemExit("CRAWLER_Rig missing — refusing to touch this file.")
    for name in [HOOKED, ORIGINAL]:
        if name not in bpy.data.meshes:
            raise SystemExit("Expected mesh %s; file is not in the state this "
                             "script was written for." % name)
    for name in LEG_FEET + [CLAW_TIP] + [n for n, _ in CLAW_CHAIN]:
        if name not in bpy.data.objects:
            raise SystemExit("Expected object %s" % name)

    before = {o.name: o.matrix_world.copy() for o in bpy.data.objects}
    bone_kids = {o.name: (o.parent.name, o.parent_bone)
                 for o in bpy.data.objects
                 if o.parent is not None and o.parent_type == 'BONE'}
    print("Opened %s — %d objects, %d armatures"
          % (os.path.basename(TARGET), len(before),
             len([o for o in bpy.data.objects if o.type == 'ARMATURE'])))

    # ---- 1. give the legs their foot back --------------------------------
    original = bpy.data.meshes[ORIGINAL]
    hooked = bpy.data.meshes[HOOKED]
    swapped = 0
    for name in LEG_FEET:
        obj = bpy.data.objects[name]
        if obj.data is not original:
            obj.data = original
            swapped += 1
    print("  re-pointed %d feet at the original mesh (%d tris)"
          % (swapped, sum(len(p.vertices) - 2 for p in original.polygons)))

    tip = bpy.data.objects[CLAW_TIP]
    if tip.data is not hooked:
        raise SystemExit("%s is not on the hooked mesh — aborting rather than "
                         "guessing." % CLAW_TIP)
    if hooked.users != 1:
        raise SystemExit("Hooked mesh still has %d users; expected only the "
                         "claw tip." % hooked.users)

    hooked.name = GRABBER_NAME
    original.name = HOOKED
    print("  hooked mesh -> %r (single user), original reclaimed the name %r"
          % (GRABBER_NAME, HOOKED))

    # ---- 2. build the claw chain ----------------------------------------
    pts = [before[n].translation.copy() for n, _ in CLAW_CHAIN]
    inv = arm.matrix_world.inverted()
    bpy.context.view_layer.objects.active = arm
    arm.hide_set(False)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    parent = eb["Root"]
    for i, (_, bone_name) in enumerate(CLAW_CHAIN):
        if bone_name in eb:
            raise SystemExit("Bone %s already exists" % bone_name)
        b = eb.new(bone_name)
        b.head = inv @ pts[i]
        if i + 1 < len(pts):
            b.tail = inv @ pts[i + 1]
        else:
            # The grabber: continue the direction the arm arrives from.
            d = (pts[i] - pts[i - 1]).normalized()
            b.tail = inv @ (pts[i] + d * 2.20)
        b.roll = 0.0
        b.parent = parent
        b.use_connect = False
        parent = b
    bpy.ops.object.mode_set(mode='OBJECT')
    print("  built %d claw bones: %s"
          % (len(CLAW_CHAIN), ", ".join(b for _, b in CLAW_CHAIN)))

    # ---- 3. everything back onto the real rig ---------------------------
    for obj_name, bone_name in CLAW_CHAIN:
        attach(bpy.data.objects[obj_name], arm, bone_name, before[obj_name])
    for obj_name, bone_name in STRANDED.items():
        attach(bpy.data.objects[obj_name], arm, bone_name, before[obj_name])
    # Anything else still hanging off a duplicate goes to the same-named bone.
    for obj_name, (parent_name, bone_name) in bone_kids.items():
        if parent_name == "CRAWLER_Rig":
            continue
        obj = bpy.data.objects[obj_name]
        if obj.parent is not arm:
            if bone_name not in arm.data.bones:
                raise SystemExit("%s hangs off bone %r the real rig lacks"
                                 % (obj_name, bone_name))
            attach(obj, arm, bone_name, before[obj_name])

    # ---- 4. the duplicate armatures go ----------------------------------
    gone = []
    for o in [o for o in bpy.data.objects if o.type == 'ARMATURE' and o is not arm]:
        kids = [k.name for k in bpy.data.objects if k.parent is o]
        if kids:
            raise SystemExit("%s still owns %s — refusing to delete it"
                             % (o.name, kids))
        data = o.data
        gone.append(o.name)
        bpy.data.objects.remove(o, do_unlink=True)
        if data.users == 0:
            bpy.data.armatures.remove(data)
    print("  removed %d duplicate armature(s): %s" % (len(gone), ", ".join(gone)))

    # ---- verify ----------------------------------------------------------
    bpy.context.view_layer.update()
    print("\nVerification")
    for name, m0 in before.items():
        obj = bpy.data.objects.get(name)
        if obj is None:
            if name in gone:
                continue
            raise SystemExit("LOST object %s" % name)
        delta = max(abs(obj.matrix_world[r][c] - m0[r][c])
                    for r in range(4) for c in range(4))
        if delta > 1e-4:
            raise SystemExit("MOVED %s by %.5f" % (name, delta))
    print("  nothing moved, nothing lost")

    foot_mesh = bpy.data.meshes[HOOKED]
    bad = [n for n in LEG_FEET if bpy.data.objects[n].data is not foot_mesh]
    if bad:
        raise SystemExit("feet not on the original mesh: %s" % bad)
    print("  all 6 feet on %r (%d users, %d tris)"
          % (HOOKED, foot_mesh.users,
             sum(len(p.vertices) - 2 for p in foot_mesh.polygons)))
    grab = bpy.data.meshes[GRABBER_NAME]
    print("  claw tip on %r (%d user, %d tris)"
          % (GRABBER_NAME, grab.users,
             sum(len(p.vertices) - 2 for p in grab.polygons)))
    print("  armatures now: %s"
          % [o.name for o in bpy.data.objects if o.type == 'ARMATURE'])
    print("  bones now %d" % len(arm.data.bones))

    bpy.ops.wm.save_as_mainfile(filepath=TARGET)
    print("\nWrote %s" % TARGET)


main()
