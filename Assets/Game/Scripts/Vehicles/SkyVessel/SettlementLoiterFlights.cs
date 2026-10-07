// Residents of a drifting airborne settlement (the Sky City) taking off to circle it for a while and landing
// back on its deck — the city's own swarm (user, 2026-10-07: "the flyers in the sky cities can also swarm
// around the sky cities").
//
// Any time the city is moored (not only early in the mooring), while a player is near enough to see it and
// fewer than maxAloft are up, every checkInterval a roll sends an idle resident up (NpcFlightModule.TakeOffNow
// — an order, so no launch delay, no flight distance; idle means no target, no goal, not offstage — a resident
// asleep only for distance (SimulationRange) still goes, and its flight wakes it). It flies an orbit station on
// the city itself (NpcAviator.Escort, anchor = this transform), so the orbit follows the hull if the city sets
// off while it is up: that is the swarm under way (nobody takes off under way: SettlementDeck has parked the
// residents aboard). Now and then a second resident goes up as its wingman. Once its time is out and the city
// is moored, it flies out to an approach fix and lands on a deck pad: a point on the promenade NavMesh
// reachable from reachableFrom with clear sky above, chosen once in the hull's own frame. It never lands under
// way — the deck's NavMesh is withdrawn then — it circles on until the next mooring; one that is due, moored,
// past maxLoiterSeconds and still finds no clear pad is put down on the ground below, said once.
//
// Server only (Network.Decides); holds no saved state. A resident in the air is withheld from the save for the
// flight and given back when it lands alive (NpcFlightModule's SaveScopeHold) — saved mid-flight, it is gone after
// a load and the population refills it (D4).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.World;

namespace SpaceGame.Vehicles
{
    public class SettlementLoiterFlights : MonoBehaviour
    {
        [SerializeField] private DriftRouteModule route;
        [SerializeField] private SettlementPopulation population;

        [Tooltip("A point on the walkable promenade: only deck reachable from here is landed on (PromenadeAnchor).")]
        [SerializeField] private Transform reachableFrom;

        [Header("Launch")]
        [Tooltip("Seconds between looks for someone to send up.")]
        [SerializeField, Min(1f)] private float checkInterval = 10f;

        [Tooltip("Chance a look sends someone up.")]
        [SerializeField, Range(0f, 1f)] private float launchChance = 0.5f;

        [Tooltip("Most residents circling at once.")]
        [SerializeField, Min(0)] private int maxAloft = 3;

        [Tooltip("Residents tried per look before giving up until the next (a crowded deck refuses a take-off).")]
        [SerializeField, Min(1)] private int candidatesPerCheck = 3;

        [Tooltip("Only with a player this close (flat), metres: a swarm nobody sees is waste.")]
        [SerializeField, Min(1f)] private float audienceDistance = 900f;

        [Tooltip("Chance a take-off brings a wingman up with it.")]
        [SerializeField, Range(0f, 1f)] private float pairChance = 0.35f;

        [Header("Orbit")]
        [Tooltip("Orbit radius range round the city's pivot, metres: inside the escorts' ring, inside the population's count radius.")]
        [SerializeField] private Vector2 orbitRadius = new Vector2(90f, 110f);

        [Tooltip("Orbit height above the city's pivot, metres: above the escorts and the city's own superstructure.")]
        [SerializeField] private float orbitHeight = 100f;

        [Tooltip("Each flier's orbit height varies by up to this, metres.")]
        [SerializeField, Min(0f)] private float orbitHeightJitter = 5f;

        [Tooltip("Orbit speed, 0..1 of the craft's top speed.")]
        [SerializeField, Range(0.1f, 1f)] private float orbitSpeed = 0.55f;

        [Tooltip("Seconds a flier circles before it heads in to land, rolled per flier.")]
        [SerializeField] private Vector2 loiterSeconds = new Vector2(60f, 150f);

        [Tooltip("Seconds aloft after which a flier due to land at a moored city that finds no clear pad is put down on the ground below instead.")]
        [SerializeField, Min(1f)] private float maxLoiterSeconds = 600f;

        [Tooltip("A wingman's station on its lead's craft, in its heading frame (x right, y up, z ahead), metres.")]
        [SerializeField] private Vector3 wingmanOffset = new Vector3(-16f, 2.5f, -14f);

        [Header("Landing")]
        [Tooltip("How many deck pads to find.")]
        [SerializeField, Min(1)] private int padCount = 4;

        [Tooltip("Promenade points sampled to find them.")]
        [SerializeField, Min(1)] private int padCandidates = 32;

