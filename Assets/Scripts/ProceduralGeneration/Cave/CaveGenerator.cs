using UnityEngine;

/// <summary>
/// Single entry point for the cave system. Mirrors the static-orchestrator style used by
/// <c>SettlementGenerator</c>: callers pass in settings + seed, get back a self-contained
/// <see cref="CaveGenerationResult"/> (graph + density field + mesh).
///
/// Pipeline:
///   1. <see cref="CaveGraphGenerator"/>  — random walk produces rooms + corridors
///   2. <see cref="CaveSdfField"/>        — wrap graph as a continuous SDF (rooms = spheres,
///                                          corridors = capsules, smooth-mined together, plus noise)
///   3. <see cref="MarchingCubesMesher"/> — voxelise + iso-surface extract the mesh
///
/// The result is *pure data* — instantiation/NavMesh-baking lives in <see cref="CaveSpawner"/>.
/// </summary>
public static class CaveGenerator
{
    public static CaveGenerationResult Generate(CaveGenerationSettings settings)
    {
        if (settings == null) settings = new CaveGenerationSettings();

        var rng = new System.Random(settings.seed);

        // 1. graph
        var graph = CaveGraphGenerator.Generate(settings, rng);

        // 2. density field
        var field = new CaveSdfField(graph, settings);

        // 3. mesh
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
}
