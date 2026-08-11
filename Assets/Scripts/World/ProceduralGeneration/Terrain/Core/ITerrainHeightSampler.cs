using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Abstraction over "what is the ground height under this world-space (x, z)?". Terrain features
    /// need this twice over:
    ///   • The DENSITY layer adds the feature shape on top of the existing ground.
    ///   • The SKIRT-BLEND helper snaps the feature mesh's lower band down onto the ground so there is
    ///     no floating gap and no hard seam.
    ///
    /// The default production implementation is <see cref="UnityTerrainHeightSampler"/>, which wraps
    /// the live Unity <c>Terrain</c> the feature sits on. Tests / previews can swap in a flat sampler.
    /// Keeping it an interface means the density + meshing pipeline never references <c>Terrain</c>
    /// directly and stays unit-testable and deterministic.
    /// </summary>
    public interface ITerrainHeightSampler
    {
        /// <summary>World-space ground height at the given world (x, z). Returns the feature's base
        /// plane height if no terrain is present, so features still work in terrain-less test scenes.</summary>
        float SampleHeight(float worldX, float worldZ);

        /// <summary>World-space ground surface normal at (x, z). Used for slope-aware blending.</summary>
        Vector3 SampleNormal(float worldX, float worldZ);
    }

    /// <summary>
    /// Wraps a Unity <c>Terrain</c> as an <see cref="ITerrainHeightSampler"/>. If the terrain reference
    /// is null every sample returns the supplied flat fallback height, so a feature still bakes in a
    /// scene with no terrain (handy for isolated previews).
    /// </summary>
    public sealed class UnityTerrainHeightSampler : ITerrainHeightSampler
    {
        readonly Terrain _terrain;
        readonly float _fallbackHeight;

        public UnityTerrainHeightSampler(Terrain terrain, float fallbackHeight = 0f)
        {
            _terrain = terrain;
            _fallbackHeight = fallbackHeight;
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            if (_terrain == null) return _fallbackHeight;
            return _terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + _terrain.transform.position.y;
        }

        public Vector3 SampleNormal(float worldX, float worldZ)
        {
            if (_terrain == null || _terrain.terrainData == null) return Vector3.up;
            var data = _terrain.terrainData;
            Vector3 tp = _terrain.transform.position;
            // Interpolated normal expects normalised [0,1] terrain coordinates.
            float u = Mathf.Clamp01((worldX - tp.x) / data.size.x);
            float v = Mathf.Clamp01((worldZ - tp.z) / data.size.z);
            return data.GetInterpolatedNormal(u, v);
        }
    }
}
