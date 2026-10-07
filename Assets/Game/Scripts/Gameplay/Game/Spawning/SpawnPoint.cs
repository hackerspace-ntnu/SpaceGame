using System.Collections.Generic;
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
        public bool TryGetSpawnPoint(out Vector3 spawnPosition) =>
            TryGetSpawnPoint(null, 0f, out spawnPosition, out _);

        /// <summary>
        /// As above, but preferring a position clear of everyone already placed.
        ///
        /// <para>
        /// The scatter on its own does not solve four people arriving together, and on this game's
        /// only spawn point it barely helps at all: it is a child of ShipRV with a 0.5 m radius on a
        /// cargo bay floor, so every candidate it can produce is within arm's reach of every other
        /// one. Worse, the geometry test it is judged by deliberately ignores player bodies —
        /// <see cref="SpawnClearance.HasRoomToStand"/> has to, or the first player to stand there
        /// would block every position the point can offer and nobody else could spawn indoors at
        /// all. So the scatter genuinely did not know who was already there, and four players landed
        /// inside one another.
        /// </para>
        ///
        /// <para>
        /// Separation is a PREFERENCE layered over validity, never a new way to fail. A candidate
        /// that clears everyone is taken immediately; a valid but crowded one is remembered, and the
        /// roomiest of those is the answer when nothing better turns up. Refusing instead would be
        /// read by the caller as "the chunk has not loaded yet" — the one thing false means here —
        /// and it would wait out its whole timeout for a bay that is merely full. Two capsules
        /// briefly overlapping resolve apart on the next physics step; a player with no body does
        /// not.
        /// </para>
        ///
        /// <para>
        /// With no occupants and a separation of zero this is exactly the old behaviour: every
        /// candidate reports infinite clearance, so the first valid one is returned.
        /// </para>
        /// </summary>
        /// <param name="occupied">Positions to stay away from — bodies already standing, and
        /// positions handed out to players whose bodies do not exist yet. May be null.</param>
        /// <param name="separation">How far away is far enough to stop looking.</param>
        /// <param name="clearance">Distance from the answer to the nearest occupant, or infinity
        /// when there are none. Lets a caller choose between several spawn points.</param>
        public bool TryGetSpawnPoint(IReadOnlyList<Vector3> occupied, float separation,
                                     out Vector3 spawnPosition, out float clearance)
        {
            // Asked once, up front, because it decides which rules the candidates below are judged
            // by — not per candidate, which would let two positions a metre apart be governed by
            // different laws.
            bool indoors = SpawnClearance.IsSheltered(transform.position);

            bool found = false;
            spawnPosition = Vector3.zero;
            clearance = 0f;

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

                float gap = DistanceToNearest(candidate, occupied);

                // Clear of everybody. Nothing later in the loop can beat that, so stop.
                if (gap >= separation)
                {
                    spawnPosition = candidate;
                    clearance = gap;
                    return true;
                }

                // Valid but crowded: kept as the best answer so far rather than returned, and never
                // discarded — a position on top of somebody is still a position.
                if (!found || gap > clearance)
                {
                    found = true;
                    spawnPosition = candidate;
                    clearance = gap;
                }
            }

            if (found) return true;

            // Every probe missed. Outdoors the terrain is still a better answer than this point's
            // authored height, which nothing has verified. Indoors it is the worst answer available
            // — it is the ground UNDER the floor the spawn point stands on — so a sheltered spawn
            // point says "not yet" instead and the caller waits for the hull's colliders.
            if (!indoors && TerrainProbe.TryGetTerrainHeight(transform.position, out float terrainY))
            {
                spawnPosition = new Vector3(transform.position.x,
                                            terrainY + groundClearance,
                                            transform.position.z);
                clearance = DistanceToNearest(spawnPosition, occupied);
                return true;
            }

            // Nothing to stand on and nothing to measure: the chunk has not loaded. Say so.
            spawnPosition = Vector3.zero;
            clearance = 0f;
            return false;
        }

        /// <summary>
        /// How far <paramref name="position"/> is from the nearest occupant, or infinity when there
        /// are none — so an empty world reads as "as clear as it is possible to be" and every
        /// separation test passes without a special case.
        ///
        /// Measured in three dimensions rather than on the floor plane. Two decks of a ship are the
        /// only place that costs anything, and it costs a spawn point being slightly shy of a
        /// position above or below it, which is a far cheaper mistake than the flat version's:
        /// counting somebody on the deck below as standing on top of you.
        /// </summary>
        private static float DistanceToNearest(Vector3 position, IReadOnlyList<Vector3> occupied)
        {
            if (occupied == null || occupied.Count == 0) return float.PositiveInfinity;

            float nearest = float.PositiveInfinity;

            for (int i = 0; i < occupied.Count; i++)
            {
                float distance = Vector3.Distance(position, occupied[i]);
                if (distance < nearest) nearest = distance;
            }

            return nearest;
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
