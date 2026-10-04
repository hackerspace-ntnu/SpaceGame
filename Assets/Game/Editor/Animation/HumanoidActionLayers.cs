using System.Collections.Generic;
using SpaceGame.Presentation;
using UnityEditor.Animations;
using UnityEngine;
using static SpaceGame.EditorTools.AnimatorGraph;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// The action layers — Full, Upper, Left Arm, Right Arm, Additive — one state per action variant.
    ///
    /// <para>
    /// The Additive layer is built exactly like the others but blends additively: Unity adds each
    /// clip's muscle values minus the clip's own first frame (made explicit on the generated
    /// recoil clips, see <see cref="RecoilClipGenerator"/>) onto the pose the layers beneath
    /// settled on. That is what lets one recoil clip serve every hold pose and every aim pitch; a
    /// level shot on an overriding layer snapped an aimed arm level on every shot.
    /// </para>
    /// <para>
    /// No Any State transitions and no trigger parameters: <see cref="CharacterActions"/> enters a
    /// state by crossfading to its name hash, so the per-frame cost of a layer does not grow with
    /// the number of actions on it, and there is no parameter whose name can drift out of step
    /// with the code. The only transitions generated are the ones a state takes by itself: a
    /// one-shot back to Empty at its end, an enter clip into its loop, an exit clip back to Empty.
    /// </para>
    /// </summary>
    internal static class HumanoidActionLayers
    {
        public static void Build(AnimatorController controller, HumanoidAnimationProfile profile,
                                 IReadOnlyList<CharacterAction> actions, CharacterAction.Slot slot, AvatarMask mask)
        {
            AnimatorLayerBlendingMode blending = HumanoidLayers.IsAdditive(slot)
                ? AnimatorLayerBlendingMode.Additive
                : AnimatorLayerBlendingMode.Override;
            AnimatorStateMachine sm = AddLayer(controller, HumanoidLayers.ForSlot(slot), mask, 0f, ikPass: false, blending);
            AnimatorState empty = AddState(sm, HumanoidLayers.EmptyState, null);
            sm.defaultState = empty;

            foreach (CharacterAction action in actions)
            {
                if (action == null || action.BodySlot != slot) continue;

                for (int v = 0; v < action.VariantCount; v++)
                    AddVariant(controller, profile, sm, empty, action, v, slot);
            }
        }

        private static void AddVariant(AnimatorController controller, HumanoidAnimationProfile profile,
                                       AnimatorStateMachine sm, AnimatorState empty,
                                       CharacterAction action, int index, CharacterAction.Slot slot)
        {
            CharacterAction.Variant variant = action.GetVariant(index);
            AnimatorState main = AddActionState(sm, HumanoidLayers.StateName(action, index, HumanoidLayers.Stage.Main),
                                                MainMotion(controller, profile, action, index), slot);

            switch (action.Mode)
            {
                case CharacterAction.Playback.OneShot:
                    AtEnd(main, empty, ExitTimeFor(variant.clip, action.FadeOut), action.FadeOut);
                    break;

                case CharacterAction.Playback.EnterLoopExit:
                    AnimatorState enter = AddActionState(sm, HumanoidLayers.StateName(action, index, HumanoidLayers.Stage.Enter),
                                                         variant.enter, slot);
                    AnimatorState exit = AddActionState(sm, HumanoidLayers.StateName(action, index, HumanoidLayers.Stage.Exit),
                                                        variant.exit, slot);
                    AtEnd(enter, main, ExitTimeFor(variant.enter, action.FadeIn), action.FadeIn);
                    AtEnd(exit, empty, ExitTimeFor(variant.exit, action.FadeOut), action.FadeOut);
                    break;
            }
        }

        /// <summary>
        /// Every action state reads its slot's speed and mirror parameters, so one state serves
        /// both arms and every character's own tempo.
        /// </summary>
        private static AnimatorState AddActionState(AnimatorStateMachine sm, string name, Motion motion,
                                                    CharacterAction.Slot slot)
        {
            AnimatorState state = AddState(sm, name, motion);
            state.speedParameterActive = true;
            state.speedParameter = HumanoidParams.ActionSpeed(slot);
            state.mirrorParameterActive = true;
            state.mirrorParameter = HumanoidParams.ActionMirror(slot);
            return state;
        }

        private static Motion MainMotion(AnimatorController controller, HumanoidAnimationProfile profile,
                                         CharacterAction action, int index)
        {
            CharacterAction.Variant variant = action.GetVariant(index);
            if (!variant.IsAimed) return variant.clip;

            float range = profile.AimPitchRange;
            return Tree1D(controller, HumanoidLayers.StateName(action, index, HumanoidLayers.Stage.Main),
                          HumanoidParams.AimPitch,
                          (variant.aimDown, -range), (variant.clip, 0f), (variant.aimUp, range));
        }

        /// <summary>The normalized time at which a fade of <paramref name="fade"/> seconds ends exactly at the clip's end.</summary>
        private static float ExitTimeFor(AnimationClip clip, float fade)
        {
            if (clip == null || clip.length <= 0f) return 1f;
            return Mathf.Clamp01(1f - fade / clip.length);
        }
    }
}
