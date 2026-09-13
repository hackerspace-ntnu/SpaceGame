---
system: WeaponCharging
layer: characters
summary: Superseded by Combat.md; retains the two-press charge model, its replication and save limits.
paths:
  - Assets/Game/Scripts/Weapons/Projectiles/IChargeable.cs
symptoms:
  - "looking for the weapon charging doc"
  - "the charging projectile is invisible to other players"
  - "a charged shot is lost after loading a save, but the ammo stays spent"
reads_with: [Combat, Artifacts]
redirect_to: Combat
updated: 2026-09-01
---

# Weapon Charging — superseded

Merged into **[Combat.md](Combat.md)**. Charging lives on
[Weapon.cs](Assets/Game/Scripts/Weapons/Core/Weapon.cs) (`enableCharging`, `chargeDuration`,
`chargeProgressCurve`) and [IChargeable.cs](Assets/Game/Scripts/Weapons/Projectiles/IChargeable.cs);
the only implementation is
[BallLightningProjectile.cs](Assets/Game/Scripts/Weapons/Projectiles/BallLightningProjectile.cs).

Details that do not fit in Combat.md:

- **Two-press model.** Press 1 consumes ammo, calls `StartCharging()` → `SpawnChargeProjectile()`
  (which the weapon subclass overrides, *not* `Fire()`) and plays `chargeStartSoundId`. `Update()`
  evaluates `chargeProgressCurve` and pushes `UpdateCharge(progress)` each frame. Press 2 calls
  `OnChargeComplete()` then `Fire()` and starts the fire-rate cooldown.
- **The projectile owns the look, the weapon owns the clock.** `chargeMinScale`/`chargeMaxScale`/
  `chargeScaleCurve` are the projectile's; movement stays frozen until `isChargeComplete`.
- **Charging is not replicated.** `Weapon.Present()` returns early when `enableCharging` is set, so
  peers hear a charged shot but never draw one. Replicating it means replicating the charge itself.
- **Charging is not saved.** `RestoreItemState` calls `CancelCharging()`; the round already spent
  stays spent. Unequipping mid-charge also cancels via `OnDisable`.
