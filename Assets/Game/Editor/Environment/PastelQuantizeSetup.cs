// Puts the pastel quantize filter on the pipeline, once.
//
// Same story as VolumetricSetup: the renderer asset keeps a feature list and a parallel
// index map, and growing one without the other yields a feature that exists and never
// runs — so registration goes through the shared SerializedObject helpers, not YAML.
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools.Environment
{
    public static class PastelQuantizeSetup
    {
        private const string MaterialPath = "Assets/Game/Art/Materials/Environment/PastelQuantize.mat";
        private const string ToggleMenuPath = "SpaceGame/Environment/Pastel Quantize Filter";

        // One-click on/off, checkmark shows the current state. Works during play mode too —
        // URP reads the feature's active flag every frame it rebuilds the renderer.
        [MenuItem(ToggleMenuPath)]
        public static void Toggle()
        {
            bool enable = !AnyActive();
            int touched = 0;

            foreach (var renderer in VolumetricSetup.FindRenderers())
            {
                foreach (var feature in renderer.rendererFeatures)
                {
                    if (feature is PastelQuantizeRenderFeature)
                    {
                        feature.SetActive(enable);
                        EditorUtility.SetDirty(feature);
                        EditorUtility.SetDirty(renderer);
                        touched++;
                    }
                }
            }

            if (touched == 0)
            {
                Debug.LogWarning("[PastelQuantize] Not installed; run " +
                                 "SpaceGame ▸ Environment ▸ Install Pastel Quantize Filter first.");
                return;
            }

            AssetDatabase.SaveAssets();
            Menu.SetChecked(ToggleMenuPath, enable);
            Debug.Log($"[PastelQuantize] {(enable ? "Enabled" : "Disabled")} on {touched} renderer feature(s).");
        }

        [MenuItem(ToggleMenuPath, true)]
        public static bool ToggleValidate()
        {
            Menu.SetChecked(ToggleMenuPath, AnyActive());
            return true;
        }

        private static bool AnyActive()
        {
            foreach (var renderer in VolumetricSetup.FindRenderers())
            {
                foreach (var feature in renderer.rendererFeatures)
                {
                    if (feature is PastelQuantizeRenderFeature && feature.isActive)
                        return true;
                }
            }

            return false;
        }

        [MenuItem("SpaceGame/Environment/Install Pastel Quantize Filter")]
        public static void Install()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Debug.LogError("[PastelQuantize] Missing " + MaterialPath +
                               ". It ships with the feature; restore it before installing.");
                return;
            }

            var renderers = VolumetricSetup.FindRenderers();
            if (renderers.Count == 0)
            {
                Debug.LogError("[PastelQuantize] No UniversalRendererData assets found in the project.");
                return;
            }

            int added = 0;
            foreach (var renderer in renderers)
            {
                if (VolumetricSetup.AddFeature<PastelQuantizeRenderFeature>(renderer, out var feature))
                {
                    feature.settings.material = material;
                    // Off by default — the look is opt-in via the toggle menu item.
                    feature.SetActive(false);
                    added++;
                }

                EditorUtility.SetDirty(renderer);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[PastelQuantize] Installed {added} render feature(s) across {renderers.Count} " +
                      "renderer(s). Toggle or tune it on the renderer asset.");
        }
    }
}
