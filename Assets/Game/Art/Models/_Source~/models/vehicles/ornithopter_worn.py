"""Build ornithopter_worn.blend — the wing pack as it looks WORN, not carried.

The held item is `wing_pack_folded.blend`: the whole craft folded into a bundle.
This is the other thing the same item has to be — what you see strapped to a
player's back. The worn form throws the aircraft away and keeps the two things
that say "flight": the webbed wings, and the spoked shoulder mechanics that beat
them.

Gone, deliberately: the fuselage, the nose, the tail boom, the tail fan, the
prone cradle, the shoulder pylons and the tie-rod struts. Everything that held
the machine together around a rider it no longer has. What is left hangs off the
expedition rig's own hardware instead — see the frame note below.

## Two files, one machine, two poses (2026-09-05)

This script writes **both** worn shapes, from the same rig at the same scale:

| `--commit` | file | pose | worn |
| --- | --- | --- | --- |
| default | `ornithopter_worn.blend` | STOWED, 1.97 m | day to day, out in the world |
| `--spread` | `ornithopter_worn_on_person.blend` | OPEN, 5.51 m | on the gear screen (I) |

**Stowed** is folded shut against its own mounts — fan closed, wrist folded back
along the arm, elbow folded back along the shoulder bar — hanging as a slim
bundle behind the pack. A 5.5 m wingspan on a walking character is a wingspan,
not something anybody wears through a desert.

**Open** is the same wing spread, and it is not history. The gear screen is the
one place a player ever looks *at* their own back, on purpose, with the camera
flown round for it — so that is where the wings get to be wings.

Nothing is culled and nothing is scaled between them. **The difference is a
pose**: the same twelve parts, the same 8,736 triangles, the same `SPAR_SCALE`.
Both mount on the same two rail tips, so neither moves when the other is swapped
in. That is the whole point of doing it here rather than sizing the model in
Unity — a smaller wing pack would be a smaller machine, and this is one machine
put away and taken out again.

(Note the file names read backwards: `ornithopter_worn` is the one worn in
ordinary play, `ornithopter_worn_on_person` is the gear-screen one. The second
name predates the split. Renaming it to `..._spread` would be an improvement;
it is left alone because it is the name the file already ships under.)

Derived from `dune_ornithopter.blend`, which carries hand edits and is NEVER
written to. This script opens it, culls, poses the rig in memory, bakes, and
saves the result to a NEW file. Pose conventions (axis table, per-side Z sign)
come from `dune_ornithopter_BUILD.md`.

**Nothing is joined.** `wing_pack_folded.py` bakes to one mesh because the held
bundle never articulates and never needs a part named; a worn wing does get
looked at, so its twelve parts stay twelve named objects.

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

    # iterate on either pose, renders and exits without saving anything:
    blender --background dune_ornithopter.blend --python ornithopter_worn.py -- \
        --preview /tmp/worn.png [--spread] [--view front|side|iso]

    # bake and write the two shipped files (each refuses to overwrite):
    blender --background dune_ornithopter.blend --python ornithopter_worn.py -- --commit
    blender --background dune_ornithopter.blend --python ornithopter_worn.py -- --commit --spread

    # CONTROL RUN — the same parameters into a scratch path. Fingerprint that
    # against the shipped .blend (per-object vertex and polygon hash) and only
    # rebuild if they match; a difference means the file carries hand edits this
    # script would destroy. Done before the 2026-09-04 re-pose and again before
    # the 2026-09-05 fold, and it is the step that makes deleting the shipped
    # file safe rather than reckless. Run it with the OLD parameters, not the
    # new ones — the point is to reproduce what shipped:
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

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(flag, default=None):
    return argv[argv.index(flag) + 1] if flag in argv else default


PREVIEW = arg("--preview")
VIEW = arg("--view", "front")
COMMIT = "--commit" in argv

# Which of the item's two worn shapes to build. Both are the same machine at the
# same scale and both mount on the same two rail tips; they differ only in pose.
SPREAD = "--spread" in argv

# Where --commit writes. Overridable so a CONTROL run — the same parameters into
# a scratch path, diffed against the shipped file to prove this script still
# reproduces it — can be done without going anywhere near the real one. Any
# rebuild of a shipped file starts with that control run.
DST = arg("--out", os.path.join(
    HERE, "ornithopter_worn_on_person.blend" if SPREAD else "ornithopter_worn.blend"))

R = math.radians
N_DIGITS = 5

# ---------------------------------------------------------------- the wearer
# Where the rail's two protruding bars end, left and right of the spine, in
# metres. The shoulder pivot of each wing is placed exactly here.
ROOT_HALF = 0.885

# How far the wing reaches from that pivot to its furthest tip WHEN IT IS OPEN,
# in metres. This is what sizes the wing against the WEARER rather than against
# the aircraft: the rail sits 0.63 m above the spine bone and the soles about
# 1.45 m below it, so the ground is 2.08 m under the rail and that is the whole
# budget an unfolded wing has. 2.775 since 2026-09-04.
EXTENDED_REACH = 2.775

# What that same reach measures in the SOURCE RIG's own units — recorded once,
# off the open pose, on 2026-09-04, and deliberately frozen.
#
# The scale used to be derived from the wing AS POSED (`EXTENDED_REACH / reach()`
# after the pose was applied), which is exactly wrong for a folded model and is
# the one trap in this whole rebuild: folding a wing shortens its reach, so
# re-deriving would scale the folded stack straight back up to the size it was
# folded out of. The fold would be invisible and every measurement would agree
# with itself. Frozen, the fold is a pose and only a pose — the same spars, the
# same cloth, the same metal, put away.
EXTENDED_RIG_REACH = 5.1518
SPAR_SCALE = EXTENDED_REACH / EXTENDED_RIG_REACH

# ---------------------------------------------------------------- the two poses
# Signs follow the rig's rule (dune_ornithopter_BUILD.md): a wing bone's local X
# and Y are already mirrored between sides so flap and twist take no per-side
# sign, while local Z is not and sweep and splay do.
#
# STOWED closes the wing in the order it physically would, on the three hinges it
# actually has, and the parameters are named for those hinges:
#
#   1. the fan shuts     five digit spars swing together onto one line   SPLAY
#   2. the wrist folds   the shut fan lies back along the arm            WRIST
#   3. the elbow folds   the arm lies back along the shoulder bar        SWEEP
#
# — and then the shoulder carries that flat bundle round behind the pack and
# down (PLANE, FLAP, YAW).
#
# OPEN is the wing spread, which is what this file shipped as until 2026-09-05.
# It is not history: it is the shape the GEAR SCREEN wears (see the module note),
# so it is built and shipped alongside the stowed one, from the same rig, at the
# same scale, out of the same script.
#
# Both were swept with `--preview /tmp/x.png --view front|side` and looked at,
# not reasoned about; every entry is argable, so the next sweep needs no edit to
# this file.
STOWED = dict(plane=-105.0, flap=-100.0, yaw=0.0, sweep=175.0, roll=0.0,
              wrist=-175.0, splay=-105.0, twist=12.0, furl=0.055, slack=0.06)
OPEN = dict(plane=0.0, flap=-52.0, yaw=0.0, sweep=16.0, roll=38.0,
            wrist=0.0, splay=-105.0, twist=14.0, furl=0.0, slack=0.0)

POSE = OPEN if SPREAD else STOWED


def pose(name):
    """One pose value, overridable on the command line for a sweep."""
    return float(arg("--" + name, POSE[name]))


PLANE = pose("plane")   # rolls the fold plane about the mount bar
FLAP = pose("flap")     # swings the wing down off the bar
YAW = pose("yaw")       # swings it aft, clear of the wearer's flank
SWEEP = pose("sweep")   # ELBOW: arm back along the shoulder bar
ROLL = pose("roll")     # twists the fan about the arm
WRIST = pose("wrist")   # WRIST: shut fan back along the arm
SPLAY = pose("splay")   # residual fan opening; -105 lays the spars parallel
TWIST = pose("twist")   # feathering across the five spars

# Where the STOWED numbers came from, since half of them are pure look and half
# are geometry, and the two want different treatment if this is ever re-swept:
#
#   SPLAY -105 is not a taste call and it is not "the fan opening angle", which
#         is what this file used to claim. The five spars sit 104.7 degrees
#         apart at rest and the grading spreads exactly SPLAY across them, so
#         -105 is the value that lays them PARALLEL. It reads as an open sail in
#         the spread pose and as a shut fan in this one for the same reason: it
#         is the same stack of parallel spars either way, with the cloth taut
#         across it or gathered onto it.
#   SWEEP 175 / WRIST -175 fold the two hinges back on themselves, five degrees
#         short of dead flat so the links do not stack into one another. This is
#         the tightest the chain goes: reach from the mount 2.775 -> 1.09 m.
#   FLAP -100 hangs the bundle from the rail tip, ten degrees past vertical so
#         it leans INBOARD behind the pack rather than out past the wearer's
#         flank. Swept: -80 through -135, and -100 is where the span bottoms out
#         at 1.99 m — which is the rail itself, so the wings have stopped
#         contributing to the silhouette's width altogether.
#   PLANE -105 lies the folded wing back against the pack instead of standing it
#         square across it. Worth 0.40 -> 0.22 m of protrusion behind the rail
#         for nothing: span and height do not move.
#   TWIST 12, ROLL 0. The spread pose spent both of these turning a sail toward
#         a head-on camera; a furled wing has no sail to turn, so ROLL goes to
#         zero and TWIST keeps only the feathering that stops the five spars
#         reading as one fused blade.
#
# The unavoidable one: the folded arm ends 0.47 m ABOVE the rail, because it is
# 0.95 m long and folds back over a 0.52 m shoulder bar. Every way of burying
# that overshoot costs more than it saves — folding the arm less puts the wrist
# 0.8 m out to the side, which is the width this whole change exists to remove.
# So two spar tips stand above the wearer's shoulders, and that is the shape of
# a folded wing rather than a defect.

# How tightly the membrane gathers onto the folded spars, in metres, and how
# much of its original slack survives that. See `furl`. Radius is a canvas
# thickness against a spar stack about 0.04 m across; slack is what keeps the
# furl tapered instead of shrink-wrapped, and it is the number to move if the
# bundle reads as a sausage (lower) or as a sail that never got put away
# (lower still, or the pose is wrong).
#
# **Zero switches it off, and the OPEN pose sets it to zero.** Furling belongs to
# a wing that has been put away; run it on a spread one and it drags the taut
# sail off its own spars and onto them, which is the sail destroyed rather than
# gathered.
FURL_RADIUS = pose("furl")
FURL_SLACK = pose("slack")

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


def rot(armature, bone_name, x=0.0, y=0.0, z=0.0, mode='XYZ'):
    pb = armature.pose.bones[bone_name]
    pb.rotation_mode = mode
    pb.rotation_euler = (R(x), R(y), R(z))


def apply_worn_pose():
    armature = bpy.data.objects["Arm_DuneOrnithopter"]
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode='POSE')

    for tag, s in (("R", 1), ("L", -1)):
        # 'YXZ' on the shoulder, and the order is load-bearing rather than
        # incidental. PLANE is a roll about the mount bar and it has to be
        # applied to a wing that is still lying in its own plane; under the
        # default 'XYZ' it lands on an already-flapped chain, where it stops
        # rolling the fold plane and starts swinging the bundle fore and aft.
        # The difference is a stack that stands on edge behind the pack versus
        # one lying flat across the wearer's shoulder blades.
        rot(armature, "Bone_Shoulder_%s" % tag,
            x=FLAP, y=PLANE * s, z=YAW * s, mode='YXZ')
        # ROLL takes a per-side sign and DIGIT TWIST does not, even though both
        # are a rotation about a bone's local Y. Measured, not reasoned: with
        # the same sign on both arms the model's bounds came out asymmetric
        # (-1.51 to +1.86 at 40 degrees) while the digits stayed symmetric.
        # `dune_ornithopter_BUILD.md`'s table covers the digits and says local Y
        # needs no sign; that does not carry to `Bone_Arm`, and the only way to
        # tell is to print the bounds.
        rot(armature, "Bone_Arm_%s" % tag, y=ROLL * s, z=SWEEP * s)
        for i in range(N_DIGITS):
            # WRIST is the fold and SPLAY is what is left open across it. They
            # share the digits' local Z because they are the same hinge — a
            # folding fan shuts and swings back on one pivot — so the fan is
            # ground down to a few degrees of residual opening and the whole
            # shut stack is then carried back over the arm by WRIST. Graded, so
            # the trailing spar still travels furthest and the five stay a stack
            # of spars rather than collapsing into one line with no read.
            k = i / (N_DIGITS - 1)
            rot(armature, "Bone_Digit_%s_%d" % (tag, i + 1),
                z=(WRIST + SPLAY * (k - 0.30)) * s,
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


def spars(armature):
    """The posed frame each wing's membrane hangs on, as world-space segments.

    Read off the armature while it still exists, because the furl below needs
    to know where the folded spars ENDED UP and there is nothing left to ask
    once the pose is baked and the rig dropped.
    """
    out = {}
    for tag in ("L", "R"):
        segs = []
        for name in (["Bone_Shoulder_%s" % tag, "Bone_Arm_%s" % tag]
                     + ["Bone_Digit_%s_%d" % (tag, i + 1) for i in range(N_DIGITS)]):
            pb = armature.pose.bones[name]
            segs.append((armature.matrix_world @ pb.head,
                         armature.matrix_world @ pb.tail))
        out[tag] = segs
    return out


def furl(meshes, frames):
    """Gather each wing's membrane onto its folded spars.

    The one thing the rig cannot do. Closing the fan stacks the five spars into
    a bundle, and that part is honest articulation — but the web is a single
    skinned sheet, so linear blend skinning carries it across the closed fan as
    one smooth 1.4 m sail rather than furling it. Posed alone, the wing reads as
    a folded frame with a bedsheet draped over it: the metal is put away and the
    cloth is not, which is precisely the read a stowed wing must not have.

    So the cloth is gathered by hand, radially onto the frame it hangs from: for
    every web vertex, find the nearest point on any posed spar and pull the
    vertex in toward it. `FURL_SLACK` is what stops that being a shrink-wrap —
    the cloth that had furthest to travel still ends up furthest out, so the
    bundle keeps the tapered, bunched profile of canvas gathered against a spar
    instead of the perfect sleeve a constant radius would give.

    Vertices keep their bearing about the spar, which is what stops the sheet's
    two faces from collapsing through each other: whatever was outboard of the
    frame stays outboard of it, just closer.

    Off entirely at radius zero, which is what the OPEN pose asks for. Furling
    belongs to a wing that has been put away; run it on a spread one and it drags
    the taut sail off its own spars and onto them — the sail destroyed rather
    than gathered.
    """
    if FURL_RADIUS <= 0.0:
        print("  furl off: the wing is spread, so its cloth is taut")
        return

    moved = 0
    for obj in meshes:
        tag = side_tag(obj.name)
        if "_Web" not in obj.name or tag is None:
            continue
        segs = frames[tag]
        mw = obj.matrix_world
        inv = mw.inverted()
        for v in obj.data.vertices:
            p = mw @ v.co
            c, d = nearest_on_frame(p, segs)
            if d <= FURL_RADIUS:
                continue
            r = FURL_RADIUS + (d - FURL_RADIUS) * FURL_SLACK
            v.co = inv @ (c + (p - c) * (r / d))
            moved += 1
        obj.data.update()
    print("  furled %d membrane vertice(s) onto the folded spars" % moved)


def nearest_on_frame(p, segs):
    """Closest point on any of `segs` to `p`, and the distance to it."""
    best, best_d = None, 1e9
    for a, b in segs:
        ab = b - a
        t = (p - a).dot(ab) / ab.length_squared
        c = a + ab * min(1.0, max(0.0, t))
        d = (p - c).length
        if d < best_d:
            best, best_d = c, d
    return best, best_d


def side_tag(name):
    for tag in ("L", "R"):
        if name.endswith("_%s" % tag) or ("_%s_" % tag) in name:
            return tag
    return None


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
    frames = spars(armature)
    meshes = bake(armature)
    furl(meshes, frames)

    for tag in ("L", "R"):
        side = [o for o in meshes if side_tag(o.name) == tag]
        place(side, piv[tag], SPAR_SCALE,
              Vector((math.copysign(ROOT_HALF, piv[tag].x), 0.0, 0.0)))
        print("  %s wing: scaled %.6f about its shoulder pivot, folded reach "
              "%.3f m (%.3f m open)"
              % (tag, SPAR_SCALE, reach(side, Vector(
                  (math.copysign(ROOT_HALF, piv[tag].x), 0.0, 0.0))),
                 EXTENDED_REACH))

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
