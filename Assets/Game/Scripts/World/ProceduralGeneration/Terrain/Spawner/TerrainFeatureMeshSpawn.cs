using UnityEngine;

namespace SpaceGame.World
{
    /// <summary>
    /// Static helper that turns a generated feature mesh into scene GameObjects. Factored out of
    /// <see cref="TerrainFeatureSpawner"/> so the spawner stays small.
    ///
    /// The mesh becomes a child GameObject with a MeshFilter, a MeshRenderer and a
    /// <see cref="MeshCollider"/> on the feature layer. The MeshCollider is what makes the feature
    /// walkable: <c>WorldNavMeshBaker</c> collects collision, not render meshes, when it bakes the
    /// world NavMesh at author time.
    /// </summary>
    public static class TerrainFeatureMeshSpawn
    {
        /// <summary>
        /// Creates a feature-root GameObject parented under <paramref name="parent"/> and placed on
        /// <paramref name="layer"/> (when valid). The mesh child inherits that layer.
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

            // Assigning sharedMesh triggers a synchronous PhysX cook, which is the single most
            // expensive thing that happens when a chunk streams in — 110 ms for one chunk's 34
            // features, measured. Mesh cleaning and vertex welding are the costly stages of that
            // cook and exist to repair untrustworthy geometry; these meshes come from the feature
            // baker, which produces closed marching-cubes surfaces with no colocated vertices.
            // Cooking options must be set BEFORE sharedMesh or the cook has already happened.
            var collider = go.AddComponent<MeshCollider>();

            // Drop CookForFasterSimulation and WeldColocatedVertices, keep EnableMeshCleaning.
            // The first two are the stages that trade cook time for runtime simulation quality, and
            // this is static scenery that is never simulated. Mesh cleaning stays: it is what
            // removes degenerate triangles, and marching-cubes output can contain them — PhysX
            // rejects a mesh it cannot cook, and a rejected collider is not a slow frame, it is a
            // cliff the player walks through.
            collider.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning
                                    | MeshColliderCookingOptions.UseFastMidphase;
            collider.sharedMesh = mesh;
        }
    }
}
