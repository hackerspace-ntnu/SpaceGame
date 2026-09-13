using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// One ship on its way down: the hull, who is allowed to sit in it, and the arc it flies.
    ///
    /// <para>
    /// Split out of <see cref="ArrivalDirector"/> when the arrival stopped being one ship. A story
    /// world has a single flight and a versus match has one per team, all flown at once and all
    /// landing together — so everything that used to be a field on the director and was implicitly
    /// "the ship" now has to be said out loud, once per hull.
    /// </para>
    ///
    /// <para>
    /// A plain class, not a component. It exists only on the server, which is the only machine that
    /// flies anything: the hull reaches everyone else through the ship prefab's own
    /// <c>ClientNetworkTransform</c>, and who is sitting in it through <see cref="SeatedRider"/>.
    /// Making it a MonoBehaviour would put server-only bookkeeping on a replicated object.
    /// </para>
    /// </summary>
    internal class ArrivalFlight
    {
        /// <summary>The key a story world's single flight is filed under — it belongs to no team.</summary>
        public const int NoTeam = -1;

        public ArrivalFlight(int team, GameObject ship, SeatedRider seating, in ArrivalPath path)
        {
            Team = team;
            Ship = ship;
            Seating = seating;
            Path = path;
        }

        /// <summary>Which team arrives in this hull, or <see cref="NoTeam"/> in a story world.</summary>
        public int Team { get; }

        public GameObject Ship { get; }

        public SeatedRider Seating { get; }

        /// <summary>The arc this hull flies. Each team's differs — see <see cref="ArrivalFormation"/>.</summary>
        public ArrivalPath Path { get; }

        /// <summary>How many seats this flight has handed out, which is also the next seat's claim.</summary>
        public int Claimed { get; set; }

        /// <summary>True once this hull has been sent down its arc, so it cannot be sent twice.</summary>
        public bool Launched { get; set; }

        /// <summary>
        /// True once the descent has settled, grounded and parked this hull. The gap between
        /// <see cref="Launched"/> and this is a ship in the air — which is exactly the state the
        /// director's watchdog and the pre-save grounding exist to never leave a hull in.
        /// </summary>
        public bool Landed { get; set; }

        /// <summary>
        /// Whether the hull still exists. A destroyed GameObject compares equal to null, so a flight
        /// whose ship went away with its scene answers false here rather than throwing a frame later
        /// inside the descent.
        /// </summary>
        /// <summary>
        /// The one thing that keeps ground loaded for this flight while it is in the air: a
        /// marker at the landing point registered with the streamer. See
        /// <c>ArrivalDirector.HoldStreamingAtLandingSite</c>.
        /// </summary>
        public Transform LandingAnchor { get; set; }

        /// <summary>
        /// Crew whose own chunk anchoring is suspended for the length of the flight. Handed back
        /// one by one as they leave, and all together when the hull is down.
        /// </summary>
        public List<Transform> SuspendedCrew { get; } = new();

        public bool IsAlive => Ship != null && Seating != null;
    }
}
