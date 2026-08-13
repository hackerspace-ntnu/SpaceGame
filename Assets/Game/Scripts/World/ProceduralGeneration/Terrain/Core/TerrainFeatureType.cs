namespace SpaceGame.World
{
    /// <summary>
    /// The procedural terrain feature kinds. The <c>TerrainFeatureSpawner</c> inspector shows this as a
    /// dropdown; <see cref="TerrainFeatureRegistry"/> maps each entry to the concrete
    /// <see cref="TerrainFeature"/> subclass that builds it.
    ///
    /// The integer values are NOT contiguous on purpose. This enum once carried fourteen entries; the
    /// twelve that no scene ever used were deleted, and the two survivors keep their original numbers
    /// because serialized scenes store the integer, not the name. Do not renumber these, and only
    /// append if you add a feature back.
    /// </summary>
    public enum TerrainFeatureType
    {
        /// <summary>Flat-topped, steep-sided mesa. Heightfield density, or voxel when overhangs are on.</summary>
        Mesa = 2,

        /// <summary>A cliff escarpment — a height step across the box. Heightfield density, or voxel
        /// when overhangs are on.</summary>
        Cliff = 4,
    }
}
