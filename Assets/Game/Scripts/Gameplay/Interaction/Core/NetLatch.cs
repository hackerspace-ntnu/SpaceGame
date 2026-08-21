// One networked on/off state, and the whole protocol for keeping every machine agreeing about it.
//
// It exists because doors and levers were plain MonoBehaviours with no netcode at all: pressing E
// swung the door on the machine that pressed it and nowhere else. That is worse than it looks,
// because DoorInteraction.IsOpen is what SandstormShelter reads to decide whether the inside of a
// ship is safe — two players standing in the same hull disagreed about whether they were being
// sanded, with nothing on screen to explain it. A lever was worse still: it fires a UnityEvent that
// may enable a hidden door or open a portal, and only the puller's machine ever saw the consequence.
//
// The shape is lifted from ArticulatedPartInteraction, deliberately, because it is the same problem:
// shared world state, so the server owns it, clients only ask, everyone is told, and a late joiner
// can ask what they missed. What is new is that none of it lives in the fixture. A door and a lever
// differ in exactly two ways — what they do when the state changes, and whether the state is allowed
// to travel back — so both are constructor arguments and the third fixture costs nothing.
//
// A plain class rather than a MonoBehaviour, on purpose. A latch is not a thing in the scene, it is
// a field of a thing in the scene, and one fixture may own several. It borrows its owner's channel,
// its coroutines and its lifetime, and the owner drives it from OnEnable/OnDisable.
using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using SpaceGame.Core;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// A component that owns one or more <see cref="NetLatch"/>es.
    ///
    /// The interface exists only so the latches on an entity can be NUMBERED, since a message
    /// addressed to a ship has to be able to say which door it means. Implementing it is what puts
    /// a fixture into that numbering; a fixture that owns latches and does not implement it will
    /// warn and fall back to its own slot numbers, which collide with everybody else's.
    /// </summary>
    public interface ILatchHost
    {
        /// <summary>
        /// How many latches this component owns.
        ///
        /// Must be answerable BEFORE Awake — a constant, or a serialized field — because the
        /// numbering is resolved by walking every host on the entity and a host further down the
        /// hierarchy may not have constructed its latches yet. Almost every fixture returns 1.
        /// </summary>
        int LatchCount { get; }
    }

    /// <summary>
    /// A binary piece of shared world state — a door's open/shut, a lever's pulled/rest — that the
    /// server owns and every machine is told about.
    /// </summary>
    public sealed class NetLatch
    {
        // ── Wire verbs. The table lives above NetMsg.LatchSet; this mirrors it. ──
        private const int AskVerb = -1;
        private const int OffVerb = 0;
        private const int OnVerb = 1;
        private const int OffInstantVerb = 2;
        private const int OnInstantVerb = 3;

        private readonly MonoBehaviour owner;
        private readonly int slot;
        private readonly bool oneWay;
        private readonly Action<bool, bool> onApply;
        private readonly Func<bool> canChange;

        private int index = -1;
        private bool subscribed;
        private Coroutine askRoutine;

        /// <param name="owner">
        /// The fixture. Supplies the channel the messages ride, the coroutine runner for the
        /// late-joiner query, and the lifetime — a latch never outlives its component.
        /// </param>
        /// <param name="onApply">
        /// Move the fixture to <c>(on, instant)</c>. Called on EVERY machine, including the one that
        /// pressed the key, and never called twice for the same state — see <see cref="Apply"/>.
        /// "Instant" means the state was already true before this machine was looking, so land in it
        /// rather than animating into it, and stay quiet about it.
        /// </param>
        /// <param name="canChange">
        /// Whether the fixture will accept a change at all right now — mid-swing, locked, in use.
        /// Asked on the machine that presses AND again on the server, because the client that
        /// pressed cannot know what happened while its message was in flight.
        /// </param>
        /// <param name="oneWay">
        /// True for a latch that travels once and stays there: a one-shot lever. Modelled as a
        /// refusal rather than a second class, so every other path is shared.
        /// </param>
        /// <param name="slot">Which of the owner's own latches this is, when it owns more than one.</param>
        public NetLatch(MonoBehaviour owner, Action<bool, bool> onApply,
                        Func<bool> canChange = null, bool oneWay = false, int slot = 0)
        {
            // Thrown rather than tolerated: both are wiring the fixture author controls, a latch
            // without them is silently inert, and silently inert is the exact failure mode this
            // whole file exists to end.
            this.owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            this.onApply = onApply ?? throw new ArgumentNullException(nameof(onApply));

            this.canChange = canChange;
            this.oneWay = oneWay;
            this.slot = Mathf.Max(0, slot);
        }

        /// <summary>
        /// The state the whole session agrees this latch is in.
        ///
        /// Starts false, so a fixture must be AUTHORED in its off state — a shut door, a lever at
        /// rest. Flips the moment a change is applied rather than when the animation lands: the
        /// player has committed by then, and delaying the consequence by the swing duration reads
        /// as a bug to everyone who is not the one who pressed.
        /// </summary>
        public bool IsOn { get; private set; }

        /// <summary>Where one press takes this latch. A one-way latch only ever asks for "on".</summary>
        public bool Next => oneWay || !IsOn;

        /// <summary>
        /// Would a request to put this latch in <paramref name="on"/> be accepted?
        ///
        /// The fixture's CanInteract should be exactly this, so the crosshair and the key can never
        /// disagree; the server asks it again on arrival.
        /// </summary>
        public bool Accepts(bool on)
        {
            // A one-way latch refuses the journey back, and refuses a second trip out.
            if (oneWay && (IsOn || !on)) return false;

            return canChange == null || canChange();
        }

        /// <summary>
        /// Which latch on this entity we are — the <c>A</c> field of every message this latch sends.
        ///
        /// Positional, over every <see cref="ILatchHost"/> under the entity, and that is a deliberate
        /// trade (the same one ArticulatedPartInteraction documents). Every machine in a session runs
        /// the same prefab out of the same build, so the order they enumerate is identical and the
        /// numbers agree without anything being authored or serialized. It does NOT survive
        /// reordering a prefab's children between builds — which is fine, because these numbers never
        /// outlive a session, but is worth knowing before someone rearranges a hierarchy and wonders
        /// why the wrong door opens.
        /// </summary>
        public int Index
        {
            get
            {
                if (index < 0) index = ResolveIndex();
                return index;
            }
        }

        private int ResolveIndex()
        {
            GameObject root = NetChannel.RootOf(owner);
            if (root == null) return slot;

            int running = 0;
            foreach (ILatchHost host in root.GetComponentsInChildren<ILatchHost>(true))
            {
                if (ReferenceEquals(host, owner)) return running + slot;
                running += Mathf.Max(0, host.LatchCount);
            }

            // The owner is not an ILatchHost, or does not sit under the entity we resolved. Either
            // way the numbering this machine produces is not the numbering the others produce, so
            // say so loudly and fall back to the raw slot — a lone latch on the entity then still
            // works, which is the case somebody is most likely to have got away with.
            Debug.LogWarning($"[NetLatch] '{owner.name}' owns a latch but is not an ILatchHost under " +
                             "its own entity, so its latch numbers cannot be matched against the " +
                             "other machines'. Implement ILatchHost on it.", owner);
            return slot;
        }

        // ─────────── Lifetime. Drive these from the fixture's OnEnable/OnDisable. ───────────

        /// <summary>Start listening, and ask the server what we missed. Call from OnEnable.</summary>
        public void Enable()
        {
            if (subscribed || owner == null) return;
            subscribed = true;

            owner.NetOn(NetMsg.LatchSet, OnSetRequested);
            owner.NetOn(NetMsg.LatchState, OnStateAnnounced);

            // Only when there is a server to ask and we are not it. Deciding here rather than in the
            // coroutine's first line costs nothing — StartCoroutine runs a body up to its first
            // yield inline, so a coroutine that bails immediately and a coroutine never started are
            // the same thing — and it keeps a whole class of hosts out of the coroutine machinery:
            // an EditMode test, or a scene opened straight from the editor, has no business there.
            if (Network.IsNetworked && !Network.Server && owner.isActiveAndEnabled)
                askRoutine = owner.StartCoroutine(AskWhenConnected());
        }

        /// <summary>
        /// Stop listening. Call from OnDisable — every Enable needs one, or the handler outlives the
        /// component and a disabled door still swings for a message it should no longer hear.
        /// </summary>
        public void Disable()
        {
            if (!subscribed) return;
            subscribed = false;

            // NetOff on a destroyed owner is safe (it finds, never creates) and is exactly the
            // OnDisable-during-OnDestroy case, so it is not guarded. StopCoroutine is.
            owner.NetOff(NetMsg.LatchSet, OnSetRequested);
            owner.NetOff(NetMsg.LatchState, OnStateAnnounced);

            if (askRoutine != null && owner != null) owner.StopCoroutine(askRoutine);
            askRoutine = null;
        }

        // ─────────── Asking ───────────

        /// <summary>Ask for this latch to become <paramref name="on"/>. Equivalent to one press.</summary>
        public void Toggle() => Request(Next);

        /// <summary>
        /// Ask for this latch to be <paramref name="on"/>.
        ///
        /// Nothing moves here. The press only ASKS: the server decides and tells everyone including
        /// us, so a door is never open on the machine that pressed it and shut for everybody else.
        /// Offline — and for a fixture with no NetworkObject above it, which is most of them, in
        /// interiors and chunk props — this dispatches locally and lands in the handler below on the
        /// same frame, which is exactly what the game did before it had netcode.
        /// </summary>
        public void Request(bool on)
        {
            if (owner == null || !Accepts(on)) return;

            owner.NetToServer(NetMsg.LatchSet, new NetArg { A = Index, B = on ? OnVerb : OffVerb });
        }

        /// <summary>
        /// Ask the server what state this latch is in, once there is somebody to ask.
        ///
        /// Waits for the entity's NetworkObject to actually be spawned rather than sending on the
        /// first frame: before that there is no relay, the send falls through to a local dispatch,
        /// and the client cheerfully answers its own question with the state it already had — which
        /// is the prefab's, which is the thing being corrected.
        ///
        /// Only reached on a client of a live session — <see cref="Enable"/> makes that decision.
        /// </summary>
        private IEnumerator AskWhenConnected()
        {
            GameObject root = NetChannel.RootOf(owner);
            NetworkObject netObj = root != null ? root.GetComponent<NetworkObject>() : null;

            // No NetworkObject means no wire: nobody to ask, and nobody who would hear the answer.
            // The latch still works on this machine alone — NetChannel.WarnUnrelayed says so once.
            if (netObj == null) yield break;

            while (!netObj.IsSpawned)
            {
                if (!Network.IsNetworked) yield break;
                yield return null;
            }

            owner.NetToServer(NetMsg.LatchSet, new NetArg { A = Index, B = AskVerb });
        }

        // ─────────── Answering ───────────

        /// <summary>Server side: decide, then tell everyone. Also answers a joiner's question.</summary>
        private void OnSetRequested(in NetArg arg, ulong sender)
        {
            if (arg.A != Index) return;

            // Network.Simulates and not a plain "am I the server", on purpose. A door in a chunk
            // scene or an interior has no NetworkObject above it, so there was no wire, the request
            // fell through to a local dispatch, and THIS machine is the only authority that door has.
            // A server test would refuse it on every client and the door would never open for them.
            if (!Network.Simulates(owner)) return;

            if (arg.B == AskVerb)
            {
                Announce(IsOn, instant: true);
                return;
            }

            bool on = arg.B == OnVerb || arg.B == OnInstantVerb;

            // Both halves of being idempotent. The state test drops a duplicate request — two
            // players pressing the same panel on the same frame, or a client retrying. The Accepts
            // test is re-run here rather than trusted from the sender, because the client that
            // pressed cannot know that somebody else already pulled the lever, or that the swing is
            // still in flight, while its message was crossing the wire.
            if (on == IsOn || !Accepts(on)) return;

            Apply(on, instant: false);
            Announce(on, instant: false);
        }

        /// <summary>Every machine: move the fixture.</summary>
        private void OnStateAnnounced(in NetArg arg, ulong sender)
        {
            if (arg.A != Index) return;

            bool on = arg.B == OnVerb || arg.B == OnInstantVerb;
            bool instant = arg.B == OnInstantVerb || arg.B == OffInstantVerb;

            Apply(on, instant);
        }

        private void Announce(bool on, bool instant)
        {
            int verb = instant ? (on ? OnInstantVerb : OffInstantVerb)
                               : (on ? OnVerb : OffVerb);

            // Sent from inside a handler, so on the host this re-enters Dispatch on this very
            // channel — see the buffer-pool comment in NetChannel, which is written for exactly this
            // round trip. The re-entrant delivery is also how the host applies its own change, and
            // it is harmless because Apply above has already committed the state.
            owner.NetToAll(NetMsg.LatchState, new NetArg { A = Index, B = verb });
        }

        /// <summary>
        /// Restore-only. Called by the save system; do not call from gameplay.
        ///
        /// Puts the latch straight into <paramref name="on"/> without asking <see cref="Accepts"/>,
        /// which is the whole difference from <see cref="Request"/>: a one-way latch refuses to
        /// travel a second time, and a restored one-shot lever has to ARRIVE pulled rather than
        /// being pulled again. Applied instantly and silently for the same reason a late joiner's
        /// state is — the fixture was already in this pose when the world was saved, and swinging
        /// every door in the world open on load is a world that greets you by unfolding itself.
        ///
        /// Announced afterwards so a client already in the session hears about a mid-session load.
        /// Only from the machine that owns the fixture; offline that collapses to a local dispatch
        /// which lands back in <see cref="Apply"/> and is ignored, the state having already changed.
        /// </summary>
        public void Restore(bool on)
        {
            if (on == IsOn) return;

            Apply(on, instant: true);

            if (owner != null && Network.Simulates(owner)) Announce(on, instant: true);
        }

        private void Apply(bool on, bool instant)
        {
            // The one rule that makes every path idempotent: a latch acts only when the state it is
            // being told about differs from the state it is already in. Three separate things need
            // it.
            //
            // The host applies twice for every change — once when its own server handler decides,
            // and once more when SendTo.ClientsAndHost hands the broadcast back to it — and the
            // second pass must not restart a swing that is already under way.
            //
            // A joiner's "what is this latch?" is answered to EVERYONE, because this layer has no
            // unicast. Every machine that already agrees has to ignore it, or one player walking up
            // to a door would snap it shut on somebody else's screen mid-swing.
            //
            // And a lever's UnityEvent must fire once per pull, not once per message that mentions
            // it — which is the difference between opening a hidden door and opening it twice.
            if (on == IsOn) return;

            IsOn = on;
            onApply(on, instant);
        }
    }
}
