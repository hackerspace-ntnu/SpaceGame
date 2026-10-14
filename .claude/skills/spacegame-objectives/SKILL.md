---
name: spacegame-objectives
description: Use when adding or editing quests, objectives or steps in SpaceGame — changing a step's visor title, status or briefing lines, reordering or removing steps, adding a step to the crash-site story, creating a new side quest, starting a quest when the crew enter a settlement or an area, after another quest finishes, or from a dialogue/pickup/UnityEvent, writing a new kind of step or quest trigger, giving a quest its own speaker, or touching the quest log (J), the active quest, the visor objective panel, the waypoint or the beacon. Also use when a quest never starts, starts on the host but not on a client, is gone after loading, briefs twice, or the visor follows the wrong quest.
---

# SpaceGame Objectives & Quests

> **Design check:** a quest is narrative and onboarding before it is code. Read `GDC-L1-UX-0001` (teach by doing,
> just in time), `GDC-L1-NARR-0005` (pace story around player-controlled time), `GDC-L1-NARR-0003` (embedded over
> delivered exposition) and `GDC-L1-LEVEL-0001` (the waypoint is the open-desert exception) in
> `docs/game-development-constitution/` before writing briefing text or choosing what a step points at. Cite the IDs.

Full system reference: `docs/AI/systems/Objectives.md` (the machinery) and `docs/AI/systems/CrashSiteQuest.md` (the
story quest's steps). Read the one you are changing **in full**, Gotchas first.

## The model in six lines

- **A quest is an `ObjectiveChain` asset**: permanent `id`, `title`, `summary`, optional `speaker`, optional `trigger`,
  and an ordered list of `ObjectiveStep` assets. The story quest (`CrashSiteChain`, id `crash-site`) begins on landing.
- **Every other quest must be in the director's catalog**: `ObjectiveDirector.quests` on the `Objectives` GameObject in
  `Assets/Game/Scenes/world/persistentScene.unity`. Not listed = never begins, never saves, never replicates.
- **Quests begin on the server**: the quest's `QuestTrigger` (asked every 0.5 s after landing), a `QuestGiver` component
  in a scene, or `ObjectiveDirector.Instance.Give(chain)` from code (safe on any machine).
- **Every begun quest advances all the time**, whether or not the player follows it. Each has its own `ObjectiveWorld`.
- **The visor follows one quest per player** (`QuestLog.Active`): the one most recently taken on, or the one picked in
  the quest log (J). That choice is local and not saved; after a load the newest quest is active.
- **Saved and replicated by ids** — the quest's `id` and each step's `id`. Never rename either once a save exists.

## Where things are

| What | Where |
| --- | --- |
| Quests (chains) and their step assets | `Assets/Game/ScriptableObjects/Objectives/` (story: `CrashSiteChain.asset`, `Steps/`) |
| Step classes | `Assets/Game/Scripts/Gameplay/Objectives/Steps/` |
| Trigger classes | `Assets/Game/Scripts/Gameplay/Objectives/Triggers/` (base: `QuestTrigger.cs`) |
| Director, runtime `Quest`, `QuestLog`, `QuestGiver`, `ObjectiveWorld` | `Assets/Game/Scripts/Gameplay/Objectives/` |
| Network / save | `ObjectiveNetwork.cs` (on `NetworkGameManager.prefab`) / `Core/Persistence/Adapters/ObjectiveSaveable.cs` |
| Briefing, beacon / visor panel / quest log screen | `Presentation/Objectives/` / `Presentation/UI/HelmetHUD/VisorObjective.cs` / `Presentation/UI/Pages/QuestLogUI.cs` |
| Settlement names for triggers | `towns:` in `Assets/Game/Settings/WorldStreamingConfigSites.asset` (the baked site catalog) |
| Tests | `Assets/Game/Editor/Tests/QuestTests.cs` |

## Decision guide

```
What do you want?
├─ Change words (visor title, status line, briefing, remark)      → Recipe 1
├─ Reorder / remove / insert steps in an existing quest           → Recipe 1
├─ Another step of a kind that already exists                     → Recipe 2
├─ A whole new quest with its own steps                           → Recipe 3, then pick how it starts:
│    ├─ the crew walk into a named settlement                     → Recipe 4 (EnterSettlementTrigger)
│    ├─ the crew reach a fixed point that is not a settlement     → Recipe 4 (EnterAreaTrigger)
│    ├─ another quest finished                                    → Recipe 4 (QuestCompletedTrigger)
│    ├─ near / on an object placed in a chunk scene, or a UnityEvent
│    │  (dialogue answer, pickup, door, poster)                   → Recipe 5 (QuestGiver)
│    └─ something in code decides                                 → Recipe 5 (Give from code)
├─ A step that finishes on a condition no step class checks yet   → Recipe 6
└─ A start condition no trigger class checks yet                  → Recipe 7
```

## Recipe 1 — edit text, reorder, remove

- Text lives on the step asset: `title` (visor line, short, imperative), `briefing` (one popup per element, said once
  when the crew reach the step), `lookAtFocus`. Status lines and remarks are in the step **class** (a serialized
  string where one exists, e.g. `ReachSettlementStep.status`), not the asset base.
- Reorder/remove by editing the quest's `steps` list. Safe: saves resolve by step `id`. A save sitting on a **removed**
  step restarts that quest from its first step, with a warning.
- Remarks (`RemarkCount`/`RemarkLines`/`IsRemarkDue`) are bits in the save: **append only**, never reorder.
- Who speaks: the quest's `speaker` (empty = `LANDER`). Give side quests their own speaker; every quest's briefings
  play, including ones the player is not following.

## Recipe 2 — another step of an existing kind

1. *Create ▸ SpaceGame ▸ Objectives ▸ <kind>* in `Assets/Game/ScriptableObjects/Objectives/` (or a sub-folder per quest).
2. Give it a **new, unique, permanent** `id` (lower-case-with-dashes), a `title` and `briefing`.
3. Add it to the quest's `steps` at the right position.

Generic kinds worth knowing: `ReachSettlementStep` (go to a named settlement; waypoint + beacon), `UseTerminalStep`,
`RecoverModuleStep` / `TryArtifactStep` (both `PlacedItemStep`: drop an item and point at it), `RepairShipStep`.

## Recipe 3 — a new quest

1. *Create ▸ SpaceGame ▸ Objectives ▸ Quest* in `Assets/Game/ScriptableObjects/Objectives/` (e.g. `ClankerQuest.asset`,
   its steps in a matching folder).
2. Fill `id` (permanent, unique across all quests, ≤ 61 bytes — it goes on the wire as `FixedString64Bytes`), `title`
   (quest log + visor heading), `summary` (one or two sentences for the log), `speaker`, `steps` (Recipe 2).
3. **Add it to `ObjectiveDirector.quests`** on the `Objectives` GameObject in `persistentScene.unity`. Forgetting this
   is the #1 failure: `StartQuest` logs *"is not in the ObjectiveDirector's quest catalog"* and a trigger on it is
   never asked.
4. Choose how it starts (Recipe 4 or 5). A quest with no `trigger` and no giver never starts.

With the Unity MCP: create the assets with `manage_scriptable_object` (`type_name`
`SpaceGame.Gameplay.Objectives.ObjectiveChain`, the step / trigger class names likewise), then open
`persistentScene` and append the asset to the director's `quests` array (`manage_components` / `execute_code` with
`SerializedObject`), and **save the scene**. Hand-editing the `.unity` YAML also works: add
`quests:\n  - {fileID: 11400000, guid: <asset guid>, type: 2}` under the `ObjectiveDirector` block (`chain:` line).

