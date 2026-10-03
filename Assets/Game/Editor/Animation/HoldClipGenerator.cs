using System.Linq;
using SpaceGame.Items;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Writes the hold poses that carried tools need and the gun clips cannot give, and points the
    /// <see cref="HumanoidAnimationProfile"/> at them, then rebuilds the humanoid controller.
    ///
    /// <para>
    /// <b>Why these exist.</b> Every older hold pose is a gun clip that keys BOTH arms, so a holder
    /// carrying a bucket in one hand also raised the other as if steadying a pistol. The poses here
    /// pose one arm and leave the other hanging at rest:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="ItemGrip.HoldStyle.Carry"/> — the library's idle, arms down.</item>
    /// <item><see cref="ItemGrip.HoldStyle.Ready"/> — the sword idle's right arm, with the left arm
    /// taken from the idle. The only clip this writes.</item>
    /// <item><see cref="ItemGrip.HoldStyle.Push"/> — the library's push loop, both hands on the handle.</item>
    /// </list>
    /// <para>
    /// The off arm is written out as a constant, not left unkeyed: what a layer does with a muscle it
    /// has no curve for is Unity's decision and differs between sampling and playing.
    /// </para>
    /// <para>
    /// <b>Carry and Ready pose the arms only</b> (<c>HoldPose.armsOnly</c>): they play on the Hold Arms
    /// layer, whose mask has no body part, so the idle's and the sword idle's spine, chest and upper
    /// chest curves are never applied. They used to be, on the Upper Body layer, and a walking holder
    /// stooped to 72 degrees (Carry) and 46 (Ready) from the hips to the head, against 88 empty-handed.
    /// Zeroing those curves in the clips was tried first and rejected: a constant spine is right for
    /// one gait and wrong for the other (standing came out 14 degrees more forward than before).
    /// </para>
    /// </summary>
    public static class HoldClipGenerator
    {
        private const string LibraryPath = "Assets/ThirdParty/Quaternius/UniversalAnimationLibrary/UAL1_Standard.fbx";
        public const string ClipFolder = HumanoidControllerBuilder.Folder + "Hold/";
        public const string ReadyPath = ClipFolder + "Hold_Ready.anim";

        /// <summary>Words that mark a humanoid muscle as part of an arm or hand.</summary>
        private static readonly string[] ArmWords = { "Shoulder", "Arm", "Hand", "Thumb", "Index", "Middle", "Ring", "Little" };

        [MenuItem("Tools/SpaceGame/Animation/Generate Hold Clips")]
        private static void GenerateMenu() => Generate();

        public static void Generate()
        {
            AnimationClip idle = LibraryClip("Idle_Loop");
            AnimationClip sword = LibraryClip("Sword_Idle");
            AnimationClip push = LibraryClip("Push_Loop");

            HumanoidControllerBuilder.EnsureFolder(ClipFolder);
            AnimationClip ready = RecoilClipGenerator.SaveClip(KeepRightArm(sword, idle, "Hold_Ready"), ReadyPath);

            // Push leans into the cart on purpose, so it keeps the torso; the other two are held while
            // walking and must not touch it (see HumanoidAnimationProfile.HoldPose.armsOnly).
            Assign(ItemGrip.HoldStyle.Push, push, armsOnly: false);
            Assign(ItemGrip.HoldStyle.Carry, idle, armsOnly: true);
            Assign(ItemGrip.HoldStyle.Ready, ready, armsOnly: true);

            AssetDatabase.SaveAssets();
            Debug.Log($"[HoldClipGenerator] Wrote {ReadyPath} and posed Push, Carry and Ready in the humanoid profile.");
            HumanoidControllerBuilder.Rebuild();
        }

        /// <summary><paramref name="armed"/> as it is, except the left arm holds <paramref name="free"/>'s first frame.</summary>
        private static AnimationClip KeepRightArm(AnimationClip armed, AnimationClip free, string name)
        {
            var clip = new AnimationClip { name = name, frameRate = armed.frameRate };
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(armed))
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(armed, binding);
                if (IsLeftArm(binding))
                {
                    AnimationCurve rest = AnimationUtility.GetEditorCurve(free, binding);
                    curve = new AnimationCurve(new Keyframe(0f, rest != null ? rest.Evaluate(0f) : 0f));
                }

                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(armed);
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        private static bool IsLeftArm(EditorCurveBinding binding) =>
            binding.type == typeof(Animator)
            && binding.propertyName.StartsWith("Left")
            && ArmWords.Any(binding.propertyName.Contains);

        private static void Assign(ItemGrip.HoldStyle style, AnimationClip clip, bool armsOnly)
        {
            var profile = AssetDatabase.LoadAssetAtPath<HumanoidAnimationProfile>(HumanoidControllerBuilder.ProfilePath);
            var so = new SerializedObject(profile);
            SerializedProperty poses = so.FindProperty("holdPoses");

            SerializedProperty entry = null;
            for (int i = 0; i < poses.arraySize && entry == null; i++)
                if (poses.GetArrayElementAtIndex(i).FindPropertyRelative("style").intValue == (int)style)
                    entry = poses.GetArrayElementAtIndex(i);

            if (entry == null)
            {
                poses.arraySize++;
                entry = poses.GetArrayElementAtIndex(poses.arraySize - 1);
                entry.FindPropertyRelative("style").intValue = (int)style;
            }

            entry.FindPropertyRelative("clip").objectReferenceValue = clip;
            entry.FindPropertyRelative("armsOnly").boolValue = armsOnly;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(profile);
        }

        private static AnimationClip LibraryClip(string name)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(LibraryPath)
                .OfType<AnimationClip>().FirstOrDefault(c => c.name == name);
            if (clip == null) throw new System.InvalidOperationException($"No clip '{name}' in {LibraryPath}.");
            return clip;
        }
    }
}