        [Tooltip("How far from reachableFrom to sample, metres.")]
        [SerializeField, Min(1f)] private float padSampleRadius = 90f;

        [Tooltip("Pads at least this far apart, metres.")]
        [SerializeField, Min(0f)] private float padSpacing = 25f;

        [Tooltip("Clear sky a pad needs: this radius ...")]
        [SerializeField, Min(0.5f)] private float padClearRadius = 8f;

        [Tooltip("... this high above it, metres.")]
        [SerializeField, Min(1f)] private float padClearHeight = 40f;

        [Tooltip("How far a candidate may be from the NavMesh it is snapped to, metres.")]
        [SerializeField, Min(0.1f)] private float padNavMeshReach = 4f;

        [Tooltip("The approach starts this far out from the pad, away from the city's centre, metres ...")]
        [SerializeField, Min(1f)] private float approachFixDistance = 220f;

        [Tooltip("... and this high above it: steep enough (over NpcFlightSettings.ApproachSlope) that the craft starts its straight-in at once, never a spiral down among the houses.")]
        [SerializeField, Min(0f)] private float approachFixHeight = 50f;

        [Tooltip("Radius of the craft swept down the approach to check it is clear, metres.")]
        [SerializeField, Min(0.5f)] private float approachClearRadius = 6f;

        [Tooltip("What blocks an approach: the hulls and the world.")]
        [SerializeField] private LayerMask approachMask = ~0;

        [SerializeField] private PhysicsGroundProbe probe = new PhysicsGroundProbe();

        private enum Leg { Orbit, Final, Landing }

        private sealed class Loiterer
        {
            public NpcFlightModule Flight;
            public FlightStation Station;
            public Loiterer Lead;
            public float LandAfter;
            public float GiveUpAt;
            public int Pad = -1;
            public Leg Leg;
        }

        private readonly List<Loiterer> aloft = new();
        private readonly List<Vector3> pads = new();   // in this transform's frame
        private readonly List<EntityFaction> people = new();
        private readonly List<Transform> players = new();
        private float nextCheck;
        private bool padsSought;

        public int Aloft => aloft.Count;

        public void Configure(DriftRouteModule drift, SettlementPopulation residents, Transform anchor)
        {
            route = drift;
            population = residents;
            reachableFrom = anchor;
        }

        private void Update()
        {
            if (!Network.Decides || route == null || population == null) return;
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;

            Step(Time.time);
        }

        /// <summary>One look: bring the fliers along their legs, then perhaps send someone up.</summary>
        public void Step(float now)
        {
            aloft.RemoveAll(flier => flier.Flight == null || !flier.Flight.InFlight || !flier.Flight.Aviator.Aloft);
            foreach (Loiterer flier in aloft) Release(flier);
            foreach (Loiterer flier in aloft) Advance(flier, now);
            population.AwayResidents = CountOutside(population.CountRadius);

            SessionPlayers.Collect(players);
            bool audience = LoiterRules.AnyWithin(transform.position, players, audienceDistance);
            if (!LoiterRules.MayLaunch(route.UnderWay, aloft.Count, maxAloft, audience)) return;
            if (Random.value > launchChance) return;

            Loiterer lead = TryLaunch(now, null);
            if (lead != null && aloft.Count < maxAloft && Random.value <= pairChance) TryLaunch(now, lead);
        }

        /// <summary>
        /// A wingman whose lead is gone or heading in to land takes an orbit of its own: following its lead down
        /// to the deck would fly it into the city.
        /// </summary>
        private void Release(Loiterer flier)
        {
            if (flier.Lead == null || (aloft.Contains(flier.Lead) && flier.Lead.Leg == Leg.Orbit)) return;

            flier.Lead = null;
            flier.Station = OrbitStation(flier.Flight.Aviator, flier.Flight);
            if (flier.Leg == Leg.Orbit) flier.Flight.Aviator.Escort(flier.Station);
        }

