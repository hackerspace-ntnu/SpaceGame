using System;
using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Cheap 2D-heightfield <see cref="ITerrainDensity"/>. The feature's surface is a single height
    /// function f(x, z) — exactly right for dunes, mesas, buttes, cliffs, canyons, ridges and canyon
    /// paths, none of which fold back over themselves.
    ///
    /// Density convention: at local position p, density = p.y - SurfaceHeight(p.x, p.z). That is
    /// negative below the surface (solid) and positive above (air) — the iso-surface at 0 is the
    /// surface itself.
    ///
    /// COVERAGE MASK: a feature footprint box is almost always larger than the feature shape itself —
    /// a mesa, butte, cliff or ridge only occupies part of its box, the rest is plain ground. Without
    /// a mask the height function returns the natural ground height in those empty columns, so the
    /// mesher builds a flat ground-following apron all around the actual feature. That apron is not
    /// wanted: only the feature rock should be meshed. A feature therefore passes an optional
    /// <c>coverageFn</c> — true only in columns the feature shape actually occupies. Uncovered columns
    /// are reported as pure air (no solid, no surface), so marching cubes produces no geometry there
    /// and the result is just the feature, not a slab of terrain around it.
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
        readonly Func<float, float, bool> _coverageFn;
        readonly Bounds _bounds;

        /// <summary>Surface height reported for uncovered columns — a sentinel the
        /// <see cref="TerrainMarchingCubesMesher"/> recognises and skips when sizing per-column bands,
        /// so an uncovered column produces no geometry and never drags a neighbour's band down.</summary>
        public const float NoColumn = -1e9f;

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
        /// <param name="coverageFn">Optional per-column mask: true where the feature shape actually
        /// exists, false where the column is just plain ground. Columns that return false are meshed
        /// as pure air so no flat ground apron is generated around the feature. When null, every
        /// column is treated as covered (legacy whole-box behaviour).</param>
        public HeightfieldDensity(
            Func<float, float, float> heightFn,
            Bounds footprint,
            float minSurfaceY,
            float maxSurfaceY,
            float bandPadding,
            Func<float, float, bool> coverageFn = null)
        {
            _heightFn = heightFn ?? ((x, z) => 0f);
            _coverageFn = coverageFn;

            // Collapse the bounds vertically to just the surface band. The mesher walks only this slab,
            // which is the whole performance point of the heightfield path.
            float lo = minSurfaceY - bandPadding;
            float hi = maxSurfaceY + bandPadding;
            Vector3 centre = new Vector3(footprint.center.x, (lo + hi) * 0.5f, footprint.center.z);
            Vector3 size = new Vector3(footprint.size.x, Mathf.Max(hi - lo, 0.1f), footprint.size.z);
            _bounds = new Bounds(centre, size);
        }

        public float Sample(Vector3 p)
        {
            // Uncovered column → pure air everywhere, so marching cubes builds nothing here.
            if (_coverageFn != null && !_coverageFn(p.x, p.z))
                return 1f;
            return p.y - _heightFn(p.x, p.z);
        }

        public float SurfaceHeight(float localX, float localZ)
        {
            // Uncovered column → report a surface far below ground so the mesher's per-column band
            // straddling the surface collapses to nothing and no apron geometry is produced.
            if (_coverageFn != null && !_coverageFn(localX, localZ))
                return NoColumn;
            return _heightFn(localX, localZ);
        }
    }
}
