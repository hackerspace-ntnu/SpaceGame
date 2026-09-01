using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Gameplay;
using SpaceGame.Presentation;

// UnityEngine.InputSystem has a PlayerInputManager of its own, for local multiplayer join
// handling. This project has never used it; the one meant here is always ours.
using PlayerInputManager = SpaceGame.Core.PlayerInputManager;

namespace SpaceGame.Items
{
    /// <summary>
    /// Focus mode: the state the player is in while rummaging in their own deployed pack.
    ///
    /// <para>
    /// <b>Nothing pauses.</b> <c>Time.timeScale</c> is never touched here. The scope is entered
    /// with <c>freezeTime: false</c> — the overload <c>ChatUI</c> already uses — so the cursor is
    /// free and the keyboard is ours while the world carries on around the player. That is a
    /// deliberate choice with a cost attached: the player is genuinely vulnerable while their head
    /// is in the pack, which is why every exit below is instant and why none of them ask for
    /// confirmation.
    /// </para>
    /// <para>
    /// It is also the only behaviour that could work in co-op. <c>GameplayMenuScope</c> refuses to
    /// freeze unless the session is solo, because the simulation is shared and authoritative on
    /// the host — so a pausing focus mode would have behaved differently in multiplayer, which is
    /// a class of bug this feature has no reason to invite.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PackFocusSession : MonoBehaviour
    {
        /// <summary>The session currently on screen, if any. At most one, on one machine.</summary>
        public static PackFocusSession Active { get; private set; }

        private BackpackController controller;
        private PlayerInputManager input;
        private PlayerController player;

        private PackFocusCamera focusCamera;
        private PackHandController hand;
        private GameObject hiddenCrosshair;

        /// <summary>
        /// Seconds a client waits for the server to say the pack is going down before giving up on
        /// the session. Long enough for a bad connection, short enough that a refused deploy does
        /// not leave the player staring at their own back.
        /// </summary>
        private const float DeployAnswerTimeout = 3f;

        private int enteredFrame = -1;
        private float enteredAt;
        private bool sawItOpen;
        private Coroutine pendingStow;

        public bool IsFocused => Active == this;

        /// <summary>The pack being rummaged in, or null when there is no session.</summary>
        public BackpackObject Pack => controller != null ? controller.Pack : null;

        private void Awake()
        {
            controller = GetComponent<BackpackController>();
            input = GetComponent<PlayerInputManager>();
            player = GetComponent<PlayerController>();
        }

        private void OnEnable()
        {
            if (input != null) input.OnBackpackPressed += OnBackpackKey;
        }

        private void OnDisable()
        {
            if (input != null) input.OnBackpackPressed -= OnBackpackKey;
            Exit();
        }

        // ── Entering ─────────────────────────────────────────────────────────

        /// <summary>
        /// One gesture, two things: B sets the pack down and drops the player into it.
        ///
        /// <para>
        /// The deploy itself is not requested here. <see cref="BackpackController.Toggle"/> is
        /// subscribed to the same event and does it, and asking twice would put a second state
        /// request on the wire for the server to refuse. Which of the two handlers runs first is
        /// not defined — they are components on one GameObject — so this reads the pack as either
        /// still shouldered or already on its way down and treats both as "the pack is opening".
        /// </para>
        /// <para>
        /// Not reachable while focus is up: entering disables the player's input component, which
        /// is what this event comes from. B while focused is read raw in <see cref="Update"/>.
        /// </para>
        /// </summary>
        private void OnBackpackKey()
        {
            if (controller == null || IsFocused) return;

            // A replica of somebody else's body must never open a focus session on this screen.
            // Their input component is disabled so this should be unreachable, and the cost of
            // being wrong is four players' packs filling one player's view.
            if (!Network.Owns(controller)) return;

            BackpackController.State state = controller.CurrentState;
            if (state != BackpackController.State.Shouldered &&
                state != BackpackController.State.Deploying) return;

            Enter();
        }

        /// <summary>Takes the screen. Refused when something else already has it.</summary>
        public void Enter()
        {
            if (IsFocused || Active != null) return;
            if (controller == null || controller.Pack == null) return;

            // A pause menu, the chat box or a cutscene already holds the controls. Stacking on top
            // would take a cursor that is already spoken for and hand it back at the wrong time.
            if (GameplayMenuScope.IsActive) return;

            // hideHud false, because the bar is half of the interaction: a click on a slot lifts
            // its item into the player's hand, and a click on a slot puts one back — neither of
            // which can happen to a bar that is not on screen. The crosshair is the part of the HUD
            // that has to go, and it goes below.
            if (!GameplayMenuScope.Enter(this, freezeTime: false, hideHud: false)) return;

            Active = this;
            enteredFrame = Time.frameCount;
            enteredAt = Time.unscaledTime;
            sawItOpen = false;

            // A stow queued by the last exit must not fire under this session: the player asked
            // for the pack back the moment they re-entered it.
            if (pendingStow != null) StopCoroutine(pendingStow);
            pendingStow = null;

            HideCrosshair();

            // After the scope, never before. Entering the scope disables the player's input
            // component, and that disable turns every action on it off — including these two.
            //
            // The stow keys are the reason the hotbar keys work at all in here. The Hotbar action
            // map went down with the component, so OnHotbarPressed does not fire while focused;
            // these are separate actions on the same physical keys, switched on for exactly as
            // long as the session owns the screen.
            if (input != null)
            {
                input.SetPackYawEnabled(true);
                input.SetPackStowEnabled(true);
                input.SetPackRackEnabled(true);

                // Subscribed alongside the enable rather than in OnEnable, because the action is
                // only live for the length of a session and a handler outliving it would answer a
                // key nothing is listening for.
                input.OnPackRackPressed -= OnRackKey;
                input.OnPackRackPressed += OnRackKey;
            }

            focusCamera = PackFocusCamera.Spawn(controller.Pack.transform, ViewDirection(), PlayerCamera());

            if (focusCamera != null)
                hand = PackHandController.Attach(focusCamera, controller, LocalInteractor(), input);
        }

        // ── Leaving ──────────────────────────────────────────────────────────

        /// <summary>
        /// Hands everything back. Safe to call when there is no session, and safe to call twice —
        /// <see cref="OnDisable"/> and an exit key can both reach it in one frame.
        /// </summary>
        public void Exit()
        {
            if (!IsFocused) return;

            Active = null;

            if (hand != null) hand.Cancel();
            hand = null;

            if (focusCamera != null) focusCamera.Dismiss();
            focusCamera = null;

            if (input != null)
            {
                input.SetPackYawEnabled(false);
                input.SetPackStowEnabled(false);
                input.SetPackRackEnabled(false);

                input.OnPackRackPressed -= OnRackKey;
            }

            RestoreCrosshair();

            // The pack itself is not touched here. This runs on teardown paths too — death, the
            // component being disabled, the pack already reshouldered by somebody else — where
            // moving the pack is either wrong or impossible. The exit GESTURE goes through
            // <see cref="ExitAndReshoulder"/>, which follows this with the stow.
            GameplayMenuScope.Exit(this);
        }

        /// <summary>
        /// The deliberate way out: hand the screen back AND throw the pack over the shoulder.
        ///
        /// <para>
        /// Closing the pack used to be a separate gesture — walk off, then B again — which in
        /// practice meant every session ended with the pack abandoned on the sand behind a player
        /// who thought they had put it away. Leaving focus IS being done with the pack, so the two
        /// are now one gesture. Only the deliberate exits stow; dying and being torn down leave
        /// the pack where it stands, which is what dropping your pack should look like.
        /// </para>
        /// </summary>
        private void ExitAndReshoulder()
        {
            Exit();

            if (controller == null) return;

            switch (controller.CurrentState)
            {
                case BackpackController.State.Open:
                    controller.Reshoulder();
                    break;

                // Exited while the pack was still flying down. Asking now would be refused — the
                // server only stows an OPEN pack — so the ask waits for the landing.
                case BackpackController.State.Deploying:
                    pendingStow = StartCoroutine(ReshoulderOnceLanded());
                    break;
            }
        }

        /// <summary>
        /// The queued half of <see cref="ExitAndReshoulder"/>. Cancelled by <see cref="Enter"/>,
        /// because a player who re-entered the pack mid-flight is no longer asking for it back.
        /// </summary>
        private IEnumerator ReshoulderOnceLanded()
        {
            while (controller != null &&
                   controller.CurrentState == BackpackController.State.Deploying)
                yield return null;

            if (controller != null && !IsFocused &&
                controller.CurrentState == BackpackController.State.Open)
                controller.Reshoulder();

            pendingStow = null;
        }

        /// <summary>
        /// Every way out, checked raw.
        ///
        /// <para>
        /// Raw, rather than through <see cref="PlayerInputManager"/>, because entering focus
        /// disabled that component: <c>MoveInput</c> is pinned at zero and no action on it fires.
        /// The exits are read straight off the devices for exactly as long as the session owns the
        /// screen, and only then.
        /// </para>
        /// </summary>
        private void Update()
        {
            if (!IsFocused) return;

            // The press that opened this is still down on the frame it opened. Without this, B
            // enters and leaves in one gesture and the pack deploys into an empty screen.
            if (Time.frameCount == enteredFrame) return;

            if (PackIsGone() || (player != null && player.IsDead)) { Exit(); return; }

            if (WantsOut()) ExitAndReshoulder();
        }

        /// <summary>
        /// Is there still a pack on the ground to be focused on?
        ///
        /// <para>
        /// The subtlety is the beginning, not the end. A CLIENT's deploy is a request: the pack
        /// stays <c>Shouldered</c> on their machine for a full round trip before the server's
        /// answer arrives. Treating "shouldered" as "gone" without waiting to see it open would
        /// close focus mode on the very next frame for everyone who is not the host — the same
        /// class of bug as answering your own question before the server has heard it.
        /// </para>
        /// <para>
        /// So the test is armed by having seen the pack move, and until then only a timeout ends
        /// the session. The timeout is what covers the deploy the server refused — no ground to
        /// set it down on — which announces nothing at all.
        /// </para>
        /// </summary>
        private bool PackIsGone()
        {
            if (controller == null || controller.Pack == null) return true;

            BackpackController.State state = controller.CurrentState;

            if (state == BackpackController.State.Deploying || state == BackpackController.State.Open)
            {
                sawItOpen = true;
                return false;
            }

            if (sawItOpen) return true;

            return Time.unscaledTime - enteredAt > DeployAnswerTimeout;
        }

        private static bool WantsOut()
        {
            Keyboard keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.escapeKey.wasPressedThisFrame) return true;
                if (keys.bKey.wasPressedThisFrame) return true;

                // Any movement input, not a movement key specifically: the player reaching for
                // WASD has decided to be somewhere else, and the pack should already be behind
                // them by the time they get there.
                if (keys.wKey.isPressed || keys.aKey.isPressed ||
                    keys.sKey.isPressed || keys.dKey.isPressed) return true;

                if (keys.upArrowKey.isPressed || keys.downArrowKey.isPressed ||
                    keys.leftArrowKey.isPressed || keys.rightArrowKey.isPressed) return true;

                if (keys.spaceKey.wasPressedThisFrame) return true;
            }

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame) return true;
                if (pad.leftStick.ReadValue().sqrMagnitude > 0.25f) return true;
            }

            return false;
        }

        // ── The rack ─────────────────────────────────────────────────────────

        /// <summary>
        /// R: flip the pack's front leaf up into a rack, or lay it back down.
        ///
        /// <para>
        /// The shortcut. The gesture is <see cref="PackLeafDrag"/> — grab the board's free edge and
        /// pull it through its arc — and the two are deliberately both here: the pull is what makes
        /// the rack feel like a thing you do to the pack, and the key is what makes it something you
        /// can do without aiming. The one worry a click on the leaf raised, that it could not be
        /// told apart from the click that PICKS UP whatever is lying on the leaf, is answered by
        /// resolving the item hit first and only reading a bare hem as a grab.
        /// </para>
        /// <para>
        /// It only asks. Which way a shared container's members are folded is state, so
        /// <see cref="BackpackObject.RequestRack"/> routes it through the wire when there is one.
        /// </para>
        /// </summary>
        private void OnRackKey()
        {
            if (!IsFocused) return;

            BackpackObject pack = Pack;
            if (pack == null || !pack.IsOpen) return;

            // A held item is being lined up against a surface that is about to swing through
            // ninety degrees, so the carried copy would be left tracking a face that has gone.
            // Putting it back where it was picked up costs nothing — the lift was never sent — and, unlike
            // Cancel, leaves the hand alive: Cancel is the session's exit and destroys it, so this
            // used to mean one press of R silently stopped the player picking anything else up for
            // the rest of the session.
            if (hand != null) hand.ReturnToOrigin();

            pack.RequestRack(!pack.IsRacked);
        }

        // ── The HUD wrinkle ──────────────────────────────────────────────────

        /// <summary>
        /// Takes the crosshair down and leaves the rest of the HUD alone.
        ///
        /// <para>
        /// Found on the HUD root by component rather than wired in the inspector, so no prefab
        /// needs rewiring for this feature — <c>playerHUD</c> is one instance holding the
        /// crosshair, the helmet, the health group, the death screen and the hotbar, and only one
        /// of those is wrong here. A crosshair drawn over a free cursor reads as two cursors, and
        /// there is nothing in focus mode to aim at.
        /// </para>
        /// </summary>
        private void HideCrosshair()
        {
            GameObject hud = player != null ? player.HudRoot : null;
            if (hud == null) return;

            var crosshair = hud.GetComponentInChildren<CrosshairUI>(includeInactive: true);
            if (crosshair == null || !crosshair.gameObject.activeSelf) return;

            hiddenCrosshair = crosshair.gameObject;
            hiddenCrosshair.SetActive(false);
        }

        private void RestoreCrosshair()
        {
            if (hiddenCrosshair != null) hiddenCrosshair.SetActive(true);
            hiddenCrosshair = null;
        }

        // ── Small resolutions ────────────────────────────────────────────────

        private Camera PlayerCamera() => player != null ? player.PlayerCamera : null;

        /// <summary>
        /// Which way the player faces the rig, flattened. The focus camera sits along it, on the
        /// far side of the pack looking back down it, so this is what decides which side of the
        /// pack the shot comes from.
        ///
        /// The actual player→pack line, frozen at this instant, not the body's facing: the
        /// player may have walked around the pack before opening it, and the camera has to land
        /// square to the mat wherever they stand. The body's facing survives only as the
        /// fallback for the degenerate case of standing exactly on top of the rig.
        /// </summary>
        private Vector3 ViewDirection()
        {
            Vector3 toPack = controller != null && controller.Pack != null
                ? controller.Pack.transform.position - transform.position
                : transform.forward;

            var flat = new Vector3(toPack.x, 0f, toPack.z);
            if (flat.sqrMagnitude > 1e-6f) return flat.normalized;

            flat = new Vector3(transform.forward.x, 0f, transform.forward.z);
            return flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
        }

        /// <summary>
        /// This player's <see cref="Interactor"/>, which is what a take is addressed from.
        ///
        /// It lives on the camera rig rather than on the body in this project, so the search is
        /// deliberately a children search from the body — the same lookup, and the same trap, that
        /// <see cref="BackpackController.RequestTake"/> documents on the receiving end.
        /// </summary>
        private Interactor LocalInteractor() => GetComponentInChildren<Interactor>(true);
    }
}
