using System.Linq;
using SpaceGame.Characters;
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools.Environment
{
    /// <summary>
    /// Builds the ocean look-dev and performance scene: a 1.2 km seabed from a beach down to a vent basin
    /// (OceanShowcaseTerrain), the clear ocean water over all of it, the seabed grass painted by zone, every aquatic
    /// plant growing where it belongs (OceanShowcasePlanting), and a free-fly camera for play mode.
    ///
    /// Built additively and closed again, like the meadow showcase, so whatever is open is never touched.
    /// Rebuilding replaces the showcase scene and its own terrain asset, nothing else. The scene is added to
    /// the end of Build Settings, because Play outside the build list is sent to the main menu (Scenes.md).
    /// </summary>
    internal static class OceanShowcaseBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/Tests/OceanShowcase.unity";
        private const string TerrainFolder = "Assets/Game/Terrain/OceanShowcase";
        private const string MaterialFolder = "Assets/Game/Art/Materials/Environment/OceanShowcase";
        private const string GrassFieldPrefab = "Assets/Game/Prefabs/Environment/Nature/GrassField.prefab";
        private const string WaterPrefab = "Assets/Game/Prefabs/Environment/Water/Water_Ocean.prefab";
        private const string GroundDir = "Assets/Game/Terrain/GrassBiomes/";

        // In OceanShowcaseTerrain's layer order: sand, rock, dune grass, then the five seabed grasses.
        private static readonly string[] LayerPaths =
        {
            "Assets/Game/Terrain/RockGallery/RockGallery_Sand.terrainlayer",
            "Assets/Game/Terrain/NoiseTerrain/NoiseTerrain_Rock.terrainlayer",
            GroundDir + "Ground_DrySteppe.terrainlayer",
            GroundDir + "Ground_Seagrass.terrainlayer",
            GroundDir + "Ground_AlgaeTurf.terrainlayer",
            GroundDir + "Ground_RedFringe.terrainlayer",
            GroundDir + "Ground_KelpSprouts.terrainlayer",
            GroundDir + "Ground_VentFloc.terrainlayer",
        };

        // The water is tiled so the camera draws only the tiles in view: one 1.2 km surface at the 1 m cells
        // the 3.5–9 m swells need would be a single 1.4 M-vertex mesh, always drawn whole.
        private const int WaterTiles = 3;
        private const float WaterCell = 1f;
        // Deep enough to hold the basin: the underwater view switches on anywhere below a tile.
        private const float UnderwaterDepth = 75f;

        // Plants stop drawing past this; the underwater haze has hidden them well before it.
        private const float TreeDistance = 320f;

        // The fly camera: slow enough to look at a coral, Shift to cross the map.
        private const float FlySpeed = 8f;
        private const float FlySprint = 5f;
        private static readonly Vector3 CameraStart = new Vector3(-40f, 2.5f, 470f);
        private static readonly Vector3 CameraLook = new Vector3(-40f, -6f, 330f);

        [MenuItem("SpaceGame/Environment/Build Ocean Showcase")]
        private static void Build()
        {
            foreach (string folder in new[] { TerrainFolder, MaterialFolder })
                MeadowShowcaseBuilder.EnsureFolder(folder);

            // As the meadow showcase: replace a lone clean untitled scene, else build beside what is open.
            Scene previous = SceneManager.GetActiveScene();
            bool replace = SceneManager.sceneCount == 1 && !previous.isDirty
                        && (string.IsNullOrEmpty(previous.path) || previous.path == ScenePath);
            if (!replace && (string.IsNullOrEmpty(previous.path) || SceneManager.GetSceneByPath(ScenePath).IsValid()))
            {
                Debug.LogError("[OceanShowcase] Save or close the untitled scene / the open showcase first.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, replace ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            Terrain terrain = CreateTerrain();
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GrassFieldPrefab), scene);
            CreateWater();
            var drift = new GameObject("Surface Drift").transform;
            int plants = OceanShowcasePlanting.Plant(terrain, drift);
            MeadowShowcaseDressing.LightAndSky(terrain, MaterialFolder);
            SetUpCamera(scene);

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!replace)
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[OceanShowcase] Built {ScenePath} with {plants} plants. Open it and press Play to fly: " +
                      "WASD, Space / Ctrl up and down, Shift faster.");
        }

        private static Terrain CreateTerrain()
        {
            TerrainLayer[] layers = LayerPaths.Select(AssetDatabase.LoadAssetAtPath<TerrainLayer>).ToArray();
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] == null)
                    Debug.LogError($"[OceanShowcase] Missing terrain layer {LayerPaths[i]}.");
            TerrainData data = OceanShowcaseTerrain.Create(TerrainFolder, layers);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Seabed";
            float half = OceanShowcaseTerrain.Size * 0.5f;
            go.transform.position = new Vector3(-half, OceanShowcaseTerrain.Bottom, -half);
            var terrain = go.GetComponent<Terrain>();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 400f;
            terrain.treeDistance = TreeDistance;
            // LODGroup prefabs have no billboards; past this distance they would simply vanish.
            terrain.treeBillboardDistance = TreeDistance;
            return terrain;
        }

        private static void CreateWater()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterPrefab);
            float tile = OceanShowcaseTerrain.Size / WaterTiles;
            var parent = new GameObject("Ocean").transform;
            for (int tz = 0; tz < WaterTiles; tz++)
            {
                for (int tx = 0; tx < WaterTiles; tx++)
                {
                    var water = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    water.name = $"Ocean Water {tx}-{tz}";
                    water.transform.SetParent(parent, false);
                    float start = -OceanShowcaseTerrain.Size * 0.5f + tile * 0.5f;
                    water.transform.position = new Vector3(start + tx * tile, 0f, start + tz * tile);
                    var body = new SerializedObject(water.GetComponent<WaterBody>());
                    body.FindProperty("size").vector2Value = new Vector2(tile, tile);
                    body.FindProperty("cellSize").floatValue = WaterCell;
                    body.ApplyModifiedPropertiesWithoutUndo();
                    var volume = new SerializedObject(water.GetComponent<UnderwaterVolume>());
                    volume.FindProperty("depth").floatValue = UnderwaterDepth;
                    volume.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        /// <summary>LightAndSky's camera, moved to the beach and given the free-fly controller.</summary>
        private static void SetUpCamera(Scene scene)
        {
            // Found in the showcase itself: Camera.main could be the camera of a scene open beside it.
            Camera cam = scene.GetRootGameObjects().Select(go => go.GetComponent<Camera>()).First(c => c != null);
            cam.farClipPlane = 1500f;
            cam.transform.SetPositionAndRotation(CameraStart, Quaternion.LookRotation(CameraLook - CameraStart));
            var fly = new SerializedObject(cam.gameObject.AddComponent<SpectatorCamera>());
            fly.FindProperty("moveSpeed").floatValue = FlySpeed;
            fly.FindProperty("sprintMultiplier").floatValue = FlySprint;
            fly.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddToBuildSettings()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Any(s => s.path == ScenePath))
                return;
            EditorBuildSettings.scenes = scenes.Append(new EditorBuildSettingsScene(ScenePath, true)).ToArray();
            Debug.Log($"[OceanShowcase] Added {ScenePath} to the end of Build Settings so Play loads it.");
        }
    }
}
