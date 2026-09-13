---
system: WeaponSystem
layer: characters
summary: Superseded by Combat.md; kept only to correct stale claims about weapons, audio and damage.
paths:
  - Assets/Game/Scripts/Weapons/
symptoms:
  - "looking for the weapon system doc"
  - "where are Weapon.cs and Magazine.cs now"
  - "a doc says Fire() plays the sound or damage goes straight to HealthComponent"
reads_with: [Combat, Artifacts, Multiplayer]
redirect_to: Combat
updated: 2026-09-01
---

# Weapon System — superseded

Merged into **[Combat.md](Combat.md)**. Read that instead: it covers the
`Weapon`/`Magazine`/`Projectile` hierarchy, the damage pipeline, the server-vs-present authority
split, network prefab rules, death and ragdolls.

Corrections to what this file used to claim, in case it is quoted somewhere:

- Files moved. `Weapons/Core/Weapon.cs`, `Weapons/Core/Magazine.cs`, `Weapons/Firearms/*`,
  `Weapons/Projectiles/*`, `Weapons/BallLightning/*`.
- There is no `OnFireRateReady` event on `Weapon`.
- `AudioManager` is gone. Audio goes through `SpaceGame.Audio.Sfx` with a compiler-checked `SfxId`.
- The old "Fire() plays the sound" recipe is wrong: the report belongs to `Present()`, which runs on
  every machine. `Fire()` on a non-authority machine must not deal damage.
- Damage is never applied straight to a `HealthComponent`. Everything goes through `NetDamage.Apply`.