        private void Advance(Loiterer flier, float now)
        {
            NpcAviator aviator = flier.Flight.Aviator;

            // The city set off under a flier heading for, or coming down on, a pad: the pad is moving away and
            // its NavMesh is withdrawn. Back on station (the orbit goes with the city), the pad freed.
            if (route.UnderWay && flier.Pad >= 0)
            {
                flier.Pad = -1;
                aviator.Escort(flier.Station);
                flier.Leg = Leg.Orbit;
                return;
            }

            switch (flier.Leg)
            {
                case Leg.Orbit:
                    // Under way it circles on: launched during a passage, it lands at the next mooring.
                    if (now < flier.LandAfter || route.UnderWay) return;
                    if (TryChoosePad(flier, out Vector3 fix))
                    {
                        aviator.CruiseTo(fix, 1f);
                        flier.Leg = Leg.Final;
                        return;
                    }
                    if (now < flier.GiveUpAt) return;
                    Debug.LogWarning($"[SettlementLoiterFlights] '{flier.Flight.name}' found no clear deck pad on " +
                                     $"'{name}' in {maxLoiterSeconds} s; putting it down on the ground below.", this);
                    aviator.LandAt(aviator.transform.position);
                    flier.Leg = Leg.Landing;
                    return;

                case Leg.Final:
                    if (!aviator.ReachedCruisePoint) return;
                    aviator.LandOnDeck(transform.TransformPoint(pads[flier.Pad]));
                    flier.Leg = Leg.Landing;
                    return;
            }
        }

        /// <summary>Fliers out beyond <paramref name="radius"/> of the city's pivot: the residents its population cannot see.</summary>
        private int CountOutside(float radius)
        {
            int outside = 0;
            foreach (Loiterer flier in aloft)
                if ((flier.Flight.transform.position - transform.position).sqrMagnitude > radius * radius) outside++;
            return outside;
        }

        /// <summary>A free pad whose straight-in approach is clear right now, and the fix that approach starts from.</summary>
        private bool TryChoosePad(Loiterer flier, out Vector3 fix)
        {
            fix = default;
            FindPads();
            for (int i = 0; i < pads.Count; i++)
            {
                if (aloft.Exists(other => other.Pad == i)) continue;

                Vector3 pad = transform.TransformPoint(pads[i]);
                Vector3 start = LoiterRules.ApproachFix(pad, transform.position, approachFixDistance, approachFixHeight);
                Vector3 path = pad - start;
                float clearUntil = path.magnitude - padClearRadius;
                if (Physics.SphereCast(start, approachClearRadius, path.normalized, out _, clearUntil, approachMask,
                                       QueryTriggerInteraction.Ignore))
                    continue;

                flier.Pad = i;
                fix = start;
                return true;
            }
            return false;
        }

        /// <summary>Send one idle resident up — onto an orbit, or as <paramref name="lead"/>'s wingman.</summary>
        private Loiterer TryLaunch(float now, Loiterer lead)
        {
            population.CollectPeople(people);
            LoiterRules.Shuffle(people);

            int tried = 0;
            foreach (EntityFaction person in people)
            {
                if (tried >= candidatesPerCheck) break;
                if (!IsIdle(person, out NpcFlightModule flight)) continue;
                tried++;

                Vector3 outward = person.transform.position - transform.position;
                if (!flight.TakeOffNow(outward, out NpcAviator aviator)) continue;

                var flier = new Loiterer
                {
                    Flight = flight,
                    Lead = lead,
                    Station = lead != null ? WingmanStation(lead, flight) : OrbitStation(aviator, flight),
                    LandAfter = now + Random.Range(loiterSeconds.x, Mathf.Max(loiterSeconds.x, loiterSeconds.y)),
                    GiveUpAt = now + maxLoiterSeconds,
                };
                aviator.Escort(flier.Station);
                aloft.Add(flier);
                return flier;
            }
            return null;
        }

        private FlightStation OrbitStation(NpcAviator aviator, NpcFlightModule flight)
        {
            float radius = Random.Range(orbitRadius.x, Mathf.Max(orbitRadius.x, orbitRadius.y));
            float height = orbitHeight + Random.Range(-orbitHeightJitter, orbitHeightJitter);
            float topSpeed = aviator.TryGetComponent(out IMovementMotor motor) ? motor.TopSpeed : 0f;
            float degreesPerSecond = LoiterRules.OrbitDegreesPerSecond(orbitSpeed * topSpeed, radius) *
                                     (Random.value < 0.5f ? -1f : 1f);
            float phase = LoiterRules.SpreadPhase(aloft.Count, Mathf.Max(1, maxAloft)) + Random.Range(0f, 360f / Mathf.Max(1, maxAloft));
            return FlightStation.Orbit(transform, radius, height, degreesPerSecond, phase, flight.GetInstanceID());
        }

        private FlightStation WingmanStation(Loiterer lead, NpcFlightModule flight) =>
            FlightStation.Fixed(lead.Flight.Aviator.transform, wingmanOffset, flight.GetInstanceID());

