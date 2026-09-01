# Audio in prefabs — what makes noise, and what sound it wants

> **This is a generated inventory, not the architecture doc.** For how the audio system works —
> `SfxId`, `AudioCatalog`, FMOD status, playback flows, multiplayer rules — read
> **[audio.md](audio.md)**. This file is a point-in-time sweep and may lag the code.

_Swept 2026-08-21 over all 118 prefabs under `Assets/Game/Prefabs` plus `Assets/Game/Resources/Cameras`._

Companion to [audio.md](audio.md), which documents the `SfxId` → FMOD catalog. **That** doc lists
the catalog slots and what each currently resolves to. **This** doc lists the prefabs, and for each
one the trigger that fires a sound and what that sound should be — the shopping list for an audio
pass.

## How to read it

A prefab makes noise in one of three ways:

| Way | Looks like in the YAML | Notes |
|---|---|---|
| **Catalog id** | `hurtId: 409` | Resolves through `AudioCatalog` at runtime. Retuning the catalog retunes every prefab that uses the id. |
| **Direct event** | `hurtSound: … Path: event:/SFX/Hit` | An `EventReference` pinned in the inspector. **Wins over the id.** Retuning the catalog does *nothing* for these. |
| **Component** | `AudioSource`, `StudioEventEmitter`, `StudioListener` | Plays without going through `Sfx` at all. |

`Path:` empty with `Data1: 0` means the override is unassigned and the catalog id is what plays.

Legend for the **State** column:

- **pinned** — a direct event is assigned; it overrides the catalog and must be repointed *in the prefab*, not in the catalog.
- **catalog** — the id resolves through the catalog; nothing to do in the prefab.
- **default** — the prefab predates the field, so the C# default applies. Fine, but it is not visible in the YAML.

---

## 1. Player

### `Characters/Player/PlayerCharacter.prefab`
### `Characters/Player/PlayerCharacterNetworked.prefab` — variant of the above, inherits everything

`PlayerAudioModule` + `DamageFeedback`.

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Footstep while grounded and moving | `PlayerFootstep` 100 | Boot on sand/grit, spaced by gait | catalog |
| Jump | `PlayerJump` 101 | Suit servo push-off | catalog |
| Land (soft) | `PlayerLand` 102 | Light boot scuff | catalog |
| Land (hard, above `heavyLandSpeed`) | `PlayerLandHeavy` 103 | Heavy thud + armour rattle | catalog |
| Dash | `PlayerDash` 104 | Suit thruster burst | catalog |
| Take damage | `PlayerHurt` 105 | Pained grunt / suit alarm | catalog |
| Die | `PlayerDeath` 106 | Death sting | catalog |
| Respawn | `PlayerRespawn` 107 | Reconstitution whoosh | catalog |
| Damage flash (`DamageFeedback`) | `PlayerHurt` 105 → **pinned `event:/UI/No`** | Should be a damage *impact*, not a UI beep. **Clear the pin.** | pinned |

> The pinned `event:/UI/No` on `DamageFeedback` is the most obviously wrong assignment in the project —
> and because it is a pin, fixing slot 105 in the catalog will not fix it.

---

## 2. Enemies, creatures and NPCs

### `agents/Robots/PatrolRobot.prefab` and variants `1`, `2`, `3`
### `agents/Robots/DeathmatchBot.prefab`

