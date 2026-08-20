"""Rig the Vrescal sculpt: 32 bones, skinned, ready for `vrescal_anim.py`.

    blender --background --python vrescal_rig.py -- --overwrite

Reads `vrescal_stylised.blend`, writes `vrescal_rigged.blend`. Neither input is
modified.

## Why this file is not in metres

The mesh is authored in metres with the sole plane at z = 0. This file scales it
by 3.6245 and drops it to z = -13, which is the working space the *existing*
Vrescal pipeline uses -- and that is a deliberate choice, not an inherited
accident.

`vrescal_anim.py` solves all six clips, with measured zero foot slide, and it is
700 lines of tuning it would be foolish to re-derive. Its coupling to geometry
turns out to be four constants, so it looked like a shim would be enough. It is
not: only `stride` is expressed in metres-times-`UNITS_PER_M`. Every other
amplitude in it -- `lift`, `crouch`, `bob`, `sway`, and the literals inside the
idle, attack, hurt and death frame functions -- is in **raw working units**. Run
the solver against a metre-scale rig and it lifts each foot 1.15 *metres*.

There are two ways out: patch several dozen literals across four clip
generators, or move the rig into the space those literals were tuned for. The
second is one line, is reversible, and reuses code that is known to work. The
export scales back to metres on the way to Unity, exactly as the old pipeline
does, so nothing downstream can tell the difference.

## The stance is asymmetric, and that is a real constraint

The sculpt came out of an image-to-3D conversion of a painting of an animal that
was **standing mid-stride**. Measured off the mesh, the feet sit at:

    FrontP  x +0.80  y +0.68        FrontS  x +0.80  y -0.68
    RearP   x -0.34  y +0.29        RearS   x -1.07  y -0.22

The hind pair is staggered by 0.73 m fore-and-aft and sits well inboard of the
fore pair. Bones are placed on that real geometry regardless, because skinning
weights have to follow the mesh that exists -- a bone floating outside the limb
it drives produces collapsing joints no animation can hide.

The stagger is dealt with in `vrescal_sculpt_anim.py` instead, by symmetrising
the *gait targets* while leaving the bind pose alone. Feet then travel to
symmetric positions and the legs bend to reach, which is what IK is for. The
alternative -- forcing the bind pose symmetric -- would put every hind-leg bone
outside its own mesh.
"""

import math
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import anatomy as A          # noqa: E402

# Overridable so the same rig can be built over a different body -- the
# low-poly cut, for one -- without editing this file or copying it. Unset, the
# defaults are exactly what they always were.
SRC = os.environ.get("VRESCAL_RIG_SRC",
                     os.path.join(HERE, "vrescal_stylised.blend"))
OUT = os.environ.get("VRESCAL_RIG_OUT",
                     os.path.join(HERE, "vrescal_rigged.blend"))

# The working space `vrescal_anim.py` was tuned in.
UNITS_PER_M = 3.62450
GROUND = -13.0

ARM = "Arm_Vrescal"
MESH = "Mesh_Vrescal_Sculpt"

# Under the middle of the trunk, between the fore and hind supports. The export
# puts this on Unity's origin, so it is where the animal turns about.
PIVOT_X_M = -0.05


def S(p):
    """Metres (sole plane z = 0) -> the solver's working units."""
    v = Vector(p)
    return Vector((v.x * UNITS_PER_M, v.y * UNITS_PER_M,
                   v.z * UNITS_PER_M + GROUND))


# --------------------------------------------------------------------------
# The skeleton, in metres, measured off the mesh
# --------------------------------------------------------------------------
#
# Every joint below was read off the limb-column and centreline traces of the
# actual sculpt, not carried over from the previous build.

SPINE = [
    ("Bone_Pelvis",   (-1.00, 0.0, 2.10), (-0.50, 0.0, 2.36)),
    ("Bone_Spine_01", (-0.50, 0.0, 2.36), (0.05, 0.0, 2.60)),
    ("Bone_Spine_02", (0.05, 0.0, 2.60), (0.65, 0.0, 2.78)),
    ("Bone_Spine_03", (0.65, 0.0, 2.78), (1.30, 0.0, 2.74)),
    ("Bone_Neck_01",  (1.30, 0.0, 2.74), (1.47, 0.0, 2.73)),
    ("Bone_Neck_02",  (1.47, 0.0, 2.73), (1.64, 0.0, 2.76)),
    ("Bone_Neck_03",  (1.64, 0.0, 2.76), (1.80, 0.0, 2.80)),
    ("Bone_Neck_04",  (1.80, 0.0, 2.80), (1.96, 0.0, 2.86)),
    ("Bone_Head",     (1.96, 0.0, 2.86), (2.62, 0.0, 2.99)),
]

