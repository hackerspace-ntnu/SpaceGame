# Weapon Charging System Architecture

## Overview
This document describes the charging system architecture for weapons that support gradual power-up mechanics.

## Core Components

### 1. IChargeable Interface (`IChargeable.cs`)
Defines the contract for projectiles that support charging.

**Key Methods:**
- `GetChargeLevel()` - Returns normalized charge progress (0-1)
- `UpdateCharge(float chargeProgress)` - Called each frame to update charge state
- `OnChargeComplete()` - Called when charge reaches maximum
- `OnChargeCancelled()` - Called if charging is interrupted/cancelled

**Design Pattern:** Interface segregation - only projectiles that charge implement this

### 2. Weapon Base Class (`Weapon.cs`)
Enhanced with charging capability management.

**New Fields:**
```csharp
[SerializeField] protected bool enableCharging = false;           // Toggle charging on/off
[SerializeField] protected float chargeDuration = 3f;            // Time to fully charge
[SerializeField] protected AnimationCurve chargeProgressCurve;   // Charge progression curve
protected bool isCharging = false;
protected IChargeable chargedProjectile;                         // Current charging projectile
```

**Key Methods:**
- `TryFire()` - ENHANCED: Handles both normal and charging fire modes
  - First call spawns projectile and enters charging mode
  - Subsequent calls during charging fire the charged projectile
- `Update()` - ENHANCED: Manages charging progression
  - Updates projectile's charge state
  - Applies animation curve for natural progression
  - Detects when charge is complete
- `StartCharging()` - Protected method to begin charging sequence
- `CancelCharging()` - Stops charging and destroys projectile if needed

**Charging Flow:**
```
TryFire() (first press)
  ↓
StartCharging()
  ↓
Fire() (weapon spawns projectile and sets chargedProjectile)
  ↓
isCharging = true, chargeStartTime = now
  ↓
Each Update():
  - Calculate chargeProgress (0 to 1)
  - Apply chargeProgressCurve
  - Call chargedProjectile.UpdateCharge(chargeProgress)
  - When chargeProgress >= 1, call chargedProjectile.OnChargeComplete()
  ↓
TryFire() (second press)
  ↓
Fire() (launches the charged projectile)
  ↓
isCharging = false, chargedProjectile = null
```

### 3. BallLightningProjectile (`BallLightningProjectile.cs`)
Implements IChargeable for charging mechanics.

**New Fields:**
```csharp
[SerializeField] private float chargeMinScale = 0.2f;           // Starting size
[SerializeField] private float chargeMaxScale = 1f;             // Final size
[SerializeField] private AnimationCurve chargeScaleCurve;       // Scale progression
private float chargeLevel = 0f;                                 // 0 to 1
private bool isChargeComplete = false;                          // Charge state
```

**IChargeable Implementation:**
- `UpdateCharge(float chargeProgress)` - Scales projectile from min to max
- `OnChargeComplete()` - Sets isChargeComplete = true, locks scale
- `OnChargeCancelled()` - Destroys projectile without launching

**Movement Changes:**
- `UpdateMovement()` - Frozen while charging (`!isChargeComplete`)
- Resumes movement after charge complete

### 4. BallLightningWeapon (`BallLightningWeapon.cs`)
Weapon implementation with charging support.

**Key Changes:**
- Stores reference to `chargedProjectile` after spawning
- Sets `chargedProjectile` interface reference for the weapon to track
- Fire() now populates the weapon's charging reference

## Usage Flow

### For Users (Players)
1. **First Click**: Press fire
   - Weapon spawns ball at very small size (chargeMinScale = 0.2)
   - Ball freezes in place and grows over 3 seconds
   - Player sees and hears charging feedback
   
2. **Wait 3 Seconds**: Ball grows to full size
   - Animation curve smooths the growth
   - OnChargeComplete() is called internally
   - Ball is now ready to launch

