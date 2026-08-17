"""Turn the shipped `dune_rat.fbx` into a maintainable `dune_rat.blend` source.

The Dune Rat arrived as an FBX and nothing else -- no .blend anywhere in the
repo, so the round-trip through FBX is the only history the rig has. That
round-trip cost it three things, and this script puts them back:

  1. **The IK constraints.** The rig carries a full control layer --
     `IK_back.L/R`, `IK_front.L/R`, the `hoof_*` toe bones under them, and the
     `pole_*` knee/elbow directors -- but FBX stores no constraints, so all of
     it imported as loose bones driving nothing. That matters more here than it
     would on a hand-keyed rig: `hoof_B.*` and `hoof_F.*` are *deform* bones
     carrying the foot and hand geometry, and they hang off the IK bones, not
     off the leg chain. Without the constraints, rotating a femur moves the
     shin and leaves the foot behind.

  2. **A sane object transform.** The armature arrived rotated 180 degrees
     about Z with the mesh carrying an unapplied 0.3935 scale, so nothing in
     the file was measured in anything. Both are baked out here.

  3. **Shipping placement.** Origin between the feet on the ground plane,
     -Y forward (the library convention; the exporter's axis conversion maps
     that onto Unity's +Z), and real metres.

## The one rig change

`IK_back.L/R` and `IK_front.L/R` were parented to `root`. That is wrong for
foot IK and it is not a cosmetic complaint: `root` is the body bone, so the
feet travelled with the hips and there was no pose in which a foot could stay
planted while the body moved over it -- which is the whole job of a foot IK
target. They are unparented here. Bone *names* are left exactly as the author
made them, misspellings (`metarsal`, `pole_fromt`) included, because the
vertex groups are keyed to them.

## Pole angles are measured, not guessed

A pole target rotates the whole chain about the target axis, and the right
angle depends on how the author happened to place the pole bone. Rather than
eyeball it, `tune_pole_angles` puts every IK target on its own rest position --
where, by construction, the chain tip already is -- sweeps the pole angle, and
keeps whichever angle reproduces the rest pose most closely. If no pole angle
beats simply having no pole at all, the pole target is dropped for that limb.
The residuals are printed; anything above a millimetre or so means the chain
is fighting the pole and the limb will pop the first time it is posed.

    blender --background --python dune_rat_rig.py
"""

import math
import os

import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))

# Walk up to the Unity project root rather than counting parent directories --
# the library has already been moved once.
REPO = HERE
while REPO != os.path.dirname(REPO) and not os.path.isdir(
        os.path.join(REPO, "ProjectSettings")):
    REPO = os.path.dirname(REPO)

# The author's untouched export, preserved out of git history (it landed on
# `main` as `Assets/Game/Art/Models/Creatures/rotte.fbx` in a0594505).
#
# This deliberately does NOT read the shipped
# `Creatures/Organic/DuneRat/dune_rat.fbx`. That path is what
# `dune_rat_export.py` *writes*, and pointing this script at it makes the
# pipeline a loop that eats its own output: run the two in sequence twice and
# the animal is normalised a second time -- yawed another 180 degrees, so it
# faces backwards, and rescaled against its own shipping length. That happened
# once and it is exactly the kind of failure that leaves no error behind, so
# `refuse_if_already_built` below checks for it as well.
FBX = os.path.join(REPO, "Assets", "Game", "Art", "Models", "_backups~",
                   "dune_rat_original.fbx")
DST = os.path.join(HERE, "dune_rat.blend")
PALETTE = os.path.abspath(os.path.join(HERE, "..", "..", "palette.blend"))

ARM = "Arm_DuneRat"
MESH = "Mesh_DuneRat"

# Nose to tail tip, in metres. The animal stands about 1.25 m at the ears on
# this figure -- a knee-high-plus desert biped, smaller than the 5.5 m Vrescal
# and large enough to be a real threat rather than vermin.
SHIP_LENGTH = 2.6

# (constrained bone, IK target, pole target, chain length)
#
# The chain tip is the constrained bone's *tail*, and each IK bone's head sits
# exactly on it in the rest pose -- that is how the author built it, and it is
# what lets the pole tuning below use the rest pose as its reference.
IK_CHAINS = [
    ("metarsal.L",   "IK_back.L",   "pole_back.L",   3),
    ("metarsal.R",   "IK_back.R",   "pole_back.R",   3),
    ("metacarpal.L", "IK_front.L",  "pole_fromt.L",  3),
    ("metacarpal.R", "IK_front.R",  "pole_fromt.R",  3),
]

