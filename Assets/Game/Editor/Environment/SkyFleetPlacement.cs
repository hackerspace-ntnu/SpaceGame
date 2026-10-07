// Puts the Sky fleet into the world: the flagship (SkyCityFleet) at the first waypoint of its drift
// route, and its escorts on station round it — in persistentScene, where the city lives because it is
// bigger than a chunk and outlives any of them.
//
// Re-run from: Tools > Environment > Place Sky Fleet In World, and run by Build Sky Fleet Prefabs as
// its last step: a rebuild re-adds the flagship's components under new file ids, which orphans the
// scene's overrides on them (the baked identity first of all) until they are written again. Idempotent: it finds the fleet's roots
// by name, re-poses and re-configures them, adds what is missing and removes escorts beyond the list.
// It opens persistentScene additively (leaving whatever the editor has open alone), saves only that
// scene, and closes it again if it opened it.
//
// The route (spec docs/superpowers/specs/2026-10-04-drifting-sky-fleet-design.md): a loop over the
// low basin east of the western mountains, in view of the spawn, every leg clear of the two rock
// spires (x≈2900 and x≈3750 near z≈1150 — the second is where the city used to stand, through its
// decks). HighestGroundAlong measures each leg's corridor against the world NavMesh, which
// SkyFleetPlacementTests holds to CruiseAltitude.
//
// Identity: the flagship and every escort get a BAKED SaveableEntity identity, not the derived one
// (scene + hierarchy path + sibling index) — any edit to persistentScene's root order by anybody would
// otherwise orphan the fleet's saved poses and send the city back to its first waypoint.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using SpaceGame.Core.Persistence;
using SpaceGame.Vehicles;
using SpaceGame.World;
using SpaceGame.World.NavMeshTools;

namespace SpaceGame.EditorTools
{
    public static class SkyFleetPlacement
    {
        public const string ScenePath = "Assets/Game/Scenes/world/persistentScene.unity";
        public const string FlagshipName = "SkyCityFleet";
        public const string EscortPrefix = "SkyEscort";
        public const string FlagshipId = "sky-city-flagship";

        /// <summary>Height of the flagship's pivot (m). The plain is 100–130 m; see HighestGroundAlong.</summary>
        public const float CruiseAltitude = 280f;

        /// <summary>How far below the flagship's pivot the lowest hull can hang (m): the tug's keel plus its wander.</summary>
        public const float FleetDraught = 45f;

        /// <summary>How far to each side of a leg (m) the fleet reaches: the outermost escort plus its wander.</summary>
        public const float FleetHalfWidth = 160f;

        public static readonly Vector3[] Route =
        {
            new(3350f, CruiseAltitude, 1400f),   // ~470 m from the spawn; a new world starts here
            new(2550f, CruiseAltitude, 1400f),
            new(2550f, CruiseAltitude, 550f),
            new(3100f, CruiseAltitude, 300f),
        };

        public struct EscortSlot
        {
            public string Vessel;
            /// <summary>In the flagship's frame (m).</summary>
            public Vector3 Station;
            /// <summary>Radians per axis; different per escort so no two drift in step.</summary>
            public Vector3 WanderPhase;
        }

        // Round the flagship at several heights, clear of its sails and outriggers and of each other
        // by more than their wander. The first three are the original escorts' stations.
        public static readonly EscortSlot[] Escorts =
        {
            new() { Vessel = "SkyFreighter", Station = new Vector3(117f, 39f, -12f), WanderPhase = new Vector3(0.0f, 1.1f, 2.3f) },
            new() { Vessel = "SkySkiff", Station = new Vector3(-120f, 12f, 57f), WanderPhase = new Vector3(2.9f, 0.4f, 1.7f) },
            new() { Vessel = "SkyTug", Station = new Vector3(-117f, -21f, -60f), WanderPhase = new Vector3(1.5f, 2.6f, 0.2f) },
            new() { Vessel = "SkySkiff", Station = new Vector3(112f, 18f, 92f), WanderPhase = new Vector3(0.8f, 3.0f, 2.0f) },
            new() { Vessel = "SkyTug", Station = new Vector3(110f, -18f, -110f), WanderPhase = new Vector3(2.2f, 1.9f, 0.9f) },
            new() { Vessel = "SkyFreighter", Station = new Vector3(-25f, 72f, 165f), WanderPhase = new Vector3(3.4f, 0.7f, 2.8f) },
            new() { Vessel = "SkySkiff", Station = new Vector3(20f, 40f, -170f), WanderPhase = new Vector3(1.2f, 2.4f, 3.3f) },
        };

