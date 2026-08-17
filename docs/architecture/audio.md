# Game Audio — architecture and event map

_Generated 2026-08-16. Regenerate the catalog with `gen_catalog.py`._


## What this is

Every sound in the game is asked for by **meaning**, not by asset. A call site says
`Sfx.Play(SfxId.PlayerJump, position)`; the `AudioCatalog` asset decides which FMOD event that
actually plays. Swapping in better audio later is an edit to one asset, not a hunt through forty
scripts.

## Why the indirection

The project ships **19 FMOD events** and no FMOD Studio project — the `.fspro` that built
`SFX.bank` was never committed and is not on this machine, and `.bank` is a compiled format that
cannot be authored without FMOD Studio. So the 69 slots below currently map onto those 19 events,
with heavy reuse. **37 mappings are honest; 32 are stand-ins**, marked in the table and carrying a
`note` in the asset so they can be found later with a single search.

The wiring is the durable part. When the `.fspro` turns up, authoring the real events and pointing
each slot at one is a content pass with no code changes.

## The pieces

| File | Role |
|---|---|
| `Assets/Game/Scripts/Audio/SfxId.cs` | The vocabulary — 69 named sounds. Values are explicit and grouped in hundreds; **never reuse a number for a new meaning**, it is serialized into the catalog and into components. |
| `Assets/Game/Scripts/Audio/AudioCatalog.cs` | ScriptableObject mapping `SfxId` → event + cooldown, max distance, volume trim. Loaded from `Resources/AudioCatalog`. |
| `Assets/Game/Scripts/Audio/Sfx.cs` | The façade. `Play`, `Play2D`, `PlayAttached`. Never throws. |
| `Assets/Game/Scripts/Audio/LoopingEmitter.cs` | Owns one sustained event. Start/stop/parameter, and the release that stops loops leaking. |
| `SpaceGame.Audio.asmdef` | Its own assembly, FMOD-only dependency — so the vehicle and creature asmdefs can reach it. They cannot reference `Assembly-CSharp`. |

## Two ways to pick a sound

Components expose **both**, and the inspector field wins:

```csharp
[SerializeField] private SfxId footstepId = SfxId.EntityFootstep;  // catalog default
[SerializeField] private EventReference footstepSound;             // hard override

Sfx.Play(footstepId, transform.position, footstepSound, GetInstanceID());
```

This is what let the **39 EventReference assignments already sitting in prefabs** keep working
untouched while every field nobody ever assigned started making noise.

## Rules worth keeping

- **Loops must be stopped in both `OnDisable` and `OnDestroy`.** A component cleaning up on only one
  path leaks a voice whenever the game exits through the other.
- **Server-only code cannot play sound.** `PickupableItem.Pickup` and `ShipInteraction.ExecuteInteraction`
  run on the server; a remote client would hear nothing. Those play locally on interact instead.
  Where a replicated channel already exists — `RepairWorkstation.PlayFeedback`, `HealthComponent.OnRevive` —
  the sound goes there, so it fires on every machine and only on genuine success.
- **`Projectile.OnImpact` is deliberately not gated on `Cosmetic`.** Every peer holds a copy of the
  shot and only one may bill the target, but all of them should hear it land.
- **Crowd control is the catalog's job.** Mumbles carry a multi-second cooldown and an 18 m range so
  a settlement full of NPCs does not turn the mix to mush.

## The event map

`✓` = the mapping is right. `~` = stand-in, awaiting real audio.


### Player

| | SfxId | # | Plays | Cooldown | Max dist | Vol |
|---|---|---|---|---|---|---|
| ✓ | `PlayerFootstep` | 100 | `event:/SFX/Footstep` | — | 30 m | 1.00 |
| ✓ | `PlayerJump` | 101 | `event:/SFX/Jump` | — | 30 m | 1.00 |
| ~ | `PlayerLand` | 102 | `event:/SFX/Footstep` | 0.05s | 30 m | 1.00 |
| ~ | `PlayerLandHeavy` | 103 | `event:/SFX/Wham` | 0.05s | 40 m | 0.90 |
| ~ | `PlayerDash` | 104 | `event:/SFX/Antigravity` | 0.10s | 30 m | 0.80 |
| ✓ | `PlayerHurt` | 105 | `event:/SFX/Hit` | 0.15s | 25 m | 1.00 |
| ✓ | `PlayerDeath` | 106 | `event:/SFX/PlayerDie` | — | — | 1.00 |
| ~ | `PlayerRespawn` | 107 | `event:/SFX/Antigravity` | — | — | 0.90 |

