"""Build ornithopter_worn.blend — the wing pack as it looks WORN, not carried.

The held item is `wing_pack_folded.blend`: the whole craft folded into a bundle.
This is the other thing the same item has to be — what you see strapped to a
player's back. A folded aircraft on somebody's shoulders reads as luggage, so
the worn form throws the aircraft away and keeps the two things that say
"flight": the webbed wings, and the spoked shoulder mechanics that beat them.

Gone, deliberately: the fuselage, the nose, the tail boom, the tail fan, the
prone cradle, the shoulder pylons and the tie-rod struts. Everything that held
the machine together around a rider it no longer has. What is left hangs off the
expedition rig's own hardware instead — see the frame note below.

Derived from `dune_ornithopter.blend`, which carries hand edits and is NEVER
written to. This script opens it, culls, poses the rig in memory, bakes, and
saves the result to a NEW file. Pose conventions (axis table, per-side Z sign)
come from `dune_ornithopter_BUILD.md`.

**Nothing is joined.** `wing_pack_folded.py` bakes to one mesh because the held
bundle never articulates and never needs a part named; a worn wing does get
looked at, so its ten parts stay ten named objects.

## The frame — this is the whole reason the numbers below are what they are

Authored in the WEARER's frame, at true wearer scale, with the ORIGIN at the
expedition rig's lash rail: +X to the wearer's left in Blender terms, +Z up,
−Y forward. `WornSeat` puts a back item's origin on that rail, so this model's
origin lands there and its two shoulder pivots reach out along the rail's own
two protruding bars to their tips.

Measured off the game, not guessed (`PlayerCharacter.prefab` with the folded
`ExpeditionRig` on its spine, 2026-09-03), in the spine bone's frame, metres:

    lash rail tips      x = ±0.885, y = 0.630, z = −0.522   ← the mount
    upper arm joint     x = ±0.233, y = 0.637
    hip joint           x = ±0.143, y = −0.269
    ankle               x = ±0.228, y = −1.259

The rail is 1.79 m of bar across a 3 m astronaut's back and it does NOT fold
with the pack, so its ends stick out well past each flank at almost exactly
shoulder height. That is what the wings bolt to.

    # iterate on the pose, renders and exits without saving anything:
    blender --background dune_ornithopter.blend --python ornithopter_worn.py -- \
        --preview /tmp/worn.png [--view front|side|iso]

    # bake and write ornithopter_worn.blend (refuses to overwrite):
    blender --background dune_ornithopter.blend --python ornithopter_worn.py -- --commit

    # CONTROL RUN — the same parameters into a scratch path. Fingerprint that
    # against the shipped .blend (per-object vertex and polygon hash) and only
    # rebuild if they match; a difference means the file carries hand edits this
    # script would destroy. Done before the 2026-09-04 re-pose, and it is the
    # step that makes deleting the shipped file safe rather than reckless:
    blender --background dune_ornithopter.blend --python ornithopter_worn.py -- \
        --commit --out /tmp/control.blend
"""

import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
LIB = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, LIB)

DST = os.path.join(HERE, "ornithopter_worn.blend")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(flag, default=None):
    return argv[argv.index(flag) + 1] if flag in argv else default


PREVIEW = arg("--preview")
VIEW = arg("--view", "front")
COMMIT = "--commit" in argv

# Where --commit writes. Overridable so a CONTROL run — the same parameters into
# a scratch path, diffed against the shipped file to prove this script still
# reproduces it — can be done without going anywhere near the real one. Any
# rebuild of the shipped file starts with that control run.
DST = arg("--out", DST)

R = math.radians
N_DIGITS = 5

# ---------------------------------------------------------------- the wearer
# Where the rail's two protruding bars end, left and right of the spine, in
# metres. The shoulder pivot of each wing is placed exactly here.
ROOT_HALF = 0.885

# How far the wing reaches from that pivot to its furthest tip, in metres. Sized
# against the WEARER rather than against the aircraft: the rail sits 0.63 m above
# the spine bone and the soles about 1.45 m below it, so the ground is 2.08 m
# under the rail and that is the whole budget a hanging wing has.
#
# 2.775 since 2026-09-04, half again the 1.85 this shipped at (user: the worn
# wings read too small on the gear screen; "1.5 then, big wings go to the side,
# and can lay a bit on the ground"). The extra length is spent OUTBOARD rather
# than downward — see FLAP — because the gear screen looks at the wearer head
# on, so span is what reads there and droop is what runs out of ground. It is
# argable so a pose can be swept without editing the file.
TARGET_REACH = float(arg("--reach", 2.775))

