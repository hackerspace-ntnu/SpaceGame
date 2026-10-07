using System.Collections.Generic;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Writes one AnimatorOverrideController per locomotion variant: the humanoid controller with
    /// the default set's clips swapped, slot for slot, for the variant's.
    ///
    /// <para>
    /// An override swaps a clip EVERYWHERE the controller uses it, so a variant may not replace a
    /// clip an action also plays — a formal walk that also rewrote an idle break would change that
    /// action for every body wearing the variant. That is refused here, loudly, rather than left to
    /// be discovered on screen.
    /// </para>
    /// <para>
    /// The override keeps the controller's layers, states and parameters, so NGO's NetworkAnimator
    /// (which unwraps overrides when it bakes) and CharacterActions work on it unchanged.
    /// </para>
    /// </summary>
    internal static class LocomotionOverrides
    {
        public const string Folder = HumanoidControllerBuilder.Folder + "Variants/";

        public static string PathFor(LocomotionSet variant) => Folder + "Humanoid_" + variant.name + ".overrideController";

        public static void Build(AnimatorController controller, HumanoidAnimationProfile profile,
                                 IReadOnlyList<CharacterAction> actions)
        {
            var actionClips = new HashSet<AnimationClip>();
            foreach (CharacterAction action in actions)
                for (int v = 0; v < action.VariantCount; v++)
                    AddVariantClips(actionClips, action.GetVariant(v));

            if (profile.LocomotionVariants.Length > 0) HumanoidControllerBuilder.EnsureFolder(Folder);

            foreach (LocomotionSet variant in profile.LocomotionVariants)
            {
                if (variant == null) continue;

                Dictionary<AnimationClip, AnimationClip> swaps = Swaps(profile.Locomotion, variant);
                foreach (AnimationClip original in swaps.Keys)
                {
                    if (actionClips.Contains(original))
                    {
                        throw new System.InvalidOperationException(
                            $"Locomotion variant '{variant.name}' replaces '{original.name}', which an action " +
                            "also plays. An override swaps a clip everywhere, so the action would change " +
                            "for every body wearing the variant. Give the action its own copy of the clip.");
                    }
                }

                Write(controller, PathFor(variant), swaps);
            }
        }

        private static void Write(AnimatorController controller, string path, Dictionary<AnimationClip, AnimationClip> swaps)
        {
            var overrides = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
            if (overrides == null)
            {
                overrides = new AnimatorOverrideController(controller);
                AssetDatabase.CreateAsset(overrides, path);
            }
            overrides.runtimeAnimatorController = controller;

            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrides.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
            {
                swaps.TryGetValue(pairs[i].Key, out AnimationClip replacement);
                pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(pairs[i].Key, replacement);
            }
            overrides.ApplyOverrides(pairs);
            EditorUtility.SetDirty(overrides);
        }

        /// <summary>The default set's clip → the variant's, for every slot the variant fills.</summary>
        private static Dictionary<AnimationClip, AnimationClip> Swaps(LocomotionSet baseline, LocomotionSet variant)
        {
            var swaps = new Dictionary<AnimationClip, AnimationClip>();

            void Swap(AnimationClip from, AnimationClip to)
            {
                if (from != null && to != null && from != to) swaps[from] = to;
            }

            for (int i = 0; i < Mathf.Min(baseline.Idles.Length, variant.Idles.Length); i++)
                Swap(baseline.Idles[i], variant.Idles[i]);
            for (int i = 0; i < Mathf.Min(baseline.Move.Length, variant.Move.Length); i++)
                Swap(baseline.Move[i].clip, variant.Move[i].clip);
            for (int i = 0; i < Mathf.Min(baseline.Crouch.Length, variant.Crouch.Length); i++)
                Swap(baseline.Crouch[i].clip, variant.Crouch[i].clip);

            Swap(baseline.JumpUp, variant.JumpUp);
            Swap(baseline.Fall, variant.Fall);
            Swap(baseline.Land, variant.Land);
            Swap(baseline.Sit, variant.Sit);
            return swaps;
        }

        private static void AddVariantClips(HashSet<AnimationClip> into, CharacterAction.Variant variant)
        {
            foreach (AnimationClip clip in new[] { variant.clip, variant.aimDown, variant.aimUp, variant.enter, variant.exit })
                if (clip != null) into.Add(clip);
        }
    }
}