CONTROL_ROOTS = ["IK_back.L", "IK_back.R", "IK_front.L", "IK_front.R"]
POLES = ["pole_back.L", "pole_back.R", "pole_fromt.L", "pole_fromt.R"]


# ---------------------------------------------------------------------------
# Import and cleanup
# ---------------------------------------------------------------------------

def import_fbx():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if not os.path.exists(FBX):
        raise SystemExit("No FBX at %s" % FBX)
    bpy.ops.import_scene.fbx(filepath=FBX)

    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    if len(arms) != 1 or len(meshes) != 1:
        raise SystemExit("Expected one armature and one mesh, got %d and %d"
                         % (len(arms), len(meshes)))
    arm, mesh = arms[0], meshes[0]
    refuse_if_already_built(arm, mesh)
    arm.name = arm.data.name = ARM
    mesh.name = mesh.data.name = MESH
    return arm, mesh


def refuse_if_already_built(arm, mesh):
    """Bail out if handed something this script has already processed.

    Normalising is not idempotent -- it yaws 180 degrees and rescales against
    the measured length -- so running it twice produces an animal facing
    backwards at the wrong size, with nothing to show for it but a slightly
    different number in the log. Two fingerprints of the author's raw export
    have to be present, and both disappear the moment this script has run:
    the armature is still called `Armature.001` rather than `Arm_DuneRat`, and
    the mesh still carries its unapplied object scale.
    """
    if arm.name == ARM or mesh.name == MESH:
        raise SystemExit(
            "%s is already a built model (found '%s'/'%s').\n"
            "This script reads the author's ORIGINAL export and must never be "
            "pointed at dune_rat_export.py's output." % (FBX, arm.name, mesh.name))
    if abs(mesh.scale.x - 1.0) < 1e-6 and arm.matrix_world.is_identity:
        raise SystemExit(
            "%s has an applied mesh scale and an identity armature -- it has "
            "already been normalised. Re-running would yaw it 180 degrees "
            "again and rescale it against its own shipping length." % FBX)


def edit_mode(arm):
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')


def object_mode():
    bpy.ops.object.mode_set(mode='OBJECT')


def strip_leaf_bones(arm, mesh):
    """Delete the `*_end` bones the FBX round-trip invented.

    Blender's FBX exporter adds a leaf bone past every chain tip so the tip has
    a length; re-importing turns them into real bones. They carry no vertex
    group -- checked, not assumed -- and re-exporting with
    `add_leaf_bones=False` would otherwise stack a second set on top.
    """
    groups = {g.name for g in mesh.vertex_groups}
    doomed = [b.name for b in arm.data.bones if b.name.endswith("_end")]
    weighted = [n for n in doomed if n in groups]
    if weighted:
        raise SystemExit("Leaf bones carry vertex weights, refusing to delete: "
                         + ", ".join(weighted))
    edit_mode(arm)
    for name in doomed:
        arm.data.edit_bones.remove(arm.data.edit_bones[name])
    object_mode()
    return doomed


def free_ik_roots(arm):
    """Unparent the foot/hand IK targets from `root`. See the module docstring."""
    edit_mode(arm)
    moved = []
    for name in CONTROL_ROOTS:
        eb = arm.data.edit_bones[name]
        if eb.parent is not None:
            moved.append((name, eb.parent.name))
            eb.use_connect = False
            eb.parent = None
    object_mode()
    return moved


# ---------------------------------------------------------------------------
# Placement
# ---------------------------------------------------------------------------

def transform_armature_data(arm, matrix):
    if hasattr(arm.data, "transform"):
        arm.data.transform(matrix)
        return
    edit_mode(arm)                       # 4.x fallback
    for eb in arm.data.edit_bones:
        eb.transform(matrix)
    object_mode()


