/// <summary>
/// Stone arch: a tall, dramatically curved, free-standing wind-eroded sandstone arch that visibly
/// spans between two ground-anchored abutments, leaving a high open air gap underneath.
///
/// This is a thin subclass of the merged <see cref="SpanFeature"/> — the project lead noted the
/// stone arch and the natural bridge "can actually be merged together", so ALL the voxel-SDF
/// construction lives once in <see cref="SpanFeature"/>. This class only declares its
/// <see cref="TerrainFeatureType"/> and picks the <see cref="SpanStyle.DramaticArch"/> default,
/// which tunes the shared <see cref="SpanFeatureSettings"/> toward a tall, narrow, high-rising arch.
///
/// The <see cref="TerrainFeatureType.StoneArch"/> enum entry is kept (scenes serialize it) and the
/// parameterless constructor is preserved so <c>TerrainFeatureRegistry</c> can
/// <c>new StoneArchFeature()</c> it.
///
/// Registration: <c>Register(() => new StoneArchFeature());</c>
/// </summary>
public sealed class StoneArchFeature : SpanFeature
{
    /// <inheritdoc/>
    public override TerrainFeatureType FeatureType => TerrainFeatureType.StoneArch;

    /// <summary>A stone arch defaults to the tall, dramatic-arch silhouette.</summary>
    protected override SpanStyle DefaultStyle => SpanStyle.DramaticArch;
}
