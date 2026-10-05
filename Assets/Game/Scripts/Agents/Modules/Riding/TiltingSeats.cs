// Holds everybody sitting on a vehicle whose seats tilt on its seat markers, every frame, wearing
// each marker's FULL rotation.
//
// Seating parents a rider once. Netcode refuses a bare seat marker as a parent, so on every
// networked path (single player included: it is a host of one) the rider hangs off the vehicle's
// root NetworkObject with the marker's pose folded in at the moment they sat down -- MountModule
// for a player, NpcSeating for an NPC. That is right for a seat that never moves relative to the
// root, which is every chair, saddle and deck post in the game. It is wrong for a seat that rides
// on posed art: a monowheel's seats hang under its Body, which MonowheelLean rolls into turns and
// pitches onto the sand, so a rider folded in once stays upright where the seat was while the
// chassis tips away under them.
//
// Adding this component is the opt-in; a vehicle without one keeps its upright riders. It runs on
// every machine, from what each machine can see: a player through the MountModule (every peer
// replays the mount), an NPC as any AgentController parented under this root, held on the marker
// nearest to where it was seated. No message and no saved state -- the seat pose is re-derived
// every frame from the marker, which is presentation the vehicle already shows everybody.
using System.Collections.Generic;
using UnityEngine;

namespace SpaceGame.Agents
{
    // After the art is posed for the frame (MonowheelLean, LateUpdate at the default order) and
    // before MountedRiderPose (900) poses bones on the rider and MountModule (1000) frames the
    // camera on where the rider ended up.
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public sealed class TiltingSeats : MonoBehaviour
    {
        [Tooltip("Every seat marker an NPC can sit on. Each must move with the posed art (be under it).")]
        [SerializeField] private Transform[] seats = new Transform[0];

        [Tooltip("Where a seated NPC's origin sits relative to its marker, in the marker's space. " +
                 "Must match the offset the NPC was seated with (NpcPassenger, VesselSeats).")]
        [SerializeField] private Vector3 npcSeatOffset = Vector3.zero;

        [Tooltip("The mount whose player rider is held on its own seat point and offset. Found on " +
                 "this GameObject when empty; none means only NPCs are held.")]
        [SerializeField] private MountModule mount;

        private readonly List<Transform> visible = new();
        private readonly Dictionary<Transform, Transform> seatOf = new();

        private void Awake()
        {
            if (mount == null) mount = GetComponent<MountModule>();
        }

        private void OnEnable() => Refresh();

        // NpcSeating parents a seated NPC straight under this root (netcode allows no other parent),
        // and on a watching machine that parenting is all that arrives.
        private void OnTransformChildrenChanged() => Refresh();

        /// <summary>
        /// Work out who is seated where, from who is visibly under this root. Public so a test,
        /// which gets no transform-children messages, can ask for it.
        /// </summary>
        public void Refresh()
        {
            NpcSeating.CollectSeatedNpcs(transform, visible);

            seatOf.Clear();
            foreach (Transform npc in visible)
            {
                Transform seat = NearestSeat(npc.position);
                if (seat != null) seatOf.Add(npc, seat);
            }
        }

        // Nearest by where a rider on each seat would stand: the authority seats an NPC exactly
        // there, and a watching machine receives it within a replication step of it.
        private Transform NearestSeat(Vector3 position)
        {
            Transform nearest = null;
            float best = float.MaxValue;
            foreach (Transform seat in seats)
            {
                if (seat == null) continue;
                float distance = (seat.TransformPoint(npcSeatOffset) - position).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = seat;
            }
            return nearest;
        }

        private void LateUpdate() => Hold();

        /// <summary>Put every rider back on their seat for this frame. Public so an EditMode test can step it.</summary>
        public void Hold()
        {
            if (mount != null && mount.IsMounted && mount.MountedPlayerTransform != null && mount.ActiveSeatPoint != null)
                Place(mount.MountedPlayerTransform, mount.ActiveSeatPoint, mount.SeatOffset);

            foreach (KeyValuePair<Transform, Transform> seated in seatOf)
                if (seated.Key != null && seated.Value != null)
                    Place(seated.Key, seated.Value, npcSeatOffset);
        }

        private static void Place(Transform rider, Transform seat, Vector3 offset)
        {
            (Vector3 position, Quaternion rotation) = NpcSeating.SeatPoseIn(null, seat, offset, Vector3.zero);
            rider.SetPositionAndRotation(position, rotation);
        }
    }
}