`EntityAudioModule` + `PerceptionModule` + `SearchModule` + `HealthReactionModule` + `CloseCombatModule` + `AgentRangedCombatModule`.

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Walk cycle (`footstepInterval` 0.45s) | `EntityFootstep` 411 | Servo-driven metal footfall | **pinned** `event:/SFX/Footstep` (all five) |
| Idle ambience (every 5–12 s) | `NpcMumbleNeutral` 400 | Machine idle chatter / servo whine | **pinned** `event:/SFX/ElectricHum` (all five) |
| Spots a target | `EntityAlert` 407 | Detection ping | **pinned** `event:/SFX/Implosion` on `1`, `2`, `3`; unassigned on `PatrolRobot`, `DeathmatchBot` |
| Loses target, starts searching | `EntitySearch` 408 | Scanning sweep, quieter | catalog (unassigned on all) |
| Aggro on target | `EntityAggro` 406 | Threat klaxon / lock-on | catalog (unassigned on all) |
| Takes damage | `EntityHurt` 409 | Metal impact + sparks | **pinned**: `Hit` on `PatrolRobot`/`DeathmatchBot`, `MetalPickup` on `2`/`3`, `PlayerDie` on `1` |
| Dies | `EntityDeath` 410 | Power-down + collapse | **pinned** `event:/SFX/PlayerDie` (all five) |
| Melee attack | `EntityAttack` 412 | Servo swing | catalog |
| Fires ranged weapon → `WPN_RobotPistol.asset` | `WeaponGunFire` 200 (default) | Robot pistol crack | **pinned** `event:/SFX/Hit` on the *ScriptableObject*, shared by all five bots |

> Every robot has five pinned events, so **the robots are the one family the catalog cannot retune.**
> The `PlayerDie` pin on `PatrolRobot 1`'s *hurt* sound is almost certainly a mistake.

### `agents/Characters/Nomad.prefab`

`PerceptionModule` + `HealthReactionModule` + `CloseCombatModule` + `DialogInteraction`. No `EntityAudioModule` → **no footsteps**.

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Spots the player | `EntityAlert` 407 | Human "hey!" / notice | catalog |
| Melee attack | `EntityAttack` 412 | Swing + effort grunt | catalog |
| Hurt | `EntityHurt` 409 | Human pain | catalog |
| Death | `EntityDeath` 410 | Human death | catalog |
| Dialog line advance (`DialogInteraction`) | `NpcMumbleFriendly` 401 | Per-character voice blip, friendly register | catalog |

### `agents/creatures/Golem.prefab`, `agents/creatures/DuneRat.prefab`, `agents/creatures/Vrescal.prefab`

Identical wiring: `PerceptionModule` + `HealthReactionModule` + `CloseCombatModule`. No `EntityAudioModule` → **no footsteps, no idle ambience**.

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Spots target | `EntityAlert` 407 | Golem: stone grind alert · DuneRat: shriek · Vrescal: hexapod chitter | catalog |
| Attack | `EntityAttack` 412 | Golem: heavy stone swing · DuneRat: bite · Vrescal: claw strike | catalog |
| Hurt | `EntityHurt` 409 | Per-creature pain | catalog |
| Death | `EntityDeath` 410 | Per-creature death | catalog |

> All three share ids with the robots, so a single catalog slot is currently doing stone, rodent,
> insect and machine. **These want per-creature pins or their own SfxId numbers.**

### `agents/Caravan/NomadOstrich.prefab`, `agents/Caravan/BountyHunter.prefab`

`ChatterModule` only.

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Idle/travel chatter | `NpcMumbleNeutral` 400 | Muttered caravan talk, long cooldown | catalog (unassigned) |

---

## 3. Weapons and projectiles

### `Items/Artifacts/Guns/Gun.prefab` — `BasicGun`

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Fire | `WeaponGunFire` 200 | Ballistic crack + mechanical cycle | **pinned** `event:/SFX/Explosion` |
| Charge start | `WeaponEnergyChargeLoop` 204 | (unused by `BasicGun`) | catalog |
| Use (base `UsableItem`) | `None` 0 | Deliberately silent — the gun makes its own noise | — |
| Pick up | `InteractPickup` 503 | Weapon pickup clack | catalog |

### `Items/Artifacts/Guns/CixinGunFinal.prefab` — `BallLightningWeapon`

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Fire | `WeaponGunFire` 200 | **Wants `WeaponBallLightningFire` 206** — the id is left on the generic gun value | catalog |
| Charging (held) | `WeaponEnergyChargeLoop` 204 | Rising electrical charge, loops | catalog |
| Use | `None` 0 | silent by design | — |

