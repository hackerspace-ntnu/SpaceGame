using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Scalar field that defines cave geometry.
    ///
    /// Convention (matches signed distance fields):
    ///   value &lt; 0  → inside the cave (empty / walkable air)
    ///   value &gt; 0  → outside the cave (solid rock)
    ///   value = 0  → the cave wall surface
    ///
    /// Marching cubes extracts the iso-surface at value == 0.
    /// </summary>
    public interface ICaveDensityField
    {
        float Sample(Vector3 worldPosition);

        /// <summary>Axis-aligned region in which the field has interesting variation.
        /// Marching cubes walks the voxel grid inside (a padded version of) these bounds.</summary>
        Bounds Bounds { get; }
    }
}
