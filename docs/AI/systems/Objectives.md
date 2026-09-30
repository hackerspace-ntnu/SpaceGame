---
system: Objectives
layer: presentation
summary: The crew's shared quest chain after the crash: one asset per step, briefings, visor panel, waypoint
paths:
  - Assets/Game/Scripts/Gameplay/Objectives/
  - Assets/Game/Scripts/Presentation/Objectives/
  - Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorObjective.cs
  - Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorProjection.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/ObjectiveSaveable.cs
symptoms:
  - "the objective line never appears on the visor after the crash"
  - "the lander's briefing plays again every time the world is loaded"
  - "a client sees the objective but never hears the lander's briefing"
  - "the first objective completes on its own while the ship is still falling"
  - "the first objective completes before I have pressed any keys"
  - "the controls checklist is stuck on waiting for the rest of the crew"
  - "the controls checklist does not tick while I am still strapped into the seat"
  - "a second belly turbine appears by the wreck after loading the world"
reads_with: [PlayerShip, Visor, Cutscenes, Multiplayer, Persistence]
updated: 2026-09-28
---

# Objectives

The start-of-game loop: an ordered chain of steps the whole crew works through after the crash —
try the basic controls, read the damage at the terminal, recover and fit a module, try an artifact,
repair the ship. The lander's computer briefs each step; the visor names it, lists its status and
points at it.

**Scope:** the chain, its steps and their presentation. The modules and sockets are
[PlayerShip](PlayerShip.md); the dialog popup is [InteractionSystem](InteractionSystem.md); the
visor layer is [Visor](Visor.md).

## Model

- **One shared place in one chain.** [`ObjectiveProgress`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveProgress.cs)
  — step index and `Begun` — is the whole of the shared state. The crew share it; there is no
  per-player progress. `default` is a fresh world.
- **One asset per step; its class is its rule.** [`ObjectiveChain`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveChain.cs)
  orders [`ObjectiveStep`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveStep.cs) assets. The
  base holds the words (id, visor title, briefing lines, `lookAtFocus`); each subclass under
  [`Steps/`](Assets/Game/Scripts/Gameplay/Objectives/Steps/) says what finishes it and what to
  show. The shipped chain is `Assets/Game/ScriptableObjects/Objectives/CrashSiteChain.asset`.
- **Steps are stateless and split across the authority line.** `TryBegin` / `IsMet` run on the
  SERVER; `TrackLocalPlayer` / `Status` / `TryGetWaypoint` / `TryGetFocus` / `TryGetBeacon` run on
  EVERY machine. Anything remembered lives in [`ObjectiveWorld`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveWorld.cs),
  whose per-step memory is wiped by `ForgetStep` whenever the step changes.
- **A step every player must do themselves** overrides `TrackLocalPlayer` (watch this machine's
  player, return true when done) and meets on `world.EveryoneFinished`. The director reports the
  local player once per step; the server counts reports per `OwnerClientId`. `LearnControlsStep` is
  the only one today.
- **What a step spawns is an ordinary pickup.** [`PlacedItemStep`](Assets/Game/Scripts/Gameplay/Objectives/Steps/PlacedItemStep.cs)
  drops its item through `GameServices.World.Spawn` in `TryBegin` and afterwards finds it again
  through `ScannerRegistry` (`PickupableItem.Item`) — nothing records where it went.
- **Guidance is explicit, with an environmental half.** The visor waypoint is the
  `GDC-L1-LEVEL-0001` open-world exception (4 km desert, few landmarks); the light-column beacon is
  something in the world to walk toward. The controls are taught by doing, with each line
  confirming itself on use (`GDC-L1-UX-0001`, `GDC-L1-UX-0003`).

## Key types

