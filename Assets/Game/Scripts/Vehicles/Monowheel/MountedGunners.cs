// The gunners aboard a double monowheel: one NPC spawned into each of the mount's VesselSeats when
// it spawns, the way NpcPassenger spawns the driver into the saddle.
//
// Server-side only: spawned from OnNetworkSpawn on the server, or from Start when there is no
// session at all (Network.Simulates guards the public calls). Each gunner is stamped into the
// mount's world-sim group before its network spawn (seat i is rider seat i + 1; the saddle is
// seat 0), so its loadout roll differs from the driver's and from the other gunners', and it
// counts as one of the group's fighters. It is seated after the spawn -- the same rule as the
// walking city's crew -- and seating replicates through NpcSeating's parenting, so clients need no
// message. A gunner killed in its seat
// is let go by VesselSeats; a hurt one stays seated and fires. When the double itself is killed,
// MonowheelWreck calls ReleaseGunners: every gunner still seated is stood on the ground beside the
// wreck through VesselSeats.Unseat (the path CrewShift puts crew ashore by) and fights on foot.
// A released gunner is no longer this mount's: it is one of the group's fighters, taken down with the
// group, so the wreck's own despawn must not take it too.
//
// Gunners this component spawned are its to take down. Netcode lifts a seated NPC to the scene root
// before any hook on the mount runs (NetworkSpawnManager.OnDespawnObject), so the earliest place to
// act is OnNetworkDespawn, which still runs before the mount's own despawn reaches the clients.
// OnDestroy covers a mount that never network-despawned: unnetworked, never spawned, or taken
// down by a session shutdown. The world sim does better still: gunners are the group's fighters
// and are despawned before any member, so nothing is lifted at all.
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;

namespace SpaceGame.Vehicles.Monowheel
{
    [RequireComponent(typeof(VesselSeats))]
    [DisallowMultipleComponent]
    public sealed class MountedGunners : NetworkBehaviour, ICrewedSeats
    {
        /// <summary>Rider seat of VesselSeats seat 0: the saddle (seat 0) is the driver's, NpcPassenger's.</summary>
        private const int FirstGunnerRiderSeat = 1;

        [Tooltip("Who mans each seat. Spawned at start when spawnOnStart is on.")]
        [SerializeField] private GameObject gunnerPrefab;

        [SerializeField] private bool spawnOnStart = true;

        [Tooltip("How far past its seat, away from the mount's centre, a released gunner is stood -- " +
                 "far enough to clear the wheel it sat inside.")]
        [SerializeField] private float stepOffDistance = 3f;

        [Tooltip("Finds the ground under a step-off point, so a gunner with no NavMesh in reach is " +
                 "stood on the ground rather than at seat height.")]
        [SerializeField] private PhysicsGroundProbe groundProbe = new PhysicsGroundProbe();

        private readonly List<GameObject> gunners = new();
        private VesselSeats seats;

        /// <inheritdoc />
        public bool IsStoodDown { get; private set; }

        /// <summary>The gunners this mount spawned, dead ones included. Filled on the authority only.</summary>
        public IReadOnlyList<GameObject> Gunners => gunners;

        private VesselSeats Seats => seats != null ? seats : seats = GetComponent<VesselSeats>();

        // Networked, the server spawns them as the mount spawns, the one moment only it can act on:
        // a Start check would also pass on a client holding a scene-placed double that netcode has not
        // spawned yet (Network.Simulates answers true for an unspawned object) and make ghost gunners.
        public override void OnNetworkSpawn()
        {
            if (IsServer) SpawnOnStart();
        }

        private void Start()
        {
            if (!Network.IsNetworked) SpawnOnStart();
        }

        private void SpawnOnStart()
        {
            if (!spawnOnStart || IsStoodDown) return;

            if (gunnerPrefab == null)
            {
                Debug.LogError($"[MountedGunners] '{name}' spawns gunners on start but has no gunnerPrefab; " +
                               "its seats stay empty.", this);
                return;
            }

            SpawnGunners();
        }

