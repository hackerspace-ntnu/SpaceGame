// Builds Assets/Game/Scenes/Tests/WorkshopTest.unity: the workshop on a flat ground, settled by four Shellbacks,
// with the offline player standing in the ship lane before the main room. A place to walk the building,
// work its machinery and watch its people, not a level: nothing here is streamed, and a scene opened directly cannot
// save (docs/AI/systems/Scenes.md).
//
// The workshop is placed the way it would be in the world, by an Outpost that generates it and its residents
// (docs/AI/systems/Outposts.md). What the world's own NavMesh bake would give a chunk scene, this scene gets from a
// bake of its own, kept beside it and added by a StaticNavMeshData.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using SpaceGame.Agents.Residents;
using SpaceGame.Gameplay;
using SpaceGame.World;

namespace SpaceGame.EditorTools
{
    public static class WorkshopTestSceneBuilder
    {
        public const string ScenePath = "Assets/Game/Scenes/Tests/WorkshopTest.unity";
        public const string NavMeshPath = "Assets/Game/Scenes/Tests/WorkshopTest_NavMesh.asset";
        private const string PlayerPath = "Assets/Game/Prefabs/Characters/Player/PlayerCharacter.prefab";
        private const string WorldNavMeshPath = "Assets/Game/Settings/WorldNavMesh.asset";

        // In the workshop's own space: the main room's pit-floor centre is the origin. The pit opens toward +X (the
        // ship tunnel), and the lane there, outside the main room's turning wall, is where a visitor first stands.
        private static readonly Vector3 PlayerStart = new(24f, 1.1f, 0f);

        // The outpost stands its building on the ground under it, so the ground is what the pit floor is level with.
        // It is drawn a little lower than it is walked on: the pit floor lies on it, and two faces at one height flicker.
        private const float GroundSize = 700f;
        private const float GroundThickness = 1f;
        private const float GroundDrawnBelow = 0.06f;
        private const float AirPadding = 20f;
        private static readonly Color Ambient = new(0.42f, 0.36f, 0.31f);
        private static readonly Color SunColour = new(1f, 0.93f, 0.82f);
        private const float SunIntensity = 1.1f;
        private const float OpeningHour = 9.5f;

        [MenuItem("Tools/SpaceGame/Workshop/Rebuild Workshop Test Scene")]
        private static void RebuildMenu() => Debug.Log("[Workshop] " + Rebuild());