        public static string EscortName(int index) => $"{EscortPrefix}{index}_{Escorts[index].Vessel}";
        public static string EscortId(int index) => $"sky-escort-{index}";

        [MenuItem("Tools/Environment/Place Sky Fleet In World")]
        public static void PlaceMenu() => Debug.Log(Place());

        [MenuItem("Tools/Environment/Place Sky Fleet In World", validate = true)]
        private static bool CanPlace() => !EditorApplication.isPlaying;

        /// <summary>Places and configures the fleet in persistentScene, saves it, and returns a report.</summary>
        public static string Place()
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            else if (scene.isDirty)
                return $"[SkyFleetPlacement] {ScenePath} is open with unsaved changes - save or discard them first.";

            try
            {
                GameObject flagship = PlaceFlagship(scene);
                for (int i = 0; i < Escorts.Length; i++)
                    PlaceEscort(scene, i, flagship.transform);
                int removed = RemoveSurplusEscorts(scene);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    return $"[SkyFleetPlacement] could not save {ScenePath}.";

                bool ok = Verify(scene, out string report);
                return $"[SkyFleetPlacement] {(ok ? "placed" : "FAILED")}: flagship at {Route[0]}, " +
                       $"{Escorts.Length} escorts{(removed > 0 ? $", {removed} surplus removed" : "")}\n{report}";
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static GameObject PlaceFlagship(Scene scene)
        {
            GameObject fleet = FindRoot(scene, FlagshipName) ?? Instantiate(SkyFleetBuilder.FleetPrefabPath, scene);
            fleet.name = FlagshipName;

            Vector3 firstLeg = Route[1] - Route[0];
            firstLeg.y = 0f;
            fleet.transform.SetPositionAndRotation(Route[0], Quaternion.LookRotation(firstLeg));

            var route = fleet.GetComponent<DriftRouteModule>();
            SerializedFields.Edit(route, so =>
            {
                SerializedProperty list = so.FindProperty("route");
                list.arraySize = Route.Length;
                for (int i = 0; i < Route.Length; i++)
                    list.GetArrayElementAtIndex(i).vector3Value = Route[i];
            });

            BakeIdentity(fleet, FlagshipId);
            Record(fleet.transform, route);
            DropStaleOverrides(fleet);
            return fleet;
        }

        private static void PlaceEscort(Scene scene, int index, Transform flagship)
        {
            EscortSlot slot = Escorts[index];
            string prefabPath = SkyFleetBuilder.Vessels.First(v => v.Name == slot.Vessel).PrefabPath;
            string name = EscortName(index);

            GameObject escort = FindRoot(scene, name);
            if (escort != null && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(escort) != prefabPath)
            {
                Object.DestroyImmediate(escort);
                escort = null;
            }

            escort ??= Instantiate(prefabPath, scene);
            escort.name = name;
            escort.transform.SetPositionAndRotation(flagship.TransformPoint(slot.Station), flagship.rotation);

            var module = escort.GetComponent<FleetEscortModule>();
            SerializedFields.Edit(module, so =>
            {
                SerializedFields.Set(so, "flagship", flagship);
                SerializedFields.SetVector3(so, "station", slot.Station);
                SerializedFields.SetVector3(so, "wanderPhase", slot.WanderPhase);
            });

            BakeIdentity(escort, EscortId(index));
            Record(escort.transform, module);
            DropStaleOverrides(escort);
        }

        private static int RemoveSurplusEscorts(Scene scene)
        {
            var wanted = new HashSet<string>(Enumerable.Range(0, Escorts.Length).Select(EscortName));
            GameObject[] surplus = scene.GetRootGameObjects()
                .Where(go => go.name.StartsWith(EscortPrefix) && !wanted.Contains(go.name)).ToArray();
            foreach (GameObject go in surplus)
                Object.DestroyImmediate(go);
            return surplus.Length;
        }

        private static GameObject FindRoot(Scene scene, string name) =>
            scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);

        private static GameObject Instantiate(string prefabPath, Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new System.InvalidOperationException($"No {prefabPath}. Run Tools > Environment > Build Sky Fleet Prefabs.");
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        }

        private static void BakeIdentity(GameObject go, string id)
        {
            var entity = go.GetComponent<SaveableEntity>();
            if (entity == null)
                throw new System.InvalidOperationException($"{go.name} has no SaveableEntity - rebuild the fleet prefabs.");

            SerializedFields.Edit(entity, so =>
            {
                SerializedFields.SetString(so, "instanceId", id);
                SerializedFields.SetBool(so, "authored", true);
            });
            Record(entity);
        }