        /// <summary>
        /// On deck, doing nothing: not offstage, no target, no goal, not flying or on a sortie. Asleep only for distance
        /// (Dormant) still counts: the swarm is for a player watching from up to audienceDistance, far past the range
        /// that keeps residents awake, and a resident in flight is never dormant (DistanceDormant), so it wakes.
        /// </summary>
        private static bool IsIdle(EntityFaction person, out NpcFlightModule flight)
        {
            flight = null;
            if (person == null || !person.TryGetComponent(out flight) || flight.InFlight || flight.OnSortie) return false;
            if (person.TryGetComponent(out AgentController controller) && (controller.Offstage || controller.RidesAsPassenger)) return false;
            if (person.TryGetComponent(out AgentTargeting targeting) && targeting.HasTarget) return false;
            return !person.TryGetComponent(out AgentGoal goal) || !goal.HasGoal;
        }

        /// <summary>The deck pads, found once, in this transform's frame (valid at every mooring).</summary>
        private void FindPads()
        {
            if (padsSought || reachableFrom == null) return;
            if (!NavMesh.SamplePosition(reachableFrom.position, out NavMeshHit anchor, padNavMeshReach, NavMesh.AllAreas)) return;
            padsSought = true;

            var candidates = new List<Vector3>(padCandidates);
            for (int i = 0; i < padCandidates; i++)
            {
                Vector2 offset = Random.insideUnitCircle * padSampleRadius;
                Vector3 at = reachableFrom.position + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(at, out NavMeshHit hit, padNavMeshReach, NavMesh.AllAreas))
                    candidates.Add(hit.position);
            }

            foreach (Vector3 pad in LoiterRules.PickPads(candidates, padCount, padSpacing,
                         point => NavMeshReach.CanWalk(anchor.position, point) &&
                                  probe.IsClear(point, padClearRadius, padClearHeight)))
                pads.Add(transform.InverseTransformPoint(pad));

            if (pads.Count == 0)
                Debug.LogWarning($"[SettlementLoiterFlights] '{name}' found no clear, reachable deck pad among " +
                                 $"{candidates.Count} promenade samples; its fliers will be put down on the ground.", this);
        }
    }

    /// <summary>The pure rules of <see cref="SettlementLoiterFlights"/>.</summary>
    public static class LoiterRules
    {
        /// <summary>
        /// Moored (at any point of the mooring: a flier still up when the city sets off circles on and lands at the
        /// next one), room for another, and someone to see it.
        /// </summary>
        public static bool MayLaunch(bool underWay, int aloft, int maxAloft, bool audience) =>
            !underWay && aloft < maxAloft && audience;

        /// <summary>Is any of <paramref name="players"/> within <paramref name="distance"/> (flat) of <paramref name="centre"/>?</summary>
        public static bool AnyWithin(Vector3 centre, IReadOnlyList<Transform> players, float distance)
        {
            foreach (Transform player in players)
            {
                if (player == null) continue;
                Vector3 offset = player.position - centre;
                offset.y = 0f;
                if (offset.sqrMagnitude <= distance * distance) return true;
            }
            return false;
        }

        /// <summary>Degrees per second that carry a flier round a circle of <paramref name="radius"/> at <paramref name="speed"/>.</summary>
        public static float OrbitDegreesPerSecond(float speed, float radius) =>
            radius > 0f ? speed / radius * Mathf.Rad2Deg : 0f;

        /// <summary>Where on the circle flier <paramref name="index"/> of <paramref name="slots"/> starts, degrees.</summary>
        public static float SpreadPhase(int index, int slots) => index * 360f / Mathf.Max(1, slots);

        /// <summary>
        /// The start of a straight-in approach to <paramref name="pad"/>: <paramref name="distance"/> out from it, away
        /// from <paramref name="centre"/> (the city's pivot), and <paramref name="height"/> above it.
        /// </summary>
        public static Vector3 ApproachFix(Vector3 pad, Vector3 centre, float distance, float height)
        {
            Vector3 outward = pad - centre;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward;
            return pad + outward * distance + Vector3.up * height;
        }

        /// <summary>
        /// Up to <paramref name="count"/> of <paramref name="candidates"/>, in order, that <paramref name="usable"/>
        /// accepts and that lie at least <paramref name="spacing"/> from every pad already picked.
        /// </summary>
        public static List<Vector3> PickPads(IReadOnlyList<Vector3> candidates, int count, float spacing,
                                             System.Func<Vector3, bool> usable)
        {
            var picked = new List<Vector3>(count);
            foreach (Vector3 candidate in candidates)
            {
                if (picked.Count >= count) break;
                if (picked.Exists(pad => Vector3.Distance(pad, candidate) < spacing)) continue;
                if (usable(candidate)) picked.Add(candidate);
            }
            return picked;
        }

        /// <summary>Fisher–Yates, so the same resident is not always the first asked.</summary>
        public static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
