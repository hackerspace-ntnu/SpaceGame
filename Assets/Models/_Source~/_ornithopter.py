"""Constants shared across the dune ornithopter's component family.

Two things live here because they are needed in more than one script and a
second copy of either would drift silently:

1. **SCALE.** The whole machine is authored at convenient round numbers taken
   off the reference sketch, which put its wingspan at 8.303 m. The brief calls
   for a 6.0 m span, so every component's mesh is scaled on the way out via
   `_buildlib.SCALE`. Scaling the source rather than the objects keeps every
   object's scale at exactly 1.0, so the .blend needs no apply step and the
   parts still bolt together.

2. **Fan hub geometry.** The hub's lug angles are needed by the component that
   builds the lugs *and* by the assembly that pins blades onto them. They have
   to agree exactly or the blades float off their sockets.

    import sys, os
    sys.path.insert(0, "<repo>/models")
    from _ornithopter import *
"""

import math

# Span the layout numbers produce before scaling, measured from the assembled
# model. Change the target, not this.
AUTHORED_SPAN = 8.196
TARGET_SPAN = 10.0

SCALE = TARGET_SPAN / AUTHORED_SPAN      # ≈ 1.2201

# --- the shared component, and why it does not follow TARGET_SPAN -----------
# `components/mechanical/shoulder_gear.blend` is NOT exclusive to this machine:
# `models/creatures/horse_robot.py` and `models/creatures/humanoid_robot.py`
# both append its `Coll_ShoulderGear_*` collections and place the meshes at
# object scale 1.0. They therefore inherit whatever size the file was last
# built at.
#
# That makes `SCALE` the wrong knob for it. Raising the ornithopter's span from
# 6 m to 10 m and rebuilding the component would leave both robots correct
# *today* — their .blends are already built — and silently grow their shoulder
# gears by 1.67x the next time either is regenerated. A latent break that only
# fires months later, in a file nobody was editing, is the worst kind.
#
# So the shared component is pinned to the span it was authored at, and the
# ornithopter's assembly scales the meshes it appends from it by FIXUP on the
# way in. `shoulder_gear.py` imports SHARED_COMPONENT_SCALE rather than SCALE,
# so rebuilding it is a no-op and the coupling is gone rather than merely
# unexercised.
SHARED_COMPONENT_SPAN = 6.0
SHARED_COMPONENT_SCALE = SHARED_COMPONENT_SPAN / AUTHORED_SPAN   # ≈ 0.7226
SHARED_COMPONENT_FIXUP = TARGET_SPAN / SHARED_COMPONENT_SPAN     # ≈ 1.6667

# --- fan hub ---------------------------------------------------------------
LUG_COUNT = 5
LUG_SPREAD = math.radians(98.0)    # total arc from the first lug to the last
LUG_RADIUS = 0.185                 # authored units; lug body centre
LUG_PIN_R = 0.230                  # authored units; where a blade root pins on


def lug_angle(i, count=LUG_COUNT, spread=LUG_SPREAD):
    """Angle of fan socket `i` about the hub's +Z. 0 points straight outboard."""
    if count == 1:
        return 0.0
    return -spread / 2 + spread * i / (count - 1)


# --- webbed wing skeleton --------------------------------------------------
# Authored in the RIGHT wing's local space: origin at the shoulder pivot, +X
# outboard, +Y aft. Needed by the component that builds the spars *and* by the
# assembly that lays bones along them, so they live here rather than in either.
# Plain tuples, so this module imports without mathutils.

WRIST = (1.45, 0.06, 0.03)

DIGIT_TIPS = [
    (3.150, -0.050, -0.115),   # 1 — leading digit, carries the wingtip
    (2.880,  0.720, -0.150),
    (2.360,  1.410, -0.180),
    (1.730,  1.940, -0.195),
    (1.010,  2.200, -0.205),
]

# Where the inner membrane runs back onto the fuselage.
ROOT_ANCHOR = (0.075, 0.880, -0.030)

DIGIT_COUNT = len(DIGIT_TIPS)

# Vertex groups every webbed panel declares, in a fixed order. Weights are
# stored against group *indices*, so the assembly can rename these per side
# without touching the weight data — but only while the order holds.
SKIN_GROUPS = ["VG_Root", "VG_Arm"] + [
    "VG_Digit_%d" % (i + 1) for i in range(DIGIT_COUNT)]

# --- tail fan --------------------------------------------------------------
TAIL_SPREAD_DEG = 104.0
TAIL_REACH = 1.16


def tail_tips():
    """Digit tips of the tail fan, in the tail hub's local space."""
    spread = math.radians(TAIL_SPREAD_DEG)
    out = []
    for i in range(DIGIT_COUNT):
        a = -spread / 2 + spread * i / (DIGIT_COUNT - 1)
        out.append((TAIL_REACH * math.cos(a), TAIL_REACH * math.sin(a),
                    -0.055 - 0.02 * abs(i - 2)))
    return out
