"""The Sandloper: a large, rideable cousin of the dune rat.

    blender --background --python models/creatures/sandloper.py -- --out models/creatures/sandloper.blend

## Why it is built from the FBX and not from dune_rat.blend

`models/creatures/dune_rat.blend` **cannot be opened by Blender 4.2**, the only
Blender installed here -- it fails with *"not a blend file"*, exactly like
`palette.blend` and `components/props/supply_crate.blend`. It was written by a
newer Blender.

That is not the only reason, though, and the other one is the important one: the
dune rat's mesh and skeleton were **hand-authored by Tobias Fremming**, so they
are not mine to edit. Importing the shipped FBX into a NEW file guarantees the
original is untouched no matter what happens in here.

## Detail: subdivision, not a remesh

A true remesh would have thrown away the 32 vertex groups and the UV map, and
the rig and all six animations with them. Catmull-Clark subdivision keeps both
-- weights are interpolated onto the new vertices and the UVs come along -- so
the animal gets ~4x the geometry and still walks.

## Colour

Five palette hides, assigned per-face by where the face sits on the body. The
dune rat shipped as one flat `Mat_Hide_Sand_Pale`; this reads as an animal.
Created locally under the palette's own names because `palette.blend` cannot be
opened either -- see saddle.py, which carries the full note.
"""
import os
import sys

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..")))

import math

import bpy
import _buildlib as B

SRC_FBX = os.path.abspath(os.path.join(os.path.dirname(__file__),
                                       "..", "..", "..", "Creatures", "Organic",
                                       "DuneRat", "dune_rat.fbx"))

# name -> (hex, roughness, metallic). Values copied from PALETTE.md.
# Countershaded, the way a real desert animal is: dark along the sunlit back,
# mid on the flank, pale underneath. The first attempt had it inverted -- a
# near-white body over saturated yellow legs -- which read as a plastic toy.
MATERIALS = [
    ("Mat_Hide_Plate_Tan",   "987340", 0.62, 0.0),   # sunlit back and skull cap
    ("Mat_Hide_Dune_Tan",    "C9BC9A", 0.74, 0.0),   # flank, the body colour
    ("Mat_Hide_Ivory_Spine", "E2D8C0", 0.38, 0.0),   # belly and inner limbs
    ("Mat_Hide_Claw_Horn",   "4A3D2E", 0.34, 0.0),   # crest spines, feet, tail tip
    ("Mat_Hide_Slate_Teal",  "5E7B7A", 0.68, 0.0),   # ear cups, the one cool note
]
BACK, FLANK, BELLY, CLAW, EAR = range(5)

# How far each pose is pushed away from the rest pose. The dune rat's clips were
# authored for a nervy 1.26 m animal and read as a shuffle on something 5 m long
# -- "so small they are barely visible". 1.75 keeps the timing and the footfalls
# exactly as the author placed them and only widens the arcs.
GAIT_GAIN = 2.1

# ...but no joint may swing further than this from the clip's mean pose. Gain
# alone took the running knee from an 84 deg sweep to 177, which folds the leg
# through itself. Everything small gets the full gain; only the joints that were
# already extreme are held back.
MAX_SWING = math.radians(55.0)

SUBDIVISIONS = 1        # 3.7k tris -> ~14.8k. Two levels is 59k, which is a lot
                        # of animal for something you will meet in herds.


