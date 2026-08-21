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
    ///
    /// A spawn point under a roof is measured differently, and has to be. The game's only spawn
    /// point is a child of ShipRV standing on the CargoBayFloor box, 0.91 m above the sand outside
    /// — so the terrain rules below, which exist to keep an outdoor spawn out of the ground, are
    /// all one small ship movement away from vetoing every position inside the bay and answering
    /// with the height of the sand instead. Under the sand is where the floor is. See
    /// <see cref="SpawnClearance"/>; indoors the floor is the authority and the heightmap is not
    /// consulted at all.
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

        [Tooltip("The volume a spawned body needs free above the ground to not be standing inside " +
                 "something. Mirrors the player capsule, which is 3 m tall.")]
        [SerializeField] private float standingHeight = 3f;

        [Tooltip("Radius of that volume. Slightly under the player capsule's 0.5 m so brushing a " +
                 "wall or a crate is not counted as being stuck in it.")]
        [SerializeField] private float standingRadius = 0.45f;

        [Tooltip("How far above this point the ground probe starts. The default clears any terrain " +
                 "relief around an outdoor spawn. An indoor spawn must lower it below the ceiling: " +
                 "the probe takes the first collider it meets, so a ray starting above the roof " +
                 "lands the player on the roof instead of on the floor beneath it.")]
        [SerializeField] private float probeHeight = 50f;

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
            // Asked once, up front, because it decides which rules the candidates below are judged
            // by — not per candidate, which would let two positions a metre apart be governed by
            // different laws.
            bool indoors = SpawnClearance.IsSheltered(transform.position);

            // One attempt past the scattered ones, on this point's own X/Z. Scattering exists so a
            // group does not spawn stacked, but it is a preference, not a requirement, and the
            // authored spot is the one position somebody actually looked at. Trying it before
            // giving up is the difference between "the bay is crowded" and "no spawn today".
            for (int attempt = 0; attempt <= attempts; attempt++)
            {
                Vector3 origin = attempt < attempts
                    ? GetRandomPoint(transform.position, spawnRadius)
                    : transform.position + Vector3.up * probeHeight;

                if (!TryGetGroundPoint(origin, out Vector3 groundPoint)) continue;
                if (!IsSpawnPointClear(groundPoint, ClearRadius, blockingLayers)) continue;

                // The check that makes "spawned inside the floor" impossible rather than unlikely,
                // and the only one here that measures the body being placed instead of the ground
                // under it.
                if (!SpawnClearance.HasRoomToStand(groundPoint, standingHeight, standingRadius))
                    continue;

                Vector3 candidate = groundPoint + Vector3.up * groundClearance;

                // Outdoors this catches a probe that hit something beneath the surface. Indoors it
                // catches the floor, which is the point of the whole exercise, so it is not asked.
                if (!indoors && IsUnderTerrain(candidate)) continue;

                spawnPosition = candidate;
                return true;
            }

            // Every probe missed. Outdoors the terrain is still a better answer than this point's
            // authored height, which nothing has verified. Indoors it is the worst answer available
            // — it is the ground UNDER the floor the spawn point stands on — so a sheltered spawn
            // point says "not yet" instead and the caller waits for the hull's colliders.
            if (!indoors && TerrainProbe.TryGetTerrainHeight(transform.position, out float terrainY))
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

        private void OnValidate()
        {
            standingHeight = Mathf.Max(0.1f, standingHeight);
            standingRadius = Mathf.Max(0.05f, standingRadius);
            groundClearance = Mathf.Max(0f, groundClearance);
            attempts = Mathf.Max(1, attempts);
        }

        private Vector3 GetRandomPoint(Vector3 center, float radius)
        {
            Vector2 randomCircle = Random.insideUnitCircle * radius;
            return new Vector3(center.x + randomCircle.x, center.y + probeHeight, center.z + randomCircle.y);
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
