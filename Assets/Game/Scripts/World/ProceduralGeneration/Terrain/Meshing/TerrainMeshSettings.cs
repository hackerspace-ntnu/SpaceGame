using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Meshing + smoothing parameters for the terrain-feature marching-cubes pass. Kept separate from
    /// the per-feature <see cref="TerrainFeatureTuning"/> because these knobs are about MESH QUALITY,
    /// not feature SHAPE — every feature usually wants the same values, and the spawner exposes one
    /// shared block of them.
    ///
    /// Mirrors the relevant fields of the cave system's <c>CaveGenerationSettings</c> so the shared
    /// <see cref="MeshSmoothingUtility"/> and <see cref="FlatShadeUtility"/> can be reused unchanged.
    /// Terrain wants smoothing ON by default (the project lead requires non-faceted terrain).
    /// </summary>
    [System.Serializable]
    public class TerrainMeshSettings
    {
        [Header("Resolution")]
        [Tooltip("Voxel edge length in metres. Smaller = more detail + much slower bake. 1.5–3 is a good band for large terrain features.")]
        [Range(0.5f, 6f)] public float voxelSize = 2f;

        [Tooltip("Heightfield features only: voxels of padding added below/above the surface band. The mesher already auto-expands the band across neighbouring columns to bridge steep walls, so 2-3 is enough even for cliffs and buttes; this is just iso-surface padding, not a steepness knob.")]
        [Range(2, 8)] public int surfaceBandVoxels = 3;

        [Tooltip("Use 32-bit mesh indices — required for features above 65k vertices.")]
        public bool use32BitIndices = true;

        [Header("Smoothing (post-marching-cubes)")]
        [Tooltip("Laplacian smoothing iterations. Terrain should not look flat-faceted, so keep this at 2-3. 0 = raw blocky MC output.")]
        [Range(0, 8)] public int smoothingIterations = 3;

        [Tooltip("Per-iteration blend factor. 0.5 is a safe default.")]
        [Range(0f, 1f)] public float smoothingStrength = 0.5f;

        [Tooltip("Taubin volume preservation — alternates an inflate pass with each shrink pass so the feature does not deflate as it smooths. Recommended on.")]
        public bool smoothingPreserveVolume = true;

        [Tooltip("After smoothing, replace normals with the analytic density gradient for perfectly smooth shading on low-poly geometry.")]
        public bool useGradientNormals = true;

        [Tooltip("Finite-difference epsilon (metres) for the density gradient. ~half the voxel size is a good default.")]
        [Range(0.05f, 3f)] public float gradientEpsilon = 1f;

        [Tooltip("Recalculate tangents (only needed for normal-mapped feature materials).")]
        public bool recalculateTangents = false;
    }
}
