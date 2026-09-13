using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The zoom-in: a camera flown from the player's eye to a seat in front of the glass, the
    /// cursor freed so the tabs can be clicked, the keys 1-3 flipping pages, and any of the usual
    /// ways out — Esc, right mouse again, reaching for WASD — handing everything back.
    ///
    /// <para>
    /// Local to the machine of the player who pressed. Nothing here is sent to anyone: the page
    /// requests go through <see cref="TerminalConsole"/>, which owns what is shared, and a peer
    /// sees the operator standing at the terminal exactly as they would without this. Like
    /// <c>PackFocusSession</c>: the clock is never stopped, every exit path comes through
    /// <see cref="Exit"/>, and the exits are read raw off the devices because entering
    /// <see cref="GameplayMenuScope"/> disables the player's input component.
    /// </para>
    /// <para>
    /// Lives on the terminal prefab rather than the player, because the shot is the terminal's:
    /// the glass anchor and its height are measured by the builder that made the prefab.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerminalFocusSession : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The world-space canvas on the glass. Its event camera is ours for the session so the tabs can be clicked.")]
        [SerializeField] private Canvas canvas;

        [Tooltip("Enabled only while a session is open — nothing should be clickable on a screen nobody is at.")]
        [SerializeField] private GraphicRaycaster raycaster;

        [Tooltip("At the glass's centre: forward is the outward normal, up is the screen's up. Placed by the builder off the plate's own mesh.")]
        [SerializeField] private Transform screenAnchor;

        [Tooltip("The glass's height along its up, metres — what the lens distance is fitted to.")]
        [SerializeField, Min(0.01f)] private float screenHeight = 0.45f;

        [Tooltip("The pages themselves. Asked to spend an Esc before this session does.")]
        [SerializeField] private TerminalScreen screen;

        [Header("The shot")]
        [SerializeField] private TerminalFocusCamera.Shot shot = TerminalFocusCamera.Shot.Default;

        [Tooltip("Seconds the camera takes to fly back to the eye when the session closes.")]
        [SerializeField, Min(0f)] private float flyOutSeconds = 0.25f;

        /// <summary>The session on screen, if any. At most one, on one machine.</summary>
        public static TerminalFocusSession Active { get; private set; }

        public bool IsOpen => Active == this;

        private TerminalConsole console;
        private PlayerController player;
        private Interactor interactor;
        private TerminalFocusCamera focusCamera;
        private int enteredFrame;

        /// <summary>Takes the screen. False when it cannot — another screen holds the controls, or this is not our player.</summary>
        public bool Enter(TerminalConsole owner, PlayerController presser, Interactor pressedWith)
        {
            if (IsOpen) return true;
            if (Active != null || owner == null || presser == null || screenAnchor == null) return false;

            // A replica of somebody else's body must never open a focus session on this machine.
            if (!Network.Owns(presser)) return false;

            // A pause menu, the chat box, the pack or the body screen already holds the controls.
            if (GameplayMenuScope.IsActive) return false;
            if (!GameplayMenuScope.Enter(this, freezeTime: false, hideHud: true)) return false;

            focusCamera = TerminalFocusCamera.Spawn(screenAnchor, screenHeight, shot, presser.PlayerCamera);
            if (focusCamera == null)
            {
                GameplayMenuScope.Exit(this);
                return false;
            }

            // Clicks on a world-space canvas are raycast through its event camera, which for the
            // length of the session is the lens the player is actually looking through.
            UIBuilder.EnsureEventSystem();
            if (canvas != null) canvas.worldCamera = focusCamera.Camera;
            if (raycaster != null) raycaster.enabled = true;

            console = owner;
            player = presser;
            interactor = pressedWith;
            enteredFrame = Time.frameCount;
            Active = this;
            return true;
        }

        /// <summary>Hands everything back. Safe to call when there is no session, and safe to call twice.</summary>
        public void Exit()
        {
            if (!IsOpen) return;

            Active = null;

            if (raycaster != null) raycaster.enabled = false;
            if (canvas != null) canvas.worldCamera = null;

            if (console != null) console.Release(interactor);

            if (focusCamera != null) focusCamera.FlyOut(flyOutSeconds);
            focusCamera = null;

            GameplayMenuScope.Exit(this);

            console = null;
            player = null;
            interactor = null;
        }

        private void OnDisable() => Exit();

        private void Update()
        {
            if (!IsOpen) return;

            // The press that opened this is still down on the frame it opened; without this the
            // right mouse button enters and leaves in one gesture.
            if (Time.frameCount == enteredFrame) return;

            // Death takes the screen: the death camera wants the player's own lens back.
            if (player == null || player.IsDead) { Exit(); return; }

            // Esc backs out of whatever the page has opened up before it leaves the terminal —
            // the layered escape a reader expects, so that pulling a motor up close is not a
            // one-way trip out of the console.
            if (EscapePressed())
            {
                if (screen != null && screen.TryStepBack()) return;
                Exit();
                return;
            }

            if (WantsOut()) { Exit(); return; }

            int key = PageKey();
            if (key >= 0 && console != null) console.RequestPage(key);
        }

        private static bool EscapePressed()
        {
            Keyboard keys = Keyboard.current;
            return keys != null && keys.escapeKey.wasPressedThisFrame;
        }

        /// <summary>Every way out but Esc, checked raw — see the class remarks.</summary>
        private static bool WantsOut()
        {
            Keyboard keys = Keyboard.current;
            if (keys != null)
            {
                // Reaching for the movement keys is deciding to be somewhere else.
                if (keys.wKey.isPressed || keys.aKey.isPressed ||
                    keys.sKey.isPressed || keys.dKey.isPressed) return true;
                if (keys.upArrowKey.isPressed || keys.downArrowKey.isPressed ||
                    keys.leftArrowKey.isPressed || keys.rightArrowKey.isPressed) return true;
                if (keys.spaceKey.wasPressedThisFrame) return true;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame) return true;

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.buttonEast.wasPressedThisFrame) return true;
                if (pad.leftStick.ReadValue().sqrMagnitude > 0.25f) return true;
            }

            return false;
        }

        /// <summary>1, 2, 3 on either row of the keyboard, as a page index; -1 for none.</summary>
        private static int PageKey()
        {
            Keyboard keys = Keyboard.current;
            if (keys == null) return -1;

            if (keys.digit1Key.wasPressedThisFrame || keys.numpad1Key.wasPressedThisFrame) return 0;
            if (keys.digit2Key.wasPressedThisFrame || keys.numpad2Key.wasPressedThisFrame) return 1;
            if (keys.digit3Key.wasPressedThisFrame || keys.numpad3Key.wasPressedThisFrame) return 2;
            return -1;
        }
    }
}
