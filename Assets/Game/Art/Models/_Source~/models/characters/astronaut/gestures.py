"""Author the player's gesture clips -- the one-shots the Upper Body layer plays over
whatever the body is doing -- and export them as FBX for `PlayerGestureBuilder`.

    blender --background astronaut.blend --python gestures.py
    blender --background astronaut.blend --python gestures.py -- --preview out.png
    blender --background astronaut.blend --python gestures.py -- --only Stab,Wave

Two kinds of clip come out of this:

  Aimed gestures, right arm, one clip per look pitch, played MIRRORED for a device on
  the left forearm (there is no Left set, for the reason `gauntlet_point.py` gives):

    Stab Right {Down,Level,Up}.fbx    the Wrist Blade's lunge, 36 frames (1.2 s)
    Punch Right {Down,Level,Up}.fbx   the Sucker Puncher's straight, 24 frames (0.8 s)

  Emotes, both arms, one clip, no pitch -- played on a /command:

    Wave.fbx     2.0 s     Cheer.fbx    1.6 s     Shrug.fbx    1.4 s     Flex.fbx    1.8 s

Every aimed clip starts and ends in the Point pose `gauntlet_point.py` holds while a
gauntlet fires, so the layer has nothing to blend across on the way in or out. The chest
is in the Upper Body mask, so the torso twists with the arm: a stab is a whole upper body
turning behind a point, not an arm moving on a statue (GDC-L1-ANIM-0002 -- anticipation,
then the action, then the settle).

Same conventions as `sit_idle.py`, whose helpers this imports: armature space is Y-up,
+Z forward, +X the character's LEFT, in centimetres; the .blend is never written back to.
"""
import math
import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import sit_idle  # noqa: E402
import gauntlet_point as point  # noqa: E402

CLIP_DIR = os.path.join(sit_idle.REPO, "Assets", "Game", "Art", "Animations", "Player")
FPS = 30
B = sit_idle.B

SPINE = ["Spine", "Spine1", "Spine2"]
ARM = ["Shoulder", "Arm", "ForeArm", "Hand"]


# ── Easing ──────────────────────────────────────────────────────────────────

def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3.0 - 2.0 * t)


def snap(t):
    """Fast out: most of the travel in the first half."""
    t = max(0.0, min(1.0, t))
    return 1.0 - (1.0 - t) ** 3


def lerp(a, b, t):
    return a + (b - a) * t


def phases(frame, *ends):
    """Which segment `frame` is in, and how far through it, given segment end frames."""
    start = 0
    for i, end in enumerate(ends):
        if frame <= end:
            return i, (frame - start) / float(max(1, end - start))
        start = end
    return len(ends), 1.0


# ── Posing primitives ───────────────────────────────────────────────────────

def twist(arm, yaw, lean, roll=0.0):
    """Turn the chest: `yaw` about the vertical axis (+ drives the RIGHT shoulder
    forward and the left one back), `lean` forward about X (+ is forward), `roll` about Z.
    Spread over the three spine joints so the back curves instead of hinging."""
    for name, share in (("Spine", 0.25), ("Spine1", 0.35), ("Spine2", 0.40)):
        if yaw:
            sit_idle.rotate(arm, name, yaw * share, 'Y')
        if lean:
            sit_idle.rotate(arm, name, lean * share, 'X')
        if roll:
            sit_idle.rotate(arm, name, roll * share, 'Z')


def limb(arm, side, sign, shoulder_fwd, arm_fwd, arm_out, fore_fwd, fore_out, hand_fwd=None, hand_out=None):
    """One arm: collarbone forward, upper arm and forearm each aimed by (forward, out).
    The hand continues the forearm unless told otherwise."""
    sit_idle.rotate(arm, side + "Shoulder", -shoulder_fwd, 'X')
    sit_idle.aim(arm, side + "Arm", sit_idle.limb_dir(sign, arm_fwd, arm_out))
    sit_idle.aim(arm, side + "ForeArm", sit_idle.limb_dir(sign, fore_fwd, fore_out))
    sit_idle.aim(arm, side + "Hand", sit_idle.limb_dir(
        sign, fore_fwd if hand_fwd is None else hand_fwd, fore_out if hand_out is None else hand_out))


def rest_arm(arm, side, sign):
    """The rig's own hanging arm, nudged so it does not clip the hips of this wide suit."""
    limb(arm, side, sign, 0.0, 4.0, 14.0, 8.0, 12.0)


