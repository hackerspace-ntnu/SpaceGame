using SpaceGame.Items;
using SpaceGame.Presentation;
using UnityEditor.Animations;
using UnityEngine;
using static SpaceGame.EditorTools.AnimatorGraph;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The pose layers: what the arms do because of what is in the hand (Upper Body), the left
    /// arm's own pose for a second worn device (Worn Left), and the wingsuit (Glide).
    ///
    /// <para>
    /// These are states, not actions, because they are held for as long as a condition is — an
    /// item in the hand, a gauntlet switched on — and PlayerAimRig / HoldAnimator drive them
    /// through parameters and own their weights. Gestures used to share the Upper Body layer and
    /// needed a <c>Gesturing</c> gate so a hold transition could not evict one mid-play; they now
    /// have action layers of their own above this one, and the gate is gone.
    /// </para>
    /// </summary>
    internal static class HumanoidPoseLayers
    {
        public const string MirroredSuffix = " Mirrored";
        public const string WornLeftPrefix = "Worn Left ";

        public static void BuildUpperBody(AnimatorController controller, HumanoidAnimationProfile profile,
                                          AvatarMask mask)
        {
            HumanoidAnimationProfile.Timings time = profile.Timing;
            AnimatorStateMachine sm = AddLayer(controller, HumanoidLayers.UpperBody, mask, 0f, ikPass: true);

            AnimatorState empty = AddState(sm, HumanoidLayers.EmptyState, null);
            sm.defaultState = empty;
            AnyWhen(sm, empty, time.hold, Is(HumanoidParams.HoldStyle, 0), Is(HumanoidParams.ArmRaise, 0));

            foreach (HumanoidAnimationProfile.HoldPose pose in profile.HoldPoses)
            {
                AnimatorState state = AddState(sm, HoldStateName(pose.style), pose.clip);
                AnyWhen(sm, state, time.hold, Is(HumanoidParams.HoldStyle, (int)pose.style),
                        Is(HumanoidParams.ArmRaise, 0), IfNot(HumanoidParams.HoldMirror));

                // Every hold clip is right-handed; the left arm's ask plays its mirror.
                AnimatorState mirrored = AddState(sm, HoldStateName(pose.style) + MirroredSuffix, pose.clip);
                mirrored.mirror = true;
                AnyWhen(sm, mirrored, time.hold, Is(HumanoidParams.HoldStyle, (int)pose.style),
                        Is(HumanoidParams.ArmRaise, 0), If(HumanoidParams.HoldMirror));
            }

            float range = profile.AimPitchRange;
            AddRaise(controller, sm, "Raise Left", profile.RaiseOneArm, range, mirror: true, 1, time.raise);
            AddRaise(controller, sm, "Raise Right", profile.RaiseOneArm, range, mirror: false, 2, time.raise);
            AddRaise(controller, sm, "Raise Both", profile.RaiseBothArms, range, mirror: false, 3, time.raise);
        }

        public static void BuildWornLeft(AnimatorController controller, HumanoidAnimationProfile profile,
                                         AvatarMask mask)
        {
            HumanoidAnimationProfile.Timings time = profile.Timing;
            AnimatorStateMachine sm = AddLayer(controller, HumanoidLayers.WornLeft, mask, 0f, ikPass: true);

            AnimatorState empty = AddState(sm, HumanoidLayers.EmptyState, null);
            sm.defaultState = empty;
            AnyWhen(sm, empty, time.hold, Is(HumanoidParams.WornLeftStyle, 0));

            foreach (HumanoidAnimationProfile.HoldPose pose in profile.HoldPoses)
            {
                AnimatorState state = AddState(sm, WornLeftPrefix + HoldStateName(pose.style), pose.clip);
                state.mirror = true;
                AnyWhen(sm, state, time.hold, Is(HumanoidParams.WornLeftStyle, (int)pose.style));
            }
        }

        public static void BuildGlide(AnimatorController controller, HumanoidAnimationProfile profile)
        {
            float blend = profile.Timing.glide;
            AnimatorStateMachine sm = AddLayer(controller, HumanoidLayers.Glide, null, 1f, ikPass: false);

            AnimatorState empty = AddState(sm, HumanoidLayers.EmptyState, null);
            sm.defaultState = empty;
            AnimatorState gliding = AddState(sm, "Gliding", profile.Glide);

            AnyWhen(sm, gliding, blend, If(HumanoidParams.IsGliding));
            AnyWhen(sm, empty, blend, IfNot(HumanoidParams.IsGliding));
        }

        public static string HoldStateName(ItemGrip.HoldStyle style) => "Hold " + style;

        private static void AddRaise(AnimatorController controller, AnimatorStateMachine sm, string name,
                                     HumanoidAnimationProfile.PitchBlend clips, float range, bool mirror,
                                     int armRaise, float blend)
        {
            BlendTree tree = Tree1D(controller, name, HumanoidParams.AimPitch,
                                    (clips.down, -range), (clips.level, 0f), (clips.up, range));
            AnimatorState state = AddState(sm, name, tree);
            state.mirror = mirror;
            AnyWhen(sm, state, blend, Is(HumanoidParams.ArmRaise, armRaise));
        }
    }
}
