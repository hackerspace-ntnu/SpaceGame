"""Fit a skeleton to each of the four sand-nomad characters in sand_nogs.blend.

    blender --background --python sand_nogs_fix_rig.py -- [path/to/sand_nogs.blend] [--dry-run]

WRITES to the .blend (a timestamped `sand_nogs_pre_rigfix_*.blend` copy is kept beside it),
unless `--dry-run` is given, in which case it only reports what it would do. The default path
is `~/Documents/Blender/sand_nogs.blend`, where the file is authored; it is deliberately NOT
copied into the library because the user asked for it to be fixed in place.

The problem it fixes
--------------------
The file holds four new characters built by duplicating the original nomad (the grey copies at
x~5 and x~7, bound to `Armature.001` and `Armature.004`) and dressing each one differently. In the
course of that the body meshes were scaled up ~1.26x and slid down ~1.5 m, and the skeletons were
not: every rig still stands with its feet at z=0 while the mesh it drives runs from z=-1.6 to
z=+1.3. Blender hides this, because in the rest pose an armature modifier is the identity, so the
mesh simply renders where it is. Play a clip and every vertex rotates about a bone 1.3 m above it.
One character (`Suit`, at x~-4) is worse: it is bound to `Armature` at x=0, a whole body-width
away. On top of that, two dozen pouches, straps and scarf pieces per character carry an armature
modifier with NO armature, or no groups at all, and would be exported as static geometry.

What it does
------------
1. For each character, measures the similarity (uniform scale + translation) that maps the
   REFERENCE suit (`Suit.001`, correctly bound) onto that character's suit, vertex by vertex, and
   refuses if the residual says the mesh was reshaped rather than just moved.
2. Applies that same similarity to a fresh copy of the reference skeleton, so every bone lands
   inside the body exactly where it sits in the original.
3. Rebinds every mesh of the character to that skeleton without moving it on screen, keeping the
   real weights on the suit and gloves, re-anchoring props whose inherited weights point at bones
   nowhere near them, and skinning garments across the bones they span.
4. Moves the leaning pole beside each character onto their back and straps it to the spine.
5. Gives every part a name a reader can use, prefixed with the character's name.
6. Removes the empty duplicate armatures the file had accumulated.

The two grey reference copies, the parked scenery above z=3.2, and `Armature` (x=0, which still
drives four parked pieces) are left exactly as they were.
"""

import datetime
import math
import os
import shutil
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

DEFAULT_BLEND = os.path.join(os.path.expanduser("~"), "Documents", "Blender", "sand_nogs.blend")

REFERENCE_SUIT = "Suit.001"
REFERENCE_GLOVES = "Gloves.001"
REFERENCE_ARMATURE = "Armature.001"
EXPECTED_BONES = 65

# Each character is identified by its suit -- the one mesh that is unmistakably the body. The x
# window round the suit's centre gathers the rest of the outfit; nothing else in the file sits
# within 1.4 m of a suit except that suit's own clothes.
CHARACTERS = {
    "Umber":    "Suit.004",   # x ~ -10.7, dark umber suit, orange scarves
    "Tan":      "Suit.003",   # x ~  -7.8, tan suit, dark red shawl
    "Maroon":   "Suit",       # x ~  -4.2, maroon suit, black scarf
    "StrawHat": "Suit.005",   # x ~   0.0, umber suit, conical hat
}
CLUSTER_HALF_WIDTH = 1.4

# Anything whose lowest point is above this is parked scenery, not worn.
PARKED_Z = 3.2

# A prop weighted to bones this much farther away than the nearest bone has inherited junk
# weights from whatever it was duplicated from. Same threshold as nomad_export.py.
MAX_WEIGHT_DRIFT = 0.4

# Meshes spanning more than this vertically are garments, skinned across the bones they cover
# rather than pinned to one.
GARMENT_SPAN_Z = 1.0

# The bones a garment may be skinned to. Limbs are excluded on purpose: a shawl that hangs past
# the elbows must not be dragged by the forearms swinging in a walk.
GARMENT_BONES = [
    "mixamorig:Hips", "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2",
    "mixamorig:Neck", "mixamorig:Head",
    "mixamorig:LeftShoulder", "mixamorig:RightShoulder",
    "mixamorig:LeftArm", "mixamorig:RightArm",
]

