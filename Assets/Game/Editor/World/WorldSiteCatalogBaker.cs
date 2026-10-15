// Bakes each world's WorldSiteCatalog from its chunk scenes: every WorldSiteMarker, and every settlement whose
// culture has an expedition profile with its muster pose, beds and roster. Also writes the ExpeditionCatalog's
// prefab table — every resident prefab a baked roster names, by GUID — so a stand-in can be spawned while its
// settlement's chunk is not loaded.
//
// It reads scenes and writes assets, nothing else. A chunk scene that is not open is opened additively, read and
// closed again; one already open is read as it stands, unsaved edits included. No scene is ever saved. Each asset
// is written with AssetDatabase.SaveAssetIfDirty (never SaveAssets, which would also flush pending terrain edits)
// and read back from disk: a write that did not land is an error.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpaceGame.Agents.Expeditions;
using SpaceGame.Agents;
using SpaceGame.Agents.Residents;
using SpaceGame.Core.Persistence;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WorldSiteCatalogBaker
    {
        public const string MenuPath = "Tools/SpaceGame/World/Bake Site Catalog";
        private const string Tag = "[WorldSiteCatalogBaker]";
        // A world's catalog is created next to its config as <config name> + this.
        private const string CatalogSuffix = "Sites";

        private sealed class Bake
        {
            public readonly StringBuilder report = new StringBuilder();
            public readonly SortedDictionary<string, GameObject> prefabsByGuid = new SortedDictionary<string, GameObject>(StringComparer.Ordinal);
            public ExpeditionCatalog expeditions;
            public bool problems;

            public void Problem(string line)
            {
                problems = true;
                report.Append("  ! ").Append(line).Append('\n');
            }
        }

        [MenuItem(MenuPath)]
        public static void BakeMenu()
        {
            var bake = new Bake();
            bake.report.Append($"{Tag} Bake Site Catalog\n");
            bake.expeditions = LoadExpeditionCatalog(bake);

            try
            {
                foreach (WorldStreamingConfig config in WorldChunkScenes.AllConfigs())
                    BakeWorld(config, bake);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (bake.expeditions != null) WritePrefabTable(bake);

            if (bake.problems) Debug.LogWarning(bake.report.ToString());
            else Debug.Log(bake.report.ToString());
        }

        [MenuItem(MenuPath, validate = true)]
        private static bool CanBake() => !EditorApplication.isPlaying;

        // ── one world ────────────────────────────────────────────────────────────────────────────

        private static void BakeWorld(WorldStreamingConfig config, Bake bake)
        {
            var sites = new List<WorldSiteCatalog.SiteEntry>();
            var settlements = new List<WorldSiteCatalog.SettlementEntry>();
            var towns = new List<WorldSiteCatalog.TownEntry>();
            List<string> scenes = WorldChunkScenes.ScenePaths(config, bake.Problem);

            for (int i = 0; i < scenes.Count; i++)
            {
                EditorUtility.DisplayProgressBar("Bake Site Catalog", $"{config.name}: {Path.GetFileName(scenes[i])}", (float)i / scenes.Count);
                ReadChunkScene(scenes[i], scene => Collect(scene, sites, settlements, towns, bake));
            }

            ReportSharedIds(config, sites, bake);

            WorldSiteCatalog catalog = CatalogFor(config, bake);
            if (catalog == null) return;

            catalog.sites = sites.ToArray();
            catalog.settlements = settlements.ToArray();
            catalog.towns = towns.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);

            string problem = CatalogProblemInFile(catalog, File.ReadAllText(AssetDatabase.GetAssetPath(catalog)));
            if (problem != null)
            {
                Debug.LogError($"{Tag} {AssetDatabase.GetAssetPath(catalog)} was saved without the bake: {problem}.", catalog);
                return;
            }

            int rows = settlements.Sum(s => s.roster.Length);
            bake.report.Append($"  {config.name}: {sites.Count} site(s), {settlements.Count} settlement(s) ({rows} roster row(s)), {towns.Count} town(s) " +
                               $"from {scenes.Count} chunk scene(s) → {AssetDatabase.GetAssetPath(catalog)}\n");
        }

        /// <summary>Runs <paramref name="read"/> on a chunk scene: as it stands when it is open, else opened additively and closed unsaved.</summary>
        private static void ReadChunkScene(string path, Action<Scene> read)
        {
            Scene open = SceneManager.GetSceneByPath(path);
            if (open.IsValid() && open.isLoaded)
            {
                read(open);
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                read(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void Collect(Scene scene, List<WorldSiteCatalog.SiteEntry> sites,
                                    List<WorldSiteCatalog.SettlementEntry> settlements,
                                    List<WorldSiteCatalog.TownEntry> towns, Bake bake)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (WorldSiteMarker marker in root.GetComponentsInChildren<WorldSiteMarker>(true))
                    sites.Add(new WorldSiteCatalog.SiteEntry
                    {
                        id = marker.SiteId,
                        kind = marker.Kind,
                        position = marker.transform.position,
                        radius = marker.Radius,
                        name = marker.SiteName,
                        airborne = marker.Airborne,
                    });

                foreach (Settlement settlement in root.GetComponentsInChildren<Settlement>(true))
                    if (settlement.Culture != null && settlement.Culture.expeditions != null)
                        settlements.Add(EntryFor(settlement, bake));

                foreach (Settlement settlement in root.GetComponentsInChildren<Settlement>(true))
                    towns.Add(TownFor(settlement, bake));

                foreach (SettlementPopulation population in root.GetComponentsInChildren<SettlementPopulation>(true))
                    if (population.GetComponent<Settlement>() == null)
                        towns.Add(TownFor(population, bake));
            }
        }

        // ── towns ────────────────────────────────────────────────────────────────────────────────

        /// <summary>A generated settlement: whose it is comes from its population's owner, else from its people.</summary>
        private static WorldSiteCatalog.TownEntry TownFor(Settlement settlement, Bake bake)
        {
            var population = settlement.GetComponent<SettlementPopulation>();
            FactionDefinition faction = population != null && population.Owner != null
                ? population.Owner
                : MostCommonFaction(settlement.CharacterPrefabs());

            if (faction == null)
                bake.Problem($"{settlement.gameObject.scene.name}/{settlement.name}: none of its people has a faction, " +
                             "so the lander's signal can never lead to it.");

            return new WorldSiteCatalog.TownEntry
            {
                id = TownId(settlement.gameObject),
                name = settlement.name,
                position = settlement.transform.position,
                radius = settlement.GeneratedExtent,
                faction = faction,
            };
        }

        /// <summary>A town with no generated layout (the Clankers'), whose owner is its population's.</summary>
        private static WorldSiteCatalog.TownEntry TownFor(SettlementPopulation population, Bake bake)
        {
            if (population.Owner == null)
                bake.Problem($"{population.gameObject.scene.name}/{population.name}: a SettlementPopulation with no owner.");

            return new WorldSiteCatalog.TownEntry
            {
                id = TownId(population.gameObject),
                name = population.name,
                position = population.transform.position,
                radius = population.CountRadius,
                faction = population.Owner,
            };
        }

        /// <summary>
        /// The settlement's authored identity when it has one (what <see cref="Settlement.SettlementId"/> reads),
        /// else the id derived from its scene and hierarchy, which an unchanged scene reproduces on every bake.
        /// </summary>
        private static string TownId(GameObject town)
        {
            var identity = town.GetComponent<SaveableEntity>();
            return identity != null && identity.IsAuthored && !string.IsNullOrEmpty(identity.InstanceId)
                ? identity.InstanceId
                : SaveableEntity.DeriveAuthoredId(town);
        }

        /// <summary>The faction most of <paramref name="people"/> belong to; a tie goes to the first one met. Null for none.</summary>
        private static FactionDefinition MostCommonFaction(IEnumerable<GameObject> people)
        {
            var counts = new Dictionary<FactionDefinition, int>();
            FactionDefinition best = null;

            foreach (GameObject person in people)
            {
                EntityFaction member = person.GetComponentInChildren<EntityFaction>(true);
                if (member == null || member.Faction == null) continue;

                counts.TryGetValue(member.Faction, out int count);
                counts[member.Faction] = ++count;
                if (best == null || count > counts[best]) best = member.Faction;
            }

            return best;
        }

        // ── settlements ──────────────────────────────────────────────────────────────────────────

        private static WorldSiteCatalog.SettlementEntry EntryFor(Settlement settlement, Bake bake)
        {
            SettlementCulture culture = settlement.Culture;
            ExpeditionProfile profile = culture.expeditions;
            string where = $"{settlement.gameObject.scene.name}/{settlement.name}";

            var entry = new WorldSiteCatalog.SettlementEntry
            {
                settlementId = settlement.SettlementId,
                name = settlement.name,
                position = settlement.transform.position,
                beds = settlement.Beds,
                cultureProfileIndex = bake.expeditions != null ? bake.expeditions.IndexOf(profile) : -1,
            };

            if (string.IsNullOrEmpty(entry.settlementId))
                bake.Problem($"{where}: no settlement id. Run Tools/SpaceGame/Expeditions/Stamp Settlement Identity and bake again.");
            if (bake.expeditions != null && entry.cultureProfileIndex < 0)
                bake.Problem($"{where}: its profile '{profile.name}' is not in the ExpeditionCatalog, so it can send no band.");

            if (TryFindMuster(settlement, profile.musterUse, out SettlementSpot muster))
            {
                entry.hasMuster = true;
                entry.musterPosition = muster.transform.position;
                entry.musterForward = Vector3.ProjectOnPlane(muster.transform.forward, Vector3.up).normalized;
            }
            else
            {
                bake.Problem($"{where}: no spot of muster use '{(profile.musterUse ? profile.musterUse.name : "(none set)")}', so it can send no band.");
            }

            entry.roster = Roster(settlement, culture, where, bake);
            return entry;
        }

        /// <summary>The first spot under the settlement whose use is the profile's muster use.</summary>
        private static bool TryFindMuster(Settlement settlement, SpotUse musterUse, out SettlementSpot muster)
        {
            muster = null;
            if (musterUse == null) return false;

            foreach (SettlementSpot spot in settlement.GetComponentsInChildren<SettlementSpot>())
                if (spot.Use == musterUse)
                {
                    muster = spot;
                    return true;
                }
            return false;
        }

        private static RosterEntry[] Roster(Settlement settlement, SettlementCulture culture, string where, Bake bake)
        {
            var roster = new List<RosterEntry>();
            int noPrefab = 0, unlisted = 0;

            foreach (Resident resident in settlement.Society.Residents)
            {
                RosterEntry row = RosterEntry.Of(resident, culture, PrefabGuid(resident.sourcePrefab, bake));

                if (string.IsNullOrEmpty(row.sourcePrefabGuid)) noPrefab++;
                if (row.archetypeIndex < 0) unlisted++;
                roster.Add(row);
            }

            if (noPrefab > 0)
                bake.Problem($"{where}: {noPrefab} resident(s) without a source prefab can never go out. " +
                             "Run Tools/SpaceGame/Expeditions/Back-fill Resident Source Prefabs and bake again.");
            if (unlisted > 0)
                bake.Problem($"{where}: {unlisted} resident(s) whose archetype is not in the culture's archetypes.");
            return roster.ToArray();
        }

        /// <summary>The asset GUID of <paramref name="prefab"/>, noted for the prefab table; empty for none.</summary>
        private static string PrefabGuid(GameObject prefab, Bake bake)
        {
            if (prefab == null) return string.Empty;

            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            if (!string.IsNullOrEmpty(guid)) bake.prefabsByGuid[guid] = prefab;
            return guid;
        }

        // ── assets ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The world's catalog: the one its config names, else the one next to the config (created when missing),
        /// which the config then names. Null when the config could not be saved naming it.
        /// </summary>
        private static WorldSiteCatalog CatalogFor(WorldStreamingConfig config, Bake bake)
        {
            if (config.siteCatalog != null) return config.siteCatalog;

            string configPath = AssetDatabase.GetAssetPath(config);
            string path = $"{Path.GetDirectoryName(configPath)?.Replace('\\', '/')}/{config.name}{CatalogSuffix}.asset";

            var catalog = AssetDatabase.LoadAssetAtPath<WorldSiteCatalog>(path);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<WorldSiteCatalog>();
                AssetDatabase.CreateAsset(catalog, path);
                bake.report.Append($"  created {path}\n");
            }

            config.siteCatalog = catalog;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssetIfDirty(config);

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (File.ReadAllText(configPath).Contains(guid)) return catalog;

            Debug.LogError($"{Tag} {configPath} was saved without its site catalog reference ({path}); nothing baked for {config.name}.", config);
            return null;
        }

        private static ExpeditionCatalog LoadExpeditionCatalog(Bake bake)
        {
            var catalog = Resources.Load<ExpeditionCatalog>(ExpeditionCatalog.ResourcePath);
            if (catalog != null && AssetDatabase.Contains(catalog)) return catalog;

            Debug.LogError($"{Tag} No ExpeditionCatalog at Resources/{ExpeditionCatalog.ResourcePath}: settlements are baked with no " +
                           "profile index and the prefab table is not written. Run Tools/SpaceGame/Expeditions/Author Expedition Content.");
            bake.problems = true;
            return null;
        }

        /// <summary>Replaces the expedition catalog's prefab table with every source prefab the baked rosters name.</summary>
        private static void WritePrefabTable(Bake bake)
        {
            ExpeditionCatalog catalog = bake.expeditions;
            catalog.prefabs = bake.prefabsByGuid.Select(pair => new PrefabEntry { guid = pair.Key, prefab = pair.Value }).ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);

            string path = AssetDatabase.GetAssetPath(catalog);
            string text = File.ReadAllText(path);
            List<string> missing = bake.prefabsByGuid.Keys.Where(guid => !text.Contains("guid: " + guid)).ToList();
            if (missing.Count > 0)
            {
                Debug.LogError($"{Tag} {path} was saved without {missing.Count} prefab table row(s): {string.Join(", ", missing)}.", catalog);
                return;
            }

            bake.report.Append($"  {path}: {catalog.prefabs.Length} resident prefab(s) in the prefab table\n");
        }

        /// <summary>What the saved catalog file lacks of the bake; null when every entry reached it.</summary>
        private static string CatalogProblemInFile(WorldSiteCatalog catalog, string text)
        {
            int rows = catalog.settlements.Sum(s => s.roster.Length);
            int sitesWritten = Occurrences(text, "airborne: ");
            int settlementsWritten = Occurrences(text, "settlementId: ");
            int rowsWritten = Occurrences(text, "residentKey: ");

            if (sitesWritten != catalog.sites.Length) return $"{sitesWritten} site(s) in the file, {catalog.sites.Length} baked";
            if (settlementsWritten != catalog.settlements.Length) return $"{settlementsWritten} settlement(s) in the file, {catalog.settlements.Length} baked";
            int townsWritten = Occurrences(text, "faction: ");
            if (townsWritten != catalog.towns.Length) return $"{townsWritten} town(s) in the file, {catalog.towns.Length} baked";
            if (rowsWritten != rows) return $"{rowsWritten} roster row(s) in the file, {rows} baked";

            List<string> missing = catalog.settlements.Select(s => s.settlementId)
                                          .Where(id => !string.IsNullOrEmpty(id) && !text.Contains(id)).ToList();
            return missing.Count == 0 ? null : "settlement id(s) missing: " + string.Join(", ", missing);
        }

        private static int Occurrences(string text, string token) => Regex.Matches(text, Regex.Escape(token)).Count;

        /// <summary>Two markers under one id are one site to the registry: the later one silently replaces the other.</summary>
        private static void ReportSharedIds(WorldStreamingConfig config, List<WorldSiteCatalog.SiteEntry> sites, Bake bake)
        {
            foreach (IGrouping<string, WorldSiteCatalog.SiteEntry> shared in sites.GroupBy(s => s.id).Where(g => g.Count() > 1))
                bake.Problem($"{config.name}: {shared.Count()} markers share the id '{shared.Key}' " +
                             $"({string.Join(", ", shared.Select(s => string.IsNullOrEmpty(s.name) ? s.kind.ToString() : s.name))}); " +
                             "the registry keeps only one of them. Give each its own id.");
        }
    }
}
