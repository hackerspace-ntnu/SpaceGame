---
system: Objectives
layer: presentation
summary: The crew's shared quest chains: the crash chain plus accepted side quests; briefings, visor, waypoint
paths:
  - Assets/Game/Scripts/Gameplay/Objectives/
  - Assets/Game/Scripts/Presentation/Objectives/
  - Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorObjective.cs
  - Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorProjection.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/ObjectiveSaveable.cs
  - Assets/Game/Editor/Tests/ObjectiveChainsTests.cs
symptoms:
  - "the objective line never appears on the visor after the crash"
  - "the lander's briefing plays again every time the world is loaded"
  - "a client sees the objective but never hears the lander's briefing"
  - "the first objective completes on its own while the ship is still falling"
  - "the first objective completes before I have pressed any keys"
  - "the controls checklist is stuck on waiting for the rest of the crew"
  - "the controls checklist does not tick while I am still strapped into the seat"
  - "a second belly turbine appears by the wreck after loading the world"
  - "an old save loads with the crash-site objectives back at the first step"
  - "a side quest I accepted is gone after loading the world"
reads_with: [PlayerShip, Visor, Cutscenes, Multiplayer, Persistence, SpriteHamletQuest]
updated: 2026-10-07
---

# Objectives

The start-of-game loop: an ordered chain of steps the whole crew works through after the crash —
try the basic controls, read the damage at the terminal, recover and fit a module, try an artifact,
repair the ship. The lander's computer briefs each step; the visor names it, lists its status and
points at it. Beside it run **side chains** — a settlement's quest, a villager's favour — each
standing still until somebody accepts it.

**Scope:** the chain, its steps and their presentation. The modules and sockets are
[PlayerShip](PlayerShip.md); the dialog popup is [InteractionSystem](InteractionSystem.md); the
visor layer is [Visor](Visor.md).

## Model

- **One shared place in each chain.** [`ObjectiveProgress`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveProgress.cs)
  — step index, `Begun`, and for a side chain `Started` — is the whole of one chain's shared state.
  The crew share it; there is no per-player progress. `default` is a chain nobody has played.
- **One [`ObjectiveRun`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveRun.cs) per chain** — chain,
  progress, own `ObjectiveWorld`. Run 0 is the director's `chain` (`StartsOnLanding`); runs 1… its
  `sideChains`, in order. **The order is the wire address**, so side chains are append-only.
- **One asset per step; its class is its rule.** [`ObjectiveChain`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveChain.cs)
  orders [`ObjectiveStep`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveStep.cs) assets. The
  base holds the words (id, visor title, briefing lines, `lookAtFocus`); each subclass under
  [`Steps/`](Assets/Game/Scripts/Gameplay/Objectives/Steps/) says what finishes it and what to
  show. A chain carries its own permanent `id` (the save key) and its briefing `speaker`. The main
  chain is `Assets/Game/ScriptableObjects/Objectives/CrashSiteChain.asset` (`crash-site`, `LANDER`).