def _srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def materials():
    out = []
    for name, hexcode, rough, metal in MATERIALS:
        mat = bpy.data.materials.get(name)
        if mat is None:
            mat = bpy.data.materials.new(name)
            mat.use_nodes = True
            bsdf = mat.node_tree.nodes["Principled BSDF"]
            rgb = [int(hexcode[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
            lin = [_srgb_to_linear(c) for c in rgb]
            bsdf.inputs["Base Color"].default_value = (lin[0], lin[1], lin[2], 1.0)
            bsdf.inputs["Roughness"].default_value = rough
            bsdf.inputs["Metallic"].default_value = metal
        out.append(mat)
    return out


def zone(cx, cy, cz, nz):
    """Which hide a face belongs to, from where it sits AND which way it faces.

    The first version cut the countershading at a flat height, which stair-steps
    across a curved animal -- a horizontal line sawing through his ribs. Using
    the face NORMAL instead lets the boundary follow the form: whatever the sun
    would land on is the back, whatever faces the sand is the belly, and the
    turn between them lands wherever his shape puts it.

    He faces -Y: the head is at y = -0.51, the tail runs to y = +1.34, and the
    spine rides at z = 1.01. Order matters -- the ears are both high and
    forward, so they have to be claimed before anything else takes them.
    """
    if cz < 0.20:                                   # feet on the sand
        return CLAW
    if cy > 1.25:                                   # the last hand of tail
        return CLAW
    if cz > 1.14 and -0.30 < cy < 0.75:             # the spined crest itself
        return CLAW
    if cz > 1.02 and cy < -0.36 and abs(cx) < 0.16:  # ear cups only, not the skull
        return EAR

    # Countershading, by facing AND by height together. Facing alone put tan
    # patches on the haunches -- every upward face on a thigh read as "back" and
    # the animal came out dappled. The dorsal stripe has to stay on the dorsal
    # line, so BACK is the sunlit surface at or above the spine (z = 1.01) and
    # nothing lower, whichever way it points.
    if nz > 0.35 and cz > 0.93:
        return BACK
    if nz < -0.30 and cz < 0.90:
        return BELLY
    if cz < 0.58 and -0.75 < cy < 0.65:             # under the barrel
        return BELLY
    return FLANK


def _mean_quaternion(quats):
    """Average of a set of quaternions, hemisphere-corrected.

    Quaternions double-cover rotations: q and -q are the same orientation, and a
    baked curve flips sign freely. Averaging without folding them into one
    hemisphere first gives a mean near zero that normalises to nonsense.
    """
    from mathutils import Quaternion

    ref = quats[0]
    acc = [0.0, 0.0, 0.0, 0.0]
    for q in quats:
        sign = -1.0 if q.dot(ref) < 0.0 else 1.0
        for i in range(4):
            acc[i] += q[i] * sign
    mean = Quaternion(acc)
    if mean.magnitude < 1e-6:
        return ref.copy()
    mean.normalize()
    return mean


def amplify(gain=GAIT_GAIN):
    """Push every pose further from the clip's average, without touching timing.

    ## Why the first attempt did nothing

    These clips are baked to `rotation_quaternion`, and the first version scaled
    each of the four components about its own mean. That is not a rotation
    operation: a quaternion is normalised on the way out, so scaling all four
    components and renormalising lands almost exactly back where it started.
    The animal came out no livelier than before -- "it looks like the sandloper
    is standing still almost".

    What actually amplifies a rotation is scaling its ANGLE. For each bone this
    takes the clip's mean orientation as the pose to keep, expresses every
    keyframe as a rotation away from that mean, multiplies that rotation's angle
    by `gain`, and puts it back. The stance is preserved and only the swing
    widens, which is why the footfalls still land where the author put them.

    Location and scale curves are untouched. The rig's feet are IK targets and
    their positions are what put the toes on the sand.
    """
    from mathutils import Quaternion

    for act in bpy.data.actions:
        # Group the four component curves per bone.
        groups = {}
        for fc in act.fcurves:
            if not fc.data_path.endswith("rotation_quaternion"):
                continue
            groups.setdefault(fc.data_path, {})[fc.array_index] = fc

        for path, comps in groups.items():
            if len(comps) != 4:
                continue
            frames = [k.co[0] for k in comps[0].keyframe_points]
            quats = []
            for i in range(len(frames)):
                quats.append(Quaternion((comps[0].keyframe_points[i].co[1],
                                         comps[1].keyframe_points[i].co[1],
                                         comps[2].keyframe_points[i].co[1],
                                         comps[3].keyframe_points[i].co[1])).normalized())

            mean = _mean_quaternion(quats)
            inv = mean.inverted()

            for i, q in enumerate(quats):
                delta = inv @ q
                if delta.w < 0.0:            # shortest arc
                    delta.negate()
                angle = min(abs(delta.angle) * gain, MAX_SWING)
                wide = Quaternion(delta.axis, math.copysign(angle, delta.angle))
                out = (mean @ wide).normalized()
                for c in range(4):
                    kp = comps[c].keyframe_points[i]
                    kp.co[1] = out[c]
                    kp.handle_left[1] = out[c]
                    kp.handle_right[1] = out[c]

        for fc in act.fcurves:
            fc.update()


def build_jump(arm):
    """A bounding hop, because a jerboa's whole silhouette is a jump.

    In place: NavMeshAgentMotor supplies the height by animating the agent's
    baseOffset, and keying a rise here as well would double it. 30 frames at
    24 fps is 1.25 s, which is the motor's 0.55 s hop at the 2.0 playback rate
    every clip on this animal runs at.
    """
    import math

    act = bpy.data.actions.new("Sandloper_Jump")
    # Or Blender drops it on save: an action nothing points at has zero users and
    # is purged, which is exactly what happened the first time -- the file
    # reported seven actions and reloaded with six, and the export shipped no
    # jump take at all.
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act

    def pulse(u, a, peak, b):
        if u <= a or u >= b:
            return 0.0
        t = (u - a) / (peak - a) if u < peak else (b - u) / (b - peak)
        return max(0.0, min(1.0, t))

    FRAMES = 30
    d = math.radians
    for f in range(FRAMES + 1):
        u = f / float(FRAMES)
        gather = pulse(u, 0.0, 0.16, 0.38)      # coil before the push
        tuck = pulse(u, 0.22, 0.55, 0.95)       # legs folded under him
        land = pulse(u, 0.80, 0.93, 1.0)        # knees taking the drop

        for name, amount in (("spine1", 10.0), ("spine2", 8.0),
                             ("spine3", 6.0), ("spine4", 5.0)):
            pb = arm.pose.bones.get(name)
            if pb is None:
                continue
            pb.rotation_mode = 'XYZ'
            pb.rotation_euler[0] = d(gather * amount - tuck * amount * 0.7)
            pb.keyframe_insert("rotation_euler", frame=f)

        for name, amount in (("neck1", 14.0), ("neck2", 10.0), ("head", 8.0)):
            pb = arm.pose.bones.get(name)
            if pb is None:
                continue
            pb.rotation_mode = 'XYZ'
            pb.rotation_euler[0] = d(gather * amount - tuck * amount * 1.2 + land * amount * 0.5)
            pb.keyframe_insert("rotation_euler", frame=f)

        # The hind legs do the work; the tail streams out as the counterweight.
        for side in ("L", "R"):
            for name, amount in (("femur." + side, 34.0), ("fibula." + side, -46.0),
                                 ("metarsal." + side, 26.0)):
                pb = arm.pose.bones.get(name)
                if pb is None:
                    continue
                pb.rotation_mode = 'XYZ'
                pb.rotation_euler[0] = d(-gather * amount * 0.4 + tuck * amount - land * amount * 0.3)
                pb.keyframe_insert("rotation_euler", frame=f)

        for name, amount in (("tail1", -12.0), ("tail2", -16.0),
                             ("tail3", -18.0), ("tail4", -20.0)):
            pb = arm.pose.bones.get(name)
            if pb is None:
                continue
            pb.rotation_mode = 'XYZ'
            pb.rotation_euler[0] = d(gather * amount * 0.5 + tuck * amount)
            pb.keyframe_insert("rotation_euler", frame=f)

    arm.animation_data.action = None
    print("  built Sandloper_Jump (%d frames)" % FRAMES)


def build():
    if not os.path.exists(SRC_FBX):
        raise SystemExit("No dune rat FBX at %s" % SRC_FBX)

    bpy.ops.import_scene.fbx(filepath=SRC_FBX)

    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    mesh = next(o for o in bpy.data.objects if o.type == 'MESH')

    arm.name = arm.data.name = "Arm_Sandloper"
    mesh.name = mesh.data.name = "Mesh_Sandloper"

    # The FBX round trip names actions "Arm_DuneRat|Arm_DuneRat|DuneRat_Idle".
    # Reduce them to the clip name this animal ships under.
    for act in bpy.data.actions:
        act.name = "Sandloper_" + act.name.rsplit("_", 1)[-1]

    # -- detail ---------------------------------------------------------
    bpy.context.view_layer.objects.active = mesh
    sub = mesh.modifiers.new("Subdivision", 'SUBSURF')
    sub.levels = sub.render_levels = SUBDIVISIONS
    sub.use_limit_surface = False        # keep the silhouette the author built
    bpy.ops.object.modifier_apply(modifier=sub.name)

    for poly in mesh.data.polygons:
        poly.use_smooth = True

    # -- colour ---------------------------------------------------------
    mats = materials()
    mesh.data.materials.clear()
    for m in mats:
        mesh.data.materials.append(m)

    mw = mesh.matrix_world
    for poly in mesh.data.polygons:
        c = mw @ poly.center
        n = (mw.to_3x3() @ poly.normal).normalized()
        poly.material_index = zone(c.x, c.y, c.z, n.z)

    amplify()
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='POSE')
    build_jump(arm)
    bpy.ops.object.mode_set(mode='OBJECT')

    counts = {}
    for poly in mesh.data.polygons:
        counts[poly.material_index] = counts.get(poly.material_index, 0) + 1
    for i, (name, _, _, _) in enumerate(MATERIALS):
        print("  %-24s %5d faces" % (name, counts.get(i, 0)))

    tris = sum(max(0, len(p.vertices) - 2) for p in mesh.data.polygons)
    print("  TOTAL TRIS: %d   bones: %d   actions: %d"
          % (tris, len(arm.data.bones), len(bpy.data.actions)))


def main():
    out = B.parse_out()
    B.start(out)
    build()
    B.save(out)


main()