## Recipe 4 — start on a world condition (trigger asset)

1. *Create ▸ SpaceGame ▸ Objectives ▸ Triggers ▸* **Enter Settlement** / **Enter Area** / **Quest Completed**.
2. Configure it:
   - `EnterSettlementTrigger.settlement` = the town's **GameObject name** exactly as in the baked catalog
     (`grep -A3 "towns:" Assets/Game/Settings/WorldStreamingConfigSites.asset`; e.g. `ClankerSettlement`,
     `astronauts_settlement`). `margin` adds metres beyond its buildings; `fallbackRadius` is used when the catalog
     knows no radius. Works while the settlement's chunk is unloaded; nothing is added to its scene.
     A new or renamed settlement needs *Tools ▸ SpaceGame ▸ World ▸ Bake Site Catalog*; an unknown name warns once.
   - `EnterAreaTrigger`: world `centre` (X/Z) and `radius`.
   - `QuestCompletedTrigger.after`: the quest that must finish first (story or catalog).
3. Set it as the quest's `trigger`. The server asks it every `triggerCheckSeconds` (0.5 s) after landing; the first
   `true` begins the quest for the whole crew and makes it everyone's active quest.

## Recipe 5 — start from an event (QuestGiver or code)

- **Scene object:** add `QuestGiver` (set `quest`). Either wire its `Give()` to any UnityEvent (button, dialogue
  answer, pickup), or set `radius > 0` to give it when any crew member comes that close (server-judged). Safe on every
  machine: a client's `Give()` is sent to the server (`GiveQuestRpc`). Holds no state; giving twice does nothing.
