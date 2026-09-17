// The seats on an NPC sky transport's deck.
//
// Seating is NpcSeating's, the same mechanics a caravan animal's saddle uses: the NPC loses its feet
// and keeps its brain (a seated Sky nomad shoots from the deck), is parented under the vessel's
// NetworkObject so netcode carries the arrangement to every machine, and gets back exactly what
// was taken when it steps off. Only the authority seats or unseats anybody.
//
// Presentation is per machine, like NpcPassenger's: a watching client is handed the parenting and
// nothing else, so the sitting pose (ChairPose — the nomads' animator has a seated idle) and the
// pairwise collision suspension are worked out here from whoever is visibly under the hull.
//
// Seats are positional: index i is the i-th marker under the Seats child. Capacity is the marker
// count, which is how a war party chooses its vessel.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Vehicles
{
    [DisallowMultipleComponent]
    public class VesselSeats : MonoBehaviour
    {
        [Tooltip("One marker per seat, on the deck. The count is this vessel's capacity.")]
        [SerializeField] private Transform[] seats = Array.Empty<Transform>();

        [Tooltip("Where a seated NPC's origin (its feet) sits relative to its marker.")]
        [SerializeField] private Vector3 seatOffset = Vector3.zero;

        [Tooltip("How far from the requested point an unseated NPC may be put onto the NavMesh.")]
        [SerializeField] private float navMeshReach = 6f;

        [Tooltip("Sits the seated NPCs down. Found on this GameObject when empty.")]
        [SerializeField] private ChairPose chairPose;

        private GameObject[] occupants = Array.Empty<GameObject>();
        private NpcSeating[] records = Array.Empty<NpcSeating>();
        private HealthComponent[] occupantHealth = Array.Empty<HealthComponent>();
        private Action[] deathHandlers = Array.Empty<Action>();

        // Per machine: who this machine is posing and holding clear of the hull.
        private readonly Dictionary<Transform, RiderCollisionIgnore> presented = new();
        private readonly List<Transform> visible = new();
        private readonly List<Transform> gone = new();

        public int Capacity => seats.Length;

        /// <summary>Living NPCs still seated. The authority's count; a watching machine reads 0.</summary>
        public int Occupied
        {
            get
            {
                int count = 0;
                for (int i = 0; i < occupants.Length; i++)
                    if (occupants[i] != null) count++;
                return count;
            }
        }

        public GameObject OccupantAt(int seat) => IsSeat(seat) ? occupants[seat] : null;

        private void Awake()
        {
            EnsureSeatArrays();
            if (chairPose == null) chairPose = GetComponent<ChairPose>();
        }

        private void OnEnable() => RefreshPresented();

        private void OnDisable() => ReleasePresented();

        private void OnTransformChildrenChanged() => RefreshPresented();

        /// <summary>Seat <paramref name="npc"/> in the first free seat. Returns the seat, or -1 when full or not the authority.</summary>
        public int Seat(GameObject npc)
        {
            EnsureSeatArrays();
            for (int i = 0; i < occupants.Length; i++)
                if (occupants[i] == null)
                    return Seat(i, npc) ? i : -1;
            return -1;
        }

        /// <summary>Seat <paramref name="npc"/> in <paramref name="seat"/>. Authority only.</summary>
        public bool Seat(int seat, GameObject npc)
        {
            EnsureSeatArrays();
            if (npc == null || !IsSeat(seat) || occupants[seat] != null) return false;
            if (!Network.Simulates(this)) return false;

            // A seat whose last occupant was despawned while sitting in it still holds that
            // occupant's record: its body claim and its death subscription.
            records[seat].Abandon();
            Vacate(seat);

            occupants[seat] = npc;
            records[seat].Suppress(npc);
            NpcSeating.Attach(npc.transform, GetComponent<NetworkObject>(), seats[seat], seatOffset, Vector3.zero);
            WatchForDeath(seat, npc);
            RefreshPresented();
            return true;
        }

        /// <summary>
        /// Take the NPC out of <paramref name="seat"/> and stand it on the NavMesh at
        /// <paramref name="worldPoint"/> (or at the point itself when there is none in reach), facing
        /// away from the vessel. Returns the NPC, or null when the seat was empty or this machine is
        /// not the authority.
        /// </summary>
        public GameObject Unseat(int seat, Vector3 worldPoint)
        {
            GameObject npc = OccupantAt(seat);
            if (npc == null || !Network.Simulates(this)) return null;

            // Unity will not reparent out of an inactive hierarchy; the NPC goes down with the hull.
            if (!gameObject.activeInHierarchy) return null;

            if (NavMesh.SamplePosition(worldPoint, out NavMeshHit hit, navMeshReach, NavMesh.AllAreas))
                worldPoint = hit.position;

            Vector3 away = worldPoint - transform.position;
            away.y = 0f;
            Quaternion facing = away.sqrMagnitude > Vector3.kEpsilon
                ? Quaternion.LookRotation(away, Vector3.up)
                : Quaternion.LookRotation(transform.forward, Vector3.up);

            Vacate(seat);
            NpcSeating.Detach(npc.transform);
            npc.transform.SetPositionAndRotation(worldPoint, facing);
            records[seat].Restore(npc, navMeshReach);
            RefreshPresented();
            return npc;
        }

        /// <summary>The first occupied seat, or -1.</summary>
        public int FirstOccupiedSeat()
        {
            for (int i = 0; i < occupants.Length; i++)
                if (occupants[i] != null) return i;
            return -1;
        }

        private bool IsSeat(int seat) => seat >= 0 && seat < occupants.Length;

        // Serialized arrays are only final once the Inspector (or a builder) has filled them, and
        // an EditMode test runs no Awake, so the per-seat records are sized on first use.
        private void EnsureSeatArrays()
        {
            if (occupants.Length == seats.Length) return;

            occupants = new GameObject[seats.Length];
            records = new NpcSeating[seats.Length];
            occupantHealth = new HealthComponent[seats.Length];
            deathHandlers = new Action[seats.Length];
            for (int i = 0; i < seats.Length; i++) records[i] = new NpcSeating();
        }

        private void Vacate(int seat)
        {
            if (occupantHealth[seat] != null) occupantHealth[seat].OnDeath -= deathHandlers[seat];
            occupantHealth[seat] = null;
            deathHandlers[seat] = null;
            occupants[seat] = null;
        }

        /// <summary>
        /// A passenger killed on the deck leaves its seat where it died: the seat stops counting it,
        /// and the body is handed back to physics (and its ragdoll) rather than carried home.
        /// A load restoring a dead NPC raises OnDeath too, and is not a death on this deck.
        /// </summary>
        private void WatchForDeath(int seat, GameObject npc)
        {
            if (!npc.TryGetComponent(out HealthComponent health)) return;

            occupantHealth[seat] = health;
            deathHandlers[seat] = () =>
            {
                if (health.IsRestoring || occupants[seat] != npc || !gameObject.activeInHierarchy) return;
                Vacate(seat);
                NpcSeating.Detach(npc.transform);
                records[seat].Restore(npc, navMeshReach);
                RefreshPresented();
            };
            health.OnDeath += deathHandlers[seat];
        }

        /// <summary>
        /// Let everyone go where they sit, for a hull being destroyed: reparenting out of it is no
        /// longer possible, so nobody is moved, but nobody is left as cargo either. Netcode lifts a
        /// spawned passenger to the scene root when the hull despawns, and without this it stood
        /// there for good with its feet switched off and a body claim nothing would ever release.
        /// The pilot unseats everyone properly before a despawn; this is the backstop.
        /// </summary>
        public void AbandonAll()
        {
            EnsureSeatArrays();
            for (int i = 0; i < occupants.Length; i++)
            {
                records[i].Abandon();
                Vacate(i);
            }
        }

        private void OnDestroy() => AbandonAll();

        // ── Presentation: every machine ─────────────────────────────────────────

        /// <summary>
        /// Pose and unhook whoever is visibly seated under the hull, and let go of anybody who left.
        /// Public so a test (which runs no transform-children messages) can ask for it.
        /// </summary>
        public void RefreshPresented()
        {
            NpcSeating.CollectSeatedNpcs(transform, visible);

            gone.Clear();
            foreach (Transform npc in presented.Keys)
                if (npc == null || !visible.Contains(npc)) gone.Add(npc);
            foreach (Transform npc in gone) Release(npc);

            foreach (Transform npc in visible)
            {
                if (presented.ContainsKey(npc)) continue;

                var collisions = new RiderCollisionIgnore();
                collisions.Apply(npc, transform);
                presented.Add(npc, collisions);
                if (chairPose != null) chairPose.PoseRider(npc);
            }
        }

        private void Release(Transform npc)
        {
            RiderCollisionIgnore collisions = presented[npc];
            presented.Remove(npc);

            // Restoring a pair needs both colliders active; a hull being deactivated has none to give.
            if (gameObject.activeInHierarchy) collisions.Restore();
            else collisions.Forget();

            if (npc != null && chairPose != null) chairPose.ReleaseRider(npc);
        }

        private void ReleasePresented()
        {
            gone.Clear();
            gone.AddRange(presented.Keys);
            foreach (Transform npc in gone) Release(npc);
        }

        private void OnValidate() => navMeshReach = Mathf.Max(0.5f, navMeshReach);

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.3f);
            foreach (Transform seat in seats)
                if (seat != null) Gizmos.DrawWireSphere(seat.TransformPoint(seatOffset), 0.4f);
        }
    }
}
