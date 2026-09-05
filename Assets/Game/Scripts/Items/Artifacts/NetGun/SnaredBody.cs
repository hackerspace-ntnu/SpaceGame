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
// machine can know: the keys the captive is pressing. Task 7 turns an accepted press into a
// message; until then it stops at the meter.
using UnityEngine;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Presentation;

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
        private InputControls struggleInput;

        /// <summary>The last direction the captive actually pushed, to measure the next against.</summary>
        private Vector2 heading;

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
            heading = Vector2.zero;
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
            heading = Vector2.zero;
            ReleaseStruggleInput();

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
        /// and the struggle they cannot have stopped goes on being counted. Today the meter is
        /// read by nothing; the moment Task 8 bills it, that is a corpse eating the shooter's net,
        /// and it would read as a balance problem rather than a lifecycle one.
        /// </para>
        /// <para>
        /// The captive stays in <c>SnareCatch</c>'s own captive list until the net rots — this ends
        /// the struggle, not the base load of a body lying in the net.
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

            ReleaseStruggleInput();
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

            // Measured before the heading is updated, or every input compares against itself.
            bool struggled = jumpPressed || IsReversal(move);
            RememberHeading(move);

            if (!struggled) return;

            // Offered rather than added. Push answering false means this input landed inside the
            // cooldown and was discarded, and that is the throttle on the WIRE as much as on the
            // meter — so Task 7 sends its message only when this answers true, and from here.
            meter.Push();
        }

        /// <summary>Has the captive thrown themselves the other way since last time?</summary>
        private bool IsReversal(Vector2 move)
        {
            if (heading == Vector2.zero || !IsPushed(move)) return false;

            return Vector2.Dot(move.normalized, heading) < settings.StruggleReversalDot;
        }

        /// <summary>
        /// Remember which way they are pushing, ignoring a stick that is not being pushed at all.
        ///
        /// A released stick must not overwrite the heading, or letting go for one frame between two
        /// opposite presses hides the reversal — and a menu closing over the captive does exactly
        /// that, because <see cref="Update"/> passes a zero move while the gate is shut.
        /// </summary>
        private void RememberHeading(Vector2 move)
        {
            if (IsPushed(move)) heading = move.normalized;
        }

        /// <summary>Is this a direction the captive meant, or a stick at rest?</summary>
        private bool IsPushed(Vector2 move)
        {
            float deadzone = settings.StruggleMoveDeadzone;
            return move.sqrMagnitude >= deadzone * deadzone;
        }

        /// <summary>
        /// Read the captive's keys and hand them to <see cref="Step"/>.
        ///
        /// <para>
        /// <b>The menu check is <see cref="GameplayMenuScope.IsActive"/> and it CANNOT be
        /// <c>AcceptsGameplayInput</c>,</b> which is the shared gate every other gameplay hotkey in
        /// this project uses. That property asks whether the local player's own
        /// <c>PlayerController.Input</c> is enabled — and going limp is precisely what disables it
        /// (see <see cref="StruggleInput"/>). It is therefore false for every netted player, always,
        /// and gating on it would leave the struggle silently unreadable for the entire feature
        /// while looking like the careful thing to do.
        /// </para>
        /// <para>
        /// <c>IsActive</c> asks the question that is actually meant here — is a menu holding the
        /// controls — and stays answerable while the captive's own input is switched off. It is
        /// needed: this component reads its own copy of the input asset, which a chat box or a pause
        /// screen does not disable, so without it typing "s" into chat is a struggle.
        /// </para>
        /// </summary>
        private void Update()
        {
            if (!bound) return;

            bool jumpPressed = false;
            Vector2 move = Vector2.zero;

            if (Network.Owns(this) && !GameplayMenuScope.IsActive)
            {
                InputControls controls = StruggleInput;
                jumpPressed = controls.Player.Jump.WasPressedThisFrame();
                move = controls.Player.Move.ReadValue<Vector2>();
            }

            Step(Time.deltaTime, jumpPressed, move);
        }

        /// <summary>
        /// This component's OWN copy of the input asset, and it has to be its own.
        ///
        /// <para>
        /// The obvious source is <c>PlayerController.Input</c>, and it is switched off: going limp
        /// runs <c>PlayerRagdoll.Suspend</c>, which disables the <c>PlayerInputManager</c> outright
        /// — killed at the source, because jump and dash arrive as events a merely-disabled
        /// PlayerMovement is still subscribed to — and that component zeroes <c>MoveInput</c> on
        /// its way down. So the one thing that could report a struggle is disabled by the very act
        /// that makes struggling necessary, and it reports a resting stick while it is.
        /// </para>
        /// <para>
        /// Constructing one is the established pattern in this codebase rather than a special case
        /// invented here — <c>HotbarController</c>, <c>ChatUI</c>, <c>PauseMenuUI</c>,
        /// <c>BodyInventoryUI</c> and <c>DevInventoryUI</c> each build their own. Only the Player
        /// map is enabled, only on the machine that owns this body, and only from the first frame a
        /// net has hold of it.
        /// </para>
        /// </summary>
        private InputControls StruggleInput
        {
            get
            {
                if (struggleInput != null) return struggleInput;

                struggleInput = new InputControls();
                struggleInput.Player.Enable();
                return struggleInput;
            }
        }

        private void ReleaseStruggleInput()
        {
            if (struggleInput == null) return;

            struggleInput.Player.Disable();
            struggleInput.Dispose();
            struggleInput = null;
        }
    }
}
