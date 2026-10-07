// The baked site catalog is only worth anything while it matches the chunk scenes: a marker placed after the
// last bake is a destination nobody can pick until its chunk happens to load, and a settlement missing from it
// sends no band while the player is away. The two scene tests read the scene files as text rather than opening
// them, so they stay cheap and never touch the editor's open scenes; the merge tests are pure.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public class WorldSiteCatalogTests
    {
        private const string MarkerScriptGuid = "5380307fed0c24ef699d4fe52135d478";

        [SetUp]
        public void Setup() => WorldSiteRegistry.Clear();

        [TearDown]
        public void TearDown() => WorldSiteRegistry.Clear();

        // ── merge (pure) ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void Merge_RegistersSitesBeforeChunksLoad()
        {
            WorldSiteRegistry.MergeSites(new[]
            {
                Entry("ruin-far", SiteKind.Ruin, new Vector3(2000f, 0f, 0f)),
                Entry("city", SiteKind.Home, new Vector3(0f, 228f, 0f), airborne: true),
            });

            Assert.AreEqual(2, WorldSiteRegistry.Count);
            Assert.IsTrue(WorldSiteRegistry.TryFindNearest(SiteKind.Ruin, Vector3.zero, 5000f, out WorldSite ruin),
                          "a baked site must be a destination before any marker has enabled");
            Assert.AreEqual("ruin-far", ruin.Id);
            Assert.IsTrue(WorldSiteRegistry.TryGet("city", out WorldSite city));
            Assert.IsTrue(city.Airborne, "the airborne flag must survive the bake");
        }

        [Test]
        public void Merge_ThenMarkerEnables_UpdatesInPlace()
        {
            WorldSiteRegistry.MergeSites(new[] { Entry("well", SiteKind.WaterHole, Vector3.zero) });

            WorldSiteRegistry.Register(SiteKind.WaterHole, new Vector3(4f, 0f, 0f), 9f, "Well", "well");

            Assert.AreEqual(1, WorldSiteRegistry.Count, "the marker must update its baked entry, not add a second site");
            Assert.IsTrue(WorldSiteRegistry.TryGet("well", out WorldSite site));
            Assert.AreEqual(4f, site.Position.x, 0.001f, "the live marker's position wins");
        }

        [Test]
        public void Merge_NeverOverwritesALiveMarker()
        {
            WorldSiteRegistry.Register(SiteKind.Camp, new Vector3(7f, 0f, 0f), 5f, "Camp", "camp");

            WorldSiteRegistry.MergeSites(new[] { Entry("camp", SiteKind.Camp, Vector3.zero) });

            Assert.AreEqual(1, WorldSiteRegistry.Count);
            Assert.IsTrue(WorldSiteRegistry.TryGet("camp", out WorldSite site));
            Assert.AreEqual(7f, site.Position.x, 0.001f, "a marker that enabled first knows where it is now; the bake does not");
        }

        // ── the baked asset against the scenes (Editor) ──────────────────────────────────────────

        [Test]
        public void Catalog_IncludesEveryMarkerInEveryChunkScene()
        {
            foreach (WorldStreamingConfig config in WorldChunkScenes.AllConfigs())
            {
                WorldSiteCatalog catalog = config.siteCatalog;
                Assert.IsNotNull(catalog, $"{config.name} has no site catalog. Run {WorldSiteCatalogBaker.MenuPath}.");

                var bakedIds = new HashSet<string>(catalog.sites.Select(s => s.id));
                int expected = 0;
                var missing = new List<string>();

                foreach (string path in WorldChunkScenes.ScenePaths(config, problem => Assert.Fail(problem)))
                {
                    SceneScan scan = SceneScan.Read(path);
                    foreach (Block marker in scan.WithScript(MarkerScriptGuid))
                    {
                        expected++;
                        string id = marker.Field("id");
                        if (!string.IsNullOrEmpty(id) && !bakedIds.Contains(id)) missing.Add($"{Path.GetFileName(path)}: {id}");
                    }
                    expected += scan.ComponentsInPrefabInstances<WorldSiteMarker>().Count;
                }

                Assert.IsEmpty(missing, $"{config.name}: markers missing from the catalog. Re-bake.\n  {string.Join("\n  ", missing)}");
                Assert.AreEqual(expected, catalog.sites.Length,
                                $"{config.name}: {expected} marker(s) in the chunk scenes, {catalog.sites.Length} in the catalog. Re-bake.");
            }
        }

        [Test]
        public void Catalog_IncludesEverySettlementWithAProfile()
        {
            string[] settlementScripts = SettlementScriptGuids();

            foreach (WorldStreamingConfig config in WorldChunkScenes.AllConfigs())
            {
                WorldSiteCatalog catalog = config.siteCatalog;
                Assert.IsNotNull(catalog, $"{config.name} has no site catalog. Run {WorldSiteCatalogBaker.MenuPath}.");

                int expected = 0;
                foreach (string path in WorldChunkScenes.ScenePaths(config, problem => Assert.Fail(problem)))
                {
                    SceneScan scan = SceneScan.Read(path);
                    foreach (string script in settlementScripts)
                        foreach (Block settlement in scan.WithScript(script))
                            if (HasProfile(settlement.ReferencedAsset<SettlementConfig>("config"))) expected++;

                    foreach (Settlement settlement in scan.ComponentsInPrefabInstances<Settlement>())
                        if (settlement.Culture != null && settlement.Culture.expeditions != null) expected++;
                }

                Assert.AreEqual(expected, catalog.settlements.Length,
                                $"{config.name}: {expected} settlement(s) with an expedition profile in the chunk scenes, " +
                                $"{catalog.settlements.Length} in the catalog. Re-bake.");

                List<string> ids = catalog.settlements.Select(s => s.settlementId).ToList();
                Assert.IsFalse(ids.Any(string.IsNullOrEmpty),
                               $"{config.name}: a baked settlement has no id. Run Tools/SpaceGame/Expeditions/Stamp Settlement Identity, then re-bake.");
                Assert.AreEqual(ids.Count, ids.Distinct().Count(), $"{config.name}: two baked settlements share an id.");
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────────────────────────

        private static WorldSiteCatalog.SiteEntry Entry(string id, SiteKind kind, Vector3 position, bool airborne = false) =>
            new WorldSiteCatalog.SiteEntry { id = id, kind = kind, position = position, radius = 10f, name = id, airborne = airborne };

        private static bool HasProfile(SettlementConfig config) =>
            config != null && config.culture != null && config.culture.expeditions != null;

        /// <summary>The script GUIDs of <see cref="Settlement"/> and every subclass of it.</summary>
        private static string[] SettlementScriptGuids() =>
            AssetDatabase.FindAssets("t:MonoScript")
                         .Select(guid => (guid, script: AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid))))
                         .Where(s => s.script != null && s.script.GetClass() != null && typeof(Settlement).IsAssignableFrom(s.script.GetClass()))
                         .Select(s => s.guid)
                         .ToArray();

        /// <summary>One YAML document of a scene file: its top-level fields, first occurrence of each.</summary>
        private sealed class Block
        {
            public readonly Dictionary<string, string> fields = new Dictionary<string, string>();

            public string Field(string key) => fields.TryGetValue(key, out string value) ? value.Trim() : string.Empty;

            /// <summary>The asset GUID in a reference field such as <c>{fileID: 11400000, guid: X, type: 2}</c>; empty when none.</summary>
            public string GuidIn(string key)
            {
                string value = Field(key);
                int at = value.IndexOf("guid: ", StringComparison.Ordinal);
                if (at < 0) return string.Empty;
                int start = at + "guid: ".Length;
                int end = value.IndexOfAny(new[] { ',', '}' }, start);
                return end < 0 ? value.Substring(start) : value.Substring(start, end - start);
            }

            public T ReferencedAsset<T>(string key) where T : UnityEngine.Object
            {
                string guid = GuidIn(key);
                return string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        /// <summary>A scene file split into its YAML documents, without deserializing it.</summary>
        private sealed class SceneScan
        {
            private readonly List<Block> blocks = new List<Block>();

            public static SceneScan Read(string path)
            {
                var scan = new SceneScan();
                Block current = null;
                foreach (string line in File.ReadLines(path))
                {
                    if (line.StartsWith("--- !u!", StringComparison.Ordinal))
                    {
                        current = new Block();
                        scan.blocks.Add(current);
                        continue;
                    }
                    if (current == null || line.Length < 3 || line[0] != ' ' || line[1] != ' ' || line[2] == ' ' || line[2] == '-') continue;

                    int colon = line.IndexOf(':');
                    if (colon < 0) continue;
                    string key = line.Substring(2, colon - 2);
                    if (!current.fields.ContainsKey(key)) current.fields[key] = line.Substring(colon + 1);
                }
                return scan;
            }

            public IEnumerable<Block> WithScript(string scriptGuid) =>
                blocks.Where(b => b.fields.ContainsKey("m_Script") && b.GuidIn("m_Script") == scriptGuid);

            /// <summary>
            /// Components of type <typeparamref name="T"/> inside the prefabs this scene places, nested prefabs
            /// included. A placed prefab's components are not written into the scene file, only its source.
            /// </summary>
            public List<T> ComponentsInPrefabInstances<T>() where T : Component
            {
                var found = new List<T>();
                var bySource = new Dictionary<string, T[]>();
                foreach (Block instance in blocks)
                {
                    string source = instance.GuidIn("m_SourcePrefab");
                    if (string.IsNullOrEmpty(source)) continue;

                    if (!bySource.TryGetValue(source, out T[] components))
                    {
                        var prefab = instance.ReferencedAsset<GameObject>("m_SourcePrefab");
                        components = prefab != null ? prefab.GetComponentsInChildren<T>(true) : Array.Empty<T>();
                        bySource[source] = components;
                    }
                    found.AddRange(components);
                }
                return found;
            }
        }
    }
}
