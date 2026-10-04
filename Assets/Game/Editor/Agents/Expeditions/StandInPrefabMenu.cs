// Prepare Resident Prefabs: every character prefab a settlement may place as a resident who can join a band gets
// what it needs to stand in for its resident on an expedition: an ExpeditionMember (the stand-in's replicated
// identity) and a FormationModule that ships SWITCHED OFF, with no formation id and the Social priority.
// FormationModule registers itself when it is enabled, under the default id "caravan", so a live one would put every
// resident at home into one formation; a stand-in sets its band's id and switches it on before its network spawn
// (ExpeditionMember.Stamp). AddComponent runs no Reset, so the priority is written here (AgentSystem.md).
//
// Which prefabs: every prefab any culture's profiles name (SettlementCulture.profiles), plus every characters and
// specialCharacters prefab of every SettlementConfig whose culture has an expedition profile, so a prefab made for one
// role (no culture profile of its own) is covered as soon as a config lists it. The prefab named is the one prepared, a
// variant included: only that prefab places residents. A prefab already prepared is not touched, so the menu is idempotent. Each prefab is written on its own with
// LoadPrefabContents/SaveAsPrefabAsset, never AssetDatabase.SaveAssets (it would flush pending terrain edits); one
// open in Prefab Mode is skipped, and so is one with a live FormationModule (it may walk in a caravan on purpose).
// The file is snapshotted before and after to Temp/PrepareResidentPrefabs/ and read back: a component that did not
// persist, or any NetworkObject GlobalObjectIdHash that changed, is an error.
//
// After it changed anything, run in order: Tools/SpaceGame/Multiplayer/Sync Network Prefabs, Tools/Save System/Wire
// Saveable Prefabs, Tools/SpaceGame/Ragdoll/Wire Prefabs (AgentSystem.md: skipping one leaves a prefab broken).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class StandInPrefabMenu
    {
        private const string Tag = "[ExpeditionAuthoring]";
        private const string SnapshotFolder = "PrepareResidentPrefabs";
        // FormationModule's and BehaviourModuleBase's serialized fields, written through SerializedObject.
        private const string FormationIdField = "formationId";
        private const string PriorityField = "priority";
        private static readonly Regex HashPattern = new Regex(@"GlobalObjectIdHash: (\d+)");

        private enum Outcome { Unchanged, Changed, Skipped, Failed }

        [MenuItem("Tools/SpaceGame/Expeditions/Prepare Resident Prefabs")]
        public static void PrepareResidentPrefabs()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError($"{Tag} Exit Play mode first: prefab edits made in Play mode are lost.");
                return;
            }

            var report = new StringBuilder($"{Tag} Prepare Resident Prefabs\n");
            string snapshots = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", SnapshotFolder);
            Directory.CreateDirectory(snapshots);

            var tally = new Dictionary<Outcome, int>();
            foreach (string path in ResidentPrefabPaths(report))
            {
                Outcome outcome = Prepare(path, snapshots, report);
                tally[outcome] = tally.TryGetValue(outcome, out int count) ? count + 1 : 1;
            }

            int Count(Outcome outcome) => tally.TryGetValue(outcome, out int n) ? n : 0;
            report.Append($"  {Count(Outcome.Changed)} changed, {Count(Outcome.Unchanged)} already prepared, " +
                          $"{Count(Outcome.Skipped)} skipped, {Count(Outcome.Failed)} failed. Snapshots: {snapshots}\n");
            if (Count(Outcome.Changed) > 0)
                report.Append("  Next, in order: Tools/SpaceGame/Multiplayer/Sync Network Prefabs, Tools/Save System/Wire Saveable " +
                              "Prefabs, Tools/SpaceGame/Ragdoll/Wire Prefabs.\n");

            if (Count(Outcome.Failed) > 0) Debug.LogError(report.ToString());
            else Debug.Log(report.ToString());
        }

        /// <summary>
        /// Every prefab any culture's profiles name, then every character of a settlement config whose culture runs bands,
        /// once each, in that order; the report says how many came from each.
        /// </summary>
        private static List<string> ResidentPrefabPaths(StringBuilder report)
        {
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SettlementCulture)))
            {
                var culture = AssetDatabase.LoadAssetAtPath<SettlementCulture>(AssetDatabase.GUIDToAssetPath(guid));
                for (int i = 0; i < culture.profiles.Length; i++)
                    Add(culture.profiles[i].prefab, $"{culture.name} profiles[{i}]");
            }
            int fromProfiles = paths.Count;

            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SettlementConfig)))
            {
                var config = AssetDatabase.LoadAssetAtPath<SettlementConfig>(AssetDatabase.GUIDToAssetPath(guid));
                if (config.culture == null || config.culture.expeditions == null) continue;
                for (int i = 0; i < config.characters.Length; i++)
                    Add(config.characters[i]?.prefab, $"{config.name} characters[{i}]");
                for (int i = 0; i < config.specialCharacters.Length; i++)
                    Add(config.specialCharacters[i]?.prefab, $"{config.name} specialCharacters[{i}]");
            }

            report.Append($"  {paths.Count} prefab(s): {fromProfiles} named by culture profiles, {paths.Count - fromProfiles} more " +
                          "listed only by the characters of settlement configs whose culture runs bands\n");
            return paths;

            void Add(GameObject prefab, string source)
            {
                if (prefab == null)
                {
                    report.Append($"  ! {source} names no prefab, skipped\n");
                    return;
                }

                string path = AssetDatabase.GetAssetPath(prefab);
                if (!paths.Contains(path)) paths.Add(path);
            }
        }

        private static Outcome Prepare(string path, string snapshots, StringBuilder report)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            string before = File.ReadAllText(path);

            if (Prepared(asset))
            {
                report.Append($"  {file}: already prepared (GlobalObjectIdHash {Hashes(before)})\n");
                return Outcome.Unchanged;
            }
            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path)
            {
                report.Append($"  ! {file}: open in Prefab Mode, skipped. Close it and run the menu again.\n");
                return Outcome.Skipped;
            }
            if (asset.GetComponent<NetworkObject>() == null)
            {
                report.Append($"  ! {file}: no root NetworkObject, so it can carry no ExpeditionMember (a NetworkBehaviour).\n");
                return Outcome.Failed;
            }
            if (asset.TryGetComponent(out FormationModule live) && live.enabled)
            {
                report.Append($"  ! {file}: has a live FormationModule (it may walk in a caravan on purpose), skipped; a resident " +
                              "at home must have it switched off. Decide by hand.\n");
                return Outcome.Skipped;
            }

            File.WriteAllText(Path.Combine(snapshots, file + ".before.prefab"), before);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            bool saved;
            try
            {
                if (!contents.TryGetComponent(out ExpeditionMember _)) contents.AddComponent<ExpeditionMember>();
                if (!contents.TryGetComponent(out FormationModule formation)) formation = contents.AddComponent<FormationModule>();
                SwitchOff(formation);
                PrefabUtility.SaveAsPrefabAsset(contents, path, out saved);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            string after = File.ReadAllText(path);
            File.WriteAllText(Path.Combine(snapshots, file + ".after.prefab"), after);
            (int added, int removed) = LineChanges(before, after);
            string problem = !saved ? "SaveAsPrefabAsset reported failure"
                : !Prepared(AssetDatabase.LoadAssetAtPath<GameObject>(path)) ? "the saved prefab lacks the components or their settings"
                : Hashes(before) != Hashes(after) ? "a NetworkObject GlobalObjectIdHash changed"
                : null;

            report.Append($"  {(problem != null ? "! " : "")}{file}: +{added} -{removed} lines, GlobalObjectIdHash " +
                          $"{Hashes(before)} -> {Hashes(after)}{(problem != null ? $": {problem}" : "")}\n");
            return problem != null ? Outcome.Failed : Outcome.Changed;
        }

        /// <summary>It has an ExpeditionMember and a FormationModule that is switched off, in no formation, at Social priority.</summary>
        private static bool Prepared(GameObject prefab)
        {
            if (prefab == null || !prefab.TryGetComponent(out ExpeditionMember _)) return false;
            if (!prefab.TryGetComponent(out FormationModule formation) || formation.enabled) return false;

            var serialized = new SerializedObject(formation);
            return serialized.FindProperty(FormationIdField).stringValue.Length == 0 &&
                   serialized.FindProperty(PriorityField).intValue == ModulePriority.Social;
        }

        private static void SwitchOff(FormationModule formation)
        {
            var serialized = new SerializedObject(formation);
            serialized.FindProperty(FormationIdField).stringValue = string.Empty;
            serialized.FindProperty(PriorityField).intValue = ModulePriority.Social;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            formation.enabled = false;
        }

        /// <summary>Every NetworkObject's GlobalObjectIdHash in the file, in file order.</summary>
        private static string Hashes(string prefabText) =>
            string.Join(",", HashPattern.Matches(prefabText).Cast<Match>().Select(m => m.Groups[1].Value));

        /// <summary>Lines the save added and removed, compared as multisets (YAML blocks move, so order is not diffed).</summary>
        private static (int added, int removed) LineChanges(string before, string after)
        {
            var counts = new Dictionary<string, int>();
            foreach (string line in before.Split('\n')) counts[line] = counts.TryGetValue(line, out int n) ? n + 1 : 1;
            foreach (string line in after.Split('\n')) counts[line] = counts.TryGetValue(line, out int n) ? n - 1 : -1;

            int removed = counts.Values.Where(n => n > 0).Sum();
            int added = -counts.Values.Where(n => n < 0).Sum();
            return (added, removed);
        }
    }
}
