---
system: Objectives
layer: presentation
summary: The crew's quests: the crash-site story and triggered side quests, steps, quest log, visor, waypoint
paths:
  - Assets/Game/Scripts/Gameplay/Objectives/
  - Assets/Game/Scripts/Presentation/Objectives/
  - Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorObjective.cs
  - Assets/Game/Scripts/Presentation/UI/Pages/QuestLogUI.cs
  - Assets/Game/ScriptableObjects/Objectives/
  - Assets/Game/Editor/Tests/QuestTests.cs
  - Assets/Game/Editor/Tests/ObjectiveChainsTests.cs
  - Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorProjection.cs
  - Assets/Game/Scripts/Core/Persistence/Adapters/ObjectiveSaveable.cs
symptoms:
  - "the quest is stuck on a step that started another quest"
  - "the objective line never appears on the visor after the crash"
  - "the lander's briefing plays again every time the world is loaded"
  - "a client sees the objective but never hears the lander's briefing"
  - "the lander says the same remark again after a reload"
  - "a side quest never starts when I walk into the settlement"
  - "a side quest starts on the host but a client never sees it"
  - "a side quest is gone after loading the world"
  - "the visor shows the wrong quest after I picked up a new one"
  - "pressing J does nothing"
  - "a villager offers a quest the crew have already taken on"
  - "talking to the elder before the step that asks for it finished the step early"
reads_with: [CrashSiteQuest, Visor, UI, Multiplayer, Persistence, WorkshopMission, Expeditions, SpriteHamletQuest]
updated: 2026-10-26
---

# Objectives

The crew's **quests**. A quest is an ordered chain of steps the whole crew works through together; any number can be
running at once, each keeping its own progress, and the visor follows one of them (the **active** quest). The story
quest begins on landing; every other quest begins when its trigger fires (the crew walk into a settlement, another
quest finishes) or something in the world gives it. The quest most recently taken on becomes active; the quest log
(**J**) lists them all and lets each player follow another.

**Scope:** the quest machinery — quests, steps, triggers, quest log, presentation. The story's steps are
[CrashSiteQuest](CrashSiteQuest.md); the dialog popup is [InteractionSystem](InteractionSystem.md); the visor is [Visor](Visor.md).

## Model

- **A quest is an `ObjectiveChain` asset.** [`ObjectiveChain`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveChain.cs)
  carries the quest's permanent `id` (save + wire key, at most 61 UTF-8 bytes), `title` and `summary` (quest log,
  visor heading), optional `speaker` (who reads its briefings; empty = the lander) and optional `trigger`. The story
  quest is the director's `chain` (`CrashSiteChain`, id `crash-site`); every other quest must be in the director's
  `quests` catalog to begin, save or replicate.
- **One shared place per quest.** A begun quest is a runtime [`Quest`](Assets/Game/Scripts/Gameplay/Objectives/Quest.cs):
  its chain, its `Order` (0 = story, then 1, 2, … in the order taken on), its own `ObjectiveWorld` and its
  [`ObjectiveProgress`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveProgress.cs) — step index, `Begun`,
  `Remarks`. The crew share it; there is no per-player progress. **Every begun quest advances every frame**, active or
  not.
- **Which quest the visor follows is per machine.** [`QuestLog`](Assets/Game/Scripts/Gameplay/Objectives/QuestLog.cs)
  (static) lists the `IQuestEntry`s open on this machine: every director `Quest`, plus world missions such as
  [WorkshopMission](WorkshopMission.md). `Open(entry, acquired: true)` — a quest taken on just now, in play — makes it
  the chosen one; restored, joined or streamed-in quests are listed quietly. `Active` = the chosen quest while it is
  open and unfinished, else the newest open unfinished one. `SetActive` is the quest log's click. Not saved: after a
  load the newest quest is active.
- **Quests begin on the server.** A [`QuestTrigger`](Assets/Game/Scripts/Gameplay/Objectives/QuestTrigger.cs) asset on
  the quest (stateless, SERVER, asked every `triggerCheckSeconds` once landed), or a
  [`QuestGiver`](Assets/Game/Scripts/Gameplay/Objectives/QuestGiver.cs) component in a scene (`Give()` from any
  UnityEvent on any machine, or a `radius` judged on the server), a `StartQuestStep` in another quest, or
  `ObjectiveDirector.Give/StartQuest` from code.
