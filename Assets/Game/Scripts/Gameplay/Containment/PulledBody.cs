// A body with a container's beam on it: the one thing it can do about that, and the moment it
// stops being a body.
//
// The struggle INPUT is the only part of a pull that has to run on the captive's own machine,
// because it is the only part only that machine can know: the keys they are pressing. An accepted
// press becomes one NetMsg.SnareStruggled on the CAPTOR's relay and nothing else. The meter here
// decides which presses are worth sending; the authority keeps a meter of its own to decide what
// they are worth — see ContainmentPull.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Items;

namespace SpaceGame.Gameplay.Containment
{
    /// <summary>
    /// One body being drawn into a container.
    ///
    /// <para>
    /// Added on demand rather than authored on a prefab, because anything can be aimed at at any
    /// time — the same shape and reasoning as <see cref="SnaredBody.Ensure"/>. It has no Awake and
    /// must not grow one: Unity does not raise Awake for an AddComponent outside play mode, so
    /// anything cached there would be null in an EditMode test and the pull would silently read no
    /// keys.
    /// </para>
    /// <para>
    /// <b>It applies no restraint.</b> A body being pulled is still walking, still fighting, still
    /// shooting back — the pull is a race, not a hold. The restraint only arrives at the end of it,
    /// and only for a player (<see cref="BottledPlayer"/>); everything else simply stops existing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class PulledBody : MonoBehaviour
    {
        /// <summary>Whose relay the struggle reports go on. See the file header.</summary>
        private GameObject captor;

        private ContainerHold container;
        private ContainmentSettings settings;

        /// <summary>This body's own send throttle, on its own machine only.</summary>
        private SnareStruggleMeter sendMeter;

        /// <summary>
        /// This body's keys, and the memory of which way they last pushed.
        ///
        /// Shared with the net gun and the leash's hogtie rather than written a third time: three
        /// systems hold the same body for different reasons and ask it the same question. See
        /// <see cref="SnareStruggleReader"/> for what could not safely be duplicated — chiefly the
        /// menu gate, which cannot be the shared hotkey gate every other control uses.
        /// </summary>
        private readonly SnareStruggleReader input = new SnareStruggleReader();

        private bool pulled;

        /// <summary>Is a container drawing this body in right now?</summary>
        public bool IsPulled => pulled;

        /// <summary>
        /// The fight is over and this body is going in. Every machine, raised while the body is
        /// still there to be seen — which is the whole reason the announcement precedes the
        /// despawn. The argument is the container it went into.
        /// </summary>
        public event System.Action<ContainerHold> Folded;

        public static PulledBody Ensure(GameObject body)
        {
            if (body == null) return null;

            return body.TryGetComponent(out PulledBody existing)
                ? existing
                : body.AddComponent<PulledBody>();
        }

        /// <summary>This body's pull if it has one. Never creates — for teardown paths.</summary>
        public static PulledBody Find(GameObject body) =>
            body != null && body.TryGetComponent(out PulledBody existing) ? existing : null;

        /// <summary>
        /// A container has hold of this body. Runs on EVERY machine, because the one that matters
        /// is the captive's own and nothing at the call site knows which that is.
        /// </summary>
        public void Begin(GameObject pullingCaptor, ContainerHold hold, ContainmentSettings pullSettings)
        {
            if (pullingCaptor == null || hold == null) return;

            // The same container beginning again is not a new pull: rebuilding the meter would wipe
            // the throttle this body has already banked and let a held key through as a burst.
            if (pulled && captor == pullingCaptor && container == hold) return;

            End();

            captor = pullingCaptor;
            container = hold;
            settings = pullSettings ?? new ContainmentSettings();
            sendMeter = new SnareStruggleMeter(settings.MaxUsefulStruggleRate,
                                               settings.StruggleDecaySeconds);
            input.ForgetHeading();

            // Registered here rather than in an OnEnable this component deliberately does not have,
            // and dropped in End, so the subscription lasts exactly as long as the pull. On THIS
            // body's own relay, because the listener is this component: NetMsg.Contained is
            // addressed to the captive precisely so a machine that can still see the body hears
            // about it on the body's own channel.
            this.NetOn(NetMsg.Contained, OnContained);

            pulled = true;
        }

        /// <summary>
        /// Let go, locally and silently. Safe from anywhere and safe twice.
        ///
        /// Says nothing on the wire on purpose: every route that ends a pull is announced by the
        /// captor, which is the machine that decided it, and a second announcement from here would
        /// be a captive reporting its own escape.
        /// </summary>
        public void End()
        {
            if (!pulled) return;

            pulled = false;

            this.NetOff(NetMsg.Contained, OnContained);

            captor = null;
            container = null;
            settings = null;
            sendMeter = null;
            input.ForgetHeading();
            input.Release();
        }

        /// <summary>
        /// Advance one step. The seam the EditMode tests use, and the reason the input arrives as
        /// arguments rather than being read in here: a test can supply presses, and an
        /// <c>InputControls</c> cannot be driven from one.
        ///
        /// <para>
        /// The meter is advanced on every machine that runs one, so a peer's decays rather than
        /// latching at whatever it last heard. Only the owner may ADD to it, because only the owner
        /// knows what the captive is pressing.
        /// </para>
        /// </summary>
        public void Step(float delta, bool jumpPressed, Vector2 move)
        {
            if (!pulled || sendMeter == null) return;

            sendMeter.Advance(delta);

            if (!Network.Owns(this)) return;

            // The reader measures the reversal against the last direction and then remembers this
            // one; the other order compares every input against itself and nothing is a reversal.
            bool struggled = input.Counts(jumpPressed, move,
                                          settings.StruggleMoveDeadzone,
                                          settings.StruggleReversalDot);
            if (!struggled) return;

            // Offered rather than added. Push answering false means this input landed inside the
            // cooldown and was discarded, and that is the throttle on the WIRE as much as on the
            // meter — a rejected input must not be reported either, or a held key becomes a message
            // per frame.
            if (sendMeter.Push()) Report();
        }

        /// <summary>
        /// Tell the authority this body fought, once.
        ///
        /// <para>
        /// <b>On the CAPTOR's relay, not on this body's.</b> The only listener is
        /// <see cref="ContainmentPull"/> and it lives on the captor; <c>NetOn</c> registers against
        /// the channel of the entity the listener sits under, and a relay delivers to the channel
        /// of its own entity. Sent from this body's relay the message would arrive at this body's
        /// channel, where nothing is subscribed, and be dropped without a word on every machine.
        /// The wire allows the crossing: <c>NetRelay</c>'s server RPC is
        /// <c>InvokePermission.Everyone</c>, which is what lets a captive send on the relay of the
        /// player drawing them in — the same crossing an attacker makes for <c>NetMsg.Damage</c>.
        /// </para>
        /// <para>
        /// The level is not sent, only the fact of the input. The server keeps its own meter under
        /// the same authored cap and works the load out from a run of these, because a level
        /// computed here would be a number the client chooses, and the escape is not the client's
        /// to decide (GDC-L1-MP-0004).
        /// </para>
        /// </summary>
        private void Report()
        {
            if (captor == null) return;

            NetMessaging.NetSendTo(captor, NetMsg.SnareStruggled,
                                   new NetArg().With(gameObject), NetTo.Server);
        }

        /// <summary>
        /// The fight is over. Every machine hears this while the body is still here.
        ///
        /// <para>
        /// A player is HELD from here rather than recorded — on every machine, because a peer that
        /// skipped the hold would watch a bottled player walk about while every other machine sees
        /// them gone. Everything else is despawned by the authority a moment later and arrives on a
        /// peer as an ordinary despawn, so there is nothing for a peer to do but play it.
        /// </para>
        /// </summary>
        private void OnContained(in NetArg arg, ulong sender)
        {
            if (!pulled) return;

            ContainerHold hold = container;
            GameObject captorObject = captor;

            if (hold != null)
            {
                hold.MarkToldFull(true);

                if (ContainmentFit.IsPlayer(gameObject))
                {
                    bool authority = captorObject != null && Network.Simulates(captorObject.transform);
                    BottledPlayer.Ensure(gameObject)?.Bottle(hold, hold.Settings, authority);
                }
            }

            End();

            Folded?.Invoke(hold);
        }

        /// <summary>
        /// Read this body's keys and hand them to <see cref="Step"/>.
        ///
        /// The gate is <see cref="SnareStruggleReader.MayRead"/>, which is NOT the shared
        /// <c>AcceptsGameplayInput</c> hotkey gate. See there for the full reason.
        /// </summary>
        private void Update()
        {
            if (!pulled) return;

            input.Poll(SnareStruggleReader.MayRead(Network.Owns(this)),
                       out bool jumpPressed, out Vector2 move);

            Step(Time.deltaTime, jumpPressed, move);
        }

        /// <summary>
        /// Teardown: let go locally, and give the input asset back.
        ///
        /// Reached when the body is destroyed or its chunk unloads. The input asset is the one
        /// thing here that outlives this component if nobody disposes it, which is why
        /// <see cref="End"/> is not enough on its own.
        /// </summary>
        private void OnDisable()
        {
            End();
            input.Release();
        }
    }
}
