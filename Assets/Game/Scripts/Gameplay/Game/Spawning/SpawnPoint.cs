using UnityEngine;
using SpaceGame.World.Safety;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Scatters spawn positions around itself and vouches for them.
    ///
    /// The old version could not fail: after twenty missed raycasts it returned its own
    /// transform.position unvalidated, which is the one position guaranteed not to have been
    /// checked against anything. In the streamed world those twenty raycasts miss for exactly one
    /// reason — the chunk has not loaded, so there are no colliders to hit — and that fallback then
    /// dropped the player at a fixed height into ground that was about to appear above them.
    ///
    /// So the answer is now allowed to be "not yet". A caller that gets false must wait and ask
    /// again rather than spawn blind; there is no position this can return that is better than
    /// waiting a frame for the world to exist.
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        [SerializeField] private float spawnRadius = 10f;
        [SerializeField] private LayerMask blockingLayers;

        [Tooltip("Lift above the sampled ground point. The player capsule's bottom sits ~1m below " +
                 "the prefab pivot, so spawning exactly on the surface buries half the collider and " +
                 "PhysX sometimes resolves that penetration downwards, dropping the player through " +
                 "the terrain.")]
        [SerializeField] private float groundClearance = 1.2f;

        [Tooltip("How many scattered positions to try before falling back to this point's own X/Z.")]
        [SerializeField] private int attempts = 20;

        private const float ProbeHeight = 50f;
        private const float ProbeDistance = 100f;
        private const float ClearRadius = 1.5f;

        /// <summary>
        /// This point's authored position, for deciding which chunks to load. Never a spawn
        /// position: nothing has verified there is ground at it, which is the whole reason
        /// <see cref="TryGetSpawnPoint"/> exists. Everything this scatters lands within
        /// <see cref="spawnRadius"/> of it, so it names the right chunks.
        /// </summary>
        public Vector3 Anchor => transform.position;

        /// <summary>
        /// A ground-backed position near this point, or false when the world here cannot yet
        /// vouch for one.
        /// </summary>
        public bool TryGetSpawnPoint(out Vector3 spawnPosition)
        {
            for (int i = 0; i < attempts; i++)
            {
                Vector3 randomPoint = GetRandomPoint(transform.position, spawnRadius);

                if (!TryGetGroundPoint(randomPoint, out Vector3 groundPoint)) continue;
                if (!IsSpawnPointClear(groundPoint, ClearRadius, blockingLayers)) continue;

                Vector3 candidate = groundPoint + Vector3.up * groundClearance;
                if (IsUnderTerrain(candidate)) continue;

                spawnPosition = candidate;
                return true;
            }

            // Every scattered probe missed. If the terrain here can still be measured, it is a
            // better answer than this point's authored height, which nothing has verified.
            if (TerrainProbe.TryGetTerrainHeight(transform.position, out float terrainY))
            {
                spawnPosition = new Vector3(transform.position.x,
                                            terrainY + groundClearance,
                                            transform.position.z);
                return true;
            }

            // Nothing to stand on and nothing to measure: the chunk has not loaded. Say so.
            spawnPosition = Vector3.zero;
            return false;
        }

        private Vector3 GetRandomPoint(Vector3 center, float radius)
        {
            Vector2 randomCircle = Random.insideUnitCircle * radius;
            return new Vector3(center.x + randomCircle.x, center.y + ProbeHeight, center.z + randomCircle.y);
        }

        /// <summary>
        /// Ignores triggers. Without that an interaction volume, a pickup radius or a damage zone
        /// counts as ground, and the spawn is placed on a surface that does not exist.
        /// </summary>
        private static bool TryGetGroundPoint(Vector3 origin, out Vector3 hitPoint)
        {
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, ProbeDistance,
                                ~0, QueryTriggerInteraction.Ignore))
            {
                hitPoint = hit.point;
                return true;
            }

            hitPoint = Vector3.zero;
            return false;
        }

        private static bool IsSpawnPointClear(Vector3 position, float radius, LayerMask blockingLayers)
        {
            return !Physics.CheckSphere(position, radius, blockingLayers);
        }

        /// <summary>
        /// Catches a probe that hit something beneath the surface — a collider left under the
        /// terrain, or a stale chunk's geometry. Silent when no terrain covers the position, since
        /// then there is no surface to be under.
        /// </summary>
        private static bool IsUnderTerrain(Vector3 position)
        {
            return TerrainProbe.TryGetTerrainHeight(position, out float terrainY)
                   && position.y < terrainY;
        }
    }
}
