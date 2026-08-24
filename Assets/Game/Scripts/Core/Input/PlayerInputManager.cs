using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceGame.Core
{
    /// <summary>
    /// Single source of truth for all player input.
    /// Owns the InputControls instance, fires events and exposes read values.
    /// All other scripts get input exclusively from here.
    /// </summary>
    public class PlayerInputManager : MonoBehaviour
    {
        /// <summary>Wheel deltas below this are noise, not a scroll.</summary>
        private const float ScrollDeadzone = 0.01f;

        /// <summary>How fast the measured notch forgets an outlier. A wheel re-asserts its own
        /// notch on every click, so only a one-off spike is worn away.</summary>
        private const float ScrollUnitDecay = 0.95f;

        [Header("Hotbar scrolling")]
        [Tooltip("Off, scrolling up selects the previous slot. On, it selects the next one.")]
        [SerializeField] private bool invertHotbarScroll;

        [Tooltip("Wheel units that make up one slot step, or 0 to measure it from the device.")]
        [SerializeField] private float scrollUnitsPerSlot;

        private InputControls inputs;
        public Vector2 LookInput  { get; private set; }
        public Vector2 MoveInput  { get; private set; }

        // Scroll left over from the last step, and the largest delta this device has produced.
        private float scrollAccumulator;
        private float measuredScrollUnit;

        public event Action<int> OnHotbarPressed;

        /// <summary>Fires once per slot the wheel travels: -1 for the previous slot, +1 for the next.</summary>
        public event Action<int> OnHotbarScrolled;

        public event Action OnDropPressed;
    
        public event Action OnInteractPressed;
    
        public event Action OnUsePressed;

        /// <summary>
        /// The use button let go again, for items that do something for as long as they are held —
        /// the laser staff's beam is the first.
        ///
        /// Bound to the same action as <see cref="OnUsePressed"/> rather than to a new one, so a
        /// press and its release can never disagree about which button they belong to. The Input
        /// System also cancels an in-progress action when the action map is disabled, which means
        /// this fires on death and on opening a menu without anyone having to remember to — a held
        /// beam that kept burning through a respawn would be the obvious failure otherwise.
        /// </summary>
        public event Action OnUseReleased;

        /// <summary>
        /// Aim: right mouse, left gamepad trigger. Held rather than toggled.
        ///
        /// Built in code rather than added to InputSystem_Actions, because
        /// InputControls.cs embeds its own copy of that asset's JSON and is what
        /// actually binds at runtime: editing the .inputactions does nothing
        /// until the generated file is regenerated, and hand-patching 3000 lines
        /// of generated code to add one button is a much larger risk to every
        /// other control in the game than owning this one action here.
        ///
        /// It still lives on this component so it obeys the same enable and
        /// disable as everything else — an aim that survived death and menus
        /// would leave the weapon up with nothing left to lower it.
        ///
        /// This was called AltUse and had no consumers anywhere in the project.
        /// It is the same button on the same two devices; only the name changed,
        /// so nothing was rebound and nothing broke.
        /// </summary>
        public event Action OnAimPressed;

        /// <summary>Aim let go of. Also raised by the Input System when the action is disabled,
        /// which is what lowers the weapon on death and on opening a menu.</summary>
        public event Action OnAimReleased;

        /// <summary>
        /// Is aim down right now? Latched from the same two callbacks, so it can never disagree
        /// with them, and cleared in <see cref="OnDisable"/> for the same reason
        /// <see cref="CrouchHeld"/> is.
        /// </summary>
        public bool AimHeld { get; private set; }

        private InputAction aim;

        /// <summary>
        /// Crouch, held rather than toggled: pressed and released are both published, and
        /// <see cref="CrouchHeld"/> is the same answer for anything that would rather ask than
        /// listen.
        ///
        /// The action was already declared in InputSystem_Actions and generated into
        /// InputControls — it simply had no consumer — so this is a subscription, not a new
        /// binding, and it costs none of the risk described on <see cref="OnAimPressed"/>.
        /// </summary>
        public event Action OnCrouchPressed;

        /// <summary>Crouch let go of. Also raised by the Input System when the map is disabled,
        /// which is what stands a player up on death and on opening a menu.</summary>
        public event Action OnCrouchReleased;

        /// <summary>
        /// Is crouch down right now? Latched from the same two callbacks, so it can never
        /// disagree with them, and cleared in <see cref="OnDisable"/> for the same reason
        /// <see cref="MoveInput"/> is: a held key that outlives the map is a stance nothing
        /// will ever come back to cancel.
        /// </summary>
        public bool CrouchHeld { get; private set; }

        public event Action OnJumpPressed;
    
        public event Action OnDashPressed;

        public event Action OnBackpackPressed;

        /// <summary>
        /// Wheel notches while an item is being dragged around a deployed pack: +1 per notch up,
        /// -1 per notch down. The consumer turns them into yaw.
        ///
        /// <para>
        /// A second action on the same physical wheel rather than a second subscriber to
        /// <see cref="OnHotbarScrolled"/>, because that one is already bound to changing hotbar
        /// slots — rotating a staff on the mat would also swap what the player is holding.
        /// </para>
        /// <para>
        /// Built in code for the reason written out on <see cref="OnAimPressed"/>:
        /// <c>InputControls.cs</c> embeds its own copy of the .inputactions JSON and is what binds
        /// at runtime, so adding a binding to the asset does nothing until the generated file is
        /// regenerated.
        /// </para>
        /// </summary>
        public event Action<int> OnPackYawScrolled;

        private InputAction packYaw;
        private float packYawAccumulator;

        /// <summary>
        /// A hotbar key pressed while a pack is open in front of the player: that slot's item goes
        /// onto the pack, at the cursor if the cursor is over a free spot. The 0-based slot index.
        ///
        /// <para>
        /// The SHORTCUT way in from the hotbar. Dragging a pouch onto the pack is the other, and
        /// the one most players will find — <c>InventorySlotUI</c> implements the EventSystem drag
        /// handlers and hands the gesture to <c>PackDragController</c>. This stays because a key
        /// the player already associates with that slot costs nothing, needs no aim, and reads as
        /// "put this one away".
        /// </para>
        /// <para>
        /// Separate actions on the same physical keys rather than a second subscriber to
        /// <see cref="OnHotbarPressed"/>, for the reason spelled out on
        /// <see cref="OnPackYawScrolled"/> and one more besides. The first: those actions live on
        /// the <c>Hotbar</c> map, and focus mode runs with this whole component — and therefore
        /// that map — disabled, so they do not fire at all while a pack is open. The second: they
        /// are already bound to CHANGING the selected slot, so waking them here would select a slot
        /// in the same keystroke that empties it.
        /// </para>
        /// <para>
        /// Four, not ten: the hotbar is four slots wide (<c>PlayerInventoryNetwork.inventorySize</c>)
        /// and keys 5-10 select nothing to stow.
        /// </para>
        /// </summary>
        public event Action<int> OnPackStowPressed;

        /// <summary>How many hotbar keys the stow gesture covers. See <see cref="OnPackStowPressed"/>.</summary>
        private const int PackStowKeys = 4;

        private InputAction[] packStow;

        /// <summary>
        /// R while a pack is open in front of the player: flip its front leaf up into a rack, or
        /// back down flat.
        ///
        /// <para>
        /// A new action rather than a reuse, and the reason is the same one that forced the two
        /// above into existence: focus mode runs with this whole component — and therefore every
        /// map on it — disabled, so nothing already bound is even firing. What IS alive in a focus
        /// session is the wheel and keys 1-4, and both are already spoken for; there is nothing
        /// left to compose with.
        /// </para>
        /// <para>
        /// R is free of conflict by construction, not by luck: the action is off unless a session
        /// switched it on, so it does nothing in the world, nothing in a menu, and nothing on a
        /// body that is not the local player's.
        /// </para>
        /// </summary>
        public event Action OnPackRackPressed;

        private InputAction packRack;

        /// <summary>
        /// Switches the pack-yaw wheel on for the length of a focus session.
        ///
        /// <para>
        /// Off by default and NOT enabled by <see cref="OnEnable"/>, unlike every other action
        /// here, because focus mode runs with this whole component disabled — taking control goes
        /// through <c>PlayerController.EnterCutsceneMode</c>, which is exactly what stops the
        /// player walking about while they rummage. So the one input that must survive that has to
        /// be turned on afterwards, by the thing that took control.
        /// </para>
        /// <para>
        /// <see cref="OnDisable"/> still turns it off. A focus session enters the menu scope first
        /// and enables this second, so the ordering works out; and a player who dies mid-drag has
        /// their input disabled again, which takes the wheel with it.
        /// </para>
        /// </summary>
        public void SetPackYawEnabled(bool on)
        {
            EnsureInputs();

            if (on) packYaw.Enable();
            else
            {
                packYaw.Disable();
                packYawAccumulator = 0f;
            }
        }

        /// <summary>
        /// Switches the hotbar-to-pack keys on for the length of a focus session.
        ///
        /// Off by default and turned on by the thing that took control, for exactly the reasons
        /// written out on <see cref="SetPackYawEnabled"/> — which this is a second copy of because
        /// the two are enabled and disabled by the same two lines and must not drift apart.
        /// </summary>
        public void SetPackStowEnabled(bool on)
        {
            EnsureInputs();

            for (int i = 0; i < packStow.Length; i++)
            {
                if (on) packStow[i].Enable();
                else packStow[i].Disable();
            }
        }

        /// <summary>
        /// Switches the rack key on for the length of a focus session. Third copy of the two
        /// above, and off by default for the same reason: the component this lives on is disabled
        /// while a session owns the screen, so anything that must survive that has to be switched
        /// on afterwards by whatever took control.
        /// </summary>
        public void SetPackRackEnabled(bool on)
        {
            EnsureInputs();

            if (on) packRack.Enable();
            else packRack.Disable();
        }

        private void Awake()
        {
            EnsureInputs();

            // The inspector value is the authored default; once the player has touched the
            // settings menu's own switch, that is what the wheel obeys.
            GameSettings.SeedInvertHotbarScroll(invertHotbarScroll);
        }

        /// <summary>
        /// Creates the action asset if it does not exist yet.
        ///
        /// OnEnable cannot assume Awake ran first: PlayerController.Awake toggles this component's
        /// enabled flag, and Unity runs that on whichever component it reached first, so an enable
        /// can arrive before this object's own Awake. Every entry point that touches `inputs` calls
        /// this, which is cheaper than the NullReferenceException it replaces.
        /// </summary>
        private void EnsureInputs()
        {
            if (inputs != null) return;

            inputs = new InputControls();

            aim = new InputAction("Aim", InputActionType.Button);
            aim.AddBinding("<Mouse>/rightButton").WithGroup("Keyboard&Mouse");
            aim.AddBinding("<Gamepad>/leftTrigger").WithGroup("Gamepad");

            // Value, not Button: a wheel reports a delta and this needs every one of them, the
            // same way Hotbar.HotbarScroll does.
            packYaw = new InputAction("PackYaw", InputActionType.Value);
            packYaw.AddBinding("<Mouse>/scroll/y").WithGroup("Keyboard&Mouse");

            // One action per key rather than one action with four bindings, so which key was
            // pressed is the action's identity instead of a string read back off the control that
            // triggered it. The paths match the Hotbar map's own — see the note on
            // OnPackStowPressed for why this cannot simply reuse those actions.
            packStow = new InputAction[PackStowKeys];

            for (int i = 0; i < PackStowKeys; i++)
            {
                packStow[i] = new InputAction("PackStow" + (i + 1), InputActionType.Button);
                packStow[i].AddBinding("<Keyboard>/" + (i + 1)).WithGroup("Keyboard&Mouse");
            }

            packRack = new InputAction("PackRack", InputActionType.Button);
            packRack.AddBinding("<Keyboard>/r").WithGroup("Keyboard&Mouse");
            packRack.AddBinding("<Gamepad>/buttonNorth").WithGroup("Gamepad");

            BindActions();
        }

        /// <summary>
        /// Binds the action callbacks exactly once, for the lifetime of the asset.
        ///
        /// These used to be bound in OnEnable, which is wrong for a component that gets toggled:
        /// the callbacks are lambdas, so nothing could ever unsubscribe them, and every
        /// disable/enable round trip — now one per death and respawn — left another copy attached.
        /// Two copies means one jump keypress fires OnJumpPressed twice.
        /// </summary>
        private void BindActions()
        {
            // Hotbar
            inputs.Hotbar.Hotbar1.performed  += _ => OnHotbarPressed?.Invoke(0);
            inputs.Hotbar.Hotbar2.performed  += _ => OnHotbarPressed?.Invoke(1);
            inputs.Hotbar.Hotbar3.performed  += _ => OnHotbarPressed?.Invoke(2);
            inputs.Hotbar.Hotbar4.performed  += _ => OnHotbarPressed?.Invoke(3);
            inputs.Hotbar.Hotbar5.performed  += _ => OnHotbarPressed?.Invoke(4);
            inputs.Hotbar.Hotbar6.performed  += _ => OnHotbarPressed?.Invoke(5);
            inputs.Hotbar.Hotbar7.performed  += _ => OnHotbarPressed?.Invoke(6);
            inputs.Hotbar.Hotbar8.performed  += _ => OnHotbarPressed?.Invoke(7);
            inputs.Hotbar.Hotbar9.performed  += _ => OnHotbarPressed?.Invoke(8);
            inputs.Hotbar.Hotbar10.performed += _ => OnHotbarPressed?.Invoke(9);
            inputs.Hotbar.Drop.performed     += _ => OnDropPressed?.Invoke();
            inputs.Hotbar.HotbarScroll.performed += HandleHotbarScroll;

            // World interaction
            inputs.Player.Interact.performed += _ => OnInteractPressed?.Invoke();
            inputs.Player.Jump.performed     += _ => OnJumpPressed?.Invoke();
            inputs.Player.Dash.performed   += _ => OnDashPressed?.Invoke();
            inputs.Player.Use.performed   += _ => OnUsePressed?.Invoke();
            inputs.Player.Use.canceled    += _ => OnUseReleased?.Invoke();
            inputs.Player.Backpack.performed += _ => OnBackpackPressed?.Invoke();
            inputs.Player.Crouch.performed += _ => { CrouchHeld = true;  OnCrouchPressed?.Invoke(); };
            inputs.Player.Crouch.canceled  += _ => { CrouchHeld = false; OnCrouchReleased?.Invoke(); };

            // Bound here with the rest, and exactly once, for the same reason
            // they are: the callback is a lambda that nothing can unsubscribe,
            // so binding it in OnEnable would leave another copy attached after
            // every death and respawn.
            aim.performed += _ => { AimHeld = true;  OnAimPressed?.Invoke(); };
            aim.canceled  += _ => { AimHeld = false; OnAimReleased?.Invoke(); };

            packYaw.performed += HandlePackYawScroll;

            // The index is captured into a local, not read off the loop variable. A lambda closing
            // over `i` would report 4 for every key.
            for (int i = 0; i < packStow.Length; i++)
            {
                int slot = i;
                packStow[i].performed += _ => OnPackStowPressed?.Invoke(slot);
            }

            packRack.performed += _ => OnPackRackPressed?.Invoke();
        }

        private void OnEnable()
        {
            EnsureInputs();
            inputs.Enable();
            aim?.Enable();
        }

        private void OnDisable()
        {
            // Not EnsureInputs: if the asset was never created there is nothing enabled to stop,
            // and building one here just to disable it would leak it past OnDestroy.
            inputs?.Disable();
            aim?.Disable();
            packYaw?.Disable();
            packYawAccumulator = 0f;

            if (packStow != null)
                foreach (InputAction action in packStow) action?.Disable();

            packRack?.Disable();

            // Stale axes outlive the disable otherwise — MoveInput and LookInput are only written
            // by Update, so whatever the stick last read stays latched. On death that is a live
            // movement vector waiting for the next system that reads it.
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
            CrouchHeld = false;
            AimHeld = false;
        }

        private void OnDestroy()
        {
            inputs?.Dispose();
            inputs = null;

            aim?.Dispose();
            aim = null;

            packYaw?.Dispose();
            packYaw = null;

            if (packStow != null)
                foreach (InputAction action in packStow) action?.Dispose();

            packStow = null;

            packRack?.Dispose();
            packRack = null;
        }

        /// <summary>
        /// One slot along the bar per notch. See <see cref="NotchesFrom"/> for what a notch is.
        /// </summary>
        private void HandleHotbarScroll(InputAction.CallbackContext context)
        {
            int notches = NotchesFrom(context.ReadValue<float>(), ref scrollAccumulator);

            for (int i = 0; i < Mathf.Abs(notches); i++)
            {
                int direction = notches > 0 ? -1 : 1;   // Scrolling up moves back along the bar.
                OnHotbarScrolled?.Invoke(GameSettings.InvertHotbarScroll ? -direction : direction);
            }
        }

        /// <summary>
        /// The same wheel, counted into its own accumulator, for rotating a dragged pack item.
        ///
        /// A separate accumulator so a flick that half-fills one is not carried into the other:
        /// the two are never live at the same time, but leftovers from before a focus session
        /// would otherwise make its first notch arrive early.
        /// </summary>
        private void HandlePackYawScroll(InputAction.CallbackContext context)
        {
            int notches = NotchesFrom(context.ReadValue<float>(), ref packYawAccumulator);
            if (notches != 0) OnPackYawScrolled?.Invoke(notches);
        }

        /// <summary>
        /// Turns a raw wheel delta into whole, signed notches. Positive is a scroll up.
        ///
        /// <para>
        /// The delta is not comparable across platforms — Windows reports 120 per notch where
        /// other backends report single digits, and a trackpad streams fractions of a notch every
        /// frame. A notch is the largest delta a device produces, so measuring against the biggest
        /// value seen makes one notch mean one step everywhere, while a trackpad's smaller deltas
        /// pile up until they add up to a notch instead of racing through.
        /// </para>
        /// <para>
        /// <see cref="measuredScrollUnit"/> is deliberately shared by both consumers: it is a fact
        /// about the DEVICE, not about what the wheel is currently driving, and measuring it twice
        /// would mean the first notch of a drag arrives on a scale nothing has calibrated yet.
        /// </para>
        /// </summary>
        private int NotchesFrom(float delta, ref float accumulator)
        {
            if (Mathf.Abs(delta) < ScrollDeadzone) return 0;

            measuredScrollUnit = Mathf.Max(Mathf.Abs(delta), measuredScrollUnit * ScrollUnitDecay);
            float unitsPerNotch = scrollUnitsPerSlot > 0f ? scrollUnitsPerSlot : measuredScrollUnit;
            if (unitsPerNotch <= 0f) return 0;

            // Reversing direction starts the count again, so a flick back the other way is not
            // held up by leftovers pointing the wrong way.
            if (accumulator != 0f && Mathf.Sign(delta) != Mathf.Sign(accumulator))
                accumulator = 0f;

            accumulator += delta;

            int notches = 0;
            while (Mathf.Abs(accumulator) >= unitsPerNotch)
            {
                float sign = Mathf.Sign(accumulator);
                accumulator -= sign * unitsPerNotch;
                notches += sign > 0f ? 1 : -1;
            }

            return notches;
        }

        private void Update()
        {
            LookInput = inputs.Player.Look.ReadValue<Vector2>();
            MoveInput = inputs.Player.Move.ReadValue<Vector2>();
        }
    }
}
