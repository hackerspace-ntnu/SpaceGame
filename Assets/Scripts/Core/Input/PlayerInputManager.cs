using System;
using UnityEngine;

/// <summary>
/// Single source of truth for all player input.
/// Owns the InputControls instance, fires events and exposes read values.
/// All other scripts get input exclusively from here.
/// </summary>
public class PlayerInputManager : MonoBehaviour
{
    private InputControls inputs;
    public Vector2 LookInput  { get; private set; }
    public Vector2 MoveInput  { get; private set; }
    
    public event Action<int> OnHotbarPressed;
    
    public event Action OnDropPressed;
    
    public event Action OnInteractPressed;
    
    public event Action OnUsePressed;
    
    public event Action OnJumpPressed;
    
    public event Action OnDashPressed;

    private void Awake()
    {
        inputs = new InputControls();
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

        // World interaction
        inputs.Player.Interact.performed += _ => OnInteractPressed?.Invoke();
        inputs.Player.Jump.performed     += _ => OnJumpPressed?.Invoke();
        inputs.Player.Dash.performed   += _ => OnDashPressed?.Invoke();
        inputs.Player.Use.performed   += _ => OnUsePressed?.Invoke();

        inputs.Enable();
    }

    private void OnDisable() => inputs.Disable();

    private void Update()
    {
        LookInput = inputs.Player.Look.ReadValue<Vector2>();
        MoveInput = inputs.Player.Move.ReadValue<Vector2>();
    }
}
