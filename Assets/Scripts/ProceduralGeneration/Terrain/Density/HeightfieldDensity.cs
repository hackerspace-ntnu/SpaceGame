using System;
using UnityEngine;

/// <summary>
/// Cheap 2D-heightfield <see cref="ITerrainDensity"/>. The feature's surface is a single height
/// function f(x, z) — exactly right for dunes, mesas, buttes, cliffs, canyons, ridges and canyon
/// paths, none of which fold back over themselves.
///
/// Density convention: at local position p, density = p.y - SurfaceHeight(p.x, p.z). That is
/// negative below the surface (solid) and positive above (air) — the iso-surface at 0 is the
/// surface itself.
///
/// Performance: because the surface is single-valued, the <see cref="TerrainMarchingCubesMesher"/>
/// only needs to voxelise a thin band straddling the surface (see <see cref="Bounds"/>, which is
/// already collapsed to that band). A 256×256 m feature meshes in well under a second.
///
/// A feature constructs this by passing in its height lambda — it never touches voxels or MC.
/// </summary>
public sealed class HeightfieldDensity : ITerrainDensity
{
    readonly Func<float, float, float> _heightFn;
    readonly Bounds _bounds;

    public Bounds Bounds => _bounds;
    public bool IsHeightfield => true;

    /// <param name="heightFn">Surface height f(localX, localZ), in feature-local Y. Must be
    /// deterministic — same inputs always same output.</param>
    /// <param name="footprint">Local-space XZ footprint the feature covers. Y of this bounds is
    /// ignored; the mesher derives its vertical band from the height function plus padding.</param>
    /// <param name="minSurfaceY">Lowest value <paramref name="heightFn"/> can return.</param>
    /// <param name="maxSurfaceY">Highest value <paramref name="heightFn"/> can return.</param>
    /// <param name="bandPadding">Extra metres added above and below the surface band so marching
    /// cubes always has a solid corner and an air corner to interpolate between (≈ 2 voxels).</param>
    public HeightfieldDensity(
        Func<float, float, float> heightFn,
        Bounds footprint,
        float minSurfaceY,
        float maxSurfaceY,
        float bandPadding)
    {
        _heightFn = heightFn ?? ((x, z) => 0f);

        // Collapse the bounds vertically to just the surface band. The mesher walks only this slab,
        // which is the whole performance point of the heightfield path.
        float lo = minSurfaceY - bandPadding;
        float hi = maxSurfaceY + bandPadding;
        Vector3 centre = new Vector3(footprint.center.x, (lo + hi) * 0.5f, footprint.center.z);
        Vector3 size = new Vector3(footprint.size.x, Mathf.Max(hi - lo, 0.1f), footprint.size.z);
        _bounds = new Bounds(centre, size);
    }

    public float Sample(Vector3 p) => p.y - _heightFn(p.x, p.z);

    public float SurfaceHeight(float localX, float localZ) => _heightFn(localX, localZ);
}
