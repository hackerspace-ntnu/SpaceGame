using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The arithmetic behind where team ships and their seats sit. No scene, no colliders, no
    /// terrain — every method here answers in the abstract and the grounding pass lowers the result
    /// onto the world afterwards.
    ///
    /// <para>
    /// Kept in <c>SpaceGame.Versus.Core</c> beside <see cref="VersusRules"/> for the same reason
    /// that class gives: this is where the rules that decide a match's fairness live, and they are
    /// worth being able to test without an Editor.
    /// </para>
    /// </summary>
    public static class ShipSpawnLayout
    {
        /// <summary>
        /// Teams spaced evenly around a circle, each ship facing the middle.
        ///
        /// <para>
        /// The symmetric layout, and the default one. Every team is the same distance from the
        /// centre and from its neighbours, so the start is fair by construction rather than fair
        /// because somebody measured it — which is what makes this the layout a mode can ship on
        /// before anyone has had time to playtest the alternative.
        /// </para>
        /// </summary>
        public static ShipSpawnPoint[] Ring(Vector2 centerXZ, float radius, int teamCount)
        {
            if (teamCount <= 0) return System.Array.Empty<ShipSpawnPoint>();

            var points = new ShipSpawnPoint[teamCount];

            for (int team = 0; team < teamCount; team++)
            {
                float degrees = team * 360f / teamCount;
                float radians = degrees * Mathf.Deg2Rad;

                Vector2 outward = new(Mathf.Sin(radians), Mathf.Cos(radians));

                // Facing the centre is facing the opposite way to the offset, which is exactly half
                // a turn from the bearing the offset was built on — so the yaw is the angle plus
                // 180 and needs no second trig call to find it.
                points[team] = new ShipSpawnPoint(team, centerXZ + outward * radius,
                                                  Wrap360(degrees + 180f));
            }

            return points;
        }

        /// <summary>
        /// Seat poses inside a hull, in the ship's own local space, for a ship that has no
        /// <c>ShipSeat</c> markers on it yet.
        ///
        /// <para>
        /// A ring rather than a row because a ring needs no opinion about which way the hull is
        /// long, and this is standing in for seats nobody has placed. A single occupant is put on
        /// the offset itself: a "ring" of one is just an arbitrary shove to one side.
        /// </para>
        /// </summary>
        public static Vector3[] SeatRing(int seatCount, float radius, Vector3 interiorOffset)
        {
            if (seatCount <= 0) return System.Array.Empty<Vector3>();
            if (seatCount == 1) return new[] { interiorOffset };

            var seats = new Vector3[seatCount];

            for (int seat = 0; seat < seatCount; seat++)
            {
                float radians = seat * (Mathf.PI * 2f / seatCount);
                seats[seat] = interiorOffset + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * radius;
            }

            return seats;
        }

        /// <summary>
        /// The point a team starts on, or false when the layout has nothing for that team.
        ///
        /// Refuses rather than indexing, because the team index can be one a config was never
        /// authored for — a four-team asset used in a two-team match, or a team that arrived over
        /// the wire from a build with different rules. A missing ship reported is recoverable; an
        /// <see cref="System.IndexOutOfRangeException"/> inside a spawn coroutine is a player who
        /// never gets a body and no clue as to why.
        /// </summary>
        public static bool TryPointForTeam(IReadOnlyList<ShipSpawnPoint> points, int team,
                                           out ShipSpawnPoint point)
        {
            point = default;
            if (points == null) return false;

            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Team != team) continue;

                point = points[i];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks a hand-authored set of points covers every team exactly once, and orders it by
        /// team so callers get a predictable list rather than whatever order somebody typed.
        ///
        /// <para>
        /// Validated on load rather than at the point of use, because both failures are silent
        /// otherwise and both are worse later: a missing team means one side of a versus match has
        /// no ship, and a duplicate means whichever row happened to be found first wins while the
        /// other sits in the asset looking authoritative. <paramref name="refusal"/> is a sentence
        /// fit to log as-is, naming the team at fault.
        /// </para>
        ///
        /// <para>
        /// Rows for teams beyond <paramref name="teamCount"/> are ignored, not refused. An asset
        /// authored for the largest match the arena supports should still run a smaller one.
        /// </para>
        /// </summary>
        public static bool TryValidateExplicit(IReadOnlyList<ShipSpawnPoint> points, int teamCount,
                                               out ShipSpawnPoint[] ordered, out string refusal)
        {
            ordered = System.Array.Empty<ShipSpawnPoint>();
            refusal = null;

            if (teamCount <= 0)
            {
                refusal = "the match has no teams.";
                return false;
            }

            if (points == null || points.Count == 0)
            {
                refusal = "no spawn points are defined.";
                return false;
            }

            var byTeam = new ShipSpawnPoint[teamCount];
            var filled = new bool[teamCount];

            for (int i = 0; i < points.Count; i++)
            {
                int team = points[i].Team;
                if (team < 0 || team >= teamCount) continue;

                if (filled[team])
                {
                    refusal = $"{VersusRules.TeamName(team)} has more than one spawn point.";
                    return false;
                }

                byTeam[team] = points[i];
                filled[team] = true;
            }

            for (int team = 0; team < teamCount; team++)
            {
                if (filled[team]) continue;

                refusal = $"{VersusRules.TeamName(team)} has no spawn point.";
                return false;
            }

            ordered = byTeam;
            return true;
        }

        /// <summary>Keeps a yaw in [0, 360) so two headings that mean the same thing compare equal.</summary>
        private static float Wrap360(float degrees)
        {
            float wrapped = degrees % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }
    }
}
