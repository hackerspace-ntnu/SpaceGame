using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay.Arrival;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Where a dead player comes back: inside their own ship. That is the rule of respawn — not a
    /// preference the spawn-point scatter is allowed to trade away for elbow room, and in versus
    /// specifically the TEAM's ship, never whichever hull happened to be found first.
    ///
    /// <para>
    /// This exists because <see cref="SpawnManager"/> answers a different question. Its spawn
    /// point is a free-standing anchor in the world — the arrival's impact site — so once the
    /// ship has been driven anywhere at all, "the roomiest spawn point" and "your ship" are
    /// different places, and a respawn through it stood players up on open sand at the coordinates
    /// the world began at.
    /// </para>
    ///
    /// <para>
    /// A false answer means no ship can take this player — a scene with no hull in it, or a versus
    /// player whose team is not resolved. The caller falls back to <see cref="SpawnManager"/>,
    /// which at least favours nobody's ship over the wrong one.
    /// </para>
    /// </summary>
    public static class ShipRespawn
    {
        /// <summary>
        /// Rotates story-world respawns through the seats so two players brought back together do
        /// not share one pose — the same wrap <see cref="VersusShipSpawner"/> keeps per team.
        /// </summary>
        private static int claims;

        public static bool TryGetPose(GameObject player, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (VersusSession.IsActive)
            {
                // Your team's ship, resolved through the one class that knows which hull belongs
                // to which team. On a refusal — no identity, no team yet, no spawner, ship not
                // standing — this deliberately does NOT fall through to the seat scan below: in
                // versus every hull on the ring carries seats, and an unfiltered scan is exactly
                // how a player respawns in the enemy's ship.
                var identity = player != null ? player.GetComponent<PlayerIdentity>() : null;

                return identity != null && identity.Team >= 0
                    && VersusShipSpawner.Instance != null
                    && VersusShipSpawner.Instance.TryClaimRespawnPose(identity.Team,
                                                                      out position, out rotation);
            }

            // The story world: the crew shares one hull, and its ShipSeat markers are the only
            // ones in the scene — they are what identifies "your ship" here. Found rather than
            // cached because the hull is persistent and streamed: it can be despawned, restored
            // from a save, or driven away between one death and the next.
            var seats = Object.FindObjectsByType<ShipSeat>(FindObjectsInactive.Exclude,
                                                           FindObjectsSortMode.None);
            if (seats.Length == 0) return false;

            var orders = new int[seats.Length];
            for (int i = 0; i < seats.Length; i++) orders[i] = seats[i].Order;

            int[] ordered = SeatOrdering.OrderedIndices(orders);
            ShipSeat seat = seats[ordered[SeatOrdering.SeatFor(claims++, seats.Length)]];

            // The dismount point is the seat's authored standing pose, on the deck. The marker
            // itself is a seated pivot on the chair's cushion — see TryClaimRespawnPose for why a
            // standing body must not be put there.
            Transform anchor = seat.DismountPoint != null ? seat.DismountPoint : seat.transform;
            position = anchor.position;
            rotation = anchor.rotation;
            return true;
        }
    }
}