def normalise(arm, mesh):
    """Bake both object transforms out, then place the animal for shipping.

    Everything downstream -- the pole tuning, the whole of `dune_rat_anim.py`,
    and every measurement in the build record -- reads bone rest positions
    straight out of armature space, so armature space has to *be* world space,
    in metres, the right way round. Doing it here rather than in the exporter
    (which is what the Vrescal does) is the opposite choice from that model,
    and deliberately: the Vrescal's .blend is a hand sculpt whose working scale
    is the author's, whereas this file's scale is an accident of an FBX.
    """
    mesh_world = mesh.matrix_world.copy()
    arm_world = arm.matrix_world.copy()

    # 1. Collapse both objects onto the identity, geometry unmoved.
    transform_armature_data(arm, arm_world)
    arm.matrix_world = Matrix.Identity(4)

    mesh.data.transform(mesh_world)
    if mesh_world.determinant() < 0.0:
        mesh.data.flip_normals()         # a mirrored parent turns them inside out
    mesh.matrix_parent_inverse = Matrix.Identity(4)
    mesh.matrix_basis = Matrix.Identity(4)

    bpy.context.view_layer.update()

    # 2. Measure the animal as it now stands, then place it.
    #
    # Off the vertices, not off `bound_box`: the bounding box is cached against
    # the mesh as it was before `data.transform`, and trusting it put the sole
    # plane 5 cm underground -- which is exactly the kind of error that only
    # shows up as the animal skating slightly above the sand.
    verts = [v.co for v in mesh.data.vertices]
    length = max(v.y for v in verts) - min(v.y for v in verts)
    ground = min(v.z for v in verts)
    factor = SHIP_LENGTH / length

    yaw = Matrix.Rotation(math.pi, 4, 'Z')            # nose was +Y; -Y forward
    transform_armature_data(arm, yaw)
    mesh.data.transform(yaw)

    # The pivot goes where the animal's weight actually lands. This is a biped
    # -- the forelimbs hang clear of the ground -- so that is the midpoint of
    # the two hind toe tips, not the middle of the body. A NavMeshAgent steers
    # the origin, and steering anything else on a two-legged animal reads as
    # the creature pivoting around a point in mid-air.
    toes = [arm.data.bones[n].tail_local for n in ("hoof_B.L", "hoof_B.R")]
    pivot = Vector((0.0, (toes[0].y + toes[1].y) * 0.5, ground))

    place = Matrix.Diagonal((factor, factor, factor, 1.0)) \
        @ Matrix.Translation(-pivot)
    transform_armature_data(arm, place)
    mesh.data.transform(place)
    bpy.context.view_layer.update()

    return factor, pivot, length


# ---------------------------------------------------------------------------
# Normals
# ---------------------------------------------------------------------------

def repair_normals(mesh):
    """Make the winding consistently outward, and say how bad it was.

    The author's mesh ships with **688 of its 1836 faces wound backwards** --
    not a uniform flip, a mixture. That is the classic cause of a model that
    renders see-through from one side: backface culling removes the near
    surface and you look straight through it into the inside of the far one.
    This was inherited, not introduced -- the counts are identical in the
    original `rotte.fbx` on `main` -- but it has to be fixed here, because
    this is the only place upstream of Unity that can fix it.

    The trap is the order of the two operations. The mesh also carries
    **custom split normals**, and those override the face normals for
    everything you can see. Recalculating outside without clearing them first
    appears to do nothing at all: the winding changes, the shading does not,
    and it is very easy to re-export a file that looks identical and conclude
    the recalculation failed. Clear first, then recalculate.

    Winding is also what culling actually keys off -- not the normals -- so
    clearing the custom normals alone would fix the shading and leave the model
    just as transparent.

    The 112 non-manifold edges are left alone on purpose. They are open
    boundaries in hand-modelled geometry (ear membranes, the mouth interior),
    and welding them shut is a change to the author's sculpt, not a repair.
    Recalculation copes with them; the signed volume afterwards is the check
    that it did.
    """
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)

    before = [tuple(p.vertices) for p in mesh.data.polygons]
    had_custom = mesh.data.has_custom_normals

    if had_custom:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()

    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    mesh.select_set(False)

    flipped = sum(1 for i, p in enumerate(mesh.data.polygons)
                  if before[i] != tuple(p.vertices))
    return had_custom, flipped, signed_volume(mesh)


def signed_volume(mesh):
    """Positive means the surface is wound outward. Cheap global sanity check."""
    total = 0.0
    verts = mesh.data.vertices
    for poly in mesh.data.polygons:
        idx = poly.vertices
        for i in range(1, len(idx) - 1):
            a = verts[idx[0]].co
            b = verts[idx[i]].co
            c = verts[idx[i + 1]].co
            total += a.dot(b.cross(c)) / 6.0
    return total


# ---------------------------------------------------------------------------
# Materials
# ---------------------------------------------------------------------------

