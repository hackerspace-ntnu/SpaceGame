// What a resident is visibly doing, as a few small values the server writes and every machine shows.
//
// The routine decides on the server; this is how the decision reaches the screen. Nothing here is
// replicated as a picture — the loop a resident holds at a spot (the smith at the anvil, a sitter by
// the fire), the prop an outrider or an errand-runner carries, the body that vanished indoors are all DERIVED locally from
// the values, so a late joiner gets the current state with the spawn and no animation has to be sent.
// The held place is an index into the settlement's places, which every machine gathers in one order; the seat a sitter
// sits on is the id every machine derives for a Seat (Seat.Id), resolved on each machine to its own copy of the seat; the
// cart a pusher has its hands on is the id of a Pushable, resolved the same way.
using System;
using System.Collections.Generic;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Items;
using SpaceGame.Presentation;
using SpaceGame.World;
using Unity.Netcode;
using UnityEngine;

namespace SpaceGame.Agents.Residents
{
    [DisallowMultipleComponent]
    public sealed class ResidentPresence : NetworkBehaviour
    {
        [Tooltip("Where a carried prop is parented. Empty: the right hand of a humanoid rig; no prop without one.")]
        [SerializeField] private Transform propSocket;

        private readonly NetworkVariable<byte> activity = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<byte> prop = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> hidden = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> place = new(NoPlace, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> attending = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> seat = new(NoSeat, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> cart = new(NoCart, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Holding at no place: walking, talking, sheltering.</summary>
        public const int NoPlace = -1;

        /// <summary>Sitting on no seat; no Seat derives this id.</summary>
        public const int NoSeat = 0;

        /// <summary>Hands on no cart; no Pushable derives this id.</summary>
        public const int NoCart = 0;

        // The deciding machine's own record: offline there is no spawn to write the variables through, and online what is
        // published before the spawn is written into them when the body spawns.
        private Activity decidedActivity;
        private byte decidedProp;
        private bool decidedHidden;
        private int decidedPlace = NoPlace;
        private bool decidedAttending;
        private int decidedSeat = NoSeat;
        private int decidedCart = NoCart;

        // What this machine currently shows, so applying the same state twice does nothing.
        private Activity shownActivity;
        private byte shownProp;
        private bool shownHidden;
        private int shownPlace = NoPlace;
        private bool shownAttending;
        private int shownSeatId = NoSeat;
        private Seat shownSeat;
        private int shownCartId = NoCart;
        private bool cueStale;

        private Resident resident;
        private ResidentHands hands;
        private BodyLanguage body;
        private Animator animator;
        private AnimatorCullingMode cullingBeforeHide;
        private SeatedBodyFit seatFit;
        private CartPusher pusher;
        private CharacterCue heldCue;
        private GameObject propInstance;
        private Collider hitCapsule;
        private bool capsuleWasEnabled;
        private readonly List<Renderer> hiddenRenderers = new();

        public Activity Activity => Mirrors ? (Activity)activity.Value : decidedActivity;
        public byte Prop => Mirrors ? prop.Value : decidedProp;
        public bool Hidden => Mirrors ? hidden.Value : decidedHidden;
        public int Place => Mirrors ? place.Value : decidedPlace;
        public int SeatId => Mirrors ? seat.Value : decidedSeat;
        public int CartId => Mirrors ? cart.Value : decidedCart;

        /// <summary>Turned to look at a player: the worker has let go of its work loop until it looks away.</summary>
        public bool Attending => Mirrors ? attending.Value : decidedAttending;

        /// <summary>Raised on every machine when the shown activity changes.</summary>
        public event Action<Activity> Changed;

        private bool Mirrors => IsSpawned && !IsServer;

        private void Awake()
        {
            resident = GetComponent<Resident>();
            var belt = GetComponent<BeltCarrier>();
            if (belt != null) belt.TransitSeconds = ResidentTuning.Instance.toolTransitSeconds;
            hands = new ResidentHands(resident, GetComponent<EntityInventoryComponent>(),
                                      GetComponent<EntityEquipmentController>(), belt, this);
            body = GetComponentInChildren<BodyLanguage>();
            animator = GetComponentInChildren<Animator>();
            seatFit = new SeatedBodyFit(animator, transform);
            pusher = CartPusher.On(gameObject);
            // The body's own capsule, the one AgentRagdoll also owns — never a ragdoll bone's.
            hitCapsule = GetComponent<Collider>();
        }

        /// <summary>Server only. Writes the state every machine derives its presentation from.</summary>
        public void Publish(Activity newActivity, byte newProp, bool newHidden, int heldPlace = NoPlace, bool newAttending = false,
                            int newSeat = NoSeat, int newCart = NoCart)
        {
            if (!Network.Decides) return;

            decidedActivity = newActivity;
            decidedProp = newProp;
            decidedHidden = newHidden;
            decidedPlace = heldPlace;
            decidedAttending = newAttending;
            decidedSeat = newSeat;
            decidedCart = newCart;

            if (IsSpawned && IsServer) WriteReplicatedCopy();

            Apply();
        }

        public override void OnNetworkSpawn()
        {
            // What was published before the spawn (a resident sent offstage or away as its chunk loads) went to the server's
            // own copy only: it reaches clients from here, with the spawn.
            if (IsServer) WriteReplicatedCopy();
            else
            {
                activity.OnValueChanged += OnStateChanged;
                prop.OnValueChanged += OnStateChanged;
                hidden.OnValueChanged += OnHiddenChanged;
                place.OnValueChanged += OnPlaceChanged;
                attending.OnValueChanged += OnAttendingChanged;
                seat.OnValueChanged += OnSeatChanged;
                cart.OnValueChanged += OnCartChanged;
            }

            // Read once as well as subscribing: a late joiner gets the current value with the spawn.
            Apply();
        }

        public override void OnNetworkDespawn()
        {
            activity.OnValueChanged -= OnStateChanged;
            prop.OnValueChanged -= OnStateChanged;
            hidden.OnValueChanged -= OnHiddenChanged;
            place.OnValueChanged -= OnPlaceChanged;
            attending.OnValueChanged -= OnAttendingChanged;
            seat.OnValueChanged -= OnSeatChanged;
            cart.OnValueChanged -= OnCartChanged;
        }

        private void WriteReplicatedCopy()
        {
            activity.Value = (byte)decidedActivity;
            prop.Value = decidedProp;
            hidden.Value = decidedHidden;
            place.Value = decidedPlace;
            attending.Value = decidedAttending;
            seat.Value = decidedSeat;
            cart.Value = decidedCart;
        }

        private void OnStateChanged(byte previous, byte next) => Apply();

        private void OnHiddenChanged(bool previous, bool next) => Apply();

        private void OnPlaceChanged(int previous, int next) => Apply();

        private void OnAttendingChanged(bool previous, bool next) => Apply();

        private void OnSeatChanged(int previous, int next) => Apply();

        private void OnCartChanged(int previous, int next) => Apply();

        private void OnEnable() => hands.Bind();

        private void OnDisable()
        {
            hands.Unbind();
            ShowSeat(NoSeat);
            ShowCart(NoCart);
        }

        // A loop is held by re-asserting it every frame, and BodyLanguage only writes where the animator is ours. The hand is
        // settled first: a loop is held only once the hand holds what it is done with, and a body that is hidden indoors
        // changes nothing (a tool put on the belt there would hang in the air).
        private void Update()
        {
            if (shownHidden) return;

            // A seat in a chunk that had not loaded when the id arrived: look again until it has.
            if (shownSeatId != NoSeat && shownSeat == null)
            {
                ResolveSeat();
                if (cueStale) Apply();
            }
            // A cart in a chunk that had not loaded when the id arrived: look again until it has.
            if (shownCartId != NoCart && pusher.Cart == null)
            {
                ResolveCart();
                if (cueStale) Apply();
            }
            if (hands.Update() && body != null) body.DropGesturesOverHands();
            if (heldCue != null && body != null && !shownAttending && hands.Ready) body.Hold(heldCue, shownPlace);
        }

        // After the animator has posed the body: a sitter is moved until its hips rest on the seat's sit point. A floor sit
        // is the held loop, a stool sit is the animator's own, so only the floor sit asks for the loop.
        private void LateUpdate() => seatFit.Update(IsSitting ? shownSeat : null, Time.deltaTime);

        private bool IsSitting => !shownHidden && shownSeat != null && (shownSeat.Pose == SeatPose.Stool || heldCue != null);

        private void Apply()
        {
            if (shownHidden != Hidden) ShowHidden(Hidden);
            if (shownProp != Prop) ShowProp(Prop);
            if (shownAttending != Attending)
            {
                shownAttending = Attending;
                // Looking up from the work: the loop lets go (through its exit clip), and Update picks it up
                // again when the resident looks away.
                if (shownAttending && heldCue != null && body != null) body.Release(heldCue);
            }
            if (shownSeatId != SeatId) ShowSeat(SeatId);
            if (shownCartId != CartId) ShowCart(CartId);
            if (shownActivity == Activity && shownPlace == Place && !cueStale) return;

            bool changed = shownActivity != Activity;
            int previousPlace = shownPlace;
            shownActivity = Activity;
            shownPlace = Place;
            cueStale = false;
            CharacterCue cue = CueFor(shownActivity, shownPlace);
            // The same loop at the same place carries on through a change of activity: a sitter that starts to talk keeps its pose,
            // where releasing and holding it again fades the sit out and back in (the body stood for a frame or two).
            bool sameLoop = cue != null && cue == heldCue && shownPlace == previousPlace;
            if (heldCue != null && body != null && !sameLoop) body.Release(heldCue);
            heldCue = cue;
            hands.Doing(shownActivity, shownPlace != NoPlace);
            hands.Station(heldCue);
            if (changed) Changed?.Invoke(shownActivity);
        }

        // The spot's own loop where the resident holds at one, a camp sleeper's lying loop, an outrider's trip cue.
        private CharacterCue CueFor(Activity shown, int heldPlace)
        {
            if (shown == Activity.Climbing) return ResidentTuning.Instance.climbCue;
            if (shown == Activity.Stalking)
                return IsTripRow(shownProp) ? ResidentTuning.Instance.tripKinds[shownProp - 1].cue : null;

            // Hands on a cart's handles: the body stands pushing, with no spot loop over the arms.
            if (pusher.Cart != null) return null;

            SettlementSociety society = resident != null ? resident.Society : null;
            SettlementPlace at = heldPlace != NoPlace && society != null ? society.Place(heldPlace) : null;
            if (at == null) return null;
            if (at.Kind == PlaceKind.Camp) return shown == Activity.Sleep ? ResidentTuning.Instance.campSleepCue : null;

            // A bed: the lying loop on its seat, and only while asleep (a rest on the bed in the day stands).
            if (at.Kind == PlaceKind.Bed)
                return shown == Activity.Sleep && shownSeat != null && shownSeat.Pose == SeatPose.Lie ? ResidentTuning.Instance.campSleepCue : null;

            // A sit is held only on a seat: with none (no Seat near the spot, or its chunk not loaded here yet) the body stands.
            // A stool sit holds no loop of the spot's: the animator's own chair sit is the pose (SetChairSit).
            if (at.Seated && (shownSeat == null || shownSeat.Pose == SeatPose.Stool)) return null;
            return at.HoldCue;
        }

        // The seat every machine records this resident on: the claim is replicated as an id and derived into the seat here.
        private void ShowSeat(int id)
        {
            if (shownSeat != null) shownSeat.Release(transform);

            shownSeatId = id;
            shownSeat = null;
            ResolveSeat();
            cueStale = true;
        }

        private void ResolveSeat()
        {
            shownSeat = Seat.Find(shownSeatId);
            if (body != null) body.SitOn(shownSeat != null);
            SetChairSit(shownSeat != null && shownSeat.Pose == SeatPose.Stool);
            if (shownSeat == null) return;

            shownSeat.TryClaim(transform);
            cueStale = true;
        }

        // The chair sit of the base layer, the one the player's chair raises: knees bent, feet down, arms at rest.
        private void SetChairSit(bool on)
        {
            if (animator != null && animator.runtimeAnimatorController != null) animator.SetBool(HumanoidParams.SeatedHash, on);
        }

        // The cart every machine puts this resident's hands on: the claim is replicated as an id and resolved to the local cart.
        private void ShowCart(int id)
        {
            shownCartId = id;
            cueStale = true;
            ResolveCart();
        }

        private void ResolveCart()
        {
            if (shownCartId == NoCart)
            {
                pusher.Release();
                hands.Pushing(false);
                return;
            }

            Pushable found = Pushable.Find(shownCartId);
            if (found == null || !pusher.Grip(found)) return;

            hands.Pushing(true);
            cueStale = true;
        }

        private void ShowProp(byte index)
        {
            shownProp = index;
            if (propInstance != null) Destroy(propInstance);
            propInstance = null;
            hands.Carrying(ResidentTuning.Instance.CarryItemAt(index));

            Transform socket = PropSocket();
            GameObject carried = ResidentTuning.Instance.PropAt(index);
            if (socket == null || carried == null) return;

            // A plain local copy, like any equipped visual: a networked prop could not parent to a bone anyway.
            propInstance = Instantiate(carried, socket, false);
            BodyAttachment.Mark(propInstance);
            propInstance.SetActive(!shownHidden);
        }

        private void ShowHidden(bool hide)
        {
            shownHidden = hide;
            // Nobody sees the body, so it does no animation work: no gesture or fidget starts, and the animator stops evaluating
            // (its renderers are off, which is what lets it cull itself completely) until the body is seen again.
            if (body != null) body.Dormant = hide;
            if (animator != null)
            {
                if (hide) cullingBeforeHide = animator.cullingMode;
                animator.cullingMode = hide ? AnimatorCullingMode.CullCompletely : cullingBeforeHide;
            }

            if (hide)
            {
                hiddenRenderers.Clear();
                foreach (Renderer part in GetComponentsInChildren<Renderer>())
                {
                    if (!part.enabled) continue;
                    part.enabled = false;
                    hiddenRenderers.Add(part);
                }

                capsuleWasEnabled = hitCapsule != null && hitCapsule.enabled;
                if (hitCapsule != null) hitCapsule.enabled = false;
            }
            else
            {
                foreach (Renderer part in hiddenRenderers)
                    if (part != null) part.enabled = true;
                hiddenRenderers.Clear();

                // Only what was switched off here: AgentRagdoll owns the same capsule while the body is limp.
                if (hitCapsule != null && capsuleWasEnabled) hitCapsule.enabled = true;
            }

            if (heldCue != null && body != null && hide) body.Release(heldCue);
            if (propInstance != null) propInstance.SetActive(!hide);
        }

        // Prop indices are 1-based: the tuning's trip rows, then its carry props; 0 carries nothing. Only a trip row has a cue.
        private static bool IsTripRow(byte index)
        {
            var rows = ResidentTuning.Instance.tripKinds;
            return index > 0 && rows != null && index <= rows.Length;
        }

        private Transform PropSocket()
        {
            if (propSocket != null) return propSocket;

            Animator animator = GetComponentInChildren<Animator>();
            return animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        }
    }
}
