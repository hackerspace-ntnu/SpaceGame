# Weapon Pickup System Setup

## Quick Start - Add BallLightning Weapon to Your Scene

### Step 1: Create a Weapon Prefab Wrapper
The `cixinGunFinal.prefab` is a visual model that needs to be wrapped to work as a pickup item. 

**In Unity Editor:**
1. Create a new empty GameObject in your scene or as a new prefab
2. Name it `BallLightningWeapon_Pickup`
3. Add these components:
   - **Transform** (default, position where weapon spawns)
   - **Mesh Filter + Mesh Renderer** (OR drag cixinGunFinal as child)
   - **Rigidbody** (IsKinematic = true, use Gravity = true)
   - **Capsule/Box Collider** (as interactable trigger)
   - **NetworkObject** (for multiplayer support)
   - **PickupableItem** (point item to BallLightning InventoryItem)

### Step 2: Create BallLightning InventoryItem ScriptableObject
1. Right-click in Project > Create > Items > Item
2. Name it `BallLightningWeapon`
3. Configure:
   - **itemName**: "Ball Lightning Weapon"
   - **itemPrefab**: Drag the equipped weapon prefab (with BallLightningWeapon component)
   - **icon**: Optional sprite for UI display

### Step 3: Add to Scene
1. Drag `cixinGunFinal.prefab` into your scene at desired pickup location
2. Add components:
   - Right-click > Add Component > NetworkObject
   - Right-click > Add Component > PickupableItem
   - Right-click > Add Component > DropItemPhysics
   - Set Rigidbody: IsKinematic = true
   - PickupableItem.item = BallLightningWeapon (the ScriptableObject from Step 2)

### Step 4: Configure Equipment Prefab
The weapon shown in player's hand needs:
- **BallLightningWeapon** component (inherits from Weapon, extends UsableItem)
- **Magazine** component (auto-created by Weapon.OnEnable)
- **AudioManager** reference in scene (auto-found if one exists)
- Fire rate, ammo settings configured in inspector

---

## File References

**Weapon System Foundation:**
- `Assets/Scripts/Weapons/Weapon.cs` – Abstract base (extends UsableItem)
- `Assets/Scripts/Weapons/Magazine.cs` – Ammo container
- `Assets/Scripts/Weapons/Projectiles/Projectile.cs` – Abstract projectile base

**Implementation:**
- `Assets/Scripts/Weapons/BallLightningWeapon.cs` – Extends Weapon, spawns projectiles
- `Assets/Scripts/Weapons/Projectiles/BallLightningProjectile.cs` – Extends Projectile
- `Assets/Scripts/Weapons/EnergyRifle.cs` – Alternative weapon example

**Inventory Integration:**
- `Assets/Scripts/Items/PickupableItem.cs` – Makes items pickupable
- `Assets/Scripts/Inventory/Components/EquipmentController.cs` – Equips items in player hand
- `Assets/Scripts/Items/InventoryItem.cs` – ScriptableObject for inventory items

**References:**
- `Assets/Prefabs/cixinGunFinal.prefab` – Visual model for BallLightning weapon

---

## Why Weapons Don't Take Inventory Slots

The Magazine is a **component attached to the weapon GameObject**, not an inventory item itself:

```
Player Inventory
├── Slot 0: BallLightning Weapon ← Takes 1 slot
│   └── Magazine (internal) ← 30 ammo, doesn't take slot
├── Slot 1: Empty
└── Slot 2: Health Potion
```

When the weapon is equipped:
1. `EquipmentController` instantiates the weapon prefab in player's hand
2. Weapon's `OnEnable()` auto-creates Magazine component
3. Player can fire and Magazine tracks ammo consumption
4. Ammo displays via weapon's `CurrentAmmo` property

---

## Weapon Firing Integration

When weapon is equipped and player presses Use (default: Right Mouse Button):

1. **EquipmentController** calls `UsableItem.TryUse()`
2. **Weapon.Use()** (override) calls `TryFire()`
3. **Weapon.TryFire()** checks:
   - ✓ Fire rate ready?
   - ✓ Ammo available?
4. If both pass: `Fire()` (abstract, subclass implements)
   - **BallLightningWeapon.Fire()** → spawns BallLightningProjectile
   - **EnergyRifle.Fire()** → raycast hitscan damage
5. Magazine.ConsumeAmmo() deducts ammo
6. PlayFireSound() plays audio

---

## Creating Additional Weapons

### 1. Create weapon script extending Weapon
```csharp
public class RailgunWeapon : Weapon
{
    [SerializeField] private RailgunProjectile projectilePrefab;
    
    protected override void Fire()
    {
        Vector3 spawnPos = GetSpawnPosition();
        Vector3 fireDir = GetFireDirection();
        
        RailgunProjectile proj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        proj.Initialize(fireDir, transform.root, spawnPos);
        
        PlayFireSound();
    }
}
```

### 2. Create InventoryItem ScriptableObject
- Right-click > Create > Items > Item
- Configure itemPrefab with Railgun script

### 3. Add equipped prefab to scene as pickupable
- Same process as BallLightning setup

---

## Troubleshooting

**Weapon doesn't fire:**
- Magazine component missing? (auto-created, check Weapon.OnEnable())
- Fire rate cooldown? Check IsReadyToFire property
- Ammo empty? Magazine.IsEmpty returns false?
- AudioManager missing? Script logs warning, still fires

**Weapon takes inventory slot:**
- Magazine should NOT be in inventory slots
- Only Weapon InventoryItem goes in slot
- Magazine is component on equipped weapon

**Can't pick up weapon:**
- PickupableItem.item ScriptableObject assigned?
- NetworkObject on pickup prefab?
- Player has inventory space?
- Player Interactor component present?

---

## Summary

**Weapons in this system:**
- ✅ Extend UsableItem for inventory compatibility
- ✅ Take only 1 inventory slot (Magazine is internal)
- ✅ Magazine attached as component (persists ammo across equip/unequip)
- ✅ Modular architecture (extend Weapon class for custom firing)
- ✅ Support Netcode multiplayer (NetworkObject-aware)
- ✅ Integrate with AudioManager for sound