# ---------------------------------------------------------------- worn pose
# Signs follow the rig's rule (dune_ornithopter_BUILD.md): a wing bone's local X
# and Y are already mirrored between sides so flap and twist take no per-side
# sign, while local Z is not and sweep and splay do.
# Re-posed 2026-09-04 with the 1.5x reach, and every number moved for a reason
# the render can be checked against — the poses were swept and looked at, not
# reasoned about (`--preview /tmp/x.png --view front`).
FLAP = float(arg("--flap", -52.0))    # shoulders: wings out to the SIDE, not down
SWEEP = float(arg("--sweep", 16.0))   # rearward rake, so a wing is not a slab
ROLL = float(arg("--roll", 38.0))     # turns each fan out of the fore-aft plane
SPLAY = float(arg("--splay", -105.0))  # fan OPEN: the web is the wing, not the spars
TWIST = float(arg("--twist", 14.0))   # web feathered, spars readable on the cloth

# Why those four moved, since three of them are pure look and the fourth is not:
#
#   FLAP  -72 -> -52. At 1.5x the old droop put the tip 2.38 m under the rail and
#         the ground is 2.08 m under it, so a third of a metre of wing swept
#         through the sand. Raising the shoulder spends the extra length
#         OUTBOARD instead: span 4.32 -> 5.51 m and the tip comes back to 1.61 m
#         down, half a metre clear of the soles. The gear screen looks at the
#         wearer head on, so span is the axis that reads there anyway.
#   SPLAY -52 -> -105. |SPLAY| IS the fan's opening angle — the digits are graded
#         across `SPLAY * (k - 0.30)`, so the total swing is exactly SPLAY. At 52
#         the fan is half shut and the wing reads as five bare spars with cloth
#         scalloped between them; at 105 the web closes into one continuous sail
#         and the spar tips stop protruding past the trailing edge. This is the
#         "make the fabric a larger part of the model" change (user, 2026-09-04)
#         and it costs nothing: no geometry moved, the fan just opened.
#   ROLL  35 -> 38, TWIST 18 -> 14. Both set how square the sail is to a head-on
#         camera, and a wing seen edge-on is a line. Swept: at ROLL 26 / TWIST 8
#         the panel turned nearly edge-on and the fabric collapsed to a sliver —
#         the flattest, worst read of the five poses tried. These two are worth
#         re-rendering rather than re-deriving if the pose is ever touched again.

# Which parts survive. The wing panels and the shoulder mechanics, nothing else.
KEEP = {
    "Mesh_Wing_L_Frame": "Mesh_OrniWorn_Wing_L_Frame",
    "Mesh_Wing_L_Web": "Mesh_OrniWorn_Wing_L_Web",
    "Mesh_Wing_R_Frame": "Mesh_OrniWorn_Wing_R_Frame",
    "Mesh_Wing_R_Web": "Mesh_OrniWorn_Wing_R_Web",
    "Mesh_Bearing_L": "Mesh_OrniWorn_Bearing_L",
    "Mesh_Bearing_R": "Mesh_OrniWorn_Bearing_R",
    "Mesh_DriveWheel_L": "Mesh_OrniWorn_Gear_L",
    "Mesh_DriveWheel_R": "Mesh_OrniWorn_Gear_R",
    "Mesh_Crank_L": "Mesh_OrniWorn_Crank_L",
    "Mesh_Crank_R": "Mesh_OrniWorn_Crank_R",
}

# The collar that grips the rail bar. Sized against the rail's own measured
# cross-section (0.164 x 0.148 m at its widest, buckles included) so the jaw
# closes round it rather than floating near it.
CLAMP_BORE_Y = 0.076       # half the opening fore-aft: the strap runs 0.134 wide
CLAMP_BORE_Z = 0.027       # half the opening vertically: the strap is 0.040 thick
CLAMP_WALL = 0.020
CLAMP_LENGTH = 0.130
CLAMP_X = 0.830            # centred inboard of the tip, over the buckle junction


def flush_edit_mode():
    """The source file was once saved mid-edit; flush so meshes are current."""
    for obj in bpy.data.objects:
        if obj.mode != 'OBJECT':
            bpy.context.view_layer.objects.active = obj
            bpy.ops.object.mode_set(mode='OBJECT')


