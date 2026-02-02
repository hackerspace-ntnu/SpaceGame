using System;
using UnityEngine;

public class HotbarController : MonoBehaviour
{
    private InputControls controls;
    
    public event Action<int> OnHotbarKeyPressed;

    private void Awake()
    {
        controls = new InputControls();
    }

    private void OnEnable()
    {
        controls.Hotbar.Hotbar1.performed += ctx => OnHotbarKeyPressed?.Invoke(0);
        controls.Hotbar.Hotbar2.performed += ctx => OnHotbarKeyPressed?.Invoke(1);
        controls.Hotbar.Hotbar3.performed += ctx => OnHotbarKeyPressed?.Invoke(2);
        controls.Hotbar.Hotbar4.performed += ctx => OnHotbarKeyPressed?.Invoke(3);
        controls.Hotbar.Hotbar5.performed += ctx => OnHotbarKeyPressed?.Invoke(4);
        controls.Hotbar.Hotbar6.performed += ctx => OnHotbarKeyPressed?.Invoke(5);
        controls.Hotbar.Hotbar7.performed += ctx => OnHotbarKeyPressed?.Invoke(6);
        controls.Hotbar.Hotbar8.performed += ctx => OnHotbarKeyPressed?.Invoke(7);
        controls.Hotbar.Hotbar9.performed += ctx => OnHotbarKeyPressed?.Invoke(8);
        controls.Hotbar.Hotbar10.performed += ctx => OnHotbarKeyPressed?.Invoke(9);
        controls.Hotbar.Enable();
    }

    private void OnDisable()
    {
        controls.Hotbar.Disable();
    }
}