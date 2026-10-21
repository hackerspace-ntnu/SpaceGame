using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Locomotion;
using SpaceGame.Persistence;
using SpaceGame.Presentation;

namespace SpaceGame.World
{
    /// <summary>
    /// A heavy load a player lifts by one end and carries: the near end rises to the hands, the far end stays on the ground and
    /// slides after them, and the carrier walks a little slower than a walk with no sprint and no jump. Today: the lander's
    /// oxygen plant, thrown out of the ship in the crash.
    ///
    /// <para>
    /// <b>The pose is derived, never sent.</b> Who carries it, and where it was last put down, are one server-written
    /// <see cref="NetworkVariable{T}"/> a late joiner reads with the spawn. While it is carried, every machine poses it from the
    /// carrier's own body (<see cref="LiftCarrier"/>) — on the carrier's machine that body is the one its input moved this
    /// frame, so the load is in the hands with no lag; everyone else's copy of the body is interpolated, and the load with it.
    /// That is how a pushed cart works too (<see cref="Pushable"/>), and for the same reason: the player's transform is
    /// owner-authoritative, so anything posed from the server would trail the hands by a round trip.
    /// </para>
    /// <para>
    /// <b>The server decides who lifts and keeps the rest pose.</b> A lift is a request; the server checks reach and that the
    /// load is free. Putting down is the carrier's to start — its machine plays the set-down at once and sends the rest pose it
    /// computed, which the server accepts unless it is somewhere the server's own view of the load disagrees with. One carrier
    /// at a time: a second pair of hands would need both bodies posed against one load, and a load pulled two ways cannot stay
    /// glued to either set of hands.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Liftable : NetworkBehaviour, ILiftLoad, IInteractable, IContextualInteractable, IInteractionReadout,
                                   IInteractionMoment, IPersistentEntity, ISavedPose
    {
        [Tooltip("What this load is, for a destination deciding whether it belongs there.")]
        [SerializeField] private string loadId = "oxygen-plant";

        [SerializeField] private string displayName = "Oxygen plant";

        [Header("Shape")]
        [Tooltip("The two grip points at the near end, on the handle. The hands close here.")]
        [SerializeField] private Transform gripLeft;
        [SerializeField] private Transform gripRight;

        [Tooltip("The near end's underside, below the grips: it comes down on the ground when the load is put down.")]
        [SerializeField] private Transform heel;

        [Tooltip("The far end's underside: it stays on the ground while the near end is carried.")]
        [SerializeField] private Transform foot;

        [Header("Carrying")]
        [Tooltip("How close to the grips a player must stand to lift, in metres.")]
        [SerializeField, Min(0.5f)] private float gripReach = 2.5f;

        [Tooltip("The carrier's walking speed while carrying, as a fraction of their ordinary walk.")]
        [SerializeField, Range(0.3f, 1f)] private float carrySpeedFraction = 0.75f;

        [Tooltip("How quickly the far end swings round behind a turn, per second. Higher follows the body more tightly.")]
        [SerializeField, Min(0.1f)] private float swingRate = 5f;

        [Tooltip("The furthest the load may trail behind the way the carrier faces, in degrees. Past it the grips would swing out " +
                 "of the hands' reach (a whip turn on the spot), so the far end is dragged round with the body instead.")]
        [SerializeField, Range(5f, 90f)] private float maxSwingLag = 25f;

        [Tooltip("How far into the lift clip the hands close on the grips, 0-1. Before it the load lies still; after it the " +
                 "near end rises with the hands.")]
        [SerializeField, Range(0.05f, 0.95f)] private float gripFraction = 0.45f;

        [Tooltip("A carrier further than this from the load is taken off it by the server, in metres: a respawn or a " +
                 "teleport leaves nobody holding a load across the map.")]
        [SerializeField, Min(1f)] private float releaseDistance = 8f;

        [Tooltip("A put-down pose further than this from where the server poses the load is replaced by the server's own, " +
                 "in metres.")]
        [SerializeField, Min(0.1f)] private float restTolerance = 2f;

        [Header("The body")]
        [Tooltip("Played by the carrier as the lift starts: squat, take hold, heave.")]
        [SerializeField] private CharacterAction liftAction;

        [Tooltip("Played by the carrier as the load is put down.")]
        [SerializeField] private CharacterAction setDownAction;

        [Tooltip("Lift and set-down length when the body has no action to play, in seconds.")]
        [SerializeField, Min(0.1f)] private float fallbackSeconds = 1.2f;

        [Header("Ground")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("How far down from above the load the ground is looked for, in metres.")]
        [SerializeField, Min(0.5f)] private float probeLength = 6f;

        [Header("Look and sound")]
        [Tooltip("Dust kicked up where the far end slides. Its rate is multiplied by how fast it slides.")]
        [SerializeField] private ParticleSystem dust;

        [Tooltip("The far end's slide speed that raises the dust to full, in m/s.")]
        [SerializeField, Min(0.1f)] private float fullDustSpeed = 3f;

        [Tooltip("A scrape played once per this many metres the far end slides.")]
        [SerializeField, Min(0.1f)] private float scrapeEvery = 0.9f;

        [SerializeField] private SfxId scrapeSound = SfxId.InteractPickupMetal;

        /// <summary>The replicated state: who carries the load, and where it rests when nobody does.</summary>
        public struct LiftState : INetworkSerializable, IEquatable<LiftState>
        {
            public bool Held;
            public ulong Carrier;
            public Vector3 RestPosition;
            public Quaternion RestRotation;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref Held);
                serializer.SerializeValue(ref Carrier);
                serializer.SerializeValue(ref RestPosition);
                serializer.SerializeValue(ref RestRotation);
            }

