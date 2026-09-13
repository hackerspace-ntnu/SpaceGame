# Measures what the Walk clip actually does to the ground, and asserts it.
#
# This is the check behind LightningConjurerBuilder.StrideSpeed. That constant claims
# a ground speed for the clip, and three things downstream are written in terms of it
# -- the blend tree's top threshold, its per-child playback rates and
# AgentAnimatorDriver.animatorSpeedScale -- so a clip that does not actually travel at
# that speed makes the creature skate at every speed, systematically.
#
# Two things about HOW it measures, both of which the old version got wrong and both
# of which flattered the old sinusoidal cycle:
#
#   * it tracks a RIGID POINT on the foot (the Foot bone's head), not the centroid of
#     whatever vertices happen to be lowest. The contact patch slides from heel to
#     sole as the foot pitches, so a centroid moves even on a foot that is welded to
#     the floor -- worth several m/s of imaginary slip at each contact.
#   * it reports the WHOLE stance, not the mean of a window. A mean over sixteen
#     frames of a cycle whose foot speed swings either side of the truth is not the
#     clip's speed; that is how StrideSpeed came to be 25% high.
#
#   blender -b "../ConjuringRobot1 (2) (1) (1).blend" -P stride.py
import bpy, statistics

arm = bpy.data.objects["ConjurerRig"]
arm.animation_data.action = bpy.data.actions["Walk"]
sc = bpy.context.scene

SCALE = (3.019 * 6) / (37.49 - 2.757)   # metres per blender unit; matches the builder
FPS = 30.0
N = int(bpy.data.actions["Walk"].frame_range[1]) - 1     # frames in one full cycle
CONTACT = 0.10          # units above the floor still counted as planted


def foot(side):
    """(height of the foot's rigid centre, its x) this frame."""
    h = arm.pose.bones[f"Foot_{side}"].head
    return h.z, h.x


rows = []
for f in range(1, N + 2):
    sc.frame_set(f)
    bpy.context.view_layer.update()
    rows.append((f,) + foot("L") + foot("R"))

ground = min(min(r[1], r[3]) for r in rows)
print(f"ground = {ground:.3f}   cycle = {N} frames = {N / FPS:.3f}s")

slip = []
for name, zi, xi in (("L", 1, 2), ("R", 3, 4)):
    planted = [r for r in rows if r[zi] < ground + CONTACT]
    vel = [(a[xi] - b[xi]) * FPS
           for a, b in zip(planted, planted[1:]) if b[0] - a[0] == 1]
    print(f"foot {name}: planted {len(planted)}/{N} frames "
          f"(duty {len(planted) / float(N):.2f}), backward speed "
          f"mean {statistics.mean(vel) * SCALE:.3f} "
          f"min {min(vel) * SCALE:.3f} max {max(vel) * SCALE:.3f} m/s")
    slip += vel

mean = statistics.mean(slip)
spread = max(max(slip) - mean, mean - min(slip))
print(f"\nStrideSpeed = {mean * SCALE:.3f} m/s   "
      f"(worst frame is {spread / mean * 100:.2f}% off it)")

# The clip is foot-locked by construction -- anim.py solves the legs FROM the contact
# trajectory rather than the other way round -- so anything but a flat velocity here
# means the trajectory and the rig have come apart, and the number above is no longer
# a speed the builder can trust.
assert spread / mean < 0.02, (
    f"the planted foot's speed swings {spread / mean * 100:.1f}% about its mean; the "
    "clip is not foot-locked and StrideSpeed is a fiction. Check anim.py's foot_track "
    "and its IK residual assert.")
