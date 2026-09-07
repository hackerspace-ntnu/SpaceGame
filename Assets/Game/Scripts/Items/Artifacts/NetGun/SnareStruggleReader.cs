// One restrained body's struggle keys, and the one question worth asking of them.
//
// Extracted from SnaredBody when the hogtie arrived, because the two hold the same body for
// different reasons and would otherwise have read the same keys through two copies of the same
// forty lines — the input asset's lifecycle, the menu gate, the deadzone and the reversal memory.
// A fix to any of those has to be a fix to both, and the one that is easiest to get wrong is the
// menu gate: see MayRead below for why it cannot be the shared hotkey gate every other control in
// this project uses.
using UnityEngine;
using SpaceGame.Presentation;

namespace SpaceGame.Items
{
    /// <summary>
    /// Reads a restrained player's own keys and decides whether what they pressed was a struggle.
    ///
    /// <para>
    /// Not a MonoBehaviour and it holds no body: the callers are components added at runtime, which
    /// never get an Awake outside play mode, so anything cached in one would be null in an EditMode
    /// test and the restraint would silently hold nothing.
    /// </para>
    /// <para>
    /// <b>Two halves, deliberately separable.</b> <see cref="Poll"/> is the part a test cannot
    /// drive — an <c>InputControls</c> takes no synthetic presses — and <see cref="Counts"/> is the
    /// part worth testing, so it takes the input as arguments instead of reading it. Every caller
    /// polls in Update and counts in its own step method, and every test calls only the second.
    /// </para>
    /// </summary>
    public sealed class SnareStruggleReader
    {
        /// <summary>The last direction the captive actually pushed, to measure the next against.</summary>
        private Vector2 heading;

        private InputControls controls;

        /// <summary>
        /// This machine's keys this frame, or nothing at all when they are not ours to read.
        ///
        /// <paramref name="readable"/> is the caller's answer to "is this the captive's own
        /// machine, and is the game listening" — see <see cref="MayRead"/>, which is what a caller
        /// should pass unless it has a reason not to.
        /// </summary>
        public void Poll(bool readable, out bool jumpPressed, out Vector2 move)
        {
            jumpPressed = false;
            move = Vector2.zero;

            if (!readable) return;

            InputControls input = Controls;
            jumpPressed = input.Player.Jump.WasPressedThisFrame();
            move = input.Player.Move.ReadValue<Vector2>();
        }

        /// <summary>
        /// Is the game listening to this machine's player at all?
        ///
        /// <para>
        /// <b>The menu check is <see cref="GameplayMenuScope.IsActive"/> and it CANNOT be
        /// <c>AcceptsGameplayInput</c>,</b> which is the shared gate every other gameplay hotkey in
        /// this project uses. That property asks whether the local player's own
        /// <c>PlayerController.Input</c> is enabled — and going limp is precisely what disables it
        /// (see <see cref="Controls"/>). It is therefore false for every restrained player, always,
        /// and gating on it would leave the struggle silently unreadable for the whole feature
        /// while looking like the careful thing to do.
        /// </para>
        /// <para>
        /// <c>IsActive</c> asks the question that is actually meant — is a menu holding the
        /// controls — and stays answerable while the captive's own input is switched off. It is
        /// needed: this reads its own copy of the input asset, which a chat box or a pause screen
        /// does not disable, so without it typing "s" into chat is a struggle.
        /// </para>
        /// </summary>
        public static bool MayRead(bool ownsBody) => ownsBody && !GameplayMenuScope.IsActive;

        /// <summary>
        /// Did that input count as fighting the restraint?
        ///
        /// <para>
        /// The heading is updated whatever the answer, and AFTER the reversal is measured — the
        /// other order compares every input against itself and nothing is ever a reversal.
        /// </para>
        /// </summary>
        public bool Counts(bool jumpPressed, Vector2 move, float moveDeadzone, float reversalDot)
        {
            bool struggled = jumpPressed || IsReversal(move, moveDeadzone, reversalDot);
            RememberHeading(move, moveDeadzone);
            return struggled;
        }

        /// <summary>Forget which way they were pushing. For a restraint that has just taken hold.</summary>
        public void ForgetHeading() => heading = Vector2.zero;

        /// <summary>
        /// Give the input asset back.
        ///
        /// Safe to call repeatedly and safe to call having never polled — a caller's teardown path
        /// runs whether or not the restraint ever read a key.
        /// </summary>
        public void Release()
        {
            if (controls == null) return;

            controls.Player.Disable();
            controls.Dispose();
            controls = null;
        }

        /// <summary>Has the captive thrown themselves the other way since last time?</summary>
        private bool IsReversal(Vector2 move, float moveDeadzone, float reversalDot)
        {
            if (heading == Vector2.zero || !IsPushed(move, moveDeadzone)) return false;

            return Vector2.Dot(move.normalized, heading) < reversalDot;
        }

        /// <summary>
        /// Remember which way they are pushing, ignoring a stick that is not being pushed at all.
        ///
        /// A released stick must not overwrite the heading, or letting go for one frame between two
        /// opposite presses hides the reversal — and a menu closing over the captive does exactly
        /// that, because <see cref="Poll"/> hands back a zero move while the gate is shut.
        /// </summary>
        private void RememberHeading(Vector2 move, float moveDeadzone)
        {
            if (IsPushed(move, moveDeadzone)) heading = move.normalized;
        }

        /// <summary>Is this a direction the captive meant, or a stick at rest?</summary>
        private static bool IsPushed(Vector2 move, float moveDeadzone) =>
            move.sqrMagnitude >= moveDeadzone * moveDeadzone;

        /// <summary>
        /// This reader's OWN copy of the input asset, and it has to be its own.
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
        /// map is enabled, only on the machine that owns the body, and only from the first frame
        /// something has hold of it.
        /// </para>
        /// </summary>
        private InputControls Controls
        {
            get
            {
                if (controls != null) return controls;

                controls = new InputControls();
                controls.Player.Enable();
                return controls;
            }
        }
    }
}
