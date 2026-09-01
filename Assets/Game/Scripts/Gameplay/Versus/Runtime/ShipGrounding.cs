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
        /// How far below the probe height the fallback ray still looks, beyond the probe height
        /// itself.
        ///
        /// <para>
        /// Expressed as a margin UNDER the origin rather than as a total length, which is the whole
        /// point. A fixed length silently means "only ground above <c>probeHeight - length</c>": the
        /// arrival probes from 600 m and a 500 m ray reached y=100, in a world whose surface sits at
        /// roughly 100-120 m — so every dip below 100 m was answered with "no ground here" and the
        /// hull was left on whatever the trajectory had guessed. Anchoring the reach to the origin
        /// means raising the probe can never shorten what it can see.
        /// </para>
        /// </summary>
        private const float ProbeReachBelowZero = 1000f;

        /// <summary>
        /// How far above a hull's own roof a collision probe starts. A margin, not a height: the
        /// probe is anchored to the hull it is measuring, so a ray can never begin inside the ship
        /// and find the ship's own shell as the first surface under it.
        /// </summary>
        private const float ProbeStartClearance = 2f;

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
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                probeHeight + ProbeReachBelowZero, ~0, QueryTriggerInteraction.Ignore))
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

        /// <summary>
        /// Where a hull can actually sit down near <paramref name="preferredXZ"/>: a spot level
        /// enough for it, with the height its ORIGIN needs so the hull's belly rests on the ground
        /// rather than in it.
        ///
        /// <para>
        /// The three things that were wrong before this existed, in the order they bite:
        /// the ground was sampled under the hull's origin alone, so a 23-metre hull on a slope was
        /// grounded against the one patch it was least likely to touch; the height added was an
        /// authored constant rather than the hull's own belly depth, so it meant something different
        /// on every prefab; and nothing checked the result, so a ship left hanging looked exactly
        /// like a ship landed.
        /// </para>
        ///
        /// <para>
        /// False means "not yet", as everywhere else here: in a streamed world the chunks under the
        /// footprint have not loaded, and the caller is expected to wait a frame and ask again.
        /// </para>
        /// </summary>
        public static bool TryResolveHullLanding(Vector2 preferredXZ, float yaw, GameObject hullPrefab,
                                                 float probeHeight, in LandingTolerance tolerance,
                                                 out Vector3 position)
        {
            position = Vector3.zero;

            Vector2 extents = ShipHull.Footprint(hullPrefab);

            bool Sample(Vector2 at, out float y) => TryResolveGround(at, probeHeight, out y);

            if (!LevelGroundSearch.TryFind(preferredXZ, yaw, extents, tolerance.MaxGroundSpread,
                                           tolerance.SearchRadius, tolerance.RingStep,
                                           Sample, out Vector2 groundXZ, out float groundY))
                return false;

            // The hull rests on the HIGHEST ground it spans, because it cannot tilt: its Rigidbody
            // freezes rotation and the arrival's settle leaves it level. Sitting it on the average,
            // or on the ground under its middle, buries the high side.
            position = new Vector3(groundXZ.x,
                                   groundY + ShipHull.BellyDrop(hullPrefab) + tolerance.BellyClearance,
                                   groundXZ.y);
            return true;
        }

        /// <summary>
        /// The surface a hull's own collision would come to rest on at <paramref name="at"/>, read
        /// from PHYSICS rather than from the heightmap, and ignoring the hull itself.
        ///
        /// <para>
        /// The counterpart to <see cref="TryResolveGround"/>, and deliberately not a variant of it.
        /// Everything above answers "how high is the terrain here" from the heightmap, which is the
        /// right question while an arc is being PLANNED: no hull exists yet to shadow a ray, and a
        /// chunk that has not streamed has to be able to say so. It is the wrong question once a
        /// hull has arrived, because the heightmap is a parallel model of the world and the player
        /// stands on the colliders. When the two disagree the arrival believed the model — the
        /// descent ended on a height the heightmap vouched for, the landing check re-read the same
        /// heightmap, agreed with itself, and reported a clean landing for a ship left in the sky.
        /// A check cannot share its input with the thing it is checking.
        /// </para>
        ///
        /// <para>
        /// <paramref name="ignoring"/> is the hull, and excluding it is why this cannot be a plain
        /// raycast: a ray dropped onto a landed ship hits the ship. Its children go with it — the
        /// salvage parts, the hatches and the boarding stair are all colliders of its own that a
        /// naive probe would call the ground.
        /// </para>
        ///
        /// <para>
        /// Everything carrying a Rigidbody goes too — see <see cref="IsWorldSurface"/>. The world's
        /// surface is its STATIC collision, and a hull that came down beside a nomad, a mount or its
        /// own crew must not be told the ground is wherever their heads are.
        /// </para>
        ///
        /// <para>
        /// False means physics has nothing to offer here — an interior with no floor beneath, a
        /// chunk that has not loaded, open air — and never "the ground is at zero".
        /// </para>
        /// </summary>
        public static bool TryResolveCollisionGround(Vector2 at, float fromHeight, float reach,
                                                     GameObject ignoring, out float groundY)
        {
            groundY = 0f;

            Vector3 origin = new(at.x, fromHeight, at.y);

            // RaycastAll rather than the nearest hit: the nearest thing under a probe started above
            // a hull IS the hull, so a single-hit query would answer with the ship's own roof and
            // ground the ship on itself.
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, Mathf.Max(0f, reach),
                                                   ~0, QueryTriggerInteraction.Ignore);

            bool found = false;
            float highest = float.NegativeInfinity;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null) continue;
                if (ignoring != null && hit.collider.transform.IsChildOf(ignoring.transform)) continue;
                if (!IsWorldSurface(hit.collider)) continue;

                if (hit.point.y > highest)
                {
                    highest = hit.point.y;
                    found = true;
                }
            }

            if (!found) return false;

            groundY = highest;
            return true;
        }

        /// <summary>
        /// Whether a hit is the world itself rather than something standing on it. The world is its
        /// STATIC collision: terrain, buildings, the rock a hull can legitimately come down on top
        /// of. A Rigidbody of any kind means the collider belongs to a body the world is holding up.
        ///
        /// <para>
        /// Deliberately NOT the rule <c>WalkerGround.IsLooseBody</c> uses, which excludes only
        /// non-kinematic bodies, and the difference is the point: a walker is MEANT to stand on a
        /// kinematic body — the deck of a mount, a moving platform — whereas a hull deciding where
        /// the world's surface is must not. Almost nothing in this project that stands on the ground
        /// is dynamic. Agents, mounts and a seated rider held by <c>CarriedBody</c> are all
        /// KINEMATIC, so the narrower rule excluded nearly nothing it was written to exclude.
        /// </para>
        /// <para>
        /// Measured: an arrival came down beside a nomad, the probe took the NPC's collider 2.81 m
        /// above the terrain it was standing on as the ground, and <c>ArrivalDirector.SetDown</c>
        /// lifted a 60-tonne hull onto its head. The hull is then PERSISTED there, so the world
        /// opens with its ship hanging in the air for good.
        /// </para>
        /// </summary>
        private static bool IsWorldSurface(Collider collider) => collider.attachedRigidbody == null;

        /// <summary>
        /// The same measurement as <see cref="TryMeasureLanding"/>, but against the world physics
        /// actually simulates. This is the one that decides whether a hull is standing on anything.
        ///
        /// <para>
        /// <paramref name="reach"/> has to exceed the descent's own start altitude, not merely the
        /// height of the terrain: the failure this exists to catch is a hull that finished its arc a
        /// kilometre up, and a probe that could only see a few hundred metres would report "no
        /// ground" for exactly the hull that most needs putting down.
        /// </para>
        /// </summary>
        public static bool TryMeasureLandingAgainstCollision(Vector3 position, float yaw,
                                                             GameObject hull, float reach,
                                                             float bellyDrop, out float airGap)
        {
            airGap = 0f;

            if (hull == null) return false;

            Vector2 extents = ShipHull.Footprint(hull);

            // Started above the hull's own roof rather than at an authored height, so the probe
            // clears the ship whatever altitude the descent left it at — an authored ceiling is
            // exactly what a hull two kilometres up is above.
            float from = ShipHull.TopOf(hull) + ProbeStartClearance;

            bool Sample(Vector2 at, out float y) =>
                TryResolveCollisionGround(at, from, reach + (from - position.y), hull, out y);

            HullFootprint.Ground ground = HullFootprint.Measure(
                new Vector2(position.x, position.z), yaw, extents, Sample);

            if (!ground.Any) return false;

            airGap = position.y - bellyDrop - ground.Highest;
            return true;
        }

        /// <summary>
        /// Whether a hull standing at <paramref name="position"/> is genuinely on the ground, and by
        /// how much it misses.
        ///
        /// <para>
        /// The check the arrival owes itself. Every step above is arithmetic done before the descent
        /// flies, against ground that streams in and out while it does; the wreck is then persisted
        /// wherever the trajectory ended, so an unnoticed miss is permanent. Answering here rather
        /// than asserting means the caller can correct it and say so.
        /// </para>
        /// </summary>
        public static bool TryMeasureLanding(Vector3 position, float yaw, GameObject hull,
                                             float probeHeight, float bellyDrop, out float airGap)
        {
            airGap = 0f;

            Vector2 extents = ShipHull.Footprint(hull);

            bool Sample(Vector2 at, out float y) =>
                TryResolveGround(at, probeHeight, out y);

            HullFootprint.Ground ground = HullFootprint.Measure(
                new Vector2(position.x, position.z), yaw, extents, Sample);

            if (!ground.Any) return false;

            // Positive: the belly is above the highest ground under the hull and it is hanging.
            // Negative: the hull is into the ground and physics will shove it out.
            airGap = position.y - bellyDrop - ground.Highest;
            return true;
        }
    }
}
