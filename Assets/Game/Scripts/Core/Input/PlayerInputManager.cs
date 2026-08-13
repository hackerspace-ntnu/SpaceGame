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
    
        public event Action OnJumpPressed;
    
        public event Action OnDashPressed;

        public event Action OnBackpackPressed;

        private void Awake()
        {
            inputs = new InputControls();

            // The inspector value is the authored default; once the player has touched the
            // settings menu's own switch, that is what the wheel obeys.
            GameSettings.SeedInvertHotbarScroll(invertHotbarScroll);
        }

        private void OnEnable()
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
            inputs.Player.Backpack.performed += _ => OnBackpackPressed?.Invoke();

            inputs.Enable();
        }

        private void OnDisable()
        {
            inputs.Hotbar.HotbarScroll.performed -= HandleHotbarScroll;
            inputs.Disable();
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
