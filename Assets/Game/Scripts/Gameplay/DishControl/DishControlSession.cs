using UnityEngine;
using UnityEngine.InputSystem;
using SpaceGame.Characters;
using SpaceGame.Core;
using SpaceGame.Presentation;

namespace SpaceGame.Gameplay
{
    /// <summary>
    /// The take-over: the camera cuts to a feed framing the dish, the readout comes up, and the move
    /// input slews the dish until the operator leaves.
    ///
    /// <para>
    /// Local to the machine of the player who pressed, like <see cref="TerminalFocusSession"/>: the
    /// claim and the motor are <see cref="DishConsole"/>'s and <see cref="DishRig"/>'s, and only the
    /// command crosses the wire. Input is read raw off the devices because entering
    /// <see cref="GameplayMenuScope"/> disables the player's input component; the clock keeps running.
    /// Every exit path comes through <see cref="Exit"/>.
    /// </para>
    /// <para>
    /// The mapping is the move stick's: left/right turn the azimuth, up/down tilt the elevation — the
    /// two-axis control a player's hand is already on (<c>GDC-L1-UX-0005</c>). Leaving is the same right
    /// mouse that took the controls, or Esc, or the pad's B.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DishControlSession : MonoBehaviour
    {
        [Header("The feed")]
        [Tooltip("Where the feed's lens sits on the tower prefab; its forward is where it looks.")]
        [SerializeField] private Transform vantage;

        [Tooltip("What the feed focuses on: the dish.")]
        [SerializeField] private Transform focusTarget;

        [SerializeField, Range(10f, 90f)] private float fieldOfView = 50f;

        [Header("Input")]
        [Tooltip("Stick deflection below which an axis reads as released.")]
        [SerializeField, Range(0f, 0.9f)] private float stickDeadZone = 0.25f;

        [Tooltip("Steps the analogue command is rounded to before it is sent, so a resting thumb does not stream messages.")]
        [SerializeField, Min(1)] private int commandSteps = 4;

        [Header("Readout")]
        [SerializeField] private DishReadout.Style readoutStyle = DishReadout.Style.Default;

        /// <summary>The session on screen, if any. At most one, on one machine.</summary>
        public static DishControlSession Active { get; private set; }

        public bool IsOpen => Active == this;

        private DishConsole console;
        private PlayerController player;
        private Interactor interactor;
        private DishFocusCamera feed;
        private DishReadout readout;
        private Vector2 sentCommand;
        private int enteredFrame;

        /// <summary>Takes the controls. False when it cannot — another screen holds them, or this is not our player.</summary>
        public bool Enter(DishConsole owner, PlayerController presser, Interactor pressedWith)
        {
            if (IsOpen) return true;
            if (Active != null || owner == null || owner.Rig == null || presser == null || vantage == null) return false;

            // A replica of somebody else's body must never open a session on this machine.
            if (!Network.Owns(presser)) return false;

            if (GameplayMenuScope.IsActive) return false;
            if (!GameplayMenuScope.Enter(this, freezeTime: false, hideHud: true)) return false;

            feed = DishFocusCamera.Spawn(vantage, focusTarget, fieldOfView, presser.PlayerCamera);
            if (feed == null)
            {
                GameplayMenuScope.Exit(this);
                return false;
            }

            readout = DishReadout.Show(owner.Rig, readoutStyle);

            console = owner;
            player = presser;
            interactor = pressedWith;
            sentCommand = Vector2.zero;
            enteredFrame = Time.frameCount;
            Active = this;
            return true;
        }

        /// <summary>Hands everything back. Safe to call when there is no session, and safe to call twice.</summary>
        public void Exit()
        {
            if (!IsOpen) return;

            Active = null;

            if (console != null)
            {
                if (sentCommand != Vector2.zero) console.Steer(Vector2.zero);
                console.Release(interactor);
            }

            if (feed != null) feed.FlyOut(0f);
            feed = null;

            if (readout != null) Destroy(readout.gameObject);
            readout = null;

            GameplayMenuScope.Exit(this);

            console = null;
            player = null;
            interactor = null;
        }

        private void OnDisable() => Exit();

        private void Update()
        {
            if (!IsOpen) return;

            // The press that opened this is still down on the frame it opened; without this the right
            // mouse button enters and leaves in one gesture.
            if (Time.frameCount == enteredFrame) return;

            // Death takes the screen: the death camera wants the player's own lens back.
            if (player == null || player.IsDead || console == null) { Exit(); return; }

            if (WantsOut()) { Exit(); return; }

            Vector2 command = ReadCommand();
            if (readout != null) readout.Present(command);

            if (command == sentCommand) return;
            sentCommand = command;
            console.Steer(command);
        }

        private Vector2 ReadCommand()
        {
            Vector2 raw = Vector2.zero;

            Keyboard keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.aKey.isPressed || keys.leftArrowKey.isPressed) raw.x -= 1f;
                if (keys.dKey.isPressed || keys.rightArrowKey.isPressed) raw.x += 1f;
                if (keys.wKey.isPressed || keys.upArrowKey.isPressed) raw.y += 1f;
                if (keys.sKey.isPressed || keys.downArrowKey.isPressed) raw.y -= 1f;
            }

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (Mathf.Abs(stick.x) > stickDeadZone) raw.x += stick.x;
                if (Mathf.Abs(stick.y) > stickDeadZone) raw.y += stick.y;
            }

            return new Vector2(Quantise(raw.x), Quantise(raw.y));
        }

        private float Quantise(float value) =>
            Mathf.Round(Mathf.Clamp(value, -1f, 1f) * commandSteps) / commandSteps;

        private static bool WantsOut()
        {
            Keyboard keys = Keyboard.current;
            if (keys != null && keys.escapeKey.wasPressedThisFrame) return true;

            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame) return true;

            Gamepad pad = Gamepad.current;
            return pad != null && pad.buttonEast.wasPressedThisFrame;
        }
    }
}
