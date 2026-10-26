---
system: WitchFight
layer: world
summary: "The Morwen boss fight: hidden witch, poison spiral, rue-lobbing keepers you carry, fail screen, finisher"
paths:
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/WitchFight.cs
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/WitchArena.cs
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/PoisonBall.cs
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/RueBomb.cs
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/PoisonTally.cs
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/PoisonPips.cs
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/KeeperCarry.cs
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/KeeperLiftHandle.cs
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/KeeperPose.cs
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/ShimmerCloak.cs
  - Assets/Game/Scripts/Gameplay/SpriteHamlet/FootprintTrail.cs
  - Assets/Game/Scripts/Agents/Modules/Quest/PoisonSpiralModule.cs
  - Assets/Game/Scripts/Agents/Modules/Quest/HerbBoundLife.cs
  - Assets/Game/Scripts/Agents/Modules/Quest/RueStrikeModule.cs
  - Assets/Game/Scripts/Agents/Modules/Movement/ProwlModule.cs
  - Assets/Game/Scripts/Presentation/Cutscenes/Actions/QuestFailedCutscene.cs
  - Assets/Game/Scripts/Presentation/Cutscenes/Actions/OrbitCutscene.cs
  - Assets/Game/Scripts/Items/Inventory/Components/ArmsLoad.cs
  - Assets/Game/Art/Shaders/Effects/WitchShimmer.shader
  - Assets/Game/Prefabs/VisualEffects/WitchFight
symptoms:
  - "the keeper I was carrying dropped out of my arms by itself"
  - "the keepers never throw anything"
  - "the QUEST FAILED screen shows black rectangles instead of letters"
  - "the QUEST FAILED letters are solid black, no world shows through"
  - "the screen stays black between QUEST and FAILED after the title"
  - "a poison ball over a keeper says BLOCKED"
  - "the finishing shot films the witch from inside her hut"
  - "the witch never shows up, I cannot find her"
  - "rue burned all three herbs and the finisher never played"
reads_with: [SpriteHamletQuest, AgentSystem, CarriedAgent, Cutscenes, Multiplayer, Artifacts]
updated: 2026-10-21
---

# The witch fight

How the crew beats Morwen at her glade: freeze her with the Oathbranch so the keepers' rue bombs can land, carry the keepers out of her poison, and watch them pluck her last herb. The quest around it (steps, spawning, the monologue, the Oathbranch itself) is [SpriteHamletQuest.md](SpriteHamletQuest.md).

## Model

