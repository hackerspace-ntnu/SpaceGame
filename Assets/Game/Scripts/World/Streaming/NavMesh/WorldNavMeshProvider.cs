using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.World
{
    /// <summary>
    /// Puts the pre-baked world NavMesh into play, and that is all it does.
    ///
    /// This replaces the whole runtime bake subsystem the streamer used to carry. The NavMesh is
    /// present from the moment the game starts and never changes, which means agents in a chunk
    /// that has just streamed in are already standing on a valid mesh — no parking, no retry loop,
    /// no waiting for a bake to land.
    ///
    /// Lives in the persistent scene alongside the <see cref="WorldStreamer"/>.
    /// </summary>
    public class WorldNavMeshProvider : MonoBehaviour
    {
        [Tooltip("The baked world NavMesh. Produced by World/Streaming/Bake World NavMesh.")]
        [SerializeField] private WorldNavMeshAsset worldNavMesh;

        private NavMeshDataInstance instance;

        /// <summary>True once the baked mesh is live. False means nothing can path.</summary>
        public bool IsActive => instance.valid;

        private void OnEnable()
        {
            // Loud rather than degraded. The alternative — quietly falling back to a runtime bake —
            // is the 5-second freeze this whole change exists to remove, so it must not be
            // reachable by accident.
            if (worldNavMesh == null)
            {
                Debug.LogError("[WorldNavMeshProvider] No WorldNavMeshAsset assigned. " +
                               "Nothing can navigate. Run World/Streaming/Bake World NavMesh " +
                               "and assign the result.", this);
                return;
            }

            if (worldNavMesh.bakedData == null)
            {
                Debug.LogError($"[WorldNavMeshProvider] '{worldNavMesh.name}' has no baked data. " +
                               "Re-run World/Streaming/Bake World NavMesh.", this);
                return;
            }

            instance = UnityEngine.AI.NavMesh.AddNavMeshData(worldNavMesh.bakedData);

            if (!instance.valid)
            {
                Debug.LogError($"[WorldNavMeshProvider] AddNavMeshData rejected " +
                               $"'{worldNavMesh.name}'. Nothing can navigate.", this);
                return;
            }

            Debug.Log($"[WorldNavMeshProvider] world NavMesh live " +
                      $"({worldNavMesh.sourceCount} sources, baked {worldNavMesh.bakedAtUtc}, " +
                      $"voxel {worldNavMesh.settings.voxelSize:0.###} m)");
        }

        private void OnDisable()
        {
            if (instance.valid) instance.Remove();
            instance = default;
        }
    }
}
