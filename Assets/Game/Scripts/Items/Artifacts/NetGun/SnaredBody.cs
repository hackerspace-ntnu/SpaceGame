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
using SpaceGame.Gameplay.Ragdoll;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// Something other than a net that is keeping a body on the floor.
    ///
    /// <para>
    /// A net lets go when it rots. A tie does not, and the two can overlap: a captive hogtied under
    /// a net is still tied when the net gives out, and <c>PlayerRagdoll.ReleaseHold</c> is a single
    /// flag with no notion of how many claims are outstanding — so whoever releases first stands
    /// the body up out from under everyone else. Anything that holds a body down for longer than
    /// one net implements this, and the net asks before letting go.
    /// </para>
    /// <para>
    /// <b>Task 9's <c>Hogtie</c> is the first implementer and MUST implement it.</b> A tie that
    /// does not is not a compile error and not a warning — it is a hogtied captive who stands up
    /// the moment the net that caught them rots away.
    /// </para>
    /// <para>
    /// Declared in this file rather than its own because the net is the thing that has to ask, and
    /// <see cref="SnaredBody"/> and <see cref="SnareTether"/> are the two askers. It moves to its
    /// own file the moment a third kind of hold exists — Task 9's tie included, which implements
    /// this from wherever it lives.
    /// </para>
    /// </summary>
    public interface IHoldsBodyDown
    {
        /// <summary>Is this component keeping the body on the floor right now?</summary>
        bool IsHoldingBodyDown { get; }
    }

    /// <summary>The question a captor asks before it lets a body up. See <see cref="IHoldsBodyDown"/>.</summary>
    public static class BodyHold
    {
        /// <summary>
        /// Is anything OTHER than the caller still holding this body down?
        ///
        /// <para>
        /// Asked of the object the ragdoll adapter is on rather than of the captor's own, because
        /// that is the body being held and where a tie would be added. The captor asking is not
        /// itself an <see cref="IHoldsBodyDown"/> — a net is a hold with an end, and one that
        /// counted itself here could never let go.
        /// </para>
        /// </summary>
        public static bool HeldByAnythingElse(GameObject body)
        {
            if (body == null) return false;

            foreach (IHoldsBodyDown holder in body.GetComponents<IHoldsBodyDown>())
                if (holder != null && holder.IsHoldingBodyDown) return true;

            return false;
        }
    }

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
        /// <summary>
        /// How far the stick has to be pushed before it is a direction at all.
        ///
        /// A const rather than a serialized field, unlike everything in <see cref="SnareStruggle"/>:
        /// this component is added at runtime and can never be selected in the Inspector, which is
        /// the reason that class exists in the first place. It is not a design knob either — it
        /// separates a pushed stick from a resting one, and a resting stick that counted as a
        /// direction would have every frame of drift read as a reversal.
        /// </summary>
        private const float MoveDeadzone = 0.5f;

        /// <summary>
        /// How far round the new heading has to be to count as a reversal rather than a turn.
        ///
        /// -0.5 is 120 degrees. Mashing A against D is 180 and lands well past it; strafing round
        /// a corner is 90 and does not, which is the discrimination wanted — a captive who happens
        /// to be steering is not struggling.
        /// </summary>
        private const float ReversalDot = -0.5f;

        private PlayerRagdoll ragdoll;
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
        /// Take hold: put the player on the floor and start counting their struggle.
        /// </summary>
        /// <returns>
        /// False when the hold did not take, and the caller must then record no capture at all.
        /// Another net already has this player is one reason; the body refusing to go down is the
        /// other, and <c>PlayerRagdoll.HoldDown</c> lists what those are — a corpse, or a rig whose
        /// skeleton build kept no bones.
        ///
        /// <para>
        /// A capture recorded over a body that never went down is worse than no capture: the net
        /// spends its pool holding somebody who is walking around, and it is the shooter who is
        /// punished for it. There is no half measure available here the way there is for a creature
        /// — <see cref="SnareTether"/> can still hobble a NavMeshAgent it failed to fell, and a
        /// player has no such dial to turn that would not be taking control away by another name.
        /// </para>
        /// </returns>
        public bool Bind(Transform netAnchor, SnareStruggle struggleSettings)
        {
            if (bound && anchor != netAnchor) return false;

            // The same net binding again is not a re-catch. Answering true without rebuilding the
            // meter is what stops it wiping the struggle the captive has already banked.
            if (bound) return true;

            PlayerRagdoll body = Ragdoll;
            if (body == null || !body.HoldDown()) return false;

            SnareStruggle settings = struggleSettings ?? new SnareStruggle();

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
        /// The player only stands up if nothing else is still holding them down — see
        /// <see cref="BodyHold.HeldByAnythingElse"/>.
        /// </para>
        /// </summary>
        public void Release(Transform netAnchor)
        {
            if (!bound || (netAnchor != null && netAnchor != anchor)) return;

            bound = false;
            anchor = null;
            meter = null;
            heading = Vector2.zero;
            ReleaseStruggleInput();

            PlayerRagdoll body = Ragdoll;
            if (body != null && !BodyHold.HeldByAnythingElse(body.gameObject)) body.ReleaseHold();
        }

        /// <summary>
        /// Let go no matter which net asks. For teardown only — a chunk unloading under a net must
        /// not leave a player limp forever with nothing left alive to stand them up.
        ///
        /// <para>
        /// <see cref="SnareTether"/> repeats its restraint unconditionally underneath the same
        /// line, and this deliberately does not. It has nothing to repeat: that component can be
        /// holding a creature by a hobble its <c>bound</c> flag does not describe, whereas
        /// <see cref="Bind"/> here fails outright when the hold is refused — so a SnaredBody that
        /// is not bound is holding nothing. The input asset is the one thing that can outlive the
        /// binding, and it is disposed either way.
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
            if (!bound || meter == null) return;

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
            if (heading == Vector2.zero || move.sqrMagnitude < MoveDeadzone * MoveDeadzone)
                return false;

            return Vector2.Dot(move.normalized, heading) < ReversalDot;
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
            if (move.sqrMagnitude >= MoveDeadzone * MoveDeadzone) heading = move.normalized;
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
