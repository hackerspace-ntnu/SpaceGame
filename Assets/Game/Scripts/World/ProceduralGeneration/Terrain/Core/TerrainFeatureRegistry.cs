namespace SpaceGame.World
{
    /// <summary>
    /// Maps a <see cref="TerrainFeatureType"/> enum value to a fresh instance of the concrete
    /// <see cref="TerrainFeature"/> subclass that builds it. The <c>TerrainFeatureSpawner</c> uses this
    /// to turn the inspector's feature-type dropdown into an actual feature object.
    ///
    /// Features are stateless (all state lives in the <see cref="FeatureContext"/>), so this news one
    /// up per request. To add a feature, write the subclass and add one arm to <see cref="Create"/> —
    /// nothing in the spawner, editor or pipeline needs to change.
    /// </summary>
    public static class TerrainFeatureRegistry
    {
        /// <summary>Creates a fresh feature instance for the given type, or null if the type has no
        /// implementation — which happens only for a stale integer in a scene authored before that
        /// feature was removed. Callers should null-check and surface a clear message.</summary>
        public static TerrainFeature Create(TerrainFeatureType type) => type switch
        {
            TerrainFeatureType.Mesa  => new MesaFeature(),
            TerrainFeatureType.Cliff => new CliffFeature(),
            _ => null,
        };

        /// <summary>True if a concrete feature exists for this type.</summary>
        public static bool IsImplemented(TerrainFeatureType type) => Create(type) != null;
    }
}
