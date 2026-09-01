// Puts the volumetric render features on the pipeline, once.
//
// Both features are inert until a renderer asset carries them, and a renderer asset is a binary-ish
// YAML file with a sub-asset per feature and a parallel index map — the kind of thing that is fine
// for Unity to write and a bad idea for anyone else to. So this asks Unity to do it, through the
// same SerializedObject dance URP's own inspector uses.
//
// Safe to run repeatedly: a renderer that already has a feature is left alone, so this is also the
// repair tool for a renderer someone has cleaned out by hand.
using System.Collections.Generic;
using SpaceGame.World;
using SpaceGame.World.Environment;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SpaceGame.EditorTools.Environment
{
    public static class VolumetricSetup
    {
        private const string FogMaterialPath = "Assets/Game/Art/Materials/Environment/VolumetricFog.mat";
        private const string CloudMaterialPath = "Assets/Game/Art/Materials/Environment/VolumetricClouds.mat";

        [MenuItem("SpaceGame/Environment/Install Volumetric Render Features")]
        public static void Install()
        {
            var fogMaterial = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
            var cloudMaterial = AssetDatabase.LoadAssetAtPath<Material>(CloudMaterialPath);

            if (fogMaterial == null || cloudMaterial == null)
            {
                Debug.LogError("[Volumetrics] Missing " + FogMaterialPath + " or " + CloudMaterialPath +
                               ". Both ship with the feature; restore them before installing.");
                return;
            }

            List<ScriptableRendererData> renderers = FindRenderers();
            if (renderers.Count == 0)
            {
                Debug.LogError("[Volumetrics] No UniversalRendererData assets found in the project.");
                return;
            }

            int added = 0;
            foreach (ScriptableRendererData renderer in renderers)
            {
                if (AddFeature<VolumetricCloudsRenderFeature>(renderer, out var clouds))
                {
                    clouds.settings.material = cloudMaterial;
                    added++;
                }

                // The fog is added second so it lands after the clouds in the list. Order in the
                // list does not decide execution order — the render pass event does — but keeping
                // the two agreeing makes the inspector readable.
                if (AddFeature<FogRenderFeature>(renderer, out var fog))
                {
                    fog.settings.material = fogMaterial;
                    added++;
                }

                EditorUtility.SetDirty(renderer);
            }

            EnsureDepthTexture();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Volumetrics] Installed {added} render feature(s) across {renderers.Count} " +
                      "renderer(s). Fog volumes and cloud layers will now render.");
        }

        /// <summary>
        /// The project's own renderers, and only those.
        ///
        /// <para>
        /// <c>FindAssets</c> also returns URP's built-in default renderer, which lives inside the
        /// package. Writing a feature into that appears to work and is worthless: the package cache
        /// is regenerated from the tarball, so the change is reverted the next time the package is
        /// resolved, it is not in version control, and no teammate ever receives it. Worse, it makes
        /// the project depend on an edit to a dependency.
        /// </para>
        /// </summary>
        private static List<ScriptableRendererData> FindRenderers()
        {
            var found = new List<ScriptableRendererData>();

            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", System.StringComparison.Ordinal))
                    continue;

                var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
                if (data != null)
                    found.Add(data);
            }

            return found;
        }

        /// <summary>
        /// Adds a feature to a renderer, or reports the one already there.
        ///
        /// <para>
        /// Written against the serialized properties rather than the <c>rendererFeatures</c> list
        /// because the list alone is half the state: URP keeps a parallel map of instance ids and
        /// rebuilds the renderer from it, so a feature pushed onto the list and saved comes back as
        /// a null row in the inspector.
        /// </para>
        /// </summary>
        private static bool AddFeature<T>(ScriptableRendererData renderer, out T feature)
            where T : ScriptableRendererFeature
        {
            foreach (ScriptableRendererFeature existing in renderer.rendererFeatures)
            {
                if (existing is T match)
                {
                    feature = match;
                    return false;
                }
            }

            feature = ScriptableObject.CreateInstance<T>();
            feature.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(feature, renderer);

            var serialized = new SerializedObject(renderer);
            SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
            SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");

            int index = features.arraySize;
            features.arraySize++;
            serialized.ApplyModifiedProperties();

            features.GetArrayElementAtIndex(index).objectReferenceValue = feature;

            // The map is what URP reads to rebuild the renderer. A renderer whose list has grown and
            // whose map has not gets a feature that exists in the asset and never runs.
            if (map != null && map.isArray)
            {
                map.arraySize = features.arraySize;
                map.GetArrayElementAtIndex(index).longValue = feature.GetInstanceID();
            }

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(feature);
            return true;
        }

        /// <summary>
        /// Both features march against scene depth, and URP does not produce a depth texture unless
        /// something asks for it. Without this they log a warning every frame and draw nothing —
        /// which looks exactly like the shaders being broken.
        /// </summary>
        private static void EnsureDepthTexture()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (pipeline == null || pipeline.supportsCameraDepthTexture)
                    continue;

                pipeline.supportsCameraDepthTexture = true;
                EditorUtility.SetDirty(pipeline);
                Debug.Log($"[Volumetrics] Enabled Depth Texture on {path}.");
            }
        }
    }
}
