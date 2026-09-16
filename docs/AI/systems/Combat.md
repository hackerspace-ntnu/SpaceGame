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
  - "a creature built from several meshes blows apart when it dies, while single-mesh ones fall fine"
  - "every limb of the ragdoll is jointed straight to one hub instead of down the limb"
  - "parts of the model stay hanging in the air while the rest of the corpse falls"
  - "the ragdoll is only the creature's neck, or a single body, and the rest of the animal is missing"
  - "the ragdoll audit reports unfiltered: 0 on a body that visibly tears itself apart"
  - "loot drops or the enrage fires again every time I load the world"
  - "the corpse stays suspended in the air with its brain switched off"
  - "pressing respawn does nothing and the console shows MissingReferenceException from RagdollRig.Recover"
  - "the turret or NPC aims its weapon at the host's camera"
  - "the gun fires at the ground, or at the vehicle, while its holder is mounted"
  - "the projectile works on the host but never appears for clients"
  - "a blast bills a creature once per limb inside its radius, so a body dies instantly"
  - "the ball lightning orb drifts through creatures without ever hurting them"
  - "the orb discharges on the host and on a client at slightly different moments"
  - "a charged shot is audible on other machines but no orb is ever drawn"
  - "a watching machine's magazine empties twice as fast as the shooter's"
  - "firing a gun near wildlife or a guard provokes no reaction at all"
  - "my worn gear is flung off my body when I die"
  - "the backpack's flaps move on their own after a death"
reads_with: [Artifacts, AgentSystem, Inventory, Persistence]
updated: 2026-09-16
---

# Combat

Health, damage, weapons, projectiles, death and ragdolls: one server-decided damage pipeline ([NetDamage.cs](Assets/Game/Scripts/Gameplay/Health/NetDamage.cs)) that every weapon, artifact, creature and hazard funnels through, plus a runtime-derived ragdoll built from skinning weights.

**Scope:** `Assets/Game/Scripts/Gameplay/Health/`, `Gameplay/Ragdoll/`, `Weapons/` (Core, Firearms, Projectiles, BallLightning), `agents/Weapons/`, `agents/Modules/Combat/`, `Characters/Player/Combat/`, `Presentation/UI/World/`.
**Related:** [Artifacts.md](Artifacts.md) (gadget weapons), [AgentSystem.md](AgentSystem.md) (AI shooters), [Inventory.md](Inventory.md) (equip/hotbar), [Persistence.md](Persistence.md), skills [spacegame-multiplayer](.claude/skills/spacegame-multiplayer/SKILL.md), [spacegame-artifact](.claude/skills/spacegame-artifact/SKILL.md).

## Model