### `Items/Artifacts/Guns/CixinGunEquipped.prefab`

Pickup-only shell (`PickupableItem` + `DropItemPhysics`).

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Pick up | `InteractPickup` 503 | Weapon pickup clack | catalog |

### `Items/Artifacts/ArtifactResources/projectile.prefab`, `RocketProjectile.prefab` — `BasicProjectile`
### `Items/Artifacts/ArtifactResources/BallLightningProjectile.prefab` — `BallLightningProjectile`

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Impact on something with health | `ImpactFlesh` 300 | Wet impact | default |
| Impact on anything else | `ImpactMetal` 301 | Hard surface impact | default |

> Neither impact field is serialized in the prefabs — the C# defaults apply. `RocketProjectile`
> arguably wants `ImpactExplosion` 304 instead, which is currently unused by anything.

### `Items/Artifacts/Pickups/BallLightningWeapon_Pickup.prefab`

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Pick up | `InteractPickup` 503 | Weapon pickup clack | catalog |

---

## 4. Gadgets and items

Every one of these carries `PickupableItem` → `pickupId: 503` (`InteractPickup`, "generic item pickup"), plus a `UsableItem` use sound.

| Prefab | Use trigger | Id / pin | Sound wanted |
|---|---|---|---|
| `Items/Artifacts/Gadgets/AntiGravityPotion.prefab` | Drink | `None` 0 → **pinned `event:/SFX/Slurp`** | Gulp + antigrav hum onset |
| `Items/Artifacts/Gadgets/GrapplingHook.prefab` | Fire hook | `None` 0 → **pinned `event:/SFX/Wham`** | Launch thunk + line reel |
| `Items/Artifacts/Gadgets/Lasso.prefab` | Throw | `None` 0 → **pinned `event:/SFX/Hit`** | Rope whip + catch |
| `Items/Artifacts/Gadgets/Leash.prefab` | Attach | `None` 0 → **pinned `event:/SFX/Hit`** | Clip-on latch |
| `Items/Artifacts/Gadgets/LightningSpell.prefab` | Cast | `None` 0 → **pinned `event:/SFX/MetalPickup`** | Electric discharge — the pin is wrong |
| `Items/Artifacts/Gadgets/RocketArtifact.prefab` | Deploy turret | `None` 0 → **pinned `event:/SFX/Hit`** | Turret deploy servo |
| `Items/Artifacts/Gadgets/RuinScanner.prefab` | Scan | `None` 0 → **pinned `event:/SFX/Implosion`** | Scanner sweep |
| ” | Discovery found | `InteractScannerDiscovery` 507 → **pinned `event:/SFX/ElectricHum`** | Positive discovery chime |
| `Items/Equipment/WingPack.prefab` | Equip / deploy | `None` 0 | Wing pack unfurl — **nothing plays today** |
| `Items/Pickups/Scraps.prefab` | Pick up | `InteractPickup` 503 | Scrap metal pickup |
| ” | `StudioEventEmitter`, trigger `2` | **`event:/SFX/MetalPickup`** | Fires independently of `Sfx`; the one prefab with a raw FMOD emitter |
| `Items/Debug/Cube.prefab`, `Items/Debug/Sphere.prefab` | Pick up | `InteractPickup` 503 | Debug props — leave as is |
| `Systems/InventoryItemModule.prefab` | Pick up | `InteractPickup` 503 | Template used by spawned inventory items |

> `WingPack` is the gap: `WingsDeploy` 600 / `WingsFold` 604 exist and are wired on the ornithopter,
> but the player's own wing pack plays nothing on use.

---

## 5. Vehicles

### `agents/Vehicles/Aircraft/DuneOrnithopter.prefab` — `OrnithopterAudio`