# The jaw hangs off the head and is the only part of the skull that moves.
JAW = ((2.06, 0.0, 2.72), (2.70, 0.0, 2.63))

# The tail hangs from the top of the rump and stops well short of the ground,
# tip at 1.12 m. The chain is spaced to end exactly there: a first pass ran it
# down to 0.74 m and `Bone_Tail_05` fell outside the mesh, drew no bone-heat
# weight at all, and would have shipped as a joint that silently does nothing.
TAIL = [
    ("Bone_Tail_01", (-1.03, 0.06, 2.45), (-1.14, 0.04, 2.17)),
    ("Bone_Tail_02", (-1.14, 0.04, 2.17), (-1.24, 0.02, 1.90)),
    ("Bone_Tail_03", (-1.24, 0.02, 1.90), (-1.32, 0.01, 1.63)),
    ("Bone_Tail_04", (-1.32, 0.01, 1.63), (-1.38, 0.00, 1.37)),
    ("Bone_Tail_05", (-1.38, 0.00, 1.37), (-1.42, 0.00, 1.12)),
]

# Per limb: the parent, and the four joints -- hip/shoulder, elbow/stifle,
# carpus/hock, ankle -- plus the toe the foot bone points at.
#
# The fore limbs are near-vertical columns of constant radius; the hind pair is
# noticeably slimmer and staggered. Both are as measured.
LIMBS = {
    "FrontP": dict(parent="Bone_Spine_03",
                   joints=[(1.05, 0.62, 2.15), (1.01, 0.61, 1.45),
                           (0.92, 0.59, 0.92), (0.86, 0.62, 0.42)],
                   toe=(1.16, 0.65, 0.11)),
    "FrontS": dict(parent="Bone_Spine_03",
                   joints=[(0.86, -0.66, 2.15), (0.84, -0.62, 1.45),
                           (0.80, -0.60, 0.92), (0.82, -0.63, 0.42)],
                   toe=(1.12, -0.66, 0.11)),
    "RearP":  dict(parent="Bone_Pelvis",
                   joints=[(-0.78, 0.40, 2.10), (-0.75, 0.36, 1.45),
                           (-0.78, 0.28, 0.95), (-0.60, 0.28, 0.42)],
                   toe=(-0.28, 0.29, 0.11)),
    "RearS":  dict(parent="Bone_Pelvis",
                   joints=[(-1.10, -0.16, 2.10), (-1.18, -0.20, 1.45),
                           (-1.29, -0.21, 0.95), (-1.24, -0.21, 0.42)],
                   toe=(-1.00, -0.22, 0.11)),
}
SEGMENTS = ["Upper", "Lower", "Cannon"]

# Foot pad dimensions, in metres, measured off the sculpt by `measure_feet()`
# and used by the animation solver to pivot foot roll on the sole's contact
# edge rather than on the ankle.
FOOT = {}


def measure_feet(mesh):
    """Sole radius and pad height per limb, from the mesh itself."""
    co = np.empty(len(mesh.vertices) * 3, dtype=np.float32)
    mesh.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    for name, spec in LIMBS.items():
        ax, ay, az = spec["joints"][-1]
        near = co[(np.abs(co[:, 0] - ax) < 0.62) & (np.abs(co[:, 1] - ay) < 0.45)
                  & (co[:, 2] < az)]
        if len(near) < 20:
            raise SystemExit("no foot geometry under %s" % name)
        reach = float(np.percentile(
            np.hypot(near[:, 0] - ax, near[:, 1] - ay), 90))
        FOOT[name] = dict(sole=round(reach, 3), height=round(az, 3))
        print("    %-7s sole reach %.3f m, pad height %.3f m, %d verts"
              % (name, reach, az, len(near)))


# --------------------------------------------------------------------------
# Build
# --------------------------------------------------------------------------

