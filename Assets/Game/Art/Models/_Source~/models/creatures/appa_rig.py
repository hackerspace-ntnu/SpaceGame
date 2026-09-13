"""Rig appa.blend -- a six-legged bison, hand-modelled by the author.

    Run through the live Blender session (MCP), not --background: the .blend is
    hand-authored and open, and this file is the record of what was done to it.

This script is **purely additive**. It creates one armature and binds the
existing meshes to it. It never moves, reshapes, renames or deletes a single
piece of the author's geometry, and it refuses to run twice.

## Why nothing here rotates or rescales the model

The sculpt faces -X at roughly 5.5 m nose to tail, and the library convention is
-Y forward with the origin between the soles. The temptation is to fix that here,
once, in the source. Do not: rescaling or yawing the .blend moves every vertex of
the author's model, and the .blend is the source of truth.

`appa_export.py` applies the yaw and the pivot on the way out instead, by
transforming `Arm_Appa` itself -- the same thing `vrescal_export.py` does, and
for the same reason. That works only because every mesh in the file ends up
parented to this armature, which is what the binding below guarantees.

## Skinned versus bone-parented

Two kinds of binding, chosen per mesh:

  * **Skinned** (`ARMATURE_AUTO`) for the six meshes that must deform: the legs,
    the body, the head, the mane, the saddle fur and the ears. The legs matter
    most -- all six of them are a SINGLE mesh (`Cube`, 1538 verts), so they can
    only move independently if vertices are weighted per leg. Splitting that
    mesh into six would have been easier to weight and is exactly the kind of
    edit to the author's geometry this workflow forbids.

  * **Bone-parented**, rigidly, for the twenty-one that do not: horns, eyes,
    teeth, muzzle, lower jaw and the six hooves. These are solid props that
    should travel with a bone without bending, and rigid parenting is both
    cheaper and more predictable than asking the heat solver to weight an
    eight-vertex tooth.

Every mesh lands in one bucket or the other, so `Arm_Appa` is the only root
object when this finishes. The exporter depends on that.
"""

import bpy
from mathutils import Vector

ARM = "Arm_Appa"

# ---------------------------------------------------------------------------
# Skeleton, in the .blend's own space: +X runs nose-to-tail (the head is at -X),
# +Z is up, the soles sit at z = -1.76. Every number below was read off the
# model's actual world bounds rather than guessed, so the bones sit inside the
# geometry they drive.
# ---------------------------------------------------------------------------

SOLE_Z = -1.76

# Leg attachment points, measured from the six foot meshes. Front pair nearest
# the head, back pair nearest the tail.
LEG_X = {"F": 0.64, "M": 1.48, "B": 2.24}
LEG_Y = 0.86

# name: (head, tail, parent, connected)
def _skeleton():
    bones = [
        ("root",   (1.44, 0.0, SOLE_Z), (1.44, 0.0, SOLE_Z + 0.45), None,    False),

        # Trunk, laid out tail-to-head so the chain flows the way the animal does.
        ("spine1", (2.60, 0.0, -0.20), (1.60, 0.0, -0.15), "root",   False),
        ("spine2", (1.60, 0.0, -0.15), (0.75, 0.0, -0.20), "spine1", True),
        ("spine3", (0.75, 0.0, -0.20), (0.25, 0.0, -0.25), "spine2", True),
        ("neck",   (0.25, 0.0, -0.25), (-0.05, 0.0, -0.35), "spine3", True),
        ("head",   (-0.05, 0.0, -0.35), (-0.50, 0.0, -0.45), "neck",  True),
        ("jaw",    (-0.15, 0.0, -0.70), (-0.45, 0.0, -0.85), "head",  False),

        # Tail, hanging off the hips and dropping toward the ground.
        ("tail1",  (2.60, 0.0, -0.20), (3.30, 0.0, -0.35), "spine1", False),
        ("tail2",  (3.30, 0.0, -0.35), (3.90, 0.0, -0.80), "tail1",  True),
        ("tail3",  (3.90, 0.0, -0.80), (4.40, 0.0, -1.50), "tail2",  True),
    ]

    # Six legs. The front pair hangs off spine2 and the middle and back off
    # spine1, which is roughly where they meet the body on the model.
    for pos, spine in (("F", "spine2"), ("M", "spine1"), ("B", "spine1")):
        x = LEG_X[pos]
        for side, sy in (("L", 1.0), ("R", -1.0)):
            y = LEG_Y * sy
            bones += [
                ("femur_%s.%s" % (pos, side),
                 (x, y * 0.75, -0.45), (x, y, -1.00), spine, False),
                ("tibia_%s.%s" % (pos, side),
                 (x, y, -1.00), (x, y, -1.45), "femur_%s.%s" % (pos, side), True),
                ("hoof_%s.%s" % (pos, side),
                 (x, y, -1.45), (x - 0.25, y, -1.70), "tibia_%s.%s" % (pos, side), True),
            ]
    return bones


