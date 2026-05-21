using UnityEngine;

/// <summary>
/// Single entry point for the terrain-feature system — the terrain-side analogue of
/// <c>CaveGenerator</c>. Given a feature and its context, it runs the whole shared pipeline and
/// returns a ready-to-use <see cref="TerrainFeatureResult"/>. The nine concrete features never
/// call the mesher / smoothing / skirt-blend themselves; they only describe their shape as an
/// <see cref="ITerrainDensity"/>, and this orchestrator does the rest.
///
/// Pipeline (multi-pass, like <c>SettlementGenerator</c>):
///   1. <see cref="TerrainFeature.BuildDensity"/>      — feature describes its solid volume.
///   2. <see cref="TerrainMarchingCubesMesher.Build"/> — voxelise + iso-surface extract the mesh
///                                                       (cheap surface-band walk for heightfields).
///   3. <see cref="TerrainSkirtBlend.Apply"/>          — snap the mesh's lower band onto the
///                                                       underlying terrain (no gap, no seam).
///   4. <see cref="TerrainFeature.PostProcess"/>       — optional per-feature final mesh tweak.
///
/// Deterministic: identical (feature, context) always yields an identical mesh.
/// </summary>
public static class TerrainFeatureGenerator
{
    /// <summary>
    /// Runs the full pipeline. Returns a result whose <see cref="TerrainFeatureResult.IsValid"/>
    /// is false if the feature produced an empty mesh (logged as a warning).
    /// </summary>
    public static TerrainFeatureResult Generate(TerrainFeature feature, FeatureContext context, TerrainMeshSettings meshSettings)
    {
        var result = new TerrainFeatureResult();
        if (feature == null)
        {
            Debug.LogWarning("[TerrainFeatureGenerator] null feature — nothing to generate.");
            return result;
        }
        if (context == null)
        {
            Debug.LogWarning("[TerrainFeatureGenerator] null context — nothing to generate.");
            return result;
        }
        if (meshSettings == null) meshSettings = new TerrainMeshSettings();

        result.FeatureType = feature.FeatureType;
        result.Seed = context.Seed;

        // 1) Feature describes its shape as a density field.
        ITerrainDensity density = feature.BuildDensity(context);
        if (density == null)
        {
            Debug.LogWarning($"[TerrainFeatureGenerator] feature '{feature.DisplayName}' returned a null density.");
            return result;
        }

        // 2) Marching cubes — surface-band walk for heightfields, full volume for voxel SDFs.
        Mesh mesh = TerrainMarchingCubesMesher.Build(density, meshSettings);
        if (mesh == null || mesh.vertexCount == 0)
        {
            Debug.LogWarning($"[TerrainFeatureGenerator] feature '{feature.DisplayName}' produced an empty mesh.");
            result.Mesh = mesh;
            return result;
        }
        mesh.name = $"{feature.FeatureType}_seed{context.Seed}";

        // 3) Skirt-blend the lower band down onto the terrain. Band width tracks the feature's
        //    overlap tuning so the skirt and the surface falloff feel consistent.
        float blendBand = Mathf.Max(2f, context.Tuning != null ? context.Tuning.overlap : 8f);
        TerrainSkirtBlend.Apply(mesh, context, blendBand, embed: 1f);

        // 4) Optional per-feature final tweak.
        feature.PostProcess(mesh, context);

        result.Mesh = mesh;
        result.Bounds = mesh.bounds;
        return result;
    }
}