def link_palette_material(mesh, name):
    """Link one material out of palette.blend, the way every model here does.

    The FBX's own `Material.002` is a bare Principled node plus an image
    texture pointing at a directory -- the texture never shipped. Dropping it
    for a palette hide material is what makes the animal match the Vrescal and
    the rest of the desert.
    """
    if not os.path.exists(PALETTE):
        raise SystemExit("No palette at %s" % PALETTE)
    with bpy.data.libraries.load(PALETTE, link=True) as (src, dst):
        if name not in src.materials:
            raise SystemExit("%s not in the palette; have %s"
                             % (name, ", ".join(sorted(src.materials))))
        dst.materials = [name]
    mat = bpy.data.materials[name]
    mesh.data.materials.clear()
    mesh.data.materials.append(mat)
    for img in list(bpy.data.images):
        if img.users == 0:
            bpy.data.images.remove(img)
    return mat


# ---------------------------------------------------------------------------
# IK
# ---------------------------------------------------------------------------

def rest_snapshot(arm, bones):
    return {n: (arm.data.bones[n].head_local.copy(),
                arm.data.bones[n].tail_local.copy()) for n in bones}


def chain_bones(arm, tip, count):
    out, bone = [], arm.data.bones[tip]
    for _ in range(count):
        out.append(bone.name)
        bone = bone.parent
        if bone is None:
            break
    return out


def add_ik(arm):
    for tip, target, pole, count in IK_CHAINS:
        pbone = arm.pose.bones[tip]
        for c in list(pbone.constraints):
            pbone.constraints.remove(c)
        ik = pbone.constraints.new('IK')
        ik.target = arm
        ik.subtarget = target
        ik.chain_count = count
        ik.use_tail = True
        ik.use_stretch = False
        ik.pole_target = arm
        ik.pole_subtarget = pole
        ik.pole_angle = 0.0


def chain_error(arm, deps, names, rest):
    """RMS distance, in metres, between the solved chain and the rest pose."""
    deps.update()
    ev = arm.evaluated_get(deps)
    total, n = 0.0, 0
    for name in names:
        pb = ev.pose.bones[name]
        h, t = rest[name]
        total += (pb.head - h).length_squared + (pb.tail - t).length_squared
        n += 2
    return math.sqrt(total / n)


def clear_pose(arm):
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def drop_pole_targets(arm):
    """Solve without pole targets, and say why.

    A pole target is the usual way to stop an IK chain flipping, and the usual
    way to *cause* a flip if its angle is wrong. It is not needed here, for a
    reason specific to this rig: these are three-bone chains, not two, so the
    solver is under-determined and Blender resolves it by staying as close to
    the pose it starts from as it can. That pose is the author's digitigrade
    rest bend -- knee forward, ankle back -- and keeping it is precisely what
    is wanted. Adding a pole overrides that with a plane derived from a control
    bone the author left at an arbitrary angle.

    `probe_bend` below is what actually establishes this: it drives each target
    through more than a full stride's worth of displacement and checks the
    chain never flips. The pole bones stay in the file, unwired, for an
    animator who wants explicit knee control later.
    """
    for tip, _target, _pole, _count in IK_CHAINS:
        ik = arm.pose.bones[tip].constraints[0]
        ik.pole_target = None
        ik.pole_subtarget = ""
        ik.pole_angle = 0.0


def probe_bend(arm):
    """Drive every IK target through its stride envelope and watch the joints.

    Two things have to hold at every sample or the limb will pop mid-stride:
    the middle joint must stay on the same side of the hip-to-foot line it
    started on (no knee inversion), and the solved tip must actually reach the
    target (no silent shortfall from a chain that cannot stretch).
    """
    clear_pose(arm)
    deps = bpy.context.evaluated_depsgraph_get()
    report = []

    for tip, target, _pole, count in IK_CHAINS:
        names = chain_bones(arm, tip, count)       # tip .. root of the chain
        mid = names[1]                             # fibula / radius
        top = names[-1]                            # femur / humerus
        rest = rest_snapshot(arm, names)
        hip = arm.data.bones[top].head_local
        tgt = arm.pose.bones[target]
        rest_tgt = arm.data.bones[target].matrix_local.copy()

        # Sign of the rest bend: which side of the hip->foot chord the mid
        # joint sits on, measured in the sagittal plane (y forward, z up).
        def bend_sign(mid_head, foot):
            chord = Vector((foot.y - hip.y, foot.z - hip.z))
            arm_v = Vector((mid_head.y - hip.y, mid_head.z - hip.z))
            return chord.x * arm_v.y - chord.y * arm_v.x

        rest_sign = bend_sign(rest[mid][0], rest[tip][1])
        flips, worst_reach = 0, 0.0

        # The envelope is a fraction of the limb's *own* reach. A fixed metre
        # figure asks the 0.29 m forelimb to cover the 0.99 m hind leg's stride
        # and then reports the arm as broken for failing to.
        span = sum(arm.data.bones[n].length for n in names)
        for dy in (-0.30, -0.15, 0.0, 0.15, 0.30):
            for dz in (0.0, 0.06, 0.14):
                tgt.matrix = Matrix.Translation(
                    (0.0, dy * span, dz * span)) @ rest_tgt
                deps.update()
                ev = arm.evaluated_get(deps)
                got = ev.pose.bones[tip].tail
                want = ev.pose.bones[target].head
                worst_reach = max(worst_reach, (got - want).length)
                s = bend_sign(ev.pose.bones[mid].head, got)
                if s * rest_sign <= 0.0:
                    flips += 1
        tgt.matrix_basis = Matrix.Identity(4)
        deps.update()
        report.append((tip, span, flips, worst_reach))
    clear_pose(arm)
    return report


