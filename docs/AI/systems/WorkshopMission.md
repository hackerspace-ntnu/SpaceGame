---
system: WorkshopMission
layer: world
summary: "The workshop's story: a Shellback at the gate, a thieves' camp, then the vault's core craned onto the ship"
paths:
  - Assets/Game/Scripts/World/WorkshopMission/
  - Assets/Game/Scripts/Core/Persistence/Adapters/WorkshopMissionSaveable.cs
  - Assets/Game/Scripts/Gameplay/Objectives/QuestLog.cs
  - Assets/Game/Scripts/Items/Core/LooseItems.cs
  - Assets/Game/Editor/Workshop/WorkshopMissionBuilder.cs
  - Assets/Game/Editor/Workshop/CarriedGearItems.cs
  - Assets/Game/Editor/Workshop/GearThiefCampPlacer.cs
  - Assets/Game/Editor/Tests/WorkshopMissionTests.cs
  - Assets/Game/Prefabs/agents/Characters/Shellback/Shellback_Gatekeeper.prefab
  - Assets/Game/Prefabs/agents/Characters/Shellback/Shellback_WorkshopFamily.prefab
  - Assets/Game/Prefabs/agents/Characters/Shellback/Shellback_WorkshopFamily_Mechanic.prefab
  - Assets/Game/Prefabs/agents/Caravan/Raider_GearThief.prefab
  - Assets/Game/Prefabs/agents/Caravan/Raider_GearCarrier.prefab
  - Assets/Game/Prefabs/Items/Workshop/HeartGear.prefab
  - Assets/Game/Prefabs/Items/Workshop/VaultReactorCore.prefab
  - Assets/Game/Resources/Items/Workshop/VaultReactorCore.asset
  - Assets/Game/ScriptableObjects/Workshop/GearThiefCampSite.asset
symptoms:
  - "there is nobody outside the workshop gate"
  - "the Shellback at the gate says the same thing whatever I do"
  - "the gate opened but the Shellback stays outside / nobody comes to meet me"
  - "only the Shellback from outside is in the main hall"
  - "the Shellback says nothing until I talk to him"
  - "the residents stand at the ledge rail over the main room instead of coming down"
  - "I stand in the middle of the main room and no mission is given"
  - "the gear thieves never appear"
  - "I reached the thieves' camp and nobody is there"
  - "the visor points at an empty patch of sand during the hunt"
  - "I examined a clue and the objective did not move on"
  - "the thief with the gear runs away and I cannot catch him"
  - "I netted the runner and he still has the gear"
  - "a second band of thieves appeared after loading the world"
  - "I killed the thieves and there is no Heart Gear"
  - "I brought the Heart Gear back and nothing happened"
  - "the visor shows the crash-site objective instead of the workshop's"
  - "I killed the last thief and the Heart Gear fell under the terrain"
  - "I brought the Heart Gear home and the Shellbacks said nothing about the vault"
  - "the vault is empty: there is no reactor core on the cradle"
  - "a second reactor core lies in the vault after loading the world"
  - "I lowered the core onto the ship and the story did not end"
  - "an old save with the story over now asks me to find the vault"
reads_with: [Workshop, GearDoor, Residents, Objectives, AgentSystem, Outposts, Artifacts, Multiplayer, Persistence, PlayerShip]
updated: 2026-10-14
---

# WorkshopMission

