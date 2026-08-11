/// <summary>
/// How an AREA terrain feature's footprint outline is authored on a <c>TerrainFeatureSpawner</c>.
/// Linear features ignore this entirely — they always sweep the spline <see cref="FeaturePath"/>.
///
/// Both modes resolve to the SAME thing: an effective closed polygon that every feature queries
/// through <see cref="FeatureContext.FootprintDistanceInside"/>. A feature never branches on the
/// mode — it only ever asks for the signed distance to the outline.
/// </summary>
public enum FootprintMode
{
    /// <summary>
    /// Hand-authored. The designer drags the polygon vertices directly in the Scene view, clicks
    /// an edge to insert a vertex, and deletes vertices with the per-vertex button. The polygon IS
    /// the footprint — nothing is generated.
    /// </summary>
    Polygon = 0,

    /// <summary>
    /// Procedurally generated. The outline is produced by <see cref="FootprintNoise"/> from the
    /// box dimensions (Width × Breadth) plus a set of explicit noise knobs — lobe frequency,
    /// lobe amplitude, detail octaves/gain, irregularity, corner sharpness. The result ranges from
    /// a clean rounded blob to a wild, messy, multi-armed silhouette. No vertex hand-editing; the
    /// designer resizes the box and tunes the knobs, and the outline regenerates deterministically.
    /// </summary>
    Noise = 1,
}
