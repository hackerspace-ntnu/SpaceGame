// Player-facing switch for one or more ArticulatedParts: look at it, press Interact, it toggles.
// Put this on the part's pivot (or on any GameObject carrying the part's collider) — Interactor
// resolves IInteractable via GetComponent then GetComponentInParent, so a collider anywhere under
// the pivot finds it.
//
// Driving several parts from one switch covers double doors: both leaves answer to a single panel.
//
// Networking rides NetMessaging like everything else, and stays a plain MonoBehaviour on purpose.
// A NetworkBehaviour on an object with no NetworkObject above it is an error in Netcode, and doors
// are exactly the thing that turns up inside interiors and chunk props where nobody has spawned
// anything. On those the send falls through to a local dispatch and the door works the way it
// always did, single-player-style, with one WarnUnrelayed line to say so.
using UnityEngine;
using SpaceGame.Agents;
using SpaceGame.Core;
using SpaceGame.Gameplay;

namespace SpaceGame.Vehicles
{
    public class ArticulatedPartInteraction : MonoBehaviour, IInteractable
    {
        [Tooltip("Parts this switch drives. Leave empty to use the ArticulatedPart on this GameObject.")]
        [SerializeField] private ArticulatedPart[] parts;

        [Tooltip("Ignore interaction while any driven part is still moving, so it can't be stuttered mid-swing.")]
        [SerializeField] private bool blockWhileMoving = true;

        [Header("Mount Lock")]
        [Tooltip("Refuse to open/close while someone is piloting the vehicle — no dropping the ramp mid-flight.")]
        [SerializeField] private bool lockedWhileMounted = true;

        [Tooltip("Vehicle this door belongs to. Auto-resolved from the parents if left empty.")]
        [SerializeField] private MountModule mountLock;

        /// <summary>
        /// Which switch on this entity we are, so a message addressed to the ship can say which
        /// door it means — ShipRV carries a cockpit door and a garage door on one NetworkObject.
        ///
        /// Positional, and that is a deliberate trade. Every machine in a session runs the same
        /// prefab out of the same build, so the child order they enumerate is identical and the
        /// indices agree without anything being authored or serialized. It does NOT survive
        /// reordering the prefab's children between builds — which is fine, because these indices
        /// never outlive a session, but is worth knowing before someone rearranges a hierarchy and
        /// wonders why the wrong door opens.
        /// </summary>
        private int switchIndex;

        private void Awake()
        {
            if (parts == null || parts.Length == 0)
            {
                ArticulatedPart own = GetComponent<ArticulatedPart>();
                parts = own ? new[] { own } : new ArticulatedPart[0];
            }

            if (!mountLock)
                mountLock = GetComponentInParent<MountModule>();

            ResolveSwitchIndex();
        }

        private void ResolveSwitchIndex()
        {
            GameObject root = NetChannel.RootOf(this);
            if (root == null) return;

            var siblings = root.GetComponentsInChildren<ArticulatedPartInteraction>(true);
            for (int i = 0; i < siblings.Length; i++)
            {
                if (siblings[i] == this)
                {
                    switchIndex = i;
                    return;
                }
            }
        }

        private void OnEnable()
        {
            this.NetOn(NetMsg.PartToggle, OnToggleRequested);
            this.NetOn(NetMsg.PartState, OnStateAnnounced);

            // A joining client asks what state this group is in.
            StartCoroutine(this.NetToServerWhenSpawned(NetMsg.PartToggle,
                                                       new NetArg { A = switchIndex, B = AskVerb }));
        }

        private void OnDisable()
        {
            this.NetOff(NetMsg.PartToggle, OnToggleRequested);
            this.NetOff(NetMsg.PartState, OnStateAnnounced);
        }

        // ── Wire verbs. See NetMsg.PartToggle for the table. ──
        private const int AskVerb = -1;
        private const int CloseVerb = 0;
        private const int OpenVerb = 1;
        private const int CloseInstantVerb = 2;
        private const int OpenInstantVerb = 3;

        public bool CanInteract()
        {
            if (parts == null || parts.Length == 0)
                return false;

            if (lockedWhileMounted && mountLock && mountLock.IsMounted)
                return false;

            if (blockWhileMoving)
            {
                foreach (ArticulatedPart part in parts)
                    if (part && part.IsMoving)
                        return false;
            }

            return true;
        }

        public void Interact(Interactor interactor)
        {
            if (!CanInteract())
                return;

            // The press only ASKS. The server decides and tells everyone, including us — so a door
            // is never open on the machine that pressed it and shut for everybody else. Offline
            // this dispatches locally and lands in the same handler on the same frame.
            this.NetToServer(NetMsg.PartToggle,
                             new NetArg { A = switchIndex, B = NextState() ? OpenVerb : CloseVerb });
        }

        /// <summary>
        /// Where one press takes the group. Mixed states resolve toward "close everything", so a
        /// press always leaves the group in a single predictable state.
        /// </summary>
        /// <summary>
        /// SERVER: open or close the group as if pressed, for something other than a player doing
        /// it — the crash bursting the lander's back door open. Same apply and announce as a press,
        /// so every machine, late joiners and the door's saver see one ordinary door state.
        /// </summary>
        public void SetOpenByAuthority(bool open)
        {
            if (!Network.Simulates(this)) return;

            Apply(open, instant: false);
            Announce(open, instant: false);
        }

        private bool NextState()
        {
            foreach (ArticulatedPart part in parts)
                if (part && part.IsOpen)
                    return false;

            return true;
        }

        /// <summary>Server side: decide, then tell everyone. Also answers a joiner's question.</summary>
        private void OnToggleRequested(in NetArg arg, ulong sender)
        {
            if (arg.A != switchIndex) return;
            if (!Network.Simulates(this)) return;

            if (arg.B == AskVerb)
            {
                Announce(IsGroupOpen(), instant: true);
                return;
            }

            // Re-checked here and not trusted from the sender: the client that pressed cannot know
            // whether somebody climbed into the pilot seat while its message was in flight.
            if (!CanInteract()) return;

            bool open = arg.B == OpenVerb;
            Apply(open, instant: false);
            Announce(open, instant: false);
        }

        /// <summary>Every machine: move the parts.</summary>
        private void OnStateAnnounced(in NetArg arg, ulong sender)
        {
            if (arg.A != switchIndex) return;

            bool open = arg.B == OpenVerb || arg.B == OpenInstantVerb;
            bool instant = arg.B == OpenInstantVerb || arg.B == CloseInstantVerb;

            Apply(open, instant);
        }

        private void Announce(bool open, bool instant)
        {
            int verb = instant ? (open ? OpenInstantVerb : CloseInstantVerb)
                               : (open ? OpenVerb : CloseVerb);

            this.NetToAll(NetMsg.PartState, new NetArg { A = switchIndex, B = verb });
        }

        private void Apply(bool open, bool instant)
        {
            foreach (ArticulatedPart part in parts)
                if (part)
                    part.SetOpen(open, instant);
        }

        private bool IsGroupOpen()
        {
            foreach (ArticulatedPart part in parts)
                if (part && part.IsOpen)
                    return true;

            return false;
        }
    }
}
