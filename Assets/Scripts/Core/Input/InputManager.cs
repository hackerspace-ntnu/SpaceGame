using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceGame.Core
{
    public class InputManager : MonoBehaviour
    {

        public event Action OnUsePressed;

        private InputAction useAction;

        private void Awake()
        {
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
}
