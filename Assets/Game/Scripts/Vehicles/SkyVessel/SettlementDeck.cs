// The walkable deck of a settlement that moves — the Sky City between its moorings.
//
// A NavMeshAgent cannot ride a moving NavMesh: Unity has no way to move a NavMeshDataInstance, and
// re-adding one every frame resets every agent's path. So the deck does what a Strider house does with
// its crew (VesselSeats / CrewShift): its people are carried while it travels and walk when it stops.
//
//   • Departing (DriftRouteModule.UnderWay turns true): every resident standing on the deck is parked
//     where it stands — NpcSeating takes its feet but not its brain, so it still sees and shoots — and
//     parented under the hull's NetworkObject, which replicates. The NavMesh is withdrawn
//     (StaticNavMeshData off) and the population suspended, so nobody is spawned onto a mesh the deck
//     has left behind.
//   • Under way: the deck re-scans every parkScanInterval, so a resident a load restored onto the deck,
//     or one that walked aboard late, is parked too; the site marker follows the hull.
//   • Moored: the NavMesh is laid again at the hull's pose first, then everyone is unparked (and warped
//     onto it) and the population resumes.
//
// A teleport — a save restore putting the hull back where it was — re-lays the NavMesh if moored: the
// scene loaded it at the authored pose before the record moved the hull.
//
// Players are not this component's business; WalkerPlatformCarrier carries rigidbodies on the deck.
//
// Server only (Network.Decides): parking moves other entities, and their parenting replicates. On a
// client the NavMesh is never walked by anything this machine simulates.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Teleporting;
using SpaceGame.World;

namespace SpaceGame.Vehicles
{
    public class SettlementDeck : MonoBehaviour, ITeleportAware
    {
        [Header("Parts")]
        [Tooltip("The brain whose UnderWay says whether the deck is travelling.")]
        [SerializeField] private DriftRouteModule route;
        [Tooltip("The deck's own NavMesh, withdrawn while under way and laid again at each mooring.")]
        [SerializeField] private StaticNavMeshData navMesh;
        [Tooltip("Whose people live on the deck, and whose clock holds while it travels.")]
        [SerializeField] private SettlementPopulation population;
        [Tooltip("The settlement's site, re-published as the hull moves.")]
        [SerializeField] private WorldSiteMarker site;
        [Tooltip("The volume people must stand in to be carried — the WalkerPlatformCarrier's own.")]
        [SerializeField] private BoxCollider deckVolume;

        [Header("Timing")]
        [Tooltip("Seconds between looks for residents to park while under way.")]
        [SerializeField] private float parkScanInterval = 0.5f;
        [Tooltip("Seconds between re-publishing the site's position while under way.")]
        [SerializeField] private float siteRefreshInterval = 5f;
        [Tooltip("How far (m) to look for NavMesh under a resident put back on its feet.")]
        [SerializeField] private float navMeshReach = 4f;

        private readonly Dictionary<GameObject, NpcSeating> parked = new();
        private readonly List<EntityFaction> people = new(32);
        private NetworkObject hull;
        private bool underWay;
        private float nextScan;
        private float nextSiteRefresh;

        public bool UnderWay => underWay;
        public int ParkedCount => parked.Count;

        /// <summary>Builder only: the parts this deck coordinates.</summary>
        public void Configure(DriftRouteModule routeModule, StaticNavMeshData mesh, SettlementPopulation people,
                              WorldSiteMarker marker, BoxCollider volume)
        {
            route = routeModule;
            navMesh = mesh;
            population = people;
            site = marker;
            deckVolume = volume;
        }

        private void Awake() => hull = GetComponent<NetworkObject>();

        private void OnValidate()
        {
            parkScanInterval = Mathf.Max(0.05f, parkScanInterval);
            siteRefreshInterval = Mathf.Max(0.1f, siteRefreshInterval);
            navMeshReach = Mathf.Max(0.1f, navMeshReach);
        }

        private void Update()
        {
            if (!Network.Decides)
                return;

            Step(Time.time);
        }

        /// <summary>One look at the route: act on a departure or an arrival, then the voyage's chores.
        /// Public so a test can drive the deck without the player loop.</summary>
        public void Step(float now)
        {
            bool underWayNow = route != null && route.UnderWay;
            if (underWayNow != underWay)
            {
                underWay = underWayNow;
                if (underWay) Depart();
                else Moor();
            }

            if (!underWay)
                return;

            if (now >= nextScan)
            {
                nextScan = now + parkScanInterval;
                ParkEveryoneAboard();
            }

            if (now >= nextSiteRefresh)
            {
                nextSiteRefresh = now + siteRefreshInterval;
                RefreshSite();
            }
        }

        private void Depart()
        {
            if (population != null) population.SpawningSuspended = true;
            ParkEveryoneAboard();
            if (navMesh != null) navMesh.enabled = false;
            RefreshSite();
        }

        private void Moor()
        {
            LayNavMesh();
            UnparkEveryone();
            if (population != null) population.SpawningSuspended = false;
            RefreshSite();
        }

        public void OnTeleported(in TeleportMove move)
        {
            if (!Network.Decides)
                return;

            if (!underWay) LayNavMesh();
            RefreshSite();
        }

        // StaticNavMeshData adds its mesh at the pose it is enabled at, so cycling it is a re-lay.
        private void LayNavMesh()
        {
            if (navMesh == null)
                return;

            navMesh.enabled = false;
            navMesh.enabled = true;
        }

        private void RefreshSite()
        {
            if (site != null) site.Refresh();
        }

        private void ParkEveryoneAboard()
        {
            if (population == null)
                return;

            population.CollectPeople(people);
            foreach (EntityFaction person in people)
            {
                GameObject npc = person.gameObject;
                if (parked.ContainsKey(npc) || !IsStandingOnDeck(npc.transform)) continue;
                if (!npc.TryGetComponent(out AgentController controller) || controller.RidesAsPassenger) continue;
                Park(npc);
            }
        }

        // Standing on the deck: inside the volume, and not carried by anything else already — a
        // seated war-party rider docked alongside is its vessel's, not the city's.
        private bool IsStandingOnDeck(Transform npc)
        {
            if (npc.parent != null || deckVolume == null)
                return false;

            Vector3 local = deckVolume.transform.InverseTransformPoint(npc.position) - deckVolume.center;
            Vector3 half = deckVolume.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private void Park(GameObject npc)
        {
            var seating = new NpcSeating();
            seating.Suppress(npc);

            Transform t = npc.transform;
            Vector3 offset = transform.InverseTransformPoint(t.position);
            Vector3 euler = (Quaternion.Inverse(transform.rotation) * t.rotation).eulerAngles;
            NpcSeating.Attach(t, hull, transform, offset, euler);

            parked[npc] = seating;
        }

        private void UnparkEveryone()
        {
            foreach (KeyValuePair<GameObject, NpcSeating> entry in parked)
            {
                if (entry.Key == null)
                {
                    entry.Value.Abandon();
                    continue;
                }

                NpcSeating.Detach(entry.Key.transform);
                entry.Value.Restore(entry.Key, navMeshReach);
            }

            parked.Clear();
        }

        // The hull going away mid-voyage (a world unloading) must not leave anyone parked for good.
        private void OnDestroy()
        {
            foreach (NpcSeating seating in parked.Values)
                seating.Abandon();
            parked.Clear();
        }
    }
}