# ---------------------------------------------------------------------------
# Which mesh goes where. Names are the author's own; the comments say what each
# one actually is, because "Cube.026" does not.
# ---------------------------------------------------------------------------

SKINNED = [
    "Cube",       # all six legs, one mesh
    "Cube.016",   # body and tail
    "Cube.004",   # head
    "Cube.026",   # mane
    "Cube.015",   # saddle fur
    "Cube.007",   # ears
]

RIGID = {
    "Cube.002": "head",   # muzzle
    "Cube.001": "head",   # horn L
    "Cube.009": "head",   # horn R
    "Cube.017": "head",   # eye
    "Cube.018": "head",   # eye
    "Cube.011": "head",   # brow tuft
    "Cube.003": "jaw",    # lower jaw
    "Cube.025": "jaw", "Cube.027": "jaw", "Cube.028": "jaw", "Cube.029": "jaw",
    "Cube.030": "jaw", "Cube.031": "jaw", "Cube.032": "jaw", "Cube.033": "jaw",
    # Hooves, matched to their leg by the foot meshes' own measured centres.
    "Cube.021": "hoof_F.R", "Cube.023": "hoof_F.L",
    "Cube.020": "hoof_M.R", "Cube.024": "hoof_M.L",
    "Cube.022": "hoof_B.R", "Cube.019": "hoof_B.L",
}


# Which bones are allowed to drive each skinned mesh, and what to fall back to
# for a vertex that ends up with nothing.
#
# Bone-heat weighting decides by proximity, and proximity lies here. The mane
# hangs down around the front legs and the saddle fur sits directly above them,
# so the solver bound 7.4% of the mane and 24.4% of the shoulder fur to
# `femur_*` -- and every stride tore the hair apart, which reads in game as
# shredded, see-through geometry. It looks perfect in Blender because the rest
# pose never moves.
#
# Nothing here is a tuning value. A leg cannot move the hair on an animal's
# neck, so leg bones are simply not in the list.
WEIGHT_RESTRICT = {
    "Cube":     (["femur_", "tibia_", "hoof_", "spine1", "spine2"], "spine1"),
    # Cube.016 carries the visible legs as well as the torso, so it needs the leg
    # chain. The first version of this row listed only spine/neck/tail -- reading
    # "Cube = legs, Cube.016 = body" off the object names -- and that one omission
    # froze every leg to spine1 while the bone-parented hooves kept swinging, which
    # read as an animal skating on its feet. See appa_weights.py.
    "Cube.016": (["femur_", "tibia_", "hoof_",
                  "spine1", "spine2", "spine3", "neck", "tail1", "tail2", "tail3"], "spine1"),
    "Cube.004": (["head", "neck", "jaw"], "head"),
    "Cube.026": (["head", "neck", "spine3"], "neck"),
    "Cube.015": (["spine1", "spine2", "spine3"], "spine2"),
    "Cube.007": (["head", "neck"], "head"),
}


def restrict_weights():
    """Drop every influence a mesh should not have, then renormalise.

    Runs after `ARMATURE_AUTO`. Deleting a vertex group would leave any vertex
    that lived entirely inside it with no weight at all -- and a zero-weight
    vertex on a skinned mesh collapses to the origin, which is a far louder bug
    than the one being fixed. So each vertex is rebuilt explicitly: keep the
    allowed influences, renormalise them to sum to 1, and give anything left
    with nothing to the mesh's fallback bone.
    """
    report = []

    for name, (allowed, fallback) in WEIGHT_RESTRICT.items():
        obj = bpy.data.objects.get(name)
        if obj is None or not obj.vertex_groups:
            continue

        keep = {vg.index: vg.name for vg in obj.vertex_groups
                if any(vg.name.startswith(a) for a in allowed)}
        dropped = [vg.name for vg in obj.vertex_groups if vg.index not in keep]
        if not dropped:
            continue

        fb = obj.vertex_groups.get(fallback) or obj.vertex_groups.new(name=fallback)

        moved = 0.0
        orphans = 0
        for v in obj.data.vertices:
            kept = [(g.group, g.weight) for g in v.groups
                    if g.group in keep and g.weight > 0.0]
            total = sum(w for _, w in kept)
            lost = sum(g.weight for g in v.groups if g.group not in keep)
            moved += lost

            if total <= 1e-6:
                for g in list(v.groups):
                    obj.vertex_groups[g.group].remove([v.index])
                fb.add([v.index], 1.0, 'REPLACE')
                orphans += 1
                continue

            for gi, w in kept:
                obj.vertex_groups[gi].add([v.index], w / total, 'REPLACE')

        for vg_name in dropped:
            vg = obj.vertex_groups.get(vg_name)
            if vg is not None:
                obj.vertex_groups.remove(vg)

        report.append((name, dropped, moved, orphans))

    return report


