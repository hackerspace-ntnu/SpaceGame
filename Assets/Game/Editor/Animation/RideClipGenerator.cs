using System.Collections.Generic;
using System.IO;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Writes the riding loop from <see cref="RidePoseProfile"/> — the seated source clip with its
    /// legs opened round an animal's barrel — and the full-body <see cref="CharacterAction"/> that
    /// plays it, then rebuilds the humanoid controller. Data in, assets out, like the recoils: the
    /// clip and action are never edited by hand.
    ///
    /// <para>
    /// The action has no cue on purpose: nothing but a saddle should ever pick it, so no gesture
    /// pool or moment can draw it. <c>MountedRiderPose</c> plays it by name.
    /// </para>
    /// </summary>
    public static class RideClipGenerator
    {
        public const string ProfilePath = "Assets/Game/ScriptableObjects/Animation/RidePoseProfile.asset";
        public const string ClipFolder = HumanoidControllerBuilder.Folder + "Ride/";
        public const string ActionFolder = HumanoidControllerBuilder.ActionsFolder + "/Riding/";

        private const string LeftPrefix = "Left ";
        private const string RightPrefix = "Right ";

        [MenuItem("Tools/SpaceGame/Animation/Generate Ride Clip")]
        private static void GenerateMenu() => Generate();

        /// <summary>Write the ride clip and its action, then rebuild the controller.</summary>
        public static void Generate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<RidePoseProfile>(ProfilePath);
            if (profile == null) throw new FileNotFoundException($"No RidePoseProfile at {ProfilePath}.");

            HumanoidControllerBuilder.EnsureFolder(ClipFolder);
            HumanoidControllerBuilder.EnsureFolder(ActionFolder.TrimEnd('/'));

            AnimationClip clip = RecoilClipGenerator.SaveClip(Build(profile), ClipFolder + profile.ActionName + ".anim");
            SaveAction(profile, clip);

            AssetDatabase.SaveAssets();
            Debug.Log($"[RideClipGenerator] Wrote {profile.ActionName} from {profile.Source.name} ({ProfilePath}).");
            HumanoidControllerBuilder.Rebuild();
        }

        /// <summary>The ride clip <paramref name="profile"/> describes, in memory. Throws on a muscle Unity does not know.</summary>
        public static AnimationClip Build(RidePoseProfile profile)
        {
            AnimationClip source = profile.Source;
            if (source == null || !source.isHumanMotion)
                throw new System.InvalidOperationException($"{ProfilePath} needs a humanoid source clip.");
            if (!AnimationUtility.GetAnimationClipSettings(source).loopTime)
                throw new System.InvalidOperationException($"{source.name} does not loop; the ride is held for as long as the seat is.");

            Dictionary<string, float> offsets = Offsets(profile);
            var clip = new AnimationClip { name = profile.ActionName, frameRate = source.frameRate };
            var applied = new HashSet<string>();

            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
                if (binding.type == typeof(Animator) && offsets.TryGetValue(binding.propertyName, out float offset))
                {
                    Keyframe[] keys = curve.keys;
                    for (int i = 0; i < keys.Length; i++) keys[i].value += offset;
                    curve.keys = keys;
                    applied.Add(binding.propertyName);
                }
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            // A muscle the source never keys sits at 0 throughout; it still has to move.
            foreach (KeyValuePair<string, float> pair in offsets)
                if (!applied.Contains(pair.Key))
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key),
                                                    AnimationCurve.Constant(0f, source.length, pair.Value));

            AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(source));
            return clip;
        }

        private static Dictionary<string, float> Offsets(RidePoseProfile profile)
        {
            var offsets = new Dictionary<string, float>();
            foreach (RidePoseProfile.MuscleOffset entry in profile.Offsets)
            {
                Add(offsets, entry.muscle, entry.offset);
                if (profile.MirrorLeftToRight && entry.muscle.StartsWith(LeftPrefix))
                    Add(offsets, RightPrefix + entry.muscle.Substring(LeftPrefix.Length), entry.offset);
            }
            return offsets;
        }

        private static void Add(Dictionary<string, float> offsets, string muscle, float offset)
        {
            if (System.Array.IndexOf(HumanTrait.MuscleName, muscle) < 0)
                throw new System.InvalidOperationException(
                    $"{ProfilePath} names muscle '{muscle}', which is not a humanoid muscle (see HumanTrait.MuscleName).");
            offsets[muscle] = offset;
        }

        /// <summary>Create or overwrite the full-body loop that plays <paramref name="clip"/>.</summary>
        private static void SaveAction(RidePoseProfile profile, AnimationClip clip)
        {
            string path = ActionFolder + profile.ActionName + ".asset";
            var action = AssetDatabase.LoadAssetAtPath<CharacterAction>(path);
            if (action == null)
            {
                action = ScriptableObject.CreateInstance<CharacterAction>();
                AssetDatabase.CreateAsset(action, path);
            }

            var so = new SerializedObject(action);
            so.FindProperty("slot").enumValueIndex = (int)CharacterAction.Slot.Full;
            so.FindProperty("playback").enumValueIndex = (int)CharacterAction.Playback.Loop;
            SerializedProperty variants = so.FindProperty("variants");
            variants.arraySize = 1;
            variants.GetArrayElementAtIndex(0).boxedValue = new CharacterAction.Variant { clip = clip };
            so.FindProperty("speedRange").vector2Value = Vector2.one;
            so.FindProperty("fadeIn").floatValue = profile.FadeIn;
            so.FindProperty("fadeOut").floatValue = profile.FadeOut;
            so.FindProperty("marks").arraySize = 0;
            so.FindProperty("cues").arraySize = 0;
            so.FindProperty("postures").intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