- **Two jobs that compete** (`GDC-L1-DESIGN-0002`, changed 2026-10-14 from "freeze her, keepers run in and press rue", which played as one button on a cooldown). Freezing her is the only way to hurt her; carrying keepers is the only way to protect them; full arms cannot fire the staff (`ArmsLoad`).
- **She hides between attacks** (`ShimmerCloak`): her meshes draw `WitchShimmer` (faint broken rim), she **pulses** every `pulseInterval` on the shared server clock (bright outline + ripple), and leaves **footprints** (`FootprintTrail`). Shown while held for the monologue, while either attack is drawn, in stasis, during the finish and once dead. Colliders are untouched, so the bolt hits her unseen.
- **She prowls, she does not chase** (`ProwlModule`): darts round a ring 8–13 m from her target, inside the glade's `WitchArena`, at run speed 7.5. Melee and chase were removed from the prefab. `BlinkModule` hops at most `maxHop` (7 m) to a `BlinkArena` point.
- **Main attack: the spiral** (`PoisonSpiralModule`, priority 23): wind-up 0.9 s (`Spell Cast`), then she spins (`Airplane`, the `Model` child turned on every machine) for 4 s throwing `PoisonBall`s on two arms. Balls float at keeper height, drift at 4 m/s, hurt + shove a player (`NetMsg.Flung`), and hit a keeper with `DamageKind.Poison`. A stasis ends the spin everywhere.
- **Rare attack: thorns** (`ConjurerCastModule`, cooldown 24 s). Warned by `ThornSproutWarning` — earth and leaves heaving over the blast radius, thickening towards impact — not a ring (the user's call: a ring around thorns from the ground made no sense). It is still a `StrikeTelegraph`, so it tracks and locks the same way.
- **The keepers hold posts and lob rue** (`RueStrikeModule`, the old class name kept for its GUID): each walks to its `WitchArena` post, throws a `RueBomb` at where she stands every 2.4 s (+ jitter), ducks while she spins. Bombs are aimed at where she *was* and fly ~1 s, so a free witch is missed; a frozen one is not. A keeper set down somewhere new throws from there.
- **Her life** (`HerbBoundLife`): only `Rue` in `Stasis`, one herb per stasis, and with `lastHerbPlucked` rue stops at **1 HP** — `IsBroken`. A burn spawns `HerbBurnFx`, jolts her model and shakes nearby cameras.
- **Three poison balls fell a keeper** (`PoisonTally`, never heals, 3 pips over its head). Any fall **loses the fight**: the failure screen, and a full reset under it.
- **Win** = `IsBroken` → `WitchFight` holds her, the orbit cutscene plays, the keepers leap onto her hat (`KeeperCarry.Perch`), and `PluckLastHerb` kills her → the quest's `DefeatActorStep` is met.

## Key types

| Type | Role |
| --- | --- |
| [`WitchFight`](Assets/Game/Scripts/Gameplay/SpriteHamlet/WitchFight.cs) | On Morwen, Scripted module. `keeperIds`; on `PoisonTally.Fell` → `WitchFightFailed` + reset coroutine; on `IsBroken` → `WitchFinisher` + `Finish` coroutine. Holds her and silences `silenced` (blink) through both. `IsFinishing` every machine |
| [`WitchArena`](Assets/Game/Scripts/Gameplay/SpriteHamlet/WitchArena.cs) | On the glade: `radius` (prowl bound), `WitchHome` (reset point), `posts` keeperId → Transform. Found via `LiveSet` |
| [`PoisonSpiralModule`](Assets/Game/Scripts/Agents/Modules/Quest/PoisonSpiralModule.cs) | Decide on server, `NetMsg.WitchSpiral` (P origin, A yaw×100, B ±1), every machine spins + throws on its own clock; `SpinningNear` (keepers duck), `Interrupt`, `IsPresenting` |
| [`PoisonBall`](Assets/Game/Scripts/Gameplay/SpriteHamlet/PoisonBall.cs) · [`RueBomb`](Assets/Game/Scripts/Gameplay/SpriteHamlet/RueBomb.cs) | Projectiles flown on every machine; only the non-`cosmetic` copy bills. Not network prefabs |
| [`PoisonTally`](Assets/Game/Scripts/Gameplay/SpriteHamlet/PoisonTally.cs) | Keeper NetworkBehaviour + `IDamageFilter` + `ISaveable` ("poisonTally"): counts `Poison`, zeroes it; `Fell` (server), `HitsChanged` (all), `Clear()` |
| [`KeeperCarry`](Assets/Game/Scripts/Gameplay/SpriteHamlet/KeeperCarry.cs) | Keeper NetworkBehaviour: replicated carrier + arm slot; server tows via `NavMeshAgentMotor.RequestTow`; body colliders off while carried; `ArmsLoad.Take` on the carrier; `Perch`/`Unperch` for the finish; `SetDownAt` → new post |
| [`KeeperLiftHandle`](Assets/Game/Scripts/Gameplay/SpriteHamlet/KeeperLiftHandle.cs) | Child trigger `IInteractable` "RMB: pick up {name}" — on its own trigger because the keeper's body answers with `DialogInteraction` |
| [`ArmsLoad`](Assets/Game/Scripts/Items/Inventory/Components/ArmsLoad.cs) | Refcounted "arms full" per body; `EquipmentController` veils the held item and routes Use to `PressUse` (sets every carried keeper down) |
| [`KeeperPose`](Assets/Game/Scripts/Gameplay/SpriteHamlet/KeeperPose.cs) · [`PoisonPips`](Assets/Game/Scripts/Gameplay/SpriteHamlet/PoisonPips.cs) | Presentation: cower/throw/flinch/fallen/wriggle layered on the armature root, per-slot green tint, cough puffs; the pip row |
| [`ShimmerCloak`](Assets/Game/Scripts/Gameplay/SpriteHamlet/ShimmerCloak.cs) · [`FootprintTrail`](Assets/Game/Scripts/Gameplay/SpriteHamlet/FootprintTrail.cs) | Material swap + `_Pulse` via MPB; prints emitted into one world-space horizontal-billboard system |
| [`QuestFailedCutscene`](Assets/Game/Scripts/Presentation/Cutscenes/Actions/QuestFailedCutscene.cs) · [`OrbitCutscene`](Assets/Game/Scripts/Presentation/Cutscenes/Actions/OrbitCutscene.cs) | Children of Morwen. Stencil-window title in Anton + grime + reason line; slow arc round her that stays in front of static scenery |

## Flows

**Lose.** Third ball → `PoisonTally.Fell` (server) → `WitchFight` (fight on: alive, not finishing/resetting/held) → `NetToAll(WitchFightFailed, A = keeper index)` → every machine interrupts the spin; players within `audienceRange` play the failure cutscene ("{name} breathed the witch's poison"). Server, `resetAfter` 1.4 s later (screen black): `KeeperCarry.SetDownEverywhere`, destroy loose balls/bombs, clear her statuses, `ResetToFull`, teleport her to `WitchHome`, each keeper `Clear` + `ReturnToPost` + teleport to its post; `graceAfterReset` 3 s holding her still.

**Carry.** RMB on the handle → `RequestLift` → server: `MayActFor`, `CanBeLifted`, within `liftReach` 4 m, a free slot of 3 → carrier + slot set. Each physics step the server tows one bounded step (14 m/s) towards `ArmPoint`: level steps drag it along the mesh, the steep last step is a lift. Every machine: colliders off, `ArmsLoad` taken, `KeeperPose` draws it at *its own* view of the carrier's arms. LMB → `ArmsLoad.PressUse` → `KeeperLift A=0` → each held keeper is let go in front of the carrier, falls, lands → `SetDownAt` → that is its new post. A carrier who dies or leaves drops them.

**Win.** `HerbBoundLife.IsBroken` (1 HP) → `WitchFinisher` → all: `IsFinishing` (shown, spin interrupted), orbit cutscene near her. Server: set everyone down, wait `leapDelay`, each keeper `Perch(head bone, offset, leapSeconds)` staggered, wait `leapSeconds + pluckSeconds`, `PluckLastHerb`, `Unperch` → they drop; her death completes the step.

## Multiplayer

| What | Carrier | Authority |
| --- | --- | --- |
| Spin start | `NetMsg.WitchSpiral` (144), witch relay | Server; balls flown everywhere, billed by server copies |
| Bomb throw | `NetMsg.RueBombThrown` (145), keeper relay | Server |
| Lift / set down | `NetMsg.KeeperLift` (146) → server, judged on the sender's body | Server writes `carrierId`, `slot` NetworkVariables |
| Fail / finish | `NetMsg.WitchFightFailed` (147) / `WitchFinisher` (148), witch relay | Server; cutscenes local |
| Poison count | `PoisonTally` NetworkVariable | Server (damage filter) |
| Cloak, pulse, footprints, poses, pips | derived from replicated state + `SharedClock` | Every machine |
| Shove on a player | `NetMsg.Flung` | Victim's owner |

## Persistence

Saved: the witch's health (herbs burned) and each keeper's `PoisonTally` (restored at most `fallsAt − 1`, so a save taken in the moment between a fall and the reset cannot load a keeper that never stands up). **Not saved, on purpose:** who carries whom (a reload puts keepers on their feet), a keeper's moved post (back to the arena's), stasis, the spin, a reset or finish in progress (a finish re-triggers on load because `IsBroken` is read from saved health). **Not yet verified by a real save/reload or on a second machine** — see Extending.

