using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.World
{
    /// <summary>
    /// Puts a pre-baked NavMesh into play where this object stands.
    ///
    /// For walkable structures that live outside the chunk scenes, and so outside the world bake —
    /// the Sky City is the first. The mesh is baked in the structure's own space, so it is added at
    /// this transform's position and rotation: moving the structure in its scene needs no re-bake.
    /// Scale is not applied (<c>AddNavMeshData</c> takes none), so the object must stand at unit
    /// scale — bake a scaled model's scale into its children instead.
    ///
    /// Runs on every machine, like <see cref="WorldNavMeshProvider"/>: every machine holds the same
    /// mesh, and only the one that simulates an agent paths on it.
    /// </summary>
    public class StaticNavMeshData : MonoBehaviour
    {
        // How far transform.lossyScale may drift from Vector3.one before it is treated as scaled.
        // Not zero: float composition of parent scales leaves values like 0.999999 on an
        // intentionally unscaled hierarchy, which must not trip the check.
        private const float ScaleEpsilon = 0.01f;

        [Tooltip("The NavMesh baked for this structure, in its local space.")]
        [SerializeField] private NavMeshData navMeshData;

        private NavMeshDataInstance instance;

        public NavMeshData Data => navMeshData;

        public void Configure(NavMeshData data) => navMeshData = data;

        private void OnEnable()
        {
            if (navMeshData == null)
            {
                Debug.LogError($"[StaticNavMeshData] '{name}' has no NavMeshData assigned. " +
                               "Nothing can navigate on it. Bake its NavMesh and assign the result.", this);
                return;
            }

            // Loud rather than degraded: AddNavMeshData takes no scale, so a scaled instance gets
            // the mesh baked at unit scale placed under a bigger (or smaller) collider, and nothing
            // else would ever say why the two silently disagree.
            if (Vector3.Distance(transform.lossyScale, Vector3.one) > ScaleEpsilon)
            {
                Debug.LogError($"[StaticNavMeshData] '{name}' is scaled ({transform.lossyScale:F2}), but " +
                               "AddNavMeshData ignores scale. The baked mesh will not match this instance's " +
                               "size. Bake the scale into the model instead and keep this transform at unit scale.",
                               this);
            }

            instance = UnityEngine.AI.NavMesh.AddNavMeshData(navMeshData, transform.position, transform.rotation);

            if (!instance.valid)
            {
                Debug.LogError($"[StaticNavMeshData] AddNavMeshData rejected '{navMeshData.name}' " +
                               $"on '{name}'. Nothing can navigate on it.", this);
            }
        }

        private void OnDisable()
        {
            if (instance.valid) instance.Remove();
            instance = default;
        }
    }
}
