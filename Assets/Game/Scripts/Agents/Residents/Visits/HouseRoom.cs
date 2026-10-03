// The room a dwelling's door opens into, as the residents' society sees it: the spots a visitor can sit at and the
// doorway they come and go by, as places with indices of their own.
//
// A house interior is a scene of its own, loaded while somebody is inside, so its places cannot be among the
// settlement's gathered ones (those are gathered once, in one order on every machine, and trip points the server alone
// finds come last). They live in a range above every settlement place instead: SettlementSociety.Place(index) hands any
// index from PlaceBase up to this room. That is all a visitor needs to be an ordinary resident — the routine walks it to
// the place, ResidentSeating puts it on the Seat beside the spot, ResidentPresence holds the sit loop, Conversations talk
// between two spots of one circle — and none of those knows the place is in a house.
//
// Spots are authored in the interior scene like anywhere else (a SettlementSpot beside each Seat, one circle for the
// seats round the hearth). Stand points are measured on the interior's own NavMesh, on the deciding machine only; every
// other machine needs just the spot's use. Nothing here is saved: it is derived from the scene.
using System.Collections.Generic;
using SpaceGame.Core;
using SpaceGame.World;
using UnityEngine;
using UnityEngine.AI;

namespace SpaceGame.Agents.Residents
{
    public sealed class HouseRoom : MonoBehaviour
    {
        /// <summary>The first place index a room's places answer to: above anything a settlement gathers.</summary>
        public const int PlaceBase = 1 << 20;

        // A room's circles are numbered above any a settlement numbers, so two rooms' spots never read as one circle.
        private const int GroupBase = 1 << 10;
        // How close to the doorway stand the NavMesh must lie for the room to count as baked.
        private const float DoorProbeRadius = 2f;

        [Tooltip("Just inside the door, on the floor, +Z towards the door: where a visitor steps in and where it leaves from.")]
        [SerializeField] private Transform doorStand;

        private static HouseRoom active;

        private readonly List<SettlementSpot> spots = new();
        private readonly List<SettlementPlace> places = new();
        private bool settled;

        /// <summary>The loaded room, or null while no house interior is loaded.</summary>
        public static HouseRoom Active => active;

        /// <summary>The room's place for a global place index (<see cref="PlaceBase"/> and up); null when none is loaded.</summary>
        public static SettlementPlace PlaceAt(int index) => active != null ? active.Local(index) : null;

        /// <summary>The spot behind a room's seat place; null for the doorway or when no room is loaded.</summary>
        public static SettlementSpot SpotAt(int index)
        {
            int local = index - PlaceBase;
            return active != null && local >= 0 && local < active.spots.Count ? active.spots[local] : null;
        }

        /// <summary>One place per seat spot, in hierarchy order.</summary>
        public int SeatCount => spots.Count;

        /// <summary>The global index of seat <paramref name="seat"/>.</summary>
        public static int SeatPlace(int seat) => PlaceBase + seat;

        /// <summary>The global index of the doorway: the place a leaving visitor walks to.</summary>
        public int DoorPlace => PlaceBase + spots.Count;

        /// <summary>The doorway's stand point; the NavMesh-measured one once the room has settled.</summary>
        public Vector3 DoorPosition => places.Count > spots.Count ? places[spots.Count].Position : doorStand.position;

        public Quaternion DoorFacing => Quaternion.LookRotation(Vector3.ProjectOnPlane(-doorStand.forward, Vector3.up));

        /// <summary>True once the stand points have been measured on the interior's NavMesh (server only; clients never settle).</summary>
        public bool Settled => settled;

        private SettlementPlace Local(int index)
        {
            int local = index - PlaceBase;
            return local >= 0 && local < places.Count ? places[local] : null;
        }

        private void Awake() => Gather();

        private void OnEnable()
        {
            if (active != null && active != this)
                Debug.LogError($"[HouseRoom] '{name}' loaded while '{active.name}' is: a room's places share one index range, so " +
                               "only one house interior can be loaded at a time.", this);
            active = this;
        }

        private void OnDisable()
        {
            if (active == this) active = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active = null;

        // The interior's NavMesh is added when its surface enables, so the stands are measured from Update until they take.
        private void Update()
        {
            if (!settled && Network.Decides) Settle();
        }

        private void Gather()
        {
            var circles = new Dictionary<(Transform, string), int>();
            var seatsOfUse = new Dictionary<SpotUse, int>();
            foreach (SettlementSpot spot in GetComponentsInChildren<SettlementSpot>())
            {
                if (spot.Use == null)
                {
                    Debug.LogError($"[HouseRoom] '{spot.name}' has no SpotUse, so nobody can be placed there.", spot);
                    continue;
                }

                int group = 0;
                if (!string.IsNullOrEmpty(spot.Group))
                {
                    var circle = (spot.transform.parent, spot.Group);
                    if (!circles.TryGetValue(circle, out group)) circles[circle] = group = GroupBase + circles.Count + 1;
                }
                seatsOfUse.TryGetValue(spot.Use, out int seatOfUse);
                seatsOfUse[spot.Use] = seatOfUse + 1;

                PlaceKind kind = spot.Use.role == SpotRole.Gathering ? PlaceKind.Hearth : PlaceKind.Stroll;
                spots.Add(spot);
                places.Add(new SettlementPlace(kind, spot.Use, group, seatOfUse, spot.Position, spot.FacePoint, spot.HoldCue, spot.HasTarget));
            }

            places.Add(new SettlementPlace(PlaceKind.Door, null, 0, 0, doorStand.position, doorStand.position + doorStand.forward));
        }

        private void Settle()
        {
            if (!NavMesh.SamplePosition(doorStand.position, out NavMeshHit door, DoorProbeRadius, NavMesh.AllAreas)) return;

            places[spots.Count].Resolve(door.position, true);
            for (int i = 0; i < spots.Count; i++)
            {
                bool usable = SettlementPlaces.TryStand(spots[i].Position, false, door.position, out Vector3 stand, out string why);
                places[i].Resolve(usable ? stand : spots[i].Position, usable);
                if (!usable)
                    Debug.LogWarning($"[HouseRoom] {spots[i].name} is unusable and nobody will sit there: {why}.", spots[i]);
            }
            settled = true;
        }
    }
}
