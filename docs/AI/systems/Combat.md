---
system: Combat
layer: characters
summary: Health, damage, weapons, projectiles, death and ragdolls through one server-decided damage pipeline
paths:
  - Assets/Game/Scripts/Gameplay/Health/
  - Assets/Game/Scripts/Weapons/
  - Assets/Game/Scripts/Gameplay/Ragdoll/
  - Assets/Game/Scripts/agents/Weapons/
  - Assets/Game/Scripts/Presentation/UI/World/
symptoms:
  - "damage lands once per player in the session, so a target dies four times too fast"
  - "the gun fires but no bullet appears and the ammo never goes down"
  - "a client sees no damage numbers at all, or its own shots do nothing"
  - "damage numbers and nameplates exist in code but nothing ever shows in game, for host and clients alike"
  - "the ragdoll jitters and vibrates instead of falling limp"
  - "loot drops or the enrage fires again every time I load the world"
  - "the corpse stays suspended in the air with its brain switched off"
  - "the turret or NPC aims its weapon at the host's camera"
  - "the projectile works on the host but never appears for clients"
  - "a blast bills a creature once per limb inside its radius, so a body dies instantly"
  - "the ball lightning orb drifts through creatures without ever hurting them"
  - "the orb discharges on the host and on a client at slightly different moments"
  - "firing a gun near wildlife or a guard provokes no reaction at all"
reads_with: [Artifacts, AgentSystem, Inventory, Persistence]
updated: 2026-09-03
---

# Combat

Health, damage, weapons, projectiles, death and ragdolls: one server-decided damage pipeline ([NetDamage.cs](Assets/Game/Scripts/Gameplay/Health/NetDamage.cs)) that every weapon, artifact, creature and hazard funnels through, plus a runtime-derived ragdoll built from skinning weights.

**Scope:** `Assets/Game/Scripts/Gameplay/Health/`, `Gameplay/Ragdoll/`, `Weapons/` (Core, Firearms, Projectiles, BallLightning), `agents/Weapons/`, `agents/Modules/Combat/`, `Characters/Player/Combat/`, `Presentation/UI/World/`.
**Related:** [Artifacts.md](Artifacts.md) (gadget weapons), [AgentSystem.md](AgentSystem.md) (AI shooters), [Inventory.md](Inventory.md) (equip/hotbar), [Persistence.md](Persistence.md), skills [spacegame-multiplayer](.claude/skills/spacegame-multiplayer/SKILL.md), [spacegame-artifact](.claude/skills/spacegame-artifact/SKILL.md).

## Model

- **One entry point.** Nothing calls `HealthComponent.Damage` directly except the pipeline. Callers use `NetDamage.Apply(target, amount, source)`: it walks `GetComponentInParent<HealthComponent>`, applies locally when `Network.Simulates(health)`, otherwise sends `NetMsg.Damage` to the server. A target with only an `IDamageable` (destructible props) is hit locally with no message.
- **Authority split is `Use()` vs `Present()`.** `Weapon` extends `UsableItem` and keeps the default `UseAuthority.Server`. `Use()` runs on the server only and sets `ShotDealsDamage = true`; `Present()` runs on every machine, plays the report, mirrors the local magazine, and re-fires with `ShotDealsDamage = false`.
- **Exactly one copy of a shot bills the target.** Every peer instantiates its own bullet. `Projectile.Cosmetic` / `AgentProjectile.Cosmetic` suppress the `NetDamage` call on the non-deciding copies; impact VFX and sound deliberately run on all of them.
- **Aim travels, it is never recomputed.** `Weapon.OnRequestUse` stamps `arg.P` (spawn point) and `arg.R` (look rotation) from the owner's *local* camera. `GetAimPoint`/`GetFireDirection` prefer `UseArg` and only fall back to the local camera. `Camera.main` on the server is the host's camera.
- **Health replicates by assignment, not delta.** `NetworkedHealthComponent` holds a `NetworkVariable<int>` (read Everyone / write Server — Owner permission published server-owned creatures to nobody) and clients apply it via `RestoreHealth`, which is the "this value is now the truth" path.
- **Death is `HealthComponent.OnDeath`**, raised both by a killing blow and by a save restoring a lethal value. `IsRestoring` tells them apart: state must be re-applied, consequences (loot, death sound, despawn timer, ragdoll impulse) must not repeat.
- **Ragdolls are derived, never authored.** `CharacterJoint` appears nowhere on disk; [RagdollSkeleton.cs](Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs) picks bones by share of mesh vertex weight, so one implementation covers all ten rigs.