## Gotchas

- **A fail resets everything, including your arms.** `SetDownEverywhere` is part of the reset, so a keeper "dropping by itself" is usually somebody else's keeper falling.
- **The spiral targets whatever her `AgentTargeting` picks — often a keeper**, since the hamlet's faction is hostile to hers. That is the pressure; keepers standing still take a ball every spin or two.
- **The keeper's armature root is animated** (Bellcap clips key its rotation). `KeeperPose` composes on top of the animator each `LateUpdate` and detects a frame the animator skipped; writing an absolute rotation freezes the walk, adding blindly compounds.
- **TMP-generated font assets use `TextMeshPro/Mobile/Distance Field`, which has no `_FaceTex`.** The grime and the stencil window are separate material assets on the full `Distance Field` shader (`QuestFailedGrime.mat`, `QuestFailedWindow.mat`).
- **A stencil-writing TMP material marks whole glyph QUADS unless `UNITY_UI_ALPHACLIP` is on** — the window showed black rectangles until the keyword was enabled.
- **Open the title out by lifting the black, not by scaling alone** — scaled up, the gap between the two words covers the screen.
- **A herb only burns in stasis, and the stasis shell already stopped her animator** (`StasisShell.HoldPose`). The burn's hit-pause is a scale jolt of `Model`; touching `Animator.speed` would thaw her pose under the shell.
- **`StunOnHitModule` swallows `Poison` without stunning or saying BLOCKED** — a floored keeper would stop throwing and the spiral would win alone.
- **Keepers carried in arms must sit low and forward** (`armSlots` feet at 0.25–0.3 m) or the bell hats fill the first-person view. A serialized field: retune the prefabs, not the default.
- **The finish is filmed against her hut after a reset** (she is put back at her door). `OrbitCutscene` picks, before it starts, the swing (authored, reversed, or half of either) whose whole arc is clear at full radius; pulling the camera in along the look ray alone left it a hand's breadth from her face. Only Rigidbody-less colliders occlude, so the keepers and the witch never pull it in.

## Extending

1. **Tuning** lives on the prefabs: spin cadence/arms on `PoisonSpiralModule`, ball speed/damage on `PoisonBall.prefab`, throw rate on each keeper's `RueStrikeModule`, `fallsAt` on `PoisonTally`, ring radii on `ProwlModule`, pulse on `ShimmerCloak`.
2. **A keeper more**: a `QuestActor` id in `WitchFight.keeperIds`, a post in the glade's `WitchArena`, an arm slot if three hands are not enough. `SpriteHamletWiringTests.EveryKeeperOfTheFightHasAPost` checks the first two.
3. **Verify** in `Tests/Marius test scene`: restore to `hamlet-confront-witch`, move the player and keepers into the glade, then `hamlet-defeat-witch` (begun) — see [SpriteHamletQuest.md](SpriteHamletQuest.md) Extending #6. Still owed: a client (lift, fail screen, spin and pulse on the second machine) and a save/reload mid-fight.
