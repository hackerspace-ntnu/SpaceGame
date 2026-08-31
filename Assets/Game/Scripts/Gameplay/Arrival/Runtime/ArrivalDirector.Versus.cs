// Arriving in a versus match: one ship per team, launched together, each on its own arc, each
// landing on the point its arena authored for it.
//
// Split off ArrivalDirector.cs purely for readability, the way VersusShipSpawner.Seats.cs is. What
// is here is only what differs from a story world's single crash — where the hulls come from, which
// arcs they fly, and the fact that a team nobody is on still gets a ship.
using System.Collections;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay.Arrival
{
    public partial class ArrivalDirector
    {
        /// <summary>
        /// Puts one client into a seat in its team's ship, building the whole formation the first
        /// time anybody asks.
        ///
        /// <para>
        /// Server-only. <paramref name="attempt"/> reports whether this client now has a body, so
        /// the caller knows whether to fall back to placing them itself — see <see cref="Attempt"/>.
        /// </para>
        ///
        /// <para>
        /// The formation is built in one go rather than a hull at a time, because the ships have to
        /// exist before any of them can be launched together and because a team nobody is on has no
        /// player whose arrival would otherwise create it. That is also why the caller must have
        /// streamed the world around EVERY team's anchor before getting here — the ground under an
        /// empty team's landing site is measured just like anyone else's.
        /// </para>
        /// </summary>
        public IEnumerator SpawnIntoVersusArrival(ulong clientId, int team, Attempt attempt)
        {
            if (!Network.Server)
            {
                Debug.LogError("[Arrival] SpawnIntoVersusArrival called off the server.", this);
                yield break;
            }

            ArrivalFlight flight = null;
            float deadline = Time.time + seatResolveTimeout;

            // "Not yet" and "never" are different answers, as in the story world: a missing chunk
            // resolves itself if we wait, a missing spawner never will.
            while (flight == null)
            {
                flight = EnsureFormation(team, out bool fatal);

                if (flight != null) break;

                // Fatal means no waiting fixes it, so the world has had whatever arrival it is
                // going to get. Saying so here rather than leaving it pending stops every client
                // after this one repeating the same failure, and sends them straight to the
                // ordinary placement the caller falls back to.
                if (fatal)
                {
                    HasArrived = true;
                    yield break;
                }

                if (Time.time >= deadline)
                {
                    Debug.LogError($"[Arrival] No ground under one of the team landing sites after " +
                                   $"{seatResolveTimeout}s, so the formation cannot be flown. Client " +
                                   clientId + " will be placed in a ship on the ground instead.", this);
                    HasArrived = true;
                    yield break;
                }

                yield return null;
            }

            yield return SeatIntoFlight(clientId, flight, attempt);
        }

        /// <summary>
        /// Every team's hull, spawned at the top of its own arc, and this team's flight handed back.
        ///
        /// <para>
        /// All or nothing, on purpose. A formation that gave up on the one team whose chunk had not
        /// arrived would launch the rest and leave that side with no ship at all, in the mode where
        /// having a ship is the entire start — so a single unmeasurable landing site means "not
        /// yet" for the whole formation, and the caller waits and asks again.
        /// </para>
        ///
        /// <para>
        /// <paramref name="fatal"/> for the things waiting cannot fix: no spawner in the scene, no
        /// layout to place teams with, or a ship prefab that cannot carry anyone.
        /// </para>
        /// </summary>
        private ArrivalFlight EnsureFormation(int team, out bool fatal)
        {
            fatal = false;

            if (flights.TryGetValue(team, out ArrivalFlight existing) && existing.IsAlive)
                return existing;

            VersusShipSpawner spawner = VersusShipSpawner.Instance;

            if (spawner == null)
            {
                Debug.LogError("[Arrival] No VersusShipSpawner in the scene, so there is no layout to " +
                               "fly a formation onto.", this);
                fatal = true;
                return null;
            }

            if (!CanFly(out fatal)) return null;

            int teamCount = spawner.TeamCount;

            if (teamCount <= 0)
            {
                Debug.LogError("[Arrival] The arena resolved no team spawn points, so there is " +
                               "nowhere for the formation to land.", this);
                fatal = true;
                return null;
            }

            // Checked before the ground is measured, because waiting cannot conjure a point the
            // arena was never authored with — and a caller that treated this as "not yet" would
            // spend its whole timeout on a match whose team count outran its arena's.
            if (team < 0 || team >= teamCount)
            {
                Debug.LogError($"[Arrival] {VersusRules.TeamName(team)} has no landing site in an " +
                               $"arena laid out for {teamCount} team(s).", this);
                fatal = true;
                return null;
            }

            // Measured for every team BEFORE anything is spawned, so a formation is never left
            // half-built with some hulls in the sky and one team's site still unmeasured.
            var landings = new (Vector3 Position, float Yaw)[teamCount];

            for (int t = 0; t < teamCount; t++)
            {
                if (!spawner.TryLandingPose(t, out Vector3 position, out float yaw)) return null;

                landings[t] = (position, yaw);
            }

            for (int t = 0; t < teamCount; t++)
            {
                if (flights.TryGetValue(t, out ArrivalFlight standing) && standing.IsAlive) continue;

                ArrivalPath teamPath = ArrivalFormation.PathFor(path, t, teamCount, landings[t].Position,
                                                                landings[t].Yaw, formationSpread);

                ArrivalTrajectory.Evaluate(0f, teamPath, out Vector3 start, out Quaternion startRotation);

                // Through the spawner rather than spawning here: it owns the arena's ship prefab, the
                // team livery and the record of which hull belongs to which team, and a second place
                // that made team ships is a second place that can give a team two of them.
                GameObject ship = spawner.EnsureShipAt(t, start, startRotation);

                if (ship == null)
                {
                    fatal = true;
                    GroundWhatWasBuilt(landings);
                    return null;
                }

                if (Register(t, ship, teamPath, out fatal) == null)
                {
                    GroundWhatWasBuilt(landings);
                    return null;
                }
            }

            return flights.TryGetValue(team, out ArrivalFlight mine) ? mine : null;
        }

        /// <summary>
        /// Puts every hull the formation managed to make down on its own landing point, after a
        /// failure part-way through building the rest.
        ///
        /// <para>
        /// Without this those hulls are left hanging at the top of arcs that will never be flown —
        /// and because the spawner has already recorded them as their teams' ships, the ordinary
        /// placement the caller falls back to would then seat players in them, two kilometres up.
        /// A ship on the ground where it was supposed to land is a fair outcome for a match that
        /// lost its opening; a ship in the sky is not.
        /// </para>
        /// </summary>
        private void GroundWhatWasBuilt((Vector3 Position, float Yaw)[] landings)
        {
            foreach (ArrivalFlight flight in flights.Values)
            {
                if (!flight.IsAlive) continue;
                if (flight.Team < 0 || flight.Team >= landings.Length) continue;

                flight.Ship.transform.SetPositionAndRotation(
                    landings[flight.Team].Position,
                    Quaternion.Euler(0f, landings[flight.Team].Yaw, 0f));
            }
        }
    }
}
