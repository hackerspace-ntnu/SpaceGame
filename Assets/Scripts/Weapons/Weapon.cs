using UnityEngine;
using System;

/// <summary>
/// Abstract base class for all weapon types.
/// Extends UsableItem to integrate with inventory system.
/// Handles ammo management, firing mechanics, and firing input.
/// Subclasses define specific projectile/hit behavior.
/// </summary>
public abstract class Weapon : UsableItem
{
    [Header("Weapon Configuration")]
    [SerializeField] protected Camera aimCamera;
    [SerializeField] protected Transform firePoint;
    [SerializeField] protected Transform handle1; // Primary grip point (main hand attachment)
    [SerializeField] protected Transform handle2; // Secondary grip point (support hand - for future use)
    [SerializeField] protected float fireRate = 1f; // Shots per second
    [SerializeField] protected float spawnOffset = 0.5f;
    [SerializeField] protected LayerMask aimMask = ~0;

    [Header("Ammo")]
    [SerializeField] private Magazine magazine;
    [SerializeField] protected int ammoPerShot = 1;

    [Header("Audio")]
    [SerializeField] protected string fireSoundName = ""; // FMOD sound event or name
    [SerializeField] protected float fireSoundVolume = 1f;

    [Header("Charging")]
    [SerializeField] protected bool enableCharging = false; // Toggle charging mode on/off
    [SerializeField] protected float chargeDuration = 3f; // Time to fully charge in seconds
    [SerializeField] protected AnimationCurve chargeProgressCurve = AnimationCurve.Linear(0, 0, 1, 1); // Curve for charge progression

    protected float nextFireTime;
    protected bool canFire = true;
    protected AudioManager audioManager;
    protected bool isCharging = false;
    protected float chargeStartTime;
    protected IChargeable chargedProjectile; // Reference to currently charging projectile

    public event Action<int> OnAmmoChanged;
    public event Action OnFireRateReady;

    public Magazine Magazine => magazine;
    public int CurrentAmmo => magazine != null ? magazine.CurrentAmmo : 0;
    public int MaxAmmo => magazine != null ? magazine.MaxAmmo : 0;
    public float FireRatePercent => Mathf.Clamp01((Time.time - (nextFireTime - 1f / Mathf.Max(0.01f, fireRate))) / (1f / Mathf.Max(0.01f, fireRate)));
    public bool IsReadyToFire => Time.time >= nextFireTime;
    public Transform Handle1 => handle1; // Primary grip point
    public Transform Handle2 => handle2; // Secondary grip point

    protected virtual void OnEnable()
    {
        // Get audio manager
        if (audioManager == null)
        {
            audioManager = FindObjectOfType<AudioManager>();
        }

        // Auto-find camera if not assigned
        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }

        // Ensure magazine exists
        if (magazine == null)
        {
            magazine = GetComponent<Magazine>();
        }

        if (magazine == null)
        {
            magazine = gameObject.AddComponent<Magazine>();
        }

        // Refill magazine when weapon is equipped/enabled
        if (magazine != null)
        {
            magazine.Refill();
        }

        // Subscribe to magazine changes
        if (magazine != null)
        {
            magazine.OnAmmoChanged += OnMagazineAmmoChanged;
        }

        // Reset firing state
        nextFireTime = Time.time;
        canFire = true;

