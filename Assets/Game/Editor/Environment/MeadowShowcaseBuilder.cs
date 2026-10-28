using System.IO;
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceGame.EditorTools.Environment
{
    /// <summary>
    /// Builds the meadow look-dev scene: a terrain with a pond, the stylized grass growing on it,
    /// SpaceGame/StylizedWater in the pond, and a soft warm sky. The grass is the shared GrassField
    /// prefab growing on the shared meadow ground layer, exactly as on a real world.
    ///
    /// The scene is built additively and closed again, so whatever the user has open is never
    /// touched. Rebuilding overwrites the showcase's own assets and nothing else.
    /// </summary>
    internal static class MeadowShowcaseBuilder
    {
        private const string ScenePath = "Assets/Game/Scenes/Tests/MeadowShowcase.unity";
        private const string TerrainFolder = "Assets/Game/Terrain/MeadowShowcase";
        private const string MaterialFolder = "Assets/Game/Art/Materials/Environment/Meadow";
        private const string GrassFieldPrefab = "Assets/Game/Prefabs/Environment/Nature/GrassField.prefab";
        private const string MeadowGroundLayer = "Assets/Game/Terrain/GrassBiomes/Ground_Meadow.terrainlayer";
        private const string PropFolder = "Assets/Game/Prefabs/Environment/";
        private const string ParticleMaterial = "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat";

        private const string WaterPrefab = "Assets/Game/Prefabs/Environment/Water/Water.prefab";

        // The pond's surface square; it runs under the shore all round.
        private const float PondSize = 60f;

        // Bubbles rise at most 0.8 m/s for 3 s, so they start this far down to vanish below the surface.
        private const float BubbleStartDepth = 2.6f;

        private static readonly Color SandGround = new Color(0.86f, 0.76f, 0.55f);

        [MenuItem("SpaceGame/Environment/Build Meadow Showcase")]
        private static void Build()
        {
            foreach (string folder in new[] { TerrainFolder, MaterialFolder })
                EnsureFolder(folder);

            // Unity refuses an additive scene beside an untitled one, and the showcase cannot be
            // saved over while it is open beside others — so a lone, clean untitled scene or the
            // showcase itself is replaced, and anything else the user has open is left alone.
            Scene previous = SceneManager.GetActiveScene();
            bool replace = SceneManager.sceneCount == 1 && !previous.isDirty
                        && (string.IsNullOrEmpty(previous.path) || previous.path == ScenePath);
            if (!replace && (string.IsNullOrEmpty(previous.path) || SceneManager.GetSceneByPath(ScenePath).IsValid()))
            {
                Debug.LogError("[MeadowShowcase] Save or close the untitled scene / the open showcase first.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                                                      replace ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            Terrain terrain = CreateTerrain();
            CreateMeadow();
            CreateWater();
            MeadowShowcaseDressing.Place(terrain, PropFolder);
            MeadowShowcaseDressing.LightAndSky(terrain, MaterialFolder);

            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!replace)
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[MeadowShowcase] Built {ScenePath}. Open it and look through the Scene view or the Main Camera.");
        }

        private static Terrain CreateTerrain()
        {
            // The shared meadow biome's ground, so the GrassField prefab grows meadow on it.
            var grass = AssetDatabase.LoadAssetAtPath<TerrainLayer>(MeadowGroundLayer);
            TerrainLayer sand = CreateLayer("MeadowShowcase_Sand", SandGround, 0.04f);
            TerrainData data = MeadowShowcaseTerrain.Create(TerrainFolder, grass, sand);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Meadow Terrain";
            float half = MeadowShowcaseTerrain.Size * 0.5f;
            go.transform.position = new Vector3(-half, 0f, -half);

            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = SaveMaterial(new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit")),
                                                    "Mat_MeadowTerrain");
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 400f;
            return terrain;
        }

        /// <summary>The shared grass prefab, kept out of the pond.</summary>
        private static void CreateMeadow()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GrassFieldPrefab);
            var meadow = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var serializedField = new SerializedObject(meadow.GetComponent<GrassField>());
            serializedField.FindProperty("minimumHeight").floatValue = MeadowShowcaseTerrain.WaterLevel + 0.05f;
            serializedField.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The shared Water prefab, sized to the pond; its reflection volume brings the manager.</summary>
        private static void CreateWater()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterPrefab);
            var water = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            water.name = "Pond Water";
            Vector2 centre = MeadowShowcaseTerrain.PondCentre;
            water.transform.position = new Vector3(centre.x, MeadowShowcaseTerrain.WaterLevel, centre.y);
            var serializedWater = new SerializedObject(water.GetComponent<WaterBody>());
            serializedWater.FindProperty("size").vector2Value = new Vector2(PondSize, PondSize);
            serializedWater.ApplyModifiedPropertiesWithoutUndo();
            CreateBubbles(water.transform.position);
        }

        /// <summary>Bubbles drifting up from the pond bed, stopping short of the surface.</summary>
        private static void CreateBubbles(Vector3 surfaceCentre)
        {
            var bubbles = new GameObject("Pond Bubbles");
            bubbles.transform.SetPositionAndRotation(surfaceCentre + Vector3.down * BubbleStartDepth,
                                                     Quaternion.Euler(-90f, 0f, 0f));
            var system = bubbles.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
            main.startColor = new Color(0.85f, 0.95f, 1f, 0.8f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 25f;
            ParticleSystem.ShapeModule shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(20f, 20f, 0.5f);
            ParticleSystem.NoiseModule noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.6f;

            var particleMaterial = AssetDatabase.LoadAssetAtPath<Material>(ParticleMaterial);
            if (particleMaterial == null)
                Debug.LogError($"[MeadowShowcase] {ParticleMaterial} is missing; the bubbles render pink.");
            bubbles.GetComponent<ParticleSystemRenderer>().sharedMaterial = particleMaterial;
        }


        private static TerrainLayer CreateLayer(string name, Color colour, float mottling)
        {
            Texture2D texture = MeadowShowcaseDressing.MottledTexture(TerrainFolder + "/" + name + ".png", colour, mottling);
            // Fully matte, so the ground shades like the blades instead of picking up a sheen.
            var layer = new TerrainLayer
            {
                diffuseTexture = texture,
                tileSize = new Vector2(6f, 6f),
                smoothness = 0f,
                metallic = 0f,
            };
            string path = TerrainFolder + "/" + name + ".terrainlayer";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(layer, path);
            return layer;
        }

        internal static Material SaveMaterial(Material material, string name)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
        }
    }
}
