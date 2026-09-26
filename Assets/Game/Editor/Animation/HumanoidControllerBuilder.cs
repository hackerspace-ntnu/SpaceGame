using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceGame.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    /// <summary>
    /// Generates the humanoid Animator controller — the one the player and every humanoid NPC
    /// wear — from <see cref="HumanoidAnimationProfile"/> and the <see cref="CharacterAction"/>
    /// assets. The controller is output, never edited by hand: the next rebuild writes over it.
    ///
    /// <para>
    /// <b>How it writes without losing the controller.</b> Thirty-odd prefabs and scene objects
    /// reference it by GUID, so it may never be deleted and recreated. A fresh controller is built
    /// at a scratch path, its <see cref="AnimatorDescription"/> compared with the live one's, and
    /// only if they differ is the fresh FILE copied over the live one — the live <c>.meta</c>, and
    /// with it the GUID, stays put. An unchanged rebuild therefore touches nothing, and a changed
    /// one leaves no orphaned sub-objects behind (the in-place alternative, emptying the live
    /// controller through the API, keeps whatever it did not know to remove — dead
    /// StateMachineBehaviours included).
    /// </para>
    /// <para>
    /// Menu: <c>Tools/SpaceGame/Animation/</c> Rebuild (writes) and Audit (reads only). Every
    /// rebuild ends in <see cref="Verify"/>, which re-reads everything from disk.
    /// </para>
    /// </summary>
    public static class HumanoidControllerBuilder
    {
        public const string Folder = "Assets/Game/Art/Animations/Humanoid/";
        public const string ControllerPath = Folder + "Humanoid.controller";
        public const string ProfilePath = "Assets/Game/ScriptableObjects/Animation/HumanoidAnimationProfile.asset";
        public const string ActionsFolder = "Assets/Game/ScriptableObjects/Animation/Actions";
        public const string CatalogPath = "Assets/Game/Resources/" + CharacterActionCatalog.ResourcePath + ".asset";
        public const string CuesFolder = "Assets/Game/ScriptableObjects/Animation/Cues";
        public const string ReactionsPath = "Assets/Game/Resources/" + MomentReactions.ResourcePath + ".asset";

        private const string ScratchFolder = Folder + "_Build";
        private const string ScratchPath = ScratchFolder + "/Humanoid.controller";

        [MenuItem("Tools/SpaceGame/Animation/Rebuild Humanoid Controller")]
        private static void RebuildMenu() => Rebuild();

        [MenuItem("Tools/SpaceGame/Animation/Audit Humanoid Controller")]
        private static void AuditMenu() => Debug.Log(Audit());

        /// <summary>Rebuild everything the profile and actions describe. True if the controller changed.</summary>
        public static bool Rebuild()
        {
            HumanoidAnimationProfile profile = LoadProfile();
            List<CharacterAction> actions = CollectActions();
            HumanoidContentCheck.ThrowIfInvalid(profile, actions);

            WriteCatalog(actions, profile);
            AnimatorController fresh = BuildScratch(profile, actions, HumanoidMasks.Write());

            var live = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            bool changed = live == null || AnimatorDescription.Describe(live) != AnimatorDescription.Describe(fresh);
            if (changed) ReplaceLiveWithScratch();
            AssetDatabase.DeleteAsset(ScratchFolder);

            live = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            LocomotionOverrides.Build(live, profile, actions);
            if (changed) NetworkAnimatorRebake.Rebake(NetworkAnimatorRebake.PlayerPrefab);
            AssetDatabase.SaveAssets();

            Verify(actions);
            Debug.Log(changed
                ? $"[HumanoidControllerBuilder] Rebuilt {ControllerPath} with {actions.Count} actions."
                : $"[HumanoidControllerBuilder] {ControllerPath} was already up to date.");
            return changed;
        }

        /// <summary>
        /// What a rebuild would change in the live controller, the content problems a rebuild
        /// would refuse, and the library clips no action or pose uses yet. Writes nothing but a
        /// scratch controller it deletes again.
        /// </summary>
        public static string Audit()
        {
            HumanoidAnimationProfile profile = LoadProfile();
            List<CharacterAction> actions = CollectActions();
            var report = new System.Text.StringBuilder("[HumanoidControllerBuilder] Audit\n");

            List<string> problems = HumanoidContentCheck.Problems(profile, actions);
            foreach (string problem in problems) report.AppendLine("  PROBLEM " + problem);

            HumanoidMasks.Set masks = HumanoidMasks.Load();
            if (masks.Upper == null || masks.Left == null || masks.Right == null)
                report.AppendLine("  PROBLEM the masks under " + HumanoidMasks.Folder + " have not been written yet; run Rebuild.");
            else if (problems.Count == 0)
            {
                AnimatorController fresh = BuildScratch(profile, actions, masks);
                var live = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                string[] after = AnimatorDescription.Describe(fresh).Split('\n');
                string[] before = live != null ? AnimatorDescription.Describe(live).Split('\n') : new string[0];
                AssetDatabase.DeleteAsset(ScratchFolder);

                foreach (string line in before.Except(after)) report.AppendLine("  - " + line.Trim());
                foreach (string line in after.Except(before)) report.AppendLine("  + " + line.Trim());
                if (before.SequenceEqual(after)) report.AppendLine("  controller is up to date");
            }

            foreach (string unused in HumanoidContentCheck.UnusedLibraryClips(profile, actions))
                report.AppendLine("  unused clip " + unused);
            foreach (string silent in HumanoidContentCheck.UnansweredCues(CollectCues(), actions))
                report.AppendLine("  unanswered " + silent);
            return report.ToString();
        }

        private static HumanoidAnimationProfile LoadProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<HumanoidAnimationProfile>(ProfilePath);
            if (profile == null)
                throw new FileNotFoundException($"No HumanoidAnimationProfile at {ProfilePath}.");
            return profile;
        }

        /// <summary>Every action asset, sorted by path — the order the catalog and wire ids follow.</summary>
        public static List<CharacterAction> CollectActions() =>
            AssetDatabase.FindAssets("t:" + nameof(CharacterAction), new[] { ActionsFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<CharacterAction>)
                .Where(action => action != null)
                .ToList();

        /// <summary>Every cue asset — the whole vocabulary, including words no action answers yet.</summary>
        public static List<CharacterCue> CollectCues() =>
            AssetDatabase.FindAssets("t:" + nameof(CharacterCue), new[] { CuesFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<CharacterCue>)
                .Where(cue => cue != null)
                .ToList();

        private static void WriteCatalog(List<CharacterAction> actions, HumanoidAnimationProfile profile)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<CharacterActionCatalog>(CatalogPath);
            if (catalog == null)
            {
                EnsureFolder(Path.GetDirectoryName(CatalogPath));
                catalog = ScriptableObject.CreateInstance<CharacterActionCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var serialized = new SerializedObject(catalog);
            SerializedProperty list = serialized.FindProperty("actions");
            list.arraySize = actions.Count;
            for (int i = 0; i < actions.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = actions[i];
            serialized.FindProperty("idleVariantCount").intValue = Mathf.Max(1, profile.Locomotion.Idles.Length);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static AnimatorController BuildScratch(HumanoidAnimationProfile profile, List<CharacterAction> actions,
                                                      HumanoidMasks.Set masks)
        {
            AssetDatabase.DeleteAsset(ScratchFolder);
            EnsureFolder(ScratchFolder);

            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ScratchPath);
            foreach ((string name, AnimatorControllerParameterType type, float value) in HumanoidParams.Contract)
            {
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = name, type = type,
                    defaultFloat = value, defaultInt = (int)value, defaultBool = value != 0f
                });
            }

            HumanoidBaseLayer.Build(controller, profile);
            HumanoidPoseLayers.BuildUpperBody(controller, profile, masks.Upper);
            HumanoidPoseLayers.BuildWornLeft(controller, profile, masks.Left);
            HumanoidActionLayers.Build(controller, profile, actions, CharacterAction.Slot.Full, null);
            HumanoidActionLayers.Build(controller, profile, actions, CharacterAction.Slot.Upper, masks.Upper);
            HumanoidActionLayers.Build(controller, profile, actions, CharacterAction.Slot.LeftArm, masks.Left);
            HumanoidActionLayers.Build(controller, profile, actions, CharacterAction.Slot.RightArm, masks.Right);
            // The upper-body mask, not an arm's: a rifle's kick travels through the chest and both
            // shoulders, and a pistol's still rocks the shoulder. The head stays off, as on every
            // upper layer, so PlayerHeadLook keeps the eyes on the target through the kick.
            HumanoidActionLayers.Build(controller, profile, actions, CharacterAction.Slot.Additive, masks.Upper);
            HumanoidPoseLayers.BuildGlide(controller, profile);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        /// <summary>Create <paramref name="path"/> and any missing parents through the AssetDatabase.</summary>
        public static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>Copy the scratch controller's file over the live one, keeping the live .meta (the GUID).</summary>
        private static void ReplaceLiveWithScratch()
        {
            File.Copy(Path.GetFullPath(ScratchPath), Path.GetFullPath(ControllerPath), overwrite: true);
            AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>Re-read the controller and the player prefab from disk and refuse anything off-contract.</summary>
        private static void Verify(List<CharacterAction> actions)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) throw new FileNotFoundException($"{ControllerPath} did not survive the rebuild.");

            string[] layers = controller.layers.Select(l => l.name).ToArray();
            if (!layers.SequenceEqual(HumanoidLayers.Order))
                throw new System.InvalidOperationException(
                    $"{ControllerPath} has layers [{string.Join(", ", layers)}], expected [{string.Join(", ", HumanoidLayers.Order)}].");

            foreach (CharacterAction action in actions)
            {
                AnimatorStateMachine sm = controller.layers[System.Array.IndexOf(layers, HumanoidLayers.ForSlot(action.BodySlot))].stateMachine;
                for (int v = 0; v < action.VariantCount; v++)
                {
                    string state = HumanoidLayers.StateName(action, v, HumanoidLayers.Stage.Main);
                    if (sm.states.All(s => s.state.name != state))
                        throw new System.InvalidOperationException($"{ControllerPath} has no state '{state}' for action '{action.name}'.");
                }
            }

            string[] baked = NetworkAnimatorRebake.BakedParameters(NetworkAnimatorRebake.PlayerPrefab);
            string[] expected = controller.parameters.Select(p => p.name).ToArray();
            if (baked.Length > 0 && !new HashSet<string>(baked).SetEquals(expected))
                throw new System.InvalidOperationException(
                    $"{NetworkAnimatorRebake.PlayerPrefab}'s NetworkAnimator synchronises " +
                    $"[{string.Join(", ", baked)}] but the controller has [{string.Join(", ", expected)}].");
        }
    }
}