            public bool Equals(LiftState other) =>
                Held == other.Held && Carrier == other.Carrier && RestPosition == other.RestPosition && RestRotation == other.RestRotation;

            public override bool Equals(object obj) => obj is LiftState other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Held, Carrier, RestPosition, RestRotation);
        }

        private readonly NetworkVariable<LiftState> networkState = new(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        // The deciding machine's own record: offline there is no spawn to write the variable through.
        private LiftState decided;

        private static readonly List<Liftable> active = new();
        private static readonly List<ILiftDestination> destinations = new();

        /// <summary>Every load in the world. The objective asks this, not the scene.</summary>
        public static IReadOnlyList<Liftable> Active => active;

        private Rigidbody body;
        private Collider[] ownColliders = Array.Empty<Collider>();
        private readonly List<Collider> offWhileHeld = new();
        private readonly HashSet<Collider> unseenByGround = new();
        private WalkerGround ground;
        private LiftShape? shape;
        private LiftCarrier carrier;

        // The body whose hands this load is in right now, whatever decided it: what a save asks for the rest pose.
        private LiftCarrier heldBy;
        private bool spawned;

        // This machine's own player, while it is the carrier.
        private LiftHands hands;
        private float liftRequestedAt = float.NegativeInfinity;
        private bool puttingDown;
        private int handledFrame = -1;

        // The ground under a carried load, probed down from above the hands.
        private float probeFrom, lastGround;
        private Func<float, float, float> groundAt;

        // Where above the hands the ground probes start: under a cabin roof, over a ramp lip.
        private const float ProbeAboveHands = 1.5f;

        private Vector3 lastFoot;
        private float slid;
        private float dustRate = -1f;

        public string LoadId => loadId;
        public string Label => displayName;
        public float SwingRate => swingRate;
        public float MaxSwingLag => maxSwingLag;
        public float GripFraction => gripFraction;
        public Transform GripLeft => gripLeft;
        public Transform GripRight => gripRight;

        // How long the prompt says "Lifting…" while the server is asked: past it, a refused request reads as refused.
        private const float RequestPatience = 1f;

        private LiftState State => IsSpawned && !IsServer ? networkState.Value : decided;

        /// <summary>Somebody has it in their hands.</summary>
        public bool IsCarried => State.Held;

        /// <summary>The load's shape in its own frame, measured from its markers.</summary>
        public LiftShape Shape => shape ??= Measure();

        /// <summary>The load <paramref name="body"/> is carrying right now, or null.</summary>
        public static Liftable CarriedBy(GameObject body)
        {
            if (body == null) return null;
            foreach (Liftable load in active)
                if (load != null && load.IsCarriedBy(body)) return load;
            return null;
        }

        public static void Register(ILiftDestination destination)
        {
            if (destination != null && !destinations.Contains(destination)) destinations.Add(destination);
        }

        public static void Unregister(ILiftDestination destination) => destinations.Remove(destination);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            active.Clear();
            destinations.Clear();
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            ownColliders = GetComponentsInChildren<Collider>(true);
            decided = RestHere();
            lastFoot = FootPoint;
        }

        private void OnEnable()
        {
            active.Add(this);
            this.NetOn(NetMsg.LiftRequest, OnLiftRequested);
        }

        private void OnDisable()
        {
            active.Remove(this);
            this.NetOff(NetMsg.LiftRequest, OnLiftRequested);
            LetGoHere();
        }

        public override void OnNetworkSpawn()
        {
            spawned = true;
            networkState.OnValueChanged += OnStateChanged;

            // Where the store or the spawn put it is where it rests. A late joiner reads the state instead.
            if (IsServer) Decide(RestHere());
            else Apply(skipLift: true);
        }

        public override void OnNetworkDespawn()
        {
            spawned = false;
            networkState.OnValueChanged -= OnStateChanged;
            LetGoHere();
        }

        private void OnStateChanged(LiftState previous, LiftState current) => Apply(skipLift: false);

        // ── The look ─────────────────────────────────────────────────────────

        public string Prompt => CarriedByLocalPlayer ? "RMB / Esc: put down"
            : Time.time - liftRequestedAt < RequestPatience ? "Lifting…"
            : IsCarried ? "Somebody is carrying it"
            : "RMB: lift it by the handle";

        public float? Value01 => null;
        public string ValueText => IsCarried ? "Carried" : "";

        /// <summary>The body shows the lift itself, not a reach.</summary>
        public CharacterMoment InteractionMoment => CharacterMoment.None;

        // ── The press ────────────────────────────────────────────────────────

        public bool CanInteract() => true;

        public bool CanInteract(Interactor interactor)
        {
            if (interactor == null) return false;
            if (CarriedByLocalPlayer) return true;
            return !IsCarried && MayLift(NetChannel.RootOf(interactor));
        }

        public void Interact(Interactor interactor)
        {
            GameObject who = interactor != null ? NetChannel.RootOf(interactor) : null;
            if (who == null || !Network.Owns(who.transform)) return;

            OnLocalInteract(who);
        }

        /// <summary>
        /// This machine's player pressed interact at the load, or anywhere while carrying it. Once per frame: the interactor and
        /// the input event both report the same press.
        /// </summary>
        private void OnLocalInteract(GameObject who)
        {
            if (handledFrame == Time.frameCount) return;
            handledFrame = Time.frameCount;

            if (CarriedByLocalPlayer) PutDownLocal();
            else if (!IsCarried && MayLift(who)) RequestLift(who);
        }

        /// <summary>
        /// A body that may lift this load: standing within reach of its grips, alive, its own hands free (no cart, no seat), and
        /// with the arms a lift needs.
        /// </summary>
        private bool MayLift(GameObject who) =>
            who != null && WithinReach(who.transform.position) && LiftHands.MayTakeHold(who, IsCarriedBy(who));

        private bool WithinReach(Vector3 point)
        {
            Vector3 grips = GripPoint;
            Vector3 flat = point - grips;
            flat.y = 0f;
            return flat.sqrMagnitude <= gripReach * gripReach;
        }

        private void RequestLift(GameObject who)
        {
            liftRequestedAt = Time.time;
            this.NetToServer(NetMsg.LiftRequest, new NetArg { A = 1 }.With(who));
        }

        /// <summary>
        /// The carrier's own machine asks to set the load into the destination it is aiming at (a <see cref="LiftDock"/>). The
        /// server finds the destination that wants it and that the carrier stands near enough to.
        /// </summary>
        public void RequestPlace()
        {
            if (carrier == null || puttingDown || !Network.Owns(carrier.transform)) return;
            this.NetToServer(NetMsg.LiftRequest, new NetArg { A = 2 }.With(carrier.gameObject));
        }

        /// <summary>
        /// The carrier's own machine puts the load down: the set-down plays now, the load lowers onto the rest pose this
        /// machine computed, and the server is told where that is.
        /// </summary>
        private void PutDownLocal()
        {
            if (carrier == null || puttingDown) return;

            puttingDown = true;
            Pose restPose = carrier.RestFromHere();
            float seconds = LiftHands.Play(carrier.gameObject, setDownAction, fallbackSeconds);
            carrier.Lower(restPose, seconds);
            Hands.SettleEndsAt = Time.time + seconds;

            var arg = new NetArg { A = 0, P = restPose.position, R = restPose.rotation }.With(carrier.gameObject);
            this.NetToServer(NetMsg.LiftRequest, arg);
        }

        // ── The server's decision ────────────────────────────────────────────

        private void OnLiftRequested(in NetArg arg, ulong sender)
        {
            if (!Network.Simulates(this)) return;

            GameObject who = arg.Resolve();
            if (who == null || !Network.MayActFor(who, sender) || !who.TryGetComponent(out NetworkObject net)) return;

            if (arg.A == 1)
            {
                if (IsCarried || !MayLift(who)) return;
                Decide(new LiftState { Held = true, Carrier = net.NetworkObjectId, RestPosition = decided.RestPosition, RestRotation = decided.RestRotation });
                return;
            }

            if (!IsCarriedBy(who)) return;

            if (arg.A == 2)
            {
                SetInto(who);
                return;
            }

            Pose restPose = new Pose(arg.P, arg.HasOrientation ? arg.R : transform.rotation);
            Pose own = ServerRestPose();
            if ((restPose.position - own.position).sqrMagnitude > restTolerance * restTolerance) restPose = own;
            Decide(Resting(restPose));
        }

        /// <summary>
        /// SERVER: set the load into the first destination that wants it and that its carrier stands within reach of. The
        /// carrier is measured, not the load: the load is 3.8 m long and out in front of them.
        /// </summary>
        private void SetInto(GameObject who)
        {
            foreach (ILiftDestination destination in destinations)
            {
                if (destination == null || !destination.Accepts(this)) continue;
                if (!LiftDestinations.Reached(who.transform.position, destination.Point, destination.Radius)) continue;

                destination.Receive(this);
                return;
            }
        }

        /// <summary>SERVER: whoever carries the load lets go of it where it is (death, disconnect, too far away).</summary>
        private void PutDownForCarrier() => Decide(Resting(ServerRestPose()));

        private Pose ServerRestPose() => carrier != null ? carrier.RestFromHere() : new Pose(transform.position, transform.rotation);

        private static LiftState Resting(Pose pose) =>
            new LiftState { Held = false, RestPosition = pose.position, RestRotation = pose.rotation };

        private LiftState RestHere() => Resting(new Pose(transform.position, transform.rotation));

        // The server (or the only machine there is) writes the state every machine shows.
        private void Decide(LiftState next)
        {
            if (!Network.Decides) return;

            decided = next;
            if (spawned && IsServer) networkState.Value = next;
            Apply(skipLift: false);
        }

        private void Update()
        {
            if (Network.Decides) Judge();
            Hands.Tick();
            Present(Time.deltaTime);
        }

        /// <summary>SERVER: drop a carrier who has gone, died or wandered off.</summary>
        private void Judge()
        {
            LiftState state = decided;
            if (state.Held)
            {
                GameObject who = BodyOf(state.Carrier);
                bool gone = who == null ||
                            (who.TryGetComponent(out PlayerController controller) && controller.IsDead) ||
                            (who.transform.position - GripPoint).sqrMagnitude > releaseDistance * releaseDistance;
                if (gone) PutDownForCarrier();
            }
        }

        // ── What every machine shows ─────────────────────────────────────────

        /// <summary>Idempotent: acts only where what this machine shows differs from the state.</summary>
        private void Apply(bool skipLift)
        {
            LiftState state = State;
            liftRequestedAt = float.NegativeInfinity;
            puttingDown = false;

            if (!state.Held)
            {
                Pose restPose = new Pose(state.RestPosition, state.RestRotation);
                if (carrier != null)
                {
                    // The carrier's own machine is already lowering onto this pose; anyone else starts now.
                    float seconds = Hands.SettleEndsAt > Time.time
                        ? Hands.SettleEndsAt - Time.time
                        : LiftHands.SecondsOf(setDownAction, carrier.gameObject, fallbackSeconds);
                    carrier.Lower(restPose, seconds);
                    carrier = null;
                }
                else if (state.RestRotation != default)
                {
                    Place(restPose);
                }

                return;
            }

            GameObject who = BodyOf(state.Carrier);
            if (who == null) return;                       // not resolved here yet: Present asks again

            LiftCarrier next = LiftCarrier.On(who);
            if (carrier == next && ReferenceEquals(next.Load, this)) return;

            carrier = next;
            bool own = Network.Owns(who.transform);
            float liftSeconds = own
                ? LiftHands.Play(who, liftAction, fallbackSeconds)
                : LiftHands.SecondsOf(liftAction, who, fallbackSeconds);
            carrier.Lift(this, liftSeconds, skipLift ? 1f : 0f);

            if (!own) return;
            Hands.Take(who);
            Hands.SettleEndsAt = Time.time + liftSeconds;
        }

        /// <summary>This machine's player's hands while they carry the load. See <see cref="LiftHands"/>.</summary>
        private LiftHands Hands => hands ??= new LiftHands(this, () => CarriedByLocalPlayer, PutDownLocal,
                                                           () => { if (carrier != null) OnLocalInteract(carrier.gameObject); },
                                                           () => carrySpeedFraction);

        private void LetGoHere()
        {
            if (carrier != null && ReferenceEquals(carrier.Load, this)) carrier.Drop();
            carrier = null;
            Hands.Release();
        }

        /// <summary>
        /// Dust and scraping where the far end slides, on every machine, from how far it actually moved; and a carrier this
        /// machine had not resolved when the state arrived (a late join) is looked for again.
        /// </summary>
        private void Present(float deltaTime)
        {
            if (State.Held && carrier == null) Apply(skipLift: true);

            Vector3 footPoint = FootPoint;
            Vector3 moved = footPoint - lastFoot;
            lastFoot = footPoint;
            moved.y = 0f;
            float speed = deltaTime > 0f ? moved.magnitude / deltaTime : 0f;

            if (dust != null)
            {
                dust.transform.position = footPoint;
                ParticleSystem.EmissionModule emission = dust.emission;
                if (dustRate < 0f) dustRate = emission.rateOverTimeMultiplier;

                // The captured rate times a factor: rateOverTimeMultiplier IS the rate.
                emission.rateOverTimeMultiplier = dustRate * Mathf.Clamp01(speed / fullDustSpeed);
            }

            slid += moved.magnitude;
            if (slid < scrapeEvery) return;

            slid = 0f;
            Sfx.Play(scrapeSound, footPoint, GetInstanceID());
        }

        // ── The load's own body ──────────────────────────────────────────────

        /// <summary>Puts the load at <paramref name="pose"/>: the transform and the kinematic body together, this frame.</summary>
        public void Place(Pose pose)
        {
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            if (body == null) return;

            body.position = pose.position;
            body.rotation = pose.rotation;
        }

        /// <summary>
        /// A carried load is a ghost: it does not shove the crew, snag on the ramp's lip or push the hull it is carried into,
        /// and the carrier's crosshair passes through it to the mount it is aimed at. Its colliders are off for as long as it is
        /// in somebody's hands — only those that were on, and only those are switched back.
        /// </summary>
        public void SetHeldBy(LiftCarrier by)
        {
            if (heldBy != null) SetSeenByGround(heldByColliders, true);
            heldBy = by;

            if (by != null)
            {
                // The carrier's own body is not ground to rest the far end on.
                heldByColliders = by.GetComponentsInChildren<Collider>(false);
                SetSeenByGround(heldByColliders, false);
                lastGround = transform.position.y;
                groundAt ??= GroundAt;

                offWhileHeld.Clear();
                foreach (Collider c in ownColliders)
                    if (c != null && c.enabled) { c.enabled = false; offWhileHeld.Add(c); }
                return;
            }

            heldByColliders = Array.Empty<Collider>();
            foreach (Collider c in offWhileHeld) if (c != null) c.enabled = true;
            offWhileHeld.Clear();

            // Let go where it is: the kinematic body back in step with the transform it was posed by.
            Place(new Pose(transform.position, transform.rotation));
        }

        private Collider[] heldByColliders = Array.Empty<Collider>();

        // ── In the hands (ILiftLoad) ─────────────────────────────────────────

        bool ILiftLoad.CanBeHeld => Shape.IsValid;

        Vector3 ILiftLoad.Axis => transform.rotation * Shape.Axis;

        Vector3 ILiftLoad.GripOf(Pose pose) => pose.position + pose.rotation * Shape.Grip;

        void ILiftLoad.Grips(Transform carrier, Vector3 grip, out Vector3 left, out Vector3 right)
        {
            left = gripLeft.position;
            right = gripRight.position;
        }

        /// <summary>
        /// Posed rigidly from the hands: the near end on the grip, the far end pitched down until it touches the ground under
        /// it (<see cref="LiftPoseSolver"/>), blended from where it lay while the hands close and onto its rest as they let go.
        /// </summary>
        void ILiftLoad.Hold(in LiftFrame frame)
        {
            probeFrom = Mathf.Max(frame.CarryPoint.y, transform.position.y) + ProbeAboveHands;

            Pose pose = LiftPoseSolver.Solve(Shape, frame.Grip, frame.Heading, groundAt);
            if (frame.FromStart < 1f)
                pose = new Pose(Vector3.Lerp(frame.From.position, pose.position, frame.FromStart),
                                Quaternion.Slerp(frame.From.rotation, pose.rotation, frame.FromStart));
            if (frame.ToRest > 0f)
                pose = new Pose(Vector3.Lerp(pose.position, frame.Rest.position, frame.ToRest),
                                Quaternion.Slerp(pose.rotation, frame.Rest.rotation, frame.ToRest));

            Place(pose);
            lastGround = pose.position.y;
        }

        /// <summary>Lying on its underside along the ground's slope where it is now.</summary>
        public Pose RestFromHere() =>
            LiftPoseSolver.Rest(Shape, new Pose(transform.position, transform.rotation), groundAt ??= GroundAt);

        private float GroundAt(float x, float z) => GroundY(x, z, probeFrom, lastGround);

        /// <summary>
        /// Ground height at a world x, z, probed down from <paramref name="fromY"/>; <paramref name="fallback"/> where nothing is
        /// there (the chunk is not loaded). Never the load itself, never the body that carries it.
        /// </summary>
        public float GroundY(float x, float z, float fromY, float fallback)
        {
            ground ??= new WalkerGround(transform, groundMask, 0f, probeLength, unseenByGround);
            return ground.Ray(new Vector3(x, fromY, z), probeLength, out RaycastHit hit) ? hit.point.y : fallback;
        }

        /// <summary>Makes the ground probes pretend these colliders (the carrier's body) are not there, or sees them again.</summary>
        public void SetSeenByGround(IEnumerable<Collider> colliders, bool seen)
        {
            foreach (Collider c in colliders)
            {
                if (seen) unseenByGround.Remove(c);
                else unseenByGround.Add(c);
            }
        }

        /// <summary>Where the grips are now, in world space.</summary>
        public Vector3 GripPoint => transform.position + transform.rotation * Shape.Grip;

        private Vector3 FootPoint => transform.position + transform.rotation * Shape.Foot;

        private LiftShape Measure()
        {
            Vector3 Local(Transform marker, Vector3 fallback) =>
                marker != null ? Quaternion.Inverse(transform.rotation) * (marker.position - transform.position) : fallback;

            Vector3 grip = (Local(gripLeft, Vector3.zero) + Local(gripRight, Vector3.zero)) * 0.5f;
            return new LiftShape(grip, Local(heel, grip), Local(foot, grip), Vector3.up);
        }

        // ── Saving ───────────────────────────────────────────────────────────

        /// <summary>
        /// A load saved while carried is saved put down where it was carried: it reloads lying on the ground at that spot,
        /// never hanging in the air from hands that are not there.
        /// </summary>
        public Vector3 PositionToSave => SavedPose.position;
        public Quaternion RotationToSave => SavedPose.rotation;

        private Pose SavedPose => heldBy != null && ReferenceEquals(heldBy.Load, this)
            ? heldBy.RestFromHere()
            : new Pose(transform.position, transform.rotation);

        // ── Who ──────────────────────────────────────────────────────────────

        private bool CarriedByLocalPlayer
        {
            get
            {
                LiftState state = State;
                if (!state.Held) return false;
                GameObject who = BodyOf(state.Carrier);
                return who != null && Network.Owns(who.transform);
            }
        }

        private bool IsCarriedBy(GameObject who) =>
            who != null && State.Held && who.TryGetComponent(out NetworkObject net) && net.NetworkObjectId == State.Carrier;

        private static GameObject BodyOf(ulong networkObjectId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.SpawnManager == null) return null;
            return manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject net) && net != null
                ? net.gameObject
                : null;
        }
    }
}