        /// <summary>Write the scene beside whatever is open, without disturbing it. Returns what was read back.</summary>
        public static string Rebuild()
        {
            var outpostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkshopOutpostBuilder.PrefabPath);
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            if (outpostPrefab == null || player == null)
                return "missing the workshop outpost or the player prefab: Rebuild Workshop Outpost Prefab";
            if (SceneManager.GetSceneByPath(ScenePath).isLoaded) return $"close {ScenePath} first: it is rebuilt from nothing";

            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            string settled, baked;
            try
            {
                SceneManager.SetActiveScene(scene);

                AddGround();
                // The prefab a chunk scene gets, placed the same way, so this scene proves what the world will hold.
                Outpost outpost = ((GameObject)PrefabUtility.InstantiatePrefab(outpostPrefab)).GetComponent<Outpost>();
                SettlementEditor.Generate(outpost);
                settled = Settled(outpost);

                Transform building = outpost.GeneratedRoot != null ? outpost.GeneratedRoot.GetComponentInChildren<Workshop>(true)?.transform : null;
                if (building == null) return "the outpost generated no workshop: read the console";

                Place(player, building.TransformPoint(PlayerStart), building.rotation * Quaternion.LookRotation(Vector3.left, Vector3.up));
                Light sun = AddSun();

                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Ambient;
                RenderSettings.sun = sun;
                RenderSettings.fog = false;

                AddAir(BoundsOf(building));
                baked = BakeNavMesh(scene);

                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? ".");
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) return "the scene could not be saved";
            }
            finally
            {
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }

            return $"{ScenePath} written; {settled}; {baked}; {AddToBuild()}";
        }

        // A collider the outpost finds the ground by and everybody walks on, and under it the slab that is drawn.
        private static void AddGround()
        {
            var ground = new GameObject("Ground") { isStatic = true };
            BoxCollider floor = ground.AddComponent<BoxCollider>();
            floor.size = new Vector3(GroundSize, GroundThickness, GroundSize);
            floor.center = new Vector3(0f, -GroundThickness * 0.5f, 0f);

            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = "Slab";
            Object.DestroyImmediate(slab.GetComponent<Collider>());
            slab.transform.SetParent(ground.transform, false);
            slab.transform.localPosition = new Vector3(0f, -GroundDrawnBelow - (GroundThickness * 0.5f), 0f);
            slab.transform.localScale = new Vector3(GroundSize, GroundThickness, GroundSize);
            slab.isStatic = true;
        }

        /// <summary>Who moved in and as what, read back off the generated residents.</summary>
        private static string Settled(Outpost outpost)
        {
            Transform generated = outpost.GeneratedRoot;
            if (generated == null) return "nobody settled: nothing was generated";

            var roles = new List<string>();
            foreach (Resident resident in generated.GetComponentsInChildren<Resident>(true))
                roles.Add($"{resident.displayName} ({(resident.archetype != null ? resident.archetype.roleName : "no role")})");
            return $"{roles.Count}/{WorkshopOutpostBuilder.ResidentCount} residents: {string.Join(", ", roles)}";
        }

        private static void Place(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetPositionAndRotation(position, rotation);
        }

        private static Bounds BoundsOf(Transform building)
        {
            var bounds = new Bounds(building.position, Vector3.one);
            foreach (Renderer renderer in building.GetComponentsInChildren<Renderer>(true))
                if (renderer is not ParticleSystemRenderer) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        /// <summary>
        /// The scene's own NavMesh, with the world bake's settings and its rules for what is ground
        /// (docs/AI/systems/NavMeshSystem.md), written beside the scene and added in play by a StaticNavMeshData.
        /// </summary>
        private static string BakeNavMesh(Scene scene)
        {
            var world = AssetDatabase.LoadAssetAtPath<WorldNavMeshAsset>(WorldNavMeshPath);
            if (world == null) return $"NO NAVMESH: {WorldNavMeshPath} is the settings source and is missing";

            Physics.SyncTransforms();
            LayerMask mask = world.settings.layerMask;
            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Collider collider in root.GetComponentsInChildren<Collider>())
                {
                    if (!NavMeshSources.IsBakeable(collider, mask)) continue;
                    if (!NavMeshSources.TryColliderToSource(collider, out NavMeshBuildSource source)) continue;

                    if (sources.Count == 0) bounds = collider.bounds;
                    else bounds.Encapsulate(collider.bounds);
                    sources.Add(source);
                }
            }
            if (sources.Count == 0) return "NO NAVMESH: the scene has nothing to walk on";

            NavMeshBuildSettings settings = world.settings.ToBuildSettings();
            var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(NavMeshPath);
            if (data == null)
            {
                data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
                AssetDatabase.CreateAsset(data, NavMeshPath);
            }
            else
            {
                // Into the asset that is already there, so its GUID and the scene's reference to it survive.
                NavMeshBuilder.UpdateNavMeshData(data, settings, sources, bounds);
                EditorUtility.SetDirty(data);
            }

            new GameObject("NavMesh").AddComponent<StaticNavMeshData>().Configure(data);
            return $"NavMesh baked from {sources.Count} colliders";
        }

        private static Light AddSun()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = SunColour;
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;

            // The residents' day runs off the scene's clock, and a scene without one stands at midnight for good:
            // everybody would be found asleep. It opens in mid-morning, with people at their work.
            sun.gameObject.AddComponent<DayNightCycle>().startTimeOfDay = OpeningHour / DayNightCycle.HoursPerDay;
            return sun;
        }

        // The whole mountain is breathable here: this scene is for walking the workshop, not for running out of air.
        private static void AddAir(Bounds bounds)
        {
            var air = new GameObject("BreathableAir");
            air.transform.position = bounds.center;

            BoxCollider box = air.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = bounds.size + (Vector3.one * AirPadding);
            air.AddComponent<BreathableVolume>();
        }

        /// <summary>Appended, never inserted: indices 0 and 1 are the boot scenes and runtime loads go by index.</summary>
        private static string AddToBuild()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (EditorBuildSettingsScene entry in scenes)
                if (entry.path == ScenePath) return "already in the build list";

            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return $"appended to the build list at index {scenes.Count - 1}";
        }
    }
}
