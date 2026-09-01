# Gives the RIGHT hand fingers that bend, and a socket to charge the spell in.
#
# STEP 3 OF 5:  rig.py -> walkerize.py -> hands.py -> anim.py -> export.py
# Safe to re-run: every step below checks for its own output first.
#
# ---- why this exists ---------------------------------------------------------
#
# rig.py bound all 52 parts by RIGID BONE-PARENTING, which is the right answer for
# a mech and wrong for exactly one thing: a hand that has to close. It bone-parented
# the finger meshes onto Hand.L / Hand.R and MUTED the armature modifiers they came
# with, so the fingers travel with the wrist and cannot move relative to it.
#
# The finger bones were never lost. The model shipped with two legacy hand rigs --
# `Armature` (right, 18 bones) and `Armature.001` (left, 14) -- which rig.py parked in
# WIP_Spares rather than deleting, precisely so this was recoverable. This script
# lifts the RIGHT one's bones into ConjurerRig, where an action can key them.
#
# ---- why only the right hand -------------------------------------------------
#
# The attack cups one hand and points the other, and only the cupping hand needs to
# deform. The right is the better candidate by some distance:
#
#   right  `Hand.001`, ONE mesh, weighted to all 18 legacy bones, four complete
#          fingers of metacarpal + 3 phalanges.
#   left   THIRTEEN separate meshes each carrying a copy of the group list, and
#          `Armature.001` is missing every metacarpal plus two of its bones are
#          orphans with no parent at all.
#
# The left hand keeps rig.py's rigid parenting and just aims. If it ever needs to
# close too, the work is the same shape as below but has to reconstruct the missing
# metacarpals first.
#
# ---- the double-transform trap -----------------------------------------------
#
# A mesh cannot be bone-parented to Hand.R AND skinned to bones under Hand.R. The
# wrist's motion would reach it twice and the fingers would swing out at double the
# angle. So the mesh is moved off the bone parent onto a plain object parent, and
# the armature modifier -- the one rig.py muted -- is re-pointed at ConjurerRig and
# switched back on. That is the whole reason the modifiers were muted rather than
# removed.
import bpy
from mathutils import Matrix, Vector

RIG = "ConjurerRig"
LEGACY = "Armature"          # the right-hand rig
MESH = "Hand.001"            # the right-hand finger mesh it skins
HAND = "Hand.R"

arm = bpy.data.objects[RIG]

# Legacy bone -> the name it takes inside ConjurerRig. The legacy names are unusable
# as-is: `Hand`/`Hand.001` collide with the rig's own arm bones and with the mesh
# object of the same name, and `Pointer.NNN` says nothing about which finger it is.
# Fingers are numbered in the order they appear in the legacy hierarchy:
#   1, 2 hang down off the palm;  3, 4 spread outward and inward respectively.
RENAME = {
    "Hand":        HAND,          # the wrist itself already exists on the rig
    "Hand.001":    "Meta1.R",
    "Pointer.004": "Finger1A.R", "Pointer.005": "Finger1B.R", "Pointer.006": "Finger1C.R",
    "Hand.002":    "Meta2.R",
    "Pointer.001": "Finger2A.R", "Pointer.002": "Finger2B.R", "Pointer.003": "Finger2C.R",
    "Hand.003":    "Meta3.R",
    "Pointer.007": "Finger3A.R", "Pointer.008": "Finger3B.R", "Pointer.009": "Finger3C.R",
    "Hand.004":    "Meta4.R",
    "Pointer.010": "Finger4A.R", "Pointer.011": "Finger4B.R", "Pointer.012": "Finger4C.R",
    # Degenerate in the source: head == tail, zero length, no children. Blender will
    # not create a bone like that. Its weights fold onto the wrist.
    "Pointer.013": HAND,
}

# Where the spell charges. A bone rather than an Empty because Unity imports bones as
# GameObjects in the model hierarchy -- so the VFX gets a transform it can parent to
# by name, and it rides the cup through the whole animation for free.
SOCKET = "CastSocket.R"
SOCKET_HEAD = Vector((-0.23, -6.46, 17.80))
SOCKET_TAIL = Vector((-0.23, -6.46, 17.20))


def log(m):
    print(f"[hands] {m}")


if SOCKET in arm.data.bones and "Meta1.R" in arm.data.bones:
    log("already merged - nothing to do")
