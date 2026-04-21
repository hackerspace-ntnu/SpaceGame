using UnityEngine;
using System;

/// <summary>
/// Ammunition/Magazine container that holds ammo for a weapon.
/// Attached to weapon, doesn't take inventory slot.
/// Handles ammo count and reload mechanics.
/// </summary>
public class Magazine : MonoBehaviour
{
    [Header("Magazine Configuration")]
    [SerializeField] private int maxAmmo = 60;
    [SerializeField] private int currentAmmo;

    public event Action OnAmmoChanged;
    public event Action OnAmmoEmpty;
    public event Action OnAmmoFull;

    public int CurrentAmmo => currentAmmo;
    public int MaxAmmo => maxAmmo;
    public float AmmoPercentage => maxAmmo > 0 ? (float)currentAmmo / maxAmmo : 0f;
    public bool IsEmpty => currentAmmo <= 0;
    public bool IsFull => currentAmmo >= maxAmmo;

    private void Awake()
    {
        // Initialize with full ammo
        currentAmmo = maxAmmo;
    }

    private void OnEnable()
    {
        // Ensure ammo is initialized (handles both creation and re-enable scenarios)
        if (currentAmmo <= 0)
        {
            currentAmmo = maxAmmo;
        }
    }

    /// <summary>
    /// Consume ammo from the magazine.
    /// </summary>
    public bool ConsumeAmmo(int amount = 1)
    {
        if (currentAmmo < amount)
        {
            return false;
        }

        currentAmmo -= amount;
        OnAmmoChanged?.Invoke();

        if (currentAmmo <= 0)
        {
            OnAmmoEmpty?.Invoke();
        }

        return true;
    }

    /// <summary>
    /// Add ammo to the magazine (capped at max).
    /// </summary>
    public int AddAmmo(int amount)
    {
        int added = 0;
        while (added < amount && currentAmmo < maxAmmo)
        {
            currentAmmo++;
            added++;
        }

        if (added > 0)
        {
            OnAmmoChanged?.Invoke();
        }

        if (currentAmmo >= maxAmmo)
        {
            OnAmmoFull?.Invoke();
        }

        return added;
    }

    /// <summary>
    /// Refill magazine to full.
    /// </summary>
    public void Refill()
    {
        currentAmmo = maxAmmo;
        OnAmmoChanged?.Invoke();
        OnAmmoFull?.Invoke();
    }

    /// <summary>
    /// Set ammo to specific amount (clamped to 0-max).
    /// </summary>
    public void SetAmmo(int amount)
    {
        currentAmmo = Mathf.Clamp(amount, 0, maxAmmo);
        OnAmmoChanged?.Invoke();

        if (currentAmmo <= 0)
        {
            OnAmmoEmpty?.Invoke();
        }
        else if (currentAmmo >= maxAmmo)
        {
            OnAmmoFull?.Invoke();
        }
    }
}
