using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Where the team ships start, and the handful of measurements the spawner needs to put them
    /// there. One asset per arena.
    ///
    /// <para>
    /// An asset rather than values in code because arena placement is the thing a versus mode gets
    /// tuned on, over and over, and a tuning pass that costs a recompile is a tuning pass that
    /// happens less often. It is an asset rather than components in the scene because the same
    /// layout has to be describable before any scene exists — the lobby chooses a match, and the
    /// scene carrying it loads afterwards.
    /// </para>
    ///
    /// <para>
    /// The asset is the AUTHORED default. Anything set through <see cref="VersusShipSpawns"/> at
    /// runtime wins over it, and nothing here is ever written to: an asset mutated in play mode
    /// keeps the change in the project afterwards, and in a build keeps nothing at all.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "VersusShipSpawnConfig", menuName = "Versus/Ship Spawn Config")]
    public class VersusShipSpawnConfig : ScriptableObject
    {
        public enum SpawnLayout
        {
            /// <summary>Teams evenly spaced on a circle, each facing the middle. Two numbers, any team count.</summary>
            Ring,

            /// <summary>One hand-placed point per team.</summary>
            Explicit
        }

        [Header("Layout")]
        [SerializeField] private SpawnLayout layout = SpawnLayout.Ring;

        [Header("Ring")]
        [Tooltip("The middle of the arena, in world X/Z. Every team is placed around this.")]
        [SerializeField] private Vector2 ringCenterXZ = Vector2.zero;

        [Tooltip("How far each team's ship starts from the centre, in metres.")]
        [SerializeField] private float ringRadius = 120f;

        [Header("Explicit")]
        [Tooltip("One row per team, numbered from zero. Every team in the match needs exactly one; " +
                 "rows for teams the match does not have are ignored.")]
        [SerializeField] private ShipSpawnPoint[] explicitPoints = System.Array.Empty<ShipSpawnPoint>();

        [Header("Placement")]
        [Tooltip("How far above the point the ground probe starts. Only used where there is no " +
                 "terrain heightmap to ask, so it has to clear the tallest thing in the arena — a " +
                 "probe starting under a roof lands the ship on whatever is beneath it.")]
        [SerializeField] private float probeHeight = 200f;

        [Tooltip("Gap left between the ship's LOWEST point and the ground under it. Small on " +
                 "purpose: this is the hull sitting down, not the hover height it flies at. The " +
                 "belly depth is measured off the prefab, so this number means the same thing on " +
                 "every ship.")]
        [SerializeField] private float shipGroundClearance = 0.05f;

        [Tooltip("How far the low corner of a hull may hang before the spot is rejected as too " +
                 "steep to land on. A ship's Rigidbody freezes rotation, so it always rests level " +
                 "on the highest ground it spans — on a slope the rest of it is simply in the air.")]
        [SerializeField] private float maxGroundSpread = 1f;

        [Tooltip("How far from the authored point a flatter landing spot may be looked for. Zero " +
                 "lands on the authored point whatever the ground does there.")]
        [SerializeField] private float landingSearchRadius = 60f;

        [Tooltip("Spacing of the search rings. Smaller finds tighter shelves and costs more probes.")]
        [SerializeField] private float landingSearchStep = 12f;

        [Header("Seats")]
        [Tooltip("Radius of the stand-in seat ring inside the hull, used until the ship prefab " +
                 "carries real ShipSeat markers. Keep it inside the hull's interior.")]
        [SerializeField] private float seatRingRadius = 1.6f;

        [Tooltip("Centre of that ring in the ship's local space. Y lifts players off the deck the " +
                 "way a spawn point's ground clearance does.")]
        [SerializeField] private Vector3 seatInteriorOffset = new(0f, 1.2f, 0f);

        public float ProbeHeight => probeHeight;

        public float ShipGroundClearance => shipGroundClearance;

        /// <summary>How level the ground has to be, and how far to look for it. See <see cref="LandingTolerance"/>.</summary>
        public LandingTolerance Landing =>
            new(maxGroundSpread, landingSearchRadius, landingSearchStep, shipGroundClearance);

        public float SeatRingRadius => seatRingRadius;

        public Vector3 SeatInteriorOffset => seatInteriorOffset;

        /// <summary>
        /// The points this asset describes for a match of <paramref name="teamCount"/> teams, or
        /// false with a reason fit to log.
        ///
        /// Ring mode cannot fail for a sane team count — it computes the points rather than reading
        /// them — so every refusal here comes from explicit mode, where a human typed the rows.
        /// </summary>
        public bool TryPoints(int teamCount, out IReadOnlyList<ShipSpawnPoint> points, out string refusal)
        {
            if (layout == SpawnLayout.Ring)
                return VersusShipSpawns.TryRing(ringCenterXZ, ringRadius, teamCount, out points, out refusal);

            bool valid = ShipSpawnLayout.TryValidateExplicit(explicitPoints, teamCount,
                                                             out ShipSpawnPoint[] ordered, out refusal);
            points = ordered;
            return valid;
        }

        private void OnValidate()
        {
            ringRadius = Mathf.Max(0f, ringRadius);
            probeHeight = Mathf.Max(1f, probeHeight);
            shipGroundClearance = Mathf.Max(0f, shipGroundClearance);
            maxGroundSpread = Mathf.Max(0f, maxGroundSpread);
            landingSearchRadius = Mathf.Max(0f, landingSearchRadius);
            landingSearchStep = Mathf.Max(0.01f, landingSearchStep);
            seatRingRadius = Mathf.Max(0f, seatRingRadius);
        }
    }
}