## Key types

| Type | File | Role |
| --- | --- | --- |
| `HealthComponent` | [HealthComponent.cs](Assets/Game/Scripts/Gameplay/Health/HealthComponent.cs) | The value + `OnDamage/OnHeal/OnDeath/OnRevive/OnRestored`, `LastDamageSource`, `IsRestoring`, static `AnyDamaged` |
| `IDamageable` | [IDamageable.cs](Assets/Game/Scripts/Gameplay/Health/IDamageable.cs) | `Damage(int)` + `Alive` for things with no HealthComponent |
| `NetDamage` | [NetDamage.cs](Assets/Game/Scripts/Gameplay/Health/NetDamage.cs) | Static `Apply` — the only sanctioned way to hurt anything |
| `RadiusDamage` | [RadiusDamage.cs](Assets/Game/Scripts/Gameplay/Health/RadiusDamage.cs) | Blasts: `Collect`/`Apply` over a sphere, deduplicated to one bill per body |
| `NetworkedHealthComponent` | [NetworkedHealthComponent.cs](Assets/Game/Scripts/Gameplay/Health/NetworkedHealthComponent.cs) | Replication + `NetMsg.Damage` handler + static `DamageAnnounced` |
| `DamageFeedback` | [DamageFeedback.cs](Assets/Game/Scripts/Gameplay/Health/DamageFeedback.cs) | Camera shake + hurt Sfx off local `OnDamage` |
| `HealthSaveable` | [HealthSaveable.cs](Assets/Game/Scripts/Core/Persistence/Adapters/HealthSaveable.cs) | Persists current HP; `max` stored but never applied |
| `HealthReactionModule` | [HealthReactionModule.cs](Assets/Game/Scripts/agents/Entity/HealthReactionModule.cs) | Threshold latches, death anim/noise/despawn, `disableAgentOnDeath` |
| `Weapon` | [Weapon.cs](Assets/Game/Scripts/Weapons/Core/Weapon.cs) | `UsableItem` base: ammo, fire rate, aim, charging, `ShotDealsDamage`, item-state capture |
| `Magazine` | [Magazine.cs](Assets/Game/Scripts/Weapons/Core/Magazine.cs) | Per-weapon ammo container; auto-added by `Weapon.OnEnable` |
| `Projectile` | [Projectile.cs](Assets/Game/Scripts/Weapons/Projectiles/Projectile.cs) | Abstract: `Initialize`, `HandleHit`, `OnImpact`, `Cosmetic`, `CrossPortal` |
| `IChargeable` | [IChargeable.cs](Assets/Game/Scripts/Weapons/Projectiles/IChargeable.cs) | Two-press charge contract implemented by the projectile |
| `AgentProjectile` | [AgentProjectile.cs](Assets/Game/Scripts/agents/Weapons/AgentProjectile.cs) | Rigidbody bullet for NPCs; friendly-fire filter via `EntityFaction` |
| `AgentWeaponDefinition` / `AgentFireProfile` / `AgentAimProfile` | [agents/Weapons/](Assets/Game/Scripts/agents/Weapons/) | ScriptableObjects: damage+prefab, range/cadence/burst, spread+lead |
| `RagdollRig` | [RagdollRig.cs](Assets/Game/Scripts/Gameplay/Ragdoll/RagdollRig.cs) | Builds bodies/joints on first limp; `GoLimp`/`Recover`/`Freeze`, `Drives`, `IsSettled` |
| `RagdollSkeleton` | [RagdollSkeleton.cs](Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs) | Pure math: bone selection by weight, settle test. Unit-testable |
| `RagdollBudget` | [RagdollBudget.cs](Assets/Game/Scripts/Gameplay/Ragdoll/RagdollBudget.cs) | Static per-process cap; freezes the oldest *settled* body |
| `AgentRagdoll` / `PlayerRagdoll` | [Gameplay/Ragdoll/](Assets/Game/Scripts/Gameplay/Ragdoll/) | Decide *when* to go limp and suspend the layers that own the transform |
| `DamageNumbers` / `PlayerNameplates` | [Presentation/UI/World/](Assets/Game/Scripts/Presentation/UI/World/) | Screen-space overlays hosted by `WorldOverlay` |
| `PlayerAimRig` / `AimPose` / `AimIkRelay` | [Characters/Player/Combat/](Assets/Game/Scripts/Characters/Player/Combat/) | Upper-body hold + aim layer and hand IK; runs on every machine |

## Weapons & projectiles

