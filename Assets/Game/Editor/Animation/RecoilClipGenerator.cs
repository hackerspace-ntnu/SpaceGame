using System.IO;
using System.Linq;
using SpaceGame.Items;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Writes the recoil clips and their <see cref="CharacterAction"/>s from
    /// <see cref="RecoilProfile"/>, then rebuilds the humanoid controller so they can play. Data in,
    /// assets out, like the controller: the clips and actions are never edited by hand, the next
    /// run writes over them.
    ///
    /// <para>
    /// <b>Why additive muscle clips.</b> A gun's shot used to be a level-only clip on an
    /// overriding layer, which snapped an aimed arm level on every shot, so guns had no use action
    /// at all. A clip on the additive layer carries only a small MOTION — the wrist flicks up, the
    /// elbow gives, the shoulder and chest rock back — which Unity adds, muscle by muscle, to
    /// whatever pose the layers beneath settled on. One clip then serves every hold pose, every aim
    /// pitch, the player and every humanoid NPC.
    /// </para>
    /// <para>
    /// <b>The reference frame.</b> An additive layer adds a clip's values minus its reference
    /// pose. Unity's default reference is the clip's first frame; the generator also sets it
    /// explicitly (<c>hasAdditiveReferencePose</c> at time 0, no separate clip), so the answer does
    /// not rest on a default. Frame 0 holds every animated muscle at 0 and unanimated muscles are 0
    /// throughout, so the added delta is exactly the curve — and the curve ends at 0, so the body
    /// is back where it was before the layer lets go (HumanoidContentCheck refuses one that is not).
    /// </para>
    /// <para>
    /// The shape follows the classical split of a hit (GDC-L1-ANIM-0001): no anticipation — a shot
    /// is the player's input and may not wait on a wind-up (GDC-L1-ANIM-0002) — a fast attack that
    /// eases into the top, and a longer smooth settle.
    /// </para>
    /// </summary>
    public static class RecoilClipGenerator
    {
        public const string ProfilePath = "Assets/Game/ScriptableObjects/Animation/RecoilProfile.asset";
        public const string ClipFolder = HumanoidControllerBuilder.Folder + "Recoil/";
        public const string ActionFolder = HumanoidControllerBuilder.ActionsFolder + "/Combat/";
        public const string CuePath = HumanoidControllerBuilder.CuesFolder + "/shoot.asset";

        /// <summary>
        /// The kick's start slope as a multiple of peak / kickSeconds. A Hermite span from 0 to the
        /// peak with slopes 2·peak/t and 0 is exactly the quadratic ease-out: full speed on the shot,
        /// decelerating to rest at the top of the kick.
        /// </summary>
        private const float EaseOutStartSlope = 2f;

        [MenuItem("Tools/SpaceGame/Animation/Generate Recoil Clips")]
        private static void GenerateMenu() => Generate();

        /// <summary>Write every recoil's clip and action, then rebuild the controller.</summary>
        public static void Generate()
        {
            RecoilProfile profile = LoadOrThrow<RecoilProfile>(ProfilePath);
            CharacterCue shoot = LoadOrThrow<CharacterCue>(CuePath);

            HumanoidControllerBuilder.EnsureFolder(ClipFolder);
            foreach (RecoilProfile.Recoil recoil in profile.Recoils)
            {
                AnimationClip clip = SaveClip(Build(recoil, profile.FrameRate), ClipPath(recoil));
                SaveAction(recoil, clip, shoot);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[RecoilClipGenerator] Wrote {profile.Recoils.Count} recoil clips and actions from {ProfilePath}.");
            HumanoidControllerBuilder.Rebuild();
        }

        /// <summary>The clip <paramref name="recoil"/> describes, in memory. Throws on a muscle Unity does not know.</summary>
        public static AnimationClip Build(RecoilProfile.Recoil recoil, float frameRate)
        {
            if (string.IsNullOrWhiteSpace(recoil.actionName))
                throw new System.InvalidOperationException($"A recoil in {ProfilePath} has no action name.");
            if (recoil.settleSeconds <= recoil.kickSeconds)
                throw new System.InvalidOperationException(
                    $"Recoil '{recoil.actionName}' settles ({recoil.settleSeconds}s) before its kick peaks ({recoil.kickSeconds}s).");

            var clip = new AnimationClip { name = recoil.actionName, frameRate = frameRate };
            foreach (RecoilProfile.MuscleKick kick in recoil.muscles)
            {
                if (!HumanTrait.MuscleName.Contains(kick.muscle))
                    throw new System.InvalidOperationException(
                        $"Recoil '{recoil.actionName}' names muscle '{kick.muscle}', which is not a humanoid muscle " +
                        "(see HumanTrait.MuscleName).");

                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kick.muscle),
                                                Curve(kick.peak, recoil.kickSeconds, recoil.settleSeconds));
            }

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            settings.hasAdditiveReferencePose = true;
            settings.additiveReferencePoseClip = null;
            settings.additiveReferencePoseTime = 0f;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        /// <summary>The action asset <paramref name="recoil"/> writes.</summary>
        public static string ActionPath(RecoilProfile.Recoil recoil) => ActionFolder + recoil.actionName + ".asset";

        /// <summary>The clip asset <paramref name="recoil"/> writes.</summary>
        public static string ClipPath(RecoilProfile.Recoil recoil) => ClipFolder + recoil.actionName + ".anim";

        /// <summary>
        /// Rest at 0, an eased climb to <paramref name="peak"/>, a smooth settle back to 0. Flat
        /// tangents at the top and at the end, so neither turn has a corner.
        /// </summary>
        private static AnimationCurve Curve(float peak, float kickSeconds, float settleSeconds) => new AnimationCurve(
            new Keyframe(0f, 0f, 0f, EaseOutStartSlope * peak / kickSeconds),
            new Keyframe(kickSeconds, peak, 0f, 0f),
            new Keyframe(settleSeconds, 0f, 0f, 0f));

        /// <summary>
        /// Write <paramref name="clip"/> over the asset at <paramref name="path"/> — in place, so its
        /// GUID and the action and controller that reference it survive a regeneration.
        /// </summary>
        private static AnimationClip SaveClip(AnimationClip clip, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(clip, path);
                return clip;
            }

            // CopySerialized copies the name too; set it back so the main object matches the file.
            string name = clip.name;
            EditorUtility.CopySerialized(clip, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            return existing;
        }

        /// <summary>
        /// Create or overwrite the action that plays <paramref name="clip"/>: additive, one-shot,
        /// right-handed and mirrored for a gun in the left hand, answering <c>shoot</c>.
        /// </summary>
        private static void SaveAction(RecoilProfile.Recoil recoil, AnimationClip clip, CharacterCue shoot)
        {
            string path = ActionPath(recoil);
            var action = AssetDatabase.LoadAssetAtPath<CharacterAction>(path);
            if (action == null)
            {
                HumanoidControllerBuilder.EnsureFolder(Path.GetDirectoryName(path));
                action = ScriptableObject.CreateInstance<CharacterAction>();
                AssetDatabase.CreateAsset(action, path);
            }

            var so = new SerializedObject(action);
            so.FindProperty("slot").enumValueIndex = (int)CharacterAction.Slot.Additive;
            so.FindProperty("playback").enumValueIndex = (int)CharacterAction.Playback.OneShot;

            // Through boxedValue rather than field by field, so a field Variant gains later starts at
            // its C# default instead of the zero a grown SerializedProperty array would give it.
            SerializedProperty variants = so.FindProperty("variants");
            variants.arraySize = 1;
            variants.GetArrayElementAtIndex(0).boxedValue = new CharacterAction.Variant { clip = clip };

            so.FindProperty("authoredHand").enumValueIndex = (int)ItemGrip.Hand.Right;
            so.FindProperty("mirrorForOtherArm").boolValue = true;
            so.FindProperty("speedRange").vector2Value = Vector2.one;
            so.FindProperty("fadeIn").floatValue = recoil.fadeIn;
            so.FindProperty("fadeOut").floatValue = recoil.fadeOut;
            so.FindProperty("marks").arraySize = 0;
            so.FindProperty("postures").intValue = 0;

            SerializedProperty cues = so.FindProperty("cues");
            cues.arraySize = 1;
            cues.GetArrayElementAtIndex(0).objectReferenceValue = shoot;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T LoadOrThrow<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException($"No {typeof(T).Name} at {path}.");
            return asset;
        }
    }
}
