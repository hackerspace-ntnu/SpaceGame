# Appa — skin weight repairs: the legs, and the jaw.
#
# Symptom this fixes: Appa's feet swing correctly while his legs stay welded to his
# body, so he looks like he is skating along on his hooves.
#
# Cause: the hooves (Cube.019..Cube.024) are BONE-PARENTED to hoof_*, so they follow
# the rig no matter what the weights say. The legs, however, are part of the body mesh
# Cube.016, and below the hip that mesh was weighted almost entirely to spine1 —
# measured 100% spine1 in the ankle band, 81% in the shin band. femur/tibia rotate a
# full 64 deg / 52 deg in Appa_Walk (verified in the fcurves); nothing was listening.
#
# Repair: take Blender's own bone-heat weights for the leg chain, and hand the leg
# region of Cube.016 over to them, blended by how much the leg bones claim each vertex.
# A vertex the leg bones do not reach keeps its existing weighting untouched, so the
# torso, tail, neck and hump deform exactly as before.
#
# This edits weights ONLY. No geometry, no objects, no materials, no actions.
# Idempotent: the non-leg weights are renormalised from their own relative shares
# rather than scaled in place, so a second run reproduces the first run's result.
#
#   blender --background appa.blend --python appa_weights.py -- --save
#
# Omit --save for a dry run that reports what it would do and writes nothing.

import sys

import bpy

TARGET = "Cube.016"
ARMATURE = "Arm_Appa"
LEG_PREFIXES = ("femur_", "tibia_", "hoof_")
MAX_INFLUENCES = 4          # Unity's default skin quality; cap here so the FBX is honest

# How far up the body the leg bones are allowed to reach, in world Z.
#
# Bone heat on its own diffuses far past the hip: left unbounded it gave the hump
# 11% femur_M and the whole back rocked with the stride. The femur heads sit at
# z = -0.45, so hand the legs full authority only below the haunch and fade them
# out over the hip joint, where a blend is what the deformation wants anyway.
GATE_TOP = -0.35            # at and above: existing weighting kept verbatim
GATE_BOTTOM = -0.70         # at and below: leg bones take everything they claim

# How the face mesh follows the jaw: it does not.
#
# The mouth is built from two interlocking pieces. `Cube.003` -- the lower jaw,
# with the eight teeth -- is BONE-PARENTED to `jaw` and moves rigidly, and it
# spans z -1.06..-0.74. The head mesh `Cube.004` spans z -0.89..+0.10. The lower
# lip belongs to the jaw piece; the head mesh is the skull around it. So the head
# does not need to follow the jaw, and every attempt to make it do so made things
# worse:
#
#   * bone heat put weight on it as far up as the brow, so opening his mouth
#     dragged his eyes down;
#   * clamping that by a horizontal Z band creased the muzzle, because a mandible
#     runs diagonally and a horizontal cut crosses it;
#   * keeping bone heat's shape and cutting only its weak tail dragged the NOSE,
#     because on this mesh heat reaches the nose at 0.45 -- the upper and lower
#     lip are a centimetre apart and no volumetric rule separates them.
#
# The jaw already has its own geometry. Hand the head mesh's jaw weight back to
# the bones that should own it and let the rigid piece hinge.
JAW_MESH = "Cube.004"
JAW_FALLBACK = "head"


def is_leg(name):
    return name.startswith(LEG_PREFIXES)


def gate(z):
    """Smoothstep from body-owned to leg-owned across the hip."""
    t = (GATE_TOP - z) / (GATE_TOP - GATE_BOTTOM)
    t = min(1.0, max(0.0, t))
    return t * t * (3.0 - 2.0 * t)


def clear_face_jaw():
    """Take the jaw out of the head mesh, giving its share to head/neck.

    Idempotent: a vertex with no jaw weight left is already finished, and the
    others are rebuilt from their own relative shares rather than scaled in
    place, so a second run reproduces the first.
    """
    obj = bpy.data.objects.get(JAW_MESH)
    if obj is None:
        raise SystemExit("No %s to take the jaw off." % JAW_MESH)

    jaw = obj.vertex_groups.get("jaw")
    if jaw is None:
        print("%s already has no jaw group." % JAW_MESH)
        return

    fallback = obj.vertex_groups.get(JAW_FALLBACK) or obj.vertex_groups.new(name=JAW_FALLBACK)
    names = {g.index: g.name for g in obj.vertex_groups}

    cleared = 0
    reclaimed = 0.0
    for i, vertex in enumerate(obj.data.vertices):
        weights = {names[g.group]: g.weight for g in vertex.groups}
        had = weights.get("jaw", 0.0)
        if had <= 1e-5:
            continue

        others = {n: w for n, w in weights.items() if n != "jaw" and w > 1e-5}
        total = sum(others.values())
        if total > 1e-5:
            others = {n: w / total for n, w in others.items()}
        else:
            others = {JAW_FALLBACK: 1.0}

        jaw.remove([i])
        for n, w in others.items():
            group = obj.vertex_groups.get(n) or fallback
            group.add([i], w, "REPLACE")

        cleared += 1
        reclaimed += had

    print("face jaw weights cleared: %d of %d verts on %s, %.1f weight returned to head/neck"
          % (cleared, len(obj.data.vertices), JAW_MESH, reclaimed))