| Weapon/Projectile | File | Notes |
| --- | --- | --- |
| `BasicGun` | [BasicGun.cs](Assets/Game/Scripts/Weapons/Firearms/BasicGun.cs) | Spawns `BasicProjectile`; must `override` (not hide) `OnEnable`/`GetSpawnPosition` |
| `EnergyRifle` | [EnergyRifle.cs](Assets/Game/Scripts/Weapons/Firearms/EnergyRifle.cs) | Hitscan, `raysPerShot`/spread/dropoff; damage gated on `ShotDealsDamage` |
| `BallLightningWeapon` | [BallLightningWeapon.cs](Assets/Game/Scripts/Weapons/BallLightning/BallLightningWeapon.cs) | The only charging weapon: press 1 spawns + charges, press 2 launches |
| `BasicProjectile` | [BasicProjectile.cs](Assets/Game/Scripts/Weapons/Projectiles/BasicProjectile.cs) | Straight line, interval raycast from `lastPosition`, portal-aware |
| `BallLightningProjectile` | [BallLightningProjectile.cs](Assets/Game/Scripts/Weapons/Projectiles/BallLightningProjectile.cs) | Perlin wander + hover + dynamic light; implements `IChargeable`; drives the discharge once launched |
| `BallLightningController` / `BoltTargeting` | [Weapons/BallLightning/](Assets/Game/Scripts/Weapons/BallLightning/) | Pure VFX: shader `iTime/iResolution/iMouse` and a cone-scan bolt target. `BoltTargeting.StrikeAt` lends that one bolt to a caller for a frame |
| `BallLightningDischarge` | [BallLightningDischarge.cs](Assets/Game/Scripts/Weapons/BallLightning/BallLightningDischarge.cs) | The orb's gimmick: sweeps for damageable bodies, bills all of them at once for 100, arcs, then ends the projectile |
| `BallLightningFlash` | [BallLightningFlash.cs](Assets/Game/Scripts/Weapons/BallLightning/BallLightningFlash.cs) | Unparented point light that fades itself out and self-destructs; outlives the orb that cast it |
| `BallLightningProjectileOld` | [BallLightningProjectileOld.cs](Assets/Game/Scripts/Weapons/BallLightning/BallLightningProjectileOld.cs) | Dead stub kept as a signpost. Do not use |
| NPC ranged | [AgentRangedCombatModule.cs](Assets/Game/Scripts/agents/Modules/Combat/AgentRangedCombatModule.cs) | `FireOne` decides, `PresentShot` draws; broadcast as `NetMsg.AgentActed` |
| NPC melee / turrets | [CloseCombatModule.cs](Assets/Game/Scripts/agents/Modules/Combat/CloseCombatModule.cs), [TurretModule.cs](Assets/Game/Scripts/agents/Modules/Combat/TurretModule.cs), [TurretProjectile.cs](Assets/Game/Scripts/agents/Modules/Combat/TurretProjectile.cs) | Same Use/Present split; `WeaponSelector` shows the model |
| Gadget weapons | [Items/Artifacts/Gadgets/](Assets/Game/Scripts/Items/Artifacts/Gadgets/) | `LaserStaffArtifact`, `GravelBlasterArtifact`, `DragonRocket`, `LightningSpell`, `RepulsorGauntletArtifact`, `SuckerPuncherArtifact`, `BlastPush` — see [Artifacts.md](Artifacts.md) |
| Hazards | [Cactus.cs](Assets/Game/Scripts/World/Environment/Props/Cactus.cs), [SandstormVictim.cs](Assets/Game/Scripts/World/Environment/Sandstorm/Effects/SandstormVictim.cs) | Scene props exist on every machine — both gate on the **victim's** authority |

## Flows