# ── The aimed gestures ──────────────────────────────────────────────────────
#
# Both are described as poses at their key moments, blended by the phase easing.
# `pitch` is the forearm's elevation above horizontal, from the Point clips' table.

def stab(arm, frame, pitch, frames=36):
    """Chamber (elbow high and back, chest turned away, off hand up), the lunge (chest
    snaps forward behind a locked arm, off arm sweeps back for balance), a hold with a
    settle -- long, because the blade slides out of the locked arm during it and the
    slide is meant to be watched -- then a slow recovery to Point."""
    seg, t = phases(frame, 7, 11, 24, frames)
    fwd = 90.0 + pitch

    # Amounts: chamber 0..1 then held, extend 0..1 then held, both easing off in recovery.
    if seg == 0:
        chamber, extend, settle = smooth(t), 0.0, 0.0
    elif seg == 1:
        chamber, extend, settle = 1.0, snap(t), 0.0
    elif seg == 2:
        chamber, extend, settle = 1.0, 1.0, smooth(t)
    else:
        e = 1.0 - smooth(t)
        chamber, extend, settle = e, e, 1.0

    # Chest: away in the chamber, then through and past neutral toward the target.
    # Right-arm blade: the right shoulder draws BACK (yaw -) then drives FORWARD (yaw +).
    yaw = lerp(0.0, -22.0, chamber)
    yaw = lerp(yaw, 16.0 - 4.0 * settle, extend)
    lean = lerp(0.0, -5.0, chamber)
    lean = lerp(lean, 12.0 - 2.0 * settle, extend)
    twist(arm, yaw, lean)

    # Blade arm.
    arm_fwd = lerp(fwd - point.ELBOW_DROP, -28.0, chamber)      # elbow drawn BEHIND the ribs
    arm_fwd = lerp(arm_fwd, fwd - 1.0, extend)
    arm_out = lerp(point.ARM_OUT + 3.0, 32.0, chamber)          # and out wide
    arm_out = lerp(arm_out, 2.0, extend)                           # then driven to the midline
    fore_fwd = lerp(fwd, 92.0, chamber)                            # forearm level, hand at the ribs
    fore_fwd = lerp(fore_fwd, fwd, extend)
    fore_out = lerp(point.ARM_OUT, 14.0, chamber)
    fore_out = lerp(fore_out, 1.0, extend)
    shoulder = lerp(point.SHOULDER_FWD, -8.0, chamber)             # collarbone pulled back
    shoulder = lerp(shoulder, 24.0, extend)                        # then thrown forward
    limb(arm, "Right", -1.0, shoulder, arm_fwd, arm_out, fore_fwd, fore_out)

    # Off arm: up as a guard in the chamber, swept back and down in the lunge.
    g_arm_fwd = lerp(fwd - point.ELBOW_DROP, 45.0, chamber)
    g_arm_fwd = lerp(g_arm_fwd, 15.0, extend)
    g_arm_out = lerp(point.ARM_OUT + 3.0, 26.0, chamber)
    g_arm_out = lerp(g_arm_out, 30.0, extend)
    g_fore_fwd = lerp(fwd, 165.0, chamber)                          # forearm folded up to the chin
    g_fore_fwd = lerp(g_fore_fwd, 40.0, extend)
    g_fore_out = lerp(point.ARM_OUT, 22.0, chamber)
    g_fore_out = lerp(g_fore_out, 24.0, extend)
    limb(arm, "Left", 1.0, lerp(point.SHOULDER_FWD, 4.0, chamber), g_arm_fwd, g_arm_out, g_fore_fwd, g_fore_out)


