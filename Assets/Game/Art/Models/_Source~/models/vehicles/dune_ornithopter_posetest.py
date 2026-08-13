"""Pose the ornithopter's rig and render it, proving the articulation works.

Opens the model read-only, applies a pose in memory, renders, and exits without
saving. Verification only — it must never write to the .blend.

    blender --background dune_ornithopter.blend --python dune_ornithopter_posetest.py -- \
        --pose downstroke --out /tmp/shot.png [--view iso]
"""

import math
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def arg(flag, default=None):
    return argv[argv.index(flag) + 1] if flag in argv else default


POSE = arg("--pose", "downstroke")
OUT = arg("--out", "/tmp/pose.png")
VIEW = arg("--view", "iso")
RES = arg("--res", "1000")

arm = bpy.data.objects["Arm_DuneOrnithopter"]
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')

R = math.radians
N_DIGITS = 5


def rot(bone_name, x=0.0, y=0.0, z=0.0):
    pb = arm.pose.bones[bone_name]
    pb.rotation_mode = 'XYZ'
    pb.rotation_euler = (R(x), R(y), R(z))


def wings(flap, splay, twist, sweep=0.0, gear=0.0):
    """flap: beat at the shoulder. splay: fan the digits. twist: incidence.

    The digit bones lie along their spars, so twist is a roll about local Y and
    fanning is a rotation about local Z — the axis normal to the wing plane.
    """
    # Sign conventions, and they are not uniform — this is the one genuinely
    # confusing thing about the rig:
    #
    #   local X and Y are MIRRORED between the two sides (the bones point
    #   outboard, in opposite directions), so the same angle on both gives a
    #   symmetric result — flap and twist take no sign flip.
    #
    #   local Z points up on BOTH sides, so it is not mirrored. Anything that
    #   rotates about Z — sweep, and the digit splay — needs an explicit
    #   per-side sign or one wing opens while the other closes.
    for tag, s in (("R", 1), ("L", -1)):
        rot("Bone_Shoulder_%s" % tag, x=flap)
        rot("Bone_Arm_%s" % tag, z=sweep * s)
        rot("Bone_Gear_%s" % tag, y=gear)
        rot("Bone_Crank_%s" % tag, x=gear * 0.3)
        for i in range(N_DIGITS):
            # Spread grows toward the trailing digit, so the fan opens instead
            # of swinging as a rigid block.
            k = i / (N_DIGITS - 1)
            rot("Bone_Digit_%s_%d" % (tag, i + 1),
                z=splay * (k - 0.30) * s,
                y=twist * (0.35 + 0.65 * k))


def tail(splay, pitch):
    rot("Bone_Boom_1", x=pitch * 0.4)
    rot("Bone_Boom_2", x=pitch * 0.6)
    for i in range(N_DIGITS):
        k = i / (N_DIGITS - 1)
        rot("Bone_TailDigit_%d" % (i + 1), z=splay * (k - 0.5) * 2.0)


if POSE == "downstroke":
    wings(flap=-24.0, splay=18.0, twist=-12.0, sweep=-4.0, gear=40.0)
    tail(splay=12.0, pitch=6.0)
elif POSE == "upstroke":
    wings(flap=32.0, splay=-26.0, twist=20.0, sweep=10.0, gear=-30.0)
    tail(splay=-14.0, pitch=-8.0)
elif POSE == "folded":
    wings(flap=6.0, splay=-58.0, twist=26.0, sweep=34.0)
    tail(splay=-32.0, pitch=0.0)
elif POSE == "glide":
    wings(flap=3.0, splay=8.0, twist=-3.0)
    tail(splay=14.0, pitch=-3.0)
elif POSE == "rest":
    pass
else:
    raise SystemExit("unknown pose: %s" % POSE)

bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.view_layer.update()

# Reuse the library preview renderer for framing and lighting. It reads the
# evaluated depsgraph, so the deformed wings are what gets framed and shot.
sys.argv = [sys.argv[0], "--", "--out", OUT, "--view", VIEW, "--res", RES]
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                       "..", "..", "_preview.py")).read())
