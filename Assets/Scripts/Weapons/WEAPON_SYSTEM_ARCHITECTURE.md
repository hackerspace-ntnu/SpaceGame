# Modular Weapon System - Architecture Overview

## Summary
Complete weapon system extending `UsableItem` with modular ammo/magazine mechanics, abstract projectile framework, and support for diverse weapon types through subclassing.

**All files compile with zero errors.**

---

## Core Architecture

### 1. **Projectile.cs** (Abstract Base)
**Purpose**: Standardize projectile behavior across weapon types

**Key Methods**:
- `Initialize(direction, owner, position)` – Set direction, owner root, spawn position
- `UpdateMovement()` – Abstract, implemented by subclasses for movement patterns
- `HandleHit(RaycastHit)` – Apply damage, call OnImpact(), destroy if configured
- `OnImpact(position, normal, collider)` – Override for effects (particles, sounds)
- `IsOwnerHit(transform)` – Prevent self-damage via root comparison
- `GetElapsedTime()` – Helper for time-based movement

**Properties**:
- `lifeTime` – How long until auto-destroy
- `collisionRadius` – Sphere cast radius for collision
- `hitMask` – Layer mask for collision
- `damage` – Damage applied on impact
- `destroyOnHit` – Auto-destroy on first hit?

**Design Philosophy**: 
- Collision/damage standardized in base
- Movement deferred to subclasses for flexibility
- Automatic lifecycle management (Initialize → UpdateMovement → HandleHit → DestroyProjectile)

---

### 2. **Magazine.cs** (Ammo Container)
**Purpose**: Weapon-attached ammo system that doesn't take inventory slot

**Key Methods**:
- `ConsumeAmmo(amount)` – Deduct ammo, fires OnAmmoChanged, returns success
- `AddAmmo(amount)` – Add up to max, returns amount actually added
- `Refill()` – Restore to max, fires OnAmmoFull
- `SetAmmo(amount)` – Direct set with clamping

**Properties**:
- `CurrentAmmo` – Current ammo count
- `MaxAmmo` – Magazine capacity
- `AmmoPercentage` – 0-1 normalized value
- `IsEmpty` – CurrentAmmo == 0?
- `IsFull` – CurrentAmmo == MaxAmmo?

**Events**:
- `OnAmmoChanged` – Fired when ammo count changes
- `OnAmmoEmpty` – Fired when magazine empties
- `OnAmmoFull` – Fired when magazine fills

**Design Pattern**:
- Instantiated as component on weapon GameObject
- Managed entirely by weapon owner
- Doesn't take inventory slot (internal to weapon)
- One magazine per weapon (1:1 relationship)

---

### 3. **Weapon.cs** (Abstract Base)
**Purpose**: Unified framework extending UsableItem for all weapon types

**Key Methods**:
- `TryFire()` – Check fire rate, consume ammo, call Fire(), return success
- `GetAimPoint()` – Raycast from screen center or return far point
- `GetFireDirection()` – Calculate direction from fire origin to aim point
- `GetSpawnPosition()` – Offset spawn position from fire origin
- `AddAmmo(amount)` – Delegate to Magazine
- `Refill()` – Delegate to Magazine
- `Use()` override – Call TryFire() (UsableItem integration)
- `CanUse()` override – Check ammo, fire rate, base class constraints
- `Fire()` abstract – Implemented by subclasses (projectile spawn logic)
- `PlayFireSound()` – Play fire audio via AudioManager

**Properties**:
- `Magazine` – Reference to attached Magazine
- `CurrentAmmo` – Delegates to Magazine
- `MaxAmmo` – Delegates to Magazine
- `FireRatePercent` – 0-1 value for UI display
- `IsReadyToFire` – Can fire now?

**Events**:
- `OnAmmoChanged` – Fired when magazine ammo changes
- `OnFireRateReady` – Fired when fire rate resets

**Configuration**:
- `aimCamera` – Camera for screen-center aiming
- `firePoint` – Transform for projectile spawn point
- `fireRate` – Shots per second
- `spawnOffset` – Distance to spawn projectile ahead of firePoint
- `aimMask` – Layer mask for raycast aiming
- `ammoPerShot` – Ammo consumed per shot
- `fireSound` – AudioClip to play on fire

**Inventory Integration**:
- Extends UsableItem
- Respects `maxUses` from UsableItem
- Override `Use()` to fire weapon
- Override `CanUse()` to check ammo availability
- Weapon takes no inventory slot but is draggable item

**Design Pattern**: Template Method
- Use/CanUse control flow defined in base
- Fire() subclass responsibility
- Aiming/firing direction calculated in base
- Subclasses only implement Fire() to customize projectile behavior

---

## Weapon Implementations

### **BallLightningProjectile** → Extends Projectile
**Type**: Networked homing projectile with AI movement

**Movement Features**:
- `UpdateMovement()` implements wandering AI
- Perlin noise-based wandering on X/Z axes
- Bob motion (up/down oscillation)
- Hover height correction above ground (raycast-based)
- Collision detection via sphere cast

**Visual Features**:
- Dynamic light with flicker based on movement
- Light color/intensity responds to active direct bolts
- Impact spotlight on collision
- Integrates with BallLightningController for bolt effects