        // Warn if handles aren't set (they're optional for now, but should be configured)
        if (handle1 == null)
        {
            Debug.LogWarning($"Weapon '{gameObject.name}' has no Handle1 assigned. Set this in the inspector to a child transform for proper hand attachment.", this);
        }
    }

    protected virtual void OnDisable()
    {
        if (magazine != null)
        {
            magazine.OnAmmoChanged -= OnMagazineAmmoChanged;
        }

        // Cancel charging when weapon is disabled
        CancelCharging();
    }

    protected virtual void Update()
    {
        // Check if ready to fire
        if (Time.time >= nextFireTime && !IsReadyToFire)
        {
            OnFireRateReady?.Invoke();
        }

        // Update charging if active
        if (isCharging && chargedProjectile != null)
        {
            float chargeElapsed = Time.time - chargeStartTime;
            float chargeProgress = Mathf.Clamp01(chargeElapsed / chargeDuration);
            
            // Apply animation curve
            chargeProgress = chargeProgressCurve.Evaluate(chargeProgress);
            
            // Double-check projectile hasn't been destroyed
            if (chargedProjectile == null)
            {
                isCharging = false;
                return;
            }
            
            // Update projectile with charge progress
            chargedProjectile.UpdateCharge(chargeProgress);
        }

        // Rotate weapon to match camera pitch (up/down look direction)
        UpdateWeaponRotation();
    }

    /// <summary>
    /// Rotate weapon to point in the direction the camera is looking (pitch only).
    /// </summary>
    protected virtual void UpdateWeaponRotation()
    {
        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }

        if (aimCamera == null)
        {
            return;
        }

        // Get camera's forward direction
        Vector3 cameraForward = aimCamera.transform.forward;

        // Create a rotation that points toward the camera's forward direction
        // This includes both pitch (up/down) and yaw (left/right)
        transform.rotation = Quaternion.LookRotation(cameraForward, aimCamera.transform.up);
    }

    /// <summary>
    /// Attempt to fire the weapon.
    /// If charging is enabled and no projectile is charging, spawn and start charging.
    /// If a projectile is already charging, fire it.
    /// Returns false if weapon can't fire (no ammo, fire rate not ready, etc).
    /// </summary>
    public bool TryFire()
    {
        if (!canFire || !IsReadyToFire)
        {
            return false;
        }

        // If charging is enabled and we're already charging, launch the charged projectile
        if (enableCharging && isCharging)
        {
            if (chargedProjectile != null)
            {
                try
                {
                    // Tell the projectile to finish charging and be ready to move
                    chargedProjectile.OnChargeComplete();
                    
                    // Launch the already-charged projectile with current aim direction
                    Fire();
                }
                catch (MissingReferenceException)
                {
                    Debug.LogWarning("Charged projectile was destroyed before launch.");
                }
            }
            
            chargedProjectile = null;
            isCharging = false;
            nextFireTime = Time.time + (1f / Mathf.Max(0.01f, fireRate));
            return true;
        }

        // Normal fire or start charging
        if (magazine == null || !magazine.ConsumeAmmo(ammoPerShot))
        {
            // No ammo
            return false;
        }

        // If charging is enabled, start charging (spawns projectile)
        if (enableCharging && !isCharging)
        {
            StartCharging();
            return true;
        }
        else
        {
            // Normal firing (no charging)
            Fire();
            nextFireTime = Time.time + (1f / Mathf.Max(0.01f, fireRate));
            return true;
        }
    }

    /// <summary>
    /// Start the charging sequence for a chargeable projectile.
    /// Spawns the projectile and begins charging it.
    /// Subclasses override SpawnChargeProjectile() to create the projectile.
    /// </summary>
    protected virtual void StartCharging()
    {
        isCharging = true;
        chargeStartTime = Time.time;
        SpawnChargeProjectile(); // Spawn the projectile for charging
    }

    /// <summary>
    /// Spawn a projectile for charging. Override in subclasses.
    /// Should set chargedProjectile to the spawned projectile.
    /// </summary>
    protected virtual void SpawnChargeProjectile()
    {
        // Subclasses override this to spawn the chargeable projectile
    }

    /// <summary>
    /// Calculate where the weapon is aiming (center screen or raycast point).
    /// </summary>
    protected virtual Vector3 GetAimPoint()
    {
        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }

        if (aimCamera != null)
        {
            Ray ray = aimCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 targetPoint = ray.origin + ray.direction * 500f; // Default far distance

            if (Physics.Raycast(ray, out RaycastHit hit, 500f, aimMask, QueryTriggerInteraction.Ignore))
            {
                targetPoint = hit.point;
            }

            return targetPoint;
        }

        return transform.position + transform.forward * 500f;
    }

    /// <summary>
    /// Get the direction the projectile should fire in.
    /// </summary>
    protected virtual Vector3 GetFireDirection()
    {
        Transform origin = GetFireOrigin();
        Vector3 aimPoint = GetAimPoint();
        Vector3 direction = (aimPoint - origin.position).normalized;

        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = origin.forward;
        }

        return direction;
    }

    /// <summary>
    /// Get the origin point for projectile spawn.
    /// </summary>
    protected virtual Transform GetFireOrigin()
    {
        if (firePoint != null)
        {
            return firePoint;
        }

        return aimCamera != null ? aimCamera.transform : transform;
    }

    /// <summary>
    /// Get spawn position for projectile.
    /// </summary>
    protected virtual Vector3 GetSpawnPosition()
    {
        Transform origin = GetFireOrigin();
        return origin.position + origin.forward * spawnOffset;
    }

    /// <summary>
    /// Override Use() from UsableItem - this is called when weapon item is used.
    /// Should attempt to fire or trigger weapon action.
    /// </summary>
    protected override void Use()
    {
        // Fire attempt is handled by TryFire()
        TryFire();
    }

    /// <summary>
    /// Override CanUse() to check ammo.
    /// Fire rate is checked in TryFire(), not here, because CanUse() must return true
    /// for the UsableItem system to call Use() from the inventory.
    /// </summary>
    protected override bool CanUse()
    {
        // Check base class first (max uses, etc.)
        if (!base.CanUse())
        {
            return false;
        }

        // Check if we have ammo
        if (magazine == null || magazine.IsEmpty)
        {
            return false;
        }

        // Fire rate is checked in TryFire(), not here
        // This allows the inventory system to properly call Use()
        return true;
    }

    /// <summary>
    /// Cancel charging if active (e.g., when weapon is unequipped).
    /// </summary>
    protected virtual void CancelCharging()
    {
        if (isCharging && chargedProjectile != null)
        {
            chargedProjectile.OnChargeCancelled();
        }

        isCharging = false;
        chargedProjectile = null;
    }

    /// <summary>
    /// Subclasses implement actual firing behavior here.
    /// </summary>
    protected abstract void Fire();

    /// <summary>
    /// Called by magazine when ammo changes.
    /// </summary>
    protected virtual void OnMagazineAmmoChanged()
    {
        OnAmmoChanged?.Invoke(CurrentAmmo);
    }

    /// <summary>
    /// Add ammo to magazine.
    /// </summary>
    public int AddAmmo(int amount)
    {
        if (magazine == null)
        {
            return 0;
        }

        return magazine.AddAmmo(amount);
    }

    /// <summary>
    /// Refill weapon magazine to full.
    /// </summary>
    public void Refill()
    {
        if (magazine != null)
        {
            magazine.Refill();
        }
    }

    /// <summary>
    /// Play fire sound via AudioManager.
    /// </summary>
    protected virtual void PlayFireSound()
    {
        if (!string.IsNullOrEmpty(fireSoundName) && audioManager != null)
        {
            audioManager.PlaySFX3d(fireSoundName, GetFireOrigin().position);
        }
    }
}