The most completely wired prefab in the project.

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Airspeed wind, continuous loop | `WingsWindLoop` 602 | Wind rush, volume scales to speed (`windFullVolumeSpeed` 30, floor 0.15) | catalog |
| Wing flap (effort > 0.12) | `WingsFlap` 601 | Membrane whoomph, per beat | catalog |
| Stall (repeat gated to 2 s) | `WingsStall` 603 | Buffet + warning | catalog |
| Wings deploy (spread > 0.5) | `WingsDeploy` 600 | Mechanical unfurl | catalog |
| Wings fold | `WingsFold` 604 | Mechanical fold-away | catalog |

Both `windLoopSound` and `flapSound` overrides exist and are **empty** — deliberate hooks for a
future dedicated event. `windSpeedParameter` is also blank; filling it lets FMOD modulate the loop
from speed instead of just volume.

### `agents/Vehicles/Spacecraft/ShipRV.prefab` — `AudioLoop`

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Always-on engine hum (`playOnEnable`, follows transform, fades on stop) | `ShipEngineLoop` 700 | Ship idle hum | catalog |

`intensityParameter` is blank — wire it if the hum should react to throttle.

### Vehicles with **no audio at all**

`agents/Vehicles/Ground/DuneFoil.prefab`, `DesertCrawler.prefab`, `RigWalker.prefab`,
`agents/Vehicles/Spacecraft/CowBotRocket.prefab`, `Vehicles/Rover.prefab`, `RoverNoHierarchy.prefab`,
`agents/creatures/CrabWalker6.prefab`, `Ostrich.prefab`, `HumanoidRobot.prefab`.

The catalog already carries `VehicleStep` 705 and `AmbAntigravity` 803 for exactly this and **nothing
references them.** Wanted, per vehicle:

| Prefab | Sound wanted |
|---|---|
| `DuneFoil` | Sand hiss under the foil, rigging creak, sail luff/snap on sheet change |
| `DesertCrawler` | Six-legged footfall (`VehicleStep` 705), habitat hum |
| `RigWalker`, `CrabWalker6`, `Ostrich`, `HumanoidRobot` | Legged footfall — these use `LeggedLocomotion` and have exact footplant events available |
| `Rover` / `RoverNoHierarchy` | Wheel/motor loop |
| `CowBotRocket` | `ShipTakeoff` 701 / `ShipLanding` 702 — both exist, both unused |

---

## 6. Interaction and structures

### `Environment/Structures/Facilities/RepairWorkstation.prefab`

| Trigger | Id | Sound wanted | State |
|---|---|---|---|
| Scrap accepted | `InteractWorkstationRepair` 506 | Machinery accepting a part, work cycle | **default** (predates the field) |
| Scrap rejected | `InteractDenied` 508 | Refusal buzz | **default** |

Also has an unsounded animation: `spinningParts` at 220 °/s and a `clunkTarget` piston (0.04 m, 0.18 s).
**The spin wants a loop and the clunk wants a hit — neither exists.**

### Interaction scripts with no prefab

`DoorInteraction` (`InteractDoorOpen` 500 / `InteractDoorClose` 501), `LeverInteraction`
(`InteractLever` 502) and `ShipInteraction` (`ShipRepair` 703) are **only placed in scenes**, never on a
prefab. `Environment/Structures/Doors/SandstoneCaveDoor.prefab` and the three
`VisualEffects/cutsceneExamples/CutsceneDoor*.prefab` carry **no interaction component and no audio** —
they're the natural home for the door sounds.

---

### `Effects/SandstormGrit.prefab`

`SandstormVfx` only — **no audio component.** The storm's sound lives on `SandstormAudio`, which is
placed in scenes rather than on this prefab, and which deliberately does **not** use the catalog: it
is a plain `AudioSource` + `AudioLowPassFilter` playing an `AudioClip` off the active
`SandstormProfile`, 2D and looping, with volume and cutoff tracking storm intensity.

| Trigger | Sound wanted | State |
|---|---|---|
| Storm intensity > 0 | Continuous sand roar, 2D loop; low-passed when the listener is indoors | `SandstormProfile.loop` — a raw `AudioClip`, not an FMOD event |