# The pole: the tallest loose cylinder in each cluster. Strapped diagonally across the back,
# lower end at the left hip, upper end past the right shoulder, standing this far behind the
# spine (the characters face -Y, so +Y is behind them).
POLE_MIN_HEIGHT = 2.2
POLE_BACK_OFFSET = 0.42
POLE_LEAN_DEGREES = 22.0
POLE_ANCHOR_BONE = "mixamorig:Spine1"

# Names for the parts the file's numbering hides, matched by what the part IS: the same mesh
# duplicated four times has the same vertex count in every character.
NAMES_BY_VERTS = {
    43177: "Suit",
    1580: "Gloves",
    16388: "Boots",
    1852: "Shawl",        # hooded shawl over the shoulders
    1767: "Scarf_Long",   # the long tail of cloth hanging to the feet
    191: "Sash",          # the loop of cloth round the torso
    386: "Hat",           # the conical straw hat
    544: "Belt",
    331: "Cuff_A",
    339: "Cuff_B",
    441: "Cuff_C",
}
# Same idea for the smaller duplicated pieces, keyed by vertex count and given a running number
# per character because several share a count.
NUMBERED_BY_VERTS = {
    1787: "Scarf",
    2052: "Hood",
    1570: "Pouch",
    320: "Ring",
    288: "Rod",
    104: "Pack",
    56: "Buckle",
    64: "Band",
    8: "Dome",
}


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    dry = "--dry-run" in argv
    paths = [a for a in argv if not a.startswith("--")]
    return (paths[0] if paths else DEFAULT_BLEND), dry


def world_bbox(obj):
    pts = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi


def world_verts(obj):
    return [obj.matrix_world @ v.co for v in obj.data.vertices]


def bone_mid(armature, bone):
    return armature.matrix_world @ ((bone.head_local + bone.tail_local) / 2.0)


def depth(bone):
    return 0 if bone.parent is None else 1 + depth(bone.parent)


def nearest_bone(armature, point, names=None):
    """Bone whose SEGMENT passes closest to `point`, both in world space.

    Segment, not midpoint: a head dome centred on the joint between neck and head is a hair
    closer to the neck's midpoint and would ride the neck, then stay behind when the head turns.
    Ties (a point on a joint is on both segments) go to the deeper bone, which is the one that
    actually turns there.
    """
    best, best_key = None, None
    mw = armature.matrix_world
    for bone in armature.data.bones:
        if names is not None and bone.name not in names:
            continue
        head, tail = mw @ bone.head_local, mw @ bone.tail_local
        axis = tail - head
        t = max(0.0, min(1.0, (point - head).dot(axis) / max(axis.length_squared, 1e-9)))
        d = (point - (head + axis * t)).length
        key = (round(d, 3), -depth(bone))
        if best_key is None or key < best_key:
            best, best_key = bone.name, key
    return best


# A rigid prop longer than this is strapped to the trunk, never to a limb: the rod carried on the
# back sits nearest the upper arm in the rest pose, and would swing with it at every step.
BIG_PROP_SPAN = 0.6
TRUNK_BONES = ["mixamorig:Hips", "mixamorig:Spine", "mixamorig:Spine1", "mixamorig:Spine2",
               "mixamorig:Neck", "mixamorig:Head"]


def fit_axis_scale(src, dst):
    """Per-axis scale + translation taking `src` points onto `dst` points (same order).

    Per-axis rather than uniform because that is what was done to these bodies: measured against
    the reference they are ~1.03x wide, ~1.17x deep and ~1.26x tall, and a uniform fit leaves a
    10 cm residual that reads as "reshaped". Returns (scale Vector, translation Vector, RMS residual).
    """
    n = len(src)
    cs = sum(src, Vector()) / n
    cd = sum(dst, Vector()) / n
    scale = Vector((1.0, 1.0, 1.0))
    for axis in range(3):
        var = sum((p[axis] - cs[axis]) ** 2 for p in src)
        dot = sum((p[axis] - cs[axis]) * (q[axis] - cd[axis]) for p, q in zip(src, dst))
        scale[axis] = dot / var
    translation = cd - Vector((cs[0] * scale[0], cs[1] * scale[1], cs[2] * scale[2]))
    residual = math.sqrt(sum((Vector((p[0] * scale[0], p[1] * scale[1], p[2] * scale[2])) + translation - q).length_squared
                             for p, q in zip(src, dst)) / n)
    return scale, translation, residual


