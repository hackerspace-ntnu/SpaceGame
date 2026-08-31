using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay.Arrival
{
    /// <summary>
    /// Holds players in this ship's seats, on every machine, and lets them go again.
    ///
    /// <para>
    /// <b>It does not reparent anything, and that is the whole design.</b> The obvious
    /// implementation — parent the body to the seat marker — throws
    /// <c>InvalidParentException</c>, because netcode refuses to put a spawned
    /// <see cref="NetworkObject"/> under a plain transform and a <c>ShipSeat</c> marker is exactly
    /// that. <c>MountModule.ParentRiderToMount</c> works around it by parenting to the mount's own
    /// NetworkObject and folding the marker's offset into that root's local space.
    /// </para>
    ///
    /// <para>
    /// That workaround is unnecessary here, because of two facts about the player prefab: its
    /// <c>NetworkTransform</c> is <b>owner-authoritative</b> and replicates in <b>world space</b>.
    /// Together those mean parenting is not what would make a rider ride — the owner's world
    /// position is what travels, so the server cannot place a client's body at all, and a parent
    /// the server sets would not move a remote body one metre. What actually carries a player is
    /// the owner writing its own world pose each frame, which is <see cref="HoldSeats"/>. Adding a
    /// reparent on top would buy nothing and cost every netcode parenting rule there is.
    /// </para>
    ///
    /// <para>
    /// <b>Two channels, answering different questions</b> — the same split
    /// <c>MountNetworkSync</c> documents:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="NetMsg.TakeSeat"/> is the EVENT. Everybody present acts on it at once.</item>
    /// <item><see cref="occupants"/> is the STATE. NetworkVariable change events never replay, so a
    /// client connecting mid-descent has nothing else to go on — the event was sent before it
    /// existed. It also re-asserts every frame, so it repairs a seat whatever went wrong.</item>
    /// </list>
    /// </summary>
    // Ordered late, and LateUpdate specifically, because of WHEN the ship moves. FlyDescent is a
    // coroutine, and a coroutine yielding null resumes after every Update in the frame but before
    // any LateUpdate. Holding the seats from Update therefore placed every rider at LAST frame's
    // seat pose against a hull travelling at descent speed — a permanent one-frame lag that reads
    // as the whole cabin vibrating. LateUpdate runs after the hull has moved, so the rider lands on
    // the pose the ship actually has this frame.
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public class SeatedRider : NetworkBehaviour
    {
        [Tooltip("Extra offset from the seat marker, in the marker's local space. Zero by default " +
                 "and meant to stay there: the arrival markers are authored AT the pose a body " +
                 "should occupy, measured against the deck, so the marker IS the answer. Positive " +
                 "Y lifts. Note the player pivot sits exactly 1 m above the soles, so an offset of " +
                 "-0.9 puts a body's feet nearly two metres through the floor — which is precisely " +
                 "what the first version of this did.")]
        [SerializeField] private Vector3 seatOffset = Vector3.zero;

        /// <summary>
        /// Raised on this machine when ITS OWN player sits down, and again when they are let go.
        ///
        /// <para>
        /// Static because the listener is <see cref="ArrivalDirector"/>, which exists on every
        /// machine but only ever spawns the ship on one — a client's director never holds a
        /// reference to the hull its player is sitting in, so there is nothing instance-level for it
        /// to subscribe to. This is also what fixes the cutscene: the descent is flown by the
        /// server, so a presentation started from that coroutine would play for the host and for
        /// nobody else.
        /// </para>
        /// </summary>
        public static event Action LocalPlayerSeated;

        /// <summary>Counterpart to <see cref="LocalPlayerSeated"/>.</summary>
        public static event Action LocalPlayerReleased;

        /// <summary>
        /// Who is in each seat, by NetworkObjectId; 0 for empty. The index is the seat's position in
        /// the ordered <see cref="ShipSeat"/> list.
        ///
        /// <para>
        /// Server-write, because seating is a server decision and this is the RECORD of it rather
        /// than a second way of making one. Nothing acts on a write to it except a peer bringing its
        /// own copy of the ship into line.
        /// </para>
        /// </summary>
        private readonly NetworkList<ulong> occupants = new();


        /// <summary>Seats in the order <see cref="SeatOrdering"/> put them, resolved once.</summary>
        private readonly List<ShipSeat> seats = new();

        /// <summary>What each seated body was before we froze it, so release can undo exactly that.</summary>
        private readonly Dictionary<ulong, SeatedBody> seated = new();

        /// <summary>
        /// Reused by the repair pass, which runs every frame — allocating a list per frame to hold
        /// the usually-empty set of stranded riders is not worth the garbage.
        /// </summary>
        private readonly List<ulong> strandedBuffer = new();

        private bool seatsResolved;

        /// <summary>
        /// This hull's hover motor, if it has one, so seated bodies can be declared as cargo.
        ///
        /// <para>
        /// A craft's height probe reads anything under it as the floor unless told otherwise, and
        /// the two rules that normally exclude a rider — "it is my own child" and "it is under its
        /// own physics" — both miss a body that is held kinematic WITHOUT being parented. That is
        /// precisely what this class does, so it has to say so out loud or the ship rises onto its
        /// own passengers and climbs away with them.
        /// </para>
        /// </summary>
        private SpaceGame.Agents.HoverRigidbodyMotor hoverMotor;

        private SpaceGame.Agents.HoverRigidbodyMotor HoverMotor =>
            hoverMotor != null ? hoverMotor : hoverMotor = GetComponent<SpaceGame.Agents.HoverRigidbodyMotor>();

        /// <summary>The cabin's red alert lamps, if this hull has any.</summary>
        private SpaceGame.Vehicles.CabinAlert cabinAlert;

        private SpaceGame.Vehicles.CabinAlert CabinAlert =>
            cabinAlert != null ? cabinAlert : cabinAlert = GetComponentInChildren<SpaceGame.Vehicles.CabinAlert>(true);

        private readonly struct SeatedBody
        {
            public readonly int SeatIndex;
            public readonly bool WasKinematic;
            public readonly bool HadGravity;
            public readonly RigidbodyInterpolation Interpolation;

            public SeatedBody(int seatIndex, bool wasKinematic, bool hadGravity,
                              RigidbodyInterpolation interpolation)
            {
                SeatIndex = seatIndex;
                WasKinematic = wasKinematic;
                HadGravity = hadGravity;
                Interpolation = interpolation;
            }
        }

        /// <summary>How many seats this ship actually has. Zero means it has no markers at all.</summary>
        public int SeatCount
        {
            get { ResolveSeats(); return seats.Count; }
        }

        public override void OnNetworkSpawn()
        {
            this.NetOn(NetMsg.TakeSeat, OnTakeSeat);
            this.NetOn(NetMsg.LeaveSeat, OnLeaveSeat);

            ResolveSeats();

            // The server fills the list once; clients receive it. Sized up front so every index is
            // addressable and "empty" is a value rather than a missing element.
            if (IsServer && occupants.Count == 0)
                for (int i = 0; i < seats.Count; i++) occupants.Add(0UL);

            // Late joiner: the events fired before this machine existed, so the state channel is the
            // only account of who is already sitting down.
            ApplyStateChannel();
        }

        public override void OnNetworkDespawn()
        {
            this.NetOff(NetMsg.TakeSeat, OnTakeSeat);
            this.NetOff(NetMsg.LeaveSeat, OnLeaveSeat);

            // The hull can be despawned with players still recorded in it — a host quitting mid
            // descent. Releasing here rather than leaving them frozen means the bodies get their
            // physics back instead of hanging kinematic in the sky.
            ReleaseEveryoneLocally();
        }

        /// <summary>
        /// Server-only. Seats <paramref name="player"/> in <paramref name="seatIndex"/> and tells
        /// everyone. False when there is no such seat, which the caller must treat as the loud
        /// failure it is rather than quietly seating them somewhere else.
        /// </summary>
        public bool Seat(GameObject player, int seatIndex)
        {
            if (!IsServer)
            {
                Debug.LogError("[SeatedRider] Seat called on a client. Seating is a server decision.", this);
                return false;
            }

            if (player == null)
            {
                Debug.LogError("[SeatedRider] Seat called with no player.", this);
                return false;
            }

            ResolveSeats();

            if (seatIndex < 0 || seatIndex >= seats.Count)
            {
                Debug.LogError($"[SeatedRider] Seat {seatIndex} does not exist on '{name}' — it has " +
                               $"{seats.Count} seat(s).", this);
                return false;
            }

            occupants[seatIndex] = IdOf(player);

            // Broadcast to All rather than Others, so the server runs the same attach path every
            // peer does instead of a private one that can drift from it.
            this.NetToAll(NetMsg.TakeSeat, new NetArg(a: seatIndex).With(player));
            return true;
        }

        /// <summary>Server-only. Empties every seat and tells everyone.</summary>
        public void ReleaseAll()
        {
            if (!IsServer) return;

            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] == 0UL) continue;

                GameObject player = ResolveById(occupants[i]);
                occupants[i] = 0UL;

                if (player != null)
                    this.NetToAll(NetMsg.LeaveSeat, new NetArg(a: i).With(player));
            }
        }

        /// <summary>
        /// Server-only. Empties whichever seat this player is in — for a disconnect mid-descent, so
        /// the hull is not left carrying a body that no longer exists.
        /// </summary>
        public void Release(GameObject player)
        {
            if (!IsServer || player == null) return;

            ulong id = IdOf(player);
            if (id == 0UL) return;

            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] != id) continue;

                occupants[i] = 0UL;
                this.NetToAll(NetMsg.LeaveSeat, new NetArg(a: i).With(player));
                return;
            }
        }

        private void OnTakeSeat(in NetArg arg, ulong sender)
        {
            GameObject player = arg.Resolve();
            if (player == null) return;

            Attach(player, arg.A);
        }

        private void OnLeaveSeat(in NetArg arg, ulong sender)
        {
            GameObject player = arg.Resolve();
            if (player == null) return;

            Detach(player);
        }

        private void Update()
        {
            if (!IsSpawned) return;

            // The state channel re-asserts itself, so a seat broken by anything — a missed event, a
            // player object that spawned after its own seating message — repairs itself on the next
            // frame rather than staying broken for the rest of the descent.
            //
            // Bookkeeping only. The actual placement is in LateUpdate; see the note on the class.
            ApplyStateChannel();
        }

        private void LateUpdate()
        {
            if (!IsSpawned) return;

            HoldSeats();
            DriveCabinAlert();
        }

        /// <summary>
        /// Sounds the cabin alarm for as long as anybody aboard is riding this down.
        ///
        /// <para>
        /// Driven from the seating rather than from <c>ArrivalCutscene</c>, because the cutscene
        /// runs only on a machine whose own player is in a chair — the alarm has to be lit on every
        /// machine that can see the cabin, including one watching a crewmate through the canopy.
        /// Read off the replicated occupancy, so every machine reaches the same answer with nothing
        /// extra on the wire.
        /// </para>
        /// </summary>
        private void DriveCabinAlert()
        {
            if (CabinAlert == null) return;

            bool crashing = false;

            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] == 0UL) continue;

                crashing = true;
                break;
            }

            CabinAlert.SetAlarm(crashing);
        }

        /// <summary>
        /// Writes this machine's own players into their seats, every frame.
        ///
        /// <para>
        /// <b>This is what actually carries a rider.</b> The player's <c>NetworkTransform</c> is
        /// owner-authoritative and world-space, so the only machine allowed to move a given body is
        /// the one that owns it, and the world position it writes is what every other machine
        /// receives. Bodies this machine does not own are deliberately left alone — their owner is
        /// doing this same job, and writing them here would be a local guess fighting the wire.
        /// </para>
        /// <para>
        /// Every frame rather than once on seating, because the seat is moving: the ship is flying a
        /// descent, so a pose written once is a body left behind at the top of the arc.
        /// </para>
        /// <para>
        /// Called from LateUpdate, which is not incidental — see the note on the class for why
        /// Update is a frame too early.
        /// </para>
        /// </summary>
        private void HoldSeats()
        {
            if (seated.Count == 0) return;

            foreach (KeyValuePair<ulong, SeatedBody> entry in seated)
            {
                GameObject player = ResolveById(entry.Key);
                if (player == null) continue;

                if (!Network.Owns(player.transform)) continue;

                // A rider the mount system has taken over belongs to it, not to us. MountModule
                // parents them to the hull and drives them from SteerModule; writing their world
                // pose here as well is two systems moving one body every frame, and the visible
                // result is a pilot pinned in the passenger pose who cannot steer.
                if (IsMountedElsewhere(player)) continue;

                int index = entry.Value.SeatIndex;
                if (index < 0 || index >= seats.Count) continue;

                Transform seat = seats[index].transform;
                player.transform.SetPositionAndRotation(seat.TransformPoint(seatOffset), seat.rotation);
            }
        }

        /// <summary>
        /// Brings this machine's copy of the ship into line with the state channel. Idempotent, as
        /// every replicated apply in this project is required to be — a machine that missed an event
        /// is corrected by the next pass rather than double-applying it.
        ///
        /// <para>
        /// Repairs in BOTH directions, and has to. Seating alone would leave a machine that dropped
        /// a <see cref="NetMsg.LeaveSeat"/> with a player held in a hull everybody else has walked
        /// out of — and unlike a missed seating, which the next frame fixes, nothing else would ever
        /// come along to undo it. Every seat that repairs itself has to be able to empty itself too.
        /// </para>
        /// </summary>
        private void ApplyStateChannel()
        {
            ResolveSeats();

            for (int i = 0; i < occupants.Count && i < seats.Count; i++)
            {
                if (occupants[i] == 0UL) continue;

                GameObject player = ResolveById(occupants[i]);
                if (player == null) continue;

                // Already in this seat: nothing to do. This is what makes the pass safe to run every
                // frame. Compared by seat INDEX rather than by parent, because nothing is reparented.
                if (seated.TryGetValue(occupants[i], out SeatedBody held) && held.SeatIndex == i)
                    continue;

                // The repair pass has no event to read the reason from, so it uses the one recorded
                // on the wire when the seat was filled. A late joiner gets it from reasons[] below,
                // which the server keeps precisely so a repair cannot invent the wrong presentation.
                Attach(player, i);
            }

            ReleaseAnyoneNoLongerListed();
        }

        /// <summary>
        /// Lets go of anybody this machine is still holding in a seat the state channel says is
        /// empty. The detach half of the repair pass.
        /// </summary>
        private void ReleaseAnyoneNoLongerListed()
        {
            if (seated.Count == 0) return;

            strandedBuffer.Clear();

            foreach (ulong id in seated.Keys)
            {
                bool stillSeated = false;

                for (int i = 0; i < occupants.Count; i++)
                {
                    if (occupants[i] != id) continue;

                    stillSeated = true;
                    break;
                }

                if (!stillSeated) strandedBuffer.Add(id);
            }

            // Collected first and released after, because Detach writes to the dictionary being
            // enumerated.
            foreach (ulong id in strandedBuffer)
            {
                GameObject player = ResolveById(id);

                if (player != null) Detach(player);
                else seated.Remove(id); // Body already gone; drop the record with it.
            }
        }

        private void Attach(GameObject player, int seatIndex)
        {
            ResolveSeats();

            if (seatIndex < 0 || seatIndex >= seats.Count) return;

            ulong id = IdOf(player);
            if (id == 0UL) return;

            bool alreadyHeld = seated.TryGetValue(id, out SeatedBody existing);

            if (alreadyHeld)
            {
                // Moved between seats rather than newly seated: keep the physics we already captured,
                // or the restore would hand back whatever we ourselves imposed.
                seated[id] = new SeatedBody(seatIndex, existing.WasKinematic, existing.HadGravity,
                                            existing.Interpolation);
            }
            else
            {
                var body = player.GetComponent<Rigidbody>();

                seated[id] = new SeatedBody(seatIndex,
                                            body != null && body.isKinematic,
                                            body != null && body.useGravity,
                                            body != null ? body.interpolation : RigidbodyInterpolation.None);

                // Before anything else touches the body: the probe runs on the physics clock and a
                // single step reading a rider as ground is a visible jump.
                if (HoverMotor != null) HoverMotor.Carry(player);

                if (body != null)
                {
                    // Kinematic, or the body keeps falling under gravity while the seat flies out
                    // from under it, and HoldSeats spends every frame fighting the fall.
                    body.isKinematic = true;
                    body.useGravity = false;

                    // Velocity cleared, or the speed the body had when it was grabbed keeps being
                    // reapplied under the teleport and shows up as a shudder.
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;

                    // Interpolation OFF, and this is the one that actually shows. The player prefab
                    // ships as Interpolate, which renders the body from where physics had it one
                    // step ago — against a seat that has moved a long way since, because the ship is
                    // falling. The result is a rider visibly shaking loose of the chair.
                    // MountModule.EnterMountedRigidbodyState does exactly this, for exactly this.
                    body.interpolation = RigidbodyInterpolation.None;
                }

                if (Network.Owns(player.transform)) LocalPlayerSeated?.Invoke();
            }

            // Placed immediately rather than waiting for the next HoldSeats, so the body never
            // renders for a frame at wherever it was spawned.
            if (Network.Owns(player.transform))
            {
                Transform seat = seats[seatIndex].transform;
                player.transform.SetPositionAndRotation(seat.TransformPoint(seatOffset), seat.rotation);
            }
        }

        private void Detach(GameObject player)
        {
            ulong id = IdOf(player);

            if (!seated.TryGetValue(id, out SeatedBody before)) return;
            seated.Remove(id);

            ReleaseBody(player, before);

            if (Network.Owns(player.transform)) LocalPlayerReleased?.Invoke();
        }

        /// <summary>
        /// Drops every hold this machine has, without touching the state channel. For teardown,
        /// where the record is going away anyway and the only thing that matters is that no body is
        /// left frozen.
        /// </summary>
        private void ReleaseEveryoneLocally()
        {
            foreach (KeyValuePair<ulong, SeatedBody> entry in seated)
            {
                GameObject player = ResolveById(entry.Key);
                if (player == null) continue;

                ReleaseBody(player, entry.Value);

                if (Network.Owns(player.transform)) LocalPlayerReleased?.Invoke();
            }

            seated.Clear();
        }

        /// <summary>Undo everything seating did to the body, physics and cargo status alike.</summary>
        private void ReleaseBody(GameObject player, SeatedBody before)
        {
            if (HoverMotor != null) HoverMotor.StopCarrying(player);

            RestorePhysics(player, before);
        }

        private static void RestorePhysics(GameObject player, SeatedBody before)
        {
            var body = player.GetComponent<Rigidbody>();
            if (body == null) return;

            body.isKinematic = before.WasKinematic;
            body.useGravity = before.HadGravity;
            body.interpolation = before.Interpolation;

            // Handed back at rest rather than carrying whatever the descent implied, so nobody is
            // flung across the wreck the instant they get their weight back.
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// This ship's seats, ordered. Resolved once and kept, because re-resolving mid-descent
        /// could renumber the seats underneath players already sitting in them.
        /// </summary>
        private void ResolveSeats()
        {
            if (seatsResolved) return;
            seatsResolved = true;

            var found = new List<ShipSeat>();
            GetComponentsInChildren(includeInactive: true, found);

            var orders = new int[found.Count];
            for (int i = 0; i < found.Count; i++) orders[i] = found[i].Order;

            foreach (int index in SeatOrdering.OrderedIndices(orders))
                seats.Add(found[index]);

            if (seats.Count == 0)
                Debug.LogError($"[SeatedRider] '{name}' has no ShipSeat markers, so nobody can be " +
                               "seated in it.", this);
        }

        /// <summary>
        /// Has the mount system claimed this body?
        ///
        /// <para>
        /// Asked of the mount rather than of the player, because <c>MountModule</c> is the thing
        /// that knows who is in its saddle. Seating and mounting are two different ways to end up
        /// in a chair on the same vehicle, and exactly one of them may be moving a given body.
        /// </para>
        /// </summary>
        private bool IsMountedElsewhere(GameObject player)
        {
            var mount = GetComponent<SpaceGame.Agents.MountModule>();

            return mount != null
                   && mount.MountedPlayerTransform != null
                   && mount.MountedPlayerTransform == player.transform;
        }

        private static ulong IdOf(GameObject go)
        {
            var netObj = go != null ? go.GetComponent<NetworkObject>() : null;
            return netObj != null && netObj.IsSpawned ? netObj.NetworkObjectId : 0UL;
        }

        private static GameObject ResolveById(ulong id)
        {
            if (id == 0UL || !Network.IsNetworked) return null;

            var spawnManager = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.SpawnManager
                : null;

            if (spawnManager == null) return null;

            return spawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject obj) && obj != null
                ? obj.gameObject
                : null;
        }
    }
}