- **One asset per step; its class is its rule.** [`ObjectiveStep`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveStep.cs)
  holds the words (id, visor title, briefing lines, `lookAtFocus`); each subclass under
  [`Steps/`](Assets/Game/Scripts/Gameplay/Objectives/Steps/) says what finishes it and what to show.
- **Steps are stateless and split across the authority line.** `TryBegin` / `IsMet` / `IsRemarkDue` run on the
  SERVER; `TrackLocalPlayer` / `Status` / `TryGetWaypoint` / `TryGetFocus` / `TryGetBeacon` on EVERY machine.
  Memory lives in the quest's [`ObjectiveWorld`](Assets/Game/Scripts/Gameplay/Objectives/ObjectiveWorld.cs), wiped by
  `ForgetStep` whenever that quest's step changes.
- **A step every player must do themselves** overrides `TrackLocalPlayer` and meets on `world.EveryoneFinished` (one
  only one player can do — one animal to ride — on `world.AnyoneFinished`); `LearnControlsStep.CanPractise` says where.
- **What a step spawns is an ordinary pickup.** [`PlacedItemStep`](Assets/Game/Scripts/Gameplay/Objectives/Steps/PlacedItemStep.cs)
  drops it via `GameServices.World.Spawn` in `TryBegin` and finds it again through `ScannerRegistry`.
- **Remarks: one-off lines partway through a step.** `RemarkCount`, `RemarkLines(i)`, `IsRemarkDue(world, i)`; the
  quest sets bit i of `ObjectiveProgress.Remarks` (replicated, saved, cleared per step) and `ObjectiveBriefing` plays
  it once; never replayed on restore or join. Indices are the save format: append only (`FitTransmitterStep`).
- **Guidance is explicit, with an environmental half.** Visor waypoint (`GDC-L1-LEVEL-0001`, a 4 km desert) plus a
  light-column beacon in the world.

## Key types

