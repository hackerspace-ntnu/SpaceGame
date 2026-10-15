// Expeditions are culture-blind: a settlement runs bands because its culture has an ExpeditionProfile and its baked
// catalog entry is complete, never because code knows which people it is. These two guards keep it that way. The
// first reads source files as text, so it is pure and needs no Editor; the second reads the baked site catalogs.
//
// The source scan is not exempt for comments: a comment that names one people is how a special case starts.
// Residents/ predates expeditions and names cultures in places on purpose (carry rules, content builders), so
// only its lines that name an expedition member are scanned there, not whole files.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using SpaceGame.Agents.Expeditions;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class ExpeditionGenericityTests
    {
        private static readonly Regex CultureOrSpecies = new Regex("raxy|nomad|drifter|astronaut", RegexOptions.IgnoreCase);

        /// <summary>Folders whose every source file is expedition code.</summary>
        private static readonly string[] ExpeditionFolders =
        {
            "Assets/Game/Scripts/agents/Expeditions",
            "Assets/Game/Editor/Agents/Expeditions",
        };

        /// <summary>Files inside <see cref="ExpeditionFolders"/> that name a culture by design.</summary>
        private static readonly string[] Exempt =
        {
            "Assets/Game/Editor/Agents/Expeditions/ExpeditionContentMenu.cs", // authors one culture's content: naming it is its job
        };

        /// <summary>Expedition code outside <see cref="ExpeditionFolders"/>.</summary>
        private static readonly string[] ExpeditionFiles =
        {
            "Assets/Game/Scripts/Core/Persistence/Adapters/ExpeditionSaveable.cs",                 // saves the director's records
            "Assets/Game/Scripts/World/ProceduralGeneration/Settlement/Spots/SettlementMuster.cs", // places the spot every band leaves from
            "Assets/Game/Scripts/World/Sites/WorldSiteCatalog.cs",                                 // which settlements run bands, and their rosters
            "Assets/Game/Editor/World/WorldSiteCatalogBaker.cs",                                   // bakes those entries and the prefab table
            "Assets/Game/Editor/World/WorldChunkScenes.cs",                                        // the chunk walk the baker and the menus share
        };

        private const string ResidentsFolder = "Assets/Game/Scripts/agents/Residents";

        /// <summary>A line in <see cref="ResidentsFolder"/> that names an expedition member is an expedition edit.</summary>
        private static readonly Regex ExpeditionMember =
            new Regex(@"(?i:expedition)|\b(IsAway|GoAway|ComeHome|IsWithBand|sourcePrefab|Away)\b");

        // ── genericity (pure) ────────────────────────────────────────────────────────────────────

        [Test]
        public void ExpeditionCode_NamesNoCultureOrSpecies()
        {
            string root = ProjectRoot();
            var exempt = new HashSet<string>(Exempt.Select(path => Resolve(root, path)), StringComparer.OrdinalIgnoreCase);
            var hits = new List<string>();

            foreach (string folder in ExpeditionFolders)
                foreach (string file in Directory.GetFiles(Resolve(root, folder), "*.cs", SearchOption.AllDirectories))
                    if (!exempt.Contains(file)) Scan(root, file, _ => true, hits);

            foreach (string path in ExpeditionFiles)
                Scan(root, Resolve(root, path), _ => true, hits);

            foreach (string file in Directory.GetFiles(Resolve(root, ResidentsFolder), "*.cs", SearchOption.AllDirectories))
                Scan(root, file, line => ExpeditionMember.IsMatch(line), hits);

            Assert.IsEmpty(hits, "Expedition code names a culture or species. A people's specifics belong on its " +
                                 "ExpeditionProfile, goals, kits and archetypes, not in code (comments included):\n  " +
                                 string.Join("\n  ", hits));
        }

        // ── completeness of the baked catalog (Editor) ──────────────────────────────────────────

        [Test]
        public void EverySettlementWithAProfile_IsComplete()
        {
            var expeditions = Resources.Load<ExpeditionCatalog>(ExpeditionCatalog.ResourcePath);
            Assert.IsNotNull(expeditions, $"No ExpeditionCatalog at Resources/{ExpeditionCatalog.ResourcePath}. " +
                                          "Run Tools/SpaceGame/Expeditions/Author Expedition Content.");

            List<WorldStreamingConfig> configs = WorldChunkScenes.AllConfigs();
            Assert.IsNotEmpty(configs, "No WorldStreamingConfig in the project: there is no world to check.");

            var problems = new List<string>();
            int settlements = 0;
            foreach (WorldStreamingConfig config in configs)
            {
                WorldSiteCatalog catalog = config.siteCatalog;
                if (catalog == null)
                {
                    problems.Add($"{config.name}: catalog not baked. Run {WorldSiteCatalogBaker.MenuPath}.");
                    continue;
                }

                foreach (WorldSiteCatalog.SettlementEntry settlement in catalog.settlements)
                {
                    settlements++;
                    problems.AddRange(Incomplete(settlement, expeditions).Select(p => $"{config.name}/{settlement.name}: {p}"));
                }
            }

            Assert.IsEmpty(problems, "Baked settlements that cannot run bands:\n  " + string.Join("\n  ", problems));
            Assert.Greater(settlements, 0, "No baked catalog lists a settlement with an expedition profile, so no band can " +
                                           $"ever leave. Give a settlement's culture a profile, then run {WorldSiteCatalogBaker.MenuPath}.");
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────

        /// <summary>What <paramref name="settlement"/> lacks to run bands; empty when it lacks nothing.</summary>
        private static IEnumerable<string> Incomplete(WorldSiteCatalog.SettlementEntry settlement, ExpeditionCatalog expeditions)
        {
            if (string.IsNullOrEmpty(settlement.settlementId))
                yield return "no settlement id. Run Tools/SpaceGame/Expeditions/Stamp Settlement Identity, then re-bake.";

            if (!settlement.hasMuster)
                yield return "no muster spot. Run Tools/SpaceGame/Expeditions/Place Muster Spots, then re-bake.";
            else if (settlement.musterForward == Vector3.zero)
                yield return "its muster spot faces nowhere (level forward is zero). Re-place it, then re-bake.";

            int index = settlement.cultureProfileIndex;
            if (index < 0 || index >= expeditions.profiles.Length || expeditions.profiles[index] == null)
            {
                yield return $"profile index {index} is not in the ExpeditionCatalog, so its quota cannot be checked.";
                yield break;
            }

            ExpeditionProfile profile = expeditions.profiles[index];
            foreach ((ExpeditionRole role, int quota) in ExpeditionRules.Quotas(profile, settlement.beds))
            {
                int holders = (settlement.roster ?? Array.Empty<RosterEntry>())
                    .Count(r => r != null && r.adult && (r.roles & role) != 0);
                if (holders < quota)
                    yield return $"{holders} adult {role}(s) for {settlement.beds} beds, quota {quota}. " +
                                 "Run Tools/SpaceGame/Expeditions/Apply Role Quotas (selected settlement), then re-bake.";
            }
        }

        /// <summary>Appends "path:line: text" to <paramref name="hits"/> for each line of <paramref name="file"/> that
        /// <paramref name="inScope"/> accepts and that names a culture or species.</summary>
        private static void Scan(string root, string file, Func<string, bool> inScope, List<string> hits)
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (inScope(lines[i]) && CultureOrSpecies.IsMatch(lines[i]))
                    hits.Add($"{Relative(root, file)}:{i + 1}: {lines[i].Trim()}");
        }

        /// <summary>The project folder: the working directory, in the Editor and in an out-of-Editor test run alike.</summary>
        private static string ProjectRoot()
        {
            string root = Directory.GetCurrentDirectory();
            Assert.That(Directory.Exists(Path.Combine(root, "Assets")),
                        $"The working directory '{root}' is not the project folder (no Assets/ in it).");
            return root;
        }

        /// <summary>
        /// The on-disk path of <paramref name="relative"/>, matching each segment regardless of case: git tracks both
        /// Scripts/Agents and Scripts/agents, and only one of them is on disk. Fails when it does not exist.
        /// </summary>
        private static string Resolve(string root, string relative)
        {
            string path = root;
            foreach (string segment in relative.Split('/'))
            {
                string match = Directory.EnumerateFileSystemEntries(path)
                    .FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase));
                Assert.IsNotNull(match, $"{relative} does not exist (no '{segment}' in {Relative(root, path)}). Moved? Update this test.");
                path = match;
            }
            return path;
        }

        private static string Relative(string root, string path) =>
            path.Length > root.Length ? path.Substring(root.Length + 1).Replace('\\', '/') : path;
    }
}
