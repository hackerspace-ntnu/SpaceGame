# Gives BOTH hands fingers that bend, and a socket for the spell to charge in.
#
# STEP 3 OF 6:  restore_parts.py -> rig.py -> walkerize.py -> hands_rebuild.py
#               -> rustify.py -> anim.py -> export.py
# Safe to re-run: every step checks for its own output first.
#
# ---- what this replaces ------------------------------------------------------
#
# The old hands.py lifted bones out of the model's own salvaged right-hand rig,
# because that rig was the only source of finger joints the file had. It could
# only ever do one hand: the right was a single mesh weighted to 18 bones, while
# the left was thirteen loose meshes on an armature missing every metacarpal.
# Neither had a thumb.
#
# restore_parts.py has since replaced both with two copies of
# components/mechanical/robot_hand.blend, so the joints now arrive as a clean,
# symmetric, five-digit armature per side. This script copies those joints into
# ConjurerRig and hangs each phalanx off its own bone.
#
# ---- why bone-parenting rather than skinning ---------------------------------
#
# The old hand had to be skinned: it was ONE mesh, so the only way to bend it was
# to weight its vertices. The new hand is sixteen rigid objects, each with its
# origin on its own hinge pin, and for that rigid bone-parenting is strictly
# better -- no weights to paint, no smearing at a knuckle, and the same treatment
# every other part of this mech already gets. It also sidesteps the double
# transform the old file kept tripping over, because nothing is both parented to
# a wrist and skinned to bones under it.
#
# Exactly one armature may reach Unity, so the two component armatures are parked
# in WIP_Spares once their joint positions have been read off them.
import bpy
from mathutils import Matrix, Vector

RIG = "ConjurerRig"
SPARES = "WIP_Spares"
DIGITS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
SEGMENTS = (1, 2, 3)
# One per hand. The right one used to be the only one because the old attack
# cupped a ball in the right palm and the left merely pointed; the chest cast
# arcs to BOTH palms and then fires from the point between them, so the left is
# now load-bearing too.
SOCKETS = {s: f"CastSocket.{s}" for s in ("R", "L")}

arm = bpy.data.objects[RIG]


def log(m):
    print(f"[hands] {m}")


def rigid_bone_parent(obj, bone_name):
    """Bone-parent `obj` to `bone_name`, leaving its world transform identical.

    Blender anchors bone parenting at the bone TAIL, so the parent matrix P
    carries a +Y translation of bone.length. With matrix_parent_inverse = P^-1
    the chain collapses to matrix_basis, which must therefore be set to the
    object's ORIGINAL WORLD matrix. Same helper, same trap, as rig.py.
    """
    world = obj.matrix_world.copy()
    bone = arm.data.bones[bone_name]
    obj.parent = arm
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    P = arm.matrix_world @ bone.matrix_local @ Matrix.Translation(
        (0.0, bone.length, 0.0))
    obj.matrix_parent_inverse = P.inverted()
    obj.matrix_basis = world


def spares():
    coll = bpy.data.collections.get(SPARES)
    if coll is None:
        coll = bpy.data.collections.new(SPARES)
        bpy.context.scene.collection.children.link(coll)
    return coll


def park(obj):
    coll = spares()
    if obj.name in coll.objects:
        return
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)


def joints(side):
    """World-space (head, tail) per digit segment, read off the component rig.

    Taken through the component armature's own matrix rather than from its bones'
    local coordinates: restore_parts.py placed, scaled and (on the left) mirrored
    those armatures into the creature's frame, and bone-local coordinates would
    land the fingers back at the origin at component scale.
    """
    src = bpy.data.objects.get(f"HandRig.{side}")
    if src is None:
        raise SystemExit(f"[hands] no HandRig.{side} - run restore_parts.py first")
    M = src.matrix_world
    out = {}
    for d in DIGITS:
        for i in SEGMENTS:
            b = src.data.bones.get(f"Bone_{d}{i}")
            if b is None:
                raise SystemExit(f"[hands] HandRig.{side} has no Bone_{d}{i}")
            out[(d, i)] = (M @ b.head_local, M @ b.tail_local)
    return out


def cast_socket_point(side, js):
    """Where the charge sits: out in front of the palm, level with the fingers.

    Derived from the hand rather than typed, so it follows the geometry if the
    hand is ever rescaled or reseated. The fingertips are SPLAYED at rest and
    converge only once the Attack clip closes them, so the socket cannot simply
    be their centroid -- it sits half a finger-length off the palm, which is
    where the cup actually forms.
    """
    palm = bpy.data.objects[f"Hand_Palm.{side}"]
    normal = (palm.matrix_world.to_3x3() @ Vector((0.0, 0.0, -1.0))).normalized()

    knuckles = sum((js[(d, 1)][0] for d in DIGITS), Vector()) / len(DIGITS)
    tips = sum((js[(d, 3)][1] for d in DIGITS), Vector()) / len(DIGITS)
    reach = (tips - knuckles).length

    return (knuckles + tips) / 2.0 + normal * reach * 0.5, normal