def cull():
    """Drop everything but the parts in KEEP and the armature that poses them."""
    dropped = []
    for obj in list(bpy.data.objects):
        if obj.type == 'ARMATURE':
            continue
        if obj.name in KEEP:
            continue
        dropped.append(obj.name)
        bpy.data.objects.remove(obj, do_unlink=True)
    print("  culled %d part(s): %s" % (len(dropped), ", ".join(sorted(dropped))))


def rot(armature, bone_name, x=0.0, y=0.0, z=0.0):
    pb = armature.pose.bones[bone_name]
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = (R(x), R(y), R(z))


def apply_worn_pose():
    armature = bpy.data.objects["Arm_DuneOrnithopter"]
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode='POSE')

    for tag, s in (("R", 1), ("L", -1)):
        rot(armature, "Bone_Shoulder_%s" % tag, x=FLAP)
        # ROLL takes a per-side sign and DIGIT TWIST does not, even though both
        # are a rotation about a bone's local Y. Measured, not reasoned: with
        # the same sign on both arms the model's bounds came out asymmetric
        # (-1.51 to +1.86 at 40 degrees) while the digits stayed symmetric.
        # `dune_ornithopter_BUILD.md`'s table covers the digits and says local Y
        # needs no sign; that does not carry to `Bone_Arm`, and the only way to
        # tell is to print the bounds.
        rot(armature, "Bone_Arm_%s" % tag, y=ROLL * s, z=SWEEP * s)
        for i in range(N_DIGITS):
            # Graded so the fan half-closes rather than swinging rigidly: the
            # trailing digit travels furthest, which is what a resting wing does.
            k = i / (N_DIGITS - 1)
            rot(armature, "Bone_Digit_%s_%d" % (tag, i + 1),
                z=SPLAY * (k - 0.30) * s,
                y=TWIST * (0.35 + 0.65 * k))

    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()
    return armature


def pivots(armature):
    """World position of each posed shoulder pivot, before the bake."""
    out = {}
    for tag in ("L", "R"):
        pb = armature.pose.bones["Bone_Shoulder_%s" % tag]
        out[tag] = armature.matrix_world @ pb.head
    return out


def bake(armature):
    """Freeze the pose into the meshes and drop the rig."""
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']

    # Skinned panels: applying the Armature modifier freezes the deformation.
    # Modifier apply needs single-user data.
    for obj in meshes:
        mods = [m for m in obj.modifiers if m.type == 'ARMATURE']
        if not mods:
            continue
        if obj.data.users > 1:
            obj.data = obj.data.copy()
        bpy.context.view_layer.objects.active = obj
        for m in mods:
            bpy.ops.object.modifier_apply(modifier=m.name)

    # Bone-parented rigid parts: keep the posed world transform, lose the bone.
    for obj in meshes:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world

    bpy.data.objects.remove(armature, do_unlink=True)
    for obj in [o for o in bpy.data.objects if o.type != 'MESH']:
        bpy.data.objects.remove(obj, do_unlink=True)
    return [o for o in bpy.data.objects if o.type == 'MESH']


def reach(objs, pivot):
    """Furthest distance from `pivot` to any vertex of `objs`, in metres."""
    far = 0.0
    for obj in objs:
        m = obj.matrix_world
        for v in obj.data.vertices:
            far = max(far, ((m @ v.co) - pivot).length)
    return far


def place(objs, pivot, scale, target):
    """Scale `objs` about `pivot` by `scale`, then move `pivot` onto `target`.

    Written as a matrix on `matrix_world` rather than as object scale, because
    the parts have to come out at scale 1.0 with the transform in the mesh — and
    six of the source's meshes are placed twice off one datablock, so scaling
    the datablock would square the factor on the second placement.
    """
    m = (Matrix.Translation(target)
         @ Matrix.Diagonal((scale, scale, scale, 1.0))
         @ Matrix.Translation(-pivot))
    for obj in objs:
        obj.matrix_world = m @ obj.matrix_world


def flatten(objs):
    """Push every object transform into its mesh, repairing mirrored winding.

    The port side is a mirrored placement in the source assembly, so half these
    objects arrive with a negative-determinant matrix. Blender draws that
    correctly and the FBX carries the flip straight through to Unity, which then
    renders the mesh inside-out with nothing in the console — the same trap
    `_exportlib._unmirror` exists for. Caught by the determinant, not by eye.
    """
    flipped = []
    for obj in objs:
        if obj.data.users > 1:
            obj.data = obj.data.copy()
        inverted = obj.matrix_world.to_3x3().determinant() < 0.0
        obj.data.transform(obj.matrix_world)
        obj.matrix_world = Matrix.Identity(4)
        if inverted:
            bm = bmesh.new()
            bm.from_mesh(obj.data)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
            bm.to_mesh(obj.data)
            bm.free()
            flipped.append(obj.name)
    if flipped:
        print("  repaired winding on %d mirrored part(s): %s"
              % (len(flipped), ", ".join(sorted(flipped))))