| Type | Role |
| --- | --- |
| `ObjectiveDirector` | `persistentScene`, `[DefaultExecutionOrder(-100)]`. Holds the progress. Every machine: `TrackLocalPlayer` → `LocalPlayerFinished(step)`. Server: begins and judges steps. `Adopt` (wire) / `Restore` (save) elsewhere. `Changed(bool live)`. Story worlds only (`!VersusSession.IsActive`) |
| `ObjectiveNetwork` | On `NetworkGameManager.prefab`, beside `ChatNetwork`. One server-write `NetworkVariable<ObjectiveProgress>`, read on spawn for late joiners; `ReportFinishedRpc(step)` client → server, sender taken from `RpcParams` |
| [`ObjectiveSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/ObjectiveSaveable.cs) | Global saver, key `objectives`: step **id**, `complete`, `begun` |
| `LearnControlsStep` | Checklist of `lessons` (move, sprint, jump, crouch) in `Status`; only counts input while out of the seat and every cutscene; met when every player has finished |
| `UseTerminalStep` | The hull's `TerminalConsole.Occupied`; focus = first empty socket |
| `RecoverModuleStep` | Drops `SmallMotor` 90–130 m out; met by any socket fitted beyond `AuthoredMask`; waypoint = loose module, else the empty socket |
| `TryArtifactStep` | Drops `JumpingRod` 22 m off the nose; met on `UseChannel.UsedOnServer` for that asset |
| `RepairShipStep` | `ShipPartRack.IsComplete`; `Status` = `Modules fitted n/total` |
| [`ObjectiveBriefing`](Assets/Game/Scripts/Presentation/Objectives/ObjectiveBriefing.cs) | Plays a step's lines through `NpcDialogPopupUI`, prefixed with the speaker, once the local player is present; `LookAtCutscene` to the focus |
| [`VisorObjective`](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorObjective.cs) | Top-right panel: heading, title, `Status`, range; NEW OBJECTIVE pop + accent pulse on a step change; waypoint diamond pinned to the edge off screen (`VisorProjection.PinToScreen`) |
| `ObjectiveBeacon` | Placeholder light column over `TryGetBeacon` |

## Flows

**Advance (server).** `Update`: nothing until `ArrivalDirector.HasArrived` → `TryBeginCurrent`
(once per step; false = retry next frame) → `IsMet` → next step (`ForgetStep`), and straight into
its `TryBegin` so peers usually hear step and `Begun` in one change → `Changed(true)` →
`ObjectiveNetwork` writes the variable.

**Report (every machine, own player).** While the step has begun, `step.TrackLocalPlayer` each
frame; the first true raises `LocalPlayerFinished(step)` → host records itself directly, a client
sends `ReportFinishedRpc` → `ObjectiveDirector.RecordFinished`, which drops a report for a step the
crew have already left.

**Brief (every machine).** `ObjectiveBriefing` queues a step's lines when it sees `Begun` flip for a
new step with `live == true` — step 0 begins only once the hull has landed, which is what makes it
briefable at all. Lines wait until the player is spawned and out of every cutscene for
`settleSeconds`, and for the popup to be free. The first line of a `lookAtFocus` step plays under
the look; the rest wait for it to end.

## Multiplayer

| What | Carrier | Authority |
| --- | --- | --- |
| Progress | `ObjectiveNetwork` `NetworkVariable` | Server |
| "My player has done their part" | `ObjectiveNetwork.ReportFinishedRpc` | Owner reports, server counts |
| Placed module / artifact | Registered item prefabs via `World.Spawn` | Server |
| Artifact use | `UseChannel.UsedOnServer`, raised in `OnUseRequested` after the peer broadcast | Server |
| Briefing, visor panel and checklist, beacon | none | Local, derived |

A late joiner adopts the progress `live: false` — no briefing replay, visor correct at once. A player
who joins during the controls step must do it too; one who leaves stops counting.

## Persistence

`ObjectiveSaveable` (key `objectives`). What a step spawned persists as its own runtime entity. The
per-step memory in `ObjectiveWorld` (uses, finished players, the local checklist) is **not** saved:
a reload during the controls step asks everyone for the controls again. A step id the chain no
longer has restarts the chain with a warning (`ObjectiveDirector.Resolve`).

## Gotchas

- **Nothing may be judged before the landing.** Bodies spawn at the hull and are seated a frame
  later. The director waits for `HasArrived`, which a restored world already has.
- **Input in the seat proves nothing.** `LearnControlsStep` ignores input while
  `SeatedRider.LocalPlayerMayLeave` or a cutscene plays; the seat's own Esc prompt gets the player up.
- **A per-player step needs every player.** One AFK player holds the controls step for the whole
  crew; that is the price of every player being taught. The checklist tells a finished player they
  are waiting on the others.
- **`Begun` is saved on purpose.** Without it a load either spawns the wreck's module again or,
  saved between step entry and setup, never spawns it.
- **`World.Spawn` alone does not make a pickup persist.** `ObjectiveWorld.Spawn` stamps it with
  `SaveableEntity.EnsureRuntime(obj, item.ID)`, as `PlayerDropService` does; without that the module
  saves with no prefab id, `Compact` drops it, and `Begun` guarantees nothing places it again.
- **Ship modules exist in no real world scene** (only `Ferdinand_Test_world`). The first one a player
  ever sees is the one `RecoverModuleStep` drops.
- **A loose item is only found while its chunk is loaded**, so a module dropped 600 m+ from the hull
  drops off the waypoint until someone walks near it.
- **Step assets are the step's class.** Changing a step's kind means a new asset of the new class (or
  editing `m_Script`), not swapping a field.
- **Finishing the ship does not win the game** — `GameManager.WinGame` still has no caller.

## Extending

1. **A new step of an existing kind:** create it from **Create ▸ SpaceGame ▸ Objectives**, give it a
   new, permanent `id`, add it to the chain. Reordering is safe — saves resolve by id.
2. **A new kind of step:** subclass `ObjectiveStep` (or `PlacedItemStep` to drop an item), keep it
   stateless, put server work in `TryBegin`/`IsMet` only, add a `[CreateAssetMenu]`. Per-step memory
   goes in `ObjectiveWorld` and is cleared in `ForgetStep`.
3. **A lead to a settlement (issue items 5–6, not built):** a step whose `TryBegin` picks a town and
   whose `IsMet` is "a crew member inside its radius". Two things will bite: `WorldSiteRegistry` only
   knows **loaded** chunks and no town carries a marker, so the destination needs a record that is
   always in memory (a baked list of towns); and the chosen town is state that must be **replicated
   and saved by a stable id**, not an index — add it to `ObjectiveProgress` and `ObjectiveSaveable`.
4. **Verify** on a client (briefing, visor panel, checklist and beacon on the second machine) and
   after a reload (`objectives` in the save JSON; no second module).
