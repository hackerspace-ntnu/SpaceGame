using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
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

        /// <summary>
        /// Whether the crew are allowed to get up yet. False for the whole descent, true once the
        /// hull is down.
        ///
        /// <para>
        /// A replicated variable rather than a local check, because the machine that knows the
        /// flight is over is the SERVER and the machine that has to draw the prompt and read the
        /// key is the CLIENT. It also survives a late join, which a one-shot event would not: a
        /// player who connects after the landing still needs to be told they may stand up.
        /// </para>
        /// <para>
        /// This is what stops Escape being a bail-out button halfway down the arc.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<bool> releasable = new(false);

        /// <summary>Raised on this machine when its own player may (or may not) leave their seat.</summary>
        public static event Action<bool> LocalPlayerMayLeaveChanged;

        /// <summary>
        /// Raised on this machine when the hull ITS OWN player is sitting in is launched, with how
        /// long ago that happened — zero for everyone who was aboard at the time.
        ///
        /// <para>
        /// The counterpart to <see cref="LocalPlayerSeated"/>, and the reason it is not enough on
        /// its own: seating happens whenever each machine finishes streaming, so a presentation
        /// timed from it runs on a different clock everywhere. The launch is one server decision,
        /// announced once, and this is where it arrives on each machine. Static for exactly the
        /// reason <see cref="LocalPlayerSeated"/> is — a client's <c>ArrivalDirector</c> never
        /// holds a reference to the hull its player is riding down.
        /// </para>
        /// </summary>
        public static event Action<float> LocalCrewLaunched;

        /// <summary>
        /// When the formation this hull belongs to was launched, on the server's clock, or
        /// <see cref="NotLaunched"/>.
        ///
        /// <para>
        /// A replicated instant rather than a flag, and it exists for late joiners alone: somebody
        /// seated into a hull that is already falling was not here for
        /// <see cref="NetMsg.ArrivalLaunched"/>, and a cutscene that started its beats from their
        /// seating would run the entry sequence while the hull was seconds from the ground. Nothing
        /// subscribes to it changing — the event is what starts a presentation, this is only ever
        /// read, which is what stops a machine starting one twice.
        /// </para>
        /// </summary>
        private readonly NetworkVariable<double> launchedAt = new(NotLaunched);

        /// <summary>The value <see cref="launchedAt"/> carries before a launch. Not zero: a session's clock legitimately reads zero.</summary>
        private const double NotLaunched = -1d;

        /// <summary>True once this machine has acted on the launch, so a repeated message is inert.</summary>
        private bool launchAnnounced;

        /// <summary>Seats in the order <see cref="SeatOrdering"/> put them, resolved once.</summary>
        private readonly List<ShipSeat> seats = new();

        /// <summary>Which seat this machine is holding each rider in.</summary>
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

        /// <summary>
        /// The seated idle, so riders sit in the chairs instead of standing to attention in them.
        ///
        /// <para>
        /// One component for all four seats, and driven from here rather than left to
        /// <c>MountModule</c>: the descent deliberately does not use mounts, so a chair that waited
        /// for a mount event would have nobody to hear from. Attach and Detach already run on every
        /// machine for every player, which is exactly the reach the pose needs.
        /// </para>
        /// </summary>
        private SpaceGame.Agents.ChairPose chairPose;

        private SpaceGame.Agents.ChairPose ChairPose =>
            chairPose != null ? chairPose : chairPose = GetComponentInChildren<SpaceGame.Agents.ChairPose>(true);

        /// <summary>
        /// What this machine is holding for one rider. Only the seat, now: the body's own physics is
        /// <see cref="SpaceGame.Agents.CarriedBody"/>'s record, precisely so that a rider held by
        /// this class AND by <c>MountModule</c> is captured once and handed back once.
        /// </summary>
        private readonly struct SeatedBody
        {
            public readonly int SeatIndex;

            public SeatedBody(int seatIndex) => SeatIndex = seatIndex;
        }

        /// <summary>
        /// Riders whose <c>PlayerMovement</c> / <c>PlayerLook</c> this machine switched off, so the
        /// release switches back on exactly what it took. A rider who arrived with either already
        /// off is a remote player, or one the mount system has suspended, and waking them is
        /// somebody else's bug — the same rule
        /// <c>MountModule.RestoreRiderComponentsAfterDismount</c> records at length.
        /// </summary>
        private readonly HashSet<ulong> movementSuspended = new();

        private readonly HashSet<ulong> lookSuspended = new();

        /// <summary>Every spawned instance, so a machine-wide question can be asked without a scene search.</summary>
        private static readonly List<SeatedRider> Spawned = new();

        /// <summary>
        /// Whether this machine's own player is sitting in a landed hull and may stand up.
        ///
        /// <para>
        /// A POLLABLE fact rather than only an event, because its one consumer — the seat-exit
        /// hint — lives on a HUD that can be disabled at the moment the event fires (the cutscene
        /// hides the HUD), and a subscriber that was asleep for the announcement never hears it
        /// again. Anything gating on this reads the current truth every frame instead.
        /// </para>
        /// </summary>
        public static bool LocalPlayerMayLeave
        {
            get
            {
                foreach (SeatedRider rider in Spawned)
                    if (rider != null && rider.releasable.Value && rider.HoldsLocalPlayer)
                        return true;

                return false;
            }
        }

        /// <summary>How many seats this ship actually has. Zero means it has no markers at all.</summary>
        public int SeatCount
        {
            get { ResolveSeats(); return seats.Count; }
        }

        public override void OnNetworkSpawn()
        {
            Spawned.Add(this);

            this.NetOn(NetMsg.TakeSeat, OnTakeSeat);
            this.NetOn(NetMsg.LeaveSeat, OnLeaveSeat);
            this.NetOn(NetMsg.LeaveSeatRequest, OnLeaveSeatRequested);
            this.NetOn(NetMsg.ArrivalLaunched, OnArrivalLaunched);

            releasable.OnValueChanged += OnReleasableChanged;

            ResolveSeats();

            // The server fills the list once; clients receive it. Sized up front so every index is
            // addressable and "empty" is a value rather than a missing element.
            if (IsServer && occupants.Count == 0)
                for (int i = 0; i < seats.Count; i++) occupants.Add(0UL);

            // Late joiner: the events fired before this machine existed, so the state channel is the
            // only account of who is already sitting down.
            ApplyStateChannel();

            if (IsServer) StartCoroutine(StraightenIfRestoredAskew());
        }

        /// <summary>
        /// A parked hull may differ from its prefab by yaw alone — that is the invariant every
        /// consumer of the wreck (grounding, belly measurement, the save file) is built on. This
        /// enforces it on the one hull nothing else answers for: a wreck restored from a save
        /// written before mid-descent captures were grounded, which reloads pitched nose-down at
        /// the angle it flew in at and is never straightened by anything, because a loaded world
        /// never re-flies its crash.
        /// </summary>
        private IEnumerator StraightenIfRestoredAskew()
        {
            // Past the restore pass that applies a saved pose, and comfortably past the frame an
            // arrival registers this hull as a flight — a hull the director is flying is pitched
            // on purpose and none of this component's business.
            yield return new WaitForSeconds(1f);

            if (this == null || !IsSpawned) yield break;

            ArrivalDirector director = ArrivalDirector.Instance;
            if (director != null && director.IsFlightHull(gameObject)) yield break;

            Vector3 euler = transform.rotation.eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, euler.x);
            float roll = Mathf.DeltaAngle(0f, euler.z);

            if (Mathf.Abs(pitch) <= AskewToleranceDegrees && Mathf.Abs(roll) <= AskewToleranceDegrees)
                yield break;

            Quaternion level = Quaternion.Euler(0f, euler.y, 0f);
            transform.rotation = level;

            // Physics.autoSyncTransforms is off project-wide, so the body is written directly or
            // the next physics step puts the old attitude straight back.
            var body = GetComponent<Rigidbody>();
            if (body != null) body.rotation = level;

            Debug.LogWarning($"[SeatedRider] '{name}' was restored {pitch:F0}° pitched / {roll:F0}° " +
                             "rolled — a parked hull differs from its prefab by yaw alone, so it " +
                             "was levelled. The save that produced this was written mid-descent.", this);
        }

        /// <summary>Attitude a parked hull may legitimately hold — anything past this is a mid-descent pose.</summary>
        private const float AskewToleranceDegrees = 2f;

        public override void OnNetworkDespawn()
        {
            Spawned.Remove(this);

            this.NetOff(NetMsg.TakeSeat, OnTakeSeat);
            this.NetOff(NetMsg.LeaveSeat, OnLeaveSeat);
            this.NetOff(NetMsg.LeaveSeatRequest, OnLeaveSeatRequested);
            this.NetOff(NetMsg.ArrivalLaunched, OnArrivalLaunched);

            releasable.OnValueChanged -= OnReleasableChanged;

            // The prompt is drawn from this, and the hull going away is one of the ways a player
            // stops being able to answer it.
            if (HoldsLocalPlayer) LocalPlayerMayLeaveChanged?.Invoke(false);

            // The hull can be despawned with players still recorded in it — a host quitting mid
            // descent. Releasing here rather than leaving them frozen means the bodies get their
            // physics back instead of hanging kinematic in the sky.
            ReleaseEveryoneLocally();
        }

        /// <summary>
        /// Last resort. A hold this component never let go of is worse than the bug it prevents:
        /// PlayerMovement stops freeing a kinematic body it can see precisely because CarriedBody
        /// says somebody is carrying it, so a leaked claim is a player who can never move again,
        /// silently. A hull destroyed without ever despawning is the one path that gets here.
        /// </summary>
        public override void OnDestroy()
        {
            SpaceGame.Agents.CarriedBody.Abandon(this);
            base.OnDestroy();
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

        /// <summary>
        /// Does this machine's own player sit in one of these seats?
        /// </summary>
        public bool HoldsLocalPlayer
        {
            get
            {
                foreach (ulong id in seated.Keys)
                {
                    GameObject player = ResolveById(id);
                    if (player != null && Network.Owns(player.transform)) return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Server-only. Lets the crew get up when they choose to, which is what ends the arrival
        /// now that landing no longer turfs everybody out automatically.
        /// </summary>
        public void AllowRelease()
        {
            if (!IsServer) return;
            releasable.Value = true;
        }

        /// <summary>
        /// Server-only. Says that this hull is on its way down, so every machine can start the same
        /// presentation on the same frame.
        ///
        /// <para>
        /// Announced on the SHIP rather than raised locally on the server: the descent coroutine
        /// runs on one machine and the presentation has to run on all of them. Announced per hull
        /// rather than per formation because a versus match launches one ship per team and each
        /// crew is watching its own.
        /// </para>
        /// <para>
        /// Idempotent, like every other handler here: a second call is dropped rather than
        /// restarting anybody's cutscene halfway down the arc.
        /// </para>
        /// </summary>
        public void AnnounceLaunch()
        {
            if (!IsServer) return;
            if (launchedAt.Value > NotLaunched) return;

            launchedAt.Value = SessionClock;

            // To All rather than Others, so the server takes the same path every peer does instead
            // of a private one that can drift from it — the rule Seat() records.
            this.NetToAll(NetMsg.ArrivalLaunched, new NetArg().With(gameObject));
        }

        /// <summary>
        /// The launch has been announced. Only the machines with a player in THIS hull care.
        ///
        /// <para>
        /// Zero seconds ago, deliberately, rather than differencing the clocks: everybody receiving
        /// this message is receiving it now, and the transport's own latency is a fraction of a
        /// second against a twenty-six second descent. The replicated instant exists for the one
        /// case where "now" is wrong — a late joiner, who never receives this at all.
        /// </para>
        /// </summary>
        private void OnArrivalLaunched(in NetArg arg, ulong sender)
        {
            if (launchAnnounced) return;

            // NOT latched when nobody local is aboard yet. On a client the seat can resolve a
            // frame or two after this message lands — the TakeSeat event references a player
            // object the local spawn manager has not filed yet, and the state channel repairs it
            // shortly after. Latching here threw the one announcement away for good: the cutscene
            // then sat on black for its whole launchWait and played the entry beats over a ship
            // that had already crashed, which from the chair reads as "the screen never went black
            // at the impact". The catch-up in Update answers for whoever seats late.
            if (!HoldsLocalPlayer) return;

            launchAnnounced = true;
            LocalCrewLaunched?.Invoke(0f);
        }

        /// <summary>
        /// The state-channel repair for the launch, mirroring the seat repair: whatever order the
        /// seat event, the launch event and the replicated instant arrived in, a machine holding a
        /// local player in a launched hull ends up having announced exactly once.
        /// </summary>
        private void CatchUpOnLaunch()
        {
            if (launchAnnounced) return;

            float sinceLaunch = SecondsSinceLaunch;
            if (sinceLaunch < 0f || !HoldsLocalPlayer) return;

            launchAnnounced = true;
            LocalCrewLaunched?.Invoke(sinceLaunch);
        }

        /// <summary>
        /// How long ago this hull launched, or -1 if it has not.
        ///
        /// <para>
        /// Read when a player is seated, which is the only moment a machine can discover it missed
        /// the announcement — and read every frame by the hull's own presentation
        /// (<c>EntryBurn</c>), which needs where the descent IS rather than when it began. Public
        /// because it is the one thing on this component that every machine agrees about: the
        /// instant is replicated and the clock is the server's, so a presenter timing itself off
        /// this is timing itself off the same number as everybody else, late joiners included.
        /// </para>
        /// </summary>
        public float SecondsSinceLaunch =>
            launchedAt.Value <= NotLaunched ? -1f : (float)(SessionClock - launchedAt.Value);

        /// <summary>
        /// The clock the launch instant is stamped on: the server's, which every machine agrees
        /// about. Falls back to local time where there is no session at all — an editor scene, a
        /// test — where "the server's clock" and "this machine's clock" are the same thing anyway.
        /// </summary>
        private static double SessionClock =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening
                ? NetworkManager.Singleton.ServerTime.Time
                : Time.timeAsDouble;

        private void OnReleasableChanged(bool _, bool now)
        {
            if (HoldsLocalPlayer) LocalPlayerMayLeaveChanged?.Invoke(now);
        }

        /// <summary>
        /// A client asking to stand up.
        ///
        /// <para>
        /// The reference on the wire is checked rather than trusted: a client may only release the
        /// body it OWNS, so a malformed or hostile message cannot turf a crewmate out of their seat
        /// mid-descent. And it is refused outright until <see cref="releasable"/> — the server
        /// decides when the flight is over, not the machine holding the key.
        /// </para>
        /// </summary>
        private void OnLeaveSeatRequested(in NetArg arg, ulong sender)
        {
            if (!IsServer || !releasable.Value) return;

            GameObject player = arg.Resolve();
            if (player == null) return;

            var netObj = player.GetComponent<NetworkObject>();
            if (netObj == null || netObj.OwnerClientId != sender) return;

            Release(player);
        }

        /// <summary>
        /// This machine's own player asks to get up. Does nothing until the server says the flight
        /// is over.
        /// </summary>
        public void RequestLocalRelease()
        {
            if (!releasable.Value) return;

            foreach (ulong id in seated.Keys)
            {
                GameObject player = ResolveById(id);
                if (player == null || !Network.Owns(player.transform)) continue;

                // Offline there is no server to ask and no spawn manager to resolve against, so the
                // host path and the single-player path meet at the same Release rather than at two.
                if (IsServer) Release(player);
                else this.NetToServer(NetMsg.LeaveSeatRequest, new NetArg().With(player));
                return;
            }
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

            CatchUpOnLaunch();

            // Q gets you out of the chair — it is what the recovery hint teaches — and Escape
            // still works because it is the key that gets you off every mount in the game. Read
            // here rather than from the UI because the seat owns standing up; the prompt only
            // draws what this will answer.
            //
            // Gated on the shared menu scope, or Escape would mean two things at once: the chat
            // box and the settings fields both use it for "never mind", and closing one of those
            // would eject the player from their seat as a side effect. The scope is also false
            // while the arrival cutscene holds the controls, which is a second reason the descent
            // cannot be bailed out of.
            if (releasable.Value && HoldsLocalPlayer &&
                SpaceGame.Presentation.GameplayMenuScope.AcceptsGameplayInput &&
                Keyboard.current != null &&
                (Keyboard.current.qKey.wasPressedThisFrame ||
                 Keyboard.current.escapeKey.wasPressedThisFrame))
                RequestLocalRelease();
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

                // The rotation is only stamped while the flight is live. During the descent the
                // rider must inherit the hull's attitude — the ship is diving and tumbling under
                // them, and a body that kept its own yaw would sit facing out through the wall.
                // Once the hull is down and the crew may leave, the seat is static, and stamping
                // the rotation would pin the camera dead ahead: PlayerLook rebuilds its view from
                // the body every frame, so a landed rider whose body is rewritten each LateUpdate
                // cannot look around the cabin the way they can in any other chair.
                if (releasable.Value)
                    player.transform.position = seat.TransformPoint(seatOffset);
                else
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

            // Both of these used to return in silence, and silence is the wrong answer here: a body
            // that is not attached is still carried down the arc by the hull it is standing in, so
            // the crash looks completely normal while the rider never gets LocalPlayerSeated — and
            // that event is the ONLY thing that starts the arrival cutscene. The failure therefore
            // presents as "the ship lands and the screen never goes black", with nothing in the
            // console to say why. Seat() already shouts about a bad index; so must this.
            if (seatIndex < 0 || seatIndex >= seats.Count)
            {
                Debug.LogError($"[SeatedRider] Cannot attach to seat {seatIndex} on '{name}' — it " +
                               $"resolved {seats.Count} seat(s). Nobody will be seated here, and " +
                               "the arrival cutscene will not play for this rider.", this);
                return;
            }

            ulong id = IdOf(player);
            if (id == 0UL)
            {
                Debug.LogError($"[SeatedRider] '{player.name}' has no spawned NetworkObject on its " +
                               "own root, so it cannot be seated in " + name + ". The arrival " +
                               "cutscene will not play for it.", this);
                return;
            }

            // A rider moved between seats keeps everything the hold already did to them — it is the
            // same body held by the same component — so only the seat index is rewritten and the
            // block below is skipped. Re-running it would be a second Carry, a second suspend, and
            // a second LocalPlayerSeated for somebody who never got up.
            bool newlySeated = !seated.ContainsKey(id);

            seated[id] = new SeatedBody(seatIndex);

            if (newlySeated)
            {
                // Before anything else touches the body: the probe runs on the physics clock and a
                // single step reading a rider as ground is a visible jump.
                if (HoverMotor != null) HoverMotor.Carry(player);

                // Kinematic, weightless and un-interpolated for the length of the ride — through
                // CarriedBody rather than by hand, because the same body can be held by MountModule
                // as well the moment somebody rides this ship down and then takes its helm, and two
                // private captures hand back a state the body was never in. See CarriedBody.
                SpaceGame.Agents.CarriedBody.Hold(player, this);

                // Stops the player walking out of their own chair. PlayerMovement is deliberately
                // suspicious of a kinematic body it can still see — it frees one every physics step
                // — and it decides "somebody is carrying this" by asking CarriedBody, which is
                // exactly the answer the line above just registered. Disabling it as well is what
                // MountModule does for its rider, and it is what keeps a crew who have been told
                // they may get up from jogging on the spot in the cabin until they do.
                SuspendMovement(player);

                // TEMPORARY DIAGNOSTIC (2026-09-02) — remove once the missing arrival blackout is
                // diagnosed. This gate is the last silent one on the path: a rider that fails it is
                // still carried down the arc correctly, so the only visible symptom is that the
                // cutscene never starts and the screen never goes black.
                Debug.Log($"[SeatedRider:DIAG] Attach '{player.name}' seat={seatIndex} " +
                          $"netObjId={id} ownsHere={Network.Owns(player.transform)} " +
                          $"isNetworked={Network.IsNetworked} server={Network.Server} " +
                          $"localClient={Network.LocalClientId}", this);

                if (Network.Owns(player.transform))
                {
                    LocalPlayerSeated?.Invoke();

                    // Seated into a descent that is already under way: the launch was announced
                    // before this machine had a body in the hull, so the announcement is caught up
                    // with here, aged. Raised AFTER LocalPlayerSeated, because that is what starts
                    // the presentation this is telling how far along it should be.
                    CatchUpOnLaunch();

                    // A late joiner can be seated into an already-landed hull, so the prompt has to
                    // be told the current answer rather than waiting for the flag to change.
                    LocalPlayerMayLeaveChanged?.Invoke(releasable.Value);
                }
            }

            // Outside the alreadyHeld branch, and for every player rather than only our own: the
            // pose is what remote machines see of a crewmate through the canopy, and a body moved
            // between seats still needs to be sitting in the new one.
            if (ChairPose != null) ChairPose.PoseRider(player.transform);

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

            // Anyone whose body had already gone never reached RestoreMovement above, so the record
            // is dropped here rather than left naming a player this hull will never see again.
            movementSuspended.Clear();
            lookSuspended.Clear();
        }

        /// <summary>Undo everything seating did to the body, physics, pose and cargo status alike.</summary>
        private void ReleaseBody(GameObject player, SeatedBody before)
        {
            if (HoverMotor != null) HoverMotor.StopCarrying(player);
            if (ChairPose != null) ChairPose.ReleaseRider(player.transform);

            // Stood up BEFORE the body gets its weight back, and before movement is handed back, so
            // the first physics step after the release already has them on the deck rather than
            // inside the chair they were sitting in.
            StandUp(player, before.SeatIndex);

            SpaceGame.Agents.CarriedBody.Release(player, this);
            RestoreMovement(player);
        }

        /// <summary>
        /// Puts a rider on their feet where the seat says people get out, rather than leaving them
        /// standing in the chair.
        ///
        /// <para>
        /// <b>Getting up used to have no placement at all.</b> The body simply stayed on the seat
        /// pose — which is the pivot of a SEATED body, 1.1 m up the chair on this ship — and was
        /// then shoved out by whatever collider it happened to be overlapping. That is why the same
        /// chair put the same player somewhere different every time: the answer was coming from
        /// physics resolving an overlap, not from anything that had decided where the door was.
        /// </para>
        /// <para>
        /// Owner-only, like every other write to a player's pose here: the player's NetworkTransform
        /// is owner-authoritative and world-space, so a write on any other machine is a local guess
        /// that the wire immediately overrules.
        /// </para>
        /// </summary>
        private void StandUp(GameObject player, int seatIndex)
        {
            if (!Network.Owns(player.transform)) return;
            if (!TryResolveDismount(seatIndex, out Vector3 position, out float yaw)) return;

            // Yaw only. A rider inherits the hull's attitude while seated, and a wreck resting on a
            // slope would otherwise stand its crew up leaning.
            player.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            // Physics.autoSyncTransforms is off project-wide, so a transform write does not reach the
            // Rigidbody until the next physics step — and PlayerLook rebuilds the player's rotation
            // every frame from the body's, so the seated pose would be put straight back on them.
            // MountModule.ApplyDismountPose writes both for exactly this reason.
            var body = player.GetComponent<Rigidbody>();
            if (body == null) return;

            body.position = position;
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>
        /// Where the occupant of <paramref name="seatIndex"/> stands up, in world space.
        ///
        /// <para>
        /// The seat's own marker first, because four crew standing up onto one spot is four bodies
        /// resolving one overlap — the very thing this is here to stop. The hull's mount dismount
        /// point second, so a ship whose seats predate those markers still puts people somewhere
        /// deliberate rather than in the chair. Nothing at all last, which leaves the old behaviour
        /// intact for a seat that is genuinely unauthored.
        /// </para>
        /// </summary>
        private bool TryResolveDismount(int seatIndex, out Vector3 position, out float yaw)
        {
            position = Vector3.zero;
            yaw = 0f;

            if (seatIndex >= 0 && seatIndex < seats.Count)
            {
                Transform marker = seats[seatIndex].DismountPoint;
                if (marker != null)
                {
                    position = marker.position;
                    yaw = marker.eulerAngles.y;
                    return true;
                }
            }

            var mount = GetComponent<SpaceGame.Agents.MountModule>();
            Transform shared = mount != null ? mount.DismountPoint : null;

            if (shared == null) return false;

            position = shared.position;
            yaw = shared.eulerAngles.y;
            return true;
        }

        /// <summary>
        /// Takes a rider's own movement AND look away for the length of the ride, remembering
        /// whether each was this machine's to take.
        ///
        /// <para>
        /// The look goes with the movement because <c>PlayerLook</c> spends its yaw rotating the
        /// player's BODY, and a seated body belongs to the chair. In-chair look is
        /// <c>ArrivalCameraRig</c> feeding <c>PlayerHeadLook</c>'s clamped neck — leaving
        /// <c>PlayerLook</c> live beside it is two systems spending the same mouse movement, one
        /// of them by spinning a body that is bolted into a seat.
        /// </para>
        /// </summary>
        private void SuspendMovement(GameObject player)
        {
            Suspend<SpaceGame.Characters.PlayerMovement>(player, movementSuspended);
            Suspend<SpaceGame.Characters.PlayerLook>(player, lookSuspended);
        }

        /// <summary>Hands back exactly what <see cref="SuspendMovement"/> took, and only that.</summary>
        private void RestoreMovement(GameObject player)
        {
            Restore<SpaceGame.Characters.PlayerMovement>(player, movementSuspended);
            Restore<SpaceGame.Characters.PlayerLook>(player, lookSuspended);
        }

        private void Suspend<T>(GameObject player, HashSet<ulong> taken) where T : Behaviour
        {
            var component = player.GetComponent<T>();
            if (component == null || !component.enabled) return;

            component.enabled = false;
            taken.Add(IdOf(player));
        }

        private void Restore<T>(GameObject player, HashSet<ulong> taken) where T : Behaviour
        {
            if (!taken.Remove(IdOf(player))) return;

            // A rider who died in the chair keeps the freeze death applied — re-enabling controls
            // here would hand them back to a corpse. The same guard MountModule states at length
            // in RestoreRiderComponentsAfterDismount.
            var controller = player.GetComponent<SpaceGame.Characters.PlayerController>();
            if (controller != null && controller.IsDead) return;

            var component = player.GetComponent<T>();
            if (component != null) component.enabled = true;
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