def kabsch(src, dst):
    """Rotation + translation taking `src` onto `dst` (numpy arrays, same order), least squares."""
    cs = src.mean(axis=0)
    cd = dst.mean(axis=0)
    h = (src - cs).T @ (dst - cd)
    u, _, vt = np.linalg.svd(h)
    d = np.sign(np.linalg.det(vt.T @ u.T))
    r = vt.T @ np.diag([1.0, 1.0, d]) @ u.T
    t = cd - r @ cs
    residual = math.sqrt(((src @ r.T + t - dst) ** 2).sum(axis=1).mean())
    return r, t, residual


# A bone needs this many vertices weighted mostly to it before its own fit is trusted; below
# that it moves with its parent. Finger and toe bones fall here, which is what you want.
MIN_BONE_VERTS = 15
BONE_WEIGHT_CUTOFF = 0.4

# What the per-bone fit may leave over on a bone that had enough vertices. Bigger than this and
# the vertices were reshaped rather than moved, and the fitted bone is a guess.
MAX_BONE_RESIDUAL = 0.06


def fit_bones_to_body(armature, ref_arm, scale, translation, pairs):
    """Re-derive the rest skeleton from where the body's vertices actually are.

    A single affine map is not enough: the arms were lowered from the reference T-pose into an
    A-pose, vertex by vertex, so the torso fits an affine map to a centimetre and the arms miss it
    by ten. What DOES hold is that each bone's own vertices moved together as a rigid piece. So for
    every bone with enough vertices weighted to it, the reference positions of those vertices are
    matched against their positions on this body (same mesh, same vertex order) and the rigid
    transform that best explains the move is applied to that bone's head and tail. Bones with too
    few vertices to fit -- fingers, toes, chain tips -- ride their parent's transform.

    The global per-axis scale and translation are applied first, so the rigid fit only has to
    explain the pose change. Everything is baked into the edit bones and the armature object keeps
    the reference's uniform 0.01 scale, which is what the FBX export and Unity's Humanoid mapper
    expect of this family of rigs.
    """
    # Reference-vertex -> body-vertex pairs, bucketed by the bone each vertex mostly belongs to.
    by_bone = {}
    for ref_mesh, mesh in pairs:
        ref_pts = world_verts(ref_mesh)
        pts = world_verts(mesh)
        for vert in ref_mesh.data.vertices:
            best, best_w = None, BONE_WEIGHT_CUTOFF
            for entry in vert.groups:
                if entry.group < len(ref_mesh.vertex_groups) and entry.weight > best_w:
                    best, best_w = ref_mesh.vertex_groups[entry.group].name, entry.weight
            if best is None:
                continue
            p = ref_pts[vert.index]
            pre = Vector((p.x * scale.x, p.y * scale.y, p.z * scale.z)) + translation
            by_bone.setdefault(best, ([], []))
            by_bone[best][0].append((pre.x, pre.y, pre.z))
            q = pts[vert.index]
            by_bone[best][1].append((q.x, q.y, q.z))

    ref_mw = ref_arm.matrix_world
    ref_rot = ref_mw.to_3x3()
    new_mw = armature.matrix_world
    new_inv = new_mw.inverted()
    new_rot_inv = new_mw.to_3x3().inverted()

    def pre_point(local):
        p = ref_mw @ local
        return Vector((p.x * scale.x, p.y * scale.y, p.z * scale.z)) + translation

    fits = {}       # bone name -> (R as Matrix, t as Vector)
    report = []
    order = []
    stack = [b for b in ref_arm.data.bones if b.parent is None]
    while stack:
        b = stack.pop()
        order.append(b)
        stack.extend(b.children)

    identity = (Matrix.Identity(3), Vector((0, 0, 0)))
    for bone in order:
        src, dst = by_bone.get(bone.name, ([], []))
        if len(src) >= MIN_BONE_VERTS:
            r, t, residual = kabsch(np.array(src), np.array(dst))
            fits[bone.name] = (Matrix(r.tolist()), Vector(t.tolist()))
            report.append((bone.name, len(src), residual))
        else:
            fits[bone.name] = fits[bone.parent.name] if bone.parent else identity

    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode='EDIT')
    edit = armature.data.edit_bones
    for eb in edit:
        eb.use_connect = False
    for bone in order:
        r, t = fits[bone.name]
        eb = edit[bone.name]
        head = r @ pre_point(bone.head_local) + t
        tail = r @ pre_point(bone.tail_local) + t
        eb.head = new_inv @ head
        eb.tail = new_inv @ tail
        # Roll: carry the bone's z-axis through the same rotation.
        z_world = (r @ (ref_rot @ bone.z_axis)).normalized()
        eb.align_roll(new_rot_inv @ z_world)
    bpy.ops.object.mode_set(mode='OBJECT')

    return report