def seat_origins(objs):
    """Give every baked part its side's shoulder pivot as its origin.

    A bake leaves everything at the world origin, which is what
    `wing_pack_folded.blend` ships and which is fine for one mesh. Twelve parts
    all pivoting about a point 0.9 m away is not: the pivot each of these
    actually turns about is the rail tip its wing hangs from, so that is where
    the origin goes and a hand rotation of a wing does the right thing.
    """
    for obj in objs:
        sign = -1.0 if centre(obj).x < 0.0 else 1.0
        pivot = Vector((sign * ROOT_HALF, 0.0, 0.0))
        obj.data.transform(Matrix.Translation(-pivot))
        obj.location = pivot


def build_clamps(coll, anchors):
    """The two jaws that grip the rail's bars, and the struts up to the wings.

    New geometry, and the only new geometry in the file. Without it the
    mechanics hang beside the bar touching nothing, which reads as a wing
    hovering next to the player rather than one bolted to their pack.

    The bore is not a guess. The rail's own mesh was binned along its axis
    (2026-09-03): every band reads 0.134 m fore-aft by 0.040 m thick, with the
    outermost 0.15 m of each end thickened by its loop buckle. So the jaw is a
    flat strap clamp, not a round collar, and it sits just inboard of the tip
    where the strap is clean.

    `anchors` is the MEASURED centre of each side's bearing yoke, taken after
    the pose and the placement. The strut is aimed at it rather than at a typed
    offset, because the flap swings the whole shoulder assembly down off the
    bar by an amount that changes with every pose tweak — a hard-coded strut
    would leave a visible gap the moment FLAP moved, and nothing would report it.
    """
    from _buildlib import Part  # noqa: E402  (import here: needs no new scene)

    mats = [bpy.data.materials[n] for n in
            ("Mat_Metal_Steel_Worn", "Mat_Metal_Brass_Tarnished")]
    STEEL, BRASS = 0, 1

    half = CLAMP_LENGTH * 0.5
    made = []
    for tag, sign in (("L", -1), ("R", 1)):
        jaw = Vector((sign * CLAMP_X, 0.0, 0.0))
        reach_v = anchors[tag] - jaw
        part = Part(mats)

        # Two plates above and below the strap. Open on both flanks, so the
        # strap visibly runs through rather than being threaded into a tube.
        for sz in (-1, 1):
            part.slab((-half, -CLAMP_BORE_Y, sz * CLAMP_BORE_Z),
                      (half, CLAMP_BORE_Y, sz * (CLAMP_BORE_Z + CLAMP_WALL)),
                      mat=STEEL)

        # The two bolts that pull the plates together, one fore and one aft of
        # the strap. Their ends are BURIED in the plates rather than tangent to
        # them: a cylinder cap flush with a face is two coincident surfaces.
        for sy in (-1, 1):
            part.cyl(center=(0.0, sy * (CLAMP_BORE_Y - 0.014), 0.0),
                     radius=0.011,
                     depth=(CLAMP_BORE_Z + CLAMP_WALL) * 2.0 - 0.010,
                     axis='Z', seg=8, mat=BRASS)

        # The strut down to the bearing. Started INSIDE the plate stack and run
        # a little past the yoke's centre, so both ends interpenetrate what they
        # meet instead of abutting it.
        length = reach_v.length + 0.055
        direction = reach_v.normalized()
        aim = direction.to_track_quat('Z', 'Y').to_matrix().to_4x4()
        part.cyl(center=tuple(direction * (length * 0.5 - 0.030)),
                 radius=0.036, radius_top=0.026, depth=length, axis='Z',
                 seg=12, mat=STEEL, rot=aim)

        # A gusset where the strut leaves the jaw: the joint carries the whole
        # wing in bending and has to look like it knows that.
        part.cyl(center=tuple(direction * 0.055), radius=0.052,
                 radius_top=0.040, depth=0.048, axis='Z', seg=12, mat=STEEL,
                 rot=aim)

        part.bevel(width=0.005, segments=2)
        obj = part.finish("Mesh_OrniWorn_Clamp_%s" % tag, coll, origin=(0, 0, 0))
        obj.location = tuple(jaw)
        made.append(obj)
    return made