- **Code:** `ObjectiveDirector.Instance?.Give(chain)` from any machine, or `StartQuest(chain)` where you already know
  you are the server (`Network.Decides`). `director.Find(chain)` tells you whether the crew have it; `IsComplete` whether
  it is done.

## Recipe 6 — a new kind of step

```csharp
using UnityEngine;

namespace SpaceGame.Gameplay.Objectives
{
    /// <summary>What finishes it, what it shows. Stateless: memory goes in ObjectiveWorld.</summary>
    [CreateAssetMenu(menuName = "SpaceGame/Objectives/Talk To Elder Step")]
    public class TalkToElderStep : ObjectiveStep
    {
        [Tooltip("…")] [SerializeField] private string elderName;   // tunables only, never runtime state

        public override bool TryBegin(ObjectiveWorld world) => true;          // SERVER, once; false = retry next frame
        public override bool IsMet(ObjectiveWorld world) => /* SERVER */ false;
        public override string Status(ObjectiveWorld world) => string.Empty;  // EVERY machine
        public override bool TryGetWaypoint(ObjectiveWorld world, out Vector3 position) { position = default; return false; }
    }
}
```

Rules that bite:
- **SERVER members** (`TryBegin`, `IsMet`, `IsRemarkDue`) may read anything; **EVERY-machine members** (`Status`,
  `TryGetWaypoint`, `TryGetFocus`, `TryGetBeacon`, `TrackLocalPlayer`) may only read replicated state.
- **No fields that change at runtime.** One asset is shared by every machine and every save. Per-step memory goes in
  `ObjectiveWorld` (add a field + clear it in `ForgetStep`); anything that must survive a reload must be derivable from
  the world (or saved by the thing it is about), because `ObjectiveWorld` is not saved.
- Spawning something: subclass `PlacedItemStep`, or call `world.Spawn(item, …)` — never `GameServices.World.Spawn`
  directly (the item would vanish at save and `Begun` stops it ever being placed again).
- A step every player must do: override `TrackLocalPlayer` (true once THIS machine's player has done it) and meet on
  `world.EveryoneFinished`.
- Something waiting on a place: `world.TryFindTown(name, out town)`, `world.NearestCrewDistance(point)`,
  `ObjectiveWorld.TownReach(town, margin, fallback)` — reuse them.

## Recipe 7 — a new kind of trigger

```csharp
[CreateAssetMenu(menuName = "SpaceGame/Objectives/Triggers/Night Falls")]
public class NightFallsTrigger : QuestTrigger
{
    public override bool ShouldStart(ObjectiveDirector director) => /* SERVER, stateless */ false;
}
```

Use `director.Lookups` (an `ObjectiveWorld`) for crew/world lookups and `director.Find(chain)` for other quests. It is
only asked on the server, so read nothing local-only (UI, the local camera, the local player's input).

## Never

- Rename a quest `id` or step `id` that has shipped in a save.
- Put a quest on the director's catalog **and** as its story `chain`, or two quests with one id (the director logs it).
- Call `QuestLog.SetActive` from gameplay: choosing what to follow is the player's (the quest log does it). Gameplay
  only `Open(entry, acquired)` / `Close`s a non-chain mission (see `WorkshopMissionObjective`).
- Add a `NetworkVariable`/RPC per quest. `ObjectiveNetwork`'s one list carries every quest.

## Verify (all of it — CLAUDE.md non-negotiables)

1. Compiles (Unity console clean), and `SpaceGame.Tests.QuestTests` plus any step's own tests pass.
2. **Host:** the quest begins where intended, the visor switches to it (NEW OBJECTIVE pop, quest name as heading), J
   lists it, clicking another quest switches the visor and back.
3. **Client:** the same quest begins on the second machine with no action there, briefings play there, a `QuestGiver`
   fired on the client starts it for everyone.
4. **Reload:** `objectives.quests[]` in the save JSON has the quest by `id`; it comes back at the same step, nothing it
   spawned appears twice, no briefing replays.
5. Docs: update `docs/AI/systems/Objectives.md` (machinery) or `CrashSiteQuest.md` (story steps); a new side quest
   with its own content gets a row in Objectives' or its own doc, then `python3 tools/docs_check.py --index`.