# ------------------------------------------------------------------ build
if ("Thumb1.R" in arm.data.bones
        and all(n in arm.data.bones for n in SOCKETS.values())):
    log("already rebuilt - nothing to do")
else:
    sides = {s: joints(s) for s in ("R", "L")}

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    ebs = arm.data.edit_bones

    created = 0
    for side, js in sides.items():
        wrist = f"Hand.{side}"
        for d in DIGITS:
            parent = ebs[wrist]
            for i in SEGMENTS:
                name = f"{d}{i}.{side}"
                if name in ebs:
                    parent = ebs[name]
                    continue
                head, tail = js[(d, i)]
                eb = ebs.new(name)
                eb.head, eb.tail, eb.roll = head, tail, 0.0
                eb.parent = parent
                # Connected only within a digit. The first phalanx starts on the
                # knuckle, not at the wrist bone's tail, so connecting it would
                # drag Hand.{side}'s tail onto the knuckle and shorten the palm.
                eb.use_connect = i > 1
                parent = eb
                created += 1

        if SOCKETS[side] not in ebs:
            point, normal = cast_socket_point(side, js)
            sk = ebs.new(SOCKETS[side])
            sk.head = point
            sk.tail = point + normal * (arm.data.edit_bones[wrist].length * 0.3)
            sk.roll = 0.0
            sk.parent = ebs[wrist]
            sk.use_connect = False
            created += 1

    bpy.ops.object.mode_set(mode='OBJECT')
    log(f"created {created} bones; rig now has {len(arm.data.bones)}")

    # ---- hang each phalanx off its own bone ---------------------------------
    bound = 0
    for side in ("R", "L"):
        for d in DIGITS:
            for i in SEGMENTS:
                o = bpy.data.objects.get(f"Hand_{d}{i}.{side}")
                if o is None:
                    raise SystemExit(f"[hands] missing mesh Hand_{d}{i}.{side}")
                rigid_bone_parent(o, f"{d}{i}.{side}")
                bound += 1
    log(f"bone-parented {bound} phalanges")

    # ---- the component armatures have served their purpose ------------------
    for side in ("R", "L"):
        src = bpy.data.objects.get(f"HandRig.{side}")
        if src is not None:
            src.parent = None
            park(src)
    log("parked HandRig.R / HandRig.L in WIP_Spares (nothing deleted)")

    bpy.ops.wm.save_mainfile()
    log("SAVED")

# ------------------------------------------------------------------ verify
# Cheap and worth it: a finger bone that failed to parent is invisible in the
# viewport until an action keys it and the finger flies off on its own.
bones = arm.data.bones
problems = []

for side in ("R", "L"):
    for d in DIGITS:
        for i in SEGMENTS:
            n = f"{d}{i}.{side}"
            if n not in bones:
                problems.append(f"{n} missing")
                continue
            want = f"Hand.{side}" if i == 1 else f"{d}{i - 1}.{side}"
            got = bones[n].parent.name if bones[n].parent else "-"
            if got != want:
                problems.append(f"{n} parented to {got}, expected {want}")

            o = bpy.data.objects.get(f"Hand_{d}{i}.{side}")
            if o is None:
                problems.append(f"mesh Hand_{d}{i}.{side} missing")
            elif o.parent_type != 'BONE' or o.parent_bone != n:
                problems.append(f"Hand_{d}{i}.{side} is not bone-parented to {n}")

for side, socket in SOCKETS.items():
    if socket not in bones:
        problems.append(f"{socket} missing")
    elif bones[socket].parent is None or bones[socket].parent.name != f"Hand.{side}":
        problems.append(f"{socket} is not parented to Hand.{side}")

# Exactly one armature may reach the exporter, which walks ConjurerRig's children.
for side in ("R", "L"):
    src = bpy.data.objects.get(f"HandRig.{side}")
    if src is not None and src.parent is arm:
        problems.append(f"HandRig.{side} is still under {RIG}")

if problems:
    for p in problems:
        print(f"[hands] FAIL: {p}")
    raise SystemExit("[hands] verification failed")

log(f"OK: 2 hands x 5 digits x 3 phalanges + {', '.join(SOCKETS.values())}, "
    f"{len(bones)} bones on {RIG}")