        /// <summary>Spawn a gunner into every empty seat. Authority only; does nothing elsewhere.</summary>
        public void SpawnGunners()
        {
            if (gunnerPrefab == null || !Network.Simulates(this)) return;

            for (int seat = 0; seat < Seats.Capacity; seat++)
            {
                if (Seats.OccupantAt(seat) != null) continue;

                int riderSeat = seat + FirstGunnerRiderSeat;
                (Vector3 position, Quaternion rotation) = Seats.SeatPose(seat);
                GameObject gunner = NpcSpawn.Create(gunnerPrefab, position, rotation, this,
                                                    spawned => GroupMembership.StampRider(gameObject, spawned, riderSeat),
                                                    seated: true);

                if (!Seats.Seat(seat, gunner))
                {
                    Debug.LogError($"[MountedGunners] Could not seat gunner '{gunner.name}' in seat {seat} of " +
                                   $"'{name}'; despawning it.", this);
                    NpcSpawn.Remove(gunner);
                    continue;
                }

                gunners.Add(gunner);
            }
        }

        /// <summary>
        /// Stand everyone still seated on the ground beside the mount, to fight on foot, and give up the
        /// gunners among them: from now on they are the group's to take down, not this mount's.
        /// Authority only; does nothing elsewhere (the reparent replicates on its own).
        /// </summary>
        public void ReleaseGunners()
        {
            if (!Network.Simulates(this)) return;

            for (int seat = 0; seat < Seats.Capacity; seat++)
            {
                GameObject gunner = Seats.OccupantAt(seat);
                if (gunner != null && Seats.Unseat(seat, StepOffPoint(seat)) != null)
                    gunners.Remove(gunner);
            }
        }

        /// <summary>
        /// Out from the seat, away from the mount's centre (behind it for a seat on the centre line),
        /// on the ground. VesselSeats.Unseat snaps it onto the NavMesh when there is one in reach and
        /// otherwise uses it as it is, so it must already be on the ground. The probe looks down from
        /// stepOffDistance above the seat, so ground rising as steeply as 45 degrees is still found.
        /// </summary>
        private Vector3 StepOffPoint(int seat)
        {
            Vector3 at = Seats.SeatPose(seat).position;
            Vector3 outward = at - transform.position;
            outward.y = 0f;
            outward = outward.sqrMagnitude > Vector3.kEpsilon ? outward.normalized : -transform.forward;
            Vector3 point = at + outward * stepOffDistance;

            groundProbe.IgnoreHierarchy(transform);
            return groundProbe.TryGroundBelow(point + Vector3.up * stepOffDistance, out Vector3 ground) ? ground : point;
        }

        /// <summary>
        /// <see cref="ICrewedSeats"/>: these seats never crew themselves again. Taken by a player, the
        /// gunners get down and fight on as the group's (<see cref="ReleaseGunners"/>). Restoring, the
        /// gunners OnNetworkSpawn just seated belong to no group and are taken away.
        /// </summary>
        public void StandDown(bool restoring)
        {
            if (!Network.Simulates(this)) return;

            IsStoodDown = true;
            if (restoring) DespawnGunners();
            else ReleaseGunners();
        }

        /// <summary>Take every gunner this mount spawned out of the world, for every peer.</summary>
        public void DespawnGunners()
        {
            foreach (GameObject gunner in gunners) NpcSpawn.Remove(gunner);
            gunners.Clear();
        }

        public override void OnNetworkDespawn()
        {
            // A session ending takes everything down by itself.
            if (NetworkManager != null && NetworkManager.ShutdownInProgress) return;

            DespawnGunners();
        }

        public override void OnDestroy()
        {
            DespawnGunners();
            base.OnDestroy();
        }

        private void OnValidate() => stepOffDistance = Mathf.Max(0f, stepOffDistance);
    }
}
