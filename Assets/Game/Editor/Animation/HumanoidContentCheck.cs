using System.Collections.Generic;
using System.Linq;
using SpaceGame.Items;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// What the builder refuses to build from, and the clips it could be building from but is not.
    ///
    /// <para>
    /// Every rule here stands in for a failure Unity would otherwise have been silent about: a null
    /// clip is a state that plays the bind pose, a generic clip on a humanoid animates nothing, two
    /// actions with one name collide on one state and only the second ever plays.
    /// </para>
    /// </summary>
    internal static class HumanoidContentCheck
    {
        /// <summary>Where humanoid clips live; the Audit lists what in here nothing uses yet.</summary>
        public static readonly string[] LibraryFolders =
        {
            "Assets/Game/Art/Animations/Player",
            "Assets/ThirdParty/Kevin Iglesias",
            QuaterniusClipImporter.Folder.TrimEnd('/'),
            CmuClipImporter.Folder.TrimEnd('/'),
        };

        public static void ThrowIfInvalid(HumanoidAnimationProfile profile, IReadOnlyList<CharacterAction> actions)
        {
            List<string> problems = Problems(profile, actions);
            if (problems.Count > 0)
            {
                throw new System.InvalidOperationException(
                    "The humanoid controller was not rebuilt:\n  " + string.Join("\n  ", problems));
            }
        }

        public static List<string> Problems(HumanoidAnimationProfile profile, IReadOnlyList<CharacterAction> actions)
        {
            var problems = new List<string>();
            CheckProfile(profile, problems);

            foreach (IGrouping<string, CharacterAction> same in actions.GroupBy(a => a.name.ToLowerInvariant()))
                if (same.Count() > 1) problems.Add($"{same.Count()} actions are named '{same.Key}'; names must be unique.");

            foreach (CharacterAction action in actions)
                CheckAction(action, problems);
            return problems;
        }

        /// <summary>"path: clip" for every humanoid clip under <see cref="LibraryFolders"/> nothing uses.</summary>
        public static IEnumerable<string> UnusedLibraryClips(HumanoidAnimationProfile profile, IReadOnlyList<CharacterAction> actions)
        {
            HashSet<AnimationClip> used = UsedClips(profile, actions);

            foreach (string path in AssetDatabase.FindAssets("t:AnimationClip", LibraryFolders)
                                                 .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p))
            {
                foreach (AnimationClip clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (clip.name.StartsWith("__preview__") || used.Contains(clip)) continue;
                    yield return $"{path}: {clip.name}";
                }
            }
        }

        /// <summary>
        /// "cue: why" for every cue nothing answers, even through its fallbacks — vocabulary a
        /// reaction or a caller can ask for and get nothing. Not a problem to refuse a build over:
        /// a word may be registered before its clips exist.
        /// </summary>
        public static IEnumerable<string> UnansweredCues(IReadOnlyList<CharacterCue> cues, IReadOnlyList<CharacterAction> actions)
        {
            var answered = new HashSet<CharacterCue>(actions.SelectMany(a => a.Cues).Where(c => c != null));
            foreach (CharacterCue cue in cues)
            {
                CharacterCue step = cue;
                for (int depth = 0; step != null && depth <= BodyLanguage.MaxFallbacks && !answered.Contains(step); depth++) step = step.Fallback;
                if (step == null || !answered.Contains(step))
                    yield return $"{cue.name}: no action is tagged with it or its fallbacks";
            }
        }

        private static void CheckProfile(HumanoidAnimationProfile profile, List<string> problems)
        {
            LocomotionSet set = profile.Locomotion;
            if (set == null)
            {
                problems.Add("The profile has no locomotion set.");
                return;
            }

            if (set.Idles.Length == 0) problems.Add($"Locomotion set '{set.name}' has no idles.");
            foreach (AnimationClip clip in set.Idles) Humanoid(clip, $"an idle of '{set.name}'", problems);
            foreach (LocomotionSet.Cell cell in set.Move) Humanoid(cell.clip, $"a move cell of '{set.name}'", problems);
            foreach (LocomotionSet.Cell cell in set.Crouch) Humanoid(cell.clip, $"a crouch cell of '{set.name}'", problems);
            Humanoid(set.JumpUp, $"'{set.name}' jump", problems);
            Humanoid(set.Fall, $"'{set.name}' fall", problems);
            Humanoid(set.Land, $"'{set.name}' landing", problems);
            Humanoid(set.Sit, $"'{set.name}' sit", problems);
            Humanoid(profile.Glide, "the glide", problems);

            foreach (HumanoidAnimationProfile.PitchBlend blend in new[] { profile.RaiseOneArm, profile.RaiseBothArms })
            {
                Humanoid(blend.down, "a raise (down)", problems);
                Humanoid(blend.level, "a raise (level)", problems);
                Humanoid(blend.up, "a raise (up)", problems);
            }

            var styles = new HashSet<ItemGrip.HoldStyle>();
            foreach (HumanoidAnimationProfile.HoldPose pose in profile.HoldPoses)
            {
                if (pose.style == ItemGrip.HoldStyle.None) problems.Add("A hold pose is set for style None, which means empty hands.");
                if (!styles.Add(pose.style)) problems.Add($"Hold style {pose.style} has two poses.");
                Humanoid(pose.clip, $"the {pose.style} hold pose", problems);
            }
        }

        private static void CheckAction(CharacterAction action, List<string> problems)
        {
            if (action.VariantCount == 0) problems.Add($"Action '{action.name}' has no variants.");
            if (action.Cues.Any(c => c == null)) problems.Add($"Action '{action.name}' has an empty cue slot.");

            for (int i = 0; i < action.VariantCount; i++)
            {
                CharacterAction.Variant v = action.GetVariant(i);
                string where = $"action '{action.name}' variant {i}";

                Humanoid(v.clip, where, problems);
                if ((v.aimDown != null) != (v.aimUp != null))
                    problems.Add($"{where} has only one of Aim Down / Aim Up; an aimed variant needs both.");
                if (v.IsAimed)
                {
                    Humanoid(v.aimDown, where + " (aim down)", problems);
                    Humanoid(v.aimUp, where + " (aim up)", problems);
                }

                if (action.Mode == CharacterAction.Playback.EnterLoopExit)
                {
                    Humanoid(v.enter, where + " (enter)", problems);
                    Humanoid(v.exit, where + " (exit)", problems);
                }

                // A pose — a clip of two frames or fewer — is held by a loop state as it is; anything
                // longer that does not loop plays once and then freezes on its last frame.
                bool pose = v.clip != null && v.clip.length * v.clip.frameRate <= 2f;
                if (action.Loops && v.clip != null && !v.clip.isLooping && !pose)
                    problems.Add($"{where} loops but its clip '{v.clip.name}' is not imported with Loop Time.");

                if (HumanoidLayers.IsAdditive(action.BodySlot) && v.clip != null && !EndsWhereItStarts(v.clip))
                    problems.Add($"{where} is additive but its clip '{v.clip.name}' does not end where it starts; " +
                                 "the body would jump by the difference when the layer lets go.");
            }
        }

        /// <summary>
        /// Muscle units, where 1 is a joint's full range: a thousandth is a tenth of a degree or
        /// less on every humanoid joint, far below anything visible.
        /// </summary>
        private const float AdditiveRestTolerance = 1e-3f;

        /// <summary>
        /// Whether every curve of <paramref name="clip"/> is back at its first value on its last
        /// frame. An additive clip adds its offset from the first frame, so a clip that ends
        /// elsewhere holds that offset until its layer drops to weight 0 — and then snaps it off.
        /// </summary>
        private static bool EndsWhereItStarts(AnimationClip clip)
        {
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (Mathf.Abs(curve.Evaluate(clip.length) - curve.Evaluate(0f)) > AdditiveRestTolerance) return false;
            }
            return true;
        }

        private static void Humanoid(AnimationClip clip, string what, List<string> problems)
        {
            if (clip == null) problems.Add($"{what} has no clip.");
            else if (!clip.humanMotion) problems.Add($"{what} uses '{clip.name}', which is not a Humanoid clip.");
        }

        private static HashSet<AnimationClip> UsedClips(HumanoidAnimationProfile profile, IReadOnlyList<CharacterAction> actions)
        {
            var used = new HashSet<AnimationClip>();
            foreach (LocomotionSet set in profile.LocomotionVariants.Append(profile.Locomotion).Where(s => s != null))
            {
                used.UnionWith(set.Idles);
                used.UnionWith(set.Move.Select(c => c.clip));
                used.UnionWith(set.Crouch.Select(c => c.clip));
                used.UnionWith(new[] { set.JumpUp, set.Fall, set.Land, set.Sit });
            }

            used.UnionWith(profile.HoldPoses.Select(p => p.clip));
            foreach (HumanoidAnimationProfile.PitchBlend b in new[] { profile.RaiseOneArm, profile.RaiseBothArms })
                used.UnionWith(new[] { b.down, b.level, b.up });
            used.Add(profile.Glide);

            foreach (CharacterAction action in actions)
            {
                for (int i = 0; i < action.VariantCount; i++)
                {
                    CharacterAction.Variant v = action.GetVariant(i);
                    used.UnionWith(new[] { v.clip, v.aimDown, v.aimUp, v.enter, v.exit });
                }
            }

            used.Remove(null);
            return used;
        }
    }
}