3. **Second Click**: Press fire again
   - Fully charged ball launches with all movement/effects active
   - Weapon is back to normal state

### For Developers (Adding Charging to New Weapons)

1. **Create Projectile:**
   - Inherit from `Projectile` and implement `IChargeable`
   - Implement the 4 required methods
   - Example: CustomProjectile with custom charging visuals

2. **Set Weapon to Charging Mode:**
   - In weapon prefab inspector, set `Enable Charging = true`
   - Adjust `Charge Duration` (how long to charge)
   - Adjust `Charge Progress Curve` (visual progression feel)

3. **Weapon Automatically Handles:**
   - Spawning the projectile on first fire
   - Tracking charge progress
   - Launching on second fire
   - Cancelling if weapon is unequipped

## Key Design Decisions

### 1. Two-Press Model
- First press: Spawn and charge
- Second press: Fire
- **Benefit**: Clear, predictable interaction pattern
- **Alternative Considered**: Hold-to-charge (less suitable for inventory system)

### 2. Interface Over Inheritance
- IChargeable interface instead of base class
- **Benefit**: Projectiles can add charging to any existing type
- **Example**: Could add IChargeable to non-Projectile objects

### 3. Weapon Ownership of Charging Logic
- Weapon manages charge state and progression
- Projectile only handles charging visualization
- **Benefit**: Separation of concerns, weapon can handle ammo/fire rate/charging centrally
- **Projectile Responsibility**: "How do I look when charging"
- **Weapon Responsibility**: "How long should charging take"

### 4. Animation Curves for Feel
- `chargeProgressCurve` in Weapon - controls charge speed/acceleration
- `chargeScaleCurve` in Projectile - controls how size grows
- **Benefit**: Tunable feel without code changes

## Extensibility

### Adding Charging to Other Weapons
```csharp
public class NewChargeableWeapon : Weapon
{
    [SerializeField] private NewProjectile projectilePrefab;
    
    protected override void Fire()
    {
        NewProjectile proj = Instantiate(projectilePrefab, ...);
        proj.Initialize(...);
        
        if (enableCharging)
        {
            chargedProjectile = proj as IChargeable;
        }
    }
}
```

### Custom Charging Visuals
```csharp
public class CustomChargeProjectile : Projectile, IChargeable
{
    public void UpdateCharge(float chargeProgress)
    {
        // Custom scaling
        float scale = Mathf.Lerp(0.1f, 2f, chargeProgress);
        transform.localScale = Vector3.one * scale;
        
        // Custom emission/glow
        material.SetFloat("_EmissionIntensity", Mathf.Lerp(0.5f, 2f, chargeProgress));
        
        // Custom sounds
        if (chargeProgress > lastSoundProgress)
        {
            audioManager.PlaySFX("charge_tick");
        }
    }
}
```

## Configurations

### BallLightningWeapon in Inspector
- **Enable Charging**: true
- **Charge Duration**: 3 (seconds)
- **Charge Progress Curve**: EaseInOut (smooth acceleration/deceleration)

### BallLightningProjectile in Inspector
- **Charge Min Scale**: 0.2 (starts very small)
- **Charge Max Scale**: 1.0 (grows to normal size)
- **Charge Scale Curve**: EaseInOut (smooth growth)

## Testing Checklist

- [ ] First press spawns small projectile
- [ ] Projectile frozen during charging
- [ ] Projectile grows smoothly over 3 seconds
- [ ] After 3 seconds, size locks at max
- [ ] Second press fires the projectile
- [ ] Projectile moves normally after fire
- [ ] Unequipping weapon during charge cancels and destroys projectile
- [ ] Normal (non-charging) weapons still work
- [ ] Fire rate is respected after launch
- [ ] Ammo is consumed on first press (charge spawn)

## Performance Notes

- Projectile only spawned once (on first press)
- No physics running during charge (frozen)
- Minimal overhead: just scale updates and animation curve evaluation
- Memory: One projectile instance per charge sequence