def main():
    save = "--save" in sys.argv

    arm = bpy.data.objects[ARMATURE]
    ob = bpy.data.objects[TARGET]

    # ── 1. bone-heat weights, computed on a throwaway copy ───────────────────
    # Running parent_set on the real object would replace every group on it,
    # discarding the torso weighting this repair is supposed to preserve.
    temp = ob.copy()
    temp.data = ob.data.copy()
    temp.name = "__appa_weight_probe"
    bpy.context.scene.collection.objects.link(temp)
    temp.modifiers.clear()
    temp.vertex_groups.clear()
    temp.parent = None
    temp.matrix_world = ob.matrix_world

    bpy.ops.object.select_all(action="DESELECT")
    temp.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
        method = "bone heat"
    except RuntimeError as exc:
        # Bone heat needs a solvable mesh; envelopes always produce something.
        print("bone heat failed (%s) — falling back to envelopes" % exc)
        bpy.ops.object.parent_set(type="ARMATURE_ENVELOPE")
        method = "envelope"

    probe_names = {g.index: g.name for g in temp.vertex_groups}
    claimed = []
    for v in temp.data.vertices:
        row = {}
        for g in v.groups:
            name = probe_names[g.group]
            if is_leg(name) and g.weight > 1e-4:
                row[name] = g.weight
        claimed.append(row)

    probe_mesh = temp.data
    bpy.data.objects.remove(temp, do_unlink=True)
    bpy.data.meshes.remove(probe_mesh)

    total_claim = sum(sum(r.values()) for r in claimed)
    print("probe: %s, %d/%d verts claimed by leg bones, total claim %.1f"
          % (method, sum(1 for r in claimed if r), len(claimed), total_claim))
    if total_claim < 1.0:
        raise SystemExit("The leg bones claim nothing on %s — the probe produced no "
                         "usable weights, so there is nothing to transfer." % TARGET)

    # ── 2. snapshot what Cube.016 has now ────────────────────────────────────
    own_names = {g.index: g.name for g in ob.vertex_groups}
    original = []
    for v in ob.data.vertices:
        original.append({own_names[g.group]: g.weight for g in v.groups})

    for name in sorted({n for row in claimed for n in row}):
        if name not in ob.vertex_groups:
            ob.vertex_groups.new(name=name)

    # ── 3. blend: legs take what they claim, the rest keeps its own shape ────
    matrix = ob.matrix_world
    touched = 0
    for i, leg in enumerate(claimed):
        if not leg:
            continue

        share = min(1.0, sum(leg.values())) * gate((matrix @ ob.data.vertices[i].co).z)
        if share < 1e-3:
            continue

        # Rebuild from the ORIGINAL non-leg shares rather than scaling whatever is
        # there now. That is what makes a second run land on the same answer.
        keep = {n: w for n, w in original[i].items() if not is_leg(n) and w > 1e-5}
        keep_total = sum(keep.values())

        merged = {}
        if keep_total > 1e-5:
            for n, w in keep.items():
                merged[n] = w * (1.0 - share) / keep_total
            leg_total = sum(leg.values())
            for n, w in leg.items():
                merged[n] = merged.get(n, 0.0) + w * share / leg_total
        else:
            leg_total = sum(leg.values())
            for n, w in leg.items():
                merged[n] = w / leg_total

        # Cap influences so the FBX carries what Unity will actually skin with.
        if len(merged) > MAX_INFLUENCES:
            top = sorted(merged.items(), key=lambda kv: -kv[1])[:MAX_INFLUENCES]
            merged = dict(top)
        norm = sum(merged.values())
        if norm <= 1e-6:
            continue
        merged = {n: w / norm for n, w in merged.items()}

        for name in set(original[i]) | set(merged):
            group = ob.vertex_groups.get(name)
            if group is None:
                continue
            w = merged.get(name, 0.0)
            if w > 1e-5:
                group.add([i], w, "REPLACE")
            else:
                group.remove([i])
        touched += 1

    print("reweighted %d of %d vertices on %s" % (touched, len(ob.data.vertices), TARGET))

    clear_face_jaw()

    if save:
        bpy.ops.wm.save_mainfile()
        print("saved", bpy.data.filepath)
    else:
        print("dry run — nothing written (pass --save)")


main()