def punch(arm, frame, pitch, frames=24):
    """Wind-up (fist to the ribs, chest coiled away), the straight (arm shot out along
    the look line, chest snapped through, off arm guarding), a short hold, recovery."""
    seg, t = phases(frame, 6, 9, 13, frames)
    fwd = 90.0 + pitch

    if seg == 0:
        coil, strike = smooth(t), 0.0
    elif seg == 1:
        coil, strike = 1.0, snap(t)
    elif seg == 2:
        coil, strike = 1.0, 1.0
    else:
        e = 1.0 - smooth(t)
        coil, strike = e, e

    yaw = lerp(0.0, -26.0, coil)
    yaw = lerp(yaw, 20.0, strike)
    lean = lerp(0.0, -4.0, coil)
    lean = lerp(lean, 10.0, strike)
    twist(arm, yaw, lean)

    # Fist drawn to the ribs: upper arm back and out, forearm folded so the hand sits
    # beside the chest; then everything along the line, elbow locked.
    arm_fwd = lerp(fwd - point.ELBOW_DROP, -22.0, coil)         # elbow behind the back
    arm_fwd = lerp(arm_fwd, fwd - 1.0, strike)
    arm_out = lerp(point.ARM_OUT + 3.0, 26.0, coil)
    arm_out = lerp(arm_out, 3.0, strike)
    fore_fwd = lerp(fwd, 108.0, coil)                              # fist cocked at the ribs
    fore_fwd = lerp(fore_fwd, fwd, strike)
    fore_out = lerp(point.ARM_OUT, 18.0, coil)
    fore_out = lerp(fore_out, 2.0, strike)
    shoulder = lerp(point.SHOULDER_FWD, -10.0, coil)
    shoulder = lerp(shoulder, 22.0, strike)
    limb(arm, "Right", -1.0, shoulder, arm_fwd, arm_out, fore_fwd, fore_out)

    # Off arm: a guard, fist up by the jaw, held through the punch.
    g_fwd = lerp(fwd - point.ELBOW_DROP, 35.0, coil)
    g_out = lerp(point.ARM_OUT + 3.0, 24.0, coil)
    g_fore = lerp(fwd, 178.0, coil)                                 # fist straight up, by the jaw
    g_fore_out = lerp(point.ARM_OUT, 12.0, coil)
    limb(arm, "Left", 1.0, lerp(point.SHOULDER_FWD, 2.0, coil), g_fwd, g_out, g_fore, g_fore_out)


# ── The emotes ──────────────────────────────────────────────────────────────
#
# Start and end at the rig's rest pose (both arms hanging), so they blend from and back
# to whatever the layer was doing with no pose of their own to return to.

def wave(arm, frame, frames=60):
    """Right arm up high, forearm swinging side to side three times."""
    seg, t = phases(frame, 10, 48, frames)
    up = smooth(t) if seg == 0 else (1.0 - smooth(t) if seg == 2 else 1.0)
    swing = math.sin(3.0 * 2.0 * math.pi * ((frame - 10) / 38.0)) if seg == 1 else 0.0
    swing *= math.sin(math.pi * (frame - 10) / 38.0) ** 0.5 if seg == 1 else 0.0
    twist(arm, 5.0 * up, 0.0)
    limb(arm, "Right", -1.0, 6.0 * up,
         lerp(4.0, 150.0, up), lerp(14.0, 30.0, up),
         lerp(8.0, 165.0, up), lerp(12.0, 20.0 + 28.0 * swing, up))
    rest_arm(arm, "Left", 1.0)


def cheer(arm, frame, frames=48):
    """Both arms thrown up, two pumps, chest back."""
    seg, t = phases(frame, 8, 40, frames)
    up = smooth(t) if seg == 0 else (1.0 - smooth(t) if seg == 2 else 1.0)
    pump = 0.5 - 0.5 * math.cos(2.0 * 2.0 * math.pi * ((frame - 8) / 32.0)) if seg == 1 else 0.0
    twist(arm, 0.0, -8.0 * up)
    for side, sign in (("Right", -1.0), ("Left", 1.0)):
        limb(arm, side, sign, 8.0 * up,
             lerp(4.0, 160.0, up), lerp(14.0, 24.0, up),
             lerp(8.0, 172.0 - 30.0 * pump, up), lerp(12.0, 18.0, up))


def shrug(arm, frame, frames=42):
    """Elbows in, forearms out flat with the palms up, shoulders hunched; held, then dropped."""
    seg, t = phases(frame, 9, 30, frames)
    up = smooth(t) if seg == 0 else (1.0 - smooth(t) if seg == 2 else 1.0)
    twist(arm, 0.0, -3.0 * up)
    for side, sign in (("Right", -1.0), ("Left", 1.0)):
        # Collarbone up rather than forward: rotate about Z toward the neck.
        sit_idle.rotate(arm, side + "Shoulder", -sign * 14.0 * up, 'Z')
        limb(arm, side, sign, 0.0,
             lerp(4.0, 18.0, up), lerp(14.0, 22.0, up),
             lerp(8.0, 92.0, up), lerp(12.0, 42.0, up))