        // A rebuild of the fleet prefabs re-adds components under new file ids, which leaves the old
        // overrides pointing at nothing in the scene file: dead weight that also no longer applies.
        private static void DropStaleOverrides(GameObject instance) =>
            PrefabUtility.RemoveUnusedOverrides(new[] { instance }, InteractionMode.AutomatedAction);

        private static void Record(params Object[] modified)
        {
            foreach (Object o in modified)
                PrefabUtility.RecordPrefabInstancePropertyModifications(o);
        }

        /// <summary>
        /// The post-condition: one flagship sailing <see cref="Route"/> with a baked identity, and every
        /// escort in <see cref="Escorts"/> keeping its station on it.
        /// </summary>
        public static bool Verify(Scene scene, out string report)
        {
            var sb = new System.Text.StringBuilder();
            GameObject fleet = FindRoot(scene, FlagshipName);
            if (fleet == null)
            {
                report = $"  no {FlagshipName} root in {scene.path}";
                return false;
            }

            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(fleet) != SkyFleetBuilder.FleetPrefabPath)
                sb.AppendLine($"  {FlagshipName} is not an instance of {SkyFleetBuilder.FleetPrefabPath}");

            var route = fleet.GetComponent<DriftRouteModule>();
            if (route == null || !route.Route.SequenceEqual(Route))
                sb.AppendLine($"  {FlagshipName}'s DriftRouteModule does not sail the {Route.Length}-waypoint route");

            CheckIdentity(fleet, FlagshipId, sb);

            for (int i = 0; i < Escorts.Length; i++)
            {
                GameObject escort = FindRoot(scene, EscortName(i));
                if (escort == null)
                {
                    sb.AppendLine($"  no {EscortName(i)}");
                    continue;
                }

                var module = escort.GetComponent<FleetEscortModule>();
                if (module == null || module.Flagship != fleet.transform)
                    sb.AppendLine($"  {EscortName(i)} does not keep station on {FlagshipName}");
                else if (module.Station != Escorts[i].Station)
                    sb.AppendLine($"  {EscortName(i)} keeps station at {module.Station}, not {Escorts[i].Station}");

                CheckIdentity(escort, EscortId(i), sb);
            }

            bool ok = sb.Length == 0;
            report = ok ? $"  {FlagshipName} sails {Route.Length} waypoints at {CruiseAltitude} m with {Escorts.Length} escorts" : sb.ToString();
            return ok;
        }

        private static void CheckIdentity(GameObject go, string id, System.Text.StringBuilder sb)
        {
            var entity = go.GetComponent<SaveableEntity>();
            if (entity == null || !entity.IsAuthored || entity.InstanceId != id)
                sb.AppendLine($"  {go.name} has no baked identity '{id}'");
        }

        /// <summary>
        /// The highest walkable ground (m) under a corridor <paramref name="halfWidth"/> to each side of
        /// the leg <paramref name="from"/>→<paramref name="to"/>, sampled every <paramref name="spacing"/>
        /// metres from the world NavMesh bake — the one height map of the whole streamed world that
        /// exists without loading a chunk. NaN when nothing under the corridor is walkable.
        /// </summary>
        public static float HighestGroundAlong(Vector3 from, Vector3 to, float halfWidth, float spacing)
        {
            var world = AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(WorldNavMeshBaker.AssetPath);
            if (world == null || world.bakedData == null)
                throw new System.InvalidOperationException($"No baked {WorldNavMeshBaker.AssetPath}.");

            NavMeshDataInstance instance = NavMesh.AddNavMeshData(world.bakedData);
            try
            {
                Vector3 along = to - from;
                along.y = 0f;
                Vector3 across = Vector3.Cross(Vector3.up, along.normalized);
                int steps = Mathf.CeilToInt(along.magnitude / spacing);
                int sideSteps = Mathf.CeilToInt(halfWidth / spacing);
                float highest = float.NaN;

                for (int i = 0; i <= steps; i++)
                {
                    Vector3 centre = from + along * (i / (float)steps);
                    for (int j = -sideSteps; j <= sideSteps; j++)
                    {
                        Vector3 probe = centre + across * (j * spacing);
                        probe.y = from.y + 200f;
                        // Nearest walkable point to a spot high overhead: the top of whatever is below.
                        if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, 600f, NavMesh.AllAreas)) continue;
                        if (Vector2.Distance(new Vector2(hit.position.x, hit.position.z), new Vector2(probe.x, probe.z)) > spacing) continue;
                        highest = float.IsNaN(highest) ? hit.position.y : Mathf.Max(highest, hit.position.y);
                    }
                }

                return highest;
            }
            finally
            {
                instance.Remove();
            }
        }
    }
}