### Weapons

| | SfxId | # | Plays | Cooldown | Max dist | Vol |
|---|---|---|---|---|---|---|
| ~ | `WeaponGunFire` | 200 | `event:/SFX/Wham` | — | 90 m | 0.70 |
| ~ | `WeaponGunReload` | 201 | `event:/SFX/MetalPickup` | — | 25 m | 0.90 |
| ~ | `WeaponGunEmpty` | 202 | `event:/UI/No` | 0.20s | 20 m | 0.70 |
| ~ | `WeaponEnergyFire` | 203 | `event:/SFX/ElectricHum` | — | 90 m | 0.85 |
| ✓ | `WeaponEnergyChargeLoop` | 204 | `event:/SFX/ElectricHum` | — | — | 0.70 |
| ✓ | `WeaponBallLightningChargeLoop` | 205 | `event:/SFX/ElectricHum` | — | — | 0.80 |
| ✓ | `WeaponBallLightningFire` | 206 | `event:/SFX/Implosion` | — | 90 m | 1.00 |
| ✓ | `WeaponBallLightningArc` | 207 | `event:/SFX/ElectricHum` | 0.08s | 45 m | 0.60 |
| ~ | `WeaponMeleeSwing` | 208 | `event:/SFX/Antigravity` | — | 25 m | 0.60 |
| ✓ | `WeaponMeleeImpact` | 209 | `event:/SFX/Wham` | — | 30 m | 0.90 |
| ✓ | `WeaponEquip` | 210 | `event:/SFX/MetalPickup` | 0.05s | 20 m | 0.80 |
| ~ | `WeaponProjectileWhoosh` | 211 | `event:/SFX/Antigravity` | 0.05s | 40 m | 0.45 |
| ✓ | `ImpactFlesh` | 300 | `event:/SFX/Slurp` | — | 40 m | 0.90 |
| ✓ | `ImpactMetal` | 301 | `event:/SFX/Hit` | — | 40 m | 1.00 |
| ~ | `ImpactShield` | 302 | `event:/SFX/ElectricHum` | 0.05s | 40 m | 0.80 |
| ✓ | `ImpactCritical` | 303 | `event:/SFX/Wham` | — | 50 m | 1.00 |
| ✓ | `ImpactExplosion` | 304 | `event:/SFX/Explosion` | — | 200 m | 1.00 |
| ✓ | `ImpactProjectile` | 305 | `event:/SFX/Hit` | — | 50 m | 0.80 |
| ~ | `NpcMumbleNeutral` | 400 | `event:/SFX/Slurp` | 4.0s | 18 m | 0.55 |
| ~ | `NpcMumbleFriendly` | 401 | `event:/SFX/Slurp` | 4.0s | 18 m | 0.55 |
| ~ | `NpcMumbleHostile` | 402 | `event:/SFX/Slurp` | 3.0s | 22 m | 0.70 |
| ~ | `NpcDialogBlip` | 403 | `event:/SFX/Slurp` | 0.06s | — | 0.35 |
| ✓ | `NpcDialogOpen` | 404 | `event:/UI/Yes` | 0.10s | — | 0.70 |
| ✓ | `NpcDialogClose` | 405 | `event:/UI/No` | 0.10s | — | 0.60 |
| ~ | `EntityAggro` | 406 | `event:/SFX/Wham` | 0.50s | 45 m | 0.85 |
| ~ | `EntityAlert` | 407 | `event:/UI/No` | 0.50s | 35 m | 0.70 |
| ~ | `EntitySearch` | 408 | `event:/SFX/Slurp` | 2.0s | 25 m | 0.50 |
| ✓ | `EntityHurt` | 409 | `event:/SFX/Hit` | 0.10s | 35 m | 0.90 |
| ~ | `EntityDeath` | 410 | `event:/SFX/Implosion` | — | 50 m | 0.95 |
| ✓ | `EntityFootstep` | 411 | `event:/SFX/Footstep` | — | 28 m | 0.75 |
| ✓ | `EntityAttack` | 412 | `event:/SFX/Wham` | — | 35 m | 0.80 |
| ~ | `InteractDoorOpen` | 500 | `event:/SFX/Antigravity` | — | 25 m | 0.85 |
| ~ | `InteractDoorClose` | 501 | `event:/SFX/Wham` | — | 25 m | 0.70 |
| ~ | `InteractLever` | 502 | `event:/SFX/MetalPickup` | — | 20 m | 0.90 |
| ✓ | `InteractPickup` | 503 | `event:/SFX/Pickup` | — | 20 m | 1.00 |
| ✓ | `InteractPickupMetal` | 504 | `event:/SFX/MetalPickup` | — | 20 m | 1.00 |
| ~ | `InteractDrop` | 505 | `event:/SFX/Hit` | — | 20 m | 0.65 |
| ~ | `InteractWorkstationRepair` | 506 | `event:/SFX/ElectricHum` | — | 25 m | 0.80 |
| ✓ | `InteractScannerDiscovery` | 507 | `event:/UI/Yes` | — | — | 1.00 |
| ✓ | `InteractDenied` | 508 | `event:/UI/No` | 0.30s | — | 0.80 |
| ~ | `InteractPrompt` | 509 | `event:/UI/Yes` | 0.25s | — | 0.40 |

