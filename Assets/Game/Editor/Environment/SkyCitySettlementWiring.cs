// Makes the Sky City a settlement of the Sky Tribe: a home site, an alarm and a population that
// fills its promenades with sky nomads and keeps them filled.
//
// Everything goes on the SkyCityFleet prefab ROOT, next to StaticNavMeshData, for the same reason
// that does: the root is what the scene places and moves, so the town's centre, its radii and the
// NavMesh it spawns on all move together. The scene instance picks it up from the prefab.
//
// The city's NavMesh is in pieces (roofs, gas-bag tops, ladder-only upper decks and the escorts'
// decks are islands nothing walks to), so the population is given a PromenadeAnchor child on the
// starboard lane and only spawns where a complete path from it leads. Wandering then cannot leave
// that region: a NavMesh island is only left by a link, and the city has none.
//
// Re-run from: Tools > Environment > Wire Sky City Settlement. Build Sky Fleet Prefabs saves the
// fleet from scratch and so ends by running this again.
//
// Multiplayer / persistence -- as ClankerSettlementBuilder's town: the components decide on the
// server (Network.Decides) and hold no state; the people they spawn replicate and save themselves.
// Unlike that town the city is not in a chunk scene, while its people are saved into the chunks
// under them, so the population keeps those chunks loaded and waits for them before counting
// (keepGroundChunksLoaded) -- or a reload spawns a second city's worth beside the restored one.
using SpaceGame.Agents;
using SpaceGame.World;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class SkyCitySettlementWiring
    {
        public const string AnchorName = "PromenadeAnchor";

        // Centre of the starboard promenade lane, in the city's modelled metres (sky_city_BUILD.md:
        // a clear lane at x 4.2-7.0 each side of the keel, walking surface at 0).
        private static readonly Vector3 PromenadeLaneCentre = new(5.6f, 0f, 0f);

        // The walkable decks reach ~90 m fore and aft of the root (scaled), and ~26 m to each side.
        private const float SpawnInnerRadius = 0f;
        private const float SpawnOuterRadius = 90f;
        // Covers every deck and stops short of the escorts' stations (~120 m out).
        private const float CityRadius = 100f;

        private const int PopulationCap = 16;
        private const float PopulationInterval = 45f;
        private const int PopulationWave = 4;
        // Four waves a second apart fill the city from empty before anyone could fly there.
        private const int InitialWaves = 4;
        private const float InitialWaveInterval = 1f;
        // A long, thin town in a round ring: about a third of ring samples land on the promenades.
        private const int PlacementAttempts = 24;

        [MenuItem("Tools/Environment/Wire Sky City Settlement")]
        public static void WireMenu() => Debug.Log(Wire());

        [MenuItem("Tools/Environment/Wire Sky City Settlement", validate = true)]
        private static bool CanWire() => !EditorApplication.isPlaying;

        /// <summary>Wires the fleet prefab root, saves it and returns a human-readable report.</summary>
        public static string Wire()
        {
            GameObject fleet = PrefabUtility.LoadPrefabContents(SkyFleetBuilder.FleetPrefabPath);
            try
            {
                string problem = Apply(fleet);
                if (problem != null)
                    return $"[SkyCitySettlementWiring] {problem} - nothing saved.";

                PrefabUtility.SaveAsPrefabAsset(fleet, SkyFleetBuilder.FleetPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(fleet);
            }

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(SkyFleetBuilder.FleetPrefabPath);
            bool ok = Verify(saved, out string report);
            return $"[SkyCitySettlementWiring] {(ok ? "wired" : "FAILED")} {SkyFleetBuilder.FleetPrefabPath}\n{report}";
        }

        /// <summary>Returns null when wired, otherwise what was missing.</summary>
        private static string Apply(GameObject root)
        {
            var sky = AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.SkyFactionPath);
            var table = AssetDatabase.LoadAssetAtPath<FactionRelationshipTable>(EntityFactionWiring.RelationshipsPath);
            if (sky == null) return $"no {RosterAuthoring.SkyFactionPath}";
            if (table == null) return $"no {EntityFactionWiring.RelationshipsPath}";

            var inhabitants = new SettlementPopulation.Inhabitant[NomadPrefabBuilder.SkyTribePeople.Length];
            for (int i = 0; i < inhabitants.Length; i++)
            {
                string path = NomadPrefabBuilder.SkyTribePeople[i].PrefabPath;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) return $"no {path} - run Tools > SpaceGame > Agents > Build Sky Nomad NPCs / Build Sky Soldier NPC";
                inhabitants[i] = new SettlementPopulation.Inhabitant { prefab = prefab, weight = 1 };
            }

            if (!root.TryGetComponent(out WorldSiteMarker marker))
                marker = root.AddComponent<WorldSiteMarker>();
            var site = new SerializedObject(marker);
            site.FindProperty("kind").enumValueIndex = (int)SiteKind.Home;
            site.FindProperty("siteName").stringValue = WorldSite.SkyCityName;
            site.FindProperty("radius").floatValue = CityRadius;
            // Ground errands searching Home would otherwise send NPCs to walk under a city 228 m up.
            site.FindProperty("airborne").boolValue = true;
            site.ApplyModifiedPropertiesWithoutUndo();

            if (!root.TryGetComponent(out SettlementAlarm alarm))
                alarm = root.AddComponent<SettlementAlarm>();
            alarm.Configure(sky, table, CityRadius);

            Transform anchor = root.transform.Find(AnchorName);
            if (anchor == null)
            {
                anchor = new GameObject(AnchorName).transform;
                anchor.SetParent(root.transform, false);
            }
            anchor.SetLocalPositionAndRotation(PromenadeLaneCentre * SkyCityBuilder.Scale, Quaternion.identity);

            if (!root.TryGetComponent(out SettlementPopulation population))
                population = root.AddComponent<SettlementPopulation>();
            population.Configure(sky, table, inhabitants, PopulationCap, PopulationInterval,
                                 SpawnInnerRadius, SpawnOuterRadius, CityRadius);
            var pace = new SerializedObject(population);
            pace.FindProperty("spawnsPerWave").intValue = PopulationWave;
            pace.FindProperty("initialWaves").intValue = InitialWaves;
            pace.FindProperty("initialWaveInterval").floatValue = InitialWaveInterval;
            pace.FindProperty("placementAttempts").intValue = PlacementAttempts;
            pace.FindProperty("reachableFrom").objectReferenceValue = anchor;
            pace.FindProperty("keepGroundChunksLoaded").boolValue = true;
            pace.ApplyModifiedPropertiesWithoutUndo();
            return null;
        }

        /// <summary>
        /// The post-condition: <paramref name="root"/> is a Sky Tribe home with an alarm and a
        /// population of the four sky nomads that spawns from the promenade anchor.
        /// </summary>
        public static bool Verify(GameObject root, out string report)
        {
            var sb = new System.Text.StringBuilder();
            var sky = AssetDatabase.LoadAssetAtPath<FactionDefinition>(RosterAuthoring.SkyFactionPath);

            if (!root.TryGetComponent(out WorldSiteMarker marker))
                sb.AppendLine("  no WorldSiteMarker");
            else if (marker.Kind != SiteKind.Home || marker.SiteName != WorldSite.SkyCityName)
                sb.AppendLine($"  WorldSiteMarker is {marker.Kind} '{marker.SiteName}', not Home '{WorldSite.SkyCityName}'");
            else if (!marker.Airborne)
                sb.AppendLine("  WorldSiteMarker is not airborne - ground errands could walk under the city");

            if (!root.TryGetComponent(out SettlementAlarm alarm))
                sb.AppendLine("  no SettlementAlarm");
            else if (new SerializedObject(alarm).FindProperty("owner").objectReferenceValue != sky)
                sb.AppendLine("  SettlementAlarm is not the Sky Tribe's");

            if (!root.TryGetComponent(out SettlementPopulation population))
            {
                sb.AppendLine("  no SettlementPopulation");
            }
            else
            {
                var so = new SerializedObject(population);
                if (so.FindProperty("owner").objectReferenceValue != sky)
                    sb.AppendLine("  SettlementPopulation is not the Sky Tribe's");

                SerializedProperty inhabitants = so.FindProperty("inhabitants");
                if (inhabitants.arraySize != NomadPrefabBuilder.SkyTribePeople.Length)
                    sb.AppendLine($"  {inhabitants.arraySize} inhabitants, not {NomadPrefabBuilder.SkyTribePeople.Length}");
                for (int i = 0; i < inhabitants.arraySize; i++)
                    if (inhabitants.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue == null)
                        sb.AppendLine($"  inhabitant {i} has no prefab");

                if (!so.FindProperty("keepGroundChunksLoaded").boolValue)
                    sb.AppendLine("  keepGroundChunksLoaded is off - every reload would spawn a second town");

                var anchor = so.FindProperty("reachableFrom").objectReferenceValue as Transform;
                if (anchor == null || anchor.parent != root.transform)
                    sb.AppendLine($"  reachableFrom is not the root's {AnchorName}");
            }

            bool ok = sb.Length == 0;
            report = ok
                ? $"  Home '{WorldSite.SkyCityName}', alarm and population r={CityRadius} m, cap {PopulationCap} every " +
                  $"{PopulationInterval} s after {InitialWaves} quick waves, spawning from {AnchorName}"
                : sb.ToString();
            return ok;
        }
    }
}
