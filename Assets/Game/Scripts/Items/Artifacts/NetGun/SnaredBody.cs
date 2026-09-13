// A netted player: put on the floor, and given one thing left to do about it.
//
// This used to be the half of a net that ONLY the netted player's machine could run, because what
// it did was write a position — and a player's body is owner-authoritative, so a write from
// anywhere else is overwritten within a tick, silently. A net no longer writes a position at all.
// It puts the body limp, which is presentation every machine performs off the capture it was
// already told about, and RagdollRig's own Drives split then decides which machine is entitled to
// drive the root and which merely watches it flail. So the hold runs everywhere.
//
// The struggle INPUT is the one part that stays owner-only, because it is the one part only one
// machine can know: the keys the captive is pressing. An accepted press becomes one
// NetMsg.SnareStruggled on the shooter's relay and nothing else; the meter here exists to decide
// which presses are worth sending, and the authority keeps a meter of its own to decide what they
// cost the net.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;

namespace SpaceGame.Items
{
    /// <summary>
    /// One netted player: the hold that put them down, and the struggle that gets them up.
    ///
    /// <para>
    /// Added on demand rather than authored on the prefab, because any player can be netted at any
    /// time. Same shape and reasoning as <see cref="LassoedBody.Ensure"/>.
    /// </para>
    /// <para>
    /// <b>Nothing here is gated on <see cref="Network.Owns"/> except the struggle input.</b> That
    /// is a change from the constraint this replaced, which had to be owner-only — see the file
    /// header. The two gates that remain are both on the input, in <see cref="Update"/> and in
    /// <see cref="Step"/>; neither is in <see cref="Bind"/>, because a peer that skipped the hold
    /// would be watching a captive stand up and walk about while every other machine sees them on
    /// the floor.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // added in code, never by hand
    public sealed class SnaredBody : MonoBehaviour
    {
        private PlayerRagdoll ragdoll;
        private HealthComponent health;
        private SnareStruggle settings;
        private Transform anchor;
        private SnareStruggleMeter meter;

        /// <summary>
        /// The captive's own keys, and the memory of which way they last pushed.
        ///
        /// Shared with <see cref="Hogtie"/> rather than written twice: a net and a tie hold the
        /// same body for different reasons and ask it the same question. See
        /// <see cref="SnareStruggleReader"/> for what could not be duplicated safely.
        /// </summary>
        private readonly SnareStruggleReader input = new SnareStruggleReader();

        private bool bound;

        public bool IsBound => bound;

        /// <summary>How hard this captive is fighting, 0-1. Zero when nothing has them.</summary>
        public float StruggleLevel => meter?.Level ?? 0f;

        public static SnaredBody Ensure(GameObject player)
        {
            if (player == null) return null;

            return player.TryGetComponent(out SnaredBody existing)
                ? existing
                : player.AddComponent<SnaredBody>();
        }

        /// <summary>
        /// The ragdoll adapter that owns this body, resolved on demand.
        ///
        /// <para>
        /// Not cached from <see cref="Awake"/>, and this component deliberately has no Awake at
        /// all: it is added at runtime by <see cref="Ensure"/>, and Unity does not raise Awake for
        /// an AddComponent outside play mode — so in an EditMode test, and in any editor tooling
        /// that lands a net, the field would still be null when the hold is applied and the net
        /// would silently hold nothing. The same trap <see cref="LassoedBody"/> documents for its
        /// own Rigidbody, and the one this file used to carry a note about.
        /// </para>
        /// <para>
        /// From the PARENT, not from this object alone. <c>SnareCatch.Capture</c> binds whatever
        /// GameObject the capture query returned, which for a body with its collider on a child is
        /// that child rather than the root the adapter sits on.
        /// </para>
        /// </summary>
        private PlayerRagdoll Ragdoll =>
            ragdoll != null ? ragdoll : ragdoll = GetComponentInParent<PlayerRagdoll>();

        /// <summary>
        /// This captive's health, resolved on demand for the reason <see cref="Ragdoll"/> is: there
        /// is no Awake to cache it in. From the PARENT for the same reason too.
        /// </summary>
        private HealthComponent Vitals =>
            health != null ? health : health = GetComponentInParent<HealthComponent>();

