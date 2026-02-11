using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    public event Action OnUsePressed;

    private InputAction useAction;

    private void Awake()
    {
        Instance = this;
        useAction = InputSystem.actions.FindAction("Attack");
    }

    private void Update()
    {
        if (useAction != null && useAction.WasPressedThisFrame())
        {
            OnUsePressed?.Invoke();
        }
    }
}