### Wings

| | SfxId | # | Plays | Cooldown | Max dist | Vol |
|---|---|---|---|---|---|---|
| ✓ | `WingsDeploy` | 600 | `event:/SFX/Takeoff` | — | 60 m | 0.90 |
| ~ | `WingsFlap` | 601 | `event:/SFX/Antigravity` | — | 45 m | 0.55 |
| ✓ | `WingsWindLoop` | 602 | `event:/SFX/Wind Howling` | — | — | 1.00 |
| ~ | `WingsStall` | 603 | `event:/UI/No` | 1.0s | 50 m | 0.75 |
| ~ | `WingsFold` | 604 | `event:/SFX/MetalPickup` | — | 40 m | 0.80 |
| ✓ | `ShipEngineLoop` | 700 | `event:/SFX/ShipHumLoop` | — | — | 1.00 |
| ✓ | `ShipTakeoff` | 701 | `event:/SFX/Takeoff` | — | — | 1.00 |
| ~ | `ShipLanding` | 702 | `event:/SFX/Wham` | — | 120 m | 0.90 |
| ~ | `ShipRepair` | 703 | `event:/SFX/ElectricHum` | — | 30 m | 0.85 |
| ~ | `ShipAlarm` | 704 | `event:/UI/No` | 1.0s | 60 m | 0.85 |
| ~ | `VehicleStep` | 705 | `event:/SFX/Wham` | — | 70 m | 0.60 |
| ✓ | `AmbWindLoop` | 800 | `event:/SFX/Wind Howling` | — | — | 1.00 |
| ✓ | `AmbInteriorHum` | 801 | `event:/SFX/ElectricHum` | — | — | 0.70 |
| ✓ | `AmbThunder` | 802 | `event:/SFX/Thunder` | — | — | 1.00 |
| ✓ | `AmbAntigravity` | 803 | `event:/SFX/Antigravity` | — | — | 0.80 |
| ✓ | `UiHover` | 900 | `event:/UI/Yes` | 0.05s | — | 0.45 |
| ✓ | `UiPress` | 901 | `event:/UI/Yes` | — | — | 0.85 |
| ✓ | `UiBack` | 902 | `event:/UI/No` | — | — | 0.75 |
| ✓ | `UiError` | 903 | `event:/UI/No` | 0.20s | — | 0.90 |
| ✓ | `UiNotify` | 904 | `event:/UI/Yes` | 0.10s | — | 0.80 |


## When the `.fspro` arrives

1. Point Unity at it: **FMOD → Edit Settings → Source Project**, then rebuild banks.
2. Author events at the paths you want. The names above are a suggestion, not a contract — the
   catalog is what binds, so you can name them anything.
3. Re-point each slot in `Assets/Game/Resources/AudioCatalog.asset`. The 32 rows carrying a `note`
   are the ones that need it most.
4. Nothing else changes. No code edits, no prefab edits.

### One trap

`FMODStudioSettings.EventLinkage` is **GUID** (`0`). Prefab references bind by GUID, so recreating
an event from scratch gives it a new GUID and silently breaks the 39 existing assignments. Either
keep the original project, or switch `EventLinkage` to **Path** before recreating anything.

