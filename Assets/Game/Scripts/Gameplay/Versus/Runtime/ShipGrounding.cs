using UnityEngine;
using SpaceGame.World.Safety;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Drops an authored X/Z point onto whatever is under it.
    ///
    /// <para>
    /// The heightmap is asked FIRST and the raycast is the fallback, which is the opposite of the
    /// obvious order and is the whole reason this is its own file. A downward ray takes the first
    /// collider it meets, so near a ship it takes the hull, near a building it takes the roof, and
    /// near a crate it takes the crate — all of which are answers, and none of which are the
    /// ground. <see cref="TerrainProbe"/> asks the terrain directly and cannot be shadowed by
    /// anything standing on it. <c>SpawnManager.TryFindOpenGround</c> orders its two passes the
    /// same way, for the same reason, after players were placed inside floors.
    /// </para>
    ///
    /// <para>
    /// The raycast still earns its place: the arena, the cave test and the personal test scenes
    /// have no terrain at all, and in those a collider is the only ground there is.
    /// </para>
    /// </summary>
    public static class ShipGrounding
    {
        /// <summary>
        /// How far down to look from the probe height. Generous, because the probe starts above the
        /// tallest thing in the arena and the ground may be a long way under that.
        /// </summary>
        private const float ProbeDistance = 500f;

        /// <summary>
        /// The height of the ground at <paramref name="groundXZ"/>, or false when nothing here can
        /// vouch for one.
        ///
        /// <para>
        /// False is a real answer and callers must treat it as "not yet" rather than "never": in
        /// the streamed world it means the chunk under this point has not loaded, so there is
        /// neither a heightmap nor a collider to measure, and the only correct response is to wait
        /// and ask again. This is the contract <c>SpawnPoint</c> established, and placing a ship on
        /// a guessed height is how things end up buried in terrain that appears a frame later.
        /// </para>
        /// </summary>
        public static bool TryResolveGround(Vector2 groundXZ, float probeHeight, out float groundY)
        {
            Vector3 at = new(groundXZ.x, 0f, groundXZ.y);

            if (TerrainProbe.TryGetTerrainHeight(at, out groundY))
                return true;

            Vector3 origin = new(groundXZ.x, probeHeight, groundXZ.y);

            // Triggers ignored for the same reason the spawn point ignores them: an interaction
            // volume, a pickup radius or a damage zone is not a surface, and landing a ship on one
            // puts it on ground that does not exist.
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, ProbeDistance,
                                ~0, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                return true;
            }

            groundY = 0f;
            return false;
        }

        /// <summary>
        /// The full pose a ship starts in — grounded, lifted by its hover clearance, and turned to
        /// the point's heading.
        /// </summary>
        public static bool TryResolvePose(ShipSpawnPoint point, float probeHeight, float groundClearance,
                                          out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.Euler(0f, point.Yaw, 0f);

            if (!TryResolveGround(point.GroundXZ, probeHeight, out float groundY))
            {
                position = Vector3.zero;
                return false;
            }

            position = new Vector3(point.GroundXZ.x, groundY + groundClearance, point.GroundXZ.y);
            return true;
        }
    }
}
