namespace SpaceGame.World
{
    /// <summary>
    /// Natural bridge: a thick, WIDE, gently-arched slab of eroded sandstone you can walk
    /// continuously across end-to-end, with open air beneath the arch.
    ///
    /// This is a thin subclass of the merged <see cref="SpanFeature"/> — the project lead noted the
    /// natural bridge and the stone arch "can actually be merged together", so ALL the voxel-SDF
    /// construction lives once in <see cref="SpanFeature"/>. This class only declares its
    /// <see cref="TerrainFeatureType"/> and picks the <see cref="SpanStyle.WalkableBridge"/> default,
    /// which tunes the shared <see cref="SpanFeatureSettings"/> toward a wide, low, walkable deck.
    ///
    /// The <see cref="TerrainFeatureType.NaturalBridge"/> enum entry is kept (scenes serialize it) and
    /// the parameterless constructor is preserved so <c>TerrainFeatureRegistry</c> can
    /// <c>new NaturalBridgeFeature()</c> it.
    ///
    /// Registration: <c>Register(() => new NaturalBridgeFeature());</c>
    /// </summary>
    public sealed class NaturalBridgeFeature : SpanFeature
    {
        /// <inheritdoc/>
        public override TerrainFeatureType FeatureType => TerrainFeatureType.NaturalBridge;

        /// <summary>A natural bridge defaults to the wide, walkable-bridge silhouette.</summary>
        protected override SpanStyle DefaultStyle => SpanStyle.WalkableBridge;
    }
}
