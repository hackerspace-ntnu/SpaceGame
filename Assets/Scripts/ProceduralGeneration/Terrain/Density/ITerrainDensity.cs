using UnityEngine;

/// <summary>
/// Scalar field that defines a terrain feature's solid volume. Same sign convention as the cave
/// system's <c>ICaveDensityField</c>, so the marching-cubes mesher reads identically:
///
///   value &lt; 0  → inside solid rock/sand (below the feature surface)
///   value &gt; 0  → outside / open air (above the feature surface)
///   value = 0  → the feature surface itself
///
/// The terrain MC mesher extracts the iso-surface at value == 0.
///
/// Two implementations exist, picked by a feature's <see cref="TerrainFeature.DensityKind"/>:
///   • <see cref="HeightfieldDensity"/> — cheap. Density derives from a 2D surface height f(x, z).
///     The mesher only voxelises a thin band straddling that surface.
///   • <see cref="VoxelSdfDensity"/> — full 3D SDF, supports real overhangs (arches, bridges).
///
/// A feature never implements this interface directly: it supplies either a height function or an
/// SDF function, and the generator wraps it in the matching implementation. This keeps all nine
/// features writing the same simple lambda and never touching the mesher.
/// </summary>
public interface ITerrainDensity
{
    /// <summary>Signed density at a feature-local position. Negative = solid, positive = air.</summary>
    float Sample(Vector3 localPosition);

    /// <summary>Local-space region the mesher should walk. For a heightfield this is a thin slab
    /// around the surface band; for a voxel SDF it is the feature's full bounding volume.</summary>
    Bounds Bounds { get; }

    /// <summary>True for a heightfield density — lets the mesher use the cheap surface-band walk
    /// instead of voxelising the whole volume.</summary>
    bool IsHeightfield { get; }

    /// <summary>For a heightfield density only: the surface height f(x, z) at a local (x, z).
    /// The mesher uses this to centre its thin voxel band. Voxel densities may return 0.</summary>
    float SurfaceHeight(float localX, float localZ);
}
