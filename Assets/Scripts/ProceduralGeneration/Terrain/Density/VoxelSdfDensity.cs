using System;
using UnityEngine;

/// <summary>
/// Full 3D-voxel <see cref="ITerrainDensity"/>. The feature supplies a true signed distance
/// function over the whole bounding volume, so the surface may fold back over itself — real
/// overhangs. Use this ONLY for natural bridges, stone arches and surface cave entrances; every
/// other feature should use the far cheaper <see cref="HeightfieldDensity"/>.
///
/// Sign convention matches the cave SDF and the heightfield: negative = solid, positive = air,
/// zero = surface. A feature builds its SDF from the shared <see cref="SdfPrimitives"/> helpers
/// (Sphere, Capsule, SmoothMin, ApplyFloorFlatten) exactly as the cave system does — that reuse
/// is mandated, and it means an arch is "solid block minus a smooth-min'd capsule tunnel", etc.
///
/// Performance: cost scales with footprint VOLUME, not area, because the mesher must voxelise the
/// whole bounds. Keep voxel-SDF feature footprints modest.
/// </summary>
public sealed class VoxelSdfDensity : ITerrainDensity
{
    readonly Func<Vector3, float> _sdfFn;
    readonly Bounds _bounds;

    public Bounds Bounds => _bounds;
    public bool IsHeightfield => false;

    /// <param name="sdfFn">Signed density at a feature-local point. Negative = solid, positive =
    /// air. Must be deterministic.</param>
    /// <param name="volumeBounds">Local-space volume the mesher walks. Should fully enclose the
    /// feature's solid geometry with a couple of voxels of air padding on every side.</param>
    public VoxelSdfDensity(Func<Vector3, float> sdfFn, Bounds volumeBounds)
    {
        _sdfFn = sdfFn ?? (_ => 1f);
        _bounds = volumeBounds;
    }

    public float Sample(Vector3 p) => _sdfFn(p);

    /// <summary>Not meaningful for a folded SDF — the surface is multi-valued. Returns the bounds
    /// centre Y so callers that blindly probe it get a sane value.</summary>
    public float SurfaceHeight(float localX, float localZ) => _bounds.center.y;
}