# ---------------------------------------------------------------------------

def organise_bones(arm):
    """Split deform bones from controls so the rig can be posed by hand."""
    for name in POLES:
        arm.data.bones[name].use_deform = False      # directors, never skin
    try:
        for existing in list(arm.data.collections):
            arm.data.collections.remove(existing)
        deform = arm.data.collections.new("Deform")
        controls = arm.data.collections.new("Controls")
        ctl = set(CONTROL_ROOTS) | set(POLES)
        for bone in arm.data.bones:
            (controls if bone.name in ctl else deform).assign(bone)
    except Exception as exc:                          # pragma: no cover
        print("  (bone collections skipped: %s)" % exc)


def main():
    arm, mesh = import_fbx()
    leaves = strip_leaf_bones(arm, mesh)
    print("Removed %d FBX leaf bones: %s" % (len(leaves), ", ".join(leaves)))

    moved = free_ik_roots(arm)
    for name, was in moved:
        print("Unparented %s from %s" % (name, was))

    factor, pivot, length = normalise(arm, mesh)
    print("Placed: %.3f working units -> %.2f m (x%.4f), pivot %s"
          % (length, SHIP_LENGTH, factor, tuple(round(v, 4) for v in pivot)))

    before_vol = signed_volume(mesh)
    had_custom, flipped, after_vol = repair_normals(mesh)
    print("Normals: custom split normals %s; recalculated outside, %d of %d "
          "faces were wound backwards" % ("cleared" if had_custom else "none",
                                          flipped, len(mesh.data.polygons)))
    print("         signed volume %.5f -> %.5f%s"
          % (before_vol, after_vol,
             "" if after_vol > 0 else "   <-- CHECK, still inside out"))

    mat = link_palette_material(mesh, "Mat_Hide_Sand_Pale")
    print("Material -> %s (linked from palette.blend)" % mat.name)

    add_ik(arm)
    drop_pole_targets(arm)
    for tip, span, flips, reach in probe_bend(arm):
        flag = "" if (flips == 0 and reach < 1e-3) else \
            "   <-- CHECK, this limb will pop mid-stride"
        print("IK %-14s reach %.3f m, %d flip(s) over 15 samples, worst miss "
              "%.5f m%s" % (tip, span, flips, reach, flag))

    organise_bones(arm)

    bpy.context.scene.render.fps = 30
    bpy.context.scene.frame_start = 1
    bpy.context.scene.frame_end = 1

    corners = [Vector(c) for c in mesh.bound_box]
    print("Shipping size: x %.3f..%.3f  y %.3f..%.3f  z %.3f..%.3f (metres)" % (
        min(c.x for c in corners), max(c.x for c in corners),
        min(c.y for c in corners), max(c.y for c in corners),
        min(c.z for c in corners), max(c.z for c in corners)))
    print("%d bones, %d vertex groups, %d verts"
          % (len(arm.data.bones), len(mesh.vertex_groups),
             len(mesh.data.vertices)))
    for name in ("root", "spine1", "spine4", "head",
                 "IK_back.L", "IK_back.R", "IK_front.L", "IK_front.R",
                 "hoof_B.L", "hoof_B.R", "femur.L", "femur.R"):
        b = arm.data.bones[name]
        print("   %-12s head %s tail %s len %.4f"
              % (name, tuple(round(v, 4) for v in b.head_local),
                 tuple(round(v, 4) for v in b.tail_local), b.length))

    bpy.ops.wm.save_as_mainfile(filepath=DST)
    print("Saved %s" % DST)


main()