**Specialized**:
- Ground hover system with configurable height
- Light intensity/color synced from shader properties
- Wander/bob frequencies customizable per instance

---

### **BallLightningWeapon** → Extends Weapon
**Type**: Projectile weapon spawning BallLightningProjectile

**Key Features**:
- Converts old MonoBehaviour implementation to new Weapon base
- Overrides `Fire()` to instantiate BallLightningProjectile
- Inherits all fire rate, ammo, aiming from base class
- Automatic Magazine creation/management
- Networking support via NetworkObject detection

**Firing Logic**:
- Calculate spawn position and fire direction via base methods
- Instantiate BallLightningProjectile with proper owner
- Play fire sound via `PlayFireSound()`
- Magazine ammo consumed automatically

---

### **EnergyRifle** → Extends Weapon
**Type**: Hitscan instant-hit weapon

**Key Features**:
- Demonstrates alternative weapon type (vs projectile)
- Instant raycast hits instead of spawned projectiles
- Implements `Fire()` for hitscan mechanics
- Configurable spread for recoil/accuracy patterns
- Multiple rays per shot support

**Firing Logic**:
- Fire N rays from firePoint in direction with spread
- Apply HealthComponent damage to all hit targets
- Distance-based damage dropoff
- Visual effects: muzzle flash, impact particles, shot tracers

**Customization Options**:
- `rayDistance` – Max raycast distance
- `raysPerShot` – Number of rays per fire (spread pattern)
- `shotSpread` – Random spread per ray
- `spreadAngle` – Controlled spread pattern
- `damageDropoff` – Damage reduction per unit

---

## System Integration Points

### With UsableItem
- Weapons are pickup-able inventory items
- `maxUses` can track weapon durability/lifetime
- `Use()` called when item used from inventory
- `CanUse()` checks ammo + fire rate before allowing use

### With AudioManager
- `PlayFireSound()` plays fireSound via AudioManager
- Location: firePoint position
- Volume: configurable per weapon

### With HealthComponent
- Projectiles and hitscan deal damage to HealthComponent
- Owner detection prevents self-damage

### With NetworkObject (Netcode)
- Weapons detect NetworkObject owner
- BallLightningProjectile uses ownerRoot for networked ownership
- Ensures proper damage attribution in multiplayer

---

## Extensibility Pattern

### Creating New Weapon Type (3 Steps)

1. **Create weapon class extending Weapon**
   ```csharp
   public class MyWeapon : Weapon
   {
       protected override void Fire()
       {
           // Implement firing logic
       }
   }
   ```

2. **Magazine auto-created** (handled by Weapon.OnEnable)
   - No manual instantiation needed
   - Ammo management automatic

3. **Integrate with inventory**
   - Assign to player inventory as UsableItem
   - Fire rate + aiming handled by base
   - Custom Fire() logic only

### Creating New Projectile Type (3 Steps)

1. **Create projectile class extending Projectile**
   ```csharp
   public class MyProjectile : Projectile
   {
       protected override void UpdateMovement()
       {
           // Implement movement logic
       }
   }
   ```

2. **Spawn from weapon Fire()**
   ```csharp
   MyProjectile proj = Instantiate(prefab, spawnPos, Quaternion.identity);
   proj.Initialize(fireDir, ownerRoot, spawnPos);
   ```

3. **Override OnImpact() for effects**
   ```csharp
   protected override void OnImpact(Vector3 pos, Vector3 normal, Collider hit)
   {
       // Spawn effects, sounds, etc
   }
   ```

---

## Configuration Checklist

### For BallLightningWeapon
- [ ] Assign projectilePrefab
- [ ] Assign projectileSpawnPoint
- [ ] Set fireRate (6 = 6 shots/sec)
- [ ] Configure aimCamera or will use Camera.main
- [ ] Set fireSound (AudioClip)

### For EnergyRifle
- [ ] Set fireRate
- [ ] Configure rayDistance
- [ ] Assign fireSound
- [ ] Set up muzzleFlashPrefab/impactEffectPrefab (optional)
- [ ] Configure raysPerShot for spread pattern

### Magazine (Auto-Created)
- [ ] Configure MaxAmmo per weapon type
- [ ] Ammo is consumed by ammoPerShot setting

---

## Design Decisions

✅ **Magazines don't take inventory slots**
- Magazine is component on weapon GameObject
- When weapon is picked up, magazine comes with it
- Ammo count displays via weapon's CurrentAmmo property

✅ **Abstract Projectile class**
- Standardizes collision, damage, lifecycle
- Movement deferred to subclasses
- Enables instant-hit (hitscan) weapons via Fire() override

✅ **Template Method pattern**
- Base class defines firing flow (TryFire → Fire)
- Subclasses implement Fire() only
- Aiming/direction calculation shared

✅ **UsableItem integration**
- Weapons are inventory items (same as consumables)
- maxUses can track durability
- Standard pickup/drop mechanics

✅ **Modular architecture**
- Each class has single responsibility
- Easy to extend without modification
- Composition over inheritance (Magazine ← Weapon)
