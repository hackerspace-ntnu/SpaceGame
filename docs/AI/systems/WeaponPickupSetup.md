---
system: WeaponPickupSetup
layer: items
summary: Superseded by Combat.md and Artifacts.md; keeps the current reference-weapon asset paths only.
paths:
  - Assets/Game/Prefabs/Items/Artifacts/
symptoms:
  - "looking for the weapon pickup setup doc"
  - "which weapon prefabs must be registered as network prefabs and which must not"
  - "ammo resets on every equip and I expected the Magazine to remember it"
reads_with: [Combat, Artifacts, Multiplayer]
redirect_to: Combat
updated: 2026-09-01
---

# Weapon Pickup Setup — superseded

Merged into **[Combat.md](Combat.md)** (see *Extending*) and
[Artifacts.md](Artifacts.md) / the
[spacegame-artifact](.claude/skills/spacegame-artifact/SKILL.md) skill, which own item prefab wiring.

Current asset locations for the reference weapon (the old paths in this file were all stale):

| Thing | Path |
| --- | --- |
| Item asset | [BallLightningWeapon.asset](Assets/Game/Resources/Items/Artifacts/BallLightningWeapon.asset) |
| Equipped prefab | [CixinGunEquipped.prefab](Assets/Game/Prefabs/Items/Artifacts/Guns/CixinGunEquipped.prefab) — **registered** as a network prefab |
| World pickup | [BallLightningWeapon_Pickup.prefab](Assets/Game/Prefabs/Items/Artifacts/Pickups/BallLightningWeapon_Pickup.prefab) — **registered** |
| Visual model | [CixinGunFinal.prefab](Assets/Game/Prefabs/Items/Artifacts/Guns/CixinGunFinal.prefab) — not registered |
| Projectile | [BallLightningProjectile.prefab](Assets/Game/Prefabs/Items/Artifacts/ArtifactResources/BallLightningProjectile.prefab) — **must not** be registered |

Also corrected: `Magazine` does **not** persist ammo by living on the equipped instance — the weapon
is destroyed and rebuilt from its prefab on every equip and `OnEnable` refills it. Ammo survives via
`Weapon.CaptureItemState`/`RestoreItemState`. There is no `AudioManager` to wire up.