- **One entry point.** Nothing calls `HealthComponent.Damage` directly except the pipeline. Callers use `NetDamage.Apply(target, amount, source)`: it walks `GetComponentInParent<HealthComponent>`, applies locally when `Network.Simulates(health)`, otherwise sends `NetMsg.Damage` to the server. A target with only an `IDamageable` (destructible props) is hit locally with no message.
- **Authority split is `Use()` vs `Present()`.** `Weapon` extends `UsableItem` and keeps the default `UseAuthority.Server`. `Use()` runs on the server only and sets `ShotDealsDamage = true`; `Present()` runs on every machine, plays the report, mirrors the local magazine, and re-fires with `ShotDealsDamage = false`.
- **Exactly one copy of a shot bills the target.** Every peer instantiates its own bullet. `Projectile.Cosmetic` / `AgentProjectile.Cosmetic` suppress the `NetDamage` call on the non-deciding copies; impact VFX and sound deliberately run on all of them.
- **Aim travels, it is never recomputed.** `Weapon.OnRequestUse` stamps `arg.P` (spawn point) and `arg.R` (look rotation) from the owner's own aim. `GetAimPoint`/`GetFireDirection` prefer `UseArg` and only fall back to the local one. That local answer comes from the holder's [`AimProvider`](Assets/Game/Scripts/Characters/Player/Combat/AimProvider.cs) — `Camera.main` is the *host's* camera on a server, and is not the mount's orbit camera either (that one is deliberately left `Untagged`), so the `aimCamera`/`Camera.main` path is now the fallback for a weapon with no player behind it. Range is `aimRange` (500 m), serialized, and used by all three of the aim paths.
- **Health replicates by assignment, not delta.** `NetworkedHealthComponent` holds a `NetworkVariable<int>` (read Everyone / write Server — Owner permission published server-owned creatures to nobody) and clients apply it via `RestoreHealth`, which is the "this value is now the truth" path.
- **Death is `HealthComponent.OnDeath`**, raised both by a killing blow and by a save restoring a lethal value. `IsRestoring` tells them apart: state must be re-applied, consequences (loot, death sound, despawn timer, ragdoll impulse) must not repeat.
- **Ragdolls are derived, never authored.** `CharacterJoint` appears nowhere on disk; [RagdollSkeleton.cs](Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs) picks bones by the share of the creature each one carries, so one implementation covers all ten rigs.
- **Bodies go on the RIG, never on the meshes hanging off it.** Both kinds of model here have one — a skinned character binds its surface to a skeleton, a hard-surface creature parents rigid pieces onto one — and only the rig knows where a limb bends. Meshes are leaves, so a skeleton built on them can find no parent to joint to and collapses into a star around one hub. `SelectRigNodes` picks the articulating nodes; `NearestRigNode` says which of them carries each piece of geometry.
- **Importance is a volume, not a vertex count.** A bone's share of its renderer's weight, scaled by that renderer's own bounds (`CarriedVolume`) — the one unit that ranks a skinned bone and a bolted-on rigid part together, and the only one that survives a model built from several meshes at different densities.

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
| `RagdollSkeleton` | [RagdollSkeleton.cs](Assets/Game/Scripts/Gameplay/Ragdoll/RagdollSkeleton.cs) | Pure math: `SelectRigNodes`, `NearestRigNode`, `SubtreeBulk`, `CarriedVolume`, `SelectBones`, settle test. Unit-testable |
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
3. **Every machine `Present()`** plays the fire Sfx and, on non-authority machines, consumes its own magazine round and re-runs the shot with `ShotDealsDamage = false`. **A charging weapon is mirrored press by press**, not skipped: the owner reports which press it is in `NetArg.B` (`Weapon.PhaseForPress` — `PhaseShot` / `PhaseChargeStart` / `PhaseChargeLaunch`, the same field `GrapplingHookArtifact` uses for Attach/Release), and a watcher reads it with `Weapon.IsLaunchPress`, falling back to mirroring its own alternation when nobody filled it in (an NPC firing through `EntityEquipmentController` never runs `OnRequestUse`). The launch itself is `Weapon.LaunchChargedProjectile`, shared by `TryFire` and `Present` so the shot cannot look different on the two sides; `ReportGunshot` stays outside it, because a noise the world reacts to belongs to the authority alone. Ammo comes off on the press that **commits** the round — the charge press — not on both.
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

- **A charging weapon has to be replicated press by press, and `ShotDealsDamage` must be down before the orb is spawned.** `Present()` used to return early for anything with `enableCharging`, on the grounds that "a peer never saw press 1" — so ball lightning, the only charging weapon in the game, was a bang with no orb on every machine except the shooter's. The orb is spawned by the subclass, which stamps `projectile.Cosmetic` from `ShotDealsDamage` at that moment, so a watcher that set the flag after calling `StartCharging` would draw an orb that deals damage on every machine in the session. Two recovery paths matter and both are cheap: a launch press with nothing charged (a late joiner, a dropped message) draws nothing and clears the state rather than flashing an orb, and a charge press while already charging cancels the stale one instead of stranding it on the barrel for the session.