Raiders broke into the workshop, smashed the gate's gear train on their way out ([GearDoor.md](GearDoor.md)) and
took the **Heart Gear**. One Shellback got out before the gate jammed. He paces in front of it, and the first time a
player comes near he shouts for them and tells them what happened unasked; talked to after that, he gives hints. When
the gate opens he says so at once and runs in to the middle of the main room, where his family (or, in an outpost,
the workshop's residents) meet the player. Reaching him starts the mission: get the Heart Gear back. The thieves
live at their camp, a scavenger outpost ~730 m south of the gate (`Chunk_5_1`, a 757 m walk), with the gear lying
on the ground there; a trail of five things they dropped leads from the gate to it. Sneak in and lift the gear, or fight; once
they spot you, the carrier snatches it up and runs. Knock him down and he drops it. The gear brought back to the
middle of the main room opens the last chapter: in thanks the Shellbacks offer the sealed reactor core in their vault, at
the far end of the turning rooms (the old engine hall, behind a round door with a five-cog lock,
[GearDoor.md](GearDoor.md)). The player fetches it, carries it up to the hoist's console on the third-floor balcony, hangs
it on the hook and lowers it onto their ship parked in the pit; letting go over the ship's empty reactor mount fits it
([Workshop.md](Workshop.md), the crane) and ends the story.

## Model

| Idea | Mechanism |
|---|---|
| One story, five phases | `WorkshopMissionPhase`: `Locked` (gate shut) → `Gathering` (gate solved) → `Hunting` (a player came within `meetRadius` of `Meeting` once the Shellback stood there) → `Vault` (the gear is home) → `Repaired` (the crane fitted the core). Saved by name. `Done` is only in older saves (the gear home, the story over) and is read back as `Vault` |
| The server decides, every machine presents | `WorkshopMission` (on `Workshop/Mission`, built into `Workshop.prefab`) thinks every `thinkSeconds` where `Network.Decides`, and announces phase, "a player carries the gear" and the camp in one message. The Shellback's lines, the briefing, the thanks and the visor objective are worked out on each machine from that state |
| The Shellback is the state's to rebuild | `Shellback_Gatekeeper` (a variant of `Shellback_Villager` + `WorkshopGatekeeper`) is spawned by the mission through `NpcSpawn.Create` (saved by nobody) at `GateStand` while `Locked`, at `Meeting` after. Despawned with the workshop. Outside any `Settlement`, so his resident parts stand down (`Resident.Settlement` is null) and his `AgentGoal` is the mission's to write |
| Two kinds of story Shellback | `WorkshopShellback` (abstract): `Stand(pose, greet)`, `PutAt`, the talk answer, `Turn` (a line set in turn, restarted per phase), `Speak` (lines said unasked, queued on every machine, popup within `hearingRadius`). `WorkshopGatekeeper` and `WorkshopFamilyMember` derive from it |
| The family shut inside | `WorkshopFamily` on the mission, for a workshop **with no residents** (the bare prefab in `Chunk_4_3`): `members` (two `Shellback_WorkshopFamily` and one `_Mechanic`, variants with `WorkshopFamilyMember`) appear through `NpcSpawn` in a row behind `Meeting` the moment the gate is solved (the player is out at the gate), stand there through the briefing, then keep to spots `spotRadius` round the hall between `firstSpotBearing` and `lastSpotBearing`. Saved by nobody; rebuilt from the phase; despawned with the workshop |
| He runs in | While `Gathering`, `Stand(meet, greet, run: true)`: the goal's `Run` makes `GoalTravelModule` run rather than walk ([AgentSystem.md](AgentSystem.md)). Every other `Stand` walks |
| Frantic, not idle | `WorkshopGatekeeper.Plead`: paces `paceRadius` either side of his spot at `paceSpeed`, and turns to a player within `callRadius` (`InteractionFocusModule.FocusOn`). His `WatchModule`, `IdleLookAroundModule` and `ChatterModule` are switched off: the watcher would freeze his walk whenever a player is near |
| He says what fits the moment | Unasked: the call-out and the whole plea the first time the local player comes within `callRadius`; `opened` the moment the gate is solved, the briefing at the meeting and the thanks at the end, each on every machine that hears the phase change **live**. Asked: the next queued line if he is mid-story, else hints (`{gears}` = `GearDoor.LooseGears`), "come in", hunting or "bring it here" lines, afterwards. Family members answer with their own sets |
| The residents meet you | `WorkshopGathering.Hold`: every present resident of the outpost's society gets a Scripted override to its place in a row (`ExpeditionRules.MusterRow`) `rowBack` behind `Meeting`, and faces the nearest player once there. One who cannot walk there (their island is the upper levels) is moved (`NetworkedTeleport.Move`) while no player sees either end, or after `movePatience`. `dismissAfterSeconds` after the mission is given they are let go, and those who cannot walk home are moved to the heart the same way (`Return`) |
| The camp is an outpost, a long way off | `GearThiefCamp` on an `Outpost` of `Outpost_Scavenger_01` with nobody living at it, placed by **Place Gear Thief Camp** (`GearThiefCampPlacer`), which also writes where it is into `GearThiefCampSite.asset`. The mission reads the camp's position from that asset (`campSite`), so the trail and the visor know it on every machine while its chunk is not loaded; the band is spawned once the loaded `GearThiefCamp` within `campMatchRadius` of the site exists. Its posts are the outpost's own `SettlementSpot`s (the tower watch among them), nearest the gear first; the gear rests on its `PropRest` nearest the middle |
| The thieves are ordinary saved entities | `GearThiefBand.TrySpawn`: the carrier (`Raider_GearCarrier`) on the post beside the gear, `count - 1` `Raider_GearThief` (a `BountyHunter` variant, Outlaw, with a bag, an `EntityLootTable` that drops it and a `GearThief` marker) on the others, each `Guard`ing its post through `AgentGoal`; the gear as a saved pickup on the rest. A chosen camp is saved: that is what stops a second band |
| Sneak or fight, then a runner | `GearThiefBand.Tick` (server): while no thief has a target the gear just lies there, for a player who got in unseen. Once any `GearThief.IsAlarmed`, the carrier snatches a gear within `snatchReach` into bag slot 1 and `Run`s (`FleeModule.Alarm`; the module's `triggerRadius` is 0.1 so nothing but the alarm starts it, `safeRadius` 70, priority Override); he runs again whenever a player is within `runFrom`. `IsDown` (`AgentRagdoll.IsHeldOrDown`: knocked down, netted, held) makes him `Drop` it; dead, his loot table does |
| A trail to follow | `WorkshopClueTrail`: `Count` (5) clues at `alongTrail` (6/25/45/65/85 %) of the way from `GateStand` to the camp, swayed up to `wander` (25 m) to the side, more the further out (the first lies on the straight line, clear of the mountain beside the gate), sampled on the world NavMesh, so every machine lays the same trail with nothing sent. Each `WorkshopClue` is local scenery: a `gear_xs` model, on the item scanner (`IScanTarget`, Signal, "Raiders' trail") until examined, and an `IInteractable` "Examine" that shows its `findings` line at once and asks the server (`WorkshopMissionExamine`), which counts it if the examiner stands within `examineRange + examineSlack` |
| One message says it all | `WorkshopMissionState`: phase, carried, band spawned, carrier has the gear, clues found, packed into NetArg.A with the live flag (`WithLive`, `FromWire`) |
| The gear comes home | Server, every think while hunting: a player whose hotbar holds the gear within `homeRadius` of `Meeting` has it taken (`IPlayerInventory.TryRemoveItem`), or a loose copy lying there is despawned → `Vault`, live: the gatekeeper speaks `thanks` then `vaultOffer` (find the vault behind the turning rooms, turn the main room to open the hidden hallways, bring the core to the crane console on the third floor) |
| The core lies in the vault from the start | Server, on its first think in any phase: `LayCore` spawns `VaultReactorCore` on `CoreRest` (the model's `Vault_CoreRest`, on the cradle), a saved pickup like the Heart Gear (`SaveableEntity.EnsureRuntime`), once per world (`coreSpawned`, saved). So the vault is never found empty, even before the Shellbacks offer it. It is a plain carried item, not a `ShipPartItem`: it cannot be pointed into a socket by hand, only lowered on by the hoist |
| The crane ends it | The `WorkshopCrane`'s `Installed` event (server) → `Repaired`, live: `repaired` lines. Fitted before the gear came home (the vault can be opened any time) it is remembered as `coreFitted` (saved), and the `Vault` phase then ends at once |
| A quest in the quest log | `WorkshopMissionObjective` is an `IQuestEntry` ("The Heart Gear") on this machine's `QuestLog` ([Objectives.md](Objectives.md)) while `Gathering`/`Hunting`/`Vault`, and while `Locked` once this machine's player heard the plea; closed at `Repaired` and when the workshop streams out. `Track(acquired)`: opened by the plea or a live phase change it becomes the **active** quest (the visor follows it); opened by streaming back in or a late join it is listed without taking the visor. The player can switch to any other quest with J. Steps: open the gate (gate) → meet (meeting) → find the trail (gate; only with no camp placed) → follow it (a search area `searchRadius * 0.6` off the next clue, never the clue: the scanner finds it) → raid the camp (camp) → catch the runner (him) → pick up the gear (it, once dropped outside the camp) → bring it home (meeting) → turn the main room (its hand cog, `MainWheel`, while the lock is shut and `MainRoomFacesLane`: the room within `laneSlack` 20 degrees of the lane, where no hidden hallway is open) → find the vault (its door, while its lock is shut; status counts its loose cogs) → take the core (it, on the cradle) or pick it up (it, dropped elsewhere) → take it to the crane console (the console) → lower it onto the ship (the nearest empty reactor mount), or bring the ship in (meeting) when no ship has one |

## Key types

| Type | Role |
|---|---|
| `WorkshopMission` | The phases, the messages, the server's thinking. `Nearest(position)` (static), `Phase`, `MainWheel`, `MainRoomFacesLane`, `GearCarried`, `CarrierHasGear`, `CluesFound`, `BandSpawned`, `TryGetCamp` (from the site asset), `Changed(bool live)` (live = a new phase in play), `HearPlea()` and `RequestExamine` (local), `Restore(phase, bandSpawned, clues)` |
| `WorkshopShellback` | Base of both: standing, facing, `IDialogResponder`, the unasked-speech queue |
| `WorkshopGatekeeper` | The one who got out: `Plead` (server), the plea, hints and the story's spoken beats. All lines are serialized fields |
| `WorkshopFamilyMember` / `WorkshopFamily` | One relative's lines; the family's spawning and places (server) |
| `WorkshopGathering` | Serializable on the mission: `Hold`, `Dismiss`, `Return` (server) |
| `GearThiefBand` | Serializable on the mission: `TrySpawn`, `Tick` (server); `Carrier()` (any machine) |
| `GearThief` / `GearThiefCamp` / `GearThiefCampSite` | On each thief: `IsCarrier`, `Guard`, `Holds`, `Take`, `Drop`, `Run`, `IsAlarmed`, `IsDown`; on the camp outpost: `GearRest`, `Posts()`, `Nearest`; the asset: `Position`, `ChunkScenePath`, `Record` (editor) |
| `WorkshopClueTrail` / `WorkshopClue` | The trail's places and lines; one local clue |
| `WorkshopMissionState` | The packed state |
| `WorkshopMissionObjective` | `ITrackedObjective` and `IQuestEntry` over the mission's state |
| `QuestLog` / `IQuestEntry` / `ITrackedObjective` (Gameplay/Objectives) | The quests open on this machine and the one the visor follows; see [Objectives.md](Objectives.md) |
| `LooseItems` (Items/Core) | `TryFind(item, near, radius, …)`: the nearest loose copy, not one in a hand. `ObjectiveWorld.TryFindLoose` delegates to it |
| `WorkshopMissionSaveable` | key `"workshopMission"`: `phase` by name, `bandSpawned`, `clues`, `coreSpawned`, `coreFitted`; null at the start (before the first think lays the core) |
| `WorkshopMissionBuilder` (editor) | Called by `WorkshopPrefabBuilder.Compose` after the doors and the crane: `Mission`, `GateStand` (18 m out from the gate's face, checked clear of the pile and both leaves' sweep), `Meeting` (first open floor from the main room's centre towards the lane), `CoreRest` (the `Vault_CoreRest` mark), the vault door, crane, console and core from the builders before it, `mainWheel` (the hand cog with id `WorkshopLayout.MainWheelId`), and the three assets |
| `CarriedGearItems` (editor) | A carried gear item (asset, prefab, icon), and `StampOnDisk` (network hash and save id on disk, variant overrides included). Shared with `GearDoorBuilder` |

## Flows

- **Build.** **Rebuild Workshop Prefab** logs `mission: meeting at (0, 0.4, 0), Shellback 18 m out from the gate`; any `MISSION:` text after it is a placement problem. The two NPC variants and the item are made once and kept.
- **Locked → Gathering.** Server sees `GearDoor.IsSolved`. The Shellback waits for `GearDoor.StandsOpen` (the leaves done swinging), then runs the ~110 m of lane in; after `walkInPatience` he is put there.
- **Gathering → Hunting.** The Shellback has arrived (or is dead) and a player is within `meetRadius`. Live: every
  machine queues the briefing and lays the trail at once. The band and the gear are spawned on the first think after
  the camp's chunk streams in, as a player nears it.
- **The camp.** **Place Gear Thief Camp** opens `Chunk_5_1`, makes or regenerates the camp outpost at (2565, -95), saves
  the scene and records the site; a camp recorded in another chunk is taken out of it first (`Outpost.Clear`, its ground
  restored). Then re-bake the world NavMesh.
- **Hunting → Vault.** As above; live, so the thanks and the offer are spoken.
- **Vault → Repaired.** Turn the main room to a J tunnel and walk the turning rooms to the old engine hall (`Final`); open
  its door's cog lock; take the core off the cradle; carry it out, up to the third-floor balcony; drive the hook beside
  the balcony's loading gap (`Crane_LoadingSpot`) and hang the core on (or drop it under the hook and grab it); drive it
  over the ship and let go over the green mount. Every machine's objective follows the crane's replicated state.

## Multiplayer

On the building's channel: `WorkshopMissionAsk` 135 (asker → server, answered with the state to all) and
`WorkshopMissionState` 136 (server → all; A = `WorkshopMissionState.WithLive`), `WorkshopMissionExamine` 137
(examiner → server; A = clue). Talk is answered on the presser's machine, like `DialogInteraction`; clues are local
scenery on every machine. The Shellbacks and the thieves are network prefabs
(registered by Rebuild), and so is the core. **A workshop with no networked wrapper above it** (the bare `Workshop.prefab`, as in
`Chunk_4_3`) runs the mission on the host only: clients see the NPCs but never hear the state. Place
`WorkshopOutpost.prefab` and Generate for multiplayer. **Not run on a client.**

## Persistence

`WorkshopMissionSaveable` through `SaveablePolicy.EnsureWorldInteractables` (the persistent root with a mission below
it). The thieves, the Heart Gear and the core (lying in the vault, at the camp or dropped) are their own records; the core
on the hoist's hook is the workshop saver's (`crane.loaded`, [Workshop.md](Workshop.md)); the carried and carrier flags
are worked out again; whether the band was spawned, the clues found, the core laid and the core fitted are saved; the camp's position is the site
asset's, never saved; the Shellbacks are rebuilt where the phase has them. A restore moves the story on silently (no briefing, no thanks). Round trip
covered by `WorkshopMissionTests`; **not run through a save file and a reload** (`WorkshopTest` cannot save).

## Gotchas

- **Edit the builder, never `Workshop.prefab`.** The mission is part of Rebuild.
- **The Shellback is NOT saved and NOT a resident.** Parented under an `Outpost` he would become a fifth resident
  (the society gathers `GetComponentsInChildren<Resident>`). Killed, he is back after a reload.
- **Never write a resident's `AgentGoal`**: the routine overwrites it every half second. Use `SetOverride(Scripted)`,
  and call `ComeOnstage()` first or an offstage resident ignores it.
- **The residents' island does not reach the pit floor**, and the routine's own settle-teleport runs only once and only
  unwatched: a resident left in the pit while watched stays at the rail for ever. `WorkshopGathering.Return` is what
  brings them home.
- **A restore solves the gate instantly**, so `IsSolved` alone cannot tell "just opened" from "loaded open"; the saved
  phase is what keeps the briefing from replaying. A save from before this system loads `Locked` with the gate open
  and goes straight to `Gathering`.
- **A variant made from a scene instance carries that instance's `InScenePlacedSourceGlobalObjectIdHash`**, and stores
  its own hash and prefab id as overrides (`propertyPath: GlobalObjectIdHash`), not as fields. `StampOnDisk` zeroes
  the first and reads both forms; the old check threw on every variant.
- **The builder's `AddComponent` runs `Awake` and `OnEnable` in the editor** (seen in the stack of `GearDoor.Awake`
  during Rebuild). The mission's `OnEnable` registered its handlers and baked a `NetChannel` onto the workshop root;
  its lifecycle now returns at once unless `Application.isPlaying`. The gate never did it only because its `Awake`
  throws first.
- **The camp is far outside the streamed area at the workshop**: the streamer loads one chunk round each player
  (`loadRadius` 1) and `Chunk_5_1` is two rows south of the workshop's. So nothing about the camp may be read off the
  scene object until a player is near it: the trail and visor use `GearThiefCampSite`, and the band waits. Chunk
  `Chunk_X_Y`'s terrain stands at (X*500, (Y-2)*500), not (X*500, Y*500): a site survey that assumes otherwise reads
  the wrong tile.
- **A clue swayed off the straight line can land in the mountain's foot beside the gate**: the first clue lies on the
  line, and sway grows with the clue's index.
- **The carrier's `FleeModule` must not trip on sight**, or he bolts the moment a sneaking player is 6 m off; its
  `triggerRadius` is 0.1 and the band's alarm is what starts him.
- **Every variant the builder configures has its stray `NetChannel` stripped** (`StripBakedChannels`): adding an
  `EntityInventoryComponent` in the editor ran its `OnEnable`, and `Raider_GearThief` had shipped with one.
- **The visor tracks the newest mission**, so the crash-site chain is hidden while the workshop's is open.
- **`Done` is never entered.** It names the end of the story in saves from before the vault and is read back as `Vault`;
  the end is `Repaired`. A value of the enum must never be renamed: saves store it by name.
- **The core is laid on the first think, so a fresh world's save is no longer null.** The saver writes `coreSpawned` from
  then on; were it skipped at the start, a reload would lay a second core beside the saved one.
- **The core is not a `ShipPartItem`**: only the hoist fits it, by design ([WorkshopCrane.md](WorkshopCrane.md)). It fills a
  `ReactorCore` socket, so it counts for the crash chain's `RepairShipStep`.
- **The story's carried things have an `UnderTerrainGuard`** (gate gears, Heart Gear, core: `CarriedGearItems`, added in place to prefabs made before it), so one that falls through the dunes is lifted back out. The Heart Gear went under when the last thief died: loot was born half in the ground (fixed in `PlayerDropService`, [Inventory.md](Inventory.md)).
- **Gatekeeper lines live on the prefab**: a new array takes its code default only while the prefab has no value for it, so the offer (`vaultOffer`) is its own array spoken after the prefab's `thanks`.
- **`mainWheel` was written into `Workshop.prefab` by hand on 2026-10-12** (the builder's value); without it the visor skips the hand-cog step. Verified then: compiled, 10 `WorkshopMissionTests` + 5 `WorkshopCraneTests` green; the run in and the marker **not seen in Play**.
- **Verified 2026-10-11:** compiled; Rebuild ran (unique network hashes, thief and carrier read back, flee priority 30);
  the camp placed in `Chunk_5_1` with 4 posts; after the bake the gate to camp walk is complete (757 m) and the five
  clues are each a complete walk from the last (44, then about 140 m apart). **Not run:** Play, a client, a save file and a reload; the clue model's lie in the sand is a guess. Vault chapter, 2026-10-11 later: Rebuild built the vault door, crane and core; 9 `WorkshopMissionTests` green (the vault comes back with its core laid, a pre-vault Done goes on to the vault). Not run in Play either.

## Extending

- **Lines:** edit the serialized arrays on `Shellback_Gatekeeper.prefab` (kept across Rebuild).
- **Tuning:** pacing on the mission (`meetRadius`, `walkInPatience`, `dismissAfterSeconds`, `homeRadius`), the band
  (`count`, `snatchReach`, `runFrom`), the trail (`alongTrail`, `wander`, `searchRadius`, `findings`) and the gathering
  row live on `Workshop/Mission`; change their defaults in code, since Rebuild makes a fresh component.
- **Another camp site:** change `GearThiefCampPlacer.Site`/`ChunkPath`, run it (it moves the camp and the site asset),
  re-bake.
- **Another mission in the world:** prefer a quest (skill `spacegame-objectives`); one with its own state machine like this implements `IQuestEntry` + `ITrackedObjective` and is `QuestLog.Open`ed while open.
