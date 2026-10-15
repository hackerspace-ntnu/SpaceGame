// Author Raxy Settlement Config: RaxySettlement.asset, a settlement of Raxy residents that sends bands out as soon as it
// is generated. Its layout is NomadSettlement.asset's -- buildings, decorations, streets, spacing, terrain shaping,
// culture and special characters -- copied when the asset is first made and the user's to tune after that. Its
// characters list is owned by this menu and written again on every run, so re-running it changes nothing else.
//
// Who is listed: every prefab directly in the Raxy character folder whose root has a Resident, once each. Clothes/ holds
// garments, worn by the characters, not people; a prefab with no Resident is not a character Generate can make a
// resident of, and is reported as left out.
//
// The copies are tuned for a settlement of ExpectedBeds beds (fourteen nomad houses of five) and the culture's
// expedition quotas at that size, which Generate moves in first (ExpeditionRules.PlanMoveIn):
//   - Bodies made for a quota's role (QuotaBodies) appear by certainty, not by luck: spawnChance 1, and enough copies
//     that those which need no spot of their own (Guard, Hunter, Scout: every settlement can host them) cover each
//     quota alone; the ones that need one (TowerGuard at a watch tower, Lookout at a gate) are margin for where those
//     spots exist. More guards than that would put every fifth resident on the perimeter walk: a body made for Guard
//     ignores the culture's 15 % patrol share.
//   - Every other body made for a role: FixedRoleCount copy at FixedRoleChance, so most trades are in every settlement
//     and two settlements still differ.
//   - Bodies with no role of their own (dealt one from their culture profile, or from every usable archetype):
//     DealtCount copies at DealtChance. They carry the culture's lifestyle and patrol shares.
//   The roll comes to roughly 85 copies for 70 beds (the report prints the expected number): the beds always fill and
//   about fifteen copies stay out, which is where the variety between two settlements comes from.
//
// Saved with SaveAssetIfDirty, never AssetDatabase.SaveAssets (it would flush pending terrain edits), then read back: the
// list in memory and the saved file must both name every prefab, and the quota bodies must cover the quotas.
// Next: Tools/SpaceGame/Expeditions/Prepare Resident Prefabs (covers every character of this config), then the network,
// saver and ragdoll sweeps it names.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents.Residents;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class RaxySettlementConfigMenu
    {
        private const string Tag = "[RaxySettlementConfig]";
        private const string SourcePath = "Assets/Game/ScriptableObjects/Settlements/NomadSettlement.asset";
        private const string TargetPath = "Assets/Game/ScriptableObjects/Settlements/RaxySettlement.asset";
        private const string CharacterFolder = "Assets/Game/Prefabs/agents/Characters/Raxy";

        // Fourteen nomad houses of five beds: the settlement the copies are tuned for.
        private const int ExpectedBeds = 70;
        private const int FixedRoleCount = 1;
        private const float FixedRoleChance = 0.9f;
        private const int DealtCount = 2;
        private const float DealtChance = 0.6f;
        private const float QuotaChance = 1f;

        // Bodies made for a quota's role, and their copies (see the header).
        private static readonly (string prefab, int count)[] QuotaBodies =
        {
            ("Raxy_Type_CaravanGuard", 3), ("Raxy_Type_DustRaider", 3), ("Raxy_Type_BountyHunter", 2), ("Raxy_Type_Duelist", 2), // Guard
            ("Raxy_Type_BeastHunter", 3),                                                    // Hunter: a warrior who goes hunting
            ("Raxy_Type_Sharpshooter", 2),                                                   // TowerGuard: needs a watch tower
            ("Raxy_Job_Scout", 2), ("Raxy_Type_SandRacer", 2), ("Raxy_Type_StormWalker", 1), // Scout
            ("Raxy_Job_Lookout", 1), ("Raxy_Type_KiteRider", 1),                             // Lookout: needs a gate
        };

        [MenuItem("Tools/SpaceGame/Residents/Author Raxy Settlement Config")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError($"{Tag} Exit Play mode first: asset edits made in Play mode are lost.");
                return;
            }

            var report = new StringBuilder($"{Tag} {TargetPath}\n");
            List<GameObject> characters = Characters(report);
            SettlementConfig config = Ensure(report);
            config.characters = characters.Select(Entry).ToArray();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);

            Verify(characters, report);
            Debug.Log(report.ToString());
        }

        /// <summary>Every resident character prefab directly in the folder, by name; the rest are reported as left out.</summary>
        private static List<GameObject> Characters(StringBuilder report)
        {
            var characters = new List<GameObject>();
            int garments = 0;
            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { CharacterFolder }).Select(AssetDatabase.GUIDToAssetPath)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                if (!string.Equals(Path.GetDirectoryName(path)?.Replace('\\', '/'), CharacterFolder, StringComparison.OrdinalIgnoreCase))
                {
                    garments++;
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab.TryGetComponent(out Resident _)) characters.Add(prefab);
                else report.Append($"  left out {prefab.name}: no Resident on its root, so it is not a character a settlement makes a resident of\n");
            }
            report.Append($"  left out {garments} prefab(s) in sub-folders (Clothes/: garments the characters wear)\n");

            foreach ((string name, int _) in QuotaBodies)
                if (characters.All(c => c.name != name))
                    Fail($"{name} is in the quota table but is no resident character in {CharacterFolder}. Renamed? Update QuotaBodies.");
            return characters;
        }

        /// <summary>The config, made as a copy of the nomad settlement's the first time; never re-copied after that.</summary>
        private static SettlementConfig Ensure(StringBuilder report)
        {
            var existing = AssetDatabase.LoadAssetAtPath<SettlementConfig>(TargetPath);
            if (existing != null)
            {
                report.Append("  exists: layout, culture and special characters kept as they are; characters rewritten\n");
                return existing;
            }

            var source = AssetDatabase.LoadAssetAtPath<SettlementConfig>(SourcePath);
            if (source == null) Fail($"{SourcePath} is missing: the settlement layout is copied from it.");

            var made = ScriptableObject.CreateInstance<SettlementConfig>();
            EditorUtility.CopySerialized(source, made);
            made.name = Path.GetFileNameWithoutExtension(TargetPath);
            AssetDatabase.CreateAsset(made, TargetPath);
            report.Append($"  made as a copy of {SourcePath}\n");
            return made;
        }

        private static SettlementConfig.SpawnEntry Entry(GameObject prefab)
        {
            (string _, int count) = QuotaBodies.FirstOrDefault(q => q.prefab == prefab.name);
            if (count > 0) return new SettlementConfig.SpawnEntry { prefab = prefab, count = count, spawnChance = QuotaChance };
            return ResidentAssignment.RoleOf(prefab) != null
                ? new SettlementConfig.SpawnEntry { prefab = prefab, count = FixedRoleCount, spawnChance = FixedRoleChance }
                : new SettlementConfig.SpawnEntry { prefab = prefab, count = DealtCount, spawnChance = DealtChance };
        }

        // ── read it back ─────────────────────────────────────────────────────────────────────────

        private static void Verify(List<GameObject> characters, StringBuilder report)
        {
            var config = AssetDatabase.LoadAssetAtPath<SettlementConfig>(TargetPath);
            if (config == null) Fail("the asset is not there after saving");
            if (config.characters.Length != characters.Count || characters.Any(c => config.characters.Count(e => e.prefab == c) != 1))
                Fail("the characters list does not name every character exactly once");

            string saved = File.ReadAllText(TargetPath);
            List<string> unsaved = characters.Where(c => !saved.Contains(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(c)))).Select(c => c.name).ToList();
            if (unsaved.Count > 0) Fail($"the saved file does not name {string.Join(", ", unsaved)}");

            ExpeditionProfile profile = config.culture != null ? config.culture.expeditions : null;
            if (profile == null) Fail("its culture has no expedition profile, so it would send no bands");

            report.Append("  prefab -> role -> count x chance\n");
            foreach (SettlementConfig.SpawnEntry entry in config.characters)
            {
                ResidentArchetype role = ResidentAssignment.RoleOf(entry.prefab);
                report.Append($"    {entry.prefab.name} -> {(role != null ? $"{role.name} ({role.expeditionRoles})" : "dealt by its culture profile")} -> " +
                              $"{entry.count} x {entry.spawnChance:0.##}\n");
            }

            foreach ((ExpeditionRole role, int quota) in ExpeditionRules.Quotas(profile, ExpectedBeds))
            {
                int anywhere = Certain(config, role, HostedAnywhere);
                int withSpot = Certain(config, role, a => !HostedAnywhere(a));
                report.Append($"  {role}: quota {quota} at {ExpectedBeds} beds; {anywhere} certain copies any settlement hosts, {withSpot} more where their spot exists\n");
                if (anywhere < quota) Fail($"only {anywhere} certain {role} copies need no spot, quota {quota}: raise their counts in QuotaBodies");
            }

            float expected = config.characters.Sum(e => e.count * e.spawnChance);
            report.Append($"  {config.characters.Length} characters, {config.characters.Sum(e => e.count)} copies at most, " +
                          $"{expected:0.#} expected for {ExpectedBeds} beds\n");
            if (expected <= ExpectedBeds) Fail($"the roll expects {expected:0.#} copies, no more than the {ExpectedBeds} beds: raise counts or chances");
        }

        // Copies that always appear (chance 1) of a body made for an archetype with the role.
        private static int Certain(SettlementConfig config, ExpeditionRole role, Func<ResidentArchetype, bool> where) =>
            config.characters
                .Where(e => e.spawnChance >= QuotaChance && ResidentAssignment.RoleOf(e.prefab) is { } a && (a.expeditionRoles & role) != 0 && where(a))
                .Sum(e => e.count);

        // What ResidentAssignment.Places.CanHost accepts in any settlement: a standing duty, or no post and no chore.
        private static bool HostedAnywhere(ResidentArchetype archetype) =>
            archetype.duty != ResidentDuty.None || (archetype.post == null && archetype.chore == null);

        private static void Fail(string message) => throw new InvalidOperationException($"{Tag} {message}");
    }
}