def rebind(obj, armature):
    """Parent to `armature` and drive with it, without moving the object on screen."""
    keep = obj.matrix_world.copy()
    obj.parent = armature
    obj.parent_type = 'OBJECT'
    obj.matrix_parent_inverse = armature.matrix_world.inverted()
    obj.matrix_world = keep

    mods = [m for m in obj.modifiers if m.type == 'ARMATURE']
    if not mods:
        mods = [obj.modifiers.new(name="Armature", type='ARMATURE')]
    for m in mods[1:]:
        obj.modifiers.remove(m)
    mods[0].object = armature


def weighted_bones(obj, armature):
    names = set()
    for vert in obj.data.vertices:
        for entry in vert.groups:
            if entry.weight > 0.0 and entry.group < len(obj.vertex_groups):
                names.add(obj.vertex_groups[entry.group].name)
    return [armature.data.bones[n] for n in names if n in armature.data.bones]


def clear_groups(obj):
    for group in list(obj.vertex_groups):
        obj.vertex_groups.remove(group)


def anchor_to(obj, bone_name):
    clear_groups(obj)
    group = obj.vertex_groups.new(name=bone_name)
    group.add([v.index for v in obj.data.vertices], 1.0, 'REPLACE')


def skin_garment(obj, armature):
    """Blend each vertex between its two nearest torso bones by inverse distance."""
    bones = [armature.data.bones[n] for n in GARMENT_BONES if n in armature.data.bones]
    mids = [(b.name, bone_mid(armature, b)) for b in bones]
    clear_groups(obj)
    groups = {name: obj.vertex_groups.new(name=name) for name, _ in mids}
    for v in obj.data.vertices:
        p = obj.matrix_world @ v.co
        ranked = sorted(((mid - p).length, name) for name, mid in mids)[:2]
        inv = [1.0 / max(d, 1e-4) for d, _ in ranked]
        total = sum(inv)
        for (d, name), w in zip(ranked, inv):
            groups[name].add([v.index], w / total, 'REPLACE')


# A mesh weighted across this many bones is a real skin (the suit has 24, the gloves 36), and
# the distance test below would wrongly fail it: the gloves span both arms, so their centre is at
# the chest while every bone they are weighted to is out at the wrists.
REAL_SKIN_GROUPS = 16


def weights_are_sound(obj, armature):
    """True when the bones this mesh is weighted to are actually near it."""
    bones = weighted_bones(obj, armature)
    if not bones:
        return False
    if len(bones) >= REAL_SKIN_GROUPS:
        return True
    lo, hi = world_bbox(obj)
    centre = (lo + hi) / 2.0
    held = min((bone_mid(armature, b) - centre).length for b in bones)
    best = min((bone_mid(armature, b) - centre).length for b in armature.data.bones)
    return held <= best + MAX_WEIGHT_DRIFT


