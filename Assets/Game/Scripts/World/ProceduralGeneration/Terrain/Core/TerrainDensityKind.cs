namespace SpaceGame.World
{
    /// <summary>
    /// Declares which density model a <see cref="TerrainFeature"/> needs. A feature returns one of
    /// these from <c>TerrainFeature.DensityKind</c>; the generator then constructs the matching
    /// <see cref="ITerrainDensity"/> implementation and the marching-cubes mesher walks it.
    ///
    /// Heightfield is far cheaper and is correct for anything whose surface is a function of (x, z):
    /// dunes, mesas, buttes, cliffs, canyons, ridges, canyon paths. Voxel is only needed where the
    /// surface genuinely folds back over itself — true overhangs: natural bridges, stone arches and
    /// cave entrances. Choosing Heightfield where possible is a hard performance requirement.
    /// </summary>
    public enum TerrainDensityKind
    {
        /// <summary>2D heightfield. Surface = f(x, z). Mesher only voxelises a thin band near the
        /// surface, so cost scales with footprint AREA, not volume. Use for everything possible.</summary>
        Heightfield,

        /// <summary>Full 3D voxel SDF. Surface may fold over itself (overhangs). Cost scales with
        /// footprint VOLUME. Use only for natural bridges, stone arches and cave entrances.</summary>
        Voxel,
    }
}
