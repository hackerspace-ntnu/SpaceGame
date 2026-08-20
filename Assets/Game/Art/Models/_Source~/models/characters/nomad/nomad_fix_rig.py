"""Consolidate nomad.blend onto a single armature.

    blender --background --python nomad_fix_rig.py

Unlike `nomad_export.py`, this one WRITES to the .blend. It keeps a `.pre_rigfix` copy beside
the file on every run.

The problem it fixes
--------------------
The file accumulated 18 copies of the same 65-bone Mixamo skeleton. Most are empty, but the
damage is done by the ones that are not: several worn meshes -- `Harness`, `Kneepads` and
friends -- are modelled on the body at x~7.08 while the armature they are bound to sits at the
world origin. Blender shows nothing wrong, because in the rest pose an armature modifier is the
identity, so the mesh simply renders at its own transform. The moment a clip plays, every one of
those vertices is rotated about a pivot 7 m away and the piece flies off the character.

Consolidating onto `Armature.001` -- the copy that shares the live body's world matrix -- both
removes the duplicates and repairs that binding, because each mesh keeps its world transform
while its skinning is recomputed against a skeleton that is actually inside it.

Bone names are identical across all copies, so vertex groups keep working untouched.
"""

import os
import shutil

import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
BLEND = os.path.join(HERE, "nomad.blend")
BACKUP = os.path.join(HERE, "nomad_pre_rigfix.blend")

# The copy sharing the live body's world matrix (x = 7.078). Everything is rebound to this.
KEEP = "Armature.001"

EXPECTED_BONES = 65

# The cape, given names that survive being read six months from now. Verified by rendering each
# candidate in isolation, not by trusting the material or the numbering: `Plane.007`, `.009` and
# `.010` sit in the same numeric run and look like cape panels by name, but they render as a grey
# shoulder pad and two thigh pads and are NOT cloth.
#
# Anything renamed here must stay in step with CLOTH_MESHES in nomad_export.py and
# ClothMeshNames in NomadPrefabBuilder.cs -- Unity names GameObjects from these object names, and
# a mismatch silently drops the ClothWind material, which is what makes the cape both animate and
# render two-sided.
RENAMES = {
    "Plane": "Cloth_Cape_01",        # the full-length tattered cloak
    "Plane.008": "Cloth_Cape_02",    # the shoulder flap
}

# A loose mesh taller than this is a hanging garment, not a worn prop, and anchors at the collar.
# Kept in step with nomad_export.py.
GARMENT_SPAN_Z = 1.2
CAPE_ANCHOR_BONE = "mixamorig:Spine2"


def nearest_bone(armature, point):
    """Bone whose midpoint is closest to `point`, both in world space."""
    best, best_d2 = None, None
    for bone in armature.data.bones:
        mid = armature.matrix_world @ ((bone.head_local + bone.tail_local) / 2.0)
        d2 = (mid - point).length_squared
        if best_d2 is None or d2 < best_d2:
            best, best_d2 = bone.name, d2
    return best


def world_bbox(obj):
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def rebind(obj, armature):
    """Point `obj` at `armature` without moving it on screen.

    `matrix_parent_inverse` must be set before assigning `matrix_world`; the armature carries a
    0.01 scale, and letting Blender fold that into the local basis is the documented way to
    collapse these objects to the origin.
    """
    keep_matrix = obj.matrix_world.copy()
    keep_type = obj.parent_type
    keep_bone = obj.parent_bone

    obj.parent = armature
    if keep_type == 'BONE' and keep_bone:
        obj.parent_type = 'BONE'
        obj.parent_bone = keep_bone
    else:
        obj.parent_type = 'OBJECT'
        obj.matrix_parent_inverse = armature.matrix_world.inverted()

    obj.matrix_world = keep_matrix

    for mod in obj.modifiers:
        if mod.type == 'ARMATURE':
            mod.object = armature


