using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Presentation helpers for <see cref="TerrainFeatureSpawner"/> — the default feature material, the
    /// material-preset palette and the passive footprint gizmo. Split out of the spawner MonoBehaviour
    /// purely to keep that file small; none of this is gameplay logic.
    /// </summary>
    public static class TerrainFeatureSpawnerVisuals
    {
        /// <summary>
        /// Maps each <see cref="TerrainMaterialPreset"/> to the GUID of its material asset. GUIDs (not
        /// paths) so the link survives a material being moved or renamed. <see cref="TerrainMaterialPreset.Custom"/>
        /// has no entry — it means "use the explicit field as-is".
        /// </summary>
        public static string PresetGuid(TerrainMaterialPreset preset) => preset switch
        {
            TerrainMaterialPreset.SandstoneLight => "e303e337cfa441c39daddcff2d8e1107", // SandstoneTriplanarLight.mat
            TerrainMaterialPreset.RedDesert      => "2d363f8cea684e899955cb0d1ca5a6d0", // SandstoneTriplanarRedDesert.mat
            TerrainMaterialPreset.GoldenDune     => "69a4c91c037a4e1e8684ffa78cb8b613", // SandstoneTriplanarGoldenDune.mat
            TerrainMaterialPreset.SaltFlat       => "81ecd7e606244a63a731160b71d541fc", // SandstoneTriplanarSaltFlat.mat
            TerrainMaterialPreset.SandstoneDark  => "ecae8f10927654f62a2252d541c4d569", // SandstoneTriplanar.mat
            _ => null,
        };

    #if UNITY_EDITOR
        /// <summary>
        /// Editor-only: loads the material asset a preset points at. Returns null for
        /// <see cref="TerrainMaterialPreset.Custom"/> or if the asset cannot be found.
        /// </summary>
        public static Material LoadPresetMaterial(TerrainMaterialPreset preset)
        {
            string guid = PresetGuid(preset);
            if (string.IsNullOrEmpty(guid)) return null;
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path)
                ? null
                : UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
        }
    #endif

        /// <summary>
        /// Returns the explicit feature material if one was assigned, otherwise builds a neutral
        /// desert-rock URP/Lit material so a feature is always visible even before art is hooked up.
        /// </summary>
        public static Material ResolveMaterial(Material assigned)
        {
            if (assigned != null) return assigned;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(shader) { name = "DefaultTerrainFeatureMaterial" };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.78f, 0.66f, 0.46f));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            return m;
        }

        /// <summary>
        /// Draws the passive footprint gizmo for a spawner — the editable polygon outline, or the
        /// seeding box when no polygon exists yet. The interactive handles are drawn separately by the
        /// editor; this is the always-on outline.
        /// </summary>
        public static void DrawFootprintGizmo(TerrainFeatureSpawner spawner)
        {
            if (spawner == null) return;
            Gizmos.matrix = spawner.transform.localToWorldMatrix;

            Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.6f);
            FeaturePolygon footprint = spawner.Footprint;
            if (footprint != null && footprint.IsValid)
            {
                var v = footprint.vertices;
                for (int i = 0; i < v.Count; i++)
                {
                    Vector3 a = new Vector3(v[i].x, 0f, v[i].z);
                    int j = (i + 1) % v.Count;
                    Vector3 b = new Vector3(v[j].x, 0f, v[j].z);
                    Gizmos.DrawLine(a, b);
                }
            }
            else
            {
                // No polygon drawn yet — show the seeding box instead.
                Gizmos.DrawWireCube(Vector3.zero, spawner.BoxHalfExtents * 2f);
            }
        }
    }
}