---

## 7. UI

### `UI/Buttons/Menu Button.prefab` — `UIButton`

| Trigger | Sound wanted | State |
|---|---|---|
| Hover | Soft menu tick | **pinned `event:/UI/No`** — hover should be `UiHover` 900 (`UI/Yes`); the pin has hover and back swapped |
| Press | Confirm click | **pinned `event:/UI/Yes`** — correct |

Every other UI prefab (`PlayerHUD`, `InventoryUI`, `Slot`, `Interact`, `DialogePanel`) is **silent**.
`UiBack` 902, `UiError` 903 and `UiNotify` 904 exist in the catalog and nothing references them.

---

## 8. Infrastructure (not sound effects, but audio)

| Prefab | What it holds | Notes |
|---|---|---|
| `Systems/AudioManager.prefab` | Three `AudioSource`s named **UI**, **SFX**, **BGM**, each routed to a group in one `AudioMixer`, all `PlayOnAwake` with **no clip assigned**; plus `AudioManager` driving the FMOD busses (master/music/sfx/ui/reverb) from `GameSettings` | The three sources are vestigial — the mixer routing is Unity-side while the actual volume control is FMOD bus-side. The BGM source is where **music** would go, and there is no music today. |
| `Camera/Main Camera.prefab` | `FMODUnity.StudioListener` | The listener. |
| `Resources/Cameras/Mount Third Person Camera.prefab` | `AudioListener` **and** `FMODUnity.StudioListener` | Two listeners on the mount camera. Worth checking there is never a second active listener when mounted. |
| `Camera/3rd person.prefab` | none | No listener — fine as long as it is never the only active camera. |

---

## What to fix first

1. **`PlayerCharacter` `DamageFeedback` plays `event:/UI/No`.** A UI beep on player damage.
2. **`Menu Button` hover/press are swapped** — hover plays the negative event.
3. **`PatrolRobot 1` plays `PlayerDie` when hurt**, not when it dies.
4. **`LightningSpell` casts with `MetalPickup`.**
5. **`CixinGunFinal` fires on `WeaponGunFire` 200** when `WeaponBallLightningFire` 206 exists and is tuned for it.
6. **Nothing plays footsteps except the five robots and the player.** Every creature and every legged vehicle is silent underfoot.
7. **`WingPack` makes no sound on use** despite the full wings family existing in the catalog.
8. **No music.** The BGM bus and its `AudioSource` are wired and empty.

## Ids that exist and no prefab or script uses

`WeaponGunReload` 201, `WeaponGunEmpty` 202, `WeaponEnergyFire` 203, `WeaponBallLightningChargeLoop` 205,
`WeaponBallLightningFire` 206, `WeaponBallLightningArc` 207, `WeaponMeleeSwing` 208,
`WeaponMeleeImpact` 209, `WeaponEquip` 210, `WeaponProjectileWhoosh` 211, `ImpactShield` 302,
`ImpactCritical` 303, `ImpactExplosion` 304, `ImpactProjectile` 305, `NpcMumbleHostile` 402,
`NpcDialogBlip` 403, `NpcDialogOpen` 404, `NpcDialogClose` 405, `InteractPickupMetal` 504,
`InteractDrop` 505, `InteractPrompt` 509, `ShipTakeoff` 701, `ShipLanding` 702, `ShipAlarm` 704,
`VehicleStep` 705, `AmbWindLoop` 800, `AmbThunder` 802, `AmbAntigravity` 803, `UiBack` 902,
`UiError` 903, `UiNotify` 904.

One caveat on that list: `ShipEngineLoop` 700 and `NpcMumbleFriendly` 401 *look* unused in code but
are set as serialized values on `ShipRV` and `Nomad` respectively — a grep for `SfxId.X` misses
prefab-side assignment.

`AmbWindLoop` 800 and `AmbThunder` 802 are genuinely unused: the sandstorm deliberately bypasses the
catalog (see below), so the two ambience slots meant for it sit idle.
