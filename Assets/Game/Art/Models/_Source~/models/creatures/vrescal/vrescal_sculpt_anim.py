"""Drive the existing Vrescal animation solver against the sculpt's rig.

    blender --background vrescal_rigged.blend --python vrescal_sculpt_anim.py

Writes the six actions Unity's `VrescalBuilder` expects, in place, with the
frame counts it hardcodes: Idle 91, Walk 37, Run 25, Attack 40, Hurt 20,
Death 64.

## Reuse rather than rewrite

`vrescal_anim.py` already solves all six clips -- closed-form two-bone IK from
an explicit gait schedule, measured zero foot slide -- and none of that is worth
re-deriving. It reads geometry from the armature itself (`Rig` takes every bone
length and rest matrix off the rig), so almost all of it is already agnostic to
which animal it is driving.

Its only hard dependency is a module-level `import vrescal_rebuild as R`, and it
touches exactly six names on it. So rather than edit a working 700-line solver,
this installs a stand-in module under that name in `sys.modules` *before*
importing it. Nothing on disk is modified and the solver cannot tell.

## Symmetrising the stance

The sculpt was reconstructed from a painting of an animal standing mid-stride,
and its hind feet are staggered by 0.73 m fore-and-aft:

    RearP  x -0.60        RearS  x -1.24

`vrescal_rig.py` puts the bones on that real geometry, because weights must
follow the mesh that exists. But the gait scheduler places each foot relative to
its *rest* ankle, so shipping the stagger would mean the two hind feet never
occupy the same ground: across a walk cycle one would sweep x -1.29..0.09 and
the other -1.93..-0.55. The animal would look like it was dragging a leg.

So the rest ankles the *scheduler* sees are averaged into a symmetric pair,
while the bind pose is left alone. The legs then bend to reach, which is what
the IK is there for. The correction is 0.32 m per hind foot.

That does interact with one known trap. The rest pose stands at essentially full
leg extension, so a foot displaced horizontally cannot quite be reached without
the hip dropping -- which is exactly why every locomotion clip applies `crouch`
before solving. Idle does not crouch, so its hind legs solve about 3 cm short of
target and simply stand straight. That is invisible, and the alternative --
forcing the bind pose symmetric -- would put every hind-leg bone outside the
mesh it drives.
"""

import os
import sys
import types

import bpy
from mathutils import Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
CREATURES = os.path.dirname(HERE)
for p in (CREATURES, HERE):
    if p not in sys.path:
        sys.path.insert(0, p)

UNITS_PER_M = 3.62450
GROUND = -13.0

# ---- the stand-in for vrescal_rebuild -------------------------------------
#
# Six names, all of them describing the feet and the working space. Measured off
# this sculpt by `vrescal_rig.measure_feet`, not carried over.

_shim = types.ModuleType("vrescal_rebuild")
_shim.GROUND = GROUND
_shim.UNITS_PER_M = UNITS_PER_M
_shim.FOOT_SCALE = UNITS_PER_M
_shim.LIMBS = {"FrontP": {"foot": "Front"}, "RearP": {"foot": "Rear"}}
_shim.FOOT_HEIGHT_M = {"Front": 0.42, "Rear": 0.42}
_shim.FOOT_SOLE_M = {"Front": 0.43, "Rear": 0.34}
sys.modules["vrescal_rebuild"] = _shim

import vrescal_anim as V          # noqa: E402


def symmetrise(sk):
    """Average each fore/hind pair's rest ankle and foot direction.

    Only the scheduler's view of the stance changes; the armature's bind pose
    is untouched.
    """
    for a, b in (("FrontP", "FrontS"), ("RearP", "RearS")):
        pa, pb = sk.ankle[a], sk.ankle[b]
        mx = (pa.x + pb.x) * 0.5
        mz = (pa.z + pb.z) * 0.5
        my = (abs(pa.y) + abs(pb.y)) * 0.5
        print("    %-7s x %+6.2f -> %+6.2f m   %-7s x %+6.2f -> %+6.2f m"
              % (a, pa.x / UNITS_PER_M, mx / UNITS_PER_M,
                 b, pb.x / UNITS_PER_M, mx / UNITS_PER_M))
        sk.ankle[a] = Vector((mx, my, mz))
        sk.ankle[b] = Vector((mx, -my, mz))

        da, db = sk.foot_dir[a], sk.foot_dir[b]
        d = Vector((da.x + db.x, da.y - db.y, da.z + db.z)) * 0.5
        if d.length < 1e-6:
            continue
        d.normalize()
        sk.foot_dir[a] = d
        sk.foot_dir[b] = Vector((d.x, -d.y, d.z))


def main():
    arm = bpy.data.objects.get("Arm_Vrescal")
    if arm is None:
        raise SystemExit("No Arm_Vrescal -- run vrescal_rig.py first.")

    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)

    rig = V.Rig(arm)
    sk = V.Skeleton(rig)
    print("  symmetrising the stance:")
    symmetrise(sk)

    built = []
    for name, frames, loop, fn in (
            ("Vrescal_Idle", 90, True, V.idle_frame(rig, sk)),
            ("Vrescal_Walk", V.WALK.frames, True,
             V.locomotion_frame(rig, sk, V.WALK)),
            ("Vrescal_Run", V.RUN.frames, True,
             V.locomotion_frame(rig, sk, V.RUN)),
            ("Vrescal_Attack", 40, False, None),
            ("Vrescal_Hurt", 20, False, V.hurt_frame(rig, sk)),
            ("Vrescal_Death", 64, False, V.death_frame(rig, sk))):
        if fn is None:
            fn = V.attack_frame(rig, sk, frames)
        act = V.write_action(rig, sk, name, frames, fn, loop=loop)
        built.append("%s 1-%d%s" % (name, int(act.frame_range[1]),
                                    " loop" if loop else ""))

    # Leave the rig at rest, so the file opens looking like the animal rather
    # than like the last frame of the death clip.
    arm.animation_data.action = None
    for pb in arm.pose.bones:
        pb.location = (0.0, 0.0, 0.0)
        pb.rotation_quaternion = Quaternion((1.0, 0.0, 0.0, 0.0))

    print("\n  actions: %s" % ", ".join(built))
    print("  walk stride %.2f m, run stride %.2f m"
          % (V.WALK.stride / UNITS_PER_M, V.RUN.stride / UNITS_PER_M))
    bpy.ops.wm.save_mainfile()
    print("  saved %s" % bpy.data.filepath)


if __name__ == "__main__":
    main()
