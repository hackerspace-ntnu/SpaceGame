// Rewires a model's renderers onto double-sided copies of their materials.
//
// Vehicle hulls here are modelled as surfaces, not solids, so with back-face culling on you can
// stand in a cabin and see straight out through the walls — and a mesh that arrives with flipped
// winding (a mirrored part exported with negative scale) is invisible from the outside entirely.
// Rendering double-sided fixes both without changing the exterior look: front faces draw exactly
// as they did, back faces simply stop being discarded.
//
// The source materials are sub-assets of the imported model, regenerated on every reimport, so a
// cull flag written onto them would silently revert. These generated copies live beside the
// prefabs instead and are refreshed from their source on every build, so re-running a builder
// after a re-texture still picks up the new look.
//
// Extracted from a vehicle builder once a second one wanted the same trick.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SpaceGame.EditorTools
{
    public static class DoubleSidedMaterials
    {
        private const string VehicleMaterialFolder = "Assets/Game/Art/Materials/Vehicles";
        private const string DoubleSidedSuffix = " (DoubleSided)";

        /// <summary>
        /// Swap every material under <paramref name="model"/> for its double-sided copy.
        /// The copies land in <paramref name="folder"/>, which defaults to the vehicle
        /// folder the first two callers used; pass the asset's own domain folder instead
        /// when it is not a vehicle.
        /// </summary>
        public static void Apply(Transform model, string folder = VehicleMaterialFolder)
        {
            var remap = new Dictionary<Material, Material>();

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                        continue;

                    Material variant = DoubleSidedCopy(mats[i], folder, remap);
                    if (variant == mats[i])
                        continue;

                    mats[i] = variant;
                    changed = true;
                }

                if (changed)
                    r.sharedMaterials = mats;
            }
        }

        private static Material DoubleSidedCopy(Material source, string folder,
                                                Dictionary<Material, Material> cache)
        {
            if (cache.TryGetValue(source, out Material cached))
                return cached;

            // Already one of ours — happens when a builder is re-run against a rebuilt prefab.
            if (source.name.EndsWith(DoubleSidedSuffix))
            {
                cache[source] = source;
                return source;
            }

            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(folder),
                                           System.IO.Path.GetFileName(folder));

            string path = $"{folder}/{source.name}{DoubleSidedSuffix}.mat";
            Material variant = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (variant == null)
            {
                variant = new Material(source) { name = source.name + DoubleSidedSuffix };
                AssetDatabase.CreateAsset(variant, path);
            }
            else
            {
                variant.shader = source.shader;
                variant.CopyPropertiesFromMaterial(source);
            }

            if (variant.HasProperty("_Cull"))
                variant.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            variant.doubleSidedGI = true;
            EditorUtility.SetDirty(variant);

            cache[source] = variant;
            return variant;
        }
    }
}
