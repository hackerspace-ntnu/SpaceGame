using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public event Action OnUsePressed;
    public event Action<float> OnScrollAdjusted;

    private InputControls controls;

    private void Awake()
    {
        controls = new InputControls();
    }

    private void OnEnable()
    {
        controls.Player.Attack.performed += OnUsePerformed;
        controls.UI.ScrollWheel.performed += OnScrollPerformed;

        controls.Player.Enable();
        controls.UI.Enable();
    }

    private void OnDisable()
    {
        controls.Player.Attack.performed -= OnUsePerformed;
        controls.UI.ScrollWheel.performed -= OnScrollPerformed;

        controls.Player.Disable();
        controls.UI.Disable();
    }

    private void OnUsePerformed(InputAction.CallbackContext context)
    {
        OnUsePressed?.Invoke();
    }

    private void OnScrollPerformed(InputAction.CallbackContext context)
    {
        float scrollDelta = context.ReadValue<Vector2>().y;
        if (Mathf.Abs(scrollDelta) > 0.01f)
        {
            OnScrollAdjusted?.Invoke(scrollDelta);
        }
    }
}
