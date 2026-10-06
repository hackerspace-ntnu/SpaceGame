using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Audio;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Items;
using SpaceGame.Locomotion;
using SpaceGame.Persistence;
using SpaceGame.Presentation;
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

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
    public sealed class Liftable : NetworkBehaviour, IInteractable, IContextualInteractable, IInteractionReadout,
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
        private readonly List<Collider> solidWhileFree = new();
        private readonly HashSet<Collider> unseenByGround = new();
        private WalkerGround ground;
        private LiftShape? shape;
        private LiftCarrier carrier;
        private bool spawned;

        // This machine's own player, while it is the carrier.
        private PlayerMovement localMovement;
        private PlayerController localController;
        private CharacterActions localActions;
        private PlayerInputManager localInputs;
        private IPlayerInventory localInventory;
        private bool liftRequested;
        private bool puttingDown;
        private float settleEndsAt = -1f;
        private int handledFrame = -1;
        private int grantedFrame = -1;

        private Vector3 lastFoot;
        private float slid;
        private float dustRate = -1f;

        public string LoadId => loadId;
        public string Label => displayName;
        public float SwingRate => swingRate;
        public float GripFraction => gripFraction;
        public Transform GripLeft => gripLeft;
        public Transform GripRight => gripRight;

        private LiftState State => IsSpawned && !IsServer ? networkState.Value : decided;

        /// <summary>Somebody has it in their hands.</summary>
        public bool IsCarried => State.Held;

        /// <summary>The load's shape in its own frame, measured from its markers.</summary>
        public LiftShape Shape => shape ??= Measure();

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
            : liftRequested ? "Lifting…"
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
        private bool MayLift(GameObject who)
        {
            if (who == null || !WithinReach(who.transform.position)) return false;
            if (who.TryGetComponent(out PlayerController controller) && controller.IsDead) return false;
            if (who.TryGetComponent(out PlayerSeating seating) && seating.IsSeated) return false;
            if (who.TryGetComponent(out PlayerPushing pushing) && pushing.IsPushing) return false;
            if (who.TryGetComponent(out PlayerMovement movement) && movement.IsHauling && !CarriedBy(who)) return false;
            return LiftCarrier.On(who).CanReach;
        }

        private bool WithinReach(Vector3 point)
        {
            Vector3 grips = GripPoint;
            Vector3 flat = point - grips;
            flat.y = 0f;
            return flat.sqrMagnitude <= gripReach * gripReach;
        }

        private void RequestLift(GameObject who)
        {
            liftRequested = true;
            this.NetToServer(NetMsg.LiftRequest, new NetArg { A = 1 }.With(who));
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
            float seconds = PlayOnCarrier(setDownAction);
            carrier.Lower(restPose, seconds);
            settleEndsAt = Time.time + seconds;

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

            if (!CarriedBy(who)) return;
            Pose restPose = new Pose(arg.P, arg.HasOrientation ? arg.R : transform.rotation);
            Pose own = ServerRestPose();
            if ((restPose.position - own.position).sqrMagnitude > restTolerance * restTolerance) restPose = own;
            Decide(Resting(restPose));
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
            if (CarriedByLocalPlayer) ReadLocalInput();
            TrackLocalPace();
            Present(Time.deltaTime);
        }

        /// <summary>SERVER: drop a carrier who has gone, died or wandered off, and hand the load to a destination it has reached.</summary>
        private void Judge()
        {
            LiftState state = decided;
            if (state.Held)
            {
                GameObject who = BodyOf(state.Carrier);
                bool gone = who == null ||
                            (who.TryGetComponent(out PlayerController controller) && controller.IsDead) ||
                            (who.transform.position - GripPoint).sqrMagnitude > releaseDistance * releaseDistance;
                if (gone)
                {
                    PutDownForCarrier();
                    return;
                }
            }

            foreach (ILiftDestination destination in destinations)
            {
                if (destination == null || !destination.Accepts(this)) continue;
                if (!LiftDestinations.Reached(transform.position, destination.Point, destination.Radius)) continue;

                destination.Receive(this);
                return;
            }
        }

        // ── What every machine shows ─────────────────────────────────────────

        /// <summary>Idempotent: acts only where what this machine shows differs from the state.</summary>
        private void Apply(bool skipLift)
        {
            LiftState state = State;
            liftRequested = false;
            puttingDown = false;

            if (!state.Held)
            {
                Pose restPose = new Pose(state.RestPosition, state.RestRotation);
                if (carrier != null)
                {
                    // The carrier's own machine is already lowering onto this pose; anyone else starts now.
                    float seconds = settleEndsAt > Time.time ? settleEndsAt - Time.time : SecondsOf(setDownAction, carrier.gameObject);
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
            if (carrier == next && next.Load == this) return;

            carrier = next;
            bool own = Network.Owns(who.transform);
            float liftSeconds = own ? PlayOnCarrier(liftAction) : SecondsOf(liftAction, who);
            carrier.Lift(this, liftSeconds, skipLift ? 1f : 0f);
            settleEndsAt = own ? Time.time + liftSeconds : -1f;

            grantedFrame = Time.frameCount;
            if (own) TakeLocalHands(who);
        }

        /// <summary>The carrier's own player: hands emptied, input heard, the walk slowed. Given back by <see cref="TrackLocalPace"/>.</summary>
        private void TakeLocalHands(GameObject who)
        {
            ReleaseLocalHands();
            localMovement = who.GetComponent<PlayerMovement>();
            localController = who.GetComponent<PlayerController>();
            if (localController == null) return;

            localInventory = localController.PlayerInventory;
            if (localInventory != null)
            {
                // Both hands are on the load: whatever was in them goes away, and drawing something puts the load down.
                if (localInventory.SelectedSlotIndex >= 0) localInventory.SelectSlot(localInventory.SelectedSlotIndex);
                localInventory.OnSlotSelected += OnLocalSlotSelected;
            }

            localInputs = localController.Input;
            if (localInputs != null) localInputs.OnInteractPressed += OnLocalInteractPressed;
        }

        private void ReleaseLocalHands()
        {
            if (localInventory != null) localInventory.OnSlotSelected -= OnLocalSlotSelected;
            if (localInputs != null) localInputs.OnInteractPressed -= OnLocalInteractPressed;
            localInventory = null;
            localInputs = null;
            if (localMovement != null) localMovement.StopHauling();
            localMovement = null;
            localController = null;
        }

        // Interact anywhere puts the load down, not only with the crosshair on it. Not on the frame the lift was granted:
        // offline the grant lands inside the very press that asked for it.
        private void OnLocalInteractPressed()
        {
            if (carrier != null && PutsDown(false, true, true, Time.frameCount == grantedFrame)) OnLocalInteract(carrier.gameObject);
        }

        private void OnLocalSlotSelected(InventorySlot slot)
        {
            if (slot != null && !slot.IsEmpty && CarriedByLocalPlayer) PutDownLocal();
        }

        /// <summary>
        /// This machine's carrier walks at the carry pace, and stands still while the lift or the set-down plays: a body that
        /// walked off mid-heave would leave its hands behind. Given back the frame the load is out of its hands.
        /// </summary>
        private void TrackLocalPace()
        {
            if (localMovement == null) return;

            bool carrying = CarriedByLocalPlayer;
            bool settling = Time.time < settleEndsAt;

            if (!carrying && !settling)
            {
                ReleaseLocalHands();
                return;
            }

            localMovement.StartHauling(settling ? 0f : localMovement.WalkSpeed * carrySpeedFraction);
        }

        /// <summary>Esc or death puts the load down — the carrier's own machine only. Interact and a drawn item are events.</summary>
        private void ReadLocalInput()
        {
            if (carrier == null || localController == null) return;

            if (localController.IsDead)
            {
                PutDownLocal();
                return;
            }

            // Gated on the shared menu scope, as the seat's Esc is: the chat box and the settings fields use Esc for "never
            // mind", and closing one of those must not drop the load as a side effect.
            Keyboard keys = Keyboard.current;
            if (keys != null && PutsDown(keys.escapeKey.wasPressedThisFrame, false, GameplayMenuScope.AcceptsGameplayInput, false))
                PutDownLocal();
        }

        /// <summary>
        /// Does this frame's input put a carried load down? Esc does — unless a menu, the chat box or a settings field has it,
        /// where Esc means "never mind" — and interact does, except on the frame the lift was granted (offline that is the very
        /// press that asked for it).
        /// </summary>
        public static bool PutsDown(bool escape, bool interact, bool gameplayInput, bool grantedThisFrame) =>
            (escape && gameplayInput) || (interact && !grantedThisFrame);

        /// <summary>Plays <paramref name="action"/> on this machine's carrier and answers how long it lasts.</summary>
        private float PlayOnCarrier(CharacterAction action)
        {
            GameObject who = carrier != null ? carrier.gameObject : BodyOf(State.Carrier);
            if (who == null) return fallbackSeconds;

            if (localActions == null || localActions.gameObject != who) localActions = who.GetComponentInChildren<CharacterActions>(true);
            if (action != null && localActions != null) localActions.Play(action);
            return SecondsOf(action, who);
        }

        /// <summary>How long <paramref name="action"/> plays on <paramref name="who"/>: the same on every machine, from the body's own seed.</summary>
        private float SecondsOf(CharacterAction action, GameObject who)
        {
            if (action == null || who == null) return fallbackSeconds;

            CharacterActions actions = who.GetComponentInChildren<CharacterActions>(true);
            float speed = actions != null ? actions.PlaybackSpeed(action) : 1f;
            return action.Seconds(0, speed);
        }

        private void LetGoHere()
        {
            if (carrier != null && carrier.Load == this) carrier.Drop();
            carrier = null;
            ReleaseLocalHands();
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
        /// A carried load is a ghost to the world: it does not shove the crew, snag on the ramp's lip or push the hull it is
        /// carried into. Put down, it is solid again. Only what was solid is switched, and only that is switched back.
        /// </summary>
        public void SetCarried(bool carried)
        {
            if (carried)
            {
                solidWhileFree.Clear();
                foreach (Collider c in ownColliders)
                    if (c != null && !c.isTrigger) { c.isTrigger = true; solidWhileFree.Add(c); }
                return;
            }

            foreach (Collider c in solidWhileFree) if (c != null) c.isTrigger = false;
            solidWhileFree.Clear();
        }

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

        private Pose SavedPose => carrier != null && carrier.Load == this
            ? carrier.RestFromHere()
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

        private bool CarriedBy(GameObject who) =>
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