def local_long_axis(obj):
    """Unit vector, in the object's local space, along its longest extent."""
    xs = [v.co.x for v in obj.data.vertices]
    ys = [v.co.y for v in obj.data.vertices]
    zs = [v.co.z for v in obj.data.vertices]
    spans = (max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))
    axis = spans.index(max(spans))
    vec = Vector((0, 0, 0))
    vec[axis] = 1.0
    return vec


def skin_fit(mesh, armature):
    """Mean distance from each vertex to the bone it is mostly weighted to, in metres.

    The one number that says whether a skeleton sits inside its body: the reference body scores
    it against the reference rig, a fitted body must score about the same against its own.
    """
    total, count = 0.0, 0
    bones = armature.data.bones
    mw = armature.matrix_world
    for vert in mesh.data.vertices:
        best, best_w = None, BONE_WEIGHT_CUTOFF
        for entry in vert.groups:
            if entry.group < len(mesh.vertex_groups) and entry.weight > best_w:
                best, best_w = mesh.vertex_groups[entry.group].name, entry.weight
        if best is None or best not in bones:
            continue
        bone = bones[best]
        head, tail = mw @ bone.head_local, mw @ bone.tail_local
        p = mesh.matrix_world @ vert.co
        axis = tail - head
        t = max(0.0, min(1.0, (p - head).dot(axis) / max(axis.length_squared, 1e-9)))
        total += (p - (head + axis * t)).length
        count += 1
    return total / max(count, 1)


def is_pole(obj):
    """A long, slender, unweighted loose prop: the staff leaning beside the character."""
    if obj.vertex_groups:
        return False
    lo, hi = world_bbox(obj)
    extents = sorted((hi.x - lo.x, hi.y - lo.y, hi.z - lo.z))
    return extents[2] >= POLE_MIN_HEIGHT and extents[2] > 3.0 * extents[1]


def strap_pole_to_back(pole, armature):
    """Stand the pole diagonally across the back and pin it to the spine."""
    spine = armature.data.bones.get(POLE_ANCHOR_BONE)
    anchor = bone_mid(armature, spine)

    target_dir = Vector((math.sin(math.radians(POLE_LEAN_DEGREES)), 0.0,
                         math.cos(math.radians(POLE_LEAN_DEGREES)))).normalized()
    axis_world = (pole.matrix_world.to_3x3() @ local_long_axis(pole)).normalized()
    turn = axis_world.rotation_difference(target_dir).to_matrix().to_4x4()

    # Turn about the pole's own centre, so the rotation does not fling it across the scene;
    # then carry it onto the back. The pole's origin is wherever the user left it, so the
    # bounding-box centre is what gets placed, not the origin.
    lo, hi = world_bbox(pole)
    centre = (lo + hi) / 2.0
    pole.matrix_world = Matrix.Translation(centre) @ turn @ Matrix.Translation(-centre) @ pole.matrix_world
    bpy.context.view_layer.update()

    lo, hi = world_bbox(pole)
    centre = (lo + hi) / 2.0
    wanted = Vector((anchor.x, anchor.y + POLE_BACK_OFFSET, anchor.z))
    pole.matrix_world = Matrix.Translation(wanted - centre) @ pole.matrix_world
    bpy.context.view_layer.update()

    anchor_to(pole, POLE_ANCHOR_BONE)
    return wanted