def flex(arm, frame, frames=54):
    """The double bicep: upper arms out level, forearms folded up, a squeeze, then down."""
    seg, t = phases(frame, 12, 42, frames)
    up = smooth(t) if seg == 0 else (1.0 - smooth(t) if seg == 2 else 1.0)
    squeeze = 0.5 - 0.5 * math.cos(2.0 * math.pi * ((frame - 12) / 30.0)) if seg == 1 else 0.0
    twist(arm, 0.0, -4.0 * up)
    for side, sign in (("Right", -1.0), ("Left", 1.0)):
        limb(arm, side, sign, 4.0 * up,
             lerp(4.0, 95.0, up), lerp(14.0, 80.0 - 6.0 * squeeze, up),
             lerp(8.0, 165.0, up), lerp(12.0, 30.0 - 10.0 * squeeze, up))


# ── The table ───────────────────────────────────────────────────────────────

AIMED = {
    "Stab": (stab, 36),
    "Punch": (punch, 24),
}

EMOTES = {
    "Wave": (wave, 60),
    "Cheer": (cheer, 48),
    "Shrug": (shrug, 42),
    "Flex": (flex, 54),
}


def keyed_bones():
    names = list(SPINE)
    for side in ("Left", "Right"):
        names += [side + b for b in ARM]
    return names


def apply_aimed(arm, fn, frame, pitch):
    # Each gesture poses both arms in full; at its neutral it IS the Point pose, so
    # nothing is pre-posed (the shoulder rotates would otherwise stack).
    sit_idle.rest_pose(arm)
    fn(arm, frame, pitch)


def apply_emote(arm, fn, frame):
    sit_idle.rest_pose(arm)
    fn(arm, frame)


def build_action(arm, name, frames, pose_at):
    arm.animation_data_create()
    for old in [a for a in bpy.data.actions if a.name == name]:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    arm.animation_data.action = action

    scene = bpy.context.scene
    scene.render.fps = FPS
    scene.frame_start = 1
    scene.frame_end = frames

    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'

    for frame in range(1, frames + 1):
        pose_at(frame)
        for bone in keyed_bones():
            arm.pose.bones[B % bone].keyframe_insert("rotation_quaternion", frame=frame)
    return action


def report(arm, label, frames):
    scene = bpy.context.scene
    hand = arm.pose.bones[B % "RightHand"]
    chest = arm.pose.bones[B % "Spine2"]
    hips = arm.pose.bones[B % "Hips"]
    for frame in sorted({1, frames // 4, frames // 2, frames}):
        scene.frame_set(frame)
        sit_idle.sync()
        d = hand.matrix.to_translation() - chest.matrix.to_translation()
        h = hand.matrix.to_translation() - hips.matrix.to_translation()
        print("  %-12s frame %2d  right hand %4.0f cm ahead of the chest, %4.0f up, %4.0f out; %3.0f above the hips"
              % (label, frame, d.z, d.y, -d.x, h.y))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    arm = sit_idle.armature()
    only = argv[argv.index("--only") + 1].split(",") if "--only" in argv else None

    if "--preview" in argv:
        base = argv[argv.index("--preview") + 1]
        root, ext = os.path.splitext(base)
        # Posed directly rather than through an action: the preview helper renders frame 1.
        for name, (fn, frames) in AIMED.items():
            if only and name not in only:
                continue
            for frame in sorted({1, frames // 4, frames // 3, frames // 2}):
                apply_aimed(arm, fn, frame, point.PITCHES["Level"])
                sit_idle.sync()
                sit_idle.preview("%s_%s_f%02d%s" % (root, name, frame, ext or ".png"), 0.0)
        for name, (fn, frames) in EMOTES.items():
            if only and name not in only:
                continue
            for frame in sorted({frames // 4, frames // 2}):
                apply_emote(arm, fn, frame)
                sit_idle.sync()
                sit_idle.preview("%s_%s_f%02d%s" % (root, name, frame, ext or ".png"), 0.0)
        return

    for name, (fn, frames) in AIMED.items():
        if only and name not in only:
            continue
        for pitch_name, pitch in point.PITCHES.items():
            clip = "%s Right %s" % (name, pitch_name)
            build_action(arm, clip, frames, lambda f: apply_aimed(arm, fn, f, pitch))
            report(arm, clip, frames)
            point.export(arm, os.path.join(CLIP_DIR, clip + ".fbx"))

    for name, (fn, frames) in EMOTES.items():
        if only and name not in only:
            continue
        build_action(arm, name, frames, lambda f: apply_emote(arm, fn, f))
        report(arm, name, frames)
        point.export(arm, os.path.join(CLIP_DIR, name + ".fbx"))


if __name__ == "__main__":
    main()
