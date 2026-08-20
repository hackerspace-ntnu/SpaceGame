using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Single entry point for the terrain-feature system — the terrain-side analogue of
    /// <c>CaveGenerator</c>. Given a feature and its context, it runs the whole shared pipeline and
    /// returns a ready-to-use <see cref="TerrainFeatureResult"/>. Concrete features never call the
    /// mesher / smoothing / skirt-blend themselves; they only describe their shape as an
    /// <see cref="ITerrainDensity"/>, and this orchestrator does the rest.
    ///
    /// Pipeline (multi-pass, like <c>SettlementGenerator</c>):
    ///   1. <see cref="TerrainFeature.BuildDensity"/>      — feature describes its solid volume.
    ///   2. <see cref="TerrainMarchingCubesMesher.Build"/> — voxelise + iso-surface extract the mesh
    ///                                                       (cheap surface-band walk for heightfields).
    ///   3. <see cref="TerrainSkirtBlend.Apply"/>          — snap the mesh's lower band onto the
    ///                                                       underlying terrain (no gap, no seam).
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

            // Surface detail vs smoothing: the post-marching-cubes Laplacian smoothing washes out the
            // fine crags the detail layer just put in. When the designer asks for strong detail we
            // automatically ease the smoothing so the jaggedness actually survives to the final mesh.
            meshSettings = AdaptSmoothingToDetail(meshSettings, context.Tuning);

            // 1) Feature describes its shape as a density field.
            ITerrainDensity density = feature.BuildDensity(context);
            if (density == null)
            {
                Debug.LogWarning($"[TerrainFeatureGenerator] feature '{feature.FeatureType}' returned a null density.");
                return result;
            }

            // 2) Marching cubes — surface-band walk for heightfields, full volume for voxel SDFs.
            Mesh mesh = TerrainMarchingCubesMesher.Build(density, meshSettings);
            if (mesh == null || mesh.vertexCount == 0)
            {
                Debug.LogWarning($"[TerrainFeatureGenerator] feature '{feature.FeatureType}' produced an empty mesh.");
                result.Mesh = mesh;
                return result;
            }
            mesh.name = $"{feature.FeatureType}_seed{context.Seed}";

            // 3) Skirt-blend: close the geometric seam where the feature meets the terrain. This is a
            //    small fixed contact fix only — it deliberately does NOT use the 'overlap' tuning, so
            //    'overlap' is free to mean exactly one thing: the feature's own soft edge falloff
            //    (TerrainNoiseHelper.OverlapWeight). The two used to fight over the same number.
            //
            TerrainSkirtBlend.Apply(mesh, context, embed: 1f);

            result.Mesh = mesh;
            return result;
        }

        /// <summary>
        /// Returns a mesh-settings instance whose smoothing has been eased in proportion to the shared
        /// <see cref="TerrainFeatureTuning.detailStrength"/>. Strong surface detail and heavy Laplacian
        /// smoothing fight each other — the smoothing flattens the very crags the detail layer adds.
        /// Rather than make the designer hand-tune two coupled knobs, we relax the smoothing here:
        /// at zero detail the original settings pass through untouched; at full detail the iteration
        /// count and per-iteration strength are pulled down so the jaggedness reaches the final mesh.
        /// The original object is never mutated — a shallow copy is returned when an adjustment applies.
        /// </summary>
        static TerrainMeshSettings AdaptSmoothingToDetail(TerrainMeshSettings settings, TerrainFeatureTuning tuning)
        {
            if (settings == null || tuning == null) return settings;
            if (tuning.detailStrength <= 0.001f || settings.smoothingIterations <= 0) return settings;

            // 0 at no detail → 1 at detailStrength >= 8m. Past ~8m the surface is meant to be raw rock.
            float t = Mathf.Clamp01(tuning.detailStrength / 8f);

            // Iterations fall from the authored count toward 1; strength eases toward 0.35.
            int   adjIterations = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(settings.smoothingIterations, 1f, t)));
            float adjStrength   = Mathf.Lerp(settings.smoothingStrength, 0.35f, t);

            if (adjIterations == settings.smoothingIterations &&
                Mathf.Approximately(adjStrength, settings.smoothingStrength))
                return settings;

            return new TerrainMeshSettings
            {
                voxelSize               = settings.voxelSize,
                surfaceBandVoxels       = settings.surfaceBandVoxels,
                use32BitIndices         = settings.use32BitIndices,
                smoothingIterations     = adjIterations,
                smoothingStrength       = adjStrength,
                smoothingPreserveVolume = settings.smoothingPreserveVolume,
                useGradientNormals      = settings.useGradientNormals,
                gradientEpsilon         = settings.gradientEpsilon,
                recalculateTangents     = settings.recalculateTangents,
            };
        }
    }
}