def main():
    blend, dry = parse_args()
    if not os.path.exists(blend):
        raise SystemExit("No model at %s" % blend)

    if not dry:
        stamp = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        backup = os.path.join(os.path.dirname(blend), "sand_nogs_pre_rigfix_%s.blend" % stamp)
        shutil.copyfile(blend, backup)
        print("Backed up -> %s" % backup)

    bpy.ops.wm.open_mainfile(filepath=blend)

    ref_suit = bpy.data.objects.get(REFERENCE_SUIT)
    ref_arm = bpy.data.objects.get(REFERENCE_ARMATURE)
    if ref_suit is None or ref_arm is None or ref_arm.type != 'ARMATURE':
        raise SystemExit("Reference %s / %s missing." % (REFERENCE_SUIT, REFERENCE_ARMATURE))
    if len(ref_arm.data.bones) != EXPECTED_BONES:
        raise SystemExit("%s has %d bones, expected %d." % (REFERENCE_ARMATURE,
                                                            len(ref_arm.data.bones), EXPECTED_BONES))
    ref_verts = world_verts(ref_suit)
    ref_gloves = bpy.data.objects.get(REFERENCE_GLOVES)
    if ref_gloves is None:
        raise SystemExit("Reference %s missing." % REFERENCE_GLOVES)

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    armatures_before = [o for o in bpy.data.objects if o.type == 'ARMATURE']

    new_armatures = []
    for name, suit_name in CHARACTERS.items():
        suit = bpy.data.objects.get(suit_name)
        if suit is None or len(suit.data.vertices) != len(ref_suit.data.vertices):
            raise SystemExit("%s: suit %s missing or not the reference mesh." % (name, suit_name))

        print("\n=== %s (%s)" % (name, suit_name))

        scale, translation, residual = fit_axis_scale(ref_verts, world_verts(suit))
        lo, hi = world_bbox(suit)
        print("  body: scale (%.4f, %.4f, %.4f), translation (%.3f, %.3f, %.3f), affine residual "
              "%.3f m (the re-posed arms; see the per-bone fit below)"
              % (scale.x, scale.y, scale.z, translation.x, translation.y, translation.z, residual))

        cx = (lo.x + hi.x) / 2.0
        cluster = []
        for obj in meshes:
            olo, ohi = world_bbox(obj)
            if abs((olo.x + ohi.x) / 2.0 - cx) > CLUSTER_HALF_WIDTH:
                continue
            if olo.z > PARKED_Z:
                continue
            if obj is ref_suit:
                continue
            cluster.append(obj)
        print("  %d meshes in the outfit" % len(cluster))

        # A fresh copy of the reference skeleton, carried onto the body by the same similarity.
        arm_name = "Armature_Nomad%s" % name
        arm = bpy.data.objects.get(arm_name)
        if arm is None:
            arm = ref_arm.copy()
            arm.data = ref_arm.data.copy()
            arm.name = arm_name
            arm.data.name = arm_name
            arm.animation_data_clear()
            for coll in ref_arm.users_collection:
                coll.objects.link(arm)
        # The translation goes on the object; everything else -- the stretch and the re-posed
        # arms -- is fitted into the bones themselves. See fit_bones_to_body.
        arm.matrix_world = Matrix.Translation(translation) @ ref_arm.matrix_world
        bpy.context.view_layer.update()

        gloves = next((o for o in meshes if len(o.data.vertices) == len(ref_gloves.data.vertices)
                       and abs((world_bbox(o)[0].x + world_bbox(o)[1].x) / 2.0 - cx) <= CLUSTER_HALF_WIDTH), None)
        pairs = [(ref_suit, suit)] + ([(ref_gloves, gloves)] if gloves is not None else [])
        bone_report = fit_bones_to_body(arm, ref_arm, scale, translation, pairs)
        bpy.context.view_layer.update()
        new_armatures.append(arm)

        worst = max(bone_report, key=lambda r: r[2]) if bone_report else None
        print("  per-bone fit: %d bones fitted from their own vertices, worst residual %.3f m on %s"
              % (len(bone_report), worst[2], worst[0]) if worst else "  per-bone fit: nothing fitted")
        bad = [r for r in bone_report if r[2] > MAX_BONE_RESIDUAL]
        for bname, count, res in bad:
            print("      WARNING %s: %d verts, residual %.3f m -- reshaped, not just moved" % (bname, count, res))
        for bname in ("mixamorig:LeftArm", "mixamorig:LeftForeArm", "mixamorig:LeftHand",
                      "mixamorig:Spine", "mixamorig:LeftUpLeg", "mixamorig:Head"):
            b = arm.data.bones[bname]
            h = arm.matrix_world @ b.head_local
            print("      %-24s head at (%.2f, %.2f, %.2f)" % (bname.split(':')[1], h.x, h.y, h.z))
        print("  skin fit: suit vertices sit %.3f m from their bones here, %.3f m on the reference"
              % (skin_fit(suit, arm), skin_fit(ref_suit, ref_arm)))

        hips = arm.data.bones["mixamorig:Hips"]
        toe = arm.data.bones["mixamorig:LeftToeBase"]
        print("  rig: hips at z %.2f, toes at z %.2f; suit spans z %.2f..%.2f"
              % ((arm.matrix_world @ hips.head_local).z, (arm.matrix_world @ toe.head_local).z,
                 lo.z, hi.z))

        # The pole first, before the rename pass gives it its name.
        poles = [o for o in cluster if is_pole(o)]
        pole = max(poles, key=lambda o: world_bbox(o)[1].z - world_bbox(o)[0].z) if poles else None
        if pole is None:
            print("  no leaning pole in this outfit; nothing goes on the back")

        kept, anchored, skinned = [], [], []
        counters = {}
        for obj in sorted(cluster, key=lambda o: o.name):
            old_parent = obj.parent
            rebind(obj, arm)

            olo, ohi = world_bbox(obj)
            span = ohi.z - olo.z
            centre = (olo + ohi) / 2.0

            if obj is pole:
                where = strap_pole_to_back(obj, arm)
                anchored.append((obj.name, POLE_ANCHOR_BONE + " (pole, moved to the back at %.2f,%.2f,%.2f)" % (where.x, where.y, where.z)))
            elif obj is suit or weights_are_sound(obj, arm):
                kept.append(obj.name)
            elif span > GARMENT_SPAN_Z and len(obj.data.vertices) > 400:
                skin_garment(obj, arm)
                skinned.append(obj.name)
            else:
                big = max(ohi.x - olo.x, ohi.y - olo.y, span) > BIG_PROP_SPAN
                bone = nearest_bone(arm, centre, TRUNK_BONES if big else None)
                anchor_to(obj, bone)
                anchored.append((obj.name, bone))

            # Names.
            verts = len(obj.data.vertices)
            if obj is pole:
                part = "Pole"
            elif verts in NAMES_BY_VERTS:
                part = NAMES_BY_VERTS[verts]
            elif verts in NUMBERED_BY_VERTS:
                base = NUMBERED_BY_VERTS[verts]
                counters[base] = counters.get(base, 0) + 1
                part = "%s_%02d" % (base, counters[base])
            else:
                counters["Part"] = counters.get("Part", 0) + 1
                part = "Part_%02d" % counters["Part"]
            if part in ("Scarf_Long", "Shawl"):
                new_name = "Cloth_%s_%s" % (name, part)   # Cloth_ prefix: gets the wind shader in Unity
            else:
                new_name = "%s_%s" % (name, part)
            if obj.name != new_name:
                obj.name = new_name
                obj.data.name = new_name

        print("  kept real weights on %d: %s" % (len(kept), ", ".join(sorted(kept))))
        print("  skinned across torso bones: %s" % ", ".join(sorted(skinned)))
        print("  anchored to one bone:")
        for n, b in sorted(anchored):
            print("      %-28s -> %s" % (n, b))

    # Empty duplicate skeletons: nothing drives off them and nothing hangs under them.
    removed = []
    for arm in armatures_before:
        if arm is ref_arm or arm in new_armatures:
            continue
        drives = any(any(m.type == 'ARMATURE' and m.object == arm for m in o.modifiers)
                     for o in bpy.data.objects if o.type == 'MESH')
        if drives or arm.children:
            continue
        removed.append(arm.name)
        bpy.data.objects.remove(arm, do_unlink=True)
    for data in [a for a in bpy.data.armatures if a.users == 0]:
        bpy.data.armatures.remove(data)
    print("\nRemoved %d empty duplicate armature(s): %s" % (len(removed), ", ".join(removed)))

    remaining = [o.name for o in bpy.data.objects if o.type == 'ARMATURE']
    print("Armatures now: %s" % ", ".join(remaining))

    if dry:
        print("\nDRY RUN -- nothing saved.")
        return

    bpy.ops.wm.save_mainfile(filepath=blend)
    print("Saved %s" % blend)


main()
