using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Pure-data bundle returned by <see cref="TerrainFeatureGenerator.Generate"/>. Mirrors the cave
    /// system's <c>CaveGenerationResult</c>: the generator produces data, the <c>TerrainFeatureSpawner</c>
    /// turns it into a scene GameObject (mesh filter + renderer + collider) and the editor saves it.
    ///
    /// The mesh is in feature-LOCAL space and has already been skirt-blended down onto the underlying
    /// terrain, smoothed, and given normals — it is ready to drop straight onto a MeshFilter.
    /// </summary>
    public sealed class TerrainFeatureResult
    {
        /// <summary>The finished feature mesh, in feature-local space. Never null on success.</summary>
        public Mesh Mesh;

        /// <summary>True when the result carries usable geometry.</summary>
        public bool IsValid => Mesh != null && Mesh.vertexCount > 0;
    }
}
