// Puts the underwater view on the pipeline.
//
// Same registration path as PastelQuantizeSetup and VolumetricSetup: the renderer asset keeps
// a feature list and a parallel index map, and growing one without the other yields a feature
// that exists and never runs — so it goes through VolumetricSetup.AddFeature, not YAML.
// Installed active: the pass only runs for a camera inside an UnderwaterVolume.
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Environment
{
    public static class UnderwaterSetup
    {
        private const string MaterialPath = "Assets/Game/Art/Materials/Environment/UnderwaterView.mat";
        private const string CausticsPath = "Assets/ThirdParty/UberStylizedWater/Textures/Caustic 1.png";

        [MenuItem("SpaceGame/Environment/Install Underwater View")]
        public static void Install()
        {
            Material material = LoadOrCreateMaterial();
            if (material == null)
                return;

            var renderers = VolumetricSetup.FindRenderers();
            if (renderers.Count == 0)
            {
                Debug.LogError("[Underwater] No UniversalRendererData assets found in the project.");
                return;
            }

            int added = 0;
            foreach (var renderer in renderers)
            {
                if (VolumetricSetup.AddFeature<UnderwaterRenderFeature>(renderer, out var feature))
                    added++;
                feature.settings.material = material;
                EditorUtility.SetDirty(feature);
                EditorUtility.SetDirty(renderer);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Underwater] Installed {added} render feature(s) across {renderers.Count} renderer(s).");
        }

        private static Material LoadOrCreateMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;

            Shader shader = Shader.Find("Hidden/UnderwaterView");
            if (shader == null)
            {
                Debug.LogError("[Underwater] Hidden/UnderwaterView did not compile; fix it before installing.");
                return null;
            }

            material = new Material(shader);
            var caustics = AssetDatabase.LoadAssetAtPath<Texture2D>(CausticsPath);
            if (caustics == null)
                Debug.LogError($"[Underwater] {CausticsPath} is missing; the view will have no caustics.");
            material.SetTexture("_CausticsMap", caustics);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }
    }
}
