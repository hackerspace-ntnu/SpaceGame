using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Turns clips into <see cref="CharacterAction"/> assets, one each — the fast way to make a
    /// whole new animation library usable: select the clips, a model full of takes, or a folder,
    /// and run Tools/SpaceGame/Animation/Create Actions From Selected Clips, then Rebuild.
    ///
    /// <para>
    /// Each action starts as the simplest thing that is always correct: the whole body, one
    /// variant, looping if the clip loops. Narrowing it to the upper body or an arm, grouping
    /// several clips into one action's variants, and marking its contact frame are edits made on
    /// the asset afterwards. An action that already exists is never overwritten — it may carry
    /// exactly that tuning.
    /// </para>
    /// </summary>
    public static class CharacterActionAuthoring
    {
        [MenuItem("Tools/SpaceGame/Animation/Create Actions From Selected Clips")]
        private static void FromSelection()
        {
            List<CharacterAction> created = CreateFor(SelectedClips());
            Debug.Log($"[CharacterActionAuthoring] Created {created.Count} action(s) under " +
                      $"{HumanoidControllerBuilder.ActionsFolder}. Run Rebuild Humanoid Controller to play them.");
        }

        /// <summary>
        /// One action per clip, in a folder named after the clip's own folder, so actions made from
        /// one library stay together. Returns the actions created. A clip some action already
        /// plays is skipped: running this over a whole library fills in only what nothing uses yet,
        /// and never duplicates an action somebody has already shaped.
        /// </summary>
        public static List<CharacterAction> CreateFor(IEnumerable<AnimationClip> clips)
        {
            var played = new HashSet<AnimationClip>();
            foreach (CharacterAction existing in HumanoidControllerBuilder.CollectActions())
                for (int i = 0; i < existing.VariantCount; i++)
                    played.UnionWith(Clips(existing.GetVariant(i)));

            var created = new List<CharacterAction>();
            foreach (AnimationClip clip in clips.Distinct())
            {
                if (played.Contains(clip)) continue;

                string library = Path.GetFileName(Path.GetDirectoryName(AssetDatabase.GetAssetPath(clip)));
                string folder = $"{HumanoidControllerBuilder.ActionsFolder}/{library}";
                string path = $"{folder}/{clip.name}.asset";
                if (AssetDatabase.LoadAssetAtPath<CharacterAction>(path) != null) continue;

                HumanoidControllerBuilder.EnsureFolder(folder);
                var action = ScriptableObject.CreateInstance<CharacterAction>();
                AssetDatabase.CreateAsset(action, path);

                var so = new SerializedObject(action);
                so.FindProperty("slot").enumValueIndex = (int)CharacterAction.Slot.Full;
                so.FindProperty("playback").enumValueIndex =
                    (int)(clip.isLooping ? CharacterAction.Playback.Loop : CharacterAction.Playback.OneShot);
                SerializedProperty variants = so.FindProperty("variants");
                variants.arraySize = 1;
                SerializedProperty variant = variants.GetArrayElementAtIndex(0);
                variant.FindPropertyRelative("clip").objectReferenceValue = clip;
                variant.FindPropertyRelative("selectionWeight").floatValue = 1f;
                so.ApplyModifiedPropertiesWithoutUndo();

                created.Add(action);
            }

            AssetDatabase.SaveAssets();
            return created;
        }

        private static IEnumerable<AnimationClip> Clips(CharacterAction.Variant v) =>
            new[] { v.clip, v.aimDown, v.aimUp, v.enter, v.exit }.Where(c => c != null);

        /// <summary>Every real clip under <paramref name="folder"/>, for scripted use.</summary>
        public static List<CharacterAction> CreateForFolder(string folder) =>
            CreateFor(ClipsUnder(new[] { folder }));

        private static IEnumerable<AnimationClip> SelectedClips()
        {
            foreach (Object selected in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(selected);
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (AnimationClip clip in ClipsUnder(new[] { path })) yield return clip;
                }
                else if (selected is AnimationClip clip)
                {
                    yield return clip;
                }
                else
                {
                    foreach (AnimationClip inModel in ClipsIn(path)) yield return inModel;
                }
            }
        }

        private static IEnumerable<AnimationClip> ClipsUnder(string[] folders) =>
            AssetDatabase.FindAssets("t:AnimationClip", folders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .SelectMany(ClipsIn);

        /// <summary>
        /// The real clips inside an asset. A model also holds <c>__preview__</c> copies of its takes,
        /// which animate nothing.
        /// </summary>
        private static IEnumerable<AnimationClip> ClipsIn(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"));
    }
}
