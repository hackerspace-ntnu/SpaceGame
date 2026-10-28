using System.IO;
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceGame.EditorTools.Environment
{
    /// <summary>
    /// Everything in the meadow showcase that is not grass or water: a few existing rocks, the
    /// stylized trees and bushes, a draggable grass bender, and the soft warm light that makes the look
    /// read as cozy rather than as a desert at noon.
    /// </summary>
    internal static class MeadowShowcaseDressing
    {
        private static readonly Color SkyAmbient = new Color(0.66f, 0.78f, 0.95f);
        private static readonly Color EquatorAmbient = new Color(0.62f, 0.72f, 0.55f);
        private static readonly Color GroundAmbient = new Color(0.34f, 0.36f, 0.24f);
        private static readonly Color Haze = new Color(0.74f, 0.85f, 0.95f);
        private const string SkyMaterial = "Assets/ThirdParty/UberStylizedWater/Sky/SKy 22.mat";

        // floats = true sits the prop on the pond surface instead of the ground.
        private static readonly (string prefab, Vector2 xz, float yaw, float scale, bool floats)[] Props =
        {
            ("Decorations/Nature/Deco_Boulder_Large", new Vector2(-19f, 2f), 30f, 1f, false),
            ("Decorations/Nature/Deco_RockCluster_Basalt", new Vector2(25f, 16f), 200f, 1f, false),
            ("Decorations/Nature/Deco_RockCluster_Basalt", new Vector2(-9f, 33f), 160f, 1.2f, false),
            ("Decorations/Nature/Deco_Plant_Reeds", new Vector2(-15f, 9f), 0f, 1f, false),
            ("Decorations/Nature/Deco_Plant_Reeds", new Vector2(-12f, 15f), 70f, 1.1f, false),
            ("Decorations/Nature/Deco_Plant_Reeds", new Vector2(23f, 6f), 140f, 1f, false),
            ("Decorations/Gardens/Deco_Pond_Lily", new Vector2(-6f, 5f), 0f, 1f, true),
            ("Decorations/Gardens/Deco_Pond_Lily", new Vector2(9f, 17f), 120f, 1f, true),
            ("Decorations/Gardens/Deco_Pond_Lily", new Vector2(2f, -2f), 240f, 0.8f, true),
            ("Nature/Vegetation/Tree_Broadleaf_A", new Vector2(-24f, 40f), 0f, 1f, false),
            ("Nature/Vegetation/Tree_Broadleaf_B", new Vector2(14f, 48f), 90f, 1.1f, false),
            ("Nature/Vegetation/Tree_Broadleaf_C", new Vector2(38f, 28f), 200f, 1f, false),
            ("Nature/Vegetation/Tree_Spire_A", new Vector2(-34f, 24f), 0f, 1f, false),
            ("Nature/Vegetation/Tree_Spire_B", new Vector2(-30f, 31f), 0f, 0.9f, false),
            ("Nature/Vegetation/Tree_Umbrella_A", new Vector2(46f, 50f), 30f, 1f, false),
            ("Nature/Vegetation/Tree_Broadleaf_B", new Vector2(-40f, 46f), 250f, 0.9f, false),
            ("Nature/Vegetation/Tree_Spire_A", new Vector2(26f, 44f), 0f, 0.85f, false),
            ("Nature/Vegetation/Bush_Round_A", new Vector2(-20f, -8f), 0f, 1f, false),
            ("Nature/Vegetation/Bush_Round_B", new Vector2(-31f, 6f), 60f, 1.1f, false),
            ("Nature/Vegetation/Bush_Round_C", new Vector2(18f, -12f), 0f, 1f, false),
            ("Nature/Vegetation/Bush_Round_A", new Vector2(31f, 14f), 120f, 0.9f, false),
            ("Nature/Vegetation/Bush_Round_C", new Vector2(-17f, 31f), 200f, 1.1f, false),
            ("Nature/Vegetation/Bush_Spiky_A", new Vector2(-9f, -19f), 0f, 1f, false),
            ("Nature/Vegetation/Bush_Spiky_B", new Vector2(11f, -21f), 40f, 1f, false),
            ("Decorations/Gardens/Deco_FungusLog", new Vector2(-14f, -16f), 65f, 1f, false),
            ("Decorations/Ways/Deco_SteppingLogs", new Vector2(6f, -14f), 10f, 1f, false),
            ("Decorations/Structures/Deco_Fence_Straight", new Vector2(-28f, -22f), 20f, 1f, false),
            ("Decorations/Structures/Deco_Fence_Straight", new Vector2(-26.1f, -22.7f), 20f, 1f, false),
        };

        private static readonly Vector2 CameraXZ = new Vector2(-6f, -30f);
        private static readonly Vector2 BenderXZ = new Vector2(-3f, -24f);

        public static void Place(Terrain terrain, string propFolder)
        {
            var root = new GameObject("Props").transform;
            foreach ((string prefab, Vector2 xz, float yaw, float scale, bool floats) in Props)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(propFolder + prefab + ".prefab");
                if (asset == null)
                {
                    Debug.LogError($"[MeadowShowcase] Missing prop prefab {prefab}; skipped.");
                    continue;
                }
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                instance.transform.SetParent(root);
                Vector3 position = floats
                    ? new Vector3(xz.x, MeadowShowcaseTerrain.WaterLevel, xz.y)
                    : OnGround(terrain, xz, 0f);
                instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                instance.transform.localScale = Vector3.one * scale;
            }

            GameObject bender = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bender.name = "Grass Bender (drag me in the Scene view)";
            Object.DestroyImmediate(bender.GetComponent<Collider>());
            bender.transform.position = OnGround(terrain, BenderXZ, 0.35f);
            bender.transform.localScale = Vector3.one * 0.7f;
            bender.AddComponent<GrassBender>();
        }

        public static void LightAndSky(Terrain terrain, string materialFolder)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.82f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(38f, 150f, 0f);
            RenderSettings.sun = sun;

            // Uber Stylized Water's painted-cloud sky: the clouds are what the pond reflects.
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterial);
            if (RenderSettings.skybox == null)
                Debug.LogError($"[MeadowShowcase] {SkyMaterial} is missing; the sky is empty.");

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = SkyAmbient;
            RenderSettings.ambientEquatorColor = EquatorAmbient;
            RenderSettings.ambientGroundColor = GroundAmbient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Haze;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 320f;

            CreateGrade(materialFolder);
            CreateCamera(terrain);
        }

        /// <summary>A flat PNG of <paramref name="colour"/> with soft value-noise mottling.</summary>
        public static Texture2D MottledTexture(string path, Color colour, float mottling)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.7f + Mathf.PerlinNoise(x * 0.31f, y * 0.31f) * 0.3f;
                    texture.SetPixel(x, y, colour * (1f + (n - 0.5f) * 2f * mottling));
                }
            }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void CreateGrade(string materialFolder)
        {
            string path = materialFolder + "/MeadowShowcase_Volume.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);
            var adjustments = profile.Add<ColorAdjustments>(true);
            adjustments.postExposure.Override(0.2f);
            adjustments.saturation.Override(14f);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.35f);
            bloom.threshold.Override(1.05f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.16f);
            foreach (VolumeComponent component in profile.components)
                AssetDatabase.AddObjectToAsset(component, profile);

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        private static void CreateCamera(Terrain terrain)
        {
            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.fieldOfView = 50f;
            cam.farClipPlane = 600f;
            var cameraData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            // Soft edges: thousands of thin blade silhouettes alias into glitter without it.
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;

            Vector3 eye = OnGround(terrain, CameraXZ, 2.2f);
            Vector2 pond = MeadowShowcaseTerrain.PondCentre;
            var target = new Vector3(pond.x, MeadowShowcaseTerrain.WaterLevel, pond.y);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        }

        private static Vector3 OnGround(Terrain terrain, Vector2 xz, float lift)
        {
            var point = new Vector3(xz.x, 0f, xz.y);
            point.y = terrain.SampleHeight(point) + terrain.transform.position.y + lift;
            return point;
        }
    }
}