def build():
    if ARM in bpy.data.objects:
        raise SystemExit("%s already exists -- this file is already rigged. "
                         "Refusing to rig it twice." % ARM)

    scene = bpy.context.scene
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='DESELECT')

    arm_data = bpy.data.armatures.new(ARM)
    arm = bpy.data.objects.new(ARM, arm_data)
    scene.collection.objects.link(arm)

    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')

    for name, head, tail, parent, connected in _skeleton():
        eb = arm_data.edit_bones.new(name)
        eb.head = Vector(head)
        eb.tail = Vector(tail)
        if parent:
            eb.parent = arm_data.edit_bones[parent]
            eb.use_connect = connected

    bpy.ops.object.mode_set(mode='OBJECT')

    report = {"skinned": [], "rigid": [], "auto_weight_failed": []}

    # --- skinned -----------------------------------------------------------
    for name in SKINNED:
        obj = bpy.data.objects.get(name)
        if obj is None:
            continue
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        arm.select_set(True)
        bpy.context.view_layer.objects.active = arm
        try:
            bpy.ops.object.parent_set(type='ARMATURE_AUTO')
            report["skinned"].append(name)
        except RuntimeError as exc:
            # Bone-heat weighting gives up on geometry it cannot solve. Say so
            # loudly and fall back, rather than leaving a mesh silently unbound.
            report["auto_weight_failed"].append("%s: %s" % (name, exc))
            _proximity_weights(obj, arm)
            report["skinned"].append(name + " (proximity)")

    # --- rigid -------------------------------------------------------------
    for name, bone in RIGID.items():
        obj = bpy.data.objects.get(name)
        if obj is None or bone not in arm_data.bones:
            continue
        world = obj.matrix_world.copy()
        obj.parent = arm
        obj.parent_type = 'BONE'
        obj.parent_bone = bone
        # Bone parenting seats the child at the bone TAIL; restoring the world
        # matrix afterwards puts the prop back exactly where it was modelled.
        obj.matrix_world = world
        report["rigid"].append("%s -> %s" % (name, bone))

    # Bone-heat weighting decides by proximity and gets the hair badly wrong --
    # see WEIGHT_RESTRICT. Never leave its output unfiltered.
    report["restricted"] = restrict_weights()

    return arm, report


def _proximity_weights(obj, arm):
    """Last-resort skinning: weight every vertex fully to its nearest bone.

    Blockier than heat weighting and only used when that fails outright. Good
    enough for a stylised model whose limbs are separate islands anyway.
    """
    for bone in arm.data.bones:
        if bone.use_deform and bone.name not in obj.vertex_groups:
            obj.vertex_groups.new(name=bone.name)

    if obj.find_armature() is None:
        mod = obj.modifiers.new(name="Armature", type='ARMATURE')
        mod.object = arm
        obj.parent = arm

    segs = [(b.name, b.head_local, b.tail_local) for b in arm.data.bones if b.use_deform]

    for v in obj.data.vertices:
        p = obj.matrix_world @ v.co
        best, best_d = None, 1e18
        for name, h, t in segs:
            d = _point_segment_distance(p, h, t)
            if d < best_d:
                best, best_d = name, d
        if best:
            obj.vertex_groups[best].add([v.index], 1.0, 'REPLACE')


def _point_segment_distance(p, a, b):
    ab = b - a
    denom = ab.dot(ab)
    if denom < 1e-9:
        return (p - a).length
    t = max(0.0, min(1.0, (p - a).dot(ab) / denom))
    return (p - (a + ab * t)).length


if __name__ == "__main__":
    armature, rep = build()
    print("skinned:", rep["skinned"])
    print("rigid  :", len(rep["rigid"]))
    if rep["auto_weight_failed"]:
        print("AUTO WEIGHT FELL BACK:", rep["auto_weight_failed"])
    roots = [o.name for o in bpy.data.objects if o.parent is None]
    print("root objects (must be only the armature):", roots)