- **The ragdoll must not build itself out of the body's GEAR, and it used to.** `RagdollRig.FlattenRig` walks every descendant and `RagdollSkeleton.SelectRigNodes` takes each node that draws nothing but has geometry beneath it — which is exactly the shape of a worn item's root. Measured on a player wearing the jetpack with the pack shouldered, **nine of the fourteen structural candidates were gear**: `Jetpack`, its `Model` and `WornModel`, and the pack's `PIVOT_Back`/`PIVOT_Leaf`/`PIVOT_Lid`/`PIVOT_Wing_L`/`PIVOT_Wing_R` flap hinges. On death those get a `Rigidbody` and a `CharacterJoint` and are simulated as limbs, so the jetpack is flung off the body and the pack's flaps are driven by physics — reported as gear that vanished and a backpack whose parts had moved. Anything the body systems attach is now marked [`BodyAttachment`](Assets/Game/Scripts/Items/Equipped/BodyAttachment.cs) and its subtree is skipped. **Both skinned passes need the same guard, not just the flatten:** they reach for renderers with `GetComponentsInChildren` rather than through the flattened hierarchy, and `Select` takes its candidates straight from the importance map with no rig-node filter — so a worn wingsuit's own bones would still have been selectable. Gear excluded here is not lost: it stays parented to the bone it hangs off and rides it, exactly as it did while the body lived. Note this pass contributes little else on a skinned character — its real bones come from `renderer.bones` — so what the structural rule was mostly finding WAS the gear.
- **Both overlays invisible for everyone, no errors → the `WorldOverlay` Canvas itself is disabled.** The components run, labels are created and positioned, nothing renders. Historic cause: menu screens hid every canvas in the game and the launch path never restored the `DontDestroyOnLoad` ones — see the [UI](UI.md) gotcha on canvas scoping. Diagnose by reading `Canvas.enabled` on the WorldOverlay object at runtime before suspecting the damage signals.
- **A shot is a gameplay event, not just a sound — and it reaches AI through `Noise`, not the damage pipeline.** A miss damages nothing, so `HealthComponent` never fires and no listener would ever learn a gun went off. `Weapon.ReportGunshot` emits `NoiseType.Gunshot` from `TryFire`, **after** a round has actually left: not when a charge *starts* (nothing is in the air yet), and not from `Present()`. That placement is what keeps it authority-only without a check of its own — `TryFire` is reached from `Use()` and nowhere else, while `Present()` calls `Fire()` directly. It has to stay that way: a creature only ticks on the machine that owns it, so a noise emitted on a peer is heard by a copy that cannot act on it while the copy that can hears nothing. The agent-side guns do the same behind `authority.SimulatedHere` (`AgentRangedCombatModule.FireOne`, `TurretModule.Fire`, `RocketLauncherTurret.Fire`). Tune with `Weapon.gunshotNoiseRadius` / `AgentWeaponDefinition.gunshotNoiseRadius`; 0 is silent to AI and still audible to players. Who listens is [AgentSystem](AgentSystem.md).
- **Damage multiplied by player count.** The classic symptom of a missing `Cosmetic`/`ShotDealsDamage` gate, or a scene prop that damages from every machine's copy. Gate on the **victim's** authority (`Network.Simulates(health)`), not the prop's — scenery has no `NetworkObject`.
- **Projectiles must NOT be in the network prefab list.** Verified: `CixinGunEquipped.prefab` and `BallLightningWeapon_Pickup.prefab` are registered in [DefaultNetworkPrefabs.asset](Assets/Game/ScriptableObjects/Networking/DefaultNetworkPrefabs.asset); `BallLightningProjectile.prefab` and `CixinGunFinal.prefab` are not, and must not be. Only what `GameServices.World.Spawn` is handed belongs there. The root-level `Assets/DefaultNetworkPrefabs.asset` regenerates itself and is **not** the list used.
- **Missing registration fails on clients only** — the host instantiates its own copy and never consults the list, so solo playtesting cannot find it.
- **`private void OnEnable` on a `Weapon` subclass hides the base** and Unity calls only the subclass: the magazine is never resolved or refilled, `CanUse()` returns false, and the gun silently fires nothing. Same trap with `new` instead of `override` on `GetSpawnPosition` — base-class callers keep the base answer.
- **Ragdoll self-collision must stay off.** Colliders are *estimated* from bone length, and sibling limbs (two thighs, both jointed to the hips and not to each other) necessarily interpenetrate — measured at 15 cm on the Nomad. That is what a jittering ragdoll is.
- **The filter must cover every collider a body owns, not one per bone.** Adding a `Rigidbody` makes PhysX adopt everything beneath the transform, so a model with a hand-authored collision proxy hands its whole hull to the ragdoll whether or not anyone asked — CrabWalker6 carries twenty-two `COL_*` boxes. An authored hull overlaps itself the way every hull does, and outside the filter that is twenty-two contacts the solver fights every tick and can never win. `RagdollRig.OwnedCollider` records them; `Diagnose Wired Prefabs` had the identical blind spot (`GetComponent<Collider>()`, the body's own object) and reported `unfiltered: 0` on a body tearing itself apart.
- **The bone a rig's branches meet at usually carries nothing, so it gets no body.** The ostrich's legs, spine and neck all hang off a `Root` with no mesh of its own — it scores no bulk and misses the weight floor. Giving it one anyway is worse than not: it would be the root of the whole chain at `minBoneMass`, 0.6 kg holding a 57 kg spine, the very ratio `minBoneMass` exists to prevent. Instead a branch with no simulated ancestor is jointed to the ragdoll's **root bone** — a leg jointed to the torso, which is what a hand-built ragdoll does anyway.
- **The root bone is the heaviest BRANCH, not the heaviest bone.** `Select` orders shallowest-first (so the joint pass finds parents already built) and breaks ties on `RagdollSkeleton.SubtreeBulk`. Its own bulk is the wrong tiebreak: a thigh outweighs a chest, and PatrolRobot 1 came out rooted at its right leg with the left leg jointed to it and the whole upper body hanging off the pair. Getting this wrong is silent — the body still holds together, it just hangs from the wrong end.
- **A held weapon's colliders are not the hand's.** A sword and a gun sit under the robots' right arm, and a bone that counts them thinks it brought a shape and skips synthesising the one it needed — four robots ended up with a 14 kg hand carrying no collision of its own. They still have to be adopted for *filtering* (PhysX attaches them to that body regardless); they just are not the bone's shape. The test is whether anything is **drawn at or below** the collider: a prop leads to a renderer, a hand-authored hull like the crab's `COL_*` draws nothing anywhere under it. On the collider's own GameObject is not enough — the sword's collider sits on a bare object whose mesh hangs one level down.
- **Recovery restores each collider, it does not switch the body's `detectCollisions` off.** A bone that inherited an authored proxy is holding the creature's own collision — how it blocks and how it is hit — and taking that down with the ragdoll leaves a creature that stood up and can no longer be touched. Everything the rig *created* was born disabled and goes back to disabled, so a purely skinned body is left exactly as it was.
- **A bone can be destroyed out from under the rig.** The skeleton is built over transforms `RagdollRig` does not own, and a body wears things that come and go — a gauntlet stripped, a backpack swapped, a held item unequipped, each an `Instantiate` onto a bone and a `Destroy` later. `Build` takes any node under the root that carries geometry, so gear worn at the moment of the first limp can end up holding bodies. Reading one of those transforms afterwards throws a `MissingReferenceException`, and from `Recover` that exception escapes through `HealthComponent`'s revive event: the rest of the revive never runs and the player is left dead with their controls never handed back — seen as a respawn button that does nothing on a world entered dead. `DropLostBones` forgets a bone whose transform or body is gone, at the top of `GoLimp` and `Recover`. Anything new that iterates `bones` must tolerate the same loss.
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
