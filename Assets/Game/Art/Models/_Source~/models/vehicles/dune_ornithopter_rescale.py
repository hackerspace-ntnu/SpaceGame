"""Uniformly rescale an assembled .blend in place, preserving hand edits.

    blender --background --python dune_ornithopter_rescale.py -- \
        --file dune_ornithopter.blend --factor 1.6666667

Run once, on 2026-08-11, to take the ornithopter from its authored 6 m span to
the 10 m the prone rider actually fits — the change `dune_ornithopter_BUILD.md`
prescribes as "set TARGET_SPAN = 10.0 and rerun the builds".

**Rerunning the builds was the wrong move, and this script exists because of
it.** The shipped .blend is not generator output: the cradle pad, both
stirrups and the fuselage core carry hand-authored object scales, and the grip
bar was saved in Edit Mode. Regenerating would have discarded all of it. The
components are pure generator output and *were* rebuilt at the new span; only
the assembly needed this treatment.

Kept in the repo rather than thrown away because the hazard is permanent: any
future rescale of this file faces the same three traps, all of which fail
silently.

Used instead of regenerating `dune_ornithopter.blend`, which carries hand edits
(non-unit object scales on the cradle pad, both stirrups and the fuselage core)
that exist nowhere else. Regenerating would silently discard them.

Every mesh in this file is a child of the armature, either bone-parented (rigid
parts) or object-parented with an Armature modifier (the six webbed panels), so
a correct rescale has three parts and all three must agree:

  1. **Mesh data**, scaled once per unique datablock. Several meshes are placed
     twice off shared data -- bearings, drive wheels, cranks, one per side --
     and scaling per object would square the factor on the second placement.
  2. **Edit bone head/tail**, so the skeleton grows with the geometry. Weights
     are per vertex group and carry no lengths, so they stay valid.
  3. **Object world translation**, scaled by the same factor with rotation and
     object scale left exactly as they are. Leaving object scale alone is the
     whole point: those non-unit values ARE the hand edits.

Rotation and scale are re-composed rather than the matrix being multiplied
through, so a hand-scaled object keeps its own factor instead of having the
global one folded into it.
"""

import sys

import bpy
from mathutils import Matrix


def arg(name, default=None):
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if name not in argv:
        if default is None:
            raise SystemExit("Missing %s" % name)
        return default
    return argv[argv.index(name) + 1]


def span(label):
    """World-space X extent across every mesh — the wingspan."""
    lo, hi = float("inf"), float("-inf")
    deps = bpy.context.evaluated_depsgraph_get()
    for o in bpy.data.objects:
        if o.type != 'MESH':
            continue
        for corner in o.bound_box:
            x = (o.matrix_world @ Matrix.Translation(corner)).to_translation().x
            lo, hi = min(lo, x), max(hi, x)
    print("  %-8s span = %.4f m" % (label, hi - lo))
    return hi - lo


def main():
    path = arg("--file")
    k = float(arg("--factor"))

    bpy.ops.wm.open_mainfile(filepath=path)

    # This file was saved with an object left in Edit Mode. Blender restores
    # that on open, and an object in edit mode keeps a separate edit-mesh that
    # is flushed back over the mesh datablock on save — so `Mesh.transform()`
    # appears to work, reports the right vertices, and is then silently thrown
    # away when the file is written. Exactly one mesh survived the first run at
    # its original size because of this. Leaving edit mode flushes the edit
    # mesh down into the datablock first, so the transform lands on the real
    # geometry.
    for o in bpy.data.objects:
        if o.mode != 'OBJECT':
            print("  %s was left in %s mode — flushing to object mode"
                  % (o.name, o.mode))
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.mode_set(mode='OBJECT')

    before = span("before")

    # 1. Record every world matrix BEFORE anything moves. Scaling the bones in
    #    step 3 drags bone-parented children with them, so the originals have
    #    to be captured up front or step 4 reads poses that already moved.
    world = {o.name: o.matrix_world.copy() for o in bpy.data.objects}

    # 2. Mesh data, once per datablock.
    # Keyed on the datablock itself, not its name. Two objects can point at one
    # mesh whose name matches neither of them — `_buildlib.save()` renames data
    # after the object it last saw — so a name-keyed guard can mark a block as
    # done that was never touched, and the mesh silently keeps its old size.
    scaled = set()
    for o in bpy.data.objects:
        if o.type != 'MESH' or o.data in scaled:
            continue
        o.data.transform(Matrix.Diagonal((k, k, k, 1.0)))
        scaled.add(o.data)
    print("  scaled %d unique mesh datablock(s)" % len(scaled))

    # 3. Edit bones. Must go through edit mode: bone.head/tail are read-only
    #    outside it, and writing the pose instead would leave the rest pose --
    #    which is what the skinning binds against -- at the old size.
    for arm in [o for o in bpy.data.objects if o.type == 'ARMATURE']:
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode='EDIT')
        # Snapshot first, then assign. A connected child's head IS its parent's
        # tail — the same point — so scaling in place reads a value the parent's
        # assignment already scaled and squares the factor on it. Three bones
        # here are connected (Nose, Boom_2, TailHub) and Boom_1's tail came out
        # at k^2. Assigning from the snapshot writes the same scaled point from
        # both ends, which is consistent rather than compounding.
        rest = {eb.name: (eb.head.copy(), eb.tail.copy())
                for eb in arm.data.edit_bones}
        for eb in arm.data.edit_bones:
            head, tail = rest[eb.name]
            eb.head = head * k
            eb.tail = tail * k
        bpy.ops.object.mode_set(mode='OBJECT')
        print("  scaled %d bone(s) on %s" % (len(arm.data.bones), arm.name))

    # 4. Object placement: translation only. Rotation and object scale are put
    #    back untouched, so the hand-authored non-unit scales survive.
    for o in bpy.data.objects:
        loc, rot, scl = world[o.name].decompose()
        o.matrix_world = (Matrix.Translation(loc * k)
                          @ rot.to_matrix().to_4x4()
                          @ Matrix.Diagonal(scl).to_4x4())

    bpy.context.view_layer.update()
    after = span("after")
    print("  ratio = %.6f (asked %.6f)" % (after / before, k))

    bpy.ops.wm.save_as_mainfile(filepath=path)
    print("Wrote %s" % path)


main()
