/// <summary>
/// How an AREA terrain feature's footprint outline is defined on a <c>TerrainFeatureSpawner</c>.
/// Linear features ignore this — they always use the spline path.
/// </summary>
public enum FootprintShape
{
    /// <summary>The designer hand-edits the closed polygon with the Scene-view vertex handles.</summary>
    Polygon = 0,

    /// <summary>The outline is generated deterministically from noise (see
    /// <see cref="FeaturePolygon.GenerateFromNoise"/>) — no hand-editing. The spawner's
    /// footprint noise-scale and irregularity knobs drive the shape.</summary>
    Noise = 1,
}
