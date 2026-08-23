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
        /// The secondary trigger — right mouse, left gamepad trigger.
        ///
        /// Built in code rather than added to InputSystem_Actions, because
        /// InputControls.cs embeds its own copy of that asset's JSON and is what
        /// actually binds at runtime: editing the .inputactions does nothing
        /// until the generated file is regenerated, and hand-patching 3000 lines
        /// of generated code to add one button is a much larger risk to every
        /// other control in the game than owning this one action here.
        ///
        /// It still lives on this component so it obeys the same enable and
        /// disable as everything else — an alternate fire that kept working
        /// through death and menus would be worse than not having one.
        /// </summary>
        public event Action OnAltUsePressed;

        private InputAction altUse;

        public event Action OnJumpPressed;
    
        public event Action OnDashPressed;

        public event Action OnBackpackPressed;

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

            altUse = new InputAction("AltUse", InputActionType.Button);
            altUse.AddBinding("<Mouse>/rightButton").WithGroup("Keyboard&Mouse");
            altUse.AddBinding("<Gamepad>/leftTrigger").WithGroup("Gamepad");

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

            // Bound here with the rest, and exactly once, for the same reason
            // they are: the callback is a lambda that nothing can unsubscribe,
            // so binding it in OnEnable would leave another copy attached after
            // every death and respawn.
            altUse.performed += _ => OnAltUsePressed?.Invoke();
        }

        private void OnEnable()
        {
            EnsureInputs();
            inputs.Enable();
            altUse?.Enable();
        }

        private void OnDisable()
        {
            // Not EnsureInputs: if the asset was never created there is nothing enabled to stop,
            // and building one here just to disable it would leak it past OnDestroy.
            inputs?.Disable();
            altUse?.Disable();

            // Stale axes outlive the disable otherwise — MoveInput and LookInput are only written
            // by Update, so whatever the stick last read stays latched. On death that is a live
            // movement vector waiting for the next system that reads it.
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;
        }

        private void OnDestroy()
        {
            inputs?.Dispose();
            inputs = null;

            altUse?.Dispose();
            altUse = null;
        }

        /// <summary>
        /// Turns a wheel delta into whole slot steps.
        /// <para>
        /// The delta is not comparable across platforms — Windows reports 120 per notch where other
        /// backends report single digits, and a trackpad streams fractions of a notch every frame.
        /// A notch is the largest delta a device produces, so measuring against the biggest value
        /// seen makes one notch move one slot everywhere, while a trackpad's smaller deltas pile up
        /// until they add up to a notch instead of racing through the bar.
        /// </para>
        /// </summary>
        private void HandleHotbarScroll(InputAction.CallbackContext context)
        {
            float delta = context.ReadValue<float>();
            if (Mathf.Abs(delta) < ScrollDeadzone) return;

            measuredScrollUnit = Mathf.Max(Mathf.Abs(delta), measuredScrollUnit * ScrollUnitDecay);
            float unitsPerSlot = scrollUnitsPerSlot > 0f ? scrollUnitsPerSlot : measuredScrollUnit;

            // Reversing direction starts the count again, so a flick back the other way is not
            // held up by leftovers pointing the wrong way.
            if (scrollAccumulator != 0f && Mathf.Sign(delta) != Mathf.Sign(scrollAccumulator))
                scrollAccumulator = 0f;

            scrollAccumulator += delta;

            while (Mathf.Abs(scrollAccumulator) >= unitsPerSlot)
            {
                float sign = Mathf.Sign(scrollAccumulator);
                scrollAccumulator -= sign * unitsPerSlot;

                int direction = sign > 0f ? -1 : 1;   // Scrolling up moves back along the bar.
                OnHotbarScrolled?.Invoke(GameSettings.InvertHotbarScroll ? -direction : direction);
            }
        }

        private void Update()
        {
            LookInput = inputs.Player.Look.ReadValue<Vector2>();
            MoveInput = inputs.Player.Move.ReadValue<Vector2>();
        }
    }
}