| Type | Role |
| --- | --- |
| `ObjectiveDirector` | `persistentScene`, `[DefaultExecutionOrder(-100)]`. `chain` (story) + `quests` (catalog) + `triggerCheckSeconds`. Holds every begun `Quest` (`Quests`, `Story`, `Find`, `FindChain`). Every machine: `Quest.TrackLocalPlayer` → `LocalPlayerFinished(quest, step)`; lists its quests on `QuestLog`. Server, once landed: asks triggers, `Quest.Advance` for every quest. `Give` (any machine) / `StartQuest` (server). `Adopt(list, live)` (wire) / `Restore(saved)` (save). Events `Started(quest, live)`, `Changed(quest, live)`, `GiveRequested`. Validates ids at Awake. Story worlds only (`!VersusSession.IsActive`) |
| `Quest` | One begun quest: `Chain`, `Order`, `World`, `Progress`, `Current`, `Objective` (`ITrackedObjective`, new object per step), `IsComplete`. `Advance`/`RecordFinished` SERVER; `TrackLocalPlayer`/`Adopt`/`Restore`/`ToSaved` everywhere |
| `QuestLog` / `IQuestEntry` / `ITrackedObjective` | Per machine: open quests, `Active`, `Open(entry, acquired)`, `Close`, `SetActive`, `Changed` |
| `QuestTrigger` | Abstract SO, `ShouldStart(director)` SERVER. `EnterSettlementTrigger` (a town in the baked `WorldSiteCatalog.towns` by GameObject name; reach = its radius or `fallbackRadius`, + `margin`), `EnterAreaTrigger` (fixed centre + radius), `QuestCompletedTrigger` (another quest finished). Under [`Triggers/`](Assets/Game/Scripts/Gameplay/Objectives/Triggers/) |
| `QuestGiver` | Scene component: `Give()` for UnityEvents (a client's goes to the server), or `radius` > 0 to give on approach (server) |
| `ObjectiveNetwork` | On `NetworkGameManager.prefab`, beside `ChatNetwork`. One server-write `NetworkList<QuestState>` (id, order, progress; one entry per begun quest, story first), read on spawn for late joiners; `ReportFinishedRpc(quest, step)` and `GiveQuestRpc(quest)` client → server, sender taken from `RpcParams` |
| [`ObjectiveSaveable`](Assets/Game/Scripts/Core/Persistence/Adapters/ObjectiveSaveable.cs) | Global saver, key `objectives`: the story quest's step **id**, `complete`, `begun`, `remarks` top-level (the pre-side-quest format, unchanged), plus `quests[]` = `{id, order, step, complete, begun, remarks}` for every other quest |
| `ReachSettlementStep` | Generic: go to a town in the baked town list by name (`ObjectiveWorld.TryFindTown`); waypoint + beacon on it; met when any crew member is within its reach + `arrivalMargin` |
| [`StartQuestStep`](Assets/Game/Scripts/Gameplay/Objectives/Steps/StartQuestStep.cs) | Gives a catalog `quest` in `TryBegin` (`ObjectiveWorld.StartQuest`). `completion`: `HandOff` = met at once, both quests run on; `WaitForQuest` = met when that quest `IsComplete` (a sub-quest), meanwhile `Status` = `waitingStatus` and waypoint/beacon forwarded from the sub-quest's current step. A refused or missing quest counts as done, never blocks |
| [`ObjectiveBriefing`](Assets/Game/Scripts/Presentation/Objectives/ObjectiveBriefing.cs) | Plays every quest's step lines through `NpcDialogPopupUI`, prefixed with the quest's `speaker` (else `LANDER`), once the local player is present; `LookAtCutscene` to the focus. Remembers per quest what was heard. Said through the character a [`QuestVoice`](Assets/Game/Scripts/Gameplay/Objectives/QuestVoice.cs) names for the quest when one is loaded and alive here (mouth, bubbles), else on the popup alone. Catalog today: `raxy-ranch` ([Ranch.md](Ranch.md)) |
| [`VisorObjective`](Assets/Game/Scripts/Presentation/UI/HelmetHUD/VisorObjective.cs) | Top-right panel: heading (the active quest's name, upper case), title, `Status`, range; NEW OBJECTIVE pop + accent pulse on a new step or a switch of quest; waypoint diamond pinned to the edge off screen (`VisorProjection.PinToScreen`). Shows `QuestLog.Active.Objective` |
| `ObjectiveBeacon` | Placeholder light column over the **active** quest's `TryGetBeacon` only |
| [`QuestLogUI`](Assets/Game/Scripts/Presentation/UI/Pages/QuestLogUI.cs) | J (`UI/QuestLog` in `InputSystem_Actions`): every open quest newest first with its objective and summary, FOLLOWING on the active one, click another to follow it; finished quests struck through. See [UI.md](UI.md) |

## Flows

**Begin a quest (server).** Every `triggerCheckSeconds` once landed, each catalog quest not yet begun with a
`trigger` is asked `ShouldStart`; a `QuestGiver` or code calls `Give` (a client's becomes `GiveQuestRpc`). `StartQuest`
refuses a quest already begun or not in the catalog (loudly) → a `Quest` with the next `Order` →
`QuestLog.Open(acquired: true)` (the host's visor switches to it) → `Started(quest, true)` → `ObjectiveNetwork`
appends it to the list → every client adopts it live and its visor switches too. Its first step begins next frame.

**Advance (server).** `Update`: nothing until `ArrivalDirector.HasArrived`; then, for **every** begun quest,
`TryBeginCurrent` (once per step; false = retry next frame) → `IsMet` → next step (`ForgetStep`), and straight into
its `TryBegin` so peers usually hear step and `Begun` in one change → `Changed(quest, true)` →
`ObjectiveNetwork` rewrites that quest's entry.

**Report (every machine, own player).** `step.TrackLocalPlayer` each frame; the first true raises
`LocalPlayerFinished(quest, step)` → host records directly, a client sends `ReportFinishedRpc`; a report for a step
the crew have left is dropped.

**Follow (every machine).** The visor, beacon and quest log read `QuestLog.Active`. A quest taken on in play becomes
active; a chosen quest that finishes or closes hands over to the newest unfinished one; J → click picks another.

**Brief (every machine).** `ObjectiveBriefing` queues a step's lines when `Begun` flips for a new step of any quest
with `live == true`. Lines wait for the player to be spawned, out of every cutscene for `settleSeconds`, and for the
popup; the first line of a `lookAtFocus` step plays under the look, the rest after it.

## Multiplayer

| What | Carrier | Authority |
| --- | --- | --- |
| Begun quests and their progress | `ObjectiveNetwork` `NetworkList<QuestState>` | Server |
| "Give us this quest" (a client's `QuestGiver`) | `ObjectiveNetwork.GiveQuestRpc` | Server begins it |
| Which quest each player follows | none (`QuestLog`) | Local, per player |
| "My player has done their part" | `ObjectiveNetwork.ReportFinishedRpc` | Owner reports, server counts |
| Briefing, visor panel and checklist, beacon | none | Local, derived |

A late joiner adopts every quest `live: false` — no briefing replay, visor correct at once (newest quest active). A
client whose build lacks a quest the server runs logs an error and skips it.

## Persistence

`ObjectiveSaveable` (key `objectives`): the story quest in the top-level fields (a save from before side quests loads
unchanged), every other quest in `quests[]` by **id** with its `order`. An unknown quest id is dropped with a warning;
an unknown step id restarts that quest (`ObjectiveDirector.Resolve`); quests begun this session but absent from the
save are dropped. Not saved: which quest a player follows (newest is active after a load) and per-step memory in
`ObjectiveWorld`. What a step spawned persists as its own runtime entity. Inserting steps is safe (by id).

## Gotchas

- **A quest not in the director's catalog never begins, saves or replicates.** `StartQuest` says so loudly; a trigger
  on a quest outside the catalog is simply never asked. Add every side quest to `ObjectiveDirector.quests` in
  `persistentScene`.
- **Quest ids are the save and wire key.** Never rename one once a save exists (the quest is dropped on load); at most
  61 UTF-8 bytes (`FixedString64Bytes`); unique across the story and the catalog. The director validates at Awake.
- **`EnterSettlementTrigger` / `ReachSettlementStep` match the baked town list by GameObject name**
  (`WorldSiteCatalog.towns[].name`, e.g. `ClankerSettlement`, `astronauts_settlement`). Rename or add a settlement and
  re-run *Tools/SpaceGame/World/Bake Site Catalog*; an unknown name warns once (`ObjectiveWorld.TryFindTown`) and the
  quest waits forever.
- **Triggers are asked only after landing, only on the server**: one reading local-only state breaks multiplayer.
- **Every quest briefs.** A background quest's step change plays its lines even while another quest is followed; give
  side quests their own `speaker` so the player can tell who is talking.
- **A `HandOff` `StartQuestStep` is never seen or briefed** — begun and met in one frame; brief on the new quest's first
  step. A quest starting **itself** is an Awake error; a cycle (A waits for B, B waits for A) deadlocks silently.
- **A finished sub-quest does not hand the visor back to its parent**: `Active` falls to the newest unfinished quest.
- **Nothing may be judged before the landing** (bodies are seated a frame after spawning): the director waits for `HasArrived`.
- **`Begun` is saved on purpose.** Without it a load spawns a step's item again (or starts a quest again), or never.
- **`World.Spawn` alone does not make a pickup persist.** Use `ObjectiveWorld.Spawn`, which stamps
  `SaveableEntity.EnsureRuntime(obj, item.ID)`; otherwise it is dropped at save and `Begun` never places it again.
- **Step assets are the step's class.** Changing a step's kind means a new asset, not swapping a field.

## Extending

**Step-by-step recipes (new quest, new trigger, new step kind) live in the `spacegame-objectives` skill.**

0. **A new quest:** *Create ▸ SpaceGame ▸ Objectives ▸ Quest* (permanent `id`, `title`, `summary`, steps, optional
   `trigger`), add it to `ObjectiveDirector.quests` in `persistentScene`; or give it with a `QuestGiver` / `StartQuestStep`.
1. **A new step of an existing kind:** **Create ▸ SpaceGame ▸ Objectives**, a new permanent `id`, add it to the quest.
2. **A new kind of step / trigger:** subclass `ObjectiveStep` (or `PlacedItemStep`) / `QuestTrigger`, stateless, server
   work in `TryBegin`/`IsMet`/`ShouldStart` only, `[CreateAssetMenu]`. Per-step memory in `ObjectiveWorld` + `ForgetStep`.
3. **Verify** on a client (quest begins, briefing, visor, J log on the second machine) and after a reload
   (`objectives.quests` in the save JSON; nothing spawned twice). `QuestTests` covers the log, restore, saver and `StartQuestStep`.
