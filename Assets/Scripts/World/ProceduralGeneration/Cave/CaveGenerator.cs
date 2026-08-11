using UnityEngine;

/// <summary>
/// Single entry point for the cave system. Two overloads:
///   • <see cref="Generate(CaveGenerationSettings)"/> — legacy shape-only path. No liquids or decoration
///     (those live on the profile, not the bare shape config). Useful for editor previews and the
///     pre-existing bake pipeline that only cares about mesh + navmesh.
///   • <see cref="Generate(CaveProfile)"/> — full pipeline. Adds liquid pool detection on top.
///     Decoration scatter happens in <see cref="CaveSpawner"/> because it needs the live mesh+transform.
///
/// Pipeline:
///   1. <see cref="CaveGraphGenerator"/>  — random walk produces rooms + corridors (+ shape modifiers)
///   2. <see cref="CaveSdfField"/>        — wraps graph as a continuous SDF
///   3. <see cref="MarchingCubesMesher"/> — voxelise + iso-surface extract the mesh
///   4. <see cref="LiquidPoolFinder"/>    — flood-fill the SDF to find liquid pools (profile path only)
/// </summary>
public static class CaveGenerator
{
    public static CaveGenerationResult Generate(CaveGenerationSettings settings)
    {
        if (settings == null) settings = new CaveGenerationSettings();

        var rng = new System.Random(settings.seed);

        var graph = CaveGraphGenerator.Generate(settings, rng);
        var field = new CaveSdfField(graph, settings);
        var mesh  = MarchingCubesMesher.Build(field, settings);

        return new CaveGenerationResult
        {
            Graph        = graph,
            DensityField = field,
            Mesh         = mesh,
            Bounds       = field.Bounds,
            Seed         = settings.seed,
        };
    }

    public static CaveGenerationResult Generate(CaveProfile profile)
    {
        if (profile == null) return Generate((CaveGenerationSettings)null);

        var result = Generate(profile.shape);

        if (profile.liquid != null && profile.liquid.enabled)
            result.LiquidPools = LiquidPoolFinder.Find(result.DensityField, profile.liquid, null);

        return result;
    }
}
