using System.Collections.Generic;
using UnityEngine;

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
    /// <summary>The finished feature mesh, in feature-local space. Never null on success for a
    /// SINGLE-mesh feature. Null for a multi-mesh feature — see <see cref="SubMeshes"/>.</summary>
    public Mesh Mesh;

    /// <summary>
    /// For a MULTI-mesh feature (<see cref="TerrainFeature.ProducesMultipleMeshes"/> true): every
    /// sub-mesh the feature produced, each in feature-local space and ready for its own
    /// MeshFilter+MeshCollider. Null for the ordinary single-mesh path (which uses <see cref="Mesh"/>).
    /// Kept as a separate optional field so the single-mesh pipeline is entirely unchanged.
    /// </summary>
    public List<Mesh> SubMeshes;

    /// <summary>True when this result came from the multi-mesh path and carries sub-meshes.</summary>
    public bool IsMultiMesh => SubMeshes != null && SubMeshes.Count > 0;

    /// <summary>Local-space axis-aligned bounds the feature actually occupies.</summary>
    public Bounds Bounds;

    /// <summary>Seed the feature was generated with (echoed for the bake-asset filename).</summary>
    public int Seed;

    /// <summary>Which feature type produced this (for logging / asset naming).</summary>
    public TerrainFeatureType FeatureType;

    /// <summary>True when the result carries usable geometry — either a valid single
    /// <see cref="Mesh"/> or at least one non-empty entry in <see cref="SubMeshes"/>.</summary>
    public bool IsValid => (Mesh != null && Mesh.vertexCount > 0) || IsMultiMesh;
}