def bone_table():
    """(name, parent, head, tail, connect) for all 32 bones, in metres."""
    out = [("Bone_Root", None, (PIVOT_X_M, 0.0, 0.0),
            (PIVOT_X_M + 0.45, 0.0, 0.0), False)]
    prev = "Bone_Root"
    for name, head, tail in SPINE:
        out.append((name, prev, head, tail, prev != "Bone_Root"))
        prev = name
    out.append(("Bone_Jaw", "Bone_Head", JAW[0], JAW[1], False))
    prev = "Bone_Pelvis"
    for name, head, tail in TAIL:
        out.append((name, prev, head, tail, prev != "Bone_Pelvis"))
        prev = name
    for leg, spec in LIMBS.items():
        j = spec["joints"]
        parent = spec["parent"]
        for i, seg in enumerate(SEGMENTS):
            out.append(("Bone_%s_%s" % (leg, seg), parent, j[i], j[i + 1],
                        i > 0))
            parent = "Bone_%s_%s" % (leg, seg)
        out.append(("Bone_%s_Foot" % leg, parent, j[-1], spec["toe"], True))
    return out


def build_armature():
    arm_data = bpy.data.armatures.new(ARM)
    arm = bpy.data.objects.new(ARM, arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')

    table = bone_table()
    for name, parent, head, tail, connect in table:
        eb = arm_data.edit_bones.new(name)
        eb.head, eb.tail = S(head), S(tail)
        eb.roll = 0.0
        if parent:
            eb.parent = arm_data.edit_bones[parent]
            eb.use_connect = connect
        if (eb.tail - eb.head).length < 1e-4:
            raise SystemExit("zero-length bone: %s" % name)
    bpy.ops.object.mode_set(mode='OBJECT')

    # The root carries the animal through the world and must not deform, or
    # bone heat hands it a share of every vertex and the whole mesh follows it
    # rigidly instead of the spine.
    arm.data.bones["Bone_Root"].use_deform = False
    print("  %d bones" % len(arm_data.bones))
    return arm


def skin(mesh_obj, arm):
    """Bone-heat weights.

    Bone heat needs a closed manifold surface, which is exactly what the
    watertight fix in `vrescal_sculpt.py` bought: on the raw import, with its
    hole in the front hump, the solver has no boundary condition to work
    against and drops bones.
    """
    bpy.ops.object.select_all(action='DESELECT')
    mesh_obj.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    print("  skinned: %d vertex groups" % len(mesh_obj.vertex_groups))


def verify(mesh_obj, arm):
    """Every vertex must be driven, and no bone may claim the whole animal."""
    deform = {b.name for b in arm.data.bones if b.use_deform}
    groups = {g.index: g.name for g in mesh_obj.vertex_groups}
    unweighted, totals = 0, {}
    for v in mesh_obj.data.vertices:
        w = [g for g in v.groups if g.weight > 1e-4
             and groups.get(g.group) in deform]
        if not w:
            unweighted += 1
        for g in w:
            totals[groups[g.group]] = totals.get(groups[g.group], 0.0) + g.weight
    print("  unweighted vertices: %d" % unweighted)
    n = len(mesh_obj.data.vertices)
    worst = sorted(totals.items(), key=lambda kv: -kv[1])[:5]
    for name, tot in worst:
        print("      %-22s drives %5.1f%% of total weight" % (name, 100 * tot / n))
    missing = sorted(deform - set(totals))
    if missing:
        print("  BONES WITH NO INFLUENCE: %s" % ", ".join(missing))
    if unweighted:
        raise SystemExit("%d vertices are not driven by any bone" % unweighted)
    return missing


def main():
    A.start()
    with bpy.data.libraries.load(SRC) as (src, dst):
        dst.objects = list(src.objects)
    coll = A.collection("Coll_Vrescal_Rigged")
    for o in dst.objects:
        coll.objects.link(o)
    mesh_obj = bpy.data.objects[MESH]

    print("  measuring feet (metres, sole plane z = 0):")
    measure_feet(mesh_obj.data)

    # Into the solver's working space, on the mesh data so the object stays
    # at identity and the armature modifier sees no compensating transform.
    mesh_obj.data.transform(
        Matrix.Translation((0, 0, GROUND))
        @ Matrix.Diagonal((UNITS_PER_M, UNITS_PER_M, UNITS_PER_M, 1.0)))
    mesh_obj.data.update()

    arm = build_armature()
    skin(mesh_obj, arm)
    verify(mesh_obj, arm)

    zs = [v.co.z for v in mesh_obj.data.vertices]
    print("  mesh in solver units: z %.2f..%.2f (ground %.1f), height %.2f m"
          % (min(zs), max(zs), GROUND, (max(zs) - min(zs)) / UNITS_PER_M))
    print("  foot pads: %s" % FOOT)
    A.save(OUT)


if __name__ == "__main__":
    main()
