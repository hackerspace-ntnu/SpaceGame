using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// Where inside a team's ship each of its players starts. Split off
    /// <c>VersusShipSpawner.cs</c> purely for readability, the way <c>MountModule</c> is split.
    /// </summary>
    public partial class VersusShipSpawner
    {
        /// <summary>How many seats each team has handed out so far.</summary>
        private readonly Dictionary<int, int> claimedByTeam = new();

        /// <summary>Reused between calls; players are placed on the server, one at a time.</summary>
        private readonly List<ShipSeat> seatBuffer = new();

        /// <summary>
        /// The pose the next player on this team starts at, or false when their ship is not
        /// standing yet.
        ///
        /// <para>
        /// Claiming is a side effect and is meant to be: the caller asks precisely once, at the
        /// moment it is about to put a body there, and two players handed the same seat is the one
        /// outcome worth engineering against.
        /// </para>
        /// </summary>
        public bool TryClaimSeat(int team, out Vector3 position, out Quaternion rotation) =>
            TryClaim(team, standing: false, out position, out rotation);

        /// <summary>
        /// As above, for a player coming back from the dead rather than starting the match: the
        /// pose is the seat's <see cref="ShipSeat.DismountPoint"/> when it has one. A respawned
        /// player is on their feet, and on this hull the seat marker itself is a seated pivot on
        /// the chair's cushion — standing a body there puts it a metre up the chair for physics to
        /// shove out in whatever direction it likes. The dismount point is the authored answer to
        /// exactly this question: where the occupant of that seat stands.
        /// </summary>
        public bool TryClaimRespawnPose(int team, out Vector3 position, out Quaternion rotation) =>
            TryClaim(team, standing: true, out position, out rotation);

        private bool TryClaim(int team, bool standing, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (!TryEnsureShip(team, out GameObject ship)) return false;

            claimedByTeam.TryGetValue(team, out int claimed);
            claimedByTeam[team] = claimed + 1;

            ResolveSeats(ship);

            if (seatBuffer.Count > 0)
            {
                // Wraps rather than refusing. More players than seats is a fair thing to have —
                // a twelve-strong team in a four-seat hull — and two capsules briefly sharing a
                // pose push apart on the next physics step, where a player with no body does not
                // recover on its own.
                ShipSeat seat = seatBuffer[claimed % seatBuffer.Count];
                Transform anchor = standing && seat.DismountPoint != null
                    ? seat.DismountPoint
                    : seat.transform;
                position = anchor.position;
                rotation = anchor.rotation;
                return true;
            }

            PlaceOnStandInRing(ship, team, claimed, out position, out rotation);
            return true;
        }

        /// <summary>
        /// Every <see cref="ShipSeat"/> on this ship, lowest order first.
        ///
        /// <para>
        /// Sorted with a stable comparison on the order alone, so seats left at the default zero
        /// keep the order they appear in the hierarchy — which is what somebody who never touched
        /// the field would expect, and means an unordered ship is still filled top to bottom rather
        /// than arbitrarily.
        /// </para>
        /// </summary>
        private void ResolveSeats(GameObject ship)
        {
            seatBuffer.Clear();
            ship.GetComponentsInChildren(includeInactive: true, seatBuffer);

            // List.Sort is unstable, so the hierarchy order the seats were collected in would not
            // survive a tie. Insertion sort over a handful of seats keeps it and costs nothing.
            for (int i = 1; i < seatBuffer.Count; i++)
            {
                ShipSeat seat = seatBuffer[i];
                int j = i - 1;

                while (j >= 0 && seatBuffer[j].Order > seat.Order)
                {
                    seatBuffer[j + 1] = seatBuffer[j];
                    j--;
                }

                seatBuffer[j + 1] = seat;
            }
        }

        /// <summary>
        /// Where players stand while the ship has no seats in it — a ring inside the hull, in the
        /// ship's own local space so it turns with the ship.
        ///
        /// <para>
        /// Sized to the team rather than to the hull, because the question it answers is "where do
        /// these people go", and a ring built for a different number would leave a four-player team
        /// bunched on one side of a ring built for twelve. Everyone faces the same way as the ship,
        /// which is at least a deliberate direction.
        /// </para>
        /// </summary>
        private void PlaceOnStandInRing(GameObject ship, int team, int claimed,
                                        out Vector3 position, out Quaternion rotation)
        {
            int seatCount = Mathf.Max(1, VersusSession.IsActive
                ? VersusSession.TeamSize
                : VersusRules.DefaultTeamSize);

            Vector3[] ring = ShipSpawnLayout.SeatRing(seatCount, config.SeatRingRadius,
                                                      config.SeatInteriorOffset);

            Transform hull = ship.transform;
            position = hull.TransformPoint(ring[claimed % ring.Length]);
            rotation = hull.rotation;

            if (claimed == 0)
                Debug.LogWarning($"[VersusShipSpawner] The ship for {VersusRules.TeamName(team)} has " +
                                 "no ShipSeat markers — starting its players on the stand-in ring " +
                                 "inside the hull.", this);
        }

        /// <summary>
        /// Drops the seat claims when this spawner goes away with its scene, so a second match in
        /// the same session does not start every team part-way through its seating.
        /// </summary>
        private void ForgetSeats()
        {
            claimedByTeam.Clear();
            seatBuffer.Clear();
            ForgetLandings();
        }
    }
}