        /// <summary>
        /// Take hold: put the player on the floor and start counting their struggle.
        /// </summary>
        /// <returns>
        /// False when the hold did not take, and the caller must then record no capture at all.
        /// Another net already has this player is one reason; the body refusing to go down is the
        /// other, and <c>PlayerRagdoll.HoldDown</c> lists what those are — a corpse, a body a seat
        /// or a saddle is already placing, or a rig whose skeleton build kept no bones.
        ///
        /// <para>
        /// A capture recorded over a body that never went down is worse than no capture: the net
        /// spends its pool holding somebody who is walking around, and it is the shooter who is
        /// punished for it. There is no half measure available here the way there is for a creature
        /// — <see cref="SnareTether"/> can still hobble a NavMeshAgent it failed to fell, and a
        /// player has no such dial to turn that would not be taking control away by another name.
        /// Where the tether has nothing to fall back on either, it refuses the same way this does.
        /// </para>
        /// </returns>
        public bool Bind(Transform netAnchor, SnareStruggle struggleSettings)
        {
            if (bound && anchor != netAnchor) return false;

            // The same net binding again is not a re-catch. Answering true without rebuilding the
            // meter is what stops it wiping the struggle the captive has already banked.
            if (bound) return true;

            PlayerRagdoll body = Ragdoll;
            if (body == null || !body.HoldDown(this)) return false;

            settings = struggleSettings ?? new SnareStruggle();

            // Subscribed here rather than in an Awake this component deliberately does not have,
            // and unsubscribed in Release, so the subscription lasts exactly as long as the hold.
            HealthComponent vitals = Vitals;
            if (vitals != null) vitals.OnDeath += OnCaptiveDied;

            anchor = netAnchor;
            meter = new SnareStruggleMeter(settings.MaxUsefulStruggleRate,
                                           settings.StruggleDecaySeconds);
            input.ForgetHeading();
            bound = true;
            return true;
        }

        /// <summary>
        /// Let go. Only the net that took hold may, so an unrelated net's expiry frees nobody.
        ///
        /// <para>
        /// This gives back the claim this net took, and no more. The player stands up only once
        /// every claim is given back — see <c>PlayerRagdoll.ReleaseHold</c> — so a net rotting off
        /// a captive something else is also holding leaves them where they are.
        /// </para>
        /// </summary>
        public void Release(Transform netAnchor)
        {
            if (!bound || (netAnchor != null && netAnchor != anchor)) return;

            bound = false;
            anchor = null;
            meter = null;
            settings = null;
            input.ForgetHeading();
            input.Release();

            HealthComponent vitals = Vitals;
            if (vitals != null) vitals.OnDeath -= OnCaptiveDied;

            PlayerRagdoll body = Ragdoll;
            if (body != null) body.ReleaseHold(this);
        }

        /// <summary>
        /// The captive died under the net. Let go of them at that moment rather than at the net's.
        ///
        /// <para>
        /// A corpse is not a captive. <c>PlayerRagdoll.OnDeath</c> drops the hold's claim on its
        /// own — a corpse is already limp and stays that way — but nothing there knows about the
        /// net, so without this the binding outlives the player: <see cref="Update"/> goes on
        /// reading a dead player's keys (the menu gate is open for a corpse, there being no menu),
        /// and the struggle they cannot have stopped goes on being reported and billed to the
        /// shooter's net — which would read as a balance problem rather than the lifecycle one it
        /// is.
        /// </para>
        /// <para>
        /// The captive stays in <c>SnareCatch</c>'s own captive list until the net rots — this ends
        /// the struggle, not the base load of a body lying in the net. The authority's own meter
        /// for them is not cleared here and does not need to be: nothing pushes it once the reports
        /// stop, so it decays back to a corpse's flat <c>ReferenceLoad</c> within a few seconds of
        /// the death. Not instantly, and the decay is exponential rather than linear: at the
        /// authored 1.2 s a saturated meter is still worth 0.19 two seconds later — about a third
        /// of an extra captive — and takes four or five to come within a twentieth of the flat
        /// load. That is a short tail on a body nobody is fighting with any more, not a corpse
        /// eating the net.
        /// </para>
        /// </summary>
        private void OnCaptiveDied() => Release(anchor);