- **Steps are stateless and split across the authority line.** `TryBegin` / `IsMet` run on the
  SERVER; `TrackLocalPlayer` / `Status` / `TryGetWaypoint` / `TryGetFocus` / `TryGetBeacon` run on
  EVERY machine. Anything remembered lives in [`ObjectiveWorld`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveWorld.cs),
  whose per-step memory — including `StepStartedAt` and the quest marks (`QuestMarks.Raise` →
  `RecordMark` → `HasMark`) — is wiped by `ForgetStep` whenever **that run's** step changes or it is accepted.
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
| `ObjectiveDirector` | `persistentScene`, `[DefaultExecutionOrder(-100)]`. Holds the runs. Every machine: `TrackLocalPlayer` → `LocalPlayerFinished(run, step)`. Server: begins and judges steps in every active run; `TryStart(chainId)` accepts a side chain. `Adopt(runIndex, …)` (wire) / `Restore(records)` (save) elsewhere. `Changed(run, bool live)`. `Focused` = the run that last changed live while it still has a step, else the first active run. Story worlds only (`!VersusSession.IsActive`) |
| `ObjectiveRun` | One chain's chain + progress + `ObjectiveWorld`; `IsStarted`, `IsActive`, `Current`, `CurrentStepId` |
| `ObjectiveNetwork` | On `NetworkGameManager.prefab`, beside `ChatNetwork`. One server-write `NetworkList<ObjectiveProgress>`, entry *i* = run *i*, read on spawn for late joiners; `ReportFinishedRpc(runIndex, step)` client → server, sender taken from `RpcParams` |
| [`ObjectiveSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/ObjectiveSaveable.cs) | Global saver, key `objectives`: `chains: [{chain, step, complete, begun, started}]` — chain and step by **id**; side chains nobody accepted are left out. Reads the pre-side-chain shape (`step`/`complete`/`begun` at the top level) as the main chain |
| `LearnControlsStep` | Checklist of `lessons` (move, sprint, jump, crouch) in `Status`; only counts input while out of the seat and every cutscene; met when every player has finished |
| `UseTerminalStep` | The hull's `TerminalConsole.Occupied`; focus = first empty socket |
| `RecoverModuleStep` | Drops `SmallMotor` 90–130 m out; met by any socket fitted beyond `AuthoredMask`; waypoint = loose module, else the empty socket |
| `TryArtifactStep` | Drops `JumpingRod` 22 m off the nose; met on `UseChannel.UsedOnServer` for that asset |
| `RepairShipStep` | `ShipPartRack.IsComplete`; `Status` = `Modules fitted n/total` |
| Quest steps | `TalkToActorStep`, `ReachSiteStep`, `EscortStep`, `SpawnActorStep`, `DefeatActorStep`, `ChantStep`, `BurnStep`, `MoveAwayStep`, `DeliverItemStep`, `RewardStep` — side-chain steps that name a `QuestActor` / `QuestSite` and read `QuestMark`s from `ObjectiveWorld`. Documented in [SpriteHamletQuest](SpriteHamletQuest.md) |
| [`ObjectiveBriefing`](Assets/Game/Scripts/Presentation/Objectives/ObjectiveBriefing.cs) | Plays a step's lines through `NpcDialogPopupUI`, prefixed with its chain's `Speaker`, once the local player is present; `LookAtCutscene` to the focus. A newer step's briefing, from any chain, replaces what is still queued |
| [`VisorObjective`](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorObjective.cs) | Top-right panel for the `Focused` run: heading, title, `Status`, range; an `ALSO` line naming the other active runs' steps; NEW OBJECTIVE pop + accent pulse when the shown step changes (including a focus change); waypoint diamond pinned to the edge off screen (`VisorProjection.PinToScreen`) |
| `ObjectiveBeacon` | Placeholder light column over the `Focused` run's `TryGetBeacon` |

## Flows

**Advance (server).** `Update`, per active run: nothing until `ArrivalDirector.HasArrived` →
`TryBeginCurrent` (once per step; false = retry next frame) → `IsMet` → next step (`ForgetStep`),
and straight into its `TryBegin` so peers usually hear step and `Begun` in one change →
`Changed(run, true)` → `ObjectiveNetwork` writes that run's list entry.

**Accept (server).** `TryStart(chainId)` sets `Started` on a side chain's run and announces it; the
next `Update` begins its first step; a second accept is refused. Players accept through a character
([SpriteHamletQuest](SpriteHamletQuest.md)).

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
| Progress | `ObjectiveNetwork` `NetworkList` (one entry per run) | Server |
| "My player has done their part" | `ObjectiveNetwork.ReportFinishedRpc` | Owner reports, server counts |
| Talked to / accepted from a character | `ObjectiveNetwork.ReportTalkedRpc` / `RequestAcceptRpc` | Talker reports, server checks the sender's range |
| Placed module / artifact | Registered item prefabs via `World.Spawn` | Server |
| Artifact use | `UseChannel.UsedOnServer`, raised in `OnUseRequested` after the peer broadcast | Server |
| Briefing, visor panel and checklist, beacon | none | Local, derived |

A late joiner adopts the progress `live: false` — no briefing replay, visor correct at once. A player
who joins during the controls step must do it too; one who leaves stops counting.

## Persistence

`ObjectiveSaveable` (key `objectives`). What a step spawned persists as its own runtime entity. The
per-step memory in `ObjectiveWorld` (uses, finished players, the local checklist) is **not** saved:
a reload during the controls step asks everyone for the controls again. A step id the chain no
longer has restarts that chain with a warning (`ObjectiveDirector.Resolve`) and keeps it accepted;
a chain id the world no longer has is dropped with a warning; a run the save does not mention
restores to `default` — a side chain nobody accepted.

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
- **A chain's `id` is a save key** — renaming it loses that chain's progress on load.
- **Old saves have no `chains`.** `ObjectiveSaveable` reads their top-level `step`/`complete`/`begun`
  as the main chain; without that every pre-2026-10-07 save restarted the crash-site chain.
- **Step assets are the step's class.** Changing a step's kind means a new asset of the new class (or
  editing `m_Script`), not swapping a field.
- **Finishing the ship does not win the game** — `GameManager.WinGame` still has no caller.

## Extending

0. **A new side chain:** a chain asset (new permanent `id`, a `speaker`) appended to the director's
   `sideChains` in `persistentScene`, started by `ObjectiveDirector.TryStart(id)` on the server.
1. **A new step of an existing kind:** create it from **Create ▸ SpaceGame ▸ Objectives**, give it a
   new, permanent `id`, add it to the chain. Reordering is safe — saves resolve by id.
2. **A new kind of step:** subclass `ObjectiveStep` (or `PlacedItemStep` to drop an item), keep it
   stateless, put server work in `TryBegin`/`IsMet` only, add a `[CreateAssetMenu]`. Per-step memory
   goes in `ObjectiveWorld` and is cleared in `ForgetStep`.
3. **A lead to a settlement (issue items 5–6, not built):** `ReachSiteStep` + a `QuestSite` on the
   town (baked into `QuestSiteTable`) covers a FIXED town. A town the step PICKS is state that must be
   replicated and saved by a stable id — add it to `ObjectiveProgress` and `ObjectiveSaveable`.
4. **Verify** on a client (briefing, visor panel, checklist and beacon on the second machine) and
   after a reload (`objectives.chains` in the save JSON; no second module). The multi-chain save
   rules are covered by [ObjectiveChainsTests](Assets/Game/Editor/Tests/ObjectiveChainsTests.cs).