def bounds(objs):
    """World AABB of `objs`, read off the MESH DATA rather than `bound_box`.

    `bound_box` is a cached evaluation. Read straight after a script has
    retransformed a mesh and reset its object matrix it is silently STALE — the
    first cut of this file aimed both wing struts at (0, 0, −0.088) because of
    it, which put them through the wearer's spine instead of onto the bearings,
    and the numbers looked plausible enough to print without complaint.
    """
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objs:
        m = obj.matrix_world
        for v in obj.data.vertices:
            w = m @ v.co
            for i in range(3):
                lo[i] = min(lo[i], w[i])
                hi[i] = max(hi[i], w[i])
    return lo, hi


def centre(obj):
    lo, hi = bounds([obj])
    return (lo + hi) * 0.5


def describe(objs):
    lo, hi = bounds(objs)
    print("  worn wings: span %.3f m, depth %.3f m, height %.3f m"
          % (hi.x - lo.x, hi.y - lo.y, hi.z - lo.z))
    print("  bounds lo (%.3f, %.3f, %.3f)  hi (%.3f, %.3f, %.3f)"
          % (lo.x, lo.y, lo.z, hi.x, hi.y, hi.z))
    tris = sum(sum(max(0, len(p.vertices) - 2) for p in o.data.polygons)
               for o in objs)
    print("  %d object(s), %d tri(s)" % (len(objs), tris))
    for obj in sorted(objs, key=lambda o: o.name):
        c = centre(obj)
        print("    %-34s centre (%.3f, %.3f, %.3f)" % (obj.name, c.x, c.y, c.z))


def main():
    flush_edit_mode()
    cull()
    armature = apply_worn_pose()
    piv = pivots(armature)
    meshes = bake(armature)

    for tag in ("L", "R"):
        side = [o for o in meshes if o.name.endswith("_%s" % tag)
                or ("_%s_" % tag) in o.name]
        k = TARGET_REACH / reach(side, piv[tag])
        place(side, piv[tag], k, Vector((math.copysign(ROOT_HALF, piv[tag].x),
                                         0.0, 0.0)))
        print("  %s wing: scaled %.4f about its shoulder pivot" % (tag, k))

    flatten(meshes)

    for old, new in KEEP.items():
        obj = bpy.data.objects.get(old)
        if obj is not None:
            obj.name = new
            obj.data.name = new

    if not PREVIEW and not COMMIT:
        raise SystemExit("pass --preview <png> or --commit")
    if COMMIT and os.path.exists(DST):
        raise SystemExit("%s already exists — it may hold hand edits; delete "
                         "it yourself if a rebuild is really wanted." % DST)

    # Localise palette links so the saved copy stands alone, the way the
    # exports do. Done BEFORE the clamps are built, so they pick up the local
    # datablocks rather than re-linking the library ones.
    for mat in list(bpy.data.materials):
        if mat.library is not None:
            mat.make_local()

    # The source's own collections come across empty once everything moves into
    # the new one, and an empty collection in a shipped file is clutter a reader
    # has to rule out.
    coll = bpy.data.collections.new("Coll_OrnithopterWorn")
    bpy.context.scene.collection.children.link(coll)
    for obj in [o for o in bpy.data.objects if o.type == 'MESH']:
        for c in list(obj.users_collection):
            c.objects.unlink(obj)
        coll.objects.link(obj)

    anchors = {}
    for tag in ("L", "R"):
        anchors[tag] = centre(bpy.data.objects["Mesh_OrniWorn_Bearing_%s" % tag])
    build_clamps(coll, anchors)
    bpy.context.view_layer.update()
    seat_origins(meshes)
    bpy.context.view_layer.update()

    for old in [c for c in bpy.data.collections if not c.objects and c is not coll]:
        bpy.data.collections.remove(old)
    describe([o for o in bpy.data.objects if o.type == 'MESH'])

    if PREVIEW:
        sys.argv = [sys.argv[0], "--", "--out", PREVIEW, "--view", VIEW,
                    "--res", "1000"]
        exec(open(os.path.join(LIB, "_preview.py")).read(),
             {"__name__": "__preview__"})
        return

    bpy.ops.wm.save_as_mainfile(filepath=DST)
    print("Wrote %s" % DST)


main()