else:
    legacy = bpy.data.objects.get(LEGACY)
    if legacy is None:
        raise SystemExit(f"[hands] {LEGACY} is gone; nothing to lift finger bones from")

    mesh = bpy.data.objects.get(MESH)
    if mesh is None:
        raise SystemExit(f"[hands] mesh {MESH} not found")

    # World-space head/tail of every legacy bone, taken through the legacy object's
    # own matrix -- it sits at (0.39, -6.46, 18.41), not the origin, so bone-local
    # coordinates would land the fingers a body-width away.
    LM = legacy.matrix_world
    src = {}
    for b in legacy.data.bones:
        src[b.name] = (LM @ b.head_local, LM @ b.tail_local,
                       b.parent.name if b.parent else None)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    ebs = arm.data.edit_bones

    # Parent before child: a bone cannot be parented to one that does not exist yet.
    def depth(n):
        d, p = 0, src[n][2]
        while p is not None and p in src:
            d, p = d + 1, src[p][2]
        return d

    order = sorted((n for n in RENAME if n in src and RENAME[n] != HAND), key=depth)

    created = 0
    for name in order:
        head, tail, parent = src[name]
        if (head - tail).length < 1e-5:
            log(f"skipping degenerate {name}")
            continue
        new = RENAME[name]
        if new in ebs:
            continue
        eb = ebs.new(new)
        eb.head, eb.tail, eb.roll = head, tail, 0.0
        # Legacy parent maps through the same table; the metacarpals' parent is the
        # legacy wrist, which maps onto the rig's existing Hand.R.
        eb.parent = ebs[RENAME.get(parent, HAND)]
        # Connected only where the joint is genuinely shared. The metacarpals start
        # at the legacy wrist's TAIL (z 18.40) while Hand.R's tail is at z 19.00,
        # so connecting them would drag Hand.R's tail down and bend the forearm.
        eb.use_connect = parent in RENAME and RENAME[parent] != HAND
        created += 1

    if SOCKET not in ebs:
        sk = ebs.new(SOCKET)
        sk.head, sk.tail, sk.roll = SOCKET_HEAD, SOCKET_TAIL, 0.0
        sk.parent = ebs[HAND]
        sk.use_connect = False
        created += 1

    bpy.ops.object.mode_set(mode='OBJECT')
    log(f"created {created} bones; rig now has {len(arm.data.bones)}")

    # ---- re-point the mesh's weights at the new names ------------------------
    # Same weights, same vertices -- only the group LABELS change, so the deform is
    # bit-identical to what the legacy rig produced.
    renamed = 0
    for vg in mesh.vertex_groups:
        if vg.name in RENAME and vg.name != RENAME[vg.name]:
            # Two legacy groups fold onto Hand.R (the wrist and the degenerate bone).
            # Blender will not hold two groups of one name, so the second is left
            # alone; its weights are on the palm, which Hand.R already carries.
            if RENAME[vg.name] in {g.name for g in mesh.vertex_groups}:
                continue
            vg.name = RENAME[vg.name]
            renamed += 1
    log(f"renamed {renamed} vertex groups on {MESH}")

    # ---- swap bone-parent for skinning --------------------------------------
    world = mesh.matrix_world.copy()
    mesh.parent = arm
    mesh.parent_type = 'OBJECT'
    mesh.parent_bone = ""
    mesh.matrix_parent_inverse = Matrix.Identity(4)
    mesh.matrix_basis = world

    mod = next((m for m in mesh.modifiers if m.type == 'ARMATURE'), None)
    if mod is None:
        mod = mesh.modifiers.new("Armature", 'ARMATURE')
    mod.object = arm
    mod.show_viewport = mod.show_render = True
    log(f"{MESH} now skinned to {RIG} (was bone-parented to {HAND})")

    bpy.ops.wm.save_mainfile()
    log("SAVED")

# ---- verify ------------------------------------------------------------------
# Cheap and worth it: a finger bone that failed to parent is invisible in the
# viewport until an action keys it and the finger flies off on its own.
bones = arm.data.bones
problems = []
for n in ("Meta1.R", "Meta2.R", "Meta3.R", "Meta4.R", SOCKET):
    if n not in bones:
        problems.append(f"{n} missing")
    elif bones[n].parent is None or bones[n].parent.name != HAND:
        problems.append(f"{n} is not parented to {HAND}")
for i in (1, 2, 3, 4):
    for a, b in (("A", f"Meta{i}.R"), ("B", f"Finger{i}A.R"), ("C", f"Finger{i}B.R")):
        n = f"Finger{i}{a}.R"
        if n not in bones:
            problems.append(f"{n} missing")
        elif bones[n].parent.name != b:
            problems.append(f"{n} parented to {bones[n].parent.name}, expected {b}")

m = bpy.data.objects[MESH]
groups = {g.name for g in m.vertex_groups}
for i in (1, 2, 3, 4):
    for s in ("A", "B", "C"):
        if f"Finger{i}{s}.R" not in groups:
            problems.append(f"{MESH} has no weights for Finger{i}{s}.R")
if m.parent_type == 'BONE':
    problems.append(f"{MESH} is still bone-parented - double transform")
if not any(x.type == 'ARMATURE' and x.object is arm and x.show_viewport for x in m.modifiers):
    problems.append(f"{MESH} has no live armature modifier for {RIG}")

if problems:
    for p in problems:
        print(f"[hands] FAIL: {p}")
    raise SystemExit("[hands] verification failed")
log(f"OK: 4 fingers x 3 phalanges + {SOCKET} on {HAND}, {MESH} skinned")