        /// <summary>
        /// Let go no matter which net asks. For teardown only — a chunk unloading under a net must
        /// not leave a player limp forever with nothing left alive to stand them up.
        ///
        /// <para>
        /// <see cref="SnareTether"/> repeats its restraint unconditionally underneath the same
        /// line and this does not, because the two have different things to be unsure about: a
        /// stranded hobble there writes a SERIALIZED field that a quit-time autosave would capture,
        /// where everything here is runtime state that dies with the object. The one exception is
        /// the input asset, which outlives this component if nobody disposes it — so that is the
        /// line repeated here.
        /// </para>
        /// </summary>
        private void OnDisable()
        {
            if (bound) Release(anchor);

            input.Release();
        }

        /// <summary>
        /// Advance one step. The seam the EditMode tests use, and the reason the input arrives as
        /// arguments rather than being read in here: a test can supply presses, and an
        /// <c>InputControls</c> cannot be driven from one.
        ///
        /// <para>
        /// The meter is advanced on every machine, so a peer's copy decays rather than latching at
        /// whatever it last heard. Only the owner may ADD to it, because only the owner knows what
        /// the captive is pressing.
        /// </para>
        /// </summary>
        public void Step(float delta, bool jumpPressed, Vector2 move)
        {
            if (!bound || meter == null || settings == null) return;

            meter.Advance(delta);

            if (!Network.Owns(this)) return;

            // The reader measures the reversal against the last direction and then remembers this
            // one; doing it the other way round compares every input against itself.
            bool struggled = input.Counts(jumpPressed, move,
                                          settings.StruggleMoveDeadzone,
                                          settings.StruggleReversalDot);

            if (!struggled) return;

            // Offered rather than added. Push answering false means this input landed inside the
            // cooldown and was discarded, and that is the throttle on the WIRE as much as on the
            // meter — a rejected input must not be reported either, or the cooldown throttles
            // nothing and a held key becomes a message per frame.
            if (meter.Push()) ReportStruggle();
        }

        /// <summary>
        /// Tell the authority this captive fought, once.
        ///
        /// <para>
        /// <b>On the SHOOTER's relay, not on this player's.</b> The only listener is
        /// <see cref="SnareReceiver"/> and it lives on the shooter; <c>NetOn</c> registers against
        /// the channel of the entity the listener sits under, and a relay delivers to the channel
        /// of its own entity. Sent from this body's relay the message would arrive at this body's
        /// channel, where nothing is subscribed, and be dropped without a word. The wire allows the
        /// crossing: <c>NetRelay</c>'s server RPC is <c>InvokePermission.Everyone</c>, which is what
        /// lets a captive send on the relay of the player who shot them — the same crossing an
        /// attacker makes to send <c>NetMsg.Damage</c> on their victim's.
        /// </para>
        /// <para>
        /// The level is not sent, only the fact of the input. The server keeps its own meter,
        /// rate-limited by the same authored cap this one is, and works out the load from a run of
        /// these — because a level computed here would be a number the client chooses, and the
        /// escape is not the client's to decide (GDC-L1-MP-0004).
        /// </para>
        /// <para>
        /// Silent when the anchor carries no net. An EditMode test binds a bare transform, and a
        /// captive whose shooter has since despawned has nobody left to tell.
        /// </para>
        /// </summary>
        private void ReportStruggle()
        {
            SnareCatch net = Net;
            GameObject shooter = net != null ? net.Shooter : null;
            if (shooter == null) return;

            NetMessaging.NetSendTo(shooter, NetMsg.SnareStruggled,
                                   new NetArg().With(gameObject), NetTo.Server);
        }

        /// <summary>
        /// The net that has hold of this body, resolved from its anchor rather than kept beside it.
        ///
        /// One field for one relationship: a second reference would be a second thing for
        /// <see cref="Release"/> to remember to clear, and this is read at most a couple of times a
        /// second, on the owner's machine, only while a net has hold.
        /// </summary>
        private SnareCatch Net => anchor != null ? anchor.GetComponent<SnareCatch>() : null;

        /// <summary>
        /// Read the captive's keys and hand them to <see cref="Step"/>.
        ///
        /// <para>
        /// The gate is <see cref="SnareStruggleReader.MayRead"/>, which is NOT the shared
        /// <c>AcceptsGameplayInput</c> hotkey gate — that one is false for every netted player by
        /// construction. See there for the full reason.
        /// </para>
        /// </summary>
        private void Update()
        {
            if (!bound) return;

            input.Poll(SnareStruggleReader.MayRead(Network.Owns(this)),
                       out bool jumpPressed, out Vector2 move);

            Step(Time.deltaTime, jumpPressed, move);
        }
    }
}
