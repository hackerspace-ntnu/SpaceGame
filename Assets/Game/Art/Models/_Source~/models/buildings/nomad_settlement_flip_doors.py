"""Turn every door in the settlement to face the other way, in place.

The generator now stamps doors at `pre_z = 0` instead of 180, but the shipped
`nomad_settlement.blend` carries hand edits - the bone course went yellow - so
it is edited rather than rebuilt. This is the one-shot that did it, kept as the
record of what changed.

Why a half turn about the group's own bounding-box centre is the whole fix: a
door is stamped as

    T(target) . Rz(alpha) . Rx(lean) . S(k) . T(-a) . Rz(pre_z)

with `a` the group's bounding-box centre, so the placed group's box centre
lands exactly on `target`, and a door's lean is always zero - R12 makes the
ground storey straight. A half turn about the vertical line through that centre
therefore maps the door's box onto itself: the frame stays straddling the wall
by exactly the depth it did, and only the leaf changes sides. No repositioning,
no re-embedding, nothing else to check.

    blender --background nomad_settlement.blend \
        --python nomad_settlement_flip_doors.py -- --save

Runs once, and only on the file it was written for. A second run on the same
file is refused - two half turns are no turn at all - but the marker lives in
the file, so a freshly generated settlement carries none. Do not run it on one:
the generator already stamps doors the right way round, and this would put them
back to front.
"""

import math
import re
import sys

import bpy
from mathutils import Matrix, Vector

# `Mesh_Door_Frame` stamped under prefix `B07_M_Door` comes out as
# `B07_M_Door_Door_Frame`; a terrace door as `B07_M_TerraceDoor_Door_Frame`.
DOOR_PART = re.compile(r"^(?P<group>.+_(?:Terrace)?Door)_Door_(?:Frame|Leaf)$")

MARKER = "nomad_doors_flipped"


def door_groups():
    groups = {}
    for o in bpy.data.objects:
        m = DOOR_PART.match(o.name)
        if m:
            groups.setdefault(m.group("group"), []).append(o)
    return groups


def bbox(objs):
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts),
                 min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts),
                 max(p.z for p in pts)))
    return lo, hi


def centre(objs):
    lo, hi = bbox(objs)
    return (lo + hi) * 0.5


def flip(objs):
    """Half turn about the vertical axis through the group's own box centre."""
    c = centre(objs)
    axis = Vector((c.x, c.y, 0.0))
    r = (Matrix.Translation(axis)
         @ Matrix.Rotation(math.pi, 4, 'Z')
         @ Matrix.Translation(-axis))
    for o in objs:
        o.matrix_world = r @ o.matrix_world


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    scene = bpy.context.scene
    if scene.get(MARKER):
        raise SystemExit("[doors] %s already flipped - refusing, two half turns "
                         "are no turn at all" % bpy.data.filepath)

    groups = door_groups()
    if not groups:
        raise SystemExit("[doors] no door groups found - wrong file?")

    # Measured before and after, because a rotation is the build error that
    # looks right and is not: the leaf must end up on the opposite side of the
    # frame, and the group's footprint must not have moved at all.
    before = {}
    for name, objs in groups.items():
        leaf = next(o for o in objs if o.name.endswith("_Leaf"))
        frame = next(o for o in objs if o.name.endswith("_Frame"))
        before[name] = (centre([frame]), centre([leaf]), bbox(objs))

    for objs in groups.values():
        flip(objs)
    bpy.context.view_layer.update()

    worst_dot, worst_drift = 1.0, 0.0
    for name, objs in groups.items():
        fc0, lc0, (lo0, hi0) = before[name]
        leaf = next(o for o in objs if o.name.endswith("_Leaf"))
        frame = next(o for o in objs if o.name.endswith("_Frame"))
        v0 = (lc0 - fc0)
        v1 = (centre([leaf]) - centre([frame]))
        v0.z = v1.z = 0.0
        if v0.length > 1e-5 and v1.length > 1e-5:
            worst_dot = min(worst_dot, -(v0.normalized().dot(v1.normalized())))
        lo1, hi1 = bbox(objs)
        worst_drift = max(worst_drift, (lo1 - lo0).length, (hi1 - hi0).length)

    print("[doors] flipped %d doors" % len(groups))
    print("[doors] leaf reversal (1.0 is an exact half turn): %.4f" % worst_dot)
    print("[doors] worst box drift: %.6f m" % worst_drift)
    if worst_dot < 0.999 or worst_drift > 1e-4:
        raise SystemExit("[doors] verification failed - nothing saved")

    scene[MARKER] = True
    if "--save" in argv:
        bpy.ops.wm.save_mainfile()
        print("[doors] saved %s" % bpy.data.filepath)
    else:
        print("[doors] dry run - pass --save to write")


if __name__ == "__main__":
    main()