def main():
    if not os.path.exists(BLEND):
        raise SystemExit("No model at %s" % BLEND)

    shutil.copyfile(BLEND, BACKUP)
    print("Backed up -> %s" % BACKUP)

    bpy.ops.wm.open_mainfile(filepath=BLEND)

    target = bpy.data.objects.get(KEEP)
    if target is None or target.type != 'ARMATURE':
        raise SystemExit("No armature named %s in the file." % KEEP)

    bones = len(target.data.bones)
    if bones != EXPECTED_BONES:
        raise SystemExit("%s has %d bones, expected %d -- refusing to consolidate onto a "
                         "skeleton that is not the one everything is weighted to."
                         % (KEEP, bones, EXPECTED_BONES))

    # Rename first, so everything logged below uses the names that ship.
    renamed, already = [], []
    for old, new in RENAMES.items():
        if new in bpy.data.objects:
            already.append(new)
            continue
        obj = bpy.data.objects.get(old)
        if obj is None:
            print("  WARNING: cannot rename %s -> %s, no such object" % (old, new))
            continue
        obj.name = new
        obj.data.name = new
        renamed.append((old, new))

    for old, new in renamed:
        print("  renamed %-22s -> %s" % (old, new))
    if already:
        print("  already named: %s" % ", ".join(sorted(already)))

    # Two-sided in Blender. This does NOT carry into Unity -- there the cape is two-sided because
    # the ClothWind shader declares `Cull Off` in every pass -- but leaving it culled here means
    # the .blend viewport lies about what the game shows.
    for new in RENAMES.values():
        obj = bpy.data.objects.get(new)
        if obj is None:
            continue
        for mat in obj.data.materials:
            if mat is not None and mat.use_backface_culling:
                mat.use_backface_culling = False
                print("  turned off backface culling on %s (used by %s)" % (mat.name, new))

    armatures = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    doomed = [a for a in armatures if a is not target]

    # Every copy must really be the same skeleton, or rebinding silently changes deformation.
    target_names = {b.name for b in target.data.bones}
    for arm in doomed:
        names = {b.name for b in arm.data.bones}
        if names != target_names:
            missing = len(target_names - names)
            extra = len(names - target_names)
            raise SystemExit(
                "%s is NOT the same skeleton as %s (%d bones missing, %d extra). Refusing to "
                "merge -- inspect it by hand." % (arm.name, KEEP, missing, extra))

    print("Found %d armatures, all identical 65-bone copies. Keeping %s."
          % (len(armatures), KEEP))

    # Rebind anything driven by, or parented to, one of the doomed copies.
    doomed_set = set(doomed)
    moved = []
    for obj in bpy.data.objects:
        if obj.type == 'ARMATURE':
            continue

        needs = obj.parent in doomed_set
        for mod in obj.modifiers:
            if mod.type == 'ARMATURE' and mod.object in doomed_set:
                needs = True

        if not needs:
            continue

        before, _ = world_bbox(obj)
        old_parent = obj.parent.name if obj.parent else "-"
        rebind(obj, target)
        after, _ = world_bbox(obj)

        drift = (after - before).length
        moved.append((obj.name, old_parent, drift))

    for name, old, drift in sorted(moved):
        flag = "  <-- MOVED" if drift > 1e-4 else ""
        print("  rebound %-24s (was %-14s) drift=%.6f%s" % (name, old, drift, flag))

    for arm in doomed:
        bpy.data.objects.remove(arm, do_unlink=True)
    print("Removed %d duplicate armature object(s)." % len(doomed))

    # Drop the now-unused armature datablocks so they do not come back on next load.
    stale = [a for a in bpy.data.armatures if a.users == 0]
    for data in stale:
        bpy.data.armatures.remove(data)
    print("Purged %d orphaned armature datablock(s)." % len(stale))

    # Bind anything still loose. These are the props the character wears -- the head dome, the
    # boolean spheres, the cape sheet, the pouches and shoulder plates -- which carry no armature
    # modifier at all, so they sit frozen in space while the body walks out from under them.
    loose = [o for o in bpy.data.objects
             if o.type == 'MESH'
             and not any(m.type == 'ARMATURE' for m in o.modifiers)]

    bound = []
    for obj in loose:
        lo, hi = world_bbox(obj)
        centre = (lo + hi) / 2.0

        # A tall hanging sheet belongs at the collar; anchoring it at whatever bone is nearest
        # its middle would put a full-length cape on the hips and tear the collar off the
        # shoulders. Anything compact rides the bone it actually sits on.
        if (hi.z - lo.z) > GARMENT_SPAN_Z:
            bone = CAPE_ANCHOR_BONE
        else:
            bone = nearest_bone(target, centre)

        rebind(obj, target)

        for group in list(obj.vertex_groups):
            obj.vertex_groups.remove(group)
        group = obj.vertex_groups.new(name=bone)
        group.add([v.index for v in obj.data.vertices], 1.0, 'REPLACE')

        mod = obj.modifiers.new(name="Armature", type='ARMATURE')
        mod.object = target

        bound.append((obj.name, bone))

    print("Bound %d loose prop(s) to the rig:" % len(bound))
    for name, bone in sorted(bound):
        print("    %-24s -> %s" % (name, bone))

    still_loose = [o.name for o in bpy.data.objects
                   if o.type in {'MESH', 'CURVE'}
                   and not any(m.type == 'ARMATURE' for m in o.modifiers)]
    if still_loose:
        print("NOTE: %d object(s) still have no armature modifier:" % len(still_loose))
        for name in sorted(still_loose):
            print("    %s" % name)

    posed = [b.name for b in target.pose.bones
             if b.matrix_basis != b.matrix_basis.Identity(4)]
    print("Pose bones off the rest pose: %d%s"
          % (len(posed), (" (" + ", ".join(posed[:6]) + ")") if posed else ""))

    constrained = [(b.name, [c.type for c in b.constraints])
                   for b in target.pose.bones if b.constraints]
    print("Pose bones carrying constraints: %d" % len(constrained))
    for name, kinds in constrained[:10]:
        print("    %s %s" % (name, kinds))

    print("Remaining armatures: %d"
          % len([o for o in bpy.data.objects if o.type == 'ARMATURE']))

    bpy.ops.wm.save_mainfile(filepath=BLEND)
    print("Saved %s" % BLEND)


main()
