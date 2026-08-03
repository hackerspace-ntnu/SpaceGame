using UnityEngine;

/// <summary>
/// Static helper that turns a generated feature mesh — or a set of internally-chunked sub-meshes —
/// into scene GameObjects. Factored out of <see cref="TerrainFeatureSpawner"/> so the spawner stays
/// small and the single-mesh and MULTI-MESH instantiation paths share one tested code path.
///
/// Every mesh becomes a child GameObject with a MeshFilter, a MeshRenderer and a
/// <see cref="MeshCollider"/> on the feature layer. The MeshCollider is what makes the feature
/// contribute to the shared world NavMesh — the world's <c>NavMeshSourceCache</c> collects it on
/// the next <c>NavMeshSurface</c> rebuild. A multi-mesh feature therefore contributes one
/// continuous walkable surface, just spread over several colliders.
/// </summary>
public static class TerrainFeatureMeshSpawn
{
    /// <summary>
    /// Creates a feature-root GameObject parented under <paramref name="parent"/> and placed on
    /// <paramref name="layer"/> (when valid). Sub-mesh children inherit that layer.
    /// </summary>
    public static GameObject CreateRoot(Transform parent, string name, int layer)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, worldPositionStays: false);
        if (layer >= 0) root.layer = layer;
        return root;
    }

    /// <summary>
    /// Adds one mesh as a child of <paramref name="root"/> with a filter, renderer and a
    /// NavMesh-contributing MeshCollider. <paramref name="material"/> is shared onto the renderer.
    /// </summary>
    public static void AttachMesh(GameObject root, Mesh mesh, string childName, Material material)
    {
        if (mesh == null) return;
        var go = new GameObject(childName);
        go.transform.SetParent(root.transform, worldPositionStays: false);
        go.layer = root.layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    /// <summary>Attaches every non-null mesh in <paramref name="meshes"/> as its own child,
    /// numbered Sub0, Sub1, … — the multi-mesh spawn path.</summary>
    public static void AttachMeshes(GameObject root, Mesh[] meshes, Material material)
    {
        if (meshes == null) return;
        for (int i = 0; i < meshes.Length; i++)
            AttachMesh(root, meshes[i], $"Sub{i}", material);
    }
}