1. **Owner presses Use.** `UsableItem.TryUse` → `Weapon.OnRequestUse(ref arg)` stamps `arg.P`/`arg.R` from `GetLocalFireDirection()`. Request goes to the server.
2. **Server `Use()`** sets `ShotDealsDamage = true` and calls `TryFire()` → ammo check on the server magazine → `Fire()`.
3. **Every machine `Present()`** plays the fire Sfx; non-authority machines also consume their own magazine round and re-run `Fire()` with `ShotDealsDamage = false` (skipped entirely for charging weapons — peers never saw press 1).
4. **Hit.** Hitscan raycasts in `FireRay`; projectiles raycast `lastPosition → position` in `CheckCollision` (`IsOwnerHit` rejects the shooter's own root). Only the non-`Cosmetic` copy calls `NetDamage.Apply(collider.gameObject, damage, ownerRoot)` — the source is the **shooter**, not the bullet, because `LastDamageSource` feeds faction/provocation lookups.
5. **Damage.** Server `HealthComponent.Damage` → `OnDamage` → static `AnyDamaged` (before the death check, so a killing blow still shows a number) → `SyncHealth` writes the NetworkVariable → `AnnounceDamage` sends `NetMsg.Damaged` **to others only** when the source resolves to a `PlayerIdentity`.
6. **Death.** `currentHealth <= 0` → `OnDeath`. `HealthReactionModule` plays the death sound/noise, fires `onDeath`, disables `AgentController`, schedules despawn; `EntityLootTable` drops loot; `MatchManager` credits the kill from `LastDamageSource` and schedules a respawn (`ResetToFull`, never `Heal`); `PlayerController.OnDeath` sets `IsDead` and shows the death screen.
7. **Ragdoll.** `AgentRagdoll`/`PlayerRagdoll` also hear `OnDeath` on **every** machine (no message needed) — they suspend the transform owners (`AgentController`, `ISelfDrivingMotor`, `LeggedLocomotion`, `PlayerMovement`/`PlayerLook`, root Rigidbody + collider) and call `rig.GoLimp(impulse)`. `RagdollRig` builds the skeleton on the *first* limp, registers with `RagdollBudget`, and either drags the root after the hips (`Drives`) or pins the hips to the replicated root.
8. **Recovery** (knockdown only): settle test or `maxLimpSeconds` ceiling → `Recover()` returns a `TeleportMove` raised as `ITeleportAware` so legged locomotion rebases instead of teleporting the creature back to where it fell. Control returns at the *start* of the blend.

**Ball lightning discharges instead of colliding.** The orb is a proximity weapon: `BallLightningProjectile.Update` ticks `BallLightningDischarge` once `isLaunched`, which sweeps `RadiusDamage.Collect` every `scanInterval`. The first sweep that finds anything damageable bills *every* body it found for 100 in the same instant, snapshots their positions, and switches the orb to arcing — it stops moving, whips the shader's single direct bolt between those points via `BoltTargeting.StrikeAt`, throws an unparented `BallLightningFlash`, plays `SfxId.WeaponBallLightningArc`, and after `arcDuration` reports `Spent`, at which point the projectile destroys itself. The owner root is passed as the sweep's `exclude`, so the orb cannot kill whoever fired it. `Projectile.HandleHit` is still the path for running into a wall.

## Multiplayer

| Concern | Who |
| --- | --- |
| Deciding a hit | Server (or offline "host of one"). `Network.Simulates(health)` |
| Player transform / player ragdoll | The **owner**, not the server (`PlayerRagdoll`) |
| Creature transform / creature ragdoll | Server (`AgentRagdoll.Drives = Network.Simulates(this)`) |
| Drawing bullets, tracers, impacts, sounds | Every machine |

Messages: `NetMsg.Damage` (10, → server on the *target's* relay, `A` = amount, Target = source), `NetMsg.Damaged` (11, server → peers on the *victim's* relay, Target = attacking player), `NetMsg.Knockdown` (82, server → everyone, `P` = impulse, `A` = downed ms). All in [NetMsg.cs](Assets/Game/Scripts/Core/Multiplayer/Messaging/NetMsg.cs).

**Why both `AnyDamaged` and `DamageAnnounced` are needed** — see [DamageNumbers.cs](Assets/Game/Scripts/Presentation/UI/World/DamageNumbers.cs). `HealthComponent.AnyDamaged` fires on the machine that *decided* the hit and needs nothing replicated, so it covers crates, test cubes and un-networked creatures. `DamageAnnounced` covers the case that signal cannot: a client's own shot is resolved on the server, so without it a client sees no numbers at all. They never double up — `AnnounceDamage` uses `NetToOthers`, which excludes the machine that applied the damage.

## Persistence

| State | Where |
| --- | --- |
| Current HP (players, agents, props) | `HealthSaveable`, key `health`. Auto-attached by [SaveablePolicy.cs](Assets/Game/Scripts/Core/Persistence/Runtime/SaveablePolicy.cs) to anything with a `HealthComponent`; it covers `NetworkedHealthComponent` too |
| Death state | Implicit: HP 0 restores → `RestoreHealth` raises `OnDeath` with `IsRestoring == true` |
| Threshold latches + the modules a reaction switched | `HealthReactionSaveable` → `HealthReactionModule.RestoreThresholds` (re-applies module enable/disable *silently*, no UnityEvent) |
| Weapon ammo + cooldown | `Weapon.CaptureItemState`/`RestoreItemState` in the item's `ItemState` (`ammo`, `cd`). Cooldown stored as time **remaining** |
| NPC fire cooldowns, bursts, aim tracking | `CombatCadenceSaveable` (one saver, three module types) |
| Ragdoll pose | **Not saved.** The rig follows the hips into the transform, so `TransformSaveable` records where the corpse lies; on load it goes limp `settled: true` with zero impulse |

Ordering on load: the record lands → `RestoreHealth` clamps to the prefab's `maxHealth` → `OnRestored` (replication) then `OnDeath`/`OnRevive`. `IsRestoring` is set for the whole call and cleared in a `finally`, so a throwing listener cannot make every later death in the session look like a restore. `PlayerController` re-checks `playerHealth.Alive` on enable because an event cannot be replayed into a delegate that was empty when it fired.

## Gotchas

- **Both overlays invisible for everyone, no errors → the `WorldOverlay` Canvas itself is disabled.** The components run, labels are created and positioned, nothing renders. Historic cause: menu screens hid every canvas in the game and the launch path never restored the `DontDestroyOnLoad` ones — see the [UI](UI.md) gotcha on canvas scoping. Diagnose by reading `Canvas.enabled` on the WorldOverlay object at runtime before suspecting the damage signals.
- **A shot is a gameplay event, not just a sound — and it reaches AI through `Noise`, not the damage pipeline.** A miss damages nothing, so `HealthComponent` never fires and no listener would ever learn a gun went off. `Weapon.ReportGunshot` emits `NoiseType.Gunshot` from `TryFire`, **after** a round has actually left: not when a charge *starts* (nothing is in the air yet), and not from `Present()`. That placement is what keeps it authority-only without a check of its own — `TryFire` is reached from `Use()` and nowhere else, while `Present()` calls `Fire()` directly. It has to stay that way: a creature only ticks on the machine that owns it, so a noise emitted on a peer is heard by a copy that cannot act on it while the copy that can hears nothing. The agent-side guns do the same behind `authority.SimulatedHere` (`AgentRangedCombatModule.FireOne`, `TurretModule.Fire`, `RocketLauncherTurret.Fire`). Tune with `Weapon.gunshotNoiseRadius` / `AgentWeaponDefinition.gunshotNoiseRadius`; 0 is silent to AI and still audible to players. Who listens is [AgentSystem](AgentSystem.md).
- **Damage multiplied by player count.** The classic symptom of a missing `Cosmetic`/`ShotDealsDamage` gate, or a scene prop that damages from every machine's copy. Gate on the **victim's** authority (`Network.Simulates(health)`), not the prop's — scenery has no `NetworkObject`.
- **Projectiles must NOT be in the network prefab list.** Verified: `CixinGunEquipped.prefab` and `BallLightningWeapon_Pickup.prefab` are registered in [DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset); `BallLightningProjectile.prefab` and `CixinGunFinal.prefab` are not, and must not be. Only what `GameServices.World.Spawn` is handed belongs there. The root-level `Assets/DefaultNetworkPrefabs.asset` regenerates itself and is **not** the list used.
- **Missing registration fails on clients only** — the host instantiates its own copy and never consults the list, so solo playtesting cannot find it.
- **`private void OnEnable` on a `Weapon` subclass hides the base** and Unity calls only the subclass: the magazine is never resolved or refilled, `CanUse()` returns false, and the gun silently fires nothing. Same trap with `new` instead of `override` on `GetSpawnPosition` — base-class callers keep the base answer.
- **Ragdoll self-collision must stay off.** Colliders are *estimated* from bone length, and sibling limbs (two thighs, both jointed to the hips and not to each other) necessarily interpenetrate — measured at 15 cm on the Nomad. That is what a jittering ragdoll is.
- **Two owners of one transform.** A `NavMeshAgent` writes the transform every enabled frame and `LeggedLocomotion` rewrites it from world-space foot state every `LateUpdate`. `AgentRagdoll` resolves `ISelfDrivingMotor` lazily because caching it in `Awake` races `AgentController.Awake` — and a null motor is a body that glitches rather than falls, decided by component order on the prefab.
- **A ragdoll frozen out from under you.** `RagdollBudget` may `Freeze` a limp rig; `AgentRagdoll.Update` watches for `!rig.IsLimp` and restores, or the creature stays suspended with its brain off forever.
- **`Weapon.ExternallyAimed`** must be set when an NPC or turret holds a weapon, or `UpdateWeaponRotation` passes its ownership test on the server and swings every NPC's barrel to follow the host's head.
- **Loot/enrage replaying on every load.** Anything acting on `OnDeath` or a threshold must check `HealthComponent.IsRestoring` — state yes, announcements no.
- **A blast must deduplicate by body, not by collider.** A rig is many colliders hanging off its bones, so `OverlapSphere` returns a creature four or five times and a naive loop bills it four or five times. `RadiusDamage` resolves each collider up to the object owning the `HealthComponent` (or the `IDamageable`) and bills that once. Written out by hand this was got wrong: `LightningSpell` deduplicated `HealthComponent` bodies correctly but fell back to `collider.gameObject` for props, so a three-collider `IDamageable` crate took triple damage. Use the helper; do not hand-roll another sweep.
- **The orb has exactly one bolt, and two things want it.** `BallLightningBoltTargeting`'s idle cone-scan and `BallLightningDischarge` both drive the same shader input (`SetExternalDirectBolt`). `StrikeAt` writes immediately and stamps `Time.frameCount`; `Update` returns early when that stamp is the current frame. Queueing the override for `Update` instead would be a frame late whenever component order put `Update` first — and adding a second LineRenderer bolt, as the laser staff uses, would draw a differently shaded lightning next to the shader's own.
- **The discharge is decided per machine, so its timing diverges.** Projectiles are not networked — every peer runs its own orb, and `BallLightningProjectile.Initialize` seeds the Perlin wander from `Random`, so the copies drift apart in flight. Damage is billed only by the non-`Cosmetic` copy and is therefore correct, but each copy triggers its own arc when *its* orb comes within `dischargeRadius`, so peers see the flash a few frames apart and, if the wander has separated them far enough, potentially against a different set of bodies. Cosmetic only. Do not "fix" it by billing from every copy.
- **`DamageNumbers` static subscription.** Outside play mode Unity raises neither `OnDisable` nor `OnDestroy`, so `Bind()` explicitly evicts the previous overlay and compares with `ReferenceEquals`, never `==`.

## Extending

**A new weapon**

1. Subclass `Weapon`, implement `protected override void Fire()`. Use `GetSpawnPosition()` / `GetFireDirection()` — never a camera of your own.
2. If it spawns a projectile, pass `projectile.Cosmetic = !ShotDealsDamage` before `Initialize(dir, ownerRoot, spawnPos)` then `StartLifetime()`. If it resolves its own hits, wrap the `NetDamage.Apply` in `if (ShotDealsDamage)`.
3. Do **not** play the fire sound in `Fire()` — the base plays it in `Present()`, which runs everywhere.
4. `override` (never hide) `OnEnable`, calling `base.OnEnable()` first. Set `fireSoundId` (an `SfxId`, compiler-checked) rather than an FMOD path.
5. Author the equipped prefab under `Assets/Game/Prefabs/Items/Artifacts/` with `Magazine`, `firePoint`, `handle1`; author the `InventoryItem` under `Assets/Game/Resources/Items/` pointing at it.
6. Register the **item** prefab (and any pickup prefab) in `DefaultNetworkPrefabs.asset`. Register the projectile nowhere.
7. Verify on an actual client: shot flies along the client's crosshair, the client's own HUD ammo drops, damage numbers appear for the client, and the target's HP matches on both machines.

**A new damage source**

1. Call `NetDamage.Apply(targetGameObject, amount, sourceTransform)`. Pass the **shooter/owner root** as source — `LastDamageSource` drives kill credit, `AgentTargeting`'s last-attacker bias, provocation and the ragdoll's death impulse direction.
2. Decide who calls it. If more than one machine runs the code (a scene prop, a peer's copy of a bullet), gate on `Network.Simulates(victimHealth)` or a `Cosmetic` flag so exactly one call is made.
3. If it hurts everything in a radius, call `RadiusDamage.Apply` (one shot) or `RadiusDamage.Collect` (sweeping every frame — it fills a caller-owned list and allocates nothing) rather than writing another `OverlapSphere` loop. It resolves and deduplicates bodies for you; see the gotcha above for what hand-rolling it costs.
4. If the target has no `HealthComponent`, implement `IDamageable` on it — `NetDamage` falls through to that and applies locally.
5. Nothing else is needed for replication, damage numbers, hit sounds, death, loot, ragdoll or persistence: they all hang off `HealthComponent`'s events.
